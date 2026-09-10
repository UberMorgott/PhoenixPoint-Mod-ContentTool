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
            return Bound(msg, out dropped, MaxLines, MaxTotalChars);
        }

        /// <summary>The same bound against a caller-chosen budget. The log sink's budget is not the
        /// pane's: the pane spends one Text per LINE, a log call spends one Text on the WHOLE
        /// message (<see cref="MaxLogChars"/>).</summary>
        internal static List<string> Bound(string msg, out int dropped, int maxLines, int maxTotal)
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
            int budget = maxTotal;
            for (int i = tailFrom; i < lines.Length; i++) budget -= lines[i].Length + 1;

            var shown = new List<string>();
            int n = 0, headMax = Math.Max(0, maxLines - TailLines);
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
        /// WHAT ONE LOG CALL MAY CARRY. A second, independent mesh limit, and the one that blanked the
        /// pane on 2026-09-10 - bounding the pane could never have closed it.
        ///
        /// TWO sinks receive every message, and BOTH are per-CALL, not per-line:
        ///   1. ModLogger.LogInfo (ModLogger.cs:21-24) hands the message to
        ///      ModManager.Console.WriteLineNoLog = GameConsoleWindow.WriteLineNoLog
        ///      (GameConsoleWindow.cs:238-253), which instantiates ONE UnityEngine.UI.Text from the
        ///      ConsoleLine prefab and puts the WHOLE message in it. ApplyConsoleLineStyle
        ///      (GameConsoleWindow.cs:603-614) enables that prefab's Shadow, and a Shadow doubles the
        ///      mesh - so ~8 vertices per character, and VertexHelper.FillMesh throws at 65000:
        ///      ~8125 characters per CALL.
        ///   2. LlockhamIndustries.Misc.DebugManager subscribes to Application.logMessageReceived
        ///      (DebugManager.cs:37) and pushes the message WHOLE into ONE plain Text shared by every
        ///      message of that LogType (DebugManager.Log :45-65 -> DebugEntry.Update, DebugEntry.cs:39
        ///      `text.text = title + " : " + log`). REPLACE, not append: it holds the LAST message, so
        ///      it is per-call too, at 4 vertices per character = 16250.
        ///
        /// Neither accumulates, which is why chunking one report into 7 calls of 8000 did NOT help:
        /// the bench of 2026-09-11 (2e2c39f) logged 5 parts of >=7846 characters in one frame and got
        /// exactly 5 "Mesh can not have more than 65000 vertices" - one per over-budget CALL, each its
        /// own console-line Text, all in the same CanvasUpdateRegistry.PerformUpdate. The 4928-character
        /// part threw nothing. Sink 1 is the tighter of the two and the one that scales with call count.
        ///
        /// 5000 = 40000 vertices with the Shadow, 62% of the limit: room for the loader's
        /// "[Mods] [com.morgott.ContentTool] " prefix, the overlay's "Log : ", and rich-text tags.
        /// A command may spend ONE such call - see <see cref="OneLogCall"/>; the rest goes to a file.
        ///
        /// The pane's own bound above needs no change for this: <see cref="MaxLineChars"/> keeps every
        /// line object at 400 characters = 1600 vertices (3200 with the Shadow), and
        /// GameConsoleWindow instantiates one Text per line (GameConsoleWindow.cs:259) and keeps 200
        /// of them, so no amount of scrollback ever concentrates in a single mesh.
        /// </summary>
        internal const int MaxLogChars = 5000;

        /// <summary>Room kept inside <see cref="MaxLogChars"/> for the spill trailer the caller adds
        /// after <see cref="OneLogCall"/>, so the bounded text plus that line is still one call.</summary>
        internal const int TrailerRoom = 256;

        /// <summary>
        /// A WHOLE COMMAND'S OUTPUT AS ONE LOG CALL. Volume per command, not per message, is the thing
        /// the sinks above actually charge for: N over-budget calls are N throwing Graphics in one
        /// frame, and one of them aborting the batch is what leaves the pane with nothing.
        ///
        /// Same shape as the pane's bound - head, "... N line(s) not shown", the reserved tail - just
        /// against the log budget. <paramref name="spill"/> is true when the caller MUST write the
        /// whole text to a file and name it: the middle this drops exists nowhere else.
        /// </summary>
        internal static string OneLogCall(string msg, out bool spill)
        {
            int dropped;
            List<string> lines = Bound(msg, out dropped, MaxLines, MaxLogChars - TrailerRoom);
            spill = dropped > 0 || Clipped(msg);
            return string.Join("\n", lines.ToArray());
        }

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
