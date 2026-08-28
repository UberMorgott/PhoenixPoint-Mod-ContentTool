# `lib\` — what each file is, and under what licence it ships

Every file in this folder is either ours or third-party with a named licence. Nothing here is a
Phoenix Point asset: the project never redistributes game data, and a public repository is exactly
the condition that rule exists for.

## Third-party libraries

Each ships its licence text beside it, and each is redistributable under it.

| File | What | Licence |
|---|---|---|
| `AssetsTools.NET.dll` | reads and writes Unity serialized files and bundles | `AssetsTools.NET-LICENSE.txt` |
| `NLayer.dll` | MP3 decoder | `NLayer-LICENSE.txt` |
| `NVorbis.dll` | Ogg Vorbis decoder | `NVorbis-LICENSE.txt` |
| `System.ValueTuple.dll` | Microsoft's `ValueTuple` backport for `net472` | MIT, from the `System.ValueTuple` NuGet package |

`classdata.tpk` is AssetsTools.NET's own Unity type database, extracted from UABEA, and is covered by
that project's licence. It is a description of Unity's serialization layout, not game content.

## Our own data

| File | What |
|---|---|
| `ppids.bin` | the packed Wwise id table this project generates; ours |

## Test fixtures — models

The three `.glb` models are CC0 downloads with their provenance, SHA-1 and preparation steps recorded
in a file of their own. Read those before touching them:

- `u8_probe-SOURCE.md` and `u10_probe-SOURCE.md` — Quaternius' "Spider", **CC0 1.0**. `u10_probe.glb`
  is the untouched download; `u8_probe.glb` is a dequantized copy kept as an independent oracle.
- `u12_probe-SOURCE.md`, `u12_norm-SOURCE.md`, `u12_uv-SOURCE.md` — the normal/UV variants and the
  `_plain` files derived from them.

## Test fixtures — audio and video, generated here

| File | What | Origin |
|---|---|---|
| `a1_probe.mp3`, `a1_probe.ogg` | short audio probes, read off disk by the offline decoder tests | generated for this repository; no sampled material |
| `v1_probe.webm` | 256×144, 2 s at 30 fps, 6 265 B — the video probe, embedded in the assembly | generated for this repository |

They are deliberately tiny: they exist to prove a decoder path, not to be listened to or watched.
