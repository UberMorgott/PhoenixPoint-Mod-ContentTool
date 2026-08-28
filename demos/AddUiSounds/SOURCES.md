# Sources and licences

Nothing in this folder was downloaded. Both clips are **generated** by
`..\tools\make_demo_audio.ps1`, which makes them ours by construction and **CC0 1.0** (public domain
dedication) — redistributable with this repository without restriction.

| asset | made by | what it is |
|---|---|---|
| `Content\Audio\blip_rise.mp3` | `..\tools\make_demo_audio.ps1` | 0.350 s, mono 44100 Hz, 64 kbps, 3 387 B — two sines rising (A5 → E6) under exponential decays, ffmpeg `aevalsrc` |
| `Content\Audio\blip_fall.mp3` | the same script | 0.450 s, mono 44100 Hz, 64 kbps, 4 223 B — three sines falling (E6 → B5 → E5), same envelope |

A sine wave under an envelope is not a recording: there is no performance, no sample and no library
in either file, so there is no licence to chase.

**The stem is load-bearing.** The bake names each event `fnv1_lower32("morgott_demo_adduisounds_" +
stem)`, and `src\AddUiSoundsMain.cs` hashes the same strings from its `Clips` array — so renaming a
file means editing that array, and the two sides agree by construction rather than by a table
someone keeps in step.

## The clips these replaced

Until 2026-08-28 this demo shipped `zvuky-2.mp3` and `7a5fa5d4f12cb3f.mp3`, two third-party UI blips
with **no licence anyone could name**. That is the whole reason they are gone: this repository is
public, and a file we cannot name a licence for cannot be in it. (They in turn had replaced two 4 s
tunes, dropped for a different reason — a hotkey gag is not a song.)

## Your own sounds

Drop a `.wav`, `.ogg` or `.mp3` into `Content\Audio\`, add its stem to `Clips` in
`src\AddUiSoundsMain.cs`, re-run `ct_project AddUiSounds`, and replace this file with **your**
sound's licence.

## Phoenix Point's own assets

Nothing of Snapshot Games' is redistributed here, and nothing of theirs is modified — this mod
replaces no shipped media at all. It ADDS two events no shipped media owns, in a bank inside its own
bundle, loaded into memory.
