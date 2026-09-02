using System;
using System.Collections.Generic;
using System.Globalization;
using Morgott.ContentTool.Import;

namespace Morgott.ContentTool.Doctor
{
    /// <summary>Everything one run produced. The report is what the author reads; the rest is what a
    /// preview and a save need, so neither has to parse the file again.</summary>
    internal sealed class ReplacementPreflightResult
    {
        internal DiagnosticReport Report = new DiagnosticReport();
        internal Outcome Outcome;
        /// <summary>The ALIASED model - what LiveMesh.Build must be handed.</summary>
        internal SkinnedModel Model;
        /// <summary>The same file with its own names, so an alias edit re-derives from originals.</summary>
        internal SkinnedModel Original;
        /// <summary>ModelBuild's own output. Null when the import was refused.</summary>
        internal BakedSkin Baked;
        internal string Sha256;
        internal ReplacementSource Source;
        /// <summary>The exception behind an ImportFailed row, for the log only. The REPORT is what the
        /// author reads; a stack trace belongs in the log and nowhere else.</summary>
        internal Exception Failure;
    }

    /// <summary>
    /// BYTES TO VERDICT, and the only thing that runs off the main thread. No UnityEngine type enters
    /// or leaves, so the whole pipeline is provable in tests\ObjCodecTests - and so a worker thread
    /// cannot construct a Unity object by accident, which would be a crash rather than a bug.
    ///
    /// The outcome is NOT decided here. It is asked of ReplacementDecision.Decide, the same function
    /// BundleBaker.ReplaceMesh asks.
    /// </summary>
    internal static class ReplacementPreflight
    {
        internal static ReplacementPreflightResult Run(byte[] bytes, string path, RigTarget target)
        {
            return Core(bytes, path, target ?? new RigTarget(), null);
        }

        /// <summary>
        /// THE PROTOTYPE ENTRY POINT. Replace is the very same run as above - the live slot renderer's
        /// own snapshot, byte for byte what the bake will see - and Extend is the only new question:
        /// the file held against the prototype's bindable bones, where a rig bone nothing uses is no
        /// defect. A slot the rebuild produced no renderer for is refused OUTRIGHT rather than judged
        /// against a bone list fabricated from the full rig, which is the one thing this picker exists
        /// to stop.
        /// </summary>
        internal static ReplacementPreflightResult Run(byte[] bytes, string path, PrototypeTarget target)
        {
            if (target == null) return Run(bytes, path, (RigTarget)null);
            if (target.Mode == VerifyMode.Replace && target.Unavailable != null)
            {
                var refused = new ReplacementPreflightResult
                {
                    Outcome = Outcome.Refused,
                    Sha256 = AliasMap.Sha256(bytes ?? new byte[0])
                };
                refused.Report.Add("SlotVisualUnavailable", Severity.Blocking, DiagnosticSide.Target,
                                   target.Unavailable + " - this slot's rebuild produced no renderer, so " +
                                   "there is no bone list to be exact against",
                                   "Ask this slot for Extend instead, or pick a slot the rebuild really produced.");
                refused.Report.Outcome = Outcome.Refused;
                return refused;
            }
            return Core(bytes, path, LiveOf(target), target);
        }

        /// <summary>The RigTarget a prototype pick is judged against: the live slot renderer on Replace,
        /// and NOTHING at all on Extend, where no renderer exists and the bindable set is the target.
        /// </summary>
        private static RigTarget LiveOf(PrototypeTarget target)
        {
            return target.Mode == VerifyMode.Replace ? (target.Live ?? new RigTarget()) : null;
        }

        /// <summary>The prototype's bone names as the array the alias checks take. Null stays null - a
        /// target with no list is never given a guessed one.</summary>
        internal static string[] BoneArray(PrototypeTarget target)
        {
            IList<string> bones = target == null ? null : target.BoneNames();
            if (bones == null) return null;
            var names = new string[bones.Count];
            bones.CopyTo(names, 0);
            return names;
        }

        private static ReplacementPreflightResult Core(byte[] bytes, string path, RigTarget live,
                                                       PrototypeTarget proto)
        {
            var result = new ReplacementPreflightResult();
            try
            {
                ReplacementSource source = GlbSource.ReadReplacement(bytes, path);
                result.Source = source;
                result.Sha256 = source.Sha256;
                result.Model = source.Model;
                result.Original = source.Original;
                result.Baked = ModelBuild.From(source.Model, "preflight");
                Sidecar(result, source, live != null ? live.BoneNames : BoneArray(proto));
                // The OUTCOME is computed from the model the BAKE would see. When a sidecar did not
                // apply, that is the unaliased one - which is exactly what the bake will read from the
                // same sidecar a moment later.
                SkinnedModel effective = source.Aliases == null ? source.Original : source.Model;
                return live != null ? Judge(result, effective, live) : Judge(result, effective, proto);
            }
            catch (ImportRefusedException refused)
            {
                result.Failure = refused;
                result.Sha256 = result.Sha256 ?? AliasMap.Sha256(bytes ?? new byte[0]);
                result.Report.Add(refused.Code.ToString(), Severity.Blocking, DiagnosticSide.File,
                                  refused.Message, Remedy.For(refused.Code));
                result.Outcome = Outcome.Refused;
                result.Report.Outcome = Outcome.Refused;
                return result;
            }
            catch (Exception ex)
            {
                // THE WORKER BOUNDARY. An I/O error, a bug, anything at all: it becomes a row rather
                // than an unhandled exception on a background thread, which in Unity is a hard stop.
                result.Failure = ex;
                result.Report.Add("ImportFailed", Severity.Blocking, DiagnosticSide.File,
                                  "'" + path + "' could not be read: " + ex.GetType().Name + " - " + ex.Message,
                                  "This is not an export setting - the log carries the details. " +
                                  "Check the file is not open in another program and try again.");
                result.Outcome = Outcome.Refused;
                result.Report.Outcome = Outcome.Refused;
                return result;
            }
        }

        /// <summary>Everything the sidecar has to say, all of it a WARNING: ignoring a sidecar leaves a
        /// file that may still bind by name on its own, so a sidecar never decides the outcome.</summary>
        private static void Sidecar(ReplacementPreflightResult result, ReplacementSource source,
                                    string[] targetBoneNames)
        {
            if (source.SidecarRefusal != null)
                result.Report.Add(source.SidecarProblem == SidecarProblem.Stale ? "SidecarStale" : "SidecarInvalid",
                                  Severity.Warning, DiagnosticSide.Sidecar, source.SidecarRefusal,
                                  "Open the bone map, set the names again and press Save aliases.");
            foreach (string key in source.UnusedAliasKeys)
                result.Report.Add("AliasUnused", Severity.Warning, DiagnosticSide.Sidecar,
                                  "the alias for '" + key + "' was ignored: this file has no bone of that name",
                                  "Delete the row, or rename the bone in Blender to '" + key + "'.", key);
            if (source.Aliases == null) return;
            foreach (string key in source.Aliases.OutputsNotIn(targetBoneNames))
                result.Report.Add("AliasNotATargetBone", Severity.Warning, DiagnosticSide.Sidecar,
                                  "the alias for '" + key + "' names a bone this model's skeleton does not have",
                                  "Pick the target bone from the list instead of typing it.", key);
        }

        /// <summary>
        /// THE EXTEND VERDICT - a PARTIAL body part held against the prototype's bindable bones. Two
        /// things are different from Replace and nothing else is:
        ///
        ///  * a rig bone the file does not use is no defect (Analyze's own extend arm suppresses
        ///    MissingBone and never asks the bijection), because a hand does not carry the legs;
        ///  * there is no nearest-bone fallback to degrade INTO. Replace loses the author's weights and
        ///    still writes a mesh; nothing at all attaches a part whose joints do not resolve onto the
        ///    rig. So every row blocks except <see cref="BindCode.ExtJointUnused"/>, the one that says
        ///    nothing is lost.
        ///
        /// A Replace pick is handed straight back to the RigTarget arm, so the shipped path cannot move.
        /// </summary>
        internal static ReplacementPreflightResult Judge(ReplacementPreflightResult result,
                                                        SkinnedModel effective, PrototypeTarget target)
        {
            if (target == null || target.Mode == VerifyMode.Replace)
                return Judge(result, effective, target == null ? new RigTarget() : (target.Live ?? new RigTarget()));

            IList<string> bones = target.BoneNames();
            bool armature = effective.JointNames.Count > 0;
            bool names = bones != null && bones.Count > 0;
            // A prototype rig HAS bind poses by construction, so "rigged" is simply "it has bones to
            // bind onto" - there is no live mesh here whose bind-pose count could disagree with them.
            IList<BindingIssue> issues = SkinCompatibility.Analyze(effective, bones, 0, true);
            // A file joint named EXT_ is ALWAYS an "extra bone" too - the bindable set excludes every
            // EXT_ name by construction - and saying both would put the design's rule out of reach: the
            // ExtraBone row would block first, every time, and an unweighted attachment point could
            // never be the warning it is meant to be. The ExtJoint* row is the precise one, so it is
            // the only one.
            for (int i = issues.Count - 1; i >= 0; i--)
                if (issues[i].Code == BindCode.ExtraBone && PrototypeCatalog.IsAttachmentPoint(issues[i].Subject))
                    issues.RemoveAt(i);
            // An attachment point the file merely CARRIES costs nothing - it is not a reason for a
            // verdict, so it is not what Decide is asked about. Everything else is.
            BindingIssue first = null;
            for (int i = 0; i < issues.Count && first == null; i++)
                if (issues[i].Code != BindCode.ExtJointUnused) first = issues[i];
            Outcome outcome = ReplacementDecision.Decide(armature, names, names, first);

            if (outcome == Outcome.Refused)
                result.Report.Add("SkinlessOntoRigged", Severity.Blocking, DiagnosticSide.File,
                                  Bake.SkinFields.Skinless(target.Record == null ? "this model" : target.Record.DisplayName),
                                  "In Blender give the mesh an Armature modifier with vertex groups, " +
                                  "weight it to the bones the target already has, and export as .glb.");
            else if (outcome == Outcome.NotRigged)
                result.Report.Add("TargetNotRigged", Severity.Info, DiagnosticSide.Target,
                                  "not rigged - this prototype lists no bindable bones", "");
            else
                foreach (BindingIssue issue in issues)
                {
                    Severity severity = issue.Code == BindCode.ExtJointUnused ? Severity.Warning : Severity.Blocking;
                    result.Report.Add(issue.Code.ToString(), severity,
                                      issue.Side == BindSide.Target ? DiagnosticSide.Target : DiagnosticSide.File,
                                      issue.Message, Remedy.For(issue.Code), issue.Subject);
                    if (severity == Severity.Blocking) outcome = Outcome.Refused;
                }

            // THE DUPLICATE RULE (design section 3). The bindable set is deduplicated, so Analyze cannot
            // see an ambiguity at all - and it must block only the file that really names one, because
            // the game matches by name and takes FirstOrDefault (Addon.cs:1202-1231). Which of the two
            // transforms that is cannot be predicted, so no verdict about it would be honest; every
            // other slot on the same rig stays perfectly usable.
            foreach (string name in target.BlockingAmbiguous(effective.JointNames))
            {
                result.Report.Add("TargetBoneDuplicate", Severity.Blocking, DiagnosticSide.Target,
                                  "this prototype's rig has more than one transform named '" + name +
                                  "' and the game binds to the first one it finds, so where the file's bone " +
                                  "of that name would land cannot be predicted",
                                  "Move that influence onto a bone the rig names only once, and re-export.", name);
                outcome = Outcome.Refused;
            }

            result.Outcome = outcome;
            result.Report.Outcome = outcome;
            return result;
        }

        /// <summary>
        /// The verdict over a model that is ALREADY the effective one, and every row that explains it.
        /// Split out because the Doctor recomputes both after an unsaved alias edit: a second copy of
        /// these three arms is a second place for "REFUSED (0 reason(s))" to appear - a header with no
        /// row under it, which is exactly what a hand-rolled rebuild produced.
        /// </summary>
        internal static ReplacementPreflightResult Judge(ReplacementPreflightResult result,
                                                        SkinnedModel effective, RigTarget target)
        {
            bool armature = effective.JointNames.Count > 0;
            bool names = target.BoneNames != null && target.BoneNames.Length > 0;

            // Analyze is asked even with no bone names: it emits TargetBonesUnavailable itself
            // (SkinCompatibility.cs:111), in the binder's own words, so the row cannot drift from a
            // second copy of the same sentence written here.
            IList<BindingIssue> issues = SkinCompatibility.Analyze(effective, target.BoneNames);

            // THE ONE CHECK ANALYZE CANNOT MAKE. It is handed bone NAMES; the bind pose count lives on
            // the target snapshot. SkinFields.RebindByName (SkinFields.cs:738-741) refuses the two
            // disagreeing and the bake falls back to nearest-bone, so a Doctor that did not ask would
            // promise BY NAME for a model the bake downgrades. First in the list, because Decide reads
            // issues[0] and this refusal happens before any name is compared.
            if (names && target.BindPoseCount != target.BoneNames.Length)
                issues.Insert(0, new BindingIssue
                {
                    Code = BindCode.TargetBindPoseMismatch,
                    Stage = BindStage.Bones,
                    Side = BindSide.Target,
                    Message = "the target has " + target.BindPoseCount.ToString(CultureInfo.InvariantCulture) +
                              " bind pose(s) but " + target.BoneNames.Length.ToString(CultureInfo.InvariantCulture) +
                              " named bone(s), so a bone in the file cannot be matched to one of them"
                });

            BindingIssue first = issues.Count == 0 ? null : issues[0];
            Outcome outcome = ReplacementDecision.Decide(armature, target.Rigged, names, first);

            if (outcome == Outcome.Refused)
                result.Report.Add("SkinlessOntoRigged", Severity.Blocking, DiagnosticSide.File,
                                  Bake.SkinFields.Skinless(target.MeshName ?? "this model"),
                                  "In Blender give the mesh an Armature modifier with vertex groups, " +
                                  "weight it to the bones the target already has, and export as .glb.");
            else if (outcome == Outcome.NotRigged)
                result.Report.Add("TargetNotRigged", Severity.Info, DiagnosticSide.Target,
                                  "not rigged - the target carries no bind poses", "");
            else
            {
                foreach (BindingIssue issue in issues)
                    result.Report.Add(issue.Code.ToString(),
                                      issue.Code == BindCode.NoArmature ? Severity.Blocking : Severity.Downgrade,
                                      issue.Side == BindSide.Target ? DiagnosticSide.Target : DiagnosticSide.File,
                                      issue.Message, Remedy.For(issue.Code), issue.Subject);
            }

            result.Outcome = outcome;
            result.Report.Outcome = outcome;
            return result;
        }
    }

    /// <summary>
    /// The one sentence that turns a refusal into an action. Deliberately separate from the engine's
    /// own message, which stays verbatim: the message says what happened, the remedy says which box
    /// to tick in Blender 4.x, and only the second one goes stale when Blender moves a menu.
    /// </summary>
    internal static class Remedy
    {
        internal static string For(ImportCode code)
        {
            switch (code)
            {
                case ImportCode.Oversize: return "Reduce texture resolution, or remove unused meshes and animations, and re-export.";
                case ImportCode.ExternalBuffer: return "In the export dialog set Format to 'glTF Binary (.glb)', not 'glTF Separate'.";
                case ImportCode.NoMesh: return "Check the mesh object is selected and visible when you export.";
                case ImportCode.NonTriangle: return "Tick 'Apply Modifiers' and triangulate the faces (Triangulate modifier, or Ctrl+T in Edit mode).";
                case ImportCode.NotIndexed: return "Re-export with Blender's own glTF exporter; indexed geometry is its default.";
                case ImportCode.TooManyInfluences: return "Weight Paint > Weights > Limit Total, set it to 4, then re-export.";
                case ImportCode.NoVertices: return "Export the mesh itself, not an empty or an armature-only selection.";
                case ImportCode.NoNormals: return "In the export dialog, under Mesh, leave 'Normals' ticked.";
                case ImportCode.UnsupportedGlb: return "Import the file into Blender and export it again with compression and extension add-ons off.";
                default: return "Re-export the file from Blender rather than editing it by hand.";
            }
        }

        internal static string For(BindCode code)
        {
            switch (code)
            {
                case BindCode.TargetBonesUnavailable: return "Re-pick the target, or reload the scene.";
                case BindCode.NoArmature: return "Parent the mesh to the armature (Ctrl+P > Armature Deform) and export with Skinning on.";
                case BindCode.JointsWeightsMismatch: return "Give the WHOLE mesh an Armature modifier with vertex groups, then re-export.";
                case BindCode.TriangleOutOfRange: return "Re-export; do not edit the .glb by hand.";
                case BindCode.BlendShapeCount: return "Remove the shape keys, or replace a model that has them - a replacement cannot add shapes.";
                case BindCode.TargetBoneEmpty: return "This is the game's own model. Re-pick the target.";
                case BindCode.TargetBoneDuplicate: return "This is the game's own model; it cannot be replaced by name.";
                case BindCode.TargetBindPoseMismatch: return "This is the game's own model - nothing in your file causes it. Pick a different target, or accept nearest-bone.";
                case BindCode.DuplicateFileBone: return "Two of your bones share a name - rename one in Blender and re-export.";
                case BindCode.PlainCollision: return "Keep the one bone that belongs to this model, delete the other, and re-export.";
                case BindCode.MissingBone: return "Rename your bone to this name - or map it in the table above.";
                case BindCode.ExtraBone: return "Map this bone to the one it stands for, or transfer its weights to its parent and delete it.";
                case BindCode.NotBijective: return "Check the table above for a target bone chosen twice.";
                case BindCode.InverseBindCount: return "Broken export - re-export from Blender rather than editing the file.";
                case BindCode.ExtJointWeighted: return "In Blender move that influence onto a real bone - one whose name does not start with EXT_ - and re-export.";
                case BindCode.ExtJointUnused: return "Nothing is lost. Delete the EXT_ bone in Blender if you want the file tidy.";
                default: return "Broken export - re-export from Blender rather than editing the file.";
            }
        }
    }
}
