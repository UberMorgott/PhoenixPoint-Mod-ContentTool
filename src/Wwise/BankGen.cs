using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Morgott.ContentTool.Wwise
{
    /// <summary>
    /// v140 SoundBank generator, ported from the proven donor
    /// (ResourceReplacer\pp-native\src\BankGen.cs, in-game proven as tests A/B/F/G/H and
    /// rr_streamtest). Written strictly from decompiled\AkSoundEngine\ANALYSIS-AkSoundEngine.md;
    /// the oracle for anything this emits is decompiled\AkSoundEngine\hirc_parse.py
    /// (53 banks / 19110 objects / 0 mismatches).
    ///
    /// The three traps the layout below exists to avoid (FINAL-PLAN 8.2, RECIPES 6):
    ///   * 0x5C770DB7 is OverrideBusId (the "UI" AuxBus in Init.bnk), NOT DirectParentID, which is
    ///     the next u32 and is 0. Both zero = loads, posts, and is silent.
    ///   * NodeBaseParams carries bIsOverrideParentMetadata / uNumMetadata between the FX block and
    ///     bOverrideAttachmentParams - new in 2021.1, absent from every older public BNK spec.
    ///   * Event ulActionListSize is a VARINT, not a u8.
    ///
    /// Unity-free and codec-free on purpose: media bytes arrive as a parameter.
    /// </summary>
    internal static class BankGen
    {
        /// <summary>"UI" AuxBus, HIRC type 8 in Init.bnk; equals fnv1_lower32("UI").</summary>
        internal const uint UiBusId = 0x5C770DB7;

        internal const uint PluginPcm = 0x00010001;
        internal const uint PluginVorbis = 0x00040001;

        internal const byte StreamEmbedded = 0;   // media lives in this bank's DIDX+DATA
        internal const byte StreamFull = 2;       // media is a loose <mediaId>.wem on a base path

        private const uint BankVersion = 140;         // the only version this engine build is current for
        private const uint LanguageSfx = 0x17705D3E;  // read off every shipped bank's BKHD
        private const uint ProjectId = 0x000017D1;    // never read by the loader; kept for realism

        /// <summary>
        /// One sound in a generated bank. The Event/Action/Sound IDs are derived from
        /// <see cref="Name"/> so no caller invents or juggles them (FINAL-PLAN 9.1); the media ID
        /// is the caller's, because media IDs are ALLOCATED and can never come from a name.
        /// </summary>
        internal sealed class Src
        {
            internal string Name;
            internal byte[] Wem;
            internal uint MediaId;
            /// <summary>false = shape A, media in this bank's DIDX+DATA; true = shape B, loose &lt;mediaId&gt;.wem.</summary>
            internal bool Stream;
            internal uint PluginId = PluginPcm;

            internal uint EventId { get { return WwiseId.Hash(Name); } }
            internal uint SoundId { get { return WwiseId.Hash(Name + "_sound"); } }
            internal uint ActionId { get { return WwiseId.Hash(Name + "_action"); } }
        }

        /// <summary>
        /// One bank carrying any mix of embedded and streamed sounds, each as
        /// Event -> Play Action -> Sound. Chunk order is BKHD, DIDX, DATA, HIRC with exactly 0
        /// trailing bytes; DIDX and DATA appear only if something is embedded.
        /// </summary>
        internal static byte[] Build(uint bankId, IList<Src> sounds)
        {
            if (sounds == null || sounds.Count == 0) throw new ArgumentException("a bank with no sounds", nameof(sounds));

            List<byte[]> didx = new List<byte[]>();
            MemoryStream data = new MemoryStream();
            HashSet<uint> seen = new HashSet<uint>();

            foreach (Src s in sounds)
            {
                if (s.Wem == null || s.Wem.Length == 0) throw new ArgumentException("no media for '" + s.Name + "'");
                // FINAL-PLAN 8.2: a streamed source with an embedded copy of the same media is a
                // generator bug - the engine reads the in-bank copy and the .wem is never touched,
                // so a stream test would pass while streaming was broken.
                if (!seen.Add(s.MediaId)) throw new ArgumentException("duplicate media ID " + s.MediaId);
                if (s.Stream) continue;

                // Media starts on a 16-byte boundary, the alignment shipped banks use and the one
                // the loader allocates the DATA buffer with. Offsets are declared, so the padding
                // costs nothing but keeps our banks the same shape as the game's.
                while (data.Length % 16 != 0) data.WriteByte(0);
                didx.Add(Concat(U32(s.MediaId), U32((uint)data.Length), U32((uint)s.Wem.Length)));
                data.Write(s.Wem, 0, s.Wem.Length);
            }

            List<byte[]> chunks = new List<byte[]> { Chunk("BKHD", Bkhd(bankId)) };
            if (didx.Count > 0)
            {
                // A DIDX entry is exactly 12 B (sourceID, offset into DATA, size); the chunk size
                // must be a multiple of 12 and must come strictly BEFORE DATA, or LoadMedia
                // silently registers nothing.
                chunks.Add(Chunk("DIDX", Concat(didx.ToArray())));
                chunks.Add(Chunk("DATA", data.ToArray()));
            }

            MemoryStream hirc = new MemoryStream();
            Write(hirc, U32((uint)(sounds.Count * 3)));
            foreach (Src s in sounds)
            {
                HircObject(hirc, 0x02, Sound(s.SoundId, s.PluginId, s.Stream ? StreamFull : StreamEmbedded,
                                             s.MediaId, s.Stream ? 0u : (uint)s.Wem.Length));
                HircObject(hirc, 0x03, PlayAction(s.ActionId, s.SoundId, bankId));
                HircObject(hirc, 0x04, Event(s.EventId, s.ActionId));
            }
            chunks.Add(Chunk("HIRC", hirc.ToArray()));

            return Concat(chunks.ToArray());
        }

        /// <summary>
        /// Shape C: BKHD + DIDX + DATA, no HIRC, DIDX declaring the GAME's own media ID - which
        /// replaces that sound with no SetMedia (PROVEN-FOUNDATIONS #12). Legal at v140: the
        /// "only DATA/DIDX" restriction in the loader is the legacy branch for versions 118..139.
        /// </summary>
        internal static byte[] BuildMediaOnly(uint bankId, uint sourceId, byte[] media)
        {
            return Concat(Chunk("BKHD", Bkhd(bankId)),
                          Chunk("DIDX", Concat(U32(sourceId), U32(0), U32((uint)media.Length))),
                          Chunk("DATA", media));
        }

        // ---------------------------------------------------------------- chunks

        /// <summary>
        /// 20-byte BKHD - the shortest the loader accepts (chunkSize >= 0x14) and exactly what
        /// Barks.bnk ships. uAlignment is 16 and must never be 0: LoadBankMemoryView does a real
        /// `addr % uAlignment`, so a zero there is a process-killing #DE rather than an AKRESULT.
        /// </summary>
        private static byte[] Bkhd(uint bankId)
        {
            return Concat(U32(BankVersion), U32(bankId), U32(LanguageSfx),
                          U16(16), U16(0), U32(ProjectId));
        }

        private static byte[] Chunk(string fourCC, byte[] body)
        {
            return Concat(Encoding.ASCII.GetBytes(fourCC), U32((uint)body.Length), body);
        }

        private static void HircObject(MemoryStream ms, byte type, byte[] body)
        {
            ms.WriteByte(type);
            Write(ms, U32((uint)body.Length));
            Write(ms, body);
        }

        // ---------------------------------------------------------------- HIRC objects

        /// <summary>
        /// Sound, HIRC type 0x02: header, then NodeBaseParams in the order the loader reads them.
        /// Every optional block is switched off, which is the shortest legal NodeBaseParams. The
        /// plugin blob is absent because (pluginID &amp; 0x0F) is 1 for PCM and Vorbis alike - it
        /// only exists for value 2.
        /// </summary>
        private static byte[] Sound(uint soundId, uint pluginId, byte eStreamType,
                                    uint sourceId, uint inMemorySize)
        {
            MemoryStream ms = new MemoryStream();
            Write(ms, U32(soundId));
            Write(ms, U32(pluginId));
            ms.WriteByte(eStreamType);
            Write(ms, U32(sourceId));
            Write(ms, U32(inMemorySize));
            ms.WriteByte(0);                    // uSourceBits

            // -- NodeBaseParams --
            ms.WriteByte(0);                    // bIsOverrideParentFX
            ms.WriteByte(0);                    // uNumFx == 0, so bitsFXBypass is ABSENT
            ms.WriteByte(0);                    // bIsOverrideParentMetadata  <- 2021.1
            ms.WriteByte(0);                    // uNumMetadata
            ms.WriteByte(0);                    // bOverrideAttachmentParams
            Write(ms, U32(UiBusId));            // OverrideBusId
            Write(ms, U32(0));                  // DirectParentID - legal at 0 only because the bus is real
            ms.WriteByte(0);                    // byBitVector (priority override / apply-distance)
            ms.WriteByte(0);                    // cProps       - 0 means the id/value arrays are absent
            ms.WriteByte(0);                    // cRangedProps
            ms.WriteByte(0);                    // uBitsPositioning: bit0 clear -> uBits3d and path block absent
            ms.WriteByte(0);                    // aux byBitVector: bit3 clear -> auxID[4] absent
            Write(ms, U32(0));                  // reflectionsAuxBus - always present
            Write(ms, new byte[6]);             // AdvSettings: bv, eVirtualQueueBehavior, u16 MaxNumInstance, eBelowThresholdBehavior, bv2
            ms.WriteByte(0);                    // StateChunk: varint numProps  = 0
            ms.WriteByte(0);                    // StateChunk: varint numGroups = 0
            Write(ms, U16(0));                  // InitialRTPC: u16 count = 0
            return ms.ToArray();
        }

        /// <summary>
        /// Play action, HIRC type 0x03, ActionType 0x0403. Play carries byBitVector + bankID and has
        /// no exception list, which is what makes it 18 B with empty prop bundles.
        /// </summary>
        private static byte[] PlayAction(uint actionId, uint targetSoundId, uint bankId)
        {
            MemoryStream ms = new MemoryStream();
            Write(ms, U32(actionId));
            Write(ms, U16(0x0403));
            Write(ms, U32(targetSoundId));
            ms.WriteByte(0);                    // bIsBus
            ms.WriteByte(0);                    // props count
            ms.WriteByte(0);                    // ranged props count
            ms.WriteByte(0x04);                 // byBitVector, bits0-4 = eFadeCurve
            Write(ms, U32(bankId));
            return ms.ToArray();
        }

        /// <summary>Event, HIRC type 0x04. The action count is a VARINT - the classic v140 trap.</summary>
        private static byte[] Event(uint eventId, uint actionId)
        {
            MemoryStream ms = new MemoryStream();
            Write(ms, U32(eventId));
            WriteVarint(ms, 1);
            Write(ms, U32(actionId));
            return ms.ToArray();
        }

        // ---------------------------------------------------------------- primitives

        /// <summary>Wwise varint: big-endian 7-bit groups, high bit set on every byte but the last.</summary>
        private static void WriteVarint(MemoryStream ms, uint value)
        {
            List<byte> groups = new List<byte>();
            do { groups.Add((byte)(value & 0x7F)); value >>= 7; } while (value != 0);
            for (int i = groups.Count - 1; i > 0; i--) ms.WriteByte((byte)(groups[i] | 0x80));
            ms.WriteByte(groups[0]);
        }

        private static byte[] U16(ushort v) { return new[] { (byte)v, (byte)(v >> 8) }; }

        private static byte[] U32(uint v)
        {
            return new[] { (byte)v, (byte)(v >> 8), (byte)(v >> 16), (byte)(v >> 24) };
        }

        private static void Write(MemoryStream ms, byte[] b) { ms.Write(b, 0, b.Length); }

        private static byte[] Concat(params byte[][] parts)
        {
            int n = 0;
            foreach (byte[] p in parts) n += p.Length;
            byte[] outp = new byte[n];
            int o = 0;
            foreach (byte[] p in parts) { Buffer.BlockCopy(p, 0, outp, o, p.Length); o += p.Length; }
            return outp;
        }

        // ---------------------------------------------------------------- self-check

        /// <summary>
        /// Re-walks a generated bank the way the loader does and asserts the chunk stream ends
        /// EXACTLY on the last byte. Returns null when clean, else the reason. Run it on every bank
        /// before it reaches LoadBankMemoryCopy: these are the failures that otherwise show up as an
        /// unexplained AKRESULT with nothing to read.
        /// </summary>
        internal static string SelfCheck(byte[] bank)
        {
            if (bank == null || bank.Length < 0x1C)
                return "bank is " + (bank == null ? 0 : bank.Length) + " bytes, the loader rejects anything under 28";
            int o = 0;
            string first = null;
            bool sawData = false;
            while (o + 8 <= bank.Length)
            {
                string tag = Encoding.ASCII.GetString(bank, o, 4);
                uint size = (uint)(bank[o + 4] | bank[o + 5] << 8 | bank[o + 6] << 16 | bank[o + 7] << 24);
                if (first == null) first = tag;
                if (tag == "DIDX")
                {
                    if (size % 12 != 0) return "DIDX size " + size + " is not a multiple of 12";
                    // DIDX AFTER DATA loads clean and registers nothing at all - silent, not an error.
                    if (sawData) return "DIDX comes after DATA; LoadMedia would register nothing";
                }
                if (tag == "DATA") sawData = true;
                o += 8 + (int)size;
                if (o > bank.Length) return "chunk '" + tag + "' runs " + (o - bank.Length) + " bytes past the end of the bank";
            }
            if (first != "BKHD") return "first chunk is '" + first + "', not BKHD";
            if (o != bank.Length) return (bank.Length - o) + " trailing byte(s) after the last chunk";
            return null;
        }
    }
}
