using System;
using System.Collections.Generic;
using System.IO;
using Morgott.ContentTool.Bake;

/// <summary>
/// VIDEO EXTRACTION, offline: the cutscenes are LOOSE .webm under StreamingAssets, so extracting one
/// is a copy - and the thing worth asserting is that the copy IS the original, byte for byte, not
/// that a file appeared. The >1 arm is driven by what is actually on disk: measured, the 69 shipped
/// .webm have 69 distinct names, so it reports that it had nothing to refuse rather than claiming a
/// refusal it never made.
///
/// The game install is machine-specific, so a missing folder is VOID, never PASS.
/// </summary>
internal static class VideoExtractTests
{
    private static int checks;

    internal static string Run()
    {
        string root = Environment.GetEnvironmentVariable("PPRoot") ?? @"D:\Steam\steamapps\common\Phoenix Point";
        string videos = Path.Combine(root, @"PhoenixPointWin64_Data\StreamingAssets\StreamableCopiedAssets");
        if (!Directory.Exists(videos)) return "VIDEO extract VOID - no " + videos + " (set PPRoot to the game folder)";

        List<string> all = LooseFiles.Find(videos, ".webm", null);
        Check(all.Count > 0, "the shipped cutscenes are found (" + all.Count + " .webm)");
        Check(LooseFiles.Find(videos, ".webm", "intro").Count < all.Count,
              "the name filter narrows the listing (" + LooseFiles.Find(videos, ".webm", "intro").Count + " of " + all.Count + ")");
        string report = LooseFiles.Report(videos, ".webm", null, 3);
        Check(report.StartsWith(all.Count + " .webm file(s) match", StringComparison.Ordinal) &&
              report.Contains("... " + (all.Count - 3) + " more"),
              "a capped listing states the total and how much it withheld");

        // The SMALLEST clip, so the byte compare stays cheap; which one that is comes off the disk,
        // never off a constant that a patch could invalidate.
        string smallest = null;
        long smallestBytes = long.MaxValue;
        Dictionary<string, int> byName = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (string rel in all)
        {
            string full = Path.Combine(videos, rel.Replace('/', Path.DirectorySeparatorChar));
            long bytes = new FileInfo(full).Length;
            string name = Path.GetFileNameWithoutExtension(rel);
            byName[name] = byName.TryGetValue(name, out int n) ? n + 1 : 1;
            if (byName[name] == 1 && bytes < smallestBytes) { smallestBytes = bytes; smallest = name; }
        }
        Check(smallest != null, "there is a uniquely named clip to extract");

        string outDir = Path.Combine(Path.GetTempPath(), "ct-videoextract");
        if (Directory.Exists(outDir)) Directory.Delete(outDir, true);
        string written = LooseFiles.CopyOut(videos, ".webm", smallest, outDir);
        byte[] copy = File.ReadAllBytes(written);
        byte[] source = File.ReadAllBytes(Path.Combine(videos,
            all.Find(r => string.Equals(Path.GetFileNameWithoutExtension(r), smallest, StringComparison.OrdinalIgnoreCase))
               .Replace('/', Path.DirectorySeparatorChar)));

        Check(copy.Length == source.Length, "the extracted clip is the size of the shipped one (" + source.Length + " B)");
        int diff = -1;
        for (int i = 0; i < copy.Length && i < source.Length; i++) if (copy[i] != source[i]) { diff = i; break; }
        Check(diff < 0, "the extracted clip IS the shipped one, byte for byte" +
                        (diff < 0 ? "" : " - FIRST DIFFERENCE at byte " + diff));
        // A .webm is a Matroska stream; its first four bytes are the EBML magic.
        Check(copy.Length > 4 && copy[0] == 0x1A && copy[1] == 0x45 && copy[2] == 0xDF && copy[3] == 0xA3,
              "the extracted file starts with the EBML magic, so it really is a .webm");

        Check(Refusal(() => LooseFiles.CopyOut(videos, ".webm", "no_such_clip", outDir), "no .webm named 'no_such_clip'"),
              "an absent name is refused, and the message says it was absent");

        string repeated = null;
        foreach (KeyValuePair<string, int> e in byName) if (e.Value > 1) { repeated = e.Key; break; }
        if (repeated != null)
            Check(Refusal(() => LooseFiles.CopyOut(videos, ".webm", repeated, outDir), "refusing to guess which one"),
                  "a name the DLC folders repeat ('" + repeated + "') is refused rather than guessed");
        Directory.Delete(outDir, true);

        return "VIDEO extract PASS, " + checks + " check(s) - " + all.Count + " shipped .webm, copied '" +
               smallest + "' (" + source.Length + " B) byte-identical" +
               (repeated == null ? ", no repeated names to refuse" : ", refused repeated name '" + repeated + "'");
    }

    /// <summary>True when the call threw AND the message names the cause - not merely that it threw.</summary>
    private static bool Refusal(Action call, string cause)
    {
        try { call(); }
        catch (Exception ex) { return ex.Message.Contains(cause); }
        return false;
    }

    private static void Check(bool ok, string what)
    {
        checks++;
        if (!ok) throw new Exception("VIDEO extract FAIL: " + what);
    }
}
