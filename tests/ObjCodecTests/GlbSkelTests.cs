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

        // --- The refusal catalogue. ppskel asserts and dies (convert:277,:286; check:241-261); this
        // runs inside OnGUI and inside a worker, so every refusal is a SENTENCE an author can act on
        // and the document is still clean afterwards - which every arm below asserts as well.

        GlbDocument u9doc = GlbDocument.Load(Fixture("u9_probe.glb"));
        GlbDocument u8doc = GlbDocument.Load(Fixture("u8_probe.glb"));

        // 7. The root is what every path a clip binds to is measured from, so one that names nothing
        //    decides nothing (ppskel.check:241-242 asserts exactly one ANIM_ROOT).
        Refuses(u9doc, new SkelPlan { Root = "Rig" }, "a root the file does not carry", "names no node");

        // 8. ... and a root TWO nodes answer to does not decide it either.
        Refuses(Renamed("u9_probe.glb", 4, "rig"), new SkelPlan { Root = "rig" },
                "an ambiguous root", "names 2 nodes");

        // 9. From has to name exactly one bone (ppskel.convert:277-278) - and the honest plan beside
        //    it has to PASS, or a Validate that refused everything would look just as green.
        IList<string> clean = GlbSkel.Validate(u9doc, Renames("rig", "hip", "Spine_1", "head", "Neck"), null);
        IList<string> absent = GlbSkel.Validate(u9doc, Renames("rig", "spine", "Spine_1"), null);
        Check(clean.Count == 0 && Says(absent, "has no bone called") && !u9doc.Dirty,
              "the honest rename plan validates clean and a From the file lacks is refused, not '" +
              Printed(clean) + "' / '" + Printed(absent) + "'");

        // 10. Two bones of one name: the plan cannot say which one it meant.
        Refuses(Renamed("u9_probe.glb", 3, "hip"), Renames("rig", "hip", "Spine_1"),
                "a From two bones answer to", "has two bones called");

        // 11. A target the file already carries would leave two bones with one name, and
        //     Addon.cs:1217 binds the first literal match - so it would bind the wrong one.
        Refuses(u9doc, Renames("rig", "hip", "head"),
                "a target the file already carries", "already has a bone called");

        // 12. The same collision AliasMap.Of refuses (AliasMap.cs:52-53), for the same reason.
        Refuses(u9doc, Renames("rig", "hip", "Neck", "head", "Neck"),
                "two renames onto one name", "two of the file's bones onto");

        // 13. A decorated target binds to NOTHING: Addon.cs:1217 compares the literal Transform.name,
        //     and the decoration is what SkinBinder.Plain exists to strip (GlbReader.cs:2499).
        Refuses(u9doc, Renames("rig", "hip", "#Root_Addon => PX_Heavy_Torso_BodyPartDef"),
                "a decorated target", "the game's own decoration");

        // 14. A collapse skips the node's PARENT, so a scene root has nothing to skip.
        Refuses(u8doc, Collapse("RootNode", "RootNode", "SpiderArmature"),
                "a collapse of a scene root", "is a root");

        // 15. ppskel.convert:287 asserts parent[keep] == drop; Body is Thorax's parent, not its
        //     grandparent, so this collapse would re-parent it onto itself.
        Refuses(u8doc, Collapse("RootNode", "Thorax", "Body"),
                "a collapse onto something that is not the grandparent", "is not the grandparent of");

        // 16. An insert only ever slips between a parent and its OWN child.
        Refuses(u8doc, Insert("RootNode", "Root", "Spine_Roll_1", "Thorax", null),
                "an insert above a child of someone else", "is not a child of");

        // 17. The new node's name is a name like any other, and a second 'Head' binds the wrong one.
        Refuses(u8doc, Insert("RootNode", "Root", "Head", "Body", null),
                "an insert taking a name the file carries", "already has a bone called");

        // 18. THE animation refusal, the one thing ppskel does not need to care about because it
        //     throws the source's own clips away: a collapse rewrites the kept bone's local and a
        //     non-identity insert rewrites its child's, and a channel that writes that local every
        //     frame overwrites the composition on frame 1. All four of u9's clips animate hip/head.
        IList<string> hoisted = GlbSkel.Validate(u9doc, Collapse("rig", "head", "rig"), null);
        IList<string> shifted = GlbSkel.Validate(u9doc,
            Insert("rig", "hip", "hip_roll", "head", new[] { 0.1, -0.2, 0.3 }), null);
        Check(Says(hoisted, "is animated by") && hoisted[0].Contains("Walk") &&
              Says(shifted, "is animated by") && shifted[0].Contains("Walk") && !u9doc.Dirty,
              "a collapse and a non-identity insert on an animated bone are refused BY CLIP NAME, not '" +
              Printed(hoisted) + "' / '" + Printed(shifted) + "'");

        return "GLB-SKEL PASS, " + checks + " check(s)";
    }

    /// <summary>One plan, one refusal, and a document nobody touched.</summary>
    private static void Refuses(GlbDocument doc, SkelPlan plan, string what, params string[] phrases)
    {
        IList<string> refusals = GlbSkel.Validate(doc, plan, null);
        bool ok = refusals.Count == 1 && !doc.Dirty;
        foreach (string phrase in phrases) ok = ok && refusals[0].Contains(phrase);
        Check(ok, what + " is refused once, saying " + string.Join(" + ", phrases) +
                  " - got '" + Printed(refusals) + "'" + (doc.Dirty ? " and a dirtied document" : ""));
    }

    private static bool Says(IList<string> refusals, string phrase) =>
        refusals.Count == 1 && refusals[0].Contains(phrase);

    private static string Printed(IList<string> refusals) =>
        refusals.Count == 0 ? "nothing" : string.Join(" | ", refusals);

    /// <summary>A fixture with one node renamed, which is how the duplicate-name cases are reached:
    /// neither probe ships two bones of one name, and a hand-built glTF would prove nothing about
    /// the files this tool actually meets.</summary>
    private static GlbDocument Renamed(string fixture, int node, string name)
    {
        GlbDocument doc = GlbDocument.Load(Fixture(fixture));
        GlbSlim.Obj(GlbSkel.Nodes(doc)[node])["name"] = name;
        return doc;
    }

    private static SkelPlan Renames(string root, params string[] fromTo)
    {
        var plan = new SkelPlan { Root = root };
        for (int i = 0; i + 1 < fromTo.Length; i += 2)
            plan.Renames.Add(new SkelRename { From = fromTo[i], To = fromTo[i + 1] });
        return plan;
    }

    private static SkelPlan Collapse(string root, string node, string into)
    {
        var plan = new SkelPlan { Root = root };
        plan.Collapses.Add(new SkelCollapse { Node = node, Into = into });
        return plan;
    }

    private static SkelPlan Insert(string root, string parent, string name, string child, double[] translation)
    {
        var plan = new SkelPlan { Root = root };
        plan.Inserts.Add(new SkelInsert { Parent = parent, Name = name, Child = child, Translation = translation });
        return plan;
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
