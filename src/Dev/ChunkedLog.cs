using PhoenixPoint.Modding;

namespace Morgott.ContentTool.Dev
{
    /// <summary>
    /// THE ONLY WAY CONTENTTOOL REACHES A LOG SINK. Not a convenience: one log call is not free.
    ///
    /// The game ships LlockhamIndustries.Misc.DebugManager, which subscribes to
    /// Application.logMessageReceived and writes EVERY message, whole, into ONE UnityEngine.UI.Text
    /// (see <see cref="ConsoleText.MaxLogChars"/> for the arithmetic and the provenance). A Text over
    /// 65000 vertices throws in UpdateGeometry and takes the WHOLE canvas rebuild for that frame with
    /// it - the developer console pane included, which is why a `ct_project` run left the pane blank
    /// while every line it had written was short and legal.
    ///
    /// ModLogger is no escape from that: LogInfo ends in Debug.Log (ModLogger.cs:23) and also writes
    /// one console line per call (ModManager.Console.WriteLineNoLog, ModLogger.cs:22), so both of its
    /// sinks want short calls for the same reason.
    ///
    /// So nothing in this assembly calls UnityEngine.Debug.Log* or ModLogger directly. The instance
    /// wraps the mod's own ModLogger (ContentToolMain.log, prefixes and all); the statics are for the
    /// code that logged straight to UnityEngine.Debug and must keep landing in exactly that sink.
    /// Both go through <see cref="ConsoleText.LogChunks"/>, so no single message can exceed the budget.
    /// </summary>
    internal sealed class ChunkedLog
    {
        /// <summary>Null until the mod is enabled - Logger does not exist before that, and a message
        /// from that window still has to go somewhere, so it takes the plain Debug sink.</summary>
        private readonly ModLogger inner;

        internal ChunkedLog(ModLogger inner) { this.inner = inner; }

        internal void LogInfo(string msg)
        {
            foreach (string part in ConsoleText.LogChunks(msg))
                if (inner != null) inner.LogInfo(part); else UnityEngine.Debug.Log(part);
        }

        internal void LogWarning(string msg)
        {
            foreach (string part in ConsoleText.LogChunks(msg))
                if (inner != null) inner.LogWarning(part); else UnityEngine.Debug.LogWarning(part);
        }

        internal void LogError(string msg)
        {
            foreach (string part in ConsoleText.LogChunks(msg))
                if (inner != null) inner.LogError(part); else UnityEngine.Debug.LogError(part);
        }

        /// <summary>UnityEngine.Debug.Log, bounded. The replacement for a direct call, one for one.</summary>
        internal static void Say(string msg)
        {
            foreach (string part in ConsoleText.LogChunks(msg)) UnityEngine.Debug.Log(part);
        }

        /// <summary>UnityEngine.Debug.LogWarning, bounded.</summary>
        internal static void Warn(string msg)
        {
            foreach (string part in ConsoleText.LogChunks(msg)) UnityEngine.Debug.LogWarning(part);
        }

        /// <summary>UnityEngine.Debug.LogError, bounded.</summary>
        internal static void Fail(string msg)
        {
            foreach (string part in ConsoleText.LogChunks(msg)) UnityEngine.Debug.LogError(part);
        }
    }
}
