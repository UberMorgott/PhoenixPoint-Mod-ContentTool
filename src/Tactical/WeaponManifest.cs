using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace Morgott.ContentTool.Tactical
{
    /// <summary>
    /// ============ THE "weapons" BLOCK, READ AND WRITTEN AS TEXT ============
    ///
    /// The half of the weapon manifest that carries NO UnityEngine type, so the round trip a live fit
    /// depends on - tune, save, reload, get the same gun - is measurable OFFLINE (gate S25) instead of
    /// only in a soldier's hand. <see cref="WeaponBuild"/> reads its entries through the same
    /// <see cref="Rows"/>/<see cref="Field"/> pair this file writes with, so a reader and a writer that
    /// disagree about what a row is cannot exist.
    ///
    /// A STRING SPLICE, NOT A JSON ROUND TRIP - the same decision, for the same reason, as
    /// <see cref="CreatureManifest.Scaffold"/>: this rewrites a file the author maintains BY HAND, and
    /// there is no writer in this tool (JsonUtility returns null for these nested shapes, which is why
    /// the reader is a regex in the first place). Reformatting it would turn every line into a diff and
    /// eat the comments. Only the three values a fit produces are replaced; every other byte - key
    /// order, indentation, line endings, a BOM, keys nothing here knows about - survives.
    /// </summary>
    internal static class WeaponManifest
    {
        /// <summary>One flat <c>{...}</c> row of the "weapons" array and where it starts in the file.</summary>
        internal struct Row
        {
            internal int Start;
            internal string Text;
        }

        private const string ArrayKey = "\"weapons\"\\s*:\\s*\\[";

        /// <summary>
        /// The rows of the manifest's "weapons" array, with absolute offsets. The row shape is
        /// deliberately FLAT (<c>\{[^{}]*\}</c>) - see the note on "keywords" in WeaponBuild.Parse:
        /// a nested object inside an entry would break this and the ContentProject parser both.
        /// </summary>
        internal static List<Row> Rows(string json)
        {
            List<Row> rows = new List<Row>();
            Match arr = Regex.Match(json, ArrayKey + "(.*?)\\]", RegexOptions.Singleline);
            if (!arr.Success) return rows;
            int at = arr.Groups[1].Index;
            foreach (Match o in Regex.Matches(arr.Groups[1].Value, "\\{[^{}]*\\}", RegexOptions.Singleline))
                rows.Add(new Row { Start = at + o.Index, Text = o.Value });
            return rows;
        }

        internal static string Field(string obj, string name)
        {
            return Regex.Match(obj, "\"" + name + "\"\\s*:\\s*\"([^\"]*)\"").Groups[1].Value;
        }

        /// <summary>A number written either bare or quoted, invariant culture - a comma decimal
        /// separator on a Russian machine would otherwise read 3.0 as 30.</summary>
        internal static float Num(string obj, string name)
        {
            Match m = Regex.Match(obj, "\"" + name + "\"\\s*:\\s*\"?(-?[0-9]*\\.?[0-9]+)\"?");
            if (!m.Success) return 0f;
            float v;
            return float.TryParse(m.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out v) ? v : 0f;
        }

        /// <summary>"x,y,z" -&gt; three floats. Absent or malformed reads as zero, which the callers
        /// treat as "not declared".</summary>
        internal static float[] Vec(string obj, string name)
        {
            float[] v = new float[3];
            string raw = Field(obj, name);
            if (string.IsNullOrEmpty(raw)) return v;
            string[] parts = raw.Split(',');
            if (parts.Length != 3) return v;
            for (int i = 0; i < 3; i++)
                if (!float.TryParse(parts[i].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out v[i]))
                    return new float[3];
            return v;
        }

        // ------------------------------------------------------------------ writing a dialled fit back

        internal static string Xyz(float[] v, string format)
        {
            return v[0].ToString(format, CultureInfo.InvariantCulture) + "," +
                   v[1].ToString(format, CultureInfo.InvariantCulture) + "," +
                   v[2].ToString(format, CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// The three keys a live fit produces, spliced into the ONE entry that carries this id.
        /// Returns the new file text, or null with <paramref name="why"/> set.
        ///
        /// REFUSES RATHER THAN GUESSES, and both refusals are ownership questions: two "weapons"
        /// arrays in one file means the row this found may not be the row the game built from, and an
        /// id that matches zero or two entries means there is no single place for these numbers to go.
        /// A fit written into the wrong entry is a weapon the author did not tune silently changing.
        /// </summary>
        internal static string Splice(string json, string id, float scale, float[] rotate, float[] offset,
                                      out string why)
        {
            why = null;
            int arrays = Regex.Matches(json, ArrayKey).Count;
            if (arrays != 1)
            {
                why = "the file declares " + arrays + " \"weapons\" arrays; exactly one is needed to " +
                      "know which entry to write";
                return null;
            }

            Row hit = new Row();
            int hits = 0;
            foreach (Row r in Rows(json))
                if (Field(r.Text, "id") == id) { hit = r; hits++; }
            if (hits != 1)
            {
                why = "'" + id + "' matches " + hits + " \"weapons\" entries in this file";
                return null;
            }

            string row = hit.Text;
            row = Set(row, "scale", scale.ToString("0.0000", CultureInfo.InvariantCulture));
            row = Set(row, "rotate", Xyz(rotate, "0.###"));
            row = Set(row, "offset", Xyz(offset, "0.####"));
            return json.Substring(0, hit.Start) + row + json.Substring(hit.Start + hit.Text.Length);
        }

        /// <summary>One key set to a QUOTED value: its own value replaced in place when the row already
        /// carries it (bare or quoted), otherwise inserted as the row's first key, in the row's own
        /// indentation and line ending. Nothing else in the row is touched.</summary>
        private static string Set(string row, string name, string value)
        {
            Match at = Regex.Match(row, "\"" + name + "\"\\s*:\\s*(\"[^\"]*\"|[^,}\\s]+)");
            if (at.Success)
            {
                Group v = at.Groups[1];
                return row.Substring(0, v.Index) + "\"" + value + "\"" + row.Substring(v.Index + v.Length);
            }
            int brace = row.IndexOf('{');
            string nl = row.IndexOf("\r\n", StringComparison.Ordinal) >= 0 ? "\r\n"
                      : row.IndexOf('\n') >= 0 ? "\n" : null;
            string pair = "\"" + name + "\": \"" + value + "\",";
            return nl == null
                ? row.Substring(0, brace + 1) + " " + pair + row.Substring(brace + 1)
                : row.Substring(0, brace + 1) + nl + Indent(row, nl) + pair + row.Substring(brace + 1);
        }

        /// <summary>The row's own leading whitespace on its second line - so an inserted key lines up
        /// with the keys already there instead of imposing this file's idea of indentation.</summary>
        private static string Indent(string row, string nl)
        {
            int line = row.IndexOf(nl, StringComparison.Ordinal) + nl.Length;
            int i = line;
            while (i < row.Length && (row[i] == ' ' || row[i] == '\t')) i++;
            return i > line ? row.Substring(line, i - line) : "      ";
        }

        // ------------------------------------------------------------------ the file itself

        /// <summary>
        /// The splice, VALIDATED and then written to disk. Returns the destination path, or null with
        /// <paramref name="why"/> set - and it never half-writes: the new text is proven to re-read as
        /// the numbers that went in BEFORE the file is touched, and then arrives through a temp file
        /// in the same directory so a crash mid-write cannot leave the author with half a manifest.
        /// </summary>
        internal static string Save(string path, string id, float scale, float[] rotate, float[] offset,
                                    out string why)
        {
            why = null;
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                why = "no ppcontent.json at '" + path + "'";
                return null;
            }

            byte[] raw;
            try { raw = File.ReadAllBytes(path); }
            catch (Exception ex) { why = "cannot read it: " + ex.GetType().Name + " " + ex.Message; return null; }

            // The BOM is part of the file, not part of the text: stripping one the author had, or
            // adding one they did not, is a whole-file diff for nothing.
            bool bom = raw.Length >= 3 && raw[0] == 0xEF && raw[1] == 0xBB && raw[2] == 0xBF;
            UTF8Encoding utf8 = new UTF8Encoding(false);
            string json = utf8.GetString(raw, bom ? 3 : 0, raw.Length - (bom ? 3 : 0));

            string updated = Splice(json, id, scale, rotate, offset, out why);
            if (updated == null) return null;

            // THE PROOF, before the write and not after it. A splice that produced a row this tool's
            // own reader no longer understands is a manifest the game silently stops building the
            // weapon from - so it is re-read here, and refused rather than shipped.
            if (!Verify(updated, id, scale, rotate, offset, out why)) return null;

            string tmp = path + ".ct_tmp";
            try
            {
                using (FileStream fs = new FileStream(tmp, FileMode.Create, FileAccess.Write))
                {
                    if (bom) fs.Write(new byte[] { 0xEF, 0xBB, 0xBF }, 0, 3);
                    byte[] body = utf8.GetBytes(updated);
                    fs.Write(body, 0, body.Length);
                }
                File.Replace(tmp, path, null);
            }
            catch (Exception ex)
            {
                why = "cannot write it: " + ex.GetType().Name + " " + ex.Message;
                try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
                return null;
            }
            return path;
        }

        /// <summary>Does the written text read back as the numbers that were written? Compared at the
        /// precision the values are PRINTED at, because that rounding is the only loss allowed.</summary>
        internal static bool Verify(string json, string id, float scale, float[] rotate, float[] offset,
                                    out string why)
        {
            why = null;
            string row = null;
            int hits = 0;
            foreach (Row r in Rows(json))
                if (Field(r.Text, "id") == id) { row = r.Text; hits++; }
            if (hits != 1)
            {
                why = "after the splice '" + id + "' matches " + hits + " entries - the file was not written";
                return false;
            }
            float[] gotRot = Vec(row, "rotate"), gotOff = Vec(row, "offset");
            float gotScale = Num(row, "scale");
            if (Math.Abs(gotScale - scale) > 5e-5f ||
                Off(gotRot, rotate, 5e-4f) || Off(gotOff, offset, 5e-5f))
            {
                why = "the spliced entry re-reads as scale " + gotScale + " rotate " + Xyz(gotRot, "0.###") +
                      " offset " + Xyz(gotOff, "0.####") + ", not as what was dialled - the file was not written";
                return false;
            }
            return true;
        }

        private static bool Off(float[] a, float[] b, float tol)
        {
            for (int i = 0; i < 3; i++) if (Math.Abs(a[i] - b[i]) > tol) return true;
            return false;
        }
    }
}
