using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using AssetsTools.NET.Extra;
using Base.Assets;
using Base.Core;
// BOTH namespaces above declare an AssetsManager (Base.Assets.AssetsManager is the game's component;
// AssetsTools.NET.Extra.AssetsManager comes in with BundleBaker), so the bare name is CS0104-ambiguous and
// the file does not compile without this alias. Spell GameAssetsManager in the variable, the generic
// GameUtl.GameComponent call and the typeof - all three.
using GameAssetsManager = Base.Assets.AssetsManager;
using Morgott.ContentTool.Bake;
using PhoenixPoint.Common.Entities.Addons;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.AddressableAssets.ResourceLocators;
using UnityEngine.ResourceManagement.ResourceLocations;
using UnityEngine.ResourceManagement.ResourceProviders;

namespace Morgott.ContentTool.Doctor
{
    /// <summary>
    /// WHICH SHIPPED BUNDLE AND WHICH SHIPPED MESH the slot standing on the bench actually is - the pair a
    /// "replace" row needs, derived from the live addon that built the renderer and then PROVED against the
    /// bundle files on disk.
    ///
    /// Three steps, each refusing rather than guessing:
    ///   1. the addon's own dependency graph (AddonDef.SkinData -> AssetReference), walked by the GAME'S OWN
    ///      reflection pass (AssetsManager.GetAssetReferencesFromObject, AssetsManager.cs:316), keeping the
    ///      reference whose .Asset IS the prefab this addon built its visuals from (Addon.cs:179);
    ///   2. that reference's runtime key through Addressables.ResourceLocators - the walk BundleLive.Locate
    ///      (:199-213) does, keyed on ONE key instead of every key - then its locations' Dependencies, where a
    ///      dependency carrying AssetBundleRequestOptions names a .bundle file;
    ///   3. per candidate present on disk, BundleBaker.WhyNot(Mesh, name) - the very call ProjectBake.Patch
    ///      makes at :1588 - which must answer null for EXACTLY ONE of them.
    /// The stored pair is therefore by construction what Patch matches: the bundle case-blind (:1534), the
    /// asset ordinal through the same WhyNot.
    /// </summary>
    internal static class ShippedTarget
    {
        /// <summary>Null on success, having filled <paramref name="target"/>'s ShippedBundle/ShippedAsset; the
        /// refusal sentence otherwise, which is also stored on the target. NEVER throws: it runs once per slot
        /// inside the bay rebuild, and one unresolvable slot must not take the rebuild down with it.</summary>
        internal static string Resolve(Addon addon, SkinnedMeshRenderer smr, PrototypeTarget target)
        {
            target.ShippedBundle = null;
            target.ShippedAsset = null;
            target.TargetRefusal = null;
            try
            {
                // R14. Each refusal below names ONE cause, because "none of the bundles holds it" was a lie
                // in every branch that had no bundles to begin with, and an author cannot act on a sentence
                // describing a step that never ran.
                if (addon == null || smr == null || smr.sharedMesh == null)
                    return Refuse(target, "TARGET REFUSED: this slot has no live mesh, so there is no shipped " +
                                          "Mesh name to look for");
                string asset = smr.sharedMesh.name;

                List<string> files;
                string why = BundlesOf(addon, out files);          // R15, R16, R17, R18, R19
                if (why != null) return Refuse(target, why);

                // THE DERIVATION LINE W4 IS PROVED BY. A successful resolve used to log nothing, so "exactly
                // one candidate answered null" was unfalsifiable after the fact: the manifest row and the
                // later patch line prove only that the CHOSEN pair works, never that no second holder
                // existed. Every deduplicated candidate is named here, with what WhyNot said about it -
                // "holds it" for the one that answered null included.
                Debug.Log("[ContentTool] ShippedTarget: '" + asset + "' candidates (" + files.Count + "): " +
                          Spell(files));

                string last = null;
                int present = 0, opened = 0;
                var holders = new List<string>();
                foreach (string file in files)
                {
                    string shipped = BakeSelfCheck.ShippedBundlePath(file);
                    if (!File.Exists(shipped))
                    {
                        Debug.Log("[ContentTool] ShippedTarget:   " + file + ": not shipped by this install");
                        continue;
                    }
                    present++;
                    try
                    {
                        // ponytail: one BundleBaker per candidate per slot - fine for the handful a slot
                        // depends on, O(bundles) if the panel ever resolves every slot eagerly. Cache by
                        // bundle file then.
                        using (BundleBaker baker = new BundleBaker(shipped, "ct.doctor"))
                        {
                            string gone = baker.WhyNot(AssetClassID.Mesh, asset);
                            opened++;                              // the archive answered, whatever it said
                            if (gone == null) holders.Add(file); else last = gone;
                            Debug.Log("[ContentTool] ShippedTarget:   " + file + ": " +
                                      (gone == null ? "HOLDS IT (WhyNot == null)" : gone));
                        }
                    }
                    catch (Exception ex)
                    {
                        last = file + ": " + ex.GetType().Name + " - " + ex.Message;
                        Debug.Log("[ContentTool] ShippedTarget:   " + last);
                    }
                }

                if (holders.Count == 1)
                {
                    target.ShippedBundle = holders[0];
                    target.ShippedAsset = asset;
                    Debug.Log("[ContentTool] ShippedTarget: resolved '" + asset + "' -> " + holders[0] +
                            " (1 of " + present + " present candidate(s) answered WhyNot == null)");
                    return null;
                }
                if (holders.Count > 1)
                    return Refuse(target, R9(asset, holders));     // R9
                if (present == 0)
                    return Refuse(target, "TARGET REFUSED: this install ships none of the bundles this addon " +
                                          "loads (" + Spell(files) + ") - verify the game files, then show " +
                                          "the prototype again");                                     // R20
                if (opened == 0)
                    return Refuse(target, "TARGET REFUSED: every bundle this addon loads refused to open (" +
                                          Spell(files) + ") - " + last);                              // R21
                return Refuse(target, "TARGET REFUSED: none of the bundles this addon loads holds a Mesh named '" +
                                      asset + "' - " + last);                                         // R10
            }
            catch (Exception ex)
            {
                // R22. The panel gets a sentence, the log gets the stack - the same split
                // ModelDoctor.Tick:391 makes.
                Debug.LogError("[ContentTool] ShippedTarget: " + ex);
                return Refuse(target, "TARGET REFUSED: the addon's dependency graph could not be walked (" +
                                      ex.GetType().Name + ": " + ex.Message + ") - see Player.log for the stack");
            }
        }

        private static string Refuse(PrototypeTarget target, string sentence)
        {
            target.TargetRefusal = sentence;
            return sentence;
        }

        private static string R9(string asset, List<string> holders)
        {
            return "TARGET REFUSED: a Mesh named '" + asset + "' is in " + holders.Count + " of the bundles " +
                   "this addon loads (" + string.Join(", ", holders.ToArray()) + ") - ContentTool will not " +
                   "guess which one the game means";
        }

        /// <summary>Every shipped .bundle FILE NAME the addon's visual prefab is served out of, or the ONE
        /// sentence naming which step could not answer: no graph (R15), no reference (R16), several
        /// references (R17), no locator (R18), or a graph that names no bundle (R19).</summary>
        private static string BundlesOf(Addon addon, out List<string> files)
        {
            files = new List<string>();
            GameObject prefab = addon.VisualsSourcePrefab;
            AddonDef def = addon.AddonDef;
            object skin = def == null ? null : def.SkinData;
            if (prefab == null || skin == null)
                return "TARGET REFUSED: this slot's addon carries no SkinData or was not built from a " +
                       "prefab, so there is no dependency graph to walk";                                 // R15

            var matched = new List<AssetReference>();
            var guids = new List<string>();
            foreach (AssetReference reference in References(skin))
            {
                if (reference == null || !ReferenceEquals(reference.Asset, prefab)) continue;
                matched.Add(reference);
                string guid = reference.AssetGUID ?? "";
                if (!guids.Contains(guid)) guids.Add(guid);
            }
            if (matched.Count == 0)
                return "TARGET REFUSED: this addon's SkinData reaches no AssetReference whose asset is the " +
                       "prefab it built, so ContentTool cannot tell which shipped bundle serves this slot"; // R16
            if (guids.Count > 1)
                return "TARGET REFUSED: this addon's SkinData reaches " + guids.Count + " different " +
                       "AssetReference GUIDs for the prefab it built (" + Spell(guids) + ") - ContentTool " +
                       "will not guess which one the game means";                                         // R17

            object key = matched[0].RuntimeKey;
            var visited = new List<IResourceLocation>();
            bool located = false;
            foreach (IResourceLocator locator in Addressables.ResourceLocators)
            {
                if (locator == null) continue;
                IList<IResourceLocation> found;
                if (!locator.Locate(key, null, out found) || found == null || found.Count == 0) continue;
                located = true;
                foreach (IResourceLocation location in found) Walk(location, files, visited);
            }
            if (!located)
                return "TARGET REFUSED: no live Addressables locator answers this addon's prefab key '" +
                       key + "' - either the catalog has not initialised yet, or this prefab is not served " +
                       "from a bundle at all";                                                            // R18
            if (files.Count == 0)
                return "TARGET REFUSED: the locations behind this addon's prefab name no .bundle at all - " +
                       "nothing in that dependency graph carries AssetBundleRequestOptions";              // R19
            return null;
        }

        /// <summary>A location's own bundle and every bundle it depends on, spelled the way
        /// BundleClaims.Matches:191 compares and BakeSelfCheck.ShippedBundlePath:735 resolves - the FILE name.
        /// VISITED SET, not a depth cap: a real catalog graph is a diamond as often as a tree, so a cap deep
        /// enough for the diamonds is no cycle guard and a cap tight enough to guard is one that silently
        /// truncates. Identity, not Equals - the same rule BundleLive.Consider:226 applies, because an
        /// IResourceLocation implementation is free to define equality however it likes.</summary>
        private static void Walk(IResourceLocation location, List<string> files, List<IResourceLocation> visited)
        {
            if (location == null) return;
            foreach (IResourceLocation seen in visited) if (ReferenceEquals(seen, location)) return;
            visited.Add(location);
            if (location.Data is AssetBundleRequestOptions)
            {
                string file = Path.GetFileName(location.InternalId ?? "");
                if (file.Length != 0 && !Has(files, file)) files.Add(file);
            }
            if (location.Dependencies == null) return;
            foreach (IResourceLocation dependency in location.Dependencies) Walk(dependency, files, visited);
        }

        /// <summary>Case-BLIND, because these are Windows file names and Patch folds them the same way
        /// (ProjectBake.cs:1534). Two locations spelling one file differently would otherwise be opened
        /// twice and counted as two holders, turning a resolvable slot into R9.</summary>
        private static bool Has(List<string> files, string file)
        {
            foreach (string had in files)
                if (string.Equals(had, file, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        private static string Spell(List<string> items) { return string.Join(", ", items.ToArray()); }

        /// <summary>The game's OWN public-field walk, by reflection because it is an internal INSTANCE method
        /// (AssetsManager.cs:316). Using it rather than a copy means this sees exactly what
        /// AcquireDependenciesAsync sees.
        ///
        /// THROWS rather than answering empty when the INFRASTRUCTURE is missing: no AssetsManager component,
        /// no such method, a null result. Folding those into an empty list made the caller print R16 - "this
        /// addon's SkinData reaches no AssetReference whose asset is the prefab it built" - which is a
        /// statement about this addon's DATA and sends the author to inspect a def that is perfectly fine.
        /// They are the tool's own footing giving way, so they belong to the outer catch: R22, with the stack
        /// in Player.log. R16 is reserved for a walk that RAN and matched nothing.
        /// ponytail: copy the :339-381 field walk if a game update ever breaks the lookup.</summary>
        private static IEnumerable<AssetReference> References(object skinData)
        {
            var found = new List<AssetReference>();
            GameAssetsManager manager = GameUtl.GameComponent<GameAssetsManager>();
            if (manager == null)
                throw new InvalidOperationException("no live Base.Assets.AssetsManager component");
            MethodInfo walk = typeof(GameAssetsManager).GetMethod(
                "GetAssetReferencesFromObject", BindingFlags.Instance | BindingFlags.NonPublic,
                null, new[] { typeof(object), typeof(Type[]) }, null);
            if (walk == null)
                throw new MissingMethodException("Base.Assets.AssetsManager",
                                                 "GetAssetReferencesFromObject(object, Type[])");
            IEnumerable produced = walk.Invoke(manager, new object[] { skinData, null }) as IEnumerable;
            if (produced == null)
                throw new InvalidOperationException(
                    "AssetsManager.GetAssetReferencesFromObject returned no enumerable");
            foreach (object item in produced)
            {
                AssetReference reference = item as AssetReference;
                if (reference != null) found.Add(reference);
            }
            return found;
        }
    }
}
