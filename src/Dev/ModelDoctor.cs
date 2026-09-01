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
        private readonly ConcurrentQueue<Job> done = new ConcurrentQueue<Job>();
        private readonly Dictionary<string, string> aliases = new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly List<int> ourMeshes = new List<int>();

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
        internal IDictionary<string, string> Aliases => aliases;

        /// <summary>Instance ids of every Mesh this Doctor ever built, so the bench's leak gate can
        /// name what is still alive and blame the right owner rather than the scene at large.</summary>
        internal IList<int> OurMeshes => ourMeshes;

        // ------------------------------------------------------------------ picking

        internal void PickFile(string path)
        {
            Path = path;
            aliases.Clear();
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
            Restart();
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
            IList<BindingIssue> issues = SkinCompatibility.Analyze(model, target.BoneNames);
            Outcome outcome = ReplacementDecision.Decide(model.JointNames.Count > 0, target.Rigged,
                                                         target.BoneNames != null && target.BoneNames.Length > 0,
                                                         issues.Count == 0 ? null : issues[0]);
            result.Model = model;
            result.Outcome = outcome;
            var report = new DiagnosticReport { Outcome = outcome };
            foreach (BindingIssue issue in issues)
                report.Add(issue.Code.ToString(),
                           issue.Code == BindCode.NoArmature ? Severity.Blocking : Severity.Downgrade,
                           issue.Side == BindSide.Target ? DiagnosticSide.Target : DiagnosticSide.File,
                           issue.Message, Remedy.For(issue.Code), issue.Subject);
            foreach (string key in unused)
                report.Add("AliasUnused", Severity.Warning, DiagnosticSide.Sidecar,
                           "the alias for '" + key + "' was ignored: this file has no bone of that name",
                           "Delete the row, or rename the bone in Blender to '" + key + "'.", key);
            foreach (string key in live.OutputsNotIn(target.BoneNames))
                report.Add("AliasNotATargetBone", Severity.Warning, DiagnosticSide.Sidecar,
                           "the alias for '" + key + "' names a bone this model's skeleton does not have",
                           "Pick the target bone from the list instead of typing it.", key);
            result.Report = report;
        }

        // ------------------------------------------------------------------ the main thread

        /// <summary>Called every frame from the bench's Update. Drains results, then intents.</summary>
        internal void Tick()
        {
            Job job;
            while (done.TryDequeue(out job))
            {
                running = false;
                if (job.Gen != gen) continue;                          // stale: the author moved on
                if (Target == null) continue;                          // Dispose ran while it was in flight
                RigTarget now = Snapshot(Renderer, Target.TransformPath);
                if (!now.SameAs(Target))
                {
                    job.Result.Report.Add("TargetChanged", Severity.Blocking, DiagnosticSide.Target,
                                          "the model this report was made for has changed since it was picked",
                                          "Press Change and pick the target again.");
                    Target = now;
                }
                Ready = job.Result;
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
            ourMeshes.Add(candidate.GetInstanceID());
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
                Ready.Report.Add("PreviewDisagreed", Severity.Blocking, DiagnosticSide.Target,
                                 "the live bind came out " + got + " where the report predicted " + Ready.Outcome +
                                 ", so the preview was not applied",
                                 "The model changed under the report. Press Change and pick the target again.");
                return "preview REFUSED: the live bind disagreed with the report (" + got + " vs " + Ready.Outcome + ")";
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
            if (preview == null) return "no preview is live";
            if (Renderer != null)
            {
                Renderer.sharedMesh = origin;
                Renderer.localBounds = originBounds;
            }
            UnityEngine.Object.Destroy(preview);
            preview = null;
            return "preview reverted - the game's own mesh is back, by reference";
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
                Ready.Report.Add("AliasesSaved", Severity.Info, DiagnosticSide.Sidecar,
                                 aliases.Count + " alias(es) saved to " + AliasMap.SidecarPathOf(Path), "");
                return "saved " + aliases.Count + " alias(es) to " + AliasMap.SidecarPathOf(Path);
            }
            catch (Exception ex) { return "could not save: " + ex.Message; }
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
            gen++;
            Path = null;
            Renderer = null;
            Target = null;
            Ready = null;
            aliases.Clear();
        }
    }
}
