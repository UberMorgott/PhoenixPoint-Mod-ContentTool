using System;
using System.Collections.Generic;
using System.Globalization;
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
        /// <summary>
        /// THE VERIFY PRODUCER, and there is exactly one of it: the dashboard's Verify row
        /// (<c>LifecycleJob.StartVerify</c>) and `ct_route7 verify &lt;project&gt;` both print what this
        /// returns, which is what makes W18's parity assertion structural rather than a comparison of two
        /// formatters.
        ///
        /// IT READS. No <see cref="BundleBaker"/> is constructed, so nothing is opened for writing and
        /// <c>baker.WhyNot</c> is never asked - the gates measure the COPY that is already on disk, and an
        /// absent copy is a CENSUS MISS rather than a failed gate: the target is named and no gate is run
        /// over a file that is not there.
        ///
        /// THE EXPECTATION LISTS CANNOT BE REUSED FROM THE PATCH LOOP - they are filled interleaved with
        /// the writer (ProjectBake.cs:1772, :1791, :1822, :1842) - so they are REBUILT here from the
        /// imported project alone, through the same <c>ProjectBake.Find</c>/<c>FindMesh</c> resolution the
        /// loop used. The WORDING is not rebuilt: every line still comes out of <see cref="Run"/>.
        ///
        /// The census is PER TARGET and never <c>BundleLive.Holds</c>, which passes on ONE matching claim
        /// (design:388): for each declared bundle the standing claim has to exist, be this project's and
        /// point at this project's own copy.
        /// </summary>
        internal static LifecycleState.StageReport Verify(ContentProject p, string patchedDir,
                                                          StringBuilder log)
        {
            List<string> declared = ProjectBake.Bundles(p);
            int served = 0, failed = 0;
            bool mandatoryVoid = false;
            string voidLine = null;

            foreach (string bundleFile in declared)
            {
                string shipped = BakeSelfCheck.ShippedBundlePath(bundleFile);
                string copy = Path.Combine(patchedDir, bundleFile);
                if (!File.Exists(copy))
                {
                    // NAMED, NOT MEASURED. This is the shortfall S6 words; running a gate over a missing
                    // file would report FAIL for an absence, which is the one thing the carrier exists to
                    // keep apart.
                    log.AppendLine("VERIFY " + bundleFile + ": no patched copy at " + copy +
                                   " - this project serves no copy of that target.");
                    continue;
                }

                List<ImportedTexture> want = new List<ImportedTexture>();
                List<KeyValuePair<string, string>> mats = new List<KeyValuePair<string, string>>();
                List<KeyValuePair<string, ImportedMesh>> meshes = new List<KeyValuePair<string, ImportedMesh>>();
                List<KeyValuePair<string, ShippedReplacement>> clips =
                    new List<KeyValuePair<string, ShippedReplacement>>();
                int textureRows = 0;
                foreach (ShippedReplacement r in Rows(p, bundleFile))
                {
                    if (!string.IsNullOrEmpty(r.material))
                    {
                        // The bake stores the NORMALISED value (ProjectBake.cs:1772), which is what P3
                        // looks for in the property block - the author's own spelling would not match.
                        string[] kv = r.material.Split('=');
                        float v;
                        if (kv.Length == 2 && float.TryParse(kv[1], NumberStyles.Float,
                                                             CultureInfo.InvariantCulture, out v))
                            mats.Add(new KeyValuePair<string, string>(
                                r.asset, kv[0] + "=" + v.ToString(CultureInfo.InvariantCulture)));
                        continue;
                    }
                    if (!string.IsNullOrEmpty(r.clip))
                    {
                        clips.Add(new KeyValuePair<string, ShippedReplacement>(r.asset, r));
                        continue;
                    }
                    if (!string.IsNullOrEmpty(r.mesh))
                    {
                        ImportedMesh im = ProjectBake.FindMesh(p, r.mesh);
                        if (im != null) meshes.Add(new KeyValuePair<string, ImportedMesh>(r.asset, im));
                        continue;
                    }
                    textureRows++;
                    ImportedTexture t = ProjectBake.Find(p, r.texture);
                    if (t != null) { want.Add(t); continue; }
                    // A SOURCE THAT DID NOT IMPORT IS MISSING EVIDENCE, NOT ONE FEWER EXPECTATION. Dropping
                    // the row left P1 measuring only the textures that DID resolve - and P1 is keyed on the
                    // BUNDLE (ReadBackResult.MandatoryVoid), so one resolved sibling proved the whole
                    // bundle and this row rode to PASS on a measurement nothing made about it. Recorded
                    // BEFORE the bundle-level gates run, so the sentence the verdict quotes is the one that
                    // names the texture rather than a gate that answered about something else.
                    mandatoryVoid = true;
                    string unresolved = "P1 VOID '" + r.texture + "' -> '" + r.asset + "' in " + bundleFile +
                                        " did not import, so nothing measured it";
                    log.AppendLine(unresolved);
                    if (voidLine == null) voidLine = unresolved;
                }

                ReadBackResult measured =
                    Run(log, bundleFile, shipped, copy, want, mats, meshes, clips, textureRows);
                failed += measured.Failed;

                foreach (ShippedReplacement r in Rows(p, bundleFile))
                {
                    // A clip row has no mandatory gate - P7 is not in ReadBackResult's three lists
                    // (StageResult.cs:103-:105), and asking for one would invent a proof nothing measures.
                    if (!string.IsNullOrEmpty(r.clip)) continue;
                    RowKind kind = !string.IsNullOrEmpty(r.material) ? RowKind.Material
                                 : !string.IsNullOrEmpty(r.mesh) ? RowKind.Mesh : RowKind.Texture;
                    if (!measured.MandatoryVoid(r.asset, kind, bundleFile)) continue;
                    mandatoryVoid = true;
                    if (voidLine != null) continue;
                    // BY GATE AND TARGET. A scan by target alone returned the optional `P4-ctl-shipped`
                    // WARN recorded under the same mesh (:250) ahead of the mandatory `P4-bytes` VOID, so
                    // the row blamed a control nobody has to act on for a proof that never ran.
                    voidLine = measured.FirstMandatoryVoid(r.asset, kind, bundleFile);
                    if (voidLine != null) continue;
                    // The gate did not run AT ALL, so there is no line of its own to quote. Verify is the
                    // producer of this one and writes it to the log too, so the row and the log still
                    // quote the same sentence.
                    voidLine = "VERIFY VOID '" + r.asset + "' in " + bundleFile + " has no measurement " +
                               "for a gate its row requires - nothing proves this target.";
                    log.AppendLine(voidLine);
                }

                // THE PER-TARGET CLAIM CENSUS. `BundleLive.Holds` answers true on one matching claim over
                // the whole mod, so a project serving one of its two targets would read as served.
                BundleClaim claim = BundleClaims.Find(bundleFile);
                if (claim != null && string.Equals(claim.Mod, p.Id, StringComparison.Ordinal) &&
                    Same(claim.Path, copy)) served++;
                else
                    log.AppendLine("VERIFY " + bundleFile + ": the live claim is " +
                                   (claim == null ? "held by nobody" : "'" + claim.Mod + "' -> " + claim.Path) +
                                   ", not this project's " + copy + ".");
            }

            return LifecycleState.VerifyVerdict(p.Id, served, declared.Count, mandatoryVoid, voidLine, failed);
        }

        /// <summary>This project's non-video rows for one bundle, in declaration order - the same filter
        /// the patch loop applies (ProjectBake.cs:1750-:1752).</summary>
        private static IEnumerable<ShippedReplacement> Rows(ContentProject p, string bundleFile)
        {
            foreach (ShippedReplacement r in p.Replace)
            {
                if (!string.IsNullOrEmpty(r.video)) continue;
                if (!string.Equals(r.bundle, bundleFile, StringComparison.OrdinalIgnoreCase)) continue;
                yield return r;
            }
        }

        /// <summary>One spelling of a file path, so a claim's and ours can be compared - the claim is
        /// recorded with whatever separators the installer handed it.</summary>
        private static bool Same(string a, string b)
        {
            try { return string.Equals(Path.GetFullPath(a), Path.GetFullPath(b),
                                       StringComparison.OrdinalIgnoreCase); }
            catch (Exception) { return false; }
        }

        /// <summary>THIS PRODUCER NEVER CARRIES A TERMINAL LINE - it returns one result PER BUNDLE, and the
        /// run's terminal sentence is composed once, after every bundle and after the gates that are not
        /// read-back at all, at ProjectBake.cs:402 (S4/S5). There is nothing to thread in: the line does not
        /// exist yet when this returns, and the sole caller adds `.Failed` to a running count
        /// (ProjectBake.cs:1657) rather than holding the result. <see cref="ReadBackResult.Terminal"/> is
        /// therefore <c>null</c> here, which is what its own factory demands be said explicitly.</summary>
        /// <param name="textureRows">how many texture rows this bundle DECLARES, when the caller knows -
        /// the bake does not need to say (an unresolved source is a counted P1 REFUSED there,
        /// ProjectBake.cs:1835), and Verify does: "0 declared" is untrue of a bundle whose rows all failed
        /// to import, which is exactly the case the row above is about.</param>
        internal static ReadBackResult Run(StringBuilder log, string bundleFile, string shipped, string copy,
                                           List<ImportedTexture> want,
                                           List<KeyValuePair<string, string>> mats,
                                           List<KeyValuePair<string, ImportedMesh>> meshes,
                                           List<KeyValuePair<string, ShippedReplacement>> clips,
                                           int textureRows = 0)
        {
            List<GateEntry> entries = new List<GateEntry>();

            // Read the pixels back out of the COPY, and check the shipped original still reads its
            // own bytes - the control that says we patched a copy and not the player's game.
            // With no texture declared for THIS bundle both arms are vacuous - and the control
            // arm is vacuously FALSE, which reported a failure on a perfectly correct mesh-only
            // bundle (ct_project 14:5x). A gate that cannot answer says VOID, never PASS or FAIL.
            if (want.Count == 0)
                Void(log, entries, "P1", bundleFile,
                     textureRows > 0
                     ? "P1 VOID " + textureRows + " texture replacement(s) declared in " + bundleFile +
                       ", 0 of them resolved to a source - nothing was measured"
                     : "P1 VOID 0 texture replacement(s) declared in " + bundleFile);
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

            foreach (KeyValuePair<string, ShippedReplacement> c in clips)
            {
                // BAKE pre-validates this row (ProjectBake.cs:1576) and never reaches here with a bad one,
                // but Verify reads the rows off DISK and does: a malformed "clip" left attribute=0 and
                // factor=0, so both arms below measured nothing at all while reporting VOID as though the
                // clip simply bound no curve. The refusal is the answer, and it is the parser's own words.
                uint attribute; float k;
                string why = ProjectBake.ParseClipEdit(c.Value.clip, out attribute, out k);
                if (why != null)
                {
                    Void(log, entries, "P7", c.Key,
                         "P7 VOID clip '" + c.Key + "' \"" + c.Value.clip + "\" " + why +
                         ", so there is no edit to read back");
                    continue;
                }
                int at = log.Length;
                // ONE failure count, not two. Both arms already RETURN what they counted; the entries are
                // sliced back off the lines they wrote, and the two must agree or one of them is lying.
                int counted = ProjectBake.Curves(log, c.Key, c.Value.clip, attribute, k, shipped, copy) +
                              ProjectBake.SampleClip(log, c.Key, attribute, k, shipped, copy);
                GateEntry.SelfCheck(log, entries, c.Key, counted, Clips(log, entries, c.Key, at));
            }

            return ReadBackResult.Of(null, entries.ToArray());   // no terminal line: see the note on Run
        }

        /// <summary>The clip arms stay where they are - `Curves` would have to be RENAMED to move (there is
        /// an unrelated `ProjectBake.Curves` at :1063) and they compose their lines through the same
        /// <see cref="ProjectBake.Check"/> - so their outcomes are read back off the lines they just
        /// appended. The producer's own second token IS the classification; nothing here re-decides a
        /// verdict, and the counted failures are the " FAIL " lines those same Check calls returned 1 for.
        ///
        /// ponytail: one line per arm, which is what Check and every P7 VOID arm write today. An arm that
        /// ever printed a detail containing a newline would need this to slice by Check's return instead.
        ///
        /// Returns the number of FAIL entries it made, so the caller can hold it against what the arms
        /// themselves returned - the two are the same fact and a mismatch means the slicing has stopped
        /// matching the producer.</summary>
        private static int Clips(StringBuilder log, List<GateEntry> entries, string target, int at)
        {
            int failed = 0;
            foreach (string line in log.ToString(at, log.Length - at)
                                       .Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries))
            {
                string[] token = line.Split(' ');
                if (token.Length < 2) continue;
                if (token[1] == "PASS") entries.Add(GateEntry.Pass(token[0], target, line));
                else if (token[1] == "FAIL") { entries.Add(GateEntry.Fail(token[0], target, line)); failed++; }
                else if (token[1] == "VOID") entries.Add(GateEntry.Void(token[0], target, line));
            }
            return failed;
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
