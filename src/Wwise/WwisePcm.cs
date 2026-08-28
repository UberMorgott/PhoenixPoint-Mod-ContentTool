using System;
using System.IO;
using System.Text;

namespace Morgott.ContentTool.Wwise
{
    /// <summary>
    /// 16-bit PCM -&gt; .wem, ported from the donor's WwiseWem.BuildPcmWem
    /// (ResourceReplacer\pp-native\src\WwiseWem.cs), whose output is the media in every proven
    /// in-game audio measurement. PCM is the only codec this tool emits: writing a Wwise Vorbis
    /// encoder is permanently ruled out (PROVEN-FOUNDATIONS, "permanently ruled out").
    ///
    /// The .ogg and .mp3 readers ARE ported now (they were not, on the reasoning that "nothing asks
    /// for them yet"): the alternative was asking the author to launch the game with Unity's audio
    /// device forced on, which means editing a shipped game file. NVorbis was already merged for the
    /// Wwise .wem decoder and reads a plain Ogg container with none of that file's packet surgery;
    /// NLayer (MIT, 72 KB, no dependencies) is merged the same way for .mp3.
    ///
    /// Not ported: the .wem-&gt;.wav Vorbis decoder, which this tool owns in <see cref="WwiseWem"/>.
    /// </summary>
    internal static class WwisePcm
    {
        internal sealed class Wav
        {
            internal int Channels, SampleRate;
            internal byte[] Pcm16;      // interleaved little-endian
        }

        /// <summary>
        /// Reads any accepted source file into 16-bit PCM, in-house. The EXTENSION picks the decoder:
        /// it is what the author sees, so a file named wrong fails here with a sentence about the
        /// name rather than three chunks later with a sentence about bytes.
        ///
        /// Returns null and sets <paramref name="why"/> - it never throws, because a project holding
        /// one unreadable sound must still bake its models (the same rule the clip path follows).
        /// </summary>
        internal static Wav ReadAudio(string path, out string why)
        {
            switch (Path.GetExtension(path ?? "").ToLowerInvariant())
            {
                case ".wav":
                    try { return ReadWav(File.ReadAllBytes(path), out why); }
                    catch (IOException ex) { why = "could not be read (" + ex.Message + ")"; return null; }
                case ".ogg": return ReadVorbis(path, out why);
                case ".mp3": return ReadMp3(path, out why);
                default:
                    why = "is not a format this tool reads; the accepted set is .wav, .ogg and .mp3";
                    return null;
            }
        }

        /// <summary>
        /// Ordinary Ogg Vorbis, through the same NVorbis already merged in for .wem decoding - a
        /// PLAIN Ogg container is the EASY case for it: <see cref="WwiseWem.PacketSource"/> exists
        /// only because Wwise strips the three header packets and the packets' leading bits, and none
        /// of that reconstruction is needed here.
        /// </summary>
        private static Wav ReadVorbis(string path, out string why)
        {
            why = null;
            try
            {
                using (var r = new NVorbis.VorbisReader(path))
                {
                    var pcm = new MemoryStream();
                    var buf = new float[Math.Max(1, r.Channels) * 8192];
                    int n;
                    while ((n = r.ReadSamples(buf, 0, buf.Length)) > 0) WritePcm16(pcm, buf, n);
                    return Decoded(r.Channels, r.SampleRate, pcm, out why);
                }
            }
            catch (Exception ex)
            {
                why = "could not be read as Ogg Vorbis (" + ex.Message + "); re-export it as .wav";
                return null;
            }
        }

        /// <summary>MPEG 1 Layer 1-3, via NLayer (MIT, merged in). Ported from the donor's ReadMp3
        /// (ResourceReplacer\pp-native\src\WwiseWem.cs:484), whose refusals are measured, not guessed.</summary>
        private static Wav ReadMp3(string path, out string why)
        {
            why = null;
            try
            {
                byte[] b = File.ReadAllBytes(path);
                int from = 0, to = b.Length;
                // NLayer wants frames and nothing else - an ID3v2 header or an ID3v1 trailer and it
                // says "Not a valid MPEG file". Practically every .mp3 an author owns carries at
                // least one of the two (ffmpeg writes ID3v2 by default), so this is the common case.
                if (to - from > 10 && b[from] == 'I' && b[from + 1] == 'D' && b[from + 2] == '3')
                {
                    int size = (b[from + 6] & 0x7F) << 21 | (b[from + 7] & 0x7F) << 14 |
                               (b[from + 8] & 0x7F) << 7 | b[from + 9] & 0x7F;
                    from += 10 + size + ((b[from + 5] & 0x10) != 0 ? 10 : 0);   // flag bit 4 = footer
                }
                if (to - from > 128 && b[to - 128] == 'T' && b[to - 127] == 'A' && b[to - 126] == 'G') to -= 128;
                if (from < 0 || from >= to)
                {
                    why = "is an ID3 tag with no MP3 audio behind it; re-export it as .wav";
                    return null;
                }
                // NLayer decodes MPEG-1 only; an MPEG-2 / 2.5 low-sample-rate stream (anything under
                // 32 kHz) makes it throw a flat "Not a valid MPEG file", which says nothing useful.
                // Read the version out of the first frame header and name the real problem instead.
                // Sync = 11 set bits, then a 2-bit version where 3 = MPEG-1.
                for (int i = from; i + 1 < to; i++)
                    if (b[i] == 0xFF && (b[i + 1] & 0xE0) == 0xE0)
                    {
                        int version = b[i + 1] >> 3 & 3;
                        if (version != 3)
                            why = "is a low-sample-rate MP3 (MPEG-" + (version == 2 ? "2" : "2.5") +
                                  ", under 32000 Hz). This tool's MP3 decoder reads MPEG-1 only - " +
                                  "re-export it at 44100 or 48000 Hz, or save it as .ogg or .wav, " +
                                  "which have no such limit";
                        break;
                    }
                if (why != null) return null;
                using (var f = new NLayer.MpegFile(new MemoryStream(b, from, to - from, false)))
                {
                    var pcm = new MemoryStream();
                    var buf = new float[Math.Max(1, f.Channels) * 8192];
                    int n;
                    while ((n = f.ReadSamples(buf, 0, buf.Length)) > 0) WritePcm16(pcm, buf, n);
                    return Decoded(f.Channels, f.SampleRate, pcm, out why);
                }
            }
            catch (Exception ex)
            {
                why = "could not be read as MP3 (" + ex.Message + "); re-export it as .wav";
                return null;
            }
        }

        /// <summary>A decode that produced no audio is a REFUSAL, not an empty buffer: a bank built
        /// from zero samples installs fine and plays silence, which is the failure nobody notices.</summary>
        private static Wav Decoded(int ch, int rate, MemoryStream pcm, out string why)
        {
            if (ch <= 0 || rate <= 0 || pcm.Length == 0)
            {
                why = "decoded to " + pcm.Length / 2 + " sample(s) at " + ch + " channel(s) / " + rate +
                      " Hz, so there is no audio in it";
                return null;
            }
            why = null;
            return new Wav { Channels = ch, SampleRate = rate, Pcm16 = pcm.ToArray() };
        }

        private static void WritePcm16(Stream s, float[] f, int n)
        {
            for (int i = 0; i < n; i++)
            {
                int v = (int)(f[i] * 32767f);
                if (v > short.MaxValue) v = short.MaxValue; else if (v < short.MinValue) v = short.MinValue;
                s.WriteByte((byte)v); s.WriteByte((byte)(v >> 8));
            }
        }

        /// <summary>
        /// Reads any ordinary .wav an editor produces (8/16/24/32-bit integer or 32/64-bit float,
        /// plain or WAVE_FORMAT_EXTENSIBLE) and normalises it to 16-bit PCM. Ported from the donor's
        /// WwiseWem.ReadWav. Returns null and sets <paramref name="why"/> when the file is something
        /// else - a trust boundary, so it is strict rather than forgiving.
        /// </summary>
        internal static Wav ReadWav(byte[] b, out string why)
        {
            why = null;
            if (b == null || b.Length < 44 || b[0] != 'R' || b[1] != 'I' || b[2] != 'F' || b[3] != 'F'
                || b[8] != 'W' || b[9] != 'A' || b[10] != 'V' || b[11] != 'E')
            { why = "is not a RIFF/WAVE file"; return null; }

            int fo = -1, fs = 0, doff = -1, dsz = 0, o = 12;
            while (o + 8 <= b.Length)
            {
                // long: an int walk wraps negative on a chunk size near uint.MaxValue, and the clamp
                // below then never fires. Clamping to what is left keeps a truncated .wav readable.
                long sz = U32(b, o + 4);
                if (o + 8L + sz > b.Length) sz = b.Length - o - 8;
                if (b[o] == 'f' && b[o + 1] == 'm' && b[o + 2] == 't') { fo = o + 8; fs = (int)sz; }
                else if (b[o] == 'd' && b[o + 1] == 'a' && b[o + 2] == 't' && b[o + 3] == 'a') { doff = o + 8; dsz = (int)sz; }
                o += (int)(8 + sz + (sz & 1));
            }
            if (fo < 0 || doff < 0 || fs < 16) { why = "has no fmt/data chunk"; return null; }

            int tag = U16(b, fo), ch = U16(b, fo + 2), rate = (int)U32(b, fo + 4), bits = U16(b, fo + 14);
            if (tag == 0xFFFE && fs >= 40) tag = U16(b, fo + 24);   // extensible: the real tag is in the GUID
            if (ch <= 0 || rate <= 0) { why = "declares " + ch + " channels at " + rate + " Hz"; return null; }
            if (tag != 1 && tag != 3) { why = "is a compressed .wav (format 0x" + tag.ToString("X4") + "); save it as PCM"; return null; }
            // The float branch reads 4 or 8 bytes per sample whatever the header says, so a float
            // .wav declaring 16-bit samples would step past the end of the data chunk.
            if (tag == 3 && bits != 32 && bits != 64)
            { why = "is a float .wav with " + bits + "-bit samples, which is not a float size"; return null; }

            int bytes = bits / 8;
            if (bytes <= 0 || dsz < bytes) { why = "has " + bits + "-bit samples, which are not supported"; return null; }
            int frames = dsz / (bytes * ch);
            byte[] outp = new byte[frames * ch * 2];
            int w = 0;
            for (int n = 0; n < frames * ch; n++)
            {
                int p = doff + n * bytes;
                int s;
                if (tag == 3) s = (int)((bits == 64 ? BitConverter.ToDouble(b, p) : BitConverter.ToSingle(b, p)) * 32767.0);
                else if (bits == 8) s = (b[p] - 128) << 8;
                else if (bits == 16) s = (short)U16(b, p);
                else if (bits == 24) s = (int)(U32(b, p - 1) & 0xFFFFFF00) >> 16;
                else if (bits == 32) s = (int)U32(b, p) >> 16;
                else { why = "has " + bits + "-bit samples, which are not supported"; return null; }
                if (s > short.MaxValue) s = short.MaxValue; else if (s < short.MinValue) s = short.MinValue;
                outp[w++] = (byte)s; outp[w++] = (byte)(s >> 8);
            }
            return new Wav { Channels = ch, SampleRate = rate, Pcm16 = outp };
        }

        private static ushort U16(byte[] b, int o) { return (ushort)(b[o] | b[o + 1] << 8); }
        private static uint U32(byte[] b, int o) { return (uint)(b[o] | b[o + 1] << 8 | b[o + 2] << 16 | b[o + 3] << 24); }

        /// <summary>
        /// Wraps interleaved little-endian 16-bit PCM in the exact .wem shape the shipped PCM media
        /// use: fmt body 24 with WAVE_FORMAT_EXTENSIBLE, cbSize 6 holding {u16 0, AkChannelConfig},
        /// a 4-byte JUNK pad so the data body lands on offset 64, then data. Byte-for-byte the
        /// layout of a file that already plays in game.
        /// </summary>
        /// <param name="loopFrames">
        /// When &gt; 0, a `smpl` chunk declaring ONE infinite loop over the whole of those frames is
        /// written between fmt and data, and the JUNK pad shrinks to nothing so the data body still
        /// lands 16-byte aligned - byte for byte the layout of the shipped looping PCM media
        /// (`1055975960.wem`: fmt 12/24, smpl 44/60, JUNK 112/0, data 120). A LOOPING Sound object
        /// does not say WHERE to loop; the media does, and a replacement that drops the chunk leaves
        /// the engine looping over nothing it declared.
        /// </param>
        internal static byte[] BuildWem(byte[] pcm16, int channels, int sampleRate, long loopFrames = 0, uint loopPlayCount = 0)
        {
            if (pcm16 == null) throw new ArgumentNullException(nameof(pcm16));
            uint channelConfig = ChannelConfigFor(channels);
            // 0 means "no layout this code can honestly name". Wwise would take it and place the
            // source wrongly rather than refuse, so the refusal happens here.
            if (channelConfig == 0)
                throw new ArgumentException(channels + " channels: no speaker layout to declare; mono or stereo only", nameof(channels));
            if (sampleRate <= 0) throw new ArgumentException("sampleRate " + sampleRate, nameof(sampleRate));

            bool loop = loopFrames > 0;
            byte[] b = new byte[(loop ? 128 : 64) + pcm16.Length];
            MemoryStream ms = new MemoryStream(b);
            ms.Write(Encoding.ASCII.GetBytes("RIFF"), 0, 4);
            U32(ms, (uint)(b.Length - 8));
            ms.Write(Encoding.ASCII.GetBytes("WAVEfmt "), 0, 8);
            U32(ms, 24);
            ms.WriteByte(0xFE); ms.WriteByte(0xFF);                          // WAVE_FORMAT_EXTENSIBLE
            ms.WriteByte((byte)channels); ms.WriteByte((byte)(channels >> 8));
            U32(ms, (uint)sampleRate);
            U32(ms, (uint)(sampleRate * channels * 2));
            ms.WriteByte((byte)(channels * 2)); ms.WriteByte(0);             // block align
            ms.WriteByte(16); ms.WriteByte(0);                               // bits per sample
            ms.WriteByte(6); ms.WriteByte(0);                                // cbSize
            ms.WriteByte(0); ms.WriteByte(0);                                // wValidBitsPerSample, unused
            U32(ms, channelConfig);
            if (loop)
            {
                ms.Write(Encoding.ASCII.GetBytes("smpl"), 0, 4);
                U32(ms, 60);
                U32(ms, 0);                       // manufacturer
                U32(ms, 0);                       // product
                U32(ms, (uint)sampleRate);        // the shipped files put the RATE here, not a period
                U32(ms, 0); U32(ms, 0);           // MIDI unity note, pitch fraction
                U32(ms, 0); U32(ms, 0);           // SMPTE format, offset
                U32(ms, 1);                       // numSampleLoops
                U32(ms, 0);                       // sampler data
                U32(ms, 0); U32(ms, 0);           // loop: cue id, type (0 = forward)
                U32(ms, 0);                       // start frame
                U32(ms, (uint)(loopFrames - 1));  // end frame, INCLUSIVE - the shipped 208540756 loops
                                                  // 0..6305360 of 6305362, i.e. an index, not a count
                U32(ms, 0);                       // fraction
                U32(ms, loopPlayCount);           // play count, copied from the target; 0 = infinite
            }
            ms.Write(Encoding.ASCII.GetBytes("JUNK"), 0, 4);
            U32(ms, loop ? 0u : 4u);
            if (!loop) U32(ms, 0);
            ms.Write(Encoding.ASCII.GetBytes("data"), 0, 4);
            U32(ms, (uint)pcm16.Length);
            ms.Write(pcm16, 0, pcm16.Length);
            return b;
        }

        /// <summary>
        /// Interleaved 16-bit PCM as a plain .wav - the canonical 44-byte header, which is exactly
        /// what <see cref="ReadWav"/> takes back in, so extract and import meet on one format.
        /// </summary>
        internal static byte[] BuildWav(byte[] pcm16, int channels, int sampleRate)
        {
            if (pcm16 == null) throw new ArgumentNullException(nameof(pcm16));
            if (channels <= 0) throw new ArgumentException("channels " + channels, nameof(channels));
            if (sampleRate <= 0) throw new ArgumentException("sampleRate " + sampleRate, nameof(sampleRate));

            byte[] b = new byte[44 + pcm16.Length];
            MemoryStream ms = new MemoryStream(b);
            ms.Write(Encoding.ASCII.GetBytes("RIFF"), 0, 4);
            U32(ms, (uint)(b.Length - 8));
            ms.Write(Encoding.ASCII.GetBytes("WAVEfmt "), 0, 8);
            U32(ms, 16);
            ms.WriteByte(1); ms.WriteByte(0);                                // PCM
            ms.WriteByte((byte)channels); ms.WriteByte((byte)(channels >> 8));
            U32(ms, (uint)sampleRate);
            U32(ms, (uint)(sampleRate * channels * 2));
            ms.WriteByte((byte)(channels * 2)); ms.WriteByte(0);             // block align
            ms.WriteByte(16); ms.WriteByte(0);                               // bits per sample
            ms.Write(Encoding.ASCII.GetBytes("data"), 0, 4);
            U32(ms, (uint)pcm16.Length);
            ms.Write(pcm16, 0, pcm16.Length);
            return b;
        }

        /// <summary>
        /// AkChannelConfig (numChannels | type&lt;&lt;8 | speakerMask&lt;&lt;12). Returns 0 past
        /// stereo: there is no layout to guess for 3+ channels, and a stereo mask on a 6-channel
        /// source is worse than a refusal.
        /// </summary>
        internal static uint ChannelConfigFor(int channels)
        {
            if (channels < 1 || channels > 2) return 0;
            uint mask = channels == 1 ? 0x4u : 0x3u;    // front centre / front left+right
            return (uint)channels | 1u << 8 | mask << 12;
        }

        private static void U32(Stream s, uint v)
        {
            s.WriteByte((byte)v); s.WriteByte((byte)(v >> 8)); s.WriteByte((byte)(v >> 16)); s.WriteByte((byte)(v >> 24));
        }
    }
}
