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
        /// <summary>The PROTOTYPE the author picked - record, variant, slot and mode - or null when
        /// nothing has been picked from the browser. The verdict is still computed from
        /// <see cref="Target"/>; this is what the header names and what the preflight will be pointed
        /// at once it takes a prototype.</summary>
        internal PrototypeTarget Prototype;
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
            // BEFORE the renderer moves. preview/origin/originBounds all belong to the renderer being
            // left behind; changing it first strands them - the next preview destroys a mesh still
            // assigned to the old renderer, and a later Revert puts the old model's mesh on the new one.
            Revert();
            Prototype = null;                          // an arbitrary renderer is not a prototype pick
            Renderer = smr;
            Target = Snapshot(smr, transformPath);
            Restart();
        }

        /// <summary>
        /// THE PICK THE BROWSER MAKES. A prototype target is not a function of who is on the stand: the
        /// bay was rebuilt as this variant, and this slot's renderer is the one that rebuild produced.
        ///
        /// The live renderer is found back through <see cref="Root"/> and the snapshot's own transform
        /// path - the same path <c>SeamSwap.RelativePath</c> wrote against the same root - so the
        /// freshness check in <see cref="Tick"/> and the preview both keep working. A slot that
        /// produced none, and the Extend path, carry no renderer at all and say so rather than being
        /// given a target fabricated from the full hierarchy.
        /// </summary>
        internal void PickTarget(PrototypeTarget target)
        {
            SkinnedMeshRenderer smr = target != null && target.Mode == VerifyMode.Replace
                ? Resolve(target) : null;
            // BEFORE the renderer moves - the same reason PickTarget(smr, path) reverts first: the
            // preview and its origin belong to the renderer being left behind.
            Revert();
            Renderer = smr;
            Target = smr == null ? null : Snapshot(smr, target.Live.TransformPath);
            // BEFORE Restart, because Restart now reads it: an Extend pick has no RigTarget at all and
            // is still a job to run.
            Prototype = target;
            Restart();
        }

        private SkinnedMeshRenderer Resolve(PrototypeTarget target)
        {
            if (target == null || target.Live == null || root == null) return null;
            if (string.IsNullOrEmpty(target.Live.TransformPath)) return null;
            Transform found = root.Find(target.Live.TransformPath);
            return found == null ? null : found.GetComponent<SkinnedMeshRenderer>();
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
            // An EMPTY map that used to have entries is still a change - it means "take the sidecar
            // away", which Save does by deleting the file. Without this arm the last alias cannot be
            // removed: the button greys out and the sidecar the author just emptied keeps binding.
            canSave = !Same(aliases, seeded) &&
                      (aliases.Count > 0 ? AliasMap.Of(aliases) != null : SidecarExists());
        }

        private bool SidecarExists()
        {
            try { return Path != null && File.Exists(AliasMap.SidecarPathOf(Path)); }
            catch (Exception) { return false; }
        }

        private static bool Same(Dictionary<string, string> a, Dictionary<string, string> b)
        {
            if (a.Count != b.Count) return false;
            foreach (KeyValuePair<string, string> e in a)
            {
                string was;
                if (!b.TryGetValue(e.Key, out was) || was != e.Value) return false;
            }
            return true;
        }

        /// <summary>
        /// The sidecar's own mappings become the editor's starting rows, once per FILE CONTENT. Without
        /// this the table shows nothing while the sidecar holds three names, and the first Save writes a
        /// map of one - the two the author could not see are gone, silently.
        ///
        /// Keyed on the hash and not the path, because the interesting case is a re-export over the same
        /// name: the sidecar goes stale, the preflight stops applying it, and a Doctor still holding the
        /// old map in memory would keep promising an outcome the bake will not produce.
        /// </summary>
        private void Seed()
        {
            if (Ready == null) return;
            // A result that FAILED carries no hash, and a key built from one that is missing looks like
            // a different file - which would clear the author's unsaved edits over a lock Blender holds
            // for a second mid-export. A failure says nothing about the sidecar, so it says nothing here.
            if (Ready.Failure != null || Ready.Sha256 == null) return;
            string key = Path + "|" + Ready.Sha256;
            if (seededFor == key) return;
            seededFor = key;

            var was = new Dictionary<string, string>(aliases, StringComparer.Ordinal);
            aliases.Clear();
            seeded.Clear();
            if (Ready.Source != null && Ready.Source.Aliases != null)
                foreach (KeyValuePair<string, string> e in Ready.Source.Aliases.Pairs)
                {
                    seeded[e.Key] = e.Value;
                    aliases[e.Key] = e.Value;
                }
            Rethink();
            // The report that just landed was computed with the map we held a moment ago. If seeding
            // changed it, that verdict is about a file that is no longer on disk - ask again.
            if (!Same(was, aliases)) Restart();
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
            if (Path == null || !Verifiable) return;
            Start(gen);
        }

        /// <summary>Is there anything to hold the file against? A live renderer's snapshot, or a
        /// prototype - which on the Extend path is a bone list and no renderer at all.</summary>
        private bool Verifiable { get { return Target != null || Prototype != null; } }

        private void Start(int forGen)
        {
            if (running) return;                       // Update starts the next one when this returns
            running = true;
            string path = Path;
            RigTarget target = Target;
            PrototypeTarget proto = Prototype;
            var map = new Dictionary<string, string>(aliases, StringComparer.Ordinal);
            ThreadPool.QueueUserWorkItem(delegate
            {
                var job = new Job { Gen = forGen };
                try
                {
                    byte[] bytes = File.ReadAllBytes(path);
                    // A LIVE SNAPSHOT WINS. Replace - picked from the browser or not - is judged against
                    // the renderer as it is NOW, which is the shipped path unchanged and the one the
                    // freshness check in Tick compares against. Only a pick with no renderer behind it
                    // (Extend, or a slot the rebuild produced none for) goes the prototype way.
                    job.Result = target != null ? ReplacementPreflight.Run(bytes, path, target)
                                                : ReplacementPreflight.Run(bytes, path, proto);
                    ApplyLiveAliases(job.Result, bytes, map, target, proto);
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
                                             Dictionary<string, string> map, RigTarget target,
                                             PrototypeTarget proto)
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
            // The SAME arms the first pass took, and the same target it was judged against - a live
            // snapshot when there is one, the prototype when there is not.
            if (target != null) ReplacementPreflight.Judge(result, model, target);
            else ReplacementPreflight.Judge(result, model, proto);
            foreach (string key in unused)
                report.Add("AliasUnused", Severity.Warning, DiagnosticSide.Sidecar,
                           "the alias for '" + key + "' was ignored: this file has no bone of that name",
                           "Delete the row, or rename the bone in Blender to '" + key + "'.", key);
            foreach (string key in live.OutputsNotIn(target != null ? target.BoneNames
                                                                   : ReplacementPreflight.BoneArray(proto)))
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
                if (!Verifiable) continue;                             // Dispose ran while it was in flight
                // An EXTEND report is about a bone list, not a renderer, so there is nothing under it
                // that could have moved while it was being read.
                RigTarget now = Target == null ? null : Snapshot(Renderer, Target.TransformPath);
                if (now != null && !now.SameAs(Target))
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
            if (!running && Ready == null && Path != null && Verifiable) Start(gen);

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
            if (Renderer == null || Target == null)
                return "there is no live renderer behind this target, so there is nothing to preview on";

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
            string stale = Stale();
            if (stale != null) { Restart(); return stale; }
            // EMPTYING the map removes the sidecar. Writing "{}" would leave a file the loader still
            // reads and the author still has to reason about; the absence of a sidecar is the state
            // they asked for by clearing the last row.
            if (aliases.Count == 0) return DropSidecar();
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

        private string DropSidecar()
        {
            string sidecar = AliasMap.SidecarPathOf(Path);
            if (!SidecarExists()) { seeded.Clear(); Rethink(); return "there is no sidecar to remove"; }
            try
            {
                File.Delete(sidecar);
                Debug.Log("[ContentTool] Model Doctor: removed the bone map '" + sidecar + "'");
                seeded.Clear();
                Rethink();
                // No row: Restart throws this report away. Message carries the sentence, and the next
                // report - computed without the sidecar - is the honest answer about what happens now.
                Restart();
                return "sidecar removed: " + sidecar;
            }
            catch (Exception ex) { return "could not remove the sidecar: " + ex.Message; }
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
        private string boneOpen;

        // ---- the prototype browser. EVERY field here is read by the draw and written ONLY by Refresh
        // (which runs on the Layout pass) or by an edit the draw enqueued (which runs in Tick, i.e. in
        // Update, before the next Layout). Nothing that decides how many controls exist may move
        // between the Layout pass and the Repaint pass that follows it.
        private bool browserOpen;
        private string query = "";
        private string queried;                        // the text `shown` was filtered for
        private IList<PrototypeRecord> all = new PrototypeRecord[0];
        private IList<PrototypeRecord> seen;           // the list `shown` was filtered FROM, by reference
        private IList<PrototypeRecord> shown = new PrototypeRecord[0];
        private readonly List<string> groups = new List<string>();
        private readonly HashSet<string> openGroups = new HashSet<string>(StringComparer.Ordinal);
        /// <summary>The group states a search auto-expanded over, put back when the box is cleared.
        /// Null while no search is filtering anything.</summary>
        private HashSet<string> beforeSearch;
        private string openRecord;                     // the record whose variants are listed
        private Vector2 browserScroll;
        private IList<PrototypeTarget> slots = new PrototypeTarget[0];
        private PrototypeVariant standing;
        private bool protoBusy;

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
                //
                // The root ALONE is not the signal, though. The bench hands us the same Transform every
                // time (FitBench.Posed: bay.CharacterBuilder.transform is the builder, and the rig is
                // rebuilt UNDERNEATH it), so a unit swap left this early-out taken and the Doctor holding
                // a Target, a report and a live preview mesh for a body part the swap had destroyed.
                // Measured in game: after 'ct_bench unit', HasPreview stayed true and OurMeshCount 1.
                // The rebuild is not observable from the transform, but the death of the chosen renderer
                // is - and that is the thing that actually invalidates everything downstream.
                bool rebuilt = Renderer == null && !ReferenceEquals(Renderer, null);
                if (ReferenceEquals(root, value) && !rebuilt) return;
                Revert();                                  // the preview belongs to the OLD renderer
                root = value;
                Renderer = null;
                Target = null;
                Prototype = null;
                Ready = null;
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
            if (Event.current.type == EventType.Layout) Refresh();

            // Open is tested BEFORE Draw and the frame ends there: the browser reorders its own recents
            // during the mouse pass, so a second Draw in the same frame lays out a different list.
            if (browser.Open)
            {
                string picked = browser.Draw(260f);
                if (picked != null) { string p = picked; edits.Enqueue(delegate { PickFile(p); }); }
                return;
            }
            // The prototype browser TAKES THE WHOLE CONTENT AREA rather than expanding inline: it is a
            // 36-prototype tree with its own scroll, and half a report behind it is a report nobody can
            // read anyway. browserOpen only ever moves in an edit, so the area cannot change mid-frame.
            if (browserOpen) { Browse(); return; }

            float col = Mathf.Max(120f, (width - 80f) * 0.45f);

            Header();
            if (Ready != null && Ready.Baked != null && Ready.Baked.Mesh != null)
                GUILayout.Label("   " + Ready.Baked.Mesh.VertexCount + " verts, " +
                                Ready.Baked.Mesh.IndexCount / 3 + " tris, " +
                                (Ready.Model == null ? 0 : Ready.Model.JointNames.Count) + " joints, " +
                                Ready.Baked.Influences + " influence(s)/vertex");

            // ABOVE the early returns. What the last press did is most worth reading exactly when the
            // panel has nothing else to show - a refused preview restarts the report, and a message
            // drawn under a verdict that is not there yet is a message nobody ever sees.
            if (Message.Length > 0) GUILayout.Label(Message);

            if (Path == null || !Verifiable) { GUILayout.Label(Hint()); return; }
            if (Ready == null) { GUILayout.Label(Busy ? "reading..." : "queued..."); return; }

            GUILayout.Space(4f);
            GUILayout.Label(Ready.Report.Header());
            GUILayout.Space(2f);

            // Shown whenever there is a map OR a reason to make one. Keying it on NearestBone alone hid
            // the table the moment an alias worked, which is exactly when the author wants to look at
            // what they mapped - and left no way to change or remove it.
            // Replace only: the map's rows are built from MissingBone/ExtraBone, and Extend has no
            // MissingBone by design - there would be nothing on the right-hand side to offer.
            if (Ready.Model != null && Target != null && Target.BoneNames != null &&
                (aliases.Count > 0 || Ready.Outcome == Outcome.NearestBone ||
                 (Ready.Source != null && Ready.Source.AliasesApplied > 0)))
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
            // A target with bind poses but no bone list previews as NotRigged whatever the report said
            // (LiveMesh.Bind takes the empty-bones arm), so every press would be a mismatch. The button
            // says so rather than inviting one.
            // Extend has no renderer to put a mesh on at all - the same "nothing to bind onto" the
            // bone-less target already says, and the same refusal to invent one.
            bool blind = Target == null || Target.BoneNames == null;
            GUI.enabled = (Ready.Outcome == Outcome.ByName || Ready.Outcome == Outcome.NearestBone) &&
                          Ready.Report.Count(Severity.Blocking) == 0 && !blind;
            if (GUILayout.Button(blind ? "Preview - no live bones to bind onto" : "Preview",
                                 GUILayout.Width(blind ? 230f : 80f))) Enqueue("preview");
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

        /// <summary>
        /// ONE LINE: source, prototype, mode, role, slot - section 6 of the picker design. What the
        /// verdict below is ABOUT, said in the order it was decided, so the author never has to open
        /// the browser to find out what they are looking at.
        /// </summary>
        private void Header()
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label("source " + BenchList.Elide(Path == null ? "-" : System.IO.Path.GetFileName(Path), 22),
                            GUILayout.Width(180f));
            if (GUILayout.Button("Browse...", GUILayout.Width(80f)))
                browser.Show(Path == null ? "" : System.IO.Path.GetDirectoryName(Path));
            GUILayout.Label("|", GUILayout.Width(8f));
            GUILayout.Label("prototype " + BenchList.Elide(Prototype == null || Prototype.Record == null
                                                           ? "-" : Prototype.Record.DisplayName, 18),
                            GUILayout.Width(160f));
            if (GUILayout.Button("Change", GUILayout.Width(70f))) edits.Enqueue(delegate { browserOpen = true; });
            GUILayout.Label("|", GUILayout.Width(8f));
            // The mode is a TOGGLE and not a label: Replace and Extend answer two different questions
            // about the same slot, and swapping between them is the comparison the author came for.
            GUI.enabled = Prototype != null;
            if (GUILayout.Button(Prototype == null ? "Replace/Extend" : Prototype.Mode.ToString(),
                                 GUILayout.Width(100f)))
            {
                PrototypeTarget pick = Prototype;
                edits.Enqueue(delegate { Flip(pick); });
            }
            GUI.enabled = true;
            GUILayout.Label("|", GUILayout.Width(8f));
            GUILayout.Label(BenchList.Elide(Prototype == null || Prototype.Variant == null
                                            ? "-" : Prototype.Variant.Name, 18), GUILayout.Width(120f));
            GUILayout.Label("|", GUILayout.Width(8f));
            GUILayout.Label(BenchList.Elide(Prototype == null ? "-" : Prototype.SlotDefName ?? "-", 22));
            if (Ready != null && Ready.Source != null && Ready.Source.AliasesApplied > 0)
                GUILayout.Label("ALIASES (" + Ready.Source.AliasesApplied + ")");
            GUILayout.EndHorizontal();
        }

        /// <summary>Swap the mode of the picked slot and ask again. Refused for a slot that produced no
        /// renderer: Replace has nothing to be exact ABOUT there, and inventing a bone list from the
        /// full rig is the one thing the whole picker exists to stop.</summary>
        private void Flip(PrototypeTarget pick)
        {
            if (pick == null || !ReferenceEquals(pick, Prototype)) return;
            VerifyMode next = pick.Mode == VerifyMode.Replace ? VerifyMode.Extend : VerifyMode.Replace;
            if (next == VerifyMode.Replace && pick.Unavailable != null)
            {
                Message = pick.Unavailable + " - Replace has no live renderer to verify against";
                return;
            }
            pick.Mode = next;
            PickTarget(pick);
        }

        /// <summary>What to say when there is no verdict to draw yet, in the author's own terms.</summary>
        private string Hint()
        {
            // Both halves are needed for a verdict and neither implies the other, so the hint names
            // exactly the one that is missing.
            if (Path == null && !Verifiable)
                return "pick a .glb and a prototype slot to see what the bake would do with them";
            return Path == null ? "pick a .glb to see what the bake would do with it"
                                : "pick a prototype slot to hold this file against - press Change";
        }

        // ------------------------------------------------------------------ the prototype browser

        /// <summary>
        /// EVERYTHING THE BROWSER LAYS OUT, decided once per frame on the Layout pass. IMGUI runs OnGUI
        /// again for the Repaint with the layout it cached here, so a list that is recomputed - or a
        /// rebuild that finishes - between the two passes is "you are pushing more GUIElements now"
        /// every frame afterwards. Reading it ONCE is also why the search filter runs on a keystroke
        /// and not on a frame.
        /// </summary>
        private void Refresh()
        {
            slots = FitBench.SlotTargets();
            standing = FitBench.ShownVariant();
            protoBusy = FitBench.PrototypeBusy;
            // The bay was rebuilt as somebody else, so a pick from the previous variant is about a slot
            // that no longer exists. Enqueued rather than done here: PickTarget reverts a preview, and
            // mutating a renderer inside OnGUI is what Tick exists to avoid.
            if (Prototype != null && !ReferenceEquals(Prototype.Variant, standing))
                edits.Enqueue(delegate { PickTarget((PrototypeTarget)null); });

            if (!browserOpen) return;
            all = FitBench.Prototypes();               // harvested here and nowhere else: FIRST open only
            if (queried == query && ReferenceEquals(seen, all)) return;

            bool was = queried != null && queried.Length > 0, now = query.Length > 0;
            queried = query;
            seen = all;
            shown = PrototypeCatalog.Search(all, query);

            // A search AUTO-EXPANDS what it matched, and clearing it puts back exactly the groups the
            // author had open before they started typing.
            if (now)
            {
                if (!was) beforeSearch = new HashSet<string>(openGroups, StringComparer.Ordinal);
                openGroups.Clear();
                foreach (PrototypeRecord r in shown) openGroups.Add(r.Category);
            }
            else if (was)
            {
                openGroups.Clear();
                if (beforeSearch != null) foreach (string c in beforeSearch) openGroups.Add(c);
                beforeSearch = null;
            }

            groups.Clear();
            foreach (PrototypeRecord r in shown) if (!groups.Contains(r.Category)) groups.Add(r.Category);
            groups.Sort(StringComparer.Ordinal);
        }

        /// <summary>Category -&gt; prototype -&gt; variant -&gt; slot, over the whole content area. Every
        /// press enqueues; nothing that decides a control's existence is written here.</summary>
        private void Browse()
        {
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("< Back", GUILayout.Width(70f))) edits.Enqueue(delegate { browserOpen = false; });
            GUILayout.Label("search", GUILayout.Width(46f));
            query = GUILayout.TextField(query ?? "", GUILayout.Width(220f));
            GUILayout.Label(shown.Count + " of " + all.Count + " prototype(s)" +
                            (protoBusy ? "   rebuilding..." : ""));
            GUILayout.EndHorizontal();
            if (Message.Length > 0) GUILayout.Label(Message);

            browserScroll = GUILayout.BeginScrollView(browserScroll);
            if (all.Count == 0)
                GUILayout.Label("   no prototypes were found - the catalogue is read off DefRepository " +
                                "when the bench opens in a geoscape campaign");
            else if (shown.Count == 0) GUILayout.Label("   nothing matches '" + query + "'");
            foreach (string category in groups)
            {
                bool open = openGroups.Contains(category);
                if (GUILayout.Button((open ? "v " : "> ") + category + "   (" + CountIn(category) + ")"))
                {
                    string c = category;
                    edits.Enqueue(delegate { if (!openGroups.Remove(c)) openGroups.Add(c); });
                }
                if (!open) continue;
                foreach (PrototypeRecord r in shown) if (r.Category == category) Record(r);
            }
            GUILayout.EndScrollView();                 // closed on EVERY path out of this method
        }

        private int CountIn(string category)
        {
            int n = 0;
            foreach (PrototypeRecord r in shown) if (r.Category == category) n++;
            return n;
        }

        private void Record(PrototypeRecord r)
        {
            bool open = r.Id == openRecord;
            if (GUILayout.Button("   " + (open ? "v " : "> ") + BenchList.Elide(r.DisplayName, 26) +
                                 "   " + r.BindableBones.Count + " bindable bone(s), " +
                                 r.Variants.Count + " variant(s)" +
                                 (r.Warning == null ? "" : "   [duplicate bone names]")))
            {
                string id = open ? null : r.Id;
                edits.Enqueue(delegate { openRecord = id; });
            }
            if (!open) return;
            foreach (PrototypeVariant v in r.Variants) Variant(r, v);
        }

        private void Variant(PrototypeRecord r, PrototypeVariant v)
        {
            bool here = ReferenceEquals(v, standing);
            GUILayout.BeginHorizontal();
            // Refused while a rebuild is in flight: two overlapping rebuilds leave the bay showing a
            // mix of two prototypes and neither slot list is worth reading.
            GUI.enabled = !protoBusy;
            if (GUILayout.Button((here ? "      * " : "        ") + BenchList.Elide(v.Name, 24) +
                                 "   " + v.Slots.Count + " slot(s)", GUILayout.Width(340f)))
            {
                PrototypeRecord rec = r; PrototypeVariant var = v;
                edits.Enqueue(delegate
                {
                    string failed = FitBench.ShowPrototype(rec, var);
                    Message = failed ?? ("standing " + var.Name + " on the platform...");
                });
            }
            GUI.enabled = true;
            GUILayout.Label(here ? (protoBusy ? "rebuilding..." : "on the platform") : "");
            GUILayout.EndHorizontal();
            if (!here) return;
            foreach (PrototypeTarget t in slots) Slot(t);
        }

        /// <summary>One slot row, and the two questions that can be asked of it. A slot the rebuild
        /// produced no renderer for offers only Extend - and says why - rather than a Replace verdict
        /// against a bone list nothing measured.</summary>
        private void Slot(PrototypeTarget t)
        {
            bool chosen = ReferenceEquals(t, Prototype);
            GUILayout.BeginHorizontal();
            GUILayout.Label((chosen ? "           > " : "             ") +
                            BenchList.Elide(t.SlotDefName ?? "(slot)", 26), GUILayout.Width(260f));
            GUI.enabled = t.Unavailable == null;
            if (GUILayout.Button(chosen && t.Mode == VerifyMode.Replace ? "[Replace]" : "Replace",
                                 GUILayout.Width(90f))) Choose(t, VerifyMode.Replace);
            GUI.enabled = true;
            if (GUILayout.Button(chosen && t.Mode == VerifyMode.Extend ? "[Extend]" : "Extend",
                                 GUILayout.Width(90f))) Choose(t, VerifyMode.Extend);
            GUILayout.Label(t.Unavailable ?? ((t.Live == null || t.Live.BoneNames == null
                                               ? 0 : t.Live.BoneNames.Length) + " live bone(s)"));
            GUILayout.EndHorizontal();
        }

        private void Choose(PrototypeTarget t, VerifyMode mode)
        {
            PrototypeTarget pick = t;
            VerifyMode m = mode;
            edits.Enqueue(delegate
            {
                pick.Mode = m;
                PickTarget(pick);
                browserOpen = false;                   // the pick was the point; the report is behind it
            });
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

            // THE ROWS ARE THE UNION: what is mapped now, plus what is still unmapped. Deriving them
            // from ExtraBone alone made a row disappear the moment its alias worked - the author could
            // see a mapping only while it was wrong, and could never take one back.
            var rows = new List<string>();
            foreach (KeyValuePair<string, string> e in aliases) rows.Add(e.Key);
            foreach (Diagnostic d in Ready.Report.Rows)
                if (d.Code == "ExtraBone" && d.Subject != null && !aliases.ContainsKey(d.Subject))
                    rows.Add(d.Subject);

            foreach (string fileBone in rows)
            {
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
                // The one-press way back out of a mapping, on the row itself: a mapping the author can
                // make but not unmake is a trap, and hiding the undo inside the dropdown is nearly one.
                if (current != null && GUILayout.Button("x", GUILayout.Width(22f)))
                {
                    string clear = fileBone;
                    edits.Enqueue(delegate { SetAlias(clear, null); });
                    boneOpen = null;
                }
                GUILayout.EndHorizontal();
                if (boneOpen != fileBone) continue;

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
            Prototype = null;
            Ready = null;
            browserOpen = false;
            query = "";
            queried = null;
            seen = null;
            all = new PrototypeRecord[0];
            shown = new PrototypeRecord[0];
            slots = new PrototypeTarget[0];
            standing = null;
            groups.Clear();
            openGroups.Clear();
            beforeSearch = null;
            openRecord = null;
            aliases.Clear();
            seeded.Clear();
            seededFor = null;
            canSave = false;
        }
    }
}
