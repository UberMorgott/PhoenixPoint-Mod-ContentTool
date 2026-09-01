# Research — source-format coverage (spike, 2026-08-12)

**The question:** the mandate is "accept anything, with no external tools". The hoped-for lever was
that the game's own Unity runtime already ships decoders — using one is not an external program.
That turned out to be true per-API, not in general: **it holds for images and video, and for audio it
holds only after one byte on the AUTHOR'S machine is flipped** — dead as shipped (§0), alive at bake
time (**§2.1**, measured). So each row is settled by an artifact (a shipped DLL, a build-data value, a decompiled
call site) or a pasted in-game measurement — never by "Unity generally does X". Verdicts: **FREE** (the engine decodes it), **PARSER** (a hand-written container reader, lines
estimated), **IMPOSSIBLE** (needs a codec nobody can hand-write, or a third-party dependency), or
**UNMEASURED** (naming the missing run).

**Every row below is a RUN, not a recollection.** In-game rows come from gate **F1**
(`ct_fmt`, `src\Dev\FormatProbe.cs`), build `b84c1085`, `ct_fmt: controls ALL PASS`. Offline .wav
rows come from `tests\ObjCodecTests` (`WAV: ALL PASS, 22 check(s)`). Probe corpus is written by
`tools\make-format-probes.ps1`.

- **ffmpeg is used to MANUFACTURE probe files and nowhere in the tool.** "No external tools" is
  about what a mod author must install, not about how a measurement gets its input. Nothing the
  script writes is committed and nothing ships.
- Controls carried in the same run: `tex.png`, `vidctl.webm` (the clip gate V1 already plays) must
  decode; `tex.junk` / `aud.junk` / `vid.junk` (4,096 random bytes wearing the extension) must NOT.
- The `F1-audio-*` and `F1-aud VOID` lines are only in the phase log — `autogate.ps1` filters
  printed lines to `PASS|FAIL|VOID|...` and those two carry no keyword.

---

## 0. Two assumptions, both settled from the artifact

Neither "the engine decodes it for free" nor "Unity audio is stripped" was allowed to stand as an
assertion. The game is decompiled and its build data is readable, so both were checked there first.

**Which modules actually ship** (`PhoenixPointWin64_Data\Managed\`, a file listing, not an
inference) — **every one is PRESENT**, so nothing was stripped at build time:
`UnityEngine.AudioModule.dll` 56.5K · `UnityEngine.UnityWebRequestAudioModule.dll` 11.0K ·
`UnityEngine.ImageConversionModule.dll` 13.0K (where `Texture2D.LoadImage` lives) ·
`UnityEngine.VideoModule.dll` 28.5K · plus `AK.Wwise.Unity.API.dll` 287.5K.

**The audio subsystem is DISABLED by project setting, not stripped.** Read straight out of
`PhoenixPointWin64_Data\globalgamemanagers`, object `AudioManager`:
```
m_DisableAudio: True      m_SampleRate: 0      Default Speaker Mode: 2
m_DSPBufferSize: 1024     m_VirtualVoiceCount: 512     m_RealVoiceCount: 32
```
`boot.config` carries no audio flag — the switch is this one. So the DLL is present and the device
never opens: **present-and-dead, which is a different fact from absent.** At the time this was
written that was read as "fatal to the `AudioClip` route all the same" — **and that inference is now
measured WRONG (§2.1)**: a switch is a thing you can throw. `m_DisableAudio` is one byte at file
offset **7168**, this tool patches serialized Unity files for a living, and with it flipped on the
author's machine the `AudioClip` route decodes `.ogg` and `.mp3` perfectly. Present-and-dead is not
absent, and it is not permanent either.

**PP's own code never uses a Unity audio object.** `CameraManager.cs:311-320` deliberately attaches
an `AkAudioListener` (Wwise) to the camera instead of a Unity `AudioListener`;
`VideoPlaybackController.cs:80,97-101` names a field `AudioSource` but its type is `VideoSoundDef`
(`VideoPlaybackSourceDef.cs:34`) — Wwise again, so even cutscene sound never touches Unity audio.
The only real `UnityEngine.AudioSource` references in the whole decompile are in the bundled Enviro
weather package (`EnviroAudioSource.cs:17`) and an unused helper (`Base.Utils\AudioUtils.cs:8`).

**This was already recorded — in four places, including this tool's own do-not-re-research list.**
`PROVEN-FOUNDATIONS.md:544` puts Unity `AudioClip`/`AudioSource` (`m_DisableAudio = true`) under
**"Permanently ruled out — do not re-research"**; `FINAL-PLAN.md:807` says the same;
`docs\research\pp-audio-architecture-FROZEN.md:12,108` states *"Unity audio is DEAD in PP
(`m_DisableAudio = true`) — no `AudioSource` path exists"*; and ResourceReplacer already ran the
runtime spike, `research\wwise-spikes\Spike05_UnityAudioReEnable.cs`, tabulated
**"Dead (m_DisableAudio=true)"** at `research\wwise-spikes\README.md:22`.
**So the omission was not in the docs — it was in the reading.** "The engine decodes ogg/mp3 for
free" was proposed against a fact already sitting in the section whose entire purpose is to stop
exactly that. The lesson to record is procedural: check "Permanently ruled out" **before** writing
a brief that assumes a native path, not after measuring one.

> **And now the other half of that lesson (§2.1).** All four of those records — including this
> tool's own do-not-re-research list — recorded a *runtime* verdict and were read as a *permanent*
> one. Every one of them traces back to the same runtime probe. A "permanently ruled out" row is
> only as wide as the run that produced it: `Spike05` could not have tested a startup flag, because
> mod code does not exist yet when the engine reads it. **Ask what a rejection actually measured
> before treating it as a wall** — the cheap re-test here overturned a four-place ruling and deleted
> ~5-8 kLOC of planned decoder work.

Gate F1 then re-measured it independently in-process (`outputSampleRate=0`,
`AudioSettings.Reset` → `False`), so the artifact and the run agree.

---

## 1. THE MP4 ANSWER — yes, measured

```
F1-vid-vid-mp4     PASS decoded frameCount=7 128x72 len=0,47s
F1-vid-vidctl-webm PASS decoded frameCount=60 256x144 len=2,00s  [CONTROL: must decode]
F1-vid-vid-junk    FAIL not decoded (Can't play movie [...vid.junk])  [CONTROL: must NOT decode]
```
build `b84c1085`, `ct_fmt: controls ALL PASS`. H.264 + AAC in MP4 prepares and reports a frame
count through the **same** `VideoPlayer` url path the game uses for its own cutscenes
(`VideoPlaybackController.cs:148-150`). `research-video-replacement.md` §4 explicitly flagged
H.264-in-mp4 as "a remembered default, not a measurement" — it is now measured, and **true**.

## 2. Matrix

### VIDEO — `VideoPlayer.source = VideoSource.Url`

| Format | Verdict | Evidence (F1, build `b84c1085`) |
|---|---|---|
| `.webm` VP8+Vorbis | **FREE** | `F1-vid-vidctl-webm PASS frameCount=60 256x144` (control) · `F1-vid-vid-webm PASS frameCount=7` |
| `.mp4` H.264+AAC | **FREE** | `F1-vid-vid-mp4 PASS frameCount=7 128x72 len=0,47s` |
| `.mov` H.264+AAC | **FREE** | `F1-vid-vid-mov PASS frameCount=7 128x72 len=0,47s` |
| `.avi` MPEG-4 pt2+MP3 | **FREE** | `F1-vid-vid-avi PASS frameCount=8 128x72 len=0,53s` |
| `.mkv` H.264 | **IMPOSSIBLE** (as a container) | `F1-vid-vid-mkv FAIL (Can't play movie [...])` — refused at the container level, same message as the junk control |
| `.webm` **VP9** | **IMPOSSIBLE** (as a codec) | `F1-vid-vid9-webm FAIL (VideoPlayer cannot play url : ...)` — a DIFFERENT refusal from mkv's: the container opens, the codec is rejected. Unity's own webm path is VP8-only |

- **Native path, artifact-backed:** `UnityEngine.VideoModule.dll` ships, and PP itself plays every
  cutscene through exactly this API — `VideoPlaybackController.cs:148-150` sets
  `source = VideoSource.Url` and assigns a StreamingAssets path. Media Foundation does the decoding,
  which is why the audio subsystem being dead does not touch this column.
  **Crutch avoided: a video decoder, a transcoder, and any "convert your clip to webm first" step.**
- Cost of the two failures is zero to us: an author re-exports to a container that works.
- The url must be a **plain Windows path**. A `file:///…` URI with percent-escapes failed EVERY
  container, control included (first F1 run) — that is an instrument trap, not a format fact.

### TEXTURE — `Texture2D.LoadImage`

| Format | Verdict | Evidence |
|---|---|---|
| `.png` | **FREE** | `F1-tex-tex-png PASS decoded 64x64` (control) |
| `.jpg` / `.jpeg` | **FREE** | `F1-tex-tex-jpg PASS decoded 64x64` |
| `.bmp` (24-bit, uncompressed) | **PARSER** ~60 lines | `F1-tex-tex-bmp FAIL 8x8 (LoadImage said True)` |
| `.tga` (32-bit, uncompressed) | **PARSER** ~70 lines (+RLE) | `F1-tex-tex-tga FAIL 8x8 (LoadImage said True)` |
| `.dds` | **PARSER** ~80 lines uncompressed; BC1/3/7 decode is much bigger | `F1-tex-tex-dds FAIL 8x8 (LoadImage said True)` — probe was a hand-written 64x64 uncompressed B8G8R8A8 DDS, so the refusal cannot be blamed on block compression |
| `.psd` | **UNMEASURED** | no probe: ffmpeg has no PSD encoder and no `.psd` exists on this machine. Missing run: hand-write a raw-mode PSD and re-run F1. The bmp/tga/dds results make a PASS very unlikely |

> **TRAP, measured: `Texture2D.LoadImage` RETURNS TRUE FOR BYTES IT CANNOT DECODE.** Random junk
> returned `true` and left the texture at Unity's 8x8 error size. The return value is worthless as
> an oracle; the **size** is the identity. `ContentProject.ImportTexture` currently trusts the
> return value (`src\Project\ContentProject.cs:216`) — a corrupt .png therefore imports as an 8x8
> magenta-ish error texture instead of refusing. Own slice.

- **Native path, artifact-backed:** `UnityEngine.ImageConversionModule.dll` ships (13.0K) and is
  already referenced by this tool; `LoadImage` is a graphics-module call and is unaffected by the
  dead audio stack — measured working for two formats.
  **Crutch avoided: a PNG/JPEG decoder.** The three PARSER rows are the crutch we would have to
  accept for bmp/tga/dds, and each is small and optional — an author can export .png instead.

### AUDIO — target is PCM into a Wwise bank (`WwisePcm.ReadWav` → `BuildWem`)

> **AMENDED — the engine lever DOES exist, on the author's machine. See §2.1 below.** Everything in
> this subsection is still exactly true *as shipped*, and every row stays correct for anything that
> runs on a player's install. What it got wrong was treating `m_DisableAudio` as a property of the
> engine rather than as **one byte in a file this tool already knows how to patch**. Flipping that
> byte on the dev machine makes Unity decode `.ogg` and `.mp3` itself — measured, §2.1. The
> `IMPOSSIBLE` verdicts below therefore mean "impossible at runtime on a player's install", which is
> not the bar that matters for a **bake-time** import.

**As shipped, the engine lever does not exist here — settled in §0 from `globalgamemanagers`
(`m_DisableAudio: True`), not from a runtime symptom.** Phoenix Point drives all sound through
Wwise. The run agrees with the artifact:

```
F1-audio-subsystem outputSampleRate=0 speakerMode=Stereo driverCapabilities=Stereo
F1-audio-config    sampleRate=0 speakerMode=Raw dspBufferSize=0 realVoices=0 virtualVoices=0
F1-audio-reset     returned False; outputSampleRate is now 0
```
`AudioSettings.Reset` cannot open it (it first threw `Raw speaker mode is not supported`, then
returned `False` with every field filled in). `UnityWebRequestMultimedia.GetAudioClip` decodes
**inside** that subsystem, so it decodes nothing here — **including `.wav`**, which the tool reads
perfectly well with its own reader. That is why the F1-aud family is declared VOID rather than
counted as a control failure.

| Format | Verdict | Evidence |
|---|---|---|
| `.wav` PCM 8/16/24/32-bit int, 32/64-bit float, plain or `WAVE_FORMAT_EXTENSIBLE` | **FREE** (own reader, already shipping) | `WAV: ALL PASS, 22 check(s)`, `tests\ObjCodecTests\WavReadTests.cs`; reader at `src\Wwise\WwisePcm.cs:31-79` |
| `.wav` compressed payload (e.g. MP3-in-RIFF, tag `0x0055`) | refused **by name** | same test: refusal message must contain "compressed" |
| `.mp3` | **IMPOSSIBLE** without a hand-written decoder | `F1-aud-aud-mp3 FAIL (loadState=Unloaded samples=0 bytes=4641)` and `F1-pcm-aud-mp3 FAIL (Can't play movie)`. NLayer would be a third-party dependency — banned |
| `.ogg` Vorbis | **IMPOSSIBLE** without a hand-written decoder | `F1-aud-aud-ogg FAIL` / `F1-pcm-aud-ogg FAIL`. NVorbis is a dependency — banned. A Wwise Vorbis ENCODER was already "permanently ruled out" (PROVEN-FOUNDATIONS); a Vorbis DECODER is the same class of work |
| `.m4a` / AAC | **IMPOSSIBLE** without a hand-written decoder | `F1-aud-aud-m4a FAIL (loadState=Unloaded samples=0 bytes=5359)`; `F1-pcm-aud-m4a FAIL (Can't play movie)` — and an `.m4a` **is** an MP4, so this is the container being refused for having no video track, not AAC being unsupported |
| `.flac` | **IMPOSSIBLE** via the engine; **PARSER** ~600-800 lines if ever wanted | `F1-aud-aud-flac FAIL (no clip: GetContent returned null)` — refused before any I/O: the `AudioType` enum in this build (measured off `UnityEngine.CoreModule.dll`) holds `WAV/MPEG/OGGVORBIS/ACC/AIFF` and **no FLAC**, so `.flac` can only be offered `UNKNOWN`. FLAC is the one lossless codec small enough to hand-write |

**The one lever that DID answer — audio inside a playable video container:**
```
F1-pcm-vid-mp4 PASS decoded 9216 sample frames 1ch 44100Hz peak=0,130
```
`VideoAudioOutputMode.APIOnly` + `VideoPlayerExtensions.GetAudioSampleProvider(0)` +
`AudioSampleProvider.ConsumeSampleFrames(NativeArray<float>)` returns **real, non-silent PCM
decoded by the platform, with Unity's audio device shut**. So an author's `.mp4` already yields
both picture and sound samples. It does **not** rescue bare `.mp3`/`.ogg`/`.m4a`: VideoPlayer
refuses every audio-only file (measured, whole `F1-pcm-aud-*` family). Turning that into general
audio import would mean **writing an MP4 muxer** to wrap a foreign elementary stream next to a
dummy video track — a real slice, not a cheap win, and it still would not decode Vorbis.

- **AMENDED by §2.1:** the paragraph below is the correct answer for a RUNTIME decoder on a
  player's install, and the wrong answer for a bake-time importer. No Vorbis or MP3 decoder needs
  writing — the engine's own decoders do the work once the dev-machine flag is flipped.
- **No native path exists for audio at runtime, and that is now proven twice** (§0 artifact + F1 run), so a
  decoder of our own is the only remaining option for anything but `.wav`. Sizes, honestly:
  a **Vorbis** decoder is ~3-5 kLOC (floor/residue/codebook machinery) and an **MP3** decoder
  ~2-3 kLOC (Huffman, IMDCT, polyphase synthesis) — both are the kind of thing this project bans as
  a dependency and should equally ban as hand-written code. **FLAC** (~600-800 lines, integer-only)
  is the only one worth considering, and only if somebody actually asks.
  **Crutch NOT avoided anywhere here — which is the point:** every non-`.wav` audio row costs a
  decoder, so the honest answer to an author is "give me a .wav" (any bit depth, int or float),
  or "put the sound in your .mp4".

## 2.1 THE AUDIO ANSWER — YES, the flag flips, and the engine decodes `.ogg` and `.mp3`

**Verdict: YES.** `m_DisableAudio` is one byte in `globalgamemanagers`. Setting it to `0` on the
**author's machine** brings Unity's audio device up, and `UnityWebRequestMultimedia.GetAudioClip`
then decodes Vorbis and MP3 into real PCM using the engine's own decoders. **This deletes the entire
Vorbis (~3-5 kLOC) and MP3 (~2-3 kLOC) decoder slice.** Nothing about it ships.

**The byte.** `globalgamemanagers` object `AudioManager` (classId 11, pathId 4) is at absolute offset
**7128**, 48 bytes, read with the tool's own `AssetsTools.NET` + `lib\classdata.tpk` — the same
machinery `AssetIndex`/`BundleBaker` use. 2019.4.31f1 layout, measured:

```
+0  m_Volume 1.0   +4 Rolloff 1.0   +8 Doppler 1.0   +12 SpeakerMode 2   +16 m_SampleRate 0
+20 m_DSPBufferSize 1024   +24 m_VirtualVoiceCount 512   +28 m_RealVoiceCount 32
+32 m_SpatializerPlugin ""   +36 m_AmbisonicDecoderPlugin ""
+40 m_DisableAudio 0x01   +41 m_VirtualizeEffects 0x01   +42 pad   +44 m_RequestedDSPBufferSize 1024
```
So **`m_DisableAudio` = absolute file offset 7168**. Writing it changes no length, so nothing in the
serialized file shifts. `m_SampleRate: 0` turned out to be a non-issue — 0 means "ask the driver",
and the driver answered 48000. Tool: **`tools\audio-flag.ps1`** — DELETED 2026-08-23, gate A7; the
measurement below stands, the tool that performed it is gone (`status` / `on` / `off` / `restore`),
which validates the object by an 11-field anchor before writing rather than trusting offset 7168 as a
remembered constant — that anchor caught a wrong literal on its first run instead of corrupting a core
game file.

**The measurement.** Three launches, all `build=b84c1085` confirmed in-phase, all driven unattended by
`.\autogate.ps1 -Commands ct_fmt -NoDeploy`. Same DLL, same probe corpus, one byte different.

| | `m_DisableAudio=True` (baseline) | `=False` (patched) | `=True` again (falsification) |
|---|---|---|---|
| `F1-audio-subsystem` | `outputSampleRate=0 speakerMode=Stereo driverCapabilities=Stereo` | `outputSampleRate=48000 speakerMode=Stereo driverCapabilities=Mode7point1` | `outputSampleRate=0 ... driverCapabilities=Stereo` |
| `F1-audio-config` | `sampleRate=0 speakerMode=Raw dspBufferSize=0 realVoices=0` | `sampleRate=48000 speakerMode=Stereo dspBufferSize=1024 realVoices=32 virtualVoices=512` | `sampleRate=0 speakerMode=Raw ...` |
| `F1-audio-reset` | `returned False` | `returned True; outputSampleRate is now 44100` | `returned False` |
| `F1-aud VOID` line | present | **absent** | present |

```
                     BASELINE                                  PATCHED (m_DisableAudio=False)
F1-aud-aud-ogg       FAIL (loadState=Unloaded samples=0)   ->  PASS decoded 22050 samples 1ch 44100Hz peak=0,128
F1-aud-aud-mp3       FAIL (loadState=Unloaded samples=0)   ->  PASS decoded 27648 samples 1ch 44100Hz peak=0,119
F1-aud-aud-wav       FAIL (loadState=Unloaded samples=0)   ->  PASS decoded 22050 samples 1ch 44100Hz peak=0,125
F1-aud-aud24-wav     FAIL                                  ->  PASS decoded 22050 samples 1ch 44100Hz peak=0,125
F1-aud-aud32f-wav    FAIL                                  ->  PASS decoded 22050 samples 1ch 44100Hz peak=0,125
F1-aud-aud-junk      FAIL  [CONTROL: must NOT decode]      ->  FAIL  [CONTROL: must NOT decode]
F1-aud-aud-flac      FAIL (no clip: GetContent null)       ->  FAIL (no clip: GetContent null)
F1-aud-aud-m4a       FAIL (samples=0 bytes=5359)           ->  FAIL (samples=0 bytes=5359)
ct_fmt: controls ALL PASS (all three runs)
```

**Why this is a positive identity and not "it didn't throw".** The probes are 0.5 s mono 44100 Hz
(ffprobe: `vorbis,44100,1,0.500000` · `mp3,44100,1,0.500000` · `pcm_s16le,44100,1,0.500000`), so the
expected sample count is **22050 exactly** — which is what `.ogg` and all three `.wav` returned. The
oracle is `AudioClip.GetData` + a peak, so a correctly-sized buffer of zeros would have failed:
`peak≈0,12` is the probe's own tone. MP3's **27648** is `24 × 1152` — whole MPEG frames including
encoder delay/padding, i.e. the expected answer for MP3, not a wrong one.

**Three negative controls held while the positives flipped**, which is what rules out "the run got
looser":
- `aud.junk` (4096 random bytes) refused in every run — the flag did not make the decoder credulous;
- `.flac` refused in every run — this build's `AudioType` enum has no FLAC, a container fact the
  device state cannot touch;
- `.m4a` refused in every run;
- and the **falsification** run put the byte back and every row returned to FAIL with the `VOID` line
  restored. One byte is the entire cause.

**Wwise is unharmed.** `.\autogate.ps1 -Commands ct_audio -NoDeploy` with Unity audio ON:
`ct_audio: ALL PASS` — bank generated, packaged, read back byte-identical, loaded
(`bankId=2358375752`), both storage modes reaching the engine (`A1c` MEMORY, `A2c` FILE), pre-load
controls correctly refusing, `A4` validator green on 7697 media IDs. The game launches and plays
normally with the flag flipped; the two audio stacks coexist. So even the dev-machine cost is zero —
and it would have been acceptable anyway, since this never reaches a player.

**Safety, proven not asserted.** `globalgamemanagers` sha256 **before** the run
`39058F675CFAC8F2EEEECEF08DDA15339A6387CC47042E9E010835E15F213A20`; patched
`85DE62E653D568506D48F1B66B84F41D0996A07FCF4C13AD4E16E7A1565C75D3`; **after `restore`
`39058F67...A20`, byte-identical to the pristine backup**, and the falsification launch then proved
the restored file is functionally pristine too, not merely hash-equal. The game file is as shipped.

> **SUPERSEDED 2026-08-23 — gate A7. Everything measured above is still true about the BYTE; what
> follows is no longer the workflow, and nothing in the tool flips that byte any more.** Reaching
> the engine's decoders means editing a file in the player's install, which the author mandate and
> the "ContentTool never modifies original game files" rule both forbid. The tool decodes `.ogg`
> and `.mp3` itself now, `-UnityAudio` and `tools\audio-flag.ps1` are deleted, and the in-house
> decode reproduces Unity's own numbers on the same probes (see the A7 row in
> `PROVEN-FOUNDATIONS.md`). The steps below are kept as the record of what was measured, not as
> instructions.

**What the author-side workflow was — WIRED AND MEASURED 2026-08-12, superseded by A7:**
1. author drops `music.ogg` / `sfx.mp3` into `Content\Audio\` like any other source file;
2. ~~`.\autogate.ps1 -Commands ct_project -UnityAudio`~~ — the switch flipped `m_DisableAudio` via
   `tools\audio-flag.ps1 on`, launched, baked, and restored in a `finally`, so a bake that threw,
   refused or was interrupted still left `globalgamemanagers` byte-identical (hash-verified, proven
   on a deliberately failing bake). **Now: nothing. `ct_project` bakes compressed audio in an
   ordinary launch;**
3. `ContentProject.ImportAudio` routed `.wav` through `WwisePcm.ReadWav` and `.ogg`/`.mp3` through
   `EngineAudio` (the engine's own `UnityWebRequestMultimedia`+`GetData`), and both into the SAME
   `WwisePcm.BuildWem`. **Now: `WwisePcm.ReadAudio` takes all three, still into that same
   `BuildWem` — still one pipeline, and now one decoder too;**
4. **the shipped mod contains a bank and nothing else** — no decoder, no dependency, no runtime code.
   (Unchanged: the merged NVorbis/NLayer are BAKE-time only and nothing of them ships to a player.)
   SHIPPING = ZERO RUNTIME CODE is untouched.

**The import arm (A6) is a positive identity, not a presence.** The oracle is the SOURCE's own
header, parsed offline by `src\Import\SourceAudio.cs` (Vorbis identification header + last page
granule; MPEG-1 Layer III frame walk, Xing frame excluded) and proven in
`tests\ObjCodecTests\SourceAudioTests.cs`. Measured, build `b70aa669`:
`A6 PASS chime.ogg decoded 1ch 44100Hz 22050 frames peak=0,128 -> 44164 B .wem vs the source's own
header: 1ch 44100Hz 22050 frames` and `A6 PASS tone.mp3 decoded 1ch 44100Hz 27648 frames peak=0,119`
against a declared `24192`. **That gap is the one new fact:** the engine hands back whole MPEG frames
plus its own decoder delay/padding (24 × 1152 out for 21 × 1152 on disk), so `.mp3` is asserted with a
4-frame tolerance and `.ogg` exactly. `.wav` says **VOID**, not PASS — `ReadWav` is its parser, so the
arm would be comparing a read against itself. A buffer of silence fails every row on the peak.

**The accepted set is `.wav`, `.ogg`, `.mp3` — a whitelist, and it is final.** Everything else in
`Content\Audio\` is refused by name with the cause and the fix ("export to .ogg or .wav"), because
those three are what people actually have and all three cost **zero added bytes**: the engine decodes
them. `.flac` was evaluated and **dropped by decision, not by inability** (2026-08-12) — nobody ships
a mod sound as FLAC, so neither a hand-written decoder nor a vendored library earns its size. A
whitelist rather than a blacklist so `.opus`/`.wma`/`.aac` cannot arrive through a forgotten line.
Size, measured back to back on the same tree: merged `ContentTool.dll` **1012 KB → 1025,5 KB**, and
**none of that +13,5 KB is a codec** — it is the import path, the A6 oracle and the refusal texts.
The probe `.ogg`/`.mp3` are deliberately NOT embedded (that would have been +8,8 KB for one gate);
the sample copies them from `<mod>\FormatProbes\` and says so when they are absent.

**Ceilings, honestly.** `.flac` and `.m4a` stay refused — the flag does not add codecs the build has
no enum for. Decode is a live game session, so bake-time audio import costs a launch (the same price
every in-game gate already pays). And it edits a core game file: the flip must always be paired with
its restore, which is why the script owns both halves.

**What Spike05 actually tried, and why it did not cover this.** ResourceReplacer's
`research\wwise-spikes\Spike05_UnityAudioReEnable.cs:27-67` reads `AudioSettings.outputSampleRate`,
`GetDSPBufferSize`, `GetConfiguration`, then adds an `AudioSource` and calls `Play()` — **entirely at
runtime, from mod code, inside an already-started process.** It never touches `globalgamemanagers`;
the word does not appear in the file, and its own header (`:2-3`) states the flag as the given
premise rather than as the thing under test. The engine reads that flag at STARTUP, before any mod
assembly is loaded, so a runtime probe **cannot** answer this question — it can only re-confirm the
consequence. `README.md:22` tabulating it "Dead (m_DisableAudio=true)" is the correct summary of what
it measured and was never a finding about patching the file.

### MESH

Our ceiling today: OBJ carries no skin, so a replaced skinned mesh gets **one full-weight influence
per vertex** (rigid, no blending — commit `0098342`). A format that carries real weights lifts it.

| Format | Verdict | Skin weights + bindposes? | Evidence |
|---|---|---|---|
| `.obj` | **works today** | **no** — derived, 1 influence/vertex | `src\Import\ObjCodec.cs` (277 lines), `src\Bake\SkinFields.cs:275` |
| `.glb` (glTF 2.0 **binary**) | **PARSER — ALREADY WRITTEN**, ~1,240 lines, port = copy 2 files | **YES** | `E:\DEV\PhoenixPoint\ResourceReplacer\pp-native\src\GlbReader.cs`: `POSITION/NORMAL/TANGENT/TEXCOORD_0/1/JOINTS_0/WEIGHTS_0` (`:248-253`), refuses >4 influences (`:243`), named joints (`:410-446`), `inverseBindMatrices` (`:447-451`), converts to Unity space, hostile-input range-checked throughout. Corpus of 18 real rigged `.glb` at `ResourceReplacer\example-content\Meshes\select\` |
| `.gltf` (JSON + external `.bin`) | **PARSER**, small delta on the above | yes, same data | `GlbReader.Read` requires the `glTF` magic and rejects anything else (`:59-60`); the JSON reader and every accessor path are already there |
| `.fbx` | **IMPOSSIBLE** in practice — no engine path at runtime (FBX import is editor-only), and a binary FBX 7.x reader with skinning is thousands of lines | n/a | no shipped decoder; nothing in `Managed\` reads FBX |
| `.dae` (COLLADA) | **PARSER**, medium (~400-800 lines) — `System.Xml` is in net472, no dependency | yes (`<skin>`/`<bind_shape_matrix>`) | XML only; nothing measured, and there is no reason to build it while `.glb` exists |

**Recommendation:** `.glb` is the whole mesh answer. It is the only interchange format on this list
that is already parsed, already carries weights and bind poses, and is what Blender exports by
default. `.fbx` and `.dae` should be answered with "export .glb", not with a parser.

## 3. What landed in this commit (cheap wins only)

- **`.jpg` / `.jpeg` textures** — `ContentProject.Load` globs them next to `.png`;
  `ImportTexture` was already decoder-agnostic (it calls `LoadImage` and reads RGBA32 back).
- **`.mp4` / `.mov` videos** — globbed next to `.webm`, and `VideoCatalog` now writes the author's
  **own extension** into `StreamingPath` instead of forcing `.webm` (the decoder is chosen by
  container).
- **Duplicate-stem refusal** — a replacement names a file STEM, so `swatch.png` next to
  `swatch.jpg` would have let the first file found win silently. `ContentProject.Sources` now
  refuses, naming both files.
- Regression check: `ct_project` still `ALL PASS` (P1/P1-ctl/P3/P3-ctl/P4×2/P4-ctl×2/P5×2/TEX/BANK)
  on build `e4e88ad8`.

## 4. UNMEASURED — the runs that are missing

1. **`.psd` → `LoadImage`.** No probe can be made here (no PSD encoder on this machine). Run: write
   a raw-mode PSD by hand into `FormatProbes\` and re-run `ct_fmt`.
2. **`.jpg` end to end through P1.** F1 proves the decoder; the bake path is unrun with a JPEG.
   Run: declare a `.jpg` texture in the sample and add a P1 arm — note JPEG is **lossy**, so P1's
   exact-pixel oracle needs a tolerance arm, not a byte compare.
3. **`.mp4` end to end through V1.** F1 proves the decoder; the catalog repoint is unrun with an
   MP4. Run: a project declaring an `.mp4`, `ct_video live <project>`, then `ct_video resolve <key>`.
4. **`.glb` inside ContentTool.** Nothing has been ported yet. Run: bring `GlbReader.cs` +
   `GlbCodec.cs` over, feed one of the 18 example `.glb`, and gate the weights/bindposes reaching
   `SkinFields` (this is the slice that lifts the one-influence ceiling).
5. **The MP4-mux route for foreign audio.** Whether a hand-written MP4 muxer could carry an MP3/AAC
   elementary stream past `VideoPlayer` is untested and is a slice, not a spike.
