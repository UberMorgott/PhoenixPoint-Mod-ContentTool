using System;
using System.Collections.Generic;
using Morgott.ContentTool.Import;

/// <summary>
/// THE RECORD THAT PREDATES THE REFACTOR. SkinBinder.Bind is about to hand its checks to
/// SkinCompatibility.Analyze (Model Doctor, task 2). Every sentence below was captured from the
/// UNREFACTORED binder, so a delegation that changes which refusal an author reads - or its
/// wording - fails here rather than in a bake log three weeks later.
///
/// The replacement path always calls Bind(file, names, 0, null, ...) - LiveMesh.cs:217 and
/// SkinFields.cs:748 - so that is what every case here uses.
/// </summary>
internal static class BinderFrozen
{
    /// <summary>name -> (file joint names, target bone names, the substring the refusal must carry,
    /// or null when it must BIND).</summary>
    private static readonly List<string[]> Cases = new List<string[]>
    {
        new[] { "binds",              "Root|Neck",  "Root|Neck", null },
        new[] { "binds reversed",     "Neck|Root",  "Root|Neck", null },
        new[] { "no target bones",    "Root",       "",          "the target model lists no bones" },
        new[] { "no armature",        "",           "Root",      "the file carries no armature" },
        new[] { "target bone empty",  "Root|Neck",  "Root|",     "has no name" },
        new[] { "target bone twice",  "Root|Neck",  "Root|Root", "the target model has two bones named 'Root'" },
        new[] { "file bone twice",    "Root|Root",  "Root|Neck", "the file has two bones named 'Root'" },
        new[] { "missing bone",       "Root|Hand",  "Root|Neck", "does not contain the bone 'Neck'" },
        new[] { "extra bone",         "Root|Neck",  "Root",      "the file adds the bone 'Neck'" },
    };

    internal static string Run()
    {
        int checks = 0;
        foreach (string[] c in Cases) checks += One(c[0], Split(c[1]), Split(c[2]), c[3]);

        // The two decorated cases, which are the ones a live rig actually produces.
        checks += One("decorated binds", new[] { "#Root_Addon => D", "#Neck_Addon => D" },
                      new[] { "Root", "Neck" }, null);
        checks += One("decoration collides", new[] { "#Root_Addon => A", "#Root_Addon => B" },
                      new[] { "Root", "Neck" }, "both name the bone 'Root'");

        // The two that need a MALFORMED model rather than a name list.
        SkinnedModel ibm = Model(new[] { "Root" });
        ibm.InverseBindMatrices = new float[0][];
        checks += Refuses("bind pose count", ibm, new[] { "Root" }, "bind poses for");

        SkinnedModel slot = Model(new[] { "Root" });
        slot.Joints = new ushort[] { 7, 0, 0, 0 };
        checks += Refuses("bone index out of range", slot, new[] { "Root" }, "references bone 7");

        SkinnedModel cover = Model(new[] { "Root" });
        cover.Weights = new float[2];
        checks += Refuses("weights do not cover", cover, new[] { "Root" },
                          "bone weights do not cover every vertex");

        // Submeshes(file, 0) is NOT a no-op: it bounds-checks every triangle index, and it runs
        // BEFORE the bone checks. A file that is wrong in both ways must still say THIS first.
        SkinnedModel tri = Model(new[] { "Hand" });
        tri.Submeshes.Clear();
        tri.Submeshes.Add(new[] { 0, 0, 99 });
        checks += Refuses("triangle bound wins over a bone name", tri, new[] { "Root" },
                          "a triangle points at vertex 99");

        // Shapes(file, null) refuses ANY blend shape on the replacement path, also before the
        // bone checks - so a shape-keyed .glb falls back to nearest-bone in the bake.
        SkinnedModel morph = Model(new[] { "Hand" });
        morph.Morphs.Add(new SkinMorph { Name = "smile" });
        checks += Refuses("blend shape wins over a bone name", morph, new[] { "Root" },
                          "the file has 1 blend shapes but this model has 0");

        // A caller that DOES drive blend shapes is the other half of Shapes(file, names): the count
        // is compared against that caller's list, not against zero, so a matching shape key binds.
        SkinnedModel keyed = Model(new[] { "Root" });
        keyed.Morphs.Add(new SkinMorph { Name = "smile" });
        ushort[] keyedJoints;
        float[][] keyedPoses;
        SkinBinder.Bind(keyed, new[] { "Root" }, 0, new[] { "smile" }, out keyedJoints, out keyedPoses);
        checks += Check(keyedJoints.Length == 4 && keyedPoses.Length == 1,
                        "a file whose one shape key matches the model's binds instead of refusing");
        checks += RefusesShapes("blend shape count against a real list", keyed, new[] { "Root" },
                                new[] { "smile", "frown" },
                                "the file has 1 blend shapes but this model has 2");

        // ---- the extraction itself: Analyze must list EVERY reason, in Bind's own throw order,
        // where Bind stops at the first. One file that is wrong three ways over.
        SkinnedModel many = Model(new[] { "Root", "Hand" });
        IList<BindingIssue> issues = SkinCompatibility.Analyze(many, new[] { "Root", "Neck" });
        checks += Check(issues.Count == 2, "Analyze lists every reason, not just the first: " + issues.Count);
        checks += Check(issues[0].Code == BindCode.MissingBone && issues[0].Subject == "Neck",
                        "the missing live bone is reported FIRST and by name: " + issues[0].Code +
                        " '" + issues[0].Subject + "'");
        checks += Check(issues[1].Code == BindCode.ExtraBone && issues[1].Subject == "Hand",
                        "the added file bone comes second: " + issues[1].Code + " '" + issues[1].Subject + "'");
        checks += Check(issues[0].Message.IndexOf("does not contain the bone 'Neck'", StringComparison.Ordinal) >= 0,
                        "an issue carries the BINDER's own sentence, not a new one: " + issues[0].Message);
        checks += Check(SkinCompatibility.Analyze(Model(new[] { "Root" }), new[] { "Root" }).Count == 0,
                        "a file that binds produces no issue at all");

        // ---- every refusal now carries a CODE, and the ones nobody catalogued still carry one.
        checks += Code(ImportCode.MalformedGlb, new byte[] { 1, 2, 3 }, "a stub is malformed");
        byte[] notGlb = new byte[16];
        checks += Code(ImportCode.MalformedGlb, notGlb, "the wrong magic is malformed");
        checks += Check(new ImportRefusedException(ImportCode.NoNormals, "x") is FormatException,
                        "a refusal is still a FormatException, so every existing catch keeps working");

        // ---- EXTEND: a partial body part is legitimate, so a rig bone the file does not use is not
        // a defect. Nothing else moves - an added bone is still an added bone.
        IList<BindingIssue> ext = SkinCompatibility.Analyze(Model(new[] { "Root" }),
                                                            new[] { "Root", "Neck" }, 0, true);
        checks += Check(ext.Count == 0, "Extend accepts a strict subset of the rig: " + Codes(ext));
        IList<BindingIssue> extAdds = SkinCompatibility.Analyze(Model(new[] { "Root", "Hand" }),
                                                                new[] { "Root" }, 0, true);
        checks += Check(extAdds.Count == 1 && extAdds[0].Code == BindCode.ExtraBone,
                        "Extend still refuses a bone the rig does not have: " + Codes(extAdds));
        // ---- and REPLACE is byte-identical to what it was before this task.
        IList<BindingIssue> rep = SkinCompatibility.Analyze(Model(new[] { "Root" }),
                                                            new[] { "Root", "Neck" }, 0, false);
        checks += Check(rep.Count == 1 && rep[0].Code == BindCode.MissingBone,
                        "Replace still requires every live bone: " + Codes(rep));

        // ---- EXT_* joints in the FILE. The game skips them (Addon.cs:1208), so a weighted one
        // loses its weights silently and an unweighted one is only noise.
        SkinnedModel weighted = Model(new[] { "Root", "EXT_Grip" });
        IList<BindingIssue> hot = SkinCompatibility.Analyze(weighted, new[] { "Root" }, 0, true);
        checks += Check(Has(hot, BindCode.ExtJointWeighted),
                        "a WEIGHTED EXT_ joint is reported: " + Codes(hot));
        SkinnedModel cold = Model(new[] { "Root", "EXT_Grip" });
        for (int i = 0; i < cold.Weights.Length; i++) if (cold.Joints[i] == 1) cold.Weights[i] = 0f;
        IList<BindingIssue> mild = SkinCompatibility.Analyze(cold, new[] { "Root" }, 0, true);
        checks += Check(Has(mild, BindCode.ExtJointUnused) && !Has(mild, BindCode.ExtJointWeighted),
                        "an UNWEIGHTED EXT_ joint is only noted: " + Codes(mild));
        // ---- and the whole EXT_ policy is EXTEND-ONLY: on the Replace path an EXT_ bone the live
        // renderer really lists is load-bearing (design section 3), so nothing new may fire there.
        checks += Check(!Has(SkinCompatibility.Analyze(weighted, new[] { "Root", "EXT_Grip" }, 0, false),
                             BindCode.ExtJointWeighted),
                        "Replace still binds an EXT_ bone the renderer lists, without a new row");

        return "BINDER-FROZEN PASS, " + checks + " check(s) - the pre-refactor record of SkinBinder.Bind";
    }

    private static bool Has(IList<BindingIssue> issues, BindCode code)
    {
        for (int i = 0; i < issues.Count; i++) if (issues[i].Code == code) return true;
        return false;
    }

    private static string Codes(IList<BindingIssue> issues)
    {
        var parts = new List<string>();
        foreach (BindingIssue i in issues) parts.Add(i.Code.ToString());
        return parts.Count == 0 ? "(none)" : string.Join(",", parts.ToArray());
    }

    private static string[] Split(string joined)
    {
        return joined.Length == 0 ? new string[0] : joined.Split('|');
    }

    private static int One(string what, string[] jointNames, string[] boneNames, string cause)
    {
        SkinnedModel file = jointNames.Length == 0 ? Empty() : Model(jointNames);
        if (cause != null) return Refuses(what, file, boneNames, cause);

        ushort[] joints;
        float[][] bindposes;
        SkinBinder.Bind(file, boneNames, 0, null, out joints, out bindposes);
        return Check(joints.Length == file.Positions.Length * 4 && bindposes.Length == boneNames.Length,
                     what + " - it bound, but produced " + joints.Length + " joint slot(s) and " +
                     bindposes.Length + " bind pose(s)");
    }

    private static int Refuses(string what, SkinnedModel file, string[] boneNames, string cause)
    {
        return RefusesShapes(what, file, boneNames, null, cause);
    }

    private static int RefusesShapes(string what, SkinnedModel file, string[] boneNames,
                                     string[] blendShapeNames, string cause)
    {
        try
        {
            ushort[] joints;
            float[][] bindposes;
            SkinBinder.Bind(file, boneNames, 0, blendShapeNames, out joints, out bindposes);
        }
        catch (FormatException e)
        {
            return Check(e.Message.IndexOf(cause, StringComparison.Ordinal) >= 0,
                         what + " - refused, but not with '" + cause + "': " + e.Message);
        }
        throw new Exception("BINDER-FROZEN FAILURE: " + what + " - it bound instead of refusing");
    }

    /// <summary>One vertex per joint, one full-weight influence each, one distinguishable bind pose
    /// per joint - the same fixture shape BoneNames.cs uses.</summary>
    private static SkinnedModel Model(string[] jointNames)
    {
        int n = jointNames.Length;
        var m = new SkinnedModel
        {
            Positions = new ObjVector3[n],
            Joints = new ushort[n * 4],
            Weights = new float[n * 4],
            InverseBindMatrices = new float[n][]
        };
        for (int j = 0; j < n; j++)
        {
            m.Positions[j] = new ObjVector3(j, 0f, 0f);
            m.Joints[j * 4] = (ushort)j;
            m.Weights[j * 4] = 1f;
            m.JointNames.Add(jointNames[j]);
            m.InverseBindMatrices[j] = new[] { 1f, 0f, 0f, 0f, 0f, 1f, 0f, 0f, 0f, 0f, 1f, 0f, j + 1f, 0f, 0f, 1f };
        }
        m.Submeshes.Add(new[] { 0, 0, 0 });
        return m;
    }

    private static SkinnedModel Empty()
    {
        var m = new SkinnedModel
        {
            Positions = new[] { new ObjVector3(0f, 0f, 0f) },
            Joints = new ushort[4],
            Weights = new float[4],
            InverseBindMatrices = new float[0][]
        };
        m.Submeshes.Add(new[] { 0, 0, 0 });
        return m;
    }

    private static int Code(ImportCode want, byte[] bytes, string what)
    {
        try { GlbReader.Read(bytes); }
        catch (ImportRefusedException e)
        {
            return Check(e.Code == want, what + " - got code " + e.Code + ": " + e.Message);
        }
        throw new Exception("BINDER-FROZEN FAILURE: " + what + " - it did not refuse at all");
    }

    private static int Check(bool condition, string what)
    {
        if (!condition) throw new Exception("BINDER-FROZEN FAILURE: " + what);
        return 1;
    }
}
