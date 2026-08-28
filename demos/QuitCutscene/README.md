# Demo mod — a video plays when you quit from the MAIN MENU, then the game exits

ESC skips it and exits immediately. Quitting from the in-game pause screen exits normally with no
clip: `GeoscapeView.ToCutsceneState` takes a priority and not a completion callback
(`GeoscapeView.cs:672`), so there is no shipped "play then continue" outside the home screen.

> **This is a SEPARATE MOD.** It installs as `Mods\QuitCutscene\` and the mod manager lists it as
> **ContentTool Demo: Quit Cutscene**. It requires the **ContentTool** mod - `meta.json` declares
> `"Dependencies": [ "com.morgott.ContentTool" ]`, so enabling this one enables ContentTool too.
> Switch it off in the mod manager and it does not load at all: the patch is never applied and
> quitting is the game's own again.

**This mod exists to show a line.** Its content half needs no logic of its own — ContentTool serves
it. Its trigger half ships a DLL. The point is exactly *which half* needed one.

```text
QuitCutscene\
  ppcontent.json                  { "id": "morgott.demo.quitcutscene", "replace": [ { "video": "quit_outro" } ] }
  Content\Videos\quit_outro.webm  the clip - drop YOUR file here under this name and re-apply
  meta.json                       the DLL half's mod manifest
  src\QuitCutsceneMain.cs         the trigger - 319 lines, of which the Harmony patch is ~40
..\tools\make_placeholders.ps1    how the shipped placeholder was made (ffmpeg, one card)
```

**Why 319 and not 40.** The recipe really is one prefix on one shared call — `QuitPatch` at the
bottom of the file is the whole of it. The other ~280 lines are two things a copyist should know
are optional: the `Q1-*` instrumentation this demo carries only while the clip is still
[unconfirmed in-game](#what-else-changed-and-why), and the watchdog, which is not optional at all
(a mod that takes the quit away and then fails to quit is worse than no mod).

**None of it belongs in ContentTool.** This demo reaches ContentTool by reflection on purpose (see
[Install](#install)), so every helper here that *looks* like engine material (`KeyFor`, the `Serve`
bridge, the reflective `VideoPlayer` readers) would, in the engine, be reachable only through another
reflection call. Moving a six-line MD5 behind a `MethodInfo.Invoke` makes the recipe longer to read,
not shorter. `BuildDef` is out for a second reason: defs are
deliberately the author's side of the line (`src\Bake\VideoCatalog.cs:50` — *"defs are the mod
author's job, the clip and the row are ours"*).

The checked-in `quit_outro.webm` is a generated title card, 90 frames, 185 KB — ours, so nothing of
anyone else's is redistributed. It is there to be replaced.

## The split

| Half | What it is | Costs what |
|---|---|---|
| **Content** — the clip and its catalog key | one call to `CatalogLive.Register(key, file)`: ContentTool serves the clip **from this folder, in memory**. No game file is written, so there is nothing to back up or revert | **ContentTool must be loaded.** No logic of our own |
| **Trigger** — playing it on quit | one Harmony prefix on `PhoenixGame.FinishLevelAndQuitGame` | **A DLL of our own** |

Adding a video to Phoenix Point is content. Deciding *when* it plays is behaviour, and Phoenix Point
ships no path from quitting to a cutscene — all 13 `ToCutsceneState` call sites are intros,
research-complete, faction rewards, the marketplace and two console commands, while both quit routes
(`UIModuleMainMenuButtons.OnExitButtonClicked:281-284` and `UIModulePauseScreen.OnQuitGamePressed:172`
→ `OnQuitConfirmed` → `QuitGameCrt`) go straight to `FinishLevelAndQuitGame`. There is nothing to
redirect, so a new trigger costs a hook. **If you only want to change which video plays somewhere the
game already plays one, you do not need any of this** — see the other demos.

## What the trigger does NOT do

Almost everything is the game's own machinery:

- `HomeScreenView.ToCutsceneState(def, callback)` (`HomeScreenView.cs:182`) already means "play this,
  then run that". The patch supplies the callback; it does not play anything itself.
- **ESC needed no input handling.** `UIStateHomeScreenCutscene.OnInputEvent:92-104` already routes
  `Cancel`/`Submit` to `OnCancel()` when `IsInterruptible`, and `OnCancel` invokes that same callback.
  Skip and finish are one path. All it took was `SkipOnPlayerInput = true` on the def, which is where
  `IsInterruptible` comes from (`VideoPlaybackController.WarmUpPlayer:174`).
- One patch on the **shared** call, not one per button. Both quit entry points funnel through
  `FinishLevelAndQuitGame`, so there is no pair of guards to keep in sync.

## Install

Drop the folder in `Mods\QuitCutscene\` (`QuitCutscene.dll` + `meta.json` + `Content\`) and enable it
alongside ContentTool. That is the whole install: the mod registers its clip itself on enable, so
there is no command to run, no restart, and nothing to uninstall.

It calls ContentTool **by reflection** rather than with an assembly reference. A reference would work
— the weapon and creature demos use one, because `meta.json` declares the dependency and the mod
manager loads a dependency before its dependents — but `Dependencies` carries no minimum version, so
an *older* ContentTool satisfies the declaration while lacking `CatalogLive.Register`. Reflection
turns that into the logged `VOID ContentTool has no CatalogLive.Register - version mismatch` instead
of a `MissingMethodException`.

The reference that really is forbidden is a Unity module under `PhoenixPointWin64_Data\Managed\` that
`ModSDK\` does not ship: PPModLoader installs no `AssemblyResolve`, so the mod fails to LOAD, and
Phoenix Point answers a failed mod load by rewriting `MOD_ACTIVATED` empty, silently disabling every
other mod on the machine (measured 2026-08-13 with `UnityEngine.VideoModule`, commit `632fba7` — it
took ContentTool down with it). That is why `UnityEngine.Video` is read reflectively here too.

## Your own clip

Drop it in `Content\Videos\` as `quit_outro.webm` and restart the game. Nothing
else changes — the `RuntimeKey` is derived from the mod id and the file's stem
(`MD5("morgott.demo.quitcutscene/quit_outro")`), computed identically by the tool and by the DLL, so
renaming the file is the only thing that would need both updated.

**Codec matters.** `.webm` / `.mp4` / `.mov` are accepted; `.mkv` and VP9 are rejected by the engine
(F1, `722370b`). Every shipped Phoenix Point clip is VP8 video + Vorbis audio in WebM, which is what
this demo ships. A file downloaded from the web is very often **AV1 + Opus** — the container says
`.webm` and it still will not play. Convert it first:

```text
ffmpeg -i yours.webm -c:v libvpx -b:v 1500k -c:a libvorbis -b:a 128k quit_outro.webm
```

## The bug — a def that was not a def

The first version of the trigger built its `VideoPlaybackSourceDef` with
`ScriptableObject.CreateInstance<VideoPlaybackSourceDef>()`. That produces an object of the right
*type* and nothing else: `BaseDef.Guid` and `BaseDef.ResourcePath` are both left **null**, and the
def is not in `DefRepository`. Playback itself does not read either field, so the mistake is
invisible — until something else does.

Something else does. TFTV — installed in most players' games, and in the one this demo was tested
in — postfixes the very state this mod enters:

```csharp
// TFTV\TFTVUI\Common\Various.cs:108-127, patching UIStateHomeScreenCutscene.EnterState
if (config.SkipMovies)
{
    if (_sourcePlaybackDef == null) return;
    if (_sourcePlaybackDef.ResourcePath.Contains("Game_Intro_Cutscene"))   // <- null here
        OnCancel();                                                        //    on our def
}
```

TFTV guards against a null *def* and not against a null *ResourcePath*, so our def threw a
`NullReferenceException` inside its postfix on every single entry. TFTV catches and logs it, so
nothing crashed and nothing said "the mod is broken" — the read on our def just silently failed.

Two lessons, and the second is the one worth carrying away:

- **A runtime def has a factory.** `DefRepository.CreateRuntimeDef` (`DefRepository.cs:214`) is what
  the engine uses for exactly this — `BaseDef.cs:128` calls it while deserializing a def that has no
  asset behind it. It stamps a `Guid` and registers the def. Use it; `CreateInstance` gives you a
  half-built def that other people's code will read.
- **`ResourcePath` must be set, and must not lie.** Ours is `Morgott/QuitCutscene/quit_outro`. It
  deliberately does **not** contain `Game_Intro_Cutscene` — with TFTV's *skip movies* option on, a
  def whose `ResourcePath` says it is the intro gets cancelled the instant it starts. Which is also
  the answer to "why does the game's own intro flash by and vanish on this machine": it is that
  postfix, working exactly as intended, and it is **not** this mod misbehaving. If you are reading a
  log and see `UIStateHomeScreenCutscene` entered and then `UIStateInitial` one frame later, that is
  the boot intro being skipped, not your cutscene.

## What else changed, and why

Chasing this cost a session mostly because the mod could not say where it had failed. Two changes fix
that, and one of them matters more than the cutscene does:

- **The clip is resolved before the quit is hijacked.** `Q1-src` calls the game's own
  `StreamableAssetsManager.GetStreamingPath` — the same call `WarmUpPlayer:150` makes — and prints
  the path plus whether the file is there. A key missing from the live catalog surfaces as the
  `NullReferenceException` it really is (`StreamableAssetsManager.cs:49` dereferences a location the
  catalog did not have) instead of as a black screen ten lines later. If it cannot resolve, the quit
  is **left alone**.
- **The quit always happens.** A mod that takes the quit away and then does not quit is worse than a
  mod that does nothing. A watchdog quits regardless, and it is armed unconditionally: the deadline
  is the clip's own `VideoPlayer.length` plus 10s of grace, or a flat 120s cap when the player will
  not say how long the clip is. (8 seconds is only how long the probe waits for the clip to report
  `isPrepared` — it is not the deadline.) The normal ending, the cutscene's callback firing first,
  is unaffected, because the real quit is idempotent.
- `Q1-play` now reads `CommonModules.CutscenesPlayer.VideoPlayer` — *the* controller
  `UIStateHomeScreenCutscene.cs:48` plays through — instead of `FindObjectOfType`, which could answer
  about a different `VideoPlaybackController` two objects away.

**Still unconfirmed in-game.** The def defect is proven from source, and the demo can no longer hang
a quit, but nobody has yet watched the clip play. The run that settles it: quit from the **main
menu** and read the log for `Q1-src PASS` → `Q1-trigger` → `Q1-play PASS ... frameCount=90`. A
`Q1-play FAIL` line now carries the url, `isPrepared` and `frameCount`, which is enough to tell a
missing catalog row from a rejected codec without another session of guessing. The ESC keypress is
still UNRUN — the code path is asserted (`OnInputEvent:92-104 -> OnCancel ->` same callback), the
keypress is a five-second manual check.

Note the separate instrument bug found while chasing this: `ct_video play`'s arm never prints and
does not hold `AsyncGate`, so it cannot currently answer "what is the player actually on". Fix the
instrument before trusting a negative from it.

## Known ceiling

Quitting from an **in-game** escape menu exits with no clip. `GeoscapeView.ToCutsceneState` takes a
priority, not a callback (`GeoscapeView.cs:672`), so the geoscape has no shipped "play then continue"
to borrow. The patch says so in the log and quits normally rather than pretending. Quitting from the
main menu is the demonstrated path.
