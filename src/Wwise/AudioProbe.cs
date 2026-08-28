using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using UnityEngine;

namespace Morgott.ContentTool.Wwise
{
    /// <summary>
    /// The objective measurement rig for every audio check, ported from the donor's SpikeInstrument.
    /// METHODOLOGY: audio is never judged by ear, and a non-zero playingID proves nothing - the
    /// readable facts are the AK_Duration callback's fDuration / mediaID / bStreaming plus the
    /// AK_EndOfEvent timing.
    ///
    /// The reason this pumps instead of sleeping: Wwise hands callbacks to
    /// AkCallbackManager.PostCallbacks(), whose only per-frame caller is
    /// AkSoundEngineController.LateUpdate. A check that blocks the main thread blocks its own
    /// callbacks and reads TIMEOUT every time. We run on the main thread, so LateUpdate cannot be
    /// inside PostCallbacks concurrently and the fields below need no lock.
    /// </summary>
    internal static class AudioProbe
    {
        // AK_EnableGetSourcePlayPosition is not a callback - it is the flag that makes Wwise TRACK the
        // position of this playing ID at all. Without it GetSourcePlayPosition answers AK_Fail, which
        // is how the continuity arm first came out VOID (measured 2026-08-12, build be1403a5).
        private const uint Flags = (uint)(AkCallbackType.AK_EndOfEvent | AkCallbackType.AK_Duration |
                                          AkCallbackType.AK_EnableGetSourcePlayPosition);
        private const int DefaultWaitMs = 3000;

        /// <summary>How long <see cref="Post"/> waits by default - quoted by gates that read a TIMEOUT
        /// as a fact ("still playing after N ms") rather than as a failure.</summary>
        internal static int WaitMs { get { return DefaultWaitMs; } }

        private static uint watched;
        private static bool gotDuration, gotEnd;
        private static float duration, estimated;
        private static uint media;
        private static bool streaming;
        private static long endMs;
        private static readonly Stopwatch since = new Stopwatch();

        /// <summary>Last AK_Duration mediaID, valid only while <see cref="GotDuration"/>.</summary>
        internal static uint Media { get { return media; } }
        /// <summary>Last AK_Duration fDuration in ms, valid only while <see cref="GotDuration"/>.</summary>
        internal static float Duration { get { return duration; } }
        internal static bool Streaming { get { return streaming; } }
        internal static bool GotDuration { get { return gotDuration; } }
        /// <summary>The playing ID of the last <see cref="Post"/> - 0 when it failed. Needed by any
        /// arm that goes on asking the engine about that voice after the post returns.</summary>
        internal static uint PlayingID { get { return watched; } }
        /// <summary>Did AK_EndOfEvent arrive before the wait ran out? A voice the engine killed on a
        /// rejected codec ends in ~23 ms; one still playing when the wait expires reads false.</summary>
        internal static bool GotEnd { get { return gotEnd; } }

        /// <summary>
        /// UnloadBank first so a re-run never reads AK_BankAlreadyLoaded as a failure, then
        /// LoadBankMemoryCopy - never View, whose `addr % uAlignment` is a real division and a
        /// process-killing #DE at 0 (RECIPES 9). Copy means the pin can be freed immediately.
        /// </summary>
        internal static string LoadBank(byte[] bank, uint bankId, out uint loadedId)
        {
            AKRESULT unload = AkSoundEngine.UnloadBank(bankId, IntPtr.Zero);
            GCHandle pin = GCHandle.Alloc(bank, GCHandleType.Pinned);
            try
            {
                AKRESULT r = AkSoundEngine.LoadBankMemoryCopy(pin.AddrOfPinnedObject(), (uint)bank.Length, out loadedId);
                return "UnloadBank: " + unload + " | LoadBankMemoryCopy: " + r + " bankId=" + loadedId;
            }
            finally { pin.Free(); }
        }

        /// <summary>Post one event with duration + end-of-event callbacks and wait for it to finish.</summary>
        internal static string Post(GameObject emitter, uint eventId, string label, int waitMs = DefaultWaitMs)
        {
            watched = 0;
            gotDuration = gotEnd = false;
            duration = estimated = 0f;
            media = 0;
            streaming = false;
            endMs = -1;

            since.Restart();
            watched = AkSoundEngine.PostEvent(eventId, emitter, Flags, OnCallback, null);
            if (watched == 0)
                return "POST " + label + ": playingID=0 POST FAILED (the event did not start; no callback " +
                       "can arrive and nothing below was measured)";

            while (!gotEnd && since.ElapsedMilliseconds < waitMs)
            {
                AkSoundEngine.RenderAudio();
                AkCallbackManager.PostCallbacks();
                Thread.Sleep(2);
            }

            return "POST " + label + ": playingID=" + watched
                 + " dur=" + (gotDuration ? Ms(duration) : "NO-DURATION-CB")
                 + " estDur=" + (gotDuration ? Ms(estimated) : "NO-DURATION-CB")
                 + " mediaID=" + (gotDuration ? media.ToString() : "?")
                 + " streaming=" + (gotDuration ? (streaming ? "true(FILE)" : "false(MEMORY)") : "?")
                 + " endOfEvent=" + (gotEnd ? endMs + "ms" : "TIMEOUT");
        }

        /// <summary>
        /// Runs inside PostCallbacks, on our own thread. The info objects are SHARED statics whose
        /// native pointer is re-aimed per callback, so every value is read here and now - keeping
        /// the object would hand back whatever the next callback happens to be.
        /// </summary>
        private static void OnCallback(object cookie, AkCallbackType type, AkCallbackInfo info)
        {
            AkEventCallbackInfo evt = info as AkEventCallbackInfo;
            if (evt == null || (watched != 0 && evt.playingID != watched)) return;

            if (type == AkCallbackType.AK_Duration)
            {
                AkDurationCallbackInfo d = info as AkDurationCallbackInfo;
                if (d == null) return;
                duration = d.fDuration;
                estimated = d.fEstimatedDuration;
                media = d.mediaID;
                streaming = d.bStreaming;
                gotDuration = true;
            }
            else if (type == AkCallbackType.AK_EndOfEvent)
            {
                gotEnd = true;
                endMs = since.ElapsedMilliseconds;
            }
        }

        private static string Ms(float v) { return v.ToString("0") + "ms"; }
    }
}
