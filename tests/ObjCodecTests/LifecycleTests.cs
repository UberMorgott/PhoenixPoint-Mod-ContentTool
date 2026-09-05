using System;
using System.IO;
using Morgott.ContentTool.Bake;
using Morgott.ContentTool.IO;

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
        checks += Check(allVoid.Failed == 0 && allVoid.MandatoryVoid("mesh_a", RowKind.Mesh),
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
        checks += Check(!meshOk.MandatoryVoid("mesh_a", RowKind.Mesh),
                        "P5/P6 VOID on a proven mesh row is allowed - only P4 and P4-bytes are mandatory");
        checks += Check(meshOk.Passed == 2 && meshOk.Void == 2 && meshOk.Failed == 0,
                        "pass/void/fail are counted apart");
        checks += Check(ReadBackResult.Of(null,
                            GateEntry.Pass("P4", "mesh_a", "x"),
                            GateEntry.Void("P4-bytes", "mesh_a", "y")).MandatoryVoid("mesh_a", RowKind.Mesh),
                        "one mandatory gate VOID is enough - P4-bytes is not optional");
        // A gate that never ran is not a proof either: absence and VOID are the same answer here.
        checks += Check(ReadBackResult.Of(null, GateEntry.Pass("P4", "mesh_a", "x"))
                            .MandatoryVoid("mesh_a", RowKind.Mesh),
                        "a mandatory gate with NO entry at all is VOID, not an implied pass");
        checks += Check(meshOk.MandatoryVoid("mesh_b", RowKind.Mesh),
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
        checks += Check(ReadBackResult.Of(null, GateEntry.Void("P3", "mat_a", "x")).MandatoryVoid("mat_a", RowKind.Material) &&
                        !ReadBackResult.Of(null, GateEntry.Pass("P3", "mat_a", "x")).MandatoryVoid("mat_a", RowKind.Material),
                        "a material row needs P3");
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
        // S4/S5 and the three bake special cases have no arm here on purpose: ProjectBake composes them
        // through StageText (`:128`-`:133`, `:402`), so a literal re-typed here would compare this file with
        // itself. W18 proves that wording against the real producer, in game.
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
                        "R37 - design:377, NO PRODUCER YET: ProjectBake.cs:69 / ContentToolMain.cs:480 / " +
                        "Route7.cs:341 must call it when Task 3 lands the guard");
        checks += Check(StageText.R38("a.bundle") ==
                        "ct_project: 'a.bundle' is being served to the game right now, so it was not " +
                        "rewritten - restart the game and bake again.",
                        "R38 - design:378, no producer yet either - Task 3 Step 4");
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

        checks += Publication();
        checks += Ownership();
        checks += Admission();

        return "LIFECYCLE PASS, " + checks + " check(s) - carrier arms, verdict wording, frozen Tail, " +
               "one file swap, one output owner, the admission table";
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

        // ---- The ONE freshness observation. Route7.cs:308-:310 computes `fresh && Directory.Exists(patched)`
        // and then clears it for every declared copy that is absent; `HaveAll` IS that expression and
        // ApplyProject now asks it here, so the panel and the checkbox cannot drift by a single term.
        FreshnessObservation gone = new FreshnessObservation("k", false, false, new string[0], new string[0]);
        FreshnessObservation wrongKey = new FreshnessObservation("k", false, true, new[] { "a.bundle" }, new string[0]);
        FreshnessObservation missing = new FreshnessObservation("k", true, true, new[] { "a.bundle" }, new[] { "a.bundle" });
        FreshnessObservation good = new FreshnessObservation("k", true, true, new[] { "a.bundle" }, new string[0]);
        checks += Check(LifecycleState.Fresh(gone) == Freshness.Never && !gone.HaveAll,
                        "no cache directory is 'never' - there is no receipt to be stale");
        checks += Check(LifecycleState.Fresh(wrongKey) == Freshness.Stale && !wrongKey.HaveAll,
                        "a cache directory whose key does not match - or that has none at all - is STALE, " +
                        "not never (PatchCache.cs:84)");
        checks += Check(LifecycleState.Fresh(missing) == Freshness.Stale && !missing.HaveAll,
                        "a matching key over a declared copy that vanished is stale - the census is the " +
                        "other half of the answer, Fresh() compares key text only");
        checks += Check(LifecycleState.Fresh(good) == Freshness.Fresh && good.HaveAll,
                        "receipt matches and every declared copy is there - this is Route7's `haveAll`");
        return checks;
    }

    /// <summary>G4 - the ONE swap. AtomicFile.Write makes its own temp (AtomicFile.cs:19) and so cannot
    /// publish one a bake already streamed; Publish takes that temp and performs the same two-armed swap.
    /// Real files, System.IO only - the thing under test IS the filesystem call.
    ///
    /// The last two arms are the regression this split could introduce: Write's own cleanup guard covers a
    /// failed open, write or flush, NONE of which ever reach Publish, so moving that guard wholesale would
    /// strand Write's temp on exactly the paths that have one today.</summary>
    private static int Publication()
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

    private static int Check(bool condition, string what)
    {
        if (!condition) throw new Exception("LIFECYCLE FAILURE: " + what);
        return 1;
    }
}
