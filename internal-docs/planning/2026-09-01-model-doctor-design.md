# Model Doctor — design (slice 1 of the in-game authoring roadmap)

Date: 2026-09-01. Status: DRAFT v2 after Codex REWORK review. Owner: Morgott. Peer-reviewed with
Codex (thread `01a05e66-5add-7c52-8b0f-736d782ce85d`; memos `C:\Temp\cx\23a85faf*`, `70322129*`,
review `1555dec7*`).

## 1. Problem

- Today an author learns how a `.glb` will behave only at bake time, after a 12-step loop:
  export → copy into `Content\Meshes\` → hand-write `ppcontent.json` → `ct_project` → enable mod →
  **restart the game** → look. One wrong bone name = the whole loop again.
- Worse than a refusal: on a bone-name mismatch the bake does **not** refuse. `BundleBaker.ReplaceMesh`
  (`src/Bake/BundleBaker.cs:142-205`) tries `SkinFields.RebindByName`, and on any exception silently
  **falls back to nearest-bone skinning** — one full-weight influence per vertex — and only says so in
  one log line: `"nearest-bone - the file's own weights were NOT used: <reason>"` (`:199-200`). The
  model imports, the game runs, and the mesh deforms like a rubber sheet. A non-programmer sees "it
  worked but looks broken" and has no idea why.
- The genuinely fatal refusals happen earlier, in parsing/baking: Draco, external buffers,
  >128 MB, non-triangle primitives, no normals, >4 influences (`src/Import/GlbReader.cs:155/240/
  248/611-618`, `src/Bake/ModelBuild.cs:153-155`).
- A runtime "file → live renderer" path already exists — `ct_replace` → `SeamSwap.ReplaceMesh`
  (`src/Dev/SeamSwap.cs:225-247`) → `LiveMesh.Load` + `LiveMesh.Bind` — but it is a console verb with
  a path argument, no report, and it uses the same by-name binding with no way to fix a name.

## 2. Goal

**Pick a `.glb` off disk, pick the skinned mesh it should replace, and within seconds see which of
three outcomes the bake would produce — BY-NAME bind / NEAREST-BONE fallback / IMPORT REFUSED — with
every reason listed as what to change in Blender, and with bone-name aliases (the one fix that is
honest to do in-game) applied on the spot and persisted so the real bake produces the same outcome.**

Success criteria:
- The Doctor's outcome for (file, target, aliases) equals the outcome `BundleBaker.ReplaceMesh`
  reports for the same triple. Same code path (§4.2), tested with frozen fixtures (§8).
- A non-programmer reading a row knows which export checkbox / rename / weight transfer to do.
- Zero new dependencies. Zero Python. Nothing here can leave the game in a bad state: import
  failures become diagnostics, preview is candidate-then-swap, no bundle unload/reload.

Scope statement: this is the **skinned-mesh replacement Doctor**. A static `.glb`/`.obj` or an
unrigged target still gets a verdict (NEAREST-BONE or NOT-RIGGED, the same words the bake uses), but
no static-mesh-specific checks are in this slice.

## 3. Non-goals

Not in this slice: drag-and-drop; the "add new model/creature/weapon" route (replacement only, and
sidecar aliases apply to the replacement path only — see §5); material-slot or blend-shape checks
(the replacement path passes `0, null` for them — `LiveMesh.Bind`, `SkinFields.RebindByName`);
**clip aliases** (replacement mesh import reads no clips — nothing to alias until the animation
slice); any geometry/weight edit; per-bone rest-pose edits; renaming the GAME's bones; animation
retargeting; keyframe editing; progress bars and cancellation (arrive with the Bake button);
UGUI/cloned Phoenix widgets; a GUI for every console command; upgrading the `ppcontent.json`
`replace[]` parser.

## 4. Architecture

### 4.1 Units

| Unit | Responsibility | Depends on |
|---|---|---|
| `Diagnostic`, `DiagnosticReport`, `Outcome` (`src/Doctor/Diagnostic.cs`, new) | Plain data. `Severity {Blocking, Downgrade, Warning, Info}`; `Code` (stable string, §7); `Message` (existing engine wording); `Remedy` (Blender sentence); `Subject`; `Side {File, Target, Sidecar}`. `Outcome {ByName, NearestBone, NotRigged, Refused}`. Report = rows + outcome. | — |
| `ImportRefusedException(Code, Message)` (`src/Import/`, new) | The typed form of the refusals `GlbReader` / `ModelBuild.From` already throw. Mechanical change: each existing `throw new …Exception("…")` on the refusal list gains its `Code`; messages unchanged. | — |
| `RigTarget` (`src/Import/SkinCompatibility.cs`, new) | Plain snapshot of the target taken on the main thread: `string[] BoneNames` (from `smr.bones[b].name`, as `LiveMesh.cs:209`), `int RendererInstanceId`, `string TransformPath`, `string MeshName`, `bool Rigged`. No Unity references held across frames. | — |
| `SkinCompatibility.Analyze(SkinnedModel file, RigTarget target) → DiagnosticReport` (same file, new; **extracted from** `SkinBinder.Bind`, `GlbReader.cs:2449-2525`) | The bone-binding checks `Bind` performs today, all of them, as rows instead of a first-failure throw. `Bind` becomes: `var r = Analyze(...); if (!r.Passes) throw new …(r.FirstBlocking.Message);` — bake behaviour and strings byte-for-byte. Material/blend-shape checks stay in `Bind` behind the existing `materialSlots > 0` / `blendShapeNames != null` conditions and are not part of the Doctor. | `SkinBinder.Plain()` |
| `ReplacementPreflight.Run(byte[] bytes, string path, RigTarget target) → DiagnosticReport` (`src/Doctor/ReplacementPreflight.cs`, new) | **The one pipeline that defines the verdict**: `GlbReader.Read` → `AliasMap.LoadSidecar(path)` → `AliasMap.Apply` → `ModelBuild.From` (the bake's own mesh build; catches `ImportRefusedException` → `Refused`) → `Analyze` → outcome per §4.2. Pure; runs on the worker thread except that it receives the `RigTarget` snapshot. | all above |
| `AliasMap` (`src/Import/AliasMap.cs`, new) | `IReadOnlyDictionary<string,string>` file-bone → game-bone. `Apply(SkinnedModel)` is a **simultaneous** rename from the immutable original names: for each joint `j`, `JointNames[j]` and `Nodes[JointNodes[j]].Name` are set from the map; index tables (`JointNodes`, weights, inverse-bind, parent indices, animation track node indices) are untouched. Rejects a map whose outputs collide or whose keys are not file bones (§5 policy). Sidecar I/O (§5). | `SkinnedModel` |
| `GlbSource.ReadReplacement(string path) → SkinnedModel` (`src/Import/GlbSource.cs`, new, ~15 lines) | The single "read a `.glb` by path for replacement" helper: bytes → `GlbReader.Read` → sidecar apply → return. Called by `LiveMesh.Load` (`LiveMesh.cs:45`) and `ContentProject.ImportMesh` (the bake's replacement read). The add-model route (`ModelBuild` hashing `BonePaths`) keeps calling `GlbReader.Read` directly — aliases must not change published bone-path hashes. | `AliasMap` |
| `LiveMesh.Build(SkinnedModel model, string name) → Mesh` (`LiveMesh.cs`, new public seam) | `ModelBuild.From(model).Mesh` + the existing private `ToMesh(BakedMesh, string)` (`:62`). Main thread only. `Load()` is rewritten to call `GlbSource.ReadReplacement` + `Build`; behaviour unchanged. | `ModelBuild`, `LiveMesh` |
| `GlbFileBrowser` (`src/Dev/GlbFileBrowser.cs`, new) | IMGUI panel: drives, up/into, `.glb` filter, recent list (5, persisted in the mod's settings). Returns a path. No native dialog in this slice. | IMGUI helpers in `src/Dev/` |
| `ModelDoctor` (`src/Dev/ModelDoctor.cs`, new) | Orchestration + UI state machine (§4.3/§4.4). Target picker = list of `SkinnedMeshRenderer`s under the benched actor, resolved the way `SeamSwap` resolves a `TargetPath`; picking one takes the `RigTarget` snapshot. | all above, `SeamSwap`, `FitBench` |
| `FitBench` (existing) | Hosts the Doctor as a tab. Numeric readouts move behind an "Advanced" toggle (§6). | — |

### 4.2 One pipeline, two callers (hard rule)

- `ReplacementPreflight.Run` is the ONLY definition of the verdict. The Doctor calls it. The bake's
  decision is the same functions in the same order: `ContentProject.ImportMesh` → `GlbSource.
  ReadReplacement` → `ModelBuild.From` → `BundleBaker.ReplaceMesh` → `SkinFields.RebindByName` →
  `SkinBinder.Bind` → `Analyze`. No copied conditions anywhere.
- Outcome mapping mirrors `BundleBaker.ReplaceMesh` (`BundleBaker.cs:175-202`) exactly:
  `Refused` ← `ImportRefusedException` from read/build; `NotRigged` ← target has no bind poses;
  `NearestBone` ← file has no armature, OR target bone names unavailable, OR `Analyze` has any
  `Blocking` row (that is the `catch` at `:197`); `ByName` ← otherwise.
- `Analyze` rows that would make the bake fall back are severity **`Downgrade`** in the Doctor
  (they do not stop the import; they ruin it), and `Blocking` only inside `Bind`'s throw. The report
  header says which: `NEAREST-BONE — the bake would import this but NOT use your weights (3 reasons)`.
- Name normalisation is `SkinBinder.Plain()` (`GlbReader.cs:2559-2564`: strips only the game's
  `#<bone>_Addon => <part>` decoration; no case folding, no suffixes). Aliases are applied to the
  model before anything reaches `Plain()`, identically for Doctor and bake.

### 4.3 Data flow and state machine

```
Idle ─pick file/target/alias─► Pending(gen N)
   worker: bytes = File.ReadAllBytes(path)
           report = ReplacementPreflight.Run(bytes, path, targetSnapshot)   // pure
           enqueue (gen N, report, model)
   main (Arm.Update drains ConcurrentQueue):
           if gen != current  → drop
           if target.RendererInstanceId no longer resolves → report += TargetGone (Blocking), no preview
           else → Ready(report)
Ready ─Preview─► candidate = LiveMesh.Build(model); try Bind(candidate, smr, model)
                 success → destroy previous Doctor-owned mesh, remember candidate   (candidate-then-swap)
                 failure → destroy candidate, previous preview untouched, row added
Ready ─edit alias─► re-Apply on a pristine copy of the original name arrays → re-run Analyze only
                    (no re-parse; ModelBuild output is bone-name-independent) → Ready(report')
Ready ─Save aliases─► sidecar write (§5) → row AliasesSaved (Info)
```

- **Generation + fingerprint.** `gen` increments on every change of file path, file bytes hash,
  target snapshot, alias map, or bench actor swap. A result carrying an old `gen` is dropped. That is
  stale-result invalidation, not cancellation (the worker finishes and its result is ignored).
- **Target snapshot.** The worker never touches a `SkinnedMeshRenderer`. `RigTarget` is plain data
  taken on the main thread when picked; before any preview the instance id is re-resolved.
- **Ownership.** The worker owns `model` until it is enqueued; after that only the main thread
  touches it. The original `JointNames` / node names are kept as an immutable copy inside the session
  so alias edits always re-derive from originals (order-independent).

### 4.4 Threading rules

- `OnGUI` renders an immutable `ReportView` snapshot and **enqueues intents** (`PickFile`,
  `SetAlias`, `Preview`, `Save`); `Update` processes intents and queue results. No mutation in `OnGUI`.
- Off the main thread: only `File.ReadAllBytes` and `ReplacementPreflight.Run` (verified
  Unity-object-free, §9). Everything that constructs a `UnityEngine.Object` is on the main thread.
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
- **Never silent.** Wherever a sidecar is applied (Doctor, `ct_replace`, bake) the log prints the
  sidecar path and every mapping; the Doctor shows an `ALIASES ACTIVE (n)` badge; the bake's
  `ReplaceMesh` `how` string gains ` with <n> aliases from <sidecar>`.
- **Policy.** Missing sidecar → nothing applied, no row. `source.sha256` ≠ file → **not applied**,
  row `SidecarStale` (Blocking-for-aliases: shown as `Downgrade` because the bake would then run
  without them). Malformed JSON / unknown `schema` / colliding outputs / output not a target bone →
  `SidecarInvalid`, not applied, `Downgrade`. Key not a file bone → `AliasUnused`, `Warning`,
  the rest applied.
- **Scope.** Applied only on the replacement read (`GlbSource.ReadReplacement`); the add-model
  route ignores sidecars (its published bone-path hashes must not depend on a side file).
- **Write.** `Save aliases` writes `<tmp>` then `File.Move` when creating, `File.Replace` when
  updating (`File.Replace` fails if the destination does not exist). Recomputes `sha256` at save.
- **Copy caveat.** Explorer copies of the `.glb` do not bring the sidecar. Visible, not silent: the
  Doctor opened on the copy shows no badge and the same `Downgrade` rows reappear. The
  "Replace one mesh" wizard (next slice) copies both files.

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
5. **Other rows** — remaining diagnostics grouped Refused → Downgrade → Warning → Info; each: icon,
   `Message`, `Remedy` muted. `Side = Target` rows are visually separate ("this is the game's asset,
   not your file").
6. **Actions** — `Preview` (enabled iff outcome is ByName or NearestBone — the author may want to
   see the fallback), `Revert preview`, `Save aliases` (enabled iff map changed and valid),
   `Copy report` (plain text to clipboard — what a non-programmer pastes when asking for help).

Right: viewport with the existing gizmo. **Advanced** toggle (off by default) reveals the
pos/rot/scale readouts, step buttons and per-axis nudges; default view = gizmo + model scale slider.

Prior art copied: `model-viewer` (open → auto-frame → reset → errors), Blender retarget add-ons
(two-column map, suggest + confirm), `glTF-Validator` (severity grouping). Not copied: generic
inspectors, dope sheets, WndProc drag-drop.

## 7. Diagnostic catalogue

Refusals (outcome `Refused`; `Side=File` unless noted). Existing message text kept verbatim.

| Code | Where thrown today | Remedy |
|---|---|---|
| `Oversize` | `GlbReader.cs:155` | Reduce texture resolution or remove unused meshes/animations; the file is N MB of which textures are M MB |
| `ExternalBuffer` | `:240` | Export as **binary** `.glb` (glTF Binary), not `.gltf` + `.bin` |
| `NoMesh` | `:248` | The export contains no mesh — check that the mesh object is selected/visible on export |
| `Draco` | `:2271-2279` | Untick *Compression* (Draco) in the glTF export dialog |
| `NonTriangle` | `:611-613` | Tick *Apply Modifiers* and ensure faces are triangulated (Triangulate modifier or Ctrl+T) |
| `NotIndexed` | `:670-671` | Re-export; indexed geometry is the default — a custom exporter setting disabled it |
| `TooManyInfluences` | `:616-618` | Weight Paint → *Weights* → *Limit Total* = 4, then re-export |
| `NoNormals` | `ModelBuild.cs:153-155` | Tick *Normals* under Mesh in the export dialog |

Binding rows (from `Analyze`; severity `Downgrade` in the Doctor — the bake would fall back).

| Code | Existing message (`GlbReader.cs`) | Remedy |
|---|---|---|
| `NoArmature` | carries no armature… (`:2455`) | Parent the mesh to the armature (Ctrl+P → Armature Deform) and export with *Skinning* on |
| `MissingBone` | does not contain the bone 'X' (`:2502`) | Rename your bone to 'X' — or map it in the table |
| `ExtraBone` | adds the bone 'X' … (`:2508`) | Map 'X' to the target bone it stands for, or transfer its weights to its parent and delete it (do not delete a bone that still carries weights) |
| `DuplicateFileBone` | (`:2480`) | Two bones share a name in the file — rename one |
| `PlainCollision` | (`:2493`) | Two file bones collapse to the same name after decoration is stripped — rename one |
| `NotBijective` | (`:2512`) | Mapping is not one-to-one — check the table for a target bone used twice |
| `JointsWeightsMismatch` | (`:2457`) | Broken export (joints/weights arrays disagree) — re-export |
| `InverseBindCount` | (`:2515`) | Broken export (bind matrices ≠ joints) — re-export |
| `BoneIndexOutOfRange` | (`:2523`) | Broken export (a vertex references a bone the skin does not list) — re-export |

Target-side rows (`Side=Target`; not the author's fault, say so):

| Code | Existing message | Severity |
|---|---|---|
| `TargetBoneEmpty` | (`:2473`) | Downgrade |
| `TargetBoneDuplicate` | (`:2475`) | Downgrade |
| `TargetNotRigged` | "not rigged - the target carries no bind poses" (`BundleBaker.cs:184`) | Info, outcome `NotRigged` |
| `TargetGone` | (new) renderer destroyed since it was picked | Blocking (for preview only) |

Sidecar rows: `SidecarStale` (Downgrade), `SidecarInvalid` (Downgrade), `AliasUnused` (Warning),
`AliasesSaved` (Info) — §5.

Exact remedy wording is finalised during implementation against the Blender 4.x export dialog;
the table fixes codes, sides, severities and the outcome mapping.

## 8. Testing

- **Frozen-fixture parity (the one that matters).** BEFORE `Bind` is refactored, capture for a set
  of fixtures (synthetic `SkinnedModel`s + target name lists covering every row in §7, plus the
  `demos/**` `.glb`s against their documented targets' bone lists read via
  `BundleBaker.ReadBoneNames`) the exact `Bind` behaviour: throws-or-not + message. After the
  refactor the same fixtures must give `ReplacementPreflight.Run(...).Outcome` and first-row message
  identical to the frozen record. This is not tautological because the record predates the
  delegation.
- **Alias semantics.** Simultaneous rename (A→B, B→A swap works); collision rejected; key not a
  file bone → `AliasUnused`; node names follow joint names; index tables unchanged (byte-compare the
  weight/IBM arrays before and after `Apply`).
- **Sidecar policy.** Stale hash not applied; malformed not applied; create-vs-update save path;
  add-model route ignores sidecar.
- **Race.** Re-pick a file while a parse is in flight → only the last `gen` becomes Ready; swap
  the bench unit during parse → `TargetGone`, no preview, no exception.
- **In-game acceptance via PPCLI.** `ct_bench` → Doctor → known-bad file: report renders, verdict
  NEAREST-BONE, Preview shows the fallback, `Revert` restores; `connect screenshot` for the record; no
  exception in `Player.log`. Then add the alias → verdict BY NAME → Save → `ct_project` on a
  project containing that file+sidecar logs `BY NAME … with n aliases`.
- **Leak gate.** Doctor tracks instance ids of every `Mesh` it created; after 100 × (preview,
  revert) and two frames, none of them resolve via `Resources.InstanceIDToObject`.

## 9. Verified facts the design rests on (checked in source 2026-09-01)

- `GlbReader.Read(byte[])` → `Model()` (`GlbReader.cs:137-344`) produces `SkinnedModel`
  (`GlbCodec.cs:83-173`), documented "free of UnityEngine types". First `UnityEngine.Object` on the
  runtime path is `new Mesh` in `LiveMesh.ToMesh(BakedMesh, string)` (`LiveMesh.cs:62/84`) — which
  takes a `BakedMesh`, hence the `LiveMesh.Build` seam in §4.1.
- `SkinBinder.Bind(SkinnedModel, IList<string> boneNames, int materialSlots, IList<string>
  blendShapeNames, out ushort[] joints, out float[][] bindposes)` (`GlbReader.cs:2449`). Replacement
  callers pass `0, null` for slots/shapes (`LiveMesh.Bind`, `SkinFields.RebindByName`
  `SkinFields.cs:730`). No alias hook exists; `Plain()` is the only name transform.
- `BundleBaker.ReplaceMesh` (`BundleBaker.cs:142-205`) does not refuse on binding failure: it
  catches, calls `SkinFields.Rebind` (nearest-bone) and reports `how`. Doctor outcomes mirror that.
- `SeamSwap.ReplaceMesh` (`SeamSwap.cs:225-247`) does not read the file itself; it calls
  `LiveMesh.Load` then `LiveMesh.Bind(ours, smr, model)` — so the single read helper goes into
  `LiveMesh.Load` and `ContentProject.ImportMesh`, not into `SeamSwap`.
- `ppcontent.json` root is `JsonUtility` (lenient) but `replace[]` is regex-parsed, one flat
  object per row (`ContentProject.cs:373-423`); row keys `bundle, asset, texture, material, mesh,
  clip, video` (`:19-53`).

## 10. Roadmap context

Agreed order with Codex: **Model Doctor (this) → preview polish → target browser → manifest
domain model + safe writer (aliases move into the manifest) → "Replace one mesh" wizard (first
console-free route; copies file + sidecar) → lifecycle dashboard (Validate/Bake/Apply/Verify/
Package, progress, cancel) → texture/sound/video wizards → safe model adapters (axis/scale/pivot)
→ C# ports of `ppskel` + `ppzip` (1–2 wk) → C# port of `ppretarget` as an out-of-process library
first (4–8 wk) → complex entity wizards.** Off the roadmap: a general retargeter, procedural
map/chunk authoring, tattoo mechanics, a "ContentTool 2.0" rewrite.
