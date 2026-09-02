# Skinned-unit prototype taxonomy (for Model Doctor "pick a prototype")

Research note, 2026-09-02. Read-only dig. Two evidence classes, both cited:

- **SRC** = decompile, `E:\DEV\PhoenixPoint\decompiled\AssemblyCSharp\Assembly-CSharp\src\...`
  (paths below are relative to that root).
- **LIVE** = the real def repository, read through PPCLI cold batches against `D:\PP-Instance2`
  (build `8939f00f`, HomeScreen, no mission). Verbs used: `find {all:true,type:...}`,
  `inspect {h:"@def:...",values:true}`, `call {op:get|invoke,target:"@def:..."}`.

---

## 0. The one sentence

**A "prototype" is an `AddonsManagerDef`, not a `TacCharacterDef`.** The def carries the rig prefab
and the root-motion node; the mesh comes in as *addons* (bodypart items) that are re-parented onto
that rig by BONE NAME. 792 `TacCharacterDef`s collapse onto **44 shipped `AddonsManagerDef`s**, of
which **40 have a rig**, resolving to **35 distinct rig prefabs**. That is the whole picker.

```
AddonsManagerDef                                   PhoenixPoint.Common.Entities.Addons\AddonsManagerDef.cs:8-17
  public AddonDef SkeletonChassisAddonDef;         :10
  public GameObject Rig;                           :12   <- the skeleton prefab, a DIRECT reference
  public string RootMotionNodeName = "";           :14
CharacterAddonsManagerDef : AddonsManagerDef       PhoenixPoint.Common.Entities\CharacterAddonsManagerDef.cs:6-12
  public AnimationClip PreviewPoseClip;            :11   <- a pose the game itself uses to preview a rig
```

43 of the 46 managers are `CharacterAddonsManagerDef`, 3 are the plain base (LIVE, `find` type dump).

---

## 1. Archetypes and the flags that separate them

`TacCharacterDef` (`PhoenixPoint.Tactical.Entities\TacCharacterDef.cs:32`) is the unit template.
Every archetype predicate is a **game-tag test**, not a class or an enum:

| Predicate | file:line | Tag field (`SharedGameTagsDataDef.cs`) |
|---|---|---|
| `IsAlien` | TacCharacterDef.cs:97, 218-221 | `AlienTag` (a `RaceTagDef`) :38 |
| `IsHuman` | :99, 223-226 | `HumanTag` :113 |
| `IsVehicle` | :101, 228-231 | `VehicleTag` :77 |
| `IsMutog` | :103, 233-236 | `MutogTag` :80 |
| `IsMutoid` | :105, 238-241 | `MutoidTag` :83 (+ `MutoidClassTag` :154, `MutoidBodyPartTag` :86) |
| civilian | — | `CivilianTag` (a `ClassTagDef`) :145 |
| vehicle class | — | `VehicleClassTag` :98 |
| organic / metallic | :243-261 | `SubstanceTags Substances` :107 |

Notes that matter for a picker:

- **Mutog is tagged vehicle-class but is not a vehicle rig.** It is its own quadruped
  (`Mutog_AddonsManagerDef` -> `ALN_Mutog_Rig_Ready`, LIVE). Do not group it with Armadillo/Scarab.
- **Mutoid is not its own rig.** LIVE: `Mutoid_CharacterTemplateDef.GetAddonsMangerDef()` ->
  `Human_AddonsManagerDef`. Same for `GenericSoldier_CharacterTemplateDef` and
  `IN_Civilian_TacCharacterDef`. Faction, class, gender and race are **skin variants on one rig**.
- Faction never appears in the manager list at all: there is exactly ONE `Human_AddonsManagerDef`
  (LIVE, `find {all:true,type:"AddonsManagerDef"}` -> 46 rows, one Human).
- Naming in defs vs the game's fiction: **Arthron = Crabman, Triton = Fishman, Mindfragger =
  Facehugger, Scylla = Queen, Chiron = Chiron, Siren = Siren, Acheron = Acheron.**
- The manager for a def is reached by `TacCharacterDef.GetAddonsMangerDef()` (TacCharacterDef.cs:172-175)
  -> `ComponentSetDef.GetComponentDef<AddonsComponentDef>().AddonsManagerDef`.

---

## 2. Rig families (LIVE — 44 shipped managers, 35 distinct `Rig` prefabs)

Grouped so that everything in one row faces the SAME verification (one bone-name set, one slot
vocabulary, one anim-actions shape). `Rig` identity was taken by `instanceId`, not by name.

| # | Family | Managers (`*_AddonsManagerDef`) | Rig prefab(s) | Root-motion node |
|---|---|---|---|---|
| 1 | **Human** (all factions, all classes, Mutoid, Civilian) | `Human` | `CHR_Human_Rig_Ready` | `BaseManReference` |
| 2 | **Anu Exalted** | `Exalted` | `CHR_AN_Exalted_Rig_Ready` | `Root` |
| 3 | **Pandoran bipeds** | `Crabman`, `Fishman`+`Oilfish`, `Siren`, `Queen` | `ALN_Crabman_`, `ALN_Fishman_` (shared by Oilfish), `ALN_Siren_`, `ALN_Queen_Rig_Ready` | `Root`, `Fishman_Root`, `Root`, `ALN_Queen_Root` |
| 4 | **Pandoran multi-leg / beasts** | `Acheron`, `Chiron`, `Mutog`, `Facehugger`+`Facehugger_DroppedTorso`, `Oilcrab` | `ALN_Acheron_V01_`, `ALN_Chiron_`, `ALN_Mutog_`, `ALN_Facehugger_` (shared), `ALN_Oilcrab_Protean_Rig_Ready` | `Root`, `Chi_Root`, `Root`, `Facehugger_Root`, `Root` |
| 5 | **Worms / small swarm** | `Fireworm`, `Acidworm`+`Poisonworm`, `Swarmer`, `Yugothian` | `ALN_Fireworm_`, `ALN_Acidworm_` (shared by Poisonworm), `ALN_Swarmer_V01_`, `ALN_Yugothian_Rig_Ready` | `Fireworm_root` (all three worms), `Swarmer_Root`, `Root` |
| 6 | **Static Pandoran structures** | `EggExplosive`, `EggFacehugger`, `EggFireWorm`, `EggSwarmer`, `SpawningPoolCrabman`, `CorruptionNode`, `SentinelHatching`, `SentinelMist`, `SentinelTerror`, `InjectorBomb` | one each (`ALN_Egg_*`, `ALN_Crabman_Spawning_Pool_`, `ALN_CorruptionNode_`, `ALN_SNT_Hatching/Mist/Terror_`, `WPN_PX_Injector_V01_Rig_Ready`) | `_Root`, `Swarmer_egg_Root`, `Fireworm_egg_Root`, `Rig_Nod`, `SNT_Htc_Root`, `Mist_Sentinel_Root`, `Terror_Sentinel_Root`, `Root` |
| 7 | **Vehicles** | `NJ_Armadillo`, `PX_Scarab`, `SY_Aspida`, `KS_Kaos_Buggy` | `VEH_NJ_Armadillo_`, `VEH_PX_Scarab_V01_`, `VEH_SYN_Sanator_`, `VEH_KS_Death_Buggy_05_Rig_Ready` | `Armadillo_Reference`, `Root`, `Root`, `Root` |
| 8 | **Turrets / drones** | `NJ_TechTurret`+`NJ_PRCRTechTurret`+`PX_LaserTechTurret`, `PX_SentryTurret`, `SpiderDrone` | `CHR_NJ_TEC_Turret_T01_V01_Rig_Ready` (**shared by all three tech turrets**), `PX_Security_Turret_`, `CHR_SY_Spider_Mine_Rig_Ready` | `Turret_Origin` (x3), `Root`, `Root` |
| 9 | **Ancients** | `HumanoidGuardian`, `MediumGuardian` | `CHR_AC_1x1_Guard_`, `CHR_AC_MediumGuardian_Rig_Ready` | `ROOT`, `Root` |
| 10 | **Rig-less** (`Rig == null`, nothing to verify against) | `DefaultTacCharacter`, `Dropped`, `FallDown`, `YuggothianDropped_ItemContainer` | — | — |
| — | (test/harness) | `Dummy` -> `Dummy_Rig_Ready`; `ct_creature_*` = ContentTool's own baked creatures | | |

**Rig sharing is real but rare** — only 4 groups share a prefab: worms (Acidworm+Poisonworm),
fish (Fishman+Oilfish), Facehugger + its dropped torso, and the three tech turrets. Everything else
is a rig of its own. **Vehicles and turrets DO have skeletons** (wheels, turret yaw/pitch, hatches):
every one has a non-null `Rig` and a named root-motion node.

`ResourcePath` (LIVE `inspect`) shows the def tree these live in, and it is a clean grouping key:
`Defs/Tactical/Actors/{Aliens|PhoenixPoint|NewJericho|Synedrion|DesciplesOfAnu|Kaos|AncientGuardians|_Common}/<UNIT>/BasicDefs/<X>_AddonsManagerDef`.

---

## 3. How a skinned mesh actually binds to a prototype (the verification rule)

This is the mechanism the Doctor must mirror; it is bone-NAME matching, nothing else.

- Rig is instantiated once per actor: `AddonsManager.cs:111-119` —
  `RigRoot = Instantiate(AddonsManagerDef.Rig, RigRootContainer)`, then
  `RootMotionNode = RigRoot.FindTransformInChildren(AddonsManagerDef.RootMotionNodeName)`
  (:117 logs an error if the node name is empty).
- Each addon's visual prefab is skinned onto that rig when `AddonDef.SkinData.SkinsToRig` is true
  (`AddonSkinDataBase.cs:12`; consumed at `Addon.cs:1059-1073`).
- The match: `Addon.GetEquivalentBones` (`Addon.cs:1202-1231`) walks the addon's own transforms and
  finds `manager.GetAllRigOnlyBones().FirstOrDefault(bone => ownBoneName == bone.name)` — **exact,
  case-sensitive, name equality**. Transforms whose name starts with `EXT_` are skipped (:1208).
- Two skin rules: `ReparentToEquivalentRigBone` (match by own name) and
  `AddToCorrespondingRigParent` (match by the PARENT's name), from
  `AddonSkinnedToRigBone.SkinRule` (`Addon.cs:1213-1222`).
- `Addon.SkinBoneToRig` (`Addon.cs:1238-1258`) re-parents and then **renames** the bone to
  `#<name>_Addon => <AddonDef.name>` (:1248). That is exactly the decoration ContentTool already
  strips in `SkinBinder.Plain()` (`ContentTool\src\Import\GlbReader.cs:2560`) — it is a runtime
  rename, so a live target's `smr.bones[i].name` is NOT the authored bone name.
- A bone name the rig does not have simply does not appear in the dictionary: **no error, the addon
  bone stays where it was**. Silent partial binding is the failure mode a prototype check prevents.

---

## 4. Body parts — the slots each prototype exposes

Two different, coexisting vocabularies:

- **Humans** get three *item* slots plus attachment points:
  `HumanBodypartsItemSlots { HeadSlotDef, TorsoSlotDef, LegsSlotDef }`
  (`PhoenixPoint.Common.Entities.GameTagsSharedData\HumanBodypartsItemSlots.cs:11,13,15`), referenced
  from `SharedGameTagsDataDef.cs:110`. LIVE the shipped `Human_*` slot defs are:
  `Head, Torso, Legs, LeftArm, RightArm, LeftHand, RightHand, LeftLeg, RightLeg, Hair, FacialHair,
  HeadAttachment, LegsAttachment, BackPack, Holster, GunPoint, MechArm, ShieldPoint,
  ShieldPoint_Back` (+ `Heavy_Jetpack_SlotDef`, `Mutoid_RightArmSyphonWeapon_SlotDef`).
- **Everything else gets its OWN slot set**, named after the archetype (LIVE, `find` over
  `AddonSlotDef` -> 241 rows incl. 199 `ItemSlotDef`). Examples, verbatim:

| Prototype | Slots |
|---|---|
| Crabman | `Head, Torso, Carapace, LeftArm, RightArm, LeftHand, RightHand, LeftLeg, RightLeg` |
| Fishman | `Head, Torso, Legs, LeftArm, RightArm, LeftLeg, RightLeg, UpperArms, UpperLeftArms, UpperRightArm` |
| Siren | `Head, Torso, Arms, LeftArm, RightArm, LeftHand, RightHand, Legs` |
| Queen (Scylla) | `Head, Torso, Abdomen, Carapace, LeftArm, RightArm, LeftHand, RightHand, Left/Right Front/Middle/Back Leg` (14) |
| Chiron | `Head, Torso, Abdomen, Front/Rear Left/Right Leg` |
| Acheron | `Head, Torso, Husk, Arms, LeftArm, RightArm, Front/Rear Left/Right Leg` |
| Mutog | `Head, Torso, Tail, Front/Rear Left/Right Leg` |
| Facehugger | `Head, Abdomen, Front/Back Left/Right Leg, DroppedTorso` |
| Worms / Oilcrab / Oilfish / SpiderDrone / Dummy | a single `*_Torso_SlotDef` |
| Exalted | `Head, Torso, Legs, LeftArm, RightArm, TentaclesLeft, TentaclesRight` |
| HumanoidGuardian | `Head, Torso, Legs, LeftLeg, RightLeg, Crystal_LeftArm, Orichalcum_Left/RightArm, Drill_RightArm` |
| MediumGuardian | `Head, Torso, Legs, Front/Rear Left/Right Leg` |
| Armadillo | `Turret, Hull_Upgrade, Engine_Upgrade` |
| Kaos Buggy | `Left/Right Front Wheel, LeftBackWheel` |
| Tech turrets | `Body, Gun` (`LaserTechTurret_*`, `PRCRTechTurret_*`); SentryTurret adds `Base` |
| SpawningPool | `Body, Roof, six *_Egg_ slots` |
| Sentinels | Hatching/Mist: `Body, Head, Roots`; Terror: `BodyFront/Left/Right, HeadFront/Left/Right, Roots` |

**So yes — the slot set differs per archetype and is the per-prototype "parts" list.** Where it comes
from at runtime: `CharacterBodyStateDef.BodyPartsDefs` (`PhoenixPoint.Common.Entities.Characters\CharacterBodyStateDef.cs:10-12`),
read by `TacCharacterDef.GetTemplateBodyparts` (`TacCharacterDef.cs:192-204`), which concatenates
`SkeletonChassisAddonDef` + `BodyPartsDefs` + `Data.BodypartItems` + every `SubAddons` recursively.
Slot wiring itself is `AddonDef.RequiredSlotBinds` / `ProvidedSlots` / `SubAddons`
(`AddonDef.cs:68-75`), with `ProvidedSlotBind.AttachmentPointName` naming the rig bone (:20).

The visual behind a bodypart is an `AddonSkinDataBase` (`AddonSkinDataBase.cs:9-30`, `GetVisuals`
:16). Shipped subclasses and LIVE counts:

| Skin-data def | LIVE count | Used by |
|---|---|---|
| `SimpleBodyPartSkinDataDef` | 409 | monsters/vehicles/turrets — `NormalPrefab` + `DisabledPrefab` (`SimpleBodyPartSkinDataDef.cs:15-17`) |
| `ArmourBodyPartSkinDataDef` | 259 | human armour pieces |
| `SimpleSkinDataDef` | 243 | weapons / props |
| `UniformBodyPartSkinDataDef` | 7 | human uniforms |
| `HumanoidBodyPartSkinDataDef` | 3 | human Head/Torso/Legs, indexed by `RaceTagDef` then per-tag variant (`HumanoidBodyPartSkinDataDef.cs:16-24,40-54`) |
| `FilteredSkinDataDef` | 0 | declared, none shipped |
| (`HumanoidHeadSkinDataDef`, `HumanoidHairSkinDataDef`, `HumanoidFacialHairSkinDataDef`) | not counted | human head/hair variants |

The user's sample dump `ContentTool\APOCD GLBs for content tool without apply tranforms\` holds
`CHR_PX_HVY_LL_M_V01_*.glb`, `CHR_PX_HVY_RL_M_V01_*.glb`, `CHR_PX_HVY_TS_M_V01_*.glb` — i.e.
Phoenix Heavy left leg / right leg / torso, male, V01. **One bodypart = one .glb = one
SkinnedMeshRenderer**, which is why a "prototype" check has to be per-slot, not per-unit.

---

## 5. Animation

- Per prototype there is a `TacActorAnimActionsDef` component on the component set:
  `TacCharacterDef.GetAnimActionDef()` (`TacCharacterDef.cs:187-190`).
- Its shape (`PhoenixPoint.Tactical.Entities.Animations\TacActorAnimActionsDef.cs:7-26`):
  `BaseAnimActions` :12, `DefaultActionClip` :14, `DefaultReactionClip` :16, `AnimActions` :19.
- **The AnimatorController is on the RIG PREFAB, not on the def**:
  `CommonCharacterUtils.cs:41-42` —
  `RigRoot.GetComponent<Animator>().runtimeAnimatorController = addonsManagerDef.Rig.GetComponent<Animator>().runtimeAnimatorController`.
  So "prototype X has clip set Y" resolves as: rig prefab -> `Animator.runtimeAnimatorController`,
  and the anim-actions def substitutes clips into an override controller on top of it.
- The anim-action classes (all under `PhoenixPoint.Tactical.Entities.Animations\`):
  `TacActorNavAnimActionDef`, `TacActorIdleAnimActionDef` (+`TacActorMenuIdleAnimActionDef`),
  `TacActorShootAnimActionDef` (+`Melee`, +`AbilityDependent`), `TacActorSimpleAbilityAnimActionDef`
  (+`Jump`), `TacActorAimingAbilityAnimActionDef`, `TacActorSimpleReactionAnimActionDef`,
  `TacActorSimpleItemAnimActionDef`, `TacActorSimpleInteractionAnimActionDef`, and the item-side
  `TacItem*AnimActionDef` family.
- LIVE: **42 `TacActorAnimActionsDef` (40 shipped + ContentTool's 2)**, and the names line up
  1:1 with the manager names — `Acheron_`, `Chiron_`(+`Chiron_StabilityStance_`), `Crabman_`,
  `Fishman_`, `Oilfish_`, `Oilcrab_`, `Siren_`, `Queen_`, `Mutog_`, `Facehugger_`, `Fireworm_`,
  `Swarmer_`, `Yugothian_`, `Exalted_`, `HumanoidGuardian_`, `MediumGuardian_`, `NJ_Armadillo_`,
  `PX_Scarab_`, `SY_Aspida_`, `KS_Kaos_Buggy_`, `SpiderDrone_`, the four turrets, the four eggs,
  `SpawningPoolCrabman_`, the three `Sentinel*`, `CorruptionNode_`, `InjectorBomb_`, `Dummy_`.
  **Humans are the exception**: there is no `Human_AnimActionsDef` — instead
  `Civilian_AnimActionsDef`, `Soldier_Menu_AnimActionsDef`, `Soldier_Utka_AnimActionsDef`, i.e. one
  human rig with several anim-action sets selected per role, and the per-weapon variation carried by
  the `*EquipmentFilteredDef` actions.
- ContentTool's own view of this (already implemented): `CreatureRoles` / required roles
  `walk, idle, attack, death` (`ContentTool\src\Tactical\CreatureManifest.cs:93`), optional
  `jump, reaction, ranged, climb`; the reaction path is `TacticalActor.cs:1627-1633` writing over
  `TacActorAnimActionsDef.DefaultReactionClip` then `SetTrigger("Reaction")`
  (cited in `CreatureManifest.cs:70-84`).

---

## 6. Catalog size, and where to load a prototype from without a mission

LIVE totals (`find {all:true,type:X}`, `total` field — `type` is an *assignable-to* filter, so a
count includes subclasses):

| Type | total |
|---|---|
| `TacCharacterDef` (incl. `CharacterTemplateDef`) | **792** |
| `AddonsManagerDef` (43 `CharacterAddonsManagerDef` + 3 base) | **46** (44 shipped + 2 ContentTool) |
| distinct `Rig` prefabs behind those | **35** |
| `TacActorAnimActionsDef` | **42** |
| `AddonSlotDef` (incl. 199 `ItemSlotDef`) | **241** |
| `AddonDef` (incl. `TacticalItemDef`, `WeaponDef`, …) | **1210** |
| `TacticalItemDef` | **1128** |
| skin-data defs (all `AddonSkinDataBase` subclasses summed) | **921** |

**Answer to "50 items or 5000": the prototype list is 40.** The parts list under a chosen prototype
is single digits to low tens of slots. Only if the UI ever enumerated every skinned prefab would it
reach ~900-1200, and it does not need to.

**Loading a prototype with no mission and no save.** The rig prefab is a *direct* `GameObject`
reference on the def (`AddonsManagerDef.cs:12`), not an Addressable — verified LIVE at the
HomeScreen: `call {op:"get",target:"@def:Crabman_AddonsManagerDef",member:"Rig"}` returned
`{name:"ALN_Crabman_Rig_Ready", instanceId:21742}` with nothing loaded. So every rig is reachable
from the main menu through `GameUtl.GameComponent<DefRepository>()`, which ContentTool already uses
(`ContentTool\src\Tactical\WeaponBuild.cs:68`, `:102`).

The game ships the exact recipe for building one standalone — `AddonsCharacterTester`
(`PhoenixPoint.Common.Entities.Addons\AddonsCharacterTester.cs:44-57`):

```csharp
AddonsManager = Repo.Instantiate<AddonsManager>(AddonsManagerDef);   // :46
AddonsManager.SetupRig(transform);                                    // :47
BodyParts = CharacterBodyStateDef.BodyPartsDefs
              .Select(id => Repo.Instantiate<TacticalItem>(id)).ToList();  // :50
AddonsManager.SetupAddons(BodyParts);                                 // :51
AddonsManager.SetRagdollMode(CollidersRagdollActivationMode.Targeting); // :58
```

That is a MonoBehaviour with a `DefRepository` field and no level, no faction, no actor — the
closest thing to an official "prototype viewer" in the codebase.

**Caveat on the meshes.** Bodypart *visuals* are Addressables:
`AddonSkinDataBase.GetPrefabAsset` returns null unless `assetReference.Asset` is already resident
(`AddonSkinDataBase.cs:18-29`), and it hard-returns null outside play mode. So a menu-time prototype
gives you the SKELETON for free; the SkinnedMeshRenderers need the addressable to be loaded first.

---

## 7. What ContentTool already knows (so the new work is additive)

- `ContentTool\src\Dev\ModelDoctor.cs:616-648` — the target picker today is
  `Root.GetComponentsInChildren<SkinnedMeshRenderer>(true)` over whatever actor is "on the stand".
  A prototype picker replaces `Root` with an instantiated `AddonsManager`, nothing else changes.
- `ModelDoctor.PickTarget` / `Snapshot` (`:101-110`, `:196`) already fingerprint a target as
  `RigTarget` (`ContentTool\src\Import\SkinCompatibility.cs:51-77`): `BoneNames`, `Rigged`,
  `BindPoseCount`, `TransformPath`, `MeshName`. A prototype only has to *produce a `RigTarget`* —
  the whole downstream check (`SkinCompatibility.Analyze`, `:91-95`) is already prototype-agnostic.
- `ContentTool\src\Tactical\CreatureManifest.cs:137` — donor default `Swarmer_TacCharacterDef`, and
  :121-137 already records why the donor choice is a rig/agent decision (a Mutog donor gives a 3x3
  `MedMonster` nav agent and `Move3x3_AbilityDef`). Same taxonomy, stated per-donor.
- `ContentTool\src\Tactical\NativeBones.cs:122-175` already measures `herRest - ppRest` across a
  donor rig by relative transform path — that IS a prototype comparison, just wired to a donor def
  instead of a picker.

---

## 8. Open questions / unverified

> **Questions 1, 2, 3 and 4 are ANSWERED** by the live run recorded in
> *Slice 0 verification (2026-09-02, live Instance2)* at the end of this file. They are left here
> as written so the before/after is visible. 5, 6 and 7 are still open.

1. **Bone counts per rig were never read.** No `Rig.transform` walk was done, so "how many bones does
   `CHR_Human_Rig_Ready` have" is unanswered here. (ContentTool's own note quotes 214 bones for the
   humanoid demo rig and 56 measured segments — `NativeBones.cs:203`, `CreatureManifest.cs:224-232` —
   but that is the mod's model, not a measurement of PP's rig taken in this dig.)
2. **DLC coverage of the def repository is unknown.** The 44 managers came from `D:\PP-Instance2` with
   whatever DLC/entitlements that install has. No manager named Umbra, Cyclops or Hoplite appeared.
   Either those units map onto managers listed here under different internal names (Guardian /
   Queen / Sentinel are the likely candidates), or that install does not load them. Re-run
   `find {all:true,type:"AddonsManagerDef"}` on `D:\Steam\steamapps\common\Phoenix Point` to settle it.
3. **Whether a menu-time prototype can actually show meshes.** The rig resolves at the HomeScreen
   (verified); the addon visuals go through Addressables and were NOT tested there. Until an
   `AddonsManager.SetupAddons` is run at the menu, "the Doctor can preview a prototype without a
   mission" is a plan, not a fact.
4. Rig sharing was proven by `Rig` **instanceId equality**, which proves the same prefab. It does NOT
   prove that two prototypes with *different* prefabs have different bone-name sets — e.g. Crabman and
   Oilcrab have separate prefabs but may still share names. Unverified.
5. `Facehugger_DroppedTorso_AddonsManagerDef` has a rig but `SkeletonChassisAddonDef == null` — the
   only such case. Its meaning (a corpse-only manager?) was not chased.
6. Whether `CharacterAddonsManagerDef.PreviewPoseClip` is populated on the shipped defs was not read
   (the LIVE `inspect` on Acheron showed `PreviewPoseClip: null`; the other 43 were not checked).
   If it is set for humans, it is the natural pose for a prototype preview.
7. The `Dropped` / `FallDown` / `YuggothianDropped_ItemContainer` managers carry `Rig == null` and
   are almost certainly item containers, not units — inferred from their `ResourcePath`
   (`Defs/Tactical/Actors/_Common/ItemContainers/...`), not from code.

---

## Slice 0 verification (2026-09-02, live Instance2)

Ran in-game on `D:\PP-Instance2` (build `8939f00f`, PPBridge `ppcli/1`), through PPCLI only.
The install is heavily modded (TFTV, ContentTool, Renderforge, PpFit, CustomCreature, ...), so
two of the 46 managers and two of the 37 rigs are ContentTool's own; everything else is shipped.

### How it was measured

```powershell
cd E:\DEV\PhoenixPoint\PPCLI
.\ppcli.ps1 connect state                                        # gate first, always
.\ppcli.ps1 connect find '{"all":true,"type":"AddonsManagerDef","pageSize":200}'
# per manager -> rig, batched:
.\ppcli.ps1 connect multi '@req-rigs.json'                       # {"op":"get","target":"@def:<M>","member":"Rig"}
# per rig -> every transform under the prefab:
.\ppcli.ps1 connect call  '{"op":"invoke","target":"<RIG>","member":"GetComponentsInChildren","typeArgs":["UnityEngine.Transform"],"args":[true]}'
.\ppcli.ps1 connect items '{"h":"<ARRAY>","page":0,"pageSize":200}'
.\ppcli.ps1 connect multi '@req-cc.json'                          # {"op":"get","target":"<T>","member":"childCount"}
# (b), one plan with a mandatory finally:
.\ppcli.ps1 plan .\plans\prototype-menu-load.json '{"manager":"Human_AddonsManagerDef","bodyPart":"PX_Assault_Torso_BodyPartDef"}'
```

`GetComponentsInChildren<Transform>(true)` returns **DFS preorder**, so the hierarchy was rebuilt
from that order plus `Transform.childCount` per node - no `parent` round-trip per bone. Verified on
`ALN_Crabman_Rig_Ready`: reconstructed parent == real `Transform.parent` for **58/58** nodes, and
`sum(childCount) == n-1` held for every one of the 37 rigs.

**PPCLI trap worth knowing (logged in `PPCLI\ISSUES.md`):** the bridge evicts live handles under
pressure. Minting ~2550 `items` handles in one `connect multi` silently killed the earlier array
handles - only 528 of 2551 transforms came back, with later rows reporting
`handle 'h:17:1811' expired or was released`. Working around it means processing **one rig at a
time** so peak live handles stays a few hundred.

Raw dump: **`internal-docs\research\rig-census-2026-09-02.json`** - rig name -> `{managers,
instanceId, count, bones:[{name, parent, path}]}`.

### (a) Bone-name census - 37 rigs, 2551 transforms

`count` is **every transform under the rig prefab**, not only skinning bones: `EXT_*` attachment /
context nodes (skipped by `Addon.GetEquivalentBones`, `Addon.cs:1208`), lights and prop sockets are
included. `AddonsManager.GetAllRigOnlyBones()` on a live instance returned the same totals
(Human 124, Crabman 58), so this IS the set a `RigTarget` is matched against.

| Rig prefab | transforms | managers |
|---|---|---|
| `ALN_Acheron_V01_Rig_Ready` | 110 | Acheron |
| `ALN_Acidworm_Rig_Ready` | 14 | Acidworm, Poisonworm |
| `ALN_Chiron_Rig_Ready` | 123 | Chiron |
| `ALN_CorruptionNode_Rig_Ready` | 107 | CorruptionNode |
| `ALN_Crabman_Rig_Ready` | 58 | Crabman |
| `ALN_Crabman_Spawning_Pool_Rig_Ready` | 41 | SpawningPoolCrabman |
| `ALN_Egg_Explosive_Rig_Ready` | 6 | EggExplosive |
| `ALN_Egg_Facehugger_Rig_Ready` | 27 | EggFacehugger |
| `ALN_Egg_FireWorm_Rig_Ready` | 19 | EggFireWorm |
| `ALN_Egg_Swarmer_Rig_Ready` | 11 | EggSwarmer |
| `ALN_Facehugger_Rig_Ready` | 43 | Facehugger, Facehugger_DroppedTorso |
| `ALN_Fireworm_Rig_Ready` | 14 | Fireworm |
| `ALN_Fishman_Rig_Ready` | 149 | Fishman, Oilfish |
| `ALN_Mutog_Rig_Ready` | 54 | Mutog |
| `ALN_Oilcrab_Protean_Rig_Ready` | 40 | Oilcrab |
| `ALN_Queen_Rig_Ready` | 177 | Queen |
| `ALN_Siren_Rig_Ready` | 126 | Siren |
| `ALN_SNT_Hatching_Rig_Ready` | 189 | SentinelHatching |
| `ALN_SNT_Mist_Rig_Ready` | 37 | SentinelMist |
| `ALN_SNT_Terror_Rig_Ready` | 63 | SentinelTerror |
| `ALN_Swarmer_V01_Rig_Ready` | 53 | Swarmer |
| `ALN_Yugothian_Rig_Ready` | 135 | Yugothian |
| `CHR_AC_1x1_Guard_Rig_Ready` | 38 | HumanoidGuardian |
| `CHR_AC_MediumGuardian_Rig_Ready` | 38 | MediumGuardian |
| `CHR_AN_Exalted_Rig_Ready` | 118 | Exalted |
| `CHR_Human_Rig_Ready` | 124 | Human |
| `CHR_NJ_TEC_Turret_T01_V01_Rig_Ready` | 32 | NJ_TechTurret, NJ_PRCRTechTurret, PX_LaserTechTurret |
| `CHR_SY_Spider_Mine_Rig_Ready` | 54 | SpiderDrone |
| `Dummy_Rig_Ready` | 5 | Dummy |
| `PX_Security_Turret_Rig_Ready` | 26 | PX_SentryTurret |
| `VEH_KS_Death_Buggy_05_Rig_Ready` | 26 | KS_Kaos_Buggy |
| `VEH_NJ_Armadillo_Rig_Ready` | 57 (56 unique) | NJ_Armadillo |
| `VEH_PX_Scarab_V01_Rig_Ready` | 86 (85 unique) | PX_Scarab |
| `VEH_SYN_Sanator_Rig_Ready` | 62 (61 unique) | SY_Aspida |
| `WPN_PX_Injector_V01_Rig_Ready` | 20 | InjectorBomb |
| `cyborg_spider` (ContentTool) | 53 | ct_creature_morgott.demo.customcreature |
| `tiffany_ppfit` (ContentTool) | 216 | ct_creature_morgott.local.ppfit |

Confirmed unchanged from section 2: **46 managers, 42 with `Rig != null`, 4 rig-less**
(`DefaultTacCharacter`, `Dropped`, `FallDown`, `YuggothianDropped_ItemContainer`), **37 distinct
rigs** (35 shipped + 2 ContentTool), and exactly the four prefab-sharing groups already listed.

### (a) Family verdicts - the merge claim in section 2 is WRONG for every family but one

Rows in section 2 were grouped by *shape*, not by bone names. Measured, **no claimed family shares a
bone-name set**. Every intra-family pair is `OVERLAP` and the overlap is almost entirely `EXT_*`
nodes, which `Addon.GetEquivalentBones` skips anyway - i.e. **effectively disjoint for binding**.

| Family (section 2) | verdict | evidence |
|---|---|---|
| 3 Pandoran bipeds (Crabman / Fishman / Siren / Queen) | **DISJOINT for binding** | common to all four = 3 names, all `EXT_`: `EXT_AboveUnit, EXT_MainContext, EXT_VoiceContext`. Crabman 58 vs Fishman 149 shared **5** (all `EXT_`); Crabman vs Siren shared **16** (5 `EXT_` + `Head, Hips, Leg01_1_L/R, Leg01_2_L/R, Neck, Root, Shoulder_L/R, Spine`); Crabman vs Queen **3**; Fishman vs Siren **5**; Fishman vs Queen **3**; Siren vs Queen **3** |
| 4 multi-leg / beasts (Acheron / Chiron / Mutog / Facehugger / Oilcrab) | **DISJOINT for binding** | common to all five = the same 3 `EXT_` names. Largest pair overlaps: Acheron-Mutog 9, Chiron-Mutog 9, Mutog-Oilcrab 9, Acheron-Chiron 8 |
| 5 worms / swarm | **PARTLY TRUE** | `ALN_Fireworm_Rig_Ready` (14) vs `ALN_Acidworm_Rig_Ready` (14): **13 shared, and the only difference is the root GameObject's own name** - the 13 bone/`EXT_` names are IDENTICAL (`Fireworm_root, Fireworm_base, Fireworm_head, Fireworm_mouth, Fireworm_mouth_end, Fireworm_tail_01..05` + 3 `EXT_`). Fireworm/Acidworm are therefore one bone set across two prefabs. Swarmer (53) and Yugothian (135) share only the 3 `EXT_` names with them and with each other |
| 6 static structures (10 rigs) | **DISJOINT** | common to all ten = 2 names (`EXT_AboveUnit, EXT_VoiceContext`). Best pair: Egg_Explosive-Egg_Facehugger 5 |
| 7 vehicles | **DISJOINT for binding** | common to all four = 5 `EXT_` names. Armadillo(56) vs Scarab(85) shared **16**, but 6 are `EXT_`, 2 are lights (`light`, `Lights`) and the rest are generic wheel names (`FrontWeel_01_L`, `BackWeel_02_R`, ...) |
| 8 turrets / drones | **DISJOINT** | common to all three = `EXT_MainContext, EXT_VoiceContext`. The three tech turrets are one PREFAB, so they are trivially identical; SentryTurret and SpiderDrone are not |
| 9 Ancients (HumanoidGuardian vs MediumGuardian) | **DISJOINT** | both 38 transforms, shared **3**, all `EXT_` |
| 1 Human vs 2 Exalted | **DISJOINT** | 124 vs 118, shared **9**: 6 `EXT_` + `Chest, Head, Neck, Root` |

Other measured relations worth keeping:

- **Open question 4 answered.** `ALN_Crabman_Rig_Ready` (58) vs `ALN_Oilcrab_Protean_Rig_Ready` (40)
  share **34** names, including real bones - `Root, Hips, Spine, Upper_Chest, Neck, Head, Jaw2, Jow1,
  Shoulder_L/R, Arm01_1_R..Arm01_3_R, Arm04_1_L/2_L/a1_L, Leg01_1..4_L/R, Carapace,
  Carapace_L/R/_Slot, TransferBone`. So different prefabs CAN share most of a naming scheme: a
  Crabman-authored mesh would bind **partially and silently** on an Oilcrab. This is exactly the
  failure mode the prototype check exists to prevent, and it means the Doctor must compare
  **prefab-to-prefab**, never "same family, close enough".
- `CHR_Human_Rig_Ready` (124) is an **exact subset** of ContentTool's own `tiffany_ppfit` (216) -
  all 124 names present; the extra 92 are PPFit-specific (`BW_Chain_*`, ...).
- The only name shared by **all 37** rigs is `EXT_VoiceContext`.
- **Duplicate names inside one rig** (a real hazard for name-equality binding):
  ~~`ALN_Fishman_Rig_Ready` has `Fishman_upWrist_l` x2 and `Fishman_upWrist_r` x2~~ **CORRECTED:**
  the census was read case-insensitively; ordinally the rig carries `Fishman_upWrist_l` AND
  `Fishman_upWrist_L` — two distinct transforms differing only in trailing-letter case.
  `GetEquivalentBones` compares case-sensitively, so both are reachable, NOT ambiguous.
  Real duplicates: `VEH_NJ_Armadillo_Rig_Ready`, `VEH_PX_Scarab_V01_Rig_Ready` and
  `VEH_SYN_Sanator_Rig_Ready` each have `light` x2. `FirstOrDefault` makes the second unreachable.

**Consequence for the picker:** the prototype unit is the **`Rig` prefab (37 of them)**, not the
9 shape families. Section 2's grouping stays useful as a UI grouping, but must not be used to claim
two prototypes verify the same.

### (b) Menu-time bodypart load - rig YES, meshes NO

Plan `PPCLI\plans\prototype-menu-load.json` (uncommitted, PPCLI repo), mirroring
`AddonsCharacterTester.Start` (`AddonsCharacterTester.cs:44-58`): `new GameObject` ->
`DefRepository.Instantiate(managerDef, null,null,null,false)` -> `SetupRig(transform, true)` ->
`Instantiate(bodyPartDef, ...)` -> `Enumerable.Repeat<Addon>(item,1)` -> `SetupAddons(seq, false)` ->
`GetComponentsInChildren<SkinnedMeshRenderer>(true)`. `finally` calls `AddonsManager.Destroy()` and
`UnityEngine.Object.Destroy(host)`.

Reflection note: `DefRepository` has both `object Instantiate(BaseDef,GameObject,Vector3?,Quaternion?,bool)`
and `T Instantiate<T>(...)` with identical parameters, so passing `typeArgs` is refused
`code:"ambiguous"` and `sig` cannot separate them either. Omit `typeArgs` - the non-generic overload
does the same work. Arity is matched exactly, so all five arguments must be passed.

Measured at `phase:menu`, both before and after the HomeScreen level came up
(`scene:BaseScene, level:none` and `scene:HomeScreen, level:HomeScreenLevel(Clone), Playing`):

| prototype | bodypart | rig instantiated | `GetAllRigOnlyBones()` | `SetupAddons` | `IsAttachedToManager` | SkinnedMeshRenderers |
|---|---|---|---|---|---|---|
| `Human_AddonsManagerDef` | `PX_Assault_Torso_BodyPartDef` | `CHR_Human_Rig_Ready(Clone)` | **124** | `true` | `true` | **0** |
| `Crabman_AddonsManagerDef` | `Crabman_Torso_BodyPartDef` | `ALN_Crabman_Rig_Ready(Clone)` | **58** | `true` | `true` | **0** |

**There is no error text.** `SetupAddons` returns `true`, the item reports itself attached, and the
plan completes `ok:true` in ~180 ms with `cleanupRan:true`. The mechanism was confirmed directly:
`AddonDef.SkinData.GetAllVariants()` returns a sequence of **count 1 whose single element is null**
(`items` -> `returned:0`) for both `PX_Assault_Torso_BodyPartDef`
(`ArmourBodyPartSkinDataDef`, `SkinsToRig=true`) and `Crabman_Torso_BodyPartDef`
(`SimpleBodyPartSkinDataDef`, `SkinsToRig=true`). That is `AddonSkinDataBase.GetPrefabAsset`
returning null because `assetReference.Asset` is not resident (`AddonSkinDataBase.cs:18-29`).

**A/B control.** The identical plan run while a tactical mission was loaded
(`scene:ALN_PLT_Nest_48x48_A`) on the same Human prototype produced **7 SkinnedMeshRenderers** -
first one `Head_Afro1_M_V01`, mesh `Head_Afro1_M_V01`, **21 bones against the rig's 124**. So the
code path is correct and the only variable is addressable residency. It also shows that a slot's
`smr.bones` is a small SUBSET of the rig, not the whole rig - a `RigTarget` from a prototype slot
must be compared per-slot.

**This settles open question 3.** A menu-time prototype gives the SKELETON for free and no meshes.
Slice 1's "slot visual unavailable" path is therefore not an edge case - at the menu it is the
*normal* state for every Addressable-gated slot, and the picker must be usable with bones only.

### (c) DLC prototype presence - no DLC-only manager exists

All 46 `AddonsManagerDef` on this install (44 shipped + ContentTool's 2), verbatim:

`Acheron`, `Acidworm`, `Chiron`, `CorruptionNode`, `Crabman`, `DefaultTacCharacter`, `Dropped`,
`Dummy`, `EggExplosive`, `EggFacehugger`, `EggFireWorm`, `EggSwarmer`, `Exalted`, `Facehugger`,
`Facehugger_DroppedTorso`, `FallDown`, `Fireworm`, `Fishman`, `Human`, `HumanoidGuardian`,
`InjectorBomb`, `KS_Kaos_Buggy`, `MediumGuardian`, `Mutog`, `NJ_Armadillo`, `NJ_PRCRTechTurret`,
`NJ_TechTurret`, `Oilcrab`, `Oilfish`, `Poisonworm`, `PX_LaserTechTurret`, `PX_Scarab`,
`PX_SentryTurret`, `Queen`, `SentinelHatching`, `SentinelMist`, `SentinelTerror`, `Siren`,
`SpawningPoolCrabman`, `SpiderDrone`, `Swarmer`, `SY_Aspida`, `Yugothian`,
`YuggothianDropped_ItemContainer` (+ `ct_creature_morgott.demo.customcreature`,
`ct_creature_morgott.local.ppfit`), each `*_AddonsManagerDef`.

- **Umbra is not a unit** - it is a mutation variation of existing ones.
  `Crabman43_Umbra_AlienMutationVariationDef.GetAddonsMangerDef()` -> `Crabman_AddonsManagerDef`;
  `Fishman19_Umbra_AlienMutationVariationDef` -> `Fishman_AddonsManagerDef` (LIVE).
- **Hoplite is an Ancient.** `AC_Hoplite1_CharacterTemplateDef` and `AC_Hoplite2_CharacterTemplateDef`
  both -> `HumanoidGuardian_AddonsManagerDef` (LIVE).
- **Cyclops has no character def at all.** `find {query:"Cyclops", type:"TacCharacterDef"}` -> 0. The
  16 `Cyclops*` rows are abilities, a status, geoscape events and hint defs, most of them TFTV's
  (`CyclopsBeamTFTVShootAbility`). The five Ancients units that DO exist are
  `HumanoidGuardian_Driller/Shielder` and `MediumGuardian_LivingCrystal/Orichalcum/ProteanMutane`,
  on the two Guardian managers.
- **DLC content is present**: `DLC1..DLC5_GeoscapeEventDatabase` all resolve, and the DLC-only rigs
  are already in the list (`ALN_Acheron_*` = DLC4, `ALN_CorruptionNode_*`/`ALN_Egg_*`/`ALN_SNT_*` =
  DLC3/5, `VEH_KS_Death_Buggy_*` = Kaos). There is no `DLCDef` type in the repository to query, so
  entitlement was read off content, not off a flag.

**Conclusion: the prototype list does not grow with DLC.** 37 rigs is the whole surface, and
`D:\Steam\steamapps\common\Phoenix Point` was deliberately not touched.

### (d) Animation - the controller is NOT per prototype

| character def | `GetAnimActionDef()` | manager / rig | `Animator.runtimeAnimatorController` | clips (total / unique) | `AnimActions` | default action / reaction clip |
|---|---|---|---|---|---|---|
| `PX_Assault_TacCharacterDef` | `Soldier_Utka_AnimActionsDef` | Human / `CHR_Human_Rig_Ready` | **`HumanoidAnimatorLOC`** | 73 / 69 | 177 | `HL_ActionPlaceholder` / `HL_ReactionPlaceholder` |
| `Crabman_Gunner_TacCharacterDef` | `Crabman_AnimActionsDef` | Crabman / `ALN_Crabman_Rig_Ready` | **`HumanoidAnimatorLOC`** | 73 / 69 | 0 | (none) |
| `Mutog_Agile_TacCharacterDef` | `Mutog_AnimActionsDef` | Mutog / `ALN_Mutog_Rig_Ready` | **`MidMonsterAnimator`** | 60 / 45 | 22 | `HL_ActionPlaceholder` / `HL_ReactionPlaceholder` |

First 10 clip names off `HumanoidAnimatorLOC` (Human AND Crabman): `HL_StepOut_AR, HL_StepIn_AR,
HR_StepOut_AR, HR_StepIn_AR, FF_EndShot_AR, FF_ShotLoop_AR, FF_Aim_Loop_AR, HL_Death_AR,
HL_ActionPlaceholder, LL_IdleAlert_AR`.

First 10 off `MidMonsterAnimator` (Mutog): `Chiron_goo_end, Chiron_goo_loop,
Chiron_goo_overwatch_wait, Chiron_death, HL_ActionPlaceholder, Chiron_alert_loop, Chiron_Idle_loop,
NO_UndeployShieldPlaceholder, NO_IdleShieldPlaceholder, NO_DeployShieldPlaceholder`.

Two facts slice 2 has to build on:

1. **`AnimatorController` is shared across prototypes with completely different skeletons.** The
   Human and Crabman rigs carry the *same* `HumanoidAnimatorLOC`, and Mutog carries
   `MidMonsterAnimator` whose clips are named `Chiron_*`. So "prototype -> clip list" resolves to
   "controller -> clip list", and the controller is a coarser key than the rig. A clip list alone
   does not identify a prototype.
2. **The per-prototype variation lives in the anim-actions def, and it can be empty.**
   `Crabman_AnimActionsDef` has `AnimActions.Count == 0`, no `DefaultActionClip` and no
   `DefaultReactionClip`; `Soldier_Utka_AnimActionsDef` has 177. A preview that only reads
   `TacActorAnimActionsDef` will show nothing for Crabman - it must fall back to the controller's
   own `animationClips`.

Clip names are duplicated inside a controller (73 entries, 69 distinct for `HumanoidAnimatorLOC`;
60 / 45 for `MidMonsterAnimator`), so dedupe before showing a list.
