using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
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

        // 18b. The other half of that refusal, found reviewing Task 3: a non-identity insert is
        //      COMPENSATED on the child (L_child' = L_child * inverse(L_new)), so a mirroring local
        //      inverts perfectly well and still leaves the child with a matrix no TRS can hold.
        //      Validate has to ask that question, not just 'is it invertible' - a clean Validate is a
        //      promise that Apply cannot throw. SpiderArmature/Root is the seam used because Root is
        //      one of u8's few nodes no clip animates, so this arm is reached on its own.
        var mirror = Insert("RootNode", "SpiderArmature", "Mirror", "Root", null);
        mirror.Inserts[0].Scale = new double[] { -1, 1, 1 };
        Refuses(u8doc, mirror, "an insert whose compensation mirrors the child",
                "no translation/rotation/scale can hold");

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

        // 28b. The same round trip with a full TRS on the insert step - the only step that carries
        //      one - compared field by field through Printed, which spells every double with "R".
        SkelPlan posed = Everything9();
        posed.Inserts[0].Translation = new[] { 0.1, 0.2, 0.3 };
        posed.Inserts[0].Rotation = new[] { 0, 0.7071068, 0, 0.7071068 };
        posed.Inserts[0].Scale = new[] { 2.0, 2, 2 };
        SkelPlan reposed = SkelPlan.Parse(posed.ToJson(), out string whyPosed);
        Check(whyPosed == null && reposed != null && Printed(reposed) == Printed(posed) &&
              Same(reposed.Inserts[0].Translation, posed.Inserts[0].Translation, 0) &&
              Same(reposed.Inserts[0].Rotation, posed.Inserts[0].Rotation, 0) &&
              Same(reposed.Inserts[0].Scale, posed.Inserts[0].Scale, 0),
              "an insert's translation, rotation and scale survive the JSON round trip exactly - got '" +
              (reposed == null ? whyPosed : Printed(reposed)) + "'");

        // --- Verify: the same file asked the TWO different questions the two binding mechanisms ask.
        // Addon.GetEquivalentBones compares a literal Transform.name (Addon.cs:1217); a generic clip
        // binds to crc32 of a '/'-joined PATH (ClipFields.cs:34-41). A file can be perfect by one and
        // useless by the other, so the two lists are computed apart and checked apart here.

        // 29. BY NAME - the Doctor's verdict question, and the whole point of a rename plan.
        GlbDocument named9 = Doc("u9_probe.glb");
        SkelVerdict blind = GlbSkel.Verify(named9, "rig", Words("Spine_1", "Neck"), null);
        GlbSkel.Apply(named9, Renames("rig", "hip", "Spine_1", "head", "Neck"));
        SkelVerdict bound = GlbSkel.Verify(named9, "rig", Words("Spine_1", "Neck"), null);
        Check(blind.MissingNames.Count == 2 && !blind.Ok && blind.NamesResolved == 0 &&
              bound.MissingNames.Count == 0 && bound.NamesResolved == 2 && bound.Ok &&
              bound.Nodes == 5 && bound.SkinJoints == 2,
              "the rename plan turns two bones the rig wants from missing into bound: '" +
              blind.Sentence() + "' -> '" + bound.Sentence() + "'");

        // 30. BY PATH - the clip question, answered by walking child names rather than by looking a
        //     name up, so a rig whose names collide across branches cannot pass this one by accident.
        SkelVerdict walked = GlbSkel.Verify(named9, "rig", null, Words("Spine_1", "Spine_1/Neck"));
        SkelVerdict unwalked = GlbSkel.Verify(Doc("u9_probe.glb"), "rig", null, Words("Spine_1", "Spine_1/Neck"));
        Check(walked.MissingPaths.Count == 0 && walked.PathsResolved == 2 && walked.Ok &&
              unwalked.MissingPaths.Count == 2 && !unwalked.Ok,
              "the same plan resolves both prototype paths, and neither of them resolved before it: '" +
              unwalked.Sentence() + "' -> '" + walked.Sentence() + "'");

        // 31. EXT_ is not a failure of anything: Addon.GetEquivalentBones skips those transforms
        //     outright (Addon.cs:1209), and PrototypeCatalog.IsAttachmentPoint (:91) is the shipped
        //     predicate for the prefix. An absent one is reported as information, in its own list.
        SkelVerdict ext = GlbSkel.Verify(named9, "rig", Words("Spine_1", "EXT_VoiceContext"),
                                         Words("Spine_1", "Spine_1/EXT_VoiceContext"));
        Check(ext.MissingNames.Count == 0 && ext.MissingPaths.Count == 0 && ext.Ok &&
              ext.AttachmentsAbsent.Count == 1 && ext.AttachmentsAbsent[0] == "EXT_VoiceContext",
              "an attachment point the file lacks is information, not a missing bone: '" + ext.Sentence() + "'");

        // 31b. A name carried TWICE is a defect, counted across every node and not just the root's
        //      subtree: 'prop' (node 4, its own root) renamed to Neck doubles the Neck under rig.
        //      Addon.cs:1217 binds the first Transform it meets and says nothing about the second.
        GlbDocument twin9 = Renamed("u9_probe.glb", 2, "Neck");
        GlbSlim.Obj(GlbSkel.Nodes(twin9)[4])["name"] = "Neck";
        SkelVerdict twinned = GlbSkel.Verify(twin9, "rig", Words("Neck", "hip"), null);
        Check(!twinned.Ok && twinned.MissingNames.Count == 0 && twinned.NamesResolved == 2 &&
              twinned.Duplicates.Count == 1 &&
              twinned.Duplicates[0] == "'Neck' is carried by 2 nodes - the game binds the first it meets" &&
              twinned.Sentence().Contains("carried by 2 nodes") && bound.Duplicates.Count == 0,
              "a bone name on two nodes is a defect naming the bone and the count, a unique one is not: '" +
              twinned.Sentence() + "'");

        // 32. ppskel.check:249-256 ported whole, over the four-phase plans of check 27: the skin
        //     block and every node's mesh/skin binding come out of a rewrite untouched. This is the
        //     assertion that would catch a pass that ever deleted or reordered a node.
        Check(SkinIntact(pristine9, all9) && SkinIntact(pristine8, all8) &&
              SkinIntact(pristine9, named9) && SkinIntact(pristine9, collapsed9),
              "every skin, every IBM accessor and every node's mesh/skin binding survive a four-phase plan");

        // 33. ppskel.check:257-261: the graph is still a forest of trees afterwards. An insert that
        //     forgot to unlink its child is exactly what produces a second parent, and Parents is the
        //     only thing that would notice. (The two reloaded documents below are covered by their own
        //     Validate calls, which refuse a two-parent file before reading a single step.)
        Check(Wired(named9, all9, all8, collapsed9, inserted8, created9, shifted8),
              "no plan in this gate leaves a node with two parents");

        // 34. Idempotence, honestly. A plan is a one-shot INSTRUCTION, not a fixed point: the second
        //     run is refused because the bones it names are gone - which is the truthful answer. A
        //     panel that reported it as a silent no-op would claim success for two different things.
        string once = Path.Combine(Path.GetTempPath(), "ct_skel_once.glb");
        GlbDocument first = Doc("u9_probe.glb");
        SkelPlan plan = Renames("rig", "hip", "Spine_1", "head", "Neck");
        GlbSkel.Apply(first, plan);
        byte[] written = first.Write();
        File.WriteAllBytes(once, written);
        GlbDocument reloaded = GlbDocument.Load(once);
        IList<string> twice = GlbSkel.Validate(reloaded, plan, null);
        Check(Same(File.ReadAllBytes(once), written) && twice.Count > 0 && !reloaded.Dirty &&
              Printed(twice).Contains("has no bone called 'hip'"),
              "the same plan run a second time is refused by name, not silently repeated - got '" +
              Printed(twice) + "'");

        // 35. A DIFFERENT plan composes onto the output, and two full rewrites later BIN is still the
        //     fixture's own bytes - the index invariant holding across a reload, not just in memory.
        SkelPlan second = Renames("rig", "Spine_1", "Root");
        IList<string> allowed = GlbSkel.Validate(reloaded, second, null);
        GlbSkel.Apply(reloaded, second);
        string thrice = Path.Combine(Path.GetTempPath(), "ct_skel_twice.glb");
        reloaded.Write(thrice);
        GlbDocument round = GlbDocument.Load(thrice);
        SkelVerdict composed2 = GlbSkel.Verify(round, "rig", Words("Root", "Neck"), Words("Root", "Root/Neck"));
        Check(allowed.Count == 0 && composed2.Ok && Same(round.Bin, pristine9.Bin),
              "a second plan applies to the first one's output and neither rewrite touched a BIN byte - '" +
              Printed(allowed) + "' / '" + composed2.Sentence() + "'");

        // 36. And the verdict is a property of the FILE, not of the plan that produced it: the same
        //     answer comes back from a document loaded off disk with no plan in hand. That is the only
        //     form of the question the game ever asks, and what makes the in-game acceptance real.
        SkelVerdict inHand = GlbSkel.Verify(first, "rig", Words("Spine_1", "Neck"),
                                            Words("Spine_1", "Spine_1/Neck"));
        SkelVerdict alone = GlbSkel.Verify(GlbDocument.Load(once), "rig", Words("Spine_1", "Neck"),
                                           Words("Spine_1", "Spine_1/Neck"));
        File.Delete(once);
        File.Delete(thrice);
        Check(alone.Ok && inHand.Sentence() == alone.Sentence(),
              "the written file answers for itself: '" + alone.Sentence() + "' vs '" + inHand.Sentence() + "'");

        // --- The bridge from the Doctor's bone map to a plan. The sidecar and a rename plan say the
        // same fact from two sides, and the flow is one-directional: aliases -> plan -> baked .glb.

        // 44. Every live alias becomes one rename, and NOTHING else is invented: the Doctor knows
        //     which bones are misnamed and nothing at all about hierarchy (design §9).
        var mapped = new Dictionary<string, string>(StringComparer.Ordinal)
            { { "hip", "Spine_1" }, { "head", "Neck" } };
        SkelPlan bridged = Morgott.ContentTool.Doctor.SkelPlanFromMap.Of(null, mapped, "rig");
        Check(bridged.Root == "rig" && bridged.Renames.Count == 2 &&
              bridged.Renames[0].From == "hip" && bridged.Renames[0].To == "Spine_1" &&
              bridged.Renames[1].From == "head" && bridged.Renames[1].To == "Neck" &&
              bridged.Collapses.Count == 0 && bridged.Inserts.Count == 0 && bridged.Create.Count == 0,
              "the bone map becomes renames and nothing else: " + Printed(bridged));

        // 45. And it validates and applies AS WRITTEN, against the file the aliases were made for -
        //     which is the only claim worth making about a plan a button produced.
        GlbDocument bridging = Doc("u9_probe.glb");
        IList<string> onThePlan = GlbSkel.Validate(bridging, bridged, null);
        GlbSkel.Stats bridgedStats = GlbSkel.Apply(bridging, bridged);
        Check(onThePlan.Count == 0 && bridgedStats.Renamed == 2 &&
              GlbSkel.Verify(bridging, "rig", Words("Spine_1", "Neck"), null).Ok,
              "a plan written from the bone map validates by construction and binds by name: " +
              Printed(onThePlan));

        // 46. An alias the REPORT itself flagged is left out: AliasUnused says the file has no bone of
        //     that name, AliasNotATargetBone says the rig has no such bone (ReplacementPreflight.cs:143,
        //     :148, Subject = the FILE bone). Either is a rename Validate would refuse, and a plan that
        //     arrives pre-refused is worse than a short one. A Doctor with nothing to say writes an
        //     empty plan rather than throwing inside OnGUI.
        var flagged = new List<Morgott.ContentTool.Doctor.Diagnostic>
        {
            new Morgott.ContentTool.Doctor.Diagnostic { Code = "AliasUnused", Subject = "hip" },
            new Morgott.ContentTool.Doctor.Diagnostic { Code = "AliasNotATargetBone", Subject = "head" },
            new Morgott.ContentTool.Doctor.Diagnostic { Code = "MissingBone", Subject = "Spine_1" },
        };
        mapped["tail"] = "Tail_1";
        SkelPlan pruned = Morgott.ContentTool.Doctor.SkelPlanFromMap.Of(flagged, mapped, null);
        SkelPlan emptied = Morgott.ContentTool.Doctor.SkelPlanFromMap.Of(flagged, null, null);
        Check(pruned.Renames.Count == 1 && pruned.Renames[0].From == "tail" &&
              pruned.Renames[0].To == "Tail_1" && pruned.Root == null && emptied.Renames.Count == 0,
              "a flagged alias is dropped and an empty map is an empty plan: " + Printed(pruned));

        // --- The job. Nothing below re-tests GlbSkel: what is on trial is the file on disk, which is
        // only ever replaced by a finished, verified temp - the same swap Execute and Zip keep
        // (SlimJob.cs:88-89, :172-173), because a rewrite the author cannot undo is a rewrite nobody ran.

        string workDir = Path.Combine(Path.GetTempPath(), "ct_skel_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workDir);
        try
        {
            string srcFile = Path.Combine(workDir, "u9_probe.glb");
            byte[] srcBytes = File.ReadAllBytes(Fixture("u9_probe.glb"));
            File.WriteAllBytes(srcFile, srcBytes);
            string planFile = Path.Combine(workDir, SkelPlan.PlanPathOf("u9_probe.glb"));
            File.WriteAllText(planFile, Renames("rig", "hip", "Spine_1", "head", "Neck").ToJson());
            Func<bool> noTmp = () => Directory.GetFiles(workDir, "*.ct_tmp").Length == 0;
            List<string> wantNames = Words("Spine_1", "Neck"), wantPaths = Words("Spine_1", "Spine_1/Neck");

            // 37. The run end to end: a NEW sibling that verifies clean against the prototype's bones,
            //     and a source that is byte for byte the file it was.
            string outFile = Path.Combine(workDir, "out.glb");
            var seen = new List<SlimProgress>();
            string ran = SlimJob.Skel(srcFile, outFile, planFile, wantNames, wantPaths,
                                      CancellationToken.None, seen.Add);
            SkelVerdict onDisk = File.Exists(outFile)
                ? GlbSkel.Verify(GlbDocument.Load(outFile), "rig", wantNames, wantPaths)
                : null;
            Check(onDisk != null && onDisk.Ok && ran.Contains("renamed 2") &&
                  Same(File.ReadAllBytes(srcFile), srcBytes),
                  "a skel run writes a verified sibling and leaves the source alone: " + ran);

            // 38. Cancel: the swap is the only line that touches the destination and a cancel is seen
            //     at the stage boundary before it, so there is nothing to undo.
            string never = Path.Combine(workDir, "never.glb");
            var cts = new CancellationTokenSource();
            cts.Cancel();
            bool cancelled = false;
            try { SlimJob.Skel(srcFile, never, planFile, wantNames, wantPaths, cts.Token, null); }
            catch (OperationCanceledException) { cancelled = true; }
            Check(cancelled && !File.Exists(never) && Same(File.ReadAllBytes(srcFile), srcBytes),
                  "a cancelled skel throws, creates no destination and leaves the source byte-identical");

            // 39. And no half-written temp survives, whichever way a run ended.
            Check(noTmp(), "no .ct_tmp survives a completed or a cancelled skel run");

            // 40. The bar the panel draws: one snapshot per checkpoint, never past its total, ending ON
            //     it - and Verify BEFORE Write, which is the whole ordering claim.
            bool orderly = seen.Count >= 6 && seen[seen.Count - 1].Stage == "Done" &&
                           seen[seen.Count - 1].Done == seen[0].Total &&
                           seen[4].Stage == "Verify" && seen[5].Stage == "Write";
            for (int i = 0; i < seen.Count; i++)
                orderly &= seen[i].Done <= seen[i].Total && seen[i].Total == seen[0].Total &&
                           (i == 0 || seen[i].Done >= seen[i - 1].Done);
            Check(orderly, "the skel publishes " + seen.Count + " orderly snapshots, verifies before it " +
                           "writes, and finishes on Done");

            // 41. A refused plan is Validate's own sentences and nothing on disk - the panel shows what
            //     the author has to fix, not "failed".
            string unwritten = Path.Combine(workDir, "unwritten.glb");
            string badPlan = Path.Combine(workDir, "bad.skelplan.json");
            File.WriteAllText(badPlan, Renames("rig", "nope", "Spine_1").ToJson());
            string reported = null;
            try { SlimJob.Skel(srcFile, unwritten, badPlan, null, null, CancellationToken.None, null); }
            catch (InvalidOperationException ex) { reported = ex.Message; }
            Check(reported != null && reported.Contains("has no bone called 'nope'") &&
                  !File.Exists(unwritten) && noTmp(),
                  "a refused plan reports Validate verbatim and writes nothing: " + reported);

            // 42. A plan that changes nothing is not a save. GlbDocument would write the source's own
            //     JSON bytes back verbatim, so the only honest thing to do with the destination is
            //     leave it alone - the same rule the zip run keeps for a file that would grow.
            string empty = Path.Combine(workDir, "empty.glb");
            string emptyPlan = Path.Combine(workDir, "empty.skelplan.json");
            File.WriteAllText(emptyPlan, new SkelPlan { Root = "rig" }.ToJson());
            string nothing = SlimJob.Skel(srcFile, empty, emptyPlan, null, null, CancellationToken.None, null);
            Check(nothing.Contains("changed nothing") && !File.Exists(empty) && noTmp(),
                  "an empty plan is reported and not written: " + nothing);

            // 43. An IN-PLACE run takes the alias sidecar with it. AliasMap.cs:189-195 guards it with
            //     the .glb's sha256, so after this rewrite it could never apply again - and every
            //     mapping it carried is now baked into the node names, which is the point of a skel run.
            string inPlace = Path.Combine(workDir, "inplace.glb");
            File.WriteAllBytes(inPlace, srcBytes);
            AliasMap.SaveSidecar(inPlace, AliasMap.Sha256(srcBytes), srcBytes.Length,
                                 new Dictionary<string, string>(StringComparer.Ordinal) { { "hip", "Spine_1" } });
            string sidecar = AliasMap.SidecarPathOf(inPlace);
            bool had = File.Exists(sidecar);
            string overwrote = SlimJob.Skel(inPlace, inPlace, planFile, wantNames, wantPaths,
                                            CancellationToken.None, null);
            Check(had && !File.Exists(sidecar) && overwrote.Contains("removed the now-stale") &&
                  !Same(File.ReadAllBytes(inPlace), srcBytes) && noTmp(),
                  "an in-place run rewrites the file and removes the sidecar the rewrite just made " +
                  "stale: " + overwrote);
        }
        finally { Directory.Delete(workDir, true); }

        return "GLB-SKEL PASS, " + checks + " check(s)";
    }

    private static List<string> Words(params string[] items) => new List<string>(items);

    /// <summary>ppskel.check:249-256, whole: the skin block and every node's mesh/skin binding come
    /// out of a rewrite untouched. Nodes a plan APPENDED sit past the source's last index and must
    /// carry neither key, so the shared prefix is compared and the tail is checked to be bone-only.</summary>
    private static bool SkinIntact(GlbDocument was, GlbDocument now)
    {
        if (!Deep(Section(now, "skins"), Section(was, "skins"))) return false;
        List<object> before = GlbSkel.Nodes(was), after = GlbSkel.Nodes(now);
        if (after.Count < before.Count) return false;
        for (int i = 0; i < before.Count; i++)
        {
            Dictionary<string, object> old = GlbSlim.Obj(before[i]), fresh = GlbSlim.Obj(after[i]);
            if (!Deep(GlbSlim.Get(old, "mesh"), GlbSlim.Get(fresh, "mesh"))) return false;
            if (!Deep(GlbSlim.Get(old, "skin"), GlbSlim.Get(fresh, "skin"))) return false;
        }
        for (int i = before.Count; i < after.Count; i++)
            if (GlbSlim.Get(GlbSlim.Obj(after[i]), "mesh") != null ||
                GlbSlim.Get(GlbSlim.Obj(after[i]), "skin") != null) return false;
        return true;
    }

    /// <summary>Every one of these documents is still a forest of trees.</summary>
    private static bool Wired(params GlbDocument[] docs)
    {
        foreach (GlbDocument doc in docs)
            if (GlbSkel.Parents(GlbSkel.Nodes(doc), out string why) == null || why != null) return false;
        return true;
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
