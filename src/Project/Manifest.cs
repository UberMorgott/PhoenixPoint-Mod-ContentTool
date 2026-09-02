using System;
using System.Collections.Generic;
using System.IO;
using Morgott.ContentTool.Import;

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

        private string Str(string key)
        {
            object value;
            return root.TryGetValue(key, out value) ? value as string : null;
        }
    }
}
