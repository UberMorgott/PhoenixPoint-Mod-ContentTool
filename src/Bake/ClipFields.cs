using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using AssetsTools.NET;
using AssetsTools.NET.Extra;
using Morgott.ContentTool.Import;

namespace Morgott.ContentTool.Bake
{
    /// <summary>
    /// U6 - the animation half of a bake: a native <c>AnimationClip</c>, an
    /// <c>AnimatorOverrideController</c> that hands it to Mecanim, and the GameObject/Transform
    /// hierarchy with the <c>Animator</c> that plays it. Free of UnityEngine types like
    /// <see cref="MeshFields"/>/<see cref="PrefabFields"/>/<see cref="SkinFields"/>, so
    /// build -&gt; repack -&gt; read runs offline (tests\ObjCodecTests\ClipRoundTrip.cs).
    ///
    /// EVERYTHING here is MEASURED 2026-08-12 off shipped 2019.4.31f1 bundles
    /// (`aln_fireworm_assets_all.bundle` 'Fireworm_unfurl', `px_equipment_assets_all.bundle`
    /// 'MV_RocketJumpIdle'/'Reload'/'Bind_Pose'/'MV_RocketJumpStartA', `_common_assets_all.bundle`
    /// 'MedKitHeartBeat1' + 'ArmadilloHulkCrateAnimator'), never remembered:
    ///
    ///  - a BUILT clip carries NOTHING in m_RotationCurves/m_PositionCurves/m_ScaleCurves/
    ///    m_FloatCurves - those are editor-only and ship empty. The curves live in
    ///    m_MuscleClip.m_Clip as three parallel banks (StreamedClip, DenseClip, ConstantClip) of
    ///    FLOATS, indexed by one flat curve index: streamed occupies [0, streamCurveCount), the
    ///    dense bank follows it, the constant bank follows that.
    ///  - m_ClipBindingConstant.genericBindings names the curves IN THAT SAME FLAT ORDER, each
    ///    binding eating as many floats as its attribute is wide. MEASURED on MV_RocketJumpIdle,
    ///    whose 12 bindings are 4x attribute 1, 4x attribute 2, 4x attribute 3 and whose 40
    ///    constant floats read: 12 small numbers, then 4 unit quaternions, then TWELVE 1.0s. So
    ///    attribute 1 = localPosition (3), 2 = localRotation (4), 3 = localScale (3), typeID 4 =
    ///    Transform - identified from the DATA, not from a remembered enum.
    ///  - <c>GenericBinding.path</c> is CRC-32 (reflected 0xEDB88320) of the transform's path
    ///    RELATIVE TO THE ANIMATOR's GameObject - the same function
    ///    <see cref="SkinFields.BoneHash"/> already identified for m_BoneNameHashes, which is why
    ///    it is reused rather than copied. Fireworm_unfurl binds 1095908316 =
    ///    crc32("Fireworm_root/Fireworm_base"), and its animator root itself would be crc32("") = 0.
    ///    THIS is why there is no free retargeting: the binding is a hash of a PATH, so a clip
    ///    authored against one skeleton drives NOTHING under a hierarchy that spells its bones
    ///    differently. Gate U6-sample-ctl-path measures exactly that.
    ///  - an empty StreamedClip is not an empty array: shipped clips with no streamed curve carry
    ///    TWO uints, 0x7F800000 (+infinity, the frame time) and 0 (its curve count), with
    ///    curveCount 0. Written as 0 entries the bank has no terminating frame.
    ///  - m_MuscleClipSize is a RUNTIME size, not the serialized one (Turret_ShootStart reports
    ///    2572 inside a 2376-byte asset), and it is exactly
    ///    <see cref="MuscleClipSize"/> - see there for the fit and the five clips it closes on.
    ///  - m_IndexArray is 200 entries of -1 on every generic clip (the humanoid muscle table,
    ///    unused here but part of the size above).
    ///  - LOOPING is m_MuscleClip.m_LoopTime, and NOTHING else. MEASURED 2026-08-23 over all 650
    ///    AnimationClips of nine shipped bundles (`px_equipment`, `nj_equipment`, `sy_equipment`,
    ///    `an_equipment`, `in_equipment`, `dlc2_ac_weapons`, `aln_fireworm`, `mutoid`, `_common`) by
    ///    reading every loop-ish field of each: <c>m_WrapMode</c> is <b>0 on all 650</b>, looping and
    ///    one-shot alike, so it is NOT the flag (it is the legacy Animation component's, and these are
    ///    m_Legacy=false clips); <c>m_LoopTime</c> is true on 132 and false on 518, and that split is
    ///    the contrast itself - `px_equipment` ships 'Turret_ShootLoop' (true) beside 'Turret_ShootEnd'
    ///    (false), both wrap 0, both otherwise identically shaped. Nothing TRAVELS with it: m_CycleOffset
    ///    is 0 on all 650, m_StartAtOrigin true on all 650, m_StartTime/m_StopTime carry the same
    ///    0..(frames-1)/rate shape either way, and m_LoopBlend is true on only 20 clips - every one of
    ///    them already m_LoopTime=true, so it is an EXTRA blend behaviour on top of looping and not a
    ///    requirement of it (112 looping clips ship without it). Hence one bool is written and its five
    ///    siblings are deliberately left as the shipped default.
    ///
    /// ponytail: ONE curve bank (dense). All THREE Transform attributes and any number of bones are
    /// written (U9) - a dense bank is a uniformly sampled float per frame, so it needs no
    /// keyframe/tangent format at all. Streamed (variable-rate, with tangents) and constant (one
    /// value, no time) are the two other banks the same flat index already reaches: a clip whose bones
    /// mostly hold still pays a full frame of floats per bone here where a constant curve would cost
    /// one, and that is the upgrade path if a real project's clips ever get too big.
    /// </summary>
    internal static class ClipFields
    {
        /// <summary>Transform, as genericBindings' typeID - measured, see the class remark.</summary>
        private const int TransformTypeId = 4;

        /// <summary>localPosition, as a genericBindings attribute. Three floats wide.</summary>
        internal const int AttributePosition = 1;
        internal const int PositionCurves = 3;

        /// <summary>localRotation (4 floats) and localScale (3) - the other two Transform attributes.</summary>
        internal const int AttributeRotation = 2;
        internal const int AttributeScale = 3;

        /// <summary>The one frame an EMPTY streamed bank still carries: +inf, then 0 curves.</summary>
        private const uint StreamedEndTime = 0x7F800000u;
        private const int StreamedEmptyUints = 2;

        /// <summary>
        /// m_MuscleClipSize = 2528 + 4*streamedUints + 4*denseSamples + 4*constantFloats
        /// + 8*valueArrayDelta. FITTED, then CLOSED EXACTLY on five shipped clips it was not fitted
        /// against: Turret_ShootStart 2572, MV_RocketJumpIdle 3016, Bind_Pose 3580, Reload 5220,
        /// MV_RocketJumpStartA 5412 and Fireworm_unfurl 7536. The 2528 is the fixed part - the
        /// poses, the four xforms and the 200-entry m_IndexArray, all of which every generic clip
        /// carries identically. A ValueDelta is two floats, hence its 8.
        /// </summary>
        internal static uint MuscleClipSize(int streamedUints, int denseSamples, int constantFloats,
                                            int valueArrayDelta)
        {
            return (uint)(2528 + 4 * streamedUints + 4 * denseSamples + 4 * constantFloats +
                          8 * valueArrayDelta);
        }

        internal struct Ids
        {
            internal long RootGameObject, RootTransform, Animator, BoneGameObject, BoneTransform;
        }

        /// <summary>
        /// ONE genericBindings entry and the dense floats it eats: a bone path, one of the three
        /// Transform attributes, and <c>frames * <see cref="CurveWidth"/>(Attribute)</c> values laid
        /// out FRAME-MAJOR within this binding (frame 0's whole vector, then frame 1's).
        /// <see cref="FillClip"/> interleaves them into the bank's own frame-major order.
        /// </summary>
        internal sealed class Binding
        {
            /// <summary>the bone's path relative to the ANIMATOR's GameObject.</summary>
            internal string BonePath;
            internal uint Attribute;
            internal float[] Values;
        }

        /// <summary>
        /// The single-binding case U6/U7 bake: one bone's localPosition at (0, y, 0) per frame. Sugar
        /// over <see cref="FillClip"/>, not a second path - the gates that predate the importer read
        /// better with it and it writes exactly what they wrote before.
        /// </summary>
        internal static Binding[] LiftY(string bonePath, float[] yPerFrame)
        {
            if (yPerFrame == null) throw new ArgumentNullException(nameof(yPerFrame));
            float[] values = new float[yPerFrame.Length * PositionCurves];
            for (int f = 0; f < yPerFrame.Length; f++) values[f * PositionCurves + 1] = yPerFrame[f];
            return new[] { new Binding { BonePath = bonePath, Attribute = AttributePosition, Values = values } };
        }

        /// <summary>
        /// An IMPORTED clip's tracks as bindings - the join U8 left open. A track's
        /// <see cref="SampledTrack.Node"/> is a JOINT SLOT, so its path is
        /// <see cref="BakedSkin.BonePath"/>'s and the CRC this binds is the one
        /// <see cref="SkinFields"/> already wrote into the MESH's m_BoneNameHashes. A channel the
        /// track does not carry produces no binding at all, which leaves that transform on its rest
        /// value - the same thing the exporter means by a null channel.
        ///
        /// Grouped by ATTRIBUTE - every position, then every rotation, then every scale. That is the
        /// order the one shipped clip whose bindings were counted carries (MV_RocketJumpIdle, 4x1 then
        /// 4x2 then 4x3, the class remark), so it is the measured layout rather than an invention.
        /// Nothing measured says the engine REQUIRES it; a per-bone grouping would be a different
        /// flat order over the same widths, and only the in-game arm can tell whether that matters.
        /// </summary>
        /// <param name="notes">takes the one line <see cref="Ramp"/> writes about the root motion it
        /// derived - or did not - so the bake log says it instead of the author guessing.</param>
        /// <param name="worldScale">the uniform scale the mod puts on the rig root in game
        /// (ppcontent.json "scale"), which is the factor between the file's units and the game's.
        /// <see cref="Ramp"/> is the one thing here that must be written in the GAME's units - see its
        /// remark. 1 means the file already imports at game scale, which is every project that says
        /// nothing.</param>
        internal static List<Binding> Bindings(SampledClip clip, BakedSkin skin, List<string> notes = null,
                                               float worldScale = 1f)
        {
            if (clip == null) throw new ArgumentNullException(nameof(clip));
            if (skin == null) throw new ArgumentNullException(nameof(skin));
            int frames = clip.Times == null ? 0 : clip.Times.Length;
            if (frames < 2)
                throw new InvalidDataException("clip '" + clip.Name + "' carries " + frames +
                    " frame(s); a clip needs at least two");

            List<Binding> bindings = new List<Binding>();
            foreach (SampledTrack t in clip.Tracks)
                if (t.Translations != null)
                    bindings.Add(Bind(clip, skin, t.Node, AttributePosition, Flat(t.Translations), frames));
            foreach (SampledTrack t in clip.Tracks)
                if (t.Rotations != null)
                    bindings.Add(Bind(clip, skin, t.Node, AttributeRotation, Flat(t.Rotations), frames));
            foreach (SampledTrack t in clip.Tracks)
                if (t.Scales != null)
                    bindings.Add(Bind(clip, skin, t.Node, AttributeScale, Flat(t.Scales), frames));

            string why = Ramp(clip, skin, bindings, worldScale);
            if (notes != null) notes.Add("clip '" + clip.Name + "': " + why);

            if (bindings.Count == 0)
                throw new InvalidDataException("clip '" + clip.Name + "' drives no bone of the rig, so " +
                    "there is nothing to bake; re-export it with the armature it was animated against");
            return bindings;
        }

        /// <summary>
        /// The ROOT-MOTION RAMP: the one binding the game measures its movement speed off, derived from
        /// the clip's own legs by <see cref="Treadmill"/> and written the way the shipped clips write
        /// it - a single localPosition curve on the armature root, everything else cycling under it
        /// (MEASURED on 'MV_RunFwd_Loop_AR', see that class's remark).
        ///
        /// A no-op unless the clip walks on the spot: a clip that already carries its own root travel,
        /// or is not locomotion at all, derives ~0 and is left exactly as the file wrote it. This
        /// coexists with <c>GlbReader</c>:804 rather than contradicting it - that refuses a FILE whose
        /// own channels would overwrite the root's rest transform (the correction folded into it), and
        /// this ADDS to that same rest instead of replacing it, keeping the fold intact.
        ///
        /// THE RAMP IS THE ONE CURVE WRITTEN IN THE GAME'S UNITS AND NOT THE FILE'S, and that is not a
        /// choice - it is what the engine measures. MEASURED against the decompile:
        ///   AnimationInfos.cs:105/108  animatedObj.transform.InverseTransformPoint(motionPoint.position)
        ///                              - the offset comes back in the ANIMATOR OBJECT'S LOCAL space,
        ///                                which divides out whatever scale the mod put on that object
        ///   AnimationInfos.cs:123      Speed = |Offset| / clip.length
        ///   TacticalNavigationComponent.cs:376  num2 += movementSpeed * Timing.Delta, where num2 is a
        ///                              WORLD path length (PathExecUtil over TacticalMap positions)
        /// So the engine reads a number in RIG units and spends it as WORLD units per second - it
        /// assumes the animated object's local space IS world space. TacticalMap.cs:67 states the other
        /// half: TileSize = 1f, so a world unit is a tile. A rig the mod shrinks by <paramref
        /// name="worldScale"/> breaks that assumption by exactly 1/worldScale, and the spider baked
        /// before this multiply existed measured 101.93 tiles/s - it crossed the map in one frame.
        /// LeapJumpPathProcessor.cs:138 divides the same OffsetMagnitude by a WORLD distance, so the
        /// assumption is the engine's throughout and not a quirk of one call site.
        ///
        /// The legs stay in step for free: they cycle at the file's own cadence, which slides the ground
        /// at <c>loco.Speed</c> rig-units/s = <c>loco.Speed * worldScale</c> world units/s - the very
        /// number written here. Travel and gait are one measurement, so nothing can drift between them.
        ///
        /// ponytail: <see cref="Treadmill.Derive"/> is called per clip and the result is used or
        /// dropped; a model with fifty clips walks its skeleton fifty times. That is milliseconds on a
        /// bake that already decodes meshes, and it keeps the derivation a pure function of ONE clip.
        /// </summary>
        internal static string Ramp(SampledClip clip, BakedSkin skin, List<Binding> bindings,
                                    float worldScale = 1f)
        {
            if (!(worldScale > 0f))
                throw new InvalidDataException("ppcontent.json \"scale\" is " +
                    worldScale.ToString("0.######", CultureInfo.InvariantCulture) +
                    "; a rig's world scale has to be a positive number, so remove the key or give it one");
            int root = Treadmill.RootBone(skin);
            if (root < 0) return "no ramp: the rig has no single root bone";
            string path = skin.BonePath(root);
            Treadmill.Locomotion loco = Treadmill.Derive(clip, skin);

            if (loco.Any)
            {
                int frames = clip.Times.Length;
                float[] rest = skin.BoneRest[root];
                SampledTrack own = null;
                foreach (SampledTrack t in clip.Tracks) if (t.Node == root) own = t;
                float t0 = clip.Times[0];
                float[] values = new float[frames * PositionCurves];
                for (int f = 0; f < frames; f++)
                {
                    // The rest position, or whatever the file's own curve puts there - the ramp is ADDED,
                    // so a root that bobs keeps its bob.
                    float bx = rest[12], by = rest[13], bz = rest[14];
                    if (own != null && own.Translations != null)
                    { bx = own.Translations[f].X; by = own.Translations[f].Y; bz = own.Translations[f].Z; }
                    float k = clip.Times[f] - t0;
                    values[f * PositionCurves] = bx + loco.Velocity.X * k;
                    values[f * PositionCurves + 1] = by + loco.Velocity.Y * k;
                    values[f * PositionCurves + 2] = bz + loco.Velocity.Z * k;
                }
                Put(bindings, path, values);
            }

            return loco.Why + ToGameUnits(bindings, path, clip, worldScale);
        }

        /// <summary>Replaces the root's position curve, or inserts it at the END of the position group
        /// (see the remark on <see cref="Bindings"/> for why the group order is load-bearing).</summary>
        private static void Put(List<Binding> bindings, string path, float[] values,
                                uint attribute = AttributePosition)
        {
            for (int i = 0; i < bindings.Count; i++)
                if (bindings[i].Attribute == attribute && bindings[i].BonePath == path)
                { bindings[i].Values = values; return; }
            int at = 0;
            while (at < bindings.Count && bindings[at].Attribute <= attribute) at++;
            bindings.Insert(at, new Binding { BonePath = path, Attribute = attribute, Values = values });
        }

        /// <summary>
        /// The root's REST rotation as a quaternion, read off its bind matrix - the pose a bone the clip
        /// never rotates is left in. Needed because the common case is a downloaded rig whose armature
        /// root carries no rotation channel at all (the fixture 'u8_probe.glb' is exactly that), and a
        /// pitch written from identity would throw away whatever correction the rest pose holds.
        ///
        /// The matrix is column-major, the same layout <see cref="Ramp"/> reads translation out of at
        /// 12/13/14, so the basis vectors are its columns. They are normalised first: a rig with a scale
        /// baked into its rest pose would otherwise produce a quaternion that is not a rotation.
        /// </summary>
        private static float[] RestRotation(float[] m)
        {
            float[] c = new float[9];
            for (int col = 0; col < 3; col++)
            {
                float x = m[col * 4], y = m[col * 4 + 1], z = m[col * 4 + 2];
                float len = (float)Math.Sqrt(x * x + y * y + z * z);
                if (!(len > 0f)) return new[] { 0f, 0f, 0f, 1f };
                c[col * 3] = x / len; c[col * 3 + 1] = y / len; c[col * 3 + 2] = z / len;
            }
            // m[row, col] = c[col * 3 + row]
            float m00 = c[0], m10 = c[1], m20 = c[2];
            float m01 = c[3], m11 = c[4], m21 = c[5];
            float m02 = c[6], m12 = c[7], m22 = c[8];
            float trace = m00 + m11 + m22, s;
            if (trace > 0f)
            {
                s = (float)Math.Sqrt(trace + 1f) * 2f;
                return new[] { (m21 - m12) / s, (m02 - m20) / s, (m10 - m01) / s, 0.25f * s };
            }
            if (m00 > m11 && m00 > m22)
            {
                s = (float)Math.Sqrt(1f + m00 - m11 - m22) * 2f;
                return new[] { 0.25f * s, (m01 + m10) / s, (m02 + m20) / s, (m21 - m12) / s };
            }
            if (m11 > m22)
            {
                s = (float)Math.Sqrt(1f + m11 - m00 - m22) * 2f;
                return new[] { (m01 + m10) / s, 0.25f * s, (m12 + m21) / s, (m02 - m20) / s };
            }
            s = (float)Math.Sqrt(1f + m22 - m00 - m11) * 2f;
            return new[] { (m02 + m20) / s, (m12 + m21) / s, 0.25f * s, (m10 - m01) / s };
        }

        /// <summary>
        /// A TRAVERSAL CLIP MADE OUT OF THE CREATURE'S OWN WALK - the bindings for one of the three
        /// parts of a climb, taken from the walk cycle and given a vertical root ramp instead of a
        /// forward one.
        ///
        /// The legs keep the walk's own cadence and the ROOT goes straight up, which is exactly what the
        /// engine measures: AnimationInfos.GetAnimInfo:104-121 samples the root-motion node at t=0 and
        /// t=length and calls the difference <c>Offset</c>, so a curve rising <paramref name="rise"/>
        /// over the clip IS a climb of that height as far as the path builder is concerned. The middle
        /// is stretched by the engine (see <see cref="Tactical.ClimbPlan.Rise"/>), so one pair of numbers
        /// covers every link height and nothing is scaled per link.
        ///
        /// The PITCH is the visual half: the root's own rotation curve is turned nose-up about the
        /// model's local +X (glTF's forward is +Z, up is +Y, so +X is its right), lerped from
        /// <paramref name="pitchFrom"/> to <paramref name="pitchTo"/> degrees across the clip. A spider
        /// climbing a wall really does face up it, so at 90 the ordinary walk cycle reads as climbing.
        ///
        /// ponytail: the walk's CADENCE is wrong against a vertical face - the feet still swing as if the
        /// ground were under them, and the gait is timed for the walk's speed, not the climb's. Authored
        /// per-family art is the upgrade, and mapping a clip to the "climb" role takes it (see
        /// <see cref="Tactical.CreatureRoles.All"/>). No pitch is written at all when the walk's
        /// root carries no rotation channel - the rest rotation is then the only thing that pose has,
        /// and replacing it would tip the creature out of whatever correction the import folded in.
        /// </summary>
        /// <param name="why">what was written, for the bake log - always, so an author reads the numbers
        /// the engine will measure instead of inferring them.</param>
        internal static List<Binding> Climb(SampledClip walk, BakedSkin skin, float worldScale,
                                            float rise, float pitchFrom, float pitchTo, out string why)
        {
            int root = Treadmill.RootBone(skin);
            if (root < 0) { why = "the rig has no single root bone, so nothing can carry a climb"; return null; }
            List<Binding> bindings = Bindings(walk, skin, null, worldScale);
            string path = skin.BonePath(root);
            int frames = walk.Times.Length;

            Binding pos = null, rot = null;
            foreach (Binding b in bindings)
            {
                if (b.BonePath != path) continue;
                if (b.Attribute == AttributePosition) pos = b;
                else if (b.Attribute == AttributeRotation) rot = b;
            }

            // The walk's forward travel is REPLACED, not added to: a climb that also drifts sideways
            // would land the actor off the link's own anchor.
            float[] rest = skin.BoneRest[root];
            float bx = rest[12], by = rest[13], bz = rest[14];
            if (pos != null) { bx = pos.Values[0]; by = pos.Values[1]; bz = pos.Values[2]; }
            float[] values = new float[frames * PositionCurves];
            for (int f = 0; f < frames; f++)
            {
                float k = frames < 2 ? 1f : (float)f / (frames - 1);
                values[f * PositionCurves] = bx;
                values[f * PositionCurves + 1] = by + rise * k;
                values[f * PositionCurves + 2] = bz;
            }
            Put(bindings, path, values);

            string pitched = "no pitch";
            if (pitchFrom != 0f || pitchTo != 0f)
            {
                // A root the walk never rotates has no curve to ride, so one is written from its REST
                // rotation - which is the pose it holds anyway, so at 0 degrees this is a no-op.
                if (rot == null)
                {
                    float[] q = RestRotation(rest);
                    float[] all = new float[frames * CurveWidth(AttributeRotation)];
                    for (int f = 0; f < frames; f++)
                        for (int i = 0; i < 4; i++) all[f * 4 + i] = q[i];
                    rot = new Binding { BonePath = path, Attribute = AttributeRotation, Values = all };
                    Put(bindings, path, all, AttributeRotation);
                }
                for (int f = 0; f < frames; f++)
                {
                    float k = frames < 2 ? 1f : (float)f / (frames - 1);
                    // Negated: the manifest's angle is NOSE-UP, and a right-hand rotation about +X of
                    // -90 degrees is what takes +Z (forward) onto +Y (up).
                    double half = -(pitchFrom + (pitchTo - pitchFrom) * k) * Math.PI / 360.0;
                    float s = (float)Math.Sin(half), w = (float)Math.Cos(half);
                    int i = f * CurveWidth(AttributeRotation);
                    float qx = rot.Values[i], qy = rot.Values[i + 1],
                          qz = rot.Values[i + 2], qw = rot.Values[i + 3];
                    // q * pitch - applied on the RIGHT, so the pitch is in the bone's own local frame
                    // and rides whatever the walk already does with it.
                    rot.Values[i] = qw * s + qx * w;
                    rot.Values[i + 1] = qy * w + qz * s;
                    rot.Values[i + 2] = qz * w - qy * s;
                    rot.Values[i + 3] = qw * w - qx * s;
                }
                pitched = "pitched " + pitchFrom.ToString("0.#", CultureInfo.InvariantCulture) + " -> " +
                          pitchTo.ToString("0.#", CultureInfo.InvariantCulture) + " degrees nose-up";
            }

            why = "rises " + rise.ToString("0.###", CultureInfo.InvariantCulture) + " over " + frames +
                  " frame(s) on '" + path + "', " + pitched;
            return bindings;
        }

        /// <summary>
        /// How far the root-motion node RISES across a clip's bindings - the <c>Offset.y</c> the engine
        /// will measure (AnimationInfos.GetAnimInfo:104-121). NaN when the root carries no position
        /// curve at all, which is a clip that moves the actor nowhere.
        /// </summary>
        internal static float RiseOf(IList<Binding> bindings, BakedSkin skin, int frames)
        {
            int root = Treadmill.RootBone(skin);
            if (root < 0 || frames < 2) return float.NaN;
            string path = skin.BonePath(root);
            foreach (Binding b in bindings)
                if (b.Attribute == AttributePosition && b.BonePath == path &&
                    b.Values != null && b.Values.Length >= frames * PositionCurves)
                    return b.Values[(frames - 1) * PositionCurves + 1] - b.Values[1];
            return float.NaN;
        }

        /// <summary>
        /// THE PROJECTION INTO THE GAME'S UNITS, and the last thing that touches the root's curve.
        ///
        /// Only the TRAVEL is scaled - each frame's offset from frame 0 - and never the rest position it
        /// starts from, so the rig stays exactly where the file puts it and only the distance it covers
        /// is restated. That makes ONE rule cover both cases: a ramp <see cref="Ramp"/> just derived,
        /// and a file that already exported real root motion in its own units. A clip whose root does not
        /// translate is left alone, because scaling zero is zero.
        ///
        /// A root that also BOBS is flattened along with its travel, and that is the right side to err
        /// on: the shipped convention puts nothing but travel on this node (MEASURED on
        /// 'MV_RunFwd_Loop_AR' - one translating binding, 279 rotations), and a generic Avatar strips
        /// this node's motion from the rendered hierarchy anyway.
        ///
        /// Returns the sentence the bake log prints - always, including at scale 1, so the author can
        /// read what the engine will measure instead of inferring it.
        /// </summary>
        private static string ToGameUnits(List<Binding> bindings, string path, SampledClip clip, float worldScale)
        {
            Binding b = null;
            foreach (Binding x in bindings)
                if (x.Attribute == AttributePosition && x.BonePath == path) b = x;
            int frames = clip.Times == null ? 0 : clip.Times.Length;
            if (b == null || frames < 2 || b.Values.Length < frames * PositionCurves) return "";

            if (worldScale != 1f)
                for (int f = 0; f < frames; f++)
                    for (int c = 0; c < 3; c++)
                    {
                        int i = f * PositionCurves + c;
                        b.Values[i] = b.Values[c] + (b.Values[i] - b.Values[c]) * worldScale;
                    }

            int last = (frames - 1) * PositionCurves;
            double dx = b.Values[last] - b.Values[0];
            double dy = b.Values[last + 1] - b.Values[1];
            double dz = b.Values[last + 2] - b.Values[2];
            double seconds = clip.Times[frames - 1] - clip.Times[0];
            double speed = seconds > 0 ? Math.Sqrt(dx * dx + dy * dy + dz * dz) / seconds : 0;
            return " -> the game measures " + speed.ToString("0.######", CultureInfo.InvariantCulture) +
                   " tile/s (TacticalMap.cs:67 TileSize=1) at scale " +
                   worldScale.ToString("0.######", CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// The clips of an imported model that can actually be BAKED, each paired with the asset name
        /// it takes, in the file's own order. Two facts about an author's file that the U8/U9 probe
        /// does not carry, neither of which is an error in the file:
        ///
        ///  - a clip whose channels are ALL blend-shape weights, or all drive nodes that are not
        ///    joints of the armature, comes back from <see cref="Import.GlbReader"/> with ZERO tracks
        ///    (deliberately - it counts the loss into <c>SampledClip.LossyReason</c>). There is nothing
        ///    to bind, so <see cref="Bindings"/> refuses it; it is left OUT here instead, because ONE
        ///    such clip must not abort the bake of the model's other clips - or of the whole project.
        ///    Every one left out is written into <paramref name="skipped"/>, so the drop is REPORTED
        ///    and never silent.
        ///  - glTF does not require animation names to be unique, and a container key is lowercased
        ///    (<c>BundleBaker.Normalize</c>), so two clips named "Walk", or "Walk" and "walk", would
        ///    collapse onto ONE key and the second AddAnimationClip would refuse it as a duplicate.
        ///    The first keeps the readable name; a colliding one takes its own index.
        /// </summary>
        internal static List<KeyValuePair<string, SampledClip>> Bakeable(
            string modelName, IList<SampledClip> clips, List<string> skipped = null)
        {
            var plan = new List<KeyValuePair<string, SampledClip>>();
            var taken = new HashSet<string>(StringComparer.Ordinal);
            foreach (SampledClip c in clips)
            {
                if (c.Tracks.Count == 0)
                {
                    if (skipped != null)
                        skipped.Add("clip '" + c.Name + "' drives no bone of this rig and was SKIPPED - " +
                                    "the model's other clips are unaffected; " + c.LossyReason);
                    continue;
                }
                // Normalized the way the container key will be, so two names that differ only in case
                // or in slash spelling collide HERE, where a name is still free to change.
                string name = (modelName + "_" + c.Name).Replace('\\', '/').Trim('/').ToLowerInvariant();
                while (!taken.Add(name)) name += "_" + plan.Count;
                plan.Add(new KeyValuePair<string, SampledClip>(name, c));
            }
            return plan;
        }

        /// <summary>
        /// The clip names one ppcontent.json string declares: <c>"loop": "Spider_Idle, Spider_Walk"</c>.
        /// Compared against the .glb's OWN animation names, case-insensitively, because that name is the
        /// only one an author can see without opening the file in anything - not the lowercased
        /// container key <see cref="Bakeable"/> derives from it.
        ///
        /// ponytail: glTF carries no loop flag at all (checked - the probe .glb's only "extras" are
        /// two MATERIAL entries from its FBX converter), and the tool's own extractor puts
        /// <c>SampledClip.Looping</c> in the SIDECAR json, never in the .glb, so there is nothing in the
        /// file to infer from and this one string is the declaration.
        /// </summary>
        internal static HashSet<string> Names(string declaration)
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrEmpty(declaration)) return names;
            foreach (string part in declaration.Split(',', ';'))
            {
                string name = part.Trim();
                if (name.Length > 0) names.Add(name);
            }
            return names;
        }

        /// <summary>
        /// A declared name that no clip of the project BAKES - an author's typo, which would otherwise
        /// do NOTHING and say nothing (the silent-failure class this repo counts as a defect). Returns
        /// the sentence to fail with, listing the names the files DO bake, or null when every declared
        /// name was found.
        ///
        /// <paramref name="clipNames"/> is the BAKEABLE list (see <see cref="Bakeable"/>), so a clip the
        /// file really carries but the bake has nowhere to put is refused here too - correctly, since
        /// declaring it would loop, or would be played by, an asset that is never written. But then
        /// "no clip carries it" is a LIE about the author's own file and sends them hunting a typo that
        /// is not there, so the <paramref name="skipped"/> lines <see cref="Bakeable"/> already wrote
        /// are matched by name and the real reason comes back with the refusal.
        /// </summary>
        /// <param name="what">the ppcontent.json key being checked, for the message.</param>
        /// <param name="skipped">the reason lines for clips left OUT of the bake, if any.</param>
        internal static string Unknown(string what, IEnumerable<string> declared, IList<string> clipNames,
                                       IList<string> skipped = null)
        {
            var missing = new List<string>();
            var why = new List<string>();
            foreach (string name in declared)
            {
                bool found = false;
                foreach (string have in clipNames)
                    if (Wants(name, have)) { found = true; break; }
                if (found) continue;
                missing.Add(name);
                // The skipped line is the one Bakeable wrote, and it opens with the clip's own quoted
                // name - so the match is on that spelling, not on a substring that "Walk" would find
                // inside "Walking".
                if (skipped != null)
                    foreach (string line in skipped)
                        if (line.IndexOf("'" + name + "'", StringComparison.OrdinalIgnoreCase) >= 0)
                            why.Add(line);
            }
            if (missing.Count == 0) return null;
            string[] have2 = new string[clipNames.Count];
            clipNames.CopyTo(have2, 0);
            return "ppcontent.json \"" + what + "\" names " + string.Join(", ", missing.ToArray()) +
                   ", which no clip in this project bakes; its Content\\Models\\ files bake: " +
                   (have2.Length == 0 ? "(no clip at all)" : string.Join(", ", have2)) +
                   (why.Count == 0 ? "" : " - and that name IS in the file, but " +
                                          string.Join(" ", why.ToArray()));
        }

        /// <summary>
        /// Does one declared entry claim <paramref name="clip"/>? An exact, case-insensitive name -
        /// UNLESS it carries a <c>*</c>, and then it is a glob.
        ///
        /// ponytail: `*` and nothing else - no character classes, no `?`. A model retargeted onto the
        /// game's own skeleton arrives with HUNDREDS of clips under the game's own names, and
        /// "which of these loop" is then a naming rule (<c>*_Loop_*</c>, <c>*Idle*</c>) and not a list
        /// anyone can keep by hand. A project that ships four clips still writes four names.
        /// </summary>
        internal static bool Wants(string declared, string clip)
        {
            if (declared.IndexOf('*') < 0)
                return string.Equals(declared, clip, StringComparison.OrdinalIgnoreCase);
            string[] parts = declared.Split('*');
            int at = 0;
            for (int i = 0; i < parts.Length; i++)
            {
                if (parts[i].Length == 0) continue;
                int found = clip.IndexOf(parts[i], at, StringComparison.OrdinalIgnoreCase);
                // An anchored end: the first part must sit at 0, the last must finish the name.
                if (found < 0 || (i == 0 && found != 0)) return false;
                at = found + parts[i].Length;
            }
            string tail = parts[parts.Length - 1];
            return tail.Length == 0 || at == clip.Length;
        }

        /// <summary>Does ANY entry claim it? The set the bake asks per clip.</summary>
        internal static bool Wants(IEnumerable<string> declared, string clip)
        {
            foreach (string d in declared) if (Wants(d, clip)) return true;
            return false;
        }

        /// <summary>
        /// The Animator plays ONE clip, so <c>"play"</c> may name one - unlike <c>"loop"</c>, which is a
        /// list because any number of clips can cycle. Returns the sentence to fail with, or null.
        ///
        /// This exists because the two sides of that string used to PARSE IT DIFFERENTLY: the gate split
        /// it on separators (<see cref="Names"/>) and accepted "Idle, Walk" when the project carried both,
        /// while <see cref="Chosen"/> compared the whole raw string, matched nothing, and the caller fell
        /// back to clip 0. A valid-looking declaration, the wrong animation, and not a word said. There is
        /// now ONE parser - <see cref="Names"/> - and both sides take their answer from it, which is why
        /// this takes the PARSED set and not the raw string.
        /// </summary>
        internal static string TooMany(ICollection<string> play)
        {
            if (play.Count < 2) return null;
            var all = new string[play.Count];
            play.CopyTo(all, 0);
            return "ppcontent.json \"play\" names " + string.Join(", ", all) + ", but the Animator plays " +
                   "ONE clip and cannot be handed " + play.Count + "; name a single one (\"loop\" is the " +
                   "declaration that takes a list)";
        }

        /// <summary>
        /// WHICH of a model's clips the Animator plays - ppcontent.json's <c>"play": "Spider_Walk"</c>,
        /// parsed by <see cref="Names"/> - the SAME parser the project gate validates with, so the two
        /// cannot read one string two ways - and matched the same case-blind way.  A model with Attack/
        /// Death/Idle/Jump/Walk otherwise always gets the FIRST bakeable one, which is alphabetical accident.
        ///
        /// Returns the plan index, 0 when nothing is declared (exactly what the bake did before), and
        /// -1 when the declaration does not resolve to one clip of THIS model's plan - the caller decides
        /// whether that is another model's clip or an author's mistake. It never GUESSES: a declaration
        /// naming several clips is -1 here and refused by name at the gate (<see cref="TooMany"/>), rather
        /// than silently becoming clip 0.
        ///
        /// ponytail: the first case-blind match wins, so a file with both "Walk" and "walk" is decided
        /// by file order (and one declaration naming both is ONE name to <see cref="Names"/>, which
        /// compares case-blind). Those two already collapse to one container key (see <see cref="Bakeable"/>);
        /// a project that really ships both can rename one.
        /// </summary>
        internal static int Chosen(IList<KeyValuePair<string, SampledClip>> plan, string play)
        {
            HashSet<string> names = Names(play);
            if (names.Count == 0 || plan.Count == 0) return 0;
            if (names.Count > 1) return -1;
            string want = null;
            foreach (string name in names) { want = name; break; }
            for (int i = 0; i < plan.Count; i++)
                if (string.Equals(plan[i].Value.Name, want, StringComparison.OrdinalIgnoreCase)) return i;
            return -1;
        }

        /// <summary>The distance a transform has to leave its rest value by before it COUNTS as
        /// driven - one number, so the count of movers and the distance that backs it cannot be
        /// measured against two different thresholds.</summary>
        internal const float RestTravel = 1e-3f;

        /// <summary>
        /// The verdict of a "the baked clip really drives the rig" arm, decided in ONE place so that
        /// no path through such an arm can end in a skip. Returns null when the drive PROVED
        /// something, and otherwise the reason it proved nothing.
        ///
        /// Every reason here used to be a VOID that returned ZERO failures - and a VOID is not a
        /// failure, so a bundle whose baked Animator or whose EXTERNAL base controller did not
        /// resolve reported ALL PASS while the shipping path the gate exists to prove was never
        /// exercised once. A missing PIECE of the shipping shape is a FAILURE of the gate, not a
        /// question the fixture cannot ask.
        /// </summary>
        /// <param name="missing">what the shipping shape did not carry, or null when it assembled.</param>
        /// <param name="bones">how many bones the clip drives.</param>
        /// <param name="wrong">how many of them are NOT where the file says they should be.</param>
        /// <param name="moved">how many left their rest pose - the control, since a rig frozen at its
        /// bind pose satisfies "no bone is wrong" perfectly.</param>
        /// <param name="travel">the furthest ANY bone travelled from rest. The count and the distance
        /// are two readings of ONE fact: movers with a travel of zero means the metric measures
        /// nothing, and a control that measures nothing is not a control.</param>
        internal static string DriveVerdict(string missing, int bones, int wrong, int moved, float travel)
        {
            if (missing != null) return missing;
            if (bones == 0) return "the clip drives no bone at all";
            if (wrong > 0) return wrong + " of " + bones + " bone(s) are not where the file says";
            if (moved == 0) return "no bone left its rest pose, so a rig frozen at its bind pose reads the same";
            if (travel <= RestTravel)
                return moved + " bone(s) count as moved but the furthest travelled " +
                       travel.ToString("0.######", CultureInfo.InvariantCulture) +
                       " - the control measures nothing";
            return null;
        }

        private static Binding Bind(SampledClip clip, BakedSkin skin, int node, uint attribute,
                                    float[] values, int frames)
        {
            if (node < 0 || node >= skin.BoneNames.Length)
                throw new InvalidDataException("clip '" + clip.Name + "' drives bone slot " + node +
                    " but the rig has " + skin.BoneNames.Length + " bone(s)");
            if (values.Length != frames * CurveWidth(attribute))
                throw new InvalidDataException("clip '" + clip.Name + "' bone '" + skin.BoneNames[node] +
                    "' has " + values.Length + " value(s) for " + frames + " frame(s) of attribute " + attribute);
            return new Binding { BonePath = skin.BonePath(node), Attribute = attribute, Values = values };
        }

        private static float[] Flat(ObjVector3[] v)
        {
            float[] f = new float[v.Length * 3];
            for (int i = 0; i < v.Length; i++) { f[i * 3] = v[i].X; f[i * 3 + 1] = v[i].Y; f[i * 3 + 2] = v[i].Z; }
            return f;
        }

        private static float[] Flat(ObjQuaternion[] q)
        {
            float[] f = new float[q.Length * 4];
            for (int i = 0; i < q.Length; i++)
            {
                f[i * 4] = q[i].X; f[i * 4 + 1] = q[i].Y; f[i * 4 + 2] = q[i].Z; f[i * 4 + 3] = q[i].W;
            }
            return f;
        }

        /// <summary>
        /// One AnimationClip over N bindings, every curve sampled uniformly at
        /// <paramref name="sampleRate"/> Hz into the DENSE bank. The bank is frame-major over the FLAT
        /// curve order (<c>sample[frame * curveCount + curve]</c>, the class remark), and
        /// genericBindings names the curves in that same order, each eating
        /// <see cref="CurveWidth"/> floats - so the interleave below and the binding list are two
        /// halves of one measured layout, not two conventions that have to agree.
        /// </summary>
        /// <param name="loop">true writes m_MuscleClip.m_LoopTime - the ONE field that makes the engine
        /// cycle the clip instead of holding its last frame (measured, see the class remark). Default
        /// false, which is what every clip baked before this carried.</param>
        internal static void FillClip(AssetTypeValueField clip, IList<Binding> bindings, int frames,
                                      float sampleRate, bool loop = false)
        {
            if (clip == null) throw new ArgumentNullException(nameof(clip));
            if (bindings == null || bindings.Count == 0)
                throw new ArgumentException("a clip needs at least one binding", nameof(bindings));
            if (frames < 2) throw new ArgumentException("a clip needs at least two frames", nameof(frames));

            int curves = 0;
            foreach (Binding b in bindings)
            {
                if (b == null || string.IsNullOrEmpty(b.BonePath))
                    throw new ArgumentException("a binding with no bone path", nameof(bindings));
                // Only the three MEASURED Transform attributes are written. CurveWidth takes anything
                // else as one float, which is a safe assumption for READING a shipped clip and a
                // guessed layout for writing one - so writing it is refused instead.
                if (b.Attribute != AttributePosition && b.Attribute != AttributeRotation &&
                    b.Attribute != AttributeScale)
                    throw new ArgumentException("attribute " + b.Attribute + " is not one of the three " +
                        "measured Transform attributes (1 position, 2 rotation, 3 scale)", nameof(bindings));
                if (b.Values == null || b.Values.Length != frames * CurveWidth(b.Attribute))
                    throw new ArgumentException("binding '" + b.BonePath + "' attribute " + b.Attribute +
                        " holds " + (b.Values == null ? 0 : b.Values.Length) + " value(s), not " +
                        frames * CurveWidth(b.Attribute), nameof(bindings));
                curves += CurveWidth(b.Attribute);
            }

            clip["m_Legacy"].AsBool = false;
            clip["m_Compressed"].AsBool = false;
            clip["m_UseHighQualityCurve"].AsBool = true;
            clip["m_SampleRate"].AsFloat = sampleRate;
            // 0 on all 650 shipped clips, looping or not - it is the legacy Animation component's field
            // and says nothing about a Mecanim clip. m_LoopTime below is the one that does.
            clip["m_WrapMode"].AsInt = 0;

            int samples = frames * curves;
            float stopTime = (frames - 1) / sampleRate;

            AssetTypeValueField muscle = clip["m_MuscleClip"];
            // Every xform in a shipped clip is the identity; the empty template leaves q and s all
            // zero, and a zero quaternion is not a rotation.
            IdentityXforms(muscle);
            muscle["m_StartTime"].AsFloat = 0f;
            muscle["m_StopTime"].AsFloat = stopTime;
            // Both true on every shipped generic clip; the template says false.
            muscle["m_StartAtOrigin"].AsBool = true;
            muscle["m_KeepOriginalPositionY"].AsBool = true;
            // The whole of "this clip cycles" - see the class remark for the 650-clip measurement that
            // says its five loop-ish siblings stay at their shipped default.
            muscle["m_LoopTime"].AsBool = loop;

            AssetTypeValueField index = muscle["m_IndexArray"]["Array"];
            index.Children.Clear();
            for (int i = 0; i < 200; i++) index.Children.Add(Int(index, -1));

            AssetTypeValueField data = muscle["m_Clip"]["data"];

            AssetTypeValueField streamed = data["m_StreamedClip"]["data"]["Array"];
            streamed.Children.Clear();
            streamed.Children.Add(UInt(streamed, StreamedEndTime));
            streamed.Children.Add(UInt(streamed, 0u));
            data["m_StreamedClip"]["curveCount"].AsUInt = 0;

            AssetTypeValueField dense = data["m_DenseClip"];
            dense["m_FrameCount"].AsInt = frames;
            dense["m_CurveCount"].AsUInt = (uint)curves;
            dense["m_SampleRate"].AsFloat = sampleRate;
            dense["m_BeginTime"].AsFloat = 0f;
            AssetTypeValueField sampleArray = dense["m_SampleArray"]["Array"];
            sampleArray.Children.Clear();
            for (int f = 0; f < frames; f++)
                foreach (Binding b in bindings)
                {
                    int width = CurveWidth(b.Attribute);
                    for (int c = 0; c < width; c++)
                        sampleArray.Children.Add(Float(sampleArray, b.Values[f * width + c]));
                }

            data["m_ConstantClip"]["data"]["Array"].Children.Clear();

            // One entry per CURVE FLOAT, holding that curve's value at m_StartTime and m_StopTime -
            // measured on MV_RocketJumpIdle, whose 40 deltas mirror its 40 constant floats.
            AssetTypeValueField delta = muscle["m_ValueArrayDelta"]["Array"];
            delta.Children.Clear();
            foreach (Binding b in bindings)
            {
                int width = CurveWidth(b.Attribute);
                for (int c = 0; c < width; c++)
                {
                    AssetTypeValueField e = ValueBuilder.DefaultValueFieldFromArrayTemplate(delta);
                    e["m_Start"].AsFloat = b.Values[c];
                    e["m_Stop"].AsFloat = b.Values[(frames - 1) * width + c];
                    delta.Children.Add(e);
                }
            }
            muscle["m_ValueArrayReferencePose"]["Array"].Children.Clear();

            clip["m_MuscleClipSize"].AsUInt = MuscleClipSize(StreamedEmptyUints, samples, 0, curves);

            AssetTypeValueField bindingArray = clip["m_ClipBindingConstant"]["genericBindings"]["Array"];
            bindingArray.Children.Clear();
            foreach (Binding binding in bindings)
            {
                AssetTypeValueField b = ValueBuilder.DefaultValueFieldFromArrayTemplate(bindingArray);
                b["path"].AsUInt = SkinFields.BoneHash(binding.BonePath);
                b["attribute"].AsUInt = binding.Attribute;
                b["script"]["m_FileID"].AsInt = 0;
                b["script"]["m_PathID"].AsLong = 0;
                b["typeID"].AsInt = TransformTypeId;
                b["customType"].AsByte = 0;
                b["isPPtrCurve"].AsByte = 0;
                bindingArray.Children.Add(b);
            }
            clip["m_ClipBindingConstant"]["pptrCurveMapping"]["Array"].Children.Clear();

            clip["m_HasGenericRootTransform"].AsBool = false;
            clip["m_HasMotionFloatCurves"].AsBool = false;
        }

        /// <summary>
        /// An AnimatorOverrideController: a base controller plus one original-&gt;override clip
        /// pair. Three fields, measured off `_common`'s own 'ArmadilloHulkCrateAnimator' (96 bytes
        /// on disk) - which is the whole reason this route is cheaper than serializing a
        /// ControllerConstant: the state machine stays the shipped one and only the clip is ours.
        /// </summary>
        /// <param name="baseFileId">PPtr fileID of the file the base controller lives in.</param>
        internal static void FillOverrideController(AssetTypeValueField aoc,
                                                    int baseFileId, long basePathId,
                                                    int originalFileId, long originalPathId,
                                                    long overridePathId)
        {
            if (aoc == null) throw new ArgumentNullException(nameof(aoc));
            aoc["m_Controller"]["m_FileID"].AsInt = baseFileId;
            aoc["m_Controller"]["m_PathID"].AsLong = basePathId;

            AssetTypeValueField clips = aoc["m_Clips"]["Array"];
            clips.Children.Clear();
            AssetTypeValueField pair = ValueBuilder.DefaultValueFieldFromArrayTemplate(clips);
            pair["m_OriginalClip"]["m_FileID"].AsInt = originalFileId;
            pair["m_OriginalClip"]["m_PathID"].AsLong = originalPathId;
            // The override is OURS, so fileID 0 - this same serialized file.
            pair["m_OverrideClip"]["m_FileID"].AsInt = 0;
            pair["m_OverrideClip"]["m_PathID"].AsLong = overridePathId;
            clips.Children.Add(pair);
        }

        /// <summary>
        /// Root GameObject+Transform+Animator, and one child transform for the clip to drive at
        /// <paramref name="boneRestY"/>. No renderer and no mesh: U6's oracle is a transform value,
        /// and a skin would only add a second thing that can fail.
        /// </summary>
        internal static Ids Build(AssetsFile afile, ClassDatabaseFile cldb, Func<long> nextPathId,
                                  string rootName, string boneName, float boneRestY,
                                  long controllerPathId)
        {
            if (afile == null) throw new ArgumentNullException(nameof(afile));
            if (string.IsNullOrEmpty(rootName)) throw new ArgumentException("empty root name", nameof(rootName));
            if (string.IsNullOrEmpty(boneName)) throw new ArgumentException("empty bone name", nameof(boneName));

            Ids ids = new Ids
            {
                RootGameObject = nextPathId(),
                RootTransform = nextPathId(),
                Animator = nextPathId(),
                BoneGameObject = nextPathId(),
                BoneTransform = nextPathId()
            };

            PrefabFields.Create(afile, cldb, ids.RootGameObject, AssetClassID.GameObject, go =>
            {
                PrefabFields.FillGameObject(go, rootName);
                PrefabFields.AddComponent(go, ids.RootTransform);
                PrefabFields.AddComponent(go, ids.Animator);
            });
            PrefabFields.Create(afile, cldb, ids.RootTransform, AssetClassID.Transform, tf =>
            {
                PrefabFields.FillTransform(tf, ids.RootGameObject, 0f, 0f, 0f);
                AssetTypeValueField children = tf["m_Children"]["Array"];
                AssetTypeValueField p = ValueBuilder.DefaultValueFieldFromArrayTemplate(children);
                p["m_FileID"].AsInt = 0;
                p["m_PathID"].AsLong = ids.BoneTransform;
                children.Children.Add(p);
            });

            PrefabFields.Create(afile, cldb, ids.Animator, AssetClassID.Animator,
                                a => FillAnimator(a, ids.RootGameObject, controllerPathId));

            PrefabFields.Create(afile, cldb, ids.BoneGameObject, AssetClassID.GameObject, go =>
            {
                PrefabFields.FillGameObject(go, boneName);
                PrefabFields.AddComponent(go, ids.BoneTransform);
            });
            PrefabFields.Create(afile, cldb, ids.BoneTransform, AssetClassID.Transform, tf =>
            {
                PrefabFields.FillTransform(tf, ids.BoneGameObject, 0f, boneRestY, 0f);
                PrefabFields.Pptr(tf["m_Father"], ids.RootTransform);
            });

            return ids;
        }

        /// <summary>
        /// The Animator a clip is played through - field by field off aln_fireworm's own Animator,
        /// with the two deliberate changes named below. Its m_Controller is 0 there (Phoenix Point
        /// assigns controllers at runtime), so the reference is the only part no shipped object could
        /// supply. ONE copy of these fields, because U7 puts the same component on an IMPORTED model
        /// root and two spellings of a measured layout is one too many.
        ///
        /// NOT written, MEASURED absent: there is no m_AnimationType on a 2019.4.31f1 AnimationClip -
        /// a shipped generic clip and ours both read "(no field)" through the class database
        /// (tests\ObjCodecTests\ClipRoundTrip.cs). "Generic = 2" is a memory of an older Unity.
        /// </summary>
        internal static void FillAnimator(AssetTypeValueField a, long gameObjectPathId, long controllerPathId)
        {
            PrefabFields.Pptr(a["m_GameObject"], gameObjectPathId);
            a["m_Enabled"].AsBool = true;
            a["m_Avatar"]["m_FileID"].AsInt = 0;
            a["m_Avatar"]["m_PathID"].AsLong = 0;   // no Avatar = generic, which is what we bake
            PrefabFields.Pptr(a["m_Controller"], controllerPathId);
            // 0 = AlwaysAnimate, NOT the shipped 1 (CullUpdateTransforms): a gate's instance is never
            // rendered, and a culled Animator legitimately writes no transform at all.
            a["m_CullingMode"].AsInt = 0;
            a["m_UpdateMode"].AsInt = 0;
            a["m_ApplyRootMotion"].AsBool = false;
            a["m_LinearVelocityBlending"].AsBool = false;
            // true, and the template says false: false is "optimize transform hierarchy", which
            // DELETES the child transforms the clip has to address by path.
            a["m_HasTransformHierarchy"].AsBool = true;
            a["m_AllowConstantClipSamplingOptimization"].AsBool = true;
            a["m_KeepAnimatorControllerStateOnDisable"].AsBool = false;
        }

        /// <summary>
        /// What a baked clip and its override controller hold in a FILE, in one line - the oracle
        /// the offline round trip and the in-game U6-wrote arm both read, so a passing test and a
        /// passing gate mean the same thing. Every reference is a NAME or a number the data itself
        /// supplies.
        /// </summary>
        /// <param name="aocName">null reports the CLIP alone - U7's case, where the clip is handed to
        /// AnimationClip.SampleAnimation and there is no override controller to report on.</param>
        /// <param name="uniqueIn">OPT-IN, and only `ct_list clip` passes it: the clip is resolved by
        /// <see cref="AssetIndex.FindUnique"/> against this label instead of by first-match
        /// <see cref="Find"/>, so an ambiguous clip name is REFUSED the way ct_list bones and props
        /// already refuse one rather than silently reporting whichever clip came first. Left null the
        /// bake side (ProjectBake, BakeSelfCheck) reads exactly what it always read.</param>
        internal static string Summary(AssetsManager m, AssetsFileInstance af, string clipName, string aocName,
                                       string uniqueIn = null)
        {
            AssetTypeValueField clip = uniqueIn == null
                ? Find(m, af, AssetClassID.AnimationClip, clipName)
                : m.GetBaseField(af, AssetIndex.FindUnique(m, af, AssetClassID.AnimationClip, clipName, uniqueIn));
            if (clip == null) return "no AnimationClip named '" + clipName + "'";

            AssetTypeValueField bindings = clip["m_ClipBindingConstant"]["genericBindings"]["Array"];
            AssetTypeValueField data = clip["m_MuscleClip"]["m_Clip"]["data"];
            AssetTypeValueField dense = data["m_DenseClip"];
            AssetTypeValueField samples = dense["m_SampleArray"]["Array"];
            int frames = dense["m_FrameCount"].AsInt;
            int curves = (int)dense["m_CurveCount"].AsUInt;

            string s = "clip '" + clipName + "' bindings=" + bindings.Children.Count;
            if (bindings.Children.Count == 1)
            {
                AssetTypeValueField b = bindings.Children[0];
                s += " path=" + b["path"].AsUInt + " attr=" + b["attribute"].AsUInt +
                     " typeID=" + b["typeID"].AsInt;
            }
            else
            {
                // An imported clip binds a hundred curves; naming them all makes an unreadable line and
                // naming none makes an unfalsifiable one. The fingerprint is ORDER-SENSITIVE and
                // computed from the same Pair() spelling the bake side predicts it from, so a curve on
                // the wrong bone, a wrong attribute, or a reordered flat index all move it.
                List<string> pairs = new List<string>();
                foreach (AssetTypeValueField b in bindings.Children)
                    pairs.Add(Pair(b["path"].AsUInt, b["attribute"].AsUInt));
                s += " sig=" + Sig(pairs) + " typeID=" + bindings.Children[0]["typeID"].AsInt;
            }
            s += " dense=" + frames + "x" + curves + "@" + F(dense["m_SampleRate"].AsFloat) +
                 " samples=" + samples.Children.Count +
                 " first=" + Triple(samples, 0, curves) + " last=" + Triple(samples, frames - 1, curves) +
                 " streamed=" + data["m_StreamedClip"]["data"]["Array"].Children.Count + "/" +
                 data["m_StreamedClip"]["curveCount"].AsUInt +
                 " const=" + data["m_ConstantClip"]["data"]["Array"].Children.Count +
                 " delta=" + clip["m_MuscleClip"]["m_ValueArrayDelta"]["Array"].Children.Count +
                 " index=" + clip["m_MuscleClip"]["m_IndexArray"]["Array"].Children.Count +
                 " stop=" + F(clip["m_MuscleClip"]["m_StopTime"].AsFloat) +
                 " muscleSize=" + clip["m_MuscleClipSize"].AsUInt +
                 " legacy=" + clip["m_Legacy"].AsBool;

            if (aocName == null) return s;
            AssetTypeValueField aoc = Find(m, af, AssetClassID.AnimatorOverrideController, aocName);
            if (aoc == null) return s + " | no AnimatorOverrideController named '" + aocName + "'";
            AssetTypeValueField clips = aoc["m_Clips"]["Array"];
            s += " | aoc '" + aocName + "' controller=" + Ptr(aoc["m_Controller"]) +
                 " overrides=" + clips.Children.Count;
            if (clips.Children.Count == 1)
                s += " original=" + Ptr(clips.Children[0]["m_OriginalClip"]) +
                     " override=" + PrefabFields.Name(m, af, clips.Children[0]["m_OverrideClip"]["m_PathID"].AsLong);
            return s;
        }

        /// <summary>
        /// What a baked ANIMATED hierarchy holds in a FILE - the second half of U6-wrote. The
        /// controller is reported by NAME, which a PPtr that resolves to nothing cannot produce.
        /// </summary>
        internal static string HierarchySummary(AssetsManager m, AssetsFileInstance af, string rootName)
        {
            AssetTypeValueField root = PrefabFields.FindGameObject(m, af, rootName);
            if (root == null) return "no GameObject named '" + rootName + "'";
            AssetTypeValueField a = PrefabFields.Component(m, af, root, AssetClassID.Animator);
            if (a == null) return "root '" + rootName + "' has no Animator";
            AssetTypeValueField tf = PrefabFields.Component(m, af, root, AssetClassID.Transform);
            AssetTypeValueField children = tf["m_Children"]["Array"];
            if (children.Children.Count != 1)
                return "root '" + rootName + "' has " + children.Children.Count + " children, expected 1";
            AssetTypeValueField boneTf = PrefabFields.Get(m, af, children.Children[0]["m_PathID"].AsLong);
            if (boneTf == null) return "root '" + rootName + "' m_Children[0] resolves to nothing";

            return "root '" + rootName + "' animator controller=" +
                   PrefabFields.Name(m, af, a["m_Controller"]["m_PathID"].AsLong) +
                   " avatar=" + a["m_Avatar"]["m_PathID"].AsLong +
                   " culling=" + a["m_CullingMode"].AsInt +
                   " hierarchy=" + a["m_HasTransformHierarchy"].AsBool +
                   " | bone=" + PrefabFields.Name(m, af, boneTf["m_GameObject"]["m_PathID"].AsLong) +
                   " rest=" + PrefabFields.V(boneTf["m_LocalPosition"]);
        }

        // ------------------------------------------------- editing a SHIPPED clip's curves

        /// <summary>
        /// How many floats one genericBindings attribute eats out of the flat curve index.
        /// 1/2/3 are the three Transform attributes, MEASURED (see the class remark). Everything
        /// else - the muscle/float/PPtr bindings a weapon clip also carries - is taken as ONE, which
        /// is not a guess left standing: <see cref="MapCurves"/> adds every width up and REFUSES the
        /// clip unless the total is exactly the number of floats its three banks hold.
        /// </summary>
        private static int CurveWidth(uint attribute)
        {
            return attribute == AttributePosition ? PositionCurves
                 : attribute == AttributeRotation ? 4
                 : attribute == AttributeScale ? PositionCurves : 1;
        }

        /// <summary>
        /// Walks every float of ONE attribute's curves in a clip, in flat curve order, writing
        /// <paramref name="map"/> back over each and returning what was there BEFORE. One walk, so
        /// reading a clip's curves (<c>map = v =&gt; v</c>) and editing them (<c>v =&gt; v * k</c>)
        /// can never disagree about which floats the curve is.
        ///
        /// All THREE banks, because a shipped clip does not use the one a baked clip does: measured
        /// 2026-08-12 on `aln_fireworm`'s 'Fireworm_unfurl' (47 streamed curves + 53 constant floats,
        /// dense EMPTY) and `px_equipment`'s 'MV_RocketJumpIdle' (40 constant floats, nothing else) -
        /// an editor that only knew the dense bank would silently change nothing on either.
        ///  - STREAMED is a uint array of frames: {float time; int keyCount; keyCount x
        ///    {int curveIndex; float coeff0..3}}. Parsed and re-serialised here; the parse is
        ///    pinned by consuming the array EXACTLY (999/999 uints, 7 frames on 'Fireworm_unfurl').
        ///    A key is a CUBIC in those four coefficients, and evaluation is linear in them, so a
        ///    factor applied to all four is that factor on the sampled value at every time.
        ///  - DENSE is frame-major: sample[frame * curveCount + curve].
        ///  - CONSTANT is one float per curve, no time.
        /// m_ValueArrayDelta carries the same curve's start/stop value and is mapped with it (its
        /// length is one per flat curve on every clip measured); m_MuscleClipSize is a function of
        /// the bank SIZES, none of which this changes.
        ///
        /// ponytail: the caller supplies one float-&gt;float function, so this can only reshape values
        /// a curve already has - it cannot add or remove a key, retime one, or introduce a curve for
        /// a bone the clip never bound. Authoring curves from a file is an importer, not this.
        /// </summary>
        /// <param name="attribute">1 position, 2 rotation, 3 scale - see the constants.</param>
        /// <param name="how">one line naming what was walked, for the bake log.</param>
        /// <returns>the ORIGINAL value of every float walked, in walk order.</returns>
        internal static List<float> MapCurves(AssetTypeValueField clip, uint attribute,
                                              Func<float, float> map, out string how)
        {
            if (clip == null) throw new ArgumentNullException(nameof(clip));
            if (map == null) throw new ArgumentNullException(nameof(map));

            AssetTypeValueField muscle = clip["m_MuscleClip"];
            AssetTypeValueField data = muscle["m_Clip"]["data"];
            AssetTypeValueField streamedArray = data["m_StreamedClip"]["data"]["Array"];
            int streamedCurves = (int)data["m_StreamedClip"]["curveCount"].AsUInt;
            AssetTypeValueField dense = data["m_DenseClip"];
            int denseCurves = (int)dense["m_CurveCount"].AsUInt;
            int denseFrames = dense["m_FrameCount"].AsInt;
            AssetTypeValueField denseArray = dense["m_SampleArray"]["Array"];
            AssetTypeValueField constArray = data["m_ConstantClip"]["data"]["Array"];
            int curves = streamedCurves + denseCurves + constArray.Children.Count;

            bool[] want = new bool[curves];
            int flat = 0, bindings = 0, wanted = 0;
            foreach (AssetTypeValueField b in clip["m_ClipBindingConstant"]["genericBindings"]["Array"].Children)
            {
                uint a = b["attribute"].AsUInt;
                int width = CurveWidth(a);
                if (a == attribute)
                    for (int i = 0; i < width && flat + i < curves; i++) { want[flat + i] = true; wanted++; }
                if (a == attribute) bindings++;
                flat += width;
            }
            // The one assumption above, checked against the clip's own data instead of trusted: if
            // the widths did not add up, every index past the first odd binding would be off and the
            // edit would land on somebody else's curve. Refuse, rather than write that.
            if (flat != curves)
                throw new InvalidDataException(
                    "clip '" + clip["m_Name"].AsString + "': its bindings account for " + flat +
                    " curve float(s) but its banks hold " + curves + " (" + streamedCurves +
                    " streamed + " + denseCurves + " dense + " + constArray.Children.Count +
                    " constant), so a curve cannot be told from its neighbour and nothing is edited");

            List<float> was = new List<float>();
            int streamedKeys = MapStreamed(streamedArray, want, map, was);
            int denseTouched = 0;
            for (int c = 0; c < denseCurves; c++)
            {
                if (!Wanted(want, streamedCurves + c)) continue;
                denseTouched++;
                for (int f = 0; f < denseFrames; f++)
                {
                    int at = f * denseCurves + c;
                    if (at >= denseArray.Children.Count) break;
                    was.Add(denseArray.Children[at].AsFloat);
                    denseArray.Children[at].AsFloat = map(was[was.Count - 1]);
                }
            }
            int constTouched = 0;
            for (int c = 0; c < constArray.Children.Count; c++)
            {
                if (!Wanted(want, streamedCurves + denseCurves + c)) continue;
                constTouched++;
                was.Add(constArray.Children[c].AsFloat);
                constArray.Children[c].AsFloat = map(was[was.Count - 1]);
            }

            AssetTypeValueField delta = muscle["m_ValueArrayDelta"]["Array"];
            int deltas = 0;
            if (delta.Children.Count == curves)
                for (int c = 0; c < curves; c++)
                {
                    if (!want[c]) continue;
                    deltas++;
                    delta.Children[c]["m_Start"].AsFloat = map(delta.Children[c]["m_Start"].AsFloat);
                    delta.Children[c]["m_Stop"].AsFloat = map(delta.Children[c]["m_Stop"].AsFloat);
                }

            how = "attribute " + attribute + ": " + bindings + " binding(s), " + wanted + " of " +
                  curves + " curve float(s) - " + streamedKeys + " streamed key(s), " + denseTouched +
                  " dense curve(s) x " + denseFrames + " frame(s), " + constTouched +
                  " constant value(s), " + deltas + " delta(s); " + was.Count + " float(s) walked";
            return was;
        }

        /// <summary>
        /// The streamed bank, parsed frame by frame and written back in place. Returns how many KEYS
        /// were touched. A malformed bank (one whose frames do not consume the array exactly) is
        /// refused rather than half-edited - the same reason <see cref="MapCurves"/> checks the widths.
        /// </summary>
        private static bool Wanted(bool[] want, int curve)
        {
            return curve >= 0 && curve < want.Length && want[curve];
        }

        private static int MapStreamed(AssetTypeValueField array, bool[] want,
                                       Func<float, float> map, List<float> was)
        {
            int count = array.Children.Count;
            if (count == 0) return 0;
            int at = 0, keys = 0;
            while (at + 2 <= count)
            {
                int keyCount = (int)array.Children[at + 1].AsUInt;
                at += 2;
                if (keyCount < 0 || at + keyCount * 5 > count)
                    throw new InvalidDataException(
                        "the streamed curve bank does not parse: a frame claims " + keyCount +
                        " key(s) at uint " + at + " of " + count + ", so nothing is edited");
                for (int k = 0; k < keyCount; k++)
                {
                    int curve = (int)array.Children[at + k * 5].AsUInt;
                    if (!Wanted(want, curve)) continue;
                    keys++;
                    for (int c = 1; c <= 4; c++)
                    {
                        AssetTypeValueField f = array.Children[at + k * 5 + c];
                        float v = BitConverter.ToSingle(BitConverter.GetBytes(f.AsUInt), 0);
                        was.Add(v);
                        f.AsUInt = BitConverter.ToUInt32(BitConverter.GetBytes(map(v)), 0);
                    }
                }
                at += keyCount * 5;
            }
            if (at != count)
                throw new InvalidDataException(
                    "the streamed curve bank does not parse: its frames consume " + at + " of " +
                    count + " uint(s), so nothing is edited");
            return keys;
        }

        // ---------------------------------------------------------------- internals

        /// <summary>
        /// Every <c>xform</c> in the subtree set to the identity - t 0, q (0,0,0,1), s (1,1,1).
        /// A ClipMuscleConstant holds fourteen of them (the delta pose's root, four goals, two hand
        /// grabs, and the four start/stop xforms), all identity on every shipped clip, and the empty
        /// template leaves every one of them all-zero.
        /// </summary>
        private static void IdentityXforms(AssetTypeValueField f)
        {
            if (f.TypeName == "xform")
            {
                PrefabFields.Vector3(f["t"], 0f, 0f, 0f);
                AssetTypeValueField q = f["q"];
                q["x"].AsFloat = 0f; q["y"].AsFloat = 0f; q["z"].AsFloat = 0f; q["w"].AsFloat = 1f;
                PrefabFields.Vector3(f["s"], 1f, 1f, 1f);
                return;
            }
            if (f.Children == null) return;
            foreach (AssetTypeValueField c in f.Children) IdentityXforms(c);
        }

        private static AssetTypeValueField Find(AssetsManager m, AssetsFileInstance af,
                                                AssetClassID cls, string name)
        {
            foreach (AssetFileInfo i in af.file.Metadata.GetAssetsOfType(cls))
            {
                AssetTypeValueField f = m.GetBaseField(af, i);
                if (!f["m_Name"].IsDummy && f["m_Name"].AsString == name) return f;
            }
            return null;
        }

        private static AssetTypeValueField Int(AssetTypeValueField array, int value)
        {
            AssetTypeValueField e = ValueBuilder.DefaultValueFieldFromArrayTemplate(array);
            e.AsInt = value;
            return e;
        }

        private static AssetTypeValueField UInt(AssetTypeValueField array, uint value)
        {
            AssetTypeValueField e = ValueBuilder.DefaultValueFieldFromArrayTemplate(array);
            e.AsUInt = value;
            return e;
        }

        private static AssetTypeValueField Float(AssetTypeValueField array, float value)
        {
            AssetTypeValueField e = ValueBuilder.DefaultValueFieldFromArrayTemplate(array);
            e.AsFloat = value;
            return e;
        }

        private static string Ptr(AssetTypeValueField p)
        {
            return "fileID" + p["m_FileID"].AsInt + "/pathID" + p["m_PathID"].AsLong;
        }

        /// <summary>
        /// An order-sensitive fingerprint of a whole binding list - one number a gate can predict from
        /// the IMPORT and compare against the FILE. CRC-32 is reused rather than a second hash: it is
        /// already in this assembly for the bone paths (<see cref="SkinFields.BoneHash"/>).
        /// </summary>
        internal static uint Sig(IList<string> pairs)
        {
            string[] a = new string[pairs.Count];
            pairs.CopyTo(a, 0);
            return SkinFields.BoneHash(string.Join("|", a));
        }

        /// <summary>One binding, in the ONE spelling both sides of <see cref="Sig"/> use.</summary>
        internal static string Pair(uint path, uint attribute)
        {
            return path.ToString(CultureInfo.InvariantCulture) + ":" +
                   attribute.ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>The same fingerprint, from the bindings a bake is about to write.</summary>
        internal static uint Sig(IList<Binding> bindings)
        {
            List<string> pairs = new List<string>();
            foreach (Binding b in bindings) pairs.Add(Pair(SkinFields.BoneHash(b.BonePath), b.Attribute));
            return Sig(pairs);
        }

        /// <summary>The first three floats of one dense frame, read back out of the sample array.</summary>
        private static string Triple(AssetTypeValueField samples, int frame, int curveCount)
        {
            int at = frame * curveCount;
            if (frame < 0 || curveCount < PositionCurves || at + PositionCurves > samples.Children.Count)
                return "(out of range)";
            return "(" + F(samples.Children[at].AsFloat) + "," + F(samples.Children[at + 1].AsFloat) +
                   "," + F(samples.Children[at + 2].AsFloat) + ")";
        }

        // InvariantCulture: this line is machine-compared and a ru-RU machine writes 0,5 for 0.5
        // (the trap MeshFields.V and ReadMaterialProperties both document).
        private static string F(float v) => v.ToString("0.###", CultureInfo.InvariantCulture);
    }
}
