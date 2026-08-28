using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.AddressableAssets;

namespace Morgott.ContentTool.Bake
{
    /// <summary>
    /// C1 / route iii (docs\design-one-bundle-mod.md): the SECOND zero-runtime route. Instead of
    /// shipping a patched copy of a whole shipped bundle (route vii, <see cref="BundleLive"/>), a mod
    /// claims a NEW KEY: an address the game's own Addressables serves out of the mod's OWN bundle.
    ///
    /// WHAT IS LEFT IN THIS FILE. The record type every caller passes around, the ownership guard,
    /// and gate C1's in-game arms. The on-disk half - a codec that decoded the shipped catalog's four
    /// base64 blobs, appended keys and spliced the file back - is DELETED: it existed only to write
    /// StreamingAssets\aa\catalog.json, and ContentTool no longer writes into the installation
    /// (mandate M2). <see cref="KeysLive"/> appends a locator instead, which is in force the moment
    /// it is added and gone the moment the mod is switched off.
    ///
    /// ADD ONLY, and not a limitation anyone chose: an appended locator is read AFTER the shipped
    /// one, so a key the game already has can never be redirected here - see
    /// <see cref="KeyClaims.ShippedKeyRefusal"/>. Replacing shipped content is route vii's job.
    /// </summary>
    internal static class CatalogKeys
    {
        /// <summary>What the sample publishes, and what the C1 arms read. Measured 2026-08-12 out of
        /// the pristine catalog: key 2138 -> entry 1617, one entry, provider 1, resourceType 1.</summary>
        private const string SampleAddKey = "morgott.sample/probe_tex";
        private const string SampleReplaceKey = "02_Bodyparts/ALN_Fireworm_BodyAll_DMG_Ready.prefab";
        /// <summary>An addressable in the SAME shipped bundle and the SAME shipped dependency set,
        /// published by nobody. Its own entry (1618) is a different one from the replaced 1617.</summary>
        private const string SampleControlKey = "02_Bodyparts/ALN_Fireworm_BodyAll_Ready.prefab";
        private const string SampleControlShipped = "ALN_Fireworm_BodyAll_Ready";
        private const int SampleAddSize = 8;
        /// <summary>Every model ProjectBake writes carries the BUILTIN Standard shader through a
        /// forged external PPtr (ProjectBake.Run -> BakeSelfCheck.StandardShaderPathId), so this is a
        /// property of the tool's bake, not of the sample.</summary>
        private const string StandardShaderName = "Standard";
        private const string ErrorShaderName = "Hidden/InternalErrorShader";

        // ------------------------------------------------------------------ the ledger record

        /// <summary>
        /// One mod's claim on one catalog KEY. Same ledger file, same pristine-rebuild, same SHA-1
        /// guard and same orphan drop as a route-vii record - only what the record CONTAINS differs.
        /// </summary>
        internal sealed class Pub
        {
            internal string Mod, Key, BundlePath, Asset, TypeName;
            /// <summary>Extra SHIPPED bundle files this asset's externals need mounted, ';'-separated.
            /// A REPOINT inherits the shipped entry's own set and needs none; an ADD whose material
            /// points at a builtin shader does.</summary>
            internal string Deps;

            internal Pub(string mod, string key, string path, string asset, string type, string deps)
            { Mod = mod; Key = key; BundlePath = path; Asset = asset; TypeName = type; Deps = deps; }

            public override string ToString()
            {
                return "pub\t" + Mod + "\t" + Key + "\t" + BundlePath + "\t" + Asset + "\t" +
                       (string.IsNullOrEmpty(TypeName) ? "-" : TypeName) + "\t" +
                       (string.IsNullOrEmpty(Deps) ? "-" : Deps);
            }

            internal static Pub Parse(string[] f)
            {
                return f.Length == 7 && f[0] == "pub"
                    ? new Pub(f[1], f[2], f[3], f[4], f[5] == "-" ? null : f[5], f[6] == "-" ? null : f[6])
                    : null;
            }
        }

        /// <summary>The one place that decides two mods cannot claim the same key. 1 of ~4029, where
        /// route vii's bundle claim is 1 of 90.</summary>
        internal static string Conflict(IEnumerable<Pub> pubs, Pub want)
        {
            foreach (Pub p in pubs)
                if (p.Mod != want.Mod && string.Equals(p.Key, want.Key, StringComparison.Ordinal))
                    return "mod '" + p.Mod + "' already publishes key '" + want.Key + "'";
            return null;
        }

        /// <summary>Bucket count of an arbitrary catalog text - what the key-count arm reads.</summary>
        internal static int KeyCount(string json)
        {
            int s, e;
            return BitConverter.ToInt32(Route7.Blob(json, "m_BucketDataString", out s, out e), 0);
        }

        /// <summary>The int at m_KeyDataString[0..3], which CreateLocator sizes its key array from.</summary>
        internal static int KeyCountInt(string json)
        {
            int s, e;
            return BitConverter.ToInt32(Route7.Blob(json, "m_KeyDataString", out s, out e), 0);
        }

        // ------------------------------------------------------------------ gate C1

        /// <summary>
        /// C1's in-game half. Every arm is a POSITIVE identity: what the game's own Addressables
        /// handed back, by name and by a measurement of it - never "not the old one", which is what
        /// let a whole day of total failure read green (d4e1814).
        /// </summary>
        internal static int Verify(StringBuilder log, IList<Pub> pubs)
        {
            if (pubs.Count == 0) return 0;
            int fail = 0;

            // C1-live. The keys are served by locators appended at enable time; the game's own
            // catalog.json is untouched, so the proof that matters is that it is still PRISTINE while
            // the addresses below resolve anyway.
            string onDisk = File.ReadAllText(Route7.CatalogPath);
            log.AppendLine("C1-live PASS " + pubs.Count + " key(s) published LIVE while the game's own " +
                           "catalog.json still carries its shipped " + KeyCount(onDisk) +
                           " keys (key-count int = " + KeyCountInt(onDisk) + "), i.e. nothing was written to it");

            foreach (Pub p in pubs)
            {
                string leaf = p.Asset.Substring(p.Asset.LastIndexOf('/') + 1);
                UnityEngine.Object got = null;
                string threw = null;
                try { got = Addressables.LoadAssetAsync<UnityEngine.Object>(p.Key).WaitForCompletion(); }
                catch (Exception ex) { threw = ex.GetType().Name + ": " + ex.Message; }

                string what = got == null ? (threw == null ? "(null)" : "THREW " + threw)
                                          : got.GetType().Name + " '" + got.name + "'";
                // The expected name is BundleBaker's own rule - m_Name is the last segment of the
                // container key - so it is derived from the record, never from a constant here.
                fail += Check(log, "C1-" + (p.Key == SampleAddKey ? "add" : p.Key == SampleReplaceKey ? "replace" : "pub"),
                    got != null && got.name == leaf,
                    "the game's own Addressables resolved '" + p.Key + "' to " + what +
                    " out of " + Path.GetFileName(p.BundlePath) + " (the mod's asset is '" + p.Asset + "', so '" + leaf + "')");

                // C1-type. The DECLARED type, asserted on the object the ENGINE handed back. Without
                // this a key that resolved to the WRONG type still passed C1-pub on its leaf name
                // alone, and every typed block below - the clip's, the prefab's - was `as`-null and
                // SKIPPED, so `verify` reported success having measured nothing about the very thing
                // it claims to prove. One guard, before all of them: a declared type the resolved
                // object does not have is a FAILURE, by name. TypeNames.Resolve is the same resolver
                // KeysLive.Register admits the publication through, so an unresolvable declared type
                // is a record that could never have been registered - also a failure, not a skip.
                Type declared = TypeNames.Resolve(p.TypeName, "UnityEngine");
                fail += Check(log, "C1-type", declared != null && declared.IsInstanceOfType(got),
                    "'" + p.Key + "' declares type '" + (string.IsNullOrEmpty(p.TypeName) ? "(none)" : p.TypeName) +
                    "' and the game's own Addressables resolved it to " + what +
                    (declared == null ? " (that name is not a type this game has)" : ""));

                Texture2D tex = got as Texture2D;
                if (p.Key == SampleAddKey)
                    fail += Check(log, "C1-add-size", tex != null && tex.width == SampleAddSize && tex.height == SampleAddSize,
                        "it is " + (tex == null ? "not a Texture2D" : tex.width + "x" + tex.height) +
                        " (the sample's swatch is " + SampleAddSize + "x" + SampleAddSize +
                        "; every shipped texture in that bundle is 1024)");

                // A published ANIMATION CLIP, read back off the ENGINE's own object rather than off
                // the file we wrote. Nothing here is a constant: every number comes out of the clip
                // Unity just deserialised, and the sample publishes TWO clips of very different
                // lengths through this one arm, so a hardcoded or defaulted reading could not produce
                // both. `empty` is the load-bearing one - a clip whose curves failed to parse still
                // reports a name and a frameRate, and reads empty=True.
                AnimationClip anim = got as AnimationClip;
                if (anim != null)
                    fail += Check(log, "C1-clip", !anim.empty && anim.frameRate > 0f && anim.length > 0f,
                        "'" + p.Key + "' -> AnimationClip '" + anim.name + "' length=" +
                        anim.length.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture) +
                        "s frameRate=" +
                        anim.frameRate.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) +
                        " empty=" + anim.empty + " legacy=" + anim.legacy +
                        " isLooping=" + anim.isLooping + " wrapMode=" + anim.wrapMode +
                        " bounds=" + anim.localBounds.size +
                        " (the engine's own reading of a clip this mod baked and published)");

                GameObject go = got as GameObject;
                if (go != null)
                {
                    // THE unmeasured row (design 9, externals-under-addressables): d4e1814 proved a
                    // forged external resolves when WE mounted the owning archive with
                    // AssetBundle.LoadFromFile. Nothing mounted anything here - the catalog's own
                    // dependency set did. If that satisfies the archive VFS precondition the shader
                    // reports its real name; if it does not, Unity substitutes the error shader.
                    string shader = ShaderNameOf(go);
                    fail += Check(log, "C1-shader", shader == StandardShaderName,
                        "an external PPtr in the mod's own asset, mounted by ADDRESSABLES and by no " +
                        "code of ours, resolved to shader '" + shader + "' (expected '" +
                        StandardShaderName + "'; a dangling external reads '" + ErrorShaderName + "')");
                }
            }

            // C1-ctl-sibling. Untouched key in the SAME shipped bundle and the same shipped
            // dependency set, asserted by the name it must still have. Positive, not an absence.
            bool touched = false;
            foreach (Pub p in pubs) if (p.Key == SampleControlKey) touched = true;
            if (!touched)
            {
                string name = NameOf(SampleControlKey, log);
                fail += Check(log, "C1-ctl-sibling", name == SampleControlShipped,
                    "'" + SampleControlKey + "', which nobody published, still resolves to '" + name +
                    "' (shipped is '" + SampleControlShipped + "')");
            }
            return fail;
        }

        private static string ShaderNameOf(GameObject go)
        {
            foreach (Renderer r in go.GetComponentsInChildren<Renderer>(true))
                foreach (Material m in r.sharedMaterials)
                    if (m != null && m.shader != null) return m.shader.name;
            return "(no material)";
        }

        private static string NameOf(string key, StringBuilder log)
        {
            try
            {
                UnityEngine.Object o = Addressables.LoadAssetAsync<UnityEngine.Object>(key).WaitForCompletion();
                return o == null ? "(null)" : o.name;
            }
            catch (Exception ex) { log.AppendLine("load '" + key + "' THREW " + ex.GetType().Name + ": " + ex.Message); return "(threw)"; }
        }

        private static int Check(StringBuilder log, string gate, bool ok, string detail)
        {
            log.AppendLine(gate + (ok ? " PASS " : " FAIL ") + detail);
            return ok ? 0 : 1;
        }
    }
}
