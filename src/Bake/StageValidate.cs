using System;
using System.Collections.Generic;
using System.IO;
using Morgott.ContentTool.Import;
using Morgott.ContentTool.Project;

namespace Morgott.ContentTool.Bake
{
    /// <summary>
    /// THE VALIDATE PRODUCER - design §4.1: the declaration's STRUCTURE, the folder's activation
    /// eligibility, and the part of every "replace" row that can be answered WITHOUT the shipped bundle.
    /// It never writes a byte.
    ///
    /// WHAT IT NOW ANSWERS, because PASSing a project whose every row the bake will refuse taught the
    /// author nothing (2026-09-06 run): a row's source file must be where the bake will look for it
    /// (Content\Textures\, Content\Meshes\ - the misplaced RR_soldier_albedo.png and the `.glb` that is not
    /// there), and a .glb that declares no armature is NOTED, because it can only bind onto a rigged
    /// target by losing its weights.
    ///
    /// WHAT STAYS WITH BAKE, deliberately: whether the shipped bundle holds an asset of that name and
    /// type, and whether the target is rigged at all. Both need the bundle open, which is the bake's job
    /// and not a stage that must stay UnityEngine-free.
    ///
    /// IT IS UnityEngine-FREE ON PURPOSE. Three of the four calls below are plain System.IO and the fourth,
    /// <see cref="ModGate.Decide"/>, takes the roster as a DICTIONARY - the one Unity-bound half is
    /// <c>ModRoster.Build</c> (ModRoster.cs:53, ModManager.Mods), which <see cref="LifecycleJob.Capture"/>
    /// runs on MAIN and hands in. That split is what lets the whole producer be armed offline (G8).
    /// </summary>
    internal static class StageValidate
    {
        internal static LifecycleState.StageReport Run(string projectRoot, string manifestPath,
                                                       IList<string> shippedPaths,
                                                       IDictionary<string, bool> roster)
        {
            string name = Path.GetFileName(projectRoot.TrimEnd('\\', '/'));
            string key;
            var missing = new List<string>();
            var noted = new List<string>();
            try
            {
                ManifestFile mf = ManifestFile.Load(manifestPath);   // Manifest.cs:290 - E1/E2/E8
                mf.Manifest.Validate();                              // :200 - E3 row, E4 duplicate target
                foreach (ReplaceRow row in mf.Manifest.Replace) Sources(projectRoot, row, missing, noted);
                // :43 - it must be COMPUTABLE, and the verdict SAYS it: a call whose result was thrown away
                // proved nothing anyone could read, and a key that stopped matching B1's read as a run
                // nobody could compare.
                key = PatchCache.Key(projectRoot, shippedPaths);
            }
            // BY TYPE, the four §4.1 can produce - the same rule LifecycleJob.Capture:88 already applies.
            // UnauthorizedAccessException is one of them: File.ReadAllBytes on a directory or an ACL-denied
            // path is the author's file being unreadable, not a bug in here.
            // Anything else is a bug in here and belongs in LifecycleJob.Worker's handler, which says a
            // stage threw and keeps the exception, rather than wearing "fix ppcontent.json".
            catch (Exception ex) when (ex is IOException || ex is InvalidDataException ||
                                       ex is ArgumentException || ex is UnauthorizedAccessException)
            {
                return new LifecycleState.StageReport(GateOutcome.Fail, StageText.ValidateFailed(ex.Message),
                                                      BakeDisposition.Failed, false, true, null);
            }
            // A ROW WHOSE FILE IS NOT THERE IS A FAILED VALIDATE, not a note: the bake refuses that row
            // (P1/P4) and counts it, so PASSing it here is the panel promising a run that cannot happen.
            if (missing.Count > 0)
                return new LifecycleState.StageReport(GateOutcome.Fail,
                                                      StageText.ValidateFailed(string.Join(" ", missing.ToArray())),
                                                      BakeDisposition.Failed, false, true, null);
            // DISABLED IS NOT MALFORMED (design:103): its own field, never folded into the verdict.
            return new LifecycleState.StageReport(GateOutcome.Pass,
                                                  StageText.S3(name, key) + StageText.ValidateNotes(noted),
                                                  BakeDisposition.Success, false, true,
                                                  ModGate.Why(ModGate.Decide(projectRoot, roster)));
        }

        /// <summary>
        /// One row's SOURCE FILE, answered from the file system alone - the half of the bake's P1/P4
        /// refusals that needs no bundle. A missing file is <paramref name="missing"/> (the run will be
        /// refused); a .glb that declares no armature is <paramref name="noted"/>, because whether that
        /// costs the author's weights or refuses the row outright depends on the TARGET, which only the
        /// bake can open.
        /// </summary>
        private static void Sources(string root, ReplaceRow row, List<string> missing, List<string> noted)
        {
            // A COLLIDING STEM IS THE BAKE'S OWN SENTENCE, not "the file is not there": both files are
            // dropped by the one enumerator (ContentMods.Sources), so the author is looking at a file
            // that IS in the right folder and would be told it is missing.
            string collision;
            if (!string.IsNullOrEmpty(row.Texture))
            {
                if (ContentMods.SourceFile(root, "Textures", row.Texture, ContentMods.TexturePatterns,
                                           out collision) == null)
                    missing.Add(collision ??
                                "'" + row.Texture + "' is not a .png/.jpg under Content\\Textures\\" +
                                ContentMods.Elsewhere(root, row.Texture, "Textures",
                                                      ContentMods.TexturePatterns) + ".");
                return;
            }
            if (string.IsNullOrEmpty(row.Mesh)) return;
            string mesh = ContentMods.SourceFile(root, "Meshes", row.Mesh, ContentMods.MeshPatterns,
                                                 out collision);
            if (mesh == null)
            {
                missing.Add(collision ??
                            "'" + row.Mesh + "' is not a .obj or .glb under Content\\Meshes\\" +
                            ContentMods.Elsewhere(root, row.Mesh, "Meshes", ContentMods.MeshPatterns) + ".");
                return;
            }
            if (!mesh.EndsWith(".glb", StringComparison.OrdinalIgnoreCase)) return;
            // THE CONTAINER'S OWN JSON, not a full import: a file that declares no "skins" carries no
            // armature by construction (GlbReader.ReadSkin:791 is only reached through one), and reading
            // 10 MB of geometry to learn it would turn Validate into a second bake. A file this cannot
            // parse says nothing here - the import verdict is the bake's, in its own words.
            try
            {
                object skins;
                List<object> declared = GlbDocument.Load(mesh).Json.TryGetValue("skins", out skins)
                                        ? skins as List<object> : null;
                if (declared == null || declared.Count == 0)
                    noted.Add("'" + Path.GetFileName(mesh) + "' carries no armature, so a rigged target " +
                              "would refuse it and an unrigged one takes it as-is.");
            }
            catch (Exception) { }
        }
    }
}
