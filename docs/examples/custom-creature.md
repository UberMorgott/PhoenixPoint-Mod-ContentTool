# CustomCreature

Start a new campaign with this demo enabled. A cyborg spider joins the starting squad and aircraft;
in tactical combat it walks, bashes, spits, reacts to damage and dies using its own model and clips.

**Corresponds to:** [Add a creature](../recipes/creature.md),
[Prepare a rigged, animated GLB](../recipes/animation-contract.md), and
[Build a behaviour DLL](../recipes/behavior-dll.md).

## Features and how they work

- **The GLB uses the Add route.** `cyborg_spider.glb` is baked into the mod's own
  `CustomCreature.bundle` at `assets/morgott.demo.customcreature/models/cyborg_spider`. It keeps its
  own 49-bone skeleton and seven animation clips; no shipped Unity bundle is targeted.
- **Six clips receive game roles.** `Spider_Walk`, `Spider_Idle`, `Spider_Damage`,
  `Spider_Attack_1`, `Spider_Attack_2` and `Spider_Death` map to walk, idle, reaction, attack,
  ranged and death. `Spider_Idle_long` is present but deliberately unassigned.
- **Preview and loop choices are declared.** `play` selects `Spider_Idle` for the baked model's
  preview Animator. `loop` marks `Spider_Idle` and `Spider_Walk`; glTF carries no loop flag for
  ContentTool to infer.
- **Animation events prevent action stalls.** The manifest places step events at 0.15 and 0.65 of
  the walk; `ActionDo`, `ShootShot` and `ActionEnd` at measured fractions of both attacks; and
  `Ragdoll` at 0.9 of death.
- **A shipped Swarmer supplies the component structure.** `donor` is
  `Swarmer_TacCharacterDef`. ContentTool clones its chassis and rewires the rig and animation-action
  defs to the mod model and clips.
- **A shipped spitter supplies ranged combat.** `ranged` is
  `Crabman_Head_Spitter_WeaponDef`; ContentTool attaches its muzzle to a measured rig point.
- **Scale and ground fit are explicit.** Project scale is `0.008`, `lift` is `2.1372`, model-up is
  `0,1,0`, volume is one tile, and `climbPitch` is 90 degrees.
- **Stats are manifest data.** The declaration requests health 40, will 10 and speed 16. The
  verification run under TFTV measured 60 health, so the live mod stack can still alter that result.
- **The DLL supplies campaign placement.** It calls `CreatureBuild.Build`, then patches both
  `GeoPhoenixFaction.CreateInitialSquad` and tutorial `GeoscapeTutorial.InitSquad` so either new-game
  route adds the built creature to the player's vehicle.

## Project on disk

```text
CustomCreature\
  meta.json                         <- AssemblyName is CustomCreature.dll
  ppcontent.json                    <- model, play/loop, roles, events, donor, fit and stats
  CustomCreature.csproj
  Content\Models\
    cyborg_spider.glb               <- 49-bone rig and seven clips
  Dist\CustomCreature.bundle       <- committed Add output
  bin\Release\CustomCreature\
    CustomCreature.dll
    CustomCreature.pdb
    meta.json
  src\CustomCreatureMain.cs        <- builder call and two new-campaign patches
  tools\check-bridge.ps1
  tools\check-donor-free.ps1
  README.md
  SOURCES.md
```

## Rebuild and run it

```text
dotnet build demos\CustomCreature\CustomCreature.csproj -c Release -p:PPRoot="D:\Steam\steamapps\common\Phoenix Point"
ct_list defs Swarmer TacCharacterDef
ct_list defs Crabman_Head_Spitter WeaponDef
ct_project CustomCreature
ct_package CustomCreature
```

Restart, enable the demo, and start a new campaign. To repeat the recorded tactical gate, start from
the main menu and use a tactical save available on your own install:

```text
ct_creature list
ct_creature gate <tactical-save-name> customcreature
```

## What a good run prints

```text
creature-clips 'cyborg_spider': 7 animation(s) in the file -> Spider_Walk, Spider_Idle, Spider_Idle_long, Spider_Damage, Spider_Attack_1, Spider_Attack_2, Spider_Death
creature-events PASS every blocking event the game waits for is declared
creature-roles PASS "clips" maps 6 of 7 discovered animation(s); every required role (walk, idle, attack, death) is mapped
ct_project: ALL PASS - <project>\Dist\CustomCreature.bundle
```

Runtime construction includes:

```text
ct_creature PASS cloning 'Swarmer_TacCharacterDef' (by def name)
ct_creature PASS '<project>\Dist\CustomCreature.bundle' -> model 'cyborg_spider', 7 clip(s): <clip list>
ct_creature PASS '<new template>' is built: set='<set>' anims='<anims>' base='<base>' chassis='<chassis>'
```

## Verification status

**Verified in-game on 2026-08-28.** The 19-arm gate passed: the spider bashed a target from 190 to
130 health, spat it from 130 to 120, walked 2.83 tiles in 0.71 seconds, and played its own death
clip. The control came from a separate launch. The 60 measured health versus manifest 40 remains an
open interaction with the resident TFTV stack.
