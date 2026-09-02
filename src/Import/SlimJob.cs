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

        /// <summary>
        /// Run the whole pipeline and return the sentence to show the author.
        /// </summary>
        /// <param name="src">Source .glb path.</param>
        /// <param name="dst">Destination .glb path; may equal src for an in-place trim.</param>
        /// <param name="drop">Clip indices to drop, as the census numbered them.</param>
        /// <param name="force">Override the mandatory-clip and rigged-character guards.</param>
        /// <param name="cancel">Cooperative cancellation, checked at every stage boundary.</param>
        /// <param name="publish">Progress sink; may be null, and is called on the calling thread.</param>
        /// <exception cref="OperationCanceledException">The run was cancelled; nothing was written.</exception>
        /// <exception cref="InvalidOperationException">The guard refused; its words are the message.</exception>
        internal static string Execute(string src, string dst, HashSet<int> drop, bool force,
                                       CancellationToken cancel, Action<SlimProgress> publish)
        {
            if (drop == null) drop = new HashSet<int>();
            string tmp = dst + ".ct_tmp";
            try
            {
                At(cancel, publish, 0, "Reading " + Path.GetFileName(src));
                GlbDocument doc = GlbDocument.Load(src);

                At(cancel, publish, 1, "Listing clips");
                int clips = GlbSlim.Census(doc).Count;

                At(cancel, publish, 2, "Checking what a trim would touch");
                string refusal = GlbSlim.Guard(doc, drop, force);
                if (refusal != null) throw new InvalidOperationException(refusal);

                At(cancel, publish, 3, "Dropping " + drop.Count + " of " + clips + " clip(s)");
                long delta = GlbSlim.Trim(doc, drop);

                At(cancel, publish, 4, "Writing " + Path.GetFileName(dst));
                doc.Write(tmp);
                // The swap, not a write onto the destination: whatever was there is whole until this
                // line, and whole again after it.
                if (File.Exists(dst)) File.Replace(tmp, dst, null);
                else File.Move(tmp, dst);

                string done = "dropped " + drop.Count + " of " + clips + " clip(s), " +
                              (delta < 0 ? (-delta) + " B freed" : "no bytes freed");
                Publish(publish, new SlimProgress("Done", Stages.Length, Stages.Length, done));
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

        /// <summary>A stage boundary: refuse to start it when the run is cancelled, then say so.</summary>
        private static void At(CancellationToken cancel, Action<SlimProgress> publish, int stage, string message)
        {
            cancel.ThrowIfCancellationRequested();
            Publish(publish, new SlimProgress(Stages[stage], stage, Stages.Length, message));
        }

        private static void Publish(Action<SlimProgress> publish, SlimProgress snapshot)
        {
            if (publish != null) publish(snapshot);
        }
    }
}
