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

        // --- Apply: the four phases, in ppskel's own order (convert:281, :285, :301, :316). Every
        // check below is one sentence of the invariant the port rests on - nothing is deleted,
        // nothing is reordered, nothing leaves skin.joints - stated numerically instead of argued.
        // Apply is called DIRECTLY here, without Validate: the refusals are Task 2's subject and
        // several of these plans (a collapse of an animated bone) are deliberately ones Validate
        // would refuse, because what Apply does to the JSON has to be checked on its own.

        // 19. The empty plan is what keeps GlbDocument's verbatim-JSON promise honest
        //     (GlbDocument.cs:91-92): nothing counted, nothing marked, the original bytes back.
        bool idle = true;
        foreach (string fixture in new[] { "u9_probe.glb", "u8_probe.glb" })
        {
            GlbDocument doc = Doc(fixture);
            GlbSkel.Stats none = GlbSkel.Apply(doc, new SkelPlan());
            idle = idle && !doc.Dirty && Counted(none) == "0/0/0/0" &&
                   Same(doc.Write(), File.ReadAllBytes(Fixture(fixture)));
        }
        Check(idle, "an empty plan counts nothing, dirties nothing and writes the file's own bytes back");

        // 20. A rename is two strings moving. The whole document is compared against the same file
        //     with ONLY those two strings moved by hand, so 'nothing else' is checked at every key
        //     rather than at the handful a list would remember to name.
        GlbDocument pristine9 = Doc("u9_probe.glb"), renamed9 = Doc("u9_probe.glb");
        GlbSkel.Stats renames = GlbSkel.Apply(renamed9, Renames("rig", "hip", "Spine_1", "head", "Neck"));
        GlbDocument mirror9 = Doc("u9_probe.glb");
        GlbSlim.Obj(GlbSkel.Nodes(mirror9)[1])["name"] = "Spine_1";
        GlbSlim.Obj(GlbSkel.Nodes(mirror9)[2])["name"] = "Neck";
        Check(Counted(renames) == "2/0/0/0" && renamed9.Dirty &&
              Deep(renamed9.Json, mirror9.Json) && Same(renamed9.Bin, pristine9.Bin),
              "a rename moves the two names and leaves every other key and every BIN byte alone");

        // 21. A rename is INDEX-blind: a channel names a node by index (glTF has no other way), so
        //     a pass that remapped one would be a bug, not a feature.
        Check(Deep(Section(renamed9, "animations"), Section(pristine9, "animations")) &&
              Joints(renamed9) == "1,2",
              "the rename leaves every channel target and skins[0].joints exactly as they were");

        // 22. An insert APPENDS (ppskel.py:306). Parents coming back non-null is the check that the
        //     moved child hangs in exactly ONE children array - two would be the refusal instead.
        GlbDocument pristine8 = Doc("u8_probe.glb"), inserted8 = Doc("u8_probe.glb");
        GlbSkel.Stats inserts = GlbSkel.Apply(inserted8, Insert("RootNode", "Root", "Spine_Roll_1", "Body", null));
        List<object> in8 = GlbSkel.Nodes(inserted8);
        int[] pin8 = GlbSkel.Parents(in8, out string whyIn8);
        Check(Counted(inserts) == "0/0/1/0" && in8.Count == 43 && GlbSkel.Name(in8, 42) == "Spine_Roll_1" &&
              whyIn8 == null && pin8[42] == 2 && pin8[3] == 42 &&
              Deep(Section(inserted8, "skins"), Section(pristine8, "skins")) &&
              Same(inserted8.Bin, pristine8.Bin),
              "the new node lands at 42 under Root with Body beneath it, and skins + BIN are untouched");

        // 23. The insert's geometry claim, measured: all 39 joints keep their world matrix, for an
        //     identity insert by construction and for a non-identity one because the child's own
        //     local is compensated with the inverse. And the inverse bind matrices, which nothing
        //     here recomputes, stay exactly as right (or as wrong) as the file shipped them: their
        //     distance from inverse(world) is the same number before and after, per joint. That is
        //     the honest form of the claim - u8's rest pose is NOT its bind pose (the two differ by
        //     up to 0.036 on MidFrontLeg3.L), so an absolute 'IBM == inverse(world)' would be false
        //     of the fixture itself and would prove nothing about Apply.
        double[][] world8 = Worlds(pristine8), worldIn8 = Worlds(inserted8);
        var shift = Insert("RootNode", "Root", "Spine_Roll_1", "Body", new[] { 0.1, -0.2, 0.3 });
        shift.Inserts[0].Rotation = Quat(0, 1, 0, 30);
        GlbDocument shifted8 = Doc("u8_probe.glb");
        GlbSkel.Apply(shifted8, shift);
        double[][] worldSh8 = Worlds(shifted8);
        bool moved = false, drifted = false;
        List<double[]> ibm8 = Ibm(pristine8), ibmSh8 = Ibm(shifted8);
        for (int j = 0; j < world8.Length; j++)
        {
            moved = moved || !Same(world8[j], worldIn8[j], 1e-9) || !Same(world8[j], worldSh8[j], 1e-9);
            drifted = drifted || Math.Abs(Apart(ibm8[j], GlbSkel.Inverse(world8[j])) -
                                          Apart(ibmSh8[j], GlbSkel.Inverse(worldSh8[j]))) > 1e-6;
        }
        Check(!moved && !drifted && world8.Length == 39 &&
              Same(GlbSkel.Trs(GlbSlim.Obj(GlbSkel.Nodes(shifted8)[42])), GlbSkel.Local(shift.Inserts[0]), 0),
              "an insert moves no joint's world matrix and no joint's bind residual, and the new " +
              "node carries exactly the local the plan gave it");

        // 24. A collapse composes the skipped node's local into the kept one (L' = L_kept * L_dropped),
        //     so the kept bone stands where it stood; the skipped node stays as a childless leaf
        //     named _unused (ppskel.py:290-297) because removing it would renumber every index in
        //     the file, skin.joints included.
        double[] headWas = Worlds(pristine9, 2);
        GlbDocument collapsed9 = Doc("u9_probe.glb");
        GlbSkel.Stats collapses = GlbSkel.Apply(collapsed9, Collapse("rig", "head", "rig"));
        List<object> co9 = GlbSkel.Nodes(collapsed9);
        int[] pco9 = GlbSkel.Parents(co9, out string whyCo9);
        Check(Counted(collapses) == "0/1/0/0" && co9.Count == 5 && whyCo9 == null && pco9[2] == 0 &&
              GlbSkel.Name(co9, 1) == "hip_unused" && !GlbSlim.Obj(co9[1]).ContainsKey("children") &&
              Same(Worlds(collapsed9, 2), headWas, 1e-9),
              "the collapsed bone keeps its world matrix under the grandparent and the skipped node " +
              "is left as a childless _unused leaf");

        // 25. ... and therefore no weight moves: the dropped node kept its own local under the same
        //     grandparent, so its bind pose is unchanged and every vertex weighted to it deforms as
        //     it did. Nothing to re-index, nothing to rewrite, BIN identical.
        Check(Joints(collapsed9) == "1,2" &&
              GlbSlim.Int(GlbSlim.Obj(GlbSlim.Arr(collapsed9.Json, "skins")[0]), "inverseBindMatrices", -1) == 5 &&
              Same(collapsed9.Bin, pristine9.Bin),
              "a collapse leaves skins[0].joints, the IBM accessor and every BIN byte alone");

        // 26. Create hangs an identity leaf at an EXPLICIT path and invents nothing (design §9).
        //     The path is measured from Root and does not repeat it, which is the semantics Resolve
        //     and Validate already keep - PP's own paths start BELOW the animator root.
        GlbDocument created9 = Doc("u9_probe.glb");
        var creation = new SkelPlan { Root = "rig" };
        creation.Create.Add("hip/Neck_Tip");
        GlbSkel.Stats creates = GlbSkel.Apply(created9, creation);
        List<object> cr9 = GlbSkel.Nodes(created9);
        int[] pcr9 = GlbSkel.Parents(cr9, out string whyCr9);
        Dictionary<string, object> leaf = GlbSlim.Obj(cr9[5]);
        Check(Counted(creates) == "0/0/0/1" && cr9.Count == 6 && whyCr9 == null && pcr9[5] == 1 &&
              GlbSkel.Name(cr9, 5) == "Neck_Tip" && leaf.Count == 1 &&
              Deep(Section(created9, "skins"), Section(pristine9, "skins")) &&
              Same(created9.Bin, pristine9.Bin),
              "create appends one named node with no transform of its own under the parent the path names");

        // 27. All four phases at once, on both fixtures: the animations block comes out identical,
        //     clip for clip, channel for channel, target.node for target.path. There is no channel
        //     remap in this port and there must not be one.
        GlbDocument all9 = Doc("u9_probe.glb"), all8 = Doc("u8_probe.glb");
        GlbSkel.Stats did9 = GlbSkel.Apply(all9, Everything9());
        GlbSkel.Stats did8 = GlbSkel.Apply(all8, Everything8());
        Check(Counted(did9) == "2/1/1/1" && Counted(did8) == "1/1/1/1" &&
              Deep(Section(all9, "animations"), Section(pristine9, "animations")) &&
              Deep(Section(all8, "animations"), Section(pristine8, "animations")) &&
              Same(all9.Bin, pristine9.Bin) && Same(all8.Bin, pristine8.Bin),
              "a four-phase plan leaves every clip, every channel and every BIN byte untouched");

        // 28. The plan is a file an author edits, so it has to survive the round trip - and a file
        //     that is not a plan has to come back as a sentence rather than as an exception thrown
        //     through OnGUI.
        string planPath = Path.Combine(Path.GetTempPath(), "ct_skel_roundtrip.json");
        SkelPlan wrote = Everything9();
        File.WriteAllText(planPath, wrote.ToJson());
        SkelPlan read = SkelPlan.Parse(File.ReadAllText(planPath), out string whyRead);
        SkelPlan broken = SkelPlan.Parse("[1, 2]", out string whyBroken);
        SkelPlan garbage = SkelPlan.Parse("{\"renames\":[{\"from\":\"a\"}]}", out string whyGarbage);
        File.Delete(planPath);
        Check(whyRead == null && read != null && Printed(read) == Printed(wrote) &&
              broken == null && !string.IsNullOrEmpty(whyBroken) &&
              garbage == null && !string.IsNullOrEmpty(whyGarbage),
              "a plan round-trips through its own JSON and a file that is not one comes back as a " +
              "sentence, not a throw");

        return "GLB-SKEL PASS, " + checks + " check(s)";
    }

    /// <summary>A plan using all four phases on u9: hip/head become PP's names, the neck chain
    /// collapses the way ppskel.py:89 needs it to, a roll bone is slipped in, and one explicit tip
    /// is created.</summary>
    private static SkelPlan Everything9()
    {
        SkelPlan plan = Renames("rig", "hip", "Spine_1", "head", "Neck");
        plan.Collapses.Add(new SkelCollapse { Node = "Neck", Into = "rig" });
        plan.Inserts.Add(new SkelInsert { Parent = "rig", Name = "Spine_Roll", Child = "Neck" });
        plan.Create.Add("Spine_Roll/Tip");
        return plan;
    }

    /// <summary>The same four phases at u8's scale, where 39 joints and 277 accessors have to come
    /// out of it unmoved.</summary>
    private static SkelPlan Everything8()
    {
        SkelPlan plan = Renames("RootNode", "Body", "Chest");
        plan.Collapses.Add(new SkelCollapse { Node = "FrontLeg2.L", Into = "Chest" });
        plan.Inserts.Add(new SkelInsert { Parent = "Root", Name = "Spine_Roll_1", Child = "Chest" });
        plan.Create.Add("SpiderArmature/Root/Tail");
        return plan;
    }

    private static GlbDocument Doc(string fixture) => GlbDocument.Load(Fixture(fixture));

    private static string Counted(GlbSkel.Stats stats) =>
        stats.Renamed + "/" + stats.Collapsed + "/" + stats.Inserted + "/" + stats.Created;

    private static object Section(GlbDocument doc, string key) => GlbSlim.Get(doc.Json, key);

    /// <summary>skins[0].joints, comma-joined, so a wrong answer prints as one.</summary>
    private static string Joints(GlbDocument doc)
    {
        var indices = new List<string>();
        foreach (object item in GlbSlim.Arr(GlbSlim.Obj(GlbSlim.Arr(doc.Json, "skins")[0]), "joints"))
            indices.Add(((int)(double)item).ToString());
        return string.Join(",", indices.ToArray());
    }

    /// <summary>Every skin joint's world matrix, walked with the same row-vector composition the
    /// port uses - no Unity anywhere in the check.</summary>
    private static double[][] Worlds(GlbDocument doc)
    {
        var joints = new List<int>();
        foreach (object item in GlbSlim.Arr(GlbSlim.Obj(GlbSlim.Arr(doc.Json, "skins")[0]), "joints"))
            joints.Add((int)(double)item);
        var worlds = new double[joints.Count][];
        for (int i = 0; i < joints.Count; i++) worlds[i] = Worlds(doc, joints[i]);
        return worlds;
    }

    private static double[] Worlds(GlbDocument doc, int node)
    {
        List<object> nodes = GlbSkel.Nodes(doc);
        int[] parents = GlbSkel.Parents(nodes, out _);
        double[] world = GlbSkel.Trs(GlbSlim.Obj(nodes[node]));
        for (int at = parents[node]; at >= 0; at = parents[at])
            world = GlbSkel.Mul(world, GlbSkel.Trs(GlbSlim.Obj(nodes[at])));
        return world;
    }

    /// <summary>The skin's inverse bind matrices, read straight out of BIN as float MAT4.</summary>
    private static List<double[]> Ibm(GlbDocument doc)
    {
        Dictionary<string, object> skin = GlbSlim.Obj(GlbSlim.Arr(doc.Json, "skins")[0]);
        Dictionary<string, object> accessor =
            GlbSlim.Obj(GlbSlim.Arr(doc.Json, "accessors")[GlbSlim.Int(skin, "inverseBindMatrices", -1)]);
        Dictionary<string, object> view =
            GlbSlim.Obj(GlbSlim.Arr(doc.Json, "bufferViews")[GlbSlim.Int(accessor, "bufferView", -1)]);
        int at = (int)(GlbSlim.Long(view, "byteOffset", 0) + GlbSlim.Long(accessor, "byteOffset", 0));
        int count = GlbSlim.Int(accessor, "count", 0);
        var all = new List<double[]>(count);
        for (int i = 0; i < count; i++)
        {
            var m = new double[16];
            for (int k = 0; k < 16; k++) m[k] = BitConverter.ToSingle(doc.Bin, at + i * 64 + k * 4);
            all.Add(m);
        }
        return all;
    }

    /// <summary>The largest element-wise distance between two matrices.</summary>
    private static double Apart(double[] left, double[] right)
    {
        double worst = 0;
        for (int i = 0; i < 16; i++) worst = Math.Max(worst, Math.Abs(left[i] - right[i]));
        return worst;
    }

    /// <summary>Structural equality over what Json.Parse hands back. Used instead of a list of keys
    /// so 'the rename changed nothing else' is asked of the whole document.</summary>
    private static bool Deep(object left, object right)
    {
        if (left == null || right == null) return left == null && right == null;
        if (left is Dictionary<string, object> a && right is Dictionary<string, object> b)
        {
            if (a.Count != b.Count) return false;
            foreach (KeyValuePair<string, object> member in a)
                if (!b.TryGetValue(member.Key, out object other) || !Deep(member.Value, other)) return false;
            return true;
        }
        if (left is List<object> x && right is List<object> y)
        {
            if (x.Count != y.Count) return false;
            for (int i = 0; i < x.Count; i++) if (!Deep(x[i], y[i])) return false;
            return true;
        }
        return left.Equals(right);
    }

    private static bool Same(byte[] left, byte[] right)
    {
        if (left == null || right == null) return left == null && right == null;
        if (left.Length != right.Length) return false;
        for (int i = 0; i < left.Length; i++) if (left[i] != right[i]) return false;
        return true;
    }

    private static string Printed(SkelPlan plan)
    {
        var text = new List<string> { "root=" + plan.Root };
        foreach (SkelRename step in plan.Renames) text.Add("rename " + step.From + ">" + step.To);
        foreach (SkelCollapse step in plan.Collapses) text.Add("collapse " + step.Node + ">" + step.Into);
        foreach (SkelInsert step in plan.Inserts)
            text.Add("insert " + step.Parent + "/" + step.Name + "/" + step.Child + " " +
                     Spelled(step.Translation) + Spelled(step.Rotation) + Spelled(step.Scale));
        foreach (string path in plan.Create) text.Add("create " + path);
        return string.Join(" | ", text.ToArray());
    }

    private static string Spelled(double[] numbers)
    {
        if (numbers == null) return "-";
        var text = new List<string>(numbers.Length);
        foreach (double value in numbers) text.Add(value.ToString("R"));
        return "[" + string.Join(",", text.ToArray()) + "]";
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
