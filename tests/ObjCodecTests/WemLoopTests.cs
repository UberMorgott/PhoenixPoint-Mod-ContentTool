using System;
using System.Collections.Generic;
using System.IO;
using Morgott.ContentTool.Wwise;

/// <summary>
/// The LOOP DECLARATION a replacement has to carry, offline.
///
/// A Wwise Sound object that loops says "loop forever" and nothing about WHERE; the region lives in
/// the media's RIFF `smpl` chunk. Dropping it on a replacement is what made the demo's menu track
/// restart before it had finished. The expectations here are not remembered - they are read off the
/// SHIPPED files when this machine has them (the pristine `.ct-backup` of the menu music, and
/// `1055975960.wem`, the 284 s looping PCM whose layout the writer reproduces), and the arms that
/// do not need the game folder run anywhere.
/// </summary>
internal static class WemLoopTests
{
    internal static string Run()
    {
        // Same PPRoot override every sibling test honours - the game folder is machine-specific and
        // the arms that need it SKIP when it is absent, they do not fail.
        string root = Environment.GetEnvironmentVariable("PPRoot") ?? @"D:\Steam\steamapps\common\Phoenix Point";
        string Root = Path.Combine(root, @"PhoenixPointWin64_Data\StreamingAssets\Audio\GeneratedSoundBanks\Windows") + @"\";

        int checks = 0;
        List<string> bad = new List<string>();

        byte[] pcm = new byte[4 * 2 * 1000];               // 1000 stereo frames
        byte[] plain = WwisePcm.BuildWem(pcm, 2, 48000);
        byte[] looped = WwisePcm.BuildWem(pcm, 2, 48000, 1000);

        // The layout, byte for byte: without a loop the data body lands at 64 (unchanged, so every
        // proven short-SFX gate keeps its file); with one it lands at 128, which is where the shipped
        // looping PCM media put theirs.
        checks++;
        if (plain.Length != 64 + pcm.Length) bad.Add("plain .wem is " + plain.Length + " B, expected " + (64 + pcm.Length));
        checks++;
        if (looped.Length != 128 + pcm.Length) bad.Add("looped .wem is " + looped.Length + " B, expected " + (128 + pcm.Length));

        WwiseWem.Info pi = WwiseWem.Parse(plain), li = WwiseWem.Parse(looped);
        checks++;
        if (pi == null || pi.HasLoop) bad.Add("a .wem built with no loop must declare none");
        checks++;
        if (li == null || !li.HasLoop || li.Loops != 1 || li.LoopStart != 0 || li.LoopEnd != 999)
            bad.Add("a .wem built with a loop must read back as 1 region 0..999 (the end is an INCLUSIVE index, as the shipped files write it), got " +
                    (li == null ? "unparsed" : li.Loops + " " + li.LoopStart + ".." + li.LoopEnd));
        checks++;
        if (li == null || li.DataOffset != 128 || li.SampleRate != 48000 || li.Channels != 2)
            bad.Add("the looped .wem must keep fmt intact with its data body at 128");

        // Against the game's own bytes, when this machine has them: our chunk offsets must be the
        // SHIPPED ones, and the shipped looping media must be the reason this feature exists.
        string shipped = Root + "1055975960.wem";
        string skipped = null;
        if (!File.Exists(shipped)) skipped = " (SKIPPED the shipped comparison - no " + shipped + ", set PPRoot to the game folder)";
        else
        {
            byte[] s = File.ReadAllBytes(shipped);
            WwiseWem.Info si = WwiseWem.Parse(s);
            checks++;
            if (si == null || !si.HasLoop) bad.Add("the shipped looping PCM media must declare a loop region");
            checks++;
            if (si == null || si.SmplOffset != 52 || si.DataOffset != 128)
                bad.Add("shipped layout moved: smpl at " + (si == null ? -1 : si.SmplOffset) +
                        ", data at " + (si == null ? -1 : si.DataOffset) + " - expected 52 and 128");
            checks++;
            if (li == null || si == null || li.SmplOffset != si.SmplOffset || li.DataOffset != si.DataOffset)
                bad.Add("our looped .wem does not put smpl/data where the shipped one does");
            // The sampler fields themselves, compared as bytes except the two the loop owns.
            checks++;
            if (si != null && li != null)
            {
                bool same = true;
                for (int k = 0; k < 44; k++) if (s[si.SmplOffset + k] != looped[li.SmplOffset + k]) same = false;
                if (!same) bad.Add("the smpl sampler fields differ from the shipped ones");
            }
        }

        string menu = Root + "208540756.wem.ct-backup";
        if (File.Exists(menu))
        {
            WwiseWem.Info mi = WwiseWem.Parse(File.ReadAllBytes(menu));
            checks++;
            if (mi == null || !mi.HasLoop || mi.LoopStart != 0)
                bad.Add("the pristine menu music must declare an infinite loop starting at 0");
        }

        return bad.Count == 0
            ? "WEM LOOP: ALL PASS, " + checks + " check(s)" + skipped
            : "WEM LOOP: " + bad.Count + " FAILURE(S) of " + checks + " check(s): " + string.Join(" | ", bad.ToArray());
    }
}
