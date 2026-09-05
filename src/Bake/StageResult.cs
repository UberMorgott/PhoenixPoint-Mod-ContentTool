using System;
using System.Collections.Generic;
using System.Text;

namespace Morgott.ContentTool.Bake
{
    /// <summary>PASS, FAIL, or NOTHING WAS MEASURED. The third one is why this enum exists: every VOID arm of
    /// the read-back returns 0 exactly like a pass (ProjectBake.cs:1835, :1852, :1866, :1873, :1894, :1923,
    /// :1942), so "no failures" and "no proof" are the same number and a panel that counts cannot tell them
    /// apart. <c>None</c> is the idle row - a stage nobody has run yet - so a default-constructed
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
    }

    /// <summary>What a read-back measured, structured. <c>Failed &gt; 0</c> is a FAIL row; <c>Failed == 0</c>
    /// is NOT automatically a PASS - <see cref="MandatoryVoid"/> has to say no first.</summary>
    internal sealed class ReadBackResult
    {
        internal readonly int Failed, Passed, Void;
        internal readonly IList<GateEntry> Entries;
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

        internal static ReadBackResult Of(params GateEntry[] entries)
        {
            return new ReadBackResult(null, entries ?? new GateEntry[0]);
        }

        internal static ReadBackResult Of(string terminal, params GateEntry[] entries)
        {
            return new ReadBackResult(terminal, entries ?? new GateEntry[0]);
        }

        /// <summary>True when this target has no non-VOID measurement for one of the gates its row kind
        /// REQUIRES. A gate that never ran at all answers the same as a gate that ran and measured nothing:
        /// neither is proof, and treating absence as an implied pass is exactly the bug this carrier exists
        /// to stop.</summary>
        internal bool MandatoryVoid(string target, RowKind kind)
        {
            string[] gates = kind == RowKind.Mesh ? MeshGates
                           : kind == RowKind.Texture ? TextureGates
                           : MaterialGates;
            foreach (string gate in gates)
            {
                bool proven = false;
                foreach (GateEntry e in Entries)
                    if (e.Outcome != GateOutcome.Void &&
                        string.Equals(e.Gate, gate, StringComparison.Ordinal) &&
                        string.Equals(e.Target, target, StringComparison.Ordinal))
                    { proven = true; break; }
                if (!proven) return true;
            }
            return false;
        }
    }

    /// <summary>A bake's two counts kept APART, plus its terminal line. <c>PatchFailed</c> alone authorises
    /// patch-cache publication; an unrelated import failure still shows in <c>Failed</c> and still fails the
    /// row (ProjectBake.cs:403-406, Route7.cs:342).</summary>
    internal sealed class BakeResult
    {
        internal readonly int Failed, PatchFailed;
        internal readonly string Terminal;

        internal BakeResult(int failed, int patchFailed, string terminal)
        {
            Failed = failed; PatchFailed = patchFailed; Terminal = terminal;
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
        /// ModelDoctor.cs:745, which was private static in a file no offline gate can link (JsonUtility and
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
    }
}
