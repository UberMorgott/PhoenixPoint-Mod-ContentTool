using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Morgott.ContentTool.Bake
{
    /// <summary>
    /// Extraction for the assets that are not in a bundle at all. The cutscenes ship as LOOSE .webm
    /// under StreamingAssets\StreamableCopiedAssets\ (see <see cref="VideoCatalog"/>, which replaces
    /// them the same way), so "extracting" one is a copy - there is nothing to decode, and re-encoding
    /// a shipped clip would only lose quality on the way to an editor that reads .webm anyway.
    ///
    /// Carries no UnityEngine type, so the listing, the ambiguity discipline and the byte-identity of
    /// the copy are all proven offline; the caller supplies the root directory.
    /// </summary>
    internal static class LooseFiles
    {
        /// <summary>
        /// Every file under <paramref name="root"/> with that extension whose NAME contains the
        /// filter, as paths relative to the root with '/' separators.
        /// </summary>
        internal static List<string> Find(string root, string extension, string nameFilter)
        {
            List<string> hits = new List<string>();
            if (!Directory.Exists(root)) return hits;
            foreach (string f in Directory.GetFiles(root, "*" + extension, SearchOption.AllDirectories))
            {
                string name = Path.GetFileNameWithoutExtension(f);
                if (!string.IsNullOrEmpty(nameFilter) &&
                    name.IndexOf(nameFilter, StringComparison.OrdinalIgnoreCase) < 0) continue;
                hits.Add(f.Substring(root.Length).TrimStart('\\', '/').Replace('\\', '/'));
            }
            hits.Sort(StringComparer.OrdinalIgnoreCase);
            return hits;
        }

        /// <summary>The listing as console lines, capped, always stating the total.</summary>
        internal static string Report(string root, string extension, string nameFilter, int max)
        {
            if (!Directory.Exists(root)) return "VOID - no folder at " + root;
            List<string> hits = Find(root, extension, nameFilter);
            StringBuilder b = new StringBuilder();
            b.Append(hits.Count).Append(' ').Append(extension).Append(" file(s) match '")
             .Append(nameFilter ?? "").Append("' under ").Append(root);
            int n = Math.Min(hits.Count, max);
            for (int i = 0; i < n; i++)
                b.Append("\n  ").Append(Path.GetFileNameWithoutExtension(hits[i])).Append("  ").Append(hits[i]);
            if (hits.Count > n) b.Append("\n  ... ").Append(hits.Count - n).Append(" more (narrow the filter)");
            return b.ToString();
        }

        /// <summary>
        /// Copies the ONE file with that name out to <paramref name="outDir"/>, keeping its extension.
        /// Refuses both nothing and more than one, with the offenders printed - the same discipline
        /// <see cref="AssetIndex.FindUnique"/> applies inside a bundle. Measured: the 69 shipped .webm
        /// happen to have 69 distinct names today, so nothing exercises the >1 arm; it stays because a
        /// DLC that adds a second `PP_Intro` must be refused rather than picked between silently.
        /// Returns the path written.
        /// </summary>
        internal static string CopyOut(string root, string extension, string name, string outDir)
        {
            List<string> all = Find(root, extension, null);
            List<string> hits = new List<string>();
            foreach (string rel in all)
                if (string.Equals(Path.GetFileNameWithoutExtension(rel), name, StringComparison.OrdinalIgnoreCase))
                    hits.Add(rel);

            if (hits.Count == 0)
                throw new InvalidOperationException("no " + extension + " named '" + name + "' under " + root +
                    " (there are " + all.Count + "; list them first)");
            if (hits.Count > 1)
                throw new InvalidOperationException(hits.Count + " " + extension + " files are named '" + name +
                    "' (" + string.Join(", ", hits) + ") - refusing to guess which one to extract");

            string source = Path.Combine(root, hits[0].Replace('/', Path.DirectorySeparatorChar));
            string destination = Path.Combine(outDir, Path.GetFileName(source));
            Directory.CreateDirectory(outDir);
            File.Copy(source, destination, true);
            return destination;
        }
    }
}
