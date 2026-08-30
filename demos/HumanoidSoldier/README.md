# ContentTool Demo: Humanoid Soldier

A foreign humanoid model — downloaded, sculpted, generated — with its **own skeleton** and **no clips
of its own**, becomes a **playable soldier** in the player's roster and aboard the starting Manticore,
with **zero C#**. Everything below is manifest data plus three offline tools.

It is the counterpart of [CustomCreature](../CustomCreature/README.md), which teaches the opposite
case: a creature that ships its own clips and needs a small DLL. The engine-level explanation of both
(def cloning, animation events, root motion, controller override) lives there and is not repeated
here. The full recipe, with the measurements behind it, is
[docs/guides/humanoid-soldier.md](../../docs/guides/humanoid-soldier.md) — read that page first; this
folder is its worked instance.

## What ships, and what you supply

| | |
|---|---|
| **Ships here** | `meta.json`, `ppcontent.json` (the whole wiring), this README, **and the model** — `Content\Models\soldier.glb`, 36 254 816 B, all 300 retargeted clips, already through the toolchain and through `tools\ppzip.py` |
| **You supply** | nothing, to run it as shipped. To make it YOUR character, your own `.glb` under the same stem, taken through the same three tools |
| **Built on your machine** | `Dist\HumanoidSoldier.bundle`, by `ct_project HumanoidSoldier` in the game console |

The model's own record — title, author, source — travels inside the `.glb`, in its glTF `asset`
block; read it out of the file if you want it. What is NOT here is the raw download and the baked
bundle: the first is input the pipeline consumes, the second is a build output that belongs on your
machine. The demo is still a **recipe** — the three commands below are the point of it — it just also
ships a worked result you can bake and see.

## Run it

Nothing to download and nothing to build offline — the model is here:

```text
ct_project HumanoidSoldier
```

Restart, then start a **NEW campaign**: `startingRoster` is read when the initial squad is created,
so an existing save will never show the soldier.

Its mirror image is [ReplaceCharacterBody](../ReplaceCharacterBody/README.md), which puts the SAME
model on an EXISTING named character instead of adding a new one. ADD and REPLACE, one manifest key
apart.

## Enabling it with no model in place

If you delete the model to drop your own in, nothing breaks and nothing throws. The mod loads, finds
no bundle, and says so once
(`src\Tactical\CreatureBuild.cs:185-189`):

```text
ct_creature VOID '<Mods>\HumanoidSoldier\Dist\HumanoidSoldier.bundle' does not exist - run `ct_project HumanoidSoldier` in the console once, then restart. Nothing was changed.
```

Baking before the `.glb` is in place is equally quiet — `ct_project HumanoidSoldier` returns
(`src\Bake\ProjectBake.cs:82-85`):

```text
nothing to bake - put .png/.jpg under Content\Textures\, .glb under Content\Models\ or .wav under Content\Audio\
```

And a `.glb` whose stem is not `soldier` is refused **by name** rather than picked at random
(`src\Tactical\CreatureBuild.cs:695-696`):

```text
ct_creature FAIL ppcontent.json "creature": "model" names 'soldier' but Content\Models\ holds [<yours>]. Nothing was changed.
```

## The toolchain — the four commands

> **The model keeps ALL 300 of PP's retargeted clips, and for a playable character that is not
> negotiable.** A soldier reaches states no manifest role names — aiming, reloading, crouching, every
> weapon family — and a clip the `.glb` does not contain cannot be substituted by anything. Dropping
> one does not produce a missing animation; it produces an **aimed shot that never returns and a
> camera frozen on the actor forever**, because the ability waits on an event no clip will fire.
> **Size is not a reason to trim**, and there is deliberately no trimming step below — the size
> question is answered by COMPRESSING the same 300 clips instead (`ppzip.py`, step 4), which takes
> the shipped model from 104,511,576 B to **36,254,816 B (-65.3%)** with every clip still in it.

All four run offline, before `ct_project`, from the `ContentTool` root. **Read this first:**
`ppskel.py` and `ppretarget.py` take no arguments — their input and output paths are constants at the
top of each file (`ppskel.SRC` / `ppskel.DST` / `ppretarget.DST`), and the foreign-to-PP bone mapping
is the `RENAME` table inside `ppskel.py` itself (`ppskel.py:44-78`). `tools\ppskel-bone-map.json` is a
GENERATED report of the renames that were applied (written by `ppskel.py:339`), **not an input the
tool reads** — retargeting a different humanoid means editing `RENAME` and those path constants in
the script, not editing that JSON. There is no generic CLI, because there is no generic rig.

**The order below is not interchangeable.** `ClipCensus --export` runs BEFORE the repose, because
`ppretarget.py` READS its output (`ppretarget.py:48` points at `tools\pp-clips.json`); reposing first
simply fails to find the clip table.

```powershell
# 1. rename the rig onto PP's bone paths (SRC/DST are constants in the file, RENAME is the map)
python tools\ppskel.py
python tools\ppskel.py --check          # every PP path resolves and the skin survived
python tools\ppskel.py --rest > tools\pp-rest.tsv

# 2. rewrite PP's shipped clips rotation-driven (strips the curves that pin PP's proportions)
dotnet run --project tools\ClipCensus -- --export <classdata.tpk> <shipped bundle> tools\pp-rest.tsv tools\pp-clips.json

# 3. PP's rest ORIENTATION, your model's OWN segment lengths, with step 2's clips embedded
python tools\ppretarget.py
python tools\ppretarget.py --check      # rest angles, segment lengths, no pinning curves left
python tools\ppretarget.py --selftest   # the check itself, against five negative controls

# 4. RECOMMENDED: compress the animation half. Same 300 clips, same 29,082 channels, 65% smaller.
python tools\ppzip.py tiffany_cox_ppfit.glb tiffany_cox_ppzip.glb

# ...and that IS the model. Copy it in, all 300 clips, and bake.
copy tiffany_cox_ppzip.glb demos\HumanoidSoldier\Content\Models\soldier.glb
```

`--check` and `--selftest` are the flags that matter. `--check` is what tells you the rename actually
bound instead of silently producing a model that loads and never moves; `--selftest` proves the
retarget itself against five negative controls.

`tools\ppzip.py` (step 4) and `tools\ppslim.py` both make the file smaller and they are NOT
interchangeable. `ppzip.py` is RECOMMENDED for every model, playable or not: it rewrites how the SAME
curves are stored — constant rotation curves collapsed to their two endpoint keys (exactly lossless)
and rotations kept as normalized int16 (worst measured error 1.526e-05 per quaternion component,
~0.002 degrees). Nothing is dropped and nothing is resampled: 300 clips in, 300 clips out, all 29,082
channels, animation 89,281,672 B -> 22,764,800 B and the file 104,511,576 B -> 36,254,816 B (-65.3%).

`tools\ppslim.py` is the dangerous one and is deliberately NOT in the list. It drops clip families,
which is the one thing you must not do to a character somebody will play — see the box above, and the
tool's own warning in its docstring. It is for non-playable props and for `--list`, which answers
"what is all this weight?" without touching the file.

Then, in game: `ct_project HumanoidSoldier`, restart, and start a **new campaign** — `startingRoster`
is read when the initial squad is created.

## The manifest, key by key

`ppcontent.json` is the entire mod. The parts that carry the demo:

- `"model": "soldier"` — which `Content\Models\*.glb` is the character, by file stem.
- `"clips"` — five **Phoenix Point** clip names mapped to roles. They are PP's own clips, riding in
  your `.glb` after step 3; nothing here is art you authored. Roles `walk, idle, attack, death,
  reaction` must be filled — `jump, climb, ranged` must be left empty (`src\Tactical\CreatureRoles.cs`).
- `"events"` — the blocking animation events the game waits for, as fractions of the clip.
- `"donor": "PX_SniperStarting_TacCharacterDef"` — the class is the donor template's. A sniper here;
  swap the def name for a different class. Data, not code.
- `"startingRoster": true` — the whole opt-in for boarding the starting aircraft. Only the literal
  `true` opts in.

## After the model first loads: where the weapon sits

A foreign model keeps its own socket placement — the `gun_point_hand` node is the source rig's own
`weapon_attach_r`, measured at ~0.49x PP's distance from the hand (`ppretarget-report.json`,
`perBoneLengthOutliers`). That placement is decided OFFLINE, by the rename in step 1 and by
`ppretarget.py` preserving the model's own segment lengths; it is a property of the rig you shipped,
not a value the game stores per weapon.

**The fit workbench (Ctrl+Alt+B) cannot correct it.** It tunes a custom weapon model declared in a
`weapons[]` manifest row, and this soldier carries PP's OWN shipped weapons — which the workbench
treats as viewable but never tunable: a shipped weapon has no `ppcontent.json` row, "so there is
nowhere for a save to go", and its axis buttons are not drawn at all (`src\Dev\FitBench.cs:53-55`).
What it is still good for here is COMPARISON: put a shipped weapon in the hand and see how far off
the socket is. See
[docs/guides/humanoid-soldier.md](../../docs/guides/humanoid-soldier.md#load-the-soldier-and-how-its-weapons-sit-in-its-hands).

## Checked offline

`tests\ObjCodecTests\StartRoster.cs` reads this folder's `ppcontent.json` and asserts the opt-in, the
donor, that every blocking role is mapped, and that the model the manifest names is actually here —
so the demo cannot rot into a manifest that would board nobody, or into a stem no file answers to.

```powershell
dotnet run --project tests\ObjCodecTests
```
