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

        /// <summary>The mesh extensions Content\Meshes\ accepts, as GetFiles patterns - the list
        /// ContentProject.Sources scans that folder with. It lives HERE, beside the manifest's own name and
        /// UnityEngine-free, because ProjectScaffold has to refuse a same-stem file under any of the OTHERS
        /// before it ships one, and a second spelling there would quietly stop guarding the day a format is
        /// added. ContentProject.cs itself cannot join the offline compile list (see ObjCodecTests.csproj).</summary>
        internal static readonly string[] MeshPatterns = { "*.obj", "*.glb" };

        /// <summary>The texture extensions Content\Textures\ accepts, the list ContentProject.Load scans that
        /// folder with (:398). Here for the same reason as <see cref="MeshPatterns"/>: the Validate stage has
        /// to answer "is this row's file where the bake will look for it" without ContentProject, which cannot
        /// join the offline compile list.</summary>
        internal static readonly string[] TexturePatterns = { "*.png", "*.jpg", "*.jpeg" };

        /// <summary>
        /// THE ONE ENUMERATOR of <c>Content\folder\</c> - what the BAKE imports (ContentProject.Load hands
        /// its refusal list in) and what the Validate stage answers a row from, so the two stages cannot
        /// disagree about which files are there. Top level only, with the EXTENSION re-checked because NTFS
        /// matches a search pattern against the 8.3 SHORT name too ("*.glb" also answers body.glbx).
        ///
        /// A record is named by its file STEM, so two files that differ only in extension (swatch.png next
        /// to swatch.jpg) would both answer to "swatch" and the first one found would win silently. That is
        /// refused, by name: EVERY file of a colliding stem is left out (choosing one is exactly what this
        /// refuses to do) and the other stems are untouched. A null <paramref name="refusals"/> keeps the
        /// throw, for a caller with no refusal channel to write into.
        /// </summary>
        internal static string[] Sources(string root, string folder, List<string> refusals,
                                         params string[] patterns)
        {
            List<string> files = Listed(root, folder, patterns);
            files.Sort(StringComparer.OrdinalIgnoreCase);
            List<string> kept = new List<string>();
            // Sorted, so a stem's files are ADJACENT: '.' is lower than any character a stem may continue
            // with, which is the same assumption the pairwise walk this replaced already made.
            for (int i = 0; i < files.Count;)
            {
                int j = i + 1;
                while (j < files.Count && SameStem(files[j], files[i])) j++;
                if (j - i == 1) kept.Add(files[i]);
                else
                {
                    string why = Collision(folder, files[i], files[i + 1]);
                    if (refusals == null) throw new InvalidDataException(why);
                    refusals.Add(why);
                }
                i = j;
            }
            return kept.ToArray();
        }

        /// <summary>
        /// The file in <c>Content\folder\</c> whose STEM is <paramref name="stem"/> - what a "replace" row
        /// names - or null when the bake will find nothing there. <paramref name="refusal"/> carries the
        /// collision sentence when the stem is answered by more than one file: the bake drops both, so a
        /// Validate that only reported "not a .png/.jpg under Content\Textures\" described a file the author
        /// can SEE sitting there (swatch.png + swatch.jpg - PASSed Validate, P1 REFUSED at bake).
        /// </summary>
        internal static string SourceFile(string root, string folder, string stem, string[] patterns,
                                          out string refusal)
        {
            refusal = null;
            if (string.IsNullOrEmpty(root) || string.IsNullOrEmpty(stem)) return null;
            List<string> hits = Listed(root, folder, patterns);
            for (int i = hits.Count - 1; i >= 0; i--)
                if (!string.Equals(Path.GetFileNameWithoutExtension(hits[i]), stem,
                                   StringComparison.OrdinalIgnoreCase))
                    hits.RemoveAt(i);
            hits.Sort(StringComparer.OrdinalIgnoreCase);
            if (hits.Count == 1) return hits[0];
            if (hits.Count > 1) refusal = Collision(folder, hits[0], hits[1]);
            return null;
        }

        /// <summary>The refusal both stages say, in one wording. Its subject is the STEM, not the project:
        /// the colliding files are skipped and everything else in the folder bakes.</summary>
        internal static string Collision(string folder, string a, string b)
        {
            return "Content\\" + folder + "\\ holds two files with the same name: " +
                   Path.GetFileName(a) + " and " + Path.GetFileName(b) +
                   " - a replacement names the stem, so one of them has to go; BOTH were " +
                   "SKIPPED, the project's other sources are unaffected";
        }

        /// <summary>Every file at the top of <c>Content\folder\</c> that one of <paramref name="patterns"/>
        /// really matches - the extension is re-checked, see <see cref="Sources"/>.</summary>
        private static List<string> Listed(string root, string folder, string[] patterns)
        {
            List<string> files = new List<string>();
            if (string.IsNullOrEmpty(root)) return files;
            string dir = Path.Combine(Path.Combine(root, "Content"), folder);
            if (!Directory.Exists(dir)) return files;
            foreach (string pattern in patterns)
                foreach (string f in Directory.GetFiles(dir, pattern))
                    if (string.Equals(Path.GetExtension(f), pattern.Substring(1),
                                      StringComparison.OrdinalIgnoreCase))
                        files.Add(f);
            return files;
        }

        private static bool SameStem(string a, string b)
        {
            return string.Equals(Path.GetFileNameWithoutExtension(a), Path.GetFileNameWithoutExtension(b),
                                 StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// " - the file IS in the project, at Content\...; move it into Content\folder\ and bake again",
        /// or "" when it is nowhere. THE one copy of that sentence: ProjectBake's P1/P4 refusals and the
        /// Validate stage both say it, and the misplaced-texture case (2026-09-06:
        /// Content\Meshes\materials\RR_soldier_albedo.png) is the one an author most needs it for.
        ///
        /// ONLY A FILE THE DESTINATION WOULD ACCEPT. Matching on the stem alone told the author to move
        /// Content\Meshes\body.glb into Content\Textures\, where the bake would then ignore it - advice
        /// that costs a second failed bake. <paramref name="patterns"/> is the destination folder's own
        /// list, so the namesake named here is one that will actually be imported once it is moved.
        /// </summary>
        internal static string Elsewhere(string root, string stem, string folder, string[] patterns)
        {
            string content = Path.Combine(root, "Content");
            if (string.IsNullOrEmpty(stem) || !Directory.Exists(content)) return "";
            string want = Path.Combine(content, folder);
            // Enumerate, not GetFiles: the FIRST namesake ends the walk, and this runs on a refusal path
            // where the whole tree would otherwise be materialised for one row.
            foreach (string pattern in patterns)
                foreach (string f in Directory.EnumerateFiles(content, pattern, SearchOption.AllDirectories))
                {
                    string dir = Path.GetDirectoryName(f);
                    if (string.Equals(dir, want, StringComparison.OrdinalIgnoreCase)) continue;
                    if (!string.Equals(Path.GetExtension(f), pattern.Substring(1),
                                       StringComparison.OrdinalIgnoreCase)) continue;
                    if (!string.Equals(Path.GetFileNameWithoutExtension(f), stem,
                                       StringComparison.OrdinalIgnoreCase)) continue;
                    // A namesake sitting directly in Content\ has no sub-path at all, and the empty
                    // Substring rendered it as "Content\\name.png" - a path with a doubled separator that
                    // is not where the file is.
                    string under = dir.Substring(content.Length).Trim('\\', '/');
                    return " - the file IS in the project, at Content\\" +
                           (under.Length == 0 ? "" : under + "\\") + Path.GetFileName(f) +
                           "; move it into Content\\" + folder + "\\ and bake again";
                }
            return "";
        }

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
        internal static IEnumerable<string> Candidates(string modDir, IDictionary<string, bool> roster)
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
