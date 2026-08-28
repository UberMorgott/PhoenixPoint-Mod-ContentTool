using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Morgott.ContentTool.Bake;
using Morgott.ContentTool.Import;

/// <summary>
/// Gate <b>U8</b>: the animation clips a .glb already carries, read into the internal clip
/// representation the bake consumes - per bone, per frame, on a uniform grid, in UNITY space.
///
/// Two independent halves, because a round trip can only prove the writer and the reader agree with
/// each other (<see cref="GlbReader"/>'s own class remark: every coordinate rule is an INVOLUTION, so
/// both halves being wrong together survives a round trip):
///
///  - <b>hand-built glTF-space bytes.</b> <see cref="Hand"/> writes a .glb whose numbers are stated
///    HERE, in glTF's own right-handed space, with no line of our writer in the loop. The reader has
///    to produce the Unity numbers this file predicts: X negated on a translation, Y and Z negated on
///    a rotation, scale untouched. Getting the quaternion rule from the vector rule - the classic way
///    to mirror an animation while the mesh stays right - fails here and nowhere else.
///  - <b>a REAL .glb</b> (<c>lib\u8_probe.glb</c>, Quaternius' CC0 spider, see its -SOURCE.md): 39
///    bones, 5 clips, keyframe-reduced 24 fps, per-sampler time arrays - none of which our own
///    exporter produces, and all of which a real download does.
///
/// The join U7 established is asserted, not assumed: a track's bone is a JOINT SLOT, so its path is
/// <c>BakedSkin.BonePath</c>'s and the CRC a curve binds is the one the MESH already carries in
/// m_BoneNameHashes. A clip whose curves bind paths the rig does not spell drives nothing, silently.
/// </summary>
internal static class ClipImport
{
    private const string Probe = "u8_probe.glb";

    internal static string Run()
    {
        string hand = HandBuilt();
        string grids = Grids();
        string real = RealFile();
        return "CLIP import PASS\n  " + hand + "\n  " + grids + "\n  " + real;
    }

    // ------------------------------------------------------------------ the FALLBACK grid

    /// <summary>
    /// The path no real file has ever taken here: key times that land on NO whole rate in [1, 120] Hz,
    /// so the clip falls back to 30 Hz and the last key does not sit on a frame.
    ///
    /// Such a fixture is harder to build than it looks, and the reason is Dirichlet's approximation
    /// theorem: for any single time t there is always a whole r &lt;= 120 with |t*r - round(t*r)| &lt;
    /// 1/121 = 0.00826, inside the 0.01 frame this reader calls "on the grid". So ONE key time can
    /// never miss - measured, by sweeping every t in [0.5, 0.9] at 1e-5 and finding zero misses. It
    /// takes TWO times that no single rate serves at once; 0.1 s and 0.511 s are such a pair (swept
    /// r = 1..120, none fits both). That is why all five clips of the real probe snap at 24 Hz and
    /// this arm exists at all.
    ///
    /// 0.511 s x 30 Hz = 15.33 frames. Rounding it puts the clip's last frame at 0.5 s and throws the
    /// tail away; a ceiling puts it at 0.5333 s, past the last key, which is what a resampler owes.
    /// </summary>
    private static string Grids()
    {
        var clips = new List<SampledClip>();
        GlbReader.Read(Timed("LINEAR", 0f, 0.1f, 0.511f), clips);
        SampledClip clip = clips[0];
        Assert(Near(clip.SampleRate, 30f) && clip.LossyReason.Contains("fit no rate up to 120 Hz"),
               "key times 0 / 0.1 / 0.511 s land on no whole rate, so the clip falls back to 30 Hz: " +
               F(clip.SampleRate) + " Hz (" + clip.LossyReason + ")");
        float last = clip.Times[clip.Times.Length - 1];
        Assert(clip.Times.Length == 17 && Near(last, 16f / 30f) && last >= 0.511f,
               "and the grid REACHES the last key: " + clip.Times.Length + " frame(s), last at " +
               F(last) + " s vs the file's last key at 0.511 s");
        // Anti-vacuity, two ways. (a) The value at that last frame is the HELD end value, not something
        // invented past the curve - a grid that merely got longer would show up here. (b) The five
        // snapped clips of the real probe below still come back at exactly their old frame counts
        // (101 for the 4.167 s idle), so a ceiling that fires on a snapped clip too would fail there.
        SampledTrack track = clip.Tracks[0];
        Assert(Near(track.Translations[clip.Times.Length - 1].X, -3f) &&
               Near(track.Translations[0].X, -1f),
               "the frame past the last key HOLDS the end value: " +
               F(track.Translations[clip.Times.Length - 1].X));

        // A finite but enormous timestamp: (int)Math.Round(1e19) does not throw in C#, it wraps to
        // int.MinValue, and +1 then falls through a "frames < 2" floor into a silent two-frame clip.
        // The count is compared as a double, before any cast, so the file is REFUSED by name instead.
        string refusal = Refusal(Timed("LINEAR", 0f, 1e19f));
        Assert(refusal.Contains("frames this mod will read") && refusal.Contains("10000"),
               "a 1e19 s key time is refused by the frame ceiling, not wrapped past it: " + refusal);

        return "fallback grid: 0/0.1/0.511 s -> " + F(clip.SampleRate) + " Hz x " + clip.Times.Length +
               " frame(s), last frame " + F(last) + " s >= last key 0.511 s (rounding ends at 0.5) | " +
               "1e19 s key refused by the " + "10000-frame ceiling";
    }

    /// <summary>
    /// The same rig as <see cref="Hand"/> with ONE translation channel, whose key times are the
    /// argument: x = 1, 2, 3 ... per key in glTF space, so Unity reads -1, -2, -3.
    /// </summary>
    private static byte[] Timed(string interpolation, params float[] keys)
    {
        var b = new Bin();
        int position = b.Vec(3, "VEC3", 0f, 0f, 0f, 1f, 0f, 0f, 0f, 2f, 0f);
        int normal = b.Vec(3, "VEC3", 0f, 0f, -1f, 0f, 0f, -1f, 0f, 0f, -1f);
        int joints = b.Joints(0, 0, 0, 0, 1, 0, 0, 0, 1, 0, 0, 0);
        int weights = b.Vec(3, "VEC4", 1f, 0f, 0f, 0f, 1f, 0f, 0f, 0f, 1f, 0f, 0f, 0f);
        int indices = b.Indices(0, 1, 2);
        int bind = b.Vec(2, "MAT4",
            1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, -1, 0, 1,
            1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, -4, 0, 1);
        int times = b.Vec(keys.Length, "SCALAR", keys);
        var moves = new float[keys.Length * 3];
        for (int i = 0; i < keys.Length; i++) moves[i * 3] = i + 1;
        int move = b.Vec(keys.Length, "VEC3", moves);

        string json =
            "{\"asset\":{\"version\":\"2.0\"}," +
            "\"scenes\":[{\"nodes\":[0,3]}],\"scene\":0," +
            "\"nodes\":[" +
              "{\"name\":\"rig\",\"children\":[1]}," +
              "{\"name\":\"hip\",\"children\":[2],\"translation\":[0,1,0]}," +
              "{\"name\":\"head\",\"translation\":[0,3,0]}," +
              "{\"name\":\"body\",\"mesh\":0,\"skin\":0}]," +
            "\"skins\":[{\"joints\":[1,2],\"inverseBindMatrices\":" + bind + "}]," +
            "\"meshes\":[{\"name\":\"u8mesh\",\"primitives\":[{\"attributes\":{\"POSITION\":" + position +
              ",\"NORMAL\":" + normal + ",\"JOINTS_0\":" + joints + ",\"WEIGHTS_0\":" + weights +
              "},\"indices\":" + indices + "}]}]," +
            "\"animations\":[{\"name\":\"timed\",\"samplers\":[" +
              Sampler(times, move, interpolation) + "]," +
            "\"channels\":[{\"sampler\":0,\"target\":{\"node\":2,\"path\":\"translation\"}}]}]," +
            b.Json() + "}";
        return Container(json, b.Bytes());
    }

    // ------------------------------------------------------------------ hand-built glTF-space bytes

    /// <summary>
    /// The numbers this fixture states, in glTF space, and what each one has to become:
    ///
    ///   head translation, LINEAR, keys 0 / 0.5 / 1 s at x = 1 / 3 / 5   -> Unity x = -1 .. -5
    ///   head rotation,    LINEAR, keys 0 / 1 s, identity -> +90 deg about glTF +Y
    ///   hip  translation, LINEAR, keys 0 / 0.25 / 1 s at z = 0 / 1 / 4  -> Unity z unchanged
    ///   hip  scale,       LINEAR, keys 0 / 1 s at (1, 2, 3)             -> Unity (1, 2, 3)
    ///   'prop' translation - a node the armature does not list           -> DROPPED
    ///   head 'weights'   - a blend-shape channel                        -> DROPPED
    ///
    /// The 0.25 key is deliberate: it forces the clip's grid to 4 Hz, so the rotation is sampled a
    /// QUARTER of the way along a 90 degree arc. SLERP puts it at 22.5 deg (y = sin 11.25 deg =
    /// 0.195090); a component-wise lerp of the same two quaternions, renormalised, puts it at
    /// 0.187369. Sampling it half way - the obvious choice - would have measured NEITHER, because
    /// nlerp and slerp agree exactly at the midpoint.
    /// </summary>
    private static string HandBuilt()
    {
        var clips = new List<SampledClip>();
        SkinnedModel model = GlbReader.Read(Hand("LINEAR"), clips);
        BakedSkin skin = ModelBuild.From(model, "u8");

        Assert(clips.Count == 1, "the file's one animation came back: " + clips.Count);
        SampledClip clip = clips[0];
        Assert(clip.Name == "hand", "the clip keeps the file's own name: '" + clip.Name + "'");
        // 4 Hz is not written anywhere in the file - it is the coarsest rate every key time of the
        // clip lands on, and the 0.25 s key is the only reason it is not 2.
        Assert(Near(clip.SampleRate, 4f) && clip.Times.Length == 5 && Near(clip.Length, 1f),
               "the grid is the coarsest one the file's own key times land on: " +
               F(clip.SampleRate) + " Hz x " + clip.Times.Length + " frame(s), " + F(clip.Length) + " s");
        Assert(clip.Tracks.Count == 2, "one track per BONE the clip drives, not per channel: " + clip.Tracks.Count);

        SampledTrack hip = Track(clip, skin, "hip"), head = Track(clip, skin, "head");

        // The vector rule, S = diag(-1, 1, 1), and the LINEAR sampling between two keys in one arm:
        // frames 1 and 3 sit between the file's keys and read -2 and -4.
        string ladder = "";
        for (int f = 0; f < 5; f++) ladder += (f == 0 ? "" : ",") + F(head.Translations[f].X);
        Assert(Near(head.Translations[0].X, -1f) && Near(head.Translations[1].X, -2f) &&
               Near(head.Translations[2].X, -3f) && Near(head.Translations[4].X, -5f) &&
               Near(head.Translations[0].Y, 0f) && Near(head.Translations[0].Z, 0f),
               "the head's glTF x = 1..5 comes back as Unity x = -1..-5: " + ladder);

        // The QUATERNION rule, which is not the vector rule: (x, y, z, w) -> (x, -y, -z, w).
        ObjQuaternion quarter = head.Rotations[1], full = head.Rotations[4];
        Assert(Near(full.Y, -0.7071068f) && Near(full.W, 0.7071068f) && Near(full.X, 0f) && Near(full.Z, 0f),
               "the head's +90 deg about glTF +Y comes back mirrored on Y: (" + F(full.X) + "," + F(full.Y) +
               "," + F(full.Z) + "," + F(full.W) + ")");
        Assert(Math.Abs(quarter.Y + 0.1950903f) < 1e-4f,
               "and a quarter of the way along that arc is SLERP's 22.5 deg (y = -0.19509), not " +
               "nlerp's -0.187369: " + quarter.Y.ToString("0.######", CultureInfo.InvariantCulture));

        // Scale crosses the basis unchanged - S*diag(s)*S = diag(s) - so a rule copied from the
        // translation arm would negate x here.
        Assert(Near(hip.Scales[0].X, 1f) && Near(hip.Scales[2].Y, 2f) && Near(hip.Scales[4].Z, 3f),
               "the hip's scale crosses unchanged: (" + F(hip.Scales[2].X) + "," + F(hip.Scales[2].Y) +
               "," + F(hip.Scales[2].Z) + ")");
        // Frame 2 (t = 0.5) lies between the file's 0.25 and 1 s keys: 1 + (0.25/0.75)*3 = 2.
        Assert(Near(hip.Translations[1].Z, 1f) && Near(hip.Translations[2].Z, 2f) && Near(hip.Translations[4].Z, 4f),
               "and its translation is LINEAR between two keys 0.75 s apart: " +
               F(hip.Translations[1].Z) + "," + F(hip.Translations[2].Z) + "," + F(hip.Translations[4].Z));

        // The two DROPPED channels, named rather than silent - a curve the bake has nowhere to put.
        Assert(clip.LossyReason.Contains("1 channel(s) drive something that is not a bone") &&
               clip.LossyReason.Contains("1 blend-shape channel(s) were dropped"),
               "the loss is stated: " + clip.LossyReason);

        // THE JOIN: the track's bone is a joint SLOT, so the path it will be hashed by is the same
        // spelling the mesh already hashed into m_BoneNameHashes.
        string path = skin.BonePath(head.Node);
        Assert(path == "hip/head" && SkinFields.BoneHash(path) == SkinFields.BoneHash(skin.BonePath(head.Node)),
               "a track names a joint slot, so its curve binds the rig's own path: '" + path + "' -> " +
               SkinFields.BoneHash(path));

        // STEP: the sampler HOLDS, and - because a dense bank has no interpolation mode and the runtime
        // ramps between the two frames that bracket a time - the GRID is what bounds the damage. The
        // same file read as LINEAR derives 4 Hz, where the frame before the 0.5 s jump sits at 0.25 s
        // and the hold comes back as a quarter-SECOND ramp. On a STEP clip the rate is raised to the
        // highest whole multiple the mod allows, so the hold survives to one frame before the boundary.
        var stepped = new List<SampledClip>();
        GlbReader.Read(Hand("STEP"), stepped);
        SampledClip stepClip = stepped[0];
        SampledTrack steppedHead = stepClip.Tracks[1].Node == head.Node ? stepClip.Tracks[1] : stepClip.Tracks[0];
        // Every frame reads a key value exactly - the hold is not smeared IN THE BANK. What the runtime
        // cannot do is jump between two of them, so the measurement that matters is WHEN the last
        // held frame sits: 59/120 s, one frame before the file's own 0.5 s key.
        int hold = 0;
        for (int f = 0; f < steppedHead.Translations.Length; f++)
        {
            float x = steppedHead.Translations[f].X;
            Assert(Near(x, -1f) || Near(x, -3f) || Near(x, -5f),
                   "a STEP sampler never interpolates: frame " + f + " reads " + F(x));
            if (Near(x, -1f)) hold = f;
        }
        float ramp = (stepClip.Times[hold + 1] - stepClip.Times[hold]) * 1000f;
        Assert(ramp <= 1000f / 120f + 1e-3f,
               "the ramp the dense bank forces between the last held frame (" + hold + " at " +
               F(stepClip.Times[hold]) + " s) and the file's own 0.5 s jump is " + F(ramp) +
               " ms wide - on the LINEAR clip's own 4 Hz grid it would be 250");
        Assert(hold == 59 && Near(stepClip.SampleRate, 120f) && stepClip.Times.Length == 121,
               "and it is a whole MULTIPLE of the 4 Hz the same file derives as LINEAR, so every key " +
               "still lands on a frame: " + F(stepClip.SampleRate) + " Hz x " + stepClip.Times.Length +
               " frame(s), hold to frame " + hold);
        Assert(stepClip.LossyReason.Contains("ONE frame wide (8.333 ms)"),
               "the bound is STATED, not implied: " + stepClip.LossyReason);

        // CUBICSPLINE is REFUSED by name, with the Blender setting that avoids it. Silence here would
        // be a curve read at a third of its rate with tangents mistaken for values.
        string refusal = Refusal(Hand("CUBICSPLINE"));
        Assert(refusal.Contains("CUBICSPLINE") && refusal.Contains("Always Sample Animations"),
               "CUBICSPLINE is refused by name, with the fix: " + refusal);

        return "hand-built glTF-space .glb: " + clip.Tracks.Count + " track(s) @ " + F(clip.SampleRate) +
               " Hz x " + clip.Times.Length + " frame(s) | head x=" + ladder + " | head rot y=" +
               F(full.Y) + " quarter=" + quarter.Y.ToString("0.######", CultureInfo.InvariantCulture) +
               " | hip scale=(" + F(hip.Scales[2].X) + "," + F(hip.Scales[2].Y) + "," + F(hip.Scales[2].Z) +
               ") | STEP holds " + F(steppedHead.Translations[1].X) + " to frame " + hold + " of " +
               F(stepClip.SampleRate) + " Hz, ramp " + F(ramp) + " ms | CUBICSPLINE refused";
    }

    // ------------------------------------------------------------------ a real downloaded .glb

    private static string RealFile()
    {
        string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"..\..\..\..\..\lib\" + Probe);
        if (!File.Exists(path)) return "real .glb VOID - no " + Path.GetFullPath(path);

        var clips = new List<SampledClip>();
        SkinnedModel model = GlbReader.Read(File.ReadAllBytes(path), clips);
        BakedSkin skin = ModelBuild.From(model, "u8_probe");

        string[] want = { "Spider_Attack", "Spider_Death", "Spider_Idle", "Spider_Jump", "Spider_Walk" };
        Assert(clips.Count == want.Length, "the file's five clips came back: " + clips.Count);
        for (int i = 0; i < want.Length; i++)
            Assert(clips[i].Name == want[i], "clip " + i + " keeps the file's name: '" + clips[i].Name + "'");

        string lines = "";
        float moved = 0f;
        foreach (SampledClip clip in clips)
        {
            // 24 Hz is the file's own authoring rate, DERIVED: no single channel of these clips is
            // uniformly spaced (Blender drops the keys that do not change), so a rate read off one
            // channel would differ per channel. Every key still lands on a 1/24 s frame.
            Assert(Near(clip.SampleRate, 24f), "clip '" + clip.Name + "' snaps to the file's own 24 fps: " +
                   F(clip.SampleRate) + " Hz (" + clip.LossyReason + ")");
            Assert(clip.LossyReason.Contains("which every key time of the clip lands on"),
                   "clip '" + clip.Name + "' needs no interpolation at all: " + clip.LossyReason);
            Assert(clip.Times.Length >= 2 && Near(clip.Length, (clip.Times.Length - 1) / clip.SampleRate),
                   "clip '" + clip.Name + "' length agrees with its own grid: " + F(clip.Length));
            Assert(clip.Tracks.Count > 0, "clip '" + clip.Name + "' drives at least one bone");

            foreach (SampledTrack track in clip.Tracks)
            {
                Assert(track.Node >= 0 && track.Node < skin.BoneNames.Length,
                       "clip '" + clip.Name + "' names bone slot " + track.Node + " of " + skin.BoneNames.Length);
                Assert(Length(track, clip.Times.Length),
                       "every channel of clip '" + clip.Name + "' covers every frame");
                if (track.Rotations != null)
                    foreach (ObjQuaternion q in track.Rotations)
                        Assert(Math.Abs(Math.Sqrt((double)q.X * q.X + (double)q.Y * q.Y +
                                                  (double)q.Z * q.Z + (double)q.W * q.W) - 1.0) < 1e-4,
                               "clip '" + clip.Name + "' bone '" + skin.BoneNames[track.Node] +
                               "' keeps unit rotations");
                if (track.Translations == null) continue;
                for (int f = 1; f < track.Translations.Length; f++)
                {
                    float d = Math.Abs(track.Translations[f].X - track.Translations[0].X) +
                              Math.Abs(track.Translations[f].Y - track.Translations[0].Y) +
                              Math.Abs(track.Translations[f].Z - track.Translations[0].Z);
                    if (d > moved) moved = d;
                }
            }
            lines += (lines.Length == 0 ? "" : "\n  ") + "'" + clip.Name + "' " + clip.Tracks.Count +
                     " bone(s) @ " + F(clip.SampleRate) + " Hz x " + clip.Times.Length + " frame(s), " +
                     F(clip.Length) + " s";
        }

        // Anti-vacuity: a reader that produced the REST pose at every frame would satisfy every
        // arm above. Something has to actually move.
        Assert(moved > 0.001f, "at least one bone actually MOVES across its clip: " + F(moved));

        // The join again, on a rig whose joint ORDER is the file's and whose bones are carried.
        SampledTrack carried = null;
        SampledClip walk = clips[4];
        foreach (SampledTrack t in walk.Tracks) if (skin.BoneParents[t.Node] >= 0) { carried = t; break; }
        Assert(carried != null, "the walk clip drives a CARRIED bone, whose path is not just its name");
        string bonePath = skin.BonePath(carried.Node);
        Assert(bonePath.Contains("/") && bonePath.EndsWith("/" + skin.BoneNames[carried.Node], StringComparison.Ordinal),
               "and that bone's curve binds the rig's own path: '" + bonePath + "' -> " +
               SkinFields.BoneHash(bonePath));

        return "real .glb " + Probe + ": " + skin.BoneNames.Length + " bones, " + clips.Count + " clip(s)\n  " +
               lines + "\n  furthest a bone travels = " + F(moved) + " | 'Spider_Walk' binds '" + bonePath +
               "' -> " + SkinFields.BoneHash(bonePath);
    }

    // ------------------------------------------------------------------ the hand-built file

    /// <summary>
    /// A whole .glb assembled here, byte by byte, in glTF's own space. Nothing in
    /// <see cref="GlbCodec"/> takes part, which is the entire point: the writer and the reader share
    /// every coordinate rule, and each of those rules is its own inverse.
    /// </summary>
    private static byte[] Hand(string interpolation)
    {
        var b = new Bin();
        // --- mesh: one triangle, weighted whole to bone 0 then bone 1.
        int position = b.Vec(3, "VEC3", 0f, 0f, 0f, 1f, 0f, 0f, 0f, 2f, 0f);
        int normal = b.Vec(3, "VEC3", 0f, 0f, -1f, 0f, 0f, -1f, 0f, 0f, -1f);
        int joints = b.Joints(0, 0, 0, 0, 1, 0, 0, 0, 1, 0, 0, 0);
        int weights = b.Vec(3, "VEC4", 1f, 0f, 0f, 0f, 1f, 0f, 0f, 0f, 1f, 0f, 0f, 0f);
        int indices = b.Indices(0, 1, 2);
        // --- the bind poses: hip rests 1 up, head 4 up, so their INVERSES are -1 and -4.
        int bind = b.Vec(2, "MAT4",
            1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, -1, 0, 1,
            1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, -4, 0, 1);
        // --- the curves, stated in glTF space.
        int headTimes = b.Vec(3, "SCALAR", 0f, 0.5f, 1f);
        int headMove = b.Vec(3, "VEC3", 1f, 0f, 0f, 3f, 0f, 0f, 5f, 0f, 0f);
        int turnTimes = b.Vec(2, "SCALAR", 0f, 1f);
        int turn = b.Vec(2, "VEC4", 0f, 0f, 0f, 1f, 0f, 0.7071068f, 0f, 0.7071068f);
        int hipTimes = b.Vec(3, "SCALAR", 0f, 0.25f, 1f);
        int hipMove = b.Vec(3, "VEC3", 0f, 0f, 0f, 0f, 0f, 1f, 0f, 0f, 4f);
        int hipScale = b.Vec(2, "VEC3", 1f, 2f, 3f, 1f, 2f, 3f);

        string json =
            "{\"asset\":{\"version\":\"2.0\"}," +
            "\"scenes\":[{\"nodes\":[0,3,4]}],\"scene\":0," +
            "\"nodes\":[" +
              "{\"name\":\"rig\",\"children\":[1]}," +
              "{\"name\":\"hip\",\"children\":[2],\"translation\":[0,1,0]}," +
              "{\"name\":\"head\",\"translation\":[0,3,0]}," +
              "{\"name\":\"body\",\"mesh\":0,\"skin\":0}," +
              "{\"name\":\"prop\"}]," +
            "\"skins\":[{\"joints\":[1,2],\"inverseBindMatrices\":" + bind + "}]," +
            "\"meshes\":[{\"name\":\"u8mesh\",\"primitives\":[{\"attributes\":{\"POSITION\":" + position +
              ",\"NORMAL\":" + normal + ",\"JOINTS_0\":" + joints + ",\"WEIGHTS_0\":" + weights +
              "},\"indices\":" + indices + "}]}]," +
            "\"animations\":[{\"name\":\"hand\",\"samplers\":[" +
              Sampler(headTimes, headMove, interpolation) + "," +      // 0 head translation
              Sampler(turnTimes, turn, "LINEAR") + "," +               // 1 head rotation
              Sampler(hipTimes, hipMove, "LINEAR") + "," +             // 2 hip translation
              Sampler(turnTimes, hipScale, "LINEAR") + "," +           // 3 hip scale
              Sampler(turnTimes, hipMove, "LINEAR") + "," +            // 4 the prop, dropped
              Sampler(turnTimes, hipScale, "LINEAR") + "]," +          // 5 blend shapes, dropped
            "\"channels\":[" +
              "{\"sampler\":0,\"target\":{\"node\":2,\"path\":\"translation\"}}," +
              "{\"sampler\":1,\"target\":{\"node\":2,\"path\":\"rotation\"}}," +
              "{\"sampler\":2,\"target\":{\"node\":1,\"path\":\"translation\"}}," +
              "{\"sampler\":3,\"target\":{\"node\":1,\"path\":\"scale\"}}," +
              "{\"sampler\":4,\"target\":{\"node\":4,\"path\":\"translation\"}}," +
              "{\"sampler\":5,\"target\":{\"node\":2,\"path\":\"weights\"}}]}]," +
            b.Json() + "}";
        return Container(json, b.Bytes());
    }

    // internal, not private: ClipPlan assembles its own multi-animation .glb out of the SAME three
    // pieces, so the container/accessor spelling this gate proves is the one that fixture uses too.
    internal static string Sampler(int input, int output, string interpolation) =>
        "{\"input\":" + input + ",\"output\":" + output + ",\"interpolation\":\"" + interpolation + "\"}";

    /// <summary>The BIN chunk, plus the bufferView/accessor tables that name its blobs.</summary>
    internal sealed class Bin
    {
        private readonly List<byte> bytes = new List<byte>();
        private readonly List<string> views = new List<string>();
        private readonly List<string> accessors = new List<string>();

        internal int Vec(int count, string type, params float[] values)
        {
            int at = Start();
            foreach (float v in values) bytes.AddRange(BitConverter.GetBytes(v));
            return Add(at, count, type, 5126);          // FLOAT
        }

        internal int Joints(params int[] values)
        {
            int at = Start();
            foreach (int v in values) bytes.AddRange(BitConverter.GetBytes((ushort)v));
            return Add(at, values.Length / 4, "VEC4", 5123);   // UNSIGNED_SHORT
        }

        internal int Indices(params int[] values)
        {
            int at = Start();
            foreach (int v in values) bytes.AddRange(BitConverter.GetBytes((ushort)v));
            return Add(at, values.Length, "SCALAR", 5123);
        }

        /// <summary>Every blob starts 4-byte aligned, which is what the reader's alignment check wants.</summary>
        private int Start()
        {
            while (bytes.Count % 4 != 0) bytes.Add(0);
            return bytes.Count;
        }

        private int Add(int at, int count, string type, int component)
        {
            views.Add("{\"buffer\":0,\"byteOffset\":" + at + ",\"byteLength\":" + (bytes.Count - at) + "}");
            accessors.Add("{\"bufferView\":" + (views.Count - 1) + ",\"componentType\":" + component +
                          ",\"count\":" + count + ",\"type\":\"" + type + "\"}");
            return accessors.Count - 1;
        }

        internal byte[] Bytes()
        {
            while (bytes.Count % 4 != 0) bytes.Add(0);
            return bytes.ToArray();
        }

        internal string Json() =>
            "\"bufferViews\":[" + string.Join(",", views.ToArray()) + "]," +
            "\"accessors\":[" + string.Join(",", accessors.ToArray()) + "]," +
            "\"buffers\":[{\"byteLength\":" + ((bytes.Count + 3) / 4 * 4) + "}]";
    }

    /// <summary>The 12-byte glTF header plus a JSON and a BIN chunk, both padded to four.</summary>
    internal static byte[] Container(string json, byte[] bin)
    {
        byte[] text = Encoding.UTF8.GetBytes(json);
        int pad = (4 - text.Length % 4) % 4;
        int total = 12 + 8 + text.Length + pad + (bin.Length == 0 ? 0 : 8 + bin.Length);
        var file = new List<byte>(total);
        file.AddRange(BitConverter.GetBytes(0x46546C67u));      // "glTF"
        file.AddRange(BitConverter.GetBytes(2u));
        file.AddRange(BitConverter.GetBytes((uint)total));
        file.AddRange(BitConverter.GetBytes((uint)(text.Length + pad)));
        file.AddRange(BitConverter.GetBytes(0x4E4F534Au));      // "JSON"
        file.AddRange(text);
        for (int i = 0; i < pad; i++) file.Add((byte)' ');
        if (bin.Length > 0)
        {
            file.AddRange(BitConverter.GetBytes((uint)bin.Length));
            file.AddRange(BitConverter.GetBytes(0x004E4942u));  // "BIN"
            file.AddRange(bin);
        }
        return file.ToArray();
    }

    // ------------------------------------------------------------------ helpers

    private static SampledTrack Track(SampledClip clip, BakedSkin skin, string bone)
    {
        foreach (SampledTrack t in clip.Tracks) if (skin.BoneNames[t.Node] == bone) return t;
        throw new Exception("CLIP import FAILED: the clip drives no bone named '" + bone + "'");
    }

    private static bool Length(SampledTrack track, int frames)
    {
        return (track.Translations == null || track.Translations.Length == frames) &&
               (track.Rotations == null || track.Rotations.Length == frames) &&
               (track.Scales == null || track.Scales.Length == frames);
    }

    /// <summary>The message a refusal carries, so the arm can assert the CAUSE and not merely a throw.</summary>
    private static string Refusal(byte[] file)
    {
        try
        {
            GlbReader.Read(file, new List<SampledClip>());
            return "(no refusal at all)";
        }
        catch (FormatException exception) { return exception.Message; }
    }

    private static bool Near(float a, float b) { return Math.Abs(a - b) < 1e-4f; }

    // InvariantCulture: these lines are machine-compared and a ru-RU machine writes 0,5 for 0.5.
    private static string F(float v) => v.ToString("0.###", CultureInfo.InvariantCulture);

    private static void Assert(bool ok, string what)
    {
        if (!ok) throw new Exception("CLIP import FAILED: " + what);
    }
}
