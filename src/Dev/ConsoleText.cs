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

        /// <summary>
        /// WHAT ONE Debug.Log CALL MAY CARRY. A second, independent mesh limit, and the one that
        /// actually blanked the pane on 2026-09-10 - bounding the pane could never have closed it.
        ///
        /// The game ships LlockhamIndustries.Misc.DebugManager, which subscribes to
        /// Application.logMessageReceived (DebugManager.cs:37) and pushes EVERY log message, WHOLE,
        /// into ONE UnityEngine.UI.Text (DebugManager.Log :45-65 -> DebugEntry.Update, DebugEntry.cs:39
        /// `text.text = title + " : " + log`). That Text is plain - DebugEntry.cs:16-31 adds a
        /// RectTransform and a Text and nothing else, so no Shadow (x2) or Outline (x5) multiplies it -
        /// which puts it at 4 vertices per character, and VertexHelper.FillMesh throws at 65000: 16250
        /// characters. A Graphic that throws in UpdateGeometry takes the WHOLE
        /// CanvasUpdateRegistry.PerformUpdate batch with it, so every other dirty Text in that frame -
        /// the console pane's own lines included - is left with no geometry. Hence the bench's exact
        /// signature: ONE throw per ct_list run (one giant Debug.Log), not one per line, and a pane
        /// that was handed nothing but short legal lines and still rendered nothing.
        ///
        /// Halved from 16250 for margin: the overlay prefixes "Log : " and the mod loader prefixes
        /// "[Mods] [com.morgott.ContentTool] " before the string is ever measured.
        ///
        /// The pane's own bound above needs no change for this: <see cref="MaxLineChars"/> keeps every
        /// line object at 400 characters = 1600 vertices (3200 with the Default style's Shadow), and
        /// GameConsoleWindow instantiates one Text per line (GameConsoleWindow.cs:259) and keeps 200
        /// of them, so no amount of scrollback ever concentrates in a single mesh.
        /// </summary>
        internal const int MaxLogChars = 8000;

        /// <summary>Room reserved for the "(part i/n)\n" header: 32 covers four-digit counts, and a
        /// message big enough to need five is not a log line, it is the spill file's job.</summary>
        private const int PartHeader = 32;

        /// <summary>
        /// The message in pieces no Debug.Log-driven UI.Text can choke on - the ONE place the
        /// arithmetic lives, so a caller cannot get it wrong by not knowing about it.
        ///
        /// A message that already fits is returned UNCHANGED and alone: the common case is a one-line
        /// verdict and it must stay one line, unadorned. Only a split message is annotated, because a
        /// reader who finds "ct_project: 1 FAILURE(S)" three log calls after its report needs to be
        /// told the two belong together. Chunks end at a line break where one is in reach, so a row
        /// is never cut in half; a single line longer than the budget is cut anyway - there is nowhere
        /// else to cut it.
        ///
        /// Lossless: strip each chunk's first line and the payloads concatenate back to the original.
        /// </summary>
        internal static List<string> LogChunks(string msg)
        {
            string s = msg ?? "";
            var parts = new List<string>();
            if (s.Length <= MaxLogChars) { parts.Add(s); return parts; }

            int budget = MaxLogChars - PartHeader;
            var payloads = new List<string>();
            int at = 0;
            while (at < s.Length)
            {
                int take = Math.Min(budget, s.Length - at);
                if (at + take < s.Length)
                {
                    int nl = s.LastIndexOf('\n', at + take - 1, take);
                    if (nl >= at) take = nl - at + 1;
                }
                payloads.Add(s.Substring(at, take));
                at += take;
            }
            for (int i = 0; i < payloads.Count; i++)
                parts.Add("(part " + (i + 1) + "/" + payloads.Count + ")\n" + payloads[i]);
            return parts;
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
