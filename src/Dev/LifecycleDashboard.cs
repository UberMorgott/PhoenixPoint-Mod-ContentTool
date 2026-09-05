using System;
using System.IO;
using Morgott.ContentTool.Bake;
using Morgott.ContentTool.Import;
using Morgott.ContentTool.Project;

namespace Morgott.ContentTool.Dev
{
    /// <summary>
    /// THE LIFECYCLE SEAM: one dispatch path for the panel's buttons and for PPCLI, and a run that survives
    /// a closed bench.
    ///
    /// WHY EVERY METHOD HERE IS `public static` AND RETURNS `string`. The transport is fixed by PPCLI, not
    /// chosen. `Reflect.Invoke` filters to STATIC members when no target is given (`PPCLI/src/Reflect.cs:479`),
    /// so an instance method is unreachable by construction; and `Reflect.Project` (:1080) never enumerates
    /// or walks properties, so a snapshot OBJECT would arrive as one `{h, type}` handle nobody can read.
    /// The payload is therefore bounded JSON text, composed with the mod's own `JsonWriter` - no JSON
    /// dependency enters the tool - and SECTIONED, because `Protocol.Clip` truncates at 2000 chars
    /// (`PPCLI/src/Protocol.cs:56`) silently and mid-token. `LifecycleView` (Bake/LifecycleState.cs) owns
    /// that composition, so the offline gate can prove the bounds without Unity.
    ///
    /// THE RUN-HANDLE PROTOCOL. `Run` returns the accepted `runId`; every later poll compares
    /// `Snapshot("").runId` against it, so a poll can never read a NEWER run's state and call it this run's
    /// result. `LifecycleRun` (offline-tested, G4) is what enforces it.
    ///
    /// THIS CLASS OWNS NO VERDICT. Every line it publishes is a string a producer returned; it stores,
    /// bounds and hands them over. Task 6 adds the drawing beside this and changes none of it.
    /// </summary>
    public static class LifecycleDashboard
    {
        /// <summary>Beside the mod's own DLL, the opt-in that `Acceptance` requires. Same shape and same
        /// reason as PPCLI's `ppcli-enabled`: the mod cannot know which installation is "the test instance",
        /// and a fixture-creating RPC must never be reachable in the game the owner actually plays.</summary>
        private const string AcceptanceMarker = "ct-acceptance-enabled";

        private static readonly LifecycleView view = new LifecycleView();
        /// <summary>Lives across frames: the `Run all` column's fields are receipts of THIS chain, and
        /// rebuilding the object per call would forget them between two stages.</summary>
        private static readonly LifecycleState.Admission ctx = new LifecycleState.Admission();

        private static string root, id;
        private static LifecycleJob.Captured captured;
        private static LifecycleState.Sequence chain;
        private static long dispatched, harvested;
        private static string log;

        // ---- the RPC surface ---------------------------------------------------------------------------

        /// <summary>
        /// Selects ONE project by name, or clears the selection with "".
        ///
        /// The empty name is answered HERE and never handed to `ContentMods.ProjectDir`, whose empty-name
        /// default is Sample (`ContentMods.cs:153`-`:154`) - "clear the selection" would otherwise silently
        /// select a project. A unique name resolves to a canonical ROOT before `LoadDeclared`, which takes a
        /// root holding ppcontent.json and not a name (`ContentProject.cs:289`); an ambiguous one - a sibling
        /// mod AND one of our own subfolders answering to it - is rejected rather than silently preferred,
        /// because `ProjectDir`'s sibling-wins precedence is right for a console verb and wrong for a picker
        /// that has to say WHICH root it bound.
        /// </summary>
        public static string Open(string projectName)
        {
            try
            {
                if (Busy) return Opened(false, root, id, StageText.R26(LifecycleJob.Run.Latest.Stage));
                if (string.IsNullOrEmpty(projectName)) { Bind(null, null); return Opened(true, "", "", null); }

                string mods = ContentToolMain.ModDir;
                string sibling = ContentMods.Sibling(mods, projectName);
                string own = string.IsNullOrEmpty(mods) ? null : Path.Combine(mods, projectName);
                bool ownHas = own != null && File.Exists(Path.Combine(own, ContentMods.Manifest));
                if (sibling != null && ownHas)
                    return Opened(false, "", "", "'" + projectName + "' names two projects - " + sibling +
                                                 " and " + own + "; rename one of them.");
                string found = sibling ?? (ownHas ? own : null);
                if (found == null)
                    return Opened(false, "", "", "no " + ContentMods.Manifest + " for '" + projectName + "'.");

                found = Path.GetFullPath(found);
                string modId = ContentProject.LoadDeclared(found).Id;
                Bind(found, modId);
                return Opened(true, found, modId, null);
            }
            catch (Exception ex) { return Opened(false, "", "", ex.GetType().Name + ": " + ex.Message); }
        }

        /// <summary>
        /// Enqueues the same intent the button does, and returns PROMPTLY - it never performs a synchronous
        /// Apply from the RPC call. The accepted tokens are exactly Validate, Bake, Apply, Verify, Package
        /// and All; anything else is R33, answered by `Admit` and not by a second list here.
        /// </summary>
        public static string Run(string stage)
        {
            try
            {
                string refusal = LifecycleState.Admit(stage, Refresh(stage == "All"));
                if (refusal != null) return Started(false, 0, refusal);

                if (stage == "All")
                {
                    chain = new LifecycleState.Sequence();
                    ctx.InRunAll = true;
                    ctx.ValidateOutcome = ctx.BakeOutcome = ctx.ApplyOutcome = GateOutcome.None;
                    string next = chain.Next(ctx);
                    if (next == null) { log = chain.Terminal; return Started(false, 0, chain.Terminal); }
                    return Dispatch(next);
                }
                chain = null;
                ctx.InRunAll = false;
                return Dispatch(stage);
            }
            catch (Exception ex) { return Started(false, 0, ex.GetType().Name + ": " + ex.Message); }
        }

        /// <summary>A REQUEST, not a completion: busy stays set until the producer says what happened, and a
        /// publication that already began finishes. Repeating it is one request (G4).</summary>
        public static string Cancel()
        {
            LifecycleRun.Snapshot now = LifecycleJob.Run.Latest;
            // NOTHING TO CANCEL IS NOT AN ACCEPTED CANCEL. `ok:true, acknowledged:false` is exactly what a
            // cancel the producer lost the race to looks like, so answering it with no run at all left a
            // caller polling for an acknowledgement that will never come.
            if (!now.Busy) return Cancelled(false, now.RunId, false, "nothing is running.");
            // The CHAIN is not touched here: it stops when the cancelled stage REPORTS `Cancelled` through
            // the pump, which is the producer saying so - a cancel the producer lost the race to is a
            // request, never an outcome (LifecycleRun's rule 3).
            LifecycleJob.Cancel();
            return Cancelled(true, now.RunId, LifecycleJob.Run.Latest.CancelAcknowledged, null);
        }

        /// <summary>Observational, and it cannot validate, apply or clear anything. "" is the poll header;
        /// a stage token, "log" or "s1s2" is one verbatim payload.</summary>
        public static string Snapshot(string section)
        {
            try
            {
                LifecycleRun.Snapshot now = LifecycleJob.Run.Latest;
                view.GameRoot = GameRoot();
                view.Root = root;
                view.Id = id;
                view.RunId = now.RunId;
                // BUSY UNTIL THE RESULT IS SERVED, not until the producer stopped. Between `Run.Complete`
                // and the next `Pump` the row and the log still hold the PREVIOUS run's answer (or none at
                // all, which is what a synchronous refusal looked like), so `busy:false` there invited a
                // poller to read a stale row as this run's verdict.
                view.Busy = now.Busy || Pending(now);
                view.Stage = now.Stage;
                view.CancelRequested = now.CancelRequested;
                view.CancelAcknowledged = now.CancelAcknowledged;
                view.ParkedForPaint = LifecycleJob.ParkedForPaint;
                view.FailedMember = Route7.IsFailed(id) ? id : null;
                view.ClaimHeld = HeldDir();
                view.BarrierParked = LifecycleJob.Barrier.Parked;
                view.BarrierRunId = LifecycleJob.Barrier.ParkedRunId;
                view.Log = log;
                return view.Section(section);
            }
            catch (Exception ex)
            {
                return new JsonWriter().Obj().Key("ok").Val(false).Key("section").Val(section ?? "")
                    .Key("error").Val(ex.GetType().Name + ": " + ex.Message).EndObj().ToString();
            }
        }

        /// <summary>
        /// TEST-INSTANCE ONLY, gated by a marker file the shipped mod never carries. Scenarios drive the
        /// PUBLIC seam and the real producers - they never install a fabricated PASS/FAIL, never set
        /// `Failed`, residency, `Holds` or a verdict field.
        /// </summary>
        public static string Acceptance(string scenario)
        {
            string mods = ContentToolMain.ModDir;
            if (string.IsNullOrEmpty(mods) || !File.Exists(Path.Combine(mods, AcceptanceMarker)))
                return Accepted(scenario, "refused: " + GameRoot() + " is not an acceptance instance - " +
                                          "create '" + AcceptanceMarker + "' beside the mod DLL to arm it.");
            try
            {
                if (scenario == "arm-cancel-bake")
                {
                    // ARMS AND RETURNS AT ONCE. It never waits for the completion that Cancel is what
                    // produces - v1's row deadlocked on exactly that.
                    LifecycleJob.Barrier.Arm();
                    return Accepted(scenario, null);
                }
                // ponytail: `prepare`, `resident`, `change-source`, `ship` and `enable-resident` need real
                // fixture ASSETS (a .glb, a shipped bundle target to contest) and the Doctor's own SHIP
                // path; none of that is decidable here without inventing content, and a DashboardValid that
                // is subtly wrong would fail W9-W12 as a fixture bug wearing a product bug's clothes.
                // They belong with the rows that consume them.
                return Accepted(scenario, "scenario '" + scenario + "' is not implemented yet - " +
                                          "'arm-cancel-bake' is.");
            }
            catch (Exception ex) { return Accepted(scenario, ex.GetType().Name + ": " + ex.Message); }
        }

        // ---- the pump ----------------------------------------------------------------------------------

        /// <summary>
        /// CALLED EVERY FRAME BY `FitBench.Update`, open or closed. It runs the parked main segment when the
        /// closed-window policy allows it, then harvests one completed run - and, inside `Run all`, asks the
        /// sequencer for the next stage.
        ///
        /// <paramref name="panelReady"/> is "the panel is open and has painted". Until Task 6 draws the tab
        /// the bench's own openness is the honest answer to that.
        /// </summary>
        internal static void Pump(bool panelReady)
        {
            LifecycleJob.PumpRegistered = true;
            LifecycleJob.Tick(panelReady);

            LifecycleRun.Snapshot now = LifecycleJob.Run.Latest;
            if (now.Busy || now.RunId == 0 || now.RunId == harvested || now.RunId != dispatched) return;
            harvested = now.RunId;

            LifecycleView.Row row = view.Of(now.Stage);
            if (row != null)
            {
                row.Verdict = now.Result;
                row.Outcome = Outcome(now.How);
                row.Freshness = LifecycleState.Fresh(LifecycleJob.Seen);
            }
            log = now.Result;

            if (chain == null) return;
            chain.Report(ctx, new LifecycleState.StageReport(Outcome(now.How), now.Result, now.How,
                                                             false, true, now.Eligibility));
            string next = chain.Next(Refresh(true));
            // A CHAIN THAT STOPPED HAS TO SAY SO SOMEWHERE. `Next` returns null both when the five stages
            // are done and when an ADMISSION refused one, and the refusal is only in `chain.Terminal` -
            // dropping it left a `Run all` that stopped at Verify's R28 reporting nowhere at all.
            if (next == null) { if (chain.Stopped) log = chain.Terminal; return; }
            Dispatch(next);
        }

        // ---- the plumbing ------------------------------------------------------------------------------

        private static bool Busy
        {
            get { LifecycleRun.Snapshot now = LifecycleJob.Run.Latest; return now.Busy || Pending(now); }
        }

        /// <summary>A run the producer has finished but the pump has not yet moved into the row and the log.
        /// It counts as busy everywhere the seam is observed or admitted, so a new run cannot evict a result
        /// nobody has been served.</summary>
        private static bool Pending(LifecycleRun.Snapshot now)
        {
            return !now.Busy && now.RunId != 0 && now.RunId == dispatched && now.RunId != harvested;
        }

        private static void Bind(string newRoot, string newId)
        {
            root = newRoot;
            id = newId;
            captured = null;
            // D: the freshness memory belongs to the project that was baked, not to the panel. Carrying it
            // into the next selection would admit Verify on B with A's copies.
            LifecycleJob.Look(null);
            chain = null;
            log = null;
            foreach (LifecycleView.Row r in view.Rows)
            {
                r.Verdict = null; r.Installation = null; r.Starts = 0;
                r.Outcome = GateOutcome.None; r.Freshness = Freshness.Never;
            }
            ctx.InRunAll = false;
            ctx.ValidateOutcome = ctx.BakeOutcome = ctx.ApplyOutcome = GateOutcome.None;
            ctx.RestartRequired = false;
        }

        /// <summary>
        /// Everything admission is allowed to know, measured now - Unity facts included. The chain's own
        /// fields are NOT touched here; they are receipts of this run and only `Sequence.Report` writes them.
        ///
        /// THE CAPTURE IS TAKEN HERE, BEFORE EVERY RUN, and never reused across one: a manifest error the
        /// first Bake captured was refused forever after the author fixed the file, and the declared-bundle
        /// list went stale with it. The freshness observation is taken from THAT capture for the same
        /// reason, one project at a time.
        /// </summary>
        private static LifecycleState.Admission Refresh(bool inChain)
        {
            LifecycleRun.Snapshot now = LifecycleJob.Run.Latest;
            ctx.Selection = string.IsNullOrEmpty(root) ? LifecycleState.Selection.None
                          : Directory.Exists(root) ? LifecycleState.Selection.Ok
                          : LifecycleState.Selection.Unavailable;
            ctx.RunningStage = now.Busy || Pending(now) ? now.Stage : null;
            ctx.ProjectId = id;
            ctx.RetryHint = Route7.IsFailed(id) ? Route7.RetryHint(root) : null;
            captured = ctx.Selection == LifecycleState.Selection.Ok ? LifecycleJob.Capture(root) : null;
            ctx.Copies = LifecycleState.Fresh(LifecycleJob.Look(captured));
            ctx.LegacyDiskActive = Route7.LegacyDiskActive(id);
            ctx.WriteOutsideRoots = OutsideRoots();
            ctx.InRunAll = inChain;
            return ctx;
        }

        /// <summary>
        /// R34, from the ONE thing Apply's destinations are derived from: `ProjectBake.OutputDirs` is the
        /// only owner of them (ProjectBake.cs), and both must land under the mod manager's own patched root
        /// or under the author's project. A mod id or a root carrying `..` escapes both - which is the write
        /// this refuses BEFORE anything is opened, rather than after.
        /// </summary>
        private static bool OutsideRoots()
        {
            if (captured == null || captured.OutputDirs == null) return false;
            try
            {
                string patched = Norm(ContentToolMain.PatchedRoot), project = Norm(root);
                foreach (string dir in captured.OutputDirs)
                {
                    string at = Norm(dir);
                    if (!at.StartsWith(patched, StringComparison.OrdinalIgnoreCase) &&
                        !at.StartsWith(project, StringComparison.OrdinalIgnoreCase)) return true;
                }
                return false;
            }
            // An unresolvable path is not a proven escape, and refusing Apply over one would be a guess.
            catch (Exception) { return false; }
        }

        private static string Norm(string path) { return Path.GetFullPath(path).TrimEnd('\\', '/'); }

        /// <summary>MAIN. Hands the stage - and the capture `Refresh` just took - to the one dispatcher the
        /// buttons will use too.</summary>
        private static string Dispatch(string stage)
        {
            if (captured == null) captured = LifecycleJob.Capture(root);
            LifecycleView.Row row = view.Of(stage);
            string refusal = LifecycleJob.Start(stage, captured);
            if (refusal != null)
            {
                // THE ROW SAYS IT, not just the return value: a button press and a chain step both land
                // here, and a refusal that only went back down the wire left the row blank. VOID, because
                // nothing was proven and nothing failed - and `Starts` stays where it was, since a stage
                // that was refused never entered.
                if (row != null) { row.Verdict = refusal; row.Outcome = GateOutcome.Void; }
                log = refusal;
                // null eligibility, explicitly: a stage that was REFUSED never asked the mod manager.
                if (chain != null) chain.Report(ctx, new LifecycleState.StageReport(
                    GateOutcome.Void, refusal, BakeDisposition.Refused, false, true, null));
                return Started(false, 0, refusal);
            }
            dispatched = LifecycleJob.Run.Latest.RunId;
            if (row != null) row.Starts++;
            return Started(true, dispatched, null);
        }

        /// <summary>The producer's disposition, never its text (design:361). `Refused` and `Cancelled` are
        /// VOID rows: nothing was proven and nothing failed.</summary>
        private static GateOutcome Outcome(BakeDisposition how)
        {
            return how == BakeDisposition.Success ? GateOutcome.Pass
                 : how == BakeDisposition.Failed ? GateOutcome.Fail
                 : GateOutcome.Void;
        }

        private static string GameRoot()
        {
            try { return Path.GetFullPath(Path.Combine(UnityEngine.Application.dataPath, "..")); }
            catch (Exception) { return ""; }
        }

        private static string HeldDir()
        {
            if (captured == null || captured.OutputDirs == null) return null;
            foreach (string dir in captured.OutputDirs) if (OutputClaim.Held(dir)) return dir;
            return null;
        }

        private static string Opened(bool ok, string at, string modId, string error)
        {
            JsonWriter w = new JsonWriter().Obj().Key("ok").Val(ok).Key("root").Val(at ?? "")
                                                 .Key("id").Val(modId ?? "");
            w.Key("error"); if (error == null) w.Null(); else w.Val(error);
            return w.EndObj().ToString();
        }

        private static string Started(bool ok, long runId, string refusal)
        {
            JsonWriter w = new JsonWriter().Obj().Key("ok").Val(ok).Key("runId").Num(runId);
            w.Key("refusal"); if (refusal == null) w.Null(); else w.Val(refusal);
            return w.EndObj().ToString();
        }

        private static string Cancelled(bool ok, long runId, bool acknowledged, string error)
        {
            JsonWriter w = new JsonWriter().Obj().Key("ok").Val(ok).Key("runId").Num(runId)
                                                 .Key("acknowledged").Val(acknowledged);
            w.Key("error"); if (error == null) w.Null(); else w.Val(error);
            return w.EndObj().ToString();
        }

        private static string Accepted(string scenario, string error)
        {
            JsonWriter w = new JsonWriter().Obj().Key("ok").Val(error == null)
                                                 .Key("scenario").Val(scenario ?? "");
            w.Key("error"); if (error == null) w.Null(); else w.Val(error);
            return w.EndObj().ToString();
        }
    }
}
