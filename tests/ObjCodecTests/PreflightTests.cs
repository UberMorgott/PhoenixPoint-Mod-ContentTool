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

            // ================= EXTEND, against the LIVE census's own rigs =================
            // Not a bone list somebody typed: these are the 2551 transforms slice 0 measured on
            // D:\PP-Instance2, read through the same PrototypeCatalog the picker is built from. The FILE
            // is the probe with its joints renamed onto real rig bones - which is what a body part
            // authored in Blender against that rig IS.
            PrototypeRecord human = CatalogTests.Live("CHR_Human_Rig_Ready");
            checks += Check(human.BindableBones.Count >= own.Length && own.Length >= 2,
                            "the Human rig has room for the probe's " + own.Length + " joint(s)");

            // ---- A PARTIAL BODY PART: a handful of the rig's bindable bones and none of the rest.
            // Replace would call every absent bone a defect; Extend is the mode that says a hand does
            // not carry the legs.
            ReplacementPreflightResult part = Extended(dir, bytes, human.BindableBones, -1, Extend(human));
            checks += Check(part.Outcome == Outcome.ByName && !Has(part, "MissingBone"),
                            "Extend accepts a partial body part: " + part.Outcome + " " + Codes(part));

            // ---- the SAME file on the Replace path is a promise to reproduce a whole renderer, so
            // every bone it does not carry is still MissingBone. The two modes must not converge.
            ReplacementPreflightResult exact = Extended(dir, bytes, human.BindableBones, -1,
                                                        Replace(human, human.BindableBones));
            checks += Check(exact.Outcome == Outcome.NearestBone && Has(exact, "MissingBone"),
                            "Replace still requires every bone of the target: " + exact.Outcome);

            // ---- a joint that names NO rig bone. Extend has no nearest-bone fallback to degrade into -
            // nothing attaches a part that does not resolve - so it is refused, not downgraded.
            var stray = new List<string>(human.BindableBones);
            stray[0] = "CT_NOT_ON_THE_RIG";
            ReplacementPreflightResult adds = Extended(dir, bytes, stray, -1, Extend(human));
            checks += Check(adds.Outcome == Outcome.Refused && Has(adds, "ExtraBone"),
                            "a joint the rig does not have refuses the Extend: " + adds.Outcome + " " + Codes(adds));

            // ---- two file joints onto ONE rig bone: nothing can say which of them wins.
            var twice = new List<string>(human.BindableBones);
            twice[1] = twice[0];
            ReplacementPreflightResult dup = Extended(dir, bytes, twice, -1, Extend(human));
            checks += Check(dup.Outcome == Outcome.Refused && Has(dup, "DuplicateFileBone"),
                            "two file joints on one rig bone refuse the Extend: " + dup.Outcome + " " + Codes(dup));

            // ---- an EXT_ joint the file WEIGHTS. Addon.GetEquivalentBones skips every EXT_ transform
            // (Addon.cs:1208), so those weights are lost without a word - the one thing a report has to
            // say out loud rather than let the author find in game.
            int hot = WeightedJoint(probe);
            var attached = new List<string>(human.BindableBones);
            attached[hot] = "EXT_Grip";
            ReplacementPreflightResult lost = Extended(dir, bytes, attached, -1, Extend(human));
            checks += Check(lost.Outcome == Outcome.Refused &&
                            Severity(lost, "ExtJointWeighted") == Morgott.ContentTool.Doctor.Severity.Blocking,
                            "a WEIGHTED EXT_ joint blocks: " + lost.Outcome + " " + Codes(lost));

            // ---- the same joint with its weights taken off costs nothing, so it is a note and the
            // verdict is untouched by it.
            ReplacementPreflightResult noise = Extended(dir, bytes, attached, hot, Extend(human));
            checks += Check(noise.Outcome == Outcome.ByName && !Has(noise, "ExtJointWeighted") &&
                            Severity(noise, "ExtJointUnused") == Morgott.ContentTool.Doctor.Severity.Warning,
                            "an UNWEIGHTED EXT_ joint is only noted: " + noise.Outcome + " " + Codes(noise));

            // ---- THE DUPLICATE RULE. Three shipped vehicle rigs carry 'light' twice and the game binds
            // to the first it finds (Addon.cs:1202-1231), so a part that names it cannot be told where
            // it would land - and every part that does not name it is untouched.
            PrototypeRecord vehicle = CatalogTests.Live("VEH_NJ_Armadillo_Rig_Ready");
            var quiet = new List<string>(vehicle.BindableBones);
            quiet.Remove("light");
            checks += Check(vehicle.AmbiguousNames.Contains("light") && quiet.Count >= own.Length,
                            "the Armadillo carries 'light' twice, and enough other bones to test it with");
            ReplacementPreflightResult unrelated = Extended(dir, bytes, quiet, -1, Extend(vehicle));
            checks += Check(unrelated.Outcome == Outcome.ByName && !Has(unrelated, "TargetBoneDuplicate"),
                            "a part that never names 'light' is not blocked by it: " + unrelated.Outcome);
            var lit = new List<string>(quiet);
            lit[0] = "light";
            ReplacementPreflightResult ambiguous = Extended(dir, bytes, lit, -1, Extend(vehicle));
            checks += Check(ambiguous.Outcome == Outcome.Refused && Has(ambiguous, "TargetBoneDuplicate"),
                            "a part that DOES name it is refused: " + ambiguous.Outcome + " " + Codes(ambiguous));

            // ---- a slot whose rebuild produced NO renderer. Replace is refused outright rather than
            // judged against a bone list fabricated from the full rig; Extend never needed a renderer,
            // so it answers exactly as before.
            PrototypeTarget dark = Extend(human);
            dark.Unavailable = "slot visual unavailable";
            checks += Check(Extended(dir, bytes, human.BindableBones, -1, dark).Outcome == Outcome.ByName,
                            "an unavailable slot still answers in Extend");
            dark.Mode = VerifyMode.Replace;
            ReplacementPreflightResult nothing = ReplacementPreflight.Run(bytes, glb, dark);
            checks += Check(nothing.Outcome == Outcome.Refused &&
                            Message(nothing, "SlotVisualUnavailable").StartsWith("slot visual unavailable"),
                            "and Replace on it is refused in its own words: " + Codes(nothing));

            // ---- a Replace pick whose renderer is simply gone. A REPORT, never an exception - and the
            // same empty target the shipped path already answers NOT RIGGED for, never a bone list
            // invented from the rig standing behind the slot.
            ReplacementPreflightResult gone = ReplacementPreflight.Run(bytes, glb, Replace(human, null));
            checks += Check(gone.Outcome == Outcome.NotRigged && Has(gone, "TargetNotRigged") &&
                            gone.Outcome == ReplacementPreflight.Run(bytes, glb, (RigTarget)null).Outcome,
                            "a Replace pick with no live renderer answers exactly as an empty target: " +
                            gone.Outcome + " " + Codes(gone));
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

    private static PrototypeTarget Extend(PrototypeRecord record)
    {
        return new PrototypeTarget { Record = record, Mode = VerifyMode.Extend, SlotDefName = "(whole rig)" };
    }

    /// <summary>A Replace pick, with the live renderer the bay rebuild would have produced - or none at
    /// all, which is the slot whose renderer has gone.</summary>
    private static PrototypeTarget Replace(PrototypeRecord record, IList<string> liveBones)
    {
        var target = new PrototypeTarget { Record = record, Mode = VerifyMode.Replace, SlotDefName = "(slot)" };
        if (liveBones != null) target.Live = Rig(Copy(liveBones));
        return target;
    }

    /// <summary>
    /// THE PROBE, ITS JOINTS RENAMED ONTO REAL RIG BONES, written back out as a real .glb and run
    /// through the whole byte path - so an Extend case exercises the reader, the sidecar and the
    /// verdict exactly as the Doctor will, not a hand-built model. Renaming a joint is the sidecar's
    /// own operation (AliasMap.Apply:67-83), name and node together.
    /// </summary>
    /// <param name="unweight">a joint index whose influences are MOVED onto another bone, or -1. The
    /// EXT_ policy turns on whether a bone carries weights, so a case about it has to be able to take
    /// them away - and zeroing them instead would leave vertices weighted to nothing, which is a
    /// broken file rather than an unused bone.</param>
    private static ReplacementPreflightResult Extended(string dir, byte[] bytes, IList<string> names,
                                                       int unweight, PrototypeTarget target)
    {
        SkinnedModel model = GlbReader.Read(bytes);
        for (int j = 0; j < model.JointNames.Count; j++)
        {
            model.JointNames[j] = names[j];
            int node = model.JointNodes[j];
            if (node >= 0 && node < model.Nodes.Count) model.Nodes[node].Name = names[j];
        }
        if (unweight >= 0)
        {
            ushort onto = unweight == 0 ? (ushort)1 : (ushort)0;
            for (int i = 0; i < model.Joints.Length; i++)
                if (model.Joints[i] == unweight) model.Joints[i] = onto;
        }
        byte[] glb = GlbCodec.Write(model);
        string path = Path.Combine(dir, "part_" + Guid.NewGuid().ToString("N") + ".glb");
        File.WriteAllBytes(path, glb);
        return ReplacementPreflight.Run(glb, path, target);
    }

    /// <summary>A joint the probe really paints weights with. An EXT_ case about a bone that carries
    /// nothing would prove the opposite of what it claims.</summary>
    private static int WeightedJoint(SkinnedModel model)
    {
        for (int i = 0; i < model.Joints.Length; i++)
            if (model.Weights[i] > 0f) return model.Joints[i];
        throw new Exception("PREFLIGHT FAILURE: the probe paints no weights at all");
    }

    private static string[] Copy(IList<string> names)
    {
        var copy = new string[names.Count];
        names.CopyTo(copy, 0);
        return copy;
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
