using System;
using System.Collections.Generic;
using System.Globalization;
// For AttachmentPrefix alone - the ONE fact both halves have to agree on about EXT_. Both files are
// UnityEngine-free, so this hop costs the worker thread nothing.
using Morgott.ContentTool.Doctor;

namespace Morgott.ContentTool.Import
{
    /// <summary>Which of Bind's three phases an issue belongs to. Bind runs Submeshes/Shapes BETWEEN
    /// the skin guards and the bone checks (GlbReader.cs:2465-2466), and Submeshes(file, 0) is not a
    /// no-op - it bounds-checks every triangle index - so the phases must be kept apart or the
    /// extraction silently changes which sentence an author reads.</summary>
    internal enum BindStage { Skin, Mesh, Bones }

    /// <summary>Whose asset the issue is about. The Doctor draws Target rows separately, because
    /// "this is the game's model, not your file" is the difference between a fix and a dead end.</summary>
    internal enum BindSide { File, Target }

    /// <summary>Stable identity of one binding disagreement. The catalogue is spec v3 §7.</summary>
    internal enum BindCode
    {
        TargetBonesUnavailable, NoArmature, JointsWeightsMismatch,
        TriangleOutOfRange, BlendShapeCount,
        TargetBoneEmpty, TargetBoneDuplicate, TargetBindPoseMismatch, DuplicateFileBone, PlainCollision,
        MissingBone, ExtraBone, NotBijective, InverseBindCount, BoneIndexOutOfRange,
        // APPEND ONLY from here down - the enum is persisted in saved reports, so a member that moves
        // renames somebody's stored diagnosis. Extend-mode only.
        ExtJointWeighted, ExtJointUnused
    }

    /// <summary>
    /// ONE reason a file and a rig do not correspond, WITHOUT a severity. Severity is a UI decision -
    /// the Doctor calls these Downgrade because the bake imports anyway and merely loses the author's
    /// weights, while SkinBinder.Bind treats the very first one as fatal. A severity carried here
    /// would have to be both at once.
    /// </summary>
    internal sealed class BindingIssue
    {
        internal BindCode Code;
        internal BindStage Stage;
        internal BindSide Side;
        /// <summary>The binder's OWN sentence, verbatim - never a new wording.</summary>
        internal string Message;
        /// <summary>The bone the row is about, or null. This is what the bone-map table keys on.</summary>
        internal string Subject;
    }

    /// <summary>
    /// A plain snapshot of the live target, taken on the main thread so the worker never touches a
    /// UnityEngine object. The last five fields are the FINGERPRINT: a SkinnedMeshRenderer keeps its
    /// instance id while its mesh, its bind poses and its bones are replaced under it.
    ///
    /// Nothing WRITES these yet - the Doctor preflight fills them from the renderer (task 7), which
    /// is why the compiler reports them as never assigned until then.
    /// </summary>
    internal sealed class RigTarget
    {
        /// <summary>smr.bones[b].name, in the live rig's order. NULL when the renderer lists no bones
        /// (LiveMesh.cs:116-117) - which is the nearest-bone branch, not an error.</summary>
        internal string[] BoneNames;
        /// <summary>From BIND POSES, the same fact SkinFields.Rigged keys on (SkinFields.cs:623-626).</summary>
        internal bool Rigged;
        internal int RendererInstanceId;
        internal int MeshInstanceId;
        internal int BindPoseCount;
        internal string TransformPath = "";
        internal string MeshName = "";

        internal bool SameAs(RigTarget other)
        {
            if (other == null) return false;
            if (RendererInstanceId != other.RendererInstanceId || MeshInstanceId != other.MeshInstanceId ||
                BindPoseCount != other.BindPoseCount || Rigged != other.Rigged ||
                !string.Equals(TransformPath, other.TransformPath, StringComparison.Ordinal) ||
                !string.Equals(MeshName, other.MeshName, StringComparison.Ordinal)) return false;
            if (BoneNames == null || other.BoneNames == null) return BoneNames == other.BoneNames;
            if (BoneNames.Length != other.BoneNames.Length) return false;
            for (int i = 0; i < BoneNames.Length; i++)
                if (!string.Equals(BoneNames[i], other.BoneNames[i], StringComparison.Ordinal)) return false;
            return true;
        }
    }

    /// <summary>
    /// Every check <see cref="SkinBinder.Bind"/> performs, in Bind's own order, as a LIST instead of a
    /// first-failure throw. Bind still throws the first one and its sentences are unchanged, so the
    /// bake cannot drift from what the Doctor predicts; the Doctor gets all of them at once, which is
    /// the whole feature (an author fixes three bone names in one pass, not one per game launch).
    ///
    /// The replacement path is what this describes: Bind(file, boneNames, 0, null, ...) -
    /// LiveMesh.cs:217, SkinFields.cs:748. Material-slot and blend-shape-name checks against a
    /// non-empty list stay in Bind, because no replacement caller passes one.
    /// </summary>
    internal static class SkinCompatibility
    {
        internal static IList<BindingIssue> Analyze(SkinnedModel file, IList<string> boneNames)
        {
            int[] liveOf, fileOf;
            return Analyze(file, boneNames, 0, false, out liveOf, out fileOf);
        }

        /// <summary>THE BAKE'S ENTRY POINT, and every gate that predates the prototype picker. It
        /// binds extend to false, so the replacement verdict cannot drift.</summary>
        internal static IList<BindingIssue> Analyze(SkinnedModel file, IList<string> boneNames, int expectedShapes,
                                                    out int[] liveOf, out int[] fileOf)
        {
            return Analyze(file, boneNames, expectedShapes, false, out liveOf, out fileOf);
        }

        internal static IList<BindingIssue> Analyze(SkinnedModel file, IList<string> boneNames, int expectedShapes,
                                                    bool extend)
        {
            int[] liveOf, fileOf;
            return Analyze(file, boneNames, expectedShapes, extend, out liveOf, out fileOf);
        }

        /// <param name="expectedShapes">How many blend shapes the target model has - what Bind hands
        /// Shapes as a NAME list. The replacement path passes 0, so a file with any shape key is
        /// refused there; a caller that does drive shapes gets the same count check Shapes performs.</param>
        /// <param name="extend">EXTEND mode: the file is a PARTIAL body part being added to a whole
        /// prototype rig, not a replacement for one slot's renderer. A rig bone the file does not use
        /// is then no defect at all, so MissingBone is suppressed and the bijection is not asked -
        /// see the design's section 3. False is the replacement path and must stay byte-identical to
        /// what the binder has always done.</param>
        /// <param name="liveOf">file joint -&gt; live bone index, or null when the file cannot be bound.</param>
        /// <param name="fileOf">live bone -&gt; file joint index, or null when the file cannot be bound.
        /// In Extend mode an unused rig bone keeps its -1: the caller asked for a SUBSET.</param>
        internal static IList<BindingIssue> Analyze(SkinnedModel file, IList<string> boneNames, int expectedShapes,
                                                    bool extend, out int[] liveOf, out int[] fileOf)
        {
            if (file == null) throw new ArgumentNullException(nameof(file));
            liveOf = null;
            fileOf = null;
            var issues = new List<BindingIssue>();

            // ---- Stage.Skin: GlbReader.cs:2454-2463, before Submeshes/Shapes.
            if (boneNames == null || boneNames.Count == 0)
                Add(issues, BindCode.TargetBonesUnavailable, BindStage.Skin, BindSide.Target, null,
                    "the target model lists no bones, so there is no skeleton to bind onto; " +
                    "reload the scene and try again");
            if (file.JointNames.Count == 0)
                Add(issues, BindCode.NoArmature, BindStage.Skin, BindSide.File, null,
                    "the file carries no armature, so it cannot replace a rigged model; " +
                    "in Blender export the mesh together with its armature, or put the file on a static object instead");
            if (file.Joints == null || file.Weights == null || file.Positions == null ||
                file.Joints.Length != (file.Positions == null ? -1 : file.Positions.Length) * 4 ||
                file.Weights.Length != file.Joints.Length)
                Add(issues, BindCode.JointsWeightsMismatch, BindStage.Skin, BindSide.File, null,
                    "the file's bone weights do not cover every vertex; " +
                    "in Blender give the whole mesh an Armature modifier with vertex groups and re-export");
            if (issues.Count > 0) return issues;

            // ---- Stage.Mesh: what Submeshes(file, 0) and Shapes(file, null) refuse.
            int vertices = file.Positions == null ? 0 : file.Positions.Length;
            foreach (int[] triangles in file.Submeshes)
            {
                foreach (int index in triangles)
                    if (index < 0 || index >= vertices)
                    {
                        Add(issues, BindCode.TriangleOutOfRange, BindStage.Mesh, BindSide.File, null,
                            "a triangle points at vertex " + index.ToString(CultureInfo.InvariantCulture) +
                            " of " + vertices.ToString(CultureInfo.InvariantCulture) +
                            "; the file is corrupt, so re-export it");
                        break;
                    }
                if (issues.Count > 0) break;
            }
            if (file.Morphs.Count != expectedShapes)
                Add(issues, BindCode.BlendShapeCount, BindStage.Mesh, BindSide.File, null,
                    "the file has " + file.Morphs.Count.ToString(CultureInfo.InvariantCulture) +
                    " blend shapes but this model has " + expectedShapes.ToString(CultureInfo.InvariantCulture) +
                    ", and the game drives them by position; " +
                    "in Blender keep every shape key that came with the model, in the same order, and re-export");
            if (issues.Count > 0) return issues;

            // ---- Stage.Bones: GlbReader.cs:2468-2544.
            var live = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int i = 0; i < boneNames.Count; i++)
            {
                if (string.IsNullOrEmpty(boneNames[i]))
                    Add(issues, BindCode.TargetBoneEmpty, BindStage.Bones, BindSide.Target, null,
                        "the target model's bone " + i.ToString(CultureInfo.InvariantCulture) +
                        " has no name, so nothing in the file can be matched to it; reload the scene and try again");
                else if (live.ContainsKey(boneNames[i]))
                    Add(issues, BindCode.TargetBoneDuplicate, BindStage.Bones, BindSide.Target, boneNames[i],
                        "the target model has two bones named '" + boneNames[i] +
                        "', so a bone in the file cannot be matched to one of them; this model cannot be replaced by name");
                else live[boneNames[i]] = i;
            }
            var seen = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int j = 0; j < file.JointNames.Count; j++)
            {
                if (seen.ContainsKey(file.JointNames[j]))
                    Add(issues, BindCode.DuplicateFileBone, BindStage.Bones, BindSide.File, file.JointNames[j],
                        "the file has two bones named '" + file.JointNames[j] +
                        "'; rename one of them in Blender so every bone name is unique, then re-export");
                else seen[file.JointNames[j]] = j;
            }
            // A rig taken from a LIVE scene carries the game's own decoration on every attachment
            // point instead of the plain bone name, so the intersection with the shipped skeleton is
            // empty. Undecorate them, EXACT NAMES FIRST so a plainly-named file behaves identically.
            var plain = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int j = 0; j < file.JointNames.Count; j++)
            {
                string bare = SkinBinder.Plain(file.JointNames[j]);
                if (bare == file.JointNames[j] || seen.ContainsKey(bare)) continue;
                if (plain.ContainsKey(bare))
                    Add(issues, BindCode.PlainCollision, BindStage.Bones, BindSide.File, bare,
                        "the file's bones '" + file.JointNames[plain[bare]] + "' and '" +
                        file.JointNames[j] + "' both name the bone '" + bare + "' once the game's own " +
                        "'#<bone>_Addon => <part>' decoration is removed, so neither can be matched to it; " +
                        "keep the one that belongs to this model and re-export");
                else plain[bare] = j;
            }
            if (issues.Count > 0) return issues;

            int[] toLive = new int[file.JointNames.Count];
            int[] toFile = new int[boneNames.Count];
            for (int i = 0; i < toLive.Length; i++) toLive[i] = -1;
            for (int i = 0; i < toFile.Length; i++) toFile[i] = -1;

            // Every live bone must be in the file. This is the one that breaks deformation, so it is
            // reported first and by name.
            for (int i = 0; i < boneNames.Count; i++)
            {
                int j;
                // BOTH SIDES, exact first. The decoration is on whichever rig came out of a LIVE scene,
                // and that is not always the file: the bake reads the SHIPPED asset's plain names, while
                // the Model Doctor and the live preview read the renderer standing in front of them,
                // where the addon system has already renamed every attachment point to
                // '#<bone>_Addon => <part>'. Undecorating the file alone made ct_extract's own dump of
                // CHR_SY_SNI_TS_F_V01 report 13 MissingBone + 13 ExtraBone against the very renderer it
                // was extracted from, so the Doctor could never reach BY NAME in game.
                if (!Match(seen, plain, boneNames[i], out j) &&
                    !Match(seen, plain, SkinBinder.Plain(boneNames[i]), out j))
                {
                    // EXTEND: a hand does not carry the legs, and the rig keeps its own bones either
                    // way. Only the REPLACE path is a promise to reproduce a renderer exactly.
                    if (!extend)
                        Add(issues, BindCode.MissingBone, BindStage.Bones, BindSide.File, boneNames[i],
                            "the file does not contain the bone '" + boneNames[i] +
                            "', which this model's skeleton has; the skeleton is never replaced, so in Blender keep the imported " +
                            "armature exactly as it came, with every bone and its name unchanged, and re-export");
                    continue;
                }
                toFile[i] = j;
                toLive[j] = i;
            }
            for (int j = 0; j < file.JointNames.Count; j++)
                if (toLive[j] < 0)
                    Add(issues, BindCode.ExtraBone, BindStage.Bones, BindSide.File, file.JointNames[j],
                        "the file adds the bone '" + file.JointNames[j] +
                        "', which this model's skeleton does not have; the skeleton is never replaced, so delete the added bone " +
                        "in Blender and re-export");
            // The FILE's own attachment points, EXTEND ONLY. Addon.GetEquivalentBones skips every
            // transform whose name starts with EXT_ (Addon.cs:1208), so weights painted onto one are
            // dropped without a word. On the REPLACE path an EXT_ bone the live renderer really lists
            // is load-bearing and binds like any other, so nothing new fires there.
            if (extend)
            {
                var weighted = new bool[file.JointNames.Count];
                for (int i = 0; i < file.Joints.Length; i++)
                {
                    int slot = file.Joints[i];
                    if (slot < weighted.Length && file.Weights[i] > 0f) weighted[slot] = true;
                }
                for (int j = 0; j < file.JointNames.Count; j++)
                {
                    if (!PrototypeCatalog.IsAttachmentPoint(file.JointNames[j])) continue;
                    if (weighted[j])
                        Add(issues, BindCode.ExtJointWeighted, BindStage.Bones, BindSide.File, file.JointNames[j],
                            "the file weights vertices to '" + file.JointNames[j] +
                            "', and the game SKIPS every bone whose name starts with EXT_ (it is an attachment point, " +
                            "not a skin joint) - so those weights would be lost silently; " +
                            "in Blender move that influence onto a real bone and re-export");
                    else
                        Add(issues, BindCode.ExtJointUnused, BindStage.Bones, BindSide.File, file.JointNames[j],
                            "the file carries the attachment point '" + file.JointNames[j] +
                            "', which the game does not bind to; nothing is lost, but it will not follow the rig either");
                }
            }
            // The bijection, the bind-pose count and the vertex bone indices are only ASKABLE once
            // every bone has a partner - toFile still holds -1 otherwise, and indexing with it is
            // the crash the binder avoids by throwing at the first failure. EXTEND never gets that
            // far by design: an unused rig bone legitimately keeps its -1, so the bijection is not a
            // question that can be asked at all on that path.
            if (issues.Count > 0) return issues;

            if (!extend)
                for (int i = 0; i < toFile.Length; i++)
                    if (toLive[toFile[i]] != i)
                    {
                        Add(issues, BindCode.NotBijective, BindStage.Bones, BindSide.File, boneNames[i],
                            "the file's bones could not be matched one to one onto this model's skeleton; " +
                            "re-export from the model this mod dumped, without adding, removing or renaming bones");
                        break;
                    }
            if (file.InverseBindMatrices == null || file.InverseBindMatrices.Length != file.JointNames.Count)
                Add(issues, BindCode.InverseBindCount, BindStage.Bones, BindSide.File, null,
                    "the file has " +
                    (file.InverseBindMatrices == null ? 0 : file.InverseBindMatrices.Length).ToString(CultureInfo.InvariantCulture) +
                    " bind poses for " + file.JointNames.Count.ToString(CultureInfo.InvariantCulture) +
                    " bones; re-export from Blender rather than editing the file by hand");
            if (issues.Count > 0) return issues;

            for (int i = 0; i < file.Joints.Length; i++)
            {
                int slot = file.Joints[i];
                if (slot < toLive.Length) continue;
                Add(issues, BindCode.BoneIndexOutOfRange, BindStage.Bones, BindSide.File, null,
                    "vertex " + (i / 4).ToString(CultureInfo.InvariantCulture) + " references bone " +
                    slot.ToString(CultureInfo.InvariantCulture) + " but the file has " +
                    toLive.Length.ToString(CultureInfo.InvariantCulture) + "; the file is corrupt, so re-export it");
                break;
            }
            if (issues.Count > 0) return issues;

            liveOf = toLive;
            fileOf = toFile;
            return issues;
        }

        /// <summary>The first issue of a stage, or null. Bind throws these one stage at a time.</summary>
        internal static BindingIssue First(IList<BindingIssue> issues, BindStage stage)
        {
            for (int i = 0; i < issues.Count; i++) if (issues[i].Stage == stage) return issues[i];
            return null;
        }

        /// <summary>One target bone name looked up against the file's bones - as exported, then as the
        /// game's decoration leaves them. Two calls with the plain and the decorated spelling cover
        /// every combination, and an undecorated name simply misses twice.</summary>
        private static bool Match(Dictionary<string, int> exact, Dictionary<string, int> plain,
                                  string name, out int j)
        {
            return exact.TryGetValue(name, out j) || plain.TryGetValue(name, out j);
        }

        private static void Add(List<BindingIssue> into, BindCode code, BindStage stage, BindSide side,
                                string subject, string message)
        {
            into.Add(new BindingIssue { Code = code, Stage = stage, Side = side, Subject = subject, Message = message });
        }
    }
}
