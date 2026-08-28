using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

namespace Morgott.ContentTool.Bake
{
    /// <summary>
    /// The entry points of the two ADDRESSABLES routes - and, since S1-b, nothing that writes into
    /// the Phoenix Point installation (mandate M2).
    ///
    ///   route vii  "replace"  a patched private copy of a shipped bundle, served through
    ///                         ResourceManager.InternalIdTransformFunc (<see cref="BundleLive"/>)
    ///   route iii  "publish"  a new catalog key, served through an appended locator
    ///                         (<see cref="KeysLive"/>)
    /// Both live exactly as long as the session. Removing the mod leaves the installation
    /// byte-identical, because nothing of ours was ever put inside it.
    ///
    /// WHAT USED TO BE HERE, and why it is gone rather than gated. An older ContentTool rewrote the
    /// game's own StreamingAssets\aa\catalog.json and kept catalog.json.ct-backup / .ct-edits beside
    /// it. That premise was a non-sequitur: the catalog is parsed once into a ResourceLocationMap at
    /// Addressables.InitializeAsync (PhoenixGame.cs:737) and the MAP - not the file - is what loads
    /// bundles afterwards, so the disk edit bought nothing the live seams do not. A flag would have
    /// shipped the violation anyway, so the verbs are DELETED.
    ///
    /// An install that ran the old code still carries those files, applied before any mod runs. They
    /// are DETECTED here, read-only, and the affected route is refused BY NAME
    /// (<see cref="BundleClaims.LegacyRefusal"/>). They are never repaired: restoring a backup is
    /// itself a write into the player's game. The one sanctioned repair is Steam -> Phoenix Point ->
    /// Properties -> Installed Files -> "Verify integrity of game files".
    /// </summary>
    internal static class Route7
    {
        /// <summary>One mod's LEGACY on-disk claim on one shipped bundle. Read, never written.</summary>
        private sealed class Rec
        {
            internal readonly string Mod, Bundle, Path;
            internal Rec(string m, string b, string p) { Mod = m; Bundle = b; Path = p; }
        }

        internal static string Run(string[] args)
        {
            string verb = args != null && args.Length > 0 ? args[0].ToLowerInvariant() : "status";
            switch (verb)
            {
                case "apply":
                    return args != null && args.Length > 1
                        ? ApplyProject(args[1])
                        : "usage: ct_route7 apply <project> - name the project whose \"replace\" " +
                          "declarations to serve. There is no target-less demo apply any more: it " +
                          "wrote the game's own catalog.json.";
                case "status": return Status();
                case "dryrun":
                case "verify":
                case "revert":
                case "stacktest":
                    return BundleClaims.Removed("ct_route7", verb,
                        "Route vii is LIVE now: 'ct_route7 apply <project>' redirects the bundle in " +
                        "memory and 'ct_route7 status' shows what is redirected. There is no " +
                        "catalog.json edit left to dry-run, verify on disk, revert or stack.");
                default: return "usage: ct_route7 apply <project> | status";
            }
        }

        /// <summary>
        /// The mod manager's checkbox, for the two routes that are NOT runtime code: replacements
        /// (<see cref="BundleLive"/>) and published keys (<see cref="KeysLive"/>). Switching a content
        /// mod OFF has to undo both, or the content is still there and the checkbox is a lie.
        ///
        /// EACH DECLARED ROUTE IS EVALUATED ON ITS OWN. They are two registries and they drift: a
        /// project declaring both can have its keys published and its replacement not (the register
        /// refused, or "replace" was added to the manifest afterwards). One collapsed "is this mod
        /// applied" answer skipped the whole toggle as soon as EITHER route held the mod, so the
        /// missing route was never repaired on enable and never undone on disable.
        ///
        /// The registry IS the state, so it is also the idempotence: a route already in the wanted
        /// state is not touched, and nothing to do returns null.
        ///
        /// ponytail: the ON path bakes synchronously when the patched copies are absent (first ever
        /// enable), so that toggle blocks for as long as the bake takes. There is no async seam here
        /// to hang it on; give it one if a first enable is ever measured as painful.
        /// </summary>
        internal static string Toggle(string modDir, bool on)
        {
            if (string.IsNullOrEmpty(modDir) ||
                !File.Exists(Path.Combine(modDir, Project.ContentMods.Manifest))) return null;
            Project.ContentProject.Declared project = Project.ContentProject.LoadDeclared(modDir);
            bool wantReplace = project.Replace.Count > 0, wantPublish = project.Publish.Count > 0;
            if (!wantReplace && !wantPublish) return null;

            StringBuilder log = new StringBuilder();
            // The mod's OWN directory, never its folder NAME. Both verbs below resolve their argument
            // through ContentToolMain.ProjectDir, which looks for a sibling of ContentTool and then
            // inside ContentTool's own folder - and a Steam Workshop mod lives at
            // workshop\content\839770\<id>, beside neither. Passing the name made every Workshop mod
            // that declares "replace" or "publish" resolve to the wrong project or to nothing at all,
            // and fail silently; reachable since 9f4a316 made media-only Workshop mods loadable.
            string name = modDir;

            if (wantReplace)
            {
                // An install carrying the OLD on-disk edit for this mod: refuse the route in both
                // directions rather than write into the player's game to repair it, and say so loudly.
                string legacy = LegacyDisk(project.Id);
                if (legacy != null) log.AppendLine(legacy);
                else if (BundleClaims.RouteMoves(true, BundleLive.Holds(project.Id), on))
                    log.AppendLine(on ? ApplyProject(name) : BundleLive.Uninstall(project.Id));
            }
            if (wantPublish)
            {
                // Same as above, for route iii: an install carrying the OLD on-disk key publication is
                // applied by Addressables before any mod runs, so the checkbox cannot switch it off and
                // we will not write into the player's game to undo it. Refuse by name instead.
                string legacy = LegacyPub(project.Id);
                if (legacy != null) log.AppendLine(legacy);
                else if (BundleClaims.RouteMoves(true, KeysLive.Holds(project.Id), on))
                    log.AppendLine(on ? CatalogApply(name) : KeysLive.Uninstall(project.Id));
            }

            string what = log.ToString().TrimEnd();
            return what.Length == 0 ? null : what;
        }

        /// <summary>
        /// Route iii's half of <see cref="LegacyDisk"/>: keys an OLDER ContentTool wrote into the
        /// game's own catalog.json for this mod. Names them and the one sanctioned repair, or null,
        /// which is every install that never ran the on-disk publisher.
        /// </summary>
        private static string LegacyPub(string modId)
        {
            List<string> keys = new List<string>();
            foreach (CatalogKeys.Pub p in LoadPubs()) if (p.Mod == modId) keys.Add("key '" + p.Key + "'");
            return BundleClaims.LegacyRefusal(modId, keys, EditsFile, Catalog, "published key");
        }

        /// <summary>
        /// The replacement route no longer writes anything into the installation - but a mod that was
        /// installed by the version that did still has its record sitting in the game's own catalog
        /// ledger, applied before any mod runs. Names the bundles and the exact files, or null.
        /// </summary>
        private static string LegacyDisk(string modId)
        {
            List<string> bundles = new List<string>();
            foreach (Rec r in LoadEdits()) if (r.Mod == modId) bundles.Add(r.Bundle);
            return BundleClaims.LegacyRefusal(modId, bundles, EditsFile, Catalog);
        }

        /// <summary>The game's own Addressables catalog. READ ONLY - nothing here ever opens it for
        /// writing; it is named so a refusal can tell the player which file to verify.</summary>
        private static string Catalog =>
            Path.Combine(Path.Combine(Application.streamingAssetsPath, "aa"), "catalog.json");
        /// <summary>Same file, for the arms that read it back.</summary>
        internal static string CatalogPath { get { return Catalog; } }
        /// <summary>The LEGACY ledger an older ContentTool wrote beside it. Read, never written.</summary>
        private static string EditsFile => Catalog + ".ct-edits";

        // ------------------------------------------------------------------ verbs

        private static string Status()
        {
            StringBuilder log = new StringBuilder();
            log.AppendLine(BundleLive.Status());
            log.AppendLine(KeysLive.Status());
            log.Append(Legacy());
            return log.ToString();
        }

        /// <summary>
        /// Read-only: what an OLDER ContentTool left INSIDE the installation. Never repaired here - a
        /// restore is itself a write - so this names the files and the one sanctioned repair, nothing
        /// more. Silence would be the worst outcome: content still applied under a checkbox reading OFF.
        /// </summary>
        private static string Legacy()
        {
            List<Rec> recs = LoadEdits();
            List<CatalogKeys.Pub> pubs = LoadPubs();
            if (recs.Count == 0 && pubs.Count == 0)
                return "legacy: none - there is no " + EditsFile +
                       ", so nothing an older ContentTool wrote is left in this installation";

            StringBuilder log = new StringBuilder("LEGACY on-disk records are present in YOUR GAME " +
                "INSTALLATION (" + EditsFile + ", written by an older ContentTool). They are applied " +
                "by Addressables before any mod runs, and ContentTool neither writes there any more " +
                "nor writes there to undo them. REPAIR: Steam -> Phoenix Point -> Properties -> " +
                "Installed Files -> \"Verify integrity of game files\", which restores " + Catalog +
                "; then delete " + EditsFile + " and " + Catalog + ".ct-backup.");
            log.Append("\ncatalog: ").Append(Catalog).Append(" sha1=")
               .Append(File.Exists(Catalog) ? Sha1(Catalog) : "(missing)");
            foreach (Rec r in recs)
                log.Append("\n  edit: ").Append(r.Mod).Append(" -> ").Append(r.Bundle).Append(" = ").Append(r.Path);
            foreach (CatalogKeys.Pub p in pubs)
                log.Append("\n  pub:  ").Append(p.Mod).Append(" -> key '").Append(p.Key).Append("' = ").Append(p.Asset);
            return log.ToString();
        }

        /// <summary>
        /// Installs a project's already-baked copies LIVE. Reads ContentToolMain.PatchedDir() and
        /// bakes when it is empty: this is what installing a DOWNLOADED mod looks like, where the
        /// patched copy cannot
        /// be shipped (it would put Phoenix Point's own assets inside a Workshop item) and is produced
        /// here, on the player's machine, from the player's own game files.
        /// </summary>
        private static string ApplyProject(string projectName)
        {
            StringBuilder pre = new StringBuilder();
            string projectRoot = ContentToolMain.ProjectDir(projectName);
            Morgott.ContentTool.Project.ContentProject project =
                Morgott.ContentTool.Project.ContentProject.Load(projectRoot);
            string modId = project.Id;
            string patched = ContentToolMain.PatchedDir(modId);

            // What the project DECLARES today, not whatever .bundle the folder still holds: a copy
            // left by an older revision of the same project would otherwise be installed forever,
            // and a target that has been retargeted away collides with whoever owns it now.
            List<string> declared = new List<string>();
            foreach (Morgott.ContentTool.Project.ShippedReplacement r in project.Replace)
                if (!declared.Contains(r.bundle)) declared.Add(r.bundle);

            // FRESHNESS, not existence (S3). "Every declared file is there" said nothing about
            // WHICH version of the game, of this mod or of ContentTool's own bake format produced
            // them, so a game update, a mod update or a format change kept serving last month's copy
            // out of AppData forever, silently. The key covers all three; a folder written by the
            // ContentTool that had no key is stale by definition, which is the right answer for it.
            List<string> sources = new List<string>();
            foreach (string b in declared) sources.Add(BakeSelfCheck.ShippedBundlePath(b));
            string key = Project.PatchCache.Key(projectRoot, sources);
            bool fresh = Project.PatchCache.Fresh(patched, key);
            bool haveAll = fresh && Directory.Exists(patched);
            foreach (string b in declared)
                if (!File.Exists(Path.Combine(patched, b))) haveAll = false;
            if (!haveAll)
            {
                pre.AppendLine(Directory.Exists(patched) && !fresh
                    ? "the patched copies in " + patched + " were built from a different project, game " +
                      "build or ContentTool format - re-baking them"
                    : "no patched copies yet - baking them from YOUR installation");
                pre.AppendLine(ProjectBake.Run(projectRoot));
                // After the bake, and only after it: a key written ahead of a failed bake would make
                // the broken output look current for the rest of this install's life.
                Project.PatchCache.Write(patched, key);
            }
            List<KeyValuePair<string, string>> copies = new List<KeyValuePair<string, string>>();
            if (Directory.Exists(patched))
                foreach (string f in Directory.GetFiles(patched, "*.bundle"))
                {
                    string name = Path.GetFileName(f);
                    if (!declared.Contains(name))
                    {
                        pre.AppendLine("skipping " + name + " - " + patched +
                                       " still holds it, but the project no longer declares it");
                        continue;
                    }
                    copies.Add(new KeyValuePair<string, string>(name, f.Replace('\\', '/')));
                }
            if (copies.Count == 0)
                return pre.Append("REFUSED: nothing to install - " + patched +
                                  " holds no .bundle, and the bake above produced none.").ToString();

            // LIVE, not on disk (S1, 2026-08-27): the catalog is parsed into a ResourceLocationMap at
            // startup and the MAP is what loads bundles afterwards, so redirecting the live locations
            // does everything the catalog edit did and writes nothing into the player's install.
            // "installing", not "installed": the count below is what was DECLARED, and BundleLive's
            // own tally on the last line is what actually happened, refusals named.
            return pre.Append("installing " + copies.Count + " patched copy(ies) as '" + modId + "'\n")
                      .Append(BundleLive.Install(modId, copies)).ToString();
        }

        // ------------------------------------------------------------------ route iii (ct_catalog)

        /// <summary>The SECOND route's verbs, in the same file because they share one legacy ledger.</summary>
        internal static string RunCatalog(string[] args)
        {
            string verb = args != null && args.Length > 0 ? args[0].ToLowerInvariant() : "status";
            switch (verb)
            {
                case "apply": return CatalogApply(args != null && args.Length > 1 ? args[1] : null);
                case "verify": return CatalogVerify();
                case "status": return Status();
                case "revert":
                case "selftest":
                    return BundleClaims.Removed("ct_catalog", verb,
                        "Route iii is LIVE now: enabling the mod publishes its keys and disabling it " +
                        "un-publishes them, so there is no catalog.json edit to revert - and no " +
                        "on-disk writer left for a self-test to falsify. 'ct_catalog status' shows " +
                        "what is published.");
                default: return "usage: ct_catalog apply [project] | verify | status";
            }
        }

        /// <summary>
        /// Installs a project's published keys LIVE (<see cref="KeysLive"/>) - nothing is written into
        /// the game installation (mandate M2). Every declared asset is still GROUNDED against the mod's
        /// own bundle file first: a key pointing at a name the bundle does not contain would load as
        /// null forever, silently, in the player's game.
        ///
        /// <paramref name="projectName"/> is a project NAME from the console, or an absolute project
        /// DIRECTORY from the enable-time toggle; ProjectDir takes either.
        /// </summary>
        private static string CatalogApply(string projectName)
        {
            StringBuilder log = new StringBuilder();
            string projectRoot = ContentToolMain.ProjectDir(projectName);
            Morgott.ContentTool.Project.ContentProject.Declared project =
                Morgott.ContentTool.Project.ContentProject.LoadDeclared(projectRoot);
            if (project.Publish.Count == 0)
                return "REFUSED: '" + project.Id + "' declares no \"publish\" entries in ppcontent.json - " +
                       "route iii installs KEYS, and there are none.";

            string bundle = Path.Combine(Path.Combine(projectRoot, "Dist"), project.BundleName);
            if (!File.Exists(bundle))
                return "REFUSED: no mod bundle at " + bundle + " - bake it first with 'ct_project " +
                       (projectName ?? "") + "'. Installing a key does not bake, on purpose: a build " +
                       "command must not mutate the player's game installation.";

            List<CatalogKeys.Pub> mine = new List<CatalogKeys.Pub>();
            // Same collision as the bake's load-back: the mod being installed may already have its
            // own bundle open, and a key that "did not open" would be refused for the wrong reason.
            BundleResidency.Release(BundleResidency.Identity(project.Id));
            AssetBundle ab = AssetBundle.LoadFromFile(bundle);
            try
            {
                if (ab == null)
                    return log.Append("REFUSED: " + bundle + " did not open - nothing was published.").ToString();
                foreach (Morgott.ContentTool.Project.PublishedKey k in project.Publish)
                {
                    // BundleBaker's own naming rule: "assets/<modid>/<relative path>", lowercased.
                    string asset = "assets/" + project.Id + "/" +
                                   k.asset.Replace('\\', '/').Trim('/').ToLowerInvariant();
                    if (!ab.Contains(asset))
                        return log.Append("REFUSED: '" + asset + "' is not in " + Path.GetFileName(bundle) +
                                          " (it holds " + ab.GetAllAssetNames().Length + " asset(s)); the key '" +
                                          k.key + "' would resolve to null forever. Nothing was published.").ToString();
                    mine.Add(new CatalogKeys.Pub(project.Id, k.key, bundle.Replace('\\', '/'), asset,
                                                 k.type, k.deps));
                }
            }
            finally { if (ab != null) ab.Unload(true); }

            // A coarser guard one rung before the live registry's own: refuse a key another mod
            // already publishes before anything is registered at all.
            List<CatalogKeys.Pub> others = new List<CatalogKeys.Pub>();
            foreach (KeyClaim c in KeyClaims.All)
                if (c.Mod != project.Id)
                    others.Add(new CatalogKeys.Pub(c.Mod, c.Key, c.BundlePath, c.Asset, c.TypeName, null));
            foreach (CatalogKeys.Pub want in mine)
            {
                string clash = CatalogKeys.Conflict(others, want);
                if (clash != null) return log.Append("REFUSED: " + clash + " - nothing was published.").ToString();
            }

            // The count comes from KeysLive's own tally of what each Register call ANSWERED. This line
            // used to print mine.Count - the DECLARED number - so a run where one key of two was
            // refused still ended "PUBLISHED 2 key(s)" and read as a success.
            log.Append(KeysLive.Install(project.Id, mine));
            log.Append("\nNo restart needed, nothing was written to your game installation, and " +
                       "disabling '" + project.Id + "' removes whatever published again.");
            return log.ToString();
        }

        private static string CatalogVerify()
        {
            StringBuilder log = new StringBuilder();
            // The LIVE registry is the state, not a ledger file - and there is no restart guard to
            // apply, because a locator is in force the moment it is appended.
            List<CatalogKeys.Pub> pubs = new List<CatalogKeys.Pub>();
            foreach (KeyClaim c in KeyClaims.All)
                pubs.Add(new CatalogKeys.Pub(c.Mod, c.Key, c.BundlePath, c.Asset, c.TypeName, null));
            if (pubs.Count == 0) return "no key published in this session - enable a mod that declares " +
                                       "\"publish\" entries, or run 'ct_catalog apply <project>'";

            int fail = CatalogKeys.Verify(log, pubs);
            log.Append(fail == 0
                ? "ct_catalog: PASS - the game's own Addressables served the mod's own bundle, and nothing was written to the installation"
                : "ct_catalog: " + fail + " FAILURE(S)");
            return log.ToString();
        }

        // ------------------------------------------------------------------ catalog text (read only)

        /// <summary>One of the catalog's base64 blobs, decoded. Read-only: the codec that USED to
        /// splice them back into the file is gone; <see cref="CatalogKeys.KeyCount"/> reads them to
        /// prove the shipped catalog is still untouched.</summary>
        internal static byte[] Blob(string json, string field, out int start, out int end)
        {
            string f = "\"" + field + "\":\"";
            start = json.IndexOf(f, StringComparison.Ordinal);
            if (start < 0) throw new InvalidOperationException("catalog has no " + field);
            start += f.Length;
            end = json.IndexOf('"', start);
            return Convert.FromBase64String(json.Substring(start, end - start));
        }

        // ------------------------------------------------------------------ the LEGACY sidecar record

        private static List<Rec> LoadEdits()
        {
            List<Rec> recs = new List<Rec>();
            if (!File.Exists(EditsFile)) return recs;
            foreach (string line in File.ReadAllLines(EditsFile))
            {
                string[] f = line.Split('\t');
                if (f.Length == 4 && f[0] == "edit") recs.Add(new Rec(f[1], f[2], f[3]));
            }
            return recs;
        }

        /// <summary>Route iii's legacy records out of the SAME ledger file. Only what a record
        /// contains differs.</summary>
        internal static List<CatalogKeys.Pub> LoadPubs()
        {
            List<CatalogKeys.Pub> pubs = new List<CatalogKeys.Pub>();
            if (!File.Exists(EditsFile)) return pubs;
            foreach (string line in File.ReadAllLines(EditsFile))
            {
                CatalogKeys.Pub p = CatalogKeys.Pub.Parse(line.Split('\t'));
                if (p != null) pubs.Add(p);
            }
            return pubs;
        }

        private static string Sha1(string path)
        {
            using (SHA1 h = SHA1.Create())
            using (FileStream f = File.OpenRead(path))
                return BitConverter.ToString(h.ComputeHash(f)).Replace("-", "").ToLowerInvariant();
        }
    }
}
