using System;
using System.Threading;
using Morgott.ContentTool.Import;

namespace Morgott.ContentTool.Bake
{
    /// <summary>
    /// ONE RUN AT A TIME, and a cancel that never lies.
    ///
    /// The segmented job (LifecycleJob) is Unity-bound and can never be linked into the offline gate, so
    /// everything it REMEMBERS lives here instead: the run handle, busy, the cancel request and its
    /// acknowledgement, the last progress snapshot and the one terminal result. G4 drives this object; the
    /// job only calls it.
    ///
    /// THE FOUR RULES, and each exists because breaking it produces a lie on screen:
    ///   1. ONE terminal result per run - a second completion cannot rewrite the first.
    ///   2. BUSY survives Cancel. A cancel is a request to a worker that is still touching files; freeing
    ///      the seam on the press would let the next stage start over it.
    ///   3. A cancel is ACKNOWLEDGED only by a producer that returned <c>Cancelled</c>. B5 is
    ///      non-cancellable, so a cancel that lost the race to a completed publication is remembered as a
    ///      request and never as an outcome.
    ///   4. A completion or progress carrying an OLD run id is DROPPED. That is the whole run-handle
    ///      protocol: a poll compares ids, so it can never read a newer run's state as this run's answer.
    ///
    /// The state is one IMMUTABLE snapshot behind a volatile field, replaced under a lock - SlimPanel's
    /// arrangement (SlimPanel.cs:74-:77), for the same reason: the worker writes, the UI thread reads, and
    /// a repaint must never catch half a transition. No filesystem, no Unity, no console.
    /// </summary>
    internal sealed class LifecycleRun
    {
        /// <summary>What the panel and the seam read. Every field is set once, in the constructor.</summary>
        internal sealed class Snapshot
        {
            /// <summary>0 when nothing has ever run. Non-zero ids are never reused.</summary>
            internal readonly long RunId;
            internal readonly string Stage;
            internal readonly bool Busy, CancelRequested, CancelAcknowledged;
            /// <summary>The worker's last published phase, or null. Reuses <see cref="SlimProgress"/> rather
            /// than a second four-field record with the same fields (SlimJob.cs:13).</summary>
            internal readonly SlimProgress Progress;
            /// <summary>The producer's OWN terminal line, verbatim. Null until it returns one.</summary>
            internal readonly string Result;
            internal readonly BakeDisposition How;

            internal Snapshot(long runId, string stage, bool busy, bool cancelRequested,
                              bool cancelAcknowledged, SlimProgress progress, string result,
                              BakeDisposition how)
            {
                RunId = runId; Stage = stage; Busy = busy; CancelRequested = cancelRequested;
                CancelAcknowledged = cancelAcknowledged; Progress = progress; Result = result; How = how;
            }
        }

        private readonly object gate = new object();
        private long ids;
        private volatile Snapshot state =
            new Snapshot(0, null, false, false, false, null, null, BakeDisposition.Success);

        internal Snapshot Latest { get { return state; } }

        /// <summary>Claims the seam for one stage. Returns the run's id, or 0 when one is already in flight
        /// - THE AUTHORITATIVE refusal. <c>LifecycleState.Admit</c>'s R26 is the one the author reads; this
        /// is the one that cannot race, because it takes the id under the same lock that publishes busy.
        /// A new run starts clean: no inherited cancel request, no previous result.</summary>
        internal long Begin(string stage)
        {
            lock (gate)
            {
                if (state.Busy) return 0;
                long id = ++ids;
                state = new Snapshot(id, stage, true, false, false, null, null, BakeDisposition.Success);
                return id;
            }
        }

        /// <summary>The author asked to stop. Silence when nothing is running - a request with no run to
        /// carry it must not be inherited by the next Begin.</summary>
        internal void Cancel()
        {
            lock (gate)
            {
                if (!state.Busy || state.CancelRequested) return;
                state = new Snapshot(state.RunId, state.Stage, true, true, state.CancelAcknowledged,
                                     state.Progress, state.Result, state.How);
            }
        }

        internal void Progress(long runId, SlimProgress progress)
        {
            lock (gate)
            {
                if (!state.Busy || state.RunId != runId) return;
                state = new Snapshot(state.RunId, state.Stage, true, state.CancelRequested,
                                     state.CancelAcknowledged, progress, state.Result, state.How);
            }
        }

        /// <summary>The producer's terminal line and disposition. False when this run is no longer the one
        /// in flight, or already reported - the caller has nothing to do about it, and the point is that
        /// the state did not move.</summary>
        internal bool Complete(long runId, string result, BakeDisposition how)
        {
            lock (gate)
            {
                if (!state.Busy || state.RunId != runId) return false;
                state = new Snapshot(state.RunId, state.Stage, false, state.CancelRequested,
                                     // RULE 3: only the producer's own Cancelled acknowledges it.
                                     how == BakeDisposition.Cancelled, state.Progress, result, how);
                return true;
            }
        }

        /// <summary>The token a producer checks. One source per run, cancelled by <see cref="Cancel"/>
        /// through the job that owns it - this class holds no CancellationTokenSource, because a reducer
        /// that owned a disposable would stop being one.</summary>
        internal static CancellationToken TokenOf(CancellationTokenSource cts)
        {
            return cts == null ? CancellationToken.None : cts.Token;
        }
    }

    /// <summary>
    /// ONE FILESYSTEM OBSERVATION, taken by the caller and handed in.
    ///
    /// <see cref="LifecycleState"/> is declared filesystem-free so the offline gate can link it, and
    /// <c>PatchCache.Key</c> (:43/:49) and <c>Fresh</c> (:84) both read and enumerate files - the two
    /// contracts cannot both hold inside one class. So the observation is taken ONCE, outside, by
    /// <c>Route7.Observe</c>, and the reducer decides from this value. <c>PatchCache</c> therefore stays out
    /// of the test project's Compile list, which is what makes the reducer testable at all.
    ///
    /// <c>Fresh</c> compares KEY TEXT ONLY, so the declared-copy census is the other half of the answer and
    /// both halves live here. <see cref="HaveAll"/> IS <c>Route7.ApplyProject</c>'s own `haveAll` - the same
    /// expression, in one place, asked by the checkbox and by the panel, so the two cannot drift by a term.
    /// </summary>
    internal sealed class FreshnessObservation
    {
        /// <summary>The key this project, this game build and this ContentTool format would produce now.</summary>
        internal readonly string Key;
        /// <summary>Does the receipt beside the copies answer to <see cref="Key"/>? A folder written by a
        /// ContentTool that had no key at all has none, and is therefore NOT a match - stale, not never
        /// (PatchCache.cs:84).</summary>
        internal readonly bool KeyMatches;
        /// <summary>Is there a patched directory at all? Its ABSENCE is what "never" means - there is no
        /// receipt to be stale.</summary>
        internal readonly bool CacheDirExists;
        /// <summary>The bundle names this project's manifest declares TODAY, video rows excluded (they are
        /// loose files served live by ct_video and this route never patches them).</summary>
        internal readonly string[] Declared;
        /// <summary>Those of <see cref="Declared"/> that are not on disk in the patched directory.</summary>
        internal readonly string[] MissingCopies;

        internal FreshnessObservation(string key, bool keyMatches, bool cacheDirExists,
                                      string[] declared, string[] missingCopies)
        {
            Key = key; KeyMatches = keyMatches; CacheDirExists = cacheDirExists;
            Declared = declared ?? new string[0];
            MissingCopies = missingCopies ?? new string[0];
        }

        /// <summary>Route7.ApplyProject's `haveAll`, and the ONLY copy of it: the receipt matches, the
        /// folder is there, and every declared copy is in it. Anything less re-bakes.</summary>
        internal bool HaveAll
        {
            get { return KeyMatches && CacheDirExists && MissingCopies.Length == 0; }
        }
    }

    /// <summary>
    /// The dashboard's reducer: what a stage is allowed to do, and how old the evidence is. PURE - no
    /// filesystem, no Unity, no console. Everything it needs is a value the caller measured.
    ///
    /// WHY ADMISSION IS ONE FUNCTION. The button path, the `Run(stage)` seam and the `Run all` sequencer all
    /// ask <see cref="Admit"/> immediately before an intent is enqueued, and nothing else re-implements a
    /// dependency graph. `Run all` asks it PER STAGE as it reaches that stage, never up front, so an earlier
    /// stage's output can satisfy a later stage's admission.
    ///
    /// THE GOVERNING RULE: a stage that can regenerate its own input is never refused for missing evidence.
    /// R28 fires only where a stage would otherwise read evidence that is not on disk - which is Verify, and
    /// only Verify.
    /// </summary>
    internal static class LifecycleState
    {
        /// <summary>How the caller's project selection resolved. The reducer never touches the filesystem,
        /// so "no project picked", "the folder is gone" and "two projects answer to that name" are collapsed
        /// to these three by whoever did look.</summary>
        internal enum Selection { None, Unavailable, Ok }

        /// <summary>Everything <see cref="Admit"/> is allowed to know. Default-constructed it refuses with
        /// R25, which is the right answer for a panel that has not selected anything yet.
        ///
        /// AUTO-PROPERTIES, not fields, and for one reason: the only thing that fills these is the panel
        /// (Task 6), so in ContentTool.csproj every field here would be a CS0649 "nobody assigns it" until
        /// then - seven of them, over the gate's one known warning. Same trap Task 1 hit with StageResult's
        /// row fields; a property carries no such warning and the object-initializer call sites are
        /// unchanged.</summary>
        internal sealed class Admission
        {
            internal Selection Selection { get; set; }
            /// <summary>The stage the seam is already running, or null. R26.</summary>
            internal string RunningStage { get; set; }
            internal string ProjectId { get; set; }
            /// <summary>Non-null when <c>Route7.IsFailed</c> says this mod's bake failed earlier in this
            /// session - the value is <c>Route7.RetryHint</c>, the only thing that knows which console
            /// argument resolves back to that folder. R29.</summary>
            internal string RetryHint { get; set; }
            /// <summary>An older ContentTool's on-disk edit is still in the installation. R36.</summary>
            internal bool LegacyDiskActive { get; set; }
            /// <summary>The apply would write somewhere that is neither the mod-manager apply path nor the
            /// author's own output. R34. Inverted on purpose: the default is "allowed".</summary>
            internal bool WriteOutsideRoots { get; set; }
            /// <summary>How old the PATCHED COPIES are - <see cref="Fresh"/> of the caller's observation.
            /// Read by Verify alone; Apply re-bakes them itself and Bake does not read them.</summary>
            internal Freshness Copies { get; set; }
        }

        /// <summary>The refusal this stage would print, or null when it may run. Design section 4.6, row by
        /// row. Order matters: an unknown token is answered before anything is asked about the project, and
        /// the selection before the busy seam, because a panel with nothing selected is never running.</summary>
        internal static string Admit(string stage, Admission ctx)
        {
            if (!Known(stage)) return StageText.R33(stage);
            if (ctx == null || ctx.Selection == Selection.None) return StageText.R25();
            if (ctx.Selection == Selection.Unavailable) return StageText.R27();
            if (!string.IsNullOrEmpty(ctx.RunningStage)) return StageText.R26(ctx.RunningStage);

            switch (stage)
            {
                case "Apply":
                    // NEVER R28 for a stale or absent bake: ApplyProject bakes on a stale or missing key
                    // ITSELF (Route7.cs:311-:351) and that bake reports through the same producer, filling
                    // the Bake row. Refusing here would block the one path that repairs the thing it is
                    // refusing over.
                    if (ctx.LegacyDiskActive) return StageText.R36();
                    if (ctx.WriteOutsideRoots) return StageText.R34();
                    if (!string.IsNullOrEmpty(ctx.RetryHint)) return StageText.R29(ctx.ProjectId, ctx.RetryHint);
                    return null;

                case "Verify":
                    // The one stage that READS evidence it cannot regenerate: it measures the copies on
                    // disk. Absent or stale, there is nothing to measure and a verdict would be invented.
                    return ctx.Copies == Freshness.Fresh
                        ? null
                        : StageText.R28("Verify", "patched copies", ctx.Copies);

                default:
                    // Validate re-derives its own receipts; Bake loads and validates the manifest itself, so
                    // a `never` Validate is not a prerequisite; Package's payload and empty-destination
                    // refusals belong to Package.Run alone (Package.cs:78) and are not restated here. "All"
                    // is admitted and re-asked per stage as the sequencer reaches it.
                    return null;
            }
        }

        /// <summary>The accepted tokens, exactly.</summary>
        private static bool Known(string stage)
        {
            return stage == "Validate" || stage == "Bake" || stage == "Apply" ||
                   stage == "Verify" || stage == "Package" || stage == "All";
        }

        /// <summary>Evidence age from the caller's one observation. No receipt at all is `never`; so is a
        /// directory in which EVERY declared copy is absent - design:199 reads "absent -> never, key
        /// mismatch -> stale", and Verify's R28 must say which of the two the author is looking at. A
        /// receipt whose key does not match, or over an output only PARTLY on disk, is `stale`; everything
        /// present and answering to the key is `fresh`.
        ///
        /// The `Declared.Length != 0` term is not decoration: a project with no declared target (all rows
        /// are video) has an empty census, so `Missing.Length == Declared.Length` is trivially true and a
        /// key mismatch would have reported `never`.</summary>
        internal static Freshness Fresh(FreshnessObservation o)
        {
            if (o == null || !o.CacheDirExists) return Freshness.Never;
            if (o.HaveAll) return Freshness.Fresh;
            return o.Declared.Length != 0 && o.MissingCopies.Length == o.Declared.Length
                ? Freshness.Never : Freshness.Stale;
        }
    }
}
