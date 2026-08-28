using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace Morgott.ContentTool.Dev
{
    /// <summary>
    /// How many gates are still running after their command returned. Gate V1 waits on a video
    /// decoder, which needs the player loop to turn, so it cannot answer synchronously - and a
    /// ct_autorun that printed DONE first would let autogate kill the game mid-measurement and read
    /// a run that measured nothing as a run that measured everything.
    /// </summary>
    internal static class AsyncGate
    {
        internal static int Pending;
    }

    /// <summary>
    /// Takes the HUMAN out of the gate loop, not the game: texture/mesh/Wwise/Addressables behaviour
    /// cannot be faked, so the gates still run inside a live Unity - nobody has to sit there typing
    /// them.
    ///
    /// If <c>autorun.txt</c> exists next to the mod (or <c>CT_AUTORUN</c> points at a file), each line
    /// is executed as a console command once the scene has something to look at, and the run ends with
    /// <c>ct_autorun: DONE n command(s)</c> so a script can tell finished from crashed.
    ///
    /// Dev-only by construction: a released mod ships no autorun.txt and nothing in the bake writes
    /// one, so with the file absent this class never allocates anything.
    /// </summary>
    internal static class AutoRun
    {
        internal const string Marker = "ct_autorun: DONE";
        private const int SettleFrames = 30;      // ~half a second between checks, cheap
        private const float TimeoutSeconds = 120f;
        // The async phase gets its own, much larger budget: gate M1 loads a savegame and waits for a
        // whole tactical mission to come up, which is minutes, not seconds. Cutting it short would
        // print "treat this run as VOID" for a gate that was still measuring correctly.
        private const float AsyncTimeoutSeconds = 600f;

        internal static void MaybeStart(string modDir, Action<string> log)
        {
            string path = Environment.GetEnvironmentVariable("CT_AUTORUN");
            if (string.IsNullOrEmpty(path)) path = Path.Combine(modDir ?? ".", "autorun.txt");
            if (!File.Exists(path)) return;       // the normal case: nothing exists, nothing runs

            GameObject go = new GameObject("ct_autorun");
            UnityEngine.Object.DontDestroyOnLoad(go);
            go.AddComponent<Runner>().Begin(path, log);
        }

        /// <summary>
        /// A MonoBehaviour only because waiting for the scene needs a tick. The wait condition is the
        /// scene itself - rigged renderers present - not a blind timer, because that is the
        /// precondition the swap gates actually share.
        /// </summary>
        private sealed class Runner : MonoBehaviour
        {
            private string[] lines;
            private Action<string> log;
            private int frame, done;
            private float started;
            private bool ran;

            internal void Begin(string path, Action<string> logger)
            {
                log = logger;
                started = Time.realtimeSinceStartup;
                List<string> keep = new List<string>();
                foreach (string raw in File.ReadAllLines(path))
                {
                    string line = raw.Trim();
                    if (line.Length > 0 && !line.StartsWith("#")) keep.Add(line);
                }
                lines = keep.ToArray();
                log("ct_autorun: armed with " + lines.Length + " command(s) from " + path);
            }

            private void Update()
            {
                if (++frame % SettleFrames != 0) return;

                bool timedOut = Time.realtimeSinceStartup - started > (ran ? AsyncTimeoutSeconds : TimeoutSeconds);

                // The list has run; what is left is whatever it started asynchronously. DONE is the
                // marker autogate kills the game on, so it must not appear while a gate is still
                // measuring.
                if (ran)
                {
                    if (AsyncGate.Pending > 0 && !timedOut) return;
                    if (AsyncGate.Pending > 0)
                        log("ct_autorun: " + AsyncGate.Pending + " async gate(s) still running after " +
                            AsyncTimeoutSeconds + "s - their arms did NOT print, treat this run as VOID");
                    log(Marker + " " + done + " command(s)");
                    Destroy(gameObject);
                    return;
                }

                if (!SceneHasRenderers() && !timedOut) return;
                if (timedOut) log("ct_autorun: scene never showed a rigged renderer within " +
                                  TimeoutSeconds + "s - running anyway, gates that need one will refuse by name");

                foreach (string line in lines)
                {
                    // One failing command must not take the rest of the list with it.
                    try { log("ct_autorun > " + line); log(ContentToolMain.RunConsoleLine(line)); }
                    catch (Exception ex) { log("ct_autorun: '" + line + "' THREW " + ex.Message); }
                    done++;
                }
                ran = true;
                started = Time.realtimeSinceStartup;   // the async wait gets its own budget
            }

            private static bool SceneHasRenderers()
            {
                foreach (SkinnedMeshRenderer r in Resources.FindObjectsOfTypeAll<SkinnedMeshRenderer>())
                    if (r.gameObject.scene.IsValid()) return true;
                return false;
            }
        }
    }
}
