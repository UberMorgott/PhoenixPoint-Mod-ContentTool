using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Morgott.ContentTool.Import;

namespace Morgott.ContentTool.Bake
{
    /// <summary>
    /// ============ A STATIC MODEL OFF THE INTERNET, MERGED INTO ONE MESH ============
    ///
    /// WHY THIS EXISTS. A modder who downloads a gun gets what an artist exported, and that is
    /// routinely a dozen separate meshes sharing two or three materials - MEASURED on two real
    /// downloads: `ar181.glb` (still in demos\WeaponAdd\Content\Models\) is 14 meshes / 3 materials,
    /// and a Tau pulse pistol was 9 meshes / 1 material (that file was deleted from the repository
    /// for licence reasons; the measurement is kept). Refusing those files and telling the author
    /// to go and join them in Blender is the tool failing at its own job: very little is supposed to
    /// be required of the modder.
    ///
    /// WHY THE OLD RULE WAS WRONG, precisely. "Take the mesh a skin drives" was derived from a
    /// CREATURE, where a rig drives the model and nothing else, so the skin IS the answer. A gun has
    /// no rig and never should have one, so that rule reads a static model as "no armature drives any
    /// of them" and refuses a file that is perfectly well formed. The rule is right for creatures and
    /// wrong for props; this class is the prop half, and the skinned path is deliberately untouched -
    /// <see cref="Static"/> REFUSES anything carrying joints, so the creature line cannot regress
    /// through it.
    ///
    /// SUBMESHES, NOT ATLASING - and the game's own content is what decided it. Unity binds submesh
    /// <c>i</c> to <c>m_Materials[i]</c>, and Phoenix Point's shipped prefabs lean on that hard:
    /// across <c>extracted\GameData\prefabs</c>, 2533 of 3463 renderer material arrays hold MORE THAN
    /// ONE material against 930 holding exactly one. Multi-material renderers are the normal case in
    /// this game, so keeping the parts as submeshes is what the engine already does. Atlasing would
    /// mean repacking someone else's textures and rewriting their UVs - lossy, far more code, and it
    /// throws away information the engine wanted anyway.
    ///
    /// The internal model already supports this on the EXPORT side: <c>SkinnedModel.Submeshes</c> and
    /// <c>.Materials</c> are lists, and <c>MeshRead</c>/<c>GlbCodec</c> fill and write N of them when
    /// reading a mesh OUT of the game. This class makes the IMPORT direction symmetric.
    ///
    /// ONE SUBMESH PER DISTINCT MATERIAL, not per source mesh. That is what keeps the result sane:
    /// AR-181's fourteen pieces collapse to THREE submeshes because they only ever used three
    /// materials, and the Tau pistol's nine collapse to ONE. Fourteen submeshes would be fourteen
    /// draw calls describing the same three surfaces.
    /// </summary>
    internal static class MeshMerge
    {
        /// <summary>
        /// Merges the primitives of a static model into a single mesh whose submeshes are its
        /// distinct materials, in first-seen order.
        ///
        /// Returns null and sets <paramref name="refusal"/> when the parts cannot honestly be
        /// merged. REFUSING BY NAME IS THE POINT: a mangled gun that renders is far worse than a
        /// message saying which piece was wrong, because the author only finds the first kind after
        /// shipping it.
        /// </summary>
        /// <param name="parts">one entry per glTF primitive, in file order.</param>
        /// <param name="name">the merged mesh's name.</param>
        /// <param name="note">what was done, for the bake log - never silent.</param>
        internal static SkinnedModel Static(IList<SkinnedModel> parts, string name,
                                            out string refusal, out string note)
        {
            refusal = null;
            note = null;
            if (parts == null || parts.Count == 0)
            {
                refusal = "the file contains no mesh; export the model itself, not an empty scene";
                return null;
            }

            // The creature line's guarantee: this path never touches a skinned model. A rigged mesh
            // merged naively would need a shared bone list and a vertex rebase, which is a different
            // job with a different failure mode - GlbReader's skin-driven pick still owns that case.
            for (int i = 0; i < parts.Count; i++)
                if (Skinned(parts[i]))
                {
                    refusal = "part " + Describe(parts, i) + " carries a skin (joints and weights). " +
                              "Merging rigged meshes needs a shared bone list and a vertex rebase, " +
                              "which this path deliberately does not do; a skinned model is picked by " +
                              "its rig instead.";
                    return null;
                }

            // UVs are all-or-nothing. Half a model with texture coordinates and half without cannot
            // be painted by one material set, and silently zeroing the missing half would put the
            // whole of one texture's top-left texel across those pieces.
            bool anyUv = false, allUv = true;
            for (int i = 0; i < parts.Count; i++)
            {
                bool has = parts[i].Uv0 != null && parts[i].Uv0.Length > 0;
                anyUv |= has;
                allUv &= has;
            }
            if (anyUv && !allUv)
            {
                List<string> without = new List<string>();
                for (int i = 0; i < parts.Count; i++)
                    if (parts[i].Uv0 == null || parts[i].Uv0.Length == 0) without.Add(Describe(parts, i));
                refusal = "some pieces carry texture coordinates and some do not, so one material set " +
                          "cannot paint them: " + string.Join(", ", without.ToArray()) +
                          " have no UVs. Unwrap them, or delete them if they are scene furniture.";
                return null;
            }

            // Tangents and UV1 are optional and likewise all-or-nothing, but they DEGRADE rather than
            // refuse: dropping them costs normal-map handedness and a lightmap channel, neither of
            // which the baked Standard material binds today. Say so rather than dropping in silence.
            bool allTangents = true, allUv1 = true;
            for (int i = 0; i < parts.Count; i++)
            {
                allTangents &= parts[i].Tangents != null && parts[i].Tangents.Length > 0;
                allUv1 &= parts[i].Uv1 != null && parts[i].Uv1.Length > 0;
            }

            // --- group the parts by material, first-seen order preserved.
            List<string> materials = new List<string>();
            List<byte[]> images = new List<byte[]>();
            List<float[]> emissive = new List<float[]>();
            List<List<int>> groups = new List<List<int>>();
            for (int i = 0; i < parts.Count; i++)
            {
                string mat = MaterialOf(parts[i], i);
                int at = materials.IndexOf(mat);
                if (at < 0)
                {
                    materials.Add(mat);
                    images.Add(null);
                    emissive.Add(null);
                    groups.Add(new List<int>());
                    at = materials.Count - 1;
                }
                // The first piece in a group that actually carries an image supplies it. Pieces
                // sharing a material name share its texture by definition, so a later null - a piece
                // whose primitive named the material but no texture - must not erase it.
                if (images[at] == null && parts[i].MaterialImages.Count > 0)
                    images[at] = parts[i].MaterialImages[0];
                if (emissive[at] == null && parts[i].MaterialEmissive.Count > 0)
                    emissive[at] = parts[i].MaterialEmissive[0];
                groups[at].Add(i);
            }

            // --- concatenate, group by group, so a submesh's triangles are contiguous.
            SkinnedModel merged = new SkinnedModel { Name = name };
            List<ObjVector3> positions = new List<ObjVector3>();
            List<ObjVector3> normals = new List<ObjVector3>();
            List<ObjVector2> uv0 = new List<ObjVector2>();
            List<ObjVector2> uv1 = new List<ObjVector2>();
            List<float> tangents = new List<float>();

            for (int g = 0; g < groups.Count; g++)
            {
                List<int> triangles = new List<int>();
                foreach (int p in groups[g])
                {
                    SkinnedModel part = parts[p];
                    int baseVertex = positions.Count;

                    positions.AddRange(part.Positions ?? new ObjVector3[0]);
                    // A missing normal array is not a refusal - it is a flat-shaded export. Zero
                    // normals would light the piece black, so fall back to +Y, which at least lights.
                    int count = part.Positions == null ? 0 : part.Positions.Length;
                    if (part.Normals != null && part.Normals.Length == count) normals.AddRange(part.Normals);
                    else for (int v = 0; v < count; v++) normals.Add(new ObjVector3(0f, 1f, 0f));

                    if (allUv) uv0.AddRange(part.Uv0);
                    if (allUv1) uv1.AddRange(part.Uv1);
                    if (allTangents) tangents.AddRange(part.Tangents);

                    foreach (int[] sub in Triangles(part))
                        for (int t = 0; t < sub.Length; t++)
                            triangles.Add(sub[t] + baseVertex);
                }
                merged.Submeshes.Add(triangles.ToArray());
                merged.Materials.Add(materials[g]);
                merged.MaterialImages.Add(images[g]);
                merged.MaterialEmissive.Add(emissive[g]);
            }

            // NORMALISE TO ONE UNIT ON THE LONGEST AXIS.
            //
            // A merged model carries every piece's NODE WORLD TRANSFORM, and an exporter's root scale
            // is arbitrary - so the author's units survive into the mesh. MEASURED in the baked
            // bundle: tau_pulse_pistol came out with m_LocalAABB extent (17.686, 8.486, 3.036), a
            // thirty-five unit object, which in the equipment screen is a colossal slab that occludes
            // the entire soldier. ar-181 came out 7 units long. Neither is a gun any more.
            //
            // The runtime fit scales this into the donor weapon's own box, but it is not allowed to
            // be the ONLY thing standing between the player and that slab: if the donor cannot be
            // measured, "leave the model as it arrives" ships the giant. Normalising here means the
            // worst case is a one-unit gun - wrong, but recognisably a gun - and the fit then does
            // the real work from a known baseline.
            //
            // Single-mesh models do not come through here at all, so a model already fitted offline
            // (the demo's sniper, whose box matches the shipped weapon's exactly) is untouched.
            float[] lo = { float.MaxValue, float.MaxValue, float.MaxValue };
            float[] hi = { float.MinValue, float.MinValue, float.MinValue };
            foreach (ObjVector3 p in positions)
            {
                if (p.X < lo[0]) lo[0] = p.X; if (p.X > hi[0]) hi[0] = p.X;
                if (p.Y < lo[1]) lo[1] = p.Y; if (p.Y > hi[1]) hi[1] = p.Y;
                if (p.Z < lo[2]) lo[2] = p.Z; if (p.Z > hi[2]) hi[2] = p.Z;
            }
            float longest = 0f;
            for (int i = 0; i < 3; i++) if (hi[i] - lo[i] > longest) longest = hi[i] - lo[i];
            float normalise = longest > 1e-6f ? 1f / longest : 1f;
            if (Math.Abs(normalise - 1f) > 1e-6f)
                for (int i = 0; i < positions.Count; i++)
                    positions[i] = new ObjVector3(positions[i].X * normalise,
                                                  positions[i].Y * normalise,
                                                  positions[i].Z * normalise);

            merged.Positions = positions.ToArray();
            merged.Normals = normals.ToArray();
            if (allUv) merged.Uv0 = uv0.ToArray();
            if (allUv1) merged.Uv1 = uv1.ToArray();
            if (allTangents) merged.Tangents = tangents.ToArray();

            int tris = 0;
            foreach (int[] sub in merged.Submeshes) tris += sub.Length / 3;
            StringBuilder said = new StringBuilder();
            said.Append("merged ").Append(parts.Count.ToString(CultureInfo.InvariantCulture))
                .Append(" piece(s) into 1 mesh, ")
                .Append(merged.Submeshes.Count.ToString(CultureInfo.InvariantCulture))
                .Append(" submesh(es) = ").Append(merged.Materials.Count.ToString(CultureInfo.InvariantCulture))
                .Append(" material(s) [").Append(string.Join(", ", merged.Materials.ToArray())).Append("], ")
                .Append(merged.Positions.Length.ToString(CultureInfo.InvariantCulture)).Append(" verts / ")
                .Append(tris.ToString(CultureInfo.InvariantCulture)).Append(" tris");
            if (Math.Abs(normalise - 1f) > 1e-6f)
                said.Append("; NORMALISED from ").Append(longest.ToString("0.###", CultureInfo.InvariantCulture))
                    .Append(" units on its longest axis to 1.0 - the exporter's scale, not the game's");
            if (!allTangents) said.Append("; NO tangents (not every piece had them) - normal-map handedness is lost");
            if (anyUv && !allUv1) said.Append("; no UV1");
            note = said.ToString();
            return merged;
        }

        /// <summary>A model carries a skin when it has joint names or per-vertex weights.</summary>
        private static bool Skinned(SkinnedModel m)
        {
            return (m.JointNames != null && m.JointNames.Count > 0) ||
                   (m.Weights != null && m.Weights.Length > 0) ||
                   (m.Joints != null && m.Joints.Length > 0);
        }

        /// <summary>
        /// A part's triangles. A primitive read out of a glTF arrives as one submesh; a part that
        /// somehow carries several contributes all of them to the same group, which is right because
        /// they already shared its material slot.
        /// </summary>
        private static IEnumerable<int[]> Triangles(SkinnedModel part)
        {
            if (part.Submeshes.Count > 0) return part.Submeshes;
            return new List<int[]>();
        }

        /// <summary>
        /// Which material slot a part belongs to. The glTF material name is the truth when there is
        /// one; without it every unnamed part would collapse into a single slot, which is why the
        /// index is the fallback rather than a shared constant.
        /// </summary>
        private static string MaterialOf(SkinnedModel part, int index)
        {
            if (part.Materials.Count > 0 && !string.IsNullOrEmpty(part.Materials[0])) return part.Materials[0];
            if (!string.IsNullOrEmpty(part.Name)) return part.Name;
            return "material" + index.ToString(CultureInfo.InvariantCulture);
        }

        private static string Describe(IList<SkinnedModel> parts, int i)
        {
            string n = parts[i].Name;
            return "'" + (string.IsNullOrEmpty(n) ? "meshes[" + i.ToString(CultureInfo.InvariantCulture) + "]" : n) + "'";
        }
    }
}
