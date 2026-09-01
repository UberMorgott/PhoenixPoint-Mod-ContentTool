using System;
using System.Collections.Generic;
using System.IO;
using Morgott.ContentTool.Wwise;

/// <summary>
/// What the ReplaceUiSounds demo's SHIPPED banks actually sound like, in ms, offline.
///
/// The demo's meta.json / README quote a replacement length per media, and those numbers are what a
/// player reads before downloading. They were once copied from a live probe of a bank that has since
/// been re-baked, so this arm re-derives them from the committed <c>Dist\Sounds\*.bnk</c> themselves:
/// walk the bank's DIDX/DATA (the layout <see cref="BankGen"/> writes), parse each media with
/// <see cref="WwiseWem"/>, and divide. A re-bake that changes a length now fails here instead of
/// silently making the published text wrong.
/// </summary>
internal static class DemoBankTests
{
    /// <summary>media id -> length in ms, read out of one bank's DIDX+DATA.</summary>
    internal static Dictionary<uint, int> Durations(byte[] bank)
    {
        var found = new Dictionary<uint, int>();
        var didx = new List<uint[]>();
        int dataOffset = -1, dataSize = 0;
        int o = 8 + (int)WwiseWem.U32(bank, 4);          // past BKHD
        while (o + 8 <= bank.Length)
        {
            string tag = "" + (char)bank[o] + (char)bank[o + 1] + (char)bank[o + 2] + (char)bank[o + 3];
            int size = (int)WwiseWem.U32(bank, o + 4);
            if (tag == "DIDX")
                for (int e = 0; e + 12 <= size; e += 12)
                    didx.Add(new[] { WwiseWem.U32(bank, o + 8 + e), WwiseWem.U32(bank, o + 8 + e + 4), WwiseWem.U32(bank, o + 8 + e + 8) });
            else if (tag == "DATA") { dataOffset = o + 8; dataSize = size; }
            o += 8 + size;
        }
        if (dataOffset < 0) return found;
        foreach (uint[] e in didx)
        {
            if (e[1] + (long)e[2] > dataSize) continue;
            var wem = new byte[e[2]];
            Array.Copy(bank, dataOffset + (int)e[1], wem, 0, wem.Length);
            int ms = Ms(WwiseWem.Parse(wem));
            if (ms > 0) found[e[0]] = ms;
        }
        return found;
    }

    /// <summary>Vorbis declares its sample count in the fmt extension; PCM is data size / frame size.</summary>
    private static int Ms(WwiseWem.Info i)
    {
        if (i == null) return -1;
        if (i.IsVorbis) return (int)Math.Round(i.SampleCount * 1000.0 / i.SampleRate);
        if (i.IsPcm16) return (int)Math.Round(i.DataSize * 1000.0 / (i.Channels * 2.0 * i.SampleRate));
        return -1;
    }

    internal static string Run()
    {
        // Measured 2026-09-01 off the committed banks; the same numbers the demo's meta.json and
        // README quote. A re-bake with different audio must update all three together.
        var expect = new Dictionary<uint, int> { { 18839791, 340 },{ 633458426, 444 }, { 940964934, 601 } };

        string dist = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,
                                                    @"..\..\..\..\..\demos\ReplaceUiSounds\Dist\Sounds"));
        if (!Directory.Exists(dist)) return "DEMO BANKS: SKIPPED - no " + dist;

        int checks = 0;
        var bad = new List<string>();
        foreach (KeyValuePair<uint, int> want in expect)
        {
            checks++;
            string path = Path.Combine(dist, want.Key + ".bnk");
            if (!File.Exists(path)) { bad.Add("no bank " + path); continue; }
            Dictionary<uint, int> got = Durations(File.ReadAllBytes(path));
            int ms;
            if (!got.TryGetValue(want.Key, out ms)) bad.Add(want.Key + ": its own media is not in the bank's DIDX");
            else if (ms != want.Value)
                bad.Add(want.Key + " is " + ms + " ms, the published text says " + want.Value + " ms");
        }
        // The "shipped length" column of the demo's table, off the game's own bank when this machine
        // has it - same PPRoot override and same SKIP-when-absent rule as WemLoopTests.
        string banks = Path.Combine(Environment.GetEnvironmentVariable("PPRoot") ?? @"D:\Steam\steamapps\common\Phoenix Point",
                                    @"PhoenixPointWin64_Data\StreamingAssets\Audio\GeneratedSoundBanks\Windows");
        string skipped = "";
        var vanilla = new Dictionary<uint, int> { { 18839791, 1200 }, { 633458426, 3533 }, { 940964934, 2231 } };
        foreach (KeyValuePair<uint, int> want in vanilla)
        {
            // These three are LOOSE media, not embedded - the demo replaces them with a bank exactly
            // because they stream from their own file.
            string wem = Path.Combine(banks, want.Key + ".wem");
            if (!File.Exists(wem)) { skipped = " (SKIPPED the shipped lengths - no " + banks + ")"; continue; }
            checks++;
            int ms = Ms(WwiseWem.Parse(File.ReadAllBytes(wem)));
            if (Math.Abs(ms - want.Value) > 5)
                bad.Add("shipped " + want.Key + " is " + ms + " ms, the published text says " + want.Value + " ms");
        }

        return bad.Count == 0
            ? "DEMO BANKS: ALL PASS, " + checks + " check(s)" + skipped
            : "DEMO BANKS: " + bad.Count + " FAILURE(S) of " + checks + " check(s): " + string.Join(" | ", bad.ToArray());
    }
}
