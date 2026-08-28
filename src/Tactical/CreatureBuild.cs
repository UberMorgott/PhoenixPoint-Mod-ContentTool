using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Base.Core;
using Base.Defs;
using Base.Utils;
using HarmonyLib;
using PhoenixPoint.Common.Core;
using PhoenixPoint.Common.Entities.Addons;
using PhoenixPoint.Common.Entities.GameTags;
using PhoenixPoint.Common.Entities.GameTagsSharedData;
using PhoenixPoint.Common.Entities.Items;
using PhoenixPoint.Geoscape.Entities;
using PhoenixPoint.Geoscape.Levels;
using PhoenixPoint.Tactical.Entities;
using PhoenixPoint.Tactical.Entities.Abilities;
using PhoenixPoint.Tactical.Entities.Animations;
using PhoenixPoint.Tactical.Entities.Equipments;
using PhoenixPoint.Tactical.Eventus;
using PhoenixPoint.Tactical.Entities.Weapons;
using UnityEngine;
using MoveAbilityDef = PhoenixPoint.Tactical.Entities.Abilities.MoveAbilityDef;

namespace Morgott.ContentTool.Tactical
{
    /// <summary>
    /// ============ A DOWNLOADED MODEL BECOMES A UNIT THE GAME CAN PLAY ============
    ///
    /// This is the MECHANISM half of a creature mod, and it is deliberately the whole of it: everything
    /// below is what EVERY creature mod would otherwise copy verbatim out of a demo. A content mod's own
    /// code is then one call, and its choices live in the <c>"creature"</c> block of the
    /// <c>ppcontent.json</c> it already ships (<see cref="CreatureManifest"/>).
    ///
    /// Four questions, four answers, all with file:line into the decompile:
    ///
    /// 1. WHAT IS AN ACTOR MADE OF?
    ///    A playable unit is a <see cref="TacCharacterDef"/>, and everything about it hangs off
    ///    <c>Data.ComponentSetTemplate</c> - a flat list of component defs looked up BY TYPE
    ///    (ComponentSetDef.cs:19-29). A shipped NON-humanoid is cloned as a STRUCTURAL template,
    ///    because TacCharacterDef.cs:174/179/184/189/194 dereference five of those components with no
    ///    null check; nothing the PLAYER sees is inherited (see the chassis, tags and items below).
    ///
    /// 2. WHERE DOES THE RIG COME FROM?
    ///    <c>AddonsManagerDef.Rig</c> is a plain GameObject reference and <c>AddonsManager.SetupRig</c>
    ///    just instantiates it (AddonsManager.cs:112-120). The prefab ContentTool bakes - root, bones,
    ///    SkinnedMeshRenderer, Animator - drops straight into that ONE field.
    ///
    /// 3. HOW DOES THE GAME DRIVE ANIMATION? TWO WAYS, and missing the second is what leaves a creature
    ///    that walks but will not turn, idle or die.
    ///    (a) CONTINUOUS states come from one shipped AnimatorController per creature family whose CLIPS
    ///        are swapped through an <c>AnimatorOverrideController</c> (TacticalActor.cs:724-726). The
    ///        state machine NEVER changes - only which clip each state plays. See <see cref="WireClips"/>.
    ///    (b) ONE-SHOTS are STATES REACHED BY TRIGGER and never touch a clip field at all:
    ///        <c>SetTrigger("Die")</c> then <c>WaitForEvent("Ragdoll")</c> (RagdollDieAbility.cs:92-95).
    ///        So the def table is only half the bridge; <see cref="RemapController"/> is the other half.
    ///
    /// 4. AND EVERY BLOCKING WAIT IS AN ANIMATION EVENT, not a clip length. TacticalAbility.cs:1206,1214
    ///    ActionDo/ActionEnd, TacticalLevelController.cs:1814 ShootShot, RagdollDieAbility.cs:95 Ragdoll,
    ///    BashAbility.cs:465,498 for melee. A downloaded clip carries none, and each missing one is a
    ///    ten-second stall (AnimEventReceiver.cs:100,126). <see cref="StampEvents"/> puts them on at the
    ///    times the MANIFEST declares - the engine will not guess a hit frame, because a shot that fires
    ///    before the leg lands reads as a bug in the game rather than in the mod.
    /// </summary>
    public static class CreatureBuild
    {
        private const string HarmonyId = "morgott.contenttool.creaturebuild";
        /// <summary>Every def a creature mints is named with it, which is also how a re-entrant load
        /// recognises its own work instead of cloning a clone.</summary>
        internal const string Prefix = "ct_creature_";

        private static Harmony harmony;

        /// <summary>The rig nodes the game posts sounds and particles at. EXT_MainContext is the body
        /// (footsteps, impacts), EXT_VoiceContext the voice - the two names the shipped
        /// TacticalEventDefs ask AddonsManager.FindTransform for.</summary>
        private static readonly string[] EventContexts = { "EXT_MainContext", "EXT_VoiceContext" };

        /// <summary>
        /// Every non-flat family on TacActorNavAnimActionDef - the segments that change HEIGHT or
        /// pivot, as opposed to Run, which is the only one a downloaded walk cycle can honestly
        /// satisfy. Read off the def's own field names, so a slot renamed by a patch stops matching
        /// loudly (the arm reports how many were cleared) rather than silently claiming a capability.
        /// Filling any of these promises the path builder an animation that carries the actor through
        /// a climb, drop or vault; see the note at the call site for what that promise costs.
        /// </summary>
        private static readonly string[] Traversal =
        {
            "TurnSequence", "Skids",
            "ClimbUpLadder", "ClimbDownLadder", "ClimbUpLowObstacle", "ClimbDownLowObstacle",
            "DropDown", "JumpOverAndDropDown", "FallNoSupport", "JetJump",
            "JumpUpOneLevel", "JumpOverLowWall", "JumpOverLowObstacle",
            "Mount", "MountIdle", "Ram", "RamPrepare", "RamFinish"
        };

        /// <summary>The animation-event channel the eventus system listens on
        /// (TacActorBaseEventusComponent.cs:34). Sound rides this; blocking waits do not.</summary>
        private const string EventusChannel = "Event";

        /// <summary>The def repository, for resolving a manifest name to a shipped def.</summary>
        private static DefRepository Repo
        {
            get { return GameUtl.GameComponent<DefRepository>(); }
        }

        /// <summary>Every creature this engine has built, so a patch can recognise its own by DEF
        /// IDENTITY rather than by a name prefix another mod could collide with.</summary>
        private static readonly List<Creature> Built = new List<Creature>();

        /// <summary>
        /// BUILD THE CREATURE THE MOD AT <paramref name="modDir"/> DECLARES, and return the character
        /// template - the one thing the content mod still has to decide what to DO with (put it in the
        /// player's squad, hand it to a faction, spawn it from an ability).
        ///
        /// Returns null and says why; it never throws into a mod's OnModEnabled, because Phoenix Point
        /// answers a failed mod load by rewriting MOD_ACTIVATED empty, which silently disables every
        /// OTHER mod too (measured 2026-08-13).
        /// </summary>
        public static TacCharacterDef Build(string modDir, Action<string> log)
        {
            Action<string> say = log ?? (m => ContentToolMain.Say(m));
            try { return BuildOrThrow(modDir, say); }
            catch (Exception ex)
            {
                say("ct_creature VOID nothing was wired and no def was minted: " + ex);
                return null;
            }
        }

        private static TacCharacterDef BuildOrThrow(string modDir, Action<string> say)
        {
            string metaPath = Path.Combine(modDir, Project.ContentMods.Manifest);
            if (!File.Exists(metaPath))
            {
                say("ct_creature VOID no " + Project.ContentMods.Manifest + " in '" + modDir +
                    "' - a creature mod declares its model, clips and stats there.");
                return null;
            }
            string json = File.ReadAllText(metaPath);
            CreatureManifest man = CreatureManifest.Parse(json);
            if (ReferenceEquals(man, CreatureManifest.None))
            {
                say("ct_creature VOID '" + metaPath + "' declares no \"creature\" block. Add one (even " +
                    "an empty \"creature\": {}) and re-run `ct_project <name>`: the bake writes every " +
                    "clip it finds in your model into it for you to map to roles.");
                return null;
            }

            // --- the ASSETS: the mod's own bundle, baked by ContentTool ------------------------
            string id = Regexy(json, "id"), bundleName = Regexy(json, "bundle");
            string path = Path.Combine(Path.Combine(modDir, "Dist"), bundleName);
            if (!File.Exists(path))
            {
                say("ct_creature VOID '" + path + "' does not exist - run `ct_project " +
                    Path.GetFileName(modDir) + "` in the console once, then restart. Nothing was changed.");
                return null;
            }
            AssetBundle bundle = AssetBundle.LoadFromFile(path);
            if (bundle == null)
            {
                say("ct_creature FAIL AssetBundle.LoadFromFile returned null for '" + path + "'");
                return null;
            }
            // The bundle stays MOUNTED for the rest of the run on purpose: the prefab and the clips are
            // referenced by live defs from here on, and Unload would take them with it.
            AnimationClip[] clips = bundle.LoadAllAssets<AnimationClip>();

            // BY ADDRESS, NEVER "the first GameObject in the bundle". A baked bundle carries external
            // PPtrs into the shipped archives (the AnimatorOverrideController's base controller lives
            // in _common), so LoadAllAssets<GameObject> also hands back whatever placeholder prefab
            // came in with them - measured: it returned 'PLACEHOLDER_Mutoid_Head_Invisible_Ready',
            // which has no Animator, and the build failed on a rig that was never ours.
            // The address is the bake's own naming rule - "assets/<mod id>/models/<glb stem>",
            // lowercased by BundleBaker.Normalize - so this is a rule and not a guess.
            string stem = ModelStem(modDir, man, say);
            if (stem == null) return null;
            string key = ("assets/" + id + "/models/" + stem).ToLowerInvariant();
            GameObject model = bundle.LoadAsset<GameObject>(key);
            say("ct_creature " + (model != null ? "PASS" : "FAIL") + " '" + path + "' -> model '" +
                (model == null ? "MISSING at '" + key + "'; the bundle holds GameObject(s) [" +
                     string.Join(", ", bundle.LoadAllAssets<GameObject>().Select(g => g.name).ToArray()) +
                     "] - re-run `ct_project " + Path.GetFileName(modDir) + "`"
                 : model.name) + "', " + clips.Length + " clip(s): " +
                string.Join(", ", clips.Select(c => c.name).ToArray()));
            if (model == null) return null;

            Creature c = new Creature { Man = man, Clips = clips, Say = say, Id = id };
            // The rig-root scale is the BAKE's own "scale", never a second number to keep in step -
            // the root-motion ramp is measured in the game's units and reads the same key.
            c.Scale = Number(json, "scale");
            if (c.Scale <= 0f) c.Scale = 1f;
            c.Up = new Vector3(man.Up[0], man.Up[1], man.Up[2]);
            if (c.Up.sqrMagnitude < 1e-6f) c.Up = Vector3.up;

            DefRepository repo = GameUtl.GameComponent<DefRepository>();

            // --- the DONOR: a shipped unit found by TAG and never by name ----------------------
            SharedGameTagsDataDef shared = GameUtl.GameComponent<SharedData>().SharedGameTags;
            // TWO SPELLINGS, BECAUSE THE GAME ONLY GIVES A TAG TO TWO OF ITS UNITS.
            // "donor" was originally a SharedGameTags FIELD name, on the reasoning that a tag is a
            // stabler handle than a def name across patches. That reasoning is sound and the choice was
            // still wrong: SharedGameTagsDataDef.cs:18-195 carries exactly TWO per-species tag fields,
            // MutogTag:80 and MutoidTag:83, and nothing at all for the other ~690 shipped characters
            // (verified: no CrabmanTag/TritonTag/SirenTag/... exists anywhere in the assembly). Every
            // other family is identified by DEF NAME, so a tag-only key could only ever name a Mutog -
            // which is how a demo about a small spider ended up cloning a three-by-three vehicle.
            // The tag form still resolves first, so "MutogTag" keeps meaning what it always meant.
            GameTagDef donorTag = TagNamed(shared, man.Donor);
            TacCharacterDef donor = repo.GetAllDefs<TacCharacterDef>()
                .FirstOrDefault(d => d.Data != null && d.Data.ComponentSetTemplate != null &&
                                     d.TacticalActorBaseDef != null &&
                                     (donorTag != null
                                          ? d.TacticalActorBaseDef.GameTags.Contains(donorTag)
                                          : string.Equals(d.name, man.Donor, StringComparison.OrdinalIgnoreCase)));
            if (donor == null)
            {
                say("ct_creature FAIL ppcontent.json \"creature\": \"donor\" is '" + man.Donor +
                    "', which is neither a GameTagDef field on SharedGameTags nor the name of a shipped " +
                    "TacCharacterDef with a component set. It names the shipped unit whose COMPONENT " +
                    "STRUCTURE is cloned - write a def name such as \"Swarmer_TacCharacterDef\". " +
                    "Nothing was changed.");
                return null;
            }
            say("ct_creature PASS cloning '" + donor.name + "' (" +
                (donorTag != null ? "tagged " + donorTag.name : "by def name") + ")");

            AddonsComponentDef donorAddons = donor.ComponentSetDef.GetComponentDef<AddonsComponentDef>();
            TacActorAnimActionsDef donorAnims = donor.ComponentSetDef.GetComponentDef<TacActorAnimActionsDef>();
            if (donorAddons == null || donorAddons.AddonsManagerDef == null || donorAnims == null)
            {
                say("ct_creature FAIL '" + donor.name + "' has addons=" + (donorAddons != null) +
                    " manager=" + (donorAddons != null && donorAddons.AddonsManagerDef != null) +
                    " anims=" + (donorAnims != null) + " - cannot wire a creature onto it.");
                return null;
            }

            GameObject rig = BuildRig(c, model, donorAddons.AddonsManagerDef);
            if (rig == null) return null;

            AddonsManagerDef managerClone = Clone(repo, c, donorAddons.AddonsManagerDef, "AddonsManagerDef");
            managerClone.Rig = rig;
            // Remembered so the SetupRig postfix can recognise its own manager by DEF IDENTITY - see
            // <see cref="CreatureRigIsScaled"/>, the seam that sizes every rig including the ruler.
            c.Manager = managerClone;
            // The root-motion node is found BY NAME inside the rig (AddonsManager.cs:120) and the game
            // measures every clip's travel off it (AnimationInfos.GetAnimInfo:99-114). It is the rig
            // ROOT's own name, which is the model's, so this is read and never typed.
            // WHICH NODE THE GAME MEASURES TRAVEL OFF, and it has to be the very bone the BAKE wrote
            // the ramp on or the two describe different transforms and the creature walks on the spot.
            //
            // This used to look for a bone literally called "Root". That was never a convention - it
            // was the first model's spelling. The second model calls its armature root '_rootJoint',
            // found nothing, and fell back to the PREFAB root, which is the ramp bone's PARENT and
            // therefore never moves: AnimationInfos.cs:105 measures the motion point in the animated
            // object's own local space, so a parent node measures 0 and TacticalNavigationComponent
            // .cs:248 then reports "this segment does not move the actor".
            //
            // So it is DERIVED, by the same rule Treadmill.RootBone uses on the baked skin: the one
            // bone no other bone carries. Applied to the live rig the two cannot disagree, whatever
            // the author called it. SkinnedMeshRenderer.rootBone is NOT used - SkinFields.cs:361
            // writes that as bone 0, which is only the armature root when the file happens to list it
            // first. Anything other than exactly one parentless bone falls back and SAYS so.
            managerClone.RootMotionNodeName = rig.name;
            SkinnedMeshRenderer skinned = rig.GetComponentInChildren<SkinnedMeshRenderer>(true);
            Transform[] bones = skinned == null ? null : skinned.bones;
            Transform ramped = null;
            int rootBones = 0;
            if (bones != null && bones.Length > 0)
            {
                HashSet<Transform> owned = new HashSet<Transform>(bones.Where(b => b != null));
                foreach (Transform b in bones)
                    if (b != null && (b.parent == null || !owned.Contains(b.parent)))
                    { ramped = b; rootBones++; }
                if (rootBones == 1) managerClone.RootMotionNodeName = ramped.name;
            }
            say("ct_creature " + (rootBones == 1 ? "PASS" : "WARN") + " root-motion node '" +
                managerClone.RootMotionNodeName + "' - " + (rootBones == 1
                    ? "the rig's one parentless bone, the same bone Treadmill wrote the walk ramp on"
                    : "the rig has " + rootBones + " parentless bone(s) of " +
                      (bones == null ? 0 : bones.Length) + ", so the prefab root is used and the game " +
                      "will measure 0 travel (AnimationInfos.cs:123) unless the ramp happens to sit there"));

            // --- the CHASSIS: the structure the engine demands, with none of the donor's geometry ---
            // SetupRig also attaches the manager's chassis addon (AddonsManager.cs:145-148), so
            // inheriting the donor's puts a whole donor body on screen next to ours. It cannot simply
            // be nulled either: GetTemplateBodyparts LINQ-Concats it with no guard
            // (TacCharacterDef.cs:194-197) and GeoscapeView.cs:429 calls that for EVERY faction
            // character - one null here stops the geoscape building, for the whole roster.
            // So it EXISTS and is EMPTY. SkinData == null makes Addon.AttachVisuals resolve a null
            // prefab (Addon.cs:1024) and return at :1029-1032 BEFORE Instantiate. SubAddons go with it -
            // each is a separate AddonDef with its OWN SkinData (Addon.Init:265-273).
            AddonDef donorChassis = donorAddons.AddonsManagerDef.SkeletonChassisAddonDef;
            if (donorChassis == null)
            {
                say("ct_creature FAIL '" + donorAddons.AddonsManagerDef.name + "' has no " +
                    "SkeletonChassisAddonDef to use as a structural template.");
                return null;
            }
            AddonDef chassis = Clone(repo, c, donorChassis, "ChassisAddonDef");
            chassis.SkinData = null;
            chassis.SubAddons = new AddonDef.SubaddonBind[0];
            chassis.Tags = new GameTagsList();
            managerClone.SkeletonChassisAddonDef = chassis;
            managerClone.Tags = new GameTagsList();      // AddonsManager.Init:102 pours these into the actor

            AddonsComponentDef addonsClone = Clone(repo, c, donorAddons, "AddonsComponentDef");
            addonsClone.AddonsManagerDef = managerClone;

            // --- the TAGS: ours is not the donor's family and not a vehicle -------------------
            // IsMutog/IsVehicle are read off ONE list (TacCharacterDef.cs:228-236) and that single bit
            // drives the "EDIT MUTOG" button, routes the unit view to UIStateViewVehicle, swaps its
            // armour list for a weapon list and charges it against ground-vehicle capacity.
            TacticalActorBaseDef donorBase = donor.ComponentSetDef.GetComponentDef<TacticalActorBaseDef>();
            TacticalActorBaseDef baseClone = Clone(repo, c, donorBase, "TacticalActorBaseDef");
            baseClone.GameTags = Purge(donorBase.GameTags, donorTag, shared.VehicleTag);
            c.BaseDef = baseClone;

            // --- the FOOTPRINT: one tile, not the donor's block -------------------------------
            // How many tiles an actor stands on is ONE STRING on the navigation component def
            // (NavMeshNavigationComponentDef.cs:15,38-45 -> NavSettings.AgentRadius), and that radius is
            // the only size the world sees: the tile graph carves nodes with it, the engine's own "is
            // this a big unit" test is AgentRadius > 0.75f (TacticalActor.cs:1813). The value is TAKEN
            // from a shipped one-tile unit, never typed in - agent type names live in the level's NavMesh
            // settings, so any literal would be a guess that resolves to nothing.
            TacticalNavigationComponentDef donorNav =
                donor.ComponentSetDef.GetComponentDef<TacticalNavigationComponentDef>();
            TacCharacterDef[] infantry = repo.GetAllDefs<TacCharacterDef>()
                .Where(d => d != donor && d.ComponentSetDef != null && d.TacticalActorBaseDef != null &&
                            d.TacticalActorBaseDef.GameTags.Contains(shared.HumanTag) &&
                            !d.TacticalActorBaseDef.GameTags.Contains(shared.VehicleTag) &&
                            (donorTag == null || !d.TacticalActorBaseDef.GameTags.Contains(donorTag)) &&
                            d.ComponentSetDef.GetComponentDef<TacticalNavigationComponentDef>() != null)
                .ToArray();
            string agent = infantry
                .Select(d => d.ComponentSetDef.GetComponentDef<TacticalNavigationComponentDef>().AgentType)
                .Where(a => !string.IsNullOrEmpty(a))
                .GroupBy(a => a).OrderByDescending(g => g.Count()).Select(g => g.Key).FirstOrDefault();
            // The ONE shipped unit everything below is copied from, so every value has a single named
            // provenance instead of one query per field.
            TacCharacterDef reference = infantry.FirstOrDefault(d =>
                d.ComponentSetDef.GetComponentDef<TacticalNavigationComponentDef>().AgentType == agent);
            TacticalNavigationComponentDef navClone = null;
            if (donorNav != null && agent != null)
            {
                navClone = Clone(repo, c, donorNav, "NavigationDef");
                navClone.AgentType = agent;
                // ...AND IT MUST NOT ASK FOR A TURN IT CANNOT PLAY. This is the same statement as the
                // cleared TurnSequence in WireClips, and the two have to be made together or the move
                // START is broken while mid-path corners are fine - which is exactly what a player sees.
                //   PathProcessorUtils.cs:508-515  ShouldTurnInPlaceBeforeSprint = AgentRadius >= 1f,
                //                                  else the def's AnimatedTurnBeforeSprint
                //   TacticalPathProcessor.cs:277   ...&& TryTurnInPlace(...) prepends turn points
                //   TacticalPathProcessor.cs:192-205  which returns FALSE at :196 with no turn clips
                // so a creature with an empty TurnSequence and this flag set asks the path processor
                // for a turn segment on every move order and is silently handed none. Cleared, the
                // actor never asks, and rotation falls to the lerp that needs no clip at all
                // (TacticalNavigationComponent.cs:1036-1059 NoAnimsFace, Slerp at 6/s).
                // The AgentType above is what makes this reachable: every shipped one-tile agent is
                // under the 1f radius, so the def's own flag is the deciding vote.
                navClone.AnimatedTurnBeforeSprint = false;
            }

            // ...AND THE OTHER HALF OF THE FOOTPRINT, WHICH IS THE ONLY HALF A PLAYER CAN SEE.
            // AgentRadius is what the WORLD believes; what the SCREEN draws is two GroundMarkerType
            // values read straight off defs with no size input at all (TacticalActorViewBaseDef.cs:23-25,
            // SceneViewElement.cs:169-186), and the enum ships explicit big-unit variants next to the
            // one-tile ones. Both are COPIED off the reference unit, never named: picking an enum value
            // by hand is picking a marker prefab by guess.
            TacticalActorViewBaseDef donorView = donor.ComponentSetDef.GetComponentDef<TacticalActorViewBaseDef>();
            TacticalActorViewBaseDef refView = reference == null ? null
                : reference.ComponentSetDef.GetComponentDef<TacticalActorViewBaseDef>();
            TacticalActorViewBaseDef viewClone = null;
            if (donorView != null && refView != null)
            {
                viewClone = Clone(repo, c, donorView, "ActorViewBaseDef");
                viewClone.HoverUICursor = refView.HoverUICursor;
                viewClone.FriendlySelectionUICursor = refView.FriendlySelectionUICursor;
            }
            // The move highlight is the third marker and lives one level down, on the ability
            // (TacticalAbilityDef.cs:26). The ability def is CLONED - the donor's is shared with every
            // shipped unit of that family and writing through it would re-size THEIR highlight too.
            MoveAbilityDef donorMove = baseClone.Abilities == null ? null
                : baseClone.Abilities.OfType<MoveAbilityDef>().FirstOrDefault();
            MoveAbilityDef refMove = reference == null || reference.TacticalActorBaseDef.Abilities == null
                ? null : reference.TacticalActorBaseDef.Abilities.OfType<MoveAbilityDef>().FirstOrDefault();
            if (donorMove != null && refMove != null && refMove.SceneViewElementDef != null)
            {
                MoveAbilityDef moveClone = Clone(repo, c, donorMove, "MoveAbilityDef");
                moveClone.SceneViewElementDef = refMove.SceneViewElementDef;
                baseClone.Abilities = baseClone.Abilities
                    .Select(a => a == donorMove ? (Base.Entities.Abilities.AbilityDef)moveClone : a).ToArray();
            }
            say("ct_creature " + (navClone != null && viewClone != null ? "PASS" : "WARN") +
                " footprint: AgentType '" + (donorNav == null ? "(none)" : donorNav.AgentType) + "' -> '" +
                (agent ?? "(no shipped one-tile unit to copy from)") + "', cursors and move highlight " +
                "copied from '" + (reference == null ? "(none)" : reference.name) +
                "' - the radius is what the world believes, the cursors are what the player sees");

            // --- the CLIPS --------------------------------------------------------------------
            TacActorAnimActionsDef animsClone = Clone(repo, c, donorAnims, "AnimActionsDef");
            c.Anims = animsClone;
            WireClips(repo, c, animsClone);

            // --- the UNIT ---------------------------------------------------------------------
            ComponentSetDef setClone = Clone(repo, c, donor.ComponentSetDef, "ComponentSetDef");
            // The other components stay: TacCharacterDef.cs:174/179/184/189/194 dereference them with no
            // null check, so a missing one is an NRE on the geoscape, not a missing feature. A donor
            // component as a STRUCTURAL template is fine; visuals, items and tags are replaced.
            // ...EXCEPT THE ONE THAT BREAKS THE SCENERY, WHICH IS A COMPONENT AND NOT A TAG.
            // A unit does not crush walls because it is big or because it is a vehicle - nothing in the
            // game branches on VehicleTag or a size tag for this. It crushes because its component set
            // carries a TacticalDemolitionComponentDef, which subscribes to the actor's own movement:
            //   TacticalDemolitionComponent.cs:75       ActorMovedEvent += ActorMoved, on enable
            //   TacticalDemolitionComponent.cs:151-214  every step sweeps a PhysicsCast for IDamageable
            //   TacticalDemolitionComponent.cs:216-239  item.ApplyDamage(.., _kineticPower * Force)
            //   TacticalDemolitionComponent.cs:98-112   _kineticPower = the run clip's Speed SQUARED
            //   TacticalDemolitionComponentDef.cs:18    AlwaysEnabled = true, by default
            // A shipped one-tile infantry unit simply HAS no such component, so the null check at
            // TacticalNavigationComponent.cs:829 is the whole of its "does not crush" behaviour - which
            // is why removing it is the honest clone of an infantry unit and not a special case.
            //
            // IN THE ENGINE AND NOT THE DEMO, for two reasons: the def defaults to AlwaysEnabled, so
            // ANY donor that ships one hands it to ANY creature built on it silently; and _kineticPower
            // is the square of the clip's own speed, so the "pace" retime that fixes the crawl would
            // otherwise multiply a small creature's crushing force by a hundred. A custom creature that
            // WANTS to demolish can add the component back - that is content, and it is not this.
            setClone.Components = donor.ComponentSetDef.Components
                .Where(x => !(x is TacticalDemolitionComponentDef))
                .Select(x => x == donorAddons ? (ObjectDef)addonsClone
                           : x == donorAnims ? (ObjectDef)animsClone
                           : x == donorBase ? (ObjectDef)baseClone
                           : (navClone != null && x == donorNav) ? (ObjectDef)navClone
                           : (viewClone != null && x == donorView) ? (ObjectDef)viewClone : x).ToArray();

            TacCharacterDef unit = Clone(repo, c, donor, "CharacterTemplateDef");
            unit.Data = donor.Data.Clone();          // TacCharacterData.Clone(), the game's own copy
            unit.Data.ComponentSetTemplate = setClone;
            if (man.Name.Length > 0) { unit.Data.Name = man.Name; unit.Data.LocalizeName = false; }
            // The donor's ITEMS were the second half of a doubled model: AddonsCharacterBuilder
            // .UseAddonManager:162-166 feeds Data.BodypartItems and EquipmentItems[0] straight into the
            // addon list and each carries its own SkinData. The *Data lists go with them -
            // GenerateInstanceData:121-123 prefers them over the arrays when non-null.
            unit.Data.BodypartItems = new ItemDef[0];
            unit.Data.EquipmentItems = new ItemDef[0];
            unit.Data.BodypartItemsData = null;
            unit.Data.EquipmentItemsData = null;
            unit.Data.GameTags = donor.Data.GameTags
                .Where(t => t != null && t != donorTag && t != shared.VehicleTag).ToArray();
            c.Def = unit;

            // --- the STATS a bodypart-free unit has to carry itself ----------------------------
            // A soldier's Speed, Endurance and Willpower are summed off its BODYPART aspects, and this
            // unit has none, so all three arrive at ZERO:
            //   CharacterStats.cs:301-302  ActionPoints.Max = Mathf.Max(1.3f, Speed)  -> one tile of move
            //   CharacterStats.cs:303      Health.Max = Toughness + Endurance * mult  -> Health 0/0
            //   TacticalActorBase.cs:118   Health.Max 0 IS IsDead, from the very first frame
            // The fix is NOT a bodypart item (every shipped one carries SkinData, i.e. the donor body).
            // The base values have their own fields and reach the same stats with no geometry
            // (TacCharacterData.cs:126-127 -> TacticalActor.cs:549).
            //
            // HEALTH, NOT STRENGTH, is what the manifest states, because Strength is not a quantity a
            // player ever sees - it is an input to the equation above whose OTHER two terms belong to the
            // actor def and may move with a game patch. So the author states the health they want and
            // the engine inverts the game's own formula. Ceil, because Health rounds up too
            // (CharacterStats.cs:304 StatRoundingMode.Ceil).
            float mult = baseClone.EnduranceToHealthMultiplier;
            if (man.Health > 0f)
                unit.Data.Strength = mult <= 0f ? unit.Data.Strength
                    : Mathf.Max(1, Mathf.CeilToInt((man.Health - baseClone.Toughness) / mult));
            if (man.Will > 0) unit.Data.Will = man.Will;
            if (man.Speed > 0) unit.Data.Speed = man.Speed;
            if (man.Volume > 0) unit.Volume = man.Volume;
            say("ct_creature " + (unit.Data.Strength > 0 ? "PASS" : "FAIL") + " '" + unit.name +
                "' carries its own base stats because it has no bodyparts to carry them: " +
                "Strength(Endurance) " + unit.Data.Strength + ", Will " + unit.Data.Will + ", Speed " +
                unit.Data.Speed + ", Volume " + unit.Volume + " -> Health.Max = Toughness " +
                baseClone.Toughness.ToString("F0") + " + " + unit.Data.Strength + " x " +
                mult.ToString("F2") + " = " + (baseClone.Toughness + unit.Data.Strength * mult).ToString("F0") +
                " BEFORE bodypart aspects (\"health\" asked for " + man.Health.ToString("F0") + "). " +
                "A zero here is Health 0/0, which is IsDead on the first frame.");

            Melee(repo, c, donor, chassis, unit, animsClone, donorTag, shared);
            // The SECOND, RANGED attack - only when the manifest declares one. Every existing
            // creature stays on the melee-only path above, untouched.
            if (man.Ranged.Length > 0)
                CreatureRanged.Ranged(repo, c, donor, chassis, unit, animsClone, setClone, rig,
                                      donorTag, shared);

            // THE ARM THAT CANNOT BE SATISFIED BY GOOD INTENTIONS. Every value is read back off the
            // FINISHED def through the game's own accessors - GetAddonsMangerDef() walks
            // Data.ComponentSetTemplate exactly as TacCharacterDef.cs:174 does, TacticalActorBaseDef is
            // the property IsMutog itself reads - so a later edit that re-points one of them at the
            // donor turns this red. The predicate is shared with tools\check-donor-free.ps1.
            AddonsManagerDef built = unit.GetAddonsMangerDef();
            AddonDef builtChassis = built == null ? null : built.SkeletonChassisAddonDef;
            string[] leaks = DonorLeaks(
                donorTag != null && (unit.TacticalActorBaseDef.GameTags.Contains(donorTag) ||
                                     unit.Data.GameTags.Contains(donorTag)),
                unit.TacticalActorBaseDef.GameTags.Contains(shared.VehicleTag)
                    || unit.Data.GameTags.Contains(shared.VehicleTag),
                // A SkinData WE minted is not a donor leak - the ranged weapon deliberately carries
                // one (its synthesised muzzle, CreatureRanged.ShootPointSkinDataDef), because a
                // skinless ranged weapon cannot resolve a shot origin at all. Only somebody ELSE's
                // SkinData is the donor showing through.
                unit.Data.BodypartItems.Count(i => i != null && i.SkinData != null &&
                                                   !(i.SkinData is CreatureRanged.ShootPointSkinDataDef)),
                unit.Data.EquipmentItems.Length,
                builtChassis != null, builtChassis != null && builtChassis.SkinData == null,
                built != null && ReferenceEquals(built.Rig, rig),
                setClone.Components.Any(x => x is TacticalDemolitionComponentDef));
            say("ct_creature " + (leaks.Length == 0 ? "PASS" : "FAIL") + " '" + unit.name +
                "' donor-free audit: " + (leaks.Length == 0
                    ? "no " + (donorTag == null ? "donor-family tag" : donorTag.name) +
                      "/VehicleTag, " + unit.Data.BodypartItems.Length +
                      " bodypart(s) with no donor SkinData + 0 equipment item(s), own empty chassis (SkinData null), " +
                      "own rig, no demolition component (" +
                      donor.ComponentSetDef.Components.Count(x => x is TacticalDemolitionComponentDef) +
                      " dropped from the donor's set)"
                    : string.Join("; ", leaks) + " <- THE DONOR IS STILL SHOWING THROUGH"));

            Install();
            Built.Add(c);
            say("ct_creature PASS '" + unit.name + "' is built: set='" + setClone.name + "' anims='" +
                animsClone.name + "' base='" + baseClone.name + "' chassis='" + chassis.name + "'");
            return unit;
        }

        /// <summary>
        /// THE DONOR-FREE PREDICATE, in one place so the in-game audit and
        /// <c>demos\CustomCreature\tools\check-donor-free.ps1</c> cannot drift apart. Deliberately
        /// takes no Unity type, so the offline script can invoke it out of the compiled DLL with no
        /// game running. Returns one line per leak; empty means clean.
        /// </summary>
        internal static string[] DonorLeaks(bool donorTag, bool vehicleTag, int bodypartsWithSkin,
                                            int equipmentItems, bool chassisPresent, bool chassisSkinNull,
                                            bool rigIsOurs, bool demolition)
        {
            List<string> bad = new List<string>();
            if (donorTag) bad.Add("carries the donor family's tag (EDIT MUTOG button, UIStateViewVehicle, " +
                                  "ground-vehicle capacity)");
            if (vehicleTag) bad.Add("carries VehicleTag");
            // A bodypart is not a leak - the melee weapon IS one, and the unit needs it. Its GEOMETRY is
            // the leak: SkinData is what Addon.AttachVisuals:1024 instantiates, and a bodypart that
            // still has one puts the donor's limb back on our rig.
            if (bodypartsWithSkin != 0) bad.Add(bodypartsWithSkin + " bodypart item(s) still carry donor " +
                                                "SkinData - Addon.AttachVisuals:1024 will hang the " +
                                                "donor's geometry on our rig");
            if (equipmentItems != 0) bad.Add(equipmentItems + " donor equipment item(s) still attached");
            // Both directions matter: a null chassis is an ArgumentNullException at
            // TacCharacterDef.cs:197 for the WHOLE roster; a chassis WITH SkinData is the donor body.
            if (!chassisPresent) bad.Add("no SkeletonChassisAddonDef - GetTemplateBodyparts:197 Concats " +
                                         "a null sequence for every faction character");
            else if (!chassisSkinNull) bad.Add("the chassis addon still has SkinData - " +
                                               "Addon.AttachVisuals:1024 will instantiate a body");
            if (!rigIsOurs) bad.Add("AddonsManagerDef.Rig is not the prefab we baked");
            // The axis a whole session was spent on: the creature was small, one-tile and tag-free, and
            // still smashed the scenery it walked past, because none of the six axes above can see a
            // component. TacticalDemolitionComponentDef.cs:18 AlwaysEnabled defaults TRUE, so this is
            // inherited by SILENCE - exactly the shape of leak this predicate exists to name.
            if (demolition) bad.Add("still carries a TacticalDemolitionComponentDef - " +
                                    "TacticalDemolitionComponent.cs:75 subscribes to ActorMovedEvent and " +
                                    ":216 ApplyDamage()s every destructible it walks past");
            return bad.ToArray();
        }

        /// <summary>
        /// WHICH <c>Content\Models\*.glb</c> IS THE CREATURE, by file stem.
        ///
        /// A project shipping exactly one model needs to say nothing - that one is it, which is the
        /// same "the file IS the declaration" rule <c>Content\Textures\</c> already follows. A project
        /// shipping several must name one in the manifest, and is REFUSED by name rather than served
        /// an arbitrary pick, because picking the wrong prefab means building a creature out of a rig
        /// that is not the author's.
        /// </summary>
        private static string ModelStem(string modDir, CreatureManifest man, Action<string> say)
        {
            string dir = Path.Combine(Path.Combine(modDir, "Content"), "Models");
            string[] glb = Directory.Exists(dir) ? Directory.GetFiles(dir, "*.glb") : new string[0];
            string[] stems = glb.Select(Path.GetFileNameWithoutExtension).ToArray();
            if (man.Model.Length > 0)
            {
                string named = stems.FirstOrDefault(s => string.Equals(s, man.Model, StringComparison.OrdinalIgnoreCase));
                if (named != null) return named;
                say("ct_creature FAIL ppcontent.json \"creature\": \"model\" names '" + man.Model +
                    "' but Content\\Models\\ holds [" + string.Join(", ", stems) + "]. Nothing was changed.");
                return null;
            }
            if (stems.Length == 1) return stems[0];
            say("ct_creature FAIL Content\\Models\\ holds " + stems.Length + " model(s) [" +
                string.Join(", ", stems) + "] - " + (stems.Length == 0
                    ? "there is no .glb to build a creature out of."
                    : "add \"model\": \"<stem>\" to ppcontent.json's \"creature\" block to say which " +
                      "one is the creature. The engine will not pick for you: the wrong prefab is a " +
                      "creature built on somebody else's rig.") + " Nothing was changed.");
            return null;
        }

        // ------------------------------------------------------------------ the melee attack

        /// <summary>
        /// THE DONOR'S OWN BODYPART WEAPON, WITH ITS BODY TAKEN OFF.
        ///
        /// The AI needs NO extra def for this. AIActionMoveAndAttack.GetAttackAbility:73-86 asks the
        /// weapon two questions - is <c>GetDamagePayload().DamageDeliveryType == Melee</c>, and does
        /// <c>WeaponDef.Abilities</c> hold a BashAbilityDef - then picks the actor's own BashAbility
        /// whose Source is that weapon. A shipped non-humanoid ships exactly such a bodypart, so this is
        /// the SAME strip the chassis gets, not a new def: clone the item, null its SkinData so
        /// Addon.AttachVisuals:1024-1032 returns before it instantiates the donor's limb, and hand it
        /// over. The ability, its damage payload and its ICON (TacticalAbilityDef.cs:24 ViewElementDef)
        /// come along unmodified and unshared.
        /// </summary>
        private static void Melee(DefRepository repo, Creature c, TacCharacterDef donor, AddonDef chassis,
                                  TacCharacterDef unit, TacActorAnimActionsDef anims,
                                  GameTagDef donorTag, SharedGameTagsDataDef shared)
        {
            WeaponDef donorMelee = DonorMeleeBodypart(donor);
            if (donorMelee == null)
            {
                c.Say("ct_creature WARN '" + donor.name + "' ships no bodypart WeaponDef that is Melee " +
                      "AND carries a BashAbilityDef - AIActionMoveAndAttack.GetAttackAbility:73-86 will " +
                      "find nothing and this creature can never attack.");
                return;
            }
            WeaponDef melee = Clone(repo, c, donorMelee, "MeleeWeaponDef");
            // THE BASH POINT, OR THE PLAYER'S BUTTON IS GREY. Nulling SkinData strips the donor's
            // limb, which is right - but it also leaves the weapon owning NO transforms at all, and
            // BashAbility.GetDisabledStateInternal:237 asks for one BY NAME:
            //     weapon.FindTransform(BashPoint) == null  ->  NoSuitableEquipment
            // FindTransform searches the addon's OwnedTransforms (Addon.cs:1374), so a skinless
            // weapon can never satisfy it. That is not cosmetic: GetDisabledState is what greys the
            // ability button (TacticalAbility.cs:367-377), so the creature could bash only when the
            // AI or a gate called Activate() directly - which is precisely how this passed every arm
            // while the user stood next to a Varg unable to press melee.
            //   MEASURED: BashWith=SourceWeapon, point 'EXT_ShootPoint' -> NOT FOUND, usableHands=0,
            //   state NoSuitableEquipment, targets offered=1.
            // The name is READ OFF THE DEF, never typed - it happens to be EXT_ShootPoint on this
            // donor, which is exactly the sort of thing no one would have guessed.
            BashAbilityDef bashDef = melee.Abilities == null ? null
                : melee.Abilities.OfType<BashAbilityDef>().FirstOrDefault();
            string bashPoint = bashDef == null ? null
                : (bashDef.BashWith == BashAbilityDef.BashingWith.SourceWeapon
                       ? bashDef.BashPoint : bashDef.NoEquipmentBashPoint);
            // ...AND SYNTHESISING ONE HERE IS NOT YET THE ANSWER. MEASURED, this exact run: giving the
            // melee weapon a SynthSkin carrying 'EXT_ShootPoint' builds clean and the donor-free audit
            // stays green (the audit already exempts our own ShootPointSkinDataDef), but the TACTICAL
            // SAVE THEN FAILS TO LOAD - "Serializing destroyed unity object at: List`1" twice, and the
            // game stops before the gate's first arm. The ranged weapon gets away with the same trick,
            // so the difference is not the helper; something on the melee item's path serializes its
            // SkinData graph, and a runtime GameObject in Visuals cannot survive that.
            //
            // A creature that cannot load a save is strictly worse than one whose melee button is grey,
            // so this stays null until the serialization path is understood. The defect is not hidden:
            // C1-offered asserts the ability is OFFERED and fails loudly, naming the state, the
            // BashWith mode and the missing transform.
            // ponytail: the ceiling is named rather than papered over - next step is to find who walks
            // BodypartItems' SkinData during save serialization, not to retry the same assignment.
            melee.SkinData = string.IsNullOrEmpty(bashPoint) ? null
                : CreatureRanged.SynthSkin(repo, c, "BashPoint", "MeleeSkinDataDef", bashPoint);
            c.Say("ct_creature " + (string.IsNullOrEmpty(bashPoint) ? "WARN" : "PASS") + " melee bash point " +
                  (string.IsNullOrEmpty(bashPoint)
                       ? "NOT DECLARED by '" + (bashDef == null ? "(no BashAbilityDef)" : bashDef.name) +
                         "' - nothing to synthesise; if the ability wants one the button stays grey"
                       : "'" + bashPoint + "' synthesised onto '" + melee.name + "' (BashWith=" +
                         bashDef.BashWith + ") - BashAbility:237 looks it up by name on the addon's " +
                         "OwnedTransforms (Addon.cs:1042-1043), which stay EMPTY for a SkinData-less " +
                         "weapon, and an empty list is what returns NoSuitableEquipment and greys the " +
                         "player's melee button"));
            melee.SubAddons = new AddonDef.SubaddonBind[0];
            melee.Tags = Purge(donorMelee.Tags, donorTag, shared.VehicleTag);
            // ...AND IT HAS TO HAVE SOMETHING TO HANG ON. A bodypart names the slot it needs
            // (AddonDef.RequiredSlotBind.IsCompatibleWith:31-49) and a donor's tail asks for a slot its
            // TORSO bodypart provides - which our creature does not have, so
            // CharacterBodyState.SetupBodyParts:89-97 silently drops it. A one-piece creature's weapon
            // hangs off the one slot the CHASSIS does provide, taken from the chassis rather than named,
            // because slot defs are shipped objects and typing one by hand is a guess.
            if (chassis.ProvidedSlots.Length > 0)
            {
                melee.RequiredSlotBinds = new[] { new AddonDef.RequiredSlotBind
                    { RequiredSlot = chassis.ProvidedSlots[0].ProvidedSlot } };
                // WHERE THE BASH POINT HANGS. The ranged path names a bone on ITS slot; this one named
                // nothing, and GetAttachTransform (Addon.cs) then falls back to the RIG ROOT itself -
                // so our synthesised marker was parented onto the very transform Orient scales and
                // ResetTransform'd against it. EXT_MainContext is a transform BuildRig ALWAYS creates
                // (see EventContexts), so it is the one anchor that cannot be missing, and it sits at
                // the body - the right place for a bash to originate.
                chassis.ProvidedSlots[0].AttachmentPointName = EventContexts[0];
            }
            unit.Data.BodypartItems = new ItemDef[] { melee };
            // The game's OWN predicate for "will this bodypart attach to that parent", asked at build
            // time instead of discovered as an empty Equipments list in a live mission.
            bool fits = chassis.ProvidesCompatibleSlotFor(melee);
            AlsoAccept(repo, c, anims, donorMelee, melee);
            BashAbilityDef bash = donorMelee.Abilities.OfType<BashAbilityDef>().FirstOrDefault();
            c.Say("ct_creature " + (bash != null && fits ? "PASS" : "FAIL") + " melee '" + donorMelee.name +
                  "' -> '" + melee.name + "' (SkinData nulled, " + donorMelee.SubAddons.Length +
                  " sub-addon(s) dropped) carrying '" + (bash == null ? "(none)" : bash.name) +
                  "'; the chassis " + (fits ? "PROVIDES" : "REFUSES") + " its slot" +
                  (fits ? " - stock AI picks this up with no extra def"
                        : " <- CharacterBodyState.SetupBodyParts:89-97 will drop it and the actor will " +
                          "enter play with no weapon and no bash ability"));
        }

        /// <summary>
        /// ============ WHY A CLONED WEAPON HAS NO ANIMATION AT ALL ============
        /// An anim action does not ask "is this a melee weapon" - it asks "is this equipment IN MY LIST",
        /// by def identity (TacActorAnimActionEquipmentFilteredDef.cs:17-22,63-70). A CLONE is a
        /// different def with a different instance id, so every one of those lists says no, and
        /// BashAbility.BashCrt:423-426 then dereferences the missing action with no null check: the bash
        /// coroutine dies on its first frame - no swing, no damage, and the ability never finishes.
        ///
        /// So: WHEREVER THE DONOR'S ITEM IS LISTED, LIST OURS BESIDE IT. The shared EquipmentListDef is
        /// CLONED before it is appended to - it is a def the shipped unit reads too.
        /// ponytail: additive, never a replacement - the donor's own entries stay.
        /// </summary>
        internal static void AlsoAccept(DefRepository repo, Creature c, TacActorAnimActionsDef anims,
                                       EquipmentDef donorItem, EquipmentDef ours)
        {
            int taught = 0;
            foreach (TacActorAnimActionBaseDef a in anims.AnimActions ?? new TacActorAnimActionBaseDef[0])
            {
                TacActorAnimActionEquipmentFilteredDef f = a as TacActorAnimActionEquipmentFilteredDef;
                if (f == null) continue;
                if (f.Equipments != null && f.Equipments.Contains(donorItem) && !f.Equipments.Contains(ours))
                { f.Equipments = f.Equipments.Concat(new[] { ours }).ToArray(); taught++; }
                if (f.EquipmentList != null && f.EquipmentList.Equipments != null &&
                    f.EquipmentList.Equipments.Contains(donorItem))
                {
                    EquipmentListDef list = Clone(repo, c, f.EquipmentList, f.EquipmentList.name);
                    if (!list.Equipments.Contains(ours))
                        list.Equipments = list.Equipments.Concat(new[] { ours }).ToArray();
                    f.EquipmentList = list;
                    taught++;
                }
                TacticalItemDef mine = ours as TacticalItemDef, theirs = donorItem as TacticalItemDef;
                if (f.Bodyparts != null && mine != null && theirs != null &&
                    f.Bodyparts.Contains(theirs) && !f.Bodyparts.Contains(mine))
                { f.Bodyparts = Enumerable.Concat(f.Bodyparts, new[] { mine }).ToArray(); taught++; }
            }
            c.Say("ct_creature " + (taught > 0 ? "PASS" : "FAIL") + " '" + ours.name + "' added beside '" +
                  donorItem.name + "' in " + taught + " equipment filter(s) - an anim action matches " +
                  "equipment by DEF IDENTITY, so a clone the lists do not name gets NO anim action and " +
                  "BashAbility.BashCrt:425 dereferences null");
        }

        /// <summary>The donor's bodypart weapon that IS its melee attack - the def the AI's attack
        /// picker resolves and the item the donor's own anim actions equipment-filter on. One lookup,
        /// shared by <see cref="Melee"/> and <see cref="CreatureRanged"/>, because the RANGED wiring
        /// teaches the donor's shoot action to accept our weapon BESIDE this very item - two copies of
        /// the predicate could drift and the drift would be a silent NRE at the first trigger pull.</summary>
        internal static WeaponDef DonorMeleeBodypart(TacCharacterDef donor)
        {
            return (donor.Data.BodypartItems ?? new ItemDef[0]).OfType<WeaponDef>()
                .FirstOrDefault(w => w.DamagePayload.DamageDeliveryType == DamageDeliveryType.Melee &&
                                     w.Abilities != null && w.Abilities.OfType<BashAbilityDef>().Any());
        }

        // ------------------------------------------------------------------ the rig

        /// <summary>
        /// The rig template: the baked prefab, parked under an INACTIVE holder so it never renders.
        ///
        /// THE ANIMATOR MUST BE ON THE RIG ROOT ITSELF. Wrapping the prefab in one extra transform to
        /// carry a scale correction is what makes the roster show a unit with no model, because the two
        /// code paths disagree about where the Animator lives:
        ///   TacticalActorBase.SetupAnimator:588  GetComponentInChildren&lt;Animator&gt;()  &lt;- tolerant
        ///   CommonCharacterUtils.DisplayCharacter:42-43  RigRoot.GetComponent&lt;Animator&gt;()  &lt;- strict
        /// - the second is a NullReferenceException through a wrapper. So the prefab IS the rig, and the
        /// scale/rotation correction moves to <see cref="Orient"/>, on the seam that runs after every reset.
        ///
        /// activeSelf is what Instantiate copies, not activeInHierarchy: the instance is active, its
        /// holder is not, so the template is invisible here and alive once instantiated.
        /// </summary>
        private static GameObject BuildRig(Creature c, GameObject model, AddonsManagerDef donorManager)
        {
            GameObject holder = new GameObject("ct_creature_templates");
            holder.SetActive(false);
            UnityEngine.Object.DontDestroyOnLoad(holder);

            GameObject inner = UnityEngine.Object.Instantiate(model, holder.transform);
            inner.name = model.name;

            // WHERE SOUND COMES OUT OF A CREATURE. Every noise this game makes on an actor is a
            // TacticalEventDef posted AT A NAMED TRANSFORM of its rig: TacticalEventDef.cs:22
            // EventTransformName, resolved by AddonsManager.FindTransform:353-371, and a name that
            // resolves to nothing drops the event ENTIRELY - audio and particles both -
            // TacActorEventusComponent.cs:65 says so and then returns.
            //
            // A downloaded .glb has no such nodes, which is why the log read "Could not find event
            // transform EXT_VoiceContext for event MissedTargetVoice". Two empty children fix every
            // INHERITED sound at once - the death voice the donor's die ability already carries
            // (Soldier_Die_AbilityDef -> DeathVoice_EventDef) and every shared bark - because those
            // defs are already correct and were only missing somewhere to play FROM. "Voice" is
            // rewritten to EXT_VoiceContext by TacActorBaseEventusComponent.cs:138-147, so the suffixed
            // spelling is the one the lookup actually asks for.
            //
            // ponytail: two empties at the rig root, not a mouth and a footfall socket. A real emitter
            // per body part is a rigging job the author must do; this is the difference between silent
            // and audible, and an author who wants the voice at the head can parent a transform of that
            // name in their model.
            foreach (string context in EventContexts)
                if (FindDeep(inner.transform, context) == null)
                    new GameObject(context).transform.SetParent(inner.transform, false);

            // THE CONTROLLER. ContentTool bakes the model's Animator over a one-state controller, which
            // is enough to prove a clip plays but has none of the states an ACTOR needs. The game's own
            // controller for this creature family does, and it is sitting on the donor's rig prefab.
            // GetComponent, NOT GetComponentInChildren - deliberately the strict call, because that is
            // what DisplayCharacter:42-43 uses and it is the invariant a wrapper silently broke.
            Animator ours = inner.GetComponent<Animator>();
            Animator theirs = donorManager.Rig == null ? null : donorManager.Rig.GetComponent<Animator>();
            if (ours == null)
            {
                c.Say("ct_creature FAIL the rig ROOT '" + inner.name + "' carries no Animator (it has " +
                      inner.GetComponentsInChildren<Animator>(true).Length + " in its children). " +
                      "CommonCharacterUtils.DisplayCharacter:42-43 does RigRoot.GetComponent<Animator>() " +
                      "with no null check, so the geoscape roster would throw and render nothing.");
                return null;
            }
            SkinnedMeshRenderer skin = inner.GetComponentInChildren<SkinnedMeshRenderer>(true);
            c.Mesh = skin == null ? null : skin.sharedMesh;
            string had = ours.runtimeAnimatorController == null ? "(null)" : ours.runtimeAnimatorController.name;
            if (theirs != null && theirs.runtimeAnimatorController != null)
                ours.runtimeAnimatorController = theirs.runtimeAnimatorController;
            c.Say("ct_creature " + (ours.runtimeAnimatorController != null && skin != null ? "PASS" : "FAIL") +
                  " rig root '" + inner.name + "' has the Animator ON THE ROOT, renderer=" +
                  (skin == null ? "MISSING" : skin.name + " bones=" + skin.bones.Length + " mesh=" +
                   (skin.sharedMesh == null ? "NULL" : skin.sharedMesh.name)) +
                  ", controller " + had + " -> " + (ours.runtimeAnimatorController == null
                      ? "(null - the donor rig had none either)" : "'" + ours.runtimeAnimatorController.name + "'"));

            // NOT ORIENTED HERE, AND THAT IS NOT AN OVERSIGHT. Orienting this prefab looks like the fix
            // that covers every consumer at once, because actor, dummy, roster and geoscape all
            // instantiate this one object. It is a NO-OP, and the game's own code says so:
            //
            //   AddonsManager.SetupRig:114   RigRoot = Instantiate(AddonsManagerDef.Rig, ...).transform;
            //   AddonsManager.SetupRig:115   RigRoot.ResetTransform();
            //   UnityUtil.ResetTransform:213-218   localPosition = zero, localRotation = identity,
            //                                      localScale = ONE
            //
            // Every rotation, scale and lift written onto the template is therefore wiped one line after
            // it is instantiated, for every one of those consumers. The seam has to be AFTER that reset:
            // the live actor gets it from <see cref="CreatureKeepsItsPose"/> and the roster from
            // <see cref="CreatureRemap"/>, and the shooting ruler - the one consumer neither of those
            // reaches - from <see cref="CreatureRigIsScaled"/>.
            return inner;
        }

        /// <summary>
        /// PUT THE CREATURE THE RIGHT WAY UP AND ON THE GROUND. Three writes, all on the rig ROOT - the
        /// very transform the Animator sits on - so this cannot re-break DisplayCharacter:42-43: it adds
        /// no object, reparents nothing and removes no component.
        ///
        /// The rotation is not an Euler triple to be tuned. It is DERIVED from the manifest's measured
        /// up axis by Quaternion.FromToRotation, whose documented contract is "a rotation that carries
        /// fromDirection onto toDirection" - so the only thing that can be wrong is the measurement. The
        /// lift is applied AFTER the scale on purpose: it moves the model's lowest vertex from "lift"
        /// below the root onto the root's own plane, and that vertex has been scaled too.
        ///
        /// It refuses to touch anything not wearing OUR mesh: on the roster path the Animator handed to
        /// the postfix has been observed to be a human soldier's rig, and rotating that would stand a
        /// PLAYER'S SOLDIER on its head.
        /// </summary>
        internal static void Orient(Creature c, Transform root, string who)
        {
            if (c == null || root == null) return;
            SkinnedMeshRenderer skin = root.GetComponentInChildren<SkinnedMeshRenderer>(true);
            if (c.Mesh == null || skin == null || skin.sharedMesh != c.Mesh) return;
            root.localRotation = Quaternion.FromToRotation(c.Up, Vector3.up);
            root.localScale = Vector3.one * c.Scale;
            root.localPosition = new Vector3(0f, c.Man.Lift * c.Scale, 0f);
            c.Say("ct_creature PASS (" + who + ") '" + root.name + "' model-up " + c.Up.ToString("F0") +
                  " -> +Y, scale " + c.Scale.ToString("F4") + ", lifted " +
                  (c.Man.Lift * c.Scale).ToString("F4") + " so the lowest vertex sits on the root's plane");
        }

        // ------------------------------------------------------------------ the clip bridge

        /// <summary>
        /// THE VANILLA CLIP'S NAME -&gt; ONE OF THE MANIFEST'S ROLES.
        ///
        /// This is the ONLY place the engine matches on a name, and it matches the DONOR's names, never
        /// the author's - which is the point: the author's clips are named by the manifest, and the
        /// donor's are whatever the shipped controller happens to call them. TOKENS, not Contains:
        /// "Soldier_Idle" CONTAINS "die" (sol-die-r) and would classify as a death animation, and so
        /// would "Bodied" or "Studies". Splitting on CamelCase boundaries and non-letters first makes the
        /// match exact and costs three lines.
        /// ponytail: a keyword rule on the vanilla name, not a per-controller table - the donor's clip
        /// names are not known offline and differ per creature family. Order matters (die before idle,
        /// turn before walk); unknown names get the idle, never nothing.
        /// </summary>
        /// <summary>
        /// THE ROLE OF ONE SLOT - asked of the DEF's own structure first, and only then of a name.
        ///
        /// <see cref="RoleForVanilla"/> reads a clip's FILE NAME, which is a heuristic and is all there
        /// is for a def whose twenty slots each mean something different. But one anim-action type is
        /// not like that: <c>TacActorAimingAbilityAnimActionDef</c> holds exactly ONE clip
        /// (<c>GetAllClips</c> returns <c>new[]{ Clip }</c>) and a REQUIRED <c>AbilityDefs[]</c> naming
        /// the abilities it plays for - <c>ValidateObject</c> logs an error if either is missing. So the
        /// meaning of that slot is not a guess at all: it is whatever the ability is.
        ///
        /// MEASURED, which is why this exists. The Swarmer's bash action 'E_Bash' names
        /// <c>BashStrike_AbilityDef</c>, and its clip is named something with no attack word in it - so
        /// the name heuristic classified the creature's ATTACK as an idle, the Action state played an
        /// event-less idle clip, and every bash cost two ten-second AnimEventReceiver timeouts
        /// (in game: damage landed 23.23 s after the swing began, the gate's C1-attack arm). Reading the
        /// ability instead is exact, and it removes a whole family of donors from the guessing game -
        /// no keyword list can cover every name a shipped bash clip might have.
        ///
        /// ponytail: only this one type gets the structural read, because only this one type carries a
        /// single clip with a declared meaning. The multi-slot types keep the name heuristic; there is
        /// nothing better available for them.
        /// </summary>
        private static string RoleFor(TacActorAnimActionBaseDef action, AnimationClip donorClip,
                                      string slot = null)
        {
            TacActorAimingAbilityAnimActionDef aiming = action as TacActorAimingAbilityAnimActionDef;
            if (aiming != null && aiming.AbilityDefs != null)
                foreach (Base.Entities.Abilities.AbilityDef ab in aiming.AbilityDefs)
                    if (ab != null) return RoleForVanilla(ab.name);

            // THE SLOT'S OWN NAME OUTRANKS THE DONOR'S CLIP NAME, and this is the bug that made the
            // spider stand still for a whole session. TacActorNavAnimActionDef.Run is the RUN sequence
            // by definition - the field is the declaration - whereas the clip the donor happens to have
            // parked in it is named whatever its animator felt like. MEASURED: the Swarmer's run clips
            // carry no locomotion word, so the name heuristic classified all three of Run.Start,
            // Run.Loop and Run.Stop as "idle", and the creature's navigation was wired to an animation
            // that travels nowhere. It reported PASS the whole time - hits were scored, no slot fell
            // back - and the only symptom was a move order that accepted, played the idle and never
            // moved:
            //   C1-walk  ordered 2.83 tile(s), travelled 0.00 in 30.01s,
            //            Run[..._idle|..._idle|..._idle] all=True
            //
            // This is the same structural read the aiming type already gets from its AbilityDefs, and
            // the reason the old comment here ("nothing better available for them") was wrong: a dotted
            // slot path IS a declaration. Asked first, and only a DEFINITE answer counts - RoleOrNull
            // returns null for a slot like "Clip" or "Start" that says nothing on its own, and then the
            // donor's clip name still decides, exactly as before.
            if (slot != null)
            {
                string bySlot = RoleOrNull(slot);
                if (bySlot != null) return bySlot;
            }
            return donorClip == null ? "idle" : RoleForVanilla(donorClip.name);
        }

        internal static string RoleForVanilla(string vanilla)
        {
            return RoleOrNull(vanilla) ?? "idle";   // idle, holster, draw, anything unknown
        }

        /// <summary>
        /// The same keyword read, but UNKNOWN comes back as null instead of "idle". That distinction is
        /// the whole point: "idle" is the catch-all, so a caller that wants to ask a SECOND source
        /// ("does this name tell me anything?") cannot use <see cref="RoleForVanilla"/> - every miss
        /// looks like a confident answer of idle. See <see cref="RoleFor"/>, which asks the slot first
        /// and the donor's clip name second precisely because of this.
        /// </summary>
        private static string RoleOrNull(string vanilla)
        {
            string[] t = Tokens(vanilla);
            if (Has(t, "die", "death", "dead")) return "death";
            if (Has(t, "turn", "skid", "rotate")) return "walk";           // turning is locomotion
            // Before the attack keywords: a flinch is a REACTION to being hit, not an attack, and the
            // shipped names for it ('E_Hurt_Reaction') would otherwise fall through to the idle.
            if (Has(t, "hurt", "react", "reaction", "flinch", "damage", "stagger")) return "reaction";
            // "shot" AND "shoot", "action" AND "attack" - the same word arrives in both tenses and the
            // token match is exact, so one spelling covers only half the states. Measured, not guessed:
            // the soldier controller 'HumanoidAnimatorLOC' names its firing states FF_FirstShot_AR /
            // FF_ShotLoop_AR / FF_EndShot_AR, none of which carries the token "shoot", so all three fell
            // through to the idle - and a creature whose bash plays an EVENT-LESS idle eats a 10s
            // AnimEventReceiver timeout per blocking event (measured in-game: a bash that landed its
            // damage 23.24 s after it started). "reaction" is one CamelCase token and does not collide
            // with "action"; nothing in the locomotion families tokenises to any of these.
            if (Has(t, "shoot", "shot", "fire", "attack", "action", "strike", "melee", "bash",
                       "aim", "reload", "peek")) return "attack";
            if (Has(t, "jump", "jet", "leap")) return "jump";
            if (Has(t, "walk", "run", "move", "step", "climb",
                       "fall", "drop", "land", "mount", "ram")) return "walk";
            return null;                           // idle, holster, draw, reaction, anything unknown
        }

        /// <summary>"Mutog_RunFwdLoop" -&gt; run, fwd, loop. Splits on CamelCase and on anything that is
        /// not a letter, so digits and separators never glue two words together.</summary>
        internal static string[] Tokens(string name)
        {
            return System.Text.RegularExpressions.Regex
                .Split(name, "(?<!^)(?=[A-Z])|[^A-Za-z]+")
                .Where(s => s.Length > 0).Select(s => s.ToLowerInvariant()).ToArray();
        }

        private static bool Has(string[] tokens, params string[] words) { return tokens.Any(words.Contains); }

        /// <summary>
        /// Applies the bridge to every NON-DEFAULT anim action.
        ///
        /// The rule, restated because getting it backwards is the classic mistake: the action with
        /// <c>IsDefaultAnimatorClips = true</c> is the KEY SET - its clips are the ones living inside the
        /// controller, and overwriting them destroys the override mapping. Every OTHER action is a VALUE
        /// SET, swapped in positionally when it Matches (TacActorAnimActions.cs:66-100).
        ///
        /// AND A NULL SLOT IS AN ANSWER, NOT A GAP. Navigation does not merely play an animation, it
        /// BLOCKS on one (TacticalNavigationComponent.cs:723-737), and the engine first asks the DEF
        /// whether the creature can do a thing: <c>UsesTurnAnimations = TurnSequence.HasAllAnimations ||
        /// (LeftLoop &amp;&amp; RightLoop)</c> (PathProcessorUtils.cs:306-328). Filling those slots on a
        /// donor that left them empty CLAIMS the creature turns in place; the controller has no such
        /// state to reach, so the animator sits in its idle and the move never starts. So a slot the
        /// donor left empty stays empty, and the TurnSequence family is CLEARED rather than mirrored -
        /// a downloaded .glb almost never carries a turn-in-place clip, and lerping round
        /// (FaceIn3d -> NoAnimsFace) finishes in a few frames.
        /// </summary>
        private static void WireClips(DefRepository repo, Creature c, TacActorAnimActionsDef anims)
        {
            StampEvents(c);

            List<string> wired = new List<string>(), empty = new List<string>();
            int noTurn = 0, fellBack = 0;
            TacActorAnimActionBaseDef[] source = anims.AnimActions ?? new TacActorAnimActionBaseDef[0];
            anims.AnimActions = source.Select(a =>
            {
                // Re-entrant: a second load in the same process sees OUR clones here already.
                if (a == null || a.name.StartsWith(Prefix, StringComparison.Ordinal)) return a;
                if (IsDefault(a)) return a;
                // The fourth type is the one an ATTACK reads, and leaving it out makes melee cost thirty
                // seconds even after the Action state is pointed at our clip: BashAbility.BashCrt:425-426
                // REWRITES DefaultActionClip with the matched TacActorAimingAbilityAnimActionDef's own
                // Clip at the moment of the swing.
                // The fifth type is the FLINCH. TacticalActor.cs:1597-1601 resolves the reaction clip
                // through TryGetAnimAction<TacActorSimpleReactionAnimActionDef>, so a creature whose
                // reaction actions still hold the DONOR's clips answers SetTrigger("Reaction") with an
                // animation that names none of our bones - the actor freezes instead of flinching.
                if (!(a is TacActorIdleAnimActionDef || a is TacActorNavAnimActionDef ||
                      a is TacActorShootAnimActionDef || a is TacActorAimingAbilityAnimActionDef ||
                      a is TacActorSimpleReactionAnimActionDef)) return a;

                TacActorAnimActionBaseDef clone = Clone(repo, c, a, a.name);
                int hits = 0;
                foreach (string slot in Slots(clone).ToArray())
                {
                    // A CAPABILITY WE CANNOT PERFORM MUST NOT BE CLAIMED. This started as a
                    // TurnSequence-only rule and the reason generalises to every family below.
                    //
                    // The engine asks the DEF whether the creature can do a thing, then trusts the
                    // answer completely. ClimbPathProcessor.EmitClimb:90 builds precise, measured climb
                    // points only when `anims.HasAllAnimations`, and otherwise calls EmitClimbFallback
                    // (:107-110) - a real, shipped degrade path. PathProcessorUtils:306-328 does the
                    // same for turning. So an EMPTY sequence is SAFE and a FILLED one is a promise.
                    //
                    // Filling these with our walk or idle is what breaks it, and the role machinery
                    // does exactly that unless stopped: "ClimbUpLadder.Start" tokenises to climb ->
                    // walk, "DropDown" -> walk, "FallNoSupport" -> walk, "Mount" -> walk, and the jump
                    // family is unmapped so it falls back to the IDLE. Every traversal slot then holds
                    // a clip that travels flat or not at all, HasAllAnimations turns TRUE, and the game
                    // emits a vertical segment measured off an animation that never rises. The mover
                    // waits to arrive somewhere the clip can never take it - which is the user's spider
                    // FROZEN half way through a window, not a refused order.
                    //
                    // This is the THIRD instance of one family: turn-in-place asking for a clip the rig
                    // lacked, Run.* misclassified as idle, and now the whole traversal set. A
                    // downloaded .glb ships a walk and an idle; it does not ship a ladder climb.
                    //
                    // ponytail: cleared wholesale, because a flat clip can never satisfy a vertical
                    // segment - mapping "jump" in the manifest is NOT yet enough and deliberately does
                    // not re-open these. Upgrade path: when an author supplies a real per-family clip,
                    // fill that family alone and leave the rest cleared.
                    if (Traversal.Any(t => slot.StartsWith(t, StringComparison.Ordinal)))
                    {
                        if (GetSlot(clone, slot) != null) { SetSlot(clone, slot, null); noTurn++; }
                        continue;
                    }
                    AnimationClip donorClip = GetSlot(clone, slot);
                    if (donorClip == null) { if (empty.Count < 16) empty.Add(a.name + "." + slot); continue; }
                    // Consistent by construction with RemapController, which keys on the same vanilla
                    // name: whatever the controller will actually play in that state is what the def
                    // slot claims, so the navigation wait can never be left waiting for another clip.
                    // ...and a role this creature has no clip for falls back to the IDLE, never to the
                    // donor's clip. Leaving the donor's in place looks like "we changed nothing" but is
                    // strictly worse: it names none of our bones, so the state plays and the creature
                    // FREEZES. Same rule RemapController already states for an unknown vanilla name -
                    // unknown gets the idle, never nothing. Only the optional roles can reach this.
                    string role = RoleFor(a, donorClip, slot);
                    AnimationClip mine = c.OurClip(role);
                    if (mine == null) { mine = c.OurClip("idle"); if (mine != null) fellBack++; }
                    if (mine == null || !SetSlot(clone, slot, mine)) continue;
                    hits++;
                }
                // The ROLE is in the log for the single-clip ability actions, because that is the one a
                // wrong answer hides in: an attack wired to the idle still reports a hit and still says
                // PASS, and the only symptom is ten seconds of nothing per swing.
                if (hits > 0)
                    wired.Add(a.name + ":" + hits +
                              (a is TacActorAimingAbilityAnimActionDef
                                   ? "->" + RoleFor(a, GetSlot(clone, "Clip")) : ""));
                return clone;
            }).ToArray();

            c.Say("ct_creature " + (wired.Count > 0 ? "PASS" : "FAIL") + " clips: " + wired.Count +
                  " non-default anim action(s) rewritten [" + string.Join(", ", wired.ToArray()) + "]; " +
                  source.Count(IsDefault) + " default action(s) left ALONE as the override keys; " +
                  fellBack + " slot(s) took the IDLE because this creature maps no clip to their role " +
                  "(optional roles only - a required one is refused at bake time); " +
                  empty.Count + " slot(s) LEFT EMPTY because the donor's own were empty; " + noTurn +
                  " TRAVERSAL slot(s) CLEARED (turn, skid, ladder, drop, vault, jump, mount, ram) " +
                  "because a FILLED one is a promise the engine trusts absolutely: " +
                  "ClimbPathProcessor.EmitClimb:90 only builds measured vertical points when " +
                  "HasAllAnimations, and falls back safely (:107-110) when it cannot - so an empty " +
                  "slot degrades and a slot holding our flat walk or idle HANGS the mover half way " +
                  "through a window, waiting to arrive where the clip never goes");

            // DOES IT CYCLE. A non-looping idle or walk plays once and holds, which in game is
            // indistinguishable from "no animation at all" - so the loop flag is asserted, not assumed.
            // It comes from the BAKE: ppcontent.json's "loop" declaration -> m_MuscleClip.m_LoopTime.
            foreach (string role in new[] { "idle", "walk" })
            {
                AnimationClip clip = c.OurClip(role);
                if (clip == null) continue;
                c.Say("ct_creature " + (clip.isLooping ? "PASS" : "FAIL") + " role '" + role + "' = '" +
                      clip.name + "' isLooping=" + clip.isLooping + (clip.isLooping ? "" :
                      " <- MUST CYCLE AND DOES NOT: it will play once and hold, which looks like no " +
                      "animation. Name it in ppcontent.json's top-level \"loop\" declaration and re-bake."));
            }
        }

        /// <summary>
        /// Puts the manifest's blocking animation events onto the mod's own clips, once.
        ///
        /// The shape is the game's, read off AnimEventReceiver.cs:49-52,54-88: ONE function name for all
        /// of them - "OnAnimEvent" - with the event's real name in stringParameter (no whitespace, :66-80
        /// rejects it). Idempotent: a clip already carrying the event is skipped, so a second load cannot
        /// double-fire a shot. Failure is reported rather than thrown - a clip Unity refuses to edit
        /// still animates, it just stalls its ability for 10s per action.
        ///
        /// The TIMES come from the manifest and are never invented. Where a hit connects is a property of
        /// the ANIMATION; an engine that guessed would produce damage on the wrong frame, which reads as
        /// a bug in the game rather than in the mod.
        /// </summary>
        private static void StampEvents(Creature c)
        {
            List<string> added = new List<string>(), silent = new List<string>();
            foreach (string role in CreatureManifest.Roles)
            {
                AnimationClip clip = c.OurClip(role);
                if (clip == null) continue;
                CreatureManifest.Event[] events = c.Man.EventsFor(role);
                if (events.Length == 0) { silent.Add(role); continue; }
                foreach (CreatureManifest.Event e in events)
                {
                    try
                    {
                        // A NAME THAT IS A SHIPPED EVENT DEF MEANS SOUND, and it needs a different
                        // shape of animation event. The blocking ones the abilities wait for
                        // (ActionDo, ShootShot, Ragdoll) travel as a plain stringParameter. An
                        // EVENTUS event - a footstep, a swing, anything audible - travels as
                        // stringParameter "Event" plus the def itself in objectReferenceParameter:
                        // TacActorBaseEventusComponent.cs:34 registers the "Event" channel and :102
                        // casts objectReferenceParameter to TacticalEventDef to raise it. So one
                        // manifest line covers both, and which one it is, is decided by whether the
                        // name resolves to a def rather than by a second key the author must learn:
                        //   "attack": "ActionDo 0.25, ShootShot 0.55"        <- blocking waits
                        //   "walk":   "SwarmerStep_EventDef 0.15, SwarmerStep_EventDef 0.65"  <- sound
                        // Shipped defs carry their own Wwise event, surface parameter setter and
                        // particles, so pointing at one costs no audio asset and no soundbank.
                        // BY NAME, not by guid. DefRepository.cs:70 GetDef takes a GUID - handing it a
                        // def NAME returns null without complaint, which silently turned a footstep
                        // into an inert string event and cost a whole gate run to notice. Same
                        // by-name scan the donor lookup uses.
                        TacticalEventDef sound = Repo.GetAllDefs<TacticalEventDef>()
                            .FirstOrDefault(d => string.Equals(d.name, e.Name, StringComparison.OrdinalIgnoreCase));
                        string channel = sound == null ? e.Name : EventusChannel;
                        float at = clip.length * e.At;
                        // Re-entrancy: a sound event's stringParameter is always "Event", so comparing
                        // that alone would let a second load stamp every footstep twice. The identity
                        // of one of these is channel + def + time.
                        if (clip.events.Any(x => x.stringParameter == channel &&
                                                 x.objectReferenceParameter == (UnityEngine.Object)sound &&
                                                 Mathf.Abs(x.time - at) < 1e-3f)) continue;
                        clip.AddEvent(new AnimationEvent
                        {
                            functionName = "OnAnimEvent",
                            stringParameter = channel,
                            objectReferenceParameter = sound,
                            time = at
                        });
                        added.Add(clip.name + ":" + e.Name + (sound == null ? "" : "(sound)") +
                                  "@" + e.At.ToString("F2"));
                    }
                    catch (Exception ex)
                    {
                        c.Say("ct_creature FAIL '" + clip.name + "' refused AddEvent(" + e.Name + "): " + ex.Message);
                        return;
                    }
                }
            }
            c.Say("ct_creature " + (added.Count > 0 ? "PASS" : "WARN") + " " + added.Count +
                  " animation event(s) stamped as OnAnimEvent(<name>) [" + string.Join(", ", added.ToArray()) +
                  "]" + (silent.Count == 0 ? "" : "; role(s) [" + string.Join(", ", silent.ToArray()) +
                  "] declare NO events in ppcontent.json \"creature\": \"events\" - each blocking event " +
                  "the game waits for and does not get costs 10s per action (AnimEventReceiver.cs:100,126)"));
        }

        private static bool IsDefault(TacActorAnimActionBaseDef a)
        {
            return (a is TacActorIdleAnimActionDef i && i.IsDefaultAnimatorClips)
                || (a is TacActorNavAnimActionDef n && n.IsDefaultAnimatorClips)
                || (a is TacActorShootAnimActionDef s && s.IsDefaultAnimatorClips);
        }

        /// <summary>Every AnimationClip field on an anim action, including the ones one level down inside
        /// a plain serializable holder (ClipSequence Start/Loop/Stop, TurnAnimationSequence ...), named as
        /// a dotted path. Unity objects are skipped, so def references are never walked.</summary>
        /// <summary>
        /// PUBLIC AND PRIVATE, because Unity serializes both and the game reads both.
        /// The reaction action keeps its two clips in <c>private [SerializeField] _highReactionClip</c>
        /// and <c>_lowReactionClip</c> (TacActorSimpleReactionAnimActionDef), so a public-only walk
        /// found no slots at all, rewrote nothing, and reported the action as untouched - the creature
        /// kept the DONOR's flinch and froze on SetTrigger("Reaction"). Widening is safe by
        /// construction: every use below filters on <c>FieldType == typeof(AnimationClip)</c>, so the
        /// only extra fields this can reach ARE serialized clip slots.
        /// </summary>
        private const BindingFlags Fields =
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

        internal static IEnumerable<string> Slots(object target)
        {
            foreach (FieldInfo f in target.GetType().GetFields(Fields))
            {
                if (f.FieldType == typeof(AnimationClip)) { yield return f.Name; continue; }
                if (!f.FieldType.IsClass || f.FieldType.IsArray ||
                    typeof(UnityEngine.Object).IsAssignableFrom(f.FieldType)) continue;
                if (f.GetValue(target) == null) continue;
                foreach (FieldInfo g in f.FieldType.GetFields(Fields))
                    if (g.FieldType == typeof(AnimationClip)) yield return f.Name + "." + g.Name;
            }
        }

        internal static bool SetSlot(object target, string path, AnimationClip clip)
        {
            int dot = path.IndexOf('.');
            if (dot < 0)
            {
                FieldInfo f = target.GetType().GetField(path, Fields);
                if (f == null || f.FieldType != typeof(AnimationClip)) return false;
                f.SetValue(target, clip);
                return true;
            }
            FieldInfo outer = target.GetType().GetField(path.Substring(0, dot), Fields);
            object nested = outer?.GetValue(target);
            return nested != null && SetSlot(nested, path.Substring(dot + 1), clip);
        }

        /// <summary>The clip currently in a slot - the DONOR's, when read off a fresh clone. Null both for
        /// "the donor left it empty" and for "no such field", which is the same answer here.</summary>
        internal static AnimationClip GetSlot(object target, string path)
        {
            int dot = path.IndexOf('.');
            if (dot < 0)
            {
                FieldInfo f = target.GetType().GetField(path, Fields);
                return f == null || f.FieldType != typeof(AnimationClip) ? null : (AnimationClip)f.GetValue(target);
            }
            FieldInfo outer = target.GetType().GetField(path.Substring(0, dot), Fields);
            object nested = outer?.GetValue(target);
            return nested == null ? null : GetSlot(nested, path.Substring(dot + 1));
        }

        /// <summary>
        /// ============ THE BRIDGE, AT THE LAYER THAT ACTUALLY DECIDES ============
        /// The def-level slots are only ever KEYS INTO the controller: TacActorAnimActions captures the
        /// DEFAULT action's clips as _clipKeys (:52-54) and then ApplyOverrides(_clipKeys, ...) (:79),
        /// and AnimatorClipOverrides DROPS the pair when the key is null (:27-31) while Unity silently
        /// ignores a key that is not a clip inside the controller. So a mod's clip reaches the screen
        /// only where the donor's default action happens to hold the very clip that state plays - which
        /// is why "walk works, the rest do not" is the classic symptom.
        ///
        /// THE FIX: override every clip the controller actually contains, by name, once, at the moment
        /// the game hands us the override table. No dependency on which donor field is null.
        ///
        /// AND THE ACTION STATE IS NAMED, NOT MATCHED. One clip in a shipped controller has a name that
        /// says nothing about what it is for ("HL_ActionPlaceholder" tokenises to hl/action/placeholder,
        /// none of which is a weapon word), so a keyword rule classifies it as an idle and EVERY action
        /// then waits out three ten-second timeouts. The game does not guess it either - it NAMES it:
        ///   TacActorAnimActionsDef.cs:15   public AnimationClip DefaultActionClip;
        ///   TacticalActorBase.OverrideDefaultActionAnimationClip:1062
        /// so it is taken from there by identity and never matched by name.
        /// </summary>
        internal static void RemapController(Creature c, AnimatorClipOverrides overrides,
                                             AnimationClip actionKey, string who)
        {
            if (overrides == null) return;
            List<string> map = new List<string>();
            int missed = 0;
            foreach (AnimationClip key in overrides.GetOverridableClips().ToArray())
            {
                if (key == null) { missed++; continue; }
                bool isAction = actionKey != null && key == actionKey;
                AnimationClip mine = c.OurClip(isAction ? "attack" : RoleForVanilla(key.name));
                if (mine == null) { missed++; continue; }
                overrides[key] = mine;
                map.Add(key.name + " -> " + mine.name + (isAction ? " (DefaultActionClip)" : ""));
            }
            overrides.ApplyOverrides();
            c.Say("ct_creature " + (map.Count > 0 && actionKey != null ? "PASS" : "FAIL") + " (" + who +
                  ") '" + overrides.Controller.name + "' had " + (map.Count + missed) +
                  " overridable clip(s); " + map.Count + " now play OUR clips" +
                  (missed > 0 ? " (" + missed + " skipped: null key or no clip for that role)" : "") +
                  (actionKey == null ? "; the anim-actions def names NO DefaultActionClip, so the Action " +
                   "state cannot be reached and every ability will eat three 10s timeouts" : "") +
                  " -> " + string.Join(", ", map.ToArray()));
        }

        // ------------------------------------------------------------------ the squad

        /// <summary>
        /// Put <paramref name="def"/> in the player's first vehicle, then PROVE it is there.
        ///
        /// NOT by appending to GameDifficultyLevelDef.StartingSquadTemplate. That array is what
        /// GeoPhoenixFaction.CreateInitialSquad:1964-1976 walks and it looks like the obvious hook, but
        /// it is not the only squad builder and appending to it actively MISFIRES when the tutorial is
        /// on: GeoscapeTutorial.InitSquad:313 reads the array for its LENGTH only and fills the gap with
        /// one FIXED human template - so the append produces an extra soldier and never the creature.
        ///
        /// AddCharacter never refuses either - it computes the space sum and throws it away
        /// (GeoVehicle.cs:759-764) - so "we called Add" is not evidence of anything, and the roster is
        /// read back by template identity afterwards.
        /// </summary>
        public static void JoinPlayerVehicle(TacCharacterDef def, string who)
        {
            try
            {
                GeoLevelController level = GameUtl.CurrentLevel() == null
                    ? null : GameUtl.CurrentLevel().GetComponent<GeoLevelController>();
                GeoVehicle vehicle = level == null || level.PhoenixFaction == null
                    ? null : level.PhoenixFaction.Vehicles.FirstOrDefault();
                if (def == null || vehicle == null)
                {
                    ContentToolMain.Say("ct_creature FAIL roster (" + who + ") def=" + (def != null) +
                                        " vehicle=" + (vehicle != null) + " - nothing was added");
                    return;
                }
                if (!vehicle.Units.Any(u => u.TemplateDef == def))
                    vehicle.AddCharacter(level.CreateCharacterFromTemplate(
                        def, level.PhoenixFaction, null,
                        level.CurrentDifficultyLevel.StartingSquadGenerationParams));

                string[] aboard = vehicle.Units.Select(u => u.TemplateDef.name).ToArray();
                bool there = vehicle.Units.Any(u => u.TemplateDef == def);
                ContentToolMain.Say("ct_creature " + (there ? "PASS" : "FAIL") + " roster (" + who + ") '" +
                    vehicle.Name + "' carries " + aboard.Length + " unit(s), space " +
                    vehicle.UsedCharacterSpace + "/" + vehicle.MaxCharacterSpace + ": " +
                    string.Join(", ", aboard) + (there ? "" : " <- THE CREATURE IS NOT IN THE AIRCRAFT"));
            }
            catch (Exception ex) { ContentToolMain.Say("ct_creature FAIL roster (" + who + ") " + ex); }
        }

        // ------------------------------------------------------------------ plumbing

        /// <summary>The creature wearing <paramref name="def"/>'s anim actions, or null.</summary>
        internal static Creature ByAnims(TacActorAnimActionsDef def)
        {
            return def == null ? null : Built.FirstOrDefault(x => ReferenceEquals(x.Anims, def));
        }

        /// <summary>The creature wearing <paramref name="def"/>, or null. Identity, never a name prefix:
        /// two mods may not collide, and a def cannot be renamed out from under this.</summary>
        internal static Creature ByBase(TacticalActorBaseDef def)
        {
            return def == null ? null : Built.FirstOrDefault(x => ReferenceEquals(x.BaseDef, def));
        }

        /// <summary>The creature whose rig <paramref name="def"/> manages, or null.</summary>
        internal static Creature ByManager(AddonsManagerDef def)
        {
            return def == null ? null : Built.FirstOrDefault(x => ReferenceEquals(x.Manager, def));
        }

        private static void Install()
        {
            if (harmony != null) return;
            harmony = new Harmony(HarmonyId);
            harmony.PatchAll(typeof(CreatureBuild).Assembly);
        }

        /// <summary>
        /// The game's own def factory, and it must be <c>CreateDef</c> - NOT <c>CreateRuntimeDef</c>.
        /// Both are Instantiate + Guid + registration (DefRepository.cs:214-276), but runtime defs are
        /// SWEPT (GeoLevelController.cs:185,750-753; TacticalLevelController.cs:131-137;
        /// PhoenixSaveManager.cs:370) and a swept def is Destroyed, so a saved reference to it becomes a
        /// dead Unity object the first time the player returns from a mission.
        ///
        /// The Guid is DERIVED from the mod id and the name, so the set can never drift out of sync with
        /// itself and re-entry returns the existing def rather than throwing on a duplicate-key Add.
        /// </summary>
        internal static T Clone<T>(DefRepository repo, Creature c, T original, string name) where T : BaseDef
        {
            string full = Prefix + c.Id + "_" + name;
            string guid;
            using (var md5 = System.Security.Cryptography.MD5.Create())
                guid = new Guid(md5.ComputeHash(System.Text.Encoding.UTF8.GetBytes(c.Id + "/" + name)))
                       .ToString("B").ToUpperInvariant();
            T existing = repo.GetDef(guid) as T;
            if (existing != null) return existing;
            T copy = (T)repo.CreateDef(guid, original);
            copy.name = full;
            return copy;
        }

        /// <summary>A fresh tag list holding <paramref name="src"/> minus <paramref name="banned"/>. A NEW
        /// list, never the donor's mutated in place - two defs sharing one GameTagsList would make
        /// stripping our tag strip the shipped unit's too.</summary>
        internal static GameTagsList Purge(IEnumerable<GameTagDef> src, params GameTagDef[] banned)
        {
            GameTagsList list = new GameTagsList();
            list.AddRange(src.Where(t => t != null && !banned.Contains(t)));
            return list;
        }

        /// <summary>The SharedGameTags member with that name - a tag is the only stable handle to a
        /// shipped family, and reflection keeps the manifest's "donor" a plain word.</summary>
        private static GameTagDef TagNamed(SharedGameTagsDataDef shared, string name)
        {
            FieldInfo f = shared.GetType().GetField(name);
            if (f != null && typeof(GameTagDef).IsAssignableFrom(f.FieldType))
                return (GameTagDef)f.GetValue(shared);
            PropertyInfo p = shared.GetType().GetProperty(name);
            return p != null && typeof(GameTagDef).IsAssignableFrom(p.PropertyType)
                 ? (GameTagDef)p.GetValue(shared) : null;
        }

        internal static Transform FindDeep(Transform t, string name)
        {
            foreach (Transform c in t.GetComponentsInChildren<Transform>(true))
                if (c.name == name) return c;
            return null;
        }

        private static string Regexy(string json, string key)
        {
            return System.Text.RegularExpressions.Regex
                .Match(json, "\"" + key + "\"\\s*:\\s*\"([^\"]*)\"").Groups[1].Value;
        }

        private static float Number(string json, string key)
        {
            float v;
            float.TryParse(System.Text.RegularExpressions.Regex
                    .Match(json, "\"" + key + "\"\\s*:\\s*\"?([-0-9.eE+]*)\"?").Groups[1].Value,
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out v);
            return v;
        }
    }

    /// <summary>
    /// One built creature's runtime state. It is an OBJECT and not a set of statics because a game may
    /// carry several content mods, each with its own model, clips and scale - statics would let the
    /// second one silently overwrite the first.
    /// </summary>
    internal sealed class Creature
    {
        internal string Id;
        internal CreatureManifest Man;
        internal AnimationClip[] Clips;
        internal Action<string> Say;
        /// <summary>The baked mesh, so a postfix can tell OUR rig from whatever else the game hands it.</summary>
        internal Mesh Mesh;
        internal TacCharacterDef Def;
        internal TacActorAnimActionsDef Anims;
        internal TacticalActorBaseDef BaseDef;
        /// <summary>Our cloned addons manager, so the SetupRig postfix can tell OUR rig from every
        /// other rig the game instantiates through that same one method.</summary>
        internal AddonsManagerDef Manager;
        internal Vector3 Up = Vector3.up;
        internal float Scale = 1f;

        /// <summary>
        /// The bundle clip playing <paramref name="role"/>, or null when the manifest maps none.
        ///
        /// Matched by SUFFIX, not by full address: the bundle names a clip
        /// "&lt;model stem&gt;_&lt;clip name in the file&gt;" lowercased, so re-stemming the model file
        /// cannot silently unbind everything.
        /// </summary>
        internal AnimationClip OurClip(string role)
        {
            string name = Man.ClipFor(role);
            return name == null ? null
                 : Clips.FirstOrDefault(c => c.name.EndsWith(name, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>The creature whose rig wears <paramref name="from"/>'s mesh - not merely the first
        /// Animator in the hierarchy. On the roster path there are TWO rigs under the character builder
        /// for one frame (AddonsCharacterBuilder.UseAddonManager:153-158 Destroys the outgoing one and
        /// Unity's Destroy is deferred to the end of the frame), so a plain GetComponentInChildren
        /// returns the corpse.</summary>
        internal Animator OurAnimator(Component from)
        {
            Animator[] all = from.GetComponentsInChildren<Animator>(true);
            foreach (Animator a in all)
            {
                SkinnedMeshRenderer s = a.GetComponentInChildren<SkinnedMeshRenderer>(true);
                if (Mesh != null && s != null && s.sharedMesh == Mesh) return a;
            }
            return all.Length > 0 ? all[0] : null;
        }
    }

    /// <summary>
    /// The one patch a creature needs on the call that hands out the override table:
    /// <c>TacActorAnimActions.Setup</c>, reached from TacticalActor.PrepareEnterPlay (:724-726). Every
    /// actor passes through it; only a creature this engine built is touched.
    /// </summary>
    [HarmonyPatch(typeof(TacActorAnimActions), nameof(TacActorAnimActions.Setup))]
    internal static class CreatureRemap
    {
        private static void Postfix(TacActorAnimActions __instance, AnimatorClipOverrides animatorOverrides,
                                    TacticalActor tacticalActor)
        {
            try
            {
                TacActorAnimActionsDef def = __instance.TacActorAnimActionsDef;
                Creature c = CreatureBuild.ByAnims(def);
                if (c == null) return;
                // tacticalActor is NULL on the geoscape roster path (DisplayCharacter:49 passes null),
                // which is exactly the path that shows no model - so both are reported, separately.
                string who = tacticalActor == null ? "roster" : "tactical";
                CreatureBuild.RemapController(c, animatorOverrides, def.DefaultActionClip, who);
                Animator animator = c.OurAnimator(__instance);
                if (animator == null)
                {
                    c.Say("ct_creature FAIL (" + who + ") no Animator under the anim-actions component - " +
                          "nothing to measure and nothing to render");
                    return;
                }
                // Enough on the ROSTER path: there the rig root is reset once by SetupRig
                // (AddonsManager.cs:115) BEFORE this postfix. The tactical path resets it again
                // afterwards, which is what CreatureKeepsItsPose is for.
                CreatureBuild.Orient(c, animator.transform, who);
            }
            catch (Exception ex) { ContentToolMain.Say("ct_creature FAIL remap " + ex); }
        }
    }

    /// <summary>
    /// ============ THE ONE PLACE EVERY RIG IS BORN ============
    /// Weapon.GetShotOrigin does not measure the actor: it poses a CharacterTargetDummy
    /// (PlaceForShooting -> PlaceForClip, which sets RigRoot.position) and reads the muzzle transform
    /// off THAT rig. Every rig, the dummy's included, is instantiated by AddonsManager.SetupRig:114-115,
    /// which resets it one line later - and UnityUtil.ResetTransform:213-218 sets localScale = ONE. So
    /// the dummy stood at scale 1 while the live actor stood at 0.008 and every bone offset it reported
    /// came out 125x too far. The shot left from the sky and hit nothing.
    ///
    /// THIS SEAM WAS ONCE NARROWED TO THE DUMMY'S OWN CONSTRUCTOR AND THAT WAS A MISTAKE, TWICE OVER.
    /// The reason for narrowing was a suspicion that scaling the live actor EARLIER was what stopped
    /// the creature walking. That suspicion was wrong - the walk was broken by a clip-role misread
    /// (see RoleFor: the navigation Run slots were wired to the idle) and had nothing to do with
    /// scale. And the narrow patch did not even work: MEASURED, it produced no line at all and the
    /// dummy went back to scale 1, origin (-39.98, 24.40, -48.11), four shots and no damage.
    ///
    /// So it patches the method they ALL route through - actor (AddonsComponent.cs:16), the shooting
    /// ruler (CharacterTargetDummy.cs:80), the ragdoll (RagdollDummy.cs:53), the geoscape roster
    /// (AddonsCharacterBuilder.cs:127) and DropItemsEffect. The other two postfixes stay because the
    /// game resets the rig AGAIN after this on their paths (TacticalActorBase.OnEnterPlay:539);
    /// Orient ASSIGNS rather than accumulates, so running it more than once writes the same values.
    ///
    /// The timing worry is no longer a matter of opinion: C1-walk gates it. With this patch in place
    /// the creature walks 2.83 of 2.83 tiles at 4.10 tile/s AND the spit lands.
    /// </summary>
    [HarmonyPatch(typeof(AddonsManager), nameof(AddonsManager.SetupRig))]
    internal static class CreatureRigIsScaled
    {
        private static void Postfix(AddonsManager __instance)
        {
            try
            {
                Creature c = CreatureBuild.ByManager(__instance.AddonsManagerDef);
                if (c == null) return;
                CreatureBuild.Orient(c, __instance.RigRoot, "SetupRig/" + __instance.GetType().Name);
            }
            catch (Exception ex) { ContentToolMain.Say("ct_creature FAIL rig scale " + ex); }
        }
    }

    /// <summary>
    /// ============ THE SEAM THE ORIENTATION HAS TO LAND ON ============
    /// "The creature stands on end, half sunk into the asphalt" is never the angle - it is the TIMING:
    ///   TacticalActor.PrepareEnterPlay:717-726   ActorAnimActions.Setup(...)     &lt;- the remap postfix
    ///   TacticalActor.OnEnterPlay:731-733        base.OnEnterPlay()
    ///   TacticalActorBase.OnEnterPlay:539          AddonsManager?.RigRoot.ResetTransform();
    /// PrepareEnterPlay ALWAYS runs first, so everything the Setup postfix wrote onto the rig root -
    /// rotation, scale AND position - is wiped before a frame is drawn, and the player sees the raw
    /// imported pose. A postfix HERE runs after :539 and after base.OnEnterPlay returns, so it is the
    /// last word on that transform; FinalizeEnterPlay:543+ does not touch it.
    /// ponytail: one postfix on the method that does the resetting, not a per-frame LateUpdate fighting
    /// it, and not a wrapper GameObject - a wrapper is what nulls DisplayCharacter:42-43's
    /// GetComponent&lt;Animator&gt;() and leaves the roster with no model at all.
    /// </summary>
    [HarmonyPatch(typeof(TacticalActor), "OnEnterPlay")]
    internal static class CreatureKeepsItsPose
    {
        private static void Postfix(TacticalActor __instance)
        {
            try
            {
                Creature c = CreatureBuild.ByBase(__instance.TacticalActorBaseDef);
                if (c == null) return;
                AddonsManager addons = __instance.AddonsManager;
                CreatureBuild.Orient(c, addons == null ? null : addons.RigRoot, "OnEnterPlay");
                // THE FOOTPRINT, MEASURED ON THE LIVE ACTOR. The def-level line proves which string was
                // written; only the level's own NavMesh can turn that string into a radius, and 0.75 is
                // the engine's own big/small line (TacticalActor.cs:1813).
                TacticalNavigationComponent nav = __instance.TacticalNav;
                float r = nav == null ? -1f : nav.AgentNavSettings.AgentRadius;
                c.Say("ct_creature " + (r > 0f && r <= 0.75f ? "PASS" : "WARN") + " (OnEnterPlay) agent '" +
                      (nav == null ? "?" : nav.TacticalNavDef.AgentType) + "' resolves to AgentRadius " +
                      r.ToString("F3") + (r > 0.75f ? " - over the 0.75 the game itself calls a big unit, " +
                      "so it stands on more than one tile" : ""));
            }
            catch (Exception ex) { ContentToolMain.Say("ct_creature FAIL pose " + ex); }
        }
    }

    // ============ WHY A MOVE ORDER NEVER FINISHES, AND WHERE THAT IS FIXED ============
    // One tile into the first move, TacticalZoneObjective.OnActorMovedInANewTile throws and takes the
    // mover's coroutine with it. IL 0x00042 in that handler is the second half of
    //   flag = flag || _boxTrigger.bounds.Contains(actor.GetAimPoint().position);   (:60)
    // - the `||` means it runs for essentially every step of every move - and the null is the aim point:
    //   TacticalActor.cs:1475-1478  GetAimPoint = RootAddon.FindAddonSlot(DefaultAimSlot).GetAimPoint()
    //   ItemSlot.cs:180-185         returns null when the slot HAS NO ATTACHED ADDONS
    // A creature assembled out of a model file has none by construction, so ExecutePoints dies
    // mid-path, PlayingAction never completes, and the actor stays "playing" for ever.
    //
    // There is deliberately NO patch for that here. CreatureFit.AfterAimPoint already answers it for
    // ANY actor whose GetAimPoint came back null - which is a superset of ours - and it answers it
    // BETTER, preferring a modder's own `ct_aim` transform, then the fitted hitbox (the middle of the
    // creature, which is what an aim point is for), and only then the actor's transform, which is the
    // base class's own answer (TacticalActorBase.cs:754-756). A second postfix here would be a
    // duplicate on a method called once per tile per move, and whichever of the two ran first would
    // decide - so the better one could silently lose. CreatureFit is installed unconditionally by
    // ContentToolMain, before any content mod's OnModEnabled, so it is always in place first.
}
