# Add a playable humanoid soldier

Add your own humanoid model as a new soldier in the starting squad. A playable soldier needs the game’s full set of actions, including aiming, reloading and weapon handling.

## You need

- A **GLB you supply** with a skinned mesh, weighted vertices, an armature with named bones, and at least an idle animation clip. Inspect its rig and clips before conversion.
- Python 3, the repository’s offline tools, and Phoenix Point’s installed data for exporting the game’s clips.
- A complete retargeted clip set for a playable soldier. Do not remove clip families to reduce file size.
- ContentTool installed and enabled, and a new campaign for `startingRoster`.

`demos/HumanoidSoldier` contains `meta.json`, `ppcontent.json`, `Content\Models\soldier.glb`, `README.md` and `SOURCES.md`. Its ready-made GLB has the retargeted clips. The raw source model it was converted from is **not in the repository**: bring your own rigged, animated humanoid GLB.

## Folder layout

```text
MySoldier\
  meta.json
  ppcontent.json
  Content\
    Models\
      soldier.glb               <- your converted humanoid and full clip set
  Dist\
    MySoldier.bundle            <- created by the bake
```

## Steps

1. Prepare **your own** source GLB in Blender. Check the skin weights, bone hierarchy, rest pose and idle clip. The game’s Generic clips bind by bone path, so matching a few bone names is insufficient. Read [the animation contract](animation-contract.md).

2. Adapt the repository’s offline conversion tools to that source. `tools\ppskel.py` takes no path arguments: it has fixed `SRC`, `DST` and `RENAME` values for the demo’s original rig, and `PP_PREFAB` expects a JSON dump of the game’s `CHR_Human_Rig_Ready` prefab at `..\extracted\GameData\prefabs\` beside the repository, which the repository does not ship. `tools\ppretarget.py` also has fixed input and output paths. Set those values for your GLB and verify every mapped Phoenix Point bone path. The generated bone-map JSON is a report, not a mapping input.

3. Run the conversion in order from the repository root. When exporting clips, pass the repository’s `lib\classdata.tpk` and a shipped bundle from **your** game installation:

   ```text
   python tools\ppskel.py
   python tools\ppskel.py --check
   python tools\ppskel.py --rest > tools\pp-rest.tsv
   dotnet run --project tools\ClipCensus -- --export <classdata.tpk> <shipped-bundle> tools\pp-rest.tsv tools\pp-clips.json
   python tools\ppretarget.py
   python tools\ppretarget.py --check
   python tools\ppretarget.py --selftest
   python tools\ppzip.py <retargeted.glb> <compressed.glb>
   python tools\ppzip.py --selfcheck
   ```

   `pp-rest.tsv` and `pp-clips.json` are generated inputs; they are not checked-in clip data. Require the conversion checks to pass before copying the final GLB to `Content\Models\soldier.glb`. Compression keeps the clips; trimming them can leave playable actions waiting for absent animation events.

4. Create `meta.json`:

   ```json
   {
     "ID": "example.mysoldier",
     "AssemblyName": "",
     "Version": "1.0.0",
     "Name": [{ "Key": "English", "Value": "My soldier" }],
     "Dependencies": ["com.morgott.ContentTool"]
   }
   ```

5. Create `ppcontent.json`. These clip names and event times are the **demo’s example**; confirm them against your converted file and measure your own event times:

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

6. Confirm the donor, bake and package:

   ```text
   ct_list defs PX_SniperStarting TacCharacterDef
   ct_project MySoldier
   ct_package MySoldier
   ```

   Enable the mod, restart and start a **new** campaign. Existing campaigns do not receive a new starting soldier.

## Check it worked

Require `ppskel check OK` and `ppretarget check OK` from the offline tools. The bake must end with:

```text
creature-roles PASS "clips" maps <mapped> of <discovered> discovered animation(s); every required role (walk, idle, attack, death) is mapped
ct_project: ALL PASS - <path>
```

In game, confirm the soldier appears in the starting squad and test movement, several weapon families, aiming, firing, reloading and death. A four-role bake check does not establish that every playable animation is present.

## Common errors

| What you see | Why | Fix |
|---|---|---|
| `ppskel check OK` is absent | The source rig or adapted bone map failed its checks. | Correct the map and rerun its check before retargeting. |
| `ppretarget check OK` is absent | Rest pose, segment lengths or clip conversion failed. | Correct the reported input or mapping; do not use that output. |
| `creature-roles FAIL` | A mapped clip name is absent or a required role is empty. | Use names found in your baked GLB, then bake again. |
| `ct_creature FAIL ppcontent.json "creature": "model" names` | `soldier.glb` is missing or the manifest names another stem. | Match the direct GLB filename and `model` value. |
| An action stalls in game | The playable clip set or its events may be incomplete. | Keep the full clip set and inspect the affected action and log. |

See [messages](../reference/messages.md) for more diagnostics.

## Example

[HumanoidSoldier](https://github.com/UberMorgott/PhoenixPoint-Mod-ContentTool/tree/main/demos/HumanoidSoldier) includes a ready-made `soldier.glb` that you can bake as a demonstration. Its raw source GLB is not included.
