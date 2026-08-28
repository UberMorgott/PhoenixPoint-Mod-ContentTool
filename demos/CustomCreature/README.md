# Demo mod — a creature downloaded from the internet, in your squad, with its OWN skeleton and its OWN animations

**Start a new campaign and the spider is standing in your aircraft.** It is a CC BY 4.0 model
downloaded from Sketchfab untouched, its 49-bone skeleton is the one that came inside the file, and
the clips it plays while it walks, shoots, flinches and dies are the clips that came inside the
file — driven by Phoenix Point's own Mecanim, its own anim-action defs and its own path processor.

> **This is a SEPARATE MOD.** It installs as `Mods\CustomCreature\` and the mod manager lists it as
> **ContentTool Demo: Custom Creature**. It requires the **ContentTool** mod — `meta.json` declares
> `"Dependencies": [ "com.morgott.ContentTool" ]`, so enabling this one enables ContentTool too.
> Switch it off in the mod manager and it does not load at all.

```text
CustomCreature\
  ppcontent.json               THE FILE YOU EDIT — model, clip->role map, event times, stats, scale
  Content\Models\cyborg_spider.glb   the download, 1 481 244 B, UNMODIFIED — see SOURCES.md
  src\CustomCreatureMain.cs    36 lines: build the creature, put it in the squad. That is the mod.
  tools\check-bridge.ps1       offline check: every clip slot is a real field on a real def
  tools\ClipEvents             measure a clip's reach curve, print the attack event line to paste
  SOURCES.md                   "Cyborg Spider" by SpiderBight, CC BY 4.0 — attribution REQUIRED
```

**The mechanism is not in this folder.** Cloning the donor, remapping the controller, wiring the clip
slots, stamping the animation events, orienting the rig — every creature mod would copy that
verbatim, so it lives in the ContentTool engine (`src\Tactical\CreatureBuild.cs`) and this demo just
calls it. What is left here is the two things that are genuinely a CHOICE: the numbers in
`ppcontent.json`, and the decision to put the unit in the player's starting squad.

## Run it

```text
deploy.ps1                      installs this folder to <Phoenix Point>\Mods\CustomCreature\
enable it in the mod manager    Dist\CustomCreature.bundle SHIPS built - no bake, no console command
main menu -> NEW CAMPAIGN       the spider is the last unit in the starting squad
```

Measured 2026-08-28 on a download-shaped install (shipped files only, `ct_project` never run): the
creature spawned into a live mission and passed all 19 `ct_creature gate` arms — bash 190 → 130,
spit 130 → 120, walk 3.98 tile/s, its own `cyborg_spider_spider_death` clip on the kill. The bake
below is the AUTHOR's step, for when the model or `ppcontent.json` changes.

### Doing it with YOUR model — the whole workflow

1. **Drop the `.glb` in** `Content\Models\`.
2. **Declare a creature**: put `"creature": {}` in `ppcontent.json`. That empty block is the opt-in;
   a project without one is a texture/sound project and nothing below happens to it.
3. **Bake once** — `ct_project <YourMod>`. The tool reads your file and *writes back into your
   `ppcontent.json`* every animation it found, then tells you what it measured and **refuses**:

   ```text
   creature-measure 'cyborg_spider': 49 bone(s), spans 120.435 x 64.237 x 105.578 file unit(s) ...
     a tile is 1.0, so "scale": 0.008 makes it one tile across
   creature-clips 'cyborg_spider': 7 animation(s) in the file -> Spider_Walk, Spider_Idle, ...
   creature-scaffold: WROTE the clip list into ...\ppcontent.json - map each one to a role there.
   creature-roles FAIL ... leaves 4 REQUIRED role(s) unmapped: walk, idle, attack, death.
   ```

   It refuses rather than guesses on purpose. glTF has no "this is the walk cycle" flag, so a walk
   and a death are the same shape of data in the file — and a wrong guess puts an event-less clip in
   the attack state, which is a ten-second stall per swing that reads like the **game** hanging.
4. **Fill in the roles** beside each clip name, and while you are in there set the numbers you care
   about. All of it is in that one block — see below.
5. **Bake again, and play.** Re-baking never churns the file: a role you already filled in survives,
   a clip you added arrives blank.

The bake step is once-per-model, not once-per-run: `ct_project` writes `Dist\CustomCreature.bundle`
next to `ppcontent.json` and the engine loads that file at mod init. Until it exists the mod says so
in the log and changes nothing:

```text
ct_creature VOID '...\Dist\CustomCreature.bundle' does not exist - run `ct_project CustomCreature`
```

Nothing in the game install is touched, ever — no `apply`, no `revert`, no Catalog.json edit.

### The one file, in full

```jsonc
"scale": 0.008,                  // file units -> game units. The BAKE reads this too (root motion),
                                 // so it is one number, at the top level, not two that must agree.
"creature": {
  "clips":  { "Spider_Walk": "walk", "Spider_Idle": "idle",     // roles: walk, idle, attack, death
              "Spider_Attack_1": "attack", "Spider_Death": "death",
              "Spider_Damage": "reaction",                       // reaction = optional flinch
              "Spider_Idle_long": "", "Spider_Attack_2": "" },   // unmapped — earmarked for later
  "events": { "attack": "ActionDo 0.4054, ShootShot 0.4865, ActionEnd 0.8378",
              "death":  "Ragdoll 0.90" },   // WHERE in the clip, as a fraction of its length
  "name": "Spider",
  "donor": "Swarmer_TacCharacterDef",       // donor = the shipped unit to clone structure from.
                                            // PICK A ONE-TILE ONE. See "Choosing a donor" below.
  "up": "0,1,0", "lift": 2.1372,            // the model's up axis, and how far its origin sits
                                            // above its lowest vertex (a centred model needs this
                                            // or it stands in a hole half its own height deep)
  "health": 40, "will": 10, "speed": 16, "volume": 1,
  "pace": 5.4284                            // OPTIONAL. tiles/second the creature travels at.
                                            // Omit it and you get this, the shipped pace. See
                                            // "Speed is baked into the clip" below.
}
```

`"events"` is the one thing the bake will not fill in for you. The engine knows the event **names**
the game blocks on — they are hard facts of the decompile — but only the animation knows the
**times**, and a `ShootShot` stamped on the wrong frame is damage on the wrong frame. An undeclared
one is named in the bake log as a `creature-events WARN`, never invented.

**`tools\ClipEvents`** takes the guesswork out: `ClipEvents <model.glb> [clipName]`. With no clip
name it lists every clip and its length; with one it prints the per-frame reach curve and the
measured `"attack": "ActionDo <a>, ShootShot <b>, ActionEnd <c>"` line to paste straight into
`ppcontent.json`. It works by measuring, per frame, how far the furthest bone has travelled from its
frame-0 position along the clip's own principal axis of motion — the peak of that curve is the
strike. For this model it measured `Spider_Attack_1` peaking on bone `lapa_1_R_4_044` at **0.4865**
of the clip, giving the demo's declared `"attack": "ActionDo 0.4054, ShootShot 0.4865, ActionEnd
0.8378"`. It refuses rather than reporting a number when no clear peak exists.

Everything else is optional and falls back to something measured or to the donor's own value.

---

# The wiring — how a downloaded creature's clips reach the game's animations

This is the part worth reading. Everything below is grounded in the decompile with `file:line`. The
code that acts on it is the ContentTool engine's `src\Tactical\CreatureBuild.cs`, which carries the
same map in its comments — **not** this demo, which only supplies the data.

## 1. An actor is a list of component defs

A playable unit is a `TacCharacterDef`. Everything about it hangs off `Data.ComponentSetTemplate`,
a `ComponentSetDef` — which is nothing but a flat `ObjectDef[]` looked up **by type**
(`Base.Core\ComponentSetDef.cs:19-29`). Stats, AI, navigation, body state, abilities, visuals and
animations are all just entries in that list.

A custom creature therefore does not need a new *kind* of anything. It needs **two** of those
entries swapped:

| entry | decides |
|---|---|
| `AddonsComponentDef` → `AddonsManagerDef` | what **mesh and skeleton** the actor wears |
| `TacActorAnimActionsDef` | what **clip** plays for walking, shooting, idling, dying |

This demo clones a shipped non-humanoid unit and repoints exactly those two.

### Choosing a donor — pick a ONE-TILE unit

This demo used to clone the **Mutog**, and every one of the following was inherited in silence:

| what the Mutog brought | where it comes from | what the player saw |
|---|---|---|
| `Mutog_DemolitionComponentDef` | its `ComponentSetDef` | a tiny spider smashing every wall it passed |
| `AgentType: "MedMonster"` | `Mutog_NavigationDef` | a multi-tile footprint and a fat path preview |
| `Move3x3_AbilityDef` | its `TacticalActorBaseDef.Abilities` | a 3×3 move ability on a 1×1 creature |
| `AgentRadius >= 1f` | Unity's NavMesh agent types | a turn-in-place demanded on every move order |

None of that is a *tag* and none of it is fixable by stripping tags — crushing is a **component**
(`TacticalDemolitionComponent.cs:75` subscribes to `ActorMovedEvent`, `:216` `ApplyDamage`s whatever
it sweeps), and the footprint is one **string**, the nav `AgentType`. The engine now drops the
demolition component from every creature it builds and re-points the AgentType at a shipped one-tile
unit, so a bad donor is survivable — but the honest fix is to start from the right unit.

**`Swarmer_TacCharacterDef`** is the default because it is the smallest shipped unit that carries
everything the clone *requires*: `AgentType "Humanoid"`, an `AddonsManagerDef` with a
`SkeletonChassisAddonDef`, a `TacActorAnimActionsDef`, and a bodypart that is a melee `WeaponDef`
with a `BashAbilityDef` on it (`Swarmer_Torso_BodyPartDef` → `BashStrike_AbilityDef`) — which is
where your creature's attack comes from. A `Facehugger` looks like a better fit until you notice its
`BodypartItems` is empty, and a creature with no bodypart weapon can never attack.

`"donor"` accepts a **def name** (the normal case) or one of the two `SharedGameTags` tag fields that
exist. It used to accept only the tag form, on the reasoning that a tag survives patches better than
a name — sound, but `SharedGameTagsDataDef` carries exactly two per-species tags, `MutogTag` and
`MutoidTag`, and nothing for the other ~690 characters. A tag-only key could only ever name a Mutog.

## 2. The rig is one `GameObject` field

```csharp
// PhoenixPoint.Common.Entities.Addons\AddonsManager.cs:112-120
RigRoot = UnityEngine.Object.Instantiate(AddonsManagerDef.Rig, RigRootContainer).transform;
RigRoot.ResetTransform();
RootMotionNode = RigRoot.FindTransformInChildren(AddonsManagerDef.RootMotionNodeName);
```

`AddonsManagerDef.Rig` is a plain `GameObject` reference, instantiated as-is. Then
`TacticalActorBase.SetupAnimator` takes the **first Animator among those children**
(`TacticalActorBase.cs:586-597`). So the prefab ContentTool bakes — root + 49 bones +
`SkinnedMeshRenderer` + `Animator` — drops straight into that field, and *that is the entire answer*
to "how do I get my model into the game".

Three things that bite:

- **The Animator must be on the rig ROOT, not on a child.** The two code paths disagree about how
  hard they look, and only one of them is forgiving:

  ```csharp
  // TacticalActorBase.SetupAnimator:588 — tolerant
  Animator = GetComponentInChildren<Animator>();
  // CommonCharacterUtils.DisplayCharacter:42-43 — strict, and no null check
  Animator c2 = charBuilder.AddonsManager.RigRoot.GetComponent<Animator>();
  c2.runtimeAnimatorController = addonsManagerDef.Rig.GetComponent<Animator>().runtimeAnimatorController;
  ```

  An earlier version of this mod wrapped the prefab in one extra transform (to survive the reset
  below). That made `RigRoot.GetComponent<Animator>()` null and the next line a
  `NullReferenceException` — and the symptom was **"the roster lists a spider and shows no model"**,
  with every other gate green. The prefab is now the rig root, unwrapped.
- **`ResetTransform()` erases the root.** Line 115 above, and again at `TacticalActorBase.cs:539` on
  every enter-play. A rotation or scale baked onto the rig root is thrown away — so setting it on the
  prefab is dead code. The correction is re-applied on the live object instead, in the one seam that
  runs after every reset on both paths (`CheckInstance`, behind the `TacActorAnimActions.Setup`
  postfix).
- **`RootMotionNodeName` must name a real node.** The engine now **derives** the root-motion node
  rather than looking for one literally called `"Root"` — it is the rig's one parentless bone (the
  same bone `Treadmill` writes the walk ramp on), so the two cannot disagree. This rig's root is
  `_rootJoint`; the old model's was `Root`. Getting it wrong means the game measures travel off the
  ramp bone's PARENT, which never moves: `AnimationInfos.cs:105` measures the motion point in the
  animated object's own local space, so it reads 0 and `TacticalNavigationComponent.cs:248` reports
  "this segment does not move the actor" — the creature walks on the spot. Bake log line:
  `ct_creature PASS root-motion node '_rootJoint'`.

## 3. Two ways, and missing the second one is what broke turn, idle and death

> **Correction.** An earlier version of this document said the game never names a state and never
> fires a trigger. That is true of *continuous* animation and **false of one-shots**, and believing
> it cost a round of "the spider walks but will not turn, idle or die". Both mechanisms are below.

**(a) Continuous — walking, idling.** One `AnimatorController` per creature family, whose clips are
rewritten **inside** it at runtime:

```csharp
// PhoenixPoint.Tactical.Entities\TacticalActor.cs:724-726
AnimatorOverrides = AnimatorClipOverrides.CreateAnimatorClipOverrides(Animator);
ActorAnimActions = GetComponent<TacActorAnimActions>();
ActorAnimActions.Setup(AnimatorOverrides, this);
```

`CreateAnimatorClipOverrides` wraps whatever controller the Animator carries in a fresh
`AnimatorOverrideController` and re-assigns it (`Base.Utils\AnimatorClipOverrides.cs:136-158`). From
that moment the state machine is frozen — only the clip each state plays ever changes.

**Consequence for us:** we cannot author a state machine offline, and we do not have to. But we DO
need a controller that *has* the states an actor uses. The one ContentTool bakes is an
`AnimatorOverrideController` over `_common`'s one-state `MedKitHeartBeat1` — enough to prove a clip
plays (gate U9), useless as an actor. The game's own controller for this creature family is sitting
on the donor's rig prefab, so `BuildRig` simply takes it:

```csharp
ours.runtimeAnimatorController = theirs.runtimeAnimatorController;   // donor rig's Animator
```

No bake change, no state machine authored, every state present.

**(b) One-shots — death, reactions — are states reached by TRIGGER, and never read a clip field:**

```csharp
// PhoenixPoint.Tactical.Entities.Abilities\RagdollDieAbility.cs:92-95
Animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
Animator.ResetTrigger("Reaction");
Animator.SetTrigger("Die");                                   // <- a STATE, by name
yield return Timing.Call(AnimEvents.WaitForEvent("Ragdoll")); // <- gated on a clip EVENT
```

…plus bools and ints on the same Animator: `"Alert"` and `"CoverType"` (`IdleAbility.cs:101-111`),
`"TravelType"` (`TacticalActorBase.cs:1109`).

**`ActiveIdleClips.Death` is read at `RagdollDieAbility.cs:102` only to hunt for a `"Voice"` event —
it is never played.** So filling the `Death` *slot* cannot make a death animation appear. The clip
has to be inside the controller's **Die state**.

### Is death just ragdoll? No — and that matters

`:90-99` splits two ways. On a **small** impact (or no rigidbody) the game plays the Die state and
waits for `"Ragdoll"` **before** applying any force — a death *animation* is expected, and only then
does physics take over. The pure-physics path is the other branch, taken on a big hit. So the honest
answer is not "document that there is no death animation"; it is to make the spider do what the donor
does: play a clip, and fire `"Ragdoll"` from inside it (which `StampEvents` does — §5).

### Why walk worked and the rest did not

The def-level slots of §4 are only ever **keys into** the controller. `TacActorAnimActions` captures
the *default* action's clips as `_clipKeys` (`TacActorAnimActions.cs:52-54`) and then calls
`ApplyOverrides(_clipKeys, ConcatAllActiveClips())` (`:79`). The override is **dropped** when the key
is null (`AnimatorClipOverrides.cs:27-31`, *"Trying to assign clip … to null key."*), and Unity
silently ignores a key that is not a clip inside the controller.

So our clip reaches the screen only where the donor's default action happens to hold the very clip
that state plays. **Walk's key matched. The others' did not** — one null or mismatched donor field
and that state keeps playing a Mutog clip, which names no bone of our rig: frozen.

### The fix: the bridge at the layer that actually decides

Override **every clip the controller contains**, by name, once, when the game hands us the override
table (`CreatureBuild.RoleForVanilla` + a single Harmony postfix on `TacActorAnimActions.Setup`). No
dependency on which donor field is null, and it covers trigger-reached states the def table can never
touch.

Note **which** names are matched here: the **donor's**, never yours. Your clips are named by the
`"clips"` map in `ppcontent.json`; the shipped controller's are whatever that creature family happens
to call them, and are not knowable offline. So the keyword rule classifies a *vanilla* name into one
of the manifest's **roles**, and the role then picks your clip:

| vanilla clip name contains the token… | role |
|---|---|
| `die`, `death`, `dead` | `death` |
| `turn`, `skid`, `rotate` | `walk` — turning is locomotion |
| `shoot`, `fire`, `attack`, `melee`, `bash`, `aim`, `reload`, `peek` | `attack` |
| `reaction`, `flinch`, `hit`, `damage` | `reaction` — optional flinch on taking damage |
| `jump`, `jet`, `leap` | `jump` |
| `walk`, `run`, `move`, `step`, `climb`, `fall`, `drop`, `land`, `mount`, `ram` | `walk` |
| anything else | `idle` — never nothing |

Matched on **tokens**, not substrings: `Soldier_Idle` *contains* `die` (sol-**die**-r) and a naive
`Contains` calls it a death animation. `tools\check-bridge.ps1` invokes the real shipped method on
that exact case and nine others.

**The one exception is the Action state**, and it is not a keyword at all. One clip in a shipped
controller is called `HL_ActionPlaceholder` — a name that says nothing about what it is for, so the
rule above files it as an idle, and an idle carries no `ShootShot`/`ActionDo`/`ActionEnd`: every
action then waits out three ten-second timeouts. The game does not guess it either, it **names** it
(`TacActorAnimActionsDef.DefaultActionClip`), so it is taken from there by identity.

**The `reaction` role** is the flinch that plays when the actor takes damage. The game has always
driven it — `TacticalActor.cs:1627-1633` asks `GetReactionAnimation`, writes the answer over
`TacActorAnimActionsDef.DefaultReactionClip` in the override controller, then fires
`SetTrigger("Reaction")`; the clip comes off a `TacActorSimpleReactionAnimActionDef`
(`TacticalActor.cs:1597-1601`). The role is **optional**: a creature that maps no reaction clip has
the idle substituted rather than keeping the donor's (a donor clip names none of our bones, so the
actor would freeze). This model maps `Spider_Damage` to it.

## 4. Which clip plays: `TacActorAnimActionsDef`, and the default/override rule

`TacActorAnimActionsDef` holds a list of anim **actions**. Each is a typed bag of clip fields plus a
`Match(context)`:

| action type | the clip fields that matter here | file |
|---|---|---|
| `TacActorIdleAnimActionDef` | `LowIdle`, `HighIdle`, `LowIdleAlert`, `HighIdleAlert`, **`Death`** | `TacActorIdleAnimActionDef.cs:20-37` |
| `TacActorNavAnimActionDef` | **`Run{Start,Loop,Stop}`**, `JetJump`, climbs, drops, turns | `TacActorNavAnimActionDef.cs:19-62` |
| `TacActorShootAnimActionDef` | **`FireStart`, `ShootPose`, `FireEnd`**, `Aim`, `Reload`, turns | `TacActorShootAnimActionDef.cs:44-76` |

**The rule — getting it backwards is the classic mistake.** Exactly one action of each type carries
`IsDefaultAnimatorClips = true`, and *its* clips are the **KEYS**: they are literally the clips
sitting inside the shipped controller. Every other action is a **VALUE SET**, swapped in positionally
— index by index over `GetAllClips()` — when its `Match` succeeds:

```csharp
// PhoenixPoint.Tactical.Entities.Animations\TacActorAnimActions.cs:52-53, 70-79
ActiveIdleClips       = GetAnimAction<TacActorIdleAnimActionDef>(new DefaultAnimActionContext());
ActiveNavigationClips = GetAnimAction<TacActorNavAnimActionDef>(new DefaultAnimActionContext());
_clipKeys = ConcatAllActiveClips();                       // <- the DEFAULT action's clips
...
ActiveIdleClips = GetAnimAction<TacActorIdleAnimActionDef>(shootContext);   // <- the match
_animatorOverrides.ApplyOverrides(_clipKeys, ConcatAllActiveClips());
```

That runs from `TacticalActor.cs:1439` the moment the actor's equipment is selected — so it fires for
every actor, weapon or not.

**So, to bind `Spider_Walk` to "what plays while this unit walks":**

> Put `Spider_Walk` into the `Run.Loop` field of a **non-default** `TacActorNavAnimActionDef`, and
> leave the default action untouched so its clips stay usable as override keys.

**The clips are never renamed, and there is no hand-kept slot table.** There used to be one — 14
rows of "this vanilla slot gets that clip of ours" — and keeping it in step with the controller was
the source of two separate bugs. It is now **one rule**, applied to every slot:

> A slot gets the clip the controller will actually play in **that slot's state**, which is
> `RoleForVanilla(theDonorClipThatWasInTheSlot)`. A slot the donor left **empty stays empty**.

That is consistent with the controller remap of §3 *by construction*, because both are keyed on the
same vanilla clip name — so the navigation wait can never end up blocking on a clip the state does
not contain. Re-download the model with different clip names and nothing here changes at all: only
the `"clips"` map in `ppcontent.json` does.

The slots themselves are walked **by reflection on the real field names**, including the ones nested
one level down inside a plain holder (`Run.Start/Loop/Stop`, `TurnSequence.*`, `Skids.*`), so a field
a game patch renames becomes unreachable rather than silently doing nothing.
`tools\check-bridge.ps1` asserts all 20 against the shipped `ModSDK\Assembly-CSharp.dll` offline,
with a falsification arm and an anti-vacuity arm for the nested ones.

### Slots with no row: why "leave the donor's clip" was wrong

The table names 14 slots. The three action types have **79** between them, and the first in-game run
found what happens to the other 65 — most visibly: *the spider slid round on the spot instead of
turning.*

Turning is **not opt-in**. The game asks whether this actor turns, and answers from the slots:

```csharp
// PathProcessorUtils.cs:316-319 / 306-314 / 321-328
UsesFullTurnSequenceAnims    = TurnSequence.HasAllAnimations          // Start && LeftLoop && RightLoop && Stop
UsesPartialTurnSequenceAnims = TurnSequence.LeftLoop && RightLoop
UsesTurnAnimations           = either
// TacticalPathProcessor.cs:196
if (PathProcessorUtils.UsesTurnAnimations(Actor)) { ...turn in place... }
```

The donor filled those slots, so the answer was yes, and the game dutifully played a **Mutog** turn
sequence on a **spider** rig. Those curves name no bone of ours, so the mesh held its pose while the
actor rotated underneath it. *"Leave the donor's clip" does not mean "play nothing" — it means "play
something that cannot move our bones",* which is strictly worse than using one of our own clips.

### …and then filling them was wrong too

The first fix was to give **every** slot one of our clips, on the reasoning above. That produced the
opposite bug, and it is the more instructive one: **a filled slot is a CLAIM, not a picture.**

Filling `TurnSequence.LeftLoop`/`RightLoop` told `UsesTurnAnimations` that this creature turns on the
spot. The donor's controller has a state for that; **ours does not** — a downloaded `.glb` has walk,
idle, attack, death and jump, and no turn-in-place clip at all. So the engine emitted
`TravelType.TurnInPlace` points, `TacticalNavigationComponent` blocked waiting for the animator to
reach a state it could never reach, and the move never started:

```text
Mutog_6 is waiting for animation spider_spider_walk timed out. Current animation: spider_spider_idle
```

Every bash began the same way, because `BashAbility` faces its target first — measured, the arm sat
there for its whole 45 s while `UpdateAimIK` spun.

So the rule is the one stated above, and the `TurnSequence` family is **cleared** rather than
mirrored. The honest answer is that this creature does not turn in place: `FaceIn3d` falls through to
`NoAnimsFace` and it lerps round in a few frames. A spider walking sideways on the spot would be
worse than a spider that pivots.

**The lesson generalises past turning.** Any optional sequence — `Skids.*` and the rest — is the game
asking the def a yes/no question about what this creature can do. Answer it honestly, or navigation
blocks on an animation that can never play.

`tools\check-bridge.ps1` prints the whole slot inventory and asserts the nested sequences are
reachable by the same reflection walk, so a refactor that silently stops seeing them fails the check.

## 5. The other half of the bridge: ANIMATION EVENTS (the one that will bite hardest)

A clip that plays is only half of it. **Phoenix Point does not time gameplay off clip length — it
blocks waiting for a named event fired from inside the clip:**

| the game waits for | where | what it gates |
|---|---|---|
| `"ShootShot"` | `TacticalLevelController.cs:1814` (`AnimEventQueue`) | the shot actually leaving the weapon |
| `"ActionDo"`, `"ActionEnd"` | `TacticalAbility.cs:1206,1214` | every generic ability |
| `"Ragdoll"` | `RagdollDieAbility.cs:95` | the actual death |
| `"ShootShot"` → `"ActionEnd"` | `BashAbility.cs:465,498` | melee |
| `"Holster"`, `"DrawOut"` | `EquipmentComponent.cs:38-39` | weapon in/out of hands |

A downloaded clip carries none of these, and **ContentTool cannot bake them** — `src\Bake\
ClipFields.cs` has zero occurrences of "event" and never writes `m_Events`. That is a real ceiling,
and it is the classic "it moves but nothing happens".

**The failure mode is precise, and it is not a hang.** `WaitForEvent` is timeout-bounded at 10 s
(`AnimEventReceiver.cs:100,108,116-127`): the coroutine stalls, logs *"the event is likely missing
from the animator"*, and continues. So the logic still fires — ten seconds late, every action.
Unplayable, not fatal. (The engine's own escape hatch for this is
`AnimEventReceiver.SkipNextWaitForEvent` at `:41,103-107`, used by the game at `TacticalActor.cs:1622`.)

So the mod stamps the events on at load with Unity's own `AnimationClip.AddEvent`, in the shape the
game reads (`AnimEventReceiver.cs:49-52,54-88`): **one function name for all of them —
`OnAnimEvent` — with the event's real name in `stringParameter`.** No whitespace in that string
(`:66-80` rejects it), never empty (`:56-64`).

| our clip | events stamped on |
|---|---|
| `Spider_Attack_1` | `ActionDo` @ 0.4054, `ShootShot` @ 0.4865, `ActionEnd` @ 0.8378 |
| `Spider_Death` | `Ragdoll` @ 0.90 |

The attack times come from `tools\ClipEvents`, which measured `Spider_Attack_1` peaking on bone
`lapa_1_R_4_044` at 0.4865 of the clip — the frame where a leg reaches farthest. A real creature
mod puts the hit frame on the frame that looks like a hit; this tool gives a grounded starting
point. The proper long-term fix is for the **bake** to emit events from the project file, so the
prefab is event-complete before it ever reaches a mod.

## 5a. A stray non-skinned mesh is no longer fatal

`GlbReader` used to refuse any file with more than one mesh and tell the author to join the pieces
in Blender — useless advice when the second "mesh" is a 2-triangle camera backdrop. This file has
exactly that: `Spider_Spider_M_0` (1 226 verts, 1 552 tris, skinned) and
`pPlane1_Camera_lambert2_0` (4 verts, 2 tris, **no skin**). The reader now picks the mesh a SKIN
drives (the definition of the model for a creature) and NAMES the meshes it dropped. If that does
not single one out — two skinned meshes, or a static file with several — the original refusal
stands unchanged.

## 5b. Clips can share ONE timeline — and importing them raw breaks everything

This file lays its 7 clips end to end on a single 13.83 s reel with absolute key times:
`Spider_Walk` 0.3333..1.1333, `Spider_Idle` 1.3333..3, `Spider_Idle_long` 3.3333..8.3333,
`Spider_Damage` 8.6667..9.5, `Spider_Attack_1` 9.6667..10.9, `Spider_Attack_2` 11.3333..12.5,
`Spider_Death` 12.6667..13.8333. ContentTool now lifts each clip off that reel to its own zero and
says so in the bake log ("lifted off the file's shared timeline at N s"). Before it did, the death
clip imported as 13.83 s of which 12.67 s was a frozen pose, and the attack ran 10.9 s — past the
gate's 9 s stall threshold. A single-clip file rebases to itself and is unaffected, which is why
the first model never showed this.

## 6. Locomotion speed comes from the clip's ROOT MOTION, not from a number

The one that will surprise you. The game measures every clip by **sampling it on the actual object**
and reading how far the root-motion node travelled:

```csharp
// PhoenixPoint.Common.Core\AnimationInfos.cs:99-123
Transform motionPoint = GetMotionPoint(animatedObj);          // = AddonsManager.RootMotionNode
clip.SampleAnimation(animatedObj, 0f);        Vector3 a = ...InverseTransformPoint(motionPoint.position);
clip.SampleAnimation(animatedObj, clip.length); Vector3 b = ...InverseTransformPoint(motionPoint.position);
offset = b - a;   Speed = offset.magnitude / clip.length;   IsLooping = clip.isLooping;
```

There is no table of shipped clips to be missing from — **it works on any clip, ours included**. But
it means a downloaded walk cycle that animates **in place** measures `Speed == 0`. Most free models
animate in place. The honest consequence is in "Ceilings" below.

Note also `IsLooping = clip.isLooping` — read straight off the clip. That is why `ppcontent.json`
declares `"loop": "Spider_Idle, Spider_Walk"`: glTF carries no loop flag, so ContentTool cannot infer
it, and an un-looped run loop plays once and freezes.

### `"pace"` — speed is baked into the clip, so the clip is what gets retimed

Read the formula above once more: `Speed = offset.magnitude / clip.length`. There is **no def field
anywhere in this game that sets a unit's movement speed** — `TacticalActorBaseDef` has none,
`TacticalNavigationComponentDef.cs:12-34` has none, and `MoveAbilityDef.cs:10-12` is an empty class.
So whatever pace your downloaded animator happened to walk at *is* the pace your creature moves at.

This spider's own cycle measures **1.986079 tile/s** — its walk clip genuinely travels, unlike the
previous model which animated in place (~0). The shipped soldier's run loop `MV_RunFwd_Loop_AR`
measures **5.4284 tile/s** the same way (2.894980 units over a 0.5333 s cycle; `TacticalMap.cs:67`
`TileSize = 1f`). About 2.7× slower, which in game reads as a cautious creep rather than a sprint.

There are two ways to raise a measured speed and **only one of them keeps the feet on the ground**:

- *stretch the ramp* — the body covers more ground while the legs cycle at the old cadence. That is
  foot sliding.
- *compress the timeline* — travel per cycle unchanged, legs and ground both `k` times faster. The
  planted foot stays planted, because a uniform retime cannot pull apart two things already in step.

The bake does the second, to `"pace"` tiles/second, and only to the clip you mapped to the `walk`
role (an attack's rate is set by the `ShootShot` frame you measured; retiming it would move the hit).

`"pace"` is **optional** — leave it out and you get the shipped 5.4284. Set it lower for something
that should lumber, or `0` to keep your clip's own authored speed. Watch the bake log line, because
a short stride has to scurry to hold the game's pace:

```
clip 'Spider_Walk' pace: 1.986079 -> 5.4284 tile/s, so the clip plays x2.733224
  (0.8 s -> 0.292695 s per cycle = 3.416476 cycle(s)/s).
  The legs and the ground speed up together, so nothing slides.
```

The old model needed **×10.65** retiming (12.78 cycles/s — a blur) because its walk animated in
place and measured ~0. This model's walk clip genuinely covers ground, so **×2.73** is enough and
**3.42 cycles/s** looks natural.

**This is not the same key as `"speed"`,** and it cannot be. `"speed"` is `Data.Speed`, which
`CharacterStats.cs:301-302` spends as `ActionPoints.Max` — how **far** the unit gets in a turn.
`"pace"` is how **fast** it crosses a tile. Every unit in this game shares one pace and differs in
range, which is exactly why one number could not serve for both.

## 7. Joining the starting squad is one array

```csharp
// PhoenixPoint.Geoscape.Levels.Factions\GeoPhoenixFaction.cs:1964-1976
foreach (TacCharacterDef template in currentDifficultyLevel.StartingSquadTemplate) {
    GeoUnitDescriptor d = _level.CharacterGenerator.GenerateUnit(this, template);
    ...
    geoVehicle.AddCharacter(d.SpawnAsCharacter());
}
```

`GameDifficultyLevelDef.StartingSquadTemplate` is a plain `TacCharacterDef[]`
(`GameDifficultyLevelDef.cs:37`), so appending our def to it looks like the whole hook.

**It is not, and this cost a whole round of "the spider is gone".** There are **two** squad builders:

```csharp
// GeoscapeTutorial.InitSquad:313-323 — the one that runs when the tutorial is on
int num2 = currentDifficultyLevel.StartingSquadTemplate.Length - num;   // our array: LENGTH only
for (int i = 0; i < num2; i++) {
    GenerateUnit(_level.PhoenixFaction, AdditionalSoldierTemplate);     // one FIXED human template
    ...
}
```

With the tutorial on, the squad comes from the tutorial's own unit results, topped up from **one
fixed human template**. Our array is read for its `Length` and nothing else — so the append did not
add a spider, it added **one extra soldier**.

So the spider joins **after** whichever builder ran: a postfix on `GeoPhoenixFaction
.CreateInitialSquad` *and* one on `GeoscapeTutorial.InitSquad`, both calling `JoinSquad`, which adds
the character and then **reads the aircraft back out** to prove it is aboard.

Two more things that silently swallow a unit here:

- **`AddCharacter` never refuses.** `GeoVehicle.cs:759-764` computes the space sum and *throws it
  away*. "We called Add" is not evidence of anything — hence the read-back.
- **Volume.** A Mutog occupies **three** unit slots (`GeoVehicle.cs:97-99` sum `TemplateDef.Volume`).
  Five soldiers plus a volume-3 spider overflows a six-slot Manticore and the roster has nowhere to
  draw it. The clone sets `Volume = 1`.

## 8. Cloning a def properly

`DefRepository` offers two factories and **the difference is a bug you only see an hour into a
campaign**. Both are `Object.Instantiate` plus a `Guid` plus registration
(`Base.Defs\DefRepository.cs:214-276`); a bare `ScriptableObject.CreateInstance` is neither, and
leaves `Guid` and `ResourcePath` null, which other mods read.

- `CreateRuntimeDef(original)` — **swept**. Returning to the geoscape destroys every runtime
  `TacCharacterDef` (`GeoLevelController.cs:185,750-753`); ending a mission destroys every runtime
  `ComponentSetDef` (`TacticalLevelController.cs:131-137`); loading a save destroys them all
  (`PhoenixSaveManager.cs:370`). Our entry in `StartingSquadTemplate` would become a dead Unity
  reference. Correct for the engine's own per-spawn scratch defs, wrong for ours.
- `CreateDef(guid, original)` — lands in `DefRepositoryDef.AllDefs`, which nothing clears, and a
  save's def reference still resolves next session. **That is what this mod uses.**

The GUIDs are *derived* — `MD5("<modid>/<def name>")` reinterpreted as a `Guid` — rather than
hand-written, so the set cannot drift out of sync with itself, and re-entry returns the existing def
instead of throwing on the repo's duplicate-key `Add`.

Four clones, in order: `AddonsManagerDef` → `AddonsComponentDef` → `TacActorAnimActionsDef` (plus one
clone per non-default anim action) → `ComponentSetDef` → `TacCharacterDef`. Nothing shipped is
mutated, so every Mutog in the game is still a Mutog.

---

## What LEAD must see in the log

Two places, and they are different questions.

**At bake time** (`ct_project CustomCreature`) — *did the tool understand the model, and is the
manifest complete?*

```text
clip-names PASS "loop" names 2 clip(s) and "play" names 1 of the 7 this project bakes
creature-measure 'cyborg_spider': 49 bone(s), spans 120.435 x 64.237 x 105.578 file unit(s)
  about 0,29.979,-3.649; a tile is 1.0, so "scale": 0.008 makes it one tile across (this
  project declares 0.008). Its origin is 32.118 above its lowest vertex on +Y, which is
  "creature": { "lift" } if the model is centred rather than standing on its feet.
creature-clips 'cyborg_spider': 7 animation(s) in the file -> Spider_Walk, Spider_Idle,
  Spider_Idle_long, Spider_Damage, Spider_Attack_1, Spider_Attack_2, Spider_Death
  lifted off the file's shared timeline at N s  (×7, one per clip — see §5b below)
creature-events PASS every blocking event the game waits for is declared
creature-roles PASS "clips" maps 6 of 7 discovered animation(s); every required role
  (walk, idle, attack, death) is mapped
ct_project: ALL PASS - ...\Dist\CustomCreature.bundle
```

Read `creature-measure` against what you declared: it prints the scale that *would* make the model
one tile across next to the one your file asks for. If those disagree wildly, that is the mistake
that corrupts the collider, the aim point and the root motion all at once.

`6 of 7` is not a gap: `Spider_Idle_long` is mapped to `""` in `ppcontent.json`, which is how you say
*bake this clip, give it no role*. No `creature-scaffold` line appears either — that one only prints
when the manifest's clip list is missing or incomplete, and this project's is already filled in.

**At load time and in a mission** — one `ct_creature` line per seam, each with its own PASS/FAIL:

```text
ct_creature PASS '...\Dist\CustomCreature.bundle' -> model 'cyborg_spider', 7 clip(s): cyborg_spider_spider_attack_1, ...
ct_creature PASS root-motion node '_rootJoint'
ct_creature PASS cloning 'AN_Mutog_..._CharacterTemplateDef' (tagged Mutog_ClassTagDef)
ct_creature PASS rig root 'cyborg_spider' has the Animator ON THE ROOT, renderer=... bones=49 ...
ct_creature PASS 4 animation event(s) stamped as OnAnimEvent(<name>) [cyborg_spider_spider_attack_1:ActionDo@0.4054, ...]
ct_creature PASS clips: N non-default anim action(s) rewritten ...; M TurnSequence slot(s) CLEARED ...
ct_creature PASS role 'walk' = 'cyborg_spider_spider_walk' isLooping=True
ct_creature PASS (tactical) 'Overridden: MidMonsterAnimator' had 45 overridable clip(s); ... -> HL_ActionPlaceholder -> cyborg_spider_spider_attack_1 (DefaultActionClip), Chiron_death -> cyborg_spider_spider_death, ...
ct_creature PASS '...' donor-free audit: no Mutog_ClassTagDef/VehicleTag, 1 geometry-free bodypart ...
ct_creature PASS roster (Tutorial.InitSquad) 'Manticore' carries 6 unit(s), space 6/6: ...
```

Then, in game: **new campaign → the spider is the last unit in the starting aircraft**, take it on a
mission, and it walks, attacks and dies on its own clips.

The ones worth knowing by name:

- **the roster line** — *is there a spider at all.* Fires on campaign start and lists every unit in
  the aircraft by template name. Every other line can be green while this one fails; that is the
  point of it. `AddCharacter` never refuses — it computes the space sum and throws it away
  (`GeoVehicle.cs:759-764`) — so "we called Add" is not evidence of anything.
- **the donor-free audit** — *is this our creature or a repainted donor.* Read back off the finished
  def through the game's own accessors, so an edit that re-points one field at the donor turns it
  red. `tools\check-donor-free.ps1` invokes the same predicate offline.
- **the controller line** — *whether turn, idle and death play at all* (§3). It fires per spawn, in a
  mission, not at mod load, and lists every clip the donor's controller holds and what each now
  plays. If it is absent the Harmony postfix never ran.
- **`isLooping=False … MUST CYCLE AND DOES NOT`** — the one to read first. A non-looping idle or walk
  plays once and holds, which in game is indistinguishable from *no animation at all*. Looping is not
  inferred: it comes from `ppcontent.json`'s top-level `"loop"` declaration →
  `m_MuscleClip.m_LoopTime`, and `ct_project` prints `, LOOPS` or `, plays once` per clip. A FAIL here
  means the deployed bundle was baked before the declaration existed — re-bake and restart.
- **`AnimEventReceiver.WaitForEvent timeout expired … the event is likely missing from the
  animator`** — the game's own error, and the sign that your `"events"` block does not cover an
  action you used (§5). It costs 10 s per action, not a hang.

## Scale and orientation — the two numbers that will bite you

Both are **measured by the bake and printed for you** (`creature-measure`, above); both are then
declared in `ppcontent.json`, and both are applied to the rig **root** on the one seam that runs
after the game's own `ResetTransform` (§2).

- **Size.** The mesh spans **120.435 file units** on its longest axis. A tactical tile is **1 unit**
  (`TacticalMap.cs:67`, `public const float TileSize = 1f`) → `"scale": 0.008`. This is the
  *top-level* `"scale"`, shared with the bake, because the root-motion ramp is measured in the game's
  units and not the file's — one number, not two that have to agree.
- **Up-axis and lift.** `"up": "0,1,0"` and `"lift": 2.1372`. The rotation is **derived** from the up
  axis by `Quaternion.FromToRotation`, whose contract is exactly "carry this onto that", so the only
  thing that can be wrong is the measurement. The lift is small here because this model's origin sits
  close to its feet; the old model's origin was its geometric centre and needed `"lift": 32.8288` to
  stop it standing in a hole half its own height deep. A rotation alone can never supply that number.
- Since commit `26eca4e` the bake honours the `.glb`'s above-the-skin node (the old model had
  `SpiderArmature`, rot −90 on X, scale 100), so a file with such a node imports **+Y-up at authored
  scale** and the rotation is the identity. The knob stays, because a file whose exporter baked a
  non-Y-up orientation into the *vertex data* still arrives on its side and this is the only thing
  that stands it up.

This model has UVs (`TEXCOORD_0`/`TEXCOORD_1`) and two embedded images, so it bakes with its own
material. The old model had **no `TEXCOORD`** at all and baked with the builtin Standard material.
For any model with UVs: drop `Content\Textures\<stem>.png` beside it — same stem as the `.glb` —
and it lands in the `_MainTex` slot.

## No external tool — the point of shipping the ORIGINAL download

`cyborg_spider.glb` is byte-identical to the Sketchfab download (renamed only). Unlike the old model
it uses **no compression extensions** — no meshopt, no quantization. ContentTool still decodes both
of those **in-house** (gate U10; the old model exercised them), so there is no Blender step, no
`gltf-transform`, no `npx`. The one format still refused is **Draco**, by name and with the fix
(open in Blender, export again with Compression unticked). Sketchfab serves downloads only to a
signed-in account, so unlike the old model this one cannot be re-fetched by a script — the
committed copy is the archive.

## What the bundle contains, and by what name

| asset | address |
|---|---|
| the creature prefab (root + 49 bones + `SkinnedMeshRenderer` + `Animator`) | `assets/morgott.demo.customcreature/models/cyborg_spider` |
| its seven clips | `assets/morgott.demo.customcreature/clips/cyborg_spider_spider_walk` … `_spider_death` |
| the override controller the bare `Animator` carries | `assets/morgott.demo.customcreature/controllers/cyborg_spider_aoc` |

A clip's name is `<model file stem>_<clip name in the .glb>`, lowercased — hence
`cyborg_spider_spider_walk`. The DLL matches clips by **suffix**, so renaming the model file cannot
silently unbind everything.

## Ceilings, stated

- **The bake emits no animation events.** §5, and the biggest one. `src\Bake\ClipFields.cs` never
  writes `m_Events` (zero occurrences of "event" in the file), so every baked clip arrives eventless
  and the game's `WaitForEvent` gates stall 10 s each. This mod works around it at load with
  `AnimationClip.AddEvent`, covering only `ShootShot`/`ActionDo`/`ActionEnd`/`Ragdoll`. **Any ability
  whose event is not in that list still costs 10 s.** The real fix belongs in the bake — events
  declared in `ppcontent.json`, written into the clip — so the prefab is event-complete before a mod
  ever touches it. That is a ContentTool slice, not a demo slice.
- **Root motion, not a speed stat.** §6: `Speed = offset.magnitude / clip.length`, sampled off the
  clip. This model's `Spider_Walk` genuinely travels (1.986079 tile/s at authored scale), so the
  retiming is a mild ×2.73 and the creature looks natural at 3.42 cycles/s. The old model animated
  **in place** and measured ~0, needing ×10.65 (12.78 cycles/s — a blur). **A walk clip with root
  translation is the single most useful thing to know before buying a model for a game.**
- **Body-part visuals do not attach.** The donor's body-part items carry prefabs that
  `Addon.AttachVisuals` reparents onto the actor's rig **matched by bone name**
  (`Addon.cs:1022-1080`, `GetEquivalentBones:1203-1232`). A 49-bone spider shares no bone name with a
  Mutog, so those attach to nothing. Harmless — our mesh is *on* the rig prefab, not an addon — but
  it means armour and weapon models will not show. A real creature mod ships its own body-part
  items, or none.
- **The stats, AI and abilities are the donor's.** This demo is about the ASSET→ANIMATION seam. It
  deliberately clones a working unit rather than inventing balance.
- **ONE clip per state, no state machine of ours.** §3: the controller is the donor's, and this
  route serializes no `ControllerConstant`. Authoring a genuinely new state machine means a
  controller of your own, from code.
- Cost is one float per curve per frame, whether the bone moves or not: seven clips on a 49-bone
  rig; mesh, skeleton and all seven fit in one bundle.

## Licence

The model is **CC BY 4.0** — attribution is **required**. The credit to SpiderBight [ArachnoBoy]
must stay in `SOURCES.md` and in the mod description. Read `SOURCES.md` before you swap in a model
of your own; not every free model is free to ship, and CC-BY additionally obliges you to keep the
author credited.
