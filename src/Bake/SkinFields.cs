using System;
using System.Collections.Generic;
using System.Globalization;
using AssetsTools.NET;
using AssetsTools.NET.Extra;
using Morgott.ContentTool.Import;

namespace Morgott.ContentTool.Bake
{
    /// <summary>
    /// U5 - the skinned half of a bake: a Mesh serialized from the EMPTY template (which U4 could not
    /// do - it borrowed a Mesh the cloned bundle already shipped), carrying bind poses, bone name
    /// hashes and per-vertex bone weights, plus the GameObject/Transform bones and the
    /// SkinnedMeshRenderer that wires them. Free of UnityEngine types like
    /// <see cref="MeshFields"/>/<see cref="PrefabFields"/>, so build -&gt; repack -&gt; read is provable
    /// offline (tests\ObjCodecTests\SkinRoundTrip.cs).
    ///
    /// EVERYTHING here is MEASURED 2026-08-12 off shipped 2019.4.31f1 bundles
    /// (`mutoid_assets_all.bundle` Mesh 'Geo_Head02_V01' + its SkinnedMeshRenderer,
    /// `aln_fireworm_assets_all.bundle` 'ALN_Fireworm', `aln_poisonworm_assets_all.bundle`), never
    /// remembered:
    ///  - skin data lives in its OWN vertex stream: shipped is stream0 pos/normal/tangent,
    ///    stream1 uv0, stream2 channel 12 (BlendWeight, float32 x2) at offset 0 and channel 13
    ///    (BlendIndices, format 10 = UInt32, x2) at offset 8. Each stream STARTS 16-byte aligned -
    ///    ALN_Siren_Arm_Slasher_Right's 1783 verts give 71320+8 | 14264+8 | 28528 = 114128 B, which
    ///    is exactly its m_DataSize. Two influences per vertex THERE - the count is
    ///    PER MESH, declared by the channel dimension, and the Phoenix Assault body parts ship four
    ///    (<see cref="InfluencesOf"/>).
    ///  - the SkinnedMeshRenderer's own defaults differ from the MeshRenderer's:
    ///    m_RayTracingMode is 0 here and 2 there, and m_SkinnedMotionVectors is true.
    ///  - m_BoneNameHashes is CRC-32 (reflected 0xEDB88320, final xor) of the bone's transform PATH
    ///    relative to the model root, NOT of the bone name: aln_fireworm's 'Fireworm_head' hashes
    ///    638923553, which is crc32("Fireworm_root/Fireworm_head") and not crc32("Fireworm_head")
    ///    (1476905596). m_RootBoneNameHash is the root bone's own entry.
    ///
    /// ponytail: two bones, two influences, weight 1 on one of them - the smallest rig that can
    /// still show a skin BINDING (half the vertices follow a bone, half do not). Real weights come
    /// from an imported skinned format; an .obj has none to carry.
    /// </summary>
    internal static class SkinFields
    {
        private const int ChannelBlendWeight = 12, ChannelBlendIndices = 13;
        private const int FormatFloat32 = 0, FormatUInt32 = 10;

        /// <summary>glTF states exactly four influences per vertex, so nothing here can carry more.</summary>
        internal const int MaxInfluences = 4;

        /// <summary>
        /// The two-bone FIXTURE's width (<see cref="Build"/>). Two is the shape of the fixture itself -
        /// weight 1 on one of two bones, see the class remark - and not a limit imposed on data: every
        /// path that has a file or a target to read takes its width from THAT
        /// (<see cref="InfluencesOf"/>, <c>BakedSkin.Influences</c>).
        /// </summary>
        internal const int FixtureInfluences = 2;

        /// <summary>
        /// Influences per vertex a serialized Mesh DECLARES - the wider of its two skin channel
        /// dimensions, capped at what a file can supply. 0 when the mesh has no skin channel slots.
        ///
        /// The count is PER MESH, not a property of the engine: mutoid and aln_fireworm ship dim2,
        /// while every Phoenix Assault body part ships dim4 (measured, `CHR_PX_ASS_TS_M_V01_02`
        /// weightCh=stream2/off0/fmt0/dim4 indexCh=stream2/off16/fmt10/dim4). So this is what a
        /// REPLACEMENT has to keep - writing 2 over a dim4 target downgrades its skinning.
        ///
        /// The wider of the two and not the narrower because a rigid attachment ships an index
        /// channel and NO weight one (`CHR_PX_ASS_HG_M_V01_02`, dim0/dim1): giving it a weight
        /// channel is an upgrade, and taking its one influence away is not.
        /// </summary>
        internal static int InfluencesOf(AssetTypeValueField mesh)
        {
            AssetTypeValueField chs = mesh["m_VertexData"]["m_Channels"]["Array"];
            if (chs.Children.Count <= ChannelBlendIndices) return 0;
            int w = chs.Children[ChannelBlendWeight]["dimension"].AsByte;
            int b = chs.Children[ChannelBlendIndices]["dimension"].AsByte;
            return Math.Min(Math.Max(w, b), MaxInfluences);
        }

        /// <summary>Bytes one vertex of the skin stream takes: that many float weights, then that
        /// many UInt32 bone indices.</summary>
        internal static int SkinStride(int influences) { return influences * 4 + influences * 4; }

        /// <summary>Every vertex stream starts on this boundary - measured, see the class remark.</summary>
        private const int StreamAlignment = 16;

        internal struct Ids
        {
            internal long RootGameObject, RootTransform;
            internal long SkinGameObject, SkinTransform, Renderer, Mesh;
            internal long Bone0GameObject, Bone0Transform, Bone1GameObject, Bone1Transform;
        }

        /// <summary>
        /// Root GameObject+Transform; two chained bones (bone1 a child of bone0, <paramref name="boneY"/>
        /// above it); and a skin GameObject carrying Transform+SkinnedMeshRenderer that points at a
        /// Mesh created HERE from the empty template.
        /// </summary>
        /// <param name="splitWeights">
        /// true binds every vertex at or above the mesh's centre to bone1 and the rest to bone0, so
        /// moving bone1 must move half the mesh. false binds EVERY vertex to bone0 - the control:
        /// moving bone1 must then move nothing.
        /// </param>
        /// <param name="rebind">
        /// true throws the synthesised weights away again and runs <see cref="Rebind"/> - the SAME
        /// call the shipped-mesh replacement path makes - over the bind poses this bake just wrote.
        /// With <paramref name="splitWeights"/> false the split can then only come from Rebind, which
        /// is what makes the deformation arm measure the replacement path and not this builder.
        /// </param>
        internal static Ids Build(AssetsFile afile, ClassDatabaseFile cldb, Func<long> nextPathId,
                                  string rootName, BakedMesh baked, long materialPathId,
                                  float boneY, bool splitWeights, bool rebind)
        {
            if (afile == null) throw new ArgumentNullException(nameof(afile));
            if (baked == null) throw new ArgumentNullException(nameof(baked));
            if (string.IsNullOrEmpty(rootName)) throw new ArgumentException("empty root name", nameof(rootName));

            Ids ids = new Ids
            {
                RootGameObject = nextPathId(),
                RootTransform = nextPathId(),
                Bone0GameObject = nextPathId(),
                Bone0Transform = nextPathId(),
                Bone1GameObject = nextPathId(),
                Bone1Transform = nextPathId(),
                SkinGameObject = nextPathId(),
                SkinTransform = nextPathId(),
                Renderer = nextPathId(),
                Mesh = nextPathId()
            };

            string bone0 = Bone0Name(rootName), bone1 = Bone1Name(rootName);

            PrefabFields.Create(afile, cldb, ids.RootGameObject, AssetClassID.GameObject, go =>
            {
                PrefabFields.FillGameObject(go, rootName);
                PrefabFields.AddComponent(go, ids.RootTransform);
            });
            PrefabFields.Create(afile, cldb, ids.RootTransform, AssetClassID.Transform, tf =>
            {
                PrefabFields.FillTransform(tf, ids.RootGameObject, 0f, 0f, 0f);
                // Both children on the ROOT: U4 measured that the engine builds the parent link from
                // this array alone, so a bone missing here is a bone the engine never parents.
                AddChild(tf, ids.Bone0Transform);
                AddChild(tf, ids.SkinTransform);
            });

            PrefabFields.Create(afile, cldb, ids.Bone0GameObject, AssetClassID.GameObject, go =>
            {
                PrefabFields.FillGameObject(go, bone0);
                PrefabFields.AddComponent(go, ids.Bone0Transform);
            });
            PrefabFields.Create(afile, cldb, ids.Bone0Transform, AssetClassID.Transform, tf =>
            {
                PrefabFields.FillTransform(tf, ids.Bone0GameObject, 0f, 0f, 0f);
                PrefabFields.Pptr(tf["m_Father"], ids.RootTransform);
                AddChild(tf, ids.Bone1Transform);
            });

            PrefabFields.Create(afile, cldb, ids.Bone1GameObject, AssetClassID.GameObject, go =>
            {
                PrefabFields.FillGameObject(go, bone1);
                PrefabFields.AddComponent(go, ids.Bone1Transform);
            });
            PrefabFields.Create(afile, cldb, ids.Bone1Transform, AssetClassID.Transform, tf =>
            {
                PrefabFields.FillTransform(tf, ids.Bone1GameObject, 0f, boneY, 0f);
                PrefabFields.Pptr(tf["m_Father"], ids.Bone0Transform);
            });

            PrefabFields.Create(afile, cldb, ids.SkinGameObject, AssetClassID.GameObject, go =>
            {
                PrefabFields.FillGameObject(go, SkinName(rootName));
                PrefabFields.AddComponent(go, ids.SkinTransform);
                PrefabFields.AddComponent(go, ids.Renderer);
            });
            PrefabFields.Create(afile, cldb, ids.SkinTransform, AssetClassID.Transform, tf =>
            {
                PrefabFields.FillTransform(tf, ids.SkinGameObject, 0f, 0f, 0f);
                PrefabFields.Pptr(tf["m_Father"], ids.RootTransform);
            });

            byte[] weights;
            int[] boneOfVertex;
            SkinBuffer(baked, boneY, splitWeights, out weights, out boneOfVertex);

            PrefabFields.Create(afile, cldb, ids.Mesh, AssetClassID.Mesh, mesh =>
            {
                FillMesh(mesh, MeshName(rootName), baked, weights, boneOfVertex, boneY, bone0, bone1);
                // FillMesh has just declared the fixture's own width, so the re-bind reads it back
                // the same way a replacement reads its target's.
                if (rebind) Rebind(mesh, baked, InfluencesOf(mesh));
            });

            PrefabFields.Create(afile, cldb, ids.Renderer, AssetClassID.SkinnedMeshRenderer, r =>
            {
                FillRenderer(r, ids.SkinGameObject, ids.Mesh, new[] { materialPathId }, baked);
                AssetTypeValueField bones = r["m_Bones"]["Array"];
                foreach (long b in new[] { ids.Bone0Transform, ids.Bone1Transform })
                {
                    AssetTypeValueField p = ValueBuilder.DefaultValueFieldFromArrayTemplate(bones);
                    p["m_FileID"].AsInt = 0;
                    p["m_PathID"].AsLong = b;
                    bones.Children.Add(p);
                }
                PrefabFields.Pptr(r["m_RootBone"], ids.Bone0Transform);
            });

            return ids;
        }

        /// <summary>
        /// Every SkinnedMeshRenderer field except the bone list, so U5's two-bone bake and the
        /// author's imported rig cannot describe the same renderer differently. The odd constants are
        /// measured (see the class remark): m_RayTracingMode is 0 here where a MeshRenderer's is 2,
        /// and m_SkinnedMotionVectors is true.
        /// </summary>
        /// <param name="rootBindPose">the ROOT bone's bind pose, column-major (index = col*4 + row) -
        /// what m_AABB has to be expressed through, see <see cref="RendererAabb"/>. null means the root
        /// bone rests at the identity, which is true of the two-bone bake and of nothing else.</param>
        /// <param name="materialPathIds">one per SUBMESH, in submesh order - Unity draws submesh i with
        /// m_Materials[i] and draws NOTHING at all for a submesh past the end of that array. A model
        /// that arrived as several meshes over several materials (a body, its clothes, its hair) is
        /// therefore invisible from the second surface on if this carries a single entry.</param>
        private static void FillRenderer(AssetTypeValueField r, long skinGameObject, long meshPathId,
                                         long[] materialPathIds, BakedMesh baked, float[] rootBindPose = null)
        {
            PrefabFields.Pptr(r["m_GameObject"], skinGameObject);
            r["m_Enabled"].AsBool = true;
            r["m_CastShadows"].AsByte = 1;
            r["m_ReceiveShadows"].AsByte = 1;
            r["m_DynamicOccludee"].AsByte = 1;
            r["m_MotionVectors"].AsByte = 1;
            r["m_LightProbeUsage"].AsByte = 1;
            r["m_ReflectionProbeUsage"].AsByte = 1;
            r["m_RayTracingMode"].AsByte = 0;
            r["m_RenderingLayerMask"].AsUInt = 1;
            r["m_LightmapIndex"].AsUShort = 65535;
            r["m_LightmapIndexDynamic"].AsUShort = 65535;
            PrefabFields.Vector4(r["m_LightmapTilingOffset"]);
            PrefabFields.Vector4(r["m_LightmapTilingOffsetDynamic"]);
            r["m_SkinnedMotionVectors"].AsBool = true;

            AssetTypeValueField mats = r["m_Materials"]["Array"];
            if (materialPathIds == null || materialPathIds.Length == 0) materialPathIds = new long[] { 0 };
            foreach (long id in materialPathIds)
            {
                AssetTypeValueField mm = ValueBuilder.DefaultValueFieldFromArrayTemplate(mats);
                mm["m_FileID"].AsInt = 0;
                mm["m_PathID"].AsLong = id;
                mats.Children.Add(mm);
            }

            PrefabFields.Pptr(r["m_Mesh"], meshPathId);

            // The renderer's own bounds. Left at zero the renderer is culled everywhere its origin is
            // off screen, which reads exactly like "the skin did not load".
            float[] aabb = RendererAabb(baked, rootBindPose);
            PrefabFields.Vector3(r["m_AABB"]["m_Center"], aabb[0], aabb[1], aabb[2]);
            PrefabFields.Vector3(r["m_AABB"]["m_Extent"], aabb[3], aabb[4], aabb[5]);
            r["m_DirtyAABB"].AsBool = false;
        }

        /// <summary>
        /// m_AABB, AS UNITY READS IT: a SkinnedMeshRenderer's serialized bounds live in the ROOT
        /// BONE's space, not the mesh's. The engine draws the box by pushing it through the root
        /// bone's world matrix - the bind pose is NOT applied, because skinning already cancelled it
        /// on the vertices and never touches this field.
        ///
        /// Writing the mesh-space box there is therefore only correct while the root bone rests at
        /// the identity. The spider's `Root` bone carries the .glb exporter's unit conversion (a
        /// uniform 1/3368.8 in its bind pose), so the mesh box came back out 3368.8 x 1105.9 x 2990.5
        /// world units on a creature one tile across - measured in a live mission, 2026-08-24. Unity
        /// culls off that number: the creature pops in and out depending on where the camera looks.
        ///
        /// So the box is expressed in the root bone's space first, by its BIND POSE (model space ->
        /// bone space), corner by corner because the pose can rotate as well as scale.
        /// </summary>
        /// <returns>centre xyz then HALF-extent xyz, the two vectors m_AABB serializes.</returns>
        private static float[] RendererAabb(BakedMesh b, float[] rootBindPose)
        {
            if (rootBindPose == null)
                return new[] { b.CenterX, b.CenterY, b.CenterZ, b.ExtentX, b.ExtentY, b.ExtentZ };

            float[] m = rootBindPose;
            float minX = float.MaxValue, minY = float.MaxValue, minZ = float.MaxValue;
            float maxX = float.MinValue, maxY = float.MinValue, maxZ = float.MinValue;
            for (int c = 0; c < 8; c++)
            {
                float x = b.CenterX + ((c & 1) == 0 ? -b.ExtentX : b.ExtentX);
                float y = b.CenterY + ((c & 2) == 0 ? -b.ExtentY : b.ExtentY);
                float z = b.CenterZ + ((c & 4) == 0 ? -b.ExtentZ : b.ExtentZ);
                float tx = m[0] * x + m[4] * y + m[8] * z + m[12];
                float ty = m[1] * x + m[5] * y + m[9] * z + m[13];
                float tz = m[2] * x + m[6] * y + m[10] * z + m[14];
                if (tx < minX) minX = tx; if (tx > maxX) maxX = tx;
                if (ty < minY) minY = ty; if (ty > maxY) maxY = ty;
                if (tz < minZ) minZ = tz; if (tz > maxZ) maxZ = tz;
            }
            return new[] { (minX + maxX) * 0.5f, (minY + maxY) * 0.5f, (minZ + maxZ) * 0.5f,
                           (maxX - minX) * 0.5f, (maxY - minY) * 0.5f, (maxZ - minZ) * 0.5f };
        }

        /// <summary>
        /// The AUTHOR's case: an arbitrary rig out of a .glb, rather than U5's two synthesised bones.
        /// Same ten-object shape and the same field layout - what changes is that every number comes
        /// out of the file: one bone per joint the file lists, each resting where its own bind pose
        /// says (<see cref="ModelBuild.Invert"/>), and the file's real per-vertex weights, so a vertex
        /// can be shared between bones. Returns the ROOT GameObject's pathId; only that one gets an
        /// m_Container entry, which is how a shipped prefab addresses.
        ///
        /// The bones carry each other: <c>BakedSkin.BoneParents</c> is the file's own node tree, so a
        /// bone is a child of the bone the .glb parents it to and only a bone the file leaves parentless
        /// hangs off the model root. That is what makes a rig ANIMATE rather than merely pose - moving a
        /// hip has to take the head with it - and it is why a bone's rest transform is written LOCAL to
        /// its parent (<c>BakedSkin.BoneRest</c>) instead of in model space: writing world there stacks
        /// the parent's transform on top of it and deforms the model before anything moves.
        /// </summary>
        /// <param name="controllerPathId">non-zero puts an <c>Animator</c> carrying that controller on
        /// the model ROOT (U7's shipping shape): the clip's binding paths are relative to the Animator's
        /// own GameObject, and the bone paths written here are exactly those. 0 bakes no Animator, which
        /// is what a model with nothing to play it wants - a dangling controller PPtr is worse than none.</param>
        internal static long BuildModel(AssetsFile afile, ClassDatabaseFile cldb, Func<long> nextPathId,
                                        string rootName, BakedSkin skin, long[] materialPathIds,
                                        long controllerPathId = 0)
        {
            if (afile == null) throw new ArgumentNullException(nameof(afile));
            if (skin == null) throw new ArgumentNullException(nameof(skin));
            if (!skin.Rigged) throw new ArgumentException("model '" + rootName + "' carries no armature", nameof(skin));
            if (skin.Influences < 1 || skin.Influences > MaxInfluences ||
                skin.Weights.Length != skin.Mesh.VertexCount * skin.Influences)
                throw new ArgumentException("model '" + rootName + "' has " + skin.Weights.Length +
                    " weights for " + skin.Mesh.VertexCount + " vertices at " + skin.Influences +
                    " influences", nameof(skin));

            int bones = skin.BoneNames.Length;
            if (skin.BoneParents == null || skin.BoneParents.Length != bones)
                throw new ArgumentException("model '" + rootName + "' has no parent link per bone", nameof(skin));

            long rootGo = nextPathId(), rootTf = nextPathId();
            long[] boneGo = new long[bones], boneTf = new long[bones];
            for (int b = 0; b < bones; b++) { boneGo[b] = nextPathId(); boneTf[b] = nextPathId(); }
            long skinGo = nextPathId(), skinTf = nextPathId(), renderer = nextPathId(), meshId = nextPathId();
            long animator = controllerPathId == 0 ? 0 : nextPathId();

            PrefabFields.Create(afile, cldb, rootGo, AssetClassID.GameObject, go =>
            {
                PrefabFields.FillGameObject(go, rootName);
                PrefabFields.AddComponent(go, rootTf);
                if (animator != 0) PrefabFields.AddComponent(go, animator);
            });
            if (animator != 0)
                PrefabFields.Create(afile, cldb, animator, AssetClassID.Animator,
                                    a => ClipFields.FillAnimator(a, rootGo, controllerPathId));
            PrefabFields.Create(afile, cldb, rootTf, AssetClassID.Transform, tf =>
            {
                PrefabFields.FillTransform(tf, rootGo, 0f, 0f, 0f);
                // U4 measured that the engine parents from the PARENT's m_Children alone, so a bone
                // missing from its parent's array is a bone the engine never parents. Only the bones
                // the file leaves parentless belong here; the rest hang off each other.
                for (int b = 0; b < bones; b++) if (skin.BoneParents[b] < 0) AddChild(tf, boneTf[b]);
                AddChild(tf, skinTf);
            });

            for (int b = 0; b < bones; b++)
            {
                int bone = b;
                PrefabFields.Create(afile, cldb, boneGo[bone], AssetClassID.GameObject, go =>
                {
                    PrefabFields.FillGameObject(go, skin.BoneNames[bone]);
                    PrefabFields.AddComponent(go, boneTf[bone]);
                });
                PrefabFields.Create(afile, cldb, boneTf[bone], AssetClassID.Transform, tf =>
                {
                    PrefabFields.FillTransform(tf, boneGo[bone], 0f, 0f, 0f);
                    Rest(tf, skin.BoneRest[bone], skin.BoneNames[bone]);
                    PrefabFields.Pptr(tf["m_Father"],
                                      skin.BoneParents[bone] < 0 ? rootTf : boneTf[skin.BoneParents[bone]]);
                    // Both halves of the link, because only one of them is what the engine reads.
                    for (int c = 0; c < bones; c++) if (skin.BoneParents[c] == bone) AddChild(tf, boneTf[c]);
                });
            }

            PrefabFields.Create(afile, cldb, skinGo, AssetClassID.GameObject, go =>
            {
                PrefabFields.FillGameObject(go, SkinName(rootName));
                PrefabFields.AddComponent(go, skinTf);
                PrefabFields.AddComponent(go, renderer);
            });
            PrefabFields.Create(afile, cldb, skinTf, AssetClassID.Transform, tf =>
            {
                PrefabFields.FillTransform(tf, skinGo, 0f, 0f, 0f);
                PrefabFields.Pptr(tf["m_Father"], rootTf);
            });

            PrefabFields.Create(afile, cldb, meshId, AssetClassID.Mesh, mesh => FillModelMesh(mesh, rootName, skin));
            PrefabFields.Create(afile, cldb, renderer, AssetClassID.SkinnedMeshRenderer, r =>
            {
                // m_RootBone below is bone 0, so bone 0's bind pose is the space m_AABB belongs in.
                FillRenderer(r, skinGo, meshId, materialPathIds, skin.Mesh, skin.BindPoses[0]);
                AssetTypeValueField list = r["m_Bones"]["Array"];
                for (int b = 0; b < bones; b++)
                {
                    AssetTypeValueField p = ValueBuilder.DefaultValueFieldFromArrayTemplate(list);
                    p["m_FileID"].AsInt = 0;
                    p["m_PathID"].AsLong = boneTf[b];
                    list.Children.Add(p);
                }
                PrefabFields.Pptr(r["m_RootBone"], boneTf[0]);
            });

            return rootGo;
        }

        /// <summary>A bone's rest transform, out of its inverted bind pose and into the three fields a
        /// Transform serializes. <see cref="GlbCodec.Decompose"/> is the exporter's own splitter,
        /// so the rig comes back apart exactly the way it was put together.</summary>
        private static void Rest(AssetTypeValueField tf, float[] rest, string boneName)
        {
            float[] t, rot, s;
            GlbCodec.Decompose(rest, boneName, out t, out rot, out s);
            PrefabFields.Vector3(tf["m_LocalPosition"], t[0], t[1], t[2]);
            PrefabFields.Vector3(tf["m_LocalScale"], s[0], s[1], s[2]);
            AssetTypeValueField q = tf["m_LocalRotation"];
            q["x"].AsFloat = rot[0]; q["y"].AsFloat = rot[1]; q["z"].AsFloat = rot[2]; q["w"].AsFloat = rot[3];
        }

        /// <summary>The Mesh of an imported model: geometry, the file's weights, the file's bind poses,
        /// and one hash per bone - the CRC of the bone's path under the model root, so a bone the file
        /// parents carries its parent's name in its hash (<c>BakedSkin.BonePath</c>).</summary>
        private static void FillModelMesh(AssetTypeValueField mesh, string rootName, BakedSkin skin)
        {
            mesh["m_Name"].AsString = MeshName(rootName);
            MeshFields.Fill(mesh, skin.Mesh);

            // NOTHING is imposed here: the width is the one the FILE actually uses (BakedSkin.Influences).
            int n = skin.Mesh.VertexCount, inf = skin.Influences;
            byte[] stream = new byte[n * SkinStride(inf)];
            for (int v = 0; v < n; v++)
            {
                int at = v * SkinStride(inf);
                for (int i = 0; i < inf; i++)
                {
                    Write(stream, at + i * 4, skin.Weights[v * inf + i]);
                    WriteU32(stream, at + inf * 4 + i * 4, skin.Bones[v * inf + i]);
                }
            }
            SetSkinStream(mesh, skin.Mesh, stream, inf);

            int bones = skin.BoneNames.Length;
            AssetTypeValueField poses = mesh["m_BindPose"]["Array"];
            poses.Children.Clear();
            for (int b = 0; b < bones; b++)
            {
                AssetTypeValueField m = ValueBuilder.DefaultValueFieldFromArrayTemplate(poses);
                // The file's matrix is column-major (index = col*4 + row); the field names are eRC.
                for (int r = 0; r < 4; r++)
                    for (int c = 0; c < 4; c++) m["e" + r + c].AsFloat = skin.BindPoses[b][c * 4 + r];
                poses.Children.Add(m);
            }

            AssetTypeValueField hashes = mesh["m_BoneNameHashes"]["Array"];
            hashes.Children.Clear();
            for (int b = 0; b < bones; b++)
            {
                AssetTypeValueField e = ValueBuilder.DefaultValueFieldFromArrayTemplate(hashes);
                e.AsUInt = BoneHash(skin.BonePath(b));
                hashes.Children.Add(e);
            }
            mesh["m_RootBoneNameHash"].AsUInt = BoneHash(skin.BonePath(0));

            // One AABB per bone, in that bone's own space, over the vertices that bone actually moves.
            // Left describing nothing the renderer is culled wherever the bone's box is empty, which
            // reads exactly like "the model did not load".
            AssetTypeValueField aabbs = mesh["m_BonesAABB"]["Array"];
            aabbs.Children.Clear();
            for (int b = 0; b < bones; b++)
            {
                float minX = 0f, minY = 0f, minZ = 0f, maxX = 0f, maxY = 0f, maxZ = 0f;
                bool any = false;
                for (int v = 0; v < n; v++)
                {
                    if (skin.WeightOf(v, b) <= 0f) continue;
                    int at = v * BakedMesh.Stride;
                    float x = BitConverter.ToSingle(skin.Mesh.VertexData, at);
                    float y = BitConverter.ToSingle(skin.Mesh.VertexData, at + 4);
                    float z = BitConverter.ToSingle(skin.Mesh.VertexData, at + 8);
                    float[] m = skin.BindPoses[b];
                    float bx = m[0] * x + m[4] * y + m[8] * z + m[12];
                    float by = m[1] * x + m[5] * y + m[9] * z + m[13];
                    float bz = m[2] * x + m[6] * y + m[10] * z + m[14];
                    if (!any) { minX = maxX = bx; minY = maxY = by; minZ = maxZ = bz; any = true; continue; }
                    if (bx < minX) minX = bx; if (bx > maxX) maxX = bx;
                    if (by < minY) minY = by; if (by > maxY) maxY = by;
                    if (bz < minZ) minZ = bz; if (bz > maxZ) maxZ = bz;
                }
                AssetTypeValueField e = ValueBuilder.DefaultValueFieldFromArrayTemplate(aabbs);
                PrefabFields.Vector3(e["m_Min"], minX, minY, minZ);
                PrefabFields.Vector3(e["m_Max"], maxX, maxY, maxZ);
                aabbs.Children.Add(e);
            }

            mesh["m_MeshUsageFlags"].AsInt = 1;
            mesh["m_MeshMetrics[0]"].AsFloat = 1f;
            mesh["m_MeshMetrics[1]"].AsFloat = 1f;
        }

        internal static string SkinName(string rootName) => rootName + "_skin";
        internal static string MeshName(string rootName) => rootName + "_mesh";
        internal static string Bone0Name(string rootName) => rootName + "_bone0";
        internal static string Bone1Name(string rootName) => rootName + "_bone1";

        /// <summary>
        /// CRC-32 of a bone's transform path relative to the model root - what m_BoneNameHashes and
        /// m_RootBoneNameHash carry (identified against aln_fireworm, see the class remark).
        /// </summary>
        internal static uint BoneHash(string path) { return Continue(0xFFFFFFFFu, path); }

        /// <summary>
        /// The same CRC-32 continued from a register state instead of started: the hash of
        /// <c>prefix + tail</c> given the state the prefix left behind, which is <c>~BoneHash(prefix)</c>.
        /// This is what lets a bone's hash be checked without knowing the prefix at all - see
        /// <see cref="BonesAligned"/>.
        /// </summary>
        private static uint Continue(uint state, string tail)
        {
            uint c = state;
            foreach (char ch in tail)
            {
                c ^= (byte)ch;
                for (int k = 0; k < 8; k++) c = (c >> 1) ^ (0xEDB88320u & (uint)-(int)(c & 1));
            }
            return ~c;
        }

        /// <summary>
        /// What a baked skin in a FILE actually holds, in one line - the oracle the offline round trip
        /// and the in-game U5 gate both read. Every reference is reported by the target's NAME, and
        /// every count by its number: values a broken PPtr or an empty array cannot produce.
        /// </summary>
        internal static string Summary(AssetsManager m, AssetsFileInstance af, string rootName)
        {
            AssetTypeValueField root = PrefabFields.FindGameObject(m, af, rootName);
            if (root == null) return "no GameObject named '" + rootName + "'";
            AssetTypeValueField rootTf = PrefabFields.Component(m, af, root, AssetClassID.Transform);
            if (rootTf == null) return "root '" + rootName + "' has no Transform";

            AssetTypeValueField skinGo = PrefabFields.FindGameObject(m, af, SkinName(rootName));
            if (skinGo == null) return "root '" + rootName + "' has no '" + SkinName(rootName) + "'";
            AssetTypeValueField r = PrefabFields.Component(m, af, skinGo, AssetClassID.SkinnedMeshRenderer);
            if (r == null) return "'" + SkinName(rootName) + "' has no SkinnedMeshRenderer";

            AssetTypeValueField bones = r["m_Bones"]["Array"];
            AssetTypeValueField mesh = PrefabFields.Get(m, af, r["m_Mesh"]["m_PathID"].AsLong);
            if (mesh == null) return "the SkinnedMeshRenderer's m_Mesh resolves to nothing";

            AssetTypeValueField vd = mesh["m_VertexData"];
            byte[] data = vd["m_DataSize"].AsByteArray;
            int verts = (int)vd["m_VertexCount"].AsUInt;
            AssetTypeValueField weightCh = vd["m_Channels"]["Array"].Children[ChannelBlendWeight];
            AssetTypeValueField indexCh = vd["m_Channels"]["Array"].Children[ChannelBlendIndices];
            int skinAt = SkinOffset(verts);

            return "root '" + rootName + "' children=" + rootTf["m_Children"]["Array"].Children.Count +
                   " | skin '" + skinGo["m_Name"].AsString + "'" +
                   " bones=" + bones.Children.Count +
                   " bone0=" + BoneName(m, af, bones, 0) +
                   " bone1=" + BoneName(m, af, bones, 1) +
                   " rootBone=" + TransformName(m, af, r["m_RootBone"]["m_PathID"].AsLong) +
                   " mesh=" + PrefabFields.Name(m, af, r["m_Mesh"]["m_PathID"].AsLong) +
                   " material=" + PrefabFields.Name(m, af, r["m_Materials"]["Array"].Children.Count == 0
                                                          ? 0 : r["m_Materials"]["Array"].Children[0]["m_PathID"].AsLong) +
                   " | mesh verts=" + verts +
                   " bindposes=" + mesh["m_BindPose"]["Array"].Children.Count +
                   " bindpose1.e13=" + Bindpose13(mesh) +
                   " hashes=" + Hashes(mesh) +
                   " rootHash=" + mesh["m_RootBoneNameHash"].AsUInt +
                   " bonesAABB=" + mesh["m_BonesAABB"]["Array"].Children.Count +
                   " weightCh=stream" + weightCh["stream"].AsByte + "/off" + weightCh["offset"].AsByte +
                   "/fmt" + weightCh["format"].AsByte + "/dim" + weightCh["dimension"].AsByte +
                   " indexCh=stream" + indexCh["stream"].AsByte + "/off" + indexCh["offset"].AsByte +
                   "/fmt" + indexCh["format"].AsByte + "/dim" + indexCh["dimension"].AsByte +
                   " bytes=" + (data == null ? 0 : data.Length) +
                   " vertex0=" + Influence(data, skinAt, 0, InfluencesOf(mesh)) +
                   " vertexLast=" + Influence(data, skinAt, verts - 1, InfluencesOf(mesh)) +
                   " | tree " + Tree(m, af, bones);
        }

        /// <summary>
        /// Who carries whom, off the WRITTEN BYTES: per bone, "name&lt;father#children". BOTH halves,
        /// because U4 measured that the engine builds the hierarchy from m_Children alone - a bone
        /// whose m_Father was zeroed still reported as a child - so a rig with one half written is
        /// invisible to any arm that asks the engine, and this is the only place it shows.
        /// </summary>
        private static string Tree(AssetsManager m, AssetsFileInstance af, AssetTypeValueField bones)
        {
            string s = "";
            for (int b = 0; b < bones.Children.Count; b++)
            {
                AssetTypeValueField tf = PrefabFields.Get(m, af, bones.Children[b]["m_PathID"].AsLong);
                if (tf == null) { s += (b == 0 ? "" : ",") + "(unresolved)"; continue; }
                s += (b == 0 ? "" : ",") + BoneName(m, af, bones, b) +
                     "<" + TransformName(m, af, tf["m_Father"]["m_PathID"].AsLong) +
                     "#" + tf["m_Children"]["Array"].Children.Count;
            }
            return s;
        }

        /// <summary>
        /// Binds a mesh that has just been REPLACED to the skeleton its target already carries. An
        /// .obj holds no skin data at all, so the weights cannot be imported - they are DERIVED from
        /// the one thing the target does supply, its <c>m_BindPose</c> array: each new vertex goes,
        /// whole, to the bone whose bind pose brings it CLOSEST to that bone's own origin. The bind
        /// poses, the bone name hashes and the root hash are left exactly as shipped, so they stay in
        /// step with the SkinnedMeshRenderer's own m_Bones list, which this never sees and never
        /// touches. Returns false, changing nothing, when the target is not rigged.
        ///
        /// Nearest-bone is chosen over transferring the ORIGINAL mesh's weights by nearest original
        /// vertex because it needs no second decode: the bind poses are already parsed field data,
        /// while the shipped weights sit in a stream whose layout varies per mesh and whose vertices
        /// have to be searched. Both give the same class of result - rigid segments, no blending -
        /// and only the cheap one is measurable offline.
        ///
        /// ponytail: one full-weight influence per vertex, so a vertex follows exactly one bone and a
        /// joint creases instead of bending. That is not a stream limit and widening it would only
        /// fabricate more - the weld is synthesised, and there is nothing to spread it over. Real
        /// smooth weights need a skinned interchange format carrying its own weights, which is
        /// <see cref="RebindByName"/>. The remaining slots are written at weight 0 so the TARGET's
        /// declared width survives the replacement.
        /// </summary>
        /// <summary>Does the target deform with a skeleton? Its own bind poses are the answer, which is
        /// the same thing <see cref="Rebind"/> keys on - one fact, read one way.</summary>
        internal static bool Rigged(AssetTypeValueField mesh)
        {
            return mesh != null && BindPoses(mesh).Length > 0;
        }

        /// <summary>
        /// Why a mesh with no armature cannot replace a RIGGED one. Both replacement paths - the bake
        /// (BundleBaker.ReplaceMesh) and the live swap (SeamSwap) - refuse with this exact sentence,
        /// because a refusal in only one of them is worse than none.
        ///
        /// The nearest-bone synthesis below still exists and still runs: it is what a .glb whose
        /// armature is somebody ELSE's falls back to. What is refused is the case with no weights to
        /// fall back FROM, where every vertex was welded to one bone and the author saw their model
        /// collapse onto a hip mid-animation without being told why.
        ///
        /// ponytail: no opt-in flag. Nothing shipped depends on skinless-onto-rigged - the one demo
        /// that replaces a mesh with a skinless source (demos\WeaponMesh) targets a STATIC
        /// MeshFilter, which this does not touch. Add a manifest key if a real rigid attachment ever
        /// turns up.
        /// </summary>
        internal static string Skinless(string target)
        {
            return "'" + target + "' is a rigged model - it bends with the character's skeleton - and " +
                   "the replacement file carries no armature, so there are no weights to follow that " +
                   "skeleton with. Every vertex would be welded to whichever bone it happens to sit " +
                   "nearest, and the model would collapse onto that one bone as soon as the character " +
                   "moves. In Blender, give the mesh an Armature modifier with vertex groups, weight it " +
                   "to the bones the target already has, and export it as .glb. A file with no armature " +
                   "can only replace a STATIC object (one with a MeshFilter, like a weapon).";
        }

        /// <param name="influences">
        /// the width the target itself declared, read with <see cref="InfluencesOf"/> BEFORE
        /// <see cref="MeshFields.Fill"/> cleared it. Below 1 it is taken as 1 - a mesh has to carry
        /// the one influence this synthesises somewhere.
        /// </param>
        internal static bool Rebind(AssetTypeValueField mesh, BakedMesh baked, int influences)
        {
            if (mesh == null) throw new ArgumentNullException(nameof(mesh));
            if (baked == null) throw new ArgumentNullException(nameof(baked));

            float[][] bind = BindPoses(mesh);
            int bones = bind.Length;
            if (bones == 0) return false;

            int inf = Math.Max(influences, 1);
            int n = baked.VertexCount;
            uint[] idx = new uint[n * inf];
            float[] w = new float[n * inf];
            for (int i = 0; i < n; i++)
            {
                int at = i * BakedMesh.Stride;
                float x = BitConverter.ToSingle(baked.VertexData, at);
                float y = BitConverter.ToSingle(baked.VertexData, at + 4);
                float z = BitConverter.ToSingle(baked.VertexData, at + 8);

                int best = 0;
                float bestD = float.MaxValue;
                for (int b = 0; b < bones; b++)
                {
                    // The bind pose IS the mesh-space -> bone-space transform, so the distance from
                    // the vertex to the bone is just the length of the transformed vertex. No matrix
                    // has to be inverted to find out where the bone sits.
                    float[] m = bind[b];
                    float tx = m[0] * x + m[1] * y + m[2] * z + m[3];
                    float ty = m[4] * x + m[5] * y + m[6] * z + m[7];
                    float tz = m[8] * x + m[9] * y + m[10] * z + m[11];
                    float d = tx * tx + ty * ty + tz * tz;
                    if (d >= bestD) continue;
                    bestD = d; best = b;
                }
                // Slot 0 takes the whole vertex; the rest stay at weight 0 on bone 0.
                idx[i * inf] = (uint)best; w[i * inf] = 1f;
            }

            WriteSkin(mesh, baked, bind, idx, w, inf);
            return true;
        }

        /// <summary>
        /// The file's OWN weights onto the target's OWN skeleton, matched BY BONE NAME - the upgrade
        /// path Rebind's remark names, taken because a .glb carries what an .obj never could.
        ///
        /// <see cref="Import.SkinBinder"/> does the matching and is the same code the live seam runs,
        /// so the preview and the bake cannot disagree about which bone a vertex belongs to. It is a
        /// strict bijection between the file's joint names and <paramref name="boneNames"/>, which
        /// means joint ORDER is free while membership is not: a file that lists the skeleton backwards
        /// binds correctly, and a file with an added or missing bone is REFUSED by name rather than
        /// bound badly. It throws before producing anything, so a caller can fall back to
        /// <see cref="Rebind"/> having changed nothing.
        ///
        /// The skeleton is never touched here either: m_BindPose, m_BoneNameHashes and
        /// m_RootBoneNameHash stay exactly as shipped, so they stay in step with the
        /// SkinnedMeshRenderer's own m_Bones list. Only the per-vertex influences change.
        ///
        /// ponytail: the file's own inverse bind matrices are READ (SkinBinder validates them) and
        /// then dropped in favour of the shipped ones - the author's model has to be posed on the
        /// skeleton it replaces, which is what "extract, edit, drop back in" already means. Upgrade
        /// path: write them into m_BindPose too, once something needs to re-pose a shipped skeleton.
        /// The stream is as wide as the TARGET declared - that upgrade path is taken: a dim4 shipped
        /// mesh keeps all four of the file's influences, and a dim2 one keeps the two heaviest,
        /// renormalised, because two is what its own vertex layout carries.
        /// </summary>
        /// <param name="influences">
        /// the target's own width, read with <see cref="InfluencesOf"/> BEFORE
        /// <see cref="MeshFields.Fill"/> cleared it. Below 1 it is taken as 1.
        /// </param>
        internal static bool RebindByName(AssetTypeValueField mesh, BakedMesh baked,
                                          SkinnedModel file, IList<string> boneNames, int influences)
        {
            if (mesh == null) throw new ArgumentNullException(nameof(mesh));
            if (baked == null) throw new ArgumentNullException(nameof(baked));

            float[][] bind = BindPoses(mesh);
            if (bind.Length == 0) return false;
            if (boneNames == null || boneNames.Count != bind.Length)
                throw new FormatException("the target has " + bind.Length.ToString(CultureInfo.InvariantCulture) +
                    " bind pose(s) but " + (boneNames == null ? 0 : boneNames.Count).ToString(CultureInfo.InvariantCulture) +
                    " named bone(s), so a bone in the file cannot be matched to one of them");

            ushort[] joints;
            float[][] unused;
            // 0 material slots and no blend shapes: the replacement keeps the shipped mesh's own
            // submesh and shape lists, so those two checks would be asking about something this
            // path does not write.
            SkinBinder.Bind(file, boneNames, 0, null, out joints, out unused);

            int n = baked.VertexCount;
            if (file.Positions == null || file.Positions.Length != n)
                throw new FormatException("the file's skin covers " +
                    (file.Positions == null ? 0 : file.Positions.Length).ToString(CultureInfo.InvariantCulture) +
                    " vertices but the baked mesh has " + n.ToString(CultureInfo.InvariantCulture));

            int inf = Math.Max(influences, 1);
            int[] slots = new int[inf];
            uint[] idx = new uint[n * inf];
            float[] w = new float[n * inf];
            for (int i = 0; i < n; i++)
            {
                Heaviest(file.Weights, i, slots);
                float sum = 0f;
                for (int k = 0; k < inf; k++) if (slots[k] >= 0) sum += file.Weights[i * 4 + slots[k]];

                int at = i * inf;
                if (sum <= 0f)
                {
                    // A vertex the file left unweighted would otherwise render at the origin; it goes
                    // whole to the bone its first slot names, which is what the file itself says.
                    idx[at] = joints[i * 4]; w[at] = 1f;
                    for (int k = 1; k < inf; k++) idx[at + k] = idx[at];
                    continue;
                }
                for (int k = 0; k < inf; k++)
                {
                    // An empty slot rides the dominant bone at weight 0 - a bone index the mesh has
                    // no bind pose for is what the engine reads as a broken skin.
                    idx[at + k] = joints[i * 4 + (slots[k] >= 0 ? slots[k] : slots[0])];
                    w[at + k] = slots[k] >= 0 ? file.Weights[i * 4 + slots[k]] / sum : 0f;
                }
            }

            WriteSkin(mesh, baked, bind, idx, w, inf);
            return true;
        }

        /// <summary>
        /// The heaviest of a glTF vertex's four influences, dominant first, one per slot of
        /// <paramref name="slots"/>; -1 for a slot the file has no non-zero weight left for. Shared
        /// with the P6 arm so the gate and the bake cannot disagree about WHICH of the four survive a
        /// narrower target - the arm's own question is which BONE they land on, and that it derives
        /// independently.
        /// </summary>
        internal static void Heaviest(float[] weights, int vertex, int[] slots)
        {
            for (int s = 0; s < slots.Length; s++)
            {
                int best = -1;
                for (int k = 0; k < 4; k++)
                {
                    if (weights[vertex * 4 + k] <= 0f) continue;
                    bool taken = false;
                    for (int t = 0; t < s; t++) if (slots[t] == k) { taken = true; break; }
                    if (taken) continue;
                    if (best < 0 || weights[vertex * 4 + k] > weights[vertex * 4 + best]) best = k;
                }
                slots[s] = best;
            }
        }

        /// <summary>The shipped bind poses, row-major [R|t] - the same eRC naming Bindpose() writes.</summary>
        private static float[][] BindPoses(AssetTypeValueField mesh)
        {
            AssetTypeValueField poses = mesh["m_BindPose"]["Array"];
            float[][] bind = new float[poses.Children.Count][];
            for (int b = 0; b < bind.Length; b++)
            {
                float[] m = new float[12];
                for (int r = 0; r < 3; r++)
                    for (int c = 0; c < 4; c++) m[r * 4 + c] = poses.Children[b]["e" + r + c].AsFloat;
                bind[b] = m;
            }
            return bind;
        }

        /// <summary>
        /// The skin stream and the per-bone bounds for an already-decided binding. Shared by
        /// <see cref="Rebind"/> and <see cref="RebindByName"/> so the two cannot lay the same bytes
        /// out differently - only WHERE the influences come from separates them.
        /// </summary>
        /// <param name="idx">bone index per influence per vertex - <paramref name="influences"/> wide.</param>
        /// <param name="w">the matching weights, same width, summing to 1 per vertex.</param>
        private static void WriteSkin(AssetTypeValueField mesh, BakedMesh baked, float[][] bind,
                                      uint[] idx, float[] w, int influences)
        {
            int bones = bind.Length, n = baked.VertexCount;
            int stride = SkinStride(influences);
            byte[] skin = new byte[n * stride];
            float[] minX = new float[bones], minY = new float[bones], minZ = new float[bones];
            float[] maxX = new float[bones], maxY = new float[bones], maxZ = new float[bones];
            bool[] used = new bool[bones];
            for (int i = 0; i < n; i++)
            {
                int at = i * BakedMesh.Stride;
                float x = BitConverter.ToSingle(baked.VertexData, at);
                float y = BitConverter.ToSingle(baked.VertexData, at + 4);
                float z = BitConverter.ToSingle(baked.VertexData, at + 8);

                // The vertex goes into the box of EVERY bone that moves it, in that bone's own space -
                // the same rule FillModelMesh keeps. The dominant influence alone leaves any bone used
                // only in a lower slot with NO box at all, and the engine culls what a box does not
                // cover, which reads exactly like "the replacement did not load". A zero-weight slot
                // does not move the vertex, so it must not stretch that bone's box either - which is
                // what keeps the nearest-bone weld (one full weight, the rest at zero) honest.
                for (int k = 0; k < influences; k++)
                {
                    if (w[i * influences + k] <= 0f) continue;
                    int d = (int)idx[i * influences + k];
                    float[] m = bind[d];
                    float tx = m[0] * x + m[1] * y + m[2] * z + m[3];
                    float ty = m[4] * x + m[5] * y + m[6] * z + m[7];
                    float tz = m[8] * x + m[9] * y + m[10] * z + m[11];
                    if (!used[d])
                    {
                        used[d] = true;
                        minX[d] = maxX[d] = tx; minY[d] = maxY[d] = ty; minZ[d] = maxZ[d] = tz;
                        continue;
                    }
                    if (tx < minX[d]) minX[d] = tx; if (tx > maxX[d]) maxX[d] = tx;
                    if (ty < minY[d]) minY[d] = ty; if (ty > maxY[d]) maxY[d] = ty;
                    if (tz < minZ[d]) minZ[d] = tz; if (tz > maxZ[d]) maxZ[d] = tz;
                }

                int sa = i * stride;
                for (int k = 0; k < influences; k++)
                {
                    Write(skin, sa + k * 4, w[i * influences + k]);
                    WriteU32(skin, sa + influences * 4 + k * 4, idx[i * influences + k]);
                }
            }

            SetSkinStream(mesh, baked, skin, influences);

            AssetTypeValueField aabbs = mesh["m_BonesAABB"]["Array"];
            aabbs.Children.Clear();
            for (int b = 0; b < bones; b++)
            {
                AssetTypeValueField e = ValueBuilder.DefaultValueFieldFromArrayTemplate(aabbs);
                PrefabFields.Vector3(e["m_Min"], minX[b], minY[b], minZ[b]);
                PrefabFields.Vector3(e["m_Max"], maxX[b], maxY[b], maxZ[b]);
                aabbs.Children.Add(e);
            }

            // The per-vertex influence COUNT of the old geometry. Left behind it is read in preference
            // to the width this stream declares - the same trap MeshFields.Fill clears for the
            // compressed mesh and the blend shapes.
            AssetTypeValueField var = mesh["m_VariableBoneCountWeights"];
            if (!var.IsDummy && !var["m_Data"].IsDummy) var["m_Data"]["Array"].Children.Clear();
        }

        /// <summary>
        /// What a mesh's SKIN holds, off the FILE - the oracle the in-game P5 gate and the offline
        /// round trip both read. Everything is a count or a number the data itself supplies; a layout
        /// this cannot decode says so rather than reporting a zero that reads like an answer.
        /// </summary>
        internal static string SkinSummary(AssetTypeValueField mesh)
        {
            int poses = mesh["m_BindPose"]["Array"].Children.Count;
            AssetTypeValueField vd = mesh["m_VertexData"];
            AssetTypeValueField chs = vd["m_Channels"]["Array"];
            string head = "bindposes=" + poses +
                          " hashes=" + mesh["m_BoneNameHashes"]["Array"].Children.Count +
                          " rootHash=" + mesh["m_RootBoneNameHash"].AsUInt +
                          " bonesAABB=" + mesh["m_BonesAABB"]["Array"].Children.Count;
            if (chs.Children.Count <= ChannelBlendIndices) return head + " (no skin channel slots)";

            AssetTypeValueField w = chs.Children[ChannelBlendWeight], b = chs.Children[ChannelBlendIndices];
            string layout = "weightCh=stream" + w["stream"].AsByte + "/off" + w["offset"].AsByte +
                            "/fmt" + w["format"].AsByte + "/dim" + w["dimension"].AsByte +
                            " indexCh=stream" + b["stream"].AsByte + "/off" + b["offset"].AsByte +
                            "/fmt" + b["format"].AsByte + "/dim" + b["dimension"].AsByte;
            head += " " + layout;
            int inf = InfluencesOf(mesh);
            if (layout != OurLayout(inf)) return head + " skinBytes=(other layout) boneMax=(other layout) inRange=(other layout)";

            int verts = (int)vd["m_VertexCount"].AsUInt;
            byte[] data = vd["m_DataSize"].AsByteArray;
            int at = SkinOffset(verts), stride = SkinStride(inf);
            int bytes = data == null ? 0 : data.Length - at;
            if (bytes != verts * stride) return head + " skinBytes=" + bytes + " boneMax=(unreadable) inRange=no";

            long max = -1;
            for (int i = 0; i < verts; i++)
                for (int k = 0; k < inf; k++)
                {
                    long index = BitConverter.ToUInt32(data, at + i * stride + inf * 4 + k * 4);
                    if (index > max) max = index;
                }
            return head + " skinBytes=" + bytes + " boneMax=" + max +
                   " inRange=" + (poses > 0 && max >= 0 && max < poses ? "yes" : "no");
        }

        /// <summary>
        /// The NAMES of the bones a Mesh is skinned to, in the order its bind poses are in - what
        /// <see cref="RebindByName"/> matches a file's joints against.
        ///
        /// The mesh itself does not carry them: it carries CRC-32 hashes of bone PATHS
        /// (<see cref="BoneHash"/>), which cannot be inverted. The names live on the
        /// SkinnedMeshRenderer that USES the mesh, whose m_Bones list is index-for-index with
        /// m_BindPose - so this finds that renderer by the PPtr it holds and reads each bone's
        /// GameObject name. null when nothing in this file uses the mesh, when a bone will not
        /// resolve, or when two renderers use it and DISAGREE about the skeleton: an ambiguity is
        /// refused and never guessed, the same rule <see cref="AssetIndex.FindUnique"/> keeps.
        ///
        /// This is the ONE place that decides whether a mesh's bones are named, so both the count
        /// and the ORDER are settled here rather than assumed by each caller: index-for-index with
        /// m_BindPose is a convention, and <see cref="BonesAligned"/> holds the file to it.
        /// </summary>
        internal static string[] BoneNames(AssetsManager m, AssetsFileInstance af, long meshPathId)
        {
            AssetTypeValueField mesh = PrefabFields.Get(m, af, meshPathId);
            if (mesh == null) return null;
            AssetTypeValueField hashArray = mesh["m_BoneNameHashes"]["Array"];
            int poses = mesh["m_BindPose"]["Array"].Children.Count;
            if (poses == 0 || hashArray.Children.Count != poses) return null;
            uint[] hashes = new uint[poses];
            for (int b = 0; b < poses; b++) hashes[b] = hashArray.Children[b].AsUInt;
            uint rootHash = mesh["m_RootBoneNameHash"].AsUInt;

            string[] found = null;
            foreach (AssetFileInfo i in af.file.Metadata.GetAssetsOfType(AssetClassID.SkinnedMeshRenderer))
            {
                AssetTypeValueField r = m.GetBaseField(af, i);
                if (r["m_Mesh"]["m_PathID"].AsLong != meshPathId) continue;

                AssetTypeValueField bones = r["m_Bones"]["Array"];
                // A renderer that disagrees about the LENGTH is not that correspondence at all, so
                // its names are refused whole rather than half-applied to the bind poses.
                if (bones.Children.Count != poses) return null;
                string[] names = new string[poses], paths = new string[poses];
                for (int b = 0; b < poses; b++)
                {
                    paths[b] = TransformPath(m, af, bones.Children[b]["m_PathID"].AsLong);
                    if (paths[b] == null) return null;
                    int slash = paths[b].LastIndexOf('/');
                    names[b] = slash < 0 ? paths[b] : paths[b].Substring(slash + 1);
                }
                // The anchor the other hashes are checked against: the renderer's own m_RootBone, or
                // the bone that carries the mesh's root hash when the renderer does not name one.
                long rootId = r["m_RootBone"]["m_PathID"].AsLong;
                string rootPath = rootId == 0 ? null : TransformPath(m, af, rootId);
                if (rootPath == null)
                    for (int b = 0; b < poses; b++) if (hashes[b] == rootHash) { rootPath = paths[b]; break; }
                if (!BonesAligned(paths, rootPath, rootHash, hashes)) return null;

                if (found == null) { found = names; continue; }
                for (int b = 0; b < poses; b++) if (found[b] != names[b]) return null;
            }
            return found;
        }

        /// <summary>
        /// Is m_Bones REALLY in the mesh's own bone order? Each m_BoneNameHashes entry is the CRC-32
        /// of that bone's transform PATH (<see cref="BoneHash"/>), while the renderer's m_Bones order
        /// is only asserted - Unity lets it be written in any order, and mutoid_assets_all really does
        /// ship one whose root bone is slot 2 - so the correspondence is verified, not trusted.
        ///
        /// The path those hashes are of CANNOT be rebuilt from the file: it is relative to the rig the
        /// mesh was IMPORTED under, and a shipped addon prefab keeps only the subtree under a renamed
        /// placeholder root (crc of every suffix of 'PLACEHOLDER_Mutoid_Head_Spitter_Ready/Head/Mutoid'
        /// misses m_BoneNameHashes[0], measured). The unknown prefix is not needed, because CRC-32 is
        /// CONTINUABLE: a bone under the root bone extends the ROOT's path, and the root's own hash is
        /// m_RootBoneNameHash - so its register state is ~rootHash and every descendant's hash is that
        /// state continued along the path BELOW the root bone. Exact on both shipped meshes the mesh
        /// gate reads, and it pins order and hierarchy together: a swapped pair fails.
        ///
        /// false whenever a bone cannot be checked at all - no anchor, or a bone that does not descend
        /// from the root bone - because "not checked" and "verified" must not report the same.
        /// </summary>
        internal static bool BonesAligned(string[] paths, string rootPath, uint rootHash, uint[] hashes)
        {
            if (rootPath == null || paths.Length != hashes.Length) return false;
            for (int b = 0; b < paths.Length; b++)
            {
                if (paths[b] == rootPath)
                {
                    if (hashes[b] != rootHash) return false;
                    continue;
                }
                if (!paths[b].StartsWith(rootPath + "/", StringComparison.Ordinal)) return false;
                if (Continue(~rootHash, paths[b].Substring(rootPath.Length)) != hashes[b]) return false;
            }
            return true;
        }

        /// <summary>
        /// One transform's path from the TOP of this file's hierarchy, root first ("root/spine/head"),
        /// walked through m_Father the same way the names are read. null when a link, a GameObject or
        /// a name will not resolve, or the chain loops. The model root the bone hashes are relative to
        /// sits somewhere on this path; <see cref="BonesAligned"/> is what finds where.
        /// </summary>
        private static string TransformPath(AssetsManager m, AssetsFileInstance af, long transformPathId)
        {
            string path = null;
            long at = transformPathId;
            for (int steps = 0; at != 0 && steps <= MaxBoneDepth; steps++)
            {
                AssetTypeValueField tf = PrefabFields.Get(m, af, at);
                AssetTypeValueField go = tf == null ? null
                    : PrefabFields.Get(m, af, tf["m_GameObject"]["m_PathID"].AsLong);
                if (go == null || go["m_Name"].IsDummy) return null;
                string name = go["m_Name"].AsString;
                if (string.IsNullOrEmpty(name)) return null;
                path = path == null ? name : name + "/" + path;
                at = tf["m_Father"]["m_PathID"].AsLong;
            }
            return at == 0 ? path : null;
        }

        private const int MaxBoneDepth = 256;

        /// <summary>
        /// Every vertex's influences, read back OUT OF THE WRITTEN BYTES: "v0=w0/w1-&gt;bone3+bone7".
        /// SkinSummary answers "is there a skin and does it index a bone that exists"; this answers
        /// WHICH bone and with what share, which is the only thing that separates a binding matched
        /// by name from one that took the file's own joint slots as target indices.
        /// </summary>
        internal static string SkinInfluences(AssetTypeValueField mesh)
        {
            AssetTypeValueField vd = mesh["m_VertexData"];
            AssetTypeValueField chs = vd["m_Channels"]["Array"];
            if (chs.Children.Count <= ChannelBlendIndices) return "(no skin channel slots)";
            AssetTypeValueField w = chs.Children[ChannelBlendWeight], b = chs.Children[ChannelBlendIndices];
            string layout = "weightCh=stream" + w["stream"].AsByte + "/off" + w["offset"].AsByte +
                            "/fmt" + w["format"].AsByte + "/dim" + w["dimension"].AsByte +
                            " indexCh=stream" + b["stream"].AsByte + "/off" + b["offset"].AsByte +
                            "/fmt" + b["format"].AsByte + "/dim" + b["dimension"].AsByte;
            int inf = InfluencesOf(mesh);
            if (layout != OurLayout(inf)) return "(other layout: " + layout + ")";

            int verts = (int)vd["m_VertexCount"].AsUInt;
            byte[] data = vd["m_DataSize"].AsByteArray;
            int at = SkinOffset(verts), stride = SkinStride(inf);
            if (data == null || at + verts * stride > data.Length) return "(skin stream is short)";
            string s = "";
            for (int i = 0; i < verts; i++)
            {
                int v = at + i * stride;
                s += (i == 0 ? "" : " ") + "v" + i + "=";
                for (int k = 0; k < inf; k++)
                    s += (k == 0 ? "" : "/") + F(BitConverter.ToSingle(data, v + k * 4));
                for (int k = 0; k < inf; k++)
                    s += (k == 0 ? "->bone" : "+bone") + BitConverter.ToUInt32(data, v + inf * 4 + k * 4);
            }
            return s;
        }

        /// <summary>The channel layout <see cref="SetSkinStream"/> writes at a given width, as
        /// SkinSummary prints it.</summary>
        internal static string OurLayout(int influences)
        {
            return "weightCh=stream1/off0/fmt0/dim" + influences +
                   " indexCh=stream1/off" + influences * 4 + "/fmt10/dim" + influences;
        }

        // ---------------------------------------------------------------- internals

        /// <summary>
        /// Channels 12 and 13 into stream 1, and <paramref name="skin"/> appended after the aligned
        /// stream 0. Shared by the from-scratch bake and by <see cref="Rebind"/> so the two cannot
        /// describe the same bytes differently.
        /// </summary>
        private static void SetSkinStream(AssetTypeValueField mesh, BakedMesh baked, byte[] skin,
                                          int influences)
        {
            AssetTypeValueField vd = mesh["m_VertexData"];
            AssetTypeValueField channels = vd["m_Channels"]["Array"];
            AssetTypeValueField w = channels.Children[ChannelBlendWeight];
            w["stream"].AsInt = 1; w["offset"].AsInt = 0;
            w["format"].AsInt = FormatFloat32; w["dimension"].AsInt = influences;
            AssetTypeValueField b = channels.Children[ChannelBlendIndices];
            b["stream"].AsInt = 1; b["offset"].AsInt = influences * 4;
            b["format"].AsInt = FormatUInt32; b["dimension"].AsInt = influences;

            int skinAt = SkinOffset(baked.VertexCount);
            byte[] all = new byte[skinAt + skin.Length];
            Buffer.BlockCopy(baked.VertexData, 0, all, 0, baked.VertexData.Length);
            Buffer.BlockCopy(skin, 0, all, skinAt, skin.Length);
            vd["m_DataSize"].Value = new AssetTypeValue(all, false);
            vd["m_DataSize"].TemplateField.ValueType = AssetValueType.ByteArray;
        }

        /// <summary>Byte offset of the skin stream: right after the 16-byte-aligned stream 0.</summary>
        internal static int SkinOffset(int vertexCount)
        {
            int stream0 = vertexCount * BakedMesh.Stride;
            int pad = (StreamAlignment - stream0 % StreamAlignment) % StreamAlignment;
            return stream0 + pad;
        }

        /// <summary>
        /// The skin stream of the two-bone FIXTURE: for every vertex, <see cref="FixtureInfluences"/>
        /// float weights then the same number of UInt32 bone indices. One full-weight influence per
        /// vertex - see the class remark.
        /// </summary>
        private static void SkinBuffer(BakedMesh baked, float boneY, bool splitWeights,
                                       out byte[] stream, out int[] boneOfVertex)
        {
            int n = baked.VertexCount;
            boneOfVertex = new int[n];
            stream = new byte[n * SkinStride(FixtureInfluences)];
            for (int i = 0; i < n; i++)
            {
                // Vertex Y out of stream 0 - position is channel 0, so it is the second float.
                float y = BitConverter.ToSingle(baked.VertexData, i * BakedMesh.Stride + 4);
                boneOfVertex[i] = splitWeights && y >= baked.CenterY ? 1 : 0;
                int at = i * SkinStride(FixtureInfluences);
                Write(stream, at, 1f);                                  // weight of influence 0
                Write(stream, at + 4, 0f);                              // influence 1 unused
                WriteU32(stream, at + 8, (uint)boneOfVertex[i]);
                WriteU32(stream, at + 12, 0u);
            }
        }

        private static void FillMesh(AssetTypeValueField mesh, string name, BakedMesh baked,
                                     byte[] skin, int[] boneOfVertex, float boneY,
                                     string bone0, string bone1)
        {
            mesh["m_Name"].AsString = name;
            // Geometry, channels 0/1/4 and every "forget the old geometry" field - one place, shared
            // with the shipped-mesh replacement path so the two cannot drift.
            MeshFields.Fill(mesh, baked);

            SetSkinStream(mesh, baked, skin, FixtureInfluences);

            // bone0 sits on the root, bone1 boneY above it, so the bind pose (the INVERSE of the
            // bone's rest transform) is identity and a -boneY translation.
            AssetTypeValueField poses = mesh["m_BindPose"]["Array"];
            poses.Children.Clear();
            poses.Children.Add(Bindpose(poses, 0f));
            poses.Children.Add(Bindpose(poses, -boneY));

            AssetTypeValueField hashes = mesh["m_BoneNameHashes"]["Array"];
            hashes.Children.Clear();
            // Paths are relative to the model root, which is why bone1 carries bone0's name too.
            uint h0 = BoneHash(bone0), h1 = BoneHash(bone0 + "/" + bone1);
            foreach (uint h in new[] { h0, h1 })
            {
                AssetTypeValueField e = ValueBuilder.DefaultValueFieldFromArrayTemplate(hashes);
                e.AsUInt = h;
                hashes.Children.Add(e);
            }
            mesh["m_RootBoneNameHash"].AsUInt = h0;

            // One AABB per bone, in that bone's own space - shipped meshes carry exactly as many as
            // they have bind poses.
            AssetTypeValueField aabbs = mesh["m_BonesAABB"]["Array"];
            aabbs.Children.Clear();
            for (int bone = 0; bone < 2; bone++)
            {
                AssetTypeValueField e = ValueBuilder.DefaultValueFieldFromArrayTemplate(aabbs);
                float minX = 0f, minY = 0f, minZ = 0f, maxX = 0f, maxY = 0f, maxZ = 0f;
                bool any = false;
                for (int i = 0; i < baked.VertexCount; i++)
                {
                    if (boneOfVertex[i] != bone) continue;
                    int at = i * BakedMesh.Stride;
                    float x = BitConverter.ToSingle(baked.VertexData, at);
                    float y = BitConverter.ToSingle(baked.VertexData, at + 4) - (bone == 1 ? boneY : 0f);
                    float z = BitConverter.ToSingle(baked.VertexData, at + 8);
                    if (!any) { minX = maxX = x; minY = maxY = y; minZ = maxZ = z; any = true; continue; }
                    if (x < minX) minX = x; if (x > maxX) maxX = x;
                    if (y < minY) minY = y; if (y > maxY) maxY = y;
                    if (z < minZ) minZ = z; if (z > maxZ) maxZ = z;
                }
                PrefabFields.Vector3(e["m_Min"], minX, minY, minZ);
                PrefabFields.Vector3(e["m_Max"], maxX, maxY, maxZ);
                aabbs.Children.Add(e);
            }

            // Both measured on every shipped skinned Mesh; the empty template has 0 for all three.
            mesh["m_MeshUsageFlags"].AsInt = 1;
            mesh["m_MeshMetrics[0]"].AsFloat = 1f;
            mesh["m_MeshMetrics[1]"].AsFloat = 1f;
        }

        private static AssetTypeValueField Bindpose(AssetTypeValueField array, float translateY)
        {
            AssetTypeValueField m = ValueBuilder.DefaultValueFieldFromArrayTemplate(array);
            for (int r = 0; r < 4; r++)
                for (int c = 0; c < 4; c++)
                    m["e" + r + c].AsFloat = r == c ? 1f : 0f;
            m["e13"].AsFloat = translateY;   // eRC, so row 1 column 3 = the Y translation
            return m;
        }

        private static void AddChild(AssetTypeValueField transform, long childTransformPathId)
        {
            AssetTypeValueField children = transform["m_Children"]["Array"];
            AssetTypeValueField p = ValueBuilder.DefaultValueFieldFromArrayTemplate(children);
            p["m_FileID"].AsInt = 0;
            p["m_PathID"].AsLong = childTransformPathId;
            children.Children.Add(p);
        }

        private static void Write(byte[] to, int at, float value)
        {
            Buffer.BlockCopy(BitConverter.GetBytes(value), 0, to, at, 4);
        }

        private static void WriteU32(byte[] to, int at, uint value)
        {
            Buffer.BlockCopy(BitConverter.GetBytes(value), 0, to, at, 4);
        }

        /// <summary>"w0/w1->bone" for one vertex of the skin stream, read back out of the bytes - as
        /// many weights as the mesh declares, then the DOMINANT bone.</summary>
        private static string Influence(byte[] data, int skinAt, int vertex, int influences)
        {
            int stride = SkinStride(influences);
            int at = skinAt + vertex * stride;
            if (data == null || vertex < 0 || influences < 1 || at + stride > data.Length) return "(out of range)";
            string s = "";
            for (int k = 0; k < influences; k++)
                s += (k == 0 ? "" : "/") + F(BitConverter.ToSingle(data, at + k * 4));
            return s + "->bone" + BitConverter.ToUInt32(data, at + influences * 4);
        }

        private static string BoneName(AssetsManager m, AssetsFileInstance af, AssetTypeValueField bones, int index)
        {
            if (index >= bones.Children.Count) return "(none)";
            return TransformName(m, af, bones.Children[index]["m_PathID"].AsLong);
        }

        private static string TransformName(AssetsManager m, AssetsFileInstance af, long transformPathId)
        {
            AssetTypeValueField tf = PrefabFields.Get(m, af, transformPathId);
            if (tf == null) return "(unresolved:" + transformPathId + ")";
            return PrefabFields.Name(m, af, tf["m_GameObject"]["m_PathID"].AsLong);
        }

        private static string Bindpose13(AssetTypeValueField mesh)
        {
            AssetTypeValueField poses = mesh["m_BindPose"]["Array"];
            return poses.Children.Count < 2 ? "(none)" : F(poses.Children[1]["e13"].AsFloat);
        }

        private static string Hashes(AssetTypeValueField mesh)
        {
            AssetTypeValueField h = mesh["m_BoneNameHashes"]["Array"];
            string s = h.Children.Count.ToString(CultureInfo.InvariantCulture);
            foreach (AssetTypeValueField e in h.Children) s += ":" + e.AsUInt;
            return s;
        }

        // InvariantCulture: these lines are machine-compared and a ru-RU machine writes 0,5 for 0.5
        // (the trap MeshFields.V and ReadMaterialProperties both document).
        private static string F(float v) => v.ToString("0.###", CultureInfo.InvariantCulture);
    }
}
