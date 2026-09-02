# Prototype Picker + Doctor screen v2 — design

Status: **v2, 2026-09-02, revised after slice 0 + Codex round 2**.
Owner decision: approved direction, layout delegated to Claude+Codex.
Supersedes: roadmap line "target browser" in `2026-09-01-model-doctor-design.md:381`.
Relates: `2026-09-01-model-doctor-design.md` (slice 1, shipped); taxonomy research
`research/2026-09-02-prototype-taxonomy.md` (incl. its **Slice 0 verification** section and the
raw census `research/rig-census-2026-09-02.json`).
Peer review sources: Codex memo `C:\Temp\cx\cde865c38bab4c0e8b72908bb7bc58a6.out.md`
(round 1, sections A-G) and `C:\Temp\cx\26ae05a8cb0342c2bed58677af758f74.out.md`
(round 2, sections A-E), both adopted in full.
Implementation plan: `2026-09-02-prototype-catalog-plan.md`.

### Changelog — what v2 changed and why

Every change below is forced by a MEASURED fact from the slice-0 live run, not by a preference.

| § | v1 said | v2 says | Because |
|---|---|---|---|
| 1, 4 | instantiate an `AddonsManager` **at menu time** and read the slot renderer | rebuild the **geoscape squad-bay `CharacterBuilder`** the bench already drives | Slice 0(b): a menu-time `SetupAddons` returns `true`, reports the item attached, and produces **0 `SkinnedMeshRenderer`s** — `AddonSkinDataBase.GetPrefabAsset` returns null because the Addressable is not resident (taxonomy "(b) Menu-time bodypart load"). The A/B control in a loaded mission produced **7** SMRs. A Replace target cannot be snapshotted where no renderer exists. |
| 2 | "40 shipped prototypes", merge on rig + slots + **animation** | **37 physical rigs, 36 binding prototypes** (Fireworm+Acidworm merged); animation is **variant** metadata | Slice 0(a): 37 distinct rig prefabs, 2551 transforms censused. Fireworm/Acidworm rigs differ only in the ROOT GAMEOBJECT'S OWN NAME — the 13 bone/`EXT_` names are identical. Slice 0(d): `HumanoidAnimatorLOC` is shared by Human AND Crabman, whose skeletons share 9 names of 124/58. A controller does not identify a rig. |
| 2 | families 1-9 are prototypes | families are **navigation only**; the prototype unit is the **rig prefab** | Slice 0(a): no claimed family shares a bone-name set. The only name common to all 37 rigs is `EXT_VoiceContext`. Crabman ∩ Oilcrab = **34** names across *different* prefabs — a Crabman mesh binds partially and silently on an Oilcrab. |
| 3 | Extend = "every joint resolves uniquely onto **the full rig**" | Extend matches **`BindableBones`** only; `EXT_*` policy stated; duplicate names refuse the verdict only when referenced | `Addon.GetEquivalentBones` skips `EXT_*` (`Addon.cs:1208`), and the intra-family overlap is almost entirely `EXT_*`. Fishman's `Fishman_upWrist_l`/`_L` are case-variants (both reachable, NOT ambiguous — census was read case-insensitively); three vehicle rigs have `light` twice and `FirstOrDefault` makes the second unreachable. `PrototypeCatalog.Signature` excludes the prefab root (`Parent == null`) from the binding signature — with the root in, Fireworm != Acidworm; root stays inside `Bindable` so Human 124 / Crabman 58 identities hold. |
| 7 | clips resolved from the selected role's `TacActorAnimActionsDef` | **fallback to the controller's own `animationClips`**, deduplicated | Slice 0(d): `Crabman_AnimActionsDef` has `AnimActions.Count == 0` and no default action/reaction clip — a preview reading only the anim-actions def shows nothing. Controllers list duplicate clip names (73 entries / 69 distinct; 60 / 45). |
| 10 | slice 1 acceptance is main-menu, `~40 prototypes`, "slot visual unavailable" | slice 1 acceptance is **geoscape-only**, against **live renderers**, `37/36` counts | Same as §1/§4: at the menu there is nothing to accept against. |

---

## 1. Goal

Give the Doctor a **prototype-aware target** so a mesh is verified against the rig it will
actually be baked onto, not against whatever actor happens to be on the stand. The picker
replaces `ModelDoctor.Targets()` (`ModelDoctor.cs:616`) — the inline
`Root.GetComponentsInChildren<SkinnedMeshRenderer>(true)` list at `ModelDoctor.cs:622-623` —
with a full-area prototype browser that **rebuilds the geoscape squad bay's own
`AddonsCharacterBuilder` as the chosen prototype**, produces a `RigTarget` from the slot renderer
that rebuild really produced, and feeds it into the existing `SkinCompatibility.Analyze` /
`ReplacementDecision.Decide` pipeline unchanged.

**The target is a LIVE renderer, and there is only one place one exists.** Slice 0(b) measured a
menu-time prototype: `AddonsManager.SetupAddons` returns `true`, the item reports
`IsAttachedToManager == true`, and `GetComponentsInChildren<SkinnedMeshRenderer>(true)` returns
**0** — because the bodypart visual is an Addressable and `AddonSkinDataBase.GetPrefabAsset`
returns null while it is not resident (`AddonSkinDataBase.cs:18-29`). The same plan run inside a
loaded mission returned **7**. So the picker does not build a hidden rig of its own; it drives the
builder the bench is already standing on — `bay.CharacterBuilder`, gated at `FitBench.cs:364-379`
(a PLAYING level, a `GeoLevelController`, a `SquadBay` with a `CharacterBuilder`) — through
`CommonCharacterUtils.DisplayCharacter` + `RebuildCharacter`, exactly as `FitBench.Show`
(`FitBench.cs:1038-1073`) already does for a `TacCharacterDef`.

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

### Four levels

1. **Category** — navigation only, 8 values:
   - Human & Anu
   - Pandoran humanoids
   - Pandoran beasts
   - Worms & small creatures
   - Pandoran structures
   - Vehicles
   - Turrets & drones
   - Ancients
2. **Prototype** — **one bone-binding signature**, nothing else. **37 physical rigs → 36 binding
   prototypes.**
3. **Variant** — manager / role plus a representative `TacCharacterDef`. Animation lives HERE.
4. **Slot** — the exact Replace target inside a variant.

"Rig-less" managers (`DefaultTacCharacter`, `Dropped`, `FallDown`,
`YuggothianDropped_ItemContainer`) and test managers (`Dummy`, `ct_creature_*`) are not picker
categories. Hidden under diagnostics / Advanced.

### The counts, measured

Slice 0(a), live on `D:\PP-Instance2`: **46 `AddonsManagerDef`** (44 shipped + ContentTool's 2),
**42 with `Rig != null`**, **4 rig-less**, **37 distinct rig prefabs** (35 shipped + ContentTool's
`cyborg_spider` and `tiffany_ppfit`), **2551 transforms** censused into
`research/rig-census-2026-09-02.json` (`rig -> {managers, instanceId, count, bones[{name,parent,path}]}`).
The DLC surface does not grow the list: Umbra is a mutation variation onto Crabman/Fishman, Hoplite
maps to `HumanoidGuardian`, and Cyclops has no `TacCharacterDef` at all (slice 0(c)).

**37 → 36:** `ALN_Fireworm_Rig_Ready` and `ALN_Acidworm_Rig_Ready` are two prefabs with **one bone
set** — 13 of 14 transforms shared, and the single difference is the root GameObject's own name.
That prototype therefore has **two prefabs and three manager variants** (Fireworm, Acidworm,
Poisonworm), not three roots. Keep every provenance so the selected variant still rebuilds its own
bodyparts and its own animation data.

### Merge rule — one signature, not three

**Merge two managers into one prototype if and only if their `BindableBones` sets are equal**
(§3 defines `BindableBones`). Nothing else merges anything:

- **Shared rig prefab is not sufficient** and not necessary — Fireworm/Acidworm prove one bone set
  across two prefabs.
- **Shared slot vocabulary is not a merge signal.** Fishman and Oilfish share
  `ALN_Fishman_Rig_Ready` yet Fishman has ten slots and Oilfish one torso slot (taxonomy `:133`) —
  same prototype, different **variants**.
- **Shared animation is not a merge signal, and never was.** Slice 0(d): `HumanoidAnimatorLOC` is
  the `runtimeAnimatorController` of BOTH `CHR_Human_Rig_Ready` (124 transforms) and
  `ALN_Crabman_Rig_Ready` (58), which share 9 names of which 6 are `EXT_*`. A shared controller
  does not imply a compatible rig, and an empty `AnimActions` does not imply no animation.
  **Animation is variant preview metadata and is not part of prototype identity.**

Section 2's nine shape families in the taxonomy stay as a **UI grouping key** and must never be
used to claim two prototypes verify the same. Measured, no claimed family shares a bone-name set:
the only name common to all 37 rigs is `EXT_VoiceContext`.

Concrete consequences — see taxonomy "(a) Family verdicts":

- **Human** = one prototype, four variants: Soldier, Civilian, Utka, Mutoid. There is no
  `Human_AnimActionsDef`; the variants carry `Civilian_AnimActionsDef`,
  `Soldier_Menu_AnimActionsDef`, `Soldier_Utka_AnimActionsDef`. Searching "Mutoid" selects
  Human -> Mutoid variant, not a separate rig.
- **Exalted** = its own prototype (Human ∩ Exalted = 9 names, 6 of them `EXT_*`).
- **Mutog** = Pandoran beast. Never Vehicle, regardless of its vehicle-class tag.
- **Fishman + Oilfish** = one prototype (one prefab), two variants.
- **Facehugger + Facehugger_DroppedTorso** = one prototype, two variants; the dropped torso stays
  under Advanced until its null `SkeletonChassisAddonDef` is understood (taxonomy `:304-305`).
- **The three tech turrets** = one prototype (`CHR_NJ_TEC_Turret_T01_V01_Rig_Ready`), three
  variants — trivially, because they are one prefab.
- **Crabman and Oilcrab are NOT one prototype** although they share **34** names including real
  bones (`Root, Hips, Spine, Upper_Chest, Neck, Head, Shoulder_L/R, Carapace, TransferBone, …`).
  A Crabman-authored mesh binds partially and **silently** on an Oilcrab. This pair is the reason
  the picker exists, and the reason comparison is prefab-to-prefab and never "same family".

### PrototypeRecord schema

```text
PrototypeRecord                       -- one BINDING SIGNATURE (36 of them)
  Id, DisplayName, Category, SearchTerms
  RigPrefabNames[]                    -- 1, or 2 for the worm prototype
  Managers[], SourcePaths[], provenance
  RootMotionNodeNames[]               -- per prefab; 'Fireworm_root' for all three worm managers
  Bone[]  { name, relativePath, parent, duplicateCount }
  BindableBones[]                     -- Bone[] minus every name starting with 'EXT_'
  AttachmentPoints[]                  -- exactly those 'EXT_' names, informational
  AmbiguousNames[]                    -- names appearing more than once in Bone[]
  Variant[]                           -- manager / role
    Name, AddonsManagerDef, representative TacCharacterDef
    CharacterBodyStateDef
    Slot[] { AddonSlotDef, AttachmentPointName, representative addon defs }
    TacActorAnimActionsDef, controller name, resolved clip catalogue (deduplicated)
    PreviewPoseClip
```

`RepresentativeCharacters` moved from the record onto the **variant**: a representative
`TacCharacterDef` is what `UnitDisplayData(TacCharacterDef, SharedData)`
(`UnitDisplayData.cs:60-83`) needs, and the bay rebuild is per variant, not per prototype.

### RigTarget

**Never persisted** in the catalog. Contains live renderer and mesh instance IDs
(`SkinCompatibility.cs:51`). Constructed at selection time from the instantiated slot
renderer, then consumed by the existing `SkinCompatibility.Analyze` unchanged.

---

## 3. Verification modes

### Bone partition — the vocabulary both modes use

Every catalog rig is partitioned once, at scan time:

- **`BindableBones`** — every transform whose name does NOT start with `EXT_`.
- **`AttachmentPoints`** — exactly the `EXT_*` transforms.

The split is the game's own: `Addon.GetEquivalentBones` (`Addon.cs:1202-1231`) skips any transform
whose name starts with `EXT_` (`:1208`) and matches everything else by exact, case-sensitive name
against `manager.GetAllRigOnlyBones()`. It is also what makes the census usable: intra-family
overlap is almost entirely `EXT_*` (all four Pandoran bipeds share exactly three names, all of them
`EXT_`), so a comparison that counts `EXT_*` reports rigs as related that cannot bind to each other.

### Replace slot (exact)

- **The `RigTarget` is snapshotted from the LIVE slot `SkinnedMeshRenderer`** the bay rebuild
  produced — `ModelDoctor.Snapshot(smr, transformPath)` (`ModelDoctor.cs:196`), unchanged.
- Bone list and bind-pose count must match that renderer exactly. `smr.bones` is a small SUBSET of
  the rig, not the whole rig: slice 0's in-mission control measured a Human head slot at
  **21 bones against the rig's 124**. Never fabricate a Replace target from the full hierarchy.
- **`EXT_*` is NOT globally discarded on the Replace path.** If the live SMR actually references an
  `EXT_*` node, that node's bone index and bind pose are load-bearing. Report such bones in a
  separate "attachment point" row of the bone map, but **require** them like any other.
- Uses `ReplacementPreflight.Run(bytes, path, target)` (`src/Doctor/ReplacementPreflight.cs:37`)
  and the full diagnostic catalogue from `2026-09-01-model-doctor-design.md:249-309`, unchanged.

### Extend prototype (new)

- Extend matches the file's joints against **`BindableBones` only**. `AttachmentPoints` are
  informational on this path — the game skips them, so a rig `EXT_*` the file does not carry is
  never a defect.
- Every imported joint must resolve **uniquely** onto `BindableBones`.
- Missing rig bones that the file does not use are **allowed** (a partial body part is legitimate).
- **A file joint named `EXT_*`** is a **warning** when it is unweighted and **blocking** when it is
  weighted — the game skips it, so its weights are silently lost.
- The current analyzer requires a bijection and reports every absent target bone
  (`SkinCompatibility.cs:196-219` — "Every live bone must be in the file"). Applying that rule to a
  full-rig target would reject legitimate partial body parts.
- **Code change:** relax the bijection at `SkinCompatibility.cs:196-219` **ONLY** when mode is
  Extend. When the file's joint set is a subset of `BindableBones` and every file joint maps
  uniquely, the check passes. `MissingBone` issues for bones absent from the file are suppressed in
  Extend mode. `ExtraBone` (`:220-225`) stays Blocking in both modes.

### Duplicate bone names — never index-disambiguate

~~Duplicates are real and shipped: `ALN_Fishman_Rig_Ready` carries `Fishman_upWrist_l` twice and
`Fishman_upWrist_r` twice~~ **CORRECTED:** the slice 0 census was read case-insensitively.
Ordinally the rig carries `Fishman_upWrist_l` AND `Fishman_upWrist_L` (two distinct transforms
differing only in trailing-letter case); `Addon.GetEquivalentBones` compares case-sensitively
(`Addon.cs:1202-1231`), so both are reachable — NOT ambiguous. The only real shipped duplicates
are the three vehicles' repeated `light` nodes: `VEH_NJ_Armadillo_`, `VEH_PX_Scarab_V01_` and
`VEH_SYN_Sanator_Rig_Ready` each carry `light` twice. The game resolves by **name plus
`FirstOrDefault`**, so the second `light` is unreachable.

- **Never disambiguate compatibility by index.** Doing so would predict a binding the game cannot
  perform.
- Display the full path and the index **for diagnosis only**.
- **Block a verdict only when the selected file or the selected renderer actually references the
  ambiguous name.** Otherwise the record carries a prototype-level **warning** and every other slot
  stays usable. The vehicles' duplicate `light` nodes must not make unrelated slots unverifiable.
  (Fishman's wrists are case-variants, not duplicates — see correction above.)
- On the Replace path this is already the existing `BindCode.TargetBoneDuplicate`
  (`SkinCompatibility.cs:158-161`), which fires from the live SMR's own bone list — i.e. it fires
  exactly when the renderer references the ambiguous name, which is the rule above.

---

## 4. Catalog build, and the bay transaction that produces a target

### DefRepository scan — lazily, on first bench open

- `GameUtl.GameComponent<DefRepository>().GetAllDefs<TacCharacterDef>()` — proven at
  `FitBench.cs:580-593`.
- Join each `TacCharacterDef` -> `GetAddonsMangerDef()` (`TacCharacterDef.cs:172-175`)
  -> manager / body state / anim-actions def.
- Deduplicate by **bindable-bone signature** only (§2). 792 `TacCharacterDef`s collapse to
  46 managers, 42 rigged, 37 rig prefabs, **36 binding prototypes**.
- **Built lazily on FIRST BENCH OPEN, not at menu time.** `DefRepository` stays the source, but
  the scan runs once the campaign's and the mods' defs have settled. **Rescan on command only** —
  a button under Advanced, not automatic.
- **No HomeScreen instantiation path ships.** At most it survives as a diagnostic census command
  (that is how `rig-census-2026-09-02.json` was produced), because no menu-time target can produce
  a trustworthy Replace renderer — see §1.

### `PrototypeBaySession` — the transaction

`src\Dev\PrototypeBaySession.cs` owns the bay for the duration of a prototype selection and puts
it back exactly as it found it. It uses the **existing squad-bay `CharacterBuilder`**, never a
second hidden builder: a hidden root buys no Addressable residency (slice 0(b)) and would duplicate
the builder/coroutine lifecycle the bench already handles.

For the chosen **variant**:

1. `new UnitDisplayData(representativeTacCharacterDef, GameUtl.GameComponent<SharedData>())`
   — `UnitDisplayData.cs:60-83`; it fills `AddonsManagerDef`, `GameTags`, `AnimActionDef` and
   `CharacterLightObjectName` off the template.
2. `CommonCharacterUtils.DisplayCharacter(bay.CharacterBuilder, data, out rigChanged)`
   — `CommonCharacterUtils.cs:25-52`. It swaps the manager (`AddonsCharacterBuilder.UseAddonManager`,
   `AddonsCharacterBuilder.cs:143-173`) and copies the controller off the rig prefab (`:43`).
3. **Copy the variant's game tags onto the manager** — `SetAutorefreshOnTagsChanged(false)`,
   `GameTags.Clear()`, `GameTags.AddRange(data.GameTags)`. This is `DisplaySoldier`'s own
   sequence (`UIModuleActorCycle.cs:613-615`) and is why `FitBench.Show` does it at
   `FitBench.cs:1061-1067`: every human template shares one rig, so `UseAddonManager` returns
   false, the manager SURVIVES the switch, and with it the previous character's tags.
4. `CommonCharacterUtils.RebuildCharacter(bay.CharacterBuilder, bodyparts, weapon)`
   — `CommonCharacterUtils.cs:54-64`, with the representative's own bodyparts.
5. **After `OnCharacterRebuilded`**, enumerate the real slot SMRs. The rebuild is a COROUTINE
   (`StartRebuildCharacter`, `AddonsCharacterBuilder.cs:176`; the event fires at `:293`), so the
   renderers do not exist when step 4 returns. The bench already subscribes at `FitBench.cs:418`
   and re-points `doctor.Root = bay.CharacterBuilder.transform` in `Posed` (`FitBench.cs:1140`).

### Restoration — explicit, not hopeful

- **Snapshot before the first prototype rebuild:** `UIModuleActorCycle.CurrentUnit`
  (`UIModuleActorCycle.cs:174`, a `UnitDisplayData`; `CurrentCharacter` at `:172` is its
  `GeoCharacter`), plus the builder's addons / tags / weapon / helmet state.
- **Never call `GeoCharacter.SetItems`, never `SaveLoadout`, never alter a template.** The session
  mutates PRESENTATION only; the save's soldier is untouched.
- **On close, failure or level change:** dispose the Doctor previews, then **wait out or invalidate
  the asynchronous prototype rebuild** (a second rebuild started before the first fires its event
  leaves the bay showing a mix), then call `DisplaySoldier` for the captured unit —
  `UIModuleActorCycle.DisplaySoldier(UnitDisplayData, resetAnimation, addWeapon, showHelmet)`
  (`UIModuleActorCycle.cs:602`); the `GeoCharacter` overload is `:590`. TheTurned uses exactly this
  seam to repaint a bay model: `BionicsApplyPatch.RefreshModel` (`BionicsApplyPatch.cs:132`) calls
  `actorCycle?.DisplaySoldier(ch, resetAnimation: false, addWeapon: true)` at `:137`.
- **Restore pose/camera only AFTER that rebuild completes** — `FitBench.Posed` (`:1130-1173`) is
  where scene-root position/scale and `Reframe()` belong, and it runs on the rebuild callback.
- `StillThere()` (`FitBench.cs:997-1002`) is the existing "the level went away" test; a session
  that fails it disposes rather than restoring into a dead bay.

### Slot availability

A slot whose Addressable has not loaded produces no renderer. Show **"slot visual unavailable"** on
that row and refuse a Replace verdict for it; the Extend path still works, because it needs
`BindableBones` and not a renderer. In a loaded geoscape this is an edge case (the bay's own soldier
renders); at the menu it was the state of EVERY slot, which is why the menu path is gone.

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

### Clip resolution — the anim-actions def is NOT enough

Slice 0(d) measured it: `Crabman_AnimActionsDef` has `AnimActions.Count == 0`, no
`DefaultActionClip` and no `DefaultReactionClip`, while `Soldier_Utka_AnimActionsDef` has 177. A
preview that reads only `TacActorAnimActionsDef` (`TacCharacterDef.GetAnimActionDef()`,
`TacCharacterDef.cs:187-190`) shows an EMPTY clip list for Crabman.

Resolution order, per variant:

1. The variant's `TacActorAnimActionsDef` — `BaseAnimActions`, `DefaultActionClip`,
   `DefaultReactionClip`, `AnimActions`.
2. **Fallback when that yields nothing: the controller's own `animationClips`.** The controller is
   on the RIG PREFAB, not on the def — `CommonCharacterUtils.cs:42-43` copies
   `addonsManagerDef.Rig.GetComponent<Animator>().runtimeAnimatorController` onto the live rig.
3. **Deduplicate by name.** Controllers list duplicates: `HumanoidAnimatorLOC` has 73 entries /
   **69 distinct**, `MidMonsterAnimator` 60 / **45**.

A clip list does NOT identify a prototype and must never be shown as if it did: Human and Crabman
both carry `HumanoidAnimatorLOC`, and Mutog carries `MidMonsterAnimator` whose clips are named
`Chiron_*`. The list is labelled with the CONTROLLER name it came from.

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

### Slice 0 — Pre-flight verification — **DONE 2026-09-02**

Ran live on `D:\PP-Instance2` through PPCLI. Results and raw census recorded in taxonomy
"Slice 0 verification" + `research/rig-census-2026-09-02.json`. (a) the family merges were WRONG,
the prototype unit is the rig prefab; (b) a menu-time prototype gives the skeleton and **zero**
renderers; (c) no DLC-only manager exists; (d) the controller is shared across prototypes and
`AnimActions` can be empty. Everything below is the original brief, kept for the record.

#### Original brief (before any catalog code)

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

**Geoscape only.** The bench already refuses to open outside a playing geoscape level with a squad
bay (`FitBench.cs:364-379`); the picker inherits that gate and adds no other entry point.

- Catalog from a `DefRepository` scan, built **lazily on first bench open**; manual Rescan kept.
- Reactive grouped search (case-insensitive, token-AND).
- Category / prototype / variant / slot / mode selection.
- No dependency on whichever actor the bench happened to pose.
- Replace mode snapshots ONE live slot renderer produced by a `PrototypeBaySession` rebuild;
  Extend validates a unique subset of `BindableBones`.
- Slots with no renderer reported explicitly ("slot visual unavailable").

**Acceptance (all in a loaded geoscape campaign on `D:\PP-Instance2`):**
- Selecting Human -> Soldier -> Head rebuilds the bay and produces a `RigTarget` whose bone names
  equal the live head `SkinnedMeshRenderer`'s — a SUBSET of the rig's 124, not the rig.
- Selecting Crabman -> Torso produces a different `RigTarget` (58-bone rig, no name overlap beyond
  the `EXT_*` three).
- Searching "mutoid" selects Human -> Mutoid variant, not a separate rig.
- Selecting the worm prototype offers three variants (Fireworm / Acidworm / Poisonworm) over one
  bone set.
- A vehicle and a static structure can both be selected and produce a target or an explicit
  "slot visual unavailable" — never a fabricated one.
- **Closing the picker leaves the SAME squad member with the SAME loadout visible**, and
  `Player.log` gains no new exception across the run.
- Offline tests, over `research/rig-census-2026-09-02.json` as the fixture: the census yields
  **37 rigs and 36 binding prototypes**; Fireworm ≡ Acidworm on `BindableBones`;
  Crabman ≢ Oilcrab despite 34 shared names; the four rig-less managers are excluded;
  `AmbiguousNames` lists Fishman's two wrist pairs and the three vehicles' `light`.
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

1. ~~**Bone counts per rig**~~ — **ANSWERED**, slice 0(a): 2551 transforms over 37 rigs, per-rig
   table in the taxonomy, raw data in `research/rig-census-2026-09-02.json`.
2. ~~**DLC coverage**~~ — **ANSWERED**, slice 0(c): no DLC-only manager. Umbra is a mutation
   variation, Hoplite is a `HumanoidGuardian`, Cyclops has no `TacCharacterDef`.
3. ~~**Menu-time bodypart visuals**~~ — **ANSWERED**, slice 0(b): `SetupAddons` succeeds and yields
   **0** `SkinnedMeshRenderer`s at the HomeScreen; 7 in a loaded mission. This is what moved the
   whole design to the geoscape bay.
4. ~~**Cross-prefab bone-name equality**~~ — **ANSWERED**, slice 0(a): Crabman ∩ Oilcrab = 34 real
   names across different prefabs. Comparison is prefab-to-prefab, never by family.
5. **Facehugger DroppedTorso** — has a rig but `SkeletonChassisAddonDef == null`. Still open;
   shipped as an Advanced/corpse **variant** of the Facehugger prototype.
6. **PreviewPoseClip** population — Acheron shows null; the other 43 unchecked. Still open. If
   populated, it is the natural pose for a prototype preview; the §7 controller fallback covers
   the case where it is not.
7. ~~**Acidworm/Poisonworm and tech turret merges**~~ — **ANSWERED**, slice 0(a): the three tech
   turrets are ONE prefab; Fireworm/Acidworm/Poisonworm are one bone set over two prefabs, and are
   merged into a single prototype with three variants (§2).
