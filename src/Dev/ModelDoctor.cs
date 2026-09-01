using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Morgott.ContentTool.Doctor;
using Morgott.ContentTool.Import;
using UnityEngine;

namespace Morgott.ContentTool.Dev
{
    /// <summary>
    /// THE MODEL DOCTOR. Pick a .glb, pick the skinned mesh it should replace, and read what the BAKE
    /// would do with them - before writing a manifest, before a bake, before a restart.
    ///
    /// The verdict is not computed here. ReplacementPreflight.Run computes it, on a worker thread,
    /// out of bytes and a plain snapshot of the rig, using the same ReplacementDecision.Decide the
    /// bake uses. This class is the part that cannot be pure: it owns the Unity objects, the
    /// generation counter that makes a stale answer harmless, and the fingerprint that makes a
    /// preview refuse to land on a target that changed under it.
    ///
    /// Threading, stated once: OnGUI only reads and enqueues intents; Update only drains and mutates;
    /// the worker only touches bytes. Nothing else is allowed to move between them.
    /// </summary>
    internal sealed class ModelDoctor
    {
        private enum Intent { Preview, Revert, Save }

        private readonly ConcurrentQueue<Intent> intents = new ConcurrentQueue<Intent>();
        /// <summary>Navigation and alias edits the DRAW pass asked for. OnGUI runs twice per frame
        /// (Layout, then Repaint) over a list it must not change between the two: picking a file or a
        /// bone mid-pass renames the rows the Repaint is about to lay out, and IMGUI answers that with
        /// "you are pushing more GUIElements now" every frame afterwards. So the draw enqueues and
        /// Tick performs, exactly like the three buttons already do.</summary>
        private readonly ConcurrentQueue<Action> edits = new ConcurrentQueue<Action>();
        private readonly ConcurrentQueue<Job> done = new ConcurrentQueue<Job>();
        private readonly Dictionary<string, string> aliases = new Dictionary<string, string>(StringComparer.Ordinal);
        /// <summary>What the sidecar already held when this file was opened. SaveSidecar rewrites the
        /// WHOLE bones object, so a Doctor that starts empty and saves deletes every mapping the author
        /// made in an earlier session. This is the baseline the editor is seeded from, and the thing
        /// "has the map changed?" is asked against.</summary>
        private readonly Dictionary<string, string> seeded = new Dictionary<string, string>(StringComparer.Ordinal);
        private string seededFor;
        private bool canSave;
        /// <summary>The Meshes this Doctor built, held by REFERENCE rather than by instance id: Unity
        /// 2019's runtime Resources has no InstanceIDToObject, and the owner keeping what it must give
        /// back is the plainer arrangement anyway.</summary>
        private readonly List<Mesh> ourMeshes = new List<Mesh>();

        private sealed class Job
        {
            internal int Gen;
            internal ReplacementPreflightResult Result;
        }

        private int gen;
        private volatile bool running;

        internal string Path;
        internal SkinnedMeshRenderer Renderer;
        internal RigTarget Target;
        internal ReplacementPreflightResult Ready;
        internal string Message = "";

        /// <summary>The mesh this Doctor put on the renderer, and the one it took off, so Revert puts
        /// back the OBJECT rather than something that looks like it.</summary>
        private Mesh preview;
        private Mesh origin;
        private Bounds originBounds;

        internal bool Busy => running;
        internal bool HasPreview => preview != null;

        /// <summary>How many Meshes this Doctor has built and not yet given back. Zero after a Revert
        /// or a Dispose; anything else in the bench's leak count belongs to someone other than us.</summary>
        internal int OurMeshCount => ourMeshes.Count;

        /// <summary>
        /// Destroy every Mesh this Doctor made that is still alive. A native mesh is not garbage
        /// collected with its C# wrapper: a preview candidate the author replaced, or one refused after
        /// it was built, stays in memory until the process ends unless it is destroyed by hand.
        /// </summary>
        private void Sweep()
        {
            foreach (Mesh m in ourMeshes) if (m != null) UnityEngine.Object.Destroy(m);
            ourMeshes.Clear();
        }

        // ------------------------------------------------------------------ picking

        internal void PickFile(string path)
        {
            Path = path;
            aliases.Clear();
            seeded.Clear();
            seededFor = null;                          // the sidecar seeds it when the first result lands
            canSave = false;
            Restart();
        }

        internal void PickTarget(SkinnedMeshRenderer smr, string transformPath)
        {
            Renderer = smr;
            Target = Snapshot(smr, transformPath);
            Restart();
        }

        internal void SetAlias(string fileBone, string targetBone)
        {
            if (string.IsNullOrEmpty(targetBone)) aliases.Remove(fileBone);
            else aliases[fileBone] = targetBone;
            Rethink();
            Restart();
        }

        /// <summary>Can this map be written? Asked HERE and cached, not in the draw: AliasMap.Of builds
        /// two collections, and a button that asks it twice a frame allocates for the whole session.</summary>
        private void Rethink()
        {
            bool differs = aliases.Count != seeded.Count;
            if (!differs)
                foreach (KeyValuePair<string, string> e in aliases)
                {
                    string was;
                    if (seeded.TryGetValue(e.Key, out was) && was == e.Value) continue;
                    differs = true;
                    break;
                }
            canSave = differs && aliases.Count > 0 && AliasMap.Of(aliases) != null;
        }

        /// <summary>The sidecar's own mappings become the editor's starting rows, once per file. Without
        /// this the table shows nothing while the sidecar holds three names, and the first Save writes a
        /// map of one - the two the author could not see are gone, silently.</summary>
        private void Seed()
        {
            if (seededFor == Path || Ready == null) return;
            seededFor = Path;
            seeded.Clear();
            if (Ready.Source != null && Ready.Source.Aliases != null)
                foreach (KeyValuePair<string, string> e in Ready.Source.Aliases.Pairs)
                {
                    seeded[e.Key] = e.Value;
                    if (!aliases.ContainsKey(e.Key)) aliases[e.Key] = e.Value;
                }
            Rethink();
        }

        internal void Enqueue(string what)
        {
            if (what == "preview") intents.Enqueue(Intent.Preview);
            else if (what == "revert") intents.Enqueue(Intent.Revert);
            else if (what == "save") intents.Enqueue(Intent.Save);
        }

        /// <summary>
        /// A plain copy of everything about the target that a preview depends on. Taken on the main
        /// thread, and compared again immediately before every swap: a SkinnedMeshRenderer keeps its
        /// instance id while another mod, an addon or the bench's own rebuild replaces its mesh, its
        /// bind poses and its bones underneath it.
        /// </summary>
        internal static RigTarget Snapshot(SkinnedMeshRenderer smr, string transformPath)
        {
            var t = new RigTarget { TransformPath = transformPath ?? "" };
            if (smr == null) return t;
            t.RendererInstanceId = smr.GetInstanceID();
            Transform[] bones = smr.bones;
            if (bones != null && bones.Length > 0)
            {
                t.BoneNames = new string[bones.Length];
                for (int b = 0; b < bones.Length; b++) t.BoneNames[b] = bones[b] == null ? "" : bones[b].name;
            }
            Mesh mesh = smr.sharedMesh;
            if (mesh == null) return t;
            t.MeshInstanceId = mesh.GetInstanceID();
            t.MeshName = mesh.name ?? "";
            Matrix4x4[] poses = mesh.bindposes;
            t.BindPoseCount = poses == null ? 0 : poses.Length;
            t.Rigged = t.BindPoseCount > 0;
            return t;
        }

        // ------------------------------------------------------------------ the job

        /// <summary>Every change bumps the generation, so an answer that was already in flight is
        /// dropped when it lands rather than cancelled - the worker has nothing to roll back.</summary>
        private void Restart()
        {
            gen++;
            Ready = null;
            if (Path == null || Target == null) return;
            Start(gen);
        }

        private void Start(int forGen)
        {
            if (running) return;                       // Update starts the next one when this returns
            running = true;
            string path = Path;
            RigTarget target = Target;
            var map = new Dictionary<string, string>(aliases, StringComparer.Ordinal);
            ThreadPool.QueueUserWorkItem(delegate
            {
                var job = new Job { Gen = forGen };
                try
                {
                    byte[] bytes = File.ReadAllBytes(path);
                    job.Result = ReplacementPreflight.Run(bytes, path, target);
                    ApplyLiveAliases(job.Result, bytes, map, target);
                }
                catch (Exception ex)
                {
                    job.Result = new ReplacementPreflightResult { Outcome = Outcome.Refused, Failure = ex };
                    job.Result.Report.Outcome = Outcome.Refused;
                    job.Result.Report.Add("ImportFailed", Severity.Blocking, DiagnosticSide.File,
                                          "'" + path + "' could not be read: " + ex.GetType().Name + " - " + ex.Message,
                                          "Check the file is not open in another program and try again.");
                }
                done.Enqueue(job);
            });
        }

        /// <summary>
        /// The aliases the author is editing RIGHT NOW, which are not in the sidecar yet. Applied to
        /// the PRISTINE names every time, so the edits are order-independent and a swap of two names
        /// works - the same rule AliasMap.Apply keeps for the saved ones.
        ///
        /// The map lands on a THIRD read of the bytes rather than on result.Original: Original is what
        /// the alias table lists as its keys, and a table whose keys are its own outputs turns the next
        /// keystroke into a second row for a bone that no longer exists. One extra parse, on the worker,
        /// and only while the author has an unsaved edit.
        /// </summary>
        private static void ApplyLiveAliases(ReplacementPreflightResult result, byte[] bytes,
                                             Dictionary<string, string> map, RigTarget target)
        {
            if (result.Original == null || map.Count == 0) return;
            AliasMap live = AliasMap.Of(map);
            if (live == null) return;
            SkinnedModel model = GlbReader.Read(bytes);
            IList<string> unused;
            live.Apply(model, out unused);
            var report = new DiagnosticReport();
            // The sidecar's own verdict SURVIVES the rebuild. It is a fact about the file on disk, not
            // about the map being typed, and an author whose sidecar is stale has to keep seeing that
            // while they edit - otherwise the warning vanishes the moment they start fixing it.
            foreach (Diagnostic d in result.Report.Rows)
                if (d.Code == "SidecarStale" || d.Code == "SidecarInvalid")
                    report.Add(d.Code, d.Severity, d.Side, d.Message, d.Remedy, d.Subject);
            result.Model = model;
            result.Report = report;
            // The SAME three arms the first pass took. Rebuilding them here is how a refusal ends up
            // with a header and no reason under it.
            ReplacementPreflight.Judge(result, model, target);
            foreach (string key in unused)
                report.Add("AliasUnused", Severity.Warning, DiagnosticSide.Sidecar,
                           "the alias for '" + key + "' was ignored: this file has no bone of that name",
                           "Delete the row, or rename the bone in Blender to '" + key + "'.", key);
            foreach (string key in live.OutputsNotIn(target.BoneNames))
                report.Add("AliasNotATargetBone", Severity.Warning, DiagnosticSide.Sidecar,
                           "the alias for '" + key + "' names a bone this model's skeleton does not have",
                           "Pick the target bone from the list instead of typing it.", key);
        }

        // ------------------------------------------------------------------ the main thread

        /// <summary>Called every frame from the bench's Update. Drains results, then intents.</summary>
        internal void Tick()
        {
            // Every edit runs. The ONE that cannot outlive its actor - picking a renderer off the stand -
            // carries its own check at the point it was queued, because only that edit knows which actor
            // it was chosen from. Picking a FILE with no actor is fine: the report waits for a target.
            Action edit;
            while (edits.TryDequeue(out edit)) edit();

            Job job;
            while (done.TryDequeue(out job))
            {
                running = false;
                if (job.Gen != gen) continue;                          // stale: the author moved on
                if (Target == null) continue;                          // Dispose ran while it was in flight
                RigTarget now = Snapshot(Renderer, Target.TransformPath);
                if (!now.SameAs(Target))
                {
                    // The report was made against a rig that no longer exists. It is not annotated and
                    // shown - it is thrown away and asked again, because a verdict about the previous
                    // mesh with a warning under it is still a verdict the author will read as this one's.
                    Target = now;
                    Message = "the target changed while it was being read - reading it again";
                    Restart();
                    continue;
                }
                Ready = job.Result;
                // §7: the REPORT says what happened in the author's words, and the exception behind it
                // goes to the log - the one place a stack trace helps and the only place it belongs.
                if (Ready.Failure != null)
                    Debug.LogError("[ContentTool] Model Doctor: '" + Path + "' - " +
                                   Ready.Failure.GetType().Name + ": " + Ready.Failure.Message + "\n" +
                                   Ready.Failure.StackTrace);
                Seed();
            }
            if (!running && Ready == null && Path != null && Target != null) Start(gen);

            Intent intent;
            while (intents.TryDequeue(out intent))
            {
                if (intent == Intent.Preview) Message = DoPreview();
                else if (intent == Intent.Revert) Message = Revert();
                else Message = DoSave();
            }
        }

        private string DoPreview()
        {
            if (Ready == null) return "nothing to preview yet";
            if (Ready.Outcome == Outcome.Refused || Ready.Outcome == Outcome.NotRigged)
                return "this file would not be written at all, so there is nothing to preview";

            string stale = Stale();
            if (stale != null) { Restart(); return stale; }

            RigTarget now = Snapshot(Renderer, Target.TransformPath);
            if (!now.SameAs(Target)) { Target = now; Restart(); return "the target changed - reading it again"; }

            Mesh candidate = LiveMesh.Build(Ready.Model, System.IO.Path.GetFileName(Path));
            ourMeshes.Add(candidate);
            LiveMesh.BindMode mode;
            string how = LiveMesh.Bind(candidate, Renderer, Ready.Model, out mode);
            Outcome got = mode == LiveMesh.BindMode.ByName ? Outcome.ByName
                        : mode == LiveMesh.BindMode.NearestBone ? Outcome.NearestBone : Outcome.NotRigged;
            if (got != Ready.Outcome)
            {
                // CANDIDATE-THEN-SWAP: the preview that is already on screen is untouched, and the one
                // that disagreed with the prediction is destroyed rather than shown. A wrong skinning
                // shown confidently is worse than none.
                UnityEngine.Object.Destroy(candidate);
                // No row is added: Restart throws this report away, and a row on a discarded report is
                // one nobody can read. The sentence goes to Message, which the panel draws above the
                // "reading..." the restart puts on screen.
                string said = "preview REFUSED: the live bind came out " + got + " where the report said " +
                              Ready.Outcome + " - the model changed under it, reading it again";
                Restart();                                  // ask again against whatever the rig is NOW
                return said;
            }

            if (preview == null)
            {
                origin = Renderer.sharedMesh;
                originBounds = Renderer.localBounds;
            }
            else UnityEngine.Object.Destroy(preview);
            preview = candidate;
            Renderer.sharedMesh = candidate;
            Renderer.localBounds = candidate.bounds;
            return "preview: " + how;
        }

        internal string Revert()
        {
            bool had = preview != null;
            if (had && Renderer != null)
            {
                Renderer.sharedMesh = origin;
                Renderer.localBounds = originBounds;
            }
            // The renderer is given its own mesh back FIRST, then everything we built is destroyed -
            // including candidates that were refused and never reached a renderer at all.
            preview = null;
            Sweep();
            return had ? "preview reverted - the game's own mesh is back, by reference" : "no preview is live";
        }

        private string DoSave()
        {
            if (Path == null || Ready == null) return "nothing to save";
            if (aliases.Count == 0) return "no aliases to save";
            string stale = Stale();
            if (stale != null) { Restart(); return stale; }
            try
            {
                byte[] bytes = File.ReadAllBytes(Path);
                AliasMap.SaveSidecar(Path, AliasMap.Sha256(bytes), bytes.Length, aliases);
                // What is on disk is the new baseline, so the button goes quiet until something changes
                // again - a Save that stays lit is a Save the author presses twice to be sure.
                seeded.Clear();
                foreach (KeyValuePair<string, string> e in aliases) seeded[e.Key] = e.Value;
                Rethink();
                Say(Ready.Report, "AliasesSaved", Severity.Info, DiagnosticSide.Sidecar,
                    aliases.Count + " alias(es) saved to " + AliasMap.SidecarPathOf(Path), "");
                return "saved " + aliases.Count + " alias(es) to " + AliasMap.SidecarPathOf(Path);
            }
            catch (Exception ex) { return "could not save: " + ex.Message; }
        }

        /// <summary>
        /// Add a row the REPORT did not produce, replacing the one this Doctor last said. Pressing a
        /// button twice is not two facts, and a report that grows a line per press is one an author
        /// stops reading. A Blocking row also moves the verdict: a header still reading BY NAME above a
        /// refusal is the panel contradicting itself.
        /// </summary>
        private static void Say(DiagnosticReport report, string code, Severity severity, DiagnosticSide side,
                                string message, string remedy)
        {
            report.Rows.RemoveAll(delegate (Diagnostic d) { return d.Code == code; });
            report.Add(code, severity, side, message, remedy);
            if (severity == Severity.Blocking) report.Outcome = Outcome.Refused;
        }

        /// <summary>
        /// Has the .glb changed since the report was made? An author re-exports from Blender while
        /// this panel is open, and saving then would bind names authored against the OLD joints to the
        /// NEW file's hash - a sidecar that is wrong and looks right.
        /// </summary>
        private string Stale()
        {
            try
            {
                if (AliasMap.Sha256(File.ReadAllBytes(Path)) == Ready.Sha256) return null;
                return "the .glb has changed on disk since this report - reading it again";
            }
            catch (Exception ex) { return "the .glb could not be re-read: " + ex.Message; }
        }

        // ------------------------------------------------------------------ the panel

        private readonly GlbFileBrowser browser = new GlbFileBrowser();
        private Vector2 rowScroll;
        private bool mapOpen;
        private bool targetsOpen;
        private string boneOpen;
        private SkinnedMeshRenderer[] candidates = new SkinnedMeshRenderer[0];

        private Transform root;

        /// <summary>
        /// The benched actor the target list is drawn from. Set by the bench, which is the only thing
        /// that knows what is on the stand; the Doctor does not go looking for it.
        ///
        /// A NEW actor invalidates everything downstream of it - the renderer, its fingerprint and the
        /// report made against it all belong to a model that is no longer here - so the setter is the
        /// generation trigger for a unit swap. The bench assigns this every time it poses one, which is
        /// why nothing else has to remember to tell the Doctor.
        /// </summary>
        internal Transform Root
        {
            get { return root; }
            set
            {
                // ReferenceEquals, not ==: Unity's operator calls a DESTROYED object equal to null, so a
                // bench that drops a dead actor with Root = null would compare equal and skip the reset -
                // leaving a report and a preview belonging to a renderer that no longer exists.
                if (ReferenceEquals(root, value)) return;
                Revert();                                  // the preview belongs to the OLD renderer
                root = value;
                Renderer = null;
                Target = null;
                Ready = null;
                candidates = new SkinnedMeshRenderer[0];
                targetsOpen = false;
                gen++;                                     // an answer in flight is about the old actor
            }
        }

        /// <summary>
        /// Draws the whole Doctor. READS ONLY: every button enqueues an intent that Tick performs on
        /// the next frame, because mutating Unity objects inside OnGUI is how an IMGUI layout ends up
        /// unbalanced and the panel throws every frame afterwards.
        /// </summary>
        internal void Draw(float width)
        {
            // Open is tested BEFORE Draw and the frame ends there: the browser reorders its own recents
            // during the mouse pass, so a second Draw in the same frame lays out a different list.
            if (browser.Open)
            {
                string picked = browser.Draw(260f);
                if (picked != null) { string p = picked; edits.Enqueue(delegate { PickFile(p); }); }
                return;
            }

            float col = Mathf.Max(120f, (width - 80f) * 0.45f);

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Browse...", GUILayout.Width(90f)))
                browser.Show(Path == null ? "" : System.IO.Path.GetDirectoryName(Path));
            GUILayout.Label("source: " + BenchList.Elide(Path == null ? "-" : System.IO.Path.GetFileName(Path), 40));
            if (Ready != null && Ready.Source != null && Ready.Source.AliasesApplied > 0)
                GUILayout.Label("ALIASES ACTIVE (" + Ready.Source.AliasesApplied + ")");
            GUILayout.EndHorizontal();
            if (Ready != null && Ready.Baked != null && Ready.Baked.Mesh != null)
                GUILayout.Label("   " + Ready.Baked.Mesh.VertexCount + " verts, " +
                                Ready.Baked.Mesh.IndexCount / 3 + " tris, " +
                                (Ready.Model == null ? 0 : Ready.Model.JointNames.Count) + " joints, " +
                                Ready.Baked.Influences + " influence(s)/vertex");

            Targets();
            // ABOVE the early returns. What the last press did is most worth reading exactly when the
            // panel has nothing else to show - a refused preview restarts the report, and a message
            // drawn under a verdict that is not there yet is a message nobody ever sees.
            if (Message.Length > 0) GUILayout.Label(Message);

            if (Path == null || Target == null)
            {
                GUILayout.Label("pick a .glb and a skinned mesh to see what the bake would do with them");
                return;
            }
            if (Ready == null) { GUILayout.Label(Busy ? "reading..." : "queued..."); return; }

            GUILayout.Space(4f);
            GUILayout.Label(Ready.Report.Header());
            GUILayout.Space(2f);

            if (Ready.Outcome == Outcome.NearestBone && Ready.Model != null && Target.BoneNames != null)
                BoneMap(col);

            rowScroll = GUILayout.BeginScrollView(rowScroll, GUILayout.Height(200f));
            Rows(Severity.Blocking, "REFUSED");
            Rows(Severity.Downgrade, "LOSES YOUR WEIGHTS");
            Rows(Severity.Warning, "IGNORED");
            Rows(Severity.Info, "NOTE");
            GUILayout.EndScrollView();

            GUILayout.BeginHorizontal();
            // A Blocking row is a refusal even when the VERDICT was reached before it - a live bind that
            // disagreed, a rig that moved. Preview follows the rows, not the outcome it was born with.
            GUI.enabled = (Ready.Outcome == Outcome.ByName || Ready.Outcome == Outcome.NearestBone) &&
                          Ready.Report.Count(Severity.Blocking) == 0;
            if (GUILayout.Button("Preview", GUILayout.Width(80f))) Enqueue("preview");
            GUI.enabled = HasPreview;
            if (GUILayout.Button("Revert preview", GUILayout.Width(110f))) Enqueue("revert");
            // Changed AND valid, decided in Rethink: an unchanged map rewrites the sidecar for nothing,
            // and a map AliasMap.Of refuses would be refused again by the loader about to read it.
            GUI.enabled = canSave;
            if (GUILayout.Button("Save aliases", GUILayout.Width(110f))) Enqueue("save");
            GUI.enabled = true;
            if (GUILayout.Button("Copy report", GUILayout.Width(100f)))
                GUIUtility.systemCopyBuffer = PlainTextOf(Ready, Path, Target);
            GUILayout.EndHorizontal();
        }

        /// <summary>The target block: what is picked, and the renderers on the stand to pick from. The
        /// list is taken once when it opens - GetComponentsInChildren allocates, and a panel that calls
        /// it twice a frame forever is a garbage collection the author feels as a stutter.</summary>
        private void Targets()
        {
            GUILayout.BeginHorizontal();
            if (GUILayout.Button((targetsOpen ? "v " : "> ") + "Change", GUILayout.Width(90f)))
            {
                targetsOpen = !targetsOpen;
                candidates = targetsOpen && Root != null
                    ? Root.GetComponentsInChildren<SkinnedMeshRenderer>(true) : new SkinnedMeshRenderer[0];
            }
            GUILayout.Label(Target == null
                ? "target: -"
                : "target: " + BenchList.Elide(Target.TransformPath, 34) + "  (" +
                  (Target.BoneNames == null ? 0 : Target.BoneNames.Length) + " bones, mesh '" +
                  BenchList.Elide(Target.MeshName, 20) + "')");
            GUILayout.EndHorizontal();
            if (!targetsOpen) return;

            if (candidates.Length == 0) GUILayout.Label("   nothing on the stand carries a skinned mesh");
            foreach (SkinnedMeshRenderer r in candidates)
            {
                if (r == null) continue;
                string path = SeamSwap.RelativePath(root, r.transform);
                if (!GUILayout.Button("   " + BenchList.Elide(path.Length == 0 ? r.name : path, BenchList.NameChars) +
                                      "  (" + (r.bones == null ? 0 : r.bones.Length) + " bones)")) continue;
                SkinnedMeshRenderer chosen = r;
                string chosenPath = path;
                Transform from = root;                     // the actor this renderer was chosen off
                edits.Enqueue(delegate
                {
                    if (ReferenceEquals(root, from)) PickTarget(chosen, chosenPath);
                });
                targetsOpen = false;
            }
        }

        /// <summary>
        /// ONE table, file bones on the left and the target bone each one will land on - or a dash -
        /// on the right. The right cell offers only target bones NOTHING else claims, so the map stays
        /// bijective by construction rather than by a check afterwards, and the closest name is merely
        /// SHOWN as a suggestion: nothing is ever applied on the author's behalf, because a wrong bone
        /// quietly chosen for them is the exact failure this whole panel exists to end.
        /// </summary>
        private void BoneMap(float col)
        {
            GUILayout.BeginHorizontal();
            if (GUILayout.Button((mapOpen ? "v " : "> ") + "bone map", GUILayout.Width(110f))) mapOpen = !mapOpen;
            GUILayout.EndHorizontal();
            if (!mapOpen) return;

            var free = new List<string>();                 // target bones the file matched nothing to
            foreach (Diagnostic d in Ready.Report.Rows)
                if (d.Code == "MissingBone" && d.Subject != null) free.Add(d.Subject);

            foreach (Diagnostic d in Ready.Report.Rows)
            {
                if (d.Code != "ExtraBone" || d.Subject == null) continue;
                string fileBone = d.Subject;
                string current;
                aliases.TryGetValue(fileBone, out current);

                GUILayout.BeginHorizontal();
                GUILayout.Label(BenchList.Elide(fileBone, 30), GUILayout.Width(col));
                GUILayout.Label("->", GUILayout.Width(20f));
                string suggestion = current ?? Suggest(fileBone, free);
                if (GUILayout.Button((current == null ? "- " : "") +
                                     BenchList.Elide(current ?? (suggestion == null ? "(pick a bone)" : suggestion + "?"), 30),
                                     GUILayout.Width(col)))
                    boneOpen = boneOpen == fileBone ? null : fileBone;
                GUILayout.EndHorizontal();
                if (boneOpen != fileBone) continue;

                if (current != null && GUILayout.Button("      (none)"))
                {
                    string k = fileBone;
                    edits.Enqueue(delegate { SetAlias(k, null); });
                    boneOpen = null;
                }
                foreach (string bone in free)
                {
                    if (Claimed(bone, fileBone)) continue;
                    if (!GUILayout.Button("      " + BenchList.Elide(bone, BenchList.NameChars) +
                                          (bone == suggestion ? "   <- closest" : ""))) continue;
                    string k = fileBone, v = bone;
                    edits.Enqueue(delegate { SetAlias(k, v); });
                    boneOpen = null;
                }
            }

            // WHAT IS STILL UNSPOKEN FOR, said outside any dropdown: the bones the target has and this
            // file answered nothing for are the whole reason the weights are being lost, and an author
            // cannot count them by opening every row's list one at a time.
            var open = new List<string>();
            foreach (string bone in free) if (!Claimed(bone, null)) open.Add(bone);
            GUILayout.Label(open.Count == 0
                ? "   every target bone is spoken for"
                : "   unmatched target bones (" + open.Count + "): " +
                  BenchList.Elide(string.Join(", ", open.ToArray()), 110));
        }

        /// <summary>Is this target bone already the output of some OTHER file bone? Two file bones on
        /// one game bone is the PlainCollision the binder refuses, so it is never offered.</summary>
        private bool Claimed(string targetBone, string exceptFor)
        {
            foreach (KeyValuePair<string, string> e in aliases)
                if (e.Value == targetBone && e.Key != exceptFor) return true;
            return false;
        }

        /// <summary>The likeliest target bone for a file bone, or null. Decoration first - '#X_Addon =&gt;
        /// Def' and 'X' are the SAME bone to the binder (SkinBinder.Plain) - then case, then the longest
        /// shared tail, which is what 'L_UpperArm' and 'UpperArm_L' have in common and a human sees
        /// instantly. A suggestion only: it is drawn with a '?' and applies to nothing until clicked.</summary>
        private static string Suggest(string fileBone, List<string> free)
        {
            string plain = SkinBinder.Plain(fileBone);
            foreach (string b in free) if (b == plain) return b;
            foreach (string b in free) if (string.Equals(b, plain, StringComparison.OrdinalIgnoreCase)) return b;
            string best = null;
            int bestTail = 2;                              // under three characters is noise, not a match
            foreach (string b in free)
            {
                int n = 0;
                while (n < b.Length && n < plain.Length &&
                       char.ToLowerInvariant(b[b.Length - 1 - n]) == char.ToLowerInvariant(plain[plain.Length - 1 - n])) n++;
                if (n > bestTail) { bestTail = n; best = b; }
            }
            return best;
        }

        /// <summary>One severity group. The game's own model is drawn APART and last: "this is not your
        /// file" is the difference between a fix and a dead end, and a target row mixed in with the
        /// author's own reads as one more thing they exported wrong.</summary>
        private void Rows(Severity severity, string heading)
        {
            bool any = false, theirs = false;
            for (int pass = 0; pass < 2; pass++)
                foreach (Diagnostic d in Ready.Report.Rows)
                {
                    if (d.Severity != severity) continue;
                    if ((d.Side == DiagnosticSide.Target) != (pass == 1)) continue;
                    if (!any) { GUILayout.Label(heading); any = true; }
                    if (pass == 1 && !theirs)
                    {
                        GUILayout.Label("  -- the game's model, not your file --");
                        theirs = true;
                    }
                    GUILayout.Label("  " + (d.Side == DiagnosticSide.Sidecar ? "[aliases] " : "") + d.Message);
                    if (d.Remedy.Length > 0) GUILayout.Label("      " + d.Remedy);
                }
        }

        /// <summary>
        /// What a non-programmer pastes when they ask for help. Pure and static so it stays testable
        /// and so the panel's own Copy button is one line - the report is data, and turning data into
        /// text has no business knowing about IMGUI.
        /// </summary>
        internal static string PlainTextOf(ReplacementPreflightResult result, string path, RigTarget target)
        {
            var sb = new System.Text.StringBuilder();
            sb.Append("MODEL DOCTOR\n");
            sb.Append("file:   ").Append(path ?? "(none)").Append('\n');
            sb.Append("target: ").Append(target == null ? "(none)" : target.TransformPath).Append('\n');
            if (result == null) { sb.Append("no report yet\n"); return sb.ToString(); }
            sb.Append("sha256: ").Append(result.Sha256 ?? "(unknown)").Append('\n');
            sb.Append("bones:  ").Append(target == null || target.BoneNames == null ? 0 : target.BoneNames.Length)
              .Append(" live, ").Append(target == null ? 0 : target.BindPoseCount).Append(" bind pose(s)\n");
            sb.Append("verdict: ").Append(result.Report.Header()).Append('\n');
            foreach (Diagnostic d in result.Report.Rows)
            {
                sb.Append("  [").Append(d.Severity).Append('/').Append(d.Side).Append("] ")
                  .Append(d.Code).Append(": ").Append(d.Message).Append('\n');
                if (!string.IsNullOrEmpty(d.Remedy)) sb.Append("      -> ").Append(d.Remedy).Append('\n');
            }
            return sb.ToString();
        }

        /// <summary>Everything this Doctor owns, given back. Called when the bench closes. The
        /// generation moves too, so a job still in flight lands on a Doctor that no longer wants it.</summary>
        internal void Dispose()
        {
            Revert();
            browser.Hide();
            gen++;
            // Drained, not left queued: a button pressed on the frame the bench closed would otherwise
            // run against a Doctor with nothing behind it the next time one is opened. The gen bump
            // covers the worker; only what the AUTHOR asked for has to be thrown away by hand.
            Action edit;
            while (edits.TryDequeue(out edit)) { }
            Intent intent;
            while (intents.TryDequeue(out intent)) { }
            Job job;
            while (done.TryDequeue(out job)) { }
            Path = null;
            root = null;
            Renderer = null;
            Target = null;
            Ready = null;
            candidates = new SkinnedMeshRenderer[0];
            targetsOpen = false;
            aliases.Clear();
            seeded.Clear();
            seededFor = null;
            canSave = false;
        }
    }
}
