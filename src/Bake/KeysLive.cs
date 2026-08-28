using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine.AddressableAssets;
using UnityEngine.AddressableAssets.ResourceLocators;
using UnityEngine.ResourceManagement.ResourceLocations;
using UnityEngine.ResourceManagement.ResourceProviders;

namespace Morgott.ContentTool.Bake
{
    /// <summary>
    /// Route iii, LIVE: a mod's own bundle serves NEW Addressables keys without a single byte being
    /// written into the Phoenix Point installation (mandate M2). The sibling of
    /// <see cref="BundleLive"/>, which does the same for route vii; between them the two replacement
    /// routes no longer touch StreamingAssets\aa\catalog.json at all.
    ///
    /// WHY A LOCATOR WORKS AT ALL. Addressables parses catalog.json once into a locator at
    /// InitializeAsync (PhoenixGame.cs:737, before InitMods at :757), and from then on the LOCATOR
    /// LIST is the live source. Verified 2026-08-27 against the shipped Unity.Addressables /
    /// Unity.ResourceManager in D:\PP-Instance2\PhoenixPointWin64_Data\Managed (ilspycmd 9.1.0):
    ///   - ResourceLocationMap(string id, int capacity = 0) allocates its Locations dictionary and
    ///     exposes public Add(object key, IResourceLocation) and Locate(key, type, out locations).
    ///   - Addressables.AddResourceLocator / RemoveResourceLocator are public and forward straight to
    ///     AddressablesImpl, whose AddResourceLocator is a plain list append with no init guard.
    ///   - ResourceLocationBase(string name, string id, string providerId, Type t,
    ///     params IResourceLocation[] dependencies) sets PrimaryKey = name, InternalId = id, and Data
    ///     is a settable property.
    ///   - ResourceProviderBase.ProviderId defaults to GetType().FullName, so the shipped provider ids
    ///     ARE the type full names of AssetBundleProvider / BundledAssetProvider.
    /// Dependencies resolve BY OBJECT REFERENCE, not by key lookup, so the bundle location needs no
    /// locator entry of its own and an absolute path outside the install loads fine - it goes through
    /// ResourceManager.TransformInternalId to AssetBundle.LoadFromFile like any other.
    ///
    /// ADD ONLY, AND THAT IS NOT A LIMITATION WE CHOSE. Our locator is appended after the shipped one,
    /// so it can never outrank a key the game already has - see KeyClaims.ShippedKeyRefusal, which is
    /// where that refusal is worded and where it is falsified offline. Replacing existing content is
    /// route vii's job.
    ///
    /// NOTHING PERSISTS. Registration lives exactly as long as the session; disabling the mod removes
    /// the locators and the next launch starts from the game's own untouched catalog.
    /// </summary>
    internal static class KeysLive
    {
        /// <summary>The Data blob every bundle location must carry: without one, GetLoadInfo:247 reads
        /// LoadType.None and refuses the load. Crc 0 because our bundle is a mod's own build, never the
        /// one the shipped catalog checksummed.</summary>
        private static AssetBundleRequestOptions Options(string bundlePath)
        {
            return new AssetBundleRequestOptions
            {
                BundleName = Path.GetFileNameWithoutExtension(bundlePath),
                Crc = 0,
                BundleSize = new FileInfo(bundlePath).Length
            };
        }

        // ------------------------------------------------------------------ install / uninstall

        /// <summary>
        /// Publishes every key a project declares. One line per key; a refusal never stops the others,
        /// because one bad key in a manifest must not silently take the rest of the mod's content with it.
        /// </summary>
        internal static string Install(string modId, IList<CatalogKeys.Pub> pubs)
        {
            if (pubs == null || pubs.Count == 0) return "no keys to publish";
            StringBuilder log = new StringBuilder();
            // One location per bundle FILE, shared by every key served out of it: two location objects
            // for one archive would make Addressables mount it twice and Unity reject the second.
            Dictionary<string, IResourceLocation> bundles =
                new Dictionary<string, IResourceLocation>(StringComparer.OrdinalIgnoreCase);
            List<string> lines = new List<string>();
            foreach (CatalogKeys.Pub p in pubs)
            {
                string line = Register(modId, p, bundles);
                lines.Add(line);
                log.AppendLine(line);
            }
            log.Append(BundleClaims.Outcome(lines, "key(s)", "published LIVE", modId));
            return log.ToString();
        }

        /// <summary>
        /// Publishes ONE key. Everything is validated before the claim is taken, so a refusal leaves the
        /// registry exactly as it found it - a half-claimed key would be a locator nobody ever removes.
        /// </summary>
        internal static string Register(string modId, CatalogKeys.Pub p,
                                        Dictionary<string, IResourceLocation> bundles)
        {
            if (p == null) return "REFUSED: no key record";
            if (string.IsNullOrEmpty(p.BundlePath) || !File.Exists(p.BundlePath))
                return "REFUSED: '" + modId + "' publishes '" + p.Key + "' out of " + p.BundlePath +
                       ", which does not exist - bake the mod's bundle first";

            string shipped = KeyClaims.ShippedKeyRefusal(modId, p.Key, Known(p.Key));
            if (shipped != null) return shipped;

            Type type = TypeNames.Resolve(p.TypeName, "UnityEngine");
            if (type == null)
                return "REFUSED: '" + p.Key + "' declares type '" + (p.TypeName ?? "(none)") +
                       "', which is not a type this game has. Name a UnityEngine class (Texture2D, " +
                       "GameObject, Mesh, Material, AnimationClip) or an assembly-qualified type name.";

            // The shipped bundles this asset's forged external PPtrs need mounted. Resolved BEFORE the
            // claim: a dep we cannot find is a refusal, and a refusal must not leave a record behind.
            List<IResourceLocation> deps = new List<IResourceLocation> { Bundle(p.BundlePath, bundles) };
            if (!string.IsNullOrEmpty(p.Deps))
                foreach (string name in p.Deps.Split(';'))
                {
                    if (name.Length == 0) continue;
                    IResourceLocation dep = Shipped(name);
                    if (dep == null)
                        return "REFUSED: '" + name + "' is not a bundle this install's live catalog " +
                               "knows, so '" + p.Key + "' cannot depend on it";
                    if (!deps.Contains(dep)) deps.Add(dep);
                }

            string refusal;
            KeyClaim evicted;
            KeyClaim claim = KeyClaims.Claim(modId, p.Key, p.BundlePath, p.Asset, p.TypeName,
                                             out refusal, out evicted);
            if (claim == null) return refusal;

            StringBuilder log = new StringBuilder();
            if (evicted != null)
            {
                Retire(evicted);
                if (evicted.Mod != modId)
                    log.AppendLine("mod '" + evicted.Mod + "' lost key '" + p.Key + "' to '" + modId +
                                   "' (one owner per key, lowest mod id keeps it); its locator was removed");
            }

            ResourceLocationBase asset = new ResourceLocationBase(
                p.Key, p.Asset, typeof(BundledAssetProvider).FullName, type, deps.ToArray());
            ResourceLocationMap map = new ResourceLocationMap("ContentTool:" + modId + ":" + p.Key, 1);
            map.Add(p.Key, asset);
            Addressables.AddResourceLocator(map);
            claim.Locator = map;

            log.Append("published '" + p.Key + "' -> " + p.Asset + " in " +
                       Path.GetFileName(p.BundlePath) + " for '" + modId + "' as " + type.Name +
                       (deps.Count > 1 ? " (+" + (deps.Count - 1) + " shipped dependency bundle(s))" : ""));
            return log.ToString();
        }

        /// <summary>Fully undoes one mod's published keys: every locator removed, ownership dropped.</summary>
        internal static string Uninstall(string modId)
        {
            List<KeyClaim> gone = KeyClaims.Drop(modId);
            if (gone.Count == 0) return null;
            StringBuilder log = new StringBuilder();
            foreach (KeyClaim c in gone)
            {
                Retire(c);
                log.AppendLine("un-published '" + c.Key + "' for '" + modId + "'");
            }
            // An object Addressables already handed out is still alive in whatever is holding it. Say
            // so rather than let a checkbox claim more than it did - the same honesty BundleLive owes.
            log.Append(gone.Count + " key(s) un-published. Anything already loaded through them stays " +
                       "until the next restart.");
            return log.ToString();
        }

        internal static bool Holds(string modId) { return KeyClaims.Holds(modId); }

        internal static string Status()
        {
            StringBuilder log = new StringBuilder("live published keys: " + KeyClaims.All.Count);
            foreach (KeyClaim c in KeyClaims.All) log.Append("\n  ").Append(c);
            return log.ToString();
        }

        // ------------------------------------------------------------------ the live catalog

        /// <summary>
        /// Does the game already resolve this key? Locators WE appended are skipped: a re-enable would
        /// otherwise read its own previous registration as "the game already has it" and refuse itself.
        /// </summary>
        private static bool Known(object key)
        {
            foreach (IResourceLocator locator in Addressables.ResourceLocators)
            {
                if (locator == null || KeyClaims.Owns(locator)) continue;
                IList<IResourceLocation> found;
                if (locator.Locate(key, null, out found) && found != null && found.Count > 0) return true;
            }
            return false;
        }

        /// <summary>Our own bundle's location, made once per bundle file per install.</summary>
        private static IResourceLocation Bundle(string bundlePath, Dictionary<string, IResourceLocation> made)
        {
            IResourceLocation have;
            if (made != null && made.TryGetValue(bundlePath, out have)) return have;

            ResourceLocationBase loc = new ResourceLocationBase(
                bundlePath, bundlePath.Replace('\\', '/'),
                typeof(AssetBundleProvider).FullName, typeof(IAssetBundleResource));
            loc.Data = Options(bundlePath);
            if (made != null) made[bundlePath] = loc;
            return loc;
        }

        /// <summary>
        /// A SHIPPED bundle's live location, by file name - what a "deps" entry names. First match
        /// wins: every location for one shipped bundle carries the same archive, unlike route vii's
        /// redirection, where two locations for one file is genuinely ambiguous.
        ///
        /// ponytail: walks every key of every locator, i.e. the whole catalog, once per declared
        /// dependency per enable. Index by bundle name if a manifest ever declares enough of them for
        /// it to be measurable.
        /// </summary>
        private static IResourceLocation Shipped(string bundleFile)
        {
            foreach (IResourceLocator locator in Addressables.ResourceLocators)
            {
                if (locator == null || locator.Keys == null || KeyClaims.Owns(locator)) continue;
                foreach (object key in locator.Keys)
                {
                    IList<IResourceLocation> found;
                    if (!locator.Locate(key, null, out found) || found == null) continue;
                    foreach (IResourceLocation l in found)
                    {
                        IResourceLocation hit = Match(l, bundleFile);
                        if (hit != null) return hit;
                        if (l.Dependencies == null) continue;
                        foreach (IResourceLocation d in l.Dependencies)
                        {
                            hit = Match(d, bundleFile);
                            if (hit != null) return hit;
                        }
                    }
                }
            }
            return null;
        }

        private static IResourceLocation Match(IResourceLocation l, string bundleFile)
        {
            if (l == null || !(l.Data is AssetBundleRequestOptions)) return null;
            return BundleClaims.Matches(l.InternalId, bundleFile) ? l : null;
        }

        private static void Retire(KeyClaim c)
        {
            IResourceLocator locator = c.Locator as IResourceLocator;
            if (locator != null) Addressables.RemoveResourceLocator(locator);
            c.Locator = null;
        }
    }
}
