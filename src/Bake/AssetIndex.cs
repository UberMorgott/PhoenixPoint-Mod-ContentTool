using System;
using System.Collections.Generic;
using System.Text;
using AssetsTools.NET;
using AssetsTools.NET.Extra;

namespace Morgott.ContentTool.Bake
{
    /// <summary>Whether ONE object of a class carries a name, and when not, which way it failed -
    /// <see cref="Addressable.Absent"/> (no such object) is a fact about this file, while
    /// <see cref="Addressable.Ambiguous"/> (several) means the name is there and merely cannot be
    /// addressed.</summary>
    internal enum Addressable { Yes, Absent, Ambiguous }

    /// <summary>
    /// Reading a bundle's TABLE OF CONTENTS: what is in there, by type and by name. An author cannot
    /// extract what they cannot find, and the shipped names are nowhere else - the catalog addresses
    /// by GUID, not by asset name.
    ///
    /// Carries no UnityEngine type on purpose (same arrangement as <see cref="MeshFields"/>), so the
    /// listing and the ambiguity discipline are proven offline in tests\ObjCodecTests instead of
    /// costing a game launch. Callers own the <see cref="AssetsManager"/>; the two entry points that
    /// open a file for you live on <see cref="BundleBaker"/>.
    /// </summary>
    internal static class AssetIndex
    {
        /// <summary>One row per asset, most console-friendly first: what it is, what it is called.</summary>
        internal struct Row
        {
            internal string Type;
            internal string Name;
            internal uint Bytes;
            internal long PathId;

            public override string ToString()
            {
                return Type + " " + (Name.Length == 0 ? "(unnamed)" : Name) +
                       " " + Bytes + "B pathId=" + PathId;
            }
        }

        /// <summary>
        /// Every asset whose type and name contain the given substrings (case-insensitive, empty
        /// matches everything). The type is filtered BEFORE the name is read, because reading a name
        /// means deserializing the object - so `ct_list x Texture2D` costs one deserialize per
        /// texture, not one per asset in the file.
        /// </summary>
        internal static List<Row> Rows(AssetsManager m, AssetsFileInstance afile, string typeFilter, string nameFilter)
        {
            List<Row> rows = new List<Row>();
            foreach (AssetFileInfo i in afile.file.Metadata.AssetInfos)
            {
                string type = TypeName(i);
                if (!Contains(type, typeFilter)) continue;
                string name = NameOf(m, afile, i);
                if (!Contains(name, nameFilter)) continue;
                rows.Add(new Row { Type = type, Name = name, Bytes = i.ByteSize, PathId = i.PathId });
            }
            return rows;
        }

        /// <summary>
        /// The rows as console lines, capped - a real bundle carries thousands of assets and the
        /// console keeps 200 line objects. The header always states the TOTAL, so a capped listing
        /// can never read like a complete one.
        /// </summary>
        internal static string Report(AssetsManager m, AssetsFileInstance afile,
                                      string typeFilter, string nameFilter, int max)
        {
            List<Row> rows = Rows(m, afile, typeFilter, nameFilter);
            StringBuilder b = new StringBuilder();
            b.Append(rows.Count).Append(" of ").Append(afile.file.Metadata.AssetInfos.Count)
             .Append(" assets match type~'").Append(typeFilter ?? "").Append("' name~'")
             .Append(nameFilter ?? "").Append("'");
            int n = Math.Min(rows.Count, max);
            for (int i = 0; i < n; i++) b.Append('\n').Append("  ").Append(rows[i].ToString());
            if (rows.Count > n) b.Append('\n').Append("  ... ").Append(rows.Count - n).Append(" more (narrow the filters)");
            return b.ToString();
        }

        /// <summary>The serialized class of an asset, by the class id the file itself records.</summary>
        internal static string TypeName(AssetFileInfo i)
        {
            return Enum.IsDefined(typeof(AssetClassID), i.TypeId)
                ? ((AssetClassID)i.TypeId).ToString()
                : "class" + i.TypeId;
        }

        /// <summary>m_Name, or "" for the classes that carry none (Transform, MeshFilter, ...).</summary>
        internal static string NameOf(AssetsManager m, AssetsFileInstance afile, AssetFileInfo i)
        {
            try
            {
                AssetTypeValueField bf = m.GetBaseField(afile, i);
                if (bf == null) return "";
                AssetTypeValueField n = bf["m_Name"];
                return n == null || n.IsDummy ? "" : (n.AsString ?? "");
            }
            catch (Exception)
            {
                // A class the database cannot template is still a row in the listing - it just has
                // no name to show. Losing the whole listing to one such asset would be worse.
                return "";
            }
        }

        /// <summary>
        /// The one asset of that class with that m_Name. Refuses BOTH nothing and more than one:
        /// aln_fireworm ships two Materials both called 'ALN_Fireworm', and picking the first would
        /// patch an arbitrary one of them quietly (FINAL-PLAN 39.2's rule - an ambiguous name is an
        /// error, never a guess). The offenders are printed, so the refusal is actionable.
        /// </summary>
        internal static AssetFileInfo FindUnique(AssetsManager m, AssetsFileInstance afile,
                                                 AssetClassID cls, string assetName, string where)
        {
            List<long> offenders;
            AssetFileInfo found = Scan(m, afile, cls, assetName, out offenders);
            if (found == null) throw new InvalidOperationException(Why(cls, assetName, where, offenders));
            return found;
        }

        /// <summary>Every pathId carrying that name, and the object itself when there is EXACTLY one -
        /// null for both nothing and several, which the caller tells apart by the offender count.</summary>
        private static AssetFileInfo Scan(AssetsManager m, AssetsFileInstance afile, AssetClassID cls,
                                          string assetName, out List<long> offenders)
        {
            AssetFileInfo found = null;
            offenders = new List<long>();
            foreach (AssetFileInfo i in afile.file.Metadata.GetAssetsOfType(cls))
                if (NameOf(m, afile, i) == assetName) { found = i; offenders.Add(i.PathId); }
            return offenders.Count == 1 ? found : null;
        }

        /// <summary>The one sentence for a failed lookup, so the throw and the question share it.</summary>
        private static string Why(AssetClassID cls, string assetName, string where, List<long> offenders)
        {
            if (offenders.Count == 0) return "no " + cls + " named '" + assetName + "' in " + where;
            return offenders.Count + " " + cls + "s are named '" + assetName + "' (pathIds " +
                   string.Join(", ", offenders) + ") - refusing to guess which one to use";
        }

        /// <summary>
        /// Why <paramref name="assetName"/> cannot be addressed - the message <see cref="FindUnique"/>
        /// would have thrown with - or null when it can. Asked BEFORE a replacement so a row naming a
        /// target the shipped bundle does not hold is ONE counted refusal instead of an exception that
        /// abandons every remaining row and prints no summary at all.
        /// </summary>
        internal static string WhyNot(AssetsManager m, AssetsFileInstance afile, AssetClassID cls,
                                      string assetName, string where)
        {
            Addressable how;
            return WhyNot(m, afile, cls, assetName, where, out how);
        }

        /// <summary>
        /// The same answer WITH the reason classified. A caller searching SEVERAL bundles for one holder
        /// needs the two failures apart: "not in here" rules a bundle out, while "in here twice" leaves it a
        /// possible holder - dropping it and calling the next bundle the proven target is a guess. The
        /// sentence is for the author; no caller should have to sniff its wording to learn which happened.
        /// </summary>
        internal static string WhyNot(AssetsManager m, AssetsFileInstance afile, AssetClassID cls,
                                      string assetName, string where, out Addressable how)
        {
            List<long> offenders;
            if (Scan(m, afile, cls, assetName, out offenders) != null) { how = Addressable.Yes; return null; }
            how = offenders.Count == 0 ? Addressable.Absent : Addressable.Ambiguous;
            return Why(cls, assetName, where, offenders);
        }

        private static bool Contains(string haystack, string needle)
        {
            return string.IsNullOrEmpty(needle) ||
                   (haystack != null && haystack.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0);
        }
    }
}
