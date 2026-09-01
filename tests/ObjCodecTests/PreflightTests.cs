using System;
using System.Collections.Generic;
using System.IO;
using Morgott.ContentTool.Doctor;
using Morgott.ContentTool.Import;

/// <summary>
/// THE WHOLE PIPELINE, on a REAL committed .glb (lib\u9_probe.glb - rigged, the same file gate U9
/// reads). Model-and-name-list fixtures cannot reach this: they never exercise the byte path, the
/// sidecar, the skinless guard or the not-rigged branch, which are four of the places the Doctor and
/// the bake could disagree.
///
/// Every target below is built FROM the file's own joint names, so a passing run is not a constant
/// that happens to match - change the fixture and the expectations move with it.
/// </summary>
internal static class PreflightTests
{
    internal static string Run()
    {
        byte[] bytes = File.ReadAllBytes(AliasTests.Probe());
        SkinnedModel probe = GlbReader.Read(bytes);
        string[] own = probe.JointNames.ToArray();
        int checks = 0;

        string dir = Path.Combine(Path.GetTempPath(), "ct_preflight_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            string glb = Path.Combine(dir, "probe.glb");
            File.WriteAllBytes(glb, bytes);

            // ---- the file against its own skeleton: the one outcome that keeps the author's weights.
            ReplacementPreflightResult ok = ReplacementPreflight.Run(bytes, glb, Rig(own));
            checks += Check(ok.Outcome == Outcome.ByName, "the file binds onto its own bone names: " + ok.Outcome);
            checks += Check(ok.Baked != null && ok.Baked.Mesh.VertexCount > 0,
                            "the bake's own mesh build ran, so a preview has something to show");
            checks += Check(ok.Sha256 == AliasMap.Sha256(bytes), "the result carries the bytes it was computed from");

            // ---- one renamed bone: the case the whole feature exists for.
            string[] renamed = (string[])own.Clone();
            string was = renamed[0];
            renamed[0] = "CT_NOT_IN_FILE";
            ReplacementPreflightResult bad = ReplacementPreflight.Run(bytes, glb, Rig(renamed));
            checks += Check(bad.Outcome == Outcome.NearestBone,
                            "one wrong bone name costs the author's weights: " + bad.Outcome);
            checks += Check(Has(bad, "MissingBone") && Has(bad, "ExtraBone"),
                            "and BOTH halves of that are listed, not just the first: " + Codes(bad));

            // ---- the alias fixes it, through the sidecar, exactly as the bake would read it.
            AliasMap.SaveSidecar(glb, ok.Sha256, bytes.Length,
                                 new Dictionary<string, string> { { was, "CT_NOT_IN_FILE" } });
            ReplacementPreflightResult fixedUp = ReplacementPreflight.Run(bytes, glb, Rig(renamed));
            checks += Check(fixedUp.Outcome == Outcome.ByName,
                            "the sidecar alias turns it back into BY NAME: " + fixedUp.Outcome + " " + Codes(fixedUp));

            // ---- a STALE sidecar is a warning, and the outcome comes from the UNALIASED model.
            AliasMap.SaveSidecar(glb, "deadbeef", bytes.Length,
                                 new Dictionary<string, string> { { was, "CT_NOT_IN_FILE" } });
            ReplacementPreflightResult stale = ReplacementPreflight.Run(bytes, glb, Rig(renamed));
            checks += Check(stale.Outcome == Outcome.NearestBone,
                            "a stale sidecar does not silently fix anything: " + stale.Outcome);
            checks += Check(Has(stale, "SidecarStale") && Severity(stale, "SidecarStale") == Morgott.ContentTool.Doctor.Severity.Warning,
                            "and it is a WARNING, not the reason for the outcome: " + Codes(stale));
            File.Delete(AliasMap.SidecarPathOf(glb));

            // ---- an alias that names a bone the TARGET does not have. Only the preflight can know.
            AliasMap.SaveSidecar(glb, ok.Sha256, bytes.Length,
                                 new Dictionary<string, string> { { was, "CT_NO_SUCH_TARGET_BONE" } });
            ReplacementPreflightResult wrongOut = ReplacementPreflight.Run(bytes, glb, Rig(own));
            checks += Check(Has(wrongOut, "AliasNotATargetBone"),
                            "an alias output that is not a target bone is named: " + Codes(wrongOut));
            checks += Check(Severity(wrongOut, "AliasNotATargetBone") == Morgott.ContentTool.Doctor.Severity.Warning,
                            "and it is a WARNING - a sidecar never decides the outcome");
            File.Delete(AliasMap.SidecarPathOf(glb));

            // ---- the target the game gave us has no bone list at all.
            RigTarget noNames = Rig(own);
            noNames.BoneNames = null;
            ReplacementPreflightResult blind = ReplacementPreflight.Run(bytes, glb, noNames);
            checks += Check(blind.Outcome == Outcome.NearestBone,
                            "no target bone names is NEAREST-BONE, not a crash");
            // The row must be the BINDER's own sentence: a second wording under the same code is how a
            // remedy and the thing it explains stop matching.
            checks += Check(Message(blind, "TargetBonesUnavailable") ==
                            "the target model lists no bones, so there is no skeleton to bind onto; " +
                            "reload the scene and try again",
                            "and it is reported in SkinCompatibility's own words: " + Codes(blind));

            // ---- the target is not rigged.
            RigTarget flat = Rig(own);
            flat.Rigged = false;
            flat.BindPoseCount = 0;
            checks += Check(ReplacementPreflight.Run(bytes, glb, flat).Outcome == Outcome.NotRigged,
                            "a target with no bind poses is NOT RIGGED");

            // ---- bind poses and named bones DISAGREE. Analyze cannot see this - it is handed names
            // only - but SkinFields.RebindByName throws on it (SkinFields.cs:738-741) and the bake
            // falls back, so a Doctor that answered ByName here would promise weights the bake drops.
            RigTarget lopsided = Rig(own);
            lopsided.BindPoseCount = own.Length + 1;
            ReplacementPreflightResult skew = ReplacementPreflight.Run(bytes, glb, lopsided);
            checks += Check(skew.Outcome == Outcome.NearestBone,
                            "more bind poses than named bones is NEAREST-BONE, as the bake makes it: " + skew.Outcome);
            checks += Check(Message(skew, "TargetBindPoseMismatch") ==
                            "the target has " + (own.Length + 1) + " bind pose(s) but " + own.Length +
                            " named bone(s), so a bone in the file cannot be matched to one of them",
                            "in RebindByName's own words: " + Codes(skew));
            checks += Check(Severity(skew, "TargetBindPoseMismatch") == Morgott.ContentTool.Doctor.Severity.Downgrade,
                            "and it costs the weights rather than refusing the import");

            // ---- a skinless source onto a rigged target: the one case that writes nothing.
            byte[] skinlessBytes = GlbCodec.Write(Skinless(GlbReader.Read(bytes)));
            string skinlessPath = Path.Combine(dir, "skinless.glb");
            File.WriteAllBytes(skinlessPath, skinlessBytes);
            ReplacementPreflightResult refused = ReplacementPreflight.Run(skinlessBytes, skinlessPath, Rig(own));
            checks += Check(refused.Outcome == Outcome.Refused && Has(refused, "SkinlessOntoRigged"),
                            "a skinless source onto a rigged target is REFUSED: " + refused.Outcome + " " + Codes(refused));

            // ---- garbage in. The worker must never throw; it must report.
            ReplacementPreflightResult junk = ReplacementPreflight.Run(new byte[] { 7, 7, 7, 7 }, glb, Rig(own));
            checks += Check(junk.Outcome == Outcome.Refused && Has(junk, "MalformedGlb"),
                            "four bytes of nonsense come back as a REPORT, not an exception: " + Codes(junk));
        }
        finally { try { Directory.Delete(dir, true); } catch (Exception) { } }

        return "PREFLIGHT PASS, " + checks + " check(s) - lib\\u9_probe.glb through the real pipeline";
    }

    /// <summary>The same geometry with the rig taken off it entirely. GlbCodec.Write keys "static" on
    /// JointNodes and refuses a model that keeps a hierarchy or weights without joints
    /// (GlbCodec.cs:1021), so every skin field has to go, not just the names.</summary>
    private static SkinnedModel Skinless(SkinnedModel model)
    {
        model.JointNames.Clear();
        model.JointNodes = new int[0];
        model.Nodes.Clear();
        model.InverseBindMatrices = null;
        model.Joints = null;
        model.Weights = null;
        return model;
    }

    /// <summary>A target that says yes to everything except what the caller changes.</summary>
    private static RigTarget Rig(string[] boneNames)
    {
        return new RigTarget
        {
            BoneNames = boneNames,
            Rigged = true,
            RendererInstanceId = 1,
            MeshInstanceId = 2,
            BindPoseCount = boneNames.Length,
            TransformPath = "Root/Body",
            MeshName = "CHR_TEST"
        };
    }

    private static bool Has(ReplacementPreflightResult r, string code)
    {
        foreach (Diagnostic d in r.Report.Rows) if (d.Code == code) return true;
        return false;
    }

    private static Morgott.ContentTool.Doctor.Severity Severity(ReplacementPreflightResult r, string code)
    {
        foreach (Diagnostic d in r.Report.Rows) if (d.Code == code) return d.Severity;
        throw new Exception("PREFLIGHT FAILURE: no row '" + code + "'");
    }

    private static string Message(ReplacementPreflightResult r, string code)
    {
        foreach (Diagnostic d in r.Report.Rows) if (d.Code == code) return d.Message;
        throw new Exception("PREFLIGHT FAILURE: no row '" + code + "'");
    }

    private static string Codes(ReplacementPreflightResult r)
    {
        var names = new List<string>();
        foreach (Diagnostic d in r.Report.Rows) names.Add(d.Code);
        return "[" + string.Join(", ", names.ToArray()) + "]";
    }

    private static int Check(bool condition, string what)
    {
        if (!condition) throw new Exception("PREFLIGHT FAILURE: " + what);
        return 1;
    }
}
