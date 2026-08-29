# Demo mod -- three weapons Phoenix Point does not ship, waiting in the base on day one

**A content mod is a FOLDER of assets -- and when the thing you are adding is a *weapon*, the assets
are only half of it. The other half is a def.**

> **This is a SEPARATE MOD.** It installs as `Mods\WeaponAdd\` and the mod manager lists it as
> **ContentTool Demo: Weapon Add**. It requires the **ContentTool** mod -- `meta.json` declares
> `"Dependencies": [ "com.morgott.ContentTool" ]`.
>
> **Nothing is written into your game files.** Unlike its sibling `WeaponMesh`, this demo does not
> patch a shipped bundle: the models are served out of *this mod's own* bundle under *their own*
> catalog keys. Disabling the mod un-publishes those keys on the spot, but defs already created stay
> alive for the session; restart for a clean undo (`docs/SHIPPING-A-CONTENT-MOD.md:396-404`).

Meet the **Vulture AR**, the **Vulture Sniper** and the **Vulture Sidearm** -- three `WeaponDef`s
the game does not ship, sitting in the Phoenix base's inventory the moment a new campaign begins,
ten of each with ten clips apiece. Equip them, holster them, take them on a mission, overwatch,
reload and shoot. Each fires with its own projectile colour and muzzle effect.

**The mechanism is not in this mod.** Cloning a def, building its view, loading its icon, pointing a
skin at a published prefab, fitting the four `EXT_` sockets, deep-copying the damage payload and
seeding starting storage are the same for every weapon anyone will ever add, so they live in the
ContentTool engine -- `src\Tactical\WeaponBuild.cs`. This mod is one call and a list. **Read
`ppcontent.json`**, not this file's `src\`.

```text
WeaponAdd\
  ppcontent.json                      THE INTERESTING FILE: three "publish" rows + three weapons
  meta.json                           the mod manifest; AssemblyName = WeaponAdd.dll
  Content\Models\sniper.glb           the geometry, fitted to the shipped sniper's own box
  Content\Models\ar181.glb            the Sketchfab download, UNMODIFIED; attribution in SOURCES.md
                                      Multi-mesh; MeshMerge joins it at bake. It is worn.
  Content\Models\nerf.glb             the Sketchfab download REDUCED - 211,120 -> 8,316 tris, three
                                      512 atlases, turned onto +Z. See tools\reduce_nerf.py
  Content\Textures\sniper.png         1024 - named after the model, which is what binds it
  Icons\sniper_inv.png                450x450 - the inventory cell, rendered FROM sniper.glb
  Icons\nerf_inv.png                  450x450 - the same, rendered FROM nerf.glb
  src\WeaponAddMain.cs                ONE CALL. The mechanism is ContentTool's WeaponBuild
  tools\fit_sniper.py                 orientation, scale AND the three EXT_ socket positions
  tools\reduce_nerf.py                decimate + downsample a render asset into a game asset
  tools\render_icon.py                the icon, rasterised offline from the shipped .glb
  tools\downscale_atlas.ps1           2048 -> 1024, and why
  tools\check_project.py              reads ppcontent.json the way the TOOL reads it
  tools\source\                       the CC0 sniper kit AND nerf_gun.glb as downloaded, so every
                                      step re-runs from the original on a fresh clone
  SOURCES.md                          source, author and licence for every shipped media file
```

## The three weapons

| weapon | donor | model | shot | key manifest fields |
|---|---|---|---|---|
| **Vulture AR** | `SY_LaserAssaultRifle_WeaponDef` | `ar181.glb` | GREEN blobs -- `projectile` from `Crabman_Head_Spitter_WeaponDef`, `tint` `#4CFF5A` | `projectile`, `tint`, `offset` |
| **Vulture Sniper** | `SY_LaserSniperRifle_WeaponDef` | `sniper.glb` | BLUE beam -- donor laser projectile, `tint` `#3FA9FF`, `trail` `0.6` (long TrailRenderer = visible beam) | `tint`, `trail`, `icon` |
| **Vulture Sidearm** | `SY_LaserPistol_WeaponDef` | `nerf.glb` | ORANGE beam -- `tint` `#FF7A14`, `flash` from `NJ_FlameThrower_WeaponDef` (igniter muzzle effect), fire damage + burning | `tint`, `flash`, `damagetype`, `keywords`, `icon` |

> **All three wear their own art**, and the Sidearm is the one that shows what a DOWNLOAD costs: as
> published it is 211 120 triangles across 11 meshes with six 1024 atlases, 25x heavier than
> anything Phoenix Point ships. `tools\reduce_nerf.py` takes it to 8 316 triangles and three 512
> atlases in one pass. The current file is 10 112 412 B -> 909 976 B; `check_project.py` reads those
> shipped bytes and all 8 316 triangles.

## Rebuild the demo art

- `fit_sniper.py` — rebuilds `sniper.glb` from the CC0 kit, turns and uniformly fits it to the
  measured Phoenix sniper box, then prints shoot/aim/shell coordinates (`fit_sniper.py:3-27`,
  `:149-163`).
- `downscale_atlas.ps1` — rebuilds `sniper.png` at 1024 from the kit's 2048 atlas; the script checks
  dimensions and sampled colours (`downscale_atlas.ps1:1-8`, `:31-47`).
- `reduce_nerf.py` — always starts from `tools\source\nerf_gun.glb`; it bakes transforms, turns the
  barrel to +Z, clusters 211 120 triangles to 8 316, and downsamples the three used base-colour
  atlases from 1024 to 512 (`reduce_nerf.py:3-29`, `:328-411`).
- `render_icon.py` — rasterises the shipped GLB into a 450x450 inventory icon. It and
  `check_project.py` iterate every primitive of every mesh, so multi-material `nerf.glb` is rendered
  and counted in full (`render_icon.py:92-105`, `check_project.py:121-134`).

Verified in-game 2026-08-28, `D:\PP-Instance2`: `ar181_skin` renders at scale 0.553 = donor
exactly; `sniper_skin` 0.819 = donor exactly. Sidearm verified 2026-08-29: `nerf_skin` at 0.1816
under `gun_point_hand`, 0.182 m long against the donor `SY_LaserPistol`'s 0.306 m, one hand,
firing.

## Install

```text
ct_project WeaponAdd                # bake this mod's own bundle from its Content\ folder
ct_catalog apply WeaponAdd          # publish the key LIVE - no restart
ct_catalog verify
ct_catalog status                   # what is published right now
```

Then **start a NEW campaign** -- the weapons are added to the STARTING storage, so an existing
save will not have them. `Player.log` carries one `ct_weapon PASS` line per weapon and one per
model.

## Projectile and colour -- why `tint` exists

**There is NO hitscan in Phoenix Point.** Every non-melee shot is a moving `Projectile` with
`DamagePayload.Speed`. A "beam" is just a projectile whose prefab has a long `TrailRenderer`.
`DamageDeliveryType` (Melee/DirectLine/Parabola/Sphere/Cone) is the only delivery selector.

**Shot colour is NOT in any def.** All laser projectile prefabs are pure white (trail gradient
keys + PS start colour = `(1,1,1,1)`); the hue comes from a SHARED trail material. You cannot get
a colour by picking a differently-coloured donor -- you must `tint`.

`tint` clones the `ProjectileDef` and takes a PRIVATE copy of its prefab, then recolours every
`TrailRenderer` colorGradient key and every `ParticleSystem` startColor/colorOverLifetime. The
engine logs `projectile=<name> (own copy)` vs `(shared)` in `Player.log`.

Caveat: tint is vertex-colour only, the material stays shared. Landed hue = tint x material.
Clean on white laser prefabs; an already-coloured donor bolt needs a private material instance
(not implemented).

## The donor picks THREE things at once

The clone source is the weapon CLASS, and that is not a detail. Cloning a donor gives you:

1. **Stats** -- damage payload, AP cost, burst count, magazine, etc.
2. **Pose and animation** -- selected by membership in an `EquipmentListDef` (`RiflesListDef`,
   `SnipersListDef`, `PistolsListDef`, ...) -- NOT by `ItemDef.Tags` and NOT by `HandsToUse`.
   ContentTool appends the clone next to its donor automatically.
3. **The reference box for `fit: auto`** -- the donor's largest mesh by bounds diagonal; uniform
   scale = smallest of the 3 extent ratios.

Consequence: a sniper mesh on a PDW donor gives wrong hands on the grips AND a wrong-size fit.
Choose the donor to match the MESH CLASS, not just the stats.

## Shared vs private visual defs

Donor visual defs are SHARED references. Assigning a donor's `ProjectileVisuals`/`VisualEffects`
is fine; mutating them repaints every weapon in the game that uses them. `tint`/`trail` therefore
clone first. The engine logs `projectile=<name> (own copy)` vs `(shared)` in `Player.log` --
check this to confirm your weapon has a private copy.

`NJ_FlameThrower_WeaponDef`'s `ProjectileVisuals` is an EMPTY prefab shared with
`Bash_WithWhateverYouCan` -- copying it gives an INVISIBLE shot. Its fire lives in
`VisualEffects.Flash`. The Sidearm uses `flash` to take the flamethrower's effects without
touching its invisible projectile.

## Why the fit must live below the prefab root

`Addon.AttachVisuals` (`Addon.cs:1079-1080`) does `VisualRoot.SetParent(attachTransform);
VisualRoot.ResetTransform();` -- that zeroes localPosition/localRotation/localScale of the prefab
ROOT on attach. ContentTool therefore writes fit/scale/rotate to the prefab's existing mesh CHILD
(commit `477a2dc`). Known ceiling: a foreign prefab whose mesh sits ON the root has nowhere below
the root to write, and keeps the erased-at-attach behaviour.

Override the fit solver when it gives the wrong answer:

- `scale`: float -- explicit uniform mesh scale, replaces the computed value.
- `rotate`: `"x,y,z"` euler degrees -- explicit mesh rotation, replaces the axis-aligned auto
  rotation and `flip`.
- `offset`: `"x,y,z"` metres -- added after the solve, so size and turn stay measured while the grip
  moves. Derived sockets move with it (`WeaponBuild.cs:797-840`). The AR uses `0,-0.07,0`; the
  Sniper uses `0,0,0.06` (`ppcontent.json:33-35`, `:50-53`).

## Muzzle flash and VFX binding

`EquipmentDef.VisualEffects` (`EquipmentDef.cs:26`) is an `EquipmentVisualEffectsDef` with
exactly four members (`EquipmentVisualEffectsDef.cs:9-15`):

| member | type | spawned by | where |
|---|---|---|---|
| `Flash` | `GameObject` | `Weapon.SpawnFlash`, `Weapon.cs:389-397` | at `EXT_ShootPoint` |
| `Smoke` | `GameObject` | `Weapon.SpawnSmoke`, `Weapon.cs:399-406` | parented to the projectile-origin transform |
| `Shell` | `GameObject` | `Weapon.SpawnShell`, `Weapon.cs:408-421` | at `FindTransform(ShellEjectionPoint)` |
| `ShellEjectionPoint` | `string` | -- | defaults to `"EXT_ShellPoint"` |

The tracer and impact are somewhere else: `WeaponDef.DamagePayload.ProjectileVisuals`, a
`ProjectileDef`.

**Swapping to a different shipped weapon's look** is what `projectile` and `flash` do in the
manifest -- no code needed.

## What "turn-key" actually consists of

Everything below is inherited by cloning the donor, and every one of them is a thing a
hand-written def gets wrong:

| what | value it inherits |
|---|---|
| equip slot | `RequiredSlotBinds` -> the donor's slot; `HandsToUse` |
| holster | `HolsterSlotDef` -> the donor's holster slot |
| abilities | `Weapon_ShootAbilityDef`, `Overwatch_AbilityDef`, `Reload_AbilityDef`, `DropItem_AbilityDef` |
| ammo | `CompatibleAmmunition`, `ChargesMax` -- the demo adds 10 clips to `StartingStorage` |
| damage | `AutoFireShotCount`, `APToUsePerc` -- the burst and the AP cost are the shipped ones; only per-shot damage is changed |
| tags | all donor tags |
| pose/animation | clone appended beside the donor in equipment-filtered action defs and shared `EquipmentListDef`s (`WeaponBuild.cs:231-304`) |
| sound | `ShootingEvent` + the Wwise `MainSwitch` |
| visuals | `VisualEffects` and `ProjectileVisuals`, unless overridden by `flash`/`projectile`/`tint`/`trail` |

## Honest limits

- **New campaigns only.** `StartingStorage` is read when a campaign is created. An existing save
  has already been filled.
- **No research, no manufacture, no vendor.** The guns cannot be built or bought.
- **Every projectile is a shipped one.** Custom particle-system projectiles are blocked on the
  bake (`ParticleSystem` is not in the serialiser's class set). `tint`/`trail` customise the
  shipped prefab's colour and length, but authoring a new effect from scratch requires
  `ParticleSystem` support.
- **The material is Unity's `Standard` with one texture.** No normal map, no ORM.
