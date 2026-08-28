# Sounds — replacing one, and adding one the game never had

The two halves of the audio ladder, and the line between them is exact:

| | **REPLACE** | **ADD** |
|---|---|---|
| What | a shipped sound becomes yours | a sound the game never had |
| Needs a DLL? | **no** | **yes** |
| Why | the media ID already exists and the game already posts an event for it | nothing in Phoenix Point would ever post a new event, so somebody has to |

`.wav`, `.ogg` and `.mp3` are all decoded by ContentTool itself, on both paths. No flag, no launch
option, no external converter. `.flac`, `.m4a`, `.aac`, `.wma` and `.opus` are refused **by name**
before any decode, and the rest of the project still installs.

---

## Finding the sound you want to change

A replacement is keyed on a **media ID**, and the media ID *is* the file name — Phoenix Point's loose
sounds are 3105 files called things like `18839791.wem`. So the listing is honest and useless on its
own:

```text
> ct_list audio
3105 .wem file(s) match '' under D:/PP-Instance2/PhoenixPointWin64_Data/StreamingAssets\Audio
  1000251363  GeneratedSoundBanks/Windows/1000251363.wem
  1000992511  GeneratedSoundBanks/Windows/1000992511.wem
  1001210683  GeneratedSoundBanks/Windows/1001210683.wem
  ... 3045 more (narrow the filter)
```

Nothing there tells you which one is the button you just clicked. **You do not pick a sound from that
list. You make the game play it and watch what it posts.**

### `ct_voices` — arm it, do the thing, read the answer

```text
ct_voices watch [seconds]        default 20
```

It patches Wwise's `PostEvent` while it runs, so it counts what the **game** posts — not what you
post — then names every event off the shipped bank listings and resolves each one to the media ID
`ct_sound bake` takes. The report prints to `Player.log`, not to the console window, because it
arrives after your command has already returned.

A real run. `ct_voices watch 30` armed while the main menu was coming up:

```text
t=2s  posts so far: 799408924x1 3086540886x1 | live voices: 3086540886->1 799408924->1
...
ct_voices timeline (2 post(s) seen through 4 overload(s)):
  931ms event=3086540886 on 'HomeScreenLevel(Clone)'
  933ms event=799408924 on 'HomeScreenLevel(Clone)'
ct_voices what those events PLAY (event -> media, the id 'ct_sound bake' takes):
  event 799408924 x1  'MainMenuMusicStart' in MainMenuMusic, TacticalMusic -> media 208540756 'MainMenuMusic' - replaceable
  event 3086540886 x1  'StopAll' in UI -> no STREAMED media named 'StopAll', so its sound is embedded in a bank and cannot be replaced by a media bank
ct_voices: DONE after 30s
```

That is the whole discovery step: *"I want to change the main menu music"* → **media 208540756**,
which is exactly what the `MenuMusic` demo replaces. Drop a file called `208540756.mp3` into
`Content\Audio\Replace\` and you are done.

**Timing is the whole technique.** A sound is posted **once**, at the moment the thing happens. The
menu music is posted while the menu is still loading, so a watch armed after you are already looking
at the menu catches nothing at all — and says so rather than reading as silence:

```text
ct_voices: nothing was posted, so there is nothing to name. Arm the watch, then do the thing you want to hear.
```

Arm it *first*, then do the thing: open the screen, click the button, start the mission, take the
shot.

### `replaceable` versus `embedded` — and why half of them are not yours

A second real run, `ct_voices watch 45` armed and then a tactical mission loaded:

```text
ct_voices what those events PLAY (event -> media, the id 'ct_sound bake' takes):
  event 1976069036 x3  'MistLoopStart' in EnvironmentMist -> no STREAMED media named 'MistLoop', so its sound is embedded in a bank and cannot be replaced by a media bank
  event 2051387629 x2  'TacticalMusicStart' in TacticalMusic -> no STREAMED media named 'TacticalMusic', so its sound is embedded in a bank and cannot be replaced by a media bank
  event 3618869941 x1  'PlayerTurnStart' in UI -> no STREAMED media named 'PlayerTurn', so its sound is embedded in a bank and cannot be replaced by a media bank
  event 3711598968 x2  'MenuClick' in UI -> no STREAMED media named 'MenuClick', so its sound is embedded in a bank and cannot be replaced by a media bank
```

Every event was **named**, and none of them was replaceable — because those sounds live *inside* a
bank rather than as a loose `.wem`. That is not a limit of the instrument, it is the coverage line
from *"What can and cannot be replaced"* below, arriving early enough to save you the work: of the
game's **843 events, 389** resolve to a streamed media and the rest are embedded.

Two more things the resolution does that you will meet:

- **It searches every bank, not the event's own.** The event `StatXPBangupStop` is declared in
  `UIGeoscape` while the media it plays is listed in `UI` — looking only in the event's own bank
  would find nothing.
- **One event can own several media.** A random container fans out: `MissionWinShow` plays one of
  `317726851`, `445739832`, `539878758`, and the report says
  `- replace ALL of them, the event picks between them`. Replace one and the sound changes about a
  third of the time, which is a maddening bug to chase.

### Going the other way — `ct_sound status <mediaId>`

Once you have a media ID, this tells you what it is, how long it is, and which bank and event own it.
It is also how you sanity-check an ID somebody handed you:

```text
> ct_sound status 18839791
audio root: D:/PP-Instance2/PhoenixPointWin64_Data/StreamingAssets\Audio (READ ONLY - ContentTool never writes there)
legacy: none - no .ct-backup and no sounds.ct-edits under ...\Audio, so no shipped .wem or .bnk here was ever overwritten by ContentTool
18839791: ...\Audio\GeneratedSoundBanks\Windows\18839791.wem 27495 B, 1200ms, vorbis 2ch 44100Hz | bank UIGeoscape, sound 'GUI_StatsPlusClick', event 784388130
```

With no media ID it prints only the two header lines — the audio root and the legacy check. That is
the answer to *"has any ContentTool ever written into my install's audio?"*, and `none` is the whole
of it.

### Hearing one — `ct_sound probe event <eventId>`

`ct_voices` hands you event IDs; this posts one and reads back what the engine actually served. Every
shipped bank that declares the event is loaded first, because a bank the game has finished with is
not resident and an unresident event does not start.

```text
> ct_sound probe event 1015492702
probe event 1015492702: 'PP_Intro' in Cinematics -> no STREAMED media named 'PP_Intro', so its sound is embedded in a bank and cannot be replaced by a media bank
LoadBank(Cinematics): AK_Success
POST event/1015492702: playingID=1 dur=6034ms estDur=6034ms mediaID=908611677 streaming=false(MEMORY) endOfEvent=TIMEOUT
```

`mediaID=908611677 streaming=false(MEMORY)` is the measurement: that is the media the engine served
and it came out of memory, not off the shipped file. The control, in the same session — an event ID
nothing owns:

```text
> ct_sound probe event 4000000001
probe event 4000000001: no shipped bank .txt names this event (a mod's own event, or a bank that ships no listing)
POST event/4000000001: playingID=0 POST FAILED (the event did not start; no callback can arrive and nothing below was measured)
```

A bogus event does not quietly return a handle. That is what makes the positive above mean something.

---

## Replacing a shipped sound

### 1. The folder

```text
ReplaceUiSounds\
  meta.json                             "AssemblyName": ""   <- no DLL at all
  ppcontent.json                        seven lines
  Content\
    Audio\
      Replace\
        tblehit04.mp3                   your three replacements, under their own names
        band_stretch_release_slap.mp3
        zvuk_-kloun-gudok_-clown-hor.mp3
  Dist\
    Sounds\
      18839791.bnk                      written by `ct_sound bake` - COMMIT AND SHIP THESE
      633458426.bnk
      940964934.bnk
```

`Dist\Sounds\<mediaId>.bnk` is a **media-only bank**. Its presence is the declaration: ContentTool
loads every enabled mod's banks into memory at its own init.

### 2. The manifest, field by field

```json
{
  "id": "morgott.demo.replaceuisounds",
  "bundle": "ReplaceUiSounds.bundle",

  "sounds": [
    { "media": 18839791,  "file": "tblehit04.mp3" },
    { "media": 633458426, "file": "band_stretch_release_slap.mp3" },
    { "media": 940964934, "file": "zvuk_-kloun-gudok_-clown-hor.mp3" }
  ]
}
```

| Field | Value | Notes |
|---|---|---|
| `id` | `morgott.demo.replaceuisounds` | must equal `meta.json`'s `ID` |
| `bundle` | `ReplaceUiSounds.bundle` | **declared and never built.** The manifest reader requires the field for every project; the sound bake reads only `id` and `sounds`, so a sounds-only mod never has a bundle. |
| `sounds[].media` | `18839791` | the **shipped media ID** you are overwriting. |
| `sounds[].file` | `tblehit04.mp3` | a file in `Content\Audio\Replace\`. Its own name — nothing is renamed. |

The three targets in that example, and what they are:

| media | shipped sound | event | gets | length |
|---|---|---|---|---|
| 18839791 | `GUI_StatsPlusClick` | 784388130 | `tblehit04.mp3` | 0.84 s |
| 633458426 | `GUI_SkillConfirmClick` | 1437631612 | `band_stretch_release_slap.mp3` | 0.40 s |
| 940964934 | `GUI_CancelMission` | 346433775 | the clown horn | 0.91 s |

!!! note "There is a second, even simpler grammar"
    A `.wav` / `.ogg` / `.mp3` **named after the media ID it replaces**, dropped in
    `Content\Audio\Replace\`, needs **no JSON at all** — the file name *is* the target. That is how the
    menu-music demo works: `Content\Audio\Replace\208540756.mp3` and `423563089.mp3`, and its
    `ppcontent.json` is nothing but `id` and `bundle`.

    Non-ASCII names are fine on the **ADD** path. On the REPLACE path the name is the media ID — a
    rule about grammar, not about encoding.

### What can and cannot be replaced

- **Coverage is the 3105 STREAMED media**, which are loose files. A media embedded inside a bank is
  refused by name: `media <id> is in a bank, not a file, and is refused by name -> <why>`.
- **The media ID must be one Phoenix Point actually owns.** The bake refuses anything else rather than
  writing a bank nothing would ever play.
- **Length is free.** The shipped intro theme is 121 s and its replacement is 6 s.
- **One media ID, one mod.** Two mods that both ship a bank for the same media ID both load, and the
  winner is whichever loaded last — so an overlap is *silent*, not an error. Do not overlap.

### 3. The commands, and what they print

```text
ct_extract audio 208540756       writes 208540756.wav for you to edit
ct_sound bake ReplaceUiSounds    .mp3 -> the TOOL decodes; writes Dist\Sounds\<mediaId>.bnk
ct_sound status [mediaId]        what is declared, and which bank serves it
ct_sound probe <mediaId>         post that media's event and read mediaID / storage back
ct_sound probe event <eventId>   post an event by ID - for media whose bank declares none
```

`ct_sound bake` is the **author's** command. Re-run it whenever you change one of the source files.
It prints one line per replacement — the bank it wrote, its size, and the decoded length, channel
count and sample rate it got out of *your* file, so a source that decoded wrong is visible here and
not in game:

```text
> ct_sound bake ReplaceUiSounds
declared 3 replacement(s) in D:\PP-Instance2\Mods\ReplaceUiSounds\Content\Audio\Replace
baked ...\Dist\Sounds\18839791.bnk: 156856 B, bankId=3272749787, media 18839791 = 888ms 2ch 44100Hz, loop 0..39167 play count 0 from tblehit04.mp3
baked ...\Dist\Sounds\633458426.bnk: 87736 B, bankId=1150663706, media 633458426 = 456ms 2ch 48000Hz, loop 0..21887 play count 0 from band_stretch_release_slap.mp3
baked ...\Dist\Sounds\940964934.bnk: 161464 B, bankId=2891764803, media 940964934 = 914ms 2ch 44100Hz, loop 0..40319 play count 0 from zvuk_-kloun-gudok_-clown-hor.mp3
ct_sound bake: 3 bank(s) in D:\PP-Instance2\Mods\ReplaceUiSounds\Dist\Sounds - NO game file was opened for writing. ContentTool loads these at init.
```

`declared 3` is the count it found; three `baked` lines mean three banks exist. A source that did not
decode is named and skipped, and the count on the last line is then lower than the first.

At startup, in `Player.log`:

```text
ct_sound: 2 shipped replacement bank(s) from ...\Mods, 0 failed, 1 skipped
  MenuMusic\208540756.bnk 24583864 B -> AK_Success ...
```

### The measurement that tells a replacement from a shipped sound

An **unreplaced** loose `.wem` probes as `streaming=true(FILE)` with a duration exactly equal to the
file's own header. **Every replaced media probes as `streaming=false(MEMORY)`.** That pairing is what
makes *"the engine is not reading the shipped file"* a measurement rather than an inference.

Measured on this demo, with **no bake ever run in that install** — the mod folder deleted and
re-deployed from its shipped files only:

| media | shipped | modded |
|---|---|---|
| 18839791 | **1200 ms** | **888 ms** |
| 633458426 | **3533 ms** | **456 ms** |
| 940964934 | **2231 ms** | **914 ms** |

All three `streaming=false(MEMORY)`. The control, taken **first** in the same run: an unreplaced
media reads **15212 ms, `streaming=true(FILE)`**.

!!! warning "Take the control FIRST, or take it in its own session"
    The probe slot is single and static. After three consecutive `ct_sound probe` calls, a fourth came
    back `dur=NO-DURATION-CB mediaID=? streaming=?`. The same control on a fresh session was clean.

!!! note "Music yields no duration"
    A looping music event returns `dur=0ms … endOfEvent=TIMEOUT`, so for a music replacement the only
    discriminator is `mediaID` + MEMORY-vs-FILE storage. Measured on the menu-music demo: the mod's
    bank served `mediaID=208540756`, `streaming=false(MEMORY)`, out of a 24 583 864 B bank
    (`LoadBankMemoryCopy: AK_Success`), while the on-disk `208540756.wem` — 3 687 722 B, 142 978 ms —
    stayed untouched.

### 4. Bake and package

```powershell
ct_sound bake ReplaceUiSounds              # in game, after editing an audio file
$PP = 'D:\Steam\steamapps\common\Phoenix Point'   # your own game folder
.\package.ps1 -Project "$PP\Mods\ReplaceUiSounds"  # with the game shut
```

Commit `Dist\Sounds\*.bnk`. That is what makes the download work with no bake.

### 5. How a player installs it

Unzip into `Phoenix Point\Mods\`, tick it on. **No bake, no console command.** The banks are loaded
into memory at ContentTool's init; **no game file is opened for writing**, so there is no backup, no
`apply` and no `revert`.

!!! warning "Switching a sound mod OFF needs a restart"
    This is the one route the checkbox is not symmetric on, and your mod description should say so
    in its **first line**. Measured: after unloading the bank the event dies at 17 ms instead of
    falling back to the shipped media — **Wwise goes silent, it does not go vanilla.** Silence is a
    broken game, not a restored one, so the bank is left loaded and the log says so. Nothing was
    written into the install, so a restart is a clean undo.

### 6. Discovery and the dependency line

`"Dependencies": [ "com.morgott.ContentTool" ]`. ContentTool loads `Dist\Sounds\*.bnk` from **every
mod the manager says is ON**, at startup and on the checkbox. With ContentTool off, a mod with no
assembly does not load at all — see
[the reference](reference.md#3-the-dependency-line-what-it-actually-buys).

### 7. When it does not work

| Line or symptom | What it means |
|---|---|
| `media <id> is in a bank, not a file, and is refused by name -> <reason>` | that media is embedded, not streamed. Only the 3105 streamed media are reachable this way. |
| the bake refuses your media ID | Phoenix Point does not own it. A bank for a nonexistent media would never play. |
| `.flac` / `.m4a` / `.aac` / `.wma` / `.opus` named and skipped | those formats have no decoder and never will. Convert to `.wav`, `.ogg` or `.mp3`. |
| `ct_sound probe <id>` → `probe VOID bank <X> declares no event for '<name>'` | a limit of the **probe**, not of the replacement: probing *by media* only works where some bank declares an event for that media. Use `ct_sound probe event <eventId>` instead — `ct_voices` is where you get the event ID. |
| `package.ps1` → `a sound replacement that was NEVER BAKED` | you have a source in `Content\Audio\Replace\` with no `Dist\Sounds\<mediaId>.bnk` beside it. The packager refuses rather than shipping a mod that plays the shipped sound. Run `ct_sound bake <YourMod>`. |
| `dur=NO-DURATION-CB mediaID=? streaming=?` | the probe slot was already used three times this session. Fresh session, control first. |
| `dur=0ms … endOfEvent=TIMEOUT` | a music event. Expected; use storage as the discriminator. |
| the sound is still the shipped one | your bank was not shipped. Check `Dist\Sounds\<mediaId>.bnk` is committed and in the package. |
| turning the mod off did not restore the sound | expected. Restart. |
| two mods, one sound, wrong winner | both banks loaded; the last one wins, silently. One media ID, one mod. |

---

## Adding a sound the game never had

### 1. The folder

```text
AddUiSounds\
  meta.json                       "AssemblyName": "AddUiSounds.dll"
  ppcontent.json                  just "id" and "bundle" - it replaces nothing
  Content\
    Audio\
      zvuky-2.mp3                 the ADDED clips - baked into this mod's OWN bank
      7a5fa5d4f12cb3f.mp3
  Dist\
    AddUiSounds.bundle            written by `ct_project` - the bank is INSIDE it. SHIP IT.
  AddUiSounds.dll                 ~60 lines: load the bank, post an event
```

!!! danger "There is no `Content\Audio\Replace\` here, and there must never be one"
    That folder — and a `Dist\Sounds\<mediaId>.bnk` beside it — is how a mod **overwrites** a shipped
    sound, which is the other half of this page. Two mods that both ship a bank for the same media ID
    both load and the last one wins, so one of them silently becomes a liar. Add **or** replace, per
    mod.

### 2. The manifest

That is genuinely all of it:

```json
{
  "id": "morgott.demo.adduisounds",
  "bundle": "AddUiSounds.bundle"
}
```

`Content\Audio\*.mp3` are baked by `ct_project` into your mod's own Wwise bank, inside your own
bundle. Both clips are **embedded** in that bank, so there is no loose media to manage. The file's
presence is the declaration — there is no JSON row for an added sound.

### 3. What the DLL does, and where it stops

Three things, and then it stops:

1. load your bundle,
2. hand the bank to Wwise with **`LoadBankMemoryCopy`, never `LoadBankMemoryView`**,
3. post an event.

**The event IDs are not a hardcoded table.** They are `fnv1_lower32("<your mod's prefix>_" + stem)` —
the same function the bake used to name them, so the two sides agree by construction and cannot
drift. Wwise name→ID is **FNV-1 32-bit, name lowercased first**; there is no masking.

At startup, this demo logs:

```text
AddUiSounds: bank 246920 B loaded as 432470233, 2 clip(s) on Alt+B
```

**Picking a hotkey.** `Alt`+letter is safe: Phoenix Point's own assembly reads **zero**
`KeyCode.LeftAlt` / `KeyCode.RightAlt` anywhere, so an Alt chord cannot collide with anything the game
binds. Other mods on this machine use `Home` and `F12`.

### The measurement

Both events posted, in a fresh session, with **no bake ever run in that install**:

| event | result |
|---|---|
| `802143502` | `playingID=6 mediaID=3338666240 dur=1489 ms`, `AK_EndOfEvent=1472 ms`, **MEMORY** |
| `3282871088` | `mediaID=3338666241 dur=653 ms`, `endOfEvent=657 ms`, **MEMORY** |
| **control** — an unregistered event `4000000001` | **`playingID=0`, POST FAILED** |

That control is what makes the two positives mean something: a bogus event ID does not quietly return
a handle, it fails.

### 4. Bake and package

```powershell
ct_project AddUiSounds                 # in game, after editing an audio file
$PP = 'D:\Steam\steamapps\common\Phoenix Point'   # your own game folder
.\package.ps1 -Project "$PP\Mods\AddUiSounds"  # with the game shut - builds your .csproj and stages the DLL
```

Commit `Dist\AddUiSounds.bundle`. It carries the bank.

### 5. How a player installs it

Unzip into `Phoenix Point\Mods\`, tick it on, press the hotkey. **No bake, no console command.** No
game file is touched — the bank lives in your own bundle and is loaded into memory. Removing the mod
removes the sounds.

### 6. Discovery and the dependency line

`"Dependencies": [ "com.morgott.ContentTool" ]`. This mod **does** ship an assembly, so with
ContentTool off it would load and then find nothing to load its bundle with. Declare the dependency
so the manager switches ContentTool on.

### 7. When it does not work

| Line or symptom | What it means |
|---|---|
| `playingID=0`, POST FAILED | the event ID you posted is not in any loaded bank. Check you are hashing the same string the bake did, lowercased. |
| a valid `playingID` and **nothing audible** | routing. A bank node with both its output bus and its parent left at zero hands out a playing ID and is silent. |
| `LoadBankMemoryCopy` returns anything but `AK_Success` | the bank's declared output bus does not exist. A missing bus is a hard bank-load failure, resolved first, not silent audio. |
| the game crashed at bank load | you used `LoadBankMemoryView`. For bank version ≥ 135 it does a real division by the alignment, and a zero alignment is a process crash, not a result code. Always `Copy`. |
| `meta.json declares "AssemblyName": "AddUiSounds.dll" but the package does not contain that file` | build before packaging, or set `"AssemblyName": ""`. |
| every other mod on the machine switched itself off | a reference your assembly could not resolve when its code first ran — most likely a Unity module out of the game's `Managed\` folder that `ModSDK\` does not ship. Phoenix Point installs no `AssemblyResolve` handler, and it answers a failed mod load by rewriting the activated-mods list **empty**. Reach those modules **by reflection**; referencing `ContentTool.dll` is not this trap. See [the reference rule](reference.md#10-the-two-rules-that-are-not-negotiable). |
