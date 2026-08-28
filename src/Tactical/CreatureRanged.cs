using System.Collections.Generic;
using System.Linq;
using Base.AI.Defs;
using Base.Core;
using Base.Defs;
using PhoenixPoint.Common.Entities.Addons;
using PhoenixPoint.Common.Entities.GameTags;
using PhoenixPoint.Common.Entities.GameTagsSharedData;
using PhoenixPoint.Common.Entities.Items;
using PhoenixPoint.Tactical.AI;
using PhoenixPoint.Tactical.Entities;
using PhoenixPoint.Tactical.Entities.Animations;
using PhoenixPoint.Tactical.Entities.Equipments;
using PhoenixPoint.Tactical.Entities.Weapons;
using UnityEngine;

namespace Morgott.ContentTool.Tactical
{
    /// <summary>
    /// ============ A SECOND, RANGED ATTACK, DRIVEN BY THE STOCK AI ============
    ///
    /// The melee bash needed NO new def because the AI's attack picker asks the WEAPON
    /// (AIActionMoveAndAttack.GetAttackAbility:73-86): a melee payload routes to the BashAbility, and
    /// ANY OTHER payload routes to <c>weapon.DefaultShootAbility</c>. So "give the creature a spit" is
    /// not a new ability system - it is carrying a SECOND bodypart weapon whose payload is not melee.
    /// Four things stand between that sentence and a shot actually leaving the creature, and each is
    /// one section below:
    ///
    /// 1. THE ACTION LIST. The donor's AIActionsTemplateDef (TacAIActorDef.AIActionsTemplateDef) may
    ///    contain no ranged action at all - the Swarmer's holds MoveAndStrike and friends, so its AI
    ///    would carry the spitter and never fire it. The template AND the TacAIActorDef are CLONED
    ///    (both are shared with every shipped unit of the family) and the manifest's AI action def -
    ///    default MoveAndShoot_AIActionDef, the ONE-TILE sibling of MoveAndStrike, same
    ///    AIActionMoveAndAttackDef class, only the Weight differs - is APPENDED, never substituted.
    ///
    /// 2. THE SHOT ORIGIN, which is the hard part. TacticalPerception.GetShotOrigin:526-536 resolves
    ///    the muzzle through the target dummy's copy of the weapon addon, and REFUSES a weapon whose
    ///    addon is not visible (Addon.IsVisible, Addon.cs:199-201 = an ACTIVE _visualRootGameObject) -
    ///    the return is Vector3NaN and CanShoot:226-229 then reports "no shot" for every target. The
    ///    melee trick of SkinData = null therefore CANNOT be reused here, and keeping the donor
    ///    weapon's own SkinData would hang ITS geometry (a Crabman head) on our rig - the exact leak
    ///    the donor-free audit exists to catch. But IsVisible needs only an ACTIVE GameObject, not a
    ///    renderer, and the muzzle is found BY NAME (DamagePayload.ProjectileOrigin, default
    ///    "EXT_ShootPoint", DamagePayload.cs:79). So the engine SYNTHESISES the minimal thing the
    ///    resolver walks: an empty prefab with two named empty children, handed to the weapon through
    ///    our own <see cref="ShootPointSkinDataDef"/> - the shipped SimpleBodyPartSkinDataDef cannot
    ///    carry it, its prefab is an ADDRESSABLE and cannot point at a runtime GameObject.
    ///
    /// 3. THE SLOT. The chassis provides several (the Swarmer's three: Torso, LeftWing, RightWing);
    ///    the melee bash took the first, so the spitter binds to the next FREE one and is APPENDED to
    ///    Data.BodypartItems - CharacterBodyState.SetupBodyParts:89-97 silently drops an incompatible
    ///    bodypart, so the game's own ProvidesCompatibleSlotFor is asked out loud at build time.
    ///
    /// 4. THE ANIMATION. Shoot anims bind by EQUIPMENT FILTER (TacActorShootAnimActionDef), and
    ///    GetShotOrigin dereferences the matched action's ShootPose with NO null check - a ranged
    ///    weapon no shoot action accepts is an NRE, not a missing animation. So the donor's own
    ///    shoot-filtered action is taught to accept our weapon (the same AlsoAccept the melee uses),
    ///    and when the manifest maps a "ranged" clip a CLONE of that action - filtered to ONLY our
    ///    weapon, its clip slots rewritten to the spit clip - is inserted BEFORE the general one,
    ///    because match order decides.
    /// </summary>
    internal static class CreatureRanged
    {
        /// <summary>
        /// OUR OWN CONCRETE SKIN DATA, because the abstract base demands a prefab source and the
        /// shipped concrete type (SimpleBodyPartSkinDataDef) stores it as an
        /// AssetReferenceGameObject - an addressables KEY, which can only name an asset in a shipped
        /// catalog, never a GameObject this mod built at runtime. A plain reference field is the
        /// whole difference. SkinsToRig stays FALSE: Addon.AttachVisuals:1060-1079 either re-skins
        /// the visuals bone-by-bone onto the rig (needs bones we do not have) or parents the whole
        /// prefab at the slot's attachment point - and a two-empty-transform muzzle marker has no
        /// bones, so the attachment-point path is the only honest one.
        /// </summary>
        internal sealed class ShootPointSkinDataDef : AddonSkinDataBase
        {
            internal GameObject Visuals;
            public override GameObject GetVisuals(IGameTagsProvider tagsProvider) { return Visuals; }
            public override IEnumerable<GameObject> GetAllVariants() { yield return Visuals; }
        }

        /// <summary>
        /// Wire the manifest's ranged weapon onto the creature. Reads everything first and mutates
        /// only after every input resolved - a FAIL here leaves a working melee-only creature, never
        /// one that looks armed and cannot shoot.
        /// </summary>
        /// <summary>
        /// THE MINIMUM THING THE ENGINE WILL CALL "VISIBLE", carrying named empty children for the
        /// transforms it will later look up BY NAME. An inactive holder (Instantiate copies
        /// activeSelf, so the TEMPLATE never renders while every INSTANCE is born active =
        /// IsVisible), a root, and one empty per name. No renderer anywhere - IsVisible
        /// (Addon.cs:199-201) asks only for an ACTIVE GameObject, and that economy is the whole
        /// reason any of this can be synthesised instead of stealing the donor's geometry.
        ///
        /// TWO CALLERS, TWO BUGS OF THE SAME SHAPE. The ranged weapon needs it because
        /// GetShotOrigin refuses a weapon whose addon is not visible. The MELEE weapon needs it
        /// because BashAbility.GetDisabledStateInternal:237 does
        /// <c>weapon.FindTransform(BashPoint)</c>, and FindTransform searches the addon's
        /// OwnedTransforms (Addon.cs:1374) - a SkinData-less weapon owns none, returns null, and the
        /// ability reports NoSuitableEquipment, which is exactly what GREYS THE PLAYER'S BUTTON.
        /// Both are "the def names a transform, so something has to own one".
        ///
        /// ponytail: one helper, because the second caller proved the first was not a special case.
        /// </summary>
        internal static ShootPointSkinDataDef SynthSkin(DefRepository repo, Creature c, string what,
                                                        string defName, params string[] childNames)
        {
            GameObject holder = new GameObject("ct_creature_shootpoint_templates");
            holder.SetActive(false);
            Object.DontDestroyOnLoad(holder);
            GameObject visuals = new GameObject(CreatureBuild.Prefix + c.Id + "_" + what);
            visuals.transform.SetParent(holder.transform, false);
            foreach (string n in childNames)
                if (!string.IsNullOrEmpty(n))
                    new GameObject(n).transform.SetParent(visuals.transform, false);

            ShootPointSkinDataDef template = ScriptableObject.CreateInstance<ShootPointSkinDataDef>();
            template.SkinsToRig = false;
            ShootPointSkinDataDef skin = CreatureBuild.Clone(repo, c, (BaseDef)template,
                                                             defName) as ShootPointSkinDataDef;
            Object.Destroy(template);
            skin.SkinsToRig = false;
            // (Re)assigned on every load, registered or fresh: Visuals is a plain reference field the
            // repo never serializes, and a re-entrant load has just built a NEW prefab.
            skin.Visuals = visuals;
            return skin;
        }

        internal static void Ranged(DefRepository repo, Creature c, TacCharacterDef donor,
                                    AddonDef chassis, TacCharacterDef unit,
                                    TacActorAnimActionsDef anims, ComponentSetDef setClone,
                                    GameObject rig, GameTagDef donorTag, SharedGameTagsDataDef shared)
        {
            // ---- resolve EVERYTHING before touching anything --------------------------------------
            WeaponDef donorWeapon = repo.GetAllDefs<WeaponDef>()
                .FirstOrDefault(w => string.Equals(w.name, c.Man.Ranged,
                                                   System.StringComparison.OrdinalIgnoreCase));
            if (donorWeapon == null)
            {
                c.Say("ct_creature FAIL ranged: ppcontent.json \"creature\": \"ranged\" names '" +
                      c.Man.Ranged + "' but no shipped WeaponDef has that name. Nothing ranged was " +
                      "wired; the creature keeps its melee.");
                return;
            }
            if (donorWeapon.DamagePayload == null ||
                donorWeapon.DamagePayload.DamageDeliveryType == DamageDeliveryType.Melee)
            {
                // A melee weapon here would be routed to a BashAbility we did not clone
                // (GetAttackAbility:73-86) - the "second attack" would silently be the first again.
                c.Say("ct_creature FAIL ranged: '" + donorWeapon.name + "' has DamageDeliveryType " +
                      (donorWeapon.DamagePayload == null ? "(no payload)"
                           : donorWeapon.DamagePayload.DamageDeliveryType.ToString()) +
                      " - AIActionMoveAndAttack.GetAttackAbility:73-86 only routes a NON-melee " +
                      "payload to DefaultShootAbility. Name a ranged bodypart weapon such as " +
                      "Crabman_Head_Spitter_WeaponDef. Nothing ranged was wired.");
                return;
            }

            AIActionDef aiAction = repo.GetAllDefs<AIActionDef>()
                .FirstOrDefault(d => string.Equals(d.name, c.Man.AiAction,
                                                   System.StringComparison.OrdinalIgnoreCase));
            TacAIActorDef donorAI = setClone.Components.OfType<TacAIActorDef>().FirstOrDefault();
            if (aiAction == null || donorAI == null || donorAI.AIActionsTemplateDef == null)
            {
                c.Say("ct_creature FAIL ranged: AI action '" + c.Man.AiAction + "' " +
                      (aiAction == null ? "does not exist in the repo" : "exists") +
                      ", donor TacAIActorDef " + (donorAI == null ? "MISSING from the component set"
                          : donorAI.AIActionsTemplateDef == null ? "has no AIActionsTemplateDef"
                          : "ok") + " - without both the AI can never CHOOSE to shoot, so nothing " +
                      "ranged was wired.");
                return;
            }

            // The donor's own shoot-filtered anim action, found through the same donor item the melee
            // rides on (the Swarmer's torso IS its weapon) - this is what AlsoAccept will append our
            // weapon beside, and it must list the donor item in an EQUIPMENT list specifically:
            // TacActorShootAnimActionDef.Match:107-109 answers a ShootContext with EquipmentMatch,
            // and Bodyparts alone never satisfies that.
            WeaponDef donorItem = CreatureBuild.DonorMeleeBodypart(donor);
            TacActorShootAnimActionDef general = (anims.AnimActions ?? new TacActorAnimActionBaseDef[0])
                .OfType<TacActorShootAnimActionDef>()
                .FirstOrDefault(s => donorItem != null &&
                    ((s.Equipments != null && s.Equipments.Contains(donorItem)) ||
                     (s.EquipmentList != null && s.EquipmentList.Equipments != null &&
                      s.EquipmentList.Equipments.Contains(donorItem))));
            if (general == null || general.ShootPose == null)
            {
                // GetShotOrigin does GetAnimAction<TacActorShootAnimActionDef>(...).ShootPose with NO
                // null check - shipping this weapon anyway would be an NRE at the first trigger pull.
                c.Say("ct_creature FAIL ranged: the donor's anim actions hold " + (general == null
                          ? "NO TacActorShootAnimActionDef that equipment-filters on '" +
                            (donorItem == null ? "(no donor bodypart weapon)" : donorItem.name) + "'"
                          : "'" + general.name + "' but its ShootPose is null") +
                      " - GetShotOrigin dereferences the matched action's ShootPose with no null " +
                      "check, so a weapon it cannot animate is an NRE, not a missing animation. " +
                      "Nothing ranged was wired.");
                return;
            }

            // The next FREE provided slot - the melee took one, and double-booking it would make
            // CharacterBodyState.SetupBodyParts:89-97 silently drop whichever loses.
            HashSet<AddonSlotDef> taken = new HashSet<AddonSlotDef>(
                (unit.Data.BodypartItems ?? new ItemDef[0]).Where(i => i != null)
                    .SelectMany(i => i.RequiredSlotBinds ?? new AddonDef.RequiredSlotBind[0])
                    .Select(b => b.RequiredSlot).Where(s => s != null));
            int slot = -1;
            for (int i = 0; i < chassis.ProvidedSlots.Length; i++)
                if (chassis.ProvidedSlots[i].ProvidedSlot != null &&
                    !taken.Contains(chassis.ProvidedSlots[i].ProvidedSlot)) { slot = i; break; }
            if (slot < 0)
            {
                c.Say("ct_creature FAIL ranged: the chassis provides " + chassis.ProvidedSlots.Length +
                      " slot(s) and every one is already bound - there is nowhere to hang a second " +
                      "weapon. Nothing ranged was wired.");
                return;
            }

            Transform bone = ShootBone(c, rig);
            if (bone == null) return;                    // ShootBone already said why, loudly

            // ---- everything resolved; now mint and mutate ----------------------------------------

            // TELL THE SLOT WHERE THE MUZZLE HANGS, or the shot leaves from the sky.
            //
            // Non-skinned visuals are positioned by the SLOT, not by the addon:
            //   Addon.GetAttachTransform  - returns the bare manager.RigRoot when the parent slot's
            //                               AttachmentPointName is blank, and only otherwise does
            //                               FindTransform(name) to land on a bone
            //   Addon.AttachVisuals:1074-5 - VisualRoot.SetParent(attachTransform) then
            //                               ResetTransform(), which resets local SCALE to 1
            // The shipped chassis slots carry NO AttachmentPointName (measured: all three of
            // _Swarmer_Chassis_AddonDef's are empty) because every shipped bodypart on them IS skinned
            // to the rig. Ours is not, so it fell into the first branch, hung off the rig ROOT, and the
            // ResetTransform threw away the rig's own "scale" - 0.008 for this model. Every offset was
            // then applied 125x too large.
            //
            // MEASURED, and this is the whole proof: C1-spit-origin reported the shot leaving
            // (1.16, 19.05, -32.57) with the actor standing at (3.50, 0.04, -15.50). That delta is
            // (-2.34, 19.01, -17.07); divide it by 125 and it is (-0.019, 0.152, -0.137) - exactly a
            // muzzle a sixth of a tile above a half-tile creature. Not a stray value: the scale factor
            // itself, applied once too often. Zero colliders were overlapping that point, which is why
            // it never hit anything and every shot reported "missed".
            //
            // Naming the bone puts the muzzle INSIDE the scaled rig, where the reset is harmless.
            chassis.ProvidedSlots[slot].AttachmentPointName = bone.name;
            c.Say("ct_creature PASS ranged muzzle attaches at '" + bone.name + "' on slot '" +
                  chassis.ProvidedSlots[slot].ProvidedSlot.name + "' - a slot with no " +
                  "AttachmentPointName drops non-skinned visuals on the RIG ROOT and " +
                  "Addon.AttachVisuals:1075 ResetTransform()s the rig's own scale away, which put the " +
                  "muzzle 1/" + c.Scale.ToString("0.####") + "x too far out");

            // THE WEAPON: same strip the melee gets (no donor geometry, no donor tags), but where the
            // melee NULLS SkinData, this one gets OUR synthesised muzzle - see the class remark.
            WeaponDef ranged = CreatureBuild.Clone(repo, c, donorWeapon, "RangedWeaponDef");
            // ACCURACY IS A CONE WIDTH, NOT A TO-HIT ROLL - and it was NOT what made the spit miss.
            // This block once claimed a ranged shot "rolls against Accuracy while a bash does not",
            // offered as the reason four spits produced four MissedTargetVoice events. FALSIFIED, by
            // the game's own arithmetic:
            //
            //   Weapon.GetWeaponSpread:322-325
            //     if (actorAccuracyMultiplier != 0f) num *= 1f / (1f + actorAccuracyMultiplier);
            //
            // Accuracy 0 SKIPS that branch, leaving the cone at WeaponDef.SpreadDegrees. Accuracy can
            // only ever NARROW the cone; it cannot widen one, and there is no roll anywhere in the
            // path. PROVEN in the gate: with this creature's accuracy still 0,00 the spit landed for
            // real damage (C1-spit, Health 130 -> 120). The miss was the SHOT ORIGIN - an unscaled
            // CharacterTargetDummy reporting the muzzle 125x too far out, fixed at the seam described
            // in CreatureBuild.CreatureRigIsScaled.
            //
            // The knob is KEPT because it is still the only route to accuracy - a bodypart's aspect.
            // CharacterStats.cs:26 declares it as a BaseStat starting at zero, and
            // SetBaseCharacterStatsBaseValues carries only Endurance/Willpower/Speed, so the base stats
            // this engine sets cannot reach it. MEASURED: both donors' aspects declare Accuracy = 0,
            // so an author who wants a tighter cone than SpreadDegrees has to say so. It is a tuning
            // knob, not a repair. The aspect is CLONED before it is written to - the donor still uses it.
            if (c.Man.Accuracy > 0f && ranged.BodyPartAspectDef != null)
            {
                BodyPartAspectDef aspect = CreatureBuild.Clone(repo, c, ranged.BodyPartAspectDef,
                                                               "RangedAspectDef");
                aspect.Accuracy = c.Man.Accuracy;
                ranged.BodyPartAspectDef = aspect;
            }
            c.Say("ct_creature PASS ranged accuracy " +
                  (c.Man.Accuracy > 0f
                      ? c.Man.Accuracy.ToString("F0") + "% on '" + ranged.BodyPartAspectDef.name +
                        "' - this NARROWS the spread cone (Weapon.GetWeaponSpread:322-325); it is not " +
                        "a to-hit roll and the shot lands without it"
                      : "not set, so the cone stays at the donor's own WeaponDef.SpreadDegrees. " +
                        "Accuracy 0 skips the narrowing branch entirely - it does NOT widen anything, " +
                        "and the spit lands on target at 0 (measured). Set \"accuracy\" only to tighten."));
            ranged.SubAddons = new AddonDef.SubaddonBind[0];
            ranged.Tags = CreatureBuild.Purge(donorWeapon.Tags, donorTag, shared.VehicleTag);
            ranged.RequiredSlotBinds = new[] { new AddonDef.RequiredSlotBind
                { RequiredSlot = chassis.ProvidedSlots[slot].ProvidedSlot } };
            // The muzzle and aim-point child names are READ off the def, never typed: the origin is
            // whatever the payload's ProjectileOrigin says (ShootOriginsCache.cs:181 looks THAT name
            // up), the aim point whatever TacticalItem.SetupAimPoint:704 will ask for. Empty fields
            // get the engine defaults written BACK so the prefab and the def cannot disagree.
            if (string.IsNullOrEmpty(ranged.DamagePayload.ProjectileOrigin))
                ranged.DamagePayload.ProjectileOrigin = "EXT_ShootPoint";
            if (string.IsNullOrEmpty(ranged.AimPoint)) ranged.AimPoint = "EXT_AimPoint";

            // THE MUZZLE PREFAB: an inactive holder (Instantiate copies activeSelf, so the TEMPLATE
            // never renders while every INSTANCE is born active = IsVisible), a root, two named
            // children. No renderer anywhere - IsVisible (Addon.cs:199-201) asks only for an active
            // GameObject, and that economy is the whole reason this can be synthesised at all.
            ranged.SkinData = SynthSkin(repo, c, "SpitMuzzle", "SpitSkinDataDef",
                                        ranged.DamagePayload.ProjectileOrigin, ranged.AimPoint);

            // ^ see SynthSkin: an inactive holder, a root, one named empty child per transform the
            //   engine will look up by name. No renderer anywhere.

            // WHERE THE SPIT LEAVES FROM: the slot's attachment point. Addon.AttachVisuals:1059,1079
            // parents the instantiated visuals at GetAttachTransform, which is
            // manager.FindTransform(_parentSlot.AttachmentPointName) (Addon.cs:1186-1200) - i.e. the
            // BONE the chassis' ProvidedSlotBind names. So naming our bone on the CLONED chassis'
            // bind is the entire "parent it under the mouth" mechanism, and ResetTransform:1080 then
            // zeroes the prefab onto that bone exactly.
            chassis.ProvidedSlots[slot].AttachmentPointName = bone.name;

            bool fits = chassis.ProvidesCompatibleSlotFor(ranged);
            if (!unit.Data.BodypartItems.Contains(ranged))
                unit.Data.BodypartItems = unit.Data.BodypartItems.Concat(new ItemDef[] { ranged }).ToArray();
            c.Say("ct_creature " + (fits ? "PASS" : "FAIL") + " ranged '" + donorWeapon.name + "' -> '" +
                  ranged.name + "' (delivery " + ranged.DamagePayload.DamageDeliveryType +
                  ", own muzzle SkinData, " + donorWeapon.SubAddons.Length + " sub-addon(s) dropped) " +
                  "on provided slot [" + slot + "] '" + chassis.ProvidedSlots[slot].ProvidedSlot.name +
                  "' @ bone '" + bone.name + "'; the chassis " + (fits ? "PROVIDES" : "REFUSES") +
                  " its slot" + (fits ? "" : " <- CharacterBodyState.SetupBodyParts:89-97 will drop " +
                  "it and the creature will never shoot"));

            // THE ANIMATION: ours listed beside the donor's wherever a filter names it (prints its
            // own PASS/FAIL), then - if the manifest maps a "ranged" clip - a clone filtered to ONLY
            // our weapon, inserted BEFORE the general action because match order decides.
            CreatureBuild.AlsoAccept(repo, c, anims, donorItem, ranged);
            SpitClip(repo, c, anims, general, ranged);

            // THE AI: clone the template AND the actor def (both shared with the whole shipped
            // family - appending in place would teach every Swarmer on the map to spit), append the
            // named action, swap the clone into the component set the same way BuildOrThrow swaps
            // navClone/viewClone/baseClone.
            AIActionsTemplateDef tmpl = CreatureBuild.Clone(repo, c, donorAI.AIActionsTemplateDef,
                                                            "AIActionsTemplateDef");
            if (!(tmpl.ActionDefs ?? new AIActionDef[0]).Contains(aiAction))
                tmpl.ActionDefs = (tmpl.ActionDefs ?? new AIActionDef[0])
                    .Concat(new[] { aiAction }).ToArray();
            TacAIActorDef ai = CreatureBuild.Clone(repo, c, donorAI, "TacAIActorDef");
            ai.AIActionsTemplateDef = tmpl;
            setClone.Components = setClone.Components
                .Select(x => x == donorAI ? (ObjectDef)ai : x).ToArray();
            c.Say("ct_creature " + (setClone.Components.Contains(ai) ? "PASS" : "FAIL") +
                  " AI '" + donorAI.name + "' -> '" + ai.name + "': template '" + tmpl.name +
                  "' now holds " + tmpl.ActionDefs.Length + " action(s) incl. '" + aiAction.name +
                  "' - without it the stock AI carries the spitter and never fires " +
                  "(Swarmer_AIActionsTemplateDef ships NO ranged action)");
        }

        /// <summary>
        /// THE BONE THE MUZZLE HANGS ON - declared in the manifest, or MEASURED: the bone whose rest
        /// position sits furthest along the model's forward axis, which for a creature is the mouth
        /// end. Measured in the rig root's local space rotated by the same up-correction
        /// <see cref="CreatureBuild.Orient"/> applies, so "forward" here is the same +Z the game
        /// will face at a target.
        /// ponytail: furthest-forward is a naive heuristic (a long tail curled forward would win);
        /// the "shootBone" manifest key is the override for exactly that model.
        /// </summary>
        private static Transform ShootBone(Creature c, GameObject rig)
        {
            if (c.Man.ShootBone.Length > 0)
            {
                Transform named = CreatureBuild.FindDeep(rig.transform, c.Man.ShootBone);
                c.Say("ct_creature " + (named != null ? "PASS" : "FAIL") + " shoot bone '" +
                      c.Man.ShootBone + "' " + (named != null
                          ? "(declared in ppcontent.json \"shootBone\")"
                          : "is declared in ppcontent.json \"shootBone\" but no transform in the rig " +
                            "has that name - fix the spelling or clear the key to let the engine " +
                            "measure. Nothing ranged was wired."));
                return named;
            }
            SkinnedMeshRenderer skin = rig.GetComponentInChildren<SkinnedMeshRenderer>(true);
            Transform[] bones = skin == null ? null : skin.bones;
            if (bones == null || bones.Length == 0)
            {
                c.Say("ct_creature FAIL shoot bone: the rig has no skinned bones to measure and the " +
                      "manifest declares no \"shootBone\" - nothing ranged was wired.");
                return null;
            }
            Quaternion up = Quaternion.FromToRotation(c.Up, Vector3.up);
            Transform best = null;
            float front = float.NegativeInfinity;
            foreach (Transform b in bones)
            {
                if (b == null) continue;
                float z = (up * rig.transform.InverseTransformPoint(b.position)).z;
                if (z > front) { front = z; best = b; }
            }
            c.Say("ct_creature PASS shoot bone '" + best.name + "' (MEASURED: rest position furthest " +
                  "along +Z of " + bones.Length + " bones, z=" + front.ToString("F3") +
                  " in up-corrected root space; declare \"shootBone\" in ppcontent.json to override)");
            return best;
        }

        /// <summary>
        /// A DISTINCT clip for the spit, when the manifest maps the optional "ranged" role. Without
        /// one this does nothing and the general shoot action - which WireClips already pointed at
        /// the creature's own clips and AlsoAccept just taught to accept the weapon - covers the
        /// shot with the attack clip.
        /// </summary>
        private static void SpitClip(DefRepository repo, Creature c, TacActorAnimActionsDef anims,
                                     TacActorShootAnimActionDef general, WeaponDef ranged)
        {
            AnimationClip clip = c.OurClip("ranged");
            if (clip == null)
            {
                c.Say("ct_creature PASS ranged clip: none mapped (optional role \"ranged\") - the " +
                      "general shoot action '" + general.name + "' plays the attack clip instead");
                return;
            }
            TacActorShootAnimActionDef spit =
                CreatureBuild.Clone(repo, c, general, "SpitShootAnimActionDef");
            // Filtered to ONLY our weapon: the general action's lists (post-AlsoAccept) also name the
            // donor's items and our melee, and a clone that still matched them would steal THEIR
            // shots too. Bodyparts empties rather than nulls - BodypartsMatch does .Length with no
            // null check and an empty list means "no bodypart constraint".
            spit.Equipments = new EquipmentDef[] { ranged };
            spit.EquipmentList = null;
            spit.Bodyparts = new TacticalItemDef[0];
            spit.IsDefaultAnimatorClips = false;         // the clone must never masquerade as the key set
            int slots = 0;
            foreach (string slot in CreatureBuild.Slots(spit).ToArray())
                if (CreatureBuild.GetSlot(spit, slot) != null &&
                    CreatureBuild.SetSlot(spit, slot, clip)) slots++;

            // INSERT BEFORE the general action - GetAnimAction returns the FIRST match, so a clone
            // sitting after it would never be reached by our weapon's ShootContext.
            List<TacActorAnimActionBaseDef> list = anims.AnimActions.ToList();
            if (!list.Contains(spit))
            {
                list.Insert(list.IndexOf(general), spit);
                anims.AnimActions = list.ToArray();
            }
            c.Say("ct_creature " + (slots > 0 && spit.ShootPose == clip ? "PASS" : "FAIL") +
                  " ranged clip '" + clip.name + "' on '" + spit.name + "': " + slots +
                  " filled slot(s) rewritten (ShootPose " +
                  (spit.ShootPose == clip ? "included" : "MISSED - GetShotOrigin will NRE") +
                  "), inserted BEFORE '" + general.name + "' because match order decides");
        }
    }
}
