using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Morgott.ContentTool.Import;
using UnityEngine;

namespace Morgott.ContentTool.Dev
{
    /// <summary>
    /// ============ DROPPING CLIPS OUT OF A .glb, FROM THE BENCH ============
    ///
    /// The file utility half of the Doctor tab (design §6: file utilities live under Advanced). It
    /// lists what a .glb spends on animation and lets the author tick the clips a prototype will
    /// never play, then hands the whole run to <see cref="SlimJob"/>.
    ///
    /// TWO RULES SHAPE EVERY LINE BELOW, and both are about frames rather than about slimming:
    ///
    /// 1. A PRESS ONLY ENQUEUES. IMGUI walks this method twice per frame - Layout, then the event
    ///    or repaint pass - and matches controls up by ORDER. A button that changed the clip list
    ///    where it was pressed would make the second pass emit a different number of controls than
    ///    the first counted, and IMGUI answers that by throwing every frame afterwards. So a press
    ///    queues an <see cref="intents"/> action and the queue is drained at the START of the next
    ///    Layout pass - the same discipline ModelDoctor.Draw keeps (src\Dev\ModelDoctor.cs:532).
    /// 2. THE WORKER'S NEWS IS COPIED ONCE PER FRAME. SlimJob.Start calls back on the POOL thread
    ///    (src\Import\SlimJob.cs:104), so <see cref="progress"/>, <see cref="result"/> and
    ///    <see cref="running"/> can change BETWEEN the two passes of one frame. They are snapshotted
    ///    into the shown* fields during Layout and only the snapshot is drawn, so the two passes
    ///    always lay out the same panel. Nothing here touches a Unity object off the main thread.
    ///
    /// ponytail: no per-clip preview, no undo, no batch over a folder. The undo is the file the run
    /// did not overwrite; add the rest when an author asks for it.
    /// </summary>
    internal sealed class SlimPanel
    {
        private readonly GlbFileBrowser browser = new GlbFileBrowser();
        /// <summary>Pressed-this-frame work, run at the next Layout pass. Main thread only - the
        /// worker never enqueues, it writes the volatile fields below.</summary>
        private readonly Queue<Action> intents = new Queue<Action>();

        private string sourcePath;
        private GlbSlim.ClipRow[] census = new GlbSlim.ClipRow[0];
        /// <summary>Which rows are ticked, i.e. which clips the run will DROP. Same length as
        /// <see cref="census"/> and rebuilt with it.</summary>
        private bool[] drop = new bool[0];
        private bool force;
        private bool inPlace;

        /// <summary>Which run this panel is set up for: drop clips, rewrite how the same curves are
        /// stored, or rename the bones onto a prototype's. One panel rather than three because every
        /// field around the middle block - the browser, the intent queue, the progress trio, the writes
        /// line - is the same panel whichever run is chosen. It is changed only in the intent drain,
        /// because the three modes draw different control COUNTS and rule 1 above is what that costs.</summary>
        private enum Mode { Slim, Zip, Skel }
        private Mode mode;

        /// <summary>The .skelplan.json a SKEL run applies, and the sentence describing it. Null while
        /// there is no plan that PARSES - the RUN button is off until there is one, because a run that
        /// starts with an unreadable plan can only end in a refusal.</summary>
        private string planPath;
        private string planLine = "no plan yet";
        /// <summary>The prototype the Doctor has selected, handed down by the bench each frame. Read
        /// for the closing Verify and for nothing else - the panel never picks a prototype.</summary>
        internal Doctor.PrototypeTarget Target;
        private bool collapse = true;
        private bool quantise = true;
        /// <summary>What a zip would work on, counted off the JSON when the file is picked: rotation
        /// channels are the ones the quantiser may touch, and the bytes are the animation half of the
        /// file. Upper bounds both - what a run actually rewrote is the sentence it returns.</summary>
        private int rotations;
        private long animBytes;

        // --- written by the worker, read by the UI ---
        private volatile SlimProgress progress;
        private volatile string result;
        private volatile bool running;
        private CancellationTokenSource cts;

        // --- the once-per-frame copies the drawing actually uses ---
        private SlimProgress shownProgress;
        private string shownResult = "";
        private bool shownRunning;
        private Mode shownMode;
        private IList<string> shownTarget;

        /// <summary>Draws the panel. Called from the bench inside the Doctor tab, under Advanced.</summary>
        internal void Draw(float width)
        {
            // Open is tested BEFORE Draw and the frame ends there, exactly as the Doctor does it: the
            // browser reorders its own recents during the mouse pass, so a second Draw in the same
            // frame would lay out a different list.
            if (browser.Open)
            {
                string picked = browser.Draw(220f);
                if (picked != null) { string p = picked; intents.Enqueue(delegate { result = null; Pick(p); }); }
                return;
            }

            if (Event.current.type == EventType.Layout)
            {
                while (intents.Count > 0) intents.Dequeue()();
                // A finished trim renumbers the clips that are left, so the ticks on screen no longer
                // name the clips they named when they were ticked. The list is re-read rather than
                // kept: a second Run off a stale list would delete something nobody ticked.
                if (shownRunning && !running && sourcePath != null) Pick(sourcePath);
                shownProgress = progress;
                shownResult = result ?? "";
                shownRunning = running;
                shownMode = mode;
                shownTarget = Target == null ? null : Target.BoneNames();
            }

            GUILayout.Space(6f);
            GUILayout.Label(shownMode == Mode.Skel
                            ? "GLB SKEL - rename this model's bones onto the prototype's"
                            : shownMode == Mode.Zip
                              ? "GLB ZIP - shrink the animation without dropping a clip"
                              : "GLB SLIM - drop animation clips this model will never play");

            GUILayout.BeginHorizontal();
            GUI.enabled = !shownRunning;
            if (GUILayout.Button("Browse...", GUILayout.Width(90f)))
                browser.Show(sourcePath == null ? "" : Path.GetDirectoryName(sourcePath));
            GUI.enabled = true;
            GUILayout.Label("source: " + BenchList.Elide(sourcePath == null ? "-" : Path.GetFileName(sourcePath), 40));
            GUILayout.EndHorizontal();

            // The mode decides how many controls the block below emits, so the press only ENQUEUES -
            // flipping it here would make the repaint pass lay out a different panel than the Layout
            // pass counted, which is rule 1 in the remark above.
            GUILayout.BeginHorizontal();
            GUI.enabled = !shownRunning;
            bool onSlim = GUILayout.Toggle(shownMode == Mode.Slim, " SLIM (drop clips)", GUILayout.Width(140f));
            bool onZip = GUILayout.Toggle(shownMode == Mode.Zip, " ZIP (rewrite curves)", GUILayout.Width(160f));
            bool onSkel = GUILayout.Toggle(shownMode == Mode.Skel, " SKEL (rename bones)", GUILayout.Width(160f));
            GUI.enabled = true;
            GUILayout.EndHorizontal();
            Mode want = shownMode;
            if (onSlim && shownMode != Mode.Slim) want = Mode.Slim;
            else if (onZip && shownMode != Mode.Zip) want = Mode.Zip;
            else if (onSkel && shownMode != Mode.Skel) want = Mode.Skel;
            if (want != shownMode) { Mode picked = want; intents.Enqueue(delegate { Switch(picked); }); }

            if (shownMode == Mode.Skel) SkelOptions();
            else if (shownMode == Mode.Zip) Options();
            else Clips(width);

            GUILayout.BeginHorizontal();
            GUI.enabled = !shownRunning;
            // No force outside SLIM: it overrides the mandatory-clip and rigged-character arms, and a
            // run that drops no clip cannot reach either of them.
            if (shownMode == Mode.Slim)
                force = GUILayout.Toggle(force, " force (drop mandatory clips too)", GUILayout.Width(220f));
            inPlace = GUILayout.Toggle(inPlace, " overwrite in place", GUILayout.Width(150f));
            // A file with no clips is a perfectly good skeleton to rewrite, so SKEL asks for a plan
            // instead of a census.
            GUI.enabled = !shownRunning && sourcePath != null &&
                          (shownMode == Mode.Skel ? planPath != null : census.Length > 0);
            bool run = GUILayout.Button("RUN", GUILayout.Width(70f));
            GUI.enabled = shownRunning;
            bool stop = GUILayout.Button("CANCEL", GUILayout.Width(80f));
            GUI.enabled = true;
            GUILayout.EndHorizontal();
            if (run) intents.Enqueue(Run);
            if (stop) intents.Enqueue(Cancel);

            // ALWAYS these three rows, whatever the state. A row that appears only while a run is on
            // is a row that appears BETWEEN the two passes of the frame the run starts in - which is
            // rule 1 above, broken in the one place it costs a wedged panel.
            GUILayout.Label("writes: " + (sourcePath == null ? "-"
                                          : Path.GetFileName(inPlace ? sourcePath
                                                             : Beside(sourcePath, Tag(shownMode)))));
            Bar();
            GUILayout.Label("result: " + (shownResult.Length == 0 ? "-" : shownResult));
        }

        /// <summary>Cancels a run in flight and closes the browser. The file on disk is safe either
        /// way: SlimJob only ever swaps a finished temp into place.</summary>
        internal void Dispose()
        {
            Cancel();
            browser.Hide();
        }

        // ------------------------------------------------------------------ the rows

        private void Clips(float width)
        {
            if (census.Length == 0) { GUILayout.Label("no clips listed - pick a .glb"); return; }

            GUILayout.Label("tick what to DROP - " + census.Length + " clip(s), 'frees' is what leaving " +
                            "it out would actually save");
            float nameW = Mathf.Max(120f, (width - 340f) * 0.5f);
            for (int i = 0; i < census.Length; i++)
            {
                GlbSlim.ClipRow row = census[i];
                GUILayout.BeginHorizontal();
                GUI.enabled = !shownRunning;
                drop[i] = GUILayout.Toggle(drop[i], "", GUILayout.Width(18f));
                GUI.enabled = true;
                GUILayout.Label(BenchList.Elide(string.IsNullOrEmpty(row.Name) ? "(unnamed)" : row.Name, 30),
                                GUILayout.Width(nameW));
                GUILayout.Label(row.Channels + " ch / " + row.Samplers + " smp", GUILayout.Width(100f));
                GUILayout.Label(Bytes(row.AccessorBytes) + " data, " + Bytes(row.ExclusiveBytes) + " frees",
                                GUILayout.Width(180f));
                if (row.Mandatory) GUILayout.Label("MANDATORY");
                GUILayout.EndHorizontal();
            }
        }

        /// <summary>ZIP has no per-clip choice to make - it drops nothing - so the middle block is the
        /// two passes and a census of what they would work on.</summary>
        private void Options()
        {
            if (census.Length == 0) { GUILayout.Label("no clips listed - pick a .glb"); return; }

            GUILayout.BeginHorizontal();
            GUI.enabled = !shownRunning;
            collapse = GUILayout.Toggle(collapse, " collapse curves that never move", GUILayout.Width(240f));
            quantise = GUILayout.Toggle(quantise, " rotations as int16 (0.002 deg)", GUILayout.Width(230f));
            GUI.enabled = true;
            GUILayout.EndHorizontal();

            // ponytail: no predicted saving and no collapsible-curve count. Both mean reading every
            // sampler's VALUES out of BIN, which is the run itself - on the UI thread, for a file the
            // author may not even run. The result line reports both exactly, once, afterwards.
            GUILayout.Label(census.Length + " clip(s), " + rotations + " rotation channel(s), " +
                            Bytes(animBytes) + " of animation - no clip is dropped");
        }

        /// <summary>SKEL has no per-clip choice either: what it needs is a PLAN, and the plan lives
        /// beside the file under a name both sides spell the same way (SkelPlan.PlanPathOf). The two
        /// labels below are FIELDS computed in the intent drain rather than facts re-read here - a
        /// label that read the disk mid-layout would answer differently in the two passes of one frame.
        ///
        /// ponytail: no file dialog for the plan. GlbFileBrowser lists .glb and nothing else, and the
        /// Doctor writes the plan to exactly one place - beside the source. "Plan..." re-reads that
        /// place. Give the browser an extension when a plan ever legitimately lives somewhere else.</summary>
        private void SkelOptions()
        {
            GUILayout.BeginHorizontal();
            GUI.enabled = !shownRunning && sourcePath != null;
            if (GUILayout.Button("Plan...", GUILayout.Width(90f)))
                intents.Enqueue(delegate { SeedPlan(sourcePath); });
            GUI.enabled = true;
            GUILayout.Label("plan: " + BenchList.Elide(planPath == null ? "-" : Path.GetFileName(planPath), 40));
            GUILayout.EndHorizontal();

            GUILayout.Label(planLine);
            // The prototype is the Doctor's, not this panel's. Without one the rewrite still happens -
            // it just claims nothing about binding, which is the honest thing for a verify with no
            // question to ask.
            GUILayout.Label(shownTarget == null || shownTarget.Count == 0
                ? "no prototype selected - the run will rewrite but claim nothing about binding"
                : "verifies against the Doctor's prototype, " + shownTarget.Count + " bone(s), BY NAME");
        }

        /// <summary>The stage line and a plain box grown to the fraction done. No texture, no widget:
        /// a Box with a width is a bar in every skin the game might be wearing.</summary>
        private void Bar()
        {
            SlimProgress p = shownProgress;
            GUILayout.Label(p == null
                            ? "idle"
                            : p.Stage + " " + p.Done + "/" + p.Total + " - " + p.Message);
            float done = p == null || p.Total <= 0 ? 0f : (float)p.Done / p.Total;
            GUILayout.Box("", GUILayout.Width(Mathf.Max(1f, 240f * done)), GUILayout.Height(6f));
        }

        // ------------------------------------------------------------------ the intents

        /// <summary>Reads the file and lists its clips. Runs in the Layout drain, never mid-layout.
        /// ponytail: the parse happens on the UI thread - a GLB's JSON chunk is kilobytes even when
        /// the file is megabytes, so it is one short hitch. Move it to the pool the way
        /// ModelDoctor.Start does (src\Dev\ModelDoctor.cs:229) if a file ever makes this stutter.</summary>
        private void Pick(string path)
        {
            sourcePath = path;
            progress = null;
            try
            {
                GlbDocument doc = GlbDocument.Load(path);
                List<GlbSlim.ClipRow> rows = GlbSlim.Census(doc);
                census = rows.ToArray();
                drop = new bool[census.Length];
                animBytes = 0L;
                foreach (GlbSlim.ClipRow row in census) animBytes += row.AccessorBytes;
                rotations = Rotations(doc);
                if (census.Length == 0) result = "no animation clips in this file - nothing to slim";
            }
            catch (Exception ex)
            {
                // A file the container reader cannot open is a sentence, not a stack trace: this runs
                // inside OnGUI, where a throw tears the whole bench panel down mid-frame.
                census = new GlbSlim.ClipRow[0];
                drop = new bool[0];
                rotations = 0;
                animBytes = 0L;
                result = Path.GetFileName(path) + " could not be read: " +
                         ex.GetType().Name + " - " + ex.Message;
            }
            // Outside the try: a .glb the container reader refuses is exactly the kind of file SKEL
            // exists for, and its plan is still worth offering.
            SeedPlan(path);
        }

        /// <summary>Switching the run also re-reads the plan, because the plan belongs to the FILE and
        /// the author reaches SKEL by pressing the toggle, not by picking the file again.</summary>
        private void Switch(Mode picked)
        {
            mode = picked;
            if (picked == Mode.Skel) SeedPlan(sourcePath);
        }

        /// <summary>Read the plan sitting beside this .glb, if there is one. planPath is left NULL for
        /// anything that will not parse - the sentence says why and the RUN button stays off, because
        /// a run that starts with an unreadable plan can only end in a refusal the author has already
        /// been told about.</summary>
        private void SeedPlan(string glbPath)
        {
            planPath = null;
            if (glbPath == null) { planLine = "pick a .glb first"; return; }
            string beside = SkelPlan.PlanPathOf(glbPath);
            if (!File.Exists(beside))
            {
                planLine = "no " + Path.GetFileName(beside) + " yet - the Model Doctor's " +
                           "'Write skel plan' writes one from the bone map";
                return;
            }
            try
            {
                string why;
                SkelPlan plan = SkelPlan.Parse(File.ReadAllText(beside), out why);
                if (plan == null) { planLine = why; return; }
                planPath = beside;
                planLine = plan.Renames.Count + " rename, " + plan.Collapses.Count + " collapse, " +
                           plan.Inserts.Count + " insert, " + plan.Create.Count + " create" +
                           (string.IsNullOrEmpty(plan.Root) ? " - no root named" : " - root '" + plan.Root + "'");
            }
            catch (Exception ex)
            {
                // Same rule as Pick: this runs inside OnGUI, where a throw tears the bench panel down.
                planLine = Path.GetFileName(beside) + " could not be read: " + ex.Message;
            }
        }

        private void Run()
        {
            if (sourcePath == null || running) return;
            cts = new CancellationTokenSource();
            result = null;
            running = true;
            // Both callbacks land on the POOL thread. They assign volatile fields and nothing else -
            // result BEFORE running, so the frame that first sees the run finished already has the
            // sentence to show for it.
            Action<SlimProgress> onProgress = delegate(SlimProgress p) { progress = p; };
            Action<string> onComplete = delegate(string r) { result = r; running = false; };

            if (mode == Mode.Skel)
            {
                progress = new SlimProgress("Queued", 0, 6, "waiting for a worker");
                // Names, not paths: on a Replace target the prototype's PATHS are the whole rig's and a
                // slot renderer holds a small subset of it, so asking the path question here would
                // report a good rename as a wall of missing paths. GlbSkel.Verify asks the two apart
                // for exactly this reason; the gate asks both.
                SlimJob.StartSkel(sourcePath, inPlace ? sourcePath : Beside(sourcePath, "skel"), planPath,
                                  Target == null ? null : Target.BoneNames(), null,
                                  cts, onProgress, onComplete);
                return;
            }

            if (mode == Mode.Zip)
            {
                progress = new SlimProgress("Queued", 0, 6, "waiting for a worker");
                SlimJob.StartZip(sourcePath, inPlace ? sourcePath : Beside(sourcePath, "zip"),
                                 collapse, quantise, cts, onProgress, onComplete);
                return;
            }

            var indices = new HashSet<int>();
            for (int i = 0; i < census.Length; i++) if (drop[i]) indices.Add(census[i].Index);
            progress = new SlimProgress("Queued", 0, 5, "waiting for a worker");
            SlimJob.Start(sourcePath, inPlace ? sourcePath : Beside(sourcePath, "slim"), indices, force,
                          cts, onProgress, onComplete);
        }

        private void Cancel()
        {
            if (cts != null) cts.Cancel();
        }

        // ------------------------------------------------------------------ small change

        private static string Tag(Mode mode)
        {
            return mode == Mode.Skel ? "skel" : mode == Mode.Zip ? "zip" : "slim";
        }

        /// <summary>The sibling a non-destructive run writes: <c>foo.glb</c> -> <c>foo.slim.glb</c>,
        /// <c>foo.zip.glb</c> or <c>foo.skel.glb</c>. The tag says which run made it, so no two ever
        /// overwrite each other's output.</summary>
        private static string Beside(string path, string tag)
        {
            string dir = Path.GetDirectoryName(path) ?? "";
            return Path.Combine(dir, Path.GetFileNameWithoutExtension(path) + "." + tag + ".glb");
        }

        /// <summary>How many channels the quantiser could touch. Only a rotation fits a normalized
        /// int16 - translation is metres with no bound and scale is unitless - so this is the same
        /// filter GlbZip applies (src\Import\GlbZip.cs:283). Channels rather than accessors: two clips
        /// sharing one output are two channels and at most one rewrite, so it is an upper bound and
        /// the run's own sentence is the exact figure.</summary>
        private static int Rotations(GlbDocument doc)
        {
            int found = 0;
            foreach (object animation in GlbSlim.Arr(doc.Json, "animations") ?? new List<object>())
                foreach (object channel in GlbSlim.Arr(GlbSlim.Obj(animation), "channels") ?? new List<object>())
                    if (GlbSlim.Str(GlbSlim.Obj(GlbSlim.Get(GlbSlim.Obj(channel), "target")), "path") == "rotation")
                        found++;
            return found;
        }

        private static string Bytes(long n)
        {
            if (n < 1024L) return n + " B";
            if (n < 1024L * 1024L) return (n / 1024f).ToString("0.#") + " KB";
            return (n / (1024f * 1024f)).ToString("0.##") + " MB";
        }
    }
}
