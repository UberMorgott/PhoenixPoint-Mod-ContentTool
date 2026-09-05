using System;
using System.Collections.Generic;
using System.IO;

namespace Morgott.ContentTool.Bake
{
    /// <summary>
    /// ONE OWNER PER CANONICAL OUTPUT DIRECTORY, process-wide.
    ///
    /// FAIL FAST: a second producer for a directory already in flight is refused (R37) and writes nothing.
    /// It never waits, never retries, never steals - a queue here would be a lock the mod-manager checkbox
    /// blocks the UI thread on, and a retry would be two runs interleaving over the same bytes anyway.
    ///
    /// WHY IT IS NOT IN THE PANEL. The panel's own ownership stops the panel's buttons and nothing else:
    /// the console verb (ContentToolMain.cs:480) and the mod-manager checkbox (Route7.cs:341) reach the same
    /// output directory without ever asking it, so two runs could interleave and leave one run's freshness
    /// key stamped over the other run's copies. The guard therefore sits where all three already pass -
    /// ProjectBake's own body - and this class is what it asks.
    ///
    /// THE PAIR IS TAKEN ATOMICALLY. A bake owns two directories (the patched copies in AppData and the
    /// project's own Dist) and takes them under ONE lock acquisition: taken one at a time, two runs could
    /// each hold one and refuse each other forever, and since nothing here waits or retries that deadlock
    /// would never resolve.
    ///
    /// UnityEngine-free on purpose - the caller derives the paths (PatchedDir needs Application.
    /// persistentDataPath) and hands them in as strings, so the offline gate can link this file.
    /// </summary>
    internal static class OutputClaim
    {
        /// <summary>ponytail: one flat set under one lock. There is no per-directory lock object and no
        /// fairness policy, because the answer to contention is a refusal, not a wait.</summary>
        private static readonly HashSet<string> InFlight =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>One spelling of a directory, so two callers naming the same folder collide.
        /// OrdinalIgnoreCase plus a trimmed trailing separator is the case-blindness Route7.cs:287 already
        /// applies to these very paths; GetFullPath normalises '/' and any '..'. A path GetFullPath refuses
        /// (an invalid character, an empty segment) is still claimable under its own literal text - a claim
        /// nobody else can name is harmless, and throwing here would turn a bad project into a crash.</summary>
        internal static string Canonical(string dir)
        {
            if (string.IsNullOrEmpty(dir)) return null;
            try { return Path.GetFullPath(dir).TrimEnd('\\', '/'); }
            catch (Exception) { return dir.Replace('/', '\\').TrimEnd('\\'); }
        }

        /// <summary>Take EVERY directory in <paramref name="dirs"/>, or none of them. Returns false with
        /// <paramref name="refusal"/> = R37 naming the first contended directory; true with a null refusal
        /// when the whole set is now owned by this caller, who must <see cref="Release"/> it in a finally.</summary>
        internal static bool Take(string[] dirs, out string refusal)
        {
            refusal = null;
            List<string> want = Wanted(dirs);
            lock (InFlight)
            {
                foreach (string c in want)
                    if (InFlight.Contains(c)) { refusal = StageText.R37(c); return false; }
                foreach (string c in want) InFlight.Add(c);
            }
            return true;
        }

        /// <summary>Releases what <see cref="Take"/> took. Idempotent: a second release is not a leak and
        /// not an error, because the `finally` that calls it cannot know whether an earlier one ran.</summary>
        internal static void Release(string[] dirs)
        {
            List<string> want = Wanted(dirs);
            lock (InFlight) foreach (string c in want) InFlight.Remove(c);
        }

        internal static bool Held(string dir)
        {
            string c = Canonical(dir);
            if (c == null) return false;
            lock (InFlight) return InFlight.Contains(c);
        }

        /// <summary>Canonical, de-duplicated, in the caller's order - so the refusal names the directory a
        /// reader can find, and a pair that happens to be one folder is taken and released once.</summary>
        private static List<string> Wanted(string[] dirs)
        {
            List<string> want = new List<string>();
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (dirs != null)
                foreach (string d in dirs)
                {
                    string c = Canonical(d);
                    if (c != null && seen.Add(c)) want.Add(c);
                }
            return want;
        }
    }
}
