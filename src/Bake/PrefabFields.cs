using System;
using System.Globalization;
using AssetsTools.NET;
using AssetsTools.NET.Extra;

namespace Morgott.ContentTool.Bake
{
    /// <summary>
    /// U4 - the GameObject/Transform half of a bake: the six serialized objects a minimal renderable
    /// hierarchy is made of, and how they read back. Split out of <see cref="BundleBaker"/> for the
    /// same reason as <see cref="MeshFields"/> - it touches no Unity type, so the whole
    /// build -&gt; repack -&gt; read round trip is provable offline (tests\ObjCodecTests).
    ///
    /// Layout and every non-zero default MEASURED 2026-08-12 off `aln_egg_explosive_assets_all.bundle`
    /// (unity 2019.4.31f1, the one shipped bundle under 30 MB that carries a MeshFilter+MeshRenderer
    /// pair) - the GameObject 'Mesh' and its three components, dumped field by field. Nothing here is
    /// a remembered Unity layout: `ComponentPair` really has the single field `component` in this
    /// version, and the renderer's odd constants (RayTracingMode 2, LightmapIndex 65535,
    /// LightmapTilingOffset 1,1,0,0) are that object's own values.
    ///
    /// ponytail: the MeshFilter points at a Mesh the cloned bundle ALREADY holds. Creating a Mesh
    /// object from nothing is U5's problem (it needs the 14 vertex channels built from an empty
    /// template, and the skinned fields on top); U4 only has to show the hierarchy and its internal
    /// PPtrs resolve.
    /// </summary>
    internal static class PrefabFields
    {
        internal struct Ids
        {
            internal long RootGameObject, RootTransform, ChildGameObject, ChildTransform, MeshFilter, MeshRenderer;
        }

        /// <summary>
        /// Root GameObject+Transform, and - unless <paramref name="childName"/> is null - a child
        /// GameObject carrying Transform+MeshFilter+MeshRenderer, parented both ways (the child's
        /// m_Father AND the root's m_Children, which is how every shipped prefab writes it).
        /// </summary>
        internal static Ids Build(AssetsFile afile, ClassDatabaseFile cldb, Func<long> nextPathId,
                                  string rootName, string childName,
                                  long meshPathId, long[] materialPathIds,
                                  float x, float y, float z)
        {
            if (afile == null) throw new ArgumentNullException(nameof(afile));
            if (string.IsNullOrEmpty(rootName)) throw new ArgumentException("empty root name", nameof(rootName));

            Ids ids = new Ids();
            ids.RootGameObject = nextPathId();
            ids.RootTransform = nextPathId();
            if (childName != null)
            {
                ids.ChildGameObject = nextPathId();
                ids.ChildTransform = nextPathId();
                ids.MeshFilter = nextPathId();
                ids.MeshRenderer = nextPathId();
            }

            Create(afile, cldb, ids.RootGameObject, AssetClassID.GameObject, go =>
            {
                FillGameObject(go, rootName);
                AddComponent(go, ids.RootTransform);
            });

            Create(afile, cldb, ids.RootTransform, AssetClassID.Transform, tf =>
            {
                FillTransform(tf, ids.RootGameObject, 0f, 0f, 0f);
                if (childName != null)
                {
                    AssetTypeValueField children = tf["m_Children"]["Array"];
                    AssetTypeValueField p = ValueBuilder.DefaultValueFieldFromArrayTemplate(children);
                    p["m_FileID"].AsInt = 0;
                    p["m_PathID"].AsLong = ids.ChildTransform;
                    children.Children.Add(p);
                }
            });

            if (childName == null) return ids;

            Create(afile, cldb, ids.ChildGameObject, AssetClassID.GameObject, go =>
            {
                FillGameObject(go, childName);
                // Component order as shipped: Transform, MeshFilter, MeshRenderer.
                AddComponent(go, ids.ChildTransform);
                AddComponent(go, ids.MeshFilter);
                AddComponent(go, ids.MeshRenderer);
            });

            Create(afile, cldb, ids.ChildTransform, AssetClassID.Transform, tf =>
            {
                FillTransform(tf, ids.ChildGameObject, x, y, z);
                tf["m_Father"]["m_FileID"].AsInt = 0;
                // Both directions, as every shipped prefab writes it. MEASURED 2026-08-12 that the
                // engine builds the parent link from the ROOT's m_Children alone: with this line
                // zeroed Unity still reported childCount=1, and only the file-level arm (U4-wrote,
                // fatherIsRoot) went red. A half-written hierarchy is therefore invisible to the
                // engine arm - do not drop U4-wrote.
                tf["m_Father"]["m_PathID"].AsLong = ids.RootTransform;
            });

            Create(afile, cldb, ids.MeshFilter, AssetClassID.MeshFilter, mf =>
            {
                Pptr(mf["m_GameObject"], ids.ChildGameObject);
                Pptr(mf["m_Mesh"], meshPathId);
            });

            Create(afile, cldb, ids.MeshRenderer, AssetClassID.MeshRenderer, mr =>
            {
                Pptr(mr["m_GameObject"], ids.ChildGameObject);
                mr["m_Enabled"].AsBool = true;
                mr["m_CastShadows"].AsByte = 1;
                mr["m_ReceiveShadows"].AsByte = 1;
                mr["m_DynamicOccludee"].AsByte = 1;
                mr["m_MotionVectors"].AsByte = 1;
                mr["m_LightProbeUsage"].AsByte = 1;
                mr["m_ReflectionProbeUsage"].AsByte = 1;
                mr["m_RayTracingMode"].AsByte = 2;
                mr["m_RenderingLayerMask"].AsUInt = 1;
                // 65535 = "no lightmap". A default 0 would send the renderer at lightmap slot 0.
                mr["m_LightmapIndex"].AsUShort = 65535;
                mr["m_LightmapIndexDynamic"].AsUShort = 65535;
                Vector4(mr["m_LightmapTilingOffset"]);
                Vector4(mr["m_LightmapTilingOffsetDynamic"]);
                // ONE ENTRY PER SUBMESH. Unity draws submesh i with m_Materials[i], so a model that
                // came in as three materials needs three entries here or two of its surfaces take
                // the first one's paint. A single-material model writes a single entry, as before.
                AssetTypeValueField mats = mr["m_Materials"]["Array"];
                foreach (long id in materialPathIds)
                {
                    AssetTypeValueField m = ValueBuilder.DefaultValueFieldFromArrayTemplate(mats);
                    m["m_FileID"].AsInt = 0;
                    m["m_PathID"].AsLong = id;
                    mats.Children.Add(m);
                }
            });

            return ids;
        }

        /// <summary>
        /// What a baked hierarchy in a FILE actually holds, in one line - the oracle the offline round
        /// trip and the in-game U4 gate both read, so a passing test and a passing gate mean the same
        /// thing. References are reported by the target's NAME, never only by pathId: a name is a value
        /// a broken PPtr cannot produce.
        /// </summary>
        internal static string Summary(AssetsManager m, AssetsFileInstance af, string rootName)
        {
            AssetTypeValueField root = FindGameObject(m, af, rootName);
            if (root == null) return "no GameObject named '" + rootName + "'";
            AssetTypeValueField rootTf = Component(m, af, root, AssetClassID.Transform);
            if (rootTf == null) return "root '" + rootName + "' has no Transform";

            AssetTypeValueField children = rootTf["m_Children"]["Array"];
            string s = "root '" + rootName + "' comps=" + root["m_Component"]["Array"].Children.Count +
                       " children=" + children.Children.Count;
            if (children.Children.Count == 0) return s;

            AssetTypeValueField childTf = Get(m, af, children.Children[0]["m_PathID"].AsLong);
            if (childTf == null) return s + " | m_Children[0] resolves to nothing";
            AssetTypeValueField childGo = Get(m, af, childTf["m_GameObject"]["m_PathID"].AsLong);
            if (childGo == null) return s + " | the child Transform owns no GameObject";

            AssetTypeValueField mf = Component(m, af, childGo, AssetClassID.MeshFilter);
            AssetTypeValueField mr = Component(m, af, childGo, AssetClassID.MeshRenderer);
            return s +
                " | child '" + childGo["m_Name"].AsString + "'" +
                " comps=" + childGo["m_Component"]["Array"].Children.Count +
                " fatherIsRoot=" + (childTf["m_Father"]["m_PathID"].AsLong ==
                                    ComponentId(af, root, AssetClassID.Transform)) +
                " pos=" + V(childTf["m_LocalPosition"]) +
                " scale=" + V(childTf["m_LocalScale"]) +
                " mesh=" + Name(m, af, mf == null ? 0 : mf["m_Mesh"]["m_PathID"].AsLong) +
                " material=" + Name(m, af, mr == null || mr["m_Materials"]["Array"].Children.Count == 0
                                           ? 0 : mr["m_Materials"]["Array"].Children[0]["m_PathID"].AsLong);
        }

        // ---------------------------------------------------------------- internals

        /// <summary>
        /// One new object of <paramref name="cls"/> at <paramref name="pathId"/>, built from the
        /// EMPTY class-database template. internal because <see cref="SkinFields"/> (U5) creates the
        /// same six object kinds plus a Mesh and a SkinnedMeshRenderer, and a second copy of this
        /// four-line sequence is exactly how the two would drift apart.
        /// </summary>
        internal static void Create(AssetsFile afile, ClassDatabaseFile cldb, long pathId, AssetClassID cls,
                                    Action<AssetTypeValueField> fill)
        {
            // RECIPES 2, the same order BundleBaker.Add uses: AssetFileInfo.Create registers the
            // TypeTreeType and MUST run before the value field is built.
            AssetFileInfo info = AssetFileInfo.Create(afile, pathId, (int)cls, cldb, false);
            AssetTypeTemplateField tf = new AssetTypeTemplateField();
            tf.FromClassDatabase(cldb, cldb.FindAssetClassByID((int)cls), false);
            AssetTypeValueField bf = ValueBuilder.DefaultValueFieldFromTemplate(tf);
            fill(bf);
            info.SetNewData(bf);
            afile.Metadata.AddAssetInfo(info);
        }

        internal static void FillGameObject(AssetTypeValueField go, string name)
        {
            go["m_Name"].AsString = name;
            go["m_Layer"].AsUInt = 0;
            go["m_Tag"].AsUShort = 0;
            go["m_IsActive"].AsBool = true;   // default false = a hierarchy that loads and never renders
        }

        internal static void FillTransform(AssetTypeValueField tf, long gameObjectPathId, float x, float y, float z)
        {
            Pptr(tf["m_GameObject"], gameObjectPathId);
            // Identity, not the all-zero default: a zero quaternion is not a rotation.
            AssetTypeValueField q = tf["m_LocalRotation"];
            q["x"].AsFloat = 0f; q["y"].AsFloat = 0f; q["z"].AsFloat = 0f; q["w"].AsFloat = 1f;
            Vector3(tf["m_LocalPosition"], x, y, z);
            Vector3(tf["m_LocalScale"], 1f, 1f, 1f);
        }

        internal static void AddComponent(AssetTypeValueField go, long componentPathId)
        {
            AssetTypeValueField arr = go["m_Component"]["Array"];
            AssetTypeValueField pair = ValueBuilder.DefaultValueFieldFromArrayTemplate(arr);
            Pptr(pair["component"], componentPathId);
            arr.Children.Add(pair);
        }

        internal static void Pptr(AssetTypeValueField p, long pathId)
        {
            p["m_FileID"].AsInt = 0;      // 0 = this same serialized file
            p["m_PathID"].AsLong = pathId;
        }

        internal static void Vector3(AssetTypeValueField v, float x, float y, float z)
        {
            v["x"].AsFloat = x; v["y"].AsFloat = y; v["z"].AsFloat = z;
        }

        internal static void Vector4(AssetTypeValueField v)
        {
            v["x"].AsFloat = 1f; v["y"].AsFloat = 1f; v["z"].AsFloat = 0f; v["w"].AsFloat = 0f;
        }

        internal static AssetTypeValueField FindGameObject(AssetsManager m, AssetsFileInstance af, string name)
        {
            foreach (AssetFileInfo i in af.file.Metadata.GetAssetsOfType(AssetClassID.GameObject))
            {
                AssetTypeValueField go = m.GetBaseField(af, i);
                if (go["m_Name"].AsString == name) return go;
            }
            return null;
        }

        internal static AssetTypeValueField Component(AssetsManager m, AssetsFileInstance af,
                                                     AssetTypeValueField go, AssetClassID cls)
        {
            return Get(m, af, ComponentId(af, go, cls));
        }

        /// <summary>The pathId of the one component of that class on a GameObject, or 0.</summary>
        internal static long ComponentId(AssetsFileInstance af, AssetTypeValueField go, AssetClassID cls)
        {
            foreach (AssetTypeValueField pair in go["m_Component"]["Array"].Children)
            {
                long pid = pair["component"]["m_PathID"].AsLong;
                AssetFileInfo i = af.file.Metadata.GetAssetInfo(pid);
                if (i != null && i.TypeId == (int)cls) return pid;
            }
            return 0;
        }

        internal static AssetTypeValueField Get(AssetsManager m, AssetsFileInstance af, long pathId)
        {
            AssetFileInfo i = pathId == 0 ? null : af.file.Metadata.GetAssetInfo(pathId);
            return i == null ? null : m.GetBaseField(af, i);
        }

        internal static string Name(AssetsManager m, AssetsFileInstance af, long pathId)
        {
            AssetTypeValueField f = Get(m, af, pathId);
            return f == null || f["m_Name"].IsDummy ? "(unresolved:" + pathId + ")" : "'" + f["m_Name"].AsString + "'";
        }

        internal static string V(AssetTypeValueField v)
        {
            // InvariantCulture: this line is machine-compared and a ru-RU machine writes 0,5 for 0.5
            // (the trap MeshFields.V and ReadMaterialProperties both document).
            return v["x"].AsFloat.ToString("0.###", CultureInfo.InvariantCulture) + "," +
                   v["y"].AsFloat.ToString("0.###", CultureInfo.InvariantCulture) + "," +
                   v["z"].AsFloat.ToString("0.###", CultureInfo.InvariantCulture);
        }
    }
}
