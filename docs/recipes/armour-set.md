# Replace a shipped armour set from a Blender export

Give a shipped armour set new geometry and colour while keeping its original body-part slots. This example replaces a PX Heavy torso and both legs with three skinned GLBs and one albedo source.

## You need

- ContentTool installed and enabled, Blender, and three GLB exports with armatures, vertex groups and weights.
- The shipped torso, right-leg and left-leg mesh targets, plus their texture targets. Confirm exact names in your installation.
- [Model Doctor](../bench/model-doctor.md) to compare each export against the intended shipped slot before baking.

## Folder layout

```text
HeavyArmour\
  meta.json
  ppcontent.json
  Content\
    Meshes\
      CHR_PX_HVY_TS_M_V01.glb       <- torso export
      CHR_PX_HVY_RL_M_V01.glb       <- right-leg export
      CHR_PX_HVY_LL_M_V01.glb       <- left-leg export
      materials\
        armour_albedo.png           <- keep if GLB material sidecars refer to it
    Textures\
      armour_albedo.png             <- source for Replace texture rows
```

## Steps

1. In the game console, list the target assets:

   ```text
   ct_list assets px_heavy_assets_all.bundle Mesh CHR_PX_HVY
   ct_list assets px_heavy_assets_all.bundle Texture2D CHR_PX_HVY
   ```

   Match the torso, right leg and left leg to their shipped slots. Preserve the exact case and spelling of the texture targets you select.

2. Open [Model Doctor](../bench/model-doctor.md) from a loaded geoscape with the `Replace a model` card. For each GLB, press `Choose your model file (.glb)...`, then `Choose the game part it replaces...` and type `PX_HeavyStarting torso` (then `PX_HeavyStarting right leg`, `PX_HeavyStarting left leg`) into `Find`. Pick the Phoenix Heavy torso, right-leg or left-leg result; its tooltip names `Human_Torso_SlotDef`, `Human_RightLeg_SlotDef` or `Human_LeftLeg_SlotDef`. Read the badge and every row in `Details and log`. Aim for `BY NAME - your weights will be used`, then inspect any `SubmeshMaterials` warning.

3. Fix the exports in Blender before baking. A torso with no armature cannot replace a rigged target. Added bones that the target does not have can cause `NEAREST-BONE`, which discards your authored weights. Remove extra bones or map the rig to the target’s bone names, then re-export and rerun Model Doctor.

   Also inspect part and material order. A one-triangle first part can consume the body material and push the real torso onto the shoulder-pad material. Remove stray geometry with `Mesh > Merge > By Distance`, assign the intended material slots, or reorder parts to match the target. Re-export and check the Doctor again.

4. Create `meta.json`:

   ```json
   {
     "ID": "example.heavyarmour",
     "AssemblyName": "",
     "Version": "1.0.0",
     "Name": [{ "Key": "English", "Value": "Heavy armour replacement" }],
     "Dependencies": ["com.morgott.ContentTool"]
   }
   ```

5. Create `ppcontent.json`. These five rows name shipped PX Heavy targets in `px_heavy_assets_all.bundle`; confirm them with step 1 before baking on your installation:

   ```json
   {
     "id": "example.heavyarmour",
     "bundle": "HeavyArmour.bundle",
     "replace": [
       { "bundle": "px_heavy_assets_all.bundle", "asset": "CHR_PX_HVY_TS_M_V01", "mesh": "CHR_PX_HVY_TS_M_V01" },
       { "bundle": "px_heavy_assets_all.bundle", "asset": "CHR_PX_HVY_RL_M_V01", "mesh": "CHR_PX_HVY_RL_M_V01" },
       { "bundle": "px_heavy_assets_all.bundle", "asset": "CHR_PX_HVY_LL_M_V01", "mesh": "CHR_PX_HVY_LL_M_V01" },
       { "bundle": "px_heavy_assets_all.bundle", "asset": "CHR_PX_HVY_TS_M_V01_Albedo", "texture": "armour_albedo" },
       { "bundle": "px_heavy_assets_all.bundle", "asset": "CHR_PX_HVY_Legs_M_V01_albedo", "texture": "armour_albedo" }
     ]
   }
   ```

   The two texture targets can use the same source image. The source must exist directly in `Content\Textures`; keep another copy in `Content\Meshes\materials` only if your material sidecars need it.

6. Bake, read every replacement and read-back line, then package only after a clean result:

   ```text
   ct_project HeavyArmour
   ct_package HeavyArmour
   ```

   Enable the mod and restart if the shipped bundle was already loaded.

## Check it worked

Require three mesh patch lines, two texture patch lines and:

```text
ct_project: ALL PASS - <path>
```

Inspect the served armour from front and back, including the legs and material boundaries. Check other variants that share the replaced texture targets. A Doctor preview checks a mesh against a slot; the in-game inspection checks what the enabled mod actually serves.

## Common errors

| What you see | Why | Fix |
|---|---|---|
| `IMPORT REFUSED` | A rigged target received an export without usable skinning. | Add an Armature modifier, vertex groups and weights, then export again. |
| `NEAREST-BONE` | The source bones cannot bind to the target by name. | Remove or correct added bones and rerun Model Doctor until it reports by-name binding. |
| `SubmeshMaterials` under `WARNING` or `P4 WARN` | The GLB parts may take the wrong shipped materials. | Remove stray shards or correct part and material order in Blender. |
| `P1 REFUSED 'armour_albedo' is not a .png/.jpg under Content\Textures\` | The image exists only beside the mesh or under `materials`. | Copy it directly into `Content\Textures`. |
| `P1 REFUSED target '<asset>' is not a Texture2D` | The row names a source image or the wrong shipped target. | Use the exact target from `ct_list assets`; keep the source stem in `texture`. |

See [messages](../reference/messages.md) for more diagnostics.

## Example

This is a generic PX Heavy example derived from a Blender export workflow. Use [Model Doctor](../bench/model-doctor.md) with your own GLBs and verify each target on your installed game.
