using System;
using System.Collections.Generic;

namespace Morgott.ContentTool.Wwise
{
    /// <summary>
    /// The bench's "is this the right sound" bank: ONE embedded PCM sound behind ONE event, built by the same
    /// <see cref="BankGen.Build"/> the ct_audio gate proved in game (A1). Its bank, event and media ids are the
    /// tool's OWN names hashed - never a shipped media id - so loading it replaces nothing, and unloading it
    /// (the next preview does) silences only the preview. Unity-free, so the bank itself is proven offline.
    /// </summary>
    internal static class PreviewBank
    {
        internal const string Name = "ct_bench_preview";
        internal static readonly uint BankId = WwiseId.Hash("ct_bench_preview_bank");
        internal static readonly uint MediaId = WwiseId.Hash("ct_bench_preview_media");
        internal static uint EventId { get { return WwiseId.Hash(Name); } }

        /// <summary>A preview is a listen, not a playthrough: longer sources are cut here so a five-minute
        /// track does not become a 50 MB bank.</summary>
        internal const int MaxSeconds = 45;

        /// <summary>PCM16 in, the bank bytes out; the event to post is <see cref="EventId"/>.</summary>
        internal static byte[] Build(byte[] pcm16, int channels, int sampleRate)
        {
            if (pcm16 == null || pcm16.Length == 0 || channels <= 0 || sampleRate <= 0)
                throw new ArgumentException("no audio to preview");
            long max = (long)MaxSeconds * sampleRate * channels * 2;
            if (pcm16.Length > max)
            {
                byte[] cut = new byte[max];
                Buffer.BlockCopy(pcm16, 0, cut, 0, (int)max);
                pcm16 = cut;
            }
            byte[] wem = WwisePcm.BuildWem(pcm16, channels, sampleRate);
            return BankGen.Build(BankId, new List<BankGen.Src>
            {
                new BankGen.Src { Name = Name, Wem = wem, MediaId = MediaId, Stream = false }
            });
        }
    }
}
