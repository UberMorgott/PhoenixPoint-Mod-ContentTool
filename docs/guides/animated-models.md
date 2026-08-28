# Animated models — with an adapter, and without one

This is the question readers actually have, so here is the answer before the recipe.

!!! danger "The one sentence"
    **The skeleton is never replaced, so a `.glb` binds BY NAME and only by name.** A file whose
    armature was not authored against the target's own bone names needs an **adapter** — either in
    the asset (rename its joints) or in the manifest (the creature route's role map).

**A model off the internet cannot be dropped onto a shipped rigged mesh and animate correctly
without an adapter.** Its joint names are its own; the shipped skeleton's are the game's. The two do
not meet by accident.

| | **WITH an adapter** — the creature route | **WITHOUT an adapter** — a `"replace"` mesh row |
|---|---|---|
| What it is | a NEW unit: your `.glb`'s own mesh, its own skeleton and its own clips, cloned onto a shipped chassis | your `.glb`'s geometry over a SHIPPED `SkinnedMeshRenderer`; the shipped skeleton, controller and clips are untouched |
| You declare | a `"creature"` block — a role per clip, and an event time per role | one row: `{ "bundle", "asset", "mesh" }`. Nothing else |
| Why an adapter is unavoidable | glTF carries no "this is the walk" flag, and no event track at all | — |
| Bakes without one? | **no** — refused by name | **yes** — but see the binding row |
| Binds by name? | not applicable: it brings its own skeleton | **only if the file's joints are the target's bones** |

---

## Without an adapter

### What it is

One `"replace"` row against a shipped **rigged** mesh. Everything about the animation stays the
game's own — the skeleton, the controller, every clip, every animation event. Only which vertices
each bone carries changes.

Because no animation event is involved on this route at all, the ten-second stall that dominates the
creature route is simply not a failure mode here.

### 1. The folder

```text
MyRetexture\
  meta.json
  ppcontent.json
  Content\
    Meshes\
      torso.glb                 armature + WEIGHTS_0, exported from Blender as glTF Binary
  Dist\
    MyRetexture.bundle
```

### 2. The manifest

Identical to the [static mesh](meshes.md) row. There is nothing extra to declare:

```json
{ "bundle": "<shipped bundle>", "asset": "CHR_PX_ASS_TS_F_V01", "mesh": "torso" }
```

### 3. The rule that decides whether it works

**Bones are matched BY NAME** against the target's shipped skeleton, so the file's joint *order* is
free — reordering joints in Blender changes nothing. Name your armature's bones the way the SHIPPED
skeleton spells them.

- If the names match, the file's own `WEIGHTS_0` is what gets written. A vertex may be shared between
  bones and a joint **bends**. That is what an `.obj` can never express — the format carries no skin
  data at all.
- If the names do not match, the bake **falls back** to nearest-bone synthesis: one full-weight
  influence per vertex, so the mesh still animates with the shipped clips, but it deforms by whichever
  bone was closest instead of by what the artist painted. A joint creases instead of bending.

**The bake log says which one you got.** There is no silent downgrade — but there is also no error.
Read the line.

### The measured verdict for a downloaded model: it FAILS

Two `"replace"` rows against the SAME shipped bundle in the SAME bake, differing only in where the
`.glb` came from:

| Row | Source `.glb` | The bake's own rebind verdict |
|---|---|---|
| **CONTROL** | generated FROM the shipped skeleton, joints reversed on purpose so a match cannot be a slot-order accident | `skinned BY NAME onto the target's own 13 bones, carrying the file's own weights` |
| **SUBJECT** | an **untouched CC0 download** | `skinned nearest-bone - the file's own weights were NOT used: the file does not contain the bone 'R.UpLeg', which this model's skeleton has` |

Then, in the same run, the row was **refused by name** for binding purposes:

```text
the file's bone 'BackLeg2.L' is not on the shipped skeleton, so this replacement was refused by name
and there is no by-name binding to measure
```

**Count the names.** The shipped armature has **13** joints — `L.UpLeg, R.UpLeg, Neck, L.Arm,
L.Shoulder, R.Arm_Roll_1, R.Arm, R.Shoulder, Chest, Spine_3, Spine_2, Spine_1, Root`. The download
has **39** — `Root, Body, FrontLeg.L, FrontLeg2.L, FrontLeg3.L, MidFrontLeg.L, …`. The intersection is
**exactly one name, `Root`** — and that is a collision of a universal word, not a match. **12 of the
13** shipped joints have no counterpart at all. A second download gave the same answer. This is not a
property of those two files; it is what "a foreign armature" means.

**The geometry still landed, and it stayed skinned to the shipped skeleton.** In the same run the
copy read **`verts=5461 indices=8136`** against the untouched shipped **`verts=2218 indices=10596`**,
and every skin field matched the shipped mesh's — `bindposes=11 hashes=11 rootHash=2424243207
bonesAABB=11 boneMax=10 inRange=yes`. That is *why* the game's own controller and clips keep driving
it. What the file did not get was its own weights.

### So: the no-adapter route is real, and its precondition is strict

It works, and the control proves it works with the file's own smooth weights on the game's own
skeleton. What it cannot do is meet a foreign armature. In practice its precondition — *the file
already spells the shipped skeleton's bone names* — means **the file came out of the game**:
`ct_extract` → edit in Blender → drop it back.

Renaming joints inside the `.glb` to reach that state is an **asset-side adapter**, and this page
labels it as one. The bake says so in the log, and says what to do about it in the same line:

> the skeleton is never replaced, so in Blender keep the imported armature exactly as it came, with
> every bone and its name unchanged, and re-export.

### 4. Bake and package · 5. Install · 6. Discovery

Exactly as the [static mesh recipe](meshes.md#4-bake-and-package): `ct_project MyRetexture`,
`package.ps1` with the game shut, the player unzips and ticks it on, and
`"Dependencies": [ "com.morgott.ContentTool" ]` turns ContentTool on for them.

### 7. When it does not work

| Line or symptom | What it means |
|---|---|
| `skinned nearest-bone - the file's own weights were NOT used: the file does not contain the bone '<name>', which this model's skeleton has` | your armature is foreign to the target. Rename the joints, or extract the shipped model and edit that. |
| `the file's bone '<name>' is not on the shipped skeleton, so this replacement was refused by name` | same cause, stated from the other side. |
| a joint creases instead of bending | you got the nearest-bone fallback. Read the bake log — it said so. |
| the model is culled where the old one was not | your geometry left the shipped local bounds. The bounds move with the mesh; fit to the shipped AABB. |
| the bake refuses the file naming **Draco** | re-export with Compression unticked. |

### What has not been measured

**A soldier wearing the replaced skinned mesh, in a mission.** The evidence that the shipped clips
keep playing is the file side — the copy's skin metadata is field-for-field the shipped mesh's, same
11 bindposes, same `rootHash` — plus a separate arm in which the engine's own `Animator.Update`
through a baked override controller put all 35 bones of an imported rig where the `.glb` said, worst
error 0. Nobody has yet loaded a mission and watched a trooper walk in the replaced leg. Nothing in
the measurement suggests it would fail; this line exists because it is not measured.

---

## With an adapter

### What it is

A new unit, wearing your model, playing **your** clips. This is the [creature recipe](creature.md) —
follow that page for the full walkthrough. What belongs here is *why* it is adapter-bound, because
that is the part people expect to be automatable and it is not.

### The tool refuses to guess, on purpose

glTF has no "this is the walk cycle" flag. A walk and a death are the same shape of data in the file,
so the bake **discovers your clips, measures them, writes the list back into your `ppcontent.json`,
and refuses** until you map each one to a role:

```text
creature-measure 'cyborg_spider': 49 bone(s), spans 120.435 x 64.237 x 105.578 file unit(s) ...
  a tile is 1.0, so "scale": 0.008 makes it one tile across
creature-clips 'cyborg_spider': 7 animation(s) in the file -> Spider_Walk, Spider_Idle, ...
creature-scaffold: WROTE the clip list into ...\ppcontent.json - map each one to a role there.
creature-roles FAIL ... leaves 4 REQUIRED role(s) unmapped: walk, idle, attack, death.
```

A keyword guess on an author's naming would silently bind the wrong one — and a wrong guess puts an
event-less clip in the attack state, which is a ten-second stall per swing that reads to a player like
**the game** hanging.

Declaring an empty `"creature": {}` block is the whole opt-in. A project with no such block is not
refused; it simply is not a creature.

### Identical bone and clip names would not help

This is the part worth understanding. Phoenix Point does not time gameplay off clip length — it
**blocks waiting for a named event fired from inside the clip**:

| The game waits for | It gates |
|---|---|
| `ActionDo`, `ActionEnd` | every generic ability |
| `ShootShot` | the shot actually leaving the weapon |
| `Ragdoll` | the actual death |

The engine knows the event **names**. Only the animation knows the **TIMES**, and a downloaded file
carries no times. That is why this route needs an author mapping and could not be inferred however
well the names lined up:

```json
"events": {
  "attack": "ActionDo 0.4054, ShootShot 0.4865, ActionEnd 0.8378",
  "death":  "Ragdoll 0.9"
}
```

An undeclared event is named in the bake log as a `creature-events WARN`, never invented — because
each one costs a **10 s stall per action**.

### It works, and it is measured

Proven end to end: a downloaded model with its own 49-bone skeleton and its own seven clips, spawned
into a live mission, passing **all 19** gate arms — bash `Fishman_12` **190,0 → 130,0**, spit
**130,0 → 120,0**, walk **3.98 tile/s**, and its own `cyborg_spider_spider_death` clip on the kill.

**Go to the [new creature recipe](creature.md)** for the folder, the whole manifest block, the
commands, the packaging and the failure lines.
