using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Morgott.ContentTool.Bake
{
    /// <summary>One mod's live claim on one shipped bundle. Fields, not a record - it is mutated in place.</summary>
    internal sealed class BundleClaim
    {
        /// <summary>Owning mod id, the key the ownership policy sorts on.</summary>
        internal string Mod;
        /// <summary>The SHIPPED bundle's file name, e.g. "mutoid_assets_all.bundle".</summary>
        internal string Bundle;
        /// <summary>Absolute path of OUR patched private copy - what the transform func hands back.</summary>
        internal string Path;
        /// <summary>The catalog's IResourceLocation for that bundle, kept as object (see the class note).</summary>
        internal object Location;
        /// <summary>Its AssetBundleRequestOptions, same reason.</summary>
        internal object Options;
        /// <summary>The CRC the game shipped, so unregister can put it back exactly.</summary>
        internal uint Crc;
        /// <summary>True while <see cref="Crc"/> is suppressed to 0 on the live options object.</summary>
        internal bool CrcSuppressed;
        /// <summary>The served copy is OLDER than <see cref="Path"/>: an install found the shipped bundle
        /// already loaded while this claim stood, so what Unity is serving predates this claim's current
        /// copy and only a restart replaces it. Written per target by <c>BundleLive.Install</c> from the
        /// same <c>wasResident</c> sample <c>Route7.ApplyDisposition.Resident</c> is decided from, so the
        /// console verb and the dashboard's <c>Admission.RestartRequired</c> answer ONE question.</summary>
        internal bool Outdated;

        public override string ToString()
        {
            return Mod + " -> " + Bundle + " = " + Path + (CrcSuppressed ? " (crc " + Crc + " -> 0)" : "");
        }
    }

    /// <summary>One mod's live claim on one PUBLISHED catalog key (route iii). Same shape, same policy.</summary>
    internal sealed class KeyClaim
    {
        /// <summary>Owning mod id, the key the ownership policy sorts on.</summary>
        internal string Mod;
        /// <summary>The address a player's game asks Addressables for, e.g. "morgott.sample/probe_tex".</summary>
        internal string Key;
        /// <summary>Absolute path of the MOD's own bundle the key is served out of.</summary>
        internal string BundlePath;
        /// <summary>The asset name inside that bundle, "assets/&lt;modid&gt;/...".</summary>
        internal string Asset;
        /// <summary>The declared resource type's short name, e.g. "GameObject".</summary>
        internal string TypeName;
        /// <summary>The IResourceLocator we appended for this key, kept as object (see BundleClaims).</summary>
        internal object Locator;

        public override string ToString()
        {
            return Mod + " -> key '" + Key + "' = " + Asset + " in " + BundlePath;
        }
    }

    /// <summary>
    /// Route iii's live registry: WHO publishes which catalog key. Same deterministic policy as
    /// <see cref="BundleClaims"/> (lowest mod id keeps it) and the same Unity-free discipline, so the
    /// whole ownership decision and the ADD/REPOINT decision are falsifiable offline. The
    /// engine-touching half - building the locations, appending the locator - is
    /// <see cref="KeysLive"/>.
    /// </summary>
    internal static class KeyClaims
    {
        private static readonly List<KeyClaim> Held = new List<KeyClaim>();

        internal static IList<KeyClaim> All { get { return Held; } }

        internal static KeyClaim Find(string key)
        {
            foreach (KeyClaim c in Held)
                if (string.Equals(c.Key, key, StringComparison.Ordinal)) return c;
            return null;
        }

        /// <summary>
        /// THE reason route iii is ADD-ONLY, decided here so it can be measured without a game.
        ///
        /// A locator we append is APPENDED: AddressablesImpl.AddResourceLocator is a plain list add,
        /// GetResourceLocations unions every locator, and LoadAssetAsync takes the first
        /// provider-compatible hit - which is the SHIPPED locator, at index 0, every time. So a key the
        /// game already knows cannot be repointed by anything we register, and a "repoint" that
        /// registered anyway would be a locator nobody ever reads: content silently missing, no error.
        /// Replacing shipped content is route vii's job (a patched private copy of the whole bundle,
        /// served through InternalIdTransformFunc), and that is what the refusal says.
        /// </summary>
        internal static string ShippedKeyRefusal(string mod, string key, bool shippedHasKey)
        {
            if (!shippedHasKey) return null;
            return "REFUSED: '" + mod + "' publishes key '" + key + "', which the game's own catalog " +
                   "already has. A locator ContentTool appends is appended AFTER the shipped one, so " +
                   "the shipped asset would keep winning and this key would silently do nothing. " +
                   "Publishing ADDS new keys only. To REPLACE what an existing key already serves, " +
                   "declare it under \"replace\" instead - that route serves a patched private copy of " +
                   "the shipped bundle and needs no catalog key at all.";
        }

        /// <summary>
        /// Records a claim on a key, or refuses it BY NAME. <paramref name="evicted"/> is a claim this
        /// one outranks: already out of the registry, but its locator is still appended to
        /// Addressables, so the caller has to take it down before it forgets about it.
        /// </summary>
        internal static KeyClaim Claim(string mod, string key, string bundlePath, string asset,
                                       string typeName, out string refusal, out KeyClaim evicted)
        {
            refusal = null;
            evicted = null;
            if (string.IsNullOrEmpty(mod) || string.IsNullOrEmpty(key) ||
                string.IsNullOrEmpty(bundlePath) || string.IsNullOrEmpty(asset))
            {
                refusal = "REFUSED: a published key needs a mod id, a key, a bundle and an asset (got '" +
                          mod + "', '" + key + "', '" + bundlePath + "', '" + asset + "')";
                return null;
            }

            KeyClaim standing = Find(key);
            if (standing != null && !string.Equals(standing.Mod, mod, StringComparison.Ordinal))
            {
                if (BundleClaims.Keeps(standing.Mod, mod))
                {
                    refusal = "REFUSED: mod '" + standing.Mod + "' already publishes key '" + key +
                              "' - '" + mod + "' cannot also publish it. One key has exactly one owner " +
                              "and the lower mod id keeps it; one of the two has to go.";
                    return null;
                }
                Held.Remove(standing);
                evicted = standing;
            }
            else if (standing != null)
            {
                // SAME MOD RE-CLAIMING (a re-enable, a second apply): keep the record and move it onto
                // the new bundle. The caller retires the old locator and appends a fresh one, so the
                // record must NOT be duplicated - a second record would leave a locator nobody drops.
                evicted = standing;
                Held.Remove(standing);
            }

            KeyClaim c = new KeyClaim
            { Mod = mod, Key = key, BundlePath = bundlePath, Asset = asset, TypeName = typeName };
            Held.Add(c);
            return c;
        }

        /// <summary>Is that locator one WE appended? What tells "the game already has this key" from
        /// "we published it a moment ago" when the ADD/REPOINT question is asked live.</summary>
        internal static bool Owns(object locator)
        {
            if (locator == null) return false;
            foreach (KeyClaim c in Held) if (ReferenceEquals(c.Locator, locator)) return true;
            return false;
        }

        internal static bool Holds(string mod)
        {
            foreach (KeyClaim c in Held)
                if (string.Equals(c.Mod, mod, StringComparison.Ordinal)) return true;
            return false;
        }

        /// <summary>Removes every claim of one mod and hands them back so their locators come down.</summary>
        internal static List<KeyClaim> Drop(string mod)
        {
            List<KeyClaim> gone = new List<KeyClaim>();
            foreach (KeyClaim c in Held)
                if (string.Equals(c.Mod, mod, StringComparison.Ordinal)) gone.Add(c);
            foreach (KeyClaim c in gone) Held.Remove(c);
            return gone;
        }
    }

    /// <summary>
    /// The bookkeeping half of the live Addressables seam: WHO owns which shipped bundle, and which
    /// path a location resolves to. Deliberately free of UnityEngine / Unity.ResourceManager types -
    /// locations and options travel as <see cref="object"/> - so gate S1 runs offline in
    /// tests\TargetPathTests instead of only inside a game session. The engine-touching half (finding
    /// the location, suppressing its CRC, installing the delegate) is <see cref="BundleLive"/>.
    ///
    /// OWNERSHIP IS BY LOWEST MOD ID, not by arrival. "First claim wins" is not deterministic here:
    /// the order mods are enabled in is the mod manager's, and it changes. Sorting on the id means two
    /// players with the same two mods get the same winner and the same refusal line.
    /// </summary>
    internal static class BundleClaims
    {
        private static readonly List<BundleClaim> Held = new List<BundleClaim>();

        internal static IList<BundleClaim> All { get { return Held; } }

        /// <summary>
        /// Does this catalog internalId name that bundle file? Suffix, on a path boundary, so
        /// "xmutoid_assets_all.bundle" does not answer for "mutoid_assets_all.bundle" - the same
        /// mistake Route7.FindInternalId refuses to make on the catalog text.
        /// </summary>
        internal static bool Matches(string internalId, string bundleFile)
        {
            if (string.IsNullOrEmpty(internalId) || string.IsNullOrEmpty(bundleFile)) return false;
            if (internalId.Length < bundleFile.Length) return false;
            int at = internalId.Length - bundleFile.Length;
            if (string.Compare(internalId, at, bundleFile, 0, bundleFile.Length,
                               StringComparison.OrdinalIgnoreCase) != 0) return false;
            if (at == 0) return true;
            char before = internalId[at - 1];
            return before == '/' || before == '\\';
        }

        /// <summary>
        /// Is a LOADED AssetBundle the one a catalog location loads? Unity names a bundle after the
        /// BUILD, never after the catalog's file: measured in the running game 2026-08-27 a shipped
        /// bundle whose file is "px_equipment_assets_all.bundle" reads
        /// AssetBundleRequestOptions.BundleName = "2b20742ec3da14eed347ece50e87df9d" and
        /// AssetBundle.name = "2b20742ec3da14eed347ece50e87df9d.bundle". So the answer comes from
        /// BundleName, with or without the ".bundle" the loaded name carries - comparing against the
        /// FILE name could never match, which left the residency refusal dead code and let a re-enable
        /// register a redirect Unity then rejected at load time.
        /// </summary>
        internal static bool SameBundle(string loadedName, string bundleName)
        {
            if (string.IsNullOrEmpty(loadedName) || string.IsNullOrEmpty(bundleName)) return false;
            return string.Equals(Path.GetFileNameWithoutExtension(loadedName),
                                 Path.GetFileNameWithoutExtension(bundleName),
                                 StringComparison.OrdinalIgnoreCase);
        }

        internal static BundleClaim Find(string bundleFile)
        {
            foreach (BundleClaim c in Held)
                if (string.Equals(c.Bundle, bundleFile, StringComparison.OrdinalIgnoreCase)) return c;
            return null;
        }

        /// <summary>
        /// Records a claim, or refuses it BY NAME. <paramref name="evicted"/> is a claim this one
        /// outranks: already removed from the registry, but its CRC is still suppressed on a live
        /// options object, so the caller has to hand that back before it forgets about it.
        /// </summary>
        internal static BundleClaim Claim(string mod, string bundleFile, string path,
                                          out string refusal, out BundleClaim evicted)
        {
            refusal = null;
            evicted = null;
            if (string.IsNullOrEmpty(mod) || string.IsNullOrEmpty(bundleFile) || string.IsNullOrEmpty(path))
            {
                refusal = "REFUSED: a claim needs a mod id, a bundle name and a path (got '" +
                          mod + "', '" + bundleFile + "', '" + path + "')";
                return null;
            }

            BundleClaim standing = Find(bundleFile);
            if (standing != null && !string.Equals(standing.Mod, mod, StringComparison.Ordinal))
            {
                if (Keeps(standing.Mod, mod))
                {
                    refusal = "REFUSED: mod '" + standing.Mod + "' already replaces " + bundleFile +
                              " - '" + mod + "' cannot also replace it. One shipped bundle has exactly " +
                              "one owner and the lower mod id keeps it; one of the two has to go.";
                    return null;
                }
                Held.Remove(standing);
                evicted = standing;
            }
            else if (standing != null)
            {
                // SAME MOD RE-CLAIMING (a second 'ct_route7 apply', a re-enable): keep the record, move
                // its path. A fresh record would lose Crc, and the shipped CRC is unrecoverable at that
                // point - the live options object it was read from already reads 0, because THIS claim
                // suppressed it. Uninstall would then restore 0 and the shipped bundle would load
                // unchecked forever.
                standing.Path = path;
                return standing;
            }

            BundleClaim c = new BundleClaim { Mod = mod, Bundle = bundleFile, Path = path };
            Held.Add(c);
            return c;
        }

        /// <summary>
        /// R30's WHOLE rule, in one pure function so both consumers ask it instead of each deciding:
        /// does this mod's declared bundle need a restart before anything can be vouched for?
        ///
        /// RESIDENCY ALONE IS NOT "RESTART REQUIRED", and reading it that way refused a verify that
        /// should pass. A mod enabled BEFORE its target was ever loaded redirects it first, and the
        /// game then loads OUR patched copy through the transform func: the bundle is resident and the
        /// resident copy IS the current one. <see cref="BundleLive.Register"/> refuses to claim over an
        /// ALREADY loaded bundle (BundleLive.cs:109-113), so a standing claim of ours is precisely the
        /// proof that the redirect was in force before the load - and <see cref="BundleClaim.Outdated"/>
        /// carries the one case where it was not: an install that found the bundle already resident,
        /// which is the same sample <c>ApplyDisposition.Resident</c> - and through it the dashboard's
        /// <c>Admission.RestartRequired</c> - is decided from.
        /// </summary>
        internal static bool RestartRequired(string mod, string bundleFile, bool residentNow)
        {
            if (!residentNow) return false;
            BundleClaim c = Find(bundleFile);
            return c == null || !string.Equals(c.Mod, mod, StringComparison.Ordinal) || c.Outdated;
        }

        /// <summary>The patched path for a location we own, if we own it.</summary>
        internal static bool TryPath(object location, out string path)
        {
            path = null;
            if (location == null) return false;
            foreach (BundleClaim c in Held)
                if (ReferenceEquals(c.Location, location)) { path = c.Path; return true; }
            return false;
        }

        /// <summary>
        /// The whole of what the InternalIdTransformFunc does: our path for a location we own, and
        /// otherwise the delegate that was already installed - or the location's own id when there was
        /// none. COMPOSED, never overwriting: a mod that set the func before us keeps working.
        /// </summary>
        internal static string Resolve(object location, Func<object, string> previous, string internalId)
        {
            string path;
            if (TryPath(location, out path)) return path;
            return previous != null ? previous(location) : internalId;
        }

        internal static bool Holds(string mod)
        {
            foreach (BundleClaim c in Held)
                if (string.Equals(c.Mod, mod, StringComparison.Ordinal)) return true;
            return false;
        }

        /// <summary>Removes every claim of one mod and hands them back so their CRCs can be restored.</summary>
        internal static List<BundleClaim> Drop(string mod)
        {
            List<BundleClaim> gone = new List<BundleClaim>();
            foreach (BundleClaim c in Held)
                if (string.Equals(c.Mod, mod, StringComparison.Ordinal)) gone.Add(c);
            foreach (BundleClaim c in gone) Held.Remove(c);
            return gone;
        }

        /// <summary>
        /// Does ONE of a mod's declared routes have to move? A project can declare both a replacement
        /// and a published key, and the two are INDEPENDENT registries: one can be applied while the
        /// other is not (it failed, or it was added to the manifest later). Collapsing them into a
        /// single "is this mod applied" answer meant the checkbox skipped the whole toggle as soon as
        /// EITHER route held it, so the missing one was never repaired and never undone.
        /// </summary>
        internal static bool RouteMoves(bool declared, bool applied, bool on)
        {
            return declared && applied != on;
        }

        /// <summary>One thing that happens while the game turns a mod on or off, in the order it happens.</summary>
        internal enum EnableStep
        {
            /// <summary>ContentTool installs the mod's live redirections and published keys.</summary>
            Publish,
            /// <summary>The GAME runs the mod's own ModMain.OnModEnabled (ModEntry.cs:219).</summary>
            Init,
            /// <summary>The game unloads the mod (ModEntry.cs:231).</summary>
            Deinit,
            /// <summary>ContentTool removes what it registered for the mod.</summary>
            Undo
        }

        /// <summary>
        /// THE ORDERING POLICY, in one pure function so it is falsifiable without Unity: what one
        /// ModEntry.SetEnabled call does, step by step, once ContentTool's two hooks are on it.
        ///
        /// <see cref="EnableStep.Publish"/> HAS TO COME FIRST. ModEntry.SetEnabled loads the mod and
        /// calls its OnModEnabled inside its own body (ModEntry.cs:198-220), so a POSTFIX-only
        /// ContentTool published the mod's keys after the mod had already asked Addressables for
        /// them - measured every launch as `ct_weapon FAIL key '...' did not load (Failed)` while
        /// `ct_catalog verify` resolved the same three keys moments later. Hence the prefix, which is
        /// what the first line below is.
        ///
        /// The trailing Publish/Undo is the postfix, kept because it is the only hook that sees the
        /// FINAL Enabled flag (the engine sets it after the body, and returns early when it is
        /// already what was asked for). It re-runs nothing: <see cref="RouteMoves"/> makes a route
        /// already in the wanted state a no-op.
        /// </summary>
        internal static IList<EnableStep> EnableSteps(bool enable, bool wasEnabled, bool hasContent)
        {
            List<EnableStep> seq = new List<EnableStep>();
            if (PublishesBeforeInit(enable, wasEnabled, hasContent)) seq.Add(EnableStep.Publish);
            if (enable != wasEnabled) seq.Add(enable ? EnableStep.Init : EnableStep.Deinit);
            if (hasContent) seq.Add(enable ? EnableStep.Publish : EnableStep.Undo);
            return seq;
        }

        /// <summary>The prefix's whole decision, shared with <see cref="EnableSteps"/> so the arm that
        /// pins the order is measuring the code that runs and not a copy of it.</summary>
        internal static bool PublishesBeforeInit(bool enable, bool wasEnabled, bool hasContent)
        {
            return enable && !wasEnabled && hasContent;
        }

        /// <summary>
        /// THE ownership policy, one copy for every route that has one: two mods want the same thing
        /// and the LOWER mod id keeps it. Arrival order is the mod manager's and it changes between
        /// launches, so "first claim wins" would give two players with the same two mods different
        /// content and different refusals.
        /// </summary>
        internal static bool Keeps(string standing, string newcomer)
        {
            return string.CompareOrdinal(standing, newcomer) <= 0;
        }

        /// <summary>
        /// The SOUND route's half of that same policy. It has to REFUSE rather than evict: a
        /// replacement bank cannot be unloaded in-session (<see cref="SoundLoad.UnloadMod"/>), so
        /// whoever holds the media keeps it whatever arrives afterwards. Determinism comes from the
        /// load ORDER instead - <see cref="SoundLoad.LoadAll"/> walks the enabled mods lowest id
        /// first, so the winner always reaches a contested media before the loser does and the two
        /// mods produce the same owner on every machine. Null when nobody else holds it.
        /// </summary>
        internal static string MediaRefusal(string owner, string mod, string media, string file)
        {
            if (string.IsNullOrEmpty(owner) || string.Equals(owner, mod, StringComparison.OrdinalIgnoreCase))
                return null;
            return "REFUSED: mod '" + owner + "' already replaces sound media " + media + " - '" + mod +
                   "' ships " + file + " for it and is NOT loaded. One media has exactly one owner and " +
                   "the lower mod id keeps it; a replacement bank cannot be taken back in-session, so " +
                   "the later mod is refused rather than the earlier one evicted. One of the two has to go.";
        }

        /// <summary>
        /// What a route's per-item lines say ACTUALLY happened. Every summary here used to print the
        /// DECLARED count - "PUBLISHED 2 key(s)" after a run where one of the two was refused - so a
        /// manifest error read as a success. Counted off the lines the register calls returned, and
        /// the refusals are named again because the summary is the line people read.
        /// </summary>
        internal static string Outcome(IList<string> lines, string unit, string did, string modId)
        {
            int done = 0;
            StringBuilder refused = new StringBuilder();
            foreach (string line in lines)
            {
                if (line != null && line.StartsWith("REFUSED", StringComparison.Ordinal))
                    refused.Append(refused.Length == 0 ? "" : " | ").Append(line);
                else done++;
            }
            StringBuilder s = new StringBuilder(done + "/" + lines.Count + " " + unit + " " + did +
                " for '" + modId + "' - nothing was written to the game installation");
            if (refused.Length > 0)
                s.Append(Environment.NewLine).Append(lines.Count - done).Append(" of them refused: ")
                 .Append(refused);
            return s.ToString();
        }

        /// <summary>
        /// A mod upgraded from the on-disk implementation of route vii can still carry its edit INSIDE
        /// the game installation, applied by Addressables before any mod runs. The checkbox cannot
        /// switch that off and this mod does not write into the player's installation to undo it, so
        /// the route is REFUSED BY NAME and the player is told the one sanctioned repair. Silence here
        /// would be the worst outcome: a checkbox reading OFF over content that is still applied.
        /// Null when there is no such record, which is every install that never ran the old code.
        /// </summary>
        /// <summary>
        /// A console verb that WROTE the player's game installation and has been deleted (mandate M2).
        /// Typing it must say so and name the live route that replaced it - a silent no-op reads like
        /// a broken command, and a crash reads like a broken mod.
        /// </summary>
        internal static string Removed(string command, string verb, string liveInstead)
        {
            return "REMOVED: '" + command + " " + verb + "' wrote into your Phoenix Point installation " +
                   "and no longer exists - ContentTool never writes there. " + liveInstead +
                   " If an OLDER ContentTool already wrote to your install, the one repair is Steam -> " +
                   "Phoenix Point -> Properties -> Installed Files -> \"Verify integrity of game files\".";
        }

        internal static string LegacyRefusal(string mod, IList<string> bundles, string ledger, string catalog,
                                             string what = "replacement")
        {
            if (bundles == null || bundles.Count == 0) return null;
            StringBuilder s = new StringBuilder("REFUSED: mod '").Append(mod).Append("' still has ")
                .Append(bundles.Count).Append(' ').Append(what).Append("(s) written INTO your game installation by an ")
                .Append("older ContentTool: ");
            for (int i = 0; i < bundles.Count; i++) { if (i > 0) s.Append(", "); s.Append(bundles[i]); }
            s.Append(". They are applied by ").Append(catalog).Append(" before any mod runs, so this ")
             .Append("checkbox cannot turn them off, and ContentTool will not write to your installation ")
             .Append("to undo them. REPAIR: Steam -> Phoenix Point -> Properties -> Installed Files -> ")
             .Append("\"Verify integrity of game files\", which restores ").Append(catalog)
             .Append(". Then delete the leftover ").Append(ledger).Append(" and ").Append(catalog)
             .Append(".ct-backup and start the game again. Until then this mod's ").Append(what).Append(" is STILL ")
             .Append("APPLIED, whatever the checkbox says.");
            return s.ToString();
        }
    }
}
