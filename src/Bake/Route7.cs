using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
        /// <summary>
        /// Mods whose bake FAILED in this session, so the checkbox does not bake them again on every pass.
        /// A failed bake installs nothing, so <see cref="BundleLive.Holds"/> stays false and
        /// <see cref="BundleClaims.RouteMoves"/> says "move" again for the POSTFIX pass - ModRoster runs
        /// BeforeSetEnabled-&gt;Reconciled AND AfterSetEnabled per press, so one checkbox cost TWO full
        /// blocking <see cref="ProjectBake.Run"/>s, both doomed the same way. Cleared by the bake that
        /// finally succeeds ('ct_route7 apply', or the next session).
        /// </summary>
        private static readonly HashSet<string> Failed = new HashSet<string>(StringComparer.Ordinal);

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
                    log.AppendLine(on && Failed.Contains(project.Id)
                        ? "'" + project.Id + "' failed to bake earlier in this session - not baking it " +
                          "again. Fix the lines it printed, then " + RetryHint(modDir)
                        : on ? ApplyProject(name) : BundleLive.Uninstall(project.Id));
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
        /// THE ARGUMENT THAT ACTUALLY RESOLVES BACK TO THIS FOLDER, or the truth that none does.
        /// The recovery line used to name the mod ID (`ct_route7 apply morgott.demo.customcreature`),
        /// which every verb here resolves through ContentToolMain.ProjectDir - and that resolves a
        /// FOLDER name (CustomCreature), never an id, so the line sent the author to run a command
        /// that finds nothing. A Steam Workshop mod lives at workshop\content\839770\&lt;id&gt;, beside
        /// no Mods\ folder, so NO console argument reaches it at all: the parser eats the backslashes
        /// of a path (ContentToolMain.ProjectDir's note) and the folder name is not a sibling. Asked of
        /// ProjectDir itself rather than re-derived, so the hint cannot drift from the resolver.
        /// </summary>
        private static string RetryHint(string modDir)
        {
            string folder = Path.GetFileName(modDir.TrimEnd('\\', '/'));
            return string.Equals(ContentToolMain.ProjectDir(folder), modDir, StringComparison.OrdinalIgnoreCase)
                ? "'ct_route7 apply " + folder + "'."
                : "restart the game - this mod is not in Mods\\, so no 'ct_route7 apply <name>' " +
                  "argument reaches " + modDir + " and the checkbox will not re-bake it this session.";
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

        /// <summary>What became of ONE bundle in an apply. A wizard cannot read this out of the log: zero
        /// claims taken is not the same fact as residency - a catalog Locate failure (BundleLive.cs:215-218)
        /// and an ownership conflict (BundleClaims.Claim:250) also take no claim, and reporting either of
        /// those as "restart and enable" is the tool telling the author something untrue with a straight
        /// face.</summary>
        internal enum ApplyDisposition { Redirected, Resident, Refused, BakeFailed }

        /// <summary>
        /// Installs a project's already-baked copies LIVE. Reads ContentToolMain.PatchedDir() and
        /// bakes when it is empty: this is what installing a DOWNLOADED mod looks like, where the
        /// patched copy cannot
        /// be shipped (it would put Phoenix Point's own assets inside a Workshop item) and is produced
        /// here, on the player's machine, from the player's own game files.
        /// </summary>
        internal static string ApplyProject(string projectName)
        {
            ApplyDisposition ignored;
            return ApplyProject(projectName, null, out ignored);
        }

        /// <param name="forBundle">the ONE shipped bundle the caller cares about, or null for the console
        /// verb, which prints the log and asks nothing.</param>
        internal static string ApplyProject(string projectName, string forBundle, out ApplyDisposition how)
        {
            how = ApplyDisposition.Refused;
            StringBuilder pre = new StringBuilder();
            string projectRoot = ContentToolMain.ProjectDir(projectName);
            Morgott.ContentTool.Project.ContentProject project =
                Morgott.ContentTool.Project.ContentProject.Load(projectRoot);
            string modId = project.Id;
            string patched = ContentToolMain.PatchedDir(modId);

            // What the project DECLARES today, not whatever .bundle the folder still holds: a copy
            // left by an older revision of the same project would otherwise be installed forever,
            // and a target that has been retargeted away collides with whoever owns it now.
            // CASE-BLIND, like every other bundle-name comparison on this route (BundleClaims.Find:224,
            // the forBundle arms below, File.Exists itself on Windows). Ordinal made a row whose casing
            // changed - "MyMod.Bundle" for "mymod.bundle" - list the same file twice here and, worse,
            // fail the :311 membership test against the patched copy already on disk, so the apply
            // ended in "REFUSED: nothing to install" permanently.
            List<string> declared = new List<string>();
            foreach (Morgott.ContentTool.Project.ShippedReplacement r in project.Replace)
                if (!declared.Contains(r.bundle, StringComparer.OrdinalIgnoreCase)) declared.Add(r.bundle);

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
                // After the bake, and only after a bake that REPORTED NOTHING WRONG. "After it" was
                // not enough: ProjectBake.Run states a failure in its own last line rather than
                // throwing, so a project with one unusable source - a malformed mesh that is skipped,
                // a replacement that refused - wrote the key anyway and last month's patched copy was
                // served as current for the life of this install. Leaving the key unwritten costs one
                // re-bake on the next enable, which is the right price for output nobody vouched for.
                //
                // The COUNT, never the text: reading it out of the log made this branch depend on the
                // wording of a sentence, and a reworded sentence would have failed the one way this
                // project keeps being bitten - silently, with a stale copy looking current.
                //
                // ONE BUTTON MUST NOT INSTALL AN UNVOUCHED BAKE. Leaving the freshness key unwritten was
                // enough while a human read the log and decided; the Doctor's Ship row presses this for
                // them, and "the copies below are whatever the last good bake produced" is not something
                // a wizard gets to do quietly.
                //
                // THE PATCH ROUTE'S OWN COUNT, not the run's. ProjectBake.Run's `failed` also folds in
                // p.ImportFailures and every arm over the mod's own bundle, so ONE unrelated .wav, .png or
                // .glb the importer refused blocked this project's perfectly good patched copies here -
                // permanently, on the PLAYER'S enable path: the key stayed unwritten, so every launch
                // re-baked, failed on the same unrelated file and installed nothing at all. Those failures
                // are still counted and printed by the bake above; they just do not decide route vii.
                int failed, patchFailed;
                pre.AppendLine(ProjectBake.Run(projectRoot, out failed, out patchFailed));
                if (patchFailed != 0)
                {
                    how = ApplyDisposition.BakeFailed;
                    Failed.Add(modId);
                    // No "press Ship again": this same text is printed by the console verb (Run:49) and by
                    // the mod-manager checkbox (Toggle:109), where there is no Ship button to press. The
                    // failures are NAMED above; a caller that has a next step adds its own.
                    return pre.AppendLine("NOT APPLIED: patching the shipped bundle(s) reported " + patchFailed +
                                          " failure(s), named in the P0/REFUSED line(s) above; nothing was " +
                                          "installed and no copy was marked current.").ToString();
                }
                Project.PatchCache.Write(patched, key);
            }
            List<KeyValuePair<string, string>> copies = new List<KeyValuePair<string, string>>();
            if (Directory.Exists(patched))
                foreach (string f in Directory.GetFiles(patched, "*.bundle"))
                {
                    string name = Path.GetFileName(f);
                    if (!declared.Contains(name, StringComparer.OrdinalIgnoreCase))
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
            //
            // RESIDENCY IS READ BEFORE THE INSTALL, because that is the order Register:80-92 decides in:
            // it refuses a resident bundle BEFORE it looks at claims. A press made after an earlier
            // redirect has already loaded would otherwise find this mod's own standing claim
            // (BundleClaims.Claim:258-267 keeps it), report Redirected, and print S2 ("redirected LIVE")
            // over a log that says "restart required" - the wizard lying with a straight face, which is
            // the whole reason this is a value and not a grep of the log.
            //
            // THE BUNDLE ASKED ABOUT HAS TO BE ONE THIS APPLY IS ABOUT, and both halves of that were
            // missing. A forBundle the project does not declare answered Refused with NO line naming it,
            // so the wizard reported a refusal nobody could act on. And a declared bundle that this apply
            // did NOT install - refused by Register, or simply absent from the patched folder - still
            // found this mod's own standing claim from an EARLIER apply (BundleClaims.Claim:258-267 keeps
            // it) and answered Redirected: the press credited with work it did not do.
            bool asked = !string.IsNullOrEmpty(forBundle);
            bool ours = asked && declared.Exists(b => string.Equals(b, forBundle, StringComparison.OrdinalIgnoreCase));
            if (asked && !ours)
                pre.AppendLine("REFUSED: " + forBundle + " is not declared by this project - its " +
                               Project.ContentMods.Manifest + " names " + declared.Count + " \"replace\" " +
                               "target(s)" + (declared.Count == 0 ? "" : ": " + string.Join(", ", declared.ToArray())));
            bool wasResident = ours && BundleLive.ResidentNow(forBundle);
            pre.Append("installing " + copies.Count + " patched copy(ies) as '" + modId + "'\n")
               .Append(BundleLive.Install(modId, copies));
            // UNCONDITIONALLY, once the copies are in. It sat inside the `!haveAll` branch, so the
            // press that arrived with a FRESH patched folder - the common case after a fix elsewhere -
            // installed the copies and left the session's "this one failed" flag standing, and the
            // mod-manager checkbox refused the mod for the rest of the session over a bake that is no
            // longer failing.
            Failed.Remove(modId);
            if (ours)
            {
                BundleClaim mine = copies.Exists(c => string.Equals(c.Key, forBundle, StringComparison.OrdinalIgnoreCase))
                    ? BundleClaims.Find(forBundle) : null;
                how = wasResident ? ApplyDisposition.Resident
                    : mine != null && string.Equals(mine.Mod, modId, StringComparison.Ordinal)
                      ? ApplyDisposition.Redirected
                      : ApplyDisposition.Refused;
            }
            return pre.ToString();
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
