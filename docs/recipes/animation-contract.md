# Prepare an animated GLB

Prepare a skeleton, skin and named clips that ContentTool can import. The result can animate an added creature or humanoid, or replace compatible shipped rigged geometry.

## You need

- A GLB 2.0 file with one usable model hierarchy.
- An armature, vertex groups and skin weights for deforming geometry.
- Bone names and hierarchy paths suited to the intended target. For shipped-mesh replacement, inspect the target with `ct_list bones`.
- Named actions for clips you want to play.

A shipped rigged mesh sets the output weight-stream width: a dim4 target keeps four influences; a dim2 target keeps the two heaviest and renormalises them. That rule does not apply to every added model.

## Folder layout

```text
Mods\
  RigCheck\
    meta.json
    ppcontent.json             <- selects a clip and loop names
    Content\
      Models\
        creature.glb           <- geometry, armature, weights and clips
```

## Steps

1. In Blender, bind each deforming mesh to its armature. Give its vertex groups the intended bone names. For a shipped target, list its bones first:

   ```text
   ct_list bones <shipped.bundle> <rigged-mesh-name>
   ```

2. Name each exported action clearly. In this example, export clips named `Spider_Idle` and `Spider_Walk`. Export binary glTF with skinning and animation enabled, then put `creature.glb` directly in `Content\Models`.

3. Create `meta.json`:

   ```json
   {
     "ID": "example.rigcheck",
     "Version": "1.0.0",
     "Name": [{ "Key": "English", "Value": "Rig check" }],
     "Dependencies": ["com.morgott.ContentTool"]
   }
   ```

4. Create `ppcontent.json`:

   ```json
   {
     "id": "example.rigcheck",
     "bundle": "RigCheck.bundle",
     "scale": 1.0,
     "play": "Spider_Idle",
     "loop": "Spider_Idle, Spider_Walk"
   }
   ```

   `play` names one clip; two names are refused. `loop` is a comma-separated list. An entry without `*` matches one name, ignoring case. With `*`, the first part must start the name and the last part must end it; `*Idle*` matches a name containing `Idle`.

5. Bake and read the clip and rig checks:

   ```text
   ct_project RigCheck
   ```

6. Use [the creature recipe](creature.md) if the model will become a creature. Its clip roles and events are separate from this import check.

## Check it worked

The output should name the imported model and end with:

```text
model 'creature' -> assets/example.rigcheck/models/creature <geometry and rig summary>
ct_project: ALL PASS - <project>\Dist\RigCheck.bundle
```

Inspect the `M1-wrote`, `M1` and `U9` checks for the model and its own clips. Then test the animation on the intended actor in game: an import pass cannot prove that the actor's controller uses every clip.

A non-bone helper node between bones contributes its rest transform to the child. A curve that only repeats that rest pose is dropped, including a held `CUBICSPLINE` curve. If the helper actually moves, import refuses the GLB rather than silently freezing that part of the rig. Re-export the FBX through Blender as GLB with the intended bone hierarchy. The bake may also report dropped curves for nodes that are not bones.

## Common errors

| What you see | Why | Fix |
|---|---|---|
| `SOURCE SKIPPED: <file>: <reason> - SKIPPED, the project's other sources are unaffected` | The GLB importer rejected the file. This counts as a bake failure. | Read the reason and re-export the named GLB. |
| A refusal saying a non-bone object sits between a parent and child bone | That helper moves, but the baked skeleton has no bone for its curve. | Export the rig from Blender with bones directly under their intended parents, or remove the helper's motion. |
| `U9 FAIL <details>` | An imported clip did not drive the rig as its sampled curves predict. | Check bone paths, the action and exported keyframes; re-export and bake again. |
| `P4 REFUSED '<source>' -> <reason>` | A source aimed at a shipped rigged mesh lacks compatible armature or weights. | Read the full reason, bind the source to the target skeleton and export a skinned GLB. |

See [messages](../reference/messages.md) for other checks and refusals.

## Example

[CustomCreature](https://github.com/UberMorgott/PhoenixPoint-Mod-ContentTool/tree/main/demos/CustomCreature) carries a creature's own rig and clips. [HumanoidSoldier](https://github.com/UberMorgott/PhoenixPoint-Mod-ContentTool/tree/main/demos/HumanoidSoldier) shows the humanoid route.
