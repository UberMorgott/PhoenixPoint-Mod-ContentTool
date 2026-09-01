# IntroVideo

Start a new campaign with this demo enabled. The Phoenix campaign intro uses the demo's short video,
theme and subtitle file instead of the shipped three parts.

**Corresponds to:** [Add or replace a video](../recipes/videos.md),
[Add or replace sounds](../recipes/sounds.md), and
[Build a behaviour DLL](../recipes/behavior-dll.md).

## Features and how they work

- **The picture uses the video Replace route.** `campaign_intro.webm` replaces the catalog path
  `StreamableCopiedAssets/Videos/Factions/Phoenix/PP_Intro.webm`. `ct_video live` repoints that row
  in memory; no Unity bundle is patched.
- **The theme uses Wwise media replacement.** Media `908611677` is replaced by
  `Content\Audio\Replace\intro_theme.mp3`; the existing `PP_Intro` event, ID `1015492702`, still
  triggers it. The author-built bank is `Dist\Sounds\908611677.bnk`.
- **The subtitles use a def field, not a file route.** `IntroVideo.dll` finds
  `PP_Intro_Cutscene`, reads `Content\Subtitles\campaign_intro.srt` into a `TextAsset`, and assigns
  `VideoPlaybackSourceDef.Subtitles` during `ApplyDefRepoPatches`.
- **The shipped subtitle is retained for disable.** The DLL captures the original field once and
  restores it in `OnModDisabled`.
- **The three mechanisms are independent.** A missing subtitle logs a DLL failure while picture and
  sound remain content routes. This is why a cutscene is not one replaceable asset.

## Project on disk

```text
IntroVideo\
  meta.json                         <- AssemblyName is IntroVideo.dll
  ppcontent.json                    <- one video Replace row + one sounds[] row
  IntroVideo.csproj
  Content\
    Videos\
      campaign_intro.webm           <- picture source
    Audio\Replace\
      intro_theme.mp3               <- media 908611677
    Subtitles\
      campaign_intro.srt            <- DLL reads this exact path
  Dist\Sounds\
    908611677.bnk                   <- committed replacement bank
  bin\Release\IntroVideo\
    IntroVideo.dll
  src\IntroVideoMain.cs             <- writes PP_Intro_Cutscene.Subtitles
  tools\check_srt.ps1
  tools\make_audio.ps1
  README.md
  SOURCES.md
```

## Rebuild and run it

Replace `PPRoot` with your game folder. Run the PowerShell line from the repository root; run the
`ct_` lines in the game console.

```text
pwsh -File demos\IntroVideo\tools\check_srt.ps1
dotnet build demos\IntroVideo\IntroVideo.csproj -c Release -p:PPRoot="D:\Steam\steamapps\common\Phoenix Point"
ct_list videos PP_Intro
ct_project IntroVideo
ct_sound bake IntroVideo
ct_video live IntroVideo
ct_sound probe event 1015492702
ct_package IntroVideo
```

Restart with the demo enabled and start a new campaign. A downloaded copy already contains its DLL
and bank; the bake commands are for authors changing the sources.

## What a good run prints

```text
PASS 3 cue(s), the game's own parser walked the whole file without throwing
video 'StreamableCopiedAssets/Videos/Factions/Phoenix/PP_Intro.webm' <- campaign_intro - serve it with: ct_video live IntroVideo
ct_project: ALL PASS - nothing needed patching: none of this project's 1 replacement(s) names a shipped bundle, so no copy was written - the video row(s) above are served live by ct_video
declared 1 replacement(s) in <project>\Content\Audio\Replace
baked <project>\Dist\Sounds\908611677.bnk: <bytes> B, bankId=<id>, media 908611677 = <ms>ms <channels>ch <rate>Hz, <loop report> from intro_theme.mp3
ct_sound bake: 1 bank(s) in <project>\Dist\Sounds - NO game file was opened for writing. ContentTool loads these at init.
```

`Player.log` also contains:

```text
IntroVideo: subtitles ON PP_Intro_Cutscene - <chars> chars from Content\Subtitles\campaign_intro.srt; the shipped def carried <original subtitle report>
```

## Verification status

**Picture and sound are verified in-game.** The picture measured 180 frames at 1280×720, against the
shipped 1934 frames at 1920×1080. The theme measured 6034 ms and
`streaming=false(MEMORY)`, against the shipped 121355 ms file. `TODO(verify)`: the ledger has no
independent observation that the demo subtitle text appeared on screen; the DLL log proves the field
write, not the rendered words.
