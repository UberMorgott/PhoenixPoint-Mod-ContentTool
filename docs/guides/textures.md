# Textures, sprites and icons

Two rungs on one page, because the question *"how do I change that picture?"* has **two different
answers in Phoenix Point** and telling them apart is most of the job.

| The picture is… | Route | Code? |
|---|---|---|
| a **Texture2D inside an asset bundle** — a body albedo, a weapon's five maps, anything on a 3D surface | one `"texture"` row in `ppcontent.json` | **no** |
| a **Sprite a def points at** — the inventory cell, a UI icon | write the def field from your own assembly | **yes** |

The second one is not a smaller version of the first. A Sprite like
`UI_PX_WeaponIcon_AssaultRifle_INV` does not live in an Addressables bundle at all — it sits in the
game's own serialized data with no catalog row, so the bundle route has nothing to aim at. The def
**field** is the seam the game itself reads, so that is the seam you take.

---

## The texture rung

### 0. Find the two names

A `"replace"` row needs a **shipped bundle file** and an **asset name inside it**, and so does
`ct_extract`. You are not expected to know either. `ct_list`, in the developer console
(`` ` `` opens it — [how](reference.md#opening-the-developer-console)), is what turns *"I want to
repaint the acidworm"* into those two strings. A real session:

```text
> ct_list bundles acidworm
1 bundle(s) match 'acidworm' in D:\PP-Instance2\PhoenixPointWin64_Data\StreamingAssets\aa\StandaloneWindows64
  aln_acidworm_assets_all.bundle

> ct_list assets aln_acidworm_assets_all.bundle Texture2D
aln_acidworm_assets_all.bundle: 10 of 276 assets match type~'Texture2D' name~''
  Texture2D acidworm_low_emissive 208B pathId=-6405678148289476329
  Texture2D fireworm_low_occlusion 208B pathId=-4087150369143416854
  Texture2D fireworm_low_normal 204B pathId=-1012142131130513509
  Texture2D fireworm_low_metallic 208B pathId=3954371988367830667
  Texture2D fireworm_low_emissive 208B pathId=4199964918287920890
  Texture2D fireworm_low_albedo 204B pathId=4464233610045586755
  Texture2D acidworm_low_occlusion 208B pathId=5072966301567840545
  Texture2D acidworm_low_normal 204B pathId=5742481235256295860
  Texture2D acidworm_low_albedo 204B pathId=7713569524997360924
  Texture2D acidworm_low_metallic 208B pathId=8177958898325892145
```

`aln_acidworm_assets_all.bundle` + `acidworm_low_albedo` — that is the manifest row below, filled in.

Read that listing rather than skimming it: **the acidworm's own bundle also carries six *fireworm*
textures.** A bundle is not one creature, which is why the sharing question above is answered here
and not by guessing from a file name. Full syntax, filters and the loose video/audio listings are in
[the shared reference](reference.md#finding-a-target-ct_list).

### 1. The folder

```text
MyTexture\
  meta.json
  ppcontent.json
  Content\
    Textures\
      acidworm.png              your image, as you painted it
  Dist\
    MyTexture.bundle            written by `ct_project` - COMMIT AND SHIP IT
```

No DLL, no `Icons\`, nothing else.

### 2. The manifest, field by field

This is the whole of a working one-texture mod:

```json
{
  "id": "morgott.demo.nodeptexture",
  "bundle": "NoDepTexture.bundle",

  "replace": [
    { "bundle": "aln_acidworm_assets_all.bundle", "asset": "acidworm_low_albedo", "texture": "acidworm" }
  ]
}
```

| Field | Value | Notes |
|---|---|---|
| `id` | `morgott.demo.nodeptexture` | must equal `meta.json`'s `ID` |
| `bundle` | `NoDepTexture.bundle` | **your own** output bundle; required even here |
| `replace[].bundle` | `aln_acidworm_assets_all.bundle` | the **shipped** bundle that holds the target |
| `replace[].asset` | `acidworm_low_albedo` | the Texture2D's name, **unique inside that bundle** |
| `replace[].texture` | `acidworm` | the **stem** of `Content\Textures\acidworm.png` |

Add one row per image. `WeaponMesh` replaces a rifle's whole material set with six rows against one
bundle — five textures and the mesh:

```json
"replace": [
  { "bundle": "px_equipment_assets_all.bundle", "asset": "WPN_PX_RG_Assault_Rifle_T01_V01",           "mesh":    "rifle" },
  { "bundle": "px_equipment_assets_all.bundle", "asset": "WPN_PX_RG_Assault_Rifle_T01_V01_albedo",    "texture": "rifle_albedo" },
  { "bundle": "px_equipment_assets_all.bundle", "asset": "WPN_PX_RG_Assault_Rifle_T01_V01_normal",    "texture": "rifle_normal_flat" },
  { "bundle": "px_equipment_assets_all.bundle", "asset": "WPN_PX_RG_Assault_Rifle_T01_V01_metallic",  "texture": "rifle_metallic_flat" },
  { "bundle": "px_equipment_assets_all.bundle", "asset": "WPN_PX_RG_Assault_Rifle_T01_V01_occlusion", "texture": "rifle_occlusion_white" },
  { "bundle": "px_equipment_assets_all.bundle", "asset": "WPN_PX_RG_Assault_Rifle_T01_V01_emissive",  "texture": "rifle_emissive_off" }
]
```

!!! warning "A mesh swap is never just a mesh"
    The shipped material keeps pointing at the shipped maps, and those were painted for the shipped
    **UV layout**. Replace only the geometry and another model's panel lines, wear and ambient
    occlusion smear across your surface at the wrong places — which reads as a bug, not as a mod.
    That is why five of those six rows are textures, four of them 4×4 neutral colours:
    flat normal `(128,128,255,128)`, metallic `(179,179,179,102)`, white occlusion, black emissive.

### What your image file may be — the whole rule

**Almost nothing is required of it.** The bake decodes your file with the engine's own image decoder
and writes the pixels it got, at the size it got them:

| Question | Answer |
|---|---|
| Which file types? | **`.png`, `.jpg`/`.jpeg`, and nothing else.** They are what Unity's own `Texture2D.LoadImage` decodes, which is the only decoder in the process. |
| Must it match the shipped texture's size? | **No.** The shipped Acidworm albedo is 1024×1024 and a 256×256 replacement is what this page's own fixture ships. |
| Must it be square? A power of two? | **No, and no.** Measured: a **300×150** source baked and read back `300x150 RGBA32`, `PASS`. Nothing in the tool rounds, pads or rejects a size. |
| Is there a maximum? | No enforced one — but see the cost rule below; the bundle grows as width × height × 4 bytes. |
| Is alpha kept? | **Yes, byte for byte.** Measured: a source painted at 50 % alpha baked to `px[0,0]=255,0,0,128`. Whether the *shader* on the shipped material does anything with it is the material's business, not yours — an opaque body albedo ignores it. |
| sRGB or linear? | **Supply ordinary sRGB art** — what any paint program saves. The bake stamps the replacement `m_ColorSpace = 1` (sRGB) so the engine does the sRGB→linear conversion on sample, exactly as it does for the shipped map. **The corollary matters:** a normal, metallic, occlusion or roughness map is *linear data* and will be tagged sRGB too. Those still work as flat neutral fillers (the rifle above uses four), but a detailed one will read wrong. |
| Colour depth, palette, interlacing? | Irrelevant — whatever the decoder accepts is normalised to RGBA32. |

Two consequences worth knowing before you paint:

- **A replaced Texture2D is written uncompressed RGBA32 with one mip.** So a 2048 source is 16 MB
  inside the patched bundle against 4 MB at 1024. Measured on the shipped rifle: **2048×2048,
  fmt=10 (DXT1), mips=12** became **1024×1024, `RGBA32`, mips=1**. One mip also means no
  distance filtering — a very fine pattern will shimmer where the shipped map would not.
- **Check whether the shipped texture is shared** — `ct_list assets <bundle> Texture2D` prints
  everything of that type in the bundle, and that listing is the answer. Aim a replacement at a map
  that rides a shared atlas and you repaint half the armoury. The rifle above was the right target
  precisely because its five Texture2Ds carry its own name. §0 above has the real listing.

### 3. The commands, and what they print

You only run these when **you** change `acidworm.png`.

```text
ct_extract tex <shipped bundle> <asset name>     read the shipped texture, to see what you are replacing
ct_project MyTexture                             bake Content\ -> Dist\MyTexture.bundle
ct_route7 status                                 what is redirected right now
```

**`ct_extract tex`** writes the shipped image out as a `.png` you can paint over, and its tail line
tells you what you are up against — here 1024×1024, `fmt=10` (DXT1), 11 mips:

```text
> ct_extract tex aln_acidworm_assets_all.bundle acidworm_low_albedo
ct_extract wrote C:/Users/<you>/AppData/LocalLow/Snapshot Games Inc/Phoenix Point\ContentTool\Extracted\aln_acidworm_assets_all\acidworm_low_albedo.png (919869 B) from w=1024 h=1024 fmt=10 mips=11 bytes=699064 from=CAB-de0c44180a522f9de32526d400ae31d9.resS@7689704+699064
```

**`ct_project`** ends on one of exactly two lines. This is a real one-texture bake, start to finish:

```text
> ct_project NoDepTexture
project 'morgott.demo.nodeptexture' at D:\PP-Instance2\Mods\NoDepTexture: 1 texture(s), 0 mesh(es), 0 model(s), 0 video(s), 0 sound(s), 1 replacement(s)
patch aln_acidworm_assets_all.bundle: 'acidworm_low_albedo' <- acidworm 256x256
WROTE C:/Users/<you>/AppData/LocalLow/Snapshot Games Inc/Phoenix Point\ContentTool\Patched\morgott.demo.nodeptexture\aln_acidworm_assets_all.bundle 4999442 B as aacab30947f9c740247e47cc63254879.bundle / CAB-de0c44180a522f9de32526d400ae31d9 (shipped source is 4986241 B)
P1 PASS every replaced Texture2D in aln_acidworm_assets_all.bundle reads back its new pixels
P1-ctl-shipped PASS the shipped aln_acidworm_assets_all.bundle does NOT contain them - it was never written
copies ready in C:/Users/<you>/AppData/LocalLow/Snapshot Games Inc/Phoenix Point\ContentTool\Patched\morgott.demo.nodeptexture - nothing to install: ticking 'NoDepTexture' on in the mod manager redirects them (dev-only shortcut: ct_route7 apply NoDepTexture)
deleted stale D:\PP-Instance2\Mods\NoDepTexture\Dist\NoDepTexture.bundle
A6-ctl-junk PASS 4096 random bytes named .ogg must NOT decode -> ...  [CONTROL: must NOT decode]
A6-ctl-junk PASS 4096 random bytes named .mp3 must NOT decode -> ...  [CONTROL: must NOT decode]
A6-ctl-name PASS .flac/.m4a/.aac/.wma/.opus are refused BY NAME before any decode, and .wav is not -> ...  [CONTROL: must refuse]
WROTE D:\PP-Instance2\Mods\NoDepTexture\Dist\NoDepTexture.bundle 177572 B as morgott_demo_nodeptexture / CAB-morgott_demo_nodeptexture
TEX PASS assets/morgott.demo.nodeptexture/textures/acidworm -> 256x256 RGBA32 px[0,0]=0,255,64,255
ct_project: ALL PASS - D:\PP-Instance2\Mods\NoDepTexture\Dist\NoDepTexture.bundle
```

Five things in that are worth reading rather than scrolling past: the patched copy of the shipped
bundle goes to **AppData**, never into the game; `P1-ctl-shipped` is the control that says so;
`deleted stale …` is the previous bake being removed so it cannot masquerade as this one (it is
absent on a first bake, when there is nothing to delete); the three `A6-ctl-…` lines are the tool's
own audio-decoder controls, which run on every bake and say **nothing about your project** — they
name files like `song.flac` that you do not have; and the last line names the file you commit and
ship. The failure form is:

```text
ct_project: <n> FAILURE(S)
```

**`ct_route7 status`** answers what is live right now — one line per redirected shipped bundle, and
what an older ContentTool might have left behind in the installation (here: nothing):

```text
> ct_route7 status
live bundle redirections: 3 (transform func installed)
  morgott.demo.materialtweak -> aln_fireworm_assets_all.bundle = C:/Users/<you>/AppData/LocalLow/Snapshot Games Inc/Phoenix Point/ContentTool/Patched/morgott.demo.materialtweak/aln_fireworm_assets_all.bundle (crc 3770137363 -> 0)
  morgott.demo.nodeptexture -> aln_acidworm_assets_all.bundle = C:/Users/<you>/AppData/LocalLow/Snapshot Games Inc/Phoenix Point/ContentTool/Patched/morgott.demo.nodeptexture/aln_acidworm_assets_all.bundle (crc 1151466550 -> 0)
  morgott.demo.weaponmesh -> px_equipment_assets_all.bundle = C:/Users/<you>/AppData/LocalLow/Snapshot Games Inc/Phoenix Point/ContentTool/Patched/morgott.demo.weaponmesh/px_equipment_assets_all.bundle (crc 3454164017 -> 0)
live published keys: 3
  morgott.demo.weaponadd -> key 'c7a9f1d24b6e4a3c8f5b7d1e9a2c4b60' = assets/morgott.demo.weaponadd/models/sniper in D:/PP-Instance2/Mods/WeaponAdd/Dist/WeaponAdd.bundle
  ...
legacy: none - there is no D:/PP-Instance2/PhoenixPointWin64_Data/StreamingAssets\aa\catalog.json.ct-edits, so nothing an older ContentTool wrote is left in this installation
```

Your own mod not being in that list, with the mod ticked on, is the fastest way to see that the
`replace` row never matched.

And at mod-enable, in `Player.log`:

```text
ct_content: 'morgott.demo.nodeptexture' is ON in the mod manager, so its live registrations were installed at startup.
1/1 bundle(s) redirected LIVE for 'morgott.demo.nodeptexture' - nothing was written to the game installation
```

**That second line is your proof, and the count is bundles, not rows.** `WeaponMesh` aims six rows —
five textures and a mesh — at one shipped bundle and prints `1/1` as well. The other `ct_` line your
mod produces at startup counts **videos**, so on a texture mod it reads `0 clip(s) served in memory`,
which is correct and not a failure. All three lines and which is which:
[the reference](reference.md#which-success-line-is-yours-there-are-three-and-they-count-different-things).

### Seeing it, not just reading about it

`ct_route7 status` and the log line say the redirect is **live**; they cannot say the picture looks
right. There is no asset viewer in the game, so looking at it means putting the thing on screen: this
target is a creature's body albedo, so start or load a tactical mission with an Acidworm in it. A
main menu that looks unchanged proves nothing. Make the first version of your image unmistakable — a
neon checkerboard settles it at a glance where a subtle repaint leaves you guessing.
[The three steps in full](reference.md#looking-at-it-not-just-at-the-log).

### 4. Bake and package

```powershell
# in game, once, after editing the .png
ct_project MyTexture

# with the game shut, from a checkout of ContentTool's source repository
$PP = 'D:\Steam\steamapps\common\Phoenix Point'   # your own game folder
.\package.ps1 -Project "$PP\Mods\MyTexture"
```

`package.ps1` copies `meta.json`, `ppcontent.json`, `Content\`, `Dist\` and your readme/licence into
`dist-package\MyTexture`. **Zip that folder, not its contents** — the archive must hold
`MyTexture\meta.json`, or a player who extracts it straight into `Mods\` ends up with a mod the game
never discovers. Full field list, the zip rule and every refusal:
[the shared reference](reference.md#6-packaging-packageps1-with-the-game-shut).

!!! danger "Never commit `Dist\Patched\`"
    The patched copy of the shipped bundle is baked **on the player's machine, out of the player's
    own game files**. Shipping one would put Phoenix Point's own assets inside your release, and the
    packager refuses it by name.

### 5. How a player installs it

Unzip into `Phoenix Point\Mods\`, start the game, tick the mod. That is all — **no bake, no console
command**. Ticking the mod off drops the redirection in the same session, no restart.

### 6. Discovery and the dependency line

```json
"Dependencies": [ "com.morgott.ContentTool" ]
```

ContentTool applies your `replace` rows for every mod the manager says is ON, one frame after it is
enabled and again whenever the player ticks you on mid-session. The dependency line is what switches
ContentTool on for your player — with it off, your code-less mod does not even load. The four-cell
measurement is in [the reference](reference.md#3-the-dependency-line-what-it-actually-buys).

### 7. When it does not work

| Line | What it means |
|---|---|
| `skipped, disabled in the mod manager` | the player has you switched off. |
| `skipped, the mod manager never discovered it (no meta.json)` | no `meta.json` — the manager cannot know you, so nothing of yours is applied. |
| `[ERROR] [Mods] Failed to enable mod '<id>', loader 'Default'` → `Loader.LoadMod() returned null!` | ContentTool is OFF and your mod ships no assembly. Declare the dependency. |
| `mod '<a>' lost <bundle> to '<b>' (one owner per shipped bundle, lowest mod id keeps it)` | another mod already claimed that shipped bundle. One bundle, one owner — the lower mod id keeps it. |
| `REMOVED: ... wrote into your Phoenix Point installation and no longer exists` | you ran `ct_route7 verify` / `revert` from an older workflow. Nothing is written to the install any more, so there is nothing to revert. Use `ct_route7 status`. |
| the picture is unchanged and there is **no** `ct_` line at all | your row's `asset` names nothing in that bundle, or names something ambiguously. `asset` must be unique inside the bundle. |

---

## The icon rung

The inventory cell **does not render the model.** It draws a Sprite the item's def points at —
`ItemDef.ViewElementDef.InventoryIcon`, with `SmallIcon` beside it. A mesh swap silently misses it.

!!! note "This rung is for re-skinning an item the game already ships"
    That is what costs code: the view def belongs to Phoenix Point, no manifest key writes it, so
    your own assembly has to. **If you are ADDING a weapon, you write none of this** — point the
    manifest's `"icon"` key at a PNG and the engine loads it and sets all three icon fields, because
    a new weapon gets a new view def of its own. See [A new weapon](weapon.md#the-inventory-icon).

### 1. The folder

```text
MyIcon\
  meta.json                     "AssemblyName": "MyIcon.dll"
  ppcontent.json
  Icons\
    rifle_inv.png               450x450 - what the shipped weapon icons measure
  src\MyIconMain.cs             ~60 lines: load the PNG, write the def field - shown below
  MyIcon.csproj                 builds it; <AssemblyName> must equal the folder name
  MyIcon.dll                    the built output, staged by package.ps1
```

`Icons\` is copied by the packager like `Content\` is, but on **this** rung ContentTool does not apply
it — your own assembly reads it, because the def you are writing is one of the game's own. (On the
[new-weapon rung](weapon.md#the-inventory-icon) the engine does apply it, because the def is yours.)

!!! success "An icon-only mod packages"
    The packager asks whether the staged folder carries a **payload**, and both halves of the tree
    above are one: the `.png` under `Icons\` is a staged file that is not paperwork, and so is your
    built `.dll`. It goes through `package.ps1` unchanged. In practice this rung usually rides along
    with a mesh or texture swap that also has `Content\`, as the demo does — but it does not have to.
    See [the packaging note](reference.md#every-refusal-and-what-it-means).

### 2. The manifest

Nothing goes in `ppcontent.json` for the icon itself. The file still needs its two required fields,
and it will normally carry the `"replace"` rows for the model half:

```json
{ "id": "morgott.demo.weaponmesh", "bundle": "WeaponMesh.bundle", "replace": [ … ] }
```

### 3. What the code does, and what it prints

At mod-enable, load the PNG into a `Texture2D`, build a `Sprite`, and write **both** fields on the
def's `ViewElementDef` — the shipped Ares names the same sprite guid for `InventoryIcon` and
`SmallIcon`, so writing one and not the other leaves half the UI on the old picture.

That is one write in your enable hook. It is also why the icon half really *is* undone by switching
the mod off, and why nothing is written into the install for it.

Here is the whole of it, from the demo. Find the view def **by its own name**, then write the fields:

```csharp
private const string ViewDefName = "E_View [PX_AssaultRifle_WeaponDef]";
private const string IconFile = "Icons\\rifle_inv.png";

public override void OnModEnabled()
{
    try { Logger.LogInfo("W1-icon " + Swap(GameUtl.GameComponent<DefRepository>())); }
    catch (Exception ex) { Logger.LogError("W1-icon FAIL threw " + ex); }
}

private string Swap(DefRepository repo)
{
    string path = Path.Combine(Instance.Entry.Directory, IconFile);
    if (!File.Exists(path))
        return "FAIL no icon at " + path + " - the mesh still swaps, the cell keeps the Ares' picture";

    ViewElementDef view = null;
    foreach (ViewElementDef d in repo.GetAllDefs<ViewElementDef>())
        if (d != null && d.name == ViewDefName) { view = d; break; }
    if (view == null)
        return "FAIL '" + ViewDefName + "' is not in the def repository - the game changed the def's name";

    string why;
    Sprite s = Load(path, out why);
    if (s == null) return "FAIL " + why;

    icon = s;
    view.InventoryIcon = s;
    view.SmallIcon = s;
    return "PASS " + ViewDefName + ".InventoryIcon and .SmallIcon now draw " + IconFile +
           " (" + s.texture.width + "x" + s.texture.height + ")";
}
```

`private static Sprite icon;` is a field, not a local, and it is not decoration — **it is what stops
Unity collecting the texture behind a live Sprite.** `Load` is the PNG decode, and it goes through
reflection rather than a reference to `UnityEngine.ImageConversionModule`; the code and the reason
are in [the reference rule](reference.md#10-the-two-rules-that-are-not-negotiable).

`OnModEnabled` is the hook, and the repository is fully populated by then — mods are started long
after the def assets load, which is the same point at which large mods rewrite hundreds of defs. The
`.csproj` is the one shown in [the weapon recipe](weapon.md#the-csproj), minus the `ContentTool`
reference, which this rung does not need: it never calls ContentTool at all.

```text
W1-icon PASS
```

Measured, on the live def: with the mod on, `PX_AssaultRifle_WeaponDef`'s `InventoryIcon.texture` is
an **unnamed standalone texture, 450, ARGB32**; the control weapon the mod does not name still reads
`UI_PX_WeaponIcon_Laser_PDW_INV` on the shared atlas `sactx-4096x4096-Uncompressed-UIAtlas_UI-c47c0ec5`,
**4096 RGBA32**. Standalone-vs-atlas is the discriminator: an icon that is still on the atlas was
never written.

### 4. Making the image

**450×450.** That is what the shipped weapon icons measure (the wide `_LR` variant is 800×450).
Render it from the same geometry the player will hold — if the cell and the hand disagree, that reads
as a bug. Any offline renderer will do; the demo uses an orthographic z-buffered rasteriser over the
mod's own `.glb`, which guarantees the two match by construction.

### 5. Bake, package, install

There is nothing to bake for an icon. `package.ps1` builds your `.csproj` first and stages the DLL,
and refuses the package if `meta.json` declares an `AssemblyName` the folder does not contain:

```text
meta.json declares "AssemblyName": "MyIcon.dll" but the package does not contain that file - the
game refuses to load the mod. Build it, or set "AssemblyName": "" for a content-only mod.
```

The player unzips and ticks it on, exactly as above.

### 6. When it does not work

| Symptom | Cause |
|---|---|
| no `W1-icon` line at all | your enable hook never ran, or threw before the write. |
| the cell shows the donor's picture | you wrote `InventoryIcon` and not `SmallIcon`, or you wrote a def other than the one the item actually uses. |
| the whole game's mods switched themselves off | a reference in your `.csproj` could not be resolved when your code first ran — most likely a Unity module out of the game's `Managed\` folder that `ModSDK\` does not ship. The game answers a failed mod load by rewriting the activated-mods list **empty**. See [the reference rule](reference.md#10-the-two-rules-that-are-not-negotiable). |
