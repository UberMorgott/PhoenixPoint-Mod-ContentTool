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
        /// <summary>RuntimeKey -> StreamingPath, relative to StreamingRoot (the game concatenates).</summary>
        private static readonly Dictionary<string, string> registered = new Dictionary<string, string>(StringComparer.Ordinal);
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
        /// </summary>
        public static string Register(string key, string absolutePath)
        {
            if (string.IsNullOrEmpty(key)) return "REFUSED: no RuntimeKey";
            if (!System.IO.File.Exists(absolutePath)) return "REFUSED: no file at " + absolutePath;

            if (!origin.ContainsKey(key)) origin[key] = Shipped(key);
            registered[key] = Relative(absolutePath);
            if (harmony == null)
            {
                harmony = new Harmony("com.morgott.ContentTool.CatalogLive");
                harmony.Patch(AccessTools.Method(typeof(StreamableAssetsManager), "Initialize"),
                              postfix: new HarmonyMethod(typeof(CatalogLive), nameof(Reinject)));
            }
            return Inject() ?? ("registered " + key + " -> " + registered[key]);
        }

        /// <summary>
        /// The exact inverse of <see cref="Register"/>, in the same session: a key we REPLACED goes
        /// back to the game's own StreamingPath, a key we ADDED leaves the catalog. This is what
        /// makes the mod manager's checkbox a real switch in both directions for a video - unticking
        /// it puts the shipped cutscene back with no restart.
        ///
        /// Returns what the key resolves to afterwards, so the caller can log a measured before/after
        /// pair instead of claiming an undo happened.
        /// </summary>
        public static string Unregister(string key)
        {
            if (string.IsNullOrEmpty(key) || !registered.ContainsKey(key)) return null;
            registered.Remove(key);

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
            if (why != null) Debug.LogError("CatalogLive: " + why);
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
