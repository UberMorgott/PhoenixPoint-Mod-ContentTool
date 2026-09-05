using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
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
        /// A submesh drawn by this many triangles or fewer is a leftover shard, not a material part.
        /// Absolute rather than a share of the mesh, because the share a real part takes varies wildly
        /// (a visor is a fraction of a percent of a body) while NOTHING anybody meant to paint
        /// separately is drawn by eight triangles - a cube is twelve. Measured on the case that
        /// prompted this: an author's torso arrived as primitive 0 = 1 triangle plus primitive 1 =
        /// 15647, and the 15647 silently took the target's SECOND material.
        /// </summary>
        private const int ShardTriangles = 8;

        /// <summary>
        /// WHICH source part landed on WHICH of the target's materials, and whether that mapping looks
        /// like an accident. Unity draws submesh i with m_Materials[i] and the bake preserves the
        /// file's primitive order, so a stray one-triangle primitive at slot 0 pushes every real
        /// triangle onto the material after it - which reaches the author as a mangled model and no
        /// reason. null when there is nothing to say (one part onto one slot).
        /// </summary>
        /// <param name="suspect">true when a part is small enough to be a shard
        /// (<see cref="ShardTriangles"/>) while another is not - the caller reports it as a problem
        /// and bakes anyway, because the file is legal, just probably not what was meant.</param>
        internal static string SubmeshReport(string fileName, IList<int> triangleCounts,
                                             IList<string> materialSlots, out bool suspect)
        {
            suspect = false;
            int parts = triangleCounts == null ? 0 : triangleCounts.Count;
            int slots = materialSlots == null ? 0 : materialSlots.Count;
            if (parts <= 1 && slots <= 1) return null;

            int biggest = 0;
            for (int i = 0; i < parts; i++) if (triangleCounts[i] > biggest) biggest = triangleCounts[i];
            int shard = -1;
            for (int i = 0; i < parts && shard < 0; i++)
                if (triangleCounts[i] <= ShardTriangles && biggest > ShardTriangles) shard = i;

            var map = new StringBuilder();
            for (int i = 0; i < parts; i++)
            {
                if (i > 0) map.Append(", ");
                map.Append("part ").Append((i + 1).ToString(CultureInfo.InvariantCulture))
                   .Append(" (").Append(triangleCounts[i].ToString(CultureInfo.InvariantCulture))
                   .Append(triangleCounts[i] == 1 ? " triangle) -> " : " triangles) -> ")
                   .Append(i < slots ? "material '" + materialSlots[i] + "'"
                           : slots == 0 ? "material unknown (no renderer in this bundle draws this mesh)"
                                        : "NO material (not drawn)");
            }
            if (shard < 0) return fileName + ": " + map;

            suspect = true;
            int real = shard == 0 ? 1 : 0;
            for (int i = 0; i < parts; i++) if (triangleCounts[i] == biggest) real = i;
            // ONLY a shard with REAL geometry behind it displaces anything: material N follows part N,
            // so trailing shards shift nothing and saying otherwise would be a false claim. "Real",
            // not "any part": [900, 1, 1] has a part after the first shard, and still moves nothing.
            bool realFollows = false;
            for (int i = shard + 1; i < parts; i++) if (triangleCounts[i] > ShardTriangles) realFollows = true;
            string consequence = realFollows
                ? ", and every part after it takes the material meant for the part before - which is " +
                  "why your real geometry is painted wrongly. "
                : ", and with no real geometry after it nothing is displaced - but it still claims a " +
                  "material slot of its own. ";
            return fileName + " part " + (shard + 1).ToString(CultureInfo.InvariantCulture) + " of " +
                   parts.ToString(CultureInfo.InvariantCulture) + " has only " +
                   triangleCounts[shard].ToString(CultureInfo.InvariantCulture) +
                   (triangleCounts[shard] == 1 ? " triangle" : " triangles") + " while part " +
                   (real + 1).ToString(CultureInfo.InvariantCulture) + " has " +
                   biggest.ToString(CultureInfo.InvariantCulture) +
                   ". The game paints part N with the target's material N, so " + map +
                   ". A part that small is almost always a leftover shard" + consequence +
                   "In Blender select the mesh, Edit Mode, select all (A) and Mesh > Merge > By " +
                   "Distance, or assign every face to ONE material slot, then re-export - or order " +
                   "the parts to match the target's materials. Baked anyway; nothing was skipped.";
        }

        /// <summary>
        /// The names of the materials a Mesh is drawn with, in submesh order - the other half of what
        /// <see cref="SubmeshReport"/> needs. Read off the renderer that USES the mesh, the same walk
        /// <see cref="SkinFields.BoneNames"/> makes for bones: skinned renderers point at the mesh
        /// themselves, static ones do it through a MeshFilter on the same GameObject. null ONLY when
        /// nothing in this file draws the mesh - renderer variants that disagree (default/Gold/Xmas
        /// all draw CHR_PX_HVY_TS_M_V01) keep their shared slot count and name the difference, because
        /// "several renderers disagree" and "there are no materials" are not the same answer.
        /// </summary>
        internal static string[] MaterialNames(AssetsManager m, AssetsFileInstance af, long meshPathId)
        {
            var drawn = new List<string[]>();
            foreach (AssetClassID kind in new[] { AssetClassID.SkinnedMeshRenderer, AssetClassID.MeshRenderer })
                foreach (AssetFileInfo i in af.file.Metadata.GetAssetsOfType(kind))
                {
                    AssetTypeValueField r = m.GetBaseField(af, i);
                    if (kind == AssetClassID.SkinnedMeshRenderer)
                    {
                        if (r["m_Mesh"]["m_PathID"].AsLong != meshPathId) continue;
                    }
                    else if (!DrawsFiltered(m, af, r["m_GameObject"]["m_PathID"].AsLong, meshPathId)) continue;

                    var names = new List<string>();
                    foreach (AssetTypeValueField p in r["m_Materials"]["Array"].Children)
                    {
                        AssetTypeValueField mat = PrefabFields.Get(m, af, p["m_PathID"].AsLong);
                        names.Add(mat == null || mat["m_Name"].IsDummy || string.IsNullOrEmpty(mat["m_Name"].AsString)
                                  ? "slot " + names.Count.ToString(CultureInfo.InvariantCulture)
                                  : mat["m_Name"].AsString);
                    }
                    drawn.Add(names.ToArray());
                }
            return Fold(drawn);
        }

        /// <summary>
        /// One display name per material slot, out of what each renderer that draws the mesh calls
        /// that slot. The variants DISAGREEING is normal - a shipped mesh has its default, Gold and
        /// Xmas renderers - so the alternatives are kept as NAMES and only joined into " or " text
        /// here, at the one place that renders them. Parsing that text back into names is what let a
        /// material legitimately called 'Red or Blue' fold into itself twice, and what let
        /// 'ALN_Fireworm_DMG' swallow 'ALN_Fireworm' when the test was Contains. null when nothing
        /// draws the mesh, which is a different answer from "the renderers disagree".
        /// </summary>
        internal static string[] Fold(IList<string[]> renderers)
        {
            if (renderers == null || renderers.Count == 0) return null;
            var slots = new List<List<string>>();
            bool seen = false;
            foreach (string[] names in renderers)
            {
                while (slots.Count < names.Length)
                    slots.Add(seen ? new List<string> { "none" } : new List<string>());
                for (int b = 0; b < slots.Count; b++)
                {
                    string n = b < names.Length ? names[b] : "none";
                    if (!slots[b].Contains(n)) slots[b].Add(n);
                }
                seen = true;
            }
            string[] found = new string[slots.Count];
            for (int b = 0; b < slots.Count; b++)
                found[b] = string.Join(" or ", slots[b].ToArray()) +
                           (slots[b].Count > 1 ? " (varies by renderer variant)" : "");
            return found;
        }

        /// <summary>Does a MeshFilter on <paramref name="gameObject"/> carry this mesh?</summary>
        private static bool DrawsFiltered(AssetsManager m, AssetsFileInstance af, long gameObject, long meshPathId)
        {
            if (gameObject == 0) return false;
            foreach (AssetFileInfo i in af.file.Metadata.GetAssetsOfType(AssetClassID.MeshFilter))
            {
                AssetTypeValueField f = m.GetBaseField(af, i);
                if (f["m_GameObject"]["m_PathID"].AsLong == gameObject &&
                    f["m_Mesh"]["m_PathID"].AsLong == meshPathId) return true;
            }
            return false;
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

        /// <summary>
        /// The raw vertex + index BYTES as one hash - the question <see cref="Summary"/> deliberately
        /// does not ask. Summary compares counts, index format and ROUNDED bounds, so a patch that wrote
        /// nothing at all reads back identical to the mesh the game shipped; the buffers cannot.
        /// A mesh that streams its vertices out of the .resS reports that path instead of a hash -
        /// nothing this tool writes streams (<see cref="Fill"/> clears m_StreamData), so a streamed
        /// answer can never equal a written one.
        /// </summary>
        internal static string Buffers(AssetTypeValueField mesh)
        {
            byte[] verts = mesh["m_VertexData"]["m_DataSize"].AsByteArray ?? new byte[0];
            byte[] indices = mesh["m_IndexBuffer"]["Array"].AsByteArray ?? new byte[0];
            AssetTypeValueField sd = mesh["m_StreamData"];
            string path = sd == null || sd.IsDummy ? "" : (sd["path"].AsString ?? "");
            if (verts.Length == 0 && path.Length != 0) return "streamed from '" + path + "'";
            var all = new byte[verts.Length + indices.Length];
            Buffer.BlockCopy(verts, 0, all, 0, verts.Length);
            Buffer.BlockCopy(indices, 0, all, verts.Length, indices.Length);
            return AliasMap.Sha256(all) + " vertexBytes=" + verts.Length + " indexBytes=" + indices.Length;
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
