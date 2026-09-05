using System;
using System.IO;
using Morgott.ContentTool.Bake;
using Morgott.ContentTool.IO;

/// <summary>The lifecycle dashboard's CARRIER and its one verdict formatter.
///
/// G1 - a read-back that failed nothing is not therefore a PASS. Every VOID arm in ProjectBake returns 0
/// exactly like a pass (`ProjectBake.cs:1835`, `:1852`, `:1866`, `:1873`, `:1894`, `:1923`, `:1942`), so a
/// failure count plus a log cannot tell the two apart and the panel would print PASS over a row nothing
/// proved. The carrier answers structurally, without anyone reading text.
///
/// G2 - the wording. Every string here was COPIED FROM DISK at the file:line named on the arm, or, where the
/// design invents one, from `2026-09-05-lifecycle-dashboard-design.md:365`-`:407`. The existing producers
/// (`ModelDoctor` S1/S2, `Route7` R29/R35) now COMPOSE their line through StageText rather than owning a
/// second copy of it, so this gate compares the formatter against disk while the producer IS the formatter.
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
        ReadBackResult allVoid = ReadBackResult.Of(
            GateEntry.Void("P4", "mesh_a", "P4 VOID mesh 'mesh_a' was not read back"),
            GateEntry.Void("P4-bytes", "mesh_a", "P4-bytes VOID mesh 'mesh_a' has no readable buffers"));
        checks += Check(allVoid.Failed == 0 && allVoid.MandatoryVoid("mesh_a", RowKind.Mesh),
                        "zero failures with a mandatory VOID is VOID, never S6");
        checks += Check(allVoid.Void == 2 && allVoid.Passed == 0 && allVoid.Entries.Count == 2,
                        "the counts are structured, not parsed out of the log");

        // A mesh row needs BOTH P4 and P4-bytes; P5/P6 may be VOID (a skinless or same-order source has
        // nothing to measure - ProjectBake.cs:1832, :1939), and that must not sink the row.
        ReadBackResult meshOk = ReadBackResult.Of(
            GateEntry.Pass("P4", "mesh_a", "P4 mesh 'mesh_a' read back"),
            GateEntry.Pass("P4-bytes", "mesh_a", "P4-bytes 12 vertices"),
            GateEntry.Void("P5", "mesh_a", "P5 VOID no bind poses"),
            GateEntry.Void("P6", "mesh_a", "P6 VOID same bone order"));
        checks += Check(!meshOk.MandatoryVoid("mesh_a", RowKind.Mesh),
                        "P5/P6 VOID on a proven mesh row is allowed - only P4 and P4-bytes are mandatory");
        checks += Check(meshOk.Passed == 2 && meshOk.Void == 2 && meshOk.Failed == 0,
                        "pass/void/fail are counted apart");
        checks += Check(ReadBackResult.Of(
                            GateEntry.Pass("P4", "mesh_a", "x"),
                            GateEntry.Void("P4-bytes", "mesh_a", "y")).MandatoryVoid("mesh_a", RowKind.Mesh),
                        "one mandatory gate VOID is enough - P4-bytes is not optional");
        // A gate that never ran is not a proof either: absence and VOID are the same answer here.
        checks += Check(ReadBackResult.Of(GateEntry.Pass("P4", "mesh_a", "x"))
                            .MandatoryVoid("mesh_a", RowKind.Mesh),
                        "a mandatory gate with NO entry at all is VOID, not an implied pass");
        checks += Check(meshOk.MandatoryVoid("mesh_b", RowKind.Mesh),
                        "another target's proofs never satisfy this target");
        checks += Check(ReadBackResult.Of(
                            GateEntry.Pass("P1", "tex_a", "x"),
                            GateEntry.Void("P1-ctl-shipped", "tex_a", "y")).MandatoryVoid("tex_a", RowKind.Texture) &&
                        !ReadBackResult.Of(
                            GateEntry.Pass("P1", "tex_a", "x"),
                            GateEntry.Pass("P1-ctl-shipped", "tex_a", "y")).MandatoryVoid("tex_a", RowKind.Texture),
                        "a texture row needs P1 AND P1-ctl-shipped");
        checks += Check(ReadBackResult.Of(GateEntry.Void("P3", "mat_a", "x")).MandatoryVoid("mat_a", RowKind.Material) &&
                        !ReadBackResult.Of(GateEntry.Pass("P3", "mat_a", "x")).MandatoryVoid("mat_a", RowKind.Material),
                        "a material row needs P3");
        ReadBackResult failed = ReadBackResult.Of(GateEntry.Fail("P4", "mesh_a", "P4 FAILED mesh 'mesh_a'"));
        checks += Check(failed.Failed == 1 && failed.Entries[0].Outcome == GateOutcome.Fail &&
                        failed.Entries[0].Gate == "P4" && failed.Entries[0].Target == "mesh_a" &&
                        failed.Entries[0].Line == "P4 FAILED mesh 'mesh_a'",
                        "an entry carries the gate id, the target key and the producer's exact line");
        ReadBackResult terminal = ReadBackResult.Of("ct_project: ALL PASS - D:\\x\\Dist\\a.bundle");
        checks += Check(terminal.Entries.Count == 0 && ReadBackResult.Of().Terminal == null &&
                        terminal.Terminal == "ct_project: ALL PASS - D:\\x\\Dist\\a.bundle",
                        "the terminal line rides along verbatim, never recomposed by the panel");
        BakeResult bake = new BakeResult(3, 1, "ct_project: 3 FAILURE(S)");
        checks += Check(bake.Failed == 3 && bake.PatchFailed == 1 && bake.Terminal == "ct_project: 3 FAILURE(S)",
                        "BakeResult keeps failed and patchFailed apart - patchFailed alone authorises publication");

        // ---- G2 wording: the exact strings, from ONE producer.
        checks += Check(StageText.S1("Replace_Rifle", "px_equipment_assets_all.bundle", false) ==
                        "applied - restart the game and enable 'Replace_Rifle' in the mod manager. " +
                        "Phoenix Point already loaded px_equipment_assets_all.bundle.",
                        "S1 is ModelDoctor.cs:710-712 verbatim");
        checks += Check(StageText.S1("Replace_Rifle", "px_equipment_assets_all.bundle", true) ==
                        StageText.S1("Replace_Rifle", "px_equipment_assets_all.bundle", false) +
                        " This session keeps showing your Doctor preview.",
                        "S1 appends the preview sentence iff HasPreview - ModelDoctor.cs:712");
        checks += Check(StageText.S2("px_equipment_assets_all.bundle") ==
                        "applied and redirected LIVE - px_equipment_assets_all.bundle now loads from the " +
                        "patched copy on the next load",
                        "S2 is ModelDoctor.cs:714-715 verbatim");
        checks += Check(StageText.S3("Replace_Rifle") == "Validate: PASS - 'Replace_Rifle'.",
                        "S3 is NEW - design:381");
        checks += Check(StageText.S4("D:\\x\\Dist\\a.bundle") == "ct_project: ALL PASS - D:\\x\\Dist\\a.bundle",
                        "S4 is ProjectBake.cs:405 verbatim");
        checks += Check(StageText.S5(3) == "ct_project: 3 FAILURE(S)", "S5 is ProjectBake.cs:406 verbatim");
        checks += Check(StageText.S6("Replace_Rifle", 2, 2) ==
                        "Verify: PASS - load-back gates passed; 2 of 2 declared target(s) served from this " +
                        "project's copies for 'Replace_Rifle'.",
                        "S6 is NEW - design:384");
        checks += Check(StageText.S7(4, 1234L, "D:\\out") == "PACKAGED 4 file(s), 1234 B into D:\\out",
                        "S7 is Package.cs:180 verbatim");
        checks += Check(StageText.PackageRefused("D:\\out") ==
                        "REFUSED: D:\\out already holds files. Name a folder that does not exist yet - a " +
                        "package is built from nothing, so no leftover of a previous run can be shipped by " +
                        "accident.",
                        "the Package refusal is Package.cs:78-80 verbatim");
        checks += Check(StageText.BakeNothingToBake() ==
                        "nothing to bake - put .png/.jpg under Content\\Textures\\, .glb under " +
                        "Content\\Models\\ or .wav under Content\\Audio\\",
                        "bake special case 1 is ProjectBake.cs:128-129 verbatim");
        checks += Check(StageText.BakeNoOwnBundle() ==
                        "ct_project: ALL PASS - this project has no bundle of its own; the patched copy(ies) " +
                        "above are the whole output",
                        "bake special case 2 is ProjectBake.cs:131-132 verbatim");
        checks += Check(StageText.BakeNothingPatched(2) ==
                        "ct_project: ALL PASS - nothing needed patching: none of this project's 2 " +
                        "replacement(s) names a shipped bundle, so no copy was written - the video row(s) " +
                        "above are served live by ct_video",
                        "bake special case 3 is ProjectBake.cs:133-135 verbatim");
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
                        "R29 is Route7.cs:130-132 verbatim, RetryHint passed in from :158");
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
                        "R35 is Route7.cs:349-351 verbatim");
        checks += Check(StageText.R36() == "Lifecycle: Apply blocked while legacy disk patching is active.",
                        "R36 - design:376");
        checks += Check(StageText.R37("D:\\x\\Dist") ==
                        "ct_project: 'D:\\x\\Dist' is already being written by another run - nothing was " +
                        "baked. Wait for it to finish, then bake again.",
                        "R37 - design:377, a PRODUCER guard: the console verb and the checkbox print it too");
        checks += Check(StageText.R38("a.bundle") ==
                        "ct_project: 'a.bundle' is being served to the game right now, so it was not " +
                        "rewritten - restart the game and bake again.",
                        "R38 - design:378, also a producer guard");
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

        return "LIFECYCLE PASS, " + checks + " check(s) - carrier arms, verdict wording, frozen Tail, " +
               "one file swap";
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
