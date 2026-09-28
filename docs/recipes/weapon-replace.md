# Replace the art on a shipped weapon

Change the mesh and textures of an existing weapon. Its `WeaponDef`, inventory identity and gameplay stats stay the shipped weapon’s.

## You need

- ContentTool installed and enabled.
- A fitted GLB or OBJ for the shipped mesh, and PNG, JPG or JPEG replacement textures.
- The exact shipped bundle, `Mesh` name and `Texture2D` names. [Find content](../find-content/index.md) explains the lookup.

## Folder layout

```text
WeaponReplace\
  meta.json
  ppcontent.json
  Content\
    Meshes\
      rifle.glb                  <- replacement geometry
    Textures\
      rifle_albedo.png           <- replacement images
      rifle_normal_flat.png
      rifle_metallic_flat.png
      rifle_occlusion_white.png
      rifle_emissive_off.png
```

## Steps

1. In the game console, list the shipped targets:

   ```text
   ct_list assets px_equipment_assets_all.bundle Mesh WPN_PX_RG_Assault_Rifle
   ct_list assets px_equipment_assets_all.bundle Texture2D WPN_PX_RG_Assault_Rifle
   ```

   Copy exact asset names from your installation. Put your source files directly in the folders shown above.

2. Create `meta.json`:

   ```json
   {
     "ID": "example.weaponreplace",
     "AssemblyName": "",
     "Version": "1.0.0",
     "Name": [{ "Key": "English", "Value": "Weapon replacement" }],
     "Dependencies": ["com.morgott.ContentTool"]
   }
   ```

3. Create `ppcontent.json`. Each `asset` is a shipped target; each `mesh` or `texture` is one of your source file stems:

   ```json
   {
     "id": "example.weaponreplace",
     "bundle": "WeaponReplace.bundle",
     "replace": [
       { "bundle": "px_equipment_assets_all.bundle", "asset": "WPN_PX_RG_Assault_Rifle_T01_V01", "mesh": "rifle" },
       { "bundle": "px_equipment_assets_all.bundle", "asset": "WPN_PX_RG_Assault_Rifle_T01_V01_albedo", "texture": "rifle_albedo" },
       { "bundle": "px_equipment_assets_all.bundle", "asset": "WPN_PX_RG_Assault_Rifle_T01_V01_normal", "texture": "rifle_normal_flat" },
       { "bundle": "px_equipment_assets_all.bundle", "asset": "WPN_PX_RG_Assault_Rifle_T01_V01_metallic", "texture": "rifle_metallic_flat" },
       { "bundle": "px_equipment_assets_all.bundle", "asset": "WPN_PX_RG_Assault_Rifle_T01_V01_occlusion", "texture": "rifle_occlusion_white" },
       { "bundle": "px_equipment_assets_all.bundle", "asset": "WPN_PX_RG_Assault_Rifle_T01_V01_emissive", "texture": "rifle_emissive_off" }
     ]
   }
   ```

4. Bake and read every patch and read-back line:

   ```text
   ct_project WeaponReplace
   ```

   Enable the mod in the mod manager. ContentTool redirects its private patched copy; it does not overwrite the shipped bundle. Restart if the game already loaded that bundle. Package after a clean bake:

   ```text
   ct_package WeaponReplace
   ```

## Check it worked

Look for a mesh patch, texture patches and the terminal success line:

```text
patch px_equipment_assets_all.bundle: mesh 'WPN_PX_RG_Assault_Rifle_T01_V01' <- rifle <geometry-and-binding-report>
patch px_equipment_assets_all.bundle: 'WPN_PX_RG_Assault_Rifle_T01_V01_albedo' <- rifle_albedo <width>x<height>
ct_project: ALL PASS - <path>
```

Equip the shipped weapon and inspect its model and texture in game. This content-only recipe changes the world model; changing its inventory icon needs separate DLL code, as the demo shows.

## Common errors

| What you see | Why | Fix |
|---|---|---|
| `P4 REFUSED 'rifle' is not a .obj or .glb under Content\Meshes\` | The mesh stem has no source in the expected folder. | Move the GLB or OBJ directly into `Content\Meshes`, or correct the stem. |
| `P1 REFUSED '<name>' is not a .png/.jpg under Content\Textures\` | A texture source is missing or placed elsewhere. | Put it directly in `Content\Textures` and use its stem. |
| `P4 REFUSED target '<asset>' is not a Mesh` | The target name or bundle is wrong. | Copy an exact `Mesh` name from `ct_list assets`. |
| `P1 REFUSED target '<asset>' is not a Texture2D` | The texture target name or bundle is wrong. | Copy an exact `Texture2D` name from `ct_list assets`. |

See [messages](../reference/messages.md) for more diagnostics.

## Example

[WeaponMesh](https://github.com/UberMorgott/PhoenixPoint-Mod-ContentTool/tree/main/demos/WeaponMesh) replaces one shipped rifle mesh and five textures. Its separate DLL also changes the inventory icon.
