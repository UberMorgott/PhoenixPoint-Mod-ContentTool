using System;
using System.Collections.Generic;

namespace Morgott.ContentTool.Dev
{
    /// <summary>
    /// WHAT THE GAME'S CONSOLE PANE MAY BE HANDED, and nothing else. Deliberately free of UnityEngine
    /// types (the Unity half is ContentToolMain.ConsoleBridge), so the bound is measured offline in
    /// tests\TargetPathTests instead of by blanking a console.
    ///
    /// GameConsoleWindow.WriteLineWithColor instantiates one UnityEngine.UI.Text per call
    /// (GameConsoleWindow.cs:255-271) and keeps 200 of them; a UI.Text mesh dies at 65000 vertices
    /// (~4 per glyph) and a Text that throws in UpdateGeometry takes the whole canvas rebuild with it,
    /// which is why the pane goes BLANK rather than showing a broken line. Per-line writes alone were
    /// not enough: `ct_project` prints thousands of lines and the pane died anyway, so what one command
    /// may add is capped in TOTAL as well - <see cref="MaxTotalChars"/> is ~3000 glyph-quads, far under
    /// the limit even before the console's own backlog is counted.
    ///
    /// The last lines are RESERVED rather than trimmed with the rest: every ct_ command's verdict is
    /// its LAST line ("ct_project: ALL PASS - ..."), so a head-only bound drops the one line the author
    /// ran the command for. The whole text is never lost - the caller spills it to a file and says
    /// where - so nothing here has to be clever about which middle lines matter.
    /// </summary>
    internal static class ConsoleText
    {
        internal const int MaxLines = 60;
        internal const int MaxLineChars = 400;
        internal const int MaxTotalChars = 12000;
        /// <summary>How many lines off the END are always kept: the verdict, and room for the blank
        /// line and the closing note a few reports put under it.</summary>
        internal const int TailLines = 3;

        /// <summary>
        /// The lines to write, in order, with a "not shown" marker already in place where the middle
        /// was dropped. <paramref name="dropped"/> is how many lines that marker stands for - 0 means
        /// the pane is getting the whole text and there is nothing to spill.
        /// </summary>
        internal static List<string> Bound(string msg, out int dropped)
        {
            string[] raw = (msg ?? "").Split('\n');
            var lines = new string[raw.Length];
            for (int i = 0; i < raw.Length; i++)
            {
                string line = raw[i].TrimEnd('\r');
                lines[i] = line.Length > MaxLineChars
                    ? line.Substring(0, MaxLineChars) + " ...(clipped)" : line;
            }

            int tailFrom = Math.Max(0, lines.Length - TailLines);
            int budget = MaxTotalChars;
            for (int i = tailFrom; i < lines.Length; i++) budget -= lines[i].Length + 1;

            var shown = new List<string>();
            int n = 0, headMax = Math.Max(0, MaxLines - TailLines);
            while (n < tailFrom && n < headMax && lines[n].Length + 1 <= budget)
            {
                budget -= lines[n].Length + 1;
                shown.Add(lines[n]);
                n++;
            }
            dropped = tailFrom - n;
            // A line was CLIPPED but no line was dropped: the text is still not what the author wrote,
            // so the spill has to happen anyway. Reported as one dropped "line" would be a lie; the
            // caller asks Clipped() instead.
            if (dropped > 0) shown.Add("... " + dropped + " line(s) not shown");
            for (int i = tailFrom; i < lines.Length; i++) shown.Add(lines[i]);
            return shown;
        }

        /// <summary>Did any single line have to be cut to <see cref="MaxLineChars"/>? A run that drops
        /// no line can still have lost the tail of one.</summary>
        internal static bool Clipped(string msg)
        {
            foreach (string line in (msg ?? "").Split('\n'))
                if (line.TrimEnd('\r').Length > MaxLineChars) return true;
            return false;
        }
    }
}
