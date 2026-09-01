# Prepare a rigged, animated GLB

This is the import contract shared by complete animated models, creatures and humanoid soldiers.
Use it before those recipes when your GLB contains a skeleton, skin weights or animation clips.

## What you need before you start

- A GLB 2.0 file with one usable model hierarchy.
- An armature, vertex groups and skin weights. A creature or rigged replacement cannot use a
  skinless file.
- Stable bone names and hierarchy paths. Unity Generic clips bind to transform paths, not visual
  similarity.
- Named animation clips when you use the file's own animations.
- No more than four source influences per vertex. The target controls what is serialized.

For shipped-mesh replacement, a dim4 target retains four influences. A dim2 target keeps the two
heaviest and renormalises them. For a complete model, the GLB's own skin is baked into the mod-owned
prefab. Do not apply the dim2 rule to every model.

## Folder tree

```text
RigCheck\
  meta.json
  ppcontent.json               <- play/loop select clips for bake checks
  Content\
    Models\
      creature.glb             <- mesh, armature, weights and named clips in one file
    Textures\
      creature.png             <- optional external base colour
```

## Steps

1. In Blender, bind every deforming mesh to the armature. Keep vertex groups named after their bones.
   Remove unused duplicate armatures and apply the coordinate transforms you intend to ship.

2. Give every exported action a unique name. Do not rely on names such as `Action` or `Take 001`
   to explain their role later.

3. Export binary glTF (`.glb`) with skinning and animation enabled. Name it `creature.glb` and put it
   directly under `Content\Models`.

4. Create `meta.json`:

   ```json
   {
     "ID": "example.rigcheck",
     "AssemblyName": "",
     "Version": "1.0.0",
     "Name": [{ "Key": "English", "Value": "Rig check" }],
     "Dependencies": ["com.morgott.ContentTool"]
   }
   ```

5. Create `ppcontent.json`. `play` must equal a clip name stored in the GLB; `loop` is a
   comma-separated wildcard list used by the bake:

   ```json
   {
     "id": "example.rigcheck",
     "bundle": "RigCheck.bundle",
     "scale": 1.0,
     "play": "Spider_Idle",
     "loop": "Spider_Idle, Spider_Walk"
   }
   ```

6. Bake. If this is becoming a creature, the first bake also writes a clip-role scaffold; continue
   with [Add a creature](creature.md) instead of guessing the roles.

   ```text
   ct_project RigCheck
   ```

7. Inspect the emitted clip list and every M1/U7 result. A final all-pass is the minimum import gate;
   it does not prove that a game controller has a state for every clip you will need.

## What success looks like

```text
model 'creature' -> assets/example.rigcheck/models/creature <geometry/rig summary> animator -> '<controller>'
WROTE <project>\Dist\RigCheck.bundle <bytes> B as example_rigcheck
M1-wrote PASS <file-level hierarchy, bind-pose and skin check>
M1 PASS <runtime deformation check>
U9 PASS <the imported GLB clip drives the imported rig at the sampled frame>
U9-mecanim PASS <the same imported clip drives the rig through the baked AnimatorOverrideController>
ct_project: ALL PASS - <project>\Dist\RigCheck.bundle
```

The checks print details derived from your own vertices, weights, bone paths and curves. Compare the
gate word (`PASS`, `FAIL` or `VOID`) and the final summary; do not expect fixed vertex counts.

## When it fails

| Exact output | Meaning | Fix |
|---|---|---|
| `SOURCE SKIPPED: <file> <reason> - SKIPPED, the project's other sources are unaffected` | The GLB parser rejected the file. | Re-export GLB 2.0 with geometry, skin and animation included; delete the rejected copy. |
| `M1 FAIL model '<name>' loaded with <details>` | The baked prefab did not preserve the imported model/skin contract. | Read the detail after `FAIL`; correct the source hierarchy, bind poses or weights, then bake again. |
| `U7 VOID the model prefab '<key>' <reason>` | The join between model and clip could not be exercised. | Supply the named clip/controller condition reported by the line. Do not call a VOID a pass. |
| `U9 FAIL <details>` | The imported clip did not put the rig where its own sampled curves predict. | Check bone hierarchy paths and the exported action, then re-export. |
| `P4 REFUSED '<source>' -> '<target>' is a rigged model - it bends with the character's skeleton - and the replacement file carries no armature, so there are no weights to follow that skeleton with. Every vertex would be welded to whichever bone it happens to sit nearest, and the model would collapse onto that one bone as soon as the character moves. In Blender, give the mesh an Armature modifier with vertex groups, weight it to the bones the target already has, and export it as .glb. A file with no armature can only replace a STATIC object (one with a MeshFilter, like a weapon).` | A rigged replacement source has no armature. | Add an armature modifier, vertex groups and weights; export GLB. |

Read [the status glossary](../troubleshooting/bake-errors.md). `VOID` means the named check could not
measure anything; only the final `ALL PASS` closes the bake.

## Worked demos

- [CustomCreature](../examples/custom-creature.md) keeps a downloaded creature's own rig and clips.
- [HumanoidSoldier](../examples/humanoid-soldier.md) carries 300 game clips retargeted to a foreign rig.
- [ReplaceCharacterBody](../examples/replace-character-body.md) uses the same full humanoid clip set
  for an experimental in-place body swap.
