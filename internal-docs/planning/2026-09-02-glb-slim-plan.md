# GLB Slim Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ship slice 3 of `internal-docs\planning\2026-09-02-prototype-picker-design.md` (GLB maintenance foundation + ppslim port): a lossless `GlbDocument` round-trip, a clip census, guarded trim with mandatory-clip protection, progress/cancel per the design's section 9 threading contract, atomic save, and an IMGUI panel on the Doctor tab's Advanced section. An unmutated document writes its ORIGINAL JSON chunk bytes verbatim (byte-equality round-trip); re-serialization happens only when the document is marked `Dirty`.

**Architecture:** `GlbDocument` is a SEPARATE file from `GlbReader.cs`. Reason: `GlbReader` is a lossy decoder that throws `ImportRefusedException` for what it cannot USE (Draco, meshopt, KHR_texture_transform, >65535 joints); its 44-refusal contract is pinned by `RefusalCount.cs` / `BinderFrozen.cs`. A maintenance tool must rewrite files it cannot import. `GlbDocument` lives in `src\Import\` because namespaces follow folders and `Json` / `JsonWriter` are `internal` in `Morgott.ContentTool.Import`. JSON library: none added -- reuse `Morgott.ContentTool.Import.Json.Parse` (`src\Import\GlbReader.cs:2313`) + `JsonWriter` (`src\Import\GlbCodec.cs:1225`). Key order is preserved via Dictionary insertion order (nothing removed, only reassigned), asserted by a key-order check. Numbers are `double` -> integral values written as integers, else `G17` (NOT `"R"`, which does not round-trip on .NET Framework). `ponytail:` Dictionary insertion order, upgrade to OrderedDictionary if a mutation needs to delete keys.

**Tech Stack:** C# 9 / net472, Mono inside Unity 2019 (Phoenix Point). No new dependencies. JSON in via `Json.Parse` (`src\Import\GlbReader.cs:2313`), JSON out via `JsonWriter` (`src\Import\GlbCodec.cs:1225`) with two new methods (`Val(object)` + `Num(double)`). UI is IMGUI (`UnityEngine.IMGUIModule`, already referenced). Build: `dotnet build -c Release`. Offline tests: `tests\ObjCodecTests` (NOT `dotnet test`), run with `dotnet run --project tests\ObjCodecTests -c Release`; every gate is a `static class X { internal static string Run() }` that throws on failure and is called from `Program.Main`. `tests\ObjCodecTests\ObjCodecTests.csproj` sets `EnableDefaultCompileItems=false`, so **every new file -- test or linked src -- must be added to its `<Compile Include>` list**; `ContentTool.csproj` globs `src\**\*.cs` and needs no edit.

**Fixtures:**
- `lib\u9_probe.glb` (2888 B, 4 clips Walk/walk/Morphs/Hold, 12 accessors/12 bufferViews, 1 skin) -- trim unit.
- `lib\u8_probe.glb` (349 KB, 5 clips Spider_*, 278 accessors but only 5 bufferViews) -- the shared-bufferView case where dropping clips frees zero bytes; census must report it.
- Real-world, by absolute path, NOT copied into the repo; gate prints a skip line when absent:
  - `E:\DEV\PhoenixPoint\ContentTool\APOCD GLBs for content tool without apply tranforms\CHR_PX_HVY_LL_M_V01_0fa9bde0c679e665.glb` (4.07 MB, 1 embedded image + 1 skin, 0 anims -- proves image/skin bytes survive compaction).
  - `...\CHR_PX_HVY_TS_M_V01_7c71cfba6f4e08f7.glb` (4.46 MB).

---

## File Structure

**Created**

| Path | Responsibility |
|---|---|
| `src\Import\GlbDocument.cs` | Lossless GLB reader/writer: 12-byte header, JSON chunk, BIN chunk, trailing chunks kept verbatim. Unmutated doc writes ORIGINAL JSON bytes. Dirty flag triggers re-serialization with 0x20 padding for JSON, 0x00 for BIN. |
| `src\Import\GlbSlim.cs` | `Census()` -> clip rows; `Guard()` -> mandatory-clip + structural checks; `Trim()` -> drop unreferenced accessors/bufferViews, compact BIN, remap indices. |
| `src\Import\SlimJob.cs` | `SlimProgress` snapshot + `Execute(...)` pure worker + `Start(...)` wrapper via `ThreadPool.QueueUserWorkItem`. Atomic save: write `.ct_tmp`, then `File.Replace`/`File.Move`; tmp deleted in `finally`. |
| `src\Dev\SlimPanel.cs` | IMGUI panel under Advanced on the Doctor tab. Source path + Browse, clip checklist with sizes from Census, Run/Cancel, progress bar, result line. |
| `tests\ObjCodecTests\GlbDocTests.cs` | Round-trip, census, trim, cancel gates. |

**Modified**

| Path | Change |
|---|---|
| `src\Import\GlbCodec.cs:1240` | Add `Val(object)` (dispatches to typed `Val` overloads or `Num`) + `Num(double)` (integral -> integer string, else `G17`) after `Null()`. |
| `src\Dev\FitBench.cs:1363` | ~4 lines after `doctor.Draw(...)`: draw `SlimPanel` when `advanced` is true, inside the Doctor tab. |
| `tests\ObjCodecTests\ObjCodecTests.csproj` | New `<Compile Include>` entries for `GlbDocTests.cs` and linked src files (`GlbDocument.cs`, `GlbSlim.cs`, `SlimJob.cs`). |
| `tests\ObjCodecTests\Program.cs:135` | New `Console.WriteLine(GlbDocTests.Run());` lines (one per gate phase). |

---

### Task 1: `GlbDocument` lossless round-trip + `JsonWriter` extensions

Port the lossless GLB container model: load 12-byte header, JSON chunk, BIN chunk, trailing chunks kept verbatim. Write with 4-byte alignment padding (0x20 for JSON, 0x00 for BIN). An unmutated document writes its ORIGINAL JSON chunk bytes verbatim so byte-equality round-trip is exact; re-serialize only when `Dirty`.

**Files:**
- Create: `src\Import\GlbDocument.cs`
- Modify: `src\Import\GlbCodec.cs:1240` (add `Val(object)` + `Num(double)` after `Null()`)
- Create: `tests\ObjCodecTests\GlbDocTests.cs`
- Modify: `tests\ObjCodecTests\ObjCodecTests.csproj` (add `<Compile Include>` entries)
- Modify: `tests\ObjCodecTests\Program.cs:135` (add `Console.WriteLine(GlbDocTests.Run());`)

- [ ] **Step 1: Write the gate stub.** Create `tests\ObjCodecTests\GlbDocTests.cs` with a `static class GlbDocTests { internal static string Run() }` that immediately returns `"GLB-DOC FAIL: not implemented"`. Register it:
  - In `tests\ObjCodecTests\ObjCodecTests.csproj`, add after the existing `<Compile Include="..\..\src\Import\GlbCodec.cs" Link="GlbCodec.cs" />` line (`:126`):
    ```xml
    <Compile Include="..\..\src\Import\GlbDocument.cs" Link="GlbDocument.cs" />
    <Compile Include="GlbDocTests.cs" />
    ```
  - In `tests\ObjCodecTests\Program.cs`, add after `Console.WriteLine(InspectTests.Run());` (`:135`):
    ```csharp
    Console.WriteLine(GlbDocTests.Run());
    ```
  - Run: `dotnet run --project tests\ObjCodecTests -c Release`
  - Expected: all existing gates PASS, new gate prints `GLB-DOC FAIL: not implemented`.

- [ ] **Step 2: Add `JsonWriter.Val(object)` and `JsonWriter.Num(double)`.** In `src\Import\GlbCodec.cs`, after `Null()` (`:1240`), add:
  ```csharp
  /// <summary>Write a parsed JSON value: string, double, bool, null, or a collection
  /// (Dictionary/List from Json.Parse). Integral doubles written as integers; fractional
  /// doubles use G17 for exact round-trip on .NET Framework (R does not round-trip).</summary>
  internal JsonWriter Val(object value) { ... }

  /// <summary>A double that may be integral. Integral -> no decimal point; else G17.</summary>
  internal JsonWriter Num(double value) { ... }
  ```
  `Val(object)` dispatches: `null` -> `Null()`, `string` -> `Val(string)`, `bool` -> `Val(bool)`, `double` -> `Num(double)`, `Dictionary<string,object>` -> recurse keys, `List<object>` -> recurse elements, else `throw new ArgumentException`. `Num(double)`: if `value == Math.Floor(value) && !double.IsInfinity(value)` -> `((long)value).ToString(CultureInfo.InvariantCulture)`, else `value.ToString("G17", CultureInfo.InvariantCulture)`.
  - Run: `dotnet build -c Release`
  - Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`

- [ ] **Step 3: Implement `GlbDocument`.** Create `src\Import\GlbDocument.cs` in namespace `Morgott.ContentTool.Import`:
  ```csharp
  /// <summary>
  /// Lossless GLB container: reads and rewrites a .glb without interpreting its glTF content,
  /// so it can handle files GlbReader refuses (Draco, meshopt, unknown extensions). An unmutated
  /// document writes its ORIGINAL JSON chunk bytes verbatim - byte equality, not semantic equality.
  /// Re-serialization fires only when Dirty is set.
  /// </summary>
  internal sealed class GlbDocument
  {
      // --- stored state ---
      internal uint Version { get; }
      /// <summary>The parsed JSON chunk (Dictionary from Json.Parse). Setting this sets Dirty.</summary>
      internal Dictionary<string, object> Json { get; }
      /// <summary>The raw JSON chunk bytes as read from disk. Used for verbatim write when !Dirty.</summary>
      private readonly byte[] originalJsonBytes;
      /// <summary>Mutable BIN chunk. Replaced wholesale by Trim.</summary>
      internal byte[] Bin { get; set; }
      /// <summary>Trailing chunks after BIN, kept verbatim (type + data pairs).</summary>
      private readonly List<(uint type, byte[] data)> trailing;
      internal bool Dirty { get; set; }

      /// <summary>Read a .glb from bytes.</summary>
      internal static GlbDocument Load(byte[] bytes) { ... }
      /// <summary>Read a .glb from a file path.</summary>
      internal static GlbDocument Load(string path) { ... }

      /// <summary>Write the document to bytes. Uses originalJsonBytes when !Dirty.</summary>
      internal byte[] Write() { ... }
      /// <summary>Write to a file path.</summary>
      internal void Write(string path) { ... }
  }
  ```
  Load: validate magic `0x46546C67`, version 2, read chunk 0 (type `0x4E4F534A` = JSON), chunk 1 (type `0x004E4942` = BIN), keep remaining chunks. Parse JSON via `Json.Parse(Encoding.UTF8.GetString(jsonBytes), 128)`. Store `originalJsonBytes`. Write: if `!Dirty`, emit `originalJsonBytes` padded to 4 bytes with `0x20`; if `Dirty`, serialize `Json` via `JsonWriter.Val(object)`, encode UTF-8, pad to 4 bytes with `0x20`. BIN padded to 4 bytes with `0x00`. Recompute total length in header.

- [ ] **Step 4: Write the round-trip gate checks.** In `GlbDocTests.Run()`, implement 11 checks:
  1. `u9_probe.glb` Load -> Write -> byte-identical to input.
  2. `u8_probe.glb` Load -> Write -> byte-identical to input.
  3. Header magic, version, chunk types validated.
  4. JSON parse produces a Dictionary with `"asset"` key.
  5. BIN length matches the `bufferViews` total byte span.
  6. Setting `Dirty = true` -> Write still round-trips semantically (re-parse equals original parse).
  7. Dirty write re-serializes (bytes may differ in whitespace but parse identically).
  8. Load from path works the same as Load from bytes.
  9. Corrupt magic -> `FormatException`.
  10. Truncated file -> `FormatException`.
  11. Key order preserved: keys of root JSON object in same order after Dirty round-trip.
  - Gate prints: `GLB-DOC PASS, 11 check(s)`.
  - Run: `dotnet run --project tests\ObjCodecTests -c Release`
  - Expected: `GLB-DOC PASS, 11 check(s)` among all-green output.

- [ ] **Step 5: Build the mod DLL.**
  - Run: `dotnet build -c Release`
  - Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`

- [ ] **Step 6: Commit.**
  - `git -C E:\DEV\PhoenixPoint\ContentTool add -A && git -C E:\DEV\PhoenixPoint\ContentTool commit -m "feat(glb): lossless GlbDocument round-trip + JsonWriter.Val(object)/Num(double)"`

---

### Task 2: Clip census via `GlbSlim.Census()`

List every animation clip in a GLB with its accessor/bufferView byte cost, distinguishing exclusive bytes (owned only by that clip) from shared bytes.

**Files:**
- Create: `src\Import\GlbSlim.cs`
- Modify: `tests\ObjCodecTests\GlbDocTests.cs` (add census checks)
- Modify: `tests\ObjCodecTests\ObjCodecTests.csproj` (add `<Compile Include="..\..\src\Import\GlbSlim.cs" Link="GlbSlim.cs" />`)

- [ ] **Step 1: Write failing census gate.** Add 7 checks to `GlbDocTests.Run()` (or a new section returning `SLIM PASS`):
  1. `u9_probe.glb` census returns 4 rows (Walk, walk, Morphs, Hold).
  2. Each row has Index, Name, Channels, Samplers, AccessorBytes, ExclusiveBytes.
  3. AccessorBytes = sum of (accessor count * element size) for each accessor referenced by the clip's samplers.
  4. `u8_probe.glb` census returns 5 rows (Spider_*).
  5. `u8_probe.glb` ExclusiveBytes is 0 for all clips (shared bufferViews).
  6. Real-world `CHR_PX_HVY_LL_M_V01` (if present): census returns 0 rows (no animations), skip if absent.
  7. Mandatory flag: clips matching the mandatory heuristic list are marked `Mandatory = true`.
  - Gate prints: `SLIM PASS, 7 check(s)` (or skip line for absent fixtures).
  - Run: `dotnet run --project tests\ObjCodecTests -c Release`
  - Expected: `SLIM FAIL: not implemented` (or compile error before implementation).

- [ ] **Step 2: Implement `GlbSlim`.** Create `src\Import\GlbSlim.cs` in namespace `Morgott.ContentTool.Import`:
  ```csharp
  /// <summary>
  /// Animation-clip census and trim for .glb files. Works on GlbDocument's parsed JSON,
  /// never on GlbReader's imported model - so it handles files GlbReader refuses.
  /// </summary>
  internal static class GlbSlim
  {
      /// <summary>One row per animation clip.</summary>
      internal sealed class ClipRow
      {
          internal int Index;
          internal string Name;
          internal int Channels;
          internal int Samplers;
          /// <summary>Sum of count * elementSize for every accessor this clip references.</summary>
          internal long AccessorBytes;
          /// <summary>byteLength of bufferViews owned ONLY by this clip's accessors (not shared
          /// with mesh/skin/image accessors or other clips). Zero when bufferViews are shared.</summary>
          internal long ExclusiveBytes;
          /// <summary>True when the clip name matches the mandatory action heuristic.</summary>
          internal bool Mandatory;
      }

      // ponytail: mandatory list is a heuristic (idle/walk/run/death/attack/hit/aim/fire/reload/
      // turn/stand/crouch/jump/climb/spawn); upgrade path = slice 1 PrototypeRecord.Variant[].resolved
      // clip catalogue when it ships.
      private static readonly HashSet<string> MandatoryTokens = ...;

      /// <summary>Enumerate every animation clip in the document.</summary>
      internal static List<ClipRow> Census(GlbDocument doc) { ... }
  }
  ```
  Census walks `doc.Json["animations"]` (a `List<object>`), for each animation reads `"channels"` and `"samplers"`, follows sampler `"input"` / `"output"` accessor indices to `doc.Json["accessors"]`, computes AccessorBytes from accessor `"count"` * element size (derived from `"componentType"` and `"type"`). ExclusiveBytes: for each accessor's `"bufferView"`, check whether any OTHER accessor (from meshes, skins, images, or other clips) references the same bufferView; if not, add the bufferView's `"byteLength"`. Mandatory: case-insensitive substring match of clip name against `MandatoryTokens`.
  - Register in csproj.
  - Run: `dotnet run --project tests\ObjCodecTests -c Release`
  - Expected: `SLIM PASS, 7 check(s)`.

- [ ] **Step 3: Build.**
  - Run: `dotnet build -c Release`
  - Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`

- [ ] **Step 4: Commit.**
  - `git -C E:\DEV\PhoenixPoint\ContentTool add -A && git -C E:\DEV\PhoenixPoint\ContentTool commit -m "feat(glb): GlbSlim.Census() lists clips with byte costs and mandatory flags"`

---

### Task 3: `GlbSlim.Guard()` + `Trim()`

Guarded destructive trim: refuse unless `force` when a dropped clip is mandatory or the file has a skin AND >30 clips. Extra structural guard: recursive count of every `"bufferView"` key in JSON must equal accessors + images, else refuse. Drop unreferenced accessors/bufferViews, compact BIN, remap indices.

**Files:**
- Modify: `src\Import\GlbSlim.cs` (add `Guard()` + `Trim()`)
- Modify: `tests\ObjCodecTests\GlbDocTests.cs` (add 11 more checks, total gate prints `SLIM PASS, 18 check(s)`)

- [ ] **Step 1: Write failing trim gate.** Add 11 checks:
  1. `u9_probe.glb`: trim dropping "Morphs" + "Hold" -> output has 2 clips, is valid GLB.
  2. Round-trip: trimmed output Load -> Write -> byte-identical.
  3. Trimmed output BIN is smaller than original.
  4. Accessor indices remapped correctly (no dangling references).
  5. Guard refuses to drop "Walk" (mandatory) without `force`.
  6. Guard with `force` allows dropping "Walk".
  7. `u8_probe.glb`: trim all 5 clips -> BIN unchanged (shared bufferViews, nothing freed).
  8. BufferView count structural check: synthetic JSON with mismatched bufferView key count -> refuses.
  9. Skin + >30 clips guard: synthetic doc triggers refusal without `force`.
  10. Real-world `CHR_PX_HVY_TS_M_V01` (if present): trim 0 clips -> byte-identical, skip if absent.
  11. Images survive: real-world `CHR_PX_HVY_LL_M_V01` (if present) -> after trim (0 clips to drop), image bufferView bytes unchanged, skip if absent.
  - Run: `dotnet run --project tests\ObjCodecTests -c Release`
  - Expected: fails (not implemented).

- [ ] **Step 2: Implement `Guard()` and `Trim()`.** Add to `GlbSlim`:
  ```csharp
  /// <summary>
  /// Pre-flight check before trim. Returns null if safe, else a refusal message.
  /// Refuses when: (a) a clip in drop set is mandatory and !force, (b) file has a skin
  /// AND >30 clips and !force, (c) recursive bufferView key count != accessors + images.
  /// </summary>
  internal static string Guard(GlbDocument doc, HashSet<int> dropIndices, bool force) { ... }

  /// <summary>
  /// Drop the clips at dropIndices, remove now-unreferenced accessors and bufferViews,
  /// compact BIN, remap all accessor/bufferView/animation indices in the JSON.
  /// Sets doc.Dirty. Returns the byte delta (negative = savings).
  /// </summary>
  internal static long Trim(GlbDocument doc, HashSet<int> dropIndices) { ... }
  ```
  Trim algorithm:
  1. Build the set of accessor indices referenced by surviving animations, meshes, skins, images.
  2. Unreferenced accessors = all accessor indices NOT in that set.
  3. Unreferenced bufferViews = bufferView indices NOT referenced by any surviving accessor or image.
  4. Build new BIN by copying surviving bufferView byte ranges in order, tracking new offsets.
  5. Rewrite `doc.Json`: remove dropped animations, remove unreferenced accessors/bufferViews, remap all index references (sampler input/output, mesh primitive attributes/indices, skin inverseBindMatrices, image bufferView, sparse accessor bufferViews).
  6. Update `doc.Bin`, set `doc.Dirty = true`.
  - Run: `dotnet run --project tests\ObjCodecTests -c Release`
  - Expected: `SLIM PASS, 18 check(s)`.

- [ ] **Step 3: Build.**
  - Run: `dotnet build -c Release`
  - Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`

- [ ] **Step 4: Commit.**
  - `git -C E:\DEV\PhoenixPoint\ContentTool add -A && git -C E:\DEV\PhoenixPoint\ContentTool commit -m "feat(glb): GlbSlim.Guard() + Trim() with mandatory-clip guard and BIN compaction"`

---

### Task 4: `SlimJob` -- progress, cancel, atomic save

Wrap `GlbSlim` in a cancellable worker with immutable progress snapshots and atomic file output.

**Files:**
- Create: `src\Import\SlimJob.cs`
- Modify: `tests\ObjCodecTests\GlbDocTests.cs` (add 4 more checks, total gate prints `SLIM PASS, 22 check(s)`)
- Modify: `tests\ObjCodecTests\ObjCodecTests.csproj` (add `<Compile Include="..\..\src\Import\SlimJob.cs" Link="SlimJob.cs" />`)

- [ ] **Step 1: Write failing job gate.** Add 4 checks:
  1. `Execute` with no clips to drop -> destination is byte-identical to source.
  2. `Execute` with cancellation token cancelled before start -> destination file does not exist, source untouched.
  3. `Execute` completes -> `.ct_tmp` file does not exist (cleaned up).
  4. Progress callback fires at least once with `Done <= Total`.
  - Run: `dotnet run --project tests\ObjCodecTests -c Release`
  - Expected: fails (not implemented).

- [ ] **Step 2: Implement `SlimJob`.** Create `src\Import\SlimJob.cs` in namespace `Morgott.ContentTool.Import`:
  ```csharp
  /// <summary>
  /// Immutable progress snapshot for the slim operation.
  /// </summary>
  internal sealed class SlimProgress
  {
      internal readonly string Stage;
      internal readonly int Done;
      internal readonly int Total;
      internal readonly string Message;
      internal SlimProgress(string stage, int done, int total, string message) { ... }
  }

  /// <summary>
  /// Slim job: load, census, guard, trim, atomic write. Pure and thread-free (cancel test
  /// is synchronous via CancellationToken). Start wraps it in ThreadPool.QueueUserWorkItem
  /// like ModelDoctor.Start (src\Dev\ModelDoctor.cs:229).
  /// </summary>
  internal static class SlimJob
  {
      /// <summary>
      /// Execute the slim pipeline. Pure, no thread affinity.
      /// </summary>
      /// <param name="src">Source .glb path.</param>
      /// <param name="dst">Destination .glb path (may equal src for in-place).</param>
      /// <param name="drop">Set of clip indices to drop.</param>
      /// <param name="force">Override mandatory-clip and skin+30 guards.</param>
      /// <param name="cancel">Cooperative cancellation.</param>
      /// <param name="publish">Progress callback (may be called from any thread).</param>
      /// <returns>Result message.</returns>
      internal static string Execute(string src, string dst, HashSet<int> drop, bool force,
                                     CancellationToken cancel, Action<SlimProgress> publish) { ... }

      /// <summary>
      /// Fire-and-forget wrapper via ThreadPool, like ModelDoctor.Start (ModelDoctor.cs:229).
      /// Sets a volatile SlimProgress field and invokes a completion callback on finish.
      /// </summary>
      internal static void Start(string src, string dst, HashSet<int> drop, bool force,
                                 CancellationTokenSource cts, Action<SlimProgress> onProgress,
                                 Action<string> onComplete) { ... }
  }
  ```
  Execute algorithm:
  1. Publish `("Load", 0, 4, "Reading " + Path.GetFileName(src))`.
  2. `cancel.ThrowIfCancellationRequested()` at each stage boundary.
  3. Load -> Census -> Guard (throw if refusal) -> Trim -> Write to `dst + ".ct_tmp"`.
  4. `File.Replace(tmp, dst, null)` when dst exists, else `File.Move(tmp, dst)`.
  5. `finally`: if `.ct_tmp` exists, delete it.
  - Register in csproj.
  - Run: `dotnet run --project tests\ObjCodecTests -c Release`
  - Expected: `SLIM PASS, 22 check(s)`.

- [ ] **Step 3: Build.**
  - Run: `dotnet build -c Release`
  - Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`

- [ ] **Step 4: Commit.**
  - `git -C E:\DEV\PhoenixPoint\ContentTool add -A && git -C E:\DEV\PhoenixPoint\ContentTool commit -m "feat(glb): SlimJob with progress snapshots, cooperative cancel and atomic save"`

---

### Task 5: `SlimPanel` IMGUI + FitBench integration

Panel under Advanced on the Doctor tab, reusing `GlbFileBrowser`. OnGUI enqueues, never mutates state directly (existing rule in `ModelDoctor`).

**Files:**
- Create: `src\Dev\SlimPanel.cs`
- Modify: `src\Dev\FitBench.cs:1363` (~4 lines after `doctor.Draw(...)`)

- [ ] **Step 1: Implement `SlimPanel`.** Create `src\Dev\SlimPanel.cs` in namespace `Morgott.ContentTool.Dev`:
  ```csharp
  /// <summary>
  /// IMGUI panel for the GLB slim tool, drawn under Advanced on the Doctor tab.
  /// Reuses GlbFileBrowser for file selection. OnGUI enqueues work, never mutates
  /// state directly (same rule as ModelDoctor).
  /// </summary>
  internal sealed class SlimPanel
  {
      private readonly GlbFileBrowser browser = new GlbFileBrowser();
      private GlbSlim.ClipRow[] census;
      private bool[] selected;           // which clips to DROP
      private volatile SlimProgress progress;
      private CancellationTokenSource cts;
      private string result;
      private string sourcePath;

      /// <summary>Draw the panel. Called from FitBench inside the Doctor tab, under Advanced.</summary>
      internal void Draw(float width) { ... }

      /// <summary>Cancel any running job and release resources.</summary>
      internal void Dispose() { ... }
  }
  ```
  Draw contents: source path label + Browse button (via `browser`), clip checklist from `census` (each row: checkbox, name, channels, AccessorBytes, ExclusiveBytes, mandatory badge), Run button (disabled while running), Cancel button (enabled while running), progress bar from `SlimProgress`, result line. Browse callback: load `GlbDocument`, run `Census`, populate `census`/`selected`. Run callback: collect drop indices from unchecked rows, call `SlimJob.Start`. Cancel callback: `cts.Cancel()`.

- [ ] **Step 2: Integrate into `FitBench`.** In `src\Dev\FitBench.cs`, add a `private static SlimPanel slim;` field near the `doctorTab` field (`:245`). After `doctor.Draw(BenchList.ContentWidth(w));` (`:1363`), add:
  ```csharp
  if (advanced)
  {
      if (slim == null) slim = new SlimPanel();
      slim.Draw(BenchList.ContentWidth(w));
  }
  ```
  In `Close()` disposal steps (`:881` area), add `Step(failed, "the slim panel", () => { slim?.Dispose(); slim = null; });`.

- [ ] **Step 3: Build.**
  - Run: `dotnet build -c Release`
  - Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`

- [ ] **Step 4: Commit.**
  - `git -C E:\DEV\PhoenixPoint\ContentTool add -A && git -C E:\DEV\PhoenixPoint\ContentTool commit -m "feat(glb): SlimPanel IMGUI under Doctor Advanced tab"`

---

### Task 6: In-game acceptance on D:\PP-Instance2

Drive the panel through PPCLI, take screenshots, run a slimmed file through the Doctor to confirm it gets a verdict (not a refusal). Owner visual check handoff at the end.

**Files:**
- No source changes.

- [ ] **Step 1: Deploy.** Read `E:\DEV\PhoenixPoint\ContentTool\deploy.ps1` for parameters, then run it targeting `D:\PP-Instance2`.

- [ ] **Step 2: Open the bench.** Wait for `connect state` to answer, then:
  ```powershell
  cd E:\DEV\PhoenixPoint\PPCLI
  .\ppcli.ps1 connect console '{"command":"ct_bench","args":["open"]}'
  ```

- [ ] **Step 3: Drive the slim panel.** The IMGUI panel is not clickable through PPCLI; use reflection as the Doctor acceptance run did:
  ```powershell
  # Verify the slim field exists
  .\ppcli.ps1 connect call '{"op":"get","target":"@type:Morgott.ContentTool.Dev.FitBench","member":"slim"}'
  # Toggle advanced on
  .\ppcli.ps1 connect call '{"op":"set","target":"@type:Morgott.ContentTool.Dev.FitBench","member":"advanced","value":true}'
  ```
  Use `AccessTools.Field(typeof(FitBench), "slim")` if direct field access fails. Set `slim.sourcePath` to a test GLB via reflection, trigger census and capture a screenshot:
  ```powershell
  .\ppcli.ps1 connect screenshot
  ```

- [ ] **Step 4: Run slim on a test file.** Set up a slim run on `u9_probe.glb` (copy to a temp location first), dropping non-mandatory clips, via reflection calls. Screenshot the progress bar and result.

- [ ] **Step 5: Feed slimmed file to Doctor.** Use `doctor.PickFile(<slimmed path>)` via reflection and confirm the Doctor produces a verdict, not a refusal:
  ```powershell
  .\ppcli.ps1 connect screenshot
  ```

- [ ] **Step 6: Owner handoff.** Present the screenshots to the owner for visual verification. Report what was tested:
  - SlimPanel visible under Advanced on Doctor tab.
  - Census populated with clip rows.
  - Trim completed, progress bar worked, result displayed.
  - Slimmed file accepted by Doctor (verdict, not refusal).
