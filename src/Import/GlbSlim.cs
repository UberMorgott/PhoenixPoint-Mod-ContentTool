using System;
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
                    row.AccessorBytes += Long(accessor, "count", 0) * ElementSize(accessor);
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
                foreach (int clip in pair.Value) rows[clip].ExclusiveBytes += Long(Obj(views[pair.Key]), "byteLength", 0);
            }
            return rows;
        }

        /// <summary>
        /// Pre-flight check before a trim. Returns null when the trim is safe, else the refusal to
        /// show the user. Refuses when a dropped clip is mandatory, or the file is a rigged
        /// character with a clip library - both overridable with force - and, force or not, when
        /// something other than an accessor or an image owns buffer data, because a trim would cut
        /// that loose without ever seeing it.
        /// </summary>
        internal static string Guard(GlbDocument doc, HashSet<int> dropIndices, bool force)
        {
            List<object> accessors = Arr(doc.Json, "accessors") ?? Empty;
            List<object> images = Arr(doc.Json, "images") ?? Empty;
            int named = BufferViewKeys(doc.Json);
            if (named != accessors.Count + images.Count)
                return "this .glb names a bufferView " + named + " times where its " + accessors.Count +
                       " accessor(s) and " + images.Count + " image(s) account for " +
                       (accessors.Count + images.Count) + ". Something the trim does not walk owns " +
                       "buffer data here - a sparse accessor, Draco or meshopt compression, an unknown " +
                       "extension - and trimming would cut it loose. Refusing.";
            foreach (object view in Arr(doc.Json, "bufferViews") ?? Empty)
                if (Int(Obj(view), "buffer", 0) != 0)
                    return "this .glb keeps a bufferView in a buffer other than the BIN chunk, and a " +
                           "trim only knows how to compact BIN. Refusing.";
            if (force) return null;

            List<object> animations = Arr(doc.Json, "animations") ?? Empty;
            foreach (int index in dropIndices)
            {
                if (index < 0 || index >= animations.Count) continue;
                string name = Str(Obj(animations[index]), "name") ?? "";
                if (IsMandatory(name))
                    return "\"" + name + "\" reads as a mandatory action clip, and a creature that " +
                           "loses one stops moving in game. Tick force to drop it anyway.";
            }
            if (animations.Count > 30 && (Arr(doc.Json, "skins") ?? Empty).Count > 0)
                return "this .glb carries a skin and " + animations.Count + " clips, so it is a rigged " +
                       "character rather than a prop, and dropping clips from one is how a soldier ends " +
                       "up T-posing. Tick force to trim it anyway.";
            return null;
        }

        /// <summary>
        /// Drop the clips at dropIndices, remove the accessors and bufferViews nothing needs any
        /// more, compact BIN and remap every index that moved. Sets Dirty - unless there was nothing
        /// to do, in which case the document is left untouched so it still writes verbatim. Returns
        /// the BIN byte delta (negative = saved).
        /// </summary>
        internal static long Trim(GlbDocument doc, HashSet<int> dropIndices)
        {
            List<object> animations = Arr(doc.Json, "animations") ?? Empty;
            List<object> accessors = Arr(doc.Json, "accessors") ?? Empty;
            List<object> views = Arr(doc.Json, "bufferViews") ?? Empty;
            List<object> images = Arr(doc.Json, "images") ?? Empty;

            var survivors = new List<object>();
            for (int i = 0; i < animations.Count; i++)
                if (!dropIndices.Contains(i)) survivors.Add(animations[i]);

            var keepAccessor = new HashSet<int>();
            AccessorSlots(doc.Json, survivors, index => { keepAccessor.Add(index); return index; });
            var keepView = new HashSet<int>();
            for (int i = 0; i < accessors.Count; i++)
            {
                if (!keepAccessor.Contains(i)) continue;
                foreach (int view in AccessorViews(Obj(accessors[i]))) keepView.Add(view);
            }
            foreach (object image in images) Add(keepView, Int(Obj(image), "bufferView", -1));

            int[] accessorMap = Remap(accessors, keepAccessor, out List<object> newAccessors);
            int[] viewMap = Remap(views, keepView, out List<object> newViews);
            if (survivors.Count == animations.Count &&
                newAccessors.Count == accessors.Count && newViews.Count == views.Count) return 0;

            long delta = newViews.Count == views.Count ? 0 : Compact(doc, newViews);

            Replace(doc.Json, "animations", survivors);
            Replace(doc.Json, "accessors", newAccessors);
            Replace(doc.Json, "bufferViews", newViews);
            AccessorSlots(doc.Json, survivors, index => accessorMap[index]);
            foreach (object accessor in newAccessors)
            {
                Dictionary<string, object> map = Obj(accessor);
                Move(map, "bufferView", viewMap);
                Dictionary<string, object> sparse = Obj(Get(map, "sparse"));
                if (sparse == null) continue;
                Move(Obj(Get(sparse, "indices")), "bufferView", viewMap);
                Move(Obj(Get(sparse, "values")), "bufferView", viewMap);
            }
            foreach (object image in images) Move(Obj(image), "bufferView", viewMap);
            doc.Dirty = true;
            return delta;
        }

        /// <summary>Rebuild BIN out of the surviving bufferViews, in order, 4-byte aligned, and move
        /// each view's byteOffset onto its new home. Returns the byte delta.</summary>
        private static long Compact(GlbDocument doc, List<object> views)
        {
            byte[] old = doc.Bin ?? new byte[0];
            long total = 0;
            foreach (object view in views) total = Aligned(total) + Long(Obj(view), "byteLength", 0);
            total = Aligned(total);
            if (total > int.MaxValue)
                throw new InvalidOperationException("a trimmed BIN chunk of " + total + " bytes does not " +
                                                    "fit an array; this .glb is beyond what the tool handles");

            var bin = new byte[total];
            int at = 0;
            foreach (object view in views)
            {
                Dictionary<string, object> map = Obj(view);
                long from = Long(map, "byteOffset", 0), length = Long(map, "byteLength", 0);
                if (from < 0 || length < 0 || from + length > old.Length)
                    throw new InvalidOperationException("a bufferView of this .glb reads bytes " + from +
                                                        ".." + (from + length) + " of a " + old.Length +
                                                        "-byte BIN chunk; the file is malformed");
                at = (int)Aligned(at);
                Buffer.BlockCopy(old, (int)from, bin, at, (int)length);
                map["byteOffset"] = (double)at;
                at += (int)length;
            }
            doc.Bin = bin;
            List<object> buffers = Arr(doc.Json, "buffers");
            if (buffers != null && buffers.Count > 0) Obj(buffers[0])["byteLength"] = (double)bin.Length;
            return bin.Length - (long)old.Length;
        }

        /// <summary>Kept entries in file order, plus old index -> new index (-1 when dropped).</summary>
        private static int[] Remap(List<object> entries, HashSet<int> keep, out List<object> kept)
        {
            var map = new int[entries.Count];
            kept = new List<object>();
            for (int i = 0; i < entries.Count; i++)
            {
                map[i] = -1;
                if (!keep.Contains(i)) continue;
                map[i] = kept.Count;
                kept.Add(entries[i]);
            }
            return map;
        }

        /// <summary>Every place core glTF names an accessor, handed to visit to read or to rewrite.</summary>
        // ponytail: core-spec slots only - mesh primitives (attributes, indices, morph targets),
        // skins and animation samplers. An extension that names an accessor of its own is invisible
        // here; upgrade path = a per-extension slot table the day a file needs one.
        private static void AccessorSlots(Dictionary<string, object> json, List<object> animations,
                                          Func<int, int> visit)
        {
            foreach (object mesh in Arr(json, "meshes") ?? Empty)
                foreach (object primitive in Arr(Obj(mesh), "primitives") ?? Empty)
                {
                    Dictionary<string, object> p = Obj(primitive);
                    Move(p, "indices", visit);
                    MoveAll(Obj(Get(p, "attributes")), visit);
                    foreach (object target in Arr(p, "targets") ?? Empty) MoveAll(Obj(target), visit);
                }
            foreach (object skin in Arr(json, "skins") ?? Empty)
                Move(Obj(skin), "inverseBindMatrices", visit);
            foreach (object animation in animations)
                foreach (object sampler in Arr(Obj(animation), "samplers") ?? Empty)
                {
                    Move(Obj(sampler), "input", visit);
                    Move(Obj(sampler), "output", visit);
                }
        }

        /// <summary>Recursive count of every "bufferView" key anywhere in the document.</summary>
        private static int BufferViewKeys(object node)
        {
            int count = 0;
            if (node is Dictionary<string, object> map)
                foreach (KeyValuePair<string, object> pair in map)
                {
                    if (pair.Key == "bufferView") count++;
                    count += BufferViewKeys(pair.Value);
                }
            else if (node is List<object> items)
                foreach (object item in items) count += BufferViewKeys(item);
            return count;
        }

        private static void Move(Dictionary<string, object> map, string key, int[] indexMap) =>
            Move(map, key, index => indexMap[index]);

        private static void Move(Dictionary<string, object> map, string key, Func<int, int> visit)
        {
            int index = Int(map, key, -1);
            if (index < 0) return;
            map[key] = (double)Moved(visit, index);
        }

        private static void MoveAll(Dictionary<string, object> map, Func<int, int> visit)
        {
            if (map == null) return;
            foreach (string key in new List<string>(map.Keys))
            {
                int index = Int(map, key, -1);
                if (index >= 0) map[key] = (double)Moved(visit, index);
            }
        }

        private static int Moved(Func<int, int> visit, int index)
        {
            int moved = visit(index);
            if (moved < 0)
                throw new InvalidOperationException("this .glb still names accessor " + index +
                                                    " after the trim decided nothing needs it; refusing " +
                                                    "to write a file with a dangling reference");
            return moved;
        }

        /// <summary>Reassign an array, or drop the key when the trim emptied it - glTF has no empty arrays.</summary>
        private static void Replace(Dictionary<string, object> json, string key, List<object> values)
        {
            if (!json.ContainsKey(key)) return;
            if (values.Count == 0) json.Remove(key);
            else json[key] = values;
        }

        private static long Aligned(long offset) => offset + (4 - offset % 4) % 4;

        private static readonly List<object> Empty = new List<object>();

        /// <summary>Case-insensitive substring match against the mandatory action tokens.</summary>
        private static bool IsMandatory(string name)
        {
            string lower = (name ?? "").ToLowerInvariant();
            foreach (string token in MandatoryTokens) if (lower.Contains(token)) return true;
            return false;
        }

        /// <summary>Every bufferView an accessor reads, its sparse blocks included.</summary>
        internal static IEnumerable<int> AccessorViews(Dictionary<string, object> accessor)
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
        internal static int ElementSize(Dictionary<string, object> accessor)
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

        internal static object Get(Dictionary<string, object> map, string key) =>
            map != null && map.TryGetValue(key, out object value) ? value : null;

        internal static Dictionary<string, object> Obj(object value) => value as Dictionary<string, object>;

        internal static List<object> Arr(Dictionary<string, object> map, string key) => Get(map, key) as List<object>;

        internal static string Str(Dictionary<string, object> map, string key) => Get(map, key) as string;

        /// <summary>An array index. Anything a C# array cannot be indexed by reads as absent.</summary>
        internal static int Int(Dictionary<string, object> map, string key, int fallback) =>
            Get(map, key) is double number && number >= 0 && number <= int.MaxValue ? (int)number : fallback;

        /// <summary>A byte size or offset. GLB spells these as uint32, so they outgrow int - and a
        /// count times an element size outgrows it twice over.</summary>
        internal static long Long(Dictionary<string, object> map, string key, long fallback) =>
            Get(map, key) is double number && number >= 0 && number <= uint.MaxValue ? (long)number : fallback;
    }
}
