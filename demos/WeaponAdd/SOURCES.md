# Sources — kept with the files, because people clone this repo

## The Sketchfab download, and what it is licensed as

One is committed and ships, in `Content\Models\` as `ar181.glb`, byte for byte the file described
here. It used to sit a second time in `tools\source\` under its download name; that copy was
identical to the shipped one and carried MB for nothing, so it is gone. Nothing about the
attribution below changes: it covers the shipped file, which is the same file.

**`ar-181.glb` (ships as `Content\Models\ar181.glb`) — SHIPPED, and attribution is a CONDITION of
doing so.**

- **Title:** AR-181 · **Author:** Frostoise
- **Source:** https://sketchfab.com/3d-models/ar-181-f32fe06215434fb2a159d29343079a1e
- **Licence:** **CC Attribution 4.0 International (CC BY 4.0)** —
  https://creativecommons.org/licenses/by/4.0/
- CC BY permits redistribution and commercial use **provided the author is credited**, which is why
  this block exists and must travel with the file. Unlike the CC0 assets below, removing the credit
  is a licence breach, not a discourtesy.
- **As downloaded:** the GLB conversion with 1k textures (8 408 796 B), not the 65 MB FBX original.
  Measured: 14 meshes, 5778 vertices / 4582 triangles, 3 materials, 10 embedded PNGs, UVs present.
- **Not usable as-is** — see the README's *The models that are not in yet*: 14 meshes must be joined
  and 3 materials atlased to one before the bake can take it.

## The model that is NOT here any more, and why

`Content\Models\taupistol.glb` — a Tau Pulse Pistol by **Black Bladder (@IronGroin)**,
https://sketchfab.com/3d-models/tau-pulse-pistol-71140a9218a148c38ed7a4cb604a053f — was shipped here
until 2026-08-28 and has been **DELETED**, along with its `publish` row and the `"model"` key on
`Morgott_VultureSidearm_WeaponDef`. Two independent reasons, either of which is sufficient:

- **Its licence forbids exactly this.** The page states Sketchfab **"Free Standard"** — Sketchfab's
  own proprietary licence, https://sketchfab.com/licenses, **not** CC0 and **not** CC BY, and
  additionally **NoAI**. Standard permits using the model inside your own product but forbids making
  the Licensed Material available "as a stand-alone file (or group of files) or in a way that allows
  third parties to use, download, extract or access the Licensed Material as a stand-alone file". A
  `.glb` committed to a public repository is straightforwardly that.
- **It is Warhammer 40,000 fan art.** The Tau are Games Workshop's intellectual property. That is a
  question about the *subject*, not the file's licence, and it does not get better by being a
  separate question.

**Nothing was lost from the demonstration.** A weapon with no `"model"` keeps its donor's
`SimpleSkinDataDef` and wears the donor's art — the README calls that a legitimate answer, and
`WeaponBuild.cs:137` logs it as one (`(no "model" - wears SY_LaserPistol_WeaponDef's own art)`). The
Vulture Sidearm is now that case, in the same mod as two weapons that do wear their own mesh, which
makes the contrast readable instead of hypothetical. If you want a third model here, drop your own
`.glb` in `Content\Models\`, add a `publish` row and a `"model"` key, re-run `ct_project WeaponAdd` —
and record ITS licence in this file.

## The model and its texture

- **Files shipped here:** `Content\Models\sniper.glb` (fitted, see `tools\fit_sniper.py`),
  `Content\Textures\sniper.png` (the kit's atlas at 1024, see `tools\downscale_atlas.ps1`),
  `Icons\sniper_inv.png` (rendered from the .glb by `tools\render_icon.py`).
- **As downloaded:** `tools\source\Gun_Sniper.gltf` + `Gun_Sniper.bin` +
  `T_Guns_Batch2_BaseColor.png`, so every step above re-runs from the original.
- **Author:** Quaternius — https://quaternius.com
- **Pack:** Sci-Fi Essentials Kit — https://opengameart.org/content/sci-fi-essentials-kit
- **License:** **CC0 1.0 Universal** (public domain) —
  https://creativecommons.org/publicdomain/zero/1.0/
- **Redistribution:** unrestricted. No attribution is required; it is here because it is the decent
  thing to do and because the next person needs to know where the file came from.
- **Geometry as shipped:** 8249 vertices / 8728 triangles, one material.
- **Not shipped:** the kit's `T_Guns_Batch2_Normal.png` and `T_Guns_Batch2_ORM.png`. ContentTool's
  baked Material binds one texture (`_MainTex`, `ProjectBake.cs:98-100`), and an ORM map would need
  an R/G/B -> R/A channel repack before Unity's Standard shader could read it. Shipping dead files
  would only suggest they do something.

## What this mod does NOT contain

Nothing of Snapshot Games'. The weapon's numbers are read out of the player's own installed
`PX_SniperRifle_WeaponDef` at runtime and cloned; the model is served out of this mod's own bundle,
which ContentTool bakes on the player's machine. No shipped file is copied, patched or modified.
