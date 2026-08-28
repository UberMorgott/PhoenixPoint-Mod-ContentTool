# The shared reference — folder, manifest, toolchain, distribution

> Every recipe on this site assumes this page. It is the part that does not change between a texture
> and a creature: what the folder looks like, what `ppcontent.json` and `meta.json` may say, which
> two commands are the AUTHOR's, how the folder becomes a zip, and how a player ends up with it.
>
> The *contract* — what applies by itself, what the mod-manager checkbox does and does not undo per
> route, and the rules that are not negotiable — is
> [Shipping a content mod](../SHIPPING-A-CONTENT-MOD.md). Read that once; this page is the field
> reference beside it.

## 1. The folder

```text
MyMod\
  meta.json                                 the mod manager entry. Without it you are not a mod.
  ppcontent.json                            the manifest: what you replace, publish or add.
  Content\
    Textures\*.png *.jpg                    images
    Meshes\*.glb *.obj                      geometry that REPLACES a shipped mesh
    Models\*.glb                            a whole NEW model (mesh + optional armature + clips)
    Audio\*.wav *.ogg *.mp3                 sounds you ADD
    Audio\Replace\<mediaId>.wav|.ogg|.mp3   sounds you REPLACE - the FILE NAME is the target
    Videos\*.webm *.mp4 *.mov               clips
    Subtitles\*.srt                         subtitle files (CRLF line endings, see the video recipe)
  Icons\*.png                               inventory / UI images your own code assigns
  Dist\
    MyMod.bundle                            written by `ct_project MyMod`   - COMMIT AND SHIP IT
    Sounds\<mediaId>.bnk                    written by `ct_sound bake MyMod` - COMMIT AND SHIP IT
  MyMod.dll                                 ONLY if the mod needs behaviour (a hotkey, a trigger,
                                            a def field write). Content alone needs no code.
  README.md  SOURCES.md  LICENSE            copied into the package; put your attribution here.
```

Your folder must be **top-level** under `Mods\`. Phoenix Point discovers only top-level directories
that hold a `meta.json`; a folder nested inside another mod can never be listed or switched off.

Sources go in **unconverted, as downloaded**. `.png`, `.glb`, `.mp3` and the rest are decoded by
ContentTool itself — there is no converter step and no external tool in the loop.

## 2. `meta.json`, field by field

```json
{
  "ID": "morgott.demo.materialtweak",
  "Version": "1.0.0",
  "AssemblyName": "",
  "Author":      [ { "Key": "English", "Value": "Morgott" } ],
  "Name":        [ { "Key": "English", "Value": "ContentTool Demo: Material Tweak" } ],
  "Description": [ { "Key": "English", "Value": "ONLY THE INJURED FIREWORM: its damaged-skin material goes matte. One number, no art, no code.\nThe smallest content mod there is. ..." } ],
  "Dependencies": [ "com.morgott.ContentTool" ]
}
```

| Field | Required | What it does |
|---|---|---|
| `ID` | **yes** | The key the mod manager stores you under. Use a reverse-domain-ish string; it never changes. |
| `Version` | yes | Plain `major.minor.patch`. |
| `Author` / `Name` / `Description` | yes | Per-language lists. `English` is enough. |
| `AssemblyName` | only with a DLL | `"MyMod.dll"`, and see below. **Set it to `""` for a content-only mod.** Measured both ways: `""` and leaving the field out entirely both load and both apply their content, and `package.ps1` refuses neither. `""` is the one the sample above uses because it says out loud that the mod meant to have no code. |
| `Dependencies` | **declare it** | `[ "com.morgott.ContentTool" ]`. See §3. |

### `AssemblyName` — how to have a DLL at all

The field is not a switch that grants you one; it **names a file that must be in the package**. The
two halves have to agree, and a third thing has to agree with them:

1. **Your `.csproj` sets `<AssemblyName>` to your mod's folder name.** `package.ps1` builds the first
   `*.csproj` it finds in the mod folder and then looks for `bin\Release\**\<FolderName>.dll`. A
   different assembly name is a build that succeeds and a package that cannot find its own output.
2. **`meta.json` declares that same name** — `"AssemblyName": "MyMod.dll"`. Declaring a DLL the
   package does not contain is what makes the game refuse the mod, and `package.ps1` refuses first,
   by name.
3. **No `.csproj`, no DLL, and `"AssemblyName": ""`.** Nothing is built and nothing is expected.

So there is no closed loop: a content-only mod says `""` and ships no code; a mod with code ships a
`.csproj` whose assembly name, the folder name and `meta.json` all match. The recipes that need one
show a complete working `.csproj` — [weapon](weapon.md#3-the-dll-the-whole-of-it),
[creature](creature.md#the-dll-the-whole-of-it), [video](videos.md#3-what-the-dll-does-and-the-whole-of-it).

!!! note "`.dll` in the name is conventional, not load-bearing"
    Every demo but one writes `"MyMod.dll"`; the creature demo writes `"CustomCreature"` with no
    extension and loads. Write the extension — it matches `package.ps1`'s refusal text and every
    other example — but do not go hunting if you meet a mod that does not.

### The description is two surfaces, and the row shows only the first line

The mod list row prints the **first line only**, split on `\n`, and then clips that line to the row's
own pixel width with no ellipsis. Measured against the widest line that still fitted in game: keep
line 1 **≤ 110 characters**. Hovering the row builds a tooltip from the *whole* description, so
nothing is lost.

- **Line 1** — self-contained, ≤110 chars, and **the caveat goes first**. If switching the mod off
  needs a restart, or the mod does nothing until something else happens, that is the half a player
  must read before filing a bug.
- **Line 2+** — the full truth: what it does, how, what it costs, what it does not do.

## 3. The dependency line — what it actually buys

`"Dependencies": [ "com.morgott.ContentTool" ]` **gates the auto-enable, not the content.** That was
measured four ways — two `meta.json` variants (the field omitted entirely, and `"Dependencies": []`)
crossed with ContentTool ON and OFF:

| `meta.json` | ContentTool | What happened |
|---|---|---|
| `Dependencies` **omitted** | **ON** | `ct_content: 'morgott.demo.nodeptexture' is ON in the mod manager`; `1/1 bundle(s) redirected LIVE` — **the replacement applies** |
| `Dependencies` **omitted** | **OFF** | `[ERROR] [Mods] Failed to enable mod 'morgott.demo.nodeptexture', loader 'Default'` → `InvalidOperationException: Loader.LoadMod() returned null!` — **the mod does not even load** |
| `"Dependencies": []` | **ON** | identical to row 1, byte for byte |
| `"Dependencies": []` | **OFF** | identical to row 2, same exception |

Three things follow, and they are the ones readers get wrong:

- **An omitted field and `"Dependencies": []` are the same input.** Confirmed on all four cells.
- **With ContentTool on, your content applies whether or not you declared it.** ContentTool's startup
  pass asks the mod manager who is ON; it never reads anybody's `Dependencies`.
- **With ContentTool off, a code-less content mod fails to enable outright.** Phoenix Point's own
  loader returns `null` for a mod with no assembly; code-less mods are loadable *only* because
  ContentTool patches that path. So the failure is loud — but the error names the loader, not the
  missing prerequisite, and a player has no way to learn that the answer is "switch ContentTool on".

**Declare it anyway.** That is the line that turns the dead end into an auto-enable: the mod manager
switches ContentTool on for your player, and the ordering comes with it.

## 4. `ppcontent.json`, field by field

Two fields are required in **every** project, whatever it does:

```json
{ "id": "author.mymod", "bundle": "MyMod.bundle" }
```

| Field | Required | What it is |
|---|---|---|
| `id` | **yes** | Must match `meta.json`'s `ID`. Also the prefix of every address inside your own bundle. |
| `bundle` | **yes** | The name of the bundle **you** produce. Required even by a project that never builds one (a sounds-only mod declares it and it is never written). |

Everything else is optional and each belongs to one rung:

| Field | Shape | Rung |
|---|---|---|
| `replace` | array of rows, see below | texture · material · static mesh · animated mesh · shipped clip · video |
| `publish` | array of `{ key, asset, type, deps }` | add a new asset under a new catalog key |
| `sounds` | array of `{ media, file }` | replace a shipped sound |
| `creature` | object | add a new creature |
| `weapons` | array of objects | add new weapons |
| `scale` | number | file units → game units for an imported model |
| `loop` | `"ClipA, ClipB"` | which of a `.glb`'s own clips must cycle (glTF carries no loop flag) |
| `play` | `"ClipA"` | which clip a bare imported model plays |

### `replace` rows — the kind is the key you use

Each row names a shipped **bundle**, an **asset inside it by name**, and exactly one *kind* key that
says what you are swapping. The lookup is by name **and** class, so a Mesh and a Material that share
a name are still told apart.

```json
"replace": [
  { "bundle": "aln_acidworm_assets_all.bundle", "asset": "acidworm_low_albedo",        "texture":  "acidworm" },
  { "bundle": "px_equipment_assets_all.bundle", "asset": "WPN_PX_RG_Assault_Rifle_T01_V01", "mesh": "rifle" },
  { "bundle": "aln_fireworm_assets_all.bundle", "asset": "ALN_Fireworm_DMG",           "material": "_GlossMapScale=0.15" },
  { "bundle": "aln_fireworm_assets_all.bundle", "asset": "Fireworm_unfurl",            "clip":     "position*3" },
  { "video": "campaign_intro", "asset": "StreamableCopiedAssets/Videos/Factions/Phoenix/PP_Intro.webm" }
]
```

| Key | Value | Points at |
|---|---|---|
| `"texture"` | the stem of a file in `Content\Textures\` | that image |
| `"mesh"` | the stem of a file in `Content\Meshes\` | that geometry |
| `"material"` | `"<property>=<number>"` | one float in the shipped material |
| `"clip"` | `"<channel>*<number>"`, `position` or `scale` only | one channel of a shipped animation clip |
| `"video"` | the stem of a file in `Content\Videos\` | a clip. `"asset"` present = REPLACE that shipped row; `"asset"` absent = ADD under a derived key |

**`asset` must name its target uniquely inside that bundle.** `aln_fireworm_assets_all.bundle` holds
three Materials and two of them are both called `ALN_Fireworm`, so only the `_DMG` one can be
addressed at all.

**One shipped bundle has exactly one owning mod.** Two mods aiming `replace` rows at the same bundle
is refused by name — *"one owner per shipped bundle, lowest mod id keeps it"* — never silently
last-writer-wins.

## 5. The two authoring commands, and why a player never runs them

### Opening the developer console

Every `ct_` command on this site is typed into Phoenix Point's own developer console.

**Press `` ` `` — the backquote/tilde key, left of `1`.** The same key closes it; `Esc` also closes
it, `/` opens it when it is closed, and `Enter` runs the line you typed.

**Nothing has to be enabled first, as long as mods are.** The console starts locked
(`disable_console_access` is `true` at boot) and the game's own mod manager unlocks it while it
initialises — which it does on every launch on a modding-capable platform. So on any install that
can run ContentTool at all, the console is already unlocked before you reach the main menu.
Read back live from a running game with ContentTool loaded, `disable_console_access` is **`false`**.

If backquote does nothing — a keyboard layout where that key produces something else, or a build
with mods off — the game ships an unlock code. With no menu focused, type:

```text
↑ ↓ ← → S N A P S H O T
```

The console unlocks and opens by itself about a tenth of a second later.

The console is where you type; it is not where the whole answer necessarily lands. Long reports are
clipped to 80 lines in the window and written whole to `Player.log` — so when a command's output
ends in `... n more line(s) not shown`, the log has the rest.

### Finding a target — `ct_list`

Both `ct_project`'s `replace` rows and `ct_extract` need **two names**: a shipped bundle file, and an
asset inside it. `ct_list` is how you get them. It is read-only and answers in the console.

```text
ct_list bundles [nameFilter]                        which shipped bundles exist
ct_list assets <bundleFile> [typeFilter] [nameFilter]   what is inside one
ct_list videos [nameFilter]                         the loose .webm cutscenes
ct_list audio  [nameFilter]                         the loose .wem sounds, by media ID
ct_list defs   <nameFilter> [typeFilter]            the def repository - donors, damage types, keywords
```

Every filter is a case-insensitive substring, and every listing stops at 60 hits and tells you to
narrow. A real session, going from *"I want to repaint the acidworm"* to the two names a manifest
row needs:

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

That is the whole discovery step: `aln_acidworm_assets_all.bundle` + `acidworm_low_albedo`.

!!! note "`ct_list audio` is the one listing you cannot choose from"
    A loose sound's file name **is** its media ID, so the listing is 3105 bare integers and no filter
    helps. Sounds are discovered by making the game play one and watching what it posts —
    [`ct_voices`, on the sounds page](sounds.md#finding-the-sound-you-want-to-change).

**It is also the sharing check.** Notice that the acidworm's bundle carries six *fireworm* textures
as well. A bundle is not one creature, so "is this map used by something else?" is answered by
reading the listing, not by guessing from the file's name. Drop the type filter to see everything —
`276` assets here — and add a name filter to cut it down: `ct_list assets aln_acidworm_assets_all.bundle Texture2D acidworm`.

Omitting the arguments prints the usage line rather than a wall of text:

```text
> ct_list
usage: ct_list bundles [nameFilter] | ct_list assets <bundleFile> [typeFilter] [nameFilter] | ct_list videos [nameFilter] | ct_list audio [nameFilter] | ct_list defs <nameFilter> [typeFilter]
```

### Finding a DEF — `ct_list defs`

Several manifest keys do not name a *file*, they name something the game **already owns**: a weapon
to clone from, a damage type, a damage keyword, a donor character. Those are **def names**, and
`ct_list defs` is how you learn one instead of guessing it.

It reads the live def repository through the same lookup every builder in ContentTool uses — walk the
defs, compare the name — so **a name it prints is a name a manifest accepts, by construction.**

```text
ct_list defs <nameFilter> [typeFilter]
```

The name filter is **required**: this install holds 23013 defs and a bare listing would be a flood.
The type filter matches the def's class **and its base classes**, which matters more than it sounds —
see the fire example below.

**Session 1 — "I want to build a shotgun."** The `clone` field of a `weapons` entry needs a real
`WeaponDef`:

```text
> ct_list defs Shotgun WeaponDef
4 def(s) match name 'Shotgun' and type 'WeaponDef' out of 23013 in the repository
  AN_Shotgun_WeaponDef   [WeaponDef]
  AN_ShreddingShotgun_WeaponDef   [WeaponDef]
  FS_SlamstrikeShotgun_WeaponDef   [WeaponDef]
  PX_ShotgunRifle_WeaponDef   [WeaponDef]
```

Four donors, and the Phoenix one is `PX_ShotgunRifle_WeaponDef` — **not** `PX_Shotgun_WeaponDef`,
which is what the naming of every other Phoenix weapon would lead you to write. That single line is
the difference between a weapon and `ct_weapon FAIL '<name>' is not in the def repository`.

Drop the type filter to see the whole family a weapon comes with — its ammo clip, its projectile, its
skin data, its view element, its research entry — which is also how you find out what else you may
want to name:

```text
> ct_list defs Shotgun
67 def(s) match name 'Shotgun' out of 23013 in the repository
  AN_Shotgun_AmmoClip_ItemDef   [TacticalItemDef]
  AN_Shotgun_WeaponDef   [WeaponDef]
  E_Projectile [PX_ShotgunRifle_WeaponDef]   [ProjectileDef]
  E_SkinData [PX_ShotgunRifle_WeaponDef]   [SimpleSkinDataDef]
  E_View [PX_ShotgunRifle_WeaponDef]   [ViewElementDef]
  PX_ShotgunRifle_AmmoClip_ItemDef   [TacticalItemDef]
  PX_ShotgunRifle_WeaponDef   [WeaponDef]
  ... 7 more (narrow the filter)
```

**Session 2 — "I want it to set things on fire."** That is two different manifest keys, and each has
its own def type. `damagetype` is a `DamageTypeBaseEffectDef`:

```text
> ct_list defs Fire DamageTypeBaseEffectDef
1 def(s) match name 'Fire' and type 'DamageTypeBaseEffectDef' out of 23013 in the repository
  Fire_StandardDamageTypeEffectDef   [StandardDamageTypeEffectDef]
```

Read that answer carefully: you filtered on `DamageTypeBaseEffectDef` and the def's own class is
`StandardDamageTypeEffectDef`. **The type filter walks the base chain**, because the manifest key is
typed by the base — if it compared only the concrete class this search would have answered
*"0 def(s) match"*, which reads exactly like "this game has no fire damage".

`keywords` are `DamageKeywordDef`s, and the burn is a separate one from the damage:

```text
> ct_list defs Burning DamageKeywordDef
1 def(s) match name 'Burning' and type 'DamageKeywordDef' out of 23013 in the repository
  Burning_DamageKeywordEffectorDef   [DamageKeywordDef]
```

So *"a shotgun that sets things alight"* is `"clone": "PX_ShotgunRifle_WeaponDef"`,
`"damagetype": "Fire_StandardDamageTypeEffectDef"` and a `keywords` entry naming
`Burning_DamageKeywordEffectorDef` — three names, none of them guessed.

!!! warning "The two filters are not the same kind of thing, and mixing them up returns nothing"
    `ct_list defs <nameFilter> [typeFilter]` — and they match different strings:

    - **`nameFilter` is a substring of the def's own NAME**, the string that goes in your manifest.
      `Burning` matches `Burning_DamageKeywordEffectorDef`.
    - **`typeFilter` is a substring of a CLASS name, and it walks the base chain.** `DamageKeywordDef`
      matches a def whose own class is `DamageKeywordDef` *or* anything derived from it.

    So a class name typed into the **first** slot finds nothing, because no def is *named* after its
    class: searching `_DamageKeywordDataDef` as a name filter answers `0 def(s) match`, and reads
    exactly like "this game has no damage keywords". The class name belongs in the second slot.

**To browse a whole family rather than one member**, put the *type* in the type slot and something
every def name carries in the name slot. Every def in the repository is named `..._SomethingDef`, so
`Def` is the "all of them" name filter:

```text
ct_list defs Def DamageKeywordDef
```

**Run that one yourself** — it is not captured here, and the listing stops at 60 hits, so expect a
`... n more (narrow the filter)` tail and to narrow it (`ct_list defs Shred DamageKeywordDef`,
`ct_list defs Viral DamageKeywordDef`) once you can see the naming. The two single-answer searches
above *are* real transcripts, and they are the shape you want when you already know the word.

!!! note "It needs the def repository, so it needs the game past its loading screen"
    Run it from the main menu or later. Before that there are no defs and it says so:
    `ct_list defs VOID - no DefRepository yet.`

### Getting the shipped asset out — `ct_extract`

Once you have the two names, `ct_extract` writes the shipped asset to a file you can open, measure
and paint over while you author. A texture comes out as `.png`, a mesh as `.glb`.

```text
ct_extract tex   <bundleFile> <assetName>
ct_extract mesh  <bundleFile> <assetName>
ct_extract video <name>
ct_extract audio <wemName>
```

Real output, from the same session:

```text
> ct_extract tex aln_acidworm_assets_all.bundle acidworm_low_albedo
ct_extract wrote C:/Users/<you>/AppData/LocalLow/Snapshot Games Inc/Phoenix Point\ContentTool\Extracted\aln_acidworm_assets_all\acidworm_low_albedo.png (919869 B) from w=1024 h=1024 fmt=10 mips=11 bytes=699064 from=CAB-de0c44180a522f9de32526d400ae31d9.resS@7689704+699064

> ct_extract mesh px_equipment_assets_all.bundle WPN_PX_RG_Assault_Rifle_T01_V01
ct_extract wrote C:/Users/<you>/AppData/LocalLow/Snapshot Games Inc/Phoenix Point\ContentTool\Extracted\px_equipment_assets_all\WPN_PX_RG_Assault_Rifle_T01_V01.glb (381108 B) from verts=5771 tris=8572 submeshes=1 normals=True uv0=True uv1=False tangents=True bindposes=0 joints=0 nodes=0
```

The tail of each line is what you were actually replacing — the shipped texture's real size and
format, the shipped mesh's real vertex and triangle counts. Extracted files land under your
**AppData**, never in a mod folder, because Steam wipes a Workshop item's folder on update.

!!! danger "Extracted files are Phoenix Point's, not yours — where the line is"
    **Authoring with one is fine. Shipping one is not.** The two are different acts and only the
    second leaves your machine:

    - **Fine** — opening the extracted `.png`/`.glb` to see the real size, format, UV layout or
      silhouette; tracing over it; using it as an alignment guide; keeping it in a scratch folder
      outside your mod.
    - **Not fine** — putting the extracted file, or a repaint that is still mostly Phoenix Point's
      pixels or vertices, into `Content\` and shipping it. A recolour, a filter pass or a few edits
      over Snapshot's art is still Snapshot's art in your release.
    - **The test to apply**: could you have produced this file without the extracted one in front of
      you? If not, it is a derivative and it does not ship.

    `Content\` is where your **own** art goes. That is why the packager also refuses Phoenix Point's
    own bundles by name (§6) and why the patched copies of shipped bundles are built on the player's
    machine instead of travelling in your zip — see §10.

```text
ct_project <YourMod>        bakes Content\ into Dist\<YourMod>.bundle
ct_sound bake <YourMod>     bakes Content\Audio\Replace\ into Dist\Sounds\<mediaId>.bnk
```

!!! success "A download needs no bake"
    **These are AUTHORING commands.** You run them when *you* change a source file. A mod is
    redistributed with its bake OUTPUT committed in `Dist\`, so the only thing a downloader does is
    tick the mod on.

    This was measured on four demos in one launch with the mod folders deleted and re-deployed from
    their shipped files only, and with **no `ct_project` and no `ct_sound bake` issued at all**:
    ReplaceUiSounds' three sounds came back **1200 → 888 ms, 3533 → 456 ms, 2231 → 914 ms**, all
    `streaming=false(MEMORY)`; IntroVideo's theme answered `dur=6034ms mediaID=908611677
    streaming=false(MEMORY)` against 121355 ms on disk; CustomCreature passed **all 19** gate arms.
    All four had previously told the player to bake first. All four were wrong.

Both commands run in the game's **developer console**, because Unity's decoders and *your own copy of
the installation* are what produce the output. Both write only inside **your project folder**.

#### They take a bare NAME, and that name is looked up under `Mods\`

`ct_project MyMod` is a **name**, never a path — the console's own parser eats backslashes, so a
path argument could not survive being typed. The name resolves in this order:

1. `<Phoenix Point>\Mods\MyMod` — the **installed mod folder beside ContentTool**. This is the
   normal answer, and the one you want: it is the folder the mod manager lists and the player can
   switch off.
2. `<Phoenix Point>\Mods\ContentTool\MyMod` — a fallback, used only when no mod folder of that name
   exists.

**So your project has to live at `<Phoenix Point>\Mods\MyMod\` while you are authoring it**, exactly
where a player's copy of it will live. That is what makes the bake reproducible: it reads *your*
installation's shipped bundles. Confirmed in a real run — `ct_project NoDepTexture` reported
`project 'morgott.demo.nodeptexture' at D:\PP-Instance2\Mods\NoDepTexture`, while a name with no mod
folder was refused against the fallback:

```text
> ct_project NoSuchModXyz
ct_project THREW System.IO.FileNotFoundException: no ppcontent.json in D:\PP-Instance2\Mods\ContentTool\NoSuchModXyz
```

Packaging is the opposite: `package.ps1 -Project` takes a real **path**, and that path is your
project folder under `Mods\`. See §6.

`ct_project` finishes with one of two lines:

```text
ct_project: ALL PASS - <your project folder>\Dist\<YourMod>.bundle
ct_project: <n> FAILURE(S)
```

A source file the importer could not use is **reported and skipped**, never fatal — the rest of the
project still bakes:

```text
SOURCE SKIPPED: <the file, and the reason>
```

A project whose `Content\` exists but is **empty** is not an error either. It counts what it found,
finds nothing, and tells you where to put things — it does not write an empty bundle and it does not
claim success:

```text
> ct_project EmptyContent
project 'morgott.demo.emptycontent' at D:\PP-Instance2\Mods\ContentTool\EmptyContent: 0 texture(s), 0 mesh(es), 0 model(s), 0 video(s), 0 sound(s), 0 replacement(s)
nothing to bake - put .png/.jpg under Content\Textures\, .glb under Content\Models\ or .wav under Content\Audio\
```

### Checking what is live — `ct_catalog`, and what it prints

Neither of these is an authoring command; both are read-only and both are how you answer *"did my
mod actually reach the engine this session?"*

```text
ct_catalog status      what is redirected and published right now, per mod
ct_catalog verify      make the game's own Addressables resolve each published key, and report
```

`status` is a roster. Real output from a launch with four demos enabled:

```text
> ct_catalog status
live bundle redirections: 3 (transform func installed)
  morgott.demo.materialtweak -> aln_fireworm_assets_all.bundle = C:/Users/<you>/AppData/LocalLow/Snapshot Games Inc/Phoenix Point/ContentTool/Patched/morgott.demo.materialtweak/aln_fireworm_assets_all.bundle (crc 3770137363 -> 0)
  morgott.demo.nodeptexture -> aln_acidworm_assets_all.bundle = .../Patched/morgott.demo.nodeptexture/aln_acidworm_assets_all.bundle (crc 1151466550 -> 0)
  morgott.demo.weaponmesh -> px_equipment_assets_all.bundle = .../Patched/morgott.demo.weaponmesh/px_equipment_assets_all.bundle (crc 3454164017 -> 0)
live published keys: 3
  morgott.demo.weaponadd -> key 'c7a9f1d24b6e4a3c8f5b7d1e9a2c4b60' = assets/morgott.demo.weaponadd/models/sniper in D:/PP-Instance2/Mods/WeaponAdd/Dist/WeaponAdd.bundle
  morgott.demo.weaponadd -> key 'c7a9f1d24b6e4a3c8f5b7d1e9a2c4b61' = assets/morgott.demo.weaponadd/models/ar181 in D:/PP-Instance2/Mods/WeaponAdd/Dist/WeaponAdd.bundle
  morgott.demo.weaponadd -> key 'c7a9f1d24b6e4a3c8f5b7d1e9a2c4b62' = assets/morgott.demo.weaponadd/models/taupistol in D:/PP-Instance2/Mods/WeaponAdd/Dist/WeaponAdd.bundle
legacy: none - there is no D:/PP-Instance2/PhoenixPointWin64_Data/StreamingAssets\aa\catalog.json.ct-edits, so nothing an older ContentTool wrote is left in this installation
```

**If your mod declares a `replace` or a `publish` row, its absence from `status` is the finding.**
With the mod ticked on, an absent row means the `replace` row never matched, or the `publish` key
never registered — not that the redirect is invisible.

!!! note "A mod with no `replace` and no `publish` row is *supposed* to be absent"
    `status` lists exactly two things: redirected shipped bundles and published keys. A mod that
    declares neither — a **sound replacement**, which is banks in `Dist\Sounds\`, or a **weapon with
    no `"model"` of its own**, which is a manifest and a DLL — has nothing for it to list, so its
    absence is correct and says nothing about whether the mod worked. Those rungs are proved by their
    own lines instead: the `ct_sound` bank line, the `ct_weapon` line — see
    [which success line is yours](#which-success-line-is-yours-there-are-three-and-they-count-different-things).

`verify` goes further: it asks the game's own `Addressables` to load each published key and reports
what came back, with an untouched sibling asset as the control in the same run.

```text
> ct_catalog verify
C1-live PASS 3 key(s) published LIVE while the game's own catalog.json still carries its shipped 8232 keys (key-count int = 8232), i.e. nothing was written to it
C1-pub PASS the game's own Addressables resolved 'c7a9f1d24b6e4a3c8f5b7d1e9a2c4b60' to GameObject 'sniper' out of WeaponAdd.bundle (the mod's asset is 'assets/morgott.demo.weaponadd/models/sniper', so 'sniper')
C1-type PASS 'c7a9f1d24b6e4a3c8f5b7d1e9a2c4b60' declares type 'GameObject' and the game's own Addressables resolved it to GameObject 'sniper'
C1-shader PASS an external PPtr in the mod's own asset, mounted by ADDRESSABLES and by no code of ours, resolved to shader 'Standard' (expected 'Standard'; a dangling external reads 'Hidden/InternalErrorShader')
...
C1-ctl-sibling PASS '02_Bodyparts/ALN_Fireworm_BodyAll_Ready.prefab', which nobody published, still resolves to 'ALN_Fireworm_BodyAll_Ready' (shipped is 'ALN_Fireworm_BodyAll_Ready')
ct_catalog: PASS - the game's own Addressables served the mod's own bundle, and nothing was written to the installation
```

The `C1-shader` arm is the one worth understanding: your bundle references a shipped shader it does
not contain, and Addressables resolved it. A dangling external reference reads
`Hidden/InternalErrorShader` instead — which in game looks like a bright pink model, not an error.

### What version am I on — `ct_version`

```text
> ct_version
ContentTool 1.0.0.0 | build=a4a89c44 | AssetsTools.NET merged: True | classdata.tpk embedded: 289605 B
```

`build=` is the stamp of the DLL that is actually loaded. If you just rebuilt and the stamp did not
change, the game is still running the old file — every result in that session is a ghost. The other
two are the merged asset library and its class database; both must be present, or bakes fail.

## 6. Packaging — `package.ps1`, with the game shut

### Where `package.ps1` comes from

**It is not in the player download.** The release zip is the mod a player installs and nothing else —
`ContentTool.dll` and `meta.json`. `package.ps1` is an **author's** tool and it lives in
ContentTool's own **source repository**:

- <https://github.com/UberMorgott/PhoenixPoint-Mod-ContentTool> — clone it, or use the *Code →
  Download ZIP* button (also attached to each release as *Source code (zip)*).
- `package.ps1` sits at the top of that tree. Run it **from there**, pointing `-Project` at your mod
  folder; it does not have to be copied anywhere.

It needs the **.NET SDK** (`dotnet` on your `PATH`), because it compiles the packaging rule — and
your own `.csproj`, if your mod has one — rather than duplicating them in PowerShell. Nothing else.

### Running it

```powershell
$PP = 'D:\Steam\steamapps\common\Phoenix Point'   # your own game folder

.\package.ps1 -Project "$PP\Mods\MyMod"
.\package.ps1 -Project "$PP\Mods\MyMod" -Out D:\MyMod-release
```

`-Project` is the folder you have been authoring and baking in (§5). Without `-Out`, the staged
release lands in `dist-package\MyMod` beside the script.

It is a script and not a console verb on purpose: cutting a release is the one step you must be able
to do without launching the game.

**It does not bake.** It copies what `ct_project` / `ct_sound bake` already wrote into `Dist\`.

**What it actually refuses, and what it lets through.** The blanket "nothing to ship" refusal asks
whether the staged package carries a **payload**, and there are two ways to have one:

- **a staged file that is not paperwork** — anything under `Content\`, `Dist\` or `Icons\`, and your
  own built `.dll`. (Paperwork is `meta.json`, `ppcontent.json`, `README.md`, `SOURCES.md` and
  `LICENSE`.)
- **a `ppcontent.json` that declares a rung** — a `replace`, `publish`, `sounds`, `creature` or
  `weapons` key.

Either one is enough, which is what lets the two file-less rungs ship: a **material tweak** is a
`replace` row and no file at all, and a **weapon with no model of its own** is a `weapons` array plus
a DLL. A folder with *neither* — an empty manifest beside a `meta.json` — genuinely ships nothing and
is still refused, because that is a mod a player installs for no effect.

Beyond that, whether a missing bake is fatal depends on **which route** the source belongs to, and
the split is exact:

| Source under `Content\` | No bake in `Dist\` | Why |
|---|---|---|
| a texture, mesh, model, material tweak, video | **packages, and works** | the player's own machine reads `Content\` |
| `Content\Audio\Replace\<mediaId>.*` | **REFUSED, by name** | the player's machine never opens that folder |

*The bundle routes.* A project with `Content\` and **no** `Dist\` **packages successfully**, with no
warning. Measured: a one-texture content mod with its source `.png` and no bake at all staged
`6 file(s), 10133 B` and printed the normal success message. That is not a hole, because such a mod
still works — on the player's first tick ContentTool finds no patched copy, bakes one **from their
own installation**, writes the mod's own `Dist\<YourMod>.bundle` on their machine and redirects the
live location at it. Measured end to end on a folder that had never been inside a game: the target
texture read back **256×256 RGBA32, 1 mip** off the live engine, against the shipped
**1024×1024 DXT1, 11 mips**.

*The sound route is the opposite, and it used to package clean.* Nothing on the player's machine ever
reads `Content\Audio\Replace\`; the only thing loaded is `Dist\Sounds\<mediaId>.bnk`. So an unbaked
sound mod installed, enabled, printed no error and played the **shipped** sound — a silently dead
release. The packager now refuses it and names the file:

```text
REFUSED - this package is NOT publishable, and dist-package\MyMod has been deleted rather than half-written.
  REFUSED: Content\Audio\Replace\18839791.mp3 (media 18839791) - a sound replacement that was NEVER BAKED. The player's game loads only Dist\Sounds\<mediaId>.bnk; it never opens Content\Audio\Replace. Without that bank this mod installs, enables and plays the shipped sound, with nothing anywhere saying why. Run 'ct_sound bake <YourMod>' in game, then package again.
```

Run `ct_sound bake <YourMod>` and package again. Note what that refusal does *not* say: it is not
about redistributing Phoenix Point's data, and it does not print that preamble — this is your own
file, simply not turned into the thing the player loads.

**Ship `Dist\` anyway, on every route.** Committing the bake output makes the artefact you tested and
the artefact they install the same file, and it saves every player that first-tick bake. Without a
bake you are also shipping untested output: nothing has told you your sources import at all.

It is an **allowlist**, so `src\`, `tools\`, `bin\`, `obj\` and `.git\` never ride along:

```text
meta.json  ppcontent.json  README.md  SOURCES.md  LICENSE  LICENSE.md
Content\   Icons\   Dist\   + your built <MyMod>.dll if meta.json declares one
```

On success:

```text
PACKAGED 6 file(s), 10133 B into dist-package\MyMod
Zip the FOLDER itself, so the archive holds MyMod\meta.json, and upload it. The player unzips it
into Mods\ (ending up with Mods\MyMod\meta.json) or subscribes on the Workshop; the mod manager
enables ContentTool for them because meta.json declares it.
```

!!! warning "Zip the folder, not its contents — this is the one that breaks a release silently"
    ```powershell
    Compress-Archive -Path dist-package\MyMod -DestinationPath MyMod-1.0.0.zip     # yes
    Compress-Archive -Path dist-package\MyMod\* -DestinationPath MyMod-1.0.0.zip   # no
    ```
    Phoenix Point discovers only **top-level directories under `Mods\` that hold a `meta.json`**. An
    archive rooted at `meta.json` unzips to `Mods\meta.json` when a player does the obvious thing —
    drop it in `Mods\` and *extract here* — and the mod is then never listed, never enabled and never
    reported: there is no error, because nothing was ever discovered. An archive rooted at the folder
    survives that, and installs correctly however careful the player is.

### Every refusal, and what it means

A refusal **deletes the staged folder** rather than half-writing a release. All of these are printed
verbatim, naming the offending file:

| Refusal | Cause |
|---|---|
| `REFUSED: there is no <path>\meta.json. Phoenix Point's mod manager lists a folder only when it holds a meta.json, so without one nobody can install this.` | no `meta.json` |
| `REFUSED: there is no <path>\ppcontent.json. That file is what tells ContentTool what this mod replaces, publishes or adds.` | no manifest |
| `REFUSED: <out> already holds files. Name a folder that does not exist yet ...` | the output folder is not empty |
| `meta.json declares no "ID" - the mod manager keys every mod on it.` | missing `ID` |
| `meta.json does not declare "Dependencies": [ "com.morgott.ContentTool" ] - without it the player can install this mod with the engine switched off and it will silently do nothing.` | missing dependency |
| `meta.json declares "AssemblyName": "<x>.dll" but the package does not contain that file - the game refuses to load the mod. Build it, or set "AssemblyName": "" for a content-only mod.` | declared DLL not staged |
| `<file> - a PATCHED COPY of a Phoenix Point bundle. ...delete the Patched folder from your project.` | a `Patched\` folder crept in |
| `<file> - a SHIPPED PHOENIX POINT BUNDLE IDENTITY. This package may carry exactly one bundle, your own '<MyMod.bundle>'` | a `.bundle` that is not yours |
| `<file> - an INSTALL BACKUP an older ContentTool left inside the game folder.` | a `.ct-backup` / `.ct-new` |
| `<file> - an EDIT LEDGER or the game's own catalog.` | a `.ct-edits` or `catalog.json` |
| `this package ships nothing at all - no asset file, no assembly, and a ppcontent.json that declares no "replace", "publish", "sounds", "creature" or "weapons" row. Either the bake has not been run ('ct_project <YourMod>', 'ct_sound bake <YourMod>'), or the manifest never declared what this mod does.` | no payload at all — see the note below for what does and does not count as one |
| `<file> (media <id>) - a sound replacement that was NEVER BAKED. The player's game loads only Dist\Sounds\<mediaId>.bnk; it never opens Content\Audio\Replace.` | a source in `Content\Audio\Replace\` with no bank beside it. Run `ct_sound bake <YourMod>`. |

!!! note "The two file-less rungs package normally — you do not zip them by hand"
    A **material tweak** (a few numbers in `ppcontent.json`, no file at all) and a **weapon with no
    model of its own** (a manifest plus a DLL) both have no `Content\` and no `Dist\`, and both go
    through `package.ps1` like everything else: the first declares a `replace` row, the second a
    `weapons` array, and a declared rung is a payload. A real run of the second stages exactly three
    files:

    ```text
    PACKAGED 3 file(s), 412 B into ...\dist-package\ModelessGun
    ```

    `meta.json`, `ppcontent.json` and `ModelessGun.dll`. What is still refused is the folder that
    declares nothing and carries nothing — and there the message's advice does apply, because the
    usual cause really is a bake that was never run.

The bundle refusals are the important ones. **Never redistribute Phoenix Point's own data.** A patched
copy of a shipped bundle is the game's file with a few of your bytes in it — ContentTool builds those
on the *player's* machine out of the *player's* own installation, into their AppData, which is exactly
why no release has to contain one.

## 7. How a player installs your mod

1. Download your zip from your release page.
2. Unzip it into `Phoenix Point\Mods\`, so they end up with `Mods\MyMod\meta.json`.
3. Start the game, open **Mods** in the main menu, tick your mod.

Because `meta.json` declares the dependency, the mod manager switches **Content Tool** on at the same
time. Both appear as separate toggles; ContentTool stays ticked.

There is **no bake step, no console command and no install step** on the player's side.

## 8. How ContentTool discovers you

ContentTool runs one gated pass one frame after it is enabled, and again the moment the player ticks
your mod on mid-session. For **every mod the manager says is ON** it applies your `Dist\Sounds\*.bnk`,
your `ppcontent.json` `replace`/`publish` rows and your video rows. Nothing is written into the game
install; everything is per session and re-applied on every launch.

What it printed when it worked — captured from one launch, not written from memory:

```text
ct_content: 'morgott.demo.materialtweak' is ON in the mod manager, so its live registrations were installed at startup.
1/1 bundle(s) redirected LIVE for 'morgott.demo.materialtweak' - nothing was written to the game installation
ct_video: 12 content project(s) serving in memory, 3 skipped
  IntroVideo: 1 clip(s) served in memory from D:\PP-Instance2\Mods\IntroVideo; nothing in the install was written
  MaterialTweak: 0 clip(s) served in memory from D:\PP-Instance2\Mods\MaterialTweak; nothing in the install was written
  NoDepTexture: skipped, disabled in the mod manager
ct_sound: 6 shipped replacement bank(s) loaded from D:\PP-Instance2\Mods, 0 failed, 0 skipped
  MenuMusic\208540756.bnk 24583864 B -> AK_Success ...
```

### Which success line is YOURS — there are three, and they count different things

Grepping `Player.log` for `ct_` gives you lines from three different passes, so a mod with one
texture in it legitimately shows a `0` on one of them:

| Line | Printed by | Counts | You get it when |
|---|---|---|---|
| `ct_content: '<id>' is ON in the mod manager…` | the discovery gate | nothing — it is the roster verdict | always, for every enabled content mod |
| `<n>/<m> bundle(s) redirected LIVE for '<id>' - nothing was written to the game installation` | route vii | **shipped bundles** your `replace` rows patched | you replace a texture, mesh, material or clip. Six rows against one bundle still read `1/1` |
| `<YourMod>: <n> clip(s) served in memory from <path>; nothing in the install was written` | the video pass | **video rows only** | always, once per enabled content mod — `0 clip(s)` on a mod with no `"video"` row is correct and not a failure |

So a one-texture mod's proof is the **`1/1 bundle(s) redirected LIVE`** line, and its
`0 clip(s) served in memory` line says nothing about it either way. Older builds printed a
`<YourMod>: <n> replacement(s) redirected in memory` line; nothing prints it now.

### Looking at it, not just at the log

There is **no asset viewer in Phoenix Point**, and ContentTool does not add one. Seeing your content
means putting the thing that wears it on screen. That is the honest answer, and it is three steps:

1. **Confirm the redirect happened** — the `bundle(s) redirected LIVE` line above, or
   `ct_route7 status`, which lists one row per redirected shipped bundle. Your mod missing from that
   list, with the mod ticked on, means the `replace` row never matched anything.
2. **Go where the asset is used.** The bundle name tells you: `aln_acidworm_assets_all.bundle` is a
   creature, so start or load a tactical mission that has one and look at it; a weapon in
   `px_equipment_assets_all.bundle` shows up the moment a soldier holds it — the geoscape roster
   screen is enough, no mission needed; a UI sprite shows on the screen that draws it; a video plays
   where the game plays it. Nothing in a menu will show you a creature skin, so a main menu that
   looks unchanged proves nothing.
3. **Make the first version unmistakable.** A flat neon checkerboard or a wildly wrong silhouette
   settles "did it apply?" at a glance, where a subtle repaint leaves you guessing. Get that far
   first, then put the real art in.

If you want a number rather than a look — the size and format the engine is actually holding — that
is the engineer's route and it needs the developer console plus a way to call into Unity; the
measurements quoted on this site were taken that way (`Addressables` → the prefab → the renderer's
`sharedMaterial.mainTexture` → `width`/`format`/`mipmapCount`). A modder does not need it. `ct_extract
tex` on the shipped bundle in the same session tells you what you replaced (`w=1024 h=1024 fmt=10
mips=11`), which is the useful half of that comparison.

### Shared failure lines — every rung can hit these

| Line in `Player.log` | Meaning |
|---|---|
| `skipped, disabled in the mod manager` | the player has you switched off. Working as intended. |
| `skipped, the mod manager never discovered it (no meta.json)` | no `meta.json`, so the manager cannot know you and nothing of yours is applied. |
| `skipped, the mod manager could not be read` | no readable roster; nothing at all is applied. There is deliberately no "apply everything" fallback. |
| `[ERROR] [Mods] Failed to enable mod '<id>', loader 'Default'` → `InvalidOperationException: Loader.LoadMod() returned null!` | a code-less mod with **ContentTool switched off**. §3. |
| `mod '<a>' lost <bundle> to '<b>' (one owner per shipped bundle, lowest mod id keeps it)` | two mods aimed `replace` rows at the same shipped bundle. |
| `REMOVED: ... wrote into your Phoenix Point installation and no longer exists` | you called an `apply`/`verify`/`revert` verb from an older workflow. There is nothing to revert any more. |

The log is at:

```text
%USERPROFILE%\AppData\LocalLow\Snapshot Games Inc\Phoenix Point\Player.log
```

Search it for `ct_`.

## 9. Distribution — GitHub, not the Workshop

**ContentTool itself is the only thing published to Steam Workshop.** Content mods built with it —
including every demo — are distributed **from GitHub**, as a release zip, and the ContentTool Workshop
page links back to this documentation.

So the last step is: `package.ps1`, zip the output **folder** (so the archive holds
`MyMod\meta.json`, see §6), attach it to a GitHub release, and point people at it.

## 10. The two rules that are not negotiable

### Never reference an assembly nothing loads for you

Phoenix Point loads a mod with `Assembly.Load` over raw bytes and installs **no `AssemblyResolve`
handler**. So the CLR can only satisfy a reference of yours from assemblies that are *already in
memory* when your code is first run — and it answers a reference it cannot satisfy by failing the mod
load, which it in turn answers by **rewriting the activated-mods list empty, silently switching off
every other mod on the machine.** That is the worst failure in this document and it does not look
like your fault when it happens.

The dangerous references are the Unity modules under `PhoenixPointWin64_Data\Managed\`. **Reference
only what `ModSDK\` ships** — `Assembly-CSharp.dll`, `0Harmony.dll`, `UnityEngine.CoreModule.dll` —
and reach anything else **by reflection**, which turns a dead mod list into one caught exception. The
demo that needs a PNG decoder does exactly that rather than reference
`UnityEngine.ImageConversionModule`:

```csharp
Texture2D tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
Type conv = Type.GetType("UnityEngine.ImageConversion, UnityEngine.ImageConversionModule");
MethodInfo load = conv == null
    ? null
    : conv.GetMethod("LoadImage", BindingFlags.Public | BindingFlags.Static, null,
                     new[] { typeof(Texture2D), typeof(byte[]) }, null);
if (load == null)
{
    why = "UnityEngine.ImageConversion.LoadImage was not found - this install's Unity differs";
    return null;
}
if (!(bool)load.Invoke(null, new object[] { tex, File.ReadAllBytes(path) }))
{
    why = path + " is not a PNG Unity can decode";
    return null;
}
```

Note the shape, because it is the whole point: **every step can fail and say so, and none of them can
stop your mod loading.** The type may be absent, the method may be absent, the decode may refuse —
each is a returned string, not an exception and not a missing reference.

#### `ContentTool.dll` itself is the exception, and the dependency line is why

**You may reference `ContentTool.dll`**, and the weapon and creature recipes do, because your
`meta.json` declares `"Dependencies": [ "com.morgott.ContentTool" ]` and **the mod manager enables and
loads a dependency before its dependents.** ContentTool is therefore already in memory when your code
first mentions one of its types, which is the only thing the CLR needs. Two conditions, both
required:

```xml
<Reference Include="ContentTool">
  <HintPath>$(PPRoot)\Mods\ContentTool\ContentTool.dll</HintPath>
  <Private>false</Private>
</Reference>
```

- **`<Private>false</Private>`**, always. Copying `ContentTool.dll` next to your own would load a
  second, rival set of the same types beside the one the player already has.
- **The dependency line in `meta.json`**, always. Without it a player can install your mod with the
  engine switched off, and then there is nothing to resolve against.

#### When to use reflection anyway

Reference it when your mod is *meaningless* without ContentTool — a weapon or a creature mod cannot
do anything if the engine is missing, so failing loudly is honest. Reach for reflection when you want
your mod to **survive** the engine being absent or older than you expect, which is what the video
demo does for its one call:

```csharp
Type api = Type.GetType("Morgott.ContentTool.Bake.CatalogLive, ContentTool");
if (api == null) return "VOID ContentTool is not loaded - this mod depends on it and has nothing to play";
MethodInfo reg = api.GetMethod("Register", BindingFlags.Public | BindingFlags.Static);
if (reg == null) return "VOID ContentTool has no CatalogLive.Register - version mismatch";
return (string)reg.Invoke(null, new object[] { KeyFor(ModId, ClipStem), clip });
```

It costs you compile-time checking and buys a mod that degrades into a logged line instead of a
failure. Both are legitimate; referencing an unresolvable `Managed\` module is not.

### Ship your own media only

Never redistribute a Phoenix Point asset. The patched copies the bundle routes need are produced on
the player's machine, from the player's own files.
