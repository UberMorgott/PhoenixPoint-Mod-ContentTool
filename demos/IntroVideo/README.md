# Demo mod — a whole cutscene: picture, sound and subtitles

**A content mod is a FOLDER of assets - no code - and ContentTool plays it.** This one is the
exception that proves where the line is: two of its three parts are pure content, and the third
costs a DLL.

> **This is a SEPARATE MOD.** It installs as `Mods\IntroVideo\` and the mod manager lists it as
> **ContentTool Demo: Replace Cutscene**. It requires the **ContentTool** mod - `meta.json` declares
> `"Dependencies": [ "com.morgott.ContentTool" ]`, so enabling this one enables ContentTool too.
> Switch it off in the mod manager and it does not load at all.

**Nothing in the install is modified.** The clip, the sound and the subtitle file all stay in this
folder; ContentTool serves them to the running game from here. No game file is edited, so there is
nothing to back up and nothing to revert.

```text
IntroVideo\
  ppcontent.json                          "replace" -> the picture, "sounds" -> the audio
  Content\Videos\campaign_intro.webm      the clip
  Content\Audio\Replace\intro_theme.mp3   the audio track - a SEPARATE Wwise media, see below
  Content\Subtitles\campaign_intro.srt    the subtitles
  src\IntroVideoMain.cs                   ~40 lines, and ONLY for the subtitles
  tools\make_audio.ps1                    how the shipped audio was generated (ffmpeg, three notes)
  tools\check_srt.ps1                     the .srt run through a port of the GAME's own parser
```

## A cutscene is three assets, not one

This is the finding that this demo exists for. `Base.UI.VideoPlayback.VideoPlaybackSourceDef` holds
**three independent things**, and replacing one does nothing to the other two:

| field | what it points at | how a mod replaces it | code? |
|---|---|---|---|
| `VideoClipSource` | a loose `.webm`, played through `VideoPlayer.url` (`VideoPlaybackController.cs:150`) | `ppcontent.json` `"replace"` | **no** |
| `AudioSource` | a `VideoSoundDef` — a Wwise **event**, posted by `VideoSound.Play` (`VideoSound.cs:50`) | `ppcontent.json` `"sounds"` | **no** |
| `Subtitles` | a `TextAsset` **field on the def**, handed to `SubtitlePlayer.SubtitleFile` (`VideoPlaybackController.cs:163`) | write the field | **yes** |

### The video's own audio track is never heard, and that is by design

`PP_Intro.webm` *does* carry a Vorbis stream (`ffprobe`: `0,vp8,video` / `1,vorbis,audio`) and so
does the placeholder in this folder — and neither one is what you hear. `Assembly-CSharp` contains
**zero** references to `VideoPlayer.audioOutputMode`, `EnableAudioTrack`, `controlledAudioTrackCount`
or `SetTargetAudioSource` (measured by grep over the whole decompiled tree), so no audio track is
ever routed anywhere and no `AudioSource` is ever attached to the player. Every sound you hear over
a cutscene comes out of Wwise, from `VideoSound.Play` posting `VideoSoundDef.VideoAudioEvent`.

**So a replaced `.webm` plays your picture under the game's original voice-over — and that is not a
bug, it is the architecture.** To replace the sound you replace the Wwise media, separately.

### Which Wwise media, and how we know

| | |
|---|---|
| bank | `Cinematics.bnk` |
| event | `PP_Intro` = **1015492702** |
| media | `PhoenixProject_Intro` = **908611677**, **streamed** (a loose `908611677.wem`) |

Both rows come out of the bank's own shipped manifest,
`StreamingAssets\Audio\GeneratedSoundBanks\Windows\Cinematics.txt` — not from a guess. The manifest
does not print which media an event plays, so the pairing was measured instead: the RIFF header of
`908611677.wem` declares 44100 Hz and **5351789 samples = 121.35 s**, and `PP_Intro.webm` is
**121.73 s**. No other intro media is within a minute of it.

`908611677` is **streamed**, which is what makes the sound half free: the media-only bank route
(`ct_sound bake`) replaces streamed media with no code and without writing to the install — the same
route `ReplaceUiSounds` uses for its three geoscape clicks.

### Subtitles are the one part that is not a file

`.srt` is right: the game parses SRT (`SubtitltesTool.SRTParser`), and the option that hides them is
`SubtitlesEnabled` (`VideoPlaybackController.cs:96`). But the def holds the subtitle **TextAsset**,
not a path, so there is no file on disk to overwrite and no catalog row to repoint. The only way in
is to write `def.Subtitles`, and writing a field is behaviour.

`src\IntroVideoMain.cs` does exactly that and nothing else, through the game's **own** mod hook -
`ModMain.ApplyDefRepoPatches` (`ModMain.cs:66`), reached from `ModManager.ApplyDefPatches:673`,
which `GeoLevelController` calls at line **523**, before it plays the intro at line **741**. No
Harmony patch, no ordering to guess. Unity 2019.4 has a public `TextAsset(string)` constructor
(verified against the shipped `UnityEngine.CoreModule.dll`), so the text goes straight from the
`.srt` into a `TextAsset` with no bundle in between.

### Your `.srt` must use CRLF line endings

Not a style preference — a measured requirement, and `tools\check_srt.ps1` is the check.
`ParserUtils.ParseTimeValue:132` unconditionally steps one character past the last digit of a
timestamp. With CRLF that step lands on the `\r` and everything works. With LF it lands **on the
newline**, so `ParseLines:14` then skips forward to the *next* one and **eats the first line of
every cue**. The parser is also unbounded (`ParseLine:47` and `SkipNewLineSymbols:226` index
`text[pointer]` with no length test), so a file that ends without a final newline throws
`IndexOutOfRangeException` inside `WarmUpPlayer` and takes the cutscene with it.

`tools\check_srt.ps1` is a faithful port of those three functions, missing bounds checks included,
so a bad file fails offline instead of in the game:

```text
pwsh -File tools\check_srt.ps1
PASS 3 cue(s), the game's own parser walked the whole file without throwing
```

`.gitattributes` pins `*.srt -text` so git cannot normalise the endings away.

## Which cutscene, and how we know

`GeoLevelController.cs:741` plays `View.IntroCinematicDef` when `instanceData == null` — that *is*
"a new campaign". The def was read **off the live field**, not guessed from a filename:

```text
GeoscapeView.IntroCinematicDef = PP_Intro_Cutscene
  key e574fca8ff2123b48850c43faa7e08c1
  row StreamableCopiedAssets/Videos/Factions/Phoenix/PP_Intro.webm
```

That is the row `"asset"` names, and `PP_Intro_Cutscene` is the def name `IntroVideoMain` looks up.
`ct_video defs <savegame>` prints it again on any install.

## Install

**A player installs nothing and bakes nothing.** The picture is served by ContentTool at its own init
(`src\Bake\VideoCatalog.cs`, `LiveAll`), and the sound ships as an already-built
`Dist\Sounds\908611677.bnk` that ContentTool loads at the same moment. Measured 2026-08-28 with
`ct_sound bake` never run in that install: the `PP_Intro` event answered `dur=6034ms
mediaID=908611677 streaming=false(MEMORY)`, against the shipped 121355 ms file, with an untouched
media in the same run still reading `streaming=true(FILE)`.

The bake below is the AUTHOR's step — a Wwise media has to become a bank before it can ship:

```text
ct_sound bake IntroVideo        # writes Dist\Sounds\908611677.bnk - NO game file is opened for writing
```

Then restart. ContentTool loads every dependent mod's `Dist\Sounds\*.bnk` into memory at init
(`src\Bake\SoundLoad.cs`) - the file's PRESENCE is the declaration. Re-run the bake whenever you
change `intro_theme.mp3`.

By hand, if you want the video log line now without waiting for a scene load:

```text
ct_video live IntroVideo        # ContentTool serves the clip from THIS folder, in memory
```

No `apply`, no `revert`: nothing was written into the install, so there is nothing to undo. Remove
the mod and the game is already back to stock.

## Your own clip

Drop it in as `campaign_intro.webm` and re-apply. **Codec matters, and it is measured:**

| Container / codec | Result |
|---|---|
| WebM **VP8 + Vorbis** (what all 69 shipped clips are) | plays |
| `.mp4`, `.mov`, `.avi` | plays |
| `.mkv` | REJECTED |
| WebM **VP9** | REJECTED |
| WebM **AV1** | REJECTED — `Error: Unsupported video codec 'AV1'`, then `VideoPlayer cannot play url`, and `isPrepared` never turns true |

AV1 is the trap: a file downloaded from the web is very often AV1 + Opus, the container still says
`.webm`, and it silently never plays. Convert first — same clip, 1920x1080, converted and measured
at 450 frames:

```text
ffmpeg -i yours.webm -c:v libvpx -b:v 1500k -c:a libvorbis -b:a 128k campaign_intro.webm
```

Whatever audio track that leaves in the file is ignored — put the sound in
`Content\Audio\Replace\` instead. `.wav`, `.ogg` and `.mp3` are all decoded by ContentTool itself.
Length is free: the shipped media is 121 s and this mod's is 6 s.

Non-ASCII file names are fine: a clip named `…Электрослабость feat. Татьяна Буланова — Ехидна.webm`
(Cyrillic, spaces, em-dash) decoded identically to a byte-identical ASCII-named twin, 90 frames each.

## This is the REPLACE demo — the ADD side is a different mod

Every demo here shows exactly one side of the line, so a modder can tell them apart in the mod
manager without opening either. This one **replaces**, and it is allowed to replace three things at
once because they are three parts of one cutscene. The ADD demos are separate mods:
**ContentTool Demo: Add UI Sounds** (`demos\AddUiSounds\`) and **QuitCutscene**.

There is no add half *here* because adding a brand-new video row is free and proven — but nothing
would ever PLAY it. Every one of the game's 67 `VideoPlaybackSourceDef`s already has a catalog row
(`ct_video defs`: *67 defs, 0 with no catalog row*), so there is no empty slot to fill, and making
the game play a NEW clip means a def, which means code. That demo is **QuitCutscene**, and its DLL
is exactly the price of the trigger.

**Replacing the picture is free. Replacing the sound is free. The subtitles, and playing something
new at all, cost a hook.**
