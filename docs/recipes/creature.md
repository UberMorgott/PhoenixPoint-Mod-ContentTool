# Add a creature

This turns one rigged GLB and its own clips into a new tactical character definition. Use it for a
non-humanoid model that should keep its own skeleton and animations. ContentTool 1.1.2 builds enabled
creature manifests automatically; `startingRoster: true` needs no DLL.

## What you need before you start

- One rigged GLB directly under `Content\Models`, with skin weights and named clips.
- A shipped `TacCharacterDef` donor with a component set, animation actions, an addons manager and a
  melee body-part weapon. `Swarmer_TacCharacterDef` is the one-tile default used here.
- Honest clip-role choices and event times measured in each clip. Required roles are `walk`, `idle`,
  `attack` and `death`; optional roles include `reaction`, `ranged` and `climb`.
- A new campaign if you set `startingRoster`.

## Folder tree

```text
MyCreature\
  meta.json
  ppcontent.json               <- donor, model, clip roles, events and stats
  Content\
    Models\
      cyborg_spider.glb        <- one armature, weighted mesh and named clips
    Textures\
      cyborg_spider.png        <- optional external base colour
  Dist\
    MyCreature.bundle          <- bake this before enabling the creature
```

## Steps

1. Prepare the GLB using the [animation contract](animation-contract.md). Put it directly in
   `Content\Models`.

2. Find a donor by exact def name. The argument order is name filter, then type filter:

   ```text
   ct_list defs Swarmer TacCharacterDef
   ```

3. Create `meta.json`:

   ```json
   {
     "ID": "example.mycreature",
     "AssemblyName": "",
     "Version": "1.0.0",
     "Name": [{ "Key": "English", "Value": "My creature" }],
     "Dependencies": ["com.morgott.ContentTool"]
   }
   ```

4. Create an initial `ppcontent.json`. The empty block asks the first bake to write the real clip
   names into this same file:

   ```json
   {
     "id": "example.mycreature",
     "bundle": "MyCreature.bundle",
     "scale": 0.008,
     "creature": {}
   }
   ```

5. Run the scaffold bake:

   ```text
   ct_project MyCreature
   ```

   Expect `creature-scaffold: WROTE the clip list into <path> - map each one to a role there.` The
   first run then reports `creature-roles FAIL`; that is deliberate. Open the rewritten
   `ppcontent.json` instead of guessing clip names.

6. Map the four required roles. Add exact event names with times from 0 to 1 at the frames where the
   animation connects. This complete example uses the checked-in spider clip names; replace them
   with the names the scaffold wrote for your GLB:

   ```json
   {
     "id": "example.mycreature",
     "bundle": "MyCreature.bundle",
     "scale": 0.008,
     "play": "Spider_Idle",
     "loop": "Spider_Idle, Spider_Walk",
     "creature": {
       "clips": {
         "Spider_Walk": "walk",
         "Spider_Idle": "idle",
         "Spider_Damage": "reaction",
         "Spider_Attack_1": "attack",
         "Spider_Attack_2": "ranged",
         "Spider_Death": "death"
       },
       "events": {
         "attack": "ActionDo 0.4054, ShootShot 0.4865, ActionEnd 0.8378",
         "ranged": "ActionDo 0.2286, ShootShot 0.5429, ActionEnd 0.9143",
         "death": "Ragdoll 0.9"
       },
       "name": "Spider",
       "model": "cyborg_spider",
       "donor": "Swarmer_TacCharacterDef",
       "startingRoster": true,
       "ranged": "Crabman_Head_Spitter_WeaponDef",
       "up": "0,1,0",
       "lift": 2.1372,
       "health": 40,
       "will": 10,
       "speed": 16,
       "volume": 1,
       "climbPitch": 90
     }
   }
   ```

   The numeric scale, lift and event times above belong to the spider. Use the `creature-measure`
   line and your animation frames for another model. Remove `ranged` and its role/events for a
   melee-only creature.

7. Bake again. Fix every `creature-events WARN`, even though it is a warning: the line names an event
   the game waits for and says that omission costs a ten-second stall.

   ```text
   ct_project MyCreature
   ```

8. Enable the mod, restart, and start a new campaign. ContentTool builds it one frame after mod
   startup and adds it to the starting roster. Use `ct_creature list` to see built templates.

9. Package only after the bake passes:

   ```text
   ct_package MyCreature
   ```

## What success looks like

```text
creature-clips 'cyborg_spider': <n> animation(s) in the file -> <clip names>
creature-events PASS every blocking event the game waits for is declared
creature-roles PASS "clips" maps <mapped> of <discovered> discovered animation(s); every required role (walk, idle, attack, death) is mapped
ct_project: ALL PASS - <project>\Dist\MyCreature.bundle
```

After restart, `Player.log` contains runtime lines including:

```text
ct_creature PASS cloning 'Swarmer_TacCharacterDef' (<lookup method>)
ct_creature PASS '<new template>' is built: set='<set>' anims='<anims>' base='<base>' chassis='<chassis>'
ct_creature: built 1 creature(s) from enabled content mods
```

## When it fails

| Console text | Meaning | Fix |
|---|---|---|
| Output starts with `creature-roles FAIL ppcontent.json "creature": "clips" leaves <n> REQUIRED role(s) unmapped: <roles>.` The same line then lists the assignment rule and all accepted roles. | The scaffold has unassigned required roles. | Edit the `clips` map written into `ppcontent.json`; assign `walk`, `idle`, `attack` and `death`, then bake again. |
| Output starts with `creature-events WARN UNDECLARED: <role.event> - each costs a 10s stall per action`. The same line then prints the manifest form to add. | A mapped action lacks an event the engine waits for. | Add that event and its measured fraction under `creature.events`. Do not guess the time. |
| `ct_creature FAIL ppcontent.json "creature": "model" names '<name>' but Content\Models\ holds [<stems>]. Nothing was changed.` | `model` does not match a GLB stem. | Rename the GLB or change `model`. Delete stale extra GLBs. |
| `ct_creature FAIL Content\Models\ holds 0 model(s) [] - there is no .glb to build a creature out of. Nothing was changed.` | No direct GLB exists. | Move one GLB directly into `Content\Models`. |
| `ct_creature FAIL ppcontent.json "creature": "donor" is '<name>', which is neither a GameTagDef field on SharedGameTags nor the name of a shipped TacCharacterDef with a component set. Nothing was changed.` | The donor name is invalid. | Run `ct_list defs <filter> TacCharacterDef` and copy an exact suitable def name. |

Read [the status glossary](../troubleshooting/bake-errors.md). The scaffold's first failure is a
request for decisions; the second bake must end in `ALL PASS`.

## Worked demo

[CustomCreature](../examples/custom-creature.md) wires a seven-clip, 49-bone spider to a Swarmer
chassis and a Crabman ranged donor.

[HumanoidSoldier](../examples/humanoid-soldier.md) uses the same creature builder with a playable
human donor and `startingRoster: true`.
