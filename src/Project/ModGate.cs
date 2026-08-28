using System;
using System.Collections.Generic;
using System.IO;

namespace Morgott.ContentTool.Project
{
    /// <summary>What the game's own mod manager says about the folder a piece of content sits in.</summary>
    internal enum ModVerdict
    {
        /// <summary>The manager discovered this folder as a mod and the player has it switched ON.</summary>
        Apply,
        /// <summary>The manager knows it and the player has it switched OFF.</summary>
        Disabled,
        /// <summary>No mod was ever discovered here - no meta.json, so the manager cannot know it.</summary>
        Unknown,
        /// <summary>The manager could not be read at all. Nothing is applied; see below.</summary>
        NoRoster
    }

    /// <summary>
    /// The ONE gate every cross-mod discovery routes through. ContentTool used to walk the Mods FOLDER
    /// and apply whatever it found there, which meant content from a mod the player had switched OFF
    /// still reached the game (a disabled demo's menu music played, 2026-08-23). A folder on disk is
    /// not a player's consent; the mod manager's own state is.
    ///
    /// The roster comes from <see cref="ModRoster.Build"/> (ModManager.Mods -> ModEntry.Enabled). This
    /// half is deliberately free of UnityEngine and PhoenixPoint types so gate G1 runs offline.
    ///
    /// A missing roster is REFUSAL, never "apply everything": falling back to the old behaviour is
    /// exactly the bug, so an unreadable manager loses content loudly instead of restoring it.
    /// </summary>
    internal static class ModGate
    {
        internal static ModVerdict Decide(string modFolder, IDictionary<string, bool> roster)
        {
            if (roster == null || roster.Count == 0) return ModVerdict.NoRoster;
            bool enabled;
            if (!roster.TryGetValue(Key(modFolder), out enabled)) return ModVerdict.Unknown;
            return enabled ? ModVerdict.Apply : ModVerdict.Disabled;
        }

        /// <summary>
        /// One spelling for one folder: the roster is keyed on ModEntry.Directory and looked up with a
        /// DirectoryInfo.FullName, which differ in trailing separator and in case on Windows.
        /// </summary>
        internal static string Key(string dir)
        {
            if (string.IsNullOrEmpty(dir)) return "";
            string full;
            try { full = Path.GetFullPath(dir); }
            catch (Exception) { full = dir; }
            return full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                       .ToLowerInvariant();
        }

        /// <summary>The reason, in the log, on the line the modder will actually read.</summary>
        internal static string Why(ModVerdict v)
        {
            switch (v)
            {
                case ModVerdict.Disabled: return "skipped, disabled in the mod manager";
                case ModVerdict.Unknown: return "skipped, the mod manager never discovered it (no meta.json)";
                case ModVerdict.NoRoster: return "skipped, the mod manager could not be read";
                default: return "applied";
            }
        }
    }
}
