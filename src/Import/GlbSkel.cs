using System;
using System.Collections.Generic;

namespace Morgott.ContentTool.Import
{
    /// <summary>One rename. The whole reason this tool exists: Addon.GetEquivalentBones matches a
    /// bone with ownBoneName == bone.name (Addon.cs:1217) - ordinal, case-sensitive, literal - so the
    /// only way a foreign mesh binds is for its node to BE called what the rig calls it.</summary>
    internal sealed class SkelRename
    {
        internal string From, To;
    }

    /// <summary>A new node slipped between Parent and one of its children. PP carries roll bones
    /// INSIDE the chain (L.UpLeg/L.UpLeg_Roll_1/L.UpLeg_Roll_2/L.Leg) where a foreign rig has its
    /// twist bones as SIBLINGS, so the chain has to grow the missing links (ppskel.py:13-15).</summary>
    internal sealed class SkelInsert
    {
        internal string Parent;      // an existing node, named after any rename in the same plan
        internal string Name;        // the new node's name; must not already exist
        internal string Child;       // the existing child of Parent that moves under the new node

        /// <summary>Local TRS of the new node. Null = identity, which is the world-preserving case
        /// and the only one ppskel emits (ppskel.py:307). A non-identity local is honoured by
        /// COMPENSATING the child: L_child' = L_child * inverse(L_new), so Child's world matrix is
        /// still exactly what it was. Refused when Child is animated - see Validate.</summary>
        internal double[] Translation, Rotation, Scale;   // 3, 4 (xyzw), 3
    }

    /// <summary>Node is re-parented onto Into, which must be its GRANDparent, with the skipped
    /// parent's local composed in. ppskel.py:89 needs it once: the source rig has neck_01/neck_02
    /// where PP has a single Neck.</summary>
    internal sealed class SkelCollapse
    {
        internal string Node, Into;
    }

    /// <summary>
    /// What an author wants done to a skeleton, spelled out. EXPLICIT ONLY: ppskel.convert:316-328
    /// sweeps every unresolved PP path into a created leaf automatically, and design §9 forbids that
    /// ("do NOT generalise them into automatic guesses"), so every step in here was written by hand
    /// or by a tool that knew the answer. Nothing is ever invented from a near-miss.
    /// </summary>
    internal sealed class SkelPlan
    {
        /// <summary>The node the Animator sits on in the converted model - ppskel's ANIM_ROOT
        /// (ppskel.py:41). PP paths start BELOW it, and the root itself is the empty path that root
        /// motion binds to (crc32("") - ClipFields.cs:38).</summary>
        internal string Root;
        internal List<SkelRename> Renames = new List<SkelRename>();
        internal List<SkelCollapse> Collapses = new List<SkelCollapse>();
        internal List<SkelInsert> Inserts = new List<SkelInsert>();
        internal List<string> Create = new List<string>();

        internal const int Schema = 1;

        /// <summary>Read a plan file. Returns null and fills <paramref name="why"/> for anything a
        /// plan cannot be - not an object, an unknown schema, a step missing a name. Never throws:
        /// this is reached from OnGUI, where a throw tears the bench panel down mid-frame.</summary>
        internal static SkelPlan Parse(string json, out string why)
        {
            why = null;
            Dictionary<string, object> root;
            try
            {
                root = Json.Parse(json, 32) as Dictionary<string, object>;
            }
            catch (Exception error)
            {
                why = "this is not a skeleton plan: " + error.Message;
                return null;
            }
            if (root == null)
            {
                why = "a skeleton plan has to be a JSON object, and this file's top level is not one";
                return null;
            }
            if (GlbSlim.Get(root, "schema") != null && GlbSlim.Int(root, "schema", -1) != Schema)
            {
                why = "this plan declares a schema this build does not know; it reads schema " + Schema;
                return null;
            }

            var plan = new SkelPlan { Root = GlbSlim.Str(root, "root") };
            foreach (Dictionary<string, object> step in Steps(root, "renames"))
            {
                string from = GlbSlim.Str(step, "from"), to = GlbSlim.Str(step, "to");
                if (string.IsNullOrEmpty(from) || string.IsNullOrEmpty(to))
                {
                    why = "a rename needs both a 'from' and a 'to', and one of this plan's renames is missing one";
                    return null;
                }
                plan.Renames.Add(new SkelRename { From = from, To = to });
            }
            foreach (Dictionary<string, object> step in Steps(root, "collapses"))
            {
                string node = GlbSlim.Str(step, "node"), into = GlbSlim.Str(step, "into");
                if (string.IsNullOrEmpty(node) || string.IsNullOrEmpty(into))
                {
                    why = "a collapse needs both a 'node' and an 'into', and one of this plan's collapses is missing one";
                    return null;
                }
                plan.Collapses.Add(new SkelCollapse { Node = node, Into = into });
            }
            foreach (Dictionary<string, object> step in Steps(root, "inserts"))
            {
                string parent = GlbSlim.Str(step, "parent"), name = GlbSlim.Str(step, "name");
                string child = GlbSlim.Str(step, "child");
                if (string.IsNullOrEmpty(parent) || string.IsNullOrEmpty(name) || string.IsNullOrEmpty(child))
                {
                    why = "an insert needs a 'parent', a 'name' and a 'child', and one of this plan's inserts is missing one";
                    return null;
                }
                plan.Inserts.Add(new SkelInsert
                {
                    Parent = parent,
                    Name = name,
                    Child = child,
                    Translation = GlbSkel.Numbers(GlbSlim.Get(step, "translation"), 3),
                    Rotation = GlbSkel.Numbers(GlbSlim.Get(step, "rotation"), 4),
                    Scale = GlbSkel.Numbers(GlbSlim.Get(step, "scale"), 3),
                });
            }
            List<object> create = GlbSlim.Arr(root, "create");
            if (create != null)
                foreach (object item in create)
                {
                    if (!(item is string path) || path.Length == 0)
                    {
                        why = "every entry of 'create' has to be a non-empty path, and one of this plan's is not";
                        return null;
                    }
                    plan.Create.Add(path);
                }
            return plan;
        }

        /// <summary>The plan as JSON, through the writer GlbDocument already uses
        /// (GlbDocument.cs:91). Round-trips Parse exactly.</summary>
        internal string ToJson()
        {
            var root = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                { "schema", (double)Schema },
                { "root", Root },
            };
            var renames = new List<object>();
            foreach (SkelRename step in Renames)
                renames.Add(new Dictionary<string, object>(StringComparer.Ordinal)
                    { { "from", step.From }, { "to", step.To } });
            root["renames"] = renames;

            var collapses = new List<object>();
            foreach (SkelCollapse step in Collapses)
                collapses.Add(new Dictionary<string, object>(StringComparer.Ordinal)
                    { { "node", step.Node }, { "into", step.Into } });
            root["collapses"] = collapses;

            var inserts = new List<object>();
            foreach (SkelInsert step in Inserts)
            {
                var written = new Dictionary<string, object>(StringComparer.Ordinal)
                    { { "parent", step.Parent }, { "name", step.Name }, { "child", step.Child } };
                if (step.Translation != null) written["translation"] = Values(step.Translation);
                if (step.Rotation != null) written["rotation"] = Values(step.Rotation);
                if (step.Scale != null) written["scale"] = Values(step.Scale);
                inserts.Add(written);
            }
            root["inserts"] = inserts;

            var create = new List<object>();
            foreach (string path in Create) create.Add(path);
            root["create"] = create;

            return new JsonWriter().Val(root).ToString();
        }

        private static IEnumerable<Dictionary<string, object>> Steps(Dictionary<string, object> root, string key)
        {
            List<object> items = GlbSlim.Arr(root, key);
            if (items == null) yield break;
            foreach (object item in items)
                if (item is Dictionary<string, object> step) yield return step;
        }

        private static List<object> Values(double[] numbers)
        {
            var items = new List<object>(numbers.Length);
            foreach (double value in numbers) items.Add(value);
            return items;
        }
    }

    /// <summary>What a converted file still does not answer for. THREE lists, because there are three
    /// different questions and merging them has cost this repo a wrong diagnosis before:
    ///
    ///  - MissingNames: prototype bones with no node of that literal name anywhere under Root. This is
    ///    the Doctor's verdict question - Addon.cs:1217 matches names, not paths.
    ///  - MissingPaths: prototype bone PATHS that do not resolve by walking child names down from
    ///    Root. This is the CLIP question - ClipFields.cs:34-41, a generic binding is CRC-32 of a
    ///    path. A file can be perfect by name and useless by path, which is exactly the state ppskel
    ///    exists to leave behind (ppskel.check:244-246 checks only this one).
    ///  - AttachmentsAbsent: EXT_ names the file lacks. Never a defect - the game skips them
    ///    (Addon.cs:1209), so they are counted in neither list above and never fail Ok.
    /// </summary>
    internal sealed class SkelVerdict
    {
        internal List<string> MissingNames = new List<string>();
        internal List<string> MissingPaths = new List<string>();
        internal List<string> AttachmentsAbsent = new List<string>();
        internal int NamesResolved, PathsResolved, Nodes, SkinJoints;
        internal bool Ok => MissingNames.Count == 0 && MissingPaths.Count == 0;

        /// <summary>ppskel's own closing line, in this repo's words.</summary>
        internal string Sentence()
        {
            var text = new List<string>
            {
                NamesResolved + " of " + (NamesResolved + MissingNames.Count) + " bone(s) bind by name",
                PathsResolved + " of " + (PathsResolved + MissingPaths.Count) + " path(s) resolve",
            };
            if (MissingNames.Count > 0) text.Add("no bone named " + Listed(MissingNames));
            if (MissingPaths.Count > 0) text.Add("no path " + Listed(MissingPaths));
            if (AttachmentsAbsent.Count > 0)
                text.Add(AttachmentsAbsent.Count + " attachment point(s) absent, which the game skips anyway");
            return string.Join("; ", text.ToArray());
        }

        /// <summary>The first few, because a rig with 39 missing bones prints as a wall otherwise.</summary>
        private static string Listed(List<string> names)
        {
            var few = names.GetRange(0, Math.Min(4, names.Count));
            return "'" + string.Join("', '", few.ToArray()) + "'" +
                   (names.Count > few.Count ? " and " + (names.Count - few.Count) + " more" : "");
        }
    }

    /// <summary>
    /// The skeleton side of a .glb, read and rewritten through GlbDocument's parsed JSON exactly as
    /// GlbSlim reads the clip side - no Unity, no glTF model, so it handles the files GlbReader
    /// refuses. This file is the port of tools\ppskel.py minus its hard-coded Tiffany map.
    ///
    /// Everything here is double, not float: a collapse composes two matrices and decomposes the
    /// product, and the gates compare world matrices to 1e-9. Values go back into the JSON as double
    /// because Json.Parse hands back double and GlbDocument writes what it is given.
    /// </summary>
    internal static class GlbSkel
    {
        /// <summary>What one run did, for the sentence the panel shows.</summary>
        internal sealed class Stats
        {
            internal int Renamed, Inserted, Collapsed, Created;
        }

        /// <summary>The document's nodes array, or an empty list. Never null.</summary>
        internal static List<object> Nodes(GlbDocument doc) =>
            (doc == null ? null : GlbSlim.Arr(doc.Json, "nodes")) ?? new List<object>();

        /// <summary>parent[i] = the index whose "children" holds i, or -1. Returns null and fills
        /// <paramref name="why"/> when a node has TWO parents - ppskel.check:257-261 asserts exactly
        /// this, because an insert that forgot to unlink the child produces it.</summary>
        internal static int[] Parents(List<object> nodes, out string why)
        {
            why = null;
            var parents = new int[nodes.Count];
            for (int i = 0; i < parents.Length; i++) parents[i] = -1;
            for (int i = 0; i < nodes.Count; i++)
                foreach (object item in Kids(nodes, i))
                {
                    int child = Index(item);
                    if (child < 0 || child >= nodes.Count)
                    {
                        why = "node " + i + " (" + Name(nodes, i) + ") lists a child that is not a node of this file";
                        return null;
                    }
                    if (parents[child] >= 0)
                    {
                        why = "node " + child + " (" + Name(nodes, child) + ") has two parents, " +
                              parents[child] + " and " + i + ", so this is not a skeleton";
                        return null;
                    }
                    parents[child] = i;
                }
            return parents;
        }

        /// <summary>Every node's '/'-joined path from its root (ppskel._paths:348). Index-parallel to
        /// nodes. A node with no name reads as "node&lt;i&gt;", exactly as ppskel spells it.</summary>
        internal static string[] Paths(List<object> nodes, int[] parents)
        {
            var paths = new string[nodes.Count];
            for (int i = 0; i < paths.Length; i++) paths[i] = Name(nodes, i);
            // Walked DOWN from the roots rather than up from each node: a file whose children form a
            // cycle has no root above it, so it is simply never visited and keeps its bare names,
            // where a walk upwards would loop forever. Parents() has already refused a second parent,
            // so no node is prefixed twice.
            var pending = new Stack<int>();
            for (int i = 0; i < parents.Length; i++) if (parents[i] < 0) pending.Push(i);
            while (pending.Count > 0)
            {
                int at = pending.Pop();
                foreach (object item in Kids(nodes, at))
                {
                    int child = Index(item);
                    if (child < 0 || child >= nodes.Count) continue;
                    paths[child] = paths[at] + "/" + paths[child];
                    pending.Push(child);
                }
            }
            return paths;
        }

        /// <summary>Walk a '/'-joined path down from <paramref name="root"/> by child NAME
        /// (ppskel.resolver:220). Returns the node index, or -1 with <paramref name="deepest"/> set to
        /// the last node that DID resolve and <paramref name="missing"/> to the first part that did
        /// not - which is what Create needs to know where to hang a leaf. The EMPTY path is the root
        /// itself: that is the path root motion binds to (crc32("") - ClipFields.cs:38).</summary>
        internal static int Resolve(List<object> nodes, int root, string path,
                                    out int deepest, out string missing)
        {
            deepest = root;
            missing = null;
            if (root < 0 || root >= nodes.Count)
            {
                deepest = -1;
                missing = path;
                return -1;
            }
            if (string.IsNullOrEmpty(path)) return root;
            foreach (string part in path.Split('/'))
            {
                int next = -1;
                foreach (object item in Kids(nodes, deepest))
                {
                    int child = Index(item);
                    if (child >= 0 && child < nodes.Count && Name(nodes, child) == part) { next = child; break; }
                }
                if (next < 0)
                {
                    missing = part;
                    return -1;
                }
                deepest = next;
            }
            return deepest;
        }

        /// <summary>A node's local 4x4, ROW-VECTOR: translation occupies row 3 and world composes as
        /// M(n) = L(n) * M(parent). Reads "matrix" when the node carries one - the key ppskel.trs:122
        /// never looks at, which is why its collapse is silently wrong under a matrix-form node - and
        /// falls back to translation/rotation/scale with glTF's own defaults. glTF stores "matrix"
        /// column-major for a column-vector convention, which is the same 16 numbers in the same
        /// order as row-major row-vector, so it is taken verbatim.</summary>
        internal static double[] Trs(Dictionary<string, object> node)
        {
            double[] matrix = Numbers(GlbSlim.Get(node, "matrix"), 16);
            if (matrix != null) return matrix;

            double[] t = Numbers(GlbSlim.Get(node, "translation"), 3) ?? new double[3];
            double[] q = Numbers(GlbSlim.Get(node, "rotation"), 4) ?? new[] { 0.0, 0.0, 0.0, 1.0 };
            double[] s = Numbers(GlbSlim.Get(node, "scale"), 3) ?? new[] { 1.0, 1.0, 1.0 };
            double x = q[0], y = q[1], z = q[2], w = q[3];
            var m = new double[16];
            m[0] = (1 - 2 * (y * y + z * z)) * s[0]; m[1] = 2 * (x * y + z * w) * s[0]; m[2] = 2 * (x * z - y * w) * s[0];
            m[4] = 2 * (x * y - z * w) * s[1]; m[5] = (1 - 2 * (x * x + z * z)) * s[1]; m[6] = 2 * (y * z + x * w) * s[1];
            m[8] = 2 * (x * z + y * w) * s[2]; m[9] = 2 * (y * z - x * w) * s[2]; m[10] = (1 - 2 * (x * x + y * y)) * s[2];
            m[12] = t[0]; m[13] = t[1]; m[14] = t[2]; m[15] = 1.0;
            return m;
        }

        /// <summary>Row-vector 4x4 product (ppskel.mul:132).</summary>
        internal static double[] Mul(double[] a, double[] b)
        {
            var m = new double[16];
            for (int i = 0; i < 4; i++)
                for (int j = 0; j < 4; j++)
                {
                    double sum = 0;
                    for (int k = 0; k < 4; k++) sum += a[i * 4 + k] * b[k * 4 + j];
                    m[i * 4 + j] = sum;
                }
            return m;
        }

        /// <summary>Inverse of an affine row-vector 4x4. Null when the matrix is singular.</summary>
        internal static double[] Inverse(double[] m)
        {
            double a = m[0], b = m[1], c = m[2];
            double d = m[4], e = m[5], f = m[6];
            double g = m[8], h = m[9], i = m[10];
            double det = a * (e * i - f * h) - b * (d * i - f * g) + c * (d * h - e * g);
            // ponytail: EXACT zero is the singular test, so a near-singular local inverts into huge
            // numbers rather than being refused. Upgrade to a conditioning threshold the day a real
            // file carries a bone scaled to 1e-8 - a plan naming such a node is the author's mistake,
            // and Validate reports the resulting world drift either way.
            if (det == 0 || double.IsNaN(det) || double.IsInfinity(det)) return null;
            double k = 1.0 / det;
            var inv = new double[16];
            inv[0] = (e * i - f * h) * k; inv[1] = (c * h - b * i) * k; inv[2] = (b * f - c * e) * k;
            inv[4] = (f * g - d * i) * k; inv[5] = (a * i - c * g) * k; inv[6] = (c * d - a * f) * k;
            inv[8] = (d * h - e * g) * k; inv[9] = (b * g - a * h) * k; inv[10] = (a * e - b * d) * k;
            double tx = m[12], ty = m[13], tz = m[14];
            inv[12] = -(tx * inv[0] + ty * inv[4] + tz * inv[8]);
            inv[13] = -(tx * inv[1] + ty * inv[5] + tz * inv[9]);
            inv[14] = -(tx * inv[2] + ty * inv[6] + tz * inv[10]);
            inv[15] = 1.0;
            return inv;
        }

        /// <summary>Split a 4x4 back into TRS (ppskel.decompose:136), four-branch quaternion
        /// extraction included. Returns false - rather than producing a mirrored quaternion nothing
        /// can represent - when the upper 3x3 has a negative determinant.</summary>
        internal static bool Decompose(double[] m, out double[] t, out double[] r, out double[] s)
        {
            t = null;
            r = null;
            s = null;
            double a = m[0], b = m[1], c = m[2];
            double d = m[4], e = m[5], f = m[6];
            double g = m[8], h = m[9], i = m[10];
            double det = a * (e * i - f * h) - b * (d * i - f * g) + c * (d * h - e * g);
            if (!(det > 0)) return false;      // also catches NaN, a singular basis and a mirror

            var scale = new[]
            {
                Math.Sqrt(a * a + b * b + c * c),
                Math.Sqrt(d * d + e * e + f * f),
                Math.Sqrt(g * g + h * h + i * i),
            };
            double r00 = a / scale[0], r01 = b / scale[0], r02 = c / scale[0];
            double r10 = d / scale[1], r11 = e / scale[1], r12 = f / scale[1];
            double r20 = g / scale[2], r21 = h / scale[2], r22 = i / scale[2];

            double trace = r00 + r11 + r22, k;
            double[] q;
            if (trace > 0)
            {
                k = Math.Sqrt(trace + 1.0) * 2;
                q = new[] { (r12 - r21) / k, (r20 - r02) / k, (r01 - r10) / k, 0.25 * k };
            }
            else if (r00 > r11 && r00 > r22)
            {
                k = Math.Sqrt(1.0 + r00 - r11 - r22) * 2;
                q = new[] { 0.25 * k, (r10 + r01) / k, (r20 + r02) / k, (r12 - r21) / k };
            }
            else if (r11 > r22)
            {
                k = Math.Sqrt(1.0 + r11 - r00 - r22) * 2;
                q = new[] { (r10 + r01) / k, 0.25 * k, (r21 + r12) / k, (r20 - r02) / k };
            }
            else
            {
                k = Math.Sqrt(1.0 + r22 - r00 - r11) * 2;
                q = new[] { (r20 + r02) / k, (r21 + r12) / k, 0.25 * k, (r01 - r10) / k };
            }

            t = new[] { m[12], m[13], m[14] };
            r = q;
            s = scale;
            return true;
        }

        /// <summary>
        /// EVERY reason this plan cannot be applied to this document, all of them at once. Empty list
        /// = go. Mutates nothing, so a caller can show the list and let the author fix the plan, and
        /// nothing in Apply re-checks: a plan that reaches it has already been proven applicable.
        ///
        /// Checked against the state each phase actually sees - renames first, so a Collapse or an
        /// Insert names PP's bones rather than the foreign ones, which is the order convert applies
        /// them in (ppskel.py:281, :285, :301) and the only order in which a plan reads like a
        /// sentence. ppskel asserts instead (convert:277, :286; check:241-261) and dies; this is
        /// reached from OnGUI, where a throw tears the bench panel down mid-frame.
        /// </summary>
        /// <param name="targetBones">the prototype this plan is aiming at, or null.</param>
        // ponytail: targetBones produces NO refusal. WHICH bones the prototype wants is Verify's
        // question and it is asked of the WRITTEN file; refusing a rename whose target is not a
        // prototype bone would refuse every legitimate rename of a node that is not a bone at all.
        // The parameter stays because SlimJob.Skel hands it down, so a target-side refusal that
        // turns out to be real lands here rather than in a new signature.
        internal static IList<string> Validate(GlbDocument doc, SkelPlan plan, IList<string> targetBones)
        {
            var refusals = new List<string>();
            if (plan == null)
            {
                refusals.Add("there is no plan to apply");
                return refusals;
            }
            List<object> nodes = Nodes(doc);
            int[] parents = Parents(nodes, out string why);
            if (parents == null)
            {
                refusals.Add(why + ", so no rewrite of it can be trusted; re-export the file rather " +
                             "than editing its JSON by hand");
                return refusals;
            }

            // The state each phase will SEE, simulated in Apply's own order: names change under
            // renames and collapses, parents change under collapses and inserts, and both lists grow
            // as inserts and creates append. The document itself is never touched.
            var names = new List<string>(nodes.Count);
            for (int i = 0; i < nodes.Count; i++) names.Add(Name(nodes, i));
            var original = new List<string>(names);
            var owner = new List<int>(parents);

            int root = -1;
            if (!string.IsNullOrEmpty(plan.Root))
            {
                int carriers = Carriers(original, plan.Root, out root);
                if (carriers == 0)
                    refusals.Add("the plan's root '" + plan.Root + "' names no node in this file; spell " +
                                 "it the way the file does - every path a clip binds to is measured from it");
                else if (carriers > 1)
                {
                    refusals.Add("the plan's root '" + plan.Root + "' names " + carriers + " nodes in this " +
                                 "file, so which one the paths start under is not decided; rename one of " +
                                 "them in Blender, or point 'root' at a name only one node carries");
                    root = -1;
                }
            }

            // RENAME. Every From is resolved against the ORIGINAL table, so the plan is read as one
            // simultaneous move - the rule AliasMap.Apply keeps (AliasMap.cs:60-63).
            var claimed = new Dictionary<string, int>(StringComparer.Ordinal);
            var renamed = new List<int>(plan.Renames.Count);
            foreach (SkelRename step in plan.Renames)
            {
                int at = Named(original, step.From, "the rename to '" + step.To + "'", refusals);
                if (string.IsNullOrEmpty(step.To))
                    refusals.Add("the rename of '" + step.From + "' names no target, so there is nothing " +
                                 "to write; give it a 'to'");
                else if (SkinBinder.Plain(step.To) != step.To)
                    refusals.Add("the rename target '" + step.To + "' carries the game's own decoration " +
                                 "('#<bone>_Addon => <part>', Addon.cs:143), and Addon.GetEquivalentBones " +
                                 "compares the literal Transform.name (Addon.cs:1217) - so a node named " +
                                 "that would bind to nothing; write the plain bone name instead");
                // Taken-ness is asked EXACTLY, unlike From: two nodes only collide when the game reads
                // one literal name twice, and a decorated node is a different literal name.
                else if (Exact(original, step.To, out int taken) > 0 && taken != at)
                    refusals.Add("this file already has a bone called '" + step.To + "', so renaming '" +
                                 step.From + "' onto it would leave two bones with one name and the game " +
                                 "would bind the wrong one; pick a name nothing in the file carries");
                else if (claimed.ContainsKey(step.To))
                    refusals.Add("this plan renames two of the file's bones onto '" + step.To + "', and the " +
                                 "game matches a bone by its literal name, so one of them could never bind; " +
                                 "give each rename its own target");
                else claimed[step.To] = at;
                renamed.Add(at);
            }
            // ponytail: a SWAP (A->B while B->A) is refused by the taken-ness arm above even though
            // Apply's simultaneity could carry it. Refusing is the safe direction and the plan can be
            // written in two runs; lift it the day a real rig needs one, by excusing a target whose
            // own node is being renamed away in the same plan.
            for (int i = 0; i < renamed.Count; i++)
                if (renamed[i] >= 0 && !string.IsNullOrEmpty(plan.Renames[i].To))
                    names[renamed[i]] = plan.Renames[i].To;

            // COLLAPSE, against the post-rename table.
            var hoisted = new List<int>();
            foreach (SkelCollapse step in plan.Collapses)
            {
                int node = Named(names, step.Node, "the collapse onto '" + step.Into + "'", refusals);
                int into = Named(names, step.Into, "the collapse of '" + step.Node + "'", refusals);
                if (node < 0 || into < 0) continue;
                int parent = owner[node];
                if (parent < 0)
                {
                    refusals.Add("'" + step.Node + "' is a root of this file's scene, so there is no parent " +
                                 "to skip and nothing to collapse; collapse a bone that has a grandparent");
                    continue;
                }
                int grand = owner[parent];
                if (grand != into)
                {
                    refusals.Add("'" + step.Into + "' is not the grandparent of '" + step.Node + "': the file " +
                                 "hangs it under '" + names[parent] + "'" +
                                 (grand < 0 ? ", and that is a scene root" : ", whose own parent is '" + names[grand] + "'") +
                                 "; a collapse skips exactly ONE node, so name that grandparent instead");
                    continue;
                }
                // ponytail: the composed local is read from the DOCUMENT, so two collapses down one
                // chain check the first one's arithmetic twice. Both ask the same mirror question and
                // a product of positive determinants keeps one, so no wrong answer gets through;
                // carry the simulated local here the day a plan really chains them.
                if (!Decompose(Mul(Trs(GlbSlim.Obj(nodes[node])), Trs(GlbSlim.Obj(nodes[parent]))),
                               out _, out _, out _))
                {
                    refusals.Add("collapsing '" + step.Node + "' past '" + names[parent] + "' composes a local " +
                                 "transform no translation/rotation/scale can hold (together they mirror or " +
                                 "flatten the bone), so the result would not be the pose the file shows; fix " +
                                 "those scales in Blender and re-export");
                    continue;
                }
                hoisted.Add(node);
                owner[node] = grand;
                names[parent] = names[parent] + "_unused";     // ppskel.py:297
            }

            // INSERT, against the post-rename-post-collapse table.
            var compensated = new List<int>();
            foreach (SkelInsert step in plan.Inserts)
            {
                int parent = Named(names, step.Parent, "the insert of '" + step.Name + "'", refusals);
                int child = Named(names, step.Child, "the insert of '" + step.Name + "'", refusals);
                if (parent < 0 || child < 0) continue;
                if (owner[child] != parent)
                {
                    refusals.Add("'" + step.Child + "' is not a child of '" + step.Parent + "' in this file" +
                                 (owner[child] < 0 ? ", it is a scene root" : ", it hangs under '" + names[owner[child]] + "'") +
                                 "; an insert only ever slips between a parent and its OWN child");
                    continue;
                }
                if (string.IsNullOrEmpty(step.Name))
                {
                    refusals.Add("an insert under '" + step.Parent + "' names the new bone nothing, and a " +
                                 "nameless bone binds to nothing; give it the name the rig spells");
                    continue;
                }
                if (Exact(names, step.Name, out _) > 0)
                {
                    refusals.Add("this file already has a bone called '" + step.Name + "', so the insert would " +
                                 "leave two bones with one name and the game would bind the wrong one; give " +
                                 "the new bone a name nothing carries");
                    continue;
                }
                if (Wrong(step.Translation, 3) || Wrong(step.Rotation, 4) || Wrong(step.Scale, 3))
                {
                    refusals.Add("the insert of '" + step.Name + "' carries a transform that is not 3 " +
                                 "translation, 4 rotation and 3 scale numbers, so what it would write is not " +
                                 "a transform at all; leave it out for an identity bone, or spell it in full");
                    continue;
                }
                double[] local = Local(step);
                if (!Same(local, Eye))
                {
                    double[] undo = Inverse(local);
                    if (undo == null)
                    {
                        refusals.Add("the insert of '" + step.Name + "' has a transform that cannot be undone " +
                                     "(an axis is scaled to nothing), and the child's own local is compensated " +
                                     "with its inverse - so the model would collapse; give it a scale no axis zeroes");
                        continue;
                    }
                    // Invertible is not enough: a MIRROR inverts perfectly and still leaves the child
                    // with a matrix no TRS can hold, which Apply would only discover mid-write. The
                    // collapse arm asks its composition the same question, and for the same reason - a
                    // clean Validate is a promise that Apply cannot throw.
                    // ponytail: read from the DOCUMENT, so a child that is itself a node an earlier
                    // step appended is not composed here (there is no such local to read yet). Carry
                    // the simulated locals the day a plan really stacks inserts down one chain.
                    if (child < nodes.Count &&
                        !Decompose(Mul(Trs(GlbSlim.Obj(nodes[child])), undo), out _, out _, out _))
                    {
                        refusals.Add("the insert of '" + step.Name + "' has a transform that would leave '" +
                                     step.Child + "' with a local transform no translation/rotation/scale can " +
                                     "hold (compensating for it mirrors or flattens the bone), so the result " +
                                     "would not be the pose the file shows; give the new bone a transform with " +
                                     "no negative or zero scale");
                        continue;
                    }
                    compensated.Add(child);
                }
                names.Add(step.Name);
                owner.Add(parent);
                owner[child] = names.Count - 1;
            }

            // CREATE. Sorted by depth, exactly as ppskel.py:322 sorts by p.count("/"), so a two-level
            // create works in one pass.
            var creates = new List<string>(plan.Create);
            creates.Sort((left, right) => Depth(left).CompareTo(Depth(right)));
            foreach (string path in creates)
            {
                if (string.IsNullOrEmpty(path))
                {
                    refusals.Add("one of the plan's 'create' entries is empty, so it names nothing to create");
                    continue;
                }
                if (root < 0)
                {
                    // A root that was already refused says so once; a plan that never named one is a
                    // different mistake and gets its own sentence.
                    if (string.IsNullOrEmpty(plan.Root))
                        refusals.Add("the plan creates '" + path + "' but never says which node is its root, " +
                                     "so there is nowhere to hang it; set 'root' to the node these paths are " +
                                     "measured from");
                    continue;
                }
                string[] parts = path.Split('/');
                int at = root;
                bool lost = false;
                for (int i = 0; i < parts.Length - 1 && !lost; i++)
                {
                    int next = ChildNamed(names, owner, at, parts[i]);
                    if (next < 0)
                    {
                        refusals.Add("the plan creates '" + path + "', but '" + names[at] + "' has no child " +
                                     "called '" + parts[i] + "'; nothing is ever invented here, so create the " +
                                     "missing parents too - this pass hangs the shallower ones first");
                        lost = true;
                    }
                    else at = next;
                }
                if (lost) continue;
                string leaf = parts[parts.Length - 1];
                if (ChildNamed(names, owner, at, leaf) >= 0)
                {
                    refusals.Add("the plan creates '" + path + "', but '" + names[at] + "' already has a child " +
                                 "called '" + leaf + "', so there is nothing to create; drop it from 'create'");
                    continue;
                }
                names.Add(leaf);
                owner.Add(at);
            }

            // THE ANIMATION ARM, last. A collapse rewrites the kept bone's own local and a
            // non-identity insert rewrites its child's, so a channel that writes that local every
            // frame overwrites the composition on frame 1 and the geometry jumps. ppskel does not
            // care - it throws the source's clips away - and this port cannot assume that.
            if (hoisted.Count > 0 || compensated.Count > 0)
            {
                Dictionary<int, string> animated = Animated(doc);
                foreach (int node in hoisted)
                    if (animated.TryGetValue(node, out string clip))
                        refusals.Add("'" + names[node] + "' is animated by the clip '" + clip + "', and a " +
                                     "collapse rewrites that bone's own local transform - the clip would " +
                                     "overwrite it on its first frame and the model would jump; drop that " +
                                     "channel, or leave this bone where it is");
                foreach (int child in compensated)
                    if (animated.TryGetValue(child, out string clip))
                        refusals.Add("'" + names[child] + "' is animated by the clip '" + clip + "', and a " +
                                     "non-identity insert compensates that bone's own local transform - the " +
                                     "clip would overwrite the compensation on its first frame and the model " +
                                     "would jump; use an identity insert (no translation/rotation/scale), or " +
                                     "drop that channel");
            }
            return refusals;
        }

        /// <summary>
        /// Apply a VALIDATED plan. Four phases in ppskel's own order (ppskel.py:281, :285, :301,
        /// :316), each one geometry-preserving, and between them they never delete a node, reorder
        /// one, or take one out of skin.joints. That is not tidiness - glTF skinning is INDEX-based
        /// (skin.joints[] is parallel to inverseBindMatrices and a vertex names a joint by its slot),
        /// so any of those three would silently re-bind every vertex in the file. Because none of
        /// them happens, doc.Bin comes out reference-identical and the inverse bind matrices need no
        /// recompute at all: an unchanged world matrix is an unchanged bind pose, which the gate
        /// measures rather than argues.
        ///
        /// Call Validate first. This method assumes what Validate proved and throws
        /// InvalidOperationException rather than write a broken document if that assumption is false.
        /// </summary>
        internal static Stats Apply(GlbDocument doc, SkelPlan plan)
        {
            var stats = new Stats();
            if (plan == null) return stats;
            List<object> nodes = Nodes(doc);
            var names = new List<string>(nodes.Count);
            for (int i = 0; i < nodes.Count; i++) names.Add(Name(nodes, i));

            // RENAME (ppskel.py:281-283). Every From is resolved against the ORIGINAL table before
            // any of them is written, so a plan that moves two names does not apply one half onto the
            // other's result - the simultaneity rule AliasMap.Apply keeps (AliasMap.cs:60-63).
            var moving = new int[plan.Renames.Count];
            for (int i = 0; i < moving.Length; i++) moving[i] = One(names, plan.Renames[i].From);
            for (int i = 0; i < moving.Length; i++)
            {
                string to = plan.Renames[i].To;
                if (string.Equals(names[moving[i]], to, StringComparison.Ordinal)) continue;
                GlbSlim.Obj(nodes[moving[i]])["name"] = to;
                names[moving[i]] = to;
                stats.Renamed++;
            }

            var owner = new List<int>();
            if (plan.Collapses.Count > 0 || plan.Inserts.Count > 0)
            {
                int[] parents = Parents(nodes, out string why);
                // An empty plan never gets here, which is what keeps the no-op run byte-identical
                // even on a file this would refuse.
                if (parents == null) throw new InvalidOperationException(why + "; Validate had to run first");
                owner.AddRange(parents);
            }

            // COLLAPSE (ppskel.py:285-297). The kept node moves onto its grandparent with the skipped
            // node's local composed in, so its world matrix is unchanged; the skipped node stays as a
            // childless leaf, because removing it would renumber every index in the file.
            foreach (SkelCollapse step in plan.Collapses)
            {
                int node = One(names, step.Node), into = One(names, step.Into);
                int parent = owner[node];
                if (parent < 0 || owner[parent] != into)
                    throw new InvalidOperationException("'" + step.Node + "' is not a grandchild of '" +
                                                        step.Into + "' in this document; Validate had to run first");
                Unlink(nodes, parent, node);
                Link(nodes, into, node);
                owner[node] = into;
                Rewrite(GlbSlim.Obj(nodes[node]),
                        Mul(Trs(GlbSlim.Obj(nodes[node])), Trs(GlbSlim.Obj(nodes[parent]))), step.Node);
                names[parent] += "_unused";
                GlbSlim.Obj(nodes[parent])["name"] = names[parent];
                stats.Collapsed++;
            }

            // INSERT (ppskel.py:301-313). APPENDED past the end of nodes[], so no existing index ever
            // changes meaning. An identity local preserves the child's world matrix by construction;
            // a non-identity one is compensated on the child, L_child' = L_child * inverse(L_new).
            foreach (SkelInsert step in plan.Inserts)
            {
                int parent = One(names, step.Parent), child = One(names, step.Child);
                if (owner[child] != parent)
                    throw new InvalidOperationException("'" + step.Child + "' is not a child of '" + step.Parent +
                                                        "' in this document; Validate had to run first");
                var fresh = new Dictionary<string, object>(StringComparer.Ordinal) { { "name", step.Name } };
                if (step.Translation != null) fresh["translation"] = Boxed(step.Translation);
                if (step.Rotation != null) fresh["rotation"] = Boxed(step.Rotation);
                if (step.Scale != null) fresh["scale"] = Boxed(step.Scale);
                nodes.Add(fresh);
                int at = nodes.Count - 1;
                names.Add(step.Name);
                owner.Add(parent);

                Unlink(nodes, parent, child);
                Link(nodes, parent, at);
                Link(nodes, at, child);
                owner[child] = at;

                double[] local = Local(step);
                if (!Same(local, Eye))
                {
                    double[] undo = Inverse(local);
                    if (undo == null)
                        throw new InvalidOperationException("the insert of '" + step.Name + "' has a transform " +
                                                            "that cannot be undone; Validate had to run first");
                    Rewrite(GlbSlim.Obj(nodes[child]), Mul(Trs(GlbSlim.Obj(nodes[child])), undo), step.Child);
                }
                stats.Inserted++;
            }

            // CREATE (ppskel.py:322-328, explicit paths only). Sorted by depth so a two-level create
            // works in one pass, exactly as ppskel sorts by p.count("/").
            var creates = new List<string>(plan.Create);
            creates.Sort((left, right) => Depth(left).CompareTo(Depth(right)));
            if (creates.Count > 0)
            {
                int root = One(names, plan.Root);
                foreach (string path in creates)
                {
                    int found = Resolve(nodes, root, path, out int deepest, out string missing);
                    string leaf = path.Substring(path.LastIndexOf('/') + 1);
                    if (found >= 0 || deepest < 0 || !string.Equals(missing, leaf, StringComparison.Ordinal))
                        throw new InvalidOperationException("the plan creates '" + path + "', which this document " +
                                                            "either already carries or has no parent for; Validate " +
                                                            "had to run first");
                    nodes.Add(new Dictionary<string, object>(StringComparer.Ordinal) { { "name", leaf } });
                    Link(nodes, deepest, nodes.Count - 1);
                    names.Add(leaf);
                    stats.Created++;
                }
            }

            // Only a run that DID something re-serialises. An empty plan leaves GlbDocument writing
            // its original JSON bytes verbatim (GlbDocument.cs:91-92), which is the cheapest possible
            // proof that no phase touched a file it had nothing to do to.
            if (stats.Renamed + stats.Collapsed + stats.Inserted + stats.Created > 0) doc.Dirty = true;
            return stats;
        }

        /// <summary>
        /// What this document still does not answer for, asked of the FILE and of nothing else - no
        /// plan, no history, no in-memory state. That is deliberate: the game asks the file, so a
        /// verdict that needed the plan in hand would be checking the tool's own opinion of its work.
        /// This is ppskel.check:237-264 as a return value rather than as an assert (an assert inside
        /// OnGUI tears the bench panel down mid-frame).
        /// </summary>
        /// <param name="rootName">the node PP's paths are measured from. When it names exactly one
        /// node, names are looked for in ITS subtree and paths are walked down from it; when it names
        /// none or two, names are looked for across the whole file and no path can resolve at all.</param>
        /// <param name="targetNames">the prototype's BindableBones. Null asks only the path question.</param>
        /// <param name="targetPaths">the prototype's bone paths, relative to the animator root -
        /// PrototypeBone.Path (src\Doctor\PrototypeCatalog.cs:11). Null asks only the name question.</param>
        internal static SkelVerdict Verify(GlbDocument doc, string rootName,
                                           IList<string> targetNames, IList<string> targetPaths)
        {
            List<object> nodes = Nodes(doc);
            var verdict = new SkelVerdict { Nodes = nodes.Count, SkinJoints = JointCount(doc) };
            var names = new List<string>(nodes.Count);
            for (int i = 0; i < nodes.Count; i++) names.Add(Name(nodes, i));

            int root = -1;
            if (!string.IsNullOrEmpty(rootName) && Carriers(names, rootName, out int at) == 1) root = at;

            // Every name under the root, decorated AND undecorated: a node the engine renamed to
            // '#<bone>_Addon => <part>' (Addon.cs:143) still ANSWERS for the bone it names. The
            // asymmetry SkinCompatibility keeps (SkinCompatibility.cs:203-215) - the FILE side is
            // undecorated, and a plan never writes a decorated name.
            var under = new HashSet<string>(StringComparer.Ordinal);
            foreach (int node in Subtree(nodes, root))
            {
                under.Add(names[node]);
                under.Add(SkinBinder.Plain(names[node]));
            }

            foreach (string bone in targetNames ?? NoWords)
            {
                if (string.IsNullOrEmpty(bone)) continue;
                if (Doctor.PrototypeCatalog.IsAttachmentPoint(bone))
                {
                    if (!under.Contains(bone)) verdict.AttachmentsAbsent.Add(bone);
                    continue;
                }
                if (under.Contains(bone)) verdict.NamesResolved++;
                else verdict.MissingNames.Add(bone);
            }

            // The PATH question is asked with EXACT names all the way down, and that is not an
            // oversight: a generic binding is crc32 of the literal path (ClipFields.cs:34-41), so a
            // decorated node genuinely does not answer for the path its plain name would spell.
            foreach (string path in targetPaths ?? NoWords)
            {
                if (string.IsNullOrEmpty(path)) continue;
                string leaf = path.Substring(path.LastIndexOf('/') + 1);
                if (Doctor.PrototypeCatalog.IsAttachmentPoint(leaf)) continue;   // skipped, as above
                if (root >= 0 && Resolve(nodes, root, path, out _, out _) >= 0) verdict.PathsResolved++;
                else verdict.MissingPaths.Add(path);
            }
            return verdict;
        }

        /// <summary>Every node at or below <paramref name="root"/>, or the whole file when there is
        /// no root to start from. The visited set is not tidiness: Verify is handed files nothing has
        /// validated, and a "children" array that points back at an ancestor would otherwise spin
        /// forever inside OnGUI.</summary>
        private static IEnumerable<int> Subtree(List<object> nodes, int root)
        {
            if (root < 0)
            {
                for (int i = 0; i < nodes.Count; i++) yield return i;
                yield break;
            }
            var seen = new bool[nodes.Count];
            var pending = new Stack<int>();
            pending.Push(root);
            seen[root] = true;
            while (pending.Count > 0)
            {
                int at = pending.Pop();
                yield return at;
                foreach (object item in Kids(nodes, at))
                {
                    int child = Index(item);
                    if (child < 0 || child >= nodes.Count || seen[child]) continue;
                    seen[child] = true;
                    pending.Push(child);
                }
            }
        }

        /// <summary>How many joint slots this file's skins carry, for the sentence.</summary>
        private static int JointCount(GlbDocument doc)
        {
            int total = 0;
            foreach (object item in (doc == null ? null : GlbSlim.Arr(doc.Json, "skins")) ?? Nothing)
                total += (GlbSlim.Arr(GlbSlim.Obj(item), "joints") ?? Nothing).Count;
            return total;
        }

        /// <summary>The one node this name reaches, or the exception that says Validate was skipped.</summary>
        private static int One(List<string> names, string name)
        {
            int carriers = Carriers(names, name ?? "", out int at);
            if (carriers != 1)
                throw new InvalidOperationException("this document has " + carriers + " bones called '" + name +
                                                    "', so the plan cannot be applied to it; Validate had to run " +
                                                    "first and its refusals had to be shown to the author");
            return at;
        }

        /// <summary>Drop a child index, and the key with it when that empties the array - glTF has no
        /// empty arrays and ppskel.py:290-291 deletes it for the same reason.</summary>
        private static void Unlink(List<object> nodes, int parent, int child)
        {
            Dictionary<string, object> node = GlbSlim.Obj(nodes[parent]);
            List<object> kids = GlbSlim.Arr(node, "children");
            if (kids == null) return;
            for (int i = 0; i < kids.Count; i++)
                if (Index(kids[i]) == child) { kids.RemoveAt(i); break; }
            if (kids.Count == 0) node.Remove("children");
        }

        private static void Link(List<object> nodes, int parent, int child)
        {
            Dictionary<string, object> node = GlbSlim.Obj(nodes[parent]);
            List<object> kids = GlbSlim.Arr(node, "children");
            if (kids == null) node["children"] = kids = new List<object>();
            kids.Add((double)child);
        }

        /// <summary>Write a composed local back as translation/rotation/scale, dropping any "matrix"
        /// the node carried (ppskel.py:295) - which ppskel does WITHOUT ever having read it, and is
        /// why its collapse under a matrix-form node is silently wrong.</summary>
        private static void Rewrite(Dictionary<string, object> node, double[] local, string what)
        {
            if (!Decompose(local, out double[] t, out double[] r, out double[] s))
                throw new InvalidOperationException("'" + what + "' composes a local transform no " +
                                                    "translation/rotation/scale can hold; Validate had to run first");
            node.Remove("matrix");
            node["translation"] = Boxed(t);
            node["rotation"] = Boxed(r);
            node["scale"] = Boxed(s);
        }

        /// <summary>The local 4x4 an insert asks for. Null members are glTF's own defaults, which is
        /// the identity - the world-preserving case and the only one ppskel emits (ppskel.py:307).</summary>
        internal static double[] Local(SkelInsert step)
        {
            var node = new Dictionary<string, object>(StringComparer.Ordinal);
            if (step.Translation != null) node["translation"] = Boxed(step.Translation);
            if (step.Rotation != null) node["rotation"] = Boxed(step.Rotation);
            if (step.Scale != null) node["scale"] = Boxed(step.Scale);
            return Trs(node);
        }

        /// <summary>node index -> the FIRST clip that animates it. An author cannot act on "some
        /// clip", so the refusal names one.</summary>
        private static Dictionary<int, string> Animated(GlbDocument doc)
        {
            var animated = new Dictionary<int, string>();
            List<object> clips = doc == null ? null : GlbSlim.Arr(doc.Json, "animations");
            if (clips == null) return animated;
            for (int i = 0; i < clips.Count; i++)
            {
                Dictionary<string, object> clip = GlbSlim.Obj(clips[i]);
                string name = GlbSlim.Str(clip, "name") ?? ("animation " + i);
                foreach (object item in GlbSlim.Arr(clip, "channels") ?? Nothing)
                {
                    var target = GlbSlim.Obj(GlbSlim.Get(GlbSlim.Obj(item), "target"));
                    int node = GlbSlim.Int(target, "node", -1);
                    if (node >= 0 && !animated.ContainsKey(node)) animated[node] = name;
                }
            }
            return animated;
        }

        /// <summary>One node the plan reached for, or -1 with the sentence appended.</summary>
        private static int Named(List<string> names, string name, string what, List<string> refusals)
        {
            int carriers = Carriers(names, name ?? "", out int at);
            if (carriers == 0)
                refusals.Add("this file has no bone called '" + name + "', so " + what + " cannot be applied; " +
                             "check the spelling against the file's own bone list - the Doctor's bone map prints it");
            else if (carriers > 1)
                refusals.Add("this file has two bones called '" + name + "', so " + what + " cannot say which " +
                             "one it means; rename one of them in Blender and re-export");
            return carriers == 1 ? at : -1;
        }

        /// <summary>How many nodes answer to this name, EXACTLY.</summary>
        private static int Exact(List<string> names, string name, out int first)
        {
            first = -1;
            int found = 0;
            for (int i = 0; i < names.Count; i++)
                if (string.Equals(names[i], name, StringComparison.Ordinal) && found++ == 0) first = i;
            return found;
        }

        /// <summary>How many nodes answer to this name, exact spelling FIRST and only then through
        /// SkinBinder.Plain - the same two-call pattern SkinCompatibility.Match keeps
        /// (SkinCompatibility.cs:334-338), so a plan written against plain names still finds a node
        /// the game decorated while a plainly-named file behaves identically.</summary>
        private static int Carriers(List<string> names, string name, out int first)
        {
            int found = Exact(names, name, out first);
            if (found > 0) return found;
            for (int i = 0; i < names.Count; i++)
                if (string.Equals(SkinBinder.Plain(names[i]), name, StringComparison.Ordinal) && found++ == 0)
                    first = i;
            return found;
        }

        /// <summary>The child of <paramref name="parent"/> spelling this name, in the SIMULATED
        /// hierarchy, or -1.</summary>
        private static int ChildNamed(List<string> names, List<int> owner, int parent, string name)
        {
            for (int i = 0; i < names.Count; i++)
                if (owner[i] == parent && string.Equals(names[i], name, StringComparison.Ordinal)) return i;
            return -1;
        }

        private static bool Wrong(double[] values, int count) => values != null && values.Length != count;

        private static bool Same(double[] left, double[] right)
        {
            for (int i = 0; i < 16; i++) if (left[i] != right[i]) return false;
            return true;
        }

        private static int Depth(string path)
        {
            int depth = 0;
            foreach (char c in path ?? "") if (c == '/') depth++;
            return depth;
        }

        private static List<object> Boxed(double[] values)
        {
            var items = new List<object>(values.Length);
            foreach (double value in values) items.Add(value);
            return items;
        }

        private static readonly double[] Eye = { 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1 };

        /// <summary>A JSON array of exactly <paramref name="count"/> numbers, or null. A wrong length
        /// or a non-number reads as ABSENT, never as a throw - the contract GlbSlim's readers keep
        /// (GlbSlim.cs:370-372) and the reason a hostile file cannot crash the bench.</summary>
        internal static double[] Numbers(object value, int count)
        {
            if (!(value is List<object> items) || items.Count != count) return null;
            var numbers = new double[count];
            for (int i = 0; i < count; i++)
            {
                if (!(items[i] is double number)) return null;
                numbers[i] = number;
            }
            return numbers;
        }

        /// <summary>A node's name, or "node&lt;i&gt;" when it carries none (ppskel._paths:354).</summary>
        internal static string Name(List<object> nodes, int index) =>
            GlbSlim.Str(GlbSlim.Obj(nodes[index]), "name") ?? ("node" + index);

        private static List<object> Kids(List<object> nodes, int index) =>
            GlbSlim.Arr(GlbSlim.Obj(nodes[index]), "children") ?? Nothing;

        private static int Index(object item) =>
            item is double number && number >= 0 && number <= int.MaxValue ? (int)number : -1;

        private static readonly List<object> Nothing = new List<object>();

        private static readonly List<string> NoWords = new List<string>();
    }
}
