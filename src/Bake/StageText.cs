namespace Morgott.ContentTool.Bake
{
    /// <summary>THE verdict strings, in one place, composed by the PRODUCERS that already printed them.
    ///
    /// Not a formatter the dashboard owns and the producers happen to match: `ModelDoctor` (S1/S2) and
    /// `Route7` (R29/R35) now call in here, so there is exactly one copy of each of those sentences on disk.
    /// A second copy that only a test reads would be a second truth, and the gate would then prove the two
    /// copies agree while the producer drifted from both.
    ///
    /// S4/S5 and the three bake special cases are now COMPOSED here for ProjectBake too (it calls S4, S5,
    /// BakeNothingToBake, BakeNoOwnBundle and BakeNothingPatched at :127-133 and :402), so bake's console
    /// line and the panel's row are the same string and cannot drift apart.
    /// S7 and the Package refusal are composed here too: `Package.Run` calls them (src\Project\Package.cs:77
    /// and :178), which cost two Compile lines in tools\Package and tests\TargetPathTests and removed the
    /// last pair of strings that existed twice. Package appends its own LEFT BEHIND tail to S7.
    ///
    /// ONE thing here IS parsed: the `ct_project: ALL PASS` PREFIX of S4, BakeNoOwnBundle and
    /// BakeNothingPatched, which `StageResult.BakePassed` classifies a bake by - on the TERMINAL line only,
    /// because those words also appear mid-report in ClipFields.cs:505's refusal. That prefix is pinned by an
    /// offline arm (LifecycleTests). Nothing else is parsed - every other outcome comes from the
    /// carrier.</summary>
    internal static class StageText
    {
        /// <summary>An em dash: the idle row, a stage nobody has run yet.</summary>
        internal const string Idle = "\u2014";
        internal const string Ready = "Ready.";

        // ---- S: the success lines. --------------------------------------------------------------------
        /// <summary>Apply PASS / Resident. Called from src\Dev\ModelDoctor.cs:710.</summary>
        internal static string S1(string name, string bundle, bool hasPreview)
        {
            return "applied - restart the game and enable '" + name + "' in the mod manager. " +
                   "Phoenix Point already loaded " + bundle + "." +
                   (hasPreview ? " This session keeps showing your Doctor preview." : "");
        }

        /// <summary>Apply PASS / Redirected. Called from src\Dev\ModelDoctor.cs:712.</summary>
        internal static string S2(string bundle)
        {
            return "applied and redirected LIVE - " + bundle + " now loads from the patched copy " +
                   "on the next load";
        }

        /// <summary>Validate PASS. NEW - Validate had no success string of its own. It carries §4.1's
        /// pre-import KEY, which is the only thing that makes "the key is computable" observable at all.</summary>
        internal static string S3(string name, string key)
        {
            return "Validate: PASS - '" + name + "' - key " + key + ".";
        }

        /// <summary>Bake PASS. Quotes ProjectBake.cs:402.</summary>
        internal static string S4(string outPath) { return "ct_project: ALL PASS - " + outPath; }

        /// <summary>Bake FAIL. Quotes ProjectBake.cs:402 (and :128, the same sentence).</summary>
        internal static string S5(int failures) { return "ct_project: " + failures + " FAILURE(S)"; }

        /// <summary>Verify PASS. NEW. Says how many of the DECLARED targets this project's own copies serve -
        /// a per-target census, never BundleLive.Holds, which passes on one matching claim.
        ///
        /// A SHORTFALL CANNOT COME OUT AS PASS. design:384 - "any target missing -> VOID" - so the census is
        /// the pass condition, and this function refuses to word "1 of 2 ... PASS". The unserved targets are
        /// NAMED by the producer's own per-target refusals, which are preserved beside this line, never
        /// aggregated into it.</summary>
        internal static string S6(string name, int served, int declared)
        {
            return served == declared
                ? "Verify: PASS - load-back gates passed; " + served + " of " + declared +
                  " declared target(s) served from this project's copies for '" + name + "'."
                : "Verify: VOID - only " + served + " of " + declared +
                  " declared target(s) are served from this project's copies for '" + name +
                  "'; the target(s) named above are unproven.";
        }

        /// <summary>Verify PASS with an EMPTY census. NEW - design:390. A project that declares no patched
        /// target has nothing on disk to measure, and refusing it with R28 forever was the bug: no bake ever
        /// writes a key for it (LifecycleState.Fresh).</summary>
        internal static string S8(string name)
        {
            return "Verify: PASS - nothing to verify for '" + name + "'; this project declares no " +
                   "patched target - its row(s) are served live by ct_video.";
        }

        /// <summary>Apply's twin of <see cref="S8"/> - a row with NO GATE AT ALL, not a blocking refusal.
        /// It is stated by <c>LifecycleJob.StartApply</c> BEFORE the live segment, from the declaration
        /// alone: a project that declares no non-video "replace" target has nothing to claim, bake or
        /// redirect, so `ApplyRoot` is never entered and every refusal that DOES come out of it - R37, R38,
        /// a contended output - is a real one that stops the chain.</summary>
        internal static string S9(string name)
        {
            return "Apply: VOID - nothing to install for '" + name + "'; this project declares no " +
                   "non-video replacement target.";
        }

        /// <summary>Package PASS. Called from src\Project\Package.cs:178, which appends its own LEFT BEHIND
        /// tail to it.</summary>
        internal static string S7(int files, long bytes, string outDir)
        {
            return "PACKAGED " + files + " file(s), " + bytes + " B into " + outDir;
        }

        /// <summary>Called from src\Project\Package.cs:77. Package.Run stays the sole authority on WHEN it
        /// fires.</summary>
        internal static string PackageRefused(string outDir)
        {
            return "REFUSED: " + outDir + " already holds files. Name a folder that does not exist " +
                   "yet - a package is built from nothing, so no leftover of a previous run can be " +
                   "shipped by accident.";
        }

        // ---- The bake special cases, ProjectBake.cs:126-135. Their OUTCOME comes from the producer; the
        // wording is quoted so the panel can show it, never parsed to classify it.
        internal static string BakeNothingToBake()
        {
            return "nothing to bake - put .png/.jpg under Content\\Textures\\, " +
                   ".glb under Content\\Models\\ or .wav under Content\\Audio\\";
        }

        internal static string BakeNoOwnBundle()
        {
            return "ct_project: ALL PASS - this project has no bundle of its own; the patched " +
                   "copy(ies) above are the whole output";
        }

        /// <summary>The bake stopped at a cooperative boundary because the author asked it to. NEW with the
        /// segmented job - there was no cancel button before it, and <c>BakeDisposition.Cancelled</c> had no
        /// producer. It says what is on disk, because "cancelled" alone leaves the author guessing whether
        /// their copies were half-replaced: nothing was published, so they were not.</summary>
        internal static string BakeCancelled(string id)
        {
            return "ct_project: CANCELLED - '" + id + "' stopped before it published anything; the " +
                   "patched copies and this project's own bundle are exactly as they were.";
        }

        internal static string BakeNothingPatched(int replacements)
        {
            return "ct_project: ALL PASS - nothing needed patching: none of this project's " +
                   replacements + " replacement(s) names a shipped bundle, so no copy was " +
                   "written - the video row(s) above are served live by ct_video";
        }

        // ---- R: the refusals. R25-R36 are dashboard guards. R37 and R38 are PRODUCER guards and Task 3
        // landed them - design:377/:378. Their live call sites: R37 from ProjectBake.cs:104 (the bake takes
        // the output claim) and Route7.cs:335 (the mod-manager checkbox takes it across bake+install);
        // R38 from ProjectBake.cs:1992 (LiveReader, a copy this mod is serving right now).
        internal static string R25() { return "Lifecycle: select a ContentMods project."; }

        internal static string R26(string stage) { return "Lifecycle: busy running " + stage + "."; }

        internal static string R27()
        {
            return "Lifecycle: selected project is unavailable; refresh the project list.";
        }

        /// <summary>Fires only where the admission table says so - never for Apply on a stale bake, which
        /// ApplyProject re-bakes itself.</summary>
        internal static string R28(string stage, string prerequisite, Freshness freshness)
        {
            // The three words are the enum's, not a caller's spelling - "never"/"stale"/"fresh" is a closed
            // set and a typo here reads as a fourth state nobody defined.
            return "Lifecycle: " + stage + " blocked; " + prerequisite + " is " +
                   (freshness == Freshness.Never ? "never" : freshness == Freshness.Stale ? "stale" : "fresh") +
                   ".";
        }

        /// <summary>THE `Run all` COLUMN of design:194-:204, and its only wording. Inside a chain a later
        /// stage is refused when the earlier one it reads did not pass; a STANDALONE run never sees this,
        /// which is why every arm that composes it is guarded by <c>Admission.InRunAll</c>. R28's own
        /// sentence cannot serve here - it words an EVIDENCE AGE ("is never"/"is stale"), and a Bake that
        /// failed two seconds ago has no freshness to report.</summary>
        internal static string R28All(string stage, string prerequisite)
        {
            return "Lifecycle: " + stage + " blocked in Run all; " + prerequisite + " did not pass.";
        }

        /// <summary>Route7.cs:129-132, and called from there. The retry hint is passed IN, because only
        /// Route7.RetryHint (:158) knows which argument actually resolves back to that folder.</summary>
        internal static string R29(string id, string retryHint)
        {
            return "'" + id + "' failed to bake earlier in this session - not baking it " +
                   "again. Fix the lines it printed, then " + retryHint;
        }

        /// <summary>A barrier, not a failure: S1 means the game has not reloaded yet.</summary>
        internal static string R30(string name) { return "Verify: VOID - restart required for '" + name + "'."; }

        /// <summary>Never claims a rollback - a cancel stops the CONTINUATION, it does not undo a
        /// publication that already succeeded.</summary>
        internal static string R31(string stage)
        {
            return "Lifecycle: " + stage + " cancelled; later stages were not run.";
        }

        internal static string R32(string stage)
        {
            return "Lifecycle: project changed during " + stage + "; validate again.";
        }

        /// <summary>The accepted tokens are exactly Validate, Bake, Apply, Verify, Package, All.</summary>
        internal static string R33(string stage) { return "Lifecycle: unknown stage '" + stage + "'."; }

        internal static string R34()
        {
            return "Lifecycle: refused a write outside the mod-manager apply path or author output.";
        }

        /// <summary>Route7.cs:349-351, and called from there.</summary>
        internal static string R35(int patchFailed)
        {
            return "NOT APPLIED: patching the shipped bundle(s) reported " + patchFailed +
                   " failure(s), named in the P0/REFUSED line(s) above; nothing was " +
                   "installed and no copy was marked current.";
        }

        internal static string R36() { return "Lifecycle: Apply blocked while legacy disk patching is active."; }

        internal static string R37(string dir)
        {
            return "ct_project: '" + dir + "' is already being written by another run - nothing was " +
                   "baked. Wait for it to finish, then bake again.";
        }

        internal static string R38(string file)
        {
            return "ct_project: '" + file + "' is being served to the game right now, so it was not " +
                   "rewritten - restart the game and bake again.";
        }

        // ---- The fallbacks, for a backend that threw without a verdict of its own. One line, one reason;
        // the detail stays in the tail.
        internal static string ValidateFailed(string reason) { return "Validate: FAIL - " + reason; }
        internal static string VerifyFailed(string reason) { return "Verify: FAIL - " + reason; }

        // ---- Transient Message strings. NEVER terminal verdicts, and never stored in a row.
        internal static string Queued(string stage) { return "Queued: " + stage; }
        internal static string Running(string stage) { return "Running: " + stage; }

        internal static string CancelRequested(string stage)
        {
            return "Cancel requested; waiting for " + stage + " to stop.";
        }

        internal static string CancelUnavailable(string stage)
        {
            return "Cancel unavailable during " + stage + ".";
        }

        /// <summary>After a publication already succeeded: that stage KEEPS its PASS.</summary>
        internal static string CancelledAfter(string stage)
        {
            return "Lifecycle: cancelled after " + stage + "; later stages were not run.";
        }
    }
}
