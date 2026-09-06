using System.Collections.Generic;

namespace Morgott.ContentTool.Bake
{
    /// <summary>
    /// THE APPLY'S OUTCOME VOCABULARY, split out of <c>Route7.cs</c> and carrying NO UnityEngine type, so
    /// the rules every consumer reads - the aggregate verdict and the restart derivation - are proven
    /// offline against the production types instead of by pressing a panel. Nothing here touches the disk,
    /// the catalog or a claim; the install path that produces these values stays in Route7.cs.
    /// </summary>
    internal static partial class Route7
    {
        /// <summary>What became of ONE bundle in an apply. A wizard cannot read this out of the log: zero
        /// claims taken is not the same fact as residency - a catalog Locate failure (BundleLive.cs:215-218)
        /// and an ownership conflict (BundleClaims.Claim:250) also take no claim, and reporting either of
        /// those as "restart and enable" is the tool telling the author something untrue with a straight
        /// face.</summary>
        internal enum ApplyDisposition { Redirected, Resident, Refused, BakeFailed }

        /// <summary>What the whole PROJECT's apply came to, from the per-target list - CONSERVATIVELY: any
        /// refusal survives, then any restart-required target (that is the one the author has to act on),
        /// and a blanket "redirected LIVE" only when every target was. An empty list is a refusal, because
        /// nothing was installed.
        ///
        /// EXTRACTED because a second consumer arrived. `Applied` computes it for the console-shaped call
        /// that names no bundle, and the dashboard needs the same answer for a SHIP that DID name one:
        /// `how` then speaks for that ONE slot (<c>Route7.cs:594</c>-<c>:601</c>), and a five-row panel
        /// showing it as the project's Apply would publish PASS over a sibling target that was refused, or
        /// miss a restart another target needs.
        ///
        /// IT IS A VERDICT, NOT THE RESTART FACT: the loop stops at the first refusal, so a Resident
        /// SIBLING behind it is not visible here at all. Ask <see cref="RestartNeeded"/> for that, never
        /// <c>== Resident</c> on this answer.</summary>
        internal static ApplyDisposition Aggregate(IList<TargetInstall> targets)
        {
            ApplyDisposition how = ApplyDisposition.Refused;
            if (targets == null) return how;
            foreach (TargetInstall t in targets)
                if (t.Outcome == ApplyDisposition.Refused) { how = ApplyDisposition.Refused; break; }
                else if (t.Outcome == ApplyDisposition.Resident) how = ApplyDisposition.Resident;
                else if (how != ApplyDisposition.Resident) how = ApplyDisposition.Redirected;
            return how;
        }

        /// <summary>
        /// S1, DERIVED INDEPENDENTLY OF THE VERDICT: does ANY target of this apply need a restart before
        /// what is on disk is what the game serves?
        ///
        /// It cannot be read off <see cref="Aggregate"/>. That fold is conservative and stops at the first
        /// refusal, so a project whose first target was Refused and whose second was Resident aggregates to
        /// Refused - and every consumer that asked `how == Resident` then left the barrier DOWN over a
        /// declared target the game is still serving an older revision of. `Admit`'s R30 (LifecycleState.cs)
        /// then admitted a Verify that `ct_route7 verify` refuses for the very same state.
        /// </summary>
        internal static bool RestartNeeded(IList<TargetInstall> targets)
        {
            if (targets == null) return false;
            foreach (TargetInstall t in targets)
                if (t.Outcome == ApplyDisposition.Resident) return true;
            return false;
        }

        /// <summary>What an apply's disposition means to the CARRIER, and the only copy of the mapping.
        /// Both consumers ask here - the lifecycle Apply producer and the Doctor's SHIP handoff - because
        /// two copies of "Resident is a Success" is exactly how one path publishes PASS for a state the
        /// other calls a refusal.</summary>
        internal static BakeDisposition Disposition(ApplyDisposition how)
        {
            return how == ApplyDisposition.BakeFailed ? BakeDisposition.Failed
                 : how == ApplyDisposition.Refused ? BakeDisposition.Refused
                 : BakeDisposition.Success;
        }

        /// <summary>What became of ONE target, kept instead of thrown away. <c>BundleLive.Install</c> builds
        /// exactly this line per bundle and then folds every one of them into an aggregate (:66), and
        /// <c>ApplyProject</c>'s single-bundle answer can only speak for the ONE bundle its caller named -
        /// so a panel with five rows had two ways to learn what happened to the other four: parse the log,
        /// or install twice. Both are forbidden, and this is the third.</summary>
        internal sealed class TargetInstall
        {
            internal readonly string Bundle;
            /// <summary>The producer's own line for this target, VERBATIM - never re-composed, and never
            /// parsed to work out <see cref="Outcome"/>, which is measured separately.</summary>
            internal readonly string Line;
            internal readonly ApplyDisposition Outcome;

            internal TargetInstall(string bundle, string line, ApplyDisposition outcome)
            {
                Bundle = bundle; Line = line; Outcome = outcome;
            }
        }
    }
}
