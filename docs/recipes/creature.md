# Add a creature

Build a new tactical creature from your rigged GLB. The creature keeps its own skeleton and animations; it can also join a new campaign’s starting roster.

## You need

- ContentTool installed and enabled.
- One skinned GLB with bones and named walk, idle, attack and death clips. A reaction clip is useful. See [the animation contract](animation-contract.md).
- A shipped `TacCharacterDef` with a suitable component structure to use as a donor.
- Animation event times measured from your clips. Do not guess when an attack connects.

## Folder layout

```text
MyCreature\
  meta.json
  ppcontent.json
  Content\
    Models\
      creature.glb             <- rig, weighted mesh and named clips
  Dist\
    MyCreature.bundle          <- created by the bake
```

## Steps

1. Put your GLB directly in `Content\Models`. In the game console, find a donor by name and type:

   ```text
   ct_list defs Swarmer TacCharacterDef
   ```

   The spider demo uses `Swarmer_TacCharacterDef`. Choose a donor whose component structure suits your creature.

2. Create `meta.json`:

   ```json
   {
     "ID": "example.mycreature",
     "AssemblyName": "",
     "Version": "1.0.0",
     "Name": [{ "Key": "English", "Value": "My creature" }],
     "Dependencies": ["com.morgott.ContentTool"]
   }
   ```

3. Create `ppcontent.json` with an empty creature block, then bake once:

   ```json
   {
     "id": "example.mycreature",
     "bundle": "MyCreature.bundle",
     "creature": {}
   }
   ```

   ```text
   ct_project MyCreature
   ```

   Look for `creature-scaffold: WROTE the clip list into <path> - map each one to a role there.` The first bake can end with `creature-roles FAIL`: the tool has found your clips but needs you to assign their roles.

4. Edit the generated `ppcontent.json`. This example shows the shape used by a spider; replace its model stem, clip names, scale, lift and event times with values from **your** GLB:

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
         "Spider_Death": "death"
       },
       "events": {
         "attack": "ActionDo 0.4054, ShootShot 0.4865, ActionEnd 0.8378",
         "death": "Ragdoll 0.9"
       },
       "name": "Spider",
       "model": "creature",
       "donor": "Swarmer_TacCharacterDef",
       "startingRoster": true,
       "up": "0,1,0",
       "lift": 2.1372,
       "health": 40,
       "will": 10,
       "speed": 16,
       "volume": 1
     }
   }
   ```

   Use the bake’s `creature-measure` line to choose scale and lift. Map all four required roles. Reaction, ranged, climb and jump are optional. Declare attack and death events at the frames where they happen.

5. Bake again and read the entire result:

   ```text
   ct_project MyCreature
   ```

   Fix any `creature-events WARN UNDECLARED:` line. Each missing blocking event can stall an action. Package only after the bake passes:

   ```text
   ct_package MyCreature
   ```

6. Enable the mod and restart the game. With `startingRoster` set to `true`, start a **new** campaign. Run `ct_creature list` to inspect built templates.

   `ct_mission list` publicly lists tactical save names. If you use `ct_creature gate` for a tactical test, save first: it loads the named save immediately, without confirmation, and discards unsaved progress.

## Check it worked

The bake should include:

```text
creature-events PASS every blocking event the game waits for is declared
creature-roles PASS "clips" maps <mapped> of <discovered> discovered animation(s); every required role (walk, idle, attack, death) is mapped
ct_project: ALL PASS - <path>
```

After restart, inspect the creature in the roster and in tactical play. Check movement, attack timing, damage reaction and death; a clean bake alone does not prove those actions look right.

## Common errors

| What you see | Why | Fix |
|---|---|---|
| `creature-roles FAIL` | A required role is unmapped. | Assign walk, idle, attack and death to clips the scaffold actually found. |
| `creature-events WARN UNDECLARED:` | An attack or death event is missing. | Add the named event at its measured fraction of the clip. |
| `SOURCE SKIPPED:` | A model source could not be imported. | Correct the file named by the line, then bake again. |
| `ct_creature FAIL ppcontent.json "creature": "model" names` | The model stem does not match a direct GLB under `Content\Models`. | Match the `model` value to the GLB stem. |
| `ct_creature FAIL ppcontent.json "creature": "donor" is` | The donor is not the name of a shipped `TacCharacterDef` with a component set. | Choose an exact suitable `TacCharacterDef` returned by `ct_list defs`. |

See [messages](../reference/messages.md) for more diagnostics.

## Example

[CustomCreature](https://github.com/UberMorgott/PhoenixPoint-Mod-ContentTool/tree/main/demos/CustomCreature) shows a spider with its own clips.
