# FIRST TASK: move the sound route off the player's install

Written 2026-08-13, cold-readable. Everything below is measured; nothing is planned-but-unproven
unless it says so.

## The ruling that makes this the first task

**ContentTool is the ENGINE mod. Content mods DEPEND on it, ship their own assets, optionally their
own DLL. Original game files are NEVER modified.** The earlier "SHIPPING = ZERO RUNTIME CODE" framing
was a misreading of the author's request, not his requirement — a patcher changes the player's
install and cannot ship as a Workshop item.

So the current sound route is the WRONG SHAPE. `ct_sound apply` overwrites
`StreamingAssets\Audio\GeneratedSoundBanks\Windows\<mediaId>.wem` and patches `ulPluginID` inside the
shipped `.bnk`. Both are game-file edits. They stay as a DEV/authoring option; they must stop being
what a demo ships.

Nothing is currently applied: every demo edit was reverted hash-verified on 2026-08-13 (media
82c123d0…, d5d2006c…, 6581eb15…, 2ed26357…, 78f4ed3c…; banks 3e759237…, 4cb4f38c…, 00185fff…;
`globalgamemanagers` 39058F67…A20). `D:\PP-Instance2` is byte-identical to shipping on everything
sound-related.

## The replacement route, and why

**A media-only bank, loaded at runtime.** `BKHD + DIDX + DATA`, no HIRC, DIDX declaring the GAME's
own media ID; `LoadBankMemoryCopy` it and the shipped sound is replaced with nothing on disk touched.

- Proven as rows 12/13 (test F/G): the same event went 1200 ms → 500 ms, `mediaID=18839791`, and the
  donor's probe for it is named `Spike02_SetMediaPcmNoBankPatch` — **no bank patch was needed**.
- **`src\Wwise\BankGen.cs:113` ALREADY EMITS THIS SHAPE.** It was built and never wired to the author
  route. This is wiring, not new machinery.
- It also reaches **embedded** media, which the file route never can: test G's own example is
  `272177053` = `GUI_MenuClick`, one of the two busiest UI sounds (measured: `MenuEnter` 14 posts in
  20 s at the main menu, `ct_voices watch`).

**Why not `AddBasePath` + a `.wem` in the mod folder** (gate 15a/A2, also proven): the bank still
declares the codec, so a PCM replacement still needs the `ulPluginID` patch **in the shipped bank** —
the exact edit being removed. It also has an unmeasured precedence question (PP registers
StreamingAssets as a base path at init; nobody has measured whether an added path wins for a filename
that exists in both). The media-only bank has no such question: the media is inside the bank handed
to the engine.

## ~~FIRST THING TOMORROW: `ct_sound bake` is UNGATED~~ — CLOSED 2026-08-13, build `db4f197a`

`ct_sound bake` now runs in game and is what shipped:
```
baked …\MenuMusic\Dist\Sounds8540756.bnk: 24583864 B, bankId=4045954504,
  media 208540756 = 128040ms 2ch 48000Hz, loop 0..6145919 play count 0 from 208540756.mp3
ct_sound bake: 2 bank(s) … - NO game file was opened for writing.
```
plus the three UiSounds banks. Both projects' banks were re-shipped from these, so the author's
install now carries banks produced by the DOCUMENTED route, not the offline fallback.

**The offline fallback is NOT bit-identical, and the criterion below was wrong.** Engine decode and
ffmpeg pad differently: `18839791` is 39168 frames / 156856 B in game against 36864 / 147640 B
offline. Same audio, different tail. So `tests\ObjCodecTests --bake` stays a fallback for producing
a bank without a game, and the check for it is the decoded LENGTH, never the hash.

**A defect the run found, now fixed:** two armed bakes in ONE session raced on the shared decode
cache — the second `Cache.Clear()` wiped the first's PCM and that bake printed NOTHING. `EngineAudio.Arm`
now refuses a second arm by name while one is decoding, and the two bakes run in separate phases.

### The original entry, kept for the reasoning

The banks now shipping in both demos were produced by `tests\ObjCodecTests --bake`, which calls the
SAME `WwisePcm.BuildWem` + `BankGen.BuildMediaOnly` with ffmpeg supplying the PCM instead of Unity's
decoder. So the OUTPUT is exercised (loaded on the author's own install: `ct_sound: 5 shipped
replacement bank(s) … 0 failed`) while the AUTHOR-FACING VERB is not. Do not read "the banks work"
as "the command that makes them works" - `ct_sound bake <project>` has never been run in game. It
needs one launch (as of gate A7 its .mp3 decode is the TOOL's, so no flag and no `-UnityAudio` —
that switch no longer exists), and the check is that its bytes match the offline ones for the same
inputs.

## What to build

1. A per-mod declaration of which shipped media it replaces (the existing `"sounds"` key already
   carries `media` + `file`; keep it).
2. ContentTool, at init: for each dependent mod, build/load its media-only bank. `BankGen` shape C +
   `LoadBankMemoryCopy` are both proven; `AudioProbe.LoadBank` already wraps the pinning correctly.
3. `ct_sound apply/revert` and the whole `.ct-backup` ledger become DEV-ONLY and should say so.

## The three unknowns — MEASURED 2026-08-13, build `feb2d3b3` (`ct_sound shapec`)

All three came back the way the design needs, on `18839791` (`GUI_StatsPlusClick`, STREAMED,
1200 ms), with **no game file written**:

| # | question | answer |
|---|---|---|
| C1 | does shape C replace a **STREAMED** media? | **YES** — `1200ms streaming=true(FILE)` → `500ms streaming=false(MEMORY)`, same `mediaID=18839791` |
| C2 | does swapping need `UnloadBank` first? | **NO** — a second media-only bank loaded on top wins: 500 → **1500 ms** |
| C3 | does the game re-loading its own bank undo us? | **NO** — `LoadBank(UIGeoscape)` after ours still reads **1500 ms** |

Control in the same run, before any bank of ours existed: `C-control PASS the untouched event is
1200ms and its file says 1200ms`.

**A FOURTH answer nobody asked for, and it constrains the loader: unloading our bank does NOT put
the shipped sound back.** `C-restore` went RED — after `UnloadBank` on both of ours the event posts
`playingID=5 dur=NO-DURATION-CB … endOfEvent=17ms`, i.e. the voice dies immediately. Within a
session the engine does not fall back to the streamed file it was serving before. Consequences:
- The loader must load once at init and **never unload**. Do not "tidy up" in `OnModDisabled`.
- Disabling a content mod mid-session leaves its replaced sounds BROKEN until restart, not restored.
  A restart is clean, because nothing is on disk.
- Whether a fresh `LoadBank` of the game's bank after unloading repairs it is UNMEASURED.

## Cost to accept, stated

PCM is the only codec this tool emits, so a 128 s stereo track is **~24 MB resident** while its bank
is loaded. Options: accept it for a demo, ship a shorter loop, or settle the `AddBasePath` precedence
question and stream instead. Short SFX are a few hundred KB and are a non-issue.

## What stays true

`S1-zero-runtime` (the author ran with NO mod enabled and still heard the replaced music) remains a
correct measured FACT about the file-overwrite route. It is no longer a goal. Do not build a
both-routes design to preserve it.

## State of the two demos

- **demo #1 `MenuMusic`** — content correct (his track, −6.3 dB, ear-confirmed, overlap fixed by the
  `smpl` loop region in `6a89bd4`), route wrong. Needs re-shaping only.
- **demo #3 `UiSounds`** — replace half green but on the wrong route; ADD half already RIGHT: the mod
  ships its own bundle+bank in its own folder and loads it itself
  (`UiSounds: bank 1830104 B loaded as 80655532, 4 clip(s) on Alt+B`). The Alt+B keypress and the
  "more than one distinct sound" arm are UNRUN — a hotkey needs a human.

## Two traps that cost time today

- `launch-instance.bat sync` MIRRORS the main install: a bundle baked into Instance2's project folder
  is DELETED by the next sync. Stage anything that must survive into the main install first.
- Editing `Options.jopt` by re-serializing the JSON shrank it 32991 → 18996 B. It still parsed, but it
  is a different file. Patch the exact text spans instead.
