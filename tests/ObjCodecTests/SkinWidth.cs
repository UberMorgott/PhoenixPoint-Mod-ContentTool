using System;
using System.IO;
using AssetsTools.NET;
using AssetsTools.NET.Extra;
using Morgott.ContentTool.Bake;
using Morgott.ContentTool.Import;

/// <summary>
/// THE INFLUENCE COUNT IS THE TARGET'S, NOT OURS. Phoenix Point ships skinned meshes of both widths -
/// mutoid and aln_fireworm declare 2 influences per vertex, every Phoenix Assault body part declares
/// 4 - so a replacement that always wrote 2 DOWNGRADED half the characters in the game. Measured, not
/// remembered: this arm reads both widths off the shipped bundles in the same run and asserts that a
/// replacement of each keeps what it found.
///
/// No repack. Everything here is settled in the serialized field tree the same reader
/// (<c>SkinFields.SkinSummary</c>) walks after a write, and the write -&gt; repack -&gt; read fidelity
/// of that tree is what MeshRoundTrip already proves on the same bundles.
///
/// A missing game install is VOID, never PASS.
/// </summary>
internal static class SkinWidth
{
    private const string Wide = "px_assault_assets_all.bundle", WideMesh = "CHR_PX_ASS_TS_M_V01_02";
    private const string Narrow = "mutoid_assets_all.bundle", NarrowMesh = "ALN_Siren_Arm_Slasher_Right";

    internal static string Run()
    {
        string root = Environment.GetEnvironmentVariable("PPRoot") ?? @"D:\Steam\steamapps\common\Phoenix Point";
        string classData = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"..\..\..\..\..\lib\classdata.tpk");
        string dir = Path.Combine(root, @"PhoenixPointWin64_Data\StreamingAssets\aa\StandaloneWindows64");
        string wide = Path.Combine(dir, Wide), narrow = Path.Combine(dir, Narrow);
        if (!File.Exists(wide)) return "SKIN width VOID - no " + wide + " (set PPRoot to the game folder)";
        if (!File.Exists(narrow)) return "SKIN width VOID - no " + narrow;
        if (!File.Exists(classData)) return "SKIN width VOID - no " + Path.GetFullPath(classData);

        // THE PREMISE, off the shipped files: the game really does ship both widths. Without this the
        // three arms below could all pass on a game that only ever had one.
        int shippedWide = Declared(classData, wide, WideMesh);
        int shippedNarrow = Declared(classData, narrow, NarrowMesh);
        Assert(shippedWide == 4, "the shipped '" + WideMesh + "' declares FOUR influences per vertex: " +
               shippedWide);
        Assert(shippedNarrow == 2, "the shipped '" + NarrowMesh + "' declares TWO: " + shippedNarrow);

        string keptWide = Replace(classData, wide, WideMesh);
        string keptNarrow = Replace(classData, narrow, NarrowMesh);
        Assert(keptWide.Contains(SkinFields.OurLayout(4)),
               "a dim4 target STAYS dim4 through a by-name replacement: " + keptWide);
        Assert(keptNarrow.Contains(SkinFields.OurLayout(2)),
               "a dim2 target stays dim2 - truncating to two is correct THERE: " + keptNarrow);
        Assert(keptWide.EndsWith(" inRange=yes", StringComparison.Ordinal) &&
               keptNarrow.EndsWith(" inRange=yes", StringComparison.Ordinal),
               "every bone index in both names a bind pose the mesh has: " + keptWide + " | " + keptNarrow);

        // The FILE's four weights all survive on the wide target and only the two heaviest on the
        // narrow one - read back out of the written bytes, per vertex.
        string wideInf = Influences(classData, wide, WideMesh);
        string narrowInf = Influences(classData, narrow, NarrowMesh);
        Assert(wideInf.StartsWith("v0=0.4/0.3/0.2/0.1->bone", StringComparison.Ordinal),
               "the dim4 target carries ALL FOUR of the file's influences, renormalised: " + wideInf);
        Assert(narrowInf.StartsWith("v0=0.571/0.429->bone", StringComparison.Ordinal),
               "the dim2 target carries the two heaviest, renormalised over those two (0.4/0.7): " +
               narrowInf);

        // THE NEAREST-BONE FALLBACK stays at ONE full-weight influence - it is a synthesised weld and
        // widening it would only fabricate more - while still declaring the target's width, so the
        // replacement does not narrow the mesh either.
        string welded = Weld(classData, wide, WideMesh);
        Assert(welded.Contains(SkinFields.OurLayout(4)),
               "a welded replacement still declares the target's four: " + welded);
        string weldedInf = WeldInfluences(classData, wide, WideMesh);
        Assert(weldedInf.StartsWith("v0=1/0/0/0->bone", StringComparison.Ordinal),
               "and puts ONE full-weight influence in slot 0, the rest at zero: " + weldedInf);

        // THE PER-BONE BOUNDS follow every influence, not just the dominant one. m_BonesAABB is
        // rewritten whole, and the engine culls what a bone's box does not cover - so a bone the
        // widening now uses only in slot 2 or 3 would get NO box and its geometry would pop out
        // mid-animation. Asserted against the weights of the very mesh that was written.
        string boxes = Boxes(classData, wide, WideMesh);
        Assert(boxes.StartsWith("weighted=4 boxed=4 mismatched=none ", StringComparison.Ordinal) &&
               !boxes.EndsWith("(none)", StringComparison.Ordinal),
               "every bone the dim4 replacement weights gets a real box and no bone without weight " +
               "does, including one used ONLY in the last slot: " + boxes);
        string weldBoxes = WeldBoxes(classData, wide, WideMesh);
        Assert(weldBoxes.StartsWith("weighted=1 boxed=1 mismatched=none", StringComparison.Ordinal),
               "and the weld's ZERO-weight slots feed no bounds at all - one bone, one box: " + weldBoxes);

        // THE ADD PATH has no target to respect, so it carries what the FILE has: four. The width is
        // BakedSkin.Influences, derived from the file's own weights and nothing else, and the model
        // this bakes declares it.
        string added = Added(classData, narrow);
        Assert(added.Contains(SkinFields.OurLayout(4)),
               "a model ADDED from a .glb with four influences per vertex is written dim4: " + added);
        Assert(added.Contains(" vertex0=0.4/0.3/0.2/0.1->bone"),
               "and carries all four of the file's own weights: " + added);

        return "SKIN width PASS - shipped dim" + shippedWide + " '" + WideMesh + "' and dim" +
               shippedNarrow + " '" + NarrowMesh + "'\n  wide   " + wideInf +
               "\n  narrow " + narrowInf + "\n  weld   " + weldedInf + "\n  added  " + added +
               "\n  boxes  " + boxes + " | weld " + weldBoxes;
    }

    /// <summary>
    /// A one-triangle .glb whose every vertex is weighted to FOUR DISTINCT bones at 0.4/0.3/0.2/0.1 -
    /// deliberately unequal, so WHICH of the four survived a narrowing is visible in the number and
    /// not only in the bone index. It names the target's own bones, which is what makes RebindByName
    /// accept it, so the names are filled in per target - the two shipped skeletons differ.
    /// </summary>
    private static SkinnedModel Model(string[] bones)
    {
        int n = bones.Length;
        SkinnedModel m = new SkinnedModel { Name = "width" };
        m.Nodes.Add(new SkinNode { Name = "width_root", Parent = -1, Local = Identity() });
        m.JointNodes = new int[n];
        m.InverseBindMatrices = new float[n][];
        // REVERSED, like MeshRoundTrip's by-name fixture: a slot is not its live bone index, so a
        // binding that took the file's slots as target indices writes different bytes.
        for (int slot = 0; slot < n; slot++)
        {
            m.Nodes.Add(new SkinNode { Name = bones[n - 1 - slot], Parent = 0, Local = Identity() });
            m.InverseBindMatrices[slot] = Identity();
            m.JointNodes[slot] = slot + 1;
        }
        m.Positions = new[] { new ObjVector3(-0.1f, 0f, 0f), new ObjVector3(0f, 0.2f, 0f),
                              new ObjVector3(0.1f, 0f, 0f) };
        m.Normals = new[] { new ObjVector3(0f, 0f, -1f), new ObjVector3(0f, 0f, -1f), new ObjVector3(0f, 0f, -1f) };
        m.Uv0 = new[] { new ObjVector2(0f, 0f), new ObjVector2(0.5f, 1f), new ObjVector2(1f, 0f) };
        m.Submeshes.Add(new[] { 0, 1, 2 });
        m.Materials.Add("width");

        m.Joints = new ushort[3 * 4];
        m.Weights = new float[3 * 4];
        for (int v = 0; v < 3; v++)
            for (int k = 0; k < 4; k++)
            {
                // Four DIFFERENT bones per vertex, wrapped so a skeleton of any size can answer.
                m.Joints[v * 4 + k] = (ushort)(k % n);
                m.Weights[v * 4 + k] = (4 - k) / 10f;      // 0.4 / 0.3 / 0.2 / 0.1, summing to 1
            }
        return m;
    }

    private static float[] Identity()
    {
        float[] f = new float[16];
        f[0] = f[5] = f[10] = f[15] = 1f;
        return f;
    }

    // ---------------------------------------------------------------- the real write path

    /// <summary>The influences a shipped Mesh DECLARES, untouched.</summary>
    private static int Declared(string classData, string bundlePath, string meshName)
    {
        return int.Parse(Open(classData, bundlePath, (m, af) =>
            SkinFields.InfluencesOf(m.GetBaseField(af, Find(m, af, meshName))).ToString()));
    }

    /// <summary>
    /// The ADD path: a .glb built into the file as a new model (the same <c>SkinFields.BuildModel</c>
    /// call BundleBaker.AddModel makes), reported by the same one-line oracle the M1 gate reads.
    /// </summary>
    private static string Added(string classData, string bundlePath)
    {
        return Open(classData, bundlePath, (m, af) =>
        {
            long next = 0;
            foreach (AssetFileInfo a in af.file.Metadata.AssetInfos) if (a.PathId > next) next = a.PathId;
            next += 1000;

            long materialPathId = 0;
            foreach (AssetFileInfo i in af.file.Metadata.GetAssetsOfType(AssetClassID.Material))
            {
                materialPathId = i.PathId;
                break;
            }
            Assert(materialPathId != 0, "no Material in " + af.name);

            // The bones are the shipped skeleton's names only so the fixture has four of them; nothing
            // in the ADD path looks at a target, which is the point.
            string[] bones = { "add_a", "add_b", "add_c", "add_d" };
            SkinnedModel file = GlbReader.Read(GlbCodec.Write(Model(bones)));
            BakedSkin skin = ModelBuild.From(file, "addwidth");
            Assert(skin.Influences == 4,
                   "the import keeps all four of the file's influences per vertex: " + skin.Influences);
            SkinFields.BuildModel(af.file, m.ClassDatabase, () => next++, "addwidth", skin,
                                  new[] { materialPathId });
            return SkinFields.Summary(m, af, "addwidth");
        });
    }

    /// <summary>
    /// The REAL replacement path (the same three calls <c>BundleBaker.ReplaceMesh</c> makes, in the
    /// same order, including reading the width BEFORE Fill clears it), then the skin summary of what
    /// it wrote.
    /// </summary>
    private static string Replace(string classData, string bundlePath, string meshName)
    {
        return Open(classData, bundlePath, (m, af) => SkinFields.SkinSummary(Bind(m, af, meshName)));
    }

    private static string Influences(string classData, string bundlePath, string meshName)
    {
        return Open(classData, bundlePath, (m, af) => SkinFields.SkinInfluences(Bind(m, af, meshName)));
    }

    private static AssetTypeValueField Bind(AssetsManager m, AssetsFileInstance af, string meshName)
    {
        AssetFileInfo info = Find(m, af, meshName);
        AssetTypeValueField mesh = m.GetBaseField(af, info);
        string[] bones = SkinFields.BoneNames(m, af, info.PathId);
        // Two is enough for the fixture's two heaviest influences to land on different bones; a
        // skeleton with fewer than four just wraps, which costs the arm nothing.
        Assert(bones != null && bones.Length >= 2,
               "the shipped '" + meshName + "' names at least two bones: " +
               (bones == null ? "(no SkinnedMeshRenderer uses it)" : bones.Length + " bone(s)"));

        // Through the FILE, like the bake: the joint NAMES only exist after GlbReader has read it.
        SkinnedModel file = GlbReader.Read(GlbCodec.Write(Model(bones)));
        BakedSkin skin = ModelBuild.From(file, "width");

        int inf = SkinFields.InfluencesOf(mesh);
        MeshFields.Fill(mesh, skin.Mesh);
        Assert(SkinFields.RebindByName(mesh, skin.Mesh, file, bones, inf),
               "'" + meshName + "' is rigged, so a by-name rebind has bind poses to work with");
        return mesh;
    }

    private static string Boxes(string classData, string bundlePath, string meshName)
    {
        return Open(classData, bundlePath, (m, af) => BoneBoxes(Bind(m, af, meshName)));
    }

    private static string WeldBoxes(string classData, string bundlePath, string meshName)
    {
        return Open(classData, bundlePath, (m, af) => BoneBoxes(Welded(m, af, meshName)));
    }

    /// <summary>
    /// The per-bone bounds against the per-bone WEIGHTS, both read back out of the mesh that was just
    /// written: a bone the skin gives weight to must have a box that describes something, and a bone
    /// with no weight must have none. `lastSlotOnly` names a bone that appears ONLY in the final
    /// influence slot - the one a dominant-only accumulation silently loses.
    /// </summary>
    private static string BoneBoxes(AssetTypeValueField mesh)
    {
        int inf = SkinFields.InfluencesOf(mesh);
        AssetTypeValueField vd = mesh["m_VertexData"];
        int verts = (int)vd["m_VertexCount"].AsUInt;
        byte[] data = vd["m_DataSize"].AsByteArray;
        int at = SkinFields.SkinOffset(verts), stride = SkinFields.SkinStride(inf);

        AssetTypeValueField aabbs = mesh["m_BonesAABB"]["Array"];
        int bones = aabbs.Children.Count;
        bool[] weighted = new bool[bones];
        int[] first = new int[bones];
        for (int b = 0; b < bones; b++) first[b] = int.MaxValue;
        for (int i = 0; i < verts; i++)
            for (int k = 0; k < inf; k++)
            {
                if (BitConverter.ToSingle(data, at + i * stride + k * 4) <= 0f) continue;
                int b = (int)BitConverter.ToUInt32(data, at + i * stride + inf * 4 + k * 4);
                weighted[b] = true;
                if (k < first[b]) first[b] = k;
            }

        string bad = "";
        int nWeighted = 0, nBoxed = 0, lastOnly = -1;
        for (int b = 0; b < bones; b++)
        {
            AssetTypeValueField e = aabbs.Children[b];
            bool box = e["m_Max"]["x"].AsFloat > e["m_Min"]["x"].AsFloat ||
                       e["m_Max"]["y"].AsFloat > e["m_Min"]["y"].AsFloat ||
                       e["m_Max"]["z"].AsFloat > e["m_Min"]["z"].AsFloat;
            if (weighted[b]) nWeighted++;
            if (box) nBoxed++;
            if (weighted[b] != box)
                bad += (bad.Length == 0 ? "" : ",") + "bone" + b +
                       (weighted[b] ? "(weighted, NO box)" : "(box, NO weight)");
            if (weighted[b] && first[b] == inf - 1) lastOnly = b;
        }
        return "weighted=" + nWeighted + " boxed=" + nBoxed +
               " mismatched=" + (bad.Length == 0 ? "none" : bad) +
               " lastSlotOnly=" + (lastOnly < 0 ? "(none)" : "bone" + lastOnly);
    }

    /// <summary>The nearest-bone fallback - the branch an .obj (no armature at all) takes.</summary>
    private static string Weld(string classData, string bundlePath, string meshName)
    {
        return Open(classData, bundlePath, (m, af) => SkinFields.SkinSummary(Welded(m, af, meshName)));
    }

    private static string WeldInfluences(string classData, string bundlePath, string meshName)
    {
        return Open(classData, bundlePath, (m, af) => SkinFields.SkinInfluences(Welded(m, af, meshName)));
    }

    private static AssetTypeValueField Welded(AssetsManager m, AssetsFileInstance af, string meshName)
    {
        AssetTypeValueField mesh = m.GetBaseField(af, Find(m, af, meshName));
        BakedMesh baked = MeshBuild.From(ObjCodec.Parse(
            "v -0.5 -0.5 0\nv 0.5 -0.5 0\nv 0.5 0.5 0\nv -0.5 0.5 0\n" +
            "vt 0 0\nvt 1 0\nvt 1 1\nvt 0 1\nvn 0 0 -1\n" +
            "f 1/1/1 2/2/1 3/3/1\nf 1/1/1 3/3/1 4/4/1\n"));
        int inf = SkinFields.InfluencesOf(mesh);
        MeshFields.Fill(mesh, baked);
        Assert(SkinFields.Rebind(mesh, baked, inf), "'" + meshName + "' is rigged, so Rebind has bind poses");
        return mesh;
    }

    // ---------------------------------------------------------------- plumbing

    private static string Open(string classData, string bundlePath,
                               Func<AssetsManager, AssetsFileInstance, string> what)
    {
        AssetsManager m = new AssetsManager();
        m.LoadClassPackage(classData);
        BundleFileInstance bun = m.LoadBundleFile(bundlePath, true);
        AssetsFileInstance af = m.LoadAssetsFileFromBundle(bun, 0, false);
        m.LoadClassDatabaseFromPackage(af.file.Metadata.UnityVersion);
        try { return what(m, af); }
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
        if (!ok) throw new Exception("SKIN width FAILED: " + what);
    }
}
