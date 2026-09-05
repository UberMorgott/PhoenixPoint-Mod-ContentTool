using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Morgott.ContentTool.Project
{
    /// <summary>
    /// Is the patched copy in the player's AppData still the copy this project, this game and this
    /// ContentTool would produce today?
    ///
    /// THE DEFECT THIS REPLACES. Route7.ApplyProject decided a re-bake was unnecessary when every
    /// declared .bundle merely EXISTED in the patched folder. Existence is not currency: the player's
    /// game updates and the shipped bundle the copy was cloned from changes underneath it; the mod
    /// updates and its manifest now names a different asset; ContentTool's own bake format changes.
    /// In all three the stale copy is served forever, silently, and the only symptom is content that
    /// looks like the version before last - or a bundle Unity refuses because the identity it was
    /// cloned from is gone.
    ///
    /// THE KEY IS THE FIX. It is written beside the copies when a bake succeeds, and read back
    /// before the next apply. The three inputs are the three ways the answer can change:
    ///   1. the ContentTool FORMAT VERSION - our own output changed, nothing on disk did;
    ///   2. the PROJECT - ppcontent.json and every source file under Content\ (path, size, mtime);
    ///   3. the SHIPPED SOURCE BUNDLES - the game's own files the copies were cloned from.
    /// Sizes and timestamps rather than a content hash for 2 and 3 on purpose: the shipped bundles
    /// run to hundreds of megabytes and this is decided on the toggle's own thread, every enable. A
    /// game update rewrites the file, so it moves both; ppcontent.json is small enough to hash and is
    /// hashed, so an edit that keeps the size is caught.
    /// </summary>
    internal static class PatchCache
    {
        /// <summary>
        /// Bumped whenever ContentTool's bake output changes shape, which forces every player's cached
        /// copy to be rebuilt on their next enable. Was implicitly 1 - the era with no key at all,
        /// where the answer was "the files exist".
        /// </summary>
        internal const int FormatVersion = 2;

        /// <summary>Beside the copies it describes, in the mod's own AppData folder - never in the
        /// game installation (M2), and never inside the package (it is the player's, not the author's).</summary>
        private const string KeyFile = "ct-cache.key";

        internal static string Key(string projectRoot, IList<string> shippedBundles)
        {
            return Key(projectRoot, shippedBundles, FormatVersion);
        }

        /// <summary>The version is a PARAMETER so a bump can be measured rather than believed.</summary>
        internal static string Key(string projectRoot, IList<string> shippedBundles, int formatVersion)
        {
            StringBuilder d = new StringBuilder("contenttool-patch-cache v").Append(formatVersion).Append('\n');

            string manifest = projectRoot == null ? null : Path.Combine(projectRoot, "ppcontent.json");
            d.Append("manifest ")
             .Append(manifest != null && File.Exists(manifest)
                     ? Sha1.Hex(File.ReadAllBytes(manifest)) : "(missing)")
             .Append('\n');

            List<string> sources = new List<string>();
            string content = projectRoot == null ? null : Path.Combine(projectRoot, "Content");
            if (content != null && Directory.Exists(content))
            {
                int cut = content.TrimEnd('\\', '/').Length + 1;
                foreach (string f in Directory.GetFiles(content, "*", SearchOption.AllDirectories))
                    sources.Add(Stamp(f.Substring(cut).Replace('\\', '/').ToLowerInvariant(), f));
            }
            sources.Sort(StringComparer.Ordinal);
            foreach (string s in sources) d.Append("source ").Append(s).Append('\n');

            List<string> shipped = new List<string>();
            if (shippedBundles != null)
                foreach (string b in shippedBundles)
                    if (!string.IsNullOrEmpty(b))
                        shipped.Add(Stamp(Path.GetFileName(b).ToLowerInvariant(), b));
            shipped.Sort(StringComparer.Ordinal);
            foreach (string s in shipped) d.Append("shipped ").Append(s).Append('\n');

            return Sha1.Hex(Encoding.UTF8.GetBytes(d.ToString()));
        }

        /// <summary>Does the cached copy in <paramref name="patchedDir"/> answer to this key? A folder
        /// written by a ContentTool that had no key at all has no file here and is therefore STALE,
        /// which is the correct answer for it.</summary>
        internal static bool Fresh(string patchedDir, string key)
        {
            if (string.IsNullOrEmpty(patchedDir) || string.IsNullOrEmpty(key)) return false;
            string file = Path.Combine(patchedDir, KeyFile);
            return File.Exists(file) && File.ReadAllText(file).Trim() == key;
        }

        /// <summary>Where the receipt for these copies lives. There is no <c>Write</c> here any more: the
        /// receipt is written by <see cref="Morgott.ContentTool.Bake.Publication"/>, LAST, in the same
        /// ordered step that invalidates the old one and publishes the copies - and a second writer beside
        /// it would be a second way for a receipt to precede the bytes it vouches for. The path is all this
        /// class still owns, because <see cref="Fresh"/> is what reads it back.</summary>
        internal static string KeyPath(string patchedDir)
        {
            return string.IsNullOrEmpty(patchedDir) ? null : Path.Combine(patchedDir, KeyFile);
        }

        /// <summary>
        /// Which GAME INSTALLATION a patched copy belongs to, as a folder name.
        ///
        /// persistentDataPath is per USER and per PRODUCT, never per install, so two Phoenix Points
        /// on one machine (a Steam copy and a second test instance) shared one Patched\&lt;modId&gt;
        /// folder and overwrote each other's hundreds of megabytes on every enable. The key above
        /// cannot separate them either: after the same Steam update both installs' shipped bundles
        /// carry the same size and mtime, so the OTHER install's copy reads FRESH. The install's own
        /// path is the only thing that tells them apart, hashed short because it becomes a segment.
        /// </summary>
        internal static string InstallTag(string installPath)
        {
            string p = (installPath ?? "").Replace('/', '\\').TrimEnd('\\').ToLowerInvariant();
            return Sha1.Hex(Encoding.UTF8.GetBytes(p)).Substring(0, TagLength);
        }

        internal const int TagLength = 8;

        /// <summary>
        /// Startup housekeeping: patched copies nobody owns any more are DELETED, not merely skipped.
        ///
        /// THE DEFECT THIS FIXES. A copy is hundreds of megabytes and was only ever written, never
        /// removed - install a content mod, remove it, and its bundles sit in the player's AppData
        /// for the life of the machine. Route7 and this file both merely stepped over them.
        ///
        /// TWO SWEEPS, because there are two ways a folder is orphaned:
        ///   1. at the ROOT, anything that is not an install tag - the flat pre-tag layout, which
        ///      nothing reads or writes any more;
        ///   2. inside THIS install's tag, any mod id <paramref name="liveModIds"/> does not name.
        /// Another install's tag dir is hex-shaped and therefore untouched by 1 and out of reach of
        /// 2, which is the whole point of the tag.
        ///
        /// SAFE BY CONSTRUCTION. A wrongly deleted entry costs one re-bake and nothing else - the
        /// bake runs from the player's own game files - and a locked file is skipped and named
        /// rather than fought. Callers run this BEFORE any bundle is installed for this session, so
        /// nothing that is deleted here can be loaded.
        ///
        /// ponytail: a mod merely switched OFF in the manager is not live and pays one re-bake when
        /// it comes back. Discover with an all-ON roster if that ever bites.
        /// </summary>
        internal static string Prune(string patchedRoot, string tag, ICollection<string> liveModIds)
        {
            if (string.IsNullOrEmpty(patchedRoot) || string.IsNullOrEmpty(tag)
                || liveModIds == null || !Directory.Exists(patchedRoot)) return null;

            HashSet<string> live = new HashSet<string>(liveModIds, StringComparer.OrdinalIgnoreCase);
            List<string> gone = new List<string>();
            List<string> kept = new List<string>();

            foreach (string dir in Directory.GetDirectories(patchedRoot))
                if (!IsTag(Path.GetFileName(dir))) Drop(dir, gone, kept);

            string mine = Path.Combine(patchedRoot, tag);
            if (Directory.Exists(mine))
                foreach (string dir in Directory.GetDirectories(mine))
                    if (!live.Contains(Path.GetFileName(dir))) Drop(dir, gone, kept);

            if (gone.Count == 0 && kept.Count == 0) return null;
            return "ct_cache: deleted " + gone.Count + " orphaned patched copy(ies) under " + patchedRoot +
                   (gone.Count > 0 ? " [" + string.Join(", ", gone.ToArray()) + "]" : "") +
                   (kept.Count > 0 ? "; " + kept.Count + " in use or unreadable, left alone [" +
                                     string.Join(", ", kept.ToArray()) + "]" : "");
        }

        /// <summary>One entry removed. The KEY goes first, so a delete a locked file interrupts
        /// leaves a folder that reads STALE - a re-bake - and never a half-empty one that reads
        /// FRESH.</summary>
        private static void Drop(string dir, List<string> gone, List<string> kept)
        {
            string name = Path.GetFileName(dir);
            try
            {
                string key = Path.Combine(dir, KeyFile);
                if (File.Exists(key)) File.Delete(key);
                Directory.Delete(dir, true);
                gone.Add(name);
            }
            catch (Exception) { kept.Add(name); }
        }

        /// <summary>ponytail: shape, not a registry - a mod id of exactly eight lowercase hex
        /// characters would survive the legacy sweep. Mod ids are reverse-DNS; none can.</summary>
        private static bool IsTag(string name)
        {
            if (name == null || name.Length != TagLength) return false;
            foreach (char c in name)
                if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f'))) return false;
            return true;
        }

        /// <summary>name + size + mtime - what changes when a file changes, without reading it.</summary>
        private static string Stamp(string name, string path)
        {
            FileInfo fi = new FileInfo(path);
            return fi.Exists
                ? name + " " + fi.Length + " " + fi.LastWriteTimeUtc.Ticks
                : name + " (missing)";
        }
    }
}
