using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Morgott.ContentTool.Wwise;
using UnityEngine;

namespace Morgott.ContentTool.Dev
{
    /// <summary>
    /// ONE sound playing on the bench at a time, through Wwise - the only audio path this game has (Unity's
    /// own audio is switched off in the player, m_DisableAudio). Two ways in:
    ///   * A FILE - an author's .wav/.ogg/.mp3 (built or not) or a shipped loose .wem: decoded off the main
    ///     thread by the tool's own readers (WwisePcm.ReadAudio, WwiseWem.ToWav), wrapped in the preview bank
    ///     (<see cref="PreviewBank"/>, the tool's own ids, replaces nothing), LoadBankMemoryCopy + PostEvent.
    ///   * A SHIPPED EVENT - a sound embedded in a game bank: that bank loaded by name and its own event posted,
    ///     exactly what `ct_sound probe` does. It plays whatever serves that sound now - a mod's replacement if
    ///     one is loaded.
    /// Stop is AkSoundEngine.StopPlayingID. The end arrives as AK_EndOfEvent through the game's own
    /// per-frame AkCallbackManager.PostCallbacks. Everything that touches Wwise runs from <see cref="Tick"/>,
    /// called on a Layout pass - never mid-event.
    /// </summary>
    internal static class SoundPreview
    {
        private sealed class Job
        {
            internal string Key, Path;
            internal byte[] Wem, Bank;
            internal string Error;
            internal int Ms = -1;
            internal volatile bool Done;
        }

        private static Job job;
        private static string playingKey;
        private static uint playingId;
        private static volatile bool ended;
        private static GameObject emitter;
        private static bool bankLoaded;
        private static string playingLabel;
        private static int playingMs = -1;
        private static float volume = 1f;

        /// <summary>What is playing, in words, or null.</summary>
        internal static string Playing { get { return playingKey == null ? null : playingLabel; } }
        /// <summary>The playing sound's length in ms, -1 when not known (yet).</summary>
        internal static int PlayingMs { get { return playingKey == null ? -1 : playingMs; } }

        /// <summary>Where the playing sound is, in ms, or -1. Wwise tracks it only because the post asked
        /// for AK_EnableGetSourcePlayPosition (AudioProbe.cs:23-27).</summary>
        internal static int PositionMs()
        {
            if (playingId == 0) return -1;
            try
            {
                int pos;
                return AkSoundEngine.GetSourcePlayPosition(playingId, out pos) == AKRESULT.AK_Success ? pos : -1;
            }
            catch (Exception) { return -1; }
        }

        /// <summary>The preview's own loudness, 0..1 - the preview emitter's output-bus volume, so no game
        /// sound and no mixer setting is touched. A null listener means every listener.</summary>
        internal static float Volume
        {
            get { return volume; }
            set
            {
                value = Mathf.Clamp01(value);
                if (Mathf.Approximately(value, volume)) return;
                volume = value;
                ApplyVolume();
            }
        }

        /// <summary>The last volume write's answer, for the pane and for a driver checking it took.</summary>
        internal static string VolumeSaid { get; private set; } = "";

        private static void ApplyVolume()
        {
            if (emitter == null) return;
            try { VolumeSaid = AkSoundEngine.SetGameObjectOutputBusVolume(emitter, null, volume).ToString(); }
            catch (Exception ex) { VolumeSaid = ex.Message; }
        }

        /// <summary>A file's length as m:ss.t for the list - measured once per file version off the main
        /// thread by the same reader that plays it; "..." until then, "?" when it cannot be read.</summary>
        internal static string Length(string path)
        {
            if (string.IsNullOrEmpty(path)) return "?";
            string key;
            try { key = path + "|" + File.GetLastWriteTimeUtc(path).Ticks; } catch (Exception) { return "?"; }
            int ms;
            lock (lengths)
            {
                if (!lengths.TryGetValue(key, out ms))
                {
                    lengths[key] = ms = -1;
                    ThreadPool.QueueUserWorkItem(delegate { Measure(key, path); });
                }
            }
            return ms >= 0 ? BenchUi.Clock(ms) : ms == -1 ? "..." : "?";
        }

        private static readonly Dictionary<string, int> lengths = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        private static void Measure(string key, string path)
        {
            int ms = -2;
            try
            {
                string why;
                WwisePcm.Wav w = WwisePcm.ReadAudio(path, out why);
                if (w != null) ms = MsOf(w);
            }
            catch (Exception) { }
            lock (lengths) lengths[key] = ms;
        }

        private static int MsOf(WwisePcm.Wav w)
        {
            long frames = w.Channels <= 0 ? 0 : w.Pcm16.Length / 2 / w.Channels;
            return w.SampleRate <= 0 ? -1 : (int)(frames * 1000L / w.SampleRate);
        }

        /// <summary>The last thing the preview said ("playing ...", a decode refusal), for the screen's hint.</summary>
        internal static string Said { get; private set; } = "";

        /// <summary>A line from the screen itself ("no preview for ...").</summary>
        internal static void Say(string s) { Stop(); Said = s ?? ""; }

        /// <summary>Is this key playing, or being decoded to play?</summary>
        internal static bool Active(string key)
        {
            return key != null && (key == playingKey || (job != null && job.Key == key));
        }

        /// <summary>The row button: "Play" or "Stop" for this key. Returns true when pressed.</summary>
        internal static bool Button(string key, string tooltip)
        {
            return GUILayout.Button(new GUIContent(Active(key) ? "Stop" : "Play", tooltip), GUILayout.Width(44f));
        }

        /// <summary>Start (or, when this key is the one playing, stop) a FILE preview.</summary>
        internal static void ToggleFile(string key, string path)
        {
            if (Active(key)) { Stop(); return; }
            Stop();
            var j = new Job { Key = key, Path = path };
            job = j;
            Said = "reading " + System.IO.Path.GetFileName(path) + "...";
            ThreadPool.QueueUserWorkItem(delegate { Decode(j); });
        }

        /// <summary>Start (or stop) a shipped media EMBEDDED in a bank, handed over as its .wem bytes;
        /// <paramref name="label"/> stands in for the file name.</summary>
        internal static void ToggleWem(string key, byte[] wem, string label)
        {
            if (Active(key)) { Stop(); return; }
            Stop();
            var j = new Job { Key = key, Path = label + ".wem", Wem = wem };
            job = j;
            Said = "reading " + label + "...";
            ThreadPool.QueueUserWorkItem(delegate { Decode(j); });
        }

        /// <summary>Start (or stop) a SHIPPED event in its own bank.</summary>
        internal static void ToggleEvent(string key, string bankName, uint eventId, string label)
        {
            if (Active(key)) { Stop(); return; }
            Stop();
            try
            {
                uint bankId;
                AKRESULT r = AkSoundEngine.LoadBank(bankName, out bankId);
                Post(key, eventId, label + " (event of " + bankName + ", " + r + ")");
            }
            catch (Exception ex) { Said = "could not play: " + ex.Message; }
        }

        internal static void Stop()
        {
            job = null;
            if (playingId != 0)
            {
                try { AkSoundEngine.StopPlayingID(playingId); } catch (Exception) { }
                playingId = 0;
            }
            if (playingKey != null) Said = "";
            playingKey = null;
        }

        /// <summary>Worker thread: the file to PCM to the preview bank. Unity-free.</summary>
        private static void Decode(Job j)
        {
            string tmp = null;
            try
            {
                string path = j.Path;
                if (path.EndsWith(".wem", StringComparison.OrdinalIgnoreCase))
                {
                    tmp = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ct_preview_" + Guid.NewGuid().ToString("N") + ".wav");
                    string no = WwiseWem.ToWav(j.Wem ?? File.ReadAllBytes(path), tmp);
                    if (no != null) { j.Error = no; return; }
                    path = tmp;
                }
                string why;
                WwisePcm.Wav w = WwisePcm.ReadAudio(path, out why);
                if (w == null) { j.Error = why; return; }
                j.Ms = MsOf(w);
                j.Bank = PreviewBank.Build(w.Pcm16, w.Channels, w.SampleRate);
            }
            catch (Exception ex) { j.Error = ex.Message; }
            finally
            {
                if (tmp != null) try { File.Delete(tmp); } catch (Exception) { }
                j.Done = true;
            }
        }

        /// <summary>Main thread, Layout pass: finish a decoded job, and notice a sound that ended.</summary>
        internal static void Tick()
        {
            Job j = job;
            if (j != null && j.Done)
            {
                job = null;
                if (j.Error != null) { Said = "can't preview " + System.IO.Path.GetFileName(j.Path) + ": " + j.Error; return; }
                try
                {
                    // Unload-then-load of the tool's OWN bank id (AudioProbe.LoadBank): the last preview's
                    // bytes go, nothing shipped is touched.
                    uint loaded;
                    AudioProbe.LoadBank(j.Bank, PreviewBank.BankId, out loaded);
                    bankLoaded = true;
                    Post(j.Key, PreviewBank.EventId, System.IO.Path.GetFileName(j.Path));
                    playingMs = j.Ms;
                }
                catch (Exception ex) { Said = "could not play: " + ex.Message; }
            }
            if (ended && playingKey != null) { ended = false; playingId = 0; playingKey = null; Said = ""; }
        }

        private static void Post(string key, uint eventId, string label)
        {
            if (emitter == null)
            {
                emitter = new GameObject("ct_bench_preview");
                UnityEngine.Object.DontDestroyOnLoad(emitter);
                AkSoundEngine.RegisterGameObj(emitter, "ct_bench_preview");
            }
            ended = false;
            ApplyVolume();
            playingMs = -1;
            playingId = AkSoundEngine.PostEvent(eventId, emitter, Flags, OnEnd, null);
            if (playingId == 0) { Said = "the game refused to play " + label; playingKey = null; return; }
            playingKey = key;
            playingLabel = label;
            Said = "playing " + label;
        }

        /// <summary>End and duration callbacks, and play-position tracking (AudioProbe.cs:23-27: without
        /// that flag GetSourcePlayPosition answers AK_Fail).</summary>
        private const uint Flags = (uint)(AkCallbackType.AK_EndOfEvent | AkCallbackType.AK_Duration |
                                          AkCallbackType.AK_EnableGetSourcePlayPosition);

        private static void OnEnd(object cookie, AkCallbackType type, AkCallbackInfo info)
        {
            AkEventCallbackInfo e = info as AkEventCallbackInfo;
            if (e == null || e.playingID != playingId) return;
            if (type == AkCallbackType.AK_EndOfEvent) ended = true;
            // A shipped event's length arrives here; a file's was measured by its decode already.
            else if (type == AkCallbackType.AK_Duration && playingMs < 0)
            {
                AkDurationCallbackInfo d = info as AkDurationCallbackInfo;
                if (d != null) playingMs = (int)d.fDuration;
            }
        }

        /// <summary>The bench closed: silence, and give the preview bank and emitter back.</summary>
        internal static void Shutdown()
        {
            Stop();
            try { if (bankLoaded) AkSoundEngine.UnloadBank(PreviewBank.BankId, IntPtr.Zero); } catch (Exception) { }
            bankLoaded = false;
            if (emitter != null)
            {
                try { AkSoundEngine.UnregisterGameObj(emitter); } catch (Exception) { }
                UnityEngine.Object.Destroy(emitter);
                emitter = null;
            }
        }
    }
}
