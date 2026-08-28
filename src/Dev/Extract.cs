using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using Base.Core;
using Base.Defs;
using Morgott.ContentTool.Bake;
using Morgott.ContentTool.Import;
using Morgott.ContentTool.Wwise;

namespace Morgott.ContentTool.Dev
{
    /// <summary>
    /// EXTRACTION - pulling a shipped asset back OUT of the game as a file an author can edit.
    /// The import side already takes .png, so extraction closes the loop: without it an author can
    /// only replace assets they happen to have a source for, which is none of the shipped ones.
    ///
    /// The decode is Unity's own. A shipped texture is BC7/DXT on the GPU and not CPU-readable, so
    /// the pixels come back through a RenderTexture blit and Unity's PNG encoder - the same path
    /// ResourceReplacer's texture dump has used in production (pp-native\src\Resource_Replacer.cs
    /// DuplicateTexture/DumpTextureTo). Carrying a BC7 decoder instead would be several hundred
    /// lines to reproduce what the engine hosting us already does.
    ///
    /// Commands take NAMES, never paths (ContentToolMain.ProjectDir explains why).
    /// </summary>
    internal static class Extract
    {
        /// <summary>The probe bundle and texture the round-trip gate writes and reads back.</summary>
        private const string GateBundle = "ctextract";
        private const string GateAsset = "ct_extract_probe";
        private const int GateSize = 8;

        /// <summary>A real shipped, GPU-compressed texture - the arm that exercises the blit path.</summary>
        private const string ShippedBundle = "aln_fireworm_assets_all.bundle";
        private const string ShippedTexture = "fireworm_low_emissive";

        /// <summary>Where extracted files land. Not the mod folder: Steam wipes a Workshop item.</summary>
        internal static string OutDir(string bundleFileName)
        {
            return Path.Combine(Path.Combine(Path.Combine(
                Application.persistentDataPath, "ContentTool"), "Extracted"),
                Path.GetFileNameWithoutExtension(bundleFileName ?? "unknown"));
        }

        // ------------------------------------------------------------------ ct_list

        internal static string List(string[] args)
        {
            string what = args != null && args.Length > 0 ? args[0].ToLowerInvariant() : "";
            if (what == "bundles")
                return Bundles(args != null && args.Length > 1 ? args[1] : null);
            if (what == "assets" && args.Length > 1)
                return Assets(args[1],
                              args.Length > 2 ? args[2] : null,
                              args.Length > 3 ? args[3] : null);
            if (what == "videos")
                return LooseFiles.Report(VideoRoot, ".webm", args.Length > 1 ? args[1] : null, 60);
            if (what == "audio")
                return LooseFiles.Report(AudioRoot, ".wem", args.Length > 1 ? args[1] : null, 60);
            if (what == "defs")
                return Defs(args.Length > 1 ? args[1] : null, args.Length > 2 ? args[2] : null);
            return "usage: ct_list bundles [nameFilter] | ct_list assets <bundleFile> [typeFilter] [nameFilter]" +
                   " | ct_list videos [nameFilter] | ct_list audio [nameFilter]" +
                   " | ct_list defs <nameFilter> [typeFilter]";
        }

        /// <summary>
        /// The DEF half of discovery. Every manifest key that names something the game already owns -
        /// "clone", "damagetype", "keywords", a donor character - is a def NAME, and until this existed
        /// the only way to learn one was to be told it. The lookup here is the SAME one every builder
        /// does (WeaponBuild.One walks GetAllDefs and compares d.name), so a name this prints is a name
        /// a manifest accepts, by construction.
        ///
        /// The name filter is REQUIRED: the repository holds tens of thousands of defs and a bare
        /// listing would be a flood nobody could read. Both filters are substring, case-insensitive;
        /// the type filter matches the def's class name (WeaponDef, DamageKeywordDef, ...).
        /// </summary>
        private static string Defs(string nameFilter, string typeFilter)
        {
            if (string.IsNullOrEmpty(nameFilter))
                return "usage: ct_list defs <nameFilter> [typeFilter] - the name filter is required, " +
                       "the repository is far too large to list whole. Both are substrings and the " +
                       "type one matches a def's BASE classes too, e.g. 'ct_list defs shotgun " +
                       "WeaponDef' or 'ct_list defs fire DamageTypeBaseEffectDef'.";

            DefRepository repo = GameUtl.GameComponent<DefRepository>();
            if (repo == null)
                return "ct_list defs VOID - no DefRepository yet. Defs exist from the MAIN MENU on; " +
                       "run this once the game has finished loading.";

            List<string> hits = new List<string>();
            int scanned = 0;
            foreach (BaseDef d in repo.GetAllDefs<BaseDef>())
            {
                scanned++;
                if (d == null || string.IsNullOrEmpty(d.name)) continue;
                string type = d.GetType().Name;
                if (d.name.IndexOf(nameFilter, StringComparison.OrdinalIgnoreCase) < 0) continue;
                if (!string.IsNullOrEmpty(typeFilter) && !IsA(d.GetType(), typeFilter)) continue;
                hits.Add(d.name + "   [" + type + "]");
            }
            hits.Sort(StringComparer.OrdinalIgnoreCase);

            StringBuilder b = new StringBuilder();
            b.Append(hits.Count).Append(" def(s) match name '").Append(nameFilter).Append("'")
             .Append(string.IsNullOrEmpty(typeFilter) ? "" : " and type '" + typeFilter + "'")
             .Append(" out of ").Append(scanned).Append(" in the repository");
            int n = Math.Min(hits.Count, 60);
            for (int i = 0; i < n; i++) b.Append("\n  ").Append(hits[i]);
            if (hits.Count > n) b.Append("\n  ... ").Append(hits.Count - n).Append(" more (narrow the filter)");
            return b.ToString();
        }

        /// <summary>
        /// The type filter walks the BASE CHAIN, not just the concrete class name, because every
        /// manifest field that takes a def name is typed by a BASE. "damagetype" is resolved against
        /// GetAllDefs&lt;DamageTypeBaseEffectDef&gt;() and the only fire damage type in the game is a
        /// StandardDamageTypeEffectDef - so a filter that compared the concrete name answered
        /// "0 def(s) match", which reads as "this game has no fire damage" and is a lie.
        /// </summary>
        private static bool IsA(Type t, string filter)
        {
            for (Type at = t; at != null; at = at.BaseType)
                if (at.Name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return false;
        }

        private static string Bundles(string nameFilter)
        {
            string dir = Path.GetDirectoryName(BakeSelfCheck.ShippedBundlePath("x"));
            if (!Directory.Exists(dir)) return "ct_list VOID - no shipped bundle folder at " + dir;

            List<string> hits = new List<string>();
            foreach (string f in Directory.GetFiles(dir, "*.bundle"))
            {
                string n = Path.GetFileName(f);
                if (string.IsNullOrEmpty(nameFilter) ||
                    n.IndexOf(nameFilter, StringComparison.OrdinalIgnoreCase) >= 0) hits.Add(n);
            }
            hits.Sort(StringComparer.OrdinalIgnoreCase);

            StringBuilder b = new StringBuilder();
            b.Append(hits.Count).Append(" bundle(s) match '").Append(nameFilter ?? "").Append("' in ").Append(dir);
            int n2 = Math.Min(hits.Count, 60);
            for (int i = 0; i < n2; i++) b.Append("\n  ").Append(hits[i]);
            if (hits.Count > n2) b.Append("\n  ... ").Append(hits.Count - n2).Append(" more (narrow the filter)");
            return b.ToString();
        }

        private static string Assets(string bundleFileName, string typeFilter, string nameFilter)
        {
            string path = BakeSelfCheck.ShippedBundlePath(bundleFileName);
            if (!File.Exists(path)) return "ct_list VOID - no bundle at " + path;
            return bundleFileName + ": " + BundleBaker.ListReport(path, typeFilter, nameFilter, 60);
        }

        // ------------------------------------------------------------------ ct_extract

        internal static string Run(string[] args)
        {
            string verb = args != null && args.Length > 0 ? args[0].ToLowerInvariant() : "";
            if (verb == "gate") return Gate();
            if (verb == "tex" && args.Length > 2) return Texture(args[1], args[2]);
            if (verb == "mesh" && args.Length > 2) return Mesh(args[1], args[2]);
            if (verb == "video" && args.Length > 1) return Video(args[1]);
            if (verb == "audio" && args.Length > 1) return Audio(args[1]);
            return "usage: ct_extract tex <bundleFile> <assetName> | ct_extract mesh <bundleFile> <assetName>" +
                   " | ct_extract video <name> | ct_extract audio <wemName> | ct_extract gate";
        }

        /// <summary>The shipped Wwise media - 3105 loose .wem, in no bundle either.</summary>
        private static string AudioRoot
        {
            get { return Path.Combine(Application.streamingAssetsPath, "Audio"); }
        }

        /// <summary>
        /// One shipped .wem out: the .wem itself byte for byte, because that is the asset, PLUS the
        /// decoded .wav. Both codecs the game ships are handled - 3097 of the 3105 .wem are Wwise
        /// Vorbis and 8 are PCM - and anything that is neither is still refused BY NAME rather than
        /// left as a silent empty .wav.
        /// </summary>
        private static string Audio(string name)
        {
            if (!Directory.Exists(AudioRoot)) return "ct_extract VOID - no audio folder at " + AudioRoot;
            string wem = LooseFiles.CopyOut(AudioRoot, ".wem", name, OutDir("audio"));

            string wavPath = Path.ChangeExtension(wem, ".wav");
            string why = WwiseWem.ToWav(File.ReadAllBytes(wem), wavPath);
            if (why != null)
                return "ct_extract wrote " + wem + " (" + new FileInfo(wem).Length + " B) - NO .wav: " + why;

            WwiseWem.Info i = WwiseWem.Parse(File.ReadAllBytes(wem));
            return "ct_extract wrote " + wem + " and " + wavPath + " (" +
                   new FileInfo(wavPath).Length + " B, " + i.Channels + " ch, " + i.SampleRate + " Hz, " +
                   (i.IsVorbis ? "Wwise Vorbis" : "PCM") + ")";
        }

        /// <summary>
        /// Where the shipped cutscenes live. They are in no bundle at all - VideoCatalog replaces them
        /// through the streamable Catalog.json for exactly that reason.
        /// </summary>
        private static string VideoRoot
        {
            get { return Path.Combine(Application.streamingAssetsPath, "StreamableCopiedAssets"); }
        }

        /// <summary>
        /// One shipped .webm out, byte for byte. There is nothing to decode - re-encoding a shipped
        /// clip would only lose quality on the way to an editor that already reads .webm.
        /// </summary>
        private static string Video(string name)
        {
            if (!Directory.Exists(VideoRoot)) return "ct_extract VOID - no video folder at " + VideoRoot;
            string written = LooseFiles.CopyOut(VideoRoot, ".webm", name, OutDir("videos"));
            return "ct_extract wrote " + written + " (" + new FileInfo(written).Length + " B)";
        }

        /// <summary>One shipped Texture2D out to a .png the importer can read straight back in.</summary>
        private static string Texture(string bundleFileName, string assetName)
        {
            string path = BakeSelfCheck.ShippedBundlePath(bundleFileName);
            if (!File.Exists(path)) return "ct_extract VOID - no bundle at " + path;

            string outPath = Path.Combine(OutDir(bundleFileName), assetName + ".png");
            string how = TextureToPng(path, assetName, outPath);
            return "ct_extract wrote " + outPath + " (" + new FileInfo(outPath).Length + " B) from " + how;
        }

        /// <summary>
        /// One shipped Mesh out to a .glb - geometry, UVs, submeshes, skin weights, bind poses and a
        /// bone node per bind pose, all in one file. Nothing about this needs Unity, so it is the same
        /// call the offline round trip makes.
        /// </summary>
        private static string Mesh(string bundleFileName, string assetName)
        {
            string path = BakeSelfCheck.ShippedBundlePath(bundleFileName);
            if (!File.Exists(path)) return "ct_extract VOID - no bundle at " + path;

            string outPath = Path.Combine(OutDir(bundleFileName), assetName + ".glb");
            string how = MeshToGlb(path, assetName, outPath);
            return "ct_extract wrote " + outPath + " (" + new FileInfo(outPath).Length + " B) from " + how;
        }

        /// <summary>Returns what was read, for the log; anything wrong throws with the cause.</summary>
        internal static string MeshToGlb(string bundlePath, string assetName, string outPath)
        {
            SkinnedModel model = BundleBaker.ReadMesh(bundlePath, assetName);
            byte[] glb = GlbCodec.Write(model);
            Directory.CreateDirectory(Path.GetDirectoryName(outPath));
            File.WriteAllBytes(outPath, glb);
            return Describe(model);
        }

        /// <summary>What a model actually holds, in one line - the oracle both gates read.</summary>
        internal static string Describe(SkinnedModel model)
        {
            int triangles = 0;
            foreach (int[] s in model.Submeshes) triangles += s.Length / 3;
            return "verts=" + model.Positions.Length + " tris=" + triangles +
                   " submeshes=" + model.Submeshes.Count +
                   " normals=" + (model.Normals != null) + " uv0=" + (model.Uv0 != null) +
                   " uv1=" + (model.Uv1 != null) + " tangents=" + (model.Tangents != null) +
                   " bindposes=" + model.BindposeCount +
                   " joints=" + (model.JointNodes == null ? 0 : model.JointNodes.Length) +
                   " nodes=" + model.Nodes.Count;
        }

        /// <summary>
        /// Read the pixels off the file, hand them to the GPU in their shipped format, read them
        /// back decompressed, write a .png. Returns what it read, for the log; anything wrong throws
        /// with the cause, and the callers turn that into VOID or REFUSED, never a silent pass.
        /// </summary>
        private static string TextureToPng(string bundlePath, string assetName, string outPath)
        {
            BundleBaker.RawTexture raw = BundleBaker.ReadTexture(bundlePath, assetName);
            Texture2D tex = new Texture2D(raw.Width, raw.Height, (TextureFormat)raw.Format, raw.MipCount, false);
            tex.hideFlags = HideFlags.HideAndDontSave;
            try
            {
                tex.LoadRawTextureData(raw.Data);
                tex.Apply(false, false);
                byte[] png = Encode(tex);
                if (png == null || png.Length == 0)
                    throw new InvalidOperationException("'" + assetName + "' (" + raw.Describe() + ") encoded to nothing");
                Directory.CreateDirectory(Path.GetDirectoryName(outPath));
                File.WriteAllBytes(outPath, png);
                return raw.Describe();
            }
            finally { UnityEngine.Object.DestroyImmediate(tex); }
        }

        /// <summary>
        /// PNG bytes for any texture. Unity's encoder takes uncompressed CPU layouts only, so an
        /// already-uncompressed texture is encoded straight - no blit, nothing that could shift a
        /// value - and every GPU-compressed one (BC7/DXT, which is most shipped art) is decompressed
        /// by the GPU through the readback copy below.
        /// </summary>
        private static byte[] Encode(Texture2D source)
        {
            if (source.format == TextureFormat.RGBA32 || source.format == TextureFormat.ARGB32 ||
                source.format == TextureFormat.RGB24)
                return ImageConversion.EncodeToPNG(source);
            Texture2D copy = Duplicate(source);
            try { return ImageConversion.EncodeToPNG(copy); }
            finally { UnityEngine.Object.DestroyImmediate(copy); }
        }

        /// <summary>
        /// Readable copy of a GPU-only texture via a RenderTexture blit. Ported verbatim from
        /// ResourceReplacer (pp-native\src\Resource_Replacer.cs DuplicateTexture) - the same code has
        /// dumped this game's textures in a shipped mod. No Apply(): ReadPixels fills the CPU-side
        /// pixels, which is all the encoder reads.
        /// </summary>
        private static Texture2D Duplicate(Texture source)
        {
            RenderTexture rt = RenderTexture.GetTemporary(
                source.width, source.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Default, 1);
            RenderTexture prev = RenderTexture.active;
            Graphics.Blit(source, rt);
            RenderTexture.active = rt;
            Texture2D copy = new Texture2D(source.width, source.height, TextureFormat.ARGB32, false);
            copy.hideFlags = HideFlags.HideAndDontSave;
            try { copy.ReadPixels(new Rect(0f, 0f, rt.width, rt.height), 0, 0); }
            finally
            {
                RenderTexture.active = prev;
                RenderTexture.ReleaseTemporary(rt);
            }
            return copy;
        }

        // ------------------------------------------------------------------ gate X

        /// <summary>
        /// Gate X - extraction is FAITHFUL, not merely non-empty.
        ///
        /// X1 is the round trip that proves it: bake a bundle carrying pixels this method spells out,
        /// extract it back to a .png, and re-import that .png through the SAME ImageConversion.LoadImage
        /// the project importer uses (ContentProject.ImportTexture). The assertion is byte identity
        /// against the pixels that went in - not "a file appeared", which a broken extractor also does.
        ///
        /// X2 is the shipped, GPU-compressed arm, which cannot be byte-compared against anything (its
        /// bytes are BC7 and the file is what we are trying to decode). Its oracle is INDEPENDENT: the
        /// dimensions the serialized file declares, read by AssetsTools with no engine involved, must
        /// equal the dimensions of the decoded PNG. Two different readers agreeing, not one twice.
        /// </summary>
        internal static string Gate()
        {
            StringBuilder log = new StringBuilder();
            int failures = 0;

            // ---- X1: authored pixels -> bundle -> png -> back
            byte[] want = Probe();
            string bundlePath = Path.Combine(Path.Combine(Application.persistentDataPath, "ContentTool"),
                                             GateBundle + ".bundle");
            string pngPath = Path.Combine(OutDir(GateBundle), GateAsset + ".png");
            if (File.Exists(bundlePath)) File.Delete(bundlePath);
            if (File.Exists(pngPath)) File.Delete(pngPath);

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(bundlePath));
                using (BundleBaker baker = new BundleBaker(BakeSelfCheck.ShippedBundlePath(null), "contenttool"))
                {
                    baker.AddTexture2D("textures/" + GateAsset, GateSize, GateSize, want);
                    baker.Write(bundlePath, GateBundle);
                }

                string how = TextureToPng(bundlePath, GateAsset, pngPath);

                // ---- X1b: the texture WE bake must declare itself sRGB, like every shipped one.
                // This game renders in LINEAR (PlayerSettings.m_ActiveColorSpace = 1), so the engine
                // converts a base-colour map on sample ONLY if the map says it is sRGB. A texture left
                // at the class-database default of 0 = Linear is uploaded as if already linear and
                // renders bright and washed out - a brown that should have been a deeper brown.
                // ASSERTED AGAINST THE LITERAL 1, not against whatever this fixture happens to hold:
                // X2 measures the same field on a SHIPPED texture, so the two lines corroborate.
                string mineSummary = BundleBaker.TextureSummary(bundlePath, GateAsset);
                bool srgbOk = mineSummary.Contains("colorSpace=1");
                if (!srgbOk) failures++;
                log.Append(srgbOk ? "X1b PASS" : "X1b FAIL")
                   .Append(" the texture this tool bakes declares colorSpace=1 (sRGB), the same as the")
                   .Append(" shipped texture X2 measures; anything else renders washed out in a LINEAR")
                   .Append(" project: ").Append(mineSummary).Append('\n');

                Texture2D back = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                back.hideFlags = HideFlags.HideAndDontSave;
                try
                {
                    bool loaded = ImageConversion.LoadImage(back, File.ReadAllBytes(pngPath));
                    // Compared as PIXELS, not as raw bytes: LoadImage picks the texture layout it
                    // likes (it handed back ARGB32 here), so a raw-byte compare would fail on
                    // channel ORDER while every pixel was in fact correct.
                    Color32[] got = loaded ? back.GetPixels32() : null;
                    int diff = Diff(want, got);
                    bool ok = loaded && back.width == GateSize && back.height == GateSize && diff < 0;
                    if (!ok) failures++;
                    log.Append(ok ? "X1 PASS" : "X1 FAIL")
                       .Append(" extract -> png -> re-import IS the authored image: loaded=").Append(loaded)
                       .Append(" size=").Append(back.width).Append('x').Append(back.height)
                       .Append(" wanted ").Append(GateSize).Append('x').Append(GateSize)
                       .Append(" px=").Append(got == null ? -1 : got.Length)
                       .Append('/').Append(want.Length / 4)
                       .Append(diff < 0 ? " every channel of every pixel identical" : " FIRST DIFFERENCE at pixel " + diff +
                               " wanted " + Show(want, diff) + " got " + (got == null ? "(none)" : got[diff].ToString()))
                       .Append(" reimportFmt=").Append(back.format)
                       .Append(" src=").Append(how)
                       .Append(" png=").Append(new FileInfo(pngPath).Length).Append("B\n");
                }
                finally { UnityEngine.Object.DestroyImmediate(back); }
            }
            catch (Exception ex)
            {
                log.Append("X1 VOID threw: ").Append(ex.Message).Append('\n');
                failures++;
            }

            // ---- X2: a real shipped, GPU-compressed texture against the file's own metadata
            string shipped = BakeSelfCheck.ShippedBundlePath(ShippedBundle);
            string shippedPng = Path.Combine(OutDir(ShippedBundle), ShippedTexture + ".png");
            if (File.Exists(shippedPng)) File.Delete(shippedPng);
            if (!File.Exists(shipped))
            {
                log.Append("X2 VOID no ").Append(shipped).Append('\n');
                failures++;
            }
            else
            {
                try
                {
                    string summary = BundleBaker.TextureSummary(shipped, ShippedTexture);
                    string how = TextureToPng(shipped, ShippedTexture, shippedPng);

                    Texture2D back = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                    back.hideFlags = HideFlags.HideAndDontSave;
                    try
                    {
                        bool loaded = ImageConversion.LoadImage(back, File.ReadAllBytes(shippedPng));
                        string got = "w=" + back.width + " h=" + back.height;
                        bool ok = loaded && summary.StartsWith(got + " ", StringComparison.Ordinal);
                        if (!ok) failures++;
                        log.Append(ok ? "X2 PASS" : "X2 FAIL")
                           .Append(" the decoded png of shipped '").Append(ShippedTexture)
                           .Append("' IS the size the serialized file declares: png ").Append(got)
                           .Append(" | file ").Append(summary)
                           .Append(" loaded=").Append(loaded)
                           .Append(" src=").Append(how)
                           .Append(" bytes=").Append(new FileInfo(shippedPng).Length).Append("B\n");
                    }
                    finally { UnityEngine.Object.DestroyImmediate(back); }
                }
                catch (Exception ex)
                {
                    log.Append("X2 VOID threw: ").Append(ex.Message).Append('\n');
                    failures++;
                }
            }

            // ---- X3: discovery finds the thing X2 just extracted, by name, in the shipped file
            if (!File.Exists(shipped))
            {
                log.Append("X3 VOID no ").Append(shipped).Append('\n');
                failures++;
            }
            else
            {
                try
                {
                    string listing = BundleBaker.ListReport(shipped, "Texture2D", ShippedTexture, 10);
                    bool ok = listing.Contains("Texture2D " + ShippedTexture + " ");
                    if (!ok) failures++;
                    log.Append(ok ? "X3 PASS" : "X3 FAIL")
                       .Append(" ct_list names the extracted asset in ").Append(ShippedBundle)
                       .Append(": ").Append(listing.Replace('\n', '|')).Append('\n');
                }
                catch (Exception ex)
                {
                    log.Append("X3 VOID threw: ").Append(ex.Message).Append('\n');
                    failures++;
                }
            }

            return log.Append("ct_extract: ").Append(failures).Append(" FAILURE(S)").ToString();
        }

        /// <summary>
        /// The authored image X1 asserts identity against: a distinct value in every channel of every
        /// pixel, so a decoder that dropped alpha, swapped channels or flipped rows would differ.
        /// </summary>
        private static byte[] Probe()
        {
            byte[] px = new byte[GateSize * GateSize * 4];
            for (int y = 0; y < GateSize; y++)
                for (int x = 0; x < GateSize; x++)
                {
                    int i = (y * GateSize + x) * 4;
                    px[i] = (byte)(x * 31 + 1);           // r varies across
                    px[i + 1] = (byte)(y * 31 + 2);       // g varies down
                    px[i + 2] = (byte)(x * y + 3);        // b varies with both
                    px[i + 3] = (byte)(255 - y * 17);     // a is never a constant 255
                }
            return px;
        }

        /// <summary>Index of the first differing PIXEL, or -1 when every channel of every one matches.</summary>
        private static int Diff(byte[] wantRgba32, Color32[] got)
        {
            if (got == null || got.Length != wantRgba32.Length / 4) return 0;
            for (int p = 0; p < got.Length; p++)
                if (got[p].r != wantRgba32[p * 4] || got[p].g != wantRgba32[p * 4 + 1] ||
                    got[p].b != wantRgba32[p * 4 + 2] || got[p].a != wantRgba32[p * 4 + 3]) return p;
            return -1;
        }

        private static string Show(byte[] rgba32, int pixel)
        {
            int i = pixel * 4;
            return i + 3 < rgba32.Length
                ? "RGBA(" + rgba32[i] + ", " + rgba32[i + 1] + ", " + rgba32[i + 2] + ", " + rgba32[i + 3] + ")"
                : "(out of range)";
        }
    }
}
