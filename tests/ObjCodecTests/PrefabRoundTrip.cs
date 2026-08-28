using System;
using System.IO;
using AssetsTools.NET;
using AssetsTools.NET.Extra;
using Morgott.ContentTool.Bake;

/// <summary>
/// U4 offline: build a hierarchy into a copy of a REAL shipped bundle, repack it the way BundleBaker
/// does, read the copy back off disk and assert the same one-line oracle the in-game gate reads
/// (<c>PrefabFields.Summary</c>). What only the game can still answer is whether Unity BUILDS a
/// hierarchy out of these bytes - the bytes themselves are settled here.
///
/// A missing game install is VOID, never PASS.
/// </summary>
internal static class PrefabRoundTrip
{
    private const string Bundle = "mutoid_assets_all.bundle";
    private const string Mesh = "Geo_Head02_V01";
    private const string Root = "u4_root", Child = "u4_child", Lone = "u4_lone";

    internal static string Run()
    {
        string root = Environment.GetEnvironmentVariable("PPRoot") ?? @"D:\Steam\steamapps\common\Phoenix Point";
        string classData = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"..\..\..\..\..\lib\classdata.tpk");
        string shipped = Path.Combine(root, @"PhoenixPointWin64_Data\StreamingAssets\aa\StandaloneWindows64\" + Bundle);
        if (!File.Exists(shipped)) return "PREFAB round trip VOID - no " + shipped + " (set PPRoot to the game folder)";
        if (!File.Exists(classData)) return "PREFAB round trip VOID - no " + Path.GetFullPath(classData);

        string copy = Path.Combine(Path.GetTempPath(), "ct-prefabroundtrip-" + Bundle);
        string before = Summarize(classData, shipped, Root);
        string materialName;
        long meshPathId = Build(classData, shipped, copy, out materialName);
        string after = Summarize(classData, copy, Root), lone = Summarize(classData, copy, Lone);
        File.Delete(copy);

        string want = "root '" + Root + "' comps=1 children=1 | child '" + Child + "' comps=3 " +
                      "fatherIsRoot=True pos=1.5,-2.25,3.75 scale=1,1,1 mesh='" + Mesh +
                      "' material='" + materialName + "'";
        Assert(after == want, "the copy holds the hierarchy: " + after + " (wanted " + want + ")");
        Assert(before.StartsWith("no GameObject", StringComparison.Ordinal),
               "the shipped bundle never had a '" + Root + "': " + before);
        Assert(lone == "root '" + Lone + "' comps=1 children=0",
               "CONTROL a root baked with no child reports no children: " + lone);
        return "PREFAB round trip PASS on " + Bundle + " (MeshFilter -> pathId " + meshPathId + ")\n  " + after +
               "\n  control " + lone;
    }

    /// <summary>The BundleBaker write path, minus everything a hierarchy does not use.</summary>
    private static long Build(string classData, string shipped, string outPath, out string materialName)
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

            long meshPathId = Find(m, af, AssetClassID.Mesh, Mesh);
            // Whichever Material the bundle happens to ship - the gate is that the PPtr resolves back
            // to THAT object, so the name is read out of the file rather than assumed.
            long materialPathId = 0;
            materialName = null;
            foreach (AssetFileInfo i in af.file.Metadata.GetAssetsOfType(AssetClassID.Material))
            {
                materialPathId = i.PathId;
                materialName = m.GetBaseField(af, i)["m_Name"].AsString;
                break;
            }
            if (materialName == null) throw new InvalidOperationException("no Material in " + af.name);

            PrefabFields.Build(af.file, m.ClassDatabase, () => next++, Root, Child,
                               meshPathId, new[] { materialPathId }, 1.5f, -2.25f, 3.75f);
            PrefabFields.Build(af.file, m.ClassDatabase, () => next++, Lone, null, 0, new long[] { 0 }, 0f, 0f, 0f);

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
            return meshPathId;
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
        try { return PrefabFields.Summary(m, af, rootName); }
        finally { m.UnloadAll(); }
    }

    private static long Find(AssetsManager m, AssetsFileInstance af, AssetClassID cls, string name)
    {
        foreach (AssetFileInfo i in af.file.Metadata.GetAssetsOfType(cls))
            if (m.GetBaseField(af, i)["m_Name"].AsString == name) return i.PathId;
        throw new InvalidOperationException("no " + cls + " named " + name + " in " + af.name);
    }

    private static void Assert(bool ok, string what)
    {
        if (!ok) throw new Exception("PREFAB round trip FAILED: " + what);
    }
}
