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

        // --- written by the worker, read by the UI ---
        private volatile SlimProgress progress;
        private volatile string result;
        private volatile bool running;
        private CancellationTokenSource cts;

        // --- the once-per-frame copies the drawing actually uses ---
        private SlimProgress shownProgress;
        private string shownResult = "";
        private bool shownRunning;

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
            }

            GUILayout.Space(6f);
            GUILayout.Label("GLB SLIM - drop animation clips this model will never play");

            GUILayout.BeginHorizontal();
            GUI.enabled = !shownRunning;
            if (GUILayout.Button("Browse...", GUILayout.Width(90f)))
                browser.Show(sourcePath == null ? "" : Path.GetDirectoryName(sourcePath));
            GUI.enabled = true;
            GUILayout.Label("source: " + BenchList.Elide(sourcePath == null ? "-" : Path.GetFileName(sourcePath), 40));
            GUILayout.EndHorizontal();

            Clips(width);

            GUILayout.BeginHorizontal();
            GUI.enabled = !shownRunning;
            force = GUILayout.Toggle(force, " force (drop mandatory clips too)", GUILayout.Width(220f));
            inPlace = GUILayout.Toggle(inPlace, " overwrite in place", GUILayout.Width(150f));
            GUI.enabled = !shownRunning && sourcePath != null && census.Length > 0;
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
                                          : Path.GetFileName(inPlace ? sourcePath : Beside(sourcePath))));
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
                List<GlbSlim.ClipRow> rows = GlbSlim.Census(GlbDocument.Load(path));
                census = rows.ToArray();
                drop = new bool[census.Length];
                if (census.Length == 0) result = "no animation clips in this file - nothing to slim";
            }
            catch (Exception ex)
            {
                // A file the container reader cannot open is a sentence, not a stack trace: this runs
                // inside OnGUI, where a throw tears the whole bench panel down mid-frame.
                census = new GlbSlim.ClipRow[0];
                drop = new bool[0];
                result = Path.GetFileName(path) + " could not be read: " +
                         ex.GetType().Name + " - " + ex.Message;
            }
        }

        private void Run()
        {
            if (sourcePath == null || running) return;
            var indices = new HashSet<int>();
            for (int i = 0; i < census.Length; i++) if (drop[i]) indices.Add(census[i].Index);

            cts = new CancellationTokenSource();
            result = null;
            running = true;
            progress = new SlimProgress("Queued", 0, 5, "waiting for a worker");
            // Both callbacks land on the POOL thread. They assign volatile fields and nothing else -
            // result BEFORE running, so the frame that first sees the run finished already has the
            // sentence to show for it.
            SlimJob.Start(sourcePath, inPlace ? sourcePath : Beside(sourcePath), indices, force, cts,
                          delegate(SlimProgress p) { progress = p; },
                          delegate(string r) { result = r; running = false; });
        }

        private void Cancel()
        {
            if (cts != null) cts.Cancel();
        }

        // ------------------------------------------------------------------ small change

        /// <summary>The sibling a non-destructive run writes: <c>foo.glb</c> -> <c>foo.slim.glb</c>.</summary>
        private static string Beside(string path)
        {
            string dir = Path.GetDirectoryName(path) ?? "";
            return Path.Combine(dir, Path.GetFileNameWithoutExtension(path) + ".slim.glb");
        }

        private static string Bytes(long n)
        {
            if (n < 1024L) return n + " B";
            if (n < 1024L * 1024L) return (n / 1024f).ToString("0.#") + " KB";
            return (n / (1024f * 1024f)).ToString("0.##") + " MB";
        }
    }
}
