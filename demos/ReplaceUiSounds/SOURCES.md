# Sources and licences

Nothing in this folder was downloaded. All three clips are **generated** by
`..\tools\make_demo_audio.ps1`, which makes them ours by construction and **CC0 1.0** (public domain
dedication) — redistributable with this repository without restriction.

| asset | made by | what it is |
|---|---|---|
| `Content\Audio\Replace\sting_plus.mp3` | `..\tools\make_demo_audio.ps1` | 0.300 s, mono 44100 Hz, 64 kbps, 2 969 B — one bright ping (G6 with a quiet fifth above), ffmpeg `aevalsrc` under an exponential decay |
| `Content\Audio\Replace\sting_confirm.mp3` | the same script | 0.400 s, mono 44100 Hz, 64 kbps, 3 805 B — two sines rising, E5 → E6 |
| `Content\Audio\Replace\sting_cancel.mp3` | the same script | 0.550 s, mono 44100 Hz, 64 kbps, 5 059 B — a falling reedy buzz, A3 → D3, each note a fundamental plus its third harmonic |

A sine wave under an envelope is not a recording: there is no performance, no sample and no library
in any of the three, so there is no licence to chase.

**The file name is free here, unlike its twin.** A replacement is bound by the `{ "media", "file" }`
row in `ppcontent.json`, so these carry descriptive names. The lengths are not free of intent: they
are deliberately unlike the media they replace (1200 / 3533 / 2231 ms), because `ct_sound probe
<mediaId>` reads `fDuration` back and that difference is the proof the engine is serving ours.

## The clips these replaced

Until 2026-08-28 this demo shipped `tblehit04.mp3`, `band_stretch_release_slap.mp3` and
`zvuk_-kloun-gudok_-clown-hor.mp3`, three third-party sound-effect files with **no licence anyone
could name**. That is the whole reason they are gone: this repository is public, and a file we
cannot name a licence for cannot be in it.

## Your own sounds

Drop a `.wav`, `.ogg` or `.mp3` into `Content\Audio\Replace\`, point its `{ "media", "file" }` row at
it, re-run `ct_sound bake ReplaceUiSounds`, and replace this file with **your** sound's licence.

## Phoenix Point's own assets

Nothing of Snapshot Games' is redistributed here, and nothing of theirs is modified. The three
shipped `.wem` stay exactly where they are on disk, untouched; ContentTool loads this mod's own
`Dist\Sounds\*.bnk` into memory at init and the game's own Wwise plays those instead.
