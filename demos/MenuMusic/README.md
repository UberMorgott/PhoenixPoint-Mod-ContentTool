# Demo mod — the main menu plays the mod's own music

**A content mod is a FOLDER of assets - no code - and ContentTool plays it.**

> **This is a SEPARATE MOD.** It installs as `Mods\MenuMusic\` and the mod manager lists it as
> **ContentTool Demo: Menu Music**. It requires the **ContentTool** mod - `meta.json` declares
> `"Dependencies": [ "com.morgott.ContentTool" ]`, so enabling this one enables ContentTool too.
> Switch it off in the mod manager and it does not load at all.

One capability, one mod. This one replaces **Phoenix Point's vanilla main-menu music** and nothing
else, so an author can open it, see the two things it consists of, and copy them.

```text
MenuMusic\
  ppcontent.json                          { "id": "morgott.demo.menumusic", ... }
  Content\Audio\Replace\208540756.mp3     MainMenuMusic  (the vanilla menu track)
  Content\Audio\Replace\423563089.mp3     MainMenuYOE    (the vanilla YOE/Complete-Edition track)
```

**A replacement is named by the shipped media ID it overwrites.** That is the whole grammar; there
is no JSON to write for a sound. `ct_extract audio <id>` writes `<id>.wav` for you to edit. Both
files here are the same 12 s loop, **generated** by `..\tools\make_demo_audio.ps1` — see
`SOURCES.md`. They carry a media ID for a name because the file NAME is the target, not because
anything mishandles the original name (see below).

## Install — and what a PLAYER actually gets

The shipped mod is this folder. `ct_sound bake MenuMusic` turns each `Content\Audio\Replace\<id>.mp3`
into a media-only `Dist\Sounds\<id>.bnk` **inside this mod**, and ContentTool loads every such bank
out of every mod folder at init (`src\Bake\SoundLoad.cs`, `SoundReplace.ShippedBanks =
"Dist\Sounds"`). No game file is opened for writing, there is nothing to revert, and switching the
mod off in the mod manager is the undo.

```text
ct_sound bake MenuMusic                  # author step: writes Dist\Sounds\<mediaId>.bnk
RESTART                                  # ContentTool loads the banks at init
```

The older **install-time** route, which wrote decoded PCM over the shipped media with a pristine
`.ct-backup` per file, is DELETED: `ct_sound apply|verify|revert` no longer exist and print a REMOVED
line. ContentTool never writes into the game installation. What is left is dev-only inspection:

```text
ct_sound status [mediaId]                # what is declared and which bank serves it
ct_sound probe <mediaId>                 # post that media and read mediaID / storage back.
                                         # THIS demo returns no duration: the menu-music event
                                         # yields dur=0ms, endOfEvent=TIMEOUT (measured 2026-08-27),
                                         # so MEMORY-vs-FILE storage is its only discriminator.
ct_sound selftest                        # offline
```

Nothing extra is needed for a **compressed** replacement on either route. This used to require
`-UnityAudio`, which flipped `m_DisableAudio` in the game's own `globalgamemanagers` so the engine's
decoders would run; the tool decodes `.ogg`/`.mp3` itself now (gate A7).

## The track, measured

**12.000 s, mono, 44100 Hz, 96 kbps mp3, 144 867 B** — an A-minor pentatonic arpeggio over a
two-note drone, sixteen sines under exponential envelopes and nothing else. It is not good music
and it is not trying to be: a demo's job is to be *obviously* not the shipped track the moment the
menu opens.

**Level was aimed, not left to chance.** The amplitudes in the generator put it at **−15.7 LUFS,
peak −4.2 dBFS** (`ffmpeg -af ebur128` / `volumedetect`), because −15 LUFS is where game music is
mixed; a louder master would be markedly louder than the vanilla menu track at the same volume
slider. Change a note and re-measure — the two commands are in the script's own comment.

**A short loop is also the cheapest thing this demo can ship.** PCM is the only codec the tool
emits, so a bank is `frames × channels × 2` bytes and the length of the source is the size of the
bank, one to one. 128 s of stereo was **24 583 864 B per edition, 49 MB in the repository**;
12 s of mono is about **1.06 MB**, twice. Nothing about the demonstration changed.

The bank also declares a **loop region** over its own frames, play count 0 = infinite, because the
media it replaces declares one and a looping Sound object carries no loop points of its own — which
is why a 12 s loop is enough for a menu you can sit on for ten minutes.

## Non-ASCII filenames work

No file shipped here needs this any more — the generated loop is named after a media ID — but the
point was proven and is worth keeping. A file carrying Cyrillic, spaces, parentheses and an
exclamation mark went end to end in game (`ct_project` on a throwaway project holding it):
`A6 PASS Аве! Император.mp3 decoded 2ch 48000Hz 147456 frames peak=0,515` (the file itself was never
committed; the run is what is kept, because it is what PROVED the point) → bank →
`LoadBankMemoryCopy: AK_Success`. The name survives `Directory.GetFiles`, `new Uri(path).AbsoluteUri`
and `UnityWebRequestMultimedia.GetAudioClip` untouched. **On the ADD path (`Content\Audio\`) you may
name your file anything.** On the REPLACE path the name IS the media ID — a rule about grammar, not
about encoding.

## Why two files

Which track the menu plays is decided by your **edition entitlement**, not by anything a mod can
see: `UIStateInitial` asks the platform whether the user owns the YOE edition (and
`CheckIsCompleteEdition`), then `EditionVisualsController.GetCorrectMusicEvent` posts either
`MainMenuMusicStart` or `MainMenuYOEStart`
(`decompiled\AssemblyCSharp\...\PhoenixPoint.Home\EditionVisualsController.cs:94-99`,
`...ViewStates\UIStateInitial.cs:74-77`). `MainMenuMusic.bnk` ships exactly those two streamed
media and exactly four events, so replacing both covers every edition.

## Vanilla, not TFTV

The music is Phoenix Point's own. TFTV ships **one file**, `TFTV.dll` — no bundle, no `.bnk`, no
`.wem`, and no `AkSoundEngine` call outside commented-out lines
(`refs\TFTV-src\TFTV\TFTVVanillaFixes.cs:2704-2719`). It cannot supply audio media, so the track
you hear at the menu comes from `StreamingAssets\Audio\GeneratedSoundBanks\Windows\`.

## Zero runtime code

The published mod is the two source files, the declaration and the two baked banks. This mod ships
**no DLL of its own** — ContentTool loads its banks and the game's own Wwise plays them.

Nothing in the install is written at all. The two banks live in this mod's own `Dist\Sounds\`, and
ContentTool loads them into memory at init; the shipped `.wem` files and `MainMenuMusic.bnk` are
never opened for writing. There is no backup and nothing to revert — unticking the mod is the undo.
