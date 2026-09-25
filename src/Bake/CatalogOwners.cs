using System;
using System.Collections.Generic;

namespace Morgott.ContentTool.Bake
{
    /// <summary>
    /// WHO serves each live video key (<see cref="CatalogLive"/>). Two mods may name the same
    /// RuntimeKey - two replacements for one shipped cutscene is the ordinary case - and before this
    /// the last to register silently won, and the first to unregister handed the shipped row back
    /// under the other one's feet.
    ///
    /// Every mod that wants a key is REMEMBERED, not only the one serving it: the lower mod id
    /// serves (<see cref="BundleClaims.Keeps"/>, the one ownership policy every route shares), and
    /// when it lets go the next-lowest takes over instead of the key falling back to the game while
    /// a mod that still wants it is switched on.
    ///
    /// UnityEngine-free on purpose, so the policy is measured offline; the catalog surgery that acts
    /// on its answers stays in <see cref="CatalogLive"/>.
    /// </summary>
    internal sealed class CatalogOwners
    {
        /// <summary>RuntimeKey -> (owner -> the path that owner wants served).</summary>
        private readonly Dictionary<string, Dictionary<string, string>> wants =
            new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);

        /// <summary>
        /// Record that <paramref name="owner"/> wants <paramref name="key"/> served from
        /// <paramref name="path"/> - a second call by the same owner moves its path. Returns the owner
        /// that serves the key afterwards, which is <paramref name="owner"/> unless a lower id holds it.
        /// </summary>
        internal string Want(string key, string owner, string path)
        {
            Dictionary<string, string> byOwner;
            if (!wants.TryGetValue(key, out byOwner))
            {
                byOwner = new Dictionary<string, string>(StringComparer.Ordinal);
                wants[key] = byOwner;
            }
            byOwner[owner ?? ""] = path;
            return Serving(key);
        }

        /// <summary>
        /// Forget what <paramref name="owner"/> wanted for <paramref name="key"/>. FALSE when it
        /// wanted nothing there, so an undo of an owner that never registered cannot disturb anyone.
        /// </summary>
        internal bool Drop(string key, string owner)
        {
            Dictionary<string, string> byOwner;
            if (!wants.TryGetValue(key, out byOwner) || !byOwner.Remove(owner ?? "")) return false;
            if (byOwner.Count == 0) wants.Remove(key);
            return true;
        }

        /// <summary>Does anyone still want this key?</summary>
        internal bool Wanted(string key)
        {
            return wants.ContainsKey(key);
        }

        /// <summary>The owner serving <paramref name="key"/> - the lowest id that wants it - or null.</summary>
        internal string Serving(string key)
        {
            Dictionary<string, string> byOwner;
            if (!wants.TryGetValue(key, out byOwner)) return null;
            string best = null;
            foreach (string o in byOwner.Keys)
                if (best == null || !BundleClaims.Keeps(best, o)) best = o;
            return best;
        }

        /// <summary>The path <paramref name="owner"/> itself wants for <paramref name="key"/>, served
        /// or held, or null when it wants nothing there.</summary>
        internal string PathFor(string key, string owner)
        {
            Dictionary<string, string> byOwner;
            string path;
            return wants.TryGetValue(key, out byOwner) && byOwner.TryGetValue(owner ?? "", out path) ? path : null;
        }

        /// <summary>The path the serving owner wants for <paramref name="key"/>, or null.</summary>
        internal string PathOf(string key)
        {
            string owner = Serving(key);
            return owner == null ? null : wants[key][owner];
        }

        /// <summary>Every wanted key with the path its serving owner wants - what goes into the catalog.</summary>
        internal Dictionary<string, string> Served()
        {
            Dictionary<string, string> rows = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (string key in wants.Keys) rows[key] = PathOf(key);
            return rows;
        }
    }
}
