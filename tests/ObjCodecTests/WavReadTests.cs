using System;
using System.Collections.Generic;
using System.IO;
using Morgott.ContentTool.Wwise;

/// <summary>
/// Which .wav files the tool's OWN reader accepts - offline, because WwisePcm carries no
/// UnityEngine type. The engine cannot answer this question at all: Phoenix Point runs with Unity's
/// audio subsystem shut (gate F1 measured outputSampleRate=0 and AudioSettings.Reset returning
/// false), so UnityWebRequestMultimedia decodes nothing here, .wav included. This reader is
/// therefore the ONLY audio route the tool has, and its accepted set is a measured fact rather than
/// a remembered one.
/// </summary>
internal static class WavReadTests
{
    internal static string Run()
    {
        int checks = 0;
        List<string> bad = new List<string>();

        // One waveform, written at every width the reader claims. A full-scale square wave means
        // the expected 16-bit value is exact at every depth, so a mis-scaled branch cannot hide
        // inside a rounding tolerance.
        foreach (int bits in new[] { 8, 16, 24, 32 })
        {
            string why;
            WwisePcm.Wav w = WwisePcm.ReadWav(Wav(1, bits, 1, 44100, 4), out why);
            Assert(w != null, bits + "-bit integer .wav is read (" + why + ")", ref checks, bad);
            if (w == null) continue;
            Assert(w.Channels == 1 && w.SampleRate == 44100 && w.Pcm16.Length == 8,
                   bits + "-bit .wav normalises to 4 frames of 16-bit mono", ref checks, bad);
            short first = BitConverter.ToInt16(w.Pcm16, 0);
            Assert(first > 32000, bits + "-bit full-scale sample survives as " + first, ref checks, bad);
        }
        foreach (int bits in new[] { 32, 64 })
        {
            string why;
            WwisePcm.Wav w = WwisePcm.ReadWav(Wav(3, bits, 2, 48000, 4), out why);
            Assert(w != null, bits + "-bit float .wav is read (" + why + ")", ref checks, bad);
            if (w == null) continue;
            Assert(w.Channels == 2 && w.SampleRate == 48000 && w.Pcm16.Length == 16,
                   bits + "-bit float .wav normalises to 4 stereo frames", ref checks, bad);
            Assert(BitConverter.ToInt16(w.Pcm16, 0) > 32000,
                   bits + "-bit float full-scale sample survives", ref checks, bad);
        }

        // WAVE_FORMAT_EXTENSIBLE is what every modern editor writes: the real tag lives in the GUID.
        string ext;
        Assert(WwisePcm.ReadWav(Extensible(Wav(1, 16, 1, 44100, 4)), out ext) != null,
               "WAVE_FORMAT_EXTENSIBLE .wav is read (" + ext + ")", ref checks, bad);

        // Trust boundary: the refusals must name the cause, or a bad file becomes a silent bad bank.
        Refuse(new byte[10], "not a RIFF", ref checks, bad);
        Refuse(Wav(0x0055, 16, 1, 44100, 4), "compressed", ref checks, bad);      // MP3-in-RIFF
        Refuse(Wav(3, 16, 1, 44100, 4), "float", ref checks, bad);                // float tag, int width

        if (bad.Count > 0) return "WAV: " + bad.Count + " FAILURE(S) - " + string.Join("; ", bad.ToArray());
        return "WAV: ALL PASS, " + checks + " check(s)";
    }

    private static void Refuse(byte[] file, string expect, ref int checks, List<string> bad)
    {
        string why;
        WwisePcm.Wav w = WwisePcm.ReadWav(file, out why);
        Assert(w == null && why != null && why.IndexOf(expect, StringComparison.OrdinalIgnoreCase) >= 0,
               "refused with a message naming '" + expect + "' (got: " + (w == null ? why : "ACCEPTED") + ")",
               ref checks, bad);
    }

    private static void Assert(bool ok, string what, ref int checks, List<string> bad)
    {
        checks++;
        if (!ok) bad.Add(what);
    }

    /// <summary>A minimal RIFF/WAVE whose every frame is full scale for the declared format.</summary>
    private static byte[] Wav(int tag, int bits, int channels, int rate, int frames)
    {
        int bytes = bits / 8, data = frames * channels * bytes;
        MemoryStream ms = new MemoryStream();
        BinaryWriter w = new BinaryWriter(ms);
        w.Write(new[] { 'R', 'I', 'F', 'F' }); w.Write(36 + data); w.Write(new[] { 'W', 'A', 'V', 'E' });
        w.Write(new[] { 'f', 'm', 't', ' ' }); w.Write(16);
        w.Write((ushort)tag); w.Write((ushort)channels); w.Write(rate);
        w.Write(rate * channels * bytes); w.Write((ushort)(channels * bytes)); w.Write((ushort)bits);
        w.Write(new[] { 'd', 'a', 't', 'a' }); w.Write(data);
        for (int i = 0; i < frames * channels; i++)
        {
            if (tag == 3) { if (bits == 64) w.Write(1.0); else w.Write(1.0f); }
            else if (bits == 8) w.Write((byte)255);
            else for (int b = 0; b < bytes; b++) w.Write((byte)(b == bytes - 1 ? 0x7F : 0xFF));
        }
        return ms.ToArray();
    }

    /// <summary>Rewrites a plain fmt chunk as WAVE_FORMAT_EXTENSIBLE, tag 0xFFFE + the tag in the GUID.</summary>
    private static byte[] Extensible(byte[] plain)
    {
        byte[] head = new byte[plain.Length + 24];
        Array.Copy(plain, head, 20);                       // through 'fmt ' + size
        head[16] = 40;                                     // fmt chunk size
        Array.Copy(plain, 20, head, 20, 16);               // the 16-byte fmt body
        head[20] = 0xFE; head[21] = 0xFF;                  // WAVE_FORMAT_EXTENSIBLE
        head[36] = 22;                                     // cbSize
        head[44] = plain[20]; head[45] = plain[21];        // the real tag, first 2 bytes of the GUID
        Array.Copy(plain, 36, head, 60, plain.Length - 36); // the data chunk, moved by 24
        return head;
    }
}
