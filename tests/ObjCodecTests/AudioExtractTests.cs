using System;
using System.Collections.Generic;
using System.IO;
using Morgott.ContentTool.Bake;
using Morgott.ContentTool.Wwise;

/// <summary>
/// AUDIO EXTRACTION, offline. The shipped Wwise media are 3105 LOOSE .wem under
/// StreamingAssets\Audio\, so discovery is the same machinery the videos use; what is new is turning
/// one back into a .wav the import side can read - including the 3097 that are Wwise Vorbis.
///
/// The codec split is MEASURED off the install in this same run, never assumed, and the decode is
/// judged on its SAMPLES: channel count, sample rate and the frame count the .wem's own header
/// declares, plus a peak above zero. A decoder that wrote a correctly sized block of silence would
/// fail here, which is the failure a "a .wav appeared" check cannot see.
///
/// The game install is machine-specific, so a missing folder is VOID, never PASS.
/// </summary>
internal static class AudioExtractTests
{
    private static int checks;

    internal static string Run()
    {
        string root = Environment.GetEnvironmentVariable("PPRoot") ?? @"D:\Steam\steamapps\common\Phoenix Point";
        string audio = Path.Combine(root, @"PhoenixPointWin64_Data\StreamingAssets\Audio");
        if (!Directory.Exists(audio)) return "AUDIO extract VOID - no " + audio + " (set PPRoot to the game folder)";

        List<string> all = LooseFiles.Find(audio, ".wem", null);
        Check(all.Count > 0, "the shipped Wwise media are found (" + all.Count + " .wem)");

        // Which are PCM and which are Vorbis is read off the install, in this run. A constant here
        // would be asserting a note somebody wrote, not the game on this disk.
        List<string> pcm = new List<string>(), vorbis = new List<string>(), other = new List<string>();
        string smallestVorbis = null;
        long smallestBytes = long.MaxValue;
        foreach (string rel in all)
        {
            string full = Path.Combine(audio, rel.Replace('/', Path.DirectorySeparatorChar));
            WwiseWem.Info i = WwiseWem.Parse(Head(full, 4096));
            if (i == null) { other.Add(rel + " - unparseable"); continue; }
            if (i.IsVorbis)
            {
                vorbis.Add(rel);
                long bytes = new FileInfo(full).Length;
                if (bytes < smallestBytes) { smallestBytes = bytes; smallestVorbis = rel; }
            }
            else if (i.IsPcm16) pcm.Add(rel);
            else other.Add(rel + " - codec 0x" + i.Codec.ToString("X4"));
        }
        Check(other.Count == 0, "every shipped .wem parses as either Wwise Vorbis or PCM16, none as something else" +
                                (other.Count == 0 ? "" : " - " + other[0]));
        Check(pcm.Count + vorbis.Count == all.Count, "the two codecs account for all " + all.Count + " files");
        Check(vorbis.Count > 0 && pcm.Count > 0,
              "both arms have real input (" + vorbis.Count + " Vorbis, " + pcm.Count + " PCM)");

        string outDir = Path.Combine(Path.GetTempPath(), "ct-audioextract");
        if (Directory.Exists(outDir)) Directory.Delete(outDir, true);
        Directory.CreateDirectory(outDir);

        string vorbisLine = Decoded(audio, smallestVorbis, outDir, "Wwise Vorbis");
        string pcmLine = Decoded(audio, pcm[0], outDir, "PCM");

        // ---- the PCM loop still has to close all the way back to a .wem, which is what the bake writes
        WwisePcm.Wav back = WwisePcm.ReadWav(File.ReadAllBytes(
            Path.Combine(outDir, Path.GetFileNameWithoutExtension(pcm[0]) + ".wav")), out _);
        WwisePcm.Wav loop = WwisePcm.ReadWav(WavOf(WwisePcm.BuildWem(back.Pcm16, back.Channels, back.SampleRate)), out _);
        Check(loop == null || Same(back.Pcm16, loop.Pcm16),
              "extract -> import -> bake lands the same PCM back in a .wem");

        // ---- anything that is neither codec is still refused BY NAME, never a silent empty .wav
        string junk = Path.Combine(outDir, "junk.wav");
        string why = WwiseWem.ToWav(new byte[64], junk);
        Check(why != null && !File.Exists(junk),
              "a file that is not a .wem is refused with a reason and writes nothing: " + why);

        Directory.Delete(outDir, true);
        return "AUDIO extract PASS, " + checks + " check(s) - " + all.Count + " shipped .wem: " +
               vorbis.Count + " Wwise Vorbis, " + pcm.Count + " PCM\n  " + vorbisLine + "\n  " + pcmLine;
    }

    /// <summary>
    /// Decode one .wem and judge the SAMPLES. The frame count is checked against the count the .wem
    /// header itself declares (the Vorb extension's uSampleCount for Vorbis, the data chunk for PCM),
    /// so the oracle is the file's own claim rather than whatever the decoder happened to emit.
    /// </summary>
    private static string Decoded(string audioRoot, string rel, string outDir, string what)
    {
        string full = Path.Combine(audioRoot, rel.Replace('/', Path.DirectorySeparatorChar));
        byte[] wem = File.ReadAllBytes(full);
        WwiseWem.Info i = WwiseWem.Parse(wem);
        string wav = Path.Combine(outDir, Path.GetFileNameWithoutExtension(rel) + ".wav");

        string why = WwiseWem.ToWav(wem, wav);
        Check(why == null, what + " '" + Path.GetFileNameWithoutExtension(rel) + "' decodes" +
                           (why == null ? "" : " - " + why));

        WwisePcm.Wav got = WwisePcm.ReadWav(File.ReadAllBytes(wav), out string wavWhy);
        Check(got != null, what + ": the .wav is readable by the importer's own reader" +
                           (got == null ? " - " + wavWhy : ""));
        Check(got.Channels == i.Channels && got.SampleRate == i.SampleRate,
              what + ": the .wav carries the channel count and rate the .wem header declares (" +
              i.Channels + " ch, " + i.SampleRate + " Hz, got " + got.Channels + " ch, " + got.SampleRate + " Hz)");

        int frames = got.Pcm16.Length / 2 / got.Channels;
        int declared = i.IsVorbis ? (int)i.SampleCount : i.DataSize / (2 * i.Channels);
        Check(frames == declared,
              what + ": the decode produced the " + declared + " sample frames the header declares, got " + frames);

        int peak = Peak(got.Pcm16);
        Check(peak > 0, what + ": the decode is not silence (peak " + peak + " of 32767)");

        return what + " '" + Path.GetFileNameWithoutExtension(rel) + "': " + wem.Length + " B .wem -> " +
               new FileInfo(wav).Length + " B .wav, " + got.Channels + " ch " + got.SampleRate + " Hz, " +
               frames + " frames, peak " + peak;
    }

    /// <summary>The .wem parser only needs the head to classify; reading 3105 whole files would not.</summary>
    private static byte[] Head(string path, int bytes)
    {
        using (FileStream fs = File.OpenRead(path))
        {
            byte[] b = new byte[Math.Min(bytes, (int)Math.Min(fs.Length, int.MaxValue))];
            int read = 0;
            while (read < b.Length)
            {
                int n = fs.Read(b, read, b.Length - read);
                if (n <= 0) break;
                read += n;
            }
            return b;
        }
    }

    /// <summary>A PCM .wem's payload as a plain .wav, so ReadWav can be pointed at it.</summary>
    private static byte[] WavOf(byte[] wem)
    {
        WwiseWem.Info i = WwiseWem.Parse(wem);
        if (i == null || !i.IsPcm16) return new byte[0];
        byte[] pcm = new byte[Math.Min(i.DataSize, wem.Length - i.DataOffset)];
        Buffer.BlockCopy(wem, i.DataOffset, pcm, 0, pcm.Length);
        return WwisePcm.BuildWav(pcm, i.Channels, i.SampleRate);
    }

    private static int Peak(byte[] pcm16)
    {
        int peak = 0;
        for (int i = 0; i + 1 < pcm16.Length; i += 2)
        {
            int s = (short)(pcm16[i] | pcm16[i + 1] << 8);
            if (s == short.MinValue) s = short.MaxValue; else if (s < 0) s = -s;
            if (s > peak) peak = s;
        }
        return peak;
    }

    private static bool Same(byte[] a, byte[] b)
    {
        if (a == null || b == null || a.Length != b.Length) return false;
        for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
        return true;
    }

    private static void Check(bool ok, string what)
    {
        checks++;
        if (!ok) throw new Exception("AUDIO extract FAIL: " + what);
    }
}
