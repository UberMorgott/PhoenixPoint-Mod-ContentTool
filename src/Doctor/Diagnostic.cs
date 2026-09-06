using System.Collections.Generic;
using Morgott.ContentTool.Import;

namespace Morgott.ContentTool.Doctor
{
    /// <summary>How much a row costs the author. Assigned by the DOCTOR - SkinCompatibility keeps its
    /// issues severity-free, because the same fact is fatal to SkinBinder.Bind and merely expensive
    /// to a bake that falls back.</summary>
    internal enum Severity
    {
        /// <summary>Nothing will be written, or nothing can be previewed.</summary>
        Blocking,
        /// <summary>It imports, and it loses the author's weights.</summary>
        Downgrade,
        /// <summary>Something was ignored. The model is unaffected.</summary>
        Warning,
        /// <summary>Said out loud so it is not a surprise later.</summary>
        Info
    }

    /// <summary>Whose asset a row is about. Target rows are drawn apart: "this is the game's model,
    /// not your file" is the difference between a fix and a dead end.</summary>
    internal enum DiagnosticSide { File, Target, Sidecar }

    /// <summary>One row of the report. Code is a stable string (spec v3 §7) so the UI, the log and a
    /// future manifest all name the same thing.</summary>
    internal sealed class Diagnostic
    {
        internal string Code;
        internal Severity Severity;
        internal DiagnosticSide Side;
        /// <summary>The engine's own sentence, verbatim.</summary>
        internal string Message;
        /// <summary>What to do in Blender. Empty when there is nothing the author can do.</summary>
        internal string Remedy = "";
        /// <summary>The bone the row is about, or null.</summary>
        internal string Subject;
    }

    /// <summary>The rows plus the verdict they add up to.</summary>
    internal sealed class DiagnosticReport
    {
        internal readonly List<Diagnostic> Rows = new List<Diagnostic>();
        internal Outcome Outcome;

        internal void Add(string code, Severity severity, DiagnosticSide side, string message,
                          string remedy = "", string subject = null)
        {
            Rows.Add(new Diagnostic
            {
                Code = code,
                Severity = severity,
                Side = side,
                Message = message,
                Remedy = remedy,
                Subject = subject
            });
        }

        internal int Count(Severity severity)
        {
            int n = 0;
            foreach (Diagnostic d in Rows) if (d.Severity == severity) n++;
            return n;
        }

        /// <summary>The one line at the top of the panel, and the one line worth pasting when asking
        /// for help.</summary>
        internal string Header()
        {
            // THE WARNING COUNT RIDES ON EVERY VERDICT, not just the clean one. A "BY NAME" that hid a
            // SubmeshMaterials warning told the author to stop reading (the 1-triangle torso of
            // 2026-09-06) - and a NEAREST-BONE or REFUSED header counting its OWN reasons only hid the
            // same row just as well, because the count LOOKS like the number of rows below it.
            int warned = Count(Severity.Warning);
            string also = warned > 0 ? ", " + warned + " warning(s)" : "";
            switch (Outcome)
            {
                case Outcome.ByName:
                    return "BY NAME - your weights will be used" +
                           (warned > 0 ? " (" + warned + " warning(s) below)" : "");
                case Outcome.NearestBone:
                    return "NEAREST-BONE - the bake would import this but NOT use your weights (" +
                           Count(Severity.Downgrade) + " reason(s)" + also + ")";
                // A rigged file onto a static target is still NOT RIGGED - the mesh is written - but the
                // author's weights are gone, and a header that said only "the target carries no bind
                // poses" read as "nothing of yours is affected".
                case Outcome.NotRigged:
                    return "NOT RIGGED - the target carries no bind poses" +
                           (Count(Severity.Downgrade) > 0 ? " and your file's weights are dropped" : "");
                default: return "IMPORT REFUSED (" + Count(Severity.Blocking) + " reason(s)" + also + ")";
            }
        }
    }
}
