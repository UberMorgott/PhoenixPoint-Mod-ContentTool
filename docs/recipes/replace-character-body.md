# Replace a shipped character body (experimental)

This route is **unverified in game**. ContentTool can bake the model and the runtime code contains a
named, contained replacement path, but this repository has no completed in-game run proving that
Eileen appears, animates, fights, saves and reloads with the new body. The example also requires the
Festering Skies DLC. Treat it as an experiment, not a finished mod recipe.

Use it when one existing `TacCharacterDef` must keep its def, GUID, name, tags and story references
while receiving a different rig/body. It does not add a second character.

## What you need before you start

- Festering Skies installed for the example target `S_SY_Eileen_CharacterTemplateDef`.
- The full, untrimmed 300-clip humanoid GLB produced by the
  [humanoid soldier pipeline](humanoid-soldier.md). The worked output is
  `tiffany_cox_ppzip.glb`.
- A direct `Content\Models\body.glb`; only GLB is scanned for complete models.
- Acceptance that the current body-swap code clears the target's body-part and equipment arrays.
  Eileen is expected to need re-equipping from stores.
- A disposable test campaign. Do not use an important save for the unverified run.

## Folder tree

```text
ReplaceCharacterBody\
  meta.json
  ppcontent.json               <- replaceBody names one shipped TacCharacterDef
  Content\
    Models\
      body.glb                 <- full rig and all 300 retargeted clips
  Dist\
    ReplaceCharacterBody.bundle <- written by ct_project
```

The model stem is matched without regard to case. `replaceBody` is a def lookup, not a bundle asset
lookup; a missing DLC means the def is absent and the route refuses by name.

## Steps

1. Produce the full humanoid output. For the checked-in worked rig, run the same commands as the Add
   route:

   ```text
   python tools\ppskel.py
   python tools\ppretarget.py
   python tools\ppzip.py tiffany_cox_ppfit.glb tiffany_cox_ppzip.glb
   python tools\ppzip.py --selfcheck
   ```

   Require `ppskel check OK` and `ppretarget check OK`. Do not use `ppslim.py`.

2. Create `ReplaceCharacterBody\Content\Models`. Copy `tiffany_cox_ppzip.glb` into it and rename
   the copy `body.glb`. Delete any second GLB from that folder.

3. Confirm that the target def exists in this installation. The argument order is name filter, type:

   ```text
   ct_list defs Eileen TacCharacterDef
   ```

   Continue only if the output contains `S_SY_Eileen_CharacterTemplateDef`.

4. Create `meta.json`:

   ```json
   {
     "ID": "example.replacecharacterbody",
     "AssemblyName": "",
     "Version": "1.0.0",
     "Name": [{ "Key": "English", "Value": "Replace Eileen's body" }],
     "Dependencies": ["com.morgott.ContentTool"]
   }
   ```

5. Create `ppcontent.json`. This is the checked-in demo shape with a new project ID:

   ```json
   {
     "id": "example.replacecharacterbody",
     "bundle": "ReplaceCharacterBody.bundle",
     "scale": 1.0,
     "play": "HL_IdleAlert_NoGun",
     "loop": "*Loop*, *Idle*",
     "creature": {
       "clips": {
         "HL_IdleAlert_NoGun": "idle",
         "MV_RunFwd_Loop_NoGunA": "walk",
         "FF_Punch_ShotLoop": "attack",
         "HL_Death_AR": "death",
         "HL_HurtFront_AR": "reaction"
       },
       "events": {
         "attack": "ActionDo 0.3, ShootShot 0.5, ActionEnd 0.9",
         "death": "Ragdoll 0.9"
       },
       "model": "body",
       "replaceBody": "S_SY_Eileen_CharacterTemplateDef",
       "up": "0,1,0",
       "lift": 0.0,
       "volume": 1
     }
   }
   ```

6. Bake and require a clean summary:

   ```text
   ct_project ReplaceCharacterBody
   ```

7. Package the author sources and baked mod-owned bundle:

   ```text
   ct_package ReplaceCharacterBody
   ```

8. For the experimental runtime check, enable the mod, restart, and open a disposable campaign in
   which Eileen exists. Check her roster model, tactical animation, equipment state, save and reload.
   Record the corresponding `ct_creature` lines from `Player.log`.

## What success looks like

The verified part is the offline conversion and bake. A clean bake has these shapes:

```text
ppskel check OK: <counts>
ppretarget check OK: <checks>; 300 clip(s) whole - <channel counts>; <rig summary>
model 'body' kept <n> material(s) as <n> submesh(es)
creature-roles PASS "clips" maps 5 of 300 discovered animation(s); every required role (walk, idle, attack, death) is mapped
ct_project: ALL PASS - <project>\Dist\ReplaceCharacterBody.bundle
```

The runtime code is written to emit these lines after it resolves and publishes the swap:

```text
ct_creature PASS replacing the body of 'S_SY_Eileen_CharacterTemplateDef' (by def name)
ct_creature PASS 'S_SY_Eileen_CharacterTemplateDef' now wears this mod's body and is otherwise the character the game shipped: name '<name>', <n> tag(s), Strength <value> - the def, its GUID and every event that names it are the game's own
ct_creature PASS 'S_SY_Eileen_CharacterTemplateDef' is built: set='<set>' anims='<anims>' base='<base>' chassis='<chassis>'
```

Those are code paths, not a recorded successful in-game run. `TODO(verify)`: run the full Eileen
roster/tactical/save/reload test with Festering Skies installed and keep the log as evidence.

## When it fails

| Console text | Meaning | Fix |
|---|---|---|
| `ct_creature FAIL ppcontent.json "creature": "replaceBody" is 'S_SY_Eileen_CharacterTemplateDef', which is not the name of a shipped TacCharacterDef with a component set. It names the ONE character whose body this model replaces - write a def name such as "S_SY_Eileen_CharacterTemplateDef". A DLC character is absent unless that DLC is installed. Nothing was changed.` | Festering Skies is absent, the name is wrong, or the def lacks the required structure. | Install/enable the DLC or choose a real target returned by `ct_list defs <filter> TacCharacterDef`. Do not fall back to a guessed name. |
| `ct_creature FAIL '<target>' already wears the body of mod '<id>' - a character has one body, and two mods replacing the same one would share ByTemplate() state. Disable one of them, or point this mod's "replaceBody" at a different character. Nothing was changed.` | Another enabled mod already owns this character's body. | Disable one body mod or choose a different target. Restart before retesting. |
| `ct_creature FAIL ppcontent.json "creature": "model" names 'body' but Content\Models\ holds [<stems>]. Nothing was changed.` | `body.glb` is absent or named differently. | Keep one direct `Content\Models\body.glb`, or update `model`. |
| `ct_creature VOID '<project>\Dist\ReplaceCharacterBody.bundle' does not exist - run `ct_project ReplaceCharacterBody` in the console once, then restart. Nothing was changed.` | Runtime started before the mod-owned bundle existed. | Run the printed bake, require `ALL PASS`, then restart. |
| Output starts with `creature-roles FAIL ppcontent.json "creature": "clips" leaves <n> REQUIRED role(s) unmapped: <roles>.` The same line then lists the assignment rule and all accepted roles. | A required clip was removed or renamed. | Restore the full 300-clip output and exact role mappings. Do not trim a playable character. |

Read [the status glossary](../troubleshooting/bake-errors.md). A clean bake does not prove the
experimental in-game result; the TODO above remains open until that separate run exists.

## Worked demo

[ReplaceCharacterBody](../examples/replace-character-body.md) targets Eileen from Festering Skies.
It remains experimental and unverified in-game.
