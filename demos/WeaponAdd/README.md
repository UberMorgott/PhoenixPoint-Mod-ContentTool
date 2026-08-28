# Demo mod — a weapon Phoenix Point does not ship, waiting in the base on day one

**A content mod is a FOLDER of assets — and when the thing you are adding is a *weapon*, the assets
are only half of it. The other half is a def.**

> **This is a SEPARATE MOD.** It installs as `Mods\WeaponAdd\` and the mod manager lists it as
> **ContentTool Demo: Weapon Add**. It requires the **ContentTool** mod — `meta.json` declares
> `"Dependencies": [ "com.morgott.ContentTool" ]`.
>
> **Nothing is written into your game files.** Unlike its sibling `WeaponMesh`, this demo does not
> patch a shipped bundle: the model is served out of *this mod's own* bundle under *its own* catalog
> key, and the weapon exists only while the mod is enabled. Switching the mod off removes it.
> Disabling the mod un-publishes its key on the spot; there is no catalog.json edit to revert.

Meet the **Vulture PDW**, the **Vulture AR** and the **Vulture Sidearm** — three `WeaponDef`s the
game does not ship, sitting in the Phoenix base's inventory the moment a new campaign begins, ten of
each with ten clips apiece. Equip them, holster them, take them on a mission, overwatch, reload and
shoot: each one fires with a muzzle flash and puts the right thing downrange, because each inherits
the effects of the **shipped weapon of its own class** that it was cloned from.

**The mechanism is not in this mod.** Cloning a def, building its view, loading its icon, pointing a
skin at a published prefab, fitting the four `EXT_` sockets, deep-copying the damage payload and
seeding starting storage are the same for every weapon anyone will ever add, so they live in the
ContentTool engine — `src\Tactical\WeaponBuild.cs`, the `CreatureBuild.cs` analogue. This mod is one
call and a list. **Read `ppcontent.json`**, not this file's `src\`.

```json
{ "id": "Morgott_VultureAR_WeaponDef", "name": "Vulture AR",
  "clone": "PX_AssaultRifle_WeaponDef", "guid": "…4b11",
  "damage": "45", "spread": "2.4", "count": "10", "clips": "10" }
```

> **Two wear their own art, one wears its donor's** — measured 2026-08-28, see *The models, and how
> you know they are worn* below. `"model"` is **optional**, and the Vulture Sidearm is deliberately
> the case with none: it keeps the SkinData of the weapon it cloned and looks like that gun, which is
> a perfectly honest state, and it means one demo shows both answers side by side.

Its sibling `WeaponMesh` is the other half of the pair: that one **REPLACES** a shipped weapon's
art — bundling a patched copy into ContentTool's own AppData folder and redirecting the game's live
Addressables at it, never into the install; this one **ADDS** a weapon and writes nothing. One capability per mod, on purpose.

```text
WeaponAdd\
  ppcontent.json                      THE INTERESTING FILE: two "publish" rows + a "weapons" array
  meta.json                           the mod manifest; AssemblyName = WeaponAdd.dll
  Content\Models\sniper.glb           the geometry, fitted to the shipped sniper's own box
  Content\Models\ar181.glb            the Sketchfab download, UNMODIFIED - see SOURCES.md.
                                      Multi-mesh; MeshMerge joins it at bake. It is worn.
  Content\Textures\sniper.png         1024 - named after the model, which is what binds it
  Icons\sniper_inv.png                450x450 - the inventory cell, rendered FROM sniper.glb
  src\WeaponAddMain.cs                ONE CALL. The mechanism is ContentTool's WeaponBuild
  tools\fit_sniper.py                 orientation, scale AND the three EXT_ socket positions
  tools\render_icon.py                the icon, rasterised offline from the shipped .glb
  tools\downscale_atlas.ps1           2048 -> 1024, and why
  tools\check_project.py              reads ppcontent.json the way the TOOL reads it
  tools\source\                       the CC0 sniper kit as downloaded, so every step re-runs
  SOURCES.md                          CC0 attribution, kept with the files
```

## Install

```text
ct_project WeaponAdd                # bake this mod's own bundle from its Content\ folder
ct_catalog apply WeaponAdd          # publish the key LIVE - no restart
ct_catalog verify
ct_catalog status                   # what is published right now
```

Then **start a NEW campaign** — the rifle is added to the STARTING storage, so an existing save will
not have it. `Player.log` carries one `ct_weapon PASS '<name>' (<id>) cloned from …` line per weapon
and then one `ct_weapon PASS '<prefab>' loaded from key … ; four EXT_ sockets derived from
<donor>'s own box` line per model (measured 2026-08-28).

## The four things a new weapon actually needs

### 1. A def — cloned, not written

```csharp
WeaponDef def = (WeaponDef)repo.CreateDef(WeaponGuid, source, null);   // source = PX_LaserPDW_WeaponDef
```

**The clone source is the weapon CLASS, and that is not a detail.** Phoenix Point picks a soldier's
hold pose, aim stance and firing animation set off the weapon's tags — `PDWItem_TagDef`,
`GunWeapon_TagDef` — so an SMG cloned from a sniper rifle is *held and fired like a sniper rifle*.
Pair the silhouette with the shipped class and the animation problem never exists. This demo clones
`PX_LaserPDW_WeaponDef` (`extracted\GameData\defs\WeaponDef\PX_LaserPDW_WeaponDef.json`): two-handed
(`:128`), 48 charges, a four-round auto burst, 25% AP per shot.

`DefRepository.CreateDef(id, original, type)` (`DefRepository.cs`, the same factory
`Helper.CreateDefFromClone` calls in TFTV — `refs\TFTV-src\TFTV\Helper.cs:149-152`) does
`Object.Instantiate(original)`, stamps the Guid you give it and files the result in
`DefRepositoryDef.AllDefs`. Cloning matters more than it sounds: `PX_LaserPDW_WeaponDef` carries
**four abilities, a damage payload with two damage keywords, a required slot bind, a holster slot,
compatible ammunition, a Wwise switch, a firing event, six game tags and a manufacture cost**
(`extracted\GameData\defs\WeaponDef\PX_LaserPDW_WeaponDef.json`). Typing that out is thirty
chances to be subtly wrong; cloning it is one line that cannot be.

> The sibling demo learned the other half of this the hard way: `ScriptableObject.CreateInstance`
> leaves `Guid` and `ResourcePath` **null**, which is invisible until another mod reads them —
> TFTV's SkipMovies postfix did, and threw (commit `8f86b38`). Both are set explicitly here.

The Guids are **constants, not `Guid.NewGuid()`**. A save stores an item by its def; a def whose
identity changes every launch is a save that stops loading.

### 2. A model, served from the mod's own bundle

`ppcontent.json` publishes **two** keys — `…4b60` → `models/sniper` and `…4b61` → `models/ar181`.
The third weapon publishes nothing, because it declares no `"model"`. One row looks like this:

```json
{ "key": "c7a9f1d24b6e4a3c8f5b7d1e9a2c4b60", "asset": "models/sniper",
  "type": "GameObject", "deps": "defaultlocalgroup_unitybuiltinshaders.bundle" }
```

- **`asset`** is what `ProjectBake` really writes — a model becomes `models/<stem>`
  (`ProjectBake.cs:164`). `ct_catalog apply` refuses a key whose asset is not in the bundle rather
  than letting it resolve to null forever (`Route7.cs:313`), and `tools\check_project.py` catches it
  before that, offline.
- **`deps`** is not decoration. Every model ContentTool bakes gets its own Material whose shader is
  the builtin `Standard` through an **external** PPtr (`ProjectBake.cs:96`), which only resolves
  while `defaultlocalgroup_unitybuiltinshaders.bundle` is mounted. Without the dep the gun renders
  with `Hidden/InternalErrorShader`.
- **The key is 32 lowercase hex digits on purpose.** That is the exact shape Phoenix Point's own
  `AssetReference`s carry (the shipped `E_SkinData [PX_AssaultRifle_WeaponDef].DefaultPrefab
  .m_AssetGUID` is `604561be7de7cb6479711b4e31bdc02d`), and `AddonSkinDataBase.GetPrefabAsset`
  checks `RuntimeKeyIsValid()` before anything else (`AddonSkinDataBase.cs:23`). Verified not to
  collide: the shipped `catalog.json`'s decoded `m_KeyDataString` does not contain it — with the
  known guid above found in the same pass as a positive control.

The def side is then one field:

```csharp
skin.DefaultPrefab = new AssetReferenceGameObject(PrefabKey);
```

**And the load is started explicitly.** `AddonSkinDataBase.GetPrefabAsset` returns
`assetReference.Asset` and *never loads anything* (`AddonSkinDataBase.cs:19-29`); the engine fills
that in through `AssetsManager.AcquireDependenciesAsync`, which reflects over a def's
`AssetReference` fields (`AssetsManager.cs:82`, `:188`). Whether a def created after boot is ever
handed to that pass is not something this mod can promise, so it loads its own prefab once and never
releases it.

### 3. Four empty transforms — the part that would silently break the gun

Phoenix Point finds a weapon's muzzle, sights and ejection port **by name**, off the weapon's visual
root. Every one of them breaks something specific:

| def field | value | what breaks without it |
|---|---|---|
| `DamagePayload.ProjectileOrigin` | `EXT_ShootPoint` | `TacticalLevelController.cs:1547-1549` logs *"Can't find … projectile origin"*, then `Weapon.cs:425` indexes an empty array. **The muzzle flash also spawns here** (`Weapon.SpawnFlash`, `Weapon.cs:389-397`) |
| `EquipmentDef.AimPoint` | `EXT_AimPoint` | aiming has no origin |
| `EquipmentDef.AimTransform` | `EXT_AimIKPoint` | `TacticalActor.cs:2028` hands the aim IK solver a null transform |
| `EquipmentVisualEffectsDef.ShellEjectionPoint` | `EXT_ShellPoint` | `Weapon.SpawnShell` (`Weapon.cs:408-421`) logs *"has a shell prefab but invalid shell ejection point"* **on every shot** and drops no brass — **moot since the clone source became the laser PDW**, whose effects def carries no `Shell` at all (measured: `shell=none`). The socket is still fitted, because it costs one line and the next clone source may want it |

A prefab baked by ContentTool is a root plus **one** mesh child (`PrefabFields.Build`), so there is
nowhere in the `.glb` to put them. They are added once, to the loaded prefab asset, at positions
`tools\fit_sniper.py` **derives** from the fitted mesh's own box — muzzle at the front face, sights
62% back along it, both on the barrel line at 70% of the box height, ejection port on the `+X` face
beside the sights:

```text
EXT_ShootPoint              (0.00435, 0.06109, 0.76880)
EXT_AimPoint/EXT_AimIKPoint (0.00435, 0.06109, 0.41911)
EXT_ShellPoint              (0.02021, 0.06109, 0.41911)
```

Note what this means for the next section: **the socket IS the VFX placement.** Get
`EXT_ShootPoint` wrong and the muzzle flash appears in the middle of the stock.

### 4. A place in the world — one array, four defs

```csharp
foreach (GameDifficultyLevelDef diff in repo.GetAllDefs<GameDifficultyLevelDef>())
    diff.StartingStorage = <the shipped array + new ItemUnit(def, 2)>;
```

`GameDifficultyLevelDef.StartingStorage` (`GameDifficultyLevelDef.cs:43`) is the `ItemUnit[]` a new
campaign fills the Phoenix base from — on Standard it is 20 AR clips, 6 autocannon clips, 5 sniper
clips, 5 pistol clips, 6 grenades and 6 medkits. Appending to all four difficulty defs puts the gun
in the player's hands before they have done anything at all. **Two entries, not one:** its
ammunition is `PX_LaserPDW_AmmoClip_ItemDef` (carried over by the clone in
`ItemDef.CompatibleAmmunition`, `ItemDef.cs:47`) and — unlike the sniper clip this demo used to rely
on — it is *not* already in that array, so ten spare clips are added beside the weapon. Without
them the gun arrives with one magazine and no way to reload.

### The three asks the clone answers for free, and the two it does not

| ask | where it comes from | lines |
|---|---|---|
| the energy-weapon **firing report** | `TacticalItemDef.MainSwitch` (`TacticalItemDef.cs:74`) — the Wwise *switch*, not the event. Every gun in the game shares one `ShootShot_EventDef`; `SoundEventHandlerDef.cs:109-111` → `:168-170` sets the used item's switch on the emitter, and that switch is the whole difference between a rifle crack and an energy discharge | 0 |
| the **tracer** | `DamagePayload.ProjectileVisuals` (`DamagePayload.cs:89`) → `E_Projectile [PX_LaserPDW_WeaponDef]` | 0 |
| the **muzzle flash** | `EquipmentDef.VisualEffects` (`EquipmentDef.cs:26`) | 0 |
| **harder hitting** | the `Damage_DamageKeywordDataDef` pair's `Value`, 40 → **60** (+50%). Not `DamagePayload.DamageValue`, which the shipped def leaves at 3: `DamagePayload.KeywordFlow` (`DamagePayload.cs:103`) switches the whole flow onto the keyword list once it is non-empty | 3 |
| **wider spread** | `WeaponDef.SpreadDegrees` (`WeaponDef.cs:24`), 2.0 → **3.0** (+50%, still under the shipped shotgun's 4.0). Larger = wider cone; `Weapon.cs:226` turns it straight into radians | 1 |

Both numbers are **deltas against the weapon the player already owns**, so the trade is legible
rather than magic. The spread is not free: `WeaponDef.EffectiveRange` is *derived* from it
(`WeaponDef.cs:91`, `1/SpreadDegrees * 41`), so the displayed effective range drops from 20 tiles to
13. That is the point of the weapon.

**`DamagePayload` is deep-copied before either number is written.** It is a plain `[Serializable]`
*class* (`DamagePayload.cs:21-22`) — not a def, not a struct — so whether `CreateDef`'s
`Object.Instantiate` handed the clone its own instance is an assumption, and if it is wrong the mod
permanently re-tunes the player's *shipped* laser PDW for the session. An unconditional
`MemberwiseClone` of the payload, its keyword list and each pair costs six lines and removes the
question; the `A1-def PASS` line then reports the source's own numbers back, so a regression is
visible in `Player.log` rather than silent.

Neither research nor manufacture is unlocked for it. That is deliberate: this demo is about a weapon
*existing*, and a research tree is a different demo.

## Muzzle flash and the rest of the VFX — where the binding lives

"How do I make my gun shoot properly" is the question a modder actually has, so here is the whole
answer.

**It is one def field.** `EquipmentDef.VisualEffects` (`EquipmentDef.cs:26`) is an
`EquipmentVisualEffectsDef`, and that def has exactly four members
(`EquipmentVisualEffectsDef.cs:9-15`):

| member | type | spawned by | where |
|---|---|---|---|
| `Flash` | `GameObject` | `Weapon.SpawnFlash`, `Weapon.cs:389-397` | **at the projectile origin**, rotated to the shot direction — i.e. at `EXT_ShootPoint` |
| `Smoke` | `GameObject` | `Weapon.SpawnSmoke`, `Weapon.cs:399-406` | parented to the projectile-origin transform (the sniper ships none) |
| `Shell` | `GameObject` | `Weapon.SpawnShell`, `Weapon.cs:408-421` | at `FindTransform(ShellEjectionPoint)` |
| `ShellEjectionPoint` | `string` | — | defaults to `"EXT_ShellPoint"`; `WeaponDef.cs:117-119` refuses a name that does not start with `EXT_` |

The **tracer and impact** are somewhere else and are easy to miss:
`WeaponDef.DamagePayload.ProjectileVisuals`, a `ProjectileDef` — for the Phoenix sniper that is
`E_Projectile [PX_LaserPDW_WeaponDef]`.

**The choice this demo made: REUSE, and it cost zero lines.** Both fields ride along with the clone,
so the Vulture fires with the Phoenix laser PDW's own muzzle flash, its own brass and its own
tracer — correct scale, correct timing, correct look, no authored particle system. That is the
honest answer for almost every new gun, and it is why the def is cloned rather than built.

Swapping to a different shipped weapon's look is one assignment — the laser rifle's flash on a
ballistic gun is a one-liner:

```csharp
def.VisualEffects = laserRifleDef.VisualEffects;                 // muzzle flash, smoke, brass
def.DamagePayload.ProjectileVisuals = laserRifleDef.DamagePayload.ProjectileVisuals;   // tracer
```

Authoring a *new* effect means a particle-system prefab in the mod's own bundle, published under its
own key exactly like the model above, then assigned to `Flash`. Worth it when no shipped effect can
express the weapon; not worth it here, and saying so is the point.

**It is reported, not assumed.** At mod-enable the log line names what is actually bound:

```text
ct_weapon PASS 'Vulture PDW' (Morgott_VulturePDW_WeaponDef) cloned from PX_LaserPDW_WeaponDef;
        icon ok; prefab load started for key c7a9f1d24b6e4a3c8f5b7d1e9a2c4b60;
        tuning dmg 40->60 spread 2->3 range 20->13 (source intact); anims named beside ...;
        vfx 'E_VisualEffects [PX_LaserPDW_WeaponDef]' flash=VFX_WPN_PX_LaserPDW_MuzzleFlash
        shell=none projectile=E_ProjectileVisuals [PX_LaserPDW_WeaponDef]
ct_weapon fit 'Morgott_VulturePDW_WeaponDef' into PX_LaserPDW_WeaponDef's own box: long axis Z -> +Z
        (rotate 0), scale 0.5376, offset -0.002,0.013,-0.054;
        donor mesh 'WPN_PX_Laser_PDW_V01_mesh' centre (0.0, 0.0, 0.1) extent (0.0, 0.1, 0.2)
ct_weapon PASS 'sniper' loaded from key c7a9f1d24b6e4a3c8f5b7d1e9a2c4b60
        for 'Morgott_VulturePDW_WeaponDef'; four EXT_ sockets derived from
        PX_LaserPDW_WeaponDef's own box shoot=(0.0, 0.1, 0.8) aim=(0.0, 0.1, 0.4)
```

Two of each, one set per model — the PDW names `sniper` and the AR `ar181`, both fitted into their
own donor's box. The Sidearm has no model and therefore no sockets to derive: it uses the ones on
the donor prefab it inherited.

## What "turn-key" actually consists of

Everything below is inherited by cloning `PX_LaserPDW_WeaponDef`, and every one of them is a
thing a hand-written def gets wrong:

| what | value it inherits |
|---|---|
| equip slot | `RequiredSlotBinds` → `Human_GunPoint_SlotDef`; two-handed (`HandsToUse` 2) |
| holster | `HolsterSlotDef` → `Human_Holster_SlotDef` — it goes on the soldier's back when not selected |
| abilities | `Weapon_ShootAbilityDef`, `Overwatch_AbilityDef`, `Reload_AbilityDef`, `DropItem_AbilityDef` |
| ammo | `CompatibleAmmunition` → `PX_LaserPDW_AmmoClip_ItemDef`, `ChargesMax` 48 — the demo adds 10 clips to `StartingStorage`, because none are there by default |
| damage | `AutoFireShotCount` 4, `APToUsePerc` 25 — the burst and the AP cost are the shipped ones; only the per-shot damage is changed |
| tags | `PDWItem_TagDef`, `Technician_ClassTagDef`, `GunWeapon_TagDef` … — which is what picks the soldier's aiming and firing animation set |
| sound | `ShootingEvent` → `ShootShot_EventDef`, **plus the Wwise `MainSwitch` that decides which weapon that shared event sounds like** |
| visuals | `VisualEffects` and `ProjectileVisuals`, above |

So: equip it, holster it, overwatch with it, reload it, drop it, and shoot it. The only things it
does **not** do are listed under *Honest limits*.

## Scale and orientation

Same problem `WeaponMesh` solves, with one difference that matters: nothing here replaces a shipped
mesh, so the shipped box is not a constraint the engine enforces — it is the **specification**. A
weapon is an Addon parented to a named attachment transform on the rig
(`AddonDef.ProvidedSlotBind.AttachmentPointName`, `Addon.cs:49-53`, `AddonsManager.cs:120`), so a
brand-new prefab lands in the hand at whatever coordinates its mesh happens to carry. Copying the
shipped sniper's own `m_LocalAABB` is the only way to arrive there at the right size and the right
way round.

```text
$ python tools\fit_sniper.py
source  bbox min ['-1.2769', '-0.1204', '-0.0296'] max ['0.4408', '0.2095', '0.0296']
per-axis ratios  x=1.2753 y=0.6884 z=0.5358  ->  uniform scale 0.535756 (smallest wins)
translate        ['0.004352', '0.001888', '0.084718']
unity bbox       min ['-0.0115', '-0.0626', '-0.1514'] max ['0.0202', '0.1141', '0.7688']
shipped bbox     min ['-0.0334', '-0.0878', '-0.1514'] max ['0.0421', '0.1393', '0.7688']
OK  ...\Content\Models\sniper.glb  8249 verts / 8728 tris / 317428 bytes
```

Target box measured with UnityPy off `px_equipment_assets_all.bundle`, Mesh
`WPN_PX_RG_Sniper_Rifle_T01_V01` (7676 verts): centre `(0.00435, 0.02574, 0.30869)`, extent
`(0.03774, 0.11355, 0.46011)` — 0.920 m of rifle down **+Z**, 0.227 m on **+Y**. The script asserts
the fitted mesh stays inside that box, that the barrel really lands on +Z, and that the uniform
scale is positive (a negative one is a point reflection: it passes a bounding-box check and ships an
inside-out gun).

## The inventory icon

Same mechanism as `WeaponMesh`, and worth restating because it is the thing people miss: the
inventory cell draws a **pre-rendered Sprite off the def**, never a live render of the model —
`UIInventorySlot.cs:445` reads `_item.ItemDef.ViewElementDef.InventoryIcon`. A new weapon that does
not set it inherits whatever its clone source had, so this one would quietly show the Phoenix sniper
rifle's picture.

`tools\render_icon.py` rasterises `Content\Models\sniper.glb` — orthographic, z-buffered, flat-shaded
off the file's own normals, 3x supersampled, stdlib only — so the cell and the hand are the same
geometry by construction. 450x450, which is what the shipped weapon icons measure.

## The models, and how you know they are worn

`Content\Models\` holds a fitted CC0 sniper and one Sketchfab download (licences in `SOURCES.md`).
The download is multi-mesh, and the demo once could **not** use it as it was, because the reader had
a creature rule that refused it. That is history: `MeshMerge` is wired and the Vulture AR wears it.

| file | what it actually is | what it bakes to |
|---|---|---|
| `ar-181.glb` (8 408 796 B) | **14 meshes**, 5778 v / 4582 t, **3 materials**, 10 embedded PNGs, UVs present | one `ar181_mesh`, **5778 verts, 3 submeshes** — one per distinct material |

**The Vulture Sidearm has no model, on purpose.** The `.glb` that used to fill that row was deleted
on 2026-08-28 — its licence forbids redistributing it as a stand-alone file, and it was Games
Workshop fan art besides (`SOURCES.md`). A weapon with no `"model"` keeps the `SimpleSkinDataDef` of
the weapon it cloned and wears the donor's art, which is a **legitimate answer**, not a
half-finished one — and having it beside two weapons that do wear their own is what makes the
difference legible.

**Measured live, 2026-08-28** (`D:\PP-Instance2`, PPBridge `build=e0ccf41f`, one launch, menu). The
probe is the def's own binding, not the catalog: `WeaponDef.SkinData.DefaultPrefab` → the engine's
own `AddonSkinDataBase.GetPrefabAsset` → `MeshFilter.sharedMesh`. A key that resolves is not a
weapon that wears it, so the vertex count is the discriminator, and each donor was read in the
**same run** as its control:

| weapon | `SkinData` | `DefaultPrefab` key | prefab | mesh | verts | donor, same run |
|---|---|---|---|---|---|---|
| `Morgott_VulturePDW` | `E_SkinData [Morgott_VulturePDW_WeaponDef]` | `…4b60` | `sniper` | `sniper_mesh` | **8249** (1 sub) | `PX_LaserPDW` key `b959e705…` → `WPN_PX_Laser_PDW_V01_mesh` **3305** |
| `Morgott_VultureAR` | `E_SkinData [Morgott_VultureAR_WeaponDef]` | `…4b61` | `ar181` | `ar181_mesh` | **5778** (3 subs) | `PX_AssaultRifle` key `604561be…` → `WPN_PX_RG_Assault_Rifle_T01_V01` **5554** |
| `Morgott_VultureSidearm` | **`E_SkinData [SY_LaserPistol_WeaponDef]`** — the donor's own | `87db8622…` | `WPN_SY_Laser_Pistol_V01_Ready` | `WPN_SY_Laser_Pistol_V01_mesh` | **2750** | it *is* the donor row |

The last row is the point of the demo, re-measured **2026-08-28** after the Tau `.glb` was deleted:
the Sidearm's `SkinData` is not a def of ours at all but `E_SkinData [SY_LaserPistol_WeaponDef]`
(guid `cf182892-be3d-1eb1-f190-64c347a53fdb`, a **shipped** def — positive `instanceId`), and its
`DefaultPrefab.AssetGUID` is the shipped `87db86228bf665d4b9ed60caa3770608`, not a key of ours. That
is exactly what `"no model"` means, read off the live engine rather than off a log line.

The other two each carry their **own** `SimpleSkinDataDef` pointing at their **own** published key.
Each of those prefabs has **6 transforms** (root + mesh + the four `EXT_` sockets)
and a material on shader **`Standard`**, not `Hidden/InternalErrorShader` — so the `deps` row above
is doing its job too.

> The AR donor's 5554 is not its shipped number. `WeaponMesh` was enabled in the same stack and
> replaces exactly that mesh (shipped 5771). It is still a valid control here — the question is
> whether the Vulture AR wears the *AssaultRifle's* mesh, and 5778 ≠ 5554 either way — but do not
> read 5554 as the vanilla value.

**This was our gap, not a Blender job, and it has been closed.** The multi-mesh tolerance added for
creatures picks *the mesh a skin drives* — a rule derived from a rigged spider. **A gun has no rig
and never should have one**, so that rule read a perfectly well-formed static model as "no armature
drives any of them" and refused it. Right for creatures, wrong for props. Telling a modder to go and
join meshes in Blender is the tool failing at its own job.

`src\Bake\MeshMerge.cs` is the prop half: it merges a static model's pieces into **one mesh whose
submeshes are its distinct materials**, so AR-181's fourteen pieces become **3 submeshes** — the
shape gate `U11` asserts, and the table above is that same number read back off the live engine.

**Submeshes, not atlasing, and the game's own content decided it:** Unity binds submesh `i` to
`m_Materials[i]`, and across `extracted\GameData\prefabs` **2533 of 3463** shipped renderer material
arrays hold more than one material against 930 holding exactly one. Multi-material renderers are the
*normal* case in Phoenix Point, so keeping the parts as submeshes is what the engine already does.
Atlasing would repack someone else's textures and rewrite their UVs — lossy, far more code, and it
discards what the engine wanted anyway.

The merge still **refuses loudly** rather than shipping a mangled gun: a piece carrying a skin (the
creature path owns that case), or a model where only *some* pieces have UVs, is named and rejected.

> **Wired, and confirmed in game.** The reader no longer refuses a rigless multi-mesh prop and the
> bake writes one material per submesh. Both `.glb`s bake, both keys publish, and both weapons wear
> the result — the measurement is the table above.

## Honest limits

- **New campaigns only.** `StartingStorage` is read when a campaign is created. An existing save has
  already been filled.
- **No research, no manufacture, no vendor.** The gun cannot be built or bought; there are ten of
  them and ten clips, and that is that.
- **Everything except damage and spread is the Phoenix laser PDW's.** Deliberately: this demo is
  about the machinery of *adding* a weapon, and two changed numbers are enough to show where the
  knobs are without making it hard to tell whether the machinery worked.
- **Every projectile is a shipped one, and a custom one is blocked on the bake — measured.** The
  *seam* needs no engine work and the shipped data proves it: of the **168** shipped `ProjectileDef`s,
  **129** set `Prefab` (the flying bolt, `ObjectDef.cs:14`) and **112** set `HitEffect.EffectPrefab`
  (the impact, `HitEffect.cs:16`) — both plain `GameObject`s a baked bundle can serve. What is
  missing is what goes *in* them:
  - **Emissive / additive material — a small, bounded gap.** `AddMaterial` already carries a colour
    and a float map, so `_EmissionColor` and the blend floats *could* be written today. But Unity's
    Standard shader only lights emission when `_EMISSION` is in **`m_ShaderKeywords`**, and only
    blends additively with **`m_CustomRenderQueue`** — and `MaterialFields` writes neither, only
    `m_TexEnvs`/`m_Floats`/`m_Colors` (`MaterialFields.cs:74/86/97`). A cyan bolt mesh bakes fine
    today; it just will not *glow*.
  - **Scattering sparks — a real gap.** The bake's complete class set is thirteen types
    (`GameObject`, `Transform`, `MeshFilter`, `MeshRenderer`, `SkinnedMeshRenderer`, `Mesh`,
    `Material`, `Texture2D`, `TextAsset`, `Animator`, `AnimatorOverrideController`, `AnimationClip`,
    `AssetBundle`). **`ParticleSystem` is not among them**, so a particle impact cannot be
    serialised at all. Sparks would have to be an animated mesh, or the class support is its own
    slice.
- **There is no beam in Phoenix Point, so the Sidearm fires a BOLT.** `ProjectileDef.LineTrail` is
  the field a continuous beam would use, and **0 of the 168 shipped projectile defs set it** — every
  laser in the game, including the Synedrion ones, throws a travelling bolt. So the "fire beam" this
  demo was asked for is a bolt, and saying otherwise in the docs would be describing a game that does
  not exist. A real beam is not a reuse job; it is the same custom-visual work as the plasma bolt and
  hits the same two gaps above.
- **Fire and burning, by contrast, are pure data and need no engine work.** `NJ_FlameThrower_WeaponDef`
  sets `DamagePayload.DamageType` to `Fire_StandardDamageTypeEffectDef` and carries **two** keyword
  pairs — `Damage_DamageKeywordDataDef` 80 **and `Burning_DamageKeywordEffectorDef` 40**. Igniting a
  target is that second pair; it is three lines in the manifest's own damage list the day
  `WeaponBuild` accepts a keyword map.
- **The material is Unity's `Standard` with one texture.** No normal map, no ORM — see `SOURCES.md`
  for why the kit's other two maps are not shipped rather than shipped dead.
