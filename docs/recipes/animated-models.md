# Add a complete model

Import a complete GLB into your mod's own bundle and publish a new Addressables key. A published model can then be used by a creature, weapon or your own code. Publishing alone does not spawn it.

## You need

- A GLB 2.0 file directly in `Content\Models`. OBJ is for [mesh replacement](meshes.md).
- Geometry, UVs and any rig, weights and clips the model needs. For animation, follow [the animation contract](animation-contract.md).
- Base-colour images in the GLB or PNG/JPG/JPEG files in `Content\Textures`.
- A new key that does not already exist in the game's catalog.

The bake binds base colour through `_MainTex` and the material colour. It does not bind normal, metallic or occlusion maps for an added model. An emissive factor can light the material; an emissive texture is not bound. See [known limitations](../known-limitations.md).

## Folder layout

```text
Mods\
  MyModelMod\
    meta.json
    ppcontent.json              <- publishes the baked model path
    Content\
      Models\
        soldier.glb             <- complete model, directly in this folder
      Textures\
        soldier.png             <- optional fallback base colour
        soldier_uniform.png     <- optional image for material "uniform"
    Dist\
      MyModelMod.bundle         <- written by the bake
```

For each material, `soldier_<material>.png` wins over `soldier.png`; an embedded base-colour image is the final fallback.

## Steps

1. Export the model as `soldier.glb` and put it directly in `Content\Models`. Keep its material names stable if you use per-material images. You should see the file in that folder.

2. Optionally put base-colour images directly in `Content\Textures`. For a material named `uniform`, save `soldier_uniform.png`.

3. Create `meta.json`:

   ```json
   {
     "ID": "example.mymodelmod",
     "Version": "1.0.0",
     "Name": [{ "Key": "English", "Value": "My model mod" }],
     "Dependencies": ["com.morgott.ContentTool"]
   }
   ```

4. Create `ppcontent.json`. `asset` is the baked relative path, not the GLB filename:

   ```json
   {
     "id": "example.mymodelmod",
     "bundle": "MyModelMod.bundle",
     "publish": [
       {
         "key": "8f924c3a6d7e4b22a5f149b3cd881001",
         "asset": "models/soldier",
         "type": "GameObject",
         "deps": "defaultlocalgroup_unitybuiltinshaders.bundle"
       }
     ]
   }
   ```

5. Bake, publish the key in this session, then ask the game's Addressables to verify it:

   ```text
   ct_project MyModelMod
   ct_catalog apply MyModelMod
   ct_catalog verify
   ```

6. Give the published key to the creature, weapon or code that will use the model. Enable the mod to publish its keys automatically for normal play. Package after the bake and catalog checks pass:

   ```text
   ct_package MyModelMod
   ```

## Check it worked

```text
model 'soldier' -> assets/example.mymodelmod/models/soldier <geometry and rig summary>
ct_project: ALL PASS - <project>\Dist\MyModelMod.bundle
ct_catalog: PASS - the game's own Addressables served the mod's own bundle, and nothing was written to the installation
```

A line beginning `model 'soldier' kept` appears only when the model has more than one material. Check the model through the creature, weapon or code that consumes its key; catalog verification alone does not display or spawn it.

For animated GLBs, a curve that only holds a non-bone helper node at its rest pose is dropped because that rest transform is already folded into the child bone. This includes a held `CUBICSPLINE` curve: its tangent values are not poses. A helper node that actually moves between bones is refused, because the resulting skeleton has no bone on which to play that motion.

## Common errors

| What you see | Why | Fix |
|---|---|---|
| `SOURCE SKIPPED: <file>: <reason> - SKIPPED, the project's other sources are unaffected` | The GLB or an image could not be imported. This counts as a bake failure. | Read the reason, re-export the named file, and bake again. |
| A refusal naming `TEXCOORD_<n>` | A material uses a base-colour UV set other than the first. | In Blender, make that UV map the first one and re-export. |
| A refusal naming `KHR_texture_transform` | The base-colour image uses a texture offset, scale or rotation that the bake does not apply. | Move or scale the UVs themselves and re-export. |
| `REFUSED: no mod bundle at <path> - bake it first with 'ct_project MyModelMod'. Installing a key does not bake, on purpose: a build command must not mutate the player's game installation.` | Catalog apply ran before a successful bake. | Bake first and resolve its failures. |

See [messages](../reference/messages.md) for other refusals.

## Example

[WeaponAdd](https://github.com/UberMorgott/PhoenixPoint-Mod-ContentTool/tree/main/demos/WeaponAdd) publishes mod-owned model keys and uses them for new weapons.
