using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace Morgott.ContentTool.Import
{
    /// <summary>
    /// One node of the exported armature. <see cref="Local"/> is a column-major 4x4 in Unity space
    /// (the layout of Unity's Matrix4x4 linear indexer, which is also what glTF asks for).
    /// </summary>
    internal sealed class SkinNode
    {
        internal string Name;
        internal int Parent = -1;
        internal float[] Local;
    }

    /// <summary>
    /// A rotation in Unity space, laid out the way glTF stores one: x, y, z, w. Kept free of
    /// UnityEngine.Quaternion so the codec and its self-check run outside Unity.
    /// </summary>
    internal readonly struct ObjQuaternion
    {
        internal ObjQuaternion(float x, float y, float z, float w) { X = x; Y = y; Z = z; W = w; }
        internal float X { get; }
        internal float Y { get; }
        internal float Z { get; }
        internal float W { get; }
    }

    /// <summary>
    /// One bone's sampled motion. A null channel means the bone never left its rest value on that
    /// channel, so the exporter drops it rather than writing a constant curve.
    /// </summary>
    internal sealed class SampledTrack
    {
        internal int Node;
        internal ObjVector3[] Translations;
        internal ObjQuaternion[] Rotations;
        internal ObjVector3[] Scales;
    }

    /// <summary>
    /// One animation clip RESAMPLED off the running game. This is never the game's own curve data:
    /// the Player has no <c>AnimationUtility</c>, so the only thing observable is the pose the clip
    /// puts the rig in at a given time. <see cref="LossyReason"/> is the player-facing sentence that
    /// says so, and it is written verbatim into the sidecar.
    /// </summary>
    internal sealed class SampledClip
    {
        internal string Name = "clip";
        /// <summary>Sidecar-only: the .glb this clip was written to, relative to the dump root.</summary>
        internal string File = "";
        /// <summary>generic | legacy | humanoid. Humanoid clips are refused, never sampled.</summary>
        internal string Kind = "generic";
        internal string WrapMode = "Default";
        internal bool Looping;
        internal float Length;
        /// <summary>The clip's own authored rate, as Unity reports it.</summary>
        internal float FrameRate;
        /// <summary>The rate actually used, which is the clip's rate unless a cap lowered it.</summary>
        internal float SampleRate;
        /// <summary>Shared sample times, seconds from the clip start. One input accessor for the file.</summary>
        internal float[] Times;
        internal readonly List<SkinNode> Nodes = new List<SkinNode>();
        internal readonly List<SampledTrack> Tracks = new List<SampledTrack>();
        internal string LossyReason = "";
    }

    /// <summary>One blend shape, as per-vertex deltas in Unity space.</summary>
    internal sealed class SkinMorph
    {
        internal string Name;
        internal ObjVector3[] Positions;
        internal ObjVector3[] Normals;
    }

    /// <summary>
    /// Unity-space skinned model, free of UnityEngine types so the codec can be exercised offline.
    /// Everything below <see cref="Checksum"/> is sidecar-only and never reaches the GLB.
    /// </summary>
    internal sealed class SkinnedModel
    {
        internal string Name = "mesh";
        internal ObjVector3[] Positions;
        internal ObjVector3[] Normals;
        internal float[] Tangents;                  // 4 per vertex, w = bitangent sign
        internal ObjVector2[] Uv0;
        internal ObjVector2[] Uv1;
        internal ushort[] Joints;                   // 4 per vertex
        internal float[] Weights;                   // 4 per vertex
        internal readonly List<int[]> Submeshes = new List<int[]>();
        internal readonly List<string> Materials = new List<string>();
        /// <summary>
        /// Import-only: the RAW BYTES of each material's base-colour image as the .glb carried it
        /// (PNG or JPEG), index-parallel to <see cref="Materials"/>, null where a material declares
        /// no texture. Kept undecoded because the decoder is Unity's own Texture2D.LoadImage, which
        /// only exists in the running game - the import side must not need it.
        /// </summary>
        internal readonly List<byte[]> MaterialImages = new List<byte[]>();
        /// <summary>
        /// Import-only: each material's glTF <c>emissiveFactor</c> already multiplied by
        /// KHR_materials_emissive_strength, index-parallel to <see cref="Materials"/>, null where the
        /// material emits nothing. This is how a model asks to GLOW without a manifest key: the
        /// author exports emission, and the bake turns it into _EMISSION + _EmissionColor.
        /// </summary>
        internal readonly List<float[]> MaterialEmissive = new List<float[]>();
        /// <summary>
        /// Per-submesh material truth, index-parallel to <see cref="Materials"/>. Empty when the
        /// caller has none, which keeps the GLB's name-only slots working exactly as before.
        /// </summary>
        internal readonly List<MaterialSnapshot> MaterialInfo = new List<MaterialSnapshot>();
        internal readonly List<SkinNode> Nodes = new List<SkinNode>();
        internal int[] JointNodes;                  // joint slot -> index into Nodes
        internal float[][] InverseBindMatrices;     // per joint, column-major, Unity space
        internal readonly List<SkinMorph> Morphs = new List<SkinMorph>();
        /// <summary>
        /// Import-only: the joint slot names read out of a GLB, in the FILE's joint order. The export
        /// path leaves this empty because a name is already reachable through <see cref="JointNodes"/>;
        /// an imported model has no node tree, so the names are what binds it to a live rig. Empty
        /// therefore also means "this model carries no skin", which is what the static path needs.
        /// </summary>
        internal readonly List<string> JointNames = new List<string>();

        /// <summary>
        /// Import-only bookkeeping: how many glTF materials and image uris the file carried. They are
        /// never applied — paint has exactly one route, the .mat.json sidecar — so these exist purely
        /// so the mod can SAY what it skipped instead of leaving a player wondering.
        /// </summary>
        internal int IgnoredMaterials;
        internal int IgnoredImages;

        /// <summary>
        /// Import-only: the meshes in the file that were NOT taken, by name, when the skin picked one
        /// out of several. Empty for the ordinary single-mesh file. Never silent - the bake prints it,
        /// because "your model had two meshes and I chose one" is exactly the sentence an author must
        /// read rather than discover by finding half their model missing.
        /// </summary>
        internal readonly List<string> DroppedMeshes = new List<string>();

        /// <summary>
        /// Import-only: the baseColorFactor of the material the file puts on the LARGEST primitive,
        /// linear RGBA, or null when the file declares no material at all. The one piece of a glTF
        /// material that is NOT ignored, because the add path has no sidecar route to state a colour
        /// on - every primitive is merged into one submesh with one material (ModelBuild's ceiling),
        /// so a model with no texture would otherwise bake to the shader's default white.
        /// </summary>
        internal float[] BaseColor;

        /// <summary>
        /// Sidecar-only: the animation destination contract, read off the live Animator at dump time.
        /// All four are null together when the renderer has NO Animator at all — a rig nothing drives
        /// is not the same fact as a rig that is driven and merely is not humanoid, and the sidecar
        /// must not let one read as the other. <see cref="AnimatorHumanoid"/> true is the case the
        /// animation dump refuses outright, which until now was only discoverable by running the game.
        /// </summary>
        internal string AnimatorController = null;
        internal string AnimatorAvatar = null;
        internal bool? AnimatorHumanoid = null;
        /// <summary>
        /// One entry per clip slot the controller exposes: the slot's own clip, and the clip that
        /// actually plays there once the game's equipment overrides are applied. Empty when the
        /// controller is not an override controller; null when there is no Animator.
        /// </summary>
        internal List<string[]> AnimatorOverrides = null;

        internal string Checksum = "";
        internal readonly List<string> BonePaths = new List<string>();
        internal string RootBonePath = "";
        internal string RendererPath = "";
        internal int BindposeCount;
    }

    /// <summary>
    /// The glTF 2.0 magic numbers the writer and the reader both speak. One declaration, because a
    /// codec whose two halves disagree on a constant is exactly the bug no self-check of either half
    /// on its own can see.
    /// </summary>
    internal static class Gltf
    {
        internal const uint Magic = 0x46546C67;      // "glTF"
        internal const uint JsonChunk = 0x4E4F534A;
        internal const uint BinChunk = 0x004E4942;

        internal const int ArrayBuffer = 34962;
        internal const int ElementArrayBuffer = 34963;
        internal const int Triangles = 4;

        // componentType codes, the whole set glTF defines and the reader accepts.
        internal const int Byte = 5120;
        internal const int UnsignedByte = 5121;
        internal const int Short = 5122;
        internal const int UnsignedShort = 5123;
        internal const int UnsignedInt = 5125;
        internal const int Float = 5126;
    }

    /// <summary>
    /// Hand-written glTF 2.0 binary container. No library: a GLB is a 12-byte header, a JSON chunk
    /// and a binary chunk, and the mod must ILRepack into a single assembly.
    ///
    /// Coordinate systems. Unity is left-handed with +X right, +Y up, +Z front; glTF is right-handed,
    /// puts +Y up and states that the front of an asset faces +Z. Keeping BOTH "up" and "front" while
    /// swapping handedness leaves exactly one choice: glTF +X is Unity's -X. So the whole conversion
    /// is the involution S = diag(-1, 1, 1), applied to positions, normals, tangent xyz and morph
    /// deltas, and as S*M*S to every matrix. Because det(S) = -1, triangle winding must be reversed
    /// too (see <see cref="Indices"/>); doing one without the other is what silently mirrors a model.
    /// </summary>
    internal static class GlbCodec
    {
        /// <summary>Unity -> glTF for a true vector: negate X.</summary>
        internal static ObjVector3 Convert(ObjVector3 v) => new ObjVector3(-v.X, v.Y, v.Z);

        /// <summary>
        /// Unity -> glTF for a ROTATION, which is NOT the vector rule. Derivation, because copying
        /// the vector rule here is the classic way to mirror an animation while the mesh stays right:
        ///
        /// The basis change is R' = S*R*S with S = diag(-1, 1, 1). For any orthogonal Q,
        /// Q*R(n, t)*Q^-1 = R(Q*n, det(Q)*t): Rodrigues' cross-product term picks up det(Q) because
        /// Q(a x b) = det(Q) * (Qa x Qb). det(S) = -1, so S*R(n, t)*S = R(S*n, -t) — the axis is
        /// reflected AND the angle flips. In quaternion terms q = (n*sin(t/2), cos(t/2)) becomes
        /// (-S*n*sin(t/2), cos(t/2)), and -S*n = (n.x, -n.y, -n.z). So:
        ///
        ///     (x, y, z, w)  ->  (x, -y, -z, w)
        ///
        /// Note the inversion against <see cref="Convert(ObjVector3)"/>: the vector rule negates X and
        /// keeps Y and Z, the quaternion rule keeps X and negates Y and Z. Expanding the quaternion
        /// into a 3x3 reproduces <see cref="ConvertMatrix"/> exactly, which is the invariant the
        /// self-check asserts by composing a converted rotation with a converted translation.
        /// </summary>
        internal static ObjQuaternion Convert(ObjQuaternion q) => new ObjQuaternion(q.X, -q.Y, -q.Z, q.W);

        /// <summary>
        /// Unity -> glTF for a matrix: M' = S*M*S. In column-major float[16] (index = col*4 + row)
        /// that negates exactly the entries where one of row/col is zero. [0][0] flips twice and so
        /// stays put; [0][3] flips once, which is the translation reducing to (-x, y, z). det is
        /// preserved, so no mirroring is introduced and the result stays TRS-decomposable.
        /// </summary>
        internal static float[] ConvertMatrix(float[] m)
        {
            var result = new float[16];
            for (int column = 0; column < 4; column++)
                for (int row = 0; row < 4; row++)
                {
                    int i = column * 4 + row;
                    result[i] = (row == 0) != (column == 0) ? -m[i] : m[i];
                }
            return result;
        }

        /// <summary>
        /// Unity's texture origin is bottom-left and glTF's is top-left, so V is mirrored. Blender's
        /// importer mirrors it back, which is what makes the round trip land on the original UVs.
        /// </summary>
        internal static ObjVector2 ConvertUv(ObjVector2 uv) => new ObjVector2(uv.X, 1f - uv.Y);

        internal static byte[] Write(SkinnedModel model)
        {
            Validate(model);
            int count = model.Positions.Length;
            var buffer = new GlbBuffer();

            int Vectors(ObjVector3[] source, int target, bool bounded)
            {
                var data = new float[source.Length * 3];
                var min = new[] { float.MaxValue, float.MaxValue, float.MaxValue };
                var max = new[] { float.MinValue, float.MinValue, float.MinValue };
                for (int i = 0; i < source.Length; i++)
                {
                    ObjVector3 v = Convert(source[i]);
                    data[i * 3] = v.X; data[i * 3 + 1] = v.Y; data[i * 3 + 2] = v.Z;
                    min[0] = Math.Min(min[0], v.X); min[1] = Math.Min(min[1], v.Y); min[2] = Math.Min(min[2], v.Z);
                    max[0] = Math.Max(max[0], v.X); max[1] = Math.Max(max[1], v.Y); max[2] = Math.Max(max[2], v.Z);
                }
                return buffer.Accessor(buffer.View(Bytes(data), target), Gltf.Float, source.Length, "VEC3", bounded ? min : null, bounded ? max : null);
            }

            int position = Vectors(model.Positions, Gltf.ArrayBuffer, true);
            int normal = model.Normals == null ? -1 : Vectors(model.Normals, Gltf.ArrayBuffer, false);

            int tangent = -1;
            if (Tangential(model.Tangents))
            {
                var data = new float[count * 4];
                for (int i = 0; i < count; i++)
                {
                    // xyz is a true vector; w is the bitangent sign and is deliberately NOT flipped.
                    // The basis reflection alone would demand -w, but mirroring V reverses the
                    // bitangent a second time, so the two cancel. Do not "fix" one without the other.
                    data[i * 4] = -model.Tangents[i * 4];
                    data[i * 4 + 1] = model.Tangents[i * 4 + 1];
                    data[i * 4 + 2] = model.Tangents[i * 4 + 2];
                    data[i * 4 + 3] = model.Tangents[i * 4 + 3];
                }
                tangent = buffer.Accessor(buffer.View(Bytes(data), Gltf.ArrayBuffer), Gltf.Float, count, "VEC4", null, null);
            }

            int Uvs(ObjVector2[] source)
            {
                var data = new float[source.Length * 2];
                for (int i = 0; i < source.Length; i++)
                {
                    ObjVector2 uv = ConvertUv(source[i]);
                    data[i * 2] = uv.X; data[i * 2 + 1] = uv.Y;
                }
                return buffer.Accessor(buffer.View(Bytes(data), Gltf.ArrayBuffer), Gltf.Float, source.Length, "VEC2", null, null);
            }

            int uv0 = model.Uv0 == null ? -1 : Uvs(model.Uv0);
            int uv1 = model.Uv1 == null ? -1 : Uvs(model.Uv1);

            // A static mesh has no rig, so it gets no JOINTS_0/WEIGHTS_0, no inverse bind matrices and
            // no skin. Everything else — geometry, submeshes, materials, images — is written the same.
            bool rigged = model.JointNodes != null && model.JointNodes.Length > 0;
            int joints = rigged ? buffer.Accessor(buffer.View(Bytes(model.Joints), Gltf.ArrayBuffer), Gltf.UnsignedShort, count, "VEC4", null, null) : -1;
            int weights = rigged ? buffer.Accessor(buffer.View(Bytes(model.Weights), Gltf.ArrayBuffer), Gltf.Float, count, "VEC4", null, null) : -1;

            var indices = new List<int>();
            foreach (int[] triangles in model.Submeshes)
                indices.Add(buffer.Accessor(buffer.View(Bytes(Indices(triangles)), Gltf.ElementArrayBuffer), Gltf.UnsignedInt, triangles.Length, "SCALAR", null, null));

            var morphPositions = new List<int>();
            var morphNormals = new List<int>();
            foreach (SkinMorph morph in model.Morphs)
            {
                morphPositions.Add(Vectors(morph.Positions, -1, true));
                morphNormals.Add(morph.Normals == null ? -1 : Vectors(morph.Normals, -1, false));
            }

            int inverseBind = -1;
            if (rigged)
            {
                var bind = new float[model.JointNodes.Length * 16];
                for (int j = 0; j < model.JointNodes.Length; j++)
                {
                    float[] matrix = ConvertMatrix(model.InverseBindMatrices[j]);
                    Array.Copy(matrix, 0, bind, j * 16, 16);
                }
                inverseBind = buffer.Accessor(buffer.View(Bytes(bind), -1), Gltf.Float, model.JointNodes.Length, "MAT4", null, null);
            }

            byte[] binary = buffer.Finish();

            var json = new JsonWriter();
            json.Obj();
            buffer.WriteAccessors(json);
            json.Key("asset").Obj().Key("generator").Val("Resource Replacer").Key("version").Val("2.0").EndObj();
            buffer.WriteBufferViews(json);
            json.Key("buffers").Arr().Obj().Key("byteLength").Val(binary.Length).EndObj().EndArr();

            // One glTF image per distinct dumped PNG, one texture per image, one shared sampler.
            // The PNG stays external: the mod dumps it where the player can edit it in place, and a
            // relative uri keeps the GLB small and the pair movable as one folder.
            var imageUris = new List<string>();
            var imageIndex = new Dictionary<string, int>(StringComparer.Ordinal);
            int Texture(MaterialTexture texture)
            {
                if (texture == null || string.IsNullOrEmpty(texture.File)) return -1;
                if (imageIndex.TryGetValue(texture.File, out int existing)) return existing;
                imageIndex[texture.File] = imageUris.Count;
                imageUris.Add(texture.File);
                return imageUris.Count - 1;
            }
            var slots = new List<int[]>();          // per material: base, normal, emissive, occlusion
            foreach (MaterialSnapshot material in model.MaterialInfo)
                slots.Add(new[]
                {
                    Texture(MaterialCodec.BaseColor(material)),
                    Texture(MaterialCodec.Normal(material)),
                    Texture(MaterialCodec.Emissive(material)),
                    Texture(MaterialCodec.Occlusion(material)),
                });

            if (imageUris.Count > 0)
            {
                json.Key("images").Arr();
                foreach (string uri in imageUris) json.Obj().Key("uri").Val(uri).EndObj();
                json.EndArr();
            }
            json.Key("materials").Arr();
            for (int i = 0; i < model.Materials.Count; i++)
            {
                MaterialSnapshot material = i < model.MaterialInfo.Count ? model.MaterialInfo[i] : null;
                json.Obj();
                if (material == null) { json.Key("name").Val(model.Materials[i]).EndObj(); continue; }
                string alpha = MaterialCodec.AlphaMode(material);
                if (alpha == "MASK") json.Key("alphaCutoff").Val(MaterialCodec.AlphaCutoff(material));
                if (alpha != "OPAQUE") json.Key("alphaMode").Val(alpha);
                if (MaterialCodec.DoubleSided(material)) json.Key("doubleSided").Val(true);
                float[] emissive = MaterialCodec.EmissiveFactor(material);
                if (emissive[0] > 0f || emissive[1] > 0f || emissive[2] > 0f) json.Key("emissiveFactor").Vals(emissive);
                if (slots[i][2] >= 0) json.Key("emissiveTexture").Obj().Key("index").Val(slots[i][2]).EndObj();
                json.Key("name").Val(model.Materials[i]);
                if (slots[i][1] >= 0) json.Key("normalTexture").Obj().Key("index").Val(slots[i][1]).EndObj();
                if (slots[i][3] >= 0) json.Key("occlusionTexture").Obj().Key("index").Val(slots[i][3]).EndObj();
                json.Key("pbrMetallicRoughness").Obj();
                json.Key("baseColorFactor").Vals(MaterialCodec.BaseColorFactor(material));
                if (slots[i][0] >= 0) json.Key("baseColorTexture").Obj().Key("index").Val(slots[i][0]).EndObj();
                // No metallicRoughnessTexture: Unity packs metallic in R and smoothness in A, glTF
                // wants roughness in G and metallic in B. Wiring the map unconverted would be wrong,
                // so only the factors are emitted and the packed map stays in the .mat.json.
                json.Key("metallicFactor").Val(MaterialCodec.MetallicFactor(material));
                json.Key("roughnessFactor").Val(MaterialCodec.RoughnessFactor(material));
                json.EndObj();
                json.EndObj();
            }
            json.EndArr();
            json.Key("meshes").Arr().Obj();
            json.Key("name").Val(model.Name);
            json.Key("primitives").Arr();
            for (int s = 0; s < indices.Count; s++)
            {
                json.Obj();
                json.Key("attributes").Obj();
                if (joints >= 0) json.Key("JOINTS_0").Val(joints);
                if (normal >= 0) json.Key("NORMAL").Val(normal);
                json.Key("POSITION").Val(position);
                if (tangent >= 0) json.Key("TANGENT").Val(tangent);
                if (uv0 >= 0) json.Key("TEXCOORD_0").Val(uv0);
                if (uv1 >= 0) json.Key("TEXCOORD_1").Val(uv1);
                if (weights >= 0) json.Key("WEIGHTS_0").Val(weights);
                json.EndObj();
                json.Key("indices").Val(indices[s]);
                json.Key("material").Val(s);
                json.Key("mode").Val(Gltf.Triangles);
                if (morphPositions.Count > 0)
                {
                    json.Key("targets").Arr();
                    for (int m = 0; m < morphPositions.Count; m++)
                    {
                        json.Obj();
                        if (morphNormals[m] >= 0) json.Key("NORMAL").Val(morphNormals[m]);
                        json.Key("POSITION").Val(morphPositions[m]);
                        json.EndObj();
                    }
                    json.EndArr();
                }
                json.EndObj();
            }
            json.EndArr();
            if (model.Morphs.Count > 0)
            {
                // targetNames in extras is the de-facto convention every importer, Blender included,
                // reads back; glTF core has no place for a morph name.
                json.Key("extras").Obj().Key("targetNames").Arr();
                foreach (SkinMorph morph in model.Morphs) json.Val(morph.Name);
                json.EndArr().EndObj();
                json.Key("weights").Arr();
                foreach (SkinMorph unused in model.Morphs) json.Val(0f);
                json.EndArr();
            }
            json.EndObj().EndArr();
            json.Key("nodes").Arr();
            List<int> scene = WriteNodes(json, model.Nodes, false);
            scene.Add(model.Nodes.Count);           // the mesh node, appended after the bones
            // The skinned mesh node's own transform is ignored by glTF, so it stays at the default.
            // A static mesh's node carries no skin and, like the OBJ path, no transform either: the
            // geometry is dumped in the mesh's own local space and is replaced back into it.
            json.Obj().Key("mesh").Val(0).Key("name").Val(model.Name);
            if (rigged) json.Key("skin").Val(0);
            json.EndObj();
            json.EndArr();
            if (imageUris.Count > 0)
            {
                // Unity's per-texture wrap/filter state is not on the material, so one repeat/linear
                // sampler is emitted rather than an invented per-texture guess.
                json.Key("samplers").Arr().Obj();
                json.Key("magFilter").Val(9729);
                json.Key("minFilter").Val(9987);
                json.Key("wrapS").Val(10497);
                json.Key("wrapT").Val(10497);
                json.EndObj().EndArr();
            }
            WriteScene(json, scene);
            if (rigged)
            {
                json.Key("skins").Arr().Obj();
                json.Key("inverseBindMatrices").Val(inverseBind);
                json.Key("joints").Arr();
                foreach (int node in model.JointNodes) json.Val(node);
                json.EndArr();
                json.Key("name").Val(model.Name + "_skin");
                json.Key("skeleton").Val(Root(model));
                json.EndObj().EndArr();
            }
            if (imageUris.Count > 0)
            {
                json.Key("textures").Arr();
                for (int i = 0; i < imageUris.Count; i++)
                    json.Obj().Key("sampler").Val(0).Key("source").Val(i).EndObj();
                json.EndArr();
            }
            json.EndObj();

            return Container(json.ToString(), binary);
        }

        /// <summary>The 12-byte header plus the padded JSON and BIN chunks that make a GLB a GLB.</summary>
        private static byte[] Container(string text, byte[] binary)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(text);
            int textLength = (bytes.Length + 3) & ~3;
            var output = new MemoryStream();
            var writer = new BinaryWriter(output);
            writer.Write(Gltf.Magic);
            writer.Write(2u);
            writer.Write((uint)(12 + 8 + textLength + 8 + binary.Length));
            writer.Write((uint)textLength);
            writer.Write(Gltf.JsonChunk);
            writer.Write(bytes);
            for (int i = bytes.Length; i < textLength; i++) writer.Write((byte)0x20);
            writer.Write((uint)binary.Length);
            writer.Write(Gltf.BinChunk);
            writer.Write(binary);
            writer.Flush();
            return output.ToArray();
        }

        /// <summary>
        /// One clip, one GLB: the rig's nodes plus a single glTF animation over them, with no mesh
        /// and no skin. That is the design's editable/animations/*.glb layout, and it keeps a rig
        /// with thirty clips as thirty small files instead of one enormous one — a bone hierarchy is
        /// a few kilobytes, a skinned mesh is megabytes.
        ///
        /// Interpolation is LINEAR and nothing else. The exporter only ever observes poses, so it has
        /// no tangents to state; writing CUBICSPLINE here would be inventing data it does not have.
        /// </summary>
        internal static byte[] WriteAnimation(SampledClip clip)
        {
            ValidateAnimation(clip);
            var buffer = new GlbBuffer();

            // glTF requires min and max on an animation sampler's input, so the shared time accessor
            // carries them. One input for the whole file: every channel is sampled on the same grid.
            int input = buffer.Floats(Bytes(clip.Times), clip.Times.Length, "SCALAR",
                new[] { clip.Times[0] }, new[] { clip.Times[clip.Times.Length - 1] });

            var channelNode = new List<int>();
            var channelPath = new List<string>();
            var channelOutput = new List<int>();

            foreach (SampledTrack track in clip.Tracks)
            {
                if (track.Translations != null)
                {
                    var data = new float[track.Translations.Length * 3];
                    for (int i = 0; i < track.Translations.Length; i++)
                    {
                        ObjVector3 v = Convert(track.Translations[i]);
                        data[i * 3] = v.X; data[i * 3 + 1] = v.Y; data[i * 3 + 2] = v.Z;
                    }
                    channelNode.Add(track.Node);
                    channelPath.Add("translation");
                    channelOutput.Add(buffer.Floats(Bytes(data), track.Translations.Length, "VEC3", null, null));
                }
                if (track.Rotations != null)
                {
                    var data = new float[track.Rotations.Length * 4];
                    for (int i = 0; i < track.Rotations.Length; i++)
                    {
                        ObjQuaternion q = Convert(track.Rotations[i]);
                        // glTF demands unit quaternions. The basis change preserves length, so this
                        // only removes the drift the sampled float already carried.
                        double length = Math.Sqrt((double)q.X * q.X + (double)q.Y * q.Y +
                                                  (double)q.Z * q.Z + (double)q.W * q.W);
                        data[i * 4] = (float)(q.X / length);
                        data[i * 4 + 1] = (float)(q.Y / length);
                        data[i * 4 + 2] = (float)(q.Z / length);
                        data[i * 4 + 3] = (float)(q.W / length);
                    }
                    channelNode.Add(track.Node);
                    channelPath.Add("rotation");
                    channelOutput.Add(buffer.Floats(Bytes(data), track.Rotations.Length, "VEC4", null, null));
                }
                if (track.Scales != null)
                {
                    // S*diag(sx, sy, sz)*S = diag(sx, sy, sz), so scale crosses the basis unchanged.
                    var data = new float[track.Scales.Length * 3];
                    for (int i = 0; i < track.Scales.Length; i++)
                    {
                        data[i * 3] = track.Scales[i].X;
                        data[i * 3 + 1] = track.Scales[i].Y;
                        data[i * 3 + 2] = track.Scales[i].Z;
                    }
                    channelNode.Add(track.Node);
                    channelPath.Add("scale");
                    channelOutput.Add(buffer.Floats(Bytes(data), track.Scales.Length, "VEC3", null, null));
                }
            }

            byte[] binary = buffer.Finish();

            var json = new JsonWriter();
            json.Obj();
            buffer.WriteAccessors(json);
            json.Key("animations").Arr().Obj();
            json.Key("channels").Arr();
            for (int i = 0; i < channelNode.Count; i++)
            {
                json.Obj();
                json.Key("sampler").Val(i);
                json.Key("target").Obj().Key("node").Val(channelNode[i]).Key("path").Val(channelPath[i]).EndObj();
                json.EndObj();
            }
            json.EndArr();
            json.Key("name").Val(clip.Name);
            json.Key("samplers").Arr();
            for (int i = 0; i < channelOutput.Count; i++)
            {
                json.Obj();
                json.Key("input").Val(input);
                json.Key("interpolation").Val("LINEAR");
                json.Key("output").Val(channelOutput[i]);
                json.EndObj();
            }
            json.EndArr();
            json.EndObj().EndArr();
            json.Key("asset").Obj().Key("generator").Val("Resource Replacer (sampled preview, not original curves)").Key("version").Val("2.0").EndObj();
            buffer.WriteBufferViews(json);
            json.Key("buffers").Arr().Obj().Key("byteLength").Val(binary.Length).EndObj().EndArr();
            json.Key("nodes").Arr();
            // TRS, not matrix: every one of these nodes is (or may be) an animation channel target,
            // and glTF 2.0 §3.3.2 says a targeted node MUST NOT carry a matrix.
            List<int> scene = WriteNodes(json, clip.Nodes, true);
            json.EndArr();
            WriteScene(json, scene);
            json.EndObj();

            return Container(json.ToString(), binary);
        }

        /// <summary>
        /// The bone nodes, into an already-open "nodes" array, and the scene roots for
        /// <see cref="WriteScene"/>. The mesh writer appends its own node after these and the
        /// animation writer does not, so closing the array is the caller's job. Parents are listed
        /// before their children (Validate enforces it), so one pass builds the child lists.
        ///
        /// <paramref name="trs"/> writes translation/rotation/scale instead of one matrix. Only the
        /// animation writer asks for it, because glTF 2.0 §3.3.2 forbids a matrix on any node an
        /// animation channel targets. The mesh writer keeps the matrix: nothing targets those nodes,
        /// and a matrix survives a shear that TRS cannot express.
        /// </summary>
        private static List<int> WriteNodes(JsonWriter json, IList<SkinNode> nodes, bool trs)
        {
            var children = new List<List<int>>();
            for (int i = 0; i < nodes.Count; i++) children.Add(new List<int>());
            var roots = new List<int>();
            for (int i = 0; i < nodes.Count; i++)
            {
                int parent = nodes[i].Parent;
                if (parent < 0) roots.Add(i);
                else children[parent].Add(i);
            }
            for (int i = 0; i < nodes.Count; i++)
            {
                json.Obj();
                if (children[i].Count > 0)
                {
                    json.Key("children").Arr();
                    foreach (int child in children[i]) json.Val(child);
                    json.EndArr();
                }
                float[] local = ConvertMatrix(nodes[i].Local);
                if (!trs) json.Key("matrix").Vals(local);
                json.Key("name").Val(nodes[i].Name);
                if (trs)
                {
                    // Decompose the ALREADY-converted matrix, so what comes out is the glTF-space
                    // rest pose by construction. Decomposing first and converting the pieces after
                    // would need the quaternion rule of Convert(ObjQuaternion) and the vector rule
                    // of Convert(ObjVector3) applied to different parts of the same transform — the
                    // classic way to ship a file that validates and is mirrored.
                    float[] translation, rotation, scale;
                    Decompose(local, nodes[i].Name, out translation, out rotation, out scale);
                    json.Key("rotation").Vals(rotation);
                    json.Key("scale").Vals(scale);
                    json.Key("translation").Vals(translation);
                }
                json.EndObj();
            }
            return roots;
        }

        /// <summary>
        /// A column-major affine 4x4 (index = col*4 + row) split into translation, an xyzw quaternion
        /// and scale, such that T*R*S reproduces the input. Translation is the fourth column; scale is
        /// the length of each basis column, with the FIRST one carrying the sign of the determinant so
        /// a mirrored basis (det &lt; 0, which the S = diag(-1, 1, 1) axis flip can produce) stays
        /// mirrored instead of silently un-mirroring. Rotation is the descaled basis, read out with the
        /// branch-on-largest-diagonal form so no branch ever divides by a near-zero number.
        ///
        /// TRS cannot express shear, so a sheared basis is refused rather than rounded off: a rest pose
        /// that is quietly wrong bends the whole skeleton and nothing downstream can see that it did.
        /// </summary>
        internal static void Decompose(float[] m, string name, out float[] translation, out float[] rotation, out float[] scale)
        {
            string bone = "bone '" + (name ?? "?") + "'";
            if (Math.Abs(m[3]) > 1e-5f || Math.Abs(m[7]) > 1e-5f || Math.Abs(m[11]) > 1e-5f || Math.Abs(m[15] - 1f) > 1e-5f)
                throw new InvalidOperationException(bone + " has a rest transform that is not affine, which glTF cannot store " +
                    "as translation/rotation/scale; re-dump from a live renderer whose bones carry plain transforms");

            translation = new[] { m[12], m[13], m[14] };

            double determinant =
                (double)m[0] * (m[5] * (double)m[10] - m[9] * (double)m[6]) -
                (double)m[4] * (m[1] * (double)m[10] - m[9] * (double)m[2]) +
                (double)m[8] * (m[1] * (double)m[6] - m[5] * (double)m[2]);

            var axis = new double[3][];
            var length = new double[3];
            for (int c = 0; c < 3; c++)
            {
                axis[c] = new[] { (double)m[c * 4], (double)m[c * 4 + 1], (double)m[c * 4 + 2] };
                length[c] = Math.Sqrt(axis[c][0] * axis[c][0] + axis[c][1] * axis[c][1] + axis[c][2] * axis[c][2]);
                if (length[c] < 1e-8)
                    throw new InvalidOperationException(bone + " has a rest transform with a zero-length axis, so it has no " +
                        "rotation to store; re-dump after the rig stops reporting collapsed bones");
                for (int r = 0; r < 3; r++) axis[c][r] /= length[c];
            }
            // Orthogonality of the descaled basis is exactly the "no shear" test: non-uniform scale
            // alone leaves the axes perpendicular, shear is what tilts them into each other.
            for (int a = 0; a < 3; a++)
                for (int b = a + 1; b < 3; b++)
                    if (Math.Abs(axis[a][0] * axis[b][0] + axis[a][1] * axis[b][1] + axis[a][2] * axis[b][2]) > 1e-4)
                        throw new InvalidOperationException(bone + " has a sheared rest transform, which glTF cannot store as " +
                            "translation/rotation/scale and which an animation channel's node must use; re-dump after removing " +
                            "the skew from the rig, or export this rig without animation");

            double sign = determinant < 0.0 ? -1.0 : 1.0;
            scale = new[] { (float)(length[0] * sign), (float)length[1], (float)length[2] };
            if (sign < 0.0) for (int r = 0; r < 3; r++) axis[0][r] = -axis[0][r];

            // axis[c][r] is column c, row r, so this is the rotation matrix R[row, col] = axis[col][row].
            double m00 = axis[0][0], m10 = axis[0][1], m20 = axis[0][2];
            double m01 = axis[1][0], m11 = axis[1][1], m21 = axis[1][2];
            double m02 = axis[2][0], m12 = axis[2][1], m22 = axis[2][2];
            double x, y, z, w;
            double trace = m00 + m11 + m22;
            if (trace > 0.0)
            {
                double s = Math.Sqrt(trace + 1.0) * 2.0;
                w = 0.25 * s; x = (m21 - m12) / s; y = (m02 - m20) / s; z = (m10 - m01) / s;
            }
            else if (m00 > m11 && m00 > m22)
            {
                double s = Math.Sqrt(1.0 + m00 - m11 - m22) * 2.0;
                w = (m21 - m12) / s; x = 0.25 * s; y = (m01 + m10) / s; z = (m02 + m20) / s;
            }
            else if (m11 > m22)
            {
                double s = Math.Sqrt(1.0 + m11 - m00 - m22) * 2.0;
                w = (m02 - m20) / s; x = (m01 + m10) / s; y = 0.25 * s; z = (m12 + m21) / s;
            }
            else
            {
                double s = Math.Sqrt(1.0 + m22 - m00 - m11) * 2.0;
                w = (m10 - m01) / s; x = (m02 + m20) / s; y = (m12 + m21) / s; z = 0.25 * s;
            }
            double norm = Math.Sqrt(x * x + y * y + z * z + w * w);
            rotation = new[] { (float)(x / norm), (float)(y / norm), (float)(z / norm), (float)(w / norm) };
        }

        private static void WriteScene(JsonWriter json, List<int> roots)
        {
            json.Key("scene").Val(0);
            json.Key("scenes").Arr().Obj().Key("nodes").Arr();
            foreach (int node in roots) json.Val(node);
            json.EndArr().EndObj().EndArr();
        }

        /// <summary>
        /// The GLB binary chunk under construction plus the two JSON tables that index into it. Both
        /// writers build one: a view is 4-byte aligned data, an accessor is a typed window onto a
        /// view, and both arrays are emitted in the order they were added.
        /// </summary>
        private sealed class GlbBuffer
        {
            private readonly MemoryStream bin = new MemoryStream();
            private readonly List<int> viewOffset = new List<int>();
            private readonly List<int> viewLength = new List<int>();
            private readonly List<int> viewTarget = new List<int>();
            private readonly List<int> accessorView = new List<int>();
            private readonly List<int> accessorComponent = new List<int>();
            private readonly List<int> accessorCount = new List<int>();
            private readonly List<string> accessorType = new List<string>();
            private readonly List<float[]> accessorMin = new List<float[]>();
            private readonly List<float[]> accessorMax = new List<float[]>();

            /// <summary>Appends aligned data. A <paramref name="target"/> below 1 emits no target.</summary>
            internal int View(byte[] data, int target)
            {
                Pad();
                viewOffset.Add((int)bin.Length);
                viewLength.Add(data.Length);
                viewTarget.Add(target);
                bin.Write(data, 0, data.Length);
                return viewOffset.Count - 1;
            }

            internal int Accessor(int view, int component, int elements, string type, float[] min, float[] max)
            {
                accessorView.Add(view);
                accessorComponent.Add(component);
                accessorCount.Add(elements);
                accessorType.Add(type);
                accessorMin.Add(min);
                accessorMax.Add(max);
                return accessorView.Count - 1;
            }

            /// <summary>A float accessor over a view of its own, which is every animation accessor.</summary>
            internal int Floats(byte[] data, int elements, string type, float[] min, float[] max) =>
                Accessor(View(data, -1), Gltf.Float, elements, type, min, max);

            internal void WriteAccessors(JsonWriter json)
            {
                json.Key("accessors").Arr();
                for (int i = 0; i < accessorView.Count; i++)
                {
                    json.Obj();
                    json.Key("bufferView").Val(accessorView[i]);
                    json.Key("componentType").Val(accessorComponent[i]);
                    json.Key("count").Val(accessorCount[i]);
                    if (accessorMax[i] != null) json.Key("max").Vals(accessorMax[i]);
                    if (accessorMin[i] != null) json.Key("min").Vals(accessorMin[i]);
                    json.Key("type").Val(accessorType[i]);
                    json.EndObj();
                }
                json.EndArr();
            }

            internal void WriteBufferViews(JsonWriter json)
            {
                json.Key("bufferViews").Arr();
                for (int i = 0; i < viewOffset.Count; i++)
                {
                    json.Obj();
                    json.Key("buffer").Val(0);
                    json.Key("byteLength").Val(viewLength[i]);
                    json.Key("byteOffset").Val(viewOffset[i]);
                    if (viewTarget[i] > 0) json.Key("target").Val(viewTarget[i]);
                    json.EndObj();
                }
                json.EndArr();
            }

            /// <summary>The padded BIN chunk. The tables stay readable afterwards.</summary>
            internal byte[] Finish()
            {
                Pad();
                return bin.ToArray();
            }

            private void Pad()
            {
                while ((bin.Length & 3) != 0) bin.WriteByte(0);
            }
        }

        /// <summary>The "Unity truth" sidecar: canonical JSON, keys in alphabetical order.</summary>
        internal static string WriteSidecar(SkinnedModel model) => WriteSidecar(model, null);

        /// <summary>
        /// The same sidecar plus the animation truth. Every clip entry carries lossy = true and the
        /// sentence explaining it, because a sampled pose grid is not what Phoenix Point plays.
        /// </summary>
        internal static string WriteSidecar(SkinnedModel model, IList<SampledClip> clips)
        {
            if (model == null) throw new ArgumentNullException(nameof(model));
            int triangles = 0;
            foreach (int[] submesh in model.Submeshes) triangles += submesh.Length / 3;
            var json = new JsonWriter();
            json.Obj();
            if (clips != null && clips.Count > 0)
            {
                json.Key("animations").Arr();
                foreach (SampledClip clip in clips)
                {
                    json.Obj();
                    json.Key("file").Val(clip.File);
                    json.Key("frameRate").Val(clip.FrameRate);
                    json.Key("kind").Val(clip.Kind);
                    json.Key("length").Val(clip.Length);
                    json.Key("looping").Val(clip.Looping);
                    json.Key("lossy").Val(true);
                    json.Key("lossyReason").Val(clip.LossyReason);
                    json.Key("name").Val(clip.Name);
                    json.Key("sampleCount").Val(clip.Times == null ? 0 : clip.Times.Length);
                    json.Key("sampleRate").Val(clip.SampleRate);
                    json.Key("wrapMode").Val(clip.WrapMode);
                    json.EndObj();
                }
                json.EndArr();
            }
            // The animation destination contract. Always emitted, and JSON null rather than "" or
            // false when there is no Animator: a reader has to be able to tell "no rig driver" from
            // "generic rig", and an empty string would quietly claim the latter.
            json.Key("animatorAvatar");
            if (model.AnimatorAvatar == null) json.Null(); else json.Val(model.AnimatorAvatar);
            json.Key("animatorController");
            if (model.AnimatorController == null) json.Null(); else json.Val(model.AnimatorController);
            json.Key("animatorHumanoid");
            if (model.AnimatorHumanoid == null) json.Null(); else json.Val(model.AnimatorHumanoid.Value);
            json.Key("animatorOverrides");
            if (model.AnimatorOverrides == null) json.Null();
            else
            {
                json.Arr();
                foreach (string[] pair in model.AnimatorOverrides)
                    json.Obj().Key("plays").Val(pair[1]).Key("slot").Val(pair[0]).EndObj();
                json.EndArr();
            }
            json.Key("bindposeCount").Val(model.BindposeCount);
            json.Key("blendShapeNames").Arr();
            foreach (SkinMorph morph in model.Morphs) json.Val(morph.Name);
            json.EndArr();
            json.Key("bonePaths").Arr();
            foreach (string path in model.BonePaths) json.Val(path);
            json.EndArr();
            json.Key("checksum").Val(model.Checksum);
            json.Key("meshName").Val(model.Name);
            json.Key("rendererPath").Val(model.RendererPath);
            json.Key("rootBonePath").Val(model.RootBonePath);
            json.Key("submeshCount").Val(model.Submeshes.Count);
            json.Key("submeshMaterials").Arr();
            foreach (string material in model.Materials) json.Val(material);
            json.EndArr();
            json.Key("triangleCount").Val(triangles);
            // "version" before "vertexCount": 's' < 't', so ordinal order puts it first. It used to be
            // emitted last, which read alphabetical but is not.
            json.Key("version").Val(1);
            json.Key("vertexCount").Val(model.Positions == null ? 0 : model.Positions.Length);
            json.EndObj();
            return json.ToString();
        }

        /// <summary>
        /// Reversed winding. cross(S*a, S*b) = det(S) * S * (a x b) = -S * (a x b), so under the axis
        /// flip the geometric normal implied by the original index order points the wrong way and
        /// every face reads inside out. Swapping the last two indices negates the cross product back.
        /// This is the second half of the handedness conversion and is not optional.
        /// </summary>
        private static uint[] Indices(int[] triangles)
        {
            var data = new uint[triangles.Length];
            for (int i = 0; i + 2 < triangles.Length; i += 3)
            {
                data[i] = (uint)triangles[i];
                data[i + 1] = (uint)triangles[i + 2];
                data[i + 2] = (uint)triangles[i + 1];
            }
            return data;
        }

        /// <summary>
        /// Whether a tangent array is writable as a glTF TANGENT attribute at all. glTF requires the
        /// xyz of every tangent to be unit length and its w to be exactly +1 or -1; Unity hands back a
        /// zero-FILLED array rather than a null one for meshes that have no tangents (its procedural UI
        /// meshes, for one), and (0, 0, 0, 0) satisfies neither.
        ///
        /// All or nothing, and the answer is DROP rather than refuse. The attribute cannot be written
        /// per-vertex, so one bad tangent makes the whole array unwritable as-is; and a mesh with no
        /// TANGENT is perfectly valid glTF that every consumer recomputes from the UVs, whereas a refusal
        /// would stop the player dumping a mesh over an attribute they never asked for. Contrast the rest
        /// pose in <see cref="Decompose"/>, which IS refused: a wrong skeleton has no such fallback.
        /// Nothing is repaired here — inventing a direction for a tangent nobody supplied is a guess.
        /// </summary>
        private static bool Tangential(float[] tangents)
        {
            if (tangents == null || tangents.Length % 4 != 0) return false;
            for (int i = 0; i < tangents.Length; i += 4)
            {
                double x = tangents[i], y = tangents[i + 1], z = tangents[i + 2], w = tangents[i + 3];
                if (Math.Abs(Math.Sqrt(x * x + y * y + z * z) - 1.0) > 1e-3) return false;
                if (Math.Abs(Math.Abs(w) - 1.0) > 1e-3) return false;
            }
            return tangents.Length > 0;
        }

        /// <summary>Topmost ancestor of the joints; the exporter emits one common root for the rig.</summary>
        private static int Root(SkinnedModel model)
        {
            int node = model.JointNodes[0];
            while (model.Nodes[node].Parent >= 0) node = model.Nodes[node].Parent;
            return node;
        }

        private static void Validate(SkinnedModel model)
        {
            if (model == null) throw new ArgumentNullException(nameof(model));
            if (model.Positions == null || model.Positions.Length == 0)
                throw new InvalidOperationException("model has no vertices; export a mesh with geometry");
            int count = model.Positions.Length;
            if (model.Submeshes.Count == 0)
                throw new InvalidOperationException("model has no submeshes; export a mesh with triangles");
            if (model.Materials.Count != model.Submeshes.Count)
                throw new InvalidOperationException("model has " + model.Materials.Count + " material names for " +
                    model.Submeshes.Count + " submeshes; supply one name per submesh");
            if (model.MaterialInfo.Count > 0 && model.MaterialInfo.Count != model.Materials.Count)
                throw new InvalidOperationException("model has " + model.MaterialInfo.Count + " material snapshots for " +
                    model.Materials.Count + " material slots; supply one snapshot per submesh or none at all");
            foreach (MaterialSnapshot material in model.MaterialInfo)
            {
                if (material == null)
                    throw new InvalidOperationException("a material slot has a null snapshot; export every slot or none at all");
                foreach (MaterialTexture texture in material.Textures) Uri(material.Name, texture);
            }
            // A model with no joints is the STATIC case, which is a whole valid export rather than a
            // broken rig: it writes the same geometry and the same materials with no skin at all.
            bool rigged = model.JointNodes != null && model.JointNodes.Length > 0;
            if (rigged)
            {
                if (model.Nodes.Count == 0)
                    throw new InvalidOperationException("model has joints but no bone nodes; export a rigged renderer with bones");
                if (model.InverseBindMatrices == null || model.InverseBindMatrices.Length != model.JointNodes.Length)
                    throw new InvalidOperationException("model has " +
                        (model.InverseBindMatrices == null ? 0 : model.InverseBindMatrices.Length) +
                        " inverse bind matrices for " + model.JointNodes.Length + " joints; the counts must match");
            }
            else if (model.Nodes.Count > 0 || model.Joints != null || model.Weights != null)
                throw new InvalidOperationException("model lists no joints but still carries a bone hierarchy or skin weights; " +
                    "export either a rigged renderer with its bones or a static mesh with neither");
            Finite(model.Positions, "position");
            if (model.Normals != null) { Length(model.Normals.Length, count, "normals"); Finite(model.Normals, "normal"); }
            if (model.Uv0 != null) Length(model.Uv0.Length, count, "uv0");
            if (model.Uv1 != null) Length(model.Uv1.Length, count, "uv1");
            if (model.Tangents != null) { Length(model.Tangents.Length, count * 4, "tangents"); Finite(model.Tangents, "tangent"); }
            if (rigged)
            {
                Length(model.Joints == null ? 0 : model.Joints.Length, count * 4, "joint indices");
                Length(model.Weights == null ? 0 : model.Weights.Length, count * 4, "skin weights");
                Finite(model.Weights, "weight");
                for (int i = 0; i < model.Joints.Length; i++)
                    if (model.Joints[i] >= model.JointNodes.Length)
                        throw new InvalidOperationException("vertex " + (i / 4).ToString(CultureInfo.InvariantCulture) +
                            " references bone " + model.Joints[i].ToString(CultureInfo.InvariantCulture) + " but the rig has " +
                            model.JointNodes.Length.ToString(CultureInfo.InvariantCulture) +
                            " bones; re-export after the renderer's bone list and the mesh agree");
                for (int i = 0; i < model.Weights.Length; i++)
                    if (model.Weights[i] < 0f)
                        throw new InvalidOperationException("vertex " + (i / 4).ToString(CultureInfo.InvariantCulture) +
                            " has a negative skin weight; re-export after fixing the rig");
            }
            foreach (int[] submesh in model.Submeshes)
            {
                if (submesh == null || submesh.Length == 0 || submesh.Length % 3 != 0)
                    throw new InvalidOperationException("a submesh is not a whole number of triangles; export triangle topology only");
                foreach (int index in submesh)
                    if (index < 0 || index >= count)
                        throw new InvalidOperationException("a triangle references vertex " +
                            index.ToString(CultureInfo.InvariantCulture) + " of " + count.ToString(CultureInfo.InvariantCulture) +
                            "; re-export after the index buffer and the vertex buffer agree");
            }
            for (int i = 0; i < model.Nodes.Count; i++)
            {
                SkinNode node = model.Nodes[i];
                if (node.Local == null || node.Local.Length != 16)
                    throw new InvalidOperationException("bone '" + (node.Name ?? "?") + "' has no 4x4 local transform; re-export from a live renderer");
                Finite(node.Local, "bone transform");
                if (node.Parent >= i)
                    throw new InvalidOperationException("bone '" + (node.Name ?? "?") + "' is listed before its parent; export parents first");
            }
            if (rigged)
                foreach (float[] matrix in model.InverseBindMatrices)
                {
                    if (matrix == null || matrix.Length != 16)
                        throw new InvalidOperationException("an inverse bind matrix is not 4x4; re-export from a live renderer");
                    Finite(matrix, "bind pose");
                }
            foreach (SkinMorph morph in model.Morphs)
            {
                if (morph.Positions == null) throw new InvalidOperationException("blend shape '" + (morph.Name ?? "?") + "' has no position deltas; re-export or remove it");
                Length(morph.Positions.Length, count, "blend shape '" + (morph.Name ?? "?") + "' deltas");
                Finite(morph.Positions, "blend shape delta");
                if (morph.Normals != null) { Length(morph.Normals.Length, count, "blend shape '" + (morph.Name ?? "?") + "' normal deltas"); Finite(morph.Normals, "blend shape normal delta"); }
            }
        }

        private static void ValidateAnimation(SampledClip clip)
        {
            if (clip == null) throw new ArgumentNullException(nameof(clip));
            string what = "clip '" + (clip.Name ?? "?") + "'";
            if (clip.Times == null || clip.Times.Length < 2)
                throw new InvalidOperationException(what + " has fewer than two samples, so there is nothing to interpolate; " +
                    "re-dump a clip whose length is longer than one sample step");
            Finite(clip.Times, "sample time");
            for (int i = 1; i < clip.Times.Length; i++)
                if (clip.Times[i] <= clip.Times[i - 1])
                    throw new InvalidOperationException(what + " has sample time " +
                        clip.Times[i].ToString("R", CultureInfo.InvariantCulture) + " at or before the previous one; " +
                        "sample times must strictly increase, so re-dump with a positive sample rate");
            if (clip.Nodes.Count == 0)
                throw new InvalidOperationException(what + " carries no bone nodes to animate; " +
                    "re-dump from a rigged renderer whose bones are still in the scene");
            for (int i = 0; i < clip.Nodes.Count; i++)
            {
                SkinNode node = clip.Nodes[i];
                if (node.Local == null || node.Local.Length != 16)
                    throw new InvalidOperationException("bone '" + (node.Name ?? "?") + "' in " + what +
                        " has no 4x4 rest transform; re-dump from a live renderer");
                Finite(node.Local, "bone transform");
                if (node.Parent >= i)
                    throw new InvalidOperationException("bone '" + (node.Name ?? "?") + "' in " + what +
                        " is listed before its parent; export parents first");
            }
            if (clip.Tracks.Count == 0)
                throw new InvalidOperationException(what + " moved no bone of this rig, so exporting it would produce a " +
                    "still pose that is not what the game plays; it most likely drives a different object or is a humanoid " +
                    "muscle clip, so dump the rig the clip actually belongs to");
            foreach (SampledTrack track in clip.Tracks)
            {
                if (track.Node < 0 || track.Node >= clip.Nodes.Count)
                    throw new InvalidOperationException(what + " targets bone " +
                        track.Node.ToString(CultureInfo.InvariantCulture) + " but the rig has " +
                        clip.Nodes.Count.ToString(CultureInfo.InvariantCulture) +
                        "; re-dump after the clip's rig and the exported rig agree");
                if (track.Translations == null && track.Rotations == null && track.Scales == null)
                    throw new InvalidOperationException("bone '" + clip.Nodes[track.Node].Name + "' in " + what +
                        " has a track with no channels; drop the track instead of writing an empty one");
                if (track.Translations != null)
                {
                    Samples(track.Translations.Length, clip.Times.Length, what, "translation");
                    Finite(track.Translations, "translation sample");
                }
                if (track.Scales != null)
                {
                    Samples(track.Scales.Length, clip.Times.Length, what, "scale");
                    Finite(track.Scales, "scale sample");
                }
                if (track.Rotations == null) continue;
                Samples(track.Rotations.Length, clip.Times.Length, what, "rotation");
                foreach (ObjQuaternion q in track.Rotations)
                {
                    Finite(q.X, "rotation sample"); Finite(q.Y, "rotation sample");
                    Finite(q.Z, "rotation sample"); Finite(q.W, "rotation sample");
                    if (q.X * q.X + q.Y * q.Y + q.Z * q.Z + q.W * q.W < 1e-8f)
                        throw new InvalidOperationException("bone '" + clip.Nodes[track.Node].Name + "' in " + what +
                            " has a zero-length rotation, which glTF cannot normalise; re-dump after the rig stops " +
                            "reporting degenerate rotations");
                }
            }
        }

        private static void Samples(int actual, int expected, string what, string channel)
        {
            if (actual != expected)
                throw new InvalidOperationException(what + " has " + actual.ToString(CultureInfo.InvariantCulture) +
                    " " + channel + " samples for " + expected.ToString(CultureInfo.InvariantCulture) +
                    " sample times; every channel shares one time grid, so re-dump the clip");
        }

        /// <summary>
        /// A glTF uri must stay a relative, '/'-separated path next to the GLB. An absolute path, a
        /// backslash or a '..' segment either fails to load elsewhere or escapes the dump folder.
        /// </summary>
        private static void Uri(string material, MaterialTexture texture)
        {
            string file = texture == null ? null : texture.File;
            if (string.IsNullOrEmpty(file)) return;
            string what = "material '" + (material ?? "?") + "' property '" + (texture.Property ?? "?") + "'";
            if (file.IndexOf('\\') >= 0)
                throw new InvalidOperationException(what + " points at '" + file +
                    "'; a glTF uri uses '/' separators, so re-export after normalising the path");
            if (file.StartsWith("/", StringComparison.Ordinal) || (file.Length > 1 && file[1] == ':'))
                throw new InvalidOperationException(what + " points at absolute path '" + file +
                    "'; a glTF uri must be relative to the GLB, so re-export after making the path relative");
            foreach (string segment in file.Split('/'))
                if (segment == "..")
                    throw new InvalidOperationException(what + " points outside the dump folder via '" + file +
                        "'; keep dumped images under the dump root and re-export");
        }

        private static void Length(int actual, int expected, string what)
        {
            if (actual != expected)
                throw new InvalidOperationException(what + " has " + actual.ToString(CultureInfo.InvariantCulture) +
                    " entries but the mesh has " + expected.ToString(CultureInfo.InvariantCulture) +
                    "; re-export after the vertex streams agree");
        }

        private static void Finite(ObjVector3[] values, string what)
        {
            foreach (ObjVector3 value in values) { Finite(value.X, what); Finite(value.Y, what); Finite(value.Z, what); }
        }

        private static void Finite(float[] values, string what)
        {
            foreach (float value in values) Finite(value, what);
        }

        private static void Finite(float value, string what)
        {
            if (float.IsNaN(value) || float.IsInfinity(value))
                throw new InvalidOperationException("a " + what + " value is not finite; glTF stores JSON numbers, so re-export after removing NaN or infinity");
        }

        private static byte[] Bytes(float[] values)
        {
            var data = new byte[values.Length * 4];
            Buffer.BlockCopy(values, 0, data, 0, data.Length);
            return data;
        }

        private static byte[] Bytes(ushort[] values)
        {
            var data = new byte[values.Length * 2];
            Buffer.BlockCopy(values, 0, data, 0, data.Length);
            return data;
        }

        private static byte[] Bytes(uint[] values)
        {
            var data = new byte[values.Length * 4];
            Buffer.BlockCopy(values, 0, data, 0, data.Length);
            return data;
        }
    }

    /// <summary>
    /// Just enough JSON to emit a glTF chunk and a canonical sidecar. Key order is whatever the
    /// caller writes, so callers write them in a fixed order and the output is deterministic.
    /// </summary>
    internal sealed class JsonWriter
    {
        private readonly StringBuilder text = new StringBuilder();
        private bool separate;

        internal JsonWriter Obj() { Comma(); text.Append('{'); return this; }
        internal JsonWriter EndObj() { text.Append('}'); separate = true; return this; }
        internal JsonWriter Arr() { Comma(); text.Append('['); return this; }
        internal JsonWriter EndArr() { text.Append(']'); separate = true; return this; }
        internal JsonWriter Key(string name) { Comma(); Quote(name); text.Append(':'); return this; }
        internal JsonWriter Val(string value) { Comma(); Quote(value); separate = true; return this; }
        internal JsonWriter Val(int value) { Comma(); text.Append(value.ToString(CultureInfo.InvariantCulture)); separate = true; return this; }
        internal JsonWriter Val(float value) { Comma(); text.Append(value.ToString("R", CultureInfo.InvariantCulture)); separate = true; return this; }
        internal JsonWriter Val(bool value) { Comma(); text.Append(value ? "true" : "false"); separate = true; return this; }
        /// <summary>An explicit absent value. Only the sidecar uses it; glTF has no null anywhere.</summary>
        internal JsonWriter Null() { Comma(); text.Append("null"); separate = true; return this; }

        /// <summary>Write a parsed JSON value: string, double, bool, null, or a collection
        /// (Dictionary/List from Json.Parse). Integral doubles written as integers; fractional
        /// doubles use G17 for exact round-trip on .NET Framework (R does not round-trip).</summary>
        internal JsonWriter Val(object value)
        {
            if (value == null) return Null();
            if (value is string word) return Val(word);
            if (value is bool flag) return Val(flag);
            if (value is double number) return Num(number);
            if (value is Dictionary<string, object> members)
            {
                Obj();
                foreach (KeyValuePair<string, object> member in members) { Key(member.Key); Val(member.Value); }
                return EndObj();
            }
            if (value is List<object> items)
            {
                Arr();
                foreach (object item in items) Val(item);
                return EndArr();
            }
            throw new ArgumentException("a " + value.GetType().Name + " is not a JSON value");
        }

        /// <summary>A double that may be integral. Integral -> no decimal point; else the shortest
        /// spelling of 15, 16 or 17 significant digits that re-parses to the same value.</summary>
        internal JsonWriter Num(double value)
        {
            Comma();
            // The long cast is the whole point of the integral arm, so it may only be taken where a
            // long can hold the value - past that G17 writes the exponent form rather than a wrap.
            bool integral = value == Math.Floor(value) && !double.IsInfinity(value) && Math.Abs(value) < 9.2e18;
            if (integral) text.Append(((long)value).ToString(CultureInfo.InvariantCulture));
            else
            {
                // Shortest-round-trip on .NET Framework, which has no "R" that round-trips and no
                // shortest-form default: try 15, 16, then 17 significant digits and take the first
                // spelling that re-parses to the same double. G16 is not a nicety - it is the width a
                // great many doubles actually need, and skipping it spells every one of them with a
                // redundant 17th digit. That is what stops a rewritten file from being byte-identical
                // to the one it came from, since every other producer already writes the shortest form.
                string brief = null;
                for (int digits = 15; digits <= 17; digits++)
                {
                    brief = value.ToString("G" + digits, CultureInfo.InvariantCulture);
                    if (double.Parse(brief, CultureInfo.InvariantCulture) == value) break;
                }
                text.Append(brief);
            }
            separate = true;
            return this;
        }

        internal JsonWriter Vals(float[] values)
        {
            Arr();
            foreach (float value in values) Val(value);
            return EndArr();
        }

        public override string ToString() => text.ToString();

        private void Comma()
        {
            if (separate) text.Append(',');
            separate = false;
        }

        private void Quote(string value)
        {
            text.Append('"');
            foreach (char c in value ?? "")
            {
                switch (c)
                {
                    case '"': text.Append("\\\""); break;
                    case '\\': text.Append("\\\\"); break;
                    case '\b': text.Append("\\b"); break;
                    case '\f': text.Append("\\f"); break;
                    case '\n': text.Append("\\n"); break;
                    case '\r': text.Append("\\r"); break;
                    case '\t': text.Append("\\t"); break;
                    default:
                        if (c < ' ') text.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        else text.Append(c);
                        break;
                }
            }
            text.Append('"');
        }
    }
}
