using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Morgott.ContentTool.Project
{
    /// <summary>Which of the three shippable identity anchors a target path is written against (FINAL-PLAN 39.2).</summary>
    internal enum AnchorKind
    {
        /// <summary>Addressables AssetGUID, read off the resolve seam. The only anchor a visual replacement can ship with.</summary>
        Guid,
        /// <summary>Frozen Wwise media index key. Audio only, and never carries a subpath.</summary>
        Media,
        /// <summary>A live UnityEngine.Object.name from the dev scan. Dev mode only; bake refuses it.</summary>
        Name,
        /// <summary>Author-facing sugar. Must be rewritten to <see cref="Guid"/> before bake.</summary>
        DefName
    }

    /// <summary>
    /// One target path - the single string that names a replacement target in the project file, in the
    /// baked table, and in every log line (FINAL-PLAN 39.2):
    ///
    ///   guid:8f3c...#Root/Chest/Arm_R@SkinnedMeshRenderer.mesh
    ///   guid:8f3c...#Root/Chest/Arm_R@Renderer.materials[1]
    ///   guid:8f3c...#Root/Chest/Arm_R@Renderer.materials[1].tex:_MainTex
    ///   guid:8f3c...#@Animator.clip:Idle_Rifle
    ///   media:18839791
    ///
    /// Deliberately free of UnityEngine types, so parse/format/validation are provable offline
    /// (gate R0) instead of only in a game session.
    ///
    /// PROVISIONAL until gate R1 has a number: the anchor set is what the seam is EXPECTED to hand
    /// us, and R-U1/R-U2 may change it. Nothing reads ContentProject.Replacements yet and no bake
    /// path touches replacements.json, so the format is still free to move - do not treat it as
    /// frozen, and do not build a migration for it.
    /// </summary>
    internal sealed class TargetPath
    {
        internal AnchorKind Anchor;
        /// <summary>32 lowercase hex for guid:, a decimal id for media:, an object/def name otherwise.</summary>
        internal string AnchorValue;
        /// <summary>Transform path inside the anchored prefab. "" means the anchored root itself.</summary>
        internal string Transform;
        /// <summary>Component type name after '@'. Null when there is no subpath (media: only).</summary>
        internal string Component;
        /// <summary>Slot field: mesh / materials / clip / ...</summary>
        internal string Field;
        /// <summary>Array index inside the slot, or -1 when the slot is not indexed.</summary>
        internal int Index = -1;
        /// <summary>"tex" in materials[1].tex:_MainTex; null when the member hangs straight off the field.</summary>
        internal string Qualifier;
        /// <summary>"_MainTex" / "Idle_Rifle"; null when the slot names no member.</summary>
        internal string Member;

        internal bool HasSubpath { get { return Component != null; } }

        /// <summary>
        /// Parses, or refuses by NAME. There is no fuzzy match and no silent skip: every rejection
        /// hands back a sentence saying which part of the string is wrong.
        /// </summary>
        internal static bool TryParse(string s, out TargetPath path, out string error)
        {
            path = null;
            if (string.IsNullOrEmpty(s)) { error = "empty target path"; return false; }

            int colon = s.IndexOf(':');
            if (colon <= 0) { error = "no anchor in '" + s + "' (expected guid:, media:, name: or defname:)"; return false; }

            AnchorKind kind;
            switch (s.Substring(0, colon))
            {
                case "guid": kind = AnchorKind.Guid; break;
                case "media": kind = AnchorKind.Media; break;
                case "name": kind = AnchorKind.Name; break;
                case "defname": kind = AnchorKind.DefName; break;
                default:
                    error = "unknown anchor '" + s.Substring(0, colon) + ":' in '" + s +
                            "' (expected guid:, media:, name: or defname:)";
                    return false;
            }

            string rest = s.Substring(colon + 1);
            int hash = rest.IndexOf('#');
            string value = hash < 0 ? rest : rest.Substring(0, hash);
            string sub = hash < 0 ? null : rest.Substring(hash + 1);

            switch (kind)
            {
                case AnchorKind.Guid:
                    if (!IsGuid(value)) { error = "guid: needs 32 lowercase hex digits, got '" + value + "'"; return false; }
                    break;
                case AnchorKind.Media:
                    uint ignored;
                    if (!uint.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out ignored))
                    { error = "media: needs an unsigned decimal media id, got '" + value + "'"; return false; }
                    if (sub != null) { error = "media: takes no subpath, got '#" + sub + "'"; return false; }
                    break;
                default:
                    if (value.Length == 0 || value.Trim().Length == 0)
                    { error = s.Substring(0, colon) + ": needs a name"; return false; }
                    if (value.IndexOf('@') >= 0)
                    { error = s.Substring(0, colon) + ": name must not contain '@' ('" + value + "')"; return false; }
                    break;
            }

            TargetPath p = new TargetPath { Anchor = kind, AnchorValue = value };
            if (kind != AnchorKind.Media)
            {
                if (sub == null)
                {
                    error = "'" + s + "' needs a subpath (<anchor>#Transform/Path@Component.slot)";
                    return false;
                }
                if (!ParseSubpath(p, sub, out error)) return false;
            }

            path = p;
            error = null;
            return true;
        }

        /// <summary>Transform path + '@' + component + '.' + slot.</summary>
        private static bool ParseSubpath(TargetPath p, string sub, out string error)
        {
            int at = sub.IndexOf('@');
            if (at < 0) { error = "subpath '" + sub + "' needs '@Component.slot'"; return false; }

            p.Transform = sub.Substring(0, at);
            if (!IsTransformPath(p.Transform))
            { error = "transform path '" + p.Transform + "' has an empty segment or an illegal character"; return false; }

            string rest = sub.Substring(at + 1);
            int dot = rest.IndexOf('.');
            if (dot <= 0) { error = "'@" + rest + "' needs '@Component.slot'"; return false; }

            p.Component = rest.Substring(0, dot);
            if (!IsIdentifier(p.Component)) { error = "component '" + p.Component + "' is not an identifier"; return false; }

            return ParseSlot(p, rest.Substring(dot + 1), out error);
        }

        /// <summary>field | field[i] | field[i].qualifier:member | field:member</summary>
        private static bool ParseSlot(TargetPath p, string slot, out string error)
        {
            int i = 0;
            while (i < slot.Length && slot[i] != '[' && slot[i] != ':' && slot[i] != '.') i++;
            p.Field = slot.Substring(0, i);
            if (!IsIdentifier(p.Field)) { error = "slot field '" + p.Field + "' is not an identifier"; return false; }

            if (i < slot.Length && slot[i] == '[')
            {
                int close = slot.IndexOf(']', i);
                if (close < 0) { error = "slot '" + slot + "' has no closing ']'"; return false; }
                int index;
                if (!int.TryParse(slot.Substring(i + 1, close - i - 1), NumberStyles.None, CultureInfo.InvariantCulture, out index))
                { error = "slot index in '" + slot + "' is not a non-negative number"; return false; }
                p.Index = index;
                i = close + 1;
            }

            if (i == slot.Length) { error = null; return true; }

            if (slot[i] == '.')
            {
                int c = slot.IndexOf(':', i);
                if (c < 0) { error = "slot '" + slot + "' has a qualifier but no ':member'"; return false; }
                p.Qualifier = slot.Substring(i + 1, c - i - 1);
                if (!IsIdentifier(p.Qualifier)) { error = "slot qualifier '" + p.Qualifier + "' is not an identifier"; return false; }
                i = c;
            }

            if (slot[i] != ':') { error = "unexpected '" + slot[i] + "' in slot '" + slot + "'"; return false; }
            p.Member = slot.Substring(i + 1);
            if (p.Member.Length == 0) { error = "slot '" + slot + "' names an empty member"; return false; }
            if (p.Member.IndexOf('@') >= 0 || p.Member.IndexOf('#') >= 0)
            { error = "slot member '" + p.Member + "' must not contain '@' or '#'"; return false; }

            error = null;
            return true;
        }

        /// <summary>Byte-identical to the string that parsed into this (gate R0).</summary>
        internal string Format()
        {
            StringBuilder b = new StringBuilder();
            b.Append(Prefix(Anchor)).Append(':').Append(AnchorValue);
            if (!HasSubpath) return b.ToString();

            b.Append('#').Append(Transform).Append('@').Append(Component).Append('.').Append(Field);
            if (Index >= 0) b.Append('[').Append(Index.ToString(CultureInfo.InvariantCulture)).Append(']');
            if (Member != null)
            {
                if (Qualifier != null) b.Append('.').Append(Qualifier);
                b.Append(':').Append(Member);
            }
            return b.ToString();
        }

        public override string ToString() { return Format(); }

        internal static string Prefix(AnchorKind k)
        {
            switch (k)
            {
                case AnchorKind.Guid: return "guid";
                case AnchorKind.Media: return "media";
                case AnchorKind.Name: return "name";
                default: return "defname";
            }
        }

        private static bool IsGuid(string s)
        {
            if (s == null || s.Length != 32) return false;
            foreach (char c in s) if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f'))) return false;
            return true;
        }

        private static bool IsIdentifier(string s)
        {
            if (string.IsNullOrEmpty(s)) return false;
            foreach (char c in s) if (!char.IsLetterOrDigit(c) && c != '_') return false;
            return true;
        }

        /// <summary>"" (the anchored root) or Slash/Separated/Segments; no empty segment, no '#'.</summary>
        private static bool IsTransformPath(string s)
        {
            if (s.Length == 0) return true;
            if (s.IndexOf('#') >= 0) return false;
            foreach (string seg in s.Split('/'))
                if (seg.Length == 0 || seg.Trim().Length == 0) return false;
            return true;
        }
    }

    /// <summary>
    /// One authored replacement: what to hit, what to put there, and what the target looked like when
    /// it was picked. <see cref="Sha1"/> is checked at apply time and NEVER used to look anything up -
    /// that is the whole departure from ResourceReplacer's name_checksum identity (FINAL-PLAN 39.2).
    /// </summary>
    internal sealed class ReplacementRule
    {
        internal string Target;
        /// <summary>Name of the baked asset in MyMod.bundle that replaces the target.</summary>
        internal string Content;
        /// <summary>40 hex, or null/"" when the target could not be hashed when it was picked.</summary>
        internal string Sha1;
        /// <summary>Filled by <see cref="ReplacementSet.Validate"/>; null for a refused record.</summary>
        internal TargetPath Path;
    }

    internal enum Verification { Match, Differ, Unverifiable }

    /// <summary>Whole-file rules: which records load, and which of those may reach a bake.</summary>
    internal static class ReplacementSet
    {
        /// <summary>
        /// Returns the records that load. Every rejected record appends one sentence to
        /// <paramref name="refusals"/> - a refusal is never a silent skip, and one bad record never
        /// takes the file down with it.
        /// </summary>
        internal static List<ReplacementRule> Validate(IList<ReplacementRule> rules, List<string> refusals)
        {
            List<ReplacementRule> ok = new List<ReplacementRule>();
            if (rules == null) return ok;

            // Two records naming the same target cannot both win, and nothing in the string says which
            // should. Refuse BOTH: picking one silently is exactly the guess FINAL-PLAN 39.2 forbids.
            Dictionary<string, int> seen = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (ReplacementRule r in rules)
            {
                if (r == null || r.Target == null) continue;
                int n;
                seen[r.Target] = seen.TryGetValue(r.Target, out n) ? n + 1 : 1;
            }

            foreach (ReplacementRule r in rules)
            {
                if (r == null) { refusals.Add("(null record)"); continue; }
                string why;
                TargetPath p;
                if (!TargetPath.TryParse(r.Target, out p, out why)) { refusals.Add(why); continue; }
                if (string.IsNullOrEmpty(r.Content)) { refusals.Add("'" + r.Target + "' names no content asset"); continue; }
                if (!string.IsNullOrEmpty(r.Sha1) && !IsSha1(r.Sha1))
                { refusals.Add("'" + r.Target + "' has a sha1 that is not 40 hex digits ('" + r.Sha1 + "')"); continue; }
                if (seen[r.Target] > 1)
                { refusals.Add("'" + r.Target + "' is named by " + seen[r.Target] + " records - ambiguous, none applied"); continue; }

                r.Path = p;
                ok.Add(r);
            }
            return ok;
        }

        /// <summary>
        /// Shipping-mode filter (FINAL-PLAN 39.4): a released mod has no scan and no def resolution
        /// step, so a name:/defname: record could never apply and bake must fail naming it.
        /// </summary>
        internal static bool Bakeable(ReplacementRule r, out string error)
        {
            if (r == null || r.Path == null) { error = "record was refused at load"; return false; }
            switch (r.Path.Anchor)
            {
                case AnchorKind.Name:
                    error = "'" + r.Target + "' is a scan-discovered name: target - a shipped mod has no scan, re-pick it at the resolve seam";
                    return false;
                case AnchorKind.DefName:
                    // ponytail: no automatic defname:->guid: rewrite. A skin def carries up to four
                    // AssetReferences (Male/Female x Normal/Disabled, see
                    // extracted\GameData\defs\ArmourBodyPartSkinDataDef\*.json), so the rewrite is a
                    // CHOICE the picker makes, not a mechanical lookup. Upgrade path: the picker
                    // writes the guid: form it resolved.
                    error = "'" + r.Target + "' is a defname: target - resolve it to guid: with the picker before baking";
                    return false;
                default:
                    error = null;
                    return true;
            }
        }

        /// <summary>
        /// Apply-time verification, the ONLY thing sha1 is for. Unverifiable is not a failure: most
        /// shipped meshes are not CPU-readable, and FINAL-PLAN 39.2 dropped the dump pipeline that
        /// used to force them to be.
        /// </summary>
        internal static Verification Verify(ReplacementRule r, string actualSha1)
        {
            if (r == null || string.IsNullOrEmpty(r.Sha1) || string.IsNullOrEmpty(actualSha1)) return Verification.Unverifiable;
            return string.Equals(r.Sha1, actualSha1, StringComparison.Ordinal) ? Verification.Match : Verification.Differ;
        }

        private static bool IsSha1(string s)
        {
            if (s.Length != 40) return false;
            foreach (char c in s) if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f'))) return false;
            return true;
        }
    }

    internal static class Sha1
    {
        /// <summary>Lowercase hex, the form every record and log line uses.</summary>
        internal static string Hex(byte[] data)
        {
            if (data == null) return null;
            using (SHA1 sha = SHA1.Create())
            {
                byte[] h = sha.ComputeHash(data);
                StringBuilder b = new StringBuilder(40);
                foreach (byte x in h) b.Append(x.ToString("x2", CultureInfo.InvariantCulture));
                return b.ToString();
            }
        }
    }
}
