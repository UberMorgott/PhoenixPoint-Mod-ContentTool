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

        /// <summary>The zip run's six checkpoints. Verify reads the TEMP back through the game's own
        /// importer before Write swaps it in, because "it still animates" is the only question worth
        /// answering, and a file that does not must never reach the destination.</summary>
        private static readonly string[] ZipStages = { "Load", "Plan", "Guard", "Zip", "Verify", "Write" };

        /// <summary>The skel run's six checkpoints. Verify comes BEFORE Write for the same reason the
        /// zip run's does: it asks the finished TEMP - with no plan in hand, the only form of the
        /// question the game ever asks - whether the prototype's bones are now there, and a rewrite
        /// that cannot answer must not replace the author's file.</summary>
        private static readonly string[] SkelStages = { "Load", "Plan", "Validate", "Rewrite", "Verify", "Write" };

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
        /// The zip run: load, plan, guard, rewrite, read back, save. Same shape and same guarantees as
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

                At(cancel, publish, ZipStages, 4, "Reading " + Path.GetFileName(dst) + " back");
                int clips;
                try { clips = ReadBack(tmp); }
                catch (Exception ex)
                {
                    done = "zipped file does not import: " + ex.Message + " - destination left alone";
                    Publish(publish, new SlimProgress("Done", ZipStages.Length, ZipStages.Length, done));
                    return done;
                }

                At(cancel, publish, ZipStages, 5, "Writing " + Path.GetFileName(dst));
                if (File.Exists(dst)) File.Replace(tmp, dst, null);
                else File.Move(tmp, dst);
                swapped = true;

                done = stats.Collapsed + " curve(s) collapsed, " + stats.Quantised + " rotation(s) as " +
                       "int16, " + stats.Skipped + " left alone, " + stats.Shared + " shared; " + was +
                       " B -> " + now + " B (-" + ((was - now) * 100f / was).ToString("0.#") + "%); " +
                       "reads back as " + clips + " clip(s)";
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

        /// <summary>
        /// The skeleton run: load, read the plan, validate, rewrite, verify, save. Same shape and same
        /// guarantees as <see cref="Execute"/> - pure, no thread affinity, and the destination is only
        /// ever touched by the swap of a finished .ct_tmp.
        /// </summary>
        /// <param name="planPath">the .skelplan.json to apply.</param>
        /// <param name="targetNames">the prototype's bindable bones, for the closing Verify. Null with
        /// <paramref name="targetPaths"/> makes the sentence say the rewrite happened and claim nothing
        /// about binding.</param>
        /// <param name="targetPaths">the prototype's bone paths - the CLIP question, asked apart.</param>
        /// <exception cref="OperationCanceledException">Cancelled before the swap; nothing was written.</exception>
        /// <exception cref="InvalidOperationException">The plan would not parse, or Validate refused;
        /// its refusals are the message, one per line.</exception>
        internal static string Skel(string src, string dst, string planPath,
                                    IList<string> targetNames, IList<string> targetPaths,
                                    CancellationToken cancel, Action<SlimProgress> publish)
        {
            string tmp = dst + "." + Guid.NewGuid().ToString("N") + ".ct_tmp";
            bool swapped = false;
            string done = null;
            try
            {
                At(cancel, publish, SkelStages, 0, "Reading " + Path.GetFileName(src));
                GlbDocument doc = GlbDocument.Load(src);

                At(cancel, publish, SkelStages, 1, "Reading " + Path.GetFileName(planPath));
                string why;
                SkelPlan plan = SkelPlan.Parse(File.ReadAllText(planPath), out why);
                if (plan == null) throw new InvalidOperationException(why);

                At(cancel, publish, SkelStages, 2, "Checking the plan against the file");
                IList<string> refusals = GlbSkel.Validate(doc, plan, targetNames);
                if (refusals.Count > 0)
                    throw new InvalidOperationException(string.Join("\n", new List<string>(refusals).ToArray()));

                At(cancel, publish, SkelStages, 3, "Rewriting the skeleton");
                GlbSkel.Stats stats = GlbSkel.Apply(doc, plan);
                // A REWRITE THAT REWRITES NOTHING IS NOT A SAVE. GlbDocument would write the source's
                // own JSON bytes back verbatim (GlbDocument.cs:91-92), so the honest thing to do with
                // the destination is leave it alone - the same rule Zip keeps for a file that would grow.
                if (!doc.Dirty)
                {
                    done = "the plan changed nothing, so nothing was written";
                    Publish(publish, new SlimProgress("Done", SkelStages.Length, SkelStages.Length, done));
                    return done;
                }
                doc.Write(tmp);

                At(cancel, publish, SkelStages, 4, "Reading " + Path.GetFileName(dst) + " back");
                SkelVerdict verdict = GlbSkel.Verify(GlbDocument.Load(tmp), plan.Root, targetNames, targetPaths);
                string unread = null;
                try { ReadBack(tmp); }
                catch (Exception ex)
                {
                    // The importer's refusal is only EVIDENCE when the source itself imported. GlbSkel
                    // exists to rewrite the files GlbReader refuses, so failing on those would deny the
                    // port the very files it was written for; what is refused here is a rewrite that
                    // BROKE a file the game could read.
                    if (Imports(src))
                    {
                        done = "the rewritten file does not import: " + ex.Message + " - destination left alone";
                        Publish(publish, new SlimProgress("Done", SkelStages.Length, SkelStages.Length, done));
                        return done;
                    }
                    unread = ex.Message;
                }

                At(cancel, publish, SkelStages, 5, "Writing " + Path.GetFileName(dst));
                if (File.Exists(dst)) File.Replace(tmp, dst, null);
                else File.Move(tmp, dst);
                swapped = true;

                done = "renamed " + stats.Renamed + ", collapsed " + stats.Collapsed + ", inserted " +
                       stats.Inserted + ", created " + stats.Created + "; " +
                       (targetNames == null && targetPaths == null
                        ? "no prototype was selected, so nothing is claimed about binding"
                        : verdict.Sentence());
                if (unread != null)
                    done += "; the game's own reader still refuses this file (" + unread +
                            "), exactly as it refused the source";
                done += Unsidecar(src, dst);
                Publish(publish, new SlimProgress("Done", SkelStages.Length, SkelStages.Length, done));
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

        /// <summary>Skel on the pool, exactly as <see cref="Start"/> runs Execute. Both callbacks land
        /// on the WORKER thread.</summary>
        internal static void StartSkel(string src, string dst, string planPath,
                                       IList<string> targetNames, IList<string> targetPaths,
                                       CancellationTokenSource cts, Action<SlimProgress> onProgress,
                                       Action<string> onComplete)
        {
            // The caller's lists belong to the caller and the prototype it picked keeps moving.
            List<string> names = targetNames == null ? null : new List<string>(targetNames);
            List<string> paths = targetPaths == null ? null : new List<string>(targetPaths);
            CancellationToken cancel = cts == null ? CancellationToken.None : cts.Token;
            ThreadPool.QueueUserWorkItem(delegate
            {
                string result;
                try
                {
                    result = Skel(src, dst, planPath, names, paths, cancel, onProgress);
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

        /// <summary>Does the GAME'S reader take this file at all? Asked only when the rewritten one was
        /// refused, so a file that never imported is not blamed on the rewrite.</summary>
        private static bool Imports(string path)
        {
            try { ReadBack(path); return true; }
            catch (Exception) { return false; }
        }

        /// <summary>An IN-PLACE run takes the alias sidecar with it and says so. The sidecar is
        /// sha256-guarded (AliasMap.cs:189-195), so after this rewrite it can never apply again - and
        /// every mapping it carried is now baked into the node names, which is the whole point of a
        /// skel run. A run that wrote a SIBLING touches nothing: the source and its sidecar are both
        /// still exactly as valid as they were.</summary>
        private static string Unsidecar(string src, string dst)
        {
            if (!string.Equals(Path.GetFullPath(src), Path.GetFullPath(dst),
                               StringComparison.OrdinalIgnoreCase)) return "";
            string sidecar = AliasMap.SidecarPathOf(dst);
            if (!File.Exists(sidecar)) return "";
            try { File.Delete(sidecar); }
            catch (IOException) { return "; " + Path.GetFileName(sidecar) + " is now stale and could not be removed"; }
            catch (UnauthorizedAccessException) { return "; " + Path.GetFileName(sidecar) + " is now stale and could not be removed"; }
            return "; removed the now-stale " + Path.GetFileName(sidecar);
        }

        /// <summary>The temp back through the GAME'S OWN importer, which is the only reader whose
        /// opinion matters: a rewrite that produces a file the game cannot animate has failed, however
        /// small it is. Throws the importer's refusal; Zip turns it into the sentence and keeps the
        /// destination. Returns the clip count.</summary>
        // ponytail: a static seam, because the gate cannot name the tmp to corrupt it - the test
        // swaps in a refusing reader and restores this one.
        internal static Func<string, int> ReadBack = path =>
        {
            var clips = new List<SampledClip>();
            GlbReader.Read(File.ReadAllBytes(path), clips);
            return clips.Count;
        };

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
