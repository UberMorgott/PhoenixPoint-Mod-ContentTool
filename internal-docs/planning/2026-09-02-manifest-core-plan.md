# Manifest core — implementation plan (`Manifest` + `ManifestFile` + `AtomicFile`)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ship `internal-docs\planning\2026-09-02-manifest-core-design.md`: one UnityEngine-free file that
reads `ppcontent.json` into a typed facade over a real JSON tree, adds one `replace` row, and writes it back
**without touching a byte the author wrote anywhere else**; plus the shared atomic-write helper; plus the
migration of the `replace` readers off the regex that cannot see a nested map.

**Architecture:** `Manifest` is a facade, not a model — it holds the tree `Json.Parse` returned and reads
through it, so unknown keys, key order, number spelling and nested values survive by construction.
`ManifestFile` owns the FILE: bytes, BOM, newline, a SHA-256 fingerprint and a string-aware scan of the ROOT
object's member spans; `Save` splices the serialized row into the `replace` value span, copies every other
byte verbatim, re-parses its own output, then commits through `AtomicFile`. Nothing reserializes the whole
tree, ever.

**Tech Stack:** C# 9 / net472, Mono inside Unity 2019. No new dependencies. Build `dotnet build -c Release`.
Offline gates are `tests\ObjCodecTests` and `tests\TargetPathTests` (NOT `dotnet test`), each a
`static class X { internal static string Run() }` that throws on failure and is called from `Program.Main`.
Both `.csproj` set `EnableDefaultCompileItems=false`, so **every new file — test or linked src — must be added
to its `<Compile Include>` list**. `ContentTool.csproj` globs `src\**\*.cs` and needs no edit.

**Three corrections to the design's inputs, all forced by the build:**

1. **`Json`/`JsonWriter` DO need extracting, and FIRST.** Four projects link ContentTool source directly:
   `tests\TargetPathTests\TargetPathTests.csproj:62` and `tools\Package\Package.csproj:14` link
   `src\Project\Package.cs` alone and compile neither GLB file (so migrating `Package` onto the tree breaks
   both, and linking `GlbReader.cs` would drag in `Bake`, `Meshopt`, `Draco`, `MaterialCodec`,
   `SkinnedModel`); `tools\ClipEvents\ClipEvents.csproj:18-19` and
   `tools\SpiderAxisCheck\SpiderAxisCheck.csproj:18-19` compile BOTH `GlbReader.cs` and `GlbCodec.cs` and so
   lose the two classes the moment they move. **Task 1** moves `Json` (`GlbReader.cs:2306-2444`) and
   `JsonWriter` (`GlbCodec.cs:1221-1332`) into `src\Import\Json.cs`, verbatim, before anything needs it —
   so `Manifest.cs` is written once, in its final shape.
2. **`ContentProject.ParseReplace` does have an offline gate.** `tests\ObjCodecTests\RefusalCount.cs:39`
   `Assembly.LoadFrom`s the built `ContentTool.dll` and invokes `ParseReplace` by reflection (`:57-60`,
   `:110-117`); the body touches no UnityEngine type, so it runs. **Its name and `(string, List<string>)`
   signature are load-bearing.**
3. **`tools\ClipEvents` and `tools\SpiderAxisCheck` are ALREADY RED on `main`**, before this slice touches
   anything — MEASURED 2026-09-02, `dotnet build <csproj> -c Release` → **2 errors each**, both from the
   file they link: `GlbReader.cs(6,27): error CS0234` (`Morgott.ContentTool.Bake`, which neither tool
   compiles) and `GlbReader.cs(2300,44): error CS0246` (`ImportCode`, defined in
   `src\Import\ImportRefused.cs:11`, which neither tool links). `tools\Package` builds clean today
   (`0 Error(s)`, measured) and must still do so after Task 9. So the two GLB tools' acceptance is a COUNT,
   not `0 Error(s)`: Task 1 links `Json.cs` **and** `ImportRefused.cs` into both, which clears the second
   pre-existing error and leaves exactly ONE (`CS0234` on `Bake`). Repairing that one is out of scope —
   record it as a follow-up in the slice handoff. `ImportRefusedException` (`ImportRefused.cs:24`) is what
   `Json.Fail` throws, so any project linking `Json.cs` needs that file unless it already compiles it
   (`ObjCodecTests.csproj:175` does; `TargetPathTests` and `tools\Package` do not — Task 9 adds it).

## File Structure

**Created**

| Path | Responsibility |
|---|---|
| `src\Import\Json.cs` | **Task 1.** `Json` + `JsonWriter`, moved verbatim out of `GlbReader.cs`/`GlbCodec.cs`. |
| `src\IO\AtomicFile.cs` | `Write`/`WriteText`: a UNIQUE same-directory temp, `CreateNew`, `Flush(true)`, then `File.Replace` (or `File.Move` when new), with one `try/finally` that leaves no temp behind. |
| `src\Project\Manifest.cs` | `ReplaceRow`, `Manifest` (facade + validation + `AddMeshReplacement`), `ManifestFile` (bytes, BOM, newline, SHA, root span scan, splice `Save`). UnityEngine-free. |
| `tests\ObjCodecTests\ManifestTests.cs` | Design §8's arms bar the alias one; 46 checks. |

**Modified**

| Path | Change |
|---|---|
| `src\Import\GlbReader.cs`, `src\Import\GlbCodec.cs` | **Task 1.** The two JSON classes cut out. |
| `tools\ClipEvents\ClipEvents.csproj`, `tools\SpiderAxisCheck\SpiderAxisCheck.csproj` | **Task 1.** link `Json.cs` + `ImportRefused.cs` (they compile both GLB files; already red for an unrelated reason — correction 3). |
| `src\Project\ContentProject.cs:385-435` | `ParseReplace` body → the core. Signature, refusal sentences and `Field` (`:539`) untouched. |
| `src\Project\Package.cs:435-456` | `OwnBundle`/`ReplaceTargets` → `Manifest.Parse`; `Bundles` deleted; `Depth` (`:467`) kept for `:87`. |
| `tools\Package\Package.csproj` | **Task 9.** link `Json.cs`, `ImportRefused.cs`, `AtomicFile.cs`, `Manifest.cs` — it links `Package.cs` alone. |
| `tests\TargetPathTests\TargetPathTests.csproj:62` | **Task 9.** the same four links. |
| `src\Import\AliasMap.cs:176`, `:230`, `:245-257` | integral-`schema` refusal; commit onto `AtomicFile`. |
| `src\Dev\ModelDoctor.cs:516` | bare `File.WriteAllText` → `AtomicFile.WriteText`. |
| `tests\ObjCodecTests\ObjCodecTests.csproj`, `Program.cs:140` | link `Json.cs`, `AtomicFile.cs`, `Manifest.cs`, `ManifestTests.cs`; call `ManifestTests.Run()`. |
| `tests\ObjCodecTests\RefusalCount.cs:72` | four migration arms (nested map, `]` in a string, the null-list throw, the exact "declares" sentence). |
| `tests\ObjCodecTests\AliasTests.cs:143` | `AliasSidecar_SchemaMustBeIntegral`, 2 checks. |

**`SlimJob` is NOT modified** — its three atomic writes are design §6; `SlimJob.cs:90-116` is read in Task 2
only as the pattern being consolidated.

---

### Task 1: move `Json` and `JsonWriter` into `src\Import\Json.cs`

Nothing below can be written twice, so the move comes first: `Manifest.cs` is then authored once, against a
`Json` every project that needs it can already link.

- [x] **Step 1: Move, and link only `ObjCodecTests` — the red state is the two TOOL builds.** Cut
  `internal static class Json` (`src\Import\GlbReader.cs:2306-2444`, doc-comment included) and
  `internal sealed class JsonWriter` (`src\Import\GlbCodec.cs:1221-1332`, doc-comment included) into a new
  `src\Import\Json.cs`:
  ```csharp
  using System;
  using System.Collections.Generic;
  using System.Globalization;
  using System.Text;

  namespace Morgott.ContentTool.Import
  {
      // Json, moved verbatim from GlbReader.cs:2306-2444 - same name, same `internal static`, same body.
      // JsonWriter, moved verbatim from GlbCodec.cs:1221-1332 - same name, same `internal sealed`, same body.
  }
  ```
  Not one character of either class changes. In `tests\ObjCodecTests\ObjCodecTests.csproj`, before the
  `GlbCodec.cs` line (`:140`):
  ```xml
  <Compile Include="..\..\src\Import\Json.cs" Link="Json.cs" />
  ```
  - Run: `dotnet build -c Release`, then
    `dotnet build tools\ClipEvents\ClipEvents.csproj -c Release` and
    `dotnet build tools\SpiderAxisCheck\SpiderAxisCheck.csproj -c Release`
  - Expected: the mod and `ObjCodecTests` build clean (`0 Error(s)`), and **both tool builds go from 2
    errors to 4** — the two pre-existing ones (correction 3) plus
    `GlbReader.cs: error CS0103: The name 'Json' does not exist in the current context` and
    `GlbCodec.cs: error CS0246: The type or namespace name 'JsonWriter' could not be found (are you missing
    a using directive or an assembly reference?)`. That rise from 2 to 4 is this task's gate.

- [x] **Step 2: Link both files into both GLB tools.** In `tools\ClipEvents\ClipEvents.csproj` and
  `tools\SpiderAxisCheck\SpiderAxisCheck.csproj`, after their `GlbReader.cs` line (`:19` in each):
  ```xml
  <Compile Include="..\..\src\Import\ImportRefused.cs" Link="ImportRefused.cs" />
  <Compile Include="..\..\src\Import\Json.cs" Link="Json.cs" />
  ```
  `ImportRefused.cs` is not optional here: `Json.Fail` throws `ImportRefusedException` (`:24`) and names
  `ImportCode` (`:11`), and neither tool linked that file before — which is one of the two errors they
  already carry.
  - Run: `dotnet build -c Release`, `dotnet build tools\ClipEvents\ClipEvents.csproj -c Release`,
    `dotnet build tools\SpiderAxisCheck\SpiderAxisCheck.csproj -c Release`,
    `dotnet run --project tests\ObjCodecTests -c Release`
  - Expected: the mod builds `0 Error(s)` (the 1 known CS0649 warning stays); each GLB tool is back to
    **exactly ONE error**, `GlbReader.cs(6,27): error CS0234` on `Morgott.ContentTool.Bake` — the
    pre-existing breakage this slice does not own — and reports nothing about `Json` or `JsonWriter`; every
    existing gate line still PASS. `tools\Package` is untouched here: `Package.cs` does not use `Json` yet.

- [x] **Step 3: Commit.**
  - `git -C E:\DEV\PhoenixPoint\ContentTool add src\Import\Json.cs src\Import\GlbReader.cs src\Import\GlbCodec.cs tests\ObjCodecTests\ObjCodecTests.csproj tools\ClipEvents\ClipEvents.csproj tools\SpiderAxisCheck\SpiderAxisCheck.csproj && git -C E:\DEV\PhoenixPoint\ContentTool commit -m "refactor(import): Json and JsonWriter move to their own file, where every project that needs them can link it"`

---

### Task 2: `AtomicFile`, and the arms that prove the `.bak` and the temp

- [x] **Step 1: Write the failing gate.** Create `tests\ObjCodecTests\ManifestTests.cs`:
  ```csharp
  using System;
  using System.IO;
  using System.Text;
  using Morgott.ContentTool.IO;
  using Morgott.ContentTool.Project;

  /// <summary>The manifest core: read ppcontent.json into a facade over the REAL tree, add one "replace"
  /// row, write it back with every byte outside the "replace" value span untouched. Every arm is a case
  /// the regex at ContentProject.cs:388-392 gets wrong, or a way a user edit could be lost.</summary>
  internal static class ManifestTests
  {
      internal static string Run()
      {
          int checks = 0;
          string dir = Path.Combine(Path.GetTempPath(), "ct_manifest_" + Guid.NewGuid().ToString("N"));
          Directory.CreateDirectory(dir);
          try
          {
              // ---- AtomicFile_WriteLeavesBakAndNoTmp
              string f = Path.Combine(dir, "a.txt");
              AtomicFile.Write(f, new byte[] { 1, 2, 3 }, f + ".bak");
              checks += Check(File.Exists(f) && !File.Exists(f + ".bak"),
                              "a FIRST write leaves no .bak - File.Replace would throw, File.Move is what runs");
              checks += Check(Temps(dir).Length == 0, "and no temp survives it");
              AtomicFile.Write(f, new byte[] { 9 }, f + ".bak");
              byte[] bak = File.ReadAllBytes(f + ".bak");
              checks += Check(bak.Length == 3 && bak[0] == 1 && bak[2] == 3,
                              "an overwrite leaves the PRE-write bytes in .bak: " + bak.Length + " B");
              checks += Check(File.ReadAllBytes(f).Length == 1 && Temps(dir).Length == 0,
                              "the destination is the new bytes and no temp is left");

              // A ".tmp" a previous crash left behind must not block the next write, and must not be
              // adopted by it: the name is unique per write, so it is simply another file.
              File.WriteAllBytes(f + ".tmp", new byte[] { 7, 7 });
              AtomicFile.Write(f, new byte[] { 4, 5 }, f + ".bak");
              checks += Check(File.ReadAllBytes(f).Length == 2 && File.Exists(f + ".tmp"),
                              "a stale .tmp neither blocks the write nor is adopted by it");
              File.Delete(f + ".tmp");

              // A failure BEFORE the commit leaves no temp of its own behind.
              string wall = Path.Combine(dir, "wall.txt");
              Directory.CreateDirectory(wall);          // the destination cannot be a file
              bool blocked = false;
              try { AtomicFile.Write(wall, new byte[] { 1 }, null); }
              catch (Exception) { blocked = true; }
              checks += Check(blocked && Temps(dir).Length == 0,
                              "a write that cannot commit rethrows and takes its temp with it");
          }
          finally { try { Directory.Delete(dir, true); } catch (Exception) { } }
          return "MANIFEST PASS, " + checks + " check(s) - atomic write";
      }

      private static string[] Temps(string dir) { return Directory.GetFiles(dir, "*.tmp"); }

      private static int Check(bool condition, string what)
      {
          if (!condition) throw new Exception("MANIFEST FAILURE: " + what);
          return 1;
      }
  }
  ```
  Register it. `tests\ObjCodecTests\ObjCodecTests.csproj`, after the `AliasTests.cs` line (`:36`):
  ```xml
  <Compile Include="..\..\src\IO\AtomicFile.cs" Link="AtomicFile.cs" />
  <Compile Include="ManifestTests.cs" />
  ```
  `tests\ObjCodecTests\Program.cs`, after `Console.WriteLine(GlbSlimTests.Run());` (`:140`):
  ```csharp
  Console.WriteLine(ManifestTests.Run());
  ```
  - Run: `dotnet run --project tests\ObjCodecTests -c Release`
  - Expected: **FAIL to compile**, three errors that all say the same thing —
    `error CS2001: Source file '..\..\src\IO\AtomicFile.cs' could not be found`,
    `error CS0234: The type or namespace name 'IO' does not exist in the namespace 'Morgott.ContentTool'`
    on the `using`, and `error CS0103: The name 'AtomicFile' does not exist in the current context`.
    `using Morgott.ContentTool.Project;` does **not** error: that namespace is already in this assembly
    (`SourceImport.cs`, `Package.cs`, `ModGate.cs` are linked). A missing type is a compile error, and that
    counts as the failing gate.

- [x] **Step 2: Implement `AtomicFile`.** Create `src\IO\AtomicFile.cs`:
  ```csharp
  using System;
  using System.IO;
  using System.Text;

  namespace Morgott.ContentTool.IO
  {
      /// <summary>
      /// The tmp-then-swap write, in ONE place - AliasMap.SaveSidecar:245-257 consolidated, with its one
      /// real weakness fixed: the temp name is UNIQUE, so two writers cannot land on each other and a
      /// ".tmp" a crash left behind is just another file rather than a blocker. File.Replace REQUIRES an
      /// existing destination, which is why the two arms are not one call; a backupPath is honoured only
      /// on the replace arm, since a file being created has nothing to back up.
      /// Never write `IO.Something` from another namespace of this mod: it would bind here, not to System.IO.
      /// </summary>
      internal static class AtomicFile
      {
          internal static void Write(string path, byte[] bytes, string backupPath = null)
          {
              string tmp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
              try
              {
                  using (FileStream stream = new FileStream(tmp, FileMode.CreateNew, FileAccess.Write,
                                                            FileShare.None))
                  {
                      stream.Write(bytes, 0, bytes.Length);
                      // The swap is atomic, the CONTENT is not: without this the name can be in place
                      // while the bytes are still in the OS cache, and a power cut leaves an empty file
                      // under a name that says it is the author's manifest.
                      stream.Flush(true);
                  }
                  if (File.Exists(path)) File.Replace(tmp, path, backupPath);
                  else File.Move(tmp, path);
              }
              finally
              {
                  // A successful swap moved it away, so this is a no-op; any failure above - the open, the
                  // write, the flush or the commit - is cleaned up here. Best effort either way: the
                  // exception the caller needs is the one from the try, never one from this line.
                  try { File.Delete(tmp); } catch (Exception) { }
              }
          }

          /// <summary>The encoding's PREAMBLE is NOT written - a BOM belongs in the bytes overload, where
          /// it is explicit and the caller can see it. Both callers pass new UTF8Encoding(false).</summary>
          internal static void WriteText(string path, string text, Encoding encoding, string backupPath = null)
          {
              Write(path, (encoding ?? new UTF8Encoding(false)).GetBytes(text), backupPath);
          }
      }
  }
  ```
  - Run: `dotnet run --project tests\ObjCodecTests -c Release`
  - Expected: `MANIFEST PASS, 6 check(s) - atomic write` among all-green output.

- [x] **Step 3: Build.** Run `dotnet build -c Release` → `0 Error(s)` (the 1 known CS0649 warning stays).

- [x] **Step 4: Commit.**
  - `git -C E:\DEV\PhoenixPoint\ContentTool add src\IO\AtomicFile.cs tests\ObjCodecTests\ManifestTests.cs tests\ObjCodecTests\ObjCodecTests.csproj tests\ObjCodecTests\Program.cs && git -C E:\DEV\PhoenixPoint\ContentTool commit -m "feat(io): AtomicFile - one copy of the tmp-then-swap write, with a unique temp and the .bak the manifest saves need"`

---

### Task 3: `Manifest` + `ReplaceRow` (read only)

- [x] **Step 1: Write the failing gate.** In `ManifestTests.Run()`, inside the `try`, after the AtomicFile arm:
  ```csharp
  // ---- Manifest_LoadsKnownAndUnknownTree: the case "\{[^{}]*\}" cannot read at all.
  const string tree =
      "{\n  \"id\": \"m.demo\",\n  \"bundle\": \"M.bundle\",\n  \"scale\": 0.008,\n" +
      "  \"play\": \"Idle\",\n  \"loop\": \"Idle, Walk\",\n  \"replace\": [\n" +
      "    { \"bundle\": \"a.bundle\", \"asset\": \"Foo\", \"mesh\": \"body\", \"opts\": { \"x\": 1 } },\n" +
      "    { \"bundle\": \"b.bundle\", \"asset\": \"Bar]\", \"texture\": \"swatch\" }\n  ],\n" +
      "  \"creature\": { \"clips\": { \"Spider_Walk\": \"walk\" } },\n  \"somethingNew\": [ 1, 2, 3 ]\n}\n";
  Manifest read = Manifest.Parse(tree);
  checks += Check(read.Id == "m.demo" && read.Bundle == "M.bundle" && read.Play == "Idle" &&
                  read.Loop == "Idle, Walk" && read.Scale == 0.008,
                  "the root scalars arrive typed, scale as a double: " + read.Scale);
  checks += Check(read.Replace.Count == 2,
                  "BOTH rows read - a nested map and a ']' inside a string end neither: " + read.Replace.Count);
  checks += Check(read.Replace[0].Kind == "mesh" && read.Replace[0].Asset == "Foo" &&
                  read.Replace[1].Kind == "texture" && read.Replace[1].Asset == "Bar]",
                  "each row's kind and asset, the bracketed one included");
  checks += Check(read.Replace[0].Tree.ContainsKey("opts"),
                  "the unknown nested member of a row is RETAINED, not dropped");
  checks += Check(read.Root.ContainsKey("creature") && read.Root.ContainsKey("somethingNew"),
                  "unknown root keys survive - the tree is the file's, not a model of it");
  Manifest bare = Manifest.Parse("{ \"bundle\": \"M.bundle\" }");
  checks += Check(bare.Id == null && bare.Replace.Count == 0 && !bare.Declares("replace"),
                  "Parse is the TOLERANT entry: no id, no replace, no throw - Package holds text, not a path");
  ```
  - Run: `dotnet run --project tests\ObjCodecTests -c Release`
  - Expected: **FAIL to compile** — `error CS0246: The type or namespace name 'Manifest' could not be found
    (are you missing a using directive or an assembly reference?)`, repeated per use.

- [x] **Step 2: Implement.** Create `src\Project\Manifest.cs`:
  ```csharp
  using System;
  using System.Collections.Generic;
  using System.IO;
  using Morgott.ContentTool.Import;

  namespace Morgott.ContentTool.Project
  {
      /// <summary>One "replace" row, as a facade over the dictionary Json.Parse produced, so a member this
      /// version never heard of is still there when the file is written back. A known field whose value is
      /// NOT a string reads as ABSENT rather than throwing - exactly what ContentProject.Field:539 does with
      /// its "([^\"]*)" group: `"mesh": 5` is a row with no mesh, and the refusal that follows says so.</summary>
      internal sealed class ReplaceRow
      {
          /// <summary>The five kinds, in the order ContentProject.cs:404-408 counts them.</summary>
          internal static readonly string[] Kinds = { "texture", "material", "mesh", "clip", "video" };

          private static readonly string[] Known =
              { "bundle", "asset", "texture", "material", "mesh", "clip", "video" };

          private readonly Dictionary<string, object> row;

          internal ReplaceRow(Dictionary<string, object> row) { this.row = row; }

          /// <summary>The row's own tree, for the writer and for anything reading an unknown member.</summary>
          internal Dictionary<string, object> Tree => row;

          internal string Bundle => Str("bundle");
          internal string Asset => Str("asset");
          internal string Texture => Str("texture");
          internal string Material => Str("material");
          internal string Mesh => Str("mesh");
          internal string Clip => Str("clip");
          internal string Video => Str("video");

          /// <summary>texture|material|mesh|clip|video - NULL when the row selects none or several, which is
          /// half of the refusal at ContentProject.cs:404-416.</summary>
          internal string Kind
          {
              get
              {
                  string found = null;
                  foreach (string kind in Kinds)
                  {
                      if (string.IsNullOrEmpty(Str(kind))) continue;
                      if (found != null) return null;
                      found = kind;
                  }
                  return found;
              }
          }

          /// <summary>V6. Tolerated on the READ side (a non-string reads as absent, as today), refused on
          /// the WRITE side: a row nothing can read is not one this tool hands back as if it were one.
          /// JSON null counts - `"mesh": null` is a mesh the file DECLARES and no reader can use.</summary>
          internal bool HasNonStringField()
          {
              foreach (string key in Known)
              {
                  object value;
                  if (row.TryGetValue(key, out value) && !(value is string)) return true;
              }
              return false;
          }

          private string Str(string key)
          {
              object value;
              return row.TryGetValue(key, out value) ? value as string : null;
          }
      }

      /// <summary>A typed facade over a PARSED ppcontent.json tree. Not a model of the file: the Dictionary
      /// and List Json.Parse returned ARE the state, so unknown keys, key order and number spelling survive
      /// whatever this class does. Parse is the tolerant entry (Package holds text, not a path, and may be
      /// handed a manifest with no "id"); ManifestFile.Load is the strict one.</summary>
      internal sealed class Manifest
      {
          /// <summary>Same cap ppcontent.json's other readers use. A manifest 64 levels deep is not one.</summary>
          internal const int MaxDepth = 64;

          /// <summary>V3. The design's E-table gives it no id, so this is the plan's own sentence.</summary>
          internal const string NotAnArray =
              "ppcontent.json's \"replace\" must be an ARRAY OF ROWS - a value of any other shape declares " +
              "nothing this tool can read or write";

          private readonly Dictionary<string, object> root;
          private readonly List<ReplaceRow> rows = new List<ReplaceRow>();
          private readonly List<ReplaceRow> pending = new List<ReplaceRow>();

          private Manifest(Dictionary<string, object> root)
          {
              this.root = root;
              object value;
              if (!root.TryGetValue("replace", out value) || value == null) return;
              List<object> array = value as List<object>;
              if (array == null) throw new InvalidDataException(NotAnArray);
              foreach (object item in array)
              {
                  Dictionary<string, object> members = item as Dictionary<string, object>;
                  if (members == null) throw new InvalidDataException(NotAnArray);
                  rows.Add(new ReplaceRow(members));
              }
          }

          internal static Manifest Parse(string text) { return ParseFor(text, "ppcontent.json"); }

          /// <summary>E1. Json.Fail throws an ImportRefusedException worded for a GLB re-export
          /// (Json.cs, moved from GlbReader.cs:2440), so both entry points catch FormatException and rethrow
          /// the one exception a manifest caller can act on.</summary>
          /// <param name="what">"ppcontent.json", or "'&lt;path&gt;'" from ManifestFile.Load.</param>
          internal static Manifest ParseFor(string text, string what)
          {
              object parsed;
              try { parsed = Json.Parse(text, MaxDepth); }
              catch (FormatException bad)
              {
                  throw new InvalidDataException(what + " is not valid JSON: " + bad.Message, bad);
              }
              Dictionary<string, object> tree = parsed as Dictionary<string, object>;
              if (tree == null)
                  throw new InvalidDataException(what + " is not valid JSON: its root is not an object");
              return new Manifest(tree);
          }

          internal string Id => Str("id");
          internal string Bundle => Str("bundle");
          internal string Loop => Str("loop");
          internal string Play => Str("play");

          internal double? Scale
          {
              get
              {
                  object value;
                  return root.TryGetValue("scale", out value) && value is double number ? (double?)number : null;
              }
          }

          /// <summary>The raw tree, kept for round-trip. Callers READ it; the file's own bytes are what
          /// ManifestFile writes, never a reserialization of this.</summary>
          internal IDictionary<string, object> Root => root;

          /// <summary>Existing rows plus anything AddMeshReplacement queued.</summary>
          internal IReadOnlyList<ReplaceRow> Replace => rows;

          /// <summary>Rows added in memory and not yet spliced into the file.</summary>
          internal IReadOnlyList<ReplaceRow> Pending => pending;

          /// <summary>Whether the file SAYS the key, as opposed to saying it empty - the distinction
          /// ParseReplace's "declares but no complete entry" sentence turns on.</summary>
          internal bool Declares(string key) { return root.ContainsKey(key); }

          private string Str(string key)
          {
              object value;
              return root.TryGetValue(key, out value) ? value as string : null;
          }
      }
  }
  ```
  In `ObjCodecTests.csproj`, before the `ManifestTests.cs` line:
  ```xml
  <Compile Include="..\..\src\Project\Manifest.cs" Link="Manifest.cs" />
  ```
  - Run: `dotnet run --project tests\ObjCodecTests -c Release`
  - Expected: `MANIFEST PASS, 12 check(s) - atomic write` (the sentence is renamed in Task 11).

- [x] **Step 3: Build.** `dotnet build -c Release` → `0 Error(s)`.

- [x] **Step 4: Commit.**
  - `git -C E:\DEV\PhoenixPoint\ContentTool add src\Project\Manifest.cs tests\ObjCodecTests\ManifestTests.cs tests\ObjCodecTests\ObjCodecTests.csproj && git -C E:\DEV\PhoenixPoint\ContentTool commit -m "feat(project): Manifest reads ppcontent.json as a facade over the real tree, nested rows included"`

---

### Task 4: `ManifestFile.Load` — bytes, strict UTF-8, BOM, newline, fingerprint, root spans

- [x] **Step 1: Write the failing gate.** Append inside the `try`:
  ```csharp
  // ---- ManifestFile.Load, the strict boundary
  string path = Path.Combine(dir, "ppcontent.json");
  File.WriteAllBytes(path, Bytes(true, Crlf));
  ManifestFile file = ManifestFile.Load(path);
  checks += Check(file.Manifest.Id == "m.demo" && file.Manifest.Replace.Count == 1 && file.Path == path,
                  "a BOM + CRLF file loads, and the facade came with it");
  checks += Check(file.Manifest.Replace[0].Kind == "texture", "its one row is a texture row");

  // ---- Manifest_RefusesMalformedWithoutWriting
  string broken = Path.Combine(dir, "broken.json");
  byte[] before = Bytes(false, "{ \"id\": \"m\", \"bundle\": \"M.bundle\", \"replace\": [ {");
  File.WriteAllBytes(broken, before);
  string why = null;
  try { ManifestFile.Load(broken); }
  catch (InvalidDataException bad) { why = bad.Message; }
  checks += Check(why != null && why.IndexOf(broken, StringComparison.Ordinal) >= 0 &&
                  why.IndexOf("is not valid JSON", StringComparison.Ordinal) >= 0,
                  "truncated JSON is refused and the sentence NAMES THE PATH: " + why);
  checks += Check(Same(File.ReadAllBytes(broken), before) &&
                  Temps(dir).Length == 0 && !File.Exists(broken + ".bak"),
                  "and the original bytes are untouched, with no temp and no .bak beside them");
  string headless = Path.Combine(dir, "headless.json");
  File.WriteAllBytes(headless, Bytes(false, "{ \"bundle\": \"M.bundle\" }"));
  why = null;
  try { ManifestFile.Load(headless); }
  catch (InvalidDataException bad) { why = bad.Message; }
  checks += Check(why == "ppcontent.json needs both \"id\" and \"bundle\"",
                  "E2 is the sentence ContentProject.cs:289 already says, word for word: " + why);
  checks += Check(Manifest.Parse("{ \"bundle\": \"M.bundle\" }").Bundle == "M.bundle",
                  "and Manifest.Parse does NOT apply that rule - only the file boundary does");

  // V1, the STRICT decode: a byte that is not UTF-8 must refuse, not decode to U+FFFD and get written back.
  string mangled = Path.Combine(dir, "mangled.json");
  byte[] raw = Bytes(false, "{ \"id\": \"m\", \"bundle\": \"M.bundle\", \"note\": \"XX\" }");
  raw[raw.Length - 5] = 0xFF;                  // the second 'X': 0xFF is not a UTF-8 byte anywhere
  File.WriteAllBytes(mangled, raw);
  why = null;
  try { ManifestFile.Load(mangled); }
  catch (InvalidDataException bad) { why = bad.Message; }
  checks += Check(why != null && why.IndexOf("is not valid JSON", StringComparison.Ordinal) >= 0,
                  "a byte that is not UTF-8 is REFUSED, not silently turned into U+FFFD: " + why);

  // V9: root keys are DECODED before they are compared, so an escaped spelling cannot smuggle in a
  // second "replace" that the tree and the span scanner would then disagree about.
  string twinned = Path.Combine(dir, "twinned.json");
  File.WriteAllBytes(twinned, Bytes(false,
      "{ \"id\": \"m\", \"bundle\": \"M.bundle\", \"replace\": [], \"\\u0072eplace\": [] }"));
  why = null;
  try { ManifestFile.Load(twinned); }
  catch (InvalidDataException bad) { why = bad.Message; }
  checks += Check(why != null && why.IndexOf("\"replace\" twice", StringComparison.Ordinal) >= 0,
                  "an escaped root key decodes, so it is caught as a SECOND \"replace\" (E8): " + why);
  string quoted = Path.Combine(dir, "quoted.json");
  File.WriteAllBytes(quoted, Bytes(false,
      "{ \"id\": \"m\", \"bundle\": \"M.bundle\", \"pa\\\"th\": \"x\", \"replace\": [] }"));
  checks += Check(ManifestFile.Load(quoted).Manifest.Root.ContainsKey("pa\"th"),
                  "and an escaped quote inside a KEY neither ends the key nor collides with anything");
  ```
  and beside `Check`, the fixtures — spelled as bytes so no checkout can normalise the line endings:
  ```csharp
  /// <summary>BOM + CRLF fixture. One "replace" row, exactly one '[' and one ']' in the whole text,
  /// pure ASCII - so a byte marker can be located independently in the before and after files.</summary>
  private const string Crlf =
      "{\r\n  \"id\": \"m.demo\",\r\n  \"bundle\": \"M.bundle\",\r\n  \"replace\": [\r\n" +
      "    { \"bundle\": \"a.bundle\", \"asset\": \"Foo\", \"texture\": \"swatch\" }\r\n" +
      "  ],\r\n  \"creature\": { \"name\": \"Spider\" }\r\n}\r\n";

  private static byte[] Bytes(bool bom, string text)
  {
      byte[] body = new UTF8Encoding(false).GetBytes(text);
      if (!bom) return body;
      byte[] all = new byte[body.Length + 3];
      all[0] = 0xEF; all[1] = 0xBB; all[2] = 0xBF;
      Buffer.BlockCopy(body, 0, all, 3, body.Length);
      return all;
  }

  private static bool Same(byte[] a, byte[] b)
  {
      if (a.Length != b.Length) return false;
      for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
      return true;
  }
  ```
  - Run: `dotnet run --project tests\ObjCodecTests -c Release`
  - Expected: **FAIL to compile** — `error CS0246: The type or namespace name 'ManifestFile' could not be
    found (are you missing a using directive or an assembly reference?)`.

- [x] **Step 2: Implement.** Append to `src\Project\Manifest.cs` inside the namespace, adding
  `using System.Security.Cryptography;`, `using System.Text;`, `using Morgott.ContentTool.IO;` at the top:
  ```csharp
      /// <summary>
      /// The FILE behind a Manifest: raw bytes, BOM, newline style, a SHA-256 of what was read, and the
      /// [start, end) span of every ROOT member's value. Save splices into ONE span and copies every other
      /// byte verbatim, so a whole-tree reserialization - which would lose the BOM, the indentation, the key
      /// order, the number spelling and every unknown key, Dictionary insertion order not being contractual
      /// (GlbDocument.cs:22) - never happens.
      /// SAVE ONCE, THEN RELOAD: after a successful Save the file no longer matches the fingerprint this
      /// instance holds, so a second Save refuses with E5 by construction.
      /// </summary>
      internal sealed class ManifestFile
      {
          /// <summary>[Start, End) of one root member's VALUE, trailing whitespace excluded - Save needs the
          /// exact index of the array's closing ']'.</summary>
          private sealed class Span { internal int Start; internal int End; }

          private readonly string text;
          private readonly string sha;
          private readonly bool bom;
          private readonly string newline;
          private readonly Dictionary<string, Span> members;
          private readonly int rootClose;

          private ManifestFile(string path, string text, string sha, bool bom, string newline,
                               Dictionary<string, Span> members, int rootClose, Manifest manifest)
          {
              Path = path; this.text = text; this.sha = sha; this.bom = bom; this.newline = newline;
              this.members = members; this.rootClose = rootClose; Manifest = manifest;
          }

          internal string Path { get; }
          internal Manifest Manifest { get; }

          /// <exception cref="InvalidDataException">E1 (not UTF-8, not JSON, or a root that is not an
          /// object), E2 (no "id"/"bundle") or E8 (two root keys that decode alike). Nothing is written on
          /// any path through this method.</exception>
          internal static ManifestFile Load(string path)
          {
              byte[] bytes = File.ReadAllBytes(path);
              bool bom = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
              int from = bom ? 3 : 0;
              string text;
              try
              {
                  // THROW ON INVALID, not replace: a permissive decode turns a byte this reader does not
                  // understand into U+FFFD, and Save would then write that replacement character back over
                  // whatever the author actually had there.
                  text = new UTF8Encoding(false, true).GetString(bytes, from, bytes.Length - from);
              }
              catch (DecoderFallbackException bad)
              {
                  throw new InvalidDataException("'" + path + "' is not valid JSON: " + bad.Message, bad);
              }

              Manifest manifest = Manifest.ParseFor(text, "'" + path + "'");
              // E2, the sentence ContentProject.cs:289 and :305 already say.
              if (string.IsNullOrEmpty(manifest.Id) || string.IsNullOrEmpty(manifest.Bundle))
                  throw new InvalidDataException("ppcontent.json needs both \"id\" and \"bundle\"");

              int close;
              Dictionary<string, Span> members = Members(text, path, out close);
              string newline = text.IndexOf("\r\n", StringComparison.Ordinal) >= 0 ? "\r\n" : "\n";
              return new ManifestFile(path, text, Sha256(bytes), bom, newline, members, close, manifest);
          }

          internal static string Sha256(byte[] bytes)
          {
              using (SHA256 hash = SHA256.Create())
              {
                  var spelled = new StringBuilder(64);
                  foreach (byte b in hash.ComputeHash(bytes)) spelled.Append(b.ToString("x2"));
                  return spelled.ToString();
              }
          }

          /// <summary>ONE forward pass over the ROOT object: at depth 1 record each key and the [start, end)
          /// of its value; deeper, only keep the counter honest. A '{', '[' or ']' inside a STRING never
          /// moves it - that is CreatureManifest.Block:407's weakness, fixed rather than reused. The text has
          /// already been through Json.Parse, so this pass never has to refuse malformed JSON - only V9.</summary>
          private static Dictionary<string, Span> Members(string text, string path, out int close)
          {
              var spans = new Dictionary<string, Span>(StringComparer.Ordinal);
              int depth = 0, valueStart = -1;
              string key = null;
              close = -1;
              for (int i = 0; i < text.Length; i++)
              {
                  char c = text[i];
                  if (c == '}' || c == ']')
                  {
                      depth--;
                      if (depth != 0) continue;
                      if (key != null && valueStart >= 0)
                          Record(spans, key, valueStart, Trim(text, valueStart, i), path);
                      close = i;
                      return spans;
                  }
                  if (c == '{' || c == '[') { depth++; continue; }
                  if (c == '"')
                  {
                      int quote = i;
                      i = EndOfString(text, i);
                      if (depth == 1 && key == null) key = Key(text, quote, i);
                      continue;
                  }
                  if (depth != 1) continue;
                  if (c == ':' && key != null && valueStart < 0)
                  {
                      valueStart = i + 1;
                      while (valueStart < text.Length && IsSpace(text[valueStart])) valueStart++;
                      continue;
                  }
                  if (c == ',' && key != null && valueStart >= 0)
                  {
                      Record(spans, key, valueStart, Trim(text, valueStart, i), path);
                      key = null;
                      valueStart = -1;
                  }
              }
              return spans;
          }

          /// <summary>The DECODED key. The scanner sees a LITERAL; the tree Json.Parse built holds the
          /// decoded name, and if the two disagree a key spelled with an escape becomes an invisible second
          /// member. Handing the literal - quotes included - back to Json.Parse means exactly one decoder
          /// decides what a key spells.</summary>
          private static string Key(string text, int openQuote, int closeQuote)
          {
              return (string)Json.Parse(text.Substring(openQuote, closeQuote - openQuote + 1), 1);
          }

          /// <summary>V9/E8. Two root keys that DECODE to one name cannot both be edited safely: the tree
          /// keeps one of them and the splice would land in the other one's span.</summary>
          private static void Record(Dictionary<string, Span> spans, string key, int start, int end,
                                     string path)
          {
              if (spans.ContainsKey(key))
                  throw new InvalidDataException("'" + path + "' declares the root key \"" + key +
                                                 "\" twice, so it cannot be edited safely - delete one of them");
              spans[key] = new Span { Start = start, End = end };
          }

          /// <summary>Index of the quote that CLOSES the string opening at <paramref name="at"/>.</summary>
          private static int EndOfString(string text, int at)
          {
              for (int i = at + 1; i < text.Length; i++)
              {
                  if (text[i] == '\\') { i++; continue; }
                  if (text[i] == '"') return i;
              }
              return text.Length - 1;
          }

          private static bool IsSpace(char c) { return c == ' ' || c == '\t' || c == '\r' || c == '\n'; }

          private static int Trim(string text, int from, int to)
          {
              while (to > from && IsSpace(text[to - 1])) to--;
              return to;
          }
      }
  ```
  - Run: `dotnet run --project tests\ObjCodecTests -c Release`
  - Expected: `MANIFEST PASS, 21 check(s) - atomic write`.

- [x] **Step 3: Build.** `dotnet build -c Release` → `0 Error(s)`.

- [x] **Step 4: Commit.**
  - `git -C E:\DEV\PhoenixPoint\ContentTool add src\Project\Manifest.cs tests\ObjCodecTests\ManifestTests.cs && git -C E:\DEV\PhoenixPoint\ContentTool commit -m "feat(project): ManifestFile.Load keeps the bytes, the BOM, the newline and a string-aware span of every root member"`

---

### Task 5: validation — V4, V5, V6, V7 and their exact refusals

- [x] **Step 1: Write the failing gate.** Append inside the `try`:
  ```csharp
  // ---- Manifest_RefusesInvalidReplaceRows: V4, V5, V6, each with E3's wording.
  // NOT named `bad`: Task 4's `catch (InvalidDataException bad)` blocks sit in a nested scope of this
  // same try, and a later outer local of that name is CS0136.
  string[] rejects =
  {
      "{ \"bundle\": \"a.bundle\", \"asset\": \"Foo\" }",
      "{ \"bundle\": \"a.bundle\", \"asset\": \"Foo\", \"mesh\": \"m\", \"clip\": \"c\" }",
      "{ \"bundle\": \"a.bundle\", \"mesh\": \"m\" }",
      "{ \"bundle\": \"a.bundle\", \"asset\": \"Foo\", \"mesh\": { \"file\": \"m\" } }",
      "{ \"bundle\": \"a.bundle\", \"asset\": \"Foo\", \"mesh\": \"m\", \"clip\": null }"
  };
  foreach (string reject in rejects)
  {
      Manifest one = Manifest.Parse(
          "{ \"id\": \"m\", \"bundle\": \"M.bundle\", \"replace\": [ " + reject + " ] }");
      string said = null;
      try { one.Validate(); }
      catch (InvalidDataException refused) { said = refused.Message; }
      checks += Check(said != null &&
                      said.StartsWith("\"replace\" row REFUSED: every entry needs exactly one of",
                                      StringComparison.Ordinal) &&
                      said.EndsWith("- SKIPPED, this project's other rows still bake", StringComparison.Ordinal),
                      "E3 verbatim for " + reject + " -> " + said);
  }

  // ---- Manifest_RefusesDuplicateMeshTarget: V7, bundle case-blind, asset verbatim.
  Manifest twice = Manifest.Parse(
      "{ \"id\": \"m\", \"bundle\": \"M.bundle\", \"replace\": [" +
      " { \"bundle\": \"a.bundle\", \"asset\": \"Foo\", \"mesh\": \"one\" }," +
      " { \"bundle\": \"A.BUNDLE\", \"asset\": \"Foo\", \"mesh\": \"two\" } ] }");
  string dup = null;
  try { twice.Validate(); }
  catch (InvalidDataException refused) { dup = refused.Message; }
  checks += Check(dup == "ppcontent.json already replaces \"Foo\" in \"A.BUNDLE\" with a mesh, so a second " +
                         "row for the same target was NOT written - edit the existing row instead",
                  "E4 names the asset, the bundle and the kind: " + dup);
  Manifest apart = Manifest.Parse(
      "{ \"id\": \"m\", \"bundle\": \"M.bundle\", \"replace\": [" +
      " { \"bundle\": \"a.bundle\", \"asset\": \"Foo\", \"mesh\": \"one\" }," +
      " { \"bundle\": \"a.bundle\", \"asset\": \"foo\", \"texture\": \"two\" } ] }");
  apart.Validate();
  checks += Check(true, "a different asset CASE and a different kind are different targets - assets fold nowhere");
  ```
  - Run: `dotnet run --project tests\ObjCodecTests -c Release`
  - Expected: **FAIL to compile** — `error CS1061: 'Manifest' does not contain a definition for 'Validate'
    and no accessible extension method 'Validate' accepting a first argument of type 'Manifest' could be
    found`.

- [x] **Step 2: Implement.** Add to `Manifest`:
  ```csharp
          /// <summary>V4/V5/V6/V7 over every row, existing and pending, before a byte moves. V4 and V5 are
          /// today's rule at ContentProject.cs:404-416 unchanged; V6 is new only because the read side can
          /// afford to treat `"mesh": 5` as an absent mesh and the WRITE side cannot hand the author back a
          /// file whose row nothing can read.</summary>
          /// <exception cref="InvalidDataException">E3 for a row, E4 for a duplicated target.</exception>
          internal void Validate()
          {
              var seen = new List<string>();
              foreach (ReplaceRow row in rows)
              {
                  string kind = row.Kind;
                  bool needsBundle = kind != "video";
                  if (kind == null || row.HasNonStringField() ||
                      (needsBundle && (string.IsNullOrEmpty(row.Bundle) || string.IsNullOrEmpty(row.Asset))))
                      throw new InvalidDataException(RowRefusal(row));

                  // A "video" row with no "asset" ADDS a clip rather than replacing one, so two of them are
                  // two additions, not a collision. Only a NAMED target can be claimed twice.
                  if (string.IsNullOrEmpty(row.Asset) || string.IsNullOrEmpty(row.Bundle)) continue;
                  // Lowercased rather than compared with OrdinalIgnoreCase because List<string>.Contains has
                  // no comparer overload; the fold is the one ProjectBake.cs:1534 uses for bundles.
                  string key = row.Bundle.ToLowerInvariant() + "\u0000" + row.Asset + "\u0000" + kind;
                  if (seen.Contains(key))
                      throw new InvalidDataException(
                          "ppcontent.json already replaces \"" + row.Asset + "\" in \"" + row.Bundle +
                          "\" with a " + kind + ", so a second row for the same target was NOT written - " +
                          "edit the existing row instead");
                  seen.Add(key);
              }
          }

          /// <summary>E3, the SENTENCE verbatim from ContentProject.cs:419-422. The row inside it is spelled
          /// by JsonWriter from the PARSED row rather than by a raw regex match, so its spacing and key order
          /// may differ from the file's - and a nested member shows up in the sentence at all, which the old
          /// "\{[^{}]*\}" match could never manage (design §7).</summary>
          internal static string RowRefusal(ReplaceRow row)
          {
              return "\"replace\" row REFUSED: every entry needs exactly one of \"texture\", \"material\", " +
                     "\"mesh\", \"clip\" or \"video\", plus \"bundle\" and \"asset\" for everything but " +
                     "\"video\" (a \"video\" entry with no \"asset\" ADDS a new clip); got " +
                     new JsonWriter().Val(row.Tree).ToString() +
                     " - SKIPPED, this project's other rows still bake";
          }
  ```
  - Run: `dotnet run --project tests\ObjCodecTests -c Release`
  - Expected: `MANIFEST PASS, 28 check(s) - atomic write`.

- [x] **Step 3: Build.** `dotnet build -c Release` → `0 Error(s)`.

- [x] **Step 4: Commit.**
  - `git -C E:\DEV\PhoenixPoint\ContentTool add src\Project\Manifest.cs tests\ObjCodecTests\ManifestTests.cs && git -C E:\DEV\PhoenixPoint\ContentTool commit -m "feat(project): Manifest.Validate refuses a row or a duplicate target with ContentProject's own wording"`

---

### Task 6: `AddMeshReplacement` and the splice — the byte-preservation gate

- [x] **Step 1: Write the failing gate.** Append inside the `try`:
  ```csharp
  // ---- Manifest_AppendsMeshWithoutCollateralRewrite: the whole point of the slice.
  string add = Path.Combine(dir, "add.json");
  byte[] originalBytes = Bytes(true, Crlf);
  File.WriteAllBytes(add, originalBytes);
  ManifestFile target = ManifestFile.Load(add);
  target.Manifest.AddMeshReplacement("b.bundle", "Torso", "torso");
  target.Save();
  byte[] afterBytes = File.ReadAllBytes(add);
  // The markers are located INDEPENDENTLY in each file, so nothing here assumes the two agree on any
  // offset. The fixture holds exactly one '[' and one ']', both belonging to "replace".
  int openWas = IndexOf(originalBytes, (byte)'['), openNow = IndexOf(afterBytes, (byte)'[');
  int closeWas = LastIndexOf(originalBytes, (byte)']'), closeNow = LastIndexOf(afterBytes, (byte)']');
  checks += Check(openWas == openNow && Same(Head(originalBytes, openWas), Head(afterBytes, openNow)),
                  "every byte BEFORE the array's '[' is identical, BOM included");
  checks += Check(Same(Tail(originalBytes, originalBytes.Length - closeWas),
                       Tail(afterBytes, afterBytes.Length - closeNow)),
                  "and every byte from its ']' on - the \"creature\" block was not rewritten");
  byte[] wasRow = new UTF8Encoding(false).GetBytes(
      "{ \"bundle\": \"a.bundle\", \"asset\": \"Foo\", \"texture\": \"swatch\" }");
  checks += Check(afterBytes.Length > originalBytes.Length && Holds(afterBytes, wasRow),
                  "the row that was already there survives BYTE FOR BYTE - nothing reserialized it");
  string afterText = new UTF8Encoding(false).GetString(afterBytes, 3, afterBytes.Length - 3);
  checks += Check(afterText.Replace("\r\n", "").IndexOf('\n') < 0, "no bare LF was introduced - still CRLF");
  checks += Check(afterText.IndexOf("}\r\n  ]", StringComparison.Ordinal) >= 0,
                  "the author's whitespace before the ']' was COPIED, not regenerated as \"}]\"");
  ManifestFile reread = ManifestFile.Load(add);
  checks += Check(reread.Manifest.Replace.Count == 2 && reread.Manifest.Replace[1].Mesh == "torso" &&
                  reread.Manifest.Replace[1].Asset == "Torso" && reread.Manifest.Replace[1].Kind == "mesh",
                  "the added row reads back as exactly one mesh row");
  checks += Check(reread.Manifest.Root.ContainsKey("creature") && reread.Manifest.Replace[0].Texture == "swatch",
                  "and the row that was already there, plus the creature block, are what they were");

  // ---- Manifest_InsertsMissingReplaceArray (demos\CustomCreature\ppcontent.json has no "replace" at all)
  string none = Path.Combine(dir, "none.json");
  File.WriteAllBytes(none, Bytes(false,
      "{\n  \"id\": \"m.demo\",\n  \"bundle\": \"M.bundle\",\n  \"creature\": { \"name\": \"Spider\" }\n}\n"));
  ManifestFile fresh = ManifestFile.Load(none);
  fresh.Manifest.AddMeshReplacement("a.bundle", "Foo", "body");
  fresh.Save();
  ManifestFile grown = ManifestFile.Load(none);
  checks += Check(grown.Manifest.Replace.Count == 1 && grown.Manifest.Replace[0].Kind == "mesh",
                  "a manifest with no \"replace\" gets one holding exactly one valid row");
  string grownText = File.ReadAllText(none);
  checks += Check(grownText.IndexOf("\"creature\"", StringComparison.Ordinal) <
                  grownText.IndexOf("\"replace\"", StringComparison.Ordinal),
                  "added as the LAST root member, so nothing the author wrote moved");
  checks += Check(grownText.StartsWith("{\n  \"id\": \"m.demo\",\n  \"bundle\": \"M.bundle\",",
                                       StringComparison.Ordinal),
                  "and the head of the file is byte-for-byte what it was");

  // No final newline in, NONE out: "...]}" is the ACCEPTED output. This tool inserts, it never reformats.
  string tight = Path.Combine(dir, "tight.json");
  File.WriteAllBytes(tight, Bytes(false, "{\"id\":\"m\",\"bundle\":\"M.bundle\"}"));
  ManifestFile squeezed = ManifestFile.Load(tight);
  squeezed.Manifest.AddMeshReplacement("a.bundle", "Foo", "body");
  squeezed.Save();
  string tightText = File.ReadAllText(tight);
  checks += Check(tightText.EndsWith("]}", StringComparison.Ordinal) &&
                  ManifestFile.Load(tight).Manifest.Replace.Count == 1,
                  "a file with no final newline ends \"]}\" and still re-reads: " + tightText);

  // An INLINE empty array is the whitespace-only branch: a body appears between the brackets.
  string inline = Path.Combine(dir, "inline.json");
  File.WriteAllBytes(inline, Bytes(false,
      "{\n  \"id\": \"m\",\n  \"bundle\": \"M.bundle\",\n  \"replace\": [],\n  \"tail\": 1\n}\n"));
  ManifestFile flat = ManifestFile.Load(inline);
  flat.Manifest.AddMeshReplacement("a.bundle", "Foo", "body");
  flat.Save();
  string inlineText = File.ReadAllText(inline);
  checks += Check(ManifestFile.Load(inline).Manifest.Replace.Count == 1,
                  "an inline \"[]\" takes the row: " + inlineText);
  checks += Check(inlineText.StartsWith("{\n  \"id\": \"m\",\n  \"bundle\": \"M.bundle\",",
                                        StringComparison.Ordinal) &&
                  inlineText.EndsWith(",\n  \"tail\": 1\n}\n", StringComparison.Ordinal),
                  "and everything on either side of it is untouched");

  // The scanner's hard cases in ONE file: an escaped quote in a value, a '{' and a '[' inside a string,
  // and a NON-ASCII character before the span, so a character index is not a byte index.
  const string hard =
      "{\n  \"id\": \"m\",\n  \"bundle\": \"M.bundle\",\n  \"note\": \"caf\u00e9 { [ \\\" ]\",\n" +
      "  \"replace\": [\n    { \"bundle\": \"a.bundle\", \"asset\": \"Fo\\\"o\", \"mesh\": \"m\" }\n  ],\n" +
      "  \"tail\": \"]\"\n}\n";
  string tricky = Path.Combine(dir, "tricky.json");
  File.WriteAllBytes(tricky, Bytes(false, hard));
  ManifestFile odd = ManifestFile.Load(tricky);
  checks += Check(odd.Manifest.Replace.Count == 1 && odd.Manifest.Replace[0].Asset == "Fo\"o",
                  "a '{', a '[' and an escaped quote inside STRINGS move neither the depth nor the span");
  odd.Manifest.AddMeshReplacement("b.bundle", "Bar", "bar");
  odd.Save();
  byte[] trickyNow = File.ReadAllBytes(tricky);
  UTF8Encoding utf8 = new UTF8Encoding(false);
  byte[] headWas = utf8.GetBytes(hard.Substring(0, hard.IndexOf("\"replace\"", StringComparison.Ordinal)));
  byte[] tailWas = utf8.GetBytes(hard.Substring(hard.IndexOf("  ],\n", StringComparison.Ordinal)));
  checks += Check(Holds(trickyNow, headWas) && Holds(trickyNow, tailWas) &&
                  Holds(trickyNow, utf8.GetBytes("\"Fo\\\"o\"")),
                  "everything before and after the array - the two-byte 'e-acute' included - is byte-identical");
  ```
  plus, beside the other helpers:
  ```csharp
  private static byte[] Head(byte[] all, int count)
  {
      byte[] part = new byte[count];
      Buffer.BlockCopy(all, 0, part, 0, count);
      return part;
  }

  private static byte[] Tail(byte[] all, int count)
  {
      byte[] part = new byte[count];
      Buffer.BlockCopy(all, all.Length - count, part, 0, count);
      return part;
  }

  private static int IndexOf(byte[] all, byte b)
  {
      for (int i = 0; i < all.Length; i++) if (all[i] == b) return i;
      return -1;
  }

  private static int LastIndexOf(byte[] all, byte b)
  {
      for (int i = all.Length - 1; i >= 0; i--) if (all[i] == b) return i;
      return -1;
  }

  /// <summary>Whether <paramref name="needle"/> appears in <paramref name="hay"/> unbroken.</summary>
  private static bool Holds(byte[] hay, byte[] needle)
  {
      for (int i = 0; i + needle.Length <= hay.Length; i++)
      {
          int k = 0;
          while (k < needle.Length && hay[i + k] == needle[k]) k++;
          if (k == needle.Length) return true;
      }
      return false;
  }
  ```
  - Run: `dotnet run --project tests\ObjCodecTests -c Release`
  - Expected: **FAIL to compile** — `error CS1061: 'Manifest' does not contain a definition for
    'AddMeshReplacement'` and `error CS1061: 'ManifestFile' does not contain a definition for 'Save'`.

- [x] **Step 2: Implement `AddMeshReplacement`.** Add to `Manifest`:
  ```csharp
          /// <summary>Queue ONE mesh row. Add only - editing or removing a row is design §2, and the wizard
          /// needs neither. The row is a flat object of three string members, so the in-game JsonUtility read
          /// of the root scalars (ContentProject.cs:287, :303) is unaffected. "asset" goes on VERBATIM:
          /// shipped names are folded nowhere.</summary>
          internal ReplaceRow AddMeshReplacement(string bundle, string asset, string meshFile)
          {
              var tree = new Dictionary<string, object>(StringComparer.Ordinal)
              {
                  { "bundle", bundle }, { "asset", asset }, { "mesh", meshFile }
              };
              var added = new ReplaceRow(tree);
              rows.Add(added);
              pending.Add(added);
              return added;
          }
  ```

- [x] **Step 3: Implement `Save` and the splice.** Add to `ManifestFile`:
  ```csharp
          /// <summary>Splice every pending row into the "replace" value span and commit. Everything outside
          /// that span - a nested map inside an existing row included - is byte-identical by construction.</summary>
          /// <exception cref="InvalidDataException">E3/E4 from Validate, or E6 when what this method produced
          /// does not re-read. Nothing is written on either path.</exception>
          /// <exception cref="IOException">E5, the file changed on disk since Load.</exception>
          internal void Save()
          {
              Manifest.Validate();
              if (Manifest.Pending.Count == 0) return;
              string produced = Splice();

              // E6: re-read what is about to be written, through the same reader and the same rules.
              try { Manifest.ParseFor(produced, "'" + Path + "'").Validate(); }
              catch (Exception)
              {
                  throw new InvalidDataException("the edited ppcontent.json did not re-read as valid JSON, " +
                                                 "so the file on disk was NOT touched");
              }

              // E5, immediately before the commit: the last moment a concurrent edit is still recoverable by
              // the author simply reloading.
              if (!string.Equals(Sha256(File.ReadAllBytes(Path)), sha, StringComparison.Ordinal))
                  throw new IOException("'" + Path + "' changed on disk since it was loaded, so nothing was " +
                                        "written - reload it and add the row again");

              // Encoding back is lossless without a strict encoder: every char in `text` came out of the
              // STRICT decode in Load, and the spliced row is JsonWriter output.
              byte[] body = new UTF8Encoding(false).GetBytes(produced);
              byte[] bytes = body;
              if (bom)
              {
                  bytes = new byte[body.Length + 3];
                  bytes[0] = 0xEF; bytes[1] = 0xBB; bytes[2] = 0xBF;
                  Buffer.BlockCopy(body, 0, bytes, 3, body.Length);
              }
              AtomicFile.Write(Path, bytes, Path + ".bak");
          }

          private string Splice()
          {
              var added = new StringBuilder();
              foreach (ReplaceRow row in Manifest.Pending)
              {
                  if (added.Length > 0) added.Append(',').Append(newline);
                  added.Append(new JsonWriter().Val(row.Tree).ToString());
              }

              Span span;
              if (!members.TryGetValue("replace", out span))
              {
                  // (c) no "replace" at all: as the LAST root member, inserted just past the last thing the
                  // author wrote, so the comma lands on THEIR line rather than on one of its own. A file
                  // with no final newline therefore ends "...]}" - accepted as written.
                  int at = Trim(text, 0, rootClose);
                  return text.Substring(0, at) + "," + newline + "  \"replace\": [" + newline + "    " +
                         added.ToString().Replace(newline, newline + "    ") + newline + "  ]" +
                         text.Substring(at);
              }

              int stop;
              int last = LastElement(text, span.Start, span.End, out stop);
              if (last < 0)
              {
                  // (b) the array is empty or holds only whitespace: give it a body, one level in from
                  // "replace" itself.
                  string close = IndentOf(text, span.Start);
                  string inner = close + "  ";
                  return text.Substring(0, span.Start + 1) + newline + inner +
                         added.ToString().Replace(newline, newline + inner) + newline + close +
                         text.Substring(span.End - 1);
              }

              // (a) insert immediately AFTER the last existing row's last byte, indented exactly like that
              // row. Everything from there on - the author's own whitespace and the closing ']' - is copied
              // unchanged rather than regenerated.
              string indent = IndentOf(text, last);
              return text.Substring(0, stop) + "," + newline + indent +
                     added.ToString().Replace(newline, newline + indent) + text.Substring(stop);
          }

          /// <summary>Where the LAST element of the array spanning [start, end) begins, or -1 when it holds
          /// none; <paramref name="stop"/> comes back as the index just PAST that element. Walks the array's
          /// INTERIOR only, so the outer brackets need no special case, and is string-aware for the same
          /// reason Members is.</summary>
          private static int LastElement(string text, int start, int end, out int stop)
          {
              int last = -1, from = -1, depth = 0;
              stop = -1;
              for (int i = start + 1; i < end - 1; i++)
              {
                  char c = text[i];
                  if (c == '"')
                  {
                      if (depth == 0 && from < 0) from = i;
                      i = EndOfString(text, i);
                      continue;
                  }
                  if (c == '{' || c == '[')
                  {
                      if (depth == 0 && from < 0) from = i;
                      depth++;
                      continue;
                  }
                  if (c == '}' || c == ']') { depth--; continue; }
                  if (depth == 0 && c == ',')
                  {
                      if (from >= 0) { last = from; stop = Trim(text, from, i); }
                      from = -1;
                      continue;
                  }
                  if (depth == 0 && from < 0 && !IsSpace(c)) from = i;
              }
              if (from >= 0) { last = from; stop = Trim(text, from, end - 1); }
              return last;
          }

          /// <summary>The whitespace run opening the line <paramref name="at"/> sits on.</summary>
          private static string IndentOf(string text, int at)
          {
              int line = text.LastIndexOf('\n', Math.Max(0, at - 1));
              var indent = new StringBuilder();
              for (int i = line + 1; i < at && IsSpace(text[i]); i++) indent.Append(text[i]);
              return indent.ToString();
          }
  ```
  - Run: `dotnet run --project tests\ObjCodecTests -c Release`
  - Expected: `MANIFEST PASS, 43 check(s) - atomic write`.

- [x] **Step 4: Build.** `dotnet build -c Release` → `0 Error(s)`.

- [x] **Step 5: Commit.**
  - `git -C E:\DEV\PhoenixPoint\ContentTool add src\Project\Manifest.cs tests\ObjCodecTests\ManifestTests.cs && git -C E:\DEV\PhoenixPoint\ContentTool commit -m "feat(project): ManifestFile.Save splices one replace row and leaves every other byte alone"`

---

### Task 7: the concurrent-edit guard, proved

The guard landed inside `Save` in Task 6; this task proves it and proves what is left on disk.

- [x] **Step 1: Write the gate.** Append inside the `try`:
  ```csharp
  // ---- Manifest_RefusesConcurrentEdit: V8/E5. The author's own edit wins, always.
  string race = Path.Combine(dir, "race.json");
  File.WriteAllBytes(race, Bytes(false, Crlf.Replace("\r\n", "\n")));
  ManifestFile held = ManifestFile.Load(race);
  byte[] external = Bytes(false, Crlf.Replace("\r\n", "\n").Replace("\"swatch\"", "\"swatch2\""));
  File.WriteAllBytes(race, external);
  held.Manifest.AddMeshReplacement("b.bundle", "Torso", "torso");
  string raced = null;
  try { held.Save(); }
  catch (IOException clash) { raced = clash.Message; }
  checks += Check(raced == "'" + race + "' changed on disk since it was loaded, so nothing was written - " +
                           "reload it and add the row again",
                  "E5 verbatim, and it names the path: " + raced);
  checks += Check(Same(File.ReadAllBytes(race), external),
                  "the EXTERNAL bytes are what remains - the tool did not overwrite a live edit");
  checks += Check(Temps(dir).Length == 0 && !File.Exists(race + ".bak"),
                  "the refusal happened before AtomicFile ran, so there is neither a temp nor a .bak");
  ```
  - Run: `dotnet run --project tests\ObjCodecTests -c Release`
  - Expected: if `Save` was written exactly as Task 6 spells it, this arm passes on the first run and the gate
    reads `MANIFEST PASS, 46 check(s) - atomic write`. Any other result is a real failure — most likely
    `MANIFEST FAILURE: the refusal happened before AtomicFile ran, ...`, meaning the SHA check sits in the
    wrong place.

- [x] **Step 2: If it failed, fix `Save`'s ORDER only.** The SHA check must sit after the E6 re-read and
  before `AtomicFile.Write`, with no write of any kind ahead of it. No other change.
  - Run: `dotnet run --project tests\ObjCodecTests -c Release`
  - Expected: `MANIFEST PASS, 46 check(s) - atomic write`.

- [x] **Step 3: Commit.**
  - `git -C E:\DEV\PhoenixPoint\ContentTool add src\Project\Manifest.cs tests\ObjCodecTests\ManifestTests.cs && git -C E:\DEV\PhoenixPoint\ContentTool commit -m "test(project): a manifest edited on disk between Load and Save refuses with E5 and keeps the author's bytes"`

---

### Task 8: `ContentProject.ParseReplace` onto the core

The defect the slice exists for. **Name and `(string, List<string>)` signature are load-bearing** —
`tests\ObjCodecTests\RefusalCount.cs:57` invokes them by reflection off the built DLL, and that is the only
gate this migration has.

- [x] **Step 1: Write the failing gate — behavioural, not compile-red.** In
  `tests\ObjCodecTests\RefusalCount.cs`, after the "each refusal names WHICH array" check (`:69-72`) and
  BEFORE the `// ---- 1:` section, so the later `refusals.Count == 5` arm still holds — none of these four
  arms adds to the list:
  ```csharp
        // ---- the TREE reader, on the shipped DLL: three shapes the regex read wrong, and the sentence.
        checks += Check(Rows("ParseReplace",
            "{\"replace\":[{\"bundle\":\"a.bundle\",\"asset\":\"Foo\",\"mesh\":\"body\"," +
            "\"opts\":{\"x\":1}},{\"bundle\":\"b.bundle\",\"asset\":\"Bar]\",\"texture\":\"t\"}]}",
            refusals) == 2 && refusals.Count == 4,
            "a NESTED map in a row and a ']' inside a string leave BOTH rows readable, and refuse neither");
        checks += Check(Threw("ParseReplace", "{\"replace\":[{\"bundle\":\"a.bundle\"}]}"),
            "an incomplete row with no list to collect into still THROWS, exactly as before");
        checks += Check(Said("ParseReplace", "{\"replace\":[]}") ==
                        "ppcontent.json declares \"replace\" but no complete entry was read from it",
            "and a declared-but-empty array throws THAT sentence, word for word");
        checks += Check((Said("ParseReplace", "{\"replace\":[1]}") ?? "")
                            .IndexOf("ARRAY OF ROWS", StringComparison.Ordinal) >= 0,
            "a \"replace\" holding a primitive is a manifest this cannot read - with no list it THROWS, " +
            "it does not report an empty project");
  ```
  and beside `Threw`:
  ```csharp
    /// <summary>The sentence the parser THREW with no list to collect into, or null if it did not.</summary>
    private static string Said(string method, string json)
    {
        try { Call(method, json, null); return null; }
        catch (InvalidDataException refused) { return refused.Message; }
    }
  ```
  - Run: `dotnet build -c Release` then `dotnet run --project tests\ObjCodecTests -c Release`
  - Expected: **FAIL** at `REFUSAL-COUNT FAILURE: a NESTED map in a row and a ']' inside a string leave BOTH
    rows readable, and refuse neither`. Today the array regex stops at the `]` inside `"Bar]"` and the row
    regex matches only the inner `{"x":1}`, so the call returns 0 rows. The fourth arm fails too once the
    first is fixed: today a primitive row throws the "declares" sentence instead.

- [x] **Step 2: Replace the regex body.** In `src\Project\ContentProject.cs`, replace `:385-435` (the whole
  `ParseReplace` body; the doc-comment at `:372-384` stays, minus its "reads the three flat string fields
  directly" sentence, which is no longer true):
  ```csharp
          private static List<ShippedReplacement> ParseReplace(string json, List<string> refusals = null)
          {
              List<ShippedReplacement> list = new List<ShippedReplacement>();
              Manifest manifest;
              try { manifest = Manifest.Parse(json); }
              catch (InvalidDataException bad)
              {
                  // Today's tolerance kept where there is a channel to say so - and ONLY there. Swallowing
                  // this into an empty list would report a manifest nothing can read as a project that
                  // replaces nothing, which is exactly the "refusal nobody counts" this gate exists for.
                  if (refusals == null) throw;
                  refusals.Add(bad.Message);
                  return list;
              }
              if (!manifest.Declares("replace")) return list;
              int marked = refusals == null ? 0 : refusals.Count;

              foreach (ReplaceRow row in manifest.Replace)
              {
                  // Exactly the rule at :404-416, asked of the parsed row: "bundle" and "asset" are required
                  // for every kind that LIVES in a bundle, and video does not - a video entry with no
                  // "asset" names no shipped clip because it ADDS one. A non-string field is NOT refused
                  // here: the read side has always treated it as absent, and the refusal that follows from
                  // the missing kind already says so.
                  string kind = row.Kind;
                  bool needsBundle = kind != "video";
                  if (kind == null ||
                      (needsBundle && (string.IsNullOrEmpty(row.Bundle) || string.IsNullOrEmpty(row.Asset))))
                  {
                      string why = Manifest.RowRefusal(row);
                      if (refusals == null) throw new InvalidDataException(why);
                      refusals.Add(why); continue;
                  }
                  list.Add(new ShippedReplacement
                  {
                      bundle = row.Bundle, asset = row.Asset, texture = row.Texture,
                      material = row.Material, mesh = row.Mesh, clip = row.Clip, video = row.Video
                  });
              }
              if (list.Count == 0 && (refusals == null || refusals.Count == marked))
              {
                  string why = "ppcontent.json declares \"replace\" but no complete entry was read from it";
                  if (refusals == null) throw new InvalidDataException(why);
                  refusals.Add(why);
              }
              return list;
          }
  ```
  `Field` (`:539-542`) stays — `ParseSounds:469` and `ParsePublish:508-511` still call it, and both are §6.
  - Run: `dotnet build -c Release`
  - Expected: `0 Error(s)` with the 1 known CS0649 warning.

- [x] **Step 3: Run the gate that actually exercises it.** `RefusalCount` reflects on the DLL just built, so
  step 2's build is its input.
  - Run: `dotnet run --project tests\ObjCodecTests -c Release`
  - Expected: `REFUSAL-COUNT PASS, N check(s) - 5 refusals, 5 failures` with N four higher than before — the
    original `refusals.Count == 2` arm still holds (one incomplete row, one refusal), and the final
    `refusals.Count == 5` arm is untouched because none of the four new arms collects. `MANIFEST PASS,
    46 check(s)` still green.
  - Deferred, not closed here: **M7**, the in-game read path. `ContentProject.cs:7` imports `UnityEngine`, so
    `Load`/`LoadDeclared` run only inside the game — Task 12 closes it.

- [x] **Step 4: Commit.**
  - `git -C E:\DEV\PhoenixPoint\ContentTool add src\Project\ContentProject.cs tests\ObjCodecTests\RefusalCount.cs && git -C E:\DEV\PhoenixPoint\ContentTool commit -m "fix(project): ParseReplace reads the parsed tree, so a nested map in a row no longer breaks the row"`

---

### Task 9: `Package` onto the core, and the two projects that link `Package.cs` alone

- [x] **Step 1: Migrate `Package`.** In `src\Project\Package.cs`, replace `OwnBundle` (`:435-440`) and
  `ReplaceTargets` (`:444-451`), and DELETE `Bundles` (`:453-456`):
  ```csharp
          /// <summary>The mod's OWN bundle: the "bundle" property of the ROOT object, as opposed to the ones
          /// nested inside "replace" entries (the shipped targets). Read from the parsed tree, so property
          /// ORDER cannot change the answer (S14-order-blind) and a "bundle" key inside any other nested
          /// block is mistaken for neither.</summary>
          internal static string OwnBundle(string manifestText)
          {
              try { return Manifest.Parse(manifestText).Bundle; }
              catch (InvalidDataException) { return null; }
          }

          /// <summary>The SHIPPED bundles the project declares as replacement targets - named in the refusal,
          /// so an author who dropped one in sees why that file is the problem. A manifest that will not
          /// PARSE declares no target here; Package.cs:87 is a coarser gate that refuses only a manifest
          /// whose braces and brackets do not close, not one that fails to parse.</summary>
          internal static List<string> ReplaceTargets(string manifestText)
          {
              List<string> targets = new List<string>();
              try
              {
                  foreach (ReplaceRow row in Manifest.Parse(manifestText).Replace)
                      if (!string.IsNullOrEmpty(row.Bundle) && !targets.Contains(row.Bundle))
                          targets.Add(row.Bundle);
              }
              catch (InvalidDataException) { }
              return targets;
          }
  ```
  `Depth` (`:467-484`) stays: `Package.cs:87`'s balanced-brace refusal is its only remaining caller and runs
  before anything parses. `Package.Run` is NOT changed — only the comment above tells the truth now.
  - Run: `dotnet build -c Release` then `dotnet run --project tests\TargetPathTests -c Release`
  - Expected: the mod builds, and the path gate **FAILS to compile** —
    `error CS0246: The type or namespace name 'Manifest' could not be found` and the same for `ReplaceRow`:
    `TargetPathTests.csproj:62` links `Package.cs` and nothing the new code needs.

- [x] **Step 2: Link the four files into both projects that compile `Package.cs` alone.** In
  `tests\TargetPathTests\TargetPathTests.csproj`, after the `Package.cs` line (`:62`):
  ```xml
  <Compile Include="..\..\src\Import\ImportRefused.cs" Link="ImportRefused.cs" />
  <Compile Include="..\..\src\Import\Json.cs" Link="Json.cs" />
  <Compile Include="..\..\src\IO\AtomicFile.cs" Link="AtomicFile.cs" />
  <Compile Include="..\..\src\Project\Manifest.cs" Link="Manifest.cs" />
  ```
  and the identical four lines in `tools\Package\Package.csproj`, after its `Package.cs` line (`:14`).
  `ImportRefused.cs` is `src\Import\ImportRefused.cs`, which defines both `ImportRefusedException` (`:24`)
  and `ImportCode` (`:11`) — `Json.Fail` throws the first and names the second.
  - Run: `dotnet run --project tests\TargetPathTests -c Release` then
    `dotnet build tools\Package\Package.csproj -c Release`
  - Expected: last line `R0: ALL PASS`, exit 0 — with `S14-ownbundle`, `S14-order-blind` and
    `S14-order-packages` among the `PASS` lines; and `0 Error(s)` from the packager tool.

- [x] **Step 3: Build and re-run the other gate.**
  - Run: `dotnet build -c Release` then `dotnet run --project tests\ObjCodecTests -c Release`
  - Expected: `0 Error(s)`; `PACKAGE-GATE PASS ...` and `MANIFEST PASS, 46 check(s)` green.

- [x] **Step 4: Commit.**
  - `git -C E:\DEV\PhoenixPoint\ContentTool add src\Project\Package.cs tests\TargetPathTests\TargetPathTests.csproj tools\Package\Package.csproj && git -C E:\DEV\PhoenixPoint\ContentTool commit -m "refactor(project): Package reads its bundles off the parsed manifest instead of a depth heuristic"`

---

### Task 10: `AliasMap` and `ModelDoctor` onto `AtomicFile`, and the integral-schema fix

- [x] **Step 1: Write the failing gate.** In `tests\ObjCodecTests\AliasTests.cs`, after the empty-`bones` arm
  (`:138-143`):
  ```csharp
  // ---- AliasSidecar_SchemaMustBeIntegral: "1.5" used to cast to 1 and LOAD, so a sidecar written for a
  // schema this mod has never seen applied itself as if it were schema 1.
  File.WriteAllText(AliasMap.SidecarPathOf(glb),
                    "{\"schema\":1.5,\"source\":{\"sha256\":\"" + sha + "\"},\"bones\":{\"A\":\"Root\"}}");
  checks += Check(AliasMap.LoadSidecar(glb, sha, out why) == null && why != null &&
                  why.IndexOf("1.5", StringComparison.Ordinal) >= 0,
                  "a non-integral schema is refused and the sentence spells it as written: " + why);
  File.WriteAllText(AliasMap.SidecarPathOf(glb),
                    "{\"schema\":1,\"source\":{\"sha256\":\"" + sha + "\"},\"bones\":{\"A\":\"Root\"}}");
  checks += Check(AliasMap.LoadSidecar(glb, sha, out why) != null, "and the real schema still loads");
  ```
  - Run: `dotnet run --project tests\ObjCodecTests -c Release`
  - Expected: `ALIAS FAILURE: a non-integral schema is refused and the sentence spells it as written: `
    followed by nothing — `1.5` loads clean today.

- [x] **Step 2: Fix the comparison and move the commit.** In `src\Import\AliasMap.cs` replace `:176-183`
  with:
  ```csharp
                  // (int) alone accepted 1.5 as 1, so a sidecar written for a schema this mod has never
                  // seen applied itself. Compared as a DOUBLE and spelled with "R", both for the same
                  // reason: no integer cast can be trusted here - it turns 1.5 into 1 and a huge value
                  // into a wrapped one, and the refusal for 1.5 would then read "declares schema 1 but
                  // this mod reads 1".
                  if (declared != Math.Floor(declared) || declared != Schema)
                  {
                      why = "'" + path + "' declares schema " +
                            declared.ToString("R", CultureInfo.InvariantCulture) +
                            " but this mod reads " + Schema.ToString(CultureInfo.InvariantCulture) +
                            ", so its aliases were NOT applied";
                      problem = SidecarProblem.Invalid;
                      return null;
                  }
  ```
  Replace `:245-257` (the `File.WriteAllText` plus its try/catch swap) with the single line
  `AtomicFile.WriteText(path, sb.ToString(), new UTF8Encoding(false));`, delete the now-unused
  `string tmp = path + ".tmp";` at `:230`, and add `using Morgott.ContentTool.IO;`. `SaveSidecar` keeps its
  API and its hand-built text; only the commit moved. In `src\Dev\ModelDoctor.cs:516` replace
  `File.WriteAllText(path, plan.ToJson());` with
  `AtomicFile.WriteText(path, plan.ToJson(), new UTF8Encoding(false));`, adding `using System.Text;` and
  `using Morgott.ContentTool.IO;` if either is absent.
  - Run: `dotnet run --project tests\ObjCodecTests -c Release`
  - Expected: `ALIAS PASS, N check(s) - ...` with N two higher than before; the existing "an unknown schema
    ... names the number" arm (`:87-91`, schema `99`) still green — `"R"` spells an integral 99 as `99`; and
    "a write that fails rethrows and takes its .tmp with it" (`:150-153`) still green — it is now proving
    `AtomicFile`'s `finally`, and it asserts the absence of `<sidecar>.tmp`, a name `AtomicFile` never
    creates, so it passes for a second reason as well.
  - `ModelDoctor` is a `Dev` panel that no offline gate reaches; its only gate here is the build. Its in-game
    check ("Write skel plan" still produces `<glb>.skel.json`) belongs to Task 12.

- [x] **Step 3: Build.** `dotnet build -c Release` → `0 Error(s)`.

- [x] **Step 4: Commit.**
  - `git -C E:\DEV\PhoenixPoint\ContentTool add src\Import\AliasMap.cs src\Dev\ModelDoctor.cs tests\ObjCodecTests\AliasTests.cs && git -C E:\DEV\PhoenixPoint\ContentTool commit -m "fix(import): a non-integral sidecar schema is refused, and two writers move onto AtomicFile"`

---

### Task 11: the offline gates, and the acceptance table walked

- [x] **Step 1: Name the arm honestly.** In `ManifestTests.Run()`, change the return to:
  ```csharp
  return "MANIFEST PASS, " + checks + " check(s) - atomic write, nested rows, byte-preserving splice, " +
         "E3/E4/E5/E6/E8 refusals";
  ```
  - Run: `dotnet run --project tests\ObjCodecTests -c Release`
  - Expected: `MANIFEST PASS, 46 check(s) - atomic write, nested rows, byte-preserving splice, E3/E4/E5/E6/E8 refusals`.

- [x] **Step 2: Every build and both gates, from clean.**
  - Run: `dotnet build -c Release` → `0 Error(s)`, 1 CS0649 warning.
  - Run: `dotnet build tools\Package\Package.csproj -c Release` → `0 Error(s)`; and
    `dotnet build tools\ClipEvents\ClipEvents.csproj -c Release`,
    `dotnet build tools\SpiderAxisCheck\SpiderAxisCheck.csproj -c Release` → exactly ONE error each,
    `GlbReader.cs(6,27): error CS0234` on `Morgott.ContentTool.Bake`, and nothing about `Json`,
    `JsonWriter`, `ImportCode` or `Manifest` (**M1**; correction 3 — those two were red before this slice,
    with 2 errors each, and leave it with 1).
  - Run: `dotnet run --project tests\ObjCodecTests -c Release` → every line PASS, exit 0 (**M2**).
  - Run: `dotnet run --project tests\TargetPathTests -c Release` → last line `R0: ALL PASS`, exit 0 (**M3**).

- [x] **Step 3: Record the offline half of design §9.**
  - **M4** byte preservation on the BOM + CRLF fixture: `Manifest_AppendsMeshWithoutCollateralRewrite` (the
    `[` and `]` markers located independently in the before and after bytes, the old row asserted present as
    an unbroken run) + `Manifest_LoadsKnownAndUnknownTree` (the nested map read at all) + the `tricky.json`
    fixture (non-ASCII before the span, `{`/`[`/escaped quote inside strings).
  - **M5** no user edit can be lost: `Manifest_RefusesConcurrentEdit` + `AtomicFile_WriteLeavesBakAndNoTmp`,
    including the stale-temp and failed-commit arms.
  - **M6** refusal wording unchanged: E3 string-compared head and tail in Task 5; the `declares "replace"`
    sentence compared in full in Task 8's `Said("ParseReplace", "{\"replace\":[]}")` arm.
  - **M7** and **M8** are NOT closed here. They are Task 12, and they are a REQUIRED ship gate — not an open
    item to hand off.

- [x] **Step 4: Commit.**
  - `git -C E:\DEV\PhoenixPoint\ContentTool add tests\ObjCodecTests\ManifestTests.cs && git -C E:\DEV\PhoenixPoint\ContentTool commit -m "test(project): name what the manifest gate proves, and close the offline half of the acceptance table"`

---

### Task 12: in-game acceptance on `D:\PP-Instance3` via PPCLI (**M7 + M8**)

The only proof against Unity's own reader. `ContentProject.cs:7` imports `UnityEngine`, so `Load` /
`LoadDeclared` — and the `JsonUtility` reads at `:287` and `:303` that must keep working over a file this
tool wrote — run nowhere but inside the game. **Do not mark the slice done before this task is green.**

**Command source: `E:\DEV\PhoenixPoint\PPCLI\PLAYBOOK.md`.** Read it and take the exact invocations from
there; this plan deliberately spells none, because a stale command line in a plan is worse than no command
line. `D:\PP-Instance3` is the automation install — never the user's own game.

- [x] **Step 1: Build and deploy.** `dotnet build -c Release`, then install the built
  `bin\Release\ContentTool\ContentTool.dll` + `meta.json` into `D:\PP-Instance3`'s mods folder and confirm
  the mod id is activated in that profile's `MOD_ACTIVATED` array. A deploy that silently leaves the old DLL
  makes every result below a ghost.

- [x] **Step 2: Launch and drive.** Cold-launch that install through PPCLI, wait until the bridge actually
  answers before sending anything, then run these checks:
  - **the migrated reader** — open the `demos\CustomCreature` project and bake it; the replacement it ships
    is applied exactly as before the migration (same bundle, same asset, same mesh swap visible).
  - **the writer's output, read by the game** — add one mesh row to that project's `ppcontent.json` with
    `Manifest.AddMeshReplacement` + `ManifestFile.Save` (`demos\CustomCreature\ppcontent.json` has no
    `replace` key, so this exercises the insert-the-array branch on a REAL authored file), re-open the
    project in the tool and bake again: the new row is read, the mod still activates, and the root scalars
    `id`/`bundle`/`scale` still arrive through `JsonUtility`.
  - **no new exception** — `Player.log` gains none across both bakes.
  - **the Doctor's writer** — "Write skel plan" still produces `<glb>.skel.json` (Task 10 moved it onto
    `AtomicFile`).
  - **M8, the owner's own eyes** — the owner diffs the hand-edited `ppcontent.json` before and after the
    tool wrote to it and confirms the change is ONE hunk: the added row and nothing else.

- [x] **Step 3: Record the evidence, then commit.** Fill this table in, in this file, with what the run
  actually produced — a screenshot path, a log excerpt, the diff hunk. An empty cell means the slice is not
  done.

  | id | Check | Evidence |
  |---|---|---|
  | M7a | `demos\CustomCreature` bakes, replacement applied as before the migration | **PASS**, on `demos\WeaponMesh` instead — `CustomCreature` ships NO `replace` key, so it proves nothing about the migrated reader; `WeaponMesh` ships six rows. `connect console '{"command":"ct_project","args":["WeaponMesh"]}'` → `project 'morgott.demo.weaponmesh' … 6 replacement(s)`, then one `patch px_equipment_assets_all.bundle:` line per row (mesh `WPN_PX_RG_Assault_Rifle_T01_V01 <- rifle verts=5554 indices=18582` + the five textures), `P1 PASS`, `P4 PASS`, `P1-ctl-shipped PASS`, `ct_project: ALL PASS` |
  | M7b | a tool-WRITTEN row bakes, mod activates, root scalars still read by `JsonUtility` | **PASS** — scratch project `D:\PP-Instance3\Mods\CtM8` (a byte-copy of `demos\CustomCreature`) after the splice: `project 'morgott.demo.customcreature' at D:\PP-Instance3\Mods\CtM8: … 1 replacement(s)`, `patch px_equipment_assets_all.bundle: mesh 'WPN_PX_RG_Assault_Rifle_T01_V01' <- cyborg_spider`, `P4 PASS mesh … IS cyborg_spider`, `ct_project: ALL PASS`. The `JsonUtility` scalars all still arrive: `clip-names PASS "loop" names 2 clip(s) and "play" names 1 of the 7`, and `creature-measure … "scale": 0.008 … (this project declares 0.008)` |
  | M7c | `Player.log` gains no new exception across both bakes | **PASS** — 3542 new lines after the mark, `Exception` matches only: 3× `TFTV REPORTED AN EXCEPTION` in `TFTV.TFTVRevenant+Resistance.GetPreferredDamageType` / `PrespawnChecks.CheckForNotDeadSoldiers` (another mod, tactical code, nothing to do with this path) and 2× `ArgumentException: Mesh can not have more than 65000 vertices` at `UnityEngine.UI.VertexHelper.FillMesh ← UI.Text.UpdateGeometry ← CanvasUpdateRegistry.PerformUpdate` — the game's console **Text widget** choking on the bake's own long output, not the manifest path. **Zero** exceptions naming `Morgott`/`ContentTool` |
  | M7d | Doctor "Write skel plan" still writes `<glb>.skel.json` | **NOT ATTEMPTED.** `DoWriteSkelPlan` (`src\Dev\ModelDoctor.cs:507`) is reachable only from the IMGUI button at `:1260`, and it early-returns unless a Doctor instance already holds a `Path`, a `Ready` report and a **non-empty** alias map — i.e. an author has opened the bench, picked a `.glb` and typed a rename. `connect call` cannot press an IMGUI button, and the process was mid-tactical-mission (no squad bay), so `ct_bench open` would have refused anyway. Task 10's `AtomicFile.WriteText` swap on this line is covered offline by `MANIFEST PASS, 53` |
  | M8 | owner: the diff of the hand-edited manifest is ONE hunk | **PASS.** Row written by the SHIPPED writer only — a throwaway `net472` console exe linking `Json.cs` + `ImportRefused.cs` + `AtomicFile.cs` + `Manifest.cs` (the `tools\Package\Package.csproj` link set), calling `ManifestFile.Load` → `AddMeshReplacement` → `Save`. No byte hand-typed. `Compare-Object` before/after, whole file: `<= "  }"`, `=> "  },"`, `=> "  \"replace\": ["`, `=> "    {\"bundle\":\"px_equipment_assets_all.bundle\",\"asset\":\"WPN_PX_RG_Assault_Rifle_T01_V01\",\"mesh\":\"cyborg_spider\"}"`, `=> "  ]"` — ONE hunk at the tail, the insert-the-array branch (`Splice` case (c)), 993 B → 1110 B. `ppcontent.json.bak` hashes **equal** to the pre-write copy, so the pre-write bytes are recoverable |

  **Screenshot:** `C:\Temp\claude\…\scratchpad\m8-console.png` (1280×720, 771628 B, `connect screenshot`,
  `jobId j19`). It does **not** show a ContentTool panel: the Instance3 process was concurrently being
  driven into `ALN_PLT_Nest_48x48_A` by another session, and the frame caught TFTV's own error popup over
  that mission. The acceptance evidence is the two `ct_project` replies above, not this frame.

  **Left behind / removed.** `D:\PP-Instance3\Mods\CtM8` was DELETED after the run — it declares
  `"id": "morgott.demo.customcreature"`, the same id as the shipped `CustomCreature` demo, so leaving it
  would give two projects one patched-copy folder. Its two patched bundles
  (`…\ContentTool\Patched\a6ca6add\morgott.demo.customcreature\`) were deleted with it. The real
  `D:\PP-Instance3\Mods\CustomCreature\ppcontent.json` hashes equal to `demos\CustomCreature\ppcontent.json`
  — never touched. `WeaponMesh`'s own patched copy is left in place; that is what baking that demo does.
  The game process is left RUNNING.

  - If PPCLI itself misbehaves during this run: append the entry to `E:\DEV\PhoenixPoint\PPCLI\ISSUES.md`
    (attempted → happened → expected → evidence → severity) and work around it. Do NOT edit PPCLI source,
    and never commit a PPCLI change from this repo.
  - `git -C E:\DEV\PhoenixPoint\ContentTool add internal-docs\planning\2026-09-02-manifest-core-plan.md && git -C E:\DEV\PhoenixPoint\ContentTool commit -m "docs(planning): record the in-game acceptance run that closes M7 and M8"`

---

**Never write a `.claude\green-pending` sentinel for any step here.** Every commit above is explicit and BY
PATH — the tree holds untracked `.zip` files and untracked import folders, so `git add -A` is forbidden
throughout.

---

## Task 11 acceptance run - 2026-09-02, offline gates

Real run only, on `main` at `48384c6` plus this task's own one-line arm rename. Every command below was
run fresh from `E:\DEV\PhoenixPoint\ContentTool`; the lines are copied out of that run, not predicted.
No game was touched — **M7** and **M8** are Task 12 and are recorded here as `pending`, not as passes.

### The gates

| # | Command | Last line | Exit |
|---|---|---|---|
| 1 | `dotnet build -c Release` | `Ошибок: 0` (`Предупреждений: 1` — the known `GlbCodec.cs(59,23) CS0649` on `SampledClip.Looping`) | 0 |
| 2 | `dotnet run --project tests\ObjCodecTests -c Release` | `DEMO BANKS: ALL PASS, 6 check(s)`; every section line reads PASS, no line reads FAIL | 0 |
| 3 | `dotnet run --project tests\TargetPathTests -c Release` | `R0: ALL PASS` (with `S14-ownbundle`, `S14-order-blind`, `S14-order-packages` all PASS) | 0 |
| 4 | `dotnet build tools\Package\Package.csproj -c Release` | `Ошибок: 0`, `Предупреждений: 0` | 0 |
| 5 | `dotnet build tools\ClipEvents\ClipEvents.csproj -c Release` | `Ошибок: 1` — `GlbReader.cs(6,27): error CS0234` on `Morgott.ContentTool.Bake`, and nothing else | 1 |
| 6 | `dotnet build tools\SpiderAxisCheck\SpiderAxisCheck.csproj -c Release` | `Ошибок: 1` — the same single `GlbReader.cs(6,27) CS0234` | 1 |

Rows 5 and 6 are the M1 measurement, not a regression: both projects carried **2** errors before this
slice and leave it with **1**. Nothing in either error mentions `Json`, `JsonWriter`, `ImportCode` or
`Manifest`, which is the whole claim — the migration did not widen their breakage.

### The arms this slice owns, as the run actually printed them

```
REFUSAL-COUNT PASS, 16 check(s) - 5 refusals, 5 failures
ALIAS PASS, 32 check(s) - simultaneous rename, untouched index tables, sidecar policy
MANIFEST PASS, 53 check(s) - atomic write, nested rows, byte-preserving splice, E3/E4/E5/E6/E8 refusals
PACKAGE-GATE PASS, 6 check(s)
```

**Deltas against this plan's own predictions.** The plan predicted `MANIFEST PASS, 46` (Task 9 step 4,
Task 10 step 1, Task 11 step 1), `REFUSAL-COUNT ... four higher` and `ALIAS ... two higher`. The real
counts are **53 / 16 / 32**. One reason for all three: review of each task added fixture arms beyond the
ones the plan enumerated — the `tricky.json` non-ASCII/escaped-quote fixture and the deleted-file,
null-replace, stale-`.tmp` and failed-commit arms among them (`ceafa8d`, `99b8589`, `b125995`). The
predictions were written before those fixtures existed and were never revised; the arm SENTENCE is the
contract, the integer is bookkeeping. Task 11 step 1 renamed that sentence, so the line now names what
it proves instead of only `atomic write`.

### Design §9 walked, row by row

| id | Verdict | Evidence | Commit |
|---|---|---|---|
| M1 | PASS | gate rows 1, 4, 5, 6 above: `Ошибок: 0` for the mod and for `Package`; exactly one pre-existing `CS0234` for `ClipEvents` and `SpiderAxisCheck`, down from two | `80d9d16`, `48384c6` |
| M2 | PASS | gate row 2: `dotnet run --project tests\ObjCodecTests -c Release`, every section PASS, exit 0 | whole slice |
| M3 | PASS | gate row 3: `R0: ALL PASS`, exit 0, `S14-ownbundle` / `S14-order-blind` / `S14-order-packages` green after the `Package` migration | `dfa5488`, `80d9d16` |
| M4 | PASS | `MANIFEST PASS, 53` covers `Manifest_AppendsMeshWithoutCollateralRewrite` (BOM + CRLF fixture, `[` and `]` located independently before and after, old row asserted as one unbroken byte run), `Manifest_LoadsKnownAndUnknownTree` (nested map read at all) and the `tricky.json` fixture | `b68029c`, `ef49e4f`, `85c6ed2` |
| M5 | PASS | same arm line: `Manifest_RefusesConcurrentEdit` (E5, the external bytes survive) and `AtomicFile_WriteLeavesBakAndNoTmp` including the stale-temp and failed-commit arms | `3a2fa8c`, `ceafa8d`, `99b8589` |
| M6 | PASS | E3 head and tail string-compared in Task 5's Validate arms; the `declares "replace"` sentence compared in full by Task 8's `Said("ParseReplace", "{\"replace\":[]}")` arm — both inside `MANIFEST PASS, 53` / `REFUSAL-COUNT PASS, 16` | `6986aed`, `428c1c9`, `b125995` |
| M7 | **PASS** (M7d not attempted) | Task 12's table: M7a/M7b/M7c green in game on `D:\PP-Instance3`. M7d is unreachable without a human at the Doctor panel — reason recorded there | run of 2026-09-02, this file |
| M8 | **PASS** | Task 12's table: `ManifestFile.Save` output re-read by `ContentProject.ParseReplace` + `JsonUtility` in game; the whole-file `Compare-Object` is ONE hunk | run of 2026-09-02, this file |

## Task 12 acceptance run - 2026-09-02, in game on `D:\PP-Instance3`

Real run only. Install `D:\PP-Instance3`, profile `76561197996210593`, `com.morgott.ContentTool` already in
that profile's `MOD_ACTIVATED` (read, not edited). `deploy.ps1 -PPRoot 'D:\PP-Instance3'` first, with no
game running (`Ошибок: 0`), then the game launched by hand with `-mods`; every call went through
`ppcli.ps1 connect … -PPRoot 'D:\PP-Instance3' -ProfileId 76561197996210593` after `connect state` answered
`{"ok":true,"phase":"menu","scene":"HomeScreen"}`. No `stale:true` in any reply. PPCLI itself behaved —
nothing appended to `PPCLI\ISSUES.md`.

**One caveat on the process.** It was launched here as pid 35736, which handed off to pid 37268 (the
bridge's own pid), and part-way through the run another session drove that same process from `HomeScreen`
into `ALN_PLT_Nest_48x48_A`. Both `ct_project` replies are unaffected — they are `ok:true` payloads from the
DLL deployed at the start of this run — but the screenshot is, which is why M7d and the screenshot row say
what they say.

**The slice is done.** Seven of eight design §9 rows are closed with evidence and the eighth (M7d) is a
button only a human at the bench can press; the code path under it is the same `AtomicFile.WriteText` the
offline gate already covers.
