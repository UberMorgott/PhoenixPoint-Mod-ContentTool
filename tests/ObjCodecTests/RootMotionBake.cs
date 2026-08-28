using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using Morgott.ContentTool.Bake;
using Morgott.ContentTool.Import;

/// <summary>
/// Gate <b>U13</b>: a downloaded clip that WALKS ON THE SPOT bakes with real root motion, and one that
/// already travels is left alone.
///
/// The oracle is the ENGINE'S OWN MEASUREMENT, spelled here the way <c>AnimationInfos</c> spells it and
/// not the way <see cref="Treadmill"/> computes it:
///   AnimationInfos.cs:104-110  SampleAnimation(0) / SampleAnimation(clip.length) on the RootMotionNode
///                              -&gt; Offset in the animator root's own space
///   AnimationInfos.cs:121-123  Speed = |Offset| / clip.length
/// The RootMotionNode is the armature root, whose parent IS the animator's GameObject, so its
/// localPosition at a frame is exactly that space - which is why this arm can read the BAKED binding's
/// own floats at frame 0 and frame N-1 and get the number the game will get. clip.length is
/// <c>(frames - 1) / sampleRate</c>, the m_StopTime <c>ClipFields.FillClip</c> writes.
///
/// THE ANTI-VACUITY TRAP, and how this arm avoids it: the fixture's walk clip LOOPS, so every bone it
/// drives returns to its starting pose and a first-versus-last reading is structurally ZERO for all of
/// them. An arm built on that metric would measure nothing and say PASS forever. So:
///  - the CONTROL below asserts that every non-root bone of the walk clip reads ~0 first-versus-last -
///    i.e. that the naive metric really is dead on this file, which is what makes the root's non-zero
///    reading mean something;
///  - and <see cref="Treadmill"/> itself never reads first-versus-last. It reads PER-FRAME velocities
///    of whatever is touching the ground and takes their median, which a closed cycle does not cancel.
///
/// A missing probe is VOID, never PASS.
/// </summary>
internal static class RootMotionBake
{
    private const string Probe = "u8_probe.glb";
    private const string Root = "spider";
    private const string Walk = "Spider_Walk";

    /// <summary>A baked ramp has to be worth measuring: at least this much of the rig's own height
    /// travelled over one cycle. Below it the "speed" would be numerical noise dressed as locomotion.
    /// </summary>
    private const float SaneTravelOfHeight = 0.5f;

    /// <summary>
    /// THE ABSOLUTE BAND, and it is absolute on purpose. Everything else in this gate is scale-free -
    /// travel against the rig's own height, ground slide against contact time - and a scale-free arm
    /// cannot see a SCALE bug, because both of its sides move together. That is exactly how a ramp
    /// written in the file's units (101.93) instead of the game's (0.51) reached the game and made the
    /// spider teleport across the map in one frame.
    ///
    /// The unit is the game's own: <c>TacticalMap.cs:67 public const float TileSize = 1f</c>, so a world
    /// unit IS a tile, and <c>AnimationInfos.cs:123</c>'s Speed is spent as world units per second by
    /// <c>TacticalNavigationComponent.cs:376</c>. The calibration point is MEASURED, not invented: the
    /// soldier run loop 'MV_RunFwd_Loop_AR' in _common_assets_all.bundle translates 'BaseManReference'
    /// by 2.894980 over 0.5333 s = 5.43 tile/s.
    /// </summary>
    private const float ShippedRunSpeed = 5.43f;
    /// <summary>A creature that crosses more than this many shipped-run-speeds is not walking.</summary>
    private const float FastestSane = 3f;
    /// <summary>And below this it would take a whole minute to cross a small room.</summary>
    private const float SlowestSane = 0.05f;
    /// <summary>A tactical actor stands between a tenth of a tile and ten tiles tall. Wider than any
    /// creature the game ships, and still 200x away from an unscaled import.</summary>
    private const float ShortestSane = 0.1f, TallestSane = 10f;

    private static int checks;

    internal static string Run()
    {
        checks = 0;
        string probe = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"..\..\..\..\..\lib\" + Probe);
        if (!File.Exists(probe)) return "ROOTMOTION VOID - no " + Path.GetFullPath(probe);

        var clips = new List<SampledClip>();
        BakedSkin skin = ModelBuild.From(GlbReader.Read(File.ReadAllBytes(probe), clips), Root);
        int root = Treadmill.RootBone(skin);
        Check(root >= 0, "the fixture has one armature root: '" + skin.BoneNames[root] + "'");
        string rootPath = skin.BonePath(root);

        SampledClip walk = null;
        foreach (SampledClip c in clips)
            if (string.Equals(c.Name, Walk, StringComparison.OrdinalIgnoreCase)) walk = c;
        Check(walk != null, "the fixture carries '" + Walk + "'");

        // THE FILE, before anything is derived: it drives no root channel at all, which is the whole
        // defect - the engine would measure Speed 0 and the walk would never end.
        foreach (SampledTrack t in walk.Tracks)
            Check(t.Node != root, "the file itself drives the root bone - this fixture no longer poses " +
                  "the problem the gate exists for");

        // THE CONTROL: on a looping clip first-versus-last is zero for every bone the file drives, so
        // the naive metric is dead here and the root's reading below is not a coincidence of it.
        float worst = 0f;
        foreach (SampledTrack t in walk.Tracks)
            if (t.Translations != null)
            {
                ObjVector3 a = t.Translations[0], b = t.Translations[t.Translations.Length - 1];
                float d = Len(b.X - a.X, b.Y - a.Y, b.Z - a.Z);
                if (d > worst) worst = d;
            }
        Check(worst < 1e-3f, "the fixture's walk closes its cycle - the furthest any bone the FILE " +
              "drives travels first-to-last is " + F(worst) + ", so a first-versus-last reading " +
              "measures nothing and only a per-frame metric can find the locomotion");

        // ---------------------------------------------------------------- the arm
        // Every clip of the fixture, said out loud: four of the five must NOT get a ramp, and a run
        // where they all do is the failure mode this line makes visible.
        foreach (SampledClip c in clips) Console.WriteLine("  " + c.Name + ": " + Treadmill.Derive(c, skin).Why);
        float height = Treadmill.Derive(walk, skin).Height;
        List<ClipFields.Binding> baked = ClipFields.Bindings(walk, skin);
        float speed = MeasureLikeTheEngine(baked, rootPath, walk.Times.Length, walk.SampleRate,
                                           out float travel);
        Check(speed > 0f, "the baked walk clip translates the root motion node '" + rootPath +
              "' - the engine measures Speed " + F(speed) + " unit/s off it, not 0, so " +
              "TacticalNavigationComponent.cs:376 advances the actor and :718 can be reached");
        Check(travel >= SaneTravelOfHeight * height, "the baked ramp is " + F(travel) +
              " over a rig " + F(height) + " tall, under " + SaneTravelOfHeight +
              " of its height - that is noise, not a walk cycle");

        // ------------------------------------------------- the SCALE arm, in the game's own units
        // The project this fixture belongs to declares how big its rig is in game; the bake writes the
        // ramp in THAT space because the engine spends the number as world units per second. Read the
        // declaration rather than restating it here, so the gate and the bake cannot disagree.
        float scale = DeclaredScale(out string from);
        Check(scale > 0f, "read \"scale\" out of " + from);
        float worldHeight = height * scale;
        Check(worldHeight >= ShortestSane && worldHeight <= TallestSane,
              "the rig is " + F(worldHeight) + " tile(s) tall at the declared scale " + F(scale) +
              ", outside " + ShortestSane + ".." + TallestSane + " - a tactical actor is not that size " +
              "(TacticalMap.cs:67 TileSize = 1)");

        List<ClipFields.Binding> scaled = ClipFields.Bindings(walk, skin, null, scale);
        float world = MeasureLikeTheEngine(scaled, rootPath, walk.Times.Length, walk.SampleRate, out _);
        Check(world >= SlowestSane && world <= FastestSane * ShippedRunSpeed,
              "the game will measure " + F(world) + " tile/s off the baked walk, outside " +
              SlowestSane + ".." + F(FastestSane * ShippedRunSpeed) + " tile/s - the shipped soldier " +
              "run 'MV_RunFwd_Loop_AR' measures " + ShippedRunSpeed + " tile/s the same way, so this " +
              "clip is written in the wrong space and the actor will crawl or teleport");

        // THE CONTROL for the arm above, and the reason it is not scale-free: the SAME clip baked
        // without the projection must land OUTSIDE the band. If it did not, the band could not tell
        // the file's units from the game's and would pass a 200x ramp forever - which is the bug that
        // shipped. Written as an inequality on the un-projected bake, so it fires by construction.
        Check(speed < SlowestSane || speed > FastestSane * ShippedRunSpeed,
              "baked in the FILE's units the walk measures " + F(speed) + " tile/s, which is inside " +
              "the sane band - so the band cannot distinguish the two spaces and the arm above proves " +
              "nothing; pick a fixture or a scale where they differ");

        // ---------------------------------------------------------------- the other side
        // A clip that ALREADY travels must come back untouched. Built by giving THIS clip's root the
        // very ramp the derivation just produced - so the second pass sees a rig whose feet are
        // planted in model space, derives ~0, and writes nothing. Same code path, opposite answer.
        SampledClip already = WithRootRamp(walk, skin, root, baked, rootPath);
        Treadmill.Locomotion again = Treadmill.Derive(already, skin);
        Check(!again.Any, "a clip that already carries its own root motion is left alone - the second " +
              "derivation says: " + again.Why);
        List<ClipFields.Binding> untouched = ClipFields.Bindings(already, skin);
        Check(untouched.Count == baked.Count, "and it bakes to the same " + baked.Count +
              " binding(s), so nothing was added on top of the travel the file brought");
        float same = MeasureLikeTheEngine(untouched, rootPath, already.Times.Length, already.SampleRate,
                                          out float sameTravel);
        Check(Math.Abs(same - speed) < 1e-3f, "and the engine still measures " + F(same) +
              " unit/s off it - the file's own travel survived the bake instead of being doubled");

        // ------------------------------------------------- THE PACE, which is the whole point of the ramp
        // A correctly-derived ramp is still the WRONG SPEED: this fixture's own cadence is 0.51 tile/s
        // against the shipped 5.43, which in game is a creature that crawls. Retiming is what closes
        // that, and it has to be measured the way the ENGINE measures it - off the baked curve - or the
        // arm would only be checking arithmetic on a float.
        float cycle = (walk.Times.Length - 1) / walk.SampleRate;
        float factor = Treadmill.Retime(walk, skin, scale, Treadmill.ShippedPace, out string retimed);
        Console.WriteLine("  " + retimed);
        List<ClipFields.Binding> quick = ClipFields.Bindings(walk, skin, null, scale);
        float paced = MeasureLikeTheEngine(quick, rootPath, walk.Times.Length, walk.SampleRate, out _);
        Check(Math.Abs(paced - Treadmill.ShippedPace) < 0.05f, "retimed to the shipped pace, the engine " +
              "measures " + F(paced) + " tile/s off the baked walk, not " + Treadmill.ShippedPace +
              " - a creature moving at " + F(world) + " tile/s is the 'it crawls' defect and this is " +
              "the only lever the game has (AnimationInfos.cs:123 Speed = |Offset| / clip.length)");
        Check(quick.Count == scaled.Count, "and it still bakes to the same " + scaled.Count +
              " binding(s) - a retime moves the TIMELINE, never a curve, which is why the legs cannot " +
              "come out of step with the travel");
        // The invariant the bake leans on: ProjectBake calls this from two places and neither may
        // depend on running first. Falsified by the factor above, which must NOT be 1.
        Check(Math.Abs(factor - 1f) > 0.5f, "the first retime really did something - factor " + F(factor) +
              ", so the idempotence check below is not vacuous");
        float twice = Treadmill.Retime(walk, skin, scale, Treadmill.ShippedPace, out string second);
        Check(twice == 1f, "and retiming an already-paced clip is a no-op (factor " + F(twice) +
              "), so the second caller measures instead of accelerating it again: " + second);

        return "ROOTMOTION PASS, " + checks + " check(s) - '" + Walk + "' derives " + F(speed) +
               " unit/s in the file's units (" + F(travel) + " over " + F(cycle) +
               " s, rig " + F(height) + " tall) = " +
               F(world) + " tile/s at the declared scale " + F(scale) + ", retimed x" + F(factor) +
               " to " + F(paced) + " tile/s against the shipped soldier run's " + ShippedRunSpeed +
               "; an already-travelling clip is untouched and a re-retime is a no-op";
    }

    /// <summary>
    /// The <c>"scale"</c> the fixture's own project declares - the factor between the file's units and
    /// the game's - read off ppcontent.json rather than restated here, so this gate measures what the
    /// bake will actually be given. 0 when the file or the key is missing, which the arm reports.
    /// </summary>
    private static float DeclaredScale(out string from)
    {
        from = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,
                                             @"..\..\..\..\..\demos\CustomCreature\ppcontent.json"));
        if (!File.Exists(from)) return 0f;
        Match m = Regex.Match(File.ReadAllText(from), "\"scale\"\\s*:\\s*(-?[0-9.eE+]+)");
        return m.Success && float.TryParse(m.Groups[1].Value, System.Globalization.NumberStyles.Float,
                                           CultureInfo.InvariantCulture, out float v) ? v : 0f;
    }

    /// <summary>
    /// <c>AnimationInfos.GetAnimInfo</c>'s own arithmetic over the BAKED binding: the root motion
    /// node's position at t=0 and at t=clip.length, the offset between them, and its magnitude over
    /// the length. Nothing here calls <see cref="Treadmill"/>.
    /// </summary>
    private static float MeasureLikeTheEngine(List<ClipFields.Binding> bindings, string rootPath,
                                              int frames, float sampleRate, out float offset)
    {
        offset = 0f;
        ClipFields.Binding b = null;
        foreach (ClipFields.Binding x in bindings)
            if (x.Attribute == ClipFields.AttributePosition && x.BonePath == rootPath) b = x;
        if (b == null) return 0f;
        int w = ClipFields.PositionCurves, last = (frames - 1) * w;
        offset = Len(b.Values[last] - b.Values[0], b.Values[last + 1] - b.Values[1],
                     b.Values[last + 2] - b.Values[2]);
        float length = (frames - 1) / sampleRate;
        return length > 0f ? offset / length : 0f;
    }

    /// <summary>The same clip with the baked ramp folded back into the FILE's own root track - what a
    /// .glb exported with real root motion would have carried in the first place.</summary>
    private static SampledClip WithRootRamp(SampledClip clip, BakedSkin skin, int root,
                                            List<ClipFields.Binding> baked, string rootPath)
    {
        ClipFields.Binding ramp = null;
        foreach (ClipFields.Binding x in baked)
            if (x.Attribute == ClipFields.AttributePosition && x.BonePath == rootPath) ramp = x;

        var copy = new SampledClip
        {
            Name = clip.Name + "_with_root",
            Times = clip.Times,
            SampleRate = clip.SampleRate,
            FrameRate = clip.FrameRate,
            Length = clip.Length
        };
        copy.Nodes.AddRange(clip.Nodes);
        foreach (SampledTrack t in clip.Tracks) copy.Tracks.Add(t);
        var t3 = new ObjVector3[clip.Times.Length];
        for (int f = 0; f < t3.Length; f++)
            t3[f] = new ObjVector3(ramp.Values[f * 3], ramp.Values[f * 3 + 1], ramp.Values[f * 3 + 2]);
        copy.Tracks.Add(new SampledTrack { Node = root, Translations = t3 });
        return copy;
    }

    private static float Len(float x, float y, float z)
    {
        return (float)Math.Sqrt(x * x + y * y + z * z);
    }

    private static string F(float v)
    {
        return v.ToString("0.######", CultureInfo.InvariantCulture);
    }

    private static void Check(bool ok, string what)
    {
        checks++;
        if (!ok) throw new Exception("ROOTMOTION FAIL: " + what);
    }
}
