# Replace a shipped character body

Give one existing character a different body while keeping that character’s shipped definition and story identity. This whole-body route is **experimental**: the repository does not yet contain a completed in-game roster, tactical, save and reload verification.

## You need

- ContentTool installed and enabled.
- A complete, skinned humanoid GLB with the full playable clip set. Follow [the humanoid conversion](humanoid-soldier.md) for your own source.
- An installed character def to replace. The example target, `S_SY_Eileen_CharacterTemplateDef`, needs the Festering Skies DLC.
- A disposable test campaign. The current body swap clears the target’s body-part and equipment arrays, so inspect equipment and re-equip as needed.

## Folder layout

```text
ReplaceCharacterBody\
  meta.json
  ppcontent.json
  Content\
    Models\
      body.glb                  <- full rig and playable clip set
  Dist\
    ReplaceCharacterBody.bundle <- created by the bake
```

## Steps

1. Put the completed GLB directly in `Content\Models` as `body.glb`. Keep its full clip set. Check the target def in the game console:

   ```text
   ct_list defs Eileen TacCharacterDef
   ```

   Continue only if the result contains `S_SY_Eileen_CharacterTemplateDef`.

2. Create `meta.json`:

   ```json
   {
     "ID": "example.replacecharacterbody",
     "AssemblyName": "",
     "Version": "1.0.0",
     "Name": [{ "Key": "English", "Value": "Replace character body" }],
     "Dependencies": ["com.morgott.ContentTool"]
   }
   ```

3. Create `ppcontent.json`. The clip names and times below match the worked demo’s model; replace them with measured values for your GLB:

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

   `replaceBody` selects one shipped def by name. It does not create a second character.

4. Bake, check the final result, then package:

   ```text
   ct_project ReplaceCharacterBody
   ct_package ReplaceCharacterBody
   ```

5. Enable the mod, restart and use a disposable campaign containing the target character. Check the roster model, tactical animations, equipment, saving and reloading. Record the runtime messages from `Player.log`.

## Check it worked

The verified offline condition is:

```text
creature-roles PASS "clips" maps <mapped> of <discovered> discovered animation(s); every required role (walk, idle, attack, death) is mapped
ct_project: ALL PASS - <path>
```

The runtime path is written to print:

```text
ct_creature PASS replacing the body of 'S_SY_Eileen_CharacterTemplateDef' (by def name)
```

That line alone does not prove the character works through tactical play and save/reload. Treat the in-game checks above as required for your own mod.

## Common errors

| What you see | Why | Fix |
|---|---|---|
| `ct_creature FAIL ppcontent.json "creature": "replaceBody" is` | The target is absent, misspelled or lacks the required component set. | Check the DLC and copy an exact `TacCharacterDef` name from `ct_list defs`. |
| `ct_creature FAIL '<target>' already wears the body of mod` | Another enabled mod replaces the same body. | Disable one replacement or choose another target, then restart. |
| `ct_creature FAIL ppcontent.json "creature": "model" names` | `body.glb` is missing or misnamed. | Match the file stem to `model`. |
| `ct_creature VOID '<path>' does not exist` | The mod-owned bundle was not baked before runtime. | Run `ct_project ReplaceCharacterBody`, require `ALL PASS`, then restart. |
| `creature-roles FAIL` | Required mapped clips are absent. | Restore the complete converted GLB and correct its clip map. |

See [messages](../reference/messages.md) for more diagnostics.

## Example

[ReplaceCharacterBody](https://github.com/UberMorgott/PhoenixPoint-Mod-ContentTool/tree/main/demos/ReplaceCharacterBody) supplies a worked model and manifest for the DLC target. Its whole-body result remains unverified in game.
