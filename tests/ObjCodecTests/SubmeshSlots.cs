using System;
using AssetsTools.NET.Extra;
using Morgott.ContentTool.Bake;
using Morgott.ContentTool.Import;

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
            Ok(last.Contains("no real geometry after it nothing is displaced") &&
               !last.Contains("painted wrongly"),
               "it does NOT claim the real geometry moved: " + last);

            // ...and SEVERAL trailing shards displace nothing either, though a part does follow.
            string two = MeshFields.SubmeshReport("x.glb", new[] { 900, 1, 1 }, new[] { "a", "b", "c" }, out suspect);
            Ok(suspect && two.Contains("no real geometry after it nothing is displaced") &&
               !two.Contains("painted wrongly"),
               "two trailing shards displace nothing either: " + two);
            Ok(MeshFields.SubmeshReport("x.glb", new[] { 1, 900 }, new[] { "a", "b" }, out suspect)
                         .Contains("every part after it takes the material meant for the part before"),
               "while a LEADING shard still says what it displaces");

            // The fold behind the variant marker: a name that merely CONTAINS another is a different
            // material. Shipped case - ALN_Fireworm is drawn as 'ALN_Fireworm_DMG' by one renderer
            // and 'ALN_Fireworm' by another, and swallowing the second hid the variation entirely.
            Ok(MeshFields.Fold(new[] { new[] { "ALN_Fireworm_DMG" }, new[] { "ALN_Fireworm" } })[0] ==
               "ALN_Fireworm_DMG or ALN_Fireworm (varies by renderer variant)",
               "a name that is a prefix of another is a SEPARATE alternative, not a duplicate");
            Ok(MeshFields.Fold(new[] { new[] { "ALN_Fireworm" }, new[] { "ALN_Fireworm" } })[0] == "ALN_Fireworm",
               "an identical name is folded away, marker and all");
            // The alternatives are NAMES, never re-parsed out of the joined text: a material actually
            // called 'Red or Blue' would double if the fold ever split its own display string.
            Ok(MeshFields.Fold(new[] { new[] { "Red or Blue" }, new[] { "Red or Blue" } })[0] == "Red or Blue",
               "a material whose own name contains ' or ' is reported once, not doubled");
            Ok(MeshFields.Fold(new[] { new[] { "a" }, new[] { "b" }, new[] { "a" } })[0] ==
               "a or b (varies by renderer variant)",
               "a third renderer repeating the first adds nothing and the marker is said once");
            Ok(MeshFields.Fold(new string[0][]) == null && MeshFields.Fold(null) == null,
               "a mesh nothing draws stays null");

            Embedded();
            return Variants();
        }
        catch (Exception ex) { return "SUBMESH-SLOTS FAIL " + ex.Message; }
    }

    /// <summary>
    /// The READER's half of the slot contract: Materials, MaterialImages and MaterialEmissive are
    /// index-parallel to the submeshes, and ProjectBake reads image s for submesh s. A slot-less
    /// primitive in front used to add a name but no image, so every later part took the texture of
    /// the part after it. Plus the two ways a base-colour texture would land on the wrong UVs, which
    /// are refused by name rather than baked crooked.
    /// </summary>
    private static void Embedded()
    {
        SkinnedModel model = GlbReader.Read(Painted(""));
        Ok(model.Submeshes.Count == 2 && model.Materials.Count == 2 &&
           model.MaterialImages.Count == 2 && model.MaterialEmissive.Count == 2,
           "a slot-less primitive keeps all three material lists parallel to the submeshes: " +
           model.Materials.Count + "/" + model.MaterialImages.Count + "/" + model.MaterialEmissive.Count);
        Ok(model.MaterialImages[0] == null && model.MaterialImages[1] != null && model.MaterialImages[1][0] == 0x89,
           "the image lands on the part whose material names it, not on the part before");

        Ok(GlbReader.Read(Painted(",\"extensions\":{\"KHR_texture_transform\":{\"offset\":[0,0],\"scale\":[1,1]}}"))
               .MaterialImages[1] != null,
           "an identity KHR_texture_transform changes nothing and is read");
        string moved = Refusal(Painted(",\"extensions\":{\"KHR_texture_transform\":{\"scale\":[4,4]}}"));
        Ok(moved.Contains("material 'Hull'") && moved.Contains("KHR_texture_transform") && moved.Contains("Mapping node"),
           "a scaled base-colour texture is refused by material name, with the Blender fix: " + moved);
        string second = Refusal(Painted(",\"texCoord\":1"));
        Ok(second.Contains("material 'Hull'") && second.Contains("TEXCOORD_1") && second.Contains("FIRST UV map"),
           "a base-colour texture on the second UV map is refused by name: " + second);
    }

    /// <summary>One static triangle, two primitives over it: #0 with no material, #1 painted by
    /// 'Hull', whose base-colour texture is a 4-byte stand-in image. <paramref name="info"/> is
    /// spliced into that textureInfo.</summary>
    private static byte[] Painted(string info)
    {
        var b = new ClipImport.Bin();
        int position = b.Vec(3, "VEC3", 0f, 0f, 0f, 1f, 0f, 0f, 0f, 1f, 0f);
        int uv = b.Vec(3, "VEC2", 0f, 0f, 1f, 0f, 0f, 1f);
        int indices = b.Indices(0, 1, 2);
        int image = b.View(new byte[] { 0x89, 0x50, 0x4E, 0x47 });
        string attributes = "\"attributes\":{\"POSITION\":" + position + ",\"TEXCOORD_0\":" + uv + "},\"indices\":" + indices;
        string json =
            "{\"asset\":{\"version\":\"2.0\"},\"scenes\":[{\"nodes\":[0]}],\"scene\":0," +
            "\"nodes\":[{\"name\":\"hull\",\"mesh\":0}]," +
            "\"meshes\":[{\"name\":\"hull\",\"primitives\":[{" + attributes + "},{" + attributes + ",\"material\":0}]}]," +
            "\"materials\":[{\"name\":\"Hull\",\"pbrMetallicRoughness\":{\"baseColorTexture\":{\"index\":0" + info + "}}}]," +
            "\"textures\":[{\"source\":0}],\"images\":[{\"bufferView\":" + image + ",\"mimeType\":\"image/png\"}]," +
            b.Json() + "}";
        return ClipImport.Container(json, b.Bytes());
    }

    private static string Refusal(byte[] file)
    {
        try { GlbReader.Read(file); return "(no refusal at all)"; }
        catch (FormatException exception) { return exception.Message; }
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
