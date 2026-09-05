using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Morgott.ContentTool.Import;
using Morgott.ContentTool.Project;

namespace Morgott.ContentTool.Bake
{
    /// <summary>
    /// ONE SEGMENTED PRODUCER: main -> worker -> main, per stage.
    ///
    /// WHAT THE SPLIT ACTUALLY IS, measured rather than assumed (design section 4). Half of a bake is Unity:
    /// <c>ContentProject.Load</c> constructs a Texture2D (:628), <c>ProjectBake</c> mounts bundles and
    /// instantiates a rig, and no token interrupts any of them. So the Unity work stays on MAIN and what
    /// goes to the worker is the part that is plain System.IO: the freshness observation - a SHA-1 of the
    /// manifest, a stat of every file under Content\ and a File.Exists per declared copy - taken before the
    /// stage and again after it, which is what the panel shows and what admission reads.
    ///
    /// EVERY UNITY-DERIVED FACT IS CAPTURED ON MAIN AND HANDED IN AS A VALUE. <see cref="Captured"/> holds
    /// them: <c>ContentToolMain.PatchedDir</c> (persistentDataPath), <c>BakeSelfCheck.ShippedBundlePath</c>
    /// (streamingAssetsPath) and - the one that is easy to miss - the R38 verdict, because
    /// <c>BundleClaims.Find</c> walks a static list main mutates (ProjectBake.cs:1986). A worker that calls
    /// any of the three is a bug.
    ///
    /// MAIN SEGMENTS ARE PARKED, NEVER BLOCKED ON. The worker publishes a request and returns; <see
    /// cref="Tick"/>, called from the bench's own Update, runs it on the next frame. Nothing here ever waits
    /// for the main thread - a worker that did would hang forever the moment the pump stopped being called.
    ///
    /// Shape copied from SlimJob/SlimPanel (SlimJob.cs:407, SlimPanel.cs:74-:77): ThreadPool, one
    /// CancellationTokenSource, volatile snapshots, and the result published before busy is cleared. What it
    /// REMEMBERS lives in <see cref="LifecycleRun"/>, which is pure and offline-tested (G4); this file is the
    /// Unity half and its proof is the compiler plus Task 8 (W12/W13).
    /// </summary>
    internal static class LifecycleJob
    {
        /// <summary>The run bookkeeping, shared with the panel and the seam. One process, one seam, one run.</summary>
        internal static readonly LifecycleRun Run = new LifecycleRun();

        private static CancellationTokenSource cts;
        /// <summary>The next main-thread segment, parked by a worker for <see cref="Tick"/> to run. Volatile
        /// because the worker writes it and main reads it, and it is taken with Interlocked so two Ticks in
        /// one frame cannot run it twice.</summary>
        private static volatile Action parked;
        /// <summary>Does the parked segment BLOCK (Unity work that freezes a frame)? Written before
        /// <see cref="parked"/> and read after it, so the pump never sees a segment without its policy.</summary>
        private static volatile bool parkedNeedsPaint;

        /// <summary>What the seam's poll header reports while a blocking segment waits for an open, painted
        /// panel - W19b's "the row says it is waiting" rather than a run that merely looks stuck.</summary>
        internal static bool ParkedForPaint { get { return parked != null && parkedNeedsPaint; } }
        /// <summary>The last observation, for the panel. Replaced whole, never mutated.</summary>
        private static volatile FreshnessObservation seen;

        internal static FreshnessObservation Seen { get { return seen; } }

        /// <summary>Every Unity-derived fact one stage needs, resolved on MAIN. Strings and bools only -
        /// nothing here is a Unity object, so a worker may read all of it.</summary>
        internal sealed class Captured
        {
            internal string Root, Id, PatchedDir, LiveRefusal;
            internal string[] Declared, Shipped, OutputDirs;
        }

        /// <summary>
        /// MAIN THREAD ONLY. Reads the declaration, resolves the three Unity-derived paths and PROBES R38,
        /// all before anything is dispatched.
        ///
        /// <c>LoadDeclared</c>, not <c>Load</c>: the capture needs the declared bundle names and nothing
        /// else, and decoding the author's textures to find them would run the whole import twice.
        /// </summary>
        internal static Captured Capture(string projectRoot)
        {
            ContentProject.Declared d;
            // A MISSING, UNREADABLE OR HALF-TYPED ppcontent.json IS A REFUSAL, NOT AN EXCEPTION OUT OF THE
            // PANEL. LoadDeclared throws for all three (ContentProject.cs:340, :344, and JsonUtility for a
            // malformed row), and this runs on MAIN under the dashboard's Bake press - the throw would have
            // escaped past every counter into the caller's frame, with no run begun and nothing said. Same
            // shape as the R38 verdict below: carried as a string, reported by StartBake as Refused.
            try { d = ContentProject.LoadDeclared(projectRoot); }
            catch (Exception ex)
            {
                return new Captured
                {
                    Root = projectRoot,
                    LiveRefusal = "ct_project: " + projectRoot + " could not be read - " + ex.Message +
                                  " - fix ppcontent.json and press Bake again."
                };
            }
            List<string> declared = new List<string>();
            foreach (ShippedReplacement r in d.Replace)
            {
                if (!string.IsNullOrEmpty(r.video)) continue;      // served live by ct_video, never patched
                if (!declared.Contains(r.bundle, StringComparer.OrdinalIgnoreCase)) declared.Add(r.bundle);
            }

            Captured on = new Captured
            {
                Root = projectRoot,
                Id = d.Id,
                PatchedDir = ContentToolMain.PatchedDir(d.Id),
                Declared = declared.ToArray(),
                Shipped = new string[declared.Count],
                OutputDirs = ProjectBake.OutputDirs(projectRoot, d.Id)
            };
            for (int i = 0; i < declared.Count; i++) on.Shipped[i] = BakeSelfCheck.ShippedBundlePath(declared[i]);

            // R38, ON MAIN. BundleClaims.Find walks the unlocked static list main mutates, so the verdict is
            // taken here and carried as a string; the boundary inside the bake asks again, also on main.
            foreach (string b in declared)
            {
                string refusal = ProjectBake.Live(d.Id, b, Path.Combine(on.PatchedDir, b));
                if (refusal == null) continue;
                on.LiveRefusal = refusal;
                break;
            }
            return on;
        }

        /// <summary>MAIN. Claims the seam and dispatches the stage. Returns the producer's refusal when it
        /// could not start, or null when it did - never a verdict, which only a producer may state.</summary>
        internal static string StartBake(Captured on)
        {
            long id = Run.Begin("Bake");
            if (id == 0) return StageText.R26(Run.Latest.Stage);
            // The captured R38 verdict, answered before a worker exists. Not a count and not a failure.
            if (on.LiveRefusal != null)
            {
                Run.Complete(id, on.LiveRefusal, BakeDisposition.Refused);
                return null;
            }
            cts = new CancellationTokenSource();
            CancellationToken cancel = LifecycleRun.TokenOf(cts);

            // ---- the WORKER segment: the freshness observation, from captured paths only.
            Worker(delegate
            {
                // The acceptance barrier, if a scenario armed one. It is the FIRST thing on the worker so
                // the run is genuinely parked before it has touched anything, and Cancel is what frees it.
                Barrier.Wait(id);
                Observe(on);
                // ---- park the MAIN segment. The bake is Unity from end to end (bundle loads, rig
                // instantiation), so it BLOCKS a frame and waits for an open, painted panel - W19b.
                Park(delegate
                {
                    BakeResult r;
                    try
                    {
                        r = ProjectBake.Bake(on.Root, false, cancel,
                                             delegate(string phase, int done, int total)
                                             { Run.Progress(id, new SlimProgress(phase, done, total, on.Id)); });
                    }
                    catch (Exception ex)
                    {
                        // A throw out of a producer is a FAILED run with the producer's own words, never a
                        // seam left busy for the rest of the session.
                        Run.Complete(id, "ct_project THREW: " + ex.Message, BakeDisposition.Failed);
                        return;
                    }
                    // ---- back to a WORKER for the trailing observation, so the panel's freshness is the
                    // one this run just produced and the completion is published with it.
                    Worker(delegate { Observe(on); Run.Complete(id, r.Terminal, r.How); });
                }, true);
            });
            return null;
        }

        /// <summary>
        /// MAIN. The stage dispatcher the seam and the `Run all` sequencer share, so a button, an RPC and a
        /// chain all enter a producer by the same door. Returns the refusal when the stage could not start,
        /// null when it did - never a verdict, which only a producer may state.
        /// </summary>
        internal static string Start(string stage, Captured on)
        {
            if (stage == "Bake") return StartBake(on);
            if (stage == "Package") return StartPackage(on);
            // ponytail: Validate, Apply and Verify have no segmented producer yet - 4.1's ManifestFile/
            // ModGate pair, Route7.ApplyProject's structured overload and 4.4's read-back producer are
            // three separate pieces of work, none of them budgeted here (Task 5 is the coordinator, the
            // seam and the pump). This says so instead of inventing a verdict for a stage nothing ran.
            // Wire them where their panel rows land, and delete this line with the last of them.
            return "Lifecycle: " + stage + " is not wired to the dashboard yet.";
        }

        /// <summary>
        /// MAIN, then WHOLLY A WORKER. `Package` is plain System.IO by construction (Package.cs:15), so it
        /// is the one stage with no main-thread final segment - which is exactly what makes W19a a real
        /// closed-window proof rather than a wait for someone to reopen the bench.
        ///
        /// It writes OUTSIDE the game installation and never through the console wrapper, whose
        /// `Directory.Delete(outDir, true)` (ContentToolMain.cs:511) destroys the previous package.
        /// `Package.Run` refuses a nonempty destination itself (:78) and stays the sole authority on that.
        /// </summary>
        internal static string StartPackage(Captured on)
        {
            long id = Run.Begin("Package");
            if (id == 0) return StageText.R26(Run.Latest.Stage);
            string outDir = PackageDir(on.Id, id);
            // Resolved before dispatch, like every other path: a null is Package.Run's own "no DLL" case
            // and it reports that itself, so a throw here must not become the run's verdict.
            string dll = null;
            try { dll = Package.BuiltAssembly(on.Root); }
            catch (Exception) { }
            Worker(delegate
            {
                bool ok;
                string line;
                try { line = Package.Run(on.Root, outDir, dll, out ok); }
                catch (Exception ex)
                {
                    Run.Complete(id, "ct_package THREW: " + ex.Message, BakeDisposition.Failed);
                    return;
                }
                // `ok` authorizes the FOLDER (design:180); a refusal is not a failure and keeps its own text.
                Run.Complete(id, line, ok ? BakeDisposition.Success : BakeDisposition.Refused);
            });
            return null;
        }

        /// <summary>A NEW directory per run under %LOCALAPPDATA%, outside the game installation
        /// (design:355). The run id alone would collide across sessions - it restarts at 1 in every process
        /// - and a timestamp alone can collide inside one second, so the name carries both.</summary>
        internal static string PackageDir(string projectId, long runId)
        {
            string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string name = string.IsNullOrEmpty(projectId) ? "project" : projectId;
            foreach (char bad in Path.GetInvalidFileNameChars()) name = name.Replace(bad, '_');
            return Path.Combine(Path.Combine(Path.Combine(local, "ContentTool"), "Packages"),
                                Path.Combine(name,
                                             DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + runId));
        }

        /// <summary>MAIN. Drains at most ONE parked segment per call: a Unity segment can freeze a frame,
        /// and running two of them back to back would freeze two. Its own try, like the Doctor drain's - a
        /// lifecycle bug must not take the bench's input down with it.
        ///
        /// THE CLOSED-WINDOW POLICY, and the whole of it (design:323-:333): a worker keeps running and
        /// publishes its result with the bench closed, and so does any main segment that only needs a FRAME.
        /// A BLOCKING main segment waits for <paramref name="panelReady"/> - the panel open and painted -
        /// exactly as SHIP's two-frame arming does (ModelDoctor.cs:443), because freezing a frame behind a
        /// window nobody can see is indistinguishable from a hang. Nothing is re-run to produce the result
        /// when the bench comes back; the segment is still sitting here.
        /// </summary>
        internal static void Tick(bool panelReady)
        {
            if (parked == null) return;
            if (parkedNeedsPaint && !panelReady) return;
            Action next = Interlocked.Exchange(ref parked, null);
            if (next == null) return;
            try { next(); }
            catch (Exception ex)
            {
                LifecycleRun.Snapshot now = Run.Latest;
                if (now.Busy) Run.Complete(now.RunId, "lifecycle: " + ex.Message, BakeDisposition.Failed);
            }
        }

        /// <summary>MAIN. Asks the running producer to stop. It is a REQUEST: busy stays set until the
        /// producer says what happened, and B5 finishes whatever it started.</summary>
        internal static void Cancel()
        {
            Run.Cancel();
            CancellationTokenSource c = cts;
            if (c != null) try { c.Cancel(); } catch (ObjectDisposedException) { }
            // AFTER the token, never before: the released worker must find a cancelled token, or it walks
            // on into a bake that completes normally and W13 loses the thing it is measuring.
            Barrier.Release();
        }

        /// <summary>The freshness observation, WORKER-SAFE by construction: every path it touches was
        /// resolved on main and handed in.</summary>
        private static void Observe(Captured on)
        {
            try { seen = Route7.Observe(on.PatchedDir, on.Root, on.Declared, on.Shipped); }
            // An unreadable source or a folder that vanished is not worth losing the run over - the panel
            // shows the observation it had, and admission treats a null one as "never".
            catch (Exception) { }
        }

        private static void Worker(Action body)
        {
            ThreadPool.QueueUserWorkItem(delegate
            {
                try { body(); }
                catch (Exception ex)
                {
                    LifecycleRun.Snapshot now = Run.Latest;
                    if (now.Busy) Run.Complete(now.RunId, "lifecycle: " + ex.Message, BakeDisposition.Failed);
                }
            });
        }

        /// <summary>Set ONCE by the bench's own Update, which is the only thing that calls
        /// <see cref="Tick"/>. Until it is, a parked segment would sit in <see cref="parked"/> for the rest
        /// of the session with the seam busy and the panel showing "Running" over a bake nobody will ever
        /// run - a hang with no symptom to grep for. Task 5 registers it.</summary>
        /// (`= false` is not decoration: nothing assigns this until Task 5 does, and an uninitialised
        /// field would be CS0649 - a NEW build warning over a field whose whole point is to be false.)
        internal static bool PumpRegistered = false;

        /// <summary>MAIN SEGMENT, handed to the pump. A worker calls this, so the throw below lands in
        /// <see cref="Worker"/>'s catch and the run ends FAILED with that sentence - loudly, at once,
        /// instead of parking work nothing drains.</summary>
        private static void Park(Action body, bool needsPaint)
        {
            if (!PumpRegistered)
                throw new InvalidOperationException("no lifecycle pump registered — FitBench.Update must " +
                                                    "call LifecycleJob.Tick");
            parkedNeedsPaint = needsPaint;
            parked = body;
        }

        /// <summary>
        /// THE ACCEPTANCE BARRIER, and the two rules that keep W13 from being a sleep.
        ///
        /// It parks a WORKER and never the main-thread pump: parking the pump would make `Snapshot`
        /// unanswerable and W13/W20 unpollable by construction. And <see cref="Parked"/> is published only
        /// once a worker is ACTUALLY sitting in <see cref="Wait"/>, never on arming alone, or the first poll
        /// passes before the run exists.
        ///
        /// It releases on the same `Cancel()` the button calls and lets NORMAL worker completion publish the
        /// verdict: the released worker walks on into a bake whose token is already cancelled and B4 returns
        /// `Cancelled` on its own. No Thread.Abort, no synthetic success, no detached worker.
        /// </summary>
        internal static class Barrier
        {
            private static volatile ManualResetEvent gate;
            /// <summary>A worker is sitting in <see cref="Wait"/> right now, and this is its run.</summary>
            internal static volatile bool Parked;
            internal static long ParkedRunId;

            internal static bool Armed { get { return gate != null; } }

            internal static void Arm()
            {
                Release();
                gate = new ManualResetEvent(false);
            }

            /// <summary>WORKER ONLY. Returns at once unless a scenario armed the barrier.</summary>
            internal static void Wait(long runId)
            {
                ManualResetEvent g = gate;
                if (g == null) return;
                ParkedRunId = runId;
                Parked = true;
                // Never disposed, so this cannot race a Release into ObjectDisposedException; one event per
                // armed scenario is a handle a test session can afford.
                try { g.WaitOne(); }
                finally { Parked = false; ParkedRunId = 0; }
            }

            internal static void Release()
            {
                ManualResetEvent g = gate;
                gate = null;
                if (g != null) g.Set();
            }
        }
    }
}
