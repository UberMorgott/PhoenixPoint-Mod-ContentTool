# u12_uv.glb / u12_uv_plain.glb - where they come from

Gate **U12**'s first fixture pair: a REAL third-party Draco stream and the same stream decoded by
an independent decoder.

## The model

**"Avocado"**, from the Khronos Group's own glTF-Sample-Assets repository, `glTF-Draco` variant.

- source: <https://github.com/KhronosGroup/glTF-Sample-Assets/tree/main/Models/Avocado/glTF-Draco>
  - `Avocado.gltf`, 3 083 B, SHA-1 `9685b036df1fe6f6164bff7e129fb47e3374b15c`
  - `Avocado.bin`, 8 724 B, SHA-1 `be9489869b9f4626c32a3db1ba248edb2ff1421d`
- LICENCE: **CC0 1.0 Universal** (public domain dedication). Confirmed FIRST-HAND at the model's own
  README in that repository, "Legal" section: "&copy; 2017, Public. Creative Commons Zero v1.0
  Universal - Microsoft for Everything". Redistribution is therefore unrestricted.
- The Draco block inside it was written by Khronos' own pipeline, not by us: it is bitstream 2.2,
  EDGEBREAKER connectivity, and carries POSITION, NORMAL, TEXCOORD_0 and TANGENT.

## u12_uv.glb - the fixture (9 828 B, SHA-1 `7affe0150b6f20a90babce04e95cd84e4e062ee3`)

The pair above REPACKED into a single .glb, because this mod reads .glb and the download is a
.gltf plus a side-car .bin. The repack is a container change only:

- the JSON keeps every mesh, node, scene, accessor and bufferView unchanged, minus `images`,
  `textures`, `samplers`, `materials` and the primitive's `material` reference (this mod paints
  from `Meshes\materials\*.mat.json` and never reads a glTF material, and dropping them keeps the
  fixture from carrying a 2 048 x 2 048 texture it would never use);
- `buffers[0].uri` is removed and `Avocado.bin` becomes the .glb's BIN chunk, byte for byte.

So **the compressed geometry bytes are Khronos' own, unmodified** - only the wrapper is ours.

## u12_uv_plain.glb - the oracle (24 668 B, SHA-1 `9efed7bda7c06734d571721fef78e43e49489bd6`)

`u12_uv.glb` DECOMPRESSED by glTF-Transform 4.4.2 (`npx @gltf-transform/cli copy`), whose Draco
decoder is Google's own `draco3d` WebAssembly build - an implementation sharing no line of code
with `src\Import\Draco.cs`. Same CC0 model, so same licence.

The gate compares our decode of `u12_uv.glb` against this file value for value: both hold the same
dequantized numbers, so the comparison is EXACT rather than tolerant.
