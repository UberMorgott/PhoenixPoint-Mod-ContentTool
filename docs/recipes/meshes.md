# Replace a mesh

This replaces the geometry of one shipped Unity `Mesh`. Use the static path for weapons and props;
use the rigged path only when your GLB carries an armature and weights compatible with the target.

## What you need before you start

- A GLB or OBJ directly under `Content\Meshes`. GLB can carry skin data; OBJ cannot.
- The shipped bundle and exact, case-sensitive target `Mesh` name.
- For a rigged target: a GLB armature, vertex groups and bone names. A skinless source is refused.
- For a static target: no armature is required.

The target decides the weight-stream width. A dim4 target keeps four influences. A dim2 target keeps
the two heaviest influences and renormalises them. ContentTool does not reduce every mesh to two.

## Folder tree

```text
MyMeshMod\
  meta.json
  ppcontent.json               <- mesh is source stem; asset is shipped Mesh name
  Content\
    Meshes\                    <- replacement geometry only
      rifle.glb                <- static example; imported as "rifle"
      soldier.glb              <- rigged example; armature and weights included
    Textures\                  <- any separate texture replacements go here
```

## Steps

1. Find the target and inspect its bones:

   ```text
   ct_list assets px_equipment_assets_all.bundle Mesh WPN_PX_RG_Assault_Rifle
   ct_list bones px_equipment_assets_all.bundle WPN_PX_RG_Assault_Rifle_T01_V01
   ```

   `ct_list bones` is useful for a rigged target. A static weapon reports no skin to reproduce.

2. Extract the shipped mesh if you want its size, UVs or rig as a reference:

   ```text
   ct_extract mesh px_equipment_assets_all.bundle WPN_PX_RG_Assault_Rifle_T01_V01
   ```

   Copy the printed GLB from `ContentTool\Extracted\px_equipment_assets_all` into your project.
   Rename your finished source `rifle.glb`.

3. For a rigged replacement, bind the source to an armature in Blender and export GLB with skinning.
   Use the target's bone names. For a static weapon, export plain geometry.

4. Create `meta.json`:

   ```json
   {
     "ID": "example.mymeshmod",
     "AssemblyName": "",
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

6. Put `rifle.glb` directly in `Content\Meshes`. If you also replace the rifle textures, add separate
   texture rows and put those images in `Content\Textures`. The `WeaponMesh` demo does exactly that:
   one mesh row and five texture rows, all on the **Replace** route.

7. Bake and package:

   ```text
   ct_project MyMeshMod
   ct_package MyMeshMod
   ```

## What success looks like

The geometry summary and binding text depend on the file:

```text
patch px_equipment_assets_all.bundle: mesh 'WPN_PX_RG_Assault_Rifle_T01_V01' <- rifle <geometry summary> - skinned <binding result>
WROTE <patched path> <bytes> B as <bundle identity>
P4 PASS <read-back geometry comparison>
P5 VOID 'WPN_PX_RG_Assault_Rifle_T01_V01' is not rigged - <skin summary>
copies ready in <path> - nothing to install: ticking 'MyMeshMod' on serves these private copies in memory; ticking it off hands the shipped bundles straight back
ct_project: ALL PASS - this project has no bundle of its own; the patched copy(ies) above are the whole output
```

For a rigged target, P5/P6 print `PASS` checks instead of the P5 `VOID` static-mesh line.

## When it fails

| Exact output | Meaning | Fix |
|---|---|---|
| `P4 REFUSED 'rifle' is not a .obj or .glb under Content\Meshes\` | The source stem was not imported. | Move `rifle.obj` or `rifle.glb` directly into `Content\Meshes`, or correct `mesh`. |
| `P4 REFUSED target 'WPN_PX_RG_Assault_Rifle_T01_V01' is not a Mesh in px_equipment_assets_all.bundle - <reason> - list the names it does hold with: ct_list assets px_equipment_assets_all.bundle Mesh` | The shipped target name is wrong or ambiguous. | Run the printed command and copy the exact name. |
| `P4 REFUSED '<source>' -> '<target>' is a rigged model - it bends with the character's skeleton - and the replacement file carries no armature, so there are no weights to follow that skeleton with. Every vertex would be welded to whichever bone it happens to sit nearest, and the model would collapse onto that one bone as soon as the character moves. In Blender, give the mesh an Armature modifier with vertex groups, weight it to the bones the target already has, and export it as .glb. A file with no armature can only replace a STATIC object (one with a MeshFilter, like a weapon).` | A skinless source was aimed at a rigged target. | Add an armature and weights in Blender and export GLB, or choose a static target. OBJ cannot satisfy this route. |
| `SOURCE SKIPPED: <file>: <reason> - SKIPPED, the project's other sources are unaffected` | The OBJ/GLB importer rejected the source. | Re-export or delete the bad file, then bake again. |

Read [the status glossary](../troubleshooting/bake-errors.md). A fallback binding reported in the
successful `patch` line is not the same as a refused skinless source.
