# Sources and licences

Nothing in this folder was downloaded. The mod's only asset is **generated** by
`..\tools\make_demo_audio.ps1`, which makes it ours by construction and **CC0 1.0** (public domain
dedication) — redistributable with this repository without restriction.

| asset | made by | what it is |
|---|---|---|
| `Content\Audio\Replace\208540756.mp3` | `..\tools\make_demo_audio.ps1` | 12.000 s, mono 44100 Hz, 96 kbps, 144 867 B — an A-minor pentatonic arpeggio over a two-note drone: sixteen sines under exponential envelopes, ffmpeg `aevalsrc` |
| `Content\Audio\Replace\423563089.mp3` | the same script, `Copy-Item` | byte-identical to the above. The two files are two *targets* (`MainMenuMusic` and `MainMenuYOE`), not two tracks — which edition the game asks for is decided by entitlement, so both are replaced |

There is no performance, no sample and no library in either file — a sine wave under an envelope is
not anyone's recording, so there is no licence to chase. Level is aimed rather than left to chance:
**−15.7 LUFS, peak −4.2 dBFS**, measured with `ffmpeg -af ebur128` and `volumedetect`, because
−15 LUFS is where game music is mixed.

## The track this replaced

Until 2026-08-28 this demo shipped two 3.88 MB copies of a **copyrighted music remix** supplied by
the repository owner (a "Gachi Remix" of a commercial track), together with the two 23.44 MB banks
baked from them — 49 MB, and the largest files in the repository. It carried **no licence anyone
could name**, which is the whole reason it is gone: this repository is public, and a file we cannot
name a licence for cannot be in it. The generated loop demonstrates exactly the same thing for
1/30th of the bytes.

## Your own music

Drop your file over `Content\Audio\Replace\<mediaId>.mp3` (`.wav` and `.ogg` work too — ContentTool
decodes all three itself), re-run `ct_sound bake MenuMusic`, and replace this file with **your**
track's licence. Length is free: the bake writes whatever it is handed. Remember that the bank is
PCM, so its size is `frames × channels × 2` — a 128 s stereo track is a 24 MB bank per edition.

## Phoenix Point's own assets

Nothing of Snapshot Games' is redistributed here, and nothing of theirs is modified. The shipped
media stay exactly where they are on disk, untouched; ContentTool loads this mod's own banks into
memory at init and the game's own Wwise plays them.
