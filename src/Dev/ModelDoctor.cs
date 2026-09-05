using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using Morgott.ContentTool.Doctor;
using Morgott.ContentTool.Import;
using Morgott.ContentTool.IO;
using Morgott.ContentTool.Project;
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
        private enum Intent { Preview, Revert, Save, SkelPlan, Ship }

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

        // ---------------------------------------------------------------- SHIP
        /// <summary>The project name the author is editing. Seeded from the resolved target on the first
        /// Layout pass that has one, then owned by the text field.</summary>
        private string projectName = "";
        /// <summary>The two-frame gate. The bake blocks the main thread for seconds, so the label has to be
        /// PAINTED before it starts: Tick N+1 arms, Draw paints during Repaint, Tick N+2 runs. SlimPanel's
        /// volatile-snapshot pattern does not apply here - no worker changes state between Layout and
        /// Repaint, the main thread simply stops.</summary>
        private bool shipPending, shipLabelPainted;
        private string shipPhase = "", shipResult = "", shipPath = "", shipTail = "";
        /// <summary>Everything the run needs, copied when the intent drains, so a click on the browser while
        /// the bake runs cannot change what is being shipped.</summary>
        private string shipName, shipSource, shipSha, shipBundle, shipAsset;
        private Dictionary<string, string> shipAliases;
        private PrototypeTarget shipProto;
        private RigTarget shipTargetWas;
        private SkinnedMeshRenderer shipRenderer;
        /// <summary>The Doctor generation this press was armed on (<see cref="gen"/>, bumped by Restart, by
        /// the slot change and by Dispose). The two-frame gate spans a frame the AUTHOR can act in -
        /// retarget, pick another file, close the bench - and every one of those moves `gen`, so an armed
        /// press whose generation no longer matches is abandoned before it writes anything.</summary>
        private int shipGen = -1;

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
            // A ROW ARMED FOR THE PREVIOUS FILE names a file joint this one need not carry, and the
            // click that answered it would map a bone nobody asked about. Disarmed here, in Root's
            // setter and on a new overlay generation - the three places the question stops being asked.
            boneOpen = null;
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
            else if (what == "skelplan") intents.Enqueue(Intent.SkelPlan);
            else if (what == "ship") intents.Enqueue(Intent.Ship);
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
                AutoOpenMap();
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
                else if (intent == Intent.SkelPlan) Message = DoWriteSkelPlan();
                else if (intent == Intent.Ship) ArmShip();
                else Message = DoSave();
            }

            // FRAME N+2. The label armed above has been painted by now, so the freeze happens under a panel
            // that already says it is happening.
            if (shipPending && shipLabelPainted)
            {
                shipPending = false;
                shipLabelPainted = false;
                DoShip();
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

        /// <summary>
        /// THE BONE MAP, BAKED INTO THE FILE INSTEAD OF PARKED BESIDE IT. The sidecar and a rename plan
        /// carry the same fact - file bone -> game bone - and the flow between them is one-directional:
        /// aliases become a plan, SKEL writes the names into the .glb, and the sidecar is then
        /// unnecessary for every bone it renamed (it is sha256-guarded, AliasMap.cs:189-195, so the
        /// rewrite makes it Stale anyway). A baked file binds on BOTH import routes; a sidecar only
        /// reaches the replacement read (AliasMap.cs:20-23).
        ///
        /// RENAMES ONLY. This panel knows which bones are misnamed and nothing about hierarchy, so it
        /// writes no collapse, no insert and no create - design §9 forbids the guess.
        /// </summary>
        private string DoWriteSkelPlan()
        {
            if (Path == null || Ready == null) return "there is nothing to write a plan from yet";
            if (aliases.Count == 0) return "the bone map is empty, so there is nothing to rename";
            try
            {
                SkelPlan plan = SkelPlanFromMap.Of(Ready.Report.Rows, aliases, RootOf(Path));
                if (plan.Renames.Count == 0)
                    return "every alias in this map is one the report itself flagged, so a plan built " +
                           "from it would be refused - fix the rows above first";
                string path = SkelPlan.PlanPathOf(Path);
                AtomicFile.WriteText(path, plan.ToJson(), new UTF8Encoding(false));
                return "wrote " + plan.Renames.Count + " rename(s) to " + path +
                       " - open Advanced > SKEL to apply it";
            }
            catch (Exception ex) { return "could not write the plan: " + ex.Message; }
        }

        /// <summary>FRAME N+1: take a copy of every input and put the panel into its "working" state. Nothing
        /// is written here - the point of this frame is that it ends with a repaint.</summary>
        private void ArmShip()
        {
            if (shipPending) return;
            shipName = (projectName ?? "").Trim();
            shipSource = Path;
            shipSha = Ready == null ? null : Ready.Sha256;
            shipBundle = Prototype == null ? null : Prototype.ShippedBundle;
            shipAsset = Prototype == null ? null : Prototype.ShippedAsset;
            shipAliases = new Dictionary<string, string>(aliases, StringComparer.Ordinal);
            shipProto = Prototype;
            shipTargetWas = Target;
            shipRenderer = Renderer;
            shipGen = gen;                      // the generation this press belongs to
            shipResult = ""; shipPath = ""; shipTail = "";
            shipPhase = "creating the project, baking and applying - the game freezes for a few seconds";
            shipPending = true;
            shipLabelPainted = false;
        }

        /// <summary>
        /// FRAME N+2, and every byte of it. Order is design §4.5: the source is re-read and compared against
        /// the verdict's own hash, the project is written, the COPY is re-judged, the renderer is
        /// re-snapshotted, and only then does the bake run. That binds the VERDICT to what is on disk - the
        /// bake's own target lookup, bundle I/O and material-slot mapping can still refuse, and its result
        /// stays authoritative.
        ///
        /// Nothing is rolled back after a failure: the copy, the sidecar and the row are authored project
        /// state, cheap and retryable on the next press, and a three-writer rollback would be the more
        /// dangerous code (design §7).
        /// </summary>
        private void DoShip()
        {
            shipPhase = "";
            // CANCEL BEFORE THE FIRST BYTE. One frame stands between arming and running, and the author owns
            // it: retargeting bumps gen, picking another file bumps it through Restart, closing the bench
            // bumps it in Dispose. Shipping the snapshot anyway would write a project for the OLD slot under
            // a panel already showing the new one - a mod folder the author never asked for, named after a
            // target they have moved away from.
            if (shipGen != gen)
            {
                shipGen = -1;
                shipResult = "the slot or the file changed before the bake started, so nothing was written - " +
                             "press Ship again";
                return;
            }
            shipGen = -1;
            string root = null;
            try
            {
                // R3 belongs to the SCAFFOLD, which raises it BEFORE it creates a directory, a manifest or a
                // meta - which is what lets its sentence say "nothing was written" and be true. Re-reading
                // the source here as well would only hash the same file twice and answer the same question.
                ProjectScaffold.Result made = ProjectScaffold.AddMeshReplacement(
                    ContentToolMain.ModDir, shipName, shipSource, shipSha, shipBundle, shipAsset, shipAliases);
                root = made.Root;
                shipPath = made.Root;

                // R7. The bake reads the COPY, so the COPY is what has to be green - including the sidecar
                // that was just written beside it. made.MeshBytes ARE the copy's bytes: the scaffold hashed
                // them against the verdict before writing, and CopyOrVerify proved an existing file equal to
                // them. Judging those is judging what is on disk, without re-opening the question.
                ReplacementPreflightResult copied =
                    ReplacementPreflight.Run(made.MeshBytes, made.MeshPath, shipProto);
                if (copied.Outcome != Outcome.ByName || copied.Report.Count(Severity.Blocking) != 0)
                {
                    shipResult = "the COPIED glb did not re-read green (" + copied.Outcome + "), so nothing was " +
                                 "baked - the project on disk is complete, fix the file and press Ship again";
                    return;
                }

                // R8, AND IT HAS TO KNOW ABOUT THE PREVIEW. Target was snapshotted when the slot was picked;
                // DoPreview then put OUR mesh on that renderer. A plain SameAs is therefore false for the
                // whole time a preview is on screen - which is exactly the state an author ships from, so a
                // naive guard would refuse every real press. With a preview live the mesh's IDENTITY is not
                // evidence about the rig; that the mesh is OUR preview object is.
                RigTarget now = shipTargetWas == null ? null : Snapshot(shipRenderer, shipTargetWas.TransformPath);
                bool same = now != null && (HasPreview
                    ? ReferenceEquals(shipRenderer == null ? null : shipRenderer.sharedMesh, preview) &&
                      now.SameRigAs(shipTargetWas)
                    : now.SameAs(shipTargetWas));
                if (!same)
                {
                    shipResult = "the slot's renderer changed while Ship was running, so nothing was baked - " +
                                 "pick the slot again";
                    return;
                }

                // ApplyProject and NOT ProjectBake.Run: it loads the project, computes PatchCache.Key,
                // re-bakes when stale and installs, and Run does not write the freshness key - calling both
                // would bake twice. The ABSOLUTE root is idempotent through ContentToolMain.ProjectDir, so
                // the two cannot disagree about which folder was baked. The DISPOSITION is asked for, not
                // read out of the log: zero claims taken can mean residency, a catalog Locate failure or
                // another mod owning that bundle, and only one of the three is S1.
                Bake.Route7.ApplyDisposition how;
                string log = Bake.Route7.ApplyProject(made.Root, shipBundle, out how);
                ContentToolMain.Say(log);
                shipTail = Tail(log, 10);
                if (how == Bake.Route7.ApplyDisposition.BakeFailed)
                    // R11. ApplyProject's own NOT APPLIED line names the count and where to read the
                    // failures; it deliberately states no next step, because the console verb and the mod
                    // manager print it too. THIS caller has one.
                    shipResult = Tail(log, 1) + " Fix the lines above and press Ship again.";
                else if (how == Bake.Route7.ApplyDisposition.Resident)
                    // S1, THE NORMAL OUTCOME. The bay rendered this very mesh, so the bundle is resident and
                    // BundleLive.Register refuses before taking a claim. No forced unload: it would pull the
                    // archive out from under live objects, which is what that refusal exists to prevent.
                    shipResult = "baked OK - restart the game and enable '" + shipName + "' in the mod manager. " +
                                 "Phoenix Point already loaded " + shipBundle + ", so this session keeps showing " +
                                 "your Doctor preview.";
                else if (how == Bake.Route7.ApplyDisposition.Redirected)
                    shipResult = "baked and redirected LIVE - " + shipBundle + " now loads from the patched copy " +
                                 "on the next load";
                else                                    // R23
                    shipResult = "baked, but NOT APPLIED: " + shipBundle + " was neither redirected nor already " +
                                 "loaded - the log above names the refusal; the project folder is complete and " +
                                 "can be enabled after a restart";
            }
            catch (InvalidDataException refused) { shipResult = refused.Message; }   // R1, R2, R5, R6, R13
            catch (IOException refused) { shipResult = refused.Message; }            // R3, R4, E5, E6
            catch (Exception ex)                                                     // R12
            {
                // OBSERVED, never assumed. This catch is reachable BEFORE anything exists - a modDir that
                // resolves nowhere, a source that cannot be read - so it asks the disk rather than sending an
                // author to look at a folder that was never created.
                string where = root ?? ProjectScaffold.RootOf(ContentToolMain.ModDir, shipName);
                bool there = where != null && Directory.Exists(where);
                shipResult = "SHIP THREW: " + ex.GetType().Name + ": " + ex.Message + " - " +
                             (there
                              ? "'" + where + "' is on disk and the files already written there were retained"
                              : "no project folder was created") + "; see Player.log for the stack";
                Debug.LogError("[ContentTool] Model Doctor Ship: " + ex);
            }
        }

        /// <summary>The last few lines of the bake log, for the panel. The WHOLE log went to
        /// ContentToolMain.Say, which is where an author reads the rows one by one.
        ///
        /// THE TRAILING EMPTY ELEMENT IS DISCARDED BEFORE THE COUNT. ApplyProject ends in AppendLine, so
        /// Split('\n') always produces one empty element at the end; taking "the last 1" then selected that
        /// empty string and Tail(log, 1) answered "", which is exactly the R11 path - the panel would report
        /// a failed bake with a BLANK result line. Trim the tail first, then take N.</summary>
        private static string Tail(string log, int lines)
        {
            if (string.IsNullOrEmpty(log)) return "";
            string[] all = log.Replace("\r\n", "\n").Split('\n');
            int end = all.Length;
            while (end > 0 && all[end - 1].Length == 0) end--;      // the AppendLine's own empty tail
            var kept = new StringBuilder();
            for (int i = Math.Max(0, end - lines); i < end; i++)
                if (all[i].Length != 0) kept.AppendLine(all[i]);
            return kept.ToString().TrimEnd();
        }

        /// <summary>The file's ONE scene root, or null when it has none or several. A plan's Root is
        /// what PP's paths are measured from and Validate refuses one that names no node; u9_probe.glb
        /// ships THREE roots, so "assume there is one" is not a hypothetical mistake.</summary>
        private static string RootOf(string glbPath)
        {
            try
            {
                List<object> nodes = GlbSkel.Nodes(GlbDocument.Load(glbPath));
                string why;
                int[] parents = GlbSkel.Parents(nodes, out why);
                if (parents == null) return null;
                string only = null;
                for (int i = 0; i < parents.Length; i++)
                {
                    if (parents[i] >= 0) continue;
                    if (only != null) return null;
                    only = GlbSlim.Str(GlbSlim.Obj(nodes[i]), "name");
                }
                return only;
            }
            catch (Exception) { return null; }
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
                picked = null;                             // ... and so is the bone the inspector named
                boneOpen = null;                           // ... and the row waiting for one of them
                gen++;                                     // an answer in flight is about the old actor
            }
        }

        // ------------------------------------------------------------------ the skeleton overlay

        /// <summary>Section 6's [Skeleton] toggle. Session-only, like every other view preference. The
        /// toggle CONTROL is drawn by the transport's own row; this is the state it reads and writes.</summary>
        private bool skeleton = true;
        internal bool Skeleton { get { return skeleton; } set { skeleton = value; } }

        // ---- ONE pass's projection, kept BETWEEN frames. A rig of 124 bones would otherwise allocate
        // six arrays, a dictionary and a legend string every Repaint, and Renderer.bones allocates on
        // every read of it. Rebuilt only when the generation or the report moves - which is every path
        // that can change what the bones ARE or what they mean (Root's setter, PickTarget, and
        // SetAlias -> Restart, all of which bump gen).
        private Transform[] joints;
        private int[] jointParent;                     // each joint's parent AS AN INDEX in joints, or -1
        private BoneStatus[] jointStatus;
        private float[] jointX, jointY;                // this pass's projection, in the camera's convention
        private bool[] jointVisible;
        private string legend = "";
        private int jointsGen = -1;
        private ReplacementPreflightResult jointsFor;

        /// <summary>One colour per <see cref="BoneStatus"/>, in the enum's own order. The same five the
        /// drag handles use (FitGizmo's AxisColour / Hot / Dim), so the bench has ONE palette.</summary>
        private static readonly Color[] Colours =
        {
            new Color(0.92f, 0.25f, 0.25f),            // Unmatched  - red
            new Color(0.35f, 0.9f, 0.35f),             // ByName     - green
            new Color(1f, 0.92f, 0.3f),                // Alias      - yellow
            new Color(0.35f, 0.55f, 1f),               // Nearest    - blue
            new Color(0.45f, 0.45f, 0.45f, 0.5f),      // Attachment - dim grey, the game's own skip
        };

        private static readonly string[] StatusNames =
            { "unmatched", "by name", "alias", "nearest bone", "attachment" };

        private const float DotPixels = 3f;

        /// <summary>The bone the inspector is showing, BY NAME - a rebuild replaces every Transform on
        /// the rig, and a name survives that where a reference does not. Null is "nothing picked".</summary>
        private string picked;
        private bool inspectorOpen;
        /// <summary>What the inspector is drawing THIS FRAME, latched on the Layout pass. The pick and
        /// the foldout both decide how many controls exist, and IMGUI replays the Repaint against the
        /// layout the Layout pass recorded - the same rule <see cref="Refresh"/> exists for.</summary>
        private string shownBone;
        private bool shownOpen;
        private int pickedAt = -1;
        private readonly List<string> inspectorLines = new List<string>();

        /// <summary>The armed bone-map row AS THE DRAW SAW IT, latched on the Layout pass beside the
        /// inspector's own state. <see cref="boneOpen"/> itself moves on the MouseUp and KeyDown passes -
        /// drawing rings off the live field would ring a different set of joints than the pass that
        /// laid the row out, and the press below would then mean something the picture never said.</summary>
        private string shownArmed;
        /// <summary>Which joints the armed row may land on this frame. Sized by <see cref="Recache"/>,
        /// filled once a frame by <see cref="Arm"/>, and read by both the picture and the press - one
        /// answer, so they cannot disagree.</summary>
        private bool[] jointEligible;

        /// <summary>The overlay's own control id, hashed once. See <see cref="Overlay"/>.</summary>
        private static readonly int PickHint = "Morgott.ContentTool.BonePick".GetHashCode();

        private const float InspectorWidth = 380f;
        /// <summary>The inspector's own label style: NO word wrap, so a row is exactly the 18 px the
        /// box was measured with. Built once, on the first draw - GUI.skin is null outside OnGUI.</summary>
        private static GUIStyle oneLine;
        private static readonly Color PickedRing = new Color(1f, 1f, 1f, 0.85f);
        /// <summary>The halo behind a joint the armed row may take - alias yellow, because that is the
        /// colour it will BECOME.</summary>
        private static readonly Color ArmedRing = new Color(1f, 0.92f, 0.3f, 0.55f);
        private const float DimAlpha = 0.15f;

        /// <summary>
        /// The skeleton over the viewport: one line per parent-child pair, one dot per joint, coloured
        /// by <see cref="BoneOverlay.Classify"/>. Drawn from OnGUI in the REPAINT pass, in PIXEL space
        /// with FitGizmo's own material, so the picture is projected by exactly the arithmetic a pick
        /// will use - the classic gizmo bug is a hit test that disagrees with what is drawn, and it is
        /// invisible until somebody clicks.
        ///
        /// The BONES ARE THE TARGET'S, not the rig's - see <see cref="Joints"/>.
        /// </summary>
        /// <param name="stripTopGui">The transport strip's top edge in IMGUI coordinates. The strip and
        /// the panel own their pixels, exactly as FitGizmo.Gui documents, so nothing is drawn over
        /// either of them.</param>
        internal void Overlay(Camera cam, float panelWidth, float stripTopGui)
        {
            Event e = Event.current;
            // FIRST and unconditionally, exactly as FitGizmo.Gui does it: a control id comes off this
            // pass's own counter, so an id fetched only sometimes is a different id every frame - and
            // every id after it moves too. It is claimed for the overlay and never latched as
            // hotControl: see Press for why a click has nothing to latch.
            GUIUtility.GetControlID(PickHint, FocusType.Passive);
            if (e == null) return;
            try
            {
                if (joints == null || jointsGen != gen || !ReferenceEquals(jointsFor, Ready))
                {
                    // A NEW GENERATION is a new rig - a unit swap, a prototype rebuild, a re-run
                    // preflight - so the name the inspector was showing is about bones that are gone.
                    // ... and so is the row armed against them: the bone map is rebuilt from the new
                    // report, and answering the old question would alias a file joint that is gone.
                    if (jointsGen != gen) { picked = null; boneOpen = null; }
                    Recache();
                    jointsGen = gen; jointsFor = Ready;
                }
                if (e.type == EventType.Layout)
                { shownBone = picked; shownOpen = inspectorOpen; shownArmed = boneOpen; Lines(); Arm(); }
                // ALWAYS drawn, on EVERY pass and whatever the skeleton toggle says: its foldout header
                // is a control, and a control that exists only on some passes is the layout imbalance
                // every other comment in this file is about.
                Inspector(panelWidth, stripTopGui);

                // THE WAY OUT, handled before the skeleton toggle can return: a row is armed from the
                // panel, so it must be cancellable whether or not the overlay is being drawn. An armed
                // state with no way out is the trap the bone map's own 'x' button exists to avoid.
                if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape && boneOpen != null)
                {
                    string armed = boneOpen;
                    edits.Enqueue(delegate { boneOpen = null; Message = "'" + armed + "' left unmapped"; });
                    e.Use();
                    return;
                }

                if (!skeleton || cam == null) return;
                bool press = e.type == EventType.MouseDown && e.button == 0;
                if (e.type != EventType.Repaint && !press) return;
                if (cam.pixelWidth - panelWidth < 1f || cam.pixelHeight < 1f) return;   // no room to draw in
                if (joints.Length == 0) return;
                // THE SAME arithmetic for the picture and for the press, run in both passes rather than
                // remembered from one: a hit test projected differently from what is drawn is the classic
                // gizmo bug, and it is invisible until somebody clicks.
                Project(cam, panelWidth, stripTopGui);
                if (press) Press(e, cam, panelWidth, stripTopGui);
                else Paint(cam, panelWidth, stripTopGui);
            }
            catch (Exception)
            {
                // SWALLOWED on purpose: this runs from OnGUI, and an exception there closes the whole
                // bench (FitBench's own catch). A skeleton nobody can see is better than that.
            }
        }

        /// <summary>This pass's screen position per joint, into the arrays <see cref="Recache"/> sized.
        /// Allocation-free on purpose - it runs every Repaint and again on every press.</summary>
        private void Project(Camera cam, float panelWidth, float stripTopGui)
        {
            // The two half-planes the overlay may draw in. Both are CONVEX, which is what makes the
            // "both ends visible" rule enough on its own: a segment between two points that are each
            // right of the panel and above the strip cannot cross into either.
            float strip = stripTopGui >= cam.pixelHeight ? 0f : cam.pixelHeight - stripTopGui;
            for (int i = 0; i < joints.Length; i++)
            {
                jointVisible[i] = false;
                if (joints[i] == null) continue;
                Vector3 p = cam.WorldToScreenPoint(joints[i].position);
                jointX[i] = p.x; jointY[i] = p.y;
                // A joint BEHIND the camera projects to a mirrored point somewhere plausible - the
                // same trap FitGizmo.AxisVisible guards - so it is neither drawn nor pickable.
                jointVisible[i] = p.z > cam.nearClipPlane &&
                                  p.x >= panelWidth && p.x <= cam.pixelWidth &&
                                  p.y >= strip && p.y <= cam.pixelHeight;
            }
        }

        /// <summary>The lines, the dots, the picked joint's marker and the legend. Repaint only.</summary>
        private void Paint(Camera cam, float panelWidth, float stripTopGui)
        {
            Material m = FitGizmo.Colored();
            if (m == null) return;                     // no shader in this build; the gizmo already said so

            m.SetPass(0);
            GL.PushMatrix();
            try
            {
                // PIXEL SPACE, stated rather than left to the default: the projection above is the
                // camera's own screen convention (origin BOTTOM-left) and these four arguments - left,
                // right, bottom, top - are the matrix that agrees with it.
                GL.LoadPixelMatrix(0f, cam.pixelWidth, 0f, cam.pixelHeight);

                GL.Begin(GL.LINES);
                for (int i = 0; i < joints.Length; i++)
                {
                    int p = jointParent[i];
                    if (p < 0 || !jointVisible[i] || !jointVisible[p]) continue;
                    GL.Color(Colours[(int)jointStatus[i]]);
                    GL.Vertex3(jointX[i], jointY[i], 0f);
                    GL.Vertex3(jointX[p], jointY[p], 0f);
                }
                GL.End();

                GL.Begin(GL.QUADS);
                // The PICKED joint gets a bigger square UNDER its own dot: an inspector that names a bone
                // the author cannot find again on the model is half an answer.
                if (pickedAt >= 0 && pickedAt < joints.Length && jointVisible[pickedAt])
                    Dot(jointX[pickedAt], jointY[pickedAt], DotPixels + 3f, PickedRing);
                bool arming = shownArmed != null && jointEligible != null;
                for (int i = 0; i < joints.Length; i++)
                {
                    if (!jointVisible[i]) continue;
                    Color c = Colours[(int)jointStatus[i]];
                    // ARMED: what the next click can land on is RINGED and what it cannot is faded, so
                    // an author can see that this click means something the last one did not.
                    if (arming && jointEligible[i]) Dot(jointX[i], jointY[i], DotPixels + 3f, ArmedRing);
                    else if (arming) c = new Color(c.r, c.g, c.b, DimAlpha);
                    Dot(jointX[i], jointY[i], DotPixels, c);
                }
                GL.End();
            }
            finally
            {
                // PAIRED WITH THE PUSH whatever happens above it: a GL matrix stack left one deep leaks
                // into every camera that renders after this one, and the panel that caused it is the one
                // place the fault does not show.
                GL.PopMatrix();
            }

            // ABOVE the strip, not inside it: the strip's pixels belong to the transport, and a
            // legend written over its controls is not a legend, it is a collision.
            // While a row is armed the legend says what the next CLICK does instead of what the colours
            // mean: the colours are on screen either way, and the armed state is not.
            string line = shownArmed == null ? legend
                        : "aliasing '" + BenchList.Elide(shownArmed, 30) +
                          "' - click a ringed bone, Esc to cancel";
            if (line.Length > 0)
                GUI.Label(new Rect(panelWidth + 8f,
                                   Mathf.Min(stripTopGui, cam.pixelHeight) - 20f,
                                   cam.pixelWidth - panelWidth - 16f, 18f), line);
        }

        private static void Dot(float x, float y, float r, Color c)
        {
            GL.Color(c);
            GL.Vertex3(x - r, y - r, 0f);
            GL.Vertex3(x - r, y + r, 0f);
            GL.Vertex3(x + r, y + r, 0f);
            GL.Vertex3(x + r, y - r, 0f);
        }

        /// <summary>
        /// THE PRESS, in the precedence the bench already documents (FitGizmo.Gui): the panel, then the
        /// transport strip, then FitGizmo's handles, then this overlay, then the orbit. A MISS consumes
        /// nothing at all - a press that landed on no joint still belongs to whoever wants it next.
        ///
        /// No <c>hotControl</c> is taken. This is a CLICK: there is no gesture to latch and nothing to
        /// hand back on a MouseUp, and a bare left press is <c>ViewGesture.None</c> anyway
        /// (OrbitCamera.Classify), so the orbit was never going to act on it. ALT+LEFT, which the orbit
        /// DOES claim, is refused below rather than fought over.
        /// </summary>
        private void Press(Event e, Camera cam, float panelWidth, float stripTopGui)
        {
            if (e.mousePosition.x <= panelWidth) return;      // the panel wins, always
            if (e.mousePosition.y >= stripTopGui) return;     // ... and so does the transport
            if (e.alt) return;                                // ALT+LEFT is the orbit's gesture
            // IMGUI measures y from the TOP; the camera, FitGizmo's picks and this pass's projection all
            // measure it from the BOTTOM. One conversion, here, for both questions asked below it.
            float x = e.mousePosition.x, y = cam.pixelHeight - e.mousePosition.y;
            if (FitGizmo.WouldGrab(x, y)) return;             // the handles get first refusal
            int hit;
            if (!BoneOverlay.Nearest(x, y, jointX, jointY, jointVisible,
                                     BoneOverlay.PickRadiusPixels, out hit)) return;
            Transform t = joints[hit];
            if (t == null) return;
            string name = t.name;
            // ENQUEUED and not assigned: this is the draw pass, and what it picks decides how many rows
            // the NEXT layout pass lays out - the same rule every button in this panel follows.
            if (shownArmed == null) edits.Enqueue(delegate { picked = name; inspectorOpen = true; });
            else Assign(shownArmed, name, hit);
            e.Use();
        }

        /// <summary>
        /// THE ARMED CLICK, answering the question the bone map's open row is already asking. It goes
        /// through the SAME <see cref="SetAlias"/> the dropdown calls - so the sidecar format, the
        /// bijection rule and the re-run preflight all come for free, and there is no second way to
        /// write this map.
        ///
        /// A refusal is SAID and the row is DISARMED either way: an armed state that survives a click
        /// the author thought did something is worse than no arming at all.
        /// </summary>
        private void Assign(string armed, string bone, int hit)
        {
            // THE SAME rule the rings were drawn from, over the same inputs: Arm ran on this frame's own
            // Layout pass and nothing between it and this press can move an alias, so asking again here
            // cannot answer differently - and it is the only way to get the REASON for a refusal.
            AliasRefusal why = BoneOverlay.CanAlias(bone, jointStatus[hit], aliases, armed);
            string k = armed, v = bone;
            edits.Enqueue(delegate
            {
                boneOpen = null;
                if (why == AliasRefusal.Ok) { SetAlias(k, v); Message = "'" + k + "' -> '" + v + "'"; return; }
                Message = "'" + v + "' cannot take '" + k + "': " + Refused(why);
            });
        }

        /// <summary>Why the click was refused, in the author's own terms - a refusal that only says 'no'
        /// leaves them clicking the same bone again.</summary>
        private static string Refused(AliasRefusal why)
        {
            if (why == AliasRefusal.Attachment)
                return "it is an EXT_ attachment point, which the game skips on every rig";
            if (why == AliasRefusal.BoundByName)
                return "a joint in your file already binds to it by name";
            return "another bone of your file is already mapped to it";
        }

        /// <summary>
        /// The inspector's text, rebuilt ONCE a frame on the Layout pass so the Repaint replays exactly
        /// the rows the layout recorded. The "current" row is live - a playing clip moves it every frame -
        /// so it cannot be cached against the selection instead.
        /// </summary>
        private void Lines()
        {
            pickedAt = -1;
            if (shownBone != null && joints != null)
                for (int i = 0; i < joints.Length; i++)
                    if (joints[i] != null && joints[i].name == shownBone) { pickedAt = i; break; }

            inspectorLines.Clear();
            if (!shownOpen) return;
            if (shownBone == null)
            { inspectorLines.Add("click a joint on the model to read it"); return; }
            if (pickedAt < 0)
            { inspectorLines.Add("'" + shownBone + "' is no longer on the rig"); return; }

            Transform t = joints[pickedAt];
            inspectorLines.Add("name     " + t.name);
            // The LIVE path, walked to Root, rather than the census's PrototypeBone.Path copy of it: the
            // path is the only thing that tells two same-named transforms apart, and a rebuild can move
            // one after the census was taken.
            inspectorLines.Add("path     " + BenchList.Elide(PathOf(t), 54));
            inspectorLines.Add("parent   " + (t.parent == null ? "-" : t.parent.name));
            inspectorLines.Add("status   " + StatusNames[(int)jointStatus[pickedAt]]);
            inspectorLines.Add("binds    " + BenchList.Elide(FileJointFor(t.name), 54));
            inspectorLines.Add("rest     " + (RestOf(pickedAt) ?? "-  (this target carries no bind pose)"));
            inspectorLines.Add("current  " + Trs(t.localPosition, t.localRotation.eulerAngles, t.localScale));
        }

        /// <summary>
        /// WHICH JOINTS THE ARMED ROW MAY TAKE, once a frame on the Layout pass, from the status each
        /// joint was already coloured by (<see cref="BoneOverlay.CanAlias"/>). Not recomputed in the
        /// press: the picture an author clicked at is the contract, and asking twice is two answers.
        /// </summary>
        private void Arm()
        {
            if (jointEligible == null || jointStatus == null) return;
            for (int i = 0; i < jointEligible.Length; i++)
                jointEligible[i] = shownArmed != null && joints[i] != null &&
                                   BoneOverlay.CanAlias(joints[i].name, jointStatus[i], aliases, shownArmed)
                                       == AliasRefusal.Ok;
        }

        /// <summary>
        /// Section 6's <c>Selected bone inspector</c> foldout, in the RIGHT column above the transport
        /// strip. Its own IMGUI area for the reason FitAnim.List documents: the strip has 80 usable
        /// pixels and IMGUI does not clip a BeginArea, so anything past that is drawn where nothing can
        /// reach it.
        ///
        /// READ-ONLY, every row. Section 1 refuses bone dragging, rest-pose editing and retargeting, and
        /// an editable field here is the first step to all three.
        /// </summary>
        private void Inspector(float panelWidth, float stripTopGui)
        {
            float w = Screen.width, h = Screen.height;
            float wide = Mathf.Min(InspectorWidth, w - panelWidth - 16f);
            if (wide < 160f) return;
            // 20 px a row, not 18: IMGUI puts the style's own vertical margin BETWEEN stacked controls,
            // and measuring the box at the bare line height left the last row outside it.
            float high = 26f + (shownOpen ? inspectorLines.Count * 20f + 8f : 0f);
            float top = Mathf.Min(stripTopGui, h) - high - 4f;
            if (top < 4f) return;

            GUI.Box(new Rect(w - 8f - wide, top, wide, high), GUIContent.none);
            GUILayout.BeginArea(new Rect(w - 2f - wide, top + 3f, wide - 12f, high - 6f));
            try
            {
                if (GUILayout.Button((shownOpen ? "v " : "> ") + "Selected bone inspector",
                                     GUILayout.Height(18f)))
                    inspectorOpen = !inspectorOpen;
                // COLLAPSED IS THE DEFAULT and the whole body hangs off the LATCHED flag, never off the
                // live one: the button above flips it mid-frame, and reading it here would lay out a
                // different number of labels than the Layout pass counted.
                if (!shownOpen) return;
                // ONE LINE PER ROW, or the box is the wrong height. The built-in label style word-wraps,
                // and a single wrapped 'path' pushed 'binds', 'rest' and 'current' clean out of an area
                // measured at 18 px a row - three of the seven rows were invisible on Instance3.
                if (oneLine == null) oneLine = new GUIStyle(GUI.skin.label) { wordWrap = false };
                for (int i = 0; i < inspectorLines.Count; i++)
                    GUILayout.Label(inspectorLines[i], oneLine, GUILayout.Height(18f));
            }
            finally { GUILayout.EndArea(); }
        }

        /// <summary>The transform's path from <see cref="Root"/>, '/'-joined - the same shape
        /// <c>SeamSwap.RelativePath</c> and <c>PrototypeBone.Path</c> use.</summary>
        private string PathOf(Transform t)
        {
            if (t == null) return "-";
            string p = t.name;
            for (Transform up = t.parent; up != null && !ReferenceEquals(up, root); up = up.parent)
                p = up.name + "/" + p;
            return p;
        }

        /// <summary>Which file joint lands on this target bone - the author's alias first, then a
        /// by-name match under <see cref="SkinBinder.Plain"/>, which is the order
        /// <see cref="BoneOverlay.Classify"/> colours it in. A dash means nothing binds here.</summary>
        private string FileJointFor(string bone)
        {
            foreach (KeyValuePair<string, string> e in aliases)
                if (e.Value == bone) return e.Key + "   (alias)";
            if (Ready != null && Ready.Model != null && Ready.Model.JointNames != null)
                foreach (string j in Ready.Model.JointNames)
                    if (BoneOverlay.MatchesByName(bone, j)) return j + "   (by name)";
            return "-";
        }

        /// <summary>
        /// The bone's REST placement, local to its parent, read off the renderer's own bind poses:
        /// <c>bindposes[i]</c> is <c>worldToBone_i * rendererLocalToWorld</c>, so
        /// <c>bindposes[p] * bindposes[i].inverse</c> is bone i expressed in bone p's rest frame.
        ///
        /// Null on Extend and on a mesh that carries no bind poses. There is no rest pose to read there,
        /// and deriving one from the live transform would be the CURRENT pose wearing a different label -
        /// which is worse than a dash, because it looks like an answer.
        /// </summary>
        private string RestOf(int i)
        {
            if (Renderer == null || Renderer.sharedMesh == null) return null;
            Matrix4x4[] poses = Renderer.sharedMesh.bindposes;
            if (poses == null || i < 0 || i >= poses.Length) return null;
            Matrix4x4 m = poses[i].inverse;
            int p = jointParent[i];
            if (p >= 0 && p < poses.Length) m = poses[p] * m;
            return Trs(m.GetColumn(3), m.rotation.eulerAngles, m.lossyScale) + "   (bind pose)";
        }

        private static string Trs(Vector3 t, Vector3 euler, Vector3 s)
        {
            return "T " + V(t) + "   R " + V(euler) + "   S " + V(s);
        }

        private static string V(Vector3 v)
        {
            return v.x.ToString("0.###") + "," + v.y.ToString("0.###") + "," + v.z.ToString("0.###");
        }

        /// <summary>
        /// THE TARGET'S bones, never the rig's full hierarchy dressed up as a slot. On Replace they are
        /// the renderer's own (slice 0 measured a Human head slot at 21 against the rig's 124); on
        /// Extend there is no renderer at all, so they are the transforms under <see cref="Root"/> the
        /// record calls bindable. A prototype whose slot produced nothing draws nothing.
        /// </summary>
        private Transform[] Joints()
        {
            if (Prototype != null && Prototype.Unavailable != null) return new Transform[0];
            if (Renderer != null) return Renderer.bones;
            if (Prototype == null || Prototype.Record == null || root == null) return new Transform[0];
            var want = new HashSet<string>(Prototype.Record.BindableBones, StringComparer.Ordinal);
            var found = new List<Transform>();
            // DUPLICATE NAMES ARE ALL KEPT, deliberately - a vehicle rig carries several transforms
            // called 'light' (PrototypeRecord.AmbiguousNames). Each is a distinct transform in a distinct
            // place, so each gets its own dot and its own pick; the inspector's PATH row is what tells
            // them apart afterwards.
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                if (want.Contains(t.name)) found.Add(t);
            return found.ToArray();
        }

        /// <summary>The whole per-generation half of the overlay: which transforms, how they hang off
        /// each other, what each one's colour MEANS, and the legend line. Everything here is read off
        /// the report the preflight already produced - no binding is recomputed, because a second
        /// opinion could drift from the verdict drawn three lines above it.</summary>
        private void Recache()
        {
            joints = Joints();
            int n = joints.Length;
            jointParent = new int[n];
            jointStatus = new BoneStatus[n];
            jointX = new float[n]; jointY = new float[n]; jointVisible = new bool[n];
            jointEligible = new bool[n];

            var index = new Dictionary<Transform, int>(n);
            for (int i = 0; i < n; i++) if (joints[i] != null) index[joints[i]] = i;
            for (int i = 0; i < n; i++)
            {
                int p;
                jointParent[i] = joints[i] != null && joints[i].parent != null &&
                                 index.TryGetValue(joints[i].parent, out p) ? p : -1;
            }

            ICollection<string> fileJoints =
                Ready == null || Ready.Model == null ? null : Ready.Model.JointNames;
            var missing = new HashSet<string>(StringComparer.Ordinal);
            if (Ready != null)
                foreach (Diagnostic d in Ready.Report.Rows)
                    if (d.Code == "MissingBone" && d.Subject != null) missing.Add(d.Subject);
            bool nearestBind = Ready != null && Ready.Outcome == Outcome.NearestBone;

            var present = new bool[StatusNames.Length];
            for (int i = 0; i < n; i++)
            {
                jointStatus[i] = BoneOverlay.Classify(joints[i] == null ? null : joints[i].name,
                                                      fileJoints, aliases, missing, nearestBind);
                present[(int)jointStatus[i]] = true;
            }

            // ONLY the statuses actually on the model. A legend naming five colours when three are on
            // screen is a legend an author has to filter for themselves.
            var names = new List<string>();
            for (int s = 0; s < StatusNames.Length; s++) if (present[s]) names.Add(StatusNames[s]);
            legend = names.Count == 0 ? "" : "skeleton: " + string.Join(" | ", names.ToArray());
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
            // EVERY CONTROL THAT CAN MOVE THE TARGET IS DEAD WHILE A PRESS IS ARMED: the gate spans one
            // frame the author can act in, and retargeting mid-press is what DoShip's generation check
            // would then have to throw the press away for.
            bool blind = Target == null || Target.BoneNames == null;
            GUI.enabled = !shipPending &&
                          (Ready.Outcome == Outcome.ByName || Ready.Outcome == Outcome.NearestBone) &&
                          Ready.Report.Count(Severity.Blocking) == 0 && !blind;
            if (GUILayout.Button(blind ? "Preview - no live bones to bind onto" : "Preview",
                                 GUILayout.Width(blind ? 230f : 80f))) Enqueue("preview");
            GUI.enabled = !shipPending && HasPreview;
            if (GUILayout.Button("Revert preview", GUILayout.Width(110f))) Enqueue("revert");
            // Changed AND valid, decided in Rethink: an unchanged map rewrites the sidecar for nothing,
            // and a map AliasMap.Of refuses would be refused again by the loader about to read it.
            GUI.enabled = !shipPending && canSave;
            if (GUILayout.Button("Save aliases", GUILayout.Width(110f))) Enqueue("save");
            // The same map, baked INTO the file instead of parked beside it. Offered whenever the map
            // has anything in it - including a map already saved to a sidecar, which is exactly the
            // state an author is in when they decide to bake it.
            GUI.enabled = !shipPending && aliases.Count > 0;
            if (GUILayout.Button("Write skel plan", GUILayout.Width(120f))) Enqueue("skelplan");
            GUI.enabled = !shipPending;
            if (GUILayout.Button("Copy report", GUILayout.Width(100f)))
                GUIUtility.systemCopyBuffer = PlainTextOf(Ready, Path, Target);
            GUI.enabled = true;
            GUILayout.EndHorizontal();

            Ship();
        }

        /// <summary>
        /// SHIP: from a green verdict to a mod folder the player can switch on, in one press. Read-and-enqueue
        /// like every other control here - the only thing it writes is its own text field and the repaint flag
        /// the two-frame gate waits for.
        /// </summary>
        private void Ship()
        {
            GUILayout.Space(6f);
            GUILayout.Label("SHIP - write a mod folder beside ContentTool, bake it, apply it");

            GUILayout.BeginHorizontal();
            GUILayout.Label("project", GUILayout.Width(56f));
            // Seeded on LAYOUT only: a value that changed between Layout and Repaint is how an IMGUI pass ends
            // up unbalanced.
            if (Event.current.type == EventType.Layout && projectName.Length == 0 &&
                Prototype != null && Prototype.ShippedAsset != null)
                projectName = ProjectScaffold.DefaultName(Prototype.ShippedAsset);
            projectName = GUILayout.TextField(projectName ?? "", GUILayout.Width(220f));
            GUILayout.Label(Prototype != null && Prototype.ShippedBundle != null
                            ? "target " + Prototype.ShippedBundle + " / " + Prototype.ShippedAsset
                            : (Prototype != null && Prototype.TargetRefusal != null
                               ? Prototype.TargetRefusal
                               : "no shipped target derived for this slot"));
            GUILayout.EndHorizontal();

            // ponytail: File.Exists on every OnGUI pass - two stats a frame on a local file. Cache it in
            // Refresh() (Layout only) if a profile ever shows it.
            string refusal = ProjectScaffold.NameRefusal(projectName);
            bool ready = Ready != null && Ready.Outcome == Outcome.ByName &&
                         Ready.Report.Count(Severity.Blocking) == 0 &&
                         Prototype != null && Prototype.Mode == VerifyMode.Replace && Prototype.Live != null &&
                         Prototype.TargetRefusal == null && Prototype.ShippedBundle != null &&
                         Renderer != null && Path != null && File.Exists(Path) &&
                         Path.EndsWith(".glb", StringComparison.OrdinalIgnoreCase) &&
                         refusal == null && !Busy && !shipPending;

            GUILayout.BeginHorizontal();
            GUI.enabled = ready;
            if (GUILayout.Button("CREATE, BAKE & APPLY", GUILayout.Width(200f))) Enqueue("ship");
            GUI.enabled = true;
            GUILayout.Label(shipPending ? shipPhase : (refusal ?? ""));
            GUILayout.EndHorizontal();

            // ALWAYS DRAWN, placeholder or not (design §4.4 "Rows, always drawn"). A row that appears only
            // once it has content makes the section jump under the author's cursor at the exact moment they
            // are reading a result, and an IMGUI layout that changes shape between one press and the next is
            // also how a Layout/Repaint pair ends up unbalanced.
            GUILayout.Label(shipPath.Length > 0 ? "project " + shipPath : "project -");
            GUILayout.Label(shipResult.Length > 0 ? shipResult : "-");
            GUILayout.Label(shipTail.Length > 0 ? shipTail : "-");

            // THE SECOND HALF OF THE GATE, and Repaint only: a Layout pass paints nothing, so arming on it
            // would let the freeze start under a panel that still says nothing.
            if (Event.current.type == EventType.Repaint && shipPending) shipLabelPainted = true;
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
        /// <summary>
        /// Section 6: the map is collapsed for a BY NAME verdict and opened for a name mismatch. Run
        /// ONCE per report, at the one moment a new one lands - never from the draw - so an author who
        /// closes it keeps it closed until the next verdict actually says something new. It only ever
        /// opens: a report that needs no map leaves an open one open, because the author opened it.
        /// </summary>
        private void AutoOpenMap()
        {
            if (mapOpen || Ready == null || Ready.Report == null) return;
            if (Ready.Outcome != Outcome.NearestBone)
            {
                bool extra = false;
                foreach (Diagnostic d in Ready.Report.Rows) if (d.Code == "ExtraBone") { extra = true; break; }
                if (!extra) return;
            }
            mapOpen = true;
        }

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

                // THE ARMED ROW SAYS SO ON THE ROW, not only over the viewport: the list below is a
                // column of names, and nothing in it hints that the model itself became clickable.
                GUILayout.Label("   armed - click a ringed bone on the model, or pick one here; Esc cancels");
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
            // An armed Ship MUST NOT survive the bench closing: it would run on the next Doctor, against a
            // renderer that is gone and a project name nobody typed. Dispose bumps gen above, so the
            // generation check would catch it too; clearing the fields is what stops the NEXT Doctor from
            // opening on the last one's result text.
            shipPending = false;
            shipLabelPainted = false;
            shipGen = -1;
            shipName = shipSource = shipSha = shipBundle = shipAsset = null;
            shipAliases = null;
            shipProto = null;
            shipTargetWas = null;
            shipRenderer = null;
            projectName = "";
            shipPhase = shipResult = shipPath = shipTail = "";
        }
    }
}
