using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Morgott.ContentTool.IO;

namespace Morgott.ContentTool.Bake
{
    /// <summary>How a publication ENDED. <c>Cancelled</c> and <c>Refused</c> published nothing at all;
    /// <c>Failed</c> stopped part-way and the receipt is absent, which is what forbids an install until a
    /// repair bake; <c>Published</c> means every copy landed AND the receipt was written after them.</summary>
    internal enum PublishOutcome { Published, Cancelled, Refused, Failed }

    /// <summary>
    /// B5: THE PUBLICATION ORDERING, and nothing else.
    ///
    /// invalidate the old receipt -> publish every complete copy -> write the new receipt LAST. Get that
    /// order wrong and a bake that dies half-way leaves a receipt saying the copies are current over copies
    /// that are not - the exact failure this project keeps being bitten by, silent and permanent.
    ///
    /// WHY IT IS ITS OWN FILE (plan review, blocker 2). The two callers - <c>ProjectBake.Patch</c> and
    /// <c>LifecycleJob</c> - both carry UnityEngine and AssetsTools and can never be linked into
    /// ObjCodecTests, so an offline gate over an inline B5 would have to re-implement this sequence and
    /// would then stay green while the real bake stamped the receipt first. <c>AtomicFile.Publish</c> swaps
    /// ONE file and says nothing about order. So the order lives here, UnityEngine-free and test-linked, and
    /// G7's fault arms drive the production code.
    ///
    /// NOT A TRANSACTION AND NOT A CRASH ROLLBACK. Files are individually complete or individually
    /// untouched; there is no multi-file atomicity to be had on Windows and pretending otherwise would be a
    /// bigger lie than the honest "the receipt is absent, bake again".
    ///
    /// ONE CANCEL CHECK, AT THE TOP. That instant IS B4 - the last cancellable one. Past it the run
    /// finishes and reports completion: a publication interrupted half-way is exactly the unclassifiable
    /// output the whole boundary exists to prevent.
    /// </summary>
    internal static class Publication
    {
        /// <param name="tempToDest">B2's work: temp the caller streamed -> the final path it replaces, in
        /// publication order. Each temp MUST be a sibling of its destination (AtomicFile.Publish:56).</param>
        /// <param name="keyPath">the freshness receipt beside the copies, or null when this output has none
        /// (the project's own Dist). It is INVALIDATED even when <paramref name="keyText"/> is null.</param>
        /// <param name="keyText">the receipt to write once every copy has landed, or null for a bake nobody
        /// vouched for - its copies are published, but nothing may read them as current.</param>
        /// <param name="live">R38, asked per DESTINATION immediately before the first swap: a copy the game
        /// is serving right now is not rewritten under its reader. Null when the caller already asked (a
        /// worker cannot ask - BundleClaims walks a list main mutates - so a job hands in a captured
        /// verdict as a closure over values it read on main).</param>
        /// <param name="cancelled">B4's check, asked ONCE. Null is "not cancellable".</param>
        internal static PublishOutcome Run(IList<KeyValuePair<string, string>> tempToDest,
                                           string keyPath, string keyText,
                                           Func<string, string> live, Func<bool> cancelled,
                                           out string message)
        {
            message = null;
            IList<KeyValuePair<string, string>> work =
                tempToDest ?? (IList<KeyValuePair<string, string>>)new List<KeyValuePair<string, string>>();

            // ---- B4. The last cancellable instant, and the only one.
            if (cancelled != null && cancelled()) { Discard(work, 0); return PublishOutcome.Cancelled; }

            // ---- R38, per destination, before the first swap. All or nothing: a run that refused its
            // second target must not have replaced its first, or the "no partial bake to explain" promise
            // is void for exactly the case it was written for. Today no caller can see a verdict change
            // between its own probe and this one - claims are taken and released on the main thread, and
            // this runs on it too - so the re-probe is cheap defence against a future off-main publisher,
            // never a race it currently closes.
            if (live != null)
                foreach (KeyValuePair<string, string> c in work)
                {
                    string refusal = live(c.Value);
                    if (refusal == null) continue;
                    message = refusal;
                    Discard(work, 0);
                    return PublishOutcome.Refused;
                }

            // ---- INVALIDATE FIRST. Not "skip when there is no new receipt to write": a failed or
            // unvouched bake still replaces the copies, and leaving the previous receipt standing over
            // them is the silent-stale-copy failure with extra steps.
            if (!string.IsNullOrEmpty(keyPath))
                try { if (File.Exists(keyPath)) File.Delete(keyPath); }
                catch (Exception ex)
                {
                    message = "PUBLICATION REFUSED: the freshness receipt " + keyPath + " could not be " +
                              "invalidated (" + ex.Message + "), so NOTHING was published - the previous " +
                              "copies are exactly as they were. Close whatever holds that file and bake again.";
                    Discard(work, 0);
                    return PublishOutcome.Failed;
                }

            // ---- PUBLISH. Each swap is atomic on its own; the set is not, and the receipt below is what
            // says whether the set as a whole is trustworthy.
            for (int i = 0; i < work.Count; i++)
                try { AtomicFile.Publish(work[i].Key, work[i].Value); }
                catch (Exception ex)
                {
                    message = "PUBLICATION FAILED at " + work[i].Value + " (" + ex.Message + ") after " + i +
                              " of " + work.Count + " copy(ies). Those are complete; the rest are the " +
                              "previous run's, and no freshness receipt was written - so nothing will read " +
                              "this output as current. Bake again to repair it.";
                    // FROM `i`, NOT `i+1`. AtomicFile.Publish deliberately keeps a temp whose swap threw -
                    // it is not the swap's to throw away - but this run is over and that temp is ours, so
                    // the one that just failed is discarded with the ones that never started.
                    Discard(work, i);
                    return PublishOutcome.Failed;
                }

            // ---- THE RECEIPT, LAST. WriteText is AtomicFile's own two-step, so a receipt is never half a
            // key. Its encoding is the one PatchCache.Fresh reads back - plain UTF-8, no BOM.
            if (!string.IsNullOrEmpty(keyPath) && !string.IsNullOrEmpty(keyText))
                try { AtomicFile.WriteText(keyPath, keyText, new UTF8Encoding(false)); }
                catch (Exception ex)
                {
                    message = "PUBLICATION INCOMPLETE: every copy landed but the freshness receipt " +
                              keyPath + " could not be written (" + ex.Message + "). The copies are the " +
                              "ones this run produced; they will simply be baked again next time.";
                    return PublishOutcome.Failed;
                }

            return PublishOutcome.Published;
        }

        /// <summary>The temps this run owns and nobody else knows the name of. Best effort: a temp that
        /// survives is a file, not a corruption, and the exception the caller needs is never this one.
        ///
        /// INTERNAL because B2's own producer needs it for the exit B5 never sees: a throw out of
        /// <c>BundleBaker.Write</c> or a read-back gate leaves every streamed temp behind (ProjectBake.cs,
        /// Patch's catch), and a second spelling of "delete what this run streamed" would drift from the
        /// one the refusals above use.</summary>
        internal static void Discard(IList<KeyValuePair<string, string>> work, int from)
        {
            for (int i = from; i < work.Count; i++)
                try { File.Delete(work[i].Key); } catch (Exception) { }
        }
    }
}
