# WeaponAdd

Start a new campaign with this demo enabled. The Phoenix base should contain ten Vulture ARs, ten
Vulture Snipers and ten Vulture Sidearms, plus ten compatible clips for each. Each weapon has its
own model and shot treatment.

**Corresponds to:** [Add a weapon](../recipes/weapon.md),
[Bake and publish a complete model](../recipes/animated-models.md), and
[Build a behaviour DLL](../recipes/behavior-dll.md).

## Features and how they work

- **Three models use the Add route.** `publish[]` exposes `models/sniper`, `models/ar181` and
  `models/nerf` from `WeaponAdd.bundle` as `GameObject`s under keys ending `4b60`, `4b61` and
  `4b62`. Each row declares `defaultlocalgroup_unitybuiltinshaders.bundle` as a dependency so the
  baked Standard materials resolve their shipped shader.
- **The sources are prepared, not accepted blindly.** `ar181.glb` keeps several source meshes which
  the bake merges. `tools\fit_sniper.py` prepares the sniper. `tools\reduce_nerf.py` bakes transforms,
  turns the barrel to +Z, reduces 211120 triangles to 8316, and reduces its used atlases to 512.
- **The sniper texture binds by stem.** `Content\Textures\sniper.png` has the same stem as
  `Content\Models\sniper.glb`; that naming convention binds the external image to the model.
- **Three new defs are cloned.** `weapons[]` clones `SY_LaserAssaultRifle_WeaponDef`,
  `SY_LaserSniperRifle_WeaponDef` and `SY_LaserPistol_WeaponDef`. ContentTool deep-copies the fields
  it tunes and appends each clone beside its donor in equipment lists used for poses and actions.
- **Vulture AR wears `ar181`.** Its model key ends `4b61`; `fit:auto` plus offset `0,-0.07,0` fits
  it to the assault-rifle donor. It borrows `Crabman_Head_Spitter_WeaponDef` projectile visuals,
  tints them `#4CFF5A`, and sets damage 45 and spread 2.4.
- **Vulture Sniper wears `sniper`.** Its key ends `4b60`; explicit scale `0.9497`, rotation
  `-4,-0.5,2` and offset `0,-0.02,-0.09` refine the auto fit. It uses
  `Icons\sniper_inv.png`, tint `#3FA9FF`, trail 0.6, damage 60 and spread 1.0.
- **Vulture Sidearm wears reduced `nerf`.** Its key ends `4b62`; scale `0.2416`, rotation
  `0,-10,0` and offset `-0.01,0,-0.062` place it on the pistol donor. It uses
  `Icons\nerf_inv.png`, orange tint `#FF7A14`, the flash from `NJ_FlameThrower_WeaponDef`, fire
  damage, `Burning_DamageKeywordEffectorDef=40`, damage 75 and spread 2.25.
- **The four weapon sockets are derived from donor geometry.** `fit:auto` builds the shoot, aim,
  shell and grip attachment points. Explicit scale/rotate/offset are written below the prefab root
  because the game's attachment code resets the root transform.
- **Starting inventory is new-campaign data.** Every row requests count 10 and clips 10. The builder
  adds them to each difficulty def's `StartingStorage`; it does not add research, manufacture or a
  vendor entry.
- **The DLL contains one builder call.** `WeaponAddMain.OnModEnabled` passes the folder to
  `WeaponBuild.Build`. The mechanism is in ContentTool; this demo supplies the manifest data.

## Project on disk

```text
WeaponAdd\
  meta.json                         <- AssemblyName is WeaponAdd.dll
  ppcontent.json                    <- three publish rows + three weapons
  WeaponAdd.csproj
  Content\
    Models\
      ar181.glb
      sniper.glb
      nerf.glb                      <- reduced from the source download
    Textures\
      sniper.png                    <- binds to the sniper model by stem
  Icons\
    sniper_inv.png
    nerf_inv.png
  Dist\WeaponAdd.bundle             <- committed Add output
  bin\Release\WeaponAdd\
    WeaponAdd.dll
    WeaponAdd.pdb
  src\WeaponAddMain.cs              <- one WeaponBuild.Build call
  tools\
    fit_sniper.py
    reduce_nerf.py
    render_icon.py
    downscale_atlas.ps1
    check_project.py
    source\                        <- original model and texture sources
  README.md
  SOURCES.md
```

## Rebuild and run it

```text
ct_list defs SY_LaserAssaultRifle WeaponDef
ct_list defs SY_LaserSniperRifle WeaponDef
ct_list defs SY_LaserPistol WeaponDef
dotnet build demos\WeaponAdd\WeaponAdd.csproj -c Release -p:PPRoot="D:\Steam\steamapps\common\Phoenix Point"
ct_project WeaponAdd
ct_catalog apply WeaponAdd
ct_catalog verify
ct_catalog status
ct_package WeaponAdd
```

Restart with the demo enabled and start a **new** campaign. Existing saves do not receive starting
storage added after their campaign was created.

## What a good run prints

```text
model 'sniper' -> assets/morgott.demo.weaponadd/models/sniper <model summary>
model 'ar181' -> assets/morgott.demo.weaponadd/models/ar181 <model summary>
model 'nerf' -> assets/morgott.demo.weaponadd/models/nerf <model summary>
ct_project: ALL PASS - <project>\Dist\WeaponAdd.bundle
3/3 key(s) published LIVE for 'morgott.demo.weaponadd' - nothing was written to the game installation
ct_catalog: PASS - the game's own Addressables served the mod's own bundle, and nothing was written to the installation
```

Runtime construction produces one line per weapon and one storage line:

```text
ct_weapon PASS 'Vulture AR' (Morgott_VultureAR_WeaponDef) cloned from SY_LaserAssaultRifle_WeaponDef; <icon, prefab, tuning and VFX report>
ct_weapon PASS 'Vulture Sniper' (Morgott_VultureSniper_WeaponDef) cloned from SY_LaserSniperRifle_WeaponDef; <icon, prefab, tuning and VFX report>
ct_weapon PASS 'Vulture Sidearm' (Morgott_VultureSidearm_WeaponDef) cloned from SY_LaserPistol_WeaponDef; <icon, prefab, tuning and VFX report>
ct_weapon PASS StartingStorage of <n> difficulty def(s) now carries [10x Morgott_VultureAR_WeaponDef + 10x <ammo>; 10x Morgott_VultureSniper_WeaponDef + 10x <ammo>; 10x Morgott_VultureSidearm_WeaponDef + 10x <ammo>]
```

## Verification status

**The current three-model manifest is not covered by the authority ledger.** Its WeaponAdd rows are
historical and explicitly predate the present AR/Sniper/Sidearm configuration. They verify the
publish, def-clone, fit and storage mechanisms in older revisions, not this exact current set.
`TODO(verify)`: measure all three current keys, loaded meshes and sockets; all three defs, stats,
projectiles and VFX; starting storage; equip/fire/reload/holster; and save/reload in one recorded run.
