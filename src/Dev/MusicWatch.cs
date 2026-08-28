using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using HarmonyLib;
using Morgott.ContentTool.Wwise;
using UnityEngine;

namespace Morgott.ContentTool.Dev
{
    /// <summary>
    /// Counts what the GAME posts, at the menu, over time - the measurement no gate arm could make.
    ///
    /// Every S1 arm posts the event ITSELF and watches one voice, so a game that re-posts the same
    /// event is invisible to all of them: the author hears copies piling up while `S1-alive` reads
    /// maximally green. This is the instrument for that question and nothing else. It patches
    /// `AkSoundEngine.PostEvent` (a prefix that only reads and logs), waits from a COROUTINE - never
    /// a main-thread loop, which would freeze the very player loop that drives the posting it is
    /// measuring - and prints a timeline plus a live voice count per checkpoint.
    ///
    /// DEV ONLY. Nothing here is on any shipping path.
    /// </summary>
    internal static class MusicWatch
    {
        private const string HarmonyId = "morgott.contenttool.musicwatch";
        private static readonly List<string> Timeline = new List<string>();
        /// <summary>Posts seen per event ID, and the game object each was posted on.</summary>
        private static readonly Dictionary<uint, int> Counts = new Dictionary<uint, int>();
        private static readonly Dictionary<uint, GameObject> Emitters = new Dictionary<uint, GameObject>();
        private static System.Diagnostics.Stopwatch clock;
        private static Harmony harmony;

        internal static string Run(string[] args)
        {
            int seconds = 20;
            if (args != null && args.Length > 1) int.TryParse(args[1], out seconds);
            if (seconds < 2) seconds = 2;

            if (harmony != null) return "ct_voices: already watching";
            Timeline.Clear(); Counts.Clear(); Emitters.Clear();
            clock = System.Diagnostics.Stopwatch.StartNew();

            int patched = 0;
            harmony = new Harmony(HarmonyId);
            HarmonyMethod prefix = new HarmonyMethod(AccessTools.Method(typeof(MusicWatch), nameof(Seen)));
            foreach (MethodInfo m in typeof(AkSoundEngine).GetMethods(BindingFlags.Public | BindingFlags.Static))
            {
                if (m.Name != "PostEvent") continue;
                ParameterInfo[] p = m.GetParameters();
                if (p.Length < 2 || p[0].ParameterType != typeof(uint) || p[1].ParameterType != typeof(GameObject)) continue;
                try { harmony.Patch(m, prefix: prefix); patched++; }
                catch (Exception) { }
            }
            if (patched == 0) { harmony = null; return "ct_voices VOID no AkSoundEngine.PostEvent(uint, GameObject, ...) overload could be patched"; }

            GameObject go = new GameObject("ct_music_watch");
            UnityEngine.Object.DontDestroyOnLoad(go);
            go.AddComponent<Runner>().Begin(seconds, patched);
            return "ct_voices: watching " + patched + " PostEvent overload(s) for " + seconds + "s - the report prints from the runner";
        }

        /// <summary>Harmony prefix: reads, never changes anything. Must not throw into the game.</summary>
        private static void Seen(uint in_eventID, GameObject in_gameObjectID)
        {
            try
            {
                int n;
                Counts.TryGetValue(in_eventID, out n);
                Counts[in_eventID] = n + 1;
                if (in_gameObjectID != null) Emitters[in_eventID] = in_gameObjectID;
                if (Timeline.Count < 400)
                    Timeline.Add(clock.ElapsedMilliseconds + "ms event=" + in_eventID + " on '" +
                                 (in_gameObjectID == null ? "(null)" : in_gameObjectID.name) + "'");
            }
            catch (Exception) { }
        }

        private sealed class Runner : MonoBehaviour
        {
            private int seconds, patched;

            internal void Begin(int forSeconds, int overloads)
            {
                seconds = forSeconds; patched = overloads;
                AsyncGate.Pending++;
                StartCoroutine(Watch());
            }

            private IEnumerator Watch()
            {
                StringBuilder log = new StringBuilder();
                try
                {
                    // Checkpoints, so a climbing count is visible as a climb rather than as a total.
                    int[] at = { 2, 6, 12, 20 };
                    foreach (int t in at)
                    {
                        if (t > seconds) break;
                        while (clock.ElapsedMilliseconds < t * 1000L) yield return null;
                        log.AppendLine("t=" + t + "s  posts so far: " + Describe() + " | live voices: " + Voices());
                    }
                    while (clock.ElapsedMilliseconds < seconds * 1000L) yield return null;

                    log.AppendLine("ct_voices timeline (" + Timeline.Count + " post(s) seen through " + patched + " overload(s)):");
                    foreach (string line in Timeline) log.AppendLine("  " + line);
                    log.AppendLine(Named());
                    log.Append("ct_voices: DONE after " + seconds + "s");
                }
                finally
                {
                    if (harmony != null) { harmony.UnpatchAll(HarmonyId); harmony = null; }
                    ContentToolMain.Say(log.ToString());
                    AsyncGate.Pending--;
                    Destroy(gameObject);
                }
            }

            /// <summary>
            /// Every event ID seen, resolved to the MEDIA ID that event plays - which is the number
            /// `ct_sound bake` takes and the raw event ID is not. Without this the instrument answers
            /// "which sound did that button make" with an integer no other command accepts.
            /// An event no shipped bank .txt names is printed as such, never dropped.
            /// </summary>
            private static string Named()
            {
                if (Counts.Count == 0)
                    return "ct_voices: nothing was posted, so there is nothing to name. Arm the watch, " +
                           "then do the thing you want to hear.";
                List<uint> ids = new List<uint>(Counts.Keys);
                ids.Sort();
                StringBuilder b = new StringBuilder();
                b.Append("ct_voices what those events PLAY (event -> media, the id 'ct_sound bake' takes):");
                foreach (uint id in ids)
                {
                    string what = Bake.SoundReplace.MediaOfEvent(id);
                    b.Append("\n  event ").Append(id).Append(" x").Append(Counts[id]).Append("  ")
                     .Append(what ?? "no shipped bank .txt names this event (a mod's own event, or a bank that ships no listing)");
                }
                return b.ToString();
            }

            /// <summary>Per-event post counts, sorted, printed even when zero - a silent watch must be
            /// visible as "0 posts", never as an empty line that reads like success.</summary>
            private static string Describe()
            {
                if (Counts.Count == 0) return "NONE";
                List<uint> ids = new List<uint>(Counts.Keys);
                ids.Sort();
                StringBuilder b = new StringBuilder();
                foreach (uint id in ids) b.Append(id).Append('x').Append(Counts[id]).Append(' ');
                return b.ToString().TrimEnd();
            }

            /// <summary>
            /// How many voices Wwise has alive on the objects the game posted on - the number that
            /// separates accumulation (climbing) from re-entry (steady at 1).
            /// </summary>
            private static string Voices()
            {
                if (Emitters.Count == 0) return "no emitter seen yet";
                StringBuilder b = new StringBuilder();
                foreach (KeyValuePair<uint, GameObject> kv in Emitters)
                {
                    if (kv.Value == null) continue;
                    uint count = 32;
                    uint[] ids = new uint[32];
                    AKRESULT r = AkSoundEngine.GetPlayingIDsFromGameObject(kv.Value, ref count, ids);
                    b.Append(kv.Key).Append("->").Append(r == AKRESULT.AK_Success ? count.ToString() : r.ToString()).Append(' ');
                }
                return b.ToString().TrimEnd();
            }
        }
    }
}
