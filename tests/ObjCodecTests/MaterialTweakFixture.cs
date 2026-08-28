using System;
using System.Collections.Generic;
using System.IO;
using AssetsTools.NET;
using AssetsTools.NET.Extra;
using Morgott.ContentTool.Bake;

/// <summary>
/// The three facts demos\MaterialTweak rests on, pinned against the SHIPPED game files, offline.
///
/// It does NOT re-implement <see cref="BundleBaker.ReplaceMaterialFloat"/> - a hand-written copy of
/// the writer would be a check on the copy, not on the writer, and the writer cannot be reached from
/// here at all (it needs ContentToolMain's embedded class database, which needs a Unity runtime).
/// What it pins instead is the demo's PRECONDITIONS, which is where this actually rots:
///
///   1. `ALN_Fireworm_DMG` is UNIQUE in aln_fireworm_assets_all.bundle. A "material" row is resolved
///      by name through BundleBaker.FindUnique, and that bundle also holds TWO Materials both named
///      `ALN_Fireworm` - so the demo can only ever address the _DMG one, and only while it stays
///      alone. A patch that adds a second one turns the demo into a throw, in game, at startup.
///   2. Its shipped `_GlossMapScale` is 1. That is the CONTROL value docs\VERIFIED-DEMOS.md records
///      the modded 0.15 against; if the game ships a different number the row is measuring nothing.
///   3. `_Glossiness` is NOT in its float block. ReplaceMaterialFloat APPENDS an unknown property
///      rather than refusing it, so a name the material's shader never declares is written to the
///      file, passes gate P3, and is invisible to the engine forever. The generated sample project
///      asks for exactly that (`ALN_Fireworm_DMG`, `_Glossiness=0.875`), which is why this arm names
///      the trap instead of leaving it to be rediscovered.
///
/// A missing game install is VOID, never PASS.
/// </summary>
internal static class MaterialTweakFixture
{
    private const string Bundle = "aln_fireworm_assets_all.bundle";
    private const string Material = "ALN_Fireworm_DMG";
    private const string Property = "_GlossMapScale";
    private const float Shipped = 1f;

    internal static string Run()
    {
        string here = AppDomain.CurrentDomain.BaseDirectory;
        string classData = Path.GetFullPath(Path.Combine(here, @"..\..\..\..\..\lib\classdata.tpk"));
        string root = Environment.GetEnvironmentVariable("PPRoot") ?? @"D:\Steam\steamapps\common\Phoenix Point";
        string shipped = Path.Combine(root, @"PhoenixPointWin64_Data\StreamingAssets\aa\StandaloneWindows64\" + Bundle);
        if (!File.Exists(classData)) return "MATERIALTWEAK fixture VOID - no " + classData;
        if (!File.Exists(shipped)) return "MATERIALTWEAK fixture VOID - no " + shipped + " (set PPRoot to the game folder)";

        AssetsManager m = new AssetsManager();
        m.LoadClassPackage(classData);
        BundleFileInstance bun = m.LoadBundleFile(shipped, true);
        AssetsFileInstance af = m.LoadAssetsFileFromBundle(bun, 0, false);
        m.LoadClassDatabaseFromPackage(af.file.Metadata.UnityVersion);
        try
        {
            List<AssetTypeValueField> named = new List<AssetTypeValueField>();
            int materials = 0;
            foreach (AssetFileInfo i in af.file.Metadata.GetAssetsOfType(AssetClassID.Material))
            {
                materials++;
                AssetTypeValueField mat = m.GetBaseField(af, i);
                if (mat["m_Name"].AsString == Material) named.Add(mat);
            }

            // 1. Unique, or the row cannot be resolved at all.
            Assert(named.Count == 1,
                   "exactly one Material in " + Bundle + " is called '" + Material + "' - a \"material\" row is " +
                   "resolved by NAME and an ambiguous one is refused; found " + named.Count + " of " + materials);

            AssetTypeValueField floats = named[0]["m_SavedProperties"]["m_Floats"]["Array"];
            float? got = null;
            bool glossiness = false;
            List<string> names = new List<string>();
            foreach (AssetTypeValueField p in floats.Children)
            {
                string key = p["first"].AsString;
                names.Add(key);
                if (key == Property) got = p["second"].AsFloat;
                if (key == "_Glossiness") glossiness = true;
            }

            // 2. The control value the in-game row is measured against.
            Assert(got.HasValue,
                   "'" + Material + "' declares '" + Property + "' in its own float block, so the demo is " +
                   "changing a property the shader really binds rather than appending an orphan. It carries: " +
                   string.Join(", ", names.ToArray()));
            Assert(Math.Abs(got.Value - Shipped) < 1e-4f,
                   "and it ships at " + Shipped + " - the control docs\\VERIFIED-DEMOS.md records the modded " +
                   "0.15 against; got " + got.Value);

            // 3. The trap, named rather than rediscovered - and it is the OPPOSITE of what it looks
            //    like. `_Glossiness` IS in this material's serialized float block (Unity keeps the
            //    properties of every shader a material was ever assigned), while the shader it
            //    actually points at declares no such property: measured live 2026-08-28,
            //    Material.HasProperty("_Glossiness") on the loaded ALN_Fireworm_DMG returns FALSE.
            //    So the sample project's `"material": "_Glossiness=0.875"` writes a real row, gate P3
            //    reads that row back off the file and passes, and the engine never binds it. A
            //    file-level check cannot tell a bound property from an orphan; only the shader's own
            //    declared list can.
            Assert(glossiness,
                   "'_Glossiness' is in that block on disk even though the shader " +
                   "(_PX_CHR/CHR_Character_shader) declares no such property - which is why gate P3, " +
                   "which reads the FILE, passes over the sample project's _Glossiness row while the " +
                   "engine ignores it. Choose a name off the shader, not off this list");

            return "MATERIALTWEAK fixture PASS " + Material + " unique in " + Bundle + ", " +
                   Property + "=" + got.Value + " shipped, " + floats.Children.Count + " float(s): " +
                   string.Join(", ", names.ToArray());
        }
        finally { m.UnloadAll(); }
    }

    private static void Assert(bool ok, string what)
    {
        if (!ok) throw new Exception("MATERIALTWEAK fixture FAILED: " + what);
    }
}
