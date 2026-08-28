# Demo mod — ADD a sound the game never had, and pay for the trigger

**A content mod is a FOLDER of assets - no code - and ContentTool plays it.** This mod is where that
stops being true, and the reason is worth the whole demo: the *sounds* here are still pure content,
but nothing in Phoenix Point would ever play them. Deciding **when** to post an event is behaviour,
and behaviour is a DLL.

> **This is a SEPARATE MOD.** It installs as `Mods\AddUiSounds\` and the mod manager lists it as
> **ContentTool Demo: Add UI Sounds**. It requires the **ContentTool** mod - `meta.json` declares
> `"Dependencies": [ "com.morgott.ContentTool" ]`, so enabling this one enables ContentTool too.
> Switch it off in the mod manager and it does not load at all.

Its twin is **ContentTool Demo: Replace UI Sounds** (`demos\ReplaceUiSounds\`), which replaces three
shipped geoscape clicks and has **no DLL at all**. **One mod per side, on purpose.**

| | ADD (this mod) | REPLACE (the twin) |
|---|---|---|
| what | two sounds the game never had, one at random on **Alt+B** | three shipped geoscape UI sounds become the author's |
| needs a DLL? | **yes** | **no** |
| why | nothing in the game would ever post a new event, so somebody has to | the media ID already exists and the game already posts an event for it |

```text
AddUiSounds\
  ppcontent.json                    just "id" and "bundle" - it replaces nothing
  Content\Audio\*.mp3               the two ADDED clips - generated, baked into this mod's own bank
  src\AddUiSoundsMain.cs            ~60 lines: load bank, post a random event on Alt+B
```

**There is no `Content\Audio\Replace\` here and there must never be one.** That folder, and a
`Dist\Sounds\<mediaId>.bnk` beside it, are how a mod OVERWRITES a shipped sound - which is the
twin's job. Two mods that both ship a bank for the same media ID both load successfully and the
winner is whichever loaded last, so one of them silently becomes a liar. **Enable both mods
together and nothing overlaps**: this one only adds `morgott_demo_adduisounds_*` events that no
shipped media owns, and the twin only replaces media IDs this one never touches.

## The clips

Both are short UI blips, measured with `ffprobe`: `blip_rise.mp3` **0.35 s** and `blip_fall.mp3`
**0.45 s**, mono 44100 Hz, 3 387 B and 4 223 B. Neither was downloaded — both are **generated** by
`..\tools\make_demo_audio.ps1`, a sine under an exponential envelope, so there is no licence to
chase and nothing of anyone else's ships here. See `SOURCES.md`.

A hotkey gag is not a song: the 4 s tunes this demo used to carry were dropped for being tunes, and
the third-party blips that replaced them were dropped for having no licence anyone could name.
Drop your own `.wav`/`.ogg`/`.mp3` in `Content\Audio\`, add its stem to `Clips` in
`src\AddUiSoundsMain.cs`, and re-bake.

## What the DLL does, and where it stops

`Content\Audio\*.mp3` are baked by `ct_project` into this mod's own Wwise bank inside its own bundle
(gate A1/A3). Both clips are EMBEDDED in the bank, so there is no loose media to manage.

`src\AddUiSoundsMain.cs` does three things and stops: load the bundle, hand the bank to Wwise
(`LoadBankMemoryCopy`, never `View`), and on **Alt+B** post one of the two event IDs at random.

**The event IDs are not a hardcoded table.** They are `fnv1_lower32("morgott_demo_adduisounds_" +
stem)` — the same function the bake used to name them, so the two sides agree by construction and
cannot drift.

**Why Alt+B.** `Assembly-CSharp` reads **zero** `KeyCode.LeftAlt` / `KeyCode.RightAlt` anywhere
(measured by grep over the decompiled tree), so an Alt chord cannot collide with anything the game
binds. Other mods on this machine use `Home` (FreeCamera) and `F12` (Resource Replacer); neither is
B, and neither is an Alt chord.

## Install

```text
ct_project AddUiSounds     # decodes the .mp3, bakes the bank into Dist\AddUiSounds.bundle
```

Then **restart** and press **Alt+B** anywhere. The mod logs the bank size and both event IDs at
startup, so a run can be correlated with `ct_voices watch`: the ID it posts has to be one of them.

No game file is touched here either — the bank lives in this mod's own bundle and is loaded into
memory. Removing the mod removes the sounds.
