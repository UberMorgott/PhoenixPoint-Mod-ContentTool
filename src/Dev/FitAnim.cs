using System;
using System.Collections.Generic;
using System.Globalization;
using PhoenixPoint.Common.Entities.Addons;
using PhoenixPoint.Common.Entities.Items;
using PhoenixPoint.Common.Utils;
using PhoenixPoint.Tactical.Entities.Animations;
using PhoenixPoint.Tactical.Entities.Equipments;
using UnityEngine;

namespace Morgott.ContentTool.Dev
{
    /// <summary>
    /// ============ THE ANIMATION TRANSPORT, AND WHY A FIT NEEDS ONE ============
    ///
    /// A grip that reads as perfect in the idle pose comes apart the moment the arm swings: the hand
    /// rotates, the gun does not follow it the way the eye expects, and there is no number anywhere
    /// that says so. So the workbench gets a transport - a bar under the model with the unit's own
    /// clips, play/pause, loop, a scrub slider and a speed - because a frozen frame half way through a
    /// reload is where a bad fit becomes visible.
    ///
    /// ============ WHY IT IS CLIPS AND NOT STATES (2026-08-29) ============
    ///
    /// This used to catalogue the controller's CLIP NAMES and keep only those that answered
    /// <c>Animator.HasState(0, StringToHash(name))</c>. A clip name is not a controller state name and
    /// nothing guarantees it ever was, so on a real soldier NOTHING survived that probe: the list was
    /// empty, so nothing could set the selection, so the whole strip drew itself permanently disabled -
    /// a self-lock with a diagnostic label where the buttons should have been.
    ///
    /// The clips are therefore taken from the DEFS, which is where the game itself gets them:
    /// <c>TacActorAnimActions.ActiveIdleClips</c> and <c>ActiveNavigationClips</c>
    /// (TacActorAnimActions.cs:40-42, re-resolved for the weapon by <c>SetActiveNumberOfHands</c>:66-86)
    /// plus the <c>TacActorShootAnimActionDef</c> that matches the equipment actually in the hand. That
    /// is exactly the rifle idle / run / shoot set for THIS character with THIS weapon, and it needs no
    /// state name at all - the clip is sampled straight onto the rig. Nothing here writes an override,
    /// a clip or any other asset; a game asset is never mutated.
    ///
    /// Leaving the transport is <see cref="CommonCharacterUtils.ResetCharacterAnimation"/>, the game's
    /// own line, plus the animator's speed put back: the default state, with the weapon's own overrides
    /// still in place, which IS the weapon-appropriate idle.
    /// </summary>
    internal static class FitAnim
    {
        // ---- what we are driving ----
        private static AddonsCharacterBuilder builder;
        private static Animator animator;
        private static GameObject rig;
        private static float savedSpeed = 1f;
        private static bool took;
        /// <summary>Whose skeleton the header row's [Skeleton] toggle switches, or null on the FIT tab.
        /// Latched by <see cref="Draw"/> on the Layout pass; see the remark there.</summary>
        private static ModelDoctor doctor;

        // ---- what it can play: one table in two arrays ----
        private static readonly List<string> names = new List<string>();
        private static readonly List<AnimationClip> clips = new List<AnimationClip>();
        /// <summary>
        /// ============ WHOSE CLIP IS ACTUALLY PLAYING ============
        ///
        /// <see cref="clips"/> is what the DEFS name. What the rig plays is what the ANIMATOR's
        /// <c>AnimatorOverrideController</c> maps that clip to, and a content mod's whole animation
        /// story lives in that table (CreatureBuild.RemapController writes it). So each row is resolved
        /// once, at bind: <see cref="sampled"/> is the clip the transport really samples, and
        /// <see cref="source"/> is the one-word marker that says whether it is the shipped clip or one
        /// a content mod substituted. The override table is the authority - nothing here re-derives it
        /// from a manifest, which would only say what the mod MEANT to ship.
        /// </summary>
        private static readonly List<AnimationClip> sampled = new List<AnimationClip>();
        private static readonly List<string> source = new List<string>();
        private static string note;

        /// <summary>The clip the selected row really plays - the override where there is one, the def's
        /// own clip where there is not. Every length, loop flag and sample goes through this.</summary>
        private static AnimationClip Cur
        {
            get { return chosen >= 0 && chosen < sampled.Count ? sampled[chosen] : null; }
        }

        // ---- the transport's own state ----
        /// <summary>Which entry is being driven, or -1 for "nothing bound / the rig is the game's".</summary>
        private static int chosen = -1;
        private static float t;
        private static bool playing;
        private static bool loop = true;
        private static float speed = 1f;
        private static Texture2D backdrop;
        /// <summary>
        /// ============ THE CLIP LIST OPENS UPWARD, AND IT HAS TO ============
        ///
        /// The stepper below is two clicks per clip and says nothing about what is further down the
        /// catalogue, so the list the user asked for is here too - the arrows stay, this is beside them.
        ///
        /// It cannot be drawn INSIDE the strip: the strip has 96 - 2*8 = 80 px of usable height, IMGUI
        /// does not clip a BeginArea, and anything past that height is drawn where nothing can click it
        /// (the same defect the stepper exists because of). So it is its own area ABOVE the strip's top
        /// edge, drawn AFTER the strip's EndArea and last of everything the bench draws, which is what
        /// puts it on top of the scene rather than under it.
        /// </summary>
        private static bool listOpen;
        private static Vector2 listScroll;
        /// <summary>Where the open list was last drawn, in IMGUI coordinates - kept only so
        /// <see cref="OverList"/> can refuse those pixels to the orbit.</summary>
        private static Rect listRect;

        /// <summary>Whether the transport is currently driving the rig itself.</summary>
        internal static bool Driving { get { return chosen >= 0 && animator != null && rig != null; } }

        // ---------------------------------------------------------------- bind / release

        /// <summary>
        /// Point the transport at the rig a rebuild has just finished, with the anim actions and the
        /// live equipment that rebuild produced - called from the SAME callback that re-applies the
        /// weapon's idle, and AFTER it, so the clips catalogued are the ones the weapon selected.
        ///
        /// It PRESERVES the selection: a rebuild is what happens when a gun is nudged and re-equipped,
        /// and being thrown back to frame 0 of nothing every time is what made the strip useless. The
        /// clip is looked up again BY NAME in the new catalogue - a name that is no longer there simply
        /// falls back to the first clip, paused, which is the empty-handed answer too.
        /// </summary>
        /// <param name="modClips">Every clip the content mod that BUILT this character shipped, or null
        /// for a vanilla one. It is the only thing that separates "the game swapped this clip" (which it
        /// does by itself, for the weapon's hand count) from "a mod shipped this clip".</param>
        /// <param name="fallback">The standing variant's own clips (PrototypeHarvest.Clips), used ONLY
        /// when the anim-actions def catalogued nothing - the shipped state of Crabman_AnimActionsDef,
        /// whose AnimActions.Count is 0. Null for an ordinary bench unit, which keeps today's behaviour
        /// byte for byte.</param>
        internal static void Bind(AddonsCharacterBuilder charBuilder, TacActorAnimActions actions,
                                 Equipment held, List<ItemDef> worn, AnimationClip[] modClips,
                                 AnimationClip[] fallback)
        {
            string was = chosen >= 0 && chosen < names.Count ? names[chosen] : null;
            float wasT = t;
            bool wasPlaying = playing;

            // The speed hold belongs to the animator that is going away, not to the new one.
            try { if (took && animator != null) animator.speed = savedSpeed; } catch (Exception) { }
            took = false;
            builder = charBuilder;
            animator = null; rig = null;
            names.Clear(); clips.Clear(); sampled.Clear(); source.Clear();
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
            rig = animator.gameObject;
            savedSpeed = animator.speed;
            Catalogue(actions, held, worn, fallback);
            Resolve(modClips);
            if (clips.Count == 0) return;

            chosen = was == null ? 0 : Math.Max(0, names.IndexOf(was));
            // Frame 0 and PAUSED for a clip we have not seen before: the workbench is a bench, and a
            // model that starts moving on its own is a model that has to be caught before it can be read.
            if (was != null && names.IndexOf(was) >= 0) { t = wasT; playing = wasPlaying; }
        }

        /// <summary>
        /// The clips the game itself would play for THIS character holding THIS weapon: the active idle
        /// set, the shoot set that matches the equipment, and the navigation set (run, climb, turn) -
        /// in that order, because that is the order they are reached for. Nulls are the norm rather than
        /// the exception here: every one of these defs is a fixed field list and most fields are unset.
        ///
        /// THE FALLBACK IS LAST AND ONLY ON AN EMPTY LIST. A def that names clips is always the better
        /// answer - it is the set the game itself would reach for, weapon and all. The rig's own
        /// controller is what is left when the def names nothing, which is the SHIPPED state of
        /// Crabman_AnimActionsDef (AnimActions.Count == 0, slice 0(d)) - and a null <paramref
        /// name="actions"/> is the same case, so it falls through here rather than returning.
        /// </summary>
        private static void Catalogue(TacActorAnimActions actions, Equipment held, List<ItemDef> worn,
                                      AnimationClip[] fallback)
        {
            if (actions == null)
                note = "this rig came back without its TacActorAnimActions, so there is no clip set to " +
                       "read. The idle below is still the weapon's own.";
            else Own(actions, held, worn);

            if (clips.Count > 0 || fallback == null) return;
            foreach (AnimationClip c in fallback) Add(c);
            if (clips.Count > 0)
                note = "clips from the rig's own controller - this variant's anim actions are empty.";
        }

        /// <summary>The def's own three sets, in the order the game reaches for them.</summary>
        private static void Own(TacActorAnimActions actions, Equipment held, List<ItemDef> worn)
        {
            Add(actions.ActiveIdleClips);
            // The shoot set is not held on the component and cannot be asked for through the game's own
            // TryGetAnimAction: that runs Match, and Match hands the context's TacticalActor to
            // BodypartsMatch (TacActorAnimActionEquipmentFilteredDef.cs:70-77), which dereferences it.
            // A bench has no actor - it displays a TEMPLATE - so every bodypart-filtered shoot action
            // would throw and take the whole shoot/reload set with it. The bench knows the bodyparts the
            // character was BUILT with, so it does the search itself with those.
            try { Add(Shoot(actions.TacActorAnimActionsDef, held, worn)); }
            catch (Exception) { }
            Add(actions.ActiveNavigationClips);

            if (clips.Count == 0)
                note = "this unit's anim actions carry no clips for what is in its hands.";
        }

        /// <summary>
        /// One pass over the catalogue, asking the LIVE animator what each def clip really resolves to.
        ///
        /// The rig's controller is an <c>AnimatorOverrideController</c> whenever anything has overridden
        /// anything - the game itself does it for the weapon's hand count, and a content mod does it for
        /// every clip it ships (CreatureBuild.RemapController). Its indexer answers "what plays instead
        /// of this clip", so the resolved clip is what the transport samples and what its length and loop
        /// flag are read off; sampling the def clip while the rig plays another one is the exact lie this
        /// removes.
        ///
        /// The MARKER is a second question and needs a second source: an override alone does not say
        /// WHOSE clip it is, because the game makes overrides of its own. A clip is a mod's when it is
        /// one of the clips that mod shipped - identity against <paramref name="modClips"/>, never a
        /// name match, so two mods can ship a clip called "walk" without either being credited the
        /// other's.
        /// </summary>
        private static void Resolve(AnimationClip[] modClips)
        {
            sampled.Clear(); source.Clear();
            AnimatorOverrideController over = null;
            try { over = animator == null ? null
                       : animator.runtimeAnimatorController as AnimatorOverrideController; }
            catch (Exception) { }

            for (int i = 0; i < clips.Count; i++)
            {
                AnimationClip play = clips[i];
                bool swapped = false;
                try
                {
                    AnimationClip o = over == null ? null : over[clips[i]];
                    // Unity hands back the key itself when nothing overrides it, so identity is the test.
                    if (o != null && o != clips[i]) { play = o; swapped = true; }
                }
                catch (Exception) { }
                sampled.Add(play);
                source.Add(Shipped(play, modClips) ? " [MOD]" : swapped ? " [game*]" : "");
            }
        }

        /// <summary>Is this the content mod's own clip? Identity against the set that mod loaded.</summary>
        private static bool Shipped(AnimationClip clip, AnimationClip[] modClips)
        {
            if (clip == null || modClips == null) return false;
            foreach (AnimationClip c in modClips) if (c == clip) return true;
            return false;
        }

        /// <summary>
        /// The shoot action for what is in the hand, chosen exactly as
        /// <c>TacActorAnimActions.SearchAnimActionInDef</c> (:134-149) chooses it - first match wins,
        /// then down into <c>BaseAnimActions</c> - with two clauses of
        /// <c>TacActorShootAnimActionDef.Match</c> (:93-110) restated against the bench's own inputs:
        /// the bodypart filter is tested against the LIST THE CHARACTER WAS BUILT WITH instead of an
        /// actor that does not exist here, and the equipment filter against the live equipment.
        /// </summary>
        private static TacActorShootAnimActionDef Shoot(TacActorAnimActionsDef def, Equipment held,
                                                        List<ItemDef> worn)
        {
            while (def != null)
            {
                if (def.AnimActions != null)
                    foreach (TacActorAnimActionBaseDef a in def.AnimActions)
                    {
                        TacActorShootAnimActionDef s = a as TacActorShootAnimActionDef;
                        if (s == null) continue;
                        if (s.Bodyparts != null && s.Bodyparts.Length > 0)
                        {
                            bool wears = false;
                            if (worn != null)
                                foreach (ItemDef bp in s.Bodyparts) if (worn.Contains(bp)) { wears = true; break; }
                            if (!wears) continue;
                        }
                        bool filtered = s.EquipmentList != null ||
                                        (s.Equipments != null && s.Equipments.Length > 0);
                        // An unfiltered action is the empty hand's; a filtered one has to name what is held.
                        if (held == null) { if (!filtered) return s; continue; }
                        if (!filtered) continue;
                        if (s.EquipmentList != null ? s.EquipmentList.Contains(held.EquipmentDef)
                                                    : s.Contains(held.EquipmentDef)) return s;
                    }
                def = def.BaseAnimActions;
            }
            return null;
        }

        private static void Add(TacActorAnimActionBaseDef def)
        {
            if (def == null) return;
            AnimationClip[] all;
            try { all = def.GetAllClips(); } catch (Exception) { return; }
            if (all == null) return;
            foreach (AnimationClip c in all) Add(c);
        }

        /// <summary>One row, deduplicated by clip IDENTITY - the same rule the def arm has always used,
        /// so a controller clip a def also names cannot be listed twice.</summary>
        private static void Add(AnimationClip c)
        {
            if (c == null || clips.Contains(c)) return;
            clips.Add(c);
            names.Add(string.IsNullOrEmpty(c.name) ? "(unnamed)" : c.name);
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
            builder = null; animator = null; rig = null; took = false;
            names.Clear(); clips.Clear(); sampled.Clear(); source.Clear();
            note = null;
            listOpen = false; listRect = new Rect(0f, 0f, 0f, 0f);
        }

        /// <summary>Stop driving and put the pose and the speed back. This is RELINQUISHING THE RIG, and
        /// nothing in the ordinary UI reaches it: a transport that can drop to "nothing selected" is a
        /// transport whose buttons can go dead with no way back, which is the defect this file exists to
        /// undo.</summary>
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
        /// One frame of playback. The position is OURS (<see cref="BenchList.Advance"/>) and the clip is
        /// SAMPLED onto the rig rather than played as a state, so pause, scrub, slow motion and loop are
        /// one number and one rule rather than four that interact - and none of it depends on a state
        /// name the controller may not have.
        ///
        /// The price of sampling, paid on purpose: animation EVENTS do not fire, root motion is not
        /// applied and there are no transitions between clips. A weapon fit is judged on where the hand
        /// is in a given frame, and none of those three move a hand.
        ///
        /// It runs in LateUpdate (FitBench.Arm) so the sample lands AFTER the animator has evaluated its
        /// own state for the frame; the speed is still held at 0 so the state underneath cannot drift.
        /// </summary>
        internal static void Tick()
        {
            if (!Driving) return;
            try
            {
                AnimationClip clip = Cur;
                if (clip == null) return;
                if (playing)
                {
                    bool ended;
                    t = BenchList.Advance(t, Time.deltaTime, speed, clip.length, loop, out ended);
                    if (ended) playing = false;
                }
                if (!took) { savedSpeed = animator.speed; took = true; }
                animator.speed = 0f;
                clip.SampleAnimation(rig, BenchList.Seconds(t, clip.length));
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
        /// <param name="owner">The Doctor whose <see cref="ModelDoctor.Skeleton"/> the header row's
        /// [Skeleton] toggle reads and writes, or null on the FIT tab - where the row keeps exactly its
        /// current shape. LATCHED on the Layout pass, like every other thing in this file that decides
        /// how many controls exist: the tab toggle flips mid-pass, and a control that appears on a
        /// Repaint the Layout pass never counted is the group-imbalance error.</param>
        internal static void Draw(float panelWidth, ModelDoctor owner)
        {
            float w = Screen.width, h = Screen.height;
            if (Event.current.type == EventType.Layout) doctor = owner;
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
                // chosen < 0 with a catalogue in hand means playback threw and let the rig go - the note
                // says what, and indexing -1 in here would take the whole panel down with it.
                if (clips.Count == 0 || chosen < 0)
                {
                    // The toggle rides along even here: a prototype whose variant resolved no clips at
                    // all still has a skeleton to show, and a toggle only reachable when something plays
                    // is a toggle the clip-less prototypes never get.
                    GUILayout.BeginHorizontal();
                    Skeleton();
                    GUILayout.Label("animation: " + (note ?? "nothing bound yet."));
                    GUILayout.EndHorizontal();
                }
                else { Clips(); Controls(); }
            }
            finally { GUILayout.EndArea(); }

            // OUTSIDE the strip's area and after it: see the comment on <see cref="listOpen"/>. This is
            // the last thing the bench draws, so nothing is drawn over the open list.
            if (listOpen && clips.Count > 0 && chosen >= 0) List(panelWidth, top);
            else listRect = new Rect(0f, 0f, 0f, 0f);
        }

        /// <summary>The open list, in the SAME shape as the panel's unit and weapon pickers: a scroll
        /// view of one button per entry, the current one marked, and the list folds itself shut the
        /// moment something is picked from it.</summary>
        private static void List(float panelWidth, float stripTop)
        {
            float wide = Mathf.Min(460f, Screen.width - panelWidth - 2f * BenchList.StripInset);
            if (wide < 120f) { listRect = new Rect(0f, 0f, 0f, 0f); return; }
            float wanted = clips.Count * 22f + 12f;
            float high = Mathf.Min(wanted, Mathf.Max(66f, stripTop - 40f));
            listRect = new Rect(panelWidth + BenchList.StripInset, stripTop - high, wide, high);

            GUI.DrawTexture(listRect, backdrop);
            GUILayout.BeginArea(new Rect(listRect.x + 4f, listRect.y + 4f,
                                         listRect.width - 8f, listRect.height - 8f));
            try
            {
                listScroll = GUILayout.BeginScrollView(listScroll);
                // No break when one is picked, deliberately: IMGUI allocates a control id per drawn
                // button and skipping the rest of them mid-pass changes the count between the layout
                // and the repaint, which is an exception every frame. The panel's own pickers do the
                // same thing for the same reason.
                // The marker is IN THE ROW, not only on the selected one: the whole point of opening a
                // character whose animations were replaced is to see at a glance which of them actually
                // came out of the mod's own bundle and which are still the donor's.
                for (int i = 0; i < clips.Count; i++)
                    if (GUILayout.Button((i == chosen ? "> " : "   ") + BenchList.Elide(names[i], 44) +
                                         Mark(i)))
                    { Select(i); listOpen = false; }
                GUILayout.EndScrollView();
            }
            finally { GUILayout.EndArea(); }
        }

        /// <summary>Is this point (in <c>Input.mousePosition</c>'s convention, y from the BOTTOM) on the
        /// open clip list? The band belongs to the list while it is open, exactly as the strip's own
        /// band belongs to the transport - otherwise a click on a clip would also start an orbit.</summary>
        internal static bool OverList(float mouseX, float mouseY)
        {
            if (!listOpen || listRect.width <= 0f) return false;
            if (float.IsNaN(mouseX) || float.IsNaN(mouseY)) return false;
            return listRect.Contains(new Vector2(mouseX, Screen.height - mouseY));
        }

        /// <summary>
        /// The clip STEPPER, not a list of buttons. The strip has 96 - 2*8 = 80 px of usable height and
        /// IMGUI does not clip an area: the old 42 px horizontal scroll view plus a control row already
        /// spent all of it, so anything below was drawn off the rect where nothing could click it. Two
        /// compact rows fit; a third would not.
        /// </summary>
        /// <summary>The row's source marker, never throwing on a catalogue a resolve pass did not
        /// reach: an empty marker is "the shipped clip", which is also the honest answer when there is
        /// no override table to ask.</summary>
        private static string Mark(int i)
        {
            return i >= 0 && i < source.Count ? source[i] : "";
        }

        /// <summary>Section 6's <c>[Skeleton]</c> control, drawn from the ONE place that owns the state -
        /// the Doctor's own field. Nothing at all on the FIT tab, where there is no overlay to toggle.
        /// </summary>
        private static void Skeleton()
        {
            if (doctor == null) return;
            doctor.Skeleton = GUILayout.Toggle(doctor.Skeleton, " Skeleton", GUILayout.Width(86f));
        }

        private static void Clips()
        {
            AnimationClip clip = Cur;
            // Section 6's header row, in its order: [Skeleton] Clip v  < >  Loop.
            GUILayout.BeginHorizontal();
            Skeleton();
            // The same count that used to be a dead label is the handle that opens the whole catalogue,
            // and the arrows STAY beside it - they are one click for "the next one".
            if (GUILayout.Button((listOpen ? "v " : "^ ") + (chosen + 1) + "/" + clips.Count,
                                 GUILayout.Width(72f)))
                listOpen = !listOpen;
            if (GUILayout.Button("<", GUILayout.Width(28f))) Hop(-1);
            if (GUILayout.Button(">", GUILayout.Width(28f))) Hop(1);
            // Loop belongs to this row per section 6; the row below it is the scrubber and the speed.
            loop = GUILayout.Toggle(loop, " loop", GUILayout.Width(60f));
            // The name the DEF asked for, plus who answered it. [MOD] = the clip on screen came out of a
            // content mod's bundle; [game*] = the game itself swapped it (the weapon's hand count does
            // exactly that); nothing = the shipped clip, played as shipped.
            GUILayout.Label(BenchList.Elide(names[chosen], 36) + Mark(chosen), GUILayout.Width(320f));
            // Whether the CLIP itself loops, off the asset - not the loop override below, which is what
            // the transport does when it reaches the end. A one-shot scrubbed round to 0 is the author's
            // choice; a clip that loops by design says so, because a fit is judged on the loop.
            GUILayout.Label(clip != null && clip.isLooping ? "[LOOPS]" : "[one-shot]", GUILayout.Width(78f));
            GUILayout.EndHorizontal();
        }

        private static void Hop(int by)
        {
            Select(((chosen + by) % clips.Count + clips.Count) % clips.Count);
        }

        /// <summary>One selection, whichever control made it - a new clip lands PAUSED on its first
        /// frame, because this is a bench and a model that starts moving on its own has to be caught
        /// before it can be read.</summary>
        private static void Select(int i)
        {
            chosen = i; t = 0f; playing = false;
        }

        /// <summary>Select a clip by NAME - the variant's own PreviewPoseClip, once, on the rebuild that
        /// stood it there. A name that is not in the catalogue is a NO-OP, not a fault: the def's pose
        /// clip need not be one of the clips its anim actions or its controller offer, and Bind's own
        /// "clip 0, paused at frame 0" is the required first state either way.</summary>
        internal static void Select(string clipName)
        {
            if (string.IsNullOrEmpty(clipName)) return;
            int i = names.IndexOf(clipName);          // ordinal, like every other lookup in this file
            if (i >= 0) Select(i);
        }

        private static void Controls()
        {
            float length = Cur == null ? 0f : Cur.length;
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(playing ? "PAUSE" : "PLAY", GUILayout.Width(70f))) playing = !playing;
            if (GUILayout.Button("x" + speed.ToString("0.##", CultureInfo.InvariantCulture),
                                 GUILayout.Width(58f)))
                speed = BenchList.NextSpeed(speed);
            GUILayout.Label(BenchList.Seconds(t, length).ToString("0.00", CultureInfo.InvariantCulture) +
                            " / " + length.ToString("0.00", CultureInfo.InvariantCulture) + "s",
                            GUILayout.Width(90f));
            // The scrub. Dragging it takes IMGUI's hotControl like any other control, and the band it
            // sits in is refused to the orbit and to the gizmo (OrbitCamera.InViewport), so a drag here
            // can never also swing the camera.
            float v = GUILayout.HorizontalSlider(t, 0f, 1f);
            if (Math.Abs(v - t) > 1e-5f) { t = BenchList.Normalized(v); playing = false; }
            GUILayout.EndHorizontal();
        }
    }
}
