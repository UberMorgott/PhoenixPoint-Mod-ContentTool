# "Replace one mesh" wizard — implementation plan (`ProjectScaffold` + `ShippedTarget` + the Doctor's SHIP row)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or
> superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ship `internal-docs\planning\2026-09-02-replace-mesh-wizard-design.md`: on the bench's MODEL DOCTOR tab,
with a prototype slot picked, a `.glb` picked, aliases set and a green `ReplacementPreflight` verdict, **one button**
writes a real mod folder beside `Mods\ContentTool\` (`ppcontent.json` + `meta.json` + `Content\Meshes\<stem>.glb` +
its alias sidecar), bakes it, applies it, and says in one honest sentence what the player must still do — no console
command typed at any point.

**Architecture:** Three seams, none of which invents a new subsystem. `ProjectScaffold` is a UnityEngine-free
writer that composes `ManifestFile`/`Manifest`/`AtomicFile`/`AliasMap` in a fixed order — everything validated
before the first byte, the manifest committed last — so the disk half is provable offline. `ShippedTarget` derives
the row's `(bundle, asset)` pair from the LIVE addon graph and proves it on disk with `BundleBaker.WhyNot`, which is
the same oracle `ProjectBake.Patch` uses, so the pair is by construction the one the bake will match. The Doctor
owns nothing but one `Intent.Ship`, a two-frame gate that paints the "baking…" label before Unity freezes, and a
call to `Route7.ApplyProject`.

**Tech Stack:** C# 9 / net472, Mono inside Unity 2019. No new dependencies. Build `dotnet build -c Release`
(`ContentTool.csproj` globs `src\**\*.cs`, so a new `src\` file needs no csproj edit). Offline gates are
`tests\ObjCodecTests` and `tests\TargetPathTests` (NOT `dotnet test`), each a `static class X { internal static
string Run() }` that throws on failure and is called from `Program.Main`. `ObjCodecTests.csproj` sets
`EnableDefaultCompileItems=false`, so **every new file — test or linked src — must be added to its
`<Compile Include>` list**.

**Seven facts this plan is built on, each read at HEAD `57226cd` rather than assumed:**

1. **`ProjectScaffold` can be test-linked; `ShippedTarget` cannot.** Everything the scaffold touches is already in
   `ObjCodecTests.csproj`: `AtomicFile.cs` (`:37`), `Manifest.cs` (`:38`), `ContentMods.cs` (`:75`), `Package.cs`
   (`:81`), `Json.cs` (`:143`), `AliasMap.cs` (`:182`). `ShippedTarget` needs `UnityEngine`, `Base.Assets`,
   `PhoenixPoint.Common.Entities.Addons` AND `BundleBaker`, so it is proven in game (Task 8) and by a build gate
   only.
2. **The writers are all in place and are called, never modified.** `Manifest.AddMeshReplacement` (`Manifest.cs:167`),
   `Manifest.Validate` (`:184`, E4 at `:205-208` is R6 verbatim), `ManifestFile.Load` (`:274`), `ManifestFile.Save`
   (`:318`, atomic splice + `.bak` + the E5 SHA guard at `:340`), `AtomicFile.Write/WriteText`,
   `AliasMap.SaveSidecar` (`:234`) / `LoadSidecar` (`:155`) / `Sha256` (`:137`) / `SidecarPathOf` (`:135`).
   `JsonWriter.Val(object)` (`Json.cs:172`) accepts exactly `Dictionary<string, object>` and `List<object>` — the
   template is built as the former or it throws `ArgumentException`.
3. **`ContentMods.ProjectDir(modDir, name)` (`:143-147`) resolves a name to `Sibling(root, name) ?? Mods\ContentTool\<name>`,
   and `Sibling` (`:123-129`) answers only for a folder that ALREADY holds `ppcontent.json`.** So the scaffold must
   create `Directory.GetParent(modDir)\<name>` itself and assert the post-condition afterwards; a folder under
   `Mods\ContentTool\` is never discovered by the manager (`ModGate.Decide:38` → `Unknown`, `Why:62`).
4. **`Route7.ApplyProject` (`:205`) is `private` and CONTINUES after a failed bake.** Today `:249-256` writes no
   freshness key but still installs whatever `PatchedDir` holds. Task 4 makes it `internal` and returns early on the
   PATCH-ROUTE failures alone (`ProjectBake.Run`'s new `patchFailed`, the count from `Patch(p, log)`) — gating on the
   run's total `failed` would let one unrelated `p.ImportFailures` (a bad .wav/.png/model) block the project's good
   patched bundles forever on the player's enable path, since the unwritten key re-bakes and re-fails every launch —
   a behaviour change that the console verb `ct_route7 apply` sees too, and that W5 in Task 8 is the proof for.
5. **A live preview makes `RigTarget.SameAs` false, always.** `Snapshot` (`ModelDoctor.cs:242-261`) records
   `MeshInstanceId` and `MeshName`; `SameAs` (`SkinCompatibility.cs:70-82`) compares both; `Target` is taken when the
   slot is picked (`:120`/`:142`) and `DoPreview:451` then assigns `Renderer.sharedMesh = candidate`. So R8 as first
   drafted would refuse every press made from a previewing panel — Task 6 step 2 splits the comparison instead.
6. **`Package.MetaRefusal` (`Package.cs:313-329`) already IS the meta validator**, and with `stagedFiles == null` its
   `AssemblyName` arm (`:324`) cannot fire. The scaffold calls it rather than growing a second opinion (R13).
7. **`Manifest.Validate` (`:184-211`) keys duplicates on `(bundle lowercased, asset, kind)`** and knows nothing about
   the mesh file, so an identical row re-added is E4 exactly like a conflicting one. Task 2 answers "is this the SAME
   row?" before adding, which is the only reason a retry after R7/R8/R11 can work at all.

## Codex plan review 2026-09-05 — applied

Deep review `C:\Temp\cx\ec5951d509df433a980182e8a0b1d624.out.md`: 19 findings plus a sequencing paragraph.

**Accepted and applied in this file:** 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 12, 13, 14, 15, 16, 17, 18 — three of them
in a lighter form. **2 + 3** land as **Task 2 Step 0**, because Task 1 is already shipped (`13e6361`) and its code
is on disk; **4** is one `FileMode.CreateNew` call, not a temp+move dance; **16** keeps every task boundary and
adds "land this task as several green commits" to Tasks 5 and 6 instead of renumbering anything.

**Rejected, each with its reason:**
- **11 (restore Instance2's `MOD_ACTIVATED`)** — rejected: Instance2 has its OWN profile `76561197996210592`
  (the user's game is `…591`), so an edit there can never reach the user's profile. Task 8's
  byte-snapshot of THAT profile's `Options.jopt` is the whole ceremony; no restore-and-hash ritual is added.
- **19 (assemble the whole `meta.json` tree through `JsonWriter`)** — rejected: `id` is the only value that can
  carry a quote or a backslash, and it already goes through `JsonWriter.Val` quoted AND escaped; the rest is a
  fixed literal whose expected bytes are spelled independently in the test. Design §4.2's wording is relaxed to
  "the id quoted through `JsonWriter`; the template body a fixed literal" instead.

**Execution order — the sequencing paragraph, accepted: 2 → 3 → 5 → 4 → 6 → 7 → 8.** The task NUMBERS do not
change; other documents cite them. Task 5 goes before Task 4 because target derivation defines the `forBundle`
value and is the riskiest compile/runtime seam, and `ApplyProject`'s global behaviour change must not sit
un-exercised across several commits — Task 4 then lands immediately adjacent to its first caller and its
acceptance evidence.

## File Structure

**Created**

| Path | Responsibility |
|---|---|
| `src\Project\ProjectScaffold.cs` | Name validation (R1), the default project name, `RootOf`, the `ppcontent.json` + `meta.json` templates (the latter keyed on the MANIFEST's id, an existing one validated through `Package.MetaRefusal` — R13), the row append-or-reuse through `ManifestFile` (R6 only for a conflict), the GLB copy policy (R3 pre-write, R4, R5) and the sidecar. UnityEngine-free, test-linked. |
| `src\Doctor\ShippedTarget.cs` | `Resolve(addon, smr, target)`: addon `SkinData` → `AssetReference` → Addressables locations (visited set, file names folded case-blind) → dependency bundle files → `BundleBaker.WhyNot(Mesh, asset)` must answer `null` for **exactly one**. One named refusal per step it could not take (R9/R10, R14–R22). Fills `PrototypeTarget.ShippedBundle/ShippedAsset/TargetRefusal`. Unity + AssetsTools, so not test-linked. |
| `tests\ObjCodecTests\ProjectScaffoldTests.cs` | The scaffold's arms: design §8's table. Prints `PROJECT-SCAFFOLD PASS, N check(s) - ...`. |

**Modified**

| Path | Change |
|---|---|
| `src\Doctor\PrototypeTarget.cs` `:43` | add `internal string ShippedBundle, ShippedAsset, TargetRefusal;` — the row's target, carried from the slot. |
| `src\Dev\FitBench.cs` `:739`, `:768` | `LiveSlots` keeps the owning `Addon` beside each renderer (`KeyValuePair<Addon, SkinnedMeshRenderer>`); `Retarget` calls `ShippedTarget.Resolve` per slot and stores the refusal instead of throwing. |
| `src\Bake\Route7.cs` `:198`, `:205`, `:249-256`, `:280` | `ApplyDisposition`; `private` → `internal` plus a `(name, forBundle, out how)` overload; a bake that reports failures returns R11 and installs nothing. |
| `src\Bake\BundleLive.cs` `:145` | add `ResidentNow(bundleFile)` — the residency fact, asked where its two private halves already live. |
| `src\Import\SkinCompatibility.cs` `:70-82` | split `RigTarget.SameAs` into `SameRigAs` + `SameAs`, so a live preview is not read as a changed rig (R8). |
| `src\Dev\ModelDoctor.cs` `:29`, `:228-234`, `:399-406`, `:1264` | `Intent.Ship`, `Enqueue("ship")`, the two-frame gate in `Tick`, the SHIP section in `Draw`, `ArmShip`/`DoShip`. |
| `tests\ObjCodecTests\ObjCodecTests.csproj` | link `..\..\src\Project\ProjectScaffold.cs` + compile `ProjectScaffoldTests.cs`. |
| `tests\ObjCodecTests\Program.cs` `:141` | `Console.WriteLine(ProjectScaffoldTests.Run());` after `ManifestTests.Run()`. |
| `internal-docs\planning\2026-09-02-replace-mesh-wizard-plan.md` | Task 8 fills in the in-game evidence table, in this file. |

**NOT modified:** `Manifest`, `ManifestFile`, `AtomicFile`, `AliasMap`, `ProjectBake`, `BundleBaker`, `BundleClaims`,
`Package`, `PatchCache`, `ContentMods`, `ModGate`. The wizard is a caller, not an author, of every one of them —
`Package.MetaRefusal` in particular is REUSED whole rather than re-implemented, so what the wizard accepts and what the
packager ships cannot drift. `ContentTool.csproj` needs no edit (it globs `src\**\*.cs`).

---

### Task 1: `ProjectScaffold` — the name table, and the two templates

- [ ] **Step 1: Write the failing gate.** Create `tests\ObjCodecTests\ProjectScaffoldTests.cs`:
  ```csharp
  using System;
  using System.Collections.Generic;
  using System.IO;
  using Morgott.ContentTool.Import;
  using Morgott.ContentTool.Project;

  /// <summary>The "Replace one mesh" wizard's DISK half: a project folder beside Mods\ContentTool\ that the mod
  /// manager can discover, one "replace" row per press, the .glb copied under Content\Meshes\ and never
  /// overwritten, and its alias sidecar keyed on the COPY. Every arm here is a way one press could quietly
  /// destroy an author's work - an overwritten mesh, a lost row, a project the manager cannot see.</summary>
  internal static class ProjectScaffoldTests
  {
      internal static string Run()
      {
          int checks = 0;
          string dir = Path.Combine(Path.GetTempPath(), "ct_scaffold_" + Guid.NewGuid().ToString("N"));
          string mods = Path.Combine(dir, "Mods");
          string modDir = Path.Combine(mods, "ContentTool");
          Directory.CreateDirectory(modDir);
          var empty = new Dictionary<string, string>(StringComparer.Ordinal);
          try
          {
              // ---- Scaffold_NameTable (R1). The folder is created BESIDE the player's other mods, so a name
              // that walks out of Mods\ is not a validation nicety - it is a write anywhere on the disk.
              checks += Check(ProjectScaffold.NameRefusal("Replace_Rifle") == null,
                              "an ordinary name is accepted");
              string[] bad =
              {
                  "", new string('a', 65), "..", "a\\b", "a/b", "C:\\x", "CON", "nul.glb", "-lead", "trail.",
                  "trail "
              };
              foreach (string name in bad)
              {
                  string said = ProjectScaffold.NameRefusal(name);
                  checks += Check(said != null &&
                                  said.StartsWith("project name REFUSED: '" + name + "'", StringComparison.Ordinal) &&
                                  said.EndsWith("no path separators, no device names", StringComparison.Ordinal),
                                  "R1 verbatim for '" + name + "' -> " + said);
              }
              checks += Check(Directory.GetDirectories(mods).Length == 1,
                              "and not one of them created a folder - Mods still holds only ContentTool");

              // ---- Scaffold_DefaultName: what the panel puts in the field before the author types.
              checks += Check(ProjectScaffold.DefaultName("WPN_PX_RG_Assault_Rifle_T01_V01") ==
                              "Replace_WPN_PX_RG_Assault_Rifle_T01_V01",
                              "the default name is Replace_ plus the shipped asset");
              checks += Check(ProjectScaffold.DefaultName("A B/C") == "Replace_A_B_C",
                              "anything the name table would refuse becomes '_'");
              string longName = ProjectScaffold.DefaultName(new string('x', 200));
              checks += Check(longName.Length == 64 && ProjectScaffold.NameRefusal(longName) == null,
                              "and a long asset name is cut to a name the table accepts: " + longName.Length);

              // ---- Scaffold_CreatesProjectTemplates
              string glb = Path.Combine(dir, "body.glb");
              File.WriteAllBytes(glb, new byte[] { 1, 2, 3 });
              string sha = AliasMap.Sha256(File.ReadAllBytes(glb));
              ProjectScaffold.Result made = ProjectScaffold.AddMeshReplacement(
                  modDir, "Replace_Rifle", glb, sha,
                  "px_equipment_assets_all.bundle", "WPN_PX_RG_Assault_Rifle_T01_V01", empty);
              checks += Check(made.Created && made.Root == Path.Combine(mods, "Replace_Rifle"),
                              "the project is the SIBLING Mods\\<name>, never a folder under ContentTool: " + made.Root);
              checks += Check(File.Exists(made.ManifestPath) && File.Exists(made.MetaPath),
                              "both templates are on disk");
              Manifest fresh = Manifest.Parse(File.ReadAllText(made.ManifestPath));
              checks += Check(fresh.Id == "Replace_Rifle" && fresh.Bundle == "Replace_Rifle.bundle",
                              "the manifest declares id and bundle: " + fresh.Id + " / " + fresh.Bundle);
              checks += Check(File.ReadAllText(made.MetaPath) == Template("Replace_Rifle"),
                              "meta.json is the design §4.2 template, byte for byte");
              checks += Check(ContentMods.ProjectDir(modDir, "Replace_Rifle") == made.Root,
                              "and ContentMods.ProjectDir now resolves that name to it - ct_project <name> finds it");

              // ---- Scaffold_KeepsAnAuthoredId. ID == folder name is true of a project THIS tool made and of
              // nothing else: an authored ppcontent.json keeps whatever "id" its author chose, and the
              // meta.json written beside it has to key the mod on THAT, or the manager lists one id while
              // every route resolves another.
              string authored = Path.Combine(mods, "Authored");
              Directory.CreateDirectory(authored);
              File.WriteAllText(Path.Combine(authored, "ppcontent.json"),
                                "{\n  \"id\": \"com.someone.hand.written\",\n  \"bundle\": \"theirs.bundle\"\n}\n");
              ProjectScaffold.Result joined = ProjectScaffold.AddMeshReplacement(
                  modDir, "Authored", glb, sha, "a.bundle", "Foo", empty);
              checks += Check(!joined.Created && File.ReadAllText(joined.MetaPath) ==
                              Template("com.someone.hand.written"),
                              "the generated meta.json carries the MANIFEST's id, not the folder name");
              checks += Check(Manifest.Parse(File.ReadAllText(joined.ManifestPath)).Bundle == "theirs.bundle",
                              "and the authored id/bundle are not rewritten");

              // ---- Scaffold_RefusesAnUnshippableMeta (R13). Reachable only for a folder that IS a project
              // already (anything else is R2), and validated by the PACKAGER'S own validator, so "what ships"
              // and "what the wizard accepts" cannot drift.
              string idless = Project(mods, "IdLess", "{ \"Version\": \"1.0.0\" }");
              byte[] metaWas = File.ReadAllBytes(Path.Combine(idless, "meta.json"));
              string noId = null;
              try { ProjectScaffold.AddMeshReplacement(modDir, "IdLess", glb, sha, "a.bundle", "Foo", empty); }
              catch (InvalidDataException refused) { noId = refused.Message; }
              checks += Check(noId != null &&
                              noId.StartsWith("'" + Path.Combine(idless, "meta.json") + "' already exists but " +
                                              "is not a mod this project can ship: ", StringComparison.Ordinal) &&
                              noId.EndsWith("the mod manager keys every mod on it. - fix that file, or ship " +
                                            "into another project", StringComparison.Ordinal),
                              "R13 wraps Package.MetaRefusal's own ID sentence: " + noId);
              Project(mods, "NoDependency", "{ \"ID\": \"NoDependency\", \"Dependencies\": [] }");
              string noDep = null;
              try { ProjectScaffold.AddMeshReplacement(modDir, "NoDependency", glb, sha, "a.bundle", "Foo", empty); }
              catch (InvalidDataException refused) { noDep = refused.Message; }
              checks += Check(noDep != null &&
                              noDep.IndexOf("does not declare \"Dependencies\": [ \"com.morgott.ContentTool\" ]",
                                            StringComparison.Ordinal) > 0,
                              "R13 also carries the DEPENDENCY sentence: " + noDep);
              checks += Check(Same(File.ReadAllBytes(Path.Combine(idless, "meta.json")), metaWas),
                              "and a refused meta.json is never rewritten");

              // ---- Scaffold_RefusesAnUnrelatedFolder (R2)
              string squatter = Path.Combine(mods, "Squatter");
              Directory.CreateDirectory(squatter);
              File.WriteAllText(Path.Combine(squatter, "readme.txt"), "not a project");
              string why = null;
              try
              {
                  ProjectScaffold.AddMeshReplacement(modDir, "Squatter", glb, sha, "a.bundle", "Foo", empty);
              }
              catch (InvalidDataException refused) { why = refused.Message; }
              checks += Check(why == "'" + squatter + "' already exists, is not empty, and holds no " +
                                     "ppcontent.json, so it is not a ContentTool project - pick another " +
                                     "project name",
                              "R2 verbatim: " + why);
              checks += Check(!File.Exists(Path.Combine(squatter, "ppcontent.json")) &&
                              !File.Exists(Path.Combine(squatter, "meta.json")),
                              "and nothing was written into someone else's folder");

              // ---- Scaffold_FillsAnEmptyFolder. An EMPTY folder of that name is not someone else's work -
              // it is a folder, and refusing it would strand an author who created it in Explorer first.
              Directory.CreateDirectory(Path.Combine(mods, "EmptyOne"));
              ProjectScaffold.Result filled = ProjectScaffold.AddMeshReplacement(
                  modDir, "EmptyOne", glb, sha, "a.bundle", "Foo", empty);
              checks += Check(filled.Created && File.Exists(filled.ManifestPath),
                              "an empty folder of that name counts as new and is filled in");
          }
          finally { try { Directory.Delete(dir, true); } catch (Exception) { } }
          return "PROJECT-SCAFFOLD PASS, " + checks + " check(s) - name table, project templates";
      }

      /// <summary>The meta.json the scaffold must produce, spelled here independently of the code that writes
      /// it: a template compared against itself proves nothing. The argument is the MANIFEST's id, which is
      /// the folder name only for a project this tool created.</summary>
      private static string Template(string id)
      {
          return "{\n  \"ID\": \"" + id + "\",\n" +
                 "  \"Version\": \"1.0.0\",\n" +
                 "  \"Name\": [ { \"Key\": \"English\", \"Value\": \"" + id + "\" } ],\n" +
                 "  \"Dependencies\": [ \"com.morgott.ContentTool\" ]\n}\n";
      }

      /// <summary>A folder that already IS a project - a valid ppcontent.json plus the meta.json under
      /// test - because R2 refuses any other non-empty folder before R13 could ever be reached.</summary>
      private static string Project(string mods, string name, string metaText)
      {
          string at = Path.Combine(mods, name);
          Directory.CreateDirectory(at);
          File.WriteAllText(Path.Combine(at, "ppcontent.json"),
                            "{\n  \"id\": \"" + name + "\",\n  \"bundle\": \"" + name + ".bundle\"\n}\n");
          File.WriteAllText(Path.Combine(at, "meta.json"), metaText);
          return at;
      }

      private static bool Same(byte[] a, byte[] b)
      {
          if (a.Length != b.Length) return false;
          for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
          return true;
      }

      private static int Check(bool condition, string what)
      {
          if (!condition) throw new Exception("PROJECT-SCAFFOLD FAILURE: " + what);
          return 1;
      }
  }
  ```
  Register it. In `tests\ObjCodecTests\ObjCodecTests.csproj`, after the `ManifestTests.cs` line (`:39`):
  ```xml
    <Compile Include="..\..\src\Project\ProjectScaffold.cs" Link="ProjectScaffold.cs" />
    <Compile Include="ProjectScaffoldTests.cs" />
  ```
  In `tests\ObjCodecTests\Program.cs`, after `Console.WriteLine(ManifestTests.Run());` (`:141`):
  ```csharp
        Console.WriteLine(ProjectScaffoldTests.Run());
  ```
  - Run: `dotnet run --project tests\ObjCodecTests -c Release`
  - Expected: **FAIL to compile**, two kinds of error saying the same thing —
    `error CS2001: Source file '..\..\src\Project\ProjectScaffold.cs' could not be found` (the linked file does
    not exist yet) and `error CS0246: The type or namespace name 'ProjectScaffold' could not be found (are you
    missing a using directive or an assembly reference?)`, repeated per use. `using Morgott.ContentTool.Project;`
    does **not** error — that namespace is already in this assembly (`Manifest.cs`, `ContentMods.cs`, `Package.cs`
    are linked) — and neither does `AliasMap` (`:182`). `Same` and `Project` are both used from this step on. A
    missing type is a compile error, and that is this step's red.

- [ ] **Step 2: Implement the name table and the templates.** Create `src\Project\ProjectScaffold.cs`:
  ```csharp
  using System;
  using System.Collections.Generic;
  using System.IO;
  using System.Text;
  using Morgott.ContentTool.Import;
  using Morgott.ContentTool.IO;

  namespace Morgott.ContentTool.Project
  {
      /// <summary>
      /// ONE PRESS, ONE MOD FOLDER. Turns a green Doctor verdict into a real project beside Mods\ContentTool\:
      /// ppcontent.json + meta.json + Content\Meshes\&lt;stem&gt;.glb + its alias sidecar, with one "replace" row
      /// added to whatever the author already had.
      ///
      /// It AUTHORS nothing itself. Every byte goes out through ManifestFile.Save (atomic splice, .bak, the E5
      /// fingerprint), AtomicFile and AliasMap.SaveSidecar, which is why an existing project's own formatting,
      /// key order and unknown keys survive a press by construction.
      ///
      /// PLACEMENT IS THE WHOLE POINT: the SIBLING Mods\&lt;name&gt;, never ContentMods.ProjectDir's
      /// Mods\ContentTool\&lt;name&gt; fallback (ContentMods.cs:147). A folder under ContentTool is not a mod the
      /// manager can discover (ModGate.Decide:38 -> Unknown) or the player can switch off, so shipping into one
      /// would produce content nobody can turn off - gate G1's bug through a different door.
      ///
      /// UnityEngine-free on purpose: the whole disk half is proven in tests\ObjCodecTests instead of by pressing
      /// a button in a running game.
      /// </summary>
      internal static class ProjectScaffold
      {
          /// <summary>What one press produced, so the panel can name the folder it wrote and the bake can be
          /// handed the ABSOLUTE root rather than a name the console parser would have to re-resolve.</summary>
          internal sealed class Result
          {
              internal string Root, ManifestPath, MetaPath, MeshPath, SidecarPath;
              internal bool Created, MeshAlreadyPresent, RowAlreadyPresent;
              /// <summary>The bytes that were VERIFIED against the verdict's sha and are now the copy's, so
              /// the caller re-judges what it wrote instead of re-reading the file and re-opening the
              /// question of whether the two are the same bytes. Null until Task 3.</summary>
              internal byte[] MeshBytes;
          }

          /// <summary>Windows reserves these with OR without an extension, so "nul.glb" is a folder that cannot
          /// be created and whose failure reads like a bug in this tool.</summary>
          private static readonly string[] Devices =
          {
              "CON", "PRN", "AUX", "NUL",
              "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
              "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
          };

          /// <summary>Null when the name may be a folder beside the player's other mods; the refusal otherwise.
          /// R1. The rule is deliberately narrower than the filesystem's: this name is also the mod ID, the
          /// meta.json "ID" and the bundle's stem, and every one of those is compared as text somewhere.</summary>
          internal static string NameRefusal(string name)
          {
              if (!Usable(name))
                  return "project name REFUSED: '" + (name ?? "") + "' - use 1-64 characters starting with a " +
                         "letter or digit, then letters, digits, '.', '_' or '-'; no path separators, no device names";
              return null;
          }

          /// <summary>What the panel offers before the author types. Never refused by NameRefusal, which is what
          /// makes the Ship button live the moment a slot resolves.</summary>
          internal static string DefaultName(string shippedAsset)
          {
              var spelled = new StringBuilder("Replace_");
              foreach (char c in shippedAsset ?? "")
                  spelled.Append(Alnum(c) || c == '.' || c == '_' || c == '-' ? c : '_');
              string name = spelled.ToString();
              if (name.Length > 64) name = name.Substring(0, 64);
              // A cut can land on a '.', and a name ending in one is refused - so the cut trims its own tail
              // rather than handing the author a default the button will not accept.
              return name.TrimEnd('.', ' ');
          }

          /// <summary>The folder <see cref="AddMeshReplacement"/> would use, or null when the name or the mod
          /// folder makes one impossible. Spelled once, here, so the ship gate's catch-all can say whether
          /// that folder exists without re-deriving the path it never got back (R12).</summary>
          internal static string RootOf(string modDir, string name)
          {
              if (NameRefusal(name) != null || string.IsNullOrEmpty(modDir)) return null;
              DirectoryInfo mods = Directory.GetParent(modDir);
              return mods == null ? null : Path.Combine(mods.FullName, name);
          }

          /// <summary>
          /// Add one mesh replacement to the project of that name, creating it when it does not exist.
          /// <paramref name="modDir"/> is ContentToolMain.ModDir; the project lands in its PARENT.
          /// </summary>
          /// <exception cref="InvalidDataException">R1, R2, R5, or R6/E3/E4 out of Manifest.Validate.</exception>
          /// <exception cref="IOException">R3, R4, or E5/E6 out of ManifestFile.Save.</exception>
          internal static Result AddMeshReplacement(string modDir, string name, string sourceGlb, string expectedSha,
                                                    string shippedBundle, string shippedAsset,
                                                    IDictionary<string, string> aliases)
          {
              string refusal = NameRefusal(name);
              if (refusal != null) throw new InvalidDataException(refusal);
              if (string.IsNullOrEmpty(sourceGlb) || !File.Exists(sourceGlb))
                  throw new FileNotFoundException("the .glb to ship is not on disk", sourceGlb ?? "");
              if (string.IsNullOrEmpty(shippedBundle) || string.IsNullOrEmpty(shippedAsset))
                  throw new InvalidDataException("no shipped target was derived for this slot, so there is no " +
                                                 "row to write - pick the slot again");

              DirectoryInfo mods = string.IsNullOrEmpty(modDir) ? null : Directory.GetParent(modDir);
              if (mods == null)
                  throw new InvalidDataException("ContentTool's own mod folder is not known, so there is nowhere " +
                                                 "beside it to put a project");

              var result = new Result();
              result.Root = Path.Combine(mods.FullName, name);
              result.ManifestPath = Path.Combine(result.Root, ContentMods.Manifest);
              result.MetaPath = Path.Combine(result.Root, "meta.json");
              string stem = Path.GetFileNameWithoutExtension(sourceGlb);
              result.MeshPath = Path.Combine(result.Root, "Content", "Meshes", stem + ".glb");
              result.SidecarPath = AliasMap.SidecarPathOf(result.MeshPath);
              result.Created = !File.Exists(result.ManifestPath);

              // R2. Only a folder that already declares itself a project may be added to; anything else with
              // files in it belongs to someone, and this tool does not move into it.
              if (result.Created && Directory.Exists(result.Root) &&
                  (Directory.GetFiles(result.Root).Length != 0 ||
                   Directory.GetDirectories(result.Root).Length != 0))
                  throw new InvalidDataException("'" + result.Root + "' already exists, is not empty, and " +
                                                 "holds no ppcontent.json, so it is not a ContentTool project " +
                                                 "- pick another project name");

              Directory.CreateDirectory(result.Root);
              if (result.Created)
              {
                  // The two keys ManifestFile.Load requires (E2) and nothing else: an authored project's own
                  // "id" and "bundle" are never rewritten, so this shape is only ever the FIRST press's.
                  var tree = new Dictionary<string, object>(StringComparer.Ordinal)
                  {
                      { "id", name }, { "bundle", name + ".bundle" }
                  };
                  AtomicFile.WriteText(result.ManifestPath,
                                       new JsonWriter().Val(tree).ToString() + "\n", new UTF8Encoding(false));
              }

              // THE MANIFEST FIRST, and its ID rather than the folder name. "id == name" is true of a project
              // THIS tool made and of nothing else - an authored ppcontent.json keeps whatever id its author
              // chose - and a meta.json keyed on the folder name would then list one mod while every route
              // resolves another. Load is also the strict reader (E1/E2), so a manifest this tool cannot edit
              // safely stops the press before a meta is written beside it.
              ManifestFile file = ManifestFile.Load(result.ManifestPath);
              string id = file.Manifest.Id;
              if (!File.Exists(result.MetaPath))
                  AtomicFile.WriteText(result.MetaPath, Meta(id), new UTF8Encoding(false));
              else
              {
                  // R13. An existing meta is never rewritten and never trusted: PACKAGE'S own validator says
                  // whether a player would end up with a working mod, so the wizard and the packager cannot
                  // disagree. stagedFiles is null on purpose - nothing is staged yet, and that null is what
                  // switches off MetaRefusal's AssemblyName arm (Package.cs:324).
                  string said = Package.MetaRefusal(File.ReadAllText(result.MetaPath), null);
                  if (said != null)
                      throw new InvalidDataException("'" + result.MetaPath + "' already exists but is not a " +
                                                     "mod this project can ship: " + said + " - fix that file, " +
                                                     "or ship into another project");
              }

              // THE POST-CONDITION, asserted rather than assumed: this is what makes `ct_project <name>` and
              // `ct_route7 apply <name>` find the folder that was just written (ContentMods.Sibling:128).
              if (!string.Equals(ContentMods.ProjectDir(modDir, name), result.Root,
                                 StringComparison.OrdinalIgnoreCase))
                  throw new IOException("'" + result.Root + "' was written but ContentMods.ProjectDir still does " +
                                        "not resolve '" + name + "' to it, so a bake would read the wrong folder");
              return result;
          }

          /// <summary>The code-free content mod's meta.json, shaped like the shipped demo
          /// demos\MaterialTweak\meta.json, keyed on the MANIFEST's id. "AssemblyName" is omitted deliberately -
          /// ModMeta defaults it to string.Empty, ModRoster.AfterLoadMod supplies the content-only instance, and
          /// Package.MetaRefusal only objects when the field NAMES a file that is not in the package.
          /// "Dependencies" is what makes Phoenix Point's manager enable ContentTool for the player
          /// (Package.EngineId:35, MetaRefusal:319-322); without it the mod installs and silently does nothing.
          /// The id goes through JsonWriter rather than into a quoted hole: a NEW project's id is
          /// NameRefusal-limited to letters, digits, '.', '_' and '-', but an EXISTING project's came back
          /// DECODED from ManifestFile.Load, so it may hold a quote or a backslash that would end the file's
          /// JSON in the wrong place.</summary>
          private static string Meta(string id)
          {
              string quoted = new JsonWriter().Val(id).ToString();     // quoted AND escaped
              return "{\n  \"ID\": " + quoted + ",\n" +
                     "  \"Version\": \"1.0.0\",\n" +
                     "  \"Name\": [ { \"Key\": \"English\", \"Value\": " + quoted + " } ],\n" +
                     "  \"Dependencies\": [ \"" + Package.EngineId + "\" ]\n}\n";
          }

          private static bool Usable(string name)
          {
              if (string.IsNullOrEmpty(name) || name.Length > 64) return false;
              if (!Alnum(name[0])) return false;
              foreach (char c in name)
                  if (!Alnum(c) && c != '.' && c != '_' && c != '-') return false;
              // ' ' is already out by the loop above; the trailing '.' is not, and Windows silently strips it,
              // so "Foo." and "Foo" would be one folder under two names.
              if (name[name.Length - 1] == '.') return false;
              string bare = name;
              int dot = bare.IndexOf('.');
              if (dot >= 0) bare = bare.Substring(0, dot);
              foreach (string device in Devices)
                  if (string.Equals(bare, device, StringComparison.OrdinalIgnoreCase)) return false;
              return true;
          }

          /// <summary>ASCII only, deliberately: char.IsLetterOrDigit would accept a name whose spelling depends
          /// on the machine's code page once it becomes a folder, a mod ID and a bundle stem.</summary>
          private static bool Alnum(char c)
          {
              return (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9');
          }
      }
  }
  ```
  - Run: `dotnet run --project tests\ObjCodecTests -c Release`
  - Expected: `PROJECT-SCAFFOLD PASS, 29 check(s) - name table, project templates` among all-green output; the last
    line of the run is still `DEMO BANKS: ALL PASS, 6 check(s)` and exit code 0.

- [ ] **Step 3: Build.** Run `dotnet build -c Release` → `Ошибок: 0` (`Предупреждений: 1`, the known
  `GlbCodec.cs(59,23) CS0649` on `SampledClip.Looping`).

- [ ] **Step 4: Commit.**
  - `git -C E:\DEV\PhoenixPoint\ContentTool add src\Project\ProjectScaffold.cs tests\ObjCodecTests\ProjectScaffoldTests.cs tests\ObjCodecTests\ObjCodecTests.csproj tests\ObjCodecTests\Program.cs && git -C E:\DEV\PhoenixPoint\ContentTool commit -m "feat(project): ProjectScaffold writes a discoverable mod folder beside ContentTool, with a name a folder and a mod id can both carry"`

---

### Task 2: the row, appended through `ManifestFile`, reused when identical, refused when it conflicts

- [ ] **Step 0: Task 1 follow-ups (Codex findings 2, 3, and the cavecrew note on `13e6361`).** Task 1 is shipped;
  these are corrections to code already on disk, landed here before the row work.
  1. **Normalize `modDir` before taking its parent** (finding 2). `Directory.GetParent("…\Mods\ContentTool\")`
     answers `…\Mods\ContentTool`, so a trailing separator puts the project UNDER ContentTool — exactly where the
     manager never discovers it (`ModGate.Decide:38` → `Unknown`) — and the post-condition accepts it, because
     `ContentMods.ProjectDir` walks the same wrong parent. In BOTH `RootOf` and `AddMeshReplacement`, replace
     `Directory.GetParent(modDir)` with the parent of
     `Path.GetFullPath(modDir).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)`.
     Arms: `RootOf(modDir + "\\", "Replace_Rifle") == RootOf(modDir, "Replace_Rifle")`, and one press made
     through the trailing-separator spelling landing in the same `Root` as the non-trailing one.
  2. **Strictly parse an EXISTING `meta.json` before `Package.MetaRefusal`** (finding 3). `MetaRefusal`
     (`Package.cs:313-329`) is REGEX-based: an unclosed object that happens to hold a matching `ID` and
     `Dependencies` passes it, so R13 alone does not prove the mod is discoverable. Use the reader this codebase
     already has — **`Json.Parse(text, maxDepth)`** (`src\Import\Json.cs:15`), which returns a
     `Dictionary<string, object>` for an object and throws `FormatException` naming the offset otherwise, and is
     already linked into the gate (`ObjCodecTests.csproj:145`). **Do not add a parser.** In the `else` branch,
     before the `MetaRefusal` call: `Json.Parse(text, 64)`, and refuse as R13 when it throws (carrying the
     `FormatException` message) or when the result is not a `Dictionary<string, object>`; then call `MetaRefusal`
     unchanged. Arms: a malformed meta
     (`{"ID":"x","Dependencies":["com.morgott.ContentTool"` — nothing closed) → R13 and the file not rewritten;
     a non-object meta (`[1,2]`) → R13 and the file not rewritten.
  3. **A quoted-id fixture** (cavecrew note). The helper `ProjectScaffoldTests.Template()` (`:146-151`) spells the
     expected meta by hand and does NOT JSON-escape the id, while production `Meta()` does (`JsonWriter.Val`). Add
     an arm shipping into a project whose authored `ppcontent.json` carries the id `com.test"quote`: the written
     `meta.json` re-reads through `Json.Parse` with `ID == com.test"quote`, and `Package.MetaRefusal` accepts it.
     Escape the id inside `Template()` the same way (or build that arm's expected bytes with `JsonWriter`) — never
     weaken the fixture to the unescaped spelling.
  - Run: `dotnet run --project tests\ObjCodecTests -c Release`
  - Expected: `PROJECT-SCAFFOLD PASS, 35 check(s) - name table, project templates` (29 + 6), last line
    `DEMO BANKS: ALL PASS, 6 check(s)`, exit 0. Commit this step on its own:
    `git -C E:\DEV\PhoenixPoint\ContentTool add src\Project\ProjectScaffold.cs tests\ObjCodecTests\ProjectScaffoldTests.cs && git -C E:\DEV\PhoenixPoint\ContentTool commit -m "fix(project): a trailing separator on ModDir no longer buries the project under ContentTool, and an existing meta.json is parsed before it is judged"`

- [ ] **Step 1: Write the failing gate.** In `ProjectScaffoldTests.Run()`, inside the `try`, after the
  `Scaffold_RefusesAnUnrelatedFolder` block (the `EmptyOne` check is the last thing before it):
  ```csharp
              // ---- Scaffold_AppendsSecondRow. The first press is the one made above; this is what it left.
              ManifestFile one = ManifestFile.Load(made.ManifestPath);
              checks += Check(one.Manifest.Replace.Count == 1 &&
                              one.Manifest.Replace[0].Bundle == "px_equipment_assets_all.bundle" &&
                              one.Manifest.Replace[0].Asset == "WPN_PX_RG_Assault_Rifle_T01_V01" &&
                              one.Manifest.Replace[0].Mesh == "body",
                              "the first press left exactly one mesh row, mesh = the .glb's own stem");

              string second = Path.Combine(dir, "hand.glb");
              File.WriteAllBytes(second, new byte[] { 4, 5, 6, 7 });
              string secondSha = AliasMap.Sha256(File.ReadAllBytes(second));

              // The append is proved against a HAND-WRITTEN manifest, not one this tool authored (Codex
              // finding 12): the template has no unknown member, no nested value and no BOM, so a splice
              // that lost any of those would still pass a check made against it. The row's own bytes are
              // asserted INSIDE an independently located span, and everything outside that span is compared
              // byte for byte - a substring search alone proves nothing about what moved elsewhere.
              string authored = Path.Combine(mods, "Handwritten");
              Directory.CreateDirectory(authored);
              string authoredManifest = Path.Combine(authored, "ppcontent.json");
              const string handwritten =
                  "\uFEFF{\n  \"id\": \"Handwritten\",\n  \"bundle\": \"Handwritten.bundle\",\n" +
                  "  \"note\": \"ünknown member, kept verbatim\",\n" +
                  "  \"replace\": [ {\"bundle\":\"px_equipment_assets_all.bundle\"," +
                  "\"asset\":\"WPN_PX_RG_Assault_Rifle_T01_V01\",\"mesh\":\"body\"} ],\n" +
                  "  \"nested\": { \"a\": [ 1, 2, { \"b\": true } ] }\n}\n";
              File.WriteAllText(authoredManifest, handwritten, new UTF8Encoding(false));
              string beforeAppend = File.ReadAllText(authoredManifest);
              ProjectScaffold.Result grew = ProjectScaffold.AddMeshReplacement(
                  modDir, "Handwritten", second, secondSha,
                  "px_equipment_assets_all.bundle", "WPN_PX_Hand", empty);
              checks += Check(!grew.Created && grew.Root == authored,
                              "the SECOND press joins the AUTHORED project instead of making another one");
              string afterAppend = File.ReadAllText(authoredManifest);
              // Located independently in each text - the '[' after the "replace" key through its ']' - so the
              // comparison never borrows the writer's own idea of where it wrote.
              int wasOpen = beforeAppend.IndexOf('[', beforeAppend.IndexOf("\"replace\"", StringComparison.Ordinal));
              int wasClose = beforeAppend.IndexOf(']', wasOpen);
              int isOpen = afterAppend.IndexOf('[', afterAppend.IndexOf("\"replace\"", StringComparison.Ordinal));
              int isClose = afterAppend.IndexOf(']', isOpen);
              checks += Check(beforeAppend.Substring(0, wasOpen) == afterAppend.Substring(0, isOpen) &&
                              beforeAppend.Substring(wasClose) == afterAppend.Substring(isClose),
                              "every byte OUTSIDE the replace span is unchanged - BOM, unknown member, nested " +
                              "value, prefix AND suffix");
              const string firstRow = "{\"bundle\":\"px_equipment_assets_all.bundle\"," +
                                      "\"asset\":\"WPN_PX_RG_Assault_Rifle_T01_V01\",\"mesh\":\"body\"}";
              checks += Check(afterAppend.Substring(isOpen, isClose - isOpen)
                                  .IndexOf(firstRow, StringComparison.Ordinal) >= 0,
                              "and the original row survived INSIDE the new span as ONE unbroken byte run");
              ManifestFile two = ManifestFile.Load(authoredManifest);
              checks += Check(two.Manifest.Replace.Count == 2 && two.Manifest.Replace[1].Mesh == "hand" &&
                              two.Manifest.Id == "Handwritten" && two.Manifest.Bundle == "Handwritten.bundle",
                              "two rows now, id and bundle untouched: " + two.Manifest.Replace.Count);
              checks += Check(File.ReadAllText(Path.Combine(authored, "meta.json")) == Template("Handwritten"),
                              "and the meta written beside it is the §4.2 template on the MANIFEST's id");

              // ---- Scaffold_ReusesAnIdenticalRow. THE RETRY PATH, in a FRESH project (Codex finding 13), so
              // the assertion is "exactly ONE row after two identical runs" rather than "two rows, one of them
              // older". Every "fix it and press Ship again" in the design meets a row this tool already
              // committed; if that read as R6 the author could never retry anything.
              ProjectScaffold.Result once = ProjectScaffold.AddMeshReplacement(
                  modDir, "Replace_Twice", second, secondSha,
                  "px_equipment_assets_all.bundle", "WPN_PX_Hand", empty);
              byte[] afterFirst = File.ReadAllBytes(once.ManifestPath);
              ProjectScaffold.Result reused = ProjectScaffold.AddMeshReplacement(
                  modDir, "Replace_Twice", second, secondSha,
                  "PX_EQUIPMENT_ASSETS_ALL.BUNDLE", "WPN_PX_Hand", empty);
              checks += Check(reused.RowAlreadyPresent && !reused.Created && reused.Root == once.Root,
                              "the IDENTICAL press reuses the row instead of refusing it");
              checks += Check(ManifestFile.Load(once.ManifestPath).Manifest.Replace.Count == 1,
                              "and the file holds exactly ONE row after two identical runs");
              checks += Check(Same(File.ReadAllBytes(once.ManifestPath), afterFirst),
                              "the manifest bytes did not move at all - a reuse writes nothing");

              // ---- Scaffold_RefusesConflictingTarget (R6 == Manifest.Validate's E4, verbatim). The same
              // target with a DIFFERENT mesh is the case R6 was written for, and the only one left.
              string dupSrc = Path.Combine(dir, "dupsrc.glb");
              File.WriteAllBytes(dupSrc, new byte[] { 8, 9 });
              byte[] beforeDup = File.ReadAllBytes(once.ManifestPath);
              string dup = null;
              try
              {
                  ProjectScaffold.AddMeshReplacement(modDir, "Replace_Twice", dupSrc,
                                                     AliasMap.Sha256(File.ReadAllBytes(dupSrc)),
                                                     "PX_EQUIPMENT_ASSETS_ALL.BUNDLE", "WPN_PX_Hand", empty);
              }
              catch (InvalidDataException refused) { dup = refused.Message; }
              checks += Check(dup == "ppcontent.json already replaces \"WPN_PX_Hand\" in " +
                                     "\"PX_EQUIPMENT_ASSETS_ALL.BUNDLE\" with a mesh, so a second row for the " +
                                     "same target was NOT written - edit the existing row instead",
                              "R6 is E4 verbatim, the bundle folded case-blind: " + dup);
              checks += Check(Same(File.ReadAllBytes(once.ManifestPath), beforeDup),
                              "the manifest bytes are identical after the refusal");
              checks += Check(!File.Exists(Path.Combine(once.Root, "Content", "Meshes", "dupsrc.glb")),
                              "and the refused row copied no .glb - Validate runs before the first byte moves");
              checks += Check(ManifestFile.Load(once.ManifestPath).Manifest.Replace.Count == 1,
                              "a conflicting press leaves the one row that was already there");
  ```
  - Run: `dotnet run --project tests\ObjCodecTests -c Release`
  - Expected: **FAIL at runtime**, not at compile time — Task 1's scaffold writes no row, so the first new check is
    false and the arm throws. The run ends with
    `Unhandled Exception: System.Exception: PROJECT-SCAFFOLD FAILURE: the first press left exactly one mesh row,
    mesh = the .glb's own stem` and a non-zero exit code, with every line printed before it still PASS.

- [ ] **Step 2: Implement.** In `src\Project\ProjectScaffold.cs`, replace the body of `AddMeshReplacement` with
  this — the same method, now committing a row through `ManifestFile`. Everything else in the file is unchanged:
  ```csharp
          internal static Result AddMeshReplacement(string modDir, string name, string sourceGlb, string expectedSha,
                                                    string shippedBundle, string shippedAsset,
                                                    IDictionary<string, string> aliases)
          {
              string refusal = NameRefusal(name);
              if (refusal != null) throw new InvalidDataException(refusal);
              if (string.IsNullOrEmpty(sourceGlb) || !File.Exists(sourceGlb))
                  throw new FileNotFoundException("the .glb to ship is not on disk", sourceGlb ?? "");
              if (string.IsNullOrEmpty(shippedBundle) || string.IsNullOrEmpty(shippedAsset))
                  throw new InvalidDataException("no shipped target was derived for this slot, so there is no " +
                                                 "row to write - pick the slot again");

              DirectoryInfo mods = string.IsNullOrEmpty(modDir) ? null : Directory.GetParent(modDir);
              if (mods == null)
                  throw new InvalidDataException("ContentTool's own mod folder is not known, so there is nowhere " +
                                                 "beside it to put a project");

              var result = new Result();
              result.Root = Path.Combine(mods.FullName, name);
              result.ManifestPath = Path.Combine(result.Root, ContentMods.Manifest);
              result.MetaPath = Path.Combine(result.Root, "meta.json");
              string stem = Path.GetFileNameWithoutExtension(sourceGlb);
              result.MeshPath = Path.Combine(result.Root, "Content", "Meshes", stem + ".glb");
              result.SidecarPath = AliasMap.SidecarPathOf(result.MeshPath);
              result.Created = !File.Exists(result.ManifestPath);

              if (result.Created && Directory.Exists(result.Root) &&
                  (Directory.GetFiles(result.Root).Length != 0 ||
                   Directory.GetDirectories(result.Root).Length != 0))
                  throw new InvalidDataException("'" + result.Root + "' already exists, is not empty, and " +
                                                 "holds no ppcontent.json, so it is not a ContentTool project " +
                                                 "- pick another project name");

              Directory.CreateDirectory(result.Root);
              if (result.Created)
              {
                  var tree = new Dictionary<string, object>(StringComparer.Ordinal)
                  {
                      { "id", name }, { "bundle", name + ".bundle" }
                  };
                  AtomicFile.WriteText(result.ManifestPath,
                                       new JsonWriter().Val(tree).ToString() + "\n", new UTF8Encoding(false));
              }
              // THE STRICT READER IS THE ONLY GATE, and it runs BEFORE meta.json. Load refuses a manifest that
              // is not UTF-8, not JSON, has no "id"/"bundle" or declares a root key twice - so an authored file
              // this tool cannot edit safely is never edited at all, no meta is written beside it, and the
              // template above is proven readable by the same reader. Its id, not <name>, keys the mod.
              ManifestFile file = ManifestFile.Load(result.ManifestPath);
              string id = file.Manifest.Id;
              if (!File.Exists(result.MetaPath))
                  AtomicFile.WriteText(result.MetaPath, Meta(id), new UTF8Encoding(false));
              else
              {
                  string said = Package.MetaRefusal(File.ReadAllText(result.MetaPath), null);   // R13
                  if (said != null)
                      throw new InvalidDataException("'" + result.MetaPath + "' already exists but is not a " +
                                                     "mod this project can ship: " + said + " - fix that file, " +
                                                     "or ship into another project");
              }

              // IDEMPOTENT REUSE, not a refusal. A row that is EXACTLY this one is what the PREVIOUS press
              // left, and every "fix it and press Ship again" in the design walks straight into it - reading
              // that as R6 would make the retry the design promises impossible. R6 stays for a CONFLICTING
              // row (same target, different mesh), and it lands HERE, before the copy, so a press that cannot
              // add its row never leaves a .glb behind that nothing references.
              result.RowAlreadyPresent = Reuses(file.Manifest, shippedBundle, shippedAsset, stem);
              if (!result.RowAlreadyPresent)
              {
                  file.Manifest.AddMeshReplacement(shippedBundle, shippedAsset, stem);
                  file.Manifest.Validate();
              }
              // The splice, the .bak and the E5 fingerprint are ManifestFile's; nothing outside the "replace"
              // value span moves - and with nothing pending, Save validates and writes NOTHING (Manifest.cs:321).
              file.Save();

              if (!string.Equals(ContentMods.ProjectDir(modDir, name), result.Root,
                                 StringComparison.OrdinalIgnoreCase))
                  throw new IOException("'" + result.Root + "' was written but ContentMods.ProjectDir still does " +
                                        "not resolve '" + name + "' to it, so a bake would read the wrong folder");
              return result;
          }
  ```
  and, beside `Meta`, the one predicate that decides reuse from refusal:
  ```csharp
          /// <summary>Does the project ALREADY declare exactly this replacement? Each field folded the way the
          /// thing that will READ it folds: the bundle case-blind (ProjectBake.cs:1534, Manifest.Validate:203),
          /// the asset ORDINAL (shipped names are folded nowhere), the mesh stem case-blind because
          /// ProjectBake.FindMesh:2152 resolves it that way and two spellings are one file on Windows.</summary>
          private static bool Reuses(Manifest manifest, string bundle, string asset, string stem)
          {
              foreach (ReplaceRow row in manifest.Replace)
                  if (row.Kind == "mesh" &&
                      string.Equals(row.Bundle, bundle, StringComparison.OrdinalIgnoreCase) &&
                      string.Equals(row.Asset, asset, StringComparison.Ordinal) &&
                      string.Equals(row.Mesh, stem, StringComparison.OrdinalIgnoreCase))
                      return true;
              return false;
          }
  ```
  - Run: `dotnet run --project tests\ObjCodecTests -c Release`
  - Expected: `PROJECT-SCAFFOLD PASS, 48 check(s) - name table, project templates`; the run's last line is
    `DEMO BANKS: ALL PASS, 6 check(s)` and exit 0.

- [ ] **Step 3: Build.** `dotnet build -c Release` → `Ошибок: 0`, `Предупреждений: 1` (the known CS0649).

- [ ] **Step 4: Commit.**
  - `git -C E:\DEV\PhoenixPoint\ContentTool add src\Project\ProjectScaffold.cs tests\ObjCodecTests\ProjectScaffoldTests.cs && git -C E:\DEV\PhoenixPoint\ContentTool commit -m "feat(project): one press appends one mesh row through ManifestFile, an identical press reuses it, and a conflicting target is refused before anything is copied"`

---

### Task 3: the .glb copy policy (R3/R4/R5) and the sidecar beside the copy

- [ ] **Step 1: Write the failing gate.** In `ProjectScaffoldTests.Run()`, inside the `try`, after the
  `Scaffold_RefusesConflictingTarget` block:
  ```csharp
              // ---- Scaffold_MeshCollisionPolicy. The .glb under Content\Meshes\ is the bake's INPUT
              // (ProjectBake.FindMesh:1581), so overwriting one silently re-points a row an author already
              // shipped. This tool never overwrites it.
              string meshPath = Path.Combine(made.Root, "Content", "Meshes", "body.glb");
              checks += Check(File.Exists(meshPath) && Same(File.ReadAllBytes(meshPath), new byte[] { 1, 2, 3 }),
                              "the first press copied the .glb under Content\\Meshes\\ verbatim");
              ProjectScaffold.Result again = ProjectScaffold.AddMeshReplacement(
                  modDir, "Replace_Rifle", glb, sha, "px_equipment_assets_all.bundle", "WPN_PX_Stock", empty);
              checks += Check(again.MeshAlreadyPresent && again.MeshPath == meshPath &&
                              Same(File.ReadAllBytes(meshPath), new byte[] { 1, 2, 3 }),
                              "the SAME bytes under the same name are a no-op, not a rewrite");

              string clashDir = Path.Combine(dir, "clash");
              Directory.CreateDirectory(clashDir);
              string clashGlb = Path.Combine(clashDir, "body.glb");
              File.WriteAllBytes(clashGlb, new byte[] { 9, 9, 9, 9 });
              string clashSha = AliasMap.Sha256(File.ReadAllBytes(clashGlb));
              string said = null;
              try
              {
                  ProjectScaffold.AddMeshReplacement(modDir, "Replace_Rifle", clashGlb, clashSha,
                                                     "px_equipment_assets_all.bundle", "WPN_PX_Barrel", empty);
              }
              catch (IOException refused) { said = refused.Message; }
              checks += Check(said == "Content\\Meshes\\body.glb already holds DIFFERENT bytes (sha " + sha +
                                      " vs " + clashSha + "), so it was NOT overwritten - rename the file you " +
                                      "are shipping, or ship into another project",
                              "R4 verbatim: " + said);
              checks += Check(Same(File.ReadAllBytes(meshPath), new byte[] { 1, 2, 3 }),
                              "and the bytes already there are still the bytes there");

              // ---- R3: the source moved between the verdict and the press.
              string stale = null;
              try
              {
                  ProjectScaffold.AddMeshReplacement(modDir, "Replace_Rifle", glb, clashSha,
                                                     "a.bundle", "StaleFoo", empty);
              }
              catch (IOException refused) { stale = refused.Message; }
              checks += Check(stale == "'" + glb + "' changed on disk after its green verdict, so nothing was " +
                                       "written - pick it again, read the report, then press Ship again",
                              "R3 verbatim: " + stale);
              checks += Check(File.ReadAllText(made.ManifestPath)
                                  .IndexOf("StaleFoo", StringComparison.Ordinal) < 0,
                              "and R3 left no row behind - the manifest is saved only after the copy lands");

              // ---- Scaffold_RefusesAStaleSourceBeforeWriting. "nothing was written" has to be true of a
              // FIRST press too: the source is read and hashed before the folder exists, so a stale file
              // cannot leave an empty project the author now has to delete.
              string never = null;
              try
              {
                  ProjectScaffold.AddMeshReplacement(modDir, "NeverMade", glb, clashSha,
                                                     "a.bundle", "StaleFoo", empty);
              }
              catch (IOException refused) { never = refused.Message; }
              checks += Check(never != null && !Directory.Exists(Path.Combine(mods, "NeverMade")),
                              "R3 on a NEW name creates no folder at all: " + never);

              // ---- R5: a sidecar beside the copy that this session never saw.
              string lone = Path.Combine(dir, "lone.glb");
              File.WriteAllBytes(lone, new byte[] { 3, 3, 3 });
              string loneSha = AliasMap.Sha256(File.ReadAllBytes(lone));
              string loneCopy = Path.Combine(made.Root, "Content", "Meshes", "lone.glb");
              Directory.CreateDirectory(Path.GetDirectoryName(loneCopy));
              File.WriteAllText(AliasMap.SidecarPathOf(loneCopy), "{}");
              string stray = null;
              try
              {
                  ProjectScaffold.AddMeshReplacement(modDir, "Replace_Rifle", lone, loneSha,
                                                     "a.bundle", "Lone", empty);
              }
              catch (InvalidDataException refused) { stray = refused.Message; }
              checks += Check(stray == "lone.glb.aliases.json already sits beside the copy but this Doctor " +
                                       "session has no bone map, so the bake would silently use mappings you " +
                                       "never saw - delete it, or set the map",
                              "R5 verbatim: " + stray);
              File.Delete(AliasMap.SidecarPathOf(loneCopy));

              // ---- Scaffold_SidecarRoundTrips: the sidecar is keyed on the COPY, which is the file the bake
              // will hash (AliasMap.LoadSidecar:196), not on the source the author picked.
              var map = new Dictionary<string, string>(StringComparer.Ordinal)
              {
                  { "Bip01_Head", "head" }, { "Bip01_Neck", "neck" }
              };
              ProjectScaffold.Result withMap = ProjectScaffold.AddMeshReplacement(
                  modDir, "Replace_Rifle", lone, loneSha, "a.bundle", "Lone", map);
              string whyNot;
              AliasMap back = AliasMap.LoadSidecar(withMap.MeshPath,
                                                   AliasMap.Sha256(File.ReadAllBytes(withMap.MeshPath)),
                                                   out whyNot);
              checks += Check(back != null && whyNot == null,
                              "the sidecar loads against the COPY's own sha: " + whyNot);
              int mapped = 0;
              string wanted;
              foreach (KeyValuePair<string, string> pair in back.Pairs)
                  if (map.TryGetValue(pair.Key, out wanted) && wanted == pair.Value) mapped++;
              checks += Check(mapped == 2, "and both rows round-trip: " + mapped);
              checks += Check(withMap.SidecarPath == AliasMap.SidecarPathOf(withMap.MeshPath),
                              "the Result names the sidecar it wrote");
              checks += Check(withMap.MeshBytes != null &&
                              Same(withMap.MeshBytes, File.ReadAllBytes(withMap.MeshPath)),
                              "and Result.MeshBytes IS the copy's bytes - what the ship gate re-judges (§4.5)");
  ```
  - Run: `dotnet run --project tests\ObjCodecTests -c Release`
  - Expected: **FAIL at runtime** — Task 2's scaffold copies nothing, so the run ends with
    `Unhandled Exception: System.Exception: PROJECT-SCAFFOLD FAILURE: the first press copied the .glb under
    Content\Meshes\ verbatim` and a non-zero exit code.

- [ ] **Step 2: Implement.** In `src\Project\ProjectScaffold.cs`, replace the body of `AddMeshReplacement` once
  more — the same method with the design §4.2 order complete — and add `CopyOrVerify` beside `Meta`:
  ```csharp
          internal static Result AddMeshReplacement(string modDir, string name, string sourceGlb, string expectedSha,
                                                    string shippedBundle, string shippedAsset,
                                                    IDictionary<string, string> aliases)
          {
              string refusal = NameRefusal(name);
              if (refusal != null) throw new InvalidDataException(refusal);
              if (string.IsNullOrEmpty(sourceGlb) || !File.Exists(sourceGlb))
                  throw new FileNotFoundException("the .glb to ship is not on disk", sourceGlb ?? "");
              if (string.IsNullOrEmpty(shippedBundle) || string.IsNullOrEmpty(shippedAsset))
                  throw new InvalidDataException("no shipped target was derived for this slot, so there is no " +
                                                 "row to write - pick the slot again");

              DirectoryInfo mods = string.IsNullOrEmpty(modDir) ? null : Directory.GetParent(modDir);
              if (mods == null)
                  throw new InvalidDataException("ContentTool's own mod folder is not known, so there is nowhere " +
                                                 "beside it to put a project");

              var result = new Result();
              result.Root = Path.Combine(mods.FullName, name);
              result.ManifestPath = Path.Combine(result.Root, ContentMods.Manifest);
              result.MetaPath = Path.Combine(result.Root, "meta.json");
              string stem = Path.GetFileNameWithoutExtension(sourceGlb);
              result.MeshPath = Path.Combine(result.Root, "Content", "Meshes", stem + ".glb");
              result.SidecarPath = AliasMap.SidecarPathOf(result.MeshPath);
              result.Created = !File.Exists(result.ManifestPath);

              if (result.Created && Directory.Exists(result.Root) &&
                  (Directory.GetFiles(result.Root).Length != 0 ||
                   Directory.GetDirectories(result.Root).Length != 0))
                  throw new InvalidDataException("'" + result.Root + "' already exists, is not empty, and " +
                                                 "holds no ppcontent.json, so it is not a ContentTool project " +
                                                 "- pick another project name");

              // R3, AND IT COMES FIRST. The refusal says "nothing was written", so it has to be true: read and
              // hash the source before a directory, a template or a meta exists, and a press that fails here
              // leaves an author with no folder to delete. The Doctor's verdict was about THESE bytes; a
              // re-export between the green report and this press would ship a file nobody has read.
              byte[] bytes = File.ReadAllBytes(sourceGlb);
              string sha = AliasMap.Sha256(bytes);
              if (!string.Equals(sha, expectedSha, StringComparison.OrdinalIgnoreCase))
                  throw new IOException("'" + sourceGlb + "' changed on disk after its green verdict, so nothing " +
                                        "was written - pick it again, read the report, then press Ship again");
              result.MeshBytes = bytes;

              Directory.CreateDirectory(result.Root);
              if (result.Created)
              {
                  var tree = new Dictionary<string, object>(StringComparer.Ordinal)
                  {
                      { "id", name }, { "bundle", name + ".bundle" }
                  };
                  AtomicFile.WriteText(result.ManifestPath,
                                       new JsonWriter().Val(tree).ToString() + "\n", new UTF8Encoding(false));
              }

              ManifestFile file = ManifestFile.Load(result.ManifestPath);
              string id = file.Manifest.Id;
              if (!File.Exists(result.MetaPath))
                  AtomicFile.WriteText(result.MetaPath, Meta(id), new UTF8Encoding(false));
              else
              {
                  string said = Package.MetaRefusal(File.ReadAllText(result.MetaPath), null);   // R13
                  if (said != null)
                      throw new InvalidDataException("'" + result.MetaPath + "' already exists but is not a " +
                                                     "mod this project can ship: " + said + " - fix that file, " +
                                                     "or ship into another project");
              }

              result.RowAlreadyPresent = Reuses(file.Manifest, shippedBundle, shippedAsset, stem);
              if (!result.RowAlreadyPresent)
              {
                  file.Manifest.AddMeshReplacement(shippedBundle, shippedAsset, stem);
                  file.Manifest.Validate();                   // R6 before any copy
              }

              // R5. A sidecar already beside the copy, with an empty map in hand, would be applied by the bake
              // and by nothing the author ever looked at. SaveSidecar rewrites the whole "bones" object, so the
              // only safe answers are "write mine" or "refuse".
              if ((aliases == null || aliases.Count == 0) && File.Exists(result.SidecarPath))
                  throw new InvalidDataException(stem + ".glb.aliases.json already sits beside the copy but this " +
                                                 "Doctor session has no bone map, so the bake would silently use " +
                                                 "mappings you never saw - delete it, or set the map");

              Directory.CreateDirectory(Path.GetDirectoryName(result.MeshPath));
              result.MeshAlreadyPresent = CopyOrVerify(result.MeshPath, bytes, sha, stem);
              if (aliases != null && aliases.Count != 0)
                  AliasMap.SaveSidecar(result.MeshPath, sha, bytes.LongLength, aliases);

              // LAST, deliberately: a manifest row pointing at a mesh file that is not there yet is the one
              // half-written state a retry cannot fix by pressing again (design §7, stages 6-8).
              file.Save();

              if (!string.Equals(ContentMods.ProjectDir(modDir, name), result.Root,
                                 StringComparison.OrdinalIgnoreCase))
                  throw new IOException("'" + result.Root + "' was written but ContentMods.ProjectDir still does " +
                                        "not resolve '" + name + "' to it, so a bake would read the wrong folder");
              return result;
          }

          /// <summary>True when the destination already held these exact bytes. R4 otherwise: the .glb under
          /// Content\Meshes\ is an authored input, and PatchCache.Key stamps it by path/size/mtime (:43/:49),
          /// so a same-size overwrite would be INVISIBLE to the freshness check and the player would keep being
          /// served last bake's copy.
          ///
          /// "Absent" is decided by the CREATE ITSELF, never by a File.Exists that another writer can falsify
          /// between the question and the write (Codex finding 4): AtomicFile.Write ends in File.Replace, which
          /// would happily overwrite a file created in that window - the one thing this method exists to
          /// forbid. FileMode.CreateNew is the stdlib's own create-only-or-fail, one line and atomic; the loser
          /// of a race re-reads the winner and judges it by the same SHA, so two presses agree.</summary>
          private static bool CopyOrVerify(string meshPath, byte[] bytes, string sha, string stem)
          {
              try
              {
                  using (var made = new FileStream(meshPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                      made.Write(bytes, 0, bytes.Length);
                  return false;
              }
              catch (IOException) when (File.Exists(meshPath)) { }
              string have = AliasMap.Sha256(File.ReadAllBytes(meshPath));
              if (string.Equals(have, sha, StringComparison.OrdinalIgnoreCase)) return true;
              throw new IOException("Content\\Meshes\\" + stem + ".glb already holds DIFFERENT bytes (sha " +
                                    have + " vs " + sha + "), so it was NOT overwritten - rename the file you " +
                                    "are shipping, or ship into another project");
          }

          /// <summary>The absent-only twin of AtomicFile.WriteText, for the two TEMPLATES. Same reason as
          /// CopyOrVerify: the upsert writer must never be the one deciding "it was not there a moment ago".
          /// A file that appeared in the meantime is left exactly as its writer left it, and the caller reads
          /// it back - ManifestFile.Load for the manifest, Json.Parse + Package.MetaRefusal for the meta -
          /// so the winner is validated rather than trusted.</summary>
          private static void CreateNew(string path, string text)
          {
              byte[] bytes = new UTF8Encoding(false).GetBytes(text);
              try
              {
                  using (var made = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                      made.Write(bytes, 0, bytes.Length);
              }
              catch (IOException) when (File.Exists(path)) { }
          }
  ```
  and the two template writes become absent-only calls — `AtomicFile.WriteText` is the UPSERT writer and is
  never used for a file that must not already exist:
  ```csharp
              if (result.Created) CreateNew(result.ManifestPath, new JsonWriter().Val(tree).ToString() + "\n");
              ...
              if (!File.Exists(result.MetaPath)) CreateNew(result.MetaPath, Meta(id));
              else { /* Json.Parse + Package.MetaRefusal, R13 - Task 2 Step 0 */ }
  ```
  The `else` arm already re-reads and validates whatever is there, so a meta that appeared in the race is
  judged, not overwritten; `ManifestFile.Load` on the next line does the same for the manifest.
  - Run: `dotnet run --project tests\ObjCodecTests -c Release`
  - Expected: `PROJECT-SCAFFOLD PASS, 60 check(s) - name table, project templates`; last line
    `DEMO BANKS: ALL PASS, 6 check(s)`, exit 0.

- [ ] **Step 3: Build.** `dotnet build -c Release` → `Ошибок: 0`, `Предупреждений: 1`.

- [ ] **Step 4: Commit.**
  - `git -C E:\DEV\PhoenixPoint\ContentTool add src\Project\ProjectScaffold.cs tests\ObjCodecTests\ProjectScaffoldTests.cs && git -C E:\DEV\PhoenixPoint\ContentTool commit -m "feat(project): the shipped .glb is copied, never overwritten, and its sidecar is keyed on the copy"`

---

### Task 4: `Route7.ApplyProject` — reachable, and refusing to install a bake nobody vouched for

**Runs AFTER Task 5** (Codex sequencing, accepted): Task 5 defines the `forBundle` value this task branches on, and
`ApplyProject`'s behaviour change is global — the console verb `ct_route7 apply` sees it too — so it lands
immediately adjacent to its first caller (Task 6's `DoShip`) and to its acceptance evidence (**W5**, Task 8 step
2.10) rather than sitting un-exercised across several commits.

Unity-only: `ApplyProject` calls `ContentProject.Load`, `ProjectBake.Run` and `BundleLive.Install`, none of which
run outside the player. The gate here is the compiler; the behaviour is proved by **W5** in Task 8.

- [ ] **Step 1: Make it reachable, make a failed bake terminal, and answer WHAT HAPPENED as a value.** In
  `src\Bake\Route7.cs`, INSIDE the `Route7` class (so it is `Route7.ApplyDisposition` to every caller), above
  `ApplyProject`'s doc comment at `:198`:
  ```csharp
          /// <summary>What became of ONE bundle in an apply. A wizard cannot read this out of the log: zero
          /// claims taken is not the same fact as residency - a catalog Locate failure (BundleLive.cs:215-218)
          /// and an ownership conflict (BundleClaims.Claim:250) also take no claim, and reporting either of
          /// those as "restart and enable" is the tool telling the author something untrue with a straight
          /// face.</summary>
          internal enum ApplyDisposition { Redirected, Resident, Refused, BakeFailed }
  ```
  then, at `:205`, the signature becomes a pair — the console verb keeps calling the one-argument form, whose
  printed output is byte-for-byte what it is today:
  ```csharp
          internal static string ApplyProject(string projectName)
          {
              ApplyDisposition ignored;
              return ApplyProject(projectName, null, out ignored);
          }

          /// <param name="forBundle">the ONE shipped bundle the caller cares about, or null for the console
          /// verb, which prints the log and asks nothing.</param>
          internal static string ApplyProject(string projectName, string forBundle, out ApplyDisposition how)
  ```
  Its first statement, so every `return` below is definitely assigned:
  ```csharp
              how = ApplyDisposition.Refused;
  ```
  Replace the block at `:249-256` (the whole `int failed; ... else pre.AppendLine(...)` sequence) with:
  ```csharp
                  // ONE BUTTON MUST NOT INSTALL AN UNVOUCHED BAKE. Leaving the freshness key unwritten was
                  // enough while a human read the log and decided; the Doctor's Ship row presses this for them,
                  // and "the copies below are whatever the last good bake produced" is not something a wizard
                  // gets to do quietly. The COUNT, never the text - a reworded sentence must not change what
                  // is installed.
                  int failed;
                  pre.AppendLine(ProjectBake.Run(projectRoot, out failed));
                  if (failed != 0)
                  {
                      how = ApplyDisposition.BakeFailed;
                      return pre.AppendLine("NOT APPLIED: the bake reported " + failed + " failure(s); fix the " +
                                            "lines above and press Ship again").ToString();
                  }
                  Project.PatchCache.Write(patched, key);
  ```
  and the final `return` (`:280-281`) becomes — the answer taken from LIVE STATE around the install, never from the
  text just produced, and **in `Register`'s own order** (Codex finding 5):
  ```csharp
              // RESIDENCY IS READ BEFORE THE INSTALL, because that is the order Register:80-92 decides in: it
              // refuses a resident bundle BEFORE it looks at claims. A press made after an earlier redirect has
              // already loaded would otherwise find this mod's own stale claim, report Redirected, and print S2
              // ("redirected LIVE") over a log that says "restart required" - the wizard lying with a straight
              // face, which is the whole reason this is a value and not a grep of the log.
              bool wasResident = !string.IsNullOrEmpty(forBundle) && BundleLive.ResidentNow(forBundle);
              pre.Append("installing " + copies.Count + " patched copy(ies) as '" + modId + "'\n")
                 .Append(BundleLive.Install(modId, copies));
              if (!string.IsNullOrEmpty(forBundle))
              {
                  BundleClaim mine = BundleClaims.Find(forBundle);
                  how = wasResident ? ApplyDisposition.Resident
                      : mine != null && string.Equals(mine.Mod, modId, StringComparison.Ordinal)
                        ? ApplyDisposition.Redirected
                        : ApplyDisposition.Refused;
              }
              return pre.ToString();
  ```

- [ ] **Step 2: The residency fact, asked once, where it already lives.** In `src\Bake\BundleLive.cs`, after
  `Holds` (`:145`):
  ```csharp
          /// <summary>Is the shipped bundle open RIGHT NOW? The same two steps Register:80-92 takes - the live
          /// location first, because only its AssetBundleRequestOptions knows the BUILD name Unity loaded it
          /// under (a 32-hex hash in this game, :230-238). Asked here rather than re-derived in Route7: this
          /// project has already shipped that comparison wrong once, and one copy of it is one too many.</summary>
          internal static bool ResidentNow(string bundleFile)
          {
              string why;
              IResourceLocation loc = Locate(bundleFile, out why);
              AssetBundleRequestOptions opts = loc == null ? null : loc.Data as AssetBundleRequestOptions;
              string who;
              return opts != null && Resident(opts.BundleName, out who);
          }
  ```
  - Run: `dotnet build -c Release`
  - Expected: `Ошибок: 0`, `Предупреждений: 1` (the known `GlbCodec.cs(59,23) CS0649`). Nothing else in the repo
    calls `ApplyProject` except `Route7.cs:50` (`ct_route7 apply`) and `:109` (the enable path), and both keep
    compiling against the one-argument overload — widening `private` to `internal` breaks no caller, and neither
    file needs a new `using` (`BundleClaim`/`BundleClaims` are `Morgott.ContentTool.Bake`, and `BundleLive.cs`
    already names `IResourceLocation` and `AssetBundleRequestOptions`).
  - Deferred to Task 8: **W5** — force a bad row into a project, press Ship, and confirm the panel's last line is
    R11, `BundleLive.Holds(<id>)` is false and `ct-cache.key` was not written.

- [ ] **Step 3: The offline gates still pass.** Run `dotnet run --project tests\ObjCodecTests -c Release` →
  `PROJECT-SCAFFOLD PASS, 60 check(s) - name table, project templates` present, last line
  `DEMO BANKS: ALL PASS, 6 check(s)`, exit 0; and `dotnet run --project tests\TargetPathTests -c Release` →
  last line `R0: ALL PASS`, exit 0.

- [ ] **Step 4: Commit.**
  - `git -C E:\DEV\PhoenixPoint\ContentTool add src\Bake\Route7.cs src\Bake\BundleLive.cs && git -C E:\DEV\PhoenixPoint\ContentTool commit -m "fix(bake): a bake that reported failures installs nothing, and an apply says what became of one bundle as a value"`

---

### Task 5: `ShippedTarget` — the row's target, derived from the addon graph and proved on disk

**Runs BEFORE Task 4** (Codex sequencing), and **lands as TWO green commits, not one** (Codex finding 16 — the task
is ~300 lines, past a reviewable diff): (a) **the carrier** — step 1's three `PrototypeTarget` fields, which
compile and change no behaviour on their own; (b) **the resolver and its plumbing** — step 2's `ShippedTarget.cs`
plus step 3's `LiveSlots`/`Retarget` change, which is the first thing that can call it. Each commit builds
`Ошибок: 0` on its own; the task boundary and the step numbering do not change.

Unity + AssetsTools, so it cannot be test-linked; the gate is `dotnet build -c Release` and the proof is **W4** in
Task 8. Never `AssetBundle.GetAllLoadedAssetBundles() + Contains`: a loaded bundle's `name` is the BUILD identity,
not the shipped file name (`BundleLive.cs:230-238`), meshes are commonly sub-assets, and a global scan throws away
the one thing that makes the answer trustworthy — the owning dependency graph.

- [ ] **Step 1: Carry the target on the slot.** In `src\Doctor\PrototypeTarget.cs`, after `Unavailable` (`:43`):
  ```csharp
          /// <summary>The SHIPPED pair this slot's renderer replaces, derived by
          /// <see cref="ShippedTarget.Resolve"/> when the bay rebuild produced the renderer: the .bundle FILE
          /// name as BakeSelfCheck.ShippedBundlePath resolves it, and the Mesh's ordinal m_Name. Null until it
          /// is derived, and null forever when it could not be - see <see cref="TargetRefusal"/>.</summary>
          internal string ShippedBundle;
          internal string ShippedAsset;

          /// <summary>Why no shipped pair could be derived for this slot - the sentence the panel shows in place
          /// of a target. Stored rather than thrown: one unresolvable slot must not cost the author the other
          /// slots' rows.</summary>
          internal string TargetRefusal;
  ```

- [ ] **Step 2: Write the derivation.** Create `src\Doctor\ShippedTarget.cs`:
  ```csharp
  using System;
  using System.Collections;
  using System.Collections.Generic;
  using System.IO;
  using System.Reflection;
  using AssetsTools.NET.Extra;
  using Base.Assets;
  using Base.Core;
  // BOTH namespaces above declare an AssetsManager (Base.Assets.AssetsManager is the game's component;
  // AssetsTools.NET.Extra.AssetsManager comes in with BundleBaker), so the bare name is CS0104-ambiguous and
  // the file does not compile without this alias. Spell GameAssetsManager in the variable, the generic
  // GameUtl.GameComponent call and the typeof - all three (Codex finding 1).
  using GameAssetsManager = Base.Assets.AssetsManager;
  using Morgott.ContentTool.Bake;
  using PhoenixPoint.Common.Entities.Addons;
  using UnityEngine;
  using UnityEngine.AddressableAssets;
  using UnityEngine.AddressableAssets.ResourceLocators;
  using UnityEngine.ResourceManagement.ResourceLocations;
  using UnityEngine.ResourceManagement.ResourceProviders;

  namespace Morgott.ContentTool.Doctor
  {
      /// <summary>
      /// WHICH SHIPPED BUNDLE AND WHICH SHIPPED MESH the slot standing on the bench actually is - the pair a
      /// "replace" row needs, derived from the live addon that built the renderer and then PROVED against the
      /// bundle files on disk.
      ///
      /// Three steps, each refusing rather than guessing:
      ///   1. the addon's own dependency graph (AddonDef.SkinData -> AssetReference), walked by the GAME'S OWN
      ///      reflection pass (AssetsManager.GetAssetReferencesFromObject, AssetsManager.cs:316), keeping the
      ///      reference whose .Asset IS the prefab this addon built its visuals from (Addon.cs:179);
      ///   2. that reference's runtime key through Addressables.ResourceLocators - the walk BundleLive.Locate
      ///      (:199-213) does, keyed on ONE key instead of every key - then its locations' Dependencies, where a
      ///      dependency carrying AssetBundleRequestOptions names a .bundle file;
      ///   3. per candidate present on disk, BundleBaker.WhyNot(Mesh, name) - the very call ProjectBake.Patch
      ///      makes at :1588 - which must answer null for EXACTLY ONE of them.
      /// The stored pair is therefore by construction what Patch matches: the bundle case-blind (:1534), the
      /// asset ordinal through the same WhyNot.
      /// </summary>
      internal static class ShippedTarget
      {
          /// <summary>Null on success, having filled <paramref name="target"/>'s ShippedBundle/ShippedAsset; the
          /// refusal sentence otherwise, which is also stored on the target. NEVER throws: it runs once per slot
          /// inside the bay rebuild, and one unresolvable slot must not take the rebuild down with it.</summary>
          internal static string Resolve(Addon addon, SkinnedMeshRenderer smr, PrototypeTarget target)
          {
              target.ShippedBundle = null;
              target.ShippedAsset = null;
              target.TargetRefusal = null;
              try
              {
                  // R14. Each refusal below names ONE cause, because "none of the bundles holds it" was a lie
                  // in every branch that had no bundles to begin with, and an author cannot act on a sentence
                  // describing a step that never ran.
                  if (addon == null || smr == null || smr.sharedMesh == null)
                      return Refuse(target, "TARGET REFUSED: this slot has no live mesh, so there is no shipped " +
                                            "Mesh name to look for");
                  string asset = smr.sharedMesh.name;

                  List<string> files;
                  string why = BundlesOf(addon, out files);          // R15, R16, R17, R18, R19
                  if (why != null) return Refuse(target, why);

                  // THE DERIVATION LINE W4 IS PROVED BY (Codex finding 8). A successful resolve used to log
                  // nothing, so "exactly one candidate answered null" was unfalsifiable after the fact: the
                  // manifest row and the later patch line prove only that the CHOSEN pair works, never that no
                  // second holder existed. Every deduplicated candidate is named here, with what WhyNot said
                  // about it - "holds it" for the one that answered null included.
                  Debug.Log("[ContentTool] ShippedTarget: '" + asset + "' candidates (" + files.Count + "): " +
                            Spell(files));

                  string last = null;
                  int present = 0, opened = 0;
                  var holders = new List<string>();
                  foreach (string file in files)
                  {
                      string shipped = BakeSelfCheck.ShippedBundlePath(file);
                      if (!File.Exists(shipped))
                      {
                          Debug.Log("[ContentTool] ShippedTarget:   " + file + ": not shipped by this install");
                          continue;
                      }
                      present++;
                      try
                      {
                          // ponytail: one BundleBaker per candidate per slot - fine for the handful a slot
                          // depends on, O(bundles) if the panel ever resolves every slot eagerly. Cache by
                          // bundle file then.
                          using (BundleBaker baker = new BundleBaker(shipped, "ct.doctor"))
                          {
                              string gone = baker.WhyNot(AssetClassID.Mesh, asset);
                              opened++;                              // the archive answered, whatever it said
                              if (gone == null) holders.Add(file); else last = gone;
                              Debug.Log("[ContentTool] ShippedTarget:   " + file + ": " +
                                        (gone == null ? "HOLDS IT (WhyNot == null)" : gone));
                          }
                      }
                      catch (Exception ex)
                      {
                          last = file + ": " + ex.GetType().Name + " - " + ex.Message;
                          Debug.Log("[ContentTool] ShippedTarget:   " + last);
                      }
                  }

                  if (holders.Count == 1)
                  {
                      target.ShippedBundle = holders[0];
                      target.ShippedAsset = asset;
                      Debug.Log("[ContentTool] ShippedTarget: resolved '" + asset + "' -> " + holders[0] +
                              " (1 of " + present + " present candidate(s) answered WhyNot == null)");
                      return null;
                  }
                  if (holders.Count > 1)
                      return Refuse(target, R9(asset, holders));     // R9
                  if (present == 0)
                      return Refuse(target, "TARGET REFUSED: this install ships none of the bundles this addon " +
                                            "loads (" + Spell(files) + ") - verify the game files, then show " +
                                            "the prototype again");                                     // R20
                  if (opened == 0)
                      return Refuse(target, "TARGET REFUSED: every bundle this addon loads refused to open (" +
                                            Spell(files) + ") - " + last);                              // R21
                  return Refuse(target, "TARGET REFUSED: none of the bundles this addon loads holds a Mesh named '" +
                                        asset + "' - " + last);                                         // R10
              }
              catch (Exception ex)
              {
                  // R22. The panel gets a sentence, the log gets the stack - the same split
                  // ModelDoctor.Tick:391 makes.
                  Debug.LogError("[ContentTool] ShippedTarget: " + ex);
                  return Refuse(target, "TARGET REFUSED: the addon's dependency graph could not be walked (" +
                                        ex.GetType().Name + ": " + ex.Message + ") - see Player.log for the stack");
              }
          }

          private static string Refuse(PrototypeTarget target, string sentence)
          {
              target.TargetRefusal = sentence;
              return sentence;
          }

          private static string R9(string asset, List<string> holders)
          {
              return "TARGET REFUSED: a Mesh named '" + asset + "' is in " + holders.Count + " of the bundles " +
                     "this addon loads (" + string.Join(", ", holders.ToArray()) + ") - ContentTool will not " +
                     "guess which one the game means";
          }

          /// <summary>Every shipped .bundle FILE NAME the addon's visual prefab is served out of, or the ONE
          /// sentence naming which step could not answer: no graph (R15), no reference (R16), several
          /// references (R17), no locator (R18), or a graph that names no bundle (R19).</summary>
          private static string BundlesOf(Addon addon, out List<string> files)
          {
              files = new List<string>();
              GameObject prefab = addon.VisualsSourcePrefab;
              AddonDef def = addon.AddonDef;
              object skin = def == null ? null : def.SkinData;
              if (prefab == null || skin == null)
                  return "TARGET REFUSED: this slot's addon carries no SkinData or was not built from a " +
                         "prefab, so there is no dependency graph to walk";                                 // R15

              var matched = new List<AssetReference>();
              var guids = new List<string>();
              foreach (AssetReference reference in References(skin))
              {
                  if (reference == null || !ReferenceEquals(reference.Asset, prefab)) continue;
                  matched.Add(reference);
                  string guid = reference.AssetGUID ?? "";
                  if (!guids.Contains(guid)) guids.Add(guid);
              }
              if (matched.Count == 0)
                  return "TARGET REFUSED: this addon's SkinData reaches no AssetReference whose asset is the " +
                         "prefab it built, so ContentTool cannot tell which shipped bundle serves this slot"; // R16
              if (guids.Count > 1)
                  return "TARGET REFUSED: this addon's SkinData reaches " + guids.Count + " different " +
                         "AssetReference GUIDs for the prefab it built (" + Spell(guids) + ") - ContentTool " +
                         "will not guess which one the game means";                                         // R17

              object key = matched[0].RuntimeKey;
              var visited = new List<IResourceLocation>();
              bool located = false;
              foreach (IResourceLocator locator in Addressables.ResourceLocators)
              {
                  if (locator == null) continue;
                  IList<IResourceLocation> found;
                  if (!locator.Locate(key, null, out found) || found == null || found.Count == 0) continue;
                  located = true;
                  foreach (IResourceLocation location in found) Walk(location, files, visited);
              }
              if (!located)
                  return "TARGET REFUSED: no live Addressables locator answers this addon's prefab key '" +
                         key + "' - either the catalog has not initialised yet, or this prefab is not served " +
                         "from a bundle at all";                                                            // R18
              if (files.Count == 0)
                  return "TARGET REFUSED: the locations behind this addon's prefab name no .bundle at all - " +
                         "nothing in that dependency graph carries AssetBundleRequestOptions";              // R19
              return null;
          }

          /// <summary>A location's own bundle and every bundle it depends on, spelled the way
          /// BundleClaims.Matches:191 compares and BakeSelfCheck.ShippedBundlePath:735 resolves - the FILE name.
          /// VISITED SET, not a depth cap: a real catalog graph is a diamond as often as a tree, so a cap deep
          /// enough for the diamonds is no cycle guard and a cap tight enough to guard is one that silently
          /// truncates. Identity, not Equals - the same rule BundleLive.Consider:226 applies, because an
          /// IResourceLocation implementation is free to define equality however it likes.</summary>
          private static void Walk(IResourceLocation location, List<string> files, List<IResourceLocation> visited)
          {
              if (location == null) return;
              foreach (IResourceLocation seen in visited) if (ReferenceEquals(seen, location)) return;
              visited.Add(location);
              if (location.Data is AssetBundleRequestOptions)
              {
                  string file = Path.GetFileName(location.InternalId ?? "");
                  if (file.Length != 0 && !Has(files, file)) files.Add(file);
              }
              if (location.Dependencies == null) return;
              foreach (IResourceLocation dependency in location.Dependencies) Walk(dependency, files, visited);
          }

          /// <summary>Case-BLIND, because these are Windows file names and Patch folds them the same way
          /// (ProjectBake.cs:1534). Two locations spelling one file differently would otherwise be opened
          /// twice and counted as two holders, turning a resolvable slot into R9.</summary>
          private static bool Has(List<string> files, string file)
          {
              foreach (string had in files)
                  if (string.Equals(had, file, StringComparison.OrdinalIgnoreCase)) return true;
              return false;
          }

          private static string Spell(List<string> items) { return string.Join(", ", items.ToArray()); }

          /// <summary>The game's OWN public-field walk, by reflection because it is an internal INSTANCE method
          /// (AssetsManager.cs:316). Using it rather than a copy means this sees exactly what
          /// AcquireDependenciesAsync sees.
          ///
          /// THROWS rather than answering empty when the INFRASTRUCTURE is missing (Codex finding 9): no
          /// AssetsManager component, no such method, a null result. Folding those into an empty list made the
          /// caller print R16 - "this addon's SkinData reaches no AssetReference whose asset is the prefab it
          /// built" - which is a statement about this addon's DATA and sends the author to inspect a def that is
          /// perfectly fine. They are the tool's own footing giving way, so they belong to the outer catch: R22,
          /// with the stack in Player.log. R16 is reserved for a walk that RAN and matched nothing.
          /// ponytail: copy the :339-381 field walk if a game update ever breaks the lookup.</summary>
          private static IEnumerable<AssetReference> References(object skinData)
          {
              var found = new List<AssetReference>();
              GameAssetsManager manager = GameUtl.GameComponent<GameAssetsManager>();
              if (manager == null)
                  throw new InvalidOperationException("no live Base.Assets.AssetsManager component");
              MethodInfo walk = typeof(GameAssetsManager).GetMethod(
                  "GetAssetReferencesFromObject", BindingFlags.Instance | BindingFlags.NonPublic,
                  null, new[] { typeof(object), typeof(Type[]) }, null);
              if (walk == null)
                  throw new MissingMethodException("Base.Assets.AssetsManager",
                                                   "GetAssetReferencesFromObject(object, Type[])");
              IEnumerable produced = walk.Invoke(manager, new object[] { skinData, null }) as IEnumerable;
              if (produced == null)
                  throw new InvalidOperationException(
                      "AssetsManager.GetAssetReferencesFromObject returned no enumerable");
              foreach (object item in produced)
              {
                  AssetReference reference = item as AssetReference;
                  if (reference != null) found.Add(reference);
              }
              return found;
          }
      }
  }
  ```

- [ ] **Step 3: Keep the owning `Addon` beside the renderer.** In `src\Dev\FitBench.cs`, `LiveSlots` (`:768`)
  becomes:
  ```csharp
          /// <summary>Slot def name -&gt; the renderer THIS rebuild produced for it, WITH the addon that owns
          /// it. Restricted to <c>proto.Slots()</c> so a renderer left over from whatever stood here before can
          /// never be snapshotted as this prototype's. The addon comes along because it - not the renderer -
          /// owns the dependency graph <see cref="ShippedTarget.Resolve"/> derives the shipped target from, and
          /// it is already in hand here.</summary>
          private static Dictionary<string, KeyValuePair<Addon, SkinnedMeshRenderer>> LiveSlots()
          {
              var found = new Dictionary<string, KeyValuePair<Addon, SkinnedMeshRenderer>>(StringComparer.Ordinal);
              try
              {
                  var produced = new HashSet<SkinnedMeshRenderer>(proto.Slots());
                  AddonsManager manager = bay.CharacterBuilder.AddonsManager;
                  if (manager == null || manager.RootAddon == null) return found;
                  foreach (Addon a in manager.RootAddon)
                  {
                      if (a == null || a.VisualRoot == null) continue;
                      AddonSlot slot = a.ParentSlot;
                      if (slot == null || slot.SlotDef == null || found.ContainsKey(slot.SlotDef.name)) continue;
                      foreach (SkinnedMeshRenderer smr in a.VisualRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                      {
                          if (smr == null || smr.sharedMesh == null || !produced.Contains(smr)) continue;
                          found[slot.SlotDef.name] = new KeyValuePair<Addon, SkinnedMeshRenderer>(a, smr);
                          break;
                      }
                  }
              }
              catch (Exception ex) { message = "ct_bench: slots - " + ex.GetType().Name + ": " + ex.Message; }
              return found;
          }
  ```
  and its one caller, the loop body inside `Retarget` (`:747-762`), becomes:
  ```csharp
              Dictionary<string, KeyValuePair<Addon, SkinnedMeshRenderer>> live = LiveSlots();
              Transform root = bay.CharacterBuilder.transform;
              foreach (PrototypeSlot slot in variant.Slots)
              {
                  var target = new PrototypeTarget
                  {
                      Record = record, Variant = variant,
                      SlotDefName = slot.SlotDefName, Mode = VerifyMode.Replace
                  };
                  KeyValuePair<Addon, SkinnedMeshRenderer> made;
                  if (slot.SlotDefName != null && live.TryGetValue(slot.SlotDefName, out made) && made.Value != null)
                  {
                      target.Live = ModelDoctor.Snapshot(made.Value, SeamSwap.RelativePath(root, made.Value.transform));
                      // Stored, never thrown: the row that could not be derived says why, and every other slot
                      // in this rebuild still gets its target.
                      ShippedTarget.Resolve(made.Key, made.Value, target);
                  }
                  else
                      target.Unavailable = "slot visual unavailable";
                  slotTargets.Add(target);
              }
  ```
  - Run: `dotnet build -c Release`
  - Expected: `Ошибок: 0`, `Предупреждений: 1` (the known CS0649). `FitBench.cs` already imports
    `PhoenixPoint.Common.Entities.Addons` (it names `Addon`, `AddonSlot` and `AddonsManager` at `:774-780`) and
    `Morgott.ContentTool.Doctor` (it constructs `PrototypeTarget`), so neither file needs a new `using`.
  - Deferred to Task 8: **W4** — via PPCLI, `connect call` on `FitBench.SlotTargets()` reads a slot's
    `ShippedBundle`/`ShippedAsset` after a prototype is shown, and step 5 asserts the row the bake matched is that
    same pair.

- [ ] **Step 4: The offline gates still pass.** `dotnet run --project tests\ObjCodecTests -c Release` →
  last line `DEMO BANKS: ALL PASS, 6 check(s)`, exit 0, `PROJECT-SCAFFOLD PASS, 60 check(s)` present.

- [ ] **Step 5: Commit.**
  - `git -C E:\DEV\PhoenixPoint\ContentTool add src\Doctor\ShippedTarget.cs src\Doctor\PrototypeTarget.cs src\Dev\FitBench.cs && git -C E:\DEV\PhoenixPoint\ContentTool commit -m "feat(doctor): derive a slot's shipped bundle and mesh from the addon graph, and prove it with the bake's own WhyNot"`

---

### Task 6: the Doctor's SHIP row — one intent, a two-frame gate, and the honest end state

**Lands as THREE green commits, not one** (Codex finding 16 — the task is over 300 lines): (a) **the fingerprint**
— step 2 whole, `SameRigAs`/`SameAs` plus its offline arm, which is green on its own; (b) **the gate and its
state** — step 1's `Intent.Ship`, the snapshot fields, `Enqueue`, the two-frame gate and the `Dispose` clearing
below; (c) **`DoShip` and the panel** — steps 3 and 4. Each commit builds `Ошибок: 0` and leaves the offline gates
green; the task boundary and the step numbering do not change.

Unity-only (IMGUI, `Route7`, `ReplacementPreflight` against a live renderer). Gate: `dotnet build -c Release`;
proof: Task 8 steps 3-6 and **W6**/**W7**.

- [ ] **Step 1: The intent, the snapshot fields and the gate.** In `src\Dev\ModelDoctor.cs`:

  At `:29`:
  ```csharp
          private enum Intent { Preview, Revert, Save, SkelPlan, Ship }
  ```

  After `internal string Message = "";` (`:70`):
  ```csharp
          // ---------------------------------------------------------------- SHIP
          /// <summary>The project name the author is editing. Seeded from the resolved target on the first
          /// Layout pass that has one, then owned by the text field.</summary>
          private string projectName = "";
          /// <summary>The two-frame gate. The bake blocks the main thread for seconds, so the label has to be
          /// PAINTED before it starts: Tick N+1 arms, Draw paints during Repaint, Tick N+2 runs. SlimPanel's
          /// volatile-snapshot pattern does not apply here - no worker changes state between Layout and
          /// Repaint, the main thread simply stops.</summary>
          private bool shipPending, shipLabelPainted;
          private string shipPhase = "", shipResult = "", shipPath = "", shipTail = "";
          /// <summary>Everything the run needs, copied when the intent drains, so a click on the browser while
          /// the bake runs cannot change what is being shipped.</summary>
          private string shipName, shipSource, shipSha, shipBundle, shipAsset;
          private Dictionary<string, string> shipAliases;
          private PrototypeTarget shipProto;
          private RigTarget shipTargetWas;
          private SkinnedMeshRenderer shipRenderer;
          /// <summary>The Doctor generation this press was armed on (`gen`, `:58`, bumped by Restart:269, by
          /// the slot change at :660 and by Dispose:1668). The two-frame gate spans a frame the AUTHOR can act
          /// in - retarget, pick another file, close the bench - and every one of those moves `gen`, so an
          /// armed press whose generation no longer matches is abandoned before it writes anything.</summary>
          private int shipGen = -1;
  ```

  At `:228-234`, `Enqueue` gains one line:
  ```csharp
          internal void Enqueue(string what)
          {
              if (what == "preview") intents.Enqueue(Intent.Preview);
              else if (what == "revert") intents.Enqueue(Intent.Revert);
              else if (what == "save") intents.Enqueue(Intent.Save);
              else if (what == "skelplan") intents.Enqueue(Intent.SkelPlan);
              else if (what == "ship") intents.Enqueue(Intent.Ship);
          }
  ```

  In `Tick`, the intent loop (`:399-406`) becomes, and the gate is drained after it:
  ```csharp
              Intent intent;
              while (intents.TryDequeue(out intent))
              {
                  if (intent == Intent.Preview) Message = DoPreview();
                  else if (intent == Intent.Revert) Message = Revert();
                  else if (intent == Intent.SkelPlan) Message = DoWriteSkelPlan();
                  else if (intent == Intent.Ship) ArmShip();
                  else Message = DoSave();
              }

              // FRAME N+2. The label armed above has been painted by now, so the freeze happens under a panel
              // that already says it is happening.
              if (shipPending && shipLabelPainted)
              {
                  shipPending = false;
                  shipLabelPainted = false;
                  DoShip();
              }
  ```

  And, in `Dispose` (`:1664`), beside the queues it already drains — an armed press MUST NOT survive the bench
  closing (Codex finding 7). Without this, a Ship armed on the frame the bench closed runs on the next Doctor,
  against a renderer that is gone and a project name nobody typed:
  ```csharp
              shipPending = false;
              shipLabelPainted = false;
              shipGen = -1;
              shipName = shipSource = shipSha = shipBundle = shipAsset = null;
              shipAliases = null;
              shipProto = null;
              shipTargetWas = null;
              shipRenderer = null;
              projectName = "";
              shipPhase = shipResult = shipPath = shipTail = "";
  ```
  `Dispose` already bumps `gen` (`:1668`), so the generation check in `DoShip` would catch it too; clearing the
  fields as well is what stops the NEXT Doctor from opening on the last one's result text.

- [ ] **Step 2: The fingerprint a live preview does not break — RED FIRST** (Codex finding 15). The arm and the
  implementation used to arrive in one step, so the arm was never observed failing and proved nothing about
  itself. Write the arm below into `ProjectScaffoldTests.Run()` FIRST, run
  `dotnet build tests\ObjCodecTests\ObjCodecTests.csproj -c Release`, and record the failure:
  `error CS1061: 'RigTarget' does not contain a definition for 'SameRigAs'` (four occurrences, one per call).
  Only then, in `src\Import\SkinCompatibility.cs`, replace `RigTarget.SameAs` (`:70-82`) with the pair — the same
  comparison, split at the seam that matters — and rerun to green:
  ```csharp
          /// <summary>Everything about the target that is NOT the mesh: which renderer, where it sits in the
          /// hierarchy, and what its bones are called. A live Doctor preview puts a mesh WE built onto the
          /// renderer (ModelDoctor.cs:451) while Target still describes the game's own (PickTarget:120/:142),
          /// so with a preview up the MESH half of SameAs is guaranteed to differ and says nothing about
          /// whether the rig moved. A caller holding the preview by reference proves that half itself.</summary>
          internal bool SameRigAs(RigTarget other)
          {
              if (other == null) return false;
              if (RendererInstanceId != other.RendererInstanceId ||
                  !string.Equals(TransformPath, other.TransformPath, StringComparison.Ordinal)) return false;
              if (BoneNames == null || other.BoneNames == null) return BoneNames == other.BoneNames;
              if (BoneNames.Length != other.BoneNames.Length) return false;
              for (int i = 0; i < BoneNames.Length; i++)
                  if (!string.Equals(BoneNames[i], other.BoneNames[i], StringComparison.Ordinal)) return false;
              return true;
          }

          /// <summary>The rig AND the mesh on it. Delegating rather than repeating is the point: two
          /// hand-written comparisons drift, and the one they would drift over is the one the preview path
          /// depends on. The null check lives in SameRigAs, so the && below is short-circuited before any
          /// member of `other` is read.</summary>
          internal bool SameAs(RigTarget other)
          {
              return SameRigAs(other) && MeshInstanceId == other.MeshInstanceId &&
                     BindPoseCount == other.BindPoseCount && Rigged == other.Rigged &&
                     string.Equals(MeshName, other.MeshName, StringComparison.Ordinal);
          }
  ```
  `SkinCompatibility.cs` IS linked into `ObjCodecTests` (`.csproj:190`), so the split is proved offline, not only
  in game. In `ProjectScaffoldTests.Run()`, after the `Scaffold_SidecarRoundTrips` block:
  ```csharp
              // ---- Fingerprint_APreviewIsNotAChangedRig. The R8 seam, on plain data: what a live Doctor
              // preview does to a snapshot is change the MESH half and nothing else.
              var was = new RigTarget
              {
                  RendererInstanceId = 7, TransformPath = "Root/Body", MeshName = "Body",
                  MeshInstanceId = 11, BindPoseCount = 3, Rigged = true, BoneNames = new[] { "a", "b" }
              };
              // ALL FOUR mesh-derived fields differ (Codex finding 14): with BindPoseCount and Rigged left
              // equal, the arm would pass even if SameRigAs still compared them, and the split it claims to
              // prove would be two fields short.
              var previewing = new RigTarget
              {
                  RendererInstanceId = 7, TransformPath = "Root/Body", MeshName = "ours.glb",
                  MeshInstanceId = 12, BindPoseCount = 0, Rigged = false, BoneNames = new[] { "a", "b" }
              };
              checks += Check(!previewing.SameAs(was) && previewing.SameRigAs(was),
                              "all four mesh fields change SameAs and NOT SameRigAs - the whole R8 split");
              var elsewhere = new RigTarget
              {
                  RendererInstanceId = 8, TransformPath = "Root/Body", MeshName = "Body",
                  MeshInstanceId = 11, BindPoseCount = 3, Rigged = true, BoneNames = new[] { "a", "b" }
              };
              checks += Check(!elsewhere.SameRigAs(was), "a DIFFERENT renderer is still a changed rig");
              var renamed = new RigTarget
              {
                  RendererInstanceId = 7, TransformPath = "Root/Body", MeshName = "Body",
                  MeshInstanceId = 11, BindPoseCount = 3, Rigged = true, BoneNames = new[] { "a", "c" }
              };
              checks += Check(!renamed.SameRigAs(was), "and so is a renamed bone");
              checks += Check(was.SameAs(was) && !was.SameAs(null),
                              "SameAs still answers itself, and still refuses null");
  ```
  - Run: `dotnet build -c Release`, then `dotnet run --project tests\ObjCodecTests -c Release`
  - Expected: `Ошибок: 0`, `Предупреждений: 1`; then `PROJECT-SCAFFOLD PASS, 64 check(s) - name table, project
    templates`. Every existing caller of `SameAs` (`ModelDoctor.cs:377`, `:421`, and the Doctor's own guards)
    keeps compiling and keeps its old meaning — this step only ADDS a weaker comparison beside it.

- [ ] **Step 3: The run itself.** In `src\Dev\ModelDoctor.cs`, after `DoWriteSkelPlan` (`:523`):
  ```csharp
          /// <summary>FRAME N+1: take a copy of every input and put the panel into its "working" state. Nothing
          /// is written here - the point of this frame is that it ends with a repaint.</summary>
          private void ArmShip()
          {
              if (shipPending) return;
              shipName = (projectName ?? "").Trim();
              shipSource = Path;
              shipSha = Ready == null ? null : Ready.Sha256;
              shipBundle = Prototype == null ? null : Prototype.ShippedBundle;
              shipAsset = Prototype == null ? null : Prototype.ShippedAsset;
              shipAliases = new Dictionary<string, string>(aliases, StringComparer.Ordinal);
              shipProto = Prototype;
              shipTargetWas = Target;
              shipRenderer = Renderer;
              shipGen = gen;                      // the generation this press belongs to
              shipResult = ""; shipPath = ""; shipTail = "";
              shipPhase = "creating the project, baking and applying - the game freezes for a few seconds";
              shipPending = true;
              shipLabelPainted = false;
          }

          /// <summary>
          /// FRAME N+2, and every byte of it. Order is design §4.5: the source is re-read and compared against
          /// the verdict's own hash, the project is written, the COPY is re-judged, the renderer is re-snapshotted,
          /// and only then does the bake run. That binds the VERDICT to what is on disk - the bake's own target
          /// lookup, bundle I/O and material-slot mapping can still refuse, and its result stays authoritative.
          ///
          /// Nothing is rolled back after a failure: the copy, the sidecar and the row are authored project
          /// state, cheap and retryable on the next press, and a three-writer rollback would be the more
          /// dangerous code (design §7).
          /// </summary>
          private void DoShip()
          {
              shipPhase = "";
              // CANCEL BEFORE THE FIRST BYTE (Codex finding 7). One frame stands between arming and running,
              // and the author owns it: retargeting bumps gen at :660, picking another file bumps it through
              // Restart:269, closing the bench bumps it at :1668. Shipping the snapshot anyway would write a
              // project for the OLD slot under a panel already showing the new one - a mod folder the author
              // never asked for, named after a target they have moved away from.
              if (shipGen != gen)
              {
                  shipGen = -1;
                  shipResult = "the slot or the file changed before the bake started, so nothing was written - " +
                               "press Ship again";
                  return;
              }
              shipGen = -1;
              string root = null;
              try
              {
                  // R3 belongs to the SCAFFOLD, which raises it BEFORE it creates a directory, a manifest or a
                  // meta - which is what lets its sentence say "nothing was written" and be true. Re-reading
                  // the source here as well would only hash the same file twice and answer the same question.
                  ProjectScaffold.Result made = ProjectScaffold.AddMeshReplacement(
                      ContentToolMain.ModDir, shipName, shipSource, shipSha, shipBundle, shipAsset, shipAliases);
                  root = made.Root;
                  shipPath = made.Root;

                  // R7. The bake reads the COPY, so the COPY is what has to be green - including the sidecar
                  // that was just written beside it. made.MeshBytes ARE the copy's bytes: the scaffold hashed
                  // them against the verdict before writing, and CopyOrVerify proved an existing file equal to
                  // them. Judging those is judging what is on disk, without re-opening the question.
                  ReplacementPreflightResult copied =
                      ReplacementPreflight.Run(made.MeshBytes, made.MeshPath, shipProto);
                  if (copied.Outcome != Outcome.ByName || copied.Report.Count(Severity.Blocking) != 0)
                  {
                      shipResult = "the COPIED glb did not re-read green (" + copied.Outcome + "), so nothing was " +
                                   "baked - the project on disk is complete, fix the file and press Ship again";
                      return;
                  }

                  // R8, AND IT HAS TO KNOW ABOUT THE PREVIEW. Target was snapshotted when the slot was picked;
                  // DoPreview:451 then put OUR mesh on that renderer. A plain SameAs is therefore false for the
                  // whole time a preview is on screen - which is exactly the state an author ships from, so a
                  // naive guard would refuse every real press and W6 could never pass. With a preview live the
                  // mesh's IDENTITY is not evidence about the rig; that the mesh is OUR preview object is.
                  RigTarget now = shipTargetWas == null ? null : Snapshot(shipRenderer, shipTargetWas.TransformPath);
                  bool same = now != null && (HasPreview
                      ? ReferenceEquals(shipRenderer == null ? null : shipRenderer.sharedMesh, preview) &&
                        now.SameRigAs(shipTargetWas)
                      : now.SameAs(shipTargetWas));
                  if (!same)
                  {
                      shipResult = "the slot's renderer changed while Ship was running, so nothing was baked - " +
                                   "pick the slot again";
                      return;
                  }

                  // ApplyProject and NOT ProjectBake.Run: it loads the project, computes PatchCache.Key, re-bakes
                  // when stale and installs, and Run does not write the freshness key - calling both would bake
                  // twice. The ABSOLUTE root is idempotent through ContentToolMain.ProjectDir (:208,
                  // ContentMods.cs:146), so the two cannot disagree about which folder was baked. The DISPOSITION
                  // is asked for, not read out of the log: zero claims taken can mean residency, a catalog
                  // Locate failure or another mod owning that bundle, and only one of the three is S1.
                  Bake.Route7.ApplyDisposition how;
                  string log = Bake.Route7.ApplyProject(made.Root, shipBundle, out how);
                  ContentToolMain.Say(log);
                  shipTail = Tail(log, 10);
                  if (how == Bake.Route7.ApplyDisposition.BakeFailed)
                      shipResult = Tail(log, 1);          // R11 - the line ApplyProject returned on
                  else if (how == Bake.Route7.ApplyDisposition.Resident)
                      // S1, THE NORMAL OUTCOME. The bay rendered this very mesh, so the bundle is resident and
                      // BundleLive.Register:88 refuses before taking a claim. No forced unload: it would pull the
                      // archive out from under live objects, which is what that refusal exists to prevent.
                      shipResult = "baked OK - restart the game and enable '" + shipName + "' in the mod manager. " +
                                   "Phoenix Point already loaded " + shipBundle + ", so this session keeps showing " +
                                   "your Doctor preview.";
                  else if (how == Bake.Route7.ApplyDisposition.Redirected)
                      shipResult = "baked and redirected LIVE - " + shipBundle + " now loads from the patched copy " +
                                   "on the next load";
                  else                                    // R23
                      shipResult = "baked, but NOT APPLIED: " + shipBundle + " was neither redirected nor already " +
                                   "loaded - the log above names the refusal; the project folder is complete and " +
                                   "can be enabled after a restart";
              }
              catch (InvalidDataException refused) { shipResult = refused.Message; }   // R1, R2, R5, R6, R13
              catch (IOException refused) { shipResult = refused.Message; }            // R3, R4, E5, E6
              catch (Exception ex)                                                     // R12
              {
                  // OBSERVED, never assumed. This catch is reachable BEFORE anything exists - a modDir that
                  // resolves nowhere, a source that cannot be read - so it asks the disk rather than sending an
                  // author to look at a folder that was never created.
                  string where = root ?? ProjectScaffold.RootOf(ContentToolMain.ModDir, shipName);
                  bool there = where != null && Directory.Exists(where);
                  shipResult = "SHIP THREW: " + ex.GetType().Name + ": " + ex.Message + " - " +
                               (there
                                ? "'" + where + "' is on disk and the files already written there were retained"
                                : "no project folder was created") + "; see Player.log for the stack";
                  Debug.LogError("[ContentTool] Model Doctor Ship: " + ex);
              }
          }

          /// <summary>The last few lines of the bake log, for the panel. The WHOLE log went to
          /// ContentToolMain.Say, which is where an author reads the rows one by one.
          ///
          /// THE TRAILING EMPTY ELEMENT IS DISCARDED BEFORE THE COUNT (Codex finding 6). ApplyProject ends in
          /// AppendLine, so Split('\n') always produces one empty element at the end; taking "the last 1" then
          /// selected that empty string and Tail(log, 1) answered "", which is exactly the R11 path - the panel
          /// would report a failed bake with a BLANK result line. Trim the tail first, then take N.</summary>
          private static string Tail(string log, int lines)
          {
              if (string.IsNullOrEmpty(log)) return "";
              string[] all = log.Replace("\r\n", "\n").Split('\n');
              int end = all.Length;
              while (end > 0 && all[end - 1].Length == 0) end--;      // the AppendLine's own empty tail
              var kept = new StringBuilder();
              for (int i = Math.Max(0, end - lines); i < end; i++)
                  if (all[i].Length != 0) kept.AppendLine(all[i]);
              return kept.ToString().TrimEnd();
          }
  ```
  **Where the exact-R11 arm lives.** Codex asked for a unit arm on this; `ModelDoctor.cs` is NOT linked into
  `ObjCodecTests` (`ObjCodecTests.csproj` links `Package.cs:83`, `Json.cs:145`, `SkinCompatibility.cs:192` and no
  Dev file), and `Tail` is `private static` inside it — an offline arm would mean linking a Unity-dependent file
  into the gate to test five lines. So the assertion is made where the string is actually produced: **Task 8 step
  2.10 (W5)** compares the panel's `shipResult` byte for byte with the string the SHIPPED code produces —
  `Tail(log, 1)` (`ModelDoctor.cs:702`, `:745`) glued to `" Fix the lines above and press Ship again."`, whose
  first half is `Route7`'s own NOT APPLIED line (`Route7.cs:341-343`):
  `NOT APPLIED: patching the shipped bundle(s) reported <n> failure(s), named in the P0/REFUSED line(s) above; nothing was installed and no copy was marked current. Fix the lines above and press Ship again.`
  — a blank or truncated result there fails W5. (Restated from disk at HEAD `578843a` on 2026-09-05; the earlier
  draft of this paragraph quoted a string no longer in the code.)

- [ ] **Step 4: The panel row.** In `src\Dev\ModelDoctor.cs`, in `Draw`, immediately after
  `GUILayout.EndHorizontal();` that closes the Preview/Revert/Save/Skel-plan row (`:1264`) and before the closing
  brace of `Draw` (`:1265`):
  ```csharp
              Ship();
  ```
  and, in the same row, **every control that can move the target is dead while a press is armed** (Codex finding
  7): the gate spans one frame the author can act in, and retargeting mid-press is what the generation check
  above then has to throw the press away for. Each `GUI.enabled =` at `:1246`, `:1250`, `:1254`, `:1259` and
  `:1261` gains `!shipPending &&` in front of its condition (`:1261` becomes `GUI.enabled = !shipPending;`), so
  Preview / Revert preview / Save aliases / Write skel plan / Copy report all grey out for that one frame.
  and, after `Draw`:
  ```csharp
          /// <summary>
          /// SHIP: from a green verdict to a mod folder the player can switch on, in one press. Read-and-enqueue
          /// like every other control here - the only thing it writes is its own text field and the repaint flag
          /// the two-frame gate waits for.
          /// </summary>
          private void Ship()
          {
              GUILayout.Space(6f);
              GUILayout.Label("SHIP - write a mod folder beside ContentTool, bake it, apply it");

              GUILayout.BeginHorizontal();
              GUILayout.Label("project", GUILayout.Width(56f));
              // Seeded on LAYOUT only: a value that changed between Layout and Repaint is how an IMGUI pass ends
              // up unbalanced.
              if (Event.current.type == EventType.Layout && projectName.Length == 0 &&
                  Prototype != null && Prototype.ShippedAsset != null)
                  projectName = ProjectScaffold.DefaultName(Prototype.ShippedAsset);
              projectName = GUILayout.TextField(projectName ?? "", GUILayout.Width(220f));
              GUILayout.Label(Prototype != null && Prototype.ShippedBundle != null
                              ? "target " + Prototype.ShippedBundle + " / " + Prototype.ShippedAsset
                              : (Prototype != null && Prototype.TargetRefusal != null
                                 ? Prototype.TargetRefusal
                                 : "no shipped target derived for this slot"));
              GUILayout.EndHorizontal();

              // ponytail: File.Exists on every OnGUI pass - two stats a frame on a local file. Cache it in
              // Refresh() (Layout only) if a profile ever shows it.
              string refusal = ProjectScaffold.NameRefusal(projectName);
              bool ready = Ready != null && Ready.Outcome == Outcome.ByName &&
                           Ready.Report.Count(Severity.Blocking) == 0 &&
                           Prototype != null && Prototype.Mode == VerifyMode.Replace && Prototype.Live != null &&
                           Prototype.TargetRefusal == null && Prototype.ShippedBundle != null &&
                           Renderer != null && Path != null && File.Exists(Path) &&
                           Path.EndsWith(".glb", StringComparison.OrdinalIgnoreCase) &&
                           refusal == null && !Busy && !shipPending;

              GUILayout.BeginHorizontal();
              GUI.enabled = ready;
              if (GUILayout.Button("CREATE, BAKE & APPLY", GUILayout.Width(200f))) Enqueue("ship");
              GUI.enabled = true;
              GUILayout.Label(shipPending ? shipPhase : (refusal ?? ""));
              GUILayout.EndHorizontal();

              // ALWAYS DRAWN, placeholder or not (design §4.4 "Rows, always drawn"; Codex finding 17). A row
              // that appears only once it has content makes the section jump under the author's cursor at the
              // exact moment they are reading a result, and an IMGUI layout that changes shape between one
              // press and the next is also how a Layout/Repaint pair ends up unbalanced.
              GUILayout.Label(shipPath.Length > 0 ? "project " + shipPath : "project -");
              GUILayout.Label(shipResult.Length > 0 ? shipResult : "-");
              GUILayout.Label(shipTail.Length > 0 ? shipTail : "-");

              // THE SECOND HALF OF THE GATE, and Repaint only: a Layout pass paints nothing, so arming on it
              // would let the freeze start under a panel that still says nothing.
              if (Event.current.type == EventType.Repaint && shipPending) shipLabelPainted = true;
          }
  ```
  - Run: `dotnet build -c Release`
  - Expected: `Ошибок: 0`, `Предупреждений: 1` (the known CS0649). `ModelDoctor.cs` already imports
    `System.Collections.Generic`, `System.IO`, `System.Text`, `Morgott.ContentTool.Doctor`,
    `Morgott.ContentTool.Import`, `Morgott.ContentTool.IO` and `UnityEngine` (`:1-10`); `ProjectScaffold` is in
    `Morgott.ContentTool.Project`, so add `using Morgott.ContentTool.Project;` to the top of the file. `Path` is
    this class's own `internal string Path` field, which is why `System.IO.Path` is already spelled in full at
    `:1275-1278` and why `Path.EndsWith` above reads a string.
  - Deferred to Task 8: steps 3-6 (the button pressed through `Enqueue("ship")`, the folder on disk, the log lines,
    S1) and **W6**/**W7** after the restart.

- [ ] **Step 5: The offline gates still pass.** `dotnet run --project tests\ObjCodecTests -c Release` → last line
  `DEMO BANKS: ALL PASS, 6 check(s)`, exit 0, `PROJECT-SCAFFOLD PASS, 64 check(s)` present; `dotnet run --project
  tests\TargetPathTests -c Release` → last line `R0: ALL PASS`, exit 0. `PreflightTests` and `DecisionGolden` lean
  on `SameAs` through `ReplacementPreflight`, so their staying green is what proves the split changed no answer.

- [ ] **Step 6: Commit.**
  - `git -C E:\DEV\PhoenixPoint\ContentTool add src\Dev\ModelDoctor.cs src\Import\SkinCompatibility.cs tests\ObjCodecTests\ProjectScaffoldTests.cs && git -C E:\DEV\PhoenixPoint\ContentTool commit -m "feat(doctor): one SHIP button turns a green verdict into a baked, applied mod folder, preview and all"`

---

### Task 7: the offline gates, and the acceptance table's offline half

- [x] **Step 1: Name the arm honestly.** In `ProjectScaffoldTests.Run()`, change the return to:
  ```csharp
          return "PROJECT-SCAFFOLD PASS, " + checks + " check(s) - name table, project templates, row append " +
                 "and reuse, mesh collision policy, sidecar, rig fingerprint";
  ```
  - Run: `dotnet run --project tests\ObjCodecTests -c Release`
  - Expected: `PROJECT-SCAFFOLD PASS, 79 check(s) - name table, project templates, row append and reuse, mesh
    collision policy, sidecar, rig fingerprint`, last line `DEMO BANKS: ALL PASS, 6 check(s)`, exit 0.
    (Drafted as 64; the review-fix commits of Tasks 2–6 added arms. 79 is what the run printed.)

- [x] **Step 2: Every build and every gate, from clean.**
  - `dotnet build -c Release` → `Ошибок: 0`, `Предупреждений: 1` (`GlbCodec.cs(59,23) CS0649`).
  - `dotnet run --project tests\ObjCodecTests -c Release` → every section line PASS, no line reads FAIL, last line
    `DEMO BANKS: ALL PASS, 6 check(s)`, exit 0.
  - `dotnet run --project tests\TargetPathTests -c Release` → last line `R0: ALL PASS`, exit 0.
  - `dotnet build tools\Package\Package.csproj -c Release` → `Ошибок: 0`. (It links `Package.cs`, `Json.cs`,
    `ImportRefused.cs`, `AtomicFile.cs` and `Manifest.cs` — not `ProjectScaffold.cs`, which nothing in that tool
    calls. Do NOT add it.)
  **Baseline known failures — NOT part of W1** (Codex finding 18). W1 asks for GREEN gates, and this slice
  neither owns nor repairs these two; listing them under "every build and every gate" made a green W1
  unachievable by definition. Run them as a REGRESSION check that the breakage did not widen, and record the
  result beside W1 rather than inside it:
  - `dotnet build tools\ClipEvents\ClipEvents.csproj -c Release` and
    `dotnet build tools\SpiderAxisCheck\SpiderAxisCheck.csproj -c Release` → still exactly ONE error each,
    `GlbReader.cs(6,27): error CS0234` on `Morgott.ContentTool.Bake`, the pre-existing breakage this slice does not
    own and did not widen. A SECOND error in either is a regression this slice owns; repairing the first one is a
    separately scoped prerequisite, not this task.

- [x] **Step 3: Record the offline half of design §10, in this file.** Append the table below under a new
  `## Task 7 acceptance run` heading, filled with what the run actually printed — command, last line, exit code —
  and mark **W4**, **W5**, **W6** and **W7** `pending`, not passed: they are Task 8.
  - **W1** offline gates green: the FOUR commands of step 2 (build, `ObjCodecTests`, `TargetPathTests`,
    `Package.csproj`). The two standalone tools are recorded under **baseline known failures**, outside W1.
  - **W2** the scaffold is exact: `Scaffold_CreatesProjectTemplates` (meta.json compared against a template spelled
    independently in the test file, plus the `com.test"quote` id arm from Task 2 Step 0) +
    `Scaffold_KeepsAnAuthoredId` (the meta carries the MANIFEST's id) + `Scaffold_AppendsSecondRow` — the append is
    made into a HAND-WRITTEN manifest carrying a BOM, an unknown member and a nested value, the `replace` span is
    located independently in the before and after texts, prefix AND suffix outside it are compared byte for byte,
    and the original row is still one unbroken run INSIDE the new span (Codex finding 12).
  - **W3** no overwrite is possible: `Scaffold_MeshCollisionPolicy` (same SHA → `MeshAlreadyPresent`, different SHA
    → R4, destination bytes unchanged) + `Scaffold_RefusesConflictingTarget` (R6, manifest bytes identical, no .glb
    copied) + `Scaffold_RefusesAnUnrelatedFolder` (R2) + `Scaffold_RefusesAnUnshippableMeta` (R13, the file not
    rewritten) + `Scaffold_RefusesAStaleSourceBeforeWriting` (R3 creates no folder).
  - **W3b** a retry is a retry: `Scaffold_ReusesAnIdenticalRow` in a FRESH project — the identical replacement run
    twice leaves EXACTLY ONE row, no R6, and byte-identical manifest state after run two (Codex finding 13).

- [x] **Step 4: Commit.**
  - `git -C E:\DEV\PhoenixPoint\ContentTool add tests\ObjCodecTests\ProjectScaffoldTests.cs internal-docs\planning\2026-09-02-replace-mesh-wizard-plan.md && git -C E:\DEV\PhoenixPoint\ContentTool commit -m "test(project): name what the scaffold gate proves, and close the offline half of the acceptance table"`

---

## Task 7 acceptance run

Run 2026-09-05 at HEAD `578843a`, from clean (`obj\`/`bin\` of the main project and of all three
sub-projects deleted first), `E:\DEV\PhoenixPoint\ContentTool`, PowerShell.

| Command | Last / verdict line, verbatim | Exit |
|---|---|---|
| `dotnet build -c Release` | `Предупреждений: 1` / `Ошибок: 0` — the only warning is `src\Import\GlbCodec.cs(59,23): warning CS0649` on `SampledClip.Looping` | 0 |
| `dotnet run --project tests\ObjCodecTests -c Release` | `DEMO BANKS: ALL PASS, 6 check(s)`; no line reads FAIL as a verdict | 0 |
| `dotnet run --project tests\TargetPathTests -c Release` | `R0: ALL PASS` | 0 |
| `dotnet build tools\Package\Package.csproj -c Release` | `Предупреждений: 0` / `Ошибок: 0` | 0 |

Section lines this slice is answerable for, verbatim from the `ObjCodecTests` run:

```
REFUSAL-COUNT PASS, 16 check(s) - 5 refusals, 5 failures
ALIAS PASS, 32 check(s) - simultaneous rename, untouched index tables, sidecar policy
PACKAGE-GATE PASS, 7 check(s)
MANIFEST PASS, 53 check(s) - atomic write, nested rows, byte-preserving splice, E3/E4/E5/E6/E8 refusals
PROJECT-SCAFFOLD PASS, 79 check(s) - name table, project templates, row append and reuse, mesh collision policy, sidecar, rig fingerprint
```

**Baseline known failures — outside W1, regression check only.** Both standalone tools still fail with
EXACTLY ONE error each, the same pre-existing one this slice neither owns nor widened:

```
dotnet build tools\ClipEvents\ClipEvents.csproj -c Release       -> Ошибок: 1
dotnet build tools\SpiderAxisCheck\SpiderAxisCheck.csproj -c Release -> Ошибок: 1
src\Import\GlbReader.cs(6,27): error CS0234: ... "Bake" ... в пространстве имен "Morgott.ContentTool"
```

### The acceptance table, offline half

| id | Check | Evidence |
|---|---|---|
| W1 | Offline gates green | The four commands in the table above, all exit 0: build `Ошибок: 0` / `Предупреждений: 1` (CS0649 only), `ObjCodecTests` last line `DEMO BANKS: ALL PASS, 6 check(s)` with `PROJECT-SCAFFOLD PASS, 79 check(s)` present, `TargetPathTests` last line `R0: ALL PASS` (incl. the `S14-owntemp` / `S14-owntemp-not` arms at `tests\TargetPathTests\Program.cs:1213`, `:1216`, which pin `Package.IsOwnTemp`'s GUID-N `.tmp` exemption), `Package.csproj` `Ошибок: 0`. The two standalone tools are recorded above as **baseline known failures**, outside this row. |
| W2 | Scaffold is exact | `PROJECT-SCAFFOLD PASS, 79 check(s)`. Arms: `Scaffold_CreatesProjectTemplates` `tests\ObjCodecTests\ProjectScaffoldTests.cs:69` (meta.json compared against the template spelled independently at `:649`), `Scaffold_QuotesAnAuthoredId` `:219` (the `com.test"quote` id), `Scaffold_KeepsAnAuthoredId` `:146`, `Scaffold_AppendsSecondRow` `:261` — the append into a hand-written manifest carrying a BOM, an unknown member and a nested value, `replace` span located independently before and after, prefix and suffix byte-compared, the original row one unbroken run inside the new span. |
| W3 | No overwrite is possible | Same gate line. Arms: `Scaffold_MeshCollisionPolicy` `:397` (same SHA → `MeshAlreadyPresent`; different SHA → R4, destination bytes unchanged), `Scaffold_RefusesConflictingTarget` `:355` (R6 == `Manifest.Validate` E4, manifest bytes identical, no .glb copied), `Scaffold_RefusesAnUnrelatedFolder` `:235` (R2), `Scaffold_RefusesAnUnshippableMeta` `:162` (R13, the file not rewritten), `Scaffold_RefusesAStaleSourceBeforeWriting` `:481` (R3 creates no folder), plus `Scaffold_RefusesASameStemMeshUnderAnotherExtension` `:592` and `Scaffold_WritesNoMetaUntilTheRowLands` `:379`. |
| W3b | A retry is a retry — OFFLINE HALF ONLY | `Scaffold_ReusesAnIdenticalRow` `:325`, in a FRESH project: the identical replacement run twice leaves EXACTLY ONE row, no R6, byte-identical manifest state after run two; `Scaffold_ValidatesTheManifestOnTheReUSED row too` `:618`. **The in-game half (the second press of Ship) is PENDING Task 8 step 8.** |
| — | R8 seam, offline | `Fingerprint_APreviewIsNotAChangedRig` `:560`: all four mesh-derived fields differ → `SameAs` false AND `SameRigAs` true; a different renderer `:577` and a renamed bone `:583` are still a changed rig. This is what lets Task 8 ship with the preview live. |
| W4 | Target derivation disk-proved | **PENDING Task 8** — `ShippedTarget` needs `UnityEngine` + `Base.Assets` + `BundleBaker` and is not test-linked; the build gate above is its only offline evidence. |
| W5 | A failed bake installs nothing | **PENDING Task 8** (step 2.10, separate never-applied project). |
| W6 | Honest end state, with the preview up | **PENDING Task 8** (steps 3 + 6 + 9). |
| W7 | Owner visual check | **PENDING Task 8** (after the step-9 restart). |

---

### Task 8: in-game acceptance on `D:\PP-Instance2` via PPCLI (**W3b + W4 + W5 + W6 + W7**)

> **Expectations restated from disk at HEAD `578843a` (2026-09-05), by Task 7.** Where this section and the
> shipped code once disagreed, the code wins and the text below has been corrected:
> - **R11 / the NOT APPLIED line.** `Route7.cs:341-343` prints
>   `NOT APPLIED: patching the shipped bundle(s) reported <n> failure(s), named in the P0/REFUSED line(s) above; nothing was installed and no copy was marked current.`
>   and the wizard's R11 is `Tail(log, 1)` + `" Fix the lines above and press Ship again."` (`ModelDoctor.cs:702`).
>   Step 2.10 below quotes the composed string; the earlier draft quoted a string no longer in the code.
> - **A refused target is `Unproven`, not a silent skip.** `ShippedTarget.Resolve` refuses an ambiguous or
>   unreadable candidate rather than naming another bundle: `TARGET REFUSED: '<file>' could not answer whether it
>   holds a Mesh named '<asset>' (<why>) - ...` (`ShippedTarget.cs:173-176`), and the negative marker is
>   `bakers[path] = null`. In step 2 the chosen slot's `TargetRefusal` must be null; a slot carrying any
>   `TARGET REFUSED:` text is a different slot, not a failure of W4.
> - **`Route7.Failed` is a per-SESSION set** (`Route7.cs:94`, added at `:337`, cleared at `:397`) consulted ONLY by
>   `Toggle` (`:120`), not by `ApplyProject`. So step 8's second press of Ship still bakes normally, but after
>   step 2.10's deliberate failure the mod-manager checkbox for THAT project prints
>   `'<id>' failed to bake earlier in this session - not baking it again. Fix the lines it printed, then <RetryHint>`
>   — expected, not a defect.
> - **The ship arm is cancelled by `Tick` after two unpainted ticks** (`SHIP: cancelled - the SHIP section was not
>   on screen ...`). Keep the Doctor's SHIP section visible between `Enqueue("ship")` and the poll, or the arm
>   cancels itself and the result line says so.
> - **`Package.IsOwnTemp`** exempts the tool's own GUID-N `.tmp` in `Occupied` and `CopyDir`, so a leftover of that
>   shape under the project folder is not "someone else's work" and does not refuse.

The only proof for three of the four seams: `ShippedTarget` reads the LIVE Addressables catalog and the LIVE addon
graph, the SHIP row is IMGUI, and `ApplyProject` ends in `BundleLive`. **Do not mark the slice done before this
task is green.**

**Command source: `E:\DEV\PhoenixPoint\PPCLI\PLAYBOOK.md`.** Read it and take the exact invocations from there;
this plan deliberately spells none, because a stale command line in a plan is worse than no command line.
`D:\PP-Instance2` is the automation install — never the user's own game at
`D:\Steam\steamapps\common\Phoenix Point`.

- [ ] **Step 1: Build, deploy, activate.** `dotnet build -c Release`, then install the built
  `bin\Release\ContentTool\ContentTool.dll` + `meta.json` into `D:\PP-Instance2`'s mods folder with no game
  running. Confirm `com.morgott.ContentTool` is in that profile's `MOD_ACTIVATED` array
  (`…LocalLow\Snapshot Games Inc\Phoenix Point\Steam\76561197996210592\Options.jopt` — **Instance2's OWN profile**;
  the user's game is `…591`, so an edit here never reaches it); **editing that array is
  allowed on Instance2 and forbidden on the user's own game** — the count is duplicated in
  `ArrayDimensions.CollectionValues` and must match. Take a byte copy of that one file before the first edit and
  keep it beside the run's notes. (Codex finding 11 asked for a full restore-and-hash-verify ritual and a
  dedicated automation profile; **rejected** — `…592` IS the dedicated automation profile, and the snapshot is
  the whole ceremony.) A deploy that silently leaves the old DLL makes every result below a ghost.

- [ ] **Step 2: Drive it.** Launch that install through PPCLI and wait until `connect state` actually answers
  before sending anything. Then, in order — the design §8 sequence:
  1. `connect state` answers; start a campaign; open the bench (`ct_bench`).
  2. Through `call`, `FitBench.ShowPrototype`, wait until `PrototypeBusy` is false, take one `SlotTargets()` entry
     and read its `ShippedBundle` / `ShippedAsset` (and `TargetRefusal`, which must be null for the slot chosen).
  3. Through `call` on the bench's `doctor`: `PickFile(<glb>)`, `PickTarget(target)`, poll until
     `Ready.Outcome == ByName` with zero Blocking rows, then `Enqueue("preview")` and confirm `HasPreview` is
     true. **The ship is performed with the preview LIVE** — that is the state an author actually presses from,
     it is what W6 asks for, and before the §4.5 fingerprint split it was refused as R8 every single time.
  4. Set the project-name field, `Enqueue("ship")`, poll the ship result.
  5. On disk: `D:\PP-Instance2\Mods\<name>\ppcontent.json` + `meta.json` + `Content\Meshes\<stem>.glb` + the
     sidecar, and the row's `bundle`/`asset` are the pair step 2 resolved. `meta.json`'s `"ID"` equals
     `ppcontent.json`'s `"id"`, and its `Dependencies` name `com.morgott.ContentTool`.
  6. In `Player.log`: the `patch <bundle>: mesh '<asset>' <- <stem> ...` line,
     `ct_project: ALL PASS - this project has no bundle of its own; ...`, `installing 1 patched copy(ies) ...`, and
     the expected `REFUSED: restart required: <bundle> is already loaded ...`; the PANEL says S1, which means
     `ApplyProject` answered `Resident` rather than that anyone matched that sentence.
  7. `connect console` → `ct_project <name>` → `ALL PASS`.
  8. **The retry arm.** Press Ship a SECOND time, everything unchanged: it must end green with S1 again, and
     `ppcontent.json` must still hold exactly ONE row — the §4.2 reuse. R6 here would mean no author can ever
     act on a "press Ship again" sentence.
  9. **Mandatory final arm** — restart so `meta.json` is discovered, enable `<name>` in the mod manager BEFORE
     entering the geoscape, show the prototype again, and assert `BundleLive.Holds(<id>)` plus the live
     `sharedMesh` vertex/index counts equal the GLB's baked counts.
  10. **W5, in a SEPARATE NEVER-APPLIED PROJECT** (Codex finding 10). It cannot run on the project above and it
     cannot run after step 9: by then `<name>` is enabled, `BundleLive.Holds(<id>)` is deliberately TRUE, and the
     earlier successful bake has already written that project's `ct-cache.key` — asserting "false" and "absent"
     against it is impossible, not merely hard. So: **before the restart of step 9**, ship into a second project
     with its own name and its own id (`Replace_BadRow`, never applied, never enabled), then hand-edit its
     `ppcontent.json` to name a bundle this install does not ship (or a mesh stem with no file) and press Ship
     again. Assert, all three about THAT project and its own patched directory: the panel's `shipResult` is byte
     for byte
     `NOT APPLIED: patching the shipped bundle(s) reported <n> failure(s), named in the P0/REFUSED line(s) above; nothing was installed and no copy was marked current. Fix the lines above and press Ship again.`
     (the exact R11 string as the SHIPPED code composes it: `Route7.cs:341-343`'s NOT APPLIED line, taken by
     `Tail(log, 1)` and suffixed at `ModelDoctor.cs:702` — a blank result is the `Tail` bug of finding 6 and fails
     this row), `BundleLive.Holds(<its id>)`
     is FALSE, and no `ct-cache.key` exists under its patched directory. Remove that project afterwards and record
     it under "Left behind / removed".

- [ ] **Step 3: Record the evidence, then commit.** Fill this table in, in this file, with what the run actually
  produced — a log excerpt, a screenshot path, the on-disk paths. An empty cell means the slice is not done.

  | id | Check | Evidence |
  |---|---|---|
  | W4 | Target derivation disk-proved: the stored pair equals the row the bake matched, and `WhyNot` answered `null` for **exactly one** bundle — evidence is the `Player.log` derivation block `[ContentTool] ShippedTarget: '<asset>' candidates (n): …` with one `WhyNot` outcome line per deduplicated candidate and a single `HOLDS IT (WhyNot == null)`, closed by `resolved '<asset>' -> <bundle> (1 of <present> present candidate(s) …)` | |
  | W5 | A failed bake installs nothing, proved in a SEPARATE never-applied project before the step-9 restart: `ApplyDisposition.BakeFailed` → the exact R11 string in `shipResult`, `Holds(<its id>)` false, no `ct-cache.key` under its patched directory | |
  | W6 | Honest end state, preview and all: shipped with `HasPreview` true and NOT refused as R8; S1 with no live swap this session; after restart + enable, `Holds` true and live mesh counts equal the GLB's | |
  | W3b | The second press of an unchanged Ship ends green with ONE row, not R6 (step 8) | |
  | W7 | Owner visual check: the replaced mesh is on the prototype after the restart in step 9 | |

  **Left behind / removed.** Record what the run created under `D:\PP-Instance2\Mods\` and whether it was deleted;
  a scratch project whose `id` collides with a shipped demo's must be removed, or two projects share one patched
  copy folder.

  - If PPCLI itself misbehaves during this run: append the entry to `E:\DEV\PhoenixPoint\PPCLI\ISSUES.md`
    (attempted → happened → expected → evidence → severity) and work around it. Do NOT edit PPCLI source, and never
    commit a PPCLI change from this repo.
  - `git -C E:\DEV\PhoenixPoint\ContentTool add internal-docs\planning\2026-09-02-replace-mesh-wizard-plan.md && git -C E:\DEV\PhoenixPoint\ContentTool commit -m "docs(planning): record the in-game acceptance run that closes W3b and W4-W7"`

---

**Never write a `.claude\green-pending` sentinel for any step here.** Every commit above is explicit and BY PATH —
the tree holds untracked `.zip` files and untracked import folders, so `git add -A` is forbidden throughout.

---

## Self-review

**Design coverage, section by section.**

| §  | What it asks | Where it lands |
|---|---|---|
| 4.1 | `ShippedTarget.Resolve`, four steps, R9/R10 + R14-R22, visited set, case-blind file names | Task 5 step 2, in full |
| 4.1 | `PrototypeTarget` gains three fields; `LiveSlots` keeps the `Addon`; `Retarget` calls `Resolve` | Task 5 steps 1 and 3 |
| 4.2 | `ProjectScaffold` API + `RootOf`, sibling placement, post-condition, name table, write order (R3 first), reuse-not-R6, GLB policy, meta.json from the manifest id, R13 | Tasks 1-3 (name table + templates + meta, row append/reuse, copy/sidecar) |
| 4.2 | normalized `modDir`, strict `Json.Parse` before `MetaRefusal`, absent-only creation (`FileMode.CreateNew`), the id quoted through `JsonWriter` and the template body a literal | Task 2 **Step 0** (1 and 2) and Task 3 step 2 (`CopyOrVerify`/`CreateNew`) |
| 4.3 | `ApplyProject` internal, early return on `failed != 0`, `ApplyDisposition`, `BundleLive.ResidentNow` | Task 4 steps 1-2 |
| 4.4 | SHIP section, the enabled condition verbatim, `Intent.Ship`, `Enqueue("ship")`, two-frame gate | Task 6 steps 1, 3, 4 |
| 4.5 | Doctor ≡ bake: R3 owned by the scaffold, sidecar beside the copy, R7 on `MeshBytes`, R8 preview-aware | Task 6 step 2 (`SameRigAs`) and step 3 (`DoShip`), in that order |
| 5 | the change table | the File Structure table above, same rows |
| 6 | R1-R23, S1, S2 | R1/R2/R4/R5/R6/R13 asserted verbatim in Tasks 1-3's arms; R3 asserted in Task 3 (both arms); R7/R8/R11/R12/R23/S1/S2 in Task 6 step 3; R9/R10/R14-R22 in Task 5 step 2; R11's text in Task 4 |
| 7 | nothing is rolled back; the write order that makes each stage retryable | Task 3's final method (SHA → dir → manifest → meta → row/reuse → sidecar guard → copy → sidecar → Save) and the `DoShip` doc comment |
| 8 | the twelve arms + the in-game sequence | Tasks 1-3 and Task 6 step 2 (arms), Task 8 (sequence, steps 1-10 as written) |
| 9 | the `ponytail:` ledger | three comments carried into the code: per-slot `BundleBaker` in `Resolve`, the copy of `GetAssetReferencesFromObject`'s field walk, and the per-frame `File.Exists` in `Ship()` |
| 10 | W1-W7 + W3b | W1/W2/W3/W3b in Task 7 step 3; W4/W5/W6/W7 and W3b's in-game half in Task 8's table |

**Two places this plan decides something the design leaves to the implementation, each deliberate:**

1. **`ApplyProject`'s disposition for `forBundle == null`.** The console verb passes null and ignores the value, so
   `how` stays at its `Refused` initial value on that path; the field is meaningful only when a bundle was named.
   Documented on the parameter rather than modelled as a fifth enum member nobody would ever branch on.
2. **The test arm prints `PROJECT-SCAFFOLD PASS`, where design §8 says `SCAFFOLD PASS`.** W1 asks for
   "`SCAFFOLD PASS` present", and `PROJECT-SCAFFOLD PASS` contains it, so the acceptance row is satisfied either
   way; the longer prefix is the one the file name and the class name already use.

**Placeholder scan.** No `TBD`, no "add validation", no "similar to Task N": `AddMeshReplacement` is given whole in
Task 1, whole again in Task 2 and whole again in Task 3, because a partially-quoted method is how a plan grows a
step nobody can execute. Every expected build/run line is a literal (`Ошибок: 0`, `Предупреждений: 1`,
`DEMO BANKS: ALL PASS, 6 check(s)`, `R0: ALL PASS`, `PROJECT-SCAFFOLD PASS, 29/35/48/60/64 check(s)`), and every red
step names its exact text (CS2001 + CS0246 in Task 1; the `PROJECT-SCAFFOLD FAILURE:` sentence in Tasks 2 and 3).

**Name and type consistency across tasks.** `ProjectScaffold.Result` fields (`Root`, `ManifestPath`, `MetaPath`,
`MeshPath`, `SidecarPath`, `Created`, `MeshAlreadyPresent`, `RowAlreadyPresent`, `MeshBytes`) are the same in Tasks 1,
2, 3 and 6. `AddMeshReplacement`'s seven parameters never change shape; `RootOf(modDir, name)` is declared in Task 1
and called in Task 6's R12 arm. `PrototypeTarget.ShippedBundle/ShippedAsset/TargetRefusal` are written in Task 5 and
read in Task 6's `ArmShip` and `Ship()`. `ShippedTarget.Resolve(Addon, SkinnedMeshRenderer, PrototypeTarget)` is
declared in Task 5 step 2 and called in Task 5 step 3 with `made.Key, made.Value, target`.
`Route7.ApplyProject(string, string, out Route7.ApplyDisposition)` is added in Task 4 and called in Task 6 step 3 as
`Bake.Route7.ApplyProject(made.Root, shipBundle, out how)` — an ABSOLUTE root, which `ContentToolMain.ProjectDir`
passes through unchanged (`ContentMods.cs:146`, `Path.Combine(root, absolute) == absolute`) — while the one-argument
overload keeps serving `ct_route7 apply` and `Toggle`. `RigTarget.SameRigAs` is added in Task 6 step 2 and used in
step 3. `ContentMods.Manifest` is the `ppcontent.json` file name; `Package.EngineId` is the dependency id in
`meta.json` and `Package.MetaRefusal` its validator; `AliasMap.SidecarPathOf` is the only speller of the sidecar path
in both the source and the arms.

**Test-count arithmetic.** Task 1 = 13 (name table: 1 valid + 11 refused + 1 "nothing created") + 3 (default name)
+ 5 (templates) + 2 (authored id) + 3 (R13) + 2 (R2) + 1 (empty folder) = **29**. Task 2 **Step 0** adds 2
(trailing separator) + 2 (malformed / non-object meta) + 2 (the `com.test"quote` id) = **35**. Task 2 Step 1 adds
1 (the first press left one row) + 5 (append into the hand-written manifest: joined, outside-span bytes, the
original row unbroken inside the span, two rows with id/bundle intact, the meta template) + 3 (reuse in a fresh
project) + 4 (conflict) = **48**. Task 3 adds 2 (copy + no-op) + 2 (R4) + 2 (R3) + 1 (R3 on a new name) + 1 (R5)
+ 4 (sidecar + `MeshBytes`) = **60**. Task 6 step 2 adds 4 (the rig fingerprint, all four mesh fields moved) =
**64**. If a reviewer's own run prints a different integer, the arm SENTENCE is the contract and the integer is
bookkeeping — update the number here rather than deleting a check to reach it.
