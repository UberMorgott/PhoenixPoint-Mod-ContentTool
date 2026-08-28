# Textures, sprites and icons

Use `replace[].texture` when Phoenix Point already requests a `Texture2D` and you want it to receive
your image instead. Use an icon path only when another route—currently `weapons[]` or your own
behavior DLL—assigns that PNG to a game field. For the latter, read the shared
[profile-wide `Managed\` module warning](behavior-dll.md#managed-module-load-failure).

## Replace a shipped texture

Find and extract the target first:

```text
ct_list bundles acidworm
ct_list assets aln_acidworm_assets_all.bundle Texture2D acidworm
ct_extract tex aln_acidworm_assets_all.bundle acidworm_low_albedo
```

Copy the asset name with exact case. The extracted PNG tells you the original dimensions and UV
layout; it is a reference, not a file to redistribute.

This deliberately wrong tree demonstrates the missing/supported-extension refusal below:

```text
MyTexture\
  meta.json
  ppcontent.json
  Content\
    Textures\
      acid_skin.tga
```

Use a complete manifest:

```json
{
  "id": "yourname.mytexture",
  "bundle": "MyTexture.bundle",
  "replace": [
    {
      "bundle": "aln_acidworm_assets_all.bundle",
      "asset": "acidworm_low_albedo",
      "texture": "acid_skin"
    }
  ]
}
```

`bundle` and `asset` on the row identify shipped data. `texture` is the lowercased stem of your file
under `Content\Textures`; do not include the extension or path. PNG, JPG and JPEG are accepted;
the `.tga` above is intentionally unsupported. A second file with the same stem in that folder is
refused.

Read these rules before editing the image:

| Property | What the bake does |
|---|---|
| Dimensions | They do not have to match the original, be square, or be powers of two. |
| Runtime format | Rewrites the image as uncompressed RGBA32, inline, with exactly one mip; no mips are generated. |
| Alpha | Preserves the decoded alpha channel. |
| Colour space | Always stamps sRGB, with no author override. |

sRGB is correct for a base-colour map. It is **wrong** for linear-data maps such as normals,
metallic, ambient occlusion, and roughness. Those maps will be mis-tagged by this route even when
their pixels and dimensions are otherwise correct. One uncompressed mip also costs four bytes per
pixel and provides no generated distance mips.

Bake and apply while authoring:

```text
ct_project MyTexture
ct_route7 apply MyTexture
```

A missing source is a failed bake, not a warning. For the manifest above, the final lines are:

```text
P1 REFUSED 'acid_skin' is not a .png/.jpg under Content\Textures\
ct_project: 1 FAILURE(S)
```

The refusal text names only `.png/.jpg`, but the importer accepts `.png`, `.jpg`, and `.jpeg`.
Replace `acid_skin.tga` with one of those extensions in that exact folder and rerun `ct_project`;
do not continue to packaging after a `FAILURE(S)` summary.

Put the asset on screen and inspect it at the angles and distances players will use. A successful
bake proves the file was encoded and the patched copy was formed; it does not prove that your UVs,
alpha, color space or intended renderer are correct.

After the last edit, rerun `ct_project`, then package:

```text
ct_package MyTexture
```

## Multiple textures in one project

Add one flat row per target. A row may contain only one route key.

```json
{
  "id": "yourname.weaponpaint",
  "bundle": "WeaponPaint.bundle",
  "replace": [
    {
      "bundle": "px_weapons_assets_all.bundle",
      "asset": "PX_AssaultRifle_Albedo",
      "texture": "rifle_albedo"
    },
    {
      "bundle": "px_weapons_assets_all.bundle",
      "asset": "PX_AssaultRifle_Normal",
      "texture": "rifle_normal"
    }
  ]
}
```

Target names above are illustrative; obtain the actual names from your installation.

## Icons

An icon is not discovered or published automatically from `Icons\`. Something must assign it. A
weapon row can do that directly:

```text
MyWeapon\
  meta.json
  ppcontent.json
  Icons\
    rifle_inventory.png
```

```json
{
  "id": "yourname.myweapon",
  "bundle": "MyWeapon.bundle",
  "weapons": [
    {
      "id": "YourName_MyRifle_WeaponDef",
      "clone": "PX_AssaultRifle_WeaponDef",
      "guid": "replace-this-with-your-own-dashed-uuid",
      "name": "My Rifle",
      "icon": "Icons\\rifle_inventory.png",
      "count": 1,
      "clips": 3
    }
  ]
}
```

That weapon needs a behavior DLL to call the weapon builder; see [A new weapon](weapon.md). For an
unrelated def, load the PNG and assign the target sprite in your own code. Merely placing a PNG in
`Icons\` changes nothing.

## Failure checklist

- No change in game: confirm the row's `asset` spelling and case, then check `ct_route7 status`.
- `texture` not found: match the lowercased source stem and supported extension.
- Wrong object changed: duplicate-looking names are common; use the bundle and path ID printed by
  discovery to verify the target.
- Black, flat or wrongly lit result: an albedo, normal, mask and emission map are different targets;
  replacing one does not change the material or the others.
- Package refusal: fix the first named cause, rebake when requested, and rerun `ct_package`.
