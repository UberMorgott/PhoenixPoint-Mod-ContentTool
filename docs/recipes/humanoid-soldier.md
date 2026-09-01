# Add a playable humanoid soldier

This converts the repository's worked foreign humanoid onto Phoenix Point's Generic rig and adds it
to a new campaign with no DLL. Use this route for a playable soldier that must carry the full game
animation set, not for a creature with its own clips.

## What you need before you start

- Python 3 and the repository's `tools` directory.
- The worked source named `tiffany_cox_idle_animation.glb` at the repository root.
- The checked-in `tools\pp-clips.json` and `tools\pp-rest.tsv` for this game build.
- All 300 retargeted clips. Do not run `ppslim.py` on a playable soldier: missing aim, reload, stance
  or weapon-family clips can leave an action waiting forever.
- A new campaign. `startingRoster` is read while the initial squad is created.

The current `ppskel.py` and `ppretarget.py` are wired to this source's bone names and fixed
input/output filenames. A different rig requires changing and validating the `RENAME` table and
source constants in the scripts; the generated `ppskel-bone-map.json` is a report, not an input.

## Folder tree

```text
ContentTool\
  tiffany_cox_idle_animation.glb <- fixed ppskel input
  tiffany_cox_ppskel.glb         <- ppskel output
  tiffany_cox_ppfit.glb          <- ppretarget output with all 300 clips
  tiffany_cox_ppzip.glb          <- compressed output you copy
  tools\
    ppskel.py
    ppretarget.py
    ppzip.py
    pp-clips.json                <- retarget input for the shipped game build
    pp-rest.tsv
  MySoldier\
    meta.json
    ppcontent.json
    Content\
      Models\
        soldier.glb              <- renamed copy of tiffany_cox_ppzip.glb
    Dist\
      MySoldier.bundle
```

## Steps

1. Put the untouched worked source at repository root as `tiffany_cox_idle_animation.glb`.

2. Rename and extend its skeleton onto Phoenix Point's bone paths:

   ```text
   python tools\ppskel.py
   ```

   Require the final `ppskel check OK` line. This writes `tiffany_cox_ppskel.glb`.

3. Repose that rig and embed the checked-in 300-clip table:

   ```text
   python tools\ppretarget.py
   ```

   Require the final `ppretarget check OK` line. This writes `tiffany_cox_ppfit.glb`.

4. Compress the same curves without trimming the clip list, then run the tool's self-check:

   ```text
   python tools\ppzip.py tiffany_cox_ppfit.glb tiffany_cox_ppzip.glb
   python tools\ppzip.py --selfcheck
   ```

5. Create `MySoldier\Content\Models`, copy `tiffany_cox_ppzip.glb` there, and rename the copy
   `soldier.glb`. Do not leave a second GLB in that folder.

6. Create `MySoldier\meta.json`:

   ```json
   {
     "ID": "example.mysoldier",
     "AssemblyName": "",
     "Version": "1.0.0",
     "Name": [{ "Key": "English", "Value": "My soldier" }],
     "Dependencies": ["com.morgott.ContentTool"]
   }
   ```

7. Create `MySoldier\ppcontent.json`:

   ```json
   {
     "id": "example.mysoldier",
     "bundle": "MySoldier.bundle",
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
       "name": "My Soldier",
       "model": "soldier",
       "donor": "PX_SniperStarting_TacCharacterDef",
       "startingRoster": true,
       "up": "0,1,0",
       "lift": 0.0,
       "health": 40,
       "will": 10,
       "speed": 16,
       "volume": 1
     }
   }
   ```

8. Confirm the donor name, bake, and package:

   ```text
   ct_list defs PX_SniperStarting TacCharacterDef
   ct_project MySoldier
   ct_package MySoldier
   ```

9. Enable the mod, restart, and start a new campaign. The soldier should be in the starting squad
   and aboard the starting aircraft. Existing campaigns do not gain it.

## What success looks like

The offline tools end with these lines:

```text
ppskel check OK: <counts>
ppretarget check OK: <rest/segment/clip checks>; 300 clip(s) whole - <channel counts>; <rig summary>
```

The bake includes the material/submesh count and ends:

```text
model 'soldier' kept <n> material(s) as <n> submesh(es)
creature-roles PASS "clips" maps 5 of 300 discovered animation(s); every required role (walk, idle, attack, death) is mapped
ct_project: ALL PASS - <project>\Dist\MySoldier.bundle
```

After restart, `Player.log` ends the build with `ct_creature PASS '<template>' is built: ...` and
`ct_creature: built 1 creature(s) from enabled content mods`.

## When it fails

| Exact output | Meaning | Fix |
|---|---|---|
| `ppskel check OK:` is absent | The fixed source, mapping or generated rig did not pass the script's checks. | Stop. Restore the expected source filename or fix the mapping; do not pass a failed output to `ppretarget.py`. |
| `ppretarget check OK:` is absent | Rest orientation, segment preservation or the 300-clip conversion failed. | Stop. Keep the console error and correct the input/mapping; do not ship `tiffany_cox_ppfit.glb`. |
| `creature-roles FAIL ppcontent.json "creature": "clips" leaves <n> REQUIRED role(s) unmapped: <roles>. ...` | A required manifest clip is absent or unmapped. | Keep all 300 clips and restore the five exact mapping names above. Bake again. |
| `ct_creature FAIL ppcontent.json "creature": "model" names 'soldier' but Content\Models\ holds [<stems>]. Nothing was changed.` | The shipped GLB was renamed differently or an extra model is present. | Keep one direct file named `soldier.glb`, or update `model`. |

Read [the status glossary](../troubleshooting/bake-errors.md). The offline check lines are required
before `ct_project`; `ALL PASS` is required before packaging.
