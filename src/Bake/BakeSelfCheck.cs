using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Morgott.ContentTool.Import;
using UnityEngine;

namespace Morgott.ContentTool.Bake
{
    /// <summary>
    /// The U0a/U0b/U1 regression, kept as a command because it can only run inside the game: it
    /// bakes a bundle with <see cref="BundleBaker"/> and hands the freshly written file straight
    /// back to AssetBundle.LoadFromFile in the same run (METHODOLOGY).
    ///
    /// These gates are PROVEN (PROVEN-FOUNDATIONS #1-#8). This exists to catch a regression in OUR
    /// writer, not to re-litigate them.
    /// </summary>
    internal static class BakeSelfCheck
    {
        private const string DefaultSource = "mutoid_assets_all.bundle";
        private const string BundleName = "ct_selfcheck";

        /// <summary>
        /// The hand-built fixture for U8-grid / U8-step / U9-plan, beside the DLL (the csproj puts it
        /// there). ponytail: gate scaffolding, like u8_probe/u10_probe - drop it with those rows.
        /// </summary>
        private const string U9Probe = "u9_probe.glb";
        private const string U9ModelName = "u9_probe";

        /// <summary>The exact payload proven byte-exact through the engine (FINAL-PLAN 1.3).</summary>
        private static readonly byte[] Payload =
            { 0x00, 0xFF, 0xC3, 0x28, 0x00, 0x00, 0x41, 0x42, 0x80, 0xFE, 0x00, 0x7F };

        // E3 / U3d constants. Both are MEASURED, never chosen: dumped offline with AssetsTools.NET
        // on 2026-08-12 out of defaultlocalgroup_unitybuiltinshaders.bundle, whose serialized file is
        // CAB-207b1100b7c0eac21654e77dc25fa206 - and that CAB is already externals[1] of the
        // mutoid bundle this bake clones, so the fileID is read off the file rather than assumed.
        // Both now LIVE in MaterialFields, which carries no UnityEngine type, so the offline material
        // arm reads the same two numbers this gate does. Aliases, not copies.
        internal const string BuiltinShaderCab = MaterialFields.BuiltinShaderCab;
        internal const long StandardShaderPathId = MaterialFields.StandardShaderPathId;
        private const string StandardShaderName = "Standard";

        // The bundle FILES those CABs live in. An external PPtr resolves through the archive VFS -
        // `archive:/cab-x/cab-x` only exists while that bundle is LOADED - so having these mounted is
        // U3d/U3e's PRECONDITION, not decoration. Nothing mounts them at mod-init time:
        // PhoenixGame.FirstRunCrt awaits Addressables.InitializeAsync (catalog only, no content) and
        // then calls InitMods, so at t~1.85 s no shipped bundle is open and every external PPtr in
        // our bake dangles. The gate mounts them itself instead of waiting on a clock.
        private const string BuiltinShaderBundle = MaterialFields.BuiltinShaderBundle;
        private const string ShadersBundle = "_shaders_assets_all.bundle";

        /// <summary>What Unity substitutes for a Material whose m_Shader did not resolve.</summary>
        private const string ErrorShaderName = "Hidden/InternalErrorShader";

        // U3e: a CAB the cloned bundle does NOT reference, so the externals ENTRY is ours. Measured
        // the same way and the same day: '_PX_CHR/CHR_Character_shader' is a Phoenix Point shader, so
        // it exists in neither the builtin bundle nor anywhere the engine could substitute it from.
        private const string ShadersCab = "cab-22f8ff865f4ca3fac668dbcaedfdbb9d";
        private const long PxShaderPathId = -1622058160989239334L;
        private const string PxShaderName = "_PX_CHR/CHR_Character_shader";

        // U4: a Mesh the cloned bundle already ships, so the hierarchy's MeshFilter points at a real
        // object without U4 having to serialize a Mesh from nothing (that is U5). Same asset the
        // offline mesh round trip uses as its control, so its presence is already established.
        private const string ShippedMesh = "Geo_Head02_V01";
        // Three values no default and no accident produces, one of each sign and none of them 1.
        private const float ChildX = 1.5f, ChildY = -2.25f, ChildZ = 3.75f;

        // U5. A 1x2 quad: the bottom row sits at y=0 and the top row at y=2, so binding "at or above
        // the centre" puts exactly one row on bone1. bone1 rests BoneY above bone0; the deformation
        // arm then lifts it by Lift, which no rest pose and no default can produce.
        private const string SkinQuad = "v 0 0 0\nv 1 0 0\nv 1 2 0\nv 0 2 0\nvn 0 0 1\nf 1//1 2//1 3//1 4//1\n";
        private const float BoneY = 2f, Lift = 10f;

        // U6. The base controller our AnimatorOverrideController overrides, and the clip it plays -
        // both MEASURED offline out of _common_assets_all.bundle on 2026-08-12, the same way U3d's
        // shader constants were. 'MedKitHeartBeat1' is the simplest AnimatorController Phoenix Point
        // ships: ONE layer, ONE state, no transitions and no parameters, m_DefaultState 0, whose one
        // blend-tree node plays m_AnimationClips[0] - the clip of the same name, local to that same
        // file. So overriding that one clip is enough to make its state machine play ours.
        // internal, not private: U7 plays a clip on an IMPORTED rig through the SAME shipped base
        // controller, and a second copy of four measured constants is how the two would drift apart.
        internal const string CommonBundle = "_common_assets_all.bundle";
        internal const string CommonCab = "cab-229815d8dce2904e975e679b608088b9";
        internal const long MedKitControllerPathId = -8389213721431673559L;
        internal const long MedKitClipPathId = -2054101139859036125L;

        // The bone the baked clip drives, its rest height, and the ramp. 30 fps and 31 frames put
        // frame i at t=i/30, so y is the frame number: sampling at 0.5 s must read 15 and at 1 s
        // must read 30. RestY is deliberately neither 0 nor a value on the ramp, so "the clip did
        // nothing" and "the clip drove it" are three DIFFERENT numbers (-3.5, 15, 30) and each arm
        // asserts one of them by identity.
        private const string ClipBone = "u6_bone", OtherBone = "u6_other";
        private const float RestY = -3.5f, ClipRate = 30f, MidTime = 0.5f, MidY = 15f, EndY = 30f;
        private const int ClipFrames = 31;

        private const int Size = 16;
        private static readonly Color32 Magenta = new Color32(255, 0, 255, 255);
        private static readonly Color32 Green = new Color32(0, 255, 0, 255);

        internal static string Run(string sourceBundleName)
        {
            if (string.IsNullOrEmpty(sourceBundleName)) sourceBundleName = DefaultSource;
            StringBuilder log = new StringBuilder();
            int failures = 0;

            string source = ShippedBundlePath(sourceBundleName);

            string outDir = Path.Combine(Application.persistentDataPath, "ContentTool");
            string outPath = Path.Combine(outDir, BundleName + ".bundle");
            Directory.CreateDirectory(outDir);

            // A stale artifact from an earlier run must never be able to pass as this run's result.
            if (File.Exists(outPath)) { File.Delete(outPath); log.AppendLine("deleted stale " + outPath); }

            string texKey, binKey, matKey, extMatKey, badMatKey, newExtKey, newExtBadKey, wrongFileKey;
            string prefabKey, loneKey, skinKey, flatKey, rebindKey;
            string rampClipKey, flatClipKey, rampAocKey, flatAocKey;
            string animKey, animFlatKey, animOtherKey;
            long texPathId;
            int shaderExtId, pxExtId, commonExtId;
            // U8-grid / U8-step / U9-plan, all read off ONE hand-built fixture (lib\u9_probe.glb,
            // written by `dotnet run --project tests\ObjCodecTests -- --u9probe`). Filled inside the
            // bake below, asserted against the engine after it.
            string u9Plan = null, u9Skip = null, u9Grid = null, u9Hold = null, u9Missing = null;
            string gridClipKey = null, holdClipKey = null;
            try
            {
                using (BundleBaker baker = new BundleBaker(source, "contenttool"))
                {
                    log.AppendLine("read " + sourceBundleName + ": " + baker.SourceInfo);
                    texKey = baker.AddTexture2D("textures/selfcheck", Size, Size, Pixels());
                    binKey = baker.AddTextAsset("bin/selfcheck.bytes", Payload);
                    matKey = baker.AddMaterial("materials/selfcheck",
                        new Dictionary<string, string> { { "_MainTex", texKey } },
                        new Dictionary<string, float> { { "_Glossiness", 0.25f } });
                    texPathId = baker.PathIdOf(texKey);

                    // U3d: the same Material, but m_Shader is an EXTERNAL PPtr into the builtin-shader
                    // bundle. The bad twin is one pathID off - a real external, no such object - so a
                    // pass on the good arm cannot be Unity substituting something for any bad reference.
                    shaderExtId = baker.ExternalIdOf(BuiltinShaderCab);
                    extMatKey = baker.AddMaterial("materials/extshader", null, null,
                                                  shaderExtId, StandardShaderPathId);
                    badMatKey = baker.AddMaterial("materials/extshader_bad", null, null,
                                                  shaderExtId, StandardShaderPathId + 1);

                    // U3e: an externals entry we ADD ourselves, which is the case
                    // AnimatorOverrideController needs (U3d forged into a CAB the clone already had).
                    pxExtId = baker.AddExternal(ShadersCab);
                    newExtKey = baker.AddMaterial("materials/newext", null, null, pxExtId, PxShaderPathId);
                    newExtBadKey = baker.AddMaterial("materials/newext_bad", null, null, pxExtId, PxShaderPathId + 1);
                    // Same pathID, but through the external the clone ALREADY had. A different file,
                    // so it must NOT produce the PP shader - this is what proves the new ENTRY did the
                    // work rather than the fileID happening to land somewhere useful.
                    wrongFileKey = baker.AddMaterial("materials/newext_wrongfile", null, null,
                                                     shaderExtId, PxShaderPathId);

                    // U4: the hierarchy hangs off the U3d material, so its renderer carries a shader
                    // the engine can actually report by name.
                    prefabKey = baker.AddPrefab("prefabs/u4_root", "u4_child",
                                                ShippedMesh, extMatKey, ChildX, ChildY, ChildZ);
                    loneKey = baker.AddPrefab("prefabs/u4_lone", null, null, null, 0f, 0f, 0f);

                    // U5: a Mesh built from the EMPTY template plus bones, bind poses and weights.
                    // The flat twin is the same bake with every vertex on bone0 - moving ITS bone1
                    // must move nothing, which is what keeps the deformation arm from passing on a
                    // renderer that simply follows a transform.
                    BakedMesh skin = MeshBuild.From(ObjCodec.Parse(SkinQuad));
                    skinKey = baker.AddSkinnedPrefab("prefabs/u5_root", skin, extMatKey, BoneY, true, false);
                    flatKey = baker.AddSkinnedPrefab("prefabs/u5_flat", skin, extMatKey, BoneY, false, false);

                    // U5b: the REPLACEMENT path's weighting, on a rigged target. Baked with
                    // splitWeights FALSE - byte for byte the u5_flat control, which moves nothing -
                    // and then handed to SkinFields.Rebind, the same call BundleBaker.ReplaceMesh
                    // makes over a shipped mesh. Any deformation this shows was derived by Rebind
                    // from the bind poses alone, because nothing else here put a vertex on bone1.
                    rebindKey = baker.AddSkinnedPrefab("prefabs/u5b_rebind", skin, extMatKey, BoneY, false, true);

                    // U6: two clips that differ ONLY in their samples, two override controllers over
                    // the SAME shipped base that differ only in which clip they hand it, and three
                    // hierarchies that differ only in the bone name or the controller. Everything a
                    // U6 arm reads is therefore one variable away from its own control.
                    commonExtId = baker.AddExternal(CommonCab);
                    rampClipKey = baker.AddAnimationClip("clips/u6_ramp", ClipBone, Ramp(), ClipRate);
                    flatClipKey = baker.AddAnimationClip("clips/u6_flat", ClipBone, Flat(), ClipRate);
                    rampAocKey = baker.AddAnimatorOverrideController("controllers/u6_aoc_ramp",
                        commonExtId, MedKitControllerPathId, MedKitClipPathId, rampClipKey);
                    flatAocKey = baker.AddAnimatorOverrideController("controllers/u6_aoc_flat",
                        commonExtId, MedKitControllerPathId, MedKitClipPathId, flatClipKey);
                    animKey = baker.AddAnimatedPrefab("prefabs/u6_root", ClipBone, RestY, rampAocKey);
                    animFlatKey = baker.AddAnimatedPrefab("prefabs/u6_flatroot", ClipBone, RestY, flatAocKey);
                    // Same ramp controller, a bone the clip's binding PATH does not name.
                    animOtherKey = baker.AddAnimatedPrefab("prefabs/u6_otherroot", OtherBone, RestY, rampAocKey);

                    // U8-grid / U8-step / U9-plan. Three paths NO shipped file and no demo reaches:
                    // all five clips of the spider probe snap onto a whole rate, none of them is a
                    // STEP curve, and all five are uniquely named and drive bones. Each was proven
                    // offline and had nowhere to run in the game, which is why the rows sat
                    // PENDING-INGAME. The fixture is the same one the offline arms are built out of,
                    // written to a real .glb so the game can read it with the shipped assembly, and
                    // its two interesting clips are BAKED here so the engine has to accept the grids
                    // the resampler chose rather than only this mod's own reader agreeing with itself.
                    string probePath = Path.Combine(ContentToolMain.ModDir ?? ".", U9Probe);
                    if (!File.Exists(probePath)) u9Missing = probePath;
                    else
                    {
                        var u9Clips = new List<SampledClip>();
                        SkinnedModel u9Model = GlbReader.Read(File.ReadAllBytes(probePath), u9Clips);
                        BakedSkin u9Skin = ModelBuild.From(u9Model, U9ModelName);
                        var u9Skipped = new List<string>();
                        List<KeyValuePair<string, SampledClip>> u9 =
                            ClipFields.Bakeable(U9ModelName, u9Clips, u9Skipped);
                        var keys = new List<string>();
                        foreach (KeyValuePair<string, SampledClip> e in u9) keys.Add(e.Key);
                        u9Plan = string.Join(", ", keys.ToArray());
                        u9Skip = string.Join(" ", u9Skipped.ToArray());
                        foreach (KeyValuePair<string, SampledClip> e in u9)
                        {
                            SampledClip c = e.Value;
                            string key = baker.AddAnimationClip("clips/" + e.Key,
                                ClipFields.Bindings(c, u9Skin), c.Times.Length, c.SampleRate, false);
                            float last = c.Times[c.Times.Length - 1];
                            if (c.Name == "Walk")
                            {
                                gridClipKey = key;
                                u9Grid = ModelBuild.F(c.SampleRate) + " Hz x " + c.Times.Length +
                                         " frame(s), last frame " + ModelBuild.F(last) + " s";
                            }
                            else if (c.Name == "Hold")
                            {
                                holdClipKey = key;
                                // WHEN the last held frame sits is the whole measurement: a dense bank
                                // cannot store a discontinuity, so the ramp the runtime is forced into
                                // is the gap between that frame and the next.
                                int hold = 0;
                                ObjVector3[] xs = c.Tracks[0].Translations;
                                for (int f = 0; f < xs.Length; f++)
                                    if (Near(xs[f].X, xs[0].X)) hold = f;
                                float ramp = (c.Times[hold + 1] - c.Times[hold]) * 1000f;
                                u9Hold = ModelBuild.F(c.SampleRate) + " Hz x " + c.Times.Length +
                                         " frame(s), holds to frame " + hold + ", ramp " +
                                         ModelBuild.F(ramp) + " ms; " + c.LossyReason;
                            }
                        }
                    }

                    baker.Write(outPath, BundleName);
                    log.AppendLine("WROTE " + outPath + " " + new FileInfo(outPath).Length +
                                   " B as " + baker.WrittenIdentity);
                }
            }
            catch (Exception ex) { return log.Append("BAKE THREW ").Append(ex).ToString(); }

            // Read off the written file BEFORE the engine opens it: Unity holds the bundle open
            // after LoadFromFile, and a second reader on the same path is asking for trouble.
            string props = BundleBaker.ReadMaterialProperties(outPath, matKey.Substring(matKey.LastIndexOf('/') + 1));
            string propsExt = BundleBaker.ReadMaterialProperties(outPath, extMatKey.Substring(extMatKey.LastIndexOf('/') + 1));

            // ---- load half: only the freshly written file, never a path we did not just write
            List<string> unity = new List<string>();
            Application.LogCallback tap = (msg, stack, type) =>
            {
                if (type == LogType.Error || type == LogType.Exception || type == LogType.Warning) unity.Add(msg);
            };
            Application.logMessageReceived += tap;
            AssetBundle bundle, builtin = null, shaders = null, common = null;
            try { bundle = AssetBundle.LoadFromFile(outPath); }
            finally { Application.logMessageReceived -= tap; }

            if (bundle == null)
                return log.Append("U0a FAIL LoadFromFile returned null; Unity said: ")
                          .Append(unity.Count == 0 ? "(nothing logged)" : string.Join(" | ", unity.ToArray()))
                          .ToString();

            try
            {
                string[] names = bundle.GetAllAssetNames();
                log.AppendLine("U0a PASS loaded, " + names.Length + " asset name(s)");

                // Control in the same run: a shipped asset of the cloned bundle must still resolve,
                // so "our objects load" cannot hide a source file we corrupted while adding them.
                string control = null;
                foreach (string n in names) if (!n.StartsWith("assets/contenttool/")) { control = n; break; }
                failures += Check(log, "CONTROL", control != null && bundle.LoadAsset<UnityEngine.Object>(control) != null,
                                  "shipped asset '" + control + "' still resolves");

                TextAsset ta = bundle.LoadAsset<TextAsset>(binKey);
                byte[] got = ta == null ? null : ta.bytes;
                failures += Check(log, "U0b", Same(got, Payload),
                                  "TextAsset len=" + (got == null ? -1 : got.Length) +
                                  " hex=" + (got == null ? "(null)" : BitConverter.ToString(got)));

                Texture2D tex = bundle.LoadAsset<Texture2D>(texKey);
                if (tex == null) failures += Check(log, "U1", false, "Texture2D is null");
                else
                {
                    Color32[] px = tex.GetPixels32();
                    Color32 a = px[0], b = px[(Size / 2) * Size + Size / 2];
                    // Two DIFFERENT expected colours: a writer that fills the whole image with one
                    // value, or reads zeroes out of a .resS, cannot pass both.
                    failures += Check(log, "U1",
                        tex.width == Size && tex.height == Size && Same(a, Magenta) && Same(b, Green),
                        tex.width + "x" + tex.height + " " + tex.format + " px[0,0]=" + Str(a) + " px[8,8]=" + Str(b));
                }

                // U3a: the Material survives the bundle. It has no shader on purpose - production
                // assigns one from a PP Def donor at runtime (U3b, next slice) - and a shaderless
                // Material exposes no properties through the engine API, so the property block is
                // read back off the written FILE instead of guessed at.
                Material mat = bundle.LoadAsset<Material>(matKey);
                failures += Check(log, "U3a", mat != null, "Material '" + matKey + "' loads from the bundle");
                failures += Check(log, "U3a-refs",
                    props.Contains("_MainTex -> fileID=0 pathID=" + texPathId) && props.Contains("_Glossiness=0.25"),
                    props + " (expected _MainTex -> fileID=0 pathID=" + texPathId + ", _Glossiness=0.25)");

                // U3d-premount: the ROOT CAUSE, asserted rather than assumed. With the target archive
                // NOT mounted the forged external must come back as the error shader - a positive
                // identity, so this arm is falsifiable: if Unity resolved externals without the
                // target bundle open, this line goes RED and the rest of the U3d story is wrong.
                // Read BEFORE anything is mounted, because a PPtr is resolved when the Material is
                // deserialized, which is now.
                // Its PRECONDITION - that nobody ELSE has the archive open - only holds when ct_bake
                // runs before the game pulls the builtin shaders in through Addressables. It is
                // MEASURED by ProbeArchive, never inferred from the value under test: skipping on
                // "the reading was 'Standard'" would silence the arm for any real defect that also
                // produces 'Standard'. When the precondition holds the assertion is unchanged -
                // the error shader by name or FAIL, with no value-shaped escape hatch.
                string builtinProbe = ProbeArchive(BuiltinShaderBundle);
                // Same measurement for U3e's archive: if it is already open our mount returns null
                // and U3e would report VOID - measuring nothing - on a run where it resolves fine.
                string shadersProbe = ProbeArchive(ShadersBundle);
                if (builtinProbe == Held)
                    log.AppendLine("U3d-premount SKIP precondition not met: '" + BuiltinShaderBundle +
                                   "' cannot be opened by us, so it is already mounted (the game loaded " +
                                   "it through Addressables) and 'not loaded' is not a state this run has");
                else if (builtinProbe != Free)
                    // Not "already open" and not openable either: the file itself is the problem, and
                    // nothing below can measure anything against an archive that is not there.
                    log.AppendLine("U3d-premount VOID '" + BuiltinShaderBundle + "' could not be read: " +
                                   builtinProbe);
                else
                    failures += Check(log, "U3d-premount",
                        ShaderNameOf(bundle.LoadAsset<Material>(extMatKey)) == ErrorShaderName,
                        "with '" + BuiltinShaderBundle + "' NOT loaded the forged external reports '" +
                        ShaderNameOf(bundle.LoadAsset<Material>(extMatKey)) + "' (expected '" + ErrorShaderName + "')");

                // Mount the two archives the externals name, then RE-open our bundle so the materials
                // deserialize against them. Nothing else in the game does this at mod-init time; the
                // human runs that recorded U3d/U3e green got it for free because the main menu had
                // already pulled both bundles in through Addressables.
                builtin = AssetBundle.LoadFromFile(ShippedBundlePath(BuiltinShaderBundle));
                shaders = AssetBundle.LoadFromFile(ShippedBundlePath(ShadersBundle));
                // U6's base AnimatorController is an external PPtr like U3d's shader, so its archive
                // has to be mounted for the same reason and at the same moment.
                common = AssetBundle.LoadFromFile(ShippedBundlePath(CommonBundle));
                // Unity refuses to open a file that is already open, so builtin==null means EITHER the
                // mount failed OR the game had it mounted all along - and the probe is the evidence
                // that tells those two apart. Only 'Held' makes the arms answerable without our own
                // mount; a probe that failed for any other reason leaves them VOID, as it should.
                bool builtinReady = builtin != null || builtinProbe == Held;
                bool shadersReady = shaders != null || shadersProbe == Held;
                log.AppendLine("mounted " + BuiltinShaderBundle + "=" + (builtin != null) +
                               " (probe: " + builtinProbe + ")" +
                               " " + ShadersBundle + "=" + (shaders != null) +
                               " (probe: " + shadersProbe + ")" +
                               " " + CommonBundle + "=" + (common != null));
                bundle.Unload(true);
                bundle = AssetBundle.LoadFromFile(outPath);
                if (bundle == null)
                {
                    // Every arm below reads through this bundle, so none of them can answer.
                    log.AppendLine("U3d VOID the bake could not be re-opened after mounting the shader archives");
                    log.AppendLine("U3e VOID same");
                    log.AppendLine("U4 VOID same");
                    return log.Append("ct_bake: ").Append(failures).Append(" FAILURE(S)").ToString();
                }
                mat = bundle.LoadAsset<Material>(matKey);

                // U3d (E3). Two separate questions, asserted separately: did we WRITE the external
                // reference, and does the ENGINE resolve it. The oracle is the shader's NAME, not
                // null-ness: a name measured offline out of the target file is a value Unity cannot
                // invent from a broken reference, whereas an unresolved shader may surface as null OR
                // as a substitute, and both would read the same under a null test. Every control arm
                // asserts the error shader BY NAME too - "not Standard" was true of a run in which
                // nothing resolved at all, which is how these passed vacuously for a day.
                failures += Check(log, "U3d-wrote",
                    shaderExtId > 0 && propsExt.Contains("shader fileID=" + shaderExtId + " pathID=" + StandardShaderPathId),
                    propsExt + " (external '" + BuiltinShaderCab + "' is fileID=" + shaderExtId + ")");

                string extName = ShaderNameOf(bundle.LoadAsset<Material>(extMatKey));
                string badName = ShaderNameOf(bundle.LoadAsset<Material>(badMatKey));

                if (!builtinReady)
                    log.AppendLine("U3d VOID '" + BuiltinShaderBundle + "' did not load, so no U3d arm can resolve");
                else
                {
                    failures += Check(log, "U3d", extName == StandardShaderName,
                        "forged external m_Shader {fileID=" + shaderExtId + ", pathID=" + StandardShaderPathId +
                        "} -> '" + extName + "' (expected '" + StandardShaderName + "')");
                    failures += Check(log, "U3d-ctl-badid", badName == ErrorShaderName,
                        "same external, pathID+1 -> '" + badName + "' (expected '" + ErrorShaderName + "')");
                    failures += Check(log, "U3d-ctl-noptr", ShaderNameOf(mat) == ErrorShaderName,
                        "the U3a material, which points at no shader at all, reports '" +
                        ShaderNameOf(mat) + "' (expected '" + ErrorShaderName + "')");
                }

                // U3e. Same shape as U3d, one rung harder: the externals ENTRY is ours, not the
                // clone's. Oracle is again the name - a Phoenix Point shader name the engine cannot
                // invent - and the written bytes are asserted separately from the resolution.
                string pxProps = BundleBaker.ReadMaterialProperties(outPath, newExtKey.Substring(newExtKey.LastIndexOf('/') + 1));
                failures += Check(log, "U3e-wrote",
                    pxExtId > shaderExtId && pxProps.Contains("shader fileID=" + pxExtId + " pathID=" + PxShaderPathId),
                    pxProps + " (our added external '" + ShadersCab + "' is fileID=" + pxExtId +
                    ", beyond the clone's own " + shaderExtId + ")");

                string pxName = ShaderNameOf(bundle.LoadAsset<Material>(newExtKey));
                string pxBadName = ShaderNameOf(bundle.LoadAsset<Material>(newExtBadKey));
                string pxWrongName = ShaderNameOf(bundle.LoadAsset<Material>(wrongFileKey));

                if (!shadersReady)
                    log.AppendLine("U3e VOID '" + ShadersBundle + "' did not load, so no U3e arm can resolve");
                else
                {
                    failures += Check(log, "U3e", pxName == PxShaderName,
                        "an externals entry WE added, {fileID=" + pxExtId + ", pathID=" + PxShaderPathId +
                        "} -> '" + pxName + "' (expected '" + PxShaderName + "')");
                    failures += Check(log, "U3e-ctl-badid", pxBadName == ErrorShaderName,
                        "our external, pathID+1 -> '" + pxBadName + "' (expected '" + ErrorShaderName + "')");
                    failures += Check(log, "U3e-ctl-wrongfile", pxWrongName == ErrorShaderName,
                        "same pathID through the clone's OWN external (fileID=" + shaderExtId + ") -> '" +
                        pxWrongName + "' (expected '" + ErrorShaderName + "' - a different file)");
                }

                // U3b/U3c: the RUNTIME donor path (FINAL-PLAN 10.1/10.2). U3d made it OPTIONAL for the
                // baked path by giving the shader at bake time, but nothing had ever measured the
                // assignment itself, and it still stands for anything materialized live. The donor is
                // a LIVE Shader instance taken off an object the game shipped - the one U3d just
                // resolved out of defaultlocalgroup_unitybuiltinshaders.bundle - which is the same
                // object a Def walk ends at, reached without loading an actor prefab. It is NOT
                // Shader.Find: that would answer "does this process have a Standard shader", not
                // "can a donor's shader be handed to our Material".
                //
                // Two separate questions again. U3b: does the assignment TAKE. U3c: does the property
                // block written BEFORE any shader existed (U3a-refs read it off the file: a _MainTex
                // PPtr and _Glossiness=0.25) survive being interpreted by a shader for the first time.
                // U3c's texture oracle is object IDENTITY against the Texture2D U1 already asserted
                // the pixels of - a shader that merely exposes a _MainTex slot cannot pass it - and
                // U3b's control is the SAME material read BEFORE the assignment, so 'it says Standard'
                // cannot be something it was already saying.
                Material donorTarget = bundle.LoadAsset<Material>(matKey);
                Shader donorShader = null;
                Material donorSource = bundle.LoadAsset<Material>(extMatKey);
                if (donorSource != null) donorShader = donorSource.shader;
                Texture2D bakedTex = bundle.LoadAsset<Texture2D>(texKey);
                if (!builtinReady || donorTarget == null || donorShader == null ||
                    donorShader.name != StandardShaderName)
                    log.AppendLine("U3b VOID no live donor shader to hand over (donor reports '" +
                                   (donorShader == null ? "(none)" : donorShader.name) + "', expected '" +
                                   StandardShaderName + "'), so U3c cannot answer either");
                else
                {
                    string before = ShaderNameOf(donorTarget);
                    donorTarget.shader = donorShader;
                    string after = ShaderNameOf(donorTarget);
                    failures += Check(log, "U3b",
                        before == ErrorShaderName && after == StandardShaderName,
                        "the shaderless baked Material reported '" + before + "', and after being handed " +
                        "the live donor's shader reports '" + after + "' (expected '" + ErrorShaderName +
                        "' then '" + StandardShaderName + "')");
                    Texture gotTex = donorTarget.GetTexture("_MainTex");
                    float gotGloss = donorTarget.GetFloat("_Glossiness");
                    failures += Check(log, "U3c",
                        bakedTex != null && ReferenceEquals(gotTex, bakedTex) && Near(gotGloss, 0.25f),
                        "after the assignment _MainTex -> '" + (gotTex == null ? "(null)" : gotTex.name) +
                        "' isTheBakedTexture2D=" + ReferenceEquals(gotTex, bakedTex) +
                        " | _Glossiness=" + gotGloss +
                        " (expected the baked texture '" + (bakedTex == null ? "(null)" : bakedTex.name) +
                        "' and 0.25)");
                }

                // U4. Same two-question shape as U3d/U3e: what did we WRITE (read off the file, by the
                // referenced objects' NAMES, which a broken PPtr cannot produce), and what does the
                // ENGINE build out of it. The no-child twin is the control that keeps childCount=1
                // from being something the engine supplies on its own.
                string rootName = prefabKey.Substring(prefabKey.LastIndexOf('/') + 1);
                string loneName = loneKey.Substring(loneKey.LastIndexOf('/') + 1);
                string wrote = BundleBaker.ReadPrefabSummary(outPath, rootName);
                string wroteLone = BundleBaker.ReadPrefabSummary(outPath, loneName);
                string wantWrote = "root '" + rootName + "' comps=1 children=1 | child 'u4_child' comps=3 " +
                                   "fatherIsRoot=True pos=1.5,-2.25,3.75 scale=1,1,1 " +
                                   "mesh='" + ShippedMesh + "' material='" +
                                   extMatKey.Substring(extMatKey.LastIndexOf('/') + 1) + "'";
                failures += Check(log, "U4-wrote", wrote == wantWrote, wrote + " (expected " + wantWrote + ")");
                failures += Check(log, "U4-wrote-ctl-lone", wroteLone == "root '" + loneName + "' comps=1 children=0",
                                  wroteLone);

                GameObject root = bundle.LoadAsset<GameObject>(prefabKey);
                if (root == null) failures += Check(log, "U4", false, "LoadAsset<GameObject>('" + prefabKey + "') is null");
                else
                {
                    Transform child = root.transform.childCount == 1 ? root.transform.GetChild(0) : null;
                    MeshFilter mf = child == null ? null : child.GetComponent<MeshFilter>();
                    MeshRenderer mr = child == null ? null : child.GetComponent<MeshRenderer>();
                    Vector3 p = child == null ? Vector3.zero : child.localPosition;
                    string meshName = mf == null || mf.sharedMesh == null ? "(none)" : mf.sharedMesh.name;
                    // The oracle for both references is the target's NAME - a value the engine cannot
                    // invent from a broken PPtr. Deliberately NOT the renderer's shader: that is an
                    // EXTERNAL reference, so it answers U3d's question (is the target archive mounted)
                    // rather than U4's. It is printed below, never asserted.
                    string matName = mr == null || mr.sharedMaterial == null ? "(none)" : mr.sharedMaterial.name;
                    string wantMat = extMatKey.Substring(extMatKey.LastIndexOf('/') + 1);
                    failures += Check(log, "U4",
                        root.name == rootName && child != null && child.name == "u4_child" &&
                        Near(p.x, ChildX) && Near(p.y, ChildY) && Near(p.z, ChildZ) &&
                        meshName == ShippedMesh && matName == wantMat,
                        "'" + root.name + "' childCount=" + root.transform.childCount +
                        " child='" + (child == null ? "(none)" : child.name) + "'" +
                        " localPosition=" + p.x + "," + p.y + "," + p.z +
                        " MeshFilter.sharedMesh='" + meshName + "'" +
                        " MeshRenderer.sharedMaterial='" + matName + "'" +
                        " (its shader, an EXTERNAL ref and U3d's question, reports '" +
                        (mr == null || mr.sharedMaterial == null || mr.sharedMaterial.shader == null
                         ? "(none)" : mr.sharedMaterial.shader.name) + "')" +
                        " (expected child 'u4_child' at " + ChildX + "," + ChildY + "," + ChildZ +
                        " with mesh '" + ShippedMesh + "' and material '" + wantMat + "')");
                }

                GameObject lone = bundle.LoadAsset<GameObject>(loneKey);
                failures += Check(log, "U4-ctl-lone", lone != null && lone.transform.childCount == 0,
                    "the root baked with NO child reports childCount=" +
                    (lone == null ? "(null GameObject)" : lone.transform.childCount.ToString()));

                // U5. Same two-question shape again, plus a third the static gates never had to ask:
                // does the skin BIND. What we WROTE is read off the file by names and counts; what the
                // ENGINE builds is read off the SkinnedMeshRenderer; and whether the binding is real
                // is measured by lifting bone1 and baking the deformed mesh, against a twin whose
                // vertices are all on bone0 and must therefore not move at all.
                string skinRoot = skinKey.Substring(skinKey.LastIndexOf('/') + 1);
                string flatRoot = flatKey.Substring(flatKey.LastIndexOf('/') + 1);
                string skinMatName = extMatKey.Substring(extMatKey.LastIndexOf('/') + 1);
                string wroteSkin = BundleBaker.ReadSkinSummary(outPath, skinRoot);
                string wantSkin = WantSkin(skinRoot, skinMatName, true);
                failures += Check(log, "U5-wrote", wroteSkin == wantSkin, wroteSkin + " (expected " + wantSkin + ")");
                string wroteFlat = BundleBaker.ReadSkinSummary(outPath, flatRoot);
                string wantFlat = WantSkin(flatRoot, skinMatName, false);
                failures += Check(log, "U5-wrote-ctl-flat", wroteFlat == wantFlat, wroteFlat + " (expected " + wantFlat + ")");

                GameObject skinRootGo = bundle.LoadAsset<GameObject>(skinKey);
                SkinnedMeshRenderer smr = skinRootGo == null
                    ? null : skinRootGo.GetComponentInChildren<SkinnedMeshRenderer>(true);
                if (smr == null)
                    failures += Check(log, "U5", false,
                        "no SkinnedMeshRenderer under '" + skinKey + "' (root is " +
                        (skinRootGo == null ? "null" : "'" + skinRootGo.name + "'") + ")");
                else
                {
                    Mesh mesh = smr.sharedMesh;
                    string bone0 = SkinFields.Bone0Name(skinRoot), bone1 = SkinFields.Bone1Name(skinRoot);
                    int bones = smr.bones == null ? -1 : smr.bones.Length;
                    string got0 = bones > 0 && smr.bones[0] != null ? smr.bones[0].name : "(none)";
                    string got1 = bones > 1 && smr.bones[1] != null ? smr.bones[1].name : "(none)";
                    string gotRoot = smr.rootBone == null ? "(none)" : smr.rootBone.name;
                    int bindposes = mesh == null || mesh.bindposes == null ? -1 : mesh.bindposes.Length;
                    int verts = mesh == null ? -1 : mesh.vertexCount;
                    failures += Check(log, "U5",
                        mesh != null && mesh.name == SkinFields.MeshName(skinRoot) && verts == 4 &&
                        bones == 2 && got0 == bone0 && got1 == bone1 && gotRoot == bone0 && bindposes == 2,
                        "sharedMesh='" + (mesh == null ? "(none)" : mesh.name) + "' vertexCount=" + verts +
                        " bones=" + bones + " [" + got0 + "," + got1 + "] rootBone='" + gotRoot + "'" +
                        " bindposes=" + bindposes +
                        " (expected mesh '" + SkinFields.MeshName(skinRoot) + "' with 4 vertices, bones [" +
                        bone0 + "," + bone1 + "], rootBone '" + bone0 + "', 2 bindposes)");

                    failures += Deform(log, "U5-deform", skinRootGo, bone1, Lift, BoneY + Lift);
                    // The control asks the SAME question of a bake whose weights are all on bone0:
                    // its top row must stay where the rest pose put it.
                    GameObject flatRootGo = bundle.LoadAsset<GameObject>(flatKey);
                    failures += Deform(log, "U5-ctl-flat", flatRootGo, SkinFields.Bone1Name(flatRoot), Lift, BoneY);

                    // U5b. The same two questions of the REPLACEMENT path's weighting. u5b_rebind was
                    // baked flat and re-bound, so U5-ctl-flat above - the identical bake without the
                    // Rebind call, staying at y=[0,2] - is this arm's control: the only difference
                    // between the two prefabs is that one call, and only one of them deforms.
                    string rebindRoot = rebindKey.Substring(rebindKey.LastIndexOf('/') + 1);
                    string wroteRebind = BundleBaker.ReadSkinSummary(outPath, rebindRoot);
                    // WantSkin(..., true) - the SPLIT expectation, asserted on a bake that asked for
                    // no split. Nearest-bindpose has to have produced it.
                    string wantRebind = WantSkin(rebindRoot, skinMatName, true);
                    failures += Check(log, "U5b-wrote", wroteRebind == wantRebind,
                                      wroteRebind + " (expected " + wantRebind + ")");
                    failures += Deform(log, "U5b-deform", bundle.LoadAsset<GameObject>(rebindKey),
                                       SkinFields.Bone1Name(rebindRoot), Lift, BoneY + Lift);
                }

                // U6. Three questions, asserted separately. What did we WRITE (read off the file);
                // does the clip EVALUATE at all (AnimationClip.SampleAnimation, which needs no
                // controller and so answers about the clip alone); and does MECANIM accept it -
                // an Animator whose only controller is our AnimatorOverrideController over a
                // SHIPPED base, driven by Animator.Update.
                string rampName = Tail(rampClipKey), flatClipName = Tail(flatClipKey);
                string rampAocName = Tail(rampAocKey), flatAocName = Tail(flatAocKey);
                uint bonePathHash = SkinFields.BoneHash(ClipBone);
                string wroteClip = BundleBaker.ReadClipSummary(outPath, rampName, rampAocName);
                string wantClip = WantClip(rampName, rampAocName, bonePathHash, commonExtId, EndY);
                failures += Check(log, "U6-wrote", wroteClip == wantClip, wroteClip + " (expected " + wantClip + ")");
                string wroteFlatClip = BundleBaker.ReadClipSummary(outPath, flatClipName, flatAocName);
                string wantFlatClip = WantClip(flatClipName, flatAocName, bonePathHash, commonExtId, 0f);
                failures += Check(log, "U6-wrote-ctl-flat", wroteFlatClip == wantFlatClip,
                                  wroteFlatClip + " (expected " + wantFlatClip + ")");
                string wroteAnim = BundleBaker.ReadAnimatedSummary(outPath, Tail(animKey));
                string wantAnim = "root '" + Tail(animKey) + "' animator controller='" + rampAocName +
                                  "' avatar=0 culling=0 hierarchy=True | bone='" + ClipBone +
                                  "' rest=0," + RestY.ToString("0.###", CultureInfo.InvariantCulture) + ",0";
                failures += Check(log, "U6-wrote-anim", wroteAnim == wantAnim, wroteAnim + " (expected " + wantAnim + ")");

                AnimationClip ramp = bundle.LoadAsset<AnimationClip>(rampClipKey);
                failures += Check(log, "U6-clip",
                    ramp != null && ramp.name == rampName && !ramp.legacy &&
                    Near(ramp.frameRate, ClipRate) && Near(ramp.length, (ClipFrames - 1) / ClipRate),
                    ramp == null ? "LoadAsset<AnimationClip>('" + rampClipKey + "') is null"
                                 : "'" + ramp.name + "' legacy=" + ramp.legacy + " frameRate=" + ramp.frameRate +
                                   " length=" + ramp.length + " (expected '" + rampName + "', legacy False, " +
                                   ClipRate + " fps, " + ((ClipFrames - 1) / ClipRate) + " s)");

                // Does the CLIP evaluate? SampleAnimation needs no controller, so a red here is the
                // clip's bytes and a red only below is Mecanim's acceptance of them.
                failures += Sample(log, "U6-sample", bundle.LoadAsset<GameObject>(animKey), ramp, ClipBone, MidY, EndY);
                // The SAME clip on a hierarchy whose bone is spelled differently. A binding is a
                // CRC-32 of a PATH, so this must leave the rest pose exactly where it was - which is
                // what "no automatic retargeting" means, measured rather than asserted.
                failures += Sample(log, "U6-sample-ctl-path", bundle.LoadAsset<GameObject>(animOtherKey),
                                   ramp, OtherBone, RestY, RestY);

                RuntimeAnimatorController rac = bundle.LoadAsset<RuntimeAnimatorController>(rampAocKey);
                string racType = rac == null ? "(null)" : rac.GetType().Name;
                string racClips = rac == null ? "(null)" : ClipNames(rac.animationClips);
                // The TYPE by name, not "is it non-null": an AnimatorOverrideController that came
                // back as a plain AnimatorController would be the interesting failure, and a null
                // test cannot tell those apart.
                failures += Check(log, "U6-aoc",
                    racType == "AnimatorOverrideController" && racClips.Contains(rampName),
                    "LoadAsset<RuntimeAnimatorController>('" + rampAocKey + "') is a " + racType +
                    " whose clips are [" + racClips + "] (expected an AnimatorOverrideController " +
                    "listing '" + rampName + "')");

                // Mecanim itself. Update by exactly MidTime, so the number is the same one
                // SampleAnimation produced and the two arms cannot disagree by construction.
                failures += Mecanim(log, "U6-mecanim", bundle.LoadAsset<GameObject>(animKey), ClipBone, MidY);
                // Same base controller, same Animator, same bone - only the OVERRIDE clip differs,
                // and the flat one must drive the bone off its -3.5 rest onto exactly 0.
                failures += Mecanim(log, "U6-mecanim-ctl-flat", bundle.LoadAsset<GameObject>(animFlatKey), ClipBone, 0f);
                // Same controller and same clip as U6-mecanim; only the bone's NAME differs.
                failures += Mecanim(log, "U6-mecanim-ctl-path", bundle.LoadAsset<GameObject>(animOtherKey), OtherBone, RestY);

                // U8-grid / U8-step / U9-plan, the game's own answer. Two readings per row, and they
                // fail apart: what THIS MOD's resampler and plan made of the fixture, and what the
                // ENGINE then made of the clip that was baked from it. A missing fixture is said out
                // loud - a silent skip is how the SKIN-ABOVE arm spent weeks reporting a green VOID.
                if (u9Missing != null)
                    log.AppendLine("U9-plan VOID the fixture is gone: no " + u9Missing +
                                   " - U8-grid and U8-step cannot answer either");
                else
                {
                    failures += Check(log, "U9-plan",
                        u9Plan == "u9_probe_walk, u9_probe_walk_1, u9_probe_hold" &&
                        u9Skip.Contains("'Morphs'") && u9Skip.Contains("SKIPPED"),
                        "a .glb whose animations are neither all bakeable nor all uniquely named still " +
                        "bakes: planned " + u9Plan + " | " + u9Skip +
                        " (expected u9_probe_walk, u9_probe_walk_1, u9_probe_hold and 'Morphs' SKIPPED)");

                    AnimationClip grid = gridClipKey == null ? null : bundle.LoadAsset<AnimationClip>(gridClipKey);
                    AnimationClip hold = holdClipKey == null ? null : bundle.LoadAsset<AnimationClip>(holdClipKey);
                    // 17 frames at 30 Hz ends at 16/30 = 0.5333 s, PAST the file's last key at 0.511;
                    // the Round() this fixed ended the clip at 0.5 and threw the last 11 ms away.
                    failures += Check(log, "U8-grid",
                        u9Grid == "30 Hz x 17 frame(s), last frame 0.533 s" &&
                        grid != null && Near(grid.frameRate, 30f) && Near(grid.length, 16f / 30f),
                        "key times 0 / 0.1 / 0.511 s fit no whole rate, so the fallback grid must REACH " +
                        "the last key: reader says " + u9Grid + ", and the engine reads the baked clip " +
                        "back as " + (grid == null ? "(null)" : ModelBuild.F(grid.frameRate) + " Hz, " +
                        ModelBuild.F(grid.length) + " s") + " (expected 30 Hz x 17, last frame 0.533 s, " +
                        "engine 30 Hz and 0.533 s; rounding would end at 0.5)");
                    // 121 frames at 120 Hz: the STEP hold survives to one frame before the file's own
                    // 0.5 s jump, and the ramp the dense bank forces is 8.333 ms rather than the 250 ms
                    // the same clip's own 2 Hz LINEAR grid would give.
                    failures += Check(log, "U8-step",
                        u9Hold != null && u9Hold.StartsWith("120 Hz x 121 frame(s), holds to frame 59, ramp 8.333 ms") &&
                        u9Hold.Contains("ONE frame wide (8.333 ms)") &&
                        hold != null && Near(hold.frameRate, 120f) && Near(hold.length, 1f),
                        "a STEP curve is resampled on the highest whole multiple of its own snapped rate " +
                        "the mod allows: reader says " + u9Hold + ", and the engine reads the baked clip " +
                        "back as " + (hold == null ? "(null)" : ModelBuild.F(hold.frameRate) + " Hz, " +
                        ModelBuild.F(hold.length) + " s") + " (expected 120 Hz x 121, hold to frame 59, " +
                        "ramp 8.333 ms, engine 120 Hz and 1 s; the 2 Hz LINEAR grid would ramp 250 ms)");
                }

                if (unity.Count > 0) log.AppendLine("Unity also said: " + string.Join(" | ", unity.ToArray()));
            }
            finally
            {
                if (bundle != null) bundle.Unload(true);
                // false, not true: the game will pull these two in through Addressables later, and
                // destroying shaders it is about to reference is not this gate's business. Unloading
                // the ARCHIVE is what matters - leaving it mounted would make a later Addressables
                // load of the same file collide.
                if (builtin != null) builtin.Unload(false);
                if (shaders != null) shaders.Unload(false);
                if (common != null) common.Unload(false);
            }

            log.Append(failures == 0 ? "ct_bake: ALL PASS" : "ct_bake: " + failures + " FAILURE(S)");
            return log.ToString();
        }

        /// <summary>
        /// ct_dump: the field-name oracle for the gates still open. Uses the same class database the
        /// writer builds from, so what it prints is what AddX has to fill in.
        /// </summary>
        internal static string Dump(string[] args)
        {
            if (args == null || args.Length == 0) return "usage: ct_dump <ClassName> [depth]";
            int depth;
            if (args.Length < 2 || !int.TryParse(args[1], out depth)) depth = 3;
            using (BundleBaker baker = new BundleBaker(ShippedBundlePath(null), "contenttool"))
                return baker.SourceInfo + "\n" + baker.DumpTemplate(args[0], depth);
        }

        /// <summary>
        /// Where the game keeps the bundles a bake clones from: the Addressables folder under
        /// StreamingAssets. Never a developer path.
        /// </summary>
        internal static string ShippedBundlePath(string bundleFileName)
        {
            if (string.IsNullOrEmpty(bundleFileName)) bundleFileName = DefaultSource;
            return Path.Combine(Path.Combine(Path.Combine(
                Application.streamingAssetsPath, "aa"), "StandaloneWindows64"), bundleFileName);
        }

        /// <summary>Magenta everywhere except the centre pixel, which is green.</summary>
        private static byte[] Pixels()
        {
            byte[] px = new byte[Size * Size * 4];
            for (int i = 0; i < px.Length; i += 4) Put(px, i, Magenta);
            Put(px, ((Size / 2) * Size + Size / 2) * 4, Green);
            return px;
        }

        private static void Put(byte[] px, int i, Color32 c)
        {
            px[i] = c.r; px[i + 1] = c.g; px[i + 2] = c.b; px[i + 3] = c.a;
        }

        /// <summary>
        /// The U5 file-level oracle, spelled out rather than read back off the writer: 4 vertices,
        /// the bottom row on bone0 and (when split) the top row on bone1, bind pose 1 the inverse of
        /// a <see cref="BoneY"/> lift, skin data in stream 1 as 2 float weights then 2 UInt32 indices.
        /// Same line the offline round trip asserts, so a passing test and a passing gate mean the
        /// same thing.
        /// </summary>
        private static string WantSkin(string rootName, string materialName, bool split)
        {
            string bone0 = SkinFields.Bone0Name(rootName), bone1 = SkinFields.Bone1Name(rootName);
            uint h0 = SkinFields.BoneHash(bone0), h1 = SkinFields.BoneHash(bone0 + "/" + bone1);
            return "root '" + rootName + "' children=2" +
                   " | skin '" + SkinFields.SkinName(rootName) + "'" +
                   " bones=2 bone0='" + bone0 + "' bone1='" + bone1 + "' rootBone='" + bone0 + "'" +
                   " mesh='" + SkinFields.MeshName(rootName) + "' material='" + materialName + "'" +
                   " | mesh verts=4 bindposes=2 bindpose1.e13=-" + (int)BoneY +
                   " hashes=2:" + h0 + ":" + h1 + " rootHash=" + h0 + " bonesAABB=2" +
                   " weightCh=stream1/off0/fmt0/dim2 indexCh=stream1/off8/fmt10/dim2" +
                   " bytes=" + (SkinFields.SkinOffset(4) +
                                4 * SkinFields.SkinStride(SkinFields.FixtureInfluences)) +
                   " vertex0=1/0->bone0 vertexLast=1/0->bone" + (split ? 1 : 0) +
                   // bone1 hangs off bone0, which is what this bake has always written and what the
                   // author's imported rig now writes too - stated here so a rig that came apart shows
                   // up in the FILE and not only in whatever the engine chooses to report.
                   " | tree '" + bone0 + "'<'" + rootName + "'#1,'" + bone1 + "'<'" + bone0 + "'#0";
        }

        /// <summary>The asset name inside a container key - 'assets/contenttool/x/NAME'.</summary>
        private static string Tail(string key) => key.Substring(key.LastIndexOf('/') + 1);

        /// <summary>y = the frame number, so sampling at t seconds reads 30*t.</summary>
        private static float[] Ramp()
        {
            float[] y = new float[ClipFrames];
            for (int i = 0; i < ClipFrames; i++) y[i] = i;
            return y;
        }

        /// <summary>The same clip with the ramp taken out - every frame at 0.</summary>
        private static float[] Flat() => new float[ClipFrames];

        /// <summary>
        /// The U6 file-level oracle, spelled out rather than read back off the writer: one Transform
        /// position binding on the bone's path hash, 31 dense frames of 3 curves at 30 Hz, an empty
        /// streamed bank that still carries its terminating frame, and an override controller whose
        /// base is an EXTERNAL PPtr. Same line the offline round trip asserts.
        /// </summary>
        private static string WantClip(string clipName, string aocName, uint bonePathHash,
                                       int commonFileId, float lastY)
        {
            return "clip '" + clipName + "' bindings=1 path=" + bonePathHash +
                   " attr=" + ClipFields.AttributePosition + " typeID=4" +
                   " dense=" + ClipFrames + "x" + ClipFields.PositionCurves + "@" + (int)ClipRate +
                   " samples=" + ClipFrames * ClipFields.PositionCurves +
                   " first=(0,0,0) last=(0," + lastY.ToString("0.###", CultureInfo.InvariantCulture) + ",0)" +
                   " streamed=2/0 const=0 delta=" + ClipFields.PositionCurves + " index=200" +
                   " stop=" + ((ClipFrames - 1) / ClipRate).ToString("0.###", CultureInfo.InvariantCulture) +
                   " muscleSize=" + ClipFields.MuscleClipSize(2, ClipFrames * ClipFields.PositionCurves,
                                                              0, ClipFields.PositionCurves) +
                   " legacy=False" +
                   " | aoc '" + aocName + "' controller=fileID" + commonFileId + "/pathID" + MedKitControllerPathId +
                   " overrides=1 original=fileID" + commonFileId + "/pathID" + MedKitClipPathId +
                   " override='" + clipName + "'";
        }

        /// <summary>
        /// Does the CLIP evaluate? Instantiates the prefab and calls AnimationClip.SampleAnimation -
        /// no controller, no state machine - at two DIFFERENT times, so a constant and a rest pose
        /// both read RED. The oracle is the bone's own localPosition.y, a number the rest pose
        /// (<see cref="RestY"/>) cannot produce unless that is exactly what is expected.
        /// Anything unmeasurable reports VOID.
        /// </summary>
        private static int Sample(StringBuilder log, string gate, GameObject prefab, AnimationClip clip,
                                  string boneName, float wantMid, float wantEnd)
        {
            if (prefab == null) { log.AppendLine(gate + " VOID the prefab did not load"); return 0; }
            if (clip == null) { log.AppendLine(gate + " VOID the clip did not load"); return 0; }
            GameObject instance = UnityEngine.Object.Instantiate(prefab);
            try
            {
                Transform bone = Find(instance, boneName);
                if (bone == null)
                {
                    log.AppendLine(gate + " VOID no transform named '" + boneName + "' under the instance");
                    return 0;
                }
                float rest = bone.localPosition.y;
                clip.SampleAnimation(instance, MidTime);
                float mid = bone.localPosition.y;
                clip.SampleAnimation(instance, (ClipFrames - 1) / ClipRate);
                float end = bone.localPosition.y;
                return Check(log, gate, Near(rest, RestY) && Near(mid, wantMid) && Near(end, wantEnd),
                    "'" + boneName + "' rest y=" + rest + ", sampled at " + MidTime + " s y=" + mid +
                    " and at " + ((ClipFrames - 1) / ClipRate) + " s y=" + end +
                    " (expected rest " + RestY + " then " + wantMid + " then " + wantEnd + ")");
            }
            finally { UnityEngine.Object.Destroy(instance); }
        }

        /// <summary>
        /// Does MECANIM accept the clip? Instantiates the prefab, checks its Animator really carries
        /// the baked controller, and advances it by exactly <see cref="MidTime"/>. The oracle is the
        /// same localPosition.y number <see cref="Sample"/> reads, so the two arms cannot disagree by
        /// construction. Anything unmeasurable reports VOID.
        /// </summary>
        private static int Mecanim(StringBuilder log, string gate, GameObject prefab, string boneName, float wantY)
        {
            if (prefab == null) { log.AppendLine(gate + " VOID the prefab did not load"); return 0; }
            GameObject instance = UnityEngine.Object.Instantiate(prefab);
            try
            {
                Animator animator = instance.GetComponent<Animator>();
                if (animator == null) { log.AppendLine(gate + " VOID the instance carries no Animator"); return 0; }
                RuntimeAnimatorController rac = animator.runtimeAnimatorController;
                if (rac == null)
                {
                    log.AppendLine(gate + " VOID the Animator's runtimeAnimatorController is null - " +
                                   "the external base controller did not resolve");
                    return 0;
                }
                Transform bone = Find(instance, boneName);
                if (bone == null)
                {
                    log.AppendLine(gate + " VOID no transform named '" + boneName + "' under the instance");
                    return 0;
                }
                float rest = bone.localPosition.y;
                animator.Update(MidTime);
                float y = bone.localPosition.y;
                return Check(log, gate, Near(rest, RestY) && Near(y, wantY),
                    "controller='" + rac.name + "' (" + rac.GetType().Name + "), '" + boneName +
                    "' rest y=" + rest + ", after Animator.Update(" + MidTime + ") y=" + y +
                    " (expected rest " + RestY + " then " + wantY + ")");
            }
            finally { UnityEngine.Object.Destroy(instance); }
        }

        private static Transform Find(GameObject instance, string name)
        {
            foreach (Transform t in instance.GetComponentsInChildren<Transform>(true))
                if (t.name == name) return t;
            return null;
        }

        private static string ClipNames(AnimationClip[] clips)
        {
            if (clips == null) return "(null)";
            string[] names = new string[clips.Length];
            for (int i = 0; i < clips.Length; i++) names[i] = clips[i] == null ? "(null)" : clips[i].name;
            return string.Join(",", names);
        }

        /// <summary>
        /// Does the skin BIND? Instantiates the baked prefab, snapshots the rest pose with
        /// SkinnedMeshRenderer.BakeMesh, lifts <paramref name="boneName"/> by
        /// <paramref name="lift"/> and snapshots again. The oracle is a NUMBER the rest pose cannot
        /// produce - the highest vertex must land on <paramref name="wantMaxY"/> while the lowest
        /// stays at 0 - so "the whole object moved" and "nothing is weighted" both read RED.
        /// Anything this cannot measure reports VOID rather than PASS.
        /// </summary>
        private static int Deform(StringBuilder log, string gate, GameObject prefab,
                                  string boneName, float lift, float wantMaxY)
        {
            if (prefab == null) { log.AppendLine(gate + " VOID the prefab did not load"); return 0; }
            GameObject instance = UnityEngine.Object.Instantiate(prefab);
            try
            {
                SkinnedMeshRenderer smr = instance.GetComponentInChildren<SkinnedMeshRenderer>(true);
                if (smr == null || smr.sharedMesh == null)
                {
                    log.AppendLine(gate + " VOID the instance carries no SkinnedMeshRenderer with a mesh");
                    return 0;
                }
                Transform bone = null;
                foreach (Transform t in instance.GetComponentsInChildren<Transform>(true))
                    if (t.name == boneName) { bone = t; break; }
                if (bone == null)
                {
                    log.AppendLine(gate + " VOID no bone named '" + boneName + "' under the instance");
                    return 0;
                }

                Mesh snapshot = new Mesh();
                smr.BakeMesh(snapshot);
                float restMin, restMax;
                if (!SpanY(snapshot, out restMin, out restMax))
                {
                    log.AppendLine(gate + " VOID BakeMesh produced no vertices in the rest pose");
                    return 0;
                }
                bone.localPosition = bone.localPosition + new Vector3(0f, lift, 0f);
                smr.BakeMesh(snapshot);
                float minY, maxY;
                if (!SpanY(snapshot, out minY, out maxY))
                {
                    log.AppendLine(gate + " VOID BakeMesh produced no vertices after the lift");
                    return 0;
                }

                return Check(log, gate,
                    Near(restMin, 0f) && Near(restMax, BoneY) && Near(minY, 0f) && Near(maxY, wantMaxY),
                    "rest y=[" + restMin + "," + restMax + "], after lifting '" + boneName + "' by " + lift +
                    " y=[" + minY + "," + maxY + "]" +
                    " (expected rest [0," + BoneY + "] and then [0," + wantMaxY + "])");
            }
            finally { UnityEngine.Object.Destroy(instance); }
        }

        private static bool SpanY(Mesh mesh, out float min, out float max)
        {
            min = 0f; max = 0f;
            Vector3[] v = mesh.vertices;
            if (v == null || v.Length == 0) return false;
            min = max = v[0].y;
            foreach (Vector3 p in v) { if (p.y < min) min = p.y; if (p.y > max) max = p.y; }
            return true;
        }

        internal const string Free = "free", Held = "held by another loader";

        /// <summary>
        /// What state is this shipped archive in? <see cref="Free"/> - we opened it ourselves, so
        /// nobody else holds it and it is closed again at once, leaving the caller's state untouched.
        /// <see cref="Held"/> - Unity refused because the same files are already loaded, which is the
        /// only null that means "someone else has it". Anything else is Unity's own complaint (or a
        /// missing file) verbatim: a probe that failed for THAT reason must not read as "mounted", or
        /// the arms below would assert against an archive that is not there.
        /// </summary>
        private static string ProbeArchive(string bundleFileName)
        {
            string path = ShippedBundlePath(bundleFileName);
            if (!File.Exists(path)) return "no such file: " + path;
            string said = null;
            Application.LogCallback tap = (msg, stack, type) =>
            {
                if (said == null && (type == LogType.Error || type == LogType.Exception)) said = msg;
            };
            Application.logMessageReceived += tap;
            AssetBundle probe;
            try { probe = AssetBundle.LoadFromFile(path); }
            finally { Application.logMessageReceived -= tap; }
            if (probe != null) { probe.Unload(false); return Free; }
            // Unity names this one case in so many words, and it is the only failure that leaves the
            // archive MOUNTED and every external in it resolvable.
            if (said != null && said.Contains("already loaded")) return Held;
            return said == null ? "LoadFromFile returned null and Unity logged nothing" : said;
        }

        private static int Check(StringBuilder log, string gate, bool ok, string detail)
        {
            log.AppendLine(gate + (ok ? " PASS " : " FAIL ") + detail);
            return ok ? 0 : 1;
        }

        private static bool Same(byte[] a, byte[] b)
        {
            if (a == null || b == null || a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
            return true;
        }

        /// <summary>
        /// The oracle for every external-PPtr arm. A Material whose m_Shader did not resolve reports
        /// <see cref="ErrorShaderName"/>, never null, so a null test would read a broken reference as
        /// a resolved one - which is exactly how U3d/U3e's controls passed vacuously.
        /// </summary>
        private static string ShaderNameOf(Material m) =>
            m == null ? "(no material)" : m.shader == null ? "(no shader)" : m.shader.name;

        private static bool Near(float a, float b) => Mathf.Abs(a - b) < 1e-4f;

        private static bool Same(Color32 a, Color32 b) => a.r == b.r && a.g == b.g && a.b == b.b && a.a == b.a;

        private static string Str(Color32 c) => c.r + "," + c.g + "," + c.b + "," + c.a;
    }
}


