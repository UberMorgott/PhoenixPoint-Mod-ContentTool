using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using AssetsTools.NET;
using AssetsTools.NET.Extra;

namespace Morgott.ContentTool.Bake
{
    /// <summary>
    /// The serialized Material, written and read with no UnityEngine type - the same arrangement
    /// MeshFields / SkinFields / ClipFields use, so what BundleBaker.AddMaterial produces can be
    /// proven offline instead of costing a game launch.
    ///
    /// Why it exists at all: a renderer whose material carries no paint draws the SHADER's defaults,
    /// which for Standard is opaque white. That is not a missing material and not a missing shader -
    /// it is a material nobody put a colour in - and it is exactly what the imported spider showed.
    /// </summary>
    internal static class MaterialFields
    {
        /// <summary>Unity Standard's albedo tint. Multiplies _MainTex; alone when there is no texture.</summary>
        internal const string ColorProperty = "_Color";

        // E3 / U3d's two measured constants, moved here from BakeSelfCheck so the OFFLINE arm and the
        // in-game one cannot drift apart. Dumped with AssetsTools.NET on 2026-08-12 out of
        // defaultlocalgroup_unitybuiltinshaders.bundle, whose serialized file is this CAB - already an
        // external of the mutoid bundle every bake clones, so the fileID is read off the file.
        internal const string BuiltinShaderCab = "cab-207b1100b7c0eac21654e77dc25fa206";
        internal const long StandardShaderPathId = 952725256833404699L;
        /// <summary>The shipped file that CAB lives in - the one place the shader itself can be READ.</summary>
        internal const string BuiltinShaderBundle = "defaultlocalgroup_unitybuiltinshaders.bundle";

        /// <summary>
        /// The property names a serialized Shader DECLARES - m_ParsedForm.m_PropInfo.m_Props, the same
        /// table the engine builds its property sheet from.
        ///
        /// Why this is the oracle and a chosen name is not: a key the shader does NOT declare is not an
        /// error. Unity keeps it in the material's property block, never binds it, and the shader draws
        /// its own default - which for Standard's albedo is opaque WHITE. So "a key called _Color is
        /// present" proves nothing at all; "the referenced shader declares the key we wrote" is the
        /// thing the game actually reads. Measured off <see cref="BuiltinShaderBundle"/> at
        /// <see cref="StandardShaderPathId"/>: 27 properties, first of them _Color / "Color",
        /// then _MainTex, _Cutoff, _Glossiness, _Metallic ... - Unity Standard's own set. The same
        /// name a SHIPPED Phoenix Point material at that identical shader PPtr carries:
        /// 'PlaceholderWeaponsMutoid' in mutoid_assets_all.bundle writes _Color, _MainTex, _Mode.
        /// </summary>
        internal static HashSet<string> DeclaredProperties(AssetTypeValueField shader)
        {
            if (shader == null) throw new ArgumentNullException(nameof(shader));
            HashSet<string> names = new HashSet<string>(StringComparer.Ordinal);
            foreach (AssetTypeValueField p in shader["m_ParsedForm"]["m_PropInfo"]["m_Props"]["Array"].Children)
                names.Add(p["m_Name"].AsString);
            return names;
        }

        /// <summary>
        /// Every field <see cref="BundleBaker.AddMaterial"/> writes except m_Name. Texture and colour
        /// PPtrs are m_FileID 0 - into this same serialized file - because the whole point of baking a
        /// texture beside the material is that the reference never leaves the bundle. The SHADER is the
        /// one reference that is external, and its fileID is read off the file's own externals table.
        /// </summary>
        /// <summary>
        /// The keyword every Standard-shader material needs before its <c>_EmissionColor</c> does
        /// anything at all. Setting the colour without it is the silent failure this whole parameter
        /// exists to prevent: the property is stored, the shader never reads it, and the model bakes
        /// looking exactly like one that was never given a glow.
        ///
        /// MEASURED off the shipped game rather than assumed - <c>WPN_ANU_Melee_Blade_V01</c>,
        /// <c>_Hammer_V01</c> and <c>_Mace_V01</c> in <c>an_equipment_assets_all.bundle</c> all carry
        /// <c>m_ShaderKeywords = "_EMISSION _METALLICGLOSSMAP _NORMALMAP"</c>, space separated, with
        /// <c>m_CustomRenderQueue = -1</c> and an opaque blend (_Mode 0, _SrcBlend 1, _DstBlend 0,
        /// _ZWrite 1). So an emissive material in this game is an ORDINARY OPAQUE one that also
        /// glows; additive transparency is a separate choice and rides in through <paramref
        /// name="floats"/>, which already existed.
        /// </summary>
        internal const string EmissionKeyword = "_EMISSION";

        /// <summary>Unity's own property name for the colour that keyword lights up.</summary>
        internal const string EmissionColorProperty = "_EmissionColor";

        internal static void Fill(AssetTypeValueField mat, int shaderFileId, long shaderPathId,
                                  IEnumerable<KeyValuePair<string, long>> textures,
                                  IEnumerable<KeyValuePair<string, float>> floats,
                                  IEnumerable<KeyValuePair<string, float[]>> colors,
                                  string keywords = null, int renderQueue = -1)
        {
            if (mat == null) throw new ArgumentNullException(nameof(mat));
            mat["m_Shader"]["m_FileID"].AsInt = shaderFileId;
            mat["m_Shader"]["m_PathID"].AsLong = shaderPathId;
            // -1 is "take it from the shader", which is what every shipped material above carries and
            // what an absent value has always meant here.
            mat["m_CustomRenderQueue"].AsInt = renderQueue;
            if (!string.IsNullOrEmpty(keywords))
            {
                mat["m_ShaderKeywords"].AsString = keywords;
                // MaterialGlobalIlluminationFlags. The shipped emissive weapons carry 0 (None): they
                // glow on screen without contributing to baked GI, which is what a hand-held object
                // in a game with no per-mission lightmap bake wants.
                if (mat["m_LightmapFlags"] != null) mat["m_LightmapFlags"].AsUInt = 0u;
            }
            AssetTypeValueField props = mat["m_SavedProperties"];
            if (textures != null)
                foreach (KeyValuePair<string, long> t in textures)
                {
                    AssetTypeValueField arr = props["m_TexEnvs"]["Array"];
                    AssetTypeValueField pair = ValueBuilder.DefaultValueFieldFromArrayTemplate(arr);
                    pair["first"].AsString = t.Key;
                    pair["second"]["m_Texture"]["m_FileID"].AsInt = 0;
                    pair["second"]["m_Texture"]["m_PathID"].AsLong = t.Value;
                    pair["second"]["m_Scale"]["x"].AsFloat = 1f;
                    pair["second"]["m_Scale"]["y"].AsFloat = 1f;
                    arr.Children.Add(pair);
                }
            if (floats != null)
                foreach (KeyValuePair<string, float> f in floats)
                {
                    AssetTypeValueField arr = props["m_Floats"]["Array"];
                    AssetTypeValueField pair = ValueBuilder.DefaultValueFieldFromArrayTemplate(arr);
                    pair["first"].AsString = f.Key;
                    pair["second"].AsFloat = f.Value;
                    arr.Children.Add(pair);
                }
            if (colors != null)
                foreach (KeyValuePair<string, float[]> c in colors)
                {
                    if (c.Value == null || c.Value.Length != 4)
                        throw new ArgumentException("material colour '" + c.Key + "' needs 4 components");
                    AssetTypeValueField arr = props["m_Colors"]["Array"];
                    AssetTypeValueField pair = ValueBuilder.DefaultValueFieldFromArrayTemplate(arr);
                    pair["first"].AsString = c.Key;
                    pair["second"]["r"].AsFloat = c.Value[0];
                    pair["second"]["g"].AsFloat = c.Value[1];
                    pair["second"]["b"].AsFloat = c.Value[2];
                    pair["second"]["a"].AsFloat = c.Value[3];
                    arr.Children.Add(pair);
                }
        }

        /// <summary>
        /// One Material's whole property block, off the FILE. The oracle for every material arm,
        /// because a Material whose m_Shader has not resolved exposes NO properties through the
        /// engine API - Material.GetTexture has no property sheet to look them up in, so the data is
        /// there and unreadable. InvariantCulture throughout: these lines are machine-compared, and a
        /// ru-RU machine writes 0,25 where the arm expects 0.25 (observed 2026-08-12 12:11:57).
        /// </summary>
        internal static string Summary(AssetTypeValueField mat)
        {
            StringBuilder sb = new StringBuilder("shader fileID=")
                .Append(mat["m_Shader"]["m_FileID"].AsInt).Append(" pathID=").Append(mat["m_Shader"]["m_PathID"].AsLong);
            foreach (AssetTypeValueField t in mat["m_SavedProperties"]["m_TexEnvs"]["Array"].Children)
                sb.Append(" | ").Append(t["first"].AsString).Append(" -> fileID=")
                  .Append(t["second"]["m_Texture"]["m_FileID"].AsInt).Append(" pathID=")
                  .Append(t["second"]["m_Texture"]["m_PathID"].AsLong);
            foreach (AssetTypeValueField f in mat["m_SavedProperties"]["m_Floats"]["Array"].Children)
                sb.Append(" | ").Append(f["first"].AsString).Append('=')
                  .Append(f["second"].AsFloat.ToString(CultureInfo.InvariantCulture));
            foreach (AssetTypeValueField c in mat["m_SavedProperties"]["m_Colors"]["Array"].Children)
                sb.Append(" | ").Append(c["first"].AsString).Append('=')
                  .Append(F(c["second"]["r"])).Append(',').Append(F(c["second"]["g"])).Append(',')
                  .Append(F(c["second"]["b"])).Append(',').Append(F(c["second"]["a"]));
            return sb.ToString();
        }

        private static string F(AssetTypeValueField v) => v.AsFloat.ToString("0.####", CultureInfo.InvariantCulture);

        /// <summary>
        /// glTF states baseColorFactor in LINEAR light; a Unity material serializes its colour the way
        /// the inspector shows it, sRGB-encoded, and the engine converts on upload. Without this the
        /// spider's 0.0418 grey is written as 0.0418 and reads as near-black - the same "looks broken"
        /// a white model does, one shade down.
        ///
        /// GROUNDED 2026-08-24, no longer an assumption: PlayerSettings.m_ActiveColorSpace in
        /// PhoenixPointWin64_Data\globalgamemanagers (pathID 1, Unity 2019.4.31f1) reads 1 = LINEAR.
        /// So the engine DOES convert material colours on upload and the encode belongs here.
        /// IEC 61966-2-1 transfer function.
        /// </summary>
        internal static float[] Srgb(float[] linear)
        {
            if (linear == null) return null;
            float[] c = new float[4];
            for (int i = 0; i < 4; i++)
            {
                float v = i < linear.Length ? linear[i] : 1f;
                if (v < 0f) v = 0f; else if (v > 1f) v = 1f;
                // Alpha is never encoded - it is coverage, not light.
                c[i] = i == 3 ? v
                     : v <= 0.0031308f ? v * 12.92f
                     : (float)(1.055 * Math.Pow(v, 1.0 / 2.4) - 0.055);
            }
            return c;
        }
    }
}
