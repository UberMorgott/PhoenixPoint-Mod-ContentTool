using System;
using System.Collections.Generic;
using System.Reflection;
using Base.Assets.StreamableSystem;
using HarmonyLib;
using UnityEngine;

namespace Morgott.ContentTool.Bake
{
    /// <summary>
    /// The streamable catalog, extended IN MEMORY. Nothing in the install is modified - no
    /// Catalog.json edit, no .ct-backup, no edits ledger, no revert. A mod hands us a RuntimeKey and
    /// a file inside its OWN folder; the game resolves that key to that file for the rest of the run.
    ///
    /// It is smaller than the on-disk route because the catalog is barely private:
    /// StreamableAssetsCatalog.AllLocations is a PUBLIC field and InitializeCache() is a PUBLIC
    /// method that rebuilds the lookup from it. Only StreamableAssetsManager._catalog needs
    /// reflection. Replace = mutate AllLocations[i] (a struct, so it must be the ARRAY element - the
    /// dictionary hands back a copy and a write to it is lost); add = append; then InitializeCache().
    ///
    /// The manager is scene-placed (Awake -> Initialize, OnDestroy -> Uninitialize) and Initialize
    /// re-reads the file every time, so one postfix on it re-injects on every scene load. That is
    /// also why the shipped path needs no Uninitialize()+Initialize() dance.
    ///
    /// SAFER than editing the file: InitializeCache's ToDictionary still throws on a duplicate
    /// RuntimeKey, but now inside OUR call instead of the game's Awake - a bad key can no longer kill
    /// the boot scene. <see cref="CatalogText.Guard"/>'s rule is kept, applied before the rebuild.
    /// </summary>
    public static class CatalogLive
    {
        /// <summary>RuntimeKey -> every owner that wants it, each with its StreamingPath relative to
        /// StreamingRoot (the game concatenates). The lowest owner id is the one the catalog serves.</summary>
        private static readonly CatalogOwners owners = new CatalogOwners();
        /// <summary>The owner a caller that names none registers as. Empty, so it sorts BELOW every
        /// mod id and keeps what a direct <see cref="Register"/> call always did: take the row.</summary>
        private const string Anonymous = "";
        /// <summary>What the game said about a key BEFORE we first touched it: its own StreamingPath,
        /// or null for a key the game never had. Captured once, so an undo restores the shipped row
        /// rather than whatever the last mod wrote.</summary>
        private static readonly Dictionary<string, string> origin = new Dictionary<string, string>(StringComparer.Ordinal);
        private static readonly FieldInfo CatalogField = AccessTools.Field(typeof(StreamableAssetsManager), "_catalog");
        private static Harmony harmony;

        /// <summary>
        /// Serve <paramref name="absolutePath"/> for <paramref name="key"/>. Replaces the row if the
        /// game already has that key, adds one if it does not - the lookup IS the mode, same rule the
        /// author-facing declaration follows. Call it from your mod's OnModEnabled; calling it again
        /// with the same key just updates the path.
        ///
        /// Content mods may reach this either way. A direct assembly reference IS legal when meta.json
        /// declares "Dependencies": [ "com.morgott.ContentTool" ] and the reference is marked private
        /// false - the loader recursively enables and loads a dependency BEFORE its dependents, so we
        /// are already in memory when the caller's code first mentions our types. REFLECTION is still
        /// the safer form, and the reason is version skew, not resolution: Dependencies carries an id
        /// and no MINIMUM VERSION, so an OLDER ContentTool satisfies it while lacking this method, and
        /// a hard reference turns that into a MissingMethodException the caller cannot log its way out
        /// of. The failure that empties MOD_ACTIVATED and silently disables every other mod (measured
        /// 2026-08-13, commit 632fba7) came from referencing a Managed\ Unity module ModSDK\ does not
        /// ship - UnityEngine.VideoModule - never from referencing ContentTool.dll.
        ///
        /// This form names NO owner, so it registers as the anonymous owner "" - which outranks
        /// every mod id. A mod that may share a key with another should call <see cref="RegisterFor"/>.
        /// </summary>
        public static string Register(string key, string absolutePath)
        {
            return RegisterFor(Anonymous, key, absolutePath);
        }

        /// <summary>
        /// <see cref="Register"/>, OWNED: <paramref name="modId"/> wants <paramref name="key"/>
        /// served from <paramref name="absolutePath"/>. When two mods want one key the LOWER mod id
        /// serves it (the rule every ContentTool route shares, so two players with the same mods get
        /// the same clip); the other is held, not refused, and takes over when the lower one lets go.
        ///
        /// Returns "registered ..." when this mod now serves the key, "QUEUED: ..." when a lower id
        /// holds it, "REFUSED: ..." when nothing was recorded. A separate NAME rather than an
        /// overload, so a caller's GetMethod("Register") cannot turn ambiguous.
        /// </summary>
        public static string RegisterFor(string modId, string key, string absolutePath)
        {
            if (string.IsNullOrEmpty(key)) return "REFUSED: no RuntimeKey";
            if (!System.IO.File.Exists(absolutePath)) return "REFUSED: no file at " + absolutePath;
            string owner = modId ?? Anonymous;

            string before = owners.Serving(key), had = owners.PathFor(key, owner);
            if (!origin.ContainsKey(key)) origin[key] = Shipped(key);
            string serving = owners.Want(key, owner, Relative(absolutePath));
            if (harmony == null)
            {
                harmony = new Harmony("com.morgott.ContentTool.CatalogLive");
                harmony.Patch(AccessTools.Method(typeof(StreamableAssetsManager), "Initialize"),
                              postfix: new HarmonyMethod(typeof(CatalogLive), nameof(Reinject)));
            }
            if (!string.Equals(serving, owner, StringComparison.Ordinal))
                return "QUEUED: mod '" + serving + "' already serves key '" + key + "' and the lower mod " +
                       "id keeps it - '" + owner + "''s clip is held and takes over if '" + serving + "' lets go";

            string refusal = Inject();
            if (refusal != null)
            {
                // Nothing reached the catalog, so the record goes back to what it was: a want that
                // outlived its refusal would be pushed in by the next scene load's postfix with no one
                // to undo it. A re-register keeps the path the catalog still serves.
                if (had != null) owners.Want(key, owner, had);
                else owners.Drop(key, owner);
                if (!owners.Wanted(key)) origin.Remove(key);
                return refusal;
            }
            return "registered " + key + " -> " + owners.PathOf(key) +
                   (before != null && !string.Equals(before, owner, StringComparison.Ordinal)
                    ? " (taken from '" + before + "' - the lower mod id keeps a key; its clip comes back if '" +
                      owner + "' lets go)"
                    : "");
        }

        /// <summary>
        /// The exact inverse of <see cref="Register"/>, in the same session: a key we REPLACED goes
        /// back to the game's own StreamingPath, a key we ADDED leaves the catalog. This is what
        /// makes the mod manager's checkbox a real switch in both directions for a video - unticking
        /// it puts the shipped cutscene back with no restart.
        ///
        /// Returns what the key resolves to afterwards, so the caller can log a measured before/after
        /// pair instead of claiming an undo happened. Anonymous, like <see cref="Register"/>.
        /// </summary>
        public static string Unregister(string key)
        {
            return UnregisterFor(Anonymous, key);
        }

        /// <summary>
        /// <see cref="Unregister"/>, OWNED: drops only what <paramref name="modId"/> wanted. When
        /// another mod still wants the key its clip is served next instead of the game's; when that
        /// mod was only held behind a lower id, the catalog is not touched at all. Null when this mod
        /// wanted nothing under that key.
        /// </summary>
        public static string UnregisterFor(string modId, string key)
        {
            string owner = modId ?? Anonymous;
            if (string.IsNullOrEmpty(key)) return null;
            bool served = string.Equals(owners.Serving(key), owner, StringComparison.Ordinal);
            if (!owners.Drop(key, owner)) return null;
            if (owners.Wanted(key))
            {
                // Someone else still wants it: a held mod was dropped (nothing to do), or the server
                // was, and the next-lowest id takes the row over through the ordinary injection.
                if (served)
                {
                    string why = Inject();
                    if (why != null) return why;
                }
                return owners.PathOf(key);
            }

            StreamableAssetsManager mgr = StreamableAssetsManager.Instance;
            StreamableAssetsCatalog cat = mgr == null || CatalogField == null
                                        ? null : CatalogField.GetValue(mgr) as StreamableAssetsCatalog;
            if (cat == null || cat.AllLocations == null) return null;

            string was;
            origin.TryGetValue(key, out was);
            origin.Remove(key);

            List<StreamableAssetLocation> rows = new List<StreamableAssetLocation>(cat.AllLocations);
            int at = rows.FindIndex(l => l.RuntimeKey == key);
            if (at < 0) return null;
            if (was == null) rows.RemoveAt(at);          // ours entirely - take the whole row with it
            else
            {
                StreamableAssetLocation row = rows[at];  // struct: edit the copy, write it back
                row.StreamingPath = was;
                rows[at] = row;
            }
            cat.AllLocations = rows.ToArray();
            cat.InitializeCache();
            return was == null ? "(row removed)" : was;
        }

        /// <summary>The game's own StreamingPath for a key right now, or null when it has no row.</summary>
        private static string Shipped(string key)
        {
            StreamableAssetsManager mgr = StreamableAssetsManager.Instance;
            StreamableAssetsCatalog cat = mgr == null || CatalogField == null
                                        ? null : CatalogField.GetValue(mgr) as StreamableAssetsCatalog;
            if (cat == null || cat.AllLocations == null) return null;
            foreach (StreamableAssetLocation l in cat.AllLocations)
                if (l.RuntimeKey == key) return l.StreamingPath;
            return null;
        }

        /// <summary>The postfix itself. Harmony requires a void return, and a refusal must not throw
        /// out of the game's Awake, so it logs instead.</summary>
        public static void Reinject()
        {
            string why = Inject();
            if (why != null) Dev.ChunkedLog.Fail("CatalogLive: " + why);
        }

        /// <summary>
        /// Push every registration into the live catalog. Runs on demand and as the Initialize
        /// postfix, so a scene load that re-reads Catalog.json from disk does not undo anything.
        /// Returns null on success, or the refusal - and REFUSES rather than throwing, because as a
        /// postfix it runs inside the game's Awake.
        /// </summary>
        public static string Inject()
        {
            StreamableAssetsManager mgr = StreamableAssetsManager.Instance;
            Dictionary<string, string> registered = owners.Served();
            if (mgr == null || registered.Count == 0) return null;
            StreamableAssetsCatalog cat = CatalogField == null ? null : CatalogField.GetValue(mgr) as StreamableAssetsCatalog;
            if (cat == null || cat.AllLocations == null) return "REFUSED: no live catalog to extend";

            List<StreamableAssetLocation> rows = new List<StreamableAssetLocation>(cat.AllLocations);
            foreach (KeyValuePair<string, string> r in registered)
            {
                int at = rows.FindIndex(l => l.RuntimeKey == r.Key);
                if (at >= 0)
                {
                    StreamableAssetLocation row = rows[at];     // struct: edit the copy, write it back
                    row.StreamingPath = r.Value;
                    rows[at] = row;
                }
                else rows.Add(new StreamableAssetLocation
                {
                    Collection = rows.Count > 0 ? rows[0].Collection : "Videos_CopyFolderLocatorDef",
                    RuntimeKey = r.Key,
                    StreamingPath = r.Value
                });
            }

            StreamableAssetLocation[] next = rows.ToArray();
            string dup = Duplicate(next);
            if (dup != null)
                return "REFUSED: RuntimeKey '" + dup + "' would appear twice - InitializeCache does " +
                       "ToDictionary on it and would throw. The live catalog is untouched.";

            cat.AllLocations = next;
            cat.InitializeCache();
            return null;
        }

        private static string Duplicate(StreamableAssetLocation[] rows)
        {
            HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (StreamableAssetLocation r in rows) if (!seen.Add(r.RuntimeKey)) return r.RuntimeKey;
            return null;
        }

        /// <summary>
        /// GetStreamingPath is StreamingRoot + "/" + StreamingPath, always rooted at StreamingAssets,
        /// so a file in a mod folder is reached by a ".."-escaping relative path. UNMEASURED as of
        /// this writing - if the engine refuses it, the fallback is a postfix on GetStreamingPath
        /// returning the absolute path for our keys, which still writes nothing.
        /// ponytail: Uri does the relative math; no path parser of our own.
        /// </summary>
        private static string Relative(string absolutePath)
        {
            Uri root = new Uri(Application.streamingAssetsPath.Replace('\\', '/') + "/");
            return Uri.UnescapeDataString(root.MakeRelativeUri(new Uri(absolutePath.Replace('\\', '/'))).ToString());
        }
    }
}
