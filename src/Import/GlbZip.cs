using System;
using System.Collections.Generic;

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

        /// <summary>The accessor at that index, or null when the document names no such thing.</summary>
        private static Dictionary<string, object> Accessor(GlbDocument doc, int index)
        {
            List<object> accessors = GlbSlim.Arr(doc.Json, "accessors");
            return accessors == null || index < 0 || index >= accessors.Count
                ? null : GlbSlim.Obj(accessors[index]);
        }
    }
}
