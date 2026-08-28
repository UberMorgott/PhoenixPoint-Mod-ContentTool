using System;
using System.Collections.Generic;
using System.IO;
using Morgott.ContentTool.Import;
using Morgott.ContentTool.Wwise;

/// <summary>
/// Gate A7, offline: an author's .ogg and .mp3 become PCM with NOTHING but this tool - no Unity
/// audio device, no flag flipped in the game's globalgamemanagers, no external converter.
///
/// Two things are asserted, and they are asserted SEPARATELY (METHODOLOGY, "assert independent
/// things independently"):
///   1. IDENTITY - channels, sample rate and length against the container's OWN declaration
///      (<see cref="SourceAudio"/>), which is a different reader over different bytes, so this is
///      a real oracle and not the decoder agreeing with itself.
///   2. CONTENT - the measured FREQUENCY of the decoded wave. This is the arm that matters: the
///      probes are a 440 Hz sine, and a decoder that handed back silence, noise, the wrong
///      channel interleave, or the file at half rate would satisfy every count in (1) and fail
///      here. Counting samples proves a buffer exists; measuring 440 Hz proves it is the sound.
///
/// Plus the refusal contract the bake now depends on: <see cref="WwisePcm.ReadAudio"/> RETURNS a
/// reason, it never throws, because a project holding one unreadable sound must still bake its
/// models.
///
/// Probes: lib\a1_probe.ogg / .mp3 - ffmpeg sine 440 Hz, 0,5 s, mono 44100, whose ffprobe ground
/// truth is recorded in docs\research-format-coverage.md 2.1.
/// </summary>
internal static class SourceDecodeTests
{
    private const int ProbeHz = 440;

    /// <summary>
    /// Frames and peak (x1000) this tool must produce from the two probes.
    ///
    /// The oracle is UNITY'S OWN DECODE of the same two files, on this build, from the one run that
    /// ever had m_DisableAudio flipped off: `F1-aud-aud-ogg PASS decoded 22050 samples 1ch 44100Hz
    /// peak=0,128` and `F1-aud-aud-mp3 PASS decoded 27648 samples 1ch 44100Hz peak=0,119`
    /// (docs\HANDOFF-2026-08-12.md:186-188). That is the strongest check available for this slice:
    /// the in-house decoder REPLACES those engine decoders, so it has to agree with them.
    ///
    /// It agrees on everything except ONE number, and the difference is MEASURED, not assumed:
    ///   * .ogg  22050 frames, peak 0,128 - identical to the engine's, exact.
    ///   * .mp3  peak 0,119 - identical to the engine's, to the thousandth.
    ///   * .mp3  24192 frames, where the engine handed back 27648. 24192 is 21 x 1152, which is
    ///     EXACTLY what the container declares (SourceAudio reads 24192 off the frame headers) -
    ///     Unity added three whole MPEG frames of its own decoder delay/padding on top. So NLayer
    ///     is the CLOSER of the two to the file, not the further, and this arm pins the number that
    ///     is right about the file rather than the number the old path happened to produce.
    /// The two peaks differ from each other (0,128 vs 0,119) because the .mp3 is lossy - so a
    /// decoder that quietly ran both files through one library would come out wrong on one of them.
    /// </summary>
    private static readonly Dictionary<string, int[]> Expected = new Dictionary<string, int[]>
    {
        { ".ogg", new[] { 22050, 128 } },   // frames, peak x1000
        { ".mp3", new[] { 24192, 119 } }
    };

    internal static string Run()
    {
        int checks = 0;
        List<string> bad = new List<string>();

        string lib = Lib();
        if (lib == null) return "SOURCEDECODE VOID - lib\\a1_probe.ogg not found from " + AppDomain.CurrentDomain.BaseDirectory;

        string detail = "";
        foreach (string ext in new string[] { ".ogg", ".mp3" })
        {
            string path = Path.Combine(lib, "a1_probe" + ext);
            string why;
            WwisePcm.Wav w = WwisePcm.ReadAudio(path, out why);
            Assert(w != null, "a1_probe" + ext + " decodes in-house (" + why + ")", ref checks, bad);
            if (w == null) continue;

            int frames = w.Pcm16.Length / (2 * Math.Max(1, w.Channels));
            SourceAudio.Info d = SourceAudio.DeclareFile(path, out why);
            Assert(d != null, ext + " declares itself, so there is an oracle to compare against",
                   ref checks, bad);
            if (d != null)
                // EXACT for both formats, and asserted that way deliberately. Under the old
                // engine-decoder path the .mp3 arm needed 4 x 1152 frames of slack for Unity's
                // padding; measured here, the in-house decode lands on the declared count to the
                // frame, so the slack would only be hiding a truncated or over-run decode.
                Assert(w.Channels == d.Channels && w.SampleRate == d.SampleRate && frames == d.Frames,
                       ext + " decoded " + w.Channels + "ch " + w.SampleRate + "Hz " + frames +
                       " frames vs the container's own " + d.Describe(), ref checks, bad);

            // Count AND amplitude, against the engine's own decode of the same file (see Expected).
            // A correctly sized buffer of ZEROES passes every count above and dies here.
            float peak = Peak(w.Pcm16);
            int[] want = Expected[ext];
            Assert(frames == want[0] && Math.Abs(peak * 1000f - want[1]) < 1.0f,
                   ext + " decoded " + frames + " frames peak=" + peak.ToString("0.000") +
                   ", expected " + want[0] + " frames peak=" + (want[1] / 1000f).ToString("0.000"),
                   ref checks, bad);

            // THE anti-vacuity arm. 440 Hz measured off the samples themselves.
            double hz = Frequency(w.Pcm16, w.Channels, w.SampleRate);
            Assert(Math.Abs(hz - ProbeHz) < 10.0,
                   ext + " decoded a " + ProbeHz + " Hz tone, measured " + hz.ToString("0.0") + " Hz",
                   ref checks, bad);
            detail += "\n  " + ext + " -> " + w.Channels + "ch " + w.SampleRate + "Hz " + frames +
                      " frames peak=" + peak.ToString("0.000") + " measured " + hz.ToString("0.0") + " Hz";
        }

        // The two formats must reach DIFFERENT decoders: an .mp3 renamed .ogg has to be refused, or
        // the dispatch is not dispatching and one library is silently handling both.
        string swapped = Path.Combine(Path.GetTempPath(), "ct-a7-swapped.ogg");
        File.Copy(Path.Combine(lib, "a1_probe.mp3"), swapped, true);
        try { RefuseFile(swapped, "an .mp3 renamed .ogg", ref checks, bad); }
        finally { try { File.Delete(swapped); } catch (IOException) { } }

        // Refusals: a reason, never an exception, never a plausible empty buffer.
        byte[] junk = new byte[4096];
        new Random(7).NextBytes(junk);
        RefuseBytes(junk, ".ogg", "4096 random bytes named .ogg", ref checks, bad);
        RefuseBytes(junk, ".mp3", "4096 random bytes named .mp3", ref checks, bad);
        RefuseBytes(new byte[0], ".ogg", "an empty .ogg", ref checks, bad);
        // ID3v2 header declaring 0 bytes of tag and nothing behind it - the shape the mp3 reader's
        // own tag strip produces when it runs off the end.
        RefuseBytes(new byte[] { (byte)'I', (byte)'D', (byte)'3', 4, 0, 0, 0, 0, 0, 0 }, ".mp3",
                    "an ID3 tag with no audio behind it", ref checks, bad);
        RefuseBytes(junk, ".flac", "a .flac (no decoder, refused by extension)", ref checks, bad);

        if (bad.Count > 0) return "SOURCEDECODE: " + bad.Count + " FAILURE(S) - " + string.Join("; ", bad.ToArray());
        return "SOURCEDECODE: ALL PASS, " + checks + " check(s)" + detail;
    }

    /// <summary>
    /// The tone's frequency, straight off the PCM. A Schmitt trigger rather than a bare sign test:
    /// an .mp3 is lossy and rings around zero, and a naive zero-crossing count would read that
    /// ringing as extra cycles. Counts full periods (-0,05 -> +0,05 transitions) over a window that
    /// STARTS at the first loud sample, which skips the mp3 decoder's leading silence.
    /// </summary>
    private static double Frequency(byte[] pcm16, int channels, int rate)
    {
        int frames = pcm16.Length / (2 * channels);
        int start = 0;
        while (start < frames && Math.Abs(S(pcm16, start * channels)) < 0.1f) start++;
        int window = Math.Min(rate / 2, frames - start);   // 0,5 s, or whatever is there
        if (window < rate / 10) return 0.0;

        int cycles = 0;
        bool armed = false;
        for (int i = start; i < start + window; i++)
        {
            float v = S(pcm16, i * channels);
            if (v < -0.05f) armed = true;
            else if (armed && v > 0.05f) { cycles++; armed = false; }
        }
        return cycles * (double)rate / window;
    }

    private static float S(byte[] pcm16, int sample)
    {
        return (short)(pcm16[sample * 2] | pcm16[sample * 2 + 1] << 8) / 32768f;
    }

    private static float Peak(byte[] pcm16)
    {
        int peak = 0;
        for (int i = 0; i + 1 < pcm16.Length; i += 2)
        {
            int s = (short)(pcm16[i] | pcm16[i + 1] << 8);
            if (s < 0) s = -s;
            if (s > peak) peak = s;
        }
        return peak / 32768f;
    }

    private static void RefuseBytes(byte[] bytes, string ext, string what, ref int checks, List<string> bad)
    {
        string path = Path.Combine(Path.GetTempPath(), "ct-a7-refuse" + ext);
        File.WriteAllBytes(path, bytes);
        try { RefuseFile(path, what, ref checks, bad); }
        finally { try { File.Delete(path); } catch (IOException) { } }
    }

    private static void RefuseFile(string path, string what, ref int checks, List<string> bad)
    {
        string why = null;
        WwisePcm.Wav got;
        try { got = WwisePcm.ReadAudio(path, out why); }
        catch (Exception ex)
        {
            // A throw here is the DEFECT this gate exists for: it would abort the whole bake.
            Assert(false, what + " must be REFUSED with a reason, but it threw " + ex.GetType().Name,
                   ref checks, bad);
            return;
        }
        Assert(got == null && !string.IsNullOrEmpty(why),
               what + " is refused with a reason and no exception (got: " +
               (got == null ? why : "DECODED " + got.Channels + "ch " + got.SampleRate + "Hz") + ")",
               ref checks, bad);
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

    private static void Assert(bool ok, string what, ref int checks, List<string> bad)
    {
        checks++;
        if (!ok) bad.Add(what);
    }
}
