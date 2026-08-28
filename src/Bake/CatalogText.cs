using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace Morgott.ContentTool.Bake
{
    /// <summary>
    /// The streamable Catalog.json as TEXT - every rule that decides what lands in the game's file,
    /// and not one UnityEngine type, which is the whole point: the same code the mod ships is
    /// compiled into tests\ObjCodecTests and proven against the REAL 69-row shipped catalog offline,
    /// instead of costing a game launch (the arrangement MeshFields/PrefabFields/SkinFields already
    /// use). <see cref="VideoCatalog"/> owns everything that needs the engine or the disk.
    /// </summary>
    internal static class CatalogText
    {
        /// <summary>One mod's claim on one catalog row.</summary>
        internal sealed class Rec
        {
            internal readonly string Mod, Key, Path;
            internal Rec(string m, string k, string p) { Mod = m; Key = k; Path = p; }
            public override string ToString() => "edit\t" + Mod + "\t" + Key + "\t" + Path;
        }

        // ---------------------------------------------------------------- catalog surgery

        /// <summary>One row of the catalog, and where its StreamingPath value sits in the text.</summary>
        internal struct Row
        {
            internal string Key, Path;
            internal int Open, Close;      // quote positions around the StreamingPath value
        }

        /// <summary>
        /// Every row, in file order. Read as text rather than through JsonUtility: a round trip would
        /// rewrite formatting and silently drop any field this tool does not model, and the game reads
        /// this file with its own reader.
        /// </summary>
        internal static List<Row> Rows(string json)
        {
            List<Row> rows = new List<Row>();
            int at = 0;
            while (true)
            {
                int ko, kc;
                string key = Value(json, "RuntimeKey", at, out ko, out kc);
                if (key == null) break;
                int po, pc;
                string path = Value(json, "StreamingPath", kc, out po, out pc);
                if (path == null) break;
                rows.Add(new Row { Key = key, Path = path, Open = po, Close = pc });
                at = pc;
            }
            return rows;
        }

        internal static string Value(string json, string field, int from, out int open, out int close)
        {
            open = close = -1;
            string f = "\"" + field + "\"";
            int i = json.IndexOf(f, from, StringComparison.Ordinal);
            if (i < 0) return null;
            i = json.IndexOf(':', i + f.Length);
            if (i < 0) return null;
            open = json.IndexOf('"', i + 1);
            if (open < 0) return null;
            close = json.IndexOf('"', open + 1);
            if (close < 0) return null;
            return json.Substring(open + 1, close - open - 1);
        }

        /// <summary>The StreamingPath a given RuntimeKey carries in that text, or null.</summary>
        internal static string PathOf(string json, string key)
        {
            foreach (Row r in Rows(json)) if (r.Key == key) return r.Path;
            return null;
        }

        /// <summary>
        /// The RuntimeKey of the single row whose StreamingPath (or its file name) the author named.
        /// Ambiguity is REFUSED with the offenders printed, never guessed.
        /// </summary>
        internal static string FindKey(string json, string asset, out string why)
        {
            why = null;
            if (string.IsNullOrEmpty(asset)) { why = "\"asset\" is empty"; return null; }
            string want = asset.Replace('\\', '/');
            List<Row> hit = new List<Row>();
            foreach (Row r in Rows(json))
                if (string.Equals(r.Path, want, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(Path.GetFileName(r.Path), want, StringComparison.OrdinalIgnoreCase))
                    hit.Add(r);

            if (hit.Count == 1) return hit[0].Key;
            if (hit.Count == 0) { why = "no catalog row names '" + asset + "'"; return null; }
            StringBuilder b = new StringBuilder("'" + asset + "' is ambiguous, " + hit.Count + " rows match:");
            foreach (Row r in hit) b.Append("\n  ").Append(r.Key).Append(" = ").Append(r.Path);
            why = b.ToString();
            return null;
        }

        /// <summary>Pristine + every surviving edit. Never edit-on-top-of-edit: that is what makes
        /// reverting one mod safe.</summary>
        internal static string Rebuild(string pristine, IEnumerable<Rec> recs)
        {
            string text = pristine;
            foreach (Rec r in recs) text = ApplyOne(text, r);
            return text;
        }

        /// <summary>
        /// One record, one row: the key the catalog already has is mutated IN PLACE (a replacement),
        /// the key it does not have is appended (an add). The lookup IS the mode - see the type
        /// comment - and either way the result goes through <see cref="Guard"/> before it lands.
        /// </summary>
        internal static string ApplyOne(string json, Rec rec)
        {
            foreach (Row r in Rows(json))
                if (r.Key == rec.Key)
                    return json.Substring(0, r.Open + 1) + rec.Path + json.Substring(r.Close);
            return Append(json, rec);
        }

        /// <summary>
        /// A brand-new row, spliced in after the last one. Text surgery for the same reason every
        /// other edit here is: a JsonUtility round trip would reformat the whole file and drop any
        /// field this tool does not model. "Collection" is READ off the file (the first row's - the
        /// base Videos folder, which is where a mod's clip lands) rather than spelled out here;
        /// runtime never reads it, only RuntimeKey and StreamingPath (StreamableAssetsCatalog.cs).
        /// </summary>
        internal static string Append(string json, Rec rec)
        {
            List<Row> rows = Rows(json);
            if (rows.Count == 0) throw new InvalidOperationException("the catalog has no rows to append to");
            int end = json.IndexOf('}', rows[rows.Count - 1].Close);
            if (end < 0) throw new InvalidOperationException("the catalog's last row is never closed");
            int o, c;
            string collection = Value(json, "Collection", 0, out o, out c);
            return json.Substring(0, end + 1) +
                   ",\n        {\n            \"Collection\": \"" + collection +
                   "\",\n            \"RuntimeKey\": \"" + rec.Key +
                   "\",\n            \"StreamingPath\": \"" + rec.Path +
                   "\"\n        }" + json.Substring(end + 1);
        }

        /// <summary>
        /// The RuntimeKey a mod's own clip gets. DERIVED, never random: the author pastes this string
        /// into a def, so it has to be the same on every machine and on every re-apply, or the def
        /// would point at nothing the second time. Shape is the shipped one - 32 lowercase hex, a
        /// Unity asset GUID - and MD5 is here as a name hash, not as a security primitive.
        /// A collision with a shipped key is refused at the call site; <see cref="Guard"/> refuses
        /// one with any other row unconditionally.
        /// </summary>
        internal static string KeyFor(string modId, string videoName)
        {
            using (MD5 h = MD5.Create())
                return BitConverter.ToString(h.ComputeHash(Encoding.UTF8.GetBytes(modId + "/" + videoName)))
                                   .Replace("-", "").ToLowerInvariant();
        }

        /// <summary>
        /// The first RuntimeKey that appears twice, or null. StreamableAssetsCatalog.cs:22 is
        /// ToDictionary(l =&gt; l.RuntimeKey) inside Awake - a duplicate does not degrade anything, it
        /// throws and the boot scene never comes up.
        /// </summary>
        internal static string DuplicateKey(string json)
        {
            HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (Row r in Rows(json)) if (!seen.Add(r.Key)) return r.Key;
            return null;
        }

        /// <summary>The refusal every write goes through, as a message. null = safe to land.</summary>
        internal static string Guard(string json)
        {
            List<Row> rows = Rows(json);
            if (rows.Count == 0) return "REFUSED: the rebuilt catalog has no rows at all";
            string dup = DuplicateKey(json);
            return dup == null ? null
                : "REFUSED: RuntimeKey '" + dup + "' would appear twice. StreamableAssetsCatalog." +
                  "InitializeCache does ToDictionary on that key - the game would throw in Awake and " +
                  "fail to boot. Nothing was written.";
        }

    }
}
