# Build a creature from its own rig and clips

A custom creature is not a standalone actor imported from a GLB. ContentTool clones a shipped
`TacCharacterDef`, installs your rig and clips, and rewrites the subset of animation actions it can
support. Read [the clone model](../SHIPPING-A-CONTENT-MOD.md#1-understand-the-clone-model) before
choosing a donor or source model.

The content engine builds the def. Your mod's DLL decides where that def enters the game: starting
squad, faction deployment, an ability, or another campaign rule.

## 1. Prepare the project

Start with one binary glTF model:

```text
MyCreature\
  meta.json
  ppcontent.json
  Content\
    Models\
      cyborg_spider.glb
```

The GLB must contain a skeleton, a skinned mesh below its rig root, and clips for at least walk,
idle, attack and death. A glTF file has no Unity `Animator` component: during import ContentTool
creates the `Animator` on the generated rig-root GameObject, then installs the donor's controller
there. Bone names and count are yours; ContentTool does not retarget them to the donor.

With exactly one `.glb` under `Content\Models\`, `creature.model` may be omitted and that file is
selected. With none or several and no `model`, the build is refused by name and nothing changes. An
explicit value is the file stem, matched case-insensitively; a stem that is not present is also
refused. The demo spells out `"model": "cyborg_spider"` even though it has only that one file.

Three terms used in this guide are practical measurements, not requirements from an assumed art
tool. A **bind pose** is the skeleton's reference pose that attaches mesh vertices to bones before
animation. **Root motion** is translation or rotation stored on the animation's root rather than
only in moving limbs; ContentTool reads that displacement for movement and climbing. **One tile**
means roughly one cell of Phoenix Point's tactical grid. The first bake prints a candidate scale
that makes the measured model about that wide; it does not assume that one GLB unit equals one tile.

Declare the route with a complete initial manifest:

```json
{
  "id": "yourname.mycreature",
  "bundle": "MyCreature.bundle",
  "creature": {}
}
```

Close or save this file in your editor, then run:

```text
ct_project MyCreature
```

The first run measures the model, prints its bone count, dimensions, one-tile scale candidate and
origin-to-lowest-vertex distance, and rewrites the `creature` block with every bakeable clip name.
It then refuses while required roles remain empty. That refusal is the intended scaffold workflow.

## 2. Map clips and measurements

Assign `walk`, `idle`, `attack`, and `death`. Optional roles are `jump`, `reaction`, `ranged`, and
`climb`. Clip names on the left must remain exactly as spelled in the GLB; the role on the right is
ContentTool's vocabulary.

`ranged` appears twice on purpose in the completed manifest below. Inside `creature.clips` it is a
clip **role**; the creature-level `ranged` key names a shipped `WeaponDef` to clone as the ranged
attack.

This is the complete working shape used by the spider demo:

```json
{
  "id": "morgott.demo.customcreature",
  "bundle": "CustomCreature.bundle",
  "scale": 0.008,
  "play": "Spider_Idle",
  "loop": "Spider_Idle, Spider_Walk",
  "creature": {
    "clips": {
      "Spider_Walk": "walk",
      "Spider_Idle": "idle",
      "Spider_Idle_long": "",
      "Spider_Damage": "reaction",
      "Spider_Attack_1": "attack",
      "Spider_Attack_2": "ranged",
      "Spider_Death": "death"
    },
    "events": {
      "walk": "SwarmerStep_EventDef 0.15, SwarmerStep_EventDef 0.65",
      "attack": "ActionDo 0.4054, ShootShot 0.4865, ActionEnd 0.8378",
      "ranged": "ActionDo 0.2286, ShootShot 0.5429, ActionEnd 0.9143",
      "death": "Ragdoll 0.9"
    },
    "name": "Spider",
    "model": "cyborg_spider",
    "donor": "Swarmer_TacCharacterDef",
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

With that already-complete manifest, a live bake printed these exact lines:

```text
clip-names PASS "loop" names 2 clip(s) and "play" names 1 of the 7 this project bakes
creature-measure 'cyborg_spider': 49 bone(s), spans 120.435 x 64.237 x 105.578 file unit(s) about 0,29.979,-3.649; a tile is 1.0, so "scale": 0.008 makes it one tile across (this project declares 0.008). Its origin is 32.118 above its lowest vertex on +Y, which is "creature": { "lift" } if the model is centred rather than standing on its feet.
creature-clips 'cyborg_spider': 7 animation(s) in the file -> Spider_Walk, Spider_Idle, Spider_Idle_long, Spider_Damage, Spider_Attack_1, Spider_Attack_2, Spider_Death
creature-events PASS every blocking event the game waits for is declared
creature-roles PASS "clips" maps 6 of 7 discovered animation(s); every required role (walk, idle, attack, death) is mapped
ct_project: ALL PASS - <project>\Dist\CustomCreature.bundle
```

No `creature-scaffold` line appears because the manifest already contains every discovered clip.
The roles line says `6 of 7` because `Spider_Idle_long` is deliberately mapped to an empty role.
The printed `32.118` origin distance is a candidate `lift` for a model centred on its origin, not a
value to copy automatically. The demo's author judged the model in game and chose `"lift": 2.1372`;
you must make the same visual decision for your model.

Use the measurements as candidates when completing the file:

- `scale` is top-level uniform rig scale. The bake prints the value that makes the model about one
  tile across.
- `up` is the model's imported up axis. ContentTool rotates that vector to world up.
- `lift` is the author-chosen file-unit distance from origin down to the lowest vertex, applied after
  scale; the printed value assumes a centred model and need not be the value you choose.
- `play` selects the imported Animator's starting clip. `loop` must include the mapped walk and idle.
- `pace` is tiles per second. Omit it for the shipped pace; set `0` to preserve the authored timing.
- `speed` is Action Points—how far the unit can move in a turn—not visual movement speed.
- `health` zero keeps the donor's value. Set it deliberately; a donor with zero strength produces a
  creature that enters play dead.

Run `ct_project MyCreature` again. Fix the first refusal and repeat until the bundle bakes.

## 3. Choose the donor by capability

`Swarmer_TacCharacterDef` is the default because it is one tile and contains the required navigation,
animation, addons and melee structure. A facehugger has no body parts and cannot supply the required
melee weapon. A large donor can bring a multi-tile footprint or demolition machinery into a small
model.

Ask the live repository for candidates:

```text
ct_list defs Swarmer TacCharacterDef
ct_list defs Crabman TacCharacterDef
```

Choose based on component and combat behavior. Donor appearance and donor clip names do not help:
the donor's clips are overridden. Its controller states, agent-related structure and abilities are
the ceiling.

ContentTool separately selects a shipped one-tile reference unit for agent type, cursors and move
highlight. Its `Walkable*` nav area supplies the ground mask because area names are scoped to that
agent type. Do not treat “reference” as another manifest key; it is an internal, copied provenance
that prevents a donor's differently scoped nav mask from immobilizing the clone.

## 4. Author events at the real frames

Phoenix Point waits for named events. The attack role normally needs `ActionDo`, `ShootShot`, then
`ActionEnd`; death needs `Ragdoll`. Values are fractions of that clip's duration. Put `ShootShot` at
the actual contact/projectile frame and `ActionEnd` when the pose is ready to leave.

Missing event declarations are warnings during bake, but a live ability can wait ten seconds for
each missing blocking event. That looks like a hung game. The event order in the string matters, and
two waits should not share one timestamp.

See [Phoenix Point animation contract](animation-reference.md) for every slot, event and parameter.

## 5. Traversal

### Why custom creatures used to stall

The pathfinder grants traversal by navmesh areas. An empty animation slot does not keep an actor off
a link: the game can emit an L-shaped fallback using the run loop, wait up to five seconds at each
point for a clip identity that never becomes current, fall back to high idle, and continue. That is
the stepped wall motion and long pause the earlier implementation produced.

There was a second trap: nav-action field names and controller clip names are different
vocabularies, and some nav fields are three-part sequences while others are single clips. Filling
only sequence fields by a nav def's apparent name left real controller states untouched.

### What ContentTool does now

ContentTool reads the rig's controller, synthesizes a start, looping pure-up climb and stop from the
creature's own walk, and maps them into the controller and nav-action fields together. It covers 20
slots across roof drops, low-obstacle roof drops, ladder climbs, low-obstacle climbs, jump-over
families and the ascent of one whole level. A nav area is added only when its real controller states
and required clip parts are filled.

For the current one-tile Humanoid creature this produces:

```text
WalkableHumanoid, RoofDrop, LowObstacleRoofDrop, ClimbLadder, LowObstacle, Jump, JumpUpOneLevel
```

That is a shipped soldier's mask plus `JumpUpOneLevel`, which the shipped Humanoid aliens
`Crabman_NavigationDef` and `HumanoidGuardian_NavigationDef` also carry. By comparison, shipped
ground-only units carry one
scoped ground area: `Sentinel_Terror` has `WalkableHumanoid`, `Chiron_FireWorm` has
`WalkableMedMonster`, `Queen_Heavy` has `WalkableBigMonster`, and `PX_Scarab` has
`WalkableArmadillo`. Shipped climbers add only the link areas they can actually traverse.

The engine derives climb height from baked root motion and repeats the loop for the variable
remainder, so one set of clips crosses different obstacle heights.

`climbPitch` rotates the creature nose-up during synthesis. The spider uses `90`: on a wall, its
ordinary leg cycle reads as climbing. `0` is the honest default for a biped, whose walk would look
wrong rotated onto its face. The general technique is: **when the animation you need does not exist,
rotate the model so an animation you do have reads correctly.**

If your GLB contains authored climbing art, map that clip to `climb`. It wins over synthesis and its
own root offset is used. The mapped clip must rise; bake refuses a non-rising climb rather than
shipping a sliding creature.

### Measured result and ceiling

The spider crossed a 4.93-unit rooftop rise over 24.90 units of path in 3.94 seconds and a 5.00-unit
drop in 2.68 seconds, ending at the ordered point with zero animation timeouts. Before the traversal
fix, a shorter five-tile feature took 13.09 seconds and emitted three timeout lines; a shipped
Fireworm took 3.25 seconds on that control route.

The synthesized walk cadence is still not authored climb art. All synthesized parts carry upward
root motion even when the family is used for descent; the shared controller mapping makes that
complete without stalls, but bespoke art is the visual upgrade.

Controller-state classification currently uses keywords in the controller's overridable clip names
because shipped controllers cannot be fully enumerated offline. A future game controller with a new
naming vocabulary can therefore be refused until ContentTool's family map is updated.

Climbing up one full level is available. It was refused for a season over a spelling: the state was
asked for as *jump* + *level* after the def field's name, while the shipped `HumanoidAnimatorLOC`
really carries `MV_JumpUpOneFloor_Start_Placeholder`, which says **floor**. The area was read the
same way, off a shipped human's list. The `JumpUpOneLevel` slot takes the same synthesized rising
clip the vault families take, so no extra clip is needed from you and nothing extra is mapped. Three
link segments carrying `JumpUpOneLevel` were crossed in 2.36 seconds with zero animation timeouts,
and a 4.93-unit ascent on another save took 3.93 seconds, also with none. One proof is still
outstanding: both measured crossings arrived on a downward order, so an arrival in the up direction
specifically over a jump-up link has not yet been demonstrated. `Mount`, `Ram`, `JetJump` and
`FallNoSupport` remain excluded because they are abilities or hazards rather than ordinary path
links.

## 6. Hitboxes and ranged attacks

By default ContentTool measures enabled renderers and creates a character-layer box collider plus a
camera collider for hover/click. Set `hitBones` to a comma-separated bone list for sphere colliders;
`hitRadius` controls their radius. Set `colliders` to `off` only when your own behavior creates and
verifies them. `aim` names a bone for the aim marker.

For a ranged creature, set `ranged` to a shipped `WeaponDef` and optionally set `aiAction`,
`shootBone`, and `accuracy`. ContentTool clones that weapon as a second attack. The `ranged` clip role
keeps its animation separate from melee. Test both attacks because their required events and target
frames can differ.

## 7. Build the def from your DLL

`ppcontent.json` cannot decide where a new actor belongs in the campaign, so a creature requires a
real DLL. Start from the complete [project, references and `ModMain` skeleton](behavior-dll.md), and
read its [profile-wide `Managed\` module warning](behavior-dll.md#managed-module-load-failure)
before adding references. Substitute `MyCreature` for `MyMod` in that project file.

`CreatureBuild` has two public members: `public static TacCharacterDef Build(string modDir,
Action<string> log)` and `public static void JoinPlayerVehicle(TacCharacterDef def, string who)`.
Everything else on that class is internal.

`Build` registers and returns the cloned def. On failure it logs and returns `null`; it never throws.
`JoinPlayerVehicle` finds the current geoscape and takes the Phoenix faction's first vehicle. When
that vehicle does not already have a unit with the same `TemplateDef`, it creates a character from
`def` for the Phoenix faction using the current difficulty's starting-squad generation parameters,
then adds it. It reads the roster back and logs `PASS` or `FAIL`; `who` is only a label for that log.
It safely does nothing when no geoscape or vehicle exists.

This complete entry point uses the demo's starting-squad policy:

```csharp
using System.Reflection;
using HarmonyLib;
using Morgott.ContentTool.Tactical;
using PhoenixPoint.Geoscape.Levels;
using PhoenixPoint.Geoscape.Levels.Factions;
using PhoenixPoint.Modding;
using PhoenixPoint.Tactical.Entities;

namespace YourName.MyCreature
{
    public sealed class MyCreatureMain : ModMain
    {
        internal static TacCharacterDef Spider;

        public override bool CanSafelyDisable => true;

        public override void OnModEnabled()
        {
            Spider = CreatureBuild.Build(Instance.Entry.Directory, m => Logger.LogInfo(m));
            ((Harmony)HarmonyInstance).PatchAll(Assembly.GetExecutingAssembly());
        }
    }

    [HarmonyPatch(typeof(GeoPhoenixFaction), nameof(GeoPhoenixFaction.CreateInitialSquad))]
    internal static class SpiderJoinsNewCampaign
    {
        private static void Postfix()
        {
            CreatureBuild.JoinPlayerVehicle(MyCreatureMain.Spider, "CreateInitialSquad");
        }
    }

    [HarmonyPatch(typeof(GeoscapeTutorial), "InitSquad")]
    internal static class SpiderJoinsTutorialSquad
    {
        private static void Postfix()
        {
            CreatureBuild.JoinPlayerVehicle(MyCreatureMain.Spider, "Tutorial.InitSquad");
        }
    }
}
```

`OnModEnabled` first builds the def, then applies this assembly's two Harmony postfixes. The normal
new-campaign path calls `GeoPhoenixFaction.CreateInitialSquad`. The tutorial replaces that builder
with its private `GeoscapeTutorial.InitSquad`, so it is patched by name. Both postfixes are required
to put the creature in the player's vehicle in both campaign starts; changing
`StartingSquadTemplate` is insufficient because the tutorial reads it only for its length. For a
different campaign design, keep the `Build` call but replace these two injection seams with your own.

`meta.json` must name the DLL you built:

```json
{
  "ID": "yourname.mycreature",
  "AssemblyName": "MyCreature.dll",
  "Version": "1.0.0",
  "Author": [
    { "Key": "English", "Value": "Your Name" }
  ],
  "Name": [
    { "Key": "English", "Value": "My Creature" }
  ],
  "Description": [
    { "Key": "English", "Value": "Adds a custom creature. Requires ContentTool." }
  ],
  "Dependencies": [
    "com.morgott.ContentTool"
  ]
}
```

`ct_package` finds the already-built DLL named by `AssemblyName`; it never compiles. Follow the
[build-to-mod-folder and restart loop](behavior-dll.md#name-the-real-dll) after every code change.

## 8. Test and ship

Test from a new campaign if your behavior adds the unit only during squad or storage creation.
Verify all of these in a tactical mission:

- selection, hover and aim targeting;
- several ground moves, corners, climb, drop and low-obstacle links;
- melee and ranged actions, including their event timing;
- reaction, damage and death;
- save, reload and mod disable behavior appropriate to your injection seam.

Then run the final bake and package:

```text
ct_project MyCreature
ct_package MyCreature
```

Install the packaged folder as a player and repeat the test after a cold restart. Ship the model only
when its license permits redistribution and include the required attribution in `SOURCES.md`.
