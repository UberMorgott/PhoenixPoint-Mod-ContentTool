using System;
using System.Collections.Generic;
using System.IO;
using AssetsTools.NET;
using AssetsTools.NET.Extra;
using Morgott.ContentTool.Bake;

/// <summary>
/// The DISCOVERY half of extraction, offline: an author has to be able to find out what a shipped
/// bundle contains before anything can be pulled out of it. AssetIndex carries no UnityEngine type,
/// so the listing and the ambiguity refusal are proven here instead of costing a game launch - the
/// same arrangement as MeshRoundTrip.
///
/// The game install is machine-specific, so a missing bundle is VOID, never PASS.
/// </summary>
internal static class AssetIndexTests
{
    private const string MeshBundle = "mutoid_assets_all.bundle";
    private const string Mesh = "ALN_Siren_Arm_Slasher_Right";

    /// <summary>aln_fireworm ships TWO Materials both called 'ALN_Fireworm' - the ambiguity arm.</summary>
    private const string AmbiguousBundle = "aln_fireworm_assets_all.bundle";
    private const string AmbiguousMaterial = "ALN_Fireworm";

    private static int checks;

    internal static string Run()
    {
        string root = Environment.GetEnvironmentVariable("PPRoot") ?? @"D:\Steam\steamapps\common\Phoenix Point";
        string classData = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"..\..\..\..\..\lib\classdata.tpk");
        string dir = Path.Combine(root, @"PhoenixPointWin64_Data\StreamingAssets\aa\StandaloneWindows64");
        string meshes = Path.Combine(dir, MeshBundle), ambiguous = Path.Combine(dir, AmbiguousBundle);
        if (!File.Exists(classData)) return "LISTING VOID - no " + Path.GetFullPath(classData);
        if (!File.Exists(meshes)) return "LISTING VOID - no " + meshes + " (set PPRoot to the game folder)";
        if (!File.Exists(ambiguous)) return "LISTING VOID - no " + ambiguous;

        string report;
        Open(classData, meshes, (m, afile) =>
        {
            int total = afile.file.Metadata.AssetInfos.Count;
            Check(AssetIndex.Rows(m, afile, null, null).Count == total,
                  "an unfiltered listing is every asset in the file (" + total + ")");

            List<AssetIndex.Row> named = AssetIndex.Rows(m, afile, "Mesh", Mesh);
            Check(named.Count == 1, "the name filter narrows to the one mesh: " + named.Count);
            Check(named[0].Type == "Mesh" && named[0].Name == Mesh && named[0].Bytes > 0 && named[0].PathId != 0,
                  "the row carries type, name, size and pathId: " + named[0]);

            // Case-insensitive, because the name an author reads off a listing is the one they type.
            Check(AssetIndex.Rows(m, afile, "mesh", Mesh.ToUpperInvariant()).Count == 1,
                  "the filters are case-insensitive");

            AssetFileInfo one = AssetIndex.FindUnique(m, afile, AssetClassID.Mesh, Mesh, MeshBundle);
            Check(one.PathId == named[0].PathId, "FindUnique returns the same object the listing showed");

            Check(Refusal(() => AssetIndex.FindUnique(m, afile, AssetClassID.Mesh, "no_such_mesh", MeshBundle),
                          "no Mesh named 'no_such_mesh'"),
                  "an absent name is refused, and the message says it was absent");

        });

        Open(classData, ambiguous, (m, afile) =>
        {
            int total = afile.file.Metadata.AssetInfos.Count;

            // The type the extractor actually pulls out, and the arm that proves a substring type
            // filter still returns exactly that class rather than its neighbours (MeshRenderer et al).
            List<AssetIndex.Row> textures = AssetIndex.Rows(m, afile, "Texture2D", null);
            Check(textures.Count > 0, "the bundle lists Texture2Ds (" + textures.Count + ")");
            foreach (AssetIndex.Row r in textures)
                Check(r.Type == "Texture2D", "a type-filtered row IS that type, not a substring neighbour: " + r.Type);
            Check(AssetIndex.Rows(m, afile, "Texture2D", "fireworm_low_emissive").Count == 1,
                  "the texture the in-game X2 arm extracts is findable by name");

            // A capped report must never read like a complete one.
            string capped = AssetIndex.Report(m, afile, "Texture2D", null, 1);
            Check(capped.Contains(textures.Count + " of " + total + " assets match") &&
                  capped.Contains("... " + (textures.Count - 1) + " more"),
                  "a capped report states the total and how much it withheld: " + capped.Replace('\n', '|'));

            List<AssetIndex.Row> dupes = AssetIndex.Rows(m, afile, "Material", AmbiguousMaterial);
            Check(dupes.Count >= 2, "the ambiguity arm is real: " + dupes.Count + " Materials named '" + AmbiguousMaterial + "'");
            Check(Refusal(() => AssetIndex.FindUnique(m, afile, AssetClassID.Material, AmbiguousMaterial, AmbiguousBundle),
                          "refusing to guess which one"),
                  "an ambiguous name is refused rather than guessed");
            Check(Refusal(() => AssetIndex.FindUnique(m, afile, AssetClassID.Material, AmbiguousMaterial, AmbiguousBundle),
                          "pathIds "),
                  "the refusal names the offenders, so it is actionable");
        });

        report = "LISTING PASS on " + MeshBundle + " and " + AmbiguousBundle + ", " + checks + " check(s)";
        return report;
    }

    private static void Open(string classData, string bundlePath, Action<AssetsManager, AssetsFileInstance> ask)
    {
        AssetsManager m = new AssetsManager();
        m.LoadClassPackage(classData);
        BundleFileInstance bun = m.LoadBundleFile(bundlePath, true);
        AssetsFileInstance afile = m.LoadAssetsFileFromBundle(bun, 0, false);
        m.LoadClassDatabaseFromPackage(afile.file.Metadata.UnityVersion);
        try { ask(m, afile); }
        finally { m.UnloadAll(); }
    }

    /// <summary>True when the call threw AND the message names the cause - not merely that it threw.</summary>
    private static bool Refusal(Action call, string cause)
    {
        try { call(); }
        catch (Exception ex) { return ex.Message.Contains(cause); }
        return false;
    }

    private static void Check(bool ok, string what)
    {
        checks++;
        if (!ok) throw new Exception("LISTING FAIL: " + what);
    }
}
