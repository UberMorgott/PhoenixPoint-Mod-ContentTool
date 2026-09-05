using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.AddressableAssets.ResourceLocators;
using UnityEngine.ResourceManagement.ResourceLocations;
using UnityEngine.ResourceManagement.ResourceProviders;

namespace Morgott.ContentTool.Bake
{
    /// <summary>
    /// The LIVE Addressables seam: a shipped bundle is served out of our patched private copy without
    /// a single byte being written into the Phoenix Point installation.
    ///
    /// WHY THIS REPLACES ROUTE 7'S DISK EDIT. Route7 rewrites the game's own
    /// StreamingAssets\aa\catalog.json (Route7.cs:14-17 argues it "must be on disk"), which is a
    /// non-sequitur: the catalog is parsed ONCE into a ResourceLocationMap at
    /// Addressables.InitializeAsync (PhoenixGame.cs:737, before InitMods at :757), and from then on
    /// the MAP is the live source, not the file. Three measured facts make the live path work:
    ///   - ResourceManager.InternalIdTransformFunc (Func&lt;IResourceLocation,string&gt;) exists and the
    ///     game sets it nowhere; TransformInternalId returns location.InternalId when it is null.
    ///   - AssetBundleResource.GetLoadInfo calls TransformInternalId at LOAD time and hands the
    ///     result straight to AssetBundle.LoadFromFile - so a delegate installed at mod init still
    ///     rewrites every bundle path the game loads afterwards.
    ///   - the CRC beside it comes from location.Data as AssetBundleRequestOptions, a LIVE object we
    ///     can zero in memory - the exact reason Route7 had to zero it on disk (Route7.cs:777-803).
    /// (All four verified 2026-08-27 against the shipped Unity.ResourceManager 1.18.15 /
    /// Unity.Addressables in PhoenixPointWin64_Data\Managed, decompiled with ilspycmd.)
    ///
    /// PATH AND CRC MOVE TOGETHER. A path-only redirect loads a rebuilt bundle against the SHIPPED
    /// bundle's CRC and fails. Register suppresses that location's CRC in the same call that starts
    /// redirecting it, and Unregister restores the shipped value in the same call that stops.
    ///
    /// NOTHING IS UNLOADED TO MAKE ROOM. If the shipped bundle is already resident, Unity refuses a
    /// second bundle of the same identity, so the redirection is REFUSED BY NAME with
    /// "restart required". BundleResidency.Release is deliberately NOT used as a workaround: its
    /// Unload(false) leaves every object already loaded from the archive alive
    /// (BundleResidency.cs:24-28), which is right for re-baking OUR bundle and wrong for swapping the
    /// game's out from under live scene objects.
    /// </summary>
    internal static class BundleLive
    {
        /// <summary>The delegate that was already installed when we arrived. Composed with, never lost.</summary>
        private static Func<object, string> _previous;
        private static bool _installed;

        // ------------------------------------------------------------------ install / uninstall

        /// <summary>
        /// Redirects every one of a project's patched copies. Same input shape as Route7.Register -
        /// shipped bundle file name -&gt; absolute path of our copy - so the caller does not change.
        /// </summary>
        internal static string Install(string modId, IList<KeyValuePair<string, string>> bundleToCopy)
        {
            IList<Route7.TargetInstall> ignored;
            return Install(modId, bundleToCopy, out ignored);
        }

        /// <param name="targets">what became of each bundle, in install order - the same lines this method
        /// already builds, kept instead of folded into the aggregate. MEASURED, never read off the line:
        /// residency is sampled immediately BEFORE that target's Register (the order Register:80-92 decides
        /// in - it refuses a resident bundle before it looks at claims), and the claim immediately after, so
        /// a mod's own standing claim from an earlier apply cannot be credited to this press.</param>
        internal static string Install(string modId, IList<KeyValuePair<string, string>> bundleToCopy,
                                       out IList<Route7.TargetInstall> targets)
        {
            List<Route7.TargetInstall> per = new List<Route7.TargetInstall>();
            targets = per;
            if (bundleToCopy == null || bundleToCopy.Count == 0) return "no patched copies to redirect";
            StringBuilder log = new StringBuilder();
            List<string> lines = new List<string>();
            foreach (KeyValuePair<string, string> c in bundleToCopy)
            {
                bool wasResident = ResidentNow(c.Key);
                string line = Register(modId, c.Key, c.Value);
                BundleClaim mine = BundleClaims.Find(c.Key);
                per.Add(new Route7.TargetInstall(c.Key, line,
                    wasResident ? Route7.ApplyDisposition.Resident
                    : mine != null && string.Equals(mine.Mod, modId, StringComparison.Ordinal)
                      ? Route7.ApplyDisposition.Redirected
                      : Route7.ApplyDisposition.Refused));
                lines.Add(line);
                log.AppendLine(line);
            }
            log.Append(BundleClaims.Outcome(lines, "bundle(s)", "redirected LIVE", modId));
            return log.ToString();
        }

        /// <summary>
        /// Points one shipped bundle at our patched copy for as long as this session lasts. Returns
        /// the line to log; every failure is a "REFUSED: ..." naming what stopped it.
        /// </summary>
        internal static string Register(string modId, string bundleFile, string patchedPath)
        {
            if (string.IsNullOrEmpty(patchedPath) || !File.Exists(patchedPath))
                return "REFUSED: '" + modId + "' has no patched copy at " + patchedPath;

            string why;
            IResourceLocation loc = Locate(bundleFile, out why);
            if (loc == null) return "REFUSED: " + why;

            // The location FIRST, because the residency answer lives on it: only the location's
            // AssetBundleRequestOptions knows the build name Unity loaded the bundle under.
            AssetBundleRequestOptions opts = loc.Data as AssetBundleRequestOptions;

            string who;
            if (Resident(opts == null ? null : opts.BundleName, out who))
                return "REFUSED: restart required: " + bundleFile + " is already loaded (as '" + who +
                       "'). Unity rejects a second bundle of the same identity, and unloading the " +
                       "game's copy would pull it out from under live objects. Restart, then enable '" +
                       modId + "'.";

            string refusal;
            BundleClaim evicted;
            BundleClaim claim = BundleClaims.Claim(modId, bundleFile, patchedPath, out refusal, out evicted);
            if (claim == null) return refusal;

            StringBuilder log = new StringBuilder();
            if (evicted != null)
            {
                Restore(evicted);
                log.AppendLine("mod '" + evicted.Mod + "' lost " + bundleFile + " to '" + modId +
                               "' (one owner per shipped bundle, lowest mod id keeps it); its " +
                               "redirection was undone and its CRC put back");
            }

            claim.Location = loc;
            if (opts != null && opts.Crc != 0)
            {
                claim.Options = opts;
                claim.Crc = opts.Crc;
                claim.CrcSuppressed = true;
                opts.Crc = 0;
            }

            Compose();
            log.Append("redirected " + bundleFile + " -> " + patchedPath + " for '" + modId + "'" +
                       (claim.CrcSuppressed ? ", crc " + claim.Crc + " -> 0 (in memory)" : ", no crc to suppress") +
                       "; this takes effect on the NEXT AssetBundle.LoadFromFile of it (measured) - " +
                       "anything the game has already loaded from the shipped bundle keeps the shipped " +
                       "asset until you restart");
            return log.ToString();
        }

        /// <summary>Fully undoes one mod's redirections: path override gone, CRC back, ownership dropped.</summary>
        internal static string Uninstall(string modId)
        {
            List<BundleClaim> gone = BundleClaims.Drop(modId);
            if (gone.Count == 0) return null;
            StringBuilder log = new StringBuilder();
            foreach (BundleClaim c in gone)
            {
                Restore(c);
                log.AppendLine("stopped redirecting " + c.Bundle + " for '" + modId + "'" +
                               (c.Crc != 0 ? ", crc restored to " + c.Crc : ""));
            }
            // The bundle the game had already loaded through our path is still the one in memory. Say
            // so rather than let a checkbox claim more than it did.
            log.Append(gone.Count + " redirection(s) dropped. Anything already loaded from the patched " +
                       "copy stays until the next restart.");
            return log.ToString();
        }

        internal static bool Holds(string modId) { return BundleClaims.Holds(modId); }

        /// <summary>Is the shipped bundle open RIGHT NOW? The same two steps Register:80-92 takes - the
        /// live location first, because only its AssetBundleRequestOptions knows the BUILD name Unity
        /// loaded it under (a 32-hex hash in this game, :230-238). Asked here rather than re-derived in
        /// Route7: this project has already shipped that comparison wrong once, and one copy of it is one
        /// too many.
        /// ponytail: second catalog walk per apply (Register:80 walks it again for the same bundle); pass
        /// the IResourceLocation out if it ever shows in a profile.</summary>
        internal static bool ResidentNow(string bundleFile)
        {
            string why;
            IResourceLocation loc = Locate(bundleFile, out why);
            AssetBundleRequestOptions opts = loc == null ? null : loc.Data as AssetBundleRequestOptions;
            string who;
            return opts != null && Resident(opts.BundleName, out who);
        }

        internal static string Status()
        {
            StringBuilder log = new StringBuilder("live bundle redirections: " + BundleClaims.All.Count +
                                                  (_installed ? " (transform func installed" +
                                                   (_previous != null ? ", chained to a pre-existing one)" : ")") : ""));
            foreach (BundleClaim c in BundleClaims.All) log.Append("\n  ").Append(c);
            return log.ToString();
        }

        // ------------------------------------------------------------------ the seam itself

        /// <summary>
        /// Installs our delegate ONCE, keeping whatever was there. Never overwrite blindly: another
        /// mod's transform func is the only thing standing between it and its own content.
        /// </summary>
        private static void Compose()
        {
            if (_installed) return;
            Func<IResourceLocation, string> was = Addressables.ResourceManager.InternalIdTransformFunc;
            _previous = was == null ? null : (Func<object, string>)(o => was((IResourceLocation)o));
            Addressables.ResourceManager.InternalIdTransformFunc = Transform;
            _installed = true;
        }

        private static string Transform(IResourceLocation location)
        {
            return BundleClaims.Resolve(location, _previous, location == null ? null : location.InternalId);
        }

        private static void Restore(BundleClaim c)
        {
            AssetBundleRequestOptions opts = c.Options as AssetBundleRequestOptions;
            if (c.CrcSuppressed && opts != null) opts.Crc = c.Crc;
            c.CrcSuppressed = false;
            c.Location = null;
            c.Options = null;
        }

        // ------------------------------------------------------------------ grounding against the live catalog

        /// <summary>
        /// The single live location for one shipped bundle, or null plus the reason. Two matches is a
        /// refusal, not a guess - the same rule Route7.FindInternalId applies to the catalog text.
        ///
        /// ponytail: this walks every key of every locator, which is the whole catalog. It runs once
        /// per bundle per enable, not per load; index by bundle name if a mod ever declares enough
        /// replacements for it to be measurable.
        /// </summary>
        private static IResourceLocation Locate(string bundleFile, out string why)
        {
            why = null;
            List<IResourceLocation> hits = new List<IResourceLocation>();
            foreach (IResourceLocator locator in Addressables.ResourceLocators)
            {
                if (locator == null || locator.Keys == null) continue;
                foreach (object key in locator.Keys)
                {
                    IList<IResourceLocation> found;
                    if (!locator.Locate(key, null, out found) || found == null) continue;
                    foreach (IResourceLocation l in found)
                    {
                        Consider(l, bundleFile, hits);
                        if (l != null && l.Dependencies != null)
                            foreach (IResourceLocation d in l.Dependencies) Consider(d, bundleFile, hits);
                    }
                }
            }
            if (hits.Count == 1) return hits[0];
            why = hits.Count == 0
                ? "no live catalog location loads " + bundleFile +
                  " - this install does not ship it, or Addressables has not initialised yet"
                : hits.Count + " live catalog locations load " + bundleFile + "; refusing to guess which";
            return null;
        }

        private static void Consider(IResourceLocation l, string bundleFile, List<IResourceLocation> hits)
        {
            if (l == null || !(l.Data is AssetBundleRequestOptions)) return;
            if (!BundleClaims.Matches(l.InternalId, bundleFile)) return;
            foreach (IResourceLocation seen in hits) if (ReferenceEquals(seen, l)) return;
            hits.Add(l);
        }

        /// <summary>
        /// Is the shipped bundle already open? Unity's own registry is the only honest answer - see the
        /// note on BundleResidency. The name compared is the LOCATION's
        /// <see cref="AssetBundleRequestOptions.BundleName"/>, never the catalog's file name: Unity
        /// names a loaded bundle after the build (a 32-hex hash in this game), so the file-name
        /// comparison this used to do never matched and the refusal below never fired - measured
        /// in-game 2026-08-27. Nothing to compare against (no options, no name) is answered "not
        /// resident": a made-up name would be the same dead comparison with a different spelling.
        /// </summary>
        private static bool Resident(string bundleName, out string who)
        {
            who = null;
            if (string.IsNullOrEmpty(bundleName)) return false;
            foreach (AssetBundle b in AssetBundle.GetAllLoadedAssetBundles())
            {
                if (b == null || !BundleClaims.SameBundle(b.name, bundleName)) continue;
                who = b.name;
                return true;
            }
            return false;
        }
    }
}
