using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Base.AI.Defs;
using Base.Core;
using Base.Defs;
using Base.Entities;
using Base.Serialization;
using Base.Utils;
using PhoenixPoint.Common.Entities;
using PhoenixPoint.Common.Entities.Addons;
using PhoenixPoint.Common.Entities.Characters;
using PhoenixPoint.Common.Game;
using PhoenixPoint.Common.Levels.ActorDeployment;
using PhoenixPoint.Common.Utils;
using PhoenixPoint.Tactical;
using PhoenixPoint.Tactical.AI;
using PhoenixPoint.Tactical.AI.Actions;
using PhoenixPoint.Tactical.Entities;
using PhoenixPoint.Tactical.Entities.Animations;
using PhoenixPoint.Tactical.Levels.PathProcessors;
using PhoenixPoint.Tactical.Entities.Abilities;
using PhoenixPoint.Tactical.Entities.ActorsInstance;
using PhoenixPoint.Tactical.Entities.Weapons;
using PhoenixPoint.Tactical.Levels;
using UnityEngine;

namespace Morgott.ContentTool.Tactical
{
    /// <summary>
    /// Gate C1 - IS A CUSTOM-MODEL CREATURE ACTUALLY HITTABLE, TARGETABLE AND KILLABLE.
    ///
    /// Everything <see cref="CreatureFit"/> does is invisible from a def dump: colliders, layers,
    /// damage routing and death are runtime facts of a live actor standing on a live tile. So this
    /// gate takes the shortest honest route to one - <c>ct_mission</c>'s own recipe: load a save that
    /// POSITIVELY declares itself tactical, then spawn the creature into it with the engine's own
    /// spawner and measure the real thing.
    ///
    /// The spawn is not an invention either. It is exactly what the shipped
    /// <c>SpawnActorAbility.SpawnActorCrt:130-131</c> does - generate the instance component set and
    /// the instance data off the character def, then <c>ActorSpawner.SpawnActor&lt;TacticalActor&gt;</c> -
    /// minus the ability wrapper. Whatever the game would spawn, this spawns.
    ///
    /// The SUBJECT is chosen by the property that defines the problem, never by name: a
    /// TacCharacterDef with a rig and ZERO bodypart items is precisely a creature assembled out of a
    /// model file, which is the case that has no authored colliders. If a content mod is installed
    /// its creature is found; if none is, the gate says so instead of passing on nothing.
    /// </summary>
    internal static class CreatureGate
    {
        private const float LoadBudgetSeconds = 420f;

        internal static string Run(string[] args)
        {
            string cmd = args != null && args.Length > 0 ? args[0].ToLowerInvariant() : "list";
            switch (cmd)
            {
                case "list": return List();
                case "gate":
                    if (args.Length < 2)
                        return "usage: ct_creature gate <tactical savename> [template name fragment]   " +
                               "(ct_mission list prints the save names, ct_creature list the templates)";
                    // The template fragment is the LAST token when there is one, because a save name may
                    // contain spaces and a def name may not.
                    string who = args.Length > 2 ? args[args.Length - 1] : null;
                    string save = string.Join(" ", args.Skip(1).Take(args.Length - (who == null ? 1 : 2)).ToArray());
                    GameObject go = new GameObject("ct_creature");
                    UnityEngine.Object.DontDestroyOnLoad(go);
                    go.AddComponent<Runner>().Begin(save, who);
                    return "C1 armed on save '" + save + "' for template '" + (who ?? "(first candidate)") +
                           "' - the arms print from the runner once the mission is live";
                default: return "usage: ct_creature [list | gate <tactical savename>]";
            }
        }

        /// <summary>
        /// Every character template that would arrive with no colliders. Answers "is there anything
        /// for the gate to measure" without loading anything.
        /// </summary>
        internal static string List()
        {
            TacCharacterDef[] all = Candidates();
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("ct_creature: " + all.Length + " character template(s) carry a rig and NO " +
                          "bodypart GEOMETRY - the shape that has no authored collider and needs fitting");
            foreach (TacCharacterDef d in all)
            {
                AddonsManagerDef m = d.GetAddonsMangerDef();
                sb.AppendLine("  '" + d.name + "' rig='" + (m == null || m.Rig == null ? "(none)" : m.Rig.name) +
                              "' equipment=" + (d.Data.EquipmentItems == null ? 0 : d.Data.EquipmentItems.Length));
            }
            return sb.ToString().TrimEnd();
        }

        /// <summary>
        /// The subject is chosen by the property that DEFINES the problem: no bodypart carries any
        /// geometry, so nothing authored a collider onto this actor.
        ///
        /// Not "zero bodypart items", which is what this used to say. A creature assembled out of a
        /// model file still needs bodyparts for the things bodyparts ARE - a melee weapon and its bash
        /// ability - and those are added with <c>SkinData</c> nulled precisely so no donor limb comes
        /// with them (Addon.AttachVisuals:1024-1032 returns before it instantiates). Counting items
        /// rather than geometry made the gate stop seeing a creature the moment it grew a weapon.
        ///
        /// ...and geometry that is OURS is not the donor's. A ranged creature carries a
        /// <see cref="CreatureRanged.ShootPointSkinDataDef"/> muzzle, which exists only because
        /// TacticalPerception.GetShotOrigin:503+ returns Vector3NaN unless the weapon's addon is
        /// VISIBLE (Addon.cs:195 - an active GameObject, no renderer needed). Counting that as donor
        /// geometry made the gate stop seeing the creature the moment it grew a SECOND weapon, which
        /// is the same mistake one rung up. The predicate is the same one the donor-free audit uses.
        /// </summary>
        private static TacCharacterDef[] Candidates()
        {
            DefRepository repo = GameUtl.GameComponent<DefRepository>();
            return repo.GetAllDefs<TacCharacterDef>()
                .Where(d => d.Data != null && d.Data.ComponentSetTemplate != null &&
                            (d.Data.BodypartItems == null ||
                             d.Data.BodypartItems.All(i => i == null || i.SkinData == null ||
                                 i.SkinData is CreatureRanged.ShootPointSkinDataDef)) &&
                            d.GetAddonsMangerDef() != null && d.GetAddonsMangerDef().Rig != null)
                .OrderBy(d => d.name).ToArray();
        }

        /// <summary>A clip's name, or "(null)" - the one thing that matters when a navigation slot is
        /// empty and the path builder has nothing to play.</summary>
        private static string Clip(AnimationClip c) => c == null ? "(null)" : c.name;

        private sealed class Runner : MonoBehaviour
        {
            private string saveName;
            private string wanted;
            private string refusal;
            /// <summary>Data.Strength as the TEMPLATE ships it, captured before the gate's clone
            /// forces a living subject - so C1-hp reports the content's own value, not the gate's.</summary>
            private static int Shipped;

            internal void Begin(string name, string template)
            {
                saveName = name;
                wanted = template;
                Dev.AsyncGate.Pending++;
                StartCoroutine(Gate());
            }

            private IEnumerator Gate()
            {
                StringBuilder log = new StringBuilder();
                int fail = 0;
                bool measured = false;
                TacticalPerceptionBase targetable = null;
                try
                {
                    GameUtl.GameComponent<TimeSource>().Timing.Start(Load(saveName));

                    float start = Time.realtimeSinceStartup;
                    TacticalLevelController tac = null;
                    while (Time.realtimeSinceStartup - start < LoadBudgetSeconds)
                    {
                        if (refusal != null) break;
                        tac = Current();
                        if (tac != null && tac.TacMission != null && Anyone(tac) != null) break;
                        yield return new WaitForSeconds(1f);
                    }
                    if (refusal != null) { log.AppendLine("C1 VOID " + refusal); yield break; }
                    if (tac == null || tac.TacMission == null || Anyone(tac) == null)
                    {
                        log.AppendLine("C1 VOID no tactical mission became live within " + LoadBudgetSeconds +
                                       "s of loading '" + saveName + "' - nothing was measured");
                        yield break;
                    }
                    yield return new WaitForSeconds(5f);

                    TacCharacterDef[] cands = Candidates();
                    TacCharacterDef def = wanted == null ? cands.FirstOrDefault()
                        : cands.FirstOrDefault(d => d.name.IndexOf(wanted, StringComparison.OrdinalIgnoreCase) >= 0);
                    if (def == null)
                    {
                        log.AppendLine("C1 VOID no installed character template carries a rig with zero " +
                                       "bodypart items" + (wanted == null ? "" : " and a name containing '" +
                                       wanted + "' (" + cands.Length + " candidate(s) were offered)") +
                                       " - there is no custom-model creature to measure. Enable a content " +
                                       "mod that adds one (the CustomCreature demo) and re-run.");
                        yield break;
                    }

                    TacticalActor host = Anyone(tac);
                    // AN ENEMY TO STAND NEXT TO, when the mission has one. A bash offers no targets
                    // at all against a friend - GetTargets returned 0 and the arm could not even
                    // start - so the creature is spawned beside a HOSTILE actor and stays in the
                    // host's own faction. Without one the gate still measures everything else and the
                    // attack arm says why.
                    TacticalActor foe = Hostile(tac, host);
                    TacticalActor actor = null;
                    string threw = null;
                    try { actor = Spawn(def, host, foe ?? host); }
                    catch (Exception ex) { threw = ex.ToString(); }
                    if (actor == null)
                    {
                        log.AppendLine("C1 VOID '" + def.name + "' could not be spawned beside '" + host.name +
                                       "': " + (threw ?? "SpawnActor returned null"));
                        yield break;
                    }
                    // One frame for FinalizeEnterPlay's postfix and the view to settle.
                    yield return null;
                    measured = true;

                    // Characters colliders are live only while something is TARGETING - the game's own
                    // rule (Addon.RefreshCollidersRagdoll:1502-1504), which CreatureFit mirrors. The
                    // round trip forces a real transition even if the manager already believed it was
                    // in that mode, so the arms below measure a collider that is switched ON; a
                    // disabled collider reports zero bounds and no raycast can find it.
                    // Not SetRagdollMode directly: TacticalPerceptionBase.RefreshCollidersMode:90-107
                    // puts the manager straight back to InactiveAll whenever ForceTargetable.RefCount
                    // is 0, so a bare mode write is undone within the frame - measured. RequestForce-
                    // Targetable:66-79 is the game's own "something is aiming at this actor" and holds
                    // a refcount, which is exactly the state a shot is resolved in.
                    targetable = actor.TacticalPerceptionBase;
                    targetable.RequestForceTargetable();
                    yield return new WaitForFixedUpdate();   // physics has to see the enabled collider

                    // ARM 1 - the two colliders exist, on the two layers the game reads, and they are
                    // where the model is. This is what "unhittable" was.
                    Collider[] chars = OnLayer(actor, UnityLayers.Characters.Index);
                    Collider[] cams = OnLayer(actor, UnityLayers.CameraCollider.Index);
                    Bounds model = ModelBounds(actor);
                    fail += Check(log, "C1-collider", chars.Length > 0 && cams.Length > 0,
                        chars.Length + " on Characters(" + UnityLayers.Characters.Index + ") " +
                        (chars.Length == 0 ? "" : chars[0].bounds.center.ToString("F2") + " size " +
                         chars[0].bounds.size.ToString("F2")) + ", " + cams.Length + " on CameraCollider(" +
                        UnityLayers.CameraCollider.Index + "); model renders " + model.center.ToString("F2") +
                        " size " + model.size.ToString("F2"));

                    // ARM 1b - A REAL SHOT. Everything above is a count and a number; this is the
                    // question itself. A raycast on the Characters layer alone, aimed at the creature
                    // from a metre away, must come back holding one of ITS colliders - that cast is
                    // what the fire path does, and until it hits, "hittable" is an assertion.
                    Vector3 aimAt = chars.Length == 0 ? actor.Pos : chars[0].bounds.center;
                    Vector3 from = aimAt + new Vector3(1.5f, 0.4f, 0f);
                    RaycastHit rh;
                    bool shot = Physics.Raycast(from, (aimAt - from).normalized, out rh, 5f,
                                                1 << UnityLayers.Characters.Index);
                    fail += Check(log, "C1-shot", shot && rh.collider != null &&
                        ReferenceEquals(TacUtil.GetActorFromTransform<TacticalActorBase>(rh.collider.transform), actor),
                        "Physics.Raycast on layer Characters from " + from.ToString("F2") + " -> " +
                        (!shot ? "NOTHING - a bullet passes straight through the creature"
                         : "'" + rh.collider.name + "' of '" +
                           (TacUtil.GetActorFromTransform<TacticalActorBase>(rh.collider.transform) == null ? "(no actor)"
                            : TacUtil.GetActorFromTransform<TacticalActorBase>(rh.collider.transform).name) +
                           "' at " + rh.point.ToString("F2")));

                    // ARM 2 - the mouse's OWN cache, not our count of colliders. SelectionColliders is
                    // the array TacticalView.SelectAtCursor:701 picks through; it is taken once at
                    // OnActorInitialized:114 and would be empty for ever without the fit's reset.
                    Collider[] sel = actor.TacticalActorViewBase.SelectionColliders;
                    fail += Check(log, "C1-hover", sel != null && sel.Length > 0,
                        "TacticalActorViewBase.SelectionColliders = " + (sel == null ? "null" : sel.Length.ToString()) +
                        " - this is the array the cursor picks through");

                    // ARM 3 - a shot that lands on that collider resolves to THIS actor. The exact call
                    // the fire path makes (TacUtil.cs:140-143), on the exact object it would hit.
                    IDamageReceiver viaCollider = chars.Length == 0 ? null
                        : TacUtil.GetDamageReceiverFromTransform(chars[0].transform);
                    IDamageReceiver forHit = actor.GetDamageReceiverForHit(actor.Pos + Vector3.up, Vector3.down);
                    fail += Check(log, "C1-receiver",
                        viaCollider != null && ReferenceEquals(viaCollider.GetActor(), actor) && forHit != null,
                        "GetComponentInParent<IDamageReceiver> on the hit collider -> " +
                        (viaCollider == null ? "null" : viaCollider.GetType().Name + " of '" +
                         (viaCollider.GetActor() == null ? "(no actor)" : viaCollider.GetActor().name) + "'") +
                        "; GetDamageReceiverForHit -> " + (forHit == null ? "null" : forHit.GetType().Name));

                    // ARM 4 - the aim point. Null here is an NRE per tile step inside
                    // TacticalZoneObjective.OnActorMovedInANewTile, which kills the mover's coroutine.
                    Transform aim = actor.GetAimPoint();
                    fail += Check(log, "C1-aim", aim != null && model.SqrDistance(aim.position) < 4f,
                        "GetAimPoint -> " + (aim == null ? "NULL - every move would throw" :
                        "'" + aim.name + "' at " + aim.position.ToString("F2") + ", " +
                        Mathf.Sqrt(model.SqrDistance(aim.position)).ToString("F2") + " from the rendered model"));

                    // ARM 5 - the PLURAL aim points, which is what VISION walks. This is the arm the
                    // first live run bought: TacticalFactionVision.CheckVisibleLineBetweenActors:770-771
                    // does `.Select(p => p.position)` over this list with no null check, from inside
                    // OnActorEnteredPlay, and one null in it threw the whole spawn away. An EMPTY list
                    // is just as fatal in a quieter way - nothing can ever see the creature, so nothing
                    // can target it.
                    Transform[] pts = actor.GetAimPoints().ToArray();
                    fail += Check(log, "C1-vision", pts.Length > 0 && pts.All(p => p != null),
                        "GetAimPoints -> " + pts.Length + " point(s), " + pts.Count(p => p == null) +
                        " null; vision walks these and dereferences every one");

                    // ARM 6 - hit zones. Reported, not required: with no bodyparts the actor is its OWN
                    // receiver (TacticalActorBase.cs:779-782), so damage lands and death happens - what
                    // is missing is per-limb health, which is a content decision, not an engine one.
                    CharacterBodyState body = actor.GetComponent<CharacterBodyState>();
                    int slots = body == null ? -1 : body.GetHealthSlots().Count();
                    log.AppendLine("C1-hitzones " + (slots > 0 ? "PASS" : "WARN") + " " +
                        (slots < 0 ? "no CharacterBodyState on the actor"
                         : slots + " health slot(s) (CharacterBodyState._healthSlots)") +
                        (slots > 0 ? "" : " - no bodypart items, so there are no per-limb hit zones and " +
                         "no aspect stats; the actor answers as ONE receiver and is still hit and killed. " +
                         "Add a bodypart ItemDef with SkinData null to the template for zones."));

                    // ARM 6 - damage actually reduces health. Health.Subtract is the very call
                    // ApplyDamageInternal:874 makes; going through a DamageResult would only add a
                    // weapon's arithmetic between us and the thing being proven.
                    // The TEMPLATE's own health first, reported separately from the mechanism: a
                    // bodypart-free template enters play at 0/0 and is IsDead from its first frame
                    // (CharacterStats.InitStats:136-163, TacticalActorBase.cs:118). That is a CONTENT
                    // defect - Data.Strength fixes it - and it must not be confused with "damage does
                    // not route", which is what the next two arms measure.
                    // Deliberately NOT counted as an engine arm: this is a defect in the CONTENT mod's
                    // template, and the engine's own ct_creature line already refuses it out loud. An
                    // engine gate must not go red because a modder forgot a number.
                    log.AppendLine("C1-hp " + (Shipped > 0 ? "PASS" : "CONTENT-DEFECT") + " the template ships " +
                        "Data.Strength=" + Shipped + ", spawned Health.Max=" + ((float)actor.Health.Max).ToString("F1") +
                        (Shipped > 0 ? "" : " - the template is BORN DEAD and every arm above would have " +
                        "measured a corpse; the gate forced Strength=20 on its own clone so the fit could " +
                        "be measured at all. Set Data.Strength on the character template - the engine " +
                        "deliberately does not invent a toughness."));

                    // ARM 7 - CAN IT ATTACK, and does the AI's OWN question resolve.
                    // AIActionMoveAndAttack.GetAttackAbility:73-86 is three lines and asks the weapon
                    // two things - is its payload DamageDeliveryType.Melee, and does its WeaponDef
                    // carry a BashAbilityDef - then takes the actor's BashAbility whose BashAbilityDef
                    // is that one and whose Source is that weapon. The same predicate is asked here
                    // from the ability's end, so a PASS is literally "stock AI would pick this".
                    BashAbility bash = actor.GetAbilities<BashAbility>().FirstOrDefault(b =>
                    {
                        Weapon w = b.Source as Weapon;
                        return w != null && w.WeaponDef != null &&
                               w.GetDamagePayload().DamageDeliveryType == DamageDeliveryType.Melee &&
                               w.WeaponDef.Abilities.OfType<BashAbilityDef>().FirstOrDefault() == b.BashAbilityDef;
                    });
                    // The three places a bodypart weapon has to survive to become an ability, named in
                    // the line itself so a FAIL says WHICH one dropped it: the addon tree it is
                    // attached into (CharacterBodyState.SetupBodyParts:89-97 BulkAttachAddons), the
                    // equipment component that adopts body equipment (EquipmentComponent
                    // .OnActorEnteredPlay:40-50, gated on EquipmentComponentDef.ObtainFromBody), and
                    // the ability list an active equipment grants (Equipment.SetActive:181-190).
                    string[] rootAddons = actor.AddonsManager == null || actor.AddonsManager.RootAddon == null
                        ? new string[0]
                        : actor.AddonsManager.RootAddon.Select(a => a.GetType().Name + "<" +
                              (a.AddonDef == null ? "?" : a.AddonDef.name) + ">").ToArray();
                    fail += Check(log, "C1-melee", bash != null,
                        "AIActionMoveAndAttack.GetAttackAbility:73-86 resolves to " +
                        (bash == null ? "NOTHING - the AI can never attack with this creature"
                         : "'" + bash.BashAbilityDef.name + "' on '" + ((Weapon)bash.Source).WeaponDef.name +
                           "', enabled=" + bash.IsEnabled(
                               IgnoredAbilityDisabledStatesFilter.IgnoreNoValidTargetsAndEquipmentNotSelected)) +
                        "; ObtainFromBody=" + actor.Equipments.EquipmentComponentDef.ObtainFromBody +
                        ", Equipments=[" + string.Join(", ", actor.Equipments.Equipments
                            .Select(e => e.EquipmentDef.name).ToArray()) + "], RootAddon=[" +
                        string.Join(", ", rootAddons) + "]");

                    // ARM 8 - IT ACTUALLY HITS SOMETHING, and does not stall doing it.
                    // The creature was spawned at the host's own tile, so the host is inside melee
                    // range by construction and no pathing is involved - what is being measured is the
                    // ATTACK, not the walk. Damage lands the game's own way: BashAbility.OnExecute ->
                    // ApplyPayloadEffects:553 -> DamagePayload.AccumulateDamage:576 ->
                    // DamageReceiverImplementation.ApplyDamage:108.
                    // The CLOCK is the second half of the arm. Every wait inside the bash is a named
                    // animation event with a 10s timeout (AnimEventReceiver.cs:100,126) - ActionDo,
                    // then ShootShot, then ActionEnd - so a clip missing them costs thirty seconds and
                    // is the difference between a creature that plays and one that does not.
                    if (bash != null)
                    {
                        // NOBODY IS WATCHING, and an unwatched Animator does not fire animation
                        // events - Unity culls it, and every one of the bash's waits is an event. The
                        // game itself has exactly this problem and exactly this answer one line before
                        // it waits for "Ragdoll" (RagdollDieAbility.cs:92-95
                        // `Animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;`), which is why
                        // death worked unattended while every attack sat there. This is the HARNESS
                        // standing in for a camera that a player would have pointed at the creature.
                        if (actor.Animator != null)
                            actor.Animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                        string blew = null;
                        // THE GAME'S OWN TARGET, not one built by hand. A hand-made
                        // `new TacticalAbilityTarget(actor)` sets Actor/GameObject/ActorGridPosition
                        // and leaves DamageReceiver NULL (TacticalAbilityTarget.cs:64-70), and
                        // BashAbility.GetEffectTarget:524-547 dereferences exactly that field -
                        // measured, the swing played to the end and then threw inside
                        // ApplyPayloadEffects, so no damage landed. GetTargets is the list the UI and
                        // the AI both pick from, and its entries carry the receiver.
                        TacticalAbilityTarget[] offered = bash.GetTargets().ToArray();
                        TacticalAbilityTarget tgt = offered.FirstOrDefault(t => t.Actor == foe)
                                                 ?? offered.FirstOrDefault(t => t.Actor != null);
                        TacticalActorBase victim = tgt == null ? null : tgt.Actor;
                        float hpBefore = victim == null ? 0f : victim.Health;

                        // ARM 8a-i - THE PLAYER CAN ACTUALLY PRESS IT.
                        //
                        // THE SECOND TIME A PLAYER-VISIBLE DEFECT PASSED EVERY ARM. An earlier round saw
                        // this very bash report itself DISABLED, watched it fire anyway when activated
                        // directly, and concluded the flag "measures whether the UI would offer the
                        // ability, not whether it works" - then dropped it. Right about the flag, wrong
                        // about the consequence: for a creature the player DRIVES, the UI offering the
                        // ability IS the feature. The user can spit but the melee button is greyed out
                        // standing next to a Varg, while this gate has been proving the bash by calling
                        // Activate() directly - which is exactly the check the player hits and we skip.
                        //
                        // GetDisabledState is what greys the button (TacticalAbility.cs:367-377:
                        // IsEnabled == GetDisabledState(filter) == NotDisabled, default filter). The
                        // REASON is printed, not just the boolean, because the two branches of
                        // BashAbility.GetDisabledStateInternal:206-246 fail for completely different
                        // causes and only the enum separates them:
                        //   BashingWith.SourceWeapon                -> :237 needs
                        //      weapon.FindTransform(BashPoint), and FindTransform searches the addon's
                        //      OwnedTransforms (Addon.cs:1374). Our melee weapon has SkinData NULLED on
                        //      purpose, so it owns no transforms at all and can never resolve one -
                        //      whereas the SPIT works precisely because CreatureRanged synthesises a
                        //      holder carrying its named muzzle child. That asymmetry inside one actor
                        //      is the whole clue.
                        //   BashingWith.SelectedEquipmentOrBareHands -> :229 needs GetUsableHands() > 0,
                        //      and this creature is a chassis with bodypart items and no hands at all.
                        // Both are plausible offline and the def decides which, so the arm REPORTS the
                        // def rather than assuming - the value is read, never typed.
                        // GIVE IT ITS TURN, THE GAME'S OWN WAY. A spawned actor in a loaded save has
                        // never had a turn start, and that is not a neutral state: RestartAbilities
                        // (TacticalActor.cs:1239-1250) is what a turn start calls, and it does TWO
                        // things - SetAbilityTraits("start") and ActionPoints.SetToMax(). Without it an
                        // ability reports NotEnoughActionPoints, and once AP is poked by hand it
                        // reports RequirementsNotMet instead, because AbilityTraits is still EMPTY and
                        // TacticalAbility.cs:164 asks HasAbilityTraits(TraitsRequired).
                        //
                        // So the hand-poked AP was measuring a half-started turn and inventing a second
                        // defect. One native call replaces it and puts the actor in exactly the state
                        // the player is in when he looks at the button - which is the only state in
                        // which "is it offered" means anything.
                        actor.RestartAbilities();

                        BashAbilityDef bdef = bash.BashAbilityDef;
                        Weapon bashWeapon = bash.Source as Weapon;
                        string point = bdef == null ? "?"
                            : (bdef.BashWith == BashAbilityDef.BashingWith.SourceWeapon
                                   ? bdef.BashPoint : bdef.NoEquipmentBashPoint);
                        AbilityDisabledState state = bash.GetDisabledState();
                        fail += Check(log, "C1-offered", state == AbilityDisabledState.NotDisabled,
                            "'" + bash.BashAbilityDef.name + "' reports " + state +
                            " to the DEFAULT filter - this is the value that greys the button, so " +
                            "anything but NotDisabled means the player cannot press it however well " +
                            "Activate() works. BashWith=" +
                            (bdef == null ? "?" : bdef.BashWith.ToString()) +
                            ", point '" + point + "' -> " +
                            (bashWeapon == null ? "(bash source is not a Weapon)"
                                 : (bashWeapon.FindTransform(point) == null
                                        ? "NOT FOUND on the weapon's OwnedTransforms (Addon.cs:1374)"
                                        : "found")) +
                            ", usableHands=" + actor.GetUsableHands() +
                            ", targets offered=" + offered.Length +
                            // RequirementsNotMet is THREE questions wearing one name
                            // (TacticalAbility.cs:164-176,199-207), and we strip tags on purpose for the
                            // donor-free audit - so which of the three fails decides whether the fix is
                            // a tag we must keep or a requirement we must clone away.
                            ", traits=" + bash.ActorTraitsSatisfied +
                            " actorTags=" + bash.ActorTagsSatisfied +
                            " equipTags=" + bash.EquipmentTagsSatisfied +
                            // ...and WHICH traits, because the fix depends on the strings: a trait our
                            // creature could legitimately carry is a template edit, while one that only
                            // means "is a shipped Swarmer" has to be cloned out of the requirement.
                            " TraitsRequired=[" + (bash.TraitsRequired == null ? "" :
                                string.Join(", ", bash.TraitsRequired.ToArray())) + "]");

                        float t0 = Time.realtimeSinceStartup;
                        if (tgt == null || victim == null)
                            blew = "the ability offers no actor target at all (" + offered.Length +
                                   " from GetTargets) - nothing is inside its reach";
                        else { try { bash.Activate(tgt); } catch (Exception ex) { blew = ex.Message; } }
                        // WHAT IS THE ANIMATOR ACTUALLY PLAYING while the ability waits. A stalled
                        // bash is always one of two things - the Action state never entered, or it
                        // entered playing a clip with no events in it - and only the clip name tells
                        // them apart. Sampled once a second, so the FAIL line diagnoses itself.
                        List<string> seen = new List<string>();
                        float next = 0f;
                        while (blew == null && bash.IsExecuting && Time.realtimeSinceStartup - t0 < 45f)
                        {
                            if (actor.Animator != null && Time.realtimeSinceStartup - t0 >= next)
                            {
                                next += 1f;
                                AnimatorClipInfo[] now = actor.Animator.GetCurrentAnimatorClipInfo(0);
                                string what = now.Length == 0 || now[0].clip == null ? "(none)" : now[0].clip.name;
                                if (seen.Count == 0 || seen[seen.Count - 1] != what) seen.Add(what);
                            }
                            yield return null;
                        }
                        float took = Time.realtimeSinceStartup - t0;
                        float hpAfter = victim == null ? 0f : victim.Health;
                        fail += Check(log, "C1-attack", blew == null && hpAfter < hpBefore && took < 9f,
                            (blew != null ? "THREW " + blew + "; " : "") + "'" + actor.name +
                            "' bashed '" + (victim == null ? "(nothing)" : victim.name) + "' " +
                            (victim == null ? "?" : Vector3.Distance(actor.Pos, victim.Pos).ToString("F2")) +
                            " tile(s) away, chosen from " + offered.Length +
                            " GetTargets offer(s): Health " +
                            hpBefore.ToString("F1") + " -> " +
                            hpAfter.ToString("F1") + " in " + took.ToString("F2") + "s, animator played [" +
                            string.Join(" -> ", seen.ToArray()) + "]" +
                            (hpAfter >= hpBefore ? " <- NO DAMAGE LANDED" : "") +
                            (took >= 9f ? " <- STALLED: each 10s is one animation event the attack " +
                             "clip does not carry (AnimEventReceiver.cs:100,126)" : ""));
                    }

                    // ARM 8b - THE RANGED ATTACK, and that the STOCK AI WOULD PICK IT.
                    //
                    // Three separate things can each leave a creature that carries a spit and never
                    // spits, and only one of them is "the shot does not work", so all three are
                    // asserted apart:
                    //   1. the weapon resolves an ability the way the AI resolves it. Not a guess -
                    //      AIActionMoveAndAttack.GetAttackAbility:73-86 reads `weapon.DefaultShootAbility`
                    //      and only swaps to a BashAbility when the payload is Melee, so this asks the
                    //      SAME question of the SAME field.
                    //   2. the AI's action list actually offers a ranged action. Cloning the donor's
                    //      TacAIActorDef is not enough: Swarmer_AIActionsTemplateDef ships no ranged
                    //      action at all, so without the append the ability exists and is never chosen.
                    //   3. the shot lands on something, at a distance, without stalling.
                    Weapon spitter = actor.Equipments.Equipments.OfType<Weapon>().FirstOrDefault(w =>
                        w.GetDamagePayload().DamageDeliveryType != DamageDeliveryType.Melee);
                    if (spitter != null)
                    {
                        TacticalAbility shoot = spitter.DefaultShootAbility;
                        AIActionsTemplateDef aiTemplate = null;
                        TacAIActorDef aiActor = def.Data.ComponentSetTemplate == null ? null
                            : def.Data.ComponentSetTemplate.GetComponentDef<TacAIActorDef>();
                        if (aiActor != null) aiTemplate = aiActor.AIActionsTemplateDef;
                        BaseDef[] actions = aiTemplate == null || aiTemplate.ActionDefs == null
                            ? new BaseDef[0] : aiTemplate.ActionDefs.Cast<BaseDef>().ToArray();
                        int ranged = actions.Count(a => a is AIActionMoveAndAttackDef);

                        // The actor's OWN accuracy, read off the live stats. A ranged shot rolls
                        // against this and a bash does not, so a creature can pass every other arm and
                        // still miss with every projectile it fires. Reported rather than asserted,
                        // because what counts as accurate is content's business - but a ZERO here
                        // explains a hundred per cent miss rate on its own.
                        float acc = 0f;
                        try { acc = actor.CharacterStats == null ? -1f : actor.CharacterStats.GetAccuracy(); }
                        catch (Exception) { acc = -1f; }

                        fail += Check(log, "C1-spit-ai", shoot != null && ranged >= 2,
                            "actor accuracy " + acc.ToString("F2") + "; the weapon '" +
                            spitter.WeaponDef.name + "' (delivery " +
                            spitter.GetDamagePayload().DamageDeliveryType + ", range " +
                            spitter.GetDamagePayload().Range.ToString("F1") + ") resolves '" +
                            (shoot == null ? "NOTHING" : shoot.AbilityDef.name) +
                            "' the way AIActionMoveAndAttack.GetAttackAbility:73-86 does, and the AI " +
                            "template '" + (aiTemplate == null ? "(none)" : aiTemplate.name) +
                            "' offers " + ranged + " move-and-attack action(s) of " + actions.Length +
                            " [" + string.Join(", ", actions.Select(a => a.name).ToArray()) + "]" +
                            (shoot == null ? " <- no shoot ability, the AI has nothing to fire"
                             : ranged >= 2 ? "" : " <- only one attack action, so the ranged weapon " +
                               "can never be chosen however well it works"));

                        // ...AND IT FIRES, AT RANGE. The target comes from the ability's own GetTargets
                        // for the same reason the bash's does (TacticalAbilityTarget.cs:64-70 leaves
                        // DamageReceiver null on a hand-built one), and the FURTHEST offer is taken so
                        // this measures a shot and not a second melee.
                        if (shoot != null)
                        {
                            if (actor.Animator != null)
                                actor.Animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                            TacticalAbilityTarget[] far = shoot.GetTargets()
                                .Where(t => t.Actor != null && t.Actor != actor)
                                .OrderByDescending(t => Vector3.Distance(actor.Pos, t.Actor.Pos)).ToArray();
                            TacticalAbilityTarget spitTgt = far.FirstOrDefault();
                            TacticalActorBase mark = spitTgt == null ? null : spitTgt.Actor;
                            float hpWas = mark == null ? 0f : mark.Health;
                            float gap = mark == null ? 0f : Vector3.Distance(actor.Pos, mark.Pos);
                            // WHERE THE SHOT STARTS, AND WHAT IS ALREADY TOUCHING IT.
                            //
                            // "Missed" in this game is NOT a roll. TacticalAbilityReport.cs:44-49 plays
                            // SharedSoundEvents.Missed when, after every projectile has landed,
                            // TryFindAbilityReport(targetActor) finds NO damage report for the target -
                            // i.e. the projectile delivered nothing. Accuracy cannot cause it: it is
                            // directional spread (Weapon.cs:291-332, num *= 1/(1+accuracy)), and at
                            // SpreadDegrees 0.8 with accuracy 70 the cone is ~0.011 degrees. That is why
                            // setting accuracy to 70 changed nothing.
                            //
                            // The mechanism that CAN eat it whole: ProjectileLogic.InitialPositionOverlapCheck
                            // (:276-298) sphere-overlaps radius 0.01 AT THE SPAWN POINT before the
                            // projectile travels at all, against Characters|BlockingAll (:240), and any
                            // hit that survives the filter detonates the shot at distance zero (:218,
                            // :158-162). The only self-exclusion is a predicate - BaseMap.IgnoreActors
                            // (:270-288) rejects a hit iff collider.GetComponentInParent<ActorComponent>()
                            // IS the shooter - so anything at the muzzle that is NOT parented under this
                            // actor kills the shot silently.
                            //
                            // So this prints the origin the engine will use and everything already
                            // overlapping it, each labelled with the actor it belongs to. Reporting
                            // only, never a verdict: it exists to make ONE run decisive.
                            try
                            {
                                Vector3 origin = actor.TacticalPerception.GetShotOrigin(spitter, actor.Pos, spitTgt);
                                int mask = (1 << UnityLayers.Characters.Index) | (int)UnityLayers.BlockingAll;
                                Collider[] touching = Physics.OverlapSphere(origin, 0.01f, mask);
                                List<string> who = new List<string>();
                                foreach (Collider col in touching)
                                {
                                    ActorComponent owner = col.GetComponentInParent<ActorComponent>();
                                    who.Add(col.name + "@" + LayerMask.LayerToName(col.gameObject.layer) +
                                            "->" + (owner == null ? "NO ActorComponent (NOT excluded)"
                                                    : owner == actor ? "the shooter (excluded)"
                                                    : owner.name + " (NOT excluded)"));
                                }
                                // THE RULER'S OWN SCALE. GetShotOrigin does not measure the actor - it
                                // poses a CharacterTargetDummy and reads the muzzle off THAT rig. The
                                // dummy is not a TacticalActor, so nothing that orients the live actor
                                // ever touched it, and an unscaled dummy reports every bone offset at
                                // 1/scale too far. Printing both scales side by side is what separates
                                // "the muzzle is on the wrong bone" from "the ruler is the wrong size":
                                // equal scales mean the origin is now honest, and if they differ the
                                // ratio IS the error.
                                CharacterTargetDummy dummy = actor.TacticalPerception.TargetDummy;
                                Transform dummyRig = dummy == null || dummy.AddonsManager == null
                                    ? null : dummy.AddonsManager.RigRoot;
                                Transform liveRig = actor.AddonsManager == null
                                    ? null : actor.AddonsManager.RigRoot;
                                log.AppendLine("C1-spit-origin " + origin.ToString("F2") +
                                    ", actor at " + actor.Pos.ToString("F2") +
                                    ", dummy rig scale " + (dummyRig == null ? "(no dummy)"
                                        : dummyRig.localScale.x.ToString("0.####")) +
                                    " vs live rig " + (liveRig == null ? "(none)"
                                        : liveRig.localScale.x.ToString("0.####")) +
                                    ", " + touching.Length +
                                    " collider(s) already overlapping the muzzle [" +
                                    string.Join("; ", who.ToArray()) + "] - anything here NOT excluded " +
                                    "detonates the shot at range zero (ProjectileLogic.cs:294)");
                            }
                            catch (Exception ex)
                            {
                                log.AppendLine("C1-spit-origin THREW " + ex.GetType().Name + ": " +
                                    ex.Message + " - GetShotOrigin itself is the failure " +
                                    "(TacticalPerception.cs:536 ElementAt throws when the " +
                                    "ProjectileOrigin name does not resolve on the dummy's addon)");
                            }

                            // A MISS IS A LEGITIMATE ROLL, NOT A DEFECT, so one shot cannot decide this
                            // arm. Measured: the first run fired a real projectile
                            // (FireWeaponAtTargetCrt ran PROJECTILES_ARE_ACTIVE to completion) and the
                            // engine then played its own MissedTargetVoice event - the whole pipeline
                            // worked and nothing landed. So this fires up to Shots times and passes on
                            // the first one that lands, reporting how many it took. Each shot costs
                            // APToUsePerc, so the ability going disabled mid-way is reported rather
                            // than treated as a failure to fire.
                            const int Shots = 4;
                            int statusWas = Statuses(mark).Length;
                            string spitBlew = null;
                            int fired = 0, blocked = 0;
                            float worstShot = 0f;
                            float s0 = Time.realtimeSinceStartup;
                            if (spitTgt == null) spitBlew = "the ability offers no actor target at all";
                            // A SHOT IS QUEUED, NOT PLAYED - which is why this cannot watch IsExecuting
                            // the way the bash arm does. ShootAbility.Activate:166-172 plays immediately
                            // ONLY for return fire, overwatch, FPS mode, an AI evaluation already
                            // running, or a weapon whose range is <= 1.5; anything else goes through
                            // EnqueueAction(soloAfterCurrent: true) and runs when the actor's action
                            // channel next pumps. Our spit has range 5, so it is always the queued path,
                            // and reading IsExecuting on the next line measured an empty queue and
                            // called a working shot a failure.
                            List<string> spitSeen = new List<string>();
                            for (int attempt = 0; attempt < Shots && spitBlew == null && mark != null; attempt++)
                            {
                                // NOT gated on IsEnabled. C1-melee reports the bash as enabled=False in
                                // this very context and it fires perfectly when activated directly -
                                // the flag is about whether the UI would OFFER the ability during the
                                // actor's turn, not whether the ability works. Pre-checking it here
                                // blocked all four shots on a creature whose shot had already been
                                // measured firing a real projectile, which measured the flag and not
                                // the weapon.
                                if (!shoot.IsEnabled(IgnoredAbilityDisabledStatesFilter
                                        .IgnoreNoValidTargetsAndEquipmentNotSelected)) blocked++;
                                try { shoot.Activate(spitTgt); }
                                catch (Exception ex) { spitBlew = ex.Message; break; }
                                fired++;
                                // The shot is QUEUED, so this waits for the OUTCOME rather than for a
                                // flag: either the target changes, or the shot has plainly had its
                                // chance. Eight seconds is under the 10s a missing animation event
                                // costs, so a genuine stall is still visible as a shot that used the
                                // whole window with the animator never leaving the idle.
                                float a0 = Time.realtimeSinceStartup;
                                while (Time.realtimeSinceStartup - a0 < 8f)
                                {
                                    string what = Playing(actor);
                                    if (spitSeen.Count == 0 || spitSeen[spitSeen.Count - 1] != what)
                                        spitSeen.Add(what);
                                    if (mark.Health < hpWas || Statuses(mark).Length > statusWas) break;
                                    yield return null;
                                }
                                float thisShot = Time.realtimeSinceStartup - a0;
                                if (thisShot > worstShot) worstShot = thisShot;
                                if (mark.Health < hpWas || Statuses(mark).Length > statusWas) break;
                            }
                            float spitTook = Time.realtimeSinceStartup - s0;
                            float hpNow = mark == null ? 0f : mark.Health;
                            string[] statusNow = Statuses(mark);
                            // POISON IS A STATUS, NOT A SUBTRACTION. This weapon's payload is
                            // Poison_DamageOverTimeDamageTypeEffectDef, so a PERFECT hit leaves Health
                            // untouched on impact and ticks it down on later turns - asserting a health
                            // drop alone would fail a shot that worked exactly as designed. Either
                            // outcome counts, and the line says WHICH so the two are never confused.
                            bool landed = mark != null &&
                                          (hpNow < hpWas || statusNow.Length > statusWas);
                            bool played = spitSeen.Any(s => s != null && s.IndexOf("idle",
                                              StringComparison.OrdinalIgnoreCase) < 0 && s != "(none)");
                            fail += Check(log, "C1-spit", spitBlew == null && landed && fired > 0 &&
                                                          worstShot < 9f,
                                (spitBlew != null ? "THREW " + spitBlew + "; " : "") + "'" + actor.name +
                                "' spat at '" + (mark == null ? "(nothing)" : mark.name) + "' " +
                                gap.ToString("F2") + " tile(s) away, chosen from " + far.Length +
                                " GetTargets offer(s): " + fired + " shot(s) fired" +
                                (blocked > 0 ? " (" + blocked + " of them while the ability reported " +
                                 "itself disabled, which the bash does too and fires anyway)" : "") +
                                ", Health " + hpWas.ToString("F1") + " -> " + hpNow.ToString("F1") +
                                ", status " + statusWas + " -> " + statusNow.Length + " [" +
                                string.Join(", ", statusNow) + "], worst shot " +
                                worstShot.ToString("F2") + "s of " + spitTook.ToString("F2") +
                                "s total, animator played [" + string.Join(" -> ", spitSeen.ToArray()) + "]" +
                                (landed ? " - landed as " + (hpNow < hpWas ? "immediate damage"
                                          : "a status, which is what a damage-over-time payload does")
                                        : " <- NOTHING LANDED in " + fired + " shot(s): " + (played
                                          ? "the spit clip PLAYED and the projectile flew, so every shot MISSED"
                                          : "the animator never left the idle, so the shot never ran")) +
                                (worstShot >= 9f ? " <- STALLED: each 10s is one animation event the " +
                                 "spit clip does not carry (AnimEventReceiver.cs:100,126)" : ""));
                        }
                    }
                    else
                        log.AppendLine("C1-spit SKIP this creature declares no ranged weapon " +
                                       "(ppcontent.json \"creature\": \"ranged\"), so there is nothing to fire");

                    // ARM 8c - IT WALKS, AND AT THE RIGHT SPEED.
                    //
                    // THIS IS THE ARM THAT WAS MISSING WHEN THE SPIDER STOPPED WALKING. Every other arm
                    // here spawns its target ADJACENT precisely so no pathing is involved, so a creature
                    // that bashes, spits and dies correctly can have lost locomotion entirely and this
                    // gate would still read green - which is exactly what happened.
                    //
                    // THE DESTINATION IS THE GAME'S OWN. GetTargetsDataInRange enumerates only positions
                    // this actor can actually PATH to (MoveAbility.cs:202-207), so the arm never has to
                    // guess whether a tile is walkable, and MoveAbilityTargetData.ToTarget() builds the
                    // target - a hand-made TacticalAbilityTarget is the trap the bash arm documents.
                    // The range argument is a PATH LENGTH in tiles (MoveAbility.cs:174-179).
                    //
                    // NOT WHILE SOMETHING IS EXECUTING: GetTargetsData logs an error and invalidates the
                    // situation cache if called mid-ability (MoveAbility.cs:170-173). Every arm above
                    // waits out its own ability, so by here the actor is idle.
                    //
                    // TWO ASSERTIONS, because "did not move" and "moved 125x too slowly" are different
                    // failures and a distance-only check passes the second one given enough time.
                    // The clock bound is DERIVED from the pace the bake retimes every walk to, so it
                    // cannot drift away from the thing it is checking.
                    MoveAbility move = actor.GetAbilities<MoveAbility>().FirstOrDefault();
                    if (move == null)
                        log.AppendLine("C1-walk VOID this creature carries no MoveAbility, so there is " +
                                       "nothing to order - a creature that cannot be told to walk is a " +
                                       "different defect from one that will not");
                    else
                    {
                        // A creature that has just bashed and spat has spent its turn, and an actor
                        // under 1 AP has Move DISABLED (MoveAbility.GetDisabledStateInternal:94). The
                        // harness hands the turn back the GAME'S own way - RestartAbilities refills AP
                        // and re-seeds AbilityTraits together, which is what a turn start really is.
                        actor.RestartAbilities();
                        if (actor.Animator != null)
                            actor.Animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

                        // WHAT THE NAVIGATION PATH NEEDS BEFORE IT WILL PLAY ANYTHING. A move that is
                        // accepted and then never animates is almost always a missing precondition
                        // rather than a broken clip, and these are the ones the path builder reads:
                        // TacticalPathProcessor.GetRunForwardAnim:207-216 refuses outright when
                        // ActiveNavigationClips.Run.Loop is null, and ActiveNavigationClips itself is a
                        // RESOLVED anim action (TacActorAnimActions.cs:42,53) that can be NULL when
                        // nothing matches the default context - the same match-by-def-identity trap the
                        // melee arm documents.
                        TacActorNavAnimActionDef nav = actor.ActorAnimActions == null
                            ? null : actor.ActorAnimActions.ActiveNavigationClips;
                        string why = nav == null
                            ? "ActiveNavigationClips is NULL - no TacActorNavAnimActionDef matched the " +
                              "default context, so the path builder has no run clip to ask for"
                            : "Run[" + Clip(nav.Run.Start) + "|" + Clip(nav.Run.Loop) + "|" +
                              Clip(nav.Run.Stop) + "] all=" + nav.Run.HasAllAnimations +
                              ", turnAnims=" + PathProcessorUtils.UsesTurnAnimations(actor) +
                              ", turnBeforeSprint=" + PathProcessorUtils.ShouldTurnInPlaceBeforeSprint(actor);

                        const float Want = 3f;      // tiles of path to ask for
                        const float Least = 2.5f;   // and the least we accept having travelled
                        MoveAbilityTargetData[] spots;
                        try { spots = move.GetTargetsDataInRange(null, Want).ToArray(); }
                        catch (Exception ex) { spots = new MoveAbilityTargetData[0];
                                               log.AppendLine("C1-walk targets THREW " + ex.Message); }
                        // The FURTHEST reachable spot inside the ask: nearest-first would let a
                        // half-tile shuffle satisfy an arm whose whole point is that it travels.
                        MoveAbilityTargetData spot = spots.OrderByDescending(s => s.PathLength).FirstOrDefault();

                        Vector3 walkFrom = actor.Pos;
                        string blew = null;
                        float t0 = Time.realtimeSinceStartup;
                        if (spot == null)
                            blew = "GetTargetsDataInRange(" + Want.ToString("F0") + ") offered no " +
                                   "reachable tile at all - either the actor is walled in or its nav " +
                                   "agent resolves to nothing";
                        else { try { move.Activate(spot.ToTarget()); }
                               catch (Exception ex) { blew = ex.Message; } }

                        // Deterministic completion is the SAME signal the bash arm waits on: the
                        // ability stays IsExecuting until its PlayingAction ends, and MoveAbility
                        // .OnPlayingActionEnd:83-90 cancels navigation as it closes.
                        //
                        // SAMPLED WHILE IT WAITS, for the same reason the bash arm samples: a move that
                        // goes nowhere is either "the navigation state was never entered" or "it was
                        // entered playing a clip that never ends", and only the clip names tell them
                        // apart. A multiple of ten seconds here is the signature of a BLOCKING
                        // animation event the clip does not carry (AnimEventReceiver.cs:100,126).
                        List<string> played = new List<string>();
                        float tick = 0f;
                        while (blew == null && move.IsExecuting && Time.realtimeSinceStartup - t0 < 30f)
                        {
                            if (actor.Animator != null && Time.realtimeSinceStartup - t0 >= tick)
                            {
                                tick += 1f;
                                AnimatorClipInfo[] now = actor.Animator.GetCurrentAnimatorClipInfo(0);
                                string what = now.Length == 0 || now[0].clip == null ? "(none)" : now[0].clip.name;
                                if (played.Count == 0 || played[played.Count - 1] != what) played.Add(what);
                            }
                            yield return null;
                        }

                        float took = Time.realtimeSinceStartup - t0;
                        float went = Vector3.Distance(walkFrom, actor.Pos);
                        float asked = spot == null ? 0f : spot.PathLength;
                        // The ceiling: the retimed pace, the path actually asked for, x3 for the start
                        // and stop clips and any turn in place, plus 3s of slack. A 125x-slow walk needs
                        // ~69s for three tiles and cannot hide under this.
                        float ceiling = asked / Import.Treadmill.ShippedPace * 3f + 3f;
                        bool moved = went >= Least;
                        bool timely = took <= ceiling;
                        fail += Check(log, "C1-walk", blew == null && moved && timely,
                            (blew != null ? "REFUSED " + blew + "; " : "") + "'" + actor.name +
                            "' was ordered " + asked.ToString("F2") + " tile(s) of path (best of " +
                            spots.Length + " reachable offer(s) within " + Want.ToString("F0") +
                            ") and travelled " + went.ToString("F2") + " tile(s) in " +
                            took.ToString("F2") + "s = " +
                            (took > 0.01f ? (went / took).ToString("F2") : "?") +
                            " tile/s, against the baked pace " +
                            Import.Treadmill.ShippedPace.ToString("F4") + " and a ceiling of " +
                            ceiling.ToString("F2") + "s, animator played [" +
                            string.Join(" -> ", played.ToArray()) + "], " + why + ", nav agent '" +
                            (actor.TacticalNav == null || actor.TacticalNav.TacticalNavDef == null
                                ? "?" : actor.TacticalNav.TacticalNavDef.AgentType) + "'" +
                            (!moved ? " <- IT DID NOT WALK: root motion drives this, so a walk clip " +
                             "whose ramp measures zero leaves the creature standing while every " +
                             "stationary action still works" : "") +
                            (!timely ? " <- TOO SLOW: it travelled, but far under the pace the bake " +
                             "retimes every walk to - the classic symptom of a displacement measured " +
                             "in the wrong space" : ""));
                    }

                    // ARM 8d - IT CROSSES A LEVEL CHANGE, which is what actually HUNG the user.
                    //
                    // C1-walk orders a path across open floor, and that is exactly how the freeze got
                    // through: the traversal families (ladder, drop, vault, jump, mount) were filled
                    // with our flat walk or idle, HasAllAnimations went TRUE, and
                    // ClimbPathProcessor.EmitClimb:90 then built a MEASURED vertical segment off a clip
                    // that never rises. The mover waited to arrive where the animation could not take
                    // it. Flat ground never touches that code, so no arm could see it.
                    //
                    // FALSIFIED, AND IT DID NOT CATCH THE BUG - read this before trusting the arm.
                    // MEASURED by disabling the traversal clearing and re-running: this arm PASSES
                    // EITHER WAY. With the traversal slots left FILLED (the state that froze the user)
                    // it arrived in 2,23s over 1 link of 12; with them cleared it arrived in 13,09s
                    // over 2 links of 13. So the link this save can reach - a 0,56 step - is NOT the
                    // link type that hangs, and the arm does not yet reproduce the window freeze. It
                    // guards arrival across A level change, which is worth having, but it is NOT proof
                    // that the freeze is fixed and must not be cited as such.
                    // ponytail: left honest rather than tuned until it goes red - the next step is a
                    // save whose spawn reaches a real vault/window (JumpOverLowWall,
                    // ClimbUpLowObstacle), not a stricter clock on a step it already clears.
                    //
                    // ASSERTED ON THE SEGMENT, NOT JUST ON ARRIVAL. Reaching the destination proves
                    // nothing if the path was flat all along - the game would simply have walked round.
                    // NavMeshPathRequest.GetLinkForSegment:44-51 returns the NavLink for a segment and
                    // null for ordinary ground, so counting non-null links is the game's OWN answer to
                    // "was this a climb". That count is what makes this arm unable to pass by walking.
                    if (move != null)
                    {
                        actor.RestartAbilities();
                        if (actor.Animator != null)
                            actor.Animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

                        // Wider than C1-walk on purpose: a level change is rarely within three tiles,
                        // and a candidate on a DIFFERENT height is the cheap honest filter for "getting
                        // there requires leaving the floor".
                        const float Reach = 12f;
                        MoveAbilityTargetData[] all;
                        try { all = move.GetTargetsDataInRange(null, Reach).ToArray(); }
                        catch (Exception ex) { all = new MoveAbilityTargetData[0];
                                               log.AppendLine("C1-traverse targets THREW " + ex.Message); }
                        MoveAbilityTargetData high = all
                            .Where(s => Mathf.Abs(s.Position.y - actor.Pos.y) > 0.5f)
                            .OrderByDescending(s => Mathf.Abs(s.Position.y - actor.Pos.y))
                            .FirstOrDefault();

                        if (high == null)
                        {
                            // VOID, never PASS. An arm that quietly settles for flat ground when it
                            // cannot find a climb is the vacuous-green failure this session already
                            // caught once, so this says plainly that the MAP could not pose the
                            // question - and that the next step is a save that can, not a weaker rule.
                            fail += Check(log, "C1-traverse", false,
                                "VOID no reachable tile within " + Reach.ToString("F0") +
                                " differs in height from the actor by more than 0,5 (" + all.Length +
                                " offer(s) considered), so THIS SAVE CANNOT POSE THE QUESTION. The arm " +
                                "refuses to fall back to flat ground: that would pass while the freeze " +
                                "it exists to catch went straight through. Re-run the gate on a save " +
                                "whose spawn has a ladder, roof drop or low wall in reach.");
                        }
                        else
                        {
                            Vector3 fromT = actor.Pos;
                            string blewT = null;
                            float tt = Time.realtimeSinceStartup;
                            try { move.Activate(high.ToTarget()); }
                            catch (Exception ex) { blewT = ex.Message; }

                            // Sampled WHILE it runs: the path is built at activation and cleared when
                            // the move ends, so the link count has to be read during the walk.
                            int links = 0, segs = 0;
                            List<string> playedT = new List<string>();
                            float tickT = 0f;
                            while (blewT == null && move.IsExecuting &&
                                   Time.realtimeSinceStartup - tt < 30f)
                            {
                                try
                                {
                                    TacticalPathRequest p = actor.TacticalNav == null
                                        ? null : actor.TacticalNav.CurrentTacPath;
                                    if (p != null && p.Path != null && p.Path.Count > segs)
                                    {
                                        segs = p.Path.Count;
                                        int n = 0;
                                        for (int i = 0; i < segs; i++)
                                            if (p.GetLinkForSegment(i) != null) n++;
                                        if (n > links) links = n;
                                    }
                                }
                                catch { /* the path is torn down mid-read; the counts already taken stand */ }
                                if (actor.Animator != null && Time.realtimeSinceStartup - tt >= tickT)
                                {
                                    tickT += 1f;
                                    AnimatorClipInfo[] now = actor.Animator.GetCurrentAnimatorClipInfo(0);
                                    string what = now.Length == 0 || now[0].clip == null ? "(none)" : now[0].clip.name;
                                    if (playedT.Count == 0 || playedT[playedT.Count - 1] != what) playedT.Add(what);
                                }
                                yield return null;
                            }

                            float tookT = Time.realtimeSinceStartup - tt;
                            float dyWant = Mathf.Abs(high.Position.y - fromT.y);
                            float dyGot = Mathf.Abs(actor.Pos.y - fromT.y);
                            float left = Vector3.Distance(actor.Pos, high.Position);
                            // Arrived, climbed, and the path really did contain a link. The clock
                            // ceiling is generous - a climb is slower than a walk - but a HANG is
                            // 30s of nothing, so it separates cleanly.
                            bool arrived = left <= 1.5f;
                            bool climbed = dyGot > 0.5f;
                            fail += Check(log, "C1-traverse",
                                blewT == null && arrived && climbed && links > 0 && tookT < 25f,
                                (blewT != null ? "THREW " + blewT + "; " : "") + "'" + actor.name +
                                "' was ordered to a tile " + dyWant.ToString("F2") +
                                " above/below it (" + high.PathLength.ToString("F2") + " tile(s) of path, " +
                                all.Length + " offer(s) within " + Reach.ToString("F0") + "): it ended " +
                                left.ToString("F2") + " tile(s) from the target having changed height by " +
                                dyGot.ToString("F2") + ", in " + tookT.ToString("F2") + "s, and the path " +
                                "it walked carried " + links + " LINK segment(s) of " + segs +
                                " (NavMeshPathRequest.GetLinkForSegment:44-51 - null is ordinary " +
                                "ground), animator played [" + string.Join(" -> ", playedT.ToArray()) + "]" +
                                (links == 0 ? " <- NO VERTICAL SEGMENT: it walked round instead of " +
                                 "climbing, so this run did NOT exercise the code that froze the user" : "") +
                                (!climbed ? " <- IT NEVER CHANGED HEIGHT" : "") +
                                (!arrived ? " <- IT DID NOT ARRIVE" : "") +
                                (tookT >= 25f ? " <- HUNG: the classic shape of a vertical segment " +
                                 "measured off a clip that cannot perform it" : ""));
                        }
                    }

                    float max = actor.Health.Max;
                    float before = actor.Health;
                    actor.Health.Subtract(1f);
                    float after = actor.Health;
                    fail += Check(log, "C1-damage", max > 0f && after < before,
                        "Health " + before.ToString("F1") + "/" + max.ToString("F1") + " -1 -> " +
                        after.ToString("F1") + " (Health.Subtract is the very call " +
                        "TacticalActorBase.ApplyDamageInternal:874 makes)");

                    // ARM 7 - and reducing it to zero reaches Die(). OnHealthChange:616-622 is the only
                    // route to it, so IsDead flipping proves the whole chain ran.
                    actor.Health.Subtract(actor.Health);
                    yield return null;
                    // ...AND THE DEATH IS PLAYED, not merely recorded. RagdollDieAbility.cs:92-95
                    // sets cullingMode, triggers "Die" and then BLOCKS on the "Ragdoll" event, so a
                    // creature whose death state holds a clip without that event stands upright for
                    // ten seconds before it falls. Watching the animator answers both halves: which
                    // clip the Die state actually reached, and how long the wait took.
                    List<string> dying = new List<string> { Playing(actor) };
                    float d0 = Time.realtimeSinceStartup;
                    // Four seconds is longer than any death animation and shorter than ONE missed
                    // event, so a clip list that never leaves the idle is the ten-second stall showing.
                    while (Time.realtimeSinceStartup - d0 < 4f)
                    {
                        string what = Playing(actor);
                        if (dying[dying.Count - 1] != what) dying.Add(what);
                        yield return null;
                    }
                    log.AppendLine("C1-death " + (dying.Count > 1 ? "PASS" : "WARN") +
                        " the animator played [" + string.Join(" -> ", dying.ToArray()) + "] in " +
                        (Time.realtimeSinceStartup - d0).ToString("F2") + "s (RagdollDieAbility.cs:94-95 " +
                        "SetTrigger(\"Die\") then WaitForEvent(\"Ragdoll\"), 10s if the clip has no such event)");
                    fail += Check(log, "C1-kill", actor.IsDead,
                        "Health -> " + ((float)actor.Health).ToString("F1") + ", IsDead=" + actor.IsDead +
                        ", die ability='" + (actor.GetPreferredDieAbility() == null ? "(none)"
                         : actor.GetPreferredDieAbility().GetType().Name) + "'" +
                        (actor.IsDead ? "" : " <- Health hit zero and Die() was never reached"));
                }
                finally
                {
                    // Hand the targeting refcount back, or the actor is left believing something is for
                    // ever aiming at it (TacticalPerceptionBase.RequestForceTargetable:69-72 logs an
                    // error past six outstanding requests).
                    try { if (targetable != null) targetable.ClearForceTargetable(); } catch (Exception) { }
                    log.Append(!measured
                        ? "ct_creature: C1 VOID - no arm ran, nothing was measured on a live creature"
                        : fail == 0 ? "ct_creature: C1 arms PASS" : "ct_creature: C1 " + fail + " FAILURE(S)");
                    ContentToolMain.Say(log.ToString());
                    Dev.AsyncGate.Pending--;
                    Destroy(gameObject);
                }
            }

            /// <summary>
            /// <c>SpawnActorAbility.GetActorInstanceData:47-110</c> + <c>:131</c>, without the ability:
            /// the character data generates its own instance component set and instance data, the
            /// placement and faction are taken off a live actor, and the game's spawner does the rest.
            /// </summary>
            /// <summary>The first live actor the given one is at war with, or null.</summary>
            private static TacticalActor Hostile(TacticalLevelController tac, TacticalActor of)
            {
                foreach (TacticalFaction f in tac.Factions)
                {
                    if (of.TacticalFaction.GetRelationTo(f) !=
                        PhoenixPoint.Common.Core.FactionRelation.Enemy) continue;
                    IEnumerable<TacticalActorBase> actors = null;
                    try { actors = f.Actors; } catch (Exception) { }
                    if (actors == null) continue;
                    foreach (TacticalActorBase a in actors)
                        if (a is TacticalActor t && t.IsAlive) return t;
                }
                return null;
            }

            private static TacticalActor Spawn(TacCharacterDef def, TacticalActor host, TacticalActor beside)
            {
                // A CLONE, and a LIVING one. TacCharacterData.Clone is the game's own copy, so the
                // installed def is never mutated by a measurement. Strength is forced only when the
                // template ships none: Health.Max is built from bodypart aspects
                // (CharacterStats.InitStats:136-163), a bodypart-free template enters play at 0/0,
                // FinalizeEnterPlay:546-548 sees IsDead and runs PostProcessDeath - which switches the
                // renderers and colliders off. Every collider arm would then be measuring a CORPSE and
                // would fail for a reason that has nothing to do with the fit. C1-hp reports the
                // template's own value; this only makes the subject alive enough to shoot at.
                TacCharacterData data = ((TacCharacterData)def.InstanceData).Clone() as TacCharacterData;
                Shipped = data.Strength;
                if (data.Strength <= 0) data.Strength = 20;
                ComponentSetDef set = data.GenerateInstanceComponentSetDef();
                TacActorInstanceData inst = (TacActorInstanceData)data.GenerateInstanceData();
                inst.Source = def;
                inst.OverrideTransform = true;
                // ONE TILE IN FRONT of the host, not on top of it. The melee arm needs a direction to
                // face: BashAbility.BashCrt:429-433 computes `forward = target.Actor.Pos - Pos` and
                // then blocks on TacticalNav.Face(forward), which never resolves for a zero vector -
                // measured, the bash sat there until the arm's own 45s deadline. One tile also keeps
                // the creature inside every melee range there is.
                inst.Pos = beside.Pos + beside.Rot * Vector3.forward;
                inst.Rot = beside.Rot;
                inst.FactionDef = host.TacticalFaction.TacticalFactionDef;
                inst.MissionParticipant = host.MissionParticipant;
                inst.AIActorData = new AIActorData();
                TacticalActor a = ActorSpawner.SpawnActor<TacticalActor>(set, inst);
                // Characters colliders are live only while something is targeting - the game's rule,
                // mirrored by CreatureFit.AfterRagdollMode. The gate is that something.
                a.AddonsManager?.SetRagdollMode(CollidersRagdollActivationMode.Targeting);
                return a;
            }

            /// <summary>The status names on an actor right now, or an empty array. Defensive because a
            /// damage-over-time payload lands as a STATUS and the arm above has to be able to see it
            /// without assuming every actor carries a status component.</summary>
            private static string[] Statuses(TacticalActorBase a)
            {
                try
                {
                    if (a == null || a.Status == null || a.Status.Statuses == null) return new string[0];
                    return a.Status.Statuses.Where(s => s != null)
                            .Select(s => s.GetType().Name).ToArray();
                }
                catch (Exception) { return new string[0]; }
            }

            /// <summary>The clip the actor's animator is playing right now, by name.</summary>
            private static string Playing(TacticalActorBase a)
            {
                if (a == null || a.Animator == null) return "(none)";
                AnimatorClipInfo[] now = a.Animator.GetCurrentAnimatorClipInfo(0);
                return now.Length == 0 || now[0].clip == null ? "(none)" : now[0].clip.name;
            }

            private static UnityEngine.Collider[] OnLayer(TacticalActorBase a, int layer)
            {
                return a.GetComponentsInChildren<Collider>(true).Where(c => c.gameObject.layer == layer).ToArray();
            }

            private static Bounds ModelBounds(TacticalActorBase a)
            {
                Renderer[] r = a.GetComponentsInChildren<Renderer>(true)
                                .Where(x => x.enabled && x.gameObject.activeInHierarchy).ToArray();
                if (r.Length == 0) return new Bounds(a.transform.position, Vector3.zero);
                Bounds b = r[0].bounds;
                for (int i = 1; i < r.Length; i++) b.Encapsulate(r[i].bounds);
                return b;
            }

            private static TacticalLevelController Current()
            {
                Base.Levels.Level lvl = GameUtl.CurrentLevel();
                return lvl == null ? null : lvl.GetComponent<TacticalLevelController>();
            }

            /// <summary>Any live actor, used only as a place to stand and a faction to belong to.</summary>
            private static TacticalActor Anyone(TacticalLevelController tac)
            {
                foreach (TacticalFaction f in tac.Factions)
                {
                    IEnumerable<TacticalActorBase> actors = null;
                    try { actors = f.Actors; } catch (Exception) { }
                    if (actors == null) continue;
                    foreach (TacticalActorBase a in actors)
                        if (a is TacticalActor t && t.IsAlive) return t;
                }
                return null;
            }

            /// <summary>ct_mission's own loader: by NAME, and only a save that says it is tactical.</summary>
            private IEnumerator<NextUpdate> Load(string name)
            {
                ByRef<List<SavegameMetaData>> all = new ByRef<List<SavegameMetaData>>();
                yield return Timing.Current.Call(
                    GameUtl.GameComponent<SerializationComponent>().GetSavegames(all));
                List<SavegameMetaData> hits = (all.Value ?? new List<SavegameMetaData>())
                    .Where(m => string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase)).ToList();
                if (hits.Count != 1)
                {
                    refusal = "REFUSED: " + hits.Count + " savegames answer to '" + name + "' (run 'ct_mission list')";
                    yield break;
                }
                PPSavegameMetaData pp = hits[0] as PPSavegameMetaData;
                if (pp == null || !pp.IsTacticalSave || !pp.IsLoadable())
                {
                    refusal = "REFUSED: '" + name + "' does not declare itself a loadable tactical save";
                    yield break;
                }
                ContentToolMain.Say("ct_creature: loading tactical save '" + pp.Name + "'");
                GameUtl.GameComponent<PhoenixGame>().FinishLevelAndLoadGame(pp);
            }

            private static int Check(StringBuilder log, string arm, bool ok, string detail)
            {
                log.AppendLine(arm + (ok ? " PASS " : " FAIL ") + detail);
                return ok ? 0 : 1;
            }
        }
    }
}
