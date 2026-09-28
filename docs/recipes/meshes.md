# Replace a mesh

Replace the geometry of a shipped Unity `Mesh`. A static prop or weapon can use OBJ or GLB. A rigged character needs a GLB with an armature and weights compatible with the shipped skeleton.

## You need

- An OBJ or GLB directly in `Content\Meshes`.
- The shipped bundle filename and exact `Mesh` name.
- For a rigged target, skin weights and bones named for the target. Use `ct_list bones` to inspect it.
- A visual check in game. ContentTool writes a tangent channel on replaced meshes (from the `.glb` when usable, otherwise computed from the UVs), but normal-map shading on a replaced mesh is not yet verified in game; see [known limitations](../known-limitations.md).

OBJ has no skin. If an OBJ face omits a normal, ContentTool computes that vertex's normal; normals supplied by the file are retained. For a rigged GLB, ContentTool matches joints to shipped bones by name and uses the shipped bind poses. Pose the source on that skeleton. A Model Doctor alias sidecar can rename source bones for replacement; see [Model Doctor](../bench/model-doctor.md).

## Folder layout

```text
Mods\
  MyMeshMod\
    meta.json
    ppcontent.json            <- mesh is the source stem
    Content\
      Meshes\
        rifle.glb             <- put replacement geometry directly here
```

## Steps

1. Find the shipped mesh. The list should include `WPN_PX_RG_Assault_Rifle_T01_V01`.

   ```text
   ct_list assets px_equipment_assets_all.bundle Mesh WPN_PX_RG_Assault_Rifle
   ```

2. Optionally extract the shipped mesh as a size and shape reference. The command prints a GLB path under `<persistentDataPath>\ContentTool\Extracted`.

   ```text
   ct_extract mesh px_equipment_assets_all.bundle WPN_PX_RG_Assault_Rifle_T01_V01
   ```

3. Make your replacement in Blender and export it as `rifle.glb`. Keep the scale and origin appropriate for the shipped target. For a rigged target, first inspect its bones and export the armature and weights with the GLB:

   ```text
   ct_list bones <shipped.bundle> <rigged-mesh-name>
   ```

4. Create `meta.json`:

   ```json
   {
     "ID": "example.mymeshmod",
     "Version": "1.0.0",
     "Name": [{ "Key": "English", "Value": "My mesh mod" }],
     "Dependencies": ["com.morgott.ContentTool"]
   }
   ```

5. Create `ppcontent.json`:

   ```json
   {
     "id": "example.mymeshmod",
     "bundle": "MyMeshMod.bundle",
     "replace": [
       {
         "bundle": "px_equipment_assets_all.bundle",
         "asset": "WPN_PX_RG_Assault_Rifle_T01_V01",
         "mesh": "rifle"
       }
     ]
   }
   ```

6. Put `rifle.glb` directly in `Content\Meshes`. Bake, then package after `ALL PASS`:

   ```text
   ct_project MyMeshMod
   ct_package MyMeshMod
   ```

7. Enable the mod and inspect the weapon in game. Check its shape, scale, material boundaries and lighting. A static GLB with several parts can keep separate material submeshes.

## Check it worked

For this static target, look for:

```text
patch px_equipment_assets_all.bundle: mesh 'WPN_PX_RG_Assault_Rifle_T01_V01' <- rifle <geometry summary> - skinned <binding result>
P4 PASS mesh 'WPN_PX_RG_Assault_Rifle_T01_V01' in the copy IS rifle -> <read-back geometry summary>
P5 VOID 'WPN_PX_RG_Assault_Rifle_T01_V01' is not rigged - <skin summary>
ct_project: ALL PASS - this project has no bundle of its own; the patched copy(ies) above are the whole output
```

`P5 VOID` is expected for a static mesh. For a rigged target, inspect the `P5` and deformation checks instead.

## Common errors

| What you see | Why | Fix |
|---|---|---|
| `P4 REFUSED 'rifle' is not a .obj or .glb under Content\Meshes\` | The source stem was not imported. | Put one matching OBJ or GLB directly in `Content\Meshes`. If the line names another folder, move it from there. |
| `P4 REFUSED target 'WPN_PX_RG_Assault_Rifle_T01_V01' is not a Mesh in px_equipment_assets_all.bundle - <reason> - list the names it does hold with: ct_list assets px_equipment_assets_all.bundle Mesh` | The target name is missing or ambiguous. | Run the printed list command and copy the exact name. |
| `P4 REFUSED '<source>' -> <reason>` | A rigged target rejected an incompatible source, such as a file with no armature. | Read the full reason; bind the mesh to the target bones and export a skinned GLB. |
| `SOURCE SKIPPED: <file>: <reason> - SKIPPED, the project's other sources are unaffected` | The source could not be imported. This counts as a bake failure. | Correct or re-export the named file, then bake again. |

See [messages](../reference/messages.md) for other refusals.

## Example

[WeaponMesh](https://github.com/UberMorgott/PhoenixPoint-Mod-ContentTool/tree/main/demos/WeaponMesh) replaces a shipped rifle mesh and five textures. Its DLL handles an additional icon edit; the mesh replacement itself does not require that DLL.
