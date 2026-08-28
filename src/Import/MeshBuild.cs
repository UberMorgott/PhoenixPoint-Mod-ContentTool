using System;
using System.Collections.Generic;
using System.IO;

namespace Morgott.ContentTool.Import
{
    /// <summary>
    /// The vertex and index buffers a serialized Unity Mesh carries, already in the exact bytes the
    /// file wants. Free of UnityEngine types on purpose, like <see cref="ObjCodec"/>: the whole
    /// OBJ -&gt; buffers conversion is then provable offline, and the engine never takes part in a
    /// SHIPPING-mode bake anyway.
    /// </summary>
    internal sealed class BakedMesh
    {
        /// <summary>Bytes per vertex of <see cref="VertexData"/> - position, normal, uv0, all float32.</summary>
        internal const int Stride = 32;

        internal int VertexCount;
        internal int IndexCount;
        /// <summary>
        /// Index count per submesh, in the order <see cref="IndexData"/> writes them, so submesh i
        /// starts at the sum of everything before it. Empty means one submesh spanning the lot,
        /// which is what every model baked before multi-material import produced.
        /// </summary>
        internal int[] SubmeshIndexCounts = new int[0];
        /// <summary>Interleaved stream 0: 3f position, 3f normal, 2f uv0.</summary>
        internal byte[] VertexData;
        /// <summary>UInt16 or UInt32 indices, matching <see cref="Index32"/> (Mesh.m_IndexFormat).</summary>
        internal byte[] IndexData;
        internal bool Index32;
        internal float CenterX, CenterY, CenterZ;
        internal float ExtentX, ExtentY, ExtentZ;

        internal string Describe()
        {
            return "verts=" + VertexCount + " indices=" + IndexCount +
                   " format=" + (Index32 ? "UInt32" : "UInt16") +
                   " centre=" + F(CenterX) + "," + F(CenterY) + "," + F(CenterZ) +
                   " extent=" + F(ExtentX) + "," + F(ExtentY) + "," + F(ExtentZ);
        }

        private static string F(float v)
        {
            return v.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
        }
    }

    /// <summary>
    /// ObjDocument -&gt; BakedMesh. The de-duplication and the missing-normal fallback are RR's
    /// <c>MeshReplacer.ToUnityMesh</c> (`MeshReplacer.cs:3287-3330`) written against buffers instead
    /// of against a live Mesh: same vertex key (position/uv/normal triple), same "no flip, no winding
    /// change", same zero uv where a face names none.
    /// </summary>
    internal static class MeshBuild
    {
        internal static BakedMesh From(ObjDocument document)
        {
            if (document == null) throw new ArgumentNullException(nameof(document));

            Dictionary<ObjVertex, int> seen = new Dictionary<ObjVertex, int>();
            List<ObjVector3> positions = new List<ObjVector3>();
            List<ObjVector3> normals = new List<ObjVector3>();
            List<ObjVector2> uvs = new List<ObjVector2>();
            List<int> indices = new List<int>();
            bool allNormals = true;

            // ponytail: every group is merged into ONE submesh. The shipped meshes this replaces
            // declare one, and a submesh the renderer has no material for draws nothing - rebuild the
            // m_SubMeshes array per group when a multi-material target actually needs it.
            foreach (ObjTriangle t in document.Triangles)
            {
                indices.Add(Index(t.A, document, seen, positions, normals, uvs, ref allNormals));
                indices.Add(Index(t.B, document, seen, positions, normals, uvs, ref allNormals));
                indices.Add(Index(t.C, document, seen, positions, normals, uvs, ref allNormals));
            }
            if (!allNormals) Recalculate(positions, indices, normals);

            BakedMesh baked = new BakedMesh
            {
                VertexCount = positions.Count,
                IndexCount = indices.Count,
                Index32 = positions.Count > ushort.MaxValue
            };
            Bounds(positions, baked);

            using (MemoryStream vs = new MemoryStream(positions.Count * BakedMesh.Stride))
            using (BinaryWriter vw = new BinaryWriter(vs))
            {
                for (int i = 0; i < positions.Count; i++)
                {
                    vw.Write(positions[i].X); vw.Write(positions[i].Y); vw.Write(positions[i].Z);
                    vw.Write(normals[i].X); vw.Write(normals[i].Y); vw.Write(normals[i].Z);
                    vw.Write(uvs[i].X); vw.Write(uvs[i].Y);
                }
                vw.Flush();
                baked.VertexData = vs.ToArray();
            }

            using (MemoryStream ist = new MemoryStream(indices.Count * (baked.Index32 ? 4 : 2)))
            using (BinaryWriter iw = new BinaryWriter(ist))
            {
                foreach (int i in indices)
                {
                    if (baked.Index32) iw.Write((uint)i); else iw.Write((ushort)i);
                }
                iw.Flush();
                baked.IndexData = ist.ToArray();
            }
            return baked;
        }

        private static int Index(ObjVertex v, ObjDocument document, Dictionary<ObjVertex, int> seen,
                                 List<ObjVector3> positions, List<ObjVector3> normals,
                                 List<ObjVector2> uvs, ref bool allNormals)
        {
            int index;
            if (seen.TryGetValue(v, out index)) return index;

            index = positions.Count;
            seen.Add(v, index);
            positions.Add(document.Positions[v.Position]);
            uvs.Add(v.Texture >= 0 ? document.TextureCoordinates[v.Texture] : new ObjVector2(0f, 0f));
            if (v.Normal >= 0) normals.Add(document.Normals[v.Normal]);
            else { normals.Add(new ObjVector3(0f, 0f, 0f)); allNormals = false; }
            return index;
        }

        /// <summary>
        /// Mesh.RecalculateNormals in buffer form: accumulate each face's cross product on its three
        /// vertices, then normalize. A .obj with no `vn` is otherwise lit as if it were black.
        /// </summary>
        private static void Recalculate(List<ObjVector3> positions, List<int> indices, List<ObjVector3> normals)
        {
            float[] nx = new float[positions.Count], ny = new float[positions.Count], nz = new float[positions.Count];
            for (int i = 0; i + 2 < indices.Count; i += 3)
            {
                int a = indices[i], b = indices[i + 1], c = indices[i + 2];
                float abx = positions[b].X - positions[a].X, aby = positions[b].Y - positions[a].Y, abz = positions[b].Z - positions[a].Z;
                float acx = positions[c].X - positions[a].X, acy = positions[c].Y - positions[a].Y, acz = positions[c].Z - positions[a].Z;
                float fx = aby * acz - abz * acy, fy = abz * acx - abx * acz, fz = abx * acy - aby * acx;
                nx[a] += fx; ny[a] += fy; nz[a] += fz;
                nx[b] += fx; ny[b] += fy; nz[b] += fz;
                nx[c] += fx; ny[c] += fy; nz[c] += fz;
            }
            for (int i = 0; i < normals.Count; i++)
            {
                double len = Math.Sqrt(nx[i] * (double)nx[i] + ny[i] * (double)ny[i] + nz[i] * (double)nz[i]);
                normals[i] = len > 0.0
                    ? new ObjVector3((float)(nx[i] / len), (float)(ny[i] / len), (float)(nz[i] / len))
                    : new ObjVector3(0f, 1f, 0f);
            }
        }

        /// <summary>m_LocalAABB: centre and HALF-extent, which is what Unity serializes.</summary>
        private static void Bounds(List<ObjVector3> positions, BakedMesh baked)
        {
            float minX = float.MaxValue, minY = float.MaxValue, minZ = float.MaxValue;
            float maxX = float.MinValue, maxY = float.MinValue, maxZ = float.MinValue;
            foreach (ObjVector3 p in positions)
            {
                if (p.X < minX) minX = p.X; if (p.X > maxX) maxX = p.X;
                if (p.Y < minY) minY = p.Y; if (p.Y > maxY) maxY = p.Y;
                if (p.Z < minZ) minZ = p.Z; if (p.Z > maxZ) maxZ = p.Z;
            }
            baked.CenterX = (minX + maxX) * 0.5f; baked.ExtentX = (maxX - minX) * 0.5f;
            baked.CenterY = (minY + maxY) * 0.5f; baked.ExtentY = (maxY - minY) * 0.5f;
            baked.CenterZ = (minZ + maxZ) * 0.5f; baked.ExtentZ = (maxZ - minZ) * 0.5f;
        }
    }
}
