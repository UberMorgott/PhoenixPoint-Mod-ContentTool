using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using AssetsTools.NET;
using AssetsTools.NET.Extra;
using Morgott.ContentTool.Import;

namespace Morgott.ContentTool.Bake
{
    /// <summary>
    /// The one place AssetsTools.NET is used: turns already-understood content into a native
    /// AssetBundle. Nothing outside this namespace may touch serialized-file concepts
    /// (FINAL-PLAN 0.2) - live materialization talks to Unity directly.
    ///
    /// The write path is the proven rr_bake sequence (PROVEN-FOUNDATIONS #8): clone a shipped
    /// bundle, add objects whose class the file's typetree never had, register them in
    /// AssetBundle.m_Container, rename the serialized file, write. Cloning rather than building a
    /// UnityFS container from nothing is deliberate - only the clone is proven in-game.
    ///
    /// Usage is create -> Add* -> Write -> Dispose, once per output file.
    /// </summary>
    internal sealed class BundleBaker : IDisposable
    {
        private readonly AssetsManager man = new AssetsManager();
        private readonly Stream classData;
        private readonly BundleFileInstance bunInst;
        private readonly AssetsFileInstance afileInst;
        private readonly string modId;

        /// <summary>Container name -> pathId, in insertion order, for the m_Container write and its assertion.</summary>
        private readonly List<KeyValuePair<string, long>> added = new List<KeyValuePair<string, long>>();

        private long nextPathId;

        /// <summary>What was actually read, for the log - never inferred from the file name.</summary>
        internal string SourceInfo { get; }

        /// <summary>The identity actually written, for the log - never the caller's intent.</summary>
        internal string WrittenIdentity { get; private set; }

        internal BundleBaker(string sourceBundlePath, string modId)
        {
            if (!File.Exists(sourceBundlePath))
                throw new FileNotFoundException("source bundle not found", sourceBundlePath);
            this.modId = Normalize(modId);

            // A ctor that throws is never `using`-bound, so nothing would ever unload what LoadBundleFile
            // has already opened - and a shipped .bundle left mapped stays FILE-LOCKED for the rest of the
            // session, which the author sees as the game refusing to be patched for no visible reason.
            try
            {
                // FINAL-PLAN 1.6: the class database comes out of our own assembly, never off a path -
                // the bake writer has to work on a machine that has only the mod.
                classData = ContentToolMain.ClassData();
                if (classData == null)
                    throw new InvalidOperationException("classdata.tpk is missing from ContentTool.dll");
                man.LoadClassPackage(classData);

                bunInst = man.LoadBundleFile(sourceBundlePath, true);
                afileInst = man.LoadAssetsFileFromBundle(bunInst, 0, false);
                AssetsFile afile = afileInst.file;
                man.LoadClassDatabaseFromPackage(afile.Metadata.UnityVersion);

                foreach (AssetFileInfo a in afile.Metadata.AssetInfos)
                    if (a.PathId > nextPathId) nextPathId = a.PathId;
                nextPathId += 1000;

                SourceInfo = "unity=" + afile.Metadata.UnityVersion +
                             " assets=" + afile.Metadata.AssetInfos.Count +
                             " cldbTypes=" + man.ClassDatabase.Classes.Count;
            }
            catch
            {
                man.UnloadAll();
                classData?.Dispose();
                throw;
            }
        }

        /// <summary>
        /// Arbitrary bytes as a binary TextAsset - how banks and streamed WEMs ship (FINAL-PLAN 1.3).
        /// </summary>
        internal string AddTextAsset(string relativePath, byte[] payload)
        {
            if (payload == null) throw new ArgumentNullException(nameof(payload));
            // RECIPES 3: the length-prefixed raw path. m_Script.AsString corrupts bytes to U+FFFD.
            return Add(relativePath, AssetClassID.TextAsset,
                bf => bf["m_Script"].Value = new AssetTypeValue(payload, true));
        }

        /// <summary>Texture2D with its pixels inline; no .resS in v1 (FINAL-PLAN 2.2).</summary>
        internal string AddTexture2D(string relativePath, int width, int height, byte[] rgba32)
        {
            if (rgba32 == null) throw new ArgumentNullException(nameof(rgba32));
            if (rgba32.Length != width * height * 4)
                throw new ArgumentException($"RGBA32 needs {width * height * 4} B, got {rgba32.Length}", nameof(rgba32));

            return Add(relativePath, AssetClassID.Texture2D, bf => FillTexture2D(bf, width, height, rgba32));
        }

        /// <summary>
        /// Overwrites a Texture2D the SOURCE bundle already contains, found by its m_Name. This is
        /// what a shipped-asset replacement is at file level: no new object, no reference to rewrite,
        /// so nothing has to resolve our name at runtime.
        /// </summary>
        internal void ReplaceTexture2D(string assetName, int width, int height, byte[] rgba32)
        {
            if (rgba32 == null) throw new ArgumentNullException(nameof(rgba32));
            if (rgba32.Length != width * height * 4)
                throw new ArgumentException($"RGBA32 needs {width * height * 4} B, got {rgba32.Length}", nameof(rgba32));

            AssetFileInfo info = FindUnique(AssetClassID.Texture2D, assetName);
            AssetTypeValueField bf = man.GetBaseField(afileInst, info);
            FillTexture2D(bf, width, height, rgba32);
            info.SetNewData(bf);
        }

        /// <summary>Sets one float property on a Material the source bundle already contains.</summary>
        internal void ReplaceMaterialFloat(string assetName, string property, float value)
        {
            AssetFileInfo info = FindUnique(AssetClassID.Material, assetName);
            AssetTypeValueField mat = man.GetBaseField(afileInst, info);
            AssetTypeValueField arr = mat["m_SavedProperties"]["m_Floats"]["Array"];
            foreach (AssetTypeValueField p in arr.Children)
                if (p["first"].AsString == property)
                {
                    p["second"].AsFloat = value;
                    info.SetNewData(mat);
                    return;
                }
            AssetTypeValueField pair = ValueBuilder.DefaultValueFieldFromArrayTemplate(arr);
            pair["first"].AsString = property;
            pair["second"].AsFloat = value;
            arr.Children.Add(pair);
            info.SetNewData(mat);
        }

        /// <summary>
        /// Overwrites a Mesh the SOURCE bundle already contains with baked OBJ geometry - the mesh
        /// half of a shipped-asset replacement, the same shape as <see cref="ReplaceTexture2D"/>:
        /// no new object, no reference to rewrite (see <see cref="MeshFields.Fill"/> for the fields).
        /// A RIGGED target is then re-bound to its own skeleton, so the replacement deforms with the
        /// bones instead of hanging off them; on an unrigged target that call changes nothing.
        ///
        /// WHICH binding depends on what the source file could tell us, and the answer is returned so
        /// the author reads it rather than guessing: a .glb carrying an armature is bound BY BONE NAME
        /// with its own weights (<see cref="SkinFields.RebindByName"/>), and everything else - an .obj
        /// always, a .glb whose armature is somebody else's - falls back to nearest-bone
        /// (<see cref="SkinFields.Rebind"/>) and SAYS why.
        /// </summary>
        /// <param name="model">the .glb this geometry came out of, or null for an .obj.</param>
        internal string ReplaceMesh(string assetName, string sourceName, BakedMesh baked, SkinnedModel model,
                                    out string refusal, out string mapping, out bool suspect,
                                    int aliases = 0, string sidecar = null, string sidecarIgnored = null,
                                    IList<string> unusedAliases = null)
        {
            AssetFileInfo info = FindUnique(AssetClassID.Mesh, assetName);
            AssetTypeValueField mesh = man.GetBaseField(afileInst, info);
            mapping = null; suspect = false;

            // A SKINLESS SOURCE CANNOT SKIN A RIGGED TARGET, and saying so beats welding it. Checked
            // before MeshFields.Fill and long before SetNewData, so a refusal writes nothing at all and
            // the player keeps the model they had. Static targets are untouched - they have no bind
            // poses, so Rigged is false and the whole guard is skipped. WHICH ending this is comes from
            // ReplacementDecision.Decide, the same function the Model Doctor predicts with.
            bool armature = model != null && model.JointNames.Count > 0;
            bool rigged = SkinFields.Rigged(mesh);
            refusal = ReplacementDecision.Decide(armature, rigged, true, null) == Outcome.Refused
                ? SkinFields.Skinless(assetName) : null;
            if (refusal != null) return null;

            // WHICH part lands on WHICH material, said out loud: the bake preserves the file's
            // primitive order and Unity paints submesh i with m_Materials[i], so a stray part at the
            // front silently repaints everything behind it (MeshFields.SubmeshReport).
            int[] counts = baked.SubmeshIndexCounts == null || baked.SubmeshIndexCounts.Length == 0
                ? new[] { baked.IndexCount } : baked.SubmeshIndexCounts;
            var triangles = new int[counts.Length];
            for (int i = 0; i < counts.Length; i++) triangles[i] = counts[i] / 3;
            mapping = MeshFields.SubmeshReport(sourceName ?? assetName, triangles,
                                               MeshFields.MaterialNames(man, afileInst, info.PathId), out suspect);

            // BEFORE Fill, which clears every channel dimension: this is the target's own influences
            // per vertex, and writing fewer over it would DOWNGRADE its skinning (PP ships dim4 body
            // parts as well as dim2 creatures - SkinFields.InfluencesOf).
            int influences = SkinFields.InfluencesOf(mesh);

            MeshFields.Fill(mesh, baked);

            string how;
            string[] names = armature ? SkinFields.BoneNames(man, afileInst, info.PathId) : null;
            // A PARTIALLY named rig is not a name array. A slot the file could not verify comes back
            // null, RebindByName would hand SkinBinder an empty bone name, and it refuses at the door
            // with a sentence about reloading the scene - offline, where there is no scene. NoNames is
            // the honest outcome, and it is the same guard the P6 fixture uses (ProjectBake.ReversedRig).
            bool allNamed = names != null && Array.IndexOf(names, null) < 0;
            Outcome outcome = ReplacementDecision.Decide(armature, rigged, allNamed, null);
            if (outcome != Outcome.ByName)
            {
                // Rebind is still CALLED for its effect; which sentence this is comes from the outcome,
                // not from its return value - two definitions of "not rigged" is one too many.
                SkinFields.Rebind(mesh, baked, influences);
                how = outcome == Outcome.NotRigged
                    ? "not rigged - the target carries no bind poses"
                    : "nearest-bone, one full-weight influence per vertex (no SkinnedMeshRenderer in " +
                      "this bundle names ALL of the target's bones)";
            }
            else
            {
                // RebindByName throws before writing anything, so a refusal costs the mesh nothing and
                // the fallback below binds the very same geometry the strict path was handed.
                //
                // FormatException, not Exception: that is the ONLY way the binding path refuses -
                // SkinBinder.Bind throws it at every site (GlbReader.cs:2463-2544) and RebindByName's
                // own width checks do too (SkinFields.cs:739/752). A NullReference or an index error
                // out of that code is a BUG, and quietly downgrading a bug to nearest-bone is how one
                // ships.
                try
                {
                    SkinFields.RebindByName(mesh, baked, model, names, influences);
                    how = "BY NAME onto the target's own " + names.Length +
                          " bones, carrying " + Math.Max(influences, 1) +
                          " of the file's own influences per vertex";
                }
                catch (FormatException ex)
                {
                    SkinFields.Rebind(mesh, baked, influences);
                    how = "nearest-bone - the file's own weights were NOT used: " + ex.Message;
                }
            }
            // NEVER SILENT: a bone renamed by a sidecar is named in the bake log beside the binding it
            // produced, so an author never discovers a rename by its effect alone.
            if (aliases > 0 && sidecar != null)
            {
                how += " with " + aliases + " alias(es) from " + sidecar;
                // The keys that matched nothing are named here too, exactly as the live preview names
                // them: an author comparing the two must not have to wonder which one is lying.
                if (unusedAliases != null && unusedAliases.Count > 0)
                    how += ", unused: '" + string.Join("', '", unusedAliases) + "'";
            }
            // A sidecar that EXISTS and did not apply changes what the author is looking at, so it is
            // named beside the binding it did not take part in rather than left to be inferred.
            else if (sidecarIgnored != null)
                how += " (sidecar ignored: " + sidecarIgnored + ")";
            // A hash-valid sidecar whose every key names a bone this file does not have: it LOADED and
            // then did nothing, which is the one case that used to pass in silence.
            else if (sidecar != null)
                how += " (sidecar " + sidecar + " matched no bone in this file)";

            info.SetNewData(mesh);
            return how;
        }

        /// <summary>
        /// Scales one attribute's CURVES in an AnimationClip the SOURCE bundle already contains -
        /// the animation half of a shipped-asset replacement, the same shape as
        /// <see cref="ReplaceMesh"/>: the clip keeps its name, its bindings and its bank sizes, so
        /// every controller that plays it keeps playing it and nothing has to resolve our name.
        /// </summary>
        /// <returns>what was walked, for the bake log (<see cref="ClipFields.MapCurves"/>).</returns>
        internal string ReplaceClipCurves(string assetName, uint attribute, float factor)
        {
            AssetFileInfo info = FindUnique(AssetClassID.AnimationClip, assetName);
            AssetTypeValueField clip = man.GetBaseField(afileInst, info);
            string how;
            ClipFields.MapCurves(clip, attribute, v => v * factor, out how);
            info.SetNewData(clip);
            return how;
        }

        /// <summary>
        /// U4: a minimal renderable hierarchy - root GameObject+Transform, and a child GameObject
        /// carrying Transform+MeshFilter+MeshRenderer - as six new objects wired by INTERNAL PPtrs
        /// (<see cref="PrefabFields"/> holds the layout). Only the ROOT gets an m_Container entry,
        /// which is how a shipped prefab addresses: LoadAsset&lt;GameObject&gt; on the returned name
        /// hands back the root and the engine walks the rest.
        /// </summary>
        /// <param name="childName">null bakes the root alone - the no-child control arm.</param>
        /// <param name="meshAssetName">a Mesh the SOURCE bundle already holds, found by m_Name.</param>
        /// <param name="materialAssetName">a name this baker handed out (see <see cref="AddMaterial"/>).</param>
        internal string AddPrefab(string relativePath, string childName,
                                  string meshAssetName, string materialAssetName,
                                  float x, float y, float z)
        {
            string key = "assets/" + modId + "/" + Normalize(relativePath);
            foreach (KeyValuePair<string, long> e in added)
                if (e.Key == key) throw new InvalidOperationException("duplicate asset name " + key);

            long meshPathId = meshAssetName == null ? 0 : FindUnique(AssetClassID.Mesh, meshAssetName).PathId;
            long materialPathId = materialAssetName == null ? 0 : PathIdOf(materialAssetName);

            PrefabFields.Ids ids = PrefabFields.Build(
                afileInst.file, man.ClassDatabase, () => nextPathId++,
                key.Substring(key.LastIndexOf('/') + 1), childName, meshPathId, new[] { materialPathId }, x, y, z);

            added.Add(new KeyValuePair<string, long>(key, ids.RootGameObject));
            return key;
        }

        /// <summary>
        /// U5: a skinned hierarchy - root, two chained bones, and a skin GameObject whose
        /// SkinnedMeshRenderer points at a Mesh created HERE from the empty template out of
        /// <paramref name="baked"/> (<see cref="SkinFields"/> holds the layout). Like
        /// <see cref="AddPrefab"/>, only the ROOT gets an m_Container entry.
        /// </summary>
        /// <param name="splitWeights">false binds every vertex to bone0 - the no-deformation control.</param>
        /// <param name="rebind">true derives the weights with <see cref="SkinFields.Rebind"/> instead,
        /// which is the call the shipped-mesh replacement path makes.</param>
        internal string AddSkinnedPrefab(string relativePath, BakedMesh baked, string materialAssetName,
                                         float boneY, bool splitWeights, bool rebind)
        {
            string key = "assets/" + modId + "/" + Normalize(relativePath);
            foreach (KeyValuePair<string, long> e in added)
                if (e.Key == key) throw new InvalidOperationException("duplicate asset name " + key);

            SkinFields.Ids ids = SkinFields.Build(
                afileInst.file, man.ClassDatabase, () => nextPathId++,
                key.Substring(key.LastIndexOf('/') + 1), baked,
                materialAssetName == null ? 0 : PathIdOf(materialAssetName), boneY, splitWeights, rebind);

            added.Add(new KeyValuePair<string, long>(key, ids.RootGameObject));
            return key;
        }

        /// <summary>
        /// An AUTHOR'S model out of a .glb, added to this bundle as a prefab: a skinned hierarchy when
        /// the file carries an armature (<see cref="SkinFields.BuildModel"/> - the file's own bones,
        /// bind poses and per-vertex weights), and the U4 shape over a Mesh created here when it does
        /// not. Like every other prefab, only the ROOT gets an m_Container entry, so
        /// LoadAsset&lt;GameObject&gt; on the returned name hands back the whole model.
        /// </summary>
        /// <param name="controllerAssetName">a controller this baker handed out
        /// (<see cref="AddAnimatorOverrideController"/>) to put on the model root's Animator - U7's
        /// shipping shape. null bakes no Animator.</param>
        internal string AddModel(string relativePath, BakedSkin model, string materialAssetName,
                                 string controllerAssetName = null)
        {
            return AddModel(relativePath, model,
                            materialAssetName == null ? new string[0] : new[] { materialAssetName },
                            controllerAssetName);
        }

        /// <summary>
        /// The multi-material form: one material asset per submesh, in submesh order, because Unity
        /// binds submesh i to m_Materials[i]. The single-material overload above is the same call
        /// with a one-entry array, so every existing caller keeps its exact behaviour.
        /// </summary>
        internal string AddModel(string relativePath, BakedSkin model, string[] materialAssetNames,
                                 string controllerAssetName = null)
        {
            if (model == null) throw new ArgumentNullException(nameof(model));
            string key = "assets/" + modId + "/" + Normalize(relativePath);
            foreach (KeyValuePair<string, long> e in added)
                if (e.Key == key) throw new InvalidOperationException("duplicate asset name " + key);

            string rootName = key.Substring(key.LastIndexOf('/') + 1);
            if (materialAssetNames == null) materialAssetNames = new string[0];
            long[] materialPathIds = new long[materialAssetNames.Length];
            for (int i = 0; i < materialAssetNames.Length; i++)
                materialPathIds[i] = materialAssetNames[i] == null ? 0 : PathIdOf(materialAssetNames[i]);
            if (materialPathIds.Length == 0) materialPathIds = new long[] { 0 };
            AssetsFile afile = afileInst.file;
            long root;
            if (model.Rigged)
                // The rigged path takes ONE MATERIAL PER SUBMESH, exactly like the static one above.
                // It used to take only materialPathIds[0], on the reasoning that a skinned model comes
                // from a single-material export. A downloaded character does not: the humanoid this was
                // measured on ships body, clothes, eyes, teeth, lashes and hair as six materials, and a
                // one-entry m_Materials array made Unity draw submesh 0 and skip the other five - so the
                // character rendered as a bare body while its clothes were in the bundle all along.
                root = SkinFields.BuildModel(afile, man.ClassDatabase, () => nextPathId++,
                                             rootName, model, materialPathIds,
                                             controllerAssetName == null ? 0 : PathIdOf(controllerAssetName));
            else
            {
                long meshPathId = nextPathId++;
                PrefabFields.Create(afile, man.ClassDatabase, meshPathId, AssetClassID.Mesh, mesh =>
                {
                    mesh["m_Name"].AsString = SkinFields.MeshName(rootName);
                    MeshFields.Fill(mesh, model.Mesh);
                });
                root = PrefabFields.Build(afile, man.ClassDatabase, () => nextPathId++,
                                          rootName, SkinFields.SkinName(rootName),
                                          meshPathId, materialPathIds, 0f, 0f, 0f).RootGameObject;
            }

            added.Add(new KeyValuePair<string, long>(key, root));
            return key;
        }

        /// <summary>
        /// U6: a native AnimationClip driving one transform's localPosition
        /// (<see cref="ClipFields"/> holds the layout).
        /// </summary>
        /// <param name="bonePath">the driven transform's path relative to the Animator's GameObject.</param>
        internal string AddAnimationClip(string relativePath, string bonePath, float[] yPerFrame, float sampleRate)
        {
            return AddAnimationClip(relativePath, ClipFields.LiftY(bonePath, yPerFrame),
                                    yPerFrame == null ? 0 : yPerFrame.Length, sampleRate);
        }

        /// <summary>
        /// U9: the same writer over N bindings - what an IMPORTED clip needs
        /// (<see cref="ClipFields.Bindings"/> turns a <c>SampledClip</c> into them).
        /// </summary>
        internal string AddAnimationClip(string relativePath, IList<ClipFields.Binding> bindings,
                                         int frames, float sampleRate, bool loop = false)
        {
            return Add(relativePath, AssetClassID.AnimationClip,
                       bf => ClipFields.FillClip(bf, bindings, frames, sampleRate, loop));
        }

        /// <summary>
        /// U6: an AnimatorOverrideController whose BASE controller lives in another serialized file
        /// (an external PPtr, U3e's case) and whose one override hands Mecanim a clip of ours. This
        /// is the cheap half of the Mecanim question: the shipped state machine is reused verbatim,
        /// so nothing here has to serialize a ControllerConstant.
        /// </summary>
        /// <param name="baseFileId">fileID of the base controller's file - see <see cref="AddExternal"/>.</param>
        /// <param name="clipAssetName">a name this baker handed out (see <see cref="AddAnimationClip"/>).</param>
        internal string AddAnimatorOverrideController(string relativePath, int baseFileId, long basePathId,
                                                      long originalClipPathId, string clipAssetName)
        {
            long overridePathId = PathIdOf(clipAssetName);
            return Add(relativePath, AssetClassID.AnimatorOverrideController,
                       bf => ClipFields.FillOverrideController(bf, baseFileId, basePathId,
                                                               baseFileId, originalClipPathId, overridePathId));
        }

        /// <summary>
        /// U6: root GameObject+Transform+Animator with one child transform for a clip to drive.
        /// Like <see cref="AddPrefab"/>, only the ROOT gets an m_Container entry.
        /// </summary>
        /// <param name="controllerAssetName">null leaves the Animator with no controller - the arm
        /// that keeps a moved bone from being something the hierarchy does by itself.</param>
        internal string AddAnimatedPrefab(string relativePath, string boneName, float boneRestY,
                                          string controllerAssetName)
        {
            string key = "assets/" + modId + "/" + Normalize(relativePath);
            foreach (KeyValuePair<string, long> e in added)
                if (e.Key == key) throw new InvalidOperationException("duplicate asset name " + key);

            ClipFields.Ids ids = ClipFields.Build(
                afileInst.file, man.ClassDatabase, () => nextPathId++,
                key.Substring(key.LastIndexOf('/') + 1), boneName, boneRestY,
                controllerAssetName == null ? 0 : PathIdOf(controllerAssetName));

            added.Add(new KeyValuePair<string, long>(key, ids.RootGameObject));
            return key;
        }

        /// <summary>
        /// Reads a written bundle back off DISK and reports one baked clip with its override
        /// controller - the U6 oracle, and the same one the offline round trip uses
        /// (<see cref="ClipFields.Summary"/>).
        /// </summary>
        /// <param name="aocName">null asks about the CLIP alone (U7).</param>
        /// <param name="unique">`ct_list clip`'s opt-in: refuse an ambiguous clip name instead of
        /// reporting the first match. The bake's own reads leave it false and are unaffected.</param>
        internal static string ReadClipSummary(string bundlePath, string clipName, string aocName = null,
                                               bool unique = false)
        {
            return Read(bundlePath, (m, afile) =>
                ClipFields.Summary(m, afile, clipName, aocName, unique ? bundlePath : null));
        }

        /// <summary>
        /// Reads one shipped or written bundle off DISK and hands back every float of one attribute's
        /// curves in a named AnimationClip, in the order <see cref="ClipFields.MapCurves"/> walks them.
        /// The oracle for an EDITED clip: read the shipped file and the copy in the same run and the
        /// two lists line up float for float, so an edit that landed on the wrong curve cannot agree.
        /// </summary>
        internal static List<float> ReadClipCurves(string bundlePath, string clipName, uint attribute)
        {
            return Read(bundlePath, (m, afile) =>
            {
                AssetFileInfo info = AssetIndex.FindUnique(m, afile, AssetClassID.AnimationClip, clipName, bundlePath);
                string how;
                return ClipFields.MapCurves(m.GetBaseField(afile, info), attribute, v => v, out how);
            });
        }

        /// <summary>
        /// U7: the Animator a baked MODEL root carries, with its controller reported by NAME - which a
        /// PPtr resolving to nothing cannot produce. Separate from
        /// <see cref="ReadAnimatedSummary"/> because a model root has a whole rig under it, not U6's
        /// single bone.
        /// </summary>
        internal static string ReadAnimatorOn(string bundlePath, string rootName)
        {
            return Read(bundlePath, (m, afile) =>
            {
                AssetTypeValueField root = PrefabFields.FindGameObject(m, afile, rootName);
                if (root == null) return "(no GameObject '" + rootName + "')";
                AssetTypeValueField a = PrefabFields.Component(m, afile, root, AssetClassID.Animator);
                if (a == null) return "(no Animator)";
                // PrefabFields.Name already quotes the name it hands back.
                return "controller=" + PrefabFields.Name(m, afile, a["m_Controller"]["m_PathID"].AsLong) +
                       " avatar=" + a["m_Avatar"]["m_PathID"].AsLong +
                       " culling=" + a["m_CullingMode"].AsInt +
                       " hierarchy=" + a["m_HasTransformHierarchy"].AsBool;
            });
        }

        /// <summary>Reads a written bundle back off DISK and reports one baked ANIMATED hierarchy.</summary>
        internal static string ReadAnimatedSummary(string bundlePath, string rootName)
        {
            return Read(bundlePath, (m, afile) => ClipFields.HierarchySummary(m, afile, rootName));
        }

        /// <summary>Open a written bundle off disk, ask it one question, close it.</summary>
        private static T Read<T>(string bundlePath, Func<AssetsManager, AssetsFileInstance, T> ask)
        {
            AssetsManager m = new AssetsManager();
            using (Stream cldb = ContentToolMain.ClassData())
            {
                m.LoadClassPackage(cldb);
                BundleFileInstance bun = m.LoadBundleFile(bundlePath, true);
                AssetsFileInstance afile = m.LoadAssetsFileFromBundle(bun, 0, false);
                m.LoadClassDatabaseFromPackage(afile.file.Metadata.UnityVersion);
                try { return ask(m, afile); }
                finally { m.UnloadAll(); }
            }
        }

        /// <summary>
        /// Reads a written bundle back off DISK and reports one baked SKIN - the U5 oracle, and the
        /// same one the offline round trip uses (<see cref="SkinFields.Summary"/>).
        /// </summary>
        internal static string ReadSkinSummary(string bundlePath, string rootName)
        {
            return Read(bundlePath, (m, afile) => SkinFields.Summary(m, afile, rootName));
        }

        /// <summary>
        /// Reads a written bundle back off DISK and reports one baked hierarchy - the U4 oracle, and
        /// the same one the offline round trip uses (<see cref="PrefabFields.Summary"/>).
        /// </summary>
        internal static string ReadPrefabSummary(string bundlePath, string rootName)
        {
            return Read(bundlePath, (m, afile) => PrefabFields.Summary(m, afile, rootName));
        }

        /// <summary>
        /// The one asset of that class with that m_Name. Refuses BOTH nothing and more than one:
        /// aln_fireworm ships two Materials both called 'ALN_Fireworm', and picking the first would
        /// patch an arbitrary one of them quietly (FINAL-PLAN 39.2's rule - an ambiguous name is an
        /// error, never a guess).
        /// </summary>
        private AssetFileInfo FindUnique(AssetClassID cls, string assetName)
        {
            return AssetIndex.FindUnique(man, afileInst, cls, assetName, SourceInfo);
        }

        /// <summary>
        /// Why one shipped object cannot be replaced (<see cref="AssetIndex.WhyNot"/>), or null when
        /// it can - what a Replace* call would have THROWN, asked without throwing.
        /// </summary>
        internal string WhyNot(AssetClassID cls, string assetName)
        {
            return AssetIndex.WhyNot(man, afileInst, cls, assetName, SourceInfo);
        }

        /// <summary>The same, with the failure CLASSIFIED - for a caller that must tell "this bundle does
        /// not hold it" from "this bundle holds it twice" (<see cref="Addressable"/>).</summary>
        internal string WhyNot(AssetClassID cls, string assetName, out Addressable how)
        {
            return AssetIndex.WhyNot(man, afileInst, cls, assetName, SourceInfo, out how);
        }

        /// <summary>
        /// What is IN a bundle, by type and name substring - the discovery half of extraction
        /// (<see cref="AssetIndex.Report"/> holds the format). Reads a file off disk, so it works on
        /// a shipped bundle and on one we wrote, the same way.
        /// </summary>
        internal static string ListReport(string bundlePath, string typeFilter, string nameFilter, int max)
        {
            return Read(bundlePath, (m, afile) => AssetIndex.Report(m, afile, typeFilter, nameFilter, max));
        }

        /// <summary>
        /// One named Mesh out of a bundle FILE as the GLB writer's model - the geometry half of
        /// extraction. Same route as <see cref="ReadTexture"/> and for the same reason: a Mesh
        /// referenced by a renderer is not registered in m_Container either, so mounting the bundle
        /// would never hand it over. <see cref="MeshRead"/> does the reading; this only opens the file
        /// and supplies the .resS lookup.
        ///
        /// Whether the joints came out NAMED is not reported back through an out-parameter: it is read
        /// off the model with <see cref="MeshRead.NamedJoints"/>, so a caller cannot say "named" about
        /// a name array <see cref="SkinFields.BoneNames"/> or <see cref="MeshRead"/> refused.
        /// </summary>
        internal static SkinnedModel ReadMesh(string bundlePath, string assetName)
        {
            AssetsManager m = new AssetsManager();
            using (Stream cldb = ContentToolMain.ClassData())
            {
                m.LoadClassPackage(cldb);
                BundleFileInstance bun = m.LoadBundleFile(bundlePath, true);
                AssetsFileInstance afile = m.LoadAssetsFileFromBundle(bun, 0, false);
                m.LoadClassDatabaseFromPackage(afile.file.Metadata.UnityVersion);
                try
                {
                    AssetFileInfo info = AssetIndex.FindUnique(m, afile, AssetClassID.Mesh, assetName, bundlePath);
                    return MeshRead.Read(m.GetBaseField(afile, info),
                                         entry => BundleHelper.LoadAssetDataFromBundle(bun.file, entry),
                                         SkinFields.BoneNames(m, afile, info.PathId));
                }
                finally { m.UnloadAll(); }
            }
        }

        /// <summary>The serialized pixels of one Texture2D, with its .resS already resolved.</summary>
        internal sealed class RawTexture
        {
            internal int Width, Height, Format, MipCount;
            internal byte[] Data;
            /// <summary>Where the pixels came from - inline, or which archive entry at which offset.</summary>
            internal string Origin;

            internal string Describe()
            {
                return "w=" + Width + " h=" + Height + " fmt=" + Format + " mips=" + MipCount +
                       " bytes=" + (Data == null ? -1 : Data.Length) + " from=" + Origin;
            }
        }

        /// <summary>
        /// The pixels of one named Texture2D, straight off the serialized file.
        ///
        /// This is the extractor's source, and it deliberately does NOT go through
        /// AssetBundle.LoadFromFile: a shipped bundle only hands out the assets registered in
        /// m_Container, and a texture referenced by a Material is not one of them - mounting
        /// aln_fireworm and asking for its 62 textures returns ZERO. Reading the file gives every
        /// asset, and it cannot collide with a bundle the running game already holds.
        ///
        /// Most shipped textures keep their pixels in the .resS archive entry beside the serialized
        /// file rather than inline, so that indirection is resolved here.
        /// </summary>
        internal static RawTexture ReadTexture(string bundlePath, string assetName)
        {
            AssetsManager m = new AssetsManager();
            using (Stream cldb = ContentToolMain.ClassData())
            {
                m.LoadClassPackage(cldb);
                BundleFileInstance bun = m.LoadBundleFile(bundlePath, true);
                AssetsFileInstance afile = m.LoadAssetsFileFromBundle(bun, 0, false);
                m.LoadClassDatabaseFromPackage(afile.file.Metadata.UnityVersion);
                try
                {
                    AssetFileInfo info = AssetIndex.FindUnique(m, afile, AssetClassID.Texture2D, assetName, bundlePath);
                    AssetTypeValueField bf = m.GetBaseField(afile, info);
                    RawTexture t = new RawTexture
                    {
                        Width = bf["m_Width"].AsInt,
                        Height = bf["m_Height"].AsInt,
                        Format = bf["m_TextureFormat"].AsInt,
                        MipCount = Math.Max(1, bf["m_MipCount"].AsInt),
                    };

                    byte[] inline = bf["image data"].AsByteArray;
                    AssetTypeValueField sd = bf["m_StreamData"];
                    string streamPath = sd == null || sd.IsDummy ? "" : (sd["path"].AsString ?? "");

                    if (inline != null && inline.Length > 0)
                    {
                        t.Data = inline;
                        t.Origin = "inline";
                    }
                    else if (streamPath.Length > 0)
                    {
                        ulong offset = sd["offset"].AsULong;
                        uint size = sd["size"].AsUInt;
                        // "archive:/CAB-xxxx/CAB-xxxx.resS" - only the last segment names an entry.
                        string entry = streamPath.Substring(streamPath.LastIndexOf('/') + 1);
                        byte[] res = BundleHelper.LoadAssetDataFromBundle(bun.file, entry);
                        if (res == null)
                            throw new InvalidOperationException(
                                "'" + assetName + "' streams its pixels from '" + entry + "', which " +
                                Path.GetFileName(bundlePath) + " does not contain");
                        if (offset + size > (ulong)res.LongLength)
                            throw new InvalidOperationException(
                                "'" + assetName + "' claims " + size + " B at " + offset + " of '" + entry +
                                "', which is only " + res.Length + " B");
                        t.Data = new byte[size];
                        Array.Copy(res, (long)offset, t.Data, 0, size);
                        t.Origin = entry + "@" + offset + "+" + size;
                    }
                    else
                    {
                        throw new InvalidOperationException(
                            "'" + assetName + "' carries no pixels: no inline image data and no m_StreamData path");
                    }
                    return t;
                }
                finally { m.UnloadAll(); }
            }
        }

        /// <summary>
        /// The dimensions and format one named Texture2D declares IN THE FILE - the extractor's
        /// independent oracle: read straight off the serialized bytes, with no engine involved, so
        /// comparing it against a decoded PNG compares two different readers rather than one twice.
        /// </summary>
        internal static string TextureSummary(string bundlePath, string assetName)
        {
            return Read(bundlePath, (m, afile) =>
            {
                AssetFileInfo i = AssetIndex.FindUnique(m, afile, AssetClassID.Texture2D, assetName, bundlePath);
                AssetTypeValueField bf = m.GetBaseField(afile, i);
                // m_ColorSpace is reported because it is the difference between a base-colour map that
                // renders as painted and one that renders bright and flat: this game runs in LINEAR
                // (PlayerSettings.m_ActiveColorSpace = 1, MaterialFields:172-174), so an sRGB-encoded
                // map MUST declare itself sRGB or the engine skips the conversion on sample.
                AssetTypeValueField cs = bf["m_ColorSpace"];
                return "w=" + bf["m_Width"].AsInt + " h=" + bf["m_Height"].AsInt +
                       " fmt=" + bf["m_TextureFormat"].AsInt +
                       " colorSpace=" + (cs.IsDummy ? "(absent)" : cs.AsInt.ToString());
            });
        }

        /// <summary>Every Texture2D field the tool sets, in one place, so add and replace cannot drift.</summary>
        private static void FillTexture2D(AssetTypeValueField bf, int width, int height, byte[] rgba32)
        {
            {
                bf["m_Width"].AsInt = width;
                bf["m_Height"].AsInt = height;
                bf["m_TextureFormat"].AsInt = 4;    // RGBA32
                bf["m_MipCount"].AsInt = 1;
                bf["m_CompleteImageSize"].AsUInt = (uint)rgba32.Length;
                bf["m_ImageCount"].AsInt = 1;
                bf["m_TextureDimension"].AsInt = 2; // Tex2D
                bf["m_IsReadable"].AsBool = true;
                // sRGB, because the class-database default is 0 = Linear and NOTHING else ever wrote
                // this field. MEASURED both sides: every shipped Phoenix Point texture reads
                // colorSpace=1 (gate X2, 'fireworm_low_emissive'), and this tool's own bake read
                // colorSpace=0 (gate X1b, which FAILED before this line existed).
                //
                // It is not cosmetic. The game renders in LINEAR - PlayerSettings.m_ActiveColorSpace
                // = 1 in globalgamemanagers, grounded in MaterialFields:172-174 - so the engine
                // applies the sRGB->linear conversion on sample ONLY for a map that declares itself
                // sRGB. Left at 0, an ordinary sRGB-encoded PNG is uploaded as if its bytes were
                // already linear and renders BRIGHTER and FLATTER than it was painted.
                //
                // ponytail: one field on the one method both AddTexture2D and ReplaceTexture2D route
                // through. Every author-supplied .png and every .glb-embedded image is base colour,
                // so sRGB is the right answer for all of them; a normal/metallic map would want 0,
                // and this tool binds none - when it does, that map picks its own value here.
                AssetTypeValueField colorSpace = bf["m_ColorSpace"];
                if (!colorSpace.IsDummy) colorSpace.AsInt = 1;
                bf["image data"].Value = new AssetTypeValue(rgba32, false);
                bf["image data"].TemplateField.ValueType = AssetValueType.ByteArray;
                // A non-empty m_StreamData path sends the engine to a .resS that has no room for
                // these pixels, and it reads zeroes instead of failing.
                AssetTypeValueField sd = bf["m_StreamData"];
                if (!sd.IsDummy) { sd["offset"].AsULong = 0; sd["size"].AsUInt = 0; sd["path"].AsString = ""; }
            }
        }

        /// <summary>
        /// The 1-based PPtr fileID of the external whose path contains <paramref name="cabName"/>,
        /// or 0 if this file declares no such external. Read off the cloned file's own externals
        /// table so a forged reference never hardcodes an index (E3, research note 2).
        /// </summary>
        internal int ExternalIdOf(string cabName)
        {
            List<AssetsFileExternal> ext = afileInst.file.Metadata.Externals;
            for (int i = 0; i < ext.Count; i++)
                if (ext[i].PathName != null &&
                    ext[i].PathName.IndexOf(cabName, StringComparison.OrdinalIgnoreCase) >= 0) return i + 1;
            return 0;
        }

        /// <summary>
        /// Declares an external the source file does NOT already reference, and returns its 1-based
        /// PPtr fileID (gate U3e). Shape copied from the shipped bundles: lowercase
        /// `archive:/cab-x/cab-x`, all-zero GUID, type Normal. Idempotent.
        /// </summary>
        internal int AddExternal(string cabName)
        {
            int have = ExternalIdOf(cabName);
            if (have > 0) return have;
            string path = "archive:/" + cabName + "/" + cabName;
            afileInst.file.Metadata.Externals.Add(new AssetsFileExternal
            {
                Type = AssetsFileExternalType.Normal,
                PathName = path,
                OriginalPathName = path,
                // A fresh AssetsFileExternal leaves this null while every shipped one carries "",
                // and GetSize() dereferences it - so omitting it NREs the whole write, not just this
                // entry (ct_bake 13:33:34, BAKE THREW at AssetsFileExternal.GetSize).
                VirtualAssetPathName = ""
            });
            return afileInst.file.Metadata.Externals.Count;
        }

        /// <summary>
        /// A Material. With <paramref name="shaderFileId"/> 0 it has no shader of its own and
        /// production assigns one at runtime from a Phoenix Point Def donor (FINAL-PLAN 10.1);
        /// a non-zero fileID makes m_Shader an EXTERNAL PPtr into another serialized file, which is
        /// what gate U3d measures. What IS baked either way is the property block - texture
        /// references (internal PPtrs into this same bundle) and floats.
        /// </summary>
        /// <param name="textures">shader property name -&gt; asset name returned by <see cref="AddTexture2D"/>.</param>
        /// <param name="colors">shader property name -&gt; RGBA. A model with no texture has nothing
        /// BUT this: left out, the renderer draws the shader's own default, which for Standard is
        /// opaque white.</param>
        /// <param name="keywords">space-separated shader keywords, e.g. <c>_EMISSION</c>. Without it
        /// an <c>_EmissionColor</c> in <paramref name="colors"/> is stored and never read.</param>
        /// <param name="renderQueue">-1 keeps the shader's own queue, which is what every shipped
        /// material carries; a transparent or additive material overrides it.</param>
        internal string AddMaterial(string relativePath, IDictionary<string, string> textures,
                                    IDictionary<string, float> floats,
                                    int shaderFileId = 0, long shaderPathId = 0,
                                    IDictionary<string, float[]> colors = null,
                                    string keywords = null, int renderQueue = -1)
        {
            Dictionary<string, long> texIds = null;
            if (textures != null)
            {
                texIds = new Dictionary<string, long>();
                foreach (KeyValuePair<string, string> t in textures) texIds[t.Key] = PathIdOf(t.Value);
            }
            return Add(relativePath, AssetClassID.Material,
                       bf => MaterialFields.Fill(bf, shaderFileId, shaderPathId, texIds, floats, colors,
                                                 keywords, renderQueue));
        }

        /// <summary>
        /// The serialized field tree of a class, straight out of the class database this bake uses.
        /// Every remaining gate (Mesh, GameObject/Transform, SkinnedMeshRenderer, AnimationClip)
        /// starts with "what are the fields actually called in 2019.4.31f1" - this answers it from
        /// the same source the writer builds from, instead of from a remembered layout.
        /// </summary>
        internal string DumpTemplate(string className, int maxDepth)
        {
            ClassDatabaseType cls = man.ClassDatabase.FindAssetClassByName(className);
            if (cls == null) return "no class named '" + className + "' in the database";
            AssetTypeTemplateField tf = new AssetTypeTemplateField();
            tf.FromClassDatabase(man.ClassDatabase, cls, false);
            StringBuilder sb = new StringBuilder(className).Append(" (classId ").Append(cls.ClassId).Append(")");
            Walk(sb, tf, 1, maxDepth);
            return sb.ToString();
        }

        private static void Walk(StringBuilder sb, AssetTypeTemplateField f, int depth, int maxDepth)
        {
            if (depth > maxDepth || f.Children == null) return;
            foreach (AssetTypeTemplateField c in f.Children)
            {
                sb.Append('\n').Append(new string(' ', depth * 2)).Append(c.Type).Append(' ').Append(c.Name);
                Walk(sb, c, depth + 1, maxDepth);
            }
        }

        /// <summary>
        /// Renames an object the SOURCE bundle already contains, and returns its old name. The one
        /// mutation route7 needs: a value the game can read back through its own Addressables, so
        /// "the game loaded our copy" is measurable without rendering anything.
        /// </summary>
        internal string RenameAsset(long pathId, string newName)
        {
            AssetFileInfo info = afileInst.file.Metadata.GetAssetInfo(pathId);
            if (info == null) throw new InvalidOperationException("no asset with pathId " + pathId);
            AssetTypeValueField bf = man.GetBaseField(afileInst, info);
            string old = bf["m_Name"].AsString;
            bf["m_Name"].AsString = newName;
            info.SetNewData(bf);
            return old;
        }

        /// <summary>The pathId behind an asset name this baker handed out; throws for anything else.</summary>
        internal long PathIdOf(string assetName)
        {
            foreach (KeyValuePair<string, long> e in added) if (e.Key == assetName) return e.Value;
            throw new InvalidOperationException("no asset named " + assetName + " in this bake");
        }

        /// <summary>
        /// Reads a written bundle back off DISK and reports one Material's property block, so a
        /// check can see the serialized reference itself. Needed because a Material with no shader
        /// exposes no properties through the engine API - the data is there, but Material.GetTexture
        /// has no property sheet to look it up in.
        /// </summary>
        /// <remarks>
        /// Resolved through <see cref="AssetIndex.FindUnique"/>: aln_fireworm ships TWO Materials
        /// called 'ALN_Fireworm', and the walk this used to do returned an arbitrary one of them
        /// quietly - which, now that ct_list props prints this to an author, would be a lie about
        /// which material they are looking at.
        /// </remarks>
        internal static string ReadMaterialProperties(string bundlePath, string materialName)
        {
            return Read(bundlePath, (m, afile) => MaterialFields.Summary(m.GetBaseField(afile,
                AssetIndex.FindUnique(m, afile, AssetClassID.Material, materialName, bundlePath))));
        }

        /// <summary>
        /// Reads a written bundle back off DISK and reports one Mesh's geometry - the P4 oracle, and
        /// the same one the offline round trip uses (<see cref="MeshFields.Summary"/>). Off the file,
        /// not through the engine: a shipped mesh is not CPU-readable, so Mesh.vertices would throw.
        /// </summary>
        /// <param name="skin">
        /// true reports the SKIN instead of the geometry (<see cref="SkinFields.SkinSummary"/>) - the
        /// P5 oracle. Same walk, so both answers come off the same file the same way.
        /// </param>
        internal static string ReadMeshSummary(string bundlePath, string meshName, bool skin = false)
        {
            string buffers;
            return ReadMeshSummary(bundlePath, meshName, skin, out buffers);
        }

        /// <summary>
        /// The same walk, and ONE decompress: <paramref name="buffers"/> comes back as
        /// <see cref="MeshFields.Buffers"/> of the SAME Mesh field the summary was read off - the raw
        /// vertex + index bytes as a hash, which the SUMMARY deliberately cannot be (a patch that wrote
        /// nothing summarises exactly like the mesh the game shipped, and on an unskinned target no
        /// other arm notices). Null when the mesh is not there, or when its buffers are not readable at
        /// all - the caller then has no answer to compare and must say VOID.
        ///
        /// Folded into this reader because every caller wants both: asking separately reopened and
        /// re-inflated the whole bundle a second time per mesh row.
        /// </summary>
        internal static string ReadMeshSummary(string bundlePath, string meshName, bool skin,
                                               out string buffers)
        {
            buffers = null;
            AssetsManager m = new AssetsManager();
            using (Stream cldb = ContentToolMain.ClassData())
            {
                m.LoadClassPackage(cldb);
                BundleFileInstance bun = m.LoadBundleFile(bundlePath, true);
                AssetsFileInstance afile = m.LoadAssetsFileFromBundle(bun, 0, false);
                m.LoadClassDatabaseFromPackage(afile.file.Metadata.UnityVersion);
                try
                {
                    foreach (AssetFileInfo i in afile.file.Metadata.GetAssetsOfType(AssetClassID.Mesh))
                    {
                        AssetTypeValueField mesh = m.GetBaseField(afile, i);
                        if (mesh["m_Name"].AsString != meshName) continue;
                        buffers = MeshFields.Buffers(mesh);
                        // A tree with NO vertex data, NO index buffer and NO stream path is not a Mesh
                        // this build can read - and Summary/SkinSummary index exactly those absent
                        // fields, where a dummy THROWS. Buffers already answers null (= VOID) for it;
                        // the summary has to say so too rather than take the gate down with an NRE.
                        if (buffers == null)
                            return "unreadable Mesh " + meshName + " in " + bundlePath;
                        return skin ? SkinFields.SkinSummary(mesh) : MeshFields.Summary(mesh);
                    }
                    return "no Mesh named " + meshName + " in " + bundlePath;
                }
                finally { m.UnloadAll(); }
            }
        }

        /// <summary>
        /// Every vertex's influences of a Mesh in a bundle FILE (<see cref="SkinFields.SkinInfluences"/>)
        /// - the P6 oracle, which needs WHICH bone each vertex went to and not just that one exists.
        /// </summary>
        internal static string ReadSkinInfluences(string bundlePath, string meshName)
        {
            return Read(bundlePath, (m, afile) =>
            {
                foreach (AssetFileInfo i in afile.file.Metadata.GetAssetsOfType(AssetClassID.Mesh))
                {
                    AssetTypeValueField mesh = m.GetBaseField(afile, i);
                    if (mesh["m_Name"].AsString == meshName) return SkinFields.SkinInfluences(mesh);
                }
                return "no Mesh named " + meshName + " in " + bundlePath;
            });
        }

        /// <summary>
        /// The influences per vertex a Mesh in a bundle FILE declares
        /// (<see cref="SkinFields.InfluencesOf"/>) - what a replacement of it must not narrow, and so
        /// what the P5/P6 arms have to predict at. 0 when there is no such mesh.
        /// </summary>
        internal static int ReadInfluenceCount(string bundlePath, string meshName)
        {
            int found = 0;
            Read(bundlePath, (m, afile) =>
            {
                foreach (AssetFileInfo i in afile.file.Metadata.GetAssetsOfType(AssetClassID.Mesh))
                {
                    AssetTypeValueField mesh = m.GetBaseField(afile, i);
                    if (mesh["m_Name"].AsString == meshName) found = SkinFields.InfluencesOf(mesh);
                }
                return "";
            });
            return found;
        }

        /// <summary>
        /// The NAMES of the bones a shipped Mesh is skinned to, in the order its bind poses are in -
        /// what <see cref="SkinFields.RebindByName"/> matches a file's joints against.
        ///
        /// The mesh itself does not carry them: it carries CRC-32 hashes of bone PATHS, which cannot
        /// be inverted. The names live on the SkinnedMeshRenderer that USES the mesh, whose m_Bones
        /// list is index-for-index with m_BindPose - so this finds that renderer by the PPtr it holds
        /// and reads each bone's GameObject name. null when no renderer in this bundle uses the mesh,
        /// or when two do and disagree about the skeleton: an ambiguity is refused, never guessed
        /// (the same rule as <see cref="AssetIndex.FindUnique"/>, which resolves the MESH here for
        /// the same reason - aln_fireworm ships two called 'ALN_Fireworm', and the walk this used to
        /// do took whichever came first, so ct_list bones would have printed one of two skeletons
        /// without saying which).
        /// </summary>
        internal static string[] ReadBoneNames(string bundlePath, string meshName)
        {
            string refusal;
            return ReadBoneNames(bundlePath, meshName, out refusal);
        }

        /// <param name="refusal">
        /// why the names were REFUSED rather than merely absent - see
        /// <see cref="SkinFields.BoneNames(AssetsManager,AssetsFileInstance,long,out string)"/>.
        /// </param>
        internal static string[] ReadBoneNames(string bundlePath, string meshName, out string refusal)
        {
            string[] found = null;
            string why = null;
            Read(bundlePath, (m, afile) =>
            {
                found = SkinFields.BoneNames(m, afile,
                    AssetIndex.FindUnique(m, afile, AssetClassID.Mesh, meshName, bundlePath).PathId, out why);
                return "";
            });
            refusal = why;
            return found;
        }

        /// <summary>
        /// Does any Texture2D in this bundle FILE carry exactly these pixels? Read off the file, not
        /// through the engine, so the patched copy and the untouched shipped original can be compared
        /// the same way without Unity ever opening either.
        /// </summary>
        internal static bool HasTexturePixels(string bundlePath, int width, int height, byte[] rgba32)
        {
            AssetsManager m = new AssetsManager();
            using (Stream cldb = ContentToolMain.ClassData())
            {
                m.LoadClassPackage(cldb);
                BundleFileInstance bun = m.LoadBundleFile(bundlePath, true);
                AssetsFileInstance afile = m.LoadAssetsFileFromBundle(bun, 0, false);
                m.LoadClassDatabaseFromPackage(afile.file.Metadata.UnityVersion);
                try
                {
                    foreach (AssetFileInfo i in afile.file.Metadata.GetAssetsOfType(AssetClassID.Texture2D))
                    {
                        AssetTypeValueField bf = m.GetBaseField(afile, i);
                        if (bf["m_Width"].AsInt != width || bf["m_Height"].AsInt != height) continue;
                        byte[] got = bf["image data"].AsByteArray;
                        if (got == null || got.Length != rgba32.Length) continue;
                        bool same = true;
                        for (int k = 0; k < got.Length; k++) if (got[k] != rgba32[k]) { same = false; break; }
                        if (same) return true;
                    }
                    return false;
                }
                finally { m.UnloadAll(); }
            }
        }

        /// <summary>
        /// Writes the bundle. <paramref name="bundleName"/> renames the serialized file and its CAB
        /// entry: that pair is the identity Unity refuses a duplicate on, so without a fresh name an
        /// already-loaded copy can masquerade as a successful load (METHODOLOGY, test hygiene).
        /// Pass null to KEEP the source identity - route7 writes a stand-in for a shipped bundle and
        /// must not change the name anything else might resolve.
        /// </summary>
        internal void Write(string outPath, string bundleName)
        {
            AssetsFile afile = afileInst.file;

            AssetFileInfo abInfo = null;
            foreach (AssetFileInfo i in afile.Metadata.GetAssetsOfType(AssetClassID.AssetBundle)) { abInfo = i; break; }
            if (abInfo == null) throw new InvalidOperationException("source bundle contains no AssetBundle asset");

            AssetTypeValueField ab = man.GetBaseField(afileInst, abInfo);
            AssetTypeValueField container = ab["m_Container"]["Array"];
            foreach (KeyValuePair<string, long> e in added) AddContainerEntry(container, e.Key, e.Value);

            // FINAL-PLAN 2.3, mandatory: every name we hand back to a caller (and therefore every
            // name that ends up as a constant in a generated loader) must resolve through
            // m_Container. A name that is not in here makes LoadAsset<T> return null on every user's
            // machine, with nothing logged - so it is checked at bake time instead.
            foreach (KeyValuePair<string, long> e in added)
                if (!ContainerHas(container, e.Key))
                    throw new InvalidOperationException("m_Container is missing the emitted asset name " + e.Key);

            if (bundleName != null)
            {
                ab["m_Name"].AsString = bundleName;
                if (!ab["m_AssetBundleName"].IsDummy) ab["m_AssetBundleName"].AsString = bundleName;
            }
            abInfo.SetNewData(ab);

            // Only entry 0 is renamed. The .resS entry keeps its name: every shipped m_StreamData
            // path points at it, and renaming it would blank every streamed texture in the bundle.
            List<AssetBundleDirectoryInfo> dirs = bunInst.file.BlockAndDirInfo.DirectoryInfos;
            if (bundleName != null) dirs[0].Name = "CAB-" + bundleName;
            dirs[0].SetNewData(afile);
            WrittenIdentity = ab["m_Name"].AsString + " / " + dirs[0].Name;

            // Pack with the SOURCE's own compression. Measured 2026-08-12: writing mutoid uncompressed
            // gives 266 623 B from a 175 599 B LZ4 source (1.52x), while repacking LZ4 gives 175 838 B
            // (1.00x) - which is what makes a private copy of a 553 MB bundle affordable. Pack reads
            // from a written bundle, so the mutated one goes through a MemoryStream first.
            AssetBundleCompressionType comp = bunInst.file.GetCompressionType();
            using (MemoryStream raw = new MemoryStream())
            {
                using (AssetsFileWriter rw = new AssetsFileWriter(raw))
                {
                    bunInst.file.Write(rw);
                    rw.Flush();
                    raw.Position = 0;
                    if (comp == AssetBundleCompressionType.None)
                    {
                        using (FileStream f = File.Create(outPath)) raw.CopyTo(f);
                    }
                    else
                    {
                        AssetBundleFile packed = new AssetBundleFile();
                        packed.Read(new AssetsFileReader(raw));
                        using (AssetsFileWriter w = new AssetsFileWriter(outPath))
                            packed.Pack(w, comp, false, null);
                        packed.Close();
                    }
                }
            }
        }

        public void Dispose()
        {
            man.UnloadAll();
            classData?.Dispose();
        }

        // ---------------------------------------------------------------- internals

        private string Add(string relativePath, AssetClassID cls, Action<AssetTypeValueField> fill)
        {
            string key = "assets/" + modId + "/" + Normalize(relativePath);
            foreach (KeyValuePair<string, long> e in added)
                if (e.Key == key) throw new InvalidOperationException("duplicate asset name " + key);

            AssetsFile afile = afileInst.file;
            long pathId = nextPathId++;

            // RECIPES 2: AssetFileInfo.Create registers the TypeTreeType and MUST run before the
            // value field is built. The returned instance is the one to fill - AssetsManager's
            // CreateValueBaseField NREs for a freshly registered type.
            AssetFileInfo info = AssetFileInfo.Create(afile, pathId, (int)cls, man.ClassDatabase, false);
            AssetTypeTemplateField tf = new AssetTypeTemplateField();
            tf.FromClassDatabase(man.ClassDatabase, man.ClassDatabase.FindAssetClassByID((int)cls), false);
            AssetTypeValueField bf = ValueBuilder.DefaultValueFieldFromTemplate(tf);

            bf["m_Name"].AsString = key.Substring(key.LastIndexOf('/') + 1);
            fill(bf);
            info.SetNewData(bf);
            afile.Metadata.AddAssetInfo(info);

            added.Add(new KeyValuePair<string, long>(key, pathId));
            return key;
        }

        private static void AddContainerEntry(AssetTypeValueField container, string key, long pathId)
        {
            AssetTypeValueField pair = ValueBuilder.DefaultValueFieldFromArrayTemplate(container);
            pair["first"].AsString = key;
            AssetTypeValueField second = pair["second"];
            second["preloadIndex"].AsInt = 0;   // directly contained: no preload table slice
            second["preloadSize"].AsInt = 0;
            second["asset"]["m_FileID"].AsInt = 0;
            second["asset"]["m_PathID"].AsLong = pathId;
            container.Children.Add(pair);
        }

        private static bool ContainerHas(AssetTypeValueField container, string key)
        {
            foreach (AssetTypeValueField pair in container.Children)
                if (pair["first"].AsString == key) return true;
            return false;
        }

        /// <summary>Container names are lowercase, forward-slashed, and have no leading/trailing slash.</summary>
        private static string Normalize(string s)
        {
            if (string.IsNullOrEmpty(s)) throw new ArgumentException("empty path segment");
            return s.Replace('\\', '/').Trim('/').ToLowerInvariant();
        }
    }
}
