using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Xml;

namespace Morgott.ContentTool.Wwise
{
    /// <summary>
    /// WHAT A SHIPPED SOUND IS CALLED. The 3105 loose .wem are named by their Wwise media ID and by
    /// nothing else, so a listing of them could only ever print numbers - an author looking for a taunt
    /// had no way to find one, and a substring filter matched nothing a human would type. The name map
    /// ships WITH THE GAME:
    /// StreamingAssets\Audio\GeneratedSoundBanks\Windows\SoundbanksInfo.xml (4.4 MB) - every media's
    /// Id -> ShortName, grouped by the SoundBank that carries it.
    ///
    /// Read off the GAME's file at runtime, never baked into the DLL. <see cref="IdIndex"/>
    /// deliberately ships the occupied ID SETS with the names stripped (a collision validator only ever
    /// asks "is this uint32 taken"), and carrying 7692 names as well would be dead weight plus a second
    /// copy of the map to fall out of step with a patch.
    ///
    /// Forward-only <see cref="XmlReader"/>: the file holds 33480 File elements to answer 7692
    /// questions - every media is repeated once per Event and again per bank - so a tree would be four
    /// times the memory for the same answer. Nothing here touches UnityEngine, so the parse is proven
    /// offline against a fixture.
    ///
    /// A MISSING OR UNPARSABLE FILE IS NOT AN ERROR. The map is then empty, <see cref="Why"/> says so,
    /// and every caller falls back to the numeric name - which is exactly what the tool printed before
    /// this existed.
    /// </summary>
    internal sealed class SoundbankNames
    {
        internal struct Media
        {
            internal string ShortName;   // as the XML spells it, e.g. "6_IND_Taunt_02.wav"
            internal string Bank;        // "" for a streamed media no bank claims
        }

        private readonly Dictionary<uint, Media> byId = new Dictionary<uint, Media>();

        /// <summary>Null when the map was read; otherwise why it was not, in one line.</summary>
        internal string Why { get; private set; }

        internal int Count { get { return byId.Count; } }

        private static string cachedPath;
        private static SoundbankNames cached;

        /// <summary>One parse per session: 4.4 MB is cheap once and silly per command.</summary>
        internal static SoundbankNames Cached(string xmlPath)
        {
            if (cached != null && string.Equals(cachedPath, xmlPath, StringComparison.OrdinalIgnoreCase))
                return cached;
            SoundbankNames n = Load(xmlPath);
            cachedPath = xmlPath;
            cached = n;
            return n;
        }

        /// <summary>Never throws: a game that ships no name map still lists and extracts, by number.</summary>
        internal static SoundbankNames Load(string xmlPath)
        {
            var n = new SoundbankNames();
            if (string.IsNullOrEmpty(xmlPath) || !File.Exists(xmlPath))
            {
                n.Why = "no SoundbanksInfo.xml at " + xmlPath;
                return n;
            }
            try
            {
                using (XmlReader r = XmlReader.Create(xmlPath, new XmlReaderSettings
                {
                    IgnoreComments = true,
                    IgnoreWhitespace = true,
                    DtdProcessing = DtdProcessing.Prohibit,
                    XmlResolver = null,
                }))
                    n.Read(r);
                if (n.byId.Count == 0) n.Why = "SoundbanksInfo.xml named no media";
            }
            catch (Exception ex) { n.Why = "SoundbanksInfo.xml unreadable: " + ex.Message; }
            return n;
        }

        /// <summary>
        /// The whole grammar this needs: a SoundBank's own ShortName is the first one inside it, and
        /// every later ShortName belongs to the File element that opened last. Elements named
        /// "ShortName" occur nowhere else in the schema, which is what makes the state this small.
        /// </summary>
        private void Read(XmlReader r)
        {
            string bank = "";
            uint pending = 0;
            bool haveFile = false, wantBankName = false;
            while (r.Read())
            {
                if (r.NodeType != XmlNodeType.Element) continue;
                switch (r.Name)
                {
                    case "SoundBank":
                        bank = ""; wantBankName = true; haveFile = false;
                        break;
                    case "StreamedFiles":
                        // Top-level: these media are streamed loose and no bank owns them yet. A bank
                        // that references one fills the column in later, through ReferencedStreamedFiles.
                        bank = ""; wantBankName = false; haveFile = false;
                        break;
                    case "File":
                        haveFile = uint.TryParse(r.GetAttribute("Id"), NumberStyles.None,
                                                 CultureInfo.InvariantCulture, out pending);
                        wantBankName = false;
                        break;
                    case "ShortName":
                        string v = r.ReadElementContentAsString();
                        if (wantBankName) { bank = v; wantBankName = false; }
                        else if (haveFile) { Add(pending, v, bank); haveFile = false; }
                        break;
                }
            }
        }

        /// <summary>First name wins; a bank column is filled the first time some bank claims the media.</summary>
        private void Add(uint id, string shortName, string bank)
        {
            Media m;
            if (byId.TryGetValue(id, out m))
            {
                if (string.IsNullOrEmpty(m.Bank) && !string.IsNullOrEmpty(bank))
                {
                    m.Bank = bank;
                    byId[id] = m;
                }
                return;
            }
            byId[id] = new Media { ShortName = shortName, Bank = bank ?? "" };
        }

        internal bool TryGet(uint id, out Media m) { return byId.TryGetValue(id, out m); }

        /// <summary>The display name - the ShortName without its ".wav" - or "" when unknown.</summary>
        internal string Name(uint id)
        {
            Media m;
            if (!byId.TryGetValue(id, out m) || string.IsNullOrEmpty(m.ShortName)) return "";
            return m.ShortName.EndsWith(".wav", StringComparison.OrdinalIgnoreCase)
                ? m.ShortName.Substring(0, m.ShortName.Length - 4) : m.ShortName;
        }

        /// <summary>The ShortName as the XML spells it (extension kept), or "" when unknown.</summary>
        internal string ShortName(uint id)
        {
            Media m;
            return byId.TryGetValue(id, out m) ? m.ShortName ?? "" : "";
        }

        internal string Bank(uint id)
        {
            Media m;
            return byId.TryGetValue(id, out m) ? m.Bank ?? "" : "";
        }

        /// <summary>The id a loose .wem's file name spells, or 0 when the name is not a number.</summary>
        internal static uint IdOf(string fileStem)
        {
            uint id;
            return uint.TryParse(fileStem, NumberStyles.None, CultureInfo.InvariantCulture, out id) ? id : 0u;
        }

        internal static bool Has(string hay, string needle)
        {
            return !string.IsNullOrEmpty(hay) && hay.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>
        /// The one filter rule both the listing and the bulk extract use: a case-insensitive substring
        /// against the sound's NAME, its id, or its bank. Matching the name is the whole point - the
        /// filter used to be the file name, which for these files is a number, so "confirm" matched
        /// nothing that exists.
        /// </summary>
        internal bool Matches(uint id, string fileStem, string filter)
        {
            if (string.IsNullOrEmpty(filter)) return true;
            if (Has(fileStem, filter)) return true;
            Media m;
            if (id == 0 || !byId.TryGetValue(id, out m)) return false;
            return Has(m.ShortName, filter) || Has(m.Bank, filter);
        }

        /// <summary>
        /// A file name for the decoded .wav: "&lt;ShortName&gt;__&lt;id&gt;", sanitised, or bare
        /// "&lt;id&gt;" when nothing names the media. The id stays on the end on purpose - two Wwise
        /// sources can share a ShortName, and the id is what every other command takes.
        /// </summary>
        internal static string WavName(string displayName, uint id, string fileStem)
        {
            string tail = id != 0 ? id.ToString(CultureInfo.InvariantCulture) : (fileStem ?? "unknown");
            if (string.IsNullOrEmpty(displayName)) return tail;
            var b = new StringBuilder();
            char[] bad = Path.GetInvalidFileNameChars();
            foreach (char c in displayName)
                b.Append(Array.IndexOf(bad, c) >= 0 ? '_' : c);
            string safe = b.ToString().Trim();
            if (safe.Length > 80) safe = safe.Substring(0, 80);
            return safe.Length == 0 ? tail : safe + "__" + tail;
        }

        private struct Row
        {
            internal uint Id;
            internal string Stem, Name, Bank;
            internal bool Loose;
        }

        /// <summary>
        /// Every media there is: what the XML names, plus what is on disk. The two sets do not nest -
        /// most named media live inside a .bnk and never appear as a file, and a handful of loose files
        /// (measured: 8) are named by no bank at all. A file on disk that no listing mentions is exactly
        /// the hole this command exists to close, so it gets a row too.
        /// </summary>
        private List<Row> Rows(IList<string> looseStems)
        {
            var loose = new HashSet<uint>();
            var rows = new List<Row>();
            if (looseStems != null)
                foreach (string stem in looseStems)
                {
                    uint id = IdOf(stem);
                    if (id != 0) loose.Add(id);
                    else rows.Add(new Row { Id = 0, Stem = stem, Name = "", Bank = "", Loose = true });
                }

            foreach (KeyValuePair<uint, Media> e in byId)
                rows.Add(new Row
                {
                    Id = e.Key,
                    Stem = e.Key.ToString(CultureInfo.InvariantCulture),
                    Name = Name(e.Key),
                    Bank = e.Value.Bank ?? "",
                    Loose = loose.Contains(e.Key),
                });
            foreach (uint id in loose)
                if (!byId.ContainsKey(id))
                    rows.Add(new Row
                    {
                        Id = id,
                        Stem = id.ToString(CultureInfo.InvariantCulture),
                        Name = "",
                        Bank = "",
                        Loose = true,
                    });
            return rows;
        }

        /// <summary>
        /// One row per media - the loose ones AND the in-bank ones. In-bank media cannot be extracted
        /// (they live inside a .bnk), but they are listed and MARKED anyway: the point of the listing is
        /// to find the id a NAME belongs to, and an answer that silently omits the 4587 sounds that are
        /// not loose is worse than a marked one.
        ///
        /// NOT CAPPED. The old 60-row cut made rows 61+ unreachable by anything but a luckier filter;
        /// the caller's console bound (Dev.ConsoleText.Bound) now shows what the pane can take and
        /// spills the whole listing to a file, so nothing is lost.
        /// </summary>
        /// <param name="looseStems">File names, no extension, of the loose .wem actually on disk.</param>
        internal string Report(IList<string> looseStems, string filter)
        {
            List<Row> rows = Rows(looseStems);
            var hits = new List<Row>();
            int nLoose = 0, nBank = 0;
            foreach (Row row in rows)
            {
                if (!Matches(row.Id, row.Stem, filter)) continue;
                hits.Add(row);
                if (row.Loose) nLoose++; else nBank++;
            }
            // Named first and alphabetically: a human reads this to FIND something, and the unnamed
            // tail is the part no filter word can ever reach anyway.
            hits.Sort(delegate (Row a, Row b)
            {
                bool na = string.IsNullOrEmpty(a.Name), nb = string.IsNullOrEmpty(b.Name);
                if (na != nb) return na ? 1 : -1;
                int c = string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
                return c != 0 ? c : a.Id.CompareTo(b.Id);
            });

            var s = new StringBuilder();
            s.Append(hits.Count).Append(" of ").Append(rows.Count).Append(" media match '")
             .Append(filter ?? "").Append("' - ").Append(nLoose).Append(" loose (extractable), ")
             .Append(nBank).Append(" in-bank (not extractable)");
            if (Why != null) s.Append(" - NO NAMES: ").Append(Why);
            foreach (Row row in hits)
                s.Append("\n  ").Append(row.Stem.PadRight(11))
                 .Append((row.Name.Length == 0 ? "(unnamed)" : row.Name).PadRight(34))
                 .Append((row.Bank.Length == 0 ? "-" : row.Bank).PadRight(26))
                 .Append(row.Loose ? "loose" : "in-bank");
            return s.ToString();
        }

        /// <summary>How many matching media are inside a .bnk - listed, findable, but not extractable.</summary>
        internal int InBankMatches(IList<string> looseStems, string filter)
        {
            int n = 0;
            foreach (Row row in Rows(looseStems))
                if (!row.Loose && Matches(row.Id, row.Stem, filter)) n++;
            return n;
        }

        /// <summary>
        /// The whole map as a .csv - every media, loose or not, with the .wav that was written for it.
        /// The listing is bounded by a console pane; this is not, which is what a spreadsheet or a
        /// script wants when it is looking for one sound among 7692.
        /// </summary>
        internal string Csv(IList<string> looseStems, IDictionary<uint, string> wavByMedia)
        {
            var s = new StringBuilder("id,shortName,bank,loose,wav\n");
            foreach (Row row in Rows(looseStems))
            {
                string wav;
                if (wavByMedia == null || !wavByMedia.TryGetValue(row.Id, out wav)) wav = "";
                s.Append(row.Stem).Append(',').Append(Field(ShortName(row.Id))).Append(',')
                 .Append(Field(row.Bank)).Append(',').Append(row.Loose ? "yes" : "no")
                 .Append(',').Append(Field(wav)).Append('\n');
            }
            return s.ToString();
        }

        private static string Field(string v)
        {
            if (string.IsNullOrEmpty(v)) return "";
            return v.IndexOf(',') >= 0 || v.IndexOf('"') >= 0
                ? "\"" + v.Replace("\"", "\"\"") + "\"" : v;
        }
    }
}
