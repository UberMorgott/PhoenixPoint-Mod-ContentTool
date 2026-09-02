using System;
using System.Collections.Generic;
using Morgott.ContentTool.Import;

namespace Morgott.ContentTool.Doctor
{
    /// <summary>
    /// THE BONE MAP AS A SKELETON PLAN. The alias sidecar and a rename plan describe the same fact -
    /// file bone -> game bone - from two sides, and the flow between them is ONE-DIRECTIONAL: the
    /// Doctor's aliases become a plan, the plan bakes the name into the .glb, and after that the
    /// sidecar is unnecessary for every bone it renamed (it is sha256-guarded anyway,
    /// AliasMap.cs:189-195, so a rewrite makes it Stale). Never the reverse.
    ///
    /// RENAMES AND NOTHING ELSE. The Doctor knows which bones are MISNAMED - that is what its report
    /// and its aliases are - and knows nothing whatsoever about hierarchy, so it writes no collapse,
    /// no insert and no create. Design §9: "do NOT generalise them into automatic guesses".
    ///
    /// Unity-free on purpose: this is string work over rows that already exist, so it is proven in a
    /// gate rather than by pressing a button and squinting at a file.
    /// </summary>
    internal static class SkelPlanFromMap
    {
        /// <param name="rows">the preflight report's own rows. Only the two the report writes ABOUT AN
        /// ALIAS are read: AliasUnused (this file has no bone of that name) and AliasNotATargetBone
        /// (the rig has no such bone), both carrying the FILE bone as their Subject
        /// (ReplacementPreflight.cs:143,148). Either one is a rename GlbSkel.Validate would refuse, and
        /// a plan that arrives pre-refused is worse than a short one.</param>
        /// <param name="aliases">file bone -&gt; target bone, the map the Doctor is editing. It is
        /// bijective by construction (ModelDoctor.Claimed), which is exactly Validate's
        /// duplicate-target rule, so a plan written from it validates by construction.</param>
        /// <param name="root">the file's single scene root, or null when it has none or several -
        /// Validate refuses a Root that names no node, and a plan with none is still a legal rewrite.</param>
        internal static SkelPlan Of(IEnumerable<Diagnostic> rows, IDictionary<string, string> aliases,
                                    string root)
        {
            var flagged = new HashSet<string>(StringComparer.Ordinal);
            if (rows != null)
                foreach (Diagnostic row in rows)
                    if (row != null && row.Subject != null &&
                        (row.Code == "AliasUnused" || row.Code == "AliasNotATargetBone"))
                        flagged.Add(row.Subject);

            var plan = new SkelPlan { Root = root };
            if (aliases == null) return plan;
            foreach (KeyValuePair<string, string> alias in aliases)
            {
                if (string.IsNullOrEmpty(alias.Key) || string.IsNullOrEmpty(alias.Value)) continue;
                if (flagged.Contains(alias.Key)) continue;
                plan.Renames.Add(new SkelRename { From = alias.Key, To = alias.Value });
            }
            return plan;
        }
    }
}
