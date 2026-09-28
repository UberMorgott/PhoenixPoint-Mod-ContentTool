using System;
using System.Collections.Generic;
using System.IO;
using AssetsTools.NET;
using AssetsTools.NET.Extra;
using Morgott.ContentTool.Bake;

/// <summary>
/// The block-reuse writer for route vii's patched copies (BlockReuse, PERF.md design 2b), offline, on a
/// real shipped bundle: one Material is edited the way a bake row edits it, the copy is written BOTH ways
/// - the full LZ4Fast repack BundleBaker.Write used before, and BlockReuse - and the two must agree:
/// AssetsTools opens the new file, entry 0 (the CAB) is byte-identical to the repack's, every asset in it
/// reads back byte-identical, the .resS is byte-identical to the shipped one, the reused compressed
/// blocks are the shipped ones verbatim, and a layout it does not handle is REFUSED with nothing written.
///
/// The game install is machine-specific, so a missing bundle is VOID, never PASS.
/// </summary>
internal static class BlockReuseTests
{
    /// <summary>CAB 358 KB over three 128 KiB blocks and a .resS starting inside the third: the shift case.</summary>
    private const string Bundle = "aln_fireworm_assets_all.bundle";

    private static int checks;

    internal static string Run()
    {
        string root = Environment.GetEnvironmentVariable("PPRoot") ?? @"D:\Steam\steamapps\common\Phoenix Point";
        string classData = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"..\..\..\..\..\lib\classdata.tpk");
        string shipped = Path.Combine(root, @"PhoenixPointWin64_Data\StreamingAssets\aa\StandaloneWindows64", Bundle);
        if (!File.Exists(classData)) return "BLOCKREUSE VOID - no " + Path.GetFullPath(classData);
        if (!File.Exists(shipped)) return "BLOCKREUSE VOID - no " + shipped + " (set PPRoot to the game folder)";

        string tmp = Path.Combine(Path.GetTempPath(), "ct-blockreuse-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        try
        {
            string repacked = Path.Combine(tmp, "repack.bundle"), reused = Path.Combine(tmp, "reuse.bundle");
            string edited = WriteBoth(classData, shipped, repacked, reused);

            BlockReuse.Layout src, outL;
            using (FileStream f = File.OpenRead(shipped)) src = BlockReuse.Read(f);
            using (FileStream f = File.OpenRead(reused))
            {
                outL = BlockReuse.Read(f);
                Check(outL.TotalSize == f.Length, "the header's total size is the file's: " + outL.TotalSize + " vs " + f.Length);
                Check(BlockReuse.Unsupported(outL, f.Length) == null, "the output is itself a layout the writer accepts");
            }
            Check(Array.TrueForAll(outL.Hash, b => b == 0), "the data hash is zero, never the shipped one");
            Check(outL.Version == src.Version && outL.UnityRevision == src.UnityRevision && outL.Flags == src.Flags,
                  "version, engine and header flags are the shipped ones: 0x" + outL.Flags.ToString("X"));
            Check(outL.Nodes.Count == src.Nodes.Count, "same directory entry count");
            for (int i = 0; i < src.Nodes.Count; i++)
                Check(outL.Nodes[i].Path == src.Nodes[i].Path && outL.Nodes[i].Flags == src.Nodes[i].Flags,
                      "entry " + i + " keeps its name and flags: " + outL.Nodes[i].Path);
            for (int i = 1; i < outL.Nodes.Count; i++)
                Check(outL.Nodes[i].Offset >= outL.Nodes[0].Offset + outL.Nodes[0].Size, "entries stay in ascending order");

            // The tail of the block list IS the shipped one: same sizes, same flags, same bytes.
            int reusedCount = 0;
            for (int i = 1; i <= Math.Min(src.Blocks.Count, outL.Blocks.Count); i++)
            {
                BlockReuse.Block a = src.Blocks[src.Blocks.Count - i], b = outL.Blocks[outL.Blocks.Count - i];
                if (a.USize != b.USize || a.CSize != b.CSize || a.Flags != b.Flags) break;
                reusedCount++;
            }
            long tail = 0;
            for (int i = src.Blocks.Count - reusedCount; i < src.Blocks.Count; i++) tail += src.Blocks[i].CSize;
            Check(reusedCount >= src.Blocks.Count - 3, "every block past the CAB is reused (" + reusedCount + " of " + src.Blocks.Count + ")");
            Check(SameBytes(shipped, new FileInfo(shipped).Length - tail, reused, new FileInfo(reused).Length - tail, tail),
                  "the reused compressed blocks are the shipped bytes verbatim (" + tail + " B)");

            // What AssetsTools reads out of the file: CAB = the repack's, .resS = the shipped one.
            byte[] cabA = Entry(repacked, 0), cabB = Entry(reused, 0);
            Check(Equal(cabA, cabB), "entry 0 (the CAB) is byte-identical to the full repack's (" + cabB.Length + " B)");
            Check(!Equal(Entry(shipped, 0), cabB), "and it really is the EDITED CAB, not the shipped one");
            for (int i = 1; i < src.Nodes.Count; i++)
                Check(Equal(Entry(shipped, i), Entry(reused, i)), "entry " + i + " is byte-identical to the shipped one");

            int assets = SameAssets(classData, repacked, reused, edited);
            return "BLOCKREUSE PASS on " + Bundle + ": " + assets + " asset(s) equal the full repack, " + reusedCount +
                   " shipped block(s) reused, " + Refusals(tmp, classData, shipped) + ", " + checks + " check(s)";
        }
        finally
        {
            try { Directory.Delete(tmp, true); } catch (Exception) { /* temp */ }
        }
    }

    /// <summary>Edits one Material, then writes the copy the old way and the new way. Returns the new name.</summary>
    private static string WriteBoth(string classData, string shipped, string repacked, string reused)
    {
        AssetsManager m = new AssetsManager();
        m.LoadClassPackage(classData);
        try
        {
            BundleFileInstance bun = m.LoadBundleFile(shipped, true);
            AssetsFileInstance afile = m.LoadAssetsFileFromBundle(bun, 0, false);
            m.LoadClassDatabaseFromPackage(afile.file.Metadata.UnityVersion);
            AssetFileInfo mat = afile.file.Metadata.GetAssetsOfType(AssetClassID.Material)[0];
            AssetTypeValueField bf = m.GetBaseField(afile, mat);
            string name = bf["m_Name"].AsString + "_ct_block_reuse_" + new string('x', 300);
            bf["m_Name"].AsString = name;
            mat.SetNewData(bf);

            // The new way: serialize the CAB alone, hand it to BlockReuse (= BundleBaker.Write).
            byte[] cab;
            using (MemoryStream ms = new MemoryStream())
            {
                using (AssetsFileWriter w = new AssetsFileWriter(ms)) { afile.file.Write(w, 0); w.Flush(); }
                cab = ms.ToArray();
            }
            string why = BlockReuse.TryWrite(shipped, cab, reused);
            Check(why == null, "BlockReuse writes a shipped LZ4 bundle: " + why);

            // The old way, exactly as BundleBaker.Write did it for a patched copy.
            bun.file.BlockAndDirInfo.DirectoryInfos[0].SetNewData(afile.file);
            using (MemoryStream raw = new MemoryStream())
            using (AssetsFileWriter rw = new AssetsFileWriter(raw))
            {
                bun.file.Write(rw, 0);
                rw.Flush();
                raw.Position = 0;
                AssetBundleFile packed = new AssetBundleFile();
                packed.Read(new AssetsFileReader(raw));
                using (AssetsFileWriter w = new AssetsFileWriter(repacked)) packed.Pack(w, AssetBundleCompressionType.LZ4Fast, false, null);
                packed.Close();
            }
            return name;
        }
        finally { m.UnloadAll(); }
    }

    /// <summary>Every asset of the two CABs, raw bytes, pairwise; and the edit reads back through the class DB.</summary>
    private static int SameAssets(string classData, string a, string b, string edited)
    {
        AssetsManager m = new AssetsManager();
        m.LoadClassPackage(classData);
        try
        {
            AssetsFileInstance fa = m.LoadAssetsFileFromBundle(m.LoadBundleFile(a, true), 0, false);
            AssetsFileInstance fb = m.LoadAssetsFileFromBundle(m.LoadBundleFile(b, true), 0, false);
            m.LoadClassDatabaseFromPackage(fb.file.Metadata.UnityVersion);
            IList<AssetFileInfo> ia = fa.file.Metadata.AssetInfos, ib = fb.file.Metadata.AssetInfos;
            Check(ia.Count == ib.Count, "same asset count: " + ia.Count + " vs " + ib.Count);
            for (int i = 0; i < ia.Count; i++)
            {
                Check(ia[i].PathId == ib[i].PathId && ia[i].TypeId == ib[i].TypeId, "asset " + i + " same pathId and type");
                Check(Equal(Raw(fa, ia[i]), Raw(fb, ib[i])), "asset pathId " + ib[i].PathId + " byte-identical");
            }
            AssetFileInfo mat = fb.file.Metadata.GetAssetsOfType(AssetClassID.Material)[0];
            Check(m.GetBaseField(fb, mat)["m_Name"].AsString == edited, "the edited Material reads back edited");
            return ia.Count;
        }
        finally { m.UnloadAll(); }
    }

    /// <summary>A layout the writer does not reproduce is refused by reason, and nothing is left behind.</summary>
    private static string Refusals(string tmp, string classData, string shipped)
    {
        string lzma = Path.Combine(tmp, "lzma.bundle"), outPath = Path.Combine(tmp, "refused.bundle");
        AssetsManager m = new AssetsManager();
        try
        {
            BundleFileInstance bun = m.LoadBundleFile(shipped, true);
            using (MemoryStream raw = new MemoryStream())
            using (AssetsFileWriter rw = new AssetsFileWriter(raw))
            {
                bun.file.Write(rw, 0);
                rw.Flush();
                raw.Position = 0;
                AssetBundleFile packed = new AssetBundleFile();
                packed.Read(new AssetsFileReader(raw));
                using (AssetsFileWriter w = new AssetsFileWriter(lzma)) packed.Pack(w, AssetBundleCompressionType.LZMA, false, null);
                packed.Close();
            }
        }
        finally { m.UnloadAll(); }

        string why = BlockReuse.TryWrite(lzma, new byte[] { 1, 2, 3 }, outPath);
        Check(why != null && why.Contains("compression"), "an LZMA bundle is refused by its compression: " + why);
        Check(!File.Exists(outPath), "a refusal writes nothing");

        string notBundle = Path.Combine(tmp, "not.bundle");
        File.WriteAllText(notBundle, "definitely not UnityFS");
        why = BlockReuse.TryWrite(notBundle, new byte[] { 1 }, outPath);
        Check(why != null && why.Contains("UnityFS"), "a non-UnityFS file is refused: " + why);
        Check(!File.Exists(outPath), "a refusal on a parse error writes nothing");
        return "LZMA + non-UnityFS refused";
    }

    private static byte[] Entry(string bundle, int index)
    {
        AssetsManager m = new AssetsManager();
        try
        {
            BundleFileInstance bun = m.LoadBundleFile(bundle, true);
            bun.file.GetFileRange(index, out long offset, out long length);
            AssetsFileReader r = bun.file.DataReader;
            r.Position = offset;
            return r.ReadBytes((int)length);
        }
        finally { m.UnloadAll(); }
    }

    private static byte[] Raw(AssetsFileInstance f, AssetFileInfo i)
    {
        AssetsFileReader r = f.file.Reader;
        r.Position = i.GetAbsoluteByteOffset(f.file);
        return r.ReadBytes((int)i.ByteSize);
    }

    private static bool SameBytes(string a, long atA, string b, long atB, long n)
    {
        using (FileStream fa = File.OpenRead(a))
        using (FileStream fb = File.OpenRead(b))
        {
            fa.Position = atA; fb.Position = atB;
            byte[] x = new byte[1 << 16], y = new byte[1 << 16];
            while (n > 0)
            {
                int want = (int)Math.Min(x.Length, n);
                if (fa.Read(x, 0, want) != want || fb.Read(y, 0, want) != want) return false;
                for (int i = 0; i < want; i++) if (x[i] != y[i]) return false;
                n -= want;
            }
        }
        return true;
    }

    private static bool Equal(byte[] a, byte[] b)
    {
        if (a.Length != b.Length) return false;
        for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
        return true;
    }

    private static void Check(bool ok, string what)
    {
        checks++;
        if (!ok) throw new Exception("BLOCKREUSE FAIL: " + what);
    }
}
