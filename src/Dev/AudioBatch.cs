using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using Morgott.ContentTool.Wwise;

namespace Morgott.ContentTool.Dev
{
    /// <summary>
    /// The body of `ct_extract audio --all`, split out of Extract so it carries no UnityEngine type.
    /// That is what lets it run on a WORKER THREAD: decoding up to 3105 .wem inside the console
    /// command held the main thread for minutes - the game froze and its own progress lines could
    /// not render until the whole run was over. Everything here is file IO, NVorbis and read-only
    /// lookups on a SoundbankNames that was loaded before the thread started.
    ///
    /// ONE run at a time: two would decode into the same folder and race on index.csv. The second
    /// asker is refused by name (<see cref="TryBegin"/>), never queued.
    ///
    /// A failing decode is COUNTED AND NAMED, never fatal: one unreadable file must not cost the
    /// other 3104. index.csv covers ALL media including the in-bank ones, so the name -> id map
    /// survives outside the console for the sounds this cannot write.
    /// </summary>
    internal sealed class AudioBatch
    {
        private static int running;

        private readonly SoundbankNames names;
        private readonly string audioRoot, dir, filter;
        private readonly List<string> rel, stems;
        private readonly List<int> todo = new List<int>();

        private int processed, done;
        private volatile bool finished;

        /// <summary>How many loose media the filter matched - the run's denominator.</summary>
        internal int Matched { get { return todo.Count; } }
        /// <summary>Decodes attempted so far, pass or fail. Safe to read from any thread.</summary>
        internal int Processed { get { return Volatile.Read(ref processed); } }
        internal int Done { get { return Volatile.Read(ref done); } }
        internal bool Finished { get { return finished; } }
        /// <summary>The closing report; set before <see cref="Finished"/> turns true.</summary>
        internal string Summary { get; private set; }
        internal string Dir { get { return dir; } }

        private AudioBatch(SoundbankNames names, string audioRoot, List<string> rel, List<string> stems,
                           string dir, string filter)
        {
            this.names = names; this.audioRoot = audioRoot; this.rel = rel; this.stems = stems;
            this.dir = dir; this.filter = filter;
            for (int k = 0; k < stems.Count; k++)
                if (names.Matches(SoundbankNames.IdOf(stems[k]), stems[k], filter)) todo.Add(k);
        }

        /// <summary>
        /// Claims the one run slot. Null plus a refusal when a run is already going; the slot is
        /// released only when <see cref="Run"/> returns, so the caller MUST run what it was given.
        /// </summary>
        /// <param name="rel">The loose .wem, relative to <paramref name="audioRoot"/>, '/'-separated.</param>
        /// <param name="stems">Their bare file names, index for index with <paramref name="rel"/>.</param>
        internal static AudioBatch TryBegin(SoundbankNames names, string audioRoot, List<string> rel,
                                            List<string> stems, string dir, string filter, out string refusal)
        {
            if (Interlocked.CompareExchange(ref running, 1, 0) != 0)
            {
                refusal = "ct_extract REFUSED - an 'audio --all' run is already decoding; wait for its " +
                          "'extracted N of M' line in the log, then run it again";
                return null;
            }
            refusal = null;
            try { return new AudioBatch(names, audioRoot, rel, stems, dir, filter); }
            catch (Exception) { Volatile.Write(ref running, 0); throw; }
        }

        /// <summary>The whole decode, synchronously. The worker thread's entry; tests call it directly.</summary>
        internal void Run()
        {
            try { Summary = Decode(); }
            catch (Exception ex)
            {
                Summary = "ct_extract audio --all THREW after " + Processed + " of " + Matched + ": " + ex;
            }
            finally
            {
                Volatile.Write(ref running, 0);
                finished = true;
            }
        }

        private string Decode()
        {
            Directory.CreateDirectory(dir);
            var wavByMedia = new Dictionary<uint, string>();
            var failures = new List<string>();
            foreach (int k in todo)
            {
                string stem = stems[k];
                uint id = SoundbankNames.IdOf(stem);
                string wav = Path.Combine(dir, SoundbankNames.WavName(names.Name(id), id, stem) + ".wav");
                string why;
                try
                {
                    why = WwiseWem.ToWav(File.ReadAllBytes(
                        Path.Combine(audioRoot, rel[k].Replace('/', Path.DirectorySeparatorChar))), wav);
                }
                catch (Exception ex) { why = ex.GetType().Name + ": " + ex.Message; }
                if (why == null) { Interlocked.Increment(ref done); if (id != 0) wavByMedia[id] = Path.GetFileName(wav); }
                else failures.Add(stem + " (" + (names.Name(id) == "" ? "unnamed" : names.Name(id)) + ") - " + why);
                Interlocked.Increment(ref processed);
            }

            int inBank = names.InBankMatches(stems, filter);
            string csv = Path.Combine(dir, "index.csv");
            Morgott.ContentTool.IO.AtomicFile.WriteText(csv, names.Csv(stems, wavByMedia), new UTF8Encoding(false));

            StringBuilder b = new StringBuilder();
            b.Append("ct_extract wrote ").Append(csv).Append(" (").Append(names.Count)
             .Append(" named media").Append(names.Why == null ? "" : " - NO NAMES: " + names.Why).Append(")");
            for (int i = 0; i < failures.Count; i++) b.Append("\n  FAILED ").Append(failures[i]);
            b.Append("\nextracted ").Append(Done).Append(" of ").Append(Matched)
             .Append(" loose media (").Append(inBank).Append(" in-bank skipped) into ").Append(dir);
            if (failures.Count > 0) b.Append(" - ").Append(failures.Count).Append(" FAILED, named above");
            return b.ToString();
        }
    }
}
