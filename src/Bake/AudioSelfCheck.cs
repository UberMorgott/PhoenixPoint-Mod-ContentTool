using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Morgott.ContentTool.Wwise;
using UnityEngine;

namespace Morgott.ContentTool.Bake
{
    /// <summary>
    /// The audio bake regression, in ONE run and ONE bank: an embedded sound (A1) and a streamed
    /// sound (A2) side by side, which is also the proof of the embed/stream mode flag (A3) - each
    /// arm is the other's control, since a mode flag that did nothing would make both arms report
    /// the same storage.
    ///
    /// Discipline (METHODOLOGY): packaging and extraction are asserted BEFORE and separately from
    /// any Wwise call; the bytes handed to Wwise are the ones read back out of the bundle; both
    /// events are posted before the bank is loaded to show they do not exist without it; nothing is
    /// judged by ear.
    /// </summary>
    internal static class AudioSelfCheck
    {
        private const string BundleName = "ct_audio";
        private const string BankName = "morgott_contenttool_selfcheck";

        // ALLOCATED media IDs, offline-checked against the index's _media_ids_all (7697 entries).
        // ponytail: two verified constants are enough for a fixed regression; the index-backed
        // allocator + collision matrix arrives with the Wwise-ID-validation task (FINAL-PLAN 9.3/9.4).
        private const uint EmbedMedia = 0xC7000001;
        private const uint StreamMedia = 0xC7000002;

        private const string EmbedName = "morgott_contenttool_selfcheck_tone";
        private const string StreamName = "morgott_contenttool_stream_tone";

        private const int Rate = 44100;
        private const int Frames = 22050;   // exactly 0.5 s - the length that identifies our tones
        private const int EmbedFreq = 1760, StreamFreq = 1100;

        internal static string Run(string sourceBundleName)
        {
            StringBuilder log = new StringBuilder();
            int failures = 0;

            List<AudioBake.Sound> sounds = new List<AudioBake.Sound>
            {
                new AudioBake.Sound { Name = EmbedName, MediaId = EmbedMedia, Stream = false,
                                      Wem = WwisePcm.BuildWem(Sine(EmbedFreq, Rate, Frames), 1, Rate) },
                new AudioBake.Sound { Name = StreamName, MediaId = StreamMedia, Stream = true,
                                      Wem = WwisePcm.BuildWem(Sine(StreamFreq, Rate, Frames), 1, Rate) }
            };
            byte[] streamedWem = sounds[1].Wem;

            string outDir = Path.Combine(Application.persistentDataPath, "ContentTool");
            string outPath = Path.Combine(outDir, BundleName + ".bundle");
            Directory.CreateDirectory(outDir);
            if (File.Exists(outPath)) { File.Delete(outPath); log.AppendLine("deleted stale " + outPath); }

            AudioBake.Result baked;
            try
            {
                using (BundleBaker baker = new BundleBaker(BakeSelfCheck.ShippedBundlePath(sourceBundleName), "contenttool"))
                {
                    log.AppendLine("read source: " + baker.SourceInfo);
                    baked = AudioBake.Package(baker, BankName, sounds);
                    baker.Write(outPath, BundleName);
                    log.AppendLine("bank " + baked.Bank.Length + " B (bankID " + baked.BankId +
                                   ", embedded " + EmbedName + "/" + EmbedMedia + " eventID " + WwiseId.Hash(EmbedName) +
                                   ", streamed " + StreamName + "/" + StreamMedia + " eventID " + WwiseId.Hash(StreamName) + ")");
                    log.AppendLine("WROTE " + outPath + " " + new FileInfo(outPath).Length + " B as " +
                                   baker.WrittenIdentity + ", bank at '" + baked.BankAsset +
                                   "', manifest at '" + baked.ManifestAsset + "'");
                }
                // Written out so the oracle (decompiled\AkSoundEngine\hirc_parse.py) can be run over
                // the exact bytes this run generated.
                File.WriteAllBytes(Path.Combine(outDir, BundleName + ".bnk"), baked.Bank);
            }
            catch (Exception ex) { return log.Append("BAKE THREW ").Append(ex).ToString(); }

            // A leftover .wem from an earlier run plays and reads exactly like success, so the
            // target is emptied first and the deletion is stated.
            string wemPath = Path.Combine(StreamCache.TargetDir, StreamMedia + ".wem");
            bool stale = File.Exists(wemPath);
            if (stale) File.Delete(wemPath);
            log.AppendLine("pre-existing " + wemPath + ": " + (stale ? "FOUND, DELETED" : "none"));

            GameObject emitter = new GameObject("ct_audio_emitter");
            AssetBundle bundle = null;
            try
            {
                log.AppendLine("pre-run UnloadBank(" + baked.BankId + "): " + AkSoundEngine.UnloadBank(baked.BankId, IntPtr.Zero));
                log.AppendLine("RegisterGameObj: " + AkSoundEngine.RegisterGameObj(emitter, "ct_audio"));

                // CONTROL in the same run: without our bank neither event exists, so both posts must
                // fail. If either plays here, nothing below is attributable to the bundle.
                string c1 = AudioProbe.Post(emitter, WwiseId.Hash(EmbedName), "CONTROL/embed-before-bank-load", 500);
                string c2 = AudioProbe.Post(emitter, WwiseId.Hash(StreamName), "CONTROL/stream-before-bank-load", 500);
                failures += Check(log, "CONTROL", c1.Contains("playingID=0") && c2.Contains("playingID=0"),
                                  "neither event exists before the bank is loaded -> " + c1 + " ; " + c2);

                bundle = AssetBundle.LoadFromFile(outPath);
                if (bundle == null) return log.Append("FAIL AssetBundle.LoadFromFile returned null").ToString();

                // ---- streamed media must be on disk BEFORE the bank is loaded
                int written;
                log.AppendLine("extract: " + StreamCache.Extract(bundle, baked.ManifestAsset, out written));
                byte[] onDisk = File.Exists(wemPath) ? File.ReadAllBytes(wemPath) : null;
                failures += Check(log, "A2a", written == 1 && Same(onDisk, streamedWem),
                                  "extracted " + (onDisk == null ? -1 : onDisk.Length) + " B to " + wemPath +
                                  ", byte-identical to the packaged .wem (rewritten=" + written + ")");
                if (!Same(onDisk, streamedWem)) return log.Append("ct_audio: FAIL - extraction, not audio").ToString();

                int again;
                log.AppendLine("extract again: " + StreamCache.Extract(bundle, baked.ManifestAsset, out again));
                failures += Check(log, "A2b", again == 0, "an unchanged cache rewrites nothing (rewritten=" + again + ")");

                // ---- the bank, from the bundle and nowhere else
                TextAsset ta = bundle.LoadAsset<TextAsset>(baked.BankAsset);
                byte[] got = ta == null ? null : ta.bytes;
                failures += Check(log, "A1a", Same(got, baked.Bank),
                                  "bank TextAsset out of the bundle is " + (got == null ? -1 : got.Length) +
                                  " B and byte-identical to the generated bank");
                if (!Same(got, baked.Bank)) return log.Append("ct_audio: FAIL - packaging, not audio").ToString();

                uint loadedId;
                string load = AudioProbe.LoadBank(got, baked.BankId, out loadedId);
                log.AppendLine(load);
                failures += Check(log, "A1b", load.Contains("LoadBankMemoryCopy: AK_Success") && loadedId == baked.BankId,
                                  "the bank Wwise loaded is the one that came out of the bundle (bankId=" + loadedId + ")");
                if (!load.Contains("LoadBankMemoryCopy: AK_Success"))
                    return log.Append("ct_audio: FAIL - the bank did not load, so nothing was played").ToString();

                // ---- the two arms. Same bank, same run: the only difference is the storage mode.
                log.AppendLine(AudioProbe.Post(emitter, WwiseId.Hash(EmbedName), "A1/embedded-" + EmbedFreq + "Hz"));
                bool embedOk = AudioProbe.GotDuration && AudioProbe.Media == EmbedMedia && !AudioProbe.Streaming;
                failures += Check(log, "A1c", embedOk, "mediaID=" + AudioProbe.Media + " (embed media is " + EmbedMedia +
                                  "), streaming=" + (AudioProbe.Streaming ? "true(FILE)" : "false(MEMORY)") +
                                  ", expected our media out of the bank's own DATA");

                log.AppendLine(AudioProbe.Post(emitter, WwiseId.Hash(StreamName), "A2/streamed-" + StreamFreq + "Hz"));
                bool streamOk = AudioProbe.GotDuration && AudioProbe.Media == StreamMedia && AudioProbe.Streaming;
                failures += Check(log, "A2c", streamOk, "mediaID=" + AudioProbe.Media + " (stream media is " + StreamMedia +
                                  "), streaming=" + (AudioProbe.Streaming ? "true(FILE)" : "false(MEMORY)") +
                                  "; that media is in no DIDX, so FILE can only mean " + wemPath);

                failures += Check(log, "A3", embedOk && streamOk,
                                  "one bank, two storage modes: the embed arm reported MEMORY and the stream arm FILE, " +
                                  "so the mode flag reaches the engine rather than both arms sharing one path");

                log.AppendLine("post-run UnloadBank: " + AkSoundEngine.UnloadBank(baked.BankId, IntPtr.Zero));

                // ---- A4: the collision matrix, exercised against IDs Phoenix Point really owns.
                // Positive-only validation is worthless - a validator that never rejects anything
                // passes every bake, so the check is that it REFUSES these two and accepts ours.
                bool rejectsMedia = Rejects(new AudioBake.Sound { Name = EmbedName, MediaId = 18839791 });
                bool allowsDeclared = !Rejects(new AudioBake.Sound { Name = EmbedName, MediaId = 18839791, Replaces = true });
                bool rejectsEvent = Rejects(new AudioBake.Sound { Name = "GUI_StatsPlusClick", MediaId = EmbedMedia });
                bool acceptsOurs = !Rejects(sounds[0]);
                failures += Check(log, "A4", rejectsMedia && allowsDeclared && rejectsEvent && acceptsOurs,
                                  "index holds " + IdIndex.MediaCount + " media + " + IdIndex.ComputedCount +
                                  " computed IDs; PP media 18839791 refused=" + rejectsMedia +
                                  ", allowed once declared as a replacement=" + allowsDeclared +
                                  ", name hashing onto PP event 784388130 refused=" + rejectsEvent +
                                  ", our own sound accepted=" + acceptsOurs);
            }
            catch (Exception ex) { return log.Append("PLAY THREW ").Append(ex).ToString(); }
            finally
            {
                try { log.AppendLine("UnregisterGameObj: " + AkSoundEngine.UnregisterGameObj(emitter)); }
                catch (Exception ex) { log.AppendLine("UnregisterGameObj THREW " + ex.Message); }
                UnityEngine.Object.Destroy(emitter);
                if (bundle != null) bundle.Unload(true);
            }

            return log.Append(failures == 0 ? "ct_audio: ALL PASS" : "ct_audio: " + failures + " FAILURE(S)").ToString();
        }

        /// <summary>Does the ID validator refuse this sound? Nothing is written - Validate runs before the bake.</summary>
        private static bool Rejects(AudioBake.Sound s)
        {
            try { AudioBake.Validate(BankName, new[] { s }); return false; }
            catch (InvalidOperationException) { return true; }
        }

        /// <summary>16-bit mono sine at half scale; Frames at Rate makes it exactly 0.5 s.</summary>
        private static byte[] Sine(int freq, int rate, int frames)
        {
            byte[] pcm = new byte[frames * 2];
            for (int i = 0; i < frames; i++)
            {
                int v = (int)(16383 * Math.Sin(2.0 * Math.PI * freq * i / rate));
                pcm[i * 2] = (byte)v;
                pcm[i * 2 + 1] = (byte)(v >> 8);
            }
            return pcm;
        }

        private static int Check(StringBuilder log, string gate, bool ok, string detail)
        {
            log.AppendLine(gate + (ok ? " PASS " : " FAIL ") + detail);
            return ok ? 0 : 1;
        }

        private static bool Same(byte[] a, byte[] b)
        {
            if (a == null || b == null || a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
            return true;
        }
    }
}
