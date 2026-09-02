using System;
using System.Collections.Generic;
using PhoenixPoint.Common.Core;
using PhoenixPoint.Common.Entities.Addons;
using PhoenixPoint.Common.Entities.GameTags;
using PhoenixPoint.Common.Entities.Items;
using PhoenixPoint.Common.Utils;
using PhoenixPoint.Common.View.ViewModules;
using PhoenixPoint.Geoscape.View.DataObjects;
using PhoenixPoint.Tactical.Entities;
using PhoenixPoint.Tactical.Entities.Animations;
using UnityEngine;

namespace Morgott.ContentTool.Dev
{
    /// <summary>
    /// ============ ONE TRANSACTION AGAINST THE SQUAD BAY ============
    ///
    /// Showing a prototype means rebuilding the bay's OWN AddonsCharacterBuilder as somebody else, so
    /// the soldier the player was looking at has to come back, exactly, whatever happens in between.
    /// It uses the existing builder and never a second hidden one: a rig outside a loaded level yields
    /// zero renderers (slice 0(b)), so a hidden builder buys nothing and duplicates the coroutine
    /// lifecycle <see cref="FitBench"/> already handles.
    ///
    /// The native sequence this mirrors is UIModuleActorCycle.DisplaySoldier:602-615 -
    /// CommonCharacterUtils.DisplayCharacter (CommonCharacterUtils.cs:25-52), autorefresh off,
    /// GameTags.Clear + AddRange, then RebuildCharacter (:54-64). FitBench.Show already performs it
    /// for a TacCharacterDef; this class adds the SNAPSHOT and the RESTORE around it.
    ///
    /// ============ WHY RAPID SELECT-AND-CLOSE STILL PUTS THE RIGHT SOLDIER BACK ============
    /// The rebuild is a COROUTINE (RebuildCharacter -> StartRebuildCharacter,
    /// AddonsCharacterBuilder.cs:176; the event is raised at :293), so NOTHING here may read renderers
    /// on return - <see cref="Rebuilt"/> is the only place they exist. Three orderings had to be
    /// answered, and each one is answered by a state flag rather than by hope:
    ///
    ///  1. SELECT WHILE A SELECT IS IN FLIGHT. <see cref="Show"/> REFUSES while <see cref="Busy"/>.
    ///     Two overlapping rebuilds leave the bay showing a mix of two prototypes and neither slot
    ///     list is trustworthy, and the engine would silently drop the first one anyway
    ///     (CreateRebuildCharacterCrt stops the running coroutine and starts a new one, :107-110).
    ///  2. CLOSE WHILE A SELECT IS IN FLIGHT. <see cref="Restore"/> INVALIDATES rather than waits -
    ///     it cannot block the main thread the coroutine needs. Clearing <c>busy</c> first makes the
    ///     late <see cref="Rebuilt"/> a no-op (no slots recorded off a rebuild nobody is waiting for),
    ///     and the restore's own DisplaySoldier then replaces the in-flight coroutine by the same
    ///     engine path as above. So the LAST rebuild the builder runs is always the restore.
    ///  3. THE RESTORE'S REBUILD FINISHING AFTER THE BENCH HAS CLOSED. It is the game's own
    ///     UIModuleActorCycle.OnCharacterRebuilded (:435-473, subscribed at :248-249 and never
    ///     unsubscribed while the module lives) that finishes it - un-quiesces the manager, re-poses
    ///     the char root and puts the builder back in front of the camera (SetCharBuilderVisibility,
    ///     :917-927). FitBench.Posed early-returns once <c>open</c> is false, so the bench does not
    ///     fight it. Nothing is left half-applied by the bench closing first.
    ///
    /// DisplaySoldier has a fast path (:636-647): same addons and no rig change means NO rebuild, so
    /// no callback. Restore must therefore never depend on one - it does not, it only issues the call.
    ///
    /// ============ SAMPLING vs REBUILD vs RESTORE - THE THIRD ORDERING ============
    /// <see cref="FitAnim"/> SAMPLES a clip straight onto the rig every LateUpdate while it is driving,
    /// holding the animator at speed 0, so three things now write to the same bones and the order they
    /// do it in is the whole contract:
    ///
    ///  a. REBUILD then SAMPLE. <see cref="Actions"/> is the clip set THIS rebuild produced, and
    ///     FitBench.Posed hands it to FitAnim.Bind in the same callback - so a sample can only ever
    ///     land on the rig the actions belong to. Before this field existed the bench catalogued the
    ///     PREVIOUS unit's actions against the prototype's new rig (Show discarded DisplayCharacter's
    ///     return), which is why a Crabman offered no clips at all.
    ///  b. RESTORE then STOP. <see cref="Restore"/> issues DisplaySoldier and returns; it does NOT
    ///     wait, so it cannot stop the sampler itself. FitBench.Close is what enforces the order, in
    ///     one synchronous block: proto.Dispose() (this restore) and then FitAnim.Release - which puts
    ///     the animator's speed back and runs CommonCharacterUtils.ResetCharacterAnimation
    ///     (= Animator.Play(0, -1, 0), CommonCharacterUtils.cs:66-73). No LateUpdate can run between
    ///     the two, so no sample can land on the restored soldier, and the rebuild the restore started
    ///     is a coroutine that has not touched anything yet either.
    ///  c. STALE ACTIONS AFTER A RESTORE. Restore and Dispose null <see cref="Actions"/> where they
    ///     null <c>slots</c>, so nothing can catalogue a gone prototype's clips against whatever the
    ///     bay puts back.
    ///
    /// PRESENTATION ONLY. Never GeoCharacter.SetItems, never SaveLoadout, never a template edit: the
    /// save's soldier is untouched, and the only thing put back is what is on the platform.
    /// </summary>
    internal sealed class PrototypeBaySession : IDisposable
    {
        private static readonly SkinnedMeshRenderer[] NoSlots = new SkinnedMeshRenderer[0];

        private AddonsCharacterBuilder builder;
        private UIModuleActorCycle cycle;
        private SharedData shared;
        /// <summary>FitBench.StillThere - "the level the bench opened into is still playing". Restoring
        /// into a dead bay is a fistful of references to things about to die.</summary>
        private readonly Func<bool> alive;

        private bool busy, captured, restored;
        private SkinnedMeshRenderer[] slots = NoSlots;

        // ---- the snapshot, taken once, BEFORE the first mutation ----
        private UnitDisplayData capturedUnit;
        private AddonsManagerDef capturedManagerDef;
        private readonly List<ItemDef> capturedAddons = new List<ItemDef>();
        private readonly List<GameTagDef> capturedTags = new List<GameTagDef>();

        internal PrototypeBaySession(AddonsCharacterBuilder builder, UIModuleActorCycle cycle,
                                     SharedData shared, Func<bool> alive = null)
        {
            this.builder = builder;
            this.cycle = cycle;
            this.shared = shared;
            this.alive = alive;
        }

        /// <summary>The bay's own actor-cycle module, matched by the builder it constructs into
        /// (UIModuleActorCycle.ConstructedCharacterTransform is the only public window onto its
        /// private _charBuilder, :188). FindObjectsOfTypeAll rather than FindObjectOfType because the
        /// module lives on a screen that is not necessarily switched on while the bench is up.</summary>
        internal static UIModuleActorCycle FindCycle(AddonsCharacterBuilder builder)
        {
            if (builder == null) return null;
            foreach (UIModuleActorCycle c in Resources.FindObjectsOfTypeAll<UIModuleActorCycle>())
            {
                try { if (c != null && c.ConstructedCharacterTransform == builder.transform) return c; }
                catch (Exception) { }   // a module whose _charBuilder was never set throws, and is not ours
            }
            return null;
        }

        /// <summary>A rebuild is in flight. <see cref="Show"/> refuses while true.</summary>
        internal bool Busy { get { return busy; } }

        /// <summary>True once the bay has been snapshotted; a session that never captured must never
        /// restore, because it would DisplaySoldier a null.</summary>
        internal bool Captured { get { return captured; } }

        /// <summary>The TacActorAnimActions the last prototype DisplayCharacter produced - the clip set
        /// that belongs to the rig now standing there. Null before the first <see cref="Show"/>, on a
        /// Show that failed, and after <see cref="Restore"/>, so a stale set can never be catalogued
        /// against a new rig (class remark, case a).</summary>
        internal TacActorAnimActions Actions { get; private set; }

        /// <summary>Snapshot the bay on first use, then show this variant. Returns null on success or
        /// a one-line reason.</summary>
        internal string Show(TacCharacterDef representative, List<ItemDef> bodyparts, ItemDef weapon)
        {
            if (representative == null) return "no representative character for this variant.";
            if (builder == null) return "the squad bay's character builder is gone.";
            if (busy) return "a prototype rebuild is still in flight - wait for it to finish.";
            if (restored) return "this session has already been put back.";

            Capture();

            AddonsManager manager = null;
            try
            {
                UnitDisplayData data = new UnitDisplayData(representative, shared);
                bool rigChanged;
                // THE RETURN VALUE IS THE PROTOTYPE'S OWN CLIP SET, and discarding it is what made the
                // transport catalogue the PREVIOUS unit's clips against this rig (FitBench.cs:1231 has
                // kept it for the bench's own picks since the strip was written).
                Actions = CommonCharacterUtils.DisplayCharacter(builder, data, out rigChanged);
                // DisplaySoldier:613-615, verbatim: the manager SURVIVES a switch between two
                // characters sharing one rig, and with it the previous character's tags - which are
                // what pick the skin variant every addon resolves to. Re-tag or rebuild the wrong skin.
                manager = builder.AddonsManager;
                if (manager != null)
                {
                    manager.SetAutorefreshOnTagsChanged(false);
                    manager.GameTags.Clear();
                    if (data.GameTags != null) manager.GameTags.AddRange(data.GameTags);
                }
                slots = NoSlots;
                busy = true;
                CommonCharacterUtils.RebuildCharacter(builder, bodyparts ?? new List<ItemDef>(), weapon);
                return null;
            }
            catch (Exception ex)
            {
                // Same shape as FitBench.Show: the quiesce is undone HERE and only here, never in a
                // finally - the successful path returns with the rebuild not yet started, and its
                // callback is what un-quiesces it.
                busy = false;
                Actions = null;
                try { if (manager != null) manager.SetAutorefreshOnTagsChanged(true); }
                catch (Exception) { }
                return "could not build '" + representative.name + "' - " + ex.GetType().Name + ": " + ex.Message;
            }
        }

        /// <summary>Called from FitBench.Posed, i.e. on OnCharacterRebuilded. A rebuild nobody here is
        /// waiting for - the bench's own unit pick, or one this session invalidated by restoring - is
        /// ignored, so <see cref="Slots"/> can only ever hold the prototype's own renderers.</summary>
        internal void Rebuilt()
        {
            if (!busy) return;
            busy = false;
            try { slots = builder == null ? NoSlots : builder.GetComponentsInChildren<SkinnedMeshRenderer>(true); }
            catch (Exception) { slots = NoSlots; }
        }

        /// <summary>The real slot renderers the last prototype rebuild produced. Empty until
        /// <see cref="Rebuilt"/> has run for a <see cref="Show"/> at least once.</summary>
        internal SkinnedMeshRenderer[] Slots() { return slots; }

        /// <summary>Put the captured unit back. Safe to call twice.</summary>
        internal string Restore()
        {
            if (restored) return null;
            restored = true;
            busy = false;            // invalidate an in-flight rebuild; see the class remark, case 2
            slots = NoSlots; Actions = null;
            if (!captured) return null;
            if (builder == null) return "the squad bay's character builder is gone.";
            try
            {
                if (capturedUnit != null && cycle != null)
                {
                    // The whole native restore in one call: it re-displays, re-tags, and rebuilds with
                    // the captured character's own armour and weapon (UIModuleActorCycle.cs:602-655).
                    cycle.DisplaySoldier(capturedUnit, false, true);
                    return null;
                }
                // ponytail: no actor-cycle module reachable (the bench can be opened from a geoscape
                // that never showed the roster screen), so put the BUILDER's own captured state back
                // instead. It restores the rig, the tags and the addons - everything this session
                // changed - just without the module's bookkeeping, which never moved either.
                builder.UseAddonManager(capturedManagerDef, false);
                AddonsManager manager = builder.AddonsManager;
                if (manager != null)
                {
                    manager.SetAutorefreshOnTagsChanged(false);
                    manager.GameTags.Clear();
                    manager.GameTags.AddRange(capturedTags);
                }
                // The captured Addons list already holds the weapon (AddonsCharacterBuilder.cs:88-91),
                // so the weapon argument stays null or it would be added twice.
                CommonCharacterUtils.RebuildCharacter(builder, new List<ItemDef>(capturedAddons), null);
                return null;
            }
            catch (Exception ex)
            {
                // Not restored after all, so a second close is allowed to try again.
                restored = false;
                return "could not put the squad bay's own soldier back - " +
                       ex.GetType().Name + ": " + ex.Message;
            }
        }

        /// <summary>Restore unless the level is already gone, then drop every reference. A restore that
        /// FAILED keeps its references and throws, so FitBench.Close names it in the failure list and a
        /// second close retries exactly it - which is the whole bookkeeping contract of that method.</summary>
        public void Dispose()
        {
            if (alive == null || alive())
            {
                string failure = Restore();
                if (failure != null) throw new Exception(failure);
            }
            builder = null; cycle = null; shared = null;
            capturedUnit = null; capturedManagerDef = null;
            capturedAddons.Clear(); capturedTags.Clear();
            slots = NoSlots; Actions = null; busy = false; restored = true;
        }

        /// <summary>The bay as it was, in this order and before anything is touched: the module's
        /// CurrentUnit (UIModuleActorCycle.cs:174, a UnitDisplayData - CurrentCharacter at :172 is its
        /// GeoCharacter), then the builder's own manager def, addons and tags as the fallback.</summary>
        private void Capture()
        {
            if (captured) return;
            captured = true;
            try { capturedUnit = cycle == null ? null : cycle.CurrentUnit; }
            catch (Exception) { capturedUnit = null; }
            try
            {
                capturedManagerDef = builder.AddonsManagerDef;
                if (builder.Addons != null) capturedAddons.AddRange(builder.Addons);
                AddonsManager manager = builder.AddonsManager;
                if (manager != null) foreach (GameTagDef t in manager.GameTags) capturedTags.Add(t);
            }
            catch (Exception) { }
        }
    }
}
