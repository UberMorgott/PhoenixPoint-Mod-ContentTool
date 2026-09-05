namespace Morgott.ContentTool.Bake
{
    /// <summary>THE verdict strings, in one place, composed by the PRODUCERS that already printed them.
    ///
    /// Not a formatter the dashboard owns and the producers happen to match: `ModelDoctor` (S1/S2) and
    /// `Route7` (R29/R35) now call in here, so there is exactly one copy of each of those sentences on disk.
    /// A second copy that only a test reads would be a second truth, and the gate would then prove the two
    /// copies agree while the producer drifted from both.
    ///
    /// The rest are read-only quotations of producers this slice does not own yet - S4/S5 and the three bake
    /// special cases (ProjectBake.cs:126-135, :405-406), S7 and the Package refusal (Package.cs:78-80, :180).
    /// Those producers keep composing their own line and the dashboard forwards it verbatim; the copy here is
    /// what the panel needs to RECOGNISE, never to substitute. ponytail: route ProjectBake through S4/S5 when
    /// Task 2 opens that file anyway - Package.cs is on the plan's "NOT modified" list and stays quoted.
    ///
    /// No string here is ever PARSED to classify an outcome. The outcome comes from the carrier.</summary>
    internal static class StageText
    {
        /// <summary>An em dash: the idle row, a stage nobody has run yet.</summary>
        internal const string Idle = "\u2014";
        internal const string Ready = "Ready.";

        // ---- S: the success lines. --------------------------------------------------------------------
        /// <summary>Apply PASS / Resident. ModelDoctor.cs:710-712, and called from there.</summary>
        internal static string S1(string name, string bundle, bool hasPreview)
        {
            return "applied - restart the game and enable '" + name + "' in the mod manager. " +
                   "Phoenix Point already loaded " + bundle + "." +
                   (hasPreview ? " This session keeps showing your Doctor preview." : "");
        }

        /// <summary>Apply PASS / Redirected. ModelDoctor.cs:714-715, and called from there.</summary>
        internal static string S2(string bundle)
        {
            return "applied and redirected LIVE - " + bundle + " now loads from the patched copy " +
                   "on the next load";
        }

        /// <summary>Validate PASS. NEW - Validate had no success string of its own.</summary>
        internal static string S3(string name) { return "Validate: PASS - '" + name + "'."; }

        /// <summary>Bake PASS. Quotes ProjectBake.cs:405.</summary>
        internal static string S4(string outPath) { return "ct_project: ALL PASS - " + outPath; }

        /// <summary>Bake FAIL. Quotes ProjectBake.cs:406 (and :126, the same sentence).</summary>
        internal static string S5(int failures) { return "ct_project: " + failures + " FAILURE(S)"; }

        /// <summary>Verify PASS. NEW. Says how many of the DECLARED targets this project's own copies serve -
        /// a per-target census, never BundleLive.Holds, which passes on one matching claim.</summary>
        internal static string S6(string name, int served, int declared)
        {
            return "Verify: PASS - load-back gates passed; " + served + " of " + declared +
                   " declared target(s) served from this project's copies for '" + name + "'.";
        }

        /// <summary>Package PASS. Quotes Package.cs:180; that producer appends its own LEFT BEHIND tail.</summary>
        internal static string S7(int files, long bytes, string outDir)
        {
            return "PACKAGED " + files + " file(s), " + bytes + " B into " + outDir;
        }

        /// <summary>Quotes Package.cs:78-80. Package.Run stays the sole authority on WHEN it fires.</summary>
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

        internal static string BakeNothingPatched(int replacements)
        {
            return "ct_project: ALL PASS - nothing needed patching: none of this project's " +
                   replacements + " replacement(s) names a shipped bundle, so no copy was " +
                   "written - the video row(s) above are served live by ct_video";
        }

        // ---- R: the refusals. R25-R36 are dashboard guards; R37 and R38 are PRODUCER guards, so the
        // console verb and the mod-manager checkbox print them too.
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
