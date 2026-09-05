using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Morgott.ContentTool.Import;
using Morgott.ContentTool.Project;

namespace Morgott.ContentTool.Bake
{
    /// <summary>
    /// THE LOAD-BACK GATES, lifted out of <c>ProjectBake.Patch</c> so bake is no longer their only
    /// caller: Verify asks the same producer the same question about a copy that is already on disk.
    /// It READS - it never bakes, never rewrites the copy and never installs anything, which is what
    /// makes it safe to run a second time over an old bake.
    ///
    /// The lines are the ones bake always printed, in the order it printed them: every arm still goes
    /// through <see cref="ProjectBake.Check"/> or appends its own VOID sentence, and the
    /// <see cref="GateEntry"/> carries THAT string, sliced out of the log rather than composed a
    /// second time. A second copy of a verdict's wording is how the log and the panel drift apart.
    ///
    /// A gate that cannot answer says VOID, never PASS or FAIL - and a VOID stays UNCOUNTED here,
    /// exactly as it was inside Patch. The carrier is what lets a caller tell the two apart
    /// (<see cref="ReadBackResult.MandatoryVoid"/>); the failure count is unchanged.
    ///
    /// P1's target key is the BUNDLE, not a texture name: that arm asks its question about every
    /// declared texture at once and always did, so recording it per row would invent a per-row proof
    /// nothing measured.
    /// </summary>
    internal static class ReadBack
    {
        internal static ReadBackResult Run(StringBuilder log, string bundleFile, string shipped, string copy,
                                           List<ImportedTexture> want,
                                           List<KeyValuePair<string, string>> mats,
                                           List<KeyValuePair<string, ImportedMesh>> meshes)
        {
            List<GateEntry> entries = new List<GateEntry>();

            // Read the pixels back out of the COPY, and check the shipped original still reads its
            // own bytes - the control that says we patched a copy and not the player's game.
            // With no texture declared for THIS bundle both arms are vacuous - and the control
            // arm is vacuously FALSE, which reported a failure on a perfectly correct mesh-only
            // bundle (ct_project 14:5x). A gate that cannot answer says VOID, never PASS or FAIL.
            if (want.Count == 0)
                Void(log, entries, "P1", bundleFile,
                     "P1 VOID 0 texture replacement(s) declared in " + bundleFile);
            else
            {
                Gate(log, entries, "P1", bundleFile, ProjectBake.PixelsIn(copy, want),
                    "every replaced Texture2D in " + bundleFile + " reads back its new pixels");
                Gate(log, entries, "P1-ctl-shipped", bundleFile, !ProjectBake.PixelsIn(shipped, want),
                    "the shipped " + bundleFile + " does NOT contain them - it was never written");
            }

            // P3 reads the property block off the FILE, the same oracle U3a-refs uses: a Material
            // is not loadable through the engine here without a shader, and the value we care
            // about is the serialized one anyway.
            foreach (KeyValuePair<string, string> m in mats)
            {
                string got = BundleBaker.ReadMaterialProperties(copy, m.Key);
                Gate(log, entries, "P3", m.Key, got.Contains("| " + m.Value),
                    "material '" + m.Key + "' in the copy carries " + m.Value + " -> " + got);
                Gate(log, entries, "P3-ctl-shipped", m.Key,
                    !BundleBaker.ReadMaterialProperties(shipped, m.Key).Contains("| " + m.Value),
                    "the shipped " + bundleFile + "'s '" + m.Key + "' does NOT carry it");
            }
            // P4 reads the geometry off the FILE, the same oracle the offline round trip uses:
            // a shipped Mesh is not CPU-readable, so the engine cannot answer this question at all.
            // Describe() is a PREFIX of Summary() by construction, so one comparison covers vertex
            // count, index count, index format and bounds together.
            foreach (KeyValuePair<string, ImportedMesh> mesh in meshes)
            {
                string want_ = mesh.Value.Baked.Describe();
                // ONE read per file: the buffers the P4-bytes arm below needs come off the same
                // Mesh field this summary does, so the bundle is decompressed once and not three
                // times per row.
                string bytesCopy, bytesShipped;
                string got = BundleBaker.ReadMeshSummary(copy, mesh.Key, false, out bytesCopy);
                Gate(log, entries, "P4", mesh.Key, got.StartsWith(want_, StringComparison.Ordinal),
                    "mesh '" + mesh.Key + "' in the copy IS " + mesh.Value.Name + " -> " + got);
                // DIAGNOSTIC, NEVER COUNTED. Describe() compares vertex/index counts, index format and
                // ROUNDED BOUNDS - not one byte of the buffers - so a replacement that only moves UVs,
                // normals or weights summarises exactly like the shipped mesh, and this control then read
                // as "we patched the player's game" and BLOCKED a correctly written mesh: patchFailed != 0
                // sends route vii straight to BakeFailed (Route7.cs:342). P4 above is the arm that says the
                // copy carries the replacement, and the shipped file's own bytes are never opened for
                // writing at all - so a summary match here is worth PRINTING and worth nothing else.
                // It is carried as a VOID when it cannot tell the two apart and a PASS when it can:
                // never a FAIL, because it has never been able to fail.
                string ctl = BundleBaker.ReadMeshSummary(shipped, mesh.Key, false, out bytesShipped);
                bool told = !ctl.StartsWith(want_, StringComparison.Ordinal);
                string ctlLine = told
                    ? "P4-ctl-shipped PASS the shipped " + bundleFile + "'s '" + mesh.Key + "' still has " +
                      "its own geometry -> " + ctl
                    : "P4-ctl-shipped WARN the shipped " + bundleFile + "'s '" + mesh.Key + "' SUMMARISES " +
                      "the same as the replacement (counts, index format and rounded bounds only, not the " +
                      "buffers), so this control cannot tell them apart -> " + ctl;
                log.AppendLine(ctlLine);
                entries.Add(told ? GateEntry.Pass("P4-ctl-shipped", mesh.Key, ctlLine)
                                 : GateEntry.Void("P4-ctl-shipped", mesh.Key, ctlLine));

                // The arm that CAN tell them apart, and the one that catches a patch which silently
                // wrote NOTHING: equal buffers mean the copy still carries the game's own mesh. P4
                // above cannot see it (same Describe() prefix) and P5 is VOID unless the target is
                // rigged, so on an UNSKINNED mesh this is the only proof the replacement landed.
                // A side with no readable buffers cannot answer this: MeshFields.Buffers says null
                // rather than hashing an empty array, because sha256("") vertexBytes=0 is what BOTH
                // sides would then report - equal, and read as "the patch wrote nothing" on a bake
                // that is perfectly correct. Same law as P1 above: a gate that cannot answer says
                // VOID. Two readable sides that agree is still a true FAIL, streamed included.
                if (bytesCopy == null || bytesShipped == null)
                    Void(log, entries, "P4-bytes", mesh.Key,
                         "P4-bytes VOID mesh '" + mesh.Key + "' has no readable vertex/index " +
                         "buffers in " + (bytesCopy == null && bytesShipped == null
                                          ? copy + " and " + shipped
                                          : bytesCopy == null ? copy : shipped));
                else
                    Gate(log, entries, "P4-bytes", mesh.Key, bytesCopy != bytesShipped,
                        "mesh '" + mesh.Key + "' in the copy carries DIFFERENT vertex/index bytes than the " +
                        "shipped " + bundleFile + " -> copy " + bytesCopy + " | shipped " + bytesShipped);

                // P5: the replacement is SKINNED to the target's own skeleton. The expected
                // skeleton is not a constant - it is read off the SHIPPED file in this same run,
                // so the arm asserts that the copy carries the exact bind poses, bone hashes and
                // root hash the game shipped, PLUS our skin stream over them, PLUS a bone index
                // that every one of those bind poses can answer. Rebind doing nothing and Rebind
                // clobbering the skeleton both come out RED.
                // No separate control arm: "the shipped file was never written" is P4-ctl-shipped's
                // question and "a rebound mesh actually deforms" is U5b-deform's, both in the same
                // run. A third arm here would restate one of them.
                string skinShipped = BundleBaker.ReadMeshSummary(shipped, mesh.Key, true);
                string skinCopy = BundleBaker.ReadMeshSummary(copy, mesh.Key, true);
                string skeleton = ProjectBake.Skeleton(skinShipped);
                if (skeleton == null)
                    Void(log, entries, "P5", mesh.Key,
                         "P5 VOID '" + mesh.Key + "' is not rigged - " + skinShipped);
                else
                {
                    // The width is the SHIPPED target's own, read in this same run: a replacement
                    // that narrowed a dim4 body part reads RED here instead of quietly shipping.
                    int inf = Math.Max(BundleBaker.ReadInfluenceCount(shipped, mesh.Key), 1);
                    string wantSkin = skeleton + " " + SkinFields.OurLayout(inf) + " skinBytes=" +
                                      mesh.Value.Baked.VertexCount * SkinFields.SkinStride(inf);
                    Gate(log, entries, "P5", mesh.Key,
                        skinCopy.StartsWith(wantSkin, StringComparison.Ordinal) &&
                        skinCopy.EndsWith(" inRange=yes", StringComparison.Ordinal),
                        "mesh '" + mesh.Key + "' in the copy is SKINNED to the shipped skeleton -> " +
                        skinCopy + " (expected " + wantSkin + " ... inRange=yes; shipped is " +
                        skinShipped + ")");
                }

                ByName(log, entries, mesh.Key, mesh.Value, shipped, copy);
            }

            return ReadBackResult.Of(entries.ToArray());
        }

        /// <summary>
        /// P6 - a rigged replacement carries the AUTHOR'S OWN weights, on the bones the author's file
        /// NAMES, and not on the slots it happens to list them in.
        ///
        /// The oracle is every vertex of the written copy, read back out of the bytes
        /// (<see cref="SkinFields.SkinInfluences"/>), against an expectation built HERE from two
        /// independent things: the file's own WEIGHTS_0, and a plain name lookup of the file's joint
        /// in the SHIPPED skeleton read off the shipped bundle in this same run. Nothing in the
        /// expectation comes from the binder, so a binder that transposed the rig cannot agree with it.
        ///
        /// The arm REFUSES to run on a file whose joint order already matches the target's, because
        /// there a by-name binding and an index binding write identical bytes and the run would
        /// measure nothing. That is a VOID, never a PASS - it is the vacuity this gate exists to
        /// avoid, and the sample's fixture is written with its joints REVERSED precisely so the
        /// question can be asked at all.
        /// </summary>
        private static void ByName(StringBuilder log, List<GateEntry> entries, string key, ImportedMesh im,
                                   string shipped, string copy)
        {
            SkinnedModel f = im.Model;
            if (f == null || f.JointNames.Count == 0)
            {
                Void(log, entries, "P6", key,
                     "P6 VOID '" + key + "' <- " + im.Name + " carries no armature (" +
                     (f == null ? "an .obj never does" : "the .glb has no skin") +
                     "), so there are no weights of its own to remap");
                return;
            }

            // REFUSED and ABSENT are not the same answer: a rig whose bones CONTRADICT the mesh's own
            // hashes is a fact about the file and a counted failure, while nobody naming them at all is
            // an absence and a VOID. Reading the refusal-less overload printed the absence sentence for
            // a self-contradicting file, so the bake log blamed the wrong thing and counted nothing.
            string refusal;
            string[] bones = BundleBaker.ReadBoneNames(shipped, key, out refusal);
            if (bones == null && refusal != null)
            {
                Gate(log, entries, "P6", key, false, "mesh '" + key + "' - " + Path.GetFileName(shipped) +
                    " REFUSES to name this mesh's bones: " + refusal);
                return;
            }
            if (bones == null)
            {
                Void(log, entries, "P6", key,
                     "P6 VOID '" + key + "' - no SkinnedMeshRenderer in " +
                     Path.GetFileName(shipped) + " names this mesh's bones, so the shipped " +
                     "skeleton cannot be looked up by name");
                return;
            }

            // A rig with an UNVERIFIED slot is a VOID, not a failure: nothing here contradicts, the
            // TARGET simply has no complete named rig to bind onto, and the bake correctly finished
            // that mesh nearest-bone (BundleBaker.ReplaceMesh, the same Array.IndexOf guard). Counting
            // it FAIL propagated to `ct_project: N FAILURE(S)` over a bake that did the right thing.
            int unverified = Array.IndexOf(bones, null);
            if (unverified >= 0)
            {
                Void(log, entries, "P6", key,
                     "P6 VOID '" + key + "' - " + Path.GetFileName(shipped) + " leaves bone " +
                     "slot " + unverified + " of " + bones.Length + " UNVERIFIED against the " +
                     "mesh's own bone path hashes, so there is no named target rig to bind " +
                     "onto and this arm cannot prove a by-name binding either way");
                return;
            }

            int n = im.Baked.VertexCount;
            if (f.Joints == null || f.Weights == null || f.Joints.Length != n * 4)
            {
                Void(log, entries, "P6", key,
                     "P6 VOID '" + key + "' - the file's skin does not cover its " + n + " vertices");
                return;
            }

            // DID THE BY-NAME BINDING ACTUALLY HAPPEN? Everything below PREDICTS one from the file's
            // own weights and asserts the copy matches - but BundleBaker.ReplaceMesh only binds by name
            // when this same binder accepts, and falls back to nearest-bone when it does not. Without
            // asking, the gate reported FAIL against a copy that was correctly bound nearest-bone,
            // contradicting the patch line in its own run (measured on an applied-transform body part
            // carrying 19 joints - both naming forms at once, plus bones the skeleton does not have).
            // The patch line was right; this arm was the liar. A refused bind is a VOID: there is no
            // by-name binding to measure, which is exactly what a skinless source already reports.
            try
            {
                ushort[] unusedJoints;
                float[][] unusedPoses;
                SkinBinder.Bind(f, bones, 0, null, out unusedJoints, out unusedPoses);
            }
            catch (Exception e)
            {
                Void(log, entries, "P6", key,
                     "P6 VOID '" + key + "' <- " + im.Name + " was bound nearest-bone, not by " +
                     "name, so there is no by-name binding to measure: " + e.Message);
                return;
            }

            // The TARGET's own width, read off the shipped file - the same number the bake keeps.
            int inf = Math.Max(BundleBaker.ReadInfluenceCount(shipped, key), 1);
            int[] slots = new int[inf];
            string want = "";
            int moved = 0, split = 0;
            for (int i = 0; i < n; i++)
            {
                SkinFields.Heaviest(f.Weights, i, slots);
                float sum = 0f;
                for (int k = 0; k < inf; k++) if (slots[k] >= 0) sum += f.Weights[i * 4 + slots[k]];
                // A vertex the file left unweighted goes whole to the bone its FIRST slot names -
                // the same rule RebindByName writes.
                bool unweighted = sum <= 0f;

                string w = "", b = "";
                bool off = false, shared = false;
                for (int k = 0; k < inf; k++)
                {
                    int at = unweighted || slots[k] < 0 ? (unweighted ? 0 : slots[0]) : slots[k];
                    int slot = f.Joints[i * 4 + at];
                    int live = Live(bones, f.JointNames[slot]);
                    if (live < 0)
                    {
                        Void(log, entries, "P6", key,
                             "P6 VOID '" + key + "' - the file's bone '" + f.JointNames[slot] +
                             "' is not on the shipped skeleton, so this replacement was refused " +
                             "by name and there is no by-name binding to measure");
                        return;
                    }
                    float weight = unweighted ? (k == 0 ? 1f : 0f)
                                 : slots[k] < 0 ? 0f : f.Weights[i * 4 + slots[k]] / sum;
                    if (live != slot) off = true;
                    if (k > 0 && weight > 0f) shared = true;
                    w += (k == 0 ? "" : "/") + ModelBuild.F(weight);
                    b += (k == 0 ? "->bone" : "+bone") + live;
                }
                if (off) moved++;
                if (shared) split++;
                want += (i == 0 ? "" : " ") + "v" + i + "=" + w + b;
            }

            if (moved == 0)
            {
                Void(log, entries, "P6", key,
                     "P6 VOID '" + key + "' <- " + im.Name + " lists the skeleton in the " +
                     "target's own order, so a binding by NAME and one by SLOT write the same " +
                     "bytes and this run would measure nothing");
                return;
            }

            string got = BundleBaker.ReadSkinInfluences(copy, key);
            Gate(log, entries, "P6", key, got == want,
                "mesh '" + key + "' <- " + im.Name + " carries the FILE's own weights on the bones it " +
                "NAMES: " + moved + " of " + n + " vertices sit at a file slot that is not the live bone " +
                "index, and " + split + " are shared between two bones (a fraction nearest-bone cannot " +
                "produce). The copy reads " + got + "; the file's own weights and a name lookup in the " +
                "shipped skeleton (" + bones.Length + " bones) predict " + want);
        }

        /// <summary>The shipped bone a file joint names, read the SAME way SkinBinder.Bind reads it -
        /// exact first, then with the game's own '#&lt;bone&gt;_Addon =&gt; &lt;part&gt;' decoration
        /// removed - or this gate would report VOID for exactly the files that now bind.</summary>
        private static int Live(string[] bones, string joint)
        {
            int at = Array.IndexOf(bones, joint);
            return at >= 0 ? at : Array.IndexOf(bones, SkinBinder.Plain(joint));
        }

        /// <summary>One counted arm. <see cref="ProjectBake.Check"/> stays the only author of the
        /// " PASS "/" FAIL " wording - the entry carries the line it just appended, sliced back out of
        /// the log, so the panel and the console can never quote two different sentences.</summary>
        private static void Gate(StringBuilder log, List<GateEntry> entries, string gate, string target,
                                 bool ok, string detail)
        {
            int at = log.Length;
            bool failed = ProjectBake.Check(log, gate, ok, detail) != 0;
            string line = log.ToString(at, log.Length - at).TrimEnd('\r', '\n');
            entries.Add(failed ? GateEntry.Fail(gate, target, line) : GateEntry.Pass(gate, target, line));
        }

        /// <summary>An arm that could not ask its question. UNCOUNTED, exactly as it was inside Patch -
        /// the carrier is what tells a VOID from a pass now, not the number.</summary>
        private static void Void(StringBuilder log, List<GateEntry> entries, string gate, string target,
                                 string line)
        {
            log.AppendLine(line);
            entries.Add(GateEntry.Void(gate, target, line));
        }
    }
}
