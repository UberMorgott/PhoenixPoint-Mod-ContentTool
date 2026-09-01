# Model Doctor — design (slice 1 of the in-game authoring roadmap)

Date: 2026-09-01. Status: DRAFT for review. Owner: Morgott. Peer-reviewed with Codex (thread
`01a05e66-5add-7c52-8b0f-736d782ce85d`, memos in `C:\Temp\cx\23a85faf*.out.md`, `70322129*.out.md`).

## 1. Problem

- Today an author learns that a GLB is incompatible only at bake time, after a 12-step loop:
  export → copy into `Content\Meshes\` → hand-write `ppcontent.json` → `ct_project` → enable mod →
  **restart the game** → look. One wrong bone name = the whole loop again.
- The importer refuses late and literally: `"the file does not contain the bone 'X'"`
  (`src/Import/GlbReader.cs:2514`), `"the file adds the bone 'X' which this model's skeleton does
  not have"` (`:2522`), `"the file has N material parts but this model draws with M"` (`:2579`),
  `"the file has N blend shapes but this model has M"` (`:2595`), `"primitive N weights some
  vertices to more than four bones"` (`:616`), `"the file carries no armature, so it cannot replace a
  rigged model"` (`:2457`), Draco / >128 MB / non-triangle / no-normals refusals.
- The in-game bench (`FitBench`, Ctrl+Alt+B, `src/Dev/FitBench.cs`) can already hot-swap a
  `SkinnedModel` onto a live actor (`LiveMesh.Bind`, `src/Dev/LiveMesh.cs:113`) — but only from a
  baked project; there is no way to point it at an arbitrary file.

## 2. Goal

One sentence: **pick a `.glb` off disk, pick what it should replace, and within seconds see every
reason the bake would refuse it — phrased as what to change in Blender — with the two fixes that are
honest to do in-game (bone-name aliases, clip-name aliases) applied on the spot and persisted so
the real bake honours them.**

Success criteria:
- The verdict shown by the Doctor and the verdict of `ct_project` on the same file + target are
  IDENTICAL (same code path, see §4.2). Divergence is a bug, tested for (§8).
- A non-programmer reading a FAIL row knows which export checkbox / rename to do in Blender.
- Zero new dependencies. Zero Python.
- Nothing the Doctor does can leave the game in a bad state: parse failures become diagnostics,
  preview swap is atomic (fail → old preview stays), no bundle unload/reload.

## 3. Non-goals (explicit, from the YAGNI pass)

Not in this slice: drag-and-drop onto the game window; the "add new creature/weapon" route
(replace-existing only); any geometry or weight edit (submesh split/merge, decimation, weight
painting, >4-influence pruning); per-bone rest-pose editing; renaming the GAME's bones; animation
retargeting; keyframe/curve editing; progress bars and cancellation (arrive with the Bake button in
a later slice); UGUI or cloned Phoenix widgets; a generic "GUI for every console command".

## 4. Architecture

### 4.1 Components

| Unit | Responsibility | Depends on |
|---|---|---|
| `Diagnostic` / `DiagnosticReport` (`src/Doctor/Diagnostic.cs`, new) | Plain data: `Severity {Blocking, Warning, Info}`, `Code`, `Message` (what is wrong), `Remedy` (what to do in Blender), `FixableInTool` flag, `Subject` (bone/clip/submesh name). Report = list + `Passes => !Any(Blocking)`. | nothing |
| `RigTarget` (`src/Import/SkinCompatibility.cs`, new, tiny) | Exactly the triple `SkinBinder.Bind` already takes: `IList<string> BoneNames`, `int MaterialSlots`, `IList<string> BlendShapeNames` (+ `IList<string> ClipNames` for the clip check). Built from a live `SkinnedMeshRenderer` (`smr.bones[b].name`, as `LiveMesh.cs:209` does) or from baked `skin.BoneNames` (`SkinFields.cs:747`). | nothing |
| `SkinCompatibility.Analyze(SkinnedModel file, RigTarget target) → DiagnosticReport` (same file, new, **extracted from** `SkinBinder.Bind` `GlbReader.cs:2449-2525`) | Every check `Bind` performs today (15 of them, §7), returned as diagnostics instead of a first-failure throw. `Bind` is rewritten to call `Analyze` and throw the first Blocking diagnostic's message — bake behaviour and error strings stay byte-for-byte. | `SkinBinder.Plain()` |
| `AliasMap` (`src/Import/AliasMap.cs`, new) | `Dictionary<string,string>` file-name → target-name for bones and for clips. Applied as an **in-memory rename of the parsed `SkinnedModel`** (`JointNames`, clip names) right after `GlbReader.Read` — so `Analyze`, `Bind`, `LiveMesh` and the bake all see the renamed model and need no alias parameter. Persisted as a sidecar next to the `.glb` (§5). | `SkinnedModel` |
| `GlbFileBrowser` (`src/Dev/GlbFileBrowser.cs`, new) | IMGUI panel: drive list, parent/child navigation, `.glb` filter, recent-files list (persisted in the mod's settings file). Returns a path. No native dialog in this slice. | IMGUI helpers already in `src/Dev/` |
| `ModelDoctor` (`src/Dev/ModelDoctor.cs`, new) | Orchestrates: path → read bytes + `GlbReader.Read` on worker → apply sidecar aliases → `Analyze` → render report → optional preview when `Passes`. Owns one `DoctorSession` (bytes, hash, parsed model, report, alias edits). | all above, `LiveMesh`, `SeamSwap` target resolution |
| **Target picker** (inside `ModelDoctor`) | The bench's `unit`/`weapon` (`FitBench.cs:234-235`) are *defs*, not renderers. The thing a GLB replaces is a `SkinnedMeshRenderer` at a `TargetPath` on the benched prefab — exactly what `ct_replace` → `SeamSwap.ReplaceMesh` (`src/Dev/SeamSwap.cs:225-247`) resolves today. The Doctor lists the SMRs under the benched actor (name + bone count + material count), the author picks one; `RigTarget` is built from it. Reuses `SeamSwap`'s resolution, adds no second path. | `SeamSwap`, `FitBench` |
| `FitBench` (existing, trimmed) | Hosts the Doctor as a tab; its Units/Weapons pickers stay as the way to put an actor on the bench. Numeric readouts move behind an "Advanced" toggle (§6). | — |

### 4.2 One validator, two callers (hard rule)

- `Analyze` is the ONLY place compatibility rules live. `SkinBinder.Bind` (bake, preview) calls it.
  The Doctor calls it. No second implementation, no copied conditions.
- Name normalisation is `SkinBinder.Plain()` (`GlbReader.cs:2559-2564`: strips only the game's
  `#<bone>_Addon => <part>` decoration, no case folding, no suffixes) and nothing else. Aliases are
  applied to the model *before* anything reaches `Plain()`, identically for Doctor and bake. This is
  what keeps Doctor-PASS ≡ bake-PASS.
- The refusal strings the bake prints today stay identical (they are what the docs and users know).
  `Diagnostic.Message` = that string; `Remedy` is the new, human sentence.

### 4.3 Data flow

```
[GlbFileBrowser] path
   → File.ReadAllBytes + GlbReader.Read(bytes)      (worker thread — Unity-object-free, §9)
   → AliasMap.Load(path + ".aliases.json")?.Apply(model)   (worker thread, pure rename)
   → post SkinnedModel to main thread (ConcurrentQueue drained in Arm.Update)
   → SkinCompatibility.Analyze(model, RigTarget.From(smr))   (main thread, cheap, pure)
   → DiagnosticReport rendered in OnGUI (immutable snapshot)
   → [Preview] iff report.Passes → LiveMesh.ToMesh(model) + LiveMesh.Bind(mesh, smr, model)  (main)
   → [Save aliases] → sidecar writer (§5)
```

- `LiveMesh.Load()` (`LiveMesh.cs:45`) today does Read **and** `ToMesh` (`:84`, first
  `new Mesh`) in one call. The Doctor calls `GlbReader.Read` and `ToMesh` separately so only the
  parse leaves the main thread; `Load()` itself is untouched.
- Alias edits re-apply the rename on a pristine copy of the joint/clip name lists and re-run
  `Analyze` synchronously (pure function over already-parsed data — no re-parse).
- Re-picking a file replaces the session; the previous session's Unity objects are destroyed by the
  existing `LiveMesh` revert path before the new bind. Failed bind leaves the old preview in place.
- Any exception from read/parse → one `Blocking` diagnostic with `Code=ParseFailed`, message = the
  exception's text, remedy = the matching export instruction (Draco → "export without compression",
  URI buffers → "export as binary .glb", oversize → "reduce textures / decimate in Blender").

### 4.4 Threading rules

- `OnGUI` reads state only. All mutation happens in `Update` from the queue or from button handlers.
- Only the byte read + `GlbReader` parse leave the main thread. Anything that constructs a
  `UnityEngine.Object` stays on the main thread. One Doctor job at a time; picking a new file while
  one parses cancels the pending result (result is dropped by session id).

## 5. Persistence — aliases live in a sidecar next to the `.glb`

- File `<name>.glb.aliases.json` beside the mesh:
  `{ "bones": { "<file bone>": "<game bone>" }, "clips": { "<file clip>": "<game clip>" } }`.
- Why not the manifest: `replace[]` rows are parsed by regex, one `\{[^{}]*\}` per row
  (`ContentProject.ParseReplace`, `src/Project/ContentProject.cs:373-423`) — a nested map inside a
  row would break every row after it. Upgrading that parser is the "manifest domain model + safe
  writer" slice; not paid for here. `ponytail:` sidecar now, fold into the manifest when that slice
  lands (the sidecar loader becomes a fallback for one release).
- Written by the Doctor's **Save aliases** button (atomic: temp file + `File.Replace`, same pattern
  as `WeaponManifest.Save`). Works for any path, project or not — the sidecar travels with the file
  when the author copies it into `Content\Meshes\`.
- Applied at every place a `.glb` is read by path: `LiveMesh.Load` (`LiveMesh.cs:45`),
  `SeamSwap.ReplaceMesh` (`:235`), and the bake's read (`ProjectBake.cs:1100`, `ModelBuild.cs:67/407`)
  — one helper `AliasMap.ApplySidecar(path, model)` called right after `GlbReader.Read`. That is the
  whole bake-side threading: no change to `ShippedReplacement`, `SkinFields.Rebind` or `Bind`.
- A sidecar that names a bone the file does not contain is a `Warning` (`AliasUnused`), never fatal.

## 6. UI — decluttered bench

Left column (fixed width), top to bottom:
1. **Source** — path field (read-only), `Browse…`, recent list (5), file size + vertex/bone/clip
   counts on one line.
2. **Target** — the existing unit / weapon pickers, collapsed to their current selection with a
   `Change` button.
3. **Report** — rows grouped Blocking → Warning → Info. Each row: icon, `Message`, `Remedy` in
   muted text; `FixableInTool` rows carry an inline control (bone rows: a dropdown of target bones
   filtered by fuzzy match, pre-selected but **never auto-applied**; clip rows: same over target
   clips). Header: `PASS — compatible with <target>` or `FAIL — N blocking`.
4. **Actions** — `Preview` (enabled iff Passes), `Revert preview`, `Save aliases` (enabled iff
   aliases changed).

Right: the viewport with the existing gizmo. **Advanced** toggle (off by default) reveals the
current pos/rot/scale readouts, step-size buttons and per-axis nudges; the default view shows only
the gizmo and the model scale slider.

Prior art copied deliberately: `model-viewer` (open → auto-frame → reset camera → errors),
Blender retarget add-ons (two-column map, auto-suggest + confirm, presets), `glTF-Validator`
(severity grouping). Not copied: generic inspectors, dope sheets, WndProc drag-drop.

## 7. Diagnostic catalogue (initial)

| Code | Severity | Message (existing string) | Remedy | Fixable |
|---|---|---|---|---|
| `NoArmature` | Blocking | carries no armature… | Parent the mesh to the armature and export with skin weights | no |
| `MissingBone` | Blocking | does not contain the bone 'X' | Rename the matching bone in Blender to 'X' — or alias it here | **alias** |
| `ExtraBone` | Blocking | adds the bone 'X' … | Delete 'X' or merge its weights into its parent | alias if it is a renamed target bone; else no |
| `SubmeshCount` | Blocking | has N material parts but this model draws with M | Merge/split materials so the mesh has M material slots | no |
| `BlendShapeCount` | Blocking | has N blend shapes but this model has M | Match the shape-key count | no |
| `BlendShapeName` | Blocking | (name mismatch) | Rename the shape key — or alias it here | alias (next slice) |
| `TooManyInfluences` | Blocking | weights some vertices to more than four bones | Limit Total = 4 in Weight Paint | no |
| `NonTriangle` | Blocking | (mode ≠ 4) | Triangulate on export | no |
| `NoNormals` | Blocking | (normals required) | Enable normals on export | no |
| `Draco` | Blocking | (Draco refused) | Export without Draco compression | no |
| `ExternalBuffer` | Blocking | geometry lives in a separate file | Export as binary `.glb` | no |
| `Oversize` | Blocking | is N MB, past the 128 MB limit | Reduce texture size / decimate | no |
| `ClipUnknown` | Warning | clip 'X' has no counterpart on the target | Rename the action — or alias it here | **alias** |
| `ClipMissing` | Info | target clip 'X' not provided by the file | Game falls back to its own clip | no |
| `AliasUnused` | Warning | alias for 'X' but the file has no such bone/clip | Remove the stale alias | remove |

Structural checks `Bind` also performs (`GlbReader.cs:2452-2523`) become `Blocking` diagnostics
with their existing messages and a generic "re-export from Blender" remedy — they indicate a broken
export, not an authoring choice: joints/weights size mismatch (`:2457`), empty or duplicate live
bone name (`:2473/2475`), duplicate file bone name (`:2480`), plain-name collision after
undecoration (`:2493`), non-bijective map (`:2512`), inverse-bind-matrix count ≠ joint count
(`:2515`), vertex references an out-of-range bone (`:2523`).

Exact wording of `Remedy` strings is finalised during implementation against the Blender 4.x
export dialog; the table fixes the code, severity and fixability.

## 8. Testing

- **Parity test (the one that matters):** for every `.glb` under `demos/**` and every target rig it
  is documented to replace, `Analyze(...).Passes == (SkinBinder.Bind(...) does not throw)`, and when
  it throws, the exception message equals the first Blocking diagnostic's `Message`. Lives next to
  the existing test project (`tests/`).
- Unit tests over synthetic `SkinnedModel`s for each diagnostic code (one missing bone, one extra,
  submesh mismatch, alias resolves a missing bone, alias cannot invent a bone that is weighted-only
  in the file, …).
- In-game acceptance via PPCLI (`connect screenshot` after `ct_bench` + Doctor open on a known-bad
  file): the report renders, Preview stays disabled, no exception in `Player.log`.
- Leak gate: open/preview/revert the same file 100× in the bench, `Resources.FindObjectsOfTypeAll<Mesh>()`
  count returns to baseline.

## 9. Verified facts the design rests on (checked in source 2026-09-01)

- `GlbReader.Read(byte[])` → `Model()` (`GlbReader.cs:137-344`) produces `SkinnedModel`
  (`GlbCodec.cs:83-173`), documented "free of UnityEngine types"; uses `ObjVector3`/`float[][]`.
  First `UnityEngine.Object` on the runtime path is `new Mesh` in `LiveMesh.ToMesh` (`LiveMesh.cs:84`).
  → parse off main thread is safe.
- `SkinBinder.Bind(SkinnedModel, IList<string> boneNames, int materialSlots, IList<string>
  blendShapeNames, out ushort[] joints, out float[][] bindposes)` (`GlbReader.cs:2449`); target
  names come from `smr.bones[b].name` (`LiveMesh.cs:209`) or baked `skin.BoneNames`
  (`SkinFields.cs:747`, `ProjectBake.cs:1811`). No alias/rename hook exists today — `Plain()` is the
  only transform. → `RigTarget` is that triple; aliases are a pre-`Bind` rename.
- Runtime "file → renderer" already exists: `ct_replace` → `SeamSwap.ReplaceMesh`
  (`SeamSwap.cs:225`): `LiveMesh.Load(file)` (`:235`) then `LiveMesh.Bind(ours, smr, model)` (`:247`)
  on the SMR resolved from a `TargetPath`. → the Doctor is that path with a report in front of it.
- `ppcontent.json` root is `JsonUtility` (lenient) but `replace[]` is regex-parsed, one flat object
  per row (`ContentProject.cs:373-423`); row keys: `bundle, asset, texture, material, mesh, clip,
  video` (`:19-53`). → nested alias maps cannot go in a row today; hence the sidecar (§5).

## 10. Roadmap context (why this first)

Agreed order with Codex: **Model Doctor (this) → live preview polish → target browser → manifest
model + safe writer → "Replace one mesh" wizard (first console-free route) → lifecycle dashboard
(Validate/Bake/Apply/Verify/Package buttons, progress, cancel) → texture/sound/video wizards →
safe model adapters (axis/scale/pivot) → C# ports of `ppskel` + `ppzip` (1–2 wk) → C# port of
`ppretarget` as an out-of-process library first (4–8 wk) → complex entity wizards.**
Off the roadmap: a general retargeter, procedural map/chunk authoring, tattoo mechanics, a
"ContentTool 2.0" rewrite.
