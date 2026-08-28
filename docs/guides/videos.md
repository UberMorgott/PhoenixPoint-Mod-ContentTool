# Videos — replacing a cutscene, and adding one

| | **REPLACE** | **ADD** |
|---|---|---|
| What | a clip the game already plays becomes yours | a clip the game never played, on your own trigger |
| Needs a DLL? | **no** | **yes** |
| Why | the def already has a catalog row and the game already plays it | Phoenix Point ships **no** path from your trigger to a cutscene, so there is nothing to redirect |

## A cutscene is THREE assets, not one

This is the finding to read before anything else. A `VideoPlaybackSourceDef` holds three independent
things, and replacing one does nothing to the other two:

| Field | What it points at | How a mod replaces it | Code? |
|---|---|---|---|
| the video clip | a loose `.webm`, played through the player's `url` | `ppcontent.json` `"replace"` → `"video"` | **no** |
| the audio | a Wwise **event**, posted separately | `ppcontent.json` `"sounds"` — the [sound route](sounds.md) | **no** |
| the subtitles | a `TextAsset` **field on the def** | write the field from your own assembly | **yes** |

!!! danger "The video's own audio track is never heard, and that is by design"
    The shipped clips carry a Vorbis stream and so will yours — and neither one is what you hear. The
    game's assembly contains **zero** references to routing a video's audio track anywhere. Every
    sound over a cutscene comes out of Wwise.

    **So a replaced `.webm` plays your picture under the game's original voice-over.** That is not a
    bug; it is the architecture. To replace the sound you replace the Wwise media, separately.

## Codec — the trap that costs an afternoon

Measured:

| Container / codec | Result |
|---|---|
| WebM **VP8 + Vorbis** — what all 69 shipped clips are | **plays** |
| `.mp4`, `.mov`, `.avi` | **plays** |
| `.mkv` | REJECTED |
| WebM **VP9** | REJECTED |
| WebM **AV1** | REJECTED — `Error: Unsupported video codec 'AV1'`, then `VideoPlayer cannot play url`, and `isPrepared` never turns true |

**AV1 is the trap.** A file downloaded from the web is very often AV1 + Opus, the container still says
`.webm`, and it silently never plays. Convert first:

```text
ffmpeg -i yours.webm -c:v libvpx -b:v 1500k -c:a libvorbis -b:a 128k campaign_intro.webm
```

Non-ASCII file names are fine: a clip named with Cyrillic, spaces and an em-dash decoded identically
to a byte-identical ASCII-named twin, 90 frames each.

---

## Replacing a shipped video

### 1. The folder

```text
IntroVideo\
  meta.json                                 "AssemblyName": "IntroVideo.dll"  (subtitles only)
  ppcontent.json                            "replace" -> the picture, "sounds" -> the audio
  Content\
    Videos\
      campaign_intro.webm                   the clip
    Audio\
      Replace\
        intro_theme.mp3                     the audio - a SEPARATE Wwise media
    Subtitles\
      campaign_intro.srt                    CRLF line endings. Not optional - see below.
  Dist\
    Sounds\
      908611677.bnk                         written by `ct_sound bake` - COMMIT AND SHIP IT
  IntroVideo.dll                            ~40 lines, and ONLY for the subtitles
```

Drop the picture and the audio in and you need no DLL at all. The DLL in this demo exists solely
because the subtitles are a def field.

### 2. The manifest, field by field

```json
{
  "id": "morgott.demo.introvideo",
  "bundle": "IntroVideo.bundle",
  "replace": [
    { "video": "campaign_intro", "asset": "StreamableCopiedAssets/Videos/Factions/Phoenix/PP_Intro.webm" }
  ],
  "sounds": [
    { "media": 908611677, "file": "intro_theme.mp3" }
  ]
}
```

| Field | Value | Notes |
|---|---|---|
| `id` / `bundle` | `morgott.demo.introvideo` / `IntroVideo.bundle` | required in every project |
| `replace[].video` | `campaign_intro` | the **stem** of `Content\Videos\campaign_intro.webm` |
| `replace[].asset` | `StreamableCopiedAssets/Videos/Factions/Phoenix/PP_Intro.webm` | the shipped catalog row you are taking over. **Presence of `asset` = REPLACE.** Omit it and the row is an ADD. |
| `sounds[].media` | `908611677` | the cutscene's own Wwise media. See [sounds](sounds.md). |
| `sounds[].file` | `intro_theme.mp3` | a file in `Content\Audio\Replace\` |

### Finding your target — read it off the live def, do not guess from a filename

`ct_video defs <savegame>` prints every `VideoPlaybackSourceDef`, its key and its catalog row on any
install. For the campaign intro it printed:

```text
GeoscapeView.IntroCinematicDef = PP_Intro_Cutscene
  key e574fca8ff2123b48850c43faa7e08c1
  row StreamableCopiedAssets/Videos/Factions/Phoenix/PP_Intro.webm
```

That `row` is exactly what goes in `"asset"`.

**Which Wwise media goes with it** comes out of the bank's own shipped manifest, not a guess: bank
`Cinematics.bnk`, event `PP_Intro` = **1015492702**, media `PhoenixProject_Intro` = **908611677**,
**streamed** — a loose `908611677.wem`. The manifest does not print which media an event plays, so
the pairing was measured: the media's RIFF header declares 44100 Hz and 5 351 789 samples =
**121.35 s**, and the shipped clip is **121.73 s**. No other intro media is within a minute of it.

Being **streamed** is what makes the sound half free — the media-only bank route replaces streamed
media with no code and without writing to the install.

### Your `.srt` must use CRLF line endings

Not a style preference. The game's timestamp parser unconditionally steps one character past the last
digit. With CRLF that step lands on the `\r` and everything works. With LF it lands **on the
newline**, the parser then skips forward to the *next* one, and it **eats the first line of every
cue**. The parser is also unbounded, so a file that ends without a final newline throws an
`IndexOutOfRangeException` while the player is warming up and takes the cutscene with it.

Pin it in `.gitattributes` (`*.srt -text`) so git cannot normalise the endings away, and check the
file offline before shipping:

```text
PASS 3 cue(s), the game's own parser walked the whole file without throwing
```

### 3. The commands, and what they print

```text
ct_video defs <savegame>        every VideoPlaybackSourceDef, its key and its catalog row
ct_video resolve <key>          where that key resolves to right now
ct_video open <key>             open it through the engine's own player - frames and dimensions
ct_video live IntroVideo        serve this mod's clips from its folder now, without a scene load
ct_sound bake IntroVideo        AUTHOR step: writes Dist\Sounds\908611677.bnk
```

At startup, in `Player.log`:

```text
ct_video: 2 content project(s) served in memory, 1 skipped
  IntroVideo: 1 clip(s) served in memory from ...\Mods\IntroVideo; nothing in the install was written
```

**Measured, one run, `ct_video resolve` then `ct_video open`:**

| | Resolves to | Frames |
|---|---|---|
| **control** key `23b0f5ba…` (`Game_Intro_Cutscene`), which this mod does not name | `StreamingAssets/StreamableCopiedAssets/Videos/GameIntro.webm` | **1934 frames 1920×1080** |
| key `e574fca8…` (`PP_Intro_Cutscene`), with the mod on | `…/../../Mods/IntroVideo/Content/Videos/campaign_intro.webm` | **180 frames 1280×720** |

And the sound half, in a run where **no bake was ever issued**: posting `PP_Intro` (1015492702)
answered `dur=6034ms mediaID=908611677 streaming=false(MEMORY)` against **121 355 ms** on disk, with
an untouched media in the same run still reading `streaming=true(FILE)`.

### 4. Bake and package

```powershell
ct_sound bake IntroVideo               # only for the audio half, and only when you change it
$PP = 'D:\Steam\steamapps\common\Phoenix Point'   # your own game folder
.\package.ps1 -Project "$PP\Mods\IntroVideo"   # with the game shut
```

There is **nothing to bake for the picture**. Commit `Dist\Sounds\*.bnk`.

### 5. How a player installs it

Unzip into `Phoenix Point\Mods\`, tick it on. **A player installs nothing and bakes nothing.**

- The **picture** is served from your folder, in memory. Ticking the mod off hands the shipped clip
  straight back, **immediately, no restart**.
- The **sound** is loaded immediately, and **stays until a restart** when the mod is ticked off —
  Wwise goes silent rather than vanilla after an unload. Say so in your description's first line.

### 6. Discovery and the dependency line

`"Dependencies": [ "com.morgott.ContentTool" ]`. ContentTool serves the video rows and loads the banks
for every mod the manager says is ON, at startup and on the checkbox.

### 7. When it does not work

| Line or symptom | What it means |
|---|---|
| `Error: Unsupported video codec 'AV1'` → `VideoPlayer cannot play url`, `isPrepared` never true | AV1. Convert to VP8 + Vorbis. |
| the clip does not play and there is no error | `.mkv` or VP9. Both rejected. |
| your picture plays under the **game's** voice-over | expected — that is the architecture. Replace the Wwise media too. |
| subtitles are missing their first line per cue | LF line endings. Use CRLF. |
| `IndexOutOfRangeException` while the player warms up | your `.srt` has no final newline. |
| `ct_sound probe 908611677` → `probe VOID bank Cinematics declares no event for 'PhoenixProject_Intro'` | a limit of the probe, not of the replacement. Post the event by ID instead: `ct_sound probe event 1015492702` (`PP_Intro`), which is how the 6034 ms reading above was taken. |
| `skipped, disabled in the mod manager` | the player has you switched off. |
| turning the mod off restored the picture but not the sound | expected. Restart. |

---

## Adding a video the game never played

### Why this half costs a DLL

Adding the clip is free. **Making anything play it is not.** Every one of the game's 67
`VideoPlaybackSourceDef`s already has a catalog row — `ct_video defs` reports *67 defs, 0 with no
catalog row* — so there is no empty slot to fill. And Phoenix Point ships no path from, say, quitting
to a cutscene: all 13 call sites that enter a cutscene are intros, research-complete, faction rewards,
the marketplace and two console commands, while both quit routes go straight to the quit call. There
is nothing to redirect, so a new trigger costs a hook.

**If you only want to change which video plays somewhere the game already plays one, you need none of
this.** Use the replace half above.

### 1. The folder

```text
QuitCutscene\
  meta.json                       "AssemblyName": "QuitCutscene.dll"
  ppcontent.json
  Content\
    Videos\
      quit_outro.webm             the clip - your own, 90 frames, 185 KB in the demo
  src\QuitCutsceneMain.cs         the trigger - one patch, plus the one call that serves the clip
  QuitCutscene.csproj             builds it - see section 3
  QuitCutscene.dll                the built output, staged by package.ps1
```

### 2. The manifest — an ADD is a `"replace"` row with **no** `"asset"`

```json
{
  "id": "morgott.demo.quitcutscene",
  "bundle": "QuitCutscene.bundle",
  "replace": [
    { "video": "quit_outro" }
  ]
}
```

| Field | Value | Notes |
|---|---|---|
| `replace[].video` | `quit_outro` | the stem of `Content\Videos\quit_outro.webm` |
| `replace[].asset` | **omitted** | **that omission is the whole declaration of an ADD.** The row is served under a derived RuntimeKey, which is printed for you to paste into your own def. |

The key is derived as `MD5("<your mod id>/<file stem>")` — here
`MD5("morgott.demo.quitcutscene/quit_outro")` — computed identically by the tool and by your DLL, so
renaming the file is the only thing that would need both updated.

### 3. What the DLL does, and the whole of it

It registers the clip on enable and patches one shared call to play it. Almost everything is the
game's own machinery: the home screen already has a "play this, then run that" entry point, and the
skip key needed **no** input handling at all — the cutscene state already routes cancel/submit to the
same callback when the def is interruptible, which is one flag on the def.

**The content half is one call, and it is made by reflection.** This is `Serve()` from the demo,
verbatim:

```csharp
private string Serve()
{
    string clip = System.IO.Path.Combine(Instance.Entry.Directory, "Content\\Videos\\" + ClipStem + ".webm");
    Type api = Type.GetType("Morgott.ContentTool.Bake.CatalogLive, ContentTool");
    if (api == null) return "VOID ContentTool is not loaded - this mod depends on it and has nothing to play";
    MethodInfo reg = api.GetMethod("Register", BindingFlags.Public | BindingFlags.Static);
    if (reg == null) return "VOID ContentTool has no CatalogLive.Register - version mismatch";
    return (string)reg.Invoke(null, new object[] { KeyFor(ModId, ClipStem), clip });
}
```

`CatalogLive.Register(key, absolutePath)` returns a status string and serves your file for that key
for the rest of the run. Nothing is written into the install: no catalog edit, no backup, no revert.
`KeyFor` is the same `MD5("<mod id>/<file stem>")` the manifest section above describes — derived
rather than pasted, so renaming the clip cannot leave a stale constant behind:

```csharp
internal static string KeyFor(string modId, string videoName)
{
    using (MD5 h = MD5.Create())
        return BitConverter.ToString(h.ComputeHash(Encoding.UTF8.GetBytes(modId + "/" + videoName)))
                           .Replace("-", "").ToLowerInvariant();
}
```

**Why reflection here and a plain reference in the weapon and creature recipes:** both are legal, and
the choice is about what should happen when ContentTool is missing. A weapon mod is meaningless
without it, so it references the assembly and fails loudly. This mod would still have a trigger worth
running, so it degrades into a logged `VOID` line instead. The rule for which references are safe at
all is in [the reference rule](reference.md#10-the-two-rules-that-are-not-negotiable), and it is the
one thing on this page that can switch off every mod the player has.

The enable hook wires both halves together:

```csharp
public override void OnModEnabled()
{
    self = this;
    Harmony h = (Harmony)HarmonyInstance;
    h.PatchAll(Assembly.GetExecutingAssembly());
    Logger.LogInfo("Q1-content " + Serve());
}
```

`HarmonyInstance` is handed to you by `ModMain` — you do not create your own.

### The `.csproj`

`package.ps1` builds the first `*.csproj` in your mod folder and looks for
`bin\Release\**\<FolderName>.dll`, so **`<AssemblyName>` must equal the folder name** and `meta.json`
must declare it. This is the demo's, complete — note that it references **only** what `ModSDK\`
ships, which is why the `UnityEngine.Video` and PNG calls elsewhere in the demo go through
reflection:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <AssemblyName>QuitCutscene</AssemblyName>
    <RootNamespace>Morgott.QuitCutscene</RootNamespace>
    <TargetFramework>net472</TargetFramework>
    <LangVersion>latest</LangVersion>
    <AppendTargetFrameworkToOutputPath>false</AppendTargetFrameworkToOutputPath>
    <EnableDefaultCompileItems>false</EnableDefaultCompileItems>
    <OutputPath>bin\$(Configuration)\QuitCutscene\</OutputPath>
  </PropertyGroup>
  <ItemGroup>
    <Compile Include="src\**\*.cs" />
    <None Include="meta.json" CopyToOutputDirectory="PreserveNewest" />
  </ItemGroup>
  <PropertyGroup>
    <PPRoot Condition="'$(PPRoot)' == ''">D:\Steam\steamapps\common\Phoenix Point</PPRoot>
    <ModSDK>$(PPRoot)\ModSDK</ModSDK>
  </PropertyGroup>
  <ItemGroup>
    <Reference Include="Assembly-CSharp">
      <HintPath>$(ModSDK)\Assembly-CSharp.dll</HintPath>
      <Private>false</Private>
    </Reference>
    <Reference Include="0Harmony">
      <HintPath>$(ModSDK)\0Harmony.dll</HintPath>
      <Private>false</Private>
    </Reference>
    <Reference Include="UnityEngine.CoreModule">
      <HintPath>$(ModSDK)\UnityEngine.CoreModule.dll</HintPath>
      <Private>false</Private>
    </Reference>
  </ItemGroup>
</Project>
```

There is **no `ContentTool` reference** — that is what the reflection above is for.

Three things this demo learned the hard way, and every video-adding mod will hit them:

!!! danger "A runtime def has a factory. `ScriptableObject.CreateInstance` gives you a half-built def."
    It produces an object of the right *type* and nothing else: the def's `Guid` and `ResourcePath`
    are both left **null**, and it is not in the def repository. Playback itself reads neither, so the
    mistake is **invisible** — until something else reads them.

    Something else does. TFTV postfixes the very state this mod enters and, with *skip movies* on,
    reads `_sourcePlaybackDef.ResourcePath.Contains(...)`. It guards against a null *def* and not a
    null *ResourcePath*, so a `CreateInstance` def threw a `NullReferenceException` inside that
    postfix on every single entry. TFTV catches and logs it, so nothing crashed and nothing said "the
    mod is broken".

!!! warning "`ResourcePath` must be set, and must not lie"
    Set it to something of your own — the demo uses `Morgott/QuitCutscene/quit_outro`. It deliberately
    does **not** contain `Game_Intro_Cutscene`: with TFTV's *skip movies* option on, a def whose
    `ResourcePath` claims to be the intro gets cancelled the instant it starts.

    That is also the answer to "why does the game's own intro flash by and vanish". If you read a log
    and see the cutscene state entered and then the initial state one frame later, that is the boot
    intro being skipped by TFTV working exactly as intended, not your mod misbehaving.

!!! warning "Never take a quit away without a watchdog"
    A mod that hijacks the quit and then does not quit is worse than a mod that does nothing. Resolve
    your clip **before** hijacking anything — and if it cannot resolve, leave the quit alone — then
    put an 8-second watchdog behind it that quits regardless. The real quit is idempotent, so the
    normal ending is unaffected.

What it prints, and what was measured:

```text
Q1-src PASS  ->  Q1-trigger  ->  Q1-play PASS ... frameCount=90
```

`ct_video open` on the added key, before any of that:

```text
key 6f3d8e3d... -> ...\Mods\QuitCutscene\Content\Videos\quit_outro.webm, 90 frames 1280x720
```

The key did not exist before the mod; every one of the 67 shipped defs resolves into the game's own
streaming assets. On a real quit the engine reported
`prepared=True playing=True frameCount=90 length=3s 1280x720 playbackSource=QuitCutscene_Runtime`,
and then the process exited.

### 4. Bake and package

```powershell
# nothing to bake - a loose video is served from your folder as it is
$PP = 'D:\Steam\steamapps\common\Phoenix Point'   # your own game folder
.\package.ps1 -Project "$PP\Mods\QuitCutscene"    # builds your .csproj and stages the DLL
```

### 5. How a player installs it

Unzip into `Phoenix Point\Mods\`, tick it on alongside ContentTool. **That is the whole install**: the
mod registers its own clip on enable, so there is no command to run, no restart and nothing to
uninstall.

### 6. Discovery and the dependency line

`"Dependencies": [ "com.morgott.ContentTool" ]`. Keys are published on the checkbox and un-published
on the checkbox, in the same session.

This demo calls ContentTool **by reflection** so that a missing engine is a logged line rather than a
dead mod — see [the code above](#3-what-the-dll-does-and-the-whole-of-it) and
[the reference rule](reference.md#10-the-two-rules-that-are-not-negotiable), which is where the real
constraint lives: never reference an assembly nothing loads for you, because a failed mod load makes
the game rewrite the activated-mods list empty and silently disable every other mod.

### 7. When it does not work

| Line or symptom | What it means |
|---|---|
| a `NullReferenceException` where the catalog is dereferenced, or a black screen ten lines later | the key is missing from the live catalog. Resolve the clip first and print the path. |
| a `NullReferenceException` inside *another mod's* postfix on your def | you built the def with `CreateInstance`. Use the runtime-def factory, and set `ResourcePath`. |
| your cutscene is cancelled the instant it starts | your `ResourcePath` contains `Game_Intro_Cutscene` and TFTV's skip-movies is on. |
| the quit hangs | your trigger took the quit and did not finish. Add the watchdog. |
| `Q1-play FAIL` with a url, `isPrepared` and `frameCount` | read them: a missing catalog row and a rejected codec look identical without those three. |

### Known ceiling

Quitting from an **in-game** escape menu exits with no clip. The geoscape's own cutscene entry point
takes a priority and not a completion callback, so there is no shipped "play then continue" to borrow
outside the home screen. Quitting from the **main menu** is the demonstrated path; the patch says so
in the log and quits normally rather than pretending.
