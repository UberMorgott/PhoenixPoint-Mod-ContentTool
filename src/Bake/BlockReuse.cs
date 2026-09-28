using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using LZ4ps;

namespace Morgott.ContentTool.Bake
{
    /// <summary>
    /// Writes a patched copy of a shipped UnityFS bundle WITHOUT recompressing what did not change
    /// (PERF.md design 2b). A route vii copy differs from the shipped file only in directory entry 0 (the
    /// serialized CAB); the rest - the .resS, 95%+ of the bytes - was being decompressed and LZ4-packed
    /// again on the player's enable path (px_equipment: 8.9 s of a 9.1 s enable, in game).
    ///
    /// Output layout: [new CAB, LZ4 in the source's block size, zero-padded to whole blocks] [the shipped compressed blocks
    /// from the one holding the first non-CAB entry onward, byte-copied]. Every other entry keeps its
    /// position inside those blocks, shifted by one delta, so the directory stays in ascending order and
    /// no old CAB block is carried (the first reused block may still start with the old CAB's tail - bytes
    /// no entry points at). Header and blocks info are rebuilt: sizes, counts, per-block flags (the reused
    /// ones verbatim), v7 alignment, padding flag, a ZERO data hash (a copied one would be stale).
    ///
    /// Anything this does not recognise returns a reason and writes nothing, and the caller falls back to
    /// the full repack - correctness first. System.IO + LZ4 only, no UnityEngine type.
    /// </summary>
    internal static class BlockReuse
    {
        internal sealed class Block { internal uint USize, CSize; internal ushort Flags; }
        internal sealed class Node { internal long Offset, Size; internal uint Flags; internal string Path; }

        internal sealed class Layout
        {
            internal string Signature, UnityVersion, UnityRevision;
            internal uint Version, InfoCSize, InfoUSize, Flags;
            internal long TotalSize, DataStart;
            internal byte[] Hash;
            internal readonly List<Block> Blocks = new List<Block>();
            internal readonly List<Node> Nodes = new List<Node>();
        }

        private const uint CompressionMask = 0x3F, HasDirectoryInfo = 0x40, InfoAtEnd = 0x80,
                           OldWebPlugin = 0x100, PaddingAtStart = 0x200;
        private const uint KnownFlags = CompressionMask | HasDirectoryInfo | InfoAtEnd | OldWebPlugin | PaddingAtStart;
        private const uint SerializedFile = 4;
        /// <summary>Unity's own chunk for LZ4 bundles, and what AssetsTools packs with.</summary>
        private const int ChunkSize = 0x20000;

        /// <summary>Parses the header + blocks info. Throws on anything malformed.</summary>
        internal static Layout Read(Stream s)
        {
            s.Position = 0;
            Layout l = new Layout();
            byte[] sig = new byte[8];
            if (s.Read(sig, 0, 8) != 8 || Encoding.ASCII.GetString(sig) != "UnityFS\0")
                throw new InvalidDataException("not a UnityFS bundle");
            l.Signature = "UnityFS";
            l.Version = U32(s);
            l.UnityVersion = CStr(s);
            l.UnityRevision = CStr(s);
            l.TotalSize = I64(s);
            l.InfoCSize = U32(s);
            l.InfoUSize = U32(s);
            l.Flags = U32(s);
            if (l.Version >= 7) s.Position = Align16(s.Position);
            long headerEnd = s.Position;
            bool atEnd = (l.Flags & InfoAtEnd) != 0;
            if (l.InfoCSize > s.Length) throw new InvalidDataException("blocks info larger than the file");
            s.Position = atEnd ? s.Length - l.InfoCSize : headerEnd;
            byte[] info = Decode(Bytes(s, (int)l.InfoCSize), l.Flags & CompressionMask, (int)l.InfoUSize);
            l.DataStart = atEnd ? headerEnd : headerEnd + l.InfoCSize;
            if ((l.Flags & PaddingAtStart) != 0) l.DataStart = Align16(l.DataStart);

            using (MemoryStream m = new MemoryStream(info))
            {
                l.Hash = Bytes(m, 16);
                int blocks = I32(m);
                if (blocks < 0 || blocks > info.Length / 10) throw new InvalidDataException("block count " + blocks);
                for (int i = 0; i < blocks; i++)
                    l.Blocks.Add(new Block { USize = U32(m), CSize = U32(m), Flags = U16(m) });
                int nodes = I32(m);
                if (nodes < 0 || nodes > info.Length / 20) throw new InvalidDataException("node count " + nodes);
                for (int i = 0; i < nodes; i++)
                    l.Nodes.Add(new Node { Offset = I64(m), Size = I64(m), Flags = U32(m), Path = CStr(m) });
            }
            return l;
        }

        /// <summary>
        /// Writes <paramref name="outPath"/> = <paramref name="sourcePath"/> with directory entry 0 replaced
        /// by <paramref name="cab"/>. Returns null when written; otherwise the reason it refused, with
        /// nothing left at <paramref name="outPath"/>.
        /// </summary>
        internal static string TryWrite(string sourcePath, byte[] cab, string outPath)
        {
            try
            {
                using (FileStream src = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16))
                {
                    Layout l = Read(src);
                    string why = Unsupported(l, src.Length);
                    if (why != null) return why;
                    using (FileStream dst = new FileStream(outPath, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16))
                        Write(l, src, cab, dst);
                }
                return null;
            }
            catch (Exception e)
            {
                try { File.Delete(outPath); } catch (Exception) { /* the caller's full repack overwrites it */ }
                return e.GetType().Name + ": " + e.Message;
            }
        }

        /// <summary>null when the layout is one this writer reproduces exactly, else why not.</summary>
        internal static string Unsupported(Layout l, long fileLength)
        {
            if (l.Version != 6 && l.Version != 7) return "UnityFS version " + l.Version;
            if ((l.Flags & ~KnownFlags) != 0) return "unknown header flags 0x" + l.Flags.ToString("X");
            if ((l.Flags & HasDirectoryInfo) == 0) return "no directory info in the blocks info";
            if (!Lz4OrRaw(l.Flags & CompressionMask)) return "blocks info compression " + (l.Flags & CompressionMask);
            if (l.TotalSize != fileLength) return "header size " + l.TotalSize + " != file " + fileLength;
            long comp = 0, raw = 0;
            foreach (Block b in l.Blocks)
            {
                if (!Lz4OrRaw(b.Flags & CompressionMask)) return "block compression " + (b.Flags & CompressionMask);
                if ((b.Flags & CompressionMask) == 0 && b.CSize != b.USize) return "raw block with csize != usize";
                comp += b.CSize; raw += b.USize;
            }
            int size = BlockSize(l);
            for (int i = 0; i < l.Blocks.Count - 1; i++)
                if (l.Blocks[i].USize != size) return "blocks of more than one size (" + l.Blocks[i].USize + " and " + size + ")";
            long end = l.DataStart + comp + ((l.Flags & InfoAtEnd) != 0 ? l.InfoCSize : 0);
            if (end > fileLength) return "blocks run past the end of the file";
            if (l.Nodes.Count == 0) return "no directory entries";
            Node cab = l.Nodes[0];
            if ((cab.Flags & SerializedFile) == 0) return "entry 0 '" + cab.Path + "' is not a serialized file";
            for (int i = 0; i < l.Nodes.Count; i++)
            {
                Node n = l.Nodes[i];
                if (n.Offset < 0 || n.Size < 0 || n.Offset + n.Size > raw) return "entry '" + n.Path + "' outside the data";
                if (i > 0 && n.Offset < cab.Offset + cab.Size) return "entry '" + n.Path + "' precedes the end of entry 0";
            }
            return null;
        }

        private static void Write(Layout l, Stream src, byte[] cab, Stream dst)
        {
            // First reused block: the one holding the lowest non-CAB entry. Everything before it is the
            // old CAB (Unsupported proved every other entry starts after it).
            long minOther = long.MaxValue;
            for (int i = 1; i < l.Nodes.Count; i++) minOther = Math.Min(minOther, l.Nodes[i].Offset);
            int k = l.Blocks.Count;
            long uStart = 0, cStart = 0, uAt = 0, cAt = 0;
            for (int i = 0; i < l.Blocks.Count; i++)
            {
                if (minOther != long.MaxValue && minOther < uAt + l.Blocks[i].USize) { k = i; uStart = uAt; cStart = cAt; break; }
                uAt += l.Blocks[i].USize; cAt += l.Blocks[i].CSize;
            }
            long cEnd = 0;
            foreach (Block b in l.Blocks) cEnd += b.CSize;
            if (k == l.Blocks.Count) { uStart = uAt; cStart = cEnd; }

            // New CAB region, zero-padded to WHOLE blocks: every block but the last must be one size, or
            // AssetsTools (the read-back gates, ct_route7 verify) refuses the file - "Cannot handle bundles
            // with multiple block sizes yet" - and Unity itself only ever writes that shape. The padding is
            // < one block of zeros (a few hundred bytes compressed); every shifted entry keeps its offset
            // modulo the block size.
            int chunkSize = BlockSize(l);
            long regionLong = (cab.Length + (long)chunkSize - 1) / chunkSize * chunkSize;
            if (regionLong > int.MaxValue) throw new InvalidDataException("CAB too large: " + cab.Length);
            int region = (int)regionLong;
            List<Block> blocks = new List<Block>();
            List<byte[]> packed = new List<byte[]>();
            byte[] chunk = new byte[chunkSize];
            for (int at = 0; at < region; at += chunkSize)
            {
                int n = Math.Min(chunkSize, region - at);
                Array.Clear(chunk, 0, n);
                Buffer.BlockCopy(cab, at, chunk, 0, Math.Max(0, Math.Min(n, cab.Length - at)));
                byte[] z = LZ4Codec.Encode64(chunk, 0, n);
                if (z.Length < n) { packed.Add(z); blocks.Add(new Block { USize = (uint)n, CSize = (uint)z.Length, Flags = 3 }); }   // 3 like every shipped and AssetsTools-packed block: LZ4 and LZ4HC share one decoder
                else
                {
                    byte[] plain = new byte[n];
                    Buffer.BlockCopy(chunk, 0, plain, 0, n);
                    packed.Add(plain); blocks.Add(new Block { USize = (uint)n, CSize = (uint)n, Flags = 0 });
                }
            }
            for (int i = k; i < l.Blocks.Count; i++) blocks.Add(l.Blocks[i]);

            long delta = region - uStart;
            List<Node> nodes = new List<Node>();
            nodes.Add(new Node { Offset = 0, Size = cab.Length, Flags = l.Nodes[0].Flags, Path = l.Nodes[0].Path });
            for (int i = 1; i < l.Nodes.Count; i++)
            {
                Node o = l.Nodes[i];
                nodes.Add(new Node { Offset = o.Offset + delta, Size = o.Size, Flags = o.Flags, Path = o.Path });
            }

            byte[] info;
            using (MemoryStream m = new MemoryStream())
            {
                m.Write(new byte[16], 0, 16);   // data hash: zero, never the shipped one (it hashed other bytes)
                PutI32(m, blocks.Count);
                foreach (Block b in blocks) { PutU32(m, b.USize); PutU32(m, b.CSize); PutU16(m, b.Flags); }
                PutI32(m, nodes.Count);
                foreach (Node n in nodes) { PutI64(m, n.Offset); PutI64(m, n.Size); PutU32(m, n.Flags); PutCStr(m, n.Path); }
                info = m.ToArray();
            }
            uint infoComp = l.Flags & CompressionMask;
            byte[] infoPacked = infoComp == 0 ? info : LZ4Codec.Encode64(info, 0, info.Length);
            if (infoComp != 0 && infoPacked.Length >= info.Length) { infoPacked = info; infoComp = 0; }
            uint flags = (l.Flags & ~(InfoAtEnd | CompressionMask)) | infoComp;

            PutCStr(dst, l.Signature);
            PutU32(dst, l.Version);
            PutCStr(dst, l.UnityVersion);
            PutCStr(dst, l.UnityRevision);
            long sizeAt = dst.Position;
            PutI64(dst, 0);
            PutU32(dst, (uint)infoPacked.Length);
            PutU32(dst, (uint)info.Length);
            PutU32(dst, flags);
            if (l.Version >= 7) Pad16(dst);
            dst.Write(infoPacked, 0, infoPacked.Length);
            if ((flags & PaddingAtStart) != 0) Pad16(dst);
            foreach (byte[] p in packed) dst.Write(p, 0, p.Length);

            src.Position = l.DataStart + cStart;
            byte[] buf = new byte[1 << 20];
            for (long left = cEnd - cStart; left > 0;)
            {
                int r = src.Read(buf, 0, (int)Math.Min(buf.Length, left));
                if (r <= 0) throw new EndOfStreamException("shipped bundle ended inside its blocks");
                dst.Write(buf, 0, r);
                left -= r;
            }
            long total = dst.Position;
            dst.Position = sizeAt;
            PutI64(dst, total);
            dst.Position = total;
        }

        // ------------------------------------------------------------ big-endian primitives

        /// <summary>The source's block size (all but its last block), or Unity's 128 KiB for a one-block file.</summary>
        private static int BlockSize(Layout l)
        {
            return l.Blocks.Count > 1 && l.Blocks[0].USize > 0 && l.Blocks[0].USize <= 1 << 24 ? (int)l.Blocks[0].USize : ChunkSize;
        }

        private static bool Lz4OrRaw(uint c) { return c == 0 || c == 2 || c == 3; }
        private static long Align16(long v) { return (v + 15) & ~15L; }

        private static byte[] Decode(byte[] b, uint comp, int usize)
        {
            if (comp == 0)
            {
                if (b.Length != usize) throw new InvalidDataException("raw blocks info size mismatch");
                return b;
            }
            if (!Lz4OrRaw(comp)) throw new InvalidDataException("blocks info compression " + comp);
            return LZ4Codec.Decode64(b, 0, b.Length, usize);
        }

        private static byte[] Bytes(Stream s, int n)
        {
            byte[] b = new byte[n];
            for (int at = 0; at < n;)
            {
                int r = s.Read(b, at, n - at);
                if (r <= 0) throw new EndOfStreamException();
                at += r;
            }
            return b;
        }

        private static ulong BE(Stream s, int n)
        {
            ulong v = 0;
            for (int i = 0; i < n; i++)
            {
                int c = s.ReadByte();
                if (c < 0) throw new EndOfStreamException();
                v = (v << 8) | (uint)c;
            }
            return v;
        }

        private static ushort U16(Stream s) { return (ushort)BE(s, 2); }
        private static uint U32(Stream s) { return (uint)BE(s, 4); }
        private static int I32(Stream s) { return (int)BE(s, 4); }
        private static long I64(Stream s) { return (long)BE(s, 8); }

        private static string CStr(Stream s)
        {
            List<byte> b = new List<byte>();
            for (int c; (c = s.ReadByte()) != 0;)
            {
                if (c < 0 || b.Count > 4096) throw new InvalidDataException("unterminated string");
                b.Add((byte)c);
            }
            return Encoding.UTF8.GetString(b.ToArray());
        }

        private static void PutBE(Stream s, ulong v, int n) { for (int i = n - 1; i >= 0; i--) s.WriteByte((byte)(v >> (8 * i))); }
        private static void PutU16(Stream s, ushort v) { PutBE(s, v, 2); }
        private static void PutU32(Stream s, uint v) { PutBE(s, v, 4); }
        private static void PutI32(Stream s, int v) { PutBE(s, (uint)v, 4); }
        private static void PutI64(Stream s, long v) { PutBE(s, (ulong)v, 8); }
        private static void PutCStr(Stream s, string v) { byte[] b = Encoding.UTF8.GetBytes(v); s.Write(b, 0, b.Length); s.WriteByte(0); }
        private static void Pad16(Stream s) { while ((s.Position & 15) != 0) s.WriteByte(0); }
    }
}
