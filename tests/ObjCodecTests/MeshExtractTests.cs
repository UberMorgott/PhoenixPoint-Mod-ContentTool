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

    private static int checks;

    internal static string Run()
    {
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
        return "MESH extract PASS on " + Bundle + ", " + checks + " check(s)\n  " + first + "\n  " + second;
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

        SkinnedModel model = MeshRead.Read(mesh, resS);
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
            // The bone name IS the CRC-32 the mesh records, because a CRC does not invert.
            Check(back.JointNames[0].StartsWith("bone_", StringComparison.Ordinal),
                  name + ": a joint is named after the hash the mesh carries: " + back.JointNames[0]);
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
               " joints=" + back.JointNames.Count;
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
