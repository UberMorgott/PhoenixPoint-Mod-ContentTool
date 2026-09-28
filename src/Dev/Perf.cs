using System.Diagnostics;

namespace Morgott.ContentTool.Dev
{
    /// <summary>
    /// One `ct_perf` line in Player.log per load stage and per mod: where a slow startup with many content
    /// mods actually spends its time (internal-docs\planning\2026-09-28-continue\PERF.md section 5). Always on,
    /// one Stopwatch and one log line per stage - grep `ct_perf` to read a launch.
    /// </summary>
    internal static class Perf
    {
        internal static Stopwatch Start() { return Stopwatch.StartNew(); }

        /// <param name="mod">the mod id or folder the stage ran for, or null for a whole-roster stage.</param>
        internal static void Line(string stage, string mod, Stopwatch sw)
        {
            ChunkedLog.Say("ct_perf " + stage + (mod == null ? "" : " '" + mod + "'") + " " +
                           sw.ElapsedMilliseconds + " ms");
        }
    }
}
