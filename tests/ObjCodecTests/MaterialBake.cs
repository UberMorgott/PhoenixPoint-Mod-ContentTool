using System;
using System.Collections.Generic;
using System.IO;
using AssetsTools.NET;
using AssetsTools.NET.Extra;
using Morgott.ContentTool.Bake;
using Morgott.ContentTool.Import;

/// <summary>
/// PAINT, offline: does an imported .glb come out of the bake with a material a renderer can DRAW?
///
/// The regression it exists for: the CustomCreature spider rendered flat white in game. Every arm
/// that existed passed - the mesh was there, the rig was there, the material was there - because
/// none of them asked what the material CONTAINED. A Material carrying no texture and no colour is
/// serialized exactly like one carrying paint, and Unity draws the shader's own default, which for
/// Standard is opaque white. On screen that is indistinguishable from "nothing loaded".
///
/// So this walks the WRITTEN FILE the way the engine does and asserts, in order:
///   1. the renderer's m_Materials is not empty,
///   2. every entry is a LOCAL PPtr that resolves to a real Material in the same file,
///   3. that Material's m_Shader is an external PPtr into the builtin-shader CAB, at the measured
///      pathID (a shader reference of 0/0 is what "no shader" looks like on disk),
///   4. the colour key the bake writes is one the REFERENCED SHADER DECLARES - read out of the
///      shipped builtin-shader bundle, not chosen here,
///   5. the Material carries PAINT - a _MainTex, or a colour that is not white.
/// Arm 5 is the one the spider failed. Arm 4 exists because arm 5 alone cannot fail for the OTHER
/// way a material draws white: a key the shader does not declare is not an error - Unity keeps it
/// in the property block, never binds it, and the shader paints its own default. So a check that
/// only asked "is there a key called _Color" would stay green while nothing reached the screen.
///
/// The fixture is the demo's REAL downloaded .glb, not a synthetic one: it is the file that broke,
/// it carries two materials over two primitives, and it carries no image and no TEXCOORD_0 at all -
/// so it is also the proof that the texture-less path still produces something visible.
///
/// A missing game install or a missing fixture is VOID, never PASS.
/// </summary>
internal static class MaterialBake
{
    private const string Bundle = "mutoid_assets_all.bundle";
    private const string Root = "paintmodel";
    private const string MaterialName = "paintmodel_mat";

    internal static string Run()
    {
        string here = AppDomain.CurrentDomain.BaseDirectory;
        // The fixture is WHATEVER .glb that demo ships, not a hard-coded name. It was
        // `spider.glb`; the creature line renamed the model to `cyborg_spider.glb` and this arm
        // quietly went VOID - it printed "VOID - no ...spider.glb" and the suite still read green,
        // which is the vacuous-pass failure mode a gate exists to prevent. A rename must not be able
        // to switch a gate off in silence again.
        string models = Path.GetFullPath(Path.Combine(here,
            @"..\..\..\..\..\demos\CustomCreature\Content\Models"));
        string glb = Directory.Exists(models)
            ? (Directory.GetFiles(models, "*.glb").Length > 0 ? Directory.GetFiles(models, "*.glb")[0] : Path.Combine(models, "none.glb"))
            : Path.Combine(models, "none.glb");
        string classData = Path.GetFullPath(Path.Combine(here, @"..\..\..\..\..\lib\classdata.tpk"));
        string root = Environment.GetEnvironmentVariable("PPRoot") ?? @"D:\Steam\steamapps\common\Phoenix Point";
        string aa = Path.Combine(root, @"PhoenixPointWin64_Data\StreamingAssets\aa\StandaloneWindows64");
        string shipped = Path.Combine(aa, Bundle);
        string shaders = Path.Combine(aa, MaterialFields.BuiltinShaderBundle);
        if (!File.Exists(glb)) return "MATERIAL bake VOID - no " + glb;
        if (!File.Exists(classData)) return "MATERIAL bake VOID - no " + classData;
        if (!File.Exists(shipped)) return "MATERIAL bake VOID - no " + shipped + " (set PPRoot to the game folder)";
        if (!File.Exists(shaders)) return "MATERIAL bake VOID - no " + shaders + " (set PPRoot to the game folder)";

        // The oracle for arm 4, off the game's OWN shader asset. Everything below compares the key the
        // bake writes against this set instead of against a name this file chose.
        HashSet<string> declared = Declared(classData, shaders);
        Assert(declared.Contains(MaterialFields.ColorProperty),
               "the shader the bake points at declares the colour key it writes, '" + MaterialFields.ColorProperty +
               "'. It declares " + declared.Count + " properties: " + Names(declared) +
               ". A key outside that list is kept in the property block, never bound, and the shader " +
               "paints its own default - opaque white");

        SkinnedModel model = GlbReader.Read(File.ReadAllBytes(glb));
        BakedSkin skin = ModelBuild.From(model, Root);

        // The IMPORT half. The file states two materials and no image at all, so the base colour is
        // the only paint that exists - and it must be the DOMINANT primitive's, not white.
        // THE FIXTURE CHANGED UNDER THIS ARM. It was written for a spider carrying no image at all,
        // where the base colour was the only paint that could exist; the creature line's model is now
        // a TEXTURED one. Both are legitimate, so the arm branches on what the file actually holds
        // rather than asserting yesterday's file - and both branches end on the same invariant, which
        // is the one that matters: SOMETHING paints this material instead of leaving it white.
        bool untextured = model.IgnoredImages == 0 && model.Uv0 == null;
        if (untextured)
        {
            Assert(skin.BaseColor != null, "the .glb's base colour survived the import into the baked skin");
            Assert(!White(skin.BaseColor),
                   "and it is the file's own colour, not the default white: " + Rgba(skin.BaseColor));
        }
        else
        {
            Assert(model.Uv0 != null,
                   "a fixture carrying images must carry the UVs that address them: images=" +
                   model.IgnoredImages);
            Assert(skin.MaterialImages.Length > 0 && skin.MaterialImages[0] != null,
                   "the .glb's own embedded texture reached the baked skin, so the file paints this " +
                   "model instead of leaving it white: " + skin.MaterialImages.Length + " image slot(s)");
        }

        // sRGB encoding must LIGHTEN a dark linear value, or the model bakes to near-black - the same
        // "looks broken" as white, one shade down.
        //
        // Tested against a KNOWN value rather than against whatever the fixture happens to carry.
        // It used to read skin.BaseColor, which worked only while the fixture's own colour was dark:
        // the textured model that replaced it has a white base colour, sRGB(1) is 1, and the arm
        // failed on a conversion that was perfectly correct. A conversion arm must not depend on the
        // model it happens to be handed.
        float[] darkLinear = { 0.0418f, 0.0418f, 0.0418f, 0.5f };
        float[] encoded = MaterialFields.Srgb(darkLinear);
        Assert(encoded[0] > darkLinear[0] && Math.Abs(encoded[0] - 0.23f) < 0.02f,
               "0.0418 linear encodes to about 0.23 sRGB, so a dark model does not bake to near-black: " +
               Rgba(darkLinear) + " -> " + Rgba(encoded));
        Assert(encoded[3] == darkLinear[3],
               "alpha is NOT an sRGB channel and must be left alone: " + Rgba(encoded));

        float[] paint = MaterialFields.Srgb(skin.BaseColor);

        string copy = Path.Combine(Path.GetTempPath(), "ct-materialbake-" + Bundle);
        try
        {
            Build(classData, shipped, copy, skin, paint);
            string what = Inspect(classData, copy, declared);
            return "MATERIAL bake PASS on " + Path.GetFileName(glb) + "\n  " + what;
        }
        finally { if (File.Exists(copy)) File.Delete(copy); }
    }

    /// <summary>The bake, cut down to the two objects paint lives on: the Material and the model.</summary>
    private static void Build(string classData, string shipped, string outPath, BakedSkin skin, float[] paint)
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

            // The externals index read off the file, never assumed - the same rule
            // BundleBaker.ExternalIdOf follows.
            int shaderFileId = 0;
            List<AssetsFileExternal> ext = af.file.Metadata.Externals;
            for (int i = 0; i < ext.Count; i++)
                if (ext[i].PathName != null &&
                    ext[i].PathName.IndexOf(MaterialFields.BuiltinShaderCab, StringComparison.OrdinalIgnoreCase) >= 0)
                    shaderFileId = i + 1;
            Assert(shaderFileId > 0, "the shipped bundle references the builtin-shader CAB, so a baked " +
                   "material can point at Standard through it");

            long materialId = next++;
            PrefabFields.Create(af.file, m.ClassDatabase, materialId, AssetClassID.Material, mat =>
            {
                mat["m_Name"].AsString = MaterialName;
                // Baked WITH emission so arm 6 below has something real to read back. The colour
                // alone is inert without the keyword, which is exactly what that arm proves.
                MaterialFields.Fill(mat, shaderFileId, MaterialFields.StandardShaderPathId, null, null,
                    new Dictionary<string, float[]>
                    {
                        { MaterialFields.ColorProperty, paint },
                        { MaterialFields.EmissionColorProperty, new[] { 0f, 0.75f, 1f, 1f } }
                    },
                    MaterialFields.EmissionKeyword);
            });
            SkinFields.BuildModel(af.file, m.ClassDatabase, () => next++, Root, skin, new[] { materialId });

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

    /// <summary>
    /// The shader's OWN declared property table, out of the shipped builtin-shader bundle at the
    /// measured pathID. A missing asset there is a FAILURE, never a pass: it would mean the two
    /// constants the whole bake points at no longer name a shader.
    /// </summary>
    private static HashSet<string> Declared(string classData, string shaderBundle)
    {
        AssetsManager m = new AssetsManager();
        m.LoadClassPackage(classData);
        BundleFileInstance bun = m.LoadBundleFile(shaderBundle, true);
        AssetsFileInstance af = m.LoadAssetsFileFromBundle(bun, 0, false);
        m.LoadClassDatabaseFromPackage(af.file.Metadata.UnityVersion);
        try
        {
            foreach (AssetFileInfo a in af.file.Metadata.AssetInfos)
            {
                if (a.PathId != MaterialFields.StandardShaderPathId) continue;
                Assert(a.TypeId == (int)AssetClassID.Shader,
                       "pathID " + a.PathId + " in " + MaterialFields.BuiltinShaderBundle +
                       " is a Shader, not class " + a.TypeId);
                HashSet<string> names = MaterialFields.DeclaredProperties(m.GetBaseField(af, a));
                Assert(names.Count > 0, "that shader declares properties at all");
                return names;
            }
            throw new Exception("MATERIAL bake FAILED: " + MaterialFields.BuiltinShaderBundle +
                                " holds nothing at pathID " + MaterialFields.StandardShaderPathId +
                                ", which every baked material points at");
        }
        finally { m.UnloadAll(); }
    }

    private static string Names(HashSet<string> names)
    {
        List<string> sorted = new List<string>(names);
        sorted.Sort(StringComparer.Ordinal);
        return string.Join(", ", sorted.ToArray());
    }

    /// <summary>The five arms, off the written file, the way the engine resolves them.</summary>
    private static string Inspect(string classData, string bundlePath, HashSet<string> declared)
    {
        AssetsManager m = new AssetsManager();
        m.LoadClassPackage(classData);
        BundleFileInstance bun = m.LoadBundleFile(bundlePath, true);
        AssetsFileInstance af = m.LoadAssetsFileFromBundle(bun, 0, false);
        m.LoadClassDatabaseFromPackage(af.file.Metadata.UnityVersion);
        try
        {
            AssetTypeValueField skinGo = PrefabFields.FindGameObject(m, af, SkinFields.SkinName(Root));
            Assert(skinGo != null, "the copy holds the model's skin GameObject");
            AssetTypeValueField r = PrefabFields.Component(m, af, skinGo, AssetClassID.SkinnedMeshRenderer);
            Assert(r != null, "the skin GameObject carries a SkinnedMeshRenderer");

            // 1. Not empty. An empty m_Materials is a renderer that draws NOTHING at all.
            AssetTypeValueField mats = r["m_Materials"]["Array"];
            Assert(mats.Children.Count > 0, "the renderer's m_Materials is not empty");

            string report = null;
            foreach (AssetTypeValueField slot in mats.Children)
            {
                // 2. A LOCAL reference that resolves. m_FileID != 0 would send the engine to another
                //    file, which is exactly what dangles once our bundle is the only thing mounted.
                Assert(slot["m_FileID"].AsInt == 0,
                       "material slot points into THIS file (m_FileID=" + slot["m_FileID"].AsInt + ")");
                AssetTypeValueField mat = PrefabFields.Get(m, af, slot["m_PathID"].AsLong);
                Assert(mat != null && !mat["m_Name"].IsDummy,
                       "material slot " + slot["m_PathID"].AsLong + " resolves to a real Material");

                // 3. A shader. 0/0 is what a Material with no shader looks like on disk, and Unity
                //    substitutes Hidden/InternalErrorShader for it - magenta, not white.
                int fileId = mat["m_Shader"]["m_FileID"].AsInt;
                long pathId = mat["m_Shader"]["m_PathID"].AsLong;
                Assert(fileId > 0 && pathId != 0,
                       "'" + mat["m_Name"].AsString + "' names a shader: fileID=" + fileId + " pathID=" + pathId);
                AssetsFileExternal named = af.file.Metadata.Externals[fileId - 1];
                Assert(named.PathName != null &&
                       named.PathName.IndexOf(MaterialFields.BuiltinShaderCab, StringComparison.OrdinalIgnoreCase) >= 0,
                       "and that shader lives in the builtin-shader CAB: " + named.PathName);
                Assert(pathId == MaterialFields.StandardShaderPathId,
                       "at the measured pathID of Standard: " + pathId);

                // 4. BOUND. Every key in the block must be one THIS shader declares. An undeclared key
                //    is silently unbound - the data is on disk, the shader paints its default - so it
                //    is the second way a material draws white while every other arm stays green.
                string summary = MaterialFields.Summary(mat);
                bool texture = false, color = false;
                foreach (AssetTypeValueField t in mat["m_SavedProperties"]["m_TexEnvs"]["Array"].Children)
                {
                    Assert(declared.Contains(t["first"].AsString),
                           "'" + mat["m_Name"].AsString + "' writes texture key '" + t["first"].AsString +
                           "', and the shader it points at declares it: " + Names(declared));
                    if (t["second"]["m_Texture"]["m_PathID"].AsLong != 0) texture = true;
                }
                foreach (AssetTypeValueField c in mat["m_SavedProperties"]["m_Colors"]["Array"].Children)
                {
                    Assert(declared.Contains(c["first"].AsString),
                           "'" + mat["m_Name"].AsString + "' writes colour key '" + c["first"].AsString +
                           "', and the shader it points at declares it - otherwise the value is kept, " +
                           "never bound, and the model draws the shader's default white. Declared: " +
                           Names(declared));
                    if (!White(new[] { c["second"]["r"].AsFloat, c["second"]["g"].AsFloat,
                                       c["second"]["b"].AsFloat, c["second"]["a"].AsFloat })) color = true;
                }

                // 5. PAINT. This is the arm the spider failed: a shader with neither a texture nor a
                //    colour draws its own default, and Standard's default is opaque white.
                Assert(texture || color,
                       "'" + mat["m_Name"].AsString + "' carries paint - a texture, or a colour that is " +
                       "not white. Without either the renderer draws the shader's default, which IS white: " + summary);
                // 6. GLOW. _EmissionColor is stored in the SAME m_Colors block as albedo, so arms 4
                //    and 5 pass whether or not the shader will ever read it. The Standard shader
                //    reads it only when _EMISSION is in m_ShaderKeywords - a field this bake never
                //    wrote at all until now - so a material can carry a perfect cyan emission and
                //    light nothing, looking on screen exactly like one that was never given a glow.
                //    Measured against the shipped game: WPN_ANU_Melee_Blade_V01 and its siblings in
                //    an_equipment_assets_all.bundle carry "_EMISSION _METALLICGLOSSMAP _NORMALMAP"
                //    with m_CustomRenderQueue -1, which is the shape asserted here.
                bool emissiveColor = false;
                foreach (AssetTypeValueField c in mat["m_SavedProperties"]["m_Colors"]["Array"].Children)
                    if (c["first"].AsString == MaterialFields.EmissionColorProperty) emissiveColor = true;
                if (emissiveColor)
                {
                    string kw = mat["m_ShaderKeywords"].AsString ?? "";
                    Assert(kw.Contains(MaterialFields.EmissionKeyword),
                        "'" + mat["m_Name"].AsString + "' writes _EmissionColor AND the _EMISSION keyword. " +
                        "Without the keyword the Standard shader never reads the colour, so the model " +
                        "bakes looking exactly like one with no glow at all. m_ShaderKeywords = '" + kw + "'");
                    Assert(declared.Contains(MaterialFields.EmissionColorProperty),
                        "the shader this material points at declares _EmissionColor: " + Names(declared));
                    Assert(mat["m_CustomRenderQueue"].AsInt == -1,
                        "an emissive OPAQUE material keeps the shader's own render queue (-1), which is " +
                        "what every shipped emissive weapon material carries; got " +
                        mat["m_CustomRenderQueue"].AsInt);
                    report = (report == null ? "" : report + "\n  ") + "glow keywords='" + kw + "' queue=" +
                             mat["m_CustomRenderQueue"].AsInt;
                }
                report = report == null ? summary : report + "\n  " + summary;
            }
            return "renderer materials=" + mats.Children.Count + "\n  " + report;
        }
        finally { m.UnloadAll(); }
    }

    /// <summary>Standard's own default albedo, and therefore the value that proves nothing was set.</summary>
    private static bool White(float[] c) =>
        c == null || (Near(c[0], 1f) && Near(c[1], 1f) && Near(c[2], 1f) && Near(c[3], 1f));

    private static bool Near(float a, float b) => Math.Abs(a - b) < 1e-4f;

    private static string Rgba(float[] c) =>
        c == null ? "(none)" : ModelBuild.F(c[0]) + "," + ModelBuild.F(c[1]) + "," +
                               ModelBuild.F(c[2]) + "," + ModelBuild.F(c[3]);

    private static void Assert(bool ok, string what)
    {
        if (!ok) throw new Exception("MATERIAL bake FAILED: " + what);
    }
}
