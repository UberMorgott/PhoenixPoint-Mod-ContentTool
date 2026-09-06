using System;
using System.Collections.Generic;
using Base.Core;
using Base.Defs;
using Morgott.ContentTool.Doctor;
using PhoenixPoint.Common.Core;
using PhoenixPoint.Common.Entities;
using PhoenixPoint.Common.Entities.Addons;
using PhoenixPoint.Common.Entities.Characters;
using PhoenixPoint.Tactical.Entities;
using PhoenixPoint.Tactical.Entities.Animations;
using PhoenixPoint.Tactical.Entities.Equipments;
using UnityEngine;

namespace Morgott.ContentTool.Dev
{
    /// <summary>
    /// ============ THE CATALOGUE, READ OFF THE LIVE DEF REPOSITORY ============
    ///
    /// The Unity half of the prototype catalog: it fills the UnityEngine-free scan DTOs
    /// (<see cref="RigScan"/>, <see cref="ManagerScan"/>) from <c>DefRepository</c> and hands them to
    /// <see cref="PrototypeCatalog.Build"/>, which owns the merge rule and is proven offline against
    /// <c>internal-docs\research\rig-census-2026-09-02.json</c>. Nothing here decides which rigs are
    /// one prototype - that decision must stay in the half a test can reach.
    ///
    /// NOTHING IS INSTANTIATED. The bone names come off the rig PREFAB's own transform hierarchy -
    /// <c>Rig.GetComponentsInChildren&lt;Transform&gt;(true)</c>, which is a DIRECT GameObject
    /// reference on the def (AddonsManagerDef.cs:12) and not an Addressable. That is the same call the
    /// slice 0 census made through PPCLI, so the numbers this logs are directly comparable with the
    /// fixture; and because it allocates no GameObject there is nothing to destroy and nothing to leak
    /// if it throws half way.
    ///
    /// It runs LAZILY, on the first browser open, and never at menu time: a campaign's and a content
    /// mod's defs have to have settled before "every shipped rig" means anything.
    /// </summary>
    internal sealed class PrototypeHarvest
    {
        /// <summary>What the census measured on 2026-09-02, so the log line below is an ASSERTION and
        /// not a number nobody reads. A modded install legitimately differs (this one carries
        /// ContentTool's own two creatures); a SHIPPED-only install that differs means the merge rule
        /// is being fed something the fixture never saw.</summary>
        private const int CensusManagers = 46, CensusRigged = 42, CensusRigs = 37,
                          CensusTransforms = 2551, CensusPrototypes = 36;

        internal IList<PrototypeRecord> Records = new List<PrototypeRecord>();
        /// <summary>Representative <c>TacCharacterDef</c> by def name - the object
        /// <c>UnitDisplayData</c> needs, which the name-only scan DTOs cannot carry.</summary>
        private readonly Dictionary<string, TacCharacterDef> representatives =
            new Dictionary<string, TacCharacterDef>(StringComparer.Ordinal);
        /// <summary>The live clips behind <see cref="ManagerScan.ClipNames"/>, by REPRESENTATIVE character
        /// name - what the transport binds when a variant's own anim actions catalogue nothing. They live
        /// HERE and not on <see cref="PrototypeVariant"/>, which stays UnityEngine-free, exactly as the
        /// representative <c>TacCharacterDef</c>s already do.
        ///
        /// Keyed on the representative and NOT on the manager since gap 2: one manager now mints one
        /// variant per armour set, the anim actions def is the CHARACTER's, and a manager key would let
        /// the last variant scanned overwrite the clips of every other one. A TacCharacterDef points at
        /// exactly one AddonsManagerDef, so a representative name is unique across the whole scan.</summary>
        private readonly Dictionary<string, AnimationClip[]> clipsByCharacter =
            new Dictionary<string, AnimationClip[]>(StringComparer.Ordinal);
        /// <summary>The one line Task 7 diffs against the fixture. Always set, even on failure.</summary>
        internal string Census = "";

        internal TacCharacterDef Representative(string defName)
        {
            TacCharacterDef d;
            return defName != null && representatives.TryGetValue(defName, out d) ? d : null;
        }

        /// <summary>The resolved clip objects for one variant, in the order its ClipNames list carries
        /// them, keyed on the variant's REPRESENTATIVE character. Null when it was never scanned -
        /// shaped exactly like Representative.</summary>
        internal AnimationClip[] Clips(string characterName)
        {
            AnimationClip[] clips;
            return characterName != null && clipsByCharacter.TryGetValue(characterName, out clips) ? clips : null;
        }

        /// <summary>Never throws: this is called from a GUI loop and its result is cached even when it
        /// fails, because a scan that throws once per frame is worse than an empty catalogue.</summary>
        internal static PrototypeHarvest Scan()
        {
            var harvest = new PrototypeHarvest();
            try { harvest.Read(); }
            catch (Exception ex)
            {
                harvest.Census = "ct_bench prototypes: the catalogue could NOT be read - " +
                                 ex.GetType().Name + ": " + ex.Message;
            }
            return harvest;
        }

        private void Read()
        {
            DefRepository repo = GameUtl.GameComponent<DefRepository>();

            // Manager -> every character def pointing at it, with the template bodypart names
            // PrototypeCatalog.Representatives keys the variant on. CHEAP on purpose:
            // GetTemplateBodyparts reads the def's own arrays, while the per-representative reads
            // below (slots, clips, body state) touch live Unity objects and run only for survivors.
            var candidates = new Dictionary<AddonsManagerDef, IDictionary<string, IList<string>>>();
            var defsByName = new Dictionary<string, TacCharacterDef>(StringComparer.Ordinal);
            var partsByDef = new Dictionary<string, List<TacticalItemDef>>(StringComparer.Ordinal);
            foreach (TacCharacterDef d in repo.GetAllDefs<TacCharacterDef>())
            {
                if (d == null || d.name == null) continue;
                AddonsManagerDef m = null;
                try { m = d.GetAddonsMangerDef(); } catch (Exception) { }
                if (m == null) continue;
                IDictionary<string, IList<string>> mine;
                if (!candidates.TryGetValue(m, out mine))
                    candidates[m] = mine = new Dictionary<string, IList<string>>(StringComparer.Ordinal);
                bool failed;
                List<TacticalItemDef> parts = Bodyparts(d, out failed);
                mine[d.name] = BodypartNames(parts, failed, d.name);
                partsByDef[d.name] = parts;
                defsByName[d.name] = d;
            }

            var rigs = new List<RigScan>();
            var byRigName = new Dictionary<string, RigScan>(StringComparer.Ordinal);
            var managers = new List<ManagerScan>();
            int managerCount = 0, rigged = 0, transforms = 0;

            foreach (AddonsManagerDef m in repo.GetAllDefs<AddonsManagerDef>())
            {
                if (m == null) continue;
                // THE CENSUS COUNTS MANAGERS, and one manager is now several scans. Counting the scan
                // list instead would make the assertion against rig-census-2026-09-02.json read as a
                // DIFFERS the moment gap 2 was closed.
                managerCount++;

                // Once per MANAGER, never once per representative: the rig prefab, its transforms and
                // the manager list on it are the manager's, and Build merges rigs by their bones.
                RigScan rig = null;
                if (m.Rig != null)
                {
                    rigged++;
                    if (!byRigName.TryGetValue(m.Rig.name, out rig))
                    {
                        rig = ScanRig(m.Rig);
                        byRigName[m.Rig.name] = rig;
                        rigs.Add(rig);
                        transforms += rig.Bones.Count;
                    }
                    rig.Managers.Add(m.name);
                }

                // ALSO once per MANAGER: the animator, its controller and the clips behind it belong to
                // the manager's rig, not to a representative - and Unity REBUILDS the array every time
                // `animationClips` is read. Per-representative, the 166-variant Human manager paid for
                // that rebuild 166 times, even in the runs where the anim-actions arm wins.
                RuntimeAnimatorController controller = null;
                if (m.Rig != null)
                    try
                    {
                        Animator animator = m.Rig.GetComponent<Animator>();
                        controller = animator == null ? null : animator.runtimeAnimatorController;
                    }
                    catch (Exception) { }
                var fromController = new List<AnimationClip>();
                try { if (controller != null) fromController.AddRange(controller.animationClips); }
                catch (Exception) { }
                IList<string> controllerNames = Names(fromController);

                IDictionary<string, IList<string>> wearers;
                candidates.TryGetValue(m, out wearers);
                IList<string> reps = PrototypeCatalog.Representatives(wearers);
                // A manager nothing points at is still exactly ONE scan with no representative -
                // what it was before gap 2, and what keeps the rig-less four out of the picker.
                if (reps.Count == 0) reps = new string[] { null };

                foreach (string repName in reps)
                {
                    TacCharacterDef rep = null;
                    if (repName != null) defsByName.TryGetValue(repName, out rep);
                    var scan = new ManagerScan
                    {
                        ManagerName = m.name,
                        RootMotionNode = m.RootMotionNodeName,
                        ResourcePath = m.ResourcePath,
                        RepresentativeCharacter = rep == null ? null : rep.name,
                        HasRig = rig != null,
                        RigName = rig == null ? null : rig.RigName
                    };
                    managers.Add(scan);
                    if (rep != null) representatives[rep.name] = rep;
                    if (rig == null) continue;   // Dropped / FallDown / ... - nothing to verify against
                    if (rep == null) continue;

                    List<TacticalItemDef> parts;
                    partsByDef.TryGetValue(rep.name, out parts);
                    ReadSlots(parts, scan);
                    ReadClips(rep, m, scan, controller, fromController, controllerNames);
                    CharacterBodyStateDef body = null;
                    try { body = rep.ComponentSetDef.GetComponentDef<CharacterBodyStateDef>(); }
                    catch (Exception) { }
                    scan.BodyStateDef = body == null ? null : body.name;
                }
            }

            Records = PrototypeCatalog.Build(rigs, managers);
            Census = Line(managerCount, rigged, rigs, transforms, Records.Count);
        }

        /// <summary>The template bodyparts of one character def, materialized ONCE - the variant key is
        /// read off them and so are the slots. Never throws: GetTemplateBodyparts Concats several def
        /// arrays (CreatureBuild.cs:366) and one broken def must not cost the catalogue. A read that
        /// throws half way returns what it got and sets <paramref name="failed"/>.</summary>
        private static List<TacticalItemDef> Bodyparts(TacCharacterDef d, out bool failed)
        {
            var parts = new List<TacticalItemDef>();
            failed = false;
            try
            {
                foreach (TacticalItemDef part in d.GetTemplateBodyparts())
                    if (part != null) parts.Add(part);
            }
            catch (Exception) { failed = true; }
            return parts;
        }

        /// <summary>The template bodypart names of one character def - the whole of what makes two defs
        /// on ONE manager different shipped targets. A FAILED read carries a per-def sentinel, so a
        /// partial (or empty) list can never merge with another def's: without it every def whose read
        /// threw collapsed into one bucket together with the genuinely part-less ones, and the armour
        /// sets vanished silently - the very thing gap 2 was closed to stop.</summary>
        private static IList<string> BodypartNames(List<TacticalItemDef> parts, bool failed, string defName)
        {
            var names = new List<string>(parts.Count + 1);
            foreach (TacticalItemDef part in parts) names.Add(part.name);
            if (failed) names.Add(PrototypeCatalog.FailedRead(defName));
            return names;
        }

        /// <summary>Every transform under the rig PREFAB, in the DFS preorder
        /// <c>GetComponentsInChildren</c> returns - the same order the census reconstructed its
        /// hierarchy from, so a path here and a path in the fixture are the same string.</summary>
        private static RigScan ScanRig(GameObject rig)
        {
            var scan = new RigScan { RigName = rig.name };
            var paths = new Dictionary<Transform, string>();
            foreach (Transform t in rig.GetComponentsInChildren<Transform>(true))
            {
                if (t == null) continue;
                Transform parent = t.parent;
                string above = null;
                bool inside = parent != null && paths.TryGetValue(parent, out above);
                string path = inside ? above + "/" + t.name : t.name;
                paths[t] = path;
                scan.Bones.Add(new PrototypeBone
                {
                    Name = t.name,
                    Parent = inside ? parent.name : null,   // null == the prefab root, as in the census
                    Path = path
                });
            }
            return scan;
        }

        /// <summary>The slots this variant's own bodyparts ask for - the same vocabulary
        /// <c>Addon.ParentSlot.SlotDef</c> answers in at rebuild time, so a harvested slot and a live
        /// renderer can be matched by name and never by a path heuristic.
        ///
        /// ponytail: an addon may declare several RequiredSlotBinds and the game takes the first
        /// AVAILABLE one (AddonDef.cs:68), so this can over-list. An over-listed slot simply reads
        /// "slot visual unavailable" - the honest answer - rather than inventing a target.</summary>
        private static void ReadSlots(List<TacticalItemDef> parts, ManagerScan scan)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            try
            {
                for (int i = 0; parts != null && i < parts.Count; i++)
                {
                    TacticalItemDef part = parts[i];
                    if (part == null || part.RequiredSlotBinds == null) continue;
                    foreach (AddonDef.RequiredSlotBind bind in part.RequiredSlotBinds)
                        if (bind.RequiredSlot != null && seen.Add(bind.RequiredSlot.name))
                            scan.SlotNames.Add(bind.RequiredSlot.name);
                }
            }
            catch (Exception) { }
            scan.SlotNames.Sort(StringComparer.Ordinal);
        }

        /// <summary>The two CANDIDATE clip lists, gathered and handed to
        /// <see cref="PrototypeCatalog.ResolveClips"/> - which one wins, and the dedup, are that pure
        /// rule's business and are proven offline. The anim-actions arm is the def's own clips
        /// (<c>GetAllClips</c> is TacActorAnimActionBaseDef's abstract member,
        /// <c>TacActorAnimActionBaseDef.cs:12</c>); the controller arm is the very controller
        /// CommonCharacterUtils.cs:41-42 copies onto the live rig - read ONCE per manager by the caller
        /// and handed in, because a get on <c>animationClips</c> rebuilds the array.</summary>
        private void ReadClips(TacCharacterDef rep, AddonsManagerDef m, ManagerScan scan,
                               RuntimeAnimatorController controller, List<AnimationClip> fromController,
                               IList<string> controllerNames)
        {
            TacActorAnimActionsDef anim = null;
            try { anim = rep.GetAnimActionDef(); } catch (Exception) { }
            scan.AnimActionsDef = anim == null ? null : anim.name;

            scan.ControllerName = controller == null ? null : controller.name;

            var character = m as CharacterAddonsManagerDef;
            AnimationClip pose = character == null ? null : character.PreviewPoseClip;
            scan.PreviewPoseClip = pose == null ? null : pose.name;

            var fromActions = new List<AnimationClip>();
            if (anim != null && anim.AnimActions != null)
            {
                fromActions.Add(anim.DefaultActionClip);
                fromActions.Add(anim.DefaultReactionClip);
                foreach (TacActorAnimActionBaseDef action in anim.AnimActions)
                {
                    if (action == null) continue;
                    AnimationClip[] clips = null;
                    try { clips = action.GetAllClips(); } catch (Exception) { }
                    if (clips != null) fromActions.AddRange(clips);
                }
            }

            PrototypeCatalog.ClipSource source;
            IList<string> names = PrototypeCatalog.ResolveClips(Names(fromActions), controllerNames,
                                                               out source);
            scan.ClipNames.AddRange(names);
            scan.ClipSource = source == PrototypeCatalog.ClipSource.Controller
                                  ? scan.ControllerName + " (controller)"
                                  : source == PrototypeCatalog.ClipSource.AnimActions
                                        ? scan.AnimActionsDef + " (anim actions)"
                                        : null;
            clipsByCharacter[rep.name] =
                Objects(source == PrototypeCatalog.ClipSource.Controller ? fromController : fromActions, names);
        }

        /// <summary>The candidate names, positionally as they came: a null clip is a null name, which
        /// ResolveClips drops exactly as it drops any null entry.</summary>
        private static IList<string> Names(List<AnimationClip> clips)
        {
            var names = new List<string>(clips.Count);
            foreach (AnimationClip clip in clips) names.Add(clip == null ? null : clip.name);
            return names;
        }

        /// <summary>The live clip behind each resolved name, first occurrence winning - the same one the
        /// dedup kept, so ClipNames[i] and Clips(manager)[i] are the same clip.</summary>
        private static AnimationClip[] Objects(List<AnimationClip> clips, IList<string> names)
        {
            var byName = new Dictionary<string, AnimationClip>(StringComparer.Ordinal);
            foreach (AnimationClip clip in clips)
                if (clip != null && !byName.ContainsKey(clip.name)) byName[clip.name] = clip;
            var kept = new List<AnimationClip>(names.Count);
            foreach (string name in names)
            {
                AnimationClip clip;
                if (byName.TryGetValue(name, out clip)) kept.Add(clip);
            }
            return kept.ToArray();
        }

        /// <summary>The line Task 7 holds against the fixture: the five totals with the census's own
        /// numbers beside them, then every rig prefab and its transform count, ordinal-sorted so a
        /// diff against the taxonomy's table is mechanical.</summary>
        private static string Line(int managers, int rigged, List<RigScan> rigs, int transforms, int prototypes)
        {
            var names = new List<string>();
            foreach (RigScan rig in rigs) names.Add(rig.RigName + "=" + rig.Bones.Count);
            names.Sort(StringComparer.Ordinal);
            return "ct_bench prototypes: " + managers + " manager(s) [census " + CensusManagers + "], " +
                   rigged + " with a rig [" + CensusRigged + "], " + rigs.Count + " distinct rig(s) [" +
                   CensusRigs + "], " + transforms + " transform(s) [" + CensusTransforms + "], " +
                   prototypes + " binding prototype(s) [" + CensusPrototypes + "]" +
                   (managers == CensusManagers && rigged == CensusRigged && rigs.Count == CensusRigs &&
                    transforms == CensusTransforms && prototypes == CensusPrototypes
                        ? " - MATCHES rig-census-2026-09-02.json."
                        : " - DIFFERS from rig-census-2026-09-02.json (a different mod set is loaded, " +
                          "or the merge rule is being fed something the fixture never saw).") +
                   "\nrig transforms: " + string.Join("; ", names.ToArray());
        }
    }
}
