using System;
using System.Collections.Generic;
using AssetsTools.NET;
using Morgott.ContentTool.Import;

namespace Morgott.ContentTool.Bake
{
    /// <summary>
    /// Reading a shipped Mesh back OUT: the serialized vertex streams, index buffer, submeshes and
    /// skin, turned into the <see cref="SkinnedModel"/> the GLB writer takes.
    ///
    /// The inverse of <see cref="MeshFields.Fill"/>, and the same arrangement: no UnityEngine type,
    /// so the whole extract -&gt; GLB -&gt; read-back round trip is proven offline against a real
    /// shipped bundle instead of costing a game launch.
    ///
    /// The layout facts are the ones already measured (MeshFields, SkinFields), not re-derived: 14
    /// channel slots whose INDEX is the semantic, every stream 16-byte aligned, blend weight in slot
    /// 12 and blend indices in slot 13. What is NOT assumed is the format and dimension of any
    /// channel: a shipped mesh packs normals as SNorm8 or Float16 as it pleases, and the influence
    /// count varies per mesh (Assault torsos carry 4, most others 2), so both are read per channel.
    /// </summary>
    internal static class MeshRead
    {
        private const int SlotPosition = 0, SlotNormal = 1, SlotTangent = 2, SlotUv0 = 4, SlotUv1 = 5;
        private const int SlotBlendWeight = 12, SlotBlendIndices = 13;
        private const int StreamAlignment = 16;

        /// <summary>glTF stores exactly four influences per vertex; a mesh with fewer is padded.</summary>
        private const int GltfInfluences = 4;

        /// <summary>
        /// One channel as the file describes it. Dimension 0 means the mesh does not carry it at all.
        /// </summary>
        private struct Channel
        {
            internal int Stream, Offset, Format, Dimension;
            internal bool Present => Dimension > 0;
        }

        /// <summary>
        /// <paramref name="resS"/> is asked for an archive entry by name when the vertices are not
        /// inline; it may return null, which is reported as a refusal rather than as empty geometry.
        /// </summary>
        internal static SkinnedModel Read(AssetTypeValueField mesh, Func<string, byte[]> resS)
        {
            if (mesh == null) throw new ArgumentNullException(nameof(mesh));
            string name = mesh["m_Name"].AsString ?? "mesh";

            int compression = mesh["m_MeshCompression"].AsInt;
            if (compression != 0)
                throw new InvalidOperationException("'" + name + "' is a COMPRESSED mesh (m_MeshCompression=" +
                    compression + "); its geometry lives in m_CompressedMesh as packed bit vectors, which this " +
                    "reader does not unpack");

            AssetTypeValueField vd = mesh["m_VertexData"];
            int count = (int)vd["m_VertexCount"].AsUInt;
            if (count <= 0) throw new InvalidOperationException("'" + name + "' declares " + count + " vertices");

            byte[] vertexData = Vertices(mesh, vd, name, resS);
            Channel[] channels = Channels(vd);
            Layout layout = Layout.Of(channels, count, vertexData.Length, name);

            SkinnedModel model = new SkinnedModel { Name = name };
            model.Positions = Vec3(vertexData, channels[SlotPosition], layout, count, name, "position");
            if (channels[SlotNormal].Present)
                model.Normals = Vec3(vertexData, channels[SlotNormal], layout, count, name, "normal");
            if (channels[SlotUv0].Present)
                model.Uv0 = Vec2(vertexData, channels[SlotUv0], layout, count, name, "uv0");
            if (channels[SlotUv1].Present)
                model.Uv1 = Vec2(vertexData, channels[SlotUv1], layout, count, name, "uv1");
            if (channels[SlotTangent].Present && channels[SlotTangent].Dimension == 4)
                model.Tangents = Floats(vertexData, channels[SlotTangent], layout, count, 4);

            Submeshes(mesh, model, name);
            Skin(mesh, model, vertexData, channels, layout, count, name);
            return model;
        }

        // ---------------------------------------------------------------- vertex bytes

        private static byte[] Vertices(AssetTypeValueField mesh, AssetTypeValueField vd, string name,
                                       Func<string, byte[]> resS)
        {
            byte[] inline = vd["m_DataSize"].AsByteArray;
            AssetTypeValueField sd = mesh["m_StreamData"];
            string path = sd == null || sd.IsDummy ? "" : (sd["path"].AsString ?? "");
            if (inline != null && inline.Length > 0) return inline;
            if (path.Length == 0)
                throw new InvalidOperationException("'" + name + "' carries no vertex bytes: m_DataSize is empty " +
                                                    "and m_StreamData names no archive entry");

            // Same indirection the textures use - the pixels and the vertices stream out of the same
            // .resS beside the serialized file.
            string entry = path.Substring(path.LastIndexOf('/') + 1);
            byte[] archive = resS == null ? null : resS(entry);
            if (archive == null)
                throw new InvalidOperationException("'" + name + "' streams its vertices from '" + entry +
                                                    "', which could not be read out of the bundle");
            ulong offset = sd["offset"].AsULong;
            uint size = sd["size"].AsUInt;
            if (offset + size > (ulong)archive.LongLength)
                throw new InvalidOperationException("'" + name + "' claims " + size + " B at " + offset + " of '" +
                                                    entry + "', which is only " + archive.Length + " B");
            byte[] bytes = new byte[size];
            Array.Copy(archive, (long)offset, bytes, 0, size);
            return bytes;
        }

        private static Channel[] Channels(AssetTypeValueField vd)
        {
            AssetTypeValueField array = vd["m_Channels"]["Array"];
            Channel[] channels = new Channel[Math.Max(MeshFields.ChannelCount, array.Children.Count)];
            for (int i = 0; i < array.Children.Count; i++)
            {
                AssetTypeValueField c = array.Children[i];
                channels[i] = new Channel
                {
                    Stream = c["stream"].AsByte,
                    Offset = c["offset"].AsByte,
                    Format = c["format"].AsByte,
                    Dimension = c["dimension"].AsByte,
                };
            }
            return channels;
        }

        /// <summary>
        /// Where each stream begins in the vertex blob and how wide it is. A stream's stride is the
        /// far edge of its widest channel, and the next stream starts at the next 16-byte boundary
        /// after this one's whole run - the same alignment <see cref="SkinFields.SkinOffset"/> writes.
        /// </summary>
        private struct Layout
        {
            internal int[] Start, Stride;

            internal static Layout Of(Channel[] channels, int count, int available, string name)
            {
                int streams = 1;
                foreach (Channel c in channels) if (c.Present && c.Stream + 1 > streams) streams = c.Stream + 1;

                Layout l = new Layout { Start = new int[streams], Stride = new int[streams] };
                foreach (Channel c in channels)
                {
                    if (!c.Present) continue;
                    int end = c.Offset + Size(c.Format, name) * c.Dimension;
                    if (end > l.Stride[c.Stream]) l.Stride[c.Stream] = end;
                }

                int at = 0;
                for (int s = 0; s < streams; s++)
                {
                    l.Start[s] = at;
                    at += l.Stride[s] * count;
                    at += (StreamAlignment - at % StreamAlignment) % StreamAlignment;
                }
                // The LAST stream needs no padding to be present, so the check is against its real end.
                int need = l.Start[streams - 1] + l.Stride[streams - 1] * count;
                if (need > available)
                    throw new InvalidOperationException("'" + name + "' describes " + need +
                                                        " B of vertex streams but carries " + available + " B");
                return l;
            }
        }

        // ---------------------------------------------------------------- attributes

        private static ObjVector3[] Vec3(byte[] data, Channel c, Layout layout, int count, string name, string what)
        {
            if (c.Dimension < 3)
                throw new InvalidOperationException("'" + name + "' has a " + c.Dimension + "-wide " + what + " channel");
            float[] f = Floats(data, c, layout, count, 3);
            ObjVector3[] v = new ObjVector3[count];
            for (int i = 0; i < count; i++) v[i] = new ObjVector3(f[i * 3], f[i * 3 + 1], f[i * 3 + 2]);
            return v;
        }

        private static ObjVector2[] Vec2(byte[] data, Channel c, Layout layout, int count, string name, string what)
        {
            if (c.Dimension < 2)
                throw new InvalidOperationException("'" + name + "' has a " + c.Dimension + "-wide " + what + " channel");
            float[] f = Floats(data, c, layout, count, 2);
            ObjVector2[] v = new ObjVector2[count];
            for (int i = 0; i < count; i++) v[i] = new ObjVector2(f[i * 2], f[i * 2 + 1]);
            return v;
        }

        /// <summary>The first <paramref name="take"/> components of a channel, per vertex, as floats.</summary>
        private static float[] Floats(byte[] data, Channel c, Layout layout, int count, int take)
        {
            int size = Size(c.Format, "");
            int stride = layout.Stride[c.Stream];
            float[] result = new float[count * take];
            for (int i = 0; i < count; i++)
            {
                int at = layout.Start[c.Stream] + i * stride + c.Offset;
                for (int k = 0; k < take; k++) result[i * take + k] = Component(data, at + k * size, c.Format);
            }
            return result;
        }

        /// <summary>Bytes one component of a vertex format occupies (Unity's VertexFormat order).</summary>
        private static int Size(int format, string name)
        {
            switch (format)
            {
                case 0: case 10: case 11: return 4;                 // Float32, UInt32, SInt32
                case 1: case 4: case 5: case 8: case 9: return 2;   // Float16, UNorm16, SNorm16, UInt16, SInt16
                case 2: case 3: case 6: case 7: return 1;           // UNorm8, SNorm8, UInt8, SInt8
                default:
                    throw new InvalidOperationException("vertex format " + format +
                        (string.IsNullOrEmpty(name) ? "" : " on '" + name + "'") + " is not one this reader knows");
            }
        }

        private static float Component(byte[] d, int at, int format)
        {
            switch (format)
            {
                case 0: return BitConverter.ToSingle(d, at);
                case 1: return Half(BitConverter.ToUInt16(d, at));
                case 2: return d[at] / 255f;
                case 3: return Math.Max((sbyte)d[at] / 127f, -1f);
                case 4: return BitConverter.ToUInt16(d, at) / 65535f;
                case 5: return Math.Max(BitConverter.ToInt16(d, at) / 32767f, -1f);
                case 6: return d[at];
                case 7: return (sbyte)d[at];
                case 8: return BitConverter.ToUInt16(d, at);
                case 9: return BitConverter.ToInt16(d, at);
                case 10: return BitConverter.ToUInt32(d, at);
                case 11: return BitConverter.ToInt32(d, at);
                default: throw new InvalidOperationException("vertex format " + format + " is not one this reader knows");
            }
        }

        /// <summary>IEEE 754 binary16 -> float. Sixteen bits are not worth a dependency.</summary>
        private static float Half(ushort h)
        {
            int sign = (h >> 15) & 1, exponent = (h >> 10) & 0x1F, mantissa = h & 0x3FF;
            if (exponent == 0)
                return (float)((sign == 1 ? -1 : 1) * Math.Pow(2, -14) * (mantissa / 1024.0));
            if (exponent == 31)
                return mantissa == 0 ? (sign == 1 ? float.NegativeInfinity : float.PositiveInfinity) : float.NaN;
            return (float)((sign == 1 ? -1 : 1) * Math.Pow(2, exponent - 15) * (1.0 + mantissa / 1024.0));
        }

        // ---------------------------------------------------------------- topology

        private static void Submeshes(AssetTypeValueField mesh, SkinnedModel model, string name)
        {
            byte[] indexBuffer = mesh["m_IndexBuffer"]["Array"].AsByteArray ?? new byte[0];
            bool wide = mesh["m_IndexFormat"].AsInt == 1;
            int width = wide ? 4 : 2;

            foreach (AssetTypeValueField sub in mesh["m_SubMeshes"]["Array"].Children)
            {
                if (sub["topology"].AsInt != 0)
                    throw new InvalidOperationException("'" + name + "' has a submesh whose topology is not triangles");
                int first = (int)sub["firstByte"].AsUInt;
                int indices = (int)sub["indexCount"].AsUInt;
                int baseVertex = (int)sub["baseVertex"].AsUInt;
                if (first + indices * width > indexBuffer.Length)
                    throw new InvalidOperationException("'" + name + "' has a submesh reaching past its index buffer");

                int[] triangles = new int[indices];
                for (int i = 0; i < indices; i++)
                {
                    int at = first + i * width;
                    triangles[i] = baseVertex + (wide ? (int)BitConverter.ToUInt32(indexBuffer, at)
                                                      : BitConverter.ToUInt16(indexBuffer, at));
                }
                model.Submeshes.Add(triangles);
                // The Mesh knows nothing about materials - those live on the renderer that draws it -
                // so the slot is named after where it came from rather than invented.
                model.Materials.Add(name + "_submesh" + (model.Submeshes.Count - 1));
            }
        }

        // ---------------------------------------------------------------- skin

        /// <summary>
        /// The rig, as much of it as a Mesh actually holds: bind poses and bone name HASHES. The bone
        /// hierarchy is not in here - it lives on the SkinnedMeshRenderer's transforms - and a CRC-32
        /// does not invert, so the export is a FLAT rig under one synthetic root: bone i's rest
        /// transform is the inverse of its bind pose, which under an identity root is exactly its
        /// model-space rest pose. The deformation is therefore exact; only the parenting is lost.
        /// </summary>
        private static void Skin(AssetTypeValueField mesh, SkinnedModel model, byte[] data,
                                 Channel[] channels, Layout layout, int count, string name)
        {
            AssetTypeValueField poses = mesh["m_BindPose"]["Array"];
            Channel w = channels[SlotBlendWeight], b = channels[SlotBlendIndices];
            if (poses.Children.Count == 0 || !w.Present || !b.Present) return;   // static mesh, a whole valid export

            int influences = Math.Min(w.Dimension, b.Dimension);
            float[] weights = Floats(data, w, layout, count, influences);
            float[] bones = Floats(data, b, layout, count, influences);

            model.Weights = new float[count * GltfInfluences];
            model.Joints = new ushort[count * GltfInfluences];
            for (int i = 0; i < count; i++)
                for (int k = 0; k < influences && k < GltfInfluences; k++)
                {
                    float weight = weights[i * influences + k];
                    int bone = (int)bones[i * influences + k];
                    if (bone < 0 || bone >= poses.Children.Count)
                        throw new InvalidOperationException("'" + name + "' has a vertex bound to bone " + bone +
                                                            " but carries " + poses.Children.Count + " bind poses");
                    model.Weights[i * GltfInfluences + k] = weight;
                    // A glTF JOINTS_0 value indexes the SKIN'S JOINT SLOTS, not the node list, so it
                    // stays the mesh's own bone index; JointNodes below is what maps a slot to a node.
                    model.Joints[i * GltfInfluences + k] = (ushort)bone;
                }

            AssetTypeValueField hashes = mesh["m_BoneNameHashes"]["Array"];
            model.RootBonePath = mesh["m_RootBoneNameHash"].AsUInt.ToString();
            model.Nodes.Add(new SkinNode { Name = name + "_rig", Parent = -1, Local = Identity() });
            model.InverseBindMatrices = new float[poses.Children.Count][];
            model.JointNodes = new int[poses.Children.Count];
            for (int i = 0; i < poses.Children.Count; i++)
            {
                float[] bindPose = Matrix(poses.Children[i]);
                model.InverseBindMatrices[i] = bindPose;
                // A CRC-32 does not invert, so the hash IS the name - it is what the mesh records and
                // what a re-import has to match, and inventing "Bone_03" would lose it.
                string bone = i < hashes.Children.Count
                    ? "bone_" + hashes.Children[i].AsUInt
                    : "bone_" + i + "_unnamed";
                model.BonePaths.Add(bone);
                model.Nodes.Add(new SkinNode { Name = bone, Parent = 0, Local = Invert(bindPose, bone) });
                model.JointNodes[i] = i + 1;
            }
            model.BindposeCount = poses.Children.Count;
        }

        /// <summary>Unity's Matrix4x4f as glTF wants it: column-major float[16], index = col*4 + row.</summary>
        private static float[] Matrix(AssetTypeValueField m)
        {
            float[] result = new float[16];
            for (int row = 0; row < 4; row++)
                for (int column = 0; column < 4; column++)
                    result[column * 4 + row] = m["e" + row + column].AsFloat;
            return result;
        }

        private static float[] Identity()
        {
            return new[] { 1f, 0f, 0f, 0f, 0f, 1f, 0f, 0f, 0f, 0f, 1f, 0f, 0f, 0f, 0f, 1f };
        }

        /// <summary>General 4x4 inverse by cofactors - a bind pose may carry non-uniform scale.</summary>
        private static float[] Invert(float[] m, string what)
        {
            double[] a = new double[16];
            for (int i = 0; i < 16; i++) a[i] = m[i];

            double[] inv = new double[16];
            inv[0] = a[5] * a[10] * a[15] - a[5] * a[11] * a[14] - a[9] * a[6] * a[15] + a[9] * a[7] * a[14] + a[13] * a[6] * a[11] - a[13] * a[7] * a[10];
            inv[4] = -a[4] * a[10] * a[15] + a[4] * a[11] * a[14] + a[8] * a[6] * a[15] - a[8] * a[7] * a[14] - a[12] * a[6] * a[11] + a[12] * a[7] * a[10];
            inv[8] = a[4] * a[9] * a[15] - a[4] * a[11] * a[13] - a[8] * a[5] * a[15] + a[8] * a[7] * a[13] + a[12] * a[5] * a[11] - a[12] * a[7] * a[9];
            inv[12] = -a[4] * a[9] * a[14] + a[4] * a[10] * a[13] + a[8] * a[5] * a[14] - a[8] * a[6] * a[13] - a[12] * a[5] * a[10] + a[12] * a[6] * a[9];
            inv[1] = -a[1] * a[10] * a[15] + a[1] * a[11] * a[14] + a[9] * a[2] * a[15] - a[9] * a[3] * a[14] - a[13] * a[2] * a[11] + a[13] * a[3] * a[10];
            inv[5] = a[0] * a[10] * a[15] - a[0] * a[11] * a[14] - a[8] * a[2] * a[15] + a[8] * a[3] * a[14] + a[12] * a[2] * a[11] - a[12] * a[3] * a[10];
            inv[9] = -a[0] * a[9] * a[15] + a[0] * a[11] * a[13] + a[8] * a[1] * a[15] - a[8] * a[3] * a[13] - a[12] * a[1] * a[11] + a[12] * a[3] * a[9];
            inv[13] = a[0] * a[9] * a[14] - a[0] * a[10] * a[13] - a[8] * a[1] * a[14] + a[8] * a[2] * a[13] + a[12] * a[1] * a[10] - a[12] * a[2] * a[9];
            inv[2] = a[1] * a[6] * a[15] - a[1] * a[7] * a[14] - a[5] * a[2] * a[15] + a[5] * a[3] * a[14] + a[13] * a[2] * a[7] - a[13] * a[3] * a[6];
            inv[6] = -a[0] * a[6] * a[15] + a[0] * a[7] * a[14] + a[4] * a[2] * a[15] - a[4] * a[3] * a[14] - a[12] * a[2] * a[7] + a[12] * a[3] * a[6];
            inv[10] = a[0] * a[5] * a[15] - a[0] * a[7] * a[13] - a[4] * a[1] * a[15] + a[4] * a[3] * a[13] + a[12] * a[1] * a[7] - a[12] * a[3] * a[5];
            inv[14] = -a[0] * a[5] * a[14] + a[0] * a[6] * a[13] + a[4] * a[1] * a[14] - a[4] * a[2] * a[13] - a[12] * a[1] * a[6] + a[12] * a[2] * a[5];
            inv[3] = -a[1] * a[6] * a[11] + a[1] * a[7] * a[10] + a[5] * a[2] * a[11] - a[5] * a[3] * a[10] - a[9] * a[2] * a[7] + a[9] * a[3] * a[6];
            inv[7] = a[0] * a[6] * a[11] - a[0] * a[7] * a[10] - a[4] * a[2] * a[11] + a[4] * a[3] * a[10] + a[8] * a[2] * a[7] - a[8] * a[3] * a[6];
            inv[11] = -a[0] * a[5] * a[11] + a[0] * a[7] * a[9] + a[4] * a[1] * a[11] - a[4] * a[3] * a[9] - a[8] * a[1] * a[7] + a[8] * a[3] * a[5];
            inv[15] = a[0] * a[5] * a[10] - a[0] * a[6] * a[9] - a[4] * a[1] * a[10] + a[4] * a[2] * a[9] + a[8] * a[1] * a[6] - a[8] * a[2] * a[5];

            double det = a[0] * inv[0] + a[1] * inv[4] + a[2] * inv[8] + a[3] * inv[12];
            if (Math.Abs(det) < 1e-12)
                throw new InvalidOperationException("'" + what + "' has a singular bind pose, so it has no rest transform");

            float[] result = new float[16];
            for (int i = 0; i < 16; i++) result[i] = (float)(inv[i] / det);
            return result;
        }
    }
}
