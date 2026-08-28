using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using Base.Core;
using HarmonyLib;
using PhoenixPoint.Modding;

namespace Morgott.ContentTool.Project
{
    /// <summary>
    /// The live half of <see cref="ModGate"/>: what the game's mod manager currently says, plus the one
    /// patch that makes what it says MEAN something for a code-less content mod.
    ///
    /// Why the patch has to exist: PP has no concept of a mod without an assembly, in EITHER loader.
    /// PPModLoader.LoadMod (decompiled PhoenixPoint.Modding\PPModLoader.cs:50-64) looks for
    /// &lt;Directory&gt;\&lt;AssemblyName&gt; and then &lt;Directory&gt;\&lt;FolderName&gt;.dll and returns
    /// null when neither exists, and SteamWorkshopModLoader.LoadMod (SteamWorkshopModLoader.cs:39-47)
    /// does the same for a subscribed item; ModEntry.SetEnabled (ModEntry.cs:198-204) throws on that null, so
    /// TryEnableMod logs "Failed to enable mod" and Enabled stays false FOREVER. A content mod that
    /// ships only media could therefore never be switched on - which is why ContentTool used to ignore
    /// the manager and scan the folder instead. Gating on Enabled without this patch would not fix the
    /// bug, it would ban every code-less content mod.
    ///
    /// The postfix only ever fires where the game had already failed (__result == null) and only for a
    /// folder that carries ContentTool content, so no other mod's load path changes shape.
    /// </summary>
    internal static class ModRoster
    {
        private const string HarmonyId = "morgott.contenttool.modroster";
        private static Harmony harmony;

        /// <summary>Our own mod id, so the keep-alive veto can only ever be about US.</summary>
        private static string selfId;

        /// <summary>
        /// Armed only for the startup reconcile. <see cref="BeforeDisable"/> exists to survive
        /// EnableModsFromStore's second half, which runs synchronously inside the very call that
        /// enabled us - so the veto is needed for that pass and for nothing else. Disarmed one frame
        /// later (ContentToolMain.LoadContent), long before the player can reach the mod manager
        /// screen: from then on his checkbox is the only thing that decides, in both directions.
        /// </summary>
        private static bool startupPass;

        /// <summary>Stop vetoing. The startup pass is over; every disable from here is the player's.</summary>
        internal static void EndStartupPass() { startupPass = false; }

        /// <summary>
        /// Folder -> Enabled, straight off the manager. Null when there is no manager to read, which
        /// <see cref="ModGate"/> turns into a refusal rather than a free pass.
        /// </summary>
        internal static IDictionary<string, bool> Build()
        {
            try
            {
                ModManager m = ModManager.GetInstance();
                if (m == null || !m.CanUseMods) return null;
                Dictionary<string, bool> roster = new Dictionary<string, bool>(StringComparer.Ordinal);
                foreach (ModEntry e in m.Mods) roster[ModGate.Key(e.Directory)] = e.Enabled;
                return roster.Count == 0 ? null : roster;
            }
            catch (Exception) { return null; }
        }

        internal static string Install(string ownId)
        {
            if (harmony != null) return "ct_content: loader gate already installed";
            harmony = new Harmony(HarmonyId);
            selfId = ownId;
            startupPass = true;

            // THE DEPENDENCY KEEP-ALIVE. See ContentMods.KeepAlive for the measured failure; the
            // patch has to sit on TryDisableMod and not on ModEntry.SetEnabled, because the cascade
            // that takes the dependent down runs at the TOP of TryDisableMod (ModManager.cs:233-240),
            // before our SetEnabled is ever reached. We are patched in from inside pass ONE of the
            // same call (TryEnableMod -> OnModEnabled -> here), so pass TWO's invocations see it.
            MethodInfo disable = AccessTools.Method(typeof(ModManager), "TryDisableMod",
                                                    new[] { typeof(ModEntry) });
            if (disable != null)
                harmony.Patch(disable, prefix: new HarmonyMethod(
                    AccessTools.Method(typeof(ModRoster), nameof(BeforeDisable))));
            else
                UnityEngine.Debug.LogError("ct_content: ModManager.TryDisableMod(ModEntry) NOT FOUND " +
                                           "- a player who only subscribed to a content mod gets it " +
                                           "switched back off at startup unless he ticks ContentTool himself");

            // BOTH loaders, because a content mod is installed either into Mods\ or from the Steam
            // Workshop, and each has its own ModLoader subclass with the SAME failure: no assembly
            // file on disk -> null (PPModLoader.cs:50-64, SteamWorkshopModLoader.cs:39-47). The
            // abstract ModLoader.LoadMod cannot be patched and the shared call site
            // (ModEntry.SetEnabled:199) is where the null is thrown on, so the fix is one postfix
            // bound twice. Patching only the local one - which is what shipped - meant a media-only
            // mod could be published to the Workshop and then never switch on for anybody.
            string missing = Join(PatchLoader("PhoenixPoint.Modding.PPModLoader"),
                                  PatchLoader("Base.Platforms.Steam.SteamWorkshopModLoader"));
            if (missing != null) UnityEngine.Debug.LogError("ct_content: " + missing);

            // THE RUNTIME HALF. ModEntry.SetEnabled is the ONE seam both the startup pass and the
            // mod menu's checkbox go through (ModEntry.cs:190-215), for a code-less mod and for one
            // that ships its own DLL alike - which is why the hook is here and not in the ContentMod
            // shim below: that shim never runs for UiSounds or QuitCutscene, which have assemblies of
            // their own. Postfix, so Enabled already holds its final value and we read it rather than
            // predict it. Nothing here may throw into the manager.
            MethodInfo toggle = AccessTools.Method(typeof(ModEntry), "SetEnabled",
                                                   new[] { typeof(bool), typeof(ModSDKContext) });
            if (toggle != null)
                harmony.Patch(toggle,
                    prefix: new HarmonyMethod(AccessTools.Method(typeof(ModRoster), nameof(BeforeSetEnabled))),
                    postfix: new HarmonyMethod(AccessTools.Method(typeof(ModRoster), nameof(AfterSetEnabled))));
            else
                // AccessTools.Method matches parameter types EXACTLY and returns null on a miss, and a
                // null target makes Harmony do nothing at all - which here means every dependent mod
                // silently starts with no content. Loud, not a returned line nobody greps for.
                UnityEngine.Debug.LogError("ct_content: ModEntry.SetEnabled(bool, ModSDKContext) NOT " +
                                           "FOUND - a content mod's keys are published only by the " +
                                           "startup reconcile, i.e. AFTER any dependent mod's own init");

            return Join("ct_content: code-less content mods are loadable, so the mod manager's switch " +
                        "governs them" + (toggle == null
                            ? " - but ModEntry.SetEnabled was not found, so a mid-session toggle needs a restart"
                            : ", live, in both directions"),
                        missing);
        }

        /// <summary>
        /// Binds <see cref="AfterLoadMod"/> to one loader, named rather than referenced: PPModLoader
        /// is internal and SteamWorkshopModLoader lives in another namespace of the same assembly, so
        /// both are reached through the assembly that owns <see cref="ModLoader"/> instead of by
        /// name-guessing an assembly. Returns null on success, else the loud line - a silently
        /// unbound patch would look exactly like a mod the player simply cannot enable.
        ///
        /// AccessTools.Method matches parameter types EXACTLY, and both overrides are declared
        /// (ModSDKContext, ModEntry), so the pair below is the whole signature.
        /// </summary>
        private static string PatchLoader(string typeName)
        {
            Type loader = typeof(ModLoader).Assembly.GetType(typeName);
            MethodInfo target = loader == null
                ? null
                : AccessTools.Method(loader, "LoadMod", new[] { typeof(ModSDKContext), typeof(ModEntry) });
            if (target == null)
                return typeName + ".LoadMod NOT FOUND - code-less content mods installed by that " +
                       "loader cannot be enabled";
            harmony.Patch(target, postfix: new HarmonyMethod(
                AccessTools.Method(typeof(ModRoster), nameof(AfterLoadMod))));
            return null;
        }

        /// <summary>
        /// Harmony PREFIX on ModManager.TryDisableMod: a dependency a still-wanted mod needs is not
        /// switched off behind the player's back.
        ///
        /// Skipping the original is the whole fix - once the body runs, the dependent is already
        /// down. __result is set to TRUE, i.e. "disabled, nothing more to do": EnableModsFromStore
        /// ABORTS its whole reconcile on a false (ModManager.cs:295-298), so answering honestly here
        /// would leave every mod after us in the loop unreconciled - a second bug to fix the first.
        ///
        /// Nothing is written to the player's profile. The condition is re-evaluated every launch
        /// from the list he owns, and the moment he visits the mod manager the game writes us into
        /// that list itself (StoreEnabledMods stores what is ENABLED) - after which
        /// <see cref="ContentMods.KeepAlive"/> is false forever and this method is inert.
        /// </summary>
        private static bool BeforeDisable(ModManager __instance, ModEntry mod, ref bool __result)
        {
            try
            {
                if (!startupPass || mod == null || __instance == null ||
                    !string.Equals(mod.ID, selfId, StringComparison.Ordinal)) return true;

                List<KeyValuePair<string, bool>> dependents = new List<KeyValuePair<string, bool>>();
                foreach (ModEntry e in __instance.Mods)
                {
                    if (e == null || e == mod) continue;
                    foreach (ModEntry dep in e.ReferencedDependencies)
                        if (dep == mod) { dependents.Add(new KeyValuePair<string, bool>(e.ID, e.Enabled)); break; }
                }
                if (!ContentMods.KeepAlive(mod.ID, Activated(), dependents)) return true;

                UnityEngine.Debug.Log("ct_content: '" + mod.ID + "' is not in the player's activated " +
                                      "list, but a mod that IS still needs it - keeping it enabled " +
                                      "so the content he subscribed to does not revert. Ticking " +
                                      "either mod off in the mod manager still turns it off.");
                __result = true;
                return false;
            }
            catch (Exception ex)
            {
                // A throwing prefix would take the manager's whole reconcile with it. Let the game
                // have its own behaviour back instead.
                UnityEngine.Debug.LogError("ct_content keep-alive: " + ex);
                return true;
            }
        }

        /// <summary>
        /// The player's own choice, exactly as the game stores it: the MOD_ACTIVATED array in his
        /// profile's Options.jopt, read through the same NamedValueStore ModManager reads
        /// (ModManager.cs:275, PhoenixGame.cs:851). Null when there is no store to read, which
        /// <see cref="ContentMods.KeepAlive"/> turns into "change nothing".
        /// </summary>
        private static string[] Activated()
        {
            try
            {
                OptionsComponent options = GameUtl.GameComponent<OptionsComponent>();
                return options == null ? null : options.Options.Get<string[]>("MOD_ACTIVATED", null);
            }
            catch (Exception) { return null; }
        }

        /// <summary>
        /// Harmony PREFIX on ModEntry.SetEnabled: the mod's content, in place BEFORE the mod itself
        /// starts. This is the whole point of the prefix, and it is the one thing the postfix below
        /// structurally cannot do.
        ///
        /// SetEnabled's own body loads the mod and calls Instance.Main.OnModEnabled inside itself
        /// (ModEntry.cs:198-220), and TryEnableMod enables a mod's dependencies before the mod
        /// (ModManager.cs:200-207) - so we ARE patched in in time, but a postfix runs after the
        /// dependent has already asked Addressables for the keys we had not published yet. Measured
        /// every launch as three `ct_weapon FAIL key '...' did not load (Failed)` lines from a mod
        /// whose keys `ct_catalog verify` resolved moments later.
        ///
        /// Only the ADDRESSABLES routes are moved forward; sound and video stay in the postfix, where
        /// nothing was ever waiting on them. At prefix time the ModInstance does not exist yet
        /// (SetEnabled:200 has not run), which is fine: <see cref="Bake.Route7.Toggle"/> takes the mod
        /// DIRECTORY, and ModEntry.Directory is set at discovery.
        ///
        /// It cannot double-register: <see cref="Bake.BundleClaims.RouteMoves"/> makes the postfix's
        /// and the startup reconcile's repeat of the same call a no-op. The ownership refusals are
        /// unaffected for the same reason - a mod re-claiming what it already holds never reaches them.
        /// </summary>
        private static void BeforeSetEnabled(ModEntry __instance, bool enable)
        {
            try
            {
                if (__instance == null ||
                    !Bake.BundleClaims.PublishesBeforeInit(enable, __instance.Enabled,
                                                           HasContent(__instance.Directory))) return;
                StringBuilder log = new StringBuilder();
                Reconciled(log, __instance.ID, __instance.Directory, true);
                if (log.Length > 0) UnityEngine.Debug.Log(log.ToString().TrimEnd());
            }
            catch (Exception ex) { UnityEngine.Debug.LogError("ct_content pre-enable: " + ex); }
        }

        /// <summary>
        /// Harmony postfix on ModEntry.SetEnabled: the player's checkbox, applied NOW.
        ///
        /// Before this, a mod switched OFF mid-session kept its content (nothing unloaded it) and a
        /// mod switched ON mid-session got nothing (the one-shot startup scan had long finished).
        /// Both were invisible because the in-game check restarted between toggles - a restart is not
        /// what a checkbox promises.
        ///
        /// The routes are NOT symmetric, and the log says which is which rather than pretending:
        /// a video is handed straight back to the game, a replacement bank cannot be (see
        /// SoundLoad.UnloadMod), and routes vii/iii are a catalog edit on DISK - the game parses it
        /// at startup, so their half of the toggle only shows after a restart (Route7.Toggle says so
        /// in its own words). Before that arm existed, switching a mod off left its replaced mesh in
        /// the game forever, because nothing ever undid the catalog edit.
        /// </summary>
        private static void AfterSetEnabled(ModEntry __instance)
        {
            try
            {
                string dir = __instance == null ? null : __instance.Directory;
                if (string.IsNullOrEmpty(dir) || !HasContent(dir)) return;

                string what;
                if (__instance.Enabled)
                {
                    StringBuilder log = new StringBuilder();
                    int failed = 0;
                    Bake.SoundLoad.LoadMod(dir, log, ref failed);
                    what = Join(Bake.VideoCatalog.LiveMod(dir), log.Length > 0 ? log.ToString().TrimEnd() : null);
                }
                else what = Join(Bake.VideoCatalog.UndoMod(dir), Bake.SoundLoad.UnloadMod(dir));

                // The persistent half. Its own guard makes it a no-op unless the ledger disagrees
                // with the checkbox, so the startup enable pass costs nothing. Caught here rather
                // than left to the outer catch, so a bad ppcontent.json cannot swallow the sound and
                // video lines above.
                try { what = Join(what, Bake.Route7.Toggle(dir, __instance.Enabled)); }
                catch (Exception ex) { what = Join(what, "ct_route7 toggle FAILED: " + ex.Message); }

                if (what != null)
                    UnityEngine.Debug.Log("ct_content: '" + __instance.ID + "' was switched " +
                                          (__instance.Enabled ? "ON" : "OFF") + " in the mod manager" +
                                          Environment.NewLine + what);
            }
            catch (Exception ex) { UnityEngine.Debug.LogError("ct_content toggle: " + ex); }
        }

        /// <summary>
        /// The startup half of the checkbox for the two ADDRESSABLES routes, in BOTH directions.
        ///
        /// <see cref="AfterSetEnabled"/> can only see a mid-session flip, and it misses startup in
        /// both directions:
        ///   OFF - a mod switched off BEFORE launch never reaches the postfix at all:
        ///         EnableModsFromStore (ModManager.cs:293-299) calls TryDisableMod ->
        ///         ModEntry.SetEnabled(false, ctx), whose first statement is
        ///         `if (Enabled == enable) return;` (ModEntry.cs:192-195), and at startup Enabled is
        ///         still its default false. In-game 2026-08-24 that left a disabled mod's content
        ///         applied with no log line to show for it.
        ///   ON  - the redirections (<see cref="Bake.BundleLive"/>) and published keys
        ///         (<see cref="Bake.KeysLive"/>) are session-only IN-MEMORY state, so every launch has
        ///         to install them again. We are patched in from inside the startup enable pass, so
        ///         every mod the manager processed BEFORE ContentTool never fired our postfix: enable a
        ///         content mod and it works, restart and its content is silently gone.
        ///
        /// So the whole roster is reconciled ONCE, after the enable pass has run (ContentToolMain
        /// defers this a frame for exactly that reason). Per mod this is the SAME call the checkbox
        /// makes, and <see cref="Bake.Route7.Toggle"/>'s own per-route guard is a no-op unless a route
        /// is not in the state the checkbox says - so the mods the postfix already handled cost
        /// nothing here and cannot be registered twice.
        ///
        /// The ON pass reuses the discovery the video and sound routes run on
        /// (<see cref="ContentMods.Enabled"/>): the manager's roster, which is also the only thing
        /// that knows a Steam Workshop mod's folder. ContentTool's OWN subprojects are deliberately
        /// not in it - they are the author's dev projects, applied by `ct_route7 apply &lt;project&gt;`,
        /// and auto-applying one would bake a patched copy of a shipped bundle at every launch.
        /// </summary>
        internal static string Reconcile(string modDir)
        {
            ModManager m = ModManager.GetInstance();
            IDictionary<string, bool> roster = Build();
            StringBuilder log = new StringBuilder();

            int skipped;
            foreach (string dir in ContentMods.Enabled(modDir, ContentMods.Manifest, roster, null, out skipped))
                Reconciled(log, new DirectoryInfo(dir).Name, dir, true);

            if (m != null && m.CanUseMods)
                foreach (ModEntry e in m.Mods)
                {
                    if (e == null || e.Enabled) continue;
                    Reconciled(log, e.ID, e.Directory, false);
                }
            return log.Length == 0 ? null : log.ToString().TrimEnd();
        }

        /// <summary>One mod put into the state the mod manager says it should be in, and the line for
        /// it. Silent when the routes were already there, which is the normal case.</summary>
        private static void Reconciled(StringBuilder log, string who, string dir, bool on)
        {
            string what;
            try { what = Bake.Route7.Toggle(dir, on); }
            catch (Exception ex) { what = "ct_route7 reconcile FAILED: " + ex.Message; }
            if (what == null) return;
            // A refusal means nothing moved - the message already names the files and the repair, so
            // it must not get the "was applied"/"was undone" wrapper.
            if (what.StartsWith("REFUSED:"))
                log.AppendLine("ct_content: '" + who + "' " + what);
            else
                log.AppendLine("ct_content: '" + who + "' is " + (on ? "ON" : "OFF") + " in the mod " +
                               "manager, so its live registrations were " + (on ? "installed" : "undone") +
                               " at startup." + Environment.NewLine + what);
        }

        private static string Join(string a, string b)
        {
            if (string.IsNullOrEmpty(a)) return string.IsNullOrEmpty(b) ? null : b;
            return string.IsNullOrEmpty(b) ? a : a + Environment.NewLine + b;
        }

        internal static void Uninstall()
        {
            if (harmony == null) return;
            harmony.UnpatchAll(HarmonyId);
            harmony = null;
        }

        /// <summary>
        /// Harmony postfix on BOTH ModLoader subclasses' LoadMod (local and Steam Workshop): the two
        /// signatures are identical, so one method serves both. The parameter name mirrors the original.
        /// Must not throw into the loader.
        /// </summary>
        private static void AfterLoadMod(ModEntry modEntry, ref ModInstance __result)
        {
            try
            {
                if (__result != null || modEntry == null || !HasContent(modEntry.Directory)) return;
                __result = new ModInstance(modEntry) { CanBeUnloaded = true, Main = new ContentMod() };
            }
            catch (Exception) { }
        }

        /// <summary>
        /// What makes a folder a ContentTool content mod: shipped replacement banks, or a project
        /// manifest. Both are the mod's own declaration - the same "the file's PRESENCE is the
        /// declaration" rule the bake already runs on.
        /// </summary>
        internal static bool HasContent(string dir)
        {
            if (string.IsNullOrEmpty(dir)) return false;
            return Directory.Exists(Path.Combine(dir, Bake.SoundReplace.ShippedBanks))
                   || File.Exists(Path.Combine(dir, "ppcontent.json"));
        }

        /// <summary>
        /// The body a code-less content mod does not have. It exists so ModEntry.SetEnabled has a
        /// ModMain to hang the mod's GameObject and enable/disable calls on; the content itself is
        /// loaded by the gated pass, not from here.
        /// ponytail: empty on purpose - give it behaviour only when a content mod needs per-mod code,
        /// at which point it ships its own DLL and this shim never runs for it.
        /// </summary>
        private sealed class ContentMod : ModMain
        {
        }
    }
}
