# Put a foreign humanoid into Phoenix Point as a playable soldier

Use this track when the source is a downloaded, sculpted or generated humanoid with its OWN
skeleton and bone names, but NO clips of its own. The finished model keeps its own body proportions
while playing Phoenix Point's animations. In the roster it is a playable HUMAN soldier with classes
and the full 102-weapon armoury; the mod uses manifest data and zero C#.

The cost is an offline conversion pipeline specific to the source rig. Starting with
`tiffany_cox_idle_animation.glb`, the worked path is: rename its rig, strip PP's pinning curves,
repose it, put the resulting model behind a `creature` manifest block, bake it with `ct_project`,
and load it in game. Each stage below names its input and output. Note that the weapon sockets are
settled offline in stage 1 — the in-game fit workbench cannot adjust them for a soldier carrying
PP's own weapons.

**The model keeps ALL of PP's clips, and that is not negotiable for a playable character.** A
soldier reaches states no manifest role names — aiming, reloading, crouching, every weapon family —
and a clip the file does not have cannot be substituted by anything. Stage 4 below is an optional
tool for non-playable models only; read its warning before you reach for it.

**This page is the ADD half of a pair.** Its mirror image is
[Give a shipped character a different body](replace-character-body.md), which takes the same model
through the same toolchain and puts it on a character the game ALREADY has, who keeps her def, name,
class and story role. Same clip roles, same bake; one manifest key apart — `"donor"` mints a new
person, `"replaceBody"` re-bodies an existing one.

## What this track gives you

The [creature recipe](creature.md) and its
[CustomCreature demo](https://github.com/UberMorgott/PhoenixPoint-Mod-ContentTool/tree/main/demos/CustomCreature) cover a different kind of source: a
creature that ships its OWN clips and needs a small DLL to build and inject the def. Nothing in that
track is retargeted; the bone names, counts and clip art all belong to the model.

| | CustomCreature (spider) | Humanoid soldier (this page) |
|---|---|---|
| Skeleton | model's own 49-bone rig, untouched | foreign rig RENAMED onto PP's bone paths |
| Clips | model ships its own walk/idle/attack/death | model ships NONE of its own — carries retargeted copies of PP's clips (565 shipped in total; all 300 that can be rewritten, and all 300 are kept) |
| Retargeting | none — bone names are free | full: orientation + segment-length preservation |
| Result | a squad creature (melee + optional ranged) | a playable HUMAN soldier (all classes, all weapons) |
| DLL | yes — builder call + squad injection | none — manifest data only |
| Proportions | model's own, unmodified | model's own segment lengths, PP's rest orientation |

Both tracks use `"creature"` in `ppcontent.json` and the same `startingRoster` key. The spider
demo's README explains the shared engine wiring: the clone model, def slots, animation events, root
motion and controller override. This page covers the additional conversion work for a humanoid that
must play PP's clips.

## Why renaming the bones is not enough

PP character rigs are Unity GENERIC, and the game has no humanoid retargeter. A clip binds to the
CRC-32 of a bone's transform path relative to the Animator. Renaming the foreign rig therefore makes
PP's curves bind, but it does not preserve the foreign model's shape.

Unity's generic bake also writes position curves on every bone. Most merely repeat the PP rig's rest
offsets, pinning PP's segment lengths onto any model that plays the clip. The measurement is
496/565 clips with non-root position curves; 95.3% of those values equal the prefab rest within
1 mm. Keeping the source proportions requires both PP-compatible paths and removal of those pinning
curves.

## End-to-end conversion

**Three stages produce the model you ship; stage 4 is an optional tool you will not use on a
playable character.** They live in `ContentTool\tools\` and run offline, before `ct_project`.
`ppskel.py` and `ppretarget.py` are wired to the worked source and output names; their script headers
contain the verified invocations.

### 1. Rename the rig with `ppskel.py`

Input is the untouched `tiffany_cox_idle_animation.glb`; output is `tiffany_cox_ppskel.glb`.
`ppskel.py` renames the foreign rig onto PP's full bone paths without changing its geometry. It also
inserts the PP nodes that the source lacks:

- roll bones inside limb chains;
- a 4th torso link;
- a single Neck;
- the `EXT_*` context and mount hierarchy;
- tip leaves.

The weapon sockets are a separate rename, not part of `EXT_*`: `weapon_attach_r` becomes
`gun_point_hand` and `weapon_attach_l` becomes `gun_point_shield` (`ppskel.py:73`). Those two bones
are what the game puts a weapon into, so where they land is decided here, offline.

The foreign-to-PP mapping is the `RENAME` table inside `ppskel.py` itself (`ppskel.py:44-78`), which
is built from the source rig's own bone-name constants. `ppskel-bone-map.json` is a GENERATED report
of the renames that were applied, not an input the tool reads — retargeting a different humanoid
currently means editing `RENAME` and those constants in the script, not editing the JSON.

Run the conversion, then use its checks before passing the output onward:

- `--check` asserts that every PP path resolves and the skin survives;
- `--rest` dumps PP's rest pose for comparison.

### 2. Strip the pinning curves with `ClipCensus\`

This step comes before the repose because `ppretarget.py` READS its output (`ppretarget.py:48`
points at `tools\pp-clips.json`). Running the repose first simply fails to find the clip table.

`ClipCensus\` is a C# AssetsTools.NET tool. Its `--export` mode reads PP's shipped bundle and the
rest-pose table, drops position curves that merely restate the prefab offsets, and writes
`tools\pp-clips.json`. Rotation curves and position curves that actually travel remain.

```powershell
dotnet run --project tools\ClipCensus -- --export <classdata.tpk> <shipped bundle> tools\pp-rest.tsv tools\pp-clips.json
```

### 3. Repose the skeleton with `ppretarget.py`

The rig input is `tiffany_cox_ppskel.glb`; the reposed output is `tiffany_cox_ppfit.glb`, and the
clip table from step 2 is embedded into it, so the rotation-driven copies let the model's segment
lengths survive playback. `ppretarget.py` gives each PP bone PP's rest ORIENTATION while retaining
the MODEL's own segment lengths. To keep the mesh attached to that new rest pose, it:

- unposes and reposes vertices;
- rewrites inverse bind matrices;
- appends PP-only nodes to `skin.joints`;
- merges multiple skinned meshes into one because the importer refuses more than one;
- drops attributes that only SOME primitives carry.

Placement has two passes. Pass 1 places PP bones and fits the swing chain. Pass 2 places every
non-PP bone, including hair, face, eyes and twist bones, by riding its parent's swing; the bone's
frame and flesh therefore receive the same rotation. Skipping pass 2 twisted the neck 88.3 degrees
in an earlier build. With it, the head sits 3.65 degrees off, versus 39.14 degrees measured on a
VANILLA soldier, which is within the rig family's own geometry.

### 4. (RECOMMENDED) compress the animation half with `ppzip.py`

```powershell
python tools\ppzip.py tiffany_cox_ppfit.glb tiffany_cox_ppzip.glb
python tools\ppzip.py --selfcheck        # the tool's own synthetic regression test
```

**Stage 4's output is the model you ship.** Copy `tiffany_cox_ppzip.glb` to
`Content\Models\soldier.glb` and go to the next section.

This is compression, not trimming, and the difference is the whole point. `ppzip.py` never touches
the clip list; it rewrites how the SAME curves are stored:

- **constant curves collapsed.** 14,283 of the 27,284 rotation channels hold one quaternion for the
  whole clip and spend up to 805 keys restating it. Each becomes its two endpoint keys, which is
  EXACTLY lossless — the importer resamples every curve onto a uniform grid anyway
  (`src\Import\GlbReader.cs:1084-1112`), and a two-key constant samples to the same value at every
  frame a 805-key constant did.
- **rotations as normalized int16.** Quaternion components are already in [-1, 1], so the worst
  measured round-trip error is **1.526e-05** per component — about 0.002 degrees — for half the
  bytes. `GlbReader.cs:2099-2108` already decodes normalized SHORT on every accessor path, so
  nothing is needed on the C# side. Translation is NOT quantised (metres, unbounded); its measured
  error is 1.4e-17.
- **no resampling.** Every sampler stays at its authored 120 Hz. Halving the rate would be the
  biggest saving on paper and the only one a player could see (up to 0.023 of a quaternion
  component, ~2.6 degrees), so it is deliberately not offered.

Measured on the shipped model:

| Artefact | Before | After | Reduction |
|---|---|---|---|
| `.glb` | 104,511,576 B | 36,254,816 B | **-65.3%** |
| of which animation | 89,281,672 B | 22,764,800 B | -74.5% |

All **300 clips** and all **29,082 channels** survive, and the demos ship exactly this build.

### 5. (OPTIONAL, and not for a playable character) trim clips with `ppslim.py`

**You do not need this stage.** The size question is answered by stage 4, which drops nothing.

!!! danger "Do not trim a playable character"

    A soldier has to be able to play the WHOLE game — any weapon, any stance, any situation — and
    every clip the game may ask for has to be in the file. Drop one and the character stalls the
    moment it reaches that state. The measured symptom is not a missing animation: it is an **aimed
    pistol shot that never returns, leaving the camera frozen on the actor forever**, because the
    ability waits on an animation event that no clip will ever fire (`AnimEventReceiver.cs:100,126`).
    The full 300-clip model is what you ship and that is CORRECT. **Size is not a reason to trim** —
    stage 4 already answers it, taking the same 300 clips from 104,511,576 B to 36,254,816 B.

    What `ppslim.py` is genuinely for is a non-playable prop or a bench model whose complete state
    list you KNOW, and for `--list`, which answers the size question without touching the file.

The rest of this section is the reference for that optional case. `ppslim.py` removes selected clip
families, garbage-collects orphaned accessors and bufferViews, and rewrites a compacted BIN chunk.
Its selection and validation flags:

- `--list` shows per-family owned bytes;
- `--drop` / `--keep` take regexes matched against FULL clip names, not family names — "family" is
  only the grouping `--list` prints. `--keep` wins over `--drop`;
- `--require` takes a comma-separated list of EXACT clip names and fails the run if any of them did
  not survive the trim. It knows nothing about roles, so `walk` or `death` are not accepted as
  shorthand — name the clips;
- `--selfcheck` runs the tool's own built-in synthetic regression test and exits without reading
  your `.glb`. It proves the tool works; it does NOT validate your trimmed output. Check that
  separately with `--list` and `--require`.

If you do run it on something that is not a playable character, list the families first, then trim
and guard in one run:

```powershell
python tools\ppslim.py --list tiffany_cox_ppfit.glb
python tools\ppslim.py tiffany_cox_ppfit.glb <out>.glb `
    --drop "<families you are certain that model never reaches>" `
    --require "HL_IdleAlert_NoGun,MV_RunFwd_Loop_NoGunA,FF_Punch_ShotLoop,HL_Death_AR,HL_HurtFront_AR"
```

`--require` must list exactly the clip names the manifest maps — keep the two in step, or the trim
can drop a clip the manifest still points at. It is a guard on the ROLES, and note what it cannot
guard: every clip the game reaches WITHOUT a manifest role, which for a soldier is most of them.

## What the clip budget actually looks like

Animation data, not textures, dominates this character bundle. The measured BIN budget of the
300-clip model, before stage 4:

| Category | Bytes | After `ppzip.py` |
|---|---|---|
| Animations | 89,281,672 | 22,764,800 |
| Mesh | 4,221,280 | unchanged |
| Images | 2,948,683 | unchanged |

All 6 embedded images are already 1024x1024, totalling 2,948,683 B. Textures are NOT the lever;
animation is, and `ppzip.py` pulls it without removing anything. Deleting clips is the OTHER lever on
the same bytes, and it is the one you must not pull on a playable character. For the record, dropping
143 of the 300 clips measured:

| Artefact (both measured before stage 4) | Full | Trimmed | Reduction |
|---|---|---|---|
| Demo bundle | 50,606,928 B | 24,522,403 B | -51.5% |
| Source `.glb` | 104,511,576 B | 46,684,104 B | -55.3% |

**That trimmed build is not what the demos ship, and this table is here to close the question rather
than to invite it.** Half the file size bought a soldier who freezes on an aimed pistol shot.

`CreatureRoles.cs` is the role safety net, and it is worth being precise about what it covers:

- **Must be filled:** walk, idle, attack, death, reaction.
- **Allowed to stay empty:** jump, climb, ranged — these are substituted automatically.

That substitution is about MAPPINGS, not about the file. A role left unmapped is filled from another
clip; a clip that is not in the `.glb` at all cannot be substituted by anything.

## Add the manifest block and bake

Put the `.glb` from stage 4 under `Content\Models`, then use the existing `creature` block in
`ppcontent.json`. Set `"startingRoster": true` inside that block. Boolean `true` is the canonical opt-in; the current parser also accepts the string `"true"`. Absent or false leaves the campaign unchanged.
The donor selects the class:

```json
"donor": "PX_SniperStarting_TacCharacterDef"
```

That donor produces a sniper through data, not code. Follow the manifest scaffold and role-mapping
loop in the [creature recipe](creature.md), then bake the project with `ct_project`. On the corrected
multi-submesh path, a successful bake reports:

```text
model '<name>' kept 7 material(s) as 7 submesh(es)
```

`BundleBaker` previously passed one material id through its rigged branch, producing a one-entry
`m_Materials` array for a multi-submesh mesh. Unity then drew only submesh 0, so dressed characters
appeared NAKED. The branch now writes one entry per submesh: the 7 material(s)/7 submesh(es) case
stays intact.

The complete manifest for the worked example is the
[HumanoidSoldier demo](https://github.com/UberMorgott/PhoenixPoint-Mod-ContentTool/tree/main/demos/HumanoidSoldier)'s
own `ppcontent.json` — that file is the entire mod. To bake and run it:

```text
ct_project HumanoidSoldier
```

Then restart and start a NEW campaign: `startingRoster` is read when the initial squad is created,
so an existing save will never show the soldier.

## Load the soldier, and how its weapons sit in its hands

After the bake, load the mod in game and confirm that the soldier is present and dressed.

An armed foreign model will not hold a gun the way a vanilla soldier does: it keeps its OWN socket
placement, measured at ~0.49x PP's distance from the hand. Where that socket sits is decided
OFFLINE, by the `weapon_attach_r` -> `gun_point_hand` rename in step 1 — it is a property of the rig
you shipped, not a value the game stores per weapon.

The in-game fit workbench (Ctrl+Alt+B) does NOT fix this case. It tunes a custom weapon model
declared in a `weapons[]` manifest row, and a soldier of this kind carries PP's own shipped weapons,
which the workbench treats as viewable but never tunable: a shipped weapon "has no ppcontent.json
row, so there is nothing to tune and nowhere to save" (`src\Dev\FitBench.cs:53-55,1504-1506`), and
its axis buttons are not drawn at all. You can still put a shipped weapon in the hand there for
COMPARISON, which is the useful thing the workbench does for this track: it shows you how far off
the socket is.

**There is no supported way to correct that socket after the fact, and this is stated rather than
solved.** `ppskel.py` renames the source rig's `weapon_attach_r` node in place, and `ppretarget.py`
then keeps every bone's OWN segment length by design — including this one, which its report names as
an outlier at **0.487x** the metre factor (`ppretarget-report.json`, `perBoneLengthOutliers`, beside
`gun_point_shield` at the same ratio). Neither script takes a per-bone override, and no other tool in
`tools\` reads or writes a socket offset; `ppskel-bone-map.json` is a generated report and changes
nothing. So the only place the placement can change is the SOURCE rig, before step 1 — move
`weapon_attach_r`/`_l` there and re-run the pipeline. That path is untested in this repository: no
measurement of a moved socket exists, so treat it as the known lever, not a verified recipe.

The workbench's saved fits, where they apply, live in `ppcontent.json`. A deploy overwrites the
installed copy with the repository copy, so preserve fitted values there before deploying again. See
the [weapon fit workbench](weapon.md#fit-the-model-in-the-workbench).

## Exact roster identity is not guaranteed by `StartingSquadTemplate`

`GameDifficultyLevelDef.StartingSquadTemplate` (`GameDifficultyLevelDef.cs:37`) is walked by
`GeoPhoenixFaction.CreateInitialSquad` (`GeoPhoenixFaction.cs:1964-1976`), and every entry is boarded
on `Vehicles.First()`. Phoenix has exactly ONE starting aircraft, so adding a unit at campaign start
and putting it in the starting Manticore's crew reach the SAME seam.

Appending to `StartingSquadTemplate` does NOT guarantee your exact soldier.
`GeoscapeTutorial.InitSquad` reads that array for its LENGTH only (`GeoscapeTutorial.cs:313`) and
fills the gap from a fixed `AdditionalSoldierTemplate` (`GeoscapeTutorial.cs:318-323`). Under a
tutorial start this can silently give you one extra generic soldier instead of the unit from your
mod. TFTV rewrites the array too.

ContentTool therefore boards the built def post-hoc through `CreatureBuild.JoinPlayerVehicle`
(`CreatureBuild.cs:1788`), which is exact in both starts.

## Starting aircraft capacity is not enforced

`GeoVehicle.AddCharacter:759-764` discards its space sum. If many mods opt into the starting roster,
they can overfill the aircraft. ContentTool reports used/max as a warning; it does not refuse the
addition.

## The pipeline does not repair the demo's source art

The demo model's hair, teeth+lashes and cloth-layering defects are SOURCE-ASSET problems, not
retargeting artefacts. The pipeline does not fix them:

- **Hair** renders as an opaque grey blob because its greyscale strand tile has no alpha and the
  material declares no `alphaMode`.
- **Teeth and lashes** draw white because their materials are factor-only and have no textures.
- **Dark patch** reads through the top where the body mesh layers under the cloth.

To put this same model on a character the game already ships instead of adding a new one, continue to
[Give a shipped character a different body](replace-character-body.md).

For events, donors, def cloning and DLL-based injection when `startingRoster` is not enough, return
to the [creature recipe](creature.md). The
[CustomCreature demo README](https://github.com/UberMorgott/PhoenixPoint-Mod-ContentTool/tree/main/demos/CustomCreature) contains the engine-level wiring.
