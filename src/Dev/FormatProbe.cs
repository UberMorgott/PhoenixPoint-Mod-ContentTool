using System;
using System.Collections;
using System.IO;
using System.Text;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Experimental.Audio;
using UnityEngine.Experimental.Video;
using UnityEngine.Networking;
using UnityEngine.Video;

namespace Morgott.ContentTool.Dev
{
    /// <summary>
    /// Gate F1 - which source formats THIS install's engine already decodes, measured rather than
    /// remembered. Every row is one real file handed to the exact API the importer would use:
    /// <see cref="Texture2D.LoadImage"/> for images, <see cref="UnityWebRequestMultimedia"/> +
    /// <see cref="AudioClip.GetData"/> for sound, <see cref="VideoPlayer"/> on a url for video -
    /// the last being the same call the game makes for its own cutscenes
    /// (VideoPlaybackController.cs:148-150).
    ///
    /// PASS on a row means "the engine decoded it", which is the matrix verdict itself; FAIL is an
    /// equally real measurement, not a broken gate. Only the four CONTROLS decide whether the run is
    /// trustworthy: png/wav/webm must decode (the instrument works) and the junk files must NOT (a
    /// decoder that succeeds on random bytes proves nothing about the files it succeeded on).
    ///
    /// Dev-only scaffolding. It reads <c>&lt;mod&gt;\FormatProbes\</c>, which only
    /// <c>tools\make-format-probes.ps1</c> ever writes; with the folder absent the gate says VOID.
    /// </summary>
    internal static class FormatProbe
    {
        internal static string Run()
        {
            string dir = Path.Combine(ContentToolMain.ModDir ?? ".", "FormatProbes");
            if (!Directory.Exists(dir))
                return "F1 VOID no " + dir + " - run tools\\make-format-probes.ps1 first";
            string[] files = Directory.GetFiles(dir);
            if (files.Length == 0) return "F1 VOID " + dir + " is empty";

            GameObject go = new GameObject("ct_fmt");
            UnityEngine.Object.DontDestroyOnLoad(go);
            go.AddComponent<Runner>().Begin(dir);
            return "F1 armed - " + files.Length + " probe(s) in " + dir + "; the arms print from the runner";
        }

        private sealed class Runner : MonoBehaviour
        {
            private string dir;
            private StringBuilder log;
            private int controlsFailed;

            internal void Begin(string folder)
            {
                dir = folder;
                log = new StringBuilder();
                AsyncGate.Pending++;
                StartCoroutine(Gate());
            }

            private IEnumerator Gate()
            {
                try
                {
                    // --- textures. Synchronous: LoadImage decodes on the calling (main) thread.
                    foreach (string f in Sorted("tex*"))
                    {
                        string why;
                        bool ok = Decode(f, out why);
                        Row("tex", f, ok, why);
                    }

                    // outputSampleRate 0 means Unity's own audio device never opened - Phoenix Point
                    // runs its sound through Wwise. That is not a detail: DownloadHandlerAudioClip
                    // decodes INSIDE that subsystem, so with it shut every audio row below is a
                    // measurement of the subsystem, not of the codec. The reset is the one cheap
                    // attempt to open it anyway; if it stays 0 the answer is structural.
                    log.AppendLine("F1-audio-subsystem outputSampleRate=" + AudioSettings.outputSampleRate +
                                   " speakerMode=" + AudioSettings.speakerMode +
                                   " driverCapabilities=" + AudioSettings.driverCapabilities);
                    AudioConfiguration cfg = AudioSettings.GetConfiguration();
                    bool reset = false;
                    log.AppendLine("F1-audio-config sampleRate=" + cfg.sampleRate + " speakerMode=" + cfg.speakerMode +
                                   " dspBufferSize=" + cfg.dspBufferSize + " realVoices=" + cfg.numRealVoices +
                                   " virtualVoices=" + cfg.numVirtualVoices);
                    try
                    {
                        // Every field is filled, not just the rate: a shut subsystem hands back a
                        // config whose speakerMode is Raw and whose voice counts are 0, and Reset
                        // throws on it ("Raw speaker mode is not supported", measured).
                        cfg.sampleRate = 44100;
                        cfg.speakerMode = AudioSpeakerMode.Stereo;
                        if (cfg.dspBufferSize <= 0) cfg.dspBufferSize = 1024;
                        if (cfg.numRealVoices <= 0) cfg.numRealVoices = 32;
                        if (cfg.numVirtualVoices <= 0) cfg.numVirtualVoices = 128;
                        reset = AudioSettings.Reset(cfg);
                    }
                    catch (Exception ex) { log.AppendLine("F1-audio-reset THREW " + ex.Message); }
                    log.AppendLine("F1-audio-reset returned " + reset + "; outputSampleRate is now " +
                                   AudioSettings.outputSampleRate);
                    yield return null;
                    if (AudioSettings.outputSampleRate <= 0)
                        log.AppendLine("F1-aud VOID Unity's audio device never opened and Reset cannot open it, so " +
                                       "every F1-aud row below measures the SUBSYSTEM, not the codec - including the .wav " +
                                       "one, which this tool reads perfectly well with its own reader");

                    // --- audio. The verdict is not "a clip came back" but "PCM came back": the bank
                    //     writer needs samples, so the row reads them and reports the peak.
                    foreach (string f in Sorted("aud*"))
                    {
                        Sound s = new Sound();
                        yield return Load(f, s);
                        Row("aud", f, s.Ok, s.Info);
                    }

                    // --- video. Same call the game makes; frameCount is the decoder's own answer.
                    foreach (string f in Sorted("vid*"))
                    {
                        Movie m = new Movie();
                        yield return Play(f, m);
                        Row("vid", f, m.Frames > 0, m.Info);
                    }

                    // --- the second audio route. VideoPlayer decodes through the PLATFORM (Media
                    //     Foundation), which is demonstrably alive here, and APIOnly hands the
                    //     decoded frames back as raw floats without going near the shut audio device.
                    //     If this answers, every container Windows can open becomes PCM.
                    foreach (string f in Sorted("aud*"))
                    {
                        Pcm p = new Pcm();
                        yield return Samples(f, p);
                        Row("pcm", f, p.Ok, p.Info);
                    }
                    Pcm mp4 = new Pcm();
                    yield return Samples(Path.Combine(dir, "vid.mp4"), mp4);
                    Row("pcm", Path.Combine(dir, "vid.mp4"), mp4.Ok, mp4.Info);
                }
                finally
                {
                    log.Append(controlsFailed == 0
                        ? "ct_fmt: controls ALL PASS - the instrument measured what it says it measured"
                        : "ct_fmt: " + controlsFailed + " CONTROL FAILURE(S) - treat every row above as VOID");
                    ContentToolMain.Say(log.ToString());
                    AsyncGate.Pending--;
                    Destroy(gameObject);
                }
            }

            private string[] Sorted(string pattern)
            {
                string[] f = Directory.GetFiles(dir, pattern);
                Array.Sort(f, StringComparer.OrdinalIgnoreCase);
                return f;
            }

            /// <summary>
            /// One measured row. A control is a file whose answer is known before the run - the three
            /// formats already shipping through this tool, and the junk that must be refused - so a
            /// broken instrument is visible without trusting the rows under test.
            /// </summary>
            private void Row(string family, string path, bool decoded, string info)
            {
                string name = Path.GetFileName(path);
                bool junk = name.EndsWith(".junk", StringComparison.Ordinal);
                // The audio family has NO positive control: Unity's audio subsystem is shut in this
                // build, so its .wav arm measures the subsystem and not the codec. That is declared
                // once, as a VOID line, rather than smuggled in as a permanent control failure that
                // would void the texture and video rows too.
                bool control = junk || name == "tex.png" || name == "vidctl.webm";
                bool good = junk ? !decoded : decoded;
                if (control && !good) controlsFailed++;

                log.AppendLine("F1-" + family + "-" + name.Replace('.', '-') +
                               (decoded ? " PASS decoded " : " FAIL not decoded ") + info +
                               (control ? junk ? "  [CONTROL: must NOT decode]" : "  [CONTROL: must decode]" : ""));
            }
        }

        // ------------------------------------------------------------------ per-family instruments

        /// <summary>
        /// Exactly what ContentProject.ImportTexture does, so a PASS here is importable - with one
        /// oracle it did not have. MEASURED on this build: <c>LoadImage</c> returns TRUE for bytes it
        /// cannot decode (random junk included) and leaves the texture at Unity's 8x8 error size, so
        /// the return value alone is worthless. Every probe is authored 64x64; the SIZE is the
        /// identity, and 8x8 is the failure sentinel.
        /// </summary>
        private const int ProbeSide = 64;

        private static bool Decode(string path, out string info)
        {
            Texture2D t = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            try
            {
                bool said = t.LoadImage(File.ReadAllBytes(path));
                bool ok = said && t.width == ProbeSide && t.height == ProbeSide;
                info = t.width + "x" + t.height + " (LoadImage said " + said + ")" +
                       (ok ? "" : "  <- not the authored " + ProbeSide + "x" + ProbeSide);
                return ok;
            }
            finally { UnityEngine.Object.Destroy(t); }
        }

        private sealed class Sound { internal bool Ok; internal string Info = "(never answered)"; }

        /// <summary>
        /// AudioType is the engine's own list of containers it will attempt; measured off
        /// UnityEngine.CoreModule.dll it holds WAV/MPEG/OGGVORBIS/ACC and NO FLAC, so .flac can only
        /// be offered UNKNOWN. Everything is asked anyway - the point is to measure, not to predict.
        /// </summary>
        private static AudioType TypeOf(string path)
        {
            switch (Path.GetExtension(path).ToLowerInvariant())
            {
                case ".wav": return AudioType.WAV;
                case ".mp3": return AudioType.MPEG;
                case ".ogg": return AudioType.OGGVORBIS;
                case ".m4a": return AudioType.ACC;
                default: return AudioType.UNKNOWN;
            }
        }

        private static IEnumerator Load(string path, Sound result)
        {
            UnityWebRequest www = UnityWebRequestMultimedia.GetAudioClip(new Uri(path).AbsoluteUri, TypeOf(path));
            yield return www.SendWebRequest();

            if (www.isNetworkError || www.isHttpError) { result.Info = "(request: " + www.error + ")"; yield break; }

            AudioClip clip = null;
            string err = null;
            try { clip = DownloadHandlerAudioClip.GetContent(www); }
            catch (Exception ex) { err = ex.GetType().Name + ": " + ex.Message; }
            if (clip == null) { result.Info = "(no clip: " + (err ?? "GetContent returned null") + ")"; yield break; }

            // Decoding is asynchronous even off a local file, so a sample count read in the same
            // frame reads 0 on a clip that decodes fine one frame later.
            float t0 = Time.realtimeSinceStartup;
            while (clip.loadState == AudioDataLoadState.Loading && Time.realtimeSinceStartup - t0 < 10f)
                yield return null;
            if (clip.samples <= 0)
            {
                result.Info = "(loadState=" + clip.loadState + " samples=" + clip.samples +
                              " len=" + clip.length.ToString("0.000") + "s bytes=" + www.downloadedBytes + ")";
                yield break;
            }

            // A clip object is not PCM. The bank writer needs samples, so read them and prove they
            // are not silence - a decoder that returns a correctly sized buffer of zeros would
            // otherwise pass.
            float[] data = new float[clip.samples * clip.channels];
            bool got = clip.GetData(data, 0);
            float peak = 0f;
            for (int i = 0; i < data.Length; i++) { float a = data[i] < 0 ? -data[i] : data[i]; if (a > peak) peak = a; }
            result.Ok = got && peak > 0.01f;
            result.Info = clip.samples + " samples " + clip.channels + "ch " + clip.frequency + "Hz peak=" +
                          peak.ToString("0.000") + (got ? "" : " (GetData refused)");
        }

        private sealed class Pcm { internal bool Ok; internal string Info = "(never answered)"; }

        /// <summary>
        /// The audio route that does NOT go through Unity's audio device: VideoPlayer decodes with
        /// the platform's own demuxer/decoder, and <c>VideoAudioOutputMode.APIOnly</c> hands the
        /// decoded frames straight back as floats through an
        /// <see cref="UnityEngine.Experimental.Audio.AudioSampleProvider"/>. If this answers, the
        /// bank writer can be fed by any container Windows can open, with no decoder of our own.
        /// </summary>
        private static IEnumerator Samples(string path, Pcm result)
        {
            if (!File.Exists(path)) { result.Info = "(no such probe)"; yield break; }

            GameObject go = new GameObject("ct_fmt_pcm");
            VideoPlayer vp = go.AddComponent<VideoPlayer>();
            vp.playOnAwake = false;
            vp.renderMode = VideoRenderMode.APIOnly;
            vp.audioOutputMode = VideoAudioOutputMode.APIOnly;
            vp.source = VideoSource.Url;
            vp.url = path;
            string error = null;
            vp.errorReceived += (VideoPlayer p, string msg) => error = msg;
            vp.EnableAudioTrack(0, true);        // must precede Prepare, or track 0 stays silent
            vp.Prepare();

            float t0 = Time.realtimeSinceStartup;
            while (!vp.isPrepared && error == null && Time.realtimeSinceStartup - t0 < 20f) yield return null;
            if (!vp.isPrepared)
            {
                result.Info = "(" + (error ?? "timed out after 20s") + ")";
                UnityEngine.Object.Destroy(go);
                yield break;
            }
            if (vp.audioTrackCount == 0)
            {
                result.Info = "(prepared, but the file declares 0 audio tracks)";
                UnityEngine.Object.Destroy(go);
                yield break;
            }

            AudioSampleProvider prov = vp.GetAudioSampleProvider(0);
            if (prov == null)
            {
                result.Info = "(prepared with " + vp.audioTrackCount + " track(s) but GetAudioSampleProvider returned null)";
                UnityEngine.Object.Destroy(go);
                yield break;
            }
            vp.Play();

            t0 = Time.realtimeSinceStartup;
            while (prov.availableSampleFrameCount == 0 && Time.realtimeSinceStartup - t0 < 10f) yield return null;

            uint have = prov.availableSampleFrameCount;
            if (have == 0)
            {
                result.Info = "(provider " + prov.channelCount + "ch " + prov.sampleRate +
                              "Hz delivered no sample frames in 10 s)";
                UnityEngine.Object.Destroy(go);
                yield break;
            }

            NativeArray<float> buf = new NativeArray<float>((int)have * prov.channelCount, Allocator.Temp);
            uint got = prov.ConsumeSampleFrames(buf);
            float peak = 0f;
            for (int i = 0; i < buf.Length; i++) { float a = buf[i] < 0 ? -buf[i] : buf[i]; if (a > peak) peak = a; }
            buf.Dispose();

            result.Ok = got > 0 && peak > 0.01f;
            result.Info = got + " sample frames " + prov.channelCount + "ch " + prov.sampleRate +
                          "Hz peak=" + peak.ToString("0.000");
            UnityEngine.Object.Destroy(go);
        }

        private sealed class Movie { internal long Frames = -1; internal string Info = "(never answered)"; }

        private static IEnumerator Play(string path, Movie result)
        {
            GameObject go = new GameObject("ct_fmt_open");
            VideoPlayer vp = go.AddComponent<VideoPlayer>();
            vp.playOnAwake = false;
            vp.renderMode = VideoRenderMode.APIOnly;
            vp.audioOutputMode = VideoAudioOutputMode.None;
            vp.source = VideoSource.Url;
            // A PLAIN Windows path, which is what the game itself assigns
            // (VideoPlaybackController.cs:150 hands it a StreamingAssets path). A file:/// URI with
            // percent-escapes made every container fail, control included - measured, first run.
            vp.url = path;
            string error = null;
            vp.errorReceived += (VideoPlayer p, string msg) => error = msg;
            vp.Prepare();

            float t0 = Time.realtimeSinceStartup;
            while (!vp.isPrepared && error == null && Time.realtimeSinceStartup - t0 < 20f) yield return null;

            if (vp.isPrepared)
            {
                result.Frames = (long)vp.frameCount;
                result.Info = "frameCount=" + result.Frames + " " + vp.width + "x" + vp.height +
                              " len=" + vp.length.ToString("0.00") + "s";
            }
            else result.Info = "(" + (error ?? "timed out after 20s") + ")";
            UnityEngine.Object.Destroy(go);
        }
    }
}
