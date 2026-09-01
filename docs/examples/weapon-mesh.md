# WeaponMesh

This demo changes the Ares AR-1 that Phoenix soldiers start with. In missions and the inventory you
see a different rifle model, its own five texture maps, and a matching inventory icon. Its rules and
stats remain those of the shipped Ares.

**Corresponds to:** [Replace weapon art](../recipes/weapon.md), [Replace a mesh](../recipes/meshes.md),
[Replace a texture](../recipes/textures.md), and [Build a behaviour DLL](../recipes/behavior-dll.md).

## Features and how they work

- **The static rifle mesh is replaced.** A **Replace** row maps `Content\Meshes\rifle.glb` to Mesh
  `WPN_PX_RG_Assault_Rifle_T01_V01` in `px_equipment_assets_all.bundle`.
- **Five shipped Texture2D assets are replaced.** Five more Replace rows target the same rifle's
  `_albedo`, `_normal`, `_metallic`, `_occlusion` and `_emissive` assets. Every source is a direct
  file under `Content\Textures`; `Content\Meshes\materials` is not used.
- **Neutral maps satisfy the shipped material slots.** The model has no tangent basis, so the normal
  is the demo's flat `(128,128,255,128)` map. Metallic is `(179,179,179,102)`, occlusion is white,
  and emissive is black. The albedo is the authored 1024-pixel map.
- **The foreign mesh was fitted before baking.** `tools\fit_rifle.py` turns its axes and applies a
  uniform scale of `0.568735` against the shipped rifle bounds. The bake does not guess grip fit.
- **The inventory icon is a separate field edit.** `WeaponMesh.dll` loads `Icons\rifle_inv.png`,
  creates a Sprite, and assigns both icon fields on `E_View [PX_AssaultRifle_WeaponDef]`. A mesh
  replacement cannot change a Sprite stored on a def.
- **This is only Replace.** The six manifest rows all patch `px_equipment_assets_all.bundle`; this
  demo does not add a new weapon def or publish a new model key.

## Project on disk

```text
WeaponMesh\
  meta.json                         <- AssemblyName is WeaponMesh.dll
  ppcontent.json                    <- one mesh + five texture Replace rows
  WeaponMesh.csproj
  Content\
    Meshes\
      rifle.glb                     <- static source mesh
    Textures\                       <- texture importer scans here
      rifle_albedo.png
      rifle_normal_flat.png
      rifle_metallic_flat.png
      rifle_occlusion_white.png
      rifle_emissive_off.png
  Icons\
    rifle_inv.png                   <- loaded by the DLL, not by ct_project
  Dist\WeaponMesh.bundle            <- mod-owned imported sources; not a shipped patched bundle
  bin\Release\WeaponMesh\
    WeaponMesh.dll
  src\WeaponMeshMain.cs             <- writes the two icon fields
  tools\fit_rifle.py
  tools\make_neutral_maps.py
  tools\render_icon.py
  tools\source\                     <- original CC0 model files
  README.md
  SOURCES.md
```

## Rebuild and run it

Replace `PPRoot` with your game folder. Run the discovery commands in the game console.

```text
ct_list assets px_equipment_assets_all.bundle Mesh WPN_PX_RG_Assault_Rifle_T01_V01
ct_list assets px_equipment_assets_all.bundle Texture2D WPN_PX_RG_Assault_Rifle_T01_V01
dotnet build demos\WeaponMesh\WeaponMesh.csproj -c Release -p:PPRoot="D:\Steam\steamapps\common\Phoenix Point"
ct_project WeaponMesh
ct_package WeaponMesh
```

Enable the mod. ContentTool redirects the shipped bundle to a private patched copy; there is no
manual install command. Open the starting inventory or a tactical mission to inspect the rifle.

## What a good run prints

```text
patch px_equipment_assets_all.bundle: mesh 'WPN_PX_RG_Assault_Rifle_T01_V01' <- rifle <geometry summary> - skinned <binding result>
patch px_equipment_assets_all.bundle: 'WPN_PX_RG_Assault_Rifle_T01_V01_albedo' <- rifle_albedo <width>x<height>
P4 PASS mesh 'WPN_PX_RG_Assault_Rifle_T01_V01' in the copy IS rifle -> <read-back geometry summary>
P1 PASS every replaced Texture2D in px_equipment_assets_all.bundle reads back its new pixels
copies ready in <path> - nothing to install: ticking 'WeaponMesh' on in the mod manager redirects them (dev-only shortcut: ct_route7 apply WeaponMesh)
ct_project: ALL PASS - <project>\Dist\WeaponMesh.bundle
```

The icon half reports in `Player.log`:

```text
W1-icon PASS E_View [PX_AssaultRifle_WeaponDef].InventoryIcon and .SmallIcon now draw Icons\rifle_inv.png (450x450)
```

## Verification status

**Verified in-game on 2026-08-27.** The live rifle read 5554 vertices instead of the shipped 5771.
Its albedo read 1024×1024 RGBA32 instead of 2048×2048 DXT1, and the icon was a standalone
450×450 ARGB32 texture.
