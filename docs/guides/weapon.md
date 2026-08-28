# A new weapon — a gun Phoenix Point does not ship

**When the thing you are adding is a weapon, the assets are only half of it. The other half is a
def.** This route needs a DLL, but a one-call one: cloning a def, building its view, loading its icon,
pointing a skin at a published prefab, fitting the sockets, deep-copying the damage payload and
seeding starting storage are the same for every weapon anyone will ever add, so they live in the
ContentTool engine. **Your surface is `ppcontent.json`.**

The demo adds **three** weapons in one mod, and they are three different jobs:

| Weapon | Cloned from | What it demonstrates |
|---|---|---|
| **Vulture PDW** | `PX_LaserPDW_WeaponDef` | a fast-firing laser PDW **using Phoenix Point's own ammo**, and the only one of the three with a hand-fitted model and hand-declared sockets |
| **Vulture AR** | `PX_AssaultRifle_WeaponDef` | a re-tuned service rifle, wearing a **multi-mesh download** merged by the bake — the case that needs no Blender work |
| **Vulture Sidearm** | `SY_LaserPistol_WeaponDef` | a tau-like pistol that **sets things on fire**, with `Fire_StandardDamageTypeEffectDef` and `Burning_DamageKeywordEffectorDef=40` |

## 1. The folder

```text
WeaponAdd\
  meta.json                      "AssemblyName": "WeaponAdd.dll"
  ppcontent.json                 THE INTERESTING FILE: "publish" rows + a "weapons" array
  Content\
    Models\
      sniper.glb                 the geometry, fitted to the shipped sniper's own box
    Textures\
      sniper.png                 1024 - named after the MODEL, which is what binds it
  Icons\
    sniper_inv.png               450x450 - the inventory cell, rendered from sniper.glb
  Dist\
    WeaponAdd.bundle             written by `ct_project` - COMMIT AND SHIP IT
  src\WeaponAddMain.cs           ONE CALL. The mechanism is the engine's - see section 3
  WeaponAdd.csproj               builds the line above
  WeaponAdd.dll                  the built output, staged by package.ps1
  SOURCES.md                     CC0 / CC-BY attribution, kept with the files
```

## 2. The manifest, field by field

### The `publish` rows — one per model you ship

```json
"publish": [
  { "key": "c7a9f1d24b6e4a3c8f5b7d1e9a2c4b60", "asset": "models/sniper",    "type": "GameObject", "deps": "defaultlocalgroup_unitybuiltinshaders.bundle" },
  { "key": "c7a9f1d24b6e4a3c8f5b7d1e9a2c4b61", "asset": "models/ar181",     "type": "GameObject", "deps": "defaultlocalgroup_unitybuiltinshaders.bundle" },
  { "key": "c7a9f1d24b6e4a3c8f5b7d1e9a2c4b62", "asset": "models/taupistol", "type": "GameObject", "deps": "defaultlocalgroup_unitybuiltinshaders.bundle" }
]
```

Field by field — and the full explanation of `key`, `asset`, `type` and `deps` is in
[Meshes → adding a whole new model](meshes.md#adding-a-whole-new-model). The two that bite:

- **`key` is 32 lowercase hex digits on purpose.** That is the exact shape Phoenix Point's own asset
  references carry — the shipped rifle's skin data names `604561be7de7cb6479711b4e31bdc02d` — and the
  engine checks the runtime key is valid before anything else. **Generate one at random** (any GUID
  generator, lowercased, dashes removed) and vary the last digit or two per row, as the three above
  do. **There is no command that lists the game's shipped keys, and you do not need one:** the engine
  compares your key against the shipped catalog itself and refuses a collision by name rather than
  letting it silently lose —

    ```text
    REFUSED: '<your mod>' publishes key '<key>', which the game's own catalog already has.
    ```

    A collision with *another mod's* published key is refused by name too — *"One key has exactly one
    owner and the lower mod id keeps it"*. So the check is done for you; what you must not do is
    reuse a key you saw written down somewhere.
- **`deps` is not decoration.** Every model the tool bakes gets a Material whose shader is the builtin
  `Standard` through an *external* reference. Without the dep the gun renders with
  `Hidden/InternalErrorShader`.

### The `weapons` array — one object per gun

```json
"weapons": [
  {
    "id":     "Morgott_VulturePDW_WeaponDef",
    "name":   "Vulture PDW",
    "clone":  "PX_LaserPDW_WeaponDef",
    "guid":   "c7a9f1d2-4b6e-4a3c-8f5b-7d1e9a2c4b01",
    "blurb":  "A Phoenix armoury rebuild of a pre-Pandoravirus energy carbine: the emitter was wound past its rated draw, so every bolt lands harder and none of them land where you meant.",
    "icon":   "Icons\\sniper_inv.png",
    "model":  "c7a9f1d24b6e4a3c8f5b7d1e9a2c4b60",
    "fit":    "auto",
    "shoot":  "0.00435,0.06109,0.76880",
    "aim":    "0.00435,0.06109,0.41911",
    "shell":  "0.02021,0.06109,0.41911",
    "damage": "60",
    "spread": "3.0",
    "count":  "10",
    "clips":  "10"
  },
  {
    "id":     "Morgott_VultureAR_WeaponDef",
    "name":   "Vulture AR",
    "clone":  "PX_AssaultRifle_WeaponDef",
    "guid":   "c7a9f1d2-4b6e-4a3c-8f5b-7d1e9a2c4b11",
    "blurb":  "The same armoury, the same bad habit, applied to a service rifle. It hits like something heavier and groups like something cheaper.",
    "model":  "c7a9f1d24b6e4a3c8f5b7d1e9a2c4b61",
    "fit":    "auto",
    "damage": "45",
    "spread": "2.4",
    "count":  "10",
    "clips":  "10"
  },
  {
    "id":     "Morgott_VultureSidearm_WeaponDef",
    "name":   "Vulture Sidearm",
    "clone":  "SY_LaserPistol_WeaponDef",
    "guid":   "c7a9f1d2-4b6e-4a3c-8f5b-7d1e9a2c4b21",
    "blurb":  "A Synedrion laser sidearm with the safeties argued out of it. One hand, one very bright bolt that leaves what it hits burning, and a grouping its previous owner would not sign for.",
    "model":  "c7a9f1d24b6e4a3c8f5b7d1e9a2c4b62",
    "fit":    "auto",
    "damage": "75",
    "spread": "2.25",
    "damagetype": "Fire_StandardDamageTypeEffectDef",
    "keywords":   "Burning_DamageKeywordEffectorDef=40",
    "count":  "10",
    "clips":  "10"
  }
]
```

| Field | Required | What it is |
|---|---|---|
| `id` | **yes** | the new def's name |
| `name` | yes | the displayed weapon name |
| `clone` | **yes** | the shipped `WeaponDef` to clone. **This is the most important field on the page** — see below. |
| `guid` | **yes** | a **constant**, never generated per launch. A save stores an item by its def; a def whose identity changes every launch is a save that stops loading. Two entries with the same guid are refused by name: *"Give them `"guid"` values that differ"*. |
| `blurb` | optional | the description text |
| `icon` | optional | a path to a PNG in your folder, relative to it. **The engine loads it and writes all three icon fields for you** — this is a *new* def, so it gets a new view def and nothing shipped is touched. Without one the weapon quietly shows its **donor's** picture. |
| `model` | **optional** | a published key from your `publish` rows. **An entry with no `"model"` keeps the SkinData of the weapon it cloned and looks like that gun.** |
| `fit` | see note | **`"auto"` is the only value with meaning.** It tells the engine to derive all four `EXT_` sockets itself — from your model's own box, then refined against the donor weapon's loaded prefab. Any other value, **or the key left out**, means *"use the `shoot`/`aim`/`shell` I declared"*. It is therefore not optional in the usual sense: a `"model"` with neither `"fit": "auto"` nor a `"shoot"` is **refused by name**. With `"auto"`, declared socket values are ignored. |
| `shoot` / `aim` / `shell` | required unless `"fit": "auto"` | socket positions, `x,y,z`. See below. |
| `damage` | optional | per-shot damage. **Not the payload's own damage value** — the flow switches onto the keyword list once it is non-empty, so this overwrites the value of every keyword pair that applies standard damage. It adds no pair and removes none. |
| `spread` | optional | degrees. Larger = wider cone. **Effective range is derived from it**, so raising the spread lowers the displayed range. |
| `count` / `clips` | optional | how many of the weapon and how many magazines land in a new campaign's starting storage |
| `damagetype` | optional | a shipped damage-type effect def, e.g. `Fire_StandardDamageTypeEffectDef`. A name that does not exist is reported by name, not silently skipped. |
| `keywords` | optional | `<KeywordDef>=<value>` pairs separated by `;`, e.g. `Burning_DamageKeywordEffectorDef=40`. **It MERGES into the donor's keyword list, it does not replace it** — see below. |

## The four things a new weapon actually needs

### 1. A def — cloned, not written

**The clone source is the weapon CLASS, and that is not a detail.** Phoenix Point picks a soldier's
hold pose, aim stance and firing animation set off the weapon's **tags**, so an SMG cloned from a
sniper rifle is *held and fired like a sniper rifle*. Pair the silhouette with the shipped class and
the animation problem never exists.

Cloning matters more than it sounds. `PX_LaserPDW_WeaponDef` carries **four abilities, a damage
payload with two damage keywords, a required slot bind, a holster slot, compatible ammunition, a
Wwise switch, a firing event, six game tags and a manufacture cost**. Typing that out is thirty
chances to be subtly wrong; cloning it is one line that cannot be.

Everything below rides along for **zero lines**:

| What | Where it comes from |
|---|---|
| equip slot, two-handed | the donor's required slot binds |
| holster — it goes on the soldier's back when not selected | the donor's holster slot |
| abilities — shoot, overwatch, reload, drop | the donor's ability list |
| ammo, and the magazine size | the donor's compatible ammunition |
| burst count and AP cost | the donor's damage payload |
| **the firing report** | the donor's Wwise **switch**, not the event. Every gun in the game shares one shoot event; the switch is the whole difference between a rifle crack and an energy discharge. |
| **the tracer** | the donor's projectile visuals |
| **the muzzle flash, smoke and brass** | the donor's visual effects |
| the animation set | the donor's game tags |

So: equip it, holster it, overwatch with it, reload it, drop it, and shoot it — none of which you
wrote.

### 2. A model, served from your own bundle

`"model"` names a key from your `publish` rows, and that key is set as the weapon's default prefab.

**`"model"` is OPTIONAL, and leaving it out is a legitimate answer.** An entry with no `"model"` keeps
the skin data it cloned and looks like the gun it came from — which is the honest state for a model
that has not been fitted yet, and strictly better than a soldier holding nothing. All three demo
weapons do ship their own; none of them has to.

### 3. Four empty transforms — the part that would silently break the gun

Phoenix Point finds a weapon's muzzle, sights and ejection port **by name**, off the weapon's visual
root. Every one of them breaks something specific:

| Def field | Value | What breaks without it |
|---|---|---|
| the payload's projectile origin | `EXT_ShootPoint` | the engine logs *"Can't find … projectile origin"* and then indexes an empty array. **The muzzle flash also spawns here.** |
| the equipment's aim point | `EXT_AimPoint` | aiming has no origin |
| the equipment's aim transform | `EXT_AimIKPoint` | the aim IK solver is handed a null transform |
| the shell ejection point | `EXT_ShellPoint` | the engine logs *"has a shell prefab but invalid shell ejection point"* **on every shot** and drops no brass. A name that does not start with `EXT_` is refused. |

A prefab baked from a `.glb` is a root plus **one** mesh child, so there is nowhere in the file to put
them. They are added to the loaded prefab, at positions **derived** from the fitted mesh's own box —
muzzle at the front face, sights 62% back along it, both on the barrel line at 70% of the box height,
ejection port on the `+X` face beside the sights:

```text
EXT_ShootPoint              (0.00435, 0.06109, 0.76880)
EXT_AimPoint/EXT_AimIKPoint (0.00435, 0.06109, 0.41911)
EXT_ShellPoint              (0.02021, 0.06109, 0.41911)
```

**The socket IS the VFX placement.** Get `EXT_ShootPoint` wrong and the muzzle flash appears in the
middle of the stock.

### 4. A place in the world

`count` and `clips` append your weapon and its ammunition to every difficulty's **starting storage** —
the item list a new campaign fills the Phoenix base from. **Two entries, not one:** a gun whose ammo
is not already in that array arrives with one magazine and no way to reload.

## Scale and orientation

Same problem as [replacing a mesh](meshes.md#scale-and-orientation-the-part-that-is-actually-hard),
with one difference that matters: nothing here replaces a shipped mesh, so the shipped box is not a
constraint the engine enforces — it is the **specification**. A weapon is an Addon parented to a named
attachment transform on the rig, so a brand-new prefab lands in the hand at whatever coordinates its
mesh happens to carry. Copying a shipped weapon's own local AABB is the only way to arrive there at
the right size and the right way round.

The fitting step's own output, from the demo:

```text
source  bbox min ['-1.2769', '-0.1204', '-0.0296'] max ['0.4408', '0.2095', '0.0296']
per-axis ratios  x=1.2753 y=0.6884 z=0.5358  ->  uniform scale 0.535756 (smallest wins)
translate        ['0.004352', '0.001888', '0.084718']
unity bbox       min ['-0.0115', '-0.0626', '-0.1514'] max ['0.0202', '0.1141', '0.7688']
shipped bbox     min ['-0.0334', '-0.0878', '-0.1514'] max ['0.0421', '0.1393', '0.7688']
OK  ...\Content\Models\sniper.glb  8249 verts / 8728 tris / 317428 bytes
```

Target box measured off the shipped sniper mesh `WPN_PX_RG_Sniper_Rifle_T01_V01` (7676 verts): centre
`(0.00435, 0.02574, 0.30869)`, extent `(0.03774, 0.11355, 0.46011)` — 0.920 m of rifle down **+Z**,
0.227 m on **+Y**. Assert three things: the fitted mesh stays inside that box, the barrel really lands
on +Z, and **the uniform scale is positive** — a negative one is a point reflection, it passes a
bounding-box check and ships an inside-out gun.

## The inventory icon

The inventory cell draws a **pre-rendered Sprite off the def**, never a live render of the model. A
new weapon that does not set it inherits whatever its clone source had, so a cloned sniper quietly
shows the sniper's picture. **450×450**, which is what the shipped weapon icons measure. Render it
from the same geometry the player will hold, so the cell and the hand cannot disagree.

**On this rung you write no code for it.** Point `"icon"` at a PNG in your folder and the engine
loads it and writes the inventory, small and large icon fields — it can, because a new weapon gets a
**new** view def of its own, cloned from the donor's, and nothing shipped is modified.

!!! note "That is the opposite of [the icon rung](textures.md#the-icon-rung), and both are true"
    The icon rung is about **re-skinning a weapon the game already ships**. That means writing a
    field on a **shipped** view def, which no manifest key does and which your own assembly has to
    do. Adding a weapon is the easy direction; changing one of theirs is the direction that costs
    code.

## 3. The DLL — the whole of it

**This is the entire assembly.** Below is `src\WeaponAddMain.cs` from the demo, reduced only by
deleting its comment block; every line of code is verbatim.

```csharp
using System.Collections.Generic;
using Morgott.ContentTool.Tactical;
using PhoenixPoint.Modding;
using PhoenixPoint.Tactical.Entities.Weapons;

namespace Morgott.WeaponAdd
{
    public class WeaponAddMain : ModMain
    {
        public override bool CanSafelyDisable => true;

        /// <summary>What the engine built, kept only so a failure is visible in the log line.</summary>
        internal static List<WeaponDef> Weapons;

        public override void OnModEnabled()
        {
            Weapons = WeaponBuild.Build(Instance.Entry.Directory, m => Logger.LogInfo(m));
        }
    }
}
```

Three things in it are the whole contract:

- **`ModMain`** is Phoenix Point's own base class, out of `PhoenixPoint.Modding`. Subclassing it is
  what makes your assembly a mod.
- **`OnModEnabled`** is the hook, **not** `ApplyDefRepoPatches`. The running game has that second one,
  but the shipped `ModSDK\Assembly-CSharp.dll` you compile against does not declare it, so the
  override does not compile. `OnModEnabled` is the portable seam, and it runs after the defs are
  loaded and long before a campaign is created — which is exactly the window a new weapon needs.
- **`WeaponBuild.Build(modDirectory, log)`** reads the `"weapons"` array out of *your* `ppcontent.json`
  and returns the `WeaponDef`s it built. `Instance.Entry.Directory` is your own mod folder. The
  logging callback is what prints the `ct_weapon PASS` lines below.

### The `.csproj`

`package.ps1` builds the first `*.csproj` it finds in your mod folder, then looks for
`bin\Release\**\<FolderName>.dll` — so **`<AssemblyName>` must equal your mod's folder name**, and
`meta.json` must declare that same name. Reduced from the demo's `WeaponAdd.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <AssemblyName>WeaponAdd</AssemblyName>
    <RootNamespace>Morgott.WeaponAdd</RootNamespace>
    <TargetFramework>net472</TargetFramework>
    <LangVersion>latest</LangVersion>
    <AppendTargetFrameworkToOutputPath>false</AppendTargetFrameworkToOutputPath>
    <EnableDefaultCompileItems>false</EnableDefaultCompileItems>
    <!-- The loader loads "<FolderName>\<FolderName>.dll", so the output folder name
         must equal the assembly name. -->
    <OutputPath>bin\$(Configuration)\WeaponAdd\</OutputPath>
  </PropertyGroup>
  <ItemGroup>
    <Compile Include="src\**\*.cs" />
    <None Include="meta.json" CopyToOutputDirectory="PreserveNewest" />
  </ItemGroup>
  <PropertyGroup>
    <PPRoot Condition="'$(PPRoot)' == ''">D:\Steam\steamapps\common\Phoenix Point</PPRoot>
    <ModSDK>$(PPRoot)\ModSDK</ModSDK>
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
  </ItemGroup>
</Project>
```

`net472`, and `<Private>false</Private>` on **every** reference — see
[the reference rule](reference.md#10-the-two-rules-that-are-not-negotiable), which is the thing on
this page most likely to break somebody else's game if you get it wrong. The demo's own `HintPath`
points into its build tree; the one above points at the copy the player already has installed, which
is what you want in your own mod.

And `meta.json` names it:

```json
{ "ID": "morgott.demo.weaponadd", "AssemblyName": "WeaponAdd.dll",
  "Dependencies": [ "com.morgott.ContentTool" ] }
```

## 4. The commands, and what they print

**These are AUTHOR commands. A player never types one** — the keys are published when the mod is
ticked on and un-published when it is ticked off, same session, no restart. You need them in the dev
loop: after `ct_project` re-bakes your bundle, `ct_catalog apply` re-publishes without toggling the
mod, and `ct_catalog verify` prints the evidence.

```text
ct_project WeaponAdd            # bake this mod's own bundle from its Content\ folder
ct_catalog apply WeaponAdd      # re-publish the keys LIVE after a re-bake - author only
ct_catalog verify
ct_catalog status               # what is published right now
```

Then **start a NEW campaign** — the weapons go into starting storage, so an existing save will not
have them.

At mod-enable, `Player.log` names what is actually bound rather than asserting it:

```text
A1-def PASS 'Vulture PDW' (Morgott_VulturePDW_WeaponDef) cloned from PX_LaserPDW_WeaponDef; icon ok;
        10 + 10 clip(s) in StartingStorage of 4 difficulty def(s);
        tuning dmg 40->60 spread 2->3 range 20->13 (source intact);
        vfx 'E_VisualEffects [PX_LaserPDW_WeaponDef]' flash=VFX_WPN_PX_LaserPDW_MuzzleFlash
        shell=none projectile=E_ProjectileVisuals [PX_LaserPDW_WeaponDef]
A1-prefab PASS 'sniper' loaded from key c7a9f1d24b6e4a3c8f5b7d1e9a2c4b60;
        EXT_ShootPoint/EXT_AimPoint/EXT_AimIKPoint/EXT_ShellPoint fitted
```

### How to learn your donor's own numbers — before you pick yours

`ct_list defs` prints def **names**, never their fields, so nothing on this site tells you what
`AN_ShreddingShotgun_WeaponDef` actually deals. **There is no read-out command. The way to get the
numbers is to bake once and read them off the log**, and the `tuning` line is built to be read that
way: both sides of every arrow come off the live defs, and the left-hand side is the **donor's own
shipped value**.

So the honest first pass is: write the entry with **no `damage` and no `spread` at all**, tick the mod
on, and read the line. With neither key set the engine writes neither number, so **both sides of every
arrow are the donor's own value** and the line is a read-out of the gun you cloned. Then put your
numbers in and read the same line again to confirm they landed. In the captured run above the PDW's
donor reads `dmg 40 … spread 2 … range 20` on the left of its arrows, which is exactly what an
untuned first pass would have shown you on both sides.

That costs one enable, not one campaign: the lines are written at **mod-enable**, long before a
campaign exists. And if your donor is one of the three the demo uses, its numbers are already in
[the measurement table below](#the-measurement).

`(source intact)` is the line to read. The damage payload is **deep-copied** before either number is
written, because it is a plain serializable class and not a def — without the copy, tuning your clone
would permanently re-tune the player's *shipped* weapon for the session.

### The measurement

Read back off the live defs, one run, with the donors as controls in the same run:

| Def | `SpreadDegrees` / `EffectiveRange` |
|---|---|
| `PX_LaserPDW` (donor) | **2 / 20** — still shipped |
| `PX_AssaultRifle` (donor) | **1.6 / 25** — still shipped |
| `SY_LaserPistol` (donor) | **1.5 / 27** — still shipped |
| `Morgott_VulturePDW` | **3 / 13** |
| `Morgott_VultureAR` | **2.4 / 17** |
| `Morgott_VultureSidearm` | **2.25 / 18** |

And the three published model keys resolved, in the same run, through the game's own Addressables:
`…4b60` → GameObject **`sniper`**, `…4b61` → **`ar181`**, `…4b62` → **`taupistol`**, each out of
`WeaponAdd.bundle`. The forged external reference resolved to shader **`Standard`** — a dangling one
reads `Hidden/InternalErrorShader`.

!!! note "All three wear their own art"
    Measured off the live engine in one run, with each donor read as a control in the same run: the
    PDW's prefab `sniper` is **8249** verts against the donor's 3305, the AR's `ar181` is **5778**
    against 5554, the Sidearm's `taupistol` is **4582** against 2750. Six keys, six meshes, six
    vertex counts — the count is the discriminator, because a key that *resolves* is not yet a weapon
    that *wears* it. Each prefab carries 6 transforms (root + mesh + the four `EXT_` sockets) on
    shader `Standard`, so the `deps` row is doing its job too.

## Fire, burning and other effects

**Fire is pure data and needs no engine work.** The shipped flamethrower sets its payload's damage
type to `Fire_StandardDamageTypeEffectDef` and carries **two** keyword pairs — a damage keyword *and*
`Burning_DamageKeywordEffectorDef` at 40. Igniting a target is that second pair, and it is two fields
in your manifest:

```json
"damagetype": "Fire_StandardDamageTypeEffectDef",
"keywords":   "Burning_DamageKeywordEffectorDef=40"
```

### `damage` and `keywords` both write the keyword list — how they divide it

They **merge**, and on the demo's Sidearm they cannot collide:

- **`keywords` never replaces the list.** For each `DefName=value` you write, the engine looks for a
  pair the clone already carries for that def. Found → it overwrites that pair's value. Not found →
  it appends one new pair. Everything the donor carried and you did not name is left alone. Two pairs
  of the same keyword would be summed twice by the game, which is why it overwrites rather than adds.
- **`damage` is narrower.** It writes its number into every pair whose keyword *applies standard
  damage*, and touches nothing else.
- So `"damage": "75"` and `"keywords": "Burning_DamageKeywordEffectorDef=40"` hit **disjoint pairs**:
  the burn is an effector, not standard damage. The first sets how hard the bolt hits, the second
  sets the target alight, and neither reads the other.
- If you ever did name a standard-damage keyword in `keywords`, **`keywords` wins** — it is applied
  after `damage`.

A keyword def that does not exist is reported `NOT FOUND` by name in the log rather than skipped: a
typo here is a weapon that quietly deals no fire, which in play is indistinguishable from fire that
does not work.

**Swapping to a different shipped weapon's look is one assignment** — the visual effects and the
projectile visuals are both plain def fields, so putting a laser rifle's flash on a ballistic gun is a
one-liner in your own code. Authoring a *new* effect means a particle-system prefab in your own
bundle, published under its own key exactly like the model — worth it when no shipped effect can
express the weapon, and not worth it otherwise.

## 5. Bake and package

```powershell
ct_project WeaponAdd                 # in game, after changing the model or the manifest
$PP = 'D:\Steam\steamapps\common\Phoenix Point'   # your own game folder
.\package.ps1 -Project "$PP\Mods\WeaponAdd"  # with the game shut - builds your .csproj and stages the DLL
```

Commit `Dist\WeaponAdd.bundle`.

!!! success "A weapon with no model of its own packages too — and there is nothing to bake"
    `"model"` is optional, so the smallest legal weapon mod is `meta.json` + `ppcontent.json` + your
    `.dll`, with no `Content\` and no `Dist\` at all. **That shape packages.** It has no bundle
    because it has no asset, so `ct_project` is not part of its loop — the only steps are the build
    and the package:

    ```powershell
    .\package.ps1 -Project "$PP\Mods\ModelessGun"
    ```

    ```text
    PACKAGED 3 file(s), 412 B into ...\dist-package\ModelessGun
    Zip the FOLDER itself, so the archive holds ModelessGun\meta.json, and upload it. ...
    ```

    Three files: `meta.json`, `ppcontent.json` and `ModelessGun.dll`. The packager asks whether the
    mod ships a **payload** — a file that is not paperwork, *or* a manifest that declares a rung —
    and a `"weapons"` array is a rung. What it still refuses is a folder with neither: an empty
    manifest beside a `meta.json` is a mod a player installs for no effect. See
    [the refusal table](reference.md#every-refusal-and-what-it-means).

## 6. How a player installs it

Unzip into `Phoenix Point\Mods\`, tick it on, **start a new campaign**. Nothing is written into the
game files: the model is served out of *your own* bundle under *your own* catalog key.

**Un-ticking it mid-session is the one asymmetric part of this rung, and it is worth saying plainly.**
The two halves come apart:

| Half | Ticked ON mid-session | Ticked OFF mid-session |
|---|---|---|
| the model keys (your `publish` rows) | published immediately | **un-published immediately**, no restart — the same as every other `publish` mod |
| the weapon **defs** your DLL built | built once, when the mod is enabled | **they stay for the rest of the session.** Nothing removes a def from the repository, and the demo assembly has no `OnModDisabled` |

So a player who switches a weapon mod off mid-game still has the weapon in the def repository and in
any campaign that was created while it was on — but its prefab key is gone, so the gun no longer has
a model to wear. **Restart the game for a clean undo**; nothing was installed anywhere, so a restart
is all it takes. A weapon that never declared a `"model"` has no key half at all, and simply stays
until the restart.

There is no catalog edit to revert either way.

## 7. Discovery and the dependency line

`"Dependencies": [ "com.morgott.ContentTool" ]`. Keys are published and un-published on the checkbox.

**That line is also what makes the assembly reference above legal.** The mod manager enables and
loads a dependency before its dependents, so `ContentTool.dll` is already in memory by the time your
code first mentions one of its types. Drop the dependency line and keep the reference and you have
built a mod that can be installed with nothing to resolve against. See
[the reference rule](reference.md#10-the-two-rules-that-are-not-negotiable) for what may and may not
be referenced, and for the reflection form you want instead if your mod should still load when
ContentTool is absent.

## 8. When it does not work

| Line or symptom | What it means |
|---|---|
| the gun renders as `Hidden/InternalErrorShader` | the `deps` entry is missing from your `publish` row. |
| `ct_catalog apply` refuses your key by name | `asset` names something your bundle does not contain, or the key is already claimed. A duplicate key does not degrade — it makes Addressables initialisation throw and the game unlaunchable for **every** installed mod, which is why it is refused twice over. |
| `ID <guid> is claimed by both <a> and <b>` — *"Give them `"guid"` values that differ"* | two `weapons` entries share a guid. |
| the muzzle flash appears in the middle of the stock | `EXT_ShootPoint` is wrong. |
| *"Can't find … projectile origin"*, then an empty-array index | no `EXT_ShootPoint` at all. |
| *"has a shell prefab but invalid shell ejection point"*, every shot | the ejection socket is missing or does not start with `EXT_`. |
| the gun is held and fired like the wrong kind of weapon | you cloned the wrong class. The animation set comes off the tags. |
| the inventory cell shows the donor's picture | you set no `"icon"`. |
| the weapon arrives with one magazine and no way to reload | its ammunition is not in starting storage. Set `clips`. |
| the weapon is not in the game at all | starting storage is read when a **campaign is created**. An existing save has already been filled. |
| your donor's *shipped* stats changed too | the payload was not deep-copied. The `A1-def PASS` line reports the source's own numbers back — read `(source intact)`. |
| the whole game's mods switched themselves off | a reference in your `.csproj` could not be resolved when your code was first run. It is **not** the `ContentTool` reference — see [the reference rule](reference.md#10-the-two-rules-that-are-not-negotiable). |
| `package.ps1` says your package *"ships nothing at all"* | your mod ships no file beyond its paperwork **and** its `ppcontent.json` declares no `"weapons"` (or `replace` / `publish` / `sounds` / `creature`) row — most likely the manifest is not the one you edited, or the array is misspelled. A weapon mod with no model of its own is **not** this case and packages normally. See §5. |
| `ct_weapon PASS '<your id>' already built this session`, and you get the *other* mod's gun | another installed weapon mod declared the **same `"guid"`**. The def repository is keyed on it, so the first mod to be enabled wins and every later entry with that guid resolves to its def — your `id`, `name`, `damage` and model are all silently ignored. There is no refusal across mods: the duplicate check runs *inside* one manifest only. Generate the guid at random per weapon, never copy one off this page. |
| the bake refuses your `.glb` saying nothing says which mesh is the model | a **rigged** file with several meshes and no armature driving any one of them — the reader cannot tell which mesh the creature is. A **static** multi-mesh prop is fine and needs no Blender work: the pieces are merged into one mesh whose submeshes are its distinct materials. Two of the demo's three weapons are exactly that (14 meshes / 3 materials, and 9 meshes / 1 material) and both are worn in game. |

## Honest limits

- **New campaigns only.** Starting storage is read when a campaign is created.
- **No research, no manufacture, no vendor.** The gun cannot be built or bought.
- **Every projectile is a shipped one.** Of the 168 shipped projectile defs, 129 set a flying-bolt
  prefab and 112 set an impact prefab — both plain objects a baked bundle could serve. What is
  missing is what goes *in* them: a particle system cannot be serialised by the bake at all, and an
  emissive material needs shader keywords and a custom render queue the bake does not write. So a
  cyan bolt mesh bakes fine today; it just will not *glow*, and it cannot throw sparks.
- **There is no beam in Phoenix Point, so a "laser" fires a BOLT.** The field a continuous beam would
  use is set by **0 of the 168** shipped projectile defs — every laser in the game, including the
  Synedrion ones, throws a travelling bolt.
- **The material on an imported model is Unity's `Standard` with one texture.** No normal map, no ORM.
