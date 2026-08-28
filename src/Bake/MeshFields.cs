using System;
using System.Globalization;
using AssetsTools.NET;
using AssetsTools.NET.Extra;
using Morgott.ContentTool.Import;

namespace Morgott.ContentTool.Bake
{
    /// <summary>
    /// The serialized-Mesh half of a mesh replacement: which fields a baked mesh writes and how the
    /// result reads back. Split out of <see cref="BundleBaker"/> because it touches no Unity type and
    /// no embedded resource, so the whole write -&gt; repack -&gt; read round trip is provable offline
    /// (tests\ObjCodecTests) against a real shipped bundle instead of only inside a game session.
    ///
    /// Layout measured 2026-08-12 off `mutoid_assets_all.bundle` (unity 2019.4.31f1): m_IndexBuffer
    /// is a byte vector, m_VertexData.m_DataSize is TypelessData, m_IndexFormat 0 = UInt16, and a
    /// shipped mesh carries 14 channel slots of which only the used ones have a non-zero dimension.
    /// </summary>
    internal static class MeshFields
    {
        private const int ChannelVertex = 0, ChannelNormal = 1, ChannelUv0 = 4;
        private const int FormatFloat32 = 0;
        /// <summary>Slots a 2019.4.31f1 Mesh carries, measured; the index carries the semantic.</summary>
        internal const int ChannelCount = 14;

        /// <summary>
        /// Overwrites a Mesh's geometry with <paramref name="baked"/>. Everything that describes the
        /// OLD geometry is cleared in the same pass - compressed mesh, baked collision, blend shapes,
        /// stream data - because a leftover of any of them is read in preference to what we wrote.
        ///
        /// This clears the skin channels along with everything else, which on a RIGGED target leaves a
        /// mesh bound to nothing. <see cref="SkinFields.Rebind"/> puts the binding back and is called
        /// right after this by every replacement path - the two belong together on a skinned target.
        /// </summary>
        internal static void Fill(AssetTypeValueField mesh, BakedMesh baked)
        {
            if (mesh == null) throw new ArgumentNullException(nameof(mesh));
            if (baked == null) throw new ArgumentNullException(nameof(baked));

            mesh["m_MeshCompression"].AsInt = 0;
            mesh["m_IsReadable"].AsBool = true;
            mesh["m_KeepVertices"].AsBool = true;
            mesh["m_KeepIndices"].AsBool = true;
            mesh["m_IndexFormat"].AsInt = baked.Index32 ? 1 : 0;

            SetBytes(mesh["m_IndexBuffer"]["Array"], baked.IndexData);

            AssetTypeValueField vd = mesh["m_VertexData"];
            vd["m_VertexCount"].AsUInt = (uint)baked.VertexCount;
            AssetTypeValueField channels = vd["m_Channels"]["Array"];
            // A Mesh built from the EMPTY class-database template carries ZERO channels, so a bake
            // that adds a Mesh rather than overwriting a shipped one would describe no vertex at all.
            // 14 is not a chosen number: every Mesh measured in mutoid/aln_fireworm/aln_poisonworm
            // (2019.4.31f1) carries exactly 14 slots, used or not, and the SLOT INDEX is the semantic
            // (0 vertex, 1 normal, 4 uv0, 12 blend weight, 13 blend index - see SkinFields).
            while (channels.Children.Count < ChannelCount)
                channels.Children.Add(ValueBuilder.DefaultValueFieldFromArrayTemplate(channels));
            for (int i = 0; i < channels.Children.Count; i++)
            {
                AssetTypeValueField c = channels.Children[i];
                c["stream"].AsInt = 0;
                c["format"].AsInt = FormatFloat32;
                switch (i)
                {
                    case ChannelVertex: c["offset"].AsInt = 0; c["dimension"].AsInt = 3; break;
                    case ChannelNormal: c["offset"].AsInt = 12; c["dimension"].AsInt = 3; break;
                    case ChannelUv0: c["offset"].AsInt = 24; c["dimension"].AsInt = 2; break;
                    default: c["offset"].AsInt = 0; c["dimension"].AsInt = 0; break;
                }
            }
            SetBytes(vd["m_DataSize"], baked.VertexData);

            // A non-empty m_StreamData sends the engine to a .resS for vertices that are now inline -
            // the same trap FillTexture2D guards (it reads zeroes rather than failing).
            AssetTypeValueField sd = mesh["m_StreamData"];
            if (!sd.IsDummy) { sd["offset"].AsULong = 0; sd["size"].AsUInt = 0; sd["path"].AsString = ""; }

            ClearArray(mesh["m_BakedConvexCollisionMesh"]["Array"]);
            ClearArray(mesh["m_BakedTriangleCollisionMesh"]["Array"]);
            AssetTypeValueField shapes = mesh["m_Shapes"];
            if (!shapes.IsDummy)
                foreach (string f in new[] { "vertices", "shapes", "channels", "fullWeights" })
                    if (!shapes[f].IsDummy) ClearArray(shapes[f]["Array"]);
            ClearCompressed(mesh["m_CompressedMesh"]);

            AssetTypeValueField aabb = mesh["m_LocalAABB"];
            SetVector3(aabb["m_Center"], baked.CenterX, baked.CenterY, baked.CenterZ);
            SetVector3(aabb["m_Extent"], baked.ExtentX, baked.ExtentY, baked.ExtentZ);

            // ONE SUBMESH PER MATERIAL, because Unity binds submesh i to the renderer's
            // m_Materials[i]. A model whose file named no materials still bakes as exactly one
            // submesh spanning every index, which is what every model produced before multi-material
            // import existed - so the spider's path through here is byte-identical.
            int[] counts = baked.SubmeshIndexCounts;
            if (counts == null || counts.Length == 0) counts = new[] { baked.IndexCount };
            int stride = baked.Index32 ? 4 : 2;

            AssetTypeValueField subs = mesh["m_SubMeshes"]["Array"];
            while (subs.Children.Count > counts.Length) subs.Children.RemoveAt(subs.Children.Count - 1);
            while (subs.Children.Count < counts.Length)
                subs.Children.Add(ValueBuilder.DefaultValueFieldFromArrayTemplate(subs));

            uint firstByte = 0;
            for (int i = 0; i < counts.Length; i++)
            {
                AssetTypeValueField sub = subs.Children[i];
                sub["firstByte"].AsUInt = firstByte;
                sub["indexCount"].AsUInt = (uint)counts[i];
                sub["topology"].AsInt = 0;                 // triangles
                sub["baseVertex"].AsUInt = 0;
                sub["firstVertex"].AsUInt = 0;
                sub["vertexCount"].AsUInt = (uint)baked.VertexCount;
                // ponytail: every submesh reports the WHOLE mesh's bounds. Unity uses these for
                // culling only, and a conservative box culls late rather than wrongly - a per-range
                // box would mean a second pass over the vertices each submesh actually touches.
                // Tighten it if a model ever culls visibly late.
                SetVector3(sub["localAABB"]["m_Center"], baked.CenterX, baked.CenterY, baked.CenterZ);
                SetVector3(sub["localAABB"]["m_Extent"], baked.ExtentX, baked.ExtentY, baked.ExtentZ);
                firstByte += (uint)(counts[i] * stride);
            }
        }

        /// <summary>
        /// What a Mesh in a FILE actually holds, in one line - the oracle both the offline round trip
        /// and the in-game P4 gate read, so a passing gate and a passing test mean the same thing.
        /// Reports the same shape <see cref="BakedMesh.Describe"/> prints.
        /// </summary>
        internal static string Summary(AssetTypeValueField mesh)
        {
            AssetTypeValueField aabb = mesh["m_LocalAABB"];
            byte[] indices = mesh["m_IndexBuffer"]["Array"].AsByteArray;
            bool wide = mesh["m_IndexFormat"].AsInt == 1;
            return "verts=" + mesh["m_VertexData"]["m_VertexCount"].AsUInt +
                   " indices=" + (indices == null ? 0 : indices.Length / (wide ? 4 : 2)) +
                   " format=" + (wide ? "UInt32" : "UInt16") +
                   " centre=" + V(aabb["m_Center"]) + " extent=" + V(aabb["m_Extent"]) +
                   " submeshes=" + mesh["m_SubMeshes"]["Array"].Children.Count +
                   " vertexBytes=" + (mesh["m_VertexData"]["m_DataSize"].AsByteArray == null
                                      ? 0 : mesh["m_VertexData"]["m_DataSize"].AsByteArray.Length) +
                   " streamPath='" + mesh["m_StreamData"]["path"].AsString + "'";
        }

        private static void SetBytes(AssetTypeValueField field, byte[] bytes)
        {
            field.Value = new AssetTypeValue(bytes, false);
            field.TemplateField.ValueType = AssetValueType.ByteArray;
        }

        private static void ClearArray(AssetTypeValueField array)
        {
            if (array.IsDummy) return;
            if (array.TemplateField.ValueType == AssetValueType.ByteArray) SetBytes(array, new byte[0]);
            else array.Children.Clear();
        }

        /// <summary>Every PackedBitVector under m_CompressedMesh back to "holds nothing".</summary>
        private static void ClearCompressed(AssetTypeValueField compressed)
        {
            if (compressed.IsDummy) return;
            foreach (AssetTypeValueField v in compressed.Children)
            {
                if (v.Children == null) continue;
                if (!v["m_NumItems"].IsDummy) v["m_NumItems"].AsUInt = 0;
                if (!v["m_BitSize"].IsDummy) v["m_BitSize"].AsInt = 0;
                if (!v["m_Data"].IsDummy) ClearArray(v["m_Data"]["Array"]);
            }
        }

        private static void SetVector3(AssetTypeValueField v, float x, float y, float z)
        {
            v["x"].AsFloat = x; v["y"].AsFloat = y; v["z"].AsFloat = z;
        }

        private static string V(AssetTypeValueField v)
        {
            // InvariantCulture: this line is machine-compared, and a ru-RU machine writes 0,5 for 0.5
            // (the same trap ReadMaterialProperties documents).
            return v["x"].AsFloat.ToString("0.###", CultureInfo.InvariantCulture) + "," +
                   v["y"].AsFloat.ToString("0.###", CultureInfo.InvariantCulture) + "," +
                   v["z"].AsFloat.ToString("0.###", CultureInfo.InvariantCulture);
        }
    }
}
