using System;
using System.IO;

namespace Morgott.ContentTool.Import
{
    /// <summary>
    /// What a compressed audio SOURCE declares about itself, read out of its own container - the
    /// independent half of the import oracle. The engine decodes .ogg/.mp3
    /// (docs\research-format-coverage.md 2.1); this says what the answer was supposed to be, so
    /// "a bank appeared" is never the verdict.
    ///
    /// No UnityEngine type on purpose: the parsers are proven offline against the real probe files
    /// (tests\ObjCodecTests\SourceAudioTests.cs) instead of costing a game launch.
    ///
    /// Never a trust boundary and never an import gate: a file this cannot read is still handed to
    /// the engine, and the arm that compares them says VOID rather than FAIL. The engine's decoder
    /// is the importer, not this.
    /// </summary>
    internal static class SourceAudio
    {
        internal sealed class Info
        {
            internal int Channels, SampleRate;
            /// <summary>Sample FRAMES the container declares, or -1 when it does not say.</summary>
            internal long Frames = -1;

            internal string Describe()
            {
                return Channels + "ch " + SampleRate + "Hz " +
                       (Frames < 0 ? "(frames not declared)" : Frames + " frames");
            }
        }

        /// <summary>Null with a reason when the container is not one this can read.</summary>
        internal static Info Declare(byte[] b, string extension, out string why)
        {
            why = null;
            switch ((extension ?? "").ToLowerInvariant())
            {
                case ".ogg": return Ogg(b, out why);
                case ".mp3": return Mp3(b, out why);
                default:
                    why = "is " + extension + " - the tool's own reader parses it, so there is nothing " +
                          "independent to compare a decode against";
                    return null;
            }
        }

        /// <summary>
        /// Vorbis identification header for the format, last page's granule position for the length -
        /// the two facts an Ogg stream states about itself. Layout: packet type 1, "vorbis",
        /// u32 version, u8 channels, u32 rate (Vorbis I spec 4.2.2); page header "OggS", u8 version,
        /// u8 flags, i64 granule at +6 (Ogg spec 6.2).
        /// </summary>
        private static Info Ogg(byte[] b, out string why)
        {
            why = null;
            if (b == null || b.Length < 58 || b[0] != 'O' || b[1] != 'g' || b[2] != 'g' || b[3] != 'S')
            { why = "is not an Ogg stream (no OggS at byte 0)"; return null; }

            int id = -1;
            int limit = Math.Min(b.Length - 16, 4096);
            for (int i = 0; i < limit; i++)
                if (b[i] == 1 && b[i + 1] == 'v' && b[i + 2] == 'o' && b[i + 3] == 'r' &&
                    b[i + 4] == 'b' && b[i + 5] == 'i' && b[i + 6] == 's') { id = i; break; }
            if (id < 0) { why = "carries no Vorbis identification header in its first page"; return null; }

            Info info = new Info
            {
                Channels = b[id + 11],
                SampleRate = (int)BitConverter.ToUInt32(b, id + 12)
            };
            if (info.Channels <= 0 || info.SampleRate <= 0)
            { why = "declares " + info.Channels + " channels at " + info.SampleRate + " Hz"; return null; }

            // The LAST page's granule is the total sample-frame count of the stream. Scanned from the
            // end, because that is the only page whose granule answers "how long is this".
            for (int i = b.Length - 27; i >= 0; i--)
                if (b[i] == 'O' && b[i + 1] == 'g' && b[i + 2] == 'g' && b[i + 3] == 'S')
                { info.Frames = BitConverter.ToInt64(b, i + 6); break; }
            if (info.Frames < 0) why = null;   // format still declared; length simply unstated
            return info;
        }

        /// <summary>
        /// Walks the MPEG frame headers. Every Layer III frame carries a fixed 1152 samples, so the
        /// frame COUNT is the declared length - which is why an .mp3 decodes to whole frames
        /// (27648 = 24 x 1152 for a 0,5 s probe) rather than to the 22050 its duration suggests: the
        /// encoder's delay and padding are inside those frames.
        ///
        /// ponytail: MPEG-1 Layer III only, which is what every encoder writes for 32/44,1/48 kHz.
        /// Anything else declares nothing and the arm goes VOID - the file still imports, because the
        /// engine and not this is the decoder.
        /// </summary>
        private static Info Mp3(byte[] b, out string why)
        {
            why = null;
            if (b == null || b.Length < 4) { why = "is too short to hold an MPEG frame"; return null; }

            int o = 0;
            if (b[0] == 'I' && b[1] == 'D' && b[2] == '3' && b.Length > 10)
                o = 10 + ((b[6] & 0x7F) << 21 | (b[7] & 0x7F) << 14 | (b[8] & 0x7F) << 7 | (b[9] & 0x7F));

            int[] bitrates = { 0, 32, 40, 48, 56, 64, 80, 96, 112, 128, 160, 192, 224, 256, 320, 0 };
            int[] rates = { 44100, 48000, 32000 };
            Info info = null;
            long frames = 0;
            while (o + 4 <= b.Length)
            {
                if (b[o] != 0xFF || (b[o + 1] & 0xE0) != 0xE0) { o++; continue; }
                int version = (b[o + 1] >> 3) & 3, layer = (b[o + 1] >> 1) & 3;
                int brIdx = (b[o + 2] >> 4) & 0xF, srIdx = (b[o + 2] >> 2) & 3, pad = (b[o + 2] >> 1) & 1;
                if (version != 3 || layer != 1 || brIdx == 0 || brIdx == 15 || srIdx == 3) { o++; continue; }

                int rate = rates[srIdx], bitrate = bitrates[brIdx] * 1000;
                int length = 144 * bitrate / rate + pad;
                if (length <= 4 || o + length > b.Length) break;

                // LAME/ffmpeg put a Xing (CBR: "Info") header in a SILENT first frame that carries no
                // samples. Counting it would overstate the length by exactly 1152.
                bool tag = false;
                for (int i = o + 4; i + 4 <= o + length && i < o + 64; i++)
                    if ((b[i] == 'X' && b[i + 1] == 'i' && b[i + 2] == 'n' && b[i + 3] == 'g') ||
                        (b[i] == 'I' && b[i + 1] == 'n' && b[i + 2] == 'f' && b[i + 3] == 'o')) { tag = true; break; }

                if (info == null)
                    info = new Info { Channels = ((b[o + 3] >> 6) & 3) == 3 ? 1 : 2, SampleRate = rate };
                if (!tag) frames++;
                o += length;
            }
            if (info == null) { why = "holds no MPEG-1 Layer III frame this reader can walk"; return null; }
            info.Frames = frames * 1152;
            return info;
        }

        /// <summary>Reads the file and declares it; a missing file is a reason, not an exception.</summary>
        internal static Info DeclareFile(string path, out string why)
        {
            if (!File.Exists(path)) { why = "does not exist"; return null; }
            return Declare(File.ReadAllBytes(path), Path.GetExtension(path), out why);
        }
    }
}
