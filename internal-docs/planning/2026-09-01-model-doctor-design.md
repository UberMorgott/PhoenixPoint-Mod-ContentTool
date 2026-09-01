# Model Doctor — design (slice 1 of the in-game authoring roadmap)

Date: 2026-09-01. Status: DRAFT v3 after Codex round-4 review (verdict SHIP WITH FIXES, 17 findings;
every finding re-checked against source before it was applied — see §11). Owner: Morgott.
Peer-reviewed with Codex (thread `01a05e66-5add-7c52-8b0f-736d782ce85d`; memos `C:\Temp\cx\23a85faf*`,
`70322129*`, reviews `1555dec7*`, `f3f24ebb*`).

## 1. Problem

- Today an author learns how a `.glb` will behave only at bake time, after a 12-step loop:
  export → copy into `Content\Meshes\` → hand-write `ppcontent.json` → `ct_project` → enable mod →
  **restart the game** → look. One wrong bone name = the whole loop again.
- Worse than a refusal: on a bone-name mismatch the bake does **not** refuse. `BundleBaker.ReplaceMesh`
  (`src/Bake/BundleBaker.cs:142-205`) tries `SkinFields.RebindByName` (`:192`), and on any exception
  silently **falls back to nearest-bone skinning** (`:197-201`) — one full-weight influence per vertex —
  and only says so in one log line: `"nearest-bone - the file's own weights were NOT used: <reason>"`.
  The model imports, the game runs, and the mesh deforms like a rubber sheet. A non-programmer sees
  "it worked but looks broken" and has no idea why.
- One case DOES refuse and writes nothing: a **skinless source against a rigged target**
  (`BundleBaker.cs:153-156` → `SkinFields.Skinless`, `src/Bake/SkinFields.cs:643-652`; the live twin is
  `SeamSwap.cs:243-246`).
- The other fatal refusals happen earlier, in parsing/baking: external buffers, >128 MB,
  non-triangle primitives, no indices, >4 influences, no normals — but they are ~90 `Bad(...)` exits
  in `src/Import/GlbReader.cs` plus two in `src/Import/ModelBuild.cs:151-155`, all of them untyped
  `FormatException`/`InvalidDataException`, so nothing downstream can tell one from another.
- A runtime "file → live renderer" path already exists — `ct_replace` → `SeamSwap.ReplaceMesh`
  (`src/Dev/SeamSwap.cs:224-274`) → `LiveMesh.Load` (`src/Dev/LiveMesh.cs:35`) + `LiveMesh.Bind`
  (`:114`) — but it is a console verb with a path argument, no report, and it uses the same by-name
  binding with no way to fix a name.

## 2. Goal

**Pick a `.glb` off disk, pick the skinned mesh it should replace, and within seconds see which of
FOUR outcomes the bake would produce — BY-NAME bind / NEAREST-BONE fallback / NOT RIGGED / IMPORT
REFUSED — with every reason listed as what to change in Blender, and with bone-name aliases (the one
fix that is honest to do in-game) applied on the spot and persisted so the real bake produces the same
outcome.**

Success criteria:
- The Doctor's outcome for (file, target, aliases) equals the outcome `BundleBaker.ReplaceMesh`
  reports for the same triple — because both read it out of the SAME pure function,
  `ReplacementDecision.Decide` (§4.2). Tested with frozen fixtures and shared goldens (§8).
- A non-programmer reading a row knows which export checkbox / rename / weight transfer to do.
- Zero new dependencies. Zero Python. Nothing here can leave the game in a bad state: import
  failures become diagnostics, preview is candidate-then-swap, no bundle unload/reload.

Scope statement: this is the **skinned-mesh replacement Doctor**, and only that. The file browser
accepts `.glb`; the target picker lists `SkinnedMeshRenderer`s. A selected SMR whose mesh carries no
bind poses gets `NotRigged`, the same word the bake uses. Static `MeshFilter` targets and `.obj`
sources are NOT reachable in this slice and are not promised.

## 3. Non-goals

Not in this slice: drag-and-drop; static/`MeshFilter` targets and `.obj` sources; the "add new
model/creature/weapon" route (replacement only, and sidecar aliases apply to the replacement path
only — see §5); material-slot or blend-shape checks (the replacement path passes `0, null` for them —
`LiveMesh.cs:217`, `SkinFields.cs:730`); **clip aliases** (replacement mesh import reads no clips —
nothing to alias until the animation slice); any geometry/weight edit; per-bone rest-pose edits;
renaming the GAME's bones; animation retargeting; keyframe editing; progress bars and cancellation
(arrive with the Bake button); UGUI/cloned Phoenix widgets; a GUI for every console command;
upgrading the `ppcontent.json` `replace[]` parser.

## 4. Architecture

### 4.1 Units

| Unit | Responsibility | Depends on |
|---|---|---|
| `Diagnostic`, `DiagnosticReport`, `Outcome` (`src/Doctor/Diagnostic.cs`, new) | Plain data, no UnityEngine type. `Severity {Blocking, Downgrade, Warning, Info}`; `Code` (stable string, §7); `Message` (existing engine wording, verbatim); `Remedy` (Blender sentence); `Subject`; `Side {File, Target, Sidecar}`. `Outcome {ByName, NearestBone, NotRigged, Refused}` — FOUR. Report = ordered rows + outcome. | — |
| `BindingIssue` (`src/Import/SkinCompatibility.cs`, new) | **Severity-free** record of one binding disagreement: `Code`, `Message` (byte-identical to today's throw text), `Subject` (the bone name where there is one), `Side`, `Stage {Skin, Bones}`. Severity is a UI decision made by the Doctor, never carried here — that is what let v2's `Passes`/`Downgrade` contradict itself. | — |
| `ImportRefusedException : FormatException` (`src/Import/ImportRefused.cs`, new) | `Code` + the message the reader already writes. Produced centrally: `GlbReader.Bad(msg)` gains an optional code and defaults to `MalformedGlb`; `GlbReader.Unreadable(name)` yields `UnsupportedGlb`; `ModelBuild.From`'s two refusals get `NoVertices`/`NoNormals`. Only the six catalogued sites (§7) pass an explicit code — the ~85 others fall into `MalformedGlb` and keep their sentence. Deriving from `FormatException` keeps every existing `catch (FormatException)` (`LiveMesh.cs:52`, `BundleBaker.cs:197`, `tests\ObjCodecTests\BoneNames.cs:100`) working unchanged. | — |
| `RigTarget` (`src/Import/SkinCompatibility.cs`, new) | Plain snapshot taken on the main thread, no Unity reference held across frames: `string[] BoneNames` (**nullable** — `smr.bones` absent/empty, `LiveMesh.cs:116-117`), `bool Rigged` (**from bind poses**: `smr.sharedMesh.bindposes.Length > 0`, the same fact `SkinFields.Rigged`, `SkinFields.cs:623-626`, keys on), `int RendererInstanceId`, `int MeshInstanceId`, `int BindPoseCount`, `string TransformPath`, `string MeshName`. The last five are the **fingerprint** re-checked before preview (§4.3). | — |
| `SkinCompatibility.Analyze(SkinnedModel file, IList<string> boneNames) → IList<BindingIssue>` (same file, new; **extracted from** `SkinBinder.Bind`, `GlbReader.cs:2450-2549`) | Every check `Bind` performs today, in `Bind`'s own throw order, as rows instead of a first-failure throw. `Bind` becomes: throw the first `Stage.Skin` issue → `Submeshes` → `Shapes` → throw the first remaining issue → build joints/bindposes. `Stage` exists for exactly one reason: `Submeshes`/`Shapes` sit BETWEEN two groups of checks today (`:2465-2466`) and reordering them would change which sentence an author reads. Material/blend-shape checks stay in `Bind` behind the existing `materialSlots > 0` / `blendShapeNames != null` conditions and are not part of the Doctor. | `SkinBinder.Plain()` |
| `ReplacementDecision.Decide(bool sourceHasArmature, bool targetRigged, bool targetBoneNamesAvailable, BindingIssue firstIssue) → Outcome` (`src/Import/ReplacementDecision.cs`, new, pure, ~10 lines) | **The single definition of the verdict**, called by the Doctor AND by `BundleBaker.ReplaceMesh` (§4.2). No UnityEngine and no `AssetTypeValueField` in its signature, so it is fully testable offline. | `BindingIssue` |
| `AliasMap` (`src/Import/AliasMap.cs`, new) | `IReadOnlyDictionary<string,string>` file-bone → game-bone. `Apply(SkinnedModel)` is a **simultaneous** rename from the immutable original names: for each joint `j`, `JointNames[j]` and `Nodes[JointNodes[j]].Name` are set from the map; index tables (`JointNodes`, `Joints`, `Weights`, `InverseBindMatrices`, node parents, animation track node indices) are untouched. `JointNames` is a `readonly List<string>` (`GlbCodec.cs:125`) so `Apply` writes by index, never reassigns. Validates only what it can see: schema, hash, non-empty values, output collisions. **Keys absent from the file are ignored** and reported one `AliasUnused` warning each; the remaining entries still apply (§5). Whether an OUTPUT names a real target bone is not knowable here — `AliasMap` never sees a target — and is checked in Preflight. | `SkinnedModel` |
| `ReplacementSource` (`src/Import/GlbSource.cs`, new) | Envelope, not a bare model: `SkinnedModel Model`, `string Path`, `string Sha256`, `long Bytes`, `string SidecarPath`, `int AliasesApplied`, `IList<Diagnostic> SidecarRows`, `string AliasLog` (path + every mapping, ready to print). Discarding this is what made v2's `→ SkinnedModel` unable to keep §5's "never silent" promise. | `AliasMap` |
| `GlbSource.ReadReplacement(byte[] bytes, string path) → ReplacementSource` (same file, new, ~30 lines) | The single "read a `.glb` for replacement" helper: `GlbReader.Read` → `AliasMap.LoadSidecar(path)` → `Apply` → envelope. Called by `LiveMesh.Load` (`LiveMesh.cs:46`, which then logs `AliasLog`), by `ContentProject.ImportMesh` (`ContentProject.cs:617`, which carries `AliasesApplied`/`SidecarPath` onto `ImportedMesh` so `BundleBaker.ReplaceMesh`'s `how` string can name them) and by `ReplacementPreflight`. The add-model route (`ContentProject.ImportModel`, `:636`) keeps calling `GlbReader.Read` directly — aliases must not change published bone-path hashes. | `AliasMap` |
| `ReplacementPreflightResult` (`src/Doctor/ReplacementPreflight.cs`, new) | `DiagnosticReport Report`, `Outcome Outcome`, `SkinnedModel Model` (the ALIASED model — what a preview must be built from), `SkinnedModel Original` (pristine names, for re-aliasing without re-parsing), `BakedSkin Baked` (`ModelBuild.From`'s own return; `.Mesh` is the `BakedMesh`), `string Sha256`, `ReplacementSource Source`. | all above |
| `ReplacementPreflight.Run(byte[] bytes, string path, RigTarget target) → ReplacementPreflightResult` (same file) | The pipeline: `GlbSource.ReadReplacement` → `ModelBuild.From` → validate alias OUTPUTS against `target.BoneNames` → `Analyze` (twice: on the UNALIASED model for the outcome per §5, and on the aliased model for the displayed rows) → `Decide` → report. Catches `ImportRefusedException` → `Refused`; catches every other `Exception` at the worker boundary → `ImportFailed` row + `Refused`, with the exception type and stack in the log. Pure: no UnityEngine type in or out. | all above |
| `LiveMesh.Build(SkinnedModel model, string name) → Mesh` (`LiveMesh.cs`, new public seam) | `ModelBuild.From(model, name).Mesh` + the existing private `ToMesh(BakedMesh, string)` (`:62`). Main thread only. `Load()` is rewritten to call `GlbSource.ReadReplacement` + `Build`; behaviour unchanged. | `ModelBuild`, `LiveMesh` |
| `LiveMesh.Bind(..., out BindMode mode)` (`LiveMesh.cs:114`, signature widened) | Today `Bind` RETURNS a sentence and never throws: `ByName` swallows its refusal (`:256-260`) and falls through to nearest-bone, and `null` means "not rigged". A preview cannot tell which happened by reading English. `BindMode {ByName, NearestBone, NotRigged}` is an out parameter; the existing return string is unchanged, so `SeamSwap.cs:248` keeps working with a discard. | — |
| `GlbFileBrowser` (`src/Dev/GlbFileBrowser.cs`, new) | IMGUI panel: drives, up/into, `.glb` filter, recent list (5). Returns a path. No native dialog in this slice. Recents persist to `<persistentDataPath>\ContentTool\doctor-recent.txt` — the mod has no settings store (grep: none), and `ContentToolMain` already owns that directory (`ContentToolMain.cs:65`). | IMGUI helpers in `src/Dev/` |
| `ModelDoctor` (`src/Dev/ModelDoctor.cs`, new) | Orchestration + UI state machine (§4.3/§4.4). Target picker = the `SkinnedMeshRenderer`s under the benched actor, resolved the way `SeamSwap` resolves a `TargetPath`; picking one takes the `RigTarget` snapshot. | all above, `SeamSwap`, `FitBench` |
| `FitBench` (existing, `src/Dev/FitBench.cs`) | Hosts the Doctor as a tab inside `Draw()` (`:1307`). Numeric readouts move behind an "Advanced" toggle (§6). | — |

### 4.2 One decision function, two callers (hard rule)

- `ReplacementDecision.Decide` is the ONLY place the four outcomes are chosen. It mirrors
  `BundleBaker.ReplaceMesh` (`:153-202`) exactly, and the bake is rewritten to ASK it rather than to
  branch in parallel:

  ```csharp
  internal static Outcome Decide(bool sourceHasArmature, bool targetRigged,
                                 bool targetBoneNamesAvailable, BindingIssue firstIssue)
  {
      if (!sourceHasArmature) return targetRigged ? Outcome.Refused : Outcome.NotRigged;
      if (!targetRigged) return Outcome.NotRigged;
      if (!targetBoneNamesAvailable) return Outcome.NearestBone;
      return firstIssue == null ? Outcome.ByName : Outcome.NearestBone;
  }
  ```

  - `!sourceHasArmature && targetRigged` → `Refused`: `BundleBaker.cs:153-156` returns null and writes
    NOTHING, with `SkinFields.Skinless` as the sentence. v2 called this `NearestBone` and was wrong.
  - `!sourceHasArmature && !targetRigged` → `NotRigged`: the guard is skipped, `names` is null
    (`:176-177`), `SkinFields.Rebind` returns false on a mesh with no bind poses (`SkinFields.cs:666`)
    and `how` is `"not rigged - the target carries no bind poses"` (`:184`).
  - `targetBoneNamesAvailable` is `SkinFields.BoneNames(...) != null` in the bake (`:177`) and
    `RigTarget.BoneNames != null && Length > 0` in the Doctor.
  - `firstIssue` is what only the caller can supply. The Doctor passes `Analyze`'s first issue. The
    bake calls `Decide(..., null)`; if the answer is `ByName` it runs `SkinFields.RebindByName`, and
    a refusal there is the same fact arriving late — the catch re-asks with the caught issue.
- **The bake's catch is narrowed** from `catch (Exception)` (`BundleBaker.cs:197`) to
  `catch (FormatException ex)`. That is the only type the binding path refuses with:
  `SkinBinder.Bind` throws `FormatException` at all fifteen sites (`GlbReader.cs:2455-2544`) and
  `RebindByName`'s own width check does too (`SkinFields.cs:739`). A `NullReferenceException` or an
  `IndexOutOfRangeException` from that code is a BUG, and silently downgrading it to nearest-bone is
  how such a bug ships. Accepted consequence: such a bug now fails the bake loudly.
- Name normalisation is `SkinBinder.Plain()` (`GlbReader.cs:2560-2565`: strips only the game's
  `#<bone>_Addon => <part>` decoration; no case folding, no suffixes). Aliases are applied to the
  model before anything reaches `Plain()`, identically for Doctor and bake.
- Severity is assigned by the Doctor when it renders, never by `Analyze`: an issue that makes the
  bake fall back is `Downgrade` (it does not stop the import; it ruins it), and the header says so —
  `NEAREST-BONE — the bake would import this but NOT use your weights (3 reasons)`.

### 4.3 Data flow and state machine

```
Idle ─pick file/target/alias─► Pending(gen N)
   worker: bytes  = File.ReadAllBytes(path)
           sha    = SHA256(bytes)
           result = ReplacementPreflight.Run(bytes, path, targetSnapshot)   // pure, catches everything
           enqueue (gen N, sha, result)
   main (Arm.Update drains ConcurrentQueue):
           if gen != current                      → drop
           if !Fingerprint(target).Equals(snapshot) → report += TargetChanged (Blocking), no preview
           else                                   → Ready(result)
Ready ─Preview─► re-hash the file on disk; sha != session sha → invalidate, re-run, no preview
                 re-snapshot the target; fingerprint differs  → TargetChanged, no preview
                 candidate = LiveMesh.Build(result.Model, name)
                 LiveMesh.Bind(candidate, smr, result.Model, out mode)
                 mode == result.Outcome → destroy previous Doctor-owned mesh, smr.sharedMesh = candidate,
                                          smr.localBounds = candidate.bounds   (candidate-then-swap)
                 mode != result.Outcome → destroy candidate, previous preview untouched,
                                          row PreviewDisagreed (Blocking), re-run preflight
Ready ─edit alias─► re-Apply on the pristine `Original` names → re-run Analyze + Decide only
                    (no re-parse; ModelBuild output is bone-name-independent) → Ready(result')
Ready ─Save aliases─► re-hash the file; sha != session sha → refuse, invalidate, re-run
                      else sidecar write (§5) → row AliasesSaved (Info)
```

- **Generation + fingerprint.** `gen` increments on every change of file path, file bytes hash,
  target snapshot, alias map, or bench actor swap. A result carrying an old `gen` is dropped. That is
  stale-result invalidation, not cancellation (the worker finishes and its result is ignored).
- **The instance id is not enough.** A `SkinnedMeshRenderer` keeps its id while its `sharedMesh`,
  its bind poses and its `bones` array are replaced — by another mod, by the bench's own rebuild, by
  an addon being equipped. The fingerprint compared before every preview is the whole `RigTarget`:
  ordered `BoneNames`, `MeshInstanceId`, `BindPoseCount`, `TransformPath`, `RendererInstanceId`.
- **The bytes are not stable either.** An author re-exports from Blender while the Doctor is open,
  and Save would then bind aliases authored against the OLD joint names to the NEW file's hash. The
  file is re-hashed immediately before Preview and before Save; a mismatch invalidates the session
  and re-runs the preflight instead of writing anything.
- **Ownership.** The worker owns the model until it is enqueued; after that only the main thread
  touches it. `Original` (the pristine `JointNames` + node names) is kept inside the session so alias
  edits always re-derive from originals and are order-independent.

### 4.4 Threading rules

- `OnGUI` renders an immutable `ReportView` snapshot and **enqueues intents** (`PickFile`,
  `SetAlias`, `Preview`, `Save`); `Update` processes intents and queue results. No mutation in `OnGUI`.
- Off the main thread: only `File.ReadAllBytes`, `SHA256` and `ReplacementPreflight.Run` (verified
  UnityEngine-free, §9). Everything that constructs a `UnityEngine.Object` is on the main thread.
- `ReplacementPreflight.Run` catches `Exception` at its own boundary and turns it into an
  `ImportFailed` row, so a worker thread can never take the game down with an unhandled exception.
- One worker job at a time per Doctor; a new intent while a job runs bumps `gen` and starts the next
  job after the current returns.

## 5. Persistence — sidecar next to the `.glb`, never silent

- File `<name>.glb.aliases.json`:
  ```json
  { "schema": 1, "source": { "sha256": "<hex of the .glb bytes>", "bytes": 1234567 },
    "bones": { "<file bone>": "<game bone>" } }
  ```
- Why a sidecar and not the manifest: `replace[]` rows are parsed by regex, one flat `\{[^{}]*\}`
  per row (`ContentProject.ParseReplace`, `src/Project/ContentProject.cs:373-423`); a nested map
  inside a row corrupts every row after it. The parser upgrade is the "manifest domain model" slice.
  Deliberate deferral: sidecar now; when the manifest slice lands the sidecar loader becomes a
  one-release fallback.
- **Never silent.** Wherever a sidecar is applied (Doctor, `ct_replace`, bake) the log prints
  `ReplacementSource.AliasLog` — the sidecar path and every mapping; the Doctor shows an
  `ALIASES ACTIVE (n)` badge; the bake's `ReplaceMesh` `how` string gains
  ` with <n> alias(es) from <sidecar>`.
- **Policy — a sidecar problem never decides the outcome.** Ignoring a sidecar leaves a file that may
  still bind perfectly by name; the BINDING rows decide, and they are computed from the **unaliased**
  model whenever the sidecar was not applied. So:

  | Situation | Row | Severity | Applied? |
  |---|---|---|---|
  | No sidecar | — | — | nothing to apply |
  | `source.sha256` ≠ file | `SidecarStale` | Warning | no |
  | Malformed JSON / unknown `schema` / colliding outputs / empty value | `SidecarInvalid` | Warning | no |
  | Key is not a bone in the file | `AliasUnused` (one per key) | Warning | the other entries ARE |
  | Output is not a bone in the target | `AliasNotATargetBone` (one per key) | Warning | no (whole map) |
  | Saved by the Doctor | `AliasesSaved` | Info | — |

  `AliasNotATargetBone` is checked in `ReplacementPreflight`, not in `AliasMap`/`GlbSource`: neither
  of those ever receives a `RigTarget`, so v2 asked them for an answer they cannot have.
- **Scope.** Applied only on the replacement read (`GlbSource.ReadReplacement`); the add-model route
  (`ContentProject.ImportModel`) ignores sidecars — its published bone-path hashes must not depend on
  a side file.
- **Write.** `Save aliases` writes `<tmp>` then `File.Move` when creating, `File.Replace` when
  updating (`File.Replace` fails if the destination does not exist). Recomputes `sha256` at save,
  from bytes re-read at that moment (§4.3).
- **Copy caveat.** Explorer copies of the `.glb` do not bring the sidecar. Visible, not silent: the
  Doctor opened on the copy shows no badge and the same rows reappear. The "Replace one mesh" wizard
  (next slice) copies both files.

## 6. UI — decluttered bench

Left column, top to bottom:
1. **Source** — path (read-only), `Browse…`, recent (5), one line of counts (vertices / bones /
   file size), `ALIASES ACTIVE (n)` badge when a sidecar applied.
2. **Target** — the picked renderer: transform path, mesh name, bone count; `Change` opens the
   SMR list under the benched actor.
3. **Verdict header** — one of: `BY NAME — your weights will be used` · `NEAREST-BONE — the bake
   would import this but NOT use your weights (n reasons)` · `NOT RIGGED — target has no bind poses` ·
   `IMPORT REFUSED (n reasons)`.
4. **Bone map** (shown when the verdict is not BY NAME because of names) — ONE two-column table:
   left = file bones, right = target bone or `—`; unmatched target bones listed below the table.
   Right-hand cell is a dropdown of unmatched target bones, pre-selected by fuzzy match, **never
   auto-applied**, bijective (a target bone can be chosen once). Editing a cell is an alias.
5. **Other rows** — remaining diagnostics grouped Blocking → Downgrade → Warning → Info; each: icon,
   `Message`, `Remedy` muted. `Side = Target` rows are visually separate ("this is the game's asset,
   not your file"); `Side = Sidecar` rows say which file they came from.
6. **Actions** — `Preview` (enabled iff outcome is `ByName` or `NearestBone` — the author may want to
   see the fallback), `Revert preview`, `Save aliases` (enabled iff map changed and valid),
   `Copy report` (plain text to clipboard — what a non-programmer pastes when asking for help).

Right: viewport with the existing gizmo. **Advanced** toggle (off by default) reveals the
pos/rot/scale readouts, step buttons and per-axis nudges (`FitBench.View`/`Dial`, `:1366`/`:1547`);
default view = gizmo + model scale slider.

Prior art copied: `model-viewer` (open → auto-frame → reset → errors), Blender retarget add-ons
(two-column map, suggest + confirm), `glTF-Validator` (severity grouping). Not copied: generic
inspectors, dope sheets, WndProc drag-drop.

## 7. Diagnostic catalogue

Refusals — outcome `Refused`, `Side=File` unless noted. Existing message text kept verbatim; the
code is added to the throw, the sentence is not touched.

| Code | Where thrown today | Remedy |
|---|---|---|
| `Oversize` | `GlbReader.cs:155-157` | Reduce texture resolution or remove unused meshes/animations |
| `ExternalBuffer` | `GlbReader.cs:239-241` | Export as **binary** `.glb` (glTF Binary), not `.gltf` + `.bin` |
| `NoMesh` | `GlbReader.cs:248-249` | The export contains no mesh — check the mesh object is selected/visible on export |
| `NonTriangle` | `GlbReader.cs:611-613` | Tick *Apply Modifiers* and triangulate the faces (Triangulate modifier or Ctrl+T) |
| `NotIndexed` | `GlbReader.cs:670-671` | Re-export; indexed geometry is the default — a custom exporter setting disabled it |
| `TooManyInfluences` | `GlbReader.cs:616-618` | Weight Paint → *Weights* → *Limit Total* = 4, then re-export |
| `NoVertices` | `ModelBuild.cs:151-152` | The export carries no geometry — export the mesh itself, not an empty object |
| `NoNormals` | `ModelBuild.cs:153-155` | Tick *Normals* under Mesh in the export dialog |
| `UnsupportedGlb` | `GlbReader.Unreadable`, `:2288-2291` | Re-export from Blender with compression and extension add-ons off (the message names the extension) |
| `MalformedGlb` | every other `Bad(...)` in `GlbReader.cs` (~85 sites) | The message already names the cause and the fix; the code exists so the UI can group them |
| `SkinlessOntoRigged` | `BundleBaker.cs:153-156` / `SeamSwap.cs:243-246` via `SkinFields.Skinless` | Give the mesh an Armature modifier with vertex groups weighted to the target's own bones, then re-export |
| `ImportFailed` | new — `ReplacementPreflight`'s worker-boundary catch | Something outside the importer failed (an I/O error, a bug); the log carries the exception type |

There is **no `Draco` refusal**: `KHR_draco_mesh_compression` is accepted in `extensionsRequired`
(`GlbReader.cs:231`) and decoded on the primitive (`:601-608` → `Decompress`, `:1774`). The
`Unreadable(Draco.Extension)` branch (`:2273-2280`) is unreachable dead code today — both call sites
exclude Draco — and is out of scope here (§11, finding 12). Draco DECODE failures are ordinary
`Bad(...)` exits and land in `MalformedGlb` with their own sentences.

Binding issues — produced by `Analyze` with no severity; the Doctor renders them `Downgrade` (the
bake would fall back), `Bind` throws the first one.

| Code | `Stage` | Existing message (`GlbReader.cs`) | Remedy |
|---|---|---|---|
| `TargetBonesUnavailable` | Skin | the target model lists no bones… (`:2454-2456`) | The renderer has no bone list — reload the scene / re-pick the target |
| `NoArmature` | Skin | the file carries no armature… (`:2457-2459`) | Parent the mesh to the armature (Ctrl+P → Armature Deform) and export with *Skinning* on |
| `JointsWeightsMismatch` | Skin | the file's bone weights do not cover every vertex (`:2460-2463`) | Give the whole mesh an Armature modifier with vertex groups and re-export |
| `TargetBoneEmpty` | Bones | the target model's bone N has no name (`:2476-2478`) | *Side=Target* — the game's own asset; re-pick the target |
| `TargetBoneDuplicate` | Bones | the target model has two bones named 'X' (`:2479-2481`) | *Side=Target* — this model cannot be replaced by name |
| `DuplicateFileBone` | Bones | the file has two bones named 'X' (`:2487-2489`) | Rename one of them in Blender |
| `PlainCollision` | Bones | …both name the bone 'X' once the decoration is removed (`:2502-2506`) | Keep the one that belongs to this model and re-export |
| `MissingBone` | Bones | the file does not contain the bone 'X' (`:2514-2517`) | Rename your bone to 'X' — or map it in the table |
| `ExtraBone` | Bones | the file adds the bone 'X' (`:2522-2525`) | Map 'X' to the target bone it stands for, or transfer its weights to its parent and delete it |
| `NotBijective` | Bones | could not be matched one to one (`:2526-2529`) | Check the table for a target bone used twice |
| `InverseBindCount` | Bones | N bind poses for M bones (`:2531-2535`) | Broken export — re-export rather than editing the file |
| `BoneIndexOutOfRange` | Bones | vertex N references bone S but the file has M (`:2541-2544`) | Broken export — re-export |

`NoArmature` is `Blocking`, not `Downgrade`: against a rigged target the bake REFUSES
(`SkinlessOntoRigged`), and against an unrigged target the outcome is `NotRigged`. There is no path
where a skinless file falls back to nearest-bone.

Target-side and session rows:

| Code | Meaning | Severity |
|---|---|---|
| `TargetNotRigged` | `"not rigged - the target carries no bind poses"` (`BundleBaker.cs:184`) | Info, outcome `NotRigged` |
| `TargetChanged` | the renderer, its mesh, its bind-pose count or its bone list changed since it was picked | Blocking (preview only) |
| `SourceChanged` | the `.glb` on disk no longer hashes to the session's `sha256` | Blocking (preview/save only) |
| `PreviewDisagreed` | `LiveMesh.Bind` reported a mode the preflight did not predict | Blocking (preview only) |

Sidecar rows: `SidecarStale`, `SidecarInvalid`, `AliasUnused`, `AliasNotATargetBone` (all Warning),
`AliasesSaved` (Info) — §5.

Exact remedy wording is finalised during implementation against the Blender 4.x export dialog;
the table fixes codes, sides, severities, stages and the outcome mapping.

## 8. Testing

Everything below runs offline in `tests\ObjCodecTests` (a console EXE, not `dotnet test`; each gate
is a `static class X { internal static string Run() }` that throws on failure and is called from
`Program.Main`; new source files must be added to the csproj's explicit `<Compile Include>` list,
`EnableDefaultCompileItems` is false). Command: `dotnet run --project tests\ObjCodecTests -c Release`.

- **Frozen Binder fixtures (must land BEFORE `Bind` is refactored).** For synthetic `SkinnedModel`s +
  target name lists covering every row in §7, record `Bind`'s exact behaviour today: threw-or-not and
  the message, verbatim. After the extraction the same fixtures must give the identical result. Not
  tautological: the record predates the delegation.
- **`ReplacementDecision` goldens.** Every combination of (`sourceHasArmature`, `targetRigged`,
  `targetBoneNamesAvailable`, `firstIssue`) → the expected outcome, asserted against the branches of
  `BundleBaker.ReplaceMesh:153-202` quoted in the test's own comment. This is the parity a
  model/name-list fixture cannot express, because it covers the skinless guard and the
  not-rigged branch — neither of which `SkinBinder.Bind` ever sees.
- **End-to-end preflight goldens.** `ReplacementPreflight.Run(bytes, path, target)` over a REAL
  committed `.glb` (`lib\u9_probe.glb`, 2 888 B, rigged, already read by `ClipPlan`) against
  targets built from the file's own joint names: exact list → `ByName`; one name changed →
  `NearestBone` + `MissingBone` + `ExtraBone`; a sidecar mapping that name back → `ByName`; a stale
  sidecar → `SidecarStale` Warning and the outcome computed from the unaliased model; empty target
  bone list → `NearestBone` + `TargetBonesUnavailable`; `Rigged=false` target → `NotRigged`; a
  skinless model against a rigged target → `Refused` + `SkinlessOntoRigged`; truncated bytes →
  `Refused` + `MalformedGlb`.
- **Alias semantics.** Simultaneous rename (A→B, B→A swap works); output collision rejected; key not
  a file bone → `AliasUnused` and the rest applied; node names follow joint names; index tables
  unchanged (byte-compare `Joints`, `Weights`, `InverseBindMatrices` before and after `Apply`).
- **Sidecar policy.** Stale hash not applied; malformed not applied; create-vs-update save path;
  add-model route ignores sidecars.
- **Race (in-game, `ct_bench`).** Re-pick a file while a parse is in flight → only the last `gen`
  becomes Ready; swap the bench unit during parse → `TargetChanged`, no preview, no exception.
- **In-game acceptance via PPCLI.** `ct_bench` → Doctor → known-bad file: report renders, verdict
  NEAREST-BONE, Preview shows the fallback, `Revert` restores; `connect screenshot` for the record; no
  exception in `Player.log`. Then add the alias → verdict BY NAME → Save → `ct_project` on a project
  containing that file+sidecar logs `BY NAME … with n alias(es) from <sidecar>`.
- **Leak gate.** The Doctor tracks the instance ids of every `Mesh` it created; after 100 ×
  (preview, revert) and two frames, none of them resolve via `Resources.InstanceIDToObject`.

## 9. Verified facts the design rests on (checked in source 2026-09-01, re-checked for v3)

- `GlbReader.Read(byte[])` → `Model()` (`GlbReader.cs:138-344`) produces `SkinnedModel`
  (`GlbCodec.cs:84-174`), documented "free of UnityEngine types". First `UnityEngine.Object` on the
  runtime path is `new Mesh` in `LiveMesh.ToMesh(BakedMesh, string)` (`LiveMesh.cs:62/85`) — which
  takes a `BakedMesh`, hence the `LiveMesh.Build` seam in §4.1.
- `SkinBinder.Bind(SkinnedModel, IList<string> boneNames, int materialSlots, IList<string>
  blendShapeNames, out ushort[] joints, out float[][] bindposes)` (`GlbReader.cs:2450`). Replacement
  callers pass `0, null` for slots/shapes (`LiveMesh.cs:217`, `SkinFields.cs:730`). No alias hook
  exists; `Plain()` is the only name transform. `Submeshes(file, 0)` is NOT a no-op — it still bounds-
  checks every triangle index (`:2584-2589`) — which is why `Stage` exists.
- `BundleBaker.ReplaceMesh` (`:142-205`) REFUSES a skinless source onto a rigged target (`:153-156`)
  and otherwise falls back to nearest-bone on any exception from `RebindByName` (`:197-201`).
- `SkinFields.RebindByName` (`:730`) refuses by `FormatException` — its own width check (`:739`) and
  everything `SkinBinder.Bind` throws — and writes nothing before it does.
- `LiveMesh.Bind` (`:114`) never throws and never assigns `smr.sharedMesh`: it returns `null` for an
  unrigged target (`:117`), a "skinned BY NAME…" sentence (`:250`) or a "skinned to the target's own…"
  sentence (`:148`), swallowing the by-name refusal at `:256-260`. `SeamSwap` is what assigns
  (`SeamSwap.cs:265-266`), after its own skinless guard (`:243-246`).
- `SkinnedModel.JointNames` is a `readonly List<string>` and `SkinNode.Name` a plain field
  (`GlbCodec.cs:125`, `:13-18`) — `AliasMap.Apply` writes by index.
- `ppcontent.json` root is `JsonUtility` (lenient) but `replace[]` is regex-parsed, one flat object
  per row (`ContentProject.cs:373-423`); row keys `bundle, asset, texture, material, mesh, clip,
  video` (`:19-53`).
- No mod-wide settings store exists anywhere in `src/` (grep: no `PlayerPrefs`, no settings class);
  small persistent state goes under `Application.persistentDataPath\ContentTool`, as
  `ContentToolMain.cs:65` and `Extract.cs:42` already do.
- `tests\ObjCodecTests` is a net472 console EXE, currently green (`ALL PASS`, exit 0), with an
  explicit compile list — new offline-testable source must be added to it.

## 10. Roadmap context

Agreed order with Codex: **Model Doctor (this) → preview polish → target browser → manifest
domain model + safe writer (aliases move into the manifest) → "Replace one mesh" wizard (first
console-free route; copies file + sidecar) → lifecycle dashboard (Validate/Bake/Apply/Verify/
Package, progress, cancel) → texture/sound/video wizards → safe model adapters (axis/scale/pivot)
→ C# ports of `ppskel` + `ppzip` (1–2 wk) → C# port of `ppretarget` as an out-of-process library
first (4–8 wk) → complex entity wizards.** Off the roadmap: a general retargeter, procedural
map/chunk authoring, tattoo mechanics, a "ContentTool 2.0" rewrite.

## 11. Review disposition — Codex round 4 (`C:\Temp\cx\f3f24ebb*`)

All 17 findings were re-checked against source before being applied. **15 adopted in full, 2 adopted
with a stated correction. None rejected.**

| # | Verdict | Note |
|---|---|---|
| 1 | Adopted, verified | `BundleBaker.cs:153-156` does refuse a skinless source onto a rigged target and returns before `MeshFields.Fill`. §4.2 + §7 now map it to `Refused`/`SkinlessOntoRigged`; `NoArmature` is Blocking. |
| 2 | Adopted, verified | `ReplacementDecision.Decide` (§4.2) is now the only definition; the bake asks it. Catch narrowed to `FormatException` — verified as the only refusal type of `SkinBinder.Bind` (15 sites) and `RebindByName` (`SkinFields.cs:739`). Stated consequence: a genuine bug in that code now fails the bake instead of being downgraded to nearest-bone. |
| 3 | Adopted **with a correction** | `ReplacementPreflightResult` added. The built-mesh field is `BakedSkin`, not `BakedMesh` — `ModelBuild.From` returns `BakedSkin` (`ModelBuild.cs:148`) and the `BakedMesh` is its `.Mesh`. Also carries `Original` (pristine names) so alias edits need no re-parse. |
| 4 | Adopted, verified | `ReplacementSource` envelope; `LiveMesh.Load` logs `AliasLog`, `ContentProject.ImportMesh` carries `AliasesApplied`/`SidecarPath` onto `ImportedMesh` for the bake's `how` string. |
| 5 | Adopted, verified | Neither `AliasMap.Apply` nor `GlbSource.ReadReplacement` receives a `RigTarget`. Output-vs-target validation moved into `ReplacementPreflight` as `AliasNotATargetBone`. |
| 6 | Adopted | §5 now states partial application explicitly: absent keys ignored, one `AliasUnused` Warning each, valid entries applied. The `AliasMap` row in §4.1 was rewritten to match. |
| 7 | Adopted, verified | `Analyze` returns severity-free `BindingIssue`s; the Doctor assigns severity; `Bind` throws the first. `Passes`/`FirstBlocking` deleted. Added `Stage {Skin, Bones}` because `Submeshes`/`Shapes` sit between two groups of checks (`GlbReader.cs:2465-2466`) and `Submeshes(file, 0)` is not a no-op (`:2584-2589`) — without it the extraction would silently change which sentence an author reads. |
| 8 | Adopted | Re-hash before Preview and before Save; mismatch → `SourceChanged`, invalidate, re-run. |
| 9 | Adopted | `RigTarget` grew `MeshInstanceId` + `BindPoseCount`; the whole fingerprint (ordered bone names, mesh identity, bind-pose count, transform path, renderer id) is compared before every preview. |
| 10 | Adopted, verified | `LiveMesh.Bind` returns a sentence and swallows the by-name refusal (`LiveMesh.cs:256-260`); it never assigns `sharedMesh`. `out BindMode mode` added; preview requires `mode == outcome`, then assigns atomically, else keeps the previous preview and raises `PreviewDisagreed`. |
| 11 | Adopted, verified | ~90 `Bad(...)` exits against 8 catalogued codes. Implemented centrally rather than by editing 90 throw sites: `Bad(msg, code = MalformedGlb)`, `Unreadable → UnsupportedGlb`, explicit codes at the six catalogued `GlbReader` sites + two in `ModelBuild`. `ImportRefusedException : FormatException` so every existing `catch (FormatException)` keeps working. Worker-boundary catch → `ImportFailed`. |
| 12 | Adopted **with a correction** | Verified: Draco is accepted (`GlbReader.cs:231`) and decoded (`:601-608`, `Decompress` at `:1774`), so the `Draco` refusal row is removed. Correction: Codex asked for typed codes on Draco DECODE failures; those are ordinary `Bad(...)` sites and stay `MalformedGlb` — their sentences already name the cause, and a dedicated code buys nothing this slice uses. Also noted: `Unreadable(Draco.Extension)` (`:2273-2280`) is now DEAD CODE, since both call sites exclude Draco. Not deleted here — out of scope. |
| 13 | Adopted | Sidecar rows are Warning; the outcome is computed from the UNALIASED model whenever the sidecar was not applied (§5 table). |
| 14 | Adopted, verified | `LiveMesh.IsModel` does accept `.obj` (`LiveMesh.cs:24-28`), but the Doctor's browser filters `.glb` and its picker lists `SkinnedMeshRenderer`s only. §2 scope statement rewritten; §3 lists static targets as a non-goal; `NotRigged` retained only for an SMR with no bind poses. |
| 15 | Adopted | `RigTarget.BoneNames` is nullable, `Rigged` derives from bind poses (matching `SkinFields.Rigged`, `:623-626`), and `TargetBonesUnavailable` is in §7. |
| 16 | Adopted | §8 now has three separate golden layers: frozen Binder fixtures, `ReplacementDecision` truth table, end-to-end preflight over `lib\u9_probe.glb` + sidecar. |
| 17 | Adopted | §2 says FOUR outcomes. |
