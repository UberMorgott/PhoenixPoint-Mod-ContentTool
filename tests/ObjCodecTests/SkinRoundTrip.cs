using System;
using System.IO;
using AssetsTools.NET;
using AssetsTools.NET.Extra;
using Morgott.ContentTool.Bake;
using Morgott.ContentTool.Import;

/// <summary>
/// U5 offline: build a SKINNED hierarchy - bones, bind poses, bone weights and a Mesh created from
/// the EMPTY class-database template - into a copy of a REAL shipped bundle, repack it the way
/// BundleBaker does, read the copy back off disk and assert the same one-line oracle the in-game gate
/// reads (<c>SkinFields.Summary</c>). What only the game can still answer is whether the skin
/// DEFORMS; the bytes are settled here.
///
/// A missing game install is VOID, never PASS.
/// </summary>
internal static class SkinRoundTrip
{
    private const string Bundle = "mutoid_assets_all.bundle";
    private const string Root = "u5_root", Flat = "u5_flat", Rebound = "u5b_rebind";
    private const float BoneY = 2f;

    /// <summary>A 1x2 quad: two vertices at y=0 and two at y=2, so a split at the centre is one row each.</summary>
    private const string Quad = "v 0 0 0\nv 1 0 0\nv 1 2 0\nv 0 2 0\nvn 0 0 1\nf 1//1 2//1 3//1 4//1\n";

    internal static string Run()
    {
        // The hash function itself, pinned to SHIPPED data rather than to our own writer:
        // aln_fireworm_assets_all.bundle's Mesh 'ALN_Fireworm' carries 638923553 for the bone whose
        // model-relative path is Fireworm_root/Fireworm_head (measured 2026-08-12).
        Assert(SkinFields.BoneHash("Fireworm_root/Fireworm_head") == 638923553,
               "m_BoneNameHashes is CRC-32 of the model-relative PATH: got " +
               SkinFields.BoneHash("Fireworm_root/Fireworm_head") + ", aln_fireworm ships 638923553");
        Assert(SkinFields.BoneHash("Fireworm_head") != 638923553,
               "CONTROL the bone NAME alone hashes to something else");

        string root = Environment.GetEnvironmentVariable("PPRoot") ?? @"D:\Steam\steamapps\common\Phoenix Point";
        string classData = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"..\..\..\..\..\lib\classdata.tpk");
        string shipped = Path.Combine(root, @"PhoenixPointWin64_Data\StreamingAssets\aa\StandaloneWindows64\" + Bundle);
        if (!File.Exists(shipped)) return "SKIN round trip VOID - no " + shipped + " (set PPRoot to the game folder)";
        if (!File.Exists(classData)) return "SKIN round trip VOID - no " + Path.GetFullPath(classData);

        BakedMesh baked = MeshBuild.From(ObjCodec.Parse(Quad));
        string copy = Path.Combine(Path.GetTempPath(), "ct-skinroundtrip-" + Bundle);
        string before = Summarize(classData, shipped, Root);
        string materialName;
        Build(classData, shipped, copy, baked, out materialName);
        string after = Summarize(classData, copy, Root), flat = Summarize(classData, copy, Flat);
        string rebound = Summarize(classData, copy, Rebound);
        File.Delete(copy);

        Assert(after == Want(Root, baked, materialName, true),
               "the copy holds the skin: " + after + " (wanted " + Want(Root, baked, materialName, true) + ")");
        Assert(flat == Want(Flat, baked, materialName, false),
               "CONTROL every vertex on bone0: " + flat + " (wanted " + Want(Flat, baked, materialName, false) + ")");
        Assert(before.StartsWith("no GameObject", StringComparison.Ordinal),
               "the shipped bundle never had a '" + Root + "': " + before);
        // The SPLIT expectation on a bake that asked for none: nearest-bindpose produced it, and the
        // byte-identical bake without the Rebind call is the `flat` line above.
        Assert(rebound == Want(Rebound, baked, materialName, true),
               "Rebind put the top row on bone1: " + rebound +
               " (wanted " + Want(Rebound, baked, materialName, true) + ")");
        return "SKIN round trip PASS on " + Bundle + "\n  " + after + "\n  control " + flat +
               "\n  rebind  " + rebound;
    }

    /// <summary>
    /// The expected line, spelled out rather than read back off the writer: 4 vertices, the bottom
    /// row on bone0 and (when split) the top row on bone1, bind pose 1 the inverse of a 2-unit lift,
    /// skin data in stream 1 as 2 float weights (format 0) then 2 UInt32 indices (format 10).
    /// </summary>
    private static string Want(string rootName, BakedMesh baked, string materialName, bool split)
    {
        uint h0 = SkinFields.BoneHash(SkinFields.Bone0Name(rootName));
        uint h1 = SkinFields.BoneHash(SkinFields.Bone0Name(rootName) + "/" + SkinFields.Bone1Name(rootName));
        return "root '" + rootName + "' children=2" +
               " | skin '" + SkinFields.SkinName(rootName) + "'" +
               " bones=2 bone0='" + SkinFields.Bone0Name(rootName) + "' bone1='" + SkinFields.Bone1Name(rootName) + "'" +
               " rootBone='" + SkinFields.Bone0Name(rootName) + "'" +
               " mesh='" + SkinFields.MeshName(rootName) + "' material='" + materialName + "'" +
               " | mesh verts=4 bindposes=2 bindpose1.e13=-2" +
               " hashes=2:" + h0 + ":" + h1 + " rootHash=" + h0 + " bonesAABB=2" +
               " weightCh=stream1/off0/fmt0/dim2 indexCh=stream1/off8/fmt10/dim2" +
               " bytes=" + (SkinFields.SkinOffset(4) +
                            4 * SkinFields.SkinStride(SkinFields.FixtureInfluences)) +
               " vertex0=1/0->bone0 vertexLast=1/0->bone" + (split ? 1 : 0) +
               // bone1 hangs off bone0 - the parenting, off the written bytes rather than off
               // whatever the engine chooses to report about it.
               " | tree '" + SkinFields.Bone0Name(rootName) + "'<'" + rootName + "'#1,'" +
               SkinFields.Bone1Name(rootName) + "'<'" + SkinFields.Bone0Name(rootName) + "'#0";
    }

    /// <summary>The BundleBaker write path, minus everything a skin does not use.</summary>
    private static void Build(string classData, string shipped, string outPath, BakedMesh baked,
                              out string materialName)
    {
        AssetsManager m = new AssetsManager();
        m.LoadClassPackage(classData);
        BundleFileInstance bun = m.LoadBundleFile(shipped, true);
        AssetsFileInstance af = m.LoadAssetsFileFromBundle(bun, 0, false);
        m.LoadClassDatabaseFromPackage(af.file.Metadata.UnityVersion);
        try
        {
            long next = 0;
            foreach (AssetFileInfo a in af.file.Metadata.AssetInfos) if (a.PathId > next) next = a.PathId;
            next += 1000;

            long materialPathId = 0;
            materialName = null;
            foreach (AssetFileInfo i in af.file.Metadata.GetAssetsOfType(AssetClassID.Material))
            {
                materialPathId = i.PathId;
                materialName = m.GetBaseField(af, i)["m_Name"].AsString;
                break;
            }
            if (materialName == null) throw new InvalidOperationException("no Material in " + af.name);

            SkinFields.Build(af.file, m.ClassDatabase, () => next++, Root, baked, materialPathId, BoneY, true, false);
            SkinFields.Build(af.file, m.ClassDatabase, () => next++, Flat, baked, materialPathId, BoneY, false, false);
            // Baked FLAT and then re-bound - the replacement path's weighting, so the split below can
            // only have come from SkinFields.Rebind reading the bind poses.
            SkinFields.Build(af.file, m.ClassDatabase, () => next++, Rebound, baked, materialPathId, BoneY, false, true);

            // Without this the bundle writes its ORIGINAL directory entry and everything added
            // vanishes silently - the same line BundleBaker.Write carries.
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

    private static string Summarize(string classData, string bundlePath, string rootName)
    {
        AssetsManager m = new AssetsManager();
        m.LoadClassPackage(classData);
        BundleFileInstance bun = m.LoadBundleFile(bundlePath, true);
        AssetsFileInstance af = m.LoadAssetsFileFromBundle(bun, 0, false);
        m.LoadClassDatabaseFromPackage(af.file.Metadata.UnityVersion);
        try { return SkinFields.Summary(m, af, rootName); }
        finally { m.UnloadAll(); }
    }

    private static void Assert(bool ok, string what)
    {
        if (!ok) throw new Exception("SKIN round trip FAILED: " + what);
    }
}
