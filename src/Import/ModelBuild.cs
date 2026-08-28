using System;
using System.Globalization;
using System.IO;

namespace Morgott.ContentTool.Import
{
    /// <summary>
    /// A .glb the tool has understood: the same <see cref="BakedMesh"/> buffers an .obj produces, plus
    /// - when the file carries an armature - the bones, where they REST, their bind poses and the
    /// file's OWN per-vertex weights.
    ///
    /// That last part is the difference from every mesh path before it. The .obj route carries no skin
    /// at all, so <see cref="Morgott.ContentTool.Bake.SkinFields.Rebind"/> has to SYNTHESISE one
    /// full-weight influence per vertex from the target's bind poses (nearest bone), which creases at
    /// every joint. A .glb states JOINTS_0 and WEIGHTS_0 per vertex, so a vertex can be shared between
    /// bones and a joint can actually bend.
    ///
    /// ponytail: the two STRONGEST influences per vertex, renormalised. Not a judgement call - the
    /// serialized layout every shipped 2019.4.31f1 mesh carries has room for exactly two
    /// (<see cref="Morgott.ContentTool.Bake.SkinFields.Influences"/>, measured), so glTF's four have to
    /// be reduced by something. Widening the stream to four is a layout change in SkinFields, not here.
    /// </summary>
    internal sealed class BakedSkin
    {
        /// <summary>Influences kept per vertex - matches the serialized layout SkinFields writes.</summary>
        internal const int Influences = 2;

        internal string Name;
        internal BakedMesh Mesh;
        /// <summary>
        /// The file's own base colour, linear RGBA, or null when it declares no material. The bake
        /// writes it as the material's _Color; without it a model with no texture draws the Standard
        /// shader's default, which is opaque white and reads exactly like "the material never loaded".
        /// </summary>
        internal float[] BaseColor;
        /// <summary>
        /// One material name per submesh, index-parallel to <see cref="BakedMesh.SubmeshIndexCounts"/>
        /// and therefore to the renderer's m_Materials array - Unity binds submesh i to material i.
        /// Empty for a model whose file named no materials, which bakes as a single material exactly
        /// as it always did.
        /// </summary>
        internal string[] MaterialNames = new string[0];
        /// <summary>
        /// The .glb's own base-colour image bytes per material, index-parallel to
        /// <see cref="MaterialNames"/>, null where a material declares none. Still undecoded - the
        /// bake decodes them with Unity's Texture2D.LoadImage, the same call the author-supplied
        /// Content\Textures route uses.
        /// </summary>
        internal byte[][] MaterialImages = new byte[0][];
        /// <summary>Each material's emissive RGBA, index-parallel to <see cref="MaterialNames"/>,
        /// null where it does not glow. Written as _EmissionColor plus the _EMISSION keyword.</summary>
        internal float[][] MaterialEmissive = new float[0][];
        /// <summary>One name per bone, in the FILE's own joint order. Empty for a static model.</summary>
        internal string[] BoneNames = new string[0];
        /// <summary>Per bone, a column-major 4x4 (index = col*4 + row) - the file's inverse bind matrix.</summary>
        internal float[][] BindPoses;
        /// <summary>
        /// Per bone, the transform the bone rests at RELATIVE TO ITS PARENT - which is what a
        /// Transform serializes. For a bone that hangs off the model root that is the plain inverse of
        /// its bind pose; for a carried bone it is <see cref="ModelBuild.LocalRest"/>'s product, so the
        /// bone still lands at the same world rest once its parent has been applied.
        /// </summary>
        internal float[][] BoneRest;
        /// <summary>
        /// Per bone, the bone that CARRIES it, or -1 when it hangs off the model root - the file's own
        /// node tree, read by <c>GlbReader</c>. Without it every bone is a root, which is identical at
        /// rest and for any single-bone move and wrong the moment a bone has to take its children with
        /// it, so a rig that must animate needs this and a static pose does not.
        /// </summary>
        internal int[] BoneParents;
        /// <summary><see cref="Influences"/> weights per vertex, summing to 1.</summary>
        internal float[] Weights;
        /// <summary><see cref="Influences"/> bone indices per vertex, parallel to <see cref="Weights"/>.</summary>
        internal uint[] Bones;

        internal bool Rigged { get { return BoneNames.Length > 0; } }

        /// <summary>
        /// The weight this vertex puts on that bone - what the in-game arm PREDICTS a translation of
        /// that bone will do to it, so the oracle is the file's own number rather than a constant.
        /// </summary>
        internal float WeightOf(int vertex, int bone)
        {
            float w = 0f;
            for (int i = 0; i < Influences; i++)
                if (Bones[vertex * Influences + i] == (uint)bone) w += Weights[vertex * Influences + i];
            return w;
        }

        /// <summary>
        /// True when <paramref name="bone"/> is <paramref name="ancestor"/> or is carried by it,
        /// however deep. Moving a bone moves exactly this set.
        /// </summary>
        internal bool Carries(int ancestor, int bone)
        {
            for (int at = bone, steps = 0; at >= 0; at = BoneParents[at], steps++)
            {
                if (at == ancestor) return true;
                if (steps > BoneParents.Length) break;      // a cycle the reader let through
            }
            return false;
        }

        /// <summary>
        /// The weight this vertex puts on <paramref name="bone"/> AND on every bone that one carries -
        /// the fraction of a translation of that bone the vertex must follow. For a flat rig it is
        /// <see cref="WeightOf"/>; for a real tree it is the number a flat bake cannot produce.
        /// </summary>
        internal float CarriedWeight(int vertex, int bone)
        {
            float w = 0f;
            for (int b = 0; b < BoneNames.Length; b++)
                if (Carries(bone, b)) w += WeightOf(vertex, b);
            return w;
        }

        /// <summary>
        /// A bone's path relative to the MODEL ROOT - "hip/head" - which is what m_BoneNameHashes and
        /// m_RootBoneNameHash are CRC-32s of (SkinFields' class remark) and what an animation curve
        /// binds by (U6). A flat rig makes this the bone's own name; a real tree does not, so parenting
        /// a rig CHANGES its hashes, and that is the point rather than a side effect.
        /// </summary>
        internal string BonePath(int bone)
        {
            string path = BoneNames[bone];
            for (int at = BoneParents[bone], steps = 0; at >= 0; at = BoneParents[at], steps++)
            {
                path = BoneNames[at] + "/" + path;
                if (steps > BoneParents.Length) break;
            }
            return path;
        }

        internal string Describe()
        {
            string s = Mesh.Describe() + " bones=" + BoneNames.Length;
            for (int b = 0; b < BoneNames.Length && b < 4; b++) s += (b == 0 ? " " : ",") + "'" + BoneNames[b] + "'";
            return s;
        }
    }

    /// <summary>SkinnedModel -&gt; the buffers a serialized Mesh wants. No UnityEngine type, like
    /// <see cref="MeshBuild"/>, so the whole conversion is provable offline.</summary>
    internal static class ModelBuild
    {
        internal static BakedSkin From(SkinnedModel model, string name)
        {
            if (model == null) throw new ArgumentNullException(nameof(model));
            if (model.Positions == null || model.Positions.Length == 0)
                throw new InvalidDataException(name + ".glb carries no vertices; export a mesh with geometry");
            if (model.Normals == null || model.Normals.Length != model.Positions.Length)
                throw new InvalidDataException(name + ".glb carries no per-vertex normals; in Blender's " +
                    "glTF export panel leave Geometry > Normals on and re-export");

            int n = model.Positions.Length;
            BakedSkin skin = new BakedSkin { Name = name, BaseColor = model.BaseColor };
            BakedMesh baked = new BakedMesh { VertexCount = n };

            using (MemoryStream vs = new MemoryStream(n * BakedMesh.Stride))
            using (BinaryWriter vw = new BinaryWriter(vs))
            {
                float minX = float.MaxValue, minY = float.MaxValue, minZ = float.MaxValue;
                float maxX = float.MinValue, maxY = float.MinValue, maxZ = float.MinValue;
                for (int i = 0; i < n; i++)
                {
                    ObjVector3 p = model.Positions[i], nr = model.Normals[i];
                    ObjVector2 uv = model.Uv0 == null ? new ObjVector2(0f, 0f) : model.Uv0[i];
                    vw.Write(p.X); vw.Write(p.Y); vw.Write(p.Z);
                    vw.Write(nr.X); vw.Write(nr.Y); vw.Write(nr.Z);
                    vw.Write(uv.X); vw.Write(uv.Y);
                    if (p.X < minX) minX = p.X; if (p.X > maxX) maxX = p.X;
                    if (p.Y < minY) minY = p.Y; if (p.Y > maxY) maxY = p.Y;
                    if (p.Z < minZ) minZ = p.Z; if (p.Z > maxZ) maxZ = p.Z;
                }
                vw.Flush();
                baked.VertexData = vs.ToArray();
                baked.CenterX = (minX + maxX) * 0.5f; baked.ExtentX = (maxX - minX) * 0.5f;
                baked.CenterY = (minY + maxY) * 0.5f; baked.ExtentY = (maxY - minY) * 0.5f;
                baked.CenterZ = (minZ + maxZ) * 0.5f; baked.ExtentZ = (maxZ - minZ) * 0.5f;
            }

            // The submeshes are kept, not flattened. They were flattened while the renderer this bake
            // writes carried exactly one material; it carries one per submesh now, so the ranges are
            // what binds surface to material. The index buffer below already writes them one after
            // another, so a range is just a running offset - recording the lengths is the whole job.
            int indexCount = 0;
            foreach (int[] s in model.Submeshes) indexCount += s.Length;
            int[] submeshCounts = new int[model.Submeshes.Count];
            for (int s = 0; s < model.Submeshes.Count; s++) submeshCounts[s] = model.Submeshes[s].Length;
            baked.SubmeshIndexCounts = submeshCounts;
            skin.MaterialNames = model.Materials.ToArray();
            skin.MaterialImages = model.MaterialImages.ToArray();
            skin.MaterialEmissive = model.MaterialEmissive.ToArray();
            if (indexCount == 0)
                throw new InvalidDataException(name + ".glb carries no triangles; export a mesh with faces");
            baked.IndexCount = indexCount;
            baked.Index32 = n > ushort.MaxValue;
            using (MemoryStream ist = new MemoryStream(indexCount * (baked.Index32 ? 4 : 2)))
            using (BinaryWriter iw = new BinaryWriter(ist))
            {
                foreach (int[] s in model.Submeshes)
                    foreach (int i in s)
                    {
                        if (baked.Index32) iw.Write((uint)i); else iw.Write((ushort)i);
                    }
                iw.Flush();
                baked.IndexData = ist.ToArray();
            }
            skin.Mesh = baked;

            if (model.JointNames.Count == 0) return skin;   // the static path - a prop, not a character

            if (model.Joints == null || model.Weights == null ||
                model.Joints.Length != n * 4 || model.Weights.Length != n * 4)
                throw new InvalidDataException(name + ".glb has an armature but its bone weights do not " +
                    "cover every vertex; in Blender give the whole mesh an Armature modifier with vertex " +
                    "groups and re-export");
            if (model.InverseBindMatrices == null || model.InverseBindMatrices.Length != model.JointNames.Count)
                throw new InvalidDataException(name + ".glb has " +
                    (model.InverseBindMatrices == null ? 0 : model.InverseBindMatrices.Length) +
                    " bind poses for " + model.JointNames.Count + " bones; re-export it from Blender");

            skin.BoneNames = model.JointNames.ToArray();
            skin.BindPoses = model.InverseBindMatrices;
            int bones = skin.BoneNames.Length;

            // The file's node tree, when it carries one. A model built somewhere other than the reader
            // (the extractor's own SkinnedModel) leaves it empty, and an empty tree IS the flat rig
            // every bake wrote before: every bone a root, every local rest the plain inverse bind pose.
            bool tree = model.Nodes.Count == bones;
            skin.BoneParents = new int[bones];
            skin.BoneRest = new float[bones][];
            for (int b = 0; b < bones; b++)
            {
                skin.BoneParents[b] = tree ? model.Nodes[b].Parent : -1;
                if (skin.BoneParents[b] >= bones)
                    throw new InvalidDataException(name + ".glb parents bone '" + skin.BoneNames[b] +
                        "' to bone " + skin.BoneParents[b] + " of " + bones + "; re-export the file");
                skin.BoneRest[b] = tree && model.Nodes[b].Local != null
                    ? model.Nodes[b].Local
                    : LocalRest(skin.BindPoses[b], null, skin.BoneNames[b]);
            }

            skin.Weights = new float[n * BakedSkin.Influences];
            skin.Bones = new uint[n * BakedSkin.Influences];
            for (int v = 0; v < n; v++) Strongest(model, v, skin);
            return skin;
        }

        /// <summary>
        /// The two heaviest of glTF's four influences for one vertex, renormalised so they sum to 1.
        /// A vertex the file leaves unweighted goes whole to bone 0 rather than vanishing to the origin,
        /// which is what a zero weight sum makes the engine do.
        /// </summary>
        private static void Strongest(SkinnedModel model, int v, BakedSkin skin)
        {
            int firstAt = -1, secondAt = -1;
            for (int i = 0; i < 4; i++)
            {
                float w = model.Weights[v * 4 + i];
                if (w <= 0f) continue;
                if (firstAt < 0 || w > model.Weights[v * 4 + firstAt]) { secondAt = firstAt; firstAt = i; }
                else if (secondAt < 0 || w > model.Weights[v * 4 + secondAt]) secondAt = i;
            }

            int at = v * BakedSkin.Influences;
            if (firstAt < 0)
            {
                skin.Weights[at] = 1f; skin.Weights[at + 1] = 0f;
                skin.Bones[at] = 0; skin.Bones[at + 1] = 0;
                return;
            }
            float w0 = model.Weights[v * 4 + firstAt];
            float w1 = secondAt < 0 ? 0f : model.Weights[v * 4 + secondAt];
            float sum = w0 + w1;
            skin.Weights[at] = w0 / sum;
            skin.Weights[at + 1] = w1 / sum;
            skin.Bones[at] = model.Joints[v * 4 + firstAt];
            skin.Bones[at + 1] = secondAt < 0 ? model.Joints[v * 4 + firstAt] : model.Joints[v * 4 + secondAt];
        }

        /// <summary>
        /// Where a bone RESTS relative to its parent, out of the bind poses and nothing else: the
        /// bone's world rest is inverse(bindPose), so its local one is bindPose(parent) * that. With no
        /// parent the two are the same matrix. One formula, one place - the reader fills the model's
        /// nodes with it and the bake reads them back, so the rig cannot be described two ways.
        /// </summary>
        internal static float[] LocalRest(float[] bind, float[] parentBind, string bone)
        {
            float[] world = Invert(bind, bone);
            return parentBind == null ? world : Multiply(parentBind, world);
        }

        /// <summary>a * b, both column-major 4x4 (index = col*4 + row).</summary>
        internal static float[] Multiply(float[] a, float[] b)
        {
            float[] r = new float[16];
            for (int col = 0; col < 4; col++)
                for (int row = 0; row < 4; row++)
                {
                    float sum = 0f;
                    for (int k = 0; k < 4; k++) sum += a[k * 4 + row] * b[col * 4 + k];
                    r[col * 4 + row] = sum;
                }
            return r;
        }

        /// <summary>
        /// The inverse of an affine column-major 4x4 (index = col*4 + row). A bone's bind pose IS the
        /// mesh-space -&gt; bone-space transform, so its inverse is where the bone has to REST for the
        /// skin to come out undeformed: Unity skins with boneWorld * bindPose, and that product must be
        /// the identity at rest. Placing the bones anywhere else deforms the model before it moves.
        /// </summary>
        internal static float[] Invert(float[] m, string bone)
        {
            if (m == null || m.Length != 16)
                throw new InvalidDataException("bone '" + bone + "' has no 4x4 bind pose; re-export the file");
            double a = m[0], b = m[4], c = m[8];
            double d = m[1], e = m[5], f = m[9];
            double g = m[2], h = m[6], i = m[10];
            double det = a * (e * i - f * h) - b * (d * i - f * g) + c * (d * h - e * g);
            if (Math.Abs(det) < 1e-12)
                throw new InvalidDataException("bone '" + bone + "' has a bind pose that cannot be inverted " +
                    "(its scale collapses to zero); fix the bone's scale in Blender and re-export");

            double[] inv =
            {
                (e * i - f * h) / det, (c * h - b * i) / det, (b * f - c * e) / det,
                (f * g - d * i) / det, (a * i - c * g) / det, (c * d - a * f) / det,
                (d * h - e * g) / det, (b * g - a * h) / det, (a * e - b * d) / det
            };
            // inv is row-major 3x3; the translation of the inverse is -inv * t.
            double tx = m[12], ty = m[13], tz = m[14];
            float[] r = new float[16];
            for (int col = 0; col < 3; col++)
                for (int row = 0; row < 3; row++) r[col * 4 + row] = (float)inv[row * 3 + col];
            r[12] = (float)-(inv[0] * tx + inv[1] * ty + inv[2] * tz);
            r[13] = (float)-(inv[3] * tx + inv[4] * ty + inv[5] * tz);
            r[14] = (float)-(inv[6] * tx + inv[7] * ty + inv[8] * tz);
            r[15] = 1f;
            return r;
        }

        // ---------------------------------------------------------------- the sample rig

        /// <summary>Bone names of <see cref="SampleGlb"/>, in the order the file lists its joints.</summary>
        internal static readonly string[] SampleBones = { "sample_hip", "sample_head", "sample_arm" };

        /// <summary>Parent slot per bone of <see cref="SampleGlb"/> - head and arm are SIBLINGS.</summary>
        internal static readonly int[] SampleParents = { -1, 0, 0 };

        /// <summary>
        /// A .glb written by the tool's own <see cref="GlbCodec"/>, so the sample project has a model
        /// to bake with no art tool in the loop - and so the OFFLINE round trip and the in-game gate
        /// read the same four vertices.
        ///
        /// The weighting is deliberately ANTI-GEOMETRIC: the two vertices at y=0 belong to the bone
        /// that sits HIGH, and one of them is split half and half between two bones. Neither
        /// arrangement is anything a nearest-bone or split-at-the-centre synthesis can produce, so a
        /// deformation that matches it can only have come from the file's own WEIGHTS_0.
        ///
        /// The TREE is anti-chain for the same reason. head and arm are SIBLINGS under hip, so a bake
        /// that parented the bones in index order (the obvious synthesis) would carry the arm with the
        /// head and move vertices 4 and 5 that the file says must stay put. And no bone rests where its
        /// parent does - hip at y=1, head at 4, arm at 2 - so a bake that wrote a bone's WORLD rest into
        /// its local transform stacks its parent's lift on top of it and the model is deformed before
        /// anything moves at all.
        /// </summary>
        internal static byte[] SampleGlb()
        {
            SkinnedModel m = new SkinnedModel { Name = "sample" };
            m.Positions = new[]
            {
                new ObjVector3(0f, 0f, 0f), new ObjVector3(1f, 0f, 0f),
                new ObjVector3(1f, 2f, 0f), new ObjVector3(0f, 2f, 0f),
                new ObjVector3(0f, 3f, 0f), new ObjVector3(1f, 3f, 0f)
            };
            m.Normals = new ObjVector3[6];
            m.Uv0 = new ObjVector2[6];
            for (int i = 0; i < 6; i++)
            {
                m.Normals[i] = new ObjVector3(0f, 0f, -1f);
                m.Uv0[i] = new ObjVector2(i % 2, i / 2 * 0.5f);
            }
            m.Submeshes.Add(new[] { 0, 1, 2, 0, 2, 3, 3, 4, 5 });
            m.Materials.Add("sample");

            // vertex 0 -> head alone, 1 -> hip/head half each, 2 and 3 -> hip alone, 4 -> arm alone,
            // 5 -> arm/head half each. That last one is what separates the real tree from a chain:
            // lifting the head must move HALF of vertex 5 and none of vertex 4.
            m.Joints = new ushort[]
            {
                1, 0, 0, 0,   0, 1, 0, 0,   0, 0, 0, 0,
                0, 0, 0, 0,   2, 0, 0, 0,   2, 1, 0, 0
            };
            m.Weights = new[]
            {
                1f, 0f, 0f, 0f,   0.5f, 0.5f, 0f, 0f,   1f, 0f, 0f, 0f,
                1f, 0f, 0f, 0f,   1f,   0f,   0f, 0f,   0.5f, 0.5f, 0f, 0f
            };
            // Rest poses are stated LOCALLY, as nodes, and the bind poses are the inverses of the
            // WORLD transforms those produce (hip 1, head 1+3, arm 1+1) - which is the relationship
            // GlbReader.RestLocals inverts on the way back in.
            m.Nodes.Add(new SkinNode { Name = SampleBones[0], Parent = SampleParents[0], Local = Identity(1f) });
            m.Nodes.Add(new SkinNode { Name = SampleBones[1], Parent = SampleParents[1], Local = Identity(3f) });
            m.Nodes.Add(new SkinNode { Name = SampleBones[2], Parent = SampleParents[2], Local = Identity(1f) });
            m.JointNodes = new[] { 0, 1, 2 };
            m.InverseBindMatrices = new[] { Identity(-1f), Identity(-4f), Identity(-2f) };
            return GlbCodec.Write(m);
        }

        /// <summary>A column-major 4x4 that is the identity plus a Y translation.</summary>
        private static float[] Identity(float y)
        {
            float[] m = new float[16];
            m[0] = m[5] = m[10] = m[15] = 1f;
            m[13] = y;
            return m;
        }

        internal static string F(float v)
        {
            // InvariantCulture: these lines are machine-compared and a ru-RU machine writes 0,5 for 0.5.
            return v.ToString("0.###", CultureInfo.InvariantCulture);
        }
    }
}
