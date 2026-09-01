# MenuMusic

This demo replaces the main-menu music in both the standard and Year One Edition paths with the
same short loop. You hear it after starting the game with the mod enabled.

**Corresponds to:** [Add or replace sounds](../recipes/sounds.md).

## Features and how they work

- **Both menu variants are covered.** The sources target Wwise media IDs `208540756` and
  `423563089`, both used by `MainMenuMusic.bnk`.
- **The numeric filenames are the declarations.** There is no `sounds[]` block. ContentTool treats a
  direct file under `Content\Audio\Replace` whose stem is an unsigned number as that media ID.
- **The target's loop layout is preserved.** `ct_sound bake` reads the shipped media's loop
  declaration, applies it to the replacement PCM, and writes one media-only bank per ID.
- **There is no DLL.** Existing menu events request the same media IDs. ContentTool only changes
  which bytes Wwise receives.

## Project on disk

```text
MenuMusic\
  meta.json
  ppcontent.json                  <- ID and bundle name; no sounds[] block
  README.md
  SOURCES.md
  Content\Audio\Replace\
    208540756.mp3                 <- filename targets standard menu media
    423563089.mp3                 <- filename targets YOE menu media
  Dist\Sounds\
    208540756.bnk                 <- committed authoring output
    423563089.bnk
```

## Rebuild and run it

```text
ct_sound status 208540756
ct_sound status 423563089
ct_sound bake MenuMusic
ct_package MenuMusic
```

Restart with the mod enabled and remain on the main menu. Storage can be checked with:

```text
ct_sound probe 208540756
ct_sound probe 423563089
```

## What a good run prints

```text
declared 2 replacement(s) in <project>\Content\Audio\Replace
baked <project>\Dist\Sounds\208540756.bnk: <bytes> B, bankId=<id>, media 208540756 = <ms>ms <channels>ch <rate>Hz, <loop report> from 208540756.mp3
baked <project>\Dist\Sounds\423563089.bnk: <bytes> B, bankId=<id>, media 423563089 = <ms>ms <channels>ch <rate>Hz, <loop report> from 423563089.mp3
ct_sound bake: 2 bank(s) in <project>\Dist\Sounds - NO game file was opened for writing. ContentTool loads these at init.
```

## Verification status

**Verified in-game on 2026-08-28 for replacement and storage.** Both IDs reported
`streaming=false(MEMORY)` from the mod banks. The music event returns no duration callback, so its
duration was not verified; `dur=0` followed by timeout is not evidence that the clip is empty.
