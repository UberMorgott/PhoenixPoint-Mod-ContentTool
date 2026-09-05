using System;
using System.Collections.Generic;
using System.Text;

namespace Morgott.ContentTool.Bake
{
    /// <summary>PASS, FAIL, or NOTHING WAS MEASURED. The third one is why this enum exists: every VOID arm of
    /// the read-back returns 0 exactly like a pass (ReadBack.cs:51, :105, :118, :141, :171 and the seven P6
    /// arms at :241, :262, :276, :287, :308, :337, :357), so "no failures" and "no proof" are the same number
    /// and a panel that counts cannot tell them apart. <c>None</c> is the idle row - a stage nobody has run yet - so a default-constructed
    /// <see cref="StageResult"/> is idle without anyone setting a field.
    ///
    /// NOT <c>Morgott.ContentTool.Import.Outcome</c> and not named <c>Outcome</c> either: that name is already
    /// taken in a namespace half of Bake\ imports (BundleBaker.cs:203 uses it unqualified), and a second
    /// <c>Outcome</c> here would make every one of those lines ambiguous.</summary>
    internal enum GateOutcome { None, Pass, Fail, Void }

    /// <summary>Which gates a declared replacement row must have measured before its Verify may say PASS.
    /// Video rows are not here - they are served live and this route never patches them.</summary>
    internal enum RowKind { Mesh, Texture, Material }

    /// <summary>Evidence age, the axis that is never collapsed into the outcome: <c>Never</c> = no receipt,
    /// <c>Stale</c> = a receipt whose inputs moved (an old cache directory with no key is stale, not never -
    /// PatchCache.cs:84), <c>Fresh</c> = receipt matches and the outputs are still there.</summary>
    internal enum Freshness { Never, Stale, Fresh }

    /// <summary>One gate instance: which gate, on which target, how it came out, and the producer's EXACT
    /// line as it already went to the log. The line is carried, never re-composed - the panel prints what the
    /// producer said, so a P6 VOID can never be relabelled PASS on the way to the screen.</summary>
    internal sealed class GateEntry
    {
        internal readonly string Gate, Target, Line;
        internal readonly GateOutcome Outcome;

        private GateEntry(string gate, string target, string line, GateOutcome outcome)
        {
            Gate = gate; Target = target; Line = line; Outcome = outcome;
        }

        internal static GateEntry Pass(string gate, string target, string line)
        {
            return new GateEntry(gate, target, line, GateOutcome.Pass);
        }

        internal static GateEntry Fail(string gate, string target, string line)
        {
            return new GateEntry(gate, target, line, GateOutcome.Fail);
        }

        internal static GateEntry Void(string gate, string target, string line)
        {
            return new GateEntry(gate, target, line, GateOutcome.Void);
        }

        /// <summary>P7's self-check: the failures the clip arms RETURNED and the failures their own lines
        /// classify as are the same fact, and a mismatch means the slicing has stopped matching the producer
        /// (ReadBack.cs:181).
        ///
        /// NEVER A THROW. That read-back runs inside ProjectBake.Patch on the player's checkbox path
        /// (ProjectBake.cs:1657 &lt;- :112 &lt;- Route7.cs:340 &lt;- ModRoster.cs:283) and nothing catches an
        /// exception before ModRoster: the whole bake log was lost, no PatchCache.Write, no disposition,
        /// `Failed` never armed, and the same throw came back on every toggle. A disagreement is a counted
        /// FAIL like any other gate and the run keeps going.
        ///
        /// It lives HERE, not beside its caller, because ReadBack carries UnityEngine types and cannot be
        /// linked offline - this file can, so the arm is proven without a game session. The " FAIL " wording
        /// is ProjectBake.Check's and is spelled out once more for that reason.
        ///
        /// ONE DEFECT IS ONE FAILURE, so the direction decides the outcome. When the SLICER saw more
        /// (sliced &gt; counted) those FAIL lines are already entries of their own (ReadBack.cs:208) and a
        /// counted P7 on top of them reported `2 FAILURE(S)` for a single defect - so that direction is a
        /// VOID note, uncounted, and the total stays the larger of the two. Only counted &gt; sliced adds a
        /// failure, because there the arms' own count reaches nobody else.</summary>
        internal static void SelfCheck(StringBuilder log, List<GateEntry> entries, string clip,
                                       int counted, int sliced)
        {
            if (sliced == counted) return;
            bool counts = counted > sliced;
            string line = "P7 " + (counts ? "FAIL" : "VOID") +
                          " the clip read-back disagrees with itself: clip '" + clip +
                          "' - its arms returned " + counted + " failure(s) and the lines they wrote " +
                          "classify as " + sliced;
            log.AppendLine(line);
            entries.Add(counts ? Fail("P7", clip, line) : Void("P7", clip, line));
        }
    }

    /// <summary>What a read-back measured, structured. <c>Failed &gt; 0</c> is a FAIL row; <c>Failed == 0</c>
    /// is NOT automatically a PASS - <see cref="MandatoryVoid"/> has to say no first.</summary>
    internal sealed class ReadBackResult
    {
        internal readonly int Failed, Passed, Void;
        /// <summary>The measurements themselves. Handed out as the ARRAY it is: an <c>IList</c> over an array
        /// has a settable indexer, so a caller could overwrite a FAIL entry through a field the class calls
        /// readonly.</summary>
        internal readonly GateEntry[] Entries;
        /// <summary>The terminal console line the producer would print, VERBATIM - the panel shows this, it
        /// never composes its own sentence out of the counts.</summary>
        internal readonly string Terminal;

        // THE MANDATORY PROOFS (design 4.4). Everything else - a clip gate on a project with no clips, P6 on
        // a skinless mesh - may be VOID without sinking the row.
        private static readonly string[] MeshGates = { "P4", "P4-bytes" };
        private static readonly string[] TextureGates = { "P1", "P1-ctl-shipped" };
        private static readonly string[] MaterialGates = { "P3" };

        private ReadBackResult(string terminal, GateEntry[] entries)
        {
            Terminal = terminal;
            Entries = entries;
            foreach (GateEntry e in entries)
                if (e.Outcome == GateOutcome.Fail) Failed++;
                else if (e.Outcome == GateOutcome.Pass) Passed++;
                else Void++;
        }

        /// <summary>ONE factory, and the terminal line is always passed - explicitly <c>null</c> where there
        /// is none. A second <c>Of(params GateEntry[])</c> overload made <c>Of(null)</c> ambiguous (CS0121),
        /// and every <c>Of(entries)</c> call silently produced a result whose <c>Terminal</c> was null.</summary>
        internal static ReadBackResult Of(string terminal, params GateEntry[] entries)
        {
            return new ReadBackResult(terminal, entries ?? new GateEntry[0]);
        }

        /// <summary>True when this target has no non-VOID measurement for one of the gates its row kind
        /// REQUIRES. A gate that never ran at all answers the same as a gate that ran and measured nothing:
        /// neither is proof, and treating absence as an implied pass is exactly the bug this carrier exists
        /// to stop.
        ///
        /// A TEXTURE ROW IS LOOKED UP BY ITS BUNDLE, not by its asset name: P1 and P1-ctl-shipped ask their
        /// question about every declared texture of a bundle AT ONCE and are recorded under the bundle file
        /// (ReadBack.cs:51, :55, :57), so no P1 entry has ever carried a texture's own name and matching on
        /// one made this predicate answer "unproven" for every texture row ever measured. Mesh and material
        /// rows keep their own key - P3/P4 are recorded per asset. <paramref name="bundle"/> has NO DEFAULT: a
        /// texture caller that forgot it answered "unproven" silently, so every caller names it and a mesh or
        /// material row says so by passing null.</summary>
        internal bool MandatoryVoid(string target, RowKind kind, string bundle)
        {
            string[] gates = kind == RowKind.Mesh ? MeshGates
                           : kind == RowKind.Texture ? TextureGates
                           : MaterialGates;
            string key = kind == RowKind.Texture ? bundle : target;
            foreach (string gate in gates)
            {
                bool proven = false;
                foreach (GateEntry e in Entries)
                    if (e.Outcome != GateOutcome.Void &&
                        string.Equals(e.Gate, gate, StringComparison.Ordinal) &&
                        string.Equals(e.Target, key, StringComparison.Ordinal))
                    { proven = true; break; }
                if (!proven) return true;
            }
            return false;
        }
    }

    /// <summary>How a bake ENDED, which its two counts cannot say. R37 and R38 return with ZERO failures -
    /// nothing was baked and nothing was wrong - and <c>Route7.ApplyProject</c> reads only
    /// <c>patchFailed != 0</c> (Route7.cs:341) before it enumerates and installs, so a zero-count refusal
    /// read as <c>Success</c> installs the STALE copies as if the bake had produced them. Encoding
    /// contention as a patch failure instead is equally wrong: that reaches <c>Failed.Add(modId)</c>
    /// (Route7.cs:344) and poisons the session's checkbox over a race nobody caused.
    ///
    /// So: <c>Refused</c> and <c>Cancelled</c> stop Apply WITHOUT touching that set; only <c>Failed</c>
    /// reaches it. <c>Cancelled</c> has no producer until the segmented job lands (Task 4) - it is here
    /// because the caller's `switch` has to be written once, not widened later.</summary>
    internal enum BakeDisposition { Success, Refused, Cancelled, Failed }

    /// <summary>A bake's two counts kept APART, plus its terminal line and how it ended. <c>PatchFailed</c>
    /// alone authorises patch-cache publication; an unrelated import failure still shows in <c>Failed</c>
    /// and still fails the row (ProjectBake.cs:401-402, Route7.cs:342).</summary>
    internal sealed class BakeResult
    {
        internal readonly int Failed, PatchFailed;
        internal readonly string Terminal;
        internal readonly BakeDisposition How;

        internal BakeResult(int failed, int patchFailed, string terminal, BakeDisposition how)
        {
            Failed = failed; PatchFailed = patchFailed; Terminal = terminal; How = how;
        }
    }

    /// <summary>One dashboard row - the four independent axes (stage, outcome, freshness, the producer's own
    /// verdict string, the generation that produced it) land here with the receipts that fill them.
    ///
    /// Today it is only the HOME of Tail: the row fields have no producer until receipts exist, and five
    /// fields nobody assigns are five CS0649s and a shape guessed a task early.</summary>
    internal static class StageResult
    {
        /// <summary>The last few lines of a bake log, for the panel. Lifted VERBATIM out of
        /// src\Dev\ModelDoctor.cs, where it was `:745` before this commit moved it (the note left behind at
        /// `:735` marks the spot); it was private static in a file no offline gate can link (JsonUtility and
        /// friends). Its semantics are FROZEN here, not fixed.
        ///
        /// THE TRAILING EMPTY ELEMENT IS DISCARDED BEFORE THE COUNT. ApplyProject ends in AppendLine, so
        /// Split('\n') always produces one empty element at the end; taking "the last 1" then selected that
        /// empty string and Tail(log, 1) answered "", which is exactly the R11 path - the panel would report
        /// a failed bake with a BLANK result line. Trim the tail first, then take N.</summary>
        internal static string Tail(string log, int lines)
        {
            if (string.IsNullOrEmpty(log)) return "";
            string[] all = log.Replace("\r\n", "\n").Split('\n');
            int end = all.Length;
            while (end > 0 && all[end - 1].Length == 0) end--;      // the AppendLine's own empty tail
            var kept = new StringBuilder();
            for (int i = Math.Max(0, end - lines); i < end; i++)
                if (all[i].Length != 0) kept.AppendLine(all[i]);
            return kept.ToString().TrimEnd();
        }

        /// <summary>A bake passed only by its TERMINAL line, and this is the ONE place that decides it.
        ///
        /// <c>report.Contains("ALL PASS")</c> scanned the whole log, and one of the lines in that log is
        /// ClipFields.cs:505's skipped-clip refusal - "...rather than reporting ALL PASS over an animation..."
        /// (printed by ProjectBake.cs:935) - so a bake ending `ct_project: 1 FAILURE(S)` classified as a pass
        /// and BundleResidency's B1/B1-rebake proceeded over it. The terminal line is the producer's verdict
        /// (ProjectBake.cs:225, :515 both Append it last); nothing above it is a verdict about the run.</summary>
        internal static bool BakePassed(string report)
        {
            return Tail(report, 1).StartsWith("ct_project: ALL PASS", StringComparison.Ordinal);
        }
    }
}
