using System;
using System.IO;
using System.Linq;
using Base.Defs;
using Base.UI.VideoPlayback;
using PhoenixPoint.Modding;
using UnityEngine;

namespace Morgott.Demo.IntroVideo
{
    /// <summary>
    /// The SUBTITLE third of this demo, and the only third that costs a line of code.
    ///
    /// A cutscene in Phoenix Point is THREE separate assets held together by one def
    /// (Base.UI.VideoPlayback.VideoPlaybackSourceDef):
    ///
    ///   VideoClipSource  -> a loose .webm, played through VideoPlayer.url
    ///                       (VideoPlaybackController.WarmUpPlayer:150). A FILE, so a content mod
    ///                       replaces it with no code at all - ppcontent.json "replace".
    ///   AudioSource      -> a VideoSoundDef, i.e. a Wwise EVENT and its bank, posted by
    ///                       VideoSound.Play:50. Also a file underneath (the media .wem), so also
    ///                       no code - ppcontent.json "sounds".
    ///   Subtitles        -> a TextAsset FIELD ON THE DEF, handed to SubtitlePlayer.SubtitleFile in
    ///                       WarmUpPlayer:163. Not a file the game looks up by name, not a catalog
    ///                       row - a reference stored inside a shipped def. There is nothing on
    ///                       disk to overwrite, so the only way to put ours there is to write the
    ///                       field, and writing a field is behaviour.
    ///
    /// That is the whole lesson of this file, and it is the same seam QuitCutscene and AddUiSounds
    /// show from the other side: content is free, behaviour costs a DLL.
    ///
    /// The write itself uses the game's OWN mod hook rather than Harmony:
    /// ModMain.ApplyDefRepoPatches (ModMain.cs:66) is invoked from ModManager.ApplyDefPatches:673,
    /// which GeoLevelController reaches at line 523 - well before it plays the intro at line 741.
    /// So there is no patch to bind, no ordering to guess, and it re-applies on every level start.
    /// </summary>
    public class IntroVideoMain : ModMain
    {
        /// <summary>The def GeoscapeView.IntroCinematicDef points at - the cutscene a NEW CAMPAIGN
        /// plays (GeoLevelController.cs:741, `instanceData == null`). Read off the live field, not
        /// guessed from a file name; see README.md.</summary>
        private const string IntroDef = "PP_Intro_Cutscene";

        private const string Srt = @"Content\Subtitles\campaign_intro.srt";

        /// <summary>What the shipped def carried before we touched it, so disabling the mod really
        /// puts it back rather than leaving our lines on a stock cutscene.</summary>
        private TextAsset shipped;
        private VideoPlaybackSourceDef patched;

        public override bool CanSafelyDisable => true;

        public override void ApplyDefRepoPatches(DefRepository defRepo)
        {
            try { Apply(defRepo); }
            catch (Exception ex) { Logger?.LogError("IntroVideo: " + ex); }
        }

        public override void OnModDisabled()
        {
            if (patched == null) return;
            patched.Subtitles = shipped;
            patched = null;
        }

        private void Apply(DefRepository defRepo)
        {
            VideoPlaybackSourceDef[] all = defRepo.GetAllDefs<VideoPlaybackSourceDef>().ToArray();
            VideoPlaybackSourceDef def = all.FirstOrDefault(d => d.name == IntroDef);
            if (def == null)
            {
                Logger?.LogError("IntroVideo: FAIL no def named " + IntroDef + " among " + all.Length +
                                 " VideoPlaybackSourceDef(s) - the subtitle third is off, the video and " +
                                 "sound thirds are unaffected because they need no def at all");
                return;
            }

            string path = Path.Combine(Instance.Entry.Directory, Srt);
            if (!File.Exists(path)) { Logger?.LogError("IntroVideo: FAIL no subtitle file at " + path); return; }

            // The shipped value is captured ONCE. ApplyDefRepoPatches runs again on every level
            // start, and re-capturing would record OUR asset as the thing to restore.
            if (patched == null) shipped = def.Subtitles;

            // Unity 2019.4 (this build: UnityPlayer.dll 2019.4.31) does have the public
            // TextAsset(string) constructor - verified against the shipped
            // UnityEngine.CoreModule.dll, whose TextAsset carries a public .ctor taking one
            // ELEMENT_TYPE_STRING. So no bundle is needed to carry a text asset.
            // HideAndDontSave because this object belongs to no scene and must survive every load.
            TextAsset ours = new TextAsset(File.ReadAllText(path)) { name = "campaign_intro_srt" };
            ours.hideFlags = HideFlags.HideAndDontSave;
            def.Subtitles = ours;
            patched = def;

            // The one line that also answers the question this demo could not answer offline:
            // whether the geoscape cutscene prefab has a SubtitlePlayer wired at all. If the
            // SHIPPED def already carried a subtitle asset, the game was already showing subtitles
            // on this cutscene, so the player is wired and ours will show too.
            Logger?.LogInfo("IntroVideo: subtitles ON " + IntroDef + " - " + ours.text.Length + " chars from " +
                            Srt + "; the shipped def carried " +
                            (shipped == null
                                ? "NONE (this cutscene had no subtitles; if nothing appears, the prefab's " +
                                  "VideoPlaybackController.SubtitlesPlayer is unset - Setup:76 will have logged it)"
                                : "'" + shipped.name + "' (" + shipped.text.Length + " chars), so the subtitle " +
                                  "player is wired and ours replaces it"));
        }
    }
}
