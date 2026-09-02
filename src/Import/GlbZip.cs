using System;
using System.Collections.Generic;
using System.IO;

namespace Morgott.ContentTool.Import
{
    /// <summary>
    /// Shrink the ANIMATION half of a .glb without dropping a clip, a channel or a key: the same
    /// curves, stored differently. Port of tools\ppzip.py. Two passes, both optional:
    ///
    ///  - CONSTANT: a curve that holds one value for the whole clip is collapsed to its two endpoint
    ///    keys. EXACTLY LOSSLESS, because GlbReader resamples every curve onto a uniform grid
    ///    (src\Import\GlbReader.cs:1104-1160) and a two-key constant samples to the same value at
    ///    every frame a 805-key constant did.
    ///  - QUANTISE: rotation outputs become normalized int16. Quaternion components are already in
    ///    [-1, 1], the worst-case error is 1/65534 (~0.002 degrees) and GlbReader.cs:2212 already
    ///    decodes the form, so nothing on the read side changes.
    ///
    /// It deliberately does NOT resample to a lower rate - see tools\ppzip.py:25-30 for the
    /// measurement that rules it out.
    ///
    /// Like GlbSlim it works on GlbDocument's parsed JSON and its BIN chunk, never on GlbReader's
    /// imported model, and it reads that JSON through GlbSlim's own readers - a wrong type reads as
    /// absent, never as a throw - so it survives the files GlbReader refuses.
    /// </summary>
    internal static class GlbZip
    {
        internal const int Float = 5126;
        internal const int Short = 5122;

        /// <summary>A quaternion component that survives the int16 round trip to within this is the
        /// same rotation: 1/32767 is the quantum, half of it is representation rather than error.</summary>
        internal const float QuantMaxError = 1.0f / 65534.0f;

        /// <summary>How still a curve has to be to count as constant, as a QUANTITY rather than a float
        /// tolerance: 1e-6 of a quaternion component is ~1e-4 degrees, of a translation 1 micrometre.
        /// ppzip uses this one number for translation, rotation, scale and weights alike
        /// (tools\ppzip.py:88-98) and so does this - a per-path knob would be a knob nobody can set.</summary>
        internal const float StillEpsilon = 1e-6f;

        /// <summary>What one run did, for the sentence the panel shows.</summary>
        internal sealed class Stats
        {
            internal int Collapsed;    // curves rewritten to two endpoint keys
            internal int Quantised;    // rotation curves stored as normalized int16
            internal int Skipped;      // samplers left alone: strided, sparse, STEP, unreadable
            internal int Shared;       // outputs left alone because more than one clip names them
            internal long KeysBefore;
            internal long KeysAfter;
        }

        /// <summary>One accessor as a flat float run. FLOAT and normalized SHORT are both understood,
        /// which is what makes the tool idempotent - a second run reads back what the first wrote.
        /// Returns null for a form Packed() would have refused.</summary>
        internal static float[] ReadFloats(GlbDocument doc, int accessorIndex)
        {
            if (!Packed(doc, accessorIndex)) return null;
            Dictionary<string, object> accessor = Accessor(doc, accessorIndex);
            Dictionary<string, object> view =
                GlbSlim.Obj(GlbSlim.Arr(doc.Json, "bufferViews")[GlbSlim.Int(accessor, "bufferView", -1)]);
            bool floats = GlbSlim.Int(accessor, "componentType", 0) == Float;
            int size = floats ? 4 : 2;
            long at = GlbSlim.Long(view, "byteOffset", 0) + GlbSlim.Long(accessor, "byteOffset", 0);
            long count = GlbSlim.Long(accessor, "count", 0) * (GlbSlim.ElementSize(accessor) / size);

            byte[] bin = doc.Bin;
            if (bin == null || count > int.MaxValue || at + count * size > bin.Length) return null;
            var values = new float[count];
            for (int i = 0; i < values.Length; i++)
                values[i] = floats
                    ? BitConverter.ToSingle(bin, (int)at + i * 4)
                    // The decoder GlbReader.cs:2212 already uses, so a re-read of a quantised file
                    // gives back exactly what the quantiser meant.
                    : Math.Max(BitConverter.ToInt16(bin, (int)at + i * 2) / 32767f, -1f);
            return values;
        }

        /// <summary>False for an accessor this tool must not touch, because ReadFloats would MISREAD
        /// it. glTF lets a bufferView declare a byteStride and an accessor be sparse; both are legal on
        /// a sampler and both mean the values are not the flat little-endian run ReadFloats assumes.
        /// Reading one as if it were would splice padding or a neighbour into the curve and then write
        /// that corruption back. Also false for a componentType that is neither FLOAT nor normalized
        /// SHORT - ppzip exits there (ppzip.py:65); inside OnGUI a skip is the only survivable answer.</summary>
        internal static bool Packed(GlbDocument doc, int accessorIndex)
        {
            Dictionary<string, object> accessor = Accessor(doc, accessorIndex);
            if (accessor == null || GlbSlim.Get(accessor, "sparse") != null) return false;

            int component = GlbSlim.Int(accessor, "componentType", 0);
            if (component != Float &&
                !(component == Short && GlbSlim.Get(accessor, "normalized") is bool on && on)) return false;
            int element = GlbSlim.ElementSize(accessor);
            if (element <= 0) return false;

            List<object> views = GlbSlim.Arr(doc.Json, "bufferViews");
            int index = GlbSlim.Int(accessor, "bufferView", -1);
            if (views == null || index < 0 || index >= views.Count) return false;
            object stride = GlbSlim.Get(GlbSlim.Obj(views[index]), "byteStride");
            return stride == null || (stride is double declared && declared == element);
        }

        /// <summary>True when every element of the curve equals the first one, component-wise.</summary>
        internal static bool IsConstant(float[] values, int stride)
        {
            if (values == null || stride <= 0 || values.Length <= stride) return false;
            for (int i = stride; i < values.Length; i++)
                if (Math.Abs(values[i] - values[i % stride]) > StillEpsilon) return false;
            return true;
        }

        /// <summary>The value block for one curve: little-endian float32, or normalized int16 with
        /// q = round(v * 32767) clamped to +-32767 - NOT -32768, which GlbReader.cs:2212 would decode
        /// as -1.0000305 and clamp anyway. Round-trip exactness first.</summary>
        internal static byte[] Pack(float[] values, bool quantise)
        {
            var bytes = new byte[values.Length * (quantise ? 2 : 4)];
            if (!quantise)
            {
                // float[] is already the little-endian run glTF wants, so this is the whole pass.
                Buffer.BlockCopy(values, 0, bytes, 0, bytes.Length);
                return bytes;
            }
            for (int i = 0; i < values.Length; i++)
            {
                double q = Math.Round(values[i] * 32767.0);
                short packed = (short)(q < -32767 ? -32767 : q > 32767 ? 32767 : q);
                bytes[i * 2] = (byte)packed;
                bytes[i * 2 + 1] = (byte)(packed >> 8);
            }
            return bytes;
        }

        /// <summary>
        /// What a run would do to ONE sampler output accessor, decided across the WHOLE document
        /// rather than per clip. ppzip decides per clip (ppzip.py:125-201) and an accessor two
        /// animations share is rewritten twice - the second read comes out of the STALE blob, which
        /// is the struct.error lib\u9_probe.glb raises. Here an output named by more than one
        /// animation, by two different target paths, by two different inputs, or by any sampler that
        /// is not LINEAR-and-packed, is left exactly as it is.
        /// </summary>
        private sealed class Curve
        {
            internal int Output;        // accessor index
            internal int Input;         // accessor index of its key times
            internal int Animation;     // which clip names it, -1 once a second one does
            internal string Path;       // "rotation" | "translation" | "scale" | "weights"
            internal bool Usable;       // false = leave the accessor alone entirely
            internal float[] Values;
            internal int Stride;
            internal bool Constant;
            /// <summary>First and last key time, read HERE rather than during the rewrite: by then
            /// output accessors have been re-pointed at bytes BIN does not hold yet, and an accessor
            /// that is one clip's input and another's output would read back garbage.</summary>
            internal float[] Ends;
        }

        /// <summary>Read every animation sampler once, from the ORIGINAL bin, and decide its fate.</summary>
        private static Dictionary<int, Curve> Plan(GlbDocument doc, Stats stats)
        {
            var curves = new Dictionary<int, Curve>();
            List<object> animations = GlbSlim.Arr(doc.Json, "animations");
            List<object> accessors = GlbSlim.Arr(doc.Json, "accessors");
            if (animations == null || accessors == null) return curves;

            for (int ai = 0; ai < animations.Count; ai++)
            {
                Dictionary<string, object> animation = GlbSlim.Obj(animations[ai]);
                List<object> samplers = GlbSlim.Arr(animation, "samplers");
                if (samplers == null) continue;

                // A sampler drives whatever its CHANNEL says; only rotation is quantisable and only a
                // channel tells us which is which. A sampler nothing points at is left alone.
                var pathOf = new Dictionary<int, string>();
                foreach (object channel in GlbSlim.Arr(animation, "channels") ?? Empty)
                {
                    int si = GlbSlim.Int(GlbSlim.Obj(channel), "sampler", -1);
                    string path = GlbSlim.Str(GlbSlim.Obj(GlbSlim.Get(GlbSlim.Obj(channel), "target")), "path");
                    if (si >= 0 && path != null && !pathOf.ContainsKey(si)) pathOf[si] = path;
                }

                for (int si = 0; si < samplers.Count; si++)
                {
                    Dictionary<string, object> sampler = GlbSlim.Obj(samplers[si]);
                    int output = GlbSlim.Int(sampler, "output", -1);
                    if (output < 0 || output >= accessors.Count) continue;
                    int input = GlbSlim.Int(sampler, "input", -1);

                    string path;
                    bool candidate = pathOf.TryGetValue(si, out path) &&
                                     (GlbSlim.Str(sampler, "interpolation") ?? "LINEAR") == "LINEAR";
                    float[] values = candidate && input >= 0 && Packed(doc, input)
                        ? ReadFloats(doc, output) : null;
                    if (candidate && values == null) stats.Skipped++;

                    Curve curve;
                    if (curves.TryGetValue(output, out curve))
                    {
                        if (curve.Animation >= 0 && curve.Animation != ai) { curve.Animation = -1; stats.Shared++; }
                        if (values == null || curve.Animation < 0 || curve.Path != path || curve.Input != input)
                            curve.Usable = false;
                        continue;
                    }

                    curve = new Curve { Output = output, Input = input, Animation = ai, Path = path,
                                        Values = values, Usable = values != null };
                    curves[output] = curve;
                    if (values == null) continue;
                    curve.Stride = Lanes(GlbSlim.Obj(accessors[output]));
                    if (curve.Stride <= 0 || values.Length < curve.Stride) { curve.Usable = false; continue; }
                    curve.Constant = IsConstant(values, curve.Stride);
                    if (!curve.Constant) continue;
                    float[] times = ReadFloats(doc, input);
                    if (times == null || times.Length == 0) curve.Usable = false;
                    else curve.Ends = new[] { times[0], times[times.Length - 1] };
                }
            }
            return curves;
        }

        /// <summary>
        /// Rewrite every sampler this document lets us rewrite, then hand the leftovers to the pass
        /// that already exists: GlbSlim.Trim with an empty drop set drops the accessors and
        /// bufferViews nothing points at any more and compacts BIN (GlbSlim.cs:147-224) - which is
        /// exactly what ppzip.zip_anims delegates to ppslim.slim with a regex matching no clip
        /// (ppzip.py:205-207). Trim does nothing at all when nothing came unreferenced (the shared-view
        /// case, GlbSlim.cs:170-171), which is why the buffer's byteLength is maintained here.
        /// </summary>
        /// <param name="constant">Collapse a curve that never moves to its two endpoint keys.</param>
        /// <param name="quantise">Store rotation outputs as normalized int16.</param>
        /// <returns>What was done, for the panel's sentence.</returns>
        internal static Stats Zip(GlbDocument doc, bool constant, bool quantise)
        {
            var stats = new Stats();
            Dictionary<int, Curve> curves = Plan(doc, stats);
            List<object> animations = GlbSlim.Arr(doc.Json, "animations");
            List<object> accessors = GlbSlim.Arr(doc.Json, "accessors");
            List<object> views = GlbSlim.Arr(doc.Json, "bufferViews");
            if (animations == null || accessors == null || views == null) return stats;

            var perClip = new Dictionary<int, List<Curve>>();
            foreach (Curve curve in curves.Values)
            {
                if (!curve.Usable || curve.Animation < 0) continue;
                List<Curve> clip;
                if (!perClip.TryGetValue(curve.Animation, out clip)) perClip[curve.Animation] = clip = new List<Curve>();
                clip.Add(curve);
            }

            byte[] old = doc.Bin ?? new byte[0];
            // ponytail: BIN is grown as one in-memory copy, so a 36 MB model peaks at ~72 MB. Upgrade
            // path = stream straight to the .ct_tmp the job already writes, the day a file needs it.
            var bin = new MemoryStream(old.Length + 1024);
            bin.Write(old, 0, old.Length);

            for (int ai = 0; ai < animations.Count; ai++)
            {
                List<Curve> clip;
                if (!perClip.TryGetValue(ai, out clip)) continue;
                clip.Sort((a, b) => a.Output.CompareTo(b.Output));

                // LEAVE A WHOLLY CONSTANT CLIP ALONE. GlbReader picks a clip's frame rate from the
                // coarsest rate every key time lands on and derives the clip's LENGTH from it; as long
                // as one channel keeps its dense key times that rate is unchanged. Collapse the last
                // dense channel too and the only times left are the endpoints, the rate can drop to
                // 1 Hz, and the clip comes out LONGER than it was authored (ppzip.py:145-151).
                bool allConstant = true;
                foreach (Curve curve in clip) allConstant &= curve.Constant;
                bool collapse = constant && !allConstant;

                var newInput = new Dictionary<(int, float, float), int>();
                var repoint = new Dictionary<int, int>();
                var block = new MemoryStream();

                foreach (Curve curve in clip)
                {
                    Dictionary<string, object> accessor = GlbSlim.Obj(accessors[curve.Output]);
                    stats.KeysBefore += GlbSlim.Long(accessor, "count", 0);
                    bool quant = quantise && curve.Path == "rotation";
                    float[] values = curve.Values;

                    if (collapse && curve.Constant)
                    {
                        values = new float[curve.Stride * 2];
                        for (int i = 0; i < values.Length; i++) values[i] = curve.Values[i % curve.Stride];
                        var key = (curve.Input, curve.Ends[0], curve.Ends[1]);
                        int replacement;
                        if (!newInput.TryGetValue(key, out replacement))
                        {
                            accessors.Add(new Dictionary<string, object>
                            {
                                { "bufferView", (double)AddView(bin, views, Pack(curve.Ends, false)) },
                                { "componentType", (double)Float },
                                { "count", 2.0 },
                                { "type", "SCALAR" },
                                // glTF requires min and max on an animation sampler's INPUT
                                // (src\Import\GlbCodec.cs:535-538), and only there.
                                { "min", new List<object> { (double)curve.Ends[0] } },
                                { "max", new List<object> { (double)curve.Ends[1] } }
                            });
                            newInput[key] = replacement = accessors.Count - 1;
                        }
                        repoint[curve.Output] = replacement;
                        stats.Collapsed++;
                    }

                    if (quant) stats.Quantised++;
                    Place(block, accessor, Pack(values, quant), values.Length / curve.Stride,
                          quant ? Short : Float, quant);
                    stats.KeysAfter += GlbSlim.Long(accessor, "count", 0);
                }

                if (block.Length == 0) continue;
                int view = AddView(bin, views, block.ToArray());
                foreach (Curve curve in clip) GlbSlim.Obj(accessors[curve.Output])["bufferView"] = (double)view;
                foreach (object sampler in GlbSlim.Arr(GlbSlim.Obj(animations[ai]), "samplers") ?? Empty)
                {
                    int replacement;
                    if (repoint.TryGetValue(GlbSlim.Int(GlbSlim.Obj(sampler), "output", -1), out replacement))
                        GlbSlim.Obj(sampler)["input"] = (double)replacement;
                }
            }

            if (bin.Length == old.Length) return stats;      // nothing was rewritable; the file is as it was
            doc.Bin = bin.ToArray();
            List<object> buffers = GlbSlim.Arr(doc.Json, "buffers");
            if (buffers != null && buffers.Count > 0)
                GlbSlim.Obj(buffers[0])["byteLength"] = (double)doc.Bin.Length;
            doc.Dirty = true;
            GlbSlim.Trim(doc, new HashSet<int>());
            return stats;
        }

        /// <summary>One curve's new bytes inside its clip's block, and the accessor pointed at them.
        /// min/max described the OLD data and glTF requires neither on a sampler output.</summary>
        private static void Place(MemoryStream block, Dictionary<string, object> accessor, byte[] payload,
                                  int count, int component, bool normalized)
        {
            Align(block);                                   // >= every component size this ever emits
            accessor["byteOffset"] = (double)block.Length;
            accessor["count"] = (double)count;
            accessor["componentType"] = (double)component;
            if (normalized) accessor["normalized"] = true;
            else accessor.Remove("normalized");
            accessor.Remove("min");
            accessor.Remove("max");
            block.Write(payload, 0, payload.Length);
        }

        /// <summary>Append a block to BIN as one more bufferView, and hand back its index.</summary>
        private static int AddView(MemoryStream bin, List<object> views, byte[] data)
        {
            Align(bin);
            views.Add(new Dictionary<string, object>
            {
                { "buffer", 0.0 }, { "byteOffset", (double)bin.Length }, { "byteLength", (double)data.Length }
            });
            bin.Write(data, 0, data.Length);
            return views.Count - 1;
        }

        private static void Align(MemoryStream stream)
        {
            for (long slack = (4 - stream.Length % 4) % 4; slack > 0; slack--) stream.WriteByte(0);
        }

        /// <summary>Components per element - 4 for a quaternion, 1 for a morph weight.</summary>
        private static int Lanes(Dictionary<string, object> accessor) =>
            GlbSlim.ElementSize(accessor) / (GlbSlim.Int(accessor, "componentType", 0) == Float ? 4 : 2);

        private static readonly List<object> Empty = new List<object>();

        /// <summary>The accessor at that index, or null when the document names no such thing.</summary>
        private static Dictionary<string, object> Accessor(GlbDocument doc, int index)
        {
            List<object> accessors = GlbSlim.Arr(doc.Json, "accessors");
            return accessors == null || index < 0 || index >= accessors.Count
                ? null : GlbSlim.Obj(accessors[index]);
        }
    }
}
