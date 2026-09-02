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
        // Stats (what one run renamed, inserted, collapsed and created) lands with Apply, which is the
        // only thing that can fill it in - a struct nothing assigns is four CS0649 warnings and no
        // information.

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
    }
}
