# Demo mod — REPLACE a shipped sound, with no code at all

**A content mod is a FOLDER of assets - no code - and ContentTool plays it.** This mod is the
smallest possible proof of that: seven lines of JSON, three `.mp3`, and **no DLL**.

> **This is a SEPARATE MOD.** It installs as `Mods\ReplaceUiSounds\` and the mod manager lists it as
> **ContentTool Demo: Replace UI Sounds**. It requires the **ContentTool** mod - `meta.json` declares
> `"Dependencies": [ "com.morgott.ContentTool" ]`, so enabling this one enables ContentTool too.
> Switch it off in the mod manager and it does not load at all.

Its twin is **ContentTool Demo: Add UI Sounds** (`demos\AddUiSounds\`), which does the other half -
sounds the game never had, on a hotkey - and ships a DLL for it. **One mod per side, on purpose.**

| | REPLACE (this mod) | ADD (the twin) |
|---|---|---|
| what | three shipped geoscape UI sounds become the author's | two sounds the game never had, on **Alt+B** |
| needs a DLL? | **no** | **yes** — a hotkey is behaviour, and behaviour is code |
| why | the media ID already exists and the game already posts an event for it | nothing in the game would ever post a new event, so somebody has to |

```text
ReplaceUiSounds\
  ppcontent.json                    "sounds": three replacements, named by MEDIA + FILE
  Content\Audio\Replace\*.mp3       the three replacements (their own names, not renamed)
  Dist\Sounds\<mediaId>.bnk         written by the bake; ContentTool loads these at startup
```

**Enable both mods together and nothing overlaps.** This one owns three shipped media IDs; the
twin adds `morgott_demo_adduisounds_*` events that no shipped media owns and ships no
`Dist\Sounds\` at all. The rule behind that: two mods that both ship a bank for the SAME media ID
both load successfully and the winner is whichever loaded last (`SoundLoad.cs` - a later bank wins
without unloading the first), so overlap is silent, not an error. One media ID, one mod.

## The whole mod

```json
"sounds": [
  { "media": 18839791,  "file": "sting_plus.mp3" },
  { "media": 633458426, "file": "sting_confirm.mp3" },
  { "media": 940964934, "file": "sting_cancel.mp3" }
]
```

| media | shipped sound | event | gets | length | shipped length |
|---|---|---|---|---|---|
| 18839791 | `GUI_StatsPlusClick` | 784388130 | `sting_plus.mp3` — one bright ping | **0.340 s** | 1.200 s |
| 633458426 | `GUI_SkillConfirmClick` | 1437631612 | `sting_confirm.mp3` — two notes, rising | **0.444 s** | 3.533 s |
| 940964934 | `GUI_CancelMission` | 346433775 | `sting_cancel.mp3` — a reedy buzz, falling | **0.601 s** | 2.231 s |

**Short blips only, and all three are GENERATED** by
`..\tools\make_demo_audio.ps1` — sines under exponential envelopes, ours by construction, so nothing
of anyone else's is redistributed here (`SOURCES.md`). Both length columns are read off the real
files by `tests\ObjCodecTests` (`DemoBankTests.cs`): the replacement out of the shipped
`Dist\Sounds\<mediaId>.bnk`, the shipped one out of the game's own `<mediaId>.wem`, so a re-bake that
changes a length fails the test instead of leaving this table wrong. The lengths are deliberately unlike the media
they replace: that difference is what `ct_sound probe <mediaId>` reads back as proof the engine is
serving ours.

`"bundle"` is declared and never built: `ppcontent.json` requires the field for every project
(`ContentProject.cs:249`) but `ct_sound bake` reads only `"id"` and `"sounds"`
(`SoundReplace.cs:109,241`), so a sounds-only mod never has a bundle.

**Why these three and not the menu click.** The busiest UI sounds are `MenuEnter` (measured: **14
posts in 20 s** at the main menu, `ct_voices watch`) and `MenuClick` — and both are **In Memory
Audio** inside `UI.bnk`. A media-only bank *does* replace embedded media — proven as test F/G, whose
own example is `272177053` = `GUI_MenuClick` — but reaching it needs `LoadBankMemoryCopy` from a
DLL, and this mod's whole point is having none. So the targets are the busiest sounds that live in
`UIGeoscape.bnk` and are already loaded for us. `GUI_StatsPlusClick` is additionally the one media
already confirmed by ear (PROVEN-FOUNDATIONS, `S1-ear`).

## Install

```text
ct_sound bake ReplaceUiSounds    # .mp3 -> the TOOL decodes; writes Dist\Sounds\<mediaId>.bnk
```

Then **restart**. ContentTool loads every enabled content mod's `Dist\Sounds\*.bnk` into memory at
its own init (`src\Bake\SoundLoad.cs`) — the file's PRESENCE is the declaration, exactly as
`Content\Textures\*.png` is. **No game file is opened for writing**, so there is no backup, no
`apply` and no `revert`: a restart without the mod is already the clean undo.

Re-run the bake whenever you change one of the `.mp3`.

## Your own sounds

Drop a `.wav`, `.ogg` or `.mp3` into `Content\Audio\Replace\`, add its `{ "media", "file" }` row, and
re-bake. All three formats are decoded by ContentTool itself — no flag, no external converter. The
media ID must be one Phoenix Point actually owns; the bake refuses anything else rather than writing
a bank nothing would ever play (`SoundReplace.cs:296`). Length is free.
