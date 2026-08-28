using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Base.Audio;
using Base.Core;
using Base.Serialization;
using Base.Utils;
using PhoenixPoint.Common.Game;
using UnityEngine;

namespace Morgott.ContentTool.Dev
{
    /// <summary>
    /// WHERE IS THE GAME ACTUALLY SILENT - the measuring instrument for demo mod #2.
    ///
    /// The question "does this screen play music" cannot be answered from the shipped artefacts: every
    /// streamed media ID has a .wem on disk (0 missing, smallest 3971 B), so there is no empty
    /// placeholder whose absence would mark a silent surface. It has to be measured in a running game,
    /// on the screen itself.
    ///
    /// METHODOLOGY, and the whole reason this file exists: "nothing is playing" and "we measured
    /// nothing" look identical in a log and mean opposite things. So the instrument proves ITSELF in
    /// every run, out of the same data it reports: the query chain
    /// (GetPlayingIDsFromGameObject -&gt; GetEventIDFromPlayingID -&gt; GetSourcePlayPosition) is
    /// considered armed only once it has produced at least one voice whose source position ADVANCED
    /// between two reads. Until that happens the verdict is VOID, never SILENT. A voice that merely
    /// exists proves nothing either - a paused or stalled one holds its position.
    ///
    /// Emitters come from two independent sides so a miss on one does not read as silence:
    ///   - the game's own <see cref="WwiseBanksEventsTracker"/> (private dictionaries, by reflection) -
    ///     these are the exact GameObjects the LEVEL's start events were posted on, which is where
    ///     music lives (PhoenixPoint.Common.Levels\GamestateSound.cs:44-70);
    ///   - every <c>AkGameObj</c> in the scene - every registered Wwise emitter, which is what supplies
    ///     the non-music control voices.
    ///
    /// Event names and the music/not-music split are READ off the shipped &lt;bank&gt;.txt (the "Event"
    /// section's Wwise Object Path column), never hardcoded: an event under <c>\Music\</c>, or one
    /// declared by a bank whose name carries "Music", is music. Nothing in this file is a constant ID.
    ///
    /// ponytail: its own 20-line &lt;bank&gt;.txt reader instead of SoundReplace's. That one keeps
    /// streamed media and drops the object-path column this needs, and it is being edited by a parallel
    /// session; upgrade path is one shared reader once both slices land.
    /// DEV ONLY - a measuring tool, never on any shipping path.
    /// </summary>
    internal static class MusicProbe
    {
        /// <summary>Gap between the two source-position reads. Long enough that a real voice moves by
        /// far more than the millisecond rounding, short enough not to stall the game visibly.</summary>
        private const int GapMs = 600;

        /// <summary>Below this the two reads are indistinguishable from rounding, so the voice does not
        /// count as advancing.
        ///
        /// MEASURED 2026-08-12, and it is why advancement is NOT the arming rule: on a voice the GAME
        /// posted, <c>GetSourcePlayPosition</c> returns <c>AK_Fail</c>. Wwise only tracks a source
        /// position when the post asked for it (<c>AK_EnableGetSourcePlayPosition</c>), and Phoenix Point
        /// does not - the main menu read `MUSIC playingID=2 event=799408924 'MainMenuMusicStart'
        /// ... pos=AK_Fail`, a correctly identified, genuinely audible track. Position stays REPORTED
        /// (a voice we posted ourselves would carry one) but nothing is gated on it.</summary>
        private const int AdvanceMs = 50;

        private const int MaxPlayingIds = 64;

        /// <summary>How long <see cref="Probe"/> keeps re-asking before it gives up and reads VOID. A
        /// screen is not measured at one instant: a level that has just come up may not have posted its
        /// start events yet, and reading that as silence is the exact mistake this file exists to
        /// prevent.</summary>
        private const int DefaultWaitSeconds = 8;

        internal static string Run(string[] args)
        {
            string cmd = args != null && args.Length > 0 ? args[0].ToLowerInvariant() : "probe";
            switch (cmd)
            {
                case "probe":
                    int secs;
                    if (args == null || args.Length < 2 || !int.TryParse(args[1], out secs)) secs = DefaultWaitSeconds;
                    return Probe(secs);
                case "gate":
                    if (args.Length < 2) return "usage: ct_music gate <savename>   (ct_mission list prints the names)";
                    return SaveGate.Arm(string.Join(" ", args.Skip(1).ToArray()));
                default:
                    return "usage: ct_music [probe [waitSeconds] | gate <savename>]";
            }
        }

        /// <summary>
        /// Everything audible right now, re-asked until the instrument arms or the budget runs out.
        /// Only the LAST attempt is reported: an early attempt that found nothing is not evidence of
        /// anything, and printing it would read like a silent screen.
        /// </summary>
        internal static System.Collections.IEnumerator Measure(int waitSeconds, StringBuilder log)
        {
            float start = Time.realtimeSinceStartup;
            for (int attempt = 1; ; attempt++)
            {
                StringBuilder one = new StringBuilder();
                List<Voice> voices = Collect(one);
                yield return new WaitForSeconds(GapMs / 1000f);
                List<Voice> again = Collect(null);
                bool armed = Verdict(one, voices, again, attempt, Time.realtimeSinceStartup - start);
                if (armed || Time.realtimeSinceStartup - start >= waitSeconds) { log.Append(one); yield break; }
            }
        }

        /// <summary>Console entry: arm the coroutine, print from it. Nothing here can answer
        /// synchronously - see <see cref="Measure"/>.</summary>
        private static string Probe(int waitSeconds)
        {
            GameObject go = new GameObject("ct_music_probe");
            UnityEngine.Object.DontDestroyOnLoad(go);
            go.AddComponent<ProbeRunner>().Begin(waitSeconds);
            return "ct_music probe armed for up to " + waitSeconds + "s - the reading prints from the runner";
        }

        private sealed class ProbeRunner : MonoBehaviour
        {
            internal void Begin(int waitSeconds) { AsyncGate.Pending++; StartCoroutine(Go(waitSeconds)); }

            private System.Collections.IEnumerator Go(int waitSeconds)
            {
                StringBuilder log = new StringBuilder();
                yield return StartCoroutine(Measure(waitSeconds, log));
                ContentToolMain.Say(log.ToString().TrimEnd());
                AsyncGate.Pending--;
                Destroy(gameObject);
            }
        }

        /// <summary>Every emitter's live playing IDs. Called twice per attempt; the second call passes a
        /// null log because the emitter accounting is the same both times.</summary>
        private static List<Voice> Collect(StringBuilder log)
        {
            Dictionary<GameObject, string> emitters = Emitters(log ?? new StringBuilder());
            List<Voice> voices = new List<Voice>();
            int queried = 0, refused = 0;
            foreach (KeyValuePair<GameObject, string> e in emitters)
            {
                ulong akId = AkSoundEngine.GetAkGameObjectID(e.Key);
                uint n = MaxPlayingIds;
                uint[] ids = new uint[MaxPlayingIds];
                AKRESULT r = AkSoundEngine.GetPlayingIDsFromGameObject(akId, ref n, ids);
                if (r != AKRESULT.AK_Success) { refused++; continue; }
                queried++;
                for (uint i = 0; i < n && i < MaxPlayingIds; i++)
                    voices.Add(new Voice { Emitter = e.Key.name, Why = e.Value, PlayingId = ids[i] });
            }

            foreach (Voice v in voices)
            {
                v.EventId = AkSoundEngine.GetEventIDFromPlayingID(v.PlayingId);
                v.PosResult = AkSoundEngine.GetSourcePlayPosition(v.PlayingId, out v.Pos1, true);
            }
            Counts = emitters.Count + " emitter(s), " + queried + " queried, " + refused + " refused by Wwise";
            return voices;
        }

        /// <summary>What <see cref="Collect"/> saw, for the line <see cref="Verdict"/> prints.</summary>
        private static string Counts = "";

        /// <summary>The SECOND position read, the naming, and the reading. Returns whether the instrument
        /// armed - which is what decides between SILENT and VOID.</summary>
        private static bool Verdict(StringBuilder log, List<Voice> voices, List<Voice> again, int attempt, float waited)
        {
            HashSet<uint> stillThere = new HashSet<uint>();
            foreach (Voice v in again) stillThere.Add(v.PlayingId);
            foreach (Voice v in voices)
            {
                int pos2;
                AKRESULT r = AkSoundEngine.GetSourcePlayPosition(v.PlayingId, out pos2, true);
                v.Pos2 = pos2;
                v.PosResult2 = r;
                v.Advanced = v.PosResult == AKRESULT.AK_Success && r == AKRESULT.AK_Success &&
                             pos2 - v.Pos1 >= AdvanceMs;
                v.Persisted = stillThere.Contains(v.PlayingId);
                Event ev;
                v.Named = Events().TryGetValue(v.EventId, out ev);
                v.Name = v.Named ? ev.Name : "(no shipped bank .txt names it)";
                v.Path = ev == null ? "" : ev.Path;
                v.Bank = ev == null ? "" : ev.Bank;
                v.Music = ev != null && (ev.Path.StartsWith("\\Music\\", StringComparison.Ordinal) ||
                                         ev.Bank.IndexOf("Music", StringComparison.OrdinalIgnoreCase) >= 0);
            }

            log.AppendLine("ct_music probe (attempt " + attempt + ", " + waited.ToString("0.0") +
                           "s into the wait): " + Counts + ", " + voices.Count + " live voice(s), " +
                           Events().Count + " event(s) named by " + BankCount + " shipped bank .txt");
            foreach (Voice v in voices)
                log.AppendLine("  " + (v.Music ? "MUSIC " : "sound ") + "playingID=" + v.PlayingId +
                               " event=" + v.EventId + " '" + v.Name + "'" +
                               (v.Bank.Length == 0 ? "" : " bank=" + v.Bank) +
                               (v.Path.Length == 0 ? "" : " path=" + v.Path) +
                               " pos=" + (v.PosResult == AKRESULT.AK_Success && v.PosResult2 == AKRESULT.AK_Success
                                          ? v.Pos1 + "->" + v.Pos2 + "ms advancing=" + v.Advanced
                                          : v.PosResult.ToString() + " (not enabled at post time)") +
                               " persisted=" + v.Persisted + " on '" + v.Emitter + "' (" + v.Why + ")");

            // The instrument is armed by a voice that EXISTS and that the shipped bank .txt NAMES: that
            // is enumeration and identification both proven, on this screen, in this run. It is not the
            // stronger criterion this file was first written with - see the note on GetSourcePlayPosition
            // above - but it is the strongest one the API actually delivers for a voice the GAME posted,
            // and a run with zero named voices still reads VOID rather than SILENT.
            Voice armed = null, music = null;
            foreach (Voice v in voices)
            {
                if (v.Named && armed == null) armed = v;
                if (v.Named && v.Music && music == null) music = v;
            }
            if (armed == null)
                log.AppendLine("ct_music VOID - no live voice on any emitter could be NAMED off the shipped " +
                               "bank .txt, so the query chain identified nothing here. This is NOT evidence " +
                               "of silence: post a sound you can hear (ct_sound probe <mediaId>) and probe " +
                               "again while it plays.");
            else if (music != null)
                log.AppendLine("ct_music MUSIC IS PLAYING - '" + music.Name + "' (event " + music.EventId +
                               ", bank " + music.Bank + ") is live on '" + music.Emitter +
                               "' (persisted across the " + GapMs + " ms gap: " + music.Persisted +
                               "). This screen is OCCUPIED.");
            else
                log.AppendLine("ct_music SILENT (measured) - the chain is armed by '" + armed.Name +
                               "' (event " + armed.EventId + ", bank " + armed.Bank + ") live on '" +
                               armed.Emitter + "', and no live voice on any emitter is a music event. " +
                               "This screen has NO MUSIC.");
            return armed != null;
        }

        // ------------------------------------------------------------------ emitters

        /// <summary>
        /// Every GameObject worth asking. The tracker's three private dictionaries are where the LEVEL's
        /// own audio was registered; AkGameObj covers everything else that is registered with Wwise.
        /// A side that fails is REPORTED, because a missing side narrows what a silent reading means.
        /// </summary>
        private static Dictionary<GameObject, string> Emitters(StringBuilder log)
        {
            Dictionary<GameObject, string> found = new Dictionary<GameObject, string>();
            AudioManager am = GameUtl.GameComponent<AudioManager>();
            if (am == null || am.WwiseBanksEventsTracker == null)
                log.AppendLine("  emitters: AudioManager/WwiseBanksEventsTracker NOT reachable - the level's " +
                               "own start-event emitters are missing from this run");
            else
            {
                found[am.gameObject] = "AudioManager";
                object t = am.WwiseBanksEventsTracker;
                int n = 0;
                foreach (string field in new[] { "_eventsOnPlayingStart", "_eventsOnPlayEnd", "_loadedSoundBanks" })
                    n += Harvest(t, field, found);
                log.AppendLine("  emitters: tracker gave " + n + " GameObject(s)");
            }
            int ak = 0;
            foreach (AkGameObj g in UnityEngine.Object.FindObjectsOfType<AkGameObj>())
                if (g != null && !found.ContainsKey(g.gameObject)) { found[g.gameObject] = "AkGameObj"; ak++; }
            log.AppendLine("  emitters: AkGameObj gave " + ak + " more");
            return found;
        }

        /// <summary>
        /// One tracker dictionary, whichever way round it stores GameObjects: the start/end event maps
        /// are keyed BY emitter, the loaded-bank map holds the emitters in its VALUES.
        /// </summary>
        private static int Harvest(object tracker, string field, Dictionary<GameObject, string> into)
        {
            FieldInfo f = tracker.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic);
            System.Collections.IDictionary d = f == null ? null : f.GetValue(tracker) as System.Collections.IDictionary;
            if (d == null) return 0;
            int n = 0;
            foreach (System.Collections.DictionaryEntry e in d)
            {
                GameObject go = e.Key as GameObject;
                if (go != null) { if (!into.ContainsKey(go)) { into[go] = field; } n++; continue; }
                System.Collections.IEnumerable users = e.Value as System.Collections.IEnumerable;
                if (users == null) continue;
                foreach (object o in users)
                {
                    GameObject u = o as GameObject;
                    if (u != null && !into.ContainsKey(u)) { into[u] = field; n++; }
                }
            }
            return n;
        }

        // ------------------------------------------------------------------ unattended, on a real level

        /// <summary>
        /// The geoscape/tactical answer without a human at the keyboard. Loads a savegame by name
        /// through the game's own loader - the identical lever <c>ct_mission</c> pulls
        /// (Base.Serialization\SerializationCommands.cs:41) - waits for the LEVEL to reach Playing,
        /// which is the transition AudioManager posts the level's start events on
        /// (Base.Audio\AudioManager.cs:93), then probes.
        ///
        /// Unlike ct_mission this does NOT refuse a non-tactical save: the geoscape is the screen the
        /// question is about. Which level it landed on is REPORTED, so a save that turns out to be the
        /// other kind is visible rather than silently mislabelled.
        /// </summary>
        private sealed class SaveGate : MonoBehaviour
        {
            private const float LoadBudgetSeconds = 420f;
            private const float SettleSeconds = 6f;

            private string saveName, refusal;

            internal static string Arm(string saveName)
            {
                GameObject go = new GameObject("ct_music_gate");
                DontDestroyOnLoad(go);
                SaveGate g = go.AddComponent<SaveGate>();
                g.saveName = saveName;
                AsyncGate.Pending++;
                g.StartCoroutine(g.Gate());
                return "ct_music gate armed on save '" + saveName + "' - the reading prints from the runner " +
                       "once the level is playing";
            }

            private System.Collections.IEnumerator Gate()
            {
                StringBuilder log = new StringBuilder();
                try
                {
                    // The level we are LEAVING. Without this the loop matches the home screen, which is
                    // already Playing, and the settle below then dereferences a destroyed Level
                    // (measured: NullReferenceException in Object.get_name at t=7.58 s).
                    Base.Levels.Level before = GameUtl.CurrentLevel();
                    GameUtl.GameComponent<TimeSource>().Timing.Start(Load(saveName));
                    float start = Time.realtimeSinceStartup;
                    Base.Levels.Level lvl = null;
                    while (Time.realtimeSinceStartup - start < LoadBudgetSeconds)
                    {
                        if (refusal != null) break;
                        lvl = GameUtl.CurrentLevel();
                        if (lvl != null && lvl != before && lvl.CurrentState == Base.Levels.Level.State.Playing) break;
                        yield return new WaitForSeconds(1f);
                    }
                    if (refusal != null) { log.AppendLine("ct_music gate VOID " + refusal); yield break; }
                    if (lvl == null || lvl == before || lvl.CurrentState != Base.Levels.Level.State.Playing)
                    {
                        log.AppendLine("ct_music gate VOID - no NEW level reached Playing within " +
                                       LoadBudgetSeconds + "s of loading '" + saveName + "' (level=" +
                                       (lvl == null ? "(none)" : lvl.name + " state=" + lvl.CurrentState) +
                                       "), so nothing was measured on it");
                        yield break;
                    }
                    // Start events are posted ON the transition; a track fades in after it.
                    yield return new WaitForSeconds(SettleSeconds);
                    // Re-read: the settle is long enough for another transition to have happened.
                    lvl = GameUtl.CurrentLevel();
                    if (lvl == null)
                    {
                        log.AppendLine("ct_music gate VOID - the level went away during the " + SettleSeconds +
                                       "s settle, so nothing was measured on it");
                        yield break;
                    }
                    log.AppendLine("ct_music gate: level '" + lvl.name + "' is Playing after loading '" +
                                   saveName + "'");
                    yield return StartCoroutine(Measure(DefaultWaitSeconds, log));
                }
                finally
                {
                    ContentToolMain.Say(log.ToString().TrimEnd());
                    AsyncGate.Pending--;
                    Destroy(gameObject);
                }
            }

            /// <summary>Find the save BY NAME, refuse ambiguity with the offenders printed, then hand it
            /// to the game's own loader.</summary>
            private IEnumerator<NextUpdate> Load(string name)
            {
                ByRef<List<SavegameMetaData>> all = new ByRef<List<SavegameMetaData>>();
                yield return Timing.Current.Call(
                    GameUtl.GameComponent<SerializationComponent>().GetSavegames(all));
                List<SavegameMetaData> hits = (all.Value ?? new List<SavegameMetaData>())
                    .Where(m => string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase)).ToList();
                if (hits.Count == 0) { refusal = "REFUSED: no savegame named '" + name + "' (run 'ct_mission list')"; yield break; }
                if (hits.Count > 1)
                {
                    refusal = "REFUSED: " + hits.Count + " savegames answer to '" + name + "': " +
                              string.Join(", ", hits.Select(h => h.Path).ToArray());
                    yield break;
                }
                PPSavegameMetaData pp = hits[0] as PPSavegameMetaData;
                if (pp == null) { refusal = "REFUSED: '" + name + "' carries no PPSavegameMetaData"; yield break; }
                if (!pp.IsLoadable()) { refusal = "REFUSED: '" + name + "' declares saveType=" + pp.SaveType + " (not loadable)"; yield break; }
                ContentToolMain.Say("ct_music: loading save '" + pp.Name + "' v." + pp.Version +
                                    " (IsTacticalSave=" + pp.IsTacticalSave + ", saveType=" + pp.SaveType + ")");
                GameUtl.GameComponent<PhoenixGame>().FinishLevelAndLoadGame(pp);
            }
        }

        // ------------------------------------------------------------------ the shipped event listing

        private sealed class Voice
        {
            internal string Emitter, Why, Name = "", Path = "", Bank = "";
            internal uint PlayingId, EventId;
            internal int Pos1, Pos2;
            internal AKRESULT PosResult, PosResult2;
            internal bool Advanced, Music, Named, Persisted;
        }

        private sealed class Event
        {
            internal string Name, Path, Bank;
        }

        private static Dictionary<uint, Event> events;
        private static int BankCount;

        /// <summary>
        /// Every event the shipped banks declare, by ID, with the Wwise Object Path that says whether it
        /// is music. Tab-separated, section headers at column 0 and rows indented - Wwise's own format;
        /// the "Event" row is [ , id, name, , , object path, notes].
        /// </summary>
        private static Dictionary<uint, Event> Events()
        {
            if (events != null) return events;
            events = new Dictionary<uint, Event>();
            string root = Path.Combine(Application.streamingAssetsPath, "Audio");
            if (!Directory.Exists(root)) return events;
            foreach (string txt in Directory.GetFiles(root, "*.txt", SearchOption.AllDirectories))
            {
                BankCount++;
                string bank = Path.GetFileNameWithoutExtension(txt);
                string section = null;
                foreach (string line in File.ReadLines(txt))
                {
                    if (line.Length == 0) continue;
                    if (line[0] != '\t' && line[0] != ' ')
                    {
                        int tab = line.IndexOf('\t');
                        section = tab < 0 ? line : line.Substring(0, tab);
                        continue;
                    }
                    if (section != "Event") continue;
                    string[] f = line.Split('\t');
                    uint id;
                    if (f.Length < 3 || !uint.TryParse(f[1].Trim(), out id)) continue;
                    if (!events.ContainsKey(id))
                        events[id] = new Event
                        {
                            Name = f[2].Trim(),
                            Path = f.Length > 5 ? f[5].Trim() : "",
                            Bank = bank
                        };
                }
            }
            return events;
        }
    }
}
