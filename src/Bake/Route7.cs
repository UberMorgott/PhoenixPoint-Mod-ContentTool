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
                // THE SECOND CONSUMER OF THE ONE VERIFY PRODUCER (§4.4 item 9). It left the removal arm
                // below - whose text is otherwise UNCHANGED - because there IS something to verify now:
                // the patched copies on disk, read back through the same gates the bake ran.
                case "verify":
                    return args != null && args.Length > 1
                        ? VerifyProject(args[1])
                        : "usage: ct_route7 verify <project> - re-read this project's patched copies. It " +
                          "installs nothing and writes nothing.";
                case "dryrun":
                case "revert":
                case "stacktest":
                    return BundleClaims.Removed("ct_route7", verb,
                        "Route vii is LIVE now: 'ct_route7 apply <project>' redirects the bundle in " +
                        "memory and 'ct_route7 status' shows what is redirected. There is no " +
                        "catalog.json edit left to dry-run, verify on disk, revert or stack.");
                default: return "usage: ct_route7 apply <project> | verify <project> | status";
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

        /// <summary>READ-ONLY view of that set, for admission (R29). The set itself is never handed out and
        /// there is no bypass: the only thing that clears an entry is <c>Failed.Remove(modId)</c> after a
        /// successful Install (:405), exactly as before.</summary>
        internal static bool IsFailed(string modId)
        {
            return !string.IsNullOrEmpty(modId) && Failed.Contains(modId);
        }

        /// <summary>
        /// THE ONE FRESHNESS OBSERVATION, and the only place the filesystem is asked.
        ///
        /// <c>ApplyProject</c> computed this inline (`fresh &amp;&amp; Directory.Exists(patched)`, then the
        /// declared-copy census) and the dashboard needs the same answer for the same folder; two copies of
        /// it would be two answers, and the failure mode of the disagreeing one is a stale copy displayed as
        /// current. So the census, the key and the `haveAll` verdict live in <see cref="FreshnessObservation"/>
        /// and BOTH callers read it here.
        ///
        /// VIDEO ROWS ARE NOT DECLARED TARGETS - they carry no "bundle" at all (ContentProject.ParseReplace
        /// exempts them), so taking one keyed the hash on ShippedBundlePath(null) and threw out of
        /// Path.Combine(patched, null). Case-blind, like every other bundle-name comparison on this route:
        /// Ordinal made a row whose casing changed list the same file twice and then fail the membership
        /// test against the copy already on disk, so the apply ended in "nothing to install" permanently.
        /// </summary>
        internal static FreshnessObservation Observe(Morgott.ContentTool.Project.ContentProject project,
                                                     string projectRoot)
        {
            string patched = ContentToolMain.PatchedDir(project.Id);
            List<string> declared = new List<string>();
            foreach (Morgott.ContentTool.Project.ShippedReplacement r in project.Replace)
            {
                if (!string.IsNullOrEmpty(r.video)) continue;
                if (!declared.Contains(r.bundle, StringComparer.OrdinalIgnoreCase)) declared.Add(r.bundle);
            }

            // FRESHNESS, not existence (S3). "Every declared file is there" said nothing about WHICH
            // version of the game, of this mod or of ContentTool's own bake format produced them, so a game
            // update, a mod update or a format change kept serving last month's copy out of AppData
            // forever, silently. The key covers all three; a folder written by a ContentTool that had no
            // key is stale by definition, which is the right answer for it.
            List<string> sources = new List<string>();
            foreach (string b in declared) sources.Add(BakeSelfCheck.ShippedBundlePath(b));
            return Observe(patched, projectRoot, declared, sources);
        }

        /// <summary>The same observation with every Unity-derived path ALREADY RESOLVED - what a worker
        /// gets. <c>ContentToolMain.PatchedDir</c> reads Application.persistentDataPath and
        /// <c>BakeSelfCheck.ShippedBundlePath</c> reads streamingAssetsPath; both are main-thread facts, so
        /// the overload above reads them on main and this one takes them as strings. Everything left here -
        /// the SHA-1 of the manifest, a stat of every source under Content\ and a File.Exists per declared
        /// copy - is plain System.IO and is the slowest non-Unity work in the whole stage, which is exactly
        /// what belongs on a worker (design section 4).</summary>
        internal static FreshnessObservation Observe(string patched, string projectRoot,
                                                     IList<string> declared, IList<string> shippedPaths)
        {
            string key = Project.PatchCache.Key(projectRoot, shippedPaths);

            List<string> missing = new List<string>();
            foreach (string b in declared)
                if (!File.Exists(Path.Combine(patched, b))) missing.Add(b);

            string[] names = new string[declared.Count];
            declared.CopyTo(names, 0);
            return new FreshnessObservation(key, Project.PatchCache.Fresh(patched, key),
                                            Directory.Exists(patched), names, missing.ToArray());
        }

        internal static string Toggle(string modDir, bool on)
        {
            if (string.IsNullOrEmpty(modDir) ||
                !File.Exists(Path.Combine(modDir, Project.ContentMods.Manifest))) return null;
            Project.ContentProject.Declared project = Project.ContentProject.LoadDeclared(modDir);
            // VIDEO rows are not the replace route's work - :288 skips them when it builds `declared`,
            // so counting them here sent a video-only mod through a full blocking ProjectBake.Run that
            // wrote a patch-cache key for an empty `declared` and ended at :360 "REFUSED: nothing to
            // install". Ask for the rows the route actually patches.
            bool wantReplace = project.Replace.Exists(r => string.IsNullOrEmpty(r.video)),
                 wantPublish = project.Publish.Count > 0;
            // ...but the LEGACY record is keyed on the MOD, not on what it declares TODAY: a mod that
            // once declared bundle rows and now declares only "video" still has its .ct-edits record
            // applied by Addressables before any mod runs, and returning here left that unwarned - the
            // one outcome this route refuses to have, content applied under a checkbox reading OFF.
            if (project.Replace.Count == 0 && !wantPublish) return null;

            StringBuilder log = new StringBuilder();
            // The mod's OWN directory, never its folder NAME. Both verbs below resolve their argument
            // through ContentToolMain.ProjectDir, which looks for a sibling of ContentTool and then
            // inside ContentTool's own folder - and a Steam Workshop mod lives at
            // workshop\content\839770\<id>, beside neither. Passing the name made every Workshop mod
            // that declares "replace" or "publish" resolve to the wrong project or to nothing at all,
            // and fail silently; reachable since 9f4a316 made media-only Workshop mods loadable.
            string name = modDir;

            // An install carrying the OLD on-disk edit for this mod: refuse the route in both
            // directions rather than write into the player's game to repair it, and say so loudly.
            // Asked whenever the project declares ANY "replace" row, video ones included; only the
            // WORK below is gated on the rows this route actually patches.
            string legacyEdit = project.Replace.Count > 0 ? LegacyDisk(project.Id) : null;
            if (legacyEdit != null) log.AppendLine(legacyEdit);
            else if (wantReplace && BundleClaims.RouteMoves(true, BundleLive.Holds(project.Id), on))
                log.AppendLine(on && Failed.Contains(project.Id)
                    ? StageText.R29(project.Id, RetryHint(modDir))   // ONE copy of R29, in StageText
                    : on ? ApplyProject(name) : BundleLive.Uninstall(project.Id));
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
        /// <summary>Internal, not private, since admission reads it: R29 needs the hint from the ONE thing
        /// that knows which argument resolves back to that folder, never a re-derived guess.</summary>
        internal static string RetryHint(string modDir)
        {
            string folder = Path.GetFileName(modDir.TrimEnd('\\', '/'));
            // NORMALISED on both sides. ProjectDir builds with Path.Combine (no trailing separator),
            // while modDir arrives as the mod manager spelled it - a trailing '\' or '/', or mixed
            // separators, made a real Mods\<name> project print "restart the game - this mod is not
            // in Mods\", which is the one sentence that is never true for it.
            return string.Equals(Norm(ContentToolMain.ProjectDir(folder)), Norm(modDir),
                                 StringComparison.OrdinalIgnoreCase)
                ? "'ct_route7 apply " + folder + "'."
                : "restart the game - this mod is not in Mods\\, so no 'ct_route7 apply <name>' " +
                  "argument reaches " + modDir + " and the checkbox will not re-bake it this session.";
        }

        /// <summary>One spelling of a directory path, so two of them can be compared.</summary>
        private static string Norm(string path) { return Path.GetFullPath(path).TrimEnd('\\', '/'); }

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

        /// <summary>R36's one fact, and the only thing outside this file needs from the legacy ledger: is an
        /// older ContentTool's on-disk edit still recorded for this mod? Read-only, like the whole route.
        /// An unreadable ledger is NOT a legacy edit - it is a missing file, which is every clean install.</summary>
        internal static bool LegacyDiskActive(string modId)
        {
            try { return !string.IsNullOrEmpty(modId) && LegacyDisk(modId) != null; }
            catch (Exception) { return false; }
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

        /// <summary>What became of ONE target, kept instead of thrown away. <c>BundleLive.Install</c> builds
        /// exactly this line per bundle and then folds every one of them into an aggregate (:66), and
        /// <c>ApplyProject</c>'s single-bundle answer can only speak for the ONE bundle its caller named -
        /// so a panel with five rows had two ways to learn what happened to the other four: parse the log,
        /// or install twice. Both are forbidden, and this is the third.</summary>
        internal sealed class TargetInstall
        {
            internal readonly string Bundle;
            /// <summary>The producer's own line for this target, VERBATIM - never re-composed, and never
            /// parsed to work out <see cref="Outcome"/>, which is measured separately.</summary>
            internal readonly string Line;
            internal readonly ApplyDisposition Outcome;

            internal TargetInstall(string bundle, string line, ApplyDisposition outcome)
            {
                Bundle = bundle; Line = line; Outcome = outcome;
            }
        }

        /// <summary>
        /// `ct_route7 verify &lt;project&gt;` - the console consumer of the ONE Verify producer. It prints
        /// the gate log and then the producer's terminal line, which is character for character the line
        /// the dashboard's Verify row shows for the same project (W18).
        ///
        /// NO INSTALL, NO WRITE, NO KEY. It loads the declaration and its sources, reads the copies that
        /// are already in <c>PatchedDir</c>, and says what they prove.
        /// </summary>
        private static string VerifyProject(string projectName)
        {
            // BY NAME, and the spec sanctions it for a console verb (plan:915) - but it is the same
            // duplicate-name trap `ApplyRoot` exists for: `ProjectDir` resolves a NAME, so a sibling mod
            // and one of our own subfolders answering to it resolve to the wrong folder. The dashboard
            // binds a canonical ROOT for exactly that reason.
            Morgott.ContentTool.Project.ContentProject p =
                Morgott.ContentTool.Project.ContentProject.Load(ContentToolMain.ProjectDir(projectName));

            // R30 BEFORE ANY GATE, and it is the same fact `Admit` refuses the dashboard's Verify on: once
            // the game has LOADED a declared bundle it keeps serving what it loaded, so the copies on disk
            // - however correct - are not what is on screen, and a "Verify: PASS" over them is the tool
            // vouching for a revision nobody can see. The dashboard carries that as `RestartRequired`
            // (Apply's `Resident`); a console verb has no session receipt, so it reads the live residency
            // itself - `BundleLive.ResidentNow`, the same call the install path samples per target.
            //
            // BUT RESIDENCY ALONE IS NOT THAT FACT, and refusing on it broke the parity it was added for.
            // A mod enabled BEFORE its target ever loaded redirects it first and the game then loads OUR
            // copy through the transform func: resident, and current. The dashboard verifies that state
            // (Apply answered Redirected, so `RestartRequired` is false) and this verb refused it. The
            // rule both now ask is `BundleClaims.RestartRequired` - resident AND not served through a
            // standing claim of ours that was in force before the load.
            foreach (Morgott.ContentTool.Project.ShippedReplacement row in p.Replace)
            {
                if (!string.IsNullOrEmpty(row.video) || string.IsNullOrEmpty(row.bundle)) continue;
                if (BundleClaims.RestartRequired(p.Id, row.bundle, BundleLive.ResidentNow(row.bundle)))
                    return StageText.R30(p.Id);
            }

            StringBuilder log = new StringBuilder();
            LifecycleState.StageReport r = ReadBack.Verify(p, ContentToolMain.PatchedDir(p.Id), log);
            return log.Append(r.Verdict).ToString();
        }

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

        /// <summary>The SAME apply, with a disposition per declared target - what a five-row panel needs and
        /// what neither wrapper can give it (see <see cref="TargetInstall"/>). <paramref name="how"/> is
        /// aggregated CONSERVATIVELY: any refusal survives, then any restart-required target, and a blanket
        /// "redirected LIVE" is only reported when every target was.</summary>
        internal static string ApplyProject(string projectName, out IList<TargetInstall> targets,
                                            out ApplyDisposition how)
        {
            return ApplyProject(projectName, null, out targets, out how);
        }

        /// <param name="forBundle">the ONE shipped bundle the caller cares about, or null for the console
        /// verb, which prints the log and asks nothing.</param>
        internal static string ApplyProject(string projectName, string forBundle, out ApplyDisposition how)
        {
            IList<TargetInstall> ignored;
            return ApplyProject(projectName, forBundle, out ignored, out how);
        }

        private static string ApplyProject(string projectName, string forBundle,
                                           out IList<TargetInstall> targets, out ApplyDisposition how)
        {
            return ApplyRoot(ContentToolMain.ProjectDir(projectName), forBundle, out targets, out how);
        }

        /// <summary>
        /// THE SAME APPLY, entered by the project's canonical ROOT instead of by a name. Every overload above
        /// resolves <c>ContentToolMain.ProjectDir</c> first, which is a NAME LOOKUP by construction (the game
        /// console eats backslashes) - and the dashboard binds a full path it already resolved, where a
        /// duplicate name would answer with the wrong folder. Same claim, taken once, in the same place:
        /// there is no second apply path here, only a second door onto this one.
        /// </summary>
        internal static string ApplyRoot(string projectRoot, string forBundle,
                                         out IList<TargetInstall> targets, out ApplyDisposition how)
        {
            targets = new List<TargetInstall>();
            how = ApplyDisposition.Refused;
            Morgott.ContentTool.Project.ContentProject project =
                Morgott.ContentTool.Project.ContentProject.Load(projectRoot);

            // THE CLAIM IS TAKEN HERE, not inside the bake below, and it is HELD ALL THE WAY THROUGH
            // INSTALL: the bake, the freshness key and the redirect are one publication, and a second
            // producer arriving between them would leave one run's key stamped over the other's copies.
            // Passed DOWN to the bake (claimHeld: true) so Apply's own bake cannot refuse against itself.
            string[] owned = ProjectBake.OutputDirs(projectRoot, project.Id);
            string contended;
            if (!OutputClaim.Take(owned, out contended)) return contended;
            try { return Applied(project, projectRoot, forBundle, out targets, out how); }
            finally { OutputClaim.Release(owned); }
        }

        /// <summary>The apply itself, under this project's output claim. Private for the same reason
        /// <c>ProjectBake.Baked</c> is: a caller that reached here directly would own nothing.</summary>
        private static string Applied(Morgott.ContentTool.Project.ContentProject project, string projectRoot,
                                      string forBundle, out IList<TargetInstall> targets,
                                      out ApplyDisposition how)
        {
            targets = new List<TargetInstall>();
            how = ApplyDisposition.Refused;
            StringBuilder pre = new StringBuilder();
            string modId = project.Id;
            string patched = ContentToolMain.PatchedDir(modId);

            // The declared targets and the freshness verdict are ONE observation now (Observe above), so
            // the panel and this checkbox read the same `haveAll` rather than each computing it. The census
            // and its case-blindness moved with it, comments and all.
            FreshnessObservation seen = Observe(project, projectRoot);
            List<string> declared = new List<string>(seen.Declared);
            if (!seen.HaveAll)
            {
                pre.AppendLine(seen.CacheDirExists && !seen.KeyMatches
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
                //
                // THE DISPOSITION, NOT THE COUNTS. R37 and R38 come back with ZERO failures - nothing was
                // baked and nothing was wrong - so `patchFailed != 0` alone would fall straight through
                // and install the STALE copies as if this bake had produced them. Counting a refusal as a
                // patch failure instead is no better: that reaches Failed.Add below and blocks the mod's
                // checkbox for the rest of the session over a race nobody caused.
                BakeResult baked = ProjectBake.Bake(projectRoot, true);   // claimHeld: this apply owns it
                pre.AppendLine(baked.Terminal);
                if (baked.How == BakeDisposition.Refused || baked.How == BakeDisposition.Cancelled)
                    return pre.ToString();                                // `how` stays Refused; Failed untouched
                int patchFailed = baked.PatchFailed;
                if (patchFailed != 0)
                {
                    how = ApplyDisposition.BakeFailed;
                    Failed.Add(modId);
                    // No "press Ship again": this same text is printed by the console verb (Run:49) and by
                    // the mod-manager checkbox (Toggle:109), where there is no Ship button to press. The
                    // failures are NAMED above; a caller that has a next step adds its own.
                    return pre.AppendLine(StageText.R35(patchFailed)).ToString();
                }
                // NO PatchCache.Write HERE ANY MORE. The receipt is written by the bake itself, LAST inside
                // its own B5 publication and under the same claim (ProjectBake.Patch), for two reasons: it
                // is the only place the copies and the receipt can be ordered against each other, and a
                // STANDALONE bake used to leave the observation reading `never`, so the dashboard's Verify
                // was refused (R28) over copies that had just been produced - it would have taken an Apply,
                // a change to game state, to make Verify admissible at all.
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
            pre.Append("installing " + copies.Count + " patched copy(ies) as '" + modId + "'\n")
               // THE PER-TARGET ANSWER COMES OUT OF THE INSTALL LOOP, not from a second sample around it.
               // The `wasResident`/`Find` pair that used to sit here measured ONE bundle either side of a
               // loop that installs several; inside, each target is sampled at its own Register, which is
               // the same rule applied per target instead of per press.
               .Append(BundleLive.Install(modId, copies, out targets));
            // UNCONDITIONALLY, once the copies are in. It sat inside the `!haveAll` branch, so the
            // press that arrived with a FRESH patched folder - the common case after a fix elsewhere -
            // installed the copies and left the session's "this one failed" flag standing, and the
            // mod-manager checkbox refused the mod for the rest of the session over a bake that is no
            // longer failing.
            Failed.Remove(modId);
            if (ours)
            {
                // A declared bundle this apply did NOT install - refused by Register, or simply absent from
                // the patched folder - is not in the list, and stays Refused.
                foreach (TargetInstall t in targets)
                    if (string.Equals(t.Bundle, forBundle, StringComparison.OrdinalIgnoreCase))
                    { how = t.Outcome; break; }
            }
            else if (!asked)
            {
                // NO BUNDLE NAMED, so speak for the whole project - CONSERVATIVELY. `how` used to stay at
                // its initial Refused here (it was only ever overwritten inside `if (ours)`), so the
                // console-shaped call reported a refusal that did not happen. Any refusal survives; then any
                // restart-required target, because that is the one the author has to act on; a blanket
                // Redirected only when every target was.
                foreach (TargetInstall t in targets)
                    if (t.Outcome == ApplyDisposition.Refused) { how = ApplyDisposition.Refused; break; }
                    else if (t.Outcome == ApplyDisposition.Resident) how = ApplyDisposition.Resident;
                    else if (how != ApplyDisposition.Resident) how = ApplyDisposition.Redirected;
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
