using System;
using System.Collections.Generic;
using System.IO;

namespace Morgott.ContentTool.Project
{
    /// <summary>
    /// The one rule that keeps a project's OTHER sources alive when one of them cannot be read.
    ///
    /// It carries no UnityEngine type on purpose - the same arrangement ModGate and CreatureManifest
    /// use - so the contract ContentProject.Load and ProjectBake state in prose ("a source the importer
    /// could not use is REPORTED and skipped, never fatal") is proven offline in ObjCodecTests rather
    /// than by watching a mod fail to activate.
    /// </summary>
    internal static class SourceImport
    {
        /// <summary>
        /// Imports every file, keeping what worked and NAMING what did not. Returns how many threw, so
        /// the bake can end on a failure count: skipping a source is not fatal to the run, but it is
        /// never an ALL PASS either.
        /// </summary>
        internal static int Each<T>(string[] files, List<T> into, List<string> refusals, Func<string, T> import)
        {
            int failures = 0;
            foreach (string f in files)
                try { into.Add(import(f)); }
                catch (Exception e)
                {
                    failures++;
                    refusals.Add(Path.GetFileName(f) + ": " + e.Message +
                                 " - SKIPPED, the project's other sources are unaffected");
                }
            return failures;
        }
    }
}
