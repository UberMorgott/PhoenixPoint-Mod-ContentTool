using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Morgott.ContentTool.Bake;
using Morgott.ContentTool.Import;
using Morgott.ContentTool.IO;
using Morgott.ContentTool.Project;

/// <summary>The lifecycle dashboard's CARRIER and its one verdict formatter.
///
/// G1 - a read-back that failed nothing is not therefore a PASS. Every VOID arm of the read-back returns 0
/// exactly like a pass (`ReadBack.cs:51`, `:105`, `:118`, `:141`, `:171` and the seven P6 arms), so a
/// failure count plus a log cannot tell the two apart and the panel would print PASS over a row nothing
/// proved. The carrier answers structurally, without anyone reading text.
///
/// G2 - the wording, frozen against `2026-09-05-lifecycle-dashboard-design.md:365`-`:407`. EVERY producer now
/// composes through StageText - `ModelDoctor` (S1/S2), `Route7` (R29/R35), `ProjectBake` (S4/S5 and the three
/// bake special cases) and `Package` (S7, the already-holds refusal) - so there is no second copy on disk to
/// compare against, and a literal re-typed here would only be this file agreeing with itself. What the arms
/// below are is a WORDING FREEZE: an accidental reword fails them, and the design line is the authority.
/// The S4/S5 and bake-special-case arms were dropped outright when `ProjectBake` joined (Task 2) - their
/// wording is now proven by W18's console/dashboard parity row, in game, against the real producer.
///
/// G3 - `Tail`, moved here out of `ModelDoctor` (it was private static in a file no test can link). Its
/// current semantics are FROZEN, not fixed: the trailing empty element from AppendLine is dropped before the
/// count (the R11 path - a blank result line under a failed bake), blank lines inside the window are skipped,
/// and the result is joined with Environment.NewLine and trimmed.</summary>
internal static class LifecycleTests
{
    internal static string Run()
    {
        int checks = 0;
        string nl = Environment.NewLine;

        // ---- G1 carrier: an all-VOID read-back is NOT a PASS, and nobody reads text to find out.
        ReadBackResult allVoid = ReadBackResult.Of(null,
            GateEntry.Void("P4", "mesh_a", "P4 VOID mesh 'mesh_a' was not read back"),
            GateEntry.Void("P4-bytes", "mesh_a", "P4-bytes VOID mesh 'mesh_a' has no readable buffers"));
        checks += Check(allVoid.Failed == 0 && allVoid.MandatoryVoid("mesh_a", RowKind.Mesh, null),
                        "zero failures with a mandatory VOID is VOID, never S6");
        checks += Check(allVoid.Void == 2 && allVoid.Passed == 0 && allVoid.Entries.Length == 2,
                        "the counts are structured, not parsed out of the log");

        // A mesh row needs BOTH P4 and P4-bytes; P5/P6 may be VOID (a skinless or same-order source has
        // nothing to measure - ReadBack.cs:141, :357), and that must not sink the row.
        ReadBackResult meshOk = ReadBackResult.Of(null,
            GateEntry.Pass("P4", "mesh_a", "P4 mesh 'mesh_a' read back"),
            GateEntry.Pass("P4-bytes", "mesh_a", "P4-bytes 12 vertices"),
            GateEntry.Void("P5", "mesh_a", "P5 VOID no bind poses"),
            GateEntry.Void("P6", "mesh_a", "P6 VOID same bone order"));
        checks += Check(!meshOk.MandatoryVoid("mesh_a", RowKind.Mesh, null),
                        "P5/P6 VOID on a proven mesh row is allowed - only P4 and P4-bytes are mandatory");
        checks += Check(meshOk.Passed == 2 && meshOk.Void == 2 && meshOk.Failed == 0,
                        "pass/void/fail are counted apart");
        checks += Check(ReadBackResult.Of(null,
                            GateEntry.Pass("P4", "mesh_a", "x"),
                            GateEntry.Void("P4-bytes", "mesh_a", "y")).MandatoryVoid("mesh_a", RowKind.Mesh, null),
                        "one mandatory gate VOID is enough - P4-bytes is not optional");
        // A gate that never ran is not a proof either: absence and VOID are the same answer here.
        checks += Check(ReadBackResult.Of(null, GateEntry.Pass("P4", "mesh_a", "x"))
                            .MandatoryVoid("mesh_a", RowKind.Mesh, null),
                        "a mandatory gate with NO entry at all is VOID, not an implied pass");
        checks += Check(meshOk.MandatoryVoid("mesh_b", RowKind.Mesh, null),
                        "another target's proofs never satisfy this target");
        // A TEXTURE ROW IS KEYED ON ITS BUNDLE. P1 and its control are recorded under the bundle file
        // (ReadBack.cs:51, :55, :57) because they measure every declared texture of that bundle at once,
        // so a lookup by the row's asset name matches nothing and calls every measured texture unproven.
        ReadBackResult texBundle = ReadBackResult.Of(null,
            GateEntry.Pass("P1", "a.bundle", "P1 PASS every replaced Texture2D in a.bundle reads back its new pixels"),
            GateEntry.Pass("P1-ctl-shipped", "a.bundle", "P1-ctl-shipped PASS the shipped a.bundle does NOT contain them"));
        checks += Check(!texBundle.MandatoryVoid("tex_a", RowKind.Texture, "a.bundle"),
                        "a texture row is proven by its BUNDLE's P1 pair, not by its asset name");
        checks += Check(texBundle.MandatoryVoid("tex_a", RowKind.Texture, "other.bundle") &&
                        texBundle.MandatoryVoid("tex_a", RowKind.Texture, null),
                        "another bundle's P1 pair - or no bundle named at all - leaves the texture row unproven");
        checks += Check(ReadBackResult.Of(null,
                            GateEntry.Pass("P1", "a.bundle", "x"),
                            GateEntry.Void("P1-ctl-shipped", "a.bundle", "y"))
                            .MandatoryVoid("tex_a", RowKind.Texture, "a.bundle") &&
                        !texBundle.MandatoryVoid("tex_a", RowKind.Texture, "a.bundle"),
                        "a texture row needs P1 AND P1-ctl-shipped");
        checks += Check(ReadBackResult.Of(null, GateEntry.Void("P3", "mat_a", "x")).MandatoryVoid("mat_a", RowKind.Material, null) &&
                        !ReadBackResult.Of(null, GateEntry.Pass("P3", "mat_a", "x")).MandatoryVoid("mat_a", RowKind.Material, null),
                        "a material row needs P3");
        // A CLIP READ-BACK THAT DISAGREES WITH ITSELF IS A COUNTED FAIL, NEVER A THROW: it runs inside
        // ProjectBake.Patch on the player's checkbox path (ProjectBake.cs:1657 <- Route7.cs:340), where an
        // exception loses the whole bake log, writes no patch cache and arms no disposition.
        StringBuilder disagreed = new StringBuilder();
        List<GateEntry> selfCheck = new List<GateEntry>();
        GateEntry.SelfCheck(disagreed, selfCheck, "clip_a", 1, 0);
        GateEntry.SelfCheck(disagreed, selfCheck, "clip_b", 1, 1);   // agreement says nothing at all
        checks += Check(selfCheck.Count == 1 && selfCheck[0].Outcome == GateOutcome.Fail &&
                        disagreed.ToString().StartsWith("P7 FAIL the clip read-back disagrees with itself:"),
                        "a P7 self-disagreement is one counted FAIL entry and no exception; agreement says nothing");
        // THE REVERSE DIRECTION IS NOT A SECOND FAILURE. sliced > counted means the slicer already made a
        // FAIL entry off that line (ReadBack.cs:208), so a counted P7 on top of it reported 2 FAILURE(S)
        // for one defect. It is a VOID note - said, never counted twice.
        List<GateEntry> reverse = new List<GateEntry>();
        StringBuilder reverseLog = new StringBuilder();
        GateEntry.SelfCheck(reverseLog, reverse, "clip_c", 0, 1);
        checks += Check(reverse.Count == 1 && reverse[0].Outcome == GateOutcome.Void &&
                        ReadBackResult.Of(null, reverse.ToArray()).Failed == 0 &&
                        reverseLog.ToString().StartsWith("P7 VOID the clip read-back disagrees with itself:"),
                        "counted < sliced is an UNCOUNTED P7 note - the sliced FAIL is the one failure, and " +
                        "the total stays the larger of the two, never their sum");
        ReadBackResult failed = ReadBackResult.Of(null, GateEntry.Fail("P4", "mesh_a", "P4 FAILED mesh 'mesh_a'"));
        checks += Check(failed.Failed == 1 && failed.Entries[0].Outcome == GateOutcome.Fail &&
                        failed.Entries[0].Gate == "P4" && failed.Entries[0].Target == "mesh_a" &&
                        failed.Entries[0].Line == "P4 FAILED mesh 'mesh_a'",
                        "an entry carries the gate id, the target key and the producer's exact line");
        ReadBackResult terminal = ReadBackResult.Of("ct_project: ALL PASS - D:\\x\\Dist\\a.bundle");
        checks += Check(terminal.Entries.Length == 0 && ReadBackResult.Of(null).Terminal == null &&
                        terminal.Terminal == "ct_project: ALL PASS - D:\\x\\Dist\\a.bundle",
                        "the terminal line rides along verbatim, never recomposed by the panel");
        BakeResult bake = new BakeResult(3, 1, "ct_project: 3 FAILURE(S)", BakeDisposition.Failed);
        checks += Check(bake.Failed == 3 && bake.PatchFailed == 1 && bake.Terminal == "ct_project: 3 FAILURE(S)" &&
                        bake.How == BakeDisposition.Failed,
                        "BakeResult keeps failed and patchFailed apart - patchFailed alone authorises " +
                        "publication; Task 3 returns one from Run and gives it a disposition (plan:143, :420)");
        // The disposition exists for THIS: a refusal that failed nothing. Read as Success it would install
        // the stale copies; counted as a patch failure it would poison Route7's session Failed set.
        BakeResult refused = new BakeResult(0, 0, StageText.R37("D:\\x"), BakeDisposition.Refused);
        checks += Check(refused.Failed == 0 && refused.PatchFailed == 0 &&
                        refused.How == BakeDisposition.Refused,
                        "R37/R38 return with ZERO counts - only the disposition tells them from a clean bake");

        // ---- G2 wording: the exact strings, from ONE producer.
        checks += Check(StageText.S1("Replace_Rifle", "px_equipment_assets_all.bundle", false) ==
                        "applied - restart the game and enable 'Replace_Rifle' in the mod manager. " +
                        "Phoenix Point already loaded px_equipment_assets_all.bundle.",
                        "S1 - design:379, composed for ModelDoctor.cs:710");
        checks += Check(StageText.S1("Replace_Rifle", "px_equipment_assets_all.bundle", true) ==
                        StageText.S1("Replace_Rifle", "px_equipment_assets_all.bundle", false) +
                        " This session keeps showing your Doctor preview.",
                        "S1 appends the preview sentence iff HasPreview - design:379");
        checks += Check(StageText.S2("px_equipment_assets_all.bundle") ==
                        "applied and redirected LIVE - px_equipment_assets_all.bundle now loads from the " +
                        "patched copy on the next load",
                        "S2 - design:380, composed for ModelDoctor.cs:712");
        checks += Check(StageText.S3("Replace_Rifle") == "Validate: PASS - 'Replace_Rifle'.",
                        "S3 is NEW - design:381");
        // S4/S5 and the three bake special cases have no WORDING arm here on purpose: ProjectBake composes
        // them through StageText (`:128`-`:133`, `:402`), so a literal re-typed here would compare this file
        // with itself. W18 proves that wording against the real producer, in game.
        // Their PREFIX is another matter - it is the one string in StageText that is PARSED:
        // StageResult.BakePassed classifies a bake by it, so a reword of these three passing sentences
        // would silently turn the B1 re-bake gate VOID (or read a pass as a failure).
        checks += Check(StageResult.BakePassed(StageText.S4("D:\\x\\Dist\\a.bundle")) &&
                        StageResult.BakePassed(StageText.BakeNoOwnBundle()) &&
                        StageResult.BakePassed(StageText.BakeNothingPatched(2)),
                        "the three passing bake sentences keep the prefix BundleResidency classifies by - " +
                        "and the arm calls the SAME helper the gate does, never a second copy of the rule");
        // ON THE TERMINAL LINE ONLY. `report.Contains("ALL PASS")` read this very report as a pass:
        // ClipFields.cs:505's skipped-clip refusal carries those two words mid-log (ProjectBake.cs:935),
        // so a FAILED bake admitted B1 and B1-rebake.
        checks += Check(!StageResult.BakePassed(
                            "P8 VOID clip 'c' skipped, rather than reporting ALL PASS over an animation " +
                            "nothing measured" + nl + StageText.S5(1)) &&
                        StageResult.BakePassed("some noise" + nl + StageText.S4("D:\\x\\Dist\\a.bundle") + nl),
                        "'ALL PASS' in the BODY of a report that ends in FAILURE(S) is not a pass - the " +
                        "terminal line is the verdict, and a trailing newline does not hide it");
        checks += Check(StageText.S6("Replace_Rifle", 2, 2) ==
                        "Verify: PASS - load-back gates passed; 2 of 2 declared target(s) served from this " +
                        "project's copies for 'Replace_Rifle'.",
                        "S6 is NEW - design:384");
        checks += Check(StageText.S6("Replace_Rifle", 1, 2) ==
                        "Verify: VOID - only 1 of 2 declared target(s) are served from this project's " +
                        "copies for 'Replace_Rifle'; the target(s) named above are unproven." &&
                        StageText.S6("Replace_Rifle", 0, 2).StartsWith("Verify: VOID"),
                        "a half-served census is VOID - design:384, 'any target missing -> VOID', never PASS");
        checks += Check(StageText.S7(4, 1234L, "D:\\out") == "PACKAGED 4 file(s), 1234 B into D:\\out",
                        "S7 - design:385, composed for Package.cs:178");
        checks += Check(StageText.S8("IntroVideo") ==
                        "Verify: PASS - nothing to verify for 'IntroVideo'; this project declares no " +
                        "patched target - its row(s) are served live by ct_video.",
                        "S8 is NEW - design:390, the empty-census Verify");
        checks += Check(StageText.PackageRefused("D:\\out") ==
                        "REFUSED: D:\\out already holds files. Name a folder that does not exist yet - a " +
                        "package is built from nothing, so no leftover of a previous run can be shipped by " +
                        "accident.",
                        "the Package refusal is composed for Package.cs:77");
        checks += Check(StageText.R25() == "Lifecycle: select a ContentMods project.", "R25 - design:365");
        checks += Check(StageText.R26("Bake") == "Lifecycle: busy running Bake.", "R26 - design:366");
        checks += Check(StageText.R27() ==
                        "Lifecycle: selected project is unavailable; refresh the project list.",
                        "R27 - design:367");
        checks += Check(StageText.R28("Verify", "patched copies", Freshness.Never) ==
                        "Lifecycle: Verify blocked; patched copies is never." &&
                        StageText.R28("Verify", "patched copies", Freshness.Stale) ==
                        "Lifecycle: Verify blocked; patched copies is stale." &&
                        StageText.R28("Package", "the payload", Freshness.Fresh) ==
                        "Lifecycle: Package blocked; the payload is fresh.",
                        "R28 - design:368, and the three freshness words are the enum's own");
        checks += Check(StageText.R29("morgott.demo", "'ct_route7 apply Demo'.") ==
                        "'morgott.demo' failed to bake earlier in this session - not baking it again. Fix " +
                        "the lines it printed, then 'ct_route7 apply Demo'.",
                        "R29 - design:369, composed for Route7.cs:130, RetryHint passed in from :158");
        checks += Check(StageText.R30("Replace_Rifle") == "Verify: VOID - restart required for 'Replace_Rifle'.",
                        "R30 - design:370");
        checks += Check(StageText.R31("Bake") == "Lifecycle: Bake cancelled; later stages were not run.",
                        "R31 - design:371");
        checks += Check(StageText.R32("Bake") == "Lifecycle: project changed during Bake; validate again.",
                        "R32 - design:372");
        checks += Check(StageText.R33("Frobnicate") == "Lifecycle: unknown stage 'Frobnicate'.",
                        "R33 - design:373");
        checks += Check(StageText.R34() ==
                        "Lifecycle: refused a write outside the mod-manager apply path or author output.",
                        "R34 - design:374");
        checks += Check(StageText.R35(2) ==
                        "NOT APPLIED: patching the shipped bundle(s) reported 2 failure(s), named in the " +
                        "P0/REFUSED line(s) above; nothing was installed and no copy was marked current.",
                        "R35 - design:375, composed for Route7.cs:348");
        checks += Check(StageText.R36() == "Lifecycle: Apply blocked while legacy disk patching is active.",
                        "R36 - design:376");
        checks += Check(StageText.R37("D:\\x\\Dist") ==
                        "ct_project: 'D:\\x\\Dist' is already being written by another run - nothing was " +
                        "baked. Wait for it to finish, then bake again.",
                        "R37 - design:377, produced by ProjectBake.cs:104 and Route7.cs:335 when the " +
                        "output claim is contended");
        checks += Check(StageText.R38("a.bundle") ==
                        "ct_project: 'a.bundle' is being served to the game right now, so it was not " +
                        "rewritten - restart the game and bake again.",
                        "R38 - design:378, produced by ProjectBake.cs:1992 (LiveReader) for a copy this " +
                        "mod is serving right now");
        checks += Check(StageText.ValidateFailed("the manifest is not JSON") ==
                        "Validate: FAIL - the manifest is not JSON" &&
                        StageText.VerifyFailed("the copy vanished") == "Verify: FAIL - the copy vanished",
                        "the backend-failure fallbacks are one line and one reason - design:387-389");
        checks += Check(StageText.Queued("Bake") == "Queued: Bake" &&
                        StageText.Running("Bake") == "Running: Bake" &&
                        StageText.CancelRequested("Bake") == "Cancel requested; waiting for Bake to stop." &&
                        StageText.CancelUnavailable("Bake") == "Cancel unavailable during Bake." &&
                        StageText.CancelledAfter("Bake") ==
                            "Lifecycle: cancelled after Bake; later stages were not run.",
                        "the transient Message strings are never terminal verdicts - design:390-393");
        checks += Check(StageText.Idle == "\u2014" && StageText.Ready == "Ready.",
                        "the idle row placeholder and the global ready line - design:394");

        // ---- G3 Tail, frozen exactly as ModelDoctor.cs:745 wrote it.
        checks += Check(StageResult.Tail(null, 1) == "" && StageResult.Tail("", 3) == "",
                        "no log is no tail, never a null the panel would print");
        checks += Check(StageResult.Tail("a" + nl + "b", 5) == "a" + nl + "b",
                        "asking for more lines than there are returns all of them");
        checks += Check(StageResult.Tail("a\nb\nc", 3) == "a" + nl + "b" + nl + "c",
                        "exactly the log, at the limit");
        checks += Check(StageResult.Tail("a\nb\nc", 2) == "b" + nl + "c", "over the limit keeps the LAST lines");
        checks += Check(StageResult.Tail("a\r\nb\r\nc", 2) == "b" + nl + "c",
                        "CRLF and LF are the same log - the split normalises first");
        checks += Check(StageResult.Tail("a\nb\n", 1) == "b",
                        "R11: AppendLine's empty tail is dropped BEFORE the count, so Tail(log,1) is never blank");
        checks += Check(StageResult.Tail("a\nb\n\n\n", 2) == "a" + nl + "b",
                        "several trailing blanks are dropped too");
        checks += Check(StageResult.Tail("a\n\nb", 3) == "a" + nl + "b",
                        "a blank line INSIDE the window is skipped, not printed");
        string wide = new string('x', 5000);
        checks += Check(StageResult.Tail(wide, 1) == wide, "one very long line comes back whole");

        checks += OneSwap();
        checks += KeyCapture();
        checks += Ordering();
        checks += Ownership();
        checks += Admission();
        checks += Cancelling();
        checks += Sequencing();
        checks += Sections();
        checks += Validating();

        return "LIFECYCLE PASS, " + checks + " check(s) - carrier arms, verdict wording, frozen Tail, " +
               "one file swap, the pre-import receipt, the publication ordering, one output owner, " +
               "the admission table, the cancel contract, the Run all chain, the bounded seam sections, " +
               "the Validate producer";
    }

    /// <summary>G6, the claim half - design:377 (R37) and §5's "fail fast, in the producer". One owner per
    /// CANONICAL output directory, so the console verb (ContentToolMain.cs:480) and the mod-manager checkbox
    /// (Route7.cs:341) are refused by the same set the panel is; none of them asks the panel.
    ///
    /// THE PAIR IS ATOMIC (plan finding 10). A run takes the patched dir AND its own Dist together: taking
    /// them one at a time lets two runs each hold one and refuse each other forever, and nothing here ever
    /// waits, retries or steals, so that deadlock would be permanent.</summary>
    private static int Ownership()
    {
        int checks = 0;
        string patched = "C:\\pd\\ContentTool\\Patched\\aabbccdd\\morgott.demo",
               dist = "C:\\proj\\Demo\\Dist",
               other = "C:\\proj\\Other\\Dist";
        string[] mine = { patched, dist };
        string refusal;

        checks += Check(OutputClaim.Take(mine, out refusal) && refusal == null &&
                        OutputClaim.Held(patched) && OutputClaim.Held(dist),
                        "a free pair is taken whole");
        checks += Check(!OutputClaim.Take(new[] { patched }, out refusal) &&
                        refusal == StageText.R37(patched),
                        "a second producer for a directory in flight is refused IMMEDIATELY, with R37");

        // The atomic half: the SECOND directory is the contended one, so a non-atomic implementation would
        // leave `other` held by a run that never started and nobody would ever release it.
        checks += Check(!OutputClaim.Take(new[] { other, dist }, out refusal) && !OutputClaim.Held(other),
                        "a pair that contends on its SECOND directory takes NEITHER - no partial acquisition");

        // Canonical, and case-blind: Route7.cs:287 already compares these paths case-insensitively, and a
        // trailing separator or a forward slash is the same folder on Windows.
        checks += Check(!OutputClaim.Take(new[] { "c:/proj/Demo/Dist\\" }, out refusal),
                        "the claim is canonical - case, separator and trailing slash name one directory");

        OutputClaim.Release(mine);
        checks += Check(!OutputClaim.Held(patched) && !OutputClaim.Held(dist) &&
                        OutputClaim.Take(mine, out refusal),
                        "Release frees the whole pair and the next run takes it");
        OutputClaim.Release(mine);
        OutputClaim.Release(mine);
        checks += Check(OutputClaim.Take(mine, out refusal), "Release is idempotent - a double release is not a leak");
        OutputClaim.Release(mine);
        return checks;
    }

    /// <summary>G6, the admission table - design:194-:200 row by row, plus the one freshness observation
    /// both `Route7.ApplyProject` and this reducer read.
    ///
    /// THE TWO ARMS THAT CATCH THE DESIGN'S OWN TRAP: Apply is NEVER R28 for a stale bake (ApplyProject
    /// re-bakes it itself, Route7.cs:311-:351), and Verify IS refused for absent copies. Everything else
    /// here is the governing rule - "a stage that can regenerate its own input is never refused for missing
    /// evidence" - stated once per row.
    ///
    /// The reducer is FILESYSTEM-FREE by construction (plan finding 11): the PatchCache observation is taken
    /// by the caller and passed IN, which is why this gate can link it at all.</summary>
    private static int Admission()
    {
        int checks = 0;
        string[] stages = { "Validate", "Bake", "Apply", "Verify", "Package" };

        // A selected, resolvable project, nothing running, nothing wrong: every stage is admitted. This is
        // also the ACTIVATION row - `Disabled` is reported by Validate as a field and blocks no stage - and
        // the AFTER A RESTART column, where there is no session receipt at all (W15).
        LifecycleState.Admission ok = new LifecycleState.Admission
        { Selection = LifecycleState.Selection.Ok, ProjectId = "morgott.demo", Copies = Freshness.Fresh };
        bool all = true;
        foreach (string s in stages) all &= LifecycleState.Admit(s, ok) == null;
        checks += Check(all, "a resolvable selection admits every stage - activation never blocks, and a " +
                             "restart with no session receipt admits all five (design:196-:200)");
        checks += Check(LifecycleState.Admit("All", ok) == null,
                        "'All' is admitted here and re-asked per stage as the sequencer reaches it (design:187)");

        // Bake does not read Validate's receipts - it loads and validates the manifest itself.
        LifecycleState.Admission never = new LifecycleState.Admission
        { Selection = LifecycleState.Selection.Ok, ProjectId = "morgott.demo", Copies = Freshness.Never };
        checks += Check(LifecycleState.Admit("Validate", never) == null &&
                        LifecycleState.Admit("Bake", never) == null,
                        "a 'never' Validate is not R28 for Bake - design:197");
        checks += Check(LifecycleState.Admit("Apply", never) == null &&
                        LifecycleState.Admit("Apply", new LifecycleState.Admission
                        { Selection = LifecycleState.Selection.Ok, Copies = Freshness.Stale }) == null,
                        "APPLY IS NEVER R28 FOR A STALE BAKE - the ApplyProject fallback owns it " +
                        "(design:198, Route7.cs:311-:351)");
        checks += Check(LifecycleState.Admit("Verify", never) ==
                        StageText.R28("Verify", "patched copies", Freshness.Never) &&
                        LifecycleState.Admit("Verify", new LifecycleState.Admission
                        { Selection = LifecycleState.Selection.Ok, Copies = Freshness.Stale }) ==
                        StageText.R28("Verify", "patched copies", Freshness.Stale),
                        "VERIFY IS REFUSED for absent or stale copies - design:199, it reads them");
        checks += Check(LifecycleState.Admit("Package", never) == null,
                        "Package's payload and empty-destination refusals are Package.Run's alone " +
                        "(Package.cs:78) - never re-checked here (design:200)");

        // Selection, and the three ways it fails: nothing selected, a folder that is gone, a name two
        // projects answer to. The reducer never touches the filesystem, so the caller resolves it to one
        // of three answers and this maps them.
        checks += Check(LifecycleState.Admit("Bake", new LifecycleState.Admission()) == StageText.R25(),
                        "no selection is R25, before anything else is asked");
        checks += Check(LifecycleState.Admit("Bake", new LifecycleState.Admission
                        { Selection = LifecycleState.Selection.Unavailable }) == StageText.R27(),
                        "a deleted, moved or ambiguous project is R27 - refresh the list");
        checks += Check(LifecycleState.Admit("Bake", new LifecycleState.Admission
                        { Selection = LifecycleState.Selection.Ok, RunningStage = "Bake" }) ==
                        StageText.R26("Bake"),
                        "a stage already running is R26, naming the stage that holds the seam");
        checks += Check(LifecycleState.Admit("Ship", ok) == StageText.R33("Ship"),
                        "the accepted tokens are exactly the five stages and All - anything else is R33");

        // Apply's own three, all of them producer facts the caller supplies.
        checks += Check(LifecycleState.Admit("Apply", new LifecycleState.Admission
                        { Selection = LifecycleState.Selection.Ok, ProjectId = "morgott.demo",
                          RetryHint = "'ct_route7 apply Demo'." }) ==
                        StageText.R29("morgott.demo", "'ct_route7 apply Demo'."),
                        "Route7's session Failed set suppresses Apply through R29 - the hint comes from " +
                        "Route7.RetryHint, the only thing that knows which argument resolves back");
        checks += Check(LifecycleState.Admit("Apply", new LifecycleState.Admission
                        { Selection = LifecycleState.Selection.Ok, LegacyDiskActive = true }) == StageText.R36() &&
                        LifecycleState.Admit("Apply", new LifecycleState.Admission
                        { Selection = LifecycleState.Selection.Ok, WriteOutsideRoots = true }) == StageText.R34(),
                        "legacy on-disk patching is R36 and a write outside the apply path or author " +
                        "output is R34 - design:198");
        // THE ORDER IS THE POINT, not just the two sentences: an installation still carrying an older
        // ContentTool's on-disk edit is answered BEFORE anything about where this apply would write, and
        // both outrank the retry hint - the author is told the oldest blocking fact first.
        checks += Check(LifecycleState.Admit("Apply", new LifecycleState.Admission
                        { Selection = LifecycleState.Selection.Ok, LegacyDiskActive = true,
                          WriteOutsideRoots = true, ProjectId = "morgott.demo",
                          RetryHint = "'ct_route7 apply Demo'." }) == StageText.R36(),
                        "R36 outranks R34 and R29 when all three hold");
        checks += Check(LifecycleState.Admit("Apply", new LifecycleState.Admission
                        { Selection = LifecycleState.Selection.Ok, WriteOutsideRoots = true,
                          ProjectId = "morgott.demo", RetryHint = "'ct_route7 apply Demo'." }) ==
                        StageText.R34(),
                        "R34 outranks R29 - a write outside the roots is refused before a retry is offered");
        checks += Check(LifecycleState.Admit("Apply", new LifecycleState.Admission
                        { Selection = LifecycleState.Selection.Ok, ProjectId = "morgott.demo" }) == null,
                        "neither field set admits Apply - R36 and R34 are facts the caller measured, " +
                        "never a default");

        // ---- The ONE freshness observation. Route7.cs:308-:310 computes `fresh && Directory.Exists(patched)`
        // and then clears it for every declared copy that is absent; `HaveAll` IS that expression and
        // ApplyProject now asks it here, so the panel and the checkbox cannot drift by a single term.
        // `gone` DECLARES a bundle on purpose: with an empty census there would be nothing to verify and
        // the answer would be `fresh` (the video-only arm below), which is not what this arm is about.
        FreshnessObservation gone = new FreshnessObservation("k", false, false, new[] { "a.bundle" },
                                                            new[] { "a.bundle" });
        FreshnessObservation wrongKey = new FreshnessObservation("k", false, true, new[] { "a.bundle" }, new string[0]);
        FreshnessObservation missing = new FreshnessObservation("k", true, true, new[] { "a.bundle" }, new[] { "a.bundle" });
        FreshnessObservation partly = new FreshnessObservation("k", true, true, new[] { "a.bundle", "b.bundle" },
                                                              new[] { "b.bundle" });
        FreshnessObservation good = new FreshnessObservation("k", true, true, new[] { "a.bundle" }, new string[0]);
        checks += Check(LifecycleState.Fresh(gone) == Freshness.Never && !gone.HaveAll,
                        "no cache directory is 'never' - there is no receipt to be stale");
        checks += Check(LifecycleState.Fresh(wrongKey) == Freshness.Stale && !wrongKey.HaveAll,
                        "a cache directory whose key does not match - or that has none at all - is STALE, " +
                        "not never (PatchCache.cs:84)");
        checks += Check(LifecycleState.Fresh(missing) == Freshness.Never && !missing.HaveAll,
                        "EVERY declared copy absent is 'never', not stale - design:199, and it is what " +
                        "Verify's R28 sentence tells the author to do next");
        checks += Check(LifecycleState.Fresh(partly) == Freshness.Stale && !partly.HaveAll,
                        "SOME copies absent is stale - the census is the other half of the answer, " +
                        "Fresh() compares key text only");
        // A VIDEO-ONLY PROJECT HAS NOTHING TO VERIFY, and it is never 'never'. Route7.cs:157/:163 leave
        // `wantReplace` false, so ApplyProject is never called, no key is ever written and no patched dir
        // ever appears: `never` here meant Verify was R28'd on every launch, permanently, over evidence
        // that cannot exist. Both shapes - no cache dir at all, and a stale key beside an empty census.
        FreshnessObservation videoOnly = new FreshnessObservation(null, false, false, new string[0], new string[0]);
        checks += Check(LifecycleState.Fresh(videoOnly) == Freshness.Fresh &&
                        LifecycleState.Fresh(new FreshnessObservation("k", false, true, new string[0],
                                                                     new string[0])) == Freshness.Fresh,
                        "a project that declares no patched target is FRESH - there is nothing to verify, " +
                        "so it can be neither never nor stale (design:390, S8)");
        checks += Check(LifecycleState.Admit("Verify", new LifecycleState.Admission
                        { Selection = LifecycleState.Selection.Ok, ProjectId = "morgott.introvideo",
                          Copies = LifecycleState.Fresh(videoOnly) }) == null,
                        "and Verify is ADMITTED for it - it reports S8 instead of refusing forever");
        checks += Check(LifecycleState.Fresh(good) == Freshness.Fresh && good.HaveAll,
                        "receipt matches and every declared copy is there - this is Route7's `haveAll`");
        return checks;
    }

    /// <summary>G4 - A CANCEL THAT NEVER LIES. The bookkeeping is pure and lives on the reducer, so the arms
    /// below drive the very object `LifecycleJob` holds; the job is Unity-bound and its thread split is
    /// proven in game (Task 8, W12/W13), but WHAT IT REMEMBERS is proven here.
    ///
    /// The four facts, each an arm: one terminal result per run; busy is retained until the worker actually
    /// finishes, not until Cancel is pressed; a cancel that arrives after the work succeeded does NOT
    /// relabel it; and a late completion from an older run can never overwrite a newer one's state.</summary>
    private static int Cancelling()
    {
        int checks = 0;
        LifecycleRun run = new LifecycleRun();

        checks += Check(!run.Latest.Busy && run.Latest.RunId == 0 && run.Latest.Stage == null,
                        "a fresh run state is idle - no run id, no stage, nothing to report");

        // ---- CANCEL BEFORE DISPATCH. The id is handed out, the flag is up before the worker's first
        // checkpoint, and BUSY IS STILL TRUE: the worker has not stopped yet and saying otherwise would
        // let the panel start the next stage over a run that is still touching files.
        long first = run.Begin("Bake");
        checks += Check(first != 0 && run.Latest.Busy && run.Latest.Stage == "Bake" &&
                        !run.Latest.CancelRequested,
                        "Begin hands out a run id and marks the seam busy");
        checks += Check(run.Begin("Apply") == 0 && run.Latest.Stage == "Bake",
                        "a second stage is REFUSED while one is in flight - Admit's R26 is advisory, this " +
                        "is the authoritative one, and it cannot race");
        run.Cancel();
        run.Cancel();
        checks += Check(run.Latest.CancelRequested && !run.Latest.CancelAcknowledged && run.Latest.Busy,
                        "a repeated Cancel is one request, unacknowledged until the producer says so, and " +
                        "the seam stays BUSY - a cancel is not a completion");
        checks += Check(run.Begin("Apply") == 0,
                        "and no next stage is dispatched while the cancelled run is still running");
        checks += Check(run.Complete(first, "stopped", BakeDisposition.Cancelled) &&
                        !run.Latest.Busy && run.Latest.CancelAcknowledged &&
                        run.Latest.Result == "stopped" && run.Latest.How == BakeDisposition.Cancelled,
                        "the producer's own Cancelled disposition is what acknowledges the cancel, and only " +
                        "then is the seam free");

        // ---- CANCEL AFTER SUCCESS. B5 is non-cancellable, so a cancel raised while it ran is answered by
        // a SUCCESSFUL result. The request is remembered - it happened - but it is NOT acknowledged, or the
        // panel would report "cancelled" over output that was published.
        long second = run.Begin("Bake");
        run.Cancel();
        checks += Check(run.Complete(second, "ct_project: ALL PASS", BakeDisposition.Success) &&
                        run.Latest.How == BakeDisposition.Success &&
                        run.Latest.CancelRequested && !run.Latest.CancelAcknowledged,
                        "a cancel that lost the race to a completed publication does not relabel it - the " +
                        "request is remembered, the acknowledgement is not invented");

        // ---- NO LATE RESULT OVERWRITES A NEWER RUN. The stale worker's completion is dropped whole:
        // result, disposition and progress. This is the run handle the seam's poll protocol rests on.
        long third = run.Begin("Verify");
        checks += Check(third != second && !run.Complete(second, "the old one finished", BakeDisposition.Failed) &&
                        run.Latest.Busy && run.Latest.Stage == "Verify" && run.Latest.Result == null,
                        "a completion carrying an OLD run id is dropped - it can never overwrite the run " +
                        "that is actually in flight");
        run.Progress(second, new SlimProgress("stale", 3, 4, "from the old run"));
        checks += Check(run.Latest.Progress == null,
                        "and neither can its progress");
        run.Progress(third, new SlimProgress("reading back", 1, 2, "px_equipment_assets_all.bundle"));
        checks += Check(run.Latest.Progress != null && run.Latest.Progress.Stage == "reading back" &&
                        run.Latest.Progress.Done == 1 && run.Latest.Progress.Total == 2,
                        "the running run's own progress lands, with a KNOWN denominator - a phase with no " +
                        "count publishes the phase, never an invented percentage");
        checks += Check(run.Complete(third, "Verify: PASS", BakeDisposition.Success) &&
                        !run.Latest.CancelRequested,
                        "a new run starts with a clean cancel flag - the previous run's request is not " +
                        "inherited");

        // ---- ONE TERMINAL RESULT. A second completion of the same run is refused too: the producer that
        // already reported is the one the panel shows.
        checks += Check(!run.Complete(third, "Verify: FAIL", BakeDisposition.Failed) &&
                        run.Latest.Result == "Verify: PASS" && run.Latest.How == BakeDisposition.Success,
                        "one terminal result per run - a second completion cannot rewrite the first");

        // ---- A CANCEL WITH NOTHING RUNNING IS SILENCE, not a flag the next run inherits.
        run.Cancel();
        long fourth = run.Begin("Package");
        checks += Check(!run.Latest.CancelRequested && run.Latest.Result == null && fourth != third,
                        "Cancel with no run in flight changes nothing, and the next Begin starts clean");
        return checks;
    }

    /// <summary>G7 - the B5 PUBLICATION ORDERING, driven through the production file.
    ///
    /// WHY THIS GATE EXISTS AT ALL (plan review, blocker 2). B5 as first planned lived only inside
    /// `ProjectBake` and `LifecycleJob`, both of which carry UnityEngine and AssetsTools and can never be
    /// linked here - so a G7 that re-implemented invalidate/swap/key would stay green while the real bake
    /// stamped the key first. `AtomicFile.Publish` only covers ONE file and says nothing about the order.
    /// The ordering therefore lives in `src\Bake\Publication.cs`, which IS what these arms call.
    ///
    /// THE FAULTS ARE REAL, NOT INJECTED. A key file held open with FileShare.None makes File.Delete throw;
    /// a destination held the same way makes File.Replace throw; a key path under a directory that does not
    /// exist makes the key write throw. No fault delegate, so nothing here can pass over a production path
    /// the test replaced with its own.
    ///
    /// Every arm asserts ACTUAL BYTES, not a return code: the whole point of the ordering is what is on disk
    /// when it stops halfway.</summary>
    private static int Ordering()
    {
        int checks = 0;
        string dir = Path.Combine(Path.GetTempPath(), "ct_order_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            byte[] fresh = { 7, 7, 7 }, old = { 1, 1 };
            string a = Path.Combine(dir, "a.bundle"), b = Path.Combine(dir, "b.bundle");
            string key = Path.Combine(dir, "ct-cache.key");
            string message;

            // ---- the happy path: old key gone, both copies published, new key LAST.
            File.WriteAllBytes(a, old); File.WriteAllBytes(b, old);
            File.WriteAllText(key, "OLDKEY");
            checks += Check(Publication.Run(Pair(dir, fresh, a, b), key, "NEWKEY", null, null, out message) ==
                            PublishOutcome.Published && message == null &&
                            Same(File.ReadAllBytes(a), fresh) && Same(File.ReadAllBytes(b), fresh) &&
                            File.ReadAllText(key) == "NEWKEY" && Temps(dir) == 0,
                            "a clean publication replaces every copy, writes the new key and leaves no temp");

            // ---- INVALIDATION FIRST, and a failed one publishes NOTHING. The old key is held open, so
            // File.Delete throws before a single swap.
            File.WriteAllBytes(a, old); File.WriteAllBytes(b, old);
            File.WriteAllText(key, "OLDKEY");
            PublishOutcome how;
            using (new FileStream(key, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                how = Publication.Run(Pair(dir, fresh, a, b), key, "NEWKEY", null, null, out message);
            checks += Check(how == PublishOutcome.Failed && message != null &&
                            Same(File.ReadAllBytes(a), old) && Same(File.ReadAllBytes(b), old) &&
                            File.ReadAllText(key) == "OLDKEY" && Temps(dir) == 0,
                            "key invalidation fails -> nothing is published, the previous outputs are intact");

            // ---- A FAILURE BETWEEN TWO REPLACEMENTS. The completed file is complete, the other one is
            // untouched, and the key is ABSENT - which is what forbids Apply until a repair bake.
            File.WriteAllBytes(a, old); File.WriteAllBytes(b, old);
            File.WriteAllText(key, "OLDKEY");
            using (new FileStream(b, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                how = Publication.Run(Pair(dir, fresh, a, b), key, "NEWKEY", null, null, out message);
            checks += Check(how == PublishOutcome.Failed && message != null &&
                            Same(File.ReadAllBytes(a), fresh) && Same(File.ReadAllBytes(b), old) &&
                            !File.Exists(key) && Temps(dir) == 0,
                            "a failure between two replacements leaves the finished copy complete, the key " +
                            "absent and no orphaned temp");

            // ---- THE KEY IS WRITTEN LAST, so a key write that throws still leaves both copies published.
            File.WriteAllBytes(a, old); File.WriteAllBytes(b, old);
            how = Publication.Run(Pair(dir, fresh, a, b),
                                  Path.Combine(Path.Combine(dir, "no-such-dir"), "ct-cache.key"),
                                  "NEWKEY", null, null, out message);
            checks += Check(how == PublishOutcome.Failed && message != null &&
                            Same(File.ReadAllBytes(a), fresh) && Same(File.ReadAllBytes(b), fresh) &&
                            Temps(dir) == 0,
                            "the key write fails LAST - the copies are published and the receipt is absent");

            // ---- CANCEL AT B4, the last cancellable instant: temps deleted, previous outputs BYTE-IDENTICAL.
            File.WriteAllBytes(a, old); File.WriteAllBytes(b, old);
            File.WriteAllText(key, "OLDKEY");
            checks += Check(Publication.Run(Pair(dir, fresh, a, b), key, "NEWKEY", null,
                                            delegate { return true; }, out message) ==
                            PublishOutcome.Cancelled &&
                            Same(File.ReadAllBytes(a), old) && Same(File.ReadAllBytes(b), old) &&
                            File.ReadAllText(key) == "OLDKEY" && Temps(dir) == 0,
                            "a cancel at B4 publishes nothing, deletes the temps and leaves the previous " +
                            "outputs byte-identical");

            // ---- CANCEL INSIDE B5 IS IGNORED. The flag flips while the run is already past its one check;
            // publication finishes and reports completion, because a half-published set is unclassifiable.
            File.WriteAllBytes(a, old); File.WriteAllBytes(b, old);
            bool late = false;
            checks += Check(Publication.Run(Pair(dir, fresh, a, b), key, "NEWKEY",
                                            delegate(string dest) { late = true; return null; },
                                            delegate { return late; }, out message) ==
                            PublishOutcome.Published &&
                            Same(File.ReadAllBytes(a), fresh) && Same(File.ReadAllBytes(b), fresh) &&
                            File.ReadAllText(key) == "NEWKEY",
                            "a cancel raised INSIDE B5 does not interrupt it - publication completes");

            // ---- R38 AT THE BOUNDARY. A copy a live reader is serving is refused before anything moves.
            File.WriteAllBytes(a, old); File.WriteAllBytes(b, old);
            File.WriteAllText(key, "OLDKEY");
            string r38 = StageText.R38(b);
            checks += Check(Publication.Run(Pair(dir, fresh, a, b), key, "NEWKEY",
                                            delegate(string dest) { return dest == b ? r38 : null; },
                                            null, out message) == PublishOutcome.Refused &&
                            message == r38 &&
                            Same(File.ReadAllBytes(a), old) && Same(File.ReadAllBytes(b), old) &&
                            File.ReadAllText(key) == "OLDKEY" && Temps(dir) == 0,
                            "a claimed or resident destination refuses the WHOLE publication with R38, " +
                            "before the first swap - the live reader keeps reading what it had");

            // ---- NO KEY AT ALL (the project's own Dist, and a bake nobody vouched for): the copies are
            // published and the stale receipt is invalidated anyway, so nothing can read them as current.
            File.WriteAllBytes(a, old); File.WriteAllBytes(b, old);
            File.WriteAllText(key, "OLDKEY");
            checks += Check(Publication.Run(Pair(dir, fresh, a, b), key, null, null, null, out message) ==
                            PublishOutcome.Published && Same(File.ReadAllBytes(a), fresh) &&
                            !File.Exists(key),
                            "a publication with no key to write still INVALIDATES the old one - a failed " +
                            "bake's copies can never be read as the receipt's");

            // ---- THE EXIT B5 NEVER SEES. A BundleBaker.Write or a read-back gate that THROWS walks out
            // of Patch with every streamed temp - a full-size clone each - still in PatchedDir, under a
            // GUID name nothing prunes. The producer sweeps its own ledger with the SAME call the
            // refusals above make, which is why Discard is internal; this arm drives that call in the
            // shape ProjectBake's catch uses it.
            File.WriteAllBytes(a, fresh); File.WriteAllBytes(b, fresh);
            IList<KeyValuePair<string, string>> streamed = Pair(dir, old, a, b);
            bool rethrew = false;
            try
            {
                try { throw new InvalidOperationException("BundleBaker.Write"); }
                catch (Exception) { Publication.Discard(streamed, 0); throw; }
            }
            catch (InvalidOperationException) { rethrew = true; }
            checks += Check(rethrew && Temps(dir) == 0 &&
                            Same(File.ReadAllBytes(a), fresh) && Same(File.ReadAllBytes(b), fresh),
                            "a throw before B5 sweeps every temp the run streamed, rethrows the " +
                            "producer's own exception, and leaves the previous copies byte-identical");

            // ---- THE HOLDER'S BYTES ARE UNTOUCHED BY A COMPETING ADMISSION, and the claim survives it.
            string[] owned = { dir };
            string refusal;
            OutputClaim.Take(owned, out refusal);
            try
            {
                string other;
                checks += Check(!OutputClaim.Take(owned, out other) && other == StageText.R37(
                                    OutputClaim.Canonical(dir)) &&
                                Same(File.ReadAllBytes(a), fresh),
                                "a competing producer is refused immediately and writes nothing - the " +
                                "holder's bytes are untouched");
            }
            finally { OutputClaim.Release(owned); }
            checks += Check(!OutputClaim.Held(dir),
                            "the claim is released in a finally - an exception inside the run cannot leak it");
        }
        finally { try { Directory.Delete(dir, true); } catch (Exception) { } }
        return checks;
    }

    /// <summary>
    /// G8 - the Validate producer's PURE half, on real files. §4.1 and nothing more: the declaration
    /// structure plus activation eligibility. The one Unity-bound piece is `ModRoster.Build`, which is why
    /// the producer takes the roster as a DICTIONARY - main captures it, the worker validates - and that
    /// split is the whole reason every arm below drives production code rather than a stub.
    ///
    /// DISABLED IS NOT MALFORMED (design:103): eligibility is its own field and never becomes the verdict,
    /// so a project the player switched off still reports Validate PASS.
    /// </summary>
    private static int Validating()
    {
        int checks = 0;
        string root = Path.Combine(Path.GetTempPath(), "ct_valid_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "Content"));
        try
        {
            string name = Path.GetFileName(root);
            string manifest = Path.Combine(root, ContentMods.Manifest);
            File.WriteAllText(manifest,
                "{ \"id\": \"m\", \"bundle\": \"M.bundle\", \"replace\": [ " +
                "{ \"mesh\": \"torso\", \"bundle\": \"b.bundle\", \"asset\": \"Torso\" } ] }");
            File.WriteAllBytes(Path.Combine(root, "Content", "torso.glb"), new byte[] { 1, 2, 3 });
            string[] shipped = { Path.Combine(root, "Content", "torso.glb") };
            Dictionary<string, bool> off = new Dictionary<string, bool>(StringComparer.Ordinal);
            off[ModGate.Key(root)] = false;

            LifecycleState.StageReport ok = StageValidate.Run(root, manifest, shipped, off);
            checks += Check(ok.Outcome == GateOutcome.Pass && ok.Verdict == StageText.S3(name) &&
                            ok.How == BakeDisposition.Success && ok.Applicable && !ok.RestartRequired,
                            "S3 verbatim, and Validate PASS is the producer's own line");
            checks += Check(ok.Eligibility == ModGate.Why(ModVerdict.Disabled),
                            "a DISABLED project still validates - eligibility is a field, never the verdict " +
                            "(design:103)");

            LifecycleState.StageReport none = StageValidate.Run(root, manifest, shipped, null);
            checks += Check(none.Outcome == GateOutcome.Pass &&
                            none.Eligibility == ModGate.Why(ModVerdict.NoRoster),
                            "no roster is an eligibility answer too, and still not a Validate failure");

            // E4, Manifest.cs:220 - the same target claimed twice.
            string twice = Path.Combine(root, "twice.json");
            File.WriteAllText(twice,
                "{ \"id\": \"m\", \"bundle\": \"M.bundle\", \"replace\": [ " +
                "{ \"mesh\": \"torso\", \"bundle\": \"b.bundle\", \"asset\": \"Torso\" }, " +
                "{ \"mesh\": \"torso2\", \"bundle\": \"b.bundle\", \"asset\": \"Torso\" } ] }");
            LifecycleState.StageReport dup = StageValidate.Run(root, twice, shipped, off);
            checks += Check(dup.Outcome == GateOutcome.Fail && dup.How == BakeDisposition.Failed &&
                            dup.Verdict.StartsWith("Validate: FAIL - ", StringComparison.Ordinal) &&
                            dup.Verdict.IndexOf("already replaces", StringComparison.Ordinal) > 0,
                            "E4 comes back as ValidateFailed carrying the manifest's own reason, and the " +
                            "throw never escapes the producer");

            LifecycleState.StageReport gone =
                StageValidate.Run(root, Path.Combine(root, "absent.json"), shipped, off);
            checks += Check(gone.Outcome == GateOutcome.Fail &&
                            gone.Verdict.StartsWith("Validate: FAIL - ", StringComparison.Ordinal) &&
                            gone.Verdict.IndexOf(Environment.NewLine, StringComparison.Ordinal) < 0,
                            "a missing ppcontent.json is an IOException answered by the same one-line " +
                            "fallback, not an exception out of the panel");

            // The key must be COMPUTABLE here - the producer calls it and stores nothing, so this is the
            // arm that fails if the four §4.1 calls stop being reachable offline.
            checks += Check(!string.IsNullOrEmpty(PatchCache.Key(root, shipped)),
                            "the pre-import key is computable from the validated declaration");
        }
        finally { try { Directory.Delete(root, true); } catch (Exception) { } }
        return checks;
    }

    /// <summary>
    /// B1 - THE RECEIPT IS THE PRE-IMPORT KEY, and that is what makes a mid-bake save cost a re-bake
    /// instead of serving a stale copy forever.
    ///
    /// `ProjectBake.Bake` captures it before `ContentProject.Load` and hands it down unchanged
    /// (ProjectBake.cs, Patch's `key` parameter); the two production pieces that decide the outcome -
    /// `PatchCache.Key`/`Fresh` and the ordering in `Publication.Run` - are both linked here, so the
    /// composed claim is measured on real files rather than argued about. What stays out of reach offline
    /// is only WHERE `Bake` calls it, which the compiler now enforces: `Patch` has no way to compute one.
    ///
    /// The arm's own falsification is the FIRST check: a key that did not move when the source changed
    /// would make every other line here pass for no reason.
    /// </summary>
    private static int KeyCapture()
    {
        int checks = 0;
        string root = Path.Combine(Path.GetTempPath(), "ct_key_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "Content"));
        try
        {
            string source = Path.Combine(root, "Content", "swatch.png");
            string[] noShipped = new string[0];
            File.WriteAllText(Path.Combine(root, "ppcontent.json"),
                              "{ \"id\": \"morgott.demo\", \"bundle\": \"demo.bundle\" }");
            File.WriteAllBytes(source, new byte[] { 1 });

            // ---- B1, before the import reads a byte.
            string before = PatchCache.Key(root, noShipped);
            // ---- the author saves that texture WHILE Load is importing it. The copies this bake
            // publishes carry the pixels Load already read; this file no longer holds them.
            File.WriteAllBytes(source, new byte[] { 2, 2 });
            string after = PatchCache.Key(root, noShipped);
            checks += Check(before != after,
                            "a source saved during the import moves the key - which is what makes the " +
                            "CAPTURE ORDER decide whether the receipt tells the truth");

            string dest = Path.Combine(root, "demo.bundle");
            string tmp = dest + "." + Guid.NewGuid().ToString("N") + ".tmp";
            File.WriteAllBytes(tmp, new byte[] { 7, 7 });
            string message;
            List<KeyValuePair<string, string>> work = new List<KeyValuePair<string, string>>
            { new KeyValuePair<string, string>(tmp, dest) };
            checks += Check(Publication.Run(work, PatchCache.KeyPath(root), before, null, null,
                                            out message) == PublishOutcome.Published &&
                            File.ReadAllText(PatchCache.KeyPath(root)).Trim() == before,
                            "B5 writes the key it was HANDED, verbatim - it never recomputes one, so the " +
                            "receipt cannot describe a source this run did not read");
            checks += Check(!PatchCache.Fresh(root, after) && PatchCache.Fresh(root, before),
                            "so the NEXT observation reads STALE and costs one re-bake - the safe " +
                            "direction; a key taken after the import would have stamped the new file's " +
                            "hash over the old file's pixels and read FRESH forever");

            // ---- THE ORDER ITSELF. Everything above measures what the key DOES; where `Bake` takes it
            // was prose, and prose is what let the capture drift below the import once. `Bake` cannot be
            // linked here (Load goes through JsonUtility, an ECall into the player - RefusalCount.cs:20),
            // so the arm reads the SOURCE, the arrangement TargetPathTests uses for the same reason.
            string src = SrcRoot();
            string file = src == null ? null : Path.Combine(src, "Bake", "ProjectBake.cs");
            string text = file != null && File.Exists(file) ? File.ReadAllText(file) : null;
            int taken = text == null ? -1 : text.IndexOf("string cacheKey = CacheKey(projectRoot);",
                                                         StringComparison.Ordinal);
            int imported = text == null ? -1 : text.IndexOf("ContentProject.Load(projectRoot, pump)",
                                                            StringComparison.Ordinal);
            checks += Check(taken >= 0 && imported > taken,
                            "and B1 is taken BEFORE the import in ProjectBake.Bake - the capture order " +
                            "this arm is named for, read off the source -> " + file);
        }
        finally { try { Directory.Delete(root, true); } catch (Exception) { } }
        return checks;
    }

    /// <summary>Two freshly streamed sibling temps for <paramref name="dests"/>, in that order - what B2
    /// hands B5. Written here rather than in the arms so every arm starts from the same shape.</summary>
    private static IList<KeyValuePair<string, string>> Pair(string dir, byte[] bytes, params string[] dests)
    {
        var work = new List<KeyValuePair<string, string>>();
        foreach (string d in dests)
        {
            string tmp = d + "." + Guid.NewGuid().ToString("N") + ".tmp";
            File.WriteAllBytes(tmp, bytes);
            work.Add(new KeyValuePair<string, string>(tmp, d));
        }
        return work;
    }

    private static int Temps(string dir) { return Directory.GetFiles(dir, "*.tmp").Length; }

    /// <summary>G7's swap half - the ONE swap. AtomicFile.Write makes its own temp (AtomicFile.cs:19) and so
    /// cannot publish one a bake already streamed; Publish takes that temp and performs the same two-armed
    /// swap. Real files, System.IO only - the thing under test IS the filesystem call.
    ///
    /// The last two arms are the regression this split could introduce: Write's own cleanup guard covers a
    /// failed open, write or flush, NONE of which ever reach Publish, so moving that guard wholesale would
    /// strand Write's temp on exactly the paths that have one today.
    ///
    /// Named OneSwap, not Publication: <see cref="Morgott.ContentTool.Bake.Publication"/> is a TYPE this
    /// class now calls, and a same-named method in the same scope hides it.</summary>
    private static int OneSwap()
    {
        int checks = 0;
        string dir = Path.Combine(Path.GetTempPath(), "ct_publish_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            byte[] streamed = { 1, 2, 3, 4, 5 };
            byte[] older = { 9, 9 };

            // Over an ABSENT destination - the File.Move arm.
            string dest = Path.Combine(dir, "absent.bundle");
            string tmp = dest + ".streamed.tmp";
            File.WriteAllBytes(tmp, streamed);
            AtomicFile.Publish(tmp, dest);          // void, so it is a STATEMENT, never an expression
            checks += Check(Same(File.ReadAllBytes(dest), streamed) && !File.Exists(tmp),
                            "Publish moves the caller's temp onto a destination that does not exist");

            // Over an EXISTING destination - the File.Replace arm.
            File.WriteAllBytes(dest, older);
            File.WriteAllBytes(tmp, streamed);
            AtomicFile.Publish(tmp, dest);
            checks += Check(Same(File.ReadAllBytes(dest), streamed) && !File.Exists(tmp),
                            "Publish replaces an existing destination and leaves no temp behind");

            // The backup is honoured on the replace arm - the same contract Write already had.
            string backup = Path.Combine(dir, "absent.bundle.bak");
            File.WriteAllBytes(dest, older);
            File.WriteAllBytes(tmp, streamed);
            AtomicFile.Publish(tmp, dest, backup);
            checks += Check(Same(File.ReadAllBytes(dest), streamed) && Same(File.ReadAllBytes(backup), older),
                            "the backupPath keeps the bytes the swap displaced");

            // A temp that is not there is the caller's bug and must be LOUD: publishing nothing over a
            // live file would otherwise look like a successful bake.
            bool threw = false;
            try { AtomicFile.Publish(Path.Combine(dir, "nothing.tmp"), dest); }
            catch (Exception) { threw = true; }
            checks += Check(threw && Same(File.ReadAllBytes(dest), streamed),
                            "publishing an absent temp throws and does not touch the destination");

            // THE RACE. The destination can appear between Publish's own File.Exists and its File.Move -
            // a second writer, or the same bake retried - and the Move then throws over a file that is
            // already there. The temp's bytes must still land.
            string raced = Path.Combine(dir, "raced.bundle");
            string racedTmp = raced + ".streamed.tmp";
            File.WriteAllBytes(racedTmp, streamed);
            File.WriteAllBytes(raced, older);       // pre-created between the temp write and the Publish
            AtomicFile.Publish(racedTmp, raced);
            checks += Check(Same(File.ReadAllBytes(raced), streamed) && !File.Exists(racedTmp),
                            "a destination that appeared after the Exists check still gets the temp's bytes");

            // A swap that THROWS must KEEP the caller's temp: a streamed bundle has no second copy, so
            // deleting it here loses the whole write and leaves the caller nothing to retry from.
            string held = Path.Combine(dir, "held.bundle");
            string heldTmp = held + ".streamed.tmp";
            File.WriteAllBytes(held, older);
            File.WriteAllBytes(heldTmp, streamed);
            bool refusedSwap = false;
            using (new FileStream(held, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                try { AtomicFile.Publish(heldTmp, held); }
                catch (Exception) { refusedSwap = true; }
            }
            checks += Check(refusedSwap && Same(File.ReadAllBytes(heldTmp), streamed),
                            "a swap that throws KEEPS the caller's temp - there is no second copy to retry from");
            File.Delete(heldTmp);                   // kept on purpose above; the Write arm below counts *.tmp

            // A temp in ANOTHER directory is not a swap at all: File.Move across volumes is copy+delete
            // and File.Replace throws outright, so the guard refuses it instead of writing non-atomically.
            bool crossVolume = false;
            try { AtomicFile.Publish(Path.Combine(Path.GetTempPath(), "elsewhere.tmp"), dest); }
            catch (ArgumentException) { crossVolume = true; }
            checks += Check(crossVolume && Same(File.ReadAllBytes(dest), streamed),
                            "a temp that is not a sibling of the destination is refused, not published");

            // Write still writes - it is now two lines over Publish, and MANIFEST/ALIAS/PROJECT-SCAFFOLD
            // all ride on it.
            string written = Path.Combine(dir, "written.bin");
            AtomicFile.Write(written, streamed);
            AtomicFile.Write(written, older);
            checks += Check(Same(File.ReadAllBytes(written), older) &&
                            Directory.GetFiles(dir, "*.tmp").Length == 0,
                            "Write publishes over its own path twice and cleans up after itself");

            // Write's OWN guard, which Publish never sees: the stream is open, the write throws.
            bool wrote = false;
            try { AtomicFile.Write(Path.Combine(dir, "midwrite.bin"), null); }
            catch (Exception) { wrote = true; }
            checks += Check(wrote && Directory.GetFiles(dir, "*.tmp").Length == 0,
                            "a Write that throws mid-write strands no .tmp - Write keeps its own cleanup");
        }
        finally { try { Directory.Delete(dir, true); } catch (Exception) { } }
        return checks;
    }

    private static bool Same(byte[] a, byte[] b)
    {
        // NEVER `a == b`: that compares references and passes on two arrays that differ.
        if (a == null || b == null || a.Length != b.Length) return false;
        for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
        return true;
    }

    /// <summary>G5 - THE `Run all` CHAIN, and the one thing it is forbidden to be: a second copy of the
    /// dependency graph. Design:202-:204 - the Run all column's conditions are FIELDS of `Admission` and ARMS
    /// of `Admit`, and the sequencer only reads them by calling `Admit` as it reaches each stage.
    ///
    /// The arms below drive `Sequence` with a recording dispatcher, so "invocation order and count" and
    /// "first stop position" are measured rather than asserted about text. The rule set (design:278-:283):
    /// stop on FAIL, on a refusal, on an acknowledged cancellation and on a BLOCKING VOID; a non-applicable
    /// row is VOID and does NOT stop the chain; Package is never entered after a stop.</summary>
    private static int Sequencing()
    {
        int checks = 0;
        List<string> ran = new List<string>();

        // Everything passes: five stages, in the displayed order, each dispatched exactly once.
        LifecycleState.Admission ctx = Fresh();
        checks += Check(Drive(ctx, ran, delegate { return Pass("ok"); }) == null &&
                        string.Join(",", ran.ToArray()) == "Validate,Bake,Apply,Verify,Package",
                        "a clean chain runs the five stages in displayed order, once each");

        // Each stage fails in turn: the chain stops AT it, the later ones are never dispatched, and the
        // run's terminal line is the producer's own - never a sentence the sequencer composed.
        string[] stages = { "Validate", "Bake", "Apply", "Verify", "Package" };
        for (int i = 0; i < stages.Length; i++)
        {
            string failAt = stages[i];
            ran.Clear();
            string terminal = Drive(Fresh(), ran, delegate(string s)
            { return s == failAt ? Fail(s + " broke") : Pass("ok"); });
            checks += Check(ran.Count == i + 1 && ran[ran.Count - 1] == failAt && terminal == failAt + " broke",
                            "a FAIL at " + failAt + " stops the chain there, with the producer's own line");
        }

        // The one the design names explicitly (design:280): a Verify that cannot prove itself stops the
        // chain, and PACKAGE IS NOT ENTERED. A green Package under an unproven Verify is the whole point.
        ran.Clear();
        Drive(Fresh(), ran, delegate(string s)
        { return s == "Verify" ? Void("Verify: VOID - nothing proved it", true) : Pass("ok"); });
        checks += Check(ran.Count == 4 && !ran.Contains("Package"),
                        "an absent mandatory proof stays VOID and blocks completion - Package is not entered");

        // ...but a VOID with no applicable gate at all is a reason, not a failure (design:281).
        ran.Clear();
        checks += Check(Drive(Fresh(), ran, delegate(string s)
                        { return s == "Apply" ? Void("Apply: VOID - no non-video target", false) : Pass("ok"); }) == null &&
                        ran.Count == 5,
                        "a non-applicable row is VOID with a reason and does NOT stop the chain");

        // THE S1 BARRIER. Apply PASSES and reports restart-required; Verify is then refused by Admit's own
        // R30 arm - the sequencer never learns what S1 means, it only re-asks Admit.
        ran.Clear();
        LifecycleState.Admission s1 = Fresh();
        string after = Drive(s1, ran, delegate(string s)
        {
            return s == "Apply"
                ? new LifecycleState.StageReport(GateOutcome.Pass, "applied", BakeDisposition.Success, true, true)
                : Pass("ok");
        });
        checks += Check(after == StageText.R30("morgott.demo") && ran.Count == 3 && !ran.Contains("Verify"),
                        "S1 -> R30: an Apply that needs a restart refuses Verify and stops before Package");

        // A PREREQUISITE REFUSAL, straight off Admit - the arm the sequencer reads and nothing re-implements.
        LifecycleState.Admission chained = Fresh();
        chained.InRunAll = true;
        checks += Check(LifecycleState.Admit("Bake", chained) == StageText.R28All("Bake", "Validate"),
                        "inside Run all, Bake is refused until Validate passed - a FIELD of Admission");
        chained.ValidateOutcome = GateOutcome.Pass;
        chained.BakeOutcome = GateOutcome.Fail;
        checks += Check(LifecycleState.Admit("Bake", chained) == null &&
                        LifecycleState.Admit("Apply", chained) == StageText.R28All("Apply", "Bake"),
                        "and Apply is refused after a Bake that FAILED - never after one that merely VOIDed");
        chained.BakeOutcome = GateOutcome.Void;
        chained.ApplyOutcome = GateOutcome.None;
        checks += Check(LifecycleState.Admit("Apply", chained) == null &&
                        LifecycleState.Admit("Verify", chained) == StageText.R28All("Verify", "Apply"),
                        "Verify comes after Apply inside the chain; standalone it asks the copies instead");
        // STANDALONE IS UNTOUCHED by all three - that is why they are guarded by InRunAll.
        LifecycleState.Admission alone = Fresh();
        checks += Check(LifecycleState.Admit("Bake", alone) == null &&
                        LifecycleState.Admit("Apply", alone) == null &&
                        LifecycleState.Admit("Verify", alone) == null,
                        "outside Run all none of the chain arms fire - a standalone stage asks only its own row");

        // CANCELLATION. The producer's Cancelled disposition is what stops it, and the terminal line says
        // later stages were not run - it never claims a rollback.
        ran.Clear();
        checks += Check(Drive(Fresh(), ran, delegate(string s)
                        {
                            return s == "Bake"
                                ? new LifecycleState.StageReport(GateOutcome.Void, "stopped",
                                                                 BakeDisposition.Cancelled, false, true)
                                : Pass("ok");
                        }) == StageText.R31("Bake") && ran.Count == 2,
                        "an acknowledged cancellation stops the chain at that stage, with R31");

        // ADMIT IS ASKED AS THE STAGE IS REACHED, NEVER UP FRONT (design:187): a context whose Verify would
        // be refused at the start still runs Validate, Bake and Apply first.
        ran.Clear();
        LifecycleState.Admission stale = Fresh();
        stale.Copies = Freshness.Never;
        Drive(stale, ran, delegate(string s)
        {
            if (s == "Apply") stale.Copies = Freshness.Fresh;    // the earlier stage's output admits the later
            return Pass("ok");
        });
        checks += Check(ran.Count == 5,
                        "Admit runs per stage as it is reached, so an earlier stage's output admits a later one");

        // EARLIER RECEIPTS ARE NEVER ERASED, and the session block is never cleared by a green stage:
        // Route7.cs:405 stays the only clearing path, so RetryHint survives a passing Validate and Bake.
        ran.Clear();
        LifecycleState.Admission blocked = Fresh();
        blocked.RetryHint = "'ct_route7 apply Demo'.";
        string stop = Drive(blocked, ran, delegate { return Pass("ok"); });
        checks += Check(stop == StageText.R29("morgott.demo", "'ct_route7 apply Demo'.") &&
                        ran.Count == 2 && blocked.RetryHint == "'ct_route7 apply Demo'." &&
                        blocked.ValidateOutcome == GateOutcome.Pass && blocked.BakeOutcome == GateOutcome.Pass,
                        "a session block refuses Apply mid-chain; the earlier receipts stand and nothing " +
                        "clears the block");
        return checks;
    }

    /// <summary>THE ARM THAT STOPS PPCLI FROM SILENTLY EATING A POLL. `Protocol.Clip` truncates a reply at
    /// `MaxOutputLineChars = 2000` (`PPCLI/src/Protocol.cs:56`) and appends " ...(clipped)" mid-token, which
    /// produces JSON that `ConvertFrom-Json` refuses - with no error anywhere near the cause. So the seam
    /// SECTIONS its snapshot and every section bounds itself, and this gate proves both offline, where the
    /// only thing needed is a parser: `Json.Parse` is the one the mod already ships.
    ///
    /// The header is the poll, so it carries no verdict TEXT at all - only each row's `verdictLength`, and
    /// the caller fetches the one row it wants. That is what keeps five maximum-length verdicts from ever
    /// reaching the wire together.</summary>
    private static int Sections()
    {
        int checks = 0;
        LifecycleView view = new LifecycleView
        {
            GameRoot = "D:\\PP-Instance2",
            Root = "D:\\PP-Instance2\\Mods\\DashboardAuthor",
            Id = "morgott.dashboardauthor",
            RunId = 7,
            Busy = true,
            Stage = "Bake",
            BarrierParked = true,
            BarrierRunId = 7,
            ClaimHeld = "C:\\pd\\ContentTool\\Patched\\aabbccdd\\morgott.dashboardauthor",
            Log = "line one\r\nline two",
            S1 = "applied - restart the game",
            S2 = null
        };
        // Five MAXIMUM-length verdicts: the case the header exists to survive.
        string huge = new string('v', 4000);
        foreach (LifecycleView.Row row in view.Rows)
        {
            row.Verdict = huge;
            row.Freshness = Freshness.Stale;
            row.Outcome = GateOutcome.Fail;
            row.Starts = 2;
        }

        string header = view.Section("");
        checks += Check(header.Length < 2000, "the poll header stays under PPCLI's 2000-char clip - " +
                                              "with five 4000-char verdicts loaded");
        Dictionary<string, object> h = Obj(header);
        checks += Check(h != null && (bool)h["ok"] && (string)h["section"] == "",
                        "the header parses as JSON - the whole point of bounding it");
        checks += Check(!header.Contains(huge.Substring(0, 64)),
                        "no verdict TEXT is in the header; the caller asks for the row it wants");
        List<object> rows = (List<object>)h["rows"];
        checks += Check(rows.Count == 5 &&
                        (string)((Dictionary<string, object>)rows[0])["stage"] == "Validate" &&
                        (string)((Dictionary<string, object>)rows[4])["stage"] == "Package",
                        "five rows, in the displayed order the panel draws");
        Dictionary<string, object> first = (Dictionary<string, object>)rows[0];
        checks += Check((double)first["verdictLength"] == 4000d && (string)first["freshness"] == "stale" &&
                        (string)first["outcome"] == "fail" && (double)first["starts"] == 2d,
                        "a row reports its length, freshness, outcome and start count - never its text");
        checks += Check((double)h["runId"] == 7d && (bool)h["busy"] && (string)h["stage"] == "Bake" &&
                        !(bool)h["cancelRequested"] && !(bool)h["cancelAcknowledged"],
                        "the run handle and the cancel flags are in the poll, so a poll can match its run");
        checks += Check((bool)h["barrierParked"] && (double)h["barrierRunId"] == 7d,
                        "W13's barrier observation is a header field - 'parked' is not guessed from a sleep");

        // ---- ONE ROW, VERBATIM, and bounded on its own.
        string bake = view.Section("Bake");
        Dictionary<string, object> b = Obj(bake);
        checks += Check(bake.Length < 2000 && b != null && (bool)b["truncated"] &&
                        (double)b["bytes"] == 4000d,
                        "an over-long verdict is clipped, says so, and reports the FULL length it clipped from");
        checks += Check(((string)b["verdict"]).Length > 0 && huge.StartsWith((string)b["verdict"]),
                        "what does arrive is the producer's own leading text, never a summary of it");

        view.Of("Bake").Verdict = "ct_project: ALL PASS - D:\\out";
        Dictionary<string, object> small = Obj(view.Section("Bake"));
        checks += Check((string)small["verdict"] == "ct_project: ALL PASS - D:\\out" &&
                        !(bool)small["truncated"],
                        "a verdict that fits arrives VERBATIM, backslashes and all");

        // ---- The other two sections, and the refusal.
        Dictionary<string, object> log = Obj(view.Section("log"));
        checks += Check((string)log["log"] == "line one\r\nline two",
                        "the log section round-trips CRLF through the parser unchanged");
        view.Log = huge;
        Dictionary<string, object> big = Obj(view.Section("log"));
        checks += Check(view.Section("log").Length < 2000 && (bool)big["truncated"],
                        "an over-long tail is clipped to fit rather than clipped by PPCLI into broken JSON");
        Dictionary<string, object> s = Obj(view.Section("s1s2"));
        checks += Check((string)s["s1"] == "applied - restart the game" && s["s2"] == null,
                        "S1/S2 have their own section, and an absent S2 is null - not an empty string");

        // ---- THE TWO FIELDS THAT WERE OUTSIDE THE BUDGET. S1/S2 are producer lines with no length bound
        // of their own and the section used to hardcode `truncated:false` over them; `installation` is
        // Apply's, and it sat outside the row's shrink loop entirely. Either one over ~1900 chars arrived
        // clipped by PPCLI mid-token instead - JSON `ConvertFrom-Json` refuses, far from the cause.
        view.S1 = huge; view.S2 = huge;
        Dictionary<string, object> pair = Obj(view.Section("s1s2"));
        checks += Check(view.Section("s1s2").Length < 2000 && pair != null && (bool)pair["truncated"] &&
                        (double)pair["bytes"] == 8000d && ((string)pair["s1"]).Length > 0 &&
                        huge.StartsWith((string)pair["s1"]) && huge.StartsWith((string)pair["s2"]),
                        "s1s2 shrinks BOTH lines to fit, verbatim, and reports the length it clipped from");

        LifecycleView.Row apply = view.Of("Apply");
        apply.Verdict = null;
        apply.Installation = huge;
        Dictionary<string, object> inst = Obj(view.Section("Apply"));
        checks += Check(view.Section("Apply").Length < 2000 && inst != null && (bool)inst["truncated"] &&
                        (double)inst["bytes"] == 4000d && inst["verdict"] == null &&
                        ((string)inst["installation"]).Length > 0 &&
                        huge.StartsWith((string)inst["installation"]),
                        "an over-long installation line shrinks with the verdict, not outside the budget");
        Dictionary<string, object> no = Obj(view.Section("Nonsense"));
        checks += Check(!(bool)no["ok"] && ((string)no["error"]).Length > 0,
                        "an unknown section is a parseable refusal, never an exception across the wire");

        foreach (string section in new[] { "", "Validate", "Bake", "Apply", "Verify", "Package", "log", "s1s2" })
            checks += Check(view.Section(section).Length < 2000 && Obj(view.Section(section)) != null,
                            "every section fits and parses: " + (section == "" ? "<header>" : section));
        return checks;
    }

    private static Dictionary<string, object> Obj(string json)
    {
        return Json.Parse(json, 32) as Dictionary<string, object>;
    }

    private static LifecycleState.Admission Fresh()
    {
        return new LifecycleState.Admission
        {
            Selection = LifecycleState.Selection.Ok,
            ProjectId = "morgott.demo",
            Copies = Freshness.Fresh
        };
    }

    private static LifecycleState.StageReport Pass(string line)
    { return new LifecycleState.StageReport(GateOutcome.Pass, line, BakeDisposition.Success, false, true); }

    private static LifecycleState.StageReport Fail(string line)
    { return new LifecycleState.StageReport(GateOutcome.Fail, line, BakeDisposition.Failed, false, true); }

    private static LifecycleState.StageReport Void(string line, bool applicable)
    { return new LifecycleState.StageReport(GateOutcome.Void, line, BakeDisposition.Success, false, applicable); }

    /// <summary>Drives one whole chain and records what was dispatched. Returns the terminal line when the
    /// chain stopped, or null when all five completed.</summary>
    private static string Drive(LifecycleState.Admission ctx, List<string> ran,
                                Func<string, LifecycleState.StageReport> producer)
    {
        LifecycleState.Sequence seq = new LifecycleState.Sequence();
        ctx.InRunAll = true;
        for (int guard = 0; guard < 16; guard++)
        {
            string stage = seq.Next(ctx);
            if (stage == null) break;
            ran.Add(stage);
            seq.Report(ctx, producer(stage));
        }
        return seq.Stopped ? seq.Terminal : null;
    }

    /// <summary>The repo's src\, walked up from the test binary - null when the suite runs from a
    /// package, which the one arm that uses it reports rather than passing blind.</summary>
    private static string SrcRoot()
    {
        DirectoryInfo d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d != null)
        {
            string s = Path.Combine(d.FullName, "src");
            if (File.Exists(Path.Combine(s, "Bake", "ProjectBake.cs"))) return s;
            d = d.Parent;
        }
        return null;
    }

    private static int Check(bool condition, string what)
    {
        if (!condition) throw new Exception("LIFECYCLE FAILURE: " + what);
        return 1;
    }
}
