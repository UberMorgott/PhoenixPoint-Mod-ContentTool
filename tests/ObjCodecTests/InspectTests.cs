using System;
using System.IO;
using AssetsTools.NET;
using AssetsTools.NET.Extra;
using Morgott.ContentTool.Bake;

/// <summary>
/// The three answers `ct_list bones` / `ct_list props` / `ct_list clip` print, taken the same way
/// the verbs take them - FindUnique for the asset, then SkinFields.BoneNames / MaterialFields.Summary
/// / ClipFields.Summary - against REAL shipped bundles. None of those three carries a UnityEngine
/// type, so the payload behind the wiring is proven here instead of costing a game launch (the
/// dispatcher line in Dev\Extract.cs is UnityEngine-bound and cannot be compiled into this harness;
/// what this catches is a helper that stops answering, which is what would empty the new verbs).
///
/// The game install is machine-specific, so a missing bundle is VOID, never PASS.
/// </summary>
internal static class InspectTests
{
    private const string Bundle = "aln_fireworm_assets_all.bundle";
    private const string Material = "ALN_Fireworm_DMG";  // UNIQUE, unlike the two 'ALN_Fireworm'
    private const string Clip = "Fireworm_unfurl";

    /// <summary>The skinned mesh the bones arm reads - unique, where 'ALN_Fireworm' is not.</summary>
    private const string MeshBundle = "mutoid_assets_all.bundle";
    private const string Mesh = "Geo_Head02_V01";

    private static int checks;

    internal static string Run()
    {
        string root = Environment.GetEnvironmentVariable("PPRoot") ?? @"D:\Steam\steamapps\common\Phoenix Point";
        string classData = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"..\..\..\..\..\lib\classdata.tpk");
        string dir = Path.Combine(root, @"PhoenixPointWin64_Data\StreamingAssets\aa\StandaloneWindows64");
        string bundle = Path.Combine(dir, Bundle), meshes = Path.Combine(dir, MeshBundle);
        if (!File.Exists(classData)) return "INSPECT VOID - no " + Path.GetFullPath(classData);
        if (!File.Exists(bundle)) return "INSPECT VOID - no " + bundle + " (set PPRoot to the game folder)";
        if (!File.Exists(meshes)) return "INSPECT VOID - no " + meshes;

        Open(classData, meshes, (m, afile) =>
        {
            // ---- ct_list bones: a name per bind pose, index for index, none of them blank.
            AssetFileInfo meshInfo = AssetIndex.FindUnique(m, afile, AssetClassID.Mesh, Mesh, MeshBundle);
            int poses = m.GetBaseField(afile, meshInfo)["m_BindPose"]["Array"].Children.Count;
            string[] bones = SkinFields.BoneNames(m, afile, meshInfo.PathId);
            Check(bones != null, "the shipped skeleton of '" + Mesh + "' is readable at all");
            Check(poses > 0 && bones.Length == poses,
                  "one bone name per bind pose: " + bones.Length + " names, " + poses + " poses");
            foreach (string b in bones) Check(!string.IsNullOrEmpty(b), "no bone is nameless");
        });

        Open(classData, bundle, (m, afile) =>
        {
            // The mesh this bundle ships TWICE under one name: ct_list bones resolves through
            // FindUnique for exactly this, so it refuses instead of printing one of two skeletons.
            Check(Refusal(() => AssetIndex.FindUnique(m, afile, AssetClassID.Mesh, "ALN_Fireworm", Bundle),
                          "refusing to guess which one"),
                  "an ambiguous mesh name is refused before any skeleton is read");

            // ---- ct_list props: the property NAMES a "material" row has to spell.
            string props = MaterialFields.Summary(m.GetBaseField(afile,
                AssetIndex.FindUnique(m, afile, AssetClassID.Material, Material, Bundle)));
            Check(props.StartsWith("shader fileID=", StringComparison.Ordinal),
                  "the property block starts at the shader: " + props);
            Check(props.Contains("| _Glossiness="),
                  "it names the float property the demo manifest tunes: " + props);

            // ---- ct_list clip: ONE named clip's fields, which the assets listing cannot say.
            string clip = ClipFields.Summary(m, afile, Clip, null);
            Check(clip.StartsWith("clip '" + Clip + "' bindings=", StringComparison.Ordinal) &&
                  !clip.Contains(" bindings=0"),
                  "the shipped clip reports its own curves: " + clip);
            Check(ClipFields.Summary(m, afile, "no_such_clip", null).Contains("no AnimationClip named"),
                  "an absent clip name says so rather than answering about another clip");
            Check(Refusal(() => ClipFields.Summary(m, afile, "no_such_clip", null, Bundle),
                          "no AnimationClip named 'no_such_clip'"),
                  "the verb's opt-in refuses an absent name outright, as bones and props do");
        });

        // ---- ct_list clip refuses an AMBIGUOUS clip name. No shipped bundle holds two clips of one
        // name, so the fixture builds one. Falsified by dropping the last argument of the Summary
        // call below: the verb then answers about one of the two and the Refusal check fails.
        string dupes = Duplicate(classData, bundle, Clip);
        Open(classData, dupes, (m, afile) =>
        {
            Check(AssetIndex.Rows(m, afile, "AnimationClip", Clip).Count == 2,
                  "the fixture really carries two clips named '" + Clip + "'");
            Check(Refusal(() => ClipFields.Summary(m, afile, Clip, null, dupes),
                          "refusing to guess which one"),
                  "an ambiguous clip name is refused instead of answering about one of the two");
            Check(ClipFields.Summary(m, afile, Clip, null).StartsWith("clip '", StringComparison.Ordinal),
                  "the bake's own first-match read is untouched by that opt-in");
        });
        File.Delete(dupes);

        return "INSPECT PASS on " + MeshBundle + " and " + Bundle + " (bones/props/clip), " +
               checks + " check(s)";
    }

    /// <summary>
    /// A temp copy of the fireworm bundle carrying a SECOND AnimationClip under the name the shipped
    /// one already has - the ambiguity no shipped bundle supplies (fireworm ships exactly one clip,
    /// measured 2026-08-28, so there is nothing to rename and the twin is created). The twin is an
    /// empty class-database template with only its m_Name filled: FindUnique reads m_Name and nothing
    /// else, so that is the whole ambiguity. Written by the same two calls the bake uses -
    /// PrefabFields.Create then ClipRoundTrip.Pack.
    /// </summary>
    private static string Duplicate(string classData, string bundlePath, string name)
    {
        string copy = Path.Combine(Path.GetTempPath(), "ct-dupclip-" + Path.GetFileName(bundlePath));
        AssetsManager m = new AssetsManager();
        m.LoadClassPackage(classData);
        BundleFileInstance bun = m.LoadBundleFile(bundlePath, true);
        AssetsFileInstance af = m.LoadAssetsFileFromBundle(bun, 0, false);
        m.LoadClassDatabaseFromPackage(af.file.Metadata.UnityVersion);
        try
        {
            long pathId = 0;
            foreach (AssetFileInfo a in af.file.Metadata.AssetInfos) if (a.PathId > pathId) pathId = a.PathId;
            PrefabFields.Create(af.file, m.ClassDatabase, pathId + 1000, AssetClassID.AnimationClip,
                                bf => bf["m_Name"].AsString = name);
            ClipRoundTrip.Pack(bun, af, copy);
            return copy;
        }
        finally { m.UnloadAll(); }
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
        if (!ok) throw new Exception("INSPECT FAIL: " + what);
    }
}
