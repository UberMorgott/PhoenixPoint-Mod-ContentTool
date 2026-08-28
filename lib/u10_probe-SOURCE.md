# u10_probe.glb — source and licence

Gate **U10**'s real-world COMPRESSED `.glb`: the very file `lib\u8_probe.glb` was made from, before
anything was run over it. Read straight off disk by `tests\ObjCodecTests\Compressed.cs`; **not**
embedded in the assembly and **not** copied into the mod folder (unlike `u8_probe.glb`, which the
sample project needs at runtime), so it costs the shipped mod nothing.

- **Model:** "Spider", by **Quaternius** — https://poly.pizza/m/yRYJiAJyiM
- **Licence:** CC0 1.0 Universal (public domain dedication) —
  https://creativecommons.org/publicdomain/zero/1.0/ . Redistribution is unrestricted and
  attribution is not required; it is recorded here anyway. Same model, same licence, same
  first-party source as `u8_probe.glb` — see `lib\u8_probe-SOURCE.md`.
- **Obtained** 2026-08-23 from `public/models/creatures/spider.glb` of
  https://github.com/levy-street/world-of-claudecraft , whose `CREDITS.md` lists it as
  "Animated creatures — Quaternius — CC0 1.0". **Unmodified**, 130 436 B,
  SHA-1 `973ee4d7c16378c249f3f8c69b028bc9970372f5`.

## Why this file and not another

It is the ORIGINAL of `u8_probe.glb` (349 468 B, SHA-1 `e5d2b6d1…`, produced from it by
`npx @gltf-transform/cli dequantize`), so the pair is the same model stated twice — once compressed,
once not — by two implementations that share no line of code. That makes `u8_probe.glb` an
independent oracle rather than a second opinion from our own writer, which is the whole basis of
gate U10.

## What it exercises, measured from its own JSON

- `extensionsRequired`: **`EXT_meshopt_compression`** and **`KHR_mesh_quantization`**.
- **13 bufferViews, every one of them compressed**, against a 161 496 B fallback buffer that holds
  nothing (`"extensions": { "EXT_meshopt_compression": { "fallback": true } }`) while the real bytes
  live in the 68 456 B GLB binary chunk.
- Modes: **`ATTRIBUTES` on 12** views, **`TRIANGLES` on 1** (both index buffers, 8 136 indices).
  `INDICES` (mode 2) appears in neither probe and is covered by a hand-built stream in the gate.
- Filters: **all four** — `NONE` (7 views), `OCTAHEDRAL` (2, the normals), `QUATERNION` (1, every
  rotation curve of all five clips), `EXPONENTIAL` (1, every translation curve).
- Quantized attributes: `POSITION` VEC3 SHORT normalized, `NORMAL` VEC3 BYTE normalized,
  `WEIGHTS_0` VEC4 UNSIGNED_BYTE normalized, `JOINTS_0` VEC4 UNSIGNED_BYTE, rotation sampler outputs
  VEC4 SHORT normalized. Its mesh is SKINNED, so the dequantization transform rides in the skin's
  `inverseBindMatrices` and the node's own scale of 100 is untouched — the exact arrangement
  KHR_mesh_quantization's "Decoding Quantized Data" section describes for a skinned mesh.
- No `TEXCOORD` at all, so the one quantized case this mod still refuses (UVs that need
  `KHR_texture_transform` to scale back) is not exercised here and is asserted on hand-built bytes.
