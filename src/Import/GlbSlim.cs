using System.Collections.Generic;

namespace Morgott.ContentTool.Import
{
    /// <summary>
    /// Animation-clip census and trim for .glb files. Works on GlbDocument's parsed JSON, never on
    /// GlbReader's imported model - so it handles files GlbReader refuses.
    /// </summary>
    internal static class GlbSlim
    {
        /// <summary>One row per animation clip.</summary>
        internal sealed class ClipRow
        {
            internal int Index;
            internal string Name;
            internal int Channels;
            internal int Samplers;
            /// <summary>Sum of count * elementSize for every accessor this clip references.</summary>
            internal long AccessorBytes;
            /// <summary>byteLength of bufferViews owned ONLY by this clip's accessors (not shared
            /// with mesh/skin/image accessors or other clips). Zero when bufferViews are shared.</summary>
            internal long ExclusiveBytes;
            /// <summary>True when the clip name matches the mandatory action heuristic.</summary>
            internal bool Mandatory;
        }

        // ponytail: mandatory list is a heuristic; upgrade path = slice 1 PrototypeRecord.Variant[]
        // resolved clip catalogue when it ships.
        private static readonly string[] MandatoryTokens =
        {
            "idle", "walk", "run", "death", "attack", "hit", "aim", "fire", "reload",
            "turn", "stand", "crouch", "jump", "climb", "spawn"
        };

        /// <summary>Enumerate every animation clip in the document.</summary>
        internal static List<ClipRow> Census(GlbDocument doc)
        {
            var rows = new List<ClipRow>();
            List<object> animations = Arr(doc.Json, "animations");
            if (animations == null || animations.Count == 0) return rows;
            List<object> accessors = Arr(doc.Json, "accessors") ?? new List<object>();
            List<object> views = Arr(doc.Json, "bufferViews") ?? new List<object>();

            // Which clips reach which accessor. An accessor no clip reaches belongs to a mesh, a
            // skin or nothing, and either way it is not a clip's to free.
            var accessorClips = new Dictionary<int, HashSet<int>>();
            for (int i = 0; i < animations.Count; i++)
            {
                var row = new ClipRow { Index = i, Name = Str(Obj(animations[i]), "name") ?? "" };
                row.Mandatory = IsMandatory(row.Name);
                List<object> samplers = Arr(Obj(animations[i]), "samplers") ?? new List<object>();
                row.Samplers = samplers.Count;
                row.Channels = (Arr(Obj(animations[i]), "channels") ?? new List<object>()).Count;

                var mine = new HashSet<int>();
                foreach (object sampler in samplers)
                {
                    Add(mine, Int(Obj(sampler), "input", -1));
                    Add(mine, Int(Obj(sampler), "output", -1));
                }
                foreach (int index in mine)
                {
                    if (index < 0 || index >= accessors.Count) continue;
                    Dictionary<string, object> accessor = Obj(accessors[index]);
                    row.AccessorBytes += Int(accessor, "count", 0) * (long)ElementSize(accessor);
                    if (!accessorClips.TryGetValue(index, out HashSet<int> clips))
                        accessorClips[index] = clips = new HashSet<int>();
                    clips.Add(i);
                }
                rows.Add(row);
            }

            // Which clips reach which bufferView, and which views something OUTSIDE every clip
            // touches - a view an image or a mesh accessor also uses can never be exclusive.
            var viewClips = new Dictionary<int, HashSet<int>>();
            var outside = new HashSet<int>();
            for (int index = 0; index < accessors.Count; index++)
            {
                accessorClips.TryGetValue(index, out HashSet<int> clips);
                foreach (int view in AccessorViews(Obj(accessors[index])))
                {
                    if (clips == null) { outside.Add(view); continue; }
                    if (!viewClips.TryGetValue(view, out HashSet<int> owners))
                        viewClips[view] = owners = new HashSet<int>();
                    owners.UnionWith(clips);
                }
            }
            foreach (object image in Arr(doc.Json, "images") ?? new List<object>())
                Add(outside, Int(Obj(image), "bufferView", -1));

            foreach (KeyValuePair<int, HashSet<int>> pair in viewClips)
            {
                if (pair.Value.Count != 1 || outside.Contains(pair.Key)) continue;
                if (pair.Key < 0 || pair.Key >= views.Count) continue;
                foreach (int clip in pair.Value) rows[clip].ExclusiveBytes += Int(Obj(views[pair.Key]), "byteLength", 0);
            }
            return rows;
        }

        /// <summary>Case-insensitive substring match against the mandatory action tokens.</summary>
        private static bool IsMandatory(string name)
        {
            string lower = (name ?? "").ToLowerInvariant();
            foreach (string token in MandatoryTokens) if (lower.Contains(token)) return true;
            return false;
        }

        /// <summary>Every bufferView an accessor reads, its sparse blocks included.</summary>
        private static IEnumerable<int> AccessorViews(Dictionary<string, object> accessor)
        {
            int view = Int(accessor, "bufferView", -1);
            if (view >= 0) yield return view;
            Dictionary<string, object> sparse = Obj(Get(accessor, "sparse"));
            if (sparse == null) yield break;
            foreach (string half in new[] { "indices", "values" })
            {
                int part = Int(Obj(Get(sparse, half)), "bufferView", -1);
                if (part >= 0) yield return part;
            }
        }

        /// <summary>Bytes one element of this accessor occupies. Zero for anything glTF does not define.</summary>
        private static int ElementSize(Dictionary<string, object> accessor)
        {
            int component;
            switch (Int(accessor, "componentType", 0))
            {
                case 5120: case 5121: component = 1; break;
                case 5122: case 5123: component = 2; break;
                case 5125: case 5126: component = 4; break;
                // ponytail: an unknown componentType costs 0 rather than throwing - a census must
                // survive the files GlbReader refuses, which is the reason this class exists.
                default: return 0;
            }
            switch (Str(accessor, "type"))
            {
                case "SCALAR": return component;
                case "VEC2": return component * 2;
                case "VEC3": return component * 3;
                case "VEC4": case "MAT2": return component * 4;
                case "MAT3": return component * 9;
                case "MAT4": return component * 16;
                default: return 0;
            }
        }

        // --- JSON reading. Json.Parse hands back Dictionary/List/double/string, so every read is a
        // cast that has to survive a hostile file: a wrong type reads as absent, never as a throw.

        private static void Add(HashSet<int> set, int value) { if (value >= 0) set.Add(value); }

        private static object Get(Dictionary<string, object> map, string key) =>
            map != null && map.TryGetValue(key, out object value) ? value : null;

        private static Dictionary<string, object> Obj(object value) => value as Dictionary<string, object>;

        private static List<object> Arr(Dictionary<string, object> map, string key) => Get(map, key) as List<object>;

        private static string Str(Dictionary<string, object> map, string key) => Get(map, key) as string;

        private static int Int(Dictionary<string, object> map, string key, int fallback) =>
            Get(map, key) is double number ? (int)number : fallback;
    }
}
