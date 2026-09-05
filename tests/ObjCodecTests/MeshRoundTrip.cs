using System;
using System.IO;
using AssetsTools.NET;
using AssetsTools.NET.Extra;
using Morgott.ContentTool.Bake;
using Morgott.ContentTool.Import;

/// <summary>
/// The mesh half of route vii, offline: patch a copy of a REAL shipped bundle, repack it the way
/// BundleBaker does, read the copy back off disk, and compare against the shipped original in the
/// same run. This is the same oracle the in-game P4 gate reads (<c>MeshFields.Summary</c>), so a
/// green run here and a green gate there mean the same thing - what the game still has to answer is
/// only whether it RENDERS the result.
///
/// The game install is machine-specific, so a missing bundle is VOID, never PASS: a gate that cannot
/// answer must say so.
/// </summary>
internal static class MeshRoundTrip
{
    private const string Bundle = "mutoid_assets_all.bundle";
    private const string Target = "ALN_Siren_Arm_Slasher_Right";
    private const string Control = "Geo_Head02_V01";

    internal static string Run()
    {
        string root = Environment.GetEnvironmentVariable("PPRoot") ?? @"D:\Steam\steamapps\common\Phoenix Point";
        string classData = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"..\..\..\..\..\lib\classdata.tpk");
        string shipped = Path.Combine(root, @"PhoenixPointWin64_Data\StreamingAssets\aa\StandaloneWindows64\" + Bundle);
        if (!File.Exists(shipped)) return "MESH round trip VOID - no " + shipped + " (set PPRoot to the game folder)";
        if (!File.Exists(classData)) return "MESH round trip VOID - no " + Path.GetFullPath(classData);

        BakedMesh baked = MeshBuild.From(ObjCodec.Parse(
            "v -0.5 -0.5 0\nv 0.5 -0.5 0\nv 0.5 0.5 0\nv -0.5 0.5 0\n" +
            "vt 0 0\nvt 1 0\nvt 1 1\nvt 0 1\nvn 0 0 -1\n" +
            "f 1/1/1 2/2/1 3/3/1\nf 1/1/1 3/3/1 4/4/1\n"));

        string copy = Path.Combine(Path.GetTempPath(), "ct-meshroundtrip-" + Bundle);
        string before = Summary(classData, shipped, Target), controlBefore = Summary(classData, shipped, Control);
        string skinBefore = Summary(classData, shipped, Target, true);
        string bufBefore = Buffers(classData, shipped, Target);
        string bufControlBefore = Buffers(classData, shipped, Control);
        Patch(classData, shipped, copy, baked);
        string after = Summary(classData, copy, Target), controlAfter = Summary(classData, copy, Control);
        string skinAfter = Summary(classData, copy, Target, true);
        string bufAfter = Buffers(classData, copy, Target);
        string bufControlAfter = Buffers(classData, copy, Control);
        bool unknown = Unknown(classData, shipped);
        File.Delete(copy);

        string want = baked.Describe();
        Assert(after.StartsWith(want, StringComparison.Ordinal),
               "the copy's '" + Target + "' IS the baked quad: " + after + " (wanted " + want + ")");
        Assert(!before.StartsWith(want, StringComparison.Ordinal),
               "the shipped '" + Target + "' never had it: " + before);
        Assert(controlBefore == controlAfter,
               "CONTROL '" + Control + "' is byte-identical in both: " + controlAfter);

        // THE BUFFERS, which the summary deliberately cannot see - the in-game P4-bytes arm compares
        // exactly these two strings, and on an unskinned target it is the ONLY proof a replacement
        // landed. All three answers the arm can get are asserted here, in one run.
        Assert(bufBefore != null && bufAfter != null && bufBefore != bufAfter,
               "the copy's '" + Target + "' carries DIFFERENT vertex/index bytes than the shipped one: " +
               bufAfter + " (shipped " + bufBefore + ")");
        Assert(bufControlBefore != null && bufControlBefore == bufControlAfter,
               "CONTROL the byte-copied '" + Control + "' hashes the SAME on both sides: " + bufControlAfter);
        Assert(unknown, "CONTROL a field that holds NEITHER buffer NOR a stream path answers null, not a " +
                        "hash of nothing - two of those compare EQUAL and would red a correct bake");
        // STREAMED, which no mesh in this bundle is - the two answers are built from the real shipped
        // field with its vertices moved out to a .resS, because "same archive entry, different slice"
        // is exactly the pair the path-only answer used to hash EQUAL.
        string[] streamed = Streamed(classData, shipped);
        Assert(streamed[0] != streamed[1],
               "two meshes streaming out of the SAME .resS at different offsets answer DIFFERENTLY: " +
               streamed[0] + " vs " + streamed[1]);

        // The SKIN half: the target is rigged, so the copy must carry the shipped skeleton unchanged
        // AND our skin stream over it. The expected skeleton is read off the shipped file in this
        // same run, never spelled out - a constant here would only be asserting our own writer.
        string skeleton = Skeleton(skinBefore);
        Assert(skeleton != null, "the target '" + Target + "' is rigged: " + skinBefore);
        // The width is the SHIPPED target's own, read in this same run - a replacement that narrowed
        // it would read RED here.
        int inf = Count(classData, shipped, Target);
        string wantSkin = skeleton + " " + SkinFields.OurLayout(inf) + " skinBytes=" +
                          baked.VertexCount * SkinFields.SkinStride(inf);
        Assert(skinAfter.StartsWith(wantSkin, StringComparison.Ordinal),
               "the copy keeps the shipped skeleton and carries our skin: " + skinAfter +
               " (wanted " + wantSkin + ")");
        Assert(skinAfter.EndsWith(" inRange=yes", StringComparison.Ordinal),
               "every bone index names a bind pose the mesh has: " + skinAfter);

        return "MESH round trip PASS on " + Bundle + "\n  skinless " + Skinless(classData, shipped) +
               "\n  target  " + before + "\n       -> " + after +
               "\n  skin    " + skinBefore + "\n       -> " + skinAfter +
               "\n  control " + controlAfter + "\n  byName  " + ByName(classData, shipped);
    }

    /// <summary>
    /// The by-name arm, offline: a .glb whose armature IS the shipped mesh's, listed in REVERSE,
    /// replaces that mesh through the REAL <see cref="SkinFields.RebindByName"/>, and every vertex of
    /// the written copy is read back out of the bytes.
    ///
    /// Vertex 1 is shared HALF and HALF, which nearest-bone cannot produce at all, and every vertex
    /// sits at a file slot that is not its live bone index. The expectation is built here from the
    /// file's own weights plus a plain name lookup in the shipped skeleton, so the binder's own
    /// arrays take no part in it - and the string an INDEX binding would have written is computed in
    /// the same run and asserted to DIFFER, which is what makes a green line mean something.
    /// </summary>
    private static string ByName(string classData, string shipped)
    {
        string[] bones = Bones(classData, shipped, Target);
        Assert(bones != null && bones.Length >= 3,
               "the shipped '" + Target + "' names its bones: " +
               (bones == null ? "(no SkinnedMeshRenderer uses it)" : bones.Length + " bone(s)"));
        int n = bones.Length;

        SkinnedModel model = new SkinnedModel { Name = "byname" };
        model.Nodes.Add(new SkinNode { Name = "byname_root", Parent = -1, Local = Identity() });
        model.JointNodes = new int[n];
        model.InverseBindMatrices = new float[n][];
        for (int slot = 0; slot < n; slot++)
        {
            model.Nodes.Add(new SkinNode { Name = bones[n - 1 - slot], Parent = 0, Local = Identity() });
            model.InverseBindMatrices[slot] = Identity();
            model.JointNodes[slot] = slot + 1;
        }
        model.Positions = new[] { new ObjVector3(-0.1f, 0f, 0f), new ObjVector3(0f, 0.2f, 0f),
                                  new ObjVector3(0.1f, 0f, 0f) };
        model.Normals = new[] { new ObjVector3(0f, 0f, -1f), new ObjVector3(0f, 0f, -1f), new ObjVector3(0f, 0f, -1f) };
        model.Uv0 = new[] { new ObjVector2(0f, 0f), new ObjVector2(0.5f, 1f), new ObjVector2(1f, 0f) };
        model.Submeshes.Add(new[] { 0, 1, 2 });
        model.Materials.Add("byname");
        // The FIRST and LAST live bones. A reversal leaves the middle slot where it is, so a rig with
        // an odd bone count would have one pair the arm cannot tell apart - these two always move.
        ushort j0 = (ushort)(n - 1), j1 = 0;                     // live bone 0, and live bone n-1
        model.Joints = new ushort[] { j0, 0, 0, 0,  j0, j1, 0, 0,  j1, 0, 0, 0 };
        model.Weights = new[] { 1f, 0f, 0f, 0f,  0.5f, 0.5f, 0f, 0f,  1f, 0f, 0f, 0f };

        // Through the FILE, not the object: a .glb an author drops in is read by GlbReader, and the
        // joint NAMES this arm depends on only exist after that read (the writer keeps them in nodes).
        SkinnedModel file = GlbReader.Read(GlbCodec.Write(model));
        BakedSkin skin = ModelBuild.From(file, "byname");
        Assert(skin.Mesh.VertexCount == file.Positions.Length,
               "the import keeps one baked vertex per file vertex: " + skin.Mesh.VertexCount +
               " vs " + file.Positions.Length);

        string copy = Path.Combine(Path.GetTempPath(), "ct-byname-" + Bundle);
        Patch(classData, shipped, copy, skin.Mesh, file);
        string got = Influences(classData, copy, Target);
        File.Delete(copy);

        int inf = Count(classData, shipped, Target);
        int[] slots = new int[inf];
        string want = "", bySlot = "";
        for (int i = 0; i < 3; i++)
        {
            SkinFields.Heaviest(file.Weights, i, slots);
            float sum = 0f;
            for (int k = 0; k < inf; k++) if (slots[k] >= 0) sum += file.Weights[i * 4 + slots[k]];
            string w = "", live = "", bySlotBones = "";
            for (int k = 0; k < inf; k++)
            {
                int slot = file.Joints[i * 4 + (slots[k] >= 0 ? slots[k] : slots[0])];
                int at = Array.IndexOf(bones, file.JointNames[slot]);
                Assert(at >= 0, "vertex " + i + "'s bones are on the shipped skeleton");
                if (k == 0)
                    Assert(at != slot, "vertex " + i + " sits at file slot " + slot + " but live bone " +
                           at + ", so a by-name binding and an index binding differ here");
                w += (k == 0 ? "" : "/") + F(slots[k] < 0 ? 0f : file.Weights[i * 4 + slots[k]] / sum);
                live += (k == 0 ? "->bone" : "+bone") + at;
                bySlotBones += (k == 0 ? "->bone" : "+bone") + slot;
            }
            want += (i == 0 ? "" : " ") + "v" + i + "=" + w + live;
            bySlot += (i == 0 ? "" : " ") + "v" + i + "=" + w + bySlotBones;
        }

        Assert(got == want, "the copy carries the FILE's own weights on the bones it NAMES: " +
               got + " (wanted " + want + ")");
        Assert(got != bySlot, "CONTROL an index binding would have written " + bySlot +
               ", which is a different string - the arm can tell the two apart");
        return n + " shipped bones, joints REVERSED -> " + got +
               " (an index binding would read " + bySlot + ")";
    }

    private static string F(float v) => v.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);

    private static float[] Identity()
    {
        float[] f = new float[16];
        f[0] = f[5] = f[10] = f[15] = 1f;
        return f;
    }

    /// <summary>
    /// THE PREDICATE THE SKINLESS REFUSAL KEYS ON, against real shipped meshes rather than a fixture:
    /// a mesh with no armature may replace a STATIC object and may NOT replace a rigged one, and the
    /// difference is the target's own bind poses. Both answers are asserted in the same bundle, so a
    /// Rigged() that had been stubbed either way turns this red.
    ///
    /// Asserted here and not through BundleBaker.ReplaceMesh because that needs a live Unity; the guard
    /// there is one call to this predicate.
    /// </summary>
    private static string Skinless(string classData, string bundlePath)
    {
        AssetsManager m = new AssetsManager();
        m.LoadClassPackage(classData);
        BundleFileInstance bun = m.LoadBundleFile(bundlePath, true);
        AssetsFileInstance af = m.LoadAssetsFileFromBundle(bun, 0, false);
        m.LoadClassDatabaseFromPackage(af.file.Metadata.UnityVersion);
        try
        {
            Assert(SkinFields.Rigged(m.GetBaseField(af, Find(m, af, Target))),
                   "'" + Target + "' is rigged, so a file with no armature is refused for it");

            // The STATIC half, made out of the same shipped mesh rather than looked for: this bundle
            // ships no unrigged mesh at all, and a target with its bind poses taken away IS the static
            // case as far as the rule is concerned - one fact, read one way (SkinFields.Rigged).
            AssetTypeValueField statik = m.GetBaseField(af, Find(m, af, Target));
            statik["m_BindPose"]["Array"].Children.Clear();
            Assert(!SkinFields.Rigged(statik),
                   "the same mesh with no bind poses is NOT rigged, so a file with no armature still " +
                   "replaces a static object - which is what demos\\WeaponMesh does");
            return "rigged '" + Target + "' refuses a file with no armature; the same mesh stripped of " +
                   "its bind poses still accepts one";
        }
        finally { m.UnloadAll(); }
    }

    /// <summary>bindposes/hashes/rootHash/bonesAABB out of a skin summary; null when not rigged.</summary>
    private static string Skeleton(string skinSummary)
    {
        string[] t = skinSummary.Split(' ');
        if (t.Length < 4 || t[0] == "bindposes=0") return null;
        return t[0] + " " + t[1] + " " + t[2] + " " + t[3];
    }

    /// <summary>
    /// The bones a shipped Mesh is skinned to, by name, in bind-pose order - the real resolver the
    /// bake path uses, so this arm cannot agree with a lookup only the test knows how to do.
    /// </summary>
    private static string[] Bones(string classData, string bundlePath, string meshName)
    {
        AssetsManager m = new AssetsManager();
        m.LoadClassPackage(classData);
        BundleFileInstance bun = m.LoadBundleFile(bundlePath, true);
        AssetsFileInstance af = m.LoadAssetsFileFromBundle(bun, 0, false);
        m.LoadClassDatabaseFromPackage(af.file.Metadata.UnityVersion);
        try { return SkinFields.BoneNames(m, af, Find(m, af, meshName).PathId); }
        finally { m.UnloadAll(); }
    }

    /// <summary>The influences per vertex a shipped Mesh declares - the width a replacement keeps.</summary>
    private static int Count(string classData, string bundlePath, string meshName)
    {
        AssetsManager m = new AssetsManager();
        m.LoadClassPackage(classData);
        BundleFileInstance bun = m.LoadBundleFile(bundlePath, true);
        AssetsFileInstance af = m.LoadAssetsFileFromBundle(bun, 0, false);
        m.LoadClassDatabaseFromPackage(af.file.Metadata.UnityVersion);
        try { return SkinFields.InfluencesOf(m.GetBaseField(af, Find(m, af, meshName))); }
        finally { m.UnloadAll(); }
    }

    private static string Influences(string classData, string bundlePath, string meshName)
    {
        AssetsManager m = new AssetsManager();
        m.LoadClassPackage(classData);
        BundleFileInstance bun = m.LoadBundleFile(bundlePath, true);
        AssetsFileInstance af = m.LoadAssetsFileFromBundle(bun, 0, false);
        m.LoadClassDatabaseFromPackage(af.file.Metadata.UnityVersion);
        try { return SkinFields.SkinInfluences(m.GetBaseField(af, Find(m, af, meshName))); }
        finally { m.UnloadAll(); }
    }

    /// <summary>
    /// The BundleBaker write path, minus everything a mesh replacement does not use.
    /// <paramref name="model"/> non-null takes the by-name binding, which is the branch
    /// <c>BundleBaker.ReplaceMesh</c> picks for a .glb that carries an armature.
    /// </summary>
    private static void Patch(string classData, string shipped, string outPath, BakedMesh baked,
                              SkinnedModel model = null)
    {
        AssetsManager m = new AssetsManager();
        m.LoadClassPackage(classData);
        BundleFileInstance bun = m.LoadBundleFile(shipped, true);
        AssetsFileInstance af = m.LoadAssetsFileFromBundle(bun, 0, false);
        m.LoadClassDatabaseFromPackage(af.file.Metadata.UnityVersion);
        try
        {
            AssetFileInfo info = Find(m, af, Target);
            AssetTypeValueField mesh = m.GetBaseField(af, info);
            // Read BEFORE Fill clears it, exactly the way BundleBaker.ReplaceMesh does.
            int inf = SkinFields.InfluencesOf(mesh);
            MeshFields.Fill(mesh, baked);
            if (model == null) SkinFields.Rebind(mesh, baked, inf);
            else Assert(SkinFields.RebindByName(mesh, baked, model, SkinFields.BoneNames(m, af, info.PathId), inf),
                        "the target is rigged, so a by-name rebind has bind poses to work with");
            info.SetNewData(mesh);

            // Without this the bundle writes its ORIGINAL directory entry and the patch vanishes
            // silently - the same line BundleBaker.Write carries.
            bun.file.BlockAndDirInfo.DirectoryInfos[0].SetNewData(af.file);

            AssetBundleCompressionType comp = bun.file.GetCompressionType();
            using (MemoryStream raw = new MemoryStream())
            using (AssetsFileWriter rw = new AssetsFileWriter(raw))
            {
                bun.file.Write(rw);
                rw.Flush();
                raw.Position = 0;
                if (comp == AssetBundleCompressionType.None)
                {
                    using (FileStream f = File.Create(outPath)) raw.CopyTo(f);
                }
                else
                {
                    AssetBundleFile packed = new AssetBundleFile();
                    packed.Read(new AssetsFileReader(raw));
                    using (AssetsFileWriter w = new AssetsFileWriter(outPath)) packed.Pack(w, comp, false, null);
                    packed.Close();
                }
            }
        }
        finally { m.UnloadAll(); }
    }

    private static string Summary(string classData, string bundlePath, string meshName, bool skin = false)
    {
        AssetsManager m = new AssetsManager();
        m.LoadClassPackage(classData);
        BundleFileInstance bun = m.LoadBundleFile(bundlePath, true);
        AssetsFileInstance af = m.LoadAssetsFileFromBundle(bun, 0, false);
        m.LoadClassDatabaseFromPackage(af.file.Metadata.UnityVersion);
        try
        {
            AssetTypeValueField mesh = m.GetBaseField(af, Find(m, af, meshName));
            return skin ? SkinFields.SkinSummary(mesh) : MeshFields.Summary(mesh);
        }
        finally { m.UnloadAll(); }
    }

    /// <summary>The raw vertex + index bytes of a Mesh in a FILE, as one hash - the P4-bytes oracle.</summary>
    private static string Buffers(string classData, string bundlePath, string meshName)
    {
        AssetsManager m = new AssetsManager();
        m.LoadClassPackage(classData);
        BundleFileInstance bun = m.LoadBundleFile(bundlePath, true);
        AssetsFileInstance af = m.LoadAssetsFileFromBundle(bun, 0, false);
        m.LoadClassDatabaseFromPackage(af.file.Metadata.UnityVersion);
        try { return MeshFields.Buffers(m.GetBaseField(af, Find(m, af, meshName))); }
        finally { m.UnloadAll(); }
    }

    /// <summary>
    /// The UNKNOWN answer, off a real field of a real shipped Mesh that is not itself a mesh: every
    /// lookup Buffers makes comes back a dummy, which is what an absent or unreadable buffer looks
    /// like. Defaulting those to an empty array hashed sha256("") vertexBytes=0 indexBytes=0, and the
    /// copy and the shipped file then reported the SAME string - the P4-bytes gate reads that as "the
    /// patch wrote nothing" and reds a correct bake, so the answer has to be "cannot say".
    /// </summary>
    private static bool Unknown(string classData, string bundlePath)
    {
        AssetsManager m = new AssetsManager();
        m.LoadClassPackage(classData);
        BundleFileInstance bun = m.LoadBundleFile(bundlePath, true);
        AssetsFileInstance af = m.LoadAssetsFileFromBundle(bun, 0, false);
        m.LoadClassDatabaseFromPackage(af.file.Metadata.UnityVersion);
        try { return MeshFields.Buffers(m.GetBaseField(af, Find(m, af, Target))["m_LocalAABB"]) == null; }
        finally { m.UnloadAll(); }
    }

    /// <summary>
    /// The STREAMED answer, twice, off the real shipped Mesh with its vertices taken out to a .resS:
    /// one slice at offset 0, one at offset 4096 of the SAME archive entry. Reporting the path alone
    /// made those two identical, so P4-bytes read a correct bake as "the patch wrote nothing".
    /// </summary>
    private static string[] Streamed(string classData, string bundlePath)
    {
        AssetsManager m = new AssetsManager();
        m.LoadClassPackage(classData);
        BundleFileInstance bun = m.LoadBundleFile(bundlePath, true);
        AssetsFileInstance af = m.LoadAssetsFileFromBundle(bun, 0, false);
        m.LoadClassDatabaseFromPackage(af.file.Metadata.UnityVersion);
        try
        {
            AssetTypeValueField mesh = m.GetBaseField(af, Find(m, af, Target));
            AssetTypeValueField data = mesh["m_VertexData"]["m_DataSize"];
            data.Value = new AssetTypeValue(new byte[0], false);
            data.TemplateField.ValueType = AssetValueType.ByteArray;
            AssetTypeValueField sd = mesh["m_StreamData"];
            sd["path"].AsString = "archive:/CAB-x/CAB-x.resS";
            sd["size"].AsUInt = 4096;
            sd["offset"].AsULong = 0;
            string first = MeshFields.Buffers(mesh);
            sd["offset"].AsULong = 4096;
            return new[] { first, MeshFields.Buffers(mesh) };
        }
        finally { m.UnloadAll(); }
    }

    private static AssetFileInfo Find(AssetsManager m, AssetsFileInstance af, string meshName)
    {
        foreach (AssetFileInfo i in af.file.Metadata.GetAssetsOfType(AssetClassID.Mesh))
            if (m.GetBaseField(af, i)["m_Name"].AsString == meshName) return i;
        throw new InvalidOperationException("no Mesh named " + meshName + " in " + af.name);
    }

    private static void Assert(bool ok, string what)
    {
        if (!ok) throw new Exception("MESH round trip FAILED: " + what);
    }
}
