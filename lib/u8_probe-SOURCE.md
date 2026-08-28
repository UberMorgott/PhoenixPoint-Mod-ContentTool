# u8_probe.glb — source and licence

Gate **U8**'s real-world .glb: a rigged, animated creature produced by an art tool, not by this
repo's own writer. Read straight off disk by `tests\ObjCodecTests\ClipImport.cs`; **not** embedded
in the assembly (same arrangement as `a1_probe.mp3`/`a1_probe.ogg`), so it costs the shipped mod
nothing.

- **Model:** "Spider", by **Quaternius** — https://poly.pizza/m/yRYJiAJyiM
- **Licence:** CC0 1.0 Universal (public domain dedication) —
  https://creativecommons.org/publicdomain/zero/1.0/ . Redistribution is unrestricted and
  attribution is not required; it is recorded here anyway.
- **Obtained** 2026-08-23 from `public/models/creatures/spider.glb` of
  https://github.com/levy-street/world-of-claudecraft , whose `CREDITS.md` lists it as
  "Animated creatures — Quaternius — CC0 1.0".
- **Prepared**, once, offline, with `npx @gltf-transform/cli dequantize Spider.glb u8_probe.glb` —
  130 436 B → **349 468 B**, `extensionsRequired` and `extensionsUsed` both empty, 0 of 5 bufferViews
  compressed. SHA-1 `e5d2b6d147da5cd4e79549cf5430766aa523b529`.
  **That step is no longer NEEDED**: `GlbReader` reads `EXT_meshopt_compression` and
  `KHR_mesh_quantization` itself as of gate **U10**, and the untouched download is committed beside
  this file as `lib\u10_probe.glb` (`lib\u10_probe-SOURCE.md`). The dequantized copy is KEPT because
  U10 uses it as an INDEPENDENT oracle — gltf-transform's decoder and ours share no line of code, so
  the two files agreeing is cross-validation rather than a round trip through our own writer — and
  because U8/U9 already cite its exact numbers.
- **Measured contents:** 1 mesh / 2 primitives, 3925+ vertices, 39 joints, 5 clips —
  `Spider_Attack`, `Spider_Death`, `Spider_Idle`, `Spider_Jump`, `Spider_Walk`. Every key time in
  every clip is a multiple of 1/24 s, with the unchanged keys dropped, which is why
  `GlbReader.Rate` looks for the coarsest rate the whole clip lands on rather than asking one
  channel for its spacing.

`CrabEnemy.glb` from the same download was NOT used: its licence could not be confirmed at any
first-party source, so it is not redistributable and is not in this repo.
