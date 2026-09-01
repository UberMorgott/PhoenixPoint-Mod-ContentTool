# QuitCutscene

Quit from the main menu and this demo plays a three-second outro before the process exits. Quitting
from an in-game pause menu follows the normal game path and shows no clip.

**Corresponds to:** [Add or replace a video](../recipes/videos.md) and
[Build a behaviour DLL](../recipes/behavior-dll.md).

## Features and how they work

- **The clip is added, not replaced.** The manifest row has `"video": "quit_outro"` and no
  `asset`. ContentTool derives the new RuntimeKey
  `6f3d8e3d761527b9f2ecac2a4dac2c17` as lowercase MD5 of
  `morgott.demo.quitcutscene/quit_outro` and registers the loose video in the live catalog.
- **A runtime def points at that key.** The DLL creates `QuitCutscene_Runtime`, a
  `VideoPlaybackSourceDef`, and sets `SkipOnPlayerInput=true`.
- **A Harmony prefix supplies the trigger.** It intercepts `PhoenixGame.FinishLevelAndQuitGame` only
  when a `HomeScreenView` exists, then calls the game's own `HomeScreenView.ToCutsceneState`.
- **The normal completion callback performs the real quit.** After the clip finishes—or the game's
  cutscene state reports a skip—the callback invokes the original quit exactly once.
- **A watchdog closes the failure path.** It uses the decoded clip length plus ten seconds, capped at
  120 seconds when no length is available. It is armed even after a successful prepare, so a stalled
  player cannot trap the user behind the cutscene.
- **Pause-menu quit is deliberately untouched.** There is no `HomeScreenView` and the prefix returns
  to the shipped quit immediately.

## Project on disk

```text
QuitCutscene\
  meta.json                         <- AssemblyName is QuitCutscene.dll
  ppcontent.json                    <- Add video row: no asset field
  QuitCutscene.csproj
  Content\Videos\
    quit_outro.webm                 <- direct child; served as a loose file
  bin\Release\QuitCutscene\
    QuitCutscene.dll
    QuitCutscene.pdb
    meta.json
  src\QuitCutsceneMain.cs           <- catalog registration, def, trigger and watchdog
  README.md
```

There is no `Dist` bundle because a loose added video does not need one.

## Rebuild and run it

```text
dotnet build demos\QuitCutscene\QuitCutscene.csproj -c Release -p:PPRoot="D:\Steam\steamapps\common\Phoenix Point"
ct_project QuitCutscene
ct_video live QuitCutscene
ct_video open 6f3d8e3d761527b9f2ecac2a4dac2c17
ct_package QuitCutscene
ct_video quit
```

Run `ct_video quit` from the main menu. It calls the same game method as the quit button.

## What a good run prints

```text
video ADD 'quit_outro' (its RuntimeKey is printed by the command) - serve it with: ct_video live QuitCutscene
ct_project: ALL PASS - nothing needed patching: none of this project's 1 replacement(s) names a shipped bundle, so no copy was written - the video row(s) above are served live by ct_video
ct_video quit: called PhoenixGame.FinishLevelAndQuitGame (the same call both quit buttons make)
```

Before exit, `Player.log` includes:

```text
Q1-src PASS key=6f3d8e3d761527b9f2ecac2a4dac2c17 resolves to '<path>\Content\Videos\quit_outro.webm' exists=True
Q1-trigger the quit was intercepted; handing the clip to the game's own HomeScreenView.ToCutsceneState, which quits for real when it ends or when ESC skips it
Q1-play PASS key=6f3d8e3d761527b9f2ecac2a4dac2c17 url='<path>' prepared=True playing=True frameCount=90 length=3s 1280x720 stopped=<value> interruptible=<value> playbackSource=QuitCutscene_Runtime
Q1-watchdog armed: this quit happens in 13.0s at the latest whatever the clip does next (clip length=3s, grace=10s)
Q1-exit the cutscene finished or was skipped; quitting for real now
```

## Verification status

**The normal main-menu exit is verified.** On 2026-09-01 the three-second clip played and the
process closed in 3.0 seconds, before the 13.0-second watchdog deadline. `TODO(verify)`: the explicit
ESC-keypress skip path has not been run. Do not treat the normal exit path as pending.
