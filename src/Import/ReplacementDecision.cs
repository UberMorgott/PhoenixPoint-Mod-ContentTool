namespace Morgott.ContentTool.Import
{
    /// <summary>What a mesh replacement WILL do, in the bake's own words. Four, not three: an
    /// unrigged target is a different sentence from a lost skin, and a skinless source onto a rigged
    /// target is refused outright rather than downgraded.</summary>
    internal enum Outcome
    {
        /// <summary>The file's own weights land on the target's own bones, matched by name.</summary>
        ByName,
        /// <summary>The file imports, but every vertex is welded whole to its nearest bone.</summary>
        NearestBone,
        /// <summary>The target carries no bind poses, so there is nothing to skin to.</summary>
        NotRigged,
        /// <summary>Nothing is written at all.</summary>
        Refused
    }

    /// <summary>
    /// THE ONE DEFINITION OF THE VERDICT. BundleBaker.ReplaceMesh asks it, and so does the Model
    /// Doctor's preflight - not "the same conditions in the same order", the same function, because
    /// two implementations of one rule drift and the author is the one who finds out.
    ///
    /// Pure: no UnityEngine type, no AssetTypeValueField, so the whole table is provable offline
    /// (tests\ObjCodecTests\DecisionGolden.cs).
    /// </summary>
    internal static class ReplacementDecision
    {
        /// <param name="sourceHasArmature">model != null &amp;&amp; model.JointNames.Count &gt; 0 (BundleBaker.cs:153/176).</param>
        /// <param name="targetRigged">the target has bind poses: SkinFields.Rigged (BundleBaker.cs:154) live-side, smr.sharedMesh.bindposes.Length &gt; 0.</param>
        /// <param name="targetBoneNamesAvailable">SkinFields.BoneNames(...) != null (BundleBaker.cs:177) live-side, smr.bones is non-empty.</param>
        /// <param name="firstIssue">
        /// the first thing SkinCompatibility.Analyze found, or null. The Doctor has this up front; the
        /// bake only learns it when RebindByName throws, and re-asks with it from the catch. It is the
        /// ONE input this function cannot compute for itself.
        /// </param>
        internal static Outcome Decide(bool sourceHasArmature, bool targetRigged,
                                       bool targetBoneNamesAvailable, BindingIssue firstIssue)
        {
            if (!sourceHasArmature) return targetRigged ? Outcome.Refused : Outcome.NotRigged;
            if (!targetRigged) return Outcome.NotRigged;
            if (!targetBoneNamesAvailable) return Outcome.NearestBone;
            return firstIssue == null ? Outcome.ByName : Outcome.NearestBone;
        }
    }
}
