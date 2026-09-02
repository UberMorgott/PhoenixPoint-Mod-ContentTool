using System;
using System.Collections.Generic;
using Morgott.ContentTool.Import;

namespace Morgott.ContentTool.Doctor
{
    /// <summary>What one rig bone's colour means. Three distinct colours for the three states the
    /// picker design's legend names, plus the two the legend does not: a bone nothing claims, and an
    /// <c>EXT_</c> attachment point the game itself skips.</summary>
    internal enum BoneStatus { Unmatched, ByName, Alias, Nearest, Attachment }

    /// <summary>Why a joint cannot take the armed alias row, or <see cref="Ok"/>. A refusal is SAID -
    /// an armed click that lands on nothing and explains nothing reads as the overlay being broken.</summary>
    internal enum AliasRefusal { Ok, Attachment, BoundByName, Claimed }

    /// <summary>
    /// THE SKELETON OVERLAY'S ARITHMETIC, decided offline. Which colour a bone gets is string work over
    /// rows the preflight already produced - the report's own <c>MissingBone</c> subjects, the author's
    /// alias map and the file's joint names - and which joint a click lands on is pixel distance. Both
    /// are proven here rather than by squinting at a screenshot, which is the only other way to find
    /// out that a picture and its hit test disagree.
    ///
    /// Nothing in this file computes a BINDING. The binder already ran, on a worker thread, and its
    /// answer is what these rows are read off; recomputing it here would be a second opinion that can
    /// drift from the verdict drawn three lines above it.
    /// </summary>
    internal static class BoneOverlay
    {
        /// <summary>Within this many pixels of a joint dot, a press is that joint's.</summary>
        internal const float PickRadiusPixels = 12f;

        /// <summary>The prefix the game's own addon code skips wholesale (Addon.cs:1208).</summary>
        internal const string AttachmentPrefix = "EXT_";

        /// <summary>
        /// ONE bone's status. The ORDER is the whole rule: <c>EXT_</c> first (the game skips it, so it
        /// is never a defect), then an explicit alias (the author outranks a coincidence of names), then
        /// a by-name match under <see cref="SkinBinder.Plain"/>, then whether the bind fell back to
        /// nearest-bone. A bone none of them claims is <see cref="BoneStatus.Unmatched"/>.
        /// </summary>
        /// <param name="fileJoints">The .glb's own joint names, decorated or not.</param>
        /// <param name="aliases">file bone -&gt; target bone, the map ModelDoctor is editing.</param>
        /// <param name="missing">The report's <c>MissingBone</c> subjects - target bones the file
        /// answered nothing for.</param>
        /// <param name="nearestBind">Did the verdict come out NEAREST-BONE? That is what turns an
        /// unanswered bone from "lost" into "bound to the closest one".</param>
        internal static BoneStatus Classify(string boneName, ICollection<string> fileJoints,
                                            IDictionary<string, string> aliases,
                                            ICollection<string> missing, bool nearestBind)
        {
            if (string.IsNullOrEmpty(boneName)) return BoneStatus.Unmatched;
            if (boneName.StartsWith(AttachmentPrefix, StringComparison.Ordinal)) return BoneStatus.Attachment;

            if (aliases != null)
                foreach (KeyValuePair<string, string> e in aliases)
                    if (string.Equals(e.Value, boneName, StringComparison.Ordinal)) return BoneStatus.Alias;

            if (fileJoints != null)
                foreach (string joint in fileJoints)
                    if (joint != null && string.Equals(SkinBinder.Plain(joint), boneName, StringComparison.Ordinal))
                        return BoneStatus.ByName;

            return nearestBind && missing != null && missing.Contains(boneName)
                 ? BoneStatus.Nearest : BoneStatus.Unmatched;
        }

        /// <summary>
        /// CAN THE ARMED ALIAS ROW LAND ON THIS BONE? Decided from the status the bone was already
        /// COLOURED by, so what an author sees is exactly what the click may do - a second rule here
        /// would be a second opinion that can drift from the picture.
        ///
        /// <see cref="BoneStatus.Unmatched"/> and <see cref="BoneStatus.Nearest"/> are the eligible
        /// pair: both mean no file joint answers for this bone, which is precisely the bone map's own
        /// dropdown contents (the report's <c>MissingBone</c> subjects, minus the ones an alias already
        /// claimed). <see cref="BoneStatus.Attachment"/> is an <c>EXT_</c> point the game skips
        /// wholesale, <see cref="BoneStatus.ByName"/> is a bone a file joint already reaches - both are
        /// the PlainCollision the binder refuses, and offering them would build a map that is rejected
        /// on the next preflight. <see cref="BoneStatus.Alias"/> belongs to whichever row put it there:
        /// another row's is claimed, the armed row's own is a harmless re-pick of what it already says.
        /// </summary>
        /// <param name="aliases">file bone -&gt; target bone. Only read for the Alias arm, where it says
        /// WHOSE alias this bone is.</param>
        /// <param name="armedFileJoint">The file joint the armed bone-map row is waiting to place.</param>
        internal static AliasRefusal CanAlias(string boneName, BoneStatus status,
                                              IDictionary<string, string> aliases, string armedFileJoint)
        {
            if (status == BoneStatus.Attachment) return AliasRefusal.Attachment;
            if (status == BoneStatus.ByName) return AliasRefusal.BoundByName;
            if (status != BoneStatus.Alias) return AliasRefusal.Ok;
            if (aliases != null)
                foreach (KeyValuePair<string, string> e in aliases)
                    if (string.Equals(e.Value, boneName, StringComparison.Ordinal))
                        return string.Equals(e.Key, armedFileJoint, StringComparison.Ordinal)
                             ? AliasRefusal.Ok : AliasRefusal.Claimed;
            // Coloured as an alias by a map that no longer holds one: the report moved under the
            // colours. Refuse rather than guess - the next generation recolours it anyway.
            return AliasRefusal.Claimed;
        }

        /// <summary>
        /// The joint nearest a cursor, or false. Ties go to the LOWEST INDEX so a pick over two
        /// overlapping joints is repeatable - one that alternates between them is a pick nobody can
        /// make. An empty array and a NaN (cursor or projected point) are a miss, never a throw: every
        /// one of those arrives from a live camera, and this runs inside OnGUI where a throw closes the
        /// whole bench.
        /// </summary>
        /// <param name="px">Projected x per joint, in the camera's screen convention.</param>
        /// <param name="visible">Which joints were in front of the near plane this pass. An invisible
        /// joint is not drawn, so it is not pickable either.</param>
        internal static bool Nearest(float x, float y, float[] px, float[] py, bool[] visible,
                                     float radiusPixels, out int index)
        {
            index = -1;
            if (px == null || py == null) return false;
            float best = radiusPixels * radiusPixels;
            for (int i = 0; i < px.Length && i < py.Length; i++)
            {
                if (visible != null && (i >= visible.Length || !visible[i])) continue;
                float dx = px[i] - x, dy = py[i] - y;
                float d = dx * dx + dy * dy;
                // Written as "not inside" rather than "outside" on purpose: a NaN distance fails EVERY
                // comparison, so this is also the arm that drops a NaN cursor and a NaN joint.
                if (!(d <= best)) continue;
                if (index >= 0 && !(d < best)) continue;      // an exact tie keeps the lower index
                best = d; index = i;
            }
            return index >= 0;
        }
    }
}
