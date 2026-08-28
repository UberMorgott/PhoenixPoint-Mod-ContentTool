# A new creature — a downloaded model, in your squad, with its own skeleton and its own animations

The tallest rung. A model downloaded from the internet, untouched, becomes a playable unit: its own
skeleton is the one that came inside the file, and the clips it plays while it walks, attacks,
flinches and dies are the clips that came inside the file — driven by Phoenix Point's own animation
system, its own anim-action defs and its own path processor.

**This route needs a DLL**, but a tiny one. Cloning the donor, remapping the controller, wiring the
clip slots, stamping the animation events and orienting the rig are the same for every creature mod
anyone will ever write, so all of that lives in the ContentTool engine. What is left for you is the
two things that are genuinely a **choice**: the numbers in `ppcontent.json`, and the decision to put
the unit somewhere — the demo puts it in the player's starting squad, which is ~36 lines.

Read [Animated models](animated-models.md) first if you have not: this is the **with an adapter** case,
and the page explains why an adapter is unavoidable rather than a missing feature.

## 1. The folder

```text
CustomCreature\
  meta.json                            "AssemblyName": "CustomCreature.dll"
  ppcontent.json                       THE FILE YOU EDIT - model, clip->role map, event times, stats, scale
  Content\
    Models\
      cyborg_spider.glb                the download, 1 481 244 B, UNMODIFIED
    Textures\
      cyborg_spider.png                OPTIONAL: same stem -> the model's _MainTex slot
  Dist\
    CustomCreature.bundle              written by `ct_project` - COMMIT AND SHIP IT
  src\CustomCreatureMain.cs            36 lines: build the creature, put it in the squad
  CustomCreature.csproj                builds the line above - see "The DLL" below
  CustomCreature.dll                   the built output, staged by package.ps1
  SOURCES.md                           attribution. CC BY REQUIRES it. Read it before swapping models.
```

## 2. The manifest, field by field

The whole of a working one, with the fields that are not `"creature"` shown at the top level where
they belong:

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
      "walk":   "SwarmerStep_EventDef 0.15, SwarmerStep_EventDef 0.65",
      "attack": "ActionDo 0.4054, ShootShot 0.4865, ActionEnd 0.8378",
      "ranged": "ActionDo 0.2286, ShootShot 0.5429, ActionEnd 0.9143",
      "death":  "Ragdoll 0.9"
    },
    "name":   "Spider",
    "model":  "cyborg_spider",
    "donor":  "Swarmer_TacCharacterDef",
    "ranged": "Crabman_Head_Spitter_WeaponDef",
    "up": "0,1,0",
    "lift": 2.1372,
    "health": 40, "will": 10, "speed": 16, "volume": 1
  }
}
```

### Top level

| Field | Value | What it is |
|---|---|---|
| `id` / `bundle` | required | as in every project |
| `scale` | `0.008` | file units → game units. **One number, at the top level**, because the bake reads it too for root motion — two numbers that must agree would drift. |
| `loop` | `"Spider_Idle, Spider_Walk"` | which of the file's own clips must cycle. **glTF carries no loop flag**, so this cannot be inferred, and an un-looped walk plays once and freezes. |
| `play` | `"Spider_Idle"` | which clip a bare imported model plays |

### The `"creature"` block

| Field | Required | What it is |
|---|---|---|
| `"creature": {}` itself | **yes** | The empty block **is the opt-in.** A project without one is a texture/sound project and none of this happens to it. |
| `clips` | **yes** | your clip name → **role**. Roles: `walk`, `idle`, `attack`, `death` (all four REQUIRED), plus optional `reaction` (flinch on damage), `ranged`, `jump`. An empty string earmarks a clip for later. |
| `events` | in practice yes | role → `"<EventName> <fraction>, …"`. **WHERE in the clip**, as a fraction of its length. |
| `name` | yes | what the unit is called |
| `model` | yes | the stem of the file in `Content\Models\` |
| `donor` | yes | the **shipped unit to clone structure from**. Pick a one-tile one — see below. Accepts a def name (the normal case) or a species tag. |
| `ranged` | optional | a shipped `WeaponDef` to give it a ranged attack |
| `up` | yes | the model's up axis, e.g. `"0,1,0"`. The rotation is *derived* from it, so the only thing that can be wrong is the measurement. |
| `lift` | yes | how far the model's origin sits above its lowest vertex. **A centred model without this stands in a hole half its own height deep**, and a rotation can never supply the number. |
| `health` `will` `speed` `volume` | optional | stats. `volume` is unit slots in the aircraft — set it to `1` or a six-slot craft overflows. |
| `pace` | optional | tiles/second the creature travels at. Omit it for the shipped soldier's **5.4284**; set `0` to keep your clip's own authored speed. |

## 3. The commands, and what they print

### The workflow — the bake refuses you on purpose, twice

**Step 1 — drop the `.glb` in**, put `"creature": {}` in `ppcontent.json`, and bake:

```text
ct_project CustomCreature
```

The tool reads your file, **writes back into your `ppcontent.json`** every animation it found, tells
you what it measured, and **refuses**:

```text
creature-measure 'cyborg_spider': 49 bone(s), spans 120.435 x 64.237 x 105.578 file unit(s) ...
  a tile is 1.0, so "scale": 0.008 makes it one tile across
creature-clips 'cyborg_spider': 7 animation(s) in the file -> Spider_Walk, Spider_Idle, ...
creature-scaffold: WROTE the clip list into ...\ppcontent.json - map each one to a role there.
creature-roles FAIL ... leaves 4 REQUIRED role(s) unmapped: walk, idle, attack, death.
```

It refuses rather than guesses on purpose. glTF has no "this is the walk cycle" flag, so a walk and a
death are the same shape of data — and a wrong guess puts an event-less clip in the attack state,
which is a ten-second stall per swing that reads like **the game** hanging.

**Step 2 — fill in the roles** beside each clip name, and set the numbers you care about. Read
`creature-measure` against what you declared: it prints the scale that *would* make the model one tile
across, next to the one your file asks for. If those disagree wildly, that mistake corrupts the
collider, the aim point and the root motion all at once.

**Step 3 — bake again.** Re-baking never churns the file: a role you already filled in survives, a
clip you added arrives blank.

```text
creature-events PASS every blocking event the game waits for is declared
creature-roles  PASS "clips" maps 7 of 7 discovered animation(s); every required role is mapped
ct_project: ALL PASS - ...\Dist\CustomCreature.bundle
```

### At load time, and in a mission

One line per seam, each with its own PASS/FAIL:

```text
ct_creature PASS '...\Dist\CustomCreature.bundle' -> model 'cyborg_spider', 7 clip(s): cyborg_spider_spider_attack_1, ...
ct_creature PASS root-motion node '_rootJoint'
ct_creature PASS rig root 'cyborg_spider' has the Animator ON THE ROOT, renderer=... bones=49 ...
ct_creature PASS 4 animation event(s) stamped as OnAnimEvent(<name>) [cyborg_spider_spider_attack_1:ActionDo@0.4054, ...]
ct_creature PASS clips: N non-default anim action(s) rewritten ...; M TurnSequence slot(s) CLEARED ...
ct_creature PASS role 'walk' = 'cyborg_spider_spider_walk' isLooping=True
ct_creature PASS (tactical) 'Overridden: MidMonsterAnimator' had 45 overridable clip(s); ... -> HL_ActionPlaceholder -> cyborg_spider_spider_attack_1 (DefaultActionClip), Chiron_death -> cyborg_spider_spider_death, ...
ct_creature PASS '...' donor-free audit: no Mutog_ClassTagDef/VehicleTag, 1 geometry-free bodypart ...
ct_creature PASS roster (Tutorial.InitSquad) 'Manticore' carries 6 unit(s), space 6/6: ...
```

**The four to know by name:**

- **the roster line** — *is there a creature at all.* Every other line can be green while this one
  fails; that is the point of it. The engine's own "add to aircraft" call **never refuses** — it
  computes the space sum and throws it away — so "we called Add" is not evidence of anything. This
  line reads the aircraft back out.
- **the controller line** — *whether turn, idle and death play at all.* It fires **per spawn, in a
  mission**, not at mod load, and lists every clip the donor's controller holds and what each now
  plays. If it is absent, the bridge never ran.
- **the donor-free audit** — *is this your creature or a repainted donor.* Read back off the finished
  def through the game's own accessors, so an edit that re-points one field at the donor turns it red.
- **`isLooping=False … MUST CYCLE AND DOES NOT`** — the one to read first. A non-looping idle or walk
  plays once and holds, which in game is indistinguishable from *no animation at all*. It comes from
  the top-level `"loop"` declaration, and `ct_project` prints `, LOOPS` or `, plays once` per clip.

### The measurement

Measured on a download-shaped install — shipped files only, `ct_project` **never run** — the creature
spawned into a live mission and passed **all 19** gate arms:

| Probe | Reading |
|---|---|
| bash `Fishman_12` | **190,0 → 130,0** |
| spit | **130,0 → 120,0** (4 → 5 statuses) |
| walk | 2,83 tiles in 0,71 s = **3,98 tile/s** |
| death clip | `cyborg_spider_spider_death` |
| `Health.Max` | **60,0**, from `Data.Strength=4` |
| animator played | `cyborg_spider_spider_attack_1 / _attack_2 / _walk / _idle / _death` |

The control, on the first shipped candidate template, in the harness's own words: animator
`[Fireworm_idle_loop → Fireworm_move_loop]`, `Data.Strength=0` → **CONTENT-DEFECT, born dead**,
`C1-melee FAIL` (no attack ability resolves), 2,32 tile/s. That control is what shows the harness is
capable of reporting failure.

!!! warning "The bake's health number is not the number the game gives the creature"
    `ppcontent.json` asks for `health: 40` and the build line computes
    `Health.Max = Toughness 0 + 4 x 10,00 = 40`. The spawned actor measured **`Health.Max = 60,0`**
    from the same `Data.Strength=4`, with TFTV resident in a 21-mod stack. Recorded, not resolved: the
    most likely explanation is a TFTV strength→health multiplier, and nothing yet establishes which
    layer applies it. **Check the live actor, not the bake line.**

## The three things that will actually bite you

### Choosing a donor — pick a ONE-TILE unit

The clone inherits its donor's whole component list, and none of it is a *tag* you can strip. The
demo used to clone a Mutog, and every one of these was inherited in silence:

| What the donor brought | What the player saw |
|---|---|
| a demolition component | a tiny spider smashing every wall it passed |
| nav `AgentType: "MedMonster"` | a multi-tile footprint and a fat path preview |
| a 3×3 move ability | a 3×3 move on a 1×1 creature |
| an agent radius ≥ 1 | a turn-in-place demanded on every move order |

Crushing is a **component**, and the footprint is one **string**. The engine now drops the demolition
component from every creature it builds and re-points the AgentType at a shipped one-tile unit, so a
bad donor is survivable — but the honest fix is to start from the right unit.

`Swarmer_TacCharacterDef` is the default because it is the smallest shipped unit that carries
everything the clone *requires*: a `Humanoid` agent type, an addons manager with a skeleton chassis,
an anim-actions def, and **a bodypart that is a melee weapon with a bash ability on it** — which is
where your creature's attack comes from. A Facehugger looks like a better fit until you notice its
bodypart list is empty, and a creature with no bodypart weapon can never attack.

### Animation events — the biggest ceiling, and it is not a hang

Phoenix Point does not time gameplay off clip length. It **blocks waiting for a named event fired from
inside the clip**:

| The game waits for | It gates |
|---|---|
| `ActionDo`, `ActionEnd` | every generic ability |
| `ShootShot` | the shot actually leaving the weapon |
| `Ragdoll` | the actual death |
| `Holster`, `DrawOut` | weapon in and out of hands |

A downloaded clip carries **none** of these, and **the bake does not write them** — that is a real
ceiling. The engine works around it at load by stamping the events your `"events"` block declares, but
**any ability whose event is not in that list still costs 10 s.**

The failure mode is precise, and it is not a hang: the wait is timeout-bounded at 10 s, logs *"the
event is likely missing from the animator"*, and continues. So the logic still fires — ten seconds
late, every action. Unplayable, not fatal.

**Measuring your own times.** The demo measured `Spider_Attack_1` peaking on bone `lapa_1_R_4_044` at
**0.4865** of the clip — the frame where a leg reaches farthest — by walking the clip frame by frame
and finding how far the furthest bone had travelled from its frame-0 position along the clip's own
principal axis of motion. That gave the declared
`"attack": "ActionDo 0.4054, ShootShot 0.4865, ActionEnd 0.8378"`. A real creature mod puts the hit
frame on the frame that *looks* like a hit; a measurement is a grounded starting point.

### Speed comes from the clip's ROOT MOTION, not from a number

The one that surprises everyone. **There is no def field anywhere in this game that sets a unit's
movement speed.** The engine measures every clip by sampling it on the actual object and reading how
far the root-motion node travelled: `Speed = offset.magnitude / clip.length`. So whatever pace your
downloaded animator happened to walk at *is* the pace your creature moves at — and a walk cycle that
animates **in place** measures `Speed == 0`. Most free models animate in place.

`"pace"` retimes the clip you mapped to `walk` to a target tiles/second. There are two ways to raise a
measured speed and **only one keeps the feet on the ground**: stretching the ramp makes the body cover
more ground at the old cadence, which is foot sliding; **compressing the timeline** leaves travel per
cycle unchanged and speeds legs and ground up together, so a planted foot stays planted. The bake does
the second, and only to the walk clip — an attack's rate is set by the `ShootShot` frame you measured,
and retiming it would move the hit.

```text
clip 'Spider_Walk' pace: 1.986079 -> 5.4284 tile/s, so the clip plays x2.733224
  (0.8 s -> 0.292695 s per cycle = 3.416476 cycle(s)/s).
  The legs and the ground speed up together, so nothing slides.
```

**A walk clip with root translation is the single most useful thing to know before buying a model for
a game.** This one genuinely travels (1.986079 tile/s), so ×2.73 is enough and 3.42 cycles/s looks
natural. The previous model animated in place and needed ×10.65 — 12.78 cycles/s, a blur.

`"pace"` is **not** the same key as `"speed"`, and cannot be: `speed` is spent as the unit's action
points — how **far** it gets in a turn. `pace` is how **fast** it crosses a tile.

## What the bundle contains, and by what name

| Asset | Address |
|---|---|
| the creature prefab (root + 49 bones + `SkinnedMeshRenderer` + `Animator`) | `assets/morgott.demo.customcreature/models/cyborg_spider` |
| its seven clips | `assets/morgott.demo.customcreature/clips/cyborg_spider_spider_walk` … `_spider_death` |
| the override controller the bare `Animator` carries | `assets/morgott.demo.customcreature/controllers/cyborg_spider_aoc` |

A clip's name is `<model file stem>_<clip name in the .glb>`, lowercased — hence
`cyborg_spider_spider_walk`. Clips are matched by **suffix**, so renaming the model file cannot
silently unbind everything.

## The DLL — the whole of it

**This is the entire assembly.** Below is `src\CustomCreatureMain.cs` from the demo, reduced only by
deleting its comment block; every line of code is verbatim.

```csharp
using HarmonyLib;
using Morgott.ContentTool.Tactical;
using PhoenixPoint.Geoscape.Levels;
using PhoenixPoint.Geoscape.Levels.Factions;
using PhoenixPoint.Modding;
using PhoenixPoint.Tactical.Entities;

namespace Morgott.CustomCreature
{
    public class CustomCreatureMain : ModMain
    {
        public override bool CanSafelyDisable => true;

        /// <summary>The creature the engine built, handed to the two squad triggers below.</summary>
        internal static TacCharacterDef Spider;

        public override void OnModEnabled()
        {
            Spider = CreatureBuild.Build(Instance.Entry.Directory, m => Logger.LogInfo(m));
            ((Harmony)HarmonyInstance).PatchAll(System.Reflection.Assembly.GetExecutingAssembly());
        }
    }

    [HarmonyPatch(typeof(GeoPhoenixFaction), nameof(GeoPhoenixFaction.CreateInitialSquad))]
    internal static class SpiderJoinsNewCampaign
    {
        private static void Postfix()
        {
            CreatureBuild.JoinPlayerVehicle(CustomCreatureMain.Spider, "CreateInitialSquad");
        }
    }

    /// <summary>Private method, patched by name.</summary>
    [HarmonyPatch(typeof(GeoscapeTutorial), "InitSquad")]
    internal static class SpiderJoinsTutorialSquad
    {
        private static void Postfix()
        {
            CreatureBuild.JoinPlayerVehicle(CustomCreatureMain.Spider, "Tutorial.InitSquad");
        }
    }
}
```

It splits cleanly in two, and the split is the point of the page:

- **`CreatureBuild.Build(modDirectory, log)`** is the mechanism, and it is one call.
  `Instance.Entry.Directory` is your own mod folder; the callback is what prints the `ct_creature`
  lines. It **never throws** — a failed mod load empties the activated-mods list, so it logs the
  reason and returns `null` instead.
- **Everything else is the choice ContentTool cannot make for you:** *where does this creature come
  from?* This demo puts it in the starting squad, so it owns those two patches. A different mod would
  hand the unit to a faction's deployment list, or spawn it from an ability, and would own a
  different hook.

!!! warning "Why the squad hook is patched TWICE"
    Phoenix Point has **two** squad builders and which one runs depends on whether the player took
    the tutorial. The obvious one is `GeoPhoenixFaction.CreateInitialSquad`. The other is
    `GeoscapeTutorial.InitSquad`, which quietly replaces it and reads the starting-squad template for
    its **length only** before filling the gap with a fixed human template — which is why appending
    to that array produces an extra soldier and never your creature. `JoinPlayerVehicle` sidesteps
    both by adding the unit to the aircraft *after* whichever builder ran, then reading the roster
    back to prove it is there. Patch only the first and your creature is missing for every player who
    took the tutorial.

`OnModEnabled` and not `ApplyDefRepoPatches`: the running game has that second hook, but the shipped
`ModSDK\Assembly-CSharp.dll` you compile against does not declare it, so the override does not
compile. `OnModEnabled` runs after the defs are loaded and before a campaign exists, which is the
window this needs.

### The `.csproj`

`package.ps1` builds the first `*.csproj` in your mod folder and then looks for
`bin\Release\**\<FolderName>.dll`, so **`<AssemblyName>` must equal your mod's folder name** and
`meta.json` must declare that same name. Reduced from the demo's `CustomCreature.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <AssemblyName>CustomCreature</AssemblyName>
    <RootNamespace>Morgott.CustomCreature</RootNamespace>
    <TargetFramework>net472</TargetFramework>
    <LangVersion>latest</LangVersion>
    <AppendTargetFrameworkToOutputPath>false</AppendTargetFrameworkToOutputPath>
    <EnableDefaultCompileItems>false</EnableDefaultCompileItems>
    <OutputPath>bin\$(Configuration)\CustomCreature\</OutputPath>
  </PropertyGroup>
  <ItemGroup>
    <Compile Include="src\**\*.cs" />
    <None Include="meta.json" CopyToOutputDirectory="PreserveNewest" />
  </ItemGroup>
  <PropertyGroup>
    <PPRoot Condition="'$(PPRoot)' == ''">D:\Steam\steamapps\common\Phoenix Point</PPRoot>
    <ModSDK>$(PPRoot)\ModSDK</ModSDK>
    <!-- The ModSDK folder ships only four assemblies; these two live in the game's own Managed\. -->
    <UnityManaged>$(PPRoot)\PhoenixPointWin64_Data\Managed</UnityManaged>
  </PropertyGroup>
  <ItemGroup>
    <Reference Include="ContentTool">
      <HintPath>$(PPRoot)\Mods\ContentTool\ContentTool.dll</HintPath>
      <Private>false</Private>
    </Reference>
    <Reference Include="Assembly-CSharp">
      <HintPath>$(ModSDK)\Assembly-CSharp.dll</HintPath>
      <Private>false</Private>
    </Reference>
    <Reference Include="0Harmony">
      <HintPath>$(ModSDK)\0Harmony.dll</HintPath>
      <Private>false</Private>
    </Reference>
    <Reference Include="UnityEngine.CoreModule">
      <HintPath>$(ModSDK)\UnityEngine.CoreModule.dll</HintPath>
      <Private>false</Private>
    </Reference>
    <Reference Include="UnityEngine.AnimationModule">
      <HintPath>$(UnityManaged)\UnityEngine.AnimationModule.dll</HintPath>
      <Private>false</Private>
    </Reference>
    <Reference Include="UnityEngine.AssetBundleModule">
      <HintPath>$(UnityManaged)\UnityEngine.AssetBundleModule.dll</HintPath>
      <Private>false</Private>
    </Reference>
  </ItemGroup>
</Project>
```

`<Private>false</Private>` on **every** reference, and read
[the reference rule](reference.md#10-the-two-rules-that-are-not-negotiable) before you add a seventh
one — this is the recipe with the most references, and the last two come from the game's own
`Managed\` folder rather than `ModSDK\`, which is the category that can take every mod on the machine
down with it.

And `meta.json` names it:

```json
{ "ID": "morgott.demo.customcreature", "AssemblyName": "CustomCreature.dll",
  "Dependencies": [ "com.morgott.ContentTool" ] }
```

## 4. Bake and package

```powershell
ct_project CustomCreature                 # in game, after changing the model or the manifest
$PP = 'D:\Steam\steamapps\common\Phoenix Point'   # your own game folder
.\package.ps1 -Project "$PP\Mods\CustomCreature"  # with the game shut - builds your .csproj too
```

Commit `Dist\CustomCreature.bundle`. **That is what makes the download work with no bake.**

## 5. How a player installs it

Unzip into `Phoenix Point\Mods\`, tick it on, start a **NEW CAMPAIGN** — the demo's unit is the last
one in the starting squad. **No bake, no console command.** Nothing in the game install is touched.

## 6. Discovery and the dependency line

`"Dependencies": [ "com.morgott.ContentTool" ]`. The engine reads `Dist\<YourMod>.bundle` at mod
enable. Until that file exists the mod says so and changes nothing:

```text
ct_creature VOID '...\Dist\CustomCreature.bundle' does not exist - run `ct_project CustomCreature`
```

## 7. When it does not work

| Line or symptom | What it means |
|---|---|
| `creature-roles FAIL ... leaves 4 REQUIRED role(s) unmapped: walk, idle, attack, death.` | you baked with `"creature": {}` and have not mapped the roles yet. That is step 1 of the workflow, not an error. |
| `creature-events WARN` | a blocking event is undeclared. It costs **10 s per action**, not a hang. |
| `AnimEventReceiver.WaitForEvent timeout expired … the event is likely missing from the animator` | the game's own error, and the sign that your `"events"` block does not cover an action you used. |
| `<unit> is waiting for animation <clip> timed out. Current animation: <other clip>` | you filled a slot with a clip whose state your controller cannot reach. **A filled slot is a CLAIM, not a picture** — filling the turn-in-place slots tells the engine this creature turns on the spot, and it then blocks forever waiting for a state a downloaded `.glb` has no clip for. Any optional sequence is the game asking a yes/no question; answer it honestly. |
| `isLooping=False … MUST CYCLE AND DOES NOT` | the deployed bundle was baked before your `"loop"` declaration existed. Re-bake and restart. |
| `ct_creature VOID '...\Dist\<Mod>.bundle' does not exist` | you never baked, or the bundle is not in the package. |
| the roster lists your creature and **shows no model** | an extra transform between the rig root and the prefab. One code path looks for the `Animator` tolerantly and one looks on the root only, with no null check — the prefab must **be** the rig root, unwrapped. |
| the creature walks on the spot | the root-motion node is wrong. The engine derives it as the rig's one parentless bone; if it measures travel off a bone's parent it reads 0 and reports "this segment does not move the actor". Look for `ct_creature PASS root-motion node '<name>'`. |
| the creature slides round instead of turning | the donor's turn clips are playing on your rig and name none of your bones. The turn slots are cleared rather than mirrored — the honest answer is that a downloaded model does not turn in place, and it lerps round in a few frames instead. |
| it stands in a hole half its own height deep | `lift` is missing or wrong. A rotation can never supply that number. |
| armour and weapon models do not show | expected. Body-part visuals are reparented onto the rig **matched by bone name**, and a foreign skeleton shares no bone name with the donor's. A real creature mod ships its own body-part items, or none. |
| a stray 2-triangle plane in the file | fine. The reader picks the mesh a **skin** drives and names the meshes it dropped. If that does not single one out — two skinned meshes, or a static file with several — it refuses. |
| the death clip is 13.83 s of frozen pose | your file lays its clips end to end on one shared reel with absolute key times. The reader lifts each clip off that reel to its own zero and says so (`lifted off the file's shared timeline at N s`); if you see the old behaviour, re-bake. |

## Ceilings, stated

- **The bake emits no animation events.** The biggest one. Every baked clip arrives eventless and the
  workaround at load covers only `ShootShot` / `ActionDo` / `ActionEnd` / `Ragdoll`.
- **Root motion, not a speed stat.** See above. Buy a model whose walk cycle travels.
- **Body-part visuals do not attach.** Matched by bone name; a foreign skeleton shares none.
- **The stats, AI and abilities are the donor's** unless you set them. Cloning a working unit rather
  than inventing balance is deliberate.
- **One clip per state, no state machine of your own.** The controller is the donor's, and this route
  serializes no controller constant. A genuinely new state machine means a controller of your own,
  from code.
- **Draco is refused.** Export from Blender as glTF Binary with Compression unticked.
  `EXT_meshopt_compression` and `KHR_mesh_quantization` are decoded in-house and need no conversion.

## Licence

Check your model's licence **before** you swap one in. Not every free model is free to ship, and
CC-BY additionally obliges you to keep the author credited — in your `SOURCES.md` and in the mod
description.
