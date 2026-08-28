using System;
using System.IO;
using AssetsTools.NET;
using AssetsTools.NET.Extra;
using Morgott.ContentTool.Bake;
using Morgott.ContentTool.Import;

/// <summary>
/// The AUTHOR's model path, offline: take the sample .glb through the real reader, build it into a
/// copy of a REAL shipped bundle exactly the way BundleBaker does, read the copy back off disk and
/// assert the same one-line oracle the in-game gate reads (<c>SkinFields.Summary</c>).
///
/// The oracle is deliberately double. A round trip alone would only prove the writer and the reader
/// agree with each other, so the numbers that matter - the per-vertex influences and the bind poses -
/// are ALSO checked against the .glb's own declared weights, which are stated once in
/// <see cref="ModelBuild.SampleGlb"/> and read back through <see cref="GlbReader"/>. What only the
/// game can still answer is whether the skin DEFORMS by those weights; the bytes are settled here.
///
/// A missing game install is VOID, never PASS.
/// </summary>
internal static class ModelRoundTrip
{
    private const string Bundle = "mutoid_assets_all.bundle";
    private const string Root = "glbmodel";

    /// <summary>U7: a clip bound to bone 1's path - a CARRIED bone, so the path is not just its
    /// name and a writer that hashed the name alone comes out here.</summary>
    private const string Clip = "glbmodel_lift", Aoc = "glbmodel_aoc";
    private const int ClipBone = 1;

    // The same MEASURED base controller the in-game gate overrides (_common's 'MedKitHeartBeat1'), and
    // the externals index a one-external clone hands out. Nothing here RESOLVES the external - only the
    // game can, which is exactly the half this test does not claim.
    private const long BaseController = -8389213721431673559L, BaseClip = -2054101139859036125L;
    private const int BaseFileId = 2;

    internal static string Run()
    {
        // The weights the sample DECLARES, and therefore what everything below has to reproduce:
        // vertex 0 belongs to the head alone, vertex 1 is split half and half, vertices 2 and 3
        // belong to the hip. Anti-geometric on purpose - the two vertices at y=0 are the ones the
        // HIGH bone owns - so no nearest-bone or split-at-the-centre synthesis can imitate it.
        BakedSkin skin = ModelBuild.From(GlbReader.Read(ModelBuild.SampleGlb()), Root);
        bool named = skin.Rigged && skin.BoneNames.Length == ModelBuild.SampleBones.Length;
        for (int b = 0; named && b < ModelBuild.SampleBones.Length; b++)
            named = skin.BoneNames[b] == ModelBuild.SampleBones[b];
        Assert(named, "the .glb comes back with every bone, in the file's own order: " + skin.Describe());
        Assert(Near(skin.WeightOf(0, 1), 1f) && Near(skin.WeightOf(0, 0), 0f),
               "vertex 0 belongs whole to '" + skin.BoneNames[1] + "': " + skin.WeightOf(0, 1));
        Assert(Near(skin.WeightOf(1, 0), 0.5f) && Near(skin.WeightOf(1, 1), 0.5f),
               "vertex 1 is SHARED half and half - the influence an .obj bake cannot express: " +
               skin.WeightOf(1, 0) + "/" + skin.WeightOf(1, 1));
        Assert(Near(skin.WeightOf(2, 0), 1f) && Near(skin.WeightOf(3, 0), 1f),
               "vertices 2 and 3 belong whole to '" + skin.BoneNames[0] + "'");

        // The PARENT LINKS, which is the whole difference between a rig that poses and one that
        // animates. head and arm are siblings under hip in the file, so a reader that invented a chain
        // (or dropped the tree and left every bone a root) comes out here and nowhere else.
        bool parented = skin.BoneParents != null && skin.BoneParents.Length == ModelBuild.SampleParents.Length;
        for (int b = 0; parented && b < ModelBuild.SampleParents.Length; b++)
            parented = skin.BoneParents[b] == ModelBuild.SampleParents[b];
        Assert(parented, "the .glb's node tree came through: " +
               Tree(skin) + " (the file says " + string.Join(",", ModelBuild.SampleParents) + ")");
        Assert(skin.BonePath(1) == ModelBuild.SampleBones[0] + "/" + ModelBuild.SampleBones[1] &&
               skin.BonePath(2) == ModelBuild.SampleBones[0] + "/" + ModelBuild.SampleBones[2],
               "a carried bone's path - what its hash is the CRC of - runs through its parent: " +
               skin.BonePath(1) + " and " + skin.BonePath(2));
        // Lifting the hip must carry everything; lifting the head must carry the head's share of
        // vertex 5 and NONE of vertex 4, which is what a chain would get wrong.
        Assert(Near(skin.CarriedWeight(4, 0), 1f) && Near(skin.CarriedWeight(4, 1), 0f) &&
               Near(skin.CarriedWeight(5, 1), 0.5f),
               "the hip carries vertex 4 whole (" + skin.CarriedWeight(4, 0) + ") while the head " +
               "carries none of it (" + skin.CarriedWeight(4, 1) + ") and half of vertex 5 (" +
               skin.CarriedWeight(5, 1) + ")");

        // The bind pose says the head rests 4 above the model root, so its INVERSE - where the bake
        // puts the bone - is +4 in WORLD terms, and +3 LOCAL to a hip that itself rests at 1. Getting
        // this backwards is what deforms a model before it ever moves.
        Assert(Near(skin.BindPoses[1][13], -4f) && Near(skin.BoneRest[1][13], 3f) &&
               Near(skin.BoneRest[0][13], 1f),
               "the head's bind pose is a -4 lift and its rest transform the +3 LOCAL to the hip's +1: " +
               skin.BindPoses[1][13] + " / " + skin.BoneRest[1][13] + " / " + skin.BoneRest[0][13]);

        string root = Environment.GetEnvironmentVariable("PPRoot") ?? @"D:\Steam\steamapps\common\Phoenix Point";
        string classData = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"..\..\..\..\..\lib\classdata.tpk");
        string shipped = Path.Combine(root, @"PhoenixPointWin64_Data\StreamingAssets\aa\StandaloneWindows64\" + Bundle);
        if (!File.Exists(shipped)) return "MODEL round trip VOID - no " + shipped + " (set PPRoot to the game folder)";
        if (!File.Exists(classData)) return "MODEL round trip VOID - no " + Path.GetFullPath(classData);

        string copy = Path.Combine(Path.GetTempPath(), "ct-modelroundtrip-" + Bundle);
        string before = Summarize(classData, shipped, Root);
        string materialName = Build(classData, shipped, copy, skin);
        string after = Summarize(classData, copy, Root);
        string clip = ReadClip(classData, copy, Clip);
        string animator = Animator(classData, copy, Root);
        float[] rendererAabb = RendererAabb(classData, copy, Root);
        File.Delete(copy);

        // m_AABB is in ROOT BONE space, not mesh space (SkinFields.RendererAabb). The sample's root
        // bone is the hip, resting +1 above the model root, so its bind pose is a pure -1 lift and
        // the renderer's box must be the mesh's box shifted DOWN by one, extents untouched. A bake
        // that wrote the mesh-space box - which is what shipped until 2026-08-24, and what put a
        // 3.4km AABB on the spider whose root bone carries its exporter's 1/3368.8 unit scale -
        // lands the centre one unit high and comes out here.
        Assert(Near(skin.BindPoses[0][13], -1f), "the sample's root bone rests +1 up: " + skin.BindPoses[0][13]);
        Assert(Near(rendererAabb[0], skin.Mesh.CenterX) &&
               Near(rendererAabb[1], skin.Mesh.CenterY - 1f) &&
               Near(rendererAabb[2], skin.Mesh.CenterZ) &&
               Near(rendererAabb[3], skin.Mesh.ExtentX) &&
               Near(rendererAabb[4], skin.Mesh.ExtentY) &&
               Near(rendererAabb[5], skin.Mesh.ExtentZ),
               "the renderer's m_AABB is the mesh box in the ROOT BONE's space: centre (" +
               rendererAabb[0] + "," + rendererAabb[1] + "," + rendererAabb[2] + ") extent (" +
               rendererAabb[3] + "," + rendererAabb[4] + "," + rendererAabb[5] + ") - wanted centre (" +
               skin.Mesh.CenterX + "," + (skin.Mesh.CenterY - 1f) + "," + skin.Mesh.CenterZ + ")");

        Assert(before.StartsWith("no GameObject", StringComparison.Ordinal),
               "the shipped bundle never had a '" + Root + "': " + before);
        Assert(after == Want(skin, materialName), "the copy holds the model: " + after +
               "\n         (wanted " + Want(skin, materialName) + ")");

        // U7's offline half - the JOIN, with no engine in the loop. The clip's one binding and the
        // mesh's own bone hash are written by two different writers out of one path spelling, and
        // nothing but this compares them: a clip bound to a path the rig does not spell drives
        // NOTHING at runtime, silently, and every other arm here stays green.
        uint hash = SkinFields.BoneHash(skin.BonePath(ClipBone));
        string wantClip = "clip '" + Clip + "' bindings=1 path=" + hash + " attr=" +
                          ClipFields.AttributePosition + " typeID=4";
        Assert(clip.StartsWith(wantClip, StringComparison.Ordinal),
               "the clip binds the CRC of '" + skin.BonePath(ClipBone) + "', the same path the mesh " +
               "hashes into m_BoneNameHashes: " + clip + "\n         (wanted " + wantClip + ")");
        Assert(after.Contains(" hashes=3:" + SkinFields.BoneHash(skin.BonePath(0)) + ":" + hash + ":"),
               "and the MESH carries that same hash for bone " + ClipBone + ": " + after);
        // The shipping shape: the Animator is on the MODEL ROOT - the GameObject those paths are
        // relative to - and it names our override controller. On the root and nowhere else, because a
        // curve bound to "hip/head" means nothing from one level down.
        string wantAnimator = "controller='" + Aoc + "' avatar=0 culling=0 hierarchy=True";
        Assert(animator == wantAnimator, "the model ROOT carries the Animator that plays it: " +
               animator + " (wanted " + wantAnimator + ")");
        return "MODEL round trip PASS on " + Bundle + "\n  " + after + "\n  " + clip +
               "\n  root animator " + animator;
    }

    /// <summary>
    /// The expected line, spelled out rather than read back off the writer: six vertices, three bones
    /// wired the way the FILE parents them - head and arm under hip, so only the hip is a child of the
    /// model root and a bone's hash is the CRC of its whole path - one bind pose each, and the skin in
    /// stream 1 as two float weights then two UInt32 indices. vertex0 is the file's own "all of me to
    /// the head", vertexLast its "half to the arm, half to the head".
    /// </summary>
    private static string Want(BakedSkin skin, string materialName)
    {
        uint h0 = SkinFields.BoneHash(skin.BonePath(0)), h1 = SkinFields.BoneHash(skin.BonePath(1));
        uint h2 = SkinFields.BoneHash(skin.BonePath(2));
        return "root '" + Root + "' children=2" +
               " | skin '" + SkinFields.SkinName(Root) + "'" +
               " bones=3 bone0='" + skin.BoneNames[0] + "' bone1='" + skin.BoneNames[1] + "'" +
               " rootBone='" + skin.BoneNames[0] + "'" +
               " mesh='" + SkinFields.MeshName(Root) + "' material='" + materialName + "'" +
               " | mesh verts=6 bindposes=3 bindpose1.e13=-4" +
               " hashes=3:" + h0 + ":" + h1 + ":" + h2 + " rootHash=" + h0 + " bonesAABB=3" +
               " " + SkinFields.OurLayout +
               " bytes=" + (SkinFields.SkinOffset(6) + 6 * SkinFields.SkinStride) +
               " vertex0=1/0->bone1 vertexLast=0.5/0.5->bone2" +
               " | tree '" + skin.BoneNames[0] + "'<'" + Root + "'#2,'" +
               skin.BoneNames[1] + "'<'" + skin.BoneNames[0] + "'#0,'" +
               skin.BoneNames[2] + "'<'" + skin.BoneNames[0] + "'#0";
    }

    /// <summary>The parent links as read, for a failing line to show.</summary>
    private static string Tree(BakedSkin skin)
    {
        string s = "";
        for (int b = 0; b < skin.BoneNames.Length; b++)
            s += (b == 0 ? "" : ",") + skin.BoneNames[b] + "<" +
                 (skin.BoneParents[b] < 0 ? "root" : skin.BoneNames[skin.BoneParents[b]]);
        return s;
    }

    /// <summary>The BundleBaker write path, minus everything a model does not use.</summary>
    private static string Build(string classData, string shipped, string outPath, BakedSkin skin)
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
            string materialName = null;
            foreach (AssetFileInfo i in af.file.Metadata.GetAssetsOfType(AssetClassID.Material))
            {
                materialPathId = i.PathId;
                materialName = m.GetBaseField(af, i)["m_Name"].AsString;
                break;
            }
            if (materialName == null) throw new InvalidOperationException("no Material in " + af.name);

            // U7: the clip the in-game gate bakes beside the model, and the AnimatorOverrideController
            // that hands it to Mecanim - written the same way (BundleBaker.Add sets m_Name, then
            // Fill*) so the two agree byte for byte. BuildModel then puts the Animator on the ROOT,
            // which is the GameObject the clip's binding paths are relative to.
            float restY = skin.BoneRest[ClipBone][13];
            long clipId = next++, aocId = next++;
            PrefabFields.Create(af.file, m.ClassDatabase, clipId, AssetClassID.AnimationClip, c =>
            {
                c["m_Name"].AsString = Clip;
                ClipFields.FillClip(c, ClipFields.LiftY(skin.BonePath(ClipBone),
                                                       new[] { restY, restY + 10f, restY + 10f }), 3, 1f);
            });
            PrefabFields.Create(af.file, m.ClassDatabase, aocId, AssetClassID.AnimatorOverrideController, a =>
            {
                a["m_Name"].AsString = Aoc;
                ClipFields.FillOverrideController(a, BaseFileId, BaseController, BaseFileId, BaseClip, clipId);
            });

            SkinFields.BuildModel(af.file, m.ClassDatabase, () => next++, Root, skin, materialPathId, aocId);

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
            return materialName;
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

    /// <summary>The model root's Animator, the same line BundleBaker.ReadAnimatorOn reports in game.</summary>
    private static string Animator(string classData, string bundlePath, string rootName)
    {
        AssetsManager m = new AssetsManager();
        m.LoadClassPackage(classData);
        BundleFileInstance bun = m.LoadBundleFile(bundlePath, true);
        AssetsFileInstance af = m.LoadAssetsFileFromBundle(bun, 0, false);
        m.LoadClassDatabaseFromPackage(af.file.Metadata.UnityVersion);
        try
        {
            AssetTypeValueField root = PrefabFields.FindGameObject(m, af, rootName);
            if (root == null) return "(no GameObject '" + rootName + "')";
            AssetTypeValueField a = PrefabFields.Component(m, af, root, AssetClassID.Animator);
            if (a == null) return "(no Animator)";
            // PrefabFields.Name already quotes the name it hands back.
            return "controller=" + PrefabFields.Name(m, af, a["m_Controller"]["m_PathID"].AsLong) +
                   " avatar=" + a["m_Avatar"]["m_PathID"].AsLong +
                   " culling=" + a["m_CullingMode"].AsInt +
                   " hierarchy=" + a["m_HasTransformHierarchy"].AsBool;
        }
        finally { m.UnloadAll(); }
    }

    /// <summary>The SkinnedMeshRenderer's serialized m_AABB: centre xyz then half-extent xyz.</summary>
    private static float[] RendererAabb(string classData, string bundlePath, string rootName)
    {
        AssetsManager m = new AssetsManager();
        m.LoadClassPackage(classData);
        BundleFileInstance bun = m.LoadBundleFile(bundlePath, true);
        AssetsFileInstance af = m.LoadAssetsFileFromBundle(bun, 0, false);
        m.LoadClassDatabaseFromPackage(af.file.Metadata.UnityVersion);
        try
        {
            AssetTypeValueField skinGo = PrefabFields.FindGameObject(m, af, SkinFields.SkinName(rootName));
            AssetTypeValueField r = PrefabFields.Component(m, af, skinGo, AssetClassID.SkinnedMeshRenderer);
            AssetTypeValueField c = r["m_AABB"]["m_Center"], e = r["m_AABB"]["m_Extent"];
            return new[] { c["x"].AsFloat, c["y"].AsFloat, c["z"].AsFloat,
                           e["x"].AsFloat, e["y"].AsFloat, e["z"].AsFloat };
        }
        finally { m.UnloadAll(); }
    }

    /// <summary>The clip half of the copy, read the same way the in-game U7-wrote arm reads it.</summary>
    private static string ReadClip(string classData, string bundlePath, string clipName)
    {
        AssetsManager m = new AssetsManager();
        m.LoadClassPackage(classData);
        BundleFileInstance bun = m.LoadBundleFile(bundlePath, true);
        AssetsFileInstance af = m.LoadAssetsFileFromBundle(bun, 0, false);
        m.LoadClassDatabaseFromPackage(af.file.Metadata.UnityVersion);
        try { return ClipFields.Summary(m, af, clipName, null); }
        finally { m.UnloadAll(); }
    }

    private static bool Near(float a, float b) { return Math.Abs(a - b) < 1e-4f; }

    private static void Assert(bool ok, string what)
    {
        if (!ok) throw new Exception("MODEL round trip FAILED: " + what);
    }
}
