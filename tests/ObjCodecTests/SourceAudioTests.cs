using System;
using System.Collections.Generic;
using System.IO;
using Morgott.ContentTool.Import;

/// <summary>
/// The declared half of the audio-import oracle, offline: what an .ogg and an .mp3 say about
/// themselves must be read out of the containers THEMSELVES, or the in-game arm would be comparing
/// a decode against a remembered constant.
///
/// Run against the real probe files the sample project ships (lib\a1_probe.ogg / .mp3, ffmpeg
/// sine 440 Hz 0,5 s mono 44100), whose ground truth is ffprobe's: vorbis,44100,1,0.500000 and
/// mp3,44100,1,0.500000 (docs\research-format-coverage.md 2.1).
/// </summary>
internal static class SourceAudioTests
{
    internal static string Run()
    {
        int checks = 0;
        List<string> bad = new List<string>();

        string lib = Lib();
        if (lib == null) return "SOURCEAUDIO VOID - lib\\a1_probe.ogg not found from " + AppDomain.CurrentDomain.BaseDirectory;

        string why;
        SourceAudio.Info ogg = SourceAudio.DeclareFile(Path.Combine(lib, "a1_probe.ogg"), out why);
        Assert(ogg != null, "the .ogg probe declares itself (" + why + ")", ref checks, bad);
        if (ogg != null)
            Assert(ogg.Channels == 1 && ogg.SampleRate == 44100 && ogg.Frames == 22050,
                   "the .ogg declares 1ch 44100Hz 22050 frames, got " + ogg.Describe(), ref checks, bad);

        SourceAudio.Info mp3 = SourceAudio.DeclareFile(Path.Combine(lib, "a1_probe.mp3"), out why);
        Assert(mp3 != null, "the .mp3 probe declares itself (" + why + ")", ref checks, bad);
        if (mp3 != null)
            // 0,5 s is 22050 samples, but MPEG frames are 1152 samples each and carry the encoder's
            // delay and padding - so the DECLARED length is whole frames, and it is what the engine
            // hands back. A count that silently included the Xing header frame would read 1152 high.
            Assert(mp3.Channels == 1 && mp3.SampleRate == 44100 && mp3.Frames % 1152 == 0 &&
                   mp3.Frames >= 22050 && mp3.Frames < 22050 + 6 * 1152,
                   "the .mp3 declares 1ch 44100Hz whole MPEG frames covering 0,5 s, got " + mp3.Describe(),
                   ref checks, bad);

        // Refusals name the cause: a reader that returned a plausible Info for junk would make the
        // in-game arm compare a decode against noise.
        byte[] junk = new byte[4096];
        new Random(7).NextBytes(junk);
        Refuse(junk, ".ogg", "Ogg", ref checks, bad);
        Refuse(junk, ".mp3", "MPEG", ref checks, bad);
        Refuse(new byte[10], ".ogg", "Ogg", ref checks, bad);
        // A .wav is refused BY NAME, not attempted: WwisePcm.ReadWav is its parser, so there is
        // nothing independent here and the in-game arm must say VOID rather than PASS.
        Refuse(new byte[64], ".wav", "nothing", ref checks, bad);

        // The numbers themselves are printed: the in-game arm asserts the engine's decode EQUALS
        // them, so a run that changes them silently would otherwise be invisible here.
        string detail = "  .ogg declares " + (ogg == null ? "(refused)" : ogg.Describe()) +
                        " | .mp3 declares " + (mp3 == null ? "(refused)" : mp3.Describe());
        if (bad.Count > 0) return "SOURCEAUDIO: " + bad.Count + " FAILURE(S) - " + string.Join("; ", bad.ToArray());
        return "SOURCEAUDIO: ALL PASS, " + checks + " check(s)\n" + detail;
    }

    /// <summary>The repo's lib\, found by walking up from wherever the test exe sits.</summary>
    private static string Lib()
    {
        DirectoryInfo d = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
        while (d != null)
        {
            string lib = Path.Combine(d.FullName, "lib");
            if (File.Exists(Path.Combine(lib, "a1_probe.ogg"))) return lib;
            d = d.Parent;
        }
        return null;
    }

    private static void Refuse(byte[] bytes, string ext, string expect, ref int checks, List<string> bad)
    {
        string why;
        SourceAudio.Info got = SourceAudio.Declare(bytes, ext, out why);
        Assert(got == null && why != null && why.IndexOf(expect, StringComparison.OrdinalIgnoreCase) >= 0,
               ext + " junk is refused with a message naming '" + expect + "' (got: " +
               (got == null ? why : "ACCEPTED " + got.Describe()) + ")", ref checks, bad);
    }

    private static void Assert(bool ok, string what, ref int checks, List<string> bad)
    {
        checks++;
        if (!ok) bad.Add(what);
    }
}
