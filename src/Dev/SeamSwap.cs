using System;
using System.Collections.Generic;
using System.Text;
using HarmonyLib;
using Morgott.ContentTool.Project;
using PhoenixPoint.Common.Entities.Addons;
using UnityEngine;
using UnityEngine.AddressableAssets;

namespace Morgott.ContentTool.Dev
{
    /// <summary>
    /// The DEVELOPER WORKBENCH engine (FINAL-PLAN 39.3). Shipping belongs to route vii
    /// (`ct_route7`, patched bundle copy + catalog repoint) - nothing here is a shipping mechanism.
    ///
    ///   AddonSkinDataBase.GetPrefabAsset(AssetReferenceGameObject)
    ///   decompiled\...\PhoenixPoint.Common.Entities.Addons\AddonSkinDataBase.cs:19-30
    ///
    /// One Harmony postfix, one binding table, one re-apply loop, and two clients on top of it:
    ///   ct_seamprobe   the measurement instrument that settled R-U1/R-U2 (foundations #18-#21);
    ///   ct_replace     the actual dev swap (Task 24/25).
    /// They share the bookkeeping instead of owning a copy each - the apply/revert/re-apply logic is
    /// the part that was expensive to get right, and a second implementation of it would drift.
    ///
    /// RE-APPLY IS NOT OPTIONAL, it is measured: Addressables releases and re-acquires prefabs
    /// (foundations #20 - 102 of 410 resolves handed back a clean object across a mission load), so a
    /// one-shot write silently evaporates on the next scene transition and the author is left
    /// believing they saw something they did not.
    /// </summary>
    internal static class SeamSwap
    {
        private const string HarmonyId = "morgott.contenttool.seamprobe";
        private const int MaxReportedGuids = 25;

        private sealed class Entry
        {
            internal string Prefab;
            internal int InstanceId;
            internal int Renderers;
            internal int Rigged;
            /// <summary>The target path this prefab would be authored against, in TargetPath spelling.</summary>
            internal string Target;
            /// <summary>Did that subpath resolve back to a live transform + component on this prefab?</summary>
            internal bool Resolvable;
            internal int Seen;
            internal int SameInstance;
            /// <summary>The prefab object itself, so 'mutate' can pick a target the scene actually wears.</summary>
            internal GameObject Asset;
            /// <summary>sharedMesh of the slot Target names, as it was before we touched anything.</summary>
            internal Mesh SlotMesh;
        }

        private static Harmony harmony;
        private static readonly Dictionary<string, Entry> Seen = new Dictionary<string, Entry>(StringComparer.Ordinal);
        /// <summary>
        /// Every Mesh the seam prefabs carry, by instance id. Attribution by MESH IDENTITY, not by
        /// name: an instance shares the prefab's Mesh asset, while its root GameObject is named by
        /// whoever built the character, so a name match would silently measure nothing.
        /// </summary>
        private static readonly HashSet<int> SeamMeshes = new HashSet<int>();
        private static int noGuid, postfixErrors;
        private static string firstError;

        /// <summary>
        /// One written slot: what we put there, and what has to go back. The Texture arm carries the
        /// original as PNG bytes because ImageConversion.LoadImage DESTROYS the pixels it overwrites -
        /// there is nothing to put back unless it was captured first (FINAL-PLAN 39.3 / 29.5).
        /// </summary>
        private enum Slot { Mesh, Texture, Material }

        private sealed class Mark
        {
            internal string Path, Component;
            internal TargetPath Target;    // texture/material arm: re-resolved on every hit
            internal Slot Kind;
            internal Material[] OriginalMats;  // material arm: the array as the game had it
            internal Material Ours;            // material arm: the clone we assigned
            internal Mesh Original;        // mesh arm
            internal Renderer Renderer;    // mesh arm: the renderer we last wrote
            internal MeshFilter Filter;    // mesh arm: a static target has no Renderer of its own to re-find it by
            internal Mesh OursMesh;        // mesh arm: the mesh WE own and write (the probe's marks share one)
            internal Bounds OriginalBounds; // mesh arm, skinned: localBounds follow the mesh or cull it away
            internal bool HadBounds;
            internal Texture2D OursTex;    // texture arm: the texture WE own and bind
            /// <summary>
            /// The file the author NAMED, before <see cref="DevLoop.Resolve"/> pointed it at a variant
            /// set. Null for the probe's own marks and for the value arms - those have no file, and a
            /// mark with no source is never re-read (S2). Keeping the AUTHORED path rather than the
            /// resolved one is what makes a set switch a re-resolve instead of a one-way trip.
            /// </summary>
            internal string Source;
        }

        /// <summary>
        /// R-U2 state. EVERY resolvable seam prefab is marked, not one chosen in advance.
        ///
        /// Three runs picked a single target and all three came back INCONCLUSIVE, because the pick
        /// kept landing on CHR_PX_UNA_TS_V01 - the naked torso under the armour - and by report time
        /// nothing on screen wore it (12:53: 0/0; 13:02: 0/0 with 8 wearers at write time). Marking
        /// every prefab removes the target lottery and the timing skill from the measurement: if the
        /// seam reaches rendered objects at all, SOMETHING in the scene must end up wearing our mesh.
        /// </summary>
        private static readonly Dictionary<string, Mark> Marks = new Dictionary<string, Mark>(StringComparer.Ordinal);
        private static readonly HashSet<int> MarkedOriginals = new HashSet<int>();
        private static Mesh mutOurs;
        private static int mutChecks, mutStillOurs, mutReapplied;

        internal static bool Active { get { return harmony != null; } }

        /// <summary>How many slots currently carry a swap - a gate must not run over a live one.</summary>
        internal static int MarkCount { get { return Marks.Count; } }

        internal static string Run(string[] args)
        {
            string cmd = args != null && args.Length > 0 ? args[0].ToLowerInvariant() : "report";
            switch (cmd)
            {
                case "on": return On();
                case "off": return Off();
                case "mutate": return Mutate();
                case "report": return Report();
                default: return "usage: ct_seamprobe [on|mutate|report|off]";
            }
        }

        // ------------------------------------------------------------- ct_replace / ct_revert (T24)

        /// <summary>
        /// `ct_replace &lt;targetpath&gt; &lt;file|value&gt;` - the dev swap. A guid: target needs the seam ON and
        /// already resolved (the GUID is only knowable from a resolve); a name: target needs the dev
        /// scan ON instead (Task 26), and is the only way to reach something no resolve goes through.
        ///
        /// The TARGET PATH names the slot and therefore the arm - one grammar, no new verbs (Task 25):
        ///   ...@Renderer.materials[i].tex:_Prop   a .png/.jpg              (Task 24)
        ///   ...@SkinnedMeshRenderer.mesh          a .glb/.obj, rebound to the target's own skeleton
        ///   ...@MeshFilter.mesh                   a .glb/.obj, static target
        ///   ...@Renderer.materials[i].col:_Prop   r,g,b,a
        ///   ...@Renderer.materials[i].num:_Prop   a number
        /// </summary>
        internal static string Replace(string[] args)
        {
            if (args == null || args.Length < 2)
                return "usage: ct_replace <targetpath> <file.png|file.glb|file.obj|value>   " +
                       "e.g. ct_replace guid:441c...#Root@SkinnedMeshRenderer.mesh C:\\torso.glb";

            TargetPath p;
            string why;
            if (!TargetPath.TryParse(args[0], out p, out why)) return "ct_replace REFUSED: " + why;
            if (p.Anchor != AnchorKind.Guid && p.Anchor != AnchorKind.Name)
                return "ct_replace REFUSED: only guid: (seam) and name: (dev scan) targets can be applied ('" + args[0] + "')";

            bool texture = p.Qualifier == "tex" && p.Member != null;
            bool value = (p.Qualifier == "col" || p.Qualifier == "num") && p.Member != null;
            bool mesh = p.Field == "mesh" && p.Member == null;
            if (!texture && !value && !mesh)
                return "ct_replace REFUSED: '" + args[0] + "' names no slot this can write " +
                       "(expected ...@Renderer.materials[i].tex:_Prop for a .png, ...@SkinnedMeshRenderer.mesh " +
                       "or ...@MeshFilter.mesh for a .glb/.obj, or ...materials[i].col:_Prop / .num:_Prop for a value)";

            // Two discovery seams, one apply path. guid: comes off the resolve postfix and is the only
            // form that can ever ship; name: comes off the dev scan (Task 26) and is refused by
            // ReplacementSet.Bakeable, because a released mod has no scan to re-find it with.
            GameObject root;
            string key;
            if (p.Anchor == AnchorKind.Guid)
            {
                if (harmony == null) return "ct_replace: the seam is OFF - run 'ct_seamprobe on' first";
                Entry e;
                if (!Seen.TryGetValue(p.AnchorValue, out e) || e.Asset == null)
                    return "ct_replace REFUSED: the seam has not resolved guid:" + p.AnchorValue +
                           " in this session - load the screen that shows it, then retry";
                root = e.Asset;
                key = p.AnchorValue;
            }
            else
            {
                root = DevScan.Find(p.AnchorValue, out why);
                if (root == null) return "ct_replace REFUSED: " + why;
                key = "name:" + p.AnchorValue;
            }

            if (Marks.ContainsKey(key))
                return "ct_replace REFUSED: " + key + " already carries a swap - ct_revert first";

            Transform tr = p.Transform.Length == 0 ? root.transform : root.transform.Find(p.Transform);
            if (tr == null) return "ct_replace REFUSED: no transform '" + p.Transform + "' under '" + root.name + "'";

            if (value) return ReplaceValue(args, p, tr, key);

            // The active variant set decides WHICH file this authored path reads (S2). With no set
            // active - and with dev mode off, which is every player's session - Resolve is identity.
            string file = DevLoop.Resolve(args[1]);
            if (mesh) return ReplaceMesh(file, args[1], p, tr, key);

            if (!System.IO.File.Exists(file)) return "ct_replace REFUSED: no such file '" + file + "'";
            Texture2D tex = ResolveTexture(root, p, out why);
            if (tex == null) return "ct_replace REFUSED: " + why;

            Renderer rend = tr.GetComponent<Renderer>();
            if (rend == null) return "ct_replace REFUSED: '" + p.Transform + "' has no Renderer";

            Texture2D ours = FromPng(System.IO.File.ReadAllBytes(file));
            if (ours == null) return "ct_replace REFUSED: '" + file + "' is not an image Unity can decode";

            Mark m = new Mark { Path = p.Transform, Component = p.Component, Target = p, Kind = Slot.Texture, OursTex = ours, Source = args[1] };
            if (!WriteMaterial(m, rend, p.Index < 0 ? 0 : p.Index, p.Member, c => c.SetTexture(p.Member, ours), out why))
            { UnityEngine.Object.Destroy(ours); return "ct_replace REFUSED: " + why; }

            Marks[key] = m;
            return "ct_replace applied " + p.Format() + "\n" +
                   "  was '" + tex.name + "' " + tex.width + "x" + tex.height + " " + tex.format +
                   " (sha1 " + AssetSha1.OfTexture(tex) + ", NOT written - the game's texture is untouched)\n" +
                   "  now '" + ours.name + "' " + ours.width + "x" + ours.height +
                   " bound through a cloned material; ct_revert restores the origin array";
        }

        /// <summary>
        /// Task 25, the mesh arm: an author's .glb/.obj onto a live renderer, no restart. Same
        /// bookkeeping as every other swap, so `ct_revert` and the re-apply loop already cover it.
        /// The skinned case keeps the SHIPPED skeleton (<see cref="LiveMesh.Bind"/>) and moves
        /// localBounds with the mesh - bounds left describing the old geometry cull the new mesh away
        /// wherever the old one was not, which reads exactly like "the replacement did not load".
        /// </summary>
        private static string ReplaceMesh(string file, string authored, TargetPath p, Transform tr, string key)
        {
            if (!System.IO.File.Exists(file)) return "ct_replace REFUSED: no such file '" + file + "'";

            SkinnedMeshRenderer smr = tr.GetComponent<SkinnedMeshRenderer>();
            MeshFilter mf = smr != null ? null : tr.GetComponent<MeshFilter>();
            if (smr == null && mf == null)
                return "ct_replace REFUSED: '" + p.Transform + "' has neither a SkinnedMeshRenderer nor a MeshFilter";

            Mesh origin = smr != null ? smr.sharedMesh : mf.sharedMesh;
            string why;
            Morgott.ContentTool.Import.SkinnedModel model;
            Mesh ours = LiveMesh.Load(file, out why, out model);
            if (ours == null) return "ct_replace REFUSED: " + why;
            // The live twin of BundleBaker.ReplaceMesh's guard, and it has to be here rather than in
            // LiveMesh.Bind: nothing may be assigned on a refusal, or the player watches their
            // character weld itself to a hip mid-animation. A MeshFilter target never reaches this.
            // Destroy before returning: no Mark owns `ours` yet, so a bare return leaks the native
            // mesh on every refused attempt - the same shape the texture refusal above already has.
            if (smr != null && (model == null || model.JointNames.Count == 0))
            // tr.name, not p.Transform: the target may be addressed in a form that leaves the path
            // empty, and the refusal then named '' instead of the renderer the player is looking at.
            { UnityEngine.Object.Destroy(ours); return "ct_replace REFUSED: " + Bake.SkinFields.Skinless(tr.name); }

            string skin = smr != null ? LiveMesh.Bind(ours, smr, model) : null;
            Mark m = new Mark
            {
                Path = p.Transform,
                Component = p.Component,
                Target = p,
                Kind = Slot.Mesh,
                Source = authored,
                Original = origin,
                OursMesh = ours,
                Filter = mf,
                Renderer = smr != null ? (Renderer)smr : mf.GetComponent<Renderer>()
            };
            if (smr != null)
            {
                m.OriginalBounds = smr.localBounds;
                m.HadBounds = true;
                smr.sharedMesh = ours;
                smr.localBounds = ours.bounds;
            }
            else mf.sharedMesh = ours;

            Marks[key] = m;
            return "ct_replace applied " + p.Format() + "\n" +
                   "  was '" + (origin == null ? "(none)" : origin.name) + "' verts=" +
                   (origin == null ? 0 : origin.vertexCount) + " (NOT written - the game's mesh is untouched)\n" +
                   "  now '" + ours.name + "' verts=" + ours.vertexCount + " indices=" + ours.triangles.Length +
                   " bounds=" + ours.bounds.size + (skin == null ? " (static target)" : "; " + skin) +
                   "; ct_revert puts the origin Mesh object back";
        }

        /// <summary>
        /// Task 25, the material-value arm: `...materials[i].col:_Prop r,g,b,a` or `.num:_Prop 0.5`.
        /// Nothing new underneath - <see cref="WriteMaterial"/> clones the game's material, changes
        /// the one named property and assigns the whole array, exactly as the texture arm does.
        /// </summary>
        private static string ReplaceValue(string[] args, TargetPath p, Transform tr, string key)
        {
            Renderer rend = tr.GetComponent<Renderer>();
            if (rend == null) return "ct_replace REFUSED: '" + p.Transform + "' has no Renderer";
            int index = p.Index < 0 ? 0 : p.Index;

            Mark m = new Mark { Path = p.Transform, Component = p.Component, Target = p, Kind = Slot.Material };
            string why, shown;
            if (p.Qualifier == "col")
            {
                Color c;
                if (!ParseColor(args[1], out c))
                    return "ct_replace REFUSED: '" + args[1] + "' is not a colour (expected r,g,b,a with 0..1 numbers)";
                if (!WriteMaterial(m, rend, index, p.Member, mat => mat.SetColor(p.Member, c), out why))
                    return "ct_replace REFUSED: " + why;
                shown = c.ToString();
            }
            else
            {
                float f;
                if (!float.TryParse(args[1], System.Globalization.NumberStyles.Float,
                                    System.Globalization.CultureInfo.InvariantCulture, out f))
                    return "ct_replace REFUSED: '" + args[1] + "' is not a number";
                if (!WriteMaterial(m, rend, index, p.Member, mat => mat.SetFloat(p.Member, f), out why))
                    return "ct_replace REFUSED: " + why;
                shown = f.ToString(System.Globalization.CultureInfo.InvariantCulture);
            }

            Marks[key] = m;
            return "ct_replace applied " + p.Format() + "\n" +
                   "  material '" + m.Ours.name + "' (a CLONE - the game's own material object is untouched) " +
                   p.Member + "=" + shown + "; ct_revert restores the origin array";
        }

        /// <summary>"r,g,b" or "r,g,b,a", invariant - a ru-RU machine must not read 0.5 as a separator.</summary>
        private static bool ParseColor(string s, out Color c)
        {
            c = default(Color);
            string[] parts = s.Split(',');
            if (parts.Length < 3 || parts.Length > 4) return false;
            float[] v = new float[4];
            v[3] = 1f;
            for (int i = 0; i < parts.Length; i++)
                if (!float.TryParse(parts[i], System.Globalization.NumberStyles.Float,
                                    System.Globalization.CultureInfo.InvariantCulture, out v[i])) return false;
            c = new Color(v[0], v[1], v[2], v[3]);
            return true;
        }

        /// <summary>`ct_revert` - undoes every swap and every probe mark, same bookkeeping.</summary>
        internal static string Revert()
        {
            return Restore();
        }

        /// <summary>
        /// Gate R2 (FINAL-PLAN 39.7, Task 24), one run, no files: pick a shipped texture at the seam,
        /// sha1 it, replace it, sha1 (must differ), revert, sha1 (must EQUAL the first).
        /// CONTROL in the same run: a second shipped texture that no record names, sha1'd at all three
        /// points, must never move. The control is what separates "revert works" from "the readback
        /// returns the same bytes no matter what", and it is also gate R-U4 (39.6) in passing.
        /// </summary>
        internal static string TexSwapGate()
        {
            Renderer rend = null;
            Texture2D subject = null, control = null;
            int slot = 0;

            // Seam first when it has anything, live scene otherwise: the postfix only sees resolves
            // that happen AFTER it attaches, so a screen already on display leaves the table empty
            // and this gate could never run (it refused twice at 13:42 for exactly that). S39.8
            // permits the scan in developer mode. Either way the subject says nothing about seam
            // coverage - R1 (#18-#21) measured that - and R2 is about replace/revert fidelity.
            bool scanned = false;
            foreach (KeyValuePair<string, Entry> kv in Seen)
            {
                if (kv.Value.Asset == null) continue;
                if (Pick(kv.Value.Asset.GetComponentsInChildren<Renderer>(true), ref rend, ref slot, ref subject, ref control)) break;
            }
            if (subject == null)
            {
                scanned = true;
                List<Renderer> live = new List<Renderer>();
                foreach (Renderer r in Resources.FindObjectsOfTypeAll<Renderer>())
                    if (r.gameObject.scene.IsValid()) live.Add(r);
                Pick(live.ToArray(), ref rend, ref slot, ref subject, ref control);
            }

            if (subject == null) return "ct_texswap: no _MainTex Texture2D anywhere at the seam or in the live scene";
            if (control == null) return "ct_texswap: only one texture found; the gate REFUSES to run without its control";

            StringBuilder log = new StringBuilder();
            int failures = 0;
            log.AppendLine("ct_texswap subject='" + subject.name + "' " + subject.width + "x" + subject.height + " " +
                           subject.format + " on '" + rend.name + "' materials[" + slot + "]" +
                           (scanned ? " picked by DEV SCAN (seam table empty - says nothing about seam coverage)" : " picked at the seam"));
            log.AppendLine("  control='" + control.name + "' " + control.width + "x" + control.height + " " + control.format);

            string pre = AssetSha1.OfTexture(subject), cPre = AssetSha1.OfTexture(control);
            if (pre == null) return log.Append("R2 FAIL could not read the subject back at all").ToString();
            Material[] originMats = rend.sharedMaterials;

            Texture2D ours = Magenta(subject.width, subject.height);
            Mark m = new Mark { Path = "", Component = "Renderer", Kind = Slot.Texture, OursTex = ours };
            string why;
            if (!WriteMaterial(m, rend, slot, "_MainTex", c => c.SetTexture("_MainTex", ours), out why))
            { UnityEngine.Object.Destroy(ours); return log.Append("R2 FAIL ").Append(why).ToString(); }
            Marks["texswap:" + rend.GetInstanceID()] = m;

            Texture2D shown = rend.sharedMaterials[slot].GetTexture("_MainTex") as Texture2D;
            failures += Check(log, "R2-replaced", ReferenceEquals(shown, ours) && AssetSha1.OfTexture(shown) != pre,
                "the renderer now shows '" + (shown == null ? "(null)" : shown.name) + "' sha1=" +
                AssetSha1.OfTexture(shown) + " (was " + pre + ")");

            // The assertion that keeps the old destructive path from creeping back: the game's own
            // Texture2D must be BYTE-IDENTICAL while the swap is live. LoadImage-in-place fails this.
            failures += Check(log, "R2-untouched", AssetSha1.OfTexture(subject) == pre,
                "the game's texture object during the swap: " + AssetSha1.OfTexture(subject));

            log.AppendLine(Restore());
            string after = AssetSha1.OfTexture(subject), cAfter = AssetSha1.OfTexture(control);
            failures += Check(log, "R2-reverted", after == pre, "pre=" + pre + " after-revert=" + after);
            failures += Check(log, "R2-revert-identity",
                ReferenceEquals(rend.sharedMaterials[slot], originMats[slot]) &&
                ReferenceEquals(rend.sharedMaterials[slot].GetTexture("_MainTex"), subject),
                "materials[" + slot + "] and its _MainTex are the origin OBJECTS again");
            failures += Check(log, "R2-CONTROL", cPre == cAfter, "control sha1 " + cPre + " / " + cAfter);

            log.Append(failures == 0 ? "ct_texswap: R2 PASS (revert is exact because the game's texture is never written)"
                                     : "ct_texswap: " + failures + " FAILURE(S)");
            return log.ToString();
        }

        /// <summary>First renderer with a _MainTex Texture2D, plus a DIFFERENT texture for the control.</summary>
        private static bool Pick(Renderer[] rs, ref Renderer rend, ref int slot, ref Texture2D subject, ref Texture2D control)
        {
            foreach (Renderer r in rs)
            {
                Material[] mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                {
                    if (mats[i] == null || !mats[i].HasProperty("_MainTex")) continue;
                    Texture2D t = mats[i].GetTexture("_MainTex") as Texture2D;
                    if (t == null) continue;
                    if (subject == null) { subject = t; rend = r; slot = i; }
                    else if (!ReferenceEquals(t, subject)) control = t;
                }
                if (subject != null && control != null) return true;
            }
            return false;
        }

        // ----------------------------------------------------- mesh + material arms (Task 25)

        /// <summary>
        /// RR's material mechanics, ported (`MeshReplacer.cs:2110-2132`, `:2169-2179`): clone the
        /// ORIGINAL so the game's material object is never written, change only named properties, and
        /// assign the whole `sharedMaterials` array in one go - nothing is written until every slot
        /// succeeded, so a half-applied renderer cannot exist.
        /// </summary>
        private static bool WriteMaterial(Mark m, Renderer r, int index, string property, Action<Material> mutate, out string why)
        {
            Material[] mats = r.sharedMaterials;
            if (index >= mats.Length || mats[index] == null)
            { why = "materials[" + index + "] of " + mats.Length + " is missing"; return false; }
            if (!mats[index].HasProperty(property))
            { why = "material '" + mats[index].name + "' has no property '" + property + "'"; return false; }

            m.OriginalMats = mats;
            Material clone = new Material(mats[index]) { name = mats[index].name + " [ct]" };
            mutate(clone);

            Material[] next = (Material[])mats.Clone();
            next[index] = clone;
            r.sharedMaterials = next;          // atomic: one assignment, whole array
            m.Ours = clone;
            m.Renderer = r;
            why = null;
            return true;
        }

        /// <summary>Re-apply arm for materials: a re-acquired prefab wears the game's array again.</summary>
        private static void CheckMaterial(Mark m, GameObject prefab)
        {
            Transform t = m.Path.Length == 0 ? prefab.transform : prefab.transform.Find(m.Path);
            Renderer r = t == null ? null : t.GetComponent<Renderer>();
            if (r == null || m.Ours == null) return;

            Material[] mats = r.sharedMaterials;
            int i = m.Target.Index < 0 ? 0 : m.Target.Index;
            mutChecks++;
            if (i < mats.Length && ReferenceEquals(mats[i], m.Ours)) { mutStillOurs++; return; }
            if (i >= mats.Length) return;

            m.OriginalMats = mats;
            Material[] next = (Material[])mats.Clone();
            next[i] = m.Ours;
            r.sharedMaterials = next;
            m.Renderer = r;
            mutReapplied++;
        }

        /// <summary>
        /// Gate R3 (FINAL-PLAN 39.7, Task 25), one run: a named renderer wears our mesh (vert count and
        /// bounds logged, differing from the origin) and our material (shader preserved, one property
        /// changed), then reverts to the exact origin OBJECT (`sharedMesh == originalMesh` by
        /// reference, not by value). CONTROL: a sibling renderer on the same prefab, named by nothing,
        /// compared by reference at all three points.
        /// </summary>
        internal static string MeshSwapGate()
        {
            if (harmony == null) return "ct_meshswap: the seam is OFF - run 'ct_seamprobe on', open a screen with characters, then retry";
            // The gate owns the binding table while it runs; it must not overwrite a live swap or
            // destroy the probe's marker mesh out from under its marks.
            if (Marks.Count > 0) return "ct_meshswap: REFUSED - " + Marks.Count + " swap(s) already active, ct_revert first";

            SkinnedMeshRenderer subject = null, sibling = null;
            GameObject prefab = null;
            string key = null;
            foreach (KeyValuePair<string, Entry> kv in Seen)
            {
                if (kv.Value.Asset == null) continue;
                SkinnedMeshRenderer[] rs = kv.Value.Asset.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                if (rs.Length < 2) continue;                       // need the sibling for the control
                foreach (SkinnedMeshRenderer r in rs)
                {
                    if (r.sharedMesh == null) continue;
                    if (subject == null) subject = r;
                    else if (!ReferenceEquals(r, subject) && r.sharedMesh != null) { sibling = r; break; }
                }
                if (subject != null && sibling != null) { prefab = kv.Value.Asset; key = kv.Key; break; }
                subject = sibling = null;
            }
            if (subject == null || sibling == null)
                return "ct_meshswap: no seam prefab carries two skinned renderers yet - open a screen with characters " +
                       "(the gate REFUSES to run without its sibling control)";

            StringBuilder log = new StringBuilder();
            int failures = 0;
            Mesh originMesh = subject.sharedMesh, siblingMesh = sibling.sharedMesh;
            Material[] originMats = subject.sharedMaterials, siblingMats = sibling.sharedMaterials;
            string shader = originMats.Length > 0 && originMats[0] != null ? originMats[0].shader.name : "(none)";
            log.AppendLine("ct_meshswap prefab='" + prefab.name + "' subject='" + subject.name +
                           "' origin mesh='" + originMesh.name + "' verts=" + originMesh.vertexCount +
                           " bounds=" + originMesh.bounds.size + " shader='" + shader + "'");
            log.AppendLine("  CONTROL sibling='" + sibling.name + "' mesh='" + siblingMesh.name + "'");

            string path = RelativePath(prefab.transform, subject.transform);
            TargetPath meshTarget, matTarget;
            string why;
            if (!TargetPath.TryParse("guid:" + key + "#" + path + "@SkinnedMeshRenderer.mesh", out meshTarget, out why))
                return log.Append("R3 FAIL target path: ").Append(why).ToString();
            TargetPath.TryParse("guid:" + key + "#" + path + "@Renderer.materials[0].tex:_MainTex", out matTarget, out why);

            // ---- mesh (MeshReplacer.cs:1896-1901 skinned arm: assign, never touch the skeleton)
            Mesh ours = Quad();
            Mark mesh = new Mark { Path = path, Component = "SkinnedMeshRenderer", Target = meshTarget, Kind = Slot.Mesh, Original = originMesh, OursMesh = ours, Renderer = subject };
            subject.sharedMesh = ours;
            Marks[key] = mesh;
            MarkedOriginals.Add(originMesh.GetInstanceID());
            failures += Check(log, "R3-mesh",
                ReferenceEquals(subject.sharedMesh, ours) && subject.sharedMesh.vertexCount != originMesh.vertexCount,
                "wears '" + subject.sharedMesh.name + "' verts=" + subject.sharedMesh.vertexCount +
                " bounds=" + subject.sharedMesh.bounds.size + " (origin verts=" + originMesh.vertexCount + ")");

            // ---- material: same renderer, one property, whole array assigned at once
            Mark mat = new Mark { Path = path, Component = "Renderer", Target = matTarget ?? meshTarget, Kind = Slot.Material };
            bool matWritten = originMats.Length > 0 && originMats[0] != null &&
                              WriteMaterial(mat, subject, 0, "_Color", c => c.SetColor("_Color", new Color(1f, 0f, 1f, 1f)), out why);
            if (matWritten)
            {
                Material now = subject.sharedMaterials[0];
                failures += Check(log, "R3-material",
                    !ReferenceEquals(now, originMats[0]) && now.shader.name == shader && now.GetColor("_Color") == new Color(1f, 0f, 1f, 1f),
                    "material '" + now.name + "' shader='" + now.shader.name + "' _Color=" + now.GetColor("_Color") +
                    " (the game's own material object untouched: " + !ReferenceEquals(now, originMats[0]) + ")");
            }
            else log.AppendLine("R3-material SKIP " + (why ?? "no material on the subject") + " - mesh arm still measured");

            failures += Check(log, "R3-CONTROL-mid",
                ReferenceEquals(sibling.sharedMesh, siblingMesh) && ReferenceEquals(sibling.sharedMaterials[0], siblingMats[0]),
                "sibling still wears its own mesh and material while the subject is swapped");

            // ---- revert, and prove it by OBJECT IDENTITY, not by value
            if (matWritten) subject.sharedMaterials = mat.OriginalMats;
            subject.sharedMesh = mesh.Original;
            Marks.Remove(key);
            MarkedOriginals.Clear();
            UnityEngine.Object.Destroy(ours);
            if (mat.Ours != null) UnityEngine.Object.Destroy(mat.Ours);

            failures += Check(log, "R3-revert-mesh", ReferenceEquals(subject.sharedMesh, originMesh),
                "sharedMesh == the origin Mesh object: " + ReferenceEquals(subject.sharedMesh, originMesh));
            if (matWritten)
                failures += Check(log, "R3-revert-material", ReferenceEquals(subject.sharedMaterials[0], originMats[0]),
                    "sharedMaterials[0] == the origin Material object: " + ReferenceEquals(subject.sharedMaterials[0], originMats[0]));
            failures += Check(log, "R3-CONTROL-end",
                ReferenceEquals(sibling.sharedMesh, siblingMesh) && ReferenceEquals(sibling.sharedMaterials[0], siblingMats[0]),
                "sibling unchanged throughout");

            log.Append(failures == 0 ? "ct_meshswap: R3 PASS" : "ct_meshswap: " + failures + " FAILURE(S)");
            return log.ToString();
        }

        /// <summary>Four verts, so "the mesh changed" is a vertex COUNT difference, not a judgement call.</summary>
        private static Mesh Quad()
        {
            Mesh m = new Mesh { name = "ct_swap_quad" };
            m.vertices = new[] { new Vector3(0, 0, 0), new Vector3(0, 0.4f, 0), new Vector3(0.4f, 0.4f, 0), new Vector3(0.4f, 0, 0) };
            m.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            m.RecalculateNormals();
            m.RecalculateBounds();
            return m;
        }

        /// <summary>Solid magenta texture of our own, so "did it change" is not a subtle judgement.</summary>
        private static Texture2D Magenta(int w, int h)
        {
            Texture2D t = new Texture2D(w, h, TextureFormat.RGBA32, false) { name = "ct_swap_magenta" };
            Color32[] px = new Color32[w * h];
            for (int i = 0; i < px.Length; i++) px[i] = new Color32(255, 0, 255, 255);
            t.SetPixels32(px);
            t.Apply();
            return t;
        }

        /// <summary>Our own texture from image bytes; LoadImage on OUR object destroys nothing.</summary>
        private static Texture2D FromPng(byte[] bytes)
        {
            Texture2D t = new Texture2D(2, 2, TextureFormat.RGBA32, false) { name = "ct_replacement" };
            if (t.LoadImage(bytes)) return t;
            UnityEngine.Object.Destroy(t);
            return null;
        }

        private static int Check(StringBuilder log, string gate, bool ok, string detail)
        {
            log.AppendLine(gate + (ok ? " PASS " : " FAIL ") + detail);
            return ok ? 0 : 1;
        }

        private static string On()
        {
            if (harmony != null) return "ct_seamprobe already ON (" + Seen.Count + " guid(s) so far)";
            // A stale reading from an earlier run must never pass as this run's result (METHODOLOGY).
            Restore();
            Seen.Clear();
            SeamMeshes.Clear();
            noGuid = postfixErrors = mutChecks = mutStillOurs = mutReapplied = 0;
            firstError = null;

            System.Reflection.MethodInfo target = AccessTools.Method(typeof(AddonSkinDataBase), "GetPrefabAsset");
            if (target == null) return "ct_seamprobe FAIL AddonSkinDataBase.GetPrefabAsset not found";

            harmony = new Harmony(HarmonyId);
            harmony.Patch(target, postfix: new HarmonyMethod(AccessTools.Method(typeof(SeamSwap), nameof(Post))));

            // The exact bound signature, printed: a silently unbound patch (AccessTools param
            // mismatch, a Prepare() that returned false) is the false-green this project keeps
            // killing, and "0 hits" must never be confusable with "never attached".
            System.Text.StringBuilder sig = new System.Text.StringBuilder();
            foreach (System.Reflection.ParameterInfo pi in target.GetParameters())
                sig.Append(sig.Length == 0 ? "" : ", ").Append(pi.ParameterType.Name).Append(' ').Append(pi.Name);
            HarmonyLib.Patches info = Harmony.GetPatchInfo(target);
            int patched = info == null || info.Postfixes == null ? 0 : info.Postfixes.Count;
            return "ct_seamprobe ON - patched " + target.DeclaringType.FullName + "." + target.Name +
                   "(" + sig + ") -> " + target.ReturnType.Name + " in " + target.DeclaringType.Assembly.GetName().Name +
                   "; postfixes now attached to THAT method: " + patched + " (0 = silent bind failure)" +
                   "; load a tactical mission, then ct_seamprobe mutate, then ct_seamprobe report";
        }

        private static string Off()
        {
            StringBuilder log = new StringBuilder();
            log.AppendLine(Restore());
            if (harmony == null) return log.Append("ct_seamprobe was already OFF").ToString();
            harmony.UnpatchAll(HarmonyId);
            harmony = null;
            return log.Append("ct_seamprobe OFF (kept ").Append(Seen.Count).Append(" guid(s) for report)").ToString();
        }

        /// <summary>
        /// The postfix. Contained: a throw here would take a prefab resolve down with it, and every
        /// character in the game goes through this call.
        /// </summary>
        private static void Post(AssetReferenceGameObject assetReference, GameObject __result)
        {
            try
            {
                if (__result == null) return;
                string guid = assetReference == null ? null : assetReference.AssetGUID;
                if (string.IsNullOrEmpty(guid)) { noGuid++; return; }

                Entry e;
                if (!Seen.TryGetValue(guid, out e)) { e = Describe(guid, __result); Seen[guid] = e; }
                e.Seen++;
                e.Asset = __result;
                if (e.InstanceId == __result.GetInstanceID()) e.SameInstance++;

                // Writes are never automatic: 'mutate' arms them, this only keeps them applied.
                CheckMutation(guid, __result);
            }
            catch (Exception ex)
            {
                postfixErrors++;
                if (firstError == null) firstError = ex.ToString();
            }
        }

        /// <summary>What this prefab looks like, and what an author would have to write to name a slot on it.</summary>
        private static Entry Describe(string guid, GameObject prefab)
        {
            Entry e = new Entry { Prefab = prefab.name, InstanceId = prefab.GetInstanceID() };
            Renderer[] rs = prefab.GetComponentsInChildren<Renderer>(true);
            e.Renderers = rs.Length;
            foreach (Renderer r in rs)
            {
                SkinnedMeshRenderer smr = r as SkinnedMeshRenderer;
                if (smr != null)
                {
                    e.Rigged++;
                    if (smr.sharedMesh != null) SeamMeshes.Add(smr.sharedMesh.GetInstanceID());
                }
                if (e.Target != null) continue;

                string component = smr != null ? "SkinnedMeshRenderer" : "MeshFilter";
                if (smr == null && r.GetComponent<MeshFilter>() == null) continue;

                string candidate = "guid:" + guid + "#" + RelativePath(prefab.transform, r.transform) +
                                   "@" + component + ".mesh";
                TargetPath p;
                string why;
                // Grammar check and resolve check are separate on purpose: a path the parser accepts
                // but the prefab does not carry is still useless.
                if (!TargetPath.TryParse(candidate, out p, out why)) continue;
                e.Target = p.Format();
                Component c = Resolve(prefab, p);
                e.Resolvable = c != null;
                SkinnedMeshRenderer sc = c as SkinnedMeshRenderer;
                MeshFilter fc = c as MeshFilter;
                e.SlotMesh = sc != null ? sc.sharedMesh : (fc != null ? fc.sharedMesh : null);
            }
            return e;
        }

        /// <summary>Transform path from the prefab root; "" is the root itself (FINAL-PLAN 39.2).</summary>
        private static string RelativePath(Transform root, Transform t)
        {
            if (t == root) return "";
            List<string> parts = new List<string>();
            for (Transform c = t; c != null && c != root; c = c.parent) parts.Add(c.name);
            parts.Reverse();
            return string.Join("/", parts.ToArray());
        }

        /// <summary>The component a target path names, or null - never a fuzzy match.</summary>
        private static Component Resolve(GameObject prefab, TargetPath p)
        {
            Transform t = p.Transform.Length == 0 ? prefab.transform : prefab.transform.Find(p.Transform);
            if (t == null) return null;
            switch (p.Component)
            {
                case "SkinnedMeshRenderer": return t.GetComponent<SkinnedMeshRenderer>();
                case "MeshFilter": return t.GetComponent<MeshFilter>();
                case "Renderer": return t.GetComponent<Renderer>();
                default: return t.GetComponent(p.Component);
            }
        }

        /// <summary>
        /// R-U2: mark EVERY resolvable seam prefab, re-runnable, and re-applied on every later resolve.
        /// One shared triangle mesh for all of them, so "does the seam reach what is rendered" is one
        /// reference comparison over the scene and does not depend on which prefab was picked or when.
        /// </summary>
        private static string Mutate()
        {
            if (harmony == null) return "ct_seamprobe mutate: probe is OFF - run 'ct_seamprobe on' first";
            if (Seen.Count == 0) return "ct_seamprobe mutate: 0 seam hits so far - the postfix has not fired yet";

            if (mutOurs == null) mutOurs = Triangle();
            int wrote = 0, already = Marks.Count, skipped = 0;
            foreach (KeyValuePair<string, Entry> kv in Seen)
            {
                Entry e = kv.Value;
                if (Marks.ContainsKey(kv.Key)) continue;
                if (!e.Resolvable || e.Asset == null) { skipped++; continue; }

                TargetPath p;
                string why;
                if (!TargetPath.TryParse(e.Target, out p, out why)) { skipped++; continue; }
                Component c = Resolve(e.Asset, p);
                SkinnedMeshRenderer smr = c as SkinnedMeshRenderer;
                MeshFilter mf = c as MeshFilter;
                Mesh original = smr != null ? smr.sharedMesh : (mf != null ? mf.sharedMesh : null);
                if (original == null) { skipped++; continue; }

                Marks[kv.Key] = new Mark
                {
                    Path = p.Transform,
                    Component = p.Component,
                    Original = original,
                    OursMesh = mutOurs,
                    Filter = mf,
                    Renderer = smr != null ? (Renderer)smr : mf.GetComponent<Renderer>()
                };
                MarkedOriginals.Add(original.GetInstanceID());
                if (smr != null) smr.sharedMesh = mutOurs; else mf.sharedMesh = mutOurs;
                wrote++;
            }

            if (wrote == 0 && already == 0)
                return "ct_seamprobe mutate: REFUSED - none of the " + Seen.Count +
                       " seam prefabs has a writable mesh slot. Nothing was written.";

            int nowOurs, nowOriginal;
            CountInstances(out nowOurs, out nowOriginal);
            return "ct_seamprobe mutate: marked " + wrote + " new prefab slot(s), " + already +
                   " already marked, " + skipped + " skipped (no resolvable mesh slot)\n" +
                   "  R-U2b instancesWearingOurs=" + nowOurs + " instancesWearingOriginal=" + nowOriginal +
                   " (right after the write - already-built instances are expected NOT to change)\n" +
                   "  now force NEW instances: re-dress a soldier, or leave and re-enter the screen, then ct_seamprobe report";
        }

        /// <summary>
        /// The decisive count: does a prefab-level write reach the objects the player is looking at?
        /// Over live scene renderers, by mesh reference - ours, or one of the originals we displaced.
        /// </summary>
        private static void CountInstances(out int ours, out int original)
        {
            ours = original = 0;
            if (mutOurs == null) return;
            foreach (SkinnedMeshRenderer r in Resources.FindObjectsOfTypeAll<SkinnedMeshRenderer>())
            {
                if (!r.gameObject.scene.IsValid() || r.sharedMesh == null) continue;
                if (ReferenceEquals(r.sharedMesh, mutOurs)) ours++;
                else if (MarkedOriginals.Contains(r.sharedMesh.GetInstanceID())) original++;
            }
        }

        /// <summary>
        /// Re-finds the marked slot on the prefab the seam just handed us and WRITES IT AGAIN when it
        /// is not ours.
        ///
        /// Measured 12:53 (Player.log:9277): 102 of 308 resolves came back without our mesh and 'off'
        /// reported "the renderer we wrote is gone" - Addressables had released the prefab and
        /// re-acquired a fresh copy from the bundle, taking the write with it. A one-shot prefab write
        /// is therefore not a binding; re-applying at the seam is what a real implementation must do.
        /// </summary>
        private static void CheckMutation(string guid, GameObject prefab)
        {
            Mark m;
            if (!Marks.TryGetValue(guid, out m)) return;
            // A texture binding IS a material binding now, so both arms share one re-apply.
            if (m.Kind != Slot.Mesh) { CheckMaterial(m, prefab); return; }

            Transform t = m.Path.Length == 0 ? prefab.transform : prefab.transform.Find(m.Path);
            SkinnedMeshRenderer smr = t == null ? null : t.GetComponent<SkinnedMeshRenderer>();
            MeshFilter mf = t == null || smr != null ? null : t.GetComponent<MeshFilter>();
            Mesh now = smr != null ? smr.sharedMesh : (mf != null ? mf.sharedMesh : null);

            // The probe's marks all share one triangle; a ct_replace mark carries its OWN mesh. The
            // re-apply must write back what THAT mark wrote, or a re-acquired prefab silently gets the
            // probe's marker instead of the author's model.
            Mesh ours = m.OursMesh != null ? m.OursMesh : mutOurs;
            if (ours == null) return;

            mutChecks++;
            if (ReferenceEquals(now, ours)) { mutStillOurs++; return; }
            if (smr == null && mf == null) return;

            // A fresh copy: remember ITS original before overwriting, so 'off' restores this copy.
            if (now != null) { m.Original = now; MarkedOriginals.Add(now.GetInstanceID()); }
            m.Renderer = smr != null ? (Renderer)smr : mf.GetComponent<Renderer>();
            m.Filter = mf;
            if (smr != null)
            {
                m.OriginalBounds = smr.localBounds;
                m.HadBounds = true;
                smr.sharedMesh = ours;
                smr.localBounds = ours.bounds;
            }
            else mf.sharedMesh = ours;
            mutReapplied++;
        }

        // ------------------------------------------------------------------ texture arm (Task 24)

        /// <summary>
        /// The Texture2D a `...@Renderer.materials[i].tex:_Prop` path names, or null with a reason.
        /// No fuzzy match: a missing node, a missing material slot and a missing property are three
        /// different refusals.
        /// </summary>
        private static Texture2D ResolveTexture(GameObject prefab, TargetPath p, out string why)
        {
            why = null;
            Transform t = p.Transform.Length == 0 ? prefab.transform : prefab.transform.Find(p.Transform);
            if (t == null) { why = "no transform '" + p.Transform + "' under '" + prefab.name + "'"; return null; }

            Renderer r = t.GetComponent<Renderer>();
            if (r == null) { why = "'" + p.Transform + "' has no Renderer"; return null; }

            Material[] mats = r.sharedMaterials;
            int i = p.Index < 0 ? 0 : p.Index;
            if (i >= mats.Length || mats[i] == null)
            { why = "materials[" + i + "] of " + mats.Length + " is missing"; return null; }
            if (!mats[i].HasProperty(p.Member))
            { why = "material '" + mats[i].name + "' has no property '" + p.Member + "'"; return null; }

            Texture2D tex = mats[i].GetTexture(p.Member) as Texture2D;
            if (tex == null) why = "property '" + p.Member + "' holds no Texture2D";
            return tex;
        }

        private static Mesh Triangle()
        {
            Mesh m = new Mesh { name = "ct_probe_mesh" };
            m.vertices = new[] { new Vector3(0, 0, 0), new Vector3(0, 0.1f, 0), new Vector3(0.1f, 0, 0) };
            m.triangles = new[] { 0, 1, 2 };
            m.RecalculateNormals();
            return m;
        }

        /// <summary>
        /// Undoes ONE mark and frees what that mark owns; true when the slot was still there to put
        /// back. Extracted from <see cref="Restore"/> unchanged, because the live loop (S2) has to
        /// undo a single binding before re-applying it and a second copy of this would drift.
        /// </summary>
        private static bool RestoreOne(Mark m)
        {
            if (m.Kind != Slot.Mesh)
            {
                // Nothing of the game's was overwritten: put its own material array back.
                bool ok = m.Renderer != null && m.OriginalMats != null;
                if (ok) m.Renderer.sharedMaterials = m.OriginalMats;
                if (m.Ours != null) UnityEngine.Object.Destroy(m.Ours);
                if (m.OursTex != null) UnityEngine.Object.Destroy(m.OursTex);
                return ok;
            }

            SkinnedMeshRenderer smr = m.Renderer as SkinnedMeshRenderer;
            MeshFilter mf = smr != null ? null
                          : (m.Filter != null ? m.Filter
                          : (m.Renderer == null ? null : m.Renderer.GetComponent<MeshFilter>()));
            bool back;
            if (smr != null)
            {
                smr.sharedMesh = m.Original;
                if (m.HadBounds) smr.localBounds = m.OriginalBounds;
                back = true;
            }
            else if (mf != null) { mf.sharedMesh = m.Original; back = true; }
            else back = false;   // the copy we wrote was released; nothing of ours survives on it

            // The probe's marks SHARE mutOurs, which Restore destroys once; a ct_replace mark owns
            // its mesh alone.
            if (m.OursMesh != null && !ReferenceEquals(m.OursMesh, mutOurs))
                UnityEngine.Object.Destroy(m.OursMesh);
            return back;
        }

        // ------------------------------------------------------- the developer live loop (S2)

        /// <summary>
        /// Re-read the files behind the bindings the changed paths name and write them again. The
        /// match is by FILE NAME, not by full path: the file the author is editing may be the one in
        /// the active variant set rather than the authored one, and comparing whole paths would miss
        /// exactly that case. ponytail: a same-named file in an INACTIVE set therefore costs one
        /// wasted re-read; upgrade path is to compare against <see cref="DevLoop.Resolve"/> of each
        /// source as well, once anybody has enough sets for it to matter.
        /// </summary>
        internal static string Reapply(List<string> changed)
        {
            return Redo(changed, int.MaxValue, false);
        }

        /// <summary>Every file-backed binding, re-resolved - what a variant-set switch needs.</summary>
        internal static string ReapplyAll()
        {
            return Redo(null, int.MaxValue, false);
        }

        /// <summary>
        /// The periodic pass: only bindings whose slot no longer carries what we wrote. A file event
        /// cannot report an object that was instantiated after the swap, and the seam postfix only
        /// re-applies `guid:` marks (<see cref="CheckMutation"/>) - a `name:` binding has nothing else
        /// to bring it back. Budgeted, because this walks live objects (RR's :1030-1055 shape).
        /// </summary>
        internal static string Rescan(int budget)
        {
            return Redo(null, budget, true);
        }

        private static string Redo(List<string> changed, int budget, bool onlyLost)
        {
            if (Marks.Count == 0) return null;
            List<string> keys = new List<string>(Marks.Keys);
            int done = 0, failed = 0, candidates = 0;
            string first = null;

            foreach (string key in keys)
            {
                if (done >= budget) break;
                Mark m;
                if (!Marks.TryGetValue(key, out m)) continue;
                if (m.Source == null || m.Target == null) continue;       // probe marks and value arms
                if (changed != null && !Touches(changed, m.Source)) continue;
                if (onlyLost && Holds(m)) continue;
                candidates++;

                string target = m.Target.Format();
                string source = m.Source;
                try { RestoreOne(m); }
                catch (Exception ex) { if (first == null) first = ex.Message; }
                Marks.Remove(key);

                // Through the REAL command, so re-apply cannot diverge from apply: it re-resolves the
                // active set, re-reads the file, and re-finds the target the same way it did first.
                string r = Replace(new[] { target, source });
                if (r != null && r.StartsWith("ct_replace applied", StringComparison.Ordinal)) done++;
                else { failed++; if (first == null) first = r; }
            }

            if (candidates == 0) return null;
            return "ct_dev: re-applied " + done + " of " + candidates + " binding(s)" +
                   (failed > 0 ? ", " + failed + " refused - first: " + First(first) : "");
        }

        private static string First(string s)
        {
            if (s == null) return "(none)";
            int nl = s.IndexOf('\n');
            return nl < 0 ? s : s.Substring(0, nl);
        }

        private static bool Touches(List<string> changed, string source)
        {
            string name = System.IO.Path.GetFileName(source);
            foreach (string c in changed)
                if (string.Equals(System.IO.Path.GetFileName(c), name, StringComparison.OrdinalIgnoreCase))
                    return true;
            return false;
        }

        /// <summary>Does the slot this mark named still carry what we wrote?</summary>
        private static bool Holds(Mark m)
        {
            if (m.Kind == Slot.Mesh)
            {
                SkinnedMeshRenderer smr = m.Renderer as SkinnedMeshRenderer;
                if (smr != null) return ReferenceEquals(smr.sharedMesh, m.OursMesh);
                if (m.Filter != null) return ReferenceEquals(m.Filter.sharedMesh, m.OursMesh);
                return false;                     // the renderer is gone; the binding is lost
            }
            if (m.Renderer == null || m.Ours == null) return false;
            Material[] mats = m.Renderer.sharedMaterials;
            int i = m.Target != null && m.Target.Index >= 0 ? m.Target.Index : 0;
            return i < mats.Length && ReferenceEquals(mats[i], m.Ours);
        }

        private static string Restore()
        {
            if (Marks.Count == 0) return "R-U2 nothing was mutated";
            int back = 0, gone = 0;
            string threw = null;
            foreach (KeyValuePair<string, Mark> kv in Marks)
            {
                try { if (RestoreOne(kv.Value)) back++; else gone++; }
                catch (Exception ex) { if (threw == null) threw = ex.Message; }
            }

            Marks.Clear();
            MarkedOriginals.Clear();
            if (mutOurs != null) { UnityEngine.Object.Destroy(mutOurs); mutOurs = null; }
            return "R-U2 restored " + back + " slot(s), " + gone + " already released" +
                   (threw != null ? ", first error: " + threw : "");
        }

        /// <summary>
        /// The measurement. Seam numbers first, then ONE scan pass in the same run as the control, so
        /// coverage is a ratio and not a claim.
        /// </summary>
        private static string Report()
        {
            // "No coverage" and "never ran" are different answers and must never look alike.
            if (Seen.Count == 0)
                return "R1 UNMEASURED - 0 seam hits. The postfix never fired: patch=" +
                       (harmony != null ? "ON" : "OFF") + ", resolvesWithNoGuid=" + noGuid +
                       ", postfixErrors=" + postfixErrors +
                       (firstError != null ? ", first error: " + firstError : "") +
                       ". Do NOT read this as zero coverage - run 'ct_seamprobe on' BEFORE loading a mission.";

            int resolvable = 0, seamRigged = 0, printed = 0, reacquired = 0, resolves = 0;
            foreach (Entry e in Seen.Values)
            {
                if (e.Resolvable) resolvable++;
                seamRigged += e.Rigged;
                resolves += e.Seen;
                reacquired += e.Seen - e.SameInstance;
            }

            // Attribution meshes are re-read from the CURRENT prefab objects, never from the ids
            // captured at first sight: the 12:53 run reported coverage 0,0% purely because
            // Addressables had re-acquired every prefab and the recorded instance ids were dead.
            // That measured staleness, not coverage.
            SeamMeshes.Clear();
            foreach (Entry e in Seen.Values)
            {
                if (e.Asset == null) continue;
                foreach (Renderer r in e.Asset.GetComponentsInChildren<Renderer>(true))
                {
                    SkinnedMeshRenderer s = r as SkinnedMeshRenderer;
                    Mesh m = s != null ? s.sharedMesh : null;
                    if (m == null) { MeshFilter f = r.GetComponent<MeshFilter>(); m = f == null ? null : f.sharedMesh; }
                    if (m != null) SeamMeshes.Add(m.GetInstanceID());
                }
            }

            // ---- control, same run: one scan pass, the thing shipping mode is not allowed to do.
            // Attribution is by Mesh identity, so this is the seam measured against what a scan sees.
            SkinnedMeshRenderer[] all = Resources.FindObjectsOfTypeAll<SkinnedMeshRenderer>();
            int inScene = 0, attributable = 0;
            foreach (SkinnedMeshRenderer r in all)
            {
                if (!r.gameObject.scene.IsValid()) continue;   // loaded asset, not an instance
                inScene++;
                if (r.sharedMesh != null && SeamMeshes.Contains(r.sharedMesh.GetInstanceID())) attributable++;
            }
            string coverage = inScene == 0 ? "n/a" : (100.0 * attributable / inScene).ToString("F1") + "%";

            // The headline is the ratio: every shipping-mode decision in FINAL-PLAN 39 is priced on it.
            StringBuilder log = new StringBuilder();
            log.AppendLine("R1 COVERAGE " + coverage + " = " + attributable + " of " + inScene +
                           " in-scene rigged renderers reachable through the seam" +
                           " (seam guids=" + Seen.Count + " resolvableTargets=" + resolvable + ")");
            log.AppendLine("ct_seamprobe report: patch=" + (harmony != null ? "ON" : "OFF"));

            foreach (KeyValuePair<string, Entry> kv in Seen)
            {
                Entry e = kv.Value;
                if (printed++ < MaxReportedGuids)
                    log.AppendLine("  " + kv.Key + " '" + e.Prefab + "' renderers=" + e.Renderers +
                                   " rigged=" + e.Rigged + " seen=" + e.Seen + " sameInstance=" + e.SameInstance +
                                   " target=" + (e.Target ?? "(none)") + " resolvable=" + e.Resolvable);
            }
            if (Seen.Count > MaxReportedGuids) log.AppendLine("  ... " + (Seen.Count - MaxReportedGuids) + " more");
            log.AppendLine("R-U1 seam: guids=" + Seen.Count + " resolvableTargets=" + resolvable +
                           " riggedRenderersUnderSeamPrefabs=" + seamRigged +
                           " resolvesWithNoGuid=" + noGuid + " postfixErrors=" + postfixErrors);
            if (firstError != null) log.AppendLine("  first postfix error: " + firstError);

            log.AppendLine("R-U2a prefab side: markedPrefabs=" + Marks.Count +
                           (Marks.Count == 0 ? " (run ct_seamprobe mutate)" : "") +
                           " resolvesSinceWrite=" + mutChecks + " stillOurs=" + mutStillOurs +
                           " reapplied=" + mutReapplied +
                           (mutChecks > 1 && mutReapplied == 0 ? " -> the one-shot prefab write SURVIVES"
                            : mutChecks > 1 ? " -> the one-shot prefab write is LOST on re-acquire; the probe re-applied it at the seam " + mutReapplied + "x"
                            : " -> not enough resolves yet, re-enter the screen then report again"));
            log.AppendLine("R-U2c re-acquire: " + reacquired + " of " + resolves +
                           " resolves returned a DIFFERENT prefab object for a GUID already seen" +
                           (reacquired > 0 ? " -> Addressables DOES release and re-acquire; a one-shot write cannot hold" : ""));

            // The decisive number. A surviving prefab write means nothing if no rendered object wears it.
            int instOurs, instOriginal;
            CountInstances(out instOurs, out instOriginal);
            log.AppendLine("R-U2b instance side: instancesWearingOurs=" + instOurs +
                           " instancesWearingOriginal=" + instOriginal +
                           (Marks.Count == 0 ? " -> nothing mutated yet"
                            : instOurs > 0 ? " -> instances DO inherit the prefab write; prefab-level binding is enough"
                            : instOriginal > 0 ? " -> instances do NOT inherit it; binding must be applied per instance (OnCharacterRebuilded)"
                            : " -> no live renderer wears either mesh; this mission never instantiated that prefab, so R-U2b is INCONCLUSIVE"));

            log.AppendLine("CONTROL scan: SkinnedMeshRenderer loaded=" + all.Length + " inScene=" + inScene +
                           " attributableToASeamPrefab=" + attributable + " coverage=" + coverage);

            bool pass = resolvable >= 1 && inScene > 0;
            log.Append(pass ? "R1 PASS" : "R1 FAIL").Append(" (needs >=1 resolvable seam target AND a non-empty scan control)");
            return log.ToString();
        }
    }
}
