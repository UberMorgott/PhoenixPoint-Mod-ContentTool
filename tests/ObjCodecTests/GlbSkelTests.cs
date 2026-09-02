using System;
using System.Collections.Generic;
using System.IO;
using Morgott.ContentTool.Import;

/// <summary>
/// SKEL: the node graph and the 4x4 arithmetic every skeleton edit is built out of. Nothing here
/// rewrites a file - it answers the three questions that decide whether a rewrite is even legal:
/// who is whose parent, what path does a clip spell for this node, and what is this node's local
/// matrix.
///
/// The arithmetic is checked by value rather than by eye because every error it can make produces a
/// file that loads perfectly and stands in the wrong place: a transposed rotation, a quaternion off
/// by a branch, an inverse that forgot the translation row. The fixtures are the two committed
/// probes, and u9's THREE scene roots are the reason the parent pass is checked at all - a port that
/// assumed one root would be wrong on the very first file it met.
///
/// Falsified by composing with the column-vector convention (the Decompose round trip goes red), by
/// reading translation/rotation/scale on a node that carries "matrix" (the verbatim check goes red),
/// or by walking paths upward through a cycle (the gate hangs instead of failing).
/// </summary>
internal static class GlbSkelTests
{
    private static int checks;

    internal static string Run()
    {
        checks = 0;

        // 1. The spider's 42 nodes hang off exactly one root, RootNode at index 0.
        List<object> u8 = GlbSkel.Nodes(GlbDocument.Load(Fixture("u8_probe.glb")));
        int[] p8 = GlbSkel.Parents(u8, out string why8);
        Check(why8 == null && p8 != null && p8.Length == 42 && Roots(p8) == "0",
              "u8_probe.glb parents 42 nodes off the single root 0, not " + (why8 ?? Roots(p8)));

        // 2. The tiny probe has THREE scene roots (rig, body, prop). A pass that assumed one would
        //    be wrong here, not in some hypothetical file.
        List<object> u9 = GlbSkel.Nodes(GlbDocument.Load(Fixture("u9_probe.glb")));
        int[] p9 = GlbSkel.Parents(u9, out string why9);
        Check(why9 == null && p9 != null && p9.Length == 5 && Roots(p9) == "0,3,4",
              "u9_probe.glb has 5 nodes and 3 roots, not " + (why9 ?? Roots(p9)));

        // 3. The path a generic clip binds to is the '/'-joined walk from the node's own root
        //    (ClipFields.cs:34-41), so a root's path is its bare name and nothing else.
        string[] paths = GlbSkel.Paths(u9, p9);
        Check(paths[2] == "rig/hip/head" && paths[4] == "prop",
              "u9 head is 'rig/hip/head' and prop is 'prop', not '" + paths[2] + "' and '" + paths[4] + "'");

        // 4. Resolve walks by child NAME and, when it fails, says where it got to and what it wanted
        //    - which is exactly what a Create needs to know where to hang its leaf.
        int found = GlbSkel.Resolve(u9, 0, "hip/head", out int deepHit, out string missHit);
        int lost = GlbSkel.Resolve(u9, 0, "hip/neck", out int deepMiss, out string missMiss);
        Check(found == 2 && deepHit == 2 && missHit == null &&
              lost == -1 && deepMiss == 1 && missMiss == "neck",
              "Resolve finds rig/hip/head and reports hip + 'neck' for rig/hip/neck");

        // 5. The local matrix: "matrix" wins when a node carries one - the key ppskel.trs:122 never
        //    reads - an empty node is identity, and an inverse undoes its own matrix.
        double[] sixteen = { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16 };
        var carried = new Dictionary<string, object>(StringComparer.Ordinal)
        {
            { "matrix", Values(sixteen) },
            { "translation", Values(new double[] { 99, 99, 99 }) },   // must be IGNORED
        };
        double[] a = GlbSkel.Trs(Node(new double[] { 1, 2, 3 }, Quat(0, 1, 0, 30), new double[] { 2, 0.5, 1.5 }));
        Check(Same(GlbSkel.Trs(carried), sixteen, 0) &&
              Same(GlbSkel.Trs(new Dictionary<string, object>(StringComparer.Ordinal)), Identity, 0) &&
              Same(GlbSkel.Mul(a, GlbSkel.Inverse(a)), Identity, 1e-12),
              "Trs reads 'matrix' verbatim, defaults to identity, and Inverse undoes it");

        // 6. Decompose is the composition read backwards. B's scale is uniform on purpose: a
        //    non-uniform scale BETWEEN two different rotations is a shear, and no TRS can hold one.
        //    A mirror cannot be held either, and is refused rather than silently mangled.
        double[] b = GlbSkel.Trs(Node(new double[] { -4, 5, 0.25 }, Quat(1, -2, 0.5, 110), new double[] { 3, 3, 3 }));
        double[] composed = GlbSkel.Mul(a, b);
        bool split = GlbSkel.Decompose(composed, out double[] t, out double[] r, out double[] s);
        double[] mirrored = GlbSkel.Trs(Node(new double[] { 0, 0, 0 }, Quat(0, 0, 1, 45), new double[] { -1, 1, 1 }));
        Check(split && Same(GlbSkel.Trs(Node(t, r, s)), composed, 1e-9) &&
              !GlbSkel.Decompose(mirrored, out _, out _, out _),
              "Decompose round-trips a composed matrix to 1e-9 and refuses a mirror");

        return "GLB-SKEL PASS, " + checks + " check(s)";
    }

    private static readonly double[] Identity = { 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1 };

    /// <summary>The indices with no parent, comma-joined, so a wrong answer prints as one.</summary>
    private static string Roots(int[] parents)
    {
        if (parents == null) return "<refused>";
        var roots = new List<string>();
        for (int i = 0; i < parents.Length; i++) if (parents[i] < 0) roots.Add(i.ToString());
        return string.Join(",", roots.ToArray());
    }

    private static Dictionary<string, object> Node(double[] t, double[] r, double[] s) =>
        new Dictionary<string, object>(StringComparer.Ordinal)
        {
            { "translation", Values(t) }, { "rotation", Values(r) }, { "scale", Values(s) },
        };

    private static List<object> Values(double[] numbers)
    {
        var items = new List<object>(numbers.Length);
        foreach (double value in numbers) items.Add(value);
        return items;
    }

    /// <summary>A unit quaternion (xyzw) about an axis that need not be normalised.</summary>
    private static double[] Quat(double x, double y, double z, double degrees)
    {
        double length = Math.Sqrt(x * x + y * y + z * z);
        double half = degrees * Math.PI / 360.0;
        double sin = Math.Sin(half) / length;
        return new[] { x * sin, y * sin, z * sin, Math.Cos(half) };
    }

    private static bool Same(double[] left, double[] right, double tolerance)
    {
        if (left == null || right == null || left.Length != right.Length) return false;
        for (int i = 0; i < left.Length; i++) if (Math.Abs(left[i] - right[i]) > tolerance) return false;
        return true;
    }

    private static string Fixture(string name) =>
        Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,
                                      @"..\..\..\..\..\lib\" + name));

    private static void Check(bool ok, string what)
    {
        checks++;
        if (!ok) throw new Exception("GLB-SKEL FAIL: " + what);
    }
}
