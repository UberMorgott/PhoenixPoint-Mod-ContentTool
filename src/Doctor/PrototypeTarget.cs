using System;
using System.Collections.Generic;
using Morgott.ContentTool.Import;

namespace Morgott.ContentTool.Doctor
{
    /// <summary>How the file is held against the rig. The two modes answer two different questions,
    /// and the wrong one is either a false refusal or a silent partial bind.</summary>
    internal enum VerifyMode
    {
        /// <summary>Exact: the file must reproduce one LIVE slot SkinnedMeshRenderer's bone list.</summary>
        Replace,
        /// <summary>Subset: the file's joints must map uniquely onto the prototype's BindableBones.</summary>
        Extend
    }

    /// <summary>
    /// WHAT THE DOCTOR VERIFIES AGAINST - one prototype, one variant, one slot, one mode.
    ///
    /// Replace is answered by the LIVE renderer and by nothing else: <c>smr.bones</c> is a small
    /// subset of the rig (slice 0 measured a Human head slot at 21 bones against the rig's 124), so a
    /// target fabricated from the full hierarchy would predict a binding the game never performs.
    /// Extend is answered by the prototype's <see cref="PrototypeRecord.BindableBones"/>, because a
    /// partial body part is legitimate and the game skips every EXT_ transform anyway
    /// (Addon.cs:1208).
    /// </summary>
    // A selection DTO: the browser and the bay session (Unity side) fill these by name, so
    // "never assigned in this assembly" is the normal state - same arrangement as ManagerScan.
#pragma warning disable 649
    internal sealed class PrototypeTarget
    {
        internal PrototypeRecord Record;
        internal PrototypeVariant Variant;
        internal string SlotDefName;
        internal VerifyMode Mode;

        /// <summary>Replace ONLY: the snapshot of the live slot renderer the bay rebuild produced.
        /// Null on the Extend path, and null when the slot has no renderer.</summary>
        internal RigTarget Live;

        /// <summary>Non-null when this slot produced no renderer - the row reads
        /// "slot visual unavailable" and Replace is refused for it. Extend still works.</summary>
        internal string Unavailable;

        /// <summary>The SHIPPED pair this slot's renderer replaces, derived by
        /// <see cref="ShippedTarget.Resolve"/> when the bay rebuild produced the renderer: the .bundle FILE
        /// name as BakeSelfCheck.ShippedBundlePath resolves it, and the Mesh's ordinal m_Name. Null until it
        /// is derived, and null forever when it could not be - see <see cref="TargetRefusal"/>.</summary>
        internal string ShippedBundle;
        internal string ShippedAsset;

        /// <summary>Why no shipped pair could be derived for this slot - the sentence the panel shows in place
        /// of a target. Stored rather than thrown: one unresolvable slot must not cost the author the other
        /// slots' rows.</summary>
        internal string TargetRefusal;

        /// <summary>The names Analyze is run against: Live.BoneNames on Replace,
        /// Record.BindableBones on Extend. Null rather than a guess when there is no live renderer.</summary>
        internal IList<string> BoneNames()
        {
            if (Mode == VerifyMode.Extend) return Record == null ? null : Record.BindableBones;
            return Live == null ? null : Live.BoneNames;
        }

        /// <summary>THE DUPLICATE RULE. Returns the ambiguous names that the given referenced set
        /// actually touches - and ONLY those block a verdict. An ambiguous name nothing references is
        /// a Record.Warning, so the vehicles' duplicated 'light' nodes never make unrelated slots
        /// unusable. NEVER disambiguate by index: the game resolves by name plus FirstOrDefault
        /// (Addon.cs:1202-1231), so the second one is unreachable.</summary>
        internal IList<string> BlockingAmbiguous(IList<string> referenced)
        {
            var blocking = new List<string>();
            if (Record == null || referenced == null) return blocking;
            var names = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < referenced.Count; i++)
                if (referenced[i] != null) names.Add(referenced[i]);
            foreach (string ambiguous in Record.AmbiguousNames)
                if (names.Contains(ambiguous) && !blocking.Contains(ambiguous)) blocking.Add(ambiguous);
            return blocking;
        }
    }
#pragma warning restore 649
}
