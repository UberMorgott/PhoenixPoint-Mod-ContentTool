# Content\Models\cyborg_spider.glb — source and licence

This demo redistributes ONE file that is not ours. Its provenance travels WITH it, because people
clone this repo.

**There is no texture file, and that is the point.** The creature is textured, and the colour map
comes out of the `.glb` itself: the bake decodes the model's own embedded `images[0]` - the image
material `Spider_M` names as its `baseColorTexture` - and binds it to `_MainTex`. Measured, with
`Content\Textures\` deleted:

```
project 'morgott.demo.customcreature': 0 texture(s), ... 1 model(s)
material 'Spider_M' -> cyborg_spider tex=_MainTex from the .glb itself, 512x512 (no file needed)
```

A hand-unpacked `Content\Textures\cyborg_spider.png` was tried first and DELETED once the embedded
path was proven to produce the same 512x512 image: one file instead of two, and a modder who drops
in a textured `.glb` gets a textured creature with nothing else to do. The author-file route still
exists and still WINS when present (`ProjectBake.cs`), which is how you repaint a downloaded model
without re-exporting it - name the .png after the model and put it in `Content\Textures\`.

- **Model:** "Cyborg Spider", by **SpiderBight [ArachnoBoy]** —
  https://sketchfab.com/3d-models/43a411dc1ba44fee9cfad0ffa610234d
- **Licence:** **CC BY 4.0** (Creative Commons Attribution) —
  https://creativecommons.org/licenses/by/4.0/ . Redistribution inside a mod is allowed and
  **attribution is REQUIRED**, unlike the CC0 model this replaced: keep this file, and keep the
  credit in the mod's own description. Do not strip the author's name.
- **Obtained** 2026-08-25 by the user from Sketchfab's own download (Sketchfab serves downloads only
  to a signed-in account, so it cannot be re-fetched by a script — treat this committed copy as the
  archive). Taken from the **"Original format"** download, `source\spider_animated_character.glb`
  inside `cyborg-spider.zip`.
- **UNMODIFIED.** 1 481 244 B, SHA-1 `d4f3d0d58809498b3a6b48451aaeedeb11f1b1cb`. Renamed only —
  `spider_animated_character.glb` -> `cyborg_spider.glb`, so the bundle address says what it is.
- **Contents, MEASURED through ContentTool's own reader** (not the store page's claims):
  - **2 meshes.** `Spider_Spider_M_0` — 1 226 vertices, **1 552 triangles**, material `Spider_M`,
    driven by the skin. And `pPlane1_Camera_lambert2_0` — **4 vertices, 2 triangles**, no skin: the
    exporter's camera backdrop. The bake takes the skinned one and NAMES the one it drops
    (`GlbReader.cs`, "which mesh is the model"). The page claimed 1 554 tris; the mesh has 1 552.
  - **49 joints**, `_rootJoint` -> `bones_01` -> `body_02`, which carries 8 legs of 5 segments
    (`lapa_*`), 4 teeth (`zub_*`) and a back/abdomen chain. Max depth 12, no IK or pole targets.
  - **UVs present** (`TEXCOORD_0` and `TEXCOORD_1`) and 2 embedded images — the previous model had
    no UVs at all.
  - **7 clips**, and they are laid out **end to end on ONE shared 13.83 s timeline**, not rebased to
    zero: `Spider_Walk` 0.3333..1.1333, `Spider_Idle` 1.3333..3, `Spider_Idle_long` 3.3333..8.3333,
    `Spider_Damage` 8.6667..9.5, `Spider_Attack_1` 9.6667..10.9, `Spider_Attack_2` 11.3333..12.5,
    `Spider_Death` 12.6667..13.8333. ContentTool lifts each clip off that reel to its own zero and
    says so in the bake log; before it did, the death clip imported as 13.83 s of which 12.67 s was
    a frozen pose.
  - **Uncompressed** — no `extensionsRequired`, unlike the previous model's meshopt+quantization.

## The model this replaced

`spider.glb` — "Spider" by **Quaternius**, https://poly.pizza/m/yRYJiAJyiM, **CC0 1.0**, 39 joints,
5 clips, no UVs, SHA-1 `973ee4d7c16378c249f3f8c69b028bc9970372f5`. It is still the fixture the
offline test suite reads (`ContentTool\lib\u8_probe.glb` / `u10_probe.glb` are byte-identical copies
of it), so it has NOT left the repository — only the demo's own content folder.

Its rig and this one are **not compatible**: 39 joints against 49, max depth 6 against 12, two hub
bones against one, 20 leaf bones against 13, and different naming entirely. No bone-name map could
carry animation between them. None is needed — this model brings its own clip for every role.

## If you swap in your own creature

Delete the `.glb`, drop yours in, re-run `ct_project CustomCreature` — and replace this file with
YOUR model's licence. A CC0 or CC-BY model is safe to ship inside a mod; a "personal use" or
"editorial use" one is not, whatever the file format says. CC-BY additionally obliges you to keep
the author credited.
