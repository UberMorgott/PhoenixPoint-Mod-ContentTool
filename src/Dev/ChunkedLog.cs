using System.Collections.Generic;
using PhoenixPoint.Modding;

namespace Morgott.ContentTool.Dev
{
    /// <summary>
    /// THE ONLY WAY CONTENTTOOL REACHES A LOG SINK. Not a convenience: one log call is not free.
    ///
    /// Both of ModLogger's sinks put a WHOLE message into ONE UnityEngine.UI.Text - the console line
    /// it instantiates (ModLogger.cs:22 -> GameConsoleWindow.cs:238-253, with a Shadow doubling the
    /// mesh) and the game's log overlay (ModLogger.cs:23 -> Debug.Log -> DebugManager.cs:37 ->
    /// DebugEntry.cs:39). A Text over 65000 vertices throws in UpdateGeometry and takes the whole
    /// canvas rebuild for that frame with it - the developer console pane included, which is why a
    /// `ct_project` run left the pane blank while every line it had written was short and legal.
    /// <see cref="ConsoleText.MaxLogChars"/> carries the arithmetic and the bench provenance.
    ///
    /// Neither sink accumulates, so splitting a report into N legal calls does not help: N over-budget
    /// calls are N throwing Graphics in the same frame. What is bounded here is therefore the whole
    /// message, ONCE - <see cref="ConsoleText.OneLogCall"/> - with the untruncated text spilled to a
    /// file the log names. <see cref="ConsoleText.LogChunks"/> stays as the safety net for the one case
    /// that has nowhere else to put the text: the spill file could not be written.
    ///
    /// So nothing in this assembly calls UnityEngine.Debug.Log* or ModLogger directly. The instance
    /// wraps the mod's own ModLogger (ContentToolMain.log, prefixes and all); the statics are for the
    /// code that logged straight to UnityEngine.Debug and must keep landing in exactly that sink.
    /// </summary>
    internal sealed class ChunkedLog
    {
        /// <summary>What actually reaches a sink: one call for a message that fits, one bounded call
        /// plus a spill file for one that does not, and only if that file could not be written, the
        /// lossless split.</summary>
        private static List<string> Bound(string msg)
        {
            bool spill;
            string one = ConsoleText.OneLogCall(msg, out spill);
            if (!spill) return new List<string> { one };
            string path = ContentToolMain.Spill("log", msg);
            if (path == null) return ConsoleText.LogChunks(msg);
            return new List<string> { one + "\n... the whole message is in " + path };
        }

        /// <summary>Null until the mod is enabled - Logger does not exist before that, and a message
        /// from that window still has to go somewhere, so it takes the plain Debug sink.</summary>
        private readonly ModLogger inner;

        internal ChunkedLog(ModLogger inner) { this.inner = inner; }

        internal void LogInfo(string msg)
        {
            foreach (string part in Bound(msg))
                if (inner != null) inner.LogInfo(part); else UnityEngine.Debug.Log(part);
        }

        internal void LogWarning(string msg)
        {
            foreach (string part in Bound(msg))
                if (inner != null) inner.LogWarning(part); else UnityEngine.Debug.LogWarning(part);
        }

        internal void LogError(string msg)
        {
            foreach (string part in Bound(msg))
                if (inner != null) inner.LogError(part); else UnityEngine.Debug.LogError(part);
        }

        /// <summary>UnityEngine.Debug.Log, bounded. The replacement for a direct call, one for one.</summary>
        internal static void Say(string msg)
        {
            foreach (string part in Bound(msg)) UnityEngine.Debug.Log(part);
        }

        /// <summary>UnityEngine.Debug.LogWarning, bounded.</summary>
        internal static void Warn(string msg)
        {
            foreach (string part in Bound(msg)) UnityEngine.Debug.LogWarning(part);
        }

        /// <summary>UnityEngine.Debug.LogError, bounded.</summary>
        internal static void Fail(string msg)
        {
            foreach (string part in Bound(msg)) UnityEngine.Debug.LogError(part);
        }
    }
}
