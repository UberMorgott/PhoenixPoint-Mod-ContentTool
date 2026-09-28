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
        /// <summary>
        /// Bytes per vertex of <see cref="VertexData"/> - position, normal, tangent, uv0, all float32,
        /// in Unity's channel order (slot 0, 1, 2, 4). The TANGENT is not optional: the shipped donor
        /// meshes carry one (stream0 pos/normal/tangent, stride 40 - SkinFields' class remark) and
        /// their materials sample a _BumpMap, which without a tangent channel shades as garbage.
        /// One stream rather than the donor's pos|uv split: the skin stream still follows it
        /// 16-byte aligned, and 48 is a multiple of 16.
        /// </summary>
        internal const int Stride = 48;
        internal const int OffsetNormal = 12, OffsetTangent = 24, OffsetUv0 = 40;

        internal int VertexCount;
        internal int IndexCount;
        /// <summary>
        /// Index count per submesh, in the order <see cref="IndexData"/> writes them, so submesh i
        /// starts at the sum of everything before it. Empty means one submesh spanning the lot,
        /// which is what every model baked before multi-material import produced.
        /// </summary>
        internal int[] SubmeshIndexCounts = new int[0];
        /// <summary>Interleaved stream 0: 3f position, 3f normal, 4f tangent (w = handedness), 2f uv0.</summary>
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
            // Per vertex: the face named no `vn`. Only THOSE are recomputed - a file whose author
            // left one stray face without normals keeps every hard edge it states everywhere else.
            List<bool> missing = new List<bool>();

            // ponytail: every group is merged into ONE submesh. The shipped meshes this replaces
            // declare one, and a submesh the renderer has no material for draws nothing - rebuild the
            // m_SubMeshes array per group when a multi-material target actually needs it.
            foreach (ObjTriangle t in document.Triangles)
            {
                indices.Add(Index(t.A, document, seen, positions, normals, uvs, missing));
                indices.Add(Index(t.B, document, seen, positions, normals, uvs, missing));
                indices.Add(Index(t.C, document, seen, positions, normals, uvs, missing));
            }
            if (missing.Contains(true)) Recalculate(positions, indices, normals, missing);

            BakedMesh baked = new BakedMesh
            {
                VertexCount = positions.Count,
                IndexCount = indices.Count,
                Index32 = positions.Count > ushort.MaxValue
            };
            Bounds(positions, baked);
            baked.VertexData = Vertices(positions, normals, Tangents(positions, normals, uvs, indices), uvs);

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
                                 List<ObjVector2> uvs, List<bool> missing)
        {
            int index;
            if (seen.TryGetValue(v, out index)) return index;

            index = positions.Count;
            seen.Add(v, index);
            positions.Add(document.Positions[v.Position]);
            uvs.Add(v.Texture >= 0 ? document.TextureCoordinates[v.Texture] : new ObjVector2(0f, 0f));
            if (v.Normal >= 0) normals.Add(document.Normals[v.Normal]);
            else normals.Add(new ObjVector3(0f, 0f, 0f));
            missing.Add(v.Normal < 0);
            return index;
        }

        /// <summary>
        /// Stream 0 in <see cref="BakedMesh.Stride"/> layout. <paramref name="uvs"/> may be null (zero
        /// uv); <paramref name="tangents"/> is 4 floats per vertex. The ONE writer both importers use,
        /// so the byte layout cannot drift between the .obj and the .glb route.
        /// </summary>
        internal static byte[] Vertices(IList<ObjVector3> positions, IList<ObjVector3> normals,
                                        float[] tangents, IList<ObjVector2> uvs)
        {
            using (MemoryStream vs = new MemoryStream(positions.Count * BakedMesh.Stride))
            using (BinaryWriter vw = new BinaryWriter(vs))
            {
                for (int i = 0; i < positions.Count; i++)
                {
                    ObjVector2 uv = uvs == null ? new ObjVector2(0f, 0f) : uvs[i];
                    vw.Write(positions[i].X); vw.Write(positions[i].Y); vw.Write(positions[i].Z);
                    vw.Write(normals[i].X); vw.Write(normals[i].Y); vw.Write(normals[i].Z);
                    vw.Write(tangents[i * 4]); vw.Write(tangents[i * 4 + 1]);
                    vw.Write(tangents[i * 4 + 2]); vw.Write(tangents[i * 4 + 3]);
                    vw.Write(uv.X); vw.Write(uv.Y);
                }
                vw.Flush();
                return vs.ToArray();
            }
        }

        /// <summary>
        /// Per-vertex tangents out of the UV layout - Lengyel's accumulation, the scheme
        /// Mesh.RecalculateTangents uses: each triangle's dP/du (tangent) and dP/dv (bitangent)
        /// summed onto its corners, then the tangent made orthogonal to the normal and w set to the
        /// handedness, -1 where cross(normal, tangent) points AGAINST dP/dv. Winding-independent (a
        /// swapped triangle negates numerator and determinant alike), so it holds after the .glb's
        /// axis flip. A vertex no UV-bearing triangle reaches (no `vt`, a zero-area UV island) gets an
        /// arbitrary perpendicular and w = +1: harmless for a model without a normal map, and there is
        /// no direction to recover for one with.
        /// </summary>
        internal static float[] Tangents(IList<ObjVector3> p, IList<ObjVector3> n, IList<ObjVector2> uv,
                                         IEnumerable<int> indices)
        {
            int count = p.Count;
            double[] t = new double[count * 3], b = new double[count * 3];
            if (uv != null)
            {
                int[] tri = new int[3];
                int k = 0;
                foreach (int index in indices)
                {
                    tri[k++] = index;
                    if (k < 3) continue;
                    k = 0;
                    int i0 = tri[0], i1 = tri[1], i2 = tri[2];
                    double x1 = p[i1].X - p[i0].X, y1 = p[i1].Y - p[i0].Y, z1 = p[i1].Z - p[i0].Z;
                    double x2 = p[i2].X - p[i0].X, y2 = p[i2].Y - p[i0].Y, z2 = p[i2].Z - p[i0].Z;
                    double s1 = uv[i1].X - uv[i0].X, t1 = uv[i1].Y - uv[i0].Y;
                    double s2 = uv[i2].X - uv[i0].X, t2 = uv[i2].Y - uv[i0].Y;
                    double det = s1 * t2 - s2 * t1;
                    if (Math.Abs(det) < 1e-20) continue;
                    double r = 1.0 / det;
                    double tx = (t2 * x1 - t1 * x2) * r, ty = (t2 * y1 - t1 * y2) * r, tz = (t2 * z1 - t1 * z2) * r;
                    double bx = (s1 * x2 - s2 * x1) * r, by = (s1 * y2 - s2 * y1) * r, bz = (s1 * z2 - s2 * z1) * r;
                    foreach (int v in tri)
                    {
                        t[v * 3] += tx; t[v * 3 + 1] += ty; t[v * 3 + 2] += tz;
                        b[v * 3] += bx; b[v * 3 + 1] += by; b[v * 3 + 2] += bz;
                    }
                }
            }

            float[] result = new float[count * 4];
            for (int v = 0; v < count; v++)
            {
                double nx = n[v].X, ny = n[v].Y, nz = n[v].Z;
                // A .obj `vn` is written as stated, not normalised - the projection below needs a unit n.
                double nl = Math.Sqrt(nx * nx + ny * ny + nz * nz);
                if (nl > 1e-12) { nx /= nl; ny /= nl; nz /= nl; }
                double tx = t[v * 3], ty = t[v * 3 + 1], tz = t[v * 3 + 2];
                // Gram-Schmidt: the part of the tangent that lies in the surface.
                double d = nx * tx + ny * ty + nz * tz;
                tx -= nx * d; ty -= ny * d; tz -= nz * d;
                double len = Math.Sqrt(tx * tx + ty * ty + tz * tz);
                double w = 1.0;
                if (len > 1e-12 && !double.IsNaN(len))
                {
                    tx /= len; ty /= len; tz /= len;
                    double cx = ny * tz - nz * ty, cy = nz * tx - nx * tz, cz = nx * ty - ny * tx;
                    if (cx * b[v * 3] + cy * b[v * 3 + 1] + cz * b[v * 3 + 2] < 0.0) w = -1.0;
                }
                else Perpendicular(nx, ny, nz, out tx, out ty, out tz);
                result[v * 4] = (float)tx; result[v * 4 + 1] = (float)ty;
                result[v * 4 + 2] = (float)tz; result[v * 4 + 3] = (float)w;
            }
            return result;
        }

        /// <summary>Some unit vector perpendicular to the normal (+X for a zero normal).</summary>
        private static void Perpendicular(double nx, double ny, double nz, out double tx, out double ty, out double tz)
        {
            // The axis least aligned with the normal, minus its normal component.
            double ax = Math.Abs(nx) < 0.9 ? 1.0 : 0.0, ay = 1.0 - ax;
            double d = nx * ax + ny * ay;
            tx = ax - nx * d; ty = ay - ny * d; tz = -nz * d;
            double len = Math.Sqrt(tx * tx + ty * ty + tz * tz);
            if (len < 1e-12) { tx = 1.0; ty = 0.0; tz = 0.0; return; }
            tx /= len; ty /= len; tz /= len;
        }

        /// <summary>
        /// Mesh.RecalculateNormals in buffer form: accumulate each face's cross product on its three
        /// vertices, then normalize - written only where <paramref name="missing"/> says the file gave
        /// none. A .obj with no `vn` is otherwise lit as if it were black.
        /// </summary>
        private static void Recalculate(List<ObjVector3> positions, List<int> indices, List<ObjVector3> normals,
                                        List<bool> missing)
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
                if (!missing[i]) continue;
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
