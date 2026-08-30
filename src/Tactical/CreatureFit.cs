using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using HarmonyLib;
using PhoenixPoint.Common.Entities.Addons;
using PhoenixPoint.Common.Utils;
using PhoenixPoint.Tactical.Entities;
using PhoenixPoint.Tactical.Entities.Weapons;
using UnityEngine;

namespace Morgott.ContentTool.Tactical
{
    /// <summary>
    /// ============ WHAT MAKES A DOWNLOADED MODEL A CREATURE THE GAME CAN SHOOT ============
    ///
    /// Phoenix Point creates NO collider in code. It READS colliders that an artist authored onto a
    /// bodypart addon prefab, keeps the ones on layer <c>Characters</c> and hangs a
    /// <c>DamageProxy</c> on each (TacticalItem.OnVisualsChanged:746-773, Addon.cs:1384-1391). A
    /// creature assembled out of a .glb has no such prefab and no bodypart items at all, so it ends
    /// up with ZERO colliders - and a tactical actor with zero colliders is a ghost:
    ///   * the mouse cannot find it. Picking raycasts a SEPARATE layer, <c>CameraCollider</c>
    ///     (TacticalView.SelectAtCursor:701, mask :714), cached once per actor by
    ///     TacticalActorViewBase.CacheCameraColliders:474 - a plain
    ///     <c>GetComponentsInChildren&lt;Collider&gt;()</c> filtered by layer.
    ///   * nothing can shoot it. Fire resolution casts <c>Characters</c> and turns the collider it
    ///     hit into a receiver with <c>TacUtil.GetDamageReceiverFromTransform:142</c>, which is
    ///     <c>GetComponentInParent&lt;IDamageReceiver&gt;()</c>.
    ///
    /// That second call is the whole reason this adapter can stay thin. The actor component ITSELF
    /// implements IDamageReceiver and answers <c>GetDamageReceiverForHit =&gt; this</c>
    /// (TacticalActorBase.cs:779-782), so ANY collider parented under the actor resolves to the
    /// actor, with no DamageProxy, no synthetic bodypart item and no second damage path. Damage then
    /// runs the game's own route - ApplyDamageInternal:874 <c>Health.Subtract</c> -&gt;
    /// OnHealthChange:616-622 -&gt; <c>Die()</c> -&gt; the ragdoll die ability.
    ///
    /// So the engine's whole job is: PUT A COLLIDER THERE, on the two layers the game reads, sized
    /// to the model that is actually on screen. Everything downstream is vanilla.
    ///
    /// WHAT THIS DOES NOT DECIDE. Bone choice, capsule size, which transform is the aim point and
    /// when a clip's damage frame fires are properties of a CREATURE, not of an engine - a biped, a
    /// flyer and a 3x3 walker want different answers and hardcoding a spider's would be wrong for
    /// the next modder. The engine owns the MECHANISM and one measured fallback; the content mod
    /// overrides through the manifest it already ships (see <see cref="CreatureManifest"/>).
    ///
    /// AND IT REFUSES RATHER THAN GUESSES. A wrong rig scale corrupts the collider, the aim point
    /// and root motion at once and looks like a game bug, not a mod bug; a guessed animation-event
    /// time produces damage that lands on the wrong frame. Both are checked out loud below.
    /// </summary>
    internal static class CreatureFit
    {
        private const string HarmonyId = "morgott.contenttool.creaturefit";
        /// <summary>Names the fitted objects carry, so a re-fit, the ragdoll-mode postfix and the
        /// gate can all find them again without a registry to keep in step.</summary>
        internal const string HitName = "ct_hitbox";
        internal const string PickName = "ct_pickbox";
        /// <summary>The transform a modder can author into the .glb to place the aim point by hand.
        /// Same shape as the game's own <c>TacticalItemDef.AimPoint</c>, which is also a NAME
        /// (TacticalItem.SetupAimPoint:702-710).</summary>
        internal const string AimName = "ct_aim";

        /// <summary>The events the game BLOCKS on. Each is waited for by name with a 10s timeout
        /// (AnimEventReceiver.cs:100,126); a clip that carries none stalls its ability for ten
        /// seconds and logs "the event is likely missing from the animator". The engine cannot
        /// invent the times - where a hit connects is per-animation - so it only reports.</summary>
        private static readonly string[] BlockingEvents = { "ShootShot", "ActionDo", "ActionEnd", "Ragdoll" };

        /// <summary>
        /// A rendered creature is at least this many tiles across and at most that many. Both are
        /// deliberately generous - a shipped Mutog and a shipped soldier are inside them by a wide
        /// margin - because this is not a style check, it is the guard against the ONE mistake that
        /// corrupts everything silently: a rig whose scale is off by the 100x an exporter's root
        /// node carries. ponytail: two constants, not a curve. Widen them if a genuine boss unit
        /// ever trips this.
        /// </summary>
        private const float MinSpan = 0.10f, MaxSpan = 12f;

        private static Harmony harmony;
        private static FieldInfo cameraCache;
        private static Action<string> say;

        internal static void Install(Action<string> log)
        {
            if (harmony != null) return;
            say = log;
            harmony = new Harmony(HarmonyId);
            // FinalizeEnterPlay and not OnEnterPlay: OnEnterPlay is where the rig root is RESET
            // (TacticalActorBase.cs:539) and where a content mod's own postfix puts the creature the
            // right way up and at the right scale. Measuring the model before that runs would measure
            // the raw import - which for a .glb carrying an exporter's scale-100 node is 100x out, and
            // the span guard below would refuse a perfectly good creature. FinalizeEnterPlay:543 is
            // the game's own "everything is set up" seam and runs strictly after.
            harmony.Patch(AccessTools.Method(typeof(TacticalActorBase), "FinalizeEnterPlay"),
                postfix: new HarmonyMethod(AccessTools.Method(typeof(CreatureFit), nameof(AfterEnterPlay))));
            // The aim point, for an actor that has no bodypart to hang one on. See AfterAimPoint.
            harmony.Patch(AccessTools.Method(typeof(TacticalActor), nameof(TacticalActor.GetAimPoint)),
                postfix: new HarmonyMethod(AccessTools.Method(typeof(CreatureFit), nameof(AfterAimPoint))));
            // ...and its PLURAL, which is a different list with a different failure. See AfterAimPoints.
            harmony.Patch(AccessTools.Method(typeof(TacticalActor), nameof(TacticalActor.GetAimPoints)),
                postfix: new HarmonyMethod(AccessTools.Method(typeof(CreatureFit), nameof(AfterAimPoints))));
            // Vanilla Characters colliders are OFF except while something is aiming, and the switch is
            // AddonsManager.SetRagdollMode -> Addon.RefreshCollidersRagdoll:1499-1505. That loop only
            // walks colliders an ADDON owns, so ours would never be told. Mirroring the same rule here
            // keeps the creature from blocking its own line of fire the way no shipped unit does.
            harmony.Patch(AccessTools.Method(typeof(AddonsManager), nameof(AddonsManager.SetRagdollMode)),
                postfix: new HarmonyMethod(AccessTools.Method(typeof(CreatureFit), nameof(AfterRagdollMode))));
            // The aim IK, for an actor whose model carries no FinalIK rig. See BeforeSetupAimIK.
            harmony.Patch(AccessTools.Method(typeof(Weapon), nameof(Weapon.TrySetupAimIK)),
                prefix: new HarmonyMethod(AccessTools.Method(typeof(CreatureFit), nameof(BeforeSetupAimIK))));
            cameraCache = AccessTools.Field(typeof(TacticalActorViewBase), "_cameraColliders");
        }

        internal static void Uninstall()
        {
            if (harmony == null) return;
            harmony.UnpatchAll(HarmonyId);
            harmony = null;
        }

        private static void Say(string m) { try { say?.Invoke(m); } catch (Exception) { } }

        // ------------------------------------------------------------------ the seams

        private static void AfterEnterPlay(TacticalActorBase __instance)
        {
            try { Fit(__instance); }
            // Never throw into the actor lifecycle: a half-entered actor is a broken mission, and a
            // creature that cannot be shot is still better than a mission that cannot be played.
            catch (Exception ex) { Say("ct_creature FAIL " + __instance.name + " fit THREW " + ex); }
        }

        /// <summary>
        /// The engine's answer for an actor with NO BODYPARTS, which is the base class's own answer.
        ///
        /// <c>TacticalActor.GetAimPoint:1475-1478</c> reads the aim transform off the addon in the
        /// default aim SLOT, and <c>ItemSlot.cs:180-185</c> returns null when that slot holds no
        /// addon. Null then reaches <c>TacticalZoneObjective.OnActorMovedInANewTile</c>, which
        /// dereferences it for every tile step of every move with no guard - one NRE per move, taken
        /// with the mover's coroutine. <c>TacticalActorBase.cs:754-756</c> - the override's own base -
        /// returns <c>transform</c> for exactly this case.
        ///
        /// Only fires where the game returned null, so every shipped actor answers first and is
        /// untouched. Prefers, in order: a transform the modder named <c>ct_aim</c> in the model, the
        /// fitted hitbox (the middle of the creature, which is what an aim point is for), the actor.
        /// </summary>
        private static void AfterAimPoint(TacticalActor __instance, ref Transform __result)
        {
            if (__result != null) return;
            try
            {
                Transform rig = __instance.AddonsManager?.RigRoot;
                // rig.Find is Unity's DIRECT-child lookup on purpose: the whole-model hull is a direct
                // child, while the optional per-bone spheres share its name deeper in the skeleton and
                // an aim point on one leg is not what "aim at this creature" means.
                __result = Find(rig, AimName) ?? (rig == null ? null : rig.Find(HitName)) ?? __instance.transform;
            }
            catch (Exception) { __result = __instance.transform; }
        }

        /// <summary>
        /// ============ THE SHOT THAT NEVER COMES BACK ============
        ///
        /// MEASURED 30.08.2026 (Player.log, 245,976 s): a custom soldier fired a pistol and the game
        /// stopped answering.
        ///   Ability Handgun_ShootAbilityDef ... Source: Morgott_VultureSidearm_WeaponDef
        ///   NullReferenceException
        ///     at Weapon.SetAimIKTarget (AimIK aimIK, ...) [0x00047]
        ///     at Weapon.TrySetupAimIK (TacticalAbilityTarget targetData) [0x00055]
        ///     at TacticalLevelController+&lt;FireWeaponAtTargetCrt&gt;d__322.MoveNext ()
        ///   CHECK/REPORT PREVIOUS ERROR!!! Broken coroutine call chain: FireWeaponAtTargetCrt ...
        /// The shot coroutine dies mid-flight, so the PlayingAction that started it is never completed
        /// and the actor never becomes idle again - the freeze the player sees.
        ///
        /// WHY. <c>TacticalActor.InitIK:2044</c> is <c>GetComponentInChildren&lt;AimIK&gt;()</c>: the FinalIK
        /// component lives on the shipped MODEL, and a creature built from a .glb has no such component,
        /// so <c>TacticalActor.AimIK</c> is null. <c>Weapon.TrySetupAimIK:962</c> then calls
        /// <c>AimEnable</c>, which returns AT ONCE on a null AimIK (<c>TacticalActor.cs:2016</c>) - and
        /// hands that same null straight to <c>SetAimIKTarget:1007</c>, which dereferences
        /// <c>aimIK.solver</c> unguarded. The engine KNOWS this can be null - <c>BashAbility.cs:440,444</c>
        /// tests exactly that before touching it, and guards its own teardown at :504 - so this is one
        /// missing check on one path, not an unsupported creature.
        ///
        /// THE ANSWER IS THE ENGINE'S OWN. Returning false is precisely what <c>ShouldUseAimIK</c>
        /// returning false already means, and every caller is written for it: <c>FireWeaponAtTargetCrt</c>
        /// carries it as <c>useAimIK</c> and skips <c>TacticalLevelController.cs:1784,1822</c> - the only
        /// other two unguarded dereferences in the shooting path - while <c>IdleAbility.GetAimIK:149-156</c>
        /// reaches its own null test first. So one prefix closes all of them.
        ///
        /// The cost is honest and cosmetic: no spine-bend towards the target. Aim IK is a LOOK, not the
        /// shot - the projectile, the damage and the animation are unaffected. Adding a real AimIK solver
        /// would mean choosing a bone chain and an aim axis for a foreign rig, which is a guess that
        /// deforms the model when it is wrong; a creature that shoots straight and does not lean is
        /// strictly better than one that hangs.
        ///
        /// Only where the game itself would have thrown - a shipped actor has its AimIK and is untouched.
        /// </summary>
        private static bool BeforeSetupAimIK(Weapon __instance, ref bool __result)
        {
            try
            {
                TacticalActor actor = __instance.TacticalActor;
                if (actor == null || actor.AimIK != null) return true;   // run the original
                actor.AimEnable(enable: false);
                __result = false;
                return false;
            }
            catch (Exception) { return true; }
        }

        /// <summary>
        /// CAN ANYTHING SEE THIS CREATURE. Measured 2026-08-24 in a live mission: spawning a
        /// bodypart-free actor threw before it finished entering play -
        ///   TacticalFactionVision.CheckVisibleLineBetweenActors:770-771
        ///     list = (from aimPoint in tacticalActor.GetAimPoints() select aimPoint.position).ToList();
        ///   TacticalFactionVision.OnActorEnteredPlay -&gt; OnActorMoved -&gt; ReUpdateVisibilityTowardsActorImpl
        /// - an unguarded <c>.position</c> on every element. <c>TacticalActor.GetAimPoints:1480-1490</c>
        /// yields <c>slot.GetAimPoint()</c> for each health slot with an attached addon, and
        /// <c>TacticalItem.GetAimPoint:644-651</c> returns the <c>_aimPoint</c> that
        /// <c>SetupAimPoint</c> fills - from <c>OnVisualsChanged</c>, which returns at :749 when the
        /// addon has no VisualRoot. A geometry-free bodypart therefore yields a NULL into that list,
        /// and the NRE takes the whole enter-play with it: no creature, no mission.
        ///
        /// Two-line fix, in the engine's own idiom: drop the nulls, and if nothing survives fall back
        /// to the single aim point - which <see cref="AfterAimPoint"/> has already guaranteed is not
        /// null. An actor whose aim points are all real is passed through untouched.
        /// </summary>
        private static void AfterAimPoints(TacticalActor __instance, ref IEnumerable<Transform> __result)
        {
            try
            {
                Transform[] real = (__result ?? Enumerable.Empty<Transform>()).Where(t => t != null).ToArray();
                if (real.Length > 0) { __result = real; return; }
                Transform one = __instance.GetAimPoint();
                __result = one == null ? new Transform[0] : new[] { one };
            }
            catch (Exception) { __result = new Transform[0]; }
        }

        private static void AfterRagdollMode(AddonsManager __instance)
        {
            try
            {
                Transform rig = __instance.RigRoot;
                if (rig == null) return;
                Transform hit = Find(rig, HitName);
                if (hit == null) return;
                CollidersRagdollActivationMode m = __instance.CollidersRagdollMode;
                // The same two-line rule Addon.cs:1502-1504 applies, minus the Ragdolls layer we do
                // not author: a Characters collider is live exactly while something is targeting.
                bool on = m == CollidersRagdollActivationMode.Targeting ||
                          m == CollidersRagdollActivationMode.Unmanaged;
                foreach (Collider c in hit.GetComponents<Collider>()) c.enabled = on;
            }
            catch (Exception) { }
        }

        // ------------------------------------------------------------------ the mechanism

        /// <summary>
        /// Give <paramref name="actor"/> the two colliders the game reads, IF it has none.
        ///
        /// The "if it has none" is the whole safety argument: every shipped unit carries authored
        /// colliders on its bodypart addons, so this predicate is false for all of them and the
        /// engine cannot touch a vanilla actor by accident. No registration, no opt-in list, no way
        /// for a content mod to forget to call something.
        /// </summary>
        internal static bool Fit(TacticalActorBase actor)
        {
            AddonsManager mgr = actor.AddonsManager;
            Transform rig = mgr == null ? null : mgr.RigRoot;
            if (rig == null) return false;
            if (Find(rig, HitName) != null) return false;                       // already fitted
            if (actor.GetComponentsInChildren<Collider>(true)
                     .Any(c => c.gameObject.layer == UnityLayers.Characters.Index)) return false;   // vanilla

            Renderer[] rends = rig.GetComponentsInChildren<Renderer>(true)
                                  .Where(r => r.enabled && r.gameObject.activeInHierarchy).ToArray();
            CreatureManifest o = ManifestFor(rends);
            if (o.Off)
            {
                Say("ct_creature VOID '" + actor.name + "' opted out of collider synthesis " +
                    "(ppcontent.json \"creature\": { \"colliders\": \"off\" }) - it will not be " +
                    "hoverable or shootable unless its own prefab carries authored colliders");
                return false;
            }
            if (rends.Length == 0)
            {
                Say("ct_creature FAIL '" + actor.name + "' has no enabled Renderer under its rig '" +
                    rig.name + "' - there is nothing on screen to size a hit shape to, and a guessed " +
                    "one would be a hitbox around empty air. REFUSED, nothing was added.");
                return false;
            }

            string how;
            Bounds world = Measure(rends, out how);
            float span = Mathf.Max(world.size.x, Mathf.Max(world.size.y, world.size.z));
            float thin = Mathf.Min(world.size.x, Mathf.Min(world.size.y, world.size.z));
            if (span < MinSpan || span > MaxSpan || thin < 1e-4f)
            {
                Say("ct_creature FAIL '" + actor.name + "' measures " + world.size.ToString("F3") +
                    " world units " + how + " (a tile is 1.0), outside the " + MinSpan + ".." + MaxSpan +
                    " a creature can plausibly be. That is almost always the RIG SCALE: a .glb whose " +
                    "exporter wrote a scale node above the skin imports ~100x, and the same wrong " +
                    "number corrupts the collider, the aim point and the root motion together. Fix " +
                    "ppcontent.json \"scale\" / the mod's rig scale. REFUSED, nothing was added.");
                return false;
            }

            // LOCAL to the rig, never world. The rig root is re-oriented, re-scaled and re-seated by
            // the content mod on a seam whose order against ours is not defined; a child expressed in
            // the rig's own space moves WITH the mesh whatever happens to that transform afterwards.
            Bounds local = ToLocal(rig, world);
            GameObject hit = Shape(rig, HitName, UnityLayers.Characters.Index, local, o);
            GameObject pick = Shape(rig, PickName, UnityLayers.CameraCollider.Index, local, o);
            // The manifest's aim bone becomes a marker with the conventional name, so the GetAimPoint
            // postfix stays a two-line lookup instead of carrying the manifest around with it.
            Transform aimBone = Find(rig, o.Aim);
            if (aimBone != null && Find(rig, AimName) == null)
                new GameObject(AimName).transform.SetParent(aimBone, false);

            // FALSIFY THE THING WE JUST BUILT. A local-space conversion that silently produced a box
            // somewhere else is exactly the failure that looks like "the creature is unhittable" for
            // a second session, so the fitted shape is measured back in WORLD space against the
            // renderer it was sized from.
            Bounds got = hit.GetComponent<Collider>().bounds;
            if (!got.Intersects(world))
            {
                UnityEngine.Object.Destroy(hit);
                UnityEngine.Object.Destroy(pick);
                Say("ct_creature FAIL '" + actor.name + "' the fitted hit shape " + got.center.ToString("F2") +
                    " " + got.size.ToString("F2") + " does not overlap the rendered model " +
                    world.center.ToString("F2") + " " + world.size.ToString("F2") + " - shots would " +
                    "pass through the creature and hit a box beside it. REFUSED, both were removed.");
                return false;
            }

            // The mouse cache was taken at OnActorInitialized:114, long before this, and would hold an
            // empty array forever. Clearing the field makes the SelectionColliders getter re-cache
            // (TacticalActorViewBase.cs:98-108) - the game's own lazy path, not a second one.
            try { cameraCache?.SetValue(actor.TacticalActorViewBase, null); } catch (Exception) { }
            // ...and put the hit collider into whatever targeting mode the actor is already in.
            AfterRagdollMode(mgr);

            Say("ct_creature PASS '" + actor.name + "' fitted " + how + ", spanning " +
                world.size.ToString("F3") + ": " + hit.GetComponent<Collider>().GetType().Name +
                " on layer Characters(" + UnityLayers.Characters.Index + ") at " + got.center.ToString("F2") +
                " size " + got.size.ToString("F2") + ", a twin on CameraCollider(" +
                UnityLayers.CameraCollider.Index + ") for hover/click, " + Describe(o) + "; aim point -> '" +
                (Find(rig, AimName) != null ? AimName : HitName) + "'");
            ReportEvents(actor);
            ReportHealth(actor);
            return true;
        }

        /// <summary>
        /// One shape, one layer. A BoxCollider over the whole model by default - it is what the
        /// renderer actually measures, so it cannot be wrong about the creature it belongs to - or
        /// per-bone capsules when the manifest names bones, which is what a modder who wants a shot
        /// to distinguish a leg from a body asks for.
        /// </summary>
        private static GameObject Shape(Transform rig, string name, int layer, Bounds local, CreatureManifest o)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(rig, false);
            go.layer = layer;
            Transform[] bones = o.HitBones.Select(b => Find(rig, b)).Where(t => t != null).ToArray();
            if (bones.Length == 0)
            {
                BoxCollider box = go.AddComponent<BoxCollider>();
                box.center = local.center;
                box.size = local.size;
                return go;
            }
            // Capsules ON the named bones, so they follow the animation instead of a static box.
            // Radius is the manifest's, or a quarter of the model's thinnest axis - small enough that
            // eight legs do not merge into one blob, big enough to be hit.
            float r = o.HitRadius > 0f ? o.HitRadius
                    : Mathf.Max(0.02f, Mathf.Min(local.size.x, Mathf.Min(local.size.y, local.size.z)) * 0.25f);
            foreach (Transform b in bones)
            {
                GameObject c = new GameObject(name);
                c.transform.SetParent(b, false);
                c.layer = layer;
                SphereCollider s = c.AddComponent<SphereCollider>();
                s.radius = r;
            }
            // The whole-model box stays as well: a bone list covers the limbs it names and nothing
            // else, and a shot at the gap between two of them must still hit the creature.
            BoxCollider hull = go.AddComponent<BoxCollider>();
            hull.center = local.center;
            hull.size = local.size;
            return go;
        }

        /// <summary>
        /// HOW BIG IS THIS CREATURE, REALLY - and NOT by asking the renderer.
        ///
        /// <c>SkinnedMeshRenderer.bounds</c> is the mesh's SERIALIZED AABB pushed through the
        /// transform; it is not measured from the geometry at runtime. A mesh that the tool baked
        /// itself carries whatever AABB the bake wrote, and for the spider that is
        /// 3368 x 1106 x 2990 world units on a creature that is one tile across - measured in a live
        /// mission, 2026-08-24. Sizing a hitbox off that number puts a 3-kilometre box around the map.
        ///
        /// THE BONES ARE THE ANIMAL. They are placed by the very animation the player watches, they
        /// carry no serialized field that can be stale, and their extent IS where the creature is. So
        /// the skeleton is the measure, padded outwards because skin hangs off the outside of a bone -
        /// a tenth of the longest axis, which is the same order as a limb's thickness.
        ///
        /// The renderer AABB stays the fallback for an unskinned model, where there is no skeleton and
        /// the serialized bounds are all there is.
        /// ponytail: one pad constant, not a per-bone radius solve. A hitbox is allowed to be a little
        /// generous; it is not allowed to be a kilometre.
        /// </summary>
        private static Bounds Measure(Renderer[] rends, out string how)
        {
            List<Vector3> bones = new List<Vector3>();
            foreach (Renderer r in rends)
            {
                SkinnedMeshRenderer s = r as SkinnedMeshRenderer;
                if (s == null) continue;
                foreach (Transform b in s.bones) if (b != null) bones.Add(b.position);
            }
            if (bones.Count >= 2)
            {
                Bounds b = new Bounds(bones[0], Vector3.zero);
                for (int i = 1; i < bones.Count; i++) b.Encapsulate(bones[i]);
                float pad = Mathf.Max(0.02f, Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z)) * 0.1f);
                b.Expand(pad * 2f);
                how = "from " + bones.Count + " live bone(s) +" + pad.ToString("F3") + " pad (the " +
                      "renderer's serialized AABB says " + Union(rends).size.ToString("F1") + ")";
                return b;
            }
            how = "from " + rends.Length + " renderer AABB(s) - no skeleton to measure";
            return Union(rends);
        }

        private static Bounds Union(Renderer[] rends)
        {
            Bounds b = rends[0].bounds;
            for (int i = 1; i < rends.Length; i++) b.Encapsulate(rends[i].bounds);
            return b;
        }

        /// <summary>A world AABB expressed in <paramref name="t"/>'s own space, corner by corner, so
        /// a rotated or scaled rig cannot smear the box.</summary>
        private static Bounds ToLocal(Transform t, Bounds w)
        {
            Vector3 e = w.extents, c = w.center;
            Bounds b = new Bounds(t.InverseTransformPoint(c + new Vector3(-e.x, -e.y, -e.z)), Vector3.zero);
            for (int i = 1; i < 8; i++)
                b.Encapsulate(t.InverseTransformPoint(c + new Vector3(
                    (i & 1) == 0 ? -e.x : e.x, (i & 2) == 0 ? -e.y : e.y, (i & 4) == 0 ? -e.z : e.z)));
            return b;
        }

        private static Transform Find(Transform root, string name)
        {
            if (root == null || string.IsNullOrEmpty(name)) return null;
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                if (t.name == name) return t;
            return null;
        }

        /// <summary>
        /// WHICH BLOCKING EVENTS THIS CREATURE'S CLIPS ACTUALLY CARRY.
        ///
        /// Reported, never repaired. The engine knows the NAMES the game waits for - they are hard
        /// facts of the decompile - but not the TIMES, which are the one thing only the animation
        /// itself can say. Stamping a guessed time produces a shot that fires before the leg lands:
        /// a bug that reads as the game's, not the mod's. A named-and-missing event is a ten-second
        /// stall per action, which is at least visible and at least explained here.
        /// </summary>
        private static void ReportEvents(TacticalActorBase actor)
        {
            RuntimeAnimatorController rac = actor.Animator == null ? null : actor.Animator.runtimeAnimatorController;
            if (rac == null) return;
            HashSet<string> have = new HashSet<string>();
            foreach (AnimationClip c in rac.animationClips)
                if (c != null)
                    foreach (AnimationEvent e in c.events)
                        if (!string.IsNullOrEmpty(e.stringParameter)) have.Add(e.stringParameter);
            string[] missing = BlockingEvents.Where(e => !have.Contains(e)).ToArray();
            Say("ct_creature " + (missing.Length == 0 ? "PASS" : "WARN") + " '" + actor.name +
                "' animation events: " + rac.animationClips.Length + " clip(s) in '" + rac.name +
                "' carry [" + string.Join(", ", have.OrderBy(x => x).ToArray()) + "]" +
                (missing.Length == 0 ? " - every event the game blocks on is present"
                 : "; MISSING [" + string.Join(", ", missing) + "] - each costs a 10s stall per action " +
                   "(AnimEventReceiver.cs:100,126). Stamp them onto the clip with AnimationClip.AddEvent" +
                   "(functionName \"OnAnimEvent\", stringParameter the name) at the frame the animation " +
                   "actually connects - the engine will not guess that time for you."));
        }

        /// <summary>
        /// CAN THIS CREATURE BE KILLED, OR WAS IT BORN DEAD.
        ///
        /// A soldier's health is not on its template - it comes from its BODYPARTS.
        /// <c>CharacterStats.InitStats:136-163</c> builds <c>Health.Max</c> out of Toughness and
        /// Endurance, and both are summed from the aspects on the bodypart items. A creature with an
        /// empty <c>Data.BodypartItems</c> therefore enters play at <c>0/0</c>, which is
        /// <c>IsDead</c> by definition (TacticalActorBase.cs:118) - it dies the instant it appears,
        /// and every "the creature is not killable" symptom downstream is that.
        ///
        /// REPORTED, NOT REPAIRED, and deliberately so. How tough a creature is IS the creature; an
        /// engine that quietly handed out 20 HP would make every modder's monster secretly identical
        /// and hide the one line that fixes it. <c>TacCharacterData.Strength</c> reaches the same stat
        /// with no geometry, exactly as <c>Speed</c> does (TacCharacterData.cs:126-127 ->
        /// TacticalActor.cs:549 SetBaseCharacterStatsBaseValues).
        /// </summary>
        private static void ReportHealth(TacticalActorBase actor)
        {
            float max = actor.Health.Max;
            Say("ct_creature " + (max > 0f ? "PASS" : "FAIL") + " '" + actor.name + "' Health " +
                ((float)actor.Health).ToString("F1") + "/" + max.ToString("F1") +
                (max > 0f ? " - damage has somewhere to land" :
                 " <- BORN DEAD. Health.Max is built from bodypart aspects (CharacterStats.InitStats:" +
                 "136-163) and this template has no bodypart items, so IsDead is true from the first " +
                 "frame (TacticalActorBase.cs:118) and nothing can ever kill it because it already is. " +
                 "Set Data.Strength on the character template - it reaches the same stat with no " +
                 "geometry, the way Data.Speed does. The engine will not invent a number: how tough a " +
                 "creature is IS the creature."));
        }

        // ------------------------------------------------------------------ the manifest

        /// <summary>
        /// WHOSE MANIFEST APPLIES TO THE MODEL ON SCREEN.
        ///
        /// THE JOIN IS THE MODEL, not a new id to keep in step. A baked mesh is named
        /// <c>&lt;file stem&gt;_mesh</c>, so the live renderer says which .glb it came from, and the
        /// project that owns <c>Content\Models\&lt;stem&gt;.glb</c> is the one whose ppcontent.json
        /// applies. Nothing has to be registered and nothing can drift.
        ///
        /// The block itself - <c>colliders</c>, <c>hitBones</c>, <c>hitRadius</c>, <c>aim</c> and every
        /// other key - is defined and parsed ONCE, in <see cref="CreatureManifest"/>, which is the same
        /// object <see cref="CreatureBuild"/> reads and the bake writes the discovered clips into. One
        /// schema, one reader, one file the author edits.
        /// </summary>
        private static CreatureManifest ManifestFor(Renderer[] rends)
        {
            try
            {
                string stem = Stem(rends);
                if (stem == null) return CreatureManifest.None;
                string dir = Path.GetDirectoryName(ContentToolMain.ModDir);
                if (dir == null || !Directory.Exists(dir)) return CreatureManifest.None;
                foreach (string mod in Directory.GetDirectories(dir))
                {
                    if (!File.Exists(Path.Combine(mod, Project.ContentMods.Manifest))) continue;
                    if (!File.Exists(Path.Combine(Path.Combine(Path.Combine(mod, "Content"), "Models"),
                                                  stem + ".glb"))) continue;
                    return CreatureManifest.Load(mod);
                }
            }
            catch (Exception) { }
            return CreatureManifest.None;
        }

        /// <summary>"spider_mesh" -> "spider". The bake's own naming, so this is a rule and not a
        /// guess; a renderer whose mesh is not ours simply yields no manifest and the defaults.</summary>
        private static string Stem(Renderer[] rends)
        {
            foreach (Renderer r in rends)
            {
                SkinnedMeshRenderer skin = r as SkinnedMeshRenderer;
                MeshFilter filter = skin != null ? null : r.GetComponent<MeshFilter>();
                Mesh m = skin != null ? skin.sharedMesh : (filter == null ? null : filter.sharedMesh);
                string n = m == null ? null : m.name;
                if (string.IsNullOrEmpty(n)) continue;
                int u = n.LastIndexOf('_');
                return u > 0 ? n.Substring(0, u) : n;
            }
            return null;
        }

        private static string Describe(CreatureManifest o)
        {
            return o.HitBones.Length == 0 && o.HitRadius <= 0f && o.Aim.Length == 0
                ? "no manifest override (measured defaults)"
                : "manifest: bones[" + string.Join(",", o.HitBones) + "] radius=" + o.HitRadius +
                  " aim='" + o.Aim + "'";
        }
    }
}
