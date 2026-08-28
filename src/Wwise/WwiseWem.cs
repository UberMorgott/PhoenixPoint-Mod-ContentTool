using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using NVorbis;
using NVorbis.Contracts;

namespace Morgott.ContentTool.Wwise
{
    /// <summary>
    /// Ported from ResourceReplacer (<c>pp-native\src\WwiseWem.cs</c>), namespace line only, with ONE
    /// deliberate cut: everything from that file's ".wav -&gt; .wem" divider down is left behind. That
    /// half is the IMPORT side - <c>Wav</c>, <c>ReadAudio</c>, <c>ReadWav</c>, <c>ReadMp3</c>,
    /// <c>BuildPcmWem</c> - which this tool already owns in <see cref="WwisePcm"/>, and taking it too
    /// would put two live copies of the same .wav parser in one assembly and drag NLayer in for an
    /// .mp3 reader nothing here asks for. What remains is the EXTRACTION half, unchanged.
    ///
    /// Wwise .wem -&gt; .wav, in managed code only. No Unity types here on purpose: this file is the
    /// part that can be exercised outside the game, which is how the decoder was validated.
    ///
    /// The .wem the game ships is a RIFF whose fmt chunk carries Wwise's own 48-byte extension
    /// (fmt body 0x42, "vorb folded into fmt"). Its Vorbis stream is not a normal Ogg Vorbis stream:
    ///   * the three Vorbis header packets are missing entirely,
    ///   * the setup header is present but with the codebooks replaced by 10-bit indices into a table
    ///     that only ww2ogg has (hence <see cref="WwiseSetupHeaders"/>: the four expansions this game
    ///     needs, baked in at build time),
    ///   * audio packets are framed {u16 length, payload} with no granule, and their leading bits -
    ///     packet type, and for long windows the two window-type flags - are stripped ("mod_packets").
    /// Undoing exactly those three things yields packets a stock Vorbis decoder accepts, which is what
    /// <see cref="PacketSource"/> hands to NVorbis.
    /// </summary>
    internal static class WwiseWem
    {
        internal const ushort CodecVorbis = 0xFFFF;
        internal const ushort CodecPcmEx = 0xFFFE;
        internal const ushort CodecPcm = 0x0001;
        /// <summary>fmt body size that marks the Wwise-2021 folded-vorb layout the field map assumes.</summary>
        private const int VorbisFmtSize = 0x42;

        // Vorb extension field offsets, relative to the fmt body. Pinned in research/wwise/README.md
        // ("Vorb extension layout"), which measured them across all 7997 media.
        private const int VorbSampleCount = 0x18;
        private const int VorbSetupOffset = 0x28;
        private const int VorbAudioOffset = 0x2C;
        private const int VorbHashCodebook = 0x3C;
        private const int VorbBlocksize0 = 0x40;
        private const int VorbBlocksize1 = 0x41;

        internal static ushort U16(byte[] b, int o) { return (ushort)(b[o] | b[o + 1] << 8); }
        internal static uint U32(byte[] b, int o) { return (uint)(b[o] | b[o + 1] << 8 | b[o + 2] << 16 | b[o + 3] << 24); }

        internal sealed class Info
        {
            public ushort Codec;
            public int FmtOffset, FmtSize, DataOffset, DataSize;
            public int Channels, SampleRate, BitsPerSample;
            /// <summary>AkChannelConfig (numChannels | type&lt;&lt;8 | speakerMask&lt;&lt;12), fmt body +0x14.</summary>
            public uint ChannelConfig;
            // Vorbis extension; see the Vorb* offset constants above for the layout and its provenance.
            public uint SampleCount, SetupOffset, AudioOffset, HashCodebook;
            public int Blk0, Blk1;
            /// <summary>Set when the buffer parsed was shorter than the declared data chunk.</summary>
            public bool Truncated;
            // The RIFF sampler chunk - where a LOOPING media declares the region Wwise loops over.
            // Every shipped music .wem carries one; a Sound object's "loop infinite" alone does not
            // say WHERE to loop (measured: 208540756 loops 0..6305360, 1055975960 7896865..13239983).
            public int SmplOffset = -1, SmplSize;
            public int Loops;
            public uint LoopStart, LoopEnd, LoopPlayCount;
            public bool HasLoop { get { return Loops > 0; } }

            public bool IsVorbis { get { return Codec == CodecVorbis && FmtSize == VorbisFmtSize; } }
            public bool IsPcm16 { get { return (Codec == CodecPcmEx || Codec == CodecPcm) && BitsPerSample == 16; } }
        }

        /// <summary>Parses the RIFF envelope. Returns null when this is not a .wem we understand.</summary>
        internal static Info Parse(byte[] w)
        {
            if (w == null || w.Length < 44) return null;
            if (w[0] != 'R' || w[1] != 'I' || w[2] != 'F' || w[3] != 'F') return null;
            if (w[8] != 'W' || w[9] != 'A' || w[10] != 'V' || w[11] != 'E') return null;

            // The chunk walk tolerates a buffer that stops early: the index only reads the first few
            // hundred bytes of each media to get its channel count, and by then fmt and the data
            // header have both been seen. Callers that go on to decode pass the whole file and
            // Truncated tells them so.
            var i = new Info { FmtOffset = -1, DataOffset = -1 };
            int o = 12;
            while (o + 8 <= w.Length)
            {
                // long throughout: a chunk size near uint.MaxValue wraps an int walk negative, and the
                // bounds check below then passes on an offset that is no longer inside the buffer.
                long sz = U32(w, o + 4);
                // Clamped to int.MaxValue and NOT to the buffer: the sizes stay as DECLARED, because
                // the index parses a few hundred head bytes of a media whose data chunk is megabytes,
                // and a buffer-clamped DataSize would shrink to the head and fail the Vorbis
                // AudioOffset check below on every real file. The clamp exists only so a size near
                // uint.MaxValue cannot wrap the int arithmetic negative; every check that mixes a
                // size with an offset is done in long for the same reason, and the real reads are
                // bounded by Truncated / the end checks in PacketSource.
                int clamped = (int)Math.Min(sz, int.MaxValue);
                if (w[o] == 'f' && w[o + 1] == 'm' && w[o + 2] == 't') { i.FmtOffset = o + 8; i.FmtSize = clamped; }
                else if (w[o] == 'd' && w[o + 1] == 'a' && w[o + 2] == 't' && w[o + 3] == 'a') { i.DataOffset = o + 8; i.DataSize = clamped; }
                else if (w[o] == 's' && w[o + 1] == 'm' && w[o + 2] == 'p' && w[o + 3] == 'l') { i.SmplOffset = o + 8; i.SmplSize = clamped; }
                if (o + 8L + sz > w.Length) break;
                o += (int)(8 + sz + (sz & 1));
            }
            if (i.FmtOffset < 0 || i.DataOffset < 0 || i.FmtSize < 16) return null;
            if (i.FmtOffset + (long)i.FmtSize > w.Length) return null;
            i.Truncated = i.DataOffset + (long)i.DataSize > w.Length;

            // smpl body: 36 bytes of sampler fields (numSampleLoops at +28), then 24 bytes per loop
            // {cueId, type, start, end, fraction, playCount}. Read off the shipped files, not a spec.
            if (i.SmplOffset >= 0 && i.SmplSize >= 36 && i.SmplOffset + 36L <= w.Length)
            {
                i.Loops = (int)U32(w, i.SmplOffset + 28);
                if (i.Loops > 0 && i.SmplSize >= 60 && i.SmplOffset + 60L <= w.Length)
                {
                    i.LoopStart = U32(w, i.SmplOffset + 44);
                    i.LoopEnd = U32(w, i.SmplOffset + 48);
                    i.LoopPlayCount = U32(w, i.SmplOffset + 56);   // 0 = infinite; a one-shot ships its own value
                }
                else i.Loops = 0;
            }

            int f = i.FmtOffset;
            i.Codec = U16(w, f);
            i.Channels = U16(w, f + 2);
            i.SampleRate = (int)U32(w, f + 4);
            i.BitsPerSample = U16(w, f + 14);
            // Nothing downstream survives either of these being zero - Decode divides by the channel
            // count - and no real media declares them, so a header that does is not one we understand.
            if (i.Channels <= 0 || i.SampleRate <= 0) return null;
            if (i.FmtSize >= 0x18) i.ChannelConfig = U32(w, f + 0x14);
            if (i.IsVorbis)
            {
                i.SampleCount = U32(w, f + VorbSampleCount);
                i.SetupOffset = U32(w, f + VorbSetupOffset);
                i.AudioOffset = U32(w, f + VorbAudioOffset);
                i.HashCodebook = U32(w, f + VorbHashCodebook);
                i.Blk0 = w[f + VorbBlocksize0];
                i.Blk1 = w[f + VorbBlocksize1];
                if (!(i.SetupOffset < i.AudioOffset && i.AudioOffset < (uint)i.DataSize)) return null;
            }
            return i;
        }

        // ------------------------------------------------------------------ .wem -> .wav
        /// <summary>
        /// Decodes one .wem into a 16-bit PCM .wav. Streams: nothing proportional to the whole file is
        /// held, which matters because the largest media here is 54 MB. Returns null on success,
        /// otherwise a one-line reason the caller can log verbatim.
        /// </summary>
        internal static string ToWav(byte[] wem, string outPath)
        {
            Info i = Parse(wem);
            if (i == null) return "not a RIFF/WAVE .wem";
            if (i.Truncated) return "truncated: the data chunk claims " + i.DataSize + " bytes but only "
                                    + (wem.Length - i.DataOffset) + " are there";

            if (i.IsPcm16)
            {
                // Already PCM (the 8 loose fmt=0xFFFE files). Rewrap, never re-encode.
                using (var fs = File.Create(outPath))
                {
                    WriteWavHeader(fs, i.Channels, i.SampleRate, i.DataSize);
                    fs.Write(wem, i.DataOffset, i.DataSize);
                }
                return null;
            }
            if (!i.IsVorbis) return "unsupported codec 0x" + i.Codec.ToString("X4") + " (fmt body " + i.FmtSize + ")";

            byte[] setup;
            if (!WwiseSetupHeaders.ByHash.TryGetValue(i.HashCodebook, out setup))
                return "no baked setup header for codebook hash " + i.HashCodebook;

            using (var fs = File.Create(outPath))
            {
                WriteWavHeader(fs, i.Channels, i.SampleRate, 0); // sizes patched at the end
                long dataStart = fs.Position;
                long written = Decode(wem, i, setup, fs);
                if (written <= 0) return "vorbis decode produced no samples";
                long bytes = fs.Position - dataStart;
                if (bytes % 2 != 0) { fs.WriteByte(0); bytes++; }
                fs.Position = 4; WriteU32(fs, (uint)(36 + bytes));
                fs.Position = 40; WriteU32(fs, (uint)bytes);
            }
            return null;
        }

        /// <summary>Runs NVorbis over the reconstructed packets, clipped to the declared sample count.</summary>
        private static long Decode(byte[] wem, Info i, byte[] setup, Stream outPcm)
        {
            var src = new PacketSource(wem, i, setup);
            long remaining = i.SampleCount;
            long total = 0;
            using (var dec = new StreamDecoder(src))
            {
                dec.ClipSamples = true;
                var f = new float[16384];
                var pcm = new byte[f.Length * 2];
                int ch = dec.Channels > 0 ? dec.Channels : i.Channels;
                int block = f.Length - f.Length % ch;
                while (remaining > 0)
                {
                    int want = (int)Math.Min(block, remaining * ch);
                    want -= want % ch;
                    if (want <= 0) break;
                    int got = dec.Read(f, 0, want);
                    if (got <= 0) break;
                    int n = 0;
                    for (int k = 0; k < got; k++)
                    {
                        int s = ToPcm16(f[k]);
                        pcm[n++] = (byte)s;
                        pcm[n++] = (byte)(s >> 8);
                    }
                    outPcm.Write(pcm, 0, n);
                    remaining -= got / ch;
                    total += got / ch;
                }
            }
            return total;
        }

        // ------------------------------------------------------------------ packet reconstruction
        /// <summary>
        /// Feeds NVorbis the three synthesised header packets and then the audio packets with the bits
        /// Wwise stripped put back. Built lazily, one packet at a time, so a long track costs one
        /// packet of memory rather than the whole stream.
        /// </summary>
        internal sealed class PacketSource : NVorbis.Contracts.IPacketProvider
        {
            /// <summary>
            /// Mode-table constant, not a per-group field: every one of the four setup headers in
            /// <see cref="WwiseSetupHeaders"/> has mode_bits 1 and mode_blockflag { false, true }, so
            /// the mode number is the low bit of the first payload byte and mode 1 is the long window.
            /// See research/wwise/README.md ("Mode table").
            /// </summary>
            private const int ModeBits = 1;

            private readonly byte[] w;
            private readonly Info info;
            private readonly byte[] setup;
            private readonly int end;           // absolute end of the data chunk
            private int next;                   // absolute offset of the next audio packet header
            private int stage;                  // 0 ident, 1 comment, 2 setup, 3+ audio
            private bool prevBlockflag;
            private Packet peeked;

            internal PacketSource(byte[] wem, Info i, byte[] setupHeader)
            {
                w = wem; info = i; setup = setupHeader;
                // DataSize is what the header declares, so bound both against the buffer: ToWav
                // refuses a Truncated file before it gets here, but this is what makes the packet
                // walk's "> end" checks a real bound rather than one inherited from the header.
                end = (int)Math.Min(i.DataOffset + (long)i.DataSize, wem.Length);
                next = (int)Math.Min(i.DataOffset + (long)i.AudioOffset, end);
            }

            public bool CanSeek { get { return false; } }
            public int StreamSerial { get { return 1; } }
            public long GetGranuleCount() { return info.SampleCount; }
            public long SeekTo(long granulePos, int preRoll, GetPacketGranuleCount getPacketGranuleCount)
            {
                throw new NotSupportedException();
            }

            public IPacket PeekNextPacket()
            {
                if (peeked == null) peeked = Build();
                return peeked;
            }

            public IPacket GetNextPacket()
            {
                Packet p = peeked ?? Build();
                peeked = null;
                p?.Reset();
                return p;
            }

            private Packet Build()
            {
                switch (stage)
                {
                    case 0: stage++; return new Packet(IdentHeader(info));
                    case 1: stage++; return new Packet(CommentHeader());
                    case 2: stage++; return new Packet(setup);
                }
                if (next + 2 > end) return null;
                int len = U16(w, next);
                int payload = next + 2;
                if (len == 0 || payload + len > end) return null;
                int after = payload + len;

                byte[] body = Rebuild(payload, len, after, ref prevBlockflag);
                next = after;
                // Flag the last packet, otherwise NVorbis has no way to know the stream ended.
                bool eos = next + 2 > end || U16(w, next) == 0;
                return new Packet(body, eos);
            }

            /// <summary>
            /// ww2ogg's mod_packets inverse (wwriff.cpp generate_ogg). Everything is LSB-first.
            /// Emitted: type bit 0, the mode number copied through, for a long window the previous and
            /// next window-type bits (the next one needs a peek at the following packet's mode), then
            /// the rest of the first input byte and the remaining bytes verbatim.
            /// </summary>
            private byte[] Rebuild(int payload, int len, int after, ref bool prev)
            {
                int mode = w[payload] & ((1 << ModeBits) - 1);
                bool longWindow = mode != 0;

                var bw = new BitWriter(len + 2);
                bw.Write(0, 1);
                bw.Write(mode, ModeBits);
                if (longWindow)
                {
                    bool nextLong = false;
                    if (after + 2 <= end)
                    {
                        int nlen = U16(w, after);
                        if (nlen > 0 && after + 2 + nlen <= end)
                            nextLong = (w[after + 2] & ((1 << ModeBits) - 1)) != 0;
                    }
                    bw.Write(prev ? 1 : 0, 1);
                    bw.Write(nextLong ? 1 : 0, 1);
                }
                prev = longWindow;
                bw.Write(w[payload] >> ModeBits, 8 - ModeBits);
                for (int k = 1; k < len; k++) bw.Write(w[payload + k], 8);
                return bw.ToArray();
            }
        }

        /// <summary>LSB-first bit writer, the order Vorbis packs in.</summary>
        private sealed class BitWriter
        {
            private byte[] buf;
            private int count;      // bytes fully or partially used
            private int bit;        // bits used in buf[count-1]

            internal BitWriter(int capacity) { buf = new byte[Math.Max(capacity, 4)]; }

            internal void Write(int value, int bits)
            {
                for (int i = 0; i < bits; i++)
                {
                    if (bit == 0)
                    {
                        if (count == buf.Length) Array.Resize(ref buf, buf.Length * 2);
                        buf[count++] = 0;
                    }
                    if ((value >> i & 1) != 0) buf[count - 1] |= (byte)(1 << bit);
                    bit = (bit + 1) & 7;
                }
            }

            internal byte[] ToArray()
            {
                var outp = new byte[count];
                Buffer.BlockCopy(buf, 0, outp, 0, count);
                return outp;
            }
        }

        /// <summary>A byte[] handed to NVorbis as a packet.</summary>
        private sealed class Packet : DataPacket
        {
            private readonly byte[] data;
            private int pos;
            internal Packet(byte[] d, bool eos = false) { data = d; IsEndOfStream = eos; }
            protected override int TotalBits { get { return data.Length * 8; } }
            protected override int ReadNextByte() { return pos < data.Length ? data[pos++] : -1; }
            public override void Reset() { pos = 0; base.Reset(); }
        }

        /// <summary>30-byte Vorbis identification header, exactly the fields ww2ogg emits.</summary>
        private static byte[] IdentHeader(Info i)
        {
            var b = new byte[30];
            b[0] = 1; Encoding.ASCII.GetBytes("vorbis", 0, 6, b, 1);
            PutU32(b, 7, 0);                            // vorbis version
            b[11] = (byte)i.Channels;
            PutU32(b, 12, (uint)i.SampleRate);
            PutU32(b, 16, 0);                           // bitrate max
            PutU32(b, 20, (uint)(i.SampleRate * i.Channels * 2 * 8));   // nominal, informational only
            PutU32(b, 24, 0);                           // bitrate min
            b[28] = (byte)(i.Blk0 | i.Blk1 << 4);
            b[29] = 1;                                  // framing
            return b;
        }

        private static byte[] CommentHeader()
        {
            var v = Encoding.ASCII.GetBytes("Resource_Replacer");
            var b = new byte[7 + 4 + v.Length + 4 + 1];
            b[0] = 3; Encoding.ASCII.GetBytes("vorbis", 0, 6, b, 1);
            PutU32(b, 7, (uint)v.Length);
            Buffer.BlockCopy(v, 0, b, 11, v.Length);
            PutU32(b, 11 + v.Length, 0);                // no user comments
            b[b.Length - 1] = 1;                        // framing
            return b;
        }

        private static void PutU32(byte[] b, int o, uint v)
        {
            b[o] = (byte)v; b[o + 1] = (byte)(v >> 8); b[o + 2] = (byte)(v >> 16); b[o + 3] = (byte)(v >> 24);
        }

        // ------------------------------------------------------------------ .wav plumbing
        private static void WriteU32(Stream s, uint v)
        {
            s.WriteByte((byte)v); s.WriteByte((byte)(v >> 8)); s.WriteByte((byte)(v >> 16)); s.WriteByte((byte)(v >> 24));
        }

        /// <summary>The one sample clamp, from below the cut - <see cref="Decode"/> is its only caller here.</summary>
        private static int ToPcm16(float v)
        {
            int s = (int)(v * 32767.0);
            return s > short.MaxValue ? short.MaxValue : s < short.MinValue ? short.MinValue : s;
        }

        private static void WriteWavHeader(Stream s, int ch, int rate, int dataBytes)
        {
            var hdr = Encoding.ASCII.GetBytes("RIFF----WAVEfmt ");
            s.Write(hdr, 0, 4);
            WriteU32(s, (uint)(36 + dataBytes));
            s.Write(hdr, 8, 8);
            WriteU32(s, 16);
            s.WriteByte(1); s.WriteByte(0);                                  // PCM
            s.WriteByte((byte)ch); s.WriteByte((byte)(ch >> 8));
            WriteU32(s, (uint)rate);
            WriteU32(s, (uint)(rate * ch * 2));
            s.WriteByte((byte)(ch * 2)); s.WriteByte(0);                     // block align
            s.WriteByte(16); s.WriteByte(0);                                 // bits
            s.Write(Encoding.ASCII.GetBytes("data"), 0, 4);
            WriteU32(s, (uint)dataBytes);
        }

    }
}
