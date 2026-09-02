using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Morgott.ContentTool.Import;
using Morgott.ContentTool.IO;

namespace Morgott.ContentTool.Project
{
    /// <summary>One "replace" row, as a facade over the dictionary Json.Parse produced, so a member this
    /// version never heard of is still there when the file is written back. A known field whose value is
    /// NOT a string reads as ABSENT rather than throwing - exactly what ContentProject.Field:539 does with
    /// its "([^\"]*)" group: `"mesh": 5` is a row with no mesh, and the refusal that follows says so.</summary>
    internal sealed class ReplaceRow
    {
        /// <summary>The five kinds, in the order ContentProject.cs:404-408 counts them.</summary>
        internal static readonly string[] Kinds = { "texture", "material", "mesh", "clip", "video" };

        private static readonly string[] Known =
            { "bundle", "asset", "texture", "material", "mesh", "clip", "video" };

        private readonly Dictionary<string, object> row;

        internal ReplaceRow(Dictionary<string, object> row) { this.row = row; }

        /// <summary>The row's own tree, for the writer and for anything reading an unknown member.</summary>
        internal Dictionary<string, object> Tree => row;

        internal string Bundle => Str("bundle");
        internal string Asset => Str("asset");
        internal string Texture => Str("texture");
        internal string Material => Str("material");
        internal string Mesh => Str("mesh");
        internal string Clip => Str("clip");
        internal string Video => Str("video");

        /// <summary>texture|material|mesh|clip|video - NULL when the row selects none or several, which is
        /// half of the refusal at ContentProject.cs:404-416.</summary>
        internal string Kind
        {
            get
            {
                string found = null;
                foreach (string kind in Kinds)
                {
                    if (string.IsNullOrEmpty(Str(kind))) continue;
                    if (found != null) return null;
                    found = kind;
                }
                return found;
            }
        }

        /// <summary>V6. Tolerated on the READ side (a non-string reads as absent, as today), refused on
        /// the WRITE side: a row nothing can read is not one this tool hands back as if it were one.
        /// JSON null counts - `"mesh": null` is a mesh the file DECLARES and no reader can use.</summary>
        internal bool HasNonStringField()
        {
            foreach (string key in Known)
            {
                object value;
                if (row.TryGetValue(key, out value) && !(value is string)) return true;
            }
            return false;
        }

        private string Str(string key)
        {
            object value;
            return row.TryGetValue(key, out value) ? value as string : null;
        }
    }

    /// <summary>A typed facade over a PARSED ppcontent.json tree. Not a model of the file: the Dictionary
    /// and List Json.Parse returned ARE the state, so unknown keys, key order and number spelling survive
    /// whatever this class does. Parse is the tolerant entry (Package holds text, not a path, and may be
    /// handed a manifest with no "id"); ManifestFile.Load is the strict one.</summary>
    internal sealed class Manifest
    {
        /// <summary>Same cap ppcontent.json's other readers use. A manifest 64 levels deep is not one.</summary>
        internal const int MaxDepth = 64;

        /// <summary>V3. The design's E-table gives it no id, so this is the plan's own sentence.</summary>
        internal const string NotAnArray =
            "ppcontent.json's \"replace\" must be an ARRAY OF ROWS - a value of any other shape declares " +
            "nothing this tool can read or write";

        private readonly Dictionary<string, object> root;
        private readonly List<ReplaceRow> rows = new List<ReplaceRow>();
        private readonly List<ReplaceRow> pending = new List<ReplaceRow>();

        private Manifest(Dictionary<string, object> root)
        {
            this.root = root;
            object value;
            if (!root.TryGetValue("replace", out value) || value == null) return;
            List<object> array = value as List<object>;
            if (array == null) throw new InvalidDataException(NotAnArray);
            foreach (object item in array)
            {
                Dictionary<string, object> members = item as Dictionary<string, object>;
                if (members == null) throw new InvalidDataException(NotAnArray);
                rows.Add(new ReplaceRow(members));
            }
        }

        internal static Manifest Parse(string text) { return ParseFor(text, "ppcontent.json"); }

        /// <summary>E1. Json.Fail throws an ImportRefusedException worded for a GLB re-export
        /// (Json.cs, moved from GlbReader.cs:2440), so both entry points catch FormatException and rethrow
        /// the one exception a manifest caller can act on.</summary>
        /// <param name="what">"ppcontent.json", or "'&lt;path&gt;'" from ManifestFile.Load.</param>
        internal static Manifest ParseFor(string text, string what)
        {
            object parsed;
            try { parsed = Json.Parse(text, MaxDepth); }
            catch (FormatException bad)
            {
                throw new InvalidDataException(what + " is not valid JSON: " + bad.Message, bad);
            }
            Dictionary<string, object> tree = parsed as Dictionary<string, object>;
            if (tree == null)
                throw new InvalidDataException(what + " is not valid JSON: its root is not an object");
            return new Manifest(tree);
        }

        internal string Id => Str("id");
        internal string Bundle => Str("bundle");
        internal string Loop => Str("loop");
        internal string Play => Str("play");

        internal double? Scale
        {
            get
            {
                object value;
                return root.TryGetValue("scale", out value) && value is double number ? (double?)number : null;
            }
        }

        /// <summary>The raw tree, kept for round-trip. Callers READ it; the file's own bytes are what
        /// ManifestFile writes, never a reserialization of this.</summary>
        internal IDictionary<string, object> Root => root;

        /// <summary>Existing rows plus anything AddMeshReplacement queued.</summary>
        internal IReadOnlyList<ReplaceRow> Replace => rows;

        /// <summary>Rows added in memory and not yet spliced into the file.</summary>
        internal IReadOnlyList<ReplaceRow> Pending => pending;

        /// <summary>Whether the file SAYS the key, as opposed to saying it empty - the distinction
        /// ParseReplace's "declares but no complete entry" sentence turns on.</summary>
        internal bool Declares(string key) { return root.ContainsKey(key); }

        /// <summary>Queue ONE mesh row. Add only - editing or removing a row is design §2, and the wizard
        /// needs neither. The row is a flat object of three string members, so the in-game JsonUtility read
        /// of the root scalars (ContentProject.cs:287, :303) is unaffected. "asset" goes on VERBATIM:
        /// shipped names are folded nowhere.</summary>
        internal ReplaceRow AddMeshReplacement(string bundle, string asset, string meshFile)
        {
            var tree = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                { "bundle", bundle }, { "asset", asset }, { "mesh", meshFile }
            };
            var added = new ReplaceRow(tree);
            rows.Add(added);
            pending.Add(added);
            return added;
        }

        /// <summary>V4/V5/V6/V7 over every row, existing and pending, before a byte moves. V4 and V5 are
        /// today's rule at ContentProject.cs:404-416 unchanged; V6 is new only because the read side can
        /// afford to treat `"mesh": 5` as an absent mesh and the WRITE side cannot hand the author back a
        /// file whose row nothing can read.</summary>
        /// <exception cref="InvalidDataException">E3 for a row, E4 for a duplicated target.</exception>
        internal void Validate()
        {
            var seen = new List<string>();
            foreach (ReplaceRow row in rows)
            {
                string kind = row.Kind;
                bool needsBundle = kind != "video";
                if (kind == null || row.HasNonStringField() ||
                    (needsBundle && (string.IsNullOrEmpty(row.Bundle) || string.IsNullOrEmpty(row.Asset))))
                    throw new InvalidDataException(RowRefusal(row));

                // A "video" row with no "asset" ADDS a clip rather than replacing one, so two of them are
                // two additions, not a collision. Only a NAMED target can be claimed twice.
                if (string.IsNullOrEmpty(row.Asset) || string.IsNullOrEmpty(row.Bundle)) continue;
                // Lowercased rather than compared with OrdinalIgnoreCase because List<string>.Contains has
                // no comparer overload; the fold is the one ProjectBake.cs:1534 uses for bundles.
                string key = row.Bundle.ToLowerInvariant() + "\u0000" + row.Asset + "\u0000" + kind;
                if (seen.Contains(key))
                    throw new InvalidDataException(
                        "ppcontent.json already replaces \"" + row.Asset + "\" in \"" + row.Bundle +
                        "\" with a " + kind + ", so a second row for the same target was NOT written - " +
                        "edit the existing row instead");
                seen.Add(key);
            }
        }

        /// <summary>E3, the SENTENCE verbatim from ContentProject.cs:419-422. The row inside it is spelled
        /// by JsonWriter from the PARSED row rather than by a raw regex match, so its spacing and key order
        /// may differ from the file's - and a nested member shows up in the sentence at all, which the old
        /// "\{[^{}]*\}" match could never manage (design §7).</summary>
        internal static string RowRefusal(ReplaceRow row)
        {
            return "\"replace\" row REFUSED: every entry needs exactly one of \"texture\", \"material\", " +
                   "\"mesh\", \"clip\" or \"video\", plus \"bundle\" and \"asset\" for everything but " +
                   "\"video\" (a \"video\" entry with no \"asset\" ADDS a new clip); got " +
                   new JsonWriter().Val(row.Tree).ToString() +
                   " - SKIPPED, this project's other rows still bake";
        }

        private string Str(string key)
        {
            object value;
            return root.TryGetValue(key, out value) ? value as string : null;
        }
    }

    /// <summary>
    /// The FILE behind a Manifest: raw bytes, BOM, newline style, a SHA-256 of what was read, and the
    /// [start, end) span of every ROOT member's value. Save splices into ONE span and copies every other
    /// byte verbatim, so a whole-tree reserialization - which would lose the BOM, the indentation, the key
    /// order, the number spelling and every unknown key, Dictionary insertion order not being contractual
    /// (GlbDocument.cs:22) - never happens.
    /// SAVE ONCE, THEN RELOAD: after a successful Save the file no longer matches the fingerprint this
    /// instance holds, so a second Save refuses with E5 by construction.
    /// </summary>
    internal sealed class ManifestFile
    {
        /// <summary>[Start, End) of one root member's VALUE, trailing whitespace excluded - Save needs the
        /// exact index of the array's closing ']'. Both are CHAR offsets into the DECODED text, and the BOM
        /// is not part of that text - Load strips those three bytes before decoding and re-emits them from
        /// the `bom` flag - so a char offset is a byte offset only while the file stays ASCII.</summary>
        private sealed class Span { internal int Start; internal int End; }

        private readonly string text;
        private readonly string sha;
        private readonly bool bom;
        private readonly string newline;
        private readonly Dictionary<string, Span> members;
        private readonly int rootClose;

        private ManifestFile(string path, string text, string sha, bool bom, string newline,
                             Dictionary<string, Span> members, int rootClose, Manifest manifest)
        {
            Path = path; this.text = text; this.sha = sha; this.bom = bom; this.newline = newline;
            this.members = members; this.rootClose = rootClose; Manifest = manifest;
        }

        internal string Path { get; }
        internal Manifest Manifest { get; }

        /// <exception cref="InvalidDataException">E1 (not UTF-8, not JSON, or a root that is not an
        /// object), E2 (no "id"/"bundle") or E8 (two root keys that decode alike). Nothing is written on
        /// any path through this method.</exception>
        internal static ManifestFile Load(string path)
        {
            byte[] bytes = File.ReadAllBytes(path);
            bool bom = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
            int from = bom ? 3 : 0;
            string text;
            try
            {
                // THROW ON INVALID, not replace: a permissive decode turns a byte this reader does not
                // understand into U+FFFD, and Save would then write that replacement character back over
                // whatever the author actually had there.
                text = new UTF8Encoding(false, true).GetString(bytes, from, bytes.Length - from);
            }
            catch (DecoderFallbackException bad)
            {
                throw new InvalidDataException("'" + path + "' is not valid JSON: " + bad.Message, bad);
            }

            Manifest manifest = Manifest.ParseFor(text, "'" + path + "'");
            // E2, the sentence ContentProject.cs:289 and :305 already say.
            if (string.IsNullOrEmpty(manifest.Id) || string.IsNullOrEmpty(manifest.Bundle))
                throw new InvalidDataException("ppcontent.json needs both \"id\" and \"bundle\"");

            int close;
            Dictionary<string, Span> members = Members(text, path, out close);
            string newline = text.IndexOf("\r\n", StringComparison.Ordinal) >= 0 ? "\r\n" : "\n";
            return new ManifestFile(path, text, Sha256(bytes), bom, newline, members, close, manifest);
        }

        internal static string Sha256(byte[] bytes)
        {
            using (SHA256 hash = SHA256.Create())
            {
                var spelled = new StringBuilder(64);
                foreach (byte b in hash.ComputeHash(bytes)) spelled.Append(b.ToString("x2"));
                return spelled.ToString();
            }
        }

        /// <summary>Splice every pending row into the "replace" value span and commit. Everything outside
        /// that span - a nested map inside an existing row included - is byte-identical by construction.</summary>
        /// <exception cref="InvalidDataException">E3/E4 from Validate, or E6 when what this method produced
        /// does not re-read. Nothing is written on either path.</exception>
        /// <exception cref="IOException">E5, the file changed on disk since Load.</exception>
        internal void Save()
        {
            Manifest.Validate();
            if (Manifest.Pending.Count == 0) return;
            string produced = Splice();

            // E6: re-read what is about to be written, through the same reader and the same rules.
            try { Manifest.ParseFor(produced, "'" + Path + "'").Validate(); }
            catch (Exception)
            {
                throw new InvalidDataException("the edited ppcontent.json did not re-read as valid JSON, " +
                                               "so the file on disk was NOT touched");
            }

            // E5, immediately before the commit: the last moment a concurrent edit is still recoverable by
            // the author simply reloading. A file DELETED since Load is that same case - the destination is
            // no longer what was read - so it is refused with E5 rather than escaping as the raw
            // FileNotFoundException, which names no remedy and reads like a bug in this tool.
            string now;
            try { now = Sha256(File.ReadAllBytes(Path)); }
            catch (FileNotFoundException) { now = null; }
            catch (DirectoryNotFoundException) { now = null; }
            if (!string.Equals(now, sha, StringComparison.Ordinal))
                throw new IOException("'" + Path + "' changed on disk since it was loaded, so nothing was " +
                                      "written - reload it and add the row again");

            // Encoding back is lossless without a strict encoder: every char in `text` came out of the
            // STRICT decode in Load, and the spliced row is JsonWriter output.
            byte[] body = new UTF8Encoding(false).GetBytes(produced);
            byte[] bytes = body;
            if (bom)
            {
                bytes = new byte[body.Length + 3];
                bytes[0] = 0xEF; bytes[1] = 0xBB; bytes[2] = 0xBF;
                Buffer.BlockCopy(body, 0, bytes, 3, body.Length);
            }
            AtomicFile.Write(Path, bytes, Path + ".bak");
        }

        private string Splice()
        {
            var added = new StringBuilder();
            foreach (ReplaceRow row in Manifest.Pending)
            {
                if (added.Length > 0) added.Append(',').Append(newline);
                added.Append(new JsonWriter().Val(row.Tree).ToString());
            }

            Span span;
            if (!members.TryGetValue("replace", out span))
            {
                // (c) no "replace" at all: as the LAST root member, inserted just past the last thing the
                // author wrote, so the comma lands on THEIR line rather than on one of its own. A file
                // with no final newline therefore ends "...]}" - accepted as written.
                int at = Trim(text, 0, rootClose);
                return text.Substring(0, at) + "," + newline + "  \"replace\": [" + newline + "    " +
                       added.ToString().Replace(newline, newline + "    ") + newline + "  ]" +
                       text.Substring(at);
            }

            if (span.End - span.Start == 4 &&
                string.CompareOrdinal(text, span.Start, "null", 0, 4) == 0)
            {
                // (d) `"replace": null`, which the Manifest ctor accepts as "no rows": there is no array to
                // splice INTO, so the value span becomes a freshly built one. Same indentation rule as the
                // empty-array arm, and every byte on either side of the span is copied as it stands.
                string outer = IndentOf(text, span.Start);
                string body = outer + "  ";
                return text.Substring(0, span.Start) + "[" + newline + body +
                       added.ToString().Replace(newline, newline + body) + newline + outer + "]" +
                       text.Substring(span.End);
            }

            int stop;
            int last = LastElement(text, span.Start, span.End, out stop);
            if (last < 0)
            {
                // (b) the array is empty or holds only whitespace: give it a body, one level in from
                // "replace" itself.
                string close = IndentOf(text, span.Start);
                string inner = close + "  ";
                return text.Substring(0, span.Start + 1) + newline + inner +
                       added.ToString().Replace(newline, newline + inner) + newline + close +
                       text.Substring(span.End - 1);
            }

            // (a) insert immediately AFTER the last existing row's last byte, indented exactly like that
            // row. Everything from there on - the author's own whitespace and the closing ']' - is copied
            // unchanged rather than regenerated.
            string indent = IndentOf(text, last);
            return text.Substring(0, stop) + "," + newline + indent +
                   added.ToString().Replace(newline, newline + indent) + text.Substring(stop);
        }

        /// <summary>Where the LAST element of the array spanning [start, end) begins, or -1 when it holds
        /// none; <paramref name="stop"/> comes back as the index just PAST that element. Walks the array's
        /// INTERIOR only, so the outer brackets need no special case, and is string-aware for the same
        /// reason Members is.</summary>
        private static int LastElement(string text, int start, int end, out int stop)
        {
            int last = -1, from = -1, depth = 0;
            stop = -1;
            for (int i = start + 1; i < end - 1; i++)
            {
                char c = text[i];
                if (c == '"')
                {
                    if (depth == 0 && from < 0) from = i;
                    i = EndOfString(text, i);
                    continue;
                }
                if (c == '{' || c == '[')
                {
                    if (depth == 0 && from < 0) from = i;
                    depth++;
                    continue;
                }
                if (c == '}' || c == ']') { depth--; continue; }
                if (depth == 0 && c == ',')
                {
                    if (from >= 0) { last = from; stop = Trim(text, from, i); }
                    from = -1;
                    continue;
                }
                if (depth == 0 && from < 0 && !IsSpace(c)) from = i;
            }
            if (from >= 0) { last = from; stop = Trim(text, from, end - 1); }
            return last;
        }

        /// <summary>The whitespace run opening the line <paramref name="at"/> sits on.</summary>
        private static string IndentOf(string text, int at)
        {
            int line = text.LastIndexOf('\n', Math.Max(0, at - 1));
            var indent = new StringBuilder();
            for (int i = line + 1; i < at && IsSpace(text[i]); i++) indent.Append(text[i]);
            return indent.ToString();
        }

        /// <summary>ONE forward pass over the ROOT object: at depth 1 record each key and the [start, end)
        /// of its value; deeper, only keep the counter honest. A '{', '[' or ']' inside a STRING never
        /// moves it - that is CreatureManifest.Block:407's weakness, fixed rather than reused. The text has
        /// already been through Json.Parse, so this pass never has to refuse malformed JSON - only V9.</summary>
        private static Dictionary<string, Span> Members(string text, string path, out int close)
        {
            var spans = new Dictionary<string, Span>(StringComparer.Ordinal);
            int depth = 0, valueStart = -1;
            string key = null;
            close = -1;
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (c == '}' || c == ']')
                {
                    depth--;
                    if (depth != 0) continue;
                    if (key != null && valueStart >= 0)
                        Record(spans, key, valueStart, Trim(text, valueStart, i), path);
                    close = i;
                    return spans;
                }
                if (c == '{' || c == '[') { depth++; continue; }
                if (c == '"')
                {
                    int quote = i;
                    i = EndOfString(text, i);
                    if (depth == 1 && key == null) key = Key(text, quote, i);
                    continue;
                }
                if (depth != 1) continue;
                if (c == ':' && key != null && valueStart < 0)
                {
                    valueStart = i + 1;
                    while (valueStart < text.Length && IsSpace(text[valueStart])) valueStart++;
                    continue;
                }
                if (c == ',' && key != null && valueStart >= 0)
                {
                    Record(spans, key, valueStart, Trim(text, valueStart, i), path);
                    key = null;
                    valueStart = -1;
                }
            }
            return spans;
        }

        /// <summary>The DECODED key. The scanner sees a LITERAL; the tree Json.Parse built holds the
        /// decoded name, and if the two disagree a key spelled with an escape becomes an invisible second
        /// member. Handing the literal - quotes included - back to Json.Parse means exactly one decoder
        /// decides what a key spells.</summary>
        private static string Key(string text, int openQuote, int closeQuote)
        {
            return (string)Json.Parse(text.Substring(openQuote, closeQuote - openQuote + 1), 1);
        }

        /// <summary>V9/E8. Two root keys that DECODE to one name cannot both be edited safely: the tree
        /// keeps one of them and the splice would land in the other one's span.</summary>
        private static void Record(Dictionary<string, Span> spans, string key, int start, int end,
                                   string path)
        {
            if (spans.ContainsKey(key))
                throw new InvalidDataException("'" + path + "' declares the root key \"" + key +
                                               "\" twice, so it cannot be edited safely - delete one of them");
            spans[key] = new Span { Start = start, End = end };
        }

        /// <summary>Index of the quote that CLOSES the string opening at <paramref name="at"/>.</summary>
        private static int EndOfString(string text, int at)
        {
            for (int i = at + 1; i < text.Length; i++)
            {
                if (text[i] == '\\') { i++; continue; }
                if (text[i] == '"') return i;
            }
            return text.Length - 1;
        }

        private static bool IsSpace(char c) { return c == ' ' || c == '\t' || c == '\r' || c == '\n'; }

        private static int Trim(string text, int from, int to)
        {
            while (to > from && IsSpace(text[to - 1])) to--;
            return to;
        }
    }
}
