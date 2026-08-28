using System;
using System.Collections.Generic;

namespace Morgott.ContentTool.Project
{
    /// <summary>
    /// What is applied RIGHT NOW, per mod. The manager's checkbox is a switch at runtime too, not
    /// only at boot: a player who ticks a content mod on mid-session must get its content, and one
    /// who ticks it off must lose it. Both used to be invisible because the in-game check restarted
    /// between toggles.
    ///
    /// This is the bookkeeping half, and it exists so the two paths cannot fight: the startup roster
    /// scan and the per-mod lifecycle hook both go through <see cref="Claim"/>, so whichever runs
    /// first applies and the other is a no-op. <see cref="Release"/> hands back exactly what was
    /// applied, so an undo can be the inverse of an apply rather than a second guess at it.
    ///
    /// UnityEngine-free, like the rest of Project\, so gate G3 measures the transitions offline.
    /// </summary>
    internal static class ContentState
    {
        /// <summary>(mod folder, route) -> what that route is currently serving for that mod. Per
        /// ROUTE, because a mod's sound and its video are applied and undone independently: one is
        /// reversible in-session and the other is not.</summary>
        private static readonly Dictionary<string, List<string>> applied =
            new Dictionary<string, List<string>>(StringComparer.Ordinal);

        private static string Id(string modDir, string route)
        {
            return ModGate.Key(modDir) + "|" + route;
        }

        /// <summary>
        /// Claim one route of one mod as applied. FALSE when it already is - the caller must then do
        /// nothing at all. Re-loading a 24 MB bank or re-registering a key is not free, and a double
        /// apply is exactly what the startup scan plus the runtime hook would otherwise produce for
        /// every mod the player has switched on.
        /// </summary>
        internal static bool Claim(string modDir, string route)
        {
            string id = Id(modDir, route);
            if (string.IsNullOrEmpty(ModGate.Key(modDir)) || applied.ContainsKey(id)) return false;
            applied[id] = new List<string>();
            return true;
        }

        internal static bool Holds(string modDir, string route)
        {
            return applied.ContainsKey(Id(modDir, route));
        }

        /// <summary>
        /// What a route is serving RIGHT NOW - items and the mods they came from. The summary lines
        /// read these instead of counting their own loop, because there are two entry points into
        /// that loop (the startup scan and the runtime toggle) and whichever one did the work, the
        /// other one's local counter stays 0. That is exactly what shipped: nine banks audibly
        /// loaded while the line said "0 shipped replacement bank(s)". A report printed from a
        /// different variable than the one doing the work is a lie waiting for a schedule.
        /// </summary>
        internal static int Items(string route) { return Tally(route, false); }

        internal static int Mods(string route) { return Tally(route, true); }

        private static int Tally(string route, bool mods)
        {
            string suffix = "|" + route;
            int n = 0;
            foreach (KeyValuePair<string, List<string>> e in applied)
                if (e.Key.EndsWith(suffix, StringComparison.Ordinal)) n += mods ? 1 : e.Value.Count;
            return n;
        }

        /// <summary>
        /// WHICH mod's route is already serving <paramref name="what"/>, or null. A route that cannot
        /// evict a standing owner - sound, where a loaded bank is one-way for the session - asks this
        /// before it does the work, so a second claimant is refused BY NAME instead of quietly winning
        /// on load order. The answer is the owning mod's <see cref="ModGate.Key"/>.
        /// </summary>
        internal static string Owner(string route, string what)
        {
            string suffix = "|" + route;
            foreach (KeyValuePair<string, List<string>> e in applied)
                if (e.Key.EndsWith(suffix, StringComparison.Ordinal) && e.Value.Contains(what))
                    return e.Key.Substring(0, e.Key.Length - suffix.Length);
            return null;
        }

        /// <summary>Record one thing that route now serves, so the undo can be its exact inverse.</summary>
        internal static void Served(string modDir, string route, string what)
        {
            List<string> items;
            if (applied.TryGetValue(Id(modDir, route), out items)) items.Add(what);
        }

        /// <summary>
        /// Forget one route of one mod and hand back what it was serving. EMPTY for a route that was
        /// never applied, so an undo of an untouched mod cannot disturb anyone else's rows.
        /// </summary>
        internal static List<string> Release(string modDir, string route)
        {
            string id = Id(modDir, route);
            List<string> items;
            if (!applied.TryGetValue(id, out items)) return new List<string>();
            applied.Remove(id);
            return items;
        }
    }
}
