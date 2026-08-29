using System;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Morgott.ContentTool.Dev
{
    /// <summary>
    /// The Unity half of the developer live loop (S2) - `ct_dev`. It owns the three things a
    /// FileSystemWatcher must never touch: the coroutine that drains the dirty set on the MAIN
    /// thread, the hotkey poll, and the re-apply itself. Every decision it acts on comes from
    /// <see cref="DevLoop"/>, which is Unity-free and proven offline.
    ///
    /// DEVELOPER MODE IS EXPLICIT AND OFF BY DEFAULT. It is turned on by ONE console verb,
    /// `ct_dev on`, and by nothing else. There is deliberately no manifest flag: a flag inside a
    /// shipped package is a switch a player can end up carrying, and the mod already has the right
    /// place for "run this at launch on MY machine" - <see cref="AutoRun"/>'s autorun.txt, which is
    /// dev-only by construction (no bake writes one, so a released mod has none). A modder who wants
    /// the loop from the first frame puts the line `ct_dev on &lt;project&gt;` in that file.
    ///
    /// With dev mode off this class allocates nothing: no GameObject, no coroutine, no watcher, no
    /// Input poll. <see cref="DevLoop.Scheduled"/> is the falsifiable statement of that.
    /// </summary>
    internal static class DevRunner
    {
        /// <summary>RR's own key for the same action (Resource_Replacer.cs:36, :539-540).</summary>
        private const KeyCode Hotkey = KeyCode.F12;

        /// <summary>How many lost bindings one periodic pass may re-apply; the rest wait a tick.</summary>
        private const int ScanBudget = 4;

        private static GameObject go;
        private static bool hotkeysBroken;

        internal static string Run(string[] args)
        {
            string cmd = args != null && args.Length > 0 ? args[0].ToLowerInvariant() : "status";
            switch (cmd)
            {
                case "on": return On(args != null && args.Length > 1 ? args[1] : null);
                case "off": return Off();
                case "status": return Status();
                case "sets": return "ct_dev sets: " + string.Join(", ", DevLoop.Sets().ToArray()) +
                                    " | active '" + DevLoop.ActiveSet + "'";
                case "next": return Switch(DevLoop.Next());
                case "set":
                    if (args.Length < 2) return "usage: ct_dev set <name>   (" + string.Join(", ", DevLoop.Sets().ToArray()) + ")";
                    string why;
                    if (!DevLoop.Select(args[1], out why)) return "ct_dev REFUSED: " + why;
                    return Switch(DevLoop.ActiveSet);
                case "reload": return DevLoop.Enabled
                    ? (SeamSwap.ReapplyAll() ?? "ct_dev reload: no file-backed binding to re-apply")
                    : "ct_dev is OFF - run 'ct_dev on' first";
                default: return "usage: ct_dev [on [project] | off | status | sets | set <name> | next | reload]";
            }
        }

        private static string On(string project)
        {
            // A NAME, never a path - the console's parser eats backslashes (ContentToolMain.ProjectDir).
            string root = project == null ? ContentToolMain.ModDir : ContentToolMain.ProjectDir(project);
            // BEFORE On(): it bakes DevLoop.Scheduled into the line it returns, so flags set after the
            // call would make that line say "loop=off hotkey=off" about a loop that is on. Cleared
            // again if it refuses, so gate S2's "off costs nothing" still reads all-zero.
            DevLoop.LoopOn = true;
            DevLoop.HotkeyOn = true;
            string started = DevLoop.On(root, ContentToolMain.Say);
            if (!DevLoop.Enabled)
            {
                DevLoop.LoopOn = false;
                DevLoop.HotkeyOn = false;
                return started;
            }

            if (go == null)
            {
                go = new GameObject("ct_dev");
                UnityEngine.Object.DontDestroyOnLoad(go);
                go.AddComponent<Runner>();
            }
            hotkeysBroken = false;
            return started + "\n  edit a file under that folder and it is re-read and re-applied live; " +
                   Hotkey + " cycles the variant set";
        }

        private static string Off()
        {
            if (go != null) { UnityEngine.Object.Destroy(go); go = null; }
            DevLoop.LoopOn = false;
            DevLoop.HotkeyOn = false;
            return DevLoop.Off();
        }

        private static string Status()
        {
            return "ct_dev is " + (DevLoop.Enabled ? "ON, watching " + DevLoop.Root : "OFF") +
                   " | " + DevLoop.Scheduled +
                   " | set '" + DevLoop.ActiveSet + "' of " + string.Join(", ", DevLoop.Sets().ToArray()) +
                   " | bindings=" + SeamSwap.MarkCount;
        }

        /// <summary>A set switch is a re-resolve of every file-backed binding, not a new mechanism.</summary>
        private static string Switch(string set)
        {
            string r = SeamSwap.ReapplyAll();
            return "ct_dev set '" + set + "'" + (r == null ? " (no file-backed binding to re-apply)" : "\n  " + r);
        }

        /// <summary>
        /// The main-thread half. Watcher threads only ever mark; every Unity call is here.
        /// </summary>
        private sealed class Runner : MonoBehaviour
        {
            private string scene = "";

            private void Start()
            {
                StartCoroutine(Pump());
            }

            private void OnDestroy()
            {
                DevLoop.LoopOn = false;
                DevLoop.HotkeyOn = false;
            }

            private IEnumerator Pump()
            {
                while (true)
                {
                    yield return null;
                    if (!DevLoop.Enabled) continue;

                    try
                    {
                        // A scene load releases and rebuilds the objects every binding points at, so
                        // it is the one moment discovery must not wait for the next interval.
                        string now = SceneManager.GetActiveScene().name;
                        if (now != scene) { scene = now; DevLoop.ForceScan(); }

                        System.Collections.Generic.List<string> changed = DevLoop.Pump(DateTime.UtcNow);
                        if (changed != null)
                        {
                            StringBuilder b = new StringBuilder();
                            b.Append("ct_dev: ").Append(changed.Count).Append(" file(s) changed");
                            string r = SeamSwap.Reapply(changed);
                            if (r != null) b.Append(" -> ").Append(r);
                            ContentToolMain.Say(b.ToString());
                        }

                        if (DevLoop.DueScan(DateTime.UtcNow))
                        {
                            string r = SeamSwap.Rescan(ScanBudget);
                            if (r != null) ContentToolMain.Say(r);
                        }
                    }
                    catch (Exception ex)
                    {
                        // One bad frame must not stop the loop, and it must not spam either.
                        ContentToolMain.Say("ct_dev: tick failed - " + ex.Message);
                    }
                }
            }

            private void Update()
            {
                if (hotkeysBroken || !DevLoop.Enabled) return;
                try
                {
                    // The same guard the fit workbench keeps, in the other place this mod reads a raw
                    // key: a key the GAME binds fires the game's action too, and the user pressed one
                    // key. F12 is nobody's, so this is a no-op today - it is here so it stays one.
                    if (BenchList.IsGameOwned(Hotkey.ToString())) return;
                    if (Input.GetKeyDown(Hotkey)) ContentToolMain.Say(Switch(DevLoop.Next()));
                }
                catch (Exception ex)
                {
                    // A build with legacy Input disabled would otherwise throw once per frame forever
                    // (RR hit exactly this, Resource_Replacer.cs:2075-2080).
                    hotkeysBroken = true;
                    DevLoop.HotkeyOn = false;
                    ContentToolMain.Say("ct_dev: the hotkey is unavailable, this build has legacy Input " +
                                        "disabled - use 'ct_dev next' (" + ex.Message + ")");
                }
            }
        }
    }
}
