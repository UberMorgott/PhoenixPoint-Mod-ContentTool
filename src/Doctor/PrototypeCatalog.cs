using System;
using System.Collections.Generic;

namespace Morgott.ContentTool.Doctor
{
    /// <summary>One transform under a rig prefab, exactly as the census recorded it.</summary>
    internal sealed class PrototypeBone
    {
        internal string Name;      // Transform.name, case-sensitive - the game matches on it exactly
        internal string Parent;    // parent's Name, or null for the prefab root
        internal string Path;      // '/'-joined path from the root, the ONLY way to tell duplicates apart
    }

    /// <summary>A rig prefab and everything under it. The unit the picker is really built on.</summary>
    internal sealed class RigScan
    {
        internal string RigName;
        internal List<PrototypeBone> Bones = new List<PrototypeBone>();
        internal List<string> Managers = new List<string>();   // AddonsManagerDef names using this prefab
    }

    // These two are pure scan DTOs: the game-side harvester fills them by name from DefRepository,
    // so "never assigned in this assembly" is the normal state, not a bug.
#pragma warning disable 649

    /// <summary>One AddonsManagerDef, flattened. HasRig false =&gt; not a picker entry at all.</summary>
    internal sealed class ManagerScan
    {
        internal string ManagerName, RigName, RootMotionNode, ResourcePath;
        internal string RepresentativeCharacter;      // a TacCharacterDef name - what the bay rebuild needs
        internal string BodyStateDef, AnimActionsDef, ControllerName;
        internal List<string> SlotNames = new List<string>();
        internal List<string> ClipNames = new List<string>();  // already deduplicated by the harvester
        internal bool HasRig;
    }

    internal sealed class PrototypeSlot
    {
        internal string SlotDefName, AttachmentPointName;
        internal List<string> RepresentativeAddons = new List<string>();
    }

#pragma warning restore 649

    internal sealed class PrototypeVariant
    {
        internal string Name, ManagerName, RepresentativeCharacter, BodyStateDef;
        internal string AnimActionsDef, ControllerName;
        internal List<PrototypeSlot> Slots = new List<PrototypeSlot>();
        /// <summary>Resolved clip catalogue: the anim-actions def's clips, or - when that is EMPTY,
        /// which is the shipped state of Crabman_AnimActionsDef - the controller's own
        /// animationClips, deduplicated by name.</summary>
        internal List<string> Clips = new List<string>();
    }

    internal sealed class PrototypeRecord
    {
        internal string Id, DisplayName, Category;
        internal List<string> SearchTerms = new List<string>();
        internal List<string> RigPrefabNames = new List<string>();   // 2 only for the worm prototype
        internal List<PrototypeBone> Bones = new List<PrototypeBone>();
        internal List<string> BindableBones = new List<string>();
        internal List<string> AttachmentPoints = new List<string>();
        internal List<string> AmbiguousNames = new List<string>();
        internal List<PrototypeVariant> Variants = new List<PrototypeVariant>();
        internal string Warning;    // set when AmbiguousNames is non-empty; never blocks by itself
    }

    /// <summary>
    /// WHAT THE DOCTOR VERIFIES AGAINST, decided off bone names and nothing else. Carries no
    /// UnityEngine type on purpose: the whole merge rule is provable offline against the live census
    /// (<c>internal-docs\research\rig-census-2026-09-02.json</c>, 37 rigs / 2551 transforms), so
    /// "these two creatures take the same mesh" never has to be guessed from a family name. Measured:
    /// Crabman and Oilcrab share 34 names across DIFFERENT prefabs, and a Crabman mesh still binds
    /// partially and silently on an Oilcrab.
    /// </summary>
    internal static class PrototypeCatalog
    {
        /// <summary>Addon.GetEquivalentBones skips every transform whose name starts with this
        /// (Addon.cs:1208), so it is the line between what can bind and what cannot.</summary>
        internal const string AttachmentPrefix = "EXT_";

        internal static bool IsAttachmentPoint(string boneName)
        {
            return boneName != null && boneName.StartsWith(AttachmentPrefix, StringComparison.Ordinal);
        }

        /// <summary>Bone names that can actually take a skin weight, ORDINAL-sorted and deduplicated.</summary>
        internal static IList<string> Bindable(IList<PrototypeBone> bones)
        {
            return Partition(bones, false);
        }

        /// <summary>The EXT_* names. Informational on the Extend path, REQUIRED on the Replace path
        /// when the live SMR references one.</summary>
        internal static IList<string> AttachmentPoints(IList<PrototypeBone> bones)
        {
            return Partition(bones, true);
        }

        private static IList<string> Partition(IList<PrototypeBone> bones, bool attachments)
        {
            var names = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < bones.Count; i++)
            {
                string name = bones[i] == null ? null : bones[i].Name;
                if (name == null || IsAttachmentPoint(name) != attachments) continue;
                if (seen.Add(name)) names.Add(name);
            }
            names.Sort(StringComparer.Ordinal);
            return names;
        }

        /// <summary>Names appearing more than once anywhere under the rig. The game resolves by name
        /// plus FirstOrDefault, so the second one is unreachable and must never be index-matched.</summary>
        internal static IList<string> Ambiguous(IList<PrototypeBone> bones)
        {
            var seen = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int i = 0; i < bones.Count; i++)
            {
                string name = bones[i] == null ? null : bones[i].Name;
                if (name == null) continue;
                int n;
                seen[name] = seen.TryGetValue(name, out n) ? n + 1 : 1;
            }
            var duplicated = new List<string>();
            foreach (KeyValuePair<string, int> pair in seen) if (pair.Value > 1) duplicated.Add(pair.Key);
            duplicated.Sort(StringComparer.Ordinal);
            return duplicated;
        }

        /// <summary>THE MERGE KEY: the ordinal-sorted Bindable() set, '\n'-joined. Two rigs are one
        /// prototype if and only if this string is equal. Not the prefab, not the slots, not the
        /// animation - slice 0 measured all three lying in both directions.
        ///
        /// The prefab ROOT is left out. It is the asset's own name, Unity renames it on Instantiate,
        /// and nothing skins to it - which is the entire difference between ALN_Fireworm_Rig_Ready and
        /// ALN_Acidworm_Rig_Ready, two prefabs carrying one 13-name bone set. It stays IN Bindable(),
        /// because that set is the Extend target and has to account for every censused transform.</summary>
        internal static string Signature(IList<PrototypeBone> bones)
        {
            var inner = new List<PrototypeBone>();
            for (int i = 0; i < bones.Count; i++)
                if (bones[i] != null && bones[i].Parent != null) inner.Add(bones[i]);
            return string.Join("\n", Bindable(inner));
        }

        /// <summary>Group rigs by Signature, attach every manager that uses one of the grouped
        /// prefabs as a VARIANT, and drop managers with HasRig == false.</summary>
        internal static IList<PrototypeRecord> Build(IList<RigScan> rigs, IList<ManagerScan> managers)
        {
            var records = new List<PrototypeRecord>();
            var bySignature = new Dictionary<string, PrototypeRecord>(StringComparer.Ordinal);
            var byRig = new Dictionary<string, PrototypeRecord>(StringComparer.Ordinal);

            for (int i = 0; rigs != null && i < rigs.Count; i++)
            {
                RigScan rig = rigs[i];
                if (rig == null || rig.RigName == null) continue;
                string signature = Signature(rig.Bones);
                PrototypeRecord record;
                if (!bySignature.TryGetValue(signature, out record))
                {
                    record = new PrototypeRecord { Id = rig.RigName };
                    record.Bones.AddRange(rig.Bones);
                    record.BindableBones.AddRange(Bindable(rig.Bones));
                    record.AttachmentPoints.AddRange(AttachmentPoints(rig.Bones));
                    record.AmbiguousNames.AddRange(Ambiguous(rig.Bones));
                    if (record.AmbiguousNames.Count > 0)
                        record.Warning = "this rig carries the name(s) " +
                                         string.Join(", ", record.AmbiguousNames) +
                                         " more than once; the game matches by name and takes the first, so " +
                                         "the others are unreachable - a slot is only blocked when it really " +
                                         "references one of them";
                    bySignature[Signature(rig.Bones)] = record;
                    records.Add(record);
                }
                record.RigPrefabNames.Add(rig.RigName);
                byRig[rig.RigName] = record;
            }

            for (int i = 0; managers != null && i < managers.Count; i++)
            {
                ManagerScan manager = managers[i];
                PrototypeRecord record;
                if (manager == null || !manager.HasRig || manager.RigName == null ||
                    !byRig.TryGetValue(manager.RigName, out record)) continue;
                var variant = new PrototypeVariant
                {
                    Name = VariantName(manager.ManagerName),
                    ManagerName = manager.ManagerName,
                    RepresentativeCharacter = manager.RepresentativeCharacter,
                    BodyStateDef = manager.BodyStateDef,
                    AnimActionsDef = manager.AnimActionsDef,
                    ControllerName = manager.ControllerName
                };
                foreach (string slot in manager.SlotNames)
                    variant.Slots.Add(new PrototypeSlot { SlotDefName = slot });
                variant.Clips.AddRange(manager.ClipNames);
                record.Variants.Add(variant);
                if (record.Category == null) record.Category = CategoryOf(manager.ResourcePath, manager.ManagerName);
            }

            foreach (PrototypeRecord record in records)
            {
                record.DisplayName = record.Variants.Count > 0 ? record.Variants[0].Name : VariantName(record.Id);
                if (record.Category == null) record.Category = CategoryOf(null, record.Id);
                Term(record, record.DisplayName);
                Term(record, record.Category);
                foreach (string prefab in record.RigPrefabNames) Term(record, prefab);
                foreach (PrototypeVariant variant in record.Variants)
                {
                    Term(record, variant.Name);
                    Term(record, variant.ManagerName);
                    Term(record, variant.RepresentativeCharacter);
                    foreach (PrototypeSlot slot in variant.Slots) Term(record, slot.SlotDefName);
                }
            }
            return records;
        }

        /// <summary>Case-insensitive token-AND over SearchTerms: every whitespace-delimited token must
        /// match somewhere. Empty query returns everything.</summary>
        internal static IList<PrototypeRecord> Search(IList<PrototypeRecord> all, string query)
        {
            var hits = new List<PrototypeRecord>();
            string[] tokens = (query ?? string.Empty).Split(new[] { ' ', '\t', '\r', '\n' },
                                                            StringSplitOptions.RemoveEmptyEntries);
            foreach (PrototypeRecord record in all)
            {
                // ponytail: one joined haystack per record per keystroke. 36 records of a few hundred
                // characters - if a search ever feels slow, cache the join on the record instead.
                string hay = string.Join("\n", record.SearchTerms);
                bool every = true;
                foreach (string token in tokens)
                    if (hay.IndexOf(token, StringComparison.OrdinalIgnoreCase) < 0) { every = false; break; }
                if (every) hits.Add(record);
            }
            return hits;
        }

        /// <summary>The 8 navigation groups, keyed off ResourcePath's faction folder plus the manager
        /// name. Navigation only - it never merges or splits anything.</summary>
        internal static string CategoryOf(string resourcePath, string managerName)
        {
            string path = resourcePath ?? string.Empty, name = managerName ?? string.Empty;
            if (In(name, "Turret") || In(name, "Drone")) return "Turrets & drones";
            if (In(path, "AncientGuardians") || In(name, "Guardian")) return "Ancients";
            if (In(path, "/Vehicles/") || In(name, "Armadillo") || In(name, "Scarab") ||
                In(name, "Aspida") || In(name, "Buggy")) return "Vehicles";
            if (In(name, "Human") || In(name, "Exalted")) return "Human & Anu";
            // Structures BEFORE worms and bipeds on purpose: EggFireWorm and SpawningPoolCrabman are
            // scenery, not the creature whose name they borrow.
            if (In(name, "Egg") || In(name, "SpawningPool") || In(name, "Sentinel") ||
                In(name, "CorruptionNode") || In(name, "Injector")) return "Pandoran structures";
            if (In(name, "worm") || In(name, "Swarmer") || In(name, "Yugothian") ||
                In(name, "Yuggothian")) return "Worms & small creatures";
            if (In(name, "Crabman") || In(name, "Fishman") || In(name, "Oilfish") ||
                In(name, "Siren") || In(name, "Queen")) return "Pandoran humanoids";
            return "Pandoran beasts";
        }

        private static bool In(string text, string part)
        {
            return text.IndexOf(part, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static string VariantName(string managerName)
        {
            if (managerName == null) return null;
            const string suffix = "_AddonsManagerDef";
            return managerName.EndsWith(suffix, StringComparison.Ordinal)
                ? managerName.Substring(0, managerName.Length - suffix.Length)
                : managerName;
        }

        private static void Term(PrototypeRecord record, string value)
        {
            if (!string.IsNullOrEmpty(value) && !record.SearchTerms.Contains(value))
                record.SearchTerms.Add(value);
        }
    }
}
