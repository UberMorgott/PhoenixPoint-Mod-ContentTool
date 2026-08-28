using System;
using System.Collections.Generic;
using System.Globalization;

namespace Morgott.ContentTool.Import
{
    /// <summary>One texture-typed shader property. <see cref="File"/> is empty when the slot is unassigned.</summary>
    internal sealed class MaterialTexture
    {
        internal string Property = "";
        internal string Texture = "";
        /// <summary>Path of the dumped PNG, relative to the mesh dump root, always with '/'.</summary>
        internal string File = "";
        internal float ScaleX = 1f;
        internal float ScaleY = 1f;
        internal float OffsetX;
        internal float OffsetY;
    }

    /// <summary>One numeric shader property. <see cref="Value"/> holds 1 float or 4.</summary>
    internal sealed class MaterialValue
    {
        internal string Property = "";
        internal float[] Value = new float[0];
    }

    /// <summary>
    /// Unity's truth about one material, free of UnityEngine types so the codec can be exercised
    /// offline. Every property the shader declares is recorded, so nothing is silently discarded.
    /// </summary>
    internal sealed class MaterialSnapshot
    {
        internal string Name = "material";
        internal string Shader = "";
        /// <summary>-1 is Unity's own "take it from the shader", which is what an absent key means.</summary>
        internal int RenderQueue = -1;
        /// <summary>The active SubShader's `RenderType` tag, "" when the shader declares none.</summary>
        internal string RenderType = "";
        internal readonly List<string> Keywords = new List<string>();
        /// <summary>
        /// Whether the file said anything about keywords. An empty list is a real value — a material
        /// with no keywords — so it cannot be told from an absent key by the list alone, and applying
        /// the absent case would switch off _NORMALMAP and leave the model flat with nothing said.
        /// </summary>
        internal bool HasKeywords;
        internal readonly List<MaterialTexture> Textures = new List<MaterialTexture>();
        internal readonly List<MaterialValue> Colors = new List<MaterialValue>();
        internal readonly List<MaterialValue> Floats = new List<MaterialValue>();
        internal readonly List<MaterialValue> Vectors = new List<MaterialValue>();
    }

    /// <summary>
    /// Canonical material sidecar plus the Unity -> glTF slot mapping.
    ///
    /// The mapping is NOT a fixed schema. Phoenix Point's character shaders live in the shipped
    /// asset bundles, not in Assembly-CSharp, so their property names cannot be read offline. What
    /// the decompile does prove is that the names the engine code itself uses are the Unity Standard
    /// ones (`_MainTex`, `_BumpMap`, `_EmissionMap`, `_EmissionColor`, `_MetallicGlossMap`,
    /// `_SpecGlossMap`, `_Color`, `_Cutoff`, `_Glossiness`, `_Metallic`; see
    /// Assembly-CSharp/src/**/*.cs). So each slot takes an ordered list of known names first and,
    /// only when none of them exists on the material, falls back to a substring hint over whatever
    /// the shader actually declares. A shader that matches neither still exports every property in
    /// the sidecar; it just arrives in Blender without that map wired.
    /// </summary>
    internal static class MaterialCodec
    {
        private static readonly string[] BaseColorMaps =
            { "_MainTex", "_BaseMap", "_BaseColorMap", "_BaseTex", "_AlbedoMap", "_Albedo", "_DiffuseMap", "_Diffuse" };
        private static readonly string[] BaseColorHints = { "albedo", "basecolor", "basemap", "diffuse", "maintex" };

        private static readonly string[] NormalMaps = { "_BumpMap", "_NormalMap", "_NormalTex", "_Normal" };
        private static readonly string[] NormalHints = { "normalmap", "bumpmap", "normaltex", "tangentmap" };

        private static readonly string[] EmissiveMaps = { "_EmissionMap", "_EmissiveMap", "_Emission", "_Emissive" };
        private static readonly string[] EmissiveHints = { "emissionmap", "emissivemap", "emission", "emissive" };

        private static readonly string[] OcclusionMaps = { "_OcclusionMap", "_AOMap", "_AmbientOcclusionMap", "_OcclusionTex" };
        private static readonly string[] OcclusionHints = { "occlusionmap", "ambientocclusion", "occlusion" };

        /// <summary>Hint-pass poison: a secondary/packed map must never be promoted to a primary slot.</summary>
        private static readonly string[] NotPrimary =
            { "detail", "secondary", "mask", "blur", "cube", "lightmap", "specgloss", "metallic", "gloss", "smooth", "rough", "flow", "noise" };

        private static readonly string[] BaseColorFactors = { "_Color", "_BaseColor", "_MainColor", "_TintColor", "_Tint" };
        private static readonly string[] EmissiveFactors = { "_EmissionColor", "_EmissiveColor" };
        private static readonly string[] MetallicFactors = { "_Metallic", "_Metalness" };
        private static readonly string[] SmoothnessFactors = { "_Glossiness", "_Smoothness" };
        private static readonly string[] RoughnessFactors = { "_Roughness" };
        private static readonly string[] CutoffFactors = { "_Cutoff", "_AlphaCutoff", "_AlphaClip" };
        private static readonly string[] CullFactors = { "_Cull", "_CullMode" };

        /// <summary>The material sidecar: canonical JSON, keys alphabetical, lists sorted by property.</summary>
        internal static string Write(MaterialSnapshot material)
        {
            if (material == null) throw new ArgumentNullException(nameof(material));
            var json = new JsonWriter();
            json.Obj();
            Values(json, "colors", material.Colors);
            Values(json, "floats", material.Floats);
            json.Key("keywords").Arr();
            foreach (string keyword in Sorted(material.Keywords)) json.Val(keyword);
            json.EndArr();
            json.Key("materialName").Val(material.Name ?? "");
            json.Key("renderQueue").Val(material.RenderQueue);
            json.Key("renderType").Val(material.RenderType ?? "");
            json.Key("shaderName").Val(material.Shader ?? "");
            json.Key("textures").Arr();
            foreach (MaterialTexture texture in Sorted(material.Textures))
            {
                json.Obj();
                json.Key("file").Val(texture.File ?? "");
                json.Key("offset").Vals(new[] { texture.OffsetX, texture.OffsetY });
                json.Key("property").Val(texture.Property ?? "");
                json.Key("scale").Vals(new[] { texture.ScaleX, texture.ScaleY });
                json.Key("texture").Val(texture.Texture ?? "");
                json.EndObj();
            }
            json.EndArr();
            Values(json, "vectors", material.Vectors);
            json.Key("version").Val(1);
            json.EndObj();
            return json.ToString();
        }

        // ------------------------------------------------------------------ reading

        /// <summary>
        /// Hostile-input ceiling on one sidecar. What the dumper itself writes is a few kilobytes even
        /// for a shader declaring a hundred properties.
        /// </summary>
        internal const int MaxTextBytes = 4 * 1024 * 1024;
        private const int MaxDepth = 32;
        private const int MaxEntries = 4096;
        private const int MaxNameLength = 512;
        /// <summary>Unity's own buckets run Background 1000 .. Overlay 4000; -1 means "whatever the shader says".</summary>
        private const int MinQueue = -1;
        private const int MaxQueue = 10000;

        /// <summary>
        /// Reads a player-edited <c>.mat.json</c> back into the same snapshot <see cref="Write"/>
        /// produces. This is a TRUST BOUNDARY: the text is whatever a player's editor left behind, so
        /// every count, length and number is bounded here and nothing is sized to a figure the file
        /// merely claims. Every refusal names the cause and the fix; the caller prefixes the path.
        /// </summary>
        internal static MaterialSnapshot Read(string text)
        {
            if (text == null)
                throw Bad("there is nothing to read; re-dump the model with rr_dumpmeshes and copy the .mat.json again");
            if (text.Length > MaxTextBytes)
                throw Bad("the file is " + Count(text.Length) + " characters, past the " + Count(MaxTextBytes) +
                    " limit this mod will read; it is meant to be a dumped .mat.json, so copy one from MeshesDump\\materials\\ again");
            var root = Json.Parse(text, MaxDepth) as Dictionary<string, object>;
            if (root == null)
                throw Bad("the file's top level is not a material description; copy a .mat.json from MeshesDump\\materials\\ and edit that");

            int version = Whole(root, "version", 1);
            if (version != 1)
                throw Bad("the file declares material format version " + Count(version) +
                    " and this mod reads version 1; re-dump the model with rr_dumpmeshes and edit the new .mat.json");

            var snapshot = new MaterialSnapshot
            {
                Name = Word(root, "materialName", true),
                Shader = Word(root, "shaderName", true),
                // Absent means "keep the shader's own", which is exactly Unity's -1 — and what this
                // file's own refusal below has always promised. It used to default to 0, which is
                // ahead of Background and drew a hand-written minimal .mat.json behind the world.
                RenderQueue = Whole(root, "renderQueue", -1),
                // Absent in every sidecar dumped before this key existed, and "" is exactly "no tag":
                // AlphaMode then falls through to the render queue, which is what those files got.
                RenderType = Word(root, "renderType", false),
            };
            if (snapshot.RenderQueue < MinQueue || snapshot.RenderQueue > MaxQueue)
                throw Bad("'renderQueue' is " + Count(snapshot.RenderQueue) + ", outside the " + Count(MinQueue) + " to " +
                    Count(MaxQueue) + " range Unity sorts with; put the dumped number back or delete the line to keep the shader's own");

            // One namespace for every property: a shader declares each name once, so the same name in
            // two lists would leave the winner up to list order.
            var seen = new HashSet<string>(StringComparer.Ordinal);
            Values(root, "colors", snapshot.Colors, 3, 4, seen);
            Values(root, "floats", snapshot.Floats, 1, 1, seen);
            Values(root, "vectors", snapshot.Vectors, 4, 4, seen);
            Keywords(root, snapshot);
            Textures(root, snapshot, seen);
            return snapshot;
        }

        private static void Values(Dictionary<string, object> root, string key, List<MaterialValue> into,
            int min, int max, HashSet<string> seen)
        {
            foreach (Dictionary<string, object> entry in Entries(root, key))
            {
                string property = Property(entry, key, seen);
                List<object> numbers = Entry(entry, "value") as List<object>;
                if (numbers == null)
                    throw Bad("'" + property + "' in '" + key + "' has no list of numbers; put back the dumped \"value\": [ ... ] line");
                if (numbers.Count < min || numbers.Count > max)
                    throw Bad("'" + property + "' in '" + key + "' has " + Count(numbers.Count) + " numbers and this mod expects " +
                        (min == max ? Count(min) : Count(min) + " to " + Count(max)) +
                        "; put the dumped numbers back without adding or removing any");
                var value = new float[numbers.Count];
                for (int i = 0; i < numbers.Count; i++) value[i] = Single(numbers[i], property, key);
                into.Add(new MaterialValue { Property = property, Value = value });
            }
        }

        private static void Keywords(Dictionary<string, object> root, MaterialSnapshot snapshot)
        {
            List<object> list = Entry(root, "keywords") as List<object>;
            if (list == null)
            {
                if (Entry(root, "keywords") != null)
                    throw Bad("'keywords' is not a list of names; put back the dumped \"keywords\": [ ... ] line");
                return;
            }
            snapshot.HasKeywords = true;
            if (list.Count > MaxEntries)
                throw Bad("'keywords' holds " + Count(list.Count) + " names, past the " + Count(MaxEntries) +
                    " limit; a real material has a handful, so copy a dumped .mat.json again");
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (object item in list)
            {
                var keyword = item as string;
                if (string.IsNullOrEmpty(keyword) || keyword.Length > MaxNameLength)
                    throw Bad("'keywords' holds an entry that is not a shader keyword name; every entry must be a non-empty name of at most " +
                        Count(MaxNameLength) + " characters, so copy a dumped .mat.json again");
                if (!seen.Add(keyword))
                    throw Bad("'keywords' names '" + keyword + "' twice; delete the duplicate line");
                snapshot.Keywords.Add(keyword);
            }
        }

        private static void Textures(Dictionary<string, object> root, MaterialSnapshot snapshot, HashSet<string> seen)
        {
            foreach (Dictionary<string, object> entry in Entries(root, "textures"))
            {
                string property = Property(entry, "textures", seen);
                var texture = new MaterialTexture
                {
                    Property = property,
                    Texture = Word(entry, "texture", false),
                    File = RelativeFile(Word(entry, "file", false), property),
                };
                Pair(entry, "scale", property, 1f, out texture.ScaleX, out texture.ScaleY);
                Pair(entry, "offset", property, 0f, out texture.OffsetX, out texture.OffsetY);
                snapshot.Textures.Add(texture);
            }
        }

        /// <summary>
        /// The image path as it is allowed to be: relative, forward slashes, no drive, no "..". A
        /// dumped sidecar says "materials/X.png" and the importer joins that onto the mod's own
        /// Meshes\ folder, so anything that could climb out of it is refused rather than resolved.
        /// </summary>
        private static string RelativeFile(string value, string property)
        {
            if (string.IsNullOrEmpty(value)) return "";
            if (value.Length > MaxNameLength)
                throw Bad("the image named by '" + property + "' has a path of " + Count(value.Length) +
                    " characters, past the " + Count(MaxNameLength) + " limit; keep the dumped \"materials/<name>.png\" path");
            string path = value.Replace('\\', '/');
            if (path.IndexOf(':') >= 0 || path.StartsWith("/", StringComparison.Ordinal))
                throw Bad("the image named by '" + property + "' is '" + value +
                    "', which points outside this mod's folder; the path must be relative, like \"materials/<name>.png\"");
            foreach (string part in path.Split('/'))
                if (part == "..")
                    throw Bad("the image named by '" + property + "' is '" + value +
                        "', and '..' would climb out of this mod's folder; keep the png beside the .mat.json and write \"materials/<name>.png\"");
            foreach (char c in path)
                if (c < ' ' || c == '"' || c == '*' || c == '?' || c == '<' || c == '>' || c == '|')
                    throw Bad("the image named by '" + property + "' is '" + value +
                        "', which is not a usable file name; rename the png to plain letters and digits and write \"materials/<name>.png\"");
            return path;
        }

        private static void Pair(Dictionary<string, object> entry, string key, string property, float fallback,
            out float x, out float y)
        {
            object value = Entry(entry, key);
            if (value == null) { x = y = fallback; return; }
            var numbers = value as List<object>;
            if (numbers == null || numbers.Count != 2)
                throw Bad("'" + key + "' of '" + property + "' is not two numbers; put back the dumped \"" + key +
                    "\": [ x, y ] line or delete it to keep the shader's own");
            x = Single(numbers[0], property, key);
            y = Single(numbers[1], property, key);
        }

        private static IEnumerable<Dictionary<string, object>> Entries(Dictionary<string, object> root, string key)
        {
            object value = Entry(root, key);
            if (value == null) yield break;
            var list = value as List<object>;
            if (list == null)
                throw Bad("'" + key + "' is not a list; put back the dumped \"" + key + "\": [ ... ] line");
            if (list.Count > MaxEntries)
                throw Bad("'" + key + "' holds " + Count(list.Count) + " entries, past the " + Count(MaxEntries) +
                    " limit; a real shader declares far fewer, so copy a dumped .mat.json again");
            foreach (object item in list)
            {
                var entry = item as Dictionary<string, object>;
                if (entry == null)
                    throw Bad("'" + key + "' holds an entry that is not a property description; copy a dumped .mat.json again");
                yield return entry;
            }
        }

        private static string Property(Dictionary<string, object> entry, string key, HashSet<string> seen)
        {
            string property = Word(entry, "property", true);
            if (!seen.Add(property))
                throw Bad("'" + property + "' is described twice; a shader declares each property once, so delete the duplicate entry");
            return property;
        }

        private static object Entry(Dictionary<string, object> where, string key)
        {
            object value;
            return where != null && where.TryGetValue(key, out value) ? value : null;
        }

        private static string Word(Dictionary<string, object> where, string key, bool required)
        {
            object value = Entry(where, key);
            if (value == null)
            {
                if (!required) return "";
                throw Bad("'" + key + "' is missing; every dumped .mat.json carries it, so copy one from MeshesDump\\materials\\ and edit that");
            }
            var text = value as string;
            if (text == null || text.Length > MaxNameLength)
                throw Bad("'" + key + "' is not a name of at most " + Count(MaxNameLength) +
                    " characters; put the dumped text back between quotes");
            if (required && text.Length == 0)
                throw Bad("'" + key + "' is empty; put the dumped name back between quotes");
            return text;
        }

        private static int Whole(Dictionary<string, object> where, string key, int fallback)
        {
            object value = Entry(where, key);
            if (value == null) return fallback;
            if (!(value is double number) || number != Math.Floor(number) || number < int.MinValue || number > int.MaxValue)
                throw Bad("'" + key + "' is not a whole number; put the dumped number back");
            return (int)number;
        }

        /// <summary>Json already refuses NaN and infinity, so only the float range is left to check.</summary>
        private static float Single(object value, string property, string key)
        {
            if (!(value is double number))
                throw Bad("'" + property + "' in '" + key + "' holds something that is not a number; put the dumped numbers back");
            if (number < -float.MaxValue || number > float.MaxValue)
                throw Bad("'" + property + "' in '" + key + "' holds " +
                    number.ToString("R", CultureInfo.InvariantCulture) + ", which is too large for the game to use; put the dumped numbers back");
            return (float)number;
        }

        // ------------------------------------------------------------------ apply-time refusals
        // The two checks that decide whether a sidecar may touch a live material. They live here,
        // free of UnityEngine, so the exact words a player reads can be asserted offline.

        /// <summary>
        /// Null when the sidecar was dumped from the shader the material still uses. Changing the
        /// shader is out of scope — a shader brings its own property set, and a .mat.json written
        /// against a different one would be applied half-blind.
        /// </summary>
        internal static string ShaderRefusal(string file, string material, string dumped, string live)
        {
            if (string.Equals(dumped ?? "", live ?? "", StringComparison.Ordinal)) return null;
            return "'" + file + "' was dumped from the shader '" + dumped + "' but '" + material +
                "' now draws with '" + live + "'; this mod never changes a shader, so run rr_dumpmeshes again " +
                "and edit the .mat.json it writes";
        }

        /// <summary>A property the live shader does not declare. Never a silent no-op: it names both.</summary>
        internal static string PropertyRefusal(string file, string property, string shader) =>
            "'" + file + "' sets '" + property + "', which the shader '" + shader +
            "' does not have; delete that entry from the .mat.json, or run rr_dumpmeshes again and edit the file it writes";

        private static string Count(int value) => value.ToString(CultureInfo.InvariantCulture);

        private static FormatException Bad(string message) => new FormatException(message);

        // ------------------------------------------------------------------ glTF slot mapping

        internal static MaterialTexture BaseColor(MaterialSnapshot m) => Map(m, BaseColorMaps, BaseColorHints);
        internal static MaterialTexture Normal(MaterialSnapshot m) => Map(m, NormalMaps, NormalHints);
        internal static MaterialTexture Emissive(MaterialSnapshot m) => Map(m, EmissiveMaps, EmissiveHints);
        internal static MaterialTexture Occlusion(MaterialSnapshot m) => Map(m, OcclusionMaps, OcclusionHints);

        /// <summary>glTF wants linear RGBA; Unity's tint colors are already stored linearly at runtime.</summary>
        internal static float[] BaseColorFactor(MaterialSnapshot m) =>
            Clamped(Color(m, BaseColorFactors) ?? new[] { 1f, 1f, 1f, 1f }, 4);

        internal static float[] EmissiveFactor(MaterialSnapshot m) =>
            Clamped(Color(m, EmissiveFactors) ?? new[] { 0f, 0f, 0f }, 3);

        internal static float MetallicFactor(MaterialSnapshot m) => Clamp01(Scalar(m, MetallicFactors) ?? 0f);

        /// <summary>Unity stores smoothness; glTF stores roughness. One is 1 minus the other.</summary>
        internal static float RoughnessFactor(MaterialSnapshot m)
        {
            float? roughness = Scalar(m, RoughnessFactors);
            if (roughness.HasValue) return Clamp01(roughness.Value);
            float? smoothness = Scalar(m, SmoothnessFactors);
            return smoothness.HasValue ? Clamp01(1f - smoothness.Value) : 1f;
        }

        /// <summary>`_Cull` is Unity's CullMode: 0 = Off, so the material renders both faces.</summary>
        internal static bool DoubleSided(MaterialSnapshot m)
        {
            float? cull = Scalar(m, CullFactors);
            return cull.HasValue && cull.Value < 0.5f;
        }

        /// <summary>
        /// Three signals, most specific first.
        ///
        /// 1. Keywords. Unity Standard sets `_ALPHATEST_ON` / `_ALPHABLEND_ON` / `_ALPHAPREMULTIPLY_ON`
        ///    for its rendering modes. They are per-MATERIAL state, so they beat anything the shader says.
        /// 2. The `RenderType` SubShader tag. This game's shaders (`_PX_GEO/*`, `_PX_CHR/*`) hardcode
        ///    Blend/ZWrite in ShaderLab and expose no `_Mode`/`_SrcBlend`/`_ZWrite` property, so the tag
        ///    is the only place they still declare their alpha character. Values are Unity's documented
        ///    built-in set (Manual/SL-ShaderReplacement): Opaque, Transparent, TransparentCutout,
        ///    Background, Overlay, Tree*, Grass*. Only the first three say anything about alpha; the
        ///    rest are shader-replacement categories, so they are deliberately left to the queue —
        ///    and the alpha-tested vegetation ones sort into AlphaTest anyway, which lands on MASK.
        /// 3. The render queue, last resort: Unity's AlphaTest bucket starts at 2450, Transparent at 3000.
        ///    It is only a sorting choice — a blended backdrop authored at Geometry-1 (1999) reads as
        ///    opaque here — which is why the tag outranks it in both directions.
        /// </summary>
        internal static string AlphaMode(MaterialSnapshot m)
        {
            if (HasKeyword(m, "_ALPHABLEND_ON") || HasKeyword(m, "_ALPHAPREMULTIPLY_ON")) return "BLEND";
            if (HasKeyword(m, "_ALPHATEST_ON")) return "MASK";
            string tag = m.RenderType ?? "";
            if (string.Equals(tag, "Transparent", StringComparison.OrdinalIgnoreCase)) return "BLEND";
            if (string.Equals(tag, "TransparentCutout", StringComparison.OrdinalIgnoreCase)) return "MASK";
            if (string.Equals(tag, "Opaque", StringComparison.OrdinalIgnoreCase)) return "OPAQUE";
            if (m.RenderQueue >= 3000) return "BLEND";
            return m.RenderQueue >= 2450 ? "MASK" : "OPAQUE";
        }

        internal static float AlphaCutoff(MaterialSnapshot m) => Clamp01(Scalar(m, CutoffFactors) ?? 0.5f);

        // ------------------------------------------------------------------ internals

        private static void Values(JsonWriter json, string key, List<MaterialValue> values)
        {
            json.Key(key).Arr();
            foreach (MaterialValue value in Sorted(values))
            {
                json.Obj();
                json.Key("property").Val(value.Property ?? "");
                json.Key("value").Vals(value.Value ?? new float[0]);
                json.EndObj();
            }
            json.EndArr();
        }

        private static List<string> Sorted(List<string> values)
        {
            var copy = new List<string>(values ?? new List<string>());
            copy.Sort(StringComparer.Ordinal);
            return copy;
        }

        private static List<MaterialValue> Sorted(List<MaterialValue> values)
        {
            var copy = new List<MaterialValue>(values ?? new List<MaterialValue>());
            copy.Sort((a, b) => string.CompareOrdinal(a.Property ?? "", b.Property ?? ""));
            return copy;
        }

        private static List<MaterialTexture> Sorted(List<MaterialTexture> values)
        {
            var copy = new List<MaterialTexture>(values ?? new List<MaterialTexture>());
            copy.Sort((a, b) => string.CompareOrdinal(a.Property ?? "", b.Property ?? ""));
            return copy;
        }

        /// <summary>Known names first, then a hint over what the shader really declares, never a guess.</summary>
        private static MaterialTexture Map(MaterialSnapshot material, string[] known, string[] hints)
        {
            if (material == null) return null;
            foreach (string name in known)
                foreach (MaterialTexture texture in material.Textures)
                    if (Assigned(texture) && string.Equals(texture.Property, name, StringComparison.OrdinalIgnoreCase))
                        return texture;
            MaterialTexture best = null;
            foreach (MaterialTexture texture in material.Textures)
            {
                if (!Assigned(texture)) continue;
                string property = texture.Property.ToLowerInvariant();
                bool poisoned = false;
                foreach (string bad in NotPrimary) if (property.IndexOf(bad, StringComparison.Ordinal) >= 0) { poisoned = true; break; }
                if (poisoned) continue;
                foreach (string hint in hints)
                    if (property.IndexOf(hint, StringComparison.Ordinal) >= 0)
                    {
                        // Deterministic when several hints match: the ordinally first property wins.
                        if (best == null || string.CompareOrdinal(texture.Property, best.Property) < 0) best = texture;
                        break;
                    }
            }
            return best;
        }

        private static bool Assigned(MaterialTexture texture) =>
            texture != null && !string.IsNullOrEmpty(texture.Property) && !string.IsNullOrEmpty(texture.File);

        private static bool HasKeyword(MaterialSnapshot material, string keyword)
        {
            foreach (string value in material.Keywords)
                if (string.Equals(value, keyword, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        private static float[] Color(MaterialSnapshot material, string[] names)
        {
            foreach (string name in names)
                foreach (MaterialValue value in material.Colors)
                    if (string.Equals(value.Property, name, StringComparison.OrdinalIgnoreCase) &&
                        value.Value != null && value.Value.Length >= 3) return value.Value;
            return null;
        }

        private static float? Scalar(MaterialSnapshot material, string[] names)
        {
            foreach (string name in names)
                foreach (MaterialValue value in material.Floats)
                    if (string.Equals(value.Property, name, StringComparison.OrdinalIgnoreCase) &&
                        value.Value != null && value.Value.Length >= 1) return value.Value[0];
            return null;
        }

        /// <summary>glTF factors are JSON numbers in [0,1]; a non-finite or out-of-range one is invalid.</summary>
        private static float[] Clamped(float[] source, int count)
        {
            var result = new float[count];
            for (int i = 0; i < count; i++) result[i] = i < source.Length ? Clamp01(source[i]) : 1f;
            return result;
        }

        private static float Clamp01(float value)
        {
            if (float.IsNaN(value)) return 0f;
            return value < 0f ? 0f : value > 1f ? 1f : value;
        }
    }
}
