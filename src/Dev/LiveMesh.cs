using System;
using System.IO;
using System.Text;
using Morgott.ContentTool.Import;
using UnityEngine;

namespace Morgott.ContentTool.Dev
{
    /// <summary>
    /// Task 25 - the MESH half of the developer workbench: an author's .glb/.obj becomes a live
    /// UnityEngine.Mesh, so `ct_replace` can write a model into a renderer that is already on screen
    /// and the author never restarts the game to look at it.
    ///
    /// Nothing here parses a model. The two importers the bake path already uses do that
    /// (<see cref="GlbReader"/> + <see cref="ModelBuild"/>, <see cref="ObjCodec"/> +
    /// <see cref="MeshBuild"/>), both of them free of UnityEngine types, and this only decodes the
    /// <see cref="BakedMesh"/> buffers they hand back into the arrays a live Mesh wants. The live
    /// preview and the shipped bake therefore read the SAME vertices - a preview built by a second
    /// importer would be a different mesh that merely looks similar.
    /// </summary>
    internal static class LiveMesh
    {
        /// <summary>The file types that name a MESH slot; `ct_replace` picks the arm from this.</summary>
        internal static bool IsModel(string ext)
        {
            return string.Equals(ext, ".glb", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(ext, ".obj", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// A live Mesh from the author's file, or null and a sentence saying why. A .glb also hands
        /// back the model it came out of - <see cref="Bind"/> needs the file's OWN joint names and
        /// WEIGHTS_0, which the BakedMesh buffers do not carry.
        /// </summary>
        internal static Mesh Load(string file, out string why, out SkinnedModel model)
        {
            why = null;
            model = null;
            string ext = Path.GetExtension(file);
            if (!IsModel(ext)) { why = "'" + file + "' is not a .glb or .obj"; return null; }
            try
            {
                BakedMesh baked;
                if (string.Equals(ext, ".glb", StringComparison.OrdinalIgnoreCase))
                {
                    ReplacementSource source = GlbSource.ReadReplacement(File.ReadAllBytes(file), file);
                    model = source.Model;
                    // NEVER SILENT: a bone the game renamed for the author is exactly the kind of help
                    // that becomes a mystery when it is not said out loud.
                    if (source.AliasLog != null)
                        ContentToolMain.Say("ct_replace: applied " + source.AliasLog);
                    else if (source.SidecarRefusal != null)
                        ContentToolMain.Say("ct_replace: " + source.SidecarRefusal);
                    baked = ModelBuild.From(model, Path.GetFileNameWithoutExtension(file)).Mesh;
                }
                else baked = MeshBuild.From(ObjCodec.Parse(File.ReadAllText(file)));
                return ToMesh(baked, Path.GetFileName(file));
            }
            catch (Exception ex)
            {
                // The importers refuse by sentence (missing normals, no faces, a broken armature) and
                // that sentence is the author's whole diagnosis - it must not be swallowed into "no".
                why = "'" + Path.GetFileName(file) + "' did not import: " + ex.Message;
                return null;
            }
        }

        /// <summary>BakedMesh buffers -&gt; the arrays a live Mesh wants. Stride and layout are BakedMesh's.</summary>
        private static Mesh ToMesh(BakedMesh b, string name)
        {
            int n = b.VertexCount;
            Vector3[] pos = new Vector3[n], nor = new Vector3[n];
            Vector2[] uv = new Vector2[n];
            for (int i = 0; i < n; i++)
            {
                int at = i * BakedMesh.Stride;
                pos[i] = new Vector3(BitConverter.ToSingle(b.VertexData, at),
                                     BitConverter.ToSingle(b.VertexData, at + 4),
                                     BitConverter.ToSingle(b.VertexData, at + 8));
                nor[i] = new Vector3(BitConverter.ToSingle(b.VertexData, at + 12),
                                     BitConverter.ToSingle(b.VertexData, at + 16),
                                     BitConverter.ToSingle(b.VertexData, at + 20));
                uv[i] = new Vector2(BitConverter.ToSingle(b.VertexData, at + 24),
                                    BitConverter.ToSingle(b.VertexData, at + 28));
            }

            int[] tri = new int[b.IndexCount];
            for (int i = 0; i < b.IndexCount; i++)
                tri[i] = b.Index32 ? (int)BitConverter.ToUInt32(b.IndexData, i * 4)
                                   : BitConverter.ToUInt16(b.IndexData, i * 2);

            Mesh m = new Mesh { name = name };
            if (b.Index32) m.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            m.vertices = pos;
            m.normals = nor;
            m.uv = uv;
            m.triangles = tri;
            m.RecalculateBounds();
            return m;
        }

        /// <summary>
        /// The runtime twin of <see cref="Morgott.ContentTool.Bake.SkinFields.Rebind"/> - SAME rule,
        /// different medium. Rebind writes serialized fields (AssetTypeValueField) and cannot be
        /// called on a live UnityEngine.Mesh at all, but the decision it makes is the one repeated
        /// here: the target's own bind poses are kept EXACTLY as they are, so they stay in step with
        /// the SkinnedMeshRenderer's m_Bones list which this never touches, and each new vertex goes
        /// WHOLE to the bone whose bind pose brings it closest to that bone's own origin.
        ///
        /// Returns null when the target is not rigged (nothing to bind), otherwise one line naming
        /// the bone count and where the bind poses came from - the two things that decide whether the
        /// swap deforms with the skeleton or hangs off it.
        ///
        /// A .glb whose joints NAME the renderer's own bones takes the other path entirely - see
        /// <see cref="ByName"/> - and keeps its own WEIGHTS_0. Nearest-bone is what is left when the
        /// file cannot be matched (an .obj always, a .glb whose armature is somebody else's).
        ///
        /// ponytail: one full-weight influence per vertex, exactly Rebind's ceiling - a joint creases
        /// instead of bending.
        /// </summary>
        internal static string Bind(Mesh ours, SkinnedMeshRenderer smr, SkinnedModel file = null)
        {
            Transform[] bones = smr == null ? null : smr.bones;
            if (bones == null || bones.Length == 0) return null;

            string source;
            Matrix4x4[] poses = Poses(smr, bones, out source);

            string refused = null;
            if (file != null && file.JointNames.Count > 0)
            {
                string byName = ByName(ours, bones, file, poses, source, out refused);
                if (byName != null) return byName;
            }

            Vector3[] verts = ours.vertices;
            BoneWeight[] weights = new BoneWeight[verts.Length];
            for (int i = 0; i < verts.Length; i++)
            {
                int best = 0;
                float bestD = float.MaxValue;
                for (int b = 0; b < poses.Length; b++)
                {
                    Vector3 v = poses[b].MultiplyPoint3x4(verts[i]);
                    float d = v.sqrMagnitude;
                    if (d >= bestD) continue;
                    bestD = d; best = b;
                }
                weights[i].boneIndex0 = best;
                weights[i].weight0 = 1f;
            }

            ours.bindposes = poses;
            ours.boneWeights = weights;
            return "skinned to the target's own " + bones.Length + " bones (bind poses from " + source +
                   ", one full-weight influence per vertex" +
                   (refused == null ? "" : "; the file's own weights were NOT used: " + refused) + ")";
        }

        /// <summary>
        /// The TARGET's own bind poses, and where they were read from - the shipped mesh when it will
        /// hand them over, the live skeleton otherwise (a bind pose IS the renderer-space -&gt; bone-space
        /// transform). BOTH binding paths bind against these, so neither can move the skeleton.
        ///
        /// ponytail: the skeleton fallback is taken at the pose the rig is in RIGHT NOW, so a swap made
        /// while a character is mid-animation binds to that pose. Upgrade path: read the bind poses off
        /// the shipped file through the bake-side reader instead of off the live skeleton.
        /// </summary>
        private static Matrix4x4[] Poses(SkinnedMeshRenderer smr, Transform[] bones, out string source)
        {
            source = "the shipped mesh";
            try
            {
                Mesh shipped = smr.sharedMesh;
                Matrix4x4[] p = shipped == null ? null : shipped.bindposes;
                if (p != null && p.Length == bones.Length) return p;
            }
            catch (Exception) { }

            source = "the live skeleton";
            var poses = new Matrix4x4[bones.Length];
            Matrix4x4 root = smr.transform.localToWorldMatrix;
            for (int b = 0; b < bones.Length; b++)
                poses[b] = bones[b] == null ? Matrix4x4.identity : bones[b].worldToLocalMatrix * root;
            return poses;
        }

        /// <summary>
        /// The file's OWN skin, onto the live rig, matched BY BONE NAME - ported from Resource
        /// Replacer (ResourceReplacer\pp-native\src\GlbReader.cs:1119-1201, whose SkinBinder is
        /// already in this tree at <see cref="SkinBinder"/> and until now had no caller).
        ///
        /// The skeleton is never touched: <see cref="SkinBinder.Bind"/> is a strict bijection between
        /// the file's joint names and <c>smr.bones[i].name</c>, so joint ORDER is free while
        /// membership is not, and it hands back the vertex joints already in the live rig's order.
        /// That is the whole difference from nearest-bone - a vertex the file splits between two bones
        /// stays split, so a joint bends instead of creasing.
        ///
        /// The bind poses are the TARGET's, exactly as the packaged path does it
        /// (<see cref="Morgott.ContentTool.Bake.SkinFields.RebindByName"/>, which reads the shipped
        /// m_BindPose and drops the file's): they have to stay in step with the m_Bones list this never
        /// touches. Binding the FILE's inverse bind matrices onto the game's own untouched skeleton is
        /// what made a preview stretch and throw a hand to the centre while the shipped bake of the very
        /// same file was right.
        ///
        /// Returns null (and a sentence in <paramref name="refused"/>) when the file and the rig do
        /// not correspond, which is the normal case for a model that was never retargeted onto this
        /// skeleton; the caller then falls back and SAYS SO rather than silently binding badly.
        /// </summary>
        private static string ByName(Mesh ours, Transform[] bones, SkinnedModel file, Matrix4x4[] poses,
                                     string source, out string refused)
        {
            refused = null;
            try
            {
                string[] names = new string[bones.Length];
                for (int b = 0; b < bones.Length; b++) names[b] = bones[b] == null ? "" : bones[b].name;

                ushort[] joints;
                float[][] unused;
                // 0 material slots and no blend shapes: this preview merges every glTF primitive into
                // one submesh (ToMesh) and reads no morphs, so those two checks would be asking about
                // something the preview does not build.
                SkinBinder.Bind(file, names, 0, null, out joints, out unused);

                // SkinBinder checks the file against ITSELF and against the rig; this is the one
                // thing only the caller can check - that the mesh built out of the file still has a
                // vertex per file vertex, which is what makes weights[i] and file.Weights[i] the
                // same vertex. ModelBuild.From welds nothing today, and an import that started to
                // would corrupt every weight silently rather than refuse.
                int n = ours.vertexCount;
                if (file.Positions == null || file.Positions.Length != n)
                    throw new FormatException("the file's skin covers " +
                        (file.Positions == null ? 0 : file.Positions.Length) + " vertices but the " +
                        "imported mesh has " + n);

                BoneWeight[] weights = new BoneWeight[n];
                float split = 0f; int splitAt = -1;
                for (int i = 0; i < n; i++)
                {
                    float w0 = file.Weights[i * 4], w1 = file.Weights[i * 4 + 1];
                    float w2 = file.Weights[i * 4 + 2], w3 = file.Weights[i * 4 + 3];
                    float sum = w0 + w1 + w2 + w3;
                    if (sum <= 0f) { w0 = 1f; sum = 1f; }
                    weights[i].boneIndex0 = joints[i * 4]; weights[i].weight0 = w0 / sum;
                    weights[i].boneIndex1 = joints[i * 4 + 1]; weights[i].weight1 = w1 / sum;
                    weights[i].boneIndex2 = joints[i * 4 + 2]; weights[i].weight2 = w2 / sum;
                    weights[i].boneIndex3 = joints[i * 4 + 3]; weights[i].weight3 = w3 / sum;
                    // The reported number is a SHARED vertex - a fraction strictly between 0 and 1 is
                    // the one thing nearest-bone cannot produce, so the log line says which path ran.
                    if (splitAt < 0 && weights[i].weight0 > 0.01f && weights[i].weight0 < 0.99f)
                    { splitAt = i; split = weights[i].weight0; }
                }

                ours.bindposes = poses;
                ours.boneWeights = weights;
                return "skinned BY NAME onto the target's own " + bones.Length +
                       " bones, carrying the file's own weights (bind poses from " + source + ", " +
                       file.JointNames.Count + " joints matched, order remapped" +
                       (splitAt < 0 ? ", no shared vertex in this file"
                                    : "; vertex " + splitAt + " is shared, weight0=" + ModelBuild.F(split)) + ")";
            }
            catch (Exception ex)
            {
                refused = ex.Message;
                return null;
            }
        }

        // ------------------------------------------------------------------------------- gate R5

        /// <summary>
        /// Gate R5 (Task 25), one run, through the REAL `ct_replace`, on a renderer that is already on
        /// screen and never reloaded:
        ///   R5-mesh       the renderer wears the SAMPLE .glb's own identity - 6 vertices, 9 indices,
        ///                 the file's name - not "something changed" and not "it did not throw";
        ///   R5-skin       on a rigged target: our mesh carries the target's own bone count and every
        ///                 weight indexes a bone that exists (VOID, never PASS, on a static target);
        ///   R5-revert     ct_revert puts the origin Mesh OBJECT back, by reference;
        ///   R5-material   a material property named by a target path changes on a CLONE, shader
        ///                 preserved, the game's own material object untouched;
        ///   R5-mat-revert the origin material ARRAY entry back, by reference;
        ///   R5-CONTROL    a second live renderer, named by nothing, compared by reference throughout.
        /// Anything the live scene cannot supply is VOID, never PASS.
        /// </summary>
        internal static string Gate()
        {
            bool scan = DevScan.Enabled;
            try { return RunGate(); }
            finally { DevScan.Enabled = scan; SeamSwap.Revert(); }
        }

        private static string RunGate()
        {
            if (SeamSwap.MarkCount > 0)
                return "R5 VOID - " + SeamSwap.MarkCount + " swap(s) are already live, ct_revert first";

            System.Collections.Generic.Dictionary<string, int> counts =
                new System.Collections.Generic.Dictionary<string, int>(StringComparer.Ordinal);
            foreach (GameObject go in Resources.FindObjectsOfTypeAll<GameObject>())
                if (go.scene.IsValid())
                {
                    int c;
                    counts[go.name] = counts.TryGetValue(go.name, out c) ? c + 1 : 1;
                }

            Renderer subject = null, control = null;
            SkinnedMeshRenderer skinned = null;
            MeshFilter filter = null;
            foreach (Renderer r in Resources.FindObjectsOfTypeAll<Renderer>())
            {
                if (!r.gameObject.scene.IsValid()) continue;
                SkinnedMeshRenderer smr = r as SkinnedMeshRenderer;
                MeshFilter mf = smr != null ? null : r.GetComponent<MeshFilter>();
                if ((smr == null || smr.sharedMesh == null) && (mf == null || mf.sharedMesh == null)) continue;

                int c;
                // The name must be UNIQUE (that is what name: resolves by) and typeable: the console
                // splits its arguments on whitespace.
                bool unique = counts.TryGetValue(r.gameObject.name, out c) && c == 1 &&
                              r.gameObject.name.IndexOf(' ') < 0 && r.gameObject.name.IndexOf('@') < 0;
                // A RIGGED subject is what the gate is really after - it is the case the skeleton can
                // break - so a skinned candidate replaces a static one already picked.
                if (unique && (subject == null || (skinned == null && smr != null)))
                {
                    subject = r; skinned = smr; filter = mf;
                    continue;
                }
                if (control == null && !ReferenceEquals(r, subject)) control = r;
            }

            if (subject == null) return "R5 VOID - no live renderer has a uniquely named GameObject with a mesh (scanned " + counts.Count + " distinct names)";
            if (control == null) return "R5 VOID - only one live renderer with a mesh; the gate REFUSES to run without its control";

            string glb = WriteSampleGlb();
            if (glb == null) return "R5 VOID - could not write the sample .glb";

            Mesh originMesh = skinned != null ? skinned.sharedMesh : filter.sharedMesh;
            Mesh controlMesh = ControlMesh(control);
            Material[] originMats = subject.sharedMaterials;
            Material[] controlMats = control.sharedMaterials;
            if (controlMesh == null) return "R5 VOID - the control renderer carries no mesh to compare by reference";

            string component = skinned != null ? "SkinnedMeshRenderer" : "MeshFilter";
            string target = "name:" + subject.gameObject.name + "#@" + component + ".mesh";

            StringBuilder log = new StringBuilder();
            int failures = 0;
            log.AppendLine("ct_liveswap subject='" + subject.gameObject.name + "' (" + component +
                           ", unique) origin mesh='" + originMesh.name + "' verts=" + originMesh.vertexCount +
                           " bones=" + (skinned == null ? 0 : skinned.bones.Length));
            log.AppendLine("  CONTROL renderer='" + control.gameObject.name + "' mesh='" + controlMesh.name +
                           "'; replacement=" + glb + " (the sample rig: 6 vertices, 9 indices)");

            DevScan.Enabled = true;
            string applied = SeamSwap.Replace(new[] { target, glb });
            log.AppendLine("  " + First(applied));

            Mesh now = skinned != null ? skinned.sharedMesh : filter.sharedMesh;
            failures += Check(log, "R5-mesh",
                applied.StartsWith("ct_replace applied", StringComparison.Ordinal) && now != null &&
                !ReferenceEquals(now, originMesh) && now.vertexCount == 6 && now.triangles.Length == 9 &&
                now.name == "ct_live_gate.glb",
                "the renderer wears '" + (now == null ? "(null)" : now.name) + "' verts=" +
                (now == null ? -1 : now.vertexCount) + " indices=" + (now == null ? -1 : now.triangles.Length) +
                " bounds=" + (now == null ? default(Bounds).size : now.bounds.size));

            if (skinned == null || skinned.bones.Length == 0)
                log.AppendLine("R5-skin VOID - the subject is not rigged, so the skinned arm could not run in this scene");
            else if (now == null)
                log.AppendLine("R5-skin VOID - nothing was written, so there is no binding to measure");
            else
            {
                Matrix4x4[] poses = now.bindposes;
                BoneWeight[] w = now.boneWeights;
                int max = -1;
                foreach (BoneWeight b in w) if (b.boneIndex0 > max) max = b.boneIndex0;
                // Measured on the mesh WE wrote, by the file's own vertex count: the shipped mesh hands
                // its own bindposes and weights over quite happily, so an arm that only asked "are
                // there bindposes" would have passed on a run where nothing was written at all.
                failures += Check(log, "R5-skin",
                    !ReferenceEquals(now, originMesh) && now.vertexCount == 6 &&
                    poses != null && poses.Length == skinned.bones.Length && w != null && w.Length == now.vertexCount &&
                    max >= 0 && max < poses.Length,
                    "ours=" + !ReferenceEquals(now, originMesh) + " verts=" + now.vertexCount +
                    " bindposes=" + (poses == null ? -1 : poses.Length) + " bones=" + skinned.bones.Length +
                    " weights=" + (w == null ? -1 : w.Length) + " boneMax=" + max +
                    " inRange=" + (poses != null && max >= 0 && max < poses.Length ? "yes" : "no") +
                    " localBounds=" + skinned.localBounds.size);
            }

            failures += Check(log, "R5-CONTROL-during",
                ReferenceEquals(ControlMesh(control), controlMesh) && Same(control.sharedMaterials, controlMats),
                "the control still wears '" + controlMesh.name + "' and its own materials while the swap is live");

            log.AppendLine(SeamSwap.Revert());
            Mesh back = skinned != null ? skinned.sharedMesh : filter.sharedMesh;
            failures += Check(log, "R5-revert", ReferenceEquals(back, originMesh),
                "sharedMesh == the origin Mesh OBJECT again: " + ReferenceEquals(back, originMesh));

            // ---- R6: the file's OWN weights, remapped by bone NAME, measured by what MOVES
            if (skinned == null) log.AppendLine("R6 VOID - the subject is not rigged, so no skin can be remapped");
            else failures += Remap(log, skinned, target);

            // ---- the material arm, same subject, now that its key is free again
            if (originMats.Length == 0 || originMats[0] == null || !originMats[0].HasProperty("_Color"))
                log.AppendLine("R5-material VOID - the subject's materials[0] declares no _Color to write");
            else
            {
                string shader = originMats[0].shader.name;
                string matTarget = "name:" + subject.gameObject.name + "#@Renderer.materials[0].col:_Color";
                string mat = SeamSwap.Replace(new[] { matTarget, "1,0,1,1" });
                log.AppendLine("  " + First(mat));
                Material shown = subject.sharedMaterials[0];
                failures += Check(log, "R5-material",
                    mat.StartsWith("ct_replace applied", StringComparison.Ordinal) && shown != null &&
                    !ReferenceEquals(shown, originMats[0]) && shown.shader.name == shader &&
                    shown.GetColor("_Color") == new Color(1f, 0f, 1f, 1f) &&
                    originMats[0].GetColor("_Color") != new Color(1f, 0f, 1f, 1f),
                    "materials[0] is '" + (shown == null ? "(null)" : shown.name) + "' shader='" +
                    (shown == null ? "?" : shown.shader.name) + "' _Color=" +
                    (shown == null ? default(Color) : shown.GetColor("_Color")) +
                    " (the game's own material still reads " + originMats[0].GetColor("_Color") + ")");

                log.AppendLine(SeamSwap.Revert());
                failures += Check(log, "R5-mat-revert", ReferenceEquals(subject.sharedMaterials[0], originMats[0]),
                    "sharedMaterials[0] == the origin Material OBJECT again: " +
                    ReferenceEquals(subject.sharedMaterials[0], originMats[0]));
            }

            failures += Check(log, "R5-CONTROL-after",
                ReferenceEquals(ControlMesh(control), controlMesh) && Same(control.sharedMaterials, controlMats),
                "the control is unchanged throughout");

            log.Append(failures == 0
                ? "ct_liveswap: R5 PASS (a model and a material property land on a renderer that is already on screen, and come back off it)"
                : "ct_liveswap: " + failures + " FAILURE(S)");
            return log.ToString();
        }

        /// <summary>How far R6 lifts a bone, in that bone's own parent space.</summary>
        private const float RemapLift = 0.25f;

        /// <summary>
        /// R6 - the file's own WEIGHTS_0 land on a LIVE rig, matched by bone NAME and not by slot.
        ///
        /// The fixture is written FROM the subject: three vertices, and joints that are the renderer's
        /// own bone names IN REVERSE ORDER, so the file's slot for a bone is never the live index
        /// except at the middle. Vertex 0 belongs wholly to bone A, vertex 2 wholly to bone B, and
        /// vertex 1 is split HALF and HALF between them - a number no nearest-bone synthesis can
        /// produce, since that gives every vertex one full-weight influence.
        ///
        /// The oracle is then the EFFECT: bone A is lifted and the skin is BAKED, so what is asserted
        /// is where each vertex ENDED UP. Unity skins with sum(weight * boneMatrix), so the lift must
        /// move vertex 0 by all of L, vertex 1 by exactly HALF of L, and vertex 2 by nothing at all.
        /// A binding that took the file's joint slots as live indices moves a different bone entirely
        /// and every one of those three numbers comes out wrong.
        /// </summary>
        private static int Remap(StringBuilder log, SkinnedMeshRenderer smr, string target)
        {
            Transform[] bones = smr.bones;
            System.Collections.Generic.HashSet<string> names =
                new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal);
            foreach (Transform t in bones)
            {
                if (t == null || string.IsNullOrEmpty(t.name) || !names.Add(t.name))
                {
                    log.AppendLine("R6 VOID - the subject's skeleton has an unnamed or repeated bone, " +
                                   "so nothing in a file can be matched to it by name");
                    return 0;
                }
            }

            int a = -1, bone2 = -1, n = bones.Length;
            for (int i = 0; i < n && bone2 < 0; i++)
            {
                if (i == n - 1 - i) continue;                       // the reversal must MOVE this slot
                for (int j = i + 1; j < n; j++)
                {
                    if (j == n - 1 - j) continue;
                    // Lifting a bone lifts everything it carries, so a pair where one is the other's
                    // ancestor would move the vertex the arm expects to stay still.
                    if (bones[i].IsChildOf(bones[j]) || bones[j].IsChildOf(bones[i])) continue;
                    a = i; bone2 = j; break;
                }
            }
            if (bone2 < 0)
            {
                log.AppendLine("R6 VOID - the subject's " + n + " bone(s) hold no two independent bones " +
                               "whose slots a reversal moves, so a remap cannot be told from an index binding");
                return 0;
            }

            string path;
            try
            {
                path = Path.Combine(Path.Combine(Application.persistentDataPath, "ContentTool"), "ct_live_remap.glb");
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllBytes(path, RemapGlb(smr, a, bone2));
            }
            catch (Exception ex) { log.AppendLine("R6 VOID - the fixture could not be written: " + ex.Message); return 0; }

            int failures = 0;
            string applied = SeamSwap.Replace(new[] { target, path });
            log.AppendLine("  " + First(applied));
            log.AppendLine("  R6 fixture: joints are the subject's " + n + " bone names REVERSED; vertex 0 -> '" +
                           bones[a].name + "' (file slot " + (n - 1 - a) + ", live " + a + "), vertex 2 -> '" +
                           bones[bone2].name + "' (file slot " + (n - 1 - bone2) + ", live " + bone2 +
                           "), vertex 1 half of each");

            Mesh now = smr.sharedMesh;
            if (!applied.StartsWith("ct_replace applied", StringComparison.Ordinal) || now == null || now.vertexCount != 3)
            {
                failures += Check(log, "R6", false, "the fixture did not land: mesh='" +
                    (now == null ? "(null)" : now.name) + "' verts=" + (now == null ? -1 : now.vertexCount));
                SeamSwap.Revert();
                return failures;
            }

            failures += Lift(log, "R6-remap", smr, a, new[] { 1f, 0.5f, 0f });
            // The control moves the OTHER bone of the same rig and asserts ITS own three numbers -
            // a second positive identity, mirrored, not a "nothing happened".
            failures += Lift(log, "R6-CONTROL-bone", smr, bone2, new[] { 0f, 0.5f, 1f });
            log.AppendLine(SeamSwap.Revert());
            return failures;
        }

        /// <summary>
        /// Lifts one live bone, bakes the skin before and after, and asserts each vertex moved by its
        /// OWN predicted share of that lift. The lift is not a constant either: it is measured out of
        /// the bone's own parent space into the renderer's, so a rig with any scale on it still states
        /// a number the arm can be wrong about.
        /// </summary>
        private static int Lift(StringBuilder log, string gate, SkinnedMeshRenderer smr, int boneIndex, float[] share)
        {
            Mesh scratch = new Mesh();
            Transform bone = smr.bones[boneIndex];
            Vector3 was = bone.localPosition;
            try
            {
                smr.BakeMesh(scratch);
                Vector3[] rest = scratch.vertices;
                bone.localPosition = was + new Vector3(0f, RemapLift, 0f);
                smr.BakeMesh(scratch);
                Vector3[] moved = scratch.vertices;
                bone.localPosition = was;

                Vector3 world = bone.parent == null
                    ? new Vector3(0f, RemapLift, 0f)
                    : bone.parent.TransformVector(new Vector3(0f, RemapLift, 0f));
                Vector3 lift = smr.transform.InverseTransformVector(world);
                float tol = Math.Max(1e-3f, 0.02f * lift.magnitude);

                bool ok = rest.Length == 3 && moved.Length == 3;
                string got = "", want = "";
                for (int i = 0; i < 3 && ok; i++)
                {
                    Vector3 d = moved[i] - rest[i], w = share[i] * lift;
                    got += (i == 0 ? "" : " ") + "v" + i + "=" + V(d);
                    want += (i == 0 ? "" : " ") + "v" + i + "=" + V(w);
                    if ((d - w).magnitude > tol) ok = false;
                }
                return Check(log, gate, ok, "lifting '" + bone.name + "' (live bone " + boneIndex +
                    ") by " + V(lift) + " in the renderer's space moved " + got +
                    "; the FILE's own weights predict " + want + " (tolerance " + ModelBuild.F(tol) + ")");
            }
            finally { bone.localPosition = was; UnityEngine.Object.Destroy(scratch); }
        }

        private static string V(Vector3 v)
        {
            return "(" + ModelBuild.F(v.x) + "," + ModelBuild.F(v.y) + "," + ModelBuild.F(v.z) + ")";
        }

        /// <summary>
        /// The R6 fixture: a .glb whose armature IS this renderer's, listed in REVERSE order. Its bind
        /// poses are the live skeleton's own (a bind pose IS the renderer-space -&gt; bone-space
        /// transform), so at rest the three vertices render exactly where they are written and any
        /// motion measured afterwards is the skinning and nothing else.
        /// </summary>
        private static byte[] RemapGlb(SkinnedMeshRenderer smr, int a, int b)
        {
            Transform[] bones = smr.bones;
            int n = bones.Length;
            Matrix4x4 root = smr.transform.localToWorldMatrix;

            SkinnedModel m = new SkinnedModel { Name = "ct_live_remap" };
            m.Nodes.Add(new SkinNode { Name = "ct_remap_rig", Parent = -1, Local = Column(Matrix4x4.identity) });
            m.JointNodes = new int[n];
            m.InverseBindMatrices = new float[n][];
            for (int slot = 0; slot < n; slot++)
            {
                Transform live = bones[n - 1 - slot];                 // REVERSED: slot != live index
                Matrix4x4 bind = live.worldToLocalMatrix * root;
                m.InverseBindMatrices[slot] = Column(bind);
                m.Nodes.Add(new SkinNode { Name = live.name, Parent = 0, Local = Column(bind.inverse) });
                m.JointNodes[slot] = slot + 1;
            }

            Vector3 pa = smr.transform.InverseTransformPoint(bones[a].position);
            Vector3 pb = smr.transform.InverseTransformPoint(bones[b].position);
            Vector3 mid = (pa + pb) * 0.5f + new Vector3(0.01f, 0.01f, 0.01f);   // never degenerate
            m.Positions = new[] { new ObjVector3(pa.x, pa.y, pa.z), new ObjVector3(mid.x, mid.y, mid.z),
                                  new ObjVector3(pb.x, pb.y, pb.z) };
            m.Normals = new[] { new ObjVector3(0f, 0f, -1f), new ObjVector3(0f, 0f, -1f), new ObjVector3(0f, 0f, -1f) };
            m.Uv0 = new[] { new ObjVector2(0f, 0f), new ObjVector2(0.5f, 1f), new ObjVector2(1f, 0f) };
            m.Submeshes.Add(new[] { 0, 1, 2 });
            m.Materials.Add("ct_remap");

            ushort ja = (ushort)(n - 1 - a), jb = (ushort)(n - 1 - b);
            m.Joints = new ushort[] { ja, 0, 0, 0,  ja, jb, 0, 0,  jb, 0, 0, 0 };
            m.Weights = new[] { 1f, 0f, 0f, 0f,  0.5f, 0.5f, 0f, 0f,  1f, 0f, 0f, 0f };
            return GlbCodec.Write(m);
        }

        /// <summary>A Unity matrix as glTF states it: column-major float[16], index = col*4 + row.</summary>
        private static float[] Column(Matrix4x4 m)
        {
            float[] f = new float[16];
            for (int col = 0; col < 4; col++)
                for (int row = 0; row < 4; row++) f[col * 4 + row] = m[row, col];
            return f;
        }

        private static Mesh ControlMesh(Renderer r)
        {
            SkinnedMeshRenderer smr = r as SkinnedMeshRenderer;
            if (smr != null) return smr.sharedMesh;
            MeshFilter mf = r.GetComponent<MeshFilter>();
            return mf == null ? null : mf.sharedMesh;
        }

        private static bool Same(Material[] a, Material[] b)
        {
            if (a == null || b == null || a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++) if (!ReferenceEquals(a[i], b[i])) return false;
            return true;
        }

        /// <summary>
        /// ct_replace takes a FILE, so the gate writes one - driving the real command is the point.
        /// The sample rig is <see cref="ModelBuild.SampleGlb"/>, so the numbers the arms assert (6
        /// vertices, 9 indices) are the FILE's own and not a constant chosen to match.
        /// </summary>
        private static string WriteSampleGlb()
        {
            try
            {
                string dir = Path.Combine(Application.persistentDataPath, "ContentTool");
                Directory.CreateDirectory(dir);
                string path = Path.Combine(dir, "ct_live_gate.glb");
                File.WriteAllBytes(path, ModelBuild.SampleGlb());
                return path;
            }
            catch (Exception) { return null; }
        }

        private static string First(string s)
        {
            int nl = s.IndexOf('\n');
            return nl < 0 ? s : s.Substring(0, nl);
        }

        private static int Check(StringBuilder log, string gate, bool ok, string detail)
        {
            log.AppendLine(gate + (ok ? " PASS " : " FAIL ") + detail);
            return ok ? 0 : 1;
        }
    }
}
