# Animated models: keep a skeleton or bring your own

ContentTool has no general animation retargeter. Choose the route by deciding which skeleton owns
the result.

## Replace a mesh on the shipped skeleton

A rigged `replace[].mesh` keeps the target prefab, skeleton, Animator controller and shipped clips.
Your GLB supplies geometry, skin weights and bone names.

Before editing, list the target skeleton:

```text
ct_list bones mutoid_assets_all.bundle Geo_Head02_V01
```

The replacement GLB must satisfy all of these conditions:

- its joint count equals the target's bind-pose count;
- every target bone name appears exactly once and there are no extra or duplicate names;
- joint order may differ because binding is by name;
- vertex count and skin data cover the replacement geometry;
- the mesh is already posed on the shipped skeleton.

A bind pose is the skeleton's reference transform set that attaches mesh vertices to bones before
animation. ContentTool reads the GLB inverse bind matrices but deliberately uses the shipped bind
poses. It reduces four glTF influences to the two strongest per vertex and renormalizes them.
Inspect joints that depended on the discarded weights.

This complete manifest uses the strict rigged replacement path:

```json
{
  "id": "yourname.riggedhead",
  "bundle": "RiggedHead.bundle",
  "replace": [
    {
      "bundle": "mutoid_assets_all.bundle",
      "asset": "Geo_Head02_V01",
      "mesh": "rigged_head"
    }
  ]
}
```

Put `rigged_head.glb` under `Content\Meshes`, then run:

```text
ct_project RiggedHead
ct_route7 apply RiggedHead
```

Missing, added or duplicate bones are named in the refusal. Do not “fix” one by renaming the target
in the manifest; the mismatch is inside the GLB armature.

An OBJ, or a GLB without a compatible skin, uses the geometry/nearest-bone fallback. That is useful
for a rigid accessory. It is not a way to make an unrelated humanoid deform correctly.

## Bring the model's own skeleton and clips

The creature route ships the complete rig below `Content\Models`. Bone names, order and count are
free because nothing is retargeted. The model must contain:

- a skeleton with a single rig root;
- a skinned mesh below that root, which ContentTool imports as a `SkinnedMeshRenderer`;
- the source clips you will map to ContentTool roles.

glTF has no Unity `Animator` component. ContentTool creates one on the generated rig-root GameObject,
installs the donor's `runtimeAnimatorController` there, and substitutes your clips into its available
slots. The donor state machine remains the behavior ceiling. Read
[the clone model](../SHIPPING-A-CONTENT-MOD.md#1-understand-the-clone-model) and the
[animation contract](animation-reference.md) before choosing art.

Start with a minimal complete manifest:

```json
{
  "id": "yourname.creature",
  "bundle": "Creature.bundle",
  "scale": 1.0,
  "play": "Idle",
  "loop": "Idle, Walk",
  "creature": {
    "clips": {
      "Walk": "walk",
      "Idle": "idle",
      "Attack": "attack",
      "Death": "death"
    },
    "events": {
      "attack": "ActionDo 0.30, ShootShot 0.55, ActionEnd 0.85",
      "death": "Ragdoll 0.90"
    },
    "name": "Creature",
    "model": "creature",
    "donor": "Swarmer_TacCharacterDef",
    "up": "0,1,0",
    "lift": 0,
    "health": 40,
    "speed": 16,
    "climbPitch": 0
  }
}
```

The normal workflow begins with an empty `creature` object and lets `ct_project` write the actual
clip names. The explicit list above illustrates the final shape; never rename source clips merely to
match it. Continue with [A new creature](creature.md).

## What name matching does and does not mean

For a shipped-skeleton mesh replacement, bone names are a strict structural contract and clip names
are irrelevant because shipped clips continue playing. For a creature with its own skeleton, bone
names are free and clip names are arbitrary, but every useful clip must be mapped to a role.

Matching a downloaded clip name to a Phoenix Point clip name does not connect it to a state. The
game substitutes clip objects into slots and selects states with parameters. ContentTool refuses to
guess a role because a walk and a death are indistinguishable as raw animation data.
