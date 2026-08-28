using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Morgott.ContentTool.Project;
using Morgott.ContentTool.Wwise;
using UnityEngine;

namespace Morgott.ContentTool.Bake
{
    /// <summary>
    /// Replacement of an EXISTING shipped sound, BAKED INTO THE MOD - never into the installation.
    ///
    /// <see cref="Bake"/> reads the author's .wav/.ogg/.mp3, re-encodes it as a .wem and wraps it in
    /// one media-only .bnk per replaced media id, written to the MOD's own
    /// <see cref="ShippedBanks"/> folder. <see cref="SoundLoad"/> hands those banks to
    /// AkSoundEngine.LoadBankMemoryCopy at init, and Wwise then serves the game's own media id out of
    /// OUR bank: gate Shape-C (`ct_sound shapec`) measures exactly that, on a STREAMED target, with
    /// no game file touched. Not one byte of StreamingAssets\Audio is opened for writing.
    ///
    /// WHAT USED TO BE HERE, and why it is gone rather than gated (S1-b). `apply` overwrote the
    /// shipped StreamingAssets\Audio\...\&lt;mediaId&gt;.wem, patched every shipped .bnk that declared
    /// a source for it (codec -&gt; PCM, prefetch -&gt; plain stream), kept a &lt;file&gt;.ct-backup
    /// beside each and a sounds.ct-edits ledger; `verify` and `revert` served that writer. All of it
    /// is DELETED. A backup INSIDE the install is a write, and restoring one is a write too.
    ///
    /// The shipped folder is still READ, for three reasons and only three: the target's own loop
    /// region (a replacement that drops it restarts early), the &lt;bank&gt;.txt listings that name
    /// media, events and sounds, and read-only detection of what an OLDER ContentTool overwrote -
    /// see <see cref="Legacy"/>, which names the one sanctioned repair and never performs it.
    ///
    /// Author flow, no JSON grammar at all:
    ///   ct_extract audio 18839791      -&gt; writes 18839791.wav
    ///   edit it
    ///   drop it in &lt;project&gt;\Content\Audio\Replace\18839791.wav
    ///   ct_sound bake                  -&gt; the file name IS the target
    /// The subfolder keeps these out of Content\Audio\, whose .wav are NEW sounds getting fresh IDs.
    /// A "sounds" entry in ppcontent.json names a target without renaming the author's file.
    ///
    /// Coverage note: a media that lives inside a bank's DIDX+DATA has no loose file, so its loop
    /// region cannot be read - the bake still produces a bank for it, and only the loop is guessed
    /// absent.
    /// </summary>
    internal static class SoundReplace
    {
        /// <summary>What the sample probe replaces. pp-audio-architecture-FROZEN.md measured this one
        /// end to end: event GUI_StatsPlusClick=784388130, media 18839791, dur=1200ms,
        /// streaming=true(FILE). Nothing else in this file is a constant.</summary>
        private const uint ProbeMedia = 18839791;
        private const int ProbeRate = 44100;
        /// <summary>731 ms - deliberately not a round number and nowhere near the 1200 ms it replaces.</summary>
        private const int ProbeFrames = 32237;
        private const int ProbeFreq = 1650;
        /// <summary>The vanilla main-menu track, for the offline event-naming arm only - MainMenuMusic
        /// in MainMenuMusic.bnk, streamed, played by the event MainMenuMusicStart.</summary>
        private const uint MenuMusicMedia = 208540756;

        /// <summary>fDuration against a header-derived length. Wwise reports whole milliseconds.</summary>
        private const int TolMs = 60;

        internal static string Run(string[] args)
        {
            string verb = args != null && args.Length > 0 ? args[0].ToLowerInvariant() : "status";
            switch (verb)
            {
                case "apply":
                case "verify":
                case "revert":
                    return BundleClaims.Removed("ct_sound", verb,
                        "Sound never overwrites a shipped .wem or patches a shipped .bnk any more. " +
                        "'ct_sound bake [project]' builds one media-only bank per replacement into the " +
                        "MOD's own " + ShippedBanks + " folder, and ContentTool loads those at init " +
                        "(SoundLoad) - so there is nothing in your installation to verify or revert.");
                case "selftest": return SelfTest();
                case "probe":
                    if (args != null && args.Length > 2 && args[1].ToLowerInvariant() == "event")
                        return ProbeEvent(args[2]);
                    return Probe(args != null && args.Length > 1 ? args[1] : null);
                case "shapec": return ShapeC(args != null && args.Length > 1 ? args[1] : null);
                case "bake": return Bake(args != null && args.Length > 1 ? args[1] : null);
                case "status": return Status(args != null && args.Length > 1 ? args[1] : null);
                default: return "usage: ct_sound bake [project] | selftest | probe <mediaId> | probe event <eventId> | shapec [mediaId] | status [mediaId]";
            }
        }

        // ---------------------------------------------------------------- paths

        /// <summary>The same root ct_extract reads shipped media from.</summary>
        private static string AudioRoot
        {
            get { return Path.Combine(Application.streamingAssetsPath, "Audio"); }
        }

        private static string EditsFile
        {
            get { return Path.Combine(AudioRoot, "sounds.ct-edits"); }
        }

        /// <summary>The project's "id" out of ppcontent.json, and nothing else out of it.</summary>
        private static string ProjectId(string root)
        {
            string id = System.Text.RegularExpressions.Regex.Match(
                File.ReadAllText(Path.Combine(root, "ppcontent.json")), "\"id\"\\s*:\\s*\"([^\"]+)\"").Groups[1].Value;
            if (id.Length == 0) throw new InvalidDataException("ppcontent.json in " + root + " has no \"id\"");
            return id;
        }

        /// <summary>Where the author's replacement .wav live, named by the media ID they replace.</summary>
        private static string ReplaceDir(string projectRoot)
        {
            return Path.Combine(Path.Combine(Path.Combine(projectRoot, "Content"), "Audio"), "Replace");
        }

        /// <summary>
        /// The replacement files, in a fixed order. The accepted set is the SAME whitelist
        /// Content\Audio\ takes - .wav, .ogg and .mp3, all three read by this tool itself - so an
        /// author does not have to convert a track by hand just because it is a REPLACEMENT.
        /// </summary>
        private static string[] Sources(string dir)
        {
            List<string> files = new List<string>();
            foreach (string pattern in new string[] { "*.wav", "*.ogg", "*.mp3" })
                files.AddRange(Directory.GetFiles(dir, pattern));
            files.Sort(StringComparer.OrdinalIgnoreCase);
            // Two files that differ only in extension name the SAME media, and one would silently
            // overwrite the other's install. Refused, the way Content\ already refuses it.
            for (int i = 1; i < files.Count; i++)
                if (string.Equals(Path.GetFileNameWithoutExtension(files[i]),
                                  Path.GetFileNameWithoutExtension(files[i - 1]), StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Content\\Audio\\Replace\\ holds two files for the same media: " +
                        Path.GetFileName(files[i - 1]) + " and " + Path.GetFileName(files[i]) + " - one of them has to go");
            return files.ToArray();
        }

        /// <summary>
        /// PCM out of one replacement file, or the reason there is none - .wav, .ogg and .mp3 alike,
        /// through the tool's own <see cref="WwisePcm.ReadAudio"/>. Every caller already skips the
        /// file and logs this sentence, so a track the decoder refuses costs that track and no more.
        /// </summary>
        private static string ReadPcm(string path, out byte[] pcm16, out int channels, out int rate)
        {
            pcm16 = null; channels = 0; rate = 0;
            string why;
            WwisePcm.Wav w = WwisePcm.ReadAudio(path, out why);
            if (w == null) return why;
            pcm16 = w.Pcm16; channels = w.Channels; rate = w.SampleRate;
            return null;
        }

        /// <summary>
        /// The ONE loose .wem with that media ID. Refuses both nothing and more than one, exactly as
        /// LooseFiles.CopyOut does - an ambiguous target is never picked between.
        /// </summary>
        private static string WemPath(uint mediaId, out string why)
        {
            why = null;
            List<string> hit = new List<string>();
            foreach (string rel in LooseFiles.Find(AudioRoot, ".wem", mediaId.ToString()))
                if (Path.GetFileNameWithoutExtension(rel) == mediaId.ToString()) hit.Add(rel);

            if (hit.Count == 0)
            {
                why = "no " + mediaId + ".wem under " + AudioRoot + " - that media is EMBEDDED in a bank, " +
                      "not a file, so overwriting a file cannot reach it (3105 of the 7696 media are loose)";
                return null;
            }
            if (hit.Count > 1) { why = hit.Count + " files are named " + mediaId + ".wem: " + string.Join(", ", hit.ToArray()); return null; }
            return Path.Combine(AudioRoot, hit[0].Replace('/', Path.DirectorySeparatorChar));
        }

        // ---------------------------------------------------------------- verbs

        private static string Status(string mediaArg)
        {
            StringBuilder log = new StringBuilder();
            log.AppendLine("audio root: " + AudioRoot + " (READ ONLY - ContentTool never writes there)");
            log.AppendLine(Legacy());

            uint id;
            if (!string.IsNullOrEmpty(mediaArg) && uint.TryParse(mediaArg, out id))
            {
                string why;
                string wem = WemPath(id, out why);
                if (wem == null) return log.Append(mediaArg + ": " + why).ToString();
                Bank b = BankOf(id);
                WwiseWem.Info i = WwiseWem.Parse(File.ReadAllBytes(wem));
                log.Append(id + ": " + wem + " " + new FileInfo(wem).Length + " B, " + Ms(i) + "ms, " +
                           (i == null ? "UNPARSED" : (i.IsVorbis ? "vorbis" : "pcm") + " " + i.Channels + "ch " + i.SampleRate + "Hz") +
                           (b == null ? " | in no bank .txt" : " | bank " + b.Name + ", sound '" + b.Media[id] +
                            "', event " + (b.Events.ContainsKey(b.Media[id]) ? b.Events[b.Media[id]].ToString() : "(none by that name)")));
            }
            return log.ToString().TrimEnd();
        }

        /// <summary>
        /// Read-only detection of what an OLDER ContentTool overwrote inside the audio folder: a
        /// pristine &lt;file&gt;.ct-backup next to every shipped .wem it replaced and every .bnk it
        /// patched, plus its ledger. NOTHING IS RESTORED FROM HERE - putting the backup back is
        /// itself a write into the player's installation (mandate M2), so the player is told the one
        /// sanctioned repair instead. Silence would be worse than the refusal: overwritten shipped
        /// audio that nothing in the tool admits to.
        /// </summary>
        private static string Legacy()
        {
            List<string> found = new List<string>();
            if (File.Exists(EditsFile)) found.Add(Path.GetFileName(EditsFile));
            if (Directory.Exists(AudioRoot))
                foreach (string f in Directory.GetFiles(AudioRoot, "*.ct-backup", SearchOption.AllDirectories))
                    found.Add(Path.GetFileName(f));
            if (found.Count == 0)
                return "legacy: none - no .ct-backup and no " + Path.GetFileName(EditsFile) + " under " +
                       AudioRoot + ", so no shipped .wem or .bnk here was ever overwritten by ContentTool";

            StringBuilder log = new StringBuilder("LEGACY: " + found.Count + " file(s) under " + AudioRoot +
                " say an OLDER ContentTool OVERWROTE shipped audio in your game installation: ");
            for (int i = 0; i < found.Count && i < 12; i++) { if (i > 0) log.Append(", "); log.Append(found[i]); }
            if (found.Count > 12) log.Append(", ... (" + (found.Count - 12) + " more)");
            return log.Append(". ContentTool no longer writes there, and will not write there to undo " +
                "it either. REPAIR: Steam -> Phoenix Point -> Properties -> Installed Files -> " +
                "\"Verify integrity of game files\", then delete the leftover .ct-backup files and " +
                EditsFile + ". Until then the replaced sounds are STILL in your game.").ToString();
        }

        private struct Rep { internal uint Media; internal string File; }

        /// <summary>
        /// What this project replaces: the `"sounds"` declarations (which keep the author's own
        /// filenames) plus the `&lt;mediaId&gt;.ext` convention for anything not declared. Refuses two
        /// files aimed at one media rather than letting one silently win.
        /// </summary>
        private static List<Rep> Replacements(string root, StringBuilder log)
        {
            string dir = ReplaceDir(root);
            List<Rep> reps = new List<Rep>();
            if (!Directory.Exists(dir)) return reps;

            foreach (ContentProject.SoundEntry s in ContentProject.ParseSounds(File.ReadAllText(Path.Combine(root, "ppcontent.json"))))
            {
                string p = Path.Combine(dir, s.File);
                if (!File.Exists(p))
                    throw new InvalidDataException("\"sounds\" names '" + s.File + "' for media " + s.Media +
                                                   ", and there is no such file in " + dir);
                reps.Add(new Rep { Media = s.Media, File = p });
            }
            foreach (string f in Sources(dir))
            {
                bool declared = false;
                foreach (Rep r in reps) if (string.Equals(r.File, f, StringComparison.OrdinalIgnoreCase)) declared = true;
                uint id;
                if (declared || !uint.TryParse(Path.GetFileNameWithoutExtension(f), out id)) continue;
                reps.Add(new Rep { Media = id, File = f });
            }
            for (int i = 0; i < reps.Count; i++)
                for (int j = i + 1; j < reps.Count; j++)
                    if (reps[i].Media == reps[j].Media)
                        throw new InvalidDataException("two files aim at media " + reps[i].Media + ": '" +
                            Path.GetFileName(reps[i].File) + "' and '" + Path.GetFileName(reps[j].File) + "'");
            log.AppendLine("declared " + reps.Count + " replacement(s) in " + dir);
            return reps;
        }

        /// <summary>
        /// Where a mod keeps the replacement banks it SHIPS. One file per replaced media, loaded at
        /// init by <see cref="SoundLoad"/> - the shape the corrected architecture asks for: the mod
        /// carries its own content and the engine mod plays it. Nothing in the install is touched.
        /// </summary>
        internal const string ShippedBanks = "Dist\\Sounds";

        /// <summary>
        /// Builds one media-only bank per replacement and writes it into the MOD's own folder. Same
        /// sources, same decode and the same <see cref="WwisePcm.BuildWem"/> as `apply`; the only
        /// difference is where the result goes - and that no game file is opened for writing.
        ///
        /// The shipped media is still READ, for one reason: its `smpl` loop region. A looping Sound
        /// says only "loop forever" and the media says WHERE, so a replacement that drops the region
        /// restarts early (measured, 6a89bd4).
        /// </summary>
        private static string Bake(string projectName)
        {
            StringBuilder log = new StringBuilder();
            string root = ContentToolMain.ProjectDir(projectName);
            if (!File.Exists(Path.Combine(root, "ppcontent.json"))) return "REFUSED: no ppcontent.json in " + root;
            string modId = ProjectId(root);
            string outDir = Path.Combine(root, ShippedBanks);
            Directory.CreateDirectory(outDir);

            List<Rep> reps = Replacements(root, log);
            if (reps.Count == 0) return log.Append("ct_sound bake: nothing declared").ToString();

            List<uint> baked = new List<uint>();
            foreach (Rep r in reps)
            {
                if (!IdIndex.IsPpMedia(r.Media))
                    return log.Append("bake REFUSED " + r.Media + " is not one of the " + IdIndex.MediaCount +
                                      " media IDs Phoenix Point owns - nothing would ever play it").ToString();

                int channels, rate;
                byte[] pcm16;
                string reason = ReadPcm(r.File, out pcm16, out channels, out rate);
                if (reason != null) return log.Append("bake REFUSED " + Path.GetFileName(r.File) + " " + reason).ToString();

                // The target's own loop declaration, read from the SHIPPED file when there is one.
                string ignored;
                string wem = WemPath(r.Media, out ignored);
                WwiseWem.Info target = wem == null ? null : WwiseWem.Parse(File.ReadAllBytes(wem));
                long frames = pcm16.Length / (2L * Math.Max(1, channels));
                bool loops = target != null && target.HasLoop;
                byte[] media = WwisePcm.BuildWem(pcm16, channels, rate, loops ? frames : 0,
                                                 loops ? target.LoopPlayCount : 0u);

                uint bankId = WwiseId.Hash(modId.ToLowerInvariant() + "_" + r.Media);
                byte[] bank = BankGen.BuildMediaOnly(bankId, r.Media, media);
                string path = Path.Combine(outDir, r.Media + ".bnk");
                File.WriteAllBytes(path, bank);
                baked.Add(r.Media);
                log.AppendLine("baked " + path + ": " + bank.Length + " B, bankId=" + bankId + ", media " + r.Media +
                               " = " + Ms(WwiseWem.Parse(media)) + "ms " + channels + "ch " + rate + "Hz" +
                               (loops ? ", loop 0.." + (frames - 1) + " play count " + target.LoopPlayCount : ", no loop region") +
                               " from " + Path.GetFileName(r.File));
            }
            // Completing the bake includes taking BACK what the project has dropped: SoundLoad loads
            // every .bnk in this folder, so a replacement removed from ppcontent.json would keep
            // playing here and would ship inside the package. Only banks THIS bake stamped are
            // removed (BankPrune's name-and-BKHD rule) - never a file the modder put there.
            string swept = BankPrune.Sweep(outDir, modId, baked);
            if (swept != null) log.AppendLine(swept);
            log.Append("ct_sound bake: " + reps.Count + " bank(s) in " + outDir +
                       " - NO game file was opened for writing. ContentTool loads these at init.");
            return log.ToString();
        }

        /// <summary>
        /// The three questions the shape-C redesign hangs on, in ONE run and with nothing written to
        /// disk: (C1) does a media-only bank replace a STREAMED shipped media, (C2) does swapping it
        /// need an UnloadBank first, (C3) does our load survive the game loading that bank again
        /// afterwards, and (C-restore) whether unloading ours gives the shipped media back - it does
        /// NOT, and that arm now measures the failure instead of asserting the wish. Every expectation is a length this run generated, and the control is the
        /// shipped media's own length measured before anything is loaded.
        /// </summary>
        private static string ShapeC(string mediaArg)
        {
            uint id = ProbeMedia;
            if (!string.IsNullOrEmpty(mediaArg) && !uint.TryParse(mediaArg, out id)) return "usage: ct_sound shapec [mediaId]";
            StringBuilder log = new StringBuilder();
            Bank bank = BankOf(id);
            if (bank == null) return "C VOID no shipped bank .txt names media " + id;
            uint ev;
            string evName = EventFor(bank, bank.Media[id], out ev);
            if (evName == null) return "C VOID bank " + bank.Name + " declares no event for '" + bank.Media[id] + "'";

            string why;
            string wem = WemPath(id, out why);
            bool streamed = wem != null;
            int shippedMs = streamed ? Ms(WwiseWem.Parse(File.ReadAllBytes(wem))) : -1;

            // Two lengths of ours, both deliberately unlike the shipped one and unlike each other.
            byte[] one = WwisePcm.BuildWem(Sine(ProbeFreq, ProbeRate, 22050), 1, ProbeRate);   // 500 ms
            byte[] two = WwisePcm.BuildWem(Sine(ProbeFreq, ProbeRate, 66150), 1, ProbeRate);   // 1500 ms
            uint bankIdA = WwiseId.Hash("ct_shapec_a"), bankIdB = WwiseId.Hash("ct_shapec_b");
            byte[] bankA = BankGen.BuildMediaOnly(bankIdA, id, one);
            byte[] bankB = BankGen.BuildMediaOnly(bankIdB, id, two);
            log.AppendLine("target " + id + " '" + bank.Media[id] + "' in " + bank.Name + ", event " + evName +
                           " | shipped media is " + (streamed ? "STREAMED, " + shippedMs + "ms" : "EMBEDDED (no loose file)") +
                           " | our banks: " + bankA.Length + " B (500ms) and " + bankB.Length + " B (1500ms)");

            int fail = 0;
            GameObject emitter = new GameObject("ct_shapec");
            try
            {
                AkSoundEngine.RegisterGameObj(emitter, "ct_shapec");
                uint loaded;
                log.AppendLine("LoadBank(" + bank.Name + "): " + AkSoundEngine.LoadBank(bank.Name, out loaded));

                // CONTROL first, before any bank of ours exists: the shipped length, from the engine.
                log.AppendLine(AudioProbe.Post(emitter, ev, "C-control/shipped"));
                int before = (int)Math.Round(AudioProbe.Duration);
                if (!AudioProbe.GotDuration) return log.Append("C VOID the shipped event reported no duration, so nothing below is measurable").ToString();
                fail += Check(log, "C-control", streamed ? Math.Abs(before - shippedMs) <= TolMs : before > 0,
                    "the untouched event is " + before + "ms" + (streamed ? " and its file says " + shippedMs + "ms" : ""));

                // C1 - does a media-only bank replace it at all?
                log.AppendLine(AudioProbe.LoadBank(bankA, bankIdA, out loaded));
                log.AppendLine(AudioProbe.Post(emitter, ev, "C1/after-media-only-bank"));
                int after = (int)Math.Round(AudioProbe.Duration);
                fail += Check(log, "C1", AudioProbe.GotDuration && Math.Abs(after - 500) <= TolMs,
                    "the same event now decodes " + after + "ms, which is OUR 500ms media, not the shipped " +
                    before + "ms - on a " + (streamed ? "STREAMED" : "EMBEDDED") + " target, with NO game file touched");

                // C2 - can a second bank swap it without unloading the first?
                log.AppendLine(AudioProbe.LoadBank(bankB, bankIdB, out loaded));
                log.AppendLine(AudioProbe.Post(emitter, ev, "C2/second-bank-no-unload"));
                int swapped = (int)Math.Round(AudioProbe.Duration);
                fail += Check(log, "C2", AudioProbe.GotDuration && Math.Abs(swapped - 1500) <= TolMs,
                    "a SECOND media-only bank, loaded without unloading the first, wins: " + swapped +
                    "ms (ours is 1500ms; the first was 500ms)");

                // C3 - does the game loading ITS bank again undo us?
                AKRESULT r = AkSoundEngine.LoadBank(bank.Name, out loaded);
                log.AppendLine("LoadBank(" + bank.Name + ") AFTER ours: " + r);
                log.AppendLine(AudioProbe.Post(emitter, ev, "C3/after-game-bank-reload"));
                int reloaded = (int)Math.Round(AudioProbe.Duration);
                fail += Check(log, "C3", AudioProbe.GotDuration && Math.Abs(reloaded - 1500) <= TolMs,
                    "the game's own bank re-loaded after ours does NOT take the sound back: " + reloaded +
                    "ms, still ours (1500ms)");

                // C-restore - and it asserts the OPPOSITE of what it used to. The old arm claimed
                // unloading both replacement banks put the shipped media back and recorded "measured
                // 2026-08-13, ALL PASS"; a full re-run 2026-08-27 (vanilla 1800ms FILE -> A 500ms
                // MEMORY -> B 1500ms -> unload both) never reproduced it: the event reports
                // NO-DURATION-CB, dies at 18 ms, and the media stays dead for the rest of the session.
                // A replacement bank is ONE-WAY per session, which is exactly why SoundLoad never
                // unloads one (SoundLoad.cs, "LOADED ONCE, NEVER UNLOADED") - the production comment
                // was the true account all along and this arm was the one lying.
                AkSoundEngine.UnloadBank(bankIdA, IntPtr.Zero);
                AkSoundEngine.UnloadBank(bankIdB, IntPtr.Zero);
                log.AppendLine(AudioProbe.Post(emitter, ev, "C-restore/after-unloading-ours"));
                int restored = (int)Math.Round(AudioProbe.Duration);
                fail += Check(log, "C-restore", !AudioProbe.GotDuration || restored * 2 < before,
                    "unloading our banks does NOT put the shipped sound back - the media is dead for " +
                    "the rest of the session: " + (AudioProbe.GotDuration ? restored + "ms" : "no duration callback") +
                    " where the shipped media was " + before + "ms");
            }
            catch (Exception ex) { return log.Append("C THREW ").Append(ex).ToString(); }
            finally
            {
                try { AkSoundEngine.UnregisterGameObj(emitter); } catch (Exception) { }
                UnityEngine.Object.Destroy(emitter);
            }
            log.Append(fail == 0 ? "ct_sound shapec: ALL PASS (no game file was written)"
                                 : "ct_sound shapec: " + fail + " FAILURE(S)");
            return log.ToString();
        }

        /// <summary>
        /// Posts one shipped media's event and prints what the engine reports, replaced or not. This
        /// exists to measure a PRECONDITION rather than a claim: the menu music reports dur=0, and the
        /// only way to know whether that is a property of the sound or of the file underneath it is to
        /// ask the same question on a PRISTINE install. Writes nothing.
        /// </summary>
        /// <summary>
        /// Posts one event BY ID and reads back what the engine served - the case
        /// <see cref="Probe"/> cannot reach, because probing by media ID needs a bank that declares
        /// an event for that media and plenty of media have none. Two commands hand out event IDs
        /// with nothing that took them: `ct_voices` reports what the game posted, and a mod's own
        /// added events are hashed names. This is that missing half.
        ///
        /// Every shipped bank whose .txt declares the event is LOADED first, exactly as
        /// <see cref="Probe"/> loads the one it found. Measured: without it, posting the very event
        /// the game itself had posted seconds earlier returned playingID=0 - a bank the game is done
        /// with is not resident, and an unresident event does not start. An event no .txt names is
        /// still posted, because that is the case of a mod's own bank, which is already loaded.
        /// Writes nothing.
        /// </summary>
        private static string ProbeEvent(string eventArg)
        {
            uint ev;
            if (string.IsNullOrEmpty(eventArg) || !uint.TryParse(eventArg, out ev))
                return "usage: ct_sound probe event <eventId>";

            string named = MediaOfEvent(ev);
            StringBuilder log = new StringBuilder();
            log.AppendLine("probe event " + ev + ": " +
                           (named ?? "no shipped bank .txt names this event (a mod's own event, or a bank that ships no listing)"));

            foreach (Bank b in Banks())
            {
                bool declares = false;
                foreach (KeyValuePair<string, uint> e in b.Events) if (e.Value == ev) { declares = true; break; }
                if (!declares) continue;
                uint bankId;
                log.AppendLine("LoadBank(" + b.Name + "): " + AkSoundEngine.LoadBank(b.Name, out bankId));
            }

            GameObject emitter = new GameObject("ct_sound_probe_event");
            try
            {
                AkSoundEngine.RegisterGameObj(emitter, "ct_sound_probe_event");
                log.Append(AudioProbe.Post(emitter, ev, "event/" + ev));
            }
            catch (Exception ex) { return log.Append("probe THREW ").Append(ex).ToString(); }
            finally
            {
                try { AkSoundEngine.UnregisterGameObj(emitter); } catch (Exception) { }
                UnityEngine.Object.Destroy(emitter);
            }
            return log.ToString();
        }

        private static string Probe(string mediaArg)
        {
            uint id;
            if (string.IsNullOrEmpty(mediaArg) || !uint.TryParse(mediaArg, out id))
                return "usage: ct_sound probe <mediaId>";
            StringBuilder log = new StringBuilder();
            string why;
            string wem = WemPath(id, out why);
            if (wem == null) return "probe VOID " + why;
            Bank b = BankOf(id);
            if (b == null) return "probe VOID no shipped bank .txt names media " + id;
            uint ev;
            string evName = EventFor(b, b.Media[id], out ev);
            if (evName == null) return "probe VOID bank " + b.Name + " declares no event for '" + b.Media[id] + "'";

            log.AppendLine("probe " + id + " '" + b.Media[id] + "' in " + b.Name + ": on disk " + Sha1(wem) + " " +
                           Ms(WwiseWem.Parse(File.ReadAllBytes(wem))) + "ms, legacy .ct-backup " +
                           (File.Exists(wem + ".ct-backup") ? "PRESENT (an older ContentTool overwrote this " +
                            "shipped file - see 'ct_sound status')" : "absent, so this file is the shipped one"));

            GameObject emitter = new GameObject("ct_sound_probe");
            try
            {
                uint bankId;
                log.AppendLine("LoadBank(" + b.Name + "): " + AkSoundEngine.LoadBank(b.Name, out bankId));
                AkSoundEngine.RegisterGameObj(emitter, "ct_sound_probe");
                log.Append(AudioProbe.Post(emitter, ev, "probe/" + evName));
            }
            catch (Exception ex) { return log.Append("probe THREW ").Append(ex).ToString(); }
            finally
            {
                try { AkSoundEngine.UnregisterGameObj(emitter); } catch (Exception) { }
                UnityEngine.Object.Destroy(emitter);
            }
            return log.ToString();
        }

        /// <summary>
        /// Offline arms. Nothing in the game folder is written: the refusals, the ledger round trip
        /// and the byte-identity of a backup+restore are all provable on a scratch file, and a gate
        /// that needs a restart to check its own bookkeeping would never be run.
        /// </summary>
        private static string SelfTest()
        {
            StringBuilder log = new StringBuilder();
            int fail = 0;

            // The refusal arms, against the real index rather than a fixture.
            fail += Check(log, "S1-notmedia", !IdIndex.IsPpMedia(0xC7000001),
                "the tool's own allocation range is NOT a shipped media, so a .wav named after one is refused (index holds " +
                IdIndex.MediaCount + " media IDs)");
            fail += Check(log, "S1-ismedia", IdIndex.IsPpMedia(ProbeMedia),
                ProbeMedia + " IS a shipped media ID, so the probe is accepted");

            // The optional "sounds" declarations. Parsed from real JSON text, and the arm that matters
            // is the NAME survival: the whole point of the key is that a file the author called
            // 'bobr kurva.mp3' keeps that name, spaces, commas, Cyrillic and all.
            try
            {
                List<ContentProject.SoundEntry> s = ContentProject.ParseSounds(
                    "{\"id\":\"x\",\"bundle\":\"x.bundle\",\"sounds\":[" +
                    "{\"media\":208540756,\"file\":\"bobr kurva.mp3\"}," +
                    "{\"file\":\"Ублюдок, мать твою.mp3\",\"media\":\"423563089\"}]}");
                fail += Check(log, "S1-sounds", s.Count == 2 &&
                    s[0].Media == 208540756 && s[0].File == "bobr kurva.mp3" &&
                    s[1].Media == 423563089 && s[1].File.EndsWith(", мать твою.mp3"),
                    "two \"sounds\" entries read, field ORDER free and a quoted media accepted, both names " +
                    "kept verbatim: '" + (s.Count > 0 ? s[0].File : "") + "' and '" + (s.Count > 1 ? s[1].File : "") + "'");
            }
            catch (Exception ex) { log.AppendLine("S1-sounds VOID the parser threw: " + ex.Message); fail++; }

            fail += Check(log, "S1-sounds-empty", ContentProject.ParseSounds("{\"id\":\"x\"}").Count == 0,
                "a project with no \"sounds\" key reads 0 entries - the filename convention is untouched");

            bool refused = false;
            try { ContentProject.ParseSounds("{\"sounds\":[{\"file\":\"a.mp3\"}]}"); }
            catch (InvalidDataException) { refused = true; }
            fail += Check(log, "S1-sounds-incomplete", refused,
                "an entry with no \"media\" is REFUSED by name rather than parsing to silence");

            string whyEmbedded;
            // An ID the index owns but that has no loose file must be refused BY NAME. Measured over
            // the whole shipped set rather than assumed: the first index entry with no .wem on disk.
            uint embedded = 0;
            foreach (uint id in new uint[] { 272177053, 44432508 })
                if (IdIndex.IsPpMedia(id) && WemPath(id, out whyEmbedded) == null) { embedded = id; break; }
            if (embedded == 0) log.AppendLine("S1-embedded VOID no known embedded-only media to test with");
            else
            {
                WemPath(embedded, out whyEmbedded);
                fail += Check(log, "S1-embedded", whyEmbedded != null && whyEmbedded.Contains("EMBEDDED"),
                    "media " + embedded + " is in a bank, not a file, and is refused by name -> " + whyEmbedded);
            }

            // The measurement itself must be able to tell the two files apart, or the gate is void.
            // The shipped media is READ, never opened for writing.
            string why;
            string src = WemPath(ProbeMedia, out why);
            if (src == null) log.AppendLine("S1-distinct: " + why);
            byte[] probe = WwisePcm.BuildWem(Sine(ProbeFreq, ProbeRate, ProbeFrames), 1, ProbeRate);
            int probeMs = Ms(WwiseWem.Parse(probe));
            int shippedMs = src == null ? -1 : Ms(WwiseWem.Parse(File.ReadAllBytes(src)));
            fail += Check(log, "S1-distinct", probeMs > 0 && shippedMs > 0 && Math.Abs(probeMs - shippedMs) > 2 * TolMs,
                "the probe is " + probeMs + "ms and the shipped media " + shippedMs + "ms - " +
                Math.Abs(probeMs - shippedMs) + "ms apart, well outside the +/-" + TolMs + " the gate allows");

            // The event the gate posts is READ from the shipped bank listing, not hardcoded.
            Bank b = BankOf(ProbeMedia);
            uint ev = 0;
            string evName = b == null ? null : EventFor(b, b.Media[ProbeMedia], out ev);
            fail += Check(log, "S1-bankindex", b != null && ev != 0,
                b == null ? "no bank .txt names " + ProbeMedia
                          : "media " + ProbeMedia + " is '" + b.Media[ProbeMedia] + "' in " + b.Name +
                            ", posted through event '" + evName + "'=" + ev + " (" + b.Media.Count +
                            " streamed media, " + b.Events.Count + " events in that bank)");

            // The Start/Stop spelling, read off the shipped bank that made this rule necessary: a
            // music track's event is not named after its sound, so the exact-name-only lookup came out
            // VOID on it. Both halves are asserted - the sound has NO same-named event, and the
            // 'Start' one exists and is the one that would be posted.
            Bank music = null;
            foreach (Bank cand in Banks()) if (cand.Name == "MainMenuMusic") { music = cand; break; }
            if (music == null || !music.Media.ContainsKey(MenuMusicMedia))
                log.AppendLine("S1-eventname VOID this install ships no MainMenuMusic bank declaring media " + MenuMusicMedia);
            else
            {
                string sound = music.Media[MenuMusicMedia];
                uint direct, viaStart;
                bool noDirect = !music.Events.TryGetValue(sound, out direct);
                string picked = EventFor(music, sound, out viaStart);
                fail += Check(log, "S1-eventname", noDirect && picked == sound + "Start" && viaStart != 0,
                    "media " + MenuMusicMedia + " is the sound '" + sound + "' in MainMenuMusic.bnk, which declares " +
                    "NO event by that name; the gate posts '" + picked + "'=" + viaStart);
            }

            log.Append(fail == 0 ? "ct_sound selftest: ALL PASS (no game file was written)"
                                 : "ct_sound selftest: " + fail + " FAILURE(S)");
            return log.ToString();
        }

        // ---------------------------------------------------------------- the shipped bank listing

        /// <summary>
        /// What one shipped &lt;bank&gt;.txt says: the streamed media it owns, and its events by name.
        /// Wwise writes these next to the .bnk and Phoenix Point ships all 53, so the gate's target,
        /// its event and its control are all READ rather than hardcoded.
        /// </summary>
        private sealed class Bank
        {
            internal string Name;
            internal readonly Dictionary<uint, string> Media = new Dictionary<uint, string>();
            internal readonly Dictionary<string, uint> Events = new Dictionary<string, uint>(StringComparer.Ordinal);
        }

        private static List<Bank> banks;

        /// <summary>The bank whose STREAMED media list contains that ID, or null.</summary>
        private static Bank BankOf(uint mediaId)
        {
            foreach (Bank b in Banks()) if (b.Media.ContainsKey(mediaId)) return b;
            return null;
        }

        private static List<Bank> Banks()
        {
            if (banks != null) return banks;
            banks = new List<Bank>();
            foreach (string rel in LooseFiles.Find(AudioRoot, ".txt", null))
            {
                Bank b = ReadBank(Path.Combine(AudioRoot, rel.Replace('/', Path.DirectorySeparatorChar)));
                if (b != null) banks.Add(b);
            }
            return banks;
        }

        /// <summary>
        /// Tab-separated, section headers at column 0 and rows indented - Wwise's own SoundBank
        /// definition file. Only two sections are read: "Event" (name -&gt; id) and "Streamed Audio"
        /// (id -&gt; name). "In Memory Audio" is deliberately NOT read: those media are inside the
        /// bank and this command cannot reach them.
        /// </summary>
        private static Bank ReadBank(string path)
        {
            Bank b = new Bank { Name = Path.GetFileNameWithoutExtension(path) };
            string section = null;
            foreach (string line in File.ReadLines(path))
            {
                if (line.Length == 0) continue;
                if (line[0] != '\t' && line[0] != ' ')
                {
                    int tab = line.IndexOf('\t');
                    section = tab < 0 ? line : line.Substring(0, tab);
                    continue;
                }
                string[] f = line.Split('\t');
                if (f.Length < 3) continue;
                uint id;
                if (!uint.TryParse(f[1].Trim(), out id)) continue;
                string name = f[2].Trim();
                if (name.Length == 0) continue;
                if (section == "Event") b.Events[name] = id;
                else if (section == "Streamed Audio") b.Media[id] = name;
            }
            return b.Media.Count > 0 || b.Events.Count > 0 ? b : null;
        }

        /// <summary>
        /// The event this gate posts to hear one sound, and its name - or null when the bank declares
        /// none. Wwise names an event after the sound for a one-shot (GUI_StatsPlusClick plays the
        /// sound GUI_StatsPlusClick), but a track that has to be stopped again carries a PAIR:
        /// MainMenuMusic.bnk ships MainMenuMusicStart / MainMenuMusicStop for the sound MainMenuMusic.
        /// Both spellings are read off the shipped &lt;bank&gt;.txt; nothing here is hardcoded, and a
        /// bank that spells its events some third way is refused BY NAME rather than guessed at.
        /// </summary>
        private static string EventFor(Bank b, string soundName, out uint eventId)
        {
            if (b.Events.TryGetValue(soundName, out eventId)) return soundName;
            if (b.Events.TryGetValue(soundName + "Start", out eventId)) return soundName + "Start";
            eventId = 0;
            return null;
        }

        /// <summary>
        /// THE OTHER DIRECTION, and the one an author actually needs: an event ID the GAME posted,
        /// back to the media ID that event plays.
        ///
        /// ct_voices reports what the game posts, which is EVENT ids, while every replacement is keyed
        /// on a MEDIA id - so without this the instrument that answers "which sound did that button
        /// make" hands back a number no other command takes. Both halves are already in the shipped
        /// &lt;bank&gt;.txt this file reads: "Event" gives name -&gt; id, "Streamed Audio" gives id -&gt; name,
        /// and <see cref="EventFor"/>'s naming rule (event = sound, or sound + "Start") joins them.
        /// Nothing is hardcoded and nothing new is parsed.
        ///
        /// A &lt;sound&gt;Stop is NOT mapped onto that sound: it stops a playback already running and
        /// plays no media, so it is reported AS a stop event with nothing to replace.
        ///
        /// Returns null when no shipped bank .txt names the event - a mod's own event, or one whose
        /// bank ships no listing. Never throws: it runs inside a Harmony-driven report.
        /// </summary>
        internal static string MediaOfEvent(uint eventId)
        {
            try
            {
                // ALL the banks that declare it, not the first one found: several banks list the same
                // event (MainMenuMusicStart is in MainMenuMusic.txt AND TacticalMusic.txt), and
                // reporting whichever the scan reached last named the wrong .bnk for the sound.
                string evName = null;
                List<string> evBanks = new List<string>();
                foreach (Bank b in Banks())
                    foreach (KeyValuePair<string, uint> ev in b.Events)
                        if (ev.Value == eventId) { evName = ev.Key; evBanks.Add(b.Name); break; }
                if (evName == null) return null;
                evBanks.Sort(StringComparer.Ordinal);
                string evBank = string.Join(", ", evBanks.ToArray());

                // The sound behind the event, by Wwise's own naming. The event's OWN name is tried
                // FIRST and that is measured, not assumed: over the 53 shipped bank .txt files, four
                // events carry a suffix and still declare streamed media of exactly their own name -
                // StatXPBangupStop (media 300750976 in UI.txt), StatXPBangupStart, ArmaRamStart and
                // GUI_AreaScanStart - so stripping before looking walks straight past the media the
                // event actually plays.
                string sound = evName;
                Dictionary<uint, string> found = StreamedFor(sound);

                // Only a <sound>Start falls back to the pair spelling (MainMenuMusic.bnk ships
                // MainMenuMusicStart for the sound MainMenuMusic). A <sound>Stop does NOT: it stops a
                // playback that is already running and plays nothing itself, so mapping
                // MainMenuMusicStop -> media 208540756 'MainMenuMusic' and calling it replaceable sent
                // an author to overwrite a file that event never plays. Of the 10 shipped Stop events
                // only StatXPBangupStop declares media, and the own-name lookup above already has it.
                if (found.Count == 0 && evName.EndsWith("Start", StringComparison.Ordinal) && evName.Length > 5)
                {
                    sound = evName.Substring(0, evName.Length - "Start".Length);
                    found = StreamedFor(sound);
                }
                else if (found.Count == 0 && evName.EndsWith("Stop", StringComparison.Ordinal) && evName.Length > 4)
                    return "'" + evName + "' in " + evBank + " -> STOP event: it ends a '" +
                           evName.Substring(0, evName.Length - "Stop".Length) + "' that is already " +
                           "playing and declares no media of its own, so there is nothing to replace " +
                           "through it - look up the Start event of that sound instead";

                List<uint> keys = new List<uint>(found.Keys);
                keys.Sort();
                List<string> hits = new List<string>();
                foreach (uint k in keys) hits.Add("media " + k + " '" + found[k] + "'");

                if (hits.Count == 0)
                    return "'" + evName + "' in " + evBank + " -> no STREAMED media named '" + sound +
                           "', so its sound is embedded in a bank and cannot be replaced by a media bank";
                return "'" + evName + "' in " + evBank + " -> " + string.Join(", ", hits.ToArray()) +
                       (hits.Count > 1 ? " - replace ALL of them, the event picks between them" : " - replaceable");
            }
            catch (Exception) { }
            return null;
        }

        /// <summary>
        /// Every STREAMED media one sound name owns, ACROSS ALL BANKS and by prefix. Both halves were
        /// measured, not assumed: the event StatXPBangupStop is listed in UIGeoscape.txt while media it
        /// names is listed in UI.txt, so searching only the event's own bank finds nothing; and a random
        /// container fans one event out over several media (MissionWinShow -&gt; MissionWinShow_1 / _3),
        /// which an author replacing "that sound" needs all of. Deduped by media ID: UI.txt and
        /// UIGeoscape.txt list the same streamed media, and "replace all of them" must not count one
        /// file twice.
        /// </summary>
        private static Dictionary<uint, string> StreamedFor(string sound)
        {
            Dictionary<uint, string> found = new Dictionary<uint, string>();
            foreach (Bank b in Banks())
                foreach (KeyValuePair<uint, string> m in b.Media)
                    if (!found.ContainsKey(m.Key) &&
                        (string.Equals(m.Value, sound, StringComparison.Ordinal) ||
                         m.Value.StartsWith(sound + "_", StringComparison.Ordinal)))
                        found[m.Key] = m.Value;
            return found;
        }

        // ---------------------------------------------------------------- small shared helpers

        /// <summary>
        /// Milliseconds of one .wem, from its header alone. Vorbis carries a sample count; PCM does
        /// not, so its length is the data chunk over the frame size. -1 when the header did not parse.
        /// </summary>
        internal static int Ms(WwiseWem.Info i)
        {
            if (i == null || i.SampleRate <= 0) return -1;
            long frames = i.IsVorbis
                ? i.SampleCount
                : i.DataSize / Math.Max(1, i.Channels * Math.Max(1, i.BitsPerSample / 8));
            return (int)(frames * 1000L / i.SampleRate);
        }

        /// <summary>16-bit mono sine at half scale - the same generator ct_audio's tones use.</summary>
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

        private static string Sha1(string path) { return Sha1(File.ReadAllBytes(path)); }
        private static string Sha1(byte[] b) { return Project.Sha1.Hex(b).ToLowerInvariant(); }
    }
}
