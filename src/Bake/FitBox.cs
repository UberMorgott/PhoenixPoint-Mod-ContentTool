using System;
using System.Globalization;

namespace Morgott.ContentTool.Bake
{
    /// <summary>
    /// ============ FITTING A DOWNLOADED MODEL INTO THE BOX THE GAME RESERVED ============
    ///
    /// THE PROBLEM, stated exactly. A weapon is an Addon parented to a named attachment transform on
    /// the soldier's rig (<c>AddonDef.ProvidedSlotBind.AttachmentPointName</c>, Addon.cs:49-53 ->
    /// AddonsManager.cs:120), so a prefab lands in the hand at WHATEVER COORDINATES ITS MESH HAPPENS
    /// TO CARRY. A model downloaded off the internet is modelled in the artist's units, centred on
    /// the artist's origin, pointing down the artist's axis - so it arrives the wrong size, in the
    /// wrong place, facing the wrong way, and none of those are the model being broken.
    ///
    /// THE ANSWER IS MEASURED, NOT TYPED. The weapon this mod CLONED already occupies exactly the
    /// box the game reserves for its class - that is what makes it sit correctly in the hand - so
    /// that box is the specification. Fit the new mesh into the donor's own bounds and the result is
    /// right by construction, for any clone source, with no per-gun magic numbers. This is the same
    /// shape the creature line settled on: measure a default, let the manifest override it.
    ///
    /// WHAT MEASUREMENT CANNOT DECIDE, and therefore what the manifest is for: which END of the gun
    /// is the muzzle. A bounding box is symmetric, so "the long axis is the barrel" is derivable but
    /// "+Z or -Z" is not - a rifle and a rifle facing backwards have identical boxes. That single
    /// bit is <c>"flip"</c>. Everything else is derived.
    ///
    /// ponytail: UNIFORM scale only, and the SMALLEST of the three ratios. Non-uniform scale would
    /// fill the box exactly and stretch the gun; the smallest ratio keeps the proportions and
    /// guarantees the result fits INSIDE the reserved box on every axis, which is what stops a
    /// rifle clipping through the soldier's arm. Add a per-axis mode only if a real model needs it.
    /// </summary>
    internal static class FitBox
    {
        /// <summary>
        /// The uniform scale and the translation that put a source box inside a target box, both
        /// given as centre + extent (half-size), the same shape Unity's Bounds and Unity's serialized
        /// <c>m_LocalAABB</c> both use.
        ///
        /// Returns false with a reason when the source has no size on some axis - a flat or empty
        /// mesh, where every ratio is infinite and the fit is meaningless.
        /// </summary>
        internal static bool Solve(float[] sourceCenter, float[] sourceExtent,
                                   float[] targetCenter, float[] targetExtent,
                                   out float scale, out float[] offset, out string why)
        {
            scale = 1f;
            offset = new[] { 0f, 0f, 0f };
            why = null;
            if (sourceCenter == null || sourceExtent == null || targetCenter == null || targetExtent == null ||
                sourceCenter.Length < 3 || sourceExtent.Length < 3 || targetCenter.Length < 3 || targetExtent.Length < 3)
            {
                why = "a fit needs a source and a target box, each as centre + extent";
                return false;
            }

            float smallest = float.MaxValue;
            for (int i = 0; i < 3; i++)
            {
                if (sourceExtent[i] <= 1e-9f)
                {
                    why = "the model has no thickness on axis " + i.ToString(CultureInfo.InvariantCulture) +
                          ", so there is nothing to scale; it is flat or empty";
                    return false;
                }
                float ratio = targetExtent[i] / sourceExtent[i];
                if (ratio < smallest) smallest = ratio;
            }
            if (smallest <= 0f || float.IsInfinity(smallest) || float.IsNaN(smallest))
            {
                why = "the target box has no size, so the donor weapon's own bounds could not be read";
                return false;
            }

            scale = smallest;
            // Scale about the SOURCE CENTRE, then move that centre onto the target's: the mesh's own
            // origin is the artist's and means nothing here.
            for (int i = 0; i < 3; i++) offset[i] = targetCenter[i] - scale * sourceCenter[i];
            return true;
        }

        /// <summary>
        /// Which axis of a box is its longest, which is the barrel of anything gun-shaped. Phoenix
        /// Point's own weapon meshes lie along +Z (the shipped sniper's box is 0.92 m on Z against
        /// 0.23 on Y), so a model whose long axis is X or Y has to be turned before it is scaled.
        /// Returns 0, 1 or 2.
        /// </summary>
        internal static int LongAxis(float[] extent)
        {
            int longest = 0;
            for (int i = 1; i < 3; i++) if (extent[i] > extent[longest]) longest = i;
            return longest;
        }

        /// <summary>
        /// The Euler rotation in degrees that turns <paramref name="from"/> onto +Z, as the whole
        /// numbers a manifest would otherwise have to carry by hand. Only the three axis-aligned
        /// cases exist, because a bounding box cannot express a diagonal.
        ///
        /// <paramref name="flip"/> adds a half turn: that is the one bit measurement cannot supply,
        /// because a box is symmetric and a gun pointing backwards has exactly the same one.
        /// </summary>
        internal static float[] RotationToZ(int from, bool flip)
        {
            float[] euler;
            switch (from)
            {
                case 0: euler = new[] { 0f, 90f, 0f }; break;   // long axis X -> yaw onto Z
                case 1: euler = new[] { -90f, 0f, 0f }; break;  // long axis Y -> pitch onto Z
                default: euler = new[] { 0f, 0f, 0f }; break;   // already Z
            }
            if (flip) euler[1] += 180f;
            return euler;
        }
    }
}
