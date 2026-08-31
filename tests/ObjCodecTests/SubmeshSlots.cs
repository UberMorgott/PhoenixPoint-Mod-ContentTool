using System;
using AssetsTools.NET.Extra;
using Morgott.ContentTool.Bake;

/// <summary>
/// A replacement's PARTS land on the target's MATERIALS by order, and until this gate existed the
/// bake never said so. The case it is written from: an author's torso arrived as primitive 0 = ONE
/// triangle plus primitive 1 = 15647, so the whole model was painted with the target's SECOND
/// material and the only symptom was a mangled character.
///
/// The rule is deliberately absolute rather than a share of the mesh - a real material part can be a
/// fraction of a percent of a body, but nothing anybody meant to paint separately is drawn by eight
/// triangles or fewer (a cube is twelve). A shard is REPORTED, never refused: the file is legal.
/// </summary>
internal static class SubmeshSlots
{
    private static int checks;

    internal static string Run()
    {
        checks = 0;
        try
        {
            bool suspect;

            // The real shape: a shard in front of the geometry, onto a two-material target.
            string warn = MeshFields.SubmeshReport("CHR_PX_HVY_TS_M_V01.glb", new[] { 1, 15647 },
                new[] { "CHR_PX_HVY_TS_M_V01", "CHR_PX_HVY_SHD_M_V01" }, out suspect);
            Ok(suspect, "a 1-triangle part beside a 15647-triangle one is suspect");
            Ok(warn.Contains("part 1 of 2 has only 1 triangle while part 2 has 15647"),
               "the warning names both parts and their sizes: " + warn);
            Ok(warn.Contains("part 1 (1 triangle) -> material 'CHR_PX_HVY_TS_M_V01'") &&
               warn.Contains("part 2 (15647 triangles) -> material 'CHR_PX_HVY_SHD_M_V01'"),
               "it names the material each part lands on: " + warn);
            Ok(warn.Contains("CHR_PX_HVY_TS_M_V01.glb") && warn.Contains("Blender") &&
               warn.Contains("ONE material slot") && warn.Contains("Baked anyway"),
               "it names the file, the Blender fix, and that nothing was skipped: " + warn);

            // The control: one part onto one material says nothing at all.
            Ok(MeshFields.SubmeshReport("body.glb", new[] { 15647 }, new[] { "CHR_PX_HVY_TS_M_V01" },
                                        out suspect) == null && !suspect,
               "a single-part source onto a single slot is silent");

            // A LEGITIMATE multi-slot replacement: equal counts, sane sizes - reported, never warned.
            string map = MeshFields.SubmeshReport("torso.glb", new[] { 3030, 2536 },
                new[] { "CHR_PX_HVY_TS_M_V01", "CHR_PX_HVY_SHD_M_V01" }, out suspect);
            Ok(!suspect, "two sane parts onto two slots are NOT suspect");
            Ok(map.Contains("part 1 (3030 triangles) -> material 'CHR_PX_HVY_TS_M_V01'") &&
               map.Contains("part 2 (2536 triangles) -> material 'CHR_PX_HVY_SHD_M_V01'"),
               "the mapping is still printed for it: " + map);

            // The boundary, both sides: 8 triangles is a shard, 9 is a part.
            MeshFields.SubmeshReport("x.glb", new[] { 8, 900 }, new[] { "a", "b" }, out suspect);
            Ok(suspect, "8 triangles is a shard");
            MeshFields.SubmeshReport("x.glb", new[] { 9, 900 }, new[] { "a", "b" }, out suspect);
            Ok(!suspect, "9 triangles is a part");

            // A part past the end of the material array is drawn by NOTHING, and says so.
            string over = MeshFields.SubmeshReport("x.glb", new[] { 900, 900 }, new[] { "a" }, out suspect);
            Ok(over.Contains("part 2 (900 triangles) -> NO material (not drawn)"),
               "a part with no material slot is named as undrawn: " + over);

            // ...but a mesh NO renderer draws is a different answer from a part with no slot.
            string none = MeshFields.SubmeshReport("x.glb", new[] { 900, 900 }, null, out suspect);
            Ok(none.Contains("material unknown (no renderer in this bundle draws this mesh)") &&
               !none.Contains("not drawn"),
               "a mesh nothing draws says so instead of calling its parts undrawn: " + none);

            // A shard BEHIND the real geometry displaces nothing, and the warning must not claim it does.
            string last = MeshFields.SubmeshReport("x.glb", new[] { 900, 1 }, new[] { "a", "b" }, out suspect);
            Ok(suspect, "a trailing 1-triangle part is still flagged");
            Ok(last.Contains("being last it displaces nothing") &&
               !last.Contains("painted wrongly"),
               "it does NOT claim the real geometry moved: " + last);
            Ok(MeshFields.SubmeshReport("x.glb", new[] { 1, 900 }, new[] { "a", "b" }, out suspect)
                         .Contains("every part after it takes the material meant for the part before"),
               "while a LEADING shard still says what it displaces");

            return Variants();
        }
        catch (Exception ex) { return "SUBMESH-SLOTS FAIL " + ex.Message; }
    }

    /// <summary>
    /// The REAL mesh the feature was written for: CHR_PX_HVY_TS_M_V01 is drawn by three renderers
    /// (default, GOLD, XMAS) whose material arrays DIFFER. That used to read back as null - "no
    /// materials" - and the report then called every part undrawn. The game install is
    /// machine-specific, so a missing bundle is VOID, never PASS.
    /// </summary>
    private static string Variants()
    {
        const string Mesh = "CHR_PX_HVY_TS_M_V01";
        string root = Environment.GetEnvironmentVariable("PPRoot") ?? @"D:\Steam\steamapps\common\Phoenix Point";
        string bundle = System.IO.Path.Combine(root,
            @"PhoenixPointWin64_Data\StreamingAssets\aa\StandaloneWindows64\px_heavy_assets_all.bundle");
        string classData = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory,
            @"..\..\..\..\..\lib\classdata.tpk");
        if (!System.IO.File.Exists(bundle) || !System.IO.File.Exists(classData))
            return "SUBMESH-SLOTS: " + checks + " check(s) PASS, variant arm VOID - no " + bundle;

        string[] slots = null;
        AssetsManager man = new AssetsManager();
        man.LoadClassPackage(classData);
        BundleFileInstance bun = man.LoadBundleFile(bundle, true);
        AssetsFileInstance afile = man.LoadAssetsFileFromBundle(bun, 0, false);
        man.LoadClassDatabaseFromPackage(afile.file.Metadata.UnityVersion);
        try
        {
            slots = MeshFields.MaterialNames(man, afile,
                AssetIndex.FindUnique(man, afile, AssetClassID.Mesh, Mesh, bundle).PathId);
        }
        finally { man.UnloadAll(); }
        Ok(slots != null, Mesh + " is drawn by renderers whose materials disagree, and that is not null");
        Ok(slots.Length == 2, Mesh + " keeps its 2 material slots across the variants, got " +
                              (slots == null ? 0 : slots.Length));
        Ok(Array.Exists(slots, s => s.Contains("varies by renderer variant")),
           "the disagreement is named rather than hidden: [" + string.Join(" | ", slots) + "]");

        bool suspect;
        string warn = MeshFields.SubmeshReport("torso.glb", new[] { 1, 15647 }, slots, out suspect);
        Ok(suspect && !warn.Contains("not drawn") && !warn.Contains("material unknown"),
           "and the shard warning on it names materials instead of calling parts undrawn: " + warn);
        return "SUBMESH-SLOTS: ALL PASS, " + checks + " check(s) (variants: [" + string.Join(" | ", slots) + "])";
    }

    private static void Ok(bool cond, string what)
    {
        checks++;
        if (!cond) throw new Exception(what);
    }
}
