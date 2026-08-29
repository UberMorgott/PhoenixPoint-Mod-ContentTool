using System;
using System.Collections.Generic;
using System.Globalization;
using PhoenixPoint.Common.Entities.Addons;
using PhoenixPoint.Common.Utils;
using UnityEngine;

namespace Morgott.ContentTool.Dev
{
    /// <summary>
    /// ============ THE ANIMATION TRANSPORT, AND WHY A FIT NEEDS ONE ============
    ///
    /// A grip that reads as perfect in the idle pose comes apart the moment the arm swings: the hand
    /// rotates, the gun does not follow it the way the eye expects, and there is no number anywhere
    /// that says so. So the workbench gets a transport - a bar under the model with the unit's own
    /// clips, play/pause, loop, a scrub slider and a speed - because slow motion through a reload is
    /// where a bad fit becomes visible.
    ///
    /// ============ WHAT IS ACTUALLY PLAYABLE, AND WHY IT IS STATES AND NOT CLIPS ============
    ///
    /// The obvious source is the anim-action defs: <c>TacActorAnimActionsDef.AnimActions[]</c>, each
    /// with a <c>GetAllClips()</c>. Those are CLIPS, and a clip is not something Unity can be told to
    /// play: <c>Animator.Play</c> takes a STATE. There is no runtime API that enumerates a controller's
    /// states - <c>AnimatorController</c> is UnityEditor-only - so the honest answer is to PROBE:
    ///
    ///   candidates = the live controller's clip names, plus (when the live controller is an
    ///                AnimatorOverrideController, which it always is here - see
    ///                AnimatorClipOverrides.CreateAnimatorClipOverrides, Base.Utils) the BASE
    ///                controller's clip names, which are the ORIGINAL keys the states were authored
    ///                against and therefore the names most likely to BE state names,
    ///   keep       = those for which Animator.HasState(0, StringToHash(name)) answers true.
    ///
    /// That set is verified against the live animator rather than assumed, it degrades to a sentence
    /// on screen when it is empty, and it is the only form of the question that can be asked in a
    /// BUILD at all.
    ///
    /// ============ AND WHY PLAYING A STATE CANNOT FIGHT THE WEAPON'S IDLE ============
    ///
    /// <c>TacActorAnimActions.SetActiveNumberOfHands</c> (:66-86) does NOT change which state plays -
    /// it calls <c>AnimatorClipOverrides.ApplyOverrides</c>, i.e. it re-points the CLIPS the existing
    /// states resolve to. So a transport that only ever names STATES rides on top of the weapon's
    /// idle instead of against it: the state we play resolves to whatever clip the current hand count
    /// selected, and it re-resolves by itself the next time a rebuild swaps them. Nothing here writes
    /// an override, a clip or any other asset - a game asset is never mutated.
    ///
    /// Leaving the transport is <see cref="CommonCharacterUtils.ResetCharacterAnimation"/>, the game's
    /// own line (:66-73), plus the animator's speed put back: the default state, with the weapon's own
    /// overrides still in place, which IS the weapon-appropriate idle.
    /// </summary>
    internal static class FitAnim
    {
        // ---- what we are driving ----
        private static AddonsCharacterBuilder builder;
        private static Animator animator;
        private static float savedSpeed = 1f;
        private static bool took;

        // ---- what it can play ----
        private static readonly List<string> names = new List<string>();
        private static readonly List<int> hashes = new List<int>();
        private static readonly List<float> lengths = new List<float>();
        private static string note;

        // ---- the transport's own state ----
        /// <summary>Which entry is being driven, or -1 for "the game's own idle, hands off".</summary>
        private static int chosen = -1;
        private static float t;
        private static bool playing;
        private static bool loop = true;
        private static float speed = 1f;
        private static Vector2 clipScroll;
        private static Texture2D backdrop;

        /// <summary>Whether the transport is currently driving the rig itself.</summary>
        internal static bool Driving { get { return chosen >= 0 && animator != null; } }

        // ---------------------------------------------------------------- bind / release

        /// <summary>
        /// Point the transport at the rig a rebuild has just finished. Called from the SAME rebuild
        /// callback that re-applies the weapon's idle, and it deliberately DROPS back to that idle: a
        /// new unit is a new controller with new states, and holding a stale state hash across a
        /// rebuild is the one way this could pose a soldier with another creature's animation.
        /// </summary>
        internal static void Bind(AddonsCharacterBuilder charBuilder)
        {
            Stop();                       // whatever we were driving belongs to the rig that just went
            builder = charBuilder;
            animator = null;
            took = false;
            names.Clear(); hashes.Clear(); lengths.Clear();
            note = null;
            chosen = -1; t = 0f; playing = false;

            try
            {
                if (builder == null || builder.AddonsManager == null || builder.AddonsManager.RigRoot == null)
                { note = "no rig is standing there yet."; return; }
                animator = builder.AddonsManager.RigRoot.GetComponent<Animator>();
            }
            catch (Exception ex) { note = "rig - " + ex.GetType().Name + ": " + ex.Message; return; }

            if (animator == null) { note = "this unit's rig has no Animator - nothing to play."; return; }
            savedSpeed = animator.speed;
            Catalogue();
        }

        /// <summary>
        /// The probe described in the class comment. Names come from the live controller first, so the
        /// LENGTH we remember is the length of the clip that will actually play under the current
        /// weapon's overrides; the base controller's names are added after, for the states whose
        /// override happens to carry a different name.
        /// </summary>
        private static void Catalogue()
        {
            int seen = 0;
            try
            {
                RuntimeAnimatorController live = animator.runtimeAnimatorController;
                if (live == null) { note = "this unit's Animator has no controller."; return; }

                Dictionary<string, float> candidates = new Dictionary<string, float>();
                seen += Gather(live, candidates);
                AnimatorOverrideController over = live as AnimatorOverrideController;
                if (over != null) seen += Gather(over.runtimeAnimatorController, candidates);

                foreach (KeyValuePair<string, float> c in candidates)
                {
                    int h = Animator.StringToHash(c.Key);
                    if (!animator.HasState(0, h)) continue;
                    names.Add(c.Key); hashes.Add(h); lengths.Add(c.Value);
                }
            }
            catch (Exception ex) { note = "clips - " + ex.GetType().Name + ": " + ex.Message; return; }

            // Sorted by name, and the three lists sorted WITH it - they are one table in three arrays.
            for (int i = 1; i < names.Count; i++)
                for (int j = i; j > 0 && string.CompareOrdinal(names[j - 1], names[j]) > 0; j--)
                    Swap(j - 1, j);

            if (names.Count == 0)
                note = "this unit's controller answers to none of its " + seen + " clip name(s) as a " +
                       "STATE name, so there is nothing this panel can ask it to play. The idle below " +
                       "is still the weapon's own.";
        }

        private static int Gather(RuntimeAnimatorController c, Dictionary<string, float> into)
        {
            if (c == null) return 0;
            AnimationClip[] clips = c.animationClips;
            if (clips == null) return 0;
            foreach (AnimationClip clip in clips)
            {
                if (clip == null || string.IsNullOrEmpty(clip.name)) continue;
                if (!into.ContainsKey(clip.name)) into[clip.name] = clip.length;
            }
            return clips.Length;
        }

        private static void Swap(int a, int b)
        {
            string n = names[a]; names[a] = names[b]; names[b] = n;
            int h = hashes[a]; hashes[a] = hashes[b]; hashes[b] = h;
            float l = lengths[a]; lengths[a] = lengths[b]; lengths[b] = l;
        }

        /// <summary>
        /// Hands off the rig: the animator's speed back to what it was found at, and the game's own
        /// reset to the default state - which, with the weapon's overrides untouched, is the
        /// weapon-appropriate idle. Deliberately NOT swallowing: it is a restoration step and
        /// <c>FitBench.Close</c> names it on the failure list if it does not work.
        /// </summary>
        internal static void Release()
        {
            Stop();
            builder = null; animator = null; took = false;
            names.Clear(); hashes.Clear(); lengths.Clear();
            note = null;
        }

        /// <summary>Stop driving, put the pose and the speed back, keep the catalogue. This is what
        /// the IDLE button presses and what a rebuild does for itself.</summary>
        private static void Stop()
        {
            chosen = -1; playing = false; t = 0f;
            if (animator == null) return;
            if (took) { animator.speed = savedSpeed; took = false; }
            if (builder != null && builder.AddonsManager != null && builder.AddonsManager.RigRoot != null)
                CommonCharacterUtils.ResetCharacterAnimation(builder);
        }

        // ---------------------------------------------------------------- the frame

        /// <summary>
        /// One frame of playback. The position is OURS (<see cref="BenchList.Advance"/>) and the
        /// animator is held at speed 0 while we drive, so pause, scrub, slow motion and loop are one
        /// number and one rule rather than four that interact. <c>Update(0f)</c> is what makes a
        /// SCRUBBED pose appear at all: a stopped animator evaluates nothing until it is ticked.
        /// </summary>
        internal static void Tick()
        {
            if (!Driving) return;
            try
            {
                if (playing)
                {
                    bool ended;
                    t = BenchList.Advance(t, Time.deltaTime, speed, lengths[chosen], loop, out ended);
                    if (ended) playing = false;
                }
                if (!took) { savedSpeed = animator.speed; took = true; }
                animator.speed = 0f;
                animator.Play(hashes[chosen], 0, t);
                animator.Update(0f);
            }
            catch (Exception ex)
            {
                note = "playback - " + ex.GetType().Name + ": " + ex.Message;
                try { Stop(); } catch (Exception) { }
            }
        }

        // ---------------------------------------------------------------- the strip

        /// <summary>
        /// The bar along the BOTTOM of the free region, right of the panel ("можно внизу под
        /// моделькой"). Its height is <see cref="BenchList.StripHeight"/> - the same one number the
        /// framing subtracts, so the unit is measured into the room that is actually left.
        ///
        /// It draws NOTHING when there is no room for it (<see cref="BenchList.StripShown"/>), and then
        /// it costs no height either.
        /// </summary>
        internal static void Draw(float panelWidth)
        {
            float w = Screen.width, h = Screen.height;
            if (!BenchList.StripShown(w, h, panelWidth)) return;

            if (backdrop == null)
            {
                backdrop = new Texture2D(1, 1);
                backdrop.SetPixel(0, 0, new Color(0.04f, 0.05f, 0.07f, 0.90f));
                backdrop.Apply();
            }
            float top = h - BenchList.StripHeight;
            GUI.DrawTexture(new Rect(panelWidth, top, w - panelWidth, BenchList.StripHeight), backdrop);

            GUILayout.BeginArea(new Rect(panelWidth + BenchList.StripInset, top + BenchList.StripInset,
                                         w - panelWidth - 2f * BenchList.StripInset,
                                         BenchList.StripHeight - 2f * BenchList.StripInset));
            try
            {
                if (names.Count == 0) GUILayout.Label("animation: " + (note ?? "nothing bound yet."));
                else Clips();
                Controls();
            }
            finally { GUILayout.EndArea(); }
        }

        private static void Clips()
        {
            GUILayout.BeginHorizontal();
            // The way back, always first and always available: the weapon's own idle, hands off.
            if (GUILayout.Button(chosen < 0 ? "> IDLE" : "IDLE", GUILayout.Width(70f))) Stop();
            clipScroll = GUILayout.BeginScrollView(clipScroll, GUILayout.Height(42f));
            GUILayout.BeginHorizontal();
            for (int i = 0; i < names.Count; i++)
            {
                string label = (i == chosen ? "> " : "") + BenchList.Elide(names[i], 26);
                if (!GUILayout.Button(label, GUILayout.Width(190f))) continue;
                chosen = i; t = 0f; playing = true;
            }
            GUILayout.EndHorizontal();
            GUILayout.EndScrollView();
            GUILayout.EndHorizontal();
        }

        private static void Controls()
        {
            float length = chosen >= 0 ? lengths[chosen] : 0f;
            GUILayout.BeginHorizontal();
            GUI.enabled = chosen >= 0;
            if (GUILayout.Button(playing ? "PAUSE" : "PLAY", GUILayout.Width(70f))) playing = !playing;
            loop = GUILayout.Toggle(loop, " loop", GUILayout.Width(60f));
            if (GUILayout.Button("x" + speed.ToString("0.##", CultureInfo.InvariantCulture),
                                 GUILayout.Width(58f)))
                speed = BenchList.NextSpeed(speed);
            GUILayout.Label(BenchList.Seconds(t, length).ToString("0.00", CultureInfo.InvariantCulture) +
                            " / " + length.ToString("0.00", CultureInfo.InvariantCulture) + "s",
                            GUILayout.Width(90f));
            // The scrub. Dragging it takes IMGUI's hotControl like any other control, and the band it
            // sits in is refused to the orbit and to the gizmo (BenchList.OverStrip), so a drag here
            // can never also swing the camera.
            float v = GUILayout.HorizontalSlider(t, 0f, 1f);
            if (Math.Abs(v - t) > 1e-5f) t = BenchList.Normalized(v);
            GUI.enabled = true;
            GUILayout.EndHorizontal();
        }
    }
}
