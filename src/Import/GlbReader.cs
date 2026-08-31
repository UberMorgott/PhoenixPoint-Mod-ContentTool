using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

using Morgott.ContentTool.Bake;

namespace Morgott.ContentTool.Import
{
    /// <summary>
    /// Hand-written glTF 2.0 binary READER, the inverse of <see cref="GlbCodec"/>. No library: the
    /// loader does <c>Assembly.Load(byte[])</c> and never resolves a sibling DLL, so the mod is one
    /// merged assembly and a glTF package is not an option.
    ///
    /// This is a TRUST BOUNDARY. The bytes come from a folder any player can drop anything into, so
    /// every offset, length, count and index read out of the file is range-checked against the data
    /// actually present BEFORE it is used, and nothing is allocated to a size the file merely claims.
    /// Failure is always a <see cref="FormatException"/> whose message names the cause and the fix;
    /// it never escapes as some other exception type into the game loop.
    ///
    /// The coordinate conversion is not re-derived here. Every rule <see cref="GlbCodec"/> applies is
    /// its own inverse — S = diag(-1,1,1) with S*S = I, the V mirror 1-(1-v) = v, S*(S*M*S)*S = M, and
    /// swapping a triangle's last two indices twice — so the reader calls the very same functions.
    /// That is why a self-check must ALSO assert the reader against hand-built glTF-space bytes: a
    /// round trip through one shared involution would survive both halves being wrong together.
    /// </summary>
    internal sealed class GlbReader
    {
        /// <summary>A whole rigged character is single-digit megabytes; this is the hostile-input ceiling.</summary>
        internal const int MaxFileBytes = 128 * 1024 * 1024;
        private const int MaxJsonBytes = 16 * 1024 * 1024;
        private const int MaxDepth = 64;
        // internal, not private: Draco sizes its point arrays from a count a hostile file states in
        // five bytes, and those points ARE these vertices - one ceiling, checked in both places.
        internal const int MaxVertices = 1000000;
        private const int MaxIndices = 6000000;
        private const int MaxSubmeshes = 256;
        private const int MaxMorphs = 256;
        private const int MaxJoints = 65535;          // glTF stores a joint index in an unsigned short
        private const int MaxCollection = 65536;      // accessors, bufferViews, nodes, primitives
        private const float MaxCoordinate = 100000f;
        private const int MaxClips = 512;
        private const int MaxFrames = 10000;          // 100 s at the highest rate below
        private const int MinRate = 1, MaxRate = 120;
        /// <summary>How near a frame a key has to sit to count as ON it - in FRAMES. One number, used
        /// both to pick the rate and to decide how many frames reach the last key, so the two cannot
        /// disagree about whether a clip is snapped.</summary>
        private const double GridTolerance = 0.01;
        /// <summary>Compressed bufferViews are expanded into memory, so they get a ceiling of their own.</summary>
        private const int MaxDecodedBytes = 64 * 1024 * 1024;

        /// <summary>
        /// Widens the component types an attribute may use (a POSITION as SHORT, a NORMAL as BYTE).
        /// It adds no decode step of its own - the file states its dequantization through mechanisms
        /// glTF already has, and this reader already honours ALL of them, which is why supporting it is
        /// a matter of not refusing it rather than a decode step:
        ///  - the integer forms themselves are read by <see cref="Value"/>, whose normalized divisors
        ///    are the extension's own "Decoding Quantized Data" table;
        ///  - a STATIC mesh states its dequantization in the mesh node's transform, which
        ///    <see cref="Bake"/> already folds into the vertices;
        ///  - a SKINNED mesh states it in the skin's inverseBindMatrices, which
        ///    <see cref="ModelBuild.Invert"/> already treats as the authority for where a bone sits,
        ///    so the vertices legitimately stay in quantized space and the bind poses carry the scale;
        ///  - a TEXCOORD would state it through KHR_texture_transform, the one case with nowhere to go
        ///    (this mod paints from Meshes\materials\ and reads no glTF material), refused by name in
        ///    <see cref="Unreadable"/>.
        /// </summary>
        private const string Quantization = "KHR_mesh_quantization";
        /// <summary>The one texture-side extension that also changes how a TEXCOORD is read.</summary>
        private const string TextureTransform = "KHR_texture_transform";
        /// <summary>Used only when no rate in [MinRate, MaxRate] lands on the file's own key times.</summary>
        private const float FallbackRate = 30f;

        private readonly Dictionary<string, object> root;
        private readonly byte[] bin;

        /// <summary>
        /// The EXPANDED bytes of every compressed bufferView touched so far, keyed by view index, and
        /// the running total they cost. Decoding is lazy and cached: one clip's 37 channels come off a
        /// handful of views, and a file's 278 accessors would otherwise decode the same view hundreds
        /// of times.
        /// </summary>
        private readonly Dictionary<int, byte[]> expanded = new Dictionary<int, byte[]>();
        private int expandedBytes;

        /// <summary>
        /// The DECODED values of every accessor a KHR_draco_mesh_compression primitive supplies, keyed
        /// by accessor index. Draco does not compress a bufferView the way meshopt does - it replaces
        /// the accessors outright ("you must ignore the bufferView and byteOffset of the accessor and
        /// go to the previously decoded Draco geometry", the extension's Conformance section) - so it
        /// lands one level up from <see cref="Expand"/>, at <see cref="Elements"/>, and every guard
        /// there still reads the accessor's own declared type, count and componentType.
        /// </summary>
        private readonly Dictionary<int, float[]> decoded = new Dictionary<int, float[]>();

        /// <summary>
        /// The glTF NODE index of every joint slot, filled by <see cref="ReadSkin"/> and null while the
        /// file has no armature. An animation channel names a NODE; a clip binds a BONE PATH, and the
        /// bone paths the bake hashes are the joint SLOTS - so this array is the whole join between the
        /// two, and reading it a second way is exactly how a curve ends up driving the wrong bone.
        /// </summary>
        private int[] jointNode;

        /// <summary>
        /// The glTF-space global transform of everything ABOVE the armature's root bone, or null until
        /// <see cref="Model"/> has read a skin. glTF 2.0 §3.7.4.3 states the skinning matrix as
        /// <c>jointMatrix = globalTransformOfJointNode * inverseBindMatrix</c>, and "global" means the
        /// whole parent chain up to the SCENE root - not the chain below the skin's own root joint. An
        /// exporter is free to park the file's Z-up -&gt; Y-up conversion and its unit scale on that
        /// chain (Blender's armature OBJECT does exactly this: spider.glb's node 1 'SpiderArmature'
        /// carries -90 deg about X and a scale of 100 above the skin's root joint), so dropping it
        /// imports a conformant file lying on its back at the wrong size. <see cref="Carry"/> folds it
        /// into the model instead; <see cref="Animation"/> reads it to refuse the one case it cannot
        /// carry honestly.
        /// </summary>
        private float[] above;

        private GlbReader(Dictionary<string, object> root, byte[] bin)
        {
            this.root = root;
            this.bin = bin;
        }

        /// <summary>
        /// One mesh out of one .glb, in UNITY space. An empty <see cref="SkinnedModel.JointNames"/>
        /// means the file carries no skin, which is the static replacement path.
        /// </summary>
        internal static SkinnedModel Read(byte[] file)
        {
            return Read(file, null);
        }

        /// <summary>
        /// The same read, plus the file's own animation clips appended to <paramref name="clips"/> -
        /// RESAMPLED onto a uniform grid and converted to Unity space, which is what a serialized
        /// AnimationClip's dense bank wants (<c>ClipFields.FillClip</c>). Each
        /// <see cref="SampledTrack.Node"/> is a JOINT SLOT, so a track's bone path is
        /// <c>BakedSkin.BonePath(track.Node)</c> and the CRC the clip binds is the one the mesh already
        /// carries in m_BoneNameHashes. A file with no "animations" leaves the list untouched.
        /// </summary>
        internal static SkinnedModel Read(byte[] file, List<SampledClip> clips)
        {
            if (file == null || file.Length < 12)
                throw Bad("the file is shorter than a glTF header, so it holds no model; re-export it from Blender as glTF Binary (.glb)");
            if (file.Length > MaxFileBytes)
                throw Bad("the file is " + Mb(file.Length) + ", past the " + Mb(MaxFileBytes) +
                    " limit this mod will read; decimate the mesh or drop its blend shapes and re-export");
            if (U32(file, 0) != Gltf.Magic)
                throw Bad("the file does not start with the glTF magic, so it is not a .glb; in Blender export with Format set to 'glTF Binary (.glb)'");
            uint version = U32(file, 4);
            if (version != 2)
                throw Bad("the file declares glTF container version " + version.ToString(CultureInfo.InvariantCulture) +
                    "; only version 2 is supported, so re-export from a current Blender");
            long declared = U32(file, 8);
            if (declared > file.Length)
                throw Bad("the file declares " + declared.ToString(CultureInfo.InvariantCulture) + " bytes but only " +
                    file.Length.ToString(CultureInfo.InvariantCulture) + " are present, so it is truncated; copy the whole file again");
            if (declared < file.Length)
                throw Bad("the file declares " + declared.ToString(CultureInfo.InvariantCulture) + " bytes but " +
                    file.Length.ToString(CultureInfo.InvariantCulture) + " are present, so it carries trailing data; re-export it rather than editing the bytes");

            string text = null;
            byte[] binary = null;
            int at = 12;
            while (at + 8 <= declared)
            {
                long length = U32(file, at);
                uint type = U32(file, at + 4);
                if ((length & 3) != 0)
                    throw Bad("a chunk length of " + length.ToString(CultureInfo.InvariantCulture) +
                        " is not a multiple of four, which glTF requires; re-export the file rather than editing the bytes");
                if (length > declared - at - 8)
                    throw Bad("a chunk claims " + length.ToString(CultureInfo.InvariantCulture) +
                        " bytes but only " + (declared - at - 8).ToString(CultureInfo.InvariantCulture) +
                        " remain, so the file is corrupt; copy or re-export it again");
                if (type == Gltf.JsonChunk)
                {
                    if (text != null) throw Bad("the file carries two JSON chunks; re-export it rather than editing the bytes");
                    if (at != 12) throw Bad("the file's first chunk is not JSON, which glTF requires; re-export it from Blender");
                    if (length > MaxJsonBytes)
                        throw Bad("the file's JSON chunk is " + Mb(length) + ", past the " + Mb(MaxJsonBytes) +
                            " limit; simplify the scene and re-export");
                    text = Encoding.UTF8.GetString(file, at + 8, (int)length);
                }
                else if (type == Gltf.BinChunk)
                {
                    if (binary != null) throw Bad("the file carries two BIN chunks; re-export it rather than editing the bytes");
                    binary = new byte[length];
                    Array.Copy(file, at + 8, binary, 0, (int)length);
                }
                at += (int)(8 + length);
            }
            if (at != declared)
                throw Bad("the file's chunks stop " + (declared - at).ToString(CultureInfo.InvariantCulture) +
                    " bytes short of its declared length, so it is corrupt; copy or re-export it again");
            if (text == null) throw Bad("the file has no JSON chunk, so it describes nothing; re-export it from Blender");

            object parsed = Json.Parse(text, MaxDepth);
            var document = parsed as Dictionary<string, object>;
            if (document == null) throw Bad("the file's JSON chunk is not an object; re-export it rather than editing it by hand");
            var reader = new GlbReader(document, binary ?? new byte[0]);
            SkinnedModel model = reader.Model();
            if (clips != null) reader.Animations(model, clips);
            return model;
        }

        // ---------------------------------------------------------------- document

        private SkinnedModel Model()
        {
            Dictionary<string, object> asset = Obj(Get(root, "asset"), "asset");
            string version = Str(Get(asset, "version"), "asset.version");
            if (!version.StartsWith("2.", StringComparison.Ordinal) && version != "2")
                throw Bad("the file declares glTF version '" + version + "'; only glTF 2.0 is supported, so re-export from a current Blender");

            List<object> required = Opt(root, "extensionsRequired") as List<object>;
            if (required != null)
                for (int i = 0; i < required.Count; i++)
                {
                    string name = Str(required[i], "extensionsRequired[" + i + "]");
                    if (name != Quantization && name != Meshopt.Extension && name != Draco.Extension)
                        throw Unreadable(name);
                }

            List<object> buffers = Array_(Opt(root, "buffers"), "buffers");
            if (buffers.Count == 0) throw Bad("the file declares no buffer, so it holds no geometry; re-export it from Blender");
            Dictionary<string, object> buffer = Obj(buffers[0], "buffers[0]");
            object uri = Opt(buffer, "uri");
            if (uri != null)
                throw Bad("the file's geometry lives in a separate file '" + Str(uri, "buffers[0].uri") +
                    "'; in Blender set Format to 'glTF Binary (.glb)' so the geometry travels inside the one file you copy");
            int bufferLength = Int(Get(buffer, "byteLength"), "buffers[0].byteLength");
            if (bufferLength > bin.Length)
                throw Bad("the file's buffer claims " + bufferLength.ToString(CultureInfo.InvariantCulture) + " bytes but its BIN chunk holds " +
                    bin.Length.ToString(CultureInfo.InvariantCulture) + "; copy or re-export the file again");

            List<object> meshes = Array_(Opt(root, "meshes"), "meshes");
            if (meshes.Count == 0)
                throw Bad("the file contains no mesh; export the edited body mesh itself, not an empty or armature-only scene");
            List<object> nodes = Array_(Opt(root, "nodes"), "nodes");
            if (nodes.Count > MaxCollection)
                throw Bad("the file declares " + nodes.Count.ToString(CultureInfo.InvariantCulture) + " nodes, past the " +
                    MaxCollection.ToString(CultureInfo.InvariantCulture) + " limit; simplify the scene and re-export");

            // WHICH MESH IS THE MODEL, when a file carries more than one.
            //
            // "Exactly one mesh" was the right rule with one wrong edge. A creature exported from a
            // model site routinely arrives with the creature PLUS whatever furniture the scene had -
            // MEASURED on 'Cyborg Spider' (Sketchfab): mesh[0] 'Spider_Spider_M_0', 1226 verts / 1552
            // tris, driven by the skin; mesh[1] 'pPlane1_Camera_lambert2_0', FOUR verts and TWO
            // triangles, no skin, the exporter's camera backdrop. Refusing that file taught the author
            // to go and join a backdrop onto their creature, which is worse than useless advice.
            //
            // The skin is what makes the choice exact rather than a guess: a rig drives the creature
            // and nothing else, so "the mesh a skin drives" IS the definition of the model here. When
            // that does not single one out - two skinned meshes, or a static file with several - there
            // is nothing to pick on and the original refusal stands, wording unchanged.
            //
            // ponytail: no merging. Merging two skinned meshes needs a shared bone list and a vertex
            // rebase; if a file ever genuinely needs it, that is a Blender job and the message says so.
            int want = 0;
            List<string> dropped = new List<string>();
            if (meshes.Count > 1)
            {
                List<int> skinned = new List<int>();
                for (int i = 0; i < nodes.Count; i++)
                {
                    var obj = Obj(nodes[i], "nodes[" + i + "]");
                    object which = Opt(obj, "mesh");
                    if (which == null || Opt(obj, "skin") == null) continue;
                    int m = Int(which, "nodes[" + i + "].mesh");
                    if (!skinned.Contains(m)) skinned.Add(m);
                }
                // NO ARMATURE AT ALL is not a broken file - it is a PROP. A gun has no rig and never
                // should have one, so "the mesh a skin drives" has no answer here and asking the
                // author to go and join meshes in Blender was the tool refusing its own job. Merge
                // it: one mesh whose submeshes are its distinct materials (MeshMerge.Static).
                // MEASURED on two real downloads - ar181.glb (demos\WeaponAdd\Content\Models\) is
                // 14 meshes over 3 materials, and a tau_pulse_pistol.glb was 9 over 1 before it was
                // deleted from the repository for licence reasons.
                if (skinned.Count == 0) return StaticMerge(root, meshes, nodes);
                if (skinned.Count != 1)
                    throw Bad("the file contains " + meshes.Count.ToString(CultureInfo.InvariantCulture) +
                        " meshes and an armature drives " + skinned.Count + " of them" +
                        ", so nothing says which one is the model; in Blender select the pieces and join " +
                        "them (Ctrl+J) before exporting");
                want = skinned[0];
                for (int m = 0; m < meshes.Count; m++)
                    if (m != want)
                        dropped.Add(Opt(Obj(meshes[m], "meshes[" + m + "]"), "name") as string ??
                                    ("meshes[" + m.ToString(CultureInfo.InvariantCulture) + "]"));
            }

            int meshNode = -1;
            for (int i = 0; i < nodes.Count; i++)
            {
                object which = Opt(Obj(nodes[i], "nodes[" + i + "]"), "mesh");
                if (which == null) continue;
                if (Int(which, "nodes[" + i + "].mesh") != want) continue;
                if (meshNode >= 0)
                    throw Bad("the file places its mesh under two objects at once, so the game cannot tell which transform to use; " +
                        "keep one object with the mesh and re-export");
                meshNode = i;
            }

            int skin = -1;
            if (meshNode >= 0)
            {
                object which = Opt(Obj(nodes[meshNode], "meshNode"), "skin");
                if (which != null) skin = Int(which, "nodes[" + meshNode + "].skin");
            }

            var model = new SkinnedModel { Name = Name(Obj(meshes[want], "meshes[" + want + "]"), meshNode < 0 ? null : Obj(nodes[meshNode], "meshNode")) };
            model.DroppedMeshes.AddRange(dropped);
            // Counted, and all of it applied EXCEPT baseColorFactor (see ReadPrimitives). Paint on the
            // REPLACE path has exactly one route — Meshes\materials\<name>.mat.json — because a glTF
            // material is a lossy PBR projection of whatever shader the game really uses, and two
            // routes that can disagree is worse than one route stated plainly. The ADD path has no
            // sidecar route at all, so there the file's own base colour is the only route there is.
            model.IgnoredMaterials = (Opt(root, "materials") as List<object>)?.Count ?? 0;
            model.IgnoredImages = (Opt(root, "images") as List<object>)?.Count ?? 0;
            ReadPrimitives(Obj(meshes[want], "meshes[" + want + "]"), model, skin >= 0);
            // The single-mesh path gets its material names and embedded images too, and that is what
            // paints the SPIDER: a creature .glb carries its own texture inside the file, and this
            // reader used to ignore it, so the imported model rendered pure white.
            PrimitiveMaterials(Obj(meshes[want], "meshes[" + want + "]"), Opt(root, "materials") as List<object>, model);
            if (skin >= 0) ReadSkin(skin, model);
            else if (model.Joints != null)
                throw Bad("the mesh carries bone weights but no armature, so nothing says which bone each weight means; " +
                    "in Blender's export panel keep 'Skinning' on and include the armature, then re-export");
            if (skin >= 0) Carry(model, above = Above(nodes, model));
            ToUnity(model, meshNode < 0 || skin >= 0 ? null : World(nodes, meshNode));
            if (skin >= 0) RestLocals(model);
            return model;
        }

        /// <summary>
        /// A STATIC model that arrived as several meshes, merged into one.
        ///
        /// EACH PIECE IS TAKEN TO WORLD SPACE FIRST, and that is the part that would silently ruin
        /// the model if it were skipped: every mesh sits under its own node with its own transform,
        /// so merging the raw vertex buffers would stack fourteen pieces on top of each other at the
        /// origin. <see cref="ToUnity"/> with the node's world matrix is the same call the
        /// single-mesh path already makes; it just has to happen per piece, before the merge.
        ///
        /// A mesh referenced by two nodes is read TWICE, once per node, which is what instancing
        /// means and what the author sees in their own viewport.
        ///
        /// The material NAME is what groups the submeshes, so it is read here: the import path never
        /// filled <c>Materials</c> before, and without it every piece would fall back to its own mesh
        /// name and produce fourteen submeshes describing three surfaces.
        /// </summary>
        private SkinnedModel StaticMerge(Dictionary<string, object> root, List<object> meshes, List<object> nodes)
        {
            List<object> materials = Opt(root, "materials") as List<object>;
            List<SkinnedModel> parts = new List<SkinnedModel>();
            string name = null;

            for (int i = 0; i < nodes.Count; i++)
            {
                Dictionary<string, object> node = Obj(nodes[i], "nodes[" + i + "]");
                object which = Opt(node, "mesh");
                if (which == null) continue;
                int m = Int(which, "nodes[" + i + "].mesh");
                if (m < 0 || m >= meshes.Count) continue;

                Dictionary<string, object> mesh = Obj(meshes[m], "meshes[" + m + "]");
                SkinnedModel part = new SkinnedModel { Name = Name(mesh, node) };
                ReadPrimitives(mesh, part, false);
                if (part.Joints != null)
                    throw Bad("'" + part.Name + "' carries bone weights but the file has no armature; " +
                              "in Blender's export panel keep 'Skinning' on and include the armature, " +
                              "then re-export");
                PrimitiveMaterials(mesh, materials, part);
                ToUnity(part, World(nodes, i));
                parts.Add(part);
                if (name == null) name = part.Name;
            }

            string refusal, note;
            SkinnedModel merged = MeshMerge.Static(parts, name ?? "model", out refusal, out note);
            if (merged == null) throw Bad(refusal);
            return merged;
        }

        /// <summary>
        /// The glTF material name behind each of a mesh's primitives, index-parallel to the submeshes
        /// <see cref="ReadPrimitives"/> just added. A primitive with no material slot, or a slot with
        /// no name, falls back to the slot number so two genuinely different unnamed materials do not
        /// collapse into one submesh.
        /// </summary>
        private void PrimitiveMaterials(Dictionary<string, object> mesh, List<object> materials, SkinnedModel part)
        {
            List<object> primitives = Array_(Opt(mesh, "primitives"), "primitives");
            foreach (object p in primitives)
            {
                object slot = Opt(Obj(p, "primitives[]"), "material");
                if (slot == null) { part.Materials.Add("material"); continue; }
                int index = Int(slot, "primitives[].material");
                string named = materials != null && index >= 0 && index < materials.Count
                    ? Opt(Obj(materials[index], "materials[" + index + "]"), "name") as string
                    : null;
                part.Materials.Add(string.IsNullOrEmpty(named)
                    ? "material" + index.ToString(CultureInfo.InvariantCulture)
                    : named);
                part.MaterialImages.Add(BaseColorImage(index));
                part.MaterialEmissive.Add(EmissiveFactor(index));
            }
        }

        /// <summary>
        /// A material's glTF <c>emissiveFactor</c>, multiplied by KHR_materials_emissive_strength
        /// when the file declares it, or null when the material does not glow.
        ///
        /// glTF's default emissiveFactor is [0,0,0] (spec 3.9.2) - "no emission" - so black is
        /// returned as null rather than as a colour. That matters: writing _EmissionColor black
        /// TOGETHER with the _EMISSION keyword makes Unity light nothing while paying the keyword's
        /// cost, and reads in a material dump exactly like a glow that failed.
        ///
        /// The strength extension is listed in extensionsUSED, not REQUIRED, precisely because a
        /// reader that ignores it still gets a sane picture - so multiplying it in is optional
        /// correctness rather than a compatibility gate. tau_pulse_pistol.glb declares it.
        /// </summary>
        private float[] EmissiveFactor(int material)
        {
            List<object> materials = Opt(root, "materials") as List<object>;
            if (materials == null || material < 0 || material >= materials.Count) return null;
            Dictionary<string, object> m = Obj(materials[material], "materials[" + material + "]");

            float[] rgb = { 0f, 0f, 0f };
            if (Opt(m, "emissiveFactor") is List<object> declared && declared.Count >= 3)
                for (int i = 0; i < 3; i++)
                    rgb[i] = Single(declared[i], "materials[" + material + "].emissiveFactor");

            float strength = 1f;
            if (Opt(m, "extensions") is Dictionary<string, object> ext &&
                Opt(ext, "KHR_materials_emissive_strength") is Dictionary<string, object> es &&
                Opt(es, "emissiveStrength") != null)
                strength = Single(Get(es, "emissiveStrength"), "KHR_materials_emissive_strength");

            if (rgb[0] <= 0f && rgb[1] <= 0f && rgb[2] <= 0f) return null;

            // AN emissiveTexture MODULATES the factor - glTF emission is factor TIMES texture
            // (spec 3.9.2), so the factor alone is the value at the texture's brightest point, not
            // the emission of the whole surface. Honouring the factor while ignoring the texture is
            // how a gun with a few glowing strips becomes a uniformly white-hot object.
            //
            // MEASURED, and it is what the user saw: ar-181's "Material" and the Tau's
            // "MAT_PulseRifle" both declare emissiveFactor [1,1,1] WITH an emissiveTexture, and the
            // Tau adds KHR_materials_emissive_strength 2.11 - so the bake wrote _EmissionColor
            // (1,1,1) and (2.114,2.114,2.114) over the entire model and both rendered as glowing
            // white shapes.
            //
            // This bake binds one texture, _MainTex, so there is nowhere to put the emissive map.
            // Refusing the glow is the honest answer: an empty array says "this material asked to
            // glow and we would not guess", which ProjectBake reports by name, as against null,
            // which means it never asked.
            if (Opt(m, "emissiveTexture") != null) return new float[0];
            return new[] { rgb[0] * strength, rgb[1] * strength, rgb[2] * strength, 1f };
        }

        /// <summary>
        /// The RAW BYTES of a material's base-colour image, straight out of the .glb, or null when
        /// the material declares no texture.
        ///
        /// WHY THIS EXISTS. A model downloaded off the internet carries its own textures inside the
        /// file, and this reader used to tally them as "Ignored" and decode none - so every imported
        /// model baked with no _MainTex and rendered PURE WHITE, which is indistinguishable on screen
        /// from a material that failed to load. Requiring the author to unpack the images by hand and
        /// name the files to match is exactly the manual step the tool exists to remove.
        ///
        /// The bytes are handed on UNDECODED: PNG and JPEG are decoded by Unity's own
        /// Texture2D.LoadImage at bake time, which is the same call the author-supplied
        /// Content\Textures\*.png route already uses, and the only image decoder in this process.
        ///
        /// A data: URI is NOT read. The glTF spec allows one, but a .glb exported by any real tool
        /// puts its images in a bufferView; a file that uses one gets no texture and says so through
        /// the bake's own "tex=(none...)" line rather than being refused outright.
        /// </summary>
        private byte[] BaseColorImage(int material)
        {
            List<object> materials = Opt(root, "materials") as List<object>;
            if (materials == null || material < 0 || material >= materials.Count) return null;
            object pbr = Opt(Obj(materials[material], "materials[]"), "pbrMetallicRoughness");
            if (pbr == null) return null;
            object baseTex = Opt(Obj(pbr, "pbrMetallicRoughness"), "baseColorTexture");
            if (baseTex == null) return null;
            object index = Opt(Obj(baseTex, "baseColorTexture"), "index");
            if (index == null) return null;

            List<object> textures = Opt(root, "textures") as List<object>;
            int t = Int(index, "baseColorTexture.index");
            if (textures == null || t < 0 || t >= textures.Count) return null;
            object source = Opt(Obj(textures[t], "textures[]"), "source");
            if (source == null) return null;

            List<object> images = Opt(root, "images") as List<object>;
            int im = Int(source, "textures[].source");
            if (images == null || im < 0 || im >= images.Count) return null;
            Dictionary<string, object> image = Obj(images[im], "images[" + im + "]");
            object view = Opt(image, "bufferView");
            if (view == null) return null;                 // a data: URI or an external file

            int viewIndex = Int(view, "images[" + im + "].bufferView");
            List<object> views = Array_(Opt(root, "bufferViews"), "bufferViews");
            if (viewIndex < 0 || viewIndex >= views.Count) return null;
            int length = Int(Get(Obj(views[viewIndex], "bufferViews[" + viewIndex + "]"), "byteLength"),
                             "bufferViews[" + viewIndex + "].byteLength");
            if (length <= 0) return null;

            // Bounds-checked through the same Resolve every accessor uses, so an image that reaches
            // past the BIN chunk is refused there rather than read out of bounds here.
            Resolve(viewIndex, 0, 1, 1, length, "images[" + im + "]", out byte[] data, out int start, out int _);
            byte[] bytes = new byte[length];
            Array.Copy(data, start, bytes, 0, length);
            return bytes;
        }

        /// <summary>
        /// One material's baseColorFactor, linear RGBA. glTF's own default when the material declares
        /// none is [1,1,1,1] (spec 3.9.2), which is also what a slot-less primitive gets - so a file
        /// that says nothing about colour still bakes to plain white, exactly as before.
        /// </summary>
        private float[] BaseColorFactor(int slot)
        {
            if (slot < 0) return null;
            List<object> materials = Opt(root, "materials") as List<object>;
            if (materials == null || slot >= materials.Count) return null;
            string what = "materials[" + slot.ToString(CultureInfo.InvariantCulture) + "]";
            object pbr = Opt(Obj(materials[slot], what), "pbrMetallicRoughness");
            object factor = pbr == null ? null : Opt(Obj(pbr, what + ".pbrMetallicRoughness"), "baseColorFactor");
            if (factor == null) return new[] { 1f, 1f, 1f, 1f };
            List<object> rgba = Array_(factor, what + ".pbrMetallicRoughness.baseColorFactor");
            if (rgba.Count != 4)
                throw Bad(what + " has a base colour of " + rgba.Count.ToString(CultureInfo.InvariantCulture) +
                    " numbers and glTF states four (RGBA); re-export the file");
            float[] c = new float[4];
            for (int i = 0; i < 4; i++) c[i] = Single(rgba[i], what + ".pbrMetallicRoughness.baseColorFactor");
            return c;
        }

        private static string Name(Dictionary<string, object> mesh, Dictionary<string, object> node)
        {
            object name = Opt(mesh, "name") ?? (node == null ? null : Opt(node, "name"));
            string text = name as string;
            return string.IsNullOrEmpty(text) ? "model" : text;
        }

        // ---------------------------------------------------------------- primitives

        /// <summary>
        /// One merged vertex block. Our own exporter writes ONE block shared by every primitive;
        /// Blender splits a multi-material mesh into one primitive per material, each with its own
        /// accessors. Keying a block on its POSITION accessor handles both: a repeated POSITION means
        /// the same vertices, so the block is reused and the file round-trips to its original vertex
        /// count instead of doubling it.
        /// </summary>
        private sealed class Block
        {
            internal int Position = -1, Normal = -1, Tangent = -1, Uv0 = -1, Uv1 = -1, Joints = -1, Weights = -1;
            internal int[] TargetPositions, TargetNormals;
            internal int Base, Count;
        }

        private void ReadPrimitives(Dictionary<string, object> mesh, SkinnedModel model, bool skinned)
        {
            List<object> primitives = Array_(Opt(mesh, "primitives"), "primitives");
            if (primitives.Count == 0)
                throw Bad("the mesh has no primitives, so it carries no faces; give the object at least one triangulated face and re-export");
            if (primitives.Count > MaxSubmeshes)
                throw Bad("the mesh has " + primitives.Count.ToString(CultureInfo.InvariantCulture) +
                    " primitives, past the " + MaxSubmeshes.ToString(CultureInfo.InvariantCulture) +
                    " limit; reduce the number of materials on the object and re-export");

            var blocks = new List<Block>();
            var byPosition = new Dictionary<int, Block>();
            var indices = new List<int[]>();
            int targets = -1;
            int vertices = 0;
            int total = 0;
            // The material of the BIGGEST primitive, not of the first: every primitive is merged into
            // one submesh carrying one material, so the colour that should survive is the one covering
            // most of the model. Picking primitive 0 would paint the whole spider with whatever
            // material its first few triangles happen to use.
            int dominant = -1, dominantSize = 0;

            for (int p = 0; p < primitives.Count; p++)
            {
                string what = "primitive " + p.ToString(CultureInfo.InvariantCulture);
                Dictionary<string, object> primitive = Obj(primitives[p], what);
                // Draco lives HERE, on the primitive, not in extensionsRequired alone - a file can
                // declare it optional and still carry its geometry only in the compressed block, so
                // this is where it is DECODED. Any other primitive extension is still refused by name.
                if (Opt(primitive, "extensions") is Dictionary<string, object> extensions && extensions.Count > 0)
                {
                    foreach (string key in extensions.Keys)
                        if (key != Draco.Extension) throw Unreadable(key);
                    Decompress(Obj(Opt(extensions, Draco.Extension), what + "." + Draco.Extension), primitive, what);
                }
                object mode = Opt(primitive, "mode");
                if (mode != null && Int(mode, what + ".mode") != 4)
                    throw Bad(what + " is not made of triangles (glTF mode " + Int(mode, what + ".mode").ToString(CultureInfo.InvariantCulture) +
                        "); in Blender apply a Triangulate modifier or tick 'Triangulate' on export");

                Dictionary<string, object> attributes = Obj(Get(primitive, "attributes"), what + ".attributes");
                if (attributes.ContainsKey("JOINTS_1") || attributes.ContainsKey("WEIGHTS_1"))
                    throw Bad(what + " weights some vertices to more than four bones, and Unity's skinning stores four; " +
                        "in Blender use Weight Paint > Weights > Limit Total with a limit of 4, then re-export");

                var block = new Block
                {
                    Position = Attribute_(attributes, "POSITION", what, true),
                    Normal = Attribute_(attributes, "NORMAL", what, false),
                    Tangent = Attribute_(attributes, "TANGENT", what, false),
                    Uv0 = Attribute_(attributes, "TEXCOORD_0", what, false),
                    Uv1 = Attribute_(attributes, "TEXCOORD_1", what, false),
                    Joints = Attribute_(attributes, "JOINTS_0", what, false),
                    Weights = Attribute_(attributes, "WEIGHTS_0", what, false),
                };
                if (skinned && (block.Joints < 0 || block.Weights < 0))
                    throw Bad(what + " has no bone weights but the model is rigged, so those faces would not follow the skeleton; " +
                        "in Blender give every part of the mesh an Armature modifier and vertex groups, then re-export");
                List<object> primitiveTargets = Opt(primitive, "targets") as List<object>;
                int count = primitiveTargets == null ? 0 : primitiveTargets.Count;
                if (targets < 0) targets = count;
                else if (targets != count)
                    throw Bad(what + " has " + count.ToString(CultureInfo.InvariantCulture) + " blend shapes but primitive 0 has " +
                        targets.ToString(CultureInfo.InvariantCulture) + "; every part of the mesh must carry the same shape keys, so re-export after adding the missing ones");
                if (count > MaxMorphs)
                    throw Bad(what + " has " + count.ToString(CultureInfo.InvariantCulture) + " blend shapes, past the " +
                        MaxMorphs.ToString(CultureInfo.InvariantCulture) + " limit; remove unused shape keys and re-export");
                block.TargetPositions = new int[count];
                block.TargetNormals = new int[count];
                for (int t = 0; t < count; t++)
                {
                    Dictionary<string, object> target = Obj(primitiveTargets[t], what + " blend shape " + t);
                    block.TargetPositions[t] = Attribute_(target, "POSITION", what + " blend shape " + t, true);
                    block.TargetNormals[t] = Attribute_(target, "NORMAL", what + " blend shape " + t, false);
                }

                if (byPosition.TryGetValue(block.Position, out Block existing))
                {
                    Same(existing, block, what);
                    block = existing;
                }
                else
                {
                    block.Count = Count(block.Position, what + " POSITION");
                    if (block.Count == 0) throw Bad(what + " has no vertices; delete the empty part and re-export");
                    vertices += block.Count;
                    if (vertices > MaxVertices)
                        throw Bad("the mesh has more than " + MaxVertices.ToString(CultureInfo.InvariantCulture) +
                            " vertices, past the limit this mod will build; decimate it in Blender and re-export");
                    block.Base = blocks.Count == 0 ? 0 : blocks[blocks.Count - 1].Base + blocks[blocks.Count - 1].Count;
                    byPosition[block.Position] = block;
                    blocks.Add(block);
                }

                object indexAccessor = Opt(primitive, "indices");
                if (indexAccessor == null)
                    throw Bad(what + " has no index buffer, and this mod only reads indexed triangles; re-export from Blender, whose exporter always writes indices");
                int[] triangles = Integers(Int(indexAccessor, what + ".indices"), what + " indices", "SCALAR", -1, true);
                if (triangles.Length % 3 != 0)
                    throw Bad(what + " has " + triangles.Length.ToString(CultureInfo.InvariantCulture) +
                        " indices, which is not a whole number of triangles; in Blender apply a Triangulate modifier and re-export");
                total += triangles.Length;
                if (total > MaxIndices)
                    throw Bad("the mesh has more than " + MaxIndices.ToString(CultureInfo.InvariantCulture) +
                        " triangle indices, past the limit this mod will build; decimate it in Blender and re-export");
                for (int i = 0; i < triangles.Length; i++)
                {
                    if (triangles[i] >= block.Count)
                        throw Bad(what + " points at vertex " + triangles[i].ToString(CultureInfo.InvariantCulture) + " of " +
                            block.Count.ToString(CultureInfo.InvariantCulture) + " it declares; the file is corrupt, so re-export it");
                    triangles[i] += block.Base;
                }
                indices.Add(triangles);
                object slot = Opt(primitive, "material");
                if (slot != null && triangles.Length > dominantSize)
                {
                    dominantSize = triangles.Length;
                    dominant = Int(slot, what + ".material");
                }
            }
            model.BaseColor = BaseColorFactor(dominant);

            model.Positions = new ObjVector3[vertices];
            if (blocks.TrueForAll(x => x.Normal >= 0)) model.Normals = new ObjVector3[vertices];
            else if (blocks.Exists(x => x.Normal >= 0)) throw Mixed("normals", "NORMAL");
            if (blocks.TrueForAll(x => x.Tangent >= 0)) model.Tangents = new float[vertices * 4];
            else if (blocks.Exists(x => x.Tangent >= 0)) throw Mixed("tangents", "TANGENT");
            if (blocks.TrueForAll(x => x.Uv0 >= 0)) model.Uv0 = new ObjVector2[vertices];
            else if (blocks.Exists(x => x.Uv0 >= 0)) throw Mixed("UVs", "TEXCOORD_0");
            if (blocks.TrueForAll(x => x.Uv1 >= 0)) model.Uv1 = new ObjVector2[vertices];
            else if (blocks.Exists(x => x.Uv1 >= 0)) throw Mixed("second UVs", "TEXCOORD_1");
            if (blocks.TrueForAll(x => x.Joints >= 0))
            {
                model.Joints = new ushort[vertices * 4];
                model.Weights = new float[vertices * 4];
            }
            else if (blocks.Exists(x => x.Joints >= 0)) throw Mixed("bone weights", "JOINTS_0");

            foreach (Block block in blocks)
            {
                Vec3(block.Position, block.Base, block.Count, model.Positions, "POSITION", false);
                if (model.Normals != null) Vec3(block.Normal, block.Base, block.Count, model.Normals, "NORMAL", false);
                if (model.Tangents != null) Vec4(block.Tangent, block.Base, block.Count, model.Tangents, "TANGENT");
                if (model.Uv0 != null) Vec2(block.Uv0, block.Base, block.Count, model.Uv0, "TEXCOORD_0");
                if (model.Uv1 != null) Vec2(block.Uv1, block.Base, block.Count, model.Uv1, "TEXCOORD_1");
                if (model.Joints != null) Skin(block, model);
            }
            foreach (int[] triangles in indices) model.Submeshes.Add(triangles);

            if (targets > 0) ReadTargets(mesh, model, blocks, targets, vertices);
        }

        private void ReadTargets(Dictionary<string, object> mesh, SkinnedModel model, List<Block> blocks, int targets, int vertices)
        {
            var names = new List<string>();
            if (Opt(mesh, "extras") is Dictionary<string, object> extras && Opt(extras, "targetNames") is List<object> declared)
                foreach (object name in declared) names.Add(Str(name, "extras.targetNames"));
            if (names.Count != targets)
                throw Bad("the mesh has " + targets.ToString(CultureInfo.InvariantCulture) + " blend shapes but " +
                    names.Count.ToString(CultureInfo.InvariantCulture) + " shape names, and the game addresses a blend shape by name; " +
                    "re-export from Blender, which always writes one name per shape key");
            for (int t = 0; t < targets; t++)
            {
                var morph = new SkinMorph { Name = names[t], Positions = new ObjVector3[vertices] };
                bool normals = blocks.TrueForAll(x => x.TargetNormals[t] >= 0);
                if (!normals && blocks.Exists(x => x.TargetNormals[t] >= 0)) throw Mixed("blend shape normals", "target NORMAL");
                if (normals) morph.Normals = new ObjVector3[vertices];
                foreach (Block block in blocks)
                {
                    // Blender writes shape keys as SPARSE accessors by default, and a shape key that
                    // moves nothing can arrive with no data block at all, which glTF reads as zeroes.
                    Vec3(block.TargetPositions[t], block.Base, block.Count, morph.Positions, "blend shape '" + names[t] + "'", true);
                    if (normals) Vec3(block.TargetNormals[t], block.Base, block.Count, morph.Normals, "blend shape '" + names[t] + "' normals", true);
                }
                model.Morphs.Add(morph);
            }
        }

        private void Skin(Block block, SkinnedModel model)
        {
            int[] joints = Integers(block.Joints, "JOINTS_0", "VEC4", block.Count, false);
            float[] weights = Floats(block.Weights, "WEIGHTS_0", "VEC4", block.Count);
            if (joints.Length != block.Count * 4)
                throw Bad("JOINTS_0 has " + (joints.Length / 4).ToString(CultureInfo.InvariantCulture) + " entries for " +
                    block.Count.ToString(CultureInfo.InvariantCulture) + " vertices; the file is corrupt, so re-export it");
            for (int v = 0; v < block.Count; v++)
            {
                double sum = 0.0;
                for (int k = 0; k < 4; k++)
                {
                    float weight = weights[v * 4 + k];
                    if (float.IsNaN(weight) || float.IsInfinity(weight))
                        throw Bad("vertex " + (block.Base + v).ToString(CultureInfo.InvariantCulture) +
                            " has a bone weight that is not a number; the file is corrupt, so re-export it");
                    if (weight < 0f)
                        throw Bad("vertex " + (block.Base + v).ToString(CultureInfo.InvariantCulture) +
                            " has a negative bone weight; in Blender use Weight Paint > Weights > Clean, then re-export");
                    sum += weight;
                }
                if (sum <= 0.0)
                    throw Bad("vertex " + (block.Base + v).ToString(CultureInfo.InvariantCulture) +
                        " is weighted to no bone at all, so it could not follow the skeleton; in Blender select the mesh and use " +
                        "Weight Paint > Weights > Normalize All with 'Lock Active' off, then re-export");
                for (int k = 0; k < 4; k++)
                {
                    int joint = joints[v * 4 + k];
                    if (joint > MaxJoints)
                        throw Bad("vertex " + (block.Base + v).ToString(CultureInfo.InvariantCulture) + " references bone " +
                            joint.ToString(CultureInfo.InvariantCulture) + ", past the " + MaxJoints.ToString(CultureInfo.InvariantCulture) +
                            " glTF limit; the file is corrupt, so re-export it");
                    model.Joints[(block.Base + v) * 4 + k] = (ushort)joint;
                    model.Weights[(block.Base + v) * 4 + k] = (float)(weights[v * 4 + k] / sum);
                }
            }
        }

        private void ReadSkin(int index, SkinnedModel model)
        {
            List<object> skins = Array_(Opt(root, "skins"), "skins");
            if (index < 0 || index >= skins.Count)
                throw Bad("the mesh points at armature " + index.ToString(CultureInfo.InvariantCulture) + " but the file declares " +
                    skins.Count.ToString(CultureInfo.InvariantCulture) + "; the file is corrupt, so re-export it");
            Dictionary<string, object> skin = Obj(skins[index], "skins[" + index + "]");
            List<object> joints = Array_(Get(skin, "joints"), "skins.joints");
            if (joints.Count == 0)
                throw Bad("the file's armature lists no bones; in Blender export the mesh together with its armature and re-export");
            if (joints.Count > MaxJoints)
                throw Bad("the file's armature has " + joints.Count.ToString(CultureInfo.InvariantCulture) + " bones, past the " +
                    MaxJoints.ToString(CultureInfo.InvariantCulture) + " glTF limit; simplify the rig and re-export");
            List<object> nodes = Array_(Opt(root, "nodes"), "nodes");
            int[] jointNode = new int[joints.Count];
            for (int j = 0; j < joints.Count; j++)
            {
                int node = Int(joints[j], "skins.joints[" + j + "]");
                if (node < 0 || node >= nodes.Count)
                    throw Bad("the armature's bone " + j.ToString(CultureInfo.InvariantCulture) + " points at node " +
                        node.ToString(CultureInfo.InvariantCulture) + " but the file declares " +
                        nodes.Count.ToString(CultureInfo.InvariantCulture) + "; the file is corrupt, so re-export it");
                object name = Opt(Obj(nodes[node], "nodes[" + node + "]"), "name");
                string text = name as string;
                if (string.IsNullOrEmpty(text))
                    throw Bad("the armature's bone " + j.ToString(CultureInfo.InvariantCulture) +
                        " has no name, and bones are matched to the game's skeleton by name; name every bone in Blender and re-export");
                model.JointNames.Add(text);
                jointNode[j] = node;
            }
            Hierarchy(model, nodes, jointNode);
            this.jointNode = jointNode;
            if (model.Joints == null)
                throw Bad("the file has an armature but the mesh carries no bone weights, so nothing would follow the skeleton; " +
                    "in Blender give the mesh an Armature modifier with vertex groups and re-export");
            for (int i = 0; i < model.Joints.Length; i++)
                if (model.Joints[i] >= joints.Count)
                    throw Bad("vertex " + (i / 4).ToString(CultureInfo.InvariantCulture) + " references bone " +
                        model.Joints[i].ToString(CultureInfo.InvariantCulture) + " but the file's armature has " +
                        joints.Count.ToString(CultureInfo.InvariantCulture) + "; the file is corrupt, so re-export it");

            object inverse = Opt(skin, "inverseBindMatrices");
            if (inverse == null)
                throw Bad("the file's armature has no bind poses, so the mesh would collapse onto the skeleton's origin; " +
                    "re-export from Blender, whose exporter always writes them");
            float[] matrices = Floats(Int(inverse, "skins.inverseBindMatrices"), "inverseBindMatrices", "MAT4", joints.Count);
            model.InverseBindMatrices = new float[joints.Count][];
            for (int j = 0; j < joints.Count; j++)
            {
                var matrix = new float[16];
                Array.Copy(matrices, j * 16, matrix, 0, 16);
                foreach (float value in matrix)
                    if (float.IsNaN(value) || float.IsInfinity(value))
                        throw Bad("the bind pose of bone '" + model.JointNames[j] +
                            "' is not a number; the file is corrupt, so re-export it");
                model.InverseBindMatrices[j] = matrix;
            }
        }

        /// <summary>
        /// The file's NODE TREE, kept as parent links between JOINT SLOTS - the one thing the bake
        /// could not know before, and the difference between a rig that poses and a rig that ANIMATES:
        /// a bone with no parent link cannot carry its children, so moving a hip leaves the head
        /// behind. A joint whose nearest ancestor is not itself a joint (Blender's armature object,
        /// our own extractor's "&lt;name&gt;_rig" holder) hangs off the model root, which is what a
        /// flat rig already was.
        ///
        /// Rest transforms are NOT read from here. The bind poses are the authority for where a bone
        /// sits - <see cref="ModelBuild.Invert"/>'s remark - so a file whose node TRS and whose
        /// inverseBindMatrices disagree still skins undeformed, and the tree contributes exactly one
        /// fact: who carries whom. <see cref="RestLocals"/> derives every local transform from those
        /// same bind poses, once <see cref="ToUnity"/> has converted them.
        /// </summary>
        private static void Hierarchy(SkinnedModel model, List<object> nodes, int[] jointNode)
        {
            int[] parentOfNode = Parents(nodes);
            var slotOfNode = new int[nodes.Count];
            for (int i = 0; i < slotOfNode.Length; i++) slotOfNode[i] = -1;
            for (int j = 0; j < jointNode.Length; j++) slotOfNode[jointNode[j]] = j;

            model.JointNodes = new int[jointNode.Length];
            for (int j = 0; j < jointNode.Length; j++)
            {
                int parent = -1, steps = 0;
                for (int at = parentOfNode[jointNode[j]]; at >= 0; at = parentOfNode[at])
                {
                    if (++steps > nodes.Count)
                        throw Bad("the file's object tree loops back on itself above bone '" +
                            model.JointNames[j] + "'; re-export the file rather than editing it by hand");
                    if (slotOfNode[at] < 0) continue;
                    parent = slotOfNode[at];
                    break;
                }
                if (parent == j)
                    throw Bad("bone '" + model.JointNames[j] + "' is its own parent; the file is corrupt, so re-export it");
                model.Nodes.Add(new SkinNode { Name = model.JointNames[j], Parent = parent });
                model.JointNodes[j] = j;
            }
        }

        /// <summary>
        /// Every bone's LOCAL rest transform, derived from the bind poses alone: a bone's world rest
        /// is the inverse of its bind pose, so its local one is bindPose(parent) * inverse(bindPose).
        /// Called after <see cref="ToUnity"/>, so what comes out is Unity space like everything else
        /// on the model.
        /// </summary>
        private static void RestLocals(SkinnedModel model)
        {
            for (int j = 0; j < model.Nodes.Count; j++)
            {
                int parent = model.Nodes[j].Parent;
                model.Nodes[j].Local = ModelBuild.LocalRest(model.InverseBindMatrices[j],
                    parent < 0 ? null : model.InverseBindMatrices[parent], model.Nodes[j].Name);
            }
        }

        /// <summary>
        /// The glTF-space global transform of the chain ABOVE the armature's root bone - see
        /// <see cref="above"/> for why it exists at all. A joint whose <see cref="SkinNode.Parent"/> is
        /// -1 is a root of the imported rig; the matrix wanted is the world transform of the glTF NODE
        /// that carries it, which is the part of <c>globalTransformOfJointNode</c> the inverse bind
        /// matrices alone cannot state. That reading holds while the node carrying the roots is the
        /// armature OBJECT, which is Blender's own shape and what u8_probe/u8_rootfold assert.
        ///
        /// TWO ROOTS UNDER DIFFERENT TRANSFORMS ARE IMPORTED, NOT REFUSED, AND THE FOLD IS DROPPED.
        /// A Phoenix Point BODY-PART mesh exported together with its rig is exactly that shape:
        /// CHR_PX_HVY_TS_M_V01's ten joints hang one under each of Root, Spine_1..3, Chest, both
        /// Shoulders, both Arms and Neck, so every one of them is a root with its own parent matrix.
        /// Those parents are BONES, already inside the space the bind poses are written in - folding
        /// one in would apply the spine chain twice - and there is no single fold to pick anyway:
        /// <see cref="Carry"/> moves the ONE vertex buffer by <c>over</c> and every bind pose by its
        /// inverse, so <c>boneWorld * bindPose</c> stays the identity - an undeformed rest - only while
        /// one matrix answers for every root. Identity is the only matrix that does, and it is the
        /// file's own statement: the bind poses stand exactly as written.
        ///
        /// It used to throw here instead, and the refusal told the author to run Blender's
        /// Object > Apply > All Transforms. That REWRITES the skin (a 10-joint / 17561-vertex torso
        /// came back 19 joints / 26059 vertices) and destroyed the weights it was supposed to fix, so
        /// no refusal on this path may ever suggest it again.
        ///
        /// ponytail: no per-root fold - geometrically impossible with one vertex buffer, see above.
        /// A body part under a SCALED armature object therefore imports at the armature's own scale;
        /// if one ever turns up, the lever is glTF's own skins[].skeleton, not a second fold.
        /// </summary>
        private float[] Above(List<object> nodes, SkinnedModel model)
        {
            int[] parentOfNode = Parents(nodes);
            float[] shared = null;
            for (int j = 0; j < model.Nodes.Count; j++)
            {
                if (model.Nodes[j].Parent >= 0) continue;
                int over = parentOfNode[jointNode[j]];
                float[] one = over < 0 ? Identity() : World(nodes, over);
                if (shared == null) { shared = one; continue; }
                for (int i = 0; i < 16; i++)
                    if (Math.Abs(shared[i] - one[i]) > 1e-6f) return Identity();
            }
            return shared ?? Identity();
        }

        /// <summary>
        /// Folds <paramref name="over"/> - the transform of the nodes ABOVE the armature - into the
        /// model, so a conformant file arrives in the space its exporter meant instead of the
        /// armature's own local space. Two halves, and both are needed or the rig deforms:
        ///
        ///   * the vertices move by it, which is what makes the model arrive Y-up and at the exporter's
        ///     scale (<see cref="Bake"/>, the same routine the static path already uses);
        ///   * every inverse bind matrix is post-multiplied by its INVERSE, so <c>bindPose</c> still
        ///     maps the NEW vertex space into bone space. Skipping this half leaves the rest pose
        ///     looking right and every posed frame wrong, which is the silent version of this bug.
        ///
        /// <see cref="RestLocals"/> then derives the bone rests from the corrected matrices, so the
        /// root bone picks the transform up and no bone below it changes at all
        /// (<c>bind(parent) * inverse(bind(child))</c> cancels it).
        /// </summary>
        private static void Carry(SkinnedModel model, float[] over)
        {
            if (IsIdentity(over)) return;
            Bake(model, over);
            float[] inverse = ModelBuild.Invert(over, "the object the armature hangs under");
            for (int j = 0; j < model.InverseBindMatrices.Length; j++)
                model.InverseBindMatrices[j] = Multiply(model.InverseBindMatrices[j], inverse);
        }

        // ---------------------------------------------------------------- animation

        /// <summary>
        /// The file's own animation clips, in the order it lists them. glTF states a curve as KEYS at
        /// arbitrary times; a serialized AnimationClip's dense bank is a uniform float per frame
        /// (ClipFields' class remark), so every channel is RESAMPLED onto one grid per clip. The grid
        /// is not a constant: <see cref="Rate"/> picks the coarsest rate every key time of the clip
        /// still lands on, so on a file exported at 24 fps with dropped keys - which is what Blender
        /// writes - a sample sits exactly ON each key and the resampling costs no accuracy at all. The
        /// grid always REACHES the last key (a ceiling, not a rounding), and a clip holding a STEP curve
        /// is sampled on a whole multiple of that rate so the ramp the dense bank has to reconstruct is
        /// as narrow as this mod allows - see <see cref="StepGrid"/>, which states the bound.
        ///
        /// A channel that drives a node which is NOT a bone of the armature (the mesh object, an empty,
        /// a camera) is DROPPED and counted: the bake writes bone transforms under the model root and
        /// has nowhere to put anything else, and a curve bound to a path the rig does not spell drives
        /// nothing silently. Blend-shape ("weights") channels are dropped the same way - the bake has
        /// no morph curve bank. Both counts are stated in <see cref="SampledClip.LossyReason"/> so the
        /// loss is never silent, and a clip can legitimately come back with zero tracks.
        /// </summary>
        private void Animations(SkinnedModel model, List<SampledClip> clips)
        {
            List<object> animations = Array_(Opt(root, "animations"), "animations");
            if (animations.Count == 0) return;
            if (animations.Count > MaxClips)
                throw Bad("the file carries " + animations.Count.ToString(CultureInfo.InvariantCulture) +
                    " animations, past the " + MaxClips.ToString(CultureInfo.InvariantCulture) +
                    " limit this mod will read; delete the clips you do not need and re-export");
            if (jointNode == null)
                throw Bad("the file carries " + animations.Count.ToString(CultureInfo.InvariantCulture) +
                    " animation(s) but the mesh has no armature, so nothing says which bone a curve drives; " +
                    "in Blender export the mesh together with its armature and re-export");

            List<object> nodes = Array_(Opt(root, "nodes"), "nodes");
            var slotOfNode = new int[nodes.Count];
            for (int i = 0; i < slotOfNode.Length; i++) slotOfNode[i] = -1;
            for (int j = 0; j < jointNode.Length; j++) slotOfNode[jointNode[j]] = j;

            for (int i = 0; i < animations.Count; i++)
                clips.Add(Animation(Obj(animations[i], "animations[" + i + "]"), i, model, slotOfNode));
        }

        private SampledClip Animation(Dictionary<string, object> animation, int index, SkinnedModel model,
                                      int[] slotOfNode)
        {
            string what = "animations[" + index.ToString(CultureInfo.InvariantCulture) + "]";
            List<object> samplers = Array_(Get(animation, "samplers"), what + ".samplers");
            List<object> channels = Array_(Get(animation, "channels"), what + ".channels");
            if (samplers.Count == 0 || channels.Count == 0)
                throw Bad("the file's animation " + index.ToString(CultureInfo.InvariantCulture) +
                    " has no " + (channels.Count == 0 ? "channels" : "samplers") +
                    ", so it drives nothing; re-export the file rather than editing it by hand");
            if (samplers.Count > MaxCollection || channels.Count > MaxCollection)
                throw Bad("the file's animation " + index.ToString(CultureInfo.InvariantCulture) +
                    " declares more curves than this mod will read; simplify the clip and re-export");

            // Same three parallel lists GlbCodec.WriteAnimation builds on the way out, so the two
            // halves of the format are spelled once each and in the same terms.
            var channelSampler = new List<int>();
            var channelSlot = new List<int>();
            var channelPath = new List<string>();
            int droppedNodes = 0, droppedShapes = 0;

            for (int c = 0; c < channels.Count; c++)
            {
                string at = what + ".channels[" + c.ToString(CultureInfo.InvariantCulture) + "]";
                Dictionary<string, object> channel = Obj(channels[c], at);
                Dictionary<string, object> target = Obj(Get(channel, "target"), at + ".target");
                string path = Str(Get(target, "path"), at + ".target.path");
                object which = Opt(target, "node");
                // glTF allows a channel with no node - it is defined to drive nothing.
                if (which == null) { droppedNodes++; continue; }
                int node = Int(which, at + ".target.node");
                if (node < 0 || node >= slotOfNode.Length)
                    throw Bad("the file's animation " + index.ToString(CultureInfo.InvariantCulture) +
                        " drives object " + node.ToString(CultureInfo.InvariantCulture) + " but the file declares " +
                        slotOfNode.Length.ToString(CultureInfo.InvariantCulture) + "; the file is corrupt, so re-export it");
                if (path == "weights") { droppedShapes++; continue; }
                if (path != "translation" && path != "rotation" && path != "scale")
                    throw Bad("the file's animation " + index.ToString(CultureInfo.InvariantCulture) + " drives '" +
                        path + "', which glTF 2.0 does not define; re-export the file rather than editing it by hand");
                if (slotOfNode[node] < 0) { droppedNodes++; continue; }
                int sampler = Int(Get(channel, "sampler"), at + ".sampler");
                if (sampler < 0 || sampler >= samplers.Count)
                    throw Bad("the file's animation " + index.ToString(CultureInfo.InvariantCulture) +
                        " points at curve " + sampler.ToString(CultureInfo.InvariantCulture) + " but declares " +
                        samplers.Count.ToString(CultureInfo.InvariantCulture) + "; the file is corrupt, so re-export it");
                channelSampler.Add(sampler);
                channelSlot.Add(slotOfNode[node]);
                channelPath.Add(path);
            }

            var clip = new SampledClip { Name = ClipName(animation, index), Kind = "generic" };
            clip.Nodes.AddRange(model.Nodes);          // joint slots, so Track.Node indexes Nodes here too

            // One read per INPUT accessor: a clip shares one time array across many channels (Spider's
            // 37 channels come off 19 of them), and every one of them feeds the rate below.
            var keyTimes = new Dictionary<int, float[]>();
            float duration = 0f;
            // WHERE THIS CLIP STARTS ON THE FILE'S TIMELINE, which is not always zero.
            //
            // A glTF animation carries absolute key times, and a great many exporters lay EVERY clip
            // out on ONE shared timeline instead of rebasing each to zero - Maya and the Sketchfab
            // pipeline both do. MEASURED on 'Cyborg Spider': Spider_Walk holds keys over 0.3333..1.1333
            // while Spider_Death holds keys over 12.6667..13.8333, seven clips end to end on a single
            // 13.83 s reel.
            //
            // Sampling every one of them from 0 was silently catastrophic: the death clip came out
            // 13.83 s long, of which the first 12.67 s was the frozen frame-0 pose, so the creature
            // stood still through twelve seconds before dying, the attack ran 10.9 s against the gate's
            // nine-second stall threshold, and the walk cycle carried a third of a second of dead air
            // that the ramp then averaged its speed over. A single-clip file rebases to itself and is
            // completely unaffected, which is why the first model never showed this.
            float start = float.MaxValue;
            for (int c = 0; c < channelSampler.Count; c++)
            {
                float[] t = Times(samplers, channelSampler[c], what, keyTimes);
                if (t[t.Length - 1] > duration) duration = t[t.Length - 1];
                if (t[0] < start) start = t[0];
            }
            if (start == float.MaxValue || start < 0f || start > duration) start = 0f;
            float reel = duration - start;

            bool snapped;
            float rate = Rate(keyTimes, out snapped);
            bool step = AnyStep(samplers, channelSampler, what);
            if (step) rate = StepGrid(rate, reel);

            // CEILING, not Round: the grid must REACH the last key. Round drops the tail whenever the
            // last key sits in the back half of a frame (0.51 s at 30 Hz ends the clip at 0.50), which
            // only the fallback rate can produce - a snapped rate puts the last key exactly on a frame,
            // where Ceiling and Round agree. GridTolerance is the same 0.01 frame Rate() calls "on the
            // grid", so a snapped clip keeps the frame count it always had.
            double span = Math.Ceiling((double)reel * rate - GridTolerance);
            if (span < 1.0) span = 1.0;               // a one-key clip is a constant pose, not nothing
            // Compared as a DOUBLE, before any cast: a large finite duration overflows int and would
            // otherwise wrap past this guard into the two-frame minimum.
            if (span + 1.0 > MaxFrames)
                throw Bad("the file's animation '" + clip.Name + "' is " +
                    reel.ToString("0.##", CultureInfo.InvariantCulture) + " s at " +
                    rate.ToString("0.##", CultureInfo.InvariantCulture) + " Hz, which is more than the " +
                    MaxFrames.ToString(CultureInfo.InvariantCulture) +
                    " frames this mod will read; shorten the clip and re-export");
            int frames = (int)span + 1;

            // TWO grids, one shape: the clip is SAMPLED where its keys actually are, and STORED
            // starting at zero, because a Unity AnimationClip's own timeline always begins at zero.
            // They differ by exactly the offset above, so a file that already rebases its clips gets
            // the identical array it got before.
            var times = new float[frames];
            var sampleAt = new float[frames];
            for (int f = 0; f < frames; f++) { times[f] = f / rate; sampleAt[f] = start + times[f]; }

            var byslot = new SampledTrack[model.Nodes.Count];
            for (int c = 0; c < channelSampler.Count; c++)
                Channel(samplers, channelSampler[c], channelPath[c], channelSlot[c], what, clip.Name,
                        keyTimes, sampleAt, byslot);

            if (above != null && !IsIdentity(above))
            {
                float[] over = GlbCodec.ConvertMatrix(above);
                for (int slot = 0; slot < byslot.Length; slot++)
                    if (byslot[slot] != null && model.Nodes[slot].Parent < 0)
                        Root(model, slot, byslot[slot], over, frames);
            }

            foreach (SampledTrack track in byslot) if (track != null) clip.Tracks.Add(track);
            clip.Times = times;
            clip.FrameRate = rate;
            clip.SampleRate = rate;
            clip.Length = (frames - 1) / rate;
            clip.WrapMode = "Default";
            clip.LossyReason = (start > 0f
                    ? "lifted off the file's shared timeline at " +
                      start.ToString("0.###", CultureInfo.InvariantCulture) + " s and "
                    : "") +
                "resampled onto a uniform " + rate.ToString("0.##", CultureInfo.InvariantCulture) +
                " Hz grid of " + frames.ToString(CultureInfo.InvariantCulture) + " frame(s)" +
                (snapped ? ", which every key time of the clip lands on" : " - the file's key times fit no rate up to " +
                    MaxRate.ToString(CultureInfo.InvariantCulture) + " Hz, so keys between frames are interpolated") +
                (step ? "; it holds a STEP curve, whose jump is reconstructed as a ramp at most ONE frame wide (" +
                    (1000f / rate).ToString("0.###", CultureInfo.InvariantCulture) +
                    " ms) - a dense clip bank stores one value per frame and no interpolation mode" : "") +
                (droppedNodes > 0 ? "; " + droppedNodes.ToString(CultureInfo.InvariantCulture) +
                    " channel(s) drive something that is not a bone of the armature and were dropped" : "") +
                (droppedShapes > 0 ? "; " + droppedShapes.ToString(CultureInfo.InvariantCulture) +
                    " blend-shape channel(s) were dropped" : "");
            return clip;
        }

        private static string ClipName(Dictionary<string, object> animation, int index)
        {
            string name = Opt(animation, "name") as string;
            return string.IsNullOrEmpty(name) ? "clip" + index.ToString(CultureInfo.InvariantCulture) : name;
        }

        /// <summary>
        /// One sampler's key TIMES, in seconds - glTF's own unit, and the unit
        /// <c>ClipFields.FillClip</c> derives its frame times in. Cached per accessor.
        /// </summary>
        private float[] Times(List<object> samplers, int sampler, string what, Dictionary<int, float[]> cache)
        {
            string at = what + ".samplers[" + sampler.ToString(CultureInfo.InvariantCulture) + "]";
            int input = Int(Get(Obj(samplers[sampler], at), "input"), at + ".input");
            float[] cached;
            if (cache.TryGetValue(input, out cached)) return cached;

            int count, components, component;
            float[] t = Elements(input, at + ".input", "SCALAR", -1, false, out count, out components, out component);
            if (count == 0)
                throw Bad(at + " has no key times, so its curve has no shape; re-export the file from Blender");
            for (int i = 0; i < count; i++)
            {
                if (float.IsNaN(t[i]) || float.IsInfinity(t[i]) || t[i] < 0f)
                    throw Bad(at + " has a key at a time that is not a real number of seconds; the file is corrupt, so re-export it");
                if (i > 0 && t[i] <= t[i - 1])
                    throw Bad(at + " lists its keys out of order, which glTF forbids; the file is corrupt, so re-export it");
            }
            cache[input] = t;
            return t;
        }

        /// <summary>
        /// The sampling rate for one clip: the COARSEST whole rate in [<see cref="MinRate"/>,
        /// <see cref="MaxRate"/>] that every key time of the clip lands on, so no key is smeared
        /// between two frames and no frame is invented between two keys. Blender writes 24 fps keys
        /// with the unchanged ones dropped, so no single channel is uniform (measured on Spider.glb:
        /// deltas 0.04167 / 0.08333 / 0.125 / 0.20833 ... all multiples of 1/24) and asking any ONE
        /// channel for its spacing would answer differently per channel.
        /// <paramref name="snapped"/> false means no such rate exists and the fallback is in use, which
        /// is the only case where a key can fall between two frames.
        /// </summary>
        private static float Rate(Dictionary<int, float[]> keyTimes, out bool snapped)
        {
            if (keyTimes.Count == 0) { snapped = false; return FallbackRate; }   // every channel was dropped
            for (int rate = MinRate; rate <= MaxRate; rate++)
            {
                bool fits = true;
                foreach (float[] times in keyTimes.Values)
                {
                    foreach (float t in times)
                    {
                        double frame = (double)t * rate;
                        if (Math.Abs(frame - Math.Round(frame)) > GridTolerance) { fits = false; break; }
                    }
                    if (!fits) break;
                }
                if (!fits) continue;
                snapped = true;
                return rate;
            }
            snapped = false;
            return FallbackRate;
        }

        /// <summary>
        /// Does any channel of this clip interpolate with STEP? Read BEFORE the grid is chosen, because
        /// a STEP curve is the one shape a dense bank cannot state and the grid is the only lever there
        /// is. CUBICSPLINE is still refused where it is used, in <see cref="Channel"/>.
        /// </summary>
        private static bool AnyStep(List<object> samplers, List<int> used, string what)
        {
            for (int c = 0; c < used.Count; c++)
            {
                string at = what + ".samplers[" + used[c].ToString(CultureInfo.InvariantCulture) + "]";
                if ((Opt(Obj(samplers[used[c]], at), "interpolation") as string) == "STEP") return true;
            }
            return false;
        }

        /// <summary>
        /// The grid for a clip that holds a STEP curve: the highest whole MULTIPLE of the snapped rate
        /// this mod's own ceilings allow.
        ///
        /// WHY IT IS NOT THE SNAPPED RATE. A serialized AnimationClip's dense bank (ClipFields' class
        /// remark) is m_FrameCount x m_CurveCount floats with a single m_SampleRate and NO per-key
        /// interpolation mode - there is nowhere to write "hold". The runtime reconstructs a value
        /// between the two frames that bracket it, so a jump the file states as instantaneous comes back
        /// as a ramp one frame wide however the samples are taken. Resampling alone is exact: the frame
        /// before a step boundary already holds the old value and the frame at it the new one, so the
        /// error is bounded by ONE FRAME of the grid - but the grid the coarsest snapped rate produces
        /// can be a whole SECOND (two keys 1 s apart snap to 1 Hz, and a hold becomes a one-second ramp).
        /// Multiplying keeps every key exactly on a frame - a multiple of a rate every key lands on is a
        /// rate every key lands on - and buys the tightest bound the mod already permits: 1/120 s, 8.3 ms.
        ///
        /// THE BOUND IS TEMPORAL, NOT IN VALUE: inside that one frame the value is wrong by up to the
        /// whole size of the step. It is stated in <see cref="SampledClip.LossyReason"/> in milliseconds.
        /// The cost is paid only by clips that actually carry a STEP curve, and MaxFrames still caps it.
        ///
        /// ponytail: refusing STEP by name (as CUBICSPLINE is) would be a smaller diff, and it was
        /// rejected - the author would have to re-author the file in another tool, which is the one
        /// outcome this importer exists to prevent. Exact holds need a bank the writer does not have
        /// (a StreamedClip keyframe with its own tangents); that is the upgrade path.
        /// </summary>
        private static float StepGrid(float rate, float duration)
        {
            int most = (int)(MaxRate / rate);
            if (most < 1) most = 1;
            while (most > 1 && Math.Ceiling((double)duration * rate * most - GridTolerance) + 1.0 > MaxFrames)
                most--;
            return rate * most;
        }

        /// <summary>
        /// One channel: read its keys, convert them to Unity space, and write the resampled values onto
        /// the clip's grid. The conversion is <see cref="GlbCodec"/>'s own, applied a second time
        /// because each rule is an involution - the vector rule for translation
        /// (<see cref="GlbCodec.Convert(ObjVector3)"/>), the quaternion rule for rotation
        /// (<see cref="GlbCodec.Convert(ObjQuaternion)"/>, which is NOT the vector rule and is the
        /// classic way to mirror an animation while the mesh stays right), and NOTHING for scale, since
        /// S*diag(sx,sy,sz)*S = diag(sx,sy,sz). These are exactly the three
        /// <c>GlbCodec.WriteAnimation</c> applies on the way out.
        /// </summary>
        private void Channel(List<object> samplers, int sampler, string path, int slot, string what,
                             string clipName, Dictionary<int, float[]> keyTimes, float[] times,
                             SampledTrack[] byslot)
        {
            string at = what + ".samplers[" + sampler.ToString(CultureInfo.InvariantCulture) + "]";
            Dictionary<string, object> s = Obj(samplers[sampler], at);
            string interpolation = Opt(s, "interpolation") as string ?? "LINEAR";
            bool step = interpolation == "STEP";
            if (!step && interpolation != "LINEAR")
                throw Bad("the clip '" + clipName + "' stores a curve with '" + interpolation +
                    "' interpolation, which this mod does not read; in Blender's export panel turn ON " +
                    "Animation > Sampling ('Always Sample Animations') so the clip exports as LINEAR keys, then re-export");

            float[] keys = keyTimes[Int(Get(s, "input"), at + ".input")];
            int output = Int(Get(s, "output"), at + ".output");
            bool rotation = path == "rotation";
            float[] values = Floats(output, at + ".output", rotation ? "VEC4" : "VEC3", keys.Length);
            for (int i = 0; i < values.Length; i++)
                if (float.IsNaN(values[i]) || float.IsInfinity(values[i]))
                    throw Bad("the clip '" + clipName + "' has a key value that is not a number; the file is corrupt, so re-export it");

            SampledTrack track = byslot[slot];
            if (track == null) byslot[slot] = track = new SampledTrack { Node = slot };

            if (rotation)
            {
                if (track.Rotations != null) throw Duplicate(clipName, path);
                var keyed = new ObjQuaternion[keys.Length];
                for (int i = 0; i < keys.Length; i++)
                    keyed[i] = GlbCodec.Convert(Unit(values, i * 4, clipName));
                var sampled = new ObjQuaternion[times.Length];
                for (int f = 0, cursor = 0; f < times.Length; f++)
                {
                    float u;
                    int k = Segment(keys, times[f], step, ref cursor, out u);
                    sampled[f] = u <= 0f ? keyed[k] : Slerp(keyed[k], keyed[k + 1], u);
                }
                track.Rotations = sampled;
                return;
            }

            var vectors = new ObjVector3[keys.Length];
            for (int i = 0; i < keys.Length; i++)
            {
                var v = new ObjVector3(values[i * 3], values[i * 3 + 1], values[i * 3 + 2]);
                vectors[i] = path == "scale" ? v : GlbCodec.Convert(v);
            }
            var grid = new ObjVector3[times.Length];
            for (int f = 0, cursor = 0; f < times.Length; f++)
            {
                float u;
                int k = Segment(keys, times[f], step, ref cursor, out u);
                grid[f] = u <= 0f ? vectors[k] : Lerp(vectors[k], vectors[k + 1], u);
            }
            if (path == "scale")
            {
                if (track.Scales != null) throw Duplicate(clipName, path);
                track.Scales = grid;
            }
            else
            {
                if (track.Translations != null) throw Duplicate(clipName, path);
                track.Translations = grid;
            }
        }

        /// <summary>
        /// A curve on a ROOT bone states that bone's local TRS relative to the object the armature hangs
        /// under - the very transform <see cref="Carry"/> folded into the root bone's REST. Playing the
        /// samples as they stand would overwrite that rest and throw the correction away mid-clip, so
        /// <paramref name="over"/> is folded into every sample instead: the frame's matrix is composed,
        /// multiplied by it and decomposed back, exactly as the rest was.
        ///
        /// A channel the file left out keeps the bone's OWN rest value for every frame, which is the
        /// stored rest with the same fold taken back off - so a clip that drives only the root's
        /// rotation still translates and scales where the rest pose says.
        /// </summary>
        private static void Root(SkinnedModel model, int slot, SampledTrack track, float[] over, int frames)
        {
            string bone = model.Nodes[slot].Name;
            float[] t0, r0, s0;
            GlbCodec.Decompose(ModelBuild.Multiply(ModelBuild.Invert(over, bone), model.Nodes[slot].Local),
                               bone, out t0, out r0, out s0);

            var translations = new ObjVector3[frames];
            var rotations = new ObjQuaternion[frames];
            var scales = new ObjVector3[frames];
            for (int f = 0; f < frames; f++)
            {
                ObjVector3 t = track.Translations == null ? new ObjVector3(t0[0], t0[1], t0[2]) : track.Translations[f];
                ObjQuaternion r = track.Rotations == null ? new ObjQuaternion(r0[0], r0[1], r0[2], r0[3]) : track.Rotations[f];
                ObjVector3 s = track.Scales == null ? new ObjVector3(s0[0], s0[1], s0[2]) : track.Scales[f];
                float[] ft, fr, fs;
                GlbCodec.Decompose(ModelBuild.Multiply(over, Trs(t, r, s)), bone, out ft, out fr, out fs);
                // ONE HEMISPHERE, frame to frame. Decompose branches on the largest diagonal and each
                // branch forces its own pivot component non-negative, so two frames that straddle a
                // branch boundary come back as q and -q for rotations the samples state as continuous.
                // A dense clip bank stores one quaternion per frame and no way to say "same rotation":
                // the runtime ramps between the two stored values, takes the long way round, and the
                // bone spins for one frame. q and -q ARE the same rotation, so picking the one that
                // agrees with the previous frame costs nothing and removes the jump.
                if (f > 0 && rotations[f - 1].X * fr[0] + rotations[f - 1].Y * fr[1] +
                             rotations[f - 1].Z * fr[2] + rotations[f - 1].W * fr[3] < 0f)
                    for (int i = 0; i < 4; i++) fr[i] = -fr[i];
                translations[f] = new ObjVector3(ft[0], ft[1], ft[2]);
                rotations[f] = new ObjQuaternion(fr[0], fr[1], fr[2], fr[3]);
                scales[f] = new ObjVector3(fs[0], fs[1], fs[2]);
            }
            track.Translations = translations;
            track.Rotations = rotations;
            track.Scales = scales;
        }

        /// <summary>TRS as one column-major 4x4 (index = col*4 + row) - <see cref="GlbCodec.Decompose"/>
        /// read backwards, so composing and decomposing a frame is a round trip.</summary>
        private static float[] Trs(ObjVector3 t, ObjQuaternion r, ObjVector3 s)
        {
            float x = r.X, y = r.Y, z = r.Z, w = r.W;
            float[] basis =
            {
                1f - 2f * (y * y + z * z), 2f * (x * y + z * w),      2f * (x * z - y * w),
                2f * (x * y - z * w),      1f - 2f * (x * x + z * z), 2f * (y * z + x * w),
                2f * (x * z + y * w),      2f * (y * z - x * w),      1f - 2f * (x * x + y * y)
            };
            float[] scale = { s.X, s.Y, s.Z };
            var m = new float[16];
            for (int col = 0; col < 3; col++)
                for (int row = 0; row < 3; row++) m[col * 4 + row] = basis[col * 3 + row] * scale[col];
            m[12] = t.X; m[13] = t.Y; m[14] = t.Z; m[15] = 1f;
            return m;
        }

        private static FormatException Duplicate(string clipName, string path) =>
            Bad("the clip '" + clipName + "' drives the same bone's " + path +
                " twice, which glTF forbids; re-export the file rather than editing it by hand");

        /// <summary>
        /// The key at or before <paramref name="time"/>, plus how far past it the sample sits.
        /// <paramref name="cursor"/> only moves forward, because the grid is walked in order. Outside
        /// the key range the end value is HELD, which is what glTF says a sampler does there; a STEP
        /// sampler holds everywhere.
        /// </summary>
        private static int Segment(float[] keys, float time, bool step, ref int cursor, out float fraction)
        {
            while (cursor + 1 < keys.Length && keys[cursor + 1] <= time) cursor++;
            fraction = 0f;
            if (step || cursor + 1 >= keys.Length || time <= keys[cursor]) return cursor;
            float span = keys[cursor + 1] - keys[cursor];
            if (span > 0f) fraction = (time - keys[cursor]) / span;
            return cursor;
        }

        private static ObjVector3 Lerp(ObjVector3 a, ObjVector3 b, float u) =>
            new ObjVector3(a.X + (b.X - a.X) * u, a.Y + (b.Y - a.Y) * u, a.Z + (b.Z - a.Z) * u);

        /// <summary>One key's rotation, renormalised - glTF demands a unit quaternion and a float that
        /// has been through an exporter drifts.</summary>
        private static ObjQuaternion Unit(float[] values, int at, string clipName)
        {
            double x = values[at], y = values[at + 1], z = values[at + 2], w = values[at + 3];
            double length = Math.Sqrt(x * x + y * y + z * z + w * w);
            if (length <= 1e-12)
                throw Bad("the clip '" + clipName + "' has a rotation key that is not a rotation at all " +
                    "(all four numbers are zero); the file is corrupt, so re-export it");
            return new ObjQuaternion((float)(x / length), (float)(y / length), (float)(z / length), (float)(w / length));
        }

        /// <summary>
        /// SLERP, which glTF 2.0 REQUIRES for a LINEAR rotation sampler ("For rotations, spherical
        /// linear interpolation (SLERP) MUST be used to interpolate quaternions"). Component-wise lerp
        /// would take the same path at the wrong speed. The shorter arc is chosen because q and -q are
        /// the same rotation and an exporter is free to write either.
        /// </summary>
        private static ObjQuaternion Slerp(ObjQuaternion a, ObjQuaternion b, float u)
        {
            double bx = b.X, by = b.Y, bz = b.Z, bw = b.W;
            double dot = (double)a.X * bx + (double)a.Y * by + (double)a.Z * bz + (double)a.W * bw;
            if (dot < 0.0) { dot = -dot; bx = -bx; by = -by; bz = -bz; bw = -bw; }
            double wa, wb;
            if (dot > 0.9995)
            {
                // The arc is shorter than float precision, so sin(theta) is noise; lerp and renormalise.
                wa = 1.0 - u; wb = u;
            }
            else
            {
                double theta = Math.Acos(dot > 1.0 ? 1.0 : dot), sine = Math.Sin(theta);
                wa = Math.Sin((1.0 - u) * theta) / sine;
                wb = Math.Sin(u * theta) / sine;
            }
            double x = wa * a.X + wb * bx, y = wa * a.Y + wb * by, z = wa * a.Z + wb * bz, w = wa * a.W + wb * bw;
            double length = Math.Sqrt(x * x + y * y + z * z + w * w);
            if (length <= 1e-12) return a;
            return new ObjQuaternion((float)(x / length), (float)(y / length), (float)(z / length), (float)(w / length));
        }

        // ---------------------------------------------------------------- coordinates

        /// <summary>
        /// glTF -> Unity, which is exactly <see cref="GlbCodec"/>'s own rules applied a second time
        /// because each of them is an involution. <paramref name="world"/> is the static mesh node's
        /// world matrix, in glTF space, or null when there is nothing to bake (a skinned mesh node's
        /// transform is ignored by glTF, and an identity one changes nothing).
        /// </summary>
        private static void ToUnity(SkinnedModel model, float[] world)
        {
            if (world != null) Bake(model, world);
            for (int i = 0; i < model.Positions.Length; i++)
            {
                ObjVector3 p = GlbCodec.Convert(model.Positions[i]);
                if (float.IsNaN(p.X) || float.IsInfinity(p.X) || float.IsNaN(p.Y) || float.IsInfinity(p.Y) ||
                    float.IsNaN(p.Z) || float.IsInfinity(p.Z))
                    throw Bad("vertex " + i.ToString(CultureInfo.InvariantCulture) +
                        " is not at a real position; the file is corrupt, so re-export it");
                if (Math.Abs(p.X) > MaxCoordinate || Math.Abs(p.Y) > MaxCoordinate || Math.Abs(p.Z) > MaxCoordinate)
                    throw Bad("vertex " + i.ToString(CultureInfo.InvariantCulture) + " sits more than " +
                        MaxCoordinate.ToString(CultureInfo.InvariantCulture) + " units from the model's origin, " +
                        "once the transforms the file states have been applied; in Blender move the model onto " +
                        "the world origin (Object > Set Origin > Origin to Geometry), scale it to the size the " +
                        "model it replaces is, and re-export");
                model.Positions[i] = p;
            }
            if (model.Normals != null)
                for (int i = 0; i < model.Normals.Length; i++) model.Normals[i] = GlbCodec.Convert(model.Normals[i]);
            if (model.Tangents != null)
                for (int i = 0; i < model.Positions.Length; i++) model.Tangents[i * 4] = -model.Tangents[i * 4];
            if (model.Uv0 != null)
                for (int i = 0; i < model.Uv0.Length; i++) model.Uv0[i] = GlbCodec.ConvertUv(model.Uv0[i]);
            if (model.Uv1 != null)
                for (int i = 0; i < model.Uv1.Length; i++) model.Uv1[i] = GlbCodec.ConvertUv(model.Uv1[i]);
            foreach (SkinMorph morph in model.Morphs)
            {
                for (int i = 0; i < morph.Positions.Length; i++) morph.Positions[i] = GlbCodec.Convert(morph.Positions[i]);
                if (morph.Normals == null) continue;
                for (int i = 0; i < morph.Normals.Length; i++) morph.Normals[i] = GlbCodec.Convert(morph.Normals[i]);
            }
            if (model.InverseBindMatrices != null)
                for (int j = 0; j < model.InverseBindMatrices.Length; j++)
                    model.InverseBindMatrices[j] = GlbCodec.ConvertMatrix(model.InverseBindMatrices[j]);
            foreach (int[] triangles in model.Submeshes)
                for (int i = 0; i + 2 < triangles.Length; i += 3)
                {
                    int swap = triangles[i + 1];
                    triangles[i + 1] = triangles[i + 2];
                    triangles[i + 2] = swap;
                }
        }

        /// <summary>
        /// Bakes a static mesh object's own transform into its vertices, because a replacement has to
        /// arrive in the original's local space. Refuses a mirrored object rather than silently
        /// turning it inside out.
        /// </summary>
        private static void Bake(SkinnedModel model, float[] m)
        {
            double determinant =
                (double)m[0] * (m[5] * (double)m[10] - m[9] * (double)m[6]) -
                (double)m[4] * (m[1] * (double)m[10] - m[9] * (double)m[2]) +
                (double)m[8] * (m[1] * (double)m[6] - m[5] * (double)m[2]);
            // FLATTENED is unrecoverable; MIRRORED is not, and they were refused together.
            //
            // A zero determinant means the object's own scale collapses it onto a plane - there is no
            // geometry left to bake and no way to invent it back, so that still refuses.
            //
            // A NEGATIVE determinant just means the artist mirrored the piece, which is how a
            // symmetrical model is normally built: export the left half, mirror it for the right.
            // MEASURED on ar-181.glb, where it refused the whole rifle. The only thing a mirror
            // actually breaks is triangle winding - the faces would point inward - and reversing the
            // winding of the pieces this transform covers fixes exactly that, which is the same
            // compensation GlbReader.ToUnity already applies for its own axis flip. Refusing instead
            // sent the author to Blender to fix a file that was never wrong.
            if (determinant == 0.0)
                throw Bad("the mesh object is flattened onto a plane by its own scale, so it carries no " +
                    "thickness to bake; in Blender give the object a non-zero scale on all three axes " +
                    "and re-export");
            if (determinant < 0.0)
                for (int s = 0; s < model.Submeshes.Count; s++)
                {
                    int[] tri = model.Submeshes[s];
                    for (int t = 0; t + 2 < tri.Length; t += 3)
                    {
                        int swap = tri[t]; tri[t] = tri[t + 2]; tri[t + 2] = swap;
                    }
                }
            for (int i = 0; i < model.Positions.Length; i++)
            {
                ObjVector3 p = model.Positions[i];
                model.Positions[i] = new ObjVector3(
                    m[0] * p.X + m[4] * p.Y + m[8] * p.Z + m[12],
                    m[1] * p.X + m[5] * p.Y + m[9] * p.Z + m[13],
                    m[2] * p.X + m[6] * p.Y + m[10] * p.Z + m[14]);
            }
            // A morph target is a DELTA, so it takes the 3x3 and never the translation - a rotated or
            // scaled object whose deltas were left behind pulls every shape key the wrong way.
            foreach (SkinMorph morph in model.Morphs)
            {
                for (int i = 0; i < morph.Positions.Length; i++) morph.Positions[i] = Turn(m, morph.Positions[i]);
                if (morph.Normals == null) continue;
                for (int i = 0; i < morph.Normals.Length; i++) morph.Normals[i] = Unit(m, morph.Normals[i]);
            }
            // The tangent's w is its handedness, not a coordinate, so only xyz turn.
            if (model.Tangents != null)
                for (int i = 0; i * 4 + 3 < model.Tangents.Length; i++)
                {
                    ObjVector3 t = Unit(m, new ObjVector3(model.Tangents[i * 4], model.Tangents[i * 4 + 1], model.Tangents[i * 4 + 2]));
                    model.Tangents[i * 4] = t.X; model.Tangents[i * 4 + 1] = t.Y; model.Tangents[i * 4 + 2] = t.Z;
                }
            if (model.Normals == null) return;
            for (int i = 0; i < model.Normals.Length; i++) model.Normals[i] = Unit(m, model.Normals[i]);
        }

        /// <summary>The matrix's 3x3 applied to a direction or a delta - no translation.</summary>
        private static ObjVector3 Turn(float[] m, ObjVector3 v)
        {
            return new ObjVector3(
                m[0] * v.X + m[4] * v.Y + m[8] * v.Z,
                m[1] * v.X + m[5] * v.Y + m[9] * v.Z,
                m[2] * v.X + m[6] * v.Y + m[10] * v.Z);
        }

        /// <summary>
        /// <see cref="Turn"/>, renormalised - for a NORMAL or a tangent, which carry direction only.
        /// ponytail: the 3x3 itself instead of its inverse transpose. Exact for rotation and uniform
        /// scale, which is what an object that has not had a non-uniform scale applied ever carries;
        /// add the inverse transpose only if a skewed object shows up.
        /// </summary>
        private static ObjVector3 Unit(float[] m, ObjVector3 v)
        {
            ObjVector3 t = Turn(m, v);
            double length = Math.Sqrt((double)t.X * t.X + (double)t.Y * t.Y + (double)t.Z * t.Z);
            return length <= 1e-12 ? v : new ObjVector3((float)(t.X / length), (float)(t.Y / length), (float)(t.Z / length));
        }

        /// <summary>
        /// One parent index per node, -1 for a root. glTF states the tree as CHILDREN, so this is the
        /// single place it gets inverted - the static mesh's world matrix and the armature's parent
        /// links are the same walk and must not read the file two different ways.
        /// </summary>
        private static int[] Parents(List<object> nodes)
        {
            var parent = new int[nodes.Count];
            for (int i = 0; i < parent.Length; i++) parent[i] = -1;
            for (int i = 0; i < nodes.Count; i++)
            {
                if (!(Opt(Obj(nodes[i], "nodes"), "children") is List<object> children)) continue;
                foreach (object entry in children)
                {
                    int child = Int(entry, "nodes.children");
                    if (child < 0 || child >= nodes.Count)
                        throw Bad("the file's object tree points at node " + child.ToString(CultureInfo.InvariantCulture) +
                            " but declares " + nodes.Count.ToString(CultureInfo.InvariantCulture) + "; the file is corrupt, so re-export it");
                    if (parent[child] >= 0)
                        throw Bad("node " + child.ToString(CultureInfo.InvariantCulture) +
                            " is parented twice, which glTF forbids; re-export the file rather than editing it by hand");
                    parent[child] = i;
                }
            }
            return parent;
        }

        /// <summary>The node's world matrix, column-major, in glTF space.</summary>
        private static float[] World(List<object> nodes, int node)
        {
            int[] parent = Parents(nodes);
            float[] world = Identity();
            var chain = new List<int>();
            for (int current = node; current >= 0; current = parent[current])
            {
                if (chain.Count > nodes.Count)
                    throw Bad("the file's object tree loops back on itself; re-export the file rather than editing it by hand");
                chain.Add(current);
            }
            for (int i = chain.Count - 1; i >= 0; i--) world = Multiply(world, Local(Obj(nodes[chain[i]], "nodes")));
            for (int i = 0; i < 16; i++)
                if (float.IsNaN(world[i]) || float.IsInfinity(world[i]))
                    throw Bad("the mesh object's transform is not a real number; re-export the file rather than editing it by hand");
            return world;
        }

        private static float[] Local(Dictionary<string, object> node)
        {
            if (Opt(node, "matrix") is List<object> matrix)
            {
                if (matrix.Count != 16) throw Bad("an object's transform is not a 4x4 matrix; re-export the file rather than editing it by hand");
                var result = new float[16];
                for (int i = 0; i < 16; i++) result[i] = Single(matrix[i], "nodes.matrix");
                return result;
            }
            float[] translation = Triple(node, "translation", 0f);
            float[] scale = Triple(node, "scale", 1f);
            float[] rotation = { 0f, 0f, 0f, 1f };
            if (Opt(node, "rotation") is List<object> quaternion)
            {
                if (quaternion.Count != 4) throw Bad("an object's rotation is not a quaternion; re-export the file rather than editing it by hand");
                for (int i = 0; i < 4; i++) rotation[i] = Single(quaternion[i], "nodes.rotation");
            }
            float x = rotation[0], y = rotation[1], z = rotation[2], w = rotation[3];
            var trs = new[]
            {
                (1f - 2f * (y * y + z * z)) * scale[0], (2f * (x * y + z * w)) * scale[0], (2f * (x * z - y * w)) * scale[0], 0f,
                (2f * (x * y - z * w)) * scale[1], (1f - 2f * (x * x + z * z)) * scale[1], (2f * (y * z + x * w)) * scale[1], 0f,
                (2f * (x * z + y * w)) * scale[2], (2f * (y * z - x * w)) * scale[2], (1f - 2f * (x * x + y * y)) * scale[2], 0f,
                translation[0], translation[1], translation[2], 1f,
            };
            return trs;
        }

        private static float[] Triple(Dictionary<string, object> node, string key, float fallback)
        {
            var result = new[] { fallback, fallback, fallback };
            if (!(Opt(node, key) is List<object> values)) return result;
            if (values.Count != 3) throw Bad("an object's " + key + " is not three numbers; re-export the file rather than editing it by hand");
            for (int i = 0; i < 3; i++) result[i] = Single(values[i], "nodes." + key);
            return result;
        }

        private static float[] Identity() => new[] { 1f, 0f, 0f, 0f, 0f, 1f, 0f, 0f, 0f, 0f, 1f, 0f, 0f, 0f, 0f, 1f };

        /// <summary>A matrix that changes nothing, so folding it in is skipped and every file that
        /// carries no transform above its armature imports exactly as it always did.</summary>
        private static bool IsIdentity(float[] m)
        {
            float[] one = Identity();
            for (int i = 0; i < 16; i++) if (Math.Abs(m[i] - one[i]) > 1e-6f) return false;
            return true;
        }

        private static float[] Multiply(float[] a, float[] b)
        {
            var result = new float[16];
            for (int column = 0; column < 4; column++)
                for (int row = 0; row < 4; row++)
                {
                    float sum = 0f;
                    for (int k = 0; k < 4; k++) sum += a[k * 4 + row] * b[column * 4 + k];
                    result[column * 4 + row] = sum;
                }
            return result;
        }

        // ---------------------------------------------------------------- accessors

        private static void Same(Block existing, Block candidate, string what)
        {
            if (existing.Normal != candidate.Normal || existing.Tangent != candidate.Tangent ||
                existing.Uv0 != candidate.Uv0 || existing.Uv1 != candidate.Uv1 ||
                existing.Joints != candidate.Joints || existing.Weights != candidate.Weights ||
                existing.TargetPositions.Length != candidate.TargetPositions.Length)
                throw Bad(what + " shares its vertices with an earlier primitive but declares different vertex data; " +
                    "re-export the file rather than editing it by hand");
            for (int t = 0; t < existing.TargetPositions.Length; t++)
                if (existing.TargetPositions[t] != candidate.TargetPositions[t] || existing.TargetNormals[t] != candidate.TargetNormals[t])
                    throw Bad(what + " shares its vertices with an earlier primitive but declares different blend shapes; " +
                        "re-export the file rather than editing it by hand");
        }

        private static FormatException Mixed(string plain, string attribute) =>
            Bad("only some parts of the mesh carry " + plain + " (" + attribute + "), and a Unity mesh needs them for every vertex " +
                "or none; in Blender make the whole mesh consistent and re-export");

        /// <summary>
        /// One KHR_draco_mesh_compression primitive: decode its bufferView once and register the
        /// values under the accessor indices the primitive already names, so nothing downstream knows
        /// the file was compressed.
        ///
        /// The extension's Conformance section is followed literally. The compressed block's
        /// <c>attributes</c> map is glTF attribute name -&gt; the attribute's UNIQUE ID inside the
        /// Draco stream, and it "must be a subset of the attributes of the primitive": an attribute
        /// the primitive declares and the block does not is left alone and read from its own
        /// bufferView, which is exactly what the last Conformance bullet asks for.
        /// </summary>
        private void Decompress(Dictionary<string, object> block, Dictionary<string, object> primitive, string what)
        {
            string at = what + "." + Draco.Extension;
            int viewIndex = Int(Get(block, "bufferView"), at + ".bufferView");
            List<object> views = Array_(Opt(root, "bufferViews"), "bufferViews");
            if (viewIndex < 0 || viewIndex >= views.Count)
                throw Bad(at + " points at data block " + viewIndex.ToString(CultureInfo.InvariantCulture) +
                    " but the file declares " + views.Count.ToString(CultureInfo.InvariantCulture) +
                    "; the file is corrupt, so re-export it");
            int viewLength = Int(Get(Obj(views[viewIndex], "bufferViews[" + viewIndex + "]"), "byteLength"),
                "bufferViews[" + viewIndex + "].byteLength");
            // Resolved through the SAME bounds check every other accessor uses, one byte at a time, so
            // a compressed block that reaches past the BIN chunk is refused there and not here.
            Resolve(viewIndex, 0, 1, 1, viewLength, at, out byte[] data, out int start, out int _);
            expandedBytes += viewLength;
            if (expandedBytes > MaxDecodedBytes)
                throw Bad("the file's compressed data expands past the " + Mb(MaxDecodedBytes) +
                    " limit this mod will hold; decimate the mesh or drop its blend shapes and re-export");

            Draco.Model model = Draco.Decode(data, start, viewLength, at);
            Dictionary<string, object> names = Obj(Get(block, "attributes"), at + ".attributes");
            Dictionary<string, object> attributes = Obj(Get(primitive, "attributes"), what + ".attributes");
            foreach (string name in names.Keys)
            {
                object accessor = Opt(attributes, name);
                if (accessor == null)
                    throw Bad(at + " decompresses '" + name + "', which " + what +
                        " does not declare; the file is corrupt, so download or export it again");
                int id = Int(Get(names, name), at + ".attributes." + name);
                Draco.Attribute found = null;
                foreach (Draco.Attribute candidate in model.Attributes)
                    if (candidate.UniqueId == id) { found = candidate; break; }
                if (found == null)
                    throw Bad(at + " names '" + name + "' as compressed attribute " +
                        id.ToString(CultureInfo.InvariantCulture) +
                        ", which its own compressed data does not hold; the file is corrupt, so download " +
                        "or export it again");
                Matches(found, Accessor(Int(accessor, what + ".attributes." + name), at + " '" + name + "'"),
                    name, at + " '" + name + "'");
                decoded[Int(accessor, what + ".attributes." + name)] = found.Values;
            }

            object indices = Opt(primitive, "indices");
            if (indices == null)
                throw Bad(what + " has no index buffer, and this mod only reads indexed triangles; " +
                    "re-export the file rather than editing it by hand");
            var triangles = new float[model.Indices.Length];
            for (int i = 0; i < triangles.Length; i++) triangles[i] = model.Indices[i];
            decoded[Int(indices, what + ".indices")] = triangles;
        }

        /// <summary>
        /// The extension's Conformance section: "The <c>accessors</c> properties corresponding to the
        /// <c>attributes</c> and <c>indices</c> of the <c>primitives</c> must match the decompressed
        /// data." PropertIES, plural - the value COUNT alone is not the requirement, and checking only
        /// that lets a mapping which is wrong but length-compatible through: POSITION pointed at some
        /// other three-component attribute reads as WRONG GEOMETRY rather than a format error, because
        /// nothing downstream can tell the difference. So the accessor's own <c>type</c>,
        /// <c>componentType</c> and <c>normalized</c> are asserted against what the Draco stream says
        /// its attribute is.
        ///
        /// INDICES are deliberately not put through here: Draco's index buffer is always 32-bit
        /// internally while all 263 real Draco primitives measured for U12 declare their index
        /// accessor UNSIGNED_SHORT, so the only property that can be compared for them is the count -
        /// which <see cref="Elements"/> already does for every accessor Draco overrides.
        /// </summary>
        private static void Matches(Draco.Attribute attribute, Dictionary<string, object> accessor,
            string name, string at)
        {
            // The length-compatible swap the properties above CANNOT see: POSITION and NORMAL are both
            // VEC3/FLOAT and both hold one value per point, so a mapping that points POSITION at the
            // normals passes every check below and yields wrong geometry. What separates them is the
            // stream's own GeometryAttribute::Type.
            if (!Draco.TypeFits(attribute.Type, name))
                throw Bad(at + " is mapped to compressed data the file itself calls " +
                    Draco.TypeName(attribute.Type) + "; the file names the wrong compressed attribute, " +
                    "so download or export it again");

            string type = Str(Get(accessor, "type"), at + ".type");
            int components = type == "SCALAR" ? 1 : type == "VEC2" ? 2 : type == "VEC3" ? 3 : type == "VEC4" ? 4 : 16;
            if (components != attribute.Components)
                throw Bad(at + " is declared " + type + " but the file's compressed data holds " +
                    attribute.Components.ToString(CultureInfo.InvariantCulture) +
                    " numbers per vertex for it; the file names the wrong compressed attribute, so " +
                    "download or export it again");

            int component = Int(Get(accessor, "componentType"), at + ".componentType");
            int stated = Draco.ComponentType(attribute.DataType);
            if (stated == 0)
                throw Bad(at + " is compressed as a number format glTF cannot name (Draco type " +
                    attribute.DataType.ToString(CultureInfo.InvariantCulture) +
                    "); export the model again with an up-to-date tool");
            if (component != stated)
                throw Bad(at + " is declared as number format " + component.ToString(CultureInfo.InvariantCulture) +
                    " but the file's compressed data stores it as " +
                    stated.ToString(CultureInfo.InvariantCulture) +
                    "; the file names the wrong compressed attribute, so download or export it again");

            bool normalized = Opt(accessor, "normalized") is bool flag && flag;
            if (normalized != attribute.Normalized)
                throw Bad(at + (normalized ? " is declared as normalized whole numbers but the file's " +
                    "compressed data is not" : " is declared as plain numbers but the file's compressed " +
                    "data is normalized") + "; the values would come out scaled wrongly, so download or " +
                    "export the file again");
        }

        private static int Attribute_(Dictionary<string, object> attributes, string name, string what, bool required)
        {
            object value = Opt(attributes, name);
            if (value != null) return Int(value, what + "." + name);
            if (!required) return -1;
            throw Bad(what + " has no " + name + ", so it carries no geometry; re-export the mesh from Blender");
        }

        private Dictionary<string, object> Accessor(int index, string what)
        {
            List<object> accessors = Array_(Opt(root, "accessors"), "accessors");
            if (accessors.Count > MaxCollection)
                throw Bad("the file declares " + accessors.Count.ToString(CultureInfo.InvariantCulture) +
                    " accessors, past the " + MaxCollection.ToString(CultureInfo.InvariantCulture) + " limit; simplify the scene and re-export");
            if (index < 0 || index >= accessors.Count)
                throw Bad(what + " points at data block " + index.ToString(CultureInfo.InvariantCulture) + " but the file declares " +
                    accessors.Count.ToString(CultureInfo.InvariantCulture) + "; the file is corrupt, so re-export it");
            return Obj(accessors[index], what);
        }

        private int Count(int index, string what) => Int(Get(Accessor(index, what), "count"), what + ".count");

        private static int Size(int component, string what)
        {
            switch (component)
            {
                case Gltf.Byte: case Gltf.UnsignedByte: return 1;
                case Gltf.Short: case Gltf.UnsignedShort: return 2;
                case Gltf.UnsignedInt: case Gltf.Float: return 4;
                default:
                    throw Bad(what + " uses number format " + component.ToString(CultureInfo.InvariantCulture) +
                        ", which glTF does not define; the file is corrupt, so re-export it");
            }
        }

        /// <summary>
        /// Resolves a bufferView plus an offset down to "read <paramref name="data"/> from
        /// <paramref name="start"/>, step <paramref name="stride"/> bytes". Every bound is computed in
        /// long arithmetic and checked against the bytes that are actually present, so no length the
        /// file merely claims is ever trusted.
        ///
        /// This is also the ONE place EXT_meshopt_compression lands. A compressed view names a
        /// FALLBACK buffer that holds nothing (the extension's own "Fallback buffers" section: a
        /// placeholder with no uri, whose byteLength is only big enough to describe the uncompressed
        /// layout) and carries the real bytes in its extension block instead. Expanding it here means
        /// every accessor path above - interleaved, normalized, sparse, animation - reads a compressed
        /// file with no arm of its own, and no second set of bounds checks that could disagree.
        /// </summary>
        private void Resolve(int viewIndex, int accessorOffset, int element, int size, int count, string what,
            out byte[] data, out int start, out int stride)
        {
            List<object> views = Array_(Opt(root, "bufferViews"), "bufferViews");
            if (views.Count > MaxCollection)
                throw Bad("the file declares " + views.Count.ToString(CultureInfo.InvariantCulture) +
                    " data blocks, past the " + MaxCollection.ToString(CultureInfo.InvariantCulture) + " limit; simplify the scene and re-export");
            if (viewIndex < 0 || viewIndex >= views.Count)
                throw Bad(what + " points at data block " + viewIndex.ToString(CultureInfo.InvariantCulture) + " but the file declares " +
                    views.Count.ToString(CultureInfo.InvariantCulture) + "; the file is corrupt, so re-export it");
            Dictionary<string, object> buffer = Obj(views[viewIndex], "bufferViews[" + viewIndex + "]");
            int viewLength = Int(Get(buffer, "byteLength"), "bufferViews[" + viewIndex + "].byteLength");
            data = Expand(viewIndex, buffer, viewLength);
            int viewOffset;
            if (data != null) viewOffset = 0;           // an expanded view IS its own buffer, from byte 0
            else
            {
                data = bin;
                object which = Opt(buffer, "buffer");
                if (which != null && Int(which, "bufferViews.buffer") != 0)
                    throw Bad(what + " reads from a second buffer, and a .glb has one; re-export with Format set to 'glTF Binary (.glb)'");
                viewOffset = Offset(buffer, "bufferViews[" + viewIndex + "]");
                if (viewLength < 0 || (long)viewOffset + viewLength > bin.Length)
                    throw Bad(what + " reads bytes " + viewOffset.ToString(CultureInfo.InvariantCulture) + " to " +
                        ((long)viewOffset + viewLength).ToString(CultureInfo.InvariantCulture) + " of a " +
                        bin.Length.ToString(CultureInfo.InvariantCulture) + " byte buffer; the file is truncated, so copy or re-export it again");
            }

            stride = element;
            object declared = Opt(buffer, "byteStride");
            if (declared != null)
            {
                stride = Int(declared, "bufferViews.byteStride");
                if (stride < 4 || stride > 252 || (stride & 3) != 0)
                    throw Bad(what + " declares a spacing of " + stride.ToString(CultureInfo.InvariantCulture) +
                        " bytes, which glTF does not allow; re-export the file rather than editing it by hand");
                if (stride < element)
                    throw Bad(what + " declares a spacing of " + stride.ToString(CultureInfo.InvariantCulture) +
                        " bytes for values " + element.ToString(CultureInfo.InvariantCulture) +
                        " bytes wide, so its values overlap; the file is corrupt, so re-export it");
            }
            // An empty accessor still has to START inside its data block: leaving accessorOffset
            // unchecked lets it name a byte past the end, and the alignment refusal below would then
            // blame the number format for what is really an out-of-range offset.
            long last = count == 0 ? accessorOffset : (long)accessorOffset + (long)(count - 1) * stride + element;
            if (last > viewLength)
                throw Bad(what + " needs " + last.ToString(CultureInfo.InvariantCulture) + " bytes but its data block holds " +
                    viewLength.ToString(CultureInfo.InvariantCulture) + "; the file is corrupt, so re-export it");
            start = viewOffset + accessorOffset;
            if (start % size != 0)
                throw Bad(what + " starts at byte " + start.ToString(CultureInfo.InvariantCulture) +
                    ", which its number format cannot be read from; re-export the file rather than editing it by hand");
        }

        /// <summary>
        /// One bufferView's EXPANDED bytes when it is compressed, null when it is stored plainly.
        /// The extension requires <c>byteLength == byteStride * count</c> and that a declared parent
        /// <c>byteStride</c> match the extension's own, so both are asserted here rather than trusted:
        /// they are the sizes every bound above is then computed from.
        /// </summary>
        private byte[] Expand(int viewIndex, Dictionary<string, object> view, int viewLength)
        {
            byte[] cached;
            if (expanded.TryGetValue(viewIndex, out cached)) return cached;
            if (!(Opt(view, "extensions") is Dictionary<string, object> extensions) || extensions.Count == 0) return null;
            string at = "bufferViews[" + viewIndex.ToString(CultureInfo.InvariantCulture) + "]";
            object entry = Opt(extensions, Meshopt.Extension);
            if (entry == null)
                foreach (string key in extensions.Keys) throw Unreadable(key);
            Dictionary<string, object> block = Obj(entry, at + "." + Meshopt.Extension);

            if (Int(Get(block, "buffer"), at + ".buffer") != 0)
                throw Bad(at + "'s compressed data lives outside the .glb's own binary chunk; re-export with " +
                    "Format set to 'glTF Binary (.glb)' so everything travels in the one file you copy");
            int count = Int(Get(block, "count"), at + ".count");
            int stride = Int(Get(block, "byteStride"), at + ".byteStride");
            if (count < 0 || stride < 1 || (long)count * stride != viewLength)
                throw Bad(at + " says its compressed data expands to " + count.ToString(CultureInfo.InvariantCulture) +
                    " x " + stride.ToString(CultureInfo.InvariantCulture) + " bytes but declares " +
                    viewLength.ToString(CultureInfo.InvariantCulture) + "; the file is corrupt, so download or export it again");
            object declared = Opt(view, "byteStride");
            if (declared != null && Int(declared, at + ".byteStride") != stride)
                throw Bad(at + " declares two different spacings for the same data; the file is corrupt, so " +
                    "download or export it again");
            expandedBytes += viewLength;
            if (expandedBytes > MaxDecodedBytes)
                throw Bad("the file's compressed data expands past the " + Mb(MaxDecodedBytes) +
                    " limit this mod will hold; decimate the mesh or drop its blend shapes and re-export");

            int offset = Offset(block, at + "." + Meshopt.Extension);
            int length = Int(Get(block, "byteLength"), at + ".byteLength");
            object filter = Opt(block, "filter");
            byte[] result = Meshopt.Decode(bin, offset, length, count, stride,
                Str(Get(block, "mode"), at + ".mode"), filter == null ? null : Str(filter, at + ".filter"), at);
            expanded[viewIndex] = result;
            return result;
        }

        private static int Offset(Dictionary<string, object> value, string what)
        {
            object offset = Opt(value, "byteOffset");
            if (offset == null) return 0;
            int result = Int(offset, what + ".byteOffset");
            if (result < 0)
                throw Bad(what + " starts at a negative offset; the file is corrupt, so re-export it");
            return result;
        }

        /// <summary>
        /// One whole accessor as floats, with every glTF storage form Blender's exporter can produce:
        /// a plain bufferView, an interleaved one with a byteStride, normalized integers, and the
        /// SPARSE form Blender writes for shape keys BY DEFAULT (export_try_sparse_sk is on), where
        /// the accessor may carry no bufferView at all and the deltas arrive as index/value pairs.
        /// An accessor with neither a bufferView nor a sparse block reads as zeroes, which is what
        /// glTF says it means and what an all-zero shape key exports as.
        /// </summary>
        private float[] Elements(int index, string what, string type, int expected, bool zeroable,
            out int count, out int components, out int component)
        {
            Dictionary<string, object> accessor = Accessor(index, what);
            string actual = Str(Get(accessor, "type"), what + ".type");
            if (actual != type)
                throw Bad(what + " is stored as " + actual + " but must be " + type + "; the file is corrupt, so re-export it");
            components = type == "SCALAR" ? 1 : type == "VEC2" ? 2 : type == "VEC3" ? 3 : type == "VEC4" ? 4 : 16;
            component = Int(Get(accessor, "componentType"), what + ".componentType");
            int size = Size(component, what);
            count = Int(Get(accessor, "count"), what + ".count");
            if (count < 0 || count > MaxIndices)
                throw Bad(what + " declares " + count.ToString(CultureInfo.InvariantCulture) +
                    " values, past the limit this mod will read; decimate the mesh in Blender and re-export");
            if (expected >= 0 && count != expected)
                throw Bad(what + " holds " + count.ToString(CultureInfo.InvariantCulture) + " values but " +
                    expected.ToString(CultureInfo.InvariantCulture) + " are needed; the file is corrupt, so re-export it");
            bool normalized = Opt(accessor, "normalized") is bool flag && flag;
            if ((long)count * components > MaxIndices * 4L)
                throw Bad(what + " is too large to read; decimate the mesh in Blender and re-export");

            // Draco supplies the VALUES and nothing else: the accessor's own type, componentType and
            // count are still the ones every guard above just checked, and the extension REQUIRES them
            // to match the decompressed data ("the accessors properties ... must match the
            // decompressed data"), which is asserted here rather than trusted.
            float[] fromDraco;
            if (decoded.TryGetValue(index, out fromDraco))
            {
                if (fromDraco.Length != count * components)
                    throw Bad(what + " declares " + count.ToString(CultureInfo.InvariantCulture) + " x " +
                        components.ToString(CultureInfo.InvariantCulture) + " values but the file's Draco " +
                        "data holds " + fromDraco.Length.ToString(CultureInfo.InvariantCulture) +
                        "; the file is corrupt, so download or export it again");
                return fromDraco;
            }

            // The buffer is resolved BEFORE the array is allocated, so a file that merely claims six
            // million values cannot make the game allocate for them: the bounds check fails first.
            object view = Opt(accessor, "bufferView");
            int start = 0, stride = 0;
            byte[] data = null;
            if (view != null)
                Resolve(Int(view, what + ".bufferView"), Offset(accessor, what), components * size, size, count, what,
                    out data, out start, out stride);
            var result = new float[count * components];
            if (view != null)
                for (int i = 0; i < count; i++)
                    for (int c = 0; c < components; c++)
                        result[i * components + c] = Value(data, start + i * stride, c, component, normalized, what);
            if (Opt(accessor, "sparse") is Dictionary<string, object> sparse)
                Sparse(sparse, result, count, components, component, size, normalized, what);
            else if (view == null && count > 0 && !zeroable)
                throw Bad(what + " has no data block of its own, so it would read as all zeroes; re-export the file from Blender");
            return result;
        }

        private void Sparse(Dictionary<string, object> sparse, float[] result, int count, int components,
            int component, int size, bool normalized, string what)
        {
            int changed = Int(Get(sparse, "count"), what + ".sparse.count");
            if (changed < 1 || changed > count)
                throw Bad(what + " changes " + changed.ToString(CultureInfo.InvariantCulture) + " of its " +
                    count.ToString(CultureInfo.InvariantCulture) + " values, which is not possible; the file is corrupt, so re-export it");

            Dictionary<string, object> indices = Obj(Get(sparse, "indices"), what + ".sparse.indices");
            int indexComponent = Int(Get(indices, "componentType"), what + ".sparse.indices.componentType");
            if (indexComponent != Gltf.UnsignedByte && indexComponent != Gltf.UnsignedShort && indexComponent != Gltf.UnsignedInt)
                throw Bad(what + " numbers its changed values with format " + indexComponent.ToString(CultureInfo.InvariantCulture) +
                    ", which glTF does not allow there; the file is corrupt, so re-export it");
            int indexSize = Size(indexComponent, what);
            Resolve(Int(Get(indices, "bufferView"), what + ".sparse.indices.bufferView"), Offset(indices, what + ".sparse.indices"),
                indexSize, indexSize, changed, what + " changed positions", out byte[] indexData, out int indexStart, out int indexStride);

            Dictionary<string, object> values = Obj(Get(sparse, "values"), what + ".sparse.values");
            Resolve(Int(Get(values, "bufferView"), what + ".sparse.values.bufferView"), Offset(values, what + ".sparse.values"),
                components * size, size, changed, what + " changed values", out byte[] valueData, out int valueStart, out int valueStride);

            int previous = -1;
            for (int k = 0; k < changed; k++)
            {
                int target = (int)Value(indexData, indexStart + k * indexStride, 0, indexComponent, false, what);
                if (target < 0 || target >= count)
                    throw Bad(what + " changes value " + target.ToString(CultureInfo.InvariantCulture) + " of " +
                        count.ToString(CultureInfo.InvariantCulture) + "; the file is corrupt, so re-export it");
                if (target <= previous)
                    throw Bad(what + " lists its changed values out of order, which glTF forbids; the file is corrupt, so re-export it");
                previous = target;
                for (int c = 0; c < components; c++)
                    result[target * components + c] = Value(valueData, valueStart + k * valueStride, c, component, normalized, what);
            }
        }

        private float[] Floats(int index, string what, string type, int expected) =>
            Floats(index, what, type, expected, false);

        private float[] Floats(int index, string what, string type, int expected, bool zeroable) =>
            Elements(index, what, type, expected, zeroable, out int _, out int _, out int _);

        private int[] Integers(int index, string what, string type, int expected, bool indices)
        {
            float[] values = Elements(index, what, type, expected, false, out int _, out int _, out int component);
            if (indices && (component == Gltf.Byte || component == Gltf.Short || component == Gltf.Float))
                throw Bad(what + " stores its triangle indices as a signed or fractional number, which glTF forbids; " +
                    "re-export the file from Blender");
            if (!indices && (component == Gltf.Float || component == Gltf.UnsignedInt))
                throw Bad(what + " stores its bone numbers as fractions or 32-bit numbers, and glTF allows only unsigned " +
                    "byte or unsigned short there; re-export the file from Blender");
            var result = new int[values.Length];
            for (int i = 0; i < values.Length; i++)
            {
                if (values[i] < 0f || values[i] > int.MaxValue)
                    throw Bad(what + " holds the out-of-range value " + values[i].ToString("R", CultureInfo.InvariantCulture) +
                        "; the file is corrupt, so re-export it");
                result[i] = (int)values[i];
            }
            return result;
        }

        private void Vec3(int index, int at, int count, ObjVector3[] target, string what, bool zeroable)
        {
            float[] values = Floats(index, what, "VEC3", count, zeroable);
            for (int i = 0; i < count; i++)
            {
                Finite(values[i * 3], values[i * 3 + 1], values[i * 3 + 2], what, at + i);
                target[at + i] = new ObjVector3(values[i * 3], values[i * 3 + 1], values[i * 3 + 2]);
            }
        }

        private void Vec2(int index, int at, int count, ObjVector2[] target, string what)
        {
            float[] values = Floats(index, what, "VEC2", count);
            for (int i = 0; i < count; i++)
            {
                Finite(values[i * 2], values[i * 2 + 1], 0f, what, at + i);
                target[at + i] = new ObjVector2(values[i * 2], values[i * 2 + 1]);
            }
        }

        private void Vec4(int index, int at, int count, float[] target, string what)
        {
            float[] values = Floats(index, what, "VEC4", count);
            for (int i = 0; i < count * 4; i++)
            {
                Finite(values[i], 0f, 0f, what, at + i / 4);
                target[at * 4 + i] = values[i];
            }
        }

        private static void Finite(float x, float y, float z, string what, int vertex)
        {
            if (float.IsNaN(x) || float.IsInfinity(x) || float.IsNaN(y) || float.IsInfinity(y) || float.IsNaN(z) || float.IsInfinity(z))
                throw Bad("vertex " + vertex.ToString(CultureInfo.InvariantCulture) + " has a " + what +
                    " value that is not a number; the file is corrupt, so re-export it");
        }

        /// <summary>
        /// One component, out of whichever bytes its bufferView resolved to. The normalized forms are
        /// the ones KHR_mesh_quantization's own "Decoding Quantized Data" table spells - <c>c/127</c>,
        /// <c>c/255</c>, <c>c/32767</c>, <c>c/65535</c>, with the signed pair clamped at -1 - so a
        /// quantized attribute needs no arm of its own here beyond not being refused.
        /// </summary>
        private static float Value(byte[] data, int at, int component, int format, bool normalized, string what)
        {
            switch (format)
            {
                case Gltf.Float: return BitConverter.ToSingle(data, at + component * 4);
                case Gltf.UnsignedInt: return BitConverter.ToUInt32(data, at + component * 4);
                case Gltf.UnsignedShort: return normalized ? BitConverter.ToUInt16(data, at + component * 2) / 65535f : BitConverter.ToUInt16(data, at + component * 2);
                case Gltf.Short: return normalized ? Math.Max(BitConverter.ToInt16(data, at + component * 2) / 32767f, -1f) : BitConverter.ToInt16(data, at + component * 2);
                case Gltf.UnsignedByte: return normalized ? data[at + component] / 255f : data[at + component];
                case Gltf.Byte: return normalized ? Math.Max((sbyte)data[at + component] / 127f, -1f) : (sbyte)data[at + component];
                default:
                    throw Bad(what + " uses number format " + format.ToString(CultureInfo.InvariantCulture) +
                        ", which glTF does not define; the file is corrupt, so re-export it");
            }
        }

        // ---------------------------------------------------------------- json helpers

        private static object Get(Dictionary<string, object> value, string key)
        {
            if (value.TryGetValue(key, out object result) && result != null) return result;
            throw Bad("the file's description has no '" + key + "', so it is not a complete glTF; re-export it from Blender");
        }

        private static object Opt(Dictionary<string, object> value, string key) =>
            value.TryGetValue(key, out object result) ? result : null;

        private static Dictionary<string, object> Obj(object value, string what) =>
            value as Dictionary<string, object> ??
            throw Bad("the file's " + what + " is not described as an object; re-export it rather than editing it by hand");

        private static List<object> Array_(object value, string what)
        {
            if (value == null) return new List<object>();
            return value as List<object> ??
                throw Bad("the file's " + what + " is not described as a list; re-export it rather than editing it by hand");
        }

        private static string Str(object value, string what) =>
            value as string ?? throw Bad("the file's " + what + " is not text; re-export it rather than editing it by hand");

        private static int Int(object value, string what)
        {
            if (!(value is double number) || double.IsNaN(number) || number < int.MinValue || number > int.MaxValue || number != Math.Floor(number))
                throw Bad("the file's " + what + " is not a whole number; re-export it rather than editing it by hand");
            return (int)number;
        }

        private static float Single(object value, string what)
        {
            if (!(value is double number) || double.IsNaN(number) || double.IsInfinity(number))
                throw Bad("the file's " + what + " is not a number; re-export it rather than editing it by hand");
            return (float)number;
        }

        private static uint U32(byte[] file, int at) => BitConverter.ToUInt32(file, at);

        private static string Mb(long bytes) =>
            (bytes / (1024.0 * 1024.0)).ToString("0.#", CultureInfo.InvariantCulture) + " MB";

        /// <summary>
        /// A glTF extension this reader cannot honour, refused BY NAME with what it means and the
        /// concrete thing to do in a 3D editor. "Unsupported format" is not an acceptable answer to an
        /// author who downloaded a file and did nothing wrong, so each ceiling that remains is spelled
        /// out where they will actually meet it.
        /// </summary>
        private static FormatException Unreadable(string name)
        {
            if (name == Draco.Extension)
                return Bad("the file's geometry is packed with Draco compression ('" + Draco.Extension +
                    "'), a codec this mod does not carry - its decoder is far larger than everything else the " +
                    "importer does. Open the file in Blender (File > Import > glTF 2.0, which reads Draco) and " +
                    "export it again with File > Export > glTF 2.0, Format 'glTF Binary (.glb)' and the " +
                    "Compression box UNTICKED. Meshopt compression (EXT_meshopt_compression) and quantized " +
                    "attributes (KHR_mesh_quantization), which most model sites ship, are read directly and " +
                    "need no such step");
            if (name == TextureTransform)
                return Bad("the file needs '" + TextureTransform + "' to make sense of its texture coordinates, " +
                    "which means its UVs are stored as whole numbers that only its own material knows how to " +
                    "scale back. This mod paints a model from Meshes\\materials\\<name>.mat.json instead of the " +
                    "file's materials, so it has nowhere to read that scale from. In Blender import the file, " +
                    "then export it again with Geometry > 'Export Original PBR Specular Glossiness'/compression " +
                    "options left off, so the UVs come out as plain numbers");
            return Bad("the file requires the glTF extension '" + name +
                "', which this mod does not read. Import it into Blender and export it again with File > " +
                "Export > glTF 2.0, Format 'glTF Binary (.glb)', with the Compression box unticked and any " +
                "extension add-on turned off; that writes a plain file this mod reads");
        }

        private static FormatException Bad(string message) => new FormatException(message);
    }

    /// <summary>
    /// Just enough JSON to read a glTF chunk, the mirror of <see cref="JsonWriter"/>. Total: every
    /// malformed input leaves as a <see cref="FormatException"/> naming the offset, and the nesting
    /// depth is capped so a file made of ten thousand open brackets cannot overflow the stack.
    /// </summary>
    internal static class Json
    {
        internal static object Parse(string text, int maxDepth)
        {
            if (text == null) throw Fail(0, "there is nothing to read");
            int at = 0;
            object value = Read(text, ref at, 0, maxDepth);
            Space(text, ref at);
            if (at != text.Length) throw Fail(at, "there is leftover text after the end of the description");
            return value;
        }

        private static object Read(string text, ref int at, int depth, int maxDepth)
        {
            if (depth > maxDepth) throw Fail(at, "the description nests deeper than " + maxDepth + " levels");
            Space(text, ref at);
            if (at >= text.Length) throw Fail(at, "the description ends early");
            char c = text[at];
            if (c == '{')
            {
                var result = new Dictionary<string, object>(StringComparer.Ordinal);
                at++;
                Space(text, ref at);
                if (Peek(text, at) == '}') { at++; return result; }
                while (true)
                {
                    Space(text, ref at);
                    if (Peek(text, at) != '"') throw Fail(at, "a name was expected");
                    string key = Text(text, ref at);
                    Space(text, ref at);
                    if (Peek(text, at) != ':') throw Fail(at, "a ':' was expected");
                    at++;
                    result[key] = Read(text, ref at, depth + 1, maxDepth);
                    Space(text, ref at);
                    char next = Peek(text, at);
                    at++;
                    if (next == ',') continue;
                    if (next == '}') return result;
                    throw Fail(at - 1, "a ',' or '}' was expected");
                }
            }
            if (c == '[')
            {
                var result = new List<object>();
                at++;
                Space(text, ref at);
                if (Peek(text, at) == ']') { at++; return result; }
                while (true)
                {
                    result.Add(Read(text, ref at, depth + 1, maxDepth));
                    Space(text, ref at);
                    char next = Peek(text, at);
                    at++;
                    if (next == ',') continue;
                    if (next == ']') return result;
                    throw Fail(at - 1, "a ',' or ']' was expected");
                }
            }
            if (c == '"') return Text(text, ref at);
            if (Literal(text, ref at, "true")) return true;
            if (Literal(text, ref at, "false")) return false;
            if (Literal(text, ref at, "null")) return null;
            return Number(text, ref at);
        }

        private static char Peek(string text, int at) => at < text.Length ? text[at] : '\0';

        private static void Space(string text, ref int at)
        {
            while (at < text.Length && (text[at] == ' ' || text[at] == '\t' || text[at] == '\n' || text[at] == '\r')) at++;
        }

        private static bool Literal(string text, ref int at, string word)
        {
            if (at + word.Length > text.Length || string.CompareOrdinal(text, at, word, 0, word.Length) != 0) return false;
            at += word.Length;
            return true;
        }

        private static object Number(string text, ref int at)
        {
            int start = at;
            if (Peek(text, at) == '-') at++;
            while (at < text.Length && ((text[at] >= '0' && text[at] <= '9') || text[at] == '.' ||
                   text[at] == 'e' || text[at] == 'E' || text[at] == '+' || text[at] == '-')) at++;
            if (at == start) throw Fail(start, "a value was expected");
            if (!double.TryParse(text.Substring(start, at - start), NumberStyles.Float, CultureInfo.InvariantCulture, out double value) ||
                double.IsNaN(value) || double.IsInfinity(value))
                throw Fail(start, "'" + text.Substring(start, at - start) + "' is not a finite number");
            return value;
        }

        private static string Text(string text, ref int at)
        {
            at++;                                   // the opening quote, already peeked
            var result = new StringBuilder();
            while (true)
            {
                if (at >= text.Length) throw Fail(at, "a piece of text is never closed");
                char c = text[at++];
                if (c == '"') return result.ToString();
                if (c != '\\') { result.Append(c); continue; }
                if (at >= text.Length) throw Fail(at, "a piece of text ends in an escape");
                char escape = text[at++];
                switch (escape)
                {
                    case '"': result.Append('"'); break;
                    case '\\': result.Append('\\'); break;
                    case '/': result.Append('/'); break;
                    case 'b': result.Append('\b'); break;
                    case 'f': result.Append('\f'); break;
                    case 'n': result.Append('\n'); break;
                    case 'r': result.Append('\r'); break;
                    case 't': result.Append('\t'); break;
                    case 'u':
                        if (at + 4 > text.Length) throw Fail(at, "an escape ends early");
                        if (!int.TryParse(text.Substring(at, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int code))
                            throw Fail(at, "'" + text.Substring(at, 4) + "' is not a character code");
                        result.Append((char)code);
                        at += 4;
                        break;
                    default: throw Fail(at - 1, "'\\" + escape + "' is not an escape JSON knows");
                }
            }
        }

        private static FormatException Fail(int at, string cause) =>
            new FormatException("the file's description is malformed at character " +
                at.ToString(CultureInfo.InvariantCulture) + ": " + cause + "; re-export it rather than editing it by hand");
    }

    /// <summary>
    /// Binds an imported model onto a LIVE rig without touching that rig. The skeleton, its bones,
    /// its Avatar and its animations are exactly what the game shipped; only the geometry is swapped,
    /// so every joint in the file must name a bone the renderer already has and vice versa.
    ///
    /// The two maps below run in OPPOSITE directions and that is the whole subtlety: a vertex carries
    /// a FILE slot and must end up with a LIVE index, while a bind pose is indexed by LIVE bone and
    /// must be fetched from the FILE slot. One map used for both silently transposes the rig whenever
    /// the orders differ, which is why the self-check uses a fixture whose orders do differ.
    /// </summary>
    internal static class SkinBinder
    {
        /// <summary>
        /// Validates everything, then hands back the vertex joint indices in the live rig's order and
        /// the bind poses in the live rig's order. Throws before producing anything if the file and
        /// the target disagree, so a caller can build a mesh only once this has returned.
        /// </summary>
        internal static void Bind(SkinnedModel file, IList<string> boneNames, int materialSlots,
            IList<string> blendShapeNames, out ushort[] joints, out float[][] bindposes)
        {
            if (file == null) throw new ArgumentNullException(nameof(file));
            if (boneNames == null || boneNames.Count == 0)
                throw new FormatException("the target model lists no bones, so there is no skeleton to bind onto; " +
                    "reload the scene and try again");
            if (file.JointNames.Count == 0)
                throw new FormatException("the file carries no armature, so it cannot replace a rigged model; " +
                    "in Blender export the mesh together with its armature, or put the file on a static object instead");
            if (file.Joints == null || file.Weights == null || file.Positions == null ||
                file.Joints.Length != file.Positions.Length * 4 || file.Weights.Length != file.Joints.Length)
                throw new FormatException("the file's bone weights do not cover every vertex; " +
                    "in Blender give the whole mesh an Armature modifier with vertex groups and re-export");

            Submeshes(file, materialSlots);
            Shapes(file, blendShapeNames);

            var liveOf = new int[file.JointNames.Count];
            var fileOf = new int[boneNames.Count];
            for (int i = 0; i < liveOf.Length; i++) liveOf[i] = -1;
            for (int i = 0; i < fileOf.Length; i++) fileOf[i] = -1;

            var live = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int i = 0; i < boneNames.Count; i++)
            {
                if (string.IsNullOrEmpty(boneNames[i]))
                    throw new FormatException("the target model's bone " + i.ToString(CultureInfo.InvariantCulture) +
                        " has no name, so nothing in the file can be matched to it; reload the scene and try again");
                if (live.ContainsKey(boneNames[i]))
                    throw new FormatException("the target model has two bones named '" + boneNames[i] +
                        "', so a bone in the file cannot be matched to one of them; this model cannot be replaced by name");
                live[boneNames[i]] = i;
            }
            var seen = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int j = 0; j < file.JointNames.Count; j++)
            {
                if (seen.ContainsKey(file.JointNames[j]))
                    throw new FormatException("the file has two bones named '" + file.JointNames[j] +
                        "'; rename one of them in Blender so every bone name is unique, then re-export");
                seen[file.JointNames[j]] = j;
            }

            // Every live bone must be in the file. This is the one that breaks deformation, so it is
            // reported first and by name.
            for (int i = 0; i < boneNames.Count; i++)
            {
                if (!seen.TryGetValue(boneNames[i], out int j))
                    throw new FormatException("the file does not contain the bone '" + boneNames[i] +
                        "', which this model's skeleton has; the skeleton is never replaced, so in Blender keep the imported " +
                        "armature exactly as it came, with every bone and its name unchanged, and re-export");
                fileOf[i] = j;
                liveOf[j] = i;
            }
            for (int j = 0; j < file.JointNames.Count; j++)
                if (liveOf[j] < 0)
                    throw new FormatException("the file adds the bone '" + file.JointNames[j] +
                        "', which this model's skeleton does not have; the skeleton is never replaced, so delete the added bone " +
                        "in Blender and re-export");
            for (int i = 0; i < fileOf.Length; i++)
                if (liveOf[fileOf[i]] != i)
                    throw new FormatException("the file's bones could not be matched one to one onto this model's skeleton; " +
                        "re-export from the model this mod dumped, without adding, removing or renaming bones");

            if (file.InverseBindMatrices == null || file.InverseBindMatrices.Length != file.JointNames.Count)
                throw new FormatException("the file has " +
                    (file.InverseBindMatrices == null ? 0 : file.InverseBindMatrices.Length).ToString(CultureInfo.InvariantCulture) +
                    " bind poses for " + file.JointNames.Count.ToString(CultureInfo.InvariantCulture) +
                    " bones; re-export from Blender rather than editing the file by hand");

            joints = new ushort[file.Joints.Length];
            for (int i = 0; i < file.Joints.Length; i++)
            {
                int slot = file.Joints[i];
                if (slot >= liveOf.Length)
                    throw new FormatException("vertex " + (i / 4).ToString(CultureInfo.InvariantCulture) + " references bone " +
                        slot.ToString(CultureInfo.InvariantCulture) + " but the file has " +
                        liveOf.Length.ToString(CultureInfo.InvariantCulture) + "; the file is corrupt, so re-export it");
                joints[i] = (ushort)liveOf[slot];
            }
            bindposes = new float[boneNames.Count][];
            for (int i = 0; i < boneNames.Count; i++) bindposes[i] = file.InverseBindMatrices[fileOf[i]];
        }

        /// <summary>The static half: no rig, so only the parts that do not mention bones are checked.</summary>
        internal static void BindStatic(SkinnedModel file, int materialSlots)
        {
            if (file == null) throw new ArgumentNullException(nameof(file));
            if (file.JointNames.Count > 0)
                throw new FormatException("the file carries an armature but the object it would replace has no skeleton; " +
                    "export the mesh from Blender without its armature, or put the file on a rigged model instead");
            Submeshes(file, materialSlots);
        }

        private static void Submeshes(SkinnedModel file, int materialSlots)
        {
            if (materialSlots > 0 && file.Submeshes.Count != materialSlots)
                throw new FormatException("the file has " + file.Submeshes.Count.ToString(CultureInfo.InvariantCulture) +
                    " material parts but this model draws with " + materialSlots.ToString(CultureInfo.InvariantCulture) +
                    "; a part with no material would not be drawn, so in Blender keep exactly " +
                    materialSlots.ToString(CultureInfo.InvariantCulture) + " material slots on the mesh and re-export");
            int vertices = file.Positions == null ? 0 : file.Positions.Length;
            foreach (int[] triangles in file.Submeshes)
                foreach (int index in triangles)
                    if (index < 0 || index >= vertices)
                        throw new FormatException("a triangle points at vertex " + index.ToString(CultureInfo.InvariantCulture) +
                            " of " + vertices.ToString(CultureInfo.InvariantCulture) + "; the file is corrupt, so re-export it");
        }

        private static void Shapes(SkinnedModel file, IList<string> blendShapeNames)
        {
            int expected = blendShapeNames == null ? 0 : blendShapeNames.Count;
            if (file.Morphs.Count != expected)
                throw new FormatException("the file has " + file.Morphs.Count.ToString(CultureInfo.InvariantCulture) +
                    " blend shapes but this model has " + expected.ToString(CultureInfo.InvariantCulture) +
                    ", and the game drives them by position; in Blender keep every shape key that came with the model, " +
                    "in the same order, and re-export");
            for (int i = 0; i < expected; i++)
                if (!string.Equals(file.Morphs[i].Name, blendShapeNames[i], StringComparison.Ordinal))
                    throw new FormatException("the file's blend shape " + i.ToString(CultureInfo.InvariantCulture) + " is named '" +
                        file.Morphs[i].Name + "' but this model's is '" + blendShapeNames[i] +
                        "'; rename the shape key in Blender to match exactly and re-export");
        }
    }
}
