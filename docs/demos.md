# Working demos

The repository contains ten installable demo mods under
[`demos\`](https://github.com/UberMorgott/PhoenixPoint-Mod-ContentTool/tree/main/demos). Each has its
own `meta.json`, `ppcontent.json` and README. Begin with the demo closest to your mod, copy its
structure, then use the linked recipe for the full field reference and failure cases.

`ct_project` and `ct_sound bake` are authoring commands. The demos already ship their baked output
where the technique requires it; a player only installs and enables them.

## [MenuMusic](https://github.com/UberMorgott/PhoenixPoint-Mod-ContentTool/tree/main/demos/MenuMusic)

Replaces the standard and YOE/Complete Edition main-menu tracks. It teaches the content-only Wwise
replacement route: the shipped media ID is the target, and the baked `Dist\Sounds\<mediaId>.bnk` is
what ContentTool loads.

Look at `Content\Audio\Replace\`, the deliberately minimal `ppcontent.json`, and the two banks under
`Dist\Sounds\`. There is no demo DLL. Read the
[sound replacement recipe](guides/sounds.md#replacing-a-shipped-sound).

## [ReplaceUiSounds](https://github.com/UberMorgott/PhoenixPoint-Mod-ContentTool/tree/main/demos/ReplaceUiSounds)

Replaces three shipped geoscape UI sounds with three short generated clips. It teaches the explicit
`"sounds"` manifest form, where each row pairs a shipped media ID with a source filename.

Compare `ppcontent.json`, `Content\Audio\Replace\`, and `Dist\Sounds\`. The README also explains why
these targets work without a DLL and why a sound replacement is only fully undone after a restart.
Read the [sound replacement recipe](guides/sounds.md#replacing-a-shipped-sound).

## [AddUiSounds](https://github.com/UberMorgott/PhoenixPoint-Mod-ContentTool/tree/main/demos/AddUiSounds)

Adds two sound events that Phoenix Point does not ship, then plays one at random on `Alt+B`. It
teaches the boundary between content and behaviour: `ct_project` bakes the clips into the mod's own
bundle, while the DLL loads the bank and posts the new event.

Look at `Content\Audio\`, `src\AddUiSoundsMain.cs`, and `Dist\AddUiSounds.bundle`. Read the
[new-sound recipe](guides/sounds.md#adding-a-sound-the-game-never-had).

## [IntroVideo](https://github.com/UberMorgott/PhoenixPoint-Mod-ContentTool/tree/main/demos/IntroVideo)

Replaces the new-campaign intro as three separate assets: the WebM picture, Wwise audio and subtitle
`TextAsset`. The first two are content routes; the subtitle needs a small DLL because it is a field
on a def.

Look at the `"replace"` and `"sounds"` rows in `ppcontent.json`, the three source folders under
`Content\`, and `src\IntroVideoMain.cs`. The SRT checker under `tools\` documents the game's CRLF
requirement. Read the [video replacement recipe](guides/videos.md#replacing-a-shipped-video).

## [QuitCutscene](https://github.com/UberMorgott/PhoenixPoint-Mod-ContentTool/tree/main/demos/QuitCutscene)

Adds a new video key and plays it before the game exits from the main menu. It teaches that publishing
a clip is content, but inventing a place where the game plays it requires a trigger; this demo uses
a Harmony prefix. Quitting from an in-game pause screen remains unchanged.

Look at the add-shaped `"replace"` row with no `"asset"`, `Content\Videos\quit_outro.webm`, and the
Harmony patch near the end of `src\QuitCutsceneMain.cs`. Read the
[new-video recipe](guides/videos.md#adding-a-video-the-game-never-played).

## [WeaponMesh](https://github.com/UberMorgott/PhoenixPoint-Mod-ContentTool/tree/main/demos/WeaponMesh)

Replaces the Ares AR-1's mesh and five textures, then changes its inventory icon through a small DLL.
It teaches shipped-bundle replacement, coordinate fitting, texture-map replacement and the difference
between a model shown in the world and a sprite stored on a def.

Start with the six `"replace"` rows in `ppcontent.json`, then inspect `Content\Meshes\rifle.glb`,
`Content\Textures\`, `Icons\rifle_inv.png`, and `src\WeaponMeshMain.cs`. Read the
[mesh replacement recipe](guides/meshes.md#replacing-a-shipped-mesh) and the
[icon recipe](guides/textures.md#the-icon-rung).

## [MaterialTweak](https://github.com/UberMorgott/PhoenixPoint-Mod-ContentTool/tree/main/demos/MaterialTweak)

Sets `_GlossMapScale` on the damaged Fireworm material from `1` to `0.15`. It teaches the smallest
bundle-replacement manifest: one material property assignment, with no source art, `Content\` folder
or DLL.

Everything important is in the single `"material"` row in `ppcontent.json`. The README explains why
the property must exist on the material's shader. Read the [material recipe](guides/materials.md).

## [NoDepTexture](https://github.com/UberMorgott/PhoenixPoint-Mod-ContentTool/tree/main/demos/NoDepTexture)

Replaces the Acidworm albedo with a checker, but deliberately omits the ContentTool dependency. This
is a measurement fixture, not a recommended template: it demonstrates why every real content mod
must declare `"Dependencies": [ "com.morgott.ContentTool" ]`.

Compare `meta.json` with `meta.deps-empty.json`, then inspect the one texture `"replace"` row and
`Content\Textures\acidworm.png`. For the normal implementation, read the
[texture replacement recipe](guides/textures.md#the-texture-rung).

## [CustomCreature](https://github.com/UberMorgott/PhoenixPoint-Mod-ContentTool/tree/main/demos/CustomCreature)

Builds a new squad creature from a rigged GLB. It maps the model's own clips to game roles, stamps
the animation events that attacks and death wait for, fits the creature's collision and aim data,
and wires inherited melee plus a declared ranged attack.

Start with the `"creature"` block in `ppcontent.json`, then inspect
`Content\Models\cyborg_spider.glb` and the small entry point in `src\CustomCreatureMain.cs`. The
README is the detailed account of the rig, root motion, events, hitbox and donor choice. Read the
[creature recipe](guides/creature.md).

## [WeaponAdd](https://github.com/UberMorgott/PhoenixPoint-Mod-ContentTool/tree/main/demos/WeaponAdd)

Adds three weapon defs to starting storage. Two publish models from the mod's own bundle; the third
keeps its donor's model. It teaches def cloning, model-key publication, inventory icons, attachment
sockets, starting inventory and per-weapon damage, spread and effect tuning.

The useful map is `ppcontent.json`: compare its two `"publish"` rows with the three `"weapons"`
entries. Then inspect `Content\Models\`, `Icons\`, and the one-call entry point in
`src\WeaponAddMain.cs`. Read the [weapon recipe](guides/weapon.md).
