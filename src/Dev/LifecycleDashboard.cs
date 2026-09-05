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
                    if (next == null) return Started(false, 0, chain.Terminal);
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
            // The CHAIN is not touched here: it stops when the cancelled stage REPORTS `Cancelled` through
            // the pump, which is the producer saying so - a cancel the producer lost the race to is a
            // request, never an outcome (LifecycleRun's rule 3).
            LifecycleJob.Cancel();
            return new JsonWriter().Obj().Key("ok").Val(true)
                .Key("runId").Num(now.RunId)
                .Key("acknowledged").Val(LifecycleJob.Run.Latest.CancelAcknowledged)
                .EndObj().ToString();
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
                view.Busy = now.Busy;
                view.Stage = now.Stage;
                view.CancelRequested = now.CancelRequested;
                view.CancelAcknowledged = now.CancelAcknowledged;
                view.ParkedForPaint = LifecycleJob.ParkedForPaint;
                view.FailedMember = Route7.IsFailed(id) ? id : null;
                view.ClaimHeld = HeldDir();
                view.BarrierArmed = LifecycleJob.Barrier.Parked;
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
                                                             false, true));
            string next = chain.Next(Refresh(true));
            if (next != null) Dispatch(next);
        }

        // ---- the plumbing ------------------------------------------------------------------------------

        private static bool Busy { get { return LifecycleJob.Run.Latest.Busy; } }

        private static void Bind(string newRoot, string newId)
        {
            root = newRoot;
            id = newId;
            captured = null;
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

        /// <summary>Everything admission is allowed to know, measured now. The chain's own fields are NOT
        /// touched here - they are receipts of this run and only `Sequence.Report` writes them.</summary>
        private static LifecycleState.Admission Refresh(bool inChain)
        {
            LifecycleRun.Snapshot now = LifecycleJob.Run.Latest;
            ctx.Selection = string.IsNullOrEmpty(root) ? LifecycleState.Selection.None
                          : Directory.Exists(root) ? LifecycleState.Selection.Ok
                          : LifecycleState.Selection.Unavailable;
            ctx.RunningStage = now.Busy ? now.Stage : null;
            ctx.ProjectId = id;
            ctx.RetryHint = Route7.IsFailed(id) ? Route7.RetryHint(root) : null;
            ctx.Copies = LifecycleState.Fresh(LifecycleJob.Seen);
            ctx.InRunAll = inChain;
            return ctx;
        }

        /// <summary>MAIN. Captures the Unity-derived facts, then hands the stage to the one dispatcher the
        /// buttons will use too.</summary>
        private static string Dispatch(string stage)
        {
            if (captured == null || captured.Root != root) captured = LifecycleJob.Capture(root);
            LifecycleView.Row row = view.Of(stage);
            string refusal = LifecycleJob.Start(stage, captured);
            if (refusal != null)
            {
                if (chain != null) chain.Report(ctx, new LifecycleState.StageReport(
                    GateOutcome.Void, refusal, BakeDisposition.Refused, false, true));
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

        private static string Accepted(string scenario, string error)
        {
            JsonWriter w = new JsonWriter().Obj().Key("ok").Val(error == null)
                                                 .Key("scenario").Val(scenario ?? "");
            w.Key("error"); if (error == null) w.Null(); else w.Val(error);
            return w.EndObj().ToString();
        }
    }
}
