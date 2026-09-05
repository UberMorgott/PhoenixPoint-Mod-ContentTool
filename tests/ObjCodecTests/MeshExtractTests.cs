using System;
using System.Collections.Generic;
using System.IO;
using AssetsTools.NET;
using AssetsTools.NET.Extra;
using Morgott.ContentTool.Bake;
using Morgott.ContentTool.Import;

/// <summary>
/// MESH EXTRACTION, offline: pull a REAL shipped mesh out of a REAL shipped bundle as GLB, read the
/// GLB back with the codec's own reader, and assert the model that comes back IS the mesh that went
/// in - vertex count, triangle count, positions, UVs, skin weights, bind poses.
///
/// The oracle is deliberately double. The round trip alone would only prove the writer and the
/// reader agree with each other, so every count is ALSO checked against what the serialized file
/// itself declares, read independently by MeshFields/SkinFields. Nothing here needs Unity, so the
/// whole gate runs here instead of costing a game launch.
///
/// The game install is machine-specific, so a missing bundle is VOID, never PASS.
/// </summary>
internal static class MeshExtractTests
{
    private const string Bundle = "mutoid_assets_all.bundle";
    /// <summary>Rigged: 3 bind poses, so the skin half is exercised.</summary>
    private const string Rigged = "ALN_Siren_Arm_Slasher_Right";
    /// <summary>A second, larger mesh in the same file - the control that the first was not a fluke.</summary>
    private const string Control = "Geo_Head02_V01";
    /// <summary>
    /// The OUT-OF-SUBTREE case, on real shipped data: PP_Security_Turret_Base's m_RootBone is Door1_L,
    /// and the other three doors are that bone's SIBLINGS, so their hashes continue a prefix that
    /// reaches ABOVE the anchor. Refusing all nine names because seven of them are not under the root
    /// bone is what sent a perfectly good rig to nearest-bone.
    /// </summary>
    private const string Turrets = "px_security_turret_assets_all.bundle";
    private const string Siblings = "PP_Security_Turret_Base";
    private const string MoreSiblings = "PP_Security_Turret_Guns";

    private static int checks;

    internal static string Run()
    {
        Alignment();
        string root = Environment.GetEnvironmentVariable("PPRoot") ?? @"D:\Steam\steamapps\common\Phoenix Point";
        string classData = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"..\..\..\..\..\lib\classdata.tpk");
        string shipped = Path.Combine(root,
            @"PhoenixPointWin64_Data\StreamingAssets\aa\StandaloneWindows64\" + Bundle);
        if (!File.Exists(classData)) return "MESH extract VOID - no " + Path.GetFullPath(classData);
        if (!File.Exists(shipped)) return "MESH extract VOID - no " + shipped + " (set PPRoot to the game folder)";

        string first = null, second = null;
        Open(classData, shipped, (m, afile, resS) =>
        {
            first = OneMesh(m, afile, resS, Rigged);
            second = OneMesh(m, afile, resS, Control);
        });
        // The turret bundle carries the out-of-subtree rig arms, and a machine without it has NOT run
        // them. Saying PASS on the line that also says "VOID - not on this machine" reads as a green
        // gate; the headline is the only part anyone greps.
        string turret = Turret(classData, Path.GetDirectoryName(shipped));
        if (turret == null)
            return "MESH extract VOID - no " + Path.Combine(Path.GetDirectoryName(shipped), Turrets) +
                   ", so the out-of-subtree rig arms did not run\n  " + first + "\n  " + second;
        return "MESH extract PASS on " + Bundle + ", " + checks + " check(s)\n  " + first + "\n  " +
               second + "\n  " + turret;
    }

    /// <summary>
    /// The regression arm, on REAL shipped data: a rig whose bones sit OUTSIDE the root bone's own
    /// subtree must still come out NAMED. Every one of these bones is checkable - the hashes continue
    /// a prefix reached by taking the anchor's own tail back off its hash - so a single hashed joint
    /// here means the whole renderer was refused again and every replacement onto these meshes is back
    /// on nearest-bone.
    /// </summary>
    private static string Turret(string classData, string streamingAssets)
    {
        string bundle = Path.Combine(streamingAssets, Turrets);
        if (!File.Exists(bundle)) return null;   // VOID, and Run's headline has to say so
        string line = null;
        Open(classData, bundle, (m, afile, resS) =>
        {
            line = "";
            foreach (string name in new[] { Siblings, MoreSiblings })
            {
                AssetFileInfo info = AssetIndex.FindUnique(m, afile, AssetClassID.Mesh, name, Turrets);
                AssetTypeValueField mesh = m.GetBaseField(afile, info);
                int poses = Int(SkinFields.SkinSummary(mesh), "bindposes=");
                int wantPoses = name == Siblings ? 9 : 6;
                Check(poses == wantPoses, name + ": the shipped mesh still carries its " + wantPoses +
                      " bind poses (" + poses + ") - every count below is measured against that");

                string refusal;
                string[] rig = SkinFields.BoneNames(m, afile, info.PathId, out refusal);
                Check(refusal == null, name + ": nothing in its rig CONTRADICTS the mesh's own hashes" +
                      (refusal == null ? "" : " - " + refusal));
                Check(rig != null && rig.Length == poses,
                      name + ": its bones are named even though its root bone's siblings are in the list (" +
                      poses + " expected, got " + (rig == null ? "null" : rig.Length.ToString()) + ")");
                int verified = 0;
                foreach (string bone in rig) if (bone != null) verified++;
                Check(verified == poses,
                      name + ": all " + poses + " of them VERIFY against the mesh's own bone path hashes, " +
                      "the siblings through the ancestor they share with the root bone (" + verified + " did) - " +
                      SkinFields.SkinSummary(mesh));

                // Read back off the WRITTEN nodes, so this cannot claim a name the .glb does not carry.
                SkinnedModel model = MeshRead.Read(mesh, resS, rig);
                int joints = model.JointNodes == null ? 0 : model.JointNodes.Length;
                // WHAT THE SOURCE MESH ACTUALLY HOLDS, written down rather than read back off the
                // extract: "every joint written is a name" is satisfied by writing NONE, so a run that
                // lost the skin whole would pass it 0 of 0. The Guns are the skinned mesh (6 joints);
                // the Base is STATIC - 9 bind poses and no weights - so it writes no joints at all and
                // its 9/9 above is the only thing that can speak for it.
                int wantJoints = name == Siblings ? 0 : 6;
                Check(joints == wantJoints,
                      name + ": the extract writes the " + wantJoints + " skinned joint(s) this mesh has" +
                      (wantJoints == 0 ? " - it carries bind poses but no weights" : "") +
                      " (got " + joints + ")");
                Check(MeshRead.NamedJoints(model) == joints,
                      name + ": every joint the extract writes is a NAME, none on a hash (" +
                      MeshRead.NamedJoints(model) + " of " + joints + ")");
                line += (line.Length == 0 ? "" : " | ") + name + ": " + verified + "/" + poses +
                        " bones verified, " + joints + " joint(s) written";
            }
        });
        return line;
    }

    private static string OneMesh(AssetsManager m, AssetsFileInstance afile, Func<string, byte[]> resS,
                                  string name)
    {
        AssetFileInfo info = AssetIndex.FindUnique(m, afile, AssetClassID.Mesh, name, Bundle);
        AssetTypeValueField mesh = m.GetBaseField(afile, info);

        // What the FILE says, read by the writer's own field readers - the independent half.
        string summary = MeshFields.Summary(mesh);
        int fileVerts = Int(summary, "verts="), fileIndices = Int(summary, "indices=");
        int filePoses = Int(SkinFields.SkinSummary(mesh), "bindposes=");

        // What BundleBaker.ReadMesh hands the reader: the rig's REAL names off the renderer that uses
        // this mesh, since the Mesh alone carries only uninvertible CRC-32s.
        string[] rig = SkinFields.BoneNames(m, afile, info.PathId);

        SkinnedModel model = MeshRead.Read(mesh, resS, rig);
        int triangles = 0;
        foreach (int[] s in model.Submeshes) triangles += s.Length / 3;

        Check(model.Positions.Length == fileVerts,
              name + ": the extracted model has the vertex count the file declares (" + fileVerts + ", got " + model.Positions.Length + ")");
        Check(triangles * 3 == fileIndices,
              name + ": the extracted model has the index count the file declares (" + fileIndices + ", got " + triangles * 3 + ")");
        Check(model.BindposeCount == filePoses,
              name + ": the extracted model has the bind poses the file declares (" + filePoses + ", got " + model.BindposeCount + ")");
        // Not a constant here: whether a mesh is rigged is the FILE's answer (does it carry bind
        // poses), and asserting a hardcoded expectation would only be asserting this test's guess -
        // which was wrong, Geo_Head02_V01 is rigged too.
        bool wantRigged = filePoses > 0;
        Check(wantRigged == (model.JointNodes != null && model.JointNodes.Length > 0),
              name + ": it comes out rigged exactly when the file carries bind poses (" + filePoses + ")");
        Check(model.Normals != null && model.Uv0 != null,
              name + ": normals and uv0 came out (a shipped character mesh has both)");

        // ---- the round trip itself
        byte[] glb = GlbCodec.Write(model);
        Check(glb.Length > 12 && BitConverter.ToUInt32(glb, 0) == 0x46546C67u,
              name + ": the bytes are a .glb (glTF magic, " + glb.Length + " B)");
        SkinnedModel back = GlbReader.Read(glb);

        Check(back.Positions.Length == model.Positions.Length,
              name + ": the GLB reads back with the same vertex count");
        int backTriangles = 0;
        foreach (int[] s in back.Submeshes) backTriangles += s.Length / 3;
        Check(backTriangles == triangles, name + ": the GLB reads back with the same triangle count");

        // Unity -> glTF -> Unity is the same reflection applied twice, so the positions must land on
        // themselves. Anything that shifted a channel or mis-strode a stream shows up here.
        Check(Furthest(model.Positions, back.Positions) < 1e-3f,
              name + ": every position survives the round trip (max drift " +
              Furthest(model.Positions, back.Positions).ToString("0.#####") + ")");
        if (model.Uv0 != null && back.Uv0 != null)
            Check(FurthestUv(model.Uv0, back.Uv0) < 1e-4f,
                  name + ": every uv0 survives the round trip (max drift " +
                  FurthestUv(model.Uv0, back.Uv0).ToString("0.#####") + ")");

        if (wantRigged)
        {
            Check(back.JointNames.Count == filePoses,
                  name + ": the GLB carries one joint per bind pose (" + filePoses + ", got " + back.JointNames.Count + ")");
            Check(back.InverseBindMatrices != null && back.InverseBindMatrices.Length == filePoses,
                  name + ": the GLB carries one inverse bind matrix per joint");
            Check(Same(model.Joints, back.Joints), name + ": every bone index survives the round trip");
            Check(back.Weights != null && Furthest(model.Weights, back.Weights) < 1e-4f,
                  name + ": every skin weight survives the round trip");
            // The bone NAMES, not the CRC-32s the Mesh alone carries: they live on the
            // SkinnedMeshRenderer that uses this mesh, and an extract that writes hashes can never
            // reach Outcome.ByName when the author edits it and ships it back.
            Check(rig != null && rig.Length == filePoses,
                  name + ": a SkinnedMeshRenderer in the bundle names all " + filePoses + " of its bones");
            bool hashed = false, sameAsRig = true;
            for (int j = 0; j < back.JointNames.Count; j++)
            {
                if (back.JointNames[j].StartsWith("bone_", StringComparison.Ordinal)) hashed = true;
                if (rig == null || j >= rig.Length || back.JointNames[j] != rig[j]) sameAsRig = false;
            }
            Check(!hashed, name + ": no joint in the GLB is named after a hash (" +
                  string.Join(", ", back.JointNames.ToArray()) + ")");
            Check(sameAsRig, name + ": the GLB's joints ARE the renderer's bones, in its order");
            // The whole point of the names: the extract, read back, binds onto its own rig BY NAME.
            IList<BindingIssue> issues = SkinCompatibility.Analyze(back, rig);
            Check(issues.Count == 0, name + ": the extracted GLB binds to its own rig with no issue" +
                  (issues.Count == 0 ? "" : " - " + issues[0].Code + ": " + issues[0].Message));
            Check(ReplacementDecision.Decide(back.JointNames.Count > 0, filePoses > 0, rig != null,
                                             issues.Count == 0 ? null : issues[0]) == Outcome.ByName,
                  name + ": the replacement verdict on it is ByName");
        }
        if (wantRigged)
        {
            // The FALLBACK arm, on the same real mesh. A name array that is not index-for-index with
            // m_BindPose is not that correspondence at all, so the joints must come out on the HASHES
            // and the count the report is built from must say so - it is read back off the written
            // nodes (MeshRead.NamedJoints), never from whether the caller passed an array.
            string[] wrong = new string[filePoses + 1];
            for (int i = 0; i < wrong.Length; i++) wrong[i] = "not_a_bone_" + i;
            SkinnedModel fell = MeshRead.Read(mesh, resS, wrong);
            Check(MeshRead.NamedJoints(fell) == 0,
                  name + ": a wrong-length name array is reported as 0 named, " + filePoses + " hashed");
            Check(fell.Nodes[fell.JointNodes[0]].Name.StartsWith("bone_", StringComparison.Ordinal),
                  name + ": ... and the joints really are named after the hashes (" +
                  fell.Nodes[fell.JointNodes[0]].Name + ")");
        }

        // The STATIC path, without hunting for an unrigged shipped mesh: the same geometry with the
        // skin stripped must still be a whole valid export - Validate refuses a half-rig either way.
        SkinnedModel stat = MeshRead.Read(mesh, resS);
        stat.Joints = null; stat.Weights = null; stat.JointNodes = null;
        stat.InverseBindMatrices = null; stat.Nodes.Clear();
        SkinnedModel statBack = GlbReader.Read(GlbCodec.Write(stat));
        Check(statBack.Positions.Length == model.Positions.Length && statBack.JointNames.Count == 0,
              name + ": the same mesh exports as a STATIC glb with no rig and the same geometry");

        return name + ": " + summary.Substring(0, summary.IndexOf(" centre=", StringComparison.Ordinal)) +
               " -> glb " + glb.Length + "B verts=" + back.Positions.Length + " tris=" + backTriangles +
               " joints=" + back.JointNames.Count +
               (back.JointNames.Count == 0 ? "" : " bone0='" + back.JointNames[0] + "'");
    }

    /// <summary>
    /// The other half of "the names are index-for-index with m_BindPose": ORDER. m_BoneNameHashes is
    /// the CRC-32 of each bone's transform PATH, and m_Bones may be written in any order, so a rig
    /// whose bones are permuted has to be REFUSED rather than renumbered - it would otherwise hand
    /// every joint the name of a different bone. Needs no bundle, so it runs even when one is VOID.
    /// </summary>
    private static void Alignment()
    {
        // The hashes are of paths under a prefix the shipped prefab does NOT keep (its root is a
        // renamed placeholder), so they are built here with one and checked without it - which is
        // the whole reason the check continues the root's CRC instead of rebuilding a path.
        const string gone = "PLACEHOLDER_Rig/";
        const string root = "Root/Spine";
        string[] paths = { root + "/Head", root, root + "/Jaw" };   // root bone in slot 1, as shipped
        uint rootHash = SkinFields.BoneHash(gone + root);
        uint[] hashes = { SkinFields.BoneHash(gone + paths[0]), rootHash, SkinFields.BoneHash(gone + paths[2]) };
        Check(SkinFields.BonesAligned(paths, root, rootHash, hashes),
              "a rig in its own bind-pose order verifies against the mesh's bone path hashes");
        string[] swapped = { paths[2], paths[1], paths[0] };
        Check(!SkinFields.BonesAligned(swapped, root, rootHash, hashes),
              "a permuted m_Bones is refused, not silently renumbered onto the bind poses");
        Check(!SkinFields.BonesAligned(new[] { "Elsewhere/Head", root, paths[2] }, root, rootHash, hashes),
              "a bone that does not descend from the root bone cannot be checked, so it is refused");

        // OUTSIDE the root bone's subtree, the route px_security_turret needs: CRC-32's round is a
        // bijection, so feeding the anchor's own tail back OUT recovers the register at the ancestor
        // the two paths share, and the sibling is hashed forward from there - all without ever
        // learning the prefix. A sibling whose hash does NOT come out of that ancestor is simply not
        // verified; only a bone that IS checkable and disagrees refuses the rig.
        const string sibling = "Root/Elsewhere";
        uint[] withSibling = { hashes[0], rootHash, SkinFields.BoneHash(gone + sibling) };
        Check(SkinFields.BonesAligned(new[] { paths[0], root, sibling }, root, rootHash, withSibling),
              "a bone outside the root bone's subtree verifies through the ancestor the two share");
        uint[] wrongSibling = { hashes[0], rootHash, SkinFields.BoneHash(gone + "Root/Somewhere") };
        Check(!SkinFields.BonesAligned(new[] { paths[0], root, sibling }, root, rootHash, wrongSibling),
              "... and one whose hash does not come out of that ancestor is not counted as verified");

        // A FALLBACK ANCHOR MUST BE CONFIRMED BY SOMETHING IT DID NOT MANUFACTURE. The bone carrying
        // the mesh's root hash matches ITSELF by construction, so retrying a contradicting rig against
        // it always "verified" at least that one slot and the refusal was thrown away. Fabricated to
        // the shape that broke it: the hashes are of Root and Root/Child, m_Bones lists them the other
        // way round, and the wrong answer was ["Child", null] - the CHILD's name in the ROOT's slot.
        const string plainRoot = "Root", plainChild = "Root/Child";
        uint plainRootHash = SkinFields.BoneHash(plainRoot);
        uint[] swappedHashes = { plainRootHash, SkinFields.BoneHash(plainChild) };
        string tautology;
        string[] taut = SkinFields.Verified(new[] { "Child", "Root" }, new[] { plainChild, plainRoot },
                                            plainRoot, plainRootHash, swappedHashes, out tautology);
        Check(taut == null && tautology != null,
              "a rig that contradicts a CONFIRMED anchor is refused, not re-read from the bone that " +
              "carries the root hash by construction (got " +
              (taut == null ? "null" : "[" + string.Join(", ", taut) + "]") + ", refusal " +
              (tautology ?? "none") + ")");

        // The other half of the same rule: an anchor NOTHING confirms reports no contradiction either.
        // m_RootBone here is the hashed root's own PARENT and no slot carries the root hash, so every
        // bone is a descendant of the anchor and every one of them fails. That is this file being
        // unreadable from that anchor, not evidence that m_Bones is in the wrong order.
        string unconfirmed;
        string[] kids = { root + "/Head", root + "/Jaw" };
        uint[] kidHashes = { hashes[0], hashes[2] };
        string[] nothing = SkinFields.Verified(new[] { "Head", "Jaw" }, kids, "Root", rootHash,
                                               kidHashes, out unconfirmed);
        Check(nothing == null && unconfirmed == null,
              "an anchor nothing confirms leaves every bone unverifiable, and refuses nothing (" +
              (unconfirmed ?? "none") + ")");

        // ORDER MUST NOT DECIDE THE ANSWER. GetAssetsOfType hands the renderers back in file order, and
        // a renderer that verifies NOTHING (the case just above) used to null the whole rig - so the
        // names an earlier renderer verified survived or vanished depending on which one came second.
        // Both folds, both orders, one answer.
        string lateRefusal, earlyRefusal;
        string[] late = SkinFields.Fold(SkinFields.Fold(null, new[] { "Root", null }, out lateRefusal),
                                        nothing, out lateRefusal);
        string[] early = SkinFields.Fold(SkinFields.Fold(null, nothing, out earlyRefusal),
                                         new[] { "Root", null }, out earlyRefusal);
        Check(late != null && early != null && late[0] == "Root" && early[0] == "Root" &&
              lateRefusal == null && earlyRefusal == null,
              "a renderer that verifies nothing cannot discard the names another verified, whichever " +
              "order the file lists the two in (late=" + Show(late) + ", early=" + Show(early) + ")");
    }

    private static string Show(string[] names)
    {
        return names == null ? "null" : "[" + string.Join(", ", names) + "]";
    }

    // ---------------------------------------------------------------- helpers

    private static void Open(string classData, string bundlePath,
                             Action<AssetsManager, AssetsFileInstance, Func<string, byte[]>> ask)
    {
        AssetsManager m = new AssetsManager();
        m.LoadClassPackage(classData);
        BundleFileInstance bun = m.LoadBundleFile(bundlePath, true);
        AssetsFileInstance afile = m.LoadAssetsFileFromBundle(bun, 0, false);
        m.LoadClassDatabaseFromPackage(afile.file.Metadata.UnityVersion);
        try { ask(m, afile, entry => BundleHelper.LoadAssetDataFromBundle(bun.file, entry)); }
        finally { m.UnloadAll(); }
    }

    private static float Furthest(ObjVector3[] a, ObjVector3[] b)
    {
        float worst = 0f;
        for (int i = 0; i < a.Length && i < b.Length; i++)
        {
            worst = Math.Max(worst, Math.Abs(a[i].X - b[i].X));
            worst = Math.Max(worst, Math.Abs(a[i].Y - b[i].Y));
            worst = Math.Max(worst, Math.Abs(a[i].Z - b[i].Z));
        }
        return worst;
    }

    private static float FurthestUv(ObjVector2[] a, ObjVector2[] b)
    {
        float worst = 0f;
        for (int i = 0; i < a.Length && i < b.Length; i++)
        {
            worst = Math.Max(worst, Math.Abs(a[i].X - b[i].X));
            worst = Math.Max(worst, Math.Abs(a[i].Y - b[i].Y));
        }
        return worst;
    }

    private static float Furthest(float[] a, float[] b)
    {
        if (a == null || b == null || a.Length != b.Length) return float.MaxValue;
        float worst = 0f;
        for (int i = 0; i < a.Length; i++) worst = Math.Max(worst, Math.Abs(a[i] - b[i]));
        return worst;
    }

    private static bool Same(ushort[] a, ushort[] b)
    {
        if (a == null || b == null || a.Length != b.Length) return false;
        for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
        return true;
    }

    /// <summary>The integer after a `key=` in one of the one-line summaries.</summary>
    private static int Int(string summary, string key)
    {
        int at = summary.IndexOf(key, StringComparison.Ordinal);
        if (at < 0) throw new Exception("MESH extract FAIL: '" + key + "' is not in '" + summary + "'");
        at += key.Length;
        int end = at;
        while (end < summary.Length && char.IsDigit(summary[end])) end++;
        return int.Parse(summary.Substring(at, end - at));
    }

    private static void Check(bool ok, string what)
    {
        checks++;
        if (!ok) throw new Exception("MESH extract FAIL: " + what);
    }
}
