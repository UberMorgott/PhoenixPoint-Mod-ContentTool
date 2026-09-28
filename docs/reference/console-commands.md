# Console commands

Open Phoenix Point's game console and type a command on one line. `<project>` means the **folder name** of your mod, not a path. ContentTool looks for a sibling mod folder with that name and `ppcontent.json`; otherwise it looks under ContentTool's own folder.

<!-- BEGIN GENERATED: command list -->
<!-- written by tools\docs-check.ps1 -Write from src\ContentToolMain.cs; do not edit by hand -->

| Command | Who | Arguments (as the command declares them) |
|---|---|---|
| `ct_bench` | everyone | `[open\|close\|reset\|rescan\|unit <name>], none toggles; 'unit' picks a template by name (exact, or a unique substring) instead of clicking the list; 'rescan' re-reads the model catalogue (the Advanced row's Rescan - a content mod enabled after the first open), bench open or not` |
| `ct_catalog` | everyone | `apply [project] \| verify \| status` |
| `ct_creature` | everyone | `[list \| gate <tactical savename>]` |
| `ct_dump` | everyone | `<ClassName> [depth, default 3]` |
| `ct_extract` | everyone | `tex <bundleFile> <assetName> \| mesh <bundleFile> <assetName> \| video <name> \| audio <mediaId> (the .wem byte for byte plus a .wav named after the sound) \| audio --all [filter] (decodes EVERY loose media the filter matches to <ShortName>__<id>.wav, plus an index.csv of all 7692 media incl. the in-bank ones; a failing decode is counted and named, never fatal) \| gate (X1/X2/X3)` |
| `ct_fit` | everyone | `[show] \| <weapon> [<dx,dy,dz> \| move <dx,dy,dz> \| pos <x,y,z> \| turn <dx,dy,dz> \| rot <x,y,z> \| scale <f> \| save \| reload]. 'save' writes scale/rotate/offset back into the mod's OWN ppcontent.json, every other byte preserved` |
| `ct_list` | everyone | `bundles [nameFilter] \| assets <bundleFile> [typeFilter] [nameFilter] \| videos [nameFilter] \| audio [filter] (every shipped sound BY NAME, read from the game's own SoundbanksInfo.xml: '<id>  <name>  <bank>  loose\|in-bank'. The filter is a case-insensitive substring of the NAME, the id or the bank, and in-bank media are listed too - they cannot be extracted, but this is where their id is found. The pane shows the first 10 rows and then names a file; the WHOLE list is always written to ContentTool\Logs\ct_list-audio-<stamp>.txt, and a capture such as PPCLI still receives every row.) \| defs <nameFilter> [typeFilter] \| bones <bundleFile> <meshName> [nameFilter] (the skeleton a shipped Mesh is skinned to, in m_BindPose order - the names a replacement rig must spell) \| props <bundleFile> <materialName> (the property names a "material": "_Prop=value" row takes) \| clip <bundleFile> <clipName> (one named AnimationClip's fields)` |
| `ct_mission` | everyone (`gate` needs `ct-dev`) | `list \| gate <savename>` |
| `ct_package` | everyone | `<project>` |
| `ct_project` | everyone | `<project> [twice] - 'twice' runs gate B1, re-baking while the project's bundle is held open the way an enabled mod holds it. With no project, a generated sample under the mod folder is used` |
| `ct_route7` | everyone | `apply <project> \| status` |
| `ct_sound` | everyone | `bake [project] \| selftest \| probe <mediaId> \| probe event <eventId> \| shapec [mediaId] \| status [mediaId]` |
| `ct_version` | everyone | - |
| `ct_video` | everyone | `live [project] \| status \| defs \| resolve <key> \| open <key> \| play <defname> \| quit` |
| `ct_voices` | everyone | - |
| `ct_audio` | developer (needs `ct-dev`) | `source bundle file name` |
| `ct_bake` | developer (needs `ct-dev`) | `source bundle file name` |
| `ct_dev` | developer (needs `ct-dev`) | `on [project] \| off \| status \| sets \| set <name> \| next \| reload` |
| `ct_fmt` | developer (needs `ct-dev`) | - |
| `ct_liveswap` | developer (needs `ct-dev`) | - |
| `ct_meshswap` | developer (needs `ct-dev`) | - |
| `ct_music` | developer (needs `ct-dev`) | `probe [waitSeconds] \| gate <savename> (loads a save, waits for the level to play, then probes)` |
| `ct_outtest` | developer (needs `ct-dev`) | - |
| `ct_replace` | developer (needs `ct-dev`) | `<targetpath> <file\|value>. Needs ct_seamprobe on (guid:) or ct_scan on (name:)` |
| `ct_revert` | developer (needs `ct-dev`) | - |
| `ct_scan` | developer (needs `ct-dev`) | `on \| off \| status (default) \| gate` |
| `ct_seamprobe` | developer (needs `ct-dev`) | `on \| off \| report (default)` |
| `ct_texswap` | developer (needs `ct-dev`) | - |

<!-- END GENERATED -->

## Public commands

These 15 commands are registered without the `ct-dev` marker. The forms below follow their handlers, including forms omitted from some console descriptions.

| Command | What it does | Accepted forms | Example |
|---|---|---|---|
| `ct_version` | Shows the installed tool version and whether its bake resources are present. | `ct_version` | `ct_version` |
| `ct_dump` | Prints the serialized field tree for a Unity class, useful when investigating a bake. Depth defaults to 3; an invalid depth also uses 3. | `ct_dump <ClassName> [depth]` | `ct_dump Mesh 3` |
| `ct_project` | Imports a project's sources, bakes its output, and reads the result back. With no argument, creates or refreshes a generated sample project. `twice` checks a second bake while the first bundle is held open. | `ct_project`; `ct_project <project>`; `ct_project <project> twice` | `ct_project <project>` |
| `ct_package` | Builds a folder to publish from an already baked project. It does not bake or compile a DLL. Its output is under `<persistentDataPath>\ContentTool\Packaged\<project>`. Running it again removes that output folder first. | `ct_package <project>` | `ct_package <project>` |
| `ct_route7` | Applies a project's replacements of shipped bundle assets to the running game. `verify` reads back patched copies without installing them. | `ct_route7 apply <project>`; `ct_route7 verify <project>`; `ct_route7 status` (`status` is also the default) | `ct_route7 apply <project>` |
| `ct_catalog` | Publishes a project's new Addressables keys from its own baked bundle. It can also inspect the live state or verify published keys. | `ct_catalog apply [project]`; `ct_catalog verify`; `ct_catalog status` (`status` is also the default) | `ct_catalog apply <project>` |
| `ct_video` | Serves project videos through the live streamable catalog. It can list video definitions, inspect a key, try opening it, play a definition, or invoke the game's quit action. `defs` and `play` accept a name containing spaces. | `ct_video live [project]`; `ct_video status`; `ct_video defs [nameFilter]`; `ct_video resolve <RuntimeKey>`; `ct_video open <RuntimeKey>`; `ct_video play <defname>`; `ct_video quit` (`status` is also the default) | `ct_video status` |
| `ct_sound` | Bakes replacements for **existing** shipped sounds into the mod's `Dist\Sounds`; the other verbs inspect or test sound handling. | `ct_sound bake [project]`; `ct_sound selftest`; `ct_sound probe <mediaId>`; `ct_sound probe event <eventId>`; `ct_sound shapec [mediaId]`; `ct_sound status [mediaId]` (`status` is also the default) | `ct_sound bake <project>` |
| `ct_voices` | Watches the game's `PostEvent` calls and reports a timeline and live voice counts. The handler also accepts the bare command. The default watch lasts 20 seconds; values below 2 become 2. | `ct_voices`; `ct_voices watch [seconds]` | `ct_voices watch 20` |
| `ct_list` | Finds shipped bundles, assets, videos, sounds, definitions, skeleton bones, material properties, and clip fields. `ct_list audio` shows a short console preview and writes its complete list to a log file. | `ct_list bundles [nameFilter]`; `ct_list assets <bundleFile> [typeFilter] [nameFilter]`; `ct_list videos [nameFilter]`; `ct_list audio [filter]`; `ct_list defs <nameFilter> [typeFilter]`; `ct_list bones <bundleFile> <meshName> [nameFilter]`; `ct_list props <bundleFile> <materialName>`; `ct_list clip <bundleFile> <clipName>` | `ct_list audio <filter>` |
| `ct_extract` | Copies a shipped texture, mesh, video, or loose sound into editable files. `audio --all` runs in the background, one run at a time, and writes an index; `gate` runs extraction checks. | `ct_extract tex <bundleFile> <assetName>`; `ct_extract mesh <bundleFile> <assetName>`; `ct_extract video <name>`; `ct_extract audio <mediaId>`; `ct_extract audio --all [filter]`; `ct_extract gate` | `ct_extract audio <mediaId>` |
| `ct_fit` | Shows or adjusts the fit of a weapon already built in this session. A bare `x,y,z` moves it by that amount. `save` writes the current fit into that weapon's `ppcontent.json` row. | `ct_fit`; `ct_fit show`; `ct_fit <weapon>`; `ct_fit <weapon> <dx,dy,dz>`; `ct_fit <weapon> move <dx,dy,dz>`; `ct_fit <weapon> pos <x,y,z>`; `ct_fit <weapon> turn <dx,dy,dz>`; `ct_fit <weapon> rot <x,y,z>`; `ct_fit <weapon> scale <f>`; `ct_fit <weapon> save`; `ct_fit <weapon> reload` | `ct_fit <weapon>` |
| `ct_bench` | Opens the visual weapon-fit workbench. A bare command toggles it. `rescan` refreshes its model catalog even while the bench is closed. | `ct_bench`; `ct_bench open`; `ct_bench close`; `ct_bench reset`; `ct_bench rescan`; `ct_bench unit <name>` | `ct_bench open` |
| `ct_mission` | Lists tactical save names. Its `gate` form loads a save and is restricted to developer mode. Save names may contain spaces. | Public: `ct_mission` or `ct_mission list`. Developer: `ct_mission gate <savename>`. | `ct_mission list` |
| `ct_creature` | Lists custom creature templates, reports available tactical links, or loads a tactical save to check a custom creature in a mission. In `gate`, when more than one token follows it, the last token is always read as a template-name fragment and the tokens before it form the save name; a save name with spaces therefore needs a fragment after it. | `ct_creature`; `ct_creature list`; `ct_creature links`; `ct_creature gate <tactical savename> [template name fragment]` | `ct_creature list` |

Use [project files](project-files.md) for folder names and source formats, and [ppcontent.json](ppcontent-json.md) for declarations used by the bake.

!!! warning "Added sounds"

    `Content\Audio` adds new sounds to the bake and self-check, but ContentTool does not load the added bank when a player runs the mod. The mod's own DLL must load that bank or those sounds are silent. `Content\Audio\Replace` → `ct_sound bake` → `Dist\Sounds` replaces shipped sounds without a DLL. Added `.stream.wav`, `.stream.ogg`, and `.stream.mp3` sounds are not packaged.

## Developer commands

ContentTool registers commands marked `[DevOnly]` only when an empty **file** named `ct-dev` is beside `ContentTool.dll` at launch. A directory with that name does not count. Add or remove the file, then launch the game again. These commands are development instruments; some alter the running game or load a save.

- `ct_bake` — checks the bundle writer and read-back; optionally takes a source bundle file name.
- `ct_audio` — checks generated-bank, extraction, and playback paths; optionally takes a source bundle file name.
- `ct_outtest` — checks that large console output remains readable.
- `ct_fmt` — probes image, audio, and video formats using files in ContentTool's `FormatProbes` folder.
- `ct_replace` — replaces a live texture, model, or material property at a target path.
- `ct_revert` — restores assets changed by the live replacement workbench.
- `ct_texswap` — checks live texture replacement and restoration.
- `ct_meshswap` — checks live skinned-mesh replacement and restoration.
- `ct_liveswap` — checks live model and material replacement, then restoration.
- `ct_dev` — controls the live file-watching development loop and variant sets.
- `ct_scan` — controls the scan fallback for targets without a GUID anchor.
- `ct_music` — probes live Wwise voices; its `gate` form loads a save.
- `ct_seamprobe` — controls the prefab-resolution seam probe.
- `ct_mission gate <savename>` — loads the named tactical save for a seam check. `ct_mission list` remains public.

Without the marker, developer commands are absent from the game console. An attempt to invoke one through `autorun.txt` returns:

```text
ct_autorun: '<command>' is a dev command - REFUSED, no 'ct-dev' marker beside ContentTool.dll
```

The public command's restricted subverb returns:

```text
'ct_mission gate' is a dev command - REFUSED, no 'ct-dev' marker beside ContentTool.dll (an empty file 'ct-dev' there arms it at the next launch)
```
