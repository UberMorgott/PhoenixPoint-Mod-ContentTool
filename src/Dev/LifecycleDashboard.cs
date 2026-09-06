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
                string refusal = LifecycleState.Admit(stage, Refresh(stage == "All", true));
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
                // THE S1 BARRIER, as a header field: it is a fact about the SESSION (`Admission`'s own,
                // which only a new process clears), so the poll must be able to see it without inferring
                // it from a row that a later stage has since overwritten.
                view.RestartRequired = ctx.RestartRequired;
                // THE DOCTOR'S SHIP ARM, as two header fields: a press accepted by `Acceptance("ship")` arms
                // on the next Tick and CANCELS ITSELF two frames later when the SHIP section is not painted,
                // and a poll waiting for the handoff cannot tell that from a bake still running. `pending`
                // is the arm, `result` is the Doctor's own line for how the last one ended - never re-worded.
                view.ShipPending = FitBench.Doctor.ShipPending;
                view.ShipResult = FitBench.Doctor.ShipResult;
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
                if (scenario == "ship")
                {
                    int gen;
                    string why = Ship(out gen);
                    return Accepted(scenario, why, why == null ? gen : -1);
                }
                return Accepted(scenario, "unknown scenario '" + scenario + "' - 'prepare', " +
                                          "'change-source', 'resident', 'enable-resident', 'ship' and " +
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
                 delegate (string json) { return Retarget(json, "asset", "ContentToolNoSuchTarget"); });
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

        /// <summary>W17. It presses the REAL button through the REAL queue - the Doctor's own
        /// <c>Enqueue("ship")</c>, its two-frame arming gate and <c>DoShip</c> - and it refuses when that
        /// press would not arm, rather than fabricating the loaded preview and the `made.Root` the row
        /// exists to measure. Nothing here installs, selects or writes a row: the handoff the press
        /// performs is what the poll then reads.</summary>
        private static string Ship(out int gen)
        {
            gen = -1;
            if (!FitBench.DoctorShowing || !FitBench.Doctor.ShipSectionShowing)
                return "refused: the bench's MODEL DOCTOR tab is not open with its SHIP section on screen " +
                       "(a file or prototype browser takes the whole area) - a press arms for two frames " +
                       "and cancels itself when that section is not painted.";
            // The BUTTON's condition, asked of the Doctor rather than restated here - a second copy of it
            // would let this scenario press something the author cannot.
            string why = FitBench.Doctor.ShipRefusal;
            if (why != null) return "refused: the SHIP button is not live - " + why + ".";
            // THE GENERATION THE PRESS BELONGS TO, handed back with the acceptance. `Enqueue` only queues an
            // intent: the arm happens on the next Tick and cancels itself two frames later if the SHIP
            // section stops painting, so "accepted" alone told a poll nothing it could wait on. With this and
            // the header's `shipPending`/`shipResult`, a dead arm is a fact the poll reads instead of a
            // handoff it waits for forever.
            gen = FitBench.Doctor.Generation;
            FitBench.Doctor.Enqueue("ship");
            return null;
        }

        // ---- the SHIP handoff ------------------------------------------------------------------------

        /// <summary>
        /// THE HANDOFF (design:341), and it runs NOTHING. A successful Doctor SHIP hands the panel the
        /// project it just made - by the absolute `made.Root` it captured, never a name rebuilt from a
        /// label - and the Apply it just performed, as VALUES: the producer's own line, the structured
        /// per-target dispositions and the aggregate. No stage is dispatched, no bake is repeated and no
        /// install happens twice; the other four rows stay `never`, because SHIP observed nothing about
        /// them.
        ///
        /// It REFUSES while a run owns the job (R26). SHIP's own claim and the lifecycle producer's are
        /// independent, so a press that landed while a run was in flight must not overwrite that run's
        /// selection and rows on its way past.
        ///
        /// Returns a refusal, or null when the panel took it.
        /// </summary>
        internal static string Handoff(string producedRoot, string applyLine,
                                       IList<Route7.TargetInstall> targets, Route7.ApplyDisposition how)
        {
            try
            {
                if (string.IsNullOrEmpty(producedRoot) || !Directory.Exists(producedRoot))
                    return "there is no project root to select.";
                LifecycleRun.Snapshot now = LifecycleJob.Run.Latest;
                if (now.Busy || Pending(now)) return StageText.R26(now.Stage);

                // EVERYTHING THAT CAN THROW HAPPENS BEFORE THE BINDING MOVES. `Bind` replaces the root, the
                // id, the rows and the chain, so a `Capture` (or a `LoadDeclared`) that threw after it left
                // the panel bound to the new project with every row cleared while this method's own return
                // told the Doctor "the Lifecycle tab was not opened" - two answers to one press.
                string full = Path.GetFullPath(producedRoot);
                string modId = ContentProject.LoadDeclared(producedRoot).Id;
                LifecycleJob.Captured taken = LifecycleJob.Capture(full);
                Bind(full, modId);
                captured = taken;
                // A PROJECT THAT DID NOT EXIST A MOMENT AGO is not in the selector's list, and the label
                // would read "(none)" over a panel whose buttons already act on it. The enumeration itself
                // happens in `Drain`, outside drawing, like every other one.
                rescan = true;

                LifecycleView.Row row = view.Of("Apply");
                row.Verdict = applyLine;
                // THE ROW IS ABOUT THE PROJECT, `how` is about the SLOT SHIP named. `ApplyRoot` narrows the
                // disposition to `forBundle` when a bundle is asked for (Route7.cs:594-:601), so a project
                // carrying a second target - which is exactly what appending a row to an existing project
                // makes - would have published this slot's PASS over a sibling that was refused, and missed
                // a restart the sibling needs. The row asks the same conservative aggregate the
                // console-shaped call gets; the Doctor keeps `how` for its own S1/S2 sentence about the one
                // slot it shipped.
                Route7.ApplyDisposition project = Route7.Aggregate(targets);
                // ...and the RESTART is asked of the targets SEPARATELY, never read off that verdict: the
                // aggregate stops at the first refusal, so a Resident sibling behind one left this column
                // blank while `view.S1` below carried that very target's "already loaded" line - and left
                // Admit's R30 down over a revision the game is not serving.
                bool restart = Route7.RestartNeeded(targets);
                // ...and then the SAME two rules the pump applies to a dashboard Apply - the carrier's
                // disposition mapping and the one outcome rule - so a row filled by SHIP and a row filled
                // by the panel cannot disagree about what `Resident` means.
                row.Outcome = LifecycleState.Outcome(GateOutcome.None, Route7.Disposition(project));
                row.Installation = restart ? StageText.RestartRequired : null;
                // `Starts` stays 0 on purpose: it counts the times THIS panel entered a stage, and the
                // panel entered none. The row is a receipt of an apply that happened elsewhere.
                row.Freshness = LifecycleState.Fresh(LifecycleJob.Look(captured));
                // S1 IS A FACT ABOUT THE SESSION, so it is set here for the same reason the pump sets it:
                // a Verify after this press must be refused R30, whichever door the apply came through -
                // and for ANY target that needs it, not only the slot SHIP named.
                if (restart) ctx.RestartRequired = true;
                view.S1 = Lines(targets, Route7.ApplyDisposition.Resident);
                view.S2 = Lines(targets, Route7.ApplyDisposition.Redirected);
                log = applyLine;
                message = null;
                return null;
            }
            catch (Exception ex) { return ex.GetType().Name + ": " + ex.Message; }
        }

        /// <summary>The producer's OWN line for each target that ended this way, joined - never re-worded,
        /// and never recovered by parsing the aggregate log. Null when no target did, which is a different
        /// fact from an empty string.</summary>
        private static string Lines(IList<Route7.TargetInstall> targets, Route7.ApplyDisposition kind)
        {
            if (targets == null) return null;
            string joined = null;
            foreach (Route7.TargetInstall t in targets)
                if (t.Outcome == kind) joined = joined == null ? t.Line : joined + "\n" + t.Line;
            return joined;
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
            string modId = "acceptance." + name.ToLowerInvariant();
            string manifest = Path.Combine(at, ContentMods.Manifest);
            string json = Retarget(File.ReadAllText(manifest), "id", modId);
            File.WriteAllText(manifest, mutate == null ? json : mutate(json));
            // AND meta.json WITH IT. The mod manager keys on that file, not on ppcontent.json, so a fork
            // carrying the source's copy verbatim declared the SOURCE's ID: all four Dashboard* folders
            // announced themselves as `Replace_Leftleg`, the one MOD_ACTIVATED entry enabled every one of
            // them at startup, and whichever loaded first claimed the shared bundle before any row ran
            // (Task 8's D5 - it is what made W10's first attempt VOID). Written through the scaffold's own
            // composer, so the id is JSON-escaped by the same writer a real project's is.
            File.WriteAllText(Path.Combine(at, "meta.json"), ProjectScaffold.Meta(modId));
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
            // ARRIVING ON THE TAB RESCANS. The only automatic scan was the `rescan = true` initialiser,
            // served on the first pump - from component Install, before the bench had ever opened - so the
            // list was built once against whatever existed then. `Drain` does the enumeration, outside
            // drawing, exactly as the Refresh button's press does.
            if (panelReady && !wasReady) rescan = true;
            wasReady = panelReady;
            Drain();
            LifecycleJob.PumpRegistered = true;
            LifecycleJob.Tick(panelReady && Painted);

            LifecycleRun.Snapshot now = LifecycleJob.Run.Latest;
            if (now.Busy || now.RunId == 0 || now.RunId == harvested || now.RunId != dispatched) return;
            harvested = now.RunId;
            // THE QUEUED LINE IS SPENT. `message` is the transient half beside `Run all`/`Cancel` and only
            // `Bind`/`Handoff`/`Choose` ever cleared it, so a finished run still read `Queued: Bake` for the
            // rest of the session (W9, W13). A stage that is about to be dispatched writes its own.
            message = null;
            // AND THE FRESHNESS IS RE-MEASURED ON EVERY ROW (design:91's "after completion"): it is one
            // observation of one project's copies, not a per-row receipt, and writing it on the finishing
            // row alone left the others reporting an age that was measured stages ago - W16 read `fresh`
            // while admission refused Verify as stale.
            Freshen(LifecycleState.Fresh(LifecycleJob.Seen));

            LifecycleView.Row row = view.Of(now.Stage);
            if (row != null)
            {
                row.Verdict = now.Result;
                row.Outcome = LifecycleState.Outcome(now.Outcome, now.How);
                // THE INSTALLATION COLUMN, and the whole of it: S1 is the only thing an author has to ACT
                // on, and it is a carrier value (`RestartRequired`), never a word read off the verdict. A
                // stage that did not report it clears the column rather than inheriting the last one's.
                row.Installation = now.RestartRequired ? StageText.RestartRequired : null;
                // A NEW APPLY REPLACES THE OLD ONE'S INSTALLATION LINES, and this producer publishes none:
                // the carrier has one verdict, not a list. Leaving a SHIP handoff's S1/S2 standing beside a
                // later apply would leave the section describing an install that has since been redone.
                if (now.Stage == "Apply") view.S1 = view.S2 = null;
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
        /// <summary>Index into <see cref="roots"/> of the bound project, or -1. DERIVED, never stored:
        /// `root` is the one binding, and a remembered index went stale the moment the RPC seam bound a
        /// different project - `Open("B")` after a Scan left the label naming A while Run and Apply acted
        /// on B. A list that no longer holds the bound root answers -1 rather than sliding the arrows onto
        /// a neighbour.</summary>
        private static int Chosen { get { return LifecycleSelector.IndexOf(roots, root); } }
        private static bool rescan = true;
        /// <summary>Last frame's answer to "the Lifecycle tab is the selected one", so <see cref="Pump"/>
        /// can rescan on ARRIVAL. The one automatic scan used to happen at component Install - before the
        /// bench had ever opened - so a roster built later, or a project `Acceptance("prepare")` forked,
        /// stayed invisible until the author pressed Refresh.</summary>
        private static bool wasReady;

        /// <summary>The GUILayout groups <see cref="Draw"/> currently has open, so its own catch can close
        /// exactly those and hand IMGUI back a balanced stack. Main thread only, like everything drawing.</summary>
        private static int openGroups;
        private static bool openScroll;

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

            // THE PANEL'S OWN GUARD, the half `Pump` already had (FitBench.cs:2147). Without it a throw in
            // here unwound into OnGUI's catch, which CLOSES the bench - and the job keeps ownership, so the
            // author loses the Cancel button to the very failure they need it for. The recovery also closes
            // the groups this method opened: an unbalanced Begin/End is itself a Layout error next frame,
            // i.e. a second wedge on top of the first.
            openGroups = 0; openScroll = false;
            try { Body(); }
            catch (Exception ex)
            {
                if (openScroll) GUILayout.EndScrollView();
                while (openGroups-- > 0) GUILayout.EndHorizontal();
                message = "lifecycle: " + ex.GetType().Name + ": " + ex.Message;
            }
        }

        private static void Body()
        {
            LifecycleRun.Snapshot now = LifecycleJob.Run.Latest;
            bool owned = now.Busy || Pending(now);
            // THE SESSION BLOCK, asked of the ACTUAL set every frame through the read-only query - never a
            // remembered flag, so it clears exactly when the set clears (a new process, or a producer
            // operation that finally succeeded) and not one frame earlier. There is no bypass here: the
            // dashboard follows the checkbox's suppression, and the console verb's override is not ours.
            bool blocked = Route7.IsFailed(id);
            // A BLOCKING MAIN SEGMENT IS PARKED until this panel paints (design:330-:333). It has NOT begun:
            // `Tick` drops the unrun segment on a cancel (LifecycleJob.cs:458-:469) and the segment's own
            // `Stopped(id)` pre-check answers one when it does run, so a parked run is CANCELLABLE (W13).
            // This is read for the status line only - the wait was invisible and the run merely looked stuck.
            bool parked = LifecycleJob.ParkedForPaint;

            GUILayout.BeginHorizontal(); openGroups++;
            GUILayout.Label("Project", GUILayout.Width(60f));
            GUI.enabled = !owned && roots.Length > 0;
            int at = Chosen;
            if (GUILayout.Button("<", GUILayout.Width(26f)))
                select = LifecycleSelector.Step(at, -1, roots.Length);
            GUI.enabled = true;
            // BOUND BUT NOT LISTED is a real state - a project SHIP just made, or one forked under a root
            // this scan did not reach - and "(none)" over it named nothing while `Run all` acted on it.
            GUILayout.Label(at >= 0 && at < labels.Length ? labels[at]
                          : string.IsNullOrEmpty(root) ? "(none)"
                          : Path.GetFileName(LifecycleSelector.Canonical(root)));
            GUI.enabled = !owned && roots.Length > 0;
            if (GUILayout.Button(">", GUILayout.Width(26f)))
                select = LifecycleSelector.Step(at, 1, roots.Length);
            GUI.enabled = !owned;
            if (GUILayout.Button("Refresh", GUILayout.Width(80f))) rescan = true;
            GUI.enabled = true;
            openGroups--; GUILayout.EndHorizontal();

            // THE GLOBAL STATUS, composed by LifecycleView so the panel and the wire say the same words.
            // The two badges are appended to the transient half, never in place of it: they outlive
            // whatever ran last, and a green stage afterwards must not read as "nothing is owed".
            // `Finishing` is the publication window - owned, not busy - where `Ready.` was a status the
            // panel invented about a run that still held the job.
            GUILayout.Label("Session  " + LifecycleView.Status(
                now.Busy ? now.CancelRequested ? StageText.CancelRequested(now.Stage)
                         : parked ? StageText.WaitingForPaint(now.Stage)
                         : StageText.Running(now.Stage)
                : owned ? StageText.Finishing(now.Stage) : null,
                ctx.RestartRequired, blocked ? id : null));

            foreach (LifecycleView.Row r in view.Rows)
            {
                GUILayout.BeginHorizontal(); openGroups++;
                // THE WIDTHS ARE BenchList'S, and asserted there: this row is five FIXED columns, so it
                // does not shrink to the panel - it is drawn past the edge, silently, with the Run button
                // off-screen. See BenchList.StageRowFits.
                GUILayout.Label(r.Stage, GUILayout.Width(BenchList.StageW));
                GUILayout.Label(LifecycleView.Word(r.Freshness), GUILayout.Width(BenchList.FreshW));
                GUILayout.Label(LifecycleView.Word(r.Outcome), GUILayout.Width(BenchList.OutcomeW));
                GUILayout.Label(Dash(r.Installation), GUILayout.Width(BenchList.InstallW));
                // APPLY ALONE IS BLOCKED by the session block - diagnosis (Validate, Verify) and the
                // author's own output (Bake, Package) stay pressable, which is what an author needs in
                // order to find out WHY the bake failed. The seam refuses it too (R29); this is the
                // button saying so before the press.
                GUI.enabled = !owned && !(blocked && r.Stage == "Apply");
                if (GUILayout.Button("Run", GUILayout.Width(BenchList.StageRunW))) intent = r.Stage;
                GUI.enabled = true;
                openGroups--; GUILayout.EndHorizontal();
                // The row's OWN verdict, never the tail's last line: the two answer different questions and
                // reading one for the other is how a panel invents a verdict.
                // ONE LINE OF IT. A Bake verdict is the whole bake log (1 225 445 chars, W10), and drawing
                // it whole pushed the rows under it, the progress track, both buttons and the log tail off
                // the screen - the layout moving when a result arrives, which is what design §4 forbids.
                // The full text is untouched: the tail holds it and `Snapshot("<stage>")` serves it verbatim.
                GUILayout.Label("  " + Dash(LifecycleView.OneLine(r.Verdict)));
            }

            SlimProgress p = now.Progress;
            GUILayout.BeginHorizontal(); openGroups++;
            GUILayout.Label("Progress", GUILayout.Width(60f));
            float done = p == null || p.Total <= 0 ? 0f : (float)p.Done / p.Total;
            // A FIXED TRACK with the fill inside it, so the phase label beside it does not walk left and
            // right as the bar grows. SlimPanel.cs:270's bar, unchanged.
            GUILayout.BeginHorizontal(GUILayout.Width(240f)); openGroups++;
            GUILayout.Box("", GUILayout.Width(Mathf.Max(1f, 240f * done)), GUILayout.Height(6f));
            GUILayout.FlexibleSpace();
            openGroups--; GUILayout.EndHorizontal();
            GUILayout.Label(p == null ? "—" : p.Stage + " " + p.Done + "/" + p.Total);
            openGroups--; GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal(); openGroups++;
            // ...AND `Run all` WITH IT, because the chain contains Apply: admitted, it would run Validate
            // and Bake and then stop at R29, which is a chain that cannot finish by construction.
            GUI.enabled = !owned && !blocked;
            if (GUILayout.Button("Run all", GUILayout.Width(80f))) intent = "All";
            // A CANCEL IS A REQUEST, and only until one is outstanding. `owned && !busy` is the producer's
            // publication - it has stated its verdict and the pump has not served it yet - which is exactly
            // the window in which there is nothing left to interrupt.
            // A PARKED SEGMENT IS NOT THAT WINDOW: it has not begun, `Tick` cancels it unrun, and W13
            // measured a parked bake cancelling cleanly - gating on it disabled Cancel in exactly the
            // state it is needed (returning to a run started with the panel closed).
            GUI.enabled = now.Busy && !now.CancelRequested;
            if (GUILayout.Button("Cancel", GUILayout.Width(80f))) intent = "Cancel";
            GUI.enabled = true;
            GUILayout.Label(Dash(owned && !now.Busy ? StageText.CancelUnavailable(now.Stage) : message));
            openGroups--; GUILayout.EndHorizontal();

            GUILayout.Label("Log tail");
            tailScroll = GUILayout.BeginScrollView(tailScroll, GUILayout.Height(120f)); openScroll = true;
            GUILayout.Label(string.IsNullOrEmpty(log) ? "—" : StageResult.Tail(log, 12));
            openScroll = false; GUILayout.EndScrollView();
        }

        private static string Dash(string s) { return string.IsNullOrEmpty(s) ? "—" : s; }

        /// <summary>MAIN, from the pump: the enumeration and every press happen HERE, outside drawing.</summary>
        private static void Drain()
        {
            // THE REFRESH PRESS RE-MEASURES THE BARRIER TOO. A bundle can become resident while the panel
            // sits idle - another mod, or a screen that loaded it - and R30 is a live fact, not a receipt
            // of what this panel's own Apply did.
            // ...AND IT RE-MEASURES THE COPIES, design:91's "explicit refresh". `!Busy` because `Look`
            // replaces the observation a running producer is writing, and a Refresh press must not reach
            // into a run's own bookkeeping; the run re-measures for itself at completion anyway.
            if (rescan) { rescan = false; Scan(); Barrier(); if (!Busy) Refresh(ctx.InRunAll); }
            int pick = select; select = int.MinValue;
            string want = intent; intent = null;
            // CANCEL IS THE ONE PRESS THAT BELONGS TO A RUNNING JOB, so it is answered before the busy
            // guard below rather than dropped by it - but it RETURNED, and a `<`/`>` taken in the same
            // frame went down with it. The selection change falls to the busy guard below like any other,
            // which is a refusal it can see, not a press that silently vanished.
            if (want == "Cancel") { Cancel(); want = null; }
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
            // ...AND THE INSTALLATION LINES WITH THEM. They are the previous project's per-target result,
            // and a `Snapshot("s1s2")` that still answers with them after the selection moved is a section
            // describing an install this panel is no longer about.
            view.S1 = view.S2 = null;
            foreach (LifecycleView.Row r in view.Rows)
            {
                r.Verdict = null; r.Installation = null; r.Starts = 0;
                r.Outcome = GateOutcome.None; r.Freshness = Freshness.Never;
            }
            ctx.InRunAll = false;
            ctx.ValidateOutcome = ctx.BakeOutcome = ctx.ApplyOutcome = GateOutcome.None;
            // THE BARRIER IS NOT THE PANEL'S MEMORY, it is a fact about the live claims - so the flag is
            // cleared and then RE-ASKED for the project being bound. Selecting B and coming back to A used
            // to drop A's barrier, and a Verify on A was then admitted over copies the game is not serving.
            ctx.RestartRequired = false;
            Barrier();
        }

        /// <summary>R30 AS A MEASUREMENT, the one rule `ct_route7 verify` asks (Route7.cs:421) and never a
        /// second idea of it: any declared bundle of the SELECTED project that is resident and not served
        /// through a standing claim of ours arms the barrier. ORed into the session flag, never able to
        /// clear it - the S1 an Apply set in this session is cleared by nothing but a new process
        /// (LifecycleState.cs:252). Silent on a manifest it cannot read: the selection's own refusal path
        /// says that, and a picker rescan is not the place to raise it.</summary>
        private static void Barrier()
        {
            if (string.IsNullOrEmpty(root) || string.IsNullOrEmpty(id)) return;
            try
            {
                foreach (ShippedReplacement r in ContentProject.LoadDeclared(root).Replace)
                    if (string.IsNullOrEmpty(r.video) && !string.IsNullOrEmpty(r.bundle) &&
                        BundleClaims.RestartRequired(id, r.bundle, BundleLive.ResidentNow(r.bundle)))
                    { ctx.RestartRequired = true; return; }
            }
            catch (Exception) { }
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
        /// <param name="atPress">this is a PRESS - a button or the RPC seam - so the paint gate (R39) is
        /// asked. The pump passes false: a chain already running keeps the closed-window policy, which
        /// parks the blocking segment and resumes it when the tab comes back (LifecycleJob.Tick), and
        /// refusing mid-chain would break that on purpose.</param>
        private static LifecycleState.Admission Refresh(bool inChain, bool atPress = false)
        {
            LifecycleRun.Snapshot now = LifecycleJob.Run.Latest;
            ctx.Selection = string.IsNullOrEmpty(root) ? LifecycleState.Selection.None
                          : Directory.Exists(root) ? LifecycleState.Selection.Ok
                          : LifecycleState.Selection.Unavailable;
            ctx.RunningStage = now.Busy || Pending(now) ? now.Stage : null;
            ctx.ProjectId = id;
            ctx.RetryHint = Route7.IsFailed(id) ? Route7.RetryHint(root) : null;
            captured = ctx.Selection == LifecycleState.Selection.Ok ? LifecycleJob.Capture(root) : null;
            Freshen(LifecycleState.Fresh(LifecycleJob.Look(captured)));
            ctx.LegacyDiskActive = Route7.LegacyDiskActive(id);
            ctx.WriteOutsideRoots = OutsideRoots();
            ctx.InRunAll = inChain;
            // THE SAME EXPRESSION THE PUMP DRAINS PARKED WORK WITH (`panelReady && Painted`, :511), so a
            // press this admits is one the pump can serve. `wasReady` is last frame's `panelReady`: the
            // press is drained from Update and the panel paints later in the frame, which is exactly the
            // one-frame window `Painted` allows.
            ctx.PaintUnavailable = atPress && !(wasReady && Painted);
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

        /// <summary>ONE FRESHNESS, on admission AND on every row (design:91 - recomputed on an explicit
        /// refresh, at stage start and after completion, never in OnGUI). It is a measurement of the
        /// selected project's copies, so a row cannot hold an older answer than the one admission acts on:
        /// W16 refused Verify as stale while all five rows still read `fresh`.</summary>
        private static void Freshen(Freshness f)
        {
            ctx.Copies = f;
            foreach (LifecycleView.Row r in view.Rows) r.Freshness = f;
        }

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

        /// <param name="gen">the Doctor generation an accepted `ship` press belongs to, or -1 for every
        /// scenario that arms nothing - the key is then absent rather than a number that means nothing.</param>
        private static string Accepted(string scenario, string error, int gen = -1)
        {
            JsonWriter w = new JsonWriter().Obj().Key("ok").Val(error == null)
                                                 .Key("scenario").Val(scenario ?? "");
            if (gen >= 0) w.Key("gen").Val(gen);
            w.Key("error"); if (error == null) w.Null(); else w.Val(error);
            return w.EndObj().ToString();
        }
    }
}
