using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Morgott.ContentTool.Import;
using Morgott.ContentTool.Project;
using Morgott.ContentTool.Tactical;
using Morgott.ContentTool.Wwise;
using UnityEngine;

namespace Morgott.ContentTool.Bake
{
    /// <summary>
    /// Bakes a content project into its release bundle: import (Project) -> serialize (Bake), the
    /// two halves of FINAL-PLAN 0.3 meeting exactly once, here. Then it loads the file it just
    /// wrote and checks the content came back - a bake that cannot be read is not a bake
    /// (METHODOLOGY, FINAL-PLAN 28).
    ///
    /// ponytail: no incremental build, no Bake Release DLL copy, no hot reload - Tasks 14/15/18
    /// own those, and a project this size rewrites in under a second.
    /// </summary>
    internal static class ProjectBake
    {
        /// <summary>
        /// Stamp in the generated sample's ppcontent.json. BUMP IT whenever the sample gains
        /// anything a gate depends on: ct_project rewrites the sample when the stamp differs, so an
        /// older copy on disk cannot silently skip the new arm. Guarding on the presence of a field
        /// instead let the material entry go unwritten and P3 never ran (ct_project 14:28).
        /// </summary>
        internal const string SampleStamp = "\"sample\": 18";

        /// <summary>
        /// How far past its declared length an .mp3 may decode.
        ///
        /// It existed because UNITY'S decoder padded: the 0,5 s probe declares 24192 sample frames
        /// (21 x 1152 on disk, Xing header frame excluded) and the engine handed back 27648. The
        /// in-house NLayer decode of the same probe lands on 24192 EXACTLY - measured, gate A7 - so
        /// on everything this repo has ever run, the slack is now unused.
        ///
        /// Kept anyway, as headroom for an encoder nobody here has a fixture for (a LAME gapless
        /// header NLayer might trim to, say). It only ever widens an arm, never a decode.
        /// ponytail: a tolerance on ONE fixture's worth of evidence, not a model of the encoder's
        /// delay tables; if a second .mp3 fixture ever also lands exact, delete it.
        /// </summary>
        private const int Mp3PaddingFrames = 4 * 1152;


        internal static string Run(string projectRoot)
        {
            StringBuilder log = new StringBuilder();
            int failures = 0;

            ContentProject p = ContentProject.Load(projectRoot);
            // The replacement count is ALWAYS printed, including 0. A declared "replace" that parses
            // to nothing used to produce no output at all (ct_project 13:51) - the run looked clean
            // and the whole feature was simply absent.
            log.AppendLine("project '" + p.Id + "' at " + p.Root + ": " + p.Textures.Count +
                           " texture(s), " + p.Meshes.Count + " mesh(es), " + p.Models.Count + " model(s), " +
                           p.Videos.Count + " video(s), " +
                           p.Audio.Count + " sound(s), " + p.Replace.Count + " replacement(s)");
            // A source the importer could not use is REPORTED and skipped, never fatal and never
            // silent: before this, one .ogg the tool could not read aborted Load and took every
            // texture, mesh and model in the project down with it (the same defect 9a3747b fixed in
            // the clip path). Printed ahead of the "nothing to bake" return, or a project holding
            // only a bad sound would say nothing about it.
            foreach (string refusal in p.SourceRefusals) log.AppendLine("SOURCE SKIPPED: " + refusal);
            // PATCHING SHIPPED BUNDLES IS ITS OWN WORK, and it comes FIRST. The guard below used to
            // stand ahead of it, so a project whose only declaration is a "material" or a "clip" row
            // - no .png, no .glb, no .wav of its own - returned "nothing to bake" and produced no
            // patched copy at all; route vii then refused with "holds no .bundle". Measured on
            // demos\MaterialTweak, 2026-08-28. Patch() writes into PatchedDir() and needs nothing
            // from the mod's own bundle, so it is simply in the wrong order, not conditional on it.
            if (p.Replace.Count > 0) failures += Patch(p, log);
            // WHAT WAS PATCHED, not how many rows were declared. A "video" row is a replacement that
            // needs no patched bundle at all - Bundles(p) skips it, because the clip is a loose file
            // served live by ct_video - so keying the success line on p.Replace.Count made a
            // video-only project (demos\IntroVideo) report copies as its whole output while Patch
            // had written none, and route vii then refused it for holding no .bundle.
            int patchedBundles = Bundles(p).Count;
            if (p.Textures.Count == 0 && p.Audio.Count == 0 && p.Models.Count == 0)
                return log.Append(p.Replace.Count == 0
                    ? "nothing to bake - put .png/.jpg under Content\\Textures\\, " +
                      ".glb under Content\\Models\\ or .wav under Content\\Audio\\"
                    : failures != 0
                        ? "ct_project: " + failures + " FAILURE(S)"
                        : patchedBundles > 0
                            ? "ct_project: ALL PASS - this project has no bundle of its own; the patched " +
                              "copy(ies) above are the whole output"
                            : "ct_project: ALL PASS - nothing needed patching: none of this project's " +
                              p.Replace.Count + " replacement(s) names a shipped bundle, so no copy was " +
                              "written - the video row(s) above are served live by ct_video").ToString();
            failures += ClipNamesDeclared(p, log);
            failures += CreatureScaffold(p, log);

            string outPath = Path.Combine(Path.Combine(p.Root, "Dist"), p.BundleName);
            Directory.CreateDirectory(Path.GetDirectoryName(outPath));
            if (File.Exists(outPath)) { File.Delete(outPath); log.AppendLine("deleted stale " + outPath); }

            List<string> texKeys = new List<string>();
            List<string> modelKeys = new List<string>();
            List<string> clipKeys = new List<string>();
            AudioBake.Result audio = null;
            // Non-zero once a model got an AnimatorOverrideController: its base controller is an
            // EXTERNAL PPtr, which resolves only while that archive is mounted (U3d's precondition),
            // so the engine half has to mount _common itself rather than wait on a clock.
            int commonExt = 0;
            using (BundleBaker baker = new BundleBaker(BakeSelfCheck.ShippedBundlePath(null), p.Id))
            {
                foreach (ImportedTexture t in p.Textures)
                    texKeys.Add(baker.AddTexture2D("textures/" + t.Name, t.Width, t.Height, t.Rgba32));

                // Every model gets its own Material. A renderer without one draws nothing, so this is
                // not decoration - and the shader is the builtin Standard through an EXTERNAL PPtr
                // (U3d's arrangement), with the project's own same-named texture in _MainTex when it
                // has one and the .glb's own base colour in _Color either way. That colour is not
                // decoration either: a material carrying NEITHER draws Standard's default, opaque
                // white - which is what the imported spider showed, and is indistinguishable on screen
                // from a material that never loaded. The SHADING of that material is the one part of a
                // model this gate cannot answer: an external shader PPtr only resolves while the
                // builtin bundle is mounted (U4's note), and deformation - what these arms measure -
                // does not depend on it.
                int shaderExt = baker.ExternalIdOf(BakeSelfCheck.BuiltinShaderCab);
                foreach (ImportedModel model in p.Models)
                {
                    // ONE MATERIAL PER SUBMESH. A model that arrived as several meshes over several
                    // materials keeps them (MeshMerge groups the pieces by material and the mesh
                    // carries one submesh each), so the renderer needs one material per submesh or
                    // two of the gun's three surfaces would take the first one's paint.
                    //
                    // WHICH TEXTURE EACH ONE GETS. `Content\Textures\<model>_<material>.png` binds to
                    // that material alone; failing that, `<model>.png` binds to every one of them,
                    // which is the single-texture project that has always worked. The .glb's own
                    // embedded images are still not used - see IgnoredImages - so a multi-material
                    // model with no per-material .png draws them all with one texture, and the line
                    // below SAYS so rather than leaving it to be discovered on screen.
                    string[] slots = model.Baked.MaterialNames;
                    if (slots == null || slots.Length == 0) slots = new[] { model.Name };
                    float[] srgb = MaterialFields.Srgb(model.Baked.BaseColor);
                    Dictionary<string, float[]> colors = srgb == null ? null
                        : new Dictionary<string, float[]> { { MaterialFields.ColorProperty, srgb } };

                    string[] matKeys = new string[slots.Length];
                    for (int s = 0; s < slots.Length; s++)
                    {
                        string perSlot = model.Name + "_" + slots[s];
                        Dictionary<string, string> tex = null;
                        string bound = null;
                        for (int i = 0; i < p.Textures.Count; i++)
                            if (string.Equals(p.Textures[i].Name, perSlot, StringComparison.OrdinalIgnoreCase))
                            { tex = new Dictionary<string, string> { { "_MainTex", texKeys[i] } }; bound = perSlot; }
                        if (tex == null)
                            for (int i = 0; i < p.Textures.Count; i++)
                                if (string.Equals(p.Textures[i].Name, model.Name, StringComparison.OrdinalIgnoreCase))
                                { tex = new Dictionary<string, string> { { "_MainTex", texKeys[i] } }; bound = model.Name; }

                        // LAST, so an author-supplied Content\Textures\*.png always WINS: the file on
                        // disk is the one the modder can edit, and letting the .glb's own image
                        // override it would make a texture swap impossible without re-exporting the
                        // model. The embedded image is the fallback that means a downloaded model
                        // just works - without it every imported model bakes with no _MainTex and
                        // renders PURE WHITE, which is what the spider did.
                        byte[][] embedded = model.Baked.MaterialImages;
                        if (tex == null && embedded != null && s < embedded.Length && embedded[s] != null)
                        {
                            string why;
                            // Indexed for the same reason the material asset is: "Material" and
                            // "material" are two different slots in ar-181.glb and collapse onto one
                            // texture path otherwise.
                            ImportedTexture decoded = DecodeEmbedded(
                                embedded[s],
                                model.Name + "_" + s.ToString(CultureInfo.InvariantCulture) + "_" + Safe(slots[s]),
                                out why);
                            if (decoded == null)
                                log.AppendLine("texture REFUSED '" + model.Name + "' material '" + slots[s] +
                                               "' carries a " + embedded[s].Length + " B embedded image that " +
                                               "could not be decoded (" + why + "); it would render white. " +
                                               "Supply Content\\Textures\\" + model.Name + "_" + slots[s] + ".png instead.");
                            else
                            {
                                string key = baker.AddTexture2D("textures/" + decoded.Name,
                                                                decoded.Width, decoded.Height, decoded.Rgba32);
                                tex = new Dictionary<string, string> { { "_MainTex", key } };
                                bound = "the .glb itself, " + decoded.Width + "x" + decoded.Height + " (no file needed)#";
                            }
                        }

                        // The asset name must be unique per material, and the SUBMESH INDEX is what
                        // guarantees that - not the material's name. MEASURED on ar-181.glb, which
                        // carries slots called "Material" and "material": names that differ only by
                        // case collapse onto one asset path and the bake threw "duplicate asset
                        // name". A one-material model keeps the exact name it has always had, so
                        // nothing that reads it by name moves.
                        string assetName = slots.Length == 1
                            ? "materials/" + model.Name
                            : "materials/" + model.Name + "_" + s.ToString(CultureInfo.InvariantCulture) +
                              "_" + Safe(slots[s]);
                        // GLOW COMES FROM THE MODEL, not from a manifest key: if the author exported
                        // an emissiveFactor, this material lights up. _EmissionColor alone does
                        // nothing - the Standard shader only reads it when _EMISSION is in
                        // m_ShaderKeywords, which is the silent failure MaterialFields documents.
                        float[][] glow = model.Baked.MaterialEmissive;
                        float[] emissive = glow != null && s < glow.Length ? glow[s] : null;
                        // An EMPTY array means the material asked to glow through an emissive
                        // TEXTURE this bake cannot bind. Reported by name rather than silently
                        // honoured, because honouring the factor alone paints the whole model white.
                        if (emissive != null && emissive.Length == 0)
                        {
                            log.AppendLine("material '" + slots[s] + "' declares an emissive TEXTURE, " +
                                           "which this bake cannot bind (it binds _MainTex only), so it " +
                                           "is NOT lit - using the factor alone would make the whole " +
                                           "surface glow uniformly white");
                            emissive = null;
                        }
                        Dictionary<string, float[]> withGlow = colors;
                        if (emissive != null)
                        {
                            withGlow = colors == null
                                ? new Dictionary<string, float[]>()
                                : new Dictionary<string, float[]>(colors);
                            withGlow[MaterialFields.EmissionColorProperty] = emissive;
                        }
                        matKeys[s] = baker.AddMaterial(assetName, tex, null,
                                                       shaderExt, BakeSelfCheck.StandardShaderPathId, withGlow,
                                                       emissive == null ? null : MaterialFields.EmissionKeyword);
                        log.AppendLine("material '" + slots[s] + "' -> " + Tail(matKeys[s]) +
                                       " tex=" + (bound == null
                                           ? "(none - no Content\\Textures\\" + perSlot + ".png or " + model.Name + ".png)"
                                           : "_MainTex from " + (bound.EndsWith("#") ? bound.TrimEnd('#') : bound + ".png")) +
                                       " color=" + (srgb == null ? "(none - the .glb declares no material)"
                                           : ModelBuild.F(srgb[0]) + "," + ModelBuild.F(srgb[1]) + "," + ModelBuild.F(srgb[2])));
                    }
                    string mat = matKeys[0];
                    if (slots.Length > 1)
                        log.AppendLine("model '" + model.Name + "' kept " + slots.Length +
                                       " material(s) as " + slots.Length + " submesh(es) [" +
                                       string.Join(", ", slots) + "]");
                    // Clip and controller BEFORE the model: the model's Animator points at the
                    // controller, and the controller hands Mecanim the clip.
                    // U9: a model whose own .glb carries animation gets THOSE baked, for every project
                    // and not only the sample - that is the feature. U7's synthetic lift clip is what a
                    // model with no animation of its own gets, and only in the sample project.
                    string clipKey = ImportedClips(baker, p, model, log) ?? LiftClip(baker, p, model, log);
                    clipKeys.Add(clipKey);
                    string aocKey = null;
                    if (clipKey != null)
                    {
                        if (commonExt == 0) commonExt = baker.AddExternal(BakeSelfCheck.CommonCab);
                        aocKey = baker.AddAnimatorOverrideController("controllers/" + model.Name + AocSuffix,
                            commonExt, BakeSelfCheck.MedKitControllerPathId,
                            BakeSelfCheck.MedKitClipPathId, clipKey);
                    }
                    modelKeys.Add(baker.AddModel("models/" + model.Name, model.Baked, matKeys, aocKey));
                    log.AppendLine("model '" + model.Name + "' -> " + modelKeys[modelKeys.Count - 1] +
                                   " " + model.Baked.Describe() +
                                   (aocKey == null ? "" : " animator -> '" + Tail(aocKey) + "'"));
                }

                failures += AudioControls(log);
                failures += AudioIdentity(p, log);
                if (p.Audio.Count > 0)
                {
                    List<AudioBake.Sound> sounds = new List<AudioBake.Sound>();
                    foreach (ImportedAudio a in p.Audio)
                        sounds.Add(new AudioBake.Sound { Name = a.Name, Wem = a.Wem, MediaId = a.MediaId, Stream = a.Stream });
                    audio = AudioBake.Package(baker, p.Id.ToLowerInvariant().Replace('.', '_'), sounds);
                }

                // The bundle is renamed to the project id, so an already-loaded bundle cannot
                // masquerade as this one.
                baker.Write(outPath, p.Id.ToLowerInvariant().Replace('.', '_'));
                log.AppendLine("WROTE " + outPath + " " + new FileInfo(outPath).Length + " B as " + baker.WrittenIdentity);
            }

            // Read off the written FILE before the engine opens it - Unity holds a bundle open after
            // LoadFromFile, and a second reader on the same path is asking for trouble (BakeSelfCheck
            // does the same, for the same reason).
            failures += ModelsWrote(p, outPath, log);

            // Mounted BEFORE our own bundle is opened, so the model's Animator deserializes against a
            // live archive - the same order BakeSelfCheck uses for its shader externals.
            AssetBundle common = null;
            if (commonExt != 0)
            {
                common = AssetBundle.LoadFromFile(BakeSelfCheck.ShippedBundlePath(BakeSelfCheck.CommonBundle));
                log.AppendLine("mount " + BakeSelfCheck.CommonBundle + ": " + (common == null ? "FAILED" : "ok"));
            }

            // The mod that SHIPS this project may already have this bundle open - its own DLL loads
            // it when the player enables the mod. Unity refuses a second archive with the same CAB,
            // so the bake wrote a correct file and then failed to read it back (build b1720c7f).
            string released = BundleResidency.Release(BundleResidency.Identity(p.Id));
            if (released != null) log.AppendLine(released);

            AssetBundle bundle = AssetBundle.LoadFromFile(outPath);
            if (bundle == null)
            {
                if (common != null) common.Unload(false);
                return log.Append("FAIL AssetBundle.LoadFromFile returned null - something still holds " +
                                  "a bundle named '" + BundleResidency.Identity(p.Id) + "'. Restart, or " +
                                  "switch that mod off in the mod manager, then bake again.").ToString();
            }
            try
            {
                for (int i = 0; i < modelKeys.Count; i++) failures += Model(bundle, modelKeys[i], p.Models[i], log);
                for (int i = 0; i < modelKeys.Count; i++)
                    failures += Animated(log, p, p.Models[i], bundle, modelKeys[i], clipKeys[i]);

                for (int i = 0; i < texKeys.Count; i++)
                {
                    Texture2D tex = bundle.LoadAsset<Texture2D>(texKeys[i]);
                    ImportedTexture src = p.Textures[i];
                    // Pixel-compared, not just non-null: a Texture2D that loads but reads zeroes is
                    // the exact failure a missing m_StreamData reset produces.
                    bool ok = tex != null && tex.width == src.Width && tex.height == src.Height && SamePixels(tex, src);
                    failures += Check(log, "TEX", ok, texKeys[i] + " -> " +
                        (tex == null ? "null" : tex.width + "x" + tex.height + " " + tex.format + " px[0,0]=" + Str(tex.GetPixels32()[0])));
                }

                if (audio != null)
                {
                    if (audio.StreamCount > 0)
                    {
                        int written;
                        log.AppendLine("extract: " + StreamCache.Extract(bundle, audio.ManifestAsset, out written));
                    }
                    TextAsset bank = bundle.LoadAsset<TextAsset>(audio.BankAsset);
                    uint loadedId;
                    string load = bank == null ? "bank asset missing" : AudioProbe.LoadBank(bank.bytes, audio.BankId, out loadedId);
                    failures += Check(log, "BANK", load.Contains("LoadBankMemoryCopy: AK_Success"),
                                      audio.BankAsset + " -> " + load);
                    AkSoundEngine.UnloadBank(audio.BankId, IntPtr.Zero);
                }
            }
            finally
            {
                bundle.Unload(true);
                // false: the game pulls _common in through Addressables later, and destroying objects
                // it is about to reference is not this gate's business. Unmounting the ARCHIVE is what
                // matters - leaving it open collides with that load.
                if (common != null) common.Unload(false);
            }

            return log.Append(failures == 0
                ? "ct_project: ALL PASS - " + outPath
                : "ct_project: " + failures + " FAILURE(S)").ToString();
        }

        /// <summary>
        /// How far the model gate lifts a bone. Neither 0 nor 1 nor any coordinate the sample rig
        /// carries, so a number that comes back scaled by a weight is unmistakable.
        /// </summary>
        private const float ModelLift = 10f;

        /// <summary>
        /// U7. The tool's own sample project - the only one that gets the gate's lift clip baked into
        /// its bundle, because a clip that lifts one bone is scaffolding and an author's release
        /// bundle must not carry it.
        /// </summary>
        private const string SampleId = "morgott.sample";

        /// <summary>
        /// U7's clip: THREE frames one second apart, holding the lift on the last two, and every arm
        /// samples at <see cref="LiftTime"/> = the SECOND frame. Three, not two, for two independent
        /// reasons: the sample lands exactly ON a frame (so no interpolation rule is assumed), and it
        /// lands strictly INSIDE the clip (so a looping state machine cannot have wrapped back to
        /// frame 0 and handed back the rest pose, which is indistinguishable from a dead binding).
        /// The lift is the SAME <see cref="ModelLift"/> the M1 arms move the bone by HAND, so both
        /// predict the same vertices from the same weights.
        /// </summary>
        private const string LiftClipSuffix = "_lift";
        private const string AocSuffix = "_aoc";
        private const float LiftClipRate = 1f, LiftTime = 1f;

        /// <summary>
        /// M1-wrote - what the written FILE holds for every added model, with no engine in the loop.
        /// Not a duplicate of the engine arm: U4 MEASURED that a half-written hierarchy is invisible
        /// to Unity (a child whose m_Father was zeroed still reported childCount=1), so the file-level
        /// arm is the only one that can see it. Every expectation is derived from the IMPORT, so it is
        /// the importer and the serializer being compared, not this file against itself.
        /// </summary>
        private static int ModelsWrote(ContentProject p, string outPath, StringBuilder log)
        {
            int failures = 0;
            foreach (ImportedModel m in p.Models)
            {
                BakedSkin s = m.Baked;
                if (!s.Rigged)
                {
                    string flat = BundleBaker.ReadPrefabSummary(outPath, m.Name);
                    failures += Check(log, "M1-wrote", flat.Contains("mesh='" + SkinFields.MeshName(m.Name) + "'"),
                        "static model '" + m.Name + "' in the file -> " + flat);
                    continue;
                }

                int bones = s.BoneNames.Length, verts = s.Mesh.VertexCount;
                // Every hash is the CRC of the bone's PATH under the model root, so a parented bone
                // carries its parent's name in it - the same paths an animation curve binds by (U6).
                string hashes = bones.ToString(CultureInfo.InvariantCulture);
                for (int b = 0; b < bones; b++) hashes += ":" + SkinFields.BoneHash(s.BonePath(b));
                // The written hierarchy, both halves, derived from the FILE's own parent links.
                string tree = "";
                int rootBones = 0;
                for (int b = 0; b < bones; b++)
                {
                    int kids = 0;
                    for (int c = 0; c < bones; c++) if (s.BoneParents[c] == b) kids++;
                    if (s.BoneParents[b] < 0) rootBones++;
                    tree += (b == 0 ? "" : ",") + "'" + s.BoneNames[b] + "'<'" +
                            (s.BoneParents[b] < 0 ? m.Name : s.BoneNames[s.BoneParents[b]]) + "'#" + kids;
                }
                List<string> want = new List<string>
                {
                    "| tree " + tree,
                    "root '" + m.Name + "' children=" + (rootBones + 1),
                    "bones=" + bones,
                    "bone0='" + s.BoneNames[0] + "'",
                    "rootBone='" + s.BoneNames[0] + "'",
                    "mesh='" + SkinFields.MeshName(m.Name) + "'",
                    "verts=" + verts,
                    "bindposes=" + bones,
                    "hashes=" + hashes,
                    "rootHash=" + SkinFields.BoneHash(s.BonePath(0)),
                    "bonesAABB=" + bones,
                    SkinFields.OurLayout,
                    "bytes=" + (SkinFields.SkinOffset(verts) + verts * SkinFields.SkinStride),
                    "vertex0=" + Influence(s, 0)
                };
                string got = BundleBaker.ReadSkinSummary(outPath, m.Name);
                List<string> missing = new List<string>();
                foreach (string w in want) if (!got.Contains(w)) missing.Add(w);
                failures += Check(log, "M1-wrote", missing.Count == 0,
                    "model '" + m.Name + "' in the file IS the .glb that was imported -> " + got +
                    (missing.Count == 0 ? "" : " (MISSING " + string.Join(" | ", missing.ToArray()) + ")"));
                failures += ClipWrote(p, outPath, m, log);
            }
            return failures;
        }

        /// <summary>One vertex's influences the way <c>SkinFields.Summary</c> prints them.</summary>
        private static string Influence(BakedSkin s, int vertex)
        {
            int at = vertex * BakedSkin.Influences;
            return ModelBuild.F(s.Weights[at]) + "/" + ModelBuild.F(s.Weights[at + 1]) +
                   "->bone" + s.Bones[at];
        }

        /// <summary>
        /// M1 - the EFFECT. The model is instantiated and its skin BAKED before and after one bone is
        /// lifted, so what is asserted is where every vertex ENDED UP, which a rest pose cannot
        /// produce. The expectation is not a constant: Unity skins with sum(weight * boneMatrix), so
        /// a bone that DISPLACED by D must move each vertex by exactly (that bone's weight) * D - and
        /// those weights come out of the author's own file, while D is measured off the bone in the
        /// mesh's own space rather than assumed to be the lift. A bake that dropped them leaves the mesh
        /// at rest, and a bake that synthesised one full-weight influence per vertex (the .obj
        /// ceiling) cannot produce a FRACTION of L on a shared vertex.
        /// </summary>
        private static int Model(AssetBundle bundle, string key, ImportedModel m, StringBuilder log)
        {
            GameObject prefab = bundle.LoadAsset<GameObject>(key);
            if (prefab == null) return Check(log, "M1", false, "LoadAsset<GameObject>('" + key + "') returned null");

            GameObject go = UnityEngine.Object.Instantiate(prefab);
            try
            {
                if (!m.Baked.Rigged)
                {
                    MeshFilter mf = go.GetComponentInChildren<MeshFilter>(true);
                    Mesh got = mf == null ? null : mf.sharedMesh;
                    return Check(log, "M1", got != null && got.vertexCount == m.Baked.Mesh.VertexCount,
                        "static model '" + m.Name + "' loads with its own Mesh '" +
                        (got == null ? "(none)" : got.name) + "' vertexCount=" +
                        (got == null ? -1 : got.vertexCount) + " (imported " + m.Baked.Mesh.VertexCount + ")");
                }

                SkinnedMeshRenderer smr = go.GetComponentInChildren<SkinnedMeshRenderer>(true);
                if (smr == null || smr.bones == null || smr.bones.Length != m.Baked.BoneNames.Length)
                    return Check(log, "M1", false, "model '" + m.Name + "' loaded with " +
                        (smr == null ? "no SkinnedMeshRenderer" :
                         (smr.bones == null ? 0 : smr.bones.Length) + " bone(s), not " + m.Baked.BoneNames.Length));

                int failures = Rest(log, m, smr);
                failures += Deform(log, "M1", m, smr, m.Baked.BoneNames.Length > 1 ? 1 : 0);
                // The control moves a DIFFERENT bone of the same rig, and asserts ITS own predicted
                // vertices - a second positive identity, not a "nothing happened". A rig with one bone
                // has no second bone to move, which is a question this cannot ask rather than one it
                // can answer.
                if (m.Baked.BoneNames.Length > 1) failures += Deform(log, "M1-ctl-bone0", m, smr, 0);
                else log.AppendLine("M1-ctl-bone0 VOID '" + m.Name + "' has a single bone, so there is no second one to move");
                return failures + Parent(log, m, smr);
            }
            finally { UnityEngine.Object.Destroy(go); }
        }

        /// <summary>
        /// M1-parent - what a FLAT rig cannot do. One bone that the file says CARRIES another is
        /// lifted, and the vertices asserted are the ones with no weight of their own on it: they
        /// belong to its children, so they may only move because the bone took its children with it.
        /// The expectation is still the file's own arithmetic - the summed weight of everything that
        /// bone carries, times the lift - and it is a positive vector per vertex, never "something
        /// changed". A rig the file leaves flat has no such vertex, which says VOID.
        /// </summary>
        private static int Parent(StringBuilder log, ImportedModel m, SkinnedMeshRenderer smr)
        {
            BakedSkin s = m.Baked;
            for (int b = 0; b < s.BoneNames.Length; b++)
            {
                List<int> carried = new List<int>();
                for (int v = 0; v < s.Mesh.VertexCount; v++)
                    if (s.WeightOf(v, b) <= 0f && s.CarriedWeight(v, b) > 0f) carried.Add(v);
                if (carried.Count == 0) continue;

                string which = "";
                foreach (int v in carried) which += (which.Length == 0 ? "" : ",") + v;
                return Deform(log, "M1-parent", m, smr, b, "vertices [" + which +
                              "] have NO weight of their own on '" + s.BoneNames[b] +
                              "' and move only because it carries " + Children(s, b) + ": ");
            }
            log.AppendLine("M1-parent VOID '" + m.Name + "' has no bone carrying a vertex weighted to " +
                           "another bone, so there is nothing a parent link could move");
            return 0;
        }

        /// <summary>The bones a bone carries, by name, for the arm's own log line.</summary>
        private static string Children(BakedSkin s, int bone)
        {
            string kids = "";
            for (int b = 0; b < s.BoneNames.Length; b++)
                if (b != bone && s.Carries(bone, b)) kids += (kids.Length == 0 ? "" : ",") + "'" + s.BoneNames[b] + "'";
            return kids;
        }

        /// <summary>
        /// M1-rest - the skin at rest IS the geometry the author's file carries. Unity skins with
        /// sum(weight * boneWorld * bindPose), so this only comes out when every bone lands exactly
        /// where its bind pose says: writing a bone's MODEL-space rest into a Transform that now has a
        /// parent stacks the parent's transform on top of it, and the model is deformed before it ever
        /// moves. That is invisible to any arm whose baseline is the rest bake itself.
        /// </summary>
        private static int Rest(StringBuilder log, ImportedModel m, SkinnedMeshRenderer smr)
        {
            Mesh scratch = new Mesh();
            try
            {
                smr.BakeMesh(scratch);
                Vector3[] rest = scratch.vertices;
                bool ok = rest.Length == m.Baked.Mesh.VertexCount;
                string want = "";
                for (int i = 0; i < rest.Length; i++)
                {
                    Vector3 file = Vertex(m.Baked, i);
                    if (i < 8) want += (i == 0 ? "" : ",") + ModelBuild.F(file.y);
                    if ((rest[i] - file).magnitude > 1e-3f) ok = false;
                }
                return Check(log, "M1-rest", ok, "'" + m.Name + "' at rest bakes to y=[" + Ys(rest) +
                    "], the .glb's own vertices [" + want + "]");
            }
            finally { UnityEngine.Object.Destroy(scratch); }
        }

        /// <summary>One imported vertex, out of the buffer the bake wrote it from.</summary>
        private static Vector3 Vertex(BakedSkin s, int i)
        {
            int at = i * BakedMesh.Stride;
            return new Vector3(BitConverter.ToSingle(s.Mesh.VertexData, at),
                               BitConverter.ToSingle(s.Mesh.VertexData, at + 4),
                               BitConverter.ToSingle(s.Mesh.VertexData, at + 8));
        }

        private static int Deform(StringBuilder log, string gate, ImportedModel m,
                                  SkinnedMeshRenderer smr, int boneIndex, string why = "")
        {
            Mesh scratch = new Mesh();
            try
            {
                smr.BakeMesh(scratch);
                Vector3[] rest = scratch.vertices;
                Transform bone = smr.bones[boneIndex];
                Vector3 was = bone.localPosition;
                // The lift is LOCAL to the bone's PARENT; BakeMesh reports vertices in the RENDERER's
                // space. Those two are the same space only for a bone whose ancestors are pure
                // translations, which is what the sample rig happens to be and what an author's file
                // is not: the spider's 'Root' rests rotated a quarter turn about X and scaled 33.69,
                // so a local +10 y on its child 'Body' is +337 z in the mesh's space and leaves y
                // untouched. So the displacement is MEASURED where the vertices are measured, and the
                // whole vector is asserted - predicting +10 y (and looking only at y) is what made
                // this arm read a correct bake as RED and could have read a dead one as green.
                Vector3 from = smr.transform.InverseTransformPoint(bone.position);
                bone.localPosition = was + new Vector3(0f, ModelLift, 0f);
                Vector3 delta = smr.transform.InverseTransformPoint(bone.position) - from;
                smr.BakeMesh(scratch);
                Vector3[] moved = scratch.vertices;
                bone.localPosition = was;

                // Skinning is float arithmetic on coordinates as large as the displacement itself, so
                // the tolerance scales with it - and stays a thousandth of the smallest movement any
                // arm here asserts.
                float tol = 1e-3f * Math.Max(1f, delta.magnitude);
                bool ok = rest.Length == m.Baked.Mesh.VertexCount && moved.Length == rest.Length;
                string predicted = "";
                int movers = 0;
                float worst = 0f;
                // The whole prediction is built even once a vertex has missed, so a FAIL line reports
                // what was expected instead of stopping at the first disagreement.
                for (int i = 0; i < rest.Length && i < moved.Length; i++)
                {
                    // CarriedWeight, not WeightOf: lifting a bone lifts every bone it carries, so a
                    // vertex follows the summed weight of that whole subtree. On a flat rig the two are
                    // the same number, which is exactly why a flat bake used to pass this arm.
                    Vector3 want = rest[i] + m.Baked.CarriedWeight(i, boneIndex) * delta;
                    if ((want - rest[i]).magnitude > tol) movers++;
                    float off = (moved[i] - want).magnitude;
                    if (off > worst) worst = off;
                    if (i < 8) predicted += (i == 0 ? "" : ",") + ModelBuild.F(want.y);
                    if (off > tol) ok = false;
                }
                // A fixture whose own weights predict that NOTHING moves cannot fail, so it measures
                // nothing: a bake that dropped every weight would satisfy it perfectly. The arm asserts
                // its own fixture is non-degenerate before it asserts anything about the bake.
                if (movers == 0) ok = false;
                return Check(log, gate, ok, why + "'" + m.Name + "' lifting bone '" + m.Baked.BoneNames[boneIndex] +
                    "' by " + ModelBuild.F(ModelLift) + " moves it by (" + ModelBuild.F(delta.x) + "," +
                    ModelBuild.F(delta.y) + "," + ModelBuild.F(delta.z) + ") in the mesh's own space: rest y=[" +
                    Ys(rest) + "] -> y=[" + Ys(moved) + "], and the file's own weights predict [" + predicted +
                    "]; " + movers + " of " + rest.Length + " vertices must move, worst off by " +
                    ModelBuild.F(worst) + " (tolerance " + ModelBuild.F(tol) + ")");
            }
            finally { UnityEngine.Object.Destroy(scratch); }
        }

        /// <summary>The bone U7's clip drives and the M1 arms lift by hand - index 1 where the rig has
        /// one, so the bone is CARRIED and its own path is not just its name.</summary>
        private static int LiftBone(BakedSkin s) { return s.BoneNames.Length > 1 ? 1 : 0; }

        /// <summary>
        /// U7, the bake half of the join: a two-frame AnimationClip that lifts one of the IMPORTED
        /// rig's own bones by <see cref="ModelLift"/>, bound by that bone's path UNDER THE MODEL ROOT.
        /// That path is the one thing the two proven halves have to agree on and nothing yet made them:
        /// row 19 writes it into the mesh's m_BoneNameHashes, and U6 measured that a curve is addressed
        /// by CRC-32 of exactly that spelling, so a disagreement drives nothing and says nothing.
        ///
        /// ponytail: the clip is SYNTHESISED here, not imported. Reading animation out of a .glb is an
        /// importer (glTF samplers, its own keyframe format), and the gate needs a clip, not that
        /// importer - add it when a project has one to declare.
        /// </summary>
        /// <summary>
        /// U9, the bake half: every clip the model's own .glb carries, written into the bundle beside
        /// it. The bones are addressed by <c>BakedSkin.BonePath</c> - the SAME spelling
        /// <see cref="SkinFields"/> hashed into the mesh's m_BoneNameHashes - which is why
        /// <see cref="ClipFields.Bindings"/> takes the skin rather than a name list: the join is by
        /// construction, and a curve bound to a path the rig does not spell drives nothing silently.
        /// </summary>
        /// <returns>the asset key of the clip the AOC hands Mecanim - ppcontent.json's <c>"play"</c>
        /// when it names one of this model's clips, else the first bakeable one - or null when the file
        /// carries no animation at all.</returns>
        /// <summary>
        /// Every clip name ppcontent.json declares, checked against the clips the project's models
        /// actually carry. A name with a typo in it would otherwise change NOTHING and say nothing -
        /// the author would watch a walk cycle still stop dead and have no line to read.
        /// One arm for every declaration of this kind, so a second one cannot be added without a check.
        /// </summary>
        private static int ClipNamesDeclared(ContentProject p, StringBuilder log)
        {
            var loop = ClipFields.Names(p.LoopDeclaration);
            var play = ClipFields.Names(p.PlayDeclaration);
            if (loop.Count == 0 && play.Count == 0) return 0;
            // The BAKEABLE clips, not every clip the files carry: a clip that drives no bone of the rig
            // is left out of the bundle entirely (and said so, one line up), so declaring it would loop
            // - or would be the one the Animator plays - is just as silent as a typo.
            // The skipped lines come along so a name that IS in the author's file but bakes to nothing
            // is refused with THAT reason instead of "no clip carries it", which would send them
            // hunting a typo that is not there.
            var names = new List<string>();
            var skipped = new List<string>();
            foreach (ImportedModel model in p.Models)
                foreach (KeyValuePair<string, SampledClip> e in ClipFields.Bakeable(model.Name, model.Clips, skipped))
                    names.Add(e.Value.Name);

            // TooMany first: "Idle, Walk" is refused for being TWO clips, not for a name being unknown -
            // both of its names may well be carried, which is exactly how it used to pass this gate and
            // then play clip 0. Both sides of the string are parsed by ClipFields.Names and nothing else.
            string unknown = ClipFields.TooMany(play) ??
                             ClipFields.Unknown("loop", loop, names, skipped) ??
                             ClipFields.Unknown("play", play, names, skipped);
            return Check(log, "clip-names", unknown == null, unknown ?? "\"loop\" names " + loop.Count +
                         " clip(s) and \"play\" names " + play.Count + " of the " + names.Count +
                         " this project bakes");
        }

        /// <summary>
        /// ============ HAND THE MODDER THE LIST OF ANIMATIONS IN THEIR MODEL ============
        ///
        /// A modder finds a model on the internet and drops it in. The one thing they cannot know from
        /// looking at it is what the file calls its own clips - and the one thing the TOOL cannot know is
        /// which of those clips is the walk and which is the death, because glTF has no such flag and a
        /// keyword guess on an author's naming would silently bind the wrong one.
        ///
        /// So: the bake DISCOVERS, MEASURES and WRITES BACK, and the author MAPS.
        ///   * every clip in the file is written into the project's own ppcontent.json, under
        ///     "creature": { "clips": { "&lt;clip&gt;": "&lt;role&gt;" } }, with roles left empty
        ///     (<see cref="CreatureManifest.Scaffold"/> - additive and idempotent, so a role already
        ///     filled in survives a re-bake and a newly added clip arrives blank);
        ///   * what was MEASURED is printed beside it - the model's span, the scale that makes it one
        ///     tile across, its bone count, and which blocking events each mapped role still lacks;
        ///   * a REQUIRED role left unmapped REFUSES the bake, by name.
        ///
        /// The refusal is the point. An unmapped role is not a missing feature, it is a wrong one: the
        /// Action state falls back to an event-less clip and every attack then eats three ten-second
        /// timeouts, which reads to a player as the GAME hanging (AnimEventReceiver.cs:100,126).
        ///
        /// A project that declares NO "creature" block is untouched - discovery still prints, nothing is
        /// written and nothing is refused. Declaring an empty <c>"creature": {}</c> is the whole opt-in.
        /// </summary>
        private static int CreatureScaffold(ContentProject p, StringBuilder log)
        {
            List<string> discovered = new List<string>();
            foreach (ImportedModel m in p.Models)
            {
                if (!m.Baked.Rigged) continue;
                foreach (KeyValuePair<string, SampledClip> e in ClipFields.Bakeable(m.Name, m.Clips))
                    if (!discovered.Contains(e.Value.Name)) discovered.Add(e.Value.Name);

                // The three numbers a downloaded rig arrives with that nobody can guess: how big it is,
                // what scale makes it a creature instead of a building, and where its feet are relative
                // to its origin. All measured off the buffers this same bake is about to write.
                float ex = m.Baked.Mesh.ExtentX, ey = m.Baked.Mesh.ExtentY, ez = m.Baked.Mesh.ExtentZ;
                float span = 2f * Math.Max(ex, Math.Max(ey, ez));
                log.AppendLine("creature-measure '" + m.Name + "': " + m.Baked.BoneNames.Length +
                    " bone(s), spans " + ModelBuild.F(2f * ex) + " x " + ModelBuild.F(2f * ey) + " x " +
                    ModelBuild.F(2f * ez) + " file unit(s) about " + ModelBuild.F(m.Baked.Mesh.CenterX) +
                    "," + ModelBuild.F(m.Baked.Mesh.CenterY) + "," + ModelBuild.F(m.Baked.Mesh.CenterZ) +
                    "; a tile is 1.0, so \"scale\": " + ModelBuild.F(span <= 0f ? 1f : 1f / span) +
                    " makes it one tile across (this project declares " + ModelBuild.F(p.Scale) +
                    "). Its origin is " + ModelBuild.F(ey) + " above its lowest vertex on +Y, which is " +
                    "\"creature\": { \"lift\" } if the model is centred rather than standing on its feet.");
                log.AppendLine("creature-clips '" + m.Name + "': " + discovered.Count + " animation(s) in " +
                    "the file -> " + (discovered.Count == 0 ? "(none)" : string.Join(", ", discovered.ToArray())));
            }
            if (discovered.Count == 0) return 0;

            string metaPath = Path.Combine(p.Root, Project.ContentMods.Manifest);
            string json = File.ReadAllText(metaPath);
            string updated = CreatureManifest.Scaffold(json, discovered);
            if (updated == null)
            {
                log.AppendLine("creature-scaffold: this project declares no \"creature\" block, so nothing " +
                    "was written and no role is required. Add \"creature\": {} to " +
                    Project.ContentMods.Manifest + " and re-bake to have the clip list filled in for you.");
                return 0;
            }
            if (updated != json)
            {
                File.WriteAllText(metaPath, updated);
                log.AppendLine("creature-scaffold: WROTE the clip list into " + metaPath +
                               " - map each one to a role there.");
            }

            CreatureManifest man = CreatureManifest.Parse(updated);
            string missing = man.Missing(discovered);
            if (missing != null) return Check(log, "creature-roles", false, missing);

            // A MAPPED "climb" clip WINS over the synthesised one and is used VERBATIM - its own root
            // motion is the height the engine will measure. So it is checked here rather than trusted:
            // a clip whose root does not RISE claims a climb and then slides the creature along the wall
            // it is supposed to be going up, and the only symptom in game is a route that looks broken.
            string climbName = man.ClipFor("climb");
            if (climbName != null)
                foreach (ImportedModel m in p.Models)
                {
                    if (!m.Baked.Rigged) continue;
                    foreach (KeyValuePair<string, SampledClip> e in ClipFields.Bakeable(m.Name, m.Clips))
                    {
                        if (!string.Equals(e.Value.Name, climbName, StringComparison.OrdinalIgnoreCase)) continue;
                        float rise = ClipFields.RiseOf(ClipFields.Bindings(e.Value, m.Baked, null, p.Scale),
                                                       m.Baked, e.Value.Times.Length);
                        if (!(rise > 0f))
                            return Check(log, "creature-climb", false, "'" + climbName + "' is mapped to " +
                                "the \"climb\" role but its root motion rises " + ModelBuild.F(rise) +
                                " - a traversal clip has to carry the creature UP (AnimationInfos.cs:" +
                                "104-121 measures Offset = end - start on the root-motion node, and " +
                                "ClimbPathProcessor places the loop point at anchor + Offset.y). Re-export " +
                                "it with real upward root motion, or unmap the role and let the tool " +
                                "synthesise the climb out of your walk cycle.");
                    }
                }

            // Mapped is not the same as PLAYABLE. Every blocking event the game waits for during a role
            // has to be somewhere in that role's clip, and only the animation knows where - so an
            // undeclared one is named here rather than invented at load time.
            List<string> gaps = new List<string>();
            foreach (KeyValuePair<string, CreatureRoles.Event[]> need in CreatureRoles.Blocking)
            {
                if (man.ClipFor(need.Key) == null) continue;
                string[] have = man.EventsFor(need.Key).Select(e => e.Name).ToArray();
                foreach (CreatureRoles.Event ev in need.Value)
                    if (!have.Contains(ev.Name)) gaps.Add(need.Key + "." + ev.Name);
            }
            log.AppendLine("creature-events " + (gaps.Count == 0 ? "PASS" : "WARN") + " " +
                (gaps.Count == 0 ? "every blocking event the game waits for is declared"
                 : "UNDECLARED: " + string.Join(", ", gaps.ToArray()) + " - each costs a 10s stall per " +
                   "action (AnimEventReceiver.cs:100,126). Add them to \"creature\": { \"events\" } as " +
                   "\"<role>\": \"<Event> <fraction of the clip>, ...\" at the frame the animation " +
                   "actually connects; the tool will not guess that time for you."));
            return Check(log, "creature-roles", true, "\"clips\" maps " +
                man.Clips.Count(e => e.Value.Length > 0) + " of " + discovered.Count +
                " discovered animation(s); every required role (" +
                string.Join(", ", CreatureManifest.RequiredRoles) + ") is mapped");
        }

        private static string ImportedClips(BundleBaker baker, ContentProject p, ImportedModel m,
                                            StringBuilder log)
        {
            if (!m.Baked.Rigged || m.Clips.Count == 0) return null;
            var skipped = new List<string>();
            List<KeyValuePair<string, SampledClip>> plan = ClipFields.Bakeable(m.Name, m.Clips, skipped);
            // Said out loud, not dropped quietly: a clip the bake has nowhere to put is an author's
            // file being read correctly, so it costs its line and not the whole bake.
            foreach (string line in skipped) log.AppendLine(line);
            var loops = ClipFields.Names(p.LoopDeclaration);
            // WHICH clip the AOC hands Mecanim. -1 is "declared, but it is another model's clip", which
            // is not an error - the project-level `clip-names` arm is what refuses a name no model has.
            int chosen = ClipFields.Chosen(plan, p.PlayDeclaration);
            if (chosen < 0) chosen = 0;
            string play = null;
            foreach (KeyValuePair<string, SampledClip> e in plan)
            {
                SampledClip c = e.Value;
                var notes = new List<string>();
                string paced = PaceClip(p, m, c);
                if (paced != null) log.AppendLine("clip '" + c.Name + "' " + paced);
                List<ClipFields.Binding> bindings = ClipFields.Bindings(c, m.Baked, notes, p.Scale);
                foreach (string note in notes) log.AppendLine(note);
                bool loop = ClipFields.Wants(loops, c.Name);
                string key = baker.AddAnimationClip("clips/" + e.Key, bindings,
                                                    c.Times.Length, c.SampleRate, loop);
                if (e.Key == plan[chosen].Key) play = key;
                int curves = Curves(bindings);
                log.AppendLine("clip '" + Tail(key) + "' from the file: " + bindings.Count +
                               " binding(s) over " + c.Tracks.Count + " bone(s), " + c.Times.Length +
                               " frame(s) @ " + ModelBuild.F(c.SampleRate) + " Hz = " +
                               (c.Times.Length * curves) + " dense float(s), muscleSize=" +
                               ClipFields.MuscleClipSize(2, c.Times.Length * curves, 0, curves) +
                               (loop ? ", LOOPS" : ", plays once") +
                               (e.Key == plan[chosen].Key ? ", ANIMATOR PLAYS THIS" : "") +
                               "; " + c.LossyReason);
            }
            ClimbClips(baker, p, m, plan, log);
            return play;
        }

        /// <summary>
        /// ============ THE THREE CLIPS THAT MAKE A CREATURE CROSS AN OBSTACLE ============
        ///
        /// Phoenix Point's maps are made of obstacles, so a creature that can only walk around them is
        /// not a creature. But a downloaded .glb ships a walk and an idle - it does not ship a wall
        /// climb - and filling the engine's traversal slots with a FLAT clip hangs the mover half way
        /// through a window (see the note at CreatureBuild's traversal arm).
        ///
        /// So the three parts a ClipSequence needs are SYNTHESISED out of the walk itself: the same
        /// bones cycling at the same cadence, with the root ramping straight UP instead of forward and
        /// the body pitched onto the wall. <see cref="ClipFields.Climb"/> does the writing; everything
        /// this decides is in <see cref="Tactical.ClimbPlan"/>, which the runtime and the offline check
        /// read too.
        ///
        /// GROUND-ONLY IS STILL THE DEFAULT. No walk role mapped, no walk clip in the plan, or no single
        /// root bone means nothing is written - and the runtime then finds no climb clips, leaves the
        /// traversal families empty and never adds the link areas, exactly as before.
        /// </summary>
        private static void ClimbClips(BundleBaker baker, ContentProject p, ImportedModel m,
                                       List<KeyValuePair<string, SampledClip>> plan, StringBuilder log)
        {
            Tactical.CreatureManifest man = Tactical.CreatureManifest.Load(p.Root);
            string walkName = man.ClipFor("walk");
            if (walkName == null) return;
            SampledClip walk = null;
            foreach (KeyValuePair<string, SampledClip> e in plan)
                if (string.Equals(e.Value.Name, walkName, StringComparison.OrdinalIgnoreCase)) walk = e.Value;
            if (walk == null) return;
            // The SAME pace the real bake used, or the synthesised legs would cycle at a different rate
            // from the walk they were taken from.
            PaceClip(p, m, walk);

            foreach (string part in Tactical.ClimbPlan.Parts)
            {
                float from, to;
                Tactical.ClimbPlan.Pitch(part, out from, out to);
                string why;
                List<ClipFields.Binding> bindings = ClipFields.Climb(
                    walk, m.Baked, p.Scale, Tactical.ClimbPlan.Rise(part),
                    from * man.ClimbPitch, to * man.ClimbPitch, out why);
                if (bindings == null) { log.AppendLine("creature-climb: " + why); return; }
                bool loops = Tactical.ClimbPlan.Loops(part);
                string key = baker.AddAnimationClip(
                    "clips/" + m.Name + Tactical.ClimbPlan.Suffix + part, bindings, walk.Times.Length,
                    walk.SampleRate * (loops ? 1f : Tactical.ClimbPlan.ShortBy), loops);
                log.AppendLine("creature-climb '" + Tail(key) + "' from '" + walk.Name + "': " + why +
                               (loops ? ", LOOPS" : ", plays once") + " - Offset.y is what the engine " +
                               "measures the link height against (AnimationInfos.cs:104-121)");
            }
        }

        /// <summary>
        /// THE WALK CYCLE PLAYED AT THE PACE THE GAME TRAVELS AT, or null when this clip is not the
        /// creature's locomotion.
        ///
        /// ONLY the clip the manifest maps to the <c>walk</c> role, because "how fast does this play"
        /// has exactly one right answer per clip and it is not the same answer for all of them: the
        /// game measures traversal speed off the locomotion clip alone (Treadmill.ShippedPace records
        /// the four file:line), while an attack's rate is set by the ActionDo/ShootShot events the
        /// ability blocks on and a death's by Ragdoll. Retiming those would move a hit frame the author
        /// measured by hand.
        ///
        /// Called from BOTH the bake and the ClipWrote oracle so neither can depend on running first -
        /// <see cref="Treadmill.Retime"/> is idempotent (a clip already at pace measures the pace and
        /// is left alone), so the second call is a measurement and not a second retime.
        /// </summary>
        private static string PaceClip(ContentProject p, ImportedModel m, SampledClip c)
        {
            Tactical.CreatureManifest man = Tactical.CreatureManifest.Load(p.Root);
            string walk = man.ClipFor("walk");
            if (walk == null || !string.Equals(walk, c.Name, StringComparison.OrdinalIgnoreCase))
                return null;
            string why;
            Treadmill.Retime(c, m.Baked, p.Scale, man.Pace, out why);
            return why;
        }

        private static string LiftClip(BundleBaker baker, ContentProject p, ImportedModel m, StringBuilder log)
        {
            if (!m.Baked.Rigged || p.Id != SampleId) return null;
            int bone = LiftBone(m.Baked);
            float restY = m.Baked.BoneRest[bone][13];
            string key = baker.AddAnimationClip("clips/" + m.Name + LiftClipSuffix,
                                                m.Baked.BonePath(bone),
                                                new[] { restY, restY + ModelLift, restY + ModelLift },
                                                LiftClipRate);
            log.AppendLine("clip '" + Tail(key) + "' drives '" + m.Baked.BonePath(bone) + "' from y=" +
                           ModelBuild.F(restY) + " to y=" + ModelBuild.F(restY + ModelLift));
            return key;
        }

        /// <summary>
        /// U7-wrote - the FILE half, with no engine in the loop: the clip's one binding is the CRC of
        /// the bone's path under the model root, which is the same number M1-wrote asserts the MESH
        /// carries in its hashes. Two writers, one path, checked before anything is loaded.
        /// </summary>
        private static int ClipWrote(ContentProject p, string outPath, ImportedModel m, StringBuilder log)
        {
            if (!m.Baked.Rigged) return 0;
            // The BAKEABLE clips, not every clip the file carries: a model whose only animation drives
            // nothing of the rig bakes exactly like a model that carries no animation at all.
            List<KeyValuePair<string, SampledClip>> plan = ClipFields.Bakeable(m.Name, m.Clips);
            bool imported = plan.Count > 0;
            if (!imported && p.Id != SampleId) return 0;
            string gate = imported ? "U9" : "U7";

            string want, got;
            if (imported)
            {
                // U9-wrote. The oracle is the IMPORT's own binding list, hashed the way the FILE's is:
                // ClipFields.Sig is order-sensitive over (pathCRC : attribute), so a curve landing on
                // the wrong bone, a wrong attribute width, or a reordered flat index all move it -
                // which is the whole silent-failure class this arm exists for. The dense dimensions
                // come with it, because a signature alone cannot see a bank sized for other frames.
                SampledClip c = plan[0].Value;
                // The SAME scale AND THE SAME PACE the real bake used, or this oracle's signature would
                // describe a clip the bundle does not contain and the arm would go red on a correct bake.
                PaceClip(p, m, c);
                List<ClipFields.Binding> bindings = ClipFields.Bindings(c, m.Baked, null, p.Scale);
                want = "clip '" + plan[0].Key + "' bindings=" + bindings.Count +
                       " sig=" + ClipFields.Sig(bindings) + " typeID=4 dense=" + c.Times.Length + "x" +
                       Curves(bindings) + "@" + ModelBuild.F(c.SampleRate);
                got = BundleBaker.ReadClipSummary(outPath, plan[0].Key);
            }
            else
            {
                int bone = LiftBone(m.Baked);
                want = "clip '" + m.Name + LiftClipSuffix + "' bindings=1 path=" +
                       SkinFields.BoneHash(m.Baked.BonePath(bone)) + " attr=" +
                       ClipFields.AttributePosition + " typeID=4";
                got = BundleBaker.ReadClipSummary(outPath, m.Name + LiftClipSuffix);
            }
            bool ok = got.StartsWith(want, StringComparison.Ordinal);
            int failures = Check(log, gate + "-wrote", ok, "the clip in the file binds the CRCs of the " +
                "imported rig's own bone paths -> " + got + (ok ? "" : " (expected " + want + ")"));

            // The Animator, asserted in the FILE and not only through the engine: a missing one makes
            // the -mecanim arm say VOID, and a VOID is not a failure - so without this the shipping
            // shape could quietly go unmeasured forever.
            string wantAnim = "controller='" + m.Name + AocSuffix + "' avatar=0 culling=0 hierarchy=True";
            string gotAnim = BundleBaker.ReadAnimatorOn(outPath, m.Name);
            return failures + Check(log, gate + "-wrote-anim", gotAnim == wantAnim,
                "the imported model root carries the Animator that plays it -> " + gotAnim +
                (gotAnim == wantAnim ? "" : " (expected " + wantAnim + ")"));
        }

        /// <summary>How many dense curve floats a binding list eats - the flat count the bank holds.</summary>
        private static int Curves(List<ClipFields.Binding> bindings)
        {
            int curves = 0;
            foreach (ClipFields.Binding b in bindings)
                curves += b.Attribute == ClipFields.AttributeRotation ? 4 : 3;
            return curves;
        }

        /// <summary>
        /// U7 - the JOIN, at runtime: a baked AnimationClip drives the IMPORTED rig and the skin
        /// follows. Both halves were proven separately and nothing joined them - row 19 ends "nothing
        /// yet drives an imported rig with a CLIP".
        ///
        /// The oracle is two numbers of different kinds in one run: the driven bone's own
        /// localPosition, asserted by IDENTITY against what the clip's last frame says, and then every
        /// vertex, predicted from the AUTHOR'S OWN weights the way M1 predicts a hand lift
        /// (rest + carriedWeight * delta). So a clip that evaluated to nothing leaves the weighted
        /// vertices at rest and reads RED, and a rig that moved as one object fails on the vertices
        /// whose carried weight is 0 - which is this arm's control, in the same bake and the same
        /// instance rather than a second run.
        ///
        /// Two arms over one measurement, because they can fail apart: **U7** is the CLIP alone
        /// (AnimationClip.SampleAnimation - no controller in the loop), **U7-mecanim** is the shipping
        /// shape (the Animator BAKED on the model root, its AnimatorOverrideController over a SHIPPED
        /// base reached by an external PPtr, driven by Animator.Update). U6 proved that pair on a
        /// hierarchy this tool synthesised; what was never asked is whether it holds on a rig whose
        /// bone paths came out of an author's FILE.
        /// </summary>
        private static int Animated(StringBuilder log, ContentProject p, ImportedModel m,
                                    AssetBundle bundle, string modelKey, string clipKey)
        {
            if (clipKey == null) return 0;
            GameObject prefab = bundle.LoadAsset<GameObject>(modelKey);
            AnimationClip clip = bundle.LoadAsset<AnimationClip>(clipKey);
            if (prefab == null || clip == null)
            {
                log.AppendLine("U7 VOID " + (prefab == null ? "the model prefab '" + modelKey : "the clip '" + clipKey) +
                               "' did not load out of the bundle");
                return 0;
            }
            if (ClipFields.Bakeable(m.Name, m.Clips).Count > 0)
                return DriveImported(log, "U9", m, prefab, clip, false, p.PlayDeclaration) +
                       DriveImported(log, "U9-mecanim", m, prefab, clip, true, p.PlayDeclaration);
            return Drive(log, "U7", m, prefab, clip, false) +
                   Drive(log, "U7-mecanim", m, prefab, clip, true);
        }

        /// <summary>
        /// U9 - the whole point of reading a .glb's animation: the clip the FILE carries, baked, drives
        /// the imported rig, and every bone lands where the file said it would.
        ///
        /// The oracle is the IMPORT itself, per bone and per channel: at the clip's MIDDLE frame
        /// each driven Transform's localPosition / localRotation / localScale is asserted BY IDENTITY
        /// against the sample <c>GlbReader</c> read out of the file for that frame. Nothing here is a
        /// constant, and a clip that evaluated to nothing leaves every bone on its rest value - which
        /// is why the arm ALSO counts how many bones actually left rest and fails when none did. That
        /// count is the control: a rig frozen at its bind pose satisfies "no bone is wrong" perfectly.
        ///
        /// The frame is sampled ON the grid (t = frame / rate), so no interpolation rule is assumed,
        /// and it is strictly INSIDE the clip, so a looping state cannot have wrapped to frame 0 and
        /// handed back the rest pose. Rotations are compared by |dot| - q and -q are the same rotation
        /// and the engine is free to hand back either.
        ///
        /// Two arms, the same pair U6/U7 keep apart: the clip ALONE (SampleAnimation, no controller in
        /// the loop) and the shipping shape (the baked Animator + AnimatorOverrideController).
        /// </summary>
        private static int DriveImported(StringBuilder log, string gate, ImportedModel m, GameObject prefab,
                                         AnimationClip clip, bool mecanim, string play)
        {
            // The clip the AOC was handed - ppcontent.json's "play", else the first BAKEABLE one, which
            // is not m.Clips[0] when the file leads with an animation this bake has nowhere to put. ONE
            // resolver (ClipFields.Chosen) with the bake side, or this arm would assert the wrong clip's
            // samples against whatever the Animator really plays and go red for the wrong reason.
            List<KeyValuePair<string, SampledClip>> plan = ClipFields.Bakeable(m.Name, m.Clips);
            int chosen = ClipFields.Chosen(plan, play);
            SampledClip src = plan[chosen < 0 ? 0 : chosen].Value;
            int frame = src.Times.Length / 2;
            float time = frame / src.SampleRate;
            GameObject go = UnityEngine.Object.Instantiate(prefab);
            try
            {
                Animator animator = go.GetComponent<Animator>();
                // A piece of the shipping shape that is not there is a FAILURE of this gate, not a
                // question it cannot ask - so nothing below returns before the verdict.
                string missing = null, how;
                if (mecanim)
                {
                    RuntimeAnimatorController rac = animator == null ? null : animator.runtimeAnimatorController;
                    if (animator == null)
                        missing = "the model root carries no baked Animator, so the shipping shape was " +
                                  "never exercised";
                    else if (rac == null)
                        missing = "the Animator's runtimeAnimatorController is null - the external base " +
                                  "controller did not resolve, so the shipping shape was never exercised";
                    how = "Animator.Update(" + ModelBuild.F(time) + ")" +
                          (rac == null ? "" : " through '" + rac.name + "' (" + rac.GetType().Name + ")");
                }
                else
                {
                    // Outside the Editor the engine refuses a NON-LEGACY clip "without an Animator" and
                    // writes NOTHING - the same line Drive carries, and the same reason.
                    if (!clip.legacy && animator == null) animator = go.AddComponent<Animator>();
                    how = "SampleAnimation(" + ModelBuild.F(time) + ") - no controller in the loop";
                }

                // Rest is read BEFORE the drive, off this same instance, so "moved" is measured against
                // what the bake actually put in the hierarchy rather than against the file's own rest.
                Transform[] driven = new Transform[src.Tracks.Count];
                Vector3[] restPos = new Vector3[src.Tracks.Count];
                Quaternion[] restRot = new Quaternion[src.Tracks.Count];
                for (int t = 0; t < src.Tracks.Count && missing == null; t++)
                {
                    string path = m.Baked.BonePath(src.Tracks[t].Node);
                    driven[t] = go.transform.Find(path);
                    if (driven[t] == null)
                    {
                        missing = "the baked hierarchy has no transform at '" + path +
                                  "', the path the clip binds by";
                        break;
                    }
                    restPos[t] = driven[t].localPosition;
                    restRot[t] = driven[t].localRotation;
                }

                int wrong = 0, moved = 0;
                string worst = "", furthest = "";
                float worstBy = 0f, travel = 0f;
                if (missing == null)
                {
                    if (mecanim) animator.Update(time);
                    else clip.SampleAnimation(go, time);

                    for (int t = 0; t < src.Tracks.Count; t++)
                    {
                        SampledTrack track = src.Tracks[t];
                        Transform bone = driven[t];
                        float off = 0f;
                        if (track.Translations != null)
                        {
                            ObjVector3 v = track.Translations[frame];
                            off = Math.Max(off, (bone.localPosition - new Vector3(v.X, v.Y, v.Z)).magnitude);
                        }
                        if (track.Scales != null)
                        {
                            ObjVector3 s = track.Scales[frame];
                            off = Math.Max(off, (bone.localScale - new Vector3(s.X, s.Y, s.Z)).magnitude);
                        }
                        if (track.Rotations != null)
                        {
                            ObjQuaternion q = track.Rotations[frame];
                            // |dot|: the engine may hand back either of the two quaternions that ARE this
                            // rotation, and 1 - |dot| is 0 exactly when they name the same one.
                            float dot = Math.Abs(Quaternion.Dot(bone.localRotation,
                                                                new Quaternion(q.X, q.Y, q.Z, q.W)));
                            off = Math.Max(off, 1f - dot);
                        }
                        // How far this bone LEFT ITS REST POSE - the control's own quantity, and NOT
                        // the error above. Reporting the error as "furthest" is what printed "18 of
                        // them off their rest pose (furthest 'BackLeg3.R' by 0)": on a green run the
                        // error IS zero, so the number contradicted the count it was printed beside.
                        float went = Math.Max((bone.localPosition - restPos[t]).magnitude,
                                              1f - Math.Abs(Quaternion.Dot(bone.localRotation, restRot[t])));
                        if (went > ClipFields.RestTravel) moved++;
                        if (went > travel)
                        {
                            travel = went;
                            furthest = m.Baked.BoneNames[track.Node];
                        }
                        if (off > worstBy)
                        {
                            worstBy = off;
                            worst = m.Baked.BoneNames[track.Node];
                        }
                        if (off > ClipFields.RestTravel) wrong++;
                    }
                }

                string why = ClipFields.DriveVerdict(missing, src.Tracks.Count, wrong, moved, travel);
                return Check(log, gate, why == null, "the file's own clip '" + clip.name + "' by " + how +
                    " puts every bone of the IMPORTED rig where the .glb says at frame " + frame +
                    " of " + src.Times.Length + ": " + src.Tracks.Count + " bone(s) checked, " + wrong +
                    " off by more than " + ModelBuild.F(ClipFields.RestTravel) + " (worst '" + worst +
                    "' by " + ModelBuild.F(worstBy) + "), " + moved + " of them off their rest pose" +
                    " (furthest '" + furthest + "' travelled " + ModelBuild.F(travel) + ")" +
                    (why == null ? "" : " - " + why));
            }
            finally { UnityEngine.Object.Destroy(go); }
        }

        /// <summary>
        /// One instantiation, one rest bake, one drive, one bake again. <paramref name="mecanim"/>
        /// picks WHO drives: the clip itself, or the baked Animator's own controller.
        /// </summary>
        private static int Drive(StringBuilder log, string gate, ImportedModel m, GameObject prefab,
                                 AnimationClip clip, bool mecanim)
        {
            BakedSkin s = m.Baked;
            int bone = LiftBone(s);
            GameObject go = UnityEngine.Object.Instantiate(prefab);
            Mesh scratch = new Mesh();
            try
            {
                SkinnedMeshRenderer smr = go.GetComponentInChildren<SkinnedMeshRenderer>(true);
                // The same rule the U9 arm follows: a missing piece of the shipping shape FAILS this
                // gate. It used to say VOID and return zero failures, so a bundle that never
                // assembled could still report ALL PASS.
                if (smr == null || smr.bones == null || smr.bones.Length <= bone)
                    return Check(log, gate, false, "the instantiated '" + m.Name +
                        "' carries no SkinnedMeshRenderer with a bone " + bone);
                Animator animator = go.GetComponent<Animator>();
                string how;
                if (mecanim)
                {
                    if (animator == null)
                        return Check(log, gate, false, "the model root carries no baked Animator, so " +
                            "the shipping shape was never exercised");
                    RuntimeAnimatorController rac = animator.runtimeAnimatorController;
                    if (rac == null)
                        return Check(log, gate, false, "the Animator's runtimeAnimatorController is " +
                            "null - the external base controller did not resolve, so the shipping " +
                            "shape was never exercised");
                    how = "Animator.Update(" + ModelBuild.F(LiftTime) + ") through '" + rac.name +
                          "' (" + rac.GetType().Name + ")";
                }
                else
                {
                    // The one component SampleAnimation cannot do without, the same line the P7 sampler
                    // carries (ported from ResourceReplacer MeshReplacer.cs:930): outside the Editor the
                    // engine refuses a NON-LEGACY clip "without an Animator" and writes NOTHING, which
                    // reads exactly like a clip whose binding path nobody matches. The baked one serves.
                    if (!clip.legacy && animator == null) animator = go.AddComponent<Animator>();
                    how = "SampleAnimation(" + ModelBuild.F(LiftTime) + ") - no controller in the loop";
                }

                Transform driven = smr.bones[bone];
                smr.BakeMesh(scratch);
                Vector3[] rest = scratch.vertices;
                Vector3 boneRest = driven.localPosition;
                // Measured in the RENDERER's space, for the reason Deform states: a bone's LOCAL
                // displacement is the mesh's only when its ancestors are pure translations. The sample
                // rig is exactly that, so this is the same vector it always was - it just stops being
                // an assumption the moment a clip drives an author's own rig.
                Vector3 from = smr.transform.InverseTransformPoint(driven.position);

                if (mecanim) animator.Update(LiftTime);
                else clip.SampleAnimation(go, LiftTime);
                smr.BakeMesh(scratch);
                Vector3[] moved = scratch.vertices;

                Vector3 target = new Vector3(0f, s.BoneRest[bone][13] + ModelLift, 0f);
                Vector3 delta = smr.transform.InverseTransformPoint(driven.position) - from;
                bool ok = (driven.localPosition - target).magnitude < 1e-3f &&
                          rest.Length == s.Mesh.VertexCount && moved.Length == rest.Length;
                string predicted = "";
                for (int i = 0; i < rest.Length && i < moved.Length; i++)
                {
                    Vector3 want = rest[i] + s.CarriedWeight(i, bone) * delta;
                    if (i < 8) predicted += (i == 0 ? "" : ",") + ModelBuild.F(want.y);
                    if ((moved[i] - want).magnitude > 1e-3f) ok = false;
                }
                return Check(log, gate, ok, "the baked clip '" + clip.name + "' by " + how +
                    " drives the IMPORTED rig's '" + s.BonePath(bone) + "' from y=" +
                    ModelBuild.F(boneRest.y) + " to y=" + ModelBuild.F(driven.localPosition.y) +
                    " (expected " + ModelBuild.F(target.y) + ") and the skin follows: rest y=[" + Ys(rest) +
                    "] -> y=[" + Ys(moved) + "], the file's own weights predict [" + predicted + "]");
            }
            finally
            {
                UnityEngine.Object.Destroy(scratch);
                UnityEngine.Object.Destroy(go);
            }
        }

        /// <summary>The asset name inside a container key - 'assets/&lt;modid&gt;/clips/NAME'.</summary>
        /// <summary>
        /// A .glb's embedded PNG or JPEG to the RGBA32 a serialized Texture2D wants, through Unity's
        /// own Texture2D.LoadImage - the same decoder ContentProject uses for an author's .png, and
        /// the only one in this process. Returns null with a reason rather than throwing: one
        /// undecodable image should cost that material its paint, not the whole bake.
        /// </summary>
        private static ImportedTexture DecodeEmbedded(byte[] bytes, string name, out string why)
        {
            why = null;
            UnityEngine.Texture2D t = new UnityEngine.Texture2D(2, 2, UnityEngine.TextureFormat.RGBA32, false);
            try
            {
                if (!t.LoadImage(bytes))
                {
                    why = "Unity could not decode it; a .glb may carry PNG or JPEG and nothing else";
                    return null;
                }
                UnityEngine.Color32[] px = t.GetPixels32();
                byte[] rgba = new byte[px.Length * 4];
                for (int i = 0; i < px.Length; i++)
                {
                    rgba[i * 4] = px[i].r; rgba[i * 4 + 1] = px[i].g;
                    rgba[i * 4 + 2] = px[i].b; rgba[i * 4 + 3] = px[i].a;
                }
                return new ImportedTexture
                {
                    Name = name.ToLowerInvariant(), Width = t.width, Height = t.height, Rgba32 = rgba
                };
            }
            finally { UnityEngine.Object.Destroy(t); }
        }

        /// <summary>A material name is part of an asset path, so it keeps only what a path can hold.</summary>
        private static string Safe(string s)
        {
            System.Text.StringBuilder b = new System.Text.StringBuilder(s.Length);
            foreach (char c in s) b.Append(char.IsLetterOrDigit(c) || c == '_' || c == '-' ? char.ToLowerInvariant(c) : '_');
            return b.ToString();
        }

        private static string Tail(string key) { return key.Substring(key.LastIndexOf('/') + 1); }

        /// <summary>The first few vertex heights, which is what a reader can eyeball.</summary>
        private static string Ys(Vector3[] v)
        {
            string s = "";
            for (int i = 0; i < v.Length && i < 8; i++) s += (i == 0 ? "" : ",") + ModelBuild.F(v[i].y);
            return v.Length > 8 ? s + ",... (" + v.Length + ")" : s;
        }

        /// <summary>
        /// The negative controls, in the SAME run as the positives (METHODOLOGY, "always take a
        /// control measurement inside the same run"): a decoder that succeeds on random bytes proves
        /// nothing about the files it succeeded on, and a format with no decoder must be refused BY
        /// NAME rather than baked into a silent empty bank.
        ///
        /// Synchronous now. These used to run from EngineAudio's coroutine because the decoder was
        /// the ENGINE'S and could not answer in the frame it was asked; the tool's own decoder can.
        /// </summary>
        private static int AudioControls(StringBuilder log)
        {
            int failures = 0;
            string dir = Path.Combine(Path.GetTempPath(), "ct-audio-controls");
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
            Directory.CreateDirectory(dir);
            try
            {
                byte[] junk = new byte[4096];
                new System.Random(7).NextBytes(junk);
                // Named .ogg AND .mp3: the two decoders are different libraries and a control that
                // only exercised one would say nothing about the other.
                foreach (string ext in new string[] { ".ogg", ".mp3" })
                {
                    string junkFile = Path.Combine(dir, "junk" + ext);
                    File.WriteAllBytes(junkFile, junk);
                    string why;
                    WwisePcm.Wav w = WwisePcm.ReadAudio(junkFile, out why);
                    if (w != null) failures++;
                    log.AppendLine("A6-ctl-junk " + (w == null ? "PASS " : "FAIL ") +
                                   "4096 random bytes named " + ext + " must NOT decode -> " +
                                   (why ?? "DECODED " + w.Channels + "ch " + w.SampleRate + "Hz") +
                                   "  [CONTROL: must NOT decode]");
                }

                // Every format the tool does NOT accept, in one arm: a whitelist that quietly grew a
                // hole would let one of these bake to silence.
                string[] unsupported = { "song.flac", "song.m4a", "song.aac", "song.wma", "song.opus" };
                foreach (string n in unsupported) File.WriteAllBytes(Path.Combine(dir, n), junk);
                File.WriteAllBytes(Path.Combine(dir, "keep.wav"), junk);   // accepted set must NOT be swept up
                string refusal = ContentProject.RefuseUnsupported(dir);
                bool named = refusal != null && !refusal.Contains("keep.wav");
                foreach (string n in unsupported) named = named && refusal.Contains(n);
                if (!named) failures++;
                log.AppendLine("A6-ctl-name " + (named ? "PASS " : "FAIL ") +
                               ".flac/.m4a/.aac/.wma/.opus are refused BY NAME before any decode, " +
                               "and .wav is not -> " +
                               (refusal ?? "ACCEPTED - they would have baked to silence") +
                               "  [CONTROL: must refuse]");
            }
            finally { try { Directory.Delete(dir, true); } catch (IOException) { } }
            return failures;
        }

        /// <summary>
        /// Gate A6 - what came out of the import IS the sound that went in. The oracle is the
        /// SOURCE's own header (<see cref="Import.SourceAudio"/>), never a constant in this file: an
        /// .ogg states its channels, rate and total sample frames, so a decode that lost a channel,
        /// resampled, or stopped early comes out RED. The peak is asserted separately because a
        /// correctly sized buffer of ZEROES would otherwise pass every count - that exact
        /// falsification caught a real bug in the extract slice (4d21bc1).
        ///
        /// A .wav declares nothing this can check independently: WwisePcm.ReadWav IS its parser, so
        /// the arm would be comparing a read against itself. That says VOID, never PASS.
        /// </summary>
        private static int AudioIdentity(ContentProject p, StringBuilder log)
        {
            int failures = 0;
            foreach (ImportedAudio a in p.Audio)
            {
                string got = a.Channels + "ch " + a.SampleRate + "Hz " + a.Frames + " frames peak=" +
                             a.Peak.ToString("0.000") + " -> " + a.Wem.Length + " B .wem";
                if (a.Declared == null)
                {
                    log.AppendLine("A6 VOID " + a.SourceFile + " declares nothing to compare - it " +
                                   a.DeclaredWhy + "; decoded " + got);
                    continue;
                }
                long slack = a.Extension == ".mp3" ? Mp3PaddingFrames : 0;
                bool frames = a.Declared.Frames < 0 ||
                              (a.Frames >= a.Declared.Frames && a.Frames <= a.Declared.Frames + slack);
                bool ok = a.Channels == a.Declared.Channels && a.SampleRate == a.Declared.SampleRate &&
                          frames && a.Peak > 0.01f;
                failures += Check(log, "A6", ok, a.SourceFile + " decoded " + got + " vs the source's own " +
                                  "header: " + a.Declared.Describe() +
                                  (slack > 0 ? " (+ up to " + slack + " frames of decoder padding)" : ""));
            }
            return failures;
        }

        /// <summary>
        /// Route vii, declared by the author (gate P1). For every shipped bundle named in
        /// ppcontent.json "replace", clone it, overwrite the named objects, write the patched copy
        /// into <see cref="ContentToolMain.PatchedDir"/> (persistentDataPath\ContentTool\Patched\
        /// &lt;modId&gt;\) with the SOURCE's compression, and hand the copies to the catalog
        /// record so the game loads them with no runtime code (PROVEN-FOUNDATIONS R7).
        ///
        /// The copy is produced from the player's own install and never leaves it, which is what
        /// keeps Phoenix Point's data out of the shipped mod.
        /// </summary>
        private static int Patch(ContentProject p, StringBuilder log)
        {
            int failures = 0;
            string outDir = ContentToolMain.PatchedDir(p.Id);
            Directory.CreateDirectory(outDir);
            List<KeyValuePair<string, string>> copies = new List<KeyValuePair<string, string>>();

            foreach (string bundleFile in Bundles(p))
            {
                string shipped = BakeSelfCheck.ShippedBundlePath(bundleFile);
                string copy = Path.Combine(outDir, bundleFile);
                List<ImportedTexture> want = new List<ImportedTexture>();
                List<KeyValuePair<string, string>> mats = new List<KeyValuePair<string, string>>();
                List<KeyValuePair<string, ImportedMesh>> meshes = new List<KeyValuePair<string, ImportedMesh>>();
                List<KeyValuePair<string, ShippedReplacement>> clips =
                    new List<KeyValuePair<string, ShippedReplacement>>();
                using (BundleBaker baker = new BundleBaker(shipped, p.Id))
                {
                    foreach (ShippedReplacement r in p.Replace)
                    {
                        if (!string.Equals(r.bundle, bundleFile, StringComparison.OrdinalIgnoreCase)) continue;

                        if (!string.IsNullOrEmpty(r.material))
                        {
                            string[] kv = r.material.Split('=');
                            float v;
                            if (kv.Length != 2 || !float.TryParse(kv[1], NumberStyles.Float,
                                                                 CultureInfo.InvariantCulture, out v))
                            {
                                log.AppendLine("P3 REFUSED \"material\": \"" + r.material + "\" is not <property>=<number>");
                                return failures + 1;
                            }
                            baker.ReplaceMaterialFloat(r.asset, kv[0], v);
                            mats.Add(new KeyValuePair<string, string>(r.asset, kv[0] + "=" +
                                     v.ToString(CultureInfo.InvariantCulture)));
                            log.AppendLine("patch " + bundleFile + ": material '" + r.asset + "' " + r.material);
                            continue;
                        }

                        if (!string.IsNullOrEmpty(r.clip))
                        {
                            uint attribute;
                            float k;
                            string why = ParseClipEdit(r.clip, out attribute, out k);
                            if (why != null)
                            {
                                log.AppendLine("P7 REFUSED \"clip\": \"" + r.clip + "\" " + why);
                                return failures + 1;
                            }
                            string walked = baker.ReplaceClipCurves(r.asset, attribute, k);
                            clips.Add(new KeyValuePair<string, ShippedReplacement>(r.asset, r));
                            log.AppendLine("patch " + bundleFile + ": clip '" + r.asset + "' " + r.clip +
                                           " - " + walked);
                            continue;
                        }

                        if (!string.IsNullOrEmpty(r.mesh))
                        {
                            ImportedMesh im = FindMesh(p, r.mesh);
                            if (im == null)
                            {
                                log.AppendLine("P4 REFUSED '" + r.mesh + "' is not a .obj or .glb under Content\\Meshes\\");
                                return failures + 1;
                            }
                            string how = baker.ReplaceMesh(r.asset, im.Baked, im.Model);
                            meshes.Add(new KeyValuePair<string, ImportedMesh>(r.asset, im));
                            log.AppendLine("patch " + bundleFile + ": mesh '" + r.asset + "' <- " + im.Name +
                                           " " + im.Baked.Describe() + " - skinned " + how);
                            continue;
                        }

                        ImportedTexture t = Find(p, r.texture);
                        if (t == null)
                        {
                            log.AppendLine("P1 REFUSED '" + r.texture + "' is not a .png/.jpg under Content\\Textures\\");
                            return failures + 1;
                        }
                        baker.ReplaceTexture2D(r.asset, t.Width, t.Height, t.Rgba32);
                        want.Add(t);
                        log.AppendLine("patch " + bundleFile + ": '" + r.asset + "' <- " + t.Name +
                                       " " + t.Width + "x" + t.Height);
                    }
                    baker.Write(copy, null);   // identity kept: the copy stands in for the shipped file
                    log.AppendLine("WROTE " + copy + " " + new FileInfo(copy).Length + " B as " +
                                   baker.WrittenIdentity + " (shipped source is " + new FileInfo(shipped).Length + " B)");
                }

                // Read the pixels back out of the COPY, and check the shipped original still reads its
                // own bytes - the control that says we patched a copy and not the player's game.
                // With no texture declared for THIS bundle both arms are vacuous - and the control
                // arm is vacuously FALSE, which reported a failure on a perfectly correct mesh-only
                // bundle (ct_project 14:5x). A gate that cannot answer says VOID, never PASS or FAIL.
                if (want.Count == 0)
                    log.AppendLine("P1 VOID 0 texture replacement(s) declared in " + bundleFile);
                else
                {
                    failures += Check(log, "P1", PixelsIn(copy, want),
                        "every replaced Texture2D in " + bundleFile + " reads back its new pixels");
                    failures += Check(log, "P1-ctl-shipped", !PixelsIn(shipped, want),
                        "the shipped " + bundleFile + " does NOT contain them - it was never written");
                }

                // P3 reads the property block off the FILE, the same oracle U3a-refs uses: a Material
                // is not loadable through the engine here without a shader, and the value we care
                // about is the serialized one anyway.
                foreach (KeyValuePair<string, string> m in mats)
                {
                    string got = BundleBaker.ReadMaterialProperties(copy, m.Key);
                    failures += Check(log, "P3", got.Contains("| " + m.Value),
                        "material '" + m.Key + "' in the copy carries " + m.Value + " -> " + got);
                    failures += Check(log, "P3-ctl-shipped",
                        !BundleBaker.ReadMaterialProperties(shipped, m.Key).Contains("| " + m.Value),
                        "the shipped " + bundleFile + "'s '" + m.Key + "' does NOT carry it");
                }
                // P4 reads the geometry off the FILE, the same oracle the offline round trip uses:
                // a shipped Mesh is not CPU-readable, so the engine cannot answer this question at all.
                // Describe() is a PREFIX of Summary() by construction, so one comparison covers vertex
                // count, index count, index format and bounds together.
                foreach (KeyValuePair<string, ImportedMesh> mesh in meshes)
                {
                    string want_ = mesh.Value.Baked.Describe();
                    string got = BundleBaker.ReadMeshSummary(copy, mesh.Key);
                    failures += Check(log, "P4", got.StartsWith(want_, StringComparison.Ordinal),
                        "mesh '" + mesh.Key + "' in the copy IS " + mesh.Value.Name + " -> " + got);
                    failures += Check(log, "P4-ctl-shipped",
                        !BundleBaker.ReadMeshSummary(shipped, mesh.Key).StartsWith(want_, StringComparison.Ordinal),
                        "the shipped " + bundleFile + "'s '" + mesh.Key + "' still has its own geometry -> " +
                        BundleBaker.ReadMeshSummary(shipped, mesh.Key));

                    // P5: the replacement is SKINNED to the target's own skeleton. The expected
                    // skeleton is not a constant - it is read off the SHIPPED file in this same run,
                    // so the arm asserts that the copy carries the exact bind poses, bone hashes and
                    // root hash the game shipped, PLUS our skin stream over them, PLUS a bone index
                    // that every one of those bind poses can answer. Rebind doing nothing and Rebind
                    // clobbering the skeleton both come out RED.
                    // No separate control arm: "the shipped file was never written" is P4-ctl-shipped's
                    // question and "a rebound mesh actually deforms" is U5b-deform's, both in the same
                    // run. A third arm here would restate one of them.
                    string skinShipped = BundleBaker.ReadMeshSummary(shipped, mesh.Key, true);
                    string skinCopy = BundleBaker.ReadMeshSummary(copy, mesh.Key, true);
                    string skeleton = Skeleton(skinShipped);
                    if (skeleton == null)
                        log.AppendLine("P5 VOID '" + mesh.Key + "' is not rigged - " + skinShipped);
                    else
                    {
                        string wantSkin = skeleton + " " + SkinFields.OurLayout + " skinBytes=" +
                                          mesh.Value.Baked.VertexCount * SkinFields.SkinStride;
                        failures += Check(log, "P5",
                            skinCopy.StartsWith(wantSkin, StringComparison.Ordinal) &&
                            skinCopy.EndsWith(" inRange=yes", StringComparison.Ordinal),
                            "mesh '" + mesh.Key + "' in the copy is SKINNED to the shipped skeleton -> " +
                            skinCopy + " (expected " + wantSkin + " ... inRange=yes; shipped is " +
                            skinShipped + ")");
                    }

                    failures += ByName(log, mesh.Key, mesh.Value, shipped, copy);
                }
                foreach (KeyValuePair<string, ShippedReplacement> c in clips)
                {
                    uint attribute; float k;
                    ParseClipEdit(c.Value.clip, out attribute, out k);
                    failures += Curves(log, c.Key, c.Value.clip, attribute, k, shipped, copy);
                    failures += SampleClip(log, c.Key, attribute, k, shipped, copy);
                }
                copies.Add(new KeyValuePair<string, string>(bundleFile, copy.Replace('\\', '/')));
            }

            // Baking produces artifacts and NOTHING else. Installing is an explicit, separate act -
            // a build command must not mutate the player's game installation, and a downloaded mod
            // gets installed without ever being baked.
            string name = Path.GetFileName(p.Root.TrimEnd('\\'));
            // Only when a copy was actually written: a project whose "replace" holds video rows alone
            // patches no bundle, and route vii refuses exactly the command this line would tell the
            // author to run.
            // The tail of this line used to read "install them with: ct_route7 apply <name>", which
            // sent every author off to run a verb nobody needs: the mod-manager checkbox installs
            // these (ModRoster.AfterSetEnabled -> Route7.Toggle), and a player never sees a console.
            // A source-blind modder read that line against the site's "there is no apply" and could
            // not tell which was current (blind test round 2, A2). `ct_route7 apply` still exists as
            // a DEV entry point, so it is named as one instead of being prescribed.
            if (copies.Count > 0)
                log.AppendLine("copies ready in " + outDir + " - nothing to install: ticking '" + name +
                               "' on in the mod manager redirects them (dev-only shortcut: ct_route7 apply " +
                               name + ")");
            foreach (ShippedReplacement r in p.Replace)
                if (!string.IsNullOrEmpty(r.video))
                {
                    // Nothing to bake for a video: there is no serialized asset to lay out, only one
                    // in-memory catalog row pointed at the mod's own file, which is ct_video's job.
                    log.AppendLine((string.IsNullOrEmpty(r.asset)
                                       ? "video ADD '" + r.video + "' (its RuntimeKey is printed by the command)"
                                       : "video '" + r.asset + "' <- " + r.video) +
                                   " - serve it with: ct_video live " + name);
                }
            return failures;
        }

        /// <summary>
        /// P6 - a rigged replacement carries the AUTHOR'S OWN weights, on the bones the author's file
        /// NAMES, and not on the slots it happens to list them in.
        ///
        /// The oracle is every vertex of the written copy, read back out of the bytes
        /// (<see cref="SkinFields.SkinInfluences"/>), against an expectation built HERE from two
        /// independent things: the file's own WEIGHTS_0, and a plain name lookup of the file's joint
        /// in the SHIPPED skeleton read off the shipped bundle in this same run. Nothing in the
        /// expectation comes from the binder, so a binder that transposed the rig cannot agree with it.
        ///
        /// The arm REFUSES to run on a file whose joint order already matches the target's, because
        /// there a by-name binding and an index binding write identical bytes and the run would
        /// measure nothing. That is a VOID, never a PASS - it is the vacuity this gate exists to
        /// avoid, and the sample's fixture is written with its joints REVERSED precisely so the
        /// question can be asked at all.
        /// </summary>
        private static int ByName(StringBuilder log, string key, ImportedMesh im, string shipped, string copy)
        {
            SkinnedModel f = im.Model;
            if (f == null || f.JointNames.Count == 0)
            {
                log.AppendLine("P6 VOID '" + key + "' <- " + im.Name + " carries no armature (" +
                               (f == null ? "an .obj never does" : "the .glb has no skin") +
                               "), so there are no weights of its own to remap");
                return 0;
            }

            string[] bones = BundleBaker.ReadBoneNames(shipped, key);
            if (bones == null)
            {
                log.AppendLine("P6 VOID '" + key + "' - no SkinnedMeshRenderer in " +
                               Path.GetFileName(shipped) + " names this mesh's bones, so the shipped " +
                               "skeleton cannot be looked up by name");
                return 0;
            }

            int n = im.Baked.VertexCount;
            if (f.Joints == null || f.Weights == null || f.Joints.Length != n * 4)
            {
                log.AppendLine("P6 VOID '" + key + "' - the file's skin does not cover its " + n + " vertices");
                return 0;
            }

            string want = "";
            int moved = 0, split = 0;
            for (int i = 0; i < n; i++)
            {
                int a, b;
                SkinFields.Heaviest(f.Weights, i, out a, out b);
                float wa = a < 0 ? 0f : f.Weights[i * 4 + a];
                float wb = b < 0 ? 0f : f.Weights[i * 4 + b];
                float sum = wa + wb;
                if (sum <= 0f) { a = 0; wa = 1f; wb = 0f; sum = 1f; b = -1; }

                int slot0 = f.Joints[i * 4 + a], slot1 = b < 0 ? slot0 : f.Joints[i * 4 + b];
                int live0 = Array.IndexOf(bones, f.JointNames[slot0]);
                int live1 = Array.IndexOf(bones, f.JointNames[slot1]);
                if (live0 < 0 || live1 < 0)
                {
                    log.AppendLine("P6 VOID '" + key + "' - the file's bone '" +
                                   f.JointNames[live0 < 0 ? slot0 : slot1] +
                                   "' is not on the shipped skeleton, so this replacement was refused " +
                                   "by name and there is no by-name binding to measure");
                    return 0;
                }
                if (live0 != slot0 || live1 != slot1) moved++;
                if (wb > 0f) split++;
                want += (i == 0 ? "" : " ") + "v" + i + "=" + ModelBuild.F(wa / sum) + "/" +
                        ModelBuild.F(wb / sum) + "->bone" + live0 + "+bone" + live1;
            }

            if (moved == 0)
            {
                log.AppendLine("P6 VOID '" + key + "' <- " + im.Name + " lists the skeleton in the " +
                               "target's own order, so a binding by NAME and one by SLOT write the same " +
                               "bytes and this run would measure nothing");
                return 0;
            }

            string got = BundleBaker.ReadSkinInfluences(copy, key);
            return Check(log, "P6", got == want,
                "mesh '" + key + "' <- " + im.Name + " carries the FILE's own weights on the bones it " +
                "NAMES: " + moved + " of " + n + " vertices sit at a file slot that is not the live bone " +
                "index, and " + split + " are shared between two bones (a fraction nearest-bone cannot " +
                "produce). The copy reads " + got + "; the file's own weights and a name lookup in the " +
                "shipped skeleton (" + bones.Length + " bones) predict " + want);
        }

        /// <summary>
        /// The skeleton half of a <see cref="SkinFields.SkinSummary"/> line - its first four tokens,
        /// bindposes/hashes/rootHash/bonesAABB. null when the mesh carries no bind poses, which is a
        /// question P5 cannot ask rather than one it can answer with "no".
        /// </summary>
        /// <summary>
        /// The author's whole curve-edit grammar: <c>&lt;channel&gt;*&lt;number&gt;</c>, the same shape
        /// "material" already uses. Returns null when it read, or the sentence that says why not.
        ///
        /// `rotation` is refused rather than supported: a rotation curve is a quaternion, and
        /// multiplying one denormalises it into something Unity renders as a sheared rig - a
        /// refusal by name beats a result nobody can read.
        /// </summary>
        private static string ParseClipEdit(string edit, out uint attribute, out float factor)
        {
            attribute = 0;
            factor = 0f;
            string[] kv = (edit ?? "").Split('*');
            if (kv.Length != 2 || !float.TryParse(kv[1], NumberStyles.Float, CultureInfo.InvariantCulture, out factor))
                return "is not <channel>*<number>, e.g. \"position*3\"";
            string channel = kv[0].Trim().ToLowerInvariant();
            if (channel == "position") attribute = ClipFields.AttributePosition;
            else if (channel == "scale") attribute = ClipFields.AttributeScale;
            else return "names the channel '" + kv[0].Trim() + "'; a clip edit takes \"position\" or " +
                        "\"scale\"" + (channel == "rotation"
                            ? " - a rotation curve is a quaternion and scaling one denormalises it, so it is refused"
                            : "");
            if (factor == 1f) return "changes nothing (x1); delete the entry or pick another factor";
            return null;
        }

        /// <summary>
        /// P7 - the edited clip in the COPY carries the shipped curve scaled by the author's own
        /// factor, float for float, and the channel the author did NOT name still carries the
        /// shipped values exactly.
        ///
        /// Both lists are read in the same run: the expectation is the SHIPPED file times the
        /// author's number, never a constant written here, so an edit that landed on the wrong curve
        /// or on the wrong bank cannot agree with it. The two numbers reported are the largest the
        /// edit moved, so a run cannot read as measured while sitting inside float noise.
        /// The control asserts the POSITIVE identity of the untouched channel (its own shipped
        /// values), not the absence of anything.
        /// </summary>
        private static int Curves(StringBuilder log, string clipName, string edit, uint attribute,
                                  float factor, string shipped, string copy)
        {
            List<float> was = BundleBaker.ReadClipCurves(shipped, clipName, attribute);
            List<float> now = BundleBaker.ReadClipCurves(copy, clipName, attribute);
            if (was.Count == 0)
            {
                log.AppendLine("P7 VOID clip '" + clipName + "' binds no curve for the channel in \"" +
                               edit + "\", so the edit had nothing to scale");
                return 0;
            }
            bool ok = now.Count == was.Count;
            int loudest = 0;
            for (int i = 0; ok && i < was.Count; i++)
            {
                if (Math.Abs(was[i]) > Math.Abs(was[loudest])) loudest = i;
                ok &= Same(now[i], was[i] * factor);
            }
            int failures = Check(log, "P7", ok,
                "clip '" + clipName + "' " + edit + ": all " + was.Count + " curve float(s) in the copy " +
                "are the shipped value x " + factor.ToString(CultureInfo.InvariantCulture) +
                " (the copy holds " + now.Count + "). The largest the edit moved is float " + loudest +
                ", shipped " + Num(was[loudest]) + " -> copy " + Num(now[loudest]) + " (expected " +
                Num(was[loudest] * factor) + ")");

            uint other = attribute == ClipFields.AttributePosition
                ? (uint)ClipFields.AttributeScale : (uint)ClipFields.AttributePosition;
            List<float> otherWas = BundleBaker.ReadClipCurves(shipped, clipName, other);
            List<float> otherNow = BundleBaker.ReadClipCurves(copy, clipName, other);
            if (otherWas.Count == 0)
            {
                log.AppendLine("P7-ctl-channel VOID clip '" + clipName + "' binds no attribute-" + other +
                               " curve, so there is no untouched channel to read");
                return failures;
            }
            bool same = otherNow.Count == otherWas.Count;
            for (int i = 0; same && i < otherWas.Count; i++) same &= otherNow[i] == otherWas[i];
            return failures + Check(log, "P7-ctl-channel", same,
                "the copy's attribute-" + other + " curves still ARE the shipped ones - all " +
                otherWas.Count + " float(s), first " + Num(otherWas.Count > 0 ? otherWas[0] : 0f) +
                " and the copy reads " + Num(otherNow.Count > 0 ? otherNow[0] : 0f));
        }

        /// <summary>
        /// P7-sample - the ENGINE's answer, and the only one that says a player would see the edit:
        /// the same rig, sampled by <c>AnimationClip.SampleAnimation</c> at the same time, once with
        /// the shipped clip and once with the edited one, both loaded out of a real bundle.
        ///
        /// Nothing here is told which bone to look at: every transform of the instance is read by
        /// NAME and the two poses are compared, so a bone the edit was supposed to move and did not
        /// is as visible as one it moved wrongly. A run in which nothing moved by a readable amount
        /// reports VOID - sampling a rig where the edit is inside float noise measures nothing.
        ///
        /// The two bundles carry the same CAB, so they are mounted one at a time, never together.
        /// </summary>
        private static int SampleClip(StringBuilder log, string clipName, uint attribute, float factor,
                                      string shipped, string copy)
        {
            string why;
            Dictionary<string, Vector3> edited = Pose(copy, clipName, attribute, out why);
            if (edited == null) { log.AppendLine("P7-sample VOID the patched copy " + why); return 0; }
            Dictionary<string, Vector3> original = Pose(shipped, clipName, attribute, out why);
            if (original == null) { log.AppendLine("P7-sample VOID the shipped bundle " + why); return 0; }

            string loudest = null;
            float best = 0f;
            bool ok = true;
            int moved = 0;
            foreach (KeyValuePair<string, Vector3> e in original)
            {
                Vector3 a;
                if (!edited.TryGetValue(e.Key, out a)) continue;
                Vector3 want = e.Value * factor;
                float delta = (a - e.Value).magnitude;
                if (delta > 0.001f) moved++;
                if (delta > best) { best = delta; loudest = e.Key; }
                // A transform the clip does not BIND keeps the prefab's rest value under both clips,
                // and a rest pose is not part of the curve, so it must not be asked to scale - that
                // is what turned an engine result of exactly x3 into a FAIL on its first run. Either
                // the edit reached this transform and it is the shipped value x factor, or neither
                // clip wrote it and it is bit-identical. The arm's strength is the VOID below, which
                // refuses a run where nothing moved by a readable amount.
                ok &= (Same(a.x, want.x) && Same(a.y, want.y) && Same(a.z, want.z)) || a == e.Value;
            }
            if (loudest == null || best < 0.01f)
            {
                log.AppendLine("P7-sample VOID the sampled rig (" + original.Count +
                               " transform(s)) moved by at most " +
                               best.ToString("0.#####", CultureInfo.InvariantCulture) +
                               " between the shipped clip and the edited one, which is not a " +
                               "distance this arm can tell from float noise");
                return 0;
            }
            return Check(log, "P7-sample", ok,
                "'" + clipName + "' sampled on the rig: " + moved + " of " + original.Count +
                " transform(s) moved, the furthest is '" + loudest + "' at " + V(original[loudest]) +
                " with the shipped clip and " + V(edited[loudest]) + " with the edited one (expected " +
                V(original[loudest] * factor) + ", the author's x" +
                factor.ToString(CultureInfo.InvariantCulture) + ")");
        }

        /// <summary>
        /// Mounts one bundle, samples its own '<paramref name="clipName"/>' on the biggest rig it
        /// ships, and hands back that pose by transform NAME. Everything is unmounted before it
        /// returns, because the caller mounts a second bundle carrying the same CAB.
        /// </summary>
        private static Dictionary<string, Vector3> Pose(string bundlePath, string clipName,
                                                        uint attribute, out string why)
        {
            why = null;
            AssetBundle bundle = AssetBundle.LoadFromFile(bundlePath);
            if (bundle == null) { why = "did not mount (" + bundlePath + ")"; return null; }
            GameObject instance = null;
            try
            {
                // LoadAsset by NAME cannot reach it: a shipped clip is a SUB-ASSET of its .fbx, and
                // m_Container registers the .fbx path, not the clip (measured 2026-08-12 -
                // aln_fireworm registers 'ALN_Fireworm_Ball.fbx' seven times, one per sub-asset).
                AnimationClip clip = null;
                foreach (AnimationClip c in bundle.LoadAllAssets<AnimationClip>())
                    if (c.name == clipName) { clip = c; break; }
                if (clip == null) { why = "holds no AnimationClip named '" + clipName + "'"; return null; }
                GameObject rig = null;
                int most = 0;
                foreach (GameObject g in bundle.LoadAllAssets<GameObject>())
                {
                    int n = g.GetComponentsInChildren<Transform>(true).Length;
                    if (n > most) { most = n; rig = g; }
                }
                if (rig == null) { why = "ships no GameObject to sample the clip on"; return null; }

                instance = UnityEngine.Object.Instantiate(rig);
                // The one component SampleAnimation cannot do without, ported from ResourceReplacer
                // (MeshReplacer.cs:930): outside the Editor the engine refuses a NON-LEGACY clip
                // "without an Animator" and writes NOTHING - the clip samples as a still rest pose,
                // which is exactly the silent zero this arm read on its first run. No controller is
                // needed; the Animator only gives SampleAnimation the binding target it demands.
                if (!clip.legacy && instance.GetComponent<Animator>() == null)
                    instance.AddComponent<Animator>();
                clip.SampleAnimation(instance, clip.length * 0.5f);
                Dictionary<string, Vector3> pose = new Dictionary<string, Vector3>();
                foreach (Transform t in instance.GetComponentsInChildren<Transform>(true))
                    if (!pose.ContainsKey(t.name))
                        pose.Add(t.name, attribute == ClipFields.AttributeScale ? t.localScale : t.localPosition);
                return pose;
            }
            finally
            {
                if (instance != null) UnityEngine.Object.Destroy(instance);
                bundle.Unload(true);
            }
        }

        /// <summary>Two floats one scaling apart - a relative window, because the numbers a curve
        /// carries run from a millimetre to a metre and one epsilon cannot serve both.</summary>
        private static bool Same(float a, float b)
        {
            return Math.Abs(a - b) <= 1e-5f + 1e-4f * Math.Abs(b);
        }

        private static string Num(float v) { return v.ToString("0.######", CultureInfo.InvariantCulture); }

        private static string V(Vector3 v) { return "(" + Num(v.x) + "," + Num(v.y) + "," + Num(v.z) + ")"; }

        private static string Skeleton(string skinSummary)
        {
            string[] t = skinSummary.Split(' ');
            if (t.Length < 4 || !t[0].StartsWith("bindposes=", StringComparison.Ordinal)) return null;
            if (t[0] == "bindposes=0") return null;
            return t[0] + " " + t[1] + " " + t[2] + " " + t[3];
        }

        /// <summary>Distinct shipped bundles named by the project, in declaration order.</summary>
        private static List<string> Bundles(ContentProject p)
        {
            List<string> names = new List<string>();
            foreach (ShippedReplacement r in p.Replace)
            {
                // Video entries carry no bundle - the cutscenes are loose files behind Catalog.json,
                // registered live by ct_video. Without this they would enter the list as "" and the baker
                // would be handed an empty shipped-bundle path.
                if (!string.IsNullOrEmpty(r.video)) continue;
                if (!names.Contains(r.bundle)) names.Add(r.bundle);
            }
            return names;
        }

        private static ImportedTexture Find(ContentProject p, string name)
        {
            foreach (ImportedTexture t in p.Textures)
                if (string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase)) return t;
            return null;
        }

        private static ImportedMesh FindMesh(ContentProject p, string name)
        {
            foreach (ImportedMesh m in p.Meshes)
                if (string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase)) return m;
            return null;
        }

        /// <summary>True when EVERY wanted texture's pixels are present in that bundle file.</summary>
        private static bool PixelsIn(string bundlePath, List<ImportedTexture> want)
        {
            foreach (ImportedTexture t in want)
                if (!BundleBaker.HasTexturePixels(bundlePath, t.Width, t.Height, t.Rgba32)) return false;
            return true;
        }

        /// <summary>
        /// Every pixel, not a sample: the import buffer came out of GetPixels32 and goes back in as
        /// image data, so the round trip preserves order and there is no row flip to reason about.
        /// (The earlier version assumed one and compared the wrong row - it failed on a bundle whose
        /// pixels were correct.)
        /// </summary>
        private static bool SamePixels(Texture2D tex, ImportedTexture src)
        {
            Color32[] got = tex.GetPixels32();
            if (got.Length * 4 != src.Rgba32.Length) return false;
            for (int i = 0; i < got.Length; i++)
                if (got[i].r != src.Rgba32[i * 4] || got[i].g != src.Rgba32[i * 4 + 1] ||
                    got[i].b != src.Rgba32[i * 4 + 2] || got[i].a != src.Rgba32[i * 4 + 3]) return false;
            return true;
        }

        private static int Check(StringBuilder log, string gate, bool ok, string detail)
        {
            log.AppendLine(gate + (ok ? " PASS " : " FAIL ") + detail);
            return ok ? 0 : 1;
        }

        private static string Str(Color32 c) { return c.r + "," + c.g + "," + c.b + "," + c.a; }

        // ---------------------------------------------------------------- sample project

        /// <summary>
        /// Writes a minimal project next to the mod so `ct_project` has something to bake with no
        /// setup. Generated rather than shipped: deploy copies files, not folders, and a generated
        /// sample cannot go stale against the importer.
        /// ponytail: our own PNG/WAV are the simplest encodings of each. A file from a real art or
        /// audio tool (16-bit PNG, float WAV, odd chunks) is covered by the reader's own branches,
        /// not by this sample.
        /// </summary>
        internal static string WriteSample(string root)
        {
            string tex = Path.Combine(Path.Combine(root, "Content"), "Textures");
            string msh = Path.Combine(Path.Combine(root, "Content"), "Meshes");
            string mdl = Path.Combine(Path.Combine(root, "Content"), "Models");
            string aud = Path.Combine(Path.Combine(root, "Content"), "Audio");
            string vid = Path.Combine(Path.Combine(root, "Content"), "Videos");
            Directory.CreateDirectory(tex);
            Directory.CreateDirectory(msh);
            Directory.CreateDirectory(mdl);
            Directory.CreateDirectory(aud);
            Directory.CreateDirectory(vid);

            // The "replace" entry is what makes the sample exercise route vii end to end. Target
            // measured 2026-08-12: 'fireworm_low_emissive' is a Texture2D in
            // aln_fireworm_assets_all.bundle (2.5 MB, one of the smallest bundles that HAS one -
            // mutoid has none). Its shipped pixels live in a .resS; ours go inline, which is the
            // U1 shape.
            // One verbatim literal with the stamp interpolated in a single place. Assembling it from
            // quoted fragments is how `"sample": 2"` reached disk and made every ct_ command throw a
            // JSON parse error (autogate 2026-08-12) - two hand-rolled halves of one format is one
            // too many, and the reader is already regex.
            // ALN_Fireworm_DMG, not ALN_Fireworm: that bundle ships TWO materials with the latter
            // name, and an ambiguous name is refused, not guessed.
            // "publish" is route iii (gate C1) and deliberately targets a DIFFERENT prefab from the
            // one P2 reads: measured 2026-08-12, '..._BodyAll_Ready.prefab' and its GUID key
            // 54a8f79... share entry 1618, which is exactly what P2 loads, while '..._DMG_Ready' is
            // entry 1617. Repointing 1618 would have made P2 report VOID for the rest of time.
            File.WriteAllText(Path.Combine(root, "ppcontent.json"), @"{
  " + SampleStamp + @",
  ""id"": ""morgott.sample"",
  ""bundle"": ""Sample.bundle"",
  ""loop"": ""Spider_Idle, Spider_Walk"",
  ""play"": ""Spider_Walk"",
  ""replace"": [
    { ""bundle"": ""aln_fireworm_assets_all.bundle"", ""asset"": ""fireworm_low_emissive"", ""texture"": ""swatch"" },
    { ""bundle"": ""aln_fireworm_assets_all.bundle"", ""asset"": ""ALN_Fireworm_DMG"", ""material"": ""_Glossiness=0.875"" },
    { ""bundle"": ""px_assault_assets_all.bundle"", ""asset"": ""CHR_PX_ASS_TS_M_V01_02"", ""mesh"": ""blade"" },
    { ""bundle"": ""px_assault_assets_all.bundle"", ""asset"": ""CHR_PX_ASS_TS_F_V01"", ""mesh"": ""rigfix"" },
    { ""bundle"": ""px_assault_assets_all.bundle"", ""asset"": ""CHR_PX_ASS_RL_F_V01"", ""mesh"": ""foreign"" },
    { ""bundle"": ""aln_fireworm_assets_all.bundle"", ""asset"": ""Fireworm_unfurl"", ""clip"": ""position*3"" },
    { ""asset"": ""StreamableCopiedAssets/Videos/Tutorials/TestTutorialVideo.webm"", ""video"": ""probe"" },
    { ""video"": ""probe_add"" }
  ],
  ""publish"": [
    { ""key"": ""morgott.sample/probe_tex"", ""asset"": ""textures/swatch"", ""type"": ""Texture2D"" },
    { ""key"": ""02_Bodyparts/ALN_Fireworm_BodyAll_DMG_Ready.prefab"", ""asset"": ""models/rig"" },
    { ""key"": ""morgott.sample/probe_clip_walk"", ""asset"": ""clips/spider_spider_walk"", ""type"": ""AnimationClip"" },
    { ""key"": ""morgott.sample/probe_clip_idle"", ""asset"": ""clips/spider_spider_idle"", ""type"": ""AnimationClip"" }
  ]
}");

            // The V1 probe: 256x144, 2 s, 30 fps -> 60 frames, VP8 video + Vorbis audio in WebM, the
            // exact container Phoenix Point ships ("Unity VP8VideoMedia 2019.4.31f1" in every shipped
            // clip's header). 6,265 B, embedded in the assembly for the same reason classdata.tpk is:
            // deploy copies files, and a gate must not depend on a developer path. The frame count is
            // never asserted as a constant - V1 measures it live and only requires it to differ from
            // the clip it replaced.
            // Twice, under two names: the sample declares a video REPLACEMENT and a video ADD, and
            // giving the add its own file is what keeps its gate arm honest - the added row's
            // StreamingPath is then a different string from the replaced row's, so an arm that
            // resolved the wrong record cannot still look right.
            foreach (string probe in new[] { "probe.webm", "probe_add.webm" })
                using (Stream s = ContentToolMain.ProbeVideo())
                using (FileStream f = File.Create(Path.Combine(vid, probe)))
                {
                    if (s == null) throw new InvalidDataException("the build lost the embedded v1_probe.webm");
                    s.CopyTo(f);
                }

            Texture2D t = new Texture2D(8, 8, TextureFormat.RGBA32, false);
            try
            {
                Color32[] px = new Color32[64];
                for (int i = 0; i < px.Length; i++) px[i] = new Color32(255, 0, 255, 255);
                px[0] = new Color32(0, 255, 0, 255);        // bottom-left, the pixel TEX compares
                t.SetPixels32(px);
                t.Apply();
                File.WriteAllBytes(Path.Combine(tex, "swatch.png"), t.EncodeToPNG());
            }
            finally { UnityEngine.Object.Destroy(t); }

            // The mesh target, re-measured 2026-08-12: the Phoenix Assault TORSO, male and female.
            // px_assault_assets_all.bundle ships 11 Meshes, every name distinct (CHR_PX_ASS_<part>_
            // <gender>_V01...), so both are unique and FindUnique accepts them. Chosen over the old
            // 'ALN_Siren_Arm_Slasher_Right' because a Siren is a late-campaign enemy nobody can
            // eyeball - the starting squad wears this armour on the very first roster screen, and
            // both genders are declared so it shows whoever the campaign rolled.
            // A quad, so the gate's numbers are readable by eye: 4 verts, 6 indices, extent 0.5,0.5,0.
            File.WriteAllText(Path.Combine(msh, "blade.obj"),
                "v -0.5 -0.5 0\nv 0.5 -0.5 0\nv 0.5 0.5 0\nv -0.5 0.5 0\n" +
                "vt 0 0\nvt 1 0\nvt 1 1\nvt 0 1\n" +
                "vn 0 0 -1\n" +
                "f 1/1/1 2/2/1 3/3/1\nf 1/1/1 3/3/1 4/4/1\n");

            // The .glb half of the same target: the FEMALE torso, replaced by a file that carries its
            // own armature. Generated from the SHIPPED skeleton in this same folder, because that is
            // the only way a fixture can name bones nobody typed - and its joints are listed in
            // REVERSE, which is what makes P6 able to tell a binding by NAME from one by slot. An
            // author's real file is the same thing without the reversal: extract the model, edit it,
            // drop it back in.
            File.WriteAllBytes(Path.Combine(msh, "rigfix.glb"),
                               ReversedRig("px_assault_assets_all.bundle", "CHR_PX_ASS_TS_F_V01"));

            // THE NEGATIVE HALF of the same question, and the reason it cannot be answered by
            // `rigfix.glb` alone: `rigfix.glb` is generated FROM the shipped skeleton, so its joints
            // are guaranteed to match by name. `foreign.glb` is a real download that was never
            // authored against this game - Quaternius' CC0 spider, 39 joints - dropped onto a shipped
            // RIGGED mesh. Its joint names share exactly ONE spelling with the shipped armature
            // ('Root', a coincidence of a universal name), so the bake log has to report the rebind
            // it could NOT do. Copied rather than generated, for the same reason spider.glb is: our
            // own writer round-tripping our own reader proves nothing about a foreign file.
            string foreign = Path.Combine(ContentToolMain.ModDir ?? ".", "u10_probe.glb");
            string missingForeign = null;
            if (File.Exists(foreign)) File.Copy(foreign, Path.Combine(msh, "foreign.glb"), true);
            else missingForeign = foreign;

            // The ADDED model. Written by the tool's own GLB writer rather than shipped, for the same
            // reason the .png and the .wav are: deploy copies files, not folders, and a generated
            // sample cannot go stale against the reader. Nothing declares it in ppcontent.json - a
            // file under Content\Models\ IS the declaration, the way Content\Textures\ already works.
            File.WriteAllBytes(Path.Combine(mdl, "rig.glb"), ModelBuild.SampleGlb());

            // U9's model, and the one thing the generated sample cannot be: a file this tool did not
            // write. 39 bones, 5 clips, keyframe-reduced 24 fps - Quaternius' CC0 spider, copied from
            // <mod>\u8_probe.glb (the csproj puts it there). Our own writer round-tripping our own
            // reader proves neither half; a real download is the only fixture that does.
            string spider = Path.Combine(ContentToolMain.ModDir ?? ".", "u8_probe.glb");
            string missingAnim = null;
            if (File.Exists(spider)) File.Copy(spider, Path.Combine(mdl, "spider.glb"), true);
            else missingAnim = spider;

            File.WriteAllBytes(Path.Combine(aud, "beep.wav"), Wav(880, 44100, 11025));
            File.WriteAllBytes(Path.Combine(aud, "hum.stream.wav"), Wav(660, 44100, 22050));

            // The A6 probes: 440 Hz sine, 0,5 s, mono 44100, one Vorbis and one MPEG - the same two
            // files gate F1 measured the engine decoding (research-format-coverage.md 2.1). COPIED,
            // not embedded: 8,8 KB of assembly to serve one gate is 8,8 KB the tool has to carry
            // forever, and the feature they prove costs zero bytes. Missing probes make the .ogg/.mp3
            // arms absent, which is said out loud rather than passed over.
            // Distinct STEMS - a record is named by its stem, so chime.ogg next to chime.mp3 is
            // exactly the ambiguity Sources() refuses.
            string probes = Path.Combine(ContentToolMain.ModDir ?? ".", "FormatProbes");
            string missing = null;
            foreach (KeyValuePair<string, string> probe in new[]
                     { new KeyValuePair<string, string>("chime", "ogg"),
                       new KeyValuePair<string, string>("tone", "mp3") })
            {
                string from = Path.Combine(probes, "aud." + probe.Value);
                if (File.Exists(from)) File.Copy(from, Path.Combine(aud, probe.Key + "." + probe.Value), true);
                else missing = (missing == null ? "" : missing + " and ") + "aud." + probe.Value;
            }
            string note = missing == null ? "" :
                " - WITHOUT the .ogg/.mp3 sources (no " + missing + " in " + probes +
                "): gate A6's compressed arms will not run until tools\\make-format-probes.ps1 writes them";
            if (missingAnim != null)
                note += " - WITHOUT the animated model (no " + missingAnim +
                        "): gate U9's arms will not run until the build copies it beside the DLL";
            if (missingForeign != null)
                note += " - WITHOUT the foreign-armature fixture (no " + missingForeign +
                        "): the no-adapter negative row will not bake until the build copies it beside the DLL";
            return root + note;
        }

        /// <summary>
        /// A .glb whose armature IS a shipped mesh's, listed in REVERSE order, with three vertices:
        /// one wholly on the first bone, one wholly on the last, and one SHARED half and half - a
        /// fraction no nearest-bone synthesis can produce, so the written bytes say which path ran.
        ///
        /// Falls back to the plain sample rig when the skeleton cannot be read, which leaves the
        /// replacement on the nearest-bone path and P6 saying VOID out loud. Never a silent skip.
        /// </summary>
        private static byte[] ReversedRig(string bundleFile, string meshName)
        {
            string[] bones;
            try { bones = BundleBaker.ReadBoneNames(BakeSelfCheck.ShippedBundlePath(bundleFile), meshName); }
            catch (Exception) { bones = null; }
            // Two bones whose slots the reversal actually MOVES, or the fixture cannot ask its question.
            int n = bones == null ? 0 : bones.Length;
            if (n < 3 || n - 1 == 0) return ModelBuild.SampleGlb();

            SkinnedModel m = new SkinnedModel { Name = "rigfix" };
            m.Nodes.Add(new SkinNode { Name = "rigfix_root", Parent = -1, Local = Identity() });
            m.JointNodes = new int[n];
            m.InverseBindMatrices = new float[n][];
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
            m.Materials.Add("rigfix");

            // The FIRST and LAST live bones, which the reversal puts in file slots n-1 and 0. Not 0
            // and 1: a reversal leaves the MIDDLE slot where it is, so on an odd bone count that pair
            // could land on the one bone whose slot never moved and P6 would have nothing to measure.
            ushort j0 = (ushort)(n - 1), j1 = 0;
            m.Joints = new ushort[] { j0, 0, 0, 0,  j0, j1, 0, 0,  j1, 0, 0, 0 };
            m.Weights = new[] { 1f, 0f, 0f, 0f,  0.5f, 0.5f, 0f, 0f,  1f, 0f, 0f, 0f };
            return GlbCodec.Write(m);
        }

        private static float[] Identity()
        {
            float[] f = new float[16];
            f[0] = f[5] = f[10] = f[15] = 1f;
            return f;
        }

        /// <summary>16-bit mono PCM .wav - a 44-byte header and the samples.</summary>
        private static byte[] Wav(int freq, int rate, int frames)
        {
            MemoryStream ms = new MemoryStream();
            BinaryWriter w = new BinaryWriter(ms);
            w.Write(Encoding.ASCII.GetBytes("RIFF")); w.Write(36 + frames * 2);
            w.Write(Encoding.ASCII.GetBytes("WAVEfmt ")); w.Write(16);
            w.Write((short)1); w.Write((short)1); w.Write(rate); w.Write(rate * 2);
            w.Write((short)2); w.Write((short)16);
            w.Write(Encoding.ASCII.GetBytes("data")); w.Write(frames * 2);
            for (int i = 0; i < frames; i++) w.Write((short)(16383 * Math.Sin(2.0 * Math.PI * freq * i / rate)));
            return ms.ToArray();
        }
    }
}
