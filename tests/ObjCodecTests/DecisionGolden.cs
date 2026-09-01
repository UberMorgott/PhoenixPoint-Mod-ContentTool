using System;
using Morgott.ContentTool.Import;

/// <summary>
/// THE VERDICT, ONCE. BundleBaker.ReplaceMesh (src\Bake\BundleBaker.cs:153-202) chooses between four
/// endings, and the Model Doctor has to predict the same one. The prediction is not a copy of those
/// branches - both call ReplacementDecision.Decide - and this table is the record of what those
/// branches DO, quoted line by line, so a change to the bake that forgets the Doctor fails here.
///
/// The branches, in the bake's own order:
///   :153-156  model==null || JointNames.Count==0, and SkinFields.Rigged(mesh)  -> refusal, writes NOTHING
///   :176-177  names = null when the source carries no armature
///   :180-184  Rebind returns false on a mesh with no bind poses                 -> "not rigged"
///   :180-183  Rebind returns true                                              -> nearest-bone
///   :190-195  RebindByName returned                                            -> BY NAME
///   :197-201  RebindByName threw                                               -> nearest-bone
/// </summary>
internal static class DecisionGolden
{
    internal static string Run()
    {
        var issue = new BindingIssue { Code = BindCode.MissingBone, Message = "x" };
        int checks = 0;

        // A skinless source onto a RIGGED target is the one case that writes nothing at all.
        checks += Is(Outcome.Refused, false, true, false, null, "skinless onto rigged is REFUSED (:153-156)");
        checks += Is(Outcome.Refused, false, true, true, null, "and stays refused however the target names its bones");

        // A skinless source onto an unrigged target: the guard is skipped, Rebind finds no bind poses.
        checks += Is(Outcome.NotRigged, false, false, false, null, "skinless onto unrigged is NOT RIGGED (:184)");

        // A rigged source onto an unrigged target: same sentence, same reason.
        checks += Is(Outcome.NotRigged, true, false, false, null, "rigged source onto unrigged target is NOT RIGGED");
        checks += Is(Outcome.NotRigged, true, false, true, null, "even when the bundle does name bones");

        // A rigged source, a rigged target, but nothing in the bundle names the target's bones.
        checks += Is(Outcome.NearestBone, true, true, false, null, "no bone names available is NEAREST-BONE (:178-183)");
        checks += Is(Outcome.NearestBone, true, true, false, issue, "and an issue cannot make that worse");

        // The two that decide whether the author's weights survive.
        checks += Is(Outcome.ByName, true, true, true, null, "a clean binding is BY NAME (:190-195)");
        checks += Is(Outcome.NearestBone, true, true, true, issue, "one issue is enough to fall back (:197-201)");

        return "DECISION PASS, " + checks + " check(s) - one Decide, four outcomes, the bake's own branches";
    }

    private static int Is(Outcome want, bool armature, bool rigged, bool names, BindingIssue first, string what)
    {
        Outcome got = ReplacementDecision.Decide(armature, rigged, names, first);
        if (got != want) throw new Exception("DECISION FAILURE: " + what + " - wanted " + want + ", got " + got);
        return 1;
    }
}
