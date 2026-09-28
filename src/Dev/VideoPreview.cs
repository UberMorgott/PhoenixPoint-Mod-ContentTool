using System;
using UnityEngine;
using UnityEngine.Video;

namespace Morgott.ContentTool.Dev
{
    /// <summary>
    /// ONE clip playing in the Videos screen's preview box, the way the game's own cutscene player does it
    /// (Base.UI.VideoPlayback.VideoPlaybackController:148-194): a UnityEngine.Video.VideoPlayer on a URL,
    /// Prepare(), and on prepareCompleted a RenderTexture.GetTemporary(width, height) as its targetTexture,
    /// then Play(). No sound: the game plays cutscene audio through Wwise (VideoSoundPlayer), never the
    /// clip's own track, so the preview is silent the same way.
    /// </summary>
    internal static class VideoPreview
    {
        private static GameObject host;
        private static VideoPlayer player;
        private static RenderTexture texture;
        private static string playingKey, label;

        internal static string Said { get; private set; } = "";

        internal static bool Active(string key) { return key != null && key == playingKey; }

        internal static bool Button(string key, string tooltip)
        {
            return GUILayout.Button(new GUIContent(Active(key) ? "Stop" : "Play", tooltip), GUILayout.Width(44f));
        }

        /// <summary>Play <paramref name="path"/> (a file on disk), or stop it when it is the one playing.
        /// Call from a Layout pass.</summary>
        internal static void Toggle(string key, string path)
        {
            if (Active(key)) { Stop(); return; }
            Stop();
            try
            {
                host = new GameObject("ct_bench_video_preview");
                UnityEngine.Object.DontDestroyOnLoad(host);
                player = host.AddComponent<VideoPlayer>();
                player.playOnAwake = false;
                player.audioOutputMode = VideoAudioOutputMode.None;
                player.renderMode = VideoRenderMode.RenderTexture;
                player.source = VideoSource.Url;
                player.url = path;
                player.isLooping = false;
                player.errorReceived += (VideoPlayer p, string msg) => { if (p == player) Said = "can't play: " + msg; };
                player.prepareCompleted += Prepared;
                player.loopPointReached += (VideoPlayer p) => { if (p == player) finished = true; };
                player.Prepare();
                playingKey = key;
                label = System.IO.Path.GetFileName(path);
                Said = "opening " + label + "...";
            }
            catch (Exception ex) { Said = "can't play: " + ex.Message; Stop(); }
        }

        private static bool finished;

        private static void Prepared(VideoPlayer p)
        {
            if (p != player) return;
            texture = RenderTexture.GetTemporary((int)p.width, (int)p.height);
            p.targetTexture = texture;
            p.Play();
            Said = "playing " + label + " (" + p.width + "x" + p.height + ", no sound - the game plays cutscene sound separately)";
        }

        /// <summary>Layout pass: a clip that reached its end stops itself.</summary>
        internal static void Tick()
        {
            if (finished) { finished = false; Stop(); }
        }

        internal static void Stop()
        {
            if (player != null)
            {
                try { player.Stop(); player.targetTexture = null; } catch (Exception) { }
            }
            if (texture != null) { RenderTexture.ReleaseTemporary(texture); texture = null; }
            if (host != null) { UnityEngine.Object.Destroy(host); host = null; }
            player = null;
            if (playingKey != null && !Said.StartsWith("can't", StringComparison.Ordinal)) Said = "";
            playingKey = null;
        }

        /// <summary>The preview box: ALWAYS one rect of the same height, so starting or stopping a clip never
        /// changes the control count; the picture is drawn in it only while one is playing.</summary>
        internal static void Box(float width)
        {
            float h = Mathf.Round(width * 9f / 16f);
            Rect r = GUILayoutUtility.GetRect(width, h, GUILayout.Width(width), GUILayout.Height(h));
            if (Event.current.type != EventType.Repaint) return;
            GUI.Box(r, texture == null ? "press Play on a clip to watch it here" : "");
            if (texture != null) GUI.DrawTexture(r, texture, ScaleMode.ScaleToFit, false);
        }
    }
}
