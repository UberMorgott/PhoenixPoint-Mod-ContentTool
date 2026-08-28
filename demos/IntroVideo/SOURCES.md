# Sources and licences

Nothing in this folder was downloaded. All three of the mod's assets are **generated** by the
scripts beside them, which makes them ours by construction and **CC0 1.0** (public domain
dedication) — redistributable with this repository without restriction.

| asset | made by | what it is |
|---|---|---|
| `Content\Videos\campaign_intro.webm` | `..\tools\make_placeholders.ps1` | a 6 s title card on a flat background — ffmpeg `color` + `drawtext`, VP8/Vorbis |
| `Content\Audio\Replace\intro_theme.mp3` | `tools\make_audio.ps1` | 6 s, three plucked notes (A3, C#4, E4) — ffmpeg `aevalsrc`, a sine under an exponential envelope |
| `Content\Subtitles\campaign_intro.srt` | typed by hand | three cues, one per note |

The audio is a sine wave with an envelope, not a recording: there is no performance, no sample and
no library in it, so there is no licence to chase. Replace any of the three with your own file of
the same name and re-run the step in the README — the plumbing does not care which file it is.

## Phoenix Point's own assets

Nothing of Snapshot Games' is redistributed here, and nothing of theirs is modified. The shipped
clip (`PP_Intro.webm`) and the shipped audio media (`908611677.wem`) stay exactly where they are on
disk, untouched; ContentTool serves ours instead, in memory, for the length of the run.
