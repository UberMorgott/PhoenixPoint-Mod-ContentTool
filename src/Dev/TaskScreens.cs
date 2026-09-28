using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Morgott.ContentTool.Bake;
using Morgott.ContentTool.Doctor;
using Morgott.ContentTool.Project;
using UnityEngine;

namespace Morgott.ContentTool.Dev
{
    /// <summary>
    /// The bench's Sounds, Videos and Add-a-weapon screens. Each is a thin face over what the console verbs
    /// already do - `ct_sound bake`, `ct_video live`, the project bake - with the rows and files written for the
    /// author by <see cref="ProjectScaffold"/>, so nobody types JSON. Same layout as the other screens: a step
    /// bar, ONE main button that says why it is grey, badges, and a Details drawer for the log.
    ///
    /// IMGUI DISCIPLINE, the bench's one rule: nothing that changes HOW MANY controls are drawn moves inside
    /// an event. A press records an action and the next Layout pass runs it (<see cref="Drain"/>); a search
    /// box's results are recomputed on Layout only; the file browser opens and closes on Layout.
    /// </summary>
    internal static class TaskScreens
    {
        // ---- shared: the mod the screens write into ------------------------------------------------------

        private static string modName = "MyContentMod", shownName;
        private static string[] mods = new string[0];
        private static bool modsStale = true;
        private static Action next;
        private static string message = "", log = "";
        private static Vector2 listScroll;

        /// <summary>Runs the press recorded last pass. Called first in every screen's Draw.</summary>
        private static void Drain()
        {
            if (Event.current.type != EventType.Layout) return;
            SoundPreview.Tick();
            VideoPreview.Tick();
            if (modsStale) { mods = ScanMods(); modsStale = false; }
            if (Name != shownName || othersStale)
            {
                othersStale = false;
                otherSounds = ProjectScaffold.OtherModFiles(ContentToolMain.ModDir, Name,
                    Path.Combine(Path.Combine("Content", "Audio"), "Replace"), ProjectScaffold.AudioExtensions);
                otherSounds.AddRange(ProjectScaffold.OtherModFiles(ContentToolMain.ModDir, Name,
                    Path.Combine("Dist", "Sounds"), new[] { ".bnk" }));
                otherVideos = ProjectScaffold.OtherModFiles(ContentToolMain.ModDir, Name,
                    Path.Combine("Content", "Videos"), ProjectScaffold.VideoExtensions);
            }
            if (Name != shownName)
            {
                shownName = Name;
                soundItemsStale = videoRowsStale = weaponRowsStale = true;
                createdRoot = null;             // "Next: Build & share" belongs to the mod it was created in
            }
            Action a = next;
            next = null;
            if (a == null) return;
            try { a(); }
            catch (Exception ex) when (ex is IOException || ex is InvalidDataException || ex is UnauthorizedAccessException)
            { message = ex.Message; }
            catch (Exception ex) { message = ex.GetType().Name + ": " + ex.Message; log = ex.ToString(); }
        }

        /// <summary>Sibling folders of ContentTool that carry a ppcontent.json - the mods these screens can add to.</summary>
        private static string[] ScanMods()
        {
            var found = new List<string>();
            try
            {
                DirectoryInfo parent = Directory.GetParent(ContentToolMain.ModDir.TrimEnd('\\', '/'));
                if (parent != null)
                    foreach (string d in Directory.GetDirectories(parent.FullName))
                        if (File.Exists(Path.Combine(d, ContentMods.Manifest)) &&
                            ProjectScaffold.NameRefusal(Path.GetFileName(d)) == null)
                            found.Add(Path.GetFileName(d));
            }
            catch (Exception) { }
            found.Sort(StringComparer.OrdinalIgnoreCase);
            return found.ToArray();
        }

        private static string Name { get { return (modName ?? "").Trim(); } }
        private static string Root { get { return ProjectScaffold.RootOf(ContentToolMain.ModDir, Name); } }

        /// <summary>"Your mod: [name]" with what that name means right now, and the author's other mods one
        /// press away under a fold. Returns the name's refusal, or null.</summary>
        private static string ModRow(string key)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(new GUIContent("Your mod", "the mod folder beside ContentTool this writes into"),
                            GUILayout.Width(84f));
            modName = GUILayout.TextField(modName ?? "", 64);
            GUILayout.EndHorizontal();
            string refusal = ProjectScaffold.NameRefusal(Name);
            string root = refusal == null ? Root : null;
            bool exists = root != null && File.Exists(Path.Combine(root, ContentMods.Manifest));
            BenchUi.Hint(refusal != null ? "type a mod name: letters, digits, '.', '_' or '-'"
                         : exists ? "adds to your mod " + Name
                         : "a NEW mod " + Name + " will be made beside ContentTool", root);
            if (BenchUi.Fold(key + "/mods", "Your other mods (" + mods.Length + ")"))
            {
                foreach (string m in mods)
                    if (BenchUi.Pick(new GUIContent(m))) { string pick = m; next = () => modName = pick; }
                if (GUILayout.Button("Look again", GUILayout.Width(100f))) modsStale = true;
            }
            return refusal == null ? null : "type a mod name first";
        }

        /// <summary>The last press's answer and, in Details, its whole log.</summary>
        private static void Result(string key)
        {
            BenchUi.Hint(LifecycleView.OneLine(message), message);
            if (BenchUi.Details(key + "/log", "Details and log"))
                BenchUi.LogView(key + "/log", log, log, 200f);
        }

        /// <summary>A picked file's line: its name, or a dash, its Play/Stop (grey until there is a file), and the
        /// button that opens the browser. <paramref name="play"/> comes back true when Play/Stop was pressed.</summary>
        private static bool FileRow(string label, string path, Func<string, string, bool> button, out bool play)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(new GUIContent(path == null ? label + ": (none yet)" : label + ": " +
                                           BenchList.Elide(Path.GetFileName(path), BenchList.NameChars - 26), path));
            play = false;
            if (button != null)
            {
                GUI.enabled = path != null;
                play = button(path == null ? null : "file:" + path, "hear / watch your file") && path != null;
                GUI.enabled = true;
            }
            bool press = GUILayout.Button(path == null ? "Pick..." : "Change...", GUILayout.Width(80f));
            GUILayout.EndHorizontal();
            return press;
        }

        /// <summary>Stops both previews - the bench left the Sounds/Videos screens.</summary>
        internal static void StopPreviews() { SoundPreview.Stop(); VideoPreview.Stop(); }

        /// <summary>The bench closed: stop and hand back everything the previews hold.</summary>
        internal static void ShutdownPreviews() { SoundPreview.Shutdown(); VideoPreview.Stop(); }

        private static List<KeyValuePair<string, string>> otherSounds = new List<KeyValuePair<string, string>>(),
                                                          otherVideos = new List<KeyValuePair<string, string>>();
        private static bool othersStale = true;

        /// <summary>A shipped sound by media id, through the best path the game offers: its loose .wem decoded
        /// into the preview bank, else its own event in its own bank; said out loud when there is neither.</summary>
        private static void PlayGameSound(uint id)
        {
            string key = "media:" + id;
            if (SoundPreview.Active(key)) { SoundPreview.Stop(); return; }
            string wem = SoundReplace.LooseWem(id);
            if (wem != null) { SoundPreview.ToggleFile(key, wem); return; }
            string name = Extract.SoundNames.Name(id), bank;
            byte[] embedded = SoundReplace.EmbeddedWem(id);
            if (embedded != null) { SoundPreview.ToggleWem(key, embedded, name.Length > 0 ? name : id.ToString()); return; }
            uint ev;
            if (SoundReplace.EventForSound(name, out bank, out ev)) SoundPreview.ToggleEvent(key, bank, ev, name);
            else SoundPreview.Say("no preview for '" + name + "': it lives inside a game bank and no event of its " +
                                  "own name plays it");
        }

        /// <summary>BenchNav's Back and step jump for a screen whose one sub-view is a file browser.</summary>
        private static bool Hide(GlbFileBrowser b) { if (!b.Open) return false; b.Hide(); return true; }
        private static bool Show(GlbFileBrowser b, string file) { if (!b.Open) b.Show(Dir(file)); return true; }

        private static string Dir(string path)
        {
            try { return string.IsNullOrEmpty(path) ? null : Path.GetDirectoryName(path); }
            catch (Exception) { return null; }
        }

        // ---- Sounds ---------------------------------------------------------------------------------------

        private static readonly GlbFileBrowser audioBrowser =
            new GlbFileBrowser(ProjectScaffold.AudioExtensions, "Pick your sound file", "sound-recent.txt");
        private static bool audioBrowserWanted;
        private static string audioFile, soundFilter = "", soundFilterShown;
        private static uint media;
        private static List<uint> soundHits = new List<uint>();
        private static int soundTotal;
        private static List<ProjectScaffold.SoundItem> soundItems = new List<ProjectScaffold.SoundItem>();
        private static bool soundItemsStale = true;
        private static readonly string[] SoundSteps = { "Mod", "Your file", "Game sound", "Build" };

        internal static void Sounds(float width)
        {
            Drain();
            Wwise.SoundbankNames names = Extract.SoundNames;
            if (Event.current.type == EventType.Layout)
            {
                if (audioBrowserWanted && !audioBrowser.Open) audioBrowser.Show(Dir(audioFile));
                audioBrowserWanted = false;
                if (soundFilter != soundFilterShown)
                {
                    soundFilterShown = soundFilter;
                    soundHits = names.Search(soundFilter, 12, out soundTotal);
                }
                if (soundItemsStale) { soundItems = Root == null ? soundItems : ProjectScaffold.Sounds(Root); soundItemsStale = false; }
            }
            BenchNav.Handlers(() => Hide(audioBrowser), i => i == 1 && Show(audioBrowser, audioFile), 1,
                              audioBrowser.Open ? "Pick your sound file" : null);
            if (audioBrowser.Open)
            {
                string picked = audioBrowser.Draw(260f);
                if (picked != null) next = () => audioFile = picked;
                return;
            }

            string nameRefusal = ModRow("sounds");
            BenchUi.Steps(SoundSteps, nameRefusal != null ? 0 : audioFile == null ? 1 : media == 0 ? 2 : 3);
            BenchUi.Hint("Replace one of the game's sounds with your own .wav, .ogg or .mp3. The mod ships the " +
                         "new sound; no game file is changed.");

            VideoPreview.Stop();

            BenchUi.Section("1  Your sound file");
            bool play;
            if (FileRow("File", audioFile, SoundPreview.Button, out play)) audioBrowserWanted = true;
            if (play) { string f = audioFile; next = () => SoundPreview.ToggleFile("file:" + f, f); }

            BenchUi.Section("2  Which game sound it replaces");
            soundFilter = GUILayout.TextField(soundFilter ?? "", 80);
            BenchUi.Hint(names.Count == 0 ? "the game's sound list could not be read - " + (names.Why ?? "no reason given")
                         : soundTotal + " match(es) - type part of a name, a bank or an id; Play to hear it"
                           + (soundTotal > soundHits.Count ? "; showing " + soundHits.Count : ""));
            foreach (uint id in soundHits)
            {
                GUILayout.BeginHorizontal();
                uint row = id;
                if (SoundPreview.Button("media:" + id, "hear the game's sound")) next = () => PlayGameSound(row);
                if (BenchUi.Pick(new GUIContent((id == media ? "> " : "") + names.Name(id) + "   (" + names.Bank(id) + ")",
                                                "media id " + id)))
                    next = () => media = row;
                GUILayout.EndHorizontal();
            }
            BenchUi.Field("Replaces", media == 0 ? "-" : names.Name(media), media == 0 ? null : "media id " + media);

            GUILayout.Space(4f);
            string refusal = nameRefusal ?? (audioFile == null ? "pick your sound file first (step 1)"
                             : media == 0 ? "pick the game sound it replaces (step 2)" : null);
            if (BenchUi.Main("Add to mod & build", refusal,
                             "copies the file into the mod, then ct_sound bake and loads it - the game pauses a moment"))
            {
                string file = audioFile, name = Name; uint target = media;
                next = () => AddSound(file, name, target);
            }
            Result("sounds");
        }

        // ---- the right-hand panes: what the screen shows where the 3D preview stands on model screens ------

        private static Vector2 paneScroll;

        /// <summary>The Sounds screen's right pane: the player (what plays, how far, Stop, volume) over the big
        /// list of this mod's sounds and the other installed mods' ones, each with its length and Play/Stop.
        /// Drawn after <see cref="Sounds"/> in the same pass, so a press here is drained on the next Layout.</summary>
        internal static void SoundsPane(Rect area)
        {
            Wwise.SoundbankNames names = Extract.SoundNames;
            GUILayout.BeginArea(area);
            BenchUi.Title("Player");
            string playing = SoundPreview.Playing;
            GUILayout.BeginHorizontal();
            GUILayout.Label(playing == null ? "nothing playing" : "playing  " + playing);
            GUILayout.FlexibleSpace();
            GUI.enabled = playing != null;
            if (GUILayout.Button("Stop", GUILayout.Width(70f))) next = SoundPreview.Stop;
            GUI.enabled = true;
            GUILayout.EndHorizontal();
            int at = SoundPreview.PositionMs(), of = SoundPreview.PlayingMs;
            BenchUi.Bar(of > 0 && at >= 0 ? (float)at / of : 0f, 10f);
            GUILayout.BeginHorizontal();
            GUILayout.Label(BenchUi.Clock(playing == null ? -1 : at) + " / " + BenchUi.Clock(of), GUILayout.Width(130f));
            GUILayout.FlexibleSpace();
            GUILayout.Label(new GUIContent("Volume", "the preview's own volume - no game setting changes"), GUILayout.Width(56f));
            SoundPreview.Volume = GUILayout.HorizontalSlider(SoundPreview.Volume, 0f, 1f, GUILayout.Width(160f));
            GUILayout.Label(Mathf.RoundToInt(SoundPreview.Volume * 100f) + "%", GUILayout.Width(40f));
            GUILayout.EndHorizontal();
            BenchUi.Hint(playing == null ? SoundPreview.Said : "");

            paneScroll = GUILayout.BeginScrollView(paneScroll);
            BenchUi.Section("Sounds in " + Name);
            if (soundItems.Count == 0) BenchUi.Hint("none yet - add one on the left");
            foreach (ProjectScaffold.SoundItem s in soundItems)
            {
                GUILayout.BeginHorizontal();
                bool live = IsLive(s.Media);
                BenchUi.Mark(live ? "LIVE" : s.State == "built" ? "BUILT" : s.State == "out of date" ? "OLD" : "-",
                             live || s.State == "built" ? Grade.Pass : s.State == "out of date" ? (Grade?)Grade.Warn : null,
                             live ? "loaded in this game session" : s.State, 50f);
                string src = Path.Combine(ProjectScaffold.SoundDir(Root ?? ""), s.File);
                if (SoundPreview.Button("file:" + src, "hear your file (built or not)"))
                    next = () => SoundPreview.ToggleFile("file:" + src, src);
                GUILayout.Label(new GUIContent(names.Name(s.Media) + "  <-  " + s.File, "media id " + s.Media));
                GUILayout.FlexibleSpace();
                GUILayout.Label(SoundPreview.Length(src), GUILayout.Width(60f));
                GUILayout.EndHorizontal();
            }
            if (soundItems.Count > 0 && GUILayout.Button(new GUIContent("Build all again", "ct_sound bake " + Name),
                                                         GUILayout.Width(140f)))
            { string name = Name; next = () => BuildSounds(name); }

            if (BenchUi.Fold("sounds/others", "Sounds other installed mods ship (" + otherSounds.Count + ")"))
                foreach (KeyValuePair<string, string> o in otherSounds)
                {
                    GUILayout.BeginHorizontal();
                    string path = o.Value;
                    bool bank = path.EndsWith(".bnk", StringComparison.OrdinalIgnoreCase);
                    uint id;
                    uint.TryParse(Path.GetFileNameWithoutExtension(path), out id);
                    // A built bank with no source beside it: the game's own event, which plays whatever
                    // serves that sound now - the mod's version when that mod is switched on.
                    if (SoundPreview.Button(bank ? "media:" + id : "file:" + path,
                                            bank ? "the game's event for this sound, as it plays now" : "hear the file"))
                        next = bank ? (Action)(() => PlayGameSound(id)) : () => SoundPreview.ToggleFile("file:" + path, path);
                    GUILayout.Label(new GUIContent(o.Key + ": " + (id != 0 && names.Name(id).Length > 0
                                                   ? names.Name(id) : Path.GetFileName(path)), path));
                    GUILayout.FlexibleSpace();
                    GUILayout.Label(bank ? "" : SoundPreview.Length(path), GUILayout.Width(60f));
                    GUILayout.EndHorizontal();
                }
            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        /// <summary>The Videos screen's right pane: a large player - the picture as big as the pane allows at
        /// 16:9, the transport under it, and what is playing.</summary>
        internal static void VideosPane(Rect area)
        {
            GUILayout.BeginArea(area);
            BenchUi.Title("Player");
            string playing = VideoPreview.Playing;
            BenchUi.Hint(playing == null ? "press Play on a clip on the left" : "");
            float w = area.width, h = Mathf.Min(Mathf.Round(w * 9f / 16f), area.height - 140f);
            VideoPreview.Box(w, h);
            VideoPreview.Transport();
            BenchUi.Hint(VideoPreview.Said);
            GUILayout.EndArea();
        }

        /// <summary>The home screen's right pane: what the bench is for in a few lines, and the author's mods
        /// beside ContentTool - picking one makes it the mod the Sounds, Videos and Add-a-weapon screens write into.</summary>
        internal static void HomePane(Rect area)
        {
            Drain();
            GUILayout.BeginArea(area);
            BenchUi.Title("Content bench");
            BenchUi.Text("Pick a task on the left. Each one walks you through its steps and says what is missing " +
                         "before its main button lights up.");
            BenchUi.Hint("Replace a model and Fit a weapon show your model live here: middle-drag to orbit, " +
                         "wheel to zoom, F to frame. Build & share turns a mod folder into an installed, tested mod " +
                         "and a zip to share.");
            BenchUi.Section("Your mods (" + mods.Length + ")");
            BenchUi.Hint("the mod folders beside ContentTool with a ppcontent.json - pick one to work on it");
            paneScroll = GUILayout.BeginScrollView(paneScroll);
            foreach (string m in mods)
                if (BenchUi.Pick(new GUIContent((m == Name ? "> " : "") + m, "Sounds, Videos and Add a weapon write into " + m)))
                { string pick = m; next = () => modName = pick; }
            GUILayout.EndScrollView();
            if (GUILayout.Button("Look again", GUILayout.Width(100f))) modsStale = true;
            GUILayout.EndArea();
        }

        private static bool IsLive(uint id)
        {
            string root = Root;
            if (root == null) return false;
            string owner = ContentState.Owner(Bake.SoundLoad.Route, id.ToString());
            return owner != null && string.Equals(owner, ModGate.Key(root), StringComparison.OrdinalIgnoreCase);
        }

        private static void AddSound(string file, string name, uint target)
        {
            string copy = ProjectScaffold.AddSound(ContentToolMain.ModDir, name, file, target);
            modsStale = true;
            BuildSounds(name);
            message = "added " + Path.GetFileName(copy) + " - " + message;
        }

        private static void BuildSounds(string name)
        {
            string root = ProjectScaffold.RootOf(ContentToolMain.ModDir, name);
            var b = new StringBuilder(Bake.SoundReplace.Run(new[] { "bake", name }));
            int failed = 0;
            Bake.SoundLoad.LoadMod(root, b, ref failed);
            log = b.ToString();
            soundItemsStale = true;
            // ASKED, not assumed: LoadMod claims a mod once per session, so a second build loads nothing.
            int total = 0, live = 0;
            foreach (ProjectScaffold.SoundItem s in ProjectScaffold.Sounds(root)) { total++; if (IsLive(s.Media)) live++; }
            bool refused = log.IndexOf("REFUSED", StringComparison.Ordinal) >= 0;
            message = refused ? "the build refused something - see Details"
                    : failed > 0 ? failed + " sound(s) could not be loaded - see Details"
                    : live < total ? "built - " + (total - live) + " sound(s) are not loaded yet: restart the game to hear them " +
                                     "(a sound loaded once this session cannot be swapped live)"
                    : "built and loaded - play the game and listen (a later change needs a restart)";
        }

        // ---- Videos ---------------------------------------------------------------------------------------

        private static readonly GlbFileBrowser videoBrowser =
            new GlbFileBrowser(ProjectScaffold.VideoExtensions, "Pick your video clip", "video-recent.txt");
        private static bool videoBrowserWanted, addNew;
        private static string videoFile, videoFilter = "", videoFilterShown, shippedVideo;
        private static List<string> videoHits = new List<string>();
        private static int videoTotal;
        private static List<KeyValuePair<string, string>> videoRows = new List<KeyValuePair<string, string>>();
        private static List<bool> videoPresent = new List<bool>();
        private static bool videoRowsStale = true;
        private static readonly string[] VideoSteps = { "Mod", "Your clip", "Game video", "Apply" };

        internal static void Videos(float width)
        {
            Drain();
            if (Event.current.type == EventType.Layout)
            {
                if (videoBrowserWanted && !videoBrowser.Open) videoBrowser.Show(Dir(videoFile));
                videoBrowserWanted = false;
                if (videoFilter != videoFilterShown)
                {
                    videoFilterShown = videoFilter;
                    List<string> all;
                    try { all = Extract.ShippedVideos(videoFilter); } catch (Exception) { all = new List<string>(); }
                    videoTotal = all.Count;
                    videoHits = all.Count > 12 ? all.GetRange(0, 12) : all;
                }
                if (videoRowsStale && Root != null) { videoRows = ProjectScaffold.Videos(Root, out videoPresent); videoRowsStale = false; }
            }
            BenchNav.Handlers(() => Hide(videoBrowser), i => i == 1 && Show(videoBrowser, videoFile), 1,
                              videoBrowser.Open ? "Pick your clip" : null);
            if (videoBrowser.Open)
            {
                string picked = videoBrowser.Draw(260f);
                if (picked != null) next = () => videoFile = picked;
                return;
            }

            string nameRefusal = ModRow("videos");
            bool target = addNew || shippedVideo != null;
            BenchUi.Steps(VideoSteps, nameRefusal != null ? 0 : videoFile == null ? 1 : !target ? 2 : 3);
            BenchUi.Hint("Replace one of the game's cutscenes with your own clip, or add a new one. It is served " +
                         "from your mod's folder; no game file is changed.");

            SoundPreview.Stop();

            BenchUi.Section("1  Your clip (.webm, .mp4 or .mov)");
            bool play;
            if (FileRow("Clip", videoFile, VideoPreview.Button, out play)) videoBrowserWanted = true;
            if (play) { string f = videoFile; next = () => VideoPreview.Toggle("file:" + f, f); }

            BenchUi.Section("2  Which game video it replaces");
            int mode = GUILayout.Toolbar(addNew ? 1 : 0, new[] { "Replace a game video", "Add as a new video" });
            if ((mode == 1) != addNew) { bool want = mode == 1; next = () => addNew = want; }
            // BOTH arms lay out the same controls; the new-video arm only greys the picks out (Play stays).
            videoFilter = GUILayout.TextField(videoFilter ?? "", 80);
            BenchUi.Hint(videoTotal + " game video(s) match; Play to watch" +
                         (videoTotal > videoHits.Count ? "; showing " + videoHits.Count : ""));
            foreach (string v in videoHits)
            {
                GUILayout.BeginHorizontal();
                string asset = v, file = Path.Combine(Application.streamingAssetsPath, v.Replace('/', Path.DirectorySeparatorChar));
                if (VideoPreview.Button("game:" + asset, "watch the game's clip"))
                    next = () => VideoPreview.Toggle("game:" + asset, file);
                GUI.enabled = !addNew;
                if (BenchUi.Pick(new GUIContent((v == shippedVideo ? "> " : "") + Path.GetFileNameWithoutExtension(v), v)))
                    next = () => shippedVideo = asset;
                GUI.enabled = true;
                GUILayout.EndHorizontal();
            }
            BenchUi.Field(addNew ? "Adds" : "Replaces",
                          addNew ? "a new video (your mod's code plays it)" : shippedVideo == null ? "-"
                                 : Path.GetFileNameWithoutExtension(shippedVideo), shippedVideo);

            GUILayout.Space(4f);
            string refusal = nameRefusal ?? (videoFile == null ? "pick your clip first (step 1)"
                             : !target ? "pick the game video it replaces, or 'Add as a new video' (step 2)" : null);
            if (BenchUi.Main("Add to mod & apply", refusal, "copies the clip, writes its row, then ct_video live"))
            {
                string file = videoFile, name = Name, asset = addNew ? null : shippedVideo;
                next = () => AddVideo(file, name, asset);
            }
            Result("videos");

            BenchUi.Section("Videos in " + Name);
            if (videoRows.Count == 0) BenchUi.Hint("none yet");
            for (int i = 0; i < videoRows.Count; i++)
            {
                bool there = i < videoPresent.Count && videoPresent[i];
                GUILayout.BeginHorizontal();
                BenchUi.Mark(there ? "OK" : "MISSING", there ? Grade.Pass : Grade.Fail,
                             there ? "the clip is in Content\\Videos" : "no clip of that name in Content\\Videos", 60f);
                string clip = ProjectScaffold.VideoFile(Root, videoRows[i].Key);
                GUI.enabled = clip != null;
                if (VideoPreview.Button("file:" + clip, "watch your clip") && clip != null)
                    next = () => VideoPreview.Toggle("file:" + clip, clip);
                GUI.enabled = true;
                GUILayout.Label(new GUIContent(videoRows[i].Key + "  ->  " +
                                               (videoRows[i].Value == null ? "new video"
                                                : Path.GetFileNameWithoutExtension(videoRows[i].Value)), videoRows[i].Value));
                GUILayout.EndHorizontal();
            }
            if (videoRows.Count > 0 && GUILayout.Button(new GUIContent("Apply again", "ct_video live " + Name),
                                                        GUILayout.Width(140f)))
            { string name = Name; next = () => ApplyVideos(name); }

            if (BenchUi.Fold("videos/others", "Videos other installed mods ship (" + otherVideos.Count + ")"))
                foreach (KeyValuePair<string, string> o in otherVideos)
                {
                    GUILayout.BeginHorizontal();
                    string path = o.Value;
                    if (VideoPreview.Button("file:" + path, "watch the clip")) next = () => VideoPreview.Toggle("file:" + path, path);
                    GUILayout.Label(new GUIContent(o.Key + ": " + Path.GetFileName(path), path));
                    GUILayout.EndHorizontal();
                }
        }

        private static void AddVideo(string file, string name, string asset)
        {
            string copy = ProjectScaffold.AddVideo(ContentToolMain.ModDir, name, file, asset);
            modsStale = true;
            ApplyVideos(name);
            message = "added " + Path.GetFileName(copy) + " - " + message;
        }

        private static void ApplyVideos(string name)
        {
            log = Bake.VideoCatalog.LiveAt(ProjectScaffold.RootOf(ContentToolMain.ModDir, name));
            videoRowsStale = true;
            message = log.IndexOf("REFUSED", StringComparison.Ordinal) >= 0 || log.IndexOf("SKIP", StringComparison.Ordinal) >= 0
                ? "applied with problems - see Details"
                : "applied - each clip's state is in Details; a served clip plays the next time that video starts";
        }

        // ---- Add a weapon ---------------------------------------------------------------------------------

        private static readonly GlbFileBrowser modelBrowser = new GlbFileBrowser();
        private static bool modelBrowserWanted;
        private static string modelFile, donorFilter = "", donorFilterShown, donor, weaponName = "";
        private static List<string> donorHits = new List<string>();
        private static int donorTotal;
        private static List<string[]> weaponRows = new List<string[]>();
        private static bool weaponRowsStale = true;
        private static string createdRoot;
        private static readonly string[] WeaponSteps = { "Mod", "Start from", "Model", "Name", "Create" };

        /// <summary>Returns true when the author asked to go on to Build &amp; share with the project just
        /// written (already bound there) - the bench moves the tab after EndArea.</summary>
        internal static bool Weapon(float width, IList<string> donors, Func<string, string> word, Action<string> showDonor)
        {
            Drain();
            if (Event.current.type == EventType.Layout)
            {
                if (modelBrowserWanted && !modelBrowser.Open) modelBrowser.Show(Dir(modelFile));
                modelBrowserWanted = false;
                if (donorFilter != donorFilterShown)
                {
                    donorFilterShown = donorFilter;
                    var all = new List<string>();
                    foreach (string d in donors)
                        if (string.IsNullOrEmpty(donorFilter) ||
                            d.IndexOf(donorFilter, StringComparison.OrdinalIgnoreCase) >= 0 ||
                            word(d).IndexOf(donorFilter, StringComparison.OrdinalIgnoreCase) >= 0) all.Add(d);
                    donorTotal = all.Count;
                    donorHits = all.Count > 12 ? all.GetRange(0, 12) : all;
                }
                if (weaponRowsStale && Root != null) { weaponRows = ProjectScaffold.Weapons(Root); weaponRowsStale = false; }
            }
            if (modelBrowser.Open)
            {
                string picked = modelBrowser.Draw(260f);
                if (picked != null) next = () => modelFile = picked;
                return false;
            }

            string nameRefusal = ModRow("weapon");
            string display = (weaponName ?? "").Trim();
            BenchUi.Steps(WeaponSteps, nameRefusal != null ? 0 : donor == null ? 1 : modelFile == null ? 2
                                       : display.Length == 0 ? 3 : 4);
            BenchUi.Hint("A new weapon copies a game weapon's behaviour (how it is held, fired and reloaded) and " +
                         "wears your model. Pick a game weapon of the same kind as yours.");

            BenchUi.Section("1  Start from a game weapon");
            donorFilter = GUILayout.TextField(donorFilter ?? "", 80);
            BenchUi.Hint(donorTotal + " weapon(s) match" + (donorTotal > donorHits.Count ? "; showing " + donorHits.Count : ""));
            foreach (string d in donorHits)
                if (BenchUi.Pick(new GUIContent((d == donor ? "> " : "") + word(d), d)))
                { string pick = d; next = () => { donor = pick; showDonor(pick); }; }
            BenchUi.Field("Start from", donor == null ? "-" : word(donor), donor);

            BenchUi.Section("2  Your model");
            bool unused;
            if (FileRow("Model", modelFile, null, out unused)) modelBrowserWanted = true;
            BenchUi.Hint("to check your model itself, open it in Replace a model - its preview shows any .glb");

            BenchUi.Section("3  Its name in the game");
            weaponName = GUILayout.TextField(weaponName ?? "", 40);
            BenchUi.Hint(nameRefusal == null && display.Length > 0
                         ? "def name " + ProjectScaffold.WeaponId(Name, display) : "", null);

            GUILayout.Space(4f);
            string refusal = nameRefusal ?? (donor == null ? "pick the game weapon to start from (step 1)"
                             : modelFile == null ? "pick your .glb model (step 2)"
                             : display.Length == 0 ? "type the weapon's name (step 3)" : null);
            if (BenchUi.Main("Create weapon", refusal,
                             "copies the model and writes the mod's publish + weapons rows (fit: auto)"))
            {
                string file = modelFile, name = Name, from = donor, called = display;
                next = () => CreateWeapon(file, name, from, called);
            }
            Result("weapon");

            BenchUi.Section("Weapons in " + Name);
            if (weaponRows.Count == 0) BenchUi.Hint("none yet");
            foreach (string[] w in weaponRows)
                BenchUi.Field(w[1] ?? "-", "from " + word(w[2] ?? "-"), w[0]);
            GUI.enabled = createdRoot != null && !LifecycleDashboard.Busy;
            bool onward = GUILayout.Button(new GUIContent("Next: Build & share", "bake the model and install the mod"));
            GUI.enabled = true;
            BenchUi.Hint(createdRoot == null ? "" : "then restart the game and switch " + Path.GetFileName(createdRoot) +
                         " on in Mods - the weapon appears in your base storage in a new campaign; fit it with 'Fit a weapon'");
            if (!onward) return false;
            string why = LifecycleDashboard.Select(createdRoot);
            if (why != null) { message = why; return false; }
            return true;
        }

        private static void CreateWeapon(string file, string name, string from, string called)
        {
            ProjectScaffold.WeaponResult r = ProjectScaffold.AddWeapon(ContentToolMain.ModDir, name, file, from, called);
            createdRoot = r.Root;
            modsStale = true;
            weaponRowsStale = true;
            log = "wrote " + r.WeaponId + " (clone of " + from + ") into " + Path.Combine(r.Root, ContentMods.Manifest) +
                  "\nmodel " + r.ModelPath + (r.ModelReused ? " (already published, key reused)" : "") + ", key " + r.ModelKey;
            message = "created " + called + " - next: Build & share";
        }

        /// <summary>Invalidates what the lists show, e.g. when the bench reopens.</summary>
        internal static void Refresh() { modsStale = soundItemsStale = videoRowsStale = weaponRowsStale = true; }
    }
}
