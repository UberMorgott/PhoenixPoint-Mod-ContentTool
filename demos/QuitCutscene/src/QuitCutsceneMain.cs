using System;
using System.Collections;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Base.Assets.StreamableSystem;
using Base.Core;
using Base.Defs;
using Base.UI.VideoPlayback;
using HarmonyLib;
using PhoenixPoint.Common.Game;
using PhoenixPoint.Home.View;
using PhoenixPoint.Modding;
using UnityEngine;

namespace Morgott.QuitCutscene
{
    /// <summary>
    /// Demo mod: the game plays a clip when the player quits, then exits.
    ///
    /// THE POINT OF THIS MOD IS THE SPLIT, and an author should leave knowing which half cost what:
    ///
    ///   CONTENT  - the clip and its catalog key. NO LOGIC OF OUR OWN and NOTHING WRITTEN INTO THE
    ///              INSTALL: one CatalogLive.Register call and ContentTool serves the file out of
    ///              this mod's folder for the rest of the run. Nothing else in this file is needed
    ///              for the video to EXIST.
    ///   TRIGGER  - this file. Phoenix Point has no shipped path from quit to a cutscene: all 13
    ///              ToCutsceneState call sites are intros, research-complete, faction rewards, the
    ///              marketplace and two console commands, while both quit routes
    ///              (UIModuleMainMenuButtons.OnExitButtonClicked:281-284 and
    ///              UIModulePauseScreen.OnQuitGamePressed:172 -> OnQuitConfirmed -> QuitGameCrt)
    ///              go straight to PhoenixGame.FinishLevelAndQuitGame. A new TRIGGER is behaviour,
    ///              and behaviour needs a hook. That is the whole reason this mod ships a DLL and
    ///              the other demos do not.
    ///
    /// Everything the trigger does is the game's own machinery: HomeScreenView.ToCutsceneState
    /// (HomeScreenView.cs:182) already means "play this, then run that", and the callback it takes is
    /// invoked by UIStateHomeScreenCutscene.OnCancel - which is ALSO what ESC and Submit reach
    /// (OnInputEvent:92-104, gated on IsInterruptible). So skip-and-exit needed no input handling
    /// from us; it needed SkipOnPlayerInput=true on the def (WarmUpPlayer:174) and nothing else.
    /// </summary>
    public class QuitCutsceneMain : ModMain
    {
        /// <summary>Must match ppcontent.json's "id" and the clip's file name: the catalog row this
        /// mod plays is keyed by MD5("&lt;id&gt;/&lt;stem&gt;") - the same derivation ContentTool uses.</summary>
        private const string ModId = "morgott.demo.quitcutscene";
        private const string ClipStem = "quit_outro";

        public override bool CanSafelyDisable => true;

        /// <summary>Set by the patch once the clip has been shown, so the second pass really quits.</summary>
        internal static bool Played;

        /// <summary>ModMain.Instance is an instance property; the patch is static and needs the log.</summary>
        private static QuitCutsceneMain self;

        public override void OnModEnabled()
        {
            self = this;
            Harmony h = (Harmony)HarmonyInstance;
            h.PatchAll(Assembly.GetExecutingAssembly());
            Logger.LogInfo("Q1-content " + Serve());

            // The gate's anti-vacuity arm, and it asks HARMONY rather than trusting that PatchAll was
            // called: "the game exited" is true whether the hook bound or never existed, so a run
            // where nothing bound has to be distinguishable in the log from one where it did.
            MethodInfo target = AccessTools.Method(typeof(PhoenixGame), nameof(PhoenixGame.FinishLevelAndQuitGame));
            Patches info = target == null ? null : Harmony.GetPatchInfo(target);
            bool bound = info != null && info.Prefixes != null && info.Prefixes.Count > 0;
            Logger.LogInfo("Q1-bound " + (bound ? "PASS" : "FAIL") + " PhoenixGame.FinishLevelAndQuitGame prefix " +
                           (bound ? "is installed by " + string.Join(",", OwnersOf(info)) : "IS NOT INSTALLED - quitting will play nothing"));
        }

        /// <summary>
        /// The CONTENT half: ask ContentTool to serve our clip for our key, in memory. Nothing is
        /// written into the install - no Catalog.json edit, no backup, no revert.
        ///
        /// BY REFLECTION on purpose - though a plain reference WOULD resolve: meta.json declares the
        /// dependency and PPModLoader enables a dependency before its dependents, which is what the
        /// weapon and creature demos rely on. The reason to reflect here is VERSION SKEW: ModMeta's
        /// Dependencies carry an id and no minimum version, so an OLDER ContentTool satisfies the
        /// declaration while lacking CatalogLive.Register. Reflection turns that into the logged
        /// "version mismatch" line below instead of a MissingMethodException.
        /// </summary>
        private string Serve()
        {
            string clip = System.IO.Path.Combine(Instance.Entry.Directory, "Content\\Videos\\" + ClipStem + ".webm");
            Type api = Type.GetType("Morgott.ContentTool.Bake.CatalogLive, ContentTool");
            if (api == null) return "VOID ContentTool is not loaded - this mod depends on it and has nothing to play";
            MethodInfo reg = api.GetMethod("Register", BindingFlags.Public | BindingFlags.Static);
            if (reg == null) return "VOID ContentTool has no CatalogLive.Register - version mismatch";
            return (string)reg.Invoke(null, new object[] { KeyFor(ModId, ClipStem), clip });
        }

        private static string[] OwnersOf(Patches info)
        {
            string[] owners = new string[info.Prefixes.Count];
            for (int i = 0; i < owners.Length; i++) owners[i] = info.Prefixes[i].owner;
            return owners;
        }

        /// <summary>
        /// The def the game plays, built at runtime because a def is a bundled asset and this mod
        /// ships no bundle. It carries one thing that matters - the RuntimeKey of the catalog row
        /// ct_video wrote - plus SkipOnPlayerInput, which is what makes ESC work.
        /// Everything else stays at its default; VideoPlaybackController.Setup only complains about a
        /// missing subtitle/audio player when the SOURCE declares one, and this source declares none.
        ///
        /// BUILT BY THE GAME'S OWN FACTORY, not by ScriptableObject.CreateInstance. DefRepository
        /// .CreateRuntimeDef (DefRepository.cs:214) is what the engine itself uses for a def that has
        /// no asset behind it (BaseDef.cs:128 calls it while deserializing) - it stamps a Guid and
        /// registers the def, so the def is a REAL def rather than a loose object that merely has the
        /// right type. A bare CreateInstance leaves Guid AND ResourcePath null, and other mods read
        /// those: TFTV's postfix on UIStateHomeScreenCutscene.EnterState does
        /// `_sourcePlaybackDef.ResourcePath.Contains("Game_Intro_Cutscene")` to implement its
        /// SkipMovies option (TFTV\TFTVUI\Common\Various.cs:124), which is a NullReferenceException on
        /// a null ResourcePath. Hence the explicit ResourcePath below - and it must NOT contain
        /// "Game_Intro_Cutscene", or TFTV would cancel this cutscene the instant it starts.
        /// </summary>
        internal static VideoPlaybackSourceDef BuildDef()
        {
            VideoPlaybackSourceDef def = GameUtl.GameComponent<DefRepository>().CreateRuntimeDef<VideoPlaybackSourceDef>();
            def.name = "QuitCutscene_Runtime";
            def.ResourcePath = "Morgott/QuitCutscene/" + ClipStem;
            def.VideoClipSource = new StreamableVideoClipReference { RuntimeKey = KeyFor(ModId, ClipStem) };
            def.SkipOnPlayerInput = true;
            def.VisibleInCinematicLibarary = false;
            return def;
        }

        /// <summary>
        /// Where the game will actually look for the clip, asked BEFORE the quit is hijacked. This is
        /// the game's own resolution - StreamableAssetsManager.GetStreamingPath, the same call
        /// VideoPlaybackController.WarmUpPlayer:150 makes to fill VideoPlayer.url - so a missing
        /// catalog row shows up here as the NullReferenceException it really is
        /// (StreamableAssetsManager.cs:49 dereferences a location the catalog did not have) instead
        /// of as a silent black screen ten lines later.
        /// Returns null when the source cannot be resolved, and the reason is logged either way.
        /// </summary>
        internal static string Resolve(VideoPlaybackSourceDef def)
        {
            try
            {
                string path = def.VideoClipSource.GetStreamingPath();
                bool exists = !string.IsNullOrEmpty(path) && System.IO.File.Exists(path);
                Say("Q1-src " + (exists ? "PASS" : "FAIL") + " key=" + def.VideoClipSource.RuntimeKey +
                    " resolves to '" + path + "' exists=" + exists);
                return exists ? path : null;
            }
            catch (Exception ex)
            {
                Say("Q1-src FAIL key=" + def.VideoClipSource.RuntimeKey + " is not in the live streamable " +
                    "catalog - ContentTool never injected the row, or a scene load dropped it: " + ex.Message);
                return null;
            }
        }

        /// <summary>The real quit, run exactly once - by whichever of the cutscene's callback and the
        /// watchdog gets here first. Both ALWAYS run (the watchdog is armed unconditionally), so this
        /// guard is not a nicety: it is what lets the deadline exist without cutting a clip that ended
        /// normally. Hijacking a quit and then not quitting is the one failure this mod must not
        /// have.</summary>
        internal static void Quit(PhoenixGame game, string why)
        {
            if (quit) return;
            quit = true;
            Say(why);
            game.FinishLevelAndQuitGame();
        }

        private static bool quit;

        /// <summary>How far past the clip's own length the watchdog waits before quitting anyway, and
        /// the deadline it uses when the player prepared but will not say how long the clip is.</summary>
        private const float Grace = 10f;
        private const float Cap = 120f;

        /// <summary>CatalogText.KeyFor, restated: MD5("&lt;modid&gt;/&lt;stem&gt;"), 32 lowercase hex.
        /// Derived rather than pasted so renaming the clip cannot leave a stale constant behind.</summary>
        internal static string KeyFor(string modId, string videoName)
        {
            using (MD5 h = MD5.Create())
                return BitConverter.ToString(h.ComputeHash(Encoding.UTF8.GetBytes(modId + "/" + videoName)))
                                   .Replace("-", "").ToLowerInvariant();
        }

        /// <summary>
        /// Q1-play: what the player is actually looking at. "The game exited" is true even when
        /// nothing played, so the gate needs the game's OWN VideoPlaybackController to say it is on
        /// our url with a decoded frame count - read off the controller
        /// UIStateHomeScreenCutscene.cs:47 assigns, not off anything this mod set.
        /// </summary>
        internal static void Watch(HomeScreenView view, PhoenixGame game, VideoPlaybackSourceDef def)
        {
            // The measurement must never be able to break the feature it measures: this runs inside
            // the quit prefix, and an exception here would propagate out of FinishLevelAndQuitGame.
            try
            {
                GameObject go = new GameObject("q1_watch");
                UnityEngine.Object.DontDestroyOnLoad(go);
                go.AddComponent<Watcher>().StartCoroutine(Sample(view, game, def, go));
            }
            catch (Exception ex) { Say("Q1-play VOID the watcher threw, the clip is unmeasured: " + ex); }
        }

        private sealed class Watcher : MonoBehaviour { }

        // UnityEngine.Video is read REFLECTIVELY on purpose. Referencing the assembly at compile time
        // made this mod fail to load, and Phoenix Point responded by rewriting MOD_ACTIVATED empty -
        // which silently disabled every other mod too, ContentTool included. Measured 2026-08-13 on
        // UnityEngine.VideoModule, commit 632fba7. THIS is the reference PPModLoader cannot resolve:
        // a Managed\ Unity module ModSDK\ does not ship. The ContentTool reference is not one.
        private static object Player(VideoPlaybackController c)
        {
            if (c == null) return null;
            FieldInfo f = AccessTools.Field(c.GetType(), "VideoPlayer");
            return f == null ? null : f.GetValue(c);
        }

        private static object Get(object o, string prop)
        {
            if (o == null) return "(no player)";
            PropertyInfo p = o.GetType().GetProperty(prop);
            if (p == null) return "(no " + prop + ")";
            try { return p.GetValue(o, null); } catch (Exception ex) { return "(threw " + ex.GetType().Name + ")"; }
        }

        private static bool Flag(object o, string prop) { object v = Get(o, prop); return v is bool && (bool)v; }

        private static double Num(object o, string prop)
        {
            object v = Get(o, prop);
            try { return v == null || v is string ? -1 : Convert.ToDouble(v); } catch { return -1; }
        }

        private static IEnumerator Sample(HomeScreenView view, PhoenixGame game, VideoPlaybackSourceDef def, GameObject go)
        {
            // THE controller, not A controller: UIStateHomeScreenCutscene.cs:28+48 plays through
            // _commonModules.CutscenesPlayer.VideoPlayer and nothing else, so read that one. The old
            // FindObjectOfType could answer about a different VideoPlaybackController entirely and
            // report FAIL for a clip that was playing fine two objects away.
            VideoPlaybackController c = view.CommonModules == null ? null : view.CommonModules.CutscenesPlayer.VideoPlayer;
            string want = def.VideoClipSource.RuntimeKey;
            float t0 = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - t0 < 8f)
            {
                object probe = Player(c);
                if (probe != null && Flag(probe, "isPrepared") && Num(probe, "frameCount") > 0) break;
                yield return null;
            }
            object vp = Player(c);
            bool played = false;
            double length = 0;
            if (vp == null)
                Say("Q1-play FAIL the home screen's CutscenesPlayer has no VideoPlayer - nothing could play");
            else
            {
                bool prepared = Flag(vp, "isPrepared");
                double frames = Num(vp, "frameCount");
                // VideoPlayer.length, seconds - read off the game's own UnityEngine.VideoModule
                // (Double length, verified against the shipped dll, not assumed). It is what turns the
                // watchdog below from a fixed 8s guillotine into a deadline the clip can live inside.
                length = Num(vp, "length");
                played = prepared && frames > 0;
                Say("Q1-play " + (played ? "PASS" : "FAIL") +
                    " key=" + want + " url='" + Get(vp, "url") + "' prepared=" + prepared +
                    " playing=" + Get(vp, "isPlaying") + " frameCount=" + frames +
                    " length=" + length + "s" +
                    " " + Get(vp, "width") + "x" + Get(vp, "height") +
                    " stopped=" + c.IsStopped + " interruptible=" + c.IsInterruptible +
                    " playbackSource=" + (c.PlaybackSource == null ? "(null)" : c.PlaybackSource.name));
            }

            // THE DEADLINE IS UNCONDITIONAL, and that is the whole point of it. A clip that reported
            // isPrepared with frames can still stall, and VideoPlaybackStopped can simply never fire -
            // and then the quit this mod TOOK AWAY from the player never happens, the main menu is gone
            // behind a frozen cutscene, and the game has to be force-killed. Arming the watchdog only
            // when the probe failed (which is what this did) left exactly that case uncovered: the
            // probe is a measurement, not a promise that the clip will end.
            // The probe decides only how LONG the deadline is, never whether there is one. Quit() is
            // idempotent - it flips `quit` under its own guard (see Quit) and returns on every later
            // call - so the normal ending, the cutscene's callback firing first, is untouched.
            // ponytail: the clip's own length plus a flat grace, and one flat cap when the player will
            // not say how long it is. No configurability - this is a demo, not a setting.
            float budget = !played ? 0f : (length > 0 ? (float)length + Grace : Cap);
            Say("Q1-watchdog armed: this quit happens in " + budget.ToString("0.0") + "s at the latest " +
                "whatever the clip does next (clip length=" + length + "s, grace=" + Grace + "s)");
            float armed = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - armed < budget) yield return null;
            UnityEngine.Object.Destroy(go);
            Quit(game, played
                ? "Q1-watchdog the clip's " + budget.ToString("0.0") + "s are up and nothing quit the game; " +
                  "quitting now rather than leaving the player stuck on a cutscene that never ended"
                : "Q1-play the clip never came up within 8s; quitting anyway rather than hanging the game");
        }

        internal static void Say(string msg)
        {
            if (self != null) self.Logger.LogInfo(msg);
            else Debug.Log(msg);
        }
    }

    /// <summary>
    /// ONE patch, on the SHARED call. Both quit entry points - the main menu's exit button and the
    /// in-game escape menu's quit - funnel into PhoenixGame.FinishLevelAndQuitGame, so patching there
    /// covers both and there is no per-entry-point guard to keep in sync.
    /// </summary>
    [HarmonyPatch(typeof(PhoenixGame), nameof(PhoenixGame.FinishLevelAndQuitGame))]
    internal static class QuitPatch
    {
        private static bool Prefix(PhoenixGame __instance)
        {
            if (QuitCutsceneMain.Played) return true;   // the clip is over: this is the real quit

            // ponytail: home screen only. GeoscapeView.ToCutsceneState takes a priority, not a
            // callback (GeoscapeView.cs:672), so there is no shipped "play then continue" on the
            // geoscape - quitting from an in-game escape menu exits with no clip. Upgrade path: give
            // the geoscape its own cutscene state with a completion callback, or route the quit
            // through the main menu first. Not worth it for a demo that shows the seam.
            HomeScreenView view = UnityEngine.Object.FindObjectOfType<HomeScreenView>();
            if (view == null)
            {
                QuitCutsceneMain.Say("Q1-play VOID quit did not come from the home screen (no HomeScreenView) - " +
                                     "nothing was played and nothing was measured; the game quits normally");
                return true;
            }

            QuitCutsceneMain.Played = true;
            VideoPlaybackSourceDef def = QuitCutsceneMain.BuildDef();

            // Ask the game where the clip is BEFORE taking the quit away from it. If the row is not
            // in the live catalog there is nothing to show, and hijacking the quit would only leave
            // the player on a black screen that never exits.
            if (QuitCutsceneMain.Resolve(def) == null)
            {
                QuitCutsceneMain.Say("Q1-play VOID the clip cannot be resolved, so the quit is left alone");
                return true;
            }

            QuitCutsceneMain.Say("Q1-trigger the quit was intercepted; handing the clip to the game's own " +
                                 "HomeScreenView.ToCutsceneState, which quits for real when it ends or when ESC skips it");
            view.ToCutsceneState(def, delegate
            {
                QuitCutsceneMain.Quit(__instance, "Q1-exit the cutscene finished or was skipped; quitting for real now");
            });
            QuitCutsceneMain.Watch(view, __instance, def);
            return false;   // the original quit does NOT run yet - the callback above runs it
        }
    }
}
