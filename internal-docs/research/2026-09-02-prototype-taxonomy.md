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
