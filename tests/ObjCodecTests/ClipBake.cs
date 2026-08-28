using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using AssetsTools.NET;
using AssetsTools.NET.Extra;
using Morgott.ContentTool.Bake;
using Morgott.ContentTool.Import;

/// <summary>
/// Gate <b>U9</b>: an IMPORTED clip is BAKED - the half U8 recorded as missing. U8 read a real .glb's
/// five clips into <c>SampledClip</c>/<c>SampledTrack</c> and stopped there, because
/// <c>ClipFields.FillClip</c> wrote ONE binding of ONE attribute over a single float per frame, while
/// an imported clip is N bones x {position 3, rotation 4, scale 3}.
///
/// The fixture is the same real download U8 used (<c>lib\u8_probe.glb</c>, Quaternius' CC0 spider): 39
/// bones, 5 clips, keyframe-reduced 24 fps, several hundred curves in one clip. Nothing our own writer
/// produced takes part - <c>GlbCodec.Write</c> emits no animation at all, so there is no round trip
/// here to be self-satisfied with.
///
/// Three independent oracles, because they fail apart:
///  - <b>the binding list</b>. Every curve's <c>path</c> must be the CRC of the bone's path UNDER THE
///    MODEL ROOT - the same number <c>SkinFields</c> wrote into the MESH's m_BoneNameHashes in the
///    same file. This test spells the expected list out ITSELF, in its own loop, so the flat ORDER is
///    pinned by a second author rather than read back off the writer.
///  - <b>every float of the dense bank</b>, against the numbers <c>GlbReader</c> read out of the .glb.
///    The bank is frame-major over the flat curve order, so a curve-major writer, an off-by-one width
///    or a rotation written as three floats all land here - and they land on a MIDDLE frame of a LATE
///    binding, never on frame 0 of binding 0, where a wrong interleave and a right one agree.
///  - <b>the sizes</b>: m_FrameCount, m_CurveCount, the sample count and m_MuscleClipSize, which is a
///    function of the bank sizes and nothing else (ClipFields' measured formula).
///
/// A missing game install or probe is VOID, never PASS.
/// </summary>
internal static class ClipBake
{
    private const string Bundle = "mutoid_assets_all.bundle";
    private const string Probe = "u8_probe.glb";
    private const string Root = "spider";

    /// <summary>
    /// The bundle the LOOP contrast is pinned against, and the two clips in it that differ in nothing
    /// but looping: 'Turret_ShootLoop' cycles, 'Turret_ShootEnd' does not. Both ship m_WrapMode 0, which
    /// is the whole point - see <see cref="Pin"/>.
    /// </summary>
    private const string LoopBundle = "px_equipment_assets_all.bundle";
    private const string ShippedLooping = "Turret_ShootLoop", ShippedOnce = "Turret_ShootEnd";

    /// <summary>
    /// What an author would put in ppcontent.json's "loop" for this file, spelled the way an author
    /// would: the file's own clip names, one of them in the wrong case, separated by a comma and a
    /// space. So the arm exercises <see cref="ClipFields.Names"/>'s parsing and case rule too, not only
    /// the field write.
    /// </summary>
    private const string LoopDeclaration = "Spider_Idle, spider_WALK";

    /// <summary>
    /// What the author would put in <c>"play"</c>. Deliberately NOT the file's first clip
    /// ('Spider_Attack'): an arm that named the first one could not tell the choice from the old
    /// hardcoded behaviour. It is also the clip a walking creature actually needs.
    /// </summary>
    private const string PlayDeclaration = "Spider_Walk";

    /// <summary>The base controller the in-game gate overrides, and the externals index a one-external
    /// clone hands out. Nothing here RESOLVES it - only the game can, which is the half U9's in-game
    /// arm is for.</summary>
    private const long BaseController = -8389213721431673559L, BaseClip = -2054101139859036125L;
    private const int BaseFileId = 2;

    internal static string Run()
    {
        string probe = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"..\..\..\..\..\lib\" + Probe);
        string root = Environment.GetEnvironmentVariable("PPRoot") ?? @"D:\Steam\steamapps\common\Phoenix Point";
        string classData = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"..\..\..\..\..\lib\classdata.tpk");
        string shipped = Path.Combine(root, @"PhoenixPointWin64_Data\StreamingAssets\aa\StandaloneWindows64\" + Bundle);
        if (!File.Exists(probe)) return "CLIP bake VOID - no " + Path.GetFullPath(probe);
        if (!File.Exists(shipped)) return "CLIP bake VOID - no " + shipped + " (set PPRoot to the game folder)";
        if (!File.Exists(classData)) return "CLIP bake VOID - no " + Path.GetFullPath(classData);

        var clips = new List<SampledClip>();
        BakedSkin skin = ModelBuild.From(GlbReader.Read(File.ReadAllBytes(probe), clips), Root);
        Assert(clips.Count > 0 && skin.Rigged, "the probe carries an armature and clips: " +
               skin.BoneNames.Length + " bone(s), " + clips.Count + " clip(s)");

        string pin = Pin(classData, Path.Combine(Path.GetDirectoryName(shipped), LoopBundle));

        string copy = Path.Combine(Path.GetTempPath(), "ct-clipbake-" + Bundle);
        Build(classData, shipped, copy, skin, clips);
        long bytes = new FileInfo(copy).Length;
        string lines = "";
        int looping = 0;
        try
        {
            foreach (SampledClip clip in clips)
            {
                if (Loops(clip)) looping++;
                lines += Clip(classData, copy, skin, clip);
            }
            lines += "\n  " + Animator(classData, copy);
        }
        finally { File.Delete(copy); }

        // Anti-vacuity for the loop arm: it can only tell a looping clip from a one-shot if this run
        // wrote BOTH. A fixture where every clip loops passes a writer that hardcodes true, and one
        // where none do passes the writer this commit replaced - the one that never wrote the field.
        Assert(looping > 0 && looping < clips.Count,
               "the declaration makes SOME of the file's clips loop and leaves the rest alone: " +
               looping + " of " + clips.Count);

        return "CLIP bake PASS on " + Bundle + " (" + bytes + " B with " + clips.Count +
               " imported clip(s), " + looping + " looping)\n  " + pin + lines;
    }

    /// <summary>Whether the author's declaration says this clip cycles - the ONE rule the bake uses,
    /// so the fixture and the writer cannot drift into two spellings of it.</summary>
    private static bool Loops(SampledClip clip)
    {
        return ClipFields.Names(LoopDeclaration).Contains(clip.Name);
    }

    /// <summary>
    /// WHICH FIELD MAKES A CLIP LOOP, pinned against SHIPPED data in the same run rather than against
    /// our own writer. 'Turret_ShootLoop' and 'Turret_ShootEnd' live in one bundle, are the same kind of
    /// generic clip, and differ in exactly one field: m_MuscleClip.m_LoopTime. m_WrapMode is 0 on BOTH -
    /// which is the assert that matters, because "set m_WrapMode to Loop" is the plausible wrong fix,
    /// and this line is what refuses it. (Measured wider before it was written down: all 650 clips of
    /// nine shipped bundles carry m_WrapMode 0, 132 of them with m_LoopTime true.)
    /// </summary>
    private static string Pin(string classData, string bundlePath)
    {
        if (!File.Exists(bundlePath)) return "LOOP PIN VOID - no " + bundlePath;
        bool loopingLoop = false, onceLoop = true;
        int loopingWrap = -1, onceWrap = -1;
        AssetsManager m = new AssetsManager();
        m.LoadClassPackage(classData);
        BundleFileInstance bun = m.LoadBundleFile(bundlePath, true);
        AssetsFileInstance af = m.LoadAssetsFileFromBundle(bun, 0, false);
        m.LoadClassDatabaseFromPackage(af.file.Metadata.UnityVersion);
        int found = 0;
        try
        {
            foreach (AssetFileInfo i in af.file.Metadata.GetAssetsOfType(AssetClassID.AnimationClip))
            {
                AssetTypeValueField clip = m.GetBaseField(af, i);
                if (clip["m_Name"].IsDummy) continue;
                string name = clip["m_Name"].AsString;
                if (name == ShippedLooping)
                {
                    found++;
                    loopingLoop = clip["m_MuscleClip"]["m_LoopTime"].AsBool;
                    loopingWrap = clip["m_WrapMode"].AsInt;
                }
                else if (name == ShippedOnce)
                {
                    found++;
                    onceLoop = clip["m_MuscleClip"]["m_LoopTime"].AsBool;
                    onceWrap = clip["m_WrapMode"].AsInt;
                }
            }
        }
        finally { m.UnloadAll(); }

        Assert(found == 2, "the pin found both shipped clips in " + LoopBundle + ": " + found + " of 2");
        Assert(loopingLoop && !onceLoop, "the SHIPPED contrast is m_LoopTime: '" + ShippedLooping +
               "' " + loopingLoop + " vs '" + ShippedOnce + "' " + onceLoop);
        Assert(loopingWrap == 0 && onceWrap == 0, "and m_WrapMode is 0 on BOTH, so it is NOT the loop " +
               "flag: '" + ShippedLooping + "' " + loopingWrap + ", '" + ShippedOnce + "' " + onceWrap);
        return "LOOP PIN " + LoopBundle + ": '" + ShippedLooping + "' m_LoopTime=True vs '" +
               ShippedOnce + "' m_LoopTime=False, m_WrapMode=0 on both";
    }

    /// <summary>
    /// One clip, read back off the written file and compared against the IMPORT float for float.
    /// </summary>
    private static string Clip(string classData, string copy, BakedSkin skin, SampledClip clip)
    {
        int frames = clip.Times.Length;
        List<Expected> want = Predict(skin, clip);
        int curves = 0;
        foreach (Expected e in want) curves += e.Values.Length / frames;

        Written got = Read(classData, copy, clip.Name.ToLowerInvariant());
        Assert(got != null, "the bake wrote a clip named '" + clip.Name.ToLowerInvariant() + "'");
        Assert(got.Bindings.Count == want.Count, "clip '" + clip.Name + "' binds one curve per driven " +
               "channel: " + got.Bindings.Count + " (the file's tracks need " + want.Count + ")");

        // THE JOIN, per binding: the CRC a curve is addressed by is the one the MESH carries for that
        // same bone - two writers, one path spelling, compared here and nowhere else.
        for (int b = 0; b < want.Count; b++)
        {
            Assert(got.Bindings[b].Path == SkinFields.BoneHash(want[b].BonePath),
                   "clip '" + clip.Name + "' binding " + b + " is the CRC of '" + want[b].BonePath +
                   "' (" + SkinFields.BoneHash(want[b].BonePath) + "), not " + got.Bindings[b].Path);
            Assert(got.Bindings[b].Attribute == want[b].Attribute,
                   "clip '" + clip.Name + "' binding " + b + " drives attribute " + want[b].Attribute +
                   ", not " + got.Bindings[b].Attribute);
            Assert(got.Bindings[b].TypeId == 4, "clip '" + clip.Name + "' binding " + b +
                   " names the Transform typeID: " + got.Bindings[b].TypeId);
        }
        // The mesh in the SAME file hashes those same paths - the agreement U7 rests on.
        Assert(got.MeshHashes.Contains(SkinFields.BoneHash(want[0].BonePath)),
               "and the MESH in the same file carries binding 0's hash " +
               SkinFields.BoneHash(want[0].BonePath) + " among its " + got.MeshHashes.Count + " bones");

        Assert(got.FrameCount == frames && got.CurveCount == curves,
               "clip '" + clip.Name + "' dense bank is " + got.FrameCount + "x" + got.CurveCount +
               ", not the file's " + frames + "x" + curves);
        Assert(got.Samples.Length == frames * curves, "clip '" + clip.Name + "' holds " +
               got.Samples.Length + " sample(s), not " + frames * curves);
        Assert(Near(got.SampleRate, clip.SampleRate) && Near(got.StopTime, (frames - 1) / clip.SampleRate),
               "clip '" + clip.Name + "' runs at the file's own rate and length: " + F(got.SampleRate) +
               " Hz, stop " + F(got.StopTime));
        Assert(got.Streamed == 2 && got.StreamedCurves == 0 && got.Constant == 0 && got.Delta == curves &&
               got.Index == 200, "clip '" + clip.Name + "' leaves the other two banks the way a shipped " +
               "generic clip does: streamed=" + got.Streamed + "/" + got.StreamedCurves + " const=" +
               got.Constant + " delta=" + got.Delta + " index=" + got.Index);
        // The one field that decides whether a walk cycle walks or stops dead on its last frame, and
        // the one the shipped PIN above says it is. Asserted BOTH ways in this run: the declared clips
        // must carry true and the undeclared ones false, so a writer that ignores the flag - or hardcodes
        // it - fails on one of the five.
        Assert(got.LoopTime == Loops(clip), "clip '" + clip.Name + "' m_LoopTime is " + Loops(clip) +
               " (the author's \"loop\" declaration), not " + got.LoopTime);
        Assert(got.WrapMode == 0, "clip '" + clip.Name + "' leaves m_WrapMode at the 0 every shipped " +
               "clip carries: " + got.WrapMode);

        uint size = ClipFields.MuscleClipSize(2, frames * curves, 0, curves);
        Assert(got.MuscleSize == size, "clip '" + clip.Name + "' m_MuscleClipSize is the measured " +
               "function of its bank sizes: " + got.MuscleSize + " (the formula says " + size + ")");

        // EVERY float of the bank, against the .glb's own numbers - frame-major over the flat curve
        // order. The worst offender a wrong interleave produces is reported by frame and curve so a
        // RED line says WHERE, not merely that something differs.
        int wrongAt = -1, wrongCurve = -1;
        float worst = 0f;
        int flat = 0;
        for (int b = 0; b < want.Count; b++)
        {
            int width = want[b].Values.Length / frames;
            for (int f = 0; f < frames; f++)
                for (int c = 0; c < width; c++)
                {
                    float mine = want[b].Values[f * width + c];
                    float theirs = got.Samples[f * curves + flat + c];
                    if (Math.Abs(mine - theirs) > worst)
                    {
                        worst = Math.Abs(mine - theirs);
                        wrongAt = f; wrongCurve = flat + c;
                    }
                }
            flat += width;
        }
        Assert(worst < 1e-6f, "clip '" + clip.Name + "' dense bank IS the file's own samples, frame-major " +
               "over the flat curve order: frame " + wrongAt + " curve " + wrongCurve + " is off by " +
               worst.ToString("0.######", CultureInfo.InvariantCulture));

        // Anti-vacuity, twice. A bank of zeroes, or one holding the same frame over and over, satisfies
        // every arm above - and both are exactly what a writer that dropped the per-frame walk emits.
        float moved = 0f;
        for (int f = 1; f < frames; f++)
            for (int c = 0; c < curves; c++)
                moved = Math.Max(moved, Math.Abs(got.Samples[f * curves + c] - got.Samples[c]));
        Assert(moved > 1e-4f, "clip '" + clip.Name + "' actually CHANGES across its frames: " + F(moved));

        // And the one-line oracle the in-game U9-wrote arm compares, computed here from the bindings
        // the bake was handed. Order-sensitive: two curves swapped moves it.
        uint sig = ClipFields.Sig(ClipFields.Bindings(clip, skin));
        Assert(got.Sig == sig, "clip '" + clip.Name + "' binding fingerprint is " + sig + ", not " + got.Sig);

        return "\n  '" + clip.Name + "' " + want.Count + " binding(s) over " + clip.Tracks.Count +
               " bone(s), dense " + frames + "x" + curves + "@" + F(clip.SampleRate) + " = " +
               got.Samples.Length + " float(s), muscleSize=" + got.MuscleSize + " sig=" + got.Sig +
               ", m_LoopTime=" + got.LoopTime + ", furthest a curve travels " + F(moved);
    }

    /// <summary>
    /// The bindings a correct bake must write, spelled out HERE rather than taken from
    /// <c>ClipFields.Bindings</c>: every position, then every rotation, then every scale, each in the
    /// clip's own track order, each addressed by the bone's path under the model root. That grouping is
    /// the flat order the whole bank is indexed by, so a second spelling of it is what makes the
    /// comparison an oracle instead of a mirror.
    /// </summary>
    private static List<Expected> Predict(BakedSkin skin, SampledClip clip)
    {
        var want = new List<Expected>();
        foreach (SampledTrack t in clip.Tracks)
            if (t.Translations != null) want.Add(Vector(skin, t.Node, 1, t.Translations));
        // ...and the ROOT-MOTION RAMP the bake adds to a clip that walks on the spot (U13). Spelled
        // here from the derived velocity by this file's own arithmetic - rest + v*t, at the END of the
        // position group - so the WRITE is pinned by a second author even though the derivation
        // itself is what RootMotionBake measures.
        Treadmill.Locomotion loco = Treadmill.Derive(clip, skin);
        if (loco.Any)
        {
            int root = Treadmill.RootBone(skin);
            float[] rest = skin.BoneRest[root];
            var ramp = new float[clip.Times.Length * 3];
            for (int f = 0; f < clip.Times.Length; f++)
            {
                float k = clip.Times[f] - clip.Times[0];
                ramp[f * 3] = rest[12] + loco.Velocity.X * k;
                ramp[f * 3 + 1] = rest[13] + loco.Velocity.Y * k;
                ramp[f * 3 + 2] = rest[14] + loco.Velocity.Z * k;
            }
            want.Add(new Expected { BonePath = skin.BonePath(root), Attribute = 1, Values = ramp });
        }
        foreach (SampledTrack t in clip.Tracks)
            if (t.Rotations != null)
            {
                var v = new float[t.Rotations.Length * 4];
                for (int i = 0; i < t.Rotations.Length; i++)
                {
                    v[i * 4] = t.Rotations[i].X; v[i * 4 + 1] = t.Rotations[i].Y;
                    v[i * 4 + 2] = t.Rotations[i].Z; v[i * 4 + 3] = t.Rotations[i].W;
                }
                want.Add(new Expected { BonePath = skin.BonePath(t.Node), Attribute = 2, Values = v });
            }
        foreach (SampledTrack t in clip.Tracks)
            if (t.Scales != null) want.Add(Vector(skin, t.Node, 3, t.Scales));
        return want;
    }

    private static Expected Vector(BakedSkin skin, int node, uint attribute, ObjVector3[] source)
    {
        var v = new float[source.Length * 3];
        for (int i = 0; i < source.Length; i++)
        {
            v[i * 3] = source[i].X; v[i * 3 + 1] = source[i].Y; v[i * 3 + 2] = source[i].Z;
        }
        return new Expected { BonePath = skin.BonePath(node), Attribute = attribute, Values = v };
    }

    private sealed class Expected
    {
        internal string BonePath;
        internal uint Attribute;
        internal float[] Values;
    }

    // ---------------------------------------------------------------- write, then read back

    /// <summary>
    /// The BundleBaker write path for a model that brings its own animation: every clip, an
    /// AnimatorOverrideController over the first of them, and the model with the Animator on its ROOT -
    /// the shipping shape U7 established, now driven by a clip nobody in this repo authored.
    /// </summary>
    private static void Build(string classData, string shipped, string outPath, BakedSkin skin,
                              List<SampledClip> clips)
    {
        AssetsManager m = new AssetsManager();
        m.LoadClassPackage(classData);
        BundleFileInstance bun = m.LoadBundleFile(shipped, true);
        AssetsFileInstance af = m.LoadAssetsFileFromBundle(bun, 0, false);
        m.LoadClassDatabaseFromPackage(af.file.Metadata.UnityVersion);
        try
        {
            long next = 0;
            foreach (AssetFileInfo a in af.file.Metadata.AssetInfos) if (a.PathId > next) next = a.PathId;
            next += 1000;

            long materialPathId = 0;
            foreach (AssetFileInfo i in af.file.Metadata.GetAssetsOfType(AssetClassID.Material))
            {
                materialPathId = i.PathId;
                break;
            }

            // The clip the AOC hands Mecanim, resolved by the SAME function the bake resolves it with -
            // one rule, or the gate would prove a choice the bake does not make.
            var plan = ClipFields.Bakeable(Root, clips);
            string chosen = plan[ClipFields.Chosen(plan, PlayDeclaration)].Value.Name;

            long playClip = 0;
            foreach (SampledClip clip in clips)
            {
                long clipId = next++;
                if (clip.Name == chosen) playClip = clipId;
                SampledClip c = clip;
                PrefabFields.Create(af.file, m.ClassDatabase, clipId, AssetClassID.AnimationClip, bf =>
                {
                    bf["m_Name"].AsString = c.Name.ToLowerInvariant();
                    ClipFields.FillClip(bf, ClipFields.Bindings(c, skin), c.Times.Length, c.SampleRate,
                                        Loops(c));
                });
            }
            long aocId = next++;
            PrefabFields.Create(af.file, m.ClassDatabase, aocId, AssetClassID.AnimatorOverrideController, a =>
            {
                a["m_Name"].AsString = Root + "_aoc";
                ClipFields.FillOverrideController(a, BaseFileId, BaseController, BaseFileId, BaseClip, playClip);
            });
            SkinFields.BuildModel(af.file, m.ClassDatabase, () => next++, Root, skin, materialPathId, aocId);

            // Without this the bundle writes its ORIGINAL directory entry and everything added
            // vanishes silently - the same line BundleBaker.Write carries.
            bun.file.BlockAndDirInfo.DirectoryInfos[0].SetNewData(af.file);

            AssetBundleCompressionType comp = bun.file.GetCompressionType();
            using (MemoryStream raw = new MemoryStream())
            using (AssetsFileWriter rw = new AssetsFileWriter(raw))
            {
                bun.file.Write(rw);
                rw.Flush();
                raw.Position = 0;
                if (comp == AssetBundleCompressionType.None)
                {
                    using (FileStream f = File.Create(outPath)) raw.CopyTo(f);
                }
                else
                {
                    AssetBundleFile packed = new AssetBundleFile();
                    packed.Read(new AssetsFileReader(raw));
                    using (AssetsFileWriter w = new AssetsFileWriter(outPath)) packed.Pack(w, comp, false, null);
                    packed.Close();
                }
            }
        }
        finally { m.UnloadAll(); }
    }

    private sealed class Bound
    {
        internal uint Path, Attribute;
        internal int TypeId;
    }

    private sealed class Written
    {
        internal readonly List<Bound> Bindings = new List<Bound>();
        internal float[] Samples;
        internal int FrameCount, CurveCount, Streamed, Constant, Delta, Index, WrapMode;
        internal bool LoopTime;
        internal uint StreamedCurves, MuscleSize, Sig;
        internal float SampleRate, StopTime;
        internal readonly List<uint> MeshHashes = new List<uint>();
    }

    /// <summary>Everything about one written clip this gate asserts, read straight off the file.</summary>
    private static Written Read(string classData, string bundlePath, string clipName)
    {
        AssetsManager m = new AssetsManager();
        m.LoadClassPackage(classData);
        BundleFileInstance bun = m.LoadBundleFile(bundlePath, true);
        AssetsFileInstance af = m.LoadAssetsFileFromBundle(bun, 0, false);
        m.LoadClassDatabaseFromPackage(af.file.Metadata.UnityVersion);
        try
        {
            AssetTypeValueField clip = null;
            foreach (AssetFileInfo i in af.file.Metadata.GetAssetsOfType(AssetClassID.AnimationClip))
            {
                AssetTypeValueField f = m.GetBaseField(af, i);
                if (!f["m_Name"].IsDummy && f["m_Name"].AsString == clipName) { clip = f; break; }
            }
            if (clip == null) return null;

            Written w = new Written();
            var pairs = new List<string>();
            foreach (AssetTypeValueField b in clip["m_ClipBindingConstant"]["genericBindings"]["Array"].Children)
            {
                w.Bindings.Add(new Bound
                {
                    Path = b["path"].AsUInt,
                    Attribute = b["attribute"].AsUInt,
                    TypeId = b["typeID"].AsInt
                });
                pairs.Add(ClipFields.Pair(b["path"].AsUInt, b["attribute"].AsUInt));
            }
            w.Sig = ClipFields.Sig(pairs);

            AssetTypeValueField muscle = clip["m_MuscleClip"];
            AssetTypeValueField data = muscle["m_Clip"]["data"];
            AssetTypeValueField dense = data["m_DenseClip"];
            w.FrameCount = dense["m_FrameCount"].AsInt;
            w.CurveCount = (int)dense["m_CurveCount"].AsUInt;
            w.SampleRate = dense["m_SampleRate"].AsFloat;
            AssetTypeValueField samples = dense["m_SampleArray"]["Array"];
            w.Samples = new float[samples.Children.Count];
            for (int i = 0; i < w.Samples.Length; i++) w.Samples[i] = samples.Children[i].AsFloat;
            w.Streamed = data["m_StreamedClip"]["data"]["Array"].Children.Count;
            w.StreamedCurves = data["m_StreamedClip"]["curveCount"].AsUInt;
            w.Constant = data["m_ConstantClip"]["data"]["Array"].Children.Count;
            w.Delta = muscle["m_ValueArrayDelta"]["Array"].Children.Count;
            w.Index = muscle["m_IndexArray"]["Array"].Children.Count;
            w.StopTime = muscle["m_StopTime"].AsFloat;
            w.MuscleSize = clip["m_MuscleClipSize"].AsUInt;
            w.LoopTime = muscle["m_LoopTime"].AsBool;
            w.WrapMode = clip["m_WrapMode"].AsInt;

            foreach (AssetFileInfo i in af.file.Metadata.GetAssetsOfType(AssetClassID.Mesh))
            {
                AssetTypeValueField mesh = m.GetBaseField(af, i);
                if (mesh["m_Name"].IsDummy || mesh["m_Name"].AsString != SkinFields.MeshName(Root)) continue;
                foreach (AssetTypeValueField h in mesh["m_BoneNameHashes"]["Array"].Children)
                    w.MeshHashes.Add(h.AsUInt);
            }
            return w;
        }
        finally { m.UnloadAll(); }
    }

    /// <summary>The model root's Animator, the same line the in-game arm reports.</summary>
    private static string Animator(string classData, string bundlePath)
    {
        AssetsManager m = new AssetsManager();
        m.LoadClassPackage(classData);
        BundleFileInstance bun = m.LoadBundleFile(bundlePath, true);
        AssetsFileInstance af = m.LoadAssetsFileFromBundle(bun, 0, false);
        m.LoadClassDatabaseFromPackage(af.file.Metadata.UnityVersion);
        try
        {
            AssetTypeValueField root = PrefabFields.FindGameObject(m, af, Root);
            Assert(root != null, "the model root '" + Root + "' is in the file");
            AssetTypeValueField a = PrefabFields.Component(m, af, root, AssetClassID.Animator);
            Assert(a != null, "the model ROOT carries the Animator that plays the imported clip");
            string got = "controller=" + PrefabFields.Name(m, af, a["m_Controller"]["m_PathID"].AsLong) +
                         " avatar=" + a["m_Avatar"]["m_PathID"].AsLong +
                         " culling=" + a["m_CullingMode"].AsInt +
                         " hierarchy=" + a["m_HasTransformHierarchy"].AsBool;
            string want = "controller='" + Root + "_aoc' avatar=0 culling=0 hierarchy=True";
            Assert(got == want, "the root's Animator names the override controller: " + got +
                   " (wanted " + want + ")");

            // WHICH clip that controller hands Mecanim - the author's "play", read back off the file by
            // NAME. A PPtr that resolved to nothing, or to the file's FIRST clip (what every bake did
            // before "play" existed), reads differently here.
            AssetTypeValueField aoc = null;
            foreach (AssetFileInfo i in af.file.Metadata.GetAssetsOfType(AssetClassID.AnimatorOverrideController))
            {
                AssetTypeValueField f = m.GetBaseField(af, i);
                if (!f["m_Name"].IsDummy && f["m_Name"].AsString == Root + "_aoc") { aoc = f; break; }
            }
            Assert(aoc != null, "the override controller '" + Root + "_aoc' is in the file");
            AssetTypeValueField overrides = aoc["m_Clips"]["Array"];
            Assert(overrides.Children.Count == 1, "it overrides exactly one clip: " + overrides.Children.Count);
            string plays = PrefabFields.Name(m, af, overrides.Children[0]["m_OverrideClip"]["m_PathID"].AsLong);
            Assert(plays == "'" + PlayDeclaration.ToLowerInvariant() + "'",
                   "and the clip it plays is the author's \"play\": " + plays + " (wanted '" +
                   PlayDeclaration.ToLowerInvariant() + "')");

            return "root animator " + got + " plays " + plays;
        }
        finally { m.UnloadAll(); }
    }

    private static bool Near(float a, float b) { return Math.Abs(a - b) < 1e-4f; }

    // InvariantCulture: these lines are machine-compared and a ru-RU machine writes 0,5 for 0.5.
    private static string F(float v) => v.ToString("0.###", CultureInfo.InvariantCulture);

    private static void Assert(bool ok, string what)
    {
        if (!ok) throw new Exception("CLIP bake FAILED: " + what);
    }
}
