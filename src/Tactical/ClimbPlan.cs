using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Morgott.ContentTool.Tactical
{
    /// <summary>
    /// THE TRAVERSAL CONTRACT, IN ONE PLACE, because its three halves are what silently disagree.
    ///
    /// A creature crosses an obstacle only when ALL of these are true at once:
    ///  * the navmesh AREA of the link is in its agent mask (NavMeshNavigationComponent:40,73 -&gt;
    ///    NavMeshPathRequest(NavAreas,...)), so a route over it is built at all;
    ///  * the matching anim-action FIELD on TacActorNavAnimActionDef holds the clip(s) the processor for
    ///    that link asks for - a ClipSequence with all three of Start/Loop/Stop for the drop and ladder
    ///    families (ClimbPathProcessor.EmitClimb:90 <c>HasAllAnimations</c>), a single AnimationClip for
    ///    the low-obstacle and jump-over families (ClimbLowUpProcessor:11-16, JumpOverProcessor:24-27);
    ///  * the animator CONTROLLER really has a state that plays that clip, or WaitForAnimation:175 loops
    ///    on a state that never comes and gives up after 5 s per point.
    /// Route without clips = EmitClimbFallback's L-shaped teleport; clips without the route = a creature
    /// that walks around everything; clips the controller cannot reach = the 12 s stall. All three are
    /// read off THIS table by <see cref="CreatureBuild"/> and by the offline check.
    ///
    /// THE TWO VOCABULARIES ARE DIFFERENT AND THAT IS THE WHOLE DIFFICULTY. The def names its field
    /// <c>ClimbUpLowObstacle</c>; the controller a shipped rig actually runs ('HumanoidAnimatorLOC',
    /// READ LIVE off its 69 overridable clips) calls the state that plays it
    /// <c>MV_ClimbLowObject_Up_AR</c>. So a row names BOTH: the def field it fills and the tokens every
    /// controller state of that family carries. Because both sides resolve to the same three synthesised
    /// clip OBJECTS through the same part names, the def's promise and the state's clip cannot disagree.
    ///
    /// WHY THE DONOR IS NO LONGER CONSULTED. A family used to be filled only where the DONOR def had
    /// filled it, which left every custom creature unable to ASCEND: the Swarmer ships a null
    /// ClimbUpLadder and the engine's <c>GetClimbAnimSequence</c> (ClimbPathProcessor.cs:50-51) returns
    /// null for JumpUpOneLevel no matter what the def holds. The controller, not the donor, is the thing
    /// that decides whether a state exists, so the controller is what is asked.
    /// </summary>
    internal static class ClimbPlan
    {
        /// <summary>What the bake names the synthesised clips, after the model stem. The runtime finds
        /// them by this suffix, so neither side spells a full asset address.</summary>
        internal const string Suffix = "_ct_climb_";

        /// <summary>The three parts, index-matched to <see cref="Slots"/> - the field names of the
        /// engine's own ClipSequence, which is what <c>HasAllAnimations</c> (ClipSequence.cs:16-25)
        /// tests for non-null.</summary>
        internal static readonly string[] Parts = { "start", "loop", "stop" };
        internal static readonly string[] Slots = { "Start", "Loop", "Stop" };

        /// <summary>One traversal family: the def field it fills, the navmesh area that routes onto it,
        /// and the controller states that play it.</summary>
        internal sealed class Family
        {
            /// <summary>The def field on TacActorNavAnimActionDef. Its <c>*Alt</c> twin - the game
            /// alternates between the two (TacticalPathProcessor._useAlternativeAnimSlot) - shares this
            /// prefix and is filled with it, because a null Alt is every OTHER crossing degrading.</summary>
            internal string Slot;
            /// <summary>The navmesh area whose links reach this family, read off the engine's own
            /// predicates (NavMeshPathRequest.IsJumpLink/IsClimbLowLink/IsClimbUpLink/IsClimbDownLink)
            /// and confirmed live: the area names really do resolve on a running map, Jump=4,
            /// ClimbLadder=8, RoofDrop=16, LowObstacle=32, LowObstacleRoofDrop=64, JumpUpOneLevel=256.</summary>
            internal string Area;
            /// <summary>A ClipSequence (Start/Loop/Stop), against a single AnimationClip field.</summary>
            internal bool Sequence;
            /// <summary>Which synthesised part a SINGLE-clip family takes - the rising start for a
            /// crossing that goes up or across, the levelling stop for one that goes down.</summary>
            internal string Part;
            /// <summary>The tokens EVERY controller state of this family carries, and no state of any
            /// other family does. Split by <see cref="Tokens"/>, so 'Over1Tile' is over+tile and
            /// 'Over2Tiles' is over+tiles - which is what tells the one-tile vault from the two-tile one.</summary>
            internal string[] State;
        }

        /// <summary>
        /// EVERY FAMILY A REAL MAP ROUTES A ONE-TILE AGENT OVER. The last row is deliberately present
        /// and deliberately never fills: no shipped humanoid controller carries a jump-up-one-level
        /// state (its area, 256, is not in a Humanoid agent's mask either - READ LIVE, the mask is 125),
        /// so it is REFUSED BY NAME in the build log rather than quietly missing. Mount, Ram, JetJump
        /// and FallNoSupport are absent on purpose: they are abilities and hazards, not links a path
        /// request routes over, so no navmesh area of ours can offer them.
        /// </summary>
        internal static readonly Family[] Table =
        {
            new Family { Slot = "DropDown",            Area = "RoofDrop",            Sequence = true,  State = new[] { "drop" } },
            new Family { Slot = "JumpOverAndDropDown", Area = "LowObstacleRoofDrop", Sequence = true,  State = new[] { "drop" } },
            new Family { Slot = "ClimbUpLadder",       Area = "ClimbLadder",         Sequence = true,  State = new[] { "ladder", "up" } },
            new Family { Slot = "ClimbDownLadder",     Area = "ClimbLadder",         Sequence = true,  State = new[] { "ladder", "dwn" } },
            new Family { Slot = "ClimbUpLowObstacle",  Area = "LowObstacle",         Part = "start",   State = new[] { "object", "up" } },
            new Family { Slot = "ClimbDownLowObstacle",Area = "LowObstacle",         Part = "stop",    State = new[] { "object", "dwn" } },
            new Family { Slot = "JumpOverLowWall",     Area = "Jump",                Part = "start",   State = new[] { "object", "tile" } },
            new Family { Slot = "JumpOverLowObstacle", Area = "Jump",                Part = "start",   State = new[] { "object", "tiles" } },
            new Family { Slot = "JumpUpOneLevel",      Area = "JumpUpOneLevel",      Part = "start",   State = new[] { "jump", "level" } },
        };

        /// <summary>The family a def slot path belongs to ("DropDown.Loop", "ClimbUpLowObstacleAlt"), or
        /// null for a slot no family covers - which is the answer that CLEARS it.</summary>
        internal static Family For(string slot)
        {
            int dot = slot.IndexOf('.');
            string field = dot < 0 ? slot : slot.Substring(0, dot);
            Family best = null;
            foreach (Family f in Table)
                if (field.StartsWith(f.Slot, StringComparison.Ordinal) &&
                    (best == null || f.Slot.Length > best.Slot.Length)) best = f;
            return best;
        }

        /// <summary>Which of the three synthesised parts a def slot takes: the dotted suffix for a
        /// sequence, the row's own answer for a single clip. Null when no family covers it.</summary>
        internal static string PartOfSlot(string slot)
        {
            Family f = For(slot);
            if (f == null) return null;
            if (!f.Sequence) return f.Part;
            int dot = slot.IndexOf('.');
            if (dot < 0) return null;
            int i = Array.IndexOf(Slots, slot.Substring(dot + 1));
            return i < 0 ? null : Parts[i];
        }

        /// <summary>
        /// Which part a CONTROLLER state plays - the other half of the same wait, keyed on the
        /// controller's own name because that is what <c>WaitForAnimation:175</c> compares against.
        /// Only families in <paramref name="filled"/> answer: a state whose def slot we left empty must
        /// keep taking the ordinary walk, or the def and the controller would name different clips.
        /// </summary>
        internal static string PartOfState(string clip, ICollection<string> filled)
        {
            string[] t = Tokens(clip);
            foreach (Family f in Table)
            {
                if (!Is(f, t) || (filled != null && !filled.Contains(f.Slot))) continue;
                string part = f.Sequence ? PartIn(t) : f.Part;
                if (part != null) return part;
            }
            return null;
        }

        /// <summary>
        /// WHY A FAMILY CANNOT BE FILLED, or null when it can - the one gate, so the def slots, the
        /// controller overrides and the navmesh areas are three readings of one answer.
        /// </summary>
        /// <param name="states">every clip name the creature's animator controller contains.</param>
        /// <param name="have">whether the creature has the synthesised (or authored) clip for a part.</param>
        internal static string Refuse(Family f, IEnumerable<string> states, Func<string, bool> have)
        {
            List<string> missing = new List<string>();
            foreach (string part in f.Sequence ? Parts : new[] { f.Part })
                if (!have(part)) missing.Add("no '" + part + "' clip");
            if (missing.Count > 0) return string.Join(", ", missing.ToArray());
            foreach (string part in f.Sequence ? Parts : new[] { f.Part })
            {
                bool found = false;
                foreach (string s in states)
                {
                    string[] t = Tokens(s);
                    if (Is(f, t) && (!f.Sequence || PartIn(t) == part)) { found = true; break; }
                }
                if (!found) return "the controller has no [" + string.Join("+", f.State) +
                                   (f.Sequence ? "+" + part : "") + "] state, so a filled slot would " +
                                   "wait 5s for a clip nothing plays";
            }
            return null;
        }

        /// <summary>Every distinct area in the table, each with the families that must ALL be filled
        /// before it may be added - one area serves two crossings (LowObstacle is up AND down, Jump is
        /// the one-tile vault AND the two-tile one) and half a pair is a route the creature meets and
        /// cannot finish.</summary>
        internal static Dictionary<string, List<Family>> ByArea()
        {
            Dictionary<string, List<Family>> map = new Dictionary<string, List<Family>>();
            foreach (Family f in Table)
            {
                if (!map.ContainsKey(f.Area)) map[f.Area] = new List<Family>();
                map[f.Area].Add(f);
            }
            return map;
        }

        private static bool Is(Family f, string[] tokens)
        {
            foreach (string want in f.State) if (Array.IndexOf(tokens, want) < 0) return false;
            return true;
        }

        private static string PartIn(string[] tokens)
        {
            if (Array.IndexOf(tokens, "start") >= 0) return "start";
            if (Array.IndexOf(tokens, "loop") >= 0) return "loop";
            if (Array.IndexOf(tokens, "stop") >= 0 || Array.IndexOf(tokens, "end") >= 0) return "stop";
            return null;
        }

        /// <summary>"MV_ClimbLowObject_Over1Tile_AR" -&gt; mv, climb, low, object, over, tile, ar. Splits
        /// on CamelCase and on anything that is not a letter, so digits never glue two words together.
        /// The ONE implementation - <see cref="CreatureBuild.Tokens"/> calls it, so the role classifier
        /// and this table can never disagree about where a word ends.</summary>
        internal static string[] Tokens(string name)
        {
            string[] raw = Regex.Split(name, "(?<!^)(?=[A-Z])|[^A-Za-z]+");
            List<string> t = new List<string>(raw.Length);
            foreach (string s in raw) if (s.Length > 0) t.Add(s.ToLowerInvariant());
            return t.ToArray();
        }

        /// <summary>Only the LOOP cycles. TacticalNavigationComponent.cs:324,339 replays it while the
        /// position lerps over the variable remainder of the link, so a loop that holds its last frame
        /// freezes the creature mid-wall.</summary>
        internal static bool Loops(string part) { return part == "loop"; }

        /// <summary>
        /// How far each part rises, in WORLD units (a tile is 1.0, TacticalMap.cs:67).
        ///
        /// These are not the link's height and do not have to be: EmitClimbStartAndLoop:226,245,277
        /// puts the loop point at <c>anchor + Offset.y*up</c> and EmitClimbStop:168,182 at
        /// <c>anchor - Offset.y*up</c>, and whatever is left between them is covered by REPLAYING the
        /// loop while the position lerps at the loop's own Speed. So the only requirements are that the
        /// numbers are honest about what the clip does and that the loop's points UP.
        ///
        /// ponytail: all three RISE, including the stop, so a DESCENT is emitted with the up-flavoured
        /// segment params (EmitClimbStop:177 reads the sign of Offset.y). It costs nothing here because
        /// every state of a family maps to the same three clip objects, so the wait matches either way -
        /// but a descent-specific stop clip is the upgrade if the pose ever needs to differ.
        /// </summary>
        internal static float Rise(string part) { return part == "loop" ? 1f : 0.3f; }

        /// <summary>How much shorter than the walk cycle a start/stop plays. The clips carry the walk's
        /// own frames, so the only thing that shortens them is the rate they are played back at.</summary>
        internal const float ShortBy = 3f;

        /// <summary>The pitch each part carries, from -&gt; to, as a fraction of the manifest's angle:
        /// the start tips the creature onto the wall, the loop holds it there, the stop puts it level
        /// again at the top.</summary>
        internal static void Pitch(string part, out float from, out float to)
        {
            from = part == "start" ? 0f : 1f;
            to = part == "stop" ? 0f : 1f;
        }
    }
}
