using System;
using System.Collections.Generic;
using Morgott.ContentTool.Bake;
using Morgott.ContentTool.Dev;
using Morgott.ContentTool.Doctor;
using Morgott.ContentTool.Import;

/// <summary>
/// THE BENCH'S PLAIN WORDS (UI redesign 2026-09-28): body regions for bone counts, def names in plain
/// words, the verdict badge, the slot-level target search and the "Build &amp; test" chain that stops
/// before Package. All of it is pure - measured here, drawn by the Unity half.
/// </summary>
internal static class BenchWordsTests
{
    private static int checks;

    internal static string Run()
    {
        checks = 0;

        // ---- regions: the names real rigs use.
        Check(BenchList.RegionOf("L.ForeArm") == "Left arm", "L.ForeArm -> " + BenchList.RegionOf("L.ForeArm"));
        Check(BenchList.RegionOf("R.Index_2") == "Right hand", "R.Index_2 -> " + BenchList.RegionOf("R.Index_2"));
        Check(BenchList.RegionOf("Bip01 L Thigh") == "Left leg", "Bip01 L Thigh");
        Check(BenchList.RegionOf("hand_r") == "Right hand", "hand_r");
        Check(BenchList.RegionOf("LeftToeBase") == "Left leg", "LeftToeBase");
        Check(BenchList.RegionOf("Head") == "Head" && BenchList.RegionOf("Neck_1") == "Head", "head/neck");
        Check(BenchList.RegionOf("Spine_2") == "Body" && BenchList.RegionOf("Root") == "Body", "spine/root");
        Check(BenchList.RegionOf("CHR_Human_Rig_Ready") == "Other", "a rig node is Other");
        int[] n = BenchList.CountByRegion(new[] { "L.Hand", "L.Index_1", "R.Arm", "Head", "weird" });
        Check(n[Array.IndexOf(BenchList.Regions, "Left hand")] == 2 && n[Array.IndexOf(BenchList.Regions, "Right arm")] == 1 &&
              n[Array.IndexOf(BenchList.Regions, "Other")] == 1, "CountByRegion");

        // ---- plain words for def names.
        Check(PrototypeCatalog.SlotWord("Human_Head_SlotDef") == "Head", "slot " + PrototypeCatalog.SlotWord("Human_Head_SlotDef"));
        Check(PrototypeCatalog.SlotWord("Crabman_LeftArm_SlotDef") == "Left arm", "slot " + PrototypeCatalog.SlotWord("Crabman_LeftArm_SlotDef"));
        Check(PrototypeCatalog.VariantWord("Human / AN_Assault1") == "Anu Assault 1", "variant " + PrototypeCatalog.VariantWord("Human / AN_Assault1"));
        Check(PrototypeCatalog.VariantWord("Human / AN_Assault2_1") == "Anu Assault 2-1", "variant " + PrototypeCatalog.VariantWord("Human / AN_Assault2_1"));
        Check(PrototypeCatalog.VariantWord("PX_Assault1_CharacterTemplateDef") == "Phoenix Assault 1",
              "unit " + PrototypeCatalog.VariantWord("PX_Assault1_CharacterTemplateDef"));
        Check(PrototypeCatalog.VariantWord("Crabman") == "Crabman", "a bare name is kept");
        Check(BenchList.StageWord("Verify") == "Verify install" && BenchList.StageWord("Odd") == "Odd", "stage words");

        // ---- the badge.
        var report = new DiagnosticReport { Outcome = Outcome.ByName };
        Check(report.Level() == Grade.Pass, "clean by-name is PASS");
        report.Add("SubmeshMaterials", Severity.Warning, DiagnosticSide.File, "w");
        Check(report.Level() == Grade.Warn && report.Plain().Contains("1 warning"), "a warning makes WARN: " + report.Plain());
        report.Outcome = Outcome.NearestBone;
        Check(report.Level() == Grade.Warn, "nearest-bone is WARN");
        report.Add("X", Severity.Blocking, DiagnosticSide.File, "b");
        Check(report.Level() == Grade.Fail, "a blocking row is FAIL whatever the outcome");

        // ---- slot-level search.
        var rec = new PrototypeRecord { Id = "h", DisplayName = "Human", Category = "Human & Anu" };
        var v1 = new PrototypeVariant { Name = "Human / AN_Assault1" };
        v1.Slots.Add(new PrototypeSlot { SlotDefName = "Human_Head_SlotDef" });
        v1.Slots.Add(new PrototypeSlot { SlotDefName = "Human_Legs_SlotDef" });
        var v2 = new PrototypeVariant { Name = "Human / NJ_Heavy1" };
        v2.Slots.Add(new PrototypeSlot { SlotDefName = "Human_Head_SlotDef" });
        rec.Variants.Add(v1); rec.Variants.Add(v2);
        var all = new List<PrototypeRecord> { rec };
        int total;
        List<PrototypeCatalog.SlotHit> hits = PrototypeCatalog.SearchSlots(all, "anu head", 10, out total);
        Check(total == 1 && hits[0].Variant == v1 && hits[0].SlotDefName == "Human_Head_SlotDef",
              "'anu head' finds exactly Anu Assault 1's head: " + total);
        hits = PrototypeCatalog.SearchSlots(all, "head", 1, out total);
        Check(total == 2 && hits.Count == 1, "the cap limits rows, not the count: " + total + "/" + hits.Count);
        Check(PrototypeCatalog.SearchSlots(all, "  ", 10, out total).Count == 0 && total == 0, "empty query finds nothing");

        // ---- Build & test: the chain stops after Verify, Package is not reached.
        var chain = new LifecycleState.Sequence("Verify");
        var ctx = new LifecycleState.Admission
        {
            Selection = LifecycleState.Selection.Ok,
            ProjectId = "morgott.demo",
            Copies = Freshness.Fresh,
            ValidateOutcome = GateOutcome.Pass,
            BakeOutcome = GateOutcome.Pass,
            ApplyOutcome = GateOutcome.Pass
        };
        var seen = new List<string>();
        for (string s = chain.Next(ctx); s != null && seen.Count < 10; s = chain.Next(ctx)) seen.Add(s);
        Check(string.Join(",", seen.ToArray()) == "Validate,Bake,Apply,Verify" && chain.Done && !chain.Stopped,
              "Build & test runs to Verify and never hands out Package: " + string.Join(",", seen.ToArray()));
        var full = new LifecycleState.Sequence();
        var every = new List<string>();
        for (string s = full.Next(ctx); s != null && every.Count < 10; s = full.Next(ctx)) every.Add(s);
        Check(string.Join(",", every.ToArray()) == "Validate,Bake,Apply,Verify,Package",
              "Run all is unchanged: " + string.Join(",", every.ToArray()));

        return "BENCH-WORDS PASS, " + checks + " check(s)";
    }

    private static void Check(bool condition, string what)
    {
        if (!condition) throw new Exception("BENCH-WORDS FAILURE: " + what);
        checks++;
    }
}
