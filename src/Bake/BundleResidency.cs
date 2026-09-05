using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace Morgott.ContentTool.Bake
{
    /// <summary>
    /// Who is holding a bundle open, and letting go of it before we load its replacement.
    ///
    /// THE BUG THIS EXISTS FOR (in-game, build b1720c7f): a shipped content mod loads its own bundle
    /// when the player enables it (demos\AddUiSounds\src\AddUiSoundsMain.cs:52). Re-baking that same
    /// project in the SAME session then wrote a correct file and failed to read it back -
    /// "another AssetBundle with the same files is already loaded", then
    /// "FAIL AssetBundle.LoadFromFile returned null". The bake reported FAIL on a bundle it had
    /// written perfectly. That breaks "edit content, re-bake, see it", which is the whole authoring
    /// loop, for every project whose mod is switched on.
    ///
    /// UNITY'S OWN REGISTRY IS THE SOURCE OF TRUTH. AssetBundle.GetAllLoadedAssetBundles() knows
    /// every resident bundle no matter who opened it - our auto-apply, a demo's own DLL, an earlier
    /// bake. A ledger of our own could only ever list the holders WE know about, and the holder that
    /// caused this was somebody else's assembly.
    ///
    /// Unload(FALSE), and the difference from sound is real rather than assumed: Unity documents
    /// Unload(false) as releasing the archive while every object already loaded from it stays alive,
    /// so the mod's clips keep playing and only the file handle goes. Wwise has no such option -
    /// UnloadBank kills the event outright (measured, see SoundLoad.UnloadMod) - which is why sound
    /// is left alone and bundles are not. Same word, different contract; each one measured on its own.
    /// </summary>
    internal static class BundleResidency
    {
        /// <summary>The name a bake writes into the bundle: the project id, lowercased, dots to
        /// underscores. One rule, so the release matches what the writer produced.</summary>
        internal static string Identity(string projectId)
        {
            return projectId == null ? null : projectId.ToLowerInvariant().Replace('.', '_');
        }

        /// <summary>
        /// Let go of every resident bundle carrying <paramref name="identity"/>. Returns the line to
        /// log, or null when nothing was holding it - silence when there is nothing to say.
        /// </summary>
        internal static string Release(string identity)
        {
            if (string.IsNullOrEmpty(identity)) return null;
            // Collected first: Unload while enumerating Unity's own list is asking for it.
            List<AssetBundle> holding = new List<AssetBundle>();
            foreach (AssetBundle b in AssetBundle.GetAllLoadedAssetBundles())
                if (b != null && b.name == identity) holding.Add(b);
            if (holding.Count == 0) return null;
            foreach (AssetBundle b in holding) b.Unload(false);
            return "released " + holding.Count + " resident copy(ies) of '" + identity +
                   "' before loading the new one (the objects already loaded from it stay alive)";
        }

        /// <summary>
        /// Gate B1: re-baking a project whose mod is ENABLED still verifies.
        ///
        /// A plain bake-twice arm cannot see this bug and that is why it shipped: ProjectBake unloads
        /// its own handle in its finally, so a second bake with nobody else holding the file passes
        /// either way. The holder has to be a THIRD party, so this arm becomes one - it opens the
        /// bundle and keeps it open exactly as the demo's DLL does, and only then re-bakes.
        ///
        /// B1-resident is the arm that makes the rest non-vacuous: it asserts the file really WAS
        /// resident at the moment of the second bake. Without it a run where the load failed would
        /// pass by accident.
        /// </summary>
        internal static string Gate(string projectRoot)
        {
            StringBuilder log = new StringBuilder();
            int fail = 0;

            string first = ProjectBake.Run(projectRoot);
            log.AppendLine("bake 1: " + Tail(first));
            if (!StageResult.BakePassed(first))
                return log.Append("B1 VOID the FIRST bake did not pass, so nothing about re-baking " +
                                  "was measured - fix that bake first").ToString();

            Project.ContentProject.Declared p = Project.ContentProject.LoadDeclared(projectRoot);
            string outPath = Path.Combine(Path.Combine(projectRoot, "Dist"), p.BundleName);
            string identity = Identity(p.Id);

            // Exactly what a shipped mod's own DLL does at enable, and then keeps.
            AssetBundle held = AssetBundle.LoadFromFile(outPath);
            fail += Check(log, "B1-hold", held != null,
                "a third party (this arm, standing in for the mod's own DLL) opened " + outPath);
            if (held == null)
                return log.Append("B1 VOID could not open the bundle to hold it").ToString();

            int resident = 0;
            foreach (AssetBundle b in AssetBundle.GetAllLoadedAssetBundles())
                if (b != null && b.name == identity) resident++;
            fail += Check(log, "B1-resident", resident > 0,
                identity + " is resident " + resident + " time(s) at the moment of the second bake - " +
                "this is what made the old bake FAIL on a file it wrote correctly");

            string second = ProjectBake.Run(projectRoot);
            log.AppendLine("bake 2: " + Tail(second));
            fail += Check(log, "B1-rebake", StageResult.BakePassed(second),
                "the second bake verified its own output while the mod was holding the old copy");

            // Ours to clean up: the arm is the holder, so the arm lets go.
            foreach (AssetBundle b in AssetBundle.GetAllLoadedAssetBundles())
                if (b != null && b.name == identity) { b.Unload(false); break; }

            return log.Append(fail == 0 ? "ct_project twice: ALL PASS" : "ct_project twice: " + fail + " FAILURE(S)").ToString();
        }

        /// <summary>The verdict line of a bake, without the report above it.</summary>
        private static string Tail(string report)
        {
            int at = report.LastIndexOf("ct_project:", System.StringComparison.Ordinal);
            return at < 0 ? report : report.Substring(at);
        }

        private static int Check(StringBuilder log, string gate, bool ok, string detail)
        {
            log.AppendLine(gate + (ok ? " PASS " : " FAIL ") + detail);
            return ok ? 0 : 1;
        }
    }
}
