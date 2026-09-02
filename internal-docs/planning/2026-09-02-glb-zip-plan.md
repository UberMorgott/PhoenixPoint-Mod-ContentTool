# GLB Zip Implementation Plan (`ppzip.py` -> in-game C#)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Port `tools\ppzip.py` (272 lines) into the mod as `src\Import\GlbZip.cs` + a second mode of
the existing slim panel, per design §9 order 2 ("ppzip 4-5 days: constant-curve collapse, quaternion
quantisation, preservation/idempotence tests"). Shrink the ANIMATION half of a `.glb` without
dropping a clip, a channel or a key: every clip, channel and key survives, only the STORAGE changes.
Threading / progress / cancel / atomic-save follow design §9 exactly, by reusing the shipped
`SlimJob` machinery rather than adding a second copy of it.

**Architecture:** Three moving parts, and only the first is new code of any size.

1. `src\Import\GlbZip.cs` - the passes. Pure, no Unity, works on `GlbDocument`'s parsed JSON + BIN
   exactly as `GlbSlim` does, so it handles files `GlbReader` refuses.
2. The garbage-collect / compact / rewrite half is **NOT re-solved**: `ppzip.zip_anims` ends by
   calling `ppslim.slim(...)` with a regex that matches no clip (`ppzip.py:205-207`), and the C#
   equivalent already ships - `GlbSlim.Trim(doc, new HashSet<int>())` drops the accessors and
   bufferViews nothing points at any more and compacts BIN (`src\Import\GlbSlim.cs:147-224`). GlbZip
   appends its new views, then calls `Trim` with an empty drop set. Same for the pre-flight:
   `GlbSlim.Guard(doc, empty, force: true)` (`GlbSlim.cs:108-139`) is the sparse / Draco / foreign-buffer
   refusal, reached with `force` because zip drops no clip and the mandatory-clip and rigged-character
   arms do not apply to it.
3. The job and the UI are EXTENSIONS OF THE SLIM ONES, not siblings. `SlimJob` gets a `Zip` /
   `StartZip` pair beside `Execute` / `Start` (`src\Import\SlimJob.cs:57,113`), reusing `SlimProgress`,
   `At`, `Publish` and - the reason this matters - the ONE copy of the `.ct_tmp` + `File.Replace`
   swap. `SlimPanel` gets a mode toggle (SLIM | ZIP) that swaps the middle block only
   (`src\Dev\SlimPanel.cs:96` `Clips(width)` <-> a new `Options(width)`), reusing `browser`,
   `intents`, the volatile `progress`/`result`/`running` trio, `Bar()`, `Bytes()` and `Beside()`.
   **Why not a sibling `ZipPanel`:** a sibling duplicates all of that plumbing (~140 new lines) AND
   needs a new `FitBench` field + Draw call + Dispose step (`src\Dev\FitBench.cs:253,1105,1616`); the
   mode toggle is ~60 lines inside one existing file and touches `FitBench` not at all. Smaller diff,
   so that is the one.

**Divergences from `ppzip.py`, each forced by a measured failure (see Fixtures):**

- **A sampler output accessor named by more than one animation is left alone entirely.** ppzip
  rewrites it once per clip and then re-reads it from the STALE blob, which is why it crashes on
  `lib\u9_probe.glb` (accessor 8 is shared by `Walk`, `walk` and `Hold`). C# rewrites each accessor at
  most once per document and skips any accessor whose samplers disagree - more than one animation,
  more than one `target.path`, or any non-LINEAR / non-packed sampler among them. Counted as `shared`.
- **An unreadable componentType is a SKIP, not an exit.** `ppzip.read_floats` raises `SystemExit`
  (`ppzip.py:65`); inside `OnGUI` that would tear the bench down. C# treats "not FLOAT and not
  normalized SHORT" as one more reason `Packed()` says no.
- **`bufferView.extensions` is refused** (one condition added to `GlbSlim.Guard`).
  `EXT_meshopt_compression` keeps its own `byteOffset` INSIDE the view's extension block, which
  `GlbSlim.Compact` does not move; `lib\u10_probe.glb` passes the bufferView-key count guard and ppzip
  grows it 20.4% while silently invalidating it. This also closes the same hole in the shipped TRIM
  path, which is why the condition goes in `GlbSlim.Guard` rather than in a zip-local guard.
- **A result that is not smaller is not written.** ppzip writes whatever comes out; on a file whose
  animation shares bufferViews with mesh data the rewrite DUPLICATES the animation bytes and the file
  grows (`u8_probe.glb` +7.9%). The job reports the growth and leaves the destination alone.

**No renormalisation of quaternions, deliberately.** ppzip rounds components and does not touch
length; `GlbReader`'s own slerp renormalises when it resamples (`src\Import\GlbReader.cs:1472-1485`),
and the worst-case component error below is 1.53e-05. Parity is kept; the gate asserts `|q|` stays
within 1e-4 of 1 rather than forcing it.

**The quantisation encoding, exactly** (`ppzip.py:101-110`, decoded at `GlbReader.cs:2212`):

| | |
|---|---|
| Which channels | `target.path == "rotation"` ONLY. Translation is metres with no bound and scale is unitless; a normalized type cannot express either. |
| Accessor after | `componentType` 5122 (SHORT), `normalized: true`, `type` unchanged (`VEC4`), `count` unchanged |
| Encoding | `q = round(v * 32767)`, clamped to **[-32767, +32767]** - NOT -32768, because the decoder is `Math.Max(x / 32767f, -1f)` and -32768 would come back -1.0000305 and be clamped anyway |
| Decoder, already shipped | `GlbReader.cs:2212` `case Gltf.Short: return normalized ? Math.Max(BitConverter.ToInt16(...) / 32767f, -1f) : ...` - nothing to add on the read side |
| Worst-case error | 1/65534 = 1.526e-05 per component, ~0.002 degrees |
| min/max | Sampler OUTPUT accessors carry none and lose any they had. glTF requires min/max only on the animation sampler **input**, which this repo states in its own writer: `src\Import\GlbCodec.cs:535-538` ("glTF requires min and max on an animation sampler's input"). `POSITION` is a mesh attribute and is never touched by this tool at all. |
| Idempotence | `ReadFloats` understands FLOAT **and** normalized SHORT, so a second run reads back what the first wrote and re-emits it bit for bit. Verified end to end below. |

**Tech Stack:** C# 9 / net472, Mono inside Unity 2019. No new dependencies. Build `dotnet build -c Release`.
Offline gates are `tests\ObjCodecTests` (NOT `dotnet test`), run with
`dotnet run --project tests\ObjCodecTests -c Release`; each gate is a `static class X { internal static
string Run() }` that throws on failure and is called from `Program.Main`.
`tests\ObjCodecTests\ObjCodecTests.csproj` sets `EnableDefaultCompileItems=false`, so **every new file -
test or linked src - must be added to its `<Compile Include>` list**; `ContentTool.csproj` globs
`src\**\*.cs` and needs no edit.

**Fixtures - all numbers below were MEASURED with `python tools\ppzip.py` on 2026-09-02, not estimated:**

| Fixture | ppzip result | What it proves in the gate |
|---|---|---|
| `lib\u8_rootfold.glb` (2,240 B, 1 clip, 2 samplers, 1 rotation) | 2,238 -> **2,212 B (-1.2%)** | the SHRINK case: exclusive views, one rotation curve quantised |
| `lib\u8_probe.glb` (349,468 B, 5 Spider clips, 180 samplers, 137 rotation, **5 shared bufferViews**) | 349,480 -> **377,256 B (+7.9%)**, 8 collapsed, 137 quantised | the GROWTH case: animation interleaved with mesh data in shared views, so the old keys cannot be freed. The job must refuse to write it. |
| `lib\u9_probe.glb` (2,888 B, 4 clips, 5 samplers, **0 rotation**, accessor 8 shared by `Walk`/`walk`/`Hold`, `Hold` is STEP) | **CRASHES** (`struct.error ... offset 380, buffer size 380`) | the shared-accessor rule: C# must finish, leave accessor 8 alone, and keep all 4 clips |
| `lib\u12_norm.glb` (Draco, 5 accessors, 1 bufferView key) | 44,429 -> **1,072 B (-97.6%): the data is destroyed** | Guard REFUSES (keys 1 != accessors 5 + images 0). Same for `lib\u12_probe.glb` (270 != 278) and `lib\u12_uv.glb` (1 != 5). |
| `lib\u10_probe.glb` (`EXT_meshopt_compression` + `KHR_mesh_quantization`, keys 278 == accessors 278) | 130,448 -> 157,056 (+20.4%), silently invalid | the NEW Guard condition: a bufferView with an `extensions` key is refused. The key-count guard alone lets this through. |
| `local\PpFit\Content\Models\tiffany_ppfit.glb` (36,254,816 B, 300 clips, 29,724 accessors, 6 images) - **gitignored, absolute path, gate prints a skip line when absent** | 15,149 collapsed, 27,284 quantised, keys 2,721,855 -> 2,721,855, output **byte-identical to input**, 1.93 s in Python | real-world IDEMPOTENCE at scale: an already-zipped file is a fixed point |

---

## File Structure

**Created**

| Path | Responsibility |
|---|---|
| `src\Import\GlbZip.cs` | `ReadFloats`, `Packed`, `IsConstant`, `Pack`, `Plan`, `Zip`, `Stats`. The passes only; GC/compaction is `GlbSlim.Trim`. |
| `tests\ObjCodecTests\GlbZipTests.cs` | The ported `ppzip.selfcheck`, the fixture gates, preservation, idempotence, the pose sampler. |

**Modified**

| Path | Change |
|---|---|
| `src\Import\GlbSlim.cs:108` | `Guard`: one more refusal - a bufferView carrying `extensions`. |
| `src\Import\GlbSlim.cs:332,346,375-391` | Widen `AccessorViews`, `ElementSize`, `Get`, `Obj`, `Arr`, `Str`, `Int`, `Long` from `private` to `internal` so GlbZip reads JSON through the same hostile-file-safe readers. |
| `src\Import\SlimJob.cs:43,57,140` | `Stages` -> a parameter of `At`; new `Zip` + `StartZip` pair beside `Execute` / `Start`. |
| `src\Dev\SlimPanel.cs:34-232` | Mode toggle SLIM \| ZIP; `Options(width)` beside `Clips(width)`; `Run()` branches. |
| `tests\ObjCodecTests\ObjCodecTests.csproj:172` | `<Compile Include>` for `..\..\src\Import\GlbZip.cs` and `GlbZipTests.cs`. |
| `tests\ObjCodecTests\Program.cs:139` | `Console.WriteLine(GlbZipTests.Run());` after the `GlbSlimTests` line. |

---

### Task 1: `GlbZip` readers and the four decision passes

The half of `ppzip.py` that DECIDES: read a sampler accessor as floats, refuse the ones that cannot be
read flat, spot a curve that never moves, and encode a value block. No file is rewritten in this task.

**Files:**
- Create: `src\Import\GlbZip.cs`
- Modify: `src\Import\GlbSlim.cs:332,346,375-391` (widen the readers to `internal`)
- Create: `tests\ObjCodecTests\GlbZipTests.cs`
- Modify: `tests\ObjCodecTests\ObjCodecTests.csproj:172`, `tests\ObjCodecTests\Program.cs:139`

- [ ] **Step 1: Write the gate stub.** Create `tests\ObjCodecTests\GlbZipTests.cs` with
  `static class GlbZipTests { internal static string Run() }` returning `"GLB-ZIP FAIL: not implemented"`.
  Register it:
  - In `tests\ObjCodecTests\ObjCodecTests.csproj`, after the `GlbReader.cs` line (`:172`):
    ```xml
    <Compile Include="..\..\src\Import\GlbZip.cs" Link="GlbZip.cs" />
    <Compile Include="GlbZipTests.cs" />
    ```
  - In `tests\ObjCodecTests\Program.cs`, after `Console.WriteLine(GlbSlimTests.Run());` (`:139`):
    ```csharp
    Console.WriteLine(GlbZipTests.Run());
    ```
  - Run: `dotnet run --project tests\ObjCodecTests -c Release`
  - Expected: every existing gate PASSes, the new line reads `GLB-ZIP FAIL: not implemented`.

- [ ] **Step 2: Widen the `GlbSlim` readers.** In `src\Import\GlbSlim.cs`, change `private static` to
  `internal static` on `AccessorViews` (`:332`), `ElementSize` (`:346`), `Get` (`:375`), `Obj` (`:378`),
  `Arr` (`:380`), `Str` (`:382`), `Int` (`:385`) and `Long` (`:390`). Nothing else moves; the comment
  block at `:370-372` already explains the contract these keep (a wrong type reads as absent, never as
  a throw) and GlbZip inherits it.
  - Run: `dotnet build -c Release`
  - Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`

- [ ] **Step 3: Implement the readers and the decision passes.** Create `src\Import\GlbZip.cs` in
  namespace `Morgott.ContentTool.Import`:
  ```csharp
  /// <summary>
  /// Shrink the ANIMATION half of a .glb without dropping a clip, a channel or a key: the same
  /// curves, stored differently. Port of tools\ppzip.py. Two passes, both optional:
  ///
  ///  - CONSTANT: a curve that holds one value for the whole clip is collapsed to its two endpoint
  ///    keys. EXACTLY LOSSLESS, because GlbReader resamples every curve onto a uniform grid
  ///    (src\Import\GlbReader.cs:1104-1160) and a two-key constant samples to the same value at
  ///    every frame a 805-key constant did.
  ///  - QUANTISE: rotation outputs become normalized int16. Quaternion components are already in
  ///    [-1, 1], the worst-case error is 1/65534 (~0.002 degrees) and GlbReader.cs:2212 already
  ///    decodes the form, so nothing on the read side changes.
  ///
  /// It deliberately does NOT resample to a lower rate - see tools\ppzip.py:25-30 for the
  /// measurement that rules it out.
  /// </summary>
  internal static class GlbZip
  {
      internal const int Float = 5126;
      internal const int Short = 5122;
      /// <summary>A quaternion component that survives the int16 round trip to within this is the
      /// same rotation: 1/32767 is the quantum, half of it is representation rather than error.</summary>
      internal const float QuantMaxError = 1.0f / 65534.0f;
      /// <summary>How still a curve has to be to count as constant, as a QUANTITY rather than a float
      /// tolerance: 1e-6 of a quaternion component is ~1e-4 degrees, of a translation 1 micrometre.
      /// ppzip uses this one number for translation, rotation, scale and weights alike
      /// (tools\ppzip.py:88-98) and so does this - a per-path knob would be a knob nobody can set.</summary>
      internal const float StillEpsilon = 1e-6f;

      /// <summary>What one run did, for the sentence the panel shows.</summary>
      internal sealed class Stats
      {
          internal int Collapsed;    // curves rewritten to two endpoint keys
          internal int Quantised;    // rotation curves stored as normalized int16
          internal int Skipped;      // samplers left alone: strided, sparse, STEP, unreadable
          internal int Shared;       // outputs left alone because more than one clip names them
          internal long KeysBefore;
          internal long KeysAfter;
      }

      /// <summary>One accessor as a flat float run. FLOAT and normalized SHORT are both understood,
      /// which is what makes the tool idempotent - a second run reads back what the first wrote.
      /// Returns null for a form Packed() would have refused.</summary>
      internal static float[] ReadFloats(GlbDocument doc, int accessorIndex) { ... }

      /// <summary>False for an accessor this tool must not touch, because ReadFloats would MISREAD
      /// it. glTF lets a bufferView declare a byteStride and an accessor be sparse; both are legal on
      /// a sampler and both mean the values are not the flat little-endian run ReadFloats assumes.
      /// Reading one as if it were would splice padding or a neighbour into the curve and then write
      /// that corruption back. Also false for a componentType that is neither FLOAT nor normalized
      /// SHORT - ppzip exits there (ppzip.py:65); inside OnGUI a skip is the only survivable answer.</summary>
      internal static bool Packed(GlbDocument doc, int accessorIndex) { ... }

      /// <summary>True when every element of the curve equals the first one, component-wise.</summary>
      internal static bool IsConstant(float[] values, int stride) { ... }

      /// <summary>The value block for one curve: little-endian float32, or normalized int16 with
      /// q = round(v * 32767) clamped to +-32767 - NOT -32768, which GlbReader.cs:2212 would decode
      /// as -1.0000305 and clamp anyway. Round-trip exactness first.</summary>
      internal static byte[] Pack(float[] values, bool quantise) { ... }
  }
  ```
  `Packed` reads `bufferView` / `sparse` / `componentType` / `type` through `GlbSlim.Int`, `GlbSlim.Str`,
  `GlbSlim.Get`, and compares the view's `byteStride` (absent, or exactly
  `GlbSlim.ElementSize(accessor)`). `IsConstant` returns false when `values.Length <= stride`.

- [ ] **Step 4: Port `ppzip.selfcheck`'s value-level assertions.** In `GlbZipTests.Run()`, build the
  same synthetic document `ppzip.py:211-238` builds (4 identical quaternion keys + one moving 2-key
  curve, 4 bufferViews, 4 accessors, 1 animation, 2 rotation channels), serialise it with
  `GlbDocument` and check 8 things:
  1. `ReadFloats` of the float accessor returns the 16 authored values.
  2. `ReadFloats` of a normalized-SHORT accessor returns them within `QuantMaxError` (write it with `Pack`).
  3. `Packed` is true for both sampler outputs.
  4. `Packed` is false once `bufferViews[2].byteStride = 32` is set.
  5. `Packed` is false for an accessor carrying `sparse`.
  6. `Packed` is false for componentType 5125 (UNSIGNED_INT).
  7. `IsConstant(still, 4)` true, `IsConstant(moving, 4)` false.
  8. `Pack(new[] { -1f, 1f, 0f }, true)` gives shorts `-32767, 32767, 0` (never -32768).
  - Gate prints: `GLB-ZIP PASS, 8 check(s)`.
  - Run: `dotnet run --project tests\ObjCodecTests -c Release`
  - Expected: `GLB-ZIP PASS, 8 check(s)` among all-green output.

- [ ] **Step 5: Build the mod DLL.**
  - Run: `dotnet build -c Release`
  - Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`

- [ ] **Step 6: Commit.**
  - `git -C E:\DEV\PhoenixPoint\ContentTool add -A && git -C E:\DEV\PhoenixPoint\ContentTool commit -m "feat(glb): GlbZip readers, constant test and int16 rotation packing"`

---

### Task 2: `GlbZip.Zip` - plan, rewrite, dedup, and hand the compaction to `GlbSlim.Trim`

The half of `ppzip.zip_anims` that WRITES, plus the two guards the measurements above forced.

**Files:**
- Modify: `src\Import\GlbZip.cs` (add `Plan` + `Zip`)
- Modify: `src\Import\GlbSlim.cs:108` (Guard: refuse a bufferView with `extensions`)
- Modify: `tests\ObjCodecTests\GlbZipTests.cs`

- [ ] **Step 1: Write the failing fixture gate.** Add 10 checks (total 18):
  9. `lib\u8_rootfold.glb`: `Zip(doc, true, true)` then `GlbDocument.Write()` is **strictly smaller
     than 2,240 B** (ppzip measures 2,212 B), `Stats.Quantised == 1`, all 1 clip present.
  10. `lib\u8_probe.glb`: 5 clips still present afterwards, `Stats.Quantised == 137`, and the written
      size is LARGER than 349,468 B (ppzip measures 377,256 B) - the shared-bufferView case is a
      documented growth, not a bug to chase.
  11. `lib\u9_probe.glb`: does NOT throw; all 4 clip names survive in file order
      (`Walk,walk,Morphs,Hold`); accessor 8 keeps `componentType` 5126 and its original `count`
      (shared by three clips), and `Stats.Shared >= 1`.
  12. `lib\u9_probe.glb`: the STEP sampler of `Hold` still reads `"interpolation": "STEP"` and its
      output is untouched.
  13. `lib\u12_norm.glb`: `GlbSlim.Guard(doc, empty, true)` returns non-null (keys 1 != accessors 5).
  14. `lib\u12_probe.glb` and `lib\u12_uv.glb`: same, non-null.
  15. `lib\u10_probe.glb`: Guard returns non-null naming the bufferView extension (this is the NEW
      condition; without it the file passes and is silently invalidated).
  16. A synthetic doc from Task 1 with two constant rotation curves sharing one input accessor:
      exactly ONE new 2-key input accessor is created, both samplers point at it (the dedup of
      `ppzip.py:181-188`).
  17. A synthetic clip whose EVERY curve is constant is left dense - `Stats.Collapsed == 0` and the
      input accessor still has its 4 key times (`ppzip.py:145-151`: collapsing the last dense channel
      would let `GlbReader`'s rate drop and the clip would come out LONGER than authored).
  18. After `Zip`, `doc.Dirty` is true and `buffers[0].byteLength == doc.Bin.Length`.
  - Run: `dotnet run --project tests\ObjCodecTests -c Release`
  - Expected: the gate fails (`GLB-ZIP FAIL: ...`) - nothing is implemented yet.

- [ ] **Step 2: Add the bufferView-extension refusal to `GlbSlim.Guard`.** In `src\Import\GlbSlim.cs`,
  inside the loop at `:119-122` that already refuses a foreign `buffer`, add:
  ```csharp
  if (Get(Obj(view), "extensions") != null)
      return "this .glb keeps a bufferView behind an extension (EXT_meshopt_compression writes its " +
             "own byteOffset inside that block), and moving the view would leave that offset pointing " +
             "at the old bytes. Refusing.";
  ```
  This closes the same hole in the shipped TRIM path, which is why it lives here and not in a
  zip-local guard.

- [ ] **Step 3: Implement `Plan` and `Zip`.** Add to `src\Import\GlbZip.cs`:
  ```csharp
  /// <summary>
  /// What a run would do to ONE sampler output accessor, decided across the WHOLE document rather
  /// than per clip. ppzip decides per clip (ppzip.py:125-201) and an accessor two animations share is
  /// rewritten twice - the second read comes out of the stale blob, which is the struct.error
  /// lib\u9_probe.glb raises. Here an output named by more than one animation, by two different
  /// target paths, or by any sampler that is not LINEAR-and-packed, is left exactly as it is.
  /// </summary>
  private sealed class Curve
  {
      internal int Output;        // accessor index
      internal int Input;         // accessor index of its key times
      internal int Animation;     // which clip names it, -1 once a second one does
      internal string Path;       // "rotation" | "translation" | "scale" | "weights", null once two disagree
      internal bool Usable;       // false = leave the accessor alone entirely
      internal float[] Values;
      internal int Stride;
      internal bool Constant;
  }

  /// <summary>Read every animation sampler once, from the ORIGINAL bin, and decide its fate.</summary>
  private static Dictionary<int, Curve> Plan(GlbDocument doc, Stats stats) { ... }

  /// <summary>
  /// Rewrite every sampler this document lets us rewrite, then hand the leftovers to the pass that
  /// already exists: GlbSlim.Trim with an empty drop set drops the accessors and bufferViews nothing
  /// points at any more and compacts BIN (GlbSlim.cs:147-224) - which is exactly what
  /// ppzip.zip_anims delegates to ppslim.slim with a regex matching no clip (ppzip.py:205-207).
  /// </summary>
  /// <param name="constant">Collapse a curve that never moves to its two endpoint keys.</param>
  /// <param name="quantise">Store rotation outputs as normalized int16.</param>
  /// <returns>What was done, for the panel's sentence.</returns>
  internal static Stats Zip(GlbDocument doc, bool constant, bool quantise) { ... }
  ```
  `Zip` algorithm, one-to-one with `ppzip.zip_anims`:
  1. `Plan(doc, stats)` - `path_of` per animation from its `channels` (`ppzip.py:128-131`), samplers
     with no channel or `interpolation != "LINEAR"` skipped, `Packed` failures counted in
     `Stats.Skipped`, values read with `ReadFloats` from the untouched `doc.Bin`, `Constant` from
     `IsConstant`. A second animation naming an output sets `Animation = -1`, `Usable = false` and
     counts one `Stats.Shared`.
  2. Per animation, `collapse = constant && !(every usable curve of this clip is Constant)`
     (`ppzip.py:151`).
  3. Per usable curve, in accessor order: `quant = quantise && Path == "rotation"`. When
     `collapse && Constant`, take `Values[0..Stride]` twice and give the sampler a 2-key input -
     deduped per clip on `(oldInput, times[0], times[last])`, its own appended bufferView, a fresh
     accessor with `componentType` FLOAT, `count` 2, `type` SCALAR and **`min` / `max`, which glTF
     requires on a sampler input** (`src\Import\GlbCodec.cs:535-538`).
  4. `Place`: append `Pack(values, quant)` into a per-clip block 4-byte aligned, set the accessor's
     `byteOffset`, `count`, `componentType`, `normalized` (set for quant, REMOVED otherwise) and
     **remove `min` / `max`** - they described the old data and glTF requires none on an output
     (`ppzip.py:160-167`).
  5. Append each clip's block as one bufferView, point its accessors at it, set `doc.Bin` to the grown
     array, update `buffers[0].byteLength`, set `doc.Dirty = true`.
  6. `GlbSlim.Trim(doc, new HashSet<int>())`. Note its early return at `GlbSlim.cs:170-171`: when
     nothing became unreferenced (the shared-view case) it does nothing, which is correct and is why
     step 5 maintains `byteLength` itself.
  - Run: `dotnet run --project tests\ObjCodecTests -c Release`
  - Expected: `GLB-ZIP PASS, 18 check(s)`.

- [ ] **Step 4: Build.**
  - Run: `dotnet build -c Release`
  - Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`

- [ ] **Step 5: Commit.**
  - `git -C E:\DEV\PhoenixPoint\ContentTool add -A && git -C E:\DEV\PhoenixPoint\ContentTool commit -m "feat(glb): GlbZip.Zip collapses constant curves and quantises rotations, guard refuses view extensions"`

---

### Task 3: Preservation, idempotence and the pose gate

The tests §9 calls for by name. Nothing in `src\` changes here - this task exists because "it still
animates" is a claim about VALUES, and the only honest way to make it is to sample the curves.

**Files:**
- Modify: `tests\ObjCodecTests\GlbZipTests.cs`

- [ ] **Step 1: Write the preservation and idempotence checks.** Add 9 checks (total 27):
  19. **Preservation, `lib\u8_probe.glb`:** for every accessor NOT named by an animation sampler, the
      bytes it spans (its view's `byteOffset` + its own `byteOffset`, `count * ElementSize`) are
      byte-identical before and after. Compare by value, not by index - `Trim` renumbers.
  20. Same over `lib\u8_rootfold.glb`.
  21. **Images survive:** every `images[i].bufferView`'s bytes are byte-identical before and after,
      on `lib\u8_probe.glb` and on `local\PpFit\Content\Models\tiffany_ppfit.glb` when present (6 images).
  22. **Skins survive:** every `skins[i].inverseBindMatrices` accessor's bytes are byte-identical.
  23. **Idempotence, `lib\u8_rootfold.glb`:** zip -> write -> load -> zip -> write is **byte-identical**
      to zip once.
  24. **Idempotence, `lib\u8_probe.glb`:** same, byte-identical.
  25. **Idempotence, real world:** `local\PpFit\Content\Models\tiffany_ppfit.glb` (36,254,816 B) is a
      FIXED POINT - zipping it produces a byte-identical file, because it is already zipped
      (`Stats.Collapsed == 15149`, `Stats.Quantised == 27284`, `KeysBefore == KeysAfter == 2721855`).
      Print a skip line and do not fail when the path is absent; `local\` is gitignored.
  26. **Clip / channel / key preservation:** for every fixture, the animation count, each clip's name,
      its channel count and every channel's `target.node` + `target.path` are unchanged, and
      `Stats.KeysBefore == Stats.KeysAfter` on any file where nothing collapsed.
  27. **No quaternion drifts off the unit sphere:** every quantised rotation key read back has `|q|`
      within 1e-4 of 1.
  - Run: `dotnet run --project tests\ObjCodecTests -c Release`
  - Expected: fails until step 2's sampler exists, then `GLB-ZIP PASS, 27 check(s)`.

- [ ] **Step 2: Write the Unity-free pose sampler and the pose check.** In `GlbZipTests.cs`, private
  to the gate (it is a test oracle, not product code):
  ```csharp
  /// <summary>
  /// Sample one animation channel at a time, over the RAW curves, with no Unity and no GlbReader:
  /// the gate has to be able to disagree with the importer. LINEAR between the two bracketing keys,
  /// clamped at both ends; rotations are slerped over the shorter arc the way GlbReader does
  /// (src\Import\GlbReader.cs:1472-1485), because a component-wise lerp takes the same path at the
  /// wrong speed and would fail this test for a reason that is not the zip's fault.
  /// </summary>
  private static float[] SampleAt(float[] times, float[] values, int stride, float t) { ... }
  ```
  Check 28: for every fixture, for every usable sampler, sample at **17 times** evenly spaced over
  `[times[0], times[last]]` before and after the zip and compare:
  - rotation: every component within `2 * GlbZip.QuantMaxError` (3.05e-05) - the bound is twice the
    per-key quantum because an interpolated value sits between two quantised keys and cannot leave
    their interval;
  - translation / scale / weights: **exactly equal**, since nothing quantises them and a collapse only
    ever replaces a curve that was already constant.
  - Gate prints: `GLB-ZIP PASS, 28 check(s)` plus the skip line when tiffany is absent.
  - Run: `dotnet run --project tests\ObjCodecTests -c Release`
  - Expected: `GLB-ZIP PASS, 28 check(s)`.

- [ ] **Step 3: Commit.**
  - `git -C E:\DEV\PhoenixPoint\ContentTool add -A && git -C E:\DEV\PhoenixPoint\ContentTool commit -m "test(glb): zip preservation, idempotence and a Unity-free pose comparison"`

---

### Task 4: `SlimJob.Zip` - stages, cancel, atomic save, read-back

Wrap the passes in the §9 contract, on the ONE copy of the swap that already ships.

**Files:**
- Modify: `src\Import\SlimJob.cs:43,57,113,140`
- Modify: `tests\ObjCodecTests\GlbZipTests.cs`

- [ ] **Step 1: Write the failing job gate.** Add 6 checks (total 34):
  29. `Zip` on `lib\u8_rootfold.glb` copied to a temp dir: destination exists and is smaller than the
      source; the SOURCE is byte-identical to what it was.
  30. `Zip` with a token cancelled before the call: `OperationCanceledException`, destination does not
      exist, source untouched.
  31. After any run - completed, cancelled or refused - no `*.ct_tmp` is left in the directory.
  32. Progress fires at least 6 times with `Done <= Total` and the last snapshot's `Stage == "Done"`.
  33. `Zip` on `lib\u8_probe.glb`: returns a sentence containing `would grow`, and the destination is
      NOT created (a rewrite that makes a file bigger is not a save).
  34. `Zip` on `lib\u12_norm.glb`: `InvalidOperationException` carrying the guard's own words; nothing
      written.
  - Run: `dotnet run --project tests\ObjCodecTests -c Release`
  - Expected: compile error / `GLB-ZIP FAIL` - not implemented.

- [ ] **Step 2: Implement `Zip` and `StartZip`.** In `src\Import\SlimJob.cs`:
  - Change `At` (`:140`) to take the stage table: `private static void At(CancellationToken cancel,
    Action<SlimProgress> publish, string[] stages, int stage, string message)`, and update the five
    call sites in `Execute` (`:66,69,72,76,79`) to pass `Stages`.
  - Add beside `Stages` (`:43`):
    ```csharp
    /// <summary>The zip run's six checkpoints. Verify is last and reads the file back through the
    /// game's own importer, because "it still animates" is the only question worth answering.</summary>
    private static readonly string[] ZipStages = { "Load", "Plan", "Guard", "Zip", "Write", "Verify" };
    ```
  - Add after `Execute` (`:106`):
    ```csharp
    /// <summary>
    /// The zip run: load, plan, guard, rewrite, save, read back. Same shape and same guarantees as
    /// Execute - pure, no thread affinity, and the destination is only ever touched by the swap of a
    /// finished .ct_tmp.
    /// </summary>
    /// <param name="constant">Collapse curves that never move to two endpoint keys.</param>
    /// <param name="quantise">Store rotation outputs as normalized int16.</param>
    /// <exception cref="OperationCanceledException">Cancelled before the swap; nothing was written.</exception>
    /// <exception cref="InvalidOperationException">The guard refused; its words are the message.</exception>
    internal static string Zip(string src, string dst, bool constant, bool quantise,
                              CancellationToken cancel, Action<SlimProgress> publish) { ... }

    /// <summary>Zip on the pool, exactly as Start runs Execute. Both callbacks land on the WORKER
    /// thread.</summary>
    internal static void StartZip(string src, string dst, bool constant, bool quantise,
                                  CancellationTokenSource cts, Action<SlimProgress> onProgress,
                                  Action<string> onComplete) { ... }
    ```
  `Zip` algorithm:
  1. `At(..., ZipStages, 0, "Reading " + Path.GetFileName(src))`, `GlbDocument.Load(src)`.
  2. Stage 1 `"Reading every sampler"`, stage 2 `"Checking what a rewrite would touch"` ->
     `GlbSlim.Guard(doc, new HashSet<int>(), true)`, throw `InvalidOperationException` on a refusal.
  3. Stage 3 -> `GlbZip.Zip(doc, constant, quantise)`.
  4. Stage 4 -> `doc.Write(tmp)`. **Then compare `new FileInfo(tmp).Length` with the source's:** when
     it is not smaller, return
     `"would grow by N B, so nothing was written - this .glb interleaves animation with mesh data in
     shared bufferViews, and the old keys cannot be freed"` WITHOUT swapping. The existing `finally`
     deletes the temp.
  5. Otherwise swap exactly as `Execute` does (`SlimJob.cs:83-84`): `File.Replace` when `dst` exists,
     else `File.Move`.
  6. Stage 5 `"Verify"`: read the written file back through the game's own importer -
     `GlbReader.Read(File.ReadAllBytes(dst), clips)` (`src\Import\GlbReader.cs:151`) - and append
     `"reads back as N clip(s)"` to the sentence, or the refusal's own message inside a
     `catch (Exception ex)` so a verify failure is reported rather than thrown after a successful swap.
  7. Sentence: `"C curve(s) collapsed, Q rotation(s) as int16, S left alone, H shared; B1 B -> B2 B
     (-P%); reads back as N clip(s)"`.
  - Run: `dotnet run --project tests\ObjCodecTests -c Release`
  - Expected: `GLB-ZIP PASS, 34 check(s)`.

- [ ] **Step 3: Build.**
  - Run: `dotnet build -c Release`
  - Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`

- [ ] **Step 4: Commit.**
  - `git -C E:\DEV\PhoenixPoint\ContentTool add -A && git -C E:\DEV\PhoenixPoint\ContentTool commit -m "feat(glb): SlimJob.Zip with the same cancel and atomic swap, and a read-back verify"`

---

### Task 5: the ZIP mode of `SlimPanel`

**Files:**
- Modify: `src\Dev\SlimPanel.cs:34-232`

- [ ] **Step 1: Add the mode and its options block.** In `src\Dev\SlimPanel.cs`:
  - Fields beside `force` / `inPlace` (`:46-47`):
    ```csharp
    /// <summary>false = SLIM (drop clips), true = ZIP (rewrite how the same curves are stored).
    /// One panel rather than two because every field above the middle block - the browser, the
    /// intent queue, the progress trio, the writes line - is the same panel either way.</summary>
    private bool zipMode;
    private bool collapse = true;
    private bool quantise = true;
    ```
  - After the Browse row (`:94`), a mode row:
    `zipMode = GUILayout.Toggle(zipMode, " ZIP (rewrite curves, keep every clip)");` - enqueued like
    every other press, never assigned mid-layout (rule 1 of the class remark, `:17-24`).
  - `Clips(width)` (`:96`) becomes `if (zipMode) Options(); else Clips(width);`. Both must emit the
    SAME NUMBER OF CONTROLS across the Layout and repaint passes of one frame, which they do because
    `zipMode` is only ever changed in the intent drain.
  - `Options()` draws two toggles and the census summary the file already has:
    `collapse` (" collapse curves that never move"), `quantise` (" rotations as int16 (0.002 deg)"),
    then a label: `census.Length + " clip(s), " + Bytes(total AccessorBytes) + " of animation - no clip is dropped"`.
  - The title label (`:86`) becomes mode-dependent: `"GLB ZIP - shrink the animation without dropping
    a clip"` / the existing slim line.
  - The force/in-place row (`:100-101`): `force` is drawn only in SLIM mode (zip drops nothing);
    `inPlace` stays in both.
  - The writes line (`:114`): `Beside` gives `foo.zip.glb` in ZIP mode. Add
    ```csharp
    private static string Beside(string path, string tag) { ... }   // foo.glb -> foo.<tag>.glb
    ```
    and keep the existing `Beside(path)` as `Beside(path, "slim")`.
  - `Run()` (`:194`) branches: in ZIP mode call
    `SlimJob.StartZip(sourcePath, inPlace ? sourcePath : Beside(sourcePath, "zip"), collapse, quantise, cts, ...)`
    with the same two callbacks, and seed `progress = new SlimProgress("Queued", 0, 6, "waiting for a worker")`
    (six stages, not five).
  - The RUN button's enable condition (`:102`) drops the `census.Length > 0` requirement in ZIP mode
    only if the file has animations - keep it: a file with no clips has nothing to zip either.

- [ ] **Step 2: Build.**
  - Run: `dotnet build -c Release`
  - Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`

- [ ] **Step 3: Re-run the offline gates** (the panel is not covered by them, but the linked src files
  are and this catches a signature drift).
  - Run: `dotnet run --project tests\ObjCodecTests -c Release`
  - Expected: `GLB-ZIP PASS, 34 check(s)`, every other gate PASS.

- [ ] **Step 4: Commit.**
  - `git -C E:\DEV\PhoenixPoint\ContentTool add -A && git -C E:\DEV\PhoenixPoint\ContentTool commit -m "feat(glb): a ZIP mode on the slim panel, one browser and one progress row for both"`

---

### Task 6: In-game acceptance on `D:\PP-Instance3`

**`D:\PP-Instance2` belongs to another session - do not deploy to it, connect to it, or kill anything
in it.** Everything here runs against `D:\PP-Instance3` with
`-PPRoot 'D:\PP-Instance3' -ProfileId 76561197996210593`.

**Files:** no source changes. Record the run as an acceptance section appended to THIS file.

- [ ] **Step 1: Deploy.**
  - Run: `E:\DEV\PhoenixPoint\ContentTool\deploy.ps1 -PPRoot 'D:\PP-Instance3'`
  - Expected: it reports the DLL and `meta.json` written into that install's `Mods` folder.

- [ ] **Step 2: Get a geoscape and open the bench.** Wait until `connect state` actually ANSWERS
  before sending anything else (a still-initialising game hangs for minutes and looks like an engine
  bug):
  ```powershell
  cd E:\DEV\PhoenixPoint\PPCLI
  .\ppcli.ps1 connect state -PPRoot 'D:\PP-Instance3' -ProfileId 76561197996210593
  .\ppcli.ps1 plan .\plans\start-campaign.json -PPRoot 'D:\PP-Instance3' -ProfileId 76561197996210593
  .\ppcli.ps1 connect console '{"command":"ct_bench","args":["open"]}' -PPRoot 'D:\PP-Instance3' -ProfileId 76561197996210593
  ```

- [ ] **Step 3: Copy a fixture out of the repo and drive the ZIP mode.** PPCLI cannot click IMGUI, so
  the panel is driven through its own fields and methods, the way the slim acceptance run did
  (`internal-docs\planning\2026-09-02-glb-slim-plan.md:441-460`):
  `AccessTools.Field(typeof(FitBench), "doctorTab" / "advanced" / "slim")`, then
  `AccessTools.Field(typeof(SlimPanel), "zipMode" / "collapse" / "quantise")` and
  `AccessTools.Method(typeof(SlimPanel), "Pick" / "Run")`. Copy `lib\u8_rootfold.glb` (the measured
  shrink case) into the scratchpad first so nothing in the repo is written. Screenshot the panel with
  its options and its result line:
  `.\ppcli.ps1 connect screenshot -PPRoot 'D:\PP-Instance3' -ProfileId 76561197996210593`
  - Expected: `result:` names a smaller output and ends `reads back as 1 clip(s)`; the sibling
    `u8_rootfold.zip.glb` exists, the source is still 2,240 B, and no `.ct_tmp` is left behind.

- [ ] **Step 4: Repeat on a file that must be REFUSED and one that must GROW.** `Pick` +
  `Run` `lib\u12_norm.glb` (Draco) and `lib\u8_probe.glb` (shared views), screenshotting each result.
  - Expected: the Draco file reports the guard's refusal and writes nothing; `u8_probe` reports
    `would grow by ... nothing was written` and writes nothing. Both are the point of the run, not
    failures of it.

- [ ] **Step 5: Prove it still animates, through the game's own importer.** Feed the zipped file to
  the Doctor - `AccessTools.Method(typeof(ModelDoctor), "PickFile")` on the bench's `doctor`
  (`src\Dev\ModelDoctor.cs:96`) - and screenshot the verdict.
  - Expected: a VERDICT, not a refusal - the same outcome the unzipped file produces against the same
    prototype target, which is what shows the mesh, skin and images came through the rewrite intact.
  - Then read the clip values back in-process, which is the animation half of the proof:
    ```powershell
    .\ppcli.ps1 connect call '{"op":"get","target":"@type:Morgott.ContentTool.Dev.ModelDoctor","member":"..."}' -PPRoot 'D:\PP-Instance3' -ProfileId 76561197996210593
    ```
    The `Verify` stage of `SlimJob.Zip` already ran `GlbReader.Read(bytes, clips)`
    (`src\Import\GlbReader.cs:151`) on the written file and put the clip count in the result line, so
    the screenshot of step 3 is the primary evidence; this call is the cross-check that the same file
    imports again on demand.
  - **Deviation from the brief, grounded:** `FitAnim` cannot play this. It binds the LIVE actor's rig
    and catalogues the clips the GAME would play for that character (`src\Dev\FitAnim.cs:120-155`,
    `Resolve` at `:202`, `ModClips` from `CreatureBuild` at `src\Dev\FitBench.cs:782`); an arbitrary
    imported `.glb`'s clips only reach a rig through the bake pipeline
    (`src\Project\ContentProject.cs:659`). The importer read-back is the reachable proof that the
    curves survived, and the offline pose gate (Task 3, check 28) is the proof that their VALUES did.

- [ ] **Step 6: Owner handoff.** Append an acceptance table to this file in the shape the slim plan
  uses (`2026-09-02-glb-slim-plan.md:441-471`): install, build stamps, fixture, one row per action
  with expected / observed / verdict, screenshot paths, and any observation that is not a defect.
  Present the screenshots to the owner for the visual check that closes the slice.

---

## Task 6 acceptance run - 2026-09-02, `D:\PP-Instance3`

Real run only, every figure read off the running game. Install `D:\PP-Instance3`, profile
`76561197996210593`, PPBridge `build=46b377c2`, `ContentTool.dll` written 2026-09-02 16:20:11 by
`.\deploy.ps1 -PPRoot 'D:\PP-Instance3'` off HEAD `1fc51c8` (offline gates first: `GLB-ZIP PASS,
36 check(s)`, `GLB-SKEL PASS, 49 check(s)`, `dotnet build -c Release` 0 errors). Instance3's
`aa\catalog.json` was verified still the restored copy - 1,670,824 B, identical size and timestamp to
`catalog.json.ct-backup` - before anything was launched. `D:\PP-Instance2` and the user's own Steam
install were never deployed to, connected to, launched or killed.

The game was launched by hand (`PhoenixPointWin64.exe -mods`; PPCLI `run`/`batch` stop the game they
launch), the gate waited for with `connect state`, the geoscape reached with
`.\ppcli.ps1 plan .\plans\start-campaign.json '{"difficultyIndex":1}'` and the bench opened with
`.\ppcli.ps1 connect console '{"command":"ct_bench","args":["open"]}'` - every PPCLI call carrying
`-PPRoot 'D:\PP-Instance3' -ProfileId 76561197996210593`.

IMGUI cannot be clicked through PPCLI, so the panel was driven on its own seams:
`AccessTools.Field(typeof(FitBench), "doctorTab" / "advanced" / "slim" / "doctor")`, then
`SlimPanel.Pick(path)` / `SlimPanel.Run()` and the private fields `mode`, `inPlace`, `result`,
`running`, `census`, `rotations`, `animBytes`. **The private `Mode` enum is reachable**: PPCLI's own
`{"$enum":"Zip","type":"Morgott.ContentTool.Dev.SlimPanel+Mode"}` envelope converts for
`FieldInfo.SetValue`, where a bare JSON value would not.

### The matrix

| # | Action | Expected | Observed | Verdict |
|---|---|---|---|---|
| 1 | ZIP `lib\u8_rootfold.glb` (copied to the scratchpad) | smaller output, `reads back as 1 clip(s)`, source untouched, no `.ct_tmp` | `0 curve(s) collapsed, 1 rotation(s) as int16, 0 left alone, 0 shared; 2240 B -> 2212 B (-1,3%); reads back as 1 clip(s)`. Sibling `u8_rootfold.zip.glb` **2,212 B** - the exact figure `tools\ppzip.py` measures - source still 2,240 B, 0 `.ct_tmp` | PASS |
| 2 | ZIP `lib\u8_probe.glb` | refused as "would grow", nothing written | `would grow by 27788 B (349468 B -> 377256 B), so nothing was written - this .glb interleaves animation with mesh data in shared bufferViews, and the old keys cannot be freed`. **377,256 B is ppzip's own measured growth, to the byte.** No sibling written | PASS |
| 3 | ZIP `lib\u12_norm.glb` (Draco) | the guard's refusal, nothing written | `this .glb names a bufferView 1 times where its 5 accessor(s) and 0 image(s) account for 5. Something the trim does not walk owns buffer data here - a sparse accessor, Draco or meshopt compression, an unknown extension - and trimming would cut it loose. Refusing.` No sibling, 0 `.ct_tmp` | PASS |
| 4 | ZIP a REAL rigged model - `demos\CustomCreature\Content\Models\cyborg_spider.glb` (1,481,244 B, 7 clips, 329 rotation channels, 495 KB of animation) | smaller, every clip back | `269 curve(s) collapsed, 329 rotation(s) as int16, 0 left alone, 0 shared; 1481244 B -> 1130496 B (-23,7%); reads back as 7 clip(s)`. ppzip on the same file: 269 collapsed / 329 quantised / 1,130,808 B - the same passes, the C# output 312 B smaller | PASS |
| 5 | the zipped file into the Doctor | a VERDICT, not a refusal | `spider.zip.glb` against the live `cyborg_spider_skin` renderer (49 bones): **BY NAME**, **0** diagnostic rows - `1226 verts, 1552 tris, 49 joints, 3 influence(s)/vertex` | PASS |
| 6 | bind it (Preview) | the zipped mesh on the prototype's rig | `preview: skinned BY NAME onto the target's own 49 bones, carrying the file's own weights (bind poses from the shipped mesh, 49 joints matched, order remapped; vertex 708 is shared, weight0=0.333)` | PASS |
| 7 | play one of the STANDING CREATURE's clips on it | it moves | transport bound **30** clips off the creature's own controller; `FitAnim.Select("cyborg_spider_spider_walk")` -> `chosen` 29, `playing` true -> `t` **0.2858** then **0.8040**, strip reading `PAUSE  0.13 / 0.29s`, `loop` on. Two mid-clip screenshots, the skeleton overlay all green (`skeleton: by name`) over the zipped mesh | PASS |
| 8 | close | the bench leaves cleanly | `ct_bench closed - the screen you came from was never left, so it is still there.`, `FitAnim.Driving` false | PASS |

### Two deviations from step 3/5 as written, both forced and both recorded

1. **The "verdict + preview + animate" half of step 5 was done on `cyborg_spider.glb`, not on
   `u8_rootfold.glb`.** `u8_rootfold.glb` is a 2,240 B synthetic with one rotation curve and no
   skinned mesh, so it has nothing for the Doctor to bind and nothing to look at. The shrink /
   growth / guard cases stay on the fixtures the plan names (rows 1-3); the binding claim is made on
   a real rigged model zipped by the same run (rows 4-7), which is the only way that claim means
   anything.
2. **The clips played are the PROTOTYPE's, exactly as step 5's own deviation note says.** `FitAnim`
   catalogues the clips the GAME would play for the character standing there
   (`src\Dev\FitAnim.cs:120-155`); an imported `.glb`'s own clips only reach a rig through the bake.
   So the proof is: the zipped mesh binds BY NAME onto the rig, and the rig's own clip drives it.
   The VALUES of the zipped curves are proven by Task 3's offline pose gate, and their survival by
   the `Verify` stage's `reads back as 7 clip(s)`.

### Observation that is not a zip defect - the prototype picker, for the owner

`FitBench.ShowPrototype` was used first, so that the Doctor could be pointed at a prototype SLOT
target. On this install it never produced one for the mod creature:

- showing the prototype the bay is ALREADY displaying fires no `AddonsCharacterBuilder.OnCharacterRebuilded`,
  so `Posed` -> `Retarget` (`src\Dev\FitBench.cs:1351-1359`) never runs and `pendingVariant` is left
  set - `SlotTargets()` stays empty and the panel shows `prototype -` with nothing to pick;
- going Human -> `ct_creature_morgott.demo.customcreature` swapped the bay's `AddonsManagerDef`
  correctly but ALSO fired no callback, so the same thing happened;
- calling `Posed()` by hand did consume it, and both of that variant's 2 slots then reported
  `Unavailable = "slot visual unavailable"` with `BoneNames().Count == 0`.

So the Doctor was pointed at the live renderer through its own
`ModelDoctor.PickTarget(SkinnedMeshRenderer, transformPath)` seam - the very renderer a slot target
would have snapshotted (`FitBench.Retarget:758`). Nothing in the ZIP port is involved. **Owner: this
is a prototype-picker gap (slice 1/2), reported not fixed.**

### `Player.log`

**0** occurrences of `Getting control ... in a group with only ...` and **0** ContentTool exceptions
across the whole run (`...LocalLow\Snapshot Games Inc\Phoenix Point\Player.log`, 1,619 lines, copied
to the scratchpad as `pp3-acceptance.log`). The 75 exception lines are all third-party and all
predate the bench: 12 `ArgumentException: Mesh can not have more than 65000 vertices` from
`UnityEngine.UI.Text.UpdateGeometry` (a UGUI text growing past the vertex cap) and 3
`AddressableAssets.InvalidKeyException` from the WeaponAdd demo's own prefab keys at mod-load time.

### Screenshots

`C:\Temp\claude\E--DEV-PhoenixPoint-ContentTool\e31d205c-b842-452c-8655-3d543056001d\scratchpad\shots\`

`zip-01-options.png` (ZIP mode, both passes ticked, the census line), `zip-02-rootfold-result.png`,
`zip-03-probe-would-grow.png`, `zip-04-draco-refused.png`, `zip-05-spider-result.png`,
`zip-06-doctor-verdict-byname.png`, `zip-07-preview-bound.png`, `zip-08-anim-mid-a.png` /
`zip-09-anim-mid-b.png` (the walk clip mid-play on the zipped mesh).

### Still the owner's to check, in game, by eye

1. the zipped model looks identical to the unzipped one at rest and mid-clip - 0.002 degrees is the
   arithmetic claim, his eye is the acceptance;
2. the walk cycle is SMOOTH (this run sampled `t` twice, it did not watch it);
3. the result sentence is readable where it lands in the panel - see the SKEL plan's own note about
   a multi-line result running off the bottom of the window.
