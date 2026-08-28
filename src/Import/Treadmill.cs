using System;
using System.Collections.Generic;
using System.Globalization;

namespace Morgott.ContentTool.Import
{
    /// <summary>
    /// THE TREADMILL ADAPTER: what a downloaded clip walks AT, derived from the clip's own legs.
    ///
    /// Phoenix Point never moves a tactical actor by root motion. The navigation component drives the
    /// transform itself, at a speed it MEASURES off the playing clip, once:
    ///   AnimationInfos.cs:99-113   SampleAnimation(0) and SampleAnimation(length) on the
    ///                              RootMotionNode -&gt; Offset, in the actor's own local space
    ///   AnimationInfos.cs:121-123  Speed = |Offset| / clip.length
    ///   TacticalNavigationComponent.cs:451/769  CurrentSpeed = that Speed
    ///   TacticalNavigationComponent.cs:376      num2 += movementSpeed * Timing.Delta
    ///   TacticalNavigationComponent.cs:248      SegAnimChangesPosition = Offset.sqrMagnitude &gt; 1e-5
    ///   TacticalNavigationComponent.cs:718      the ONLY exit: segment &gt;= numPoints - 1
    /// so a clip whose root-motion node never translates measures Speed 0 AND reports "this segment
    /// does not move the actor" - the actor stands still and the walk never ends.
    ///
    /// THE SHIPPED CONVENTION, MEASURED 2026-08-24 rather than remembered -
    /// `_common_assets_all.bundle` 'MV_RunFwd_Loop_AR' (the soldier's own run loop, m_LoopTime=true,
    /// length 0.5333 s, 280 bindings): EXACTLY ONE binding translates between first and last key -
    /// attribute 1 on 'BaseManReference', by 2.894980, the node every bone hangs under. Every other
    /// binding in that clip is a rotation and every one of them returns to where it started, because
    /// a looping clip closes its cycle. So Speed = 2.89498 / 0.5333 = 5.43 unit/s, and the shape a
    /// clip needs is: the ONE top node ramps, the legs cycle under it.
    ///
    /// A clip downloaded from a model site carries the legs and not the ramp - it walks on the spot.
    /// This derives the ramp from the legs, so the two match BY CONSTRUCTION instead of by a tuned
    /// constant: while a foot is planted on the ground it is stationary in the WORLD, so in a
    /// treadmill clip it slides BACKWARDS through the model's own space at exactly the speed the
    /// creature travels forwards. Measure that slide, negate it, and you have the locomotion.
    ///
    /// MEASURED on this file rather than assumed: 'Spider_Walk' bone 'MidFrontFoot.R' holds y = -31.99
    /// for frames 0..10 while its z runs 57.41 -&gt; 5.91, then lifts to y = -23.4 and swings back. It
    /// really is a treadmill - the foot is DOWN and the ground slides 51.5 under it.
    ///
    /// WHY THIS METRIC AND NOT FIRST-VERSUS-LAST. A looping clip returns every bone it drives to its
    /// starting pose, so first-versus-last is ZERO for every bone of a walk cycle and an arm built on
    /// it is structurally dead. This reads the ground slid over each CONTACT STEP and divides by the
    /// time those steps cover - a quantity a closed cycle does not cancel.
    ///
    /// AND IT NO-OPS ON A CLIP THAT ALREADY TRAVELS, with no second code path: if the file's own root
    /// track carries the travel, the forward kinematics below already include it, so the planted foot
    /// is stationary IN MODEL SPACE too, the measured slide is ~0, and nothing is written. One rule,
    /// both cases (proven both ways in tests\ObjCodecTests\RootMotionBake.cs).
    ///
    /// ponytail: a division, not a foot-contact solver. No per-limb phase detection, no ground plane
    /// fit, no gait model - whatever is near the floor is treated as down, every planted bone reads
    /// the same speed, and the average over all of them is that speed.
    /// <see cref="ContactBand"/> is the calibration knob a real rig may need: 0.02 is tight because
    /// this spider's feet lift only 12% of its own height, and a band wide enough to swallow the whole
    /// swing makes every reading cancel to zero (measured: exactly 0/s at 0.15).
    /// </summary>
    internal static class Treadmill
    {
        /// <summary>How far up from the rig's lowest point still counts as touching the ground, as a
        /// fraction of the rig's own vertical extent over the clip. One number: the contact set and
        /// the height it is measured against cannot drift apart.</summary>
        internal const float ContactBand = 0.02f;

        /// <summary>Below this much travel over the whole clip - as a fraction of the rig's height -
        /// the clip is not a locomotion cycle (an idle, an attack) or it already carries its own root
        /// motion, and nothing is written either way.</summary>
        internal const float MinTravelOfHeight = 0.25f;

        /// <summary>
        /// THE PACE EVERY SHIPPED UNIT TRAVELS AT - MEASURED, not chosen, and the engine's fallback when
        /// a mod declares no <c>"pace"</c>.
        ///
        /// `_common_assets_all.bundle` 'MV_RunFwd_Loop_AR', the soldier's OWN locomotion loop, ramps its
        /// 'BaseManReference' node by 2.894980 over a 0.533300 s cycle (the measurement this class's
        /// remark records in full). The engine spends that as world units per second - AnimationInfos
        /// .cs:123 Speed = |Offset| / clip.length, TacticalNavigationComponent.cs:769 CurrentSpeed = it,
        /// :376 num2 += movementSpeed * Timing.Delta - and TacticalMap.cs:67 TileSize = 1f makes a world
        /// unit a tile. So 2.894980 / 0.533300 = 5.4284 tile/s IS the game's standard traversal pace.
        ///
        /// It is the same number for every unit because the game HAS no speed def field to vary it with:
        /// TacticalActorBaseDef carries none, TacticalNavigationComponentDef.cs:12-34 carries none, and
        /// MoveAbilityDef.cs:10-12 is an empty class. Data.Speed is a different quantity entirely -
        /// CharacterStats.cs:301-302 spends it as ActionPoints.Max, i.e. how FAR a unit gets per turn,
        /// never how fast it crosses a tile. That is why <see cref="Retime"/> exists and why "pace"
        /// could not be folded into the manifest's "speed": one is tiles per turn, the other tiles per
        /// second, and a 6-AP unit and a 16-AP unit walk at this identical pace.
        /// </summary>
        internal const float ShippedPace = 5.4284f;

        /// <summary>A retime this close to 1 is not worth reopening the clip's timeline for, and it is
        /// what makes <see cref="Retime"/> close on itself: a clip already at pace re-derives to the
        /// pace, so a second bake measures a factor of 1 and changes nothing.</summary>
        private const float PaceTolerance = 0.005f;

        /// <summary>
        /// PLAY A LOCOMOTION CLIP AT WHATEVER RATE MAKES THE GAME MEASURE <paramref name="target"/>
        /// OFF IT - the one lever this game gives a mod, and the one the defect "the creature crawls"
        /// actually has.
        ///
        /// The game reads a unit's metres per second off the CLIP and nowhere else (see
        /// <see cref="ShippedPace"/> for the four file:line that make that a fact rather than a belief),
        /// so a downloaded walk cycle travels at whatever pace its author happened to animate. This
        /// spider's own measured 101.9315 rig-units/s over a 0.833333 s cycle is 0.509657 tile/s at its
        /// declared scale - one tenth of the shipped 5.4284, which is exactly what "it crawls" looks
        /// like from the player's chair.
        ///
        /// THE FIX IS THE CLIP'S TIMELINE AND NOT ITS TRAVEL, because those are the two ways to raise a
        /// speed and only one of them keeps the feet on the ground:
        ///   - stretch the ramp  -> the body covers more ground while the legs cycle at the old cadence,
        ///                          which IS foot sliding, and it is the defect this class exists to
        ///                          prevent (the whole derivation above is one long argument that travel
        ///                          and gait must be a single measurement).
        ///   - compress the time -> travel per cycle unchanged, legs and ground both k times faster.
        ///                          Speed = travel / duration rises by exactly k and the planted foot is
        ///                          still planted, because a UNIFORM retime cannot desynchronise two
        ///                          things that were in step - it is the same clip on a faster projector.
        /// So this scales the sample times and the sample rate together and touches no curve at all. The
        /// ramp is DERIVED downstream from the retimed clip (ClipFields.Ramp), so it comes out at the
        /// target with no second calculation to keep in step.
        ///
        /// IDEMPOTENT BY CONSTRUCTION, which matters because the bake samples the same SampledClip object
        /// more than once (ProjectBake.ImportedClips and again in the ClipWrote oracle): a clip already
        /// travelling at the target derives the target, so k comes back 1 and nothing is touched.
        ///
        /// ponytail: uniform retime, not a per-phase one. A real gait spends longer in stance than in
        /// swing and a stylist would compress those differently; that needs a gait model this does not
        /// have, and the whole clip playing k times faster is what the shipped units do anyway.
        /// </summary>
        /// <param name="target">tiles per second the game should measure. 0 leaves the clip alone.</param>
        /// <param name="why">the line the bake log prints - always set, retimed or not.</param>
        /// <returns>the factor applied; 1 when nothing changed.</returns>
        internal static float Retime(SampledClip clip, BakedSkin skin, float worldScale, float target,
                                     out string why)
        {
            why = "";
            if (!(worldScale > 0f)) { why = "pace: no scale to measure in"; return 1f; }
            if (target <= 0f)
            { why = "pace: \"pace\": 0 - the clip keeps its own authored speed"; return 1f; }

            Locomotion loco = Derive(clip, skin);
            if (!loco.Any) { why = "pace: not a locomotion cycle, left alone (" + loco.Why + ")"; return 1f; }
            float now = loco.Speed * worldScale;
            if (!(now > 0f)) { why = "pace: the clip measures 0 tile/s, nothing to retime"; return 1f; }

            float k = target / now;
            int frames = clip.Times.Length;
            float was = clip.Times[frames - 1] - clip.Times[0];
            if (Math.Abs(k - 1f) < PaceTolerance)
                return One(out why, "pace: already " + F(now) + " tile/s, within " +
                                    F(PaceTolerance * 100f) + "% of the " + F(target) + " asked for");

            for (int f = 0; f < frames; f++) clip.Times[f] /= k;
            clip.SampleRate *= k;
            clip.Length /= k;

            // The cycle rate is in the sentence on purpose: it is the number an author judges the
            // result by. A creature whose stride is a fraction of a tile HAS to scurry to hold the
            // game's pace, and seeing "12.8 cycle(s)/s" is how they find out before the player does -
            // "pace" is the knob that answers it.
            float now2 = was / k;
            why = "pace: " + F(now) + " -> " + F(target) + " tile/s, so the clip plays x" + F(k) +
                  " (" + F(was) + " s -> " + F(now2) + " s per cycle = " +
                  F(now2 > 0f ? 1f / now2 : 0f) + " cycle(s)/s, sample rate " + F(clip.SampleRate) +
                  " Hz). The legs and the ground speed up together, so nothing slides.";
            return k;
        }

        private static float One(out string why, string text) { why = text; return 1f; }

        internal struct Locomotion
        {
            /// <summary>true when a ramp should be written.</summary>
            internal bool Any;
            /// <summary>Units per second, in the ROOT BONE'S PARENT space - the same space the root's
            /// own localPosition is written in, which is why any uniform rig scale the game applies
            /// later scales the travel and the legs by the same factor.</summary>
            internal ObjVector3 Velocity;
            internal float Speed;
            /// <summary>The rig's own vertical extent over the clip - the scale every threshold here is
            /// relative to, so nothing assumes a file's units. 0 when nothing was measured.</summary>
            internal float Height;
            /// <summary>One line, for the bake log - always set, whether or not <see cref="Any"/>.</summary>
            internal string Why;
        }

        /// <summary>
        /// The armature root - the one bone no other bone carries. The same node
        /// <c>GlbReader</c>:804 refuses a FILE's own channels on, and the one
        /// <c>AnimationInfos.GetMotionPoint</c> falls back to by name ("...ROOT"/"...Reference").
        /// -1 when the rig has none or several, which this refuses to guess between.
        /// </summary>
        internal static int RootBone(BakedSkin skin)
        {
            if (skin == null || skin.BoneParents == null) return -1;
            int found = -1;
            for (int b = 0; b < skin.BoneParents.Length; b++)
                if (skin.BoneParents[b] < 0)
                {
                    if (found >= 0) return -1;
                    found = b;
                }
            return found;
        }

        internal static Locomotion Derive(SampledClip clip, BakedSkin skin)
        {
            Locomotion no = new Locomotion { Any = false, Why = "" };
            if (clip == null || skin == null || !skin.Rigged) { no.Why = "no rig"; return no; }
            int bones = skin.BoneNames.Length;
            int frames = clip.Times == null ? 0 : clip.Times.Length;
            if (frames < 2 || skin.BoneRest == null || skin.BoneRest.Length != bones)
            { no.Why = "too few frames or no rest pose"; return no; }
            if (RootBone(skin) < 0) { no.Why = "the rig has no single root bone"; return no; }

            float[][] pos = Positions(clip, skin);
            if (pos == null) { no.Why = "the rig's bone tree does not resolve"; return no; }

            // A bone NO channel of this clip can reach - itself undriven, under an undriven chain -
            // cannot be a foot: it is rigging furniture, and on this fixture the armature root itself
            // sits at the floor and never moves, which pinned the reading below at exactly 0/s until
            // this existed. Structural, not a value test: a clip that already carries its root motion
            // drives its root, so every bone under it stays IN and its planted feet correctly read ~0.
            bool[] movable = Movable(clip, skin);
            // And never the root motion node itself. It sits at the floor, so it is always "on the
            // ground", and once a ramp is written it slides at exactly the speed that was derived -
            // it would be evidence for itself, and every re-bake would derive the same speed again on
            // a clip that now travels perfectly.
            movable[RootBone(skin)] = false;

            float low = float.MaxValue, high = float.MinValue;
            for (int f = 0; f < frames; f++)
                for (int b = 0; b < bones; b++)
                {
                    if (!movable[b]) continue;
                    float y = pos[f][b * 3 + 1];
                    if (y < low) low = y;
                    if (y > high) high = y;
                }
            float height = high - low;
            if (!(height > 0f)) { no.Why = "the rig has no vertical extent"; return no; }
            float band = low + ContactBand * height;

            // ONE PASS over every ground-contact step of every bone, twice - first for the AXIS the
            // ground slides along, then for how far it slides. Two passes over the same set rather
            // than a guessed forward vector: an imported file has no convention about which way its
            // creature faces.
            double mxx = 0, mxz = 0, mzz = 0;
            int steps = 0;
            for (int f = 0; f + 1 < frames; f++)
                for (int b = 0; b < bones; b++)
                {
                    if (!Contact(pos, movable, f, b, band)) continue;
                    double ddx = pos[f + 1][b * 3] - pos[f][b * 3];
                    double ddz = pos[f + 1][b * 3 + 2] - pos[f][b * 3 + 2];
                    mxx += ddx * ddx; mxz += ddx * ddz; mzz += ddz * ddz;
                    steps++;
                }
            if (steps == 0) { no.Why = "no bone of the rig touches the ground"; return no; }
            float ex, ez;
            if (!Principal(mxx, mxz, mzz, out ex, out ez))
            { no.Why = "nothing on the ground moves at all"; return no; }

            // SIGN: over a whole cycle a foot returns to where it started, so its forward return
            // happens in the AIR and what is left inside the contact set is the backward slide. The
            // axis therefore points the way the ground goes; the creature goes the other way.
            double along = 0;
            for (int f = 0; f + 1 < frames; f++)
                for (int b = 0; b < bones; b++)
                    if (Contact(pos, movable, f, b, band))
                        along += (pos[f + 1][b * 3] - pos[f][b * 3]) * ex +
                                 (pos[f + 1][b * 3 + 2] - pos[f][b * 3 + 2]) * ez;
            if (along < 0) { ex = -ex; ez = -ez; }

            // THE SPEED, and it is one division. While a foot is on the ground it is stationary in
            // the WORLD, so the model space it lives in slides under it at exactly the speed the
            // creature travels - therefore total ground slid over every contact sample, divided by
            // the time those samples cover, IS that speed. Every planted bone reads the same number,
            // so no foot has to be picked out and no gait has to be recognised.
            // SIGNED, which is what makes it close on itself: re-derive a clip that already carries
            // this ramp and its planted feet no longer slide at all, so the answer is ~0 and nothing
            // is written a second time.
            double slid = 0, seconds = 0;
            for (int f = 0; f + 1 < frames; f++)
            {
                float dt = clip.Times[f + 1] - clip.Times[f];
                if (!(dt > 0f)) continue;
                for (int b = 0; b < bones; b++)
                {
                    if (!Contact(pos, movable, f, b, band)) continue;
                    slid += (pos[f + 1][b * 3] - pos[f][b * 3]) * ex +
                            (pos[f + 1][b * 3 + 2] - pos[f][b * 3 + 2]) * ez;
                    seconds += dt;
                }
            }
            float speed = seconds > 0 ? (float)(slid / seconds) : 0f;
            if (speed < 0f) speed = 0f;
            float vx = -ex * speed, vz = -ez * speed;
            float duration = clip.Times[frames - 1] - clip.Times[0];
            float travel = speed * duration;
            string measured = "the ground slides " + F((float)slid) + " under " +
                              steps.ToString(CultureInfo.InvariantCulture) + " contact step(s) worth " +
                              F((float)seconds) + " s = " + F(speed) + "/s, " + F(travel) + " over the " +
                              F(duration) + " s clip, against a rig " + F(height) + " tall";
            if (travel < MinTravelOfHeight * height)
                return new Locomotion { Any = false, Height = height, Why = "no ramp: " + measured +
                    " - the clip either stands still or already carries its own root motion" };
            return new Locomotion
            {
                Any = true,
                Velocity = new ObjVector3(vx, 0f, vz),
                Speed = speed,
                Height = height,
                Why = "ramp " + F(speed) + "/s: " + measured
            };
        }

        /// <summary>Is this bone on the ground across the step from <paramref name="f"/> to f+1?</summary>
        private static bool Contact(float[][] pos, bool[] movable, int f, int b, float band)
        {
            return movable[b] && pos[f][b * 3 + 1] <= band && pos[f + 1][b * 3 + 1] <= band;
        }

        /// <summary>
        /// The dominant axis of a 2x2 second-moment matrix, as a unit vector - the line the ground
        /// slides along. Closed form: the larger eigenvalue, then whichever of the two eigenvector
        /// expressions is better conditioned. false when the matrix is degenerate (nothing moved).
        /// </summary>
        private static bool Principal(double mxx, double mxz, double mzz, out float ex, out float ez)
        {
            ex = 0f; ez = 0f;
            double trace = mxx + mzz;
            if (!(trace > 0)) return false;
            double half = trace * 0.5;
            double disc = half * half - (mxx * mzz - mxz * mxz);
            double lambda = half + Math.Sqrt(disc > 0 ? disc : 0);
            double ax = mxz, az = lambda - mxx;
            double bx = lambda - mzz, bz = mxz;
            if (bx * bx + bz * bz > ax * ax + az * az) { ax = bx; az = bz; }
            double len = Math.Sqrt(ax * ax + az * az);
            // A perfectly axis-aligned slide leaves the off-diagonal at zero and both expressions
            // collapse; the axis is then whichever diagonal carries the motion.
            if (len < 1e-12) { ax = mxx >= mzz ? 1 : 0; az = mxx >= mzz ? 0 : 1; len = 1; }
            ex = (float)(ax / len); ez = (float)(az / len);
            return true;
        }

        /// <summary>Per bone, whether this clip can move it at all: it carries a channel, or something
        /// that carries it does.</summary>
        private static bool[] Movable(SampledClip clip, BakedSkin skin)
        {
            int bones = skin.BoneNames.Length;
            bool[] driven = new bool[bones];
            foreach (SampledTrack t in clip.Tracks)
                if (t.Node >= 0 && t.Node < bones) driven[t.Node] = true;
            bool[] movable = new bool[bones];
            for (int b = 0; b < bones; b++)
                for (int at = b, n = 0; at >= 0 && n <= bones; at = skin.BoneParents[at], n++)
                    if (driven[at]) { movable[b] = true; break; }
            return movable;
        }

        /// <summary>
        /// Every bone's position at every frame, in the ROOT BONE'S PARENT space - the file's own rest
        /// pose with the clip's channels laid over it, walked down the bone tree. A channel the clip
        /// does not carry leaves that component at its rest value, which is exactly what a missing
        /// glTF sampler means. null when the tree does not resolve (a cycle the reader let through).
        /// </summary>
        internal static float[][] Positions(SampledClip clip, BakedSkin skin)
        {
            int bones = skin.BoneNames.Length, frames = clip.Times.Length;
            SampledTrack[] track = new SampledTrack[bones];
            foreach (SampledTrack t in clip.Tracks)
                if (t.Node >= 0 && t.Node < bones) track[t.Node] = t;

            // The rest transform, split into the three parts a glTF channel replaces one at a time.
            float[][] restRot = new float[bones][];
            float[][] restScale = new float[bones][];
            float[][] restPos = new float[bones][];
            for (int b = 0; b < bones; b++)
            {
                float[] m = skin.BoneRest[b];
                if (m == null || m.Length != 16) return null;
                float[] r = new float[9];
                float[] s = new float[3];
                for (int c = 0; c < 3; c++)
                {
                    float x = m[c * 4], y = m[c * 4 + 1], z = m[c * 4 + 2];
                    float len = (float)Math.Sqrt(x * x + y * y + z * z);
                    s[c] = len;
                    float k = len > 1e-12f ? 1f / len : 0f;
                    r[c * 3] = x * k; r[c * 3 + 1] = y * k; r[c * 3 + 2] = z * k;
                }
                restRot[b] = r; restScale[b] = s;
                restPos[b] = new[] { m[12], m[13], m[14] };
            }

            float[][] pos = new float[frames][];
            float[][] world = new float[bones][];
            bool[] done = new bool[bones];
            for (int f = 0; f < frames; f++)
            {
                Array.Clear(done, 0, bones);
                for (int b = 0; b < bones; b++)
                    if (!Resolve(b, f, skin, track, restRot, restScale, restPos, world, done, 0)) return null;
                float[] p = new float[bones * 3];
                for (int b = 0; b < bones; b++)
                {
                    p[b * 3] = world[b][12]; p[b * 3 + 1] = world[b][13]; p[b * 3 + 2] = world[b][14];
                }
                pos[f] = p;
            }
            return pos;
        }

        private static bool Resolve(int b, int f, BakedSkin skin, SampledTrack[] track,
                                    float[][] restRot, float[][] restScale, float[][] restPos,
                                    float[][] world, bool[] done, int depth)
        {
            if (done[b]) return true;
            if (depth > skin.BoneNames.Length) return false;     // a cycle
            int parent = skin.BoneParents[b];
            if (parent >= 0 && !Resolve(parent, f, skin, track, restRot, restScale, restPos, world, done, depth + 1))
                return false;

            SampledTrack t = track[b];
            float[] r = t != null && t.Rotations != null ? Basis(t.Rotations[f]) : restRot[b];
            float[] s = restScale[b];
            if (t != null && t.Scales != null) s = new[] { t.Scales[f].X, t.Scales[f].Y, t.Scales[f].Z };
            float[] p = restPos[b];
            if (t != null && t.Translations != null)
                p = new[] { t.Translations[f].X, t.Translations[f].Y, t.Translations[f].Z };

            float[] local = new float[16];
            for (int c = 0; c < 3; c++)
                for (int row = 0; row < 3; row++) local[c * 4 + row] = r[c * 3 + row] * s[c];
            local[12] = p[0]; local[13] = p[1]; local[14] = p[2]; local[15] = 1f;

            world[b] = parent < 0 ? local : Mul(world[parent], local);
            done[b] = true;
            return true;
        }

        /// <summary>A unit quaternion as a column-major 3x3 basis, nine floats.</summary>
        private static float[] Basis(ObjQuaternion q)
        {
            float x = q.X, y = q.Y, z = q.Z, w = q.W;
            float n = x * x + y * y + z * z + w * w;
            float k = n > 1e-12f ? 2f / n : 0f;
            float xx = x * x * k, yy = y * y * k, zz = z * z * k;
            float xy = x * y * k, xz = x * z * k, yz = y * z * k;
            float wx = w * x * k, wy = w * y * k, wz = w * z * k;
            return new[]
            {
                1f - (yy + zz), xy + wz,        xz - wy,
                xy - wz,        1f - (xx + zz), yz + wx,
                xz + wy,        yz - wx,        1f - (xx + yy)
            };
        }

        /// <summary>a * b, both column-major 4x4.</summary>
        private static float[] Mul(float[] a, float[] b)
        {
            float[] m = new float[16];
            for (int c = 0; c < 4; c++)
                for (int row = 0; row < 4; row++)
                {
                    float v = 0f;
                    for (int k = 0; k < 4; k++) v += a[k * 4 + row] * b[c * 4 + k];
                    m[c * 4 + row] = v;
                }
            return m;
        }

        private static float Median(List<float> v)
        {
            float[] a = v.ToArray();
            Array.Sort(a);
            int n = a.Length;
            return (n % 2 == 1) ? a[n / 2] : 0.5f * (a[n / 2 - 1] + a[n / 2]);
        }

        private static string F(float v)
        {
            return v.ToString("0.######", CultureInfo.InvariantCulture);
        }
    }
}
