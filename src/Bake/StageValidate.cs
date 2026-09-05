using System;
using System.Collections.Generic;
using System.IO;
using Morgott.ContentTool.Project;

namespace Morgott.ContentTool.Bake
{
    /// <summary>
    /// THE VALIDATE PRODUCER - design §4.1, and nothing more: the declaration's STRUCTURE plus the folder's
    /// activation eligibility. It does NOT prove the assets import or that the targets exist (design:102),
    /// and it never writes a byte.
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
            try
            {
                ManifestFile mf = ManifestFile.Load(manifestPath);   // Manifest.cs:290 - E1/E2/E8
                mf.Manifest.Validate();                              // :200 - E3 row, E4 duplicate target
                PatchCache.Key(projectRoot, shippedPaths);           // :43 - it must be COMPUTABLE, not stored
            }
            // BY TYPE, the three §4.1 can produce - the same rule LifecycleJob.Capture:88 already applies.
            // Anything else is a bug in here and belongs in LifecycleJob.Worker's handler, which says a
            // stage threw and keeps the exception, rather than wearing "fix ppcontent.json".
            catch (Exception ex) when (ex is IOException || ex is InvalidDataException ||
                                       ex is ArgumentException)
            {
                return new LifecycleState.StageReport(GateOutcome.Fail, StageText.ValidateFailed(ex.Message),
                                                      BakeDisposition.Failed, false, true, null);
            }
            // DISABLED IS NOT MALFORMED (design:103): its own field, never folded into the verdict.
            return new LifecycleState.StageReport(GateOutcome.Pass, StageText.S3(name),
                                                  BakeDisposition.Success, false, true,
                                                  ModGate.Why(ModGate.Decide(projectRoot, roster)));
        }
    }
}
