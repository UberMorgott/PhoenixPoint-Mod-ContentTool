using System;
using System.Globalization;

namespace Morgott.ContentTool.Import
{
    /// <summary>
    /// Hand-written decoder for <b>EXT_meshopt_compression</b>, the compression virtually every
    /// "optimised for the web" .glb download arrives in. No library and no NuGet package: the loader
    /// does <c>Assembly.Load(byte[])</c> and never resolves a sibling DLL, so the mod is one merged
    /// assembly.
    ///
    /// Every rule below is taken from the RATIFIED Khronos extension text
    /// (<c>extensions/2.0/Vendor/EXT_meshopt_compression/README.md</c>, "Appendix A: Bitstream" and
    /// "Appendix B: Filters"), fetched rather than remembered, and cross-checked against the MIT
    /// reference decoder (github.com/zeux/meshoptimizer, <c>src/vertexcodec.cpp</c> and
    /// <c>src/indexcodec.cpp</c>) wherever the prose leaves a choice open. The two places the prose
    /// alone is not enough are called out at their sites: the 4-bit packing order (the README's own
    /// example line prints <c>delta1</c> twice) and the codec VERSION each mode is pinned to.
    ///
    /// VERSIONS, because the header byte carries one and getting it wrong reads garbage silently.
    /// The extension pins each mode to an exact header byte: ATTRIBUTES is <c>0xa0</c> (vertex codec
    /// v0), TRIANGLES is <c>0xe1</c> and INDICES is <c>0xd1</c> (index codec v1 - which is why a
    /// <c>0xXd</c>/<c>0xXe</c> code means "last-1 / last+1" here and not a vertex-FIFO read; the
    /// reference spells that as <c>fecmax = version >= 1 ? 13 : 15</c>). Any other version is REFUSED
    /// by name rather than guessed at.
    ///
    /// This is a TRUST BOUNDARY like <see cref="GlbReader"/>: the bytes come from a folder a player
    /// can drop anything into. Every stream is decoded into an array sized from the JSON's own
    /// <c>count x byteStride</c> (which the extension REQUIRES to equal the parent bufferView's
    /// byteLength, checked by the caller), the reference decoder's own remaining-length checks are
    /// kept, and any read that still escapes leaves as a <see cref="FormatException"/> naming the
    /// cause - never an IndexOutOfRangeException into the game loop.
    /// </summary>
    internal static class Meshopt
    {
        /// <summary>The extension's own name, as it appears in extensionsRequired and on a bufferView.</summary>
        internal const string Extension = "EXT_meshopt_compression";

        private const int GroupSize = 16;         // "Each group always contains 16 elements"
        private const int GroupDecodeLimit = 24;  // the largest a 16-element group can encode to
        private const int BlockSizeBytes = 8192;  // maxBlockElements = min((8192 / byteStride) & ~15, 256)
        private const int BlockMaxElements = 256;
        private const int TailPadding = 32;       // "a baseline element stored verbatim, padded to 32 bytes"

        /// <summary>Header bit lengths for vertex codec v0: "bits 0/1/2/3" -> 0, 2, 4 or 8 bits per delta.</summary>
        private static readonly int[] Bits = { 0, 2, 4, 8 };

        /// <summary>
        /// One compressed bufferView expanded to the <paramref name="count"/> x
        /// <paramref name="stride"/> bytes its parent bufferView declares. <paramref name="mode"/> and
        /// <paramref name="filter"/> are the extension JSON's own strings.
        /// </summary>
        internal static byte[] Decode(byte[] source, int at, int length, int count, int stride,
                                      string mode, string filter, string what)
        {
            if (stride < 1 || stride > 256)
                throw Bad(what + " declares elements " + stride.ToString(CultureInfo.InvariantCulture) +
                    " bytes wide, which the compression format does not allow; re-export the model without compression");
            if (count < 0 || (long)count * stride > int.MaxValue)
                throw Bad(what + " declares " + count.ToString(CultureInfo.InvariantCulture) +
                    " compressed elements, which is more than this mod will read; simplify the model and re-export");
            if (at < 0 || length < 0 || (long)at + length > source.Length)
                throw Bad(what + " reads compressed bytes past the end of the file, so the file is truncated; " +
                    "copy or re-export it again");

            var result = new byte[(long)count * stride];
            try
            {
                switch (mode)
                {
                    case "ATTRIBUTES": Attributes(source, at, length, result, count, stride, what); break;
                    case "TRIANGLES": Triangles(source, at, length, result, count, stride, what); break;
                    case "INDICES": Indices(source, at, length, result, count, stride, what); break;
                    default:
                        throw Bad(what + " uses the compression mode '" + mode +
                            "', which EXT_meshopt_compression does not define; re-export the model without compression");
                }
                Filter(result, count, stride, filter, what);
            }
            catch (FormatException) { throw; }
            catch (Exception exception)
            {
                throw Bad(what + " could not be decompressed (" + exception.GetType().Name +
                    "), so the file's compressed geometry is corrupt; download or export it again");
            }
            return result;
        }

        // ---------------------------------------------------------------- mode 0: attributes

        /// <summary>
        /// Appendix A, "Mode 0: attributes". A header byte, then attribute blocks of whole elements,
        /// then a TAIL holding the baseline element verbatim. Within a block the element bytes are
        /// DEINTERLEAVED - one "data block" per byte position of the element - and each is a run of
        /// zigzag byte deltas from the same byte of the previous element, in groups of 16.
        /// </summary>
        private static void Attributes(byte[] source, int at, int length, byte[] result, int count,
                                       int stride, string what)
        {
            if ((stride & 3) != 0)
                throw Bad(what + " is compressed with elements " + stride.ToString(CultureInfo.InvariantCulture) +
                    " bytes wide, and the format requires a multiple of four there; re-export the model without compression");
            int end = at + length;
            if (length < 1) throw Truncated(what);
            int header = source[at++];
            if ((header & 0xf0) != 0xa0)
                throw Bad(what + " does not start with the EXT_meshopt_compression vertex header, so its compressed " +
                    "data is corrupt; download or export the model again");
            if ((header & 0x0f) != 0)
                throw Bad(what + " is compressed with version " + (header & 0x0f).ToString(CultureInfo.InvariantCulture) +
                    " of the meshopt vertex codec; this mod reads version 0, the one the ratified " +
                    "EXT_meshopt_compression specifies. Re-run gltfpack without '-vv', or export the model uncompressed");

            // The tail is the baseline element, stored verbatim and padded to 32 bytes. The element
            // sits at the very END of the stream; the padding only sets the minimum stream length.
            int tail = stride < TailPadding ? TailPadding : stride;
            if (end - at < tail) throw Truncated(what);
            var last = new byte[stride];
            Array.Copy(source, end - stride, last, 0, stride);

            int blockElements = (BlockSizeBytes / stride) & ~(GroupSize - 1);
            if (blockElements > BlockMaxElements) blockElements = BlockMaxElements;
            if (blockElements < GroupSize)
                throw Bad(what + " is compressed with elements too wide for the format's block size; " +
                    "re-export the model without compression");

            var column = new byte[BlockMaxElements + GroupSize];
            int done = 0;
            while (done < count)
            {
                int block = count - done < blockElements ? count - done : blockElements;
                int aligned = (block + GroupSize - 1) & ~(GroupSize - 1);
                for (int k = 0; k < stride; k++)
                {
                    at = Bytes(source, at, end, column, aligned, what);
                    int previous = last[k];
                    int to = done * stride + k;
                    for (int i = 0; i < block; i++)
                    {
                        int v = column[i];
                        // "deltas are computed in 8-bit integer space with wrap-around two-complement
                        // arithmetic", zigzag decode(v) = (v & 1) ? ~(v >> 1) : (v >> 1).
                        previous = (previous + ((v & 1) != 0 ? ~(v >> 1) : (v >> 1))) & 0xff;
                        result[to] = (byte)previous;
                        to += stride;
                    }
                    last[k] = (byte)previous;
                }
                done += block;
            }
            if (end - at != tail)
                throw Bad(what + " has " + (end - at).ToString(CultureInfo.InvariantCulture) +
                    " compressed bytes left over where " + tail.ToString(CultureInfo.InvariantCulture) +
                    " are expected, so its compressed data is corrupt; download or export the model again");
        }

        /// <summary>
        /// One "data block": two header bits per group of 16, packed four groups to a byte from the
        /// LEAST significant bit up, then the groups themselves.
        /// </summary>
        private static int Bytes(byte[] source, int at, int end, byte[] target, int size, string what)
        {
            int headerSize = (size / GroupSize + 3) / 4;
            if (end - at < headerSize) throw Truncated(what);
            int header = at;
            at += headerSize;
            for (int i = 0; i < size; i += GroupSize)
            {
                if (end - at < GroupDecodeLimit) throw Truncated(what);
                int group = i / GroupSize;
                at = Group(source, at, target, i, Bits[(source[header + group / 4] >> ((group % 4) * 2)) & 3]);
            }
            return at;
        }

        /// <summary>
        /// 16 byte deltas in one of four encodings. In the 2- and 4-bit encodings the deltas are
        /// packed from the MOST significant bit of each byte down, and an all-ones delta is a SENTINEL
        /// meaning "the real byte follows, after the bit deltas of this group".
        ///
        /// The README's worked example prints the 4-bit packing as
        /// <c>(delta1 &lt;&lt; 0) | (delta1 &lt;&lt; 4)</c> - a typo, the same symbol twice. The
        /// reference decoder settles it (<c>vertexcodec.cpp</c>, <c>decodeBytesGroup</c> case 4:
        /// <c>enc = byte >> (8 - bits), byte &lt;&lt;= bits</c>), which is the high nibble FIRST, and
        /// that is also what the 2-bit line of the same README says in prose.
        /// </summary>
        private static int Group(byte[] source, int at, byte[] target, int to, int bits)
        {
            if (bits == 0)
            {
                for (int i = 0; i < GroupSize; i++) target[to + i] = 0;
                return at;
            }
            if (bits == 8)
            {
                Array.Copy(source, at, target, to, GroupSize);
                return at + GroupSize;
            }
            int perByte = 8 / bits;
            int sentinel = (1 << bits) - 1;
            int spill = at + GroupSize / perByte;   // the explicit bytes follow the packed deltas
            for (int i = 0; i < GroupSize; i += perByte)
            {
                int packed = source[at++];
                for (int j = 0; j < perByte; j++)
                {
                    int value = (packed >> (8 - bits)) & sentinel;
                    packed = (packed << bits) & 0xff;
                    target[to + i + j] = value == sentinel ? source[spill++] : (byte)value;
                }
            }
            return spill;
        }

        // ---------------------------------------------------------------- mode 1: triangles

        /// <summary>
        /// Appendix A, "Mode 1: triangles". One code byte per triangle over a 16-entry edge FIFO and a
        /// 16-entry vertex FIFO, a 16-byte <c>codeaux</c> lookup table in the tail, and a stream of
        /// extra bytes for whatever does not fit. The header is <c>0xe1</c>, i.e. index codec version
        /// 1 - which is the version whose <c>0xXd</c>/<c>0xXe</c> codes mean <c>last-1</c>/<c>last+1</c>
        /// rather than a 14th/15th vertex-FIFO slot.
        /// </summary>
        private static void Triangles(byte[] source, int at, int length, byte[] result, int count,
                                      int stride, string what)
        {
            if (count % 3 != 0)
                throw Bad(what + " holds " + count.ToString(CultureInfo.InvariantCulture) +
                    " compressed indices, which is not a whole number of triangles; re-export the model without compression");
            if (stride != 2 && stride != 4)
                throw Bad(what + " stores compressed triangle indices " + stride.ToString(CultureInfo.InvariantCulture) +
                    " bytes wide, and the format allows only 2 or 4; re-export the model without compression");
            if (length < 1 + count / 3 + 16) throw Truncated(what);
            if ((source[at] & 0xf0) != 0xe0)
                throw Bad(what + " does not start with the EXT_meshopt_compression triangle header, so its compressed " +
                    "data is corrupt; download or export the model again");
            if ((source[at] & 0x0f) != 1)
                throw Bad(what + " is compressed with version " + (source[at] & 0x0f).ToString(CultureInfo.InvariantCulture) +
                    " of the meshopt triangle codec; this mod reads version 1, the one the ratified " +
                    "EXT_meshopt_compression specifies. Export the model uncompressed");

            var edgeA = new int[16];
            var edgeB = new int[16];
            var vertex = new int[16];
            int edgeAt = 0, vertexAt = 0;
            int next = 0, last = 0;

            int code = at + 1;
            int codeEnd = code + count / 3;
            int data = codeEnd;
            int safeEnd = at + length - 16;       // the codeaux table occupies the final 16 bytes
            int aux = safeEnd;
            int wrote = 0;

            while (code < codeEnd)
            {
                int codetri = source[code++];
                int a, b, c;
                if (codetri < 0xf0)
                {
                    int fe = codetri >> 4;
                    a = edgeA[(edgeAt - 1 - fe) & 15];
                    b = edgeB[(edgeAt - 1 - fe) & 15];
                    int fec = codetri & 15;
                    if (fec < 13)
                    {
                        c = fec == 0 ? next++ : vertex[(vertexAt - 1 - fec) & 15];
                        // The vertex FIFO only ADVANCES for a genuinely new vertex; a re-used one is
                        // written into the slot and the cursor stays. This has to match the encoder
                        // exactly or every later FIFO index is off by one.
                        vertex[vertexAt] = c;
                        if (fec == 0) vertexAt = (vertexAt + 1) & 15;
                    }
                    else
                    {
                        if (data > safeEnd) throw Truncated(what);
                        // "fec * 2 - 27" turns 13 and 14 into -1 and +1; 15 means an explicit index.
                        last = c = fec != 15 ? last + (fec * 2 - 27) : Index(source, ref data, last);
                        vertex[vertexAt] = c;
                        vertexAt = (vertexAt + 1) & 15;
                    }
                    Edge(edgeA, edgeB, ref edgeAt, c, b);
                    Edge(edgeA, edgeB, ref edgeAt, a, c);
                }
                else
                {
                    int fea, feb, fec;
                    if (codetri < 0xfe)
                    {
                        int codeaux = source[aux + (codetri & 15)];
                        fea = 0;
                        feb = codeaux >> 4;
                        fec = codeaux & 15;
                    }
                    else
                    {
                        if (data > safeEnd) throw Truncated(what);
                        int codeaux = source[data++];
                        fea = codetri == 0xfe ? 0 : 15;
                        feb = codeaux >> 4;
                        fec = codeaux & 15;
                        // "If 0xZW == 0x00, then next is reset to 0", before anything else.
                        if (codeaux == 0) next = 0;
                    }
                    // next is advanced for all three vertices BEFORE any explicit index is read, which
                    // is what the encoder does; reordering these two steps decodes a different mesh.
                    a = fea == 0 ? next++ : 0;
                    b = feb == 0 ? next++ : vertex[(vertexAt - feb) & 15];
                    c = fec == 0 ? next++ : vertex[(vertexAt - fec) & 15];
                    if (fea == 15) { if (data > safeEnd) throw Truncated(what); last = a = Index(source, ref data, last); }
                    if (feb == 15) { if (data > safeEnd) throw Truncated(what); last = b = Index(source, ref data, last); }
                    if (fec == 15) { if (data > safeEnd) throw Truncated(what); last = c = Index(source, ref data, last); }

                    vertex[vertexAt] = a;
                    vertexAt = (vertexAt + 1) & 15;
                    vertex[vertexAt] = b;
                    if (feb == 0 || feb == 15) vertexAt = (vertexAt + 1) & 15;
                    vertex[vertexAt] = c;
                    if (fec == 0 || fec == 15) vertexAt = (vertexAt + 1) & 15;

                    Edge(edgeA, edgeB, ref edgeAt, b, a);
                    Edge(edgeA, edgeB, ref edgeAt, c, b);
                    Edge(edgeA, edgeB, ref edgeAt, a, c);
                }
                Write(result, ref wrote, stride, a, what);
                Write(result, ref wrote, stride, b, what);
                Write(result, ref wrote, stride, c, what);
            }
            if (data != safeEnd)
                throw Bad(what + " has compressed triangle data left over, so its compressed geometry is corrupt; " +
                    "download or export the model again");
        }

        private static void Edge(int[] a, int[] b, ref int at, int first, int second)
        {
            a[at] = first;
            b[at] = second;
            at = (at + 1) & 15;
        }

        // ---------------------------------------------------------------- mode 2: indices

        /// <summary>
        /// Appendix A, "Mode 2: indices". Each index is a varint-7 whose LOW bit picks one of TWO
        /// running baselines and whose remaining bits are a zigzag delta from it - a scheme that stays
        /// cheap when two independent monotonic runs are interleaved. Header <c>0xd1</c>, four zero
        /// bytes of tail.
        /// </summary>
        private static void Indices(byte[] source, int at, int length, byte[] result, int count,
                                    int stride, string what)
        {
            if (stride != 2 && stride != 4)
                throw Bad(what + " stores compressed indices " + stride.ToString(CultureInfo.InvariantCulture) +
                    " bytes wide, and the format allows only 2 or 4; re-export the model without compression");
            if (length < 1 + count + 4) throw Truncated(what);
            if ((source[at] & 0xf0) != 0xd0)
                throw Bad(what + " does not start with the EXT_meshopt_compression index header, so its compressed " +
                    "data is corrupt; download or export the model again");
            if ((source[at] & 0x0f) != 1)
                throw Bad(what + " is compressed with version " + (source[at] & 0x0f).ToString(CultureInfo.InvariantCulture) +
                    " of the meshopt index codec; this mod reads version 1, the one the ratified " +
                    "EXT_meshopt_compression specifies. Export the model uncompressed");

            int data = at + 1;
            int safeEnd = at + length - 4;
            var last = new int[2];
            int wrote = 0;
            for (int i = 0; i < count; i++)
            {
                if (data >= safeEnd) throw Truncated(what);
                uint v = VByte(source, ref data);
                int baseline = (int)(v & 1);
                v >>= 1;
                last[baseline] += (int)((v >> 1) ^ (uint)(-(int)(v & 1)));
                Write(result, ref wrote, stride, last[baseline], what);
            }
            if (data != safeEnd)
                throw Bad(what + " has compressed index data left over, so its compressed geometry is corrupt; " +
                    "download or export the model again");
        }

        /// <summary>Unsigned LEB128, at most five 7-bit groups - the format's "varint-7".</summary>
        private static uint VByte(byte[] source, ref int at)
        {
            uint lead = source[at++];
            if (lead < 128) return lead;
            uint result = lead & 127u;
            int shift = 7;
            for (int i = 0; i < 4; i++)
            {
                uint group = source[at++];
                result |= (group & 127u) << shift;
                shift += 7;
                if (group < 128) break;
            }
            return result;
        }

        /// <summary>A zigzag delta from <paramref name="last"/>, which it also advances.</summary>
        private static int Index(byte[] source, ref int at, int last)
        {
            uint v = VByte(source, ref at);
            return last + (int)((v >> 1) ^ (uint)(-(int)(v & 1)));
        }

        private static void Write(byte[] result, ref int at, int stride, int index, string what)
        {
            if (at + stride > result.Length)
                throw Bad(what + " decompresses to more indices than it declares, so its compressed geometry is " +
                    "corrupt; download or export the model again");
            if (stride == 2)
            {
                if ((uint)index > ushort.MaxValue)
                    throw Bad(what + " decompresses to vertex " + index.ToString(CultureInfo.InvariantCulture) +
                        ", which does not fit the 16-bit indices it declares; download or export the model again");
                result[at] = (byte)index;
                result[at + 1] = (byte)(index >> 8);
            }
            else
            {
                result[at] = (byte)index;
                result[at + 1] = (byte)(index >> 8);
                result[at + 2] = (byte)(index >> 16);
                result[at + 3] = (byte)(index >> 24);
            }
            at += stride;
        }

        // ---------------------------------------------------------------- appendix B: filters

        /// <summary>
        /// Appendix B. A filter rewrites each decoded element IN PLACE - it never changes the size -
        /// and afterwards "the resulting data can then be used according to the referencing accessors
        /// without further modifications", so the accessor's own componentType/normalized still apply.
        /// </summary>
        private static void Filter(byte[] data, int count, int stride, string filter, string what)
        {
            if (string.IsNullOrEmpty(filter) || filter == "NONE") return;
            switch (filter)
            {
                case "OCTAHEDRAL":
                    if (stride != 4 && stride != 8)
                        throw FilterWidth(what, filter, stride, "4 or 8");
                    for (int i = 0; i < count; i++) Octahedral(data, i * stride, stride == 8);
                    return;
                case "QUATERNION":
                    if (stride != 8) throw FilterWidth(what, filter, stride, "8");
                    for (int i = 0; i < count; i++) Quaternion(data, i * stride);
                    return;
                case "EXPONENTIAL":
                    if ((stride & 3) != 0) throw FilterWidth(what, filter, stride, "a multiple of 4");
                    for (int i = 0; i < count * stride; i += 4) Exponential(data, i);
                    return;
                default:
                    throw Bad(what + " uses the compression filter '" + filter +
                        "', which EXT_meshopt_compression does not define; re-export the model without compression");
            }
        }

        /// <summary>
        /// Filter 1. Four K-bit signed components: X and Y octahedral, the third holding 1.0 as a
        /// K-bit signed normalized integer (which is how K is recovered per element), the fourth
        /// passed through untouched - that slot carries a tangent's handedness.
        /// </summary>
        private static void Octahedral(byte[] data, int at, bool wide)
        {
            double one = Component(data, at, 2, wide);
            if (one == 0.0) return;                       // an unencodable vector; leave the bytes alone
            double x = Component(data, at, 0, wide) / one;
            double y = Component(data, at, 1, wide) / one;
            double z = 1.0 - Math.Abs(x) - Math.Abs(y);
            // The octahedral fold for the negative hemisphere. t is min(z, 0), so it is never
            // positive and copysign(t, x) is |t| carrying x's sign - NOT t times the sign of x, which
            // is the same expression with the wrong sign and folds the lower hemisphere the wrong way.
            double t = z < 0.0 ? z : 0.0;
            x -= CopySign(t, x);
            y -= CopySign(t, y);
            double len = Math.Sqrt(x * x + y * y + z * z);
            if (len <= 0.0) return;
            double max = wide ? 32767.0 : 127.0;
            Store(data, at, 0, wide, x / len * max);
            Store(data, at, 1, wide, y / len * max);
            Store(data, at, 2, wide, z / len * max);
        }

        /// <summary>
        /// Filter 2. Three components of the quaternion, the LARGEST one dropped and reconstructed
        /// positive (q and -q are the same rotation). The fourth input holds 1.0 as a K-bit signed
        /// normalized integer EXCEPT its bottom two bits, which name the dropped component; the
        /// remaining three are stored scaled by sqrt(2) because that is their largest possible
        /// magnitude once the maximum is gone.
        /// </summary>
        private static void Quaternion(byte[] data, int at)
        {
            const double Range = 0.70710678118654752440;  // 1 / sqrt(2)
            int packed = (short)(data[at + 6] | (data[at + 7] << 8));
            double one = packed | 3;
            if (one == 0.0) return;
            double x = (short)(data[at] | (data[at + 1] << 8)) / one * Range;
            double y = (short)(data[at + 2] | (data[at + 3] << 8)) / one * Range;
            double z = (short)(data[at + 4] | (data[at + 5] << 8)) / one * Range;
            double squared = 1.0 - x * x - y * y - z * z;
            double w = Math.Sqrt(squared > 0.0 ? squared : 0.0);
            int max = packed & 3;
            Short(data, at + ((max + 1) & 3) * 2, x * 32767.0);
            Short(data, at + ((max + 2) & 3) * 2, y * 32767.0);
            Short(data, at + ((max + 3) & 3) * 2, z * 32767.0);
            Short(data, at + max * 2, w * 32767.0);
        }

        /// <summary>
        /// Filter 3. A 32-bit integer whose top 8 bits are a signed exponent and whose bottom 24 are a
        /// signed mantissa, decoding to 2^e * m as a float in the same four bytes.
        /// </summary>
        private static void Exponential(byte[] data, int at)
        {
            int packed = data[at] | (data[at + 1] << 8) | (data[at + 2] << 16) | (data[at + 3] << 24);
            int e = packed >> 24;
            int m = (packed << 8) >> 8;
            byte[] bytes = BitConverter.GetBytes((float)(Math.Pow(2.0, e) * m));
            data[at] = bytes[0];
            data[at + 1] = bytes[1];
            data[at + 2] = bytes[2];
            data[at + 3] = bytes[3];
        }

        /// <summary>C99 <c>copysign</c>: the magnitude of the first argument, the sign of the second.</summary>
        private static double CopySign(double magnitude, double sign) =>
            sign >= 0.0 ? Math.Abs(magnitude) : -Math.Abs(magnitude);

        private static double Component(byte[] data, int at, int index, bool wide) =>
            wide ? (short)(data[at + index * 2] | (data[at + index * 2 + 1] << 8)) : (sbyte)data[at + index];

        private static void Store(byte[] data, int at, int index, bool wide, double value)
        {
            if (wide) Short(data, at + index * 2, value);
            else data[at + index] = (byte)(sbyte)Round(value, -128, 127);
        }

        private static void Short(byte[] data, int at, double value)
        {
            int rounded = Round(value, short.MinValue, short.MaxValue);
            data[at] = (byte)rounded;
            data[at + 1] = (byte)(rounded >> 8);
        }

        /// <summary>C99 <c>round()</c> - half away from zero, which is what Appendix B's pseudo-code means.</summary>
        private static int Round(double value, int low, int high)
        {
            double rounded = Math.Round(value, MidpointRounding.AwayFromZero);
            if (rounded < low) return low;
            if (rounded > high) return high;
            return (int)rounded;
        }

        private static FormatException FilterWidth(string what, string filter, int stride, string allowed) =>
            Bad(what + " applies the '" + filter + "' compression filter to elements " +
                stride.ToString(CultureInfo.InvariantCulture) + " bytes wide, and the format allows " + allowed +
                "; re-export the model without compression");

        private static FormatException Truncated(string what) =>
            Bad(what + " runs out of compressed bytes before it decodes everything it declares, so the file is " +
                "truncated; copy or download it again");

        private static FormatException Bad(string message) => new FormatException(message);
    }
}
