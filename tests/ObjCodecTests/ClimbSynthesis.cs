using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Morgott.ContentTool.Bake;
using Morgott.ContentTool.Import;
using Morgott.ContentTool.Tactical;

/// <summary>
/// The TRAVERSAL synthesis, offline: a creature whose model ships nothing but a walk cycle still gets
/// three clips that carry it UP an obstacle, and the two halves that have to agree for that to work
/// really do.
///
/// Three failures this arm exists to catch, each of which is invisible in a bake log and expensive in
/// game (a 5 s "waiting for animation ... timed out" per link, or a creature stuck half way up a wall):
///  * the LOOP stops looping - the engine replays it over the variable remainder of every link
///    (TacticalNavigationComponent.cs:324,339), so one that holds its last frame stops the mover dead;
///  * its OFFSET stops pointing up - AnimationInfos.GetAnimInfo:104-121 measures Offset = end - start on
///    the root-motion node and ClimbPathProcessor puts the loop point at anchor + Offset.y, so a climb
///    that does not rise sends the actor somewhere the animation never goes;
///  * the navmesh AREAS and the filled SLOTS disagree - an area without clips is EmitClimbFallback's
///    L-shaped teleport, clips without the area is a creature that walks around everything. Both are
///    read off <see cref="ClimbPlan"/> by the runtime, so this checks that table is 1:1.
///
/// Every arm is falsified: the same writer given a downward rise must be SEEN to point down, and the
/// same writer given no pitch must leave the walk's rotations untouched.
/// </summary>
internal static class ClimbSynthesis
{
    private const string Probe = "u8_probe.glb";
    private const string Root = "spider";
    private const string Walk = "Spider_Walk";

    private static int checks;

    internal static string Run()
    {
        checks = 0;

        // ---- the TABLE, which is where the two halves either agree or silently do not
        Check(ClimbPlan.Parts.Length == ClimbPlan.Slots.Length && ClimbPlan.Parts.Length == 3,
              "a ClipSequence is Start/Loop/Stop and the plan names exactly those three parts " +
              "(ClipSequence.cs:16-25 tests all three for non-null)");
        Check(ClimbPlan.Table.Length > 0, "the traversal table names at least one family");
        var slots = new HashSet<string>();
        foreach (ClimbPlan.Family f in ClimbPlan.Table)
        {
            Check(slots.Add(f.Slot), "'" + f.Slot + "' is named once, so a lookup cannot depend on " +
                  "which row is found first");
            Check(!string.IsNullOrEmpty(f.Area), "'" + f.Slot + "' names the navmesh area that routes " +
                  "onto it - a family with no area is a creature that never meets the link it can now " +
                  "perform");
            Check(f.State != null && f.State.Length > 0, "'" + f.Slot + "' names the controller tokens " +
                  "its states carry - without them the def would promise a clip no state plays, which " +
                  "is a 5s WaitForAnimation:175 per point and not a missing feature");
            Check(f.Sequence ? f.Part == null : Array.IndexOf(ClimbPlan.Parts, f.Part) >= 0,
                  "'" + f.Slot + "' is a " + (f.Sequence ? "ClipSequence, so it takes all three parts"
                      : "single AnimationClip field, so it names ONE synthesised part and names a real " +
                        "one ('" + f.Part + "')"));
            // Two rows MAY share a state set - the controller has one set of drop states and both the
            // plain drop and the vault-then-drop enter it - but then they must want the same clip out
            // of it, or which def slot a state answers for would be a coin toss.
            foreach (ClimbPlan.Family g in ClimbPlan.Table)
                Check(f == g || !(Sub(f.State, g.State) && Sub(g.State, f.State)) ||
                      (f.Sequence == g.Sequence && f.Part == g.Part),
                      "'" + f.Slot + "' and '" + g.Slot + "' share the state tokens [" +
                      string.Join("+", f.State) + "] and must therefore take the same clip out of them " +
                      "- one controller state cannot play two different parts at once");
        }
        // The two halves of the SAME slot path: what the def writer takes and what the controller remap
        // hands the state it enters have to be the same part, or the wait can never end.
        foreach (ClimbPlan.Family f in ClimbPlan.Table)
        {
            if (!f.Sequence) { Check(ClimbPlan.PartOfSlot(f.Slot + "Alt") == f.Part,
                  "'" + f.Slot + "Alt' - the twin the engine alternates onto " +
                  "(TacticalPathProcessor._useAlternativeAnimSlot) - takes the SAME part, or every " +
                  "other crossing silently degrades"); continue; }
            for (int i = 0; i < ClimbPlan.Parts.Length; i++)
                Check(ClimbPlan.PartOfSlot(f.Slot + "." + ClimbPlan.Slots[i]) == ClimbPlan.Parts[i],
                      "'" + f.Slot + "." + ClimbPlan.Slots[i] + "' takes the '" + ClimbPlan.Parts[i] +
                      "' clip");
        }

        // ---- THE CONTROLLER VOCABULARY, read live off 'HumanoidAnimatorLOC' (the 69 overridable clips
        // a shipped rig really carries). The def slot and the state must resolve to the same part.
        string[] real =
        {
            "MV_ClimbDropLowStart_AR", "MV_DropLoop_AR", "MV_ClimbDropLowStop_AR",
            "MV_ClimbDropLowStartWall_AR",
            "MV_ClimbLadderUp_Start_NoGunA", "MV_ClimbLadderUp_Loop_NoGunA", "MV_ClimbLadderUp_Stop_NoGunA",
            "MV_ClimbLadderDwnStart_NoGunA", "MV_ClimbLadderDwnLoop_NoGunA", "MV_ClimbLadderDwnStop_NoGunA",
            "MV_ClimbLowObject_Up_AR", "MV_ClimbLowObject_Up_AR_Alt",
            "MV_ClimbLowObject_Dwn_AR", "MV_ClimbLowObject_Dwn_AR_Alt",
            "MV_ClimbLowObject_Over1Tile_AR", "MV_ClimbLowObject_Over1Tile_AR_Alt",
            "MV_ClimbLowObject_Over2Tiles_AR",
            "MV_RunFwd_Loop_AR", "HL_Idle_AR", "MV_MountStartPlaceholder", "Dilo_Ram_RunLoop"
        };
        var everything = new HashSet<string>();
        foreach (ClimbPlan.Family f in ClimbPlan.Table) everything.Add(f.Slot);
        foreach (ClimbPlan.Family f in ClimbPlan.Table)
        {
            string why = ClimbPlan.Refuse(f, real, p => true);
            bool jumpUp = f.Slot == "JumpUpOneLevel";
            Check(jumpUp ? why != null : why == null, "'" + f.Slot + "' " + (jumpUp
                ? "is REFUSED BY NAME against a real controller (" + why + ") - no shipped humanoid " +
                  "controller has a jump-up-one-level state, and its area 256 is not in a Humanoid " +
                  "agent's mask (125) either, so claiming it would be a promise with nothing behind it"
                : "is covered by a real controller's states, not by what the donor def happened to fill: " +
                  (why ?? "")));
        }
        Check(ClimbPlan.PartOfState("MV_ClimbLowObject_Up_AR", everything) == "start" &&
              ClimbPlan.PartOfState("MV_ClimbLowObject_Up_AR", everything) ==
              ClimbPlan.PartOfSlot("ClimbUpLowObstacle"),
              "the state 'MV_ClimbLowObject_Up_AR' and the def field 'ClimbUpLowObstacle' - two names " +
              "for one crossing in two vocabularies - resolve to the SAME synthesised clip, which is " +
              "the whole of what WaitForAnimation:175 compares");
        Check(ClimbPlan.PartOfState("MV_ClimbLowObject_Over1Tile_AR", everything) ==
              ClimbPlan.PartOfSlot("JumpOverLowWall") &&
              ClimbPlan.PartOfState("MV_ClimbLowObject_Over2Tiles_AR", everything) ==
              ClimbPlan.PartOfSlot("JumpOverLowObstacle"),
              "the one-tile vault and the two-tile one are told apart by 'tile' against 'tiles' - the " +
              "only thing in the controller's names that distinguishes them");
        Check(ClimbPlan.PartOfState("MV_ClimbLadderUp_Loop_NoGunA", everything) == "loop" &&
              ClimbPlan.PartOfState("MV_ClimbLadderDwnStop_NoGunA", everything) == "stop",
              "a ladder's own states resolve to the parts their def slots take");
        foreach (string flat in new[] { "MV_RunFwd_Loop_AR", "HL_Idle_AR", "MV_MountStartPlaceholder",
                                        "Dilo_Ram_RunLoop" })
            Check(ClimbPlan.PartOfState(flat, everything) == null,
                  "'" + flat + "' is NOT a traversal state and takes no climb clip - a walk, an idle, " +
                  "a mount and a ram must keep the clips their roles give them");
        // FALSIFICATION: an unfilled family answers for nothing, so a def slot we left empty can never
        // be contradicted by the controller override.
        Check(ClimbPlan.PartOfState("MV_ClimbLowObject_Up_AR", new HashSet<string>()) == null,
              "a family that was NOT filled claims none of its states - the arm above is reading the " +
              "filled set and not merely matching a name");
        Check(ClimbPlan.Loops("loop") && !ClimbPlan.Loops("start") && !ClimbPlan.Loops("stop"),
              "the LOOP cycles and the one-shot parts do not");
        foreach (string part in ClimbPlan.Parts)
            Check(ClimbPlan.Rise(part) > 0f, "part '" + part + "' rises " +
                  F(ClimbPlan.Rise(part)) + " - a traversal clip that does not go UP cannot satisfy a " +
                  "vertical segment however honestly the rest of it is built");

        // ---- the CLIPS, written by the same call the bake makes
        string probe = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"..\..\..\..\..\lib\" + Probe);
        if (!File.Exists(probe))
            return "CLIMB VOID - no " + Path.GetFullPath(probe) + " (" + checks + " table check(s) passed)";

        var clips = new List<SampledClip>();
        BakedSkin skin = ModelBuild.From(GlbReader.Read(File.ReadAllBytes(probe), clips), Root);
        SampledClip walk = null;
        foreach (SampledClip c in clips)
            if (string.Equals(c.Name, Walk, StringComparison.OrdinalIgnoreCase)) walk = c;
        Check(walk != null, "the fixture carries '" + Walk + "' to synthesise the climb out of");
        int frames = walk.Times.Length;
        string rootPath = skin.BonePath(Treadmill.RootBone(skin));

        float rise = 0f;
        foreach (string part in ClimbPlan.Parts)
        {
            float from, to;
            ClimbPlan.Pitch(part, out from, out to);
            string why;
            List<ClipFields.Binding> b = ClipFields.Climb(walk, skin, 1f, ClimbPlan.Rise(part),
                                                          from * 90f, to * 90f, out why);
            Check(b != null, "'" + part + "' was written: " + why);
            float y = ClipFields.RiseOf(b, skin, frames);
            Check(y > 0f, "'" + part + "' rises " + F(y) + " on '" + rootPath + "' - this is the " +
                  "engine's own Offset.y (AnimationInfos.cs:104-121) and it MUST point up");
            Check(Math.Abs(y - ClimbPlan.Rise(part)) < 1e-4f, "'" + part + "' rises exactly the " +
                  F(ClimbPlan.Rise(part)) + " the plan declares, not " + F(y) + " - the number the " +
                  "path builder stretches the middle against is the number the clip really carries");
            Check(Flat(b, rootPath, frames) < 1e-4f, "'" + part + "' carries no sideways drift - the " +
                  "walk's forward travel was REPLACED, so the actor lands on the link's own anchor");
            if (part == "loop") rise = y;
        }

        // ---- the PITCH: it turns the body, and it turns it only when asked
        List<ClipFields.Binding> pitched = ClipFields.Climb(walk, skin, 1f, 1f, 90f, 90f, out _);
        List<ClipFields.Binding> barely = ClipFields.Climb(walk, skin, 1f, 1f, 1f, 1f, out _);
        Check(Find(pitched, ClipFields.AttributeRotation, rootPath) != null,
              "a pitched climb writes a rotation curve on the root even though this rig's walk never " +
              "rotates it - the rest pose is what it rides, so nothing the import folded in is lost");
        float turned = RotationDelta(pitched, barely, rootPath, frames);
        Check(turned > 0.5f, "90 degrees of nose-up pitch really turns the root, and 1 degree barely " +
              "does (quaternion moved " + F(turned) + " between them) - this is what makes an ordinary " +
              "walk cycle read as climbing a wall rather than sliding up it upright");
        List<ClipFields.Binding> level = ClipFields.Climb(walk, skin, 1f, 1f, 0f, 0f, out _);
        Check(level.Count == ClipFields.Bindings(walk, skin).Count,
              "and 0 degrees - the default, honest for a biped - adds no rotation curve at all, so the " +
              "walk's own pose is exactly what the file wrote");

        // ---- FALSIFICATION: the metric can see a climb that goes the wrong way
        List<ClipFields.Binding> down = ClipFields.Climb(walk, skin, 1f, -1f, 0f, 0f, out _);
        Check(ClipFields.RiseOf(down, skin, frames) < 0f, "a clip written to descend reads as NEGATIVE " +
              "rise, so the arm above is measuring the direction and not merely finding a curve");

        return "CLIMB PASS, " + checks + " check(s) - three parts out of '" + Walk + "', the loop " +
               "rising " + F(rise) + " tile(s) on '" + rootPath + "' and cycling, " +
               ClimbPlan.Table.Length + " traversal family(ies), each paired with its navmesh area and " +
               "with the controller states that play it";
    }

    /// <summary>Is every token of <paramref name="a"/> in <paramref name="b"/>.</summary>
    private static bool Sub(string[] a, string[] b)
    {
        foreach (string s in a) if (Array.IndexOf(b, s) < 0) return false;
        return true;
    }

    /// <summary>The furthest the root strays from its frame-0 X/Z - a climb goes straight up.</summary>
    private static float Flat(List<ClipFields.Binding> bindings, string rootPath, int frames)
    {
        ClipFields.Binding b = Find(bindings, ClipFields.AttributePosition, rootPath);
        if (b == null) return float.MaxValue;
        int w = ClipFields.PositionCurves;
        float worst = 0f;
        for (int f = 0; f < frames; f++)
        {
            float dx = Math.Abs(b.Values[f * w] - b.Values[0]);
            float dz = Math.Abs(b.Values[f * w + 2] - b.Values[2]);
            if (dx > worst) worst = dx;
            if (dz > worst) worst = dz;
        }
        return worst;
    }

    /// <summary>How far apart two bakes put the root's rotation, over every frame.</summary>
    private static float RotationDelta(List<ClipFields.Binding> a, List<ClipFields.Binding> b,
                                       string rootPath, int frames)
    {
        ClipFields.Binding x = Find(a, ClipFields.AttributeRotation, rootPath);
        ClipFields.Binding y = Find(b, ClipFields.AttributeRotation, rootPath);
        if (x == null || y == null) return 0f;
        float worst = 0f;
        for (int i = 0; i < frames * 4; i++)
        {
            float d = Math.Abs(x.Values[i] - y.Values[i]);
            if (d > worst) worst = d;
        }
        return worst;
    }

    private static ClipFields.Binding Find(List<ClipFields.Binding> bindings, uint attribute, string path)
    {
        foreach (ClipFields.Binding b in bindings)
            if (b.Attribute == attribute && b.BonePath == path) return b;
        return null;
    }

    private static string F(float v) { return v.ToString("0.######", CultureInfo.InvariantCulture); }

    private static void Check(bool ok, string what)
    {
        checks++;
        if (!ok) throw new Exception("CLIMB FAIL: " + what);
    }
}
