# Phoenix Point animation contract

Phoenix Point does not ask an actor to play a state by a string name. It selects an animation-action
definition, substitutes clips into named slots on a shared Animator controller, then drives that
controller with parameters. The practical contract has three parts:

1. the clip slot the game is filling;
2. the named animation events the clip must fire;
3. the Animator parameters that select the state.

A downloaded model's names are not part of that contract. ContentTool's `creature.clips` map is the
bridge from arbitrary source names to a smaller set of roles.

## The ContentTool role bridge

The first `ct_project MyMod` run discovers the GLB's clips and writes them into the manifest. You
assign roles; ContentTool never infers them from names.

```json
{
  "id": "morgott.demo.customcreature",
  "bundle": "CustomCreature.bundle",
  "scale": 0.008,
  "play": "Spider_Idle",
  "loop": "Spider_Idle, Spider_Walk",
  "creature": {
    "clips": {
      "Spider_Walk": "walk",
      "Spider_Idle": "idle",
      "Spider_Idle_long": "",
      "Spider_Damage": "reaction",
      "Spider_Attack_1": "attack",
      "Spider_Attack_2": "ranged",
      "Spider_Death": "death"
    },
    "events": {
      "walk": "SwarmerStep_EventDef 0.15, SwarmerStep_EventDef 0.65",
      "attack": "ActionDo 0.4054, ShootShot 0.4865, ActionEnd 0.8378",
      "ranged": "ActionDo 0.2286, ShootShot 0.5429, ActionEnd 0.9143",
      "death": "Ragdoll 0.9"
    },
    "name": "Spider",
    "model": "cyborg_spider",
    "donor": "Swarmer_TacCharacterDef",
    "ranged": "Crabman_Head_Spitter_WeaponDef",
    "up": "0,1,0",
    "lift": 2.1372,
    "health": 40,
    "will": 10,
    "speed": 16,
    "volume": 1,
    "climbPitch": 90
  }
}
```

`walk`, `idle`, `attack`, and `death` are required. `jump`, `reaction`, `ranged`, and `climb` are
optional. Missing optional combat roles fall back to the creature's own idle, never a donor clip.
The worked spider has exactly the seven clips shown and no authored climb. It deliberately leaves
`climb` unmapped; ContentTool builds start, loop, and stop traversal clips from `Spider_Walk`, while
`"climbPitch": 90` turns that gait nose-up on a wall.

`walk` and `idle` must be named in the top-level `loop`. Event entries are ordered. Do not place two
blocking events at the same fraction: the game begins waiting for the second only after the first
has returned.

The donor supplies the state machine, not its motion. ContentTool overrides every overridable donor
clip with clips from your model. A source clip that happens to share a shipped name still requires a
role mapping.

## Actor clip slots

These are field names on Phoenix Point's animation-action defs. They are the vocabulary the engine
uses even though ContentTool exposes only the reduced role set above.

### Locomotion and traversal: `TacActorNavAnimActionDef`

`ClipSequence` means `Start`, `Loop`, and `Stop`. A turn sequence has `Start`, `LeftLoop`,
`RightLoop`, and `Stop`; a one-shot turn has `Left` and `Right`.

| Slot | Shape | Use |
|---|---|---|
| `Run` | `ClipSequence` | Ordinary path movement; effectively mandatory. |
| `TurnSequence` | turn sequence | Animated turning when the actor/def requests it. |
| `Skids` | one-shot turn | Left/right skids. |
| `ClimbUpLadder`, `ClimbDownLadder` | `ClipSequence` | Ladder or full-height climb links. |
| `DropDown`, `JumpOverAndDropDown` | `ClipSequence` | Roof drops and low-obstacle roof drops. |
| `FallNoSupport`, `JetJump` | `ClipSequence` | Falling hazard and jetpack ability. |
| `Mount` | `ClipSequence` | Enter/leave a mount. |
| `MountIdle` | clip | Mounted idle. |
| `Ram` | `ClipSequence` | Mutog-style ram. |
| `RamPrepare`, `RamFinish` | clip | Ram endpoints. |
| `JumpUpOneLevel` | clip | One-level ascent. Unavailable on the Humanoid controller used by current custom creatures. |
| `JumpOverLowWall`, `JumpOverLowWallAlt` | clip | Low-wall crossing and alternate. |
| `JumpOverLowObstacle` | clip | Longer low-obstacle crossing; shipped registration is uncertain because this field is omitted from the def's `GetAllClips()`. |
| `ClimbUpLowObstacle`, `ClimbUpLowObstacleAlt` | clip | Step/climb up a low obstacle. |
| `ClimbDownLowObstacle`, `ClimbDownLowObstacleAlt` | clip | Step/climb down a low obstacle. |

The def's slot names and the controller's default clip names are different vocabularies. For
example, a nav def can name `Swarmer_dropDownStart` while the Humanoid controller reaches a state
whose default clip is `MV_ClimbDropLowStart_AR`. Replacing the first by name does not affect the
second. ContentTool classifies the controller's actual clips and maps both sides to the same clip
objects.

### Idle, stance and death: `TacActorIdleAnimActionDef`

| Slot | Use |
|---|---|
| `LowIdle`, `LowIdleAlert` | Crouched idle, normal and alert. |
| `HighIdle`, `HighIdleAlert` | Standing idle, normal and alert. |
| `LowHolsterWeapon`, `LowDrawOutWeapon` | Crouched stow/draw. |
| `HighHolsterWeapon`, `HighDrawOutWeapon` | Standing stow/draw. |
| `Death` | Die state. |

### Ranged combat: `TacActorShootAnimActionDef`

| Slot | Shape and use |
|---|---|
| `FireStart`, `ShootPose`, `FireEnd`, `ShootWaitPose` | Fire transition, shot, exit and overwatch/return-fire hold. `ShootPose` is used without a null guard. |
| `StepOutLeft`, `StepOutRight`, `StepBackLeft`, `StepBackRight` | Cover movement around a shot. |
| `PeekLeft`, `PeekRight` | `ClipSequence` cover peeks. |
| `Aim` | `ClipSequence` used when `UseAiming` is true. |
| `Turn180`, `Turn90`, `AimedTurn180`, `AimedTurn90` | Left/right one-shot turns. |
| `Reload` | Reload clip. |

The chosen shoot set is weapon-specific. That is why a new weapon should clone a shipped weapon of
the same class: tags and action defs decide how the actor holds and fires it.

### Ability, reaction and interaction slots

| Def type | Slots and selection |
|---|---|
| `TacActorAimingAbilityAnimActionDef` | `Clip` plus an `AimIKWeightCurve`; filtered by abilities, hands, sub-items and target height. |
| `TacActorSimpleAbilityAnimActionDef` | One `Clip`; filtered by ability and equipment/body parts. |
| `TacActorJumpAbilityAnimActionDef` | `ClipStart`, inherited `Clip`, `ClipEnd`. |
| `TacActorSimpleReactionAnimActionDef` | `_highReactionClip`, `_lowReactionClip`; reaction types used by the game are `Hurt` and `MindControl`. |
| `TacActorSimpleInteractionAnimActionDef` | `Self`, `OtherLow`, `OtherHigh`. |
| `TacActorSimpleItemAnimActionDef` | `Clips` as `Start`, `Loop`, `Stop`. |

Weapon prefabs have a separate item-side controller. Its container has `IdlePose`,
`DefaultActionClip`, `DefaultIdleClip`, and action rows. Item action types provide `ShootPose`;
`AimStart`, `AimIdle`, `ShootEnd`; one `Clip`; or `Self`, `OtherLow`, `OtherHigh`.

## Animation events

Phoenix Point waits for events during many actions. A missing blocking event normally looks like a
ten-second hang followed by the game continuing.

In a Unity clip, the callback function is `OnAnimEvent`; the event name is its `stringParameter`.
Extra values follow semicolons. Whitespace in that string is an error. ContentTool authors do not
edit Unity event objects directly: `creature.events` stamps the required callback and name at the
declared fraction.

| Event | Use |
|---|---|
| `ActionDo` | Frame at which a general ability effect happens. |
| `ActionEnd` | End of an action; required by general actions, bash and spawn actions. |
| `ShootShot` | Frame at which a shot or melee blow connects. |
| `Ragdoll` | Death hands the body to physics. |
| `ActionHeal` | Heal is applied. |
| `SpawnMist` | Mist is spawned. |
| `RemoveFacehugger` | Facehugger removal occurs. |
| `OpenedDoor` | A vehicle door is open during enter/exit. |
| `IKRefresh` | Recalculate IK; optional. |
| `Holster`, `DrawOut` | Weapon leaves or reaches the hand. |
| `Event` | Generic Eventus hook; extra parameters identify the event def. |

Some ability defs choose their own event names. Shipped examples include `SpawnEffect`,
`SpawnHulk`, and `ProjectileMaterialImpact`. Map only events the cloned machinery actually waits
for; inventing an event name does not create a listener.

For ContentTool's current roles, an attack normally needs `ActionDo`, `ShootShot`, and `ActionEnd`,
and death needs `Ragdoll`. Use fractions matching the actual contact and completion frames in each
source clip.

## Animator parameters

The following parameters are written directly or by the path layer. A custom controller may omit a
parameter it does not implement, but the shipped donor controller defines the behavior ceiling for a
ContentTool creature.

| Parameter | Type | Purpose |
|---|---|---|
| `Action` | trigger | Enter the action state. |
| `ActionType` | int | Select action sub-state; shipped ability defs use 0, 1 and 2. A negative `AnimType` disables action animation and waits. |
| `Reaction` | trigger | Play a reaction/flinch. |
| `Die` | trigger | Enter the death state. |
| `Alert` | bool | Select alert idle. |
| `CoverType` | int | `None=0`, `Low=1`, `High=2`. |
| `NavSpeed` | float | Playback rate for the current path segment. |
| `Preparing`, `Exhausted`, `ShieldDeploy` | bool | Status-driven states. |
| `OpenDoor`, `BreakLoop` | trigger | Vehicle door and loop termination. |
| `TravelType` | int | `None=0`, `Sprint=1`, `TurnInPlace=2`, `Climb=3`, `DropDown=4`, `Mount=5`, `Shoot=6`, `Aim=7`, `AimedTurn=8`. |
| `SprintSegmentType` | int | Run start/loop/stop, wall jumps, low climbs, skids and ram segments. |
| `TurnInPlaceSegmentType` | int | Turn start, left/right loops, stop and 90/180 one-shots. |
| `ClimbSegmentType` | int | Climb up/down start/loop/stop, jump-up and jet-jump segments. |
| `DropSegmentType` | int | Step/drop, loop, jump-over/drop and stop. |
| `MountSegmentType` | int | Start, loop, stop and idle. |
| `ShootSegmentType` | int | Cover step-out/back, shoot loop/end and overwatch waits. |
| `AimSegmentType` | int | Aim start/loop and left/right peek start/loop. |

Data-declared sub-parameters used by shipped action defs are `Action`, `ActionType`, `FireStart`,
`FireStartEnding`, `ShootEnd`, `Melee`, `Jetpack`, `JetpackStop`, and `TechnicianHeal`.

## Traversal clips and root motion

A traversal link is usable only when three things agree: its navmesh area is in the agent's mask,
the matching nav-action slot is filled, and the Animator controller has a reachable state that plays
that clip. An empty slot does not prevent route creation; the game can emit a fallback path, wait up
to five seconds for an animation that never arrives, then continue in steps.

ContentTool synthesizes three climb clips from the creature's walk: a start, a looping pure `+Y`
rise, and a stop. The engine reads height from baked root motion and repeats the loop while it lerps
across the remaining distance, so one set handles varying obstacle heights. ContentTool fills 19
usable slots across drop, ladder, low-obstacle and jump families, then adds an area only when the
controller and clips genuinely support it.

`climbPitch` rotates the model during those clips. The worked spider uses `90` because a spider's
ordinary gait reads as a wall climb when its nose points up the wall. A biped should normally use
the default `0`. The general technique is broader than this example: **when the animation you need
does not exist, rotate the model so an animation you do have reads correctly.**

If the model has real climbing art, map one source clip to the optional `climb` role. It replaces
the synthesized motion, and its root must rise; a non-rising mapped climb is refused at bake time.

The current Humanoid controller has no usable `JumpUpOneLevel` state, so ContentTool does not add
that area. `Mount`, `Ram`, `JetJump`, and `FallNoSupport` are abilities or hazards, not ordinary path
links, and remain unavailable through this synthesis.

## Rig names and sockets

The creature route ships its own skeleton. Bone names and count are free because no retargeting is
performed. The GLB needs a skeleton and skinned mesh, not a Unity component: glTF has no `Animator`.
ContentTool creates an `Animator` on the generated rig-root GameObject and installs the donor
controller there. It creates `EXT_MainContext` and `EXT_VoiceContext` if absent.

A rigged `replace[].mesh` is different: it keeps the shipped skeleton and animations. Its GLB must
have exactly the target bind-pose count and a one-to-one match of bone names. Order may differ.
ContentTool discards the GLB inverse bind matrices in favor of the shipped ones and reduces four
weights to the two strongest per vertex, so export the mesh already posed on that skeleton. Use
`ct_list bones` before editing.

Common event attachment names are `EXT_MainContext`, `EXT_VoiceContext`, `Root`,
`EXT_ShootPoint`, `EXT_AboveUnit`, and `EXT_Bleed`. Other sockets are donor-specific and must match
the event defs you clone.

## What cannot be discovered yet

ContentTool can list `AnimationClip` assets and summarize one with `ct_list clip`, but it cannot
dump an AnimatorController's layers, state names, transitions, parameters or complete overridable
clip list. It also cannot show which actor selects a listed clip or extract a clip. The exact
object-reference names embedded in shipped reaction clips remain unavailable for the same reason.

Treat the slot, event and parameter lists above as the stable authoring vocabulary. Do not invent a
controller state name from a clip asset name.
