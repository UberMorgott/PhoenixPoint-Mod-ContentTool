# u12_probe.glb - where it comes from

Gate **U12**'s second fixture: the suite's own CC0 spider, Draco-compressed.

- 106 872 B, SHA-1 `5e30a6166987a4ce75f3a8cc105217df3c5debc7`.
- It is `lib\u8_probe.glb` (349 468 B, SHA-1 `e5d2b6d147da5cd4e79549cf5430766aa523b529`) run through
  `npx @gltf-transform/cli@4.4.2 draco`, whose encoder is Google's own `draco3d` WebAssembly build.
  Default settings: 14-bit positions, 10-bit normals, EDGEBREAKER connectivity, bitstream 2.2.
- LICENCE: unchanged from what it was made from - Quaternius' **"Spider", CC0 1.0**, whose
  first-party source is already recorded in `lib\u8_probe-SOURCE.md`. A CC0 work stays CC0 after a
  format change, so redistribution is unrestricted.

## Why this one exists as well as u12_uv.glb

`u12_uv.glb` is a real third-party Draco file, but it is a static prop. This one carries what a
Phoenix Point creature carries and that file does not:

- JOINTS_0 / WEIGHTS_0 - the INTEGER attribute decoder, which is not the quantized path the
  positions and texture coordinates take;
- a 39-bone skin and five animation clips, which Draco does not touch at all - so the fixture also
  proves the REST of a .glb still reads after the geometry takes the compressed detour;
- TWO primitives inside one compressed block.

## The oracle

`lib\u8_probe.glb` itself - the file this one was compressed FROM, already in the repo. Draco
quantizes, so that comparison is bounded rather than exact: the gate derives one quantization step
from the oracle's own bounding box (14 bits) and asserts every vertex lands inside it, then compares
the triangles as a multiset over position classes, which is what proves the connectivity.
