using System;
using System.IO;

namespace Morgott.ContentTool.Dev
{
    /// <summary>
    /// WHO GETS THE DEV COMMANDS. The probes and gates (ct_seamprobe, ct_texswap, ct_mission, ...) are
    /// instruments for the tool's own development and for autogate, not for a player: several patch a
    /// call every character resolve goes through, and `ct_mission gate` / `ct_music gate` load a save
    /// with no confirmation - the running campaign is simply gone. So they are registered ONLY when an
    /// opt-in marker file sits beside ContentTool.dll, the same shape as PPBridge's `ppcli-enabled`.
    ///
    /// The game console has no hidden or dev-only flag to use instead: ConsoleCommandAttribute carries
    /// Command and Description and nothing else, and every entry of CommandToInfo is listed and
    /// dispatched alike (decompiled Base.Utils.GameConsole\ConsoleCommandAttribute.cs:9-21). Not being
    /// in the table is the only way not to be reachable.
    ///
    /// UnityEngine-free, so the rule is proven offline (gate DG in tests\TargetPathTests).
    /// </summary>
    internal static class DevGate
    {
        /// <summary>The opt-in file, beside ContentTool.dll. Its content is never read.</summary>
        internal const string Marker = "ct-dev";

        /// <summary>True only when the marker is a FILE in the mod folder. A missing folder, an
        /// unreadable one or a directory of that name is not an opt-in.</summary>
        internal static bool Armed(string modDir)
        {
            if (string.IsNullOrEmpty(modDir)) return false;
            try { return File.Exists(Path.Combine(modDir, Marker)); }
            catch (Exception) { return false; }
        }

        /// <summary>The one init line saying which way it went, and how to flip it.</summary>
        internal static string Line(bool armed, int devCommands)
        {
            return armed
                ? "Dev commands: ARMED (" + Marker + " marker present) - " + devCommands + " dev command(s) registered"
                : "Dev commands: not armed - " + devCommands + " dev command(s) not registered (an empty file '" +
                  Marker + "' beside ContentTool.dll arms them at the next launch)";
        }

        /// <summary>What a dev command answers through a path that is not the console (autorun.txt)
        /// while the marker is absent.</summary>
        internal static string Refusal(string command)
        {
            return "ct_autorun: '" + command + "' is a dev command - REFUSED, no '" + Marker +
                   "' marker beside ContentTool.dll";
        }
    }
}
