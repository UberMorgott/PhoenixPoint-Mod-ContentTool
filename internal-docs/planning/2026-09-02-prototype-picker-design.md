# Prototype Picker + Doctor screen v2 — design

Status: **Draft v1**. Date: 2026-09-02.
Owner decision: approved direction, layout delegated to Claude+Codex.
Supersedes: roadmap line "target browser" in `2026-09-01-model-doctor-design.md:381`.
Relates: `2026-09-01-model-doctor-design.md` (slice 1, shipped); taxonomy research
`research/2026-09-02-prototype-taxonomy.md`.
Peer review source: Codex memo `C:\Temp\cx\cde865c38bab4c0e8b72908bb7bc58a6.out.md`
(sections A-G, all decisions adopted).

---

## 1. Goal

Give the Doctor a **prototype-aware target** so a mesh is verified against the rig it will
actually be baked onto, not against whatever actor happens to be on the stand. The picker
replaces `ModelDoctor.Targets()` (`ModelDoctor.cs:616`) — the inline
`Root.GetComponentsInChildren<SkinnedMeshRenderer>(true)` list — with a full-area prototype
browser that instantiates a real `AddonsManager` rig and body parts at menu time, produces a
`RigTarget` from the instantiated slot renderer, and feeds it into the existing
`SkinCompatibility.Analyze` / `ReplacementDecision.Decide` pipeline unchanged.

### Non-goals (explicit refusals)

- Bone dragging, IK/FK controls — creates corrupt assets unless it becomes a real retargeter.
- Rest-pose editing — same; `ppretarget` reposes geometry, normals, tangents, rebuilds inverse
  bind matrices, adjusts segment scales and rewrites animation curves
  (`ppretarget.py:516`); a transform handle is not a smaller version of that work.
- Weight painting.
- Automatic retargeting.
- `ppretarget` port — kept out-of-process until algorithm and fixtures are stable.
- UGUI rewrite — IMGUI only, no cloned Phoenix widgets.

---

## 2. Prototype model

### Three levels

1. **Category** — navigation only, 8 values:
   - Human & Anu
   - Pandoran humanoids
   - Pandoran beasts
   - Worms & small creatures
   - Pandoran structures
   - Vehicles
   - Turrets & drones
   - Ancients
2. **Prototype** — a distinct rig + slot vocabulary + animation behaviour contract.
   40 shipped prototypes (44 `AddonsManagerDef`s minus 4 rig-less).
3. **Role / slot variant** — exact replacement target within a prototype.

"Rig-less" managers (`DefaultTacCharacter`, `Dropped`, `FallDown`,
`YuggothianDropped_ItemContainer`) and test managers (`Dummy`, `ct_creature_*`) are not picker
categories. Hidden under diagnostics / Advanced.

### Merge rule

Merge two managers into one prototype ONLY when ALL THREE signatures match:

- Rig hierarchy (bone-name set + parent chain).
- Slot / attachment vocabulary (the `AddonSlotDef` set from
  `CharacterBodyStateDef.BodyPartsDefs`).
- Animation behaviour (`TacActorAnimActionsDef` shape).

Shared prefab identity alone is insufficient. Shared rig prefab alone is insufficient.

Concrete merges and splits — see taxonomy `research/2026-09-02-prototype-taxonomy.md:65-82`:

- **Human** = one prototype, four role profiles: Soldier, Civilian, Utka, Mutoid.
  Animation actions differ per role (no `Human_AnimActionsDef`; instead
  `Civilian_AnimActionsDef`, `Soldier_Menu_AnimActionsDef`, `Soldier_Utka_AnimActionsDef`).
  Searching "Mutoid" selects Human -> Mutoid profile, not a separate rig.
- **Exalted** = separate prototype under Human & Anu.
- **Mutog** = Pandoran beast. Never Vehicle, regardless of its vehicle-class tag.
- **Fishman / Oilfish** = separate variants (share a rig prefab, but Fishman has many slots
  while Oilfish has one torso slot — taxonomy `:133`).
- **Acidworm / Poisonworm** and **three tech turrets** — merge only after comparing slots and
  anim-action signatures (unverified, taxonomy `:284`).
- **Facehugger DroppedTorso** — hidden as Advanced/corpse variant until its missing
  `SkeletonChassisAddonDef` is understood (taxonomy `:300-301`).

### PrototypeRecord schema

```text
PrototypeRecord
  Id, DisplayName, Category, SearchTerms
  Managers[], RepresentativeCharacters[], SourcePaths[], provenance/DLC
  RigPrefab, RootMotionNode, PreviewPoseClip
  Bone[] { name, relativePath, parent, rest TRS }
  Variant[]
    role/name, representative TacCharacterDef
    CharacterBodyStateDef
    Slot[] { slot def, attachment point, representative addons,
             replacement renderer signature/availability }
    TacActorAnimActionsDef, controller, resolved clip catalogue
```

### RigTarget

**Never persisted** in the catalog. Contains live renderer and mesh instance IDs
(`SkinCompatibility.cs:51`). Constructed at selection time from the instantiated slot
renderer, then consumed by the existing `SkinCompatibility.Analyze` unchanged.

---

## 3. Verification modes

### Replace slot (exact)

- Bone list and bind-pose count must match exactly.
- Uses `ReplacementPreflight` from slice 1 (`src/Doctor/ReplacementPreflight.cs`).
- `RigTarget` is snapshotted from the selected slot's instantiated `SkinnedMeshRenderer`.
- The full diagnostic catalogue from `2026-09-01-model-doctor-design.md:249-309` applies.

### Extend prototype (new)

- Every imported joint must resolve **uniquely** onto the full rig.
- Missing rig bones that the file does not use are **allowed** (a partial body part is
  legitimate).
- The current analyzer requires a bijection and reports every absent target bone
  (`SkinCompatibility.cs:196` — "Every live bone must be in the file"). Applying that rule
  to a full-rig target would reject legitimate partial body parts.
- **Code change:** relax the bijection at `SkinCompatibility.cs:196` **ONLY** when mode is
  Extend. When the file's joint set is a strict subset of the rig's bone-name set and every
  file joint maps uniquely, the check passes. `MissingBone` issues for bones absent from the
  file but present in the rig are suppressed in Extend mode. `ExtraBone` (file has a bone the
  rig does not) remains Blocking in both modes.

---

## 4. Catalog build + prototype instantiation at menu time

### DefRepository scan

- `GameUtl.GameComponent<DefRepository>().GetAllDefs<TacCharacterDef>()` — proven at
  `FitBench.cs:580-593`.
- Join each `TacCharacterDef` -> `GetAddonsMangerDef()` (`TacCharacterDef.cs:172-175`)
  -> manager / body state / anim-actions def.
- Deduplicate by composite signature (rig hierarchy + slot vocabulary + anim behaviour).
  792 `TacCharacterDef`s collapse to ~40 prototypes.
- Build metadata once. **Rescan on command only** — a button under Advanced, not automatic.

### Prototype instantiation

The `AddonsCharacterTester` sequence (`AddonsCharacterTester.cs:44-57`):

```
AddonsManager = Repo.Instantiate<AddonsManager>(AddonsManagerDef);   // :46
AddonsManager.SetupRig(transform);                                    // :47
BodyParts = CharacterBodyStateDef.BodyPartsDefs
              .Select(id => Repo.Instantiate<TacticalItem>(id));      // :50
AddonsManager.SetupAddons(BodyParts);                                 // :51
```

Instantiated ONLY for the selected record. Destroyed on deselection / prototype change.

### Rig availability

Rig prefab is a **direct** `GameObject` reference on the def (`AddonsManagerDef.cs:12`),
not an Addressable — verified LIVE at the HomeScreen (taxonomy `:236-238`). Every rig is
reachable from the main menu through `DefRepository`.

### "Slot visual unavailable" behaviour

Bodypart visuals are Addressables (`AddonSkinDataBase.GetPrefabAsset` returns null unless
`assetReference.Asset` is already resident — `AddonSkinDataBase.cs:18-29`). If a bodypart
Addressable cannot load at the menu, show **"slot visual unavailable"** in the slot list.
Never fabricate a target from the full hierarchy. The skeleton is always available; the
`SkinnedMeshRenderer` may not be.

---

## 5. Prototype browser + search behaviour

Replace the inline `Change target` renderer list (`ModelDoctor.cs:616`) with a **full-area
prototype browser** that temporarily replaces the entire content area (not inline expansion).

### Layout

- Category groups, collapsible, each containing prototype rows.
- Each row: display name, bone count, slot count, DLC badge if applicable.

### Search

- **Case-insensitive, token-AND** — every whitespace-delimited token must match somewhere in
  the searchable fields.
- Covers: fiction names, internal names, roles, slots, factions, rig prefab names.
- Recompute filtered rows only when text changes.
- Search results **auto-expand** matching groups; clearing search restores previous group
  collapse states.
- Reactive: no "search" button, filters on each keystroke.

---

## 6. Doctor screen layout

```text
+- Source [Browse] | Prototype [Change] | Replace/Extend | Role | Slot -+
+------------------------------+-------------------------------------+
| VERDICT + compact counts     | [Skeleton] Clip v  < >  Loop       |
|                              | +----------- viewport -----------+ |
| Diagnostics                  | | model + prototype skeleton      | |
| [scrolls]                    | |                                 | |
|                              | +--------------------------------+ |
| > Bone map (auto-open only   | > Selected bone inspector          |
|   for name mismatch)         | scrubber / speed                   |
+------------------------------+-------------------------------------+
| Preview | Revert | Save aliases | Copy report | Advanced...        |
+------------------------------------------------------------------------+
```

- Proportions: ~42% report / ~58% viewport.
- Source and target selectors stay on one line.
- Diagnostics receive the left-column scroll.
- Bone map collapsed when BY NAME, auto-opened for name mismatch.
- Transform gizmos, numeric TRS, file utilities, catalog rescan: under Advanced.
- Prototype browser temporarily replaces the entire content area.

---

## 7. Skeleton overlay + animation preview

### MVP

- Clone the selected rig and bind the imported mesh onto it.
- Offer preview pose plus idle/navigation/action clips resolved from the selected role.
- Draw parent-child lines and clickable joint dots over the viewport.
- Colour legend: matched = green, unmatched = red, aliased = yellow (three distinct colours).
- Clicking a bone shows name, path, parent, rest/current transforms.
- While an alias row is armed, clicking an eligible target bone assigns it
  (click-to-alias).

### Clip sampling

Reuse the existing `FitAnim` clip sampler (`FitAnim.cs:314`): samples clips directly, avoids
confusing clip names with controller-state names. Events, transitions and root motion are
intentionally absent — documented at that line.

### Refusals (same as section 1)

No bone dragging, IK/FK, rest-pose edit, weight painting, automatic retargeting.

---

## 8. Viewport mouse controls to 3D-editor standard

### Problem

The current bench controls (`FitBench.cs:1843-1904`, `BenchList.cs:576-682`) are ad-hoc:

| Gesture | Current | Standard |
|---|---|---|
| LMB drag | orbit camera | **pick bone/part** |
| RMB drag | turn model | (reserved for context) |
| MMB drag | pan | **orbit around focus point** |
| Shift+MMB | — | **pan** |
| Alt+LMB | — | **orbit (MMB fallback)** |
| Wheel | zoom (proportional) | **zoom toward cursor, distance-scaled step** |

### Target scheme

All gestures apply to both Fit and Doctor tabs identically.

| Gesture | Action |
|---|---|
| **MMB drag** | Orbit around focus point (measured bounds centre + lift + pan offset). Uses `BenchList.Orbit` / `BenchList.Tilt` with existing gain `DegreesPerPixel = 0.2` and pitch clamp `[-80, 80]`. |
| **Shift+MMB drag** | Pan (translate focus point in screen plane). Uses `FitBench.PanBy`. |
| **Wheel** | Zoom toward cursor with distance-scaled step. Existing `BenchList.Wheel` (`ZoomFactor = 0.12`, proportional) already does this. |
| **Alt+LMB drag** | Orbit — identical to MMB, as a fallback for mice without a middle button. |
| **LMB click** | Pick bone/part (Doctor tab: select bone in overlay, Fit tab: select gizmo handle — existing `FitGizmo.WouldGrab` priority preserved). |
| **F** | Frame selection — re-run `Reframe()` to fit the selected bone/part, or the whole model if nothing selected. |
| **Home** | Reset view — equivalent to existing reset: `zoom = ZoomDefault; lift = 0; yaw = 0; pitch = 0; pan = Vector3.zero;` (`FitBench.cs:530`). |

### Implementation notes

- **Smooth damping** on orbit and zoom: `Mathf.SmoothDamp` with a ~0.08 s smooth time, applied
  per-frame to yaw/pitch/zoom. Current code applies deltas immediately.
- **No gimbal flip**: pitch clamped to `[PitchMin, PitchMax]` = `[-80, 80]` (already in
  `BenchList.cs:596`; keep as-is).
- **Same scheme on every bench tab** — the `Mouse()` method in `FitBench.Arm` drives all tabs.
  The only per-tab difference is what LMB-click picks (gizmo handle on Fit, bone on Doctor).
- **Migration**: LMB-orbit (current) -> MMB-orbit. RMB-turn-model removed (the model turn was
  useful for "how does the far side look" but orbiting serves the same purpose; the hint text
  at `FitBench.cs:1473` is updated). InvertX/InvertY toggles remain and apply to MMB-orbit.
- **Existing helpers reused**: `BenchList.Orbit`, `BenchList.Tilt`, `BenchList.Wheel`,
  `FitBench.PanBy`, `BenchList.OverScene`, `FitGizmo.WouldGrab`.

### Acceptance (part of slice 2)

- MMB-drag orbits, Shift+MMB pans, Alt+LMB orbits, wheel zooms, LMB picks, F frames, Home
  resets — verified by PPCLI screenshot sequence on both Fit and Doctor tabs.
- Pitch stays within `[-80, 80]` under continuous vertical drag.
- Smooth damping visible (orbit does not snap to final position in one frame).
- No regression: existing gizmo drag on Fit tab still works (FitGizmo gets first refusal on
  LMB).

---

## 9. Python ports

Order and effort, after a shared lossless `GlbDocument` reader/writer:

| Order | Script | Effort | What ports | What drops |
|---|---|---|---|---|
| 1 | `ppslim` | 2-3 days | Clip census, reachability, BIN compaction | Regex UI; replaced by clip checklist + mandatory-clip guard. Destructive trimming disabled by default for playable prototypes (script documents game-stalling failures at `ppslim.py:1`). |
| 2 | `ppzip` | 4-5 days | Constant-curve collapse, quaternion quantisation | — (preservation/idempotence tests required). |
| 3 | `ppskel` | 5-7 days | Validation + explicit rename/insert/collapse plans against selected prototype | Tiffany-specific paths, hard-coded map (`ppskel.py:36`). Do NOT generalise them into automatic guesses. |
| — | `ppretarget` | DO NOT PORT | — | Kept out-of-process until algorithm and fixtures are stable. |

### Threading / progress / cancel contract

- File parsing and rewriting on a **worker thread**, not a coroutine (coroutines still block
  Unity's main thread).
- Publish **immutable progress snapshots** at checkpoints per clip, accessor, buffer view or
  node.
- Cancellation is **cooperative**: leaves the destination untouched, deletes only the known
  temporary file.
- **Atomic save**: write to `<tmp>`, then `File.Move` / `File.Replace`.
- Main-thread coroutine used **solely** for `DefRepository`, Addressable and `UnityEngine.Object`
  operations.

---

## 10. Slices

### Slice 0 — Pre-flight verification (before any catalog code)

Run IN GAME on `D:\PP-Instance2` via PPCLI. Three checks:

**(a) Bone-name equality across prefabs inside each claimed family.**
For every family row in taxonomy `:65-82` that claims a shared rig, instantiate both managers
via `call` and walk `RigRoot.transform` children — compare the full bone-name set. If they
differ, the merge claim is wrong and the prototype list must be adjusted.

**(b) Bodypart Addressable load at HomeScreen.**
`AddonsManager.SetupAddons` with a representative `CharacterBodyStateDef.BodyPartsDefs` at
the HomeScreen. Confirm whether `SkinnedMeshRenderer`s actually appear or
`AddonSkinDataBase.GetPrefabAsset` returns null. This settles taxonomy open question 3
(`:293-296`).

**(c) DLC prototype presence.**
Re-run `find {all:true,type:"AddonsManagerDef"}` on
`D:\Steam\steamapps\common\Phoenix Point` (with PPCLI `-PPRoot`). Check whether Umbra,
Cyclops, Hoplite appear as managers or map onto existing ones under different internal names
(taxonomy `:288-292`).

### Slice 1 — Prototype catalog and targeting (5 days)

- Main-menu catalog from `DefRepository` scan.
- Reactive grouped search (case-insensitive, token-AND).
- Prototype / role / slot / mode selection.
- No dependency on the benched actor for targeting.
- Replace mode uses a real slot renderer; Extend uses subset validation.
- Unavailable visuals reported explicitly ("slot visual unavailable").

**Acceptance:**
- Selecting Human -> Soldier -> Head produces a `RigTarget` with the same bone names as the
  live `SkinnedMeshRenderer` on a benched soldier's head.
- Selecting Crabman -> Head produces a different `RigTarget`.
- Searching "mutoid" selects Human -> Mutoid profile.
- "slot visual unavailable" shown for at least one Addressable-gated part (if (b) confirmed).
- Offline tests: catalog deduplication produces ~40 prototypes from 792 `TacCharacterDef`s;
  `RigTarget` from an instantiated slot matches the target from a live actor.
- Owner visual in-game check at end of slice.

### Slice 2 — Prototype visual check + viewport controls (5 days)

- Standalone rig instantiation (clone selected rig, bind imported mesh).
- Representative clip list from selected role (preview pose + idle/navigation/action).
- Play/pause/scrub transport — reuse `FitAnim.cs:314` clip sampler.
- Skeleton overlay: parent-child lines, clickable joint dots, colour legend
  (matched/unmatched/aliased).
- Click-to-inspect (name, path, parent, rest/current transforms).
- Click-to-alias (while an alias row is armed, clicking a target bone assigns it).
- Closing or changing prototype destroys all owned objects and restores the viewport.
- **Viewport mouse controls** standardized per section 8.

**Acceptance:**
- Skeleton overlay renders on top of a Crabman rig with correct parent-child lines.
- Clicking a bone shows its name, path and transforms in the inspector panel.
- Aliasing a bone via click changes the verdict from NEAREST-BONE to BY NAME for a
  single-rename test case.
- Idle clip plays on a Human prototype with play/pause/scrub.
- Viewport controls per section 8 acceptance criteria.
- Owner visual in-game check at end of slice.

### Slice 3 — GLB maintenance foundation + ppslim (5 days)

- Lossless `GlbDocument` round-trip (read-write-read produces byte-identical accessors,
  images, skins).
- Clip census from `ppslim`: list all clips, mark reachable/unreachable.
- Guarded trim: mandatory-clip guard, destructive trimming disabled by default for playable
  prototypes.
- Progress / cancel per section 9 contract.
- Atomic save.
- Offline fixtures proving surviving accessors/images/skins remain byte-correct after trim.

**Acceptance:**
- Round-trip of `lib\u9_probe.glb`: output byte-identical to input (accessor/image/skin
  level).
- Clip census lists all clips in a multi-clip `.glb` with correct reachability.
- Trim with mandatory-clip guard refuses to remove a clip in the mandatory set.
- Cancel mid-trim leaves the original file untouched.
- Owner visual in-game check at end of slice.

### Verification responsibility

- Everything except owner visual checks is verified by agents (offline tests + PPCLI +
  `connect screenshot`).
- Owner is called for a visual in-game check at the end of each slice.

---

## 11. Rules + open questions

### Rules (from Codex section G — adopted)

1. "One `AddonsManagerDef` equals one complete prototype" is **false** for slots and animation.
2. Do not merge by rig prefab alone.
3. Do not report an exact replacement verdict against a fabricated full-rig `RigTarget`.
4. Do not ship bone dragging as a cosmetic feature — it creates corrupt assets unless it
   becomes a real retargeter.
5. Do not port the Python scripts line-for-line. Preserve validated capabilities; discard
   hard-coded experiments and unsafe default workflows.

### Open questions (from taxonomy `:282-307`)

1. **Bone counts per rig** — never read. No `Rig.transform` walk was done. Needed for the
   catalog's bone-count display. Resolved in slice 0(a).
2. **DLC coverage** — whether Umbra/Cyclops/Hoplite map to existing managers or need new
   entries. Resolved in slice 0(c).
3. **Menu-time bodypart visuals** — whether `AddonsManager.SetupAddons` produces
   `SkinnedMeshRenderer`s at the HomeScreen. Resolved in slice 0(b).
4. **Cross-prefab bone-name equality** — e.g. Crabman vs Oilcrab have separate prefabs but
   may share names. Resolved in slice 0(a).
5. **Facehugger DroppedTorso** — has a rig but `SkeletonChassisAddonDef == null`. Meaning
   unresolved; hidden as Advanced/corpse variant.
6. **PreviewPoseClip** population — Acheron shows null; other 43 unchecked. If populated for
   humans, it is the natural pose for a prototype preview.
7. **Acidworm/Poisonworm and tech turret merges** — slot and anim-action comparison not yet
   done. Resolved in slice 0(a).
