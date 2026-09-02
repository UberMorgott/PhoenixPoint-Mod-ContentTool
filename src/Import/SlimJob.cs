using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace Morgott.ContentTool.Import
{
    /// <summary>
    /// Where a slim run has got to, frozen. Immutable on purpose: the worker hands whole snapshots
    /// to a volatile field and the UI thread reads one, so a repaint can never catch a half-written
    /// stage - it either sees the old snapshot or the new one.
    /// </summary>
    internal sealed class SlimProgress
    {
        internal readonly string Stage;
        internal readonly int Done;
        internal readonly int Total;
        internal readonly string Message;

        internal SlimProgress(string stage, int done, int total, string message)
        {
            Stage = stage;
            Done = done;
            Total = total;
            Message = message;
        }
    }

    /// <summary>
    /// The slim run: load, census, guard, trim, save. Execute is pure and has no thread affinity, so
    /// the cancel path can be proven synchronously in a gate rather than raced in a game session;
    /// Start is the same call on the pool, the way ModelDoctor.Start runs its preflight
    /// (src\Dev\ModelDoctor.cs:229).
    ///
    /// The save is the reason this class exists at all. A trim rewrites a file the author cannot get
    /// back, so the run writes a sibling .ct_tmp and only then swaps it into place - a cancel, a
    /// refusal or a crash leaves the destination exactly as it was.
    /// </summary>
    internal static class SlimJob
    {
        /// <summary>The five checkpoints. A cancel between any two of them is seen before the next
        /// one does any work, and the last of them is the only one that touches the destination.</summary>
        private static readonly string[] Stages = { "Load", "Census", "Guard", "Trim", "Write" };

        /// <summary>The zip run's six checkpoints. Verify is last and reads the file back through the
        /// game's own importer, because "it still animates" is the only question worth answering.</summary>
        private static readonly string[] ZipStages = { "Load", "Plan", "Guard", "Zip", "Write", "Verify" };

        /// <summary>
        /// Run the whole pipeline and return the sentence to show the author.
        /// </summary>
        /// <param name="src">Source .glb path.</param>
        /// <param name="dst">Destination .glb path; may equal src for an in-place trim.</param>
        /// <param name="drop">Clip indices to drop, as the census numbered them.</param>
        /// <param name="force">Override the mandatory-clip and rigged-character guards.</param>
        /// <param name="cancel">Cooperative cancellation, checked at every stage boundary.</param>
        /// <param name="publish">Progress sink; may be null, and is called on the calling thread.</param>
        /// <exception cref="OperationCanceledException">The run was cancelled before the swap; nothing was written.
        /// A cancel that lands after the swap is not one - the file is there, and the run returns like any other.</exception>
        /// <exception cref="InvalidOperationException">The guard refused; its words are the message.</exception>
        internal static string Execute(string src, string dst, HashSet<int> drop, bool force,
                                       CancellationToken cancel, Action<SlimProgress> publish)
        {
            if (drop == null) drop = new HashSet<int>();
            string tmp = dst + "." + Guid.NewGuid().ToString("N") + ".ct_tmp";
            bool swapped = false;
            string done = null;
            try
            {
                At(cancel, publish, Stages, 0, "Reading " + Path.GetFileName(src));
                GlbDocument doc = GlbDocument.Load(src);

                At(cancel, publish, Stages, 1, "Listing clips");
                int clips = GlbSlim.Census(doc).Count;

                At(cancel, publish, Stages, 2, "Checking what a trim would touch");
                string refusal = GlbSlim.Guard(doc, drop, force);
                if (refusal != null) throw new InvalidOperationException(refusal);

                At(cancel, publish, Stages, 3, "Dropping " + drop.Count + " of " + clips + " clip(s)");
                long delta = GlbSlim.Trim(doc, drop);

                At(cancel, publish, Stages, 4, "Writing " + Path.GetFileName(dst));
                doc.Write(tmp);
                // The swap, not a write onto the destination: whatever was there is whole until this
                // line, and whole again after it.
                if (File.Exists(dst)) File.Replace(tmp, dst, null);
                else File.Move(tmp, dst);
                swapped = true;

                done = "dropped " + drop.Count + " of " + clips + " clip(s), " +
                       (delta < 0 ? (-delta) + " B freed" : "no bytes freed");
                Publish(publish, new SlimProgress("Done", Stages.Length, Stages.Length, done));
                return done;
            }
            catch (OperationCanceledException)
            {
                if (!swapped) throw;
                return done;
            }
            finally
            {
                // ponytail: best-effort delete - a temp still held by a scanner is left on disk
                // rather than turned into a failure for a run that already swapped the file in.
                // Upgrade path = sweep stale .ct_tmp siblings when the panel opens.
                try { if (File.Exists(tmp)) File.Delete(tmp); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }

        /// <summary>
        /// The zip run: load, plan, guard, rewrite, save, read back. Same shape and same guarantees as
        /// <see cref="Execute"/> - pure, no thread affinity, and the destination is only ever touched
        /// by the swap of a finished .ct_tmp.
        /// </summary>
        /// <param name="constant">Collapse curves that never move to two endpoint keys.</param>
        /// <param name="quantise">Store rotation outputs as normalized int16.</param>
        /// <exception cref="OperationCanceledException">Cancelled before the swap; nothing was written.</exception>
        /// <exception cref="InvalidOperationException">The guard refused; its words are the message.</exception>
        internal static string Zip(string src, string dst, bool constant, bool quantise,
                                   CancellationToken cancel, Action<SlimProgress> publish)
        {
            string tmp = dst + "." + Guid.NewGuid().ToString("N") + ".ct_tmp";
            bool swapped = false;
            string done = null;
            try
            {
                At(cancel, publish, ZipStages, 0, "Reading " + Path.GetFileName(src));
                GlbDocument doc = GlbDocument.Load(src);
                long was = new FileInfo(src).Length;

                At(cancel, publish, ZipStages, 1, "Reading every sampler");
                At(cancel, publish, ZipStages, 2, "Checking what a rewrite would touch");
                // force, because zip drops no clip: neither the mandatory-clip nor the
                // rigged-character arm can apply to it, and what is left is the sparse / Draco /
                // foreign-buffer / view-extension refusal, which does.
                string refusal = GlbSlim.Guard(doc, new HashSet<int>(), true);
                if (refusal != null) throw new InvalidOperationException(refusal);

                At(cancel, publish, ZipStages, 3, "Rewriting the curves");
                GlbZip.Stats stats = GlbZip.Zip(doc, constant, quantise);

                At(cancel, publish, ZipStages, 4, "Writing " + Path.GetFileName(dst));
                doc.Write(tmp);
                long now = new FileInfo(tmp).Length;

                // A REWRITE THAT MAKES THE FILE BIGGER IS NOT A SAVE. On a .glb whose animation shares
                // bufferViews with mesh data the old keys cannot be freed, so the new ones are added to
                // them and the file grows (lib\u8_probe.glb, +7.9%). Report it and leave the
                // destination alone; the finally below takes the temp with it.
                if (now >= was)
                {
                    done = "would grow by " + (now - was) + " B (" + was + " B -> " + now + " B), so " +
                           "nothing was written - this .glb interleaves animation with mesh data in " +
                           "shared bufferViews, and the old keys cannot be freed";
                    Publish(publish, new SlimProgress("Done", ZipStages.Length, ZipStages.Length, done));
                    return done;
                }

                if (File.Exists(dst)) File.Replace(tmp, dst, null);
                else File.Move(tmp, dst);
                swapped = true;

                done = stats.Collapsed + " curve(s) collapsed, " + stats.Quantised + " rotation(s) as " +
                       "int16, " + stats.Skipped + " left alone, " + stats.Shared + " shared; " + was +
                       " B -> " + now + " B (-" + ((was - now) * 100f / was).ToString("0.#") + "%)";

                At(cancel, publish, ZipStages, 5, "Reading " + Path.GetFileName(dst) + " back");
                done += "; " + ReadBack(dst);
                Publish(publish, new SlimProgress("Done", ZipStages.Length, ZipStages.Length, done));
                return done;
            }
            catch (OperationCanceledException)
            {
                if (!swapped) throw;
                return done;
            }
            finally
            {
                // Same best-effort delete as Execute, for the same reason.
                try { if (File.Exists(tmp)) File.Delete(tmp); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }

        /// <summary>Zip on the pool, exactly as <see cref="Start"/> runs Execute. Both callbacks land
        /// on the WORKER thread.</summary>
        internal static void StartZip(string src, string dst, bool constant, bool quantise,
                                      CancellationTokenSource cts, Action<SlimProgress> onProgress,
                                      Action<string> onComplete)
        {
            CancellationToken cancel = cts == null ? CancellationToken.None : cts.Token;
            ThreadPool.QueueUserWorkItem(delegate
            {
                string result;
                try
                {
                    result = Zip(src, dst, constant, quantise, cancel, onProgress);
                }
                catch (OperationCanceledException)
                {
                    result = "cancelled - " + Path.GetFileName(dst) + " was left alone";
                }
                catch (Exception ex)
                {
                    result = ex.Message;
                }
                if (onComplete != null) onComplete(result);
            });
        }

        /// <summary>The written file back through the GAME'S OWN importer, which is the only reader
        /// whose opinion matters: a rewrite that produces a file the game cannot animate has failed,
        /// however small it is. A failure here is REPORTED and not thrown - the swap already happened,
        /// so the author needs the sentence, not a stack trace over a file that is already there.</summary>
        private static string ReadBack(string path)
        {
            try
            {
                var clips = new List<SampledClip>();
                GlbReader.Read(File.ReadAllBytes(path), clips);
                return "reads back as " + clips.Count + " clip(s)";
            }
            catch (Exception ex)
            {
                return "but it does not read back: " + ex.Message;
            }
        }

        /// <summary>
        /// Execute on the pool, like ModelDoctor.Start (src\Dev\ModelDoctor.cs:229). onProgress and
        /// onComplete land on the WORKER thread - a UI caller stores the snapshot in a volatile field
        /// and reads it back in OnGUI rather than drawing from here.
        /// </summary>
        internal static void Start(string src, string dst, HashSet<int> drop, bool force,
                                   CancellationTokenSource cts, Action<SlimProgress> onProgress,
                                   Action<string> onComplete)
        {
            // The caller's set belongs to the caller and its checkboxes keep moving while this runs.
            var mine = new HashSet<int>(drop ?? new HashSet<int>());
            CancellationToken cancel = cts == null ? CancellationToken.None : cts.Token;
            ThreadPool.QueueUserWorkItem(delegate
            {
                string result;
                try
                {
                    result = Execute(src, dst, mine, force, cancel, onProgress);
                }
                catch (OperationCanceledException)
                {
                    result = "cancelled - " + Path.GetFileName(dst) + " was left alone";
                }
                catch (Exception ex)
                {
                    result = ex.Message;
                }
                if (onComplete != null) onComplete(result);
            });
        }

        /// <summary>A stage boundary: refuse to start it when the run is cancelled, then say so. The
        /// table is a parameter because the two runs have different checkpoints and the same rule.</summary>
        private static void At(CancellationToken cancel, Action<SlimProgress> publish, string[] stages,
                               int stage, string message)
        {
            cancel.ThrowIfCancellationRequested();
            Publish(publish, new SlimProgress(stages[stage], stage, stages.Length, message));
        }

        private static void Publish(Action<SlimProgress> publish, SlimProgress snapshot)
        {
            if (publish != null) publish(snapshot);
        }
    }
}
