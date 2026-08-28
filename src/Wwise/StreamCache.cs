using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

namespace Morgott.ContentTool.Wwise
{
    /// <summary>
    /// The streamed-WEM half of packaging: the manifest the bake writes into the bundle, and the
    /// materialization that puts each &lt;mediaId&gt;.wem on disk at runtime.
    ///
    /// This exists only because native Wwise streaming reads FILES (FINAL-PLAN 6). It is not a
    /// content format, not a build cache and not a distribution layout - the .wem ships inside
    /// MyMod.bundle and nowhere else.
    ///
    /// Target is the MOD's own <c>&lt;modDir&gt;\WwiseAudio\</c> (ARM1, proven in rr_streamtest,
    /// donor commit dc5f583), which needs ONE setup-time AddBasePath because that directory is not
    /// a Wwise default. USER DECISION 2026-08-12, reversing the earlier persistentDataPath shape:
    /// a mod must be self-contained - deleting its folder removes its media - and the AppData root
    /// must not accumulate flat &lt;mediaId&gt;.wem from every mod built with this tool.
    /// Base-path SHADOWING stays rejected; this is one call for our own directory.
    ///
    /// Extraction MUST complete before LoadBankMemoryCopy: a streamed source resolves to a file at
    /// load time, and a missing one is simply unreachable.
    ///
    /// ponytail: line-based manifest, SHA-1, whole-file reads. Streams are a handful per mod and the
    /// largest shipped media is 54 MB; move to chunked hashing if a mod ever ships hundreds.
    /// </summary>
    internal static class StreamCache
    {
        /// <summary>Bumping this invalidates every cached .wem, whatever its hash says.</summary>
        private const int FormatVersion = 1;
        private const string Magic = "ctstream";

        /// <summary>Same folder name the donor's proven ARM1 used.</summary>
        private const string AudioFolder = "WwiseAudio";

        private static bool basePathAdded;

        /// <summary>The one place the on-disk shape is decided: &lt;modDir&gt;\WwiseAudio.</summary>
        internal static string TargetDir
        {
            get
            {
                string mod = ContentToolMain.ModDir;
                if (string.IsNullOrEmpty(mod))
                    throw new InvalidOperationException("ModEntry.Directory is empty; there is no mod folder to stream from");
                return Path.Combine(mod, AudioFolder);
            }
        }

        internal sealed class Entry
        {
            internal uint MediaId;
            internal string Asset;      // container name inside the bundle, written by the baker
            internal int Length;
            internal string Hash;
        }

        /// <summary>
        /// The manifest, itself a TextAsset in the bundle. Holds the container name the baker
        /// actually wrote, so the extractor never re-derives an asset path and never has a second
        /// spelling of it (FINAL-PLAN 4.3).
        /// </summary>
        internal static byte[] BuildManifest(IEnumerable<Entry> entries)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append(Magic).Append(' ').Append(FormatVersion).Append('\n');
            foreach (Entry e in entries)
                sb.Append(e.MediaId).Append('|').Append(e.Asset).Append('|')
                  .Append(e.Length).Append('|').Append(e.Hash).Append('\n');
            return Encoding.UTF8.GetBytes(sb.ToString());
        }

        /// <summary>An entry for <paramref name="wem"/>, hashed here so bake and runtime agree by construction.</summary>
        internal static Entry Describe(uint mediaId, string containerName, byte[] wem)
        {
            return new Entry { MediaId = mediaId, Asset = containerName, Length = wem.Length, Hash = Sha1(wem) };
        }

        internal static List<Entry> ParseManifest(byte[] manifest)
        {
            string[] lines = Encoding.UTF8.GetString(manifest).Split('\n');
            if (lines.Length == 0 || lines[0].Trim() != Magic + " " + FormatVersion)
                throw new InvalidDataException("stream manifest is not '" + Magic + " " + FormatVersion +
                                               "' but '" + (lines.Length == 0 ? "" : lines[0].Trim()) + "'");
            List<Entry> outp = new List<Entry>();
            for (int i = 1; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                if (line.Length == 0) continue;
                string[] f = line.Split('|');
                if (f.Length != 4) throw new InvalidDataException("malformed stream manifest line: " + line);
                outp.Add(new Entry
                {
                    MediaId = uint.Parse(f[0], CultureInfo.InvariantCulture),
                    Asset = f[1],
                    Length = int.Parse(f[2], CultureInfo.InvariantCulture),
                    Hash = f[3]
                });
            }
            return outp;
        }

        /// <summary>
        /// Materializes every stream the manifest lists into <see cref="TargetDir"/> as
        /// &lt;mediaId&gt;.wem, skipping the ones already on disk with the right length and hash.
        /// <paramref name="written"/> counts the files this call actually rewrote - a second run
        /// over an unchanged cache must report 0. Throws on a payload that does not match its
        /// manifest entry, or on a write that does not read back identical: a corrupted extraction
        /// must never be left for Wwise to fail on later, where it would read as an engine problem.
        /// </summary>
        internal static string Extract(AssetBundle bundle, string manifestAsset, out int written)
        {
            written = 0;
            TextAsset m = bundle.LoadAsset<TextAsset>(manifestAsset);
            if (m == null) throw new InvalidDataException("stream manifest '" + manifestAsset + "' is not in the bundle");

            List<Entry> entries = ParseManifest(m.bytes);
            string targetDir = TargetDir;
            Directory.CreateDirectory(targetDir);
            StringBuilder log = new StringBuilder();

            // The one AddBasePath call, made here so no caller can extract without it or load a bank
            // before it. Wwise keeps base paths for the session, so it only has to happen once; the
            // trailing separator is the form the proven ARM1 used.
            string basePath = "AddBasePath: already registered";
            if (!basePathAdded)
            {
                AKRESULT r = AkSoundEngine.AddBasePath(targetDir + Path.DirectorySeparatorChar);
                basePathAdded = r == AKRESULT.AK_Success;
                basePath = "AddBasePath(" + targetDir + Path.DirectorySeparatorChar + "): " + r;
            }

            foreach (Entry e in entries)
            {
                string path = Path.Combine(targetDir, e.MediaId + ".wem");
                if (File.Exists(path))
                {
                    byte[] have = File.ReadAllBytes(path);
                    if (have.Length == e.Length && Sha1(have) == e.Hash)
                    {
                        log.Append(e.MediaId).Append(": cached ");
                        continue;
                    }
                }

                TextAsset ta = bundle.LoadAsset<TextAsset>(e.Asset);
                if (ta == null) throw new InvalidDataException("packaged stream '" + e.Asset + "' is not in the bundle");
                byte[] wem = ta.bytes;
                if (wem.Length != e.Length || Sha1(wem) != e.Hash)
                    throw new InvalidDataException("packaged stream '" + e.Asset + "' is " + wem.Length +
                                                   " B, the manifest says " + e.Length + " B with a different hash");

                // Write through a temp file so a half-written .wem can never be picked up as the
                // real one; replacing is the last step and it is a single filesystem operation.
                string tmp = path + ".tmp";
                File.WriteAllBytes(tmp, wem);
                if (File.Exists(path)) File.Delete(path);
                File.Move(tmp, path);

                byte[] back = File.ReadAllBytes(path);
                if (back.Length != e.Length || Sha1(back) != e.Hash)
                    throw new IOException("extracted " + path + " does not read back identical to the packaged bytes");
                written++;
                log.Append(e.MediaId).Append(": wrote ").Append(back.Length).Append(" B ");
            }
            return entries.Count + " stream(s), " + written + " rewritten | " + basePath +
                   " | " + log.ToString().TrimEnd();
        }

        internal static string Sha1(byte[] b)
        {
            using (SHA1 sha = SHA1.Create()) return BitConverter.ToString(sha.ComputeHash(b)).Replace("-", "");
        }
    }
}
