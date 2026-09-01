using System;
using System.Collections.Generic;
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
            var result = new ReplacementPreflightResult();
            if (target == null) target = new RigTarget();
            try
            {
                ReplacementSource source = GlbSource.ReadReplacement(bytes, path);
                result.Source = source;
                result.Sha256 = source.Sha256;
                result.Model = source.Model;
                result.Original = source.Original;
                result.Baked = ModelBuild.From(source.Model, "preflight");
                Sidecar(result, source, target);
                return Verdict(result, source, target);
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
        private static void Sidecar(ReplacementPreflightResult result, ReplacementSource source, RigTarget target)
        {
            if (source.SidecarRefusal != null)
                result.Report.Add(source.SidecarRefusal.IndexOf("re-exported", StringComparison.Ordinal) >= 0
                                      ? "SidecarStale" : "SidecarInvalid",
                                  Severity.Warning, DiagnosticSide.Sidecar, source.SidecarRefusal,
                                  "Open the bone map, set the names again and press Save aliases.");
            foreach (string key in source.UnusedAliasKeys)
                result.Report.Add("AliasUnused", Severity.Warning, DiagnosticSide.Sidecar,
                                  "the alias for '" + key + "' was ignored: this file has no bone of that name",
                                  "Delete the row, or rename the bone in Blender to '" + key + "'.", key);
            if (source.Aliases == null) return;
            foreach (string key in source.Aliases.OutputsNotIn(target.BoneNames))
                result.Report.Add("AliasNotATargetBone", Severity.Warning, DiagnosticSide.Sidecar,
                                  "the alias for '" + key + "' names a bone this model's skeleton does not have",
                                  "Pick the target bone from the list instead of typing it.", key);
        }

        private static ReplacementPreflightResult Verdict(ReplacementPreflightResult result,
                                                          ReplacementSource source, RigTarget target)
        {
            // The OUTCOME is computed from the model the BAKE would see. When a sidecar did not apply,
            // that is the unaliased one - which is exactly what the bake will read from the same
            // sidecar a moment later.
            SkinnedModel effective = source.Aliases == null ? source.Original : source.Model;
            bool armature = effective.JointNames.Count > 0;
            bool names = target.BoneNames != null && target.BoneNames.Length > 0;

            IList<BindingIssue> issues = names
                ? SkinCompatibility.Analyze(effective, target.BoneNames)
                : new List<BindingIssue>();
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
                if (!names)
                    result.Report.Add("TargetBonesUnavailable", Severity.Downgrade, DiagnosticSide.Target,
                                      "the target model lists no bones, so there is no skeleton to bind onto",
                                      "Re-pick the target, or reload the scene.");
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
                case BindCode.DuplicateFileBone: return "Two of your bones share a name - rename one in Blender and re-export.";
                case BindCode.PlainCollision: return "Keep the one bone that belongs to this model, delete the other, and re-export.";
                case BindCode.MissingBone: return "Rename your bone to this name - or map it in the table above.";
                case BindCode.ExtraBone: return "Map this bone to the one it stands for, or transfer its weights to its parent and delete it.";
                case BindCode.NotBijective: return "Check the table above for a target bone chosen twice.";
                case BindCode.InverseBindCount: return "Broken export - re-export from Blender rather than editing the file.";
                default: return "Broken export - re-export from Blender rather than editing the file.";
            }
        }
    }
}
