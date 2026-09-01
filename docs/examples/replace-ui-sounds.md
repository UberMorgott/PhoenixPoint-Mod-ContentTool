# ReplaceUiSounds

This demo replaces three shipped geoscape UI stings. You hear the author's short plus, confirm and
cancel sounds wherever the existing game events request those media IDs.

**Corresponds to:** [Add or replace sounds](../recipes/sounds.md).

## Features and how they work

- **Three shipped media are replaced without code.** `sounds[]` maps `sting_plus.mp3` to `18839791`,
  `sting_confirm.mp3` to `633458426`, and `sting_cancel.mp3` to `940964934`.
- **Each replacement has its own bank.** `ct_sound bake` writes one media-only `.bnk` per target ID
  under `Dist\Sounds`. ContentTool loads those banks at startup.
- **The game keeps posting its existing events.** The mod replaces media, not events. It therefore
  needs no DLL and no new trigger.
- **Disable needs a restart.** Wwise does not fall back to the shipped media during the same session
  after these replacement banks have been loaded.

## Project on disk

```text
ReplaceUiSounds\
  meta.json
  ppcontent.json                  <- three sounds[] mappings
  README.md
  SOURCES.md
  Content\Audio\Replace\          <- replacement sources live here
    sting_plus.mp3
    sting_confirm.mp3
    sting_cancel.mp3
  Dist\Sounds\                    <- committed output; a downloader does not bake
    18839791.bnk
    633458426.bnk
    940964934.bnk
```

## Rebuild and run it

```text
ct_sound status 18839791
ct_sound status 633458426
ct_sound status 940964934
ct_sound bake ReplaceUiSounds
ct_package ReplaceUiSounds
```

Restart with the mod enabled, use the affected geoscape controls, then compare the loaded media:

```text
ct_sound probe 18839791
ct_sound probe 633458426
ct_sound probe 940964934
```

## What a good run prints

```text
declared 3 replacement(s) in <project>\Content\Audio\Replace
baked <project>\Dist\Sounds\18839791.bnk: <bytes> B, bankId=<id>, media 18839791 = <ms>ms <channels>ch <rate>Hz, <loop report> from sting_plus.mp3
baked <project>\Dist\Sounds\633458426.bnk: <bytes> B, bankId=<id>, media 633458426 = <ms>ms <channels>ch <rate>Hz, <loop report> from sting_confirm.mp3
baked <project>\Dist\Sounds\940964934.bnk: <bytes> B, bankId=<id>, media 940964934 = <ms>ms <channels>ch <rate>Hz, <loop report> from sting_cancel.mp3
ct_sound bake: 3 bank(s) in <project>\Dist\Sounds - NO game file was opened for writing. ContentTool loads these at init.
```

## Verification status

**Verified in-game on 2026-08-28.** The measured media changed from 1200 to 340 ms, 3533 to
444 ms, and 2231 to 601 ms. All three reported `streaming=false(MEMORY)`.
