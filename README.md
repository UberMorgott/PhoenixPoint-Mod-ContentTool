<p align="center"><img src="docs/images/banner.png" alt="Phoenix Point: Content Tool"></p>

# ContentTool

> ## ⚠️ WORK IN PROGRESS — NOT READY FOR USE
>
> This is an early build under active development. Expect bugs, expect things not to work, and
> expect the manifest format and commands to change without notice. Do not build a mod you care
> about on it yet.

[![License](https://img.shields.io/badge/license-CC%20BY--NC%204.0-blue?style=flat-square)](LICENSE)
[![Issues](https://img.shields.io/github/issues/UberMorgott/PhoenixPoint-Mod-ContentTool?style=flat-square)](https://github.com/UberMorgott/PhoenixPoint-Mod-ContentTool/issues)

**[Open the ContentTool documentation](https://ubermorgott.github.io/PhoenixPoint-Mod-ContentTool/)**

ContentTool lets Phoenix Point mods replace or add textures, materials, models, animations, sounds,
videos, creatures and weapons. It is an engine for other mods; it changes nothing by itself.

## Players

1. Download `ContentTool-*.zip` from the [latest release](https://github.com/UberMorgott/PhoenixPoint-Mod-ContentTool/releases/latest).
2. Copy the `ContentTool` folder into `Phoenix Point\Mods\`.
3. Start the game, open **Mods**, and tick **Content Tool**.

That is the whole install. If another mod declares ContentTool as a dependency, the mod manager
enables it automatically. See the [player page](https://ubermorgott.github.io/PhoenixPoint-Mod-ContentTool/#players)
if the mod does not appear.

## Modders

Start with the [documentation map](https://ubermorgott.github.io/PhoenixPoint-Mod-ContentTool/#modders),
then choose a [working demo](https://ubermorgott.github.io/PhoenixPoint-Mod-ContentTool/demos/)
or a [recipe](https://ubermorgott.github.io/PhoenixPoint-Mod-ContentTool/guides/).

A content mod is an ordinary Phoenix Point mod folder. It ships `meta.json`, `ppcontent.json`, the
media files its route needs, and baked output in `Dist\`; add a DLL only when the mod needs behaviour
such as a trigger, hotkey or def change. The [shipping walkthrough](https://ubermorgott.github.io/PhoenixPoint-Mod-ContentTool/SHIPPING-A-CONTENT-MOD/)
covers the complete authoring loop.

## Working demos

- [MenuMusic](https://ubermorgott.github.io/PhoenixPoint-Mod-ContentTool/demos/#menumusic) — replaces both editions' main-menu music with baked Wwise media, without mod code.
- [ReplaceUiSounds](https://ubermorgott.github.io/PhoenixPoint-Mod-ContentTool/demos/#replaceuisounds) — replaces three shipped geoscape UI sounds, without mod code.
- [AddUiSounds](https://ubermorgott.github.io/PhoenixPoint-Mod-ContentTool/demos/#adduisounds) — adds two sound events and uses a small DLL to play one on `Alt+B`.
- [IntroVideo](https://ubermorgott.github.io/PhoenixPoint-Mod-ContentTool/demos/#introvideo) — replaces a cutscene's video, Wwise audio and subtitles.
- [QuitCutscene](https://ubermorgott.github.io/PhoenixPoint-Mod-ContentTool/demos/#quitcutscene) — adds a video and uses Harmony to play it when quitting from the main menu.
- [WeaponMesh](https://ubermorgott.github.io/PhoenixPoint-Mod-ContentTool/demos/#weaponmesh) — replaces the Ares AR-1 mesh, five textures and inventory icon.
- [MaterialTweak](https://ubermorgott.github.io/PhoenixPoint-Mod-ContentTool/demos/#materialtweak) — changes one float on one shipped material, with no art or code.
- [NoDepTexture](https://ubermorgott.github.io/PhoenixPoint-Mod-ContentTool/demos/#nodeptexture) — measures what happens when a texture mod omits the ContentTool dependency.
- [CustomCreature](https://ubermorgott.github.io/PhoenixPoint-Mod-ContentTool/demos/#customcreature) — builds a squad creature with its own rig, clips, hitbox and attacks.
- [WeaponAdd](https://ubermorgott.github.io/PhoenixPoint-Mod-ContentTool/demos/#weaponadd) — clones weapon defs, publishes new models and applies per-weapon tuning.

## License

[CC BY-NC 4.0](LICENSE) — Morgott. Content mods built with ContentTool remain their authors' work.
