using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Morgott.ContentTool.IO;

namespace Morgott.ContentTool.Import
{
    /// <summary>Why a sidecar that EXISTS was not applied, as a VALUE rather than a sentence: the
    /// Doctor has to tell "re-export the file" apart from "the map is broken", and deciding that by
    /// searching the prose for a word means a reworded sentence silently changes a diagnostic code.
    /// Everything that is not a hash mismatch is Invalid - the message already names which.</summary>
    internal enum SidecarProblem { None, Stale, Invalid }

    /// <summary>
    /// FILE BONE -&gt; GAME BONE, and nothing else. The game's skeleton is never renamed and never
    /// replaced; what an author can honestly fix from inside the game is which of THEIR bones stands
    /// for which of the game's, so that is the only thing this carries.
    ///
    /// Applied only on the REPLACEMENT read (GlbSource.ReadReplacement). The add-model route
    /// (ContentProject.ImportModel) ignores sidecars on purpose: its published bone-path hashes must
    /// not depend on a file sitting next to the .glb.
    /// </summary>
    internal sealed class AliasMap
    {
        internal const int Schema = 1;

        private readonly Dictionary<string, string> bones;

        private AliasMap(Dictionary<string, string> map) { bones = map; }

        internal int Count => bones.Count;

        /// <summary>The mappings themselves, for an editor that has to SEED itself from what is already
        /// on disk. SaveSidecar rewrites the whole "bones" object, so an editor that starts empty and
        /// saves silently deletes every mapping it never saw.</summary>
        internal IDictionary<string, string> Pairs => bones;

        /// <summary>
        /// A map, or NULL when it could never be applied: no entries, an empty output, or two file
        /// bones renamed onto one game bone (which is the PlainCollision the binder already refuses,
        /// and doing it silently here would be worse).
        /// </summary>
        internal static AliasMap Of(IDictionary<string, string> map)
        {
            if (map == null || map.Count == 0) return null;
            var copy = new Dictionary<string, string>(StringComparer.Ordinal);
            var outputs = new HashSet<string>(StringComparer.Ordinal);
            foreach (KeyValuePair<string, string> e in map)
            {
                if (string.IsNullOrEmpty(e.Key) || string.IsNullOrEmpty(e.Value)) return null;
                if (!outputs.Add(e.Value)) return null;
                copy[e.Key] = e.Value;
            }
            return new AliasMap(copy);
        }

        /// <summary>
        /// Renames the file's joints SIMULTANEOUSLY - every new name is read from the ORIGINAL names,
        /// so a map that swaps two bones does not apply one half onto the result of the other. The
        /// joint order, the weights, the inverse bind matrices, the node parents and every animation
        /// track's node index are untouched: only the strings move.
        /// </summary>
        /// <param name="unusedKeys">keys the file has no bone for. Applied partially and reported,
        /// never refused whole - an author who fixed two of three names should keep the two.</param>
        internal void Apply(SkinnedModel model, out IList<string> unusedKeys)
        {
            if (model == null) throw new ArgumentNullException(nameof(model));
            var used = new HashSet<string>(StringComparer.Ordinal);
            string[] original = model.JointNames.ToArray();
            for (int j = 0; j < original.Length; j++)
            {
                string to;
                if (!bones.TryGetValue(original[j], out to)) continue;
                used.Add(original[j]);
                model.JointNames[j] = to;
                if (model.JointNodes != null && j < model.JointNodes.Length)
                {
                    int node = model.JointNodes[j];
                    if (node >= 0 && node < model.Nodes.Count) model.Nodes[node].Name = to;
                }
            }
            var unused = new List<string>();
            foreach (KeyValuePair<string, string> e in bones) if (!used.Contains(e.Key)) unused.Add(e.Key);
            unusedKeys = unused;
        }

        /// <summary>Which outputs do NOT name a bone the target has. Asked here and only here - neither
        /// Apply nor the loader ever sees a target, which is why spec v2's "output not a target bone"
        /// check had nowhere to live.</summary>
        internal IList<string> OutputsNotIn(string[] targetBoneNames)
        {
            var bad = new List<string>();
            if (targetBoneNames == null) return bad;
            // The DECORATED spelling counts too. A target read off a live renderer names its bones
            // '#L.Arm_Addon => SY_Sniper_Torso_BodyPartDef' where the shipped asset says 'L.Arm', and
            // SkinCompatibility matches those two, so an alias onto 'L.Arm' binds - while this check,
            // comparing raw strings, called it a bone the skeleton does not have and put a warning row
            // under a BY NAME verdict telling the author to fix what was already right.
            var have = new HashSet<string>(targetBoneNames, StringComparer.Ordinal);
            foreach (string name in targetBoneNames) have.Add(SkinBinder.Plain(name));
            foreach (KeyValuePair<string, string> e in bones) if (!have.Contains(e.Value)) bad.Add(e.Key);
            return bad;
        }

        /// <summary>
        /// The block a caller logs. It counts and lists what ACTUALLY APPLIED - the same number the
        /// bake prints - and names the rest on a line of its own, because a block claiming three
        /// aliases beside a log saying two is the kind of disagreement an author cannot resolve.
        /// </summary>
        /// <param name="unusedKeys">Apply's out list, or null when every key applied.</param>
        internal string Describe(string sidecarPath, IList<string> unusedKeys)
        {
            var skipped = new HashSet<string>(unusedKeys ?? new List<string>(), StringComparer.Ordinal);
            var sb = new StringBuilder();
            sb.Append((bones.Count - skipped.Count).ToString(CultureInfo.InvariantCulture))
              .Append(" alias(es) from ").Append(sidecarPath);
            foreach (KeyValuePair<string, string> e in bones)
                if (!skipped.Contains(e.Key))
                    sb.Append("\n    '").Append(e.Key).Append("' -> '").Append(e.Value).Append('\'');
            if (skipped.Count > 0)
            {
                sb.Append("\n    unused (this file has no such bone):");
                bool first = true;
                foreach (KeyValuePair<string, string> e in bones)
                    if (skipped.Contains(e.Key)) { sb.Append(first ? " '" : ", '").Append(e.Key).Append('\''); first = false; }
            }
            return sb.ToString();
        }

        // ------------------------------------------------------------------ the sidecar

        internal static string SidecarPathOf(string glbPath) { return glbPath + ".aliases.json"; }

        internal static string Sha256(byte[] bytes)
        {
            using (SHA256 sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(bytes);
                var sb = new StringBuilder(hash.Length * 2);
                foreach (byte b in hash) sb.Append(b.ToString("x2", CultureInfo.InvariantCulture));
                return sb.ToString();
            }
        }

        /// <summary>
        /// The sidecar next to the .glb, or null. <paramref name="why"/> is null when there is simply
        /// no sidecar and a SENTENCE whenever one exists but was not applied - stale, malformed,
        /// unknown schema, colliding outputs. Never silent, and never fatal: a sidecar that does not
        /// apply leaves a file that may still bind by name on its own, so the caller carries this as a
        /// WARNING and computes the outcome from the unaliased model.
        /// </summary>
        internal static AliasMap LoadSidecar(string glbPath, string sha256, out string why)
        {
            SidecarProblem problem;
            return LoadSidecar(glbPath, sha256, out why, out problem);
        }

        /// <param name="problem">the same refusal as a VALUE, for a caller that has to BRANCH on it
        /// rather than print it. <paramref name="why"/> stays the sentence, word for word.</param>
        internal static AliasMap LoadSidecar(string glbPath, string sha256, out string why,
                                             out SidecarProblem problem)
        {
            why = null;
            problem = SidecarProblem.None;
            string path = SidecarPathOf(glbPath);
            if (!File.Exists(path)) return null;
            try
            {
                var root = Json.Parse(File.ReadAllText(path), 16) as Dictionary<string, object>;
                if (root == null) { why = "'" + path + "' is not a JSON object, so its aliases were NOT applied"; problem = SidecarProblem.Invalid; return null; }

                object schema;
                double declared = root.TryGetValue("schema", out schema) && schema is double d ? d : 0;
                // (int) alone accepted 1.5 as 1, so a sidecar written for a schema this mod has never
                // seen applied itself. Compared as a DOUBLE and spelled with "R", both for the same
                // reason: no integer cast can be trusted here - it turns 1.5 into 1 and a huge value
                // into a wrapped one, and the refusal for 1.5 would then read "declares schema 1 but
                // this mod reads 1".
                if (declared != Math.Floor(declared) || declared != Schema)
                {
                    why = "'" + path + "' declares schema " +
                          declared.ToString("R", CultureInfo.InvariantCulture) +
                          " but this mod reads " + Schema.ToString(CultureInfo.InvariantCulture) +
                          ", so its aliases were NOT applied";
                    problem = SidecarProblem.Invalid;
                    return null;
                }

                object src;
                var source = root.TryGetValue("source", out src) ? src as Dictionary<string, object> : null;
                object stated;
                string was = source != null && source.TryGetValue("sha256", out stated) ? stated as string : null;
                if (!string.Equals(was, sha256, StringComparison.OrdinalIgnoreCase))
                {
                    why = "'" + path + "' was written for a different version of this .glb (the file has been " +
                          "re-exported since), so its aliases were NOT applied - open the Doctor and save them again";
                    problem = SidecarProblem.Stale;
                    return null;
                }

                object raw;
                var bones = root.TryGetValue("bones", out raw) ? raw as Dictionary<string, object> : null;
                if (bones == null) { why = "'" + path + "' carries no \"bones\" object, so nothing was applied"; problem = SidecarProblem.Invalid; return null; }
                var map = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (KeyValuePair<string, object> e in bones) map[e.Key] = e.Value as string;
                // Said as its own sentence: an empty map is a sidecar with nothing in it, NOT the
                // collision below, and naming the wrong cause sends the author to fix the wrong thing.
                if (map.Count == 0) { why = "'" + path + "' carries no aliases, so nothing was applied"; problem = SidecarProblem.Invalid; return null; }
                AliasMap loaded = Of(map);
                if (loaded == null)
                {
                    why = "'" + path + "' maps two of the file's bones onto one of the game's, or leaves a name " +
                          "empty, so NONE of its aliases were applied";
                    problem = SidecarProblem.Invalid;
                }
                return loaded;
            }
            catch (Exception ex)
            {
                why = "'" + path + "' could not be read (" + ex.Message + "), so its aliases were NOT applied";
                problem = SidecarProblem.Invalid;
                return null;
            }
        }

        /// <summary>
        /// Writes the sidecar through AtomicFile: a temp beside the destination, then the swap, so a
        /// crash mid-write cannot leave half a map beside the model. The TEXT is still built here,
        /// by hand - only the commit moved.
        /// </summary>
        internal static void SaveSidecar(string glbPath, string sha256, long bytes, IDictionary<string, string> map)
        {
            string path = SidecarPathOf(glbPath);
            var sb = new StringBuilder();
            sb.Append("{\n  \"schema\": ").Append(Schema.ToString(CultureInfo.InvariantCulture));
            sb.Append(",\n  \"source\": { \"sha256\": \"").Append(sha256).Append("\", \"bytes\": ")
              .Append(bytes.ToString(CultureInfo.InvariantCulture)).Append(" }");
            sb.Append(",\n  \"bones\": {");
            bool first = true;
            foreach (KeyValuePair<string, string> e in map)
            {
                sb.Append(first ? "\n    " : ",\n    ");
                sb.Append('"').Append(Escape(e.Key)).Append("\": \"").Append(Escape(e.Value)).Append('"');
                first = false;
            }
            sb.Append(first ? "}" : "\n  }").Append("\n}\n");

            AtomicFile.WriteText(path, sb.ToString(), new UTF8Encoding(false));
        }

        private static string Escape(string s)
        {
            var sb = new StringBuilder(s.Length + 8);
            foreach (char c in s)
            {
                if (c == '"' || c == '\\') sb.Append('\\').Append(c);
                else if (c < ' ') sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                else sb.Append(c);
            }
            return sb.ToString();
        }
    }
}
