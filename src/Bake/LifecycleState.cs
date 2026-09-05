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
    /// WHAT THE SEAM SHOWS, AND HOW IT FITS DOWN THE WIRE.
    ///
    /// The transport is not a choice. `connect call` returns what `PPCLI/src/Reflect.cs` can project, and
    /// `Project` (:1080) never enumerates or walks properties: a non-trivial reference comes back as a
    /// `{h, type}` handle, so an object of five rows would arrive as one useless number. Every seam method
    /// therefore returns a JSON STRING. And `Protocol.Clip` truncates a reply at `MaxOutputLineChars = 2000`
    /// (`PPCLI/src/Protocol.cs:56`) and appends " ...(clipped)" - SILENTLY, mid-token, producing JSON that
    /// `ConvertFrom-Json` refuses with an error nowhere near the cause. So the snapshot is SECTIONED and
    /// every section bounds ITSELF: the header is the poll and carries no verdict text at all, and one
    /// section at a time carries one verbatim payload.
    ///
    /// THIS LIVES BESIDE THE REDUCER, NOT IN THE PANEL, for the reason the offline gate exists: composing
    /// the sections is the half that can be proven without Unity, and `LifecycleDashboard` pulls
    /// UnityEngine and can never be linked into `ObjCodecTests`. The panel fills these fields; this file
    /// turns them into strings that fit.
    /// </summary>
    internal sealed class LifecycleView
    {
        /// <summary>PPCLI's own limit is 2000; the margin absorbs the transport's framing so a payload that
        /// measured as fitting cannot arrive clipped anyway.</summary>
        internal const int MaxPayload = 1900;
        /// <summary>How much of a header string field is worth showing. Five rows plus these fields is a
        /// header of roughly 1200 chars, which is what keeps the poll bounded BY CONSTRUCTION rather than by
        /// a retry.</summary>
        private const int FieldRoom = 200;

        internal sealed class Row
        {
            internal string Stage, Verdict, Installation;
            internal Freshness Freshness;
            internal GateOutcome Outcome;
            /// <summary>How many times THIS run entered this stage. W11 asserts the later stages' counts stay
            /// zero when the chain stopped early, which a verdict of "-" cannot express.</summary>
            internal int Starts;
        }

        internal string GameRoot, Root, Id, Stage, FailedMember, ClaimHeld, Log;
        /// <summary>Apply's own installation lines. PROPERTIES, not fields, for the reason `Admission`'s are:
        /// nothing assigns them until Apply has a producer, and a field would be a NEW CS0649 over the gate's
        /// one known warning.</summary>
        internal string S1 { get; set; }
        internal string S2 { get; set; }
        internal long RunId, BarrierRunId;
        internal bool Busy, CancelRequested, CancelAcknowledged, BarrierArmed, ParkedForPaint;
        internal readonly Row[] Rows;

        internal LifecycleView()
        {
            Rows = new Row[LifecycleState.Sequence.Stages.Length];
            for (int i = 0; i < Rows.Length; i++)
                Rows[i] = new Row { Stage = LifecycleState.Sequence.Stages[i] };
        }

        internal Row Of(string stage)
        {
            foreach (Row r in Rows) if (r.Stage == stage) return r;
            return null;
        }

        /// <summary>"" is the poll header; a stage token, "log" or "s1s2" is one payload. Anything else is a
        /// parseable refusal - never an exception, which would reach PPCLI as a transport error.</summary>
        internal string Section(string name)
        {
            if (name == null) name = "";
            if (name == "") return Header();
            if (name == "log") return Bounded(Log, delegate(string text, bool cut)
            {
                Import.JsonWriter w = Open("log");
                return w.Key("log").Val(text).Key("bytes").Val(Len(Log)).Key("truncated").Val(cut)
                        .EndObj().ToString();
            });
            if (name == "s1s2")
            {
                Import.JsonWriter w = Open("s1s2");
                w.Key("s1"); Text(w, S1);
                w.Key("s2"); Text(w, S2);
                return w.Key("bytes").Val(Len(S1) + Len(S2)).Key("truncated").Val(false).EndObj().ToString();
            }
            Row row = Of(name);
            if (row == null)
            {
                return new Import.JsonWriter().Obj().Key("ok").Val(false).Key("section").Val(name)
                    .Key("error").Val("unknown section '" + Clip(name, 60) + "' - ask for \"\", a stage " +
                                      "name, \"log\" or \"s1s2\"").EndObj().ToString();
            }
            return Bounded(row.Verdict, delegate(string text, bool cut)
            {
                Import.JsonWriter w = Open(name);
                w.Key("stage").Val(row.Stage)
                 .Key("freshness").Val(Word(row.Freshness))
                 .Key("outcome").Val(Word(row.Outcome))
                 .Key("starts").Val(row.Starts);
                w.Key("installation"); Text(w, row.Installation);
                w.Key("verdict"); if (row.Verdict == null) w.Null(); else w.Val(text);
                return w.Key("bytes").Val(Len(row.Verdict)).Key("truncated").Val(cut).EndObj().ToString();
            });
        }

        /// <summary>The poll. Row TEXT is deliberately absent - only its length, so the caller knows whether
        /// a section is worth fetching. Every string field is clipped to <see cref="FieldRoom"/> first, which
        /// is what makes the total bounded without a retry loop.</summary>
        private string Header()
        {
            Import.JsonWriter w = Open("");
            w.Key("gameRoot").Val(Clip(GameRoot, FieldRoom))
             .Key("root").Val(Clip(Root, FieldRoom))
             .Key("id").Val(Clip(Id, FieldRoom))
             .Key("runId").Num(RunId)
             .Key("busy").Val(Busy);
            w.Key("stage"); Text(w, Stage);
            w.Key("cancelRequested").Val(CancelRequested)
             .Key("cancelAcknowledged").Val(CancelAcknowledged)
             .Key("parkedForPaint").Val(ParkedForPaint);
            w.Key("failedMember"); Text(w, Clip(FailedMember, FieldRoom));
            w.Key("claimHeld"); Text(w, Clip(ClaimHeld, FieldRoom));
            w.Key("barrierArmed").Val(BarrierArmed).Key("barrierRunId").Num(BarrierRunId);

            w.Key("rows").Arr();
            foreach (Row r in Rows)
                w.Obj().Key("stage").Val(r.Stage)
                       .Key("freshness").Val(Word(r.Freshness))
                       .Key("outcome").Val(Word(r.Outcome))
                       .Key("starts").Val(r.Starts)
                       .Key("verdictLength").Val(Len(r.Verdict))
                       .EndObj();
            w.EndArr();

            string json = w.Key("bytes").Val(0).Key("truncated").Val(false).EndObj().ToString();
            if (json.Length <= MaxPayload) return json;
            // Unreachable with the clips above; kept because "unreachable" is exactly what a 2000-char
            // silent truncation always was before someone measured it.
            return new Import.JsonWriter().Obj().Key("ok").Val(true).Key("section").Val("")
                .Key("runId").Num(RunId).Key("busy").Val(Busy).Key("bytes").Val(json.Length)
                .Key("truncated").Val(true).EndObj().ToString();
        }

        private static Import.JsonWriter Open(string section)
        {
            return new Import.JsonWriter().Obj().Key("ok").Val(true).Key("section").Val(section);
        }

        /// <summary>A string field that is genuinely absent stays null - an empty string would read as "the
        /// producer returned a blank line", which is a different fact.</summary>
        private static void Text(Import.JsonWriter w, string value)
        {
            if (value == null) w.Null(); else w.Val(value);
        }

        private static int Len(string s) { return s == null ? 0 : s.Length; }
        private static string Word(Freshness f)
        {
            return f == Freshness.Never ? "never" : f == Freshness.Stale ? "stale" : "fresh";
        }
        private static string Word(GateOutcome o)
        {
            return o == GateOutcome.Pass ? "pass" : o == GateOutcome.Fail ? "fail"
                 : o == GateOutcome.Void ? "void" : "none";
        }

        /// <summary>Composes a payload and shrinks its ONE variable-length field until the whole thing fits.
        /// ponytail: a geometric clip rather than budget arithmetic, because the escaping is what decides the
        /// final width (a newline is two chars, a control char six) and computing that exactly costs more
        /// code than the dozen iterations this takes; make it exact if a section is ever measured as hot.</summary>
        private static string Bounded(string text, Func<string, bool, string> compose)
        {
            string full = compose(text ?? "", false);
            if (full.Length <= MaxPayload || text == null) return full;
            for (int room = text.Length; room > 0; )
            {
                room = room * 3 / 4;
                string json = compose(Clip(text, room), true);
                if (json.Length <= MaxPayload) return json;
            }
            return compose("", true);
        }

        /// <summary>Cuts to a character budget, never through a surrogate pair - half of one is not a
        /// character and the reply would stop being valid UTF-8 on the way back.</summary>
        private static string Clip(string s, int room)
        {
            if (s == null || s.Length <= room) return s;
            if (room > 0 && char.IsHighSurrogate(s[room - 1])) room--;
            return s.Substring(0, room);
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

            // ---- THE `Run all` COLUMN (design:194-:204), as FIELDS. Its conditions - Bake after Validate
            // PASS, Apply after a Bake that did not FAIL, Verify after Apply, and S1 -> R30 - live here and
            // as arms of Admit below. The sequencer READS them and re-implements none of them; a second copy
            // of the graph in the coordinator is the exact drift 4.6 exists to prevent.

            /// <summary>True only while <see cref="Sequence"/> is driving. Every chain arm is guarded by it,
            /// so a STANDALONE stage is asked exactly what it was asked before Task 5.</summary>
            internal bool InRunAll { get; set; }
            /// <summary>What each earlier stage of THIS chain reported. <c>None</c> means "not reached yet",
            /// which is what refuses Verify before Apply.</summary>
            internal GateOutcome ValidateOutcome { get; set; }
            internal GateOutcome BakeOutcome { get; set; }
            internal GateOutcome ApplyOutcome { get; set; }
            /// <summary>Apply reported S1: it installed, but the game is still serving what it loaded, so
            /// nothing on disk answers for what is visible. R30, and it holds for a STANDALONE Verify too -
            /// it is a fact about this session, not about this chain. A restart clears it by ending the
            /// process; nothing else may.</summary>
            internal bool RestartRequired { get; set; }
        }

        /// <summary>
        /// What one stage's producer reported, as VALUES - the only thing <see cref="Sequence"/> is allowed
        /// to read. No text is parsed to classify anything (design:361).
        /// </summary>
        internal sealed class StageReport
        {
            internal readonly GateOutcome Outcome;
            /// <summary>The producer's own terminal line, verbatim.</summary>
            internal readonly string Verdict;
            internal readonly BakeDisposition How;
            /// <summary>Apply's S1. Feeds <see cref="Admission.RestartRequired"/>.</summary>
            internal readonly bool RestartRequired;
            /// <summary>False for a row with NO applicable gate at all - a video-only project's Apply, a
            /// Verify with nothing declared. Design:281: those are "VOID with a reason" and do NOT stop the
            /// chain, while a VOID that means "the proof is missing" does (design:279-:280).</summary>
            internal readonly bool Applicable;

            internal StageReport(GateOutcome outcome, string verdict, BakeDisposition how,
                                 bool restartRequired, bool applicable)
            {
                Outcome = outcome; Verdict = verdict; How = how;
                RestartRequired = restartRequired; Applicable = applicable;
            }
        }

        /// <summary>
        /// THE `Run all` COORDINATOR. Serial, in displayed order, stopping immediately on FAIL, refusal,
        /// acknowledged cancellation or a blocking VOID (design:278-:283).
        ///
        /// IT IS A STATE MACHINE, NOT A LOOP, and that is not a style choice: every producer is
        /// main -> worker -> main and answers frames later, so a synchronous `foreach` over the five stages
        /// could only exist by blocking the main thread on a worker - the one thing LifecycleJob's whole
        /// shape forbids. The pump calls <see cref="Next"/> for the stage to dispatch and <see cref="Report"/>
        /// when that stage completes; the offline gate calls exactly the same two methods in a tight loop.
        /// One object, two callers, one graph.
        ///
        /// It knows NOTHING about dependencies. <see cref="Admit"/> answers those, asked per stage AS IT IS
        /// REACHED (design:187), so an earlier stage's output can admit a later one.
        /// </summary>
        internal sealed class Sequence
        {
            /// <summary>Displayed order, and the panel draws them in this order too (design:293-:303).</summary>
            internal static readonly string[] Stages = { "Validate", "Bake", "Apply", "Verify", "Package" };

            private int at = -1;

            /// <summary>The stage last handed out, so <see cref="Report"/> knows whose report it is.</summary>
            internal string Current { get; private set; }
            /// <summary>The last thing a producer or an admission said. Null while nothing has run.</summary>
            internal string Terminal { get; private set; }
            /// <summary>The chain ended early. <see cref="Terminal"/> says why, in the producer's own words
            /// or in the refusal's.</summary>
            internal bool Stopped { get; private set; }
            internal bool Done { get { return Stopped || at >= Stages.Length; } }

            /// <summary>The next stage to dispatch, or null when the chain is over - stopped, or all five
            /// done. A refusal stops it and is remembered as the terminal line.</summary>
            internal string Next(Admission ctx)
            {
                if (Stopped) return null;
                if (++at >= Stages.Length) { Current = null; return null; }

                string stage = Stages[at];
                Current = stage;
                string refusal = Admit(stage, ctx);
                if (refusal == null) return stage;
                Stopped = true;
                Terminal = refusal;
                return null;
            }

            /// <summary>The report of the stage <see cref="Next"/> just handed out. Records what the later
            /// stages' admission reads, then applies the four stop rules.</summary>
            internal void Report(Admission ctx, StageReport r)
            {
                if (Stopped || Current == null || r == null) return;

                if (Current == "Validate") ctx.ValidateOutcome = r.Outcome;
                else if (Current == "Bake") ctx.BakeOutcome = r.Outcome;
                else if (Current == "Apply")
                {
                    ctx.ApplyOutcome = r.Outcome;
                    // Never cleared here: an Apply that once needed a restart still does until the process
                    // ends, and a later green row must not talk the author out of restarting.
                    if (r.RestartRequired) ctx.RestartRequired = true;
                }

                Terminal = r.Verdict;
                if (r.How == BakeDisposition.Cancelled) { Stopped = true; Terminal = StageText.R31(Current); }
                else if (r.How == BakeDisposition.Refused || r.Outcome == GateOutcome.Fail) Stopped = true;
                else if (r.Outcome == GateOutcome.Void && r.Applicable) Stopped = true;
            }
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
                case "Bake":
                    // Standalone: Bake loads and validates the manifest itself, so a `never` Validate is not
                    // a prerequisite. Inside a chain it IS one, because the chain declared the order.
                    if (ctx.InRunAll && ctx.ValidateOutcome != GateOutcome.Pass)
                        return StageText.R28All("Bake", "Validate");
                    return null;

                case "Apply":
                    // NEVER R28 for a stale or absent bake: ApplyProject bakes on a stale or missing key
                    // ITSELF (Route7.cs:311-:351) and that bake reports through the same producer, filling
                    // the Bake row. Refusing here would block the one path that repairs the thing it is
                    // refusing over.
                    if (ctx.LegacyDiskActive) return StageText.R36();
                    if (ctx.WriteOutsideRoots) return StageText.R34();
                    if (!string.IsNullOrEmpty(ctx.RetryHint)) return StageText.R29(ctx.ProjectId, ctx.RetryHint);
                    // "after a Bake that did not FAIL" - a VOID Bake (nothing to bake, no own bundle) is not
                    // a failure and does not block the install of what IS on disk.
                    if (ctx.InRunAll && ctx.BakeOutcome == GateOutcome.Fail)
                        return StageText.R28All("Apply", "Bake");
                    return null;

                case "Verify":
                    // S1 -> R30, and it is asked FIRST because it outranks every disk answer: the copies can
                    // be fresh, complete and correct while the game is still serving what it loaded, and a
                    // Verify run over them would prove a revision nobody can see.
                    if (ctx.RestartRequired) return StageText.R30(ctx.ProjectId);
                    if (ctx.InRunAll && ctx.ApplyOutcome == GateOutcome.None)
                        return StageText.R28All("Verify", "Apply");
                    // The one stage that READS evidence it cannot regenerate: it measures the copies on
                    // disk. Absent or stale, there is nothing to measure and a verdict would be invented.
                    return ctx.Copies == Freshness.Fresh
                        ? null
                        : StageText.R28("Verify", "patched copies", ctx.Copies);

                default:
                    // Validate re-derives its own receipts; Package's payload and empty-destination refusals
                    // belong to Package.Run alone (Package.cs:78) and are not restated here. "All" is
                    // admitted and re-asked per stage as the sequencer reaches it. Package needs no chain
                    // arm either: it is only ever reached when nothing stopped the chain before it.
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
        /// NOTHING DECLARED IS NOTHING TO VERIFY, and it is answered FIRST. A video-only project's rows are
        /// served live by ct_video, so `Route7.cs:157`/`:163` leave `wantReplace` false and never call
        /// `ApplyProject`: no key is ever written, no patched directory ever appears, and this answered
        /// `never` forever - `Admit("Verify")` returned R28 over evidence that will never exist, on every
        /// launch. An empty census has nothing to be stale about either, so `fresh` is the honest answer and
        /// Verify says so with <c>StageText.S8</c>.</summary>
        internal static Freshness Fresh(FreshnessObservation o)
        {
            if (o == null) return Freshness.Never;
            if (o.Declared.Length == 0) return Freshness.Fresh;
            if (!o.CacheDirExists) return Freshness.Never;
            if (o.HaveAll) return Freshness.Fresh;
            return o.MissingCopies.Length == o.Declared.Length ? Freshness.Never : Freshness.Stale;
        }
    }
}
