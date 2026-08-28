using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Morgott.ContentTool.Project
{
    /// <summary>
    /// How a SHIPPED content mod is found. A player enables a mod in the manager and the content is
    /// there - no console command, no install step. That is the whole contract, and it has exactly two
    /// halves: a folder beside ours under Mods\ that carries the marker file its route reads, and the
    /// mod manager saying the player has it switched ON.
    ///
    /// One enumerator for every route, because there is only one rule. <see cref="Bake.SoundLoad"/>
    /// asks for Dist\Sounds, <see cref="Bake.VideoCatalog"/> asks for ppcontent.json, and neither gets
    /// to invent its own idea of which folders count - that divergence is what let a disabled mod's
    /// music through (2026-08-23, gate G1).
    ///
    /// Deliberately free of UnityEngine and PhoenixPoint types, like <see cref="ModGate"/>, so gate G2
    /// runs offline against real folders on disk instead of only inside a game session.
    /// </summary>
    internal static class ContentMods
    {
        /// <summary>The manifest that makes a folder a content project. Its PRESENCE is the declaration.</summary>
        internal const string Manifest = "ppcontent.json";

        /// <summary>
        /// Every ENABLED mod folder that carries <paramref name="marker"/> (a file or a directory,
        /// relative to the mod folder), with one line per refusal appended to <paramref name="log"/>
        /// - a modder whose content did not appear reads WHY there.
        ///
        /// The gate is asked for every candidate, so a folder on disk is never mistaken for a
        /// player's consent. A null roster means the manager could not be read, and
        /// <see cref="ModGate"/> turns that into a refusal rather than a free pass.
        /// </summary>
        internal static List<string> Enabled(string modDir, string marker,
                                             IDictionary<string, bool> roster,
                                             StringBuilder log, out int skipped)
        {
            List<string> hits = new List<string>();
            skipped = 0;
            HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);

            foreach (string dir in Candidates(modDir, roster))
            {
                if (!seen.Add(ModGate.Key(dir))) continue;
                string at = Path.Combine(dir, marker);
                if (!File.Exists(at) && !Directory.Exists(at)) continue;

                ModVerdict verdict = ModGate.Decide(dir, roster);
                if (verdict != ModVerdict.Apply)
                {
                    skipped++;
                    if (log != null)
                        log.AppendLine("  " + new DirectoryInfo(dir).Name + ": " + ModGate.Why(verdict));
                    continue;
                }
                hits.Add(dir);
            }
            return hits;
        }

        /// <summary>
        /// Must a dependency the activated list does not name stay enabled anyway?
        ///
        /// The bug this answers, measured on a packaged install 2026-08-27: a player subscribes to a
        /// content mod and never hears of ContentTool. The startup pass enables the content mod,
        /// which enables US as its dependency (ModManager.cs:200-207) - and then the SECOND half of
        /// the very same pass switches off everything the stored list does not name
        /// (ModManager.cs:293-299), which is us. TryDisableMod cascade-disables our dependents first
        /// (ModManager.cs:233-240), so the content mod goes down with us: it applies for a moment and
        /// silently reverts. The list is written only by the mod-manager screen
        /// (UIStateModManagment.cs:132 -> StoreEnabledMods), so nothing ever puts a dependency into
        /// it on the player's behalf.
        ///
        /// TRUE means exactly one thing: the player's own list still names an ENABLED mod that
        /// requires <paramref name="candidateId"/>, and does not name the dependency itself. It is
        /// deliberately false when the list names the candidate - a mod the player ticked is a mod
        /// the player may untick, and that disable is his to make. It is also false when the list
        /// could not be read at all (null), because "I don't know what he chose" may never become
        /// "so I'll keep it on".
        /// </summary>
        internal static bool KeepAlive(string candidateId, ICollection<string> activated,
                                       IEnumerable<KeyValuePair<string, bool>> dependents)
        {
            if (string.IsNullOrEmpty(candidateId) || activated == null || dependents == null) return false;
            if (activated.Contains(candidateId)) return false;
            foreach (KeyValuePair<string, bool> d in dependents)
                if (d.Value && !string.IsNullOrEmpty(d.Key) && activated.Contains(d.Key)) return true;
            return false;
        }

        /// <summary>
        /// Where a content mod can live, in the order that decides which spelling of a folder wins.
        ///
        /// A mod installed from the Steam Workshop is NOT beside us: the loader builds its entry from
        /// UgcItemInstallInfo.InstallDir, i.e. workshop\content\839770\&lt;id&gt;
        /// (SteamWorkshopModLoader.cs:21-32). Walking our own parent folder therefore missed every
        /// subscribed content mod - the manager's roster is the only source that knows them, and it
        /// knows the local ones too.
        ///
        /// The sibling walk stays, second and deduped, for the ONE thing the roster cannot express: a
        /// folder carrying content that the manager never discovered (no meta.json). Anything the
        /// manager does know is already in the roster, so this pass can only ever produce a named
        /// REFUSAL, never an extra applied mod - it is a diagnostic, not a second discovery rule.
        /// It runs first only so a local mod keeps the on-disk spelling of its own name in the log
        /// (roster keys are normalised lower-case, see <see cref="ModGate.Key"/>).
        /// </summary>
        private static IEnumerable<string> Candidates(string modDir, IDictionary<string, bool> roster)
        {
            DirectoryInfo mods = string.IsNullOrEmpty(modDir) ? null : Directory.GetParent(modDir);
            if (mods != null && mods.Exists)
                foreach (DirectoryInfo mod in mods.GetDirectories()) yield return mod.FullName;

            if (roster != null)
                foreach (string dir in roster.Keys) yield return dir;
        }

        /// <summary>
        /// The SIBLING mod folder of that name when it carries a manifest, null otherwise. This is
        /// the installed mod: a folder the manager lists, that the player can switch off.
        /// </summary>
        internal static string Sibling(string modDir, string name)
        {
            DirectoryInfo mods = string.IsNullOrEmpty(modDir) ? null : Directory.GetParent(modDir);
            if (mods == null || string.IsNullOrEmpty(name)) return null;
            string at = Path.Combine(mods.FullName, name);
            return File.Exists(Path.Combine(at, Manifest)) ? at : null;
        }

        /// <summary>
        /// A project folder by NAME (never a path - the console's parser eats backslashes, see
        /// ContentToolMain.ProjectDir). The SIBLING mod of that name wins; ContentTool's own
        /// subfolder is the fallback.
        ///
        /// That precedence is the point, not a detail. Every demo is its own mod beside us now, so
        /// `ct_project CustomCreature` has to reach Mods\CustomCreature - and an older copy of the
        /// same project left behind inside Mods\ContentTool\ is a STALE COPY that the mod manager
        /// knows nothing about. Preferring it would apply content the player cannot switch off,
        /// which is gate G1's bug through a different door. Our own projects (Sample, Route7, ...)
        /// have no sibling and are unaffected.
        /// </summary>
        internal static string ProjectDir(string modDir, string name)
        {
            string root = string.IsNullOrEmpty(modDir) ? Directory.GetCurrentDirectory() : modDir;
            string own = Path.Combine(root, string.IsNullOrEmpty(name) ? "Sample" : name);
            return string.IsNullOrEmpty(name) ? own : (Sibling(root, name) ?? own);
        }
    }
}
