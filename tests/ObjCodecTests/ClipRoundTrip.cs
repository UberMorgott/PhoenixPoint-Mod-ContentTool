using System;
using System.Collections.Generic;
using System.IO;
using AssetsTools.NET;
using AssetsTools.NET.Extra;
using Morgott.ContentTool.Bake;

/// <summary>
/// U6 offline: build an AnimationClip, an AnimatorOverrideController and the Animator hierarchy that
/// plays it into a copy of a REAL shipped bundle, repack it the way BundleBaker does, read the copy
/// back off disk and assert the same one-line oracle the in-game gate reads
/// (<c>ClipFields.Summary</c>). What only the game can still answer is whether Mecanim ACCEPTS the
/// clip; the bytes are settled here.
///
/// The two numbers that identify the format are pinned to SHIPPED data, not to our writer: the
/// binding path hash and the m_MuscleClipSize formula are both checked against
/// aln_fireworm_assets_all.bundle's own 'Fireworm_unfurl'.
///
/// A missing game install is VOID, never PASS.
/// </summary>
internal static class ClipRoundTrip
{
    private const string Bundle = "mutoid_assets_all.bundle";
    private const string Fireworm = "aln_fireworm_assets_all.bundle";
    private const string Clip = "u6_ramp", Aoc = "u6_aoc_ramp", Root = "u6_root", Bone = "u6_bone";
    private const float Rate = 30f, RestY = -3.5f;
    private const int Frames = 31;

    // MEASURED off _common_assets_all.bundle 2026-08-12, the same constants the in-game gate uses.
    private const long BaseController = -8389213721431673559L, BaseClip = -2054101139859036125L;
    /// <summary>
    /// The externals index the in-game bake gets back from AddExternal. Nothing here RESOLVES it -
    /// only the game can, and that is exactly the half this test does not claim - so any index does
    /// as long as the bytes come back carrying it. 2 is what a one-external clone hands out.
    /// </summary>
    private const int BaseFileId = 2;

    internal static string Run()
    {
        string root = Environment.GetEnvironmentVariable("PPRoot") ?? @"D:\Steam\steamapps\common\Phoenix Point";
        string classData = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"..\..\..\..\..\lib\classdata.tpk");
        string aa = Path.Combine(root, @"PhoenixPointWin64_Data\StreamingAssets\aa\StandaloneWindows64");
        string shipped = Path.Combine(aa, Bundle);
        if (!File.Exists(shipped)) return "CLIP round trip VOID - no " + shipped + " (set PPRoot to the game folder)";
        if (!File.Exists(classData)) return "CLIP round trip VOID - no " + Path.GetFullPath(classData);

        string pinned = Pin(classData, Path.Combine(aa, Fireworm));

        string copy = Path.Combine(Path.GetTempPath(), "ct-cliproundtrip-" + Bundle);
        string before = Summarize(classData, shipped);
        Build(classData, shipped, copy);
        string after = Summarize(classData, copy);
        string hierarchy = Hierarchy(classData, copy);

        Assert(before.StartsWith("no AnimationClip", StringComparison.Ordinal),
               "the shipped bundle never had a '" + Clip + "': " + before);
        Assert(after == Want(), "the copy holds the clip: " + after + " (wanted " + Want() + ")");
        string wantHierarchy = "root '" + Root + "' animator controller='" + Aoc +
                               "' avatar=0 culling=0 hierarchy=True | bone='" + Bone + "' rest=0,-3.5,0";
        Assert(hierarchy == wantHierarchy, "the Animator points at the controller: " + hierarchy +
               " (wanted " + wantHierarchy + ")");
        // m_AnimationType, MEASURED on both sides rather than assumed. A remembered "Generic = 2" is
        // exactly the kind of constant that loads fine and drives nothing, so what a SHIPPED generic
        // clip carries is read in the same run as what our writer leaves - and if the two ever part,
        // this line names both numbers instead of a gate going quietly red in the game.
        string shippedType = AnimType(classData, Path.Combine(aa, Fireworm), null);
        string ourType = AnimType(classData, copy, Clip);
        File.Delete(copy);
        Assert(shippedType == ourType, "our clip's m_AnimationType is what a shipped generic clip " +
               "carries: shipped " + shippedType + ", ours " + ourType);

        string edited = Edit(classData, Path.Combine(aa, Fireworm));

        return "CLIP round trip PASS on " + Bundle + "\n  " + pinned + "\n  " + after +
               "\n  m_AnimationType shipped " + shippedType + " == ours " + ourType + "\n  " +
               hierarchy + "\n" + edited;
    }

    /// <summary>
    /// The format identified against SHIPPED data rather than against this writer: 'Fireworm_unfurl'
    /// binds the path hash of a bone the same bundle's hierarchy actually spells, and its own
    /// m_MuscleClipSize is what <see cref="ClipFields.MuscleClipSize"/> computes from its bank sizes.
    /// </summary>
    private static string Pin(string classData, string fireworm)
    {
        if (!File.Exists(fireworm)) return "PIN VOID - no " + fireworm;
        AssetsManager m = new AssetsManager();
        m.LoadClassPackage(classData);
        BundleFileInstance bun = m.LoadBundleFile(fireworm, true);
        AssetsFileInstance af = m.LoadAssetsFileFromBundle(bun, 0, false);
        m.LoadClassDatabaseFromPackage(af.file.Metadata.UnityVersion);
        try
        {
            AssetTypeValueField clip = null;
            foreach (AssetFileInfo i in af.file.Metadata.GetAssetsOfType(AssetClassID.AnimationClip))
            { clip = m.GetBaseField(af, i); break; }
            if (clip == null) return "PIN VOID - no AnimationClip in " + fireworm;

            uint want = SkinFields.BoneHash("Fireworm_root/Fireworm_base");
            bool bound = false;
            foreach (AssetTypeValueField b in clip["m_ClipBindingConstant"]["genericBindings"]["Array"].Children)
                if (b["path"].AsUInt == want && b["attribute"].AsUInt == ClipFields.AttributePosition &&
                    b["typeID"].AsInt == 4) bound = true;
            Assert(bound, "a genericBinding path is CRC-32 of the transform PATH: '" +
                          "Fireworm_root/Fireworm_base' hashes " + want + ", which " + Fireworm +
                          "'s clip does not bind as a Transform position");
            Assert(SkinFields.BoneHash("Fireworm_base") != want,
                   "CONTROL the bone NAME alone hashes to something else");

            AssetTypeValueField data = clip["m_MuscleClip"]["m_Clip"]["data"];
            int streamed = data["m_StreamedClip"]["data"]["Array"].Children.Count;
            int dense = data["m_DenseClip"]["m_SampleArray"]["Array"].Children.Count;
            int constant = data["m_ConstantClip"]["data"]["Array"].Children.Count;
            int delta = clip["m_MuscleClip"]["m_ValueArrayDelta"]["Array"].Children.Count;
            uint got = ClipFields.MuscleClipSize(streamed, dense, constant, delta);
            uint shippedSize = clip["m_MuscleClipSize"].AsUInt;
            Assert(got == shippedSize,
                   "m_MuscleClipSize from the bank sizes: computed " + got + ", '" +
                   clip["m_Name"].AsString + "' ships " + shippedSize);
            return "PIN '" + clip["m_Name"].AsString + "' path hash " + want + " bound, muscleSize " +
                   shippedSize + " = computed from " + streamed + "/" + dense + "/" + constant + "/" + delta;
        }
        finally { m.UnloadAll(); }
    }

    /// <summary>
    /// One AnimationClip's m_AnimationType, as text so a field the class database does not have reads
    /// "(no field)" on BOTH sides instead of silently comparing two zeroes.
    /// </summary>
    /// <param name="name">null takes the file's first AnimationClip.</param>
    private static string AnimType(string classData, string bundlePath, string name)
    {
        if (!File.Exists(bundlePath)) return "(no bundle)";
        AssetsManager m = new AssetsManager();
        m.LoadClassPackage(classData);
        BundleFileInstance bun = m.LoadBundleFile(bundlePath, true);
        AssetsFileInstance af = m.LoadAssetsFileFromBundle(bun, 0, false);
        m.LoadClassDatabaseFromPackage(af.file.Metadata.UnityVersion);
        try
        {
            foreach (AssetFileInfo i in af.file.Metadata.GetAssetsOfType(AssetClassID.AnimationClip))
            {
                AssetTypeValueField clip = m.GetBaseField(af, i);
                if (name != null && clip["m_Name"].AsString != name) continue;
                AssetTypeValueField t = clip["m_AnimationType"];
                return t.IsDummy ? "(no field)" : t.AsInt.ToString();
            }
            return "(no clip)";
        }
        finally { m.UnloadAll(); }
    }

    /// <summary>
    /// The expected line, spelled out rather than read back off the writer: one Transform position
    /// binding, 31 dense frames of 3 curves at 30 Hz, the empty streamed bank's terminating frame,
    /// and an override controller whose base is an external PPtr.
    /// </summary>
    private static string Want()
    {
        return "clip '" + Clip + "' bindings=1 path=" + SkinFields.BoneHash(Bone) +
               " attr=1 typeID=4 dense=31x3@30 samples=93 first=(0,0,0) last=(0,30,0)" +
               " streamed=2/0 const=0 delta=3 index=200 stop=1" +
               " muscleSize=" + ClipFields.MuscleClipSize(2, Frames * 3, 0, 3) + " legacy=False" +
               " | aoc '" + Aoc + "' controller=fileID" + BaseFileId + "/pathID" + BaseController +
               " overrides=1 original=fileID" + BaseFileId + "/pathID" + BaseClip +
               " override='" + Clip + "'";
    }

    /// <summary>The BundleBaker write path, minus everything a clip does not use.</summary>
    private static void Build(string classData, string shipped, string outPath)
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

            float[] ramp = new float[Frames];
            for (int i = 0; i < Frames; i++) ramp[i] = i;

            long clipId = next++;
            PrefabFields.Create(af.file, m.ClassDatabase, clipId, AssetClassID.AnimationClip, bf =>
            {
                bf["m_Name"].AsString = Clip;
                ClipFields.FillClip(bf, ClipFields.LiftY(Bone, ramp), Frames, Rate);
            });
            long aocId = next++;
            PrefabFields.Create(af.file, m.ClassDatabase, aocId, AssetClassID.AnimatorOverrideController, bf =>
            {
                bf["m_Name"].AsString = Aoc;
                ClipFields.FillOverrideController(bf, BaseFileId, BaseController, BaseFileId, BaseClip, clipId);
            });
            ClipFields.Build(af.file, m.ClassDatabase, () => next++, Root, Bone, RestY, aocId);

            Pack(bun, af, outPath);
        }
        finally { m.UnloadAll(); }
    }

    /// <summary>
    /// The repack half of BundleBaker.Write, shared by the two round trips below it - a clip EDIT
    /// has to survive the same LZ4 block layout an added clip does, so it goes out the same way.
    /// </summary>
    internal static void Pack(BundleFileInstance bun, AssetsFileInstance af, string outPath)
    {
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

    private static string Summarize(string classData, string bundlePath)
    {
        return Ask(classData, bundlePath, (m, af) => ClipFields.Summary(m, af, Clip, Aoc));
    }

    private static string Hierarchy(string classData, string bundlePath)
    {
        return Ask(classData, bundlePath, (m, af) => ClipFields.HierarchySummary(m, af, Root));
    }

    private static string Ask(string classData, string bundlePath,
                              Func<AssetsManager, AssetsFileInstance, string> question)
    {
        AssetsManager m = new AssetsManager();
        m.LoadClassPackage(classData);
        BundleFileInstance bun = m.LoadBundleFile(bundlePath, true);
        AssetsFileInstance af = m.LoadAssetsFileFromBundle(bun, 0, false);
        m.LoadClassDatabaseFromPackage(af.file.Metadata.UnityVersion);
        try { return question(m, af); }
        finally { m.UnloadAll(); }
    }

    // ---------------------------------------------------------------- editing a SHIPPED clip

    /// <summary>The clip the edit round trip runs on, and the channel and factor it edits.</summary>
    private const string EditClip = "Fireworm_unfurl";
    private const float EditFactor = 3f;

    /// <summary>
    /// The other half of the animation story: take a clip the GAME ships, scale one channel of its
    /// curves, repack, read the copy back and assert every float against the shipped one times the
    /// author's factor. The expectation is the shipped file read in this same run - never a constant
    /// written here - so an edit that landed on the wrong curve or on the wrong bank cannot agree.
    ///
    /// 'Fireworm_unfurl' is chosen because it uses TWO banks (47 streamed curves + 53 constant
    /// floats, dense empty, measured 2026-08-12): an editor that only knew the dense bank a BAKED
    /// clip uses would silently change nothing here, and this run would catch it.
    /// </summary>
    private static string Edit(string classData, string fireworm)
    {
        if (!File.Exists(fireworm)) return "CLIP edit VOID - no " + fireworm;
        string copy = Path.Combine(Path.GetTempPath(), "ct-clipedit-" + Fireworm);
        string how, ignore;
        uint sizeBefore, sizeAfter;

        AssetsManager m = new AssetsManager();
        m.LoadClassPackage(classData);
        BundleFileInstance bun = m.LoadBundleFile(fireworm, true);
        AssetsFileInstance af = m.LoadAssetsFileFromBundle(bun, 0, false);
        m.LoadClassDatabaseFromPackage(af.file.Metadata.UnityVersion);
        List<float> was, wasRotation;
        try
        {
            AssetFileInfo info = AssetIndex.FindUnique(m, af, AssetClassID.AnimationClip, EditClip, fireworm);
            AssetTypeValueField clip = m.GetBaseField(af, info);
            sizeBefore = clip["m_MuscleClipSize"].AsUInt;
            was = ClipFields.MapCurves(clip, ClipFields.AttributePosition, v => v, out ignore);
            wasRotation = ClipFields.MapCurves(clip, ClipFields.AttributeRotation, v => v, out ignore);
            ClipFields.MapCurves(clip, ClipFields.AttributePosition, v => v * EditFactor, out how);
            info.SetNewData(clip);
            Pack(bun, af, copy);
        }
        finally { m.UnloadAll(); }

        List<float> now, nowRotation;
        AssetsManager r = new AssetsManager();
        r.LoadClassPackage(classData);
        BundleFileInstance rbun = r.LoadBundleFile(copy, true);
        AssetsFileInstance raf = r.LoadAssetsFileFromBundle(rbun, 0, false);
        r.LoadClassDatabaseFromPackage(raf.file.Metadata.UnityVersion);
        try
        {
            AssetFileInfo info = AssetIndex.FindUnique(r, raf, AssetClassID.AnimationClip, EditClip, copy);
            AssetTypeValueField clip = r.GetBaseField(raf, info);
            sizeAfter = clip["m_MuscleClipSize"].AsUInt;
            now = ClipFields.MapCurves(clip, ClipFields.AttributePosition, v => v, out ignore);
            nowRotation = ClipFields.MapCurves(clip, ClipFields.AttributeRotation, v => v, out ignore);
        }
        finally { r.UnloadAll(); }
        File.Delete(copy);

        Assert(was.Count > 0 && now.Count == was.Count,
               "the copy holds the same " + was.Count + " position float(s) (" + now.Count + ")");
        // Both banks, or the run measured only the easy one.
        Assert(!how.Contains(" 0 streamed key(s)") && !how.Contains(" 0 constant value(s)"),
               "the edit reached BOTH banks the clip uses: " + how);
        int loudest = 0;
        for (int i = 0; i < was.Count; i++)
        {
            if (Math.Abs(was[i]) > Math.Abs(was[loudest])) loudest = i;
            Assert(Math.Abs(now[i] - was[i] * EditFactor) <= 1e-5f + 1e-4f * Math.Abs(was[i] * EditFactor),
                   "float " + i + " is " + was[i] + " x " + EditFactor + " = " + was[i] * EditFactor +
                   ", the copy reads " + now[i]);
        }
        Assert(Math.Abs(was[loudest]) > 0.01f,
               "the largest float the edit moved is " + was[loudest] + ", which is not a number this " +
               "round trip can tell from float noise");
        // The control asserts the untouched channel's POSITIVE identity - its own shipped values.
        Assert(wasRotation.Count > 0 && nowRotation.Count == wasRotation.Count,
               "the copy holds the same " + wasRotation.Count + " rotation float(s)");
        for (int i = 0; i < wasRotation.Count; i++)
            Assert(nowRotation[i] == wasRotation[i],
                   "rotation float " + i + " is untouched: shipped " + wasRotation[i] + ", copy " + nowRotation[i]);
        // Scaling values changes no bank SIZE, so the runtime size must be the shipped one.
        Assert(sizeAfter == sizeBefore,
               "m_MuscleClipSize is unchanged: " + sizeBefore + " -> " + sizeAfter);

        return "CLIP edit PASS on " + Fireworm + " '" + EditClip + "' position*" + EditFactor +
               "\n  " + how + "\n  largest float " + loudest + ": " + was[loudest] + " -> " + now[loudest] +
               ", " + wasRotation.Count + " rotation float(s) untouched, m_MuscleClipSize " + sizeAfter;
    }

    private static void Assert(bool ok, string what)
    {
        if (!ok) throw new Exception("CLIP round trip FAILED: " + what);
    }
}
