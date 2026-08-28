using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using Morgott.ContentTool.Import;

namespace Morgott.ContentTool.Tools
{
    /// <summary>
    /// C1-axis, offline. Measures the CustomCreature demo's .glb through ContentTool's OWN importer -
    /// the same GlbReader/ModelBuild the bake runs, compiled in, so this measures what the bundle
    /// contains and not a second reader written for the check - and asserts that the two constants
    /// the mod applies to the rig root MATCH that measurement:
    ///
    ///   ModelUp  must be the axis the model actually stands on, sign included. The mod turns it into
    ///            a rotation with Quaternion.FromToRotation(ModelUp, Vector3.up), whose contract is
    ///            "carry fromDirection onto toDirection" - so the ONLY thing that can be wrong is
    ///            this measurement, and no Unity Euler-order convention is assumed anywhere here.
    ///   RigLift  must be exactly how far the lowest vertex sits BELOW the origin along ModelUp, or
    ///            the creature stands in a hole (or floats) by the difference.
    ///
    /// The up axis is not eyeballed off the bounding box: it is read off the SKELETON, from where the
    /// eight *Foot bones rest against Body/Thorax/Abdomen/Head, and the check refuses to conclude
    /// anything unless that separation dominates the other two axes by 3x.
    ///
    /// It also asserts the model's FRONT (where the *Front* feet are, against the *Back* ones) comes
    /// out along Unity's +Z, so a correction that stands the creature up backwards is red too.
    /// </summary>
    internal static class Program
    {
        private static readonly string[] Axis = { "X", "Y", "Z" };
        private static int failures;

        private static int Main(string[] argv)
        {
            string here = AppDomain.CurrentDomain.BaseDirectory;
            string demo = Path.GetFullPath(Path.Combine(here, @"..\..\..\..\..\demos\CustomCreature"));
            string glb = argv.Length > 0 ? argv[0] : Path.Combine(demo, @"Content\Models\cyborg_spider.glb");
            string source = argv.Length > 1 ? argv[1] : Path.Combine(demo, @"src\CustomCreatureMain.cs");

            Console.WriteLine("model  " + glb);
            Console.WriteLine("source " + source);
            var clips = new List<SampledClip>();
            SkinnedModel model = GlbReader.Read(File.ReadAllBytes(glb), clips);
            BakedSkin skin = ModelBuild.From(model, "spider");

            // ---- what the FILE says ------------------------------------------------------------
            float[] feet = Mean(skin, n => n.IndexOf("Foot", StringComparison.OrdinalIgnoreCase) >= 0);
            float[] body = Mean(skin, n => n == "Body" || n == "Thorax" || n == "Abdomen" || n == "Head");
            float[] front = Mean(skin, n => n.IndexOf("Front", StringComparison.OrdinalIgnoreCase) >= 0 &&
                                            n.IndexOf("Foot", StringComparison.OrdinalIgnoreCase) >= 0);
            float[] back = Mean(skin, n => n.IndexOf("Back", StringComparison.OrdinalIgnoreCase) >= 0 &&
                                           n.IndexOf("Foot", StringComparison.OrdinalIgnoreCase) >= 0);

            // THE UP AXIS IS THE ONE THE FEET ARE COPLANAR ON. That is what a foot IS - eight of them
            // resting on one plane, and that plane is the ground. Reading it off the bounding box
            // instead ("the thin axis must be height") is a guess about the animal; this is a
            // measurement. Here the eight *Foot bones spread 0.2406 along Y against 162.9381 on the
            // next axis, so Y wins by ~677x - not a judgement call.
            int up = Flattest(skin, "Foot", out float upSpread, out float upRunnerUp);
            float upSign = body[up] - feet[up] < 0f ? -1f : 1f;
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "the 8 feet are coplanar on {0} (spread {1:F4} against {2:F4} on the next axis), and the " +
                "body sits on its {3} side", Axis[up], upSpread, upRunnerUp, upSign < 0 ? "-" : "+"));
            Assert(upRunnerUp > upSpread * 10f, "the feet are not clearly coplanar on any axis (" +
                upSpread.ToString("F4", CultureInfo.InvariantCulture) + " against " +
                upRunnerUp.ToString("F4", CultureInfo.InvariantCulture) +
                ") - the ground plane cannot be read off this skeleton, so nothing below means anything");
            int fwd = Dominant(front, back, "forward (front feet ahead of back feet)", out float fwdSign);

            float lowest = float.MaxValue, highest = float.MinValue;
            foreach (ObjVector3 v in model.Positions)
            {
                float a = Component(v, up) * upSign;
                if (a < lowest) lowest = a;
                if (a > highest) highest = a;
            }
            float[] gLo = { float.MaxValue, float.MaxValue, float.MaxValue };
            float[] gHi = { float.MinValue, float.MinValue, float.MinValue };
            foreach (ObjVector3 v in model.Positions)
                for (int a = 0; a < 3; a++)
                {
                    float c = Component(v, a);
                    if (c < gLo[a]) gLo[a] = c;
                    if (c > gHi[a]) gHi[a] = c;
                }
            float ground = 0f;
            for (int a = 0; a < 3; a++) if (a != up) ground = Math.Max(ground, gHi[a] - gLo[a]);

            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "MEASURED up = {0}{1}, forward = {2}{3}; along up the geometry spans {4:F4} .. {5:F4} " +
                "({6} vertices, {7} bones)",
                upSign < 0 ? "-" : "+", Axis[up], fwdSign < 0 ? "-" : "+", Axis[fwd],
                lowest, highest, model.Positions.Length, skin.BoneNames.Length));

            // ---- what the MOD applies ----------------------------------------------------------
            string text = File.ReadAllText(source);
            float[] declared = Vector(text, @"ModelUp\s*=\s*new Vector3\(\s*(-?[\d.]+)f\s*,\s*(-?[\d.]+)f\s*,\s*(-?[\d.]+)f\s*\)",
                                      "ModelUp");
            float lift = Scalar(text, @"RigLift\s*=\s*(-?[\d.]+)f", "RigLift");
            float scale = Scalar(text, @"RigScale\s*=\s*(-?[\d.]+)f", "RigScale");
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "DECLARED ModelUp = ({0}, {1}, {2}), RigLift = {3}, RigScale = {4}",
                declared[0], declared[1], declared[2], lift, scale));

            // ---- the arms ----------------------------------------------------------------------
            // 0. THE ONE NUMBER THAT NOW LIVES IN TWO FILES. The mod scales the rig root at runtime;
            //    the BAKE has to know the same factor, because the root-motion ramp is the one curve
            //    the engine reads in the game's units and not the file's (AnimationInfos.cs:105/108
            //    measures the offset in the animator object's LOCAL space and :123 hands it to
            //    TacticalNavigationComponent.cs:376 as world units per second, over a map whose tile
            //    is 1 unit - TacticalMap.cs:67). If the two drift apart the spider crawls or teleports
            //    and nothing else goes red, so they are compared here rather than trusted.
            string metaPath = Path.Combine(demo, "ppcontent.json");
            float declaredScale = Scalar(File.ReadAllText(metaPath), "\"scale\"\\s*:\\s*(-?[\\d.]+)",
                                         "ppcontent.json \"scale\"");
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "DECLARED scale: mod RigScale = {0}, bake ppcontent.json \"scale\" = {1}",
                scale, declaredScale));
            Assert(Math.Abs(declaredScale - scale) <= Math.Abs(scale) * 1e-4f,
                "the mod scales the rig by " + scale.ToString("0.######", CultureInfo.InvariantCulture) +
                " but ppcontent.json tells the bake " +
                declaredScale.ToString("0.######", CultureInfo.InvariantCulture) +
                " - the baked ramp would be written in a space the rig is not in, and the engine " +
                "would measure a movement speed off by exactly their ratio");

            // 1. The axis the mod hands FromToRotation must BE the measured up axis, sign and all.
            //    A wrong entry here is the whole "reared up like a man" bug, and it goes red.
            for (int a = 0; a < 3; a++)
            {
                float want = a == up ? upSign : 0f;
                Assert(Math.Abs(declared[a] - want) < 0.001f, "ModelUp." + Axis[a] +
                    " is " + declared[a].ToString("F3", CultureInfo.InvariantCulture) + ", measured " +
                    want.ToString("F3", CultureInfo.InvariantCulture) +
                    " - FromToRotation(ModelUp, up) would not carry the model's own up axis onto +Y");
            }

            // 2. The lift must seat the LOWEST vertex on the root's own plane. The origin of this
            //    model is its geometric centre, so a rotation-only correction leaves it in a hole
            //    exactly this deep - which is the "sunk into the asphalt" half of the same bug.
            Assert(Math.Abs(lift + lowest) < 0.005f, "RigLift is " +
                lift.ToString("F4", CultureInfo.InvariantCulture) + " but the lowest vertex sits " +
                (-lowest).ToString("F4", CultureInfo.InvariantCulture) +
                " below the origin along the measured up axis - the creature would " +
                (lift + lowest < 0 ? "sink " : "float ") +
                Math.Abs((lift + lowest) * scale).ToString("F4", CultureInfo.InvariantCulture) +
                " units after RigScale");

            // 3. Standing it up must not stand it up BACKWARDS. The minimal arc that carries the
            //    measured up axis onto +Y leaves the third axis alone and carries the remaining one
            //    onto +/-Z; assert it is the model's FRONT that lands on Unity's forward.
            int third = 3 - up - fwd;
            Assert(fwd != up, "the forward axis measured the same as the up axis - the bone names " +
                              "no longer separate the two, so nothing here means anything");
            // FromToRotation cannot pick a minimal arc between antipodal directions, so a model that
            // imports UPSIDE DOWN needs a rotation this correction cannot express - refuse to bless it.
            Assert(!(up == 1 && upSign < 0f), "the model imports upside down (-Y up); " +
                "FromToRotation(-Y, +Y) is an ambiguous half turn, so ModelUp cannot express the fix");
            Assert(Math.Abs(Rotated(fwdSign, fwd, up, upSign) - 1f) < 0.001f,
                "the model's front does not come out along Unity +Z once its up axis is carried onto " +
                "+Y - the creature would stand up facing backwards (up " + Axis[up] + ", forward " +
                Axis[fwd] + ", spare " + Axis[third] + ")");

            // 4. THE CORRECTION IS NOT OPTIONAL, SO EVERY PATH THAT SHOWS THE RIG MUST APPLY IT.
            //    Since 26eca4e the .glb's above-the-skin node - scale 100 - is baked into the
            //    vertices, so an UNCORRECTED rig spans ~200 units against a 1-unit tile. The roster
            //    preview never calls TacticalActor.OnEnterPlay, so its only chance to be oriented is
            //    the TacActorAnimActions.Setup postfix - and that postfix must pick the rig wearing
            //    OUR mesh: UseAddonManager:153-158 destroys the outgoing rig with Unity's deferred
            //    Destroy, so a plain GetComponentInChildren<Animator> returns the still-live human
            //    corpse, Orient refuses it, and the spider renders 100x oversized = off screen.
            float span = highest - lowest;
            bool needsCorrection = span * 1f > 4f;      // uncorrected, in tiles
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "uncorrected the model spans {0:F2} tiles along up; corrected {1:F2}; " +
                "GROUND footprint {2:F2} tiles across at RigScale {3}",
                span, span * scale, ground * scale, scale));
            // The player reported "still four tiles" AFTER AgentRadius was measured at 0.200, so the
            // first thing to rule out is the dullest one: a model that is simply two tiles wide. It
            // is not - and this keeps it that way across a model swap, because RigScale is chosen off
            // the UP axis alone and nothing else looks at how far the legs reach.
            Assert(ground * scale <= 1.05f, "at RigScale " +
                scale.ToString("F4", CultureInfo.InvariantCulture) + " the model covers " +
                (ground * scale).ToString("F2", CultureInfo.InvariantCulture) +
                " tiles on the ground - it overhangs its own tile no matter what the navigation " +
                "agent says, so the footprint the player sees is the MESH and RigScale is the knob");
            Assert(needsCorrection == Regex.IsMatch(text,
                       @"Check\(\s*animatorOverrides\s*,\s*CustomCreatureMain\.OurAnimator\("),
                needsCorrection
                    ? "the Setup postfix does not select the rig by OUR mesh (OurAnimator), yet an " +
                      "uncorrected rig spans " + span.ToString("F2", CultureInfo.InvariantCulture) +
                      " tiles - the roster preview, which has no other seam to orient it, shows nothing"
                    : "the model no longer needs a scale correction, so the OurAnimator selection " +
                      "in the Setup postfix is measuring nothing - re-derive this arm");
            Assert(!Regex.IsMatch(text, @"Check\(\s*animatorOverrides\s*,\s*__instance\.GetComponent"),
                "the Setup postfix hands Check the FIRST Animator under the character builder; on " +
                "the roster path that is the outgoing rig Unity has not destroyed yet");

            // 5. CAN THE UNIT MOVE AT ALL, AND CAN THE WALK EVER END?
            //    Nothing in Phoenix Point moves an actor by root motion. TacticalNavigationComponent
            //    drives the transform itself, at a speed it MEASURES off the playing clip:
            //      :769  CurrentSpeed = GetCurrentAnimSpeed()          -> AnimInfo.Speed
            //      :376  num2 += movementSpeed * Timing.Delta          -> the only advance there is
            //      :718  while (segment >= numPoints - 1) break        -> the only way a move ends
            //    and AnimationInfos.cs:104-123 computes that Speed as |RootMotionNode(length) -
            //    RootMotionNode(0)| / length. So a walk clip that never translates the node named by
            //    RootMotionNodeName measures ZERO, the segment index never grows, and the walk plays
            //    forever with the body standing still - which is exactly what the game did.
            //
            //    THE LOOKUP IS THE GAME'S, NOT A LENIENT COPY OF IT. AddonsManager.cs:116-120 finds
            //    the node BY THE DECLARED NAME, and AnimationInfos.GetMotionPoint reads that one
            //    transform - so this measures the SAME node, matched by the SAME name out of the
            //    source, and refuses to conclude anything if the file has no such bone.
            string motionNode = Text(text, @"RootMotionNodeName\s*=\s*""([^""]+)""", "RootMotionNodeName");
            string walkName = Text(text, @"ClipWalk\s*=\s*""([^""]+)""", "ClipWalk");
            Assert(Array.IndexOf(skin.BoneNames, motionNode) >= 0, "the rig has no bone called '" +
                motionNode + "', so AddonsManager.cs:120 finds no RootMotionNode and " +
                "AnimationInfos.GetMotionPoint logs 'No motion point supplied' - every clip would " +
                "measure a zero offset no matter what it contains");
            float travel = RootTravel(clips, walkName, motionNode, out int found);
            Assert(found == 1, found + " clip(s) in the file are called '" + walkName +
                "' - the nav clip cannot be identified, so nothing below means anything");
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "the walk clip translates '{0}' by {1:F6} over its length ({2} clip(s) in the file)",
                motionNode, travel, clips.Count));

            //    THE CONTROL, because a zero is the answer this arm expects and an arm whose metric
            //    can ONLY return zero would report that zero forever. A LOOPING clip returns every
            //    bone it drives to its starting value, so first-versus-last is zero for all of them -
            //    on THIS file the only non-zero reading in the set is Body over Spider_Death
            //    (0.017537), and if even that goes away the reading below is measuring nothing.
            float anywhere = 0f;
            foreach (SampledClip c in clips)
                foreach (SampledTrack t in c.Tracks)
                {
                    float d = Travel(t);
                    if (d > anywhere) anywhere = d;
                }
            Assert(anywhere > 1e-4f, "not one node in any of the file's " + clips.Count +
                " clip(s) translates between its first and last sample, so the travel reading can " +
                "only ever come back zero and the arm below measures nothing");

            //    THE TRAVEL NOW COMES FROM THE BAKE, NOT FROM THE MOD. ContentTool's Treadmill derives
            //    the locomotion from the clip's own legs (the ground slid under whatever is on the
            //    floor, over the time it was down) and writes it into the root motion node's curve, so
            //    the engine measures a real number off a real clip. The mod must therefore declare NO
            //    speed of its own and patch NOTHING: a hand-fed constant would override a measurement
            //    that matches the legs, which is exactly the mismatch it used to produce on screen.
            SampledClip walkClip = null;
            foreach (SampledClip c in clips)
                if (string.Equals(c.Name, walkName, StringComparison.OrdinalIgnoreCase)) walkClip = c;
            Treadmill.Locomotion loco = Treadmill.Derive(walkClip, skin);
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "BAKED root motion: {0} -> {1:F4} tile(s)/s at RigScale {2} ({3})",
                loco.Any ? "yes" : "NO", loco.Speed * scale, scale, loco.Why));
            bool needsSpeed = travel <= 1e-4f && !loco.Any;
            float declaredNav = Optional(text, @"NavTravelSpeed\s*=\s*(-?[\d.]+)f");
            bool patched = Regex.IsMatch(text,
                @"HarmonyPatch\(typeof\(AnimationInfos\),\s*nameof\(AnimationInfos\.GetAnimInfo\)\)");
            Assert(needsSpeed == (declaredNav > 0f && patched), needsSpeed
                ? "neither the walk clip nor the BAKE moves '" + motionNode +
                  "', so the engine measures Speed = 0 and the actor stands still: either the file " +
                  "must travel, or Treadmill must derive it, or the mod must declare a positive " +
                  "NavTravelSpeed (it is " +
                  declaredNav.ToString("F2", CultureInfo.InvariantCulture) + ") and postfix " +
                  "AnimationInfos.GetAnimInfo (it does" + (patched ? "" : " NOT") + ")"
                : "the clip travels on its own now (" +
                  (loco.Any ? "the bake derives " + loco.Speed.ToString("F2", CultureInfo.InvariantCulture) +
                              " unit/s from its legs" : "the file carries " +
                              travel.ToString("F6", CultureInfo.InvariantCulture)) +
                  "), so the engine measures a real speed and the mod's NavTravelSpeed compensation " +
                  "is overriding a good measurement - delete it and its AnimationInfos postfix");

            // 6. AND CAN IT MOVE MORE THAN ONE TILE? ActionPoints.Max is Mathf.Max(1.3f, Speed)
            //    (CharacterStats.cs:301-302) and MaxMoveRange is that times DistanceToAPFactor
            //    (TacticalActor.cs:183). A unit whose base Speed is 0 - which is what emptying
            //    Data.BodypartItems left behind - gets the 1.3 floor and therefore ONE tile.
            float speed = Optional(text, @"SpiderSpeed\s*=\s*(-?[\d.]+)");
            bool assigned = Regex.IsMatch(text, @"Data\.Speed\s*=\s*SpiderSpeed\s*;");
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "DECLARED SpiderSpeed = {0} tile(s)/turn, assigned to Data.Speed: {1}", speed, assigned));
            Assert(speed > 1.3f && assigned, assigned
                ? "the unit's base Speed is " + speed.ToString("F2", CultureInfo.InvariantCulture) +
                  ", at or below the 1.3 floor in CharacterStats.cs:301, so ActionPoints.Max stays " +
                  "1.3 and the unit gets one tile of movement per turn"
                : "SpiderSpeed is declared (" + speed.ToString("F2", CultureInfo.InvariantCulture) +
                  ") but never assigned to Data.Speed, so nothing reaches " +
                  "TacCharacterData.cs:127 and the unit's base Speed is still 0 - one tile per turn");

            // 7. HOW MANY TILES DOES IT STAND ON? Not the mesh bounds, not a collider, not anything on
            //    the ActorDef - ONE string on the navigation component def:
            //      NavMeshNavigationComponentDef.cs:15     public string AgentType;
            //      NavMeshNavigationComponentDef.cs:38-45  GetAgentSettings -> GetNavSettings(AgentType)
            //    and the radius it resolves to is the only size the tile graph knows
            //    (TacTileGraphBuilder.cs:184-200, CellGraphBuilder.cs:665-687); `AgentRadius > 0.75f`
            //    is the engine's own big-unit test (TacticalActor.cs:1813). Inheriting the donor's put
            //    the spider on a Mutog's 2x2. The name itself lives in the level's NavMesh settings,
            //    so it must be COPIED off a shipped one-tile unit - a literal here would resolve to
            //    nothing and fail silently, which is why the third clause is a ban.
            bool navCloned = Regex.IsMatch(text, @"navClone\.AgentType\s*=\s*infantryAgent\s*;");
            bool navInstalled = Regex.IsMatch(text, @"c\s*==\s*donorNav\)\s*\?\s*\(ObjectDef\)navClone");
            bool literalAgent = Regex.IsMatch(text, @"AgentType\s*=\s*""");
            Assert(navCloned && navInstalled && !literalAgent,
                !navCloned ? "nothing assigns a one-tile AgentType onto a cloned navigation def, so the " +
                             "unit keeps the donor's agent and therefore the donor's tile footprint"
                : !navInstalled ? "the navigation def is cloned and re-pointed but never swapped into " +
                             "the component set, so the actor is still built from the donor's - the " +
                             "clone is dead weight and the footprint is unchanged"
                : "AgentType is assigned a string literal; agent names come from the level's NavMesh " +
                  "settings, so a typo resolves to no settings at all and fails silently");

            // 8. THE SLOT RULE, AND WHY IT IS NOT A TABLE. Navigation BLOCKS on an animation:
            //    TacticalNavigationComponent.cs:723-737 waits for the animator's current clip to become
            //    PathPointInfo.Animation - the clip in the active nav action's slot - and gives up
            //    after five seconds (:181). So a def slot must hold exactly what our controller remap
            //    puts in that state, i.e. ForVanillaClip(the donor's own clip for that slot); and a
            //    slot the donor left EMPTY must stay empty, because filling it is how the engine is
            //    told the creature can do something (PathProcessorUtils.cs:306-328 ->
            //    TacticalPathProcessor.cs:196 emits TurnInPlace points) that the donor's controller has
            //    no state for. That is measured, not theorised: Player.log 2026-08-24 has
            //    "waiting for animation spider_spider_walk timed out. Current animation:
            //     spider_spider_idle" over a path whose first three points are TurnInPlace 1/3/4.
            Assert(Regex.IsMatch(text, @"if\s*\(donorClip\s*==\s*null\)[^\n]*continue;"),
                "WireClips does not skip a slot the donor left empty - filling one claims a capability " +
                "the donor's controller cannot play, and navigation then waits five seconds for an " +
                "animation that never comes and never leaves segment 0");
            Assert(Regex.IsMatch(text, @"SlotClip\(\s*donorClip\s*,\s*clips\s*\)") &&
                   !Regex.IsMatch(text, @"private static string Fallback\("),
                "the slot's clip is not derived from the donor's own clip through the same mapping " +
                "RemapController applies to the controller - a hand-kept table can disagree with it, " +
                "and every disagreement is a five-second stall on a clip that cannot play");

            //    AND THE MAPPING MUST BE TOTAL OVER THIS FILE. ForVanillaClip returns a clip NAME; if
            //    one of them matches nothing in the .glb, SlotClip returns null, the slot keeps the
            //    DONOR's clip, and that slot deadlocks exactly as above. Checked against the real file,
            //    with the same case-insensitive comparison Find/RootTravel use - not against a list.
            int from = text.IndexOf("internal static string ForVanillaClip", StringComparison.Ordinal);
            int to = text.IndexOf("internal static string[] Tokens", StringComparison.Ordinal);
            Assert(from > 0 && to > from, "ForVanillaClip is no longer where this check reads it, so " +
                                          "the arm below is measuring nothing");
            string mapping = text.Substring(from, to - from);
            var returned = new SortedSet<string>();
            foreach (Match m in Regex.Matches(mapping, @"return\s+(Clip\w+)\s*;")) returned.Add(m.Groups[1].Value);
            Assert(returned.Count >= 2, "ForVanillaClip returns fewer than two distinct clip constants " +
                                        "- it no longer maps anything and the arm below is vacuous");
            foreach (string constant in returned)
            {
                string clipName = Text(text, constant + @"\s*=\s*""([^""]+)""", constant);
                int n = 0;
                foreach (SampledClip c in clips)
                    if (string.Equals(c.Name, clipName, StringComparison.OrdinalIgnoreCase)) n++;
                Assert(n == 1, "ForVanillaClip can return " + constant + " = '" + clipName + "', and the " +
                    "model file holds " + n + " clip(s) by that name - Find returns " +
                    (n == 0 ? "null, so every slot it maps keeps the DONOR's clip and stalls navigation"
                            : "an arbitrary one of them"));
            }

            // 9. AND THE HALF OF THE FOOTPRINT THE PLAYER ACTUALLY SEES. AgentRadius is what the
            //    world believes; the screen draws GroundMarkerType values read straight off defs with
            //    no size input (SceneViewElement.cs:169-186, TacticalActorViewBaseDef.cs:23-25), and
            //    the enum ships big-unit variants the donor uses - the live log names the ability:
            //    "Ability Move3x3_AbilityDef of type MoveAbility activated by Mutog_10". So both the
            //    selection cursors and the move ability's scene-view def must be COPIED off a shipped
            //    one-tile unit, and swapped in; a named enum literal is a marker prefab picked blind.
            Assert(Regex.IsMatch(text, @"viewClone\.FriendlySelectionUICursor\s*=\s*refView\.") &&
                   Regex.IsMatch(text, @"viewClone\.HoverUICursor\s*=\s*refView\."),
                "the unit keeps the donor's selection/hover cursors, so it goes on drawing a big-unit " +
                "patch under itself however small its navigation agent is");
            Assert(Regex.IsMatch(text, @"c\s*==\s*donorView\)\s*\?\s*\(ObjectDef\)viewClone"),
                "the view def is cloned and re-cursored but never swapped into the component set - " +
                "the actor is still built from the donor's and the clone is dead weight");
            Assert(Regex.IsMatch(text, @"moveClone\.SceneViewElementDef\s*=\s*refMove\.SceneViewElementDef") &&
                   Regex.IsMatch(text, @"a\s*==\s*donorMove\s*\?\s*\(Base\.Entities\.Abilities\.AbilityDef\)moveClone"),
                "the move ability still draws the donor's reachable-tile markers (Move3x3Highlight), " +
                "or the clone that fixes them never replaces the donor's ability on the actor");
            Assert(!Regex.IsMatch(text, @"GroundMarkerType\s*\.\s*[A-Z]"),
                "a GroundMarkerType is named in code; these are references to shipped marker prefabs, " +
                "so the value must be copied off a one-tile unit rather than chosen by name");

            // 10. AND THE MOVE MUST BE ABLE TO END. TacticalZoneObjective.cs:60 dereferences
            //     actor.GetAimPoint() for essentially every tile step, TacticalActor.cs:1475-1478
            //     resolves that through an item slot, and ItemSlot.cs:180-185 returns NULL when the
            //     slot has no attached addons - which this unit has none of by construction. The NRE
            //     kills TacticalNavigationComponent's ExecutePoints coroutine, PlayingAction never
            //     completes, and no later order can start. The remedy must be the base class's own
            //     answer (TacticalActorBase.cs:754-756 returns base.transform), applied only where the
            //     override came back null, or it changes aiming for shipped actors too.
            Assert(Regex.IsMatch(text, @"nameof\(TacticalActor\.GetAimPoint\)"),
                "nothing supplies an aim point; the unit has no bodypart to carry one, so the first " +
                "move throws inside TacticalZoneObjective and the mover's coroutine dies mid-path");
            Assert(Regex.IsMatch(text, @"if\s*\(__result\s*!=\s*null\)\s*return;") &&
                   Regex.IsMatch(text, @"__result\s*=\s*__instance\.transform;"),
                "the aim-point postfix either overwrites a real aim point or answers with something " +
                "other than the base class's own base.transform - both change shipped actors' aiming");

            Console.WriteLine(failures == 0
                ? "C1-axis PASS - the constants match the file"
                : "C1-axis FAIL - " + failures + " arm(s) red");
            return failures == 0 ? 0 : 1;
        }

        /// <summary>
        /// How far the named node is translated by the named clip, first sample to last, in the
        /// model's own space - the same difference AnimationInfos.cs:104-110 takes off the live
        /// transform. A node the clip does not drive at all travels zero, which is the answer that
        /// matters here.
        /// </summary>
        private static float RootTravel(List<SampledClip> clips, string clipName, string node, out int found)
        {
            found = 0;
            float most = 0f;
            foreach (SampledClip c in clips)
            {
                if (!string.Equals(c.Name, clipName, StringComparison.OrdinalIgnoreCase)) continue;
                found++;
                foreach (SampledTrack t in c.Tracks)
                {
                    if (t.Translations == null || t.Translations.Length < 2) continue;
                    if (!string.Equals(c.Nodes[t.Node].Name, node, StringComparison.Ordinal)) continue;
                    float d = Travel(t);
                    if (d > most) most = d;
                }
            }
            return most;
        }

        /// <summary>One track's first-to-last displacement; zero when it drives no translation.</summary>
        private static float Travel(SampledTrack t)
        {
            if (t.Translations == null || t.Translations.Length < 2) return 0f;
            ObjVector3 a = t.Translations[0], b = t.Translations[t.Translations.Length - 1];
            return (float)Math.Sqrt((b.X - a.X) * (b.X - a.X) + (b.Y - a.Y) * (b.Y - a.Y) +
                                    (b.Z - a.Z) * (b.Z - a.Z));
        }

        /// <summary>A constant the source may legitimately not declare; absent reads as zero, which
        /// is what every arm above treats as "not supplied".</summary>
        private static float Optional(string text, string pattern)
        {
            Match m = Regex.Match(text, pattern);
            return m.Success ? Num(m.Groups[1].Value) : 0f;
        }

        private static string Text(string text, string pattern, string what)
        {
            Match m = Regex.Match(text, pattern);
            if (!m.Success) throw new InvalidDataException(what + " is not declared the way this check " +
                "reads it - the constant was renamed or reshaped, so the check is measuring nothing");
            return m.Groups[1].Value;
        }

        /// <summary>
        /// Where the model's <paramref name="fwd"/> axis lands, as a +Z component, after the minimal
        /// rotation that carries axis <paramref name="up"/> onto +Y. That rotation is a quarter turn
        /// in the plane the two axes span, so the arithmetic is one signed permutation and needs no
        /// engine convention: the up axis goes to +Y, and the other in-plane axis goes to the
        /// remaining one of +/-Z such that the pair keeps its orientation.
        /// </summary>
        private static float Rotated(float fwdSign, int fwd, int up, float upSign)
        {
            // The three axes in cyclic order X->Y->Z->X. Carrying +Z onto +Y turns the (Y, Z) plane
            // by -90 degrees, which sends +Y onto -Z; carrying +Y onto +Y is the identity. Since
            // commit 26eca4e honours the .glb node above the skin root, spider.glb measures up=+Y,
            // forward=+Z and takes that identity branch - the Z-up branch is what a model with its
            // axis baked into the vertex data still needs. Written out generally:
            if (up == 1) return fwd == 2 ? fwdSign * upSign : 0f;      // already Y-up: Z stays Z
            if (up == 2) return fwd == 1 ? -fwdSign * upSign : 0f;     // Z-up: +Y -> -Z
            return 0f;                                                 // X-up: forward cannot be Z-ish
        }

        private static float Component(ObjVector3 v, int axis) => axis == 0 ? v.X : axis == 1 ? v.Y : v.Z;

        /// <summary>Mean rest position, in the imported model's own space, of every bone whose name
        /// the predicate accepts. Rest position is the inverse bind pose's translation - the same
        /// derivation ModelBuild.LocalRest uses to build the rig, so this cannot drift from it.</summary>
        private static float[] Mean(BakedSkin skin, Func<string, bool> want)
        {
            var sum = new float[3];
            int n = 0;
            for (int b = 0; b < skin.BoneNames.Length; b++)
            {
                if (!want(skin.BoneNames[b])) continue;
                float[] world = ModelBuild.Invert(skin.BindPoses[b], skin.BoneNames[b]);
                sum[0] += world[12]; sum[1] += world[13]; sum[2] += world[14];
                n++;
            }
            if (n == 0) throw new InvalidDataException("no bone matched - the file's bone names changed");
            for (int a = 0; a < 3; a++) sum[a] /= n;
            return sum;
        }

        /// <summary>The axis on which the bones whose name contains <paramref name="token"/> are
        /// flattest - smallest max-minus-min of their rest positions - plus the runner-up's spread so
        /// the caller can refuse an ambiguous answer.</summary>
        private static int Flattest(BakedSkin skin, string token, out float spread, out float runnerUp)
        {
            var lo = new[] { float.MaxValue, float.MaxValue, float.MaxValue };
            var hi = new[] { float.MinValue, float.MinValue, float.MinValue };
            int n = 0;
            for (int b = 0; b < skin.BoneNames.Length; b++)
            {
                if (skin.BoneNames[b].IndexOf(token, StringComparison.OrdinalIgnoreCase) < 0) continue;
                float[] world = ModelBuild.Invert(skin.BindPoses[b], skin.BoneNames[b]);
                for (int a = 0; a < 3; a++)
                {
                    float v = world[12 + a];
                    if (v < lo[a]) lo[a] = v;
                    if (v > hi[a]) hi[a] = v;
                }
                n++;
            }
            if (n < 3) throw new InvalidDataException("only " + n + " bone(s) are named '" + token +
                "' - a ground plane needs at least three, so the file's bone names changed");
            var span = new float[3];
            for (int a = 0; a < 3; a++) span[a] = hi[a] - lo[a];
            int best = 0;
            for (int a = 1; a < 3; a++) if (span[a] < span[best]) best = a;
            spread = span[best];
            runnerUp = float.MaxValue;
            for (int a = 0; a < 3; a++) if (a != best) runnerUp = Math.Min(runnerUp, span[a]);
            return best;
        }

        /// <summary>The axis on which <paramref name="high"/> most clearly beats <paramref name="low"/>,
        /// refused unless it beats the runner-up 3x - an ambiguous separation is no measurement.</summary>
        private static int Dominant(float[] high, float[] low, string what, out float sign)
        {
            var d = new float[3];
            for (int a = 0; a < 3; a++) d[a] = high[a] - low[a];
            int best = 0;
            for (int a = 1; a < 3; a++) if (Math.Abs(d[a]) > Math.Abs(d[best])) best = a;
            float runnerUp = 0f;
            for (int a = 0; a < 3; a++) if (a != best) runnerUp = Math.Max(runnerUp, Math.Abs(d[a]));
            Assert(Math.Abs(d[best]) > runnerUp * 3f, what + " separates by (" +
                string.Join(", ", Array.ConvertAll(d, v => v.ToString("F4", CultureInfo.InvariantCulture))) +
                ") - no axis dominates, so the file's " + what + " axis cannot be read off the skeleton");
            sign = d[best] < 0f ? -1f : 1f;
            return best;
        }

        private static float[] Vector(string text, string pattern, string what)
        {
            Match m = Regex.Match(text, pattern);
            if (!m.Success) throw new InvalidDataException(what + " is not declared the way this check " +
                "reads it - the constant was renamed or reshaped, so the check is measuring nothing");
            return new[] { Num(m.Groups[1].Value), Num(m.Groups[2].Value), Num(m.Groups[3].Value) };
        }

        private static float Scalar(string text, string pattern, string what)
        {
            Match m = Regex.Match(text, pattern);
            if (!m.Success) throw new InvalidDataException(what + " is not declared the way this check " +
                "reads it - the constant was renamed or reshaped, so the check is measuring nothing");
            return Num(m.Groups[1].Value);
        }

        private static float Num(string s) => float.Parse(s, CultureInfo.InvariantCulture);

        private static void Assert(bool ok, string complaint)
        {
            if (ok) return;
            failures++;
            Console.WriteLine("  FAIL " + complaint);
        }
    }
}
