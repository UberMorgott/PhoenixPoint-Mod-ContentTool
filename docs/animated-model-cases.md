# The animated-model cases — WITH an adapter and WITHOUT one, measured

> Measured 2026-08-28 on `D:\PP-Instance2`, ContentTool `build=27c7b58b`, one `ct_project` run
> (`ct_project: ALL PASS`) plus one `ct_catalog apply Sample` / `verify`. Every number below is a
> line that run printed, not an intention. The fixtures are the repo's own recorded CC0 downloads —
> `lib\u8_probe.glb` / `lib\u10_probe.glb`, Quaternius "Spider", CC0 1.0, 39 joints, 5 clips
> (`lib\u8_probe-SOURCE.md`, `lib\u10_probe-SOURCE.md`). Nothing new was downloaded.

## The short answer

**A model off the internet cannot be dropped onto a shipped rigged mesh and animate correctly
without an adapter.** Its joint names are its own; the shipped skeleton's are the game's, and the
binding is BY NAME. The two do not meet by accident.

There is a no-adapter route, and it is real — but its precondition is that the file already spells
the shipped skeleton's bone names, which in practice means the file came OUT of the game
(`ct_extract` → edit → drop back). Renaming joints inside the `.glb` to reach that state is an
**asset-side adapter**, and this document labels it as one.

**The one sentence a modder needs:** *the skeleton is never replaced, so a `.glb` binds by name and
only by name — a file whose armature was not authored against the target's own bone names needs an
adapter, either in the asset (rename its joints) or in the manifest (the creature route's role map).*

## The two cases, side by side

| | **WITH an adapter — the creature route** | **WITHOUT an adapter — a `"replace"` mesh row** |
|---|---|---|
| What it is | a NEW unit: the `.glb`'s own mesh, its own 39-bone skeleton and its own clips, cloned onto a shipped chassis | the `.glb`'s geometry over a SHIPPED `SkinnedMeshRenderer`; the shipped skeleton, controller and clips are untouched (`src\Project\ContentProject.cs:30-36`) |
| The author declares | `"creature": { "clips": {…}, "events": {…}, … }` — a role per clip and an event time per role | one row: `{ "bundle": …, "asset": …, "mesh": … }`. Nothing else |
| Why an adapter is unavoidable | glTF carries no "this is the walk" flag, and no event track at all | — |
| Bakes without it? | **no** — refused by name | **yes** — but see the binding row |
| Binds by name? | not applicable: it brings its own skeleton | **only if the file's joints are the target's bones** |

### WITH an adapter — it is adapter-bound BY DESIGN, and the refusals are in the source

- `src\Bake\ProjectBake.cs:694-697` — *"the one thing the TOOL cannot know is which of those clips is
  the walk and which is the death, because glTF has no such flag and a keyword guess on an author's
  naming would silently bind the wrong one."* So the bake **discovers, measures and writes back**,
  and the **author maps** (`:699-706`).
- `src\Bake\ProjectBake.cs:713` — declaring an empty `"creature": {}` is the whole **opt-in**; once
  opted in, `:759-761` parses the manifest and a **required role left unmapped fails the bake by
  name** (`creature-roles`, via `CreatureManifest.Missing`). A project with no `"creature"` block at
  all is not refused — it simply is not a creature (`:745-750`).
- `src\Bake\ProjectBake.cs:786-796` — the blocking animation events are **enforced per role**, and
  they are decompile facts, not policy: `attack` needs `ActionDo`, `ShootShot`, `ActionEnd`
  (`TacticalAbility.cs:1206,1214`, `BashAbility.cs:465`), `death` needs `Ragdoll`
  (`RagdollDieAbility.cs:95`). Undeclared ones are named as a `creature-events WARN`, because each
  costs a **10 s stall per action** (`AnimEventReceiver.cs:100,126`) — which reads to a player as
  the game hanging.
- **Identical bone and clip names would not help.** The engine knows the event NAMES; only the
  animation knows the TIMES (`:790`), and a downloaded file carries no times. That is the reason
  this route needs an author mapping and could not be inferred however well the names lined up.
- Proven end to end by `demos\CustomCreature` — see `VERIFIED-DEMOS.md` (19-arm `ct_creature gate`,
  re-run 2026-08-28: bash 190,0 → 130,0, spit 130,0 → 120,0, walk 3,98 tile/s, own death clip).

### WITHOUT an adapter — measured, with a control in the same run

Two `"replace"` rows against the SAME shipped bundle in the SAME bake, differing only in where the
`.glb` came from:

| Row | Source `.glb` | Bake's own rebind verdict |
|---|---|---|
| **CONTROL** `CHR_PX_ASS_TS_F_V01` ← `rigfix` | generated FROM the shipped skeleton (joints reversed on purpose, so a match cannot be a slot-order accident) | **`skinned BY NAME onto the target's own 13 bones, carrying the file's own weights`** |
| **SUBJECT** `CHR_PX_ASS_RL_F_V01` ← `foreign` | `lib\u10_probe.glb`, the **untouched download** | **`skinned nearest-bone - the file's own weights were NOT used: the file does not contain the bone 'R.UpLeg', which this model's skeleton has`** |

The gate arms behind those two lines, same run:

- `P6 PASS` on the control — *"carries the FILE's own weights on the bones it NAMES: 3 of 3 vertices
  sit at a file slot that is not the live bone index, and 1 are shared between two bones (a fraction
  nearest-bone cannot produce)"*. A shared fractional weight is the discriminator: nearest-bone can
  only ever write one full-weight influence.
- `P6 VOID` on the subject — *"the file's bone 'BackLeg2.L' is not on the shipped skeleton, so this
  replacement was refused by name and there is no by-name binding to measure"*.
- `P4 PASS` — the geometry **did** land: the copy's `CHR_PX_ASS_RL_F_V01` reads
  **`verts=5461 indices=8136 … extent=100,32.829,88.769`**, against
  `P4-ctl-shipped PASS … verts=2218 indices=10596 … extent=0.08,0.392,0.123` read off the untouched
  shipped bundle in the same run.
- `P5 PASS` — the copy is **still skinned to the SHIPPED skeleton**:
  `bindposes=11 hashes=11 rootHash=2424243207 bonesAABB=11 weightCh=stream1/off0/fmt0/dim2
  indexCh=stream1/off8/fmt10/dim2 boneMax=10 inRange=yes`, every field equal to the shipped mesh's.
  That is why the game's own controller and clips keep driving it — nothing about the animation
  changed, only which vertices each bone carries.

**The joint names, counted directly.** The shipped `CHR_PX_ASS_TS_F_V01` armature has **13** joints
(`L.UpLeg, R.UpLeg, Neck, L.Arm, L.Shoulder, R.Arm_Roll_1, R.Arm, R.Shoulder, Chest, Spine_3,
Spine_2, Spine_1, Root`). `u10_probe.glb` has **39** (`Root, Body, FrontLeg.L, FrontLeg2.L,
FrontLeg3.L, MidFrontLeg.L, …`). The intersection is **exactly one name, `Root`** — and that is a
collision of a universal word, not a match: **12 of the 13** shipped joints have no counterpart at
all. `u8_probe.glb` gives the same answer. This is not a property of these two files; it is what
"a foreign armature" means.

### So the verdict is: **NO — not for a downloaded model. ASSET-SIDE ADAPTER ONLY.**

The no-adapter route works, and the control proves it works with the file's own smooth weights on
the game's own skeleton. What it cannot do is meet a foreign armature. The bake says so in the log
rather than failing silently, and it says what to do about it in the same line: *"the skeleton is
never replaced, so in Blender keep the imported armature exactly as it came, with every bone and its
name unchanged, and re-export."*

Falling back to nearest-bone is a degradation, not a crash: one full-weight influence per vertex,
so the mesh still animates with the shipped clips — it just deforms by whichever bone was closest
instead of by what the artist painted.

## Fixes shipped after this measurement (2026-08-31)

- **Decorated bone names** (`cc9bef1`): a model ripped from a live scene carries
  `#<Bone>_Addon => <BodyPartDef>` names (`Addon.cs:143`). By-name binding found zero matches
  (intersection 0 of 10) and silently fell back to nearest-bone — this was the real root cause of
  "lost weights and positions" reported by a modder. `SkinBinder.Plain()` now normalises to the
  plain bone name.
- **Multi-parent root joints** (`109a338`): `GlbReader.Above()` refused normal PP body-part meshes
  whose addon joints hang under different parent bones. The old refusal told the modder to run
  `Apply All Transforms` — that advice was WRONG and damaged real rigs.
- **Skinless onto rigged** (`cd9f867`): now refused in both bake and live-swap paths. Nearest-bone
  welding fabricated a skin that collapsed on animation.
- **Bake resilience** (`89fcbcf`): a single refused replacement no longer aborts the rest of the
  bundle or every bundle after it.
- **Skin width preserved, not forced to 2** (`0bd7363`): ContentTool used to keep only the two
  heaviest bone influences per vertex (from its own constant `Influences = 2`) on BOTH bake paths
  and overwrite the target's channel dimension with 2. This was never a PP or Unity requirement —
  the count is per mesh (`CHR_PX_ASS_TS_M_V01_02` is dim4, `ALN_Siren_Arm_Slasher_Right` is dim2).
  Replacement now preserves the TARGET's own channel dimension (`SkinFields.InfluencesOf`, read at
  `BundleBaker.ReplaceMesh:155` before `MeshFields.Fill` clears it); add carries what the file
  actually has (`BakedSkin.Influences`, 1..4). Of 17561 vertices in one real modder's torso, 13244
  were being truncated (8152 with 3 influences, 5092 with 4). More than four is refused by name
  (Blender's "Include All" emits a second joint set the tool rejects). If a mesh still looks
  two-bone: check Blender's "Bone Influences" exporter setting and PP's "Very Low" graphics tier
  (renders at 2 regardless of mesh data).
- **Per-bone bounds accumulate all weighted influences** (`527de06`): `m_BonesAABB` now boxes every
  positively weighted influence, not just the dominant bone. Without it, a bone used only in slot 3
  or 4 got a zero or undersized box and animation could push skinned vertices outside it, culling
  or popping body parts. Zero-weight slots are excluded, so the weld path still boxes exactly one
  bone. Offline arm: `bounds … weighted=4 boxed=4 mismatched=none lastSlotOnly=bone6`.

## What this run did NOT measure

- **A soldier wearing the replaced skinned mesh, in a mission.** The evidence above that the shipped
  clips keep playing is the FILE side (`P5`: the copy's skin metadata is field-for-field the shipped
  mesh's, same 11 bindposes, same `rootHash`) plus `U9-mecanim PASS` (`Animator.Update` through a
  baked `AnimatorOverrideController` puts all 35 bones of an imported rig where the `.glb` says,
  worst error 0). Nobody has yet loaded a mission and watched a Phoenix Assault trooper walk in the
  replaced leg. Nothing in the measurement suggests it would fail — no animation event is involved
  on this route at all, which is exactly why a timeout is not a failure mode here the way it is on
  the creature route — but it is not measured, and this line says so.
- **A model whose joints were renamed to match.** That is the asset-side adapter case; it is the
  same code path as the `rigfix` control, so it is covered by construction, but no run has taken a
  real download through a rename and back.
