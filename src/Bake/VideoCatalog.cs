using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Base.Assets.StreamableSystem;
using Base.Core;
using Base.Defs;
using Base.Serialization;
using Base.UI.VideoPlayback;
using Base.Utils;
using Morgott.ContentTool.Project;
using PhoenixPoint.Common.Game;
using PhoenixPoint.Geoscape.View;
using PhoenixPoint.Home.View;
using UnityEngine;
using UnityEngine.Video;
// Every rule about the catalog's TEXT lives there, UnityEngine-free, so tests\ObjCodecTests can
// compile the same code and prove it against the real shipped Catalog.json without a game launch.
using static Morgott.ContentTool.Bake.CatalogText;

namespace Morgott.ContentTool.Bake
{
    /// <summary>
    /// Video replacement (docs\research-video-replacement.md). NOT route vii: Phoenix Point's 69
    /// cutscenes are LOOSE .webm under StreamingAssets\StreamableCopiedAssets\, in no bundle at all,
    /// resolved by a plain-JSON side catalog - so there is no bundle machinery here on purpose.
    ///
    /// SERVED IN MEMORY, NEVER WRITTEN (mandate M2). A mod's clip stays in the mod's own folder and
    /// <see cref="CatalogLive"/> points the catalog row at it: StreamableAssetsCatalog.AllLocations
    /// is a public array and InitializeCache() a public method, and the manager re-reads the file on
    /// every scene load, so one Harmony postfix re-injects for the whole session. Nothing in the
    /// player's installation is copied into, backed up or edited - and unticking the mod hands the
    /// shipped cutscene back, live, with no restart.
    ///
    /// WHAT USED TO BE HERE, and why it is gone rather than gated (S1-b). `apply` copied a clip into
    /// StreamingAssets\StreamableCopiedAssets and rewrote the game's own Catalog.json, keeping
    /// Catalog.json.ct-backup / .ct-edits beside it; `revert`, `verify` and `selftest` existed only
    /// to serve that writer. All of it is DELETED - a flag would have shipped the violation behind a
    /// different verb. An install that ran the old code still carries those files: `ct_video status`
    /// DETECTS them read-only and names the one sanctioned repair (Steam -> Phoenix Point ->
    /// Properties -> Installed Files -> "Verify integrity of game files"). They are never restored
    /// from here, because a restore is itself a write into the player's game.
    ///
    /// Which shape an entry asks for is decided by the declaration, never by a mode the author
    /// picks: naming a shipped clip in "asset" replaces that row, naming none adds a row keyed by
    /// <see cref="KeyFor"/>. The author puts that printed RuntimeKey in their own
    /// VideoPlaybackSourceDef; defs are the mod author's job, the clip and the row are ours.
    /// </summary>
    internal static class VideoCatalog
    {
        internal static string Run(string[] args)
        {
            string verb = args != null && args.Length > 0 ? args[0].ToLowerInvariant() : "status";
            switch (verb)
            {
                case "apply":
                case "verify":
                case "revert":
                case "selftest":
                    return BundleClaims.Removed("ct_video", verb,
                        "Video is LIVE now: 'ct_video live [project]' serves a project's clips out of " +
                        "the mod's own folder for this session, enabling the mod does the same on the " +
                        "shipping path, and disabling it hands the shipped cutscene straight back. " +
                        "Nothing is copied into StreamingAssets, so there is no Catalog.json edit to " +
                        "verify, revert or self-test. 'ct_video status' and 'ct_video resolve <key>' " +
                        "show what the running game resolves.");
                case "status": return Status();
                case "defs": return Defs(args != null && args.Length > 1 ? string.Join(" ", args, 1, args.Length - 1) : null);
                case "live": return Live(args != null && args.Length > 1 ? args[1] : null);
                // What the RUNNING game resolves a key to, right now. The measurement primitive for
                // the in-memory route: the file on disk still says one thing, the live catalog says
                // another, and only this says which one the game will actually open.
                // Does the DECODER open what a key resolves to? `resolve` proves the string; this
                // proves the file behind it, which is the whole question for a ".."-escaping path.
                case "open":
                    {
                        if (args == null || args.Length < 2) return "usage: ct_video open <RuntimeKey>";
                        string url = LiveResolve(args[1]);
                        if (url == null) return "ct_video open: no StreamableAssetsManager";
                        GameObject go = new GameObject("ct_open");
                        UnityEngine.Object.DontDestroyOnLoad(go);
                        go.AddComponent<OpenArm>().Begin(args[1], url);
                        return "ct_video open armed on " + url;
                    }
                case "resolve":
                    if (args == null || args.Length < 2) return "usage: ct_video resolve <RuntimeKey>";
                    return "ct_video resolve " + args[1] + " -> " + (LiveResolve(args[1]) ?? "(no manager)");
                case "play": return Play(args != null && args.Length > 1 ? string.Join(" ", args, 1, args.Length - 1) : null);
                // The quit lever for gate Q1 (demos\QuitCutscene). It calls exactly what both of the
                // game's quit buttons call - PhoenixGame.FinishLevelAndQuitGame - so a gate can reach
                // the quit path without a human clicking, and whatever is patched onto it runs the
                // same way it would for a player. Nothing of the QuitCutscene mod is referenced here.
                case "quit":
                    GameUtl.GameComponent<PhoenixGame>().FinishLevelAndQuitGame();
                    return "ct_video quit: called PhoenixGame.FinishLevelAndQuitGame (the same call both quit buttons make)";
                default: return "usage: ct_video live [project] | status | defs | resolve <key> | open <key> | play <defname> | quit";
            }
        }

        // ---------------------------------------------------------------- paths

        /// <summary>The game's own constant, not ours: StreamableAssetsManager.StreamingCatalog (:15).
        /// READ ONLY - it is named so a refusal can tell the player which file to verify.</summary>
        private static string Catalog =>
            Application.streamingAssetsPath + "/" + StreamableAssetsCatalog.CatalogPath;
        /// <summary>Artifacts an OLDER ContentTool left in the install. Detected, never written.</summary>
        private static string Backup => Catalog + ".ct-backup";
        private static string EditsFile => Catalog + ".ct-edits";

        // ---------------------------------------------------------------- verbs

        /// <summary>
        /// What is serving in memory, plus read-only detection of what an OLDER ContentTool wrote
        /// INTO the installation. Never repaired here - a restore is itself a write - so this names
        /// the files and the one sanctioned repair, and nothing more.
        /// </summary>
        private static string Status()
        {
            StringBuilder log = new StringBuilder("live video: " + Project.ContentState.Mods(Route) +
                " content project(s) serving clips in memory; nothing in the installation is written");
            List<Rec> legacy = LegacyEdits();
            if (legacy.Count == 0 && !File.Exists(Backup))
                return log.Append("\nlegacy: none - there is no " + EditsFile + " and no " + Backup +
                                  ", so nothing an older ContentTool wrote is left in this installation").ToString();

            List<string> rows = new List<string>();
            foreach (Rec r in legacy) rows.Add("row '" + r.Key + "' = " + r.Path);
            if (rows.Count == 0) rows.Add("(no ledger, but " + Backup + " is still there)");
            return log.Append('\n')
                      .Append(BundleClaims.LegacyRefusal("(any mod)", rows, EditsFile, Catalog, "video row"))
                      .ToString();
        }

        /// <summary>
        /// WHICH def names WHICH catalog row - the question an author has to answer before writing
        /// "asset" in ppcontent.json, and the one this tool could only guess at from file names.
        /// The game itself enumerates these defs the same way at MenuLevelController.cs:154, so this
        /// reads its data, not ours.
        ///
        /// Two things it prints that nothing else can:
        ///   * DANGLING - a def whose RuntimeKey has NO row in the catalog. That slot plays nothing
        ///     today (VideoPlayer.Prepare() fails silently, which V1-missing measures), so it is a
        ///     video the game is already wired to play and simply has no file for.
        ///   * the def GeoscapeView.IntroCinematicDef holds, when a geoscape is loaded - the video a
        ///     NEW CAMPAIGN plays (GeoLevelController.cs:741, gated on instanceData == null). Read
        ///     off the field, so which row it is stops being a name guess.
        /// Dev workbench only: nothing here is on a shipping path.
        /// </summary>
        private static string Defs(string saveName)
        {
            if (saveName != null)
            {
                // GeoscapeView only exists once a geoscape is live, and there is no console lever that
                // starts a NEW campaign, so the field is read off a loaded campaign instead. Same shape
                // MissionGate uses for its tactical save, mirrored: a GEOSCAPE save is what is wanted
                // here, and a tactical one is refused rather than loaded into the wrong scene.
                GameObject g = new GameObject("ct_video_defs");
                UnityEngine.Object.DontDestroyOnLoad(g);
                g.AddComponent<DefsArm>().Begin(saveName);
                return "ct_video defs armed on save '" + saveName + "' - the rows print from the runner";
            }
            return DefsNow();
        }

        /// <summary>Loads a GEOSCAPE save, waits for the view, then prints the same rows.</summary>
        private sealed class DefsArm : MonoBehaviour
        {
            private const float LoadBudgetSeconds = 420f;
            private string saveName, refusal;

            internal void Begin(string name)
            {
                saveName = name;
                Dev.AsyncGate.Pending++;
                StartCoroutine(Gate());
            }

            private IEnumerator Gate()
            {
                StringBuilder log = new StringBuilder();
                try
                {
                    GameUtl.GameComponent<TimeSource>().Timing.Start(Load(saveName));
                    float t0 = Time.realtimeSinceStartup;
                    while (Time.realtimeSinceStartup - t0 < LoadBudgetSeconds)
                    {
                        if (refusal != null) break;
                        if (UnityEngine.Object.FindObjectOfType<GeoscapeView>() != null) break;
                        yield return new WaitForSeconds(1f);
                    }
                    if (refusal != null) { log.Append("ct_video defs VOID " + refusal); yield break; }
                    if (UnityEngine.Object.FindObjectOfType<GeoscapeView>() == null)
                    {
                        log.Append("ct_video defs VOID no GeoscapeView came up within " + LoadBudgetSeconds +
                                   "s of loading '" + saveName + "' - the intro def was NOT measured");
                        yield break;
                    }
                    log.Append(DefsNow());
                }
                finally
                {
                    ContentToolMain.Say(log.ToString());
                    Dev.AsyncGate.Pending--;
                    Destroy(gameObject);
                }
            }

            private IEnumerator<NextUpdate> Load(string name)
            {
                // The second argument is what MissionGate's LIST uses; without it the enumeration came
                // back empty on Instance2 and a save that exists on disk read as "no savegame named".
                ByRef<List<SavegameMetaData>> all = new ByRef<List<SavegameMetaData>>();
                yield return Timing.Current.Call(
                    GameUtl.GameComponent<SerializationComponent>().GetSavegames(all, true));
                if (all.Value != null)
                    ContentToolMain.Say("ct_video defs: " + all.Value.Count + " savegame(s): " +
                                        string.Join(", ", all.Value.Select(m => "'" + m.Name + "'").ToArray()));
                List<SavegameMetaData> hits = (all.Value ?? new List<SavegameMetaData>())
                    .Where(m => string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase)).ToList();
                if (hits.Count == 0) { refusal = "no savegame named '" + name + "'"; yield break; }
                if (hits.Count > 1)
                {
                    refusal = hits.Count + " savegames answer to '" + name + "': " +
                              string.Join(", ", hits.Select(h => h.Path).ToArray());
                    yield break;
                }
                PPSavegameMetaData pp = hits[0] as PPSavegameMetaData;
                if (pp == null) { refusal = "'" + name + "' carries no PPSavegameMetaData"; yield break; }
                if (pp.IsTacticalSave)
                {
                    refusal = "'" + name + "' declares IsTacticalSave=True (saveType=" + pp.SaveType +
                              ") - it lands in a mission, where there is no GeoscapeView to read";
                    yield break;
                }
                if (!pp.IsLoadable()) { refusal = "'" + name + "' declares saveType=" + pp.SaveType + " (not loadable)"; yield break; }
                ContentToolMain.Say("ct_video defs: loading geoscape save '" + pp.Name + "' v." + pp.Version +
                                    " (IsTacticalSave=False, saveType=" + pp.SaveType + ")");
                GameUtl.GameComponent<PhoenixGame>().FinishLevelAndLoadGame(pp);
            }
        }

        private static string DefsNow()
        {
            if (!File.Exists(Catalog)) return "ct_video defs: no catalog at " + Catalog;
            string json = File.ReadAllText(Catalog);

            DefRepository repo = GameUtl.GameComponent<DefRepository>();
            if (repo == null) return "ct_video defs: no DefRepository yet - run this from the main menu";

            StringBuilder log = new StringBuilder();
            int dangling = 0, total = 0;
            foreach (VideoPlaybackSourceDef d in repo.GetAllDefs<VideoPlaybackSourceDef>())
            {
                total++;
                string key = d.VideoClipSource != null ? d.VideoClipSource.RuntimeKey : null;
                string path = string.IsNullOrEmpty(key) ? null : PathOf(json, key);
                if (path == null) dangling++;
                log.AppendLine("  " + d.name + "\t" + (key ?? "(no key)") + "\t" +
                               (path ?? "DANGLING - no catalog row carries that key"));
            }
            log.AppendLine("ct_video defs: " + total + " VideoPlaybackSourceDef(s), " + dangling +
                           " with no catalog row");

            // The new-campaign intro, off the field rather than off a file name. Only readable once a
            // geoscape exists (load_game a campaign save); at the main menu there is no GeoscapeView.
            GeoscapeView gv = UnityEngine.Object.FindObjectOfType<GeoscapeView>();
            if (gv == null)
                log.Append("new-campaign intro: NOT MEASURED - no GeoscapeView in the scene " +
                           "(load_game a campaign save first, then re-run)");
            else if (gv.IntroCinematicDef == null)
                log.Append("new-campaign intro: GeoscapeView.IntroCinematicDef is NULL - this build " +
                           "plays no video on a new campaign");
            else
            {
                string k = gv.IntroCinematicDef.VideoClipSource != null
                         ? gv.IntroCinematicDef.VideoClipSource.RuntimeKey : null;
                log.Append("new-campaign intro: GeoscapeView.IntroCinematicDef = " +
                           gv.IntroCinematicDef.name + " key=" + (k ?? "(none)") + " row=" +
                           (string.IsNullOrEmpty(k) ? "(none)" : (PathOf(json, k) ?? "DANGLING")));
            }
            return log.ToString();
        }

        /// <summary>
        /// V1-play - the EFFECT arm. <see cref="Verify"/> proves the game's resolver hands our path
        /// back and that a decoder opens the file; this proves the GAME'S OWN cutscene player is the
        /// one showing it. It drives the same two lines the shipped console command
        /// play_home_cutscene drives (MenuLevelController.cs:150-168) - find the def by name, then
        /// HomeScreenView.ToCutsceneState - and then reads the identity off
        /// VideoPlaybackController.VideoPlayer, the field UIStateHomeScreenCutscene assigns
        /// (UIStateHomeScreenCutscene.cs:47). Nothing here is invented playback: the trigger is the
        /// game's, the player is the game's, and only the measurement is ours.
        ///
        /// The expectation is taken from the LIVE catalog for that def's own key, so the arm cannot
        /// pass on a clip the def does not name. Dev workbench only - the shipped mod is the .webm
        /// and the catalog row.
        /// </summary>
        private static string Play(string namePrefix)
        {
            if (string.IsNullOrEmpty(namePrefix)) return "usage: ct_video play <VideoPlaybackSourceDef name prefix>   (ct_video defs lists them)";
            if (!File.Exists(Catalog)) return "V1-play VOID no catalog at " + Catalog;

            DefRepository repo = GameUtl.GameComponent<DefRepository>();
            if (repo == null) return "V1-play VOID no DefRepository - run this from the main menu";
            HomeScreenView home = UnityEngine.Object.FindObjectOfType<HomeScreenView>();
            if (home == null) return "V1-play VOID no HomeScreenView - this arm plays in the home screen, run it there";

            VideoPlaybackSourceDef def = null;
            foreach (VideoPlaybackSourceDef d in repo.GetAllDefs<VideoPlaybackSourceDef>())
                if (d.name.StartsWith(namePrefix, StringComparison.InvariantCultureIgnoreCase)) { def = d; break; }
            if (def == null) return "V1-play VOID no VideoPlaybackSourceDef whose name starts with '" + namePrefix + "'";

            string key = def.VideoClipSource != null ? def.VideoClipSource.RuntimeKey : null;
            if (string.IsNullOrEmpty(key)) return "V1-play VOID " + def.name + " names no clip at all";
            string row = PathOf(File.ReadAllText(Catalog), key);

            GameObject go = new GameObject("ct_v1_play");
            UnityEngine.Object.DontDestroyOnLoad(go);
            go.AddComponent<PlayArm>().Begin(home, def, key, row);
            return "V1-play armed on " + def.name + " (key " + key + ") - the arm prints from the runner";
        }

        /// <summary>The V1-play runner: trigger, wait for the game's own player, measure, stop.</summary>
        private sealed class PlayArm : MonoBehaviour
        {
            private HomeScreenView home;
            private VideoPlaybackSourceDef def;
            private string key, row;

            internal void Begin(HomeScreenView h, VideoPlaybackSourceDef d, string k, string r)
            {
                home = h; def = d; key = k; row = r;
                Dev.AsyncGate.Pending++;
                StartCoroutine(Gate());
            }

            private IEnumerator Gate()
            {
                StringBuilder log = new StringBuilder();
                int fail = 0;
                try
                {
                    if (row == null)
                    {
                        // A def with no catalog row plays NOTHING today. That is not a failure of this
                        // arm - it is the measurement, and it is what makes filling that row provable.
                        log.Append("V1-play " + def.name + " key " + key +
                                   " has NO catalog row: the game is wired to play a clip it has no file for");
                        yield break;
                    }

                    home.ToCutsceneState(def);

                    VideoPlaybackController ctl = null;
                    float t0 = Time.realtimeSinceStartup;
                    while (Time.realtimeSinceStartup - t0 < PrepareTimeout)
                    {
                        ctl = UnityEngine.Object.FindObjectOfType<VideoPlaybackController>();
                        if (ctl != null && ctl.VideoPlayer != null && ctl.VideoPlayer.isPrepared) break;
                        yield return null;
                    }

                    if (ctl == null || ctl.VideoPlayer == null)
                    {
                        fail += Check(log, "V1-play", false, "no VideoPlaybackController came up within " +
                                      PrepareTimeout + "s - nothing was measured");
                    }
                    else
                    {
                        VideoPlayer vp = ctl.VideoPlayer;
                        string url = (vp.url ?? "").Replace('\\', '/');
                        // Two identities, same rule as V1-url/V1-frames: the path says the def resolved
                        // through the catalog we wrote, the frame count says the DECODER opened that
                        // file rather than a string being assigned.
                        fail += Check(log, "V1-play-url", vp.isPrepared && url.EndsWith(row, StringComparison.OrdinalIgnoreCase),
                            "the game's own cutscene player, driven through HomeScreenView.ToCutsceneState(" +
                            def.name + "), is on " + (string.IsNullOrEmpty(url) ? "(no url)" : url) +
                            " (expected it to end with " + row + "), prepared=" + vp.isPrepared);
                        fail += Check(log, "V1-play-frames", vp.isPrepared && vp.frameCount > 0,
                            "it decoded frameCount=" + vp.frameCount + " " + vp.width + "x" + vp.height +
                            ", playing=" + vp.isPlaying);
                    }

                    if (ctl != null) ctl.Stop();
                    home.ResetViewState();
                    log.Append(fail == 0 ? "ct_video play: ALL PASS" : "ct_video play: " + fail + " FAILURE(S)");
                }
                finally
                {
                    Debug.Log(log.ToString());
                    Dev.AsyncGate.Pending--;
                    UnityEngine.Object.Destroy(gameObject);
                }
            }
        }

        /// <summary>
        /// THE SHIPPED-MOD PATH. Every content project the player has switched on serves its own clips
        /// at mod-enable: no console command, no install-time step, nothing written into the game.
        ///
        /// Two sources, and they are gated differently on purpose (the rulings of gate G1):
        ///   * ContentTool's own subfolders (Mods\ContentTool\Sample, ...) - our content under our own
        ///     switch, so the manager has nothing separate to say about them.
        ///   * SIBLING mod folders under Mods\ - each one a mod the player can switch off, so each one
        ///     goes through <see cref="Project.ContentMods.Enabled"/> and is refused by name when off.
        /// Called from the DEFERRED pass, one frame after OnModEnabled, because a mod that depends on
        /// us still reads Enabled=false while the startup enable pass is running.
        /// </summary>
        internal static string LiveAll()
        {
            StringBuilder log = new StringBuilder();
            int skipped = 0, gated;

            string own = ContentToolMain.ModDir;
            if (!string.IsNullOrEmpty(own) && Directory.Exists(own))
                foreach (string dir in Directory.GetDirectories(own))
                {
                    if (!File.Exists(Path.Combine(dir, Project.ContentMods.Manifest))) continue;
                    string sub = Path.GetFileName(dir);
                    // A demo that has since become its own mod may have left a copy inside us. That
                    // copy is invisible to the manager, so serving it would apply content the player
                    // cannot switch off - the installed mod beside us is the only one that counts.
                    if (Project.ContentMods.Sibling(own, sub) != null)
                    {
                        skipped++;
                        log.AppendLine("  " + sub + ": skipped, a stale copy inside Mods\\ContentTool - " +
                                       "Mods\\" + sub + " is the installed mod and the manager governs it");
                        continue;
                    }
                    // Through LiveMod, like a sibling mod: our own projects are claimed too, so the
                    // summary below has ONE list to read and re-running is a no-op for them as well.
                    string own2 = LiveMod(dir);
                    if (own2 != null) log.AppendLine(own2);
                }

            foreach (string dir in Project.ContentMods.Enabled(
                         own, Project.ContentMods.Manifest, Project.ModRoster.Build(), log, out gated))
            {
                // Null = the runtime enable hook already served this mod, one frame earlier.
                string line = LiveMod(dir);
                if (line != null) log.AppendLine(line);
            }
            skipped += gated;

            // Counts including 0, always - an empty block reads like success and is not. The served
            // count is read from the LEDGER, for the same reason ct_sound's is: the work has two
            // entry points and a counter local to this loop reports 0 for everything the runtime
            // toggle already did.
            return "ct_video: " + Project.ContentState.Mods(Route) + " content project(s) serving " +
                   "in memory, " + skipped + " skipped" +
                   (log.Length > 0 ? Environment.NewLine + log.ToString().TrimEnd() : "");
        }

        /// <summary>
        /// Serve a project's declared clips IN MEMORY, out of the project's own folder
        /// (<see cref="CatalogLive"/>). Nothing in the install is written - this is
        /// <see cref="Apply"/> minus the file copy, the backup, the ledger and the revert.
        /// A mod with its own DLL calls CatalogLive.Register directly instead.
        /// </summary>
        private static string Live(string projectName)
        {
            return LiveAt(ContentToolMain.ProjectDir(projectName));
        }

        /// <summary>The route name this file owns in <see cref="Project.ContentState"/>.</summary>
        internal const string Route = "video";

        /// <summary>
        /// ONE mod's clips, on the SHIPPED path. Claimed, so the startup scan and the runtime enable
        /// hook cannot both register the same rows; the console's own `ct_video live` deliberately
        /// does NOT claim, because re-serving an edited clip is the author's whole loop.
        /// </summary>
        internal static string LiveMod(string modDir)
        {
            // The runtime toggle reaches here for ANY content mod, including a sound-only one.
            if (!File.Exists(Path.Combine(modDir, Project.ContentMods.Manifest))) return null;
            if (!Project.ContentState.Claim(modDir, Route)) return null;
            // A claim standing over NOTHING is worse than no claim: it makes the summary count this
            // mod as serving and it makes the next scan or toggle a no-op, so a run that installed no
            // row - or threw halfway - can never be retried. Hand the route back in both cases.
            int served = 0;
            try { return LiveAt(modDir, out served); }
            finally { if (served == 0) Project.ContentState.Release(modDir, Route); }
        }

        /// <summary>
        /// The inverse, in the same session: every row this mod registered goes back to what the game
        /// said before we touched it, and a row we invented leaves the catalog. The before/after pair
        /// is MEASURED through the game's own resolver and printed, so the log says what happened
        /// rather than that something happened.
        /// </summary>
        internal static string UndoMod(string modDir)
        {
            List<string> keys = Project.ContentState.Release(modDir, Route);
            if (keys.Count == 0) return null;
            StringBuilder log = new StringBuilder();
            foreach (string key in keys)
            {
                string before = LiveResolve(key);
                string was = CatalogLive.Unregister(key);
                log.AppendLine("  " + key + "\n    before: " + (before ?? "(no manager)") +
                               "\n    after:  " + (LiveResolve(key) ?? "(no manager)") +
                               "\n    restored to " + (was ?? "(nothing to restore)"));
            }
            return "  " + new DirectoryInfo(modDir).Name + ": " + keys.Count +
                   " clip(s) handed back to the game, live, no restart" +
                   Environment.NewLine + log.ToString().TrimEnd();
        }

        internal static string LiveAt(string root)
        {
            int served;
            return LiveAt(root, out served);
        }

        /// <summary><paramref name="served"/> = rows that ACTUALLY reached the live catalog, so the
        /// caller holding a claim knows whether it has anything to hand back.</summary>
        internal static string LiveAt(string root, out int served)
        {
            served = 0;
            if (!File.Exists(Path.Combine(root, Project.ContentMods.Manifest)))
                return "REFUSED: no ppcontent.json in " + root;
            ContentProject.Declared p = ContentProject.LoadDeclared(root);
            string json = File.Exists(Catalog) ? File.ReadAllText(Catalog) : "";
            string name = new DirectoryInfo(root).Name;

            StringBuilder log = new StringBuilder();
            int n = 0, refused = 0;
            foreach (ShippedReplacement r in p.Replace)
            {
                if (string.IsNullOrEmpty(r.video)) continue;
                ImportedVideo v = FindVideo(p, r.video);
                if (v == null) { log.AppendLine("SKIP '" + r.video + "' is not a .webm/.mp4/.mov under Content\\Videos\\"); refused++; continue; }

                // Same rule as the declaration: naming a shipped clip REPLACES that row, naming none ADDS.
                string key;
                if (string.IsNullOrEmpty(r.asset)) key = KeyFor(p.Id, v.Name);
                else
                {
                    string why;
                    key = FindKey(json, r.asset, out why);
                    if (key == null) { log.AppendLine("SKIP " + why); refused++; continue; }
                }
                string before = LiveResolve(key);
                string note = CatalogLive.Register(key, v.Path);
                // Register REFUSES rather than throws (it runs as a Harmony postfix), so its refusal
                // has to be read: recording a row it did not install made the summary say "serving"
                // over a clip the game never sees, and made the undo hand back a row nobody replaced.
                if (note != null && note.StartsWith("REFUSED", StringComparison.Ordinal))
                {
                    log.AppendLine("  " + key + "\n    " + note);
                    refused++; continue;
                }
                // A no-op unless this mod is CLAIMED (the shipped path); the console verb records
                // nothing, because nothing claimed it and there is nothing to hand back.
                Project.ContentState.Served(root, Route, key);
                log.AppendLine("  " + key + "\n    before: " + (before ?? "(no manager)") +
                               "\n    after:  " + (LiveResolve(key) ?? "(no manager)") + "\n    " + note);
                n++;
            }
            // ONE line per mod, first and unconditional, so a modder reads at a glance that enabling
            // the mod was enough - and reads the reason on the same line when it was not.
            served = n;
            StringBuilder head = new StringBuilder("  " + name + ": " + n +
                " clip(s) served in memory from " + root +
                (refused > 0 ? ", " + refused + " refused/skipped" : "") +
                "; nothing in the install was written");
            if (log.Length > 0) head.Append(Environment.NewLine).Append(log.ToString().TrimEnd());
            return head.ToString();
        }

        // The "NOT applied by enabling this mod - it declares a route that WRITES YOUR GAME INSTALL"
        // notice that stood here is DELETED, not reworded: both routes it warned about now run on the
        // checkbox (ModRoster.AfterSetEnabled -> Route7.Toggle, which applies AND undoes them), and
        // route vii applies LIVE without writing a byte into the install (BundleLive). Every clause
        // of it had become false, and it printed two lines above the redirect that had just applied
        // the same mod. The route that still needs a restart says so itself, on its own line
        // (Route7.CatalogApply).

        /// <summary>Opens whatever a key resolves to and prints the decoder's own answer.</summary>
        private sealed class OpenArm : MonoBehaviour
        {
            internal void Begin(string key, string url)
            {
                Dev.AsyncGate.Pending++;
                StartCoroutine(Run(key, url));
            }

            private IEnumerator Run(string key, string url)
            {
                Clip c = new Clip();
                yield return Open(url, c);
                ContentToolMain.Say("V1-open " + (c.Frames > 0 ? "PASS" : "FAIL") + " " + key +
                                    " -> " + url + " decodes as " + c);
                Dev.AsyncGate.Pending--;
                UnityEngine.Object.Destroy(gameObject);
            }
        }

        private static string LiveResolve(string key)
        {
            StreamableAssetsManager m = StreamableAssetsManager.Instance;
            if (m == null) return null;
            return m.GetStreamingPath(new StreamableAssetReference { RuntimeKey = key }).Replace('\\', '/');
        }

        /// <summary>What a decoder said about one url. Frames &lt;= 0 means it never opened.</summary>
        private sealed class Clip
        {
            internal long Frames = -1;
            internal int Width, Height;
            internal string Error;
            public override string ToString()
            {
                return Frames > 0 ? "frameCount=" + Frames + " " + Width + "x" + Height
                                  : "NOT PREPARED (" + (Error ?? "timed out") + ")";
            }
        }

        private const float PrepareTimeout = 30f;

        /// <summary>
        /// Prepares one url through Unity's own VideoPlayer and reads back what the decoder found.
        /// APIOnly render mode: the gate wants the numbers, not a picture on a camera that may not
        /// exist in this scene. Audio is off - PP drives cutscene sound through Wwise anyway
        /// (VideoPlaybackController.cs:97-101), so a clip's own track is not part of the question.
        /// </summary>
        private static IEnumerator Open(string url, Clip result)
        {
            GameObject go = new GameObject("ct_v1_open");
            VideoPlayer vp = go.AddComponent<VideoPlayer>();
            vp.playOnAwake = false;
            vp.renderMode = VideoRenderMode.APIOnly;
            vp.audioOutputMode = VideoAudioOutputMode.None;
            vp.source = VideoSource.Url;
            vp.url = url;
            // errorReceived is the ONLY channel a bad url reports on - Prepare() itself is void and
            // isPrepared simply never turns true.
            vp.errorReceived += (VideoPlayer p, string msg) => result.Error = msg;
            vp.Prepare();

            float t0 = Time.realtimeSinceStartup;
            while (!vp.isPrepared && result.Error == null && Time.realtimeSinceStartup - t0 < PrepareTimeout)
                yield return null;

            if (vp.isPrepared)
            {
                result.Frames = (long)vp.frameCount;
                result.Width = (int)vp.width;
                result.Height = (int)vp.height;
            }
            UnityEngine.Object.Destroy(go);
        }

        // ---------------------------------------------------------------- the LEGACY sidecar record

        /// <summary>
        /// What an OLDER ContentTool wrote into the game's own Catalog.json ledger. READ ONLY: this
        /// file no longer writes it, and it will not rewrite the catalog to undo it either - see
        /// <see cref="Status"/>.
        /// </summary>
        private static List<Rec> LegacyEdits()
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

        private static ImportedVideo FindVideo(ContentProject.Declared p, string name)
        {
            foreach (ImportedVideo v in p.Videos)
                if (string.Equals(v.Name, name, StringComparison.OrdinalIgnoreCase)) return v;
            return null;
        }

        private static int Check(StringBuilder log, string gate, bool ok, string detail)
        {
            log.AppendLine(gate + (ok ? " PASS " : " FAIL ") + detail);
            return ok ? 0 : 1;
        }

    }
}
