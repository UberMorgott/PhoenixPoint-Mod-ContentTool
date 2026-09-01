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
                Code = code, Severity = severity, Side = side,
                Message = message, Remedy = remedy, Subject = subject
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
            switch (Outcome)
            {
                case Outcome.ByName: return "BY NAME - your weights will be used";
                case Outcome.NearestBone:
                    return "NEAREST-BONE - the bake would import this but NOT use your weights (" +
                           Count(Severity.Downgrade) + " reason(s))";
                case Outcome.NotRigged: return "NOT RIGGED - the target carries no bind poses";
                default: return "IMPORT REFUSED (" + Count(Severity.Blocking) + " reason(s))";
            }
        }
    }
}
