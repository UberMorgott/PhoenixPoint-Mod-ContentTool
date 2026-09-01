# HumanoidSoldier

This demo adds a foreign humanoid as a playable Phoenix soldier. On a new campaign it should appear
in the starting roster and aircraft, using Phoenix Point's weapon poses and actions on its own body.

**Corresponds to:** [Add a playable humanoid soldier](../recipes/humanoid-soldier.md),
[Add a creature](../recipes/creature.md), and
[Prepare a rigged, animated GLB](../recipes/animation-contract.md).

## Features and how they work

- **The body uses the Add route.** `soldier.glb` is baked into `HumanoidSoldier.bundle` at
  `assets/morgott.demo.humanoidsoldier/models/soldier`. It does not replace a shipped model.
- **The foreign rig carries 300 retargeted clips.** The offline `ppskel`/`ppretarget` pipeline
  converts Phoenix Point motion to the model's own skeleton. The full set remains because playable
  soldiers enter weapon, aim, reload, crouch and recovery states beyond the five manifest roles.
- **Five representative clips map required roles.** Idle, run, punch, death and hurt clips map to
  idle, walk, attack, death and reaction. Attack gets `ActionDo 0.3`, `ShootShot 0.5`,
  `ActionEnd 0.9`; death gets `Ragdoll 0.9`.
- **Preview and loop choices are explicit.** `play` selects `HL_IdleAlert_NoGun`. The wildcard
  declarations `*Loop*` and `*Idle*` mark matching baked clips as looping.
- **A shipped sniper supplies the human structure.** `PX_SniperStarting_TacCharacterDef` is the
  donor, so the clone starts from a playable human component set rather than a creature chassis.
- **Campaign placement is data-only.** `"startingRoster": true` tells ContentTool to place the new
  template in a new campaign. This demo has no DLL.
- **Fit and base stats are explicit.** Scale is 1, up is `0,1,0`, lift is zero, volume is one, and
  the requested health/will/speed values are 40/10/16.

## Project on disk

```text
HumanoidSoldier\
  meta.json                       <- content-only; no AssemblyName
  ppcontent.json                  <- Add declaration, play/loop and startingRoster=true
  Content\Models\
    soldier.glb                   <- own rig plus 300 retargeted clips
  README.md
```

`Dist\HumanoidSoldier.bundle` is not committed. You must bake this demo before testing it.

## Rebuild and run it

```text
ct_list defs PX_SniperStarting TacCharacterDef
ct_project HumanoidSoldier
ct_package HumanoidSoldier
```

Require `ALL PASS`, restart with the demo enabled, and start a **new** campaign. An existing campaign
has already built its starting roster.

## What a good run prints

```text
model 'soldier' kept <n> material(s) as <n> submesh(es)
creature-clips 'soldier': 300 animation(s) in the file -> <clip list>
creature-events PASS every blocking event the game waits for is declared
creature-roles PASS "clips" maps 5 of 300 discovered animation(s); every required role (walk, idle, attack, death) is mapped
ct_project: ALL PASS - <project>\Dist\HumanoidSoldier.bundle
```

The runtime builder is written to report:

```text
ct_creature PASS cloning 'PX_SniperStarting_TacCharacterDef' (by def name)
ct_creature PASS '<new template>' is built: set='<set>' anims='<anims>' base='<base>' chassis='<chassis>'
```

## Verification status

**Not verified in-game.** The authority ledger explicitly excludes `HumanoidSoldier`; a successful
bake and builder log are not a measured roster or tactical run. `TODO(verify)`: measure new-campaign
roster and aircraft placement, tactical idle/move/aim/fire/reload/hurt/death across weapon families,
then save and reload.
