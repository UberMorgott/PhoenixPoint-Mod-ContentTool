using System;
using System.Collections.Generic;
using System.IO;
using Morgott.ContentTool.Bake;
using Morgott.ContentTool.Import;
using Morgott.ContentTool.Project;
using UnityEngine;

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
                // THE PANEL'S LINE, taken here because a button press ends in this return too: an admission
                // refusal that only went back down the wire left the panel silent about the press.
                if (refusal != null) { message = refusal; return Started(false, 0, refusal); }

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
                if (scenario == "prepare") return Accepted(scenario, Prepare(mods));
                if (scenario == "change-source") return Accepted(scenario, ChangeSource());
                if (scenario == "resident") return Accepted(scenario, Resident(mods));
                if (scenario == "enable-resident") return Accepted(scenario, EnableResident(mods));
                // ponytail: `ship` needs a Doctor carrying a loaded preview and its `made.Root`
                // (ModelDoctor.cs:653), which Task 7 Step 1 is what wires. Driving the SHIP path without
                // it would mean fabricating the very state the row is supposed to measure.
                if (scenario == "ship")
                    return Accepted(scenario, "scenario 'ship' needs the Doctor's loaded preview and its " +
                                              "made.Root, which Task 7 Step 1 wires - it is not decidable " +
                                              "here yet.");
                return Accepted(scenario, "unknown scenario '" + scenario + "' - 'prepare', " +
                                          "'change-source', 'resident', 'enable-resident' and " +
                                          "'arm-cancel-bake' are.");
            }
            catch (Exception ex) { return Accepted(scenario, ex.GetType().Name + ": " + ex.Message); }
        }

        // ---- the acceptance fixtures ---------------------------------------------------------------------

        /// <summary>The wizard slice's own project on the bench, and the ONLY fixture source. Nothing below
        /// invents content: every fixture is a copy of this with its id rewritten, so the assets, the rows
        /// and the shipped targets are ones that already bake.</summary>
        private const string FixtureSource = "Replace_Leftleg";

        private static string Prepare(string mods)
        {
            string source = ContentMods.Sibling(mods, FixtureSource);
            if (source == null)
                return "refused: no " + FixtureSource + " beside " + mods + " - the fixtures are forks of " +
                       "that project, so it has to be on disk first.";
            // EVERY FIXTURE DECLARES THE SAME SHIPPED BUNDLE, because every one of them forks the same
            // source - and Replace_Leftleg names exactly ONE target with exactly one asset, so retargeting
            // a fixture's bundle can only produce a MissingTarget bake failure (which is
            // DashboardPatchFail's job, not DashboardAuthor's). A previous session's `enable-resident`
            // therefore leaves a REAL claim for 'acceptance.dashboardresident' standing on that bundle, and
            // `ReadBack.Verify`'s census is PER TARGET: every other fixture then reports "the live claim is
            // 'acceptance.dashboardresident'" and VOIDs. So the standing claim is DROPPED first - through
            // the real uninstall body the checkbox calls (BundleLive.cs:148), never by editing the ledger,
            // and never touching a claim that is not one of these fixtures'.
            List<string> stale = new List<string>();
            foreach (BundleClaim c in BundleClaims.All)
                if (c.Mod != null && c.Mod.StartsWith("acceptance.", StringComparison.Ordinal) &&
                    !stale.Contains(c.Mod)) stale.Add(c.Mod);
            foreach (string mod in stale) BundleLive.Uninstall(mod);

            string why = Fork(source, "DashboardValid", null);
            // A REAL MissingTarget (ProjectBake.cs:1807), not a fabricated FAIL: the row names an asset the
            // shipped bundle does not contain, so the bake fails the way a broken project fails.
            why = why ?? Fork(source, "DashboardPatchFail",
                 delegate(string json) { return Retarget(json, "asset", "ContentToolNoSuchTarget"); });
            // ponytail: the plan words this fixture as "retargeted to a bundle no other fixture and no live
            // claim contests". Its own ID already is that: R38 asks whether THIS project's copy is being
            // served (Capture -> ProjectBake.Live over PatchedDir(id)\<bundle>), and a distinct id is a
            // distinct copy path that nothing loads while the fixture is never applied. The claim census is
            // the OTHER contest, and it is the one released above.
            return why ?? Fork(source, "DashboardAuthor", null);
        }

        /// <summary>Only the fixture that is SELECTED, and only one file of it.</summary>
        private static string ChangeSource()
        {
            if (string.IsNullOrEmpty(root) || !Directory.Exists(root))
                return "refused: nothing is selected - Open a forked fixture first.";
            string name = Path.GetFileName(root.TrimEnd('\\', '/'));
            if (!name.StartsWith("Dashboard", StringComparison.Ordinal))
                return "refused: '" + name + "' is not a forked fixture, and this rewrites a source file.";
            string content = Path.Combine(root, "Content");
            string[] files = Directory.Exists(content)
                ? Directory.GetFiles(content, "*", SearchOption.AllDirectories) : new string[0];
            if (files.Length == 0) return "refused: " + content + " holds no source file to change.";
            Array.Sort(files, StringComparer.Ordinal);
            // The bytes are rewritten UNCHANGED and the stamp moves with them: `PatchCache.Key` hashes each
            // source's name, length and LastWriteTimeUtc, so the receipt goes stale by an actual key
            // comparison (Route7.Observe:150) - and the fixture stays a loadable .glb, which flipping a
            // byte inside it would not.
            File.WriteAllBytes(files[0], File.ReadAllBytes(files[0]));
            return null;
        }

        /// <summary>It does not INVENT a resident bundle - it asks which of the source project's declared
        /// targets the running game has already loaded, and refuses when none has. That refusal is the
        /// honest answer to a question about live state, not a fixture bug.</summary>
        private static string Resident(string mods)
        {
            string source = ContentMods.Sibling(mods, FixtureSource);
            if (source == null) return "refused: no " + FixtureSource + " beside " + mods + ".";
            foreach (ShippedReplacement r in ContentProject.LoadDeclared(source).Replace)
            {
                if (!string.IsNullOrEmpty(r.video) || string.IsNullOrEmpty(r.bundle)) continue;
                if (!BundleLive.ResidentNow(r.bundle)) continue;
                // THE FORK'S OWN REFUSAL, like `Prepare` returns it. Discarding it reported `ok:true` with
                // nothing prepared - a re-fork of the SELECTED fixture refuses (Fork:308) and the
                // acceptance row then read as a pass over a tree that was never made.
                return Fork(source, "DashboardResident", null);
            }
            return "refused: the game has none of " + FixtureSource + "'s declared bundles loaded right " +
                   "now, so there is no resident target to fork onto.";
        }

        /// <summary>The REAL checkbox body (`Route7.Toggle`, the one ModRoster calls), never a roster edit
        /// and never a fabricated claim. The fixture has to be on disk from a previous session, because the
        /// point of the row is a mod enabled AFTER a restart.</summary>
        private static string EnableResident(string mods)
        {
            string at = ContentMods.Sibling(mods, "DashboardResident");
            if (at == null)
                return "refused: no DashboardResident on disk - run 'resident', restart the game, then " +
                       "ask again.";
            Route7.Toggle(at, true);
            return null;
        }

        /// <summary>A fixture is a COPY of a real project carrying its own id, so two of them can be baked,
        /// applied and claimed without contesting each other. <paramref name="mutate"/> edits the manifest
        /// TEXT, which is how a fixture that must fail gets its defect - a real row naming a real absence.
        /// Returns a refusal, or null when the fork was made.</summary>
        private static string Fork(string source, string name, Func<string, string> mutate)
        {
            string at = Path.Combine(Directory.GetParent(source).FullName, name);
            // A RE-FORK DELETES THE TREE, and the panel may be BOUND to it: `root` and the capture taken
            // from it come from an earlier Open of this same fixture, and deleting the folder out from
            // under them left the selection Unavailable mid-suite - R27 over a project the scenario had
            // just been told to rebuild. The author clears the selection first; this refuses rather than
            // clearing it for them, because Open("") is theirs to press.
            if (Under(root, at))
                return "refused: '" + name + "' is the selected project (" + at + ") - re-forking it " +
                       "would delete the tree the panel is bound to. Open(\"\") first, then prepare.";
            if (Directory.Exists(at)) Directory.Delete(at, true);
            Copy(source, at);
            string manifest = Path.Combine(at, ContentMods.Manifest);
            string json = Retarget(File.ReadAllText(manifest), "id", "acceptance." + name.ToLowerInvariant());
            File.WriteAllText(manifest, mutate == null ? json : mutate(json));
            return null;
        }

        /// <summary>Is <paramref name="path"/> that directory, or inside it? Canonical and case-blind, like
        /// every other path comparison on this route. An unresolvable path is not a proven overlap.</summary>
        private static bool Under(string path, string dir)
        {
            if (string.IsNullOrEmpty(path)) return false;
            try
            {
                string a = Norm(path), b = Norm(dir);
                return a.Equals(b, StringComparison.OrdinalIgnoreCase) ||
                       a.StartsWith(b + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception) { return false; }
        }

        /// <summary>Rewrites the FIRST value of a named string field, in place, leaving every other byte of
        /// the manifest as the author wrote it.</summary>
        private static string Retarget(string json, string field, string value)
        {
            int key = json.IndexOf("\"" + field + "\"", StringComparison.Ordinal);
            if (key < 0)
                throw new InvalidDataException("no \"" + field + "\" in " + ContentMods.Manifest);
            int open = json.IndexOf('"', json.IndexOf(':', key) + 1);
            int close = json.IndexOf('"', open + 1);
            return json.Substring(0, open + 1) + value + json.Substring(close);
        }

        private static void Copy(string from, string to)
        {
            Directory.CreateDirectory(to);
            foreach (string f in Directory.GetFiles(from))
                File.Copy(f, Path.Combine(to, Path.GetFileName(f)), true);
            foreach (string d in Directory.GetDirectories(from))
                Copy(d, Path.Combine(to, Path.GetFileName(d)));
        }

        // ---- the pump ----------------------------------------------------------------------------------

        /// <summary>
        /// CALLED EVERY FRAME BY `FitBench.Update`, open or closed. It runs the parked main segment when the
        /// closed-window policy allows it, then harvests one completed run - and, inside `Run all`, asks the
        /// sequencer for the next stage.
        ///
        /// <paramref name="panelReady"/> is the bench's half of "the panel is open and has painted" - the
        /// Lifecycle tab is the selected one. <see cref="Painted"/> is the other half, and it is ANDed HERE
        /// rather than at the call site so the paint gate cannot be forgotten by a second caller.
        /// </summary>
        internal static void Pump(bool panelReady)
        {
            Drain();
            LifecycleJob.PumpRegistered = true;
            LifecycleJob.Tick(panelReady && Painted);

            LifecycleRun.Snapshot now = LifecycleJob.Run.Latest;
            if (now.Busy || now.RunId == 0 || now.RunId == harvested || now.RunId != dispatched) return;
            harvested = now.RunId;

            LifecycleView.Row row = view.Of(now.Stage);
            if (row != null)
            {
                row.Verdict = now.Result;
                row.Outcome = LifecycleState.Outcome(now.Outcome, now.How);
                row.Freshness = LifecycleState.Fresh(LifecycleJob.Seen);
            }
            // The producer's gate log when it published one - Verify's FAIL/VOID lines are what its
            // verdict points at - and the verdict itself for every stage whose verdict is the whole of
            // what it measured.
            log = now.Log ?? now.Result;

            // S1 IS A FACT ABOUT THE SESSION, NOT ABOUT A CHAIN (LifecycleState.cs:443). A STANDALONE Apply
            // never reaches `Sequence.Report`, so setting it only there left the button path's Verify
            // admitted after an apply the game is not yet serving. Never cleared here: only `Bind` does.
            if (now.RestartRequired) ctx.RestartRequired = true;

            if (chain == null) return;
            chain.Report(ctx, new LifecycleState.StageReport(
                                  LifecycleState.Outcome(now.Outcome, now.How), now.Result, now.How,
                                  now.RestartRequired, now.Applicable, now.Eligibility));
            string next = chain.Next(Refresh(true));
            // A CHAIN THAT STOPPED HAS TO SAY SO SOMEWHERE. `Next` returns null both when the five stages
            // are done and when an ADMISSION refused one, and the refusal is only in `chain.Terminal` -
            // dropping it left a `Run all` that stopped at Verify's R28 reporting nowhere at all.
            if (next == null) { if (chain.Stopped) log = chain.Terminal; return; }
            Dispatch(next);
        }

        // ---- the panel ---------------------------------------------------------------------------------

        /// <summary>The canonical project roots the selector offers, and the label each one shows.</summary>
        private static string[] roots = new string[0], labels = new string[0];
        /// <summary>Index into <see cref="roots"/> of the bound project, or -1: NOT a second selection.
        /// `root` is the binding; this is only where the arrows currently stand, and a `Refresh` that no
        /// longer finds the bound root leaves it at -1 rather than sliding the selection onto a neighbour.</summary>
        private static int chosen = -1;
        private static bool rescan = true;

        /// <summary>The panel's transient line - a refusal, a queued stage, a cancel note. NEVER a verdict:
        /// those live in the rows, and only a producer writes one.</summary>
        private static string message;

        /// <summary>A press, taken during a layout pass and acted on by <see cref="Drain"/> one frame later.
        /// Same discipline as the Doctor's and the slim panel's intent queues: starting a producer between
        /// IMGUI's Layout and Repaint passes edits the very state the Repaint is about to lay out.</summary>
        private static string intent;
        private static int select = int.MinValue;

        private static UnityEngine.Vector2 tailScroll;
        private static int paintedFrame = -2;

        /// <summary>The panel has PAINTED within a frame of now - what a blocking main segment waits for
        /// (design:323-:333), and the same two-frame shape as SHIP's arming gate (ModelDoctor.cs:443).</summary>
        private static bool Painted { get { return UnityEngine.Time.frameCount - paintedFrame <= 1; } }

        /// <summary>
        /// MAIN, from FitBench's Lifecycle tab. It DRAWS and it records presses; it decides nothing. Every
        /// line is a producer's string, a <see cref="StageText"/> line or a placeholder, and the control
        /// sequence is CONSTANT - five rows, both buttons and the tail exist before anything has ever run,
        /// disabled rather than absent, so nothing about the layout moves when a result arrives.
        /// </summary>
        internal static void Draw()
        {
            if (UnityEngine.Event.current.type == UnityEngine.EventType.Repaint)
                paintedFrame = UnityEngine.Time.frameCount;

            LifecycleRun.Snapshot now = LifecycleJob.Run.Latest;
            bool owned = now.Busy || Pending(now);

            GUILayout.BeginHorizontal();
            GUILayout.Label("Project", GUILayout.Width(60f));
            GUI.enabled = !owned && roots.Length > 0;
            if (GUILayout.Button("<", GUILayout.Width(26f))) select = chosen - 1;
            GUI.enabled = true;
            GUILayout.Label(chosen >= 0 && chosen < labels.Length ? labels[chosen] : "(none)");
            GUI.enabled = !owned && roots.Length > 0;
            if (GUILayout.Button(">", GUILayout.Width(26f))) select = chosen + 1;
            GUI.enabled = !owned;
            if (GUILayout.Button("Refresh", GUILayout.Width(80f))) rescan = true;
            GUI.enabled = true;
            GUILayout.EndHorizontal();

            GUILayout.Label("Session  " + (now.Busy
                ? now.CancelRequested ? StageText.CancelRequested(now.Stage) : StageText.Running(now.Stage)
                : "Ready."));

            foreach (LifecycleView.Row r in view.Rows)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label(r.Stage, GUILayout.Width(70f));
                GUILayout.Label(LifecycleView.Word(r.Freshness), GUILayout.Width(56f));
                GUILayout.Label(LifecycleView.Word(r.Outcome), GUILayout.Width(48f));
                GUILayout.Label(Dash(r.Installation), GUILayout.Width(150f));
                GUI.enabled = !owned;
                if (GUILayout.Button("Run", GUILayout.Width(60f))) intent = r.Stage;
                GUI.enabled = true;
                GUILayout.EndHorizontal();
                // The row's OWN verdict, never the tail's last line: the two answer different questions and
                // reading one for the other is how a panel invents a verdict.
                GUILayout.Label("  " + Dash(r.Verdict));
            }

            SlimProgress p = now.Progress;
            GUILayout.BeginHorizontal();
            GUILayout.Label("Progress", GUILayout.Width(60f));
            float done = p == null || p.Total <= 0 ? 0f : (float)p.Done / p.Total;
            // A FIXED TRACK with the fill inside it, so the phase label beside it does not walk left and
            // right as the bar grows. SlimPanel.cs:270's bar, unchanged.
            GUILayout.BeginHorizontal(GUILayout.Width(240f));
            GUILayout.Box("", GUILayout.Width(Mathf.Max(1f, 240f * done)), GUILayout.Height(6f));
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            GUILayout.Label(p == null ? "—" : p.Stage + " " + p.Done + "/" + p.Total);
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUI.enabled = !owned;
            if (GUILayout.Button("Run all", GUILayout.Width(80f))) intent = "All";
            // A CANCEL IS A REQUEST, and only until one is outstanding. `owned && !busy` is the producer's
            // publication - it has stated its verdict and the pump has not served it yet - which is exactly
            // the window in which there is nothing left to interrupt.
            GUI.enabled = now.Busy && !now.CancelRequested;
            if (GUILayout.Button("Cancel", GUILayout.Width(80f))) intent = "Cancel";
            GUI.enabled = true;
            GUILayout.Label(Dash(owned && !now.Busy ? StageText.CancelUnavailable(now.Stage) : message));
            GUILayout.EndHorizontal();

            GUILayout.Label("Log tail");
            tailScroll = GUILayout.BeginScrollView(tailScroll, GUILayout.Height(120f));
            GUILayout.Label(string.IsNullOrEmpty(log) ? "—" : StageResult.Tail(log, 12));
            GUILayout.EndScrollView();
        }

        private static string Dash(string s) { return string.IsNullOrEmpty(s) ? "—" : s; }

        /// <summary>MAIN, from the pump: the enumeration and every press happen HERE, outside drawing.</summary>
        private static void Drain()
        {
            if (rescan) { rescan = false; Scan(); }
            int pick = select; select = int.MinValue;
            string want = intent; intent = null;
            // CANCEL IS THE ONE PRESS THAT BELONGS TO A RUNNING JOB, so it is answered before the busy
            // guard below rather than dropped by it.
            if (want == "Cancel") { Cancel(); return; }
            if (Busy) return;
            if (pick != int.MinValue) Choose(pick);
            if (want != null) Run(want);
        }

        /// <summary>
        /// Every root that CARRIES a manifest: ContentTool's own children, the siblings under Mods\ and the
        /// mod manager's roster (`ContentMods.Candidates`, the one enumerator the routes already share),
        /// canonicalized and deduped.
        ///
        /// Deliberately NOT `ContentMods.Enabled` and NOT `ContentToolMain.LiveProjectIds`: both answer
        /// "what has the player switched on", and an author's DISABLED project is exactly what this picker
        /// exists to reach. `LoadDeclared` when one is chosen, never the source-importing `Load` - listing
        /// projects must not decode anybody's textures.
        /// </summary>
        private static void Scan()
        {
            string mods = ContentToolMain.ModDir;
            List<string> found = new List<string>();
            try
            {
                if (!string.IsNullOrEmpty(mods) && Directory.Exists(mods))
                    foreach (string dir in Directory.GetDirectories(mods)) Offer(found, dir);
                foreach (string dir in ContentMods.Candidates(mods, ModRoster.Build())) Offer(found, dir);
            }
            catch (Exception ex) { message = "lifecycle: " + ex.GetType().Name + ": " + ex.Message; }
            found.Sort(StringComparer.OrdinalIgnoreCase);

            roots = found.ToArray();
            labels = new string[roots.Length];
            for (int i = 0; i < roots.Length; i++)
            {
                string name = Path.GetFileName(roots[i]);
                bool duplicate = false;
                for (int j = 0; j < roots.Length && !duplicate; j++)
                    duplicate = j != i &&
                        string.Equals(Path.GetFileName(roots[j]), name, StringComparison.OrdinalIgnoreCase);
                // TWO PROJECTS MAY ANSWER TO ONE NAME - a sibling mod and one of our own subfolders - and
                // the picker has to say WHICH root it bound, which is the ambiguity `Open` refuses outright.
                labels[i] = duplicate ? name + "  [" + roots[i] + "]" : name;
            }
            chosen = -1;
            for (int i = 0; i < roots.Length; i++) if (Under(root, roots[i])) { chosen = i; break; }
        }

        private static void Offer(List<string> found, string dir)
        {
            try
            {
                if (!File.Exists(Path.Combine(dir, ContentMods.Manifest))) return;
                string full = Norm(dir);
                foreach (string had in found)
                    if (had.Equals(full, StringComparison.OrdinalIgnoreCase)) return;
                found.Add(full);
            }
            // A roster entry pointing at a path this process cannot resolve is one project missing from the
            // list, never a picker that throws out of the pump.
            catch (Exception) { }
        }

        /// <summary>Binds the ABSOLUTE root, which is what Apply is handed - never a name rebuilt from the
        /// label. A manifest that will not load leaves the previous binding alone and says why.</summary>
        private static void Choose(int i)
        {
            if (roots.Length == 0) return;
            i = ((i % roots.Length) + roots.Length) % roots.Length;
            try
            {
                string modId = ContentProject.LoadDeclared(roots[i]).Id;
                chosen = i;
                Bind(roots[i], modId);
                message = null;
            }
            catch (Exception ex) { message = roots[i] + ": " + ex.Message; }
        }

        // ---- the plumbing ------------------------------------------------------------------------------

        /// <summary>Internal because the BENCH asks it too: a tab change while a run owns the job is refused
        /// at FitBench.cs's toggle row, and asking there means asking this, not a second idea of busy.</summary>
        internal static bool Busy
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
                message = refusal;
                // null eligibility, explicitly: a stage that was REFUSED never asked the mod manager.
                if (chain != null) chain.Report(ctx, new LifecycleState.StageReport(
                    GateOutcome.Void, refusal, BakeDisposition.Refused, false, true, null));
                return Started(false, 0, refusal);
            }
            dispatched = LifecycleJob.Run.Latest.RunId;
            if (row != null) row.Starts++;
            message = StageText.Queued(stage);
            return Started(true, dispatched, null);
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
