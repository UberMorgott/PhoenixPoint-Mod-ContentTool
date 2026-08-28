# u12_norm.glb / u12_norm_plain.glb - where they come from

Gate **U12**'s third fixture pair, and the only one that measures
MESH_PREDICTION_GEOMETRIC_NORMAL - the predictor 261 of the 263 real Draco primitives sampled for
this gate use, and the one neither other fixture reaches (the Avocado's normals come back through
PREDICTION_DIFFERENCE instead).

## The model

**"BarramundiFish"**, from the Khronos Group's glTF-Sample-Assets repository, `glTF-Draco` variant.

- source: <https://github.com/KhronosGroup/glTF-Sample-Assets/tree/main/Models/BarramundiFish/glTF-Draco>
  - `BarramundiFish.gltf`, 3 266 B, SHA-1 `3f021432d63839c3ad1ffcd87377375a38384449`
  - `BarramundiFish.bin`, 43 300 B, SHA-1 `a5629e177ff1d56388bdb0683be2c3e595a85800`
- LICENCE: **CC0 1.0 Universal**. Confirmed FIRST-HAND at the model's own README in that repository,
  "Legal" section: "&copy; 2017, Public. Creative Commons Zero v1.0 Universal - Microsoft for
  Everything". Redistribution is unrestricted.

## u12_norm.glb - the fixture (44 432 B, SHA-1 `64caeafce0d4c8e72b977b9dfe592b00429735af`)

The pair above REPACKED into one .glb exactly as `u12_uv.glb` was: the JSON keeps every mesh, node,
scene, accessor and bufferView, minus `images`/`textures`/`samplers`/`materials` and the
primitive's `material` reference, and the .bin becomes the BIN chunk byte for byte. **The
compressed geometry bytes are Khronos' own, unmodified.**

## u12_norm_plain.glb - the oracle (129 324 B, SHA-1 `9aa8dcc016a0e0db4400960e78c0ff459251069c`)

`u12_norm.glb` decompressed by glTF-Transform 4.4.2 (`npx @gltf-transform/cli copy`), i.e. by
Google's own `draco3d` WebAssembly decoder. Same CC0 model, same licence.

The gate compares our decode against it value for value (worst normal 2e-7 over 2 188 vertices,
11 592 triangle indices exact) and asserts two things about the FIXTURE so the arm cannot pass
while measuring nothing: that its normals really are coded with scheme 6
(MESH_PREDICTION_GEOMETRIC_NORMAL), read back out of the decoder, and that 947 of them lie in the
folded half of the octahedron.
