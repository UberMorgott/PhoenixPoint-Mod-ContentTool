using System;
using System.Collections.Generic;
using System.Linq;

namespace Morgott.ContentTool.Tactical
{
    /// <summary>
    /// ============ WHERE A HOLE IN THE ROLE MAP IS SAFE, AND WHERE IT COSTS TEN SECONDS ============
    ///
    /// A content mod maps its model's clips to roles in ppcontent.json (<see cref="CreatureManifest"/>),
    /// and it will inevitably map some and forget others. The two halves of that are NOT the same
    /// failure, and this file is the one place the difference is written down:
    ///
    ///  * <see cref="Empty"/> - roles the ENGINE ITSELF degrades safely. Leaving them unmapped is the
    ///    right answer and filling them is the bug. The traversal families are cleared on purpose
    ///    (CreatureBuild.cs:1116-1162) so ClimbPathProcessor.EmitClimb:90 takes its own shipped
    ///    fallback (:107-110) instead of trusting a flat walk to carry the creature up a wall, and
    ///    "ranged" has a shipped degrade of its own - the general shoot action covers the spit with
    ///    the attack clip (CreatureRanged.cs:411-417).
    ///  * <see cref="Fill"/> - roles the game BLOCKS on. An unmapped one leaves the donor's clip in the
    ///    slot, which names none of our bones (CreatureBuild.cs:1169-1172), and an event-less clip in
    ///    the Action state costs a 10 s AnimEventReceiver timeout per blocking event
    ///    (AnimEventReceiver.cs:100,126 - measured in game as a bash landing 23 s after it started).
    ///    So these are SUBSTITUTED rather than left alone, by the rule the code already used for a
    ///    role with no clip: the idle is the default (CreatureBuild.cs:1007,1175).
    ///
    /// UnityEngine-free on purpose, exactly like <see cref="ClimbPlan"/>: the contract that decides
    /// whether a creature hangs is checked by the offline gate rather than by watching one freeze.
    /// </summary>
    internal static class CreatureRoles
    {
        /// <summary>
        /// One blocking animation event and WHERE in its clip it fires, as a fraction of the clip's
        /// length. A fraction rather than a second so the number survives a re-export at a different
        /// frame rate.
        /// </summary>
        internal struct Event
        {
            internal string Name;
            internal float At;
        }

        /// <summary>
        /// The roles the engine drives and BLOCKS on, so a hole in one is substituted and never left.
        ///
        /// Each is a state the shipped controllers reach: locomotion (TacticalNavigationComponent waits
        /// for the nav clip), the Action state (BashAbility/TacticalAbility wait for ActionDo/ShootShot/
        /// ActionEnd), the Die state (RagdollDieAbility waits for Ragdoll), the idle everything falls
        /// back to, and the flinch (TacticalActor.cs:1627-1633 writes GetReactionAnimation over
        /// DefaultReactionClip and fires SetTrigger("Reaction")).
        /// </summary>
        internal static readonly string[] Fill = { "walk", "idle", "attack", "death", "reaction" };

        /// <summary>
        /// The roles a creature is BETTER OFF not having, because the engine already degrades them.
        /// "jump" and "climb" are traversal: a FILLED traversal slot is a promise the engine trusts
        /// absolutely, and an empty one is the shipped fallback (CreatureBuild.cs:1118-1143; the climb
        /// the bake synthesises out of the walk cycle still fills these when it really rises).
        /// "ranged" is optional twice over - CreatureRanged.cs:411-417 says so and covers the shot.
        /// </summary>
        internal static readonly string[] Empty = { "jump", "climb", "ranged" };

        /// <summary>Every role there is - the two classes and nothing else, so a role can never be
        /// added to one and forgotten by the other.</summary>
        internal static readonly string[] All = Fill.Concat(Empty).ToArray();

        /// <summary>
        /// Which blocking animation events each role's clip has to carry, and the NOMINAL times an
        /// auto-filled stand-in gets them at. The names are HARD FACTS of the decompile, not a policy:
        /// TacticalAbility.cs:1206,1214 wait for ActionDo then ActionEnd, BashAbility.cs:465 for
        /// ShootShot in between, RagdollDieAbility.cs:95 for Ragdoll.
        ///
        /// The TIMES are the honest weakness and are only ever used on a clip the author never mapped:
        /// where a hit connects is a property of the ANIMATION and only the author knows it, so a
        /// mapped role still gets its times from ppcontent.json and the bake still names an undeclared
        /// one out loud. ponytail: evenly spread, in the order the abilities wait - two events sharing
        /// a timestamp are not two events, the second fires while nothing is listening.
        /// </summary>
        internal static readonly KeyValuePair<string, Event[]>[] Blocking =
        {
            new KeyValuePair<string, Event[]>("attack", new[]
            {
                new Event { Name = "ActionDo",  At = 0.25f },
                new Event { Name = "ShootShot", At = 0.55f },
                new Event { Name = "ActionEnd", At = 0.90f },
            }),
            new KeyValuePair<string, Event[]>("death", new[]
            {
                new Event { Name = "Ragdoll", At = 0.90f },
            }),
        };

        /// <summary>The blocking events <paramref name="role"/>'s clip must fire, or none.</summary>
        internal static Event[] BlockingFor(string role)
        {
            foreach (KeyValuePair<string, Event[]> e in Blocking)
                if (string.Equals(e.Key, role, StringComparison.OrdinalIgnoreCase)) return e.Value;
            return new Event[0];
        }

        /// <summary>
        /// The clip name a role the author left unmapped should play - the model's OWN clip in every
        /// case, never the donor's, because the donor's names none of our bones and plays as a freeze.
        ///
        /// The order is the one the code already used and not a new policy: the IDLE is the default a
        /// slot with no clip for its role falls back to (CreatureBuild.cs:1175) and the answer an
        /// unknown clip name resolves to (:1007). Failing that, any clip the author DID map, and
        /// failing that the first clip the model ships at all - a stand-in that looks wrong still
        /// leaves a creature that plays, which an unfilled blocking role does not.
        /// Null only when the model ships no clips whatsoever, which is reported and not filled.
        /// </summary>
        internal static string Substitute(Func<string, string> clipForRole, IList<string> known)
        {
            string idle = clipForRole("idle");
            if (idle != null) return idle;
            foreach (string role in All)
            {
                string mapped = clipForRole(role);
                if (mapped != null) return mapped;
            }
            return known != null && known.Count > 0 ? known[0] : null;
        }
    }
}
