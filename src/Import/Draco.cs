using System;
using System.Collections.Generic;
using System.Globalization;

namespace Morgott.ContentTool.Import
{
    /// <summary>
    /// Hand-written decoder for <b>KHR_draco_mesh_compression</b> - Draco bitstream 2.2, the geometry
    /// compression the Khronos sample assets and most "Draco-optimised" downloads arrive in. No
    /// library and no NuGet package, for the same reason <see cref="Meshopt"/> has none: the loader
    /// does <c>Assembly.Load(byte[])</c> and never resolves a sibling DLL.
    ///
    /// GROUNDING. Every rule is taken from the Draco bitstream specification's own pseudocode
    /// (github.com/google/draco, <c>docs/spec/*.md</c>, published at google.github.io/draco/spec),
    /// FETCHED rather than remembered, and named at its site by the file it comes from
    /// (edgebreaker.decoder.md, rans.decoding.md, prediction.*.md ...). Where the specification is
    /// silent or contradicts itself it is settled against the Apache-2.0 reference decoder
    /// (github.com/google/draco/src/draco), and each of those FOUR places is called out where it
    /// happens:
    ///  - the bit reader's ORDER. The spec's conventions say "the bits are read from high to low
    ///    order"; the reference reads them LOW first (decoder_buffer.h, BitDecoder::GetBit:
    ///    <c>(bit_buffer_[byte_offset] &gt;&gt; bit_shift) &amp; 1</c> with bit_shift = off &amp; 7,
    ///    and GetBits assembling <c>value |= GetBit() &lt;&lt; bit</c>). Low first is used here.
    ///  - TAGGED symbols. The spec calls ResetBitReader() after every value, which would byte-align
    ///    the value stream 4 000 times in one attribute; symbol_decoding.cc calls StartBitDecoding
    ///    once before the loop and EndBitDecoding once after it, so the bits run on unbroken.
    ///  - a NORMALS attribute's component count. The spec's GetNumComponents() returns 2 only when
    ///    the prediction is PREDICTION_DIFFERENCE; the reference's
    ///    SequentialNormalAttributeDecoder::GetNumValueComponents() returns 2 ALWAYS ("we quantize
    ///    everything into two components"), which is the only count under which the octahedral
    ///    transform's own s/t pairs line up.
    ///  - PREDICTION_NONE. The spec's DecodePortableAttributes() skips DecodeIntegerValues()
    ///    entirely when an attribute has no prediction scheme, which would leave that attribute
    ///    empty and its bytes unread; the reference decodes the values either way
    ///    (sequential_integer_attribute_decoder.cc).
    ///
    /// This is a TRUST BOUNDARY like <see cref="GlbReader"/> and <see cref="Meshopt"/>: the bytes
    /// come from a folder a player can drop anything into. Every count is checked against a ceiling
    /// before an array is allocated for it, every corner and vertex index is range-checked at the
    /// point of use, and anything that still escapes leaves as a <see cref="FormatException"/>
    /// naming the cause - never an IndexOutOfRangeException into the game loop.
    ///
    /// ponytail: MESH connectivity only, and only the two traversal decoders the format actually
    /// ships (STANDARD and VALENCE edgebreaker, plus the sequential fallback). A POINT_CLOUD stream
    /// and the PREDICTIVE traversal are refused BY NAME - the spec has no pseudocode for the latter
    /// and no real file measured here uses either.
    /// </summary>
    internal sealed class Draco
    {
        /// <summary>The extension's own name, as it appears in extensionsRequired and on a primitive.</summary>
        internal const string Extension = "KHR_draco_mesh_compression";

        // ---- constants, all from docs/spec/variable.descriptions.md ----------------------------
        private const int MESH_SEQUENTIAL_ENCODING = 0, MESH_EDGEBREAKER_ENCODING = 1;
        private const int METADATA_FLAG_MASK = 32768;
        private const int SEQUENTIAL_ATTRIBUTE_ENCODER_GENERIC = 0;
        private const int SEQUENTIAL_ATTRIBUTE_ENCODER_INTEGER = 1;
        private const int SEQUENTIAL_ATTRIBUTE_ENCODER_QUANTIZATION = 2;
        private const int SEQUENTIAL_ATTRIBUTE_ENCODER_NORMALS = 3;
        private const int SEQUENTIAL_COMPRESSED_INDICES = 0, SEQUENTIAL_UNCOMPRESSED_INDICES = 1;
        private const int PREDICTION_NONE = -2, PREDICTION_DIFFERENCE = 0;
        private const int MESH_PREDICTION_PARALLELOGRAM = 1;
        private const int MESH_PREDICTION_CONSTRAINED_MULTI_PARALLELOGRAM = 4;
        private const int MESH_PREDICTION_TEX_COORDS_PORTABLE = 5;
        private const int MESH_PREDICTION_GEOMETRIC_NORMAL = 6;
        private const int PREDICTION_TRANSFORM_WRAP = 1;
        private const int PREDICTION_TRANSFORM_NORMAL_OCTAHEDRON_CANONICALIZED = 3;
        private const int MESH_TRAVERSAL_DEPTH_FIRST = 0, MESH_TRAVERSAL_PREDICTION_DEGREE = 1;
        private const int MESH_VERTEX_ATTRIBUTE = 0, MESH_CORNER_ATTRIBUTE = 1;
        private const int STANDARD_EDGEBREAKER = 0, VALENCE_EDGEBREAKER = 2;
        private const int kInvalidCornerIndex = -1;
        private const int LEFT_FACE_EDGE = 0, RIGHT_FACE_EDGE = 1;
        private const int kTexCoordsNumComponents = 2, kMaxNumParallelograms = 4, kMaxPriority = 3;
        private const int TOPOLOGY_C = 0, TOPOLOGY_S = 1, TOPOLOGY_L = 3, TOPOLOGY_R = 5, TOPOLOGY_E = 7;
        private const int MIN_VALENCE = 2, MAX_VALENCE = 7, NUM_UNIQUE_VALENCES = 6;
        private const int rabs_ans_p8_precision = 256, rabs_l_base = 4096;
        private const int IO_BASE = 256, L_RANS_BASE = 4096;
        private const int TAGGED_RANS_BASE = 16384, TAGGED_RANS_PRECISION = 4096;
        private const int TAGGED_SYMBOLS = 0, RAW_SYMBOLS = 1;
        /// <summary>draco's GeometryAttribute::Type - POSITION is 0, and it is how the position
        /// attribute is FOUND rather than assumed to be first.</summary>
        private const int ATTRIBUTE_POSITION = 0;
        /// <summary>draco's DataType enum, as att_dec_data_type states it.</summary>
        private const int DT_INT8 = 1, DT_UINT8 = 2, DT_INT16 = 3, DT_UINT16 = 4,
            DT_INT32 = 5, DT_UINT32 = 6, DT_FLOAT32 = 9;

        // draco's GeometryAttribute::Type. GENERIC means "the stream states no semantic", which is
        // what every TANGENT, JOINTS_0 and WEIGHTS_0 in the 263 real Draco primitives measured for
        // U12 arrives as - so only the three NAMED semantics can be held to a glTF attribute name.
        private const int GA_POSITION = 0, GA_NORMAL = 1, GA_COLOR = 2, GA_TEX_COORD = 3, GA_GENERIC = 4;

        /// <summary>
        /// Whether an attribute the stream calls <paramref name="type"/> may be the one a primitive
        /// maps to the glTF attribute <paramref name="name"/>. A stream that NAMES its semantic and
        /// names a different one than the glTF side asked for is a wrong mapping, whatever the
        /// lengths say. GENERIC states nothing, so nothing can be proven against it.
        /// ponytail: two GENERIC attributes of equal width (TANGENT vs WEIGHTS_0) remain
        /// indistinguishable - the stream itself carries nothing left to tell them apart.
        /// </summary>
        internal static bool TypeFits(int type, string name)
        {
            if (type == GA_GENERIC) return true;
            int wanted = name == "POSITION" ? GA_POSITION
                : name == "NORMAL" ? GA_NORMAL
                : name.StartsWith("COLOR_", StringComparison.Ordinal) ? GA_COLOR
                : name.StartsWith("TEXCOORD_", StringComparison.Ordinal) ? GA_TEX_COORD
                : -1;
            return type == wanted;
        }

        /// <summary>The name for <see cref="TypeFits"/>'s refusal, so it says what the stream holds.</summary>
        internal static string TypeName(int type) =>
            type == GA_POSITION ? "positions" : type == GA_NORMAL ? "normals" :
            type == GA_COLOR ? "vertex colours" : type == GA_TEX_COORD ? "texture coordinates" :
            "unnamed data";

        /// <summary>
        /// A Draco attribute's data type as the glTF componentType that means the same thing, so a
        /// caller can hold the extension's "the accessors properties ... must match the decompressed
        /// data" to the accessor beside it. 0 = glTF has no such component type (Draco's signed
        /// 32-bit and 64-bit forms), which is itself a mismatch.
        /// </summary>
        internal static int ComponentType(int dataType)
        {
            switch (dataType)
            {
                case DT_INT8: return Gltf.Byte;
                case DT_UINT8: return Gltf.UnsignedByte;
                case DT_INT16: return Gltf.Short;
                case DT_UINT16: return Gltf.UnsignedShort;
                case DT_UINT32: return Gltf.UnsignedInt;
                case DT_FLOAT32: return Gltf.Float;
                default: return 0;
            }
        }

        /// <summary>"Array of EdgeBreaker symbols", variable.descriptions.md - symbol id to topology bits.</summary>
        private static readonly int[] SymbolToTopology =
        { TOPOLOGY_C, TOPOLOGY_S, TOPOLOGY_L, TOPOLOGY_R, TOPOLOGY_E };

        // ---- ceilings, checked BEFORE any array is sized from a file's own number ---------------
        private const int MaxFaces = 4000000;
        private const int MaxAttributes = 64;
        private const int MaxSymbols = 1 << 24;

        // ------------------------------------------------------------------ the result

        internal sealed class Attribute
        {
            internal int UniqueId, Components, DataType;
            /// <summary>Draco's own GeometryAttribute::Type - what the stream says this attribute IS.</summary>
            internal int Type;
            /// <summary>The prediction scheme its values came back through - the test fixtures assert
            /// this so an arm cannot pass while measuring a path the file never took.</summary>
            internal int Prediction;
            internal bool Normalized;
            /// <summary>Points * Components values, already in POINT order.</summary>
            internal float[] Values;
        }

        internal sealed class Model
        {
            internal int Points;
            internal int[] Indices;
            internal readonly List<Attribute> Attributes = new List<Attribute>();
            /// <summary>What the stream said it was, for the log line.</summary>
            internal string Method = "";
        }

        /// <summary>
        /// One KHR_draco_mesh_compression bufferView decoded to points, triangle indices and one
        /// value array per attribute. <paramref name="at"/>/<paramref name="length"/> are the view's
        /// own bounds inside <paramref name="source"/>, already checked by the caller.
        /// </summary>
        internal static Model Decode(byte[] source, int at, int length, string what)
        {
            if (at < 0 || length < 0 || (long)at + length > source.Length)
                throw Bad(what + " reads compressed geometry past the end of the file, so the file is " +
                "truncated; copy or export it again");
            var decoder = new Draco(source, at, length, what);
            try { return decoder.Run(); }
            catch (FormatException) { throw; }
            catch (Exception exception)
            {
                // A malformed stream must not reach the game as an IndexOutOfRange/Overflow: every
                // escape is renamed here, with the cause kept for the log.
                throw Bad(what + "'s Draco geometry could not be decoded (" + exception.GetType().Name +
                    "); the file is corrupt, so download or export it again");
            }
        }

        // ------------------------------------------------------------------ the byte stream

        private readonly byte[] buf;
        private readonly int end;
        private readonly string what;
        private int p;
        private int bitStart = -1, bitOffset;

        private Draco(byte[] source, int at, int length, string where)
        {
            buf = source; p = at; end = at + length; what = where;
        }

        private FormatException Short() =>
            Bad(what + "'s Draco geometry ends in the middle of a value; the file is truncated, so " +
            "download or export it again");

        private byte U8()
        {
            if (p >= end) throw Short();
            return buf[p++];
        }

        private int I8() { return (sbyte)U8(); }

        private int U16()
        {
            int low = U8();
            return low | (U8() << 8);
        }

        private uint U32()
        {
            uint value = U8();
            value |= (uint)U8() << 8;
            value |= (uint)U8() << 16;
            value |= (uint)U8() << 24;
            return value;
        }

        private float F32()
        {
            uint bits = U32();
            return BitConverter.ToSingle(BitConverter.GetBytes(bits), 0);
        }

        /// <summary>varUI32/varUI64, decoded by core.functions.md's LEB128().</summary>
        private ulong Leb()
        {
            ulong result = 0;
            int shift = 0;
            while (true)
            {
                byte in_ = U8();
                if (shift > 63) throw Bad(what + "'s Draco geometry states a length that cannot be read; " +
                    "the file is corrupt, so download or export it again");
                result |= (ulong)(in_ & 0x7F) << shift;
                if ((in_ & 0x80) == 0) break;
                shift += 7;
            }
            return result;
        }

        private int Var(string field)
        {
            ulong value = Leb();
            if (value > int.MaxValue)
                throw Bad(what + "'s Draco geometry declares " + value.ToString(CultureInfo.InvariantCulture) +
                " " + field + ", which is more than this mod will read; simplify the mesh and export it again");
            return (int)value;
        }

        private byte[] Block(int size)
        {
            if (size < 0 || p + size > end) throw Short();
            var block = new byte[size];
            Buffer.BlockCopy(buf, p, block, 0, size);
            p += size;
            return block;
        }

        // The bit reader. Reference-grounded LOW bit first (decoder_buffer.h BitDecoder::GetBit),
        // NOT the "high to low order" of the spec's conventions page - see the class remark.
        private void StartBits() { bitStart = p; bitOffset = 0; }

        private uint ReadBits(int count)
        {
            if (bitStart < 0) throw Bad(what + "'s Draco geometry reads bits outside a bit block; " +
                "the file is corrupt, so download or export it again");
            if (count < 0 || count > 32)
                throw Bad(what + "'s Draco geometry asks for " + count.ToString(CultureInfo.InvariantCulture) +
                " bits at once, which the format does not allow; the file is corrupt, so export it again");
            uint value = 0;
            for (int bit = 0; bit < count; bit++)
            {
                int at = bitStart + (bitOffset >> 3);
                // The reference returns 0 past the end rather than failing; the same, so a stream that
                // ends on a partial byte decodes identically instead of throwing on the last value.
                int one = at < end ? (buf[at] >> (bitOffset & 7)) & 1 : 0;
                value |= (uint)one << bit;
                bitOffset++;
            }
            return value;
        }

        /// <summary>The spec's ResetBitReader(): "pad the read to the current byte".</summary>
        private void EndBits()
        {
            p = bitStart + ((bitOffset + 7) >> 3);
            if (p > end) throw Short();
            bitStart = -1;
        }

        // ------------------------------------------------------------------ rANS (rans.decoding.md)

        private sealed class Ans
        {
            internal byte[] Buf;
            internal int Offset;
            internal long State;
        }

        private Ans RansInitDecoder(byte[] buffer, int offset, int lRansBase)
        {
            if (offset < 1 || offset > buffer.Length)
                throw Bad2("a Draco entropy block is empty");
            var ans = new Ans { Buf = buffer };
            int x = buffer[offset - 1] >> 6;
            if (x == 0) { ans.Offset = offset - 1; ans.State = buffer[offset - 1] & 0x3F; }
            else if (x == 1) { ans.Offset = offset - 2; ans.State = Le(buffer, offset - 2, 2) & 0x3FFF; }
            else if (x == 2) { ans.Offset = offset - 3; ans.State = Le(buffer, offset - 3, 3) & 0x3FFFFF; }
            else { ans.Offset = offset - 4; ans.State = Le(buffer, offset - 4, 4) & 0x3FFFFFFF; }
            if (ans.Offset < 0) throw Bad2("a Draco entropy block is shorter than its own state");
            ans.State += lRansBase;
            return ans;
        }

        private static long Le(byte[] buffer, int at, int bytes)
        {
            if (at < 0 || at + bytes > buffer.Length) return 0;
            long value = 0;
            for (int i = bytes - 1; i >= 0; i--) value = (value << 8) | buffer[at + i];
            return value;
        }

        /// <summary>rans.decoding.md RansRead(), with its look-up table and probability table.</summary>
        private static uint RansRead(Ans ans, int lRansBase, int precision, int[] lut, int[] prob, int[] cum)
        {
            while (ans.State < lRansBase && ans.Offset > 0)
                ans.State = ans.State * IO_BASE + ans.Buf[--ans.Offset];
            long quo = ans.State / precision;
            long rem = ans.State % precision;
            int symbol = lut[rem];
            ans.State = quo * prob[symbol] + rem - cum[symbol];
            return (uint)symbol;
        }

        /// <summary>rans.decoding.md RabsDescRead(), the binary coder behind every flag stream.</summary>
        private static bool RabsDescRead(Ans ans, int probZero)
        {
            int pr = rabs_ans_p8_precision - probZero;
            if (ans.State < rabs_l_base && ans.Offset > 0)
                ans.State = ans.State * IO_BASE + ans.Buf[--ans.Offset];
            long x = ans.State;
            long quot = x / rabs_ans_p8_precision;
            long rem = x % rabs_ans_p8_precision;
            long xn = quot * pr;
            bool val = rem < pr;
            ans.State = val ? xn + rem : x - xn - pr;
            return val;
        }

        private sealed class Table
        {
            internal int[] Lut, Prob, Cum;
        }

        /// <summary>rans.decoding.md BuildSymbolTables() + rans_build_look_up_table().</summary>
        private Table BuildSymbolTables(int numSymbols, int precision)
        {
            if (numSymbols < 0 || numSymbols > MaxSymbols)
                throw Bad2("a Draco entropy table declares " + numSymbols.ToString(CultureInfo.InvariantCulture) +
                " symbols");
            var probs = new int[numSymbols];
            for (int i = 0; i < numSymbols; i++)
            {
                byte data = U8();
                int token = data & 3;
                if (token == 3)
                {
                    int offset = data >> 2;
                    if (i + offset >= numSymbols) throw Bad2("a Draco entropy table runs past its own symbols");
                    for (int j = 0; j < offset + 1; j++) probs[i + j] = 0;
                    i += offset;
                }
                else
                {
                    int prob = data >> 2;
                    for (int j = 0; j < token; j++)
                    {
                        int extra = U8();
                        prob |= extra << (8 * (j + 1) - 2);
                    }
                    probs[i] = prob;
                }
            }
            var table = new Table { Lut = new int[precision], Prob = probs, Cum = new int[numSymbols] };
            int cumulative = 0, act = 0;
            for (int i = 0; i < numSymbols; i++)
            {
                table.Cum[i] = cumulative;
                cumulative += probs[i];
                if (cumulative > precision) throw Bad2("a Draco entropy table's probabilities do not add up");
                for (int j = act; j < cumulative; j++) table.Lut[j] = i;
                act = cumulative;
            }
            if (numSymbols > 0 && cumulative != precision)
                throw Bad2("a Draco entropy table's probabilities do not add up");
            return table;
        }

        /// <summary>rans.decoding.md DecodeSymbols(): the one entry point every symbol stream uses.</summary>
        private uint[] DecodeSymbols(int numValues, int components)
        {
            var values = new uint[numValues];
            if (numValues == 0) return values;
            int scheme = U8();
            if (scheme == TAGGED_SYMBOLS) DecodeTaggedSymbols(numValues, components, values);
            else if (scheme == RAW_SYMBOLS) DecodeRawSymbols(numValues, values);
            else throw Bad2("a Draco symbol stream uses coding scheme " +
                scheme.ToString(CultureInfo.InvariantCulture) + ", which the format does not define");
            return values;
        }

        private void DecodeTaggedSymbols(int numValues, int components, uint[] values)
        {
            if (components < 1) throw Bad2("a Draco symbol stream has no components");
            int numSymbols = Var("symbols");
            Table table = BuildSymbolTables(numSymbols, TAGGED_RANS_PRECISION);
            int size = Var("entropy bytes");
            byte[] data = Block(size);
            Ans ans = RansInitDecoder(data, size, TAGGED_RANS_BASE);
            // ONE bit block for every value, not one per value: symbol_decoding.cc brackets the whole
            // loop with StartBitDecoding/EndBitDecoding. See the class remark.
            StartBits();
            int id = 0;
            for (int i = 0; i < numValues; i += components)
            {
                int bits = (int)RansRead(ans, TAGGED_RANS_BASE, TAGGED_RANS_PRECISION, table.Lut, table.Prob, table.Cum);
                for (int j = 0; j < components; j++)
                {
                    if (id >= numValues) throw Bad2("a Draco symbol stream holds more values than it declares");
                    values[id++] = ReadBits(bits);
                }
            }
            EndBits();
        }

        private void DecodeRawSymbols(int numValues, uint[] values)
        {
            int maxBitLength = U8();
            if (maxBitLength < 1 || maxBitLength > 18)
                throw Bad2("a Draco symbol stream declares " + maxBitLength.ToString(CultureInfo.InvariantCulture) +
                " bits per symbol, which the format does not define");
            int numSymbols = Var("symbols");
            int precisionBits = (3 * maxBitLength) / 2;
            if (precisionBits > 20) precisionBits = 20;
            if (precisionBits < 12) precisionBits = 12;
            int precision = 1 << precisionBits;
            int lRansBase = precision * 4;
            Table table = BuildSymbolTables(numSymbols, precision);
            int size = Var("entropy bytes");
            byte[] data = Block(size);
            Ans ans = RansInitDecoder(data, size, lRansBase);
            for (int i = 0; i < numValues; i++)
                values[i] = RansRead(ans, lRansBase, precision, table.Lut, table.Prob, table.Cum);
        }

        // ------------------------------------------------------------------ header + metadata

        private int encoderMethod, ebTraversalType;
        private int numEncodedVertices, numFaces, numAttributeData, numEncodedSymbols, numEncodedSplitSymbols;
        private int numPoints;

        private Model Run()
        {
            // draco.decoder.md ParseHeader()
            if (U8() != 'D' || U8() != 'R' || U8() != 'A' || U8() != 'C' || U8() != 'O')
                throw Bad(what + " is marked as Draco-compressed but its data does not start with 'DRACO'; " +
                "the file is corrupt, so download or export it again");
            int major = U8(), minor = U8();
            int encoderType = U8();
            encoderMethod = U8();
            int flags = U16();
            if (major != 2)
                throw Bad(what + " uses Draco bitstream version " + major.ToString(CultureInfo.InvariantCulture) +
                "." + minor.ToString(CultureInfo.InvariantCulture) + "; this mod reads version 2 (2.2 and " +
                "the versions before it), which is what every current exporter writes - export it again " +
                "with an up-to-date tool");
            if (encoderType != 1)
                throw Bad(what + " holds a Draco POINT CLOUD, not a mesh, so it has no faces to build; " +
                "export the object as a mesh");
            if (encoderMethod != MESH_SEQUENTIAL_ENCODING && encoderMethod != MESH_EDGEBREAKER_ENCODING)
                throw Bad(what + " uses Draco connectivity method " +
                encoderMethod.ToString(CultureInfo.InvariantCulture) + ", which the format does not define; " +
                "the file is corrupt, so download or export it again");
            if ((flags & METADATA_FLAG_MASK) != 0) DecodeMetadata();

            DecodeConnectivityData();
            Model model = DecodeAttributeData();
            model.Method = encoderMethod == MESH_EDGEBREAKER_ENCODING
                ? (ebTraversalType == VALENCE_EDGEBREAKER ? "edgebreaker/valence" : "edgebreaker/standard")
                : "sequential";
            return model;
        }

        /// <summary>metadata.decoder.md - read and DISCARDED: it names things, it does not shape geometry.</summary>
        private void DecodeMetadata()
        {
            int count = Var("metadata blocks");
            if (count > MaxAttributes) throw Bad2("a Draco stream declares more metadata than this mod will read");
            for (int i = 0; i < count; i++) { Var("metadata id"); DecodeMetadataElement(0); }
            DecodeMetadataElement(0);
        }

        private void DecodeMetadataElement(int depth)
        {
            if (depth > 8) throw Bad2("a Draco stream nests its metadata deeper than this mod will read");
            int entries = Var("metadata entries");
            if (entries > MaxSymbols) throw Bad2("a Draco stream declares more metadata entries than it can hold");
            for (int i = 0; i < entries; i++) { Block(U8()); Block(U8()); }
            int sub = Var("metadata blocks");
            if (sub > MaxAttributes) throw Bad2("a Draco stream declares more metadata than this mod will read");
            for (int i = 0; i < sub; i++) { Block(U8()); DecodeMetadataElement(depth + 1); }
        }

        // ------------------------------------------------------------------ connectivity

        private List<int>[] faceToVertex;
        private int[] oppositeCorners;
        private int[] cornerToVertex;                        // the POSITION corner table
            private int[] vertexCorners;
        private bool[] isVertHole;
        private int[] vertexValences;
        private readonly List<int> activeCornerStack = new List<int>();
        private readonly List<int> topologySplitId = new List<int>();
        private readonly List<int> splitActiveCorners = new List<int>();
        private readonly List<int> sourceSymbolId = new List<int>();
        private readonly List<int> splitSymbolId = new List<int>();
        private readonly List<int> sourceEdgeBit = new List<int>();
        private int lastSymbol, activeContext, lastVertAdded;

        private byte[] ebSymbolBuffer;
        private int ebSymbolBits;
        private int ebStartFaceProbZero;
        private byte[] ebStartFaceBuffer;
        private int[] attributeConnectivityProbZero;
        private byte[][] attributeConnectivityBuffer;
        private int[] ebvContextCounters;
        private uint[][] ebvContextSymbols;

        private void DecodeConnectivityData()
        {
            if (encoderMethod == MESH_SEQUENTIAL_ENCODING) DecodeSequentialConnectivityData();
            else DecodeEdgebreakerConnectivityData();
        }

        // ---- sequential.decoder.md ----

        private void DecodeSequentialConnectivityData()
        {
            numFaces = Var("faces");
            Points(Var("points"));
            int method = U8();
            Faces(numFaces);
            if (method == SEQUENTIAL_COMPRESSED_INDICES)
            {
                uint[] symbols = DecodeSymbols(numFaces * 3, 1);
                long last = 0;
                for (int i = 0; i < numFaces; i++)
                    for (int j = 0; j < 3; j++)
                {
                    uint encoded = symbols[i * 3 + j];
                    long diff = encoded >> 1;
                    if ((encoded & 1) != 0) diff = -diff;
                    last += diff;
                    faceToVertex[j].Add(Point(last));
                }
            }
            else if (method == SEQUENTIAL_UNCOMPRESSED_INDICES)
            {
                for (int i = 0; i < numFaces; i++)
                    for (int j = 0; j < 3; j++)
                {
                    long value = numPoints < 256 ? U8()
                        : numPoints < (1 << 16) ? U16()
                        : numPoints < (1 << 21) ? Var("indices")
                        : (long)U32();
                    faceToVertex[j].Add(Point(value));
                }
            }
            else throw Bad2("a Draco stream stores its indices in form " +
                method.ToString(CultureInfo.InvariantCulture) + ", which the format does not define");
        }

        /// <summary>
        /// The point count, checked BEFORE anything is sized from it. The SEQUENTIAL header states it
        /// outright in five bytes, so an unchecked one is a hostile file's cheapest way to make the
        /// game allocate for two billion points (GenerateSequence fills a list with one entry per
        /// point long before any per-attribute ceiling is reached). The edgebreaker path counts its
        /// own points off the decoded corners and goes through here too, so there is ONE guard rather
        /// than one per producer. The ceiling is the importer's OWN vertex ceiling, not a second one:
        /// these points BECOME the mesh's vertices, which <see cref="GlbReader"/> already refuses past
        /// <see cref="GlbReader.MaxVertices"/>.
        /// </summary>
        private void Points(long count)
        {
            if (count < 0 || count > GlbReader.MaxVertices)
                throw Bad(what + "'s Draco geometry declares " + count.ToString(CultureInfo.InvariantCulture) +
                " vertices, past the " + GlbReader.MaxVertices.ToString(CultureInfo.InvariantCulture) +
                " this mod will read; decimate the mesh and export it again");
            numPoints = (int)count;
        }

        private int Point(long value)
        {
            if (value < 0 || value >= numPoints)
                throw Bad2("a Draco face names point " + value.ToString(CultureInfo.InvariantCulture) +
                " of " + numPoints.ToString(CultureInfo.InvariantCulture));
            return (int)value;
        }

        private void Faces(int count)
        {
            if (count < 0 || count > MaxFaces)
                throw Bad(what + "'s Draco geometry declares " + count.ToString(CultureInfo.InvariantCulture) +
                " triangles, past the " + MaxFaces.ToString(CultureInfo.InvariantCulture) +
                " this mod will read; decimate the mesh and export it again");
            faceToVertex = new List<int>[3];
            for (int i = 0; i < 3; i++) faceToVertex[i] = new List<int>(count);
        }

        // ---- edgebreaker.decoder.md ----

        private void DecodeEdgebreakerConnectivityData()
        {
            ebTraversalType = U8();
            numEncodedVertices = Var("vertices");
            numFaces = Var("faces");
            numAttributeData = U8();
            numEncodedSymbols = Var("symbols");
            numEncodedSplitSymbols = Var("split symbols");
            if (ebTraversalType != STANDARD_EDGEBREAKER && ebTraversalType != VALENCE_EDGEBREAKER)
                throw Bad(what + " uses the Draco '" + (ebTraversalType == 1 ? "predictive" : "unknown") +
                "' edgebreaker traversal (type " + ebTraversalType.ToString(CultureInfo.InvariantCulture) +
                "), which the published Draco specification does not describe and this mod therefore " +
                "will not guess at; export the model again with an up-to-date tool, which writes the " +
                "standard or valence traversal");
            Faces(numFaces);
            if (numEncodedSymbols < 0 || numEncodedSymbols > numFaces)
                throw Bad2("a Draco stream declares more edgebreaker symbols than faces");
            if (numEncodedVertices < 0 || numEncodedSplitSymbols < 0 ||
                (long)numEncodedVertices + numEncodedSplitSymbols > (long)numFaces * 3 + 3)
                throw Bad2("a Draco stream declares more vertices than its faces can carry");
            if (numAttributeData < 0 || numAttributeData > MaxAttributes)
                throw Bad2("a Draco stream declares more attribute channels than this mod will read");

            int corners = numFaces * 3;
            oppositeCorners = new int[corners];
            for (int i = 0; i < corners; i++) oppositeCorners[i] = kInvalidCornerIndex;
            cornerToVertex = new int[corners];
            for (int c = 0; c < corners; c++) cornerToVertex[c] = -1;
            int verts = numEncodedVertices + numEncodedSplitSymbols;
            vertexCorners = new int[verts];
            for (int i = 0; i < verts; i++) vertexCorners[i] = kInvalidCornerIndex;
            vertexValences = new int[verts];

            DecodeTopologySplitEvents();
            EdgebreakerTraversalStart();
            DecodeEdgeBreakerConnectivity();
        }

        private void DecodeTopologySplitEvents()
        {
            int count = Var("topology splits");
            if (count < 0 || count > numFaces + 1)
                throw Bad2("a Draco stream declares more topology splits than faces");
            var sourceDelta = new int[count];
            var splitDelta = new int[count];
            for (int i = 0; i < count; i++) { sourceDelta[i] = Var("split ids"); splitDelta[i] = Var("split ids"); }
            StartBits();
            for (int i = 0; i < count; i++) sourceEdgeBit.Add((int)ReadBits(1));
            EndBits();
            long last = 0;
            for (int i = 0; i < count; i++)
            {
                long source = sourceDelta[i] + last;
                sourceSymbolId.Add((int)source);
                splitSymbolId.Add((int)(source - splitDelta[i]));
                last = source;
            }
        }

        private void EdgebreakerTraversalStart()
        {
            lastSymbol = -1;
            activeContext = -1;
            if (ebTraversalType == STANDARD_EDGEBREAKER)
            {
                int size = Var("symbol bytes");
                ebSymbolBuffer = Block(size);
                ebSymbolBits = 0;
                ParseStandardFaceData();
                ParseAttributeConnectivityData();
            }
            else
            {
                ParseStandardFaceData();
                ParseAttributeConnectivityData();
                ebvContextCounters = new int[NUM_UNIQUE_VALENCES];
                ebvContextSymbols = new uint[NUM_UNIQUE_VALENCES][];
                for (int i = 0; i < NUM_UNIQUE_VALENCES; i++)
                {
                    ebvContextCounters[i] = Var("valence symbols");
                    if (ebvContextCounters[i] < 0 || ebvContextCounters[i] > numFaces + 1)
                        throw Bad2("a Draco valence context declares more symbols than faces");
                    ebvContextSymbols[i] = ebvContextCounters[i] > 0
                        ? DecodeSymbols(ebvContextCounters[i], 1)
                        : new uint[0];
                }
            }
        }

        private void ParseStandardFaceData()
        {
            ebStartFaceProbZero = U8();
            ebStartFaceBuffer = Block(Var("face bytes"));
        }

        private void ParseAttributeConnectivityData()
        {
            attributeConnectivityProbZero = new int[numAttributeData];
            attributeConnectivityBuffer = new byte[numAttributeData][];
            for (int i = 0; i < numAttributeData; i++)
            {
                attributeConnectivityProbZero[i] = U8();
                attributeConnectivityBuffer[i] = Block(Var("attribute bytes"));
            }
        }

        private void DecodeEdgeBreakerConnectivity()
        {
            int verts = numEncodedVertices + numEncodedSplitSymbols;
            isVertHole = new bool[verts];
            for (int i = 0; i < verts; i++) isVertHole[i] = true;
            lastVertAdded = -1;
            for (int i = 0; i < numEncodedSymbols; i++)
            {
                EdgebreakerDecodeSymbol();
                NewActiveCornerReached(3 * i, i);
            }
            ProcessInteriorEdges();
        }

        private void EdgebreakerDecodeSymbol()
        {
            if (ebTraversalType == VALENCE_EDGEBREAKER)
            {
                // edgebreaker.traversal.valence.md EdgebreakerValenceDecodeSymbol()
                if (activeContext != -1)
                {
                    int left = --ebvContextCounters[activeContext];
                    if (left < 0) throw Bad2("a Draco valence context ran out of symbols");
                    uint symbol = ebvContextSymbols[activeContext][left];
                    if (symbol >= SymbolToTopology.Length)
                        throw Bad2("a Draco valence context names topology symbol " +
                        symbol.ToString(CultureInfo.InvariantCulture));
                    lastSymbol = SymbolToTopology[symbol];
                }
                else lastSymbol = TOPOLOGY_E;
                return;
            }
            // edgebreaker.decoder.md ParseEdgebreakerStandardSymbol(): one bit for C, three for the rest.
            int value = (int)SymbolBits(1);
            if (value != TOPOLOGY_C) value |= (int)SymbolBits(2) << 1;
            lastSymbol = value;
        }

        /// <summary>Bits from the standard traversal's OWN buffer, which is not the main stream.</summary>
        private uint SymbolBits(int count)
        {
            uint value = 0;
            for (int bit = 0; bit < count; bit++)
            {
                int at = ebSymbolBits >> 3;
                int one = at < ebSymbolBuffer.Length ? (ebSymbolBuffer[at] >> (ebSymbolBits & 7)) & 1 : 0;
                value |= (uint)one << bit;
                ebSymbolBits++;
            }
            return value;
        }

        private static int Next(int corner) => corner < 0 ? corner : (corner % 3) == 2 ? corner - 2 : corner + 1;
        private static int Previous(int corner) => corner < 0 ? corner : (corner % 3) == 0 ? corner + 2 : corner - 1;

        private int PosOpposite(int c) =>
            c < 0 || c >= oppositeCorners.Length ? kInvalidCornerIndex : oppositeCorners[c];

        private void SetOppositeCorners(int c, int opp)
        {
            if (c < 0 || c >= oppositeCorners.Length || opp < 0 || opp >= oppositeCorners.Length)
                throw Bad2("a Draco face names corner " + c.ToString(CultureInfo.InvariantCulture) + "/" +
                opp.ToString(CultureInfo.InvariantCulture) + " of " +
                oppositeCorners.Length.ToString(CultureInfo.InvariantCulture));
            oppositeCorners[c] = opp;
            oppositeCorners[opp] = c;
        }

        /// <summary>
        /// A corner's vertex. This does NOT touch the vertex's left-most corner, and that is the whole
        /// point: the specification's MapCornerToVertex() sets <c>vertex_corners_[vert] = corner</c>
        /// as a side effect, the reference's does not (corner_table.h: two lines, only the map), and
        /// the left-most corner is written ONLY by the explicit SetLeftMostCorner() calls the
        /// edgebreaker makes - which the specification does not mention at all. MEASURED: with the
        /// spec's version, seven pairs of seam points on Khronos' Avocado came back numbered the other
        /// way round, because a vertex's deduplication walk starts at its left-most corner.
        /// </summary>
        private void MapCornerToVertex(int corner, int vert)
        {
            if (corner < 0 || corner >= cornerToVertex.Length)
                throw Bad2("a Draco face names corner " + corner.ToString(CultureInfo.InvariantCulture));
            int face = corner / 3;
            if (face >= faceToVertex[0].Count)
                throw Bad2("a Draco stream maps corner " + corner.ToString(CultureInfo.InvariantCulture) +
                " of a face it has not built");
            cornerToVertex[corner] = vert;
            faceToVertex[corner % 3][face] = vert;
        }

        /// <summary>corner_table.h SetLeftMostCorner(): the ONLY writer of the vertex-to-corner map.</summary>
        private void SetLeftMost(int vert, int corner)
        {
            if (vert < 0) return;
            if (vert >= vertexCorners.Length)
                throw Bad2("a Draco face names vertex " + vert.ToString(CultureInfo.InvariantCulture) +
                " of " + vertexCorners.Length.ToString(CultureInfo.InvariantCulture));
            vertexCorners[vert] = corner;
        }

        private int Vert(int vert)
        {
            if (vert < 0 || vert >= vertexCorners.Length)
                throw Bad2("a Draco face names vertex " + vert.ToString(CultureInfo.InvariantCulture) +
                " of " + vertexCorners.Length.ToString(CultureInfo.InvariantCulture));
            return vert;
        }

        private void PushFace(int v, int n, int prev)
        {
            if (faceToVertex[0].Count >= numFaces)
                throw Bad2("a Draco stream builds more faces than it declares");
            faceToVertex[0].Add(v);
            faceToVertex[1].Add(n);
            faceToVertex[2].Add(prev);
        }

        private bool IsTopologySplit(int encoderSymbolId, out int outFaceEdge, out int outSplitId)
        {
            outFaceEdge = 0; outSplitId = 0;
            if (sourceSymbolId.Count == 0 || sourceSymbolId[sourceSymbolId.Count - 1] != encoderSymbolId)
                return false;
            outFaceEdge = sourceEdgeBit[sourceEdgeBit.Count - 1];
            sourceEdgeBit.RemoveAt(sourceEdgeBit.Count - 1);
            outSplitId = splitSymbolId[splitSymbolId.Count - 1];
            splitSymbolId.RemoveAt(splitSymbolId.Count - 1);
            sourceSymbolId.RemoveAt(sourceSymbolId.Count - 1);
            return true;
        }

        /// <summary>edgebreaker.decoder.md NewActiveCornerReached(), symbol by symbol.</summary>
        private void NewActiveCornerReached(int newCorner, int symbolId)
        {
            bool checkTopologySplit = false;
            int vert, next, prev;
            switch (lastSymbol)
            {
                case TOPOLOGY_C:
                {
                    int cornerA = Top();
                    int cornerB = Previous(cornerA);
                    int guard = 0;
                    while (PosOpposite(cornerB) >= 0)
                    {
                        cornerB = Previous(PosOpposite(cornerB));
                        if (++guard > oppositeCorners.Length) throw Bad2("a Draco vertex fan does not close");
                    }
                    SetOppositeCorners(cornerA, newCorner + 1);
                    SetOppositeCorners(cornerB, newCorner + 2);
                    activeCornerStack[activeCornerStack.Count - 1] = newCorner;
                    vert = CornerToVertPos(Next(cornerA));
                    next = CornerToVertPos(Next(cornerB));
                    prev = CornerToVertPos(Previous(cornerA));
                }
                if (ebTraversalType == VALENCE_EDGEBREAKER)
                {
                    vertexValences[Vert(next)] += 1;
                    vertexValences[Vert(prev)] += 1;
                }
                PushFace(vert, next, prev);
                isVertHole[Vert(vert)] = false;
                SetLeftMost(Vert(prev), newCorner + 2);
                break;

                case TOPOLOGY_S:
                {
                    int cornerB = Pop();
                    for (int i = 0; i < topologySplitId.Count; i++)
                        if (topologySplitId[i] == symbolId) activeCornerStack.Add(splitActiveCorners[i]);
                    int cornerA = Top();
                    SetOppositeCorners(cornerA, newCorner + 2);
                    SetOppositeCorners(cornerB, newCorner + 1);
                    activeCornerStack[activeCornerStack.Count - 1] = newCorner;

                    vert = CornerToVertPos(Previous(cornerA));
                    next = CornerToVertPos(Next(cornerA));
                    prev = CornerToVertPos(Previous(cornerB));
                    int cornerN = Next(cornerB);
                    int vertexN = CornerToVertPos(cornerN);
                    if (ebTraversalType == VALENCE_EDGEBREAKER)
                        vertexValences[Vert(vert)] += vertexValences[Vert(vertexN)];
                    if (ebTraversalType == VALENCE_EDGEBREAKER)
                    {
                        vertexValences[Vert(next)] += 1;
                        vertexValences[Vert(prev)] += 1;
                    }
                    PushFace(vert, next, prev);
                    SetLeftMost(Vert(prev), newCorner + 2);
                    // The merge, as the reference performs it: the left-most corner of the vertex
                    // that survives becomes the one of the vertex that disappears, and the whole
                    // fan around the old vertex is re-mapped by swinging LEFT from it. The spec's
                    // version instead rewrites every face in the mesh (ReplaceVerts) and swings
                    // from a different corner - same faces, different left-most corners.
                    SetLeftMost(Vert(vert), vertexCorners[Vert(vertexN)]);
                    int first = cornerN;
                    while (cornerN >= 0)
                    {
                        MapCornerToVertex(cornerN, vert);
                        cornerN = SwingLeftAttr(-1, cornerN);
                        if (cornerN == first) throw Bad2("a Draco merge swings back on itself");
                    }
                    vertexCorners[Vert(vertexN)] = kInvalidCornerIndex;
                }
                break;

                case TOPOLOGY_R:
                {
                    int cornerA = Top();
                    SetOppositeCorners(newCorner + 2, cornerA);
                    activeCornerStack[activeCornerStack.Count - 1] = newCorner;
                    checkTopologySplit = true;
                    vert = CornerToVertPos(Previous(cornerA));
                    next = CornerToVertPos(Next(cornerA));
                    prev = ++lastVertAdded;
                }
                if (ebTraversalType == VALENCE_EDGEBREAKER)
                {
                    vertexValences[Vert(vert)] += 1;
                    vertexValences[Vert(next)] += 1;
                    vertexValences[Vert(prev)] += 2;
                }
                PushFace(vert, next, prev);
                // R: the new vertex sits at corner+2 and the vertex at corner ('r') gets a new
                // left-most corner too - eb_impl.cc SetLeftMostCorner(new_vert_index, opp_corner)
                // and SetLeftMostCorner(vertex_r, corner_r).
                SetLeftMost(Vert(prev), newCorner + 2);
                SetLeftMost(Vert(vert), newCorner);
                break;

                case TOPOLOGY_L:
                {
                    int cornerA = Top();
                    SetOppositeCorners(newCorner + 1, cornerA);
                    activeCornerStack[activeCornerStack.Count - 1] = newCorner;
                    checkTopologySplit = true;
                    vert = CornerToVertPos(Next(cornerA));
                    next = ++lastVertAdded;
                    prev = CornerToVertPos(Previous(cornerA));
                }
                if (ebTraversalType == VALENCE_EDGEBREAKER)
                {
                    vertexValences[Vert(vert)] += 1;
                    vertexValences[Vert(next)] += 2;
                    vertexValences[Vert(prev)] += 1;
                }
                PushFace(vert, next, prev);
                // L: the new vertex sits at corner+1, and 'r' is corner+2.
                SetLeftMost(Vert(next), newCorner + 1);
                SetLeftMost(Vert(prev), newCorner + 2);
                break;

                case TOPOLOGY_E:
                activeCornerStack.Add(newCorner);
                checkTopologySplit = true;
                vert = lastVertAdded + 1;
                next = vert + 1;
                prev = next + 1;
                if (ebTraversalType == VALENCE_EDGEBREAKER)
                {
                    vertexValences[Vert(vert)] += 2;
                    vertexValences[Vert(next)] += 2;
                    vertexValences[Vert(prev)] += 2;
                }
                PushFace(vert, next, prev);
                lastVertAdded = prev;
                SetLeftMost(Vert(vert), newCorner);
                SetLeftMost(Vert(next), newCorner + 1);
                SetLeftMost(Vert(prev), newCorner + 2);
                break;

                default:
                throw Bad2("a Draco stream names topology symbol " +
                    lastSymbol.ToString(CultureInfo.InvariantCulture));
            }

            if (ebTraversalType == VALENCE_EDGEBREAKER)
            {
                int valence = vertexValences[Vert(next)];
                int clamped = valence < MIN_VALENCE ? MIN_VALENCE : valence > MAX_VALENCE ? MAX_VALENCE : valence;
                activeContext = clamped - MIN_VALENCE;
            }

            if (!checkTopologySplit) return;
            int encoderSymbolId = numEncodedSymbols - symbolId - 1;
            int splitEdge, encSplitId;
            while (IsTopologySplit(encoderSymbolId, out splitEdge, out encSplitId))
            {
                int actTopCorner = Top();
                int newActive = splitEdge == RIGHT_FACE_EDGE ? Next(actTopCorner) : Previous(actTopCorner);
                topologySplitId.Add(numEncodedSymbols - encSplitId - 1);
                splitActiveCorners.Add(newActive);
            }
        }

        private int Top()
        {
            if (activeCornerStack.Count == 0) throw Bad2("a Draco stream reads an empty corner stack");
            return activeCornerStack[activeCornerStack.Count - 1];
        }

        private int Pop()
        {
            int value = Top();
            activeCornerStack.RemoveAt(activeCornerStack.Count - 1);
            return value;
        }

        /// <summary>edgebreaker.decoder.md ProcessInteriorEdges(), which closes the remaining faces.</summary>
        private void ProcessInteriorEdges()
        {
            Ans ans = RansInitDecoder(ebStartFaceBuffer, ebStartFaceBuffer.Length, L_RANS_BASE);
            while (activeCornerStack.Count > 0)
            {
                int cornerA = Pop();
                if (!RabsDescRead(ans, ebStartFaceProbZero)) continue;
                // The other two corners of the hole, found through the LEFT-MOST corner of each
                // vertex (eb_impl.cc: Next(LeftMostCorner(vert))). The spec instead walks the
                // opposite-corner chain to the boundary; the two agree only while the left-most
                // corners are right, so the one that DEFINES them is used.
                int vertN = CornerToVertPos(Next(cornerA));
                int cornerB = Next(vertexCorners[Vert(vertN)]);
                int vertX = CornerToVertPos(Next(cornerB));
                int cornerC = Next(vertexCorners[Vert(vertX)]);
                int vertP = CornerToVertPos(Next(cornerC));
                if (cornerA == cornerB || cornerA == cornerC || cornerB == cornerC)
                    throw Bad2("a Draco hole names the same corner twice");
                int newCorner = faceToVertex[0].Count * 3;
                SetOppositeCorners(newCorner, cornerA);
                SetOppositeCorners(newCorner + 1, cornerB);
                SetOppositeCorners(newCorner + 2, cornerC);
                PushFace(vertX, vertP, vertN);
                isVertHole[Vert(vertX)] = false;
                isVertHole[Vert(vertP)] = false;
                isVertHole[Vert(vertN)] = false;
            }
            if (faceToVertex[0].Count != numFaces)
                throw Bad2("a Draco stream built " + faceToVertex[0].Count.ToString(CultureInfo.InvariantCulture) +
                " of the " + numFaces.ToString(CultureInfo.InvariantCulture) + " faces it declares");
        }

        // ------------------------------------------------------------------ corners (corner.md)

        private int currAttDec, currAtt;
        private int[] attDecDecoderType, attDecTraversalMethod;
        private List<int>[][] attrFaceToVertex;    // [attr][3]

            private void CornerToVertsInternal(List<int>[] ftv, int corner, out int v, out int n, out int prev)
        {
            if (corner < 0) throw Bad2("a Draco stream reads corner " + corner.ToString(CultureInfo.InvariantCulture));
            int local = corner % 3, face = corner / 3;
            if (face >= ftv[0].Count)
                throw Bad2("a Draco stream reads face " + face.ToString(CultureInfo.InvariantCulture) + " of " +
                ftv[0].Count.ToString(CultureInfo.InvariantCulture));
            if (local == 0) { v = ftv[0][face]; n = ftv[1][face]; prev = ftv[2][face]; }
            else if (local == 1) { v = ftv[1][face]; n = ftv[2][face]; prev = ftv[0][face]; }
            else { v = ftv[2][face]; n = ftv[0][face]; prev = ftv[1][face]; }
        }

        /// <summary>
        /// Which ATTRIBUTE CONNECTIVITY TABLE a decoder reads through, or -1 for the position table.
        ///
        /// This is the one place the published specification cannot be followed literally, and it is
        /// worth spelling out because everything downstream depends on it. The spec indexes every
        /// attribute table by <c>curr_att_dec - 1</c>, i.e. it assumes decoder 1 owns attribute
        /// channel 0, decoder 2 owns channel 1, and so on. The bitstream does not work that way: each
        /// decoder names its channel in <c>att_dec_data_id</c>, which is a SIGNED byte (the reference
        /// reads <c>int8_t</c>; the spec's table says UI8), and a NEGATIVE id means "this decoder
        /// carries the positions and reads the position corner table". MEASURED on Khronos' own CC0
        /// Avocado, whose four decoders arrive as data ids 2, -1, 0, 1 - under the spec's assumption
        /// the texture coordinates would be read through the tangent's seams. A decoder whose id is
        /// non-negative but whose type is MESH_VERTEX_ATTRIBUTE also reads the POSITION table; the
        /// reference marks its channel's connectivity "not used" for exactly that reason.
        /// </summary>
        private int Attr(int attDec) =>
            // null while the connectivity is still being decoded: there is only the position table then.
        attDecDataId != null && attrFaceToVertex != null &&
            encoderMethod == MESH_EDGEBREAKER_ENCODING && attDecDataId[attDec] >= 0 &&
            attDecDecoderType[attDec] == MESH_CORNER_ATTRIBUTE ? attDecDataId[attDec] : -1;

        private void CornerToVerts(int attDec, int corner, out int v, out int n, out int prev)
        {
            int attr = Attr(attDec);
            if (attr < 0) CornerToVertsInternal(faceToVertex, corner, out v, out n, out prev);
            else CornerToVertsInternal(attrFaceToVertex[attr], corner, out v, out n, out prev);
        }

        private void CornerToVertsPos(int corner, out int v, out int n, out int prev) =>
            CornerToVertsInternal(faceToVertex, corner, out v, out n, out prev);

        private int CornerToVertPos(int corner)
        {
            int v, n, prev;
            CornerToVertsPos(corner, out v, out n, out prev);
            return v;
        }

        private int CornerToVert(int attDec, int corner)
        {
            int v, n, prev;
            CornerToVerts(attDec, corner, out v, out n, out prev);
            return v;
        }

        private bool IsCornerOppositeToSeamEdge(int attr, int corner) =>
            corner >= 0 && attr >= 0 && isEdgeOnSeam[attr][corner];

        private bool IsCornerOppositeToSeamEdge(int corner) =>
            IsCornerOppositeToSeamEdge(Attr(currAttDec), corner);

        private int Opposite(int attDec, int c) => OppositeAttr(Attr(attDec), c);

        private int OppositeAttr(int attr, int c) =>
            attr >= 0 && IsCornerOppositeToSeamEdge(attr, c) ? kInvalidCornerIndex : PosOpposite(c);

        private int GetLeftCorner(int corner) => corner < 0 ? kInvalidCornerIndex : PosOpposite(Previous(corner));
        private int GetRightCorner(int corner) => corner < 0 ? kInvalidCornerIndex : PosOpposite(Next(corner));
        private int SwingRight(int attDec, int corner) => Previous(Opposite(attDec, Previous(corner)));
        private int SwingLeft(int attDec, int corner) => Next(Opposite(attDec, Next(corner)));
        private int SwingRightAttr(int attr, int corner) => Previous(OppositeAttr(attr, Previous(corner)));
        private int SwingLeftAttr(int attr, int corner) => Next(OppositeAttr(attr, Next(corner)));

        // ------------------------------------------------------------------ attributes

        private int numAttributesDecoders;
        private int[] attDecDataId;
        private int[] attDecNumAttributes;
        private int[][] attType, attDataType, attNumComponents, attNormalized, attUniqueId, seqDecoderType;
        private int[][] predScheme, predTransform;
        private int[][] numValuesToDecode;
        private int[][] quantBits;
        private float[][][] quantMin;
        private float[][] quantRange;
        private int[][] normalMaxQ;
        private int[][] wrapMin, wrapMax;
        private int[][][] symbolsToSignedInts;
        private int[][][] originalValues;
        private float[][][] dequantizedValues;
        private List<bool>[][] texOrientations;
        private bool[][][] flipNormalBits;
        private bool[][] genericValues;
        private List<bool>[][][] creaseEdges;      // [attDec][att][parallelogram]

            private bool[][] isEdgeOnSeam;          // [attribute channel][corner]
            private int[][] attrCornerToVertex;     // [attribute channel][corner]
            private List<int>[] seamSrc, seamDest;
        private int[] cornerToPointMap;
        private int[] vertexVisitedPointIds;
        private List<int>[] valueIndexToCorner;    // [attDec]
            private int[][] vertexToValueIndex;        // [attDec][vertex]
            private List<int>[] vertexToLeftMostCorner; // [attr]
            private int[][] indicesMap;                            // [attDec][point]
            private bool[] isFaceVisited, isVertexVisited;
        private int[] predictionDegree;
        private List<int>[] traversalStacks;
        private int bestPriority;
        private readonly List<int> cornerTraversalStack = new List<int>();

        private void ParseAttributeDecodersData()
        {
            numAttributesDecoders = U8();
            if (numAttributesDecoders < 1 || numAttributesDecoders > MaxAttributes)
                throw Bad2("a Draco stream declares " + numAttributesDecoders.ToString(CultureInfo.InvariantCulture) +
                " attribute decoders");
            attDecDataId = new int[numAttributesDecoders];
            attDecDecoderType = new int[numAttributesDecoders];
            attDecTraversalMethod = new int[numAttributesDecoders];
            if (encoderMethod == MESH_EDGEBREAKER_ENCODING)
            {
                if (numAttributesDecoders != numAttributeData + 1)
                    throw Bad2("a Draco stream declares " + numAttributesDecoders.ToString(CultureInfo.InvariantCulture) +
                    " attribute decoders for " + numAttributeData.ToString(CultureInfo.InvariantCulture) +
                    " attribute channels");
                for (int i = 0; i < numAttributesDecoders; i++)
                {
                    attDecDataId[i] = I8();
                    attDecDecoderType[i] = U8();
                    attDecTraversalMethod[i] = U8();
                    if (attDecTraversalMethod[i] != MESH_TRAVERSAL_DEPTH_FIRST &&
                        attDecTraversalMethod[i] != MESH_TRAVERSAL_PREDICTION_DEGREE)
                        throw Bad2("a Draco attribute uses traversal method " +
                        attDecTraversalMethod[i].ToString(CultureInfo.InvariantCulture));
                }
            }
            attDecNumAttributes = new int[numAttributesDecoders];
            attType = New(numAttributesDecoders); attDataType = New(numAttributesDecoders);
            attNumComponents = New(numAttributesDecoders); attNormalized = New(numAttributesDecoders);
            attUniqueId = New(numAttributesDecoders); seqDecoderType = New(numAttributesDecoders);
            for (int i = 0; i < numAttributesDecoders; i++)
            {
                int count = Var("attributes");
                if (count < 0 || count > MaxAttributes)
                    throw Bad2("a Draco decoder declares " + count.ToString(CultureInfo.InvariantCulture) + " attributes");
                attDecNumAttributes[i] = count;
                attType[i] = new int[count]; attDataType[i] = new int[count];
                attNumComponents[i] = new int[count]; attNormalized[i] = new int[count];
                attUniqueId[i] = new int[count]; seqDecoderType[i] = new int[count];
                for (int j = 0; j < count; j++)
                {
                    attType[i][j] = U8();
                    attDataType[i][j] = U8();
                    attNumComponents[i][j] = U8();
                    attNormalized[i][j] = U8();
                    attUniqueId[i][j] = Var("attribute ids");
                    if (attNumComponents[i][j] < 1 || attNumComponents[i][j] > 4)
                        throw Bad2("a Draco attribute declares " +
                        attNumComponents[i][j].ToString(CultureInfo.InvariantCulture) + " components");
                }
                for (int j = 0; j < count; j++) seqDecoderType[i][j] = U8();
            }
            positionDecoder = -1;
            for (int i = 0; i < numAttributesDecoders; i++)
                for (int j = 0; j < attDecNumAttributes[i]; j++)
                if (attType[i][j] == ATTRIBUTE_POSITION && positionDecoder < 0)
            {
                positionDecoder = i; positionAttribute = j;
            }
        }

        /// <summary>Where the POSITION attribute lives, by declared type - not by index. See
        /// <see cref="PositionForDataId"/>.</summary>
        private int positionDecoder = -1, positionAttribute;

        private static int[][] New(int count) => new int[count][];

        /// <summary>
        /// The component count of the PORTABLE (integer) stage. A NORMALS attribute is always two
        /// (octahedral s/t) - the reference's SequentialNormalAttributeDecoder::GetNumValueComponents,
        /// not the spec's GetNumComponents(), which names PREDICTION_DIFFERENCE only. See the class remark.
        /// </summary>
        private int GetNumComponents() =>
            seqDecoderType[currAttDec][currAtt] == SEQUENTIAL_ATTRIBUTE_ENCODER_NORMALS
            ? 2 : attNumComponents[currAttDec][currAtt];

        private Model DecodeAttributeData()
        {
            ParseAttributeDecodersData();
            Allocate();
            vertexVisitedPointIds = new int[numAttributesDecoders];

            if (encoderMethod == MESH_EDGEBREAKER_ENCODING)
            {
                DecodeAttributeSeams();
                int verts = numEncodedVertices + numEncodedSplitSymbols;
                for (int i = 0; i < verts; i++) if (isVertHole[i]) UpdateVertexToCornerMap(i);
                for (int a = 0; a < numAttributeData; a++) RecomputeVerticesInternal(a);
                AssignPointsToCorners();
            }

            for (int i = 0; i < numAttributesDecoders; i++)
            {
                currAttDec = i;
                isFaceVisited = new bool[numFaces];
                isVertexVisited = new bool[numFaces * 3 + 3];
                GenerateSequence();
                if (encoderMethod == MESH_EDGEBREAKER_ENCODING) UpdatePointToAttributeIndexMapping();
            }

            for (int i = 0; i < numAttributesDecoders; i++)
                for (int j = 0; j < attDecNumAttributes[i]; j++)
                numValuesToDecode[i][j] = valueIndexToCorner[i].Count;

            for (int i = 0; i < numAttributesDecoders; i++)
            {
                currAttDec = i;
                DecodePortableAttributes();
                DecodeDataNeededByPortableTransforms();
                TransformAttributesToOriginalFormat();
            }
            return Assemble();
        }

        private void Allocate()
        {
            int decoders = numAttributesDecoders;
            numValuesToDecode = new int[decoders][];
            quantBits = new int[decoders][]; quantMin = new float[decoders][][]; quantRange = new float[decoders][];
            normalMaxQ = new int[decoders][]; wrapMin = new int[decoders][]; wrapMax = new int[decoders][];
            predScheme = new int[decoders][]; predTransform = new int[decoders][];
            symbolsToSignedInts = new int[decoders][][]; originalValues = new int[decoders][][];
            dequantizedValues = new float[decoders][][];
            texOrientations = new List<bool>[decoders][]; flipNormalBits = new bool[decoders][][];
            genericValues = new bool[decoders][];
            creaseEdges = new List<bool>[decoders][][];
            for (int i = 0; i < decoders; i++)
            {
                int count = attDecNumAttributes[i];
                numValuesToDecode[i] = new int[count];
                quantBits[i] = new int[count]; quantMin[i] = new float[count][]; quantRange[i] = new float[count];
                normalMaxQ[i] = new int[count]; wrapMin[i] = new int[count]; wrapMax[i] = new int[count];
                predScheme[i] = new int[count]; predTransform[i] = new int[count];
                symbolsToSignedInts[i] = new int[count][]; originalValues[i] = new int[count][];
                dequantizedValues[i] = new float[count][];
                texOrientations[i] = new List<bool>[count]; flipNormalBits[i] = new bool[count][];
                genericValues[i] = new bool[count];
                creaseEdges[i] = new List<bool>[count][];
            }
            valueIndexToCorner = new List<int>[decoders];
            vertexToValueIndex = new int[decoders][];
            indicesMap = new int[decoders][];
            int corners = numFaces * 3;
            for (int i = 0; i < decoders; i++)
            {
                valueIndexToCorner[i] = new List<int>();
                vertexToValueIndex[i] = new int[corners + 3];
            }
            if (encoderMethod == MESH_SEQUENTIAL_ENCODING) return;
            // Keyed by ATTRIBUTE CHANNEL, not by decoder - see Attr().
            isEdgeOnSeam = new bool[numAttributeData][];
            seamSrc = new List<int>[numAttributeData];
            seamDest = new List<int>[numAttributeData];
            attrFaceToVertex = new List<int>[numAttributeData][];
            vertexToLeftMostCorner = new List<int>[numAttributeData];
            attrCornerToVertex = new int[numAttributeData][];
            for (int a = 0; a < numAttributeData; a++) attrCornerToVertex[a] = new int[corners];
            cornerToPointMap = new int[corners];
            for (int i = 0; i < corners; i++) cornerToPointMap[i] = -1;
        }

        // ---- boundary.decoder.md ----

        private void DecodeAttributeSeams()
        {
            int seams = numAttributeData;
            var decoders = new Ans[seams];
            for (int a = 0; a < seams; a++)
            {
                decoders[a] = RansInitDecoder(attributeConnectivityBuffer[a],
                    attributeConnectivityBuffer[a].Length, L_RANS_BASE);
                isEdgeOnSeam[a] = new bool[faceToVertex[0].Count * 3];
                seamSrc[a] = new List<int>();
                seamDest[a] = new List<int>();
            }
            for (int j = 0; j < numFaces; j++)
                for (int k = 0; k < 3; k++)
            {
                int corner = j * 3 + k;
                int v, n, prev;
                CornerToVertsPos(corner, out v, out n, out prev);
                int opp = PosOpposite(corner);
                if (opp >= 0)
                {
                    if (opp < corner) continue;
                    for (int a = 0; a < seams; a++)
                        if (RabsDescRead(decoders[a], attributeConnectivityProbZero[a]))
                    {
                        seamSrc[a].Add(n); seamDest[a].Add(prev);
                        isEdgeOnSeam[a][corner] = true;
                        int oppV, oppN, oppP;
                        CornerToVertsPos(opp, out oppV, out oppN, out oppP);
                        seamSrc[a].Add(oppN); seamDest[a].Add(oppP);
                        isEdgeOnSeam[a][opp] = true;
                    }
                }
                else
                    for (int a = 0; a < seams; a++)
                {
                    seamSrc[a].Add(n); seamDest[a].Add(prev);
                    isEdgeOnSeam[a][corner] = true;
                }
            }
            // The spec's IsVertexOnAttributeSeam() is a LINEAR scan of that list per vertex, which is
            // O(vertices x seam edges) - minutes on a 40 000 vertex model. Same answer, one set.
            seamVertices = new HashSet<int>[seams];
            for (int a = 0; a < seams; a++)
            {
                seamVertices[a] = new HashSet<int>();
                for (int i = 0; i < seamSrc[a].Count; i++)
                {
                    seamVertices[a].Add(seamSrc[a][i]);
                    seamVertices[a].Add(seamDest[a][i]);
                }
            }
        }

        private HashSet<int>[] seamVertices;

        private bool IsVertexOnAttributeSeam(int attr, int vert) =>
            attr >= 0 && attr < seamVertices.Length && seamVertices[attr].Contains(vert);

        /// <summary>
        /// Is this vertex on the boundary of the table the decoder reads through? ONE rule for both
        /// tables, taken from the reference (corner_table.h and mesh_attribute_corner_table.h state
        /// the same three lines): the vertex's left-most corner, then a swing left that finds nothing.
        /// On the attribute table that swing stops at a seam edge as well as at a hole, so a seam is a
        /// boundary there by construction - which is why the specification's two-function version
        /// (IsOnPositionBoundary / IsOnAttributeBoundary, both keyed by curr_att_dec - 1) is not
        /// reproduced here: it cannot express a decoder whose channel id is not its own index.
        /// </summary>
        private bool IsOnBoundary(int attDec, int vertId)
        {
            int attr = Attr(attDec);
            if (attr < 0)
            {
                int corner = vertexCorners[Vert(vertId)];
                return corner < 0 || SwingLeftAttr(-1, corner) < 0;
            }
            List<int> map = vertexToLeftMostCorner[attr];
            if (vertId < 0 || vertId >= map.Count) return true;
            return map[vertId] < 0 || SwingLeftAttr(attr, map[vertId]) < 0;
        }

        private void UpdateVertexToCornerMap(int vert)
        {
            int firstC = vertexCorners[vert];
            if (firstC < 0) return;
            int actC = SwingLeftAttr(-1, firstC), c = firstC, guard = 0;
            while (actC >= 0 && actC != firstC)
            {
                c = actC;
                actC = SwingLeftAttr(-1, actC);
                if (++guard > oppositeCorners.Length) throw Bad2("a Draco vertex fan does not close");
            }
            if (actC != firstC) vertexCorners[vert] = c;
        }

        // ---- attributes.decoder.md ----

        /// <summary>
        /// One ATTRIBUTE channel's corner table, rebuilt from its seam edges. Runs for every channel
        /// the connectivity declares, before any decoder is read - the reference does the same, at the
        /// end of DecodeConnectivity - so it cannot depend on which decoder ends up consuming it.
        /// </summary>
        private void RecomputeVerticesInternal(int attr)
        {
            int numNewVertices = 0;
            attrFaceToVertex[attr] = new List<int>[3];
            for (int i = 0; i < 3; i++) attrFaceToVertex[attr][i] = new List<int>(faceToVertex[i]);
            vertexToLeftMostCorner[attr] = new List<int>();
            int[] map = attrCornerToVertex[attr];
            for (int i = 0; i < map.Length; i++) map[i] = -1;

            int verts = numEncodedVertices + numEncodedSplitSymbols;
            for (int v = 0; v < verts; v++)
            {
                int c = vertexCorners[v];
                if (c < 0) continue;
                int firstVertId = numNewVertices++;
                int firstC = c;
                if (IsVertexOnAttributeSeam(attr, v))
                {
                    // The reference stops this walk when it comes back to the corner it started from
                    // and calls that a corrupt file; the spec's version has no such exit at all.
                    int actLeft = SwingLeftAttr(attr, firstC);
                    while (actLeft >= 0)
                    {
                        firstC = actLeft;
                        actLeft = SwingLeftAttr(attr, actLeft);
                        if (actLeft == c) throw Bad2("a Draco attribute seam swings back on itself");
                    }
                }
                map[firstC] = firstVertId;
                vertexToLeftMostCorner[attr].Add(firstC);
                int actC = SwingRightAttr(-1, firstC), spin = 0;
                while (actC >= 0 && actC != firstC)
                {
                    int nextActC = Next(actC);
                    if (IsCornerOppositeToSeamEdge(attr, nextActC))
                    {
                        firstVertId = numNewVertices++;
                        vertexToLeftMostCorner[attr].Add(actC);
                    }
                    map[actC] = firstVertId;
                    actC = SwingRightAttr(-1, actC);
                    if (++spin > map.Length) throw Bad2("a Draco vertex fan does not close");
                }
            }

            for (int i = 0; i < map.Length; i += 3)
            {
                int face = i / 3;
                attrFaceToVertex[attr][0][face] = map[i];
                attrFaceToVertex[attr][1][face] = map[i + 1];
                attrFaceToVertex[attr][2][face] = map[i + 2];
            }
        }

        private void AssignPointsToCorners()
        {
            int count = 0;
            int verts = numEncodedVertices + numEncodedSplitSymbols;
            for (int v = 0; v < verts; v++)
            {
                int c = vertexCorners[v];
                if (c < 0) continue;
                int first = c;
                if (!isVertHole[v])
                    for (int a = 0; a < numAttributeData; a++)
                {
                    if (!IsVertexOnAttributeSeam(a, CornerToVertPos(c))) continue;
                    int vertId = attrCornerToVertex[a][c];
                    int actC = SwingRightAttr(-1, c);
                    bool found = false;
                    int guard = 0;
                    while (actC != c && actC >= 0)
                    {
                        if (attrCornerToVertex[a][actC] != vertId) { first = actC; found = true; break; }
                        actC = SwingRightAttr(-1, actC);
                        if (++guard > cornerToPointMap.Length) throw Bad2("a Draco vertex fan does not close");
                    }
                    if (found) break;
                }

                c = first;
                cornerToPointMap[c] = count++;
                int prevC = c;
                c = SwingRightAttr(-1, c);
                int spin = 0;
                while (c >= 0 && c != first)
                {
                    bool seam = false;
                    for (int a = 0; a < numAttributeData; a++)
                        if (attrCornerToVertex[a][c] != attrCornerToVertex[a][prevC]) { seam = true; break; }
                    cornerToPointMap[c] = seam ? count++ : cornerToPointMap[prevC];
                    prevC = c;
                    c = SwingRightAttr(-1, c);
                    if (++spin > cornerToPointMap.Length) throw Bad2("a Draco vertex fan does not close");
                }
            }
            Points(count);
            for (int i = 0; i < cornerToPointMap.Length; i++)
                if (cornerToPointMap[i] < 0)
                throw Bad2("a Draco stream leaves corner " + i.ToString(CultureInfo.InvariantCulture) +
                " without a point");
        }

        private void GenerateSequence()
        {
            if (encoderMethod != MESH_EDGEBREAKER_ENCODING)
            {
                for (int i = 0; i < numPoints; i++) valueIndexToCorner[currAttDec].Add(i);
                return;
            }
            if (attDecTraversalMethod[currAttDec] == MESH_TRAVERSAL_PREDICTION_DEGREE)
            {
                predictionDegree = new int[numEncodedVertices + numEncodedSplitSymbols + 3];
                traversalStacks = new List<int>[kMaxPriority];
                for (int i = 0; i < kMaxPriority; i++) traversalStacks[i] = new List<int>();
            }
            for (int i = 0; i < numFaces; i++)
            {
                if (attDecTraversalMethod[currAttDec] == MESH_TRAVERSAL_DEPTH_FIRST)
                    TraverseFromCorner(3 * i, Attr(currAttDec) >= 0);
                else PredictionDegreeTraverseFromCorner(3 * i);
            }
        }

        private bool IsFaceVisited(int face) => face < 0 || isFaceVisited[face];

        private void OnNewVertexVisited(int vertex, int corner)
        {
            valueIndexToCorner[currAttDec].Add(corner);
            if (vertex < 0 || vertex >= vertexToValueIndex[currAttDec].Length)
                throw Bad2("a Draco stream names vertex " + vertex.ToString(CultureInfo.InvariantCulture));
            vertexToValueIndex[currAttDec][vertex] = vertexVisitedPointIds[currAttDec];
            vertexVisitedPointIds[currAttDec]++;
        }

        /// <summary>
        /// edgebreaker.traversal.md EdgeBreakerTraverser_ProcessCorner() and its attribute twin. The
        /// two differ only in whether a seam edge blocks the step, so they are ONE method here -
        /// which is also why this file does not reproduce the spec's own transcription slip: the
        /// attribute version prints its `if (IsFaceVisited(right_face_id))` line with the condition
        /// missing, leaving an `else` with no `if`. Taken from the position version above it, whose
        /// body is otherwise identical.
        /// </summary>
        private void TraverseFromCorner(int cornerId, bool attribute)
        {
            // The table is always the CURRENT decoder's own - CornerToVerts picks it from Attr().
            if (IsFaceVisited(cornerId / 3)) return;
            cornerTraversalStack.Clear();
            cornerTraversalStack.Add(cornerId);
            int vertId, nextVert, prevVert;
            CornerToVerts(currAttDec, cornerId, out vertId, out nextVert, out prevVert);
            if (!Visited(nextVert)) { isVertexVisited[nextVert] = true; OnNewVertexVisited(nextVert, Next(cornerId)); }
            if (!Visited(prevVert)) { isVertexVisited[prevVert] = true; OnNewVertexVisited(prevVert, Previous(cornerId)); }

            while (cornerTraversalStack.Count > 0)
            {
                cornerId = cornerTraversalStack[cornerTraversalStack.Count - 1];
                if (cornerId < 0 || IsFaceVisited(cornerId / 3))
                {
                    cornerTraversalStack.RemoveAt(cornerTraversalStack.Count - 1);
                    continue;
                }
                while (true)
                {
                    isFaceVisited[cornerId / 3] = true;
                    vertId = CornerToVert(currAttDec, cornerId);
                    if (!Visited(vertId))
                    {
                        // ONE test, on the table this traversal reads through - the reference's
                        // corner_table()->IsOnBoundary(vert_id), whichever table that is.
                        bool onBoundary = IsOnBoundary(currAttDec, vertId);
                        isVertexVisited[vertId] = true;
                        OnNewVertexVisited(vertId, cornerId);
                        if (!onBoundary) { cornerId = GetRightCorner(cornerId); continue; }
                    }
                    int rightCorner, leftCorner;
                    if (attribute)
                    {
                        rightCorner = IsCornerOppositeToSeamEdge(Next(cornerId)) ? -1 : GetRightCorner(cornerId);
                        leftCorner = IsCornerOppositeToSeamEdge(Previous(cornerId)) ? -1 : GetLeftCorner(cornerId);
                    }
                    else
                    {
                        rightCorner = GetRightCorner(cornerId);
                        leftCorner = GetLeftCorner(cornerId);
                    }
                    int rightFace = rightCorner < 0 ? -1 : rightCorner / 3;
                    int leftFace = leftCorner < 0 ? -1 : leftCorner / 3;
                    if (IsFaceVisited(rightFace))
                    {
                        if (IsFaceVisited(leftFace))
                        {
                            cornerTraversalStack.RemoveAt(cornerTraversalStack.Count - 1);
                            break;
                        }
                        cornerId = leftCorner;
                    }
                    else if (IsFaceVisited(leftFace)) cornerId = rightCorner;
                    else
                    {
                        cornerTraversalStack[cornerTraversalStack.Count - 1] = leftCorner;
                        cornerTraversalStack.Add(rightCorner);
                        break;
                    }
                }
            }
        }

        private bool Visited(int vertex)
        {
            if (vertex < 0 || vertex >= isVertexVisited.Length)
                throw Bad2("a Draco stream names vertex " + vertex.ToString(CultureInfo.InvariantCulture));
            return isVertexVisited[vertex];
        }

        // ---- edgebreaker.traversal.prediction.degree.md ----

        private void AddCornerToTraversalStack(int ci, int priority)
        {
            traversalStacks[priority].Add(ci);
            if (priority < bestPriority) bestPriority = priority;
        }

        private int ComputePriority(int cornerId)
        {
            int vTip, nextVert, prevVert;
            CornerToVerts(currAttDec, cornerId, out vTip, out nextVert, out prevVert);
            int priority = 0;
            if (!Visited(vTip))
            {
                if (vTip >= predictionDegree.Length)
                    throw Bad2("a Draco stream names vertex " + vTip.ToString(CultureInfo.InvariantCulture));
                int degree = ++predictionDegree[vTip];
                priority = degree > 1 ? 1 : 2;
            }
            if (priority >= kMaxPriority) priority = kMaxPriority - 1;
            return priority;
        }

        private int PopNextCornerToTraverse()
        {
            for (int i = bestPriority; i < kMaxPriority; i++)
                if (traversalStacks[i].Count > 0)
            {
                int ret = traversalStacks[i][traversalStacks[i].Count - 1];
                traversalStacks[i].RemoveAt(traversalStacks[i].Count - 1);
                bestPriority = i;
                return ret;
            }
            return kInvalidCornerIndex;
        }

        private void PredictionDegreeTraverseFromCorner(int cornerId)
        {
            if (IsFaceVisited(cornerId / 3)) return;
            traversalStacks[0].Add(cornerId);
            bestPriority = 0;
            int tipVertex, nextVert, prevVert;
            CornerToVerts(currAttDec, cornerId, out tipVertex, out nextVert, out prevVert);
            if (!Visited(nextVert)) { isVertexVisited[nextVert] = true; OnNewVertexVisited(nextVert, Next(cornerId)); }
            if (!Visited(prevVert)) { isVertexVisited[prevVert] = true; OnNewVertexVisited(prevVert, Previous(cornerId)); }
            if (!Visited(tipVertex)) { isVertexVisited[tipVertex] = true; OnNewVertexVisited(tipVertex, cornerId); }

            while ((cornerId = PopNextCornerToTraverse()) >= 0)
            {
                if (IsFaceVisited(cornerId / 3)) continue;
                while (true)
                {
                    isFaceVisited[cornerId / 3] = true;
                    int vertId;
                    CornerToVerts(currAttDec, cornerId, out vertId, out nextVert, out prevVert);
                    if (!Visited(vertId)) { isVertexVisited[vertId] = true; OnNewVertexVisited(vertId, cornerId); }
                    int rightCorner = GetRightCorner(cornerId);
                    int leftCorner = GetLeftCorner(cornerId);
                    int rightFace = rightCorner < 0 ? -1 : rightCorner / 3;
                    int leftFace = leftCorner < 0 ? -1 : leftCorner / 3;
                    bool rightVisited = IsFaceVisited(rightFace), leftVisited = IsFaceVisited(leftFace);
                    if (!leftVisited)
                    {
                        int priority = ComputePriority(leftCorner);
                        if (rightVisited && priority <= bestPriority) { cornerId = leftCorner; continue; }
                        AddCornerToTraversalStack(leftCorner, priority);
                    }
                    if (!rightVisited)
                    {
                        int priority = ComputePriority(rightCorner);
                        if (priority <= bestPriority) { cornerId = rightCorner; continue; }
                        AddCornerToTraversalStack(rightCorner, priority);
                    }
                    break;
                }
            }
        }

        private void UpdatePointToAttributeIndexMapping()
        {
            var map = new int[numPoints];
            for (int i = 0; i < numPoints; i++) map[i] = -1;
            for (int f = 0; f < numFaces; f++)
                for (int c = 0; c < 3; c++)
            {
                int corner = f * 3 + c;
                int point = cornerToPointMap[corner];
                int vert, next, prev;
                CornerToVerts(currAttDec, corner, out vert, out next, out prev);
                if (point < 0 || point >= numPoints)
                    throw Bad2("a Draco corner names point " + point.ToString(CultureInfo.InvariantCulture));
                map[point] = vertexToValueIndex[currAttDec][Bounded(vert, vertexToValueIndex[currAttDec].Length)];
            }
            indicesMap[currAttDec] = map;
        }

        private int Bounded(int value, int limit)
        {
            if (value < 0 || value >= limit)
                throw Bad2("a Draco stream names entry " + value.ToString(CultureInfo.InvariantCulture) +
                " of " + limit.ToString(CultureInfo.InvariantCulture));
            return value;
        }

        // ------------------------------------------------------------------ attribute values

        /// <summary>
        /// prediction.decoder.md DecodePortableAttributes(), in the REFERENCE's order: the values are
        /// decoded whether or not the attribute carries a prediction scheme. The spec skips them for
        /// PREDICTION_NONE, which would leave the attribute empty and its bytes unread - see the class
        /// remark.
        /// </summary>
        private void DecodePortableAttributes()
        {
            for (int i = 0; i < attDecNumAttributes[currAttDec]; i++)
            {
                currAtt = i;
                if (seqDecoderType[currAttDec][i] == SEQUENTIAL_ATTRIBUTE_ENCODER_GENERIC)
                {
                    // The plain SequentialAttributeDecoder: no prediction byte, no symbols - the
                    // values arrive verbatim in the attribute's own data type, one entry after
                    // another (sequential_attribute_decoder.cc, DecodeValues: num_values entries of
                    // byte_stride = num_components x sizeof(data type)). MEASURED in the wild: it is
                    // what Khronos' own Avocado uses for its TANGENT.
                    predScheme[currAttDec][i] = PREDICTION_NONE;
                    DecodeGenericValues();
                    continue;
                }
                predScheme[currAttDec][i] = I8();
                predTransform[currAttDec][i] = PREDICTION_TRANSFORM_WRAP;
                if (predScheme[currAttDec][i] != PREDICTION_NONE)
                    predTransform[currAttDec][i] = I8();
                DecodeIntegerValues();
            }
        }

        /// <summary>An UNQUANTIZED attribute, read straight out of the stream in its own data type.</summary>
        private void DecodeGenericValues()
        {
            int components = attNumComponents[currAttDec][currAtt];
            int entries = numValuesToDecode[currAttDec][currAtt];
            int type = attDataType[currAttDec][currAtt];
            if ((long)entries * components > (long)MaxFaces * 4)
                throw Bad2("a Draco attribute declares more values than this mod will read");
            var values = new float[entries * components];
            for (int i = 0; i < values.Length; i++)
                switch (type)
            {
                case DT_INT8: values[i] = (sbyte)U8(); break;
                case DT_UINT8: values[i] = U8(); break;
                case DT_INT16: values[i] = (short)U16(); break;
                case DT_UINT16: values[i] = U16(); break;
                case DT_INT32: values[i] = (int)U32(); break;
                case DT_UINT32: values[i] = U32(); break;
                case DT_FLOAT32: values[i] = F32(); break;
                default:
                throw Bad(what + " stores an unquantized Draco attribute as number format " +
                    type.ToString(CultureInfo.InvariantCulture) + ", which this mod does not " +
                    "read; export the model again with an up-to-date tool");
            }
            dequantizedValues[currAttDec][currAtt] = values;
            genericValues[currAttDec][currAtt] = true;
        }

        private void DecodeIntegerValues()
        {
            int components = GetNumComponents();
            int entries = numValuesToDecode[currAttDec][currAtt];
            int values = entries * components;
            if (entries < 0 || (long)entries * components > (long)MaxFaces * 4)
                throw Bad2("a Draco attribute declares more values than this mod will read");

            uint[] symbols;
            int compressed = U8();
            if (compressed > 0) symbols = DecodeSymbols(values, components);
            else
            {
                // sequential_integer_attribute_decoder.cc's uncompressed branch, which the spec omits:
                // one byte width, then that many little-endian bytes per value.
                int bytes = U8();
                if (bytes < 1 || bytes > 4)
                    throw Bad2("a Draco attribute stores values " + bytes.ToString(CultureInfo.InvariantCulture) +
                    " bytes wide");
                symbols = new uint[values];
                for (int i = 0; i < values; i++)
                {
                    uint value = 0;
                    for (int b = 0; b < bytes; b++) value |= (uint)U8() << (8 * b);
                    symbols[i] = value;
                }
            }

            var signed = new int[values];
            bool correctionsPositive =
                predScheme[currAttDec][currAtt] != PREDICTION_NONE &&
                predTransform[currAttDec][currAtt] == PREDICTION_TRANSFORM_NORMAL_OCTAHEDRON_CANONICALIZED;
            for (int i = 0; i < values; i++)
                signed[i] = correctionsPositive ? (int)symbols[i] : ConvertSymbolToSignedInt(symbols[i]);
            symbolsToSignedInts[currAttDec][currAtt] = signed;

            if (predScheme[currAttDec][currAtt] == PREDICTION_NONE)
            {
                originalValues[currAttDec][currAtt] = signed;
                return;
            }
            DecodePredictionData(predScheme[currAttDec][currAtt]);
            ComputeOriginalValues(predScheme[currAttDec][currAtt], entries);
        }

        /// <summary>sequential.integer.attribute.decoder.md ConvertSymbolToSignedInt().</summary>
        private static int ConvertSymbolToSignedInt(uint val)
        {
            bool positive = (val & 1) == 0;
            int value = (int)(val >> 1);
            return positive ? value : -value - 1;
        }

        private void DecodePredictionData(int method)
        {
            if (method == MESH_PREDICTION_CONSTRAINED_MULTI_PARALLELOGRAM)
            {
                var flags = new List<bool>[kMaxNumParallelograms];
                for (int i = 0; i < kMaxNumParallelograms; i++)
                {
                    flags[i] = new List<bool>();
                    int count = Var("crease flags");
                    if (count < 0 || count > (long)numFaces * 3 + 3)
                        throw Bad2("a Draco attribute declares more crease flags than corners");
                    if (count > 0)
                    {
                        int probZero = U8();
                        byte[] data = Block(Var("crease bytes"));
                        Ans ans = RansInitDecoder(data, data.Length, L_RANS_BASE);
                        for (int j = 0; j < count; j++) flags[i].Add(RabsDescRead(ans, probZero));
                    }
                }
                creaseEdges[currAttDec][currAtt] = flags;
            }
            else if (method == MESH_PREDICTION_TEX_COORDS_PORTABLE)
            {
                int count = (int)U32();
                if (count < 0 || count > (long)numFaces * 3 + 3)
                    throw Bad2("a Draco attribute declares more texture orientations than corners");
                int probZero = U8();
                byte[] data = Block(Var("orientation bytes"));
                Ans ans = RansInitDecoder(data, data.Length, L_RANS_BASE);
                bool last = true;
                var orientations = new List<bool>(count);
                for (int i = 0; i < count; i++)
                {
                    if (!RabsDescRead(ans, probZero)) last = !last;
                    orientations.Add(last);
                }
                texOrientations[currAttDec][currAtt] = orientations;
            }
            else if (method == MESH_PREDICTION_GEOMETRIC_NORMAL)
            {
                DecodeTransformData();
                int probZero = U8();
                byte[] data = Block(Var("normal bytes"));
                Ans ans = RansInitDecoder(data, data.Length, L_RANS_BASE);
                int count = numValuesToDecode[currAttDec][currAtt];
                var flips = new bool[count];
                for (int i = 0; i < count; i++) flips[i] = RabsDescRead(ans, probZero);
                flipNormalBits[currAttDec][currAtt] = flips;
            }
            if (method != MESH_PREDICTION_GEOMETRIC_NORMAL) DecodeTransformData();
        }

        private void DecodeTransformData()
        {
            int type = predTransform[currAttDec][currAtt];
            if (type == PREDICTION_TRANSFORM_WRAP)
            {
                wrapMin[currAttDec][currAtt] = (int)U32();
                wrapMax[currAttDec][currAtt] = (int)U32();
                if (wrapMax[currAttDec][currAtt] < wrapMin[currAttDec][currAtt])
                    throw Bad2("a Draco attribute's value range runs backwards");
            }
            else if (type == PREDICTION_TRANSFORM_NORMAL_OCTAHEDRON_CANONICALIZED)
            {
                normalMaxQ[currAttDec][currAtt] = (int)U32();
                U32();   // "unused_center_value", prediction.decoder.md
                    if (normalMaxQ[currAttDec][currAtt] < 2)
                    throw Bad2("a Draco normal attribute declares a quantization of " +
                    normalMaxQ[currAttDec][currAtt].ToString(CultureInfo.InvariantCulture));
            }
            else throw Bad2("a Draco attribute uses prediction transform " +
                type.ToString(CultureInfo.InvariantCulture) + ", which the format does not define");
        }

        // ---- the transforms (prediction.wrap.transform.md, prediction.normal.transform.md) ----

        private void TransformOriginalValue(int[] pred, int predAt, int[] corr, int corrAt, int[] outv, int outAt)
        {
            if (predTransform[currAttDec][currAtt] == PREDICTION_TRANSFORM_WRAP)
            {
                int components = GetNumComponents();
                int min = wrapMin[currAttDec][currAtt], max = wrapMax[currAttDec][currAtt];
                long maxDif = 1L + max - min;
                for (int i = 0; i < components; i++)
                {
                    int predicted = pred[predAt + i];
                    int clamped = predicted > max ? max : predicted < min ? min : predicted;
                    long value = (long)clamped + corr[corrAt + i];
                    if (value > max) value -= maxDif;
                    else if (value < min) value += maxDif;
                    outv[outAt + i] = (int)value;
                }
                return;
            }
            NormalOctahedronOriginalValue(pred, predAt, corr, corrAt, outv, outAt);
        }

        private static int MostSignificantBit(int n)
        {
            int msb = -1;
            while (n != 0) { msb++; n >>= 1; }
            return msb;
        }

        private void NormalRange(out int maxQuantized, out int maxValue, out int centerValue)
        {
            int encoded = normalMaxQ[currAttDec][currAtt];
            int bits = MostSignificantBit(encoded) + 1;
            maxQuantized = (1 << bits) - 1;
            maxValue = maxQuantized - 1;
            centerValue = maxValue / 2;
        }

        private static int ModMax(int x, int center, int maxQuantized)
        {
            if (x > center) return x - maxQuantized;
            if (x < -center) return x + maxQuantized;
            return x;
        }

        private static void InvertDiamond(ref int s, ref int t, int center)
        {
            int signS, signT;
            if (s >= 0 && t >= 0) { signS = 1; signT = 1; }
            else if (s <= 0 && t <= 0) { signS = -1; signT = -1; }
            else { signS = s > 0 ? 1 : -1; signT = t > 0 ? 1 : -1; }
            int cornerS = signS * center, cornerT = signT * center;
            s = 2 * s - cornerS;
            t = 2 * t - cornerT;
            if (signS * signT >= 0) { int temp = s; s = -t; t = -temp; }
            else { int temp = s; s = t; t = temp; }
            s = (s + cornerS) / 2;
            t = (t + cornerT) / 2;
        }

        private static int RotationCount(int signX, int signY)
        {
            if (signX == 0) return signY == 0 ? 0 : signY > 0 ? 3 : 1;
            if (signX > 0) return signY >= 0 ? 2 : 1;
            return signY <= 0 ? 0 : 3;
        }

        private static void RotatePoint(ref int x, ref int y, int count)
        {
            int px = x, py = y;
            switch (count)
            {
                case 1: x = py; y = -px; return;
                case 2: x = -px; y = -py; return;
                case 3: x = -py; y = px; return;
                default: return;
            }
        }

        private void NormalOctahedronOriginalValue(int[] pred, int predAt, int[] corr, int corrAt,
            int[] outv, int outAt)
        {
            int maxQuantized, maxValue, center;
            NormalRange(out maxQuantized, out maxValue, out center);
            int ps = pred[predAt] - center, pt = pred[predAt + 1] - center;
            bool inDiamond = Math.Abs(ps) + Math.Abs(pt) <= center;
            if (!inDiamond) InvertDiamond(ref ps, ref pt, center);
            bool inBottomLeft = (ps == 0 && pt == 0) || (ps < 0 && pt <= 0);
            int rotation = RotationCount(ps, pt);
            if (!inBottomLeft) RotatePoint(ref ps, ref pt, rotation);
            int os = ModMax(ps + corr[corrAt], center, maxQuantized);
            int ot = ModMax(pt + corr[corrAt + 1], center, maxQuantized);
            if (!inBottomLeft) RotatePoint(ref os, ref ot, (4 - rotation) % 4);
            if (!inDiamond) InvertDiamond(ref os, ref ot, center);
            outv[outAt] = os + center;
            outv[outAt + 1] = ot + center;
        }

        // ---- the prediction schemes ----

        private void ComputeOriginalValues(int method, int numValues)
        {
            switch (method)
            {
                case PREDICTION_DIFFERENCE: Difference(numValues); break;
                case MESH_PREDICTION_PARALLELOGRAM: Parallelogram(numValues); break;
                case MESH_PREDICTION_CONSTRAINED_MULTI_PARALLELOGRAM: ConstrainedMulti(numValues); break;
                case MESH_PREDICTION_TEX_COORDS_PORTABLE: TexCoords(numValues); break;
                case MESH_PREDICTION_GEOMETRIC_NORMAL: GeometricNormal(numValues); break;
                default:
                throw Bad(what + " stores a Draco attribute predicted by scheme " +
                    method.ToString(CultureInfo.InvariantCulture) + ", which the Draco specification " +
                    "does not describe; export the model again with an up-to-date tool");
            }
        }

        private void Difference(int numValues)
        {
            int components = GetNumComponents();
            int[] signed = symbolsToSignedInts[currAttDec][currAtt];
            var outv = (int[])signed.Clone();
            var zero = new int[components];
            TransformOriginalValue(zero, 0, signed, 0, outv, 0);
            for (int i = components; i < components * numValues; i += components)
                TransformOriginalValue(outv, i - components, signed, i, outv, i);
            originalValues[currAttDec][currAtt] = outv;
        }

        /// <summary>prediction.parallelogram.decoder.md ComputeParallelogramPrediction().</summary>
        private bool ParallelogramPrediction(int entryId, int ci, int[] data, int components, int[] pred, int predAt)
        {
            int oci = Opposite(currAttDec, ci);
            if (oci < 0) return false;
            int v, n, prev;
            CornerToVerts(currAttDec, oci, out v, out n, out prev);
            int[] map = vertexToValueIndex[currAttDec];
            int opp = map[Bounded(v, map.Length)];
            int next = map[Bounded(n, map.Length)];
            int previous = map[Bounded(prev, map.Length)];
            if (opp >= entryId || next >= entryId || previous >= entryId) return false;
            for (int c = 0; c < components; c++)
                pred[predAt + c] = data[next * components + c] + data[previous * components + c] -
                data[opp * components + c];
            return true;
        }

        private void Parallelogram(int numValues)
        {
            int components = GetNumComponents();
            int[] signed = symbolsToSignedInts[currAttDec][currAtt];
            var outv = (int[])signed.Clone();
            var pred = new int[components];
            TransformOriginalValue(pred, 0, signed, 0, outv, 0);
            List<int> corners = valueIndexToCorner[currAttDec];
            for (int p = 1; p < numValues; p++)
            {
                int dst = p * components;
                if (ParallelogramPrediction(p, corners[p], outv, components, pred, 0))
                    TransformOriginalValue(pred, 0, signed, dst, outv, dst);
                else
                    TransformOriginalValue(outv, dst - components, signed, dst, outv, dst);
            }
            originalValues[currAttDec][currAtt] = outv;
        }

        private void ConstrainedMulti(int numValues)
        {
            int components = GetNumComponents();
            int[] signed = symbolsToSignedInts[currAttDec][currAtt];
            var outv = (int[])signed.Clone();
            var pred = new int[kMaxNumParallelograms * components];
            var zero = new int[components];
            TransformOriginalValue(zero, 0, signed, 0, outv, 0);
            List<bool>[] crease = creaseEdges[currAttDec][currAtt];
            var creasePos = new int[kMaxNumParallelograms];
            var multi = new int[components];
            List<int> corners = valueIndexToCorner[currAttDec];

            for (int p = 1; p < numValues; p++)
            {
                int startCorner = corners[p];
                int cornerId = startCorner;
                int count = 0;
                bool firstPass = true;
                int guard = 0;
                while (cornerId >= 0)
                {
                    if (ParallelogramPrediction(p, cornerId, outv, components, pred, count * components))
                    {
                        count++;
                        if (count == kMaxNumParallelograms) break;
                    }
                    cornerId = firstPass ? SwingLeft(currAttDec, cornerId) : SwingRight(currAttDec, cornerId);
                    if (cornerId == startCorner) break;
                    if (cornerId < 0 && firstPass)
                    {
                        firstPass = false;
                        cornerId = SwingRight(currAttDec, startCorner);
                    }
                    if (++guard > oppositeCorners.Length) throw Bad2("a Draco vertex fan does not close");
                }

                int used = 0;
                if (count > 0)
                {
                    for (int i = 0; i < components; i++) multi[i] = 0;
                    for (int i = 0; i < count; i++)
                    {
                        int context = count - 1;
                        List<bool> flags = crease[context];
                        if (creasePos[context] >= flags.Count)
                            throw Bad2("a Draco attribute ran out of crease flags");
                        bool isCrease = flags[creasePos[context]++];
                        if (isCrease) continue;
                        used++;
                        for (int j = 0; j < components; j++) multi[j] += pred[i * components + j];
                    }
                }
                int dst = p * components;
                if (used == 0)
                    TransformOriginalValue(outv, dst - components, signed, dst, outv, dst);
                else
                {
                    for (int c = 0; c < components; c++) multi[c] /= used;
                    TransformOriginalValue(multi, 0, signed, dst, outv, dst);
                }
            }
            originalValues[currAttDec][currAtt] = outv;
        }

        // ---- prediction.texcoords.decoder.md ----

        private static long IntSqrt(long number)
        {
            if (number <= 0) return 0;
            long act = number, root = 1;
            while (act >= 2) { root *= 2; act /= 4; }
            do { root = (root + number / root) / 2; } while (root * root > number);
            return root;
        }

        /// <summary>
        /// The POSITION attribute's own PORTABLE (still quantized) value for the entry a predictor is
        /// working on. The spec hardcodes <c>indices_map_[0]</c> and
        /// <c>seq_int_att_dec_original_values[0][0]</c>, i.e. "decoder 0, attribute 0 IS the
        /// position". It is not, in general - see <see cref="Attr"/> - so the position is looked up by
        /// its declared attribute TYPE, which is what the reference does
        /// (<c>GetNamedAttribute(GeometryAttribute::POSITION)</c>).
        /// </summary>
        private void PositionForDataId(int dataId, int[] pos)
        {
            if (positionDecoder < 0)
                throw Bad(what + " predicts a Draco attribute from positions the file does not carry; " +
                "the file is corrupt, so download or export it again");
            int corner = valueIndexToCorner[currAttDec][Bounded(dataId, valueIndexToCorner[currAttDec].Count)];
            int point = cornerToPointMap[Bounded(corner, cornerToPointMap.Length)];
            int[] map = indicesMap[positionDecoder];
            if (map == null)
                throw Bad(what + " predicts a Draco attribute from positions that have not been read " +
                "yet; the file is corrupt, so download or export it again");
            int mapped = map[Bounded(point, map.Length)];
            int[] source = originalValues[positionDecoder][positionAttribute];
            for (int i = 0; i < 3; i++) pos[i] = source[Bounded(mapped * 3 + i, source.Length)];
        }

        private void TexCoords(int numValues)
        {
            int components = GetNumComponents();
            if (components != kTexCoordsNumComponents)
                throw Bad2("a Draco texture coordinate has " + components.ToString(CultureInfo.InvariantCulture) +
                " components");
            int[] signed = symbolsToSignedInts[currAttDec][currAtt];
            var outv = (int[])signed.Clone();
            List<bool> orientations = texOrientations[currAttDec][currAtt];
            var pred = new int[2];
            var tip = new int[3]; var nextPos = new int[3]; var prevPos = new int[3];
            List<int> corners = valueIndexToCorner[currAttDec];
            int[] map = vertexToValueIndex[currAttDec];

            for (int p = 0; p < numValues; p++)
            {
                int cornerId = corners[p];
                int vertId, nextVert, prevVert;
                CornerToVerts(currAttDec, cornerId, out vertId, out nextVert, out prevVert);
                int nextData = map[Bounded(nextVert, map.Length)];
                int prevData = map[Bounded(prevVert, map.Length)];
                bool done = false;
                if (prevData < p && nextData < p)
                {
                    long nu = outv[nextData * 2], nv = outv[nextData * 2 + 1];
                    long pu = outv[prevData * 2], pv = outv[prevData * 2 + 1];
                    if (pu == nu && pv == nv) { pred[0] = (int)pu; pred[1] = (int)pv; done = true; }
                    else
                    {
                        PositionForDataId(p, tip);
                        PositionForDataId(nextData, nextPos);
                        PositionForDataId(prevData, prevPos);
                        long pnX = prevPos[0] - nextPos[0], pnY = prevPos[1] - nextPos[1], pnZ = prevPos[2] - nextPos[2];
                        long pnNorm2 = pnX * pnX + pnY * pnY + pnZ * pnZ;
                        if (pnNorm2 != 0)
                        {
                            long cnX = tip[0] - nextPos[0], cnY = tip[1] - nextPos[1], cnZ = tip[2] - nextPos[2];
                            long cnDotPn = cnX * pnX + cnY * pnY + cnZ * pnZ;
                            long pnU = pu - nu, pnV = pv - nv;
                            long xU = pnU * cnDotPn + nu * pnNorm2;
                            long xV = pnV * cnDotPn + nv * pnNorm2;
                            long xX = nextPos[0] + pnX * cnDotPn / pnNorm2;
                            long xY = nextPos[1] + pnY * cnDotPn / pnNorm2;
                            long xZ = nextPos[2] + pnZ * cnDotPn / pnNorm2;
                            long dX = tip[0] - xX, dY = tip[1] - xY, dZ = tip[2] - xZ;
                            long cxNorm2 = dX * dX + dY * dY + dZ * dZ;
                            long norm = IntSqrt(cxNorm2 * pnNorm2);
                            long cxU = pnV * norm, cxV = -pnU * norm;
                            if (orientations.Count == 0) throw Bad2("a Draco attribute ran out of texture orientations");
                            bool orientation = orientations[orientations.Count - 1];
                            orientations.RemoveAt(orientations.Count - 1);
                            long u = orientation ? xU + cxU : xU - cxU;
                            long v = orientation ? xV + cxV : xV - cxV;
                            pred[0] = (int)(u / pnNorm2);
                            pred[1] = (int)(v / pnNorm2);
                            done = true;
                        }
                    }
                }
                if (!done)
                {
                    int offset = 0;
                    bool have = false;
                    if (prevData < p) { offset = prevData * 2; have = true; }
                    if (nextData < p) { offset = nextData * 2; have = true; }
                    else if (p > 0) { offset = (p - 1) * 2; have = true; }
                    if (!have) { pred[0] = 0; pred[1] = 0; }
                    else { pred[0] = outv[offset]; pred[1] = outv[offset + 1]; }
                }
                int dst = p * 2;
                TransformOriginalValue(pred, 0, outv, dst, outv, dst);
            }
            originalValues[currAttDec][currAtt] = outv;
        }

        // ---- prediction.normal.decoder.md ----

        private void PositionForCorner(int ci, int[] pos)
        {
            int vertId, n, prev;
            CornerToVerts(currAttDec, ci, out vertId, out n, out prev);
            int[] map = vertexToValueIndex[currAttDec];
            PositionForDataId(map[Bounded(vertId, map.Length)], pos);
        }

        private void GeometricNormal(int numValues)
        {
            int[] signed = symbolsToSignedInts[currAttDec][currAtt];
            var outv = (int[])signed.Clone();
            bool[] flips = flipNormalBits[currAttDec][currAtt];
            int maxQuantized, maxValue, center;
            NormalRange(out maxQuantized, out maxValue, out center);
            var cent = new int[3]; var nextPos = new int[3]; var prevPos = new int[3];
            var pred = new int[2];
            List<int> corners = valueIndexToCorner[currAttDec];

            for (int dataId = 0; dataId < numValues; dataId++)
            {
                int cornerId = corners[dataId];
                PositionForCorner(cornerId, cent);
                long nx = 0, ny = 0, nz = 0;
                int corner = cornerId, start = cornerId, guard = 0;
                bool leftTraversal = true;
                while (corner >= 0)
                {
                    PositionForCorner(Next(corner), nextPos);
                    PositionForCorner(Previous(corner), prevPos);
                    long ax = nextPos[0] - cent[0], ay = nextPos[1] - cent[1], az = nextPos[2] - cent[2];
                    long bx = prevPos[0] - cent[0], by = prevPos[1] - cent[1], bz = prevPos[2] - cent[2];
                    nx += ay * bz - az * by;
                    ny += az * bx - ax * bz;
                    nz += ax * by - ay * bx;
                    if (leftTraversal)
                    {
                        corner = SwingLeft(currAttDec, corner);
                        if (corner < 0) { corner = SwingRight(currAttDec, start); leftTraversal = false; }
                        else if (corner == start) corner = kInvalidCornerIndex;
                    }
                    else corner = SwingRight(currAttDec, corner);
                    if (++guard > oppositeCorners.Length) throw Bad2("a Draco vertex fan does not close");
                }
                long absSum = Math.Abs(nx) + Math.Abs(ny) + Math.Abs(nz);
                const long upper = 1L << 29;
                if (absSum > upper)
                {
                    long quotient = absSum / upper;
                    nx /= quotient; ny /= quotient; nz /= quotient;
                }
                // CanonicalizeIntegerVector()
                int vx, vy, vz;
                long sum = Math.Abs(nx) + Math.Abs(ny) + Math.Abs(nz);
                if (sum == 0) { vx = center; vy = 0; vz = 0; }
                else
                {
                    vx = (int)(nx * center / sum);
                    vy = (int)(ny * center / sum);
                    int rest = center - Math.Abs(vx) - Math.Abs(vy);
                    vz = nz >= 0 ? rest : -rest;
                }
                if (flips[dataId]) { vx = -vx; vy = -vy; vz = -vz; }
                // IntegerVectorToQuantizedOctahedralCoords()
                int s, t;
                if (vx >= 0) { s = vy + center; t = vz + center; }
                else
                {
                    s = vy < 0 ? Math.Abs(vz) : maxValue - Math.Abs(vz);
                    t = vz < 0 ? Math.Abs(vy) : maxValue - Math.Abs(vy);
                }
                if ((s == 0 && t == 0) || (s == 0 && t == maxValue) || (s == maxValue && t == 0)) { s = maxValue; t = maxValue; }
                else if (s == 0 && t > center) t = center - (t - center);
                else if (s == maxValue && t < center) t = center + (center - t);
                else if (t == maxValue && s < center) s = center + (center - s);
                else if (t == 0 && s > center) s = center - (s - center);
                pred[0] = s; pred[1] = t;
                int dst = dataId * 2;
                TransformOriginalValue(pred, 0, outv, dst, outv, dst);
            }
            originalValues[currAttDec][currAtt] = outv;
        }

        // ---- back to the original format ----

        private void DecodeDataNeededByPortableTransforms()
        {
            for (int i = 0; i < attDecNumAttributes[currAttDec]; i++)
            {
                currAtt = i;
                if (genericValues[currAttDec][i]) continue;
                if (seqDecoderType[currAttDec][i] == SEQUENTIAL_ATTRIBUTE_ENCODER_NORMALS)
                    quantBits[currAttDec][i] = U8();
                else if (seqDecoderType[currAttDec][i] == SEQUENTIAL_ATTRIBUTE_ENCODER_QUANTIZATION)
                {
                    int components = GetNumComponents();
                    var min = new float[components];
                    for (int c = 0; c < components; c++) min[c] = F32();
                    quantMin[currAttDec][i] = min;
                    quantRange[currAttDec][i] = F32();
                    quantBits[currAttDec][i] = U8();
                    if (quantBits[currAttDec][i] < 1 || quantBits[currAttDec][i] > 30)
                        throw Bad2("a Draco attribute is quantized to " +
                        quantBits[currAttDec][i].ToString(CultureInfo.InvariantCulture) + " bits");
                }
            }
        }

        private void TransformAttributesToOriginalFormat()
        {
            for (int i = 0; i < attDecNumAttributes[currAttDec]; i++)
            {
                currAtt = i;
                if (genericValues[currAttDec][i]) continue;
                int[] source = originalValues[currAttDec][i];
                int values = numValuesToDecode[currAttDec][i];
                int type = seqDecoderType[currAttDec][i];
                if (type == SEQUENTIAL_ATTRIBUTE_ENCODER_NORMALS)
                {
                    // sequential.normal.attribute.decoder.md: octahedral s/t back to a unit vector.
                    var normals = new float[values * 3];
                    int encoded = normalMaxQ[currAttDec][i];
                    int bits = MostSignificantBit(encoded) + 1;
                    double scale = 1.0 / ((1 << bits) - 2);
                    for (int v = 0; v < values; v++)
                    {
                        double x, y, z;
                        Octahedral(source[v * 2] * scale, source[v * 2 + 1] * scale, out x, out y, out z);
                        normals[v * 3] = (float)x; normals[v * 3 + 1] = (float)y; normals[v * 3 + 2] = (float)z;
                    }
                    dequantizedValues[currAttDec][i] = normals;
                }
                else if (type == SEQUENTIAL_ATTRIBUTE_ENCODER_INTEGER)
                {
                    int components = GetNumComponents();
                    var plain = new float[values * components];
                    for (int v = 0; v < plain.Length; v++) plain[v] = source[v];
                    dequantizedValues[currAttDec][i] = plain;
                }
                else
                {
                    // sequential.quantization.attribute.decoder.md DequantizeValues()
                    int components = GetNumComponents();
                    int bits = quantBits[currAttDec][i];
                    float maxQuantized = (1 << bits) - 1;
                    float factor = 1f / maxQuantized;
                    float range = quantRange[currAttDec][i];
                    float[] min = quantMin[currAttDec][i];
                    var result = new float[values * components];
                    for (int v = 0; v < values; v++)
                        for (int c = 0; c < components; c++)
                    {
                        int val = source[v * components + c];
                        bool negative = val < 0;
                        float norm = (negative ? -val : val) * factor;
                        if (negative) norm = -norm;
                        result[v * components + c] = norm * range + min[c];
                    }
                    dequantizedValues[currAttDec][i] = result;
                }
            }
        }

        /// <summary>sequential.normal.attribute.decoder.md OctaherdalCoordsToUnitVector().</summary>
        private static void Octahedral(double inS, double inT, out double x, out double y, out double z)
        {
            double s = inS, t = inT;
            double spt = s + t, smt = s - t;
            double xSign = 1.0;
            if (!(spt >= 0.5 && spt <= 1.5 && smt >= -0.5 && smt <= 0.5))
            {
                xSign = -1.0;
                if (spt <= 0.5) { s = 0.5 - inT; t = 0.5 - inS; }
                else if (spt >= 1.5) { s = 1.5 - inT; t = 1.5 - inS; }
                else if (smt <= -0.5) { s = inT - 0.5; t = inS + 0.5; }
                else { s = inT + 0.5; t = inS - 0.5; }
                spt = s + t; smt = s - t;
            }
            y = 2.0 * s - 1.0;
            z = 2.0 * t - 1.0;
            x = Math.Min(Math.Min(2.0 * spt - 1.0, 3.0 - 2.0 * spt),
                Math.Min(2.0 * smt + 1.0, 1.0 - 2.0 * smt)) * xSign;
            double norm = x * x + y * y + z * z;
            if (norm < 1e-6) { x = 0; y = 0; z = 0; return; }
            double d = 1.0 / Math.Sqrt(norm);
            x *= d; y *= d; z *= d;
        }

        // ------------------------------------------------------------------ the result

        private Model Assemble()
        {
            var model = new Model { Points = numPoints };
            model.Indices = new int[numFaces * 3];
            for (int f = 0; f < numFaces; f++)
                for (int c = 0; c < 3; c++)
            {
                int index = encoderMethod == MESH_EDGEBREAKER_ENCODING
                    ? cornerToPointMap[f * 3 + c]
                    : faceToVertex[c][f];
                model.Indices[f * 3 + c] = Bounded(index, numPoints);
            }

            for (int i = 0; i < numAttributesDecoders; i++)
                for (int j = 0; j < attDecNumAttributes[i]; j++)
            {
                currAttDec = i; currAtt = j;
                int components = seqDecoderType[i][j] == SEQUENTIAL_ATTRIBUTE_ENCODER_NORMALS
                    ? 3 : genericValues[i][j] ? attNumComponents[i][j] : GetNumComponents();
                float[] source = dequantizedValues[i][j];
                var values = new float[(long)numPoints * components <= int.MaxValue
                    ? numPoints * components : 0];
                if (values.Length == 0 && numPoints > 0)
                    throw Bad2("a Draco attribute is too large to read");
                int[] map = encoderMethod == MESH_EDGEBREAKER_ENCODING ? indicesMap[i] : null;
                for (int point = 0; point < numPoints; point++)
                {
                    int entry = map == null ? point : map[point];
                    int at = Bounded(entry, numValuesToDecode[i][j]) * components;
                    for (int c = 0; c < components; c++) values[point * components + c] = source[at + c];
                }
                model.Attributes.Add(new Attribute
                {
                    UniqueId = attUniqueId[i][j],
                        Type = attType[i][j],
                        Components = components,
                        DataType = attDataType[i][j],
                        Normalized = attNormalized[i][j] != 0,
                        Prediction = predScheme[i][j],
                        Values = values,
                });
            }
            return model;
        }

        // ------------------------------------------------------------------ refusals

        private FormatException Bad2(string message) =>
            Bad(what + ": " + message + "; the file is corrupt, so download or export it again");

        private static FormatException Bad(string message) => new FormatException(message);
    }
}
