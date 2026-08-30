using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using AssetsTools.NET;
using AssetsTools.NET.Extra;

// OPTION 3, the reading half: turn Phoenix Point's SHIPPED generic clips into rotation-driven
// tracks that carry no statement about how long a bone is.
//
// A generic clip binds every curve to CRC32(transform path) and Unity's generic bake writes
// position, rotation AND scale on every bound bone. The position curves are the problem: on 95% of
// them the value never changes and equals the PP prefab's own m_LocalPosition, so they do not
// animate anything - they PIN PP's segment lengths onto whatever rig plays the clip. Drop those and
// a foreign model keeps its own proportions; keep the ones that actually move (root/hips) and it
// still travels. Scale is dropped only where it is genuinely unit; a squash curve is real animation
// and is carried through.
//
// Every decision here is made from the clip's own samples against the prefab's own rest, per curve.
// There is no bone list in this file and no clip is special-cased. A clip that cannot be decoded,
// whose bank sizes do not add up, or that binds a bone the PP rig does not have is REFUSED, not
// half-exported: a partially bound animation looks like a working one right up to the moment a limb
// does not move.
//
//   ClipCensus.exe --fields    <tpk> <bundle> <clipName>
//   ClipCensus.exe --selfcheck
//   ClipCensus.exe --export    <tpk> <bundle> <pp-rest.tsv> <out.json> [clip,clip,...]
//
// --export always walks EVERY clip in the bundle for the aggregate counts; the clip list only says
// which ones get their samples written out for tools\ppretarget.py to lay into a .glb.
internal static class Export
{
    private const uint AttrPosition = 1, AttrRotation = 2, AttrScale = 3;
    private const int TransformTypeId = 4;

    /// <summary>A position curve counts as PINNING when it never moves and sits on the rest offset.
    /// Both tests are on the DISTANCE, not per component - a component-wise millimetre accepts a
    /// bone sqrt(3) mm off its rest, which is not what "on the rest offset" means.</summary>
    private const float StillMetres = 1e-4f;      // 0.1 mm travelled over the whole clip
    private const float RestMetres = 1e-3f;       // 1 mm from the prefab's own m_LocalPosition
    // The shipped scale curves measure 1.0 +-5e-5 PER COMPONENT; as a distance that is sqrt(3) x
    // 5e-5. Stating it per component and testing it as a distance is how gun_point_hand's
    // 1.000045 bake noise came out the far side classified as a squash.
    private const float UnitScale = 8.7e-5f;

    /// <summary>
    /// Export grid, in Hz. 120 is not a taste: glTF gets LINEAR samplers while a streamed key is a
    /// CUBIC running to the NEXT key, and these clips key sparsely (73 keys over 6.7 s on
    /// LL_IdleAlert_AR), so the curve bulges between samples and denser is better - but GlbReader
    /// bakes onto "the coarsest rate every key time still lands on" and looks no higher than
    /// MaxRate = 120 (src\Import\GlbReader.cs:44), falling back to 30 Hz when nothing fits. So 120
    /// Hz EXACTLY, on whole 1/120 instants, is the densest grid that survives the bake; sampling
    /// finer would be thrown away and would land the clip on the 30 Hz fallback.
    ///
    /// <see cref="Sag"/> measures what linearising costs at that rate on every exported curve, the
    /// number is printed, and a clip past <see cref="MaxSag"/> is refused rather than quietly
    /// smoothed - measured, 60 Hz cost up to 0.023 of a quaternion component (about 2.6 degrees).
    /// The pinning verdict is taken on a grid PinSub times denser again, because a cubic can bulge
    /// between two sample instants and look still at both.
    /// </summary>
    private const float Fps = 120f;
    private const int PinSub = 4;
    private const float MaxSag = 0.01f;

    // ------------------------------------------------------------------ the PP rest, by path hash

    private sealed class Rest
    {
        internal string Path;
        internal float[] T = new float[3];
        internal float[] Q = new float[4];
    }

    private static Dictionary<uint, Rest> ReadRest(string tsv)
    {
        var byHash = new Dictionary<uint, Rest>();
        foreach (string line in File.ReadAllLines(tsv))
        {
            if (line.Length == 0) continue;
            string[] c = line.Split('\t');
            if (c.Length < 3) throw new InvalidDataException("bad rest row: " + line);
            var r = new Rest { Path = c[0] };
            string[] t = c[1].Split(','), q = c[2].Split(',');
            for (int i = 0; i < 3; i++) r.T[i] = float.Parse(t[i], CultureInfo.InvariantCulture);
            for (int i = 0; i < 4; i++) r.Q[i] = float.Parse(q[i], CultureInfo.InvariantCulture);
            uint h = Crc32(c[0]);
            if (byHash.ContainsKey(h))
                throw new InvalidDataException("two PP paths collide on CRC " + h + ": " +
                                               byHash[h].Path + " and " + c[0]);
            byHash[h] = r;
        }
        // The Animator's own GameObject is the EMPTY path, and root motion binds to CRC32("").
        // Leaving it out marked every clip that moves the rig unbindable and threw the motion away.
        if (!byHash.ContainsKey(Crc32("")))
            throw new InvalidDataException(tsv + " has no row for the Animator's own empty path");
        return byHash;
    }

    /// <summary>Standard CRC-32 (reflected 0xEDB88320) of the UTF-8 path - what Unity hashes a
    /// generic binding's transform path with. Not trusted on faith: --export refuses to run unless
    /// the shipped clips' own binding hashes resolve against this table.</summary>
    private static readonly uint[] Table = BuildTable();

    private static uint[] BuildTable()
    {
        var t = new uint[256];
        for (uint i = 0; i < 256; i++)
        {
            uint c = i;
            for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            t[i] = c;
        }
        return t;
    }

    private static uint Crc32(string s)
    {
        uint c = 0xFFFFFFFFu;
        foreach (byte b in Encoding.UTF8.GetBytes(s)) c = Table[(c ^ b) & 0xFF] ^ (c >> 8);
        return c ^ 0xFFFFFFFFu;
    }

    // ------------------------------------------------------------------------- the three banks

    /// <summary>
    /// Every float curve of one clip, evaluated at an arbitrary time, whichever bank holds it.
    /// The layout is ClipFields.cs's, which measured it: STREAMED is a uint array of frames
    /// {float time; int keyCount; keyCount x {int curveIndex; float coeff0..3}} and a key is the
    /// cubic v(dt) = ((c0*dt + c1)*dt + c2)*dt + c3 running to that curve's NEXT key; DENSE is
    /// frame-major sample[frame*curveCount + curve] on a uniform grid; CONSTANT is one float per
    /// curve, no time. The clip's LENGTH is none of their business - it is m_StartTime/m_StopTime,
    /// which is the only thing that gets a constant-only clip its real duration and stops a clip
    /// whose last key precedes its stop time from being cut short.
    /// </summary>
    private sealed class Banks
    {
        internal int StreamCurves, DenseCurves, DenseFrames, ConstCount;
        internal float DenseBegin, DenseRate, Start, Stop;
        internal List<float[]>[] Keys;      // per streamed curve: {time, c0, c1, c2, c3}
        internal float[] Dense, Const;

        internal float Duration { get { return Math.Max(0f, Stop - Start); } }
        internal int Total { get { return StreamCurves + DenseCurves + ConstCount; } }

        internal float Eval(int curve, float t)
        {
            if (curve < StreamCurves)
            {
                List<float[]> k = Keys[curve];
                if (k == null || k.Count == 0) return 0f;
                int at = 0;
                while (at + 1 < k.Count && k[at + 1][0] <= t) at++;
                float dt = t - k[at][0];
                if (dt < 0f) dt = 0f;
                float[] c = k[at];
                return ((c[1] * dt + c[2]) * dt + c[3]) * dt + c[4];
            }
            if (curve < StreamCurves + DenseCurves)
            {
                if (DenseFrames <= 0 || DenseRate <= 0f) return 0f;
                // LINEAR between the two frames either side. Rounding to the nearest instead put a
                // whole dense frame's error into any sample that did not land on the dense grid.
                float f = (t - DenseBegin) * DenseRate;
                int a = (int)Math.Floor(f);
                float u = f - a;
                if (a < 0) { a = 0; u = 0f; }
                if (a >= DenseFrames - 1) { a = Math.Max(0, DenseFrames - 2); u = DenseFrames > 1 ? 1f : 0f; }
                return DenseAt(a, curve) * (1f - u) + DenseAt(a + 1, curve) * u;
            }
            int ci = curve - StreamCurves - DenseCurves;
            return ci >= 0 && ci < Const.Length ? Const[ci] : 0f;
        }

        private float DenseAt(int frame, int curve)
        {
            if (frame >= DenseFrames) frame = DenseFrames - 1;
            int at = frame * DenseCurves + (curve - StreamCurves);
            return at >= 0 && at < Dense.Length ? Dense[at] : 0f;
        }
    }

    private static Banks ReadBanks(AssetTypeValueField clip)
    {
        AssetTypeValueField muscle = clip["m_MuscleClip"];
        AssetTypeValueField data = muscle["m_Clip"]["data"];
        AssetTypeValueField streamed = data["m_StreamedClip"];
        AssetTypeValueField dense = data["m_DenseClip"];
        AssetTypeValueField constants = data["m_ConstantClip"]["data"]["Array"];

        var b = new Banks
        {
            StreamCurves = (int)streamed["curveCount"].AsUInt,
            DenseCurves = (int)dense["m_CurveCount"].AsUInt,
            DenseFrames = dense["m_FrameCount"].AsInt,
            DenseBegin = dense["m_BeginTime"].AsFloat,
            DenseRate = dense["m_SampleRate"].AsFloat,
            ConstCount = constants.Children.Count,
            Start = muscle["m_StartTime"].AsFloat,
            Stop = muscle["m_StopTime"].AsFloat,
        };
        b.Keys = new List<float[]>[Math.Max(b.StreamCurves, 0)];
        b.Const = new float[b.ConstCount];
        for (int i = 0; i < b.ConstCount; i++) b.Const[i] = constants.Children[i].AsFloat;

        AssetTypeValueField denseArray = dense["m_SampleArray"]["Array"];
        b.Dense = new float[denseArray.Children.Count];
        for (int i = 0; i < b.Dense.Length; i++) b.Dense[i] = denseArray.Children[i].AsFloat;

        AssetTypeValueField array = streamed["data"]["Array"];
        int count = array.Children.Count, at = 0;
        while (at + 2 <= count)
        {
            float time = AsFloat(array.Children[at]);
            int keyCount = (int)array.Children[at + 1].AsUInt;
            at += 2;
            if (keyCount < 0 || at + keyCount * 5 > count)
                throw new InvalidDataException("streamed bank does not parse at uint " + at);
            for (int k = 0; k < keyCount; k++)
            {
                int curve = (int)array.Children[at + k * 5].AsUInt;
                var key = new float[5];
                key[0] = time;
                for (int c = 0; c < 4; c++) key[c + 1] = AsFloat(array.Children[at + k * 5 + 1 + c]);
                if (curve >= 0 && curve < b.Keys.Length)
                {
                    if (b.Keys[curve] == null) b.Keys[curve] = new List<float[]>();
                    b.Keys[curve].Add(key);
                }
            }
            at += keyCount * 5;
        }
        if (at != count)
            throw new InvalidDataException("streamed bank consumed " + at + " of " + count + " uint(s)");
        return b;
    }

    private static float AsFloat(AssetTypeValueField f)
    {
        // The streamed bank is typed as uint but holds float bits; AsFloat on a uint field would
        // CONVERT the integer instead of reinterpreting it, which silently produces 1e9-sized times.
        return BitConverter.ToSingle(BitConverter.GetBytes(f.AsUInt), 0);
    }

    private static int Width(uint attribute)
    {
        return attribute == AttrPosition || attribute == AttrScale ? 3 : attribute == AttrRotation ? 4 : 1;
    }

    // -------------------------------------------------------------------------------- the pass

    private sealed class Track
    {
        internal string Path;
        internal float[][] Rot;     // per frame xyzw, Unity space
        internal float[][] Pos;     // per frame xyz, metres, or null when the curve was dropped
        internal float[][] Scl;     // per frame xyz, or null when the curve was unit
    }

    private sealed class Verdict
    {
        internal string Name, Refusal, Kind;
        internal int Frames;
        internal float Step, Sag, Hz;
        internal int PosKept, PosDropped, RotKept, ScaleDropped, ScaleKept, OtherBindings;
        internal List<Track> Tracks = new List<Track>();
        internal bool Bindable { get { return Refusal == null; } }
    }

    internal static int Run(string[] args)
    {
        // args: --export <tpk> <bundle> <rest.tsv> <out.json> [clips]
        if (args.Length < 5)
        {
            Console.Error.WriteLine("usage: ClipCensus --export <tpk> <bundle> <pp-rest.tsv> <out.json> [clip,clip,...]");
            return 2;
        }
        Dictionary<uint, Rest> rest = ReadRest(args[3]);
        var wanted = new HashSet<string>(StringComparer.Ordinal);
        if (args.Length > 5)
            foreach (string s in args[5].Split(','))
                if (s.Trim().Length > 0) wanted.Add(s.Trim());

        var manager = new AssetsManager();
        manager.LoadClassPackage(args[1]);
        BundleFileInstance bundle = manager.LoadBundleFile(args[2], true);
        AssetsFileInstance af = manager.LoadAssetsFileFromBundle(bundle, 0, false);
        manager.LoadClassDatabaseFromPackage(af.file.Metadata.UnityVersion);

        var all = new List<Verdict>();
        var exported = new List<Verdict>();
        int clips = 0, hashHits = 0, hashMisses = 0;
        try
        {
            foreach (AssetFileInfo info in af.file.Metadata.GetAssetsOfType(AssetClassID.AnimationClip))
            {
                AssetTypeValueField clip = manager.GetBaseField(af, info);
                string name = clip["m_Name"].IsDummy ? "?" : clip["m_Name"].AsString;
                clips++;
                // NO CLIP LIST MEANS EVERY CLIP THE PP RIG CAN CARRY. Naming a handful was right while
                // the question was "does one retargeted clip play at all"; it is wrong once the answer
                // is yes, because the sample IS the creature's whole repertoire. An omitted list takes
                // every clip that walks whole (a non-bindable one is still refused, just silently -
                // there is no author asking for it by name).
                bool keep = wanted.Count == 0 || wanted.Contains(name);
                Verdict v = Walk(clip, name, rest, keep, Fps, ref hashHits, ref hashMisses);
                all.Add(v);
                // A clip with no track left is not a thin clip, it is NO clip: glTF has no such thing
                // and GlbReader.Animation refuses "animation N has no channels, so it drives nothing".
                // The shipped set is full of them - Empty, AnimNotUsed and every *Placeholder state -
                // and they were harmless only while nobody asked for the whole set.
                // ...and a track with no CURVE on it is not a track: a Track is created for every
                // binding path, and a clip whose every curve was dropped as pinning or unit still
                // carries its paths. It is the curves that have to survive.
                if (keep && v.Bindable &&
                    v.Tracks.Exists(t => t.Rot != null || t.Pos != null || t.Scl != null))
                    exported.Add(v);
            }
        }
        finally { manager.UnloadAll(); }

        if (hashHits == 0)
            throw new InvalidDataException(
                "not one binding path hash in " + clips + " clip(s) matched a PP path, so the CRC " +
                "is not the hash Unity used and nothing below could be trusted");

        // A REQUESTED clip that cannot be exported whole is an error, never a thinner animation.
        var refused = all.FindAll(v => wanted.Contains(v.Name) && !v.Bindable);
        if (refused.Count > 0)
            throw new InvalidDataException("refused " + refused.Count + " requested clip(s): " +
                string.Join("; ", refused.ConvertAll(v => v.Name + " - " + v.Refusal).ToArray()));
        foreach (string w in wanted)
            if (!exported.Exists(e => e.Name == w))
                throw new InvalidDataException("clip '" + w + "' is not in this bundle");

        Write(args[4], exported);

        int posKept = 0, posDropped = 0, rot = 0, scaleDropped = 0, scaleKept = 0, rewritten = 0, ppRig = 0;
        var why = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (Verdict v in all)
        {
            if (!v.Bindable)
            {
                why.TryGetValue(v.Kind, out int had);
                why[v.Kind] = had + 1;
                continue;
            }
            ppRig++;
            posKept += v.PosKept; posDropped += v.PosDropped; rot += v.RotKept;
            scaleDropped += v.ScaleDropped; scaleKept += v.ScaleKept;
            if (v.PosDropped > 0 || v.ScaleDropped > 0) rewritten++;
        }
        Console.Error.WriteLine(
            "clips " + clips + " | wholly on the PP rig " + ppRig + " | refused " + (clips - ppRig) +
            " | path hashes " + hashHits + " resolved / " + hashMisses + " foreign");
        foreach (var pair in why) Console.Error.WriteLine("  refused: " + pair.Value + " x " + pair.Key);
        Console.Error.WriteLine(
            "rewritten " + rewritten + " clip(s): position curves dropped " + posDropped +
            " (pinning), kept " + posKept + " (moving); scale curves dropped " + scaleDropped +
            " (unit), KEPT " + scaleKept + " (animated); rotation curves untouched " + rot);
        float sag = 0f, hz = 0f;
        foreach (Verdict v in exported) { sag = Math.Max(sag, v.Sag); hz = Math.Max(hz, v.Hz); }
        Console.Error.WriteLine("exported " + exported.Count + " clip(s) at up to " + hz +
            " Hz, worst linearisation sag " + sag.ToString("0.#####", CultureInfo.InvariantCulture) +
            " (limit " + MaxSag + ") -> " + args[4]);
        return 0;
    }

    private static Verdict Walk(AssetTypeValueField clip, string name, Dictionary<uint, Rest> rest,
                                bool keep, float fps, ref int hits, ref int misses)
    {
        var v = new Verdict { Name = name, Hz = fps };
        Banks banks;
        try { banks = ReadBanks(clip); }
        catch (InvalidDataException e)
        {
            v.Kind = "the clip does not decode";
            v.Refusal = v.Kind + " (" + e.Message + ")";
            return v;
        }

        // Whole 1/fps instants, and a CEILING so the grid always reaches m_StopTime - the same rule
        // GlbReader's own resampler follows. Dividing the duration into frames-1 equal steps instead
        // would put the samples off the 120 Hz grid and drop the bake onto its 30 Hz fallback.
        v.Step = 1f / fps;
        v.Frames = banks.Duration > 0f ? (int)Math.Ceiling(banks.Duration * fps - 1e-4f) + 1 : 1;

        int flat = 0;
        foreach (AssetTypeValueField b in clip["m_ClipBindingConstant"]["genericBindings"]["Array"].Children)
            flat += Width(b["attribute"].AsUInt);
        if (flat != banks.Total)
        {
            // The same refusal ClipFields.MapCurves makes: if the widths do not add up, a curve
            // cannot be told from its neighbour and every index past the first odd binding is wrong.
            v.Kind = "its bindings do not add up to its curve banks";
            v.Refusal = v.Kind + " (" + flat + " float(s) bound, " + banks.Total + " held)";
            return v;
        }

        var tracks = new Dictionary<string, Track>(StringComparer.Ordinal);
        flat = 0;
        foreach (AssetTypeValueField b in clip["m_ClipBindingConstant"]["genericBindings"]["Array"].Children)
        {
            uint attribute = b["attribute"].AsUInt, path = b["path"].AsUInt;
            int typeId = b["typeID"].AsInt, width = Width(attribute);
            int offset = flat;
            flat += width;
            if (typeId != TransformTypeId) { v.OtherBindings++; continue; }

            Rest r;
            if (!rest.TryGetValue(path, out r))
            {
                misses++;
                if (v.Refusal == null)
                {
                    v.Kind = "it binds a transform the PP rig does not have";
                    v.Refusal = v.Kind + " (path hash " + path + ")";
                }
                continue;
            }
            hits++;
            if (!v.Bindable) continue;

            Track t;
            if (!tracks.TryGetValue(r.Path, out t))
            {
                t = new Track { Path = r.Path };
                tracks[r.Path] = t;
                v.Tracks.Add(t);
            }

            if (attribute == AttrRotation)
            {
                v.RotKept++;
                if (keep) t.Rot = Sample(banks, offset, 4, v, true);
                continue;
            }
            if (attribute == AttrScale)
            {
                // Unit scale is bake noise; anything else is a squash the animator MEANT.
                if (Still(banks, offset, 3, v, UnitScale) &&
                    Near(banks, offset, 3, new[] { 1f, 1f, 1f }, banks.Start, UnitScale))
                    v.ScaleDropped++;
                else
                {
                    v.ScaleKept++;
                    if (keep) t.Scl = Sample(banks, offset, 3, v, false);
                }
                continue;
            }
            if (attribute != AttrPosition) { v.OtherBindings++; continue; }

            // THE decision, made from this curve's own samples: a position curve that never moves
            // and sits within a millimetre of the prefab's rest offset states a SEGMENT LENGTH, not
            // an animation. Anything else - the root travelling, a real offset - is kept.
            if (Still(banks, offset, 3, v) && Near(banks, offset, 3, r.T, banks.Start, RestMetres))
                v.PosDropped++;
            else
            {
                v.PosKept++;
                if (keep) t.Pos = Sample(banks, offset, 3, v, false);
            }
        }
        if (!v.Bindable || !keep) v.Tracks.Clear();
        if (v.Bindable && v.Sag > MaxSag)
        {
            v.Kind = "linearising it at " + fps + " Hz would move a curve too far";
            v.Refusal = v.Kind + " (by " + v.Sag.ToString("0.#####", CultureInfo.InvariantCulture) + ")";
        }
        return v;
    }

    private static float Distance(Banks b, int offset, int width, float ta, float[] to)
    {
        double d = 0.0;
        for (int c = 0; c < width; c++)
        {
            double e = b.Eval(offset + c, ta) - to[c];
            d += e * e;
        }
        return (float)Math.Sqrt(d);
    }

    /// <summary>Never moves - judged on a grid PinSub times denser than the export's, because a
    /// cubic key can bulge between two sample instants and read as still at both of them.</summary>
    private static bool Still(Banks b, int offset, int width, Verdict v, float tolerance = StillMetres)
    {
        var first = new float[width];
        for (int c = 0; c < width; c++) first[c] = b.Eval(offset + c, b.Start);
        int steps = Math.Max(1, (v.Frames - 1) * PinSub);
        float step = v.Frames > 1 ? b.Duration / steps : 0f;
        for (int f = 1; f <= steps; f++)
            if (Distance(b, offset, width, b.Start + f * step, first) > tolerance) return false;
        return true;
    }

    private static bool Near(Banks b, int offset, int width, float[] want, float at, float tolerance)
    {
        return Distance(b, offset, width, at, want) <= tolerance;
    }

    /// <summary>Frame f's instant: whole 1/fps steps, held at m_StopTime by the ceiling frame.</summary>
    private static float At(Banks b, Verdict v, float f)
    {
        return Math.Min(b.Start + f * v.Step, b.Stop);
    }

    private static float[][] Sample(Banks b, int offset, int width, Verdict v, bool normalise)
    {
        var outp = new float[v.Frames][];
        for (int f = 0; f < v.Frames; f++)
        {
            var value = new float[width];
            for (int c = 0; c < width; c++) value[c] = b.Eval(offset + c, At(b, v, f));
            if (normalise)
            {
                Normalise(value);
                // Keys either side of a hemisphere flip interpolate the long way round; the .glb
                // gets one continuous hemisphere so LINEAR sampling between them is the short arc.
                if (f > 0)
                {
                    double dot = 0.0;
                    for (int c = 0; c < width; c++) dot += (double)value[c] * outp[f - 1][c];
                    if (dot < 0.0) for (int c = 0; c < width; c++) value[c] = -value[c];
                }
            }
            outp[f] = value;
        }
        v.Sag = Math.Max(v.Sag, Sag(b, offset, width, v, outp, normalise));
        return outp;
    }

    private static void Normalise(float[] q)
    {
        double n = 0.0;
        foreach (float c in q) n += (double)c * c;
        n = Math.Sqrt(n);
        if (n > 1e-9) for (int c = 0; c < q.Length; c++) q[c] = (float)(q[c] / n);
        else { q[0] = q[1] = q[2] = 0f; q[3] = 1f; }
    }

    /// <summary>What LINEAR interpolation between two exported samples costs against the curve's
    /// real cubic, measured at every midpoint. This is the number that says the timebase is dense
    /// enough - without it "we sample at 60 Hz" is a hope, not a bound.</summary>
    private static float Sag(Banks b, int offset, int width, Verdict v, float[][] taken, bool normalise)
    {
        float worst = 0f;
        for (int f = 0; f + 1 < v.Frames; f++)
        {
            float t = At(b, v, f + 0.5f);
            var truth = new float[width];
            for (int c = 0; c < width; c++) truth[c] = b.Eval(offset + c, t);
            if (normalise)
            {
                Normalise(truth);
                double dot = 0.0;
                for (int c = 0; c < width; c++) dot += (double)truth[c] * taken[f][c];
                if (dot < 0.0) for (int c = 0; c < width; c++) truth[c] = -truth[c];
            }
            for (int c = 0; c < width; c++)
                worst = Math.Max(worst, Math.Abs(truth[c] - 0.5f * (taken[f][c] + taken[f + 1][c])));
        }
        return worst;
    }

    // ------------------------------------------------------------------------------- the file

    private static void Write(string path, List<Verdict> clips)
    {
        var sb = new StringBuilder();
        sb.Append("{\"fps\":").Append(F(Fps)).Append(",\"space\":\"unity\",\"clips\":[");
        for (int i = 0; i < clips.Count; i++)
        {
            Verdict v = clips[i];
            if (i > 0) sb.Append(',');
            sb.Append("{\"name\":\"").Append(v.Name).Append("\",\"frames\":").Append(v.Frames)
              .Append(",\"posDropped\":").Append(v.PosDropped).Append(",\"posKept\":").Append(v.PosKept)
              .Append(",\"scaleDropped\":").Append(v.ScaleDropped).Append(",\"scaleKept\":").Append(v.ScaleKept)
              .Append(",\"rot\":").Append(v.RotKept)
              .Append(",\"hz\":").Append(F(v.Hz)).Append(",\"sag\":").Append(F(v.Sag));
            // The TIMES the samples were actually taken at. The .glb writes exactly these, so the
            // clip keeps its real m_StopTime length instead of frame/30 rounding it.
            sb.Append(",\"times\":[");
            for (int f = 0; f < v.Frames; f++)
            {
                if (f > 0) sb.Append(',');
                sb.Append(F(f * v.Step));       // the GRID instant; the last frame's VALUE is held at m_StopTime
            }
            sb.Append("],\"tracks\":[");
            for (int t = 0; t < v.Tracks.Count; t++)
            {
                Track k = v.Tracks[t];
                if (t > 0) sb.Append(',');
                sb.Append("{\"path\":\"").Append(k.Path).Append('"');
                if (k.Rot != null) { sb.Append(",\"rot\":"); Frames(sb, k.Rot); }
                if (k.Pos != null) { sb.Append(",\"pos\":"); Frames(sb, k.Pos); }
                if (k.Scl != null) { sb.Append(",\"scl\":"); Frames(sb, k.Scl); }
                sb.Append('}');
            }
            sb.Append("]}");
        }
        sb.Append("]}");
        File.WriteAllText(path, sb.ToString());
    }

    private static void Frames(StringBuilder sb, float[][] v)
    {
        sb.Append('[');
        for (int f = 0; f < v.Length; f++)
        {
            if (f > 0) sb.Append(',');
            sb.Append('[');
            for (int c = 0; c < v[f].Length; c++)
            {
                if (c > 0) sb.Append(',');
                sb.Append(F(v[f][c]));
            }
            sb.Append(']');
        }
        sb.Append(']');
    }

    private static string F(float v)
    {
        return v.ToString("R", CultureInfo.InvariantCulture);
    }

    // ------------------------------------------------------------------------------ --selfcheck
    // The bundle cannot exercise the dense bank - all 565 shipped clips are streamed plus constant -
    // so the one path with no live coverage gets a synthetic one, next to the two that do.
    internal static int SelfCheck()
    {
        var b = new Banks
        {
            StreamCurves = 1, DenseCurves = 1, ConstCount = 1,
            DenseFrames = 3, DenseBegin = 0f, DenseRate = 10f,
            Dense = new[] { 0f, 10f, 20f },        // one curve, frames 0/0.1/0.2 -> a ramp
            Const = new[] { 7f },
            Start = 0f, Stop = 1f,
            Keys = new List<float[]>[1],
        };
        b.Keys[0] = new List<float[]> { new[] { 0f, 1f, 0f, 0f, 0f } };   // v = dt^3

        Fail(Math.Abs(b.Eval(1, 0.15f) - 15f) < 1e-4f, "dense bank must INTERPOLATE: 0.15s of a " +
             "0..20 ramp is 15, not the nearest frame (" + b.Eval(1, 0.15f) + ")");
        Fail(Math.Abs(b.Eval(1, 0.05f) - 5f) < 1e-4f, "dense bank midpoint wrong: " + b.Eval(1, 0.05f));
        Fail(Math.Abs(b.Eval(1, -1f) - 0f) < 1e-4f && Math.Abs(b.Eval(1, 99f) - 20f) < 1e-4f,
             "dense bank must clamp outside its own range");
        Fail(Math.Abs(b.Eval(0, 0.5f) - 0.125f) < 1e-5f, "streamed key is a CUBIC in its four " +
             "coefficients: 0.5^3 = 0.125, got " + b.Eval(0, 0.5f));
        Fail(Math.Abs(b.Eval(2, 0.3f) - 7f) < 1e-6f, "constant bank must ignore time");
        Fail(Math.Abs(b.Duration - 1f) < 1e-6f, "duration is m_StopTime - m_StartTime, not what the banks happen to reach");

        // a constant-only clip still has a real length, and a still cubic-bulging curve is not still
        var only = new Banks { ConstCount = 3, Const = new[] { 1f, 2f, 3f }, Start = 0f, Stop = 2.5f,
                               Keys = new List<float[]>[0], Dense = new float[0] };
        Fail(Math.Abs(only.Duration - 2.5f) < 1e-6f, "a constant-only clip must not collapse to zero length");
        var bulge = new Banks { StreamCurves = 1, Start = 0f, Stop = 1f, Keys = new List<float[]>[1],
                                Dense = new float[0], Const = new float[0] };
        // v(dt) = dt^3 - 1.5 dt^2 + 0.5 dt: zero at t=0, at t=0.5 and at t=1, 0.048 in between
        bulge.Keys[0] = new List<float[]> { new[] { 0f, 1f, -1.5f, 0.5f, 0f } };
        var grid = new Verdict { Frames = 3, Step = 0.5f };
        Fail(!Still(bulge, 0, 1, grid), "a curve that bulges BETWEEN two sample instants must not " +
             "count as still - that is a pinning verdict on a moving bone");

        // The scale classifier, both ways round. The bundle exercises only one of them - all 28451
        // shipped scale curves turn out to be bake noise - so the branch that KEEPS a squash would
        // otherwise ship with no coverage at all.
        var scale = new Banks { ConstCount = 3, Start = 0f, Stop = 1f, Keys = new List<float[]>[0],
                                Dense = new float[0], Const = new[] { 1.000045f, 0.999955f, 1.000045f } };
        var one = new Verdict { Frames = 2, Step = 1f };
        Fail(Still(scale, 0, 3, one, UnitScale) && Near(scale, 0, 3, new[] { 1f, 1f, 1f }, 0f, UnitScale),
             "1.0 +-4.5e-5 per component is the bake's own noise and must read as UNIT scale");
        scale.Const = new[] { 1.2f, 1f, 1f };
        Fail(!Near(scale, 0, 3, new[] { 1f, 1f, 1f }, 0f, UnitScale),
             "a constant 1.2 is a SQUASH the animator meant and must be kept, not dropped with the noise");
        var ramp = new Banks { StreamCurves = 1, ConstCount = 2, Start = 0f, Stop = 1f,
                               Keys = new List<float[]>[1], Dense = new float[0], Const = new[] { 1f, 1f } };
        ramp.Keys[0] = new List<float[]> { new[] { 0f, 0f, 0f, 0.5f, 1f } };   // 1 -> 1.5 over the clip
        Fail(!Still(ramp, 0, 3, one, UnitScale), "an animated scale must not read as unit");

        Console.WriteLine("ClipCensus selfcheck OK: dense interpolation, cubic keys, constant bank, " +
                          "clip length from m_StartTime/m_StopTime, sub-grid pinning, scale kept vs dropped");
        return 0;
    }

    private static void Fail(bool ok, string what)
    {
        if (!ok) throw new InvalidDataException("SELFCHECK FAILED: " + what);
    }

    // -------------------------------------------------------------------------------- --fields

    internal static int Fields(string[] args)
    {
        if (args.Length < 4) { Console.Error.WriteLine("usage: ClipCensus --fields <tpk> <bundle> <clip>"); return 2; }
        var manager = new AssetsManager();
        manager.LoadClassPackage(args[1]);
        BundleFileInstance bundle = manager.LoadBundleFile(args[2], true);
        AssetsFileInstance af = manager.LoadAssetsFileFromBundle(bundle, 0, false);
        manager.LoadClassDatabaseFromPackage(af.file.Metadata.UnityVersion);
        try
        {
            foreach (AssetFileInfo info in af.file.Metadata.GetAssetsOfType(AssetClassID.AnimationClip))
            {
                AssetTypeValueField clip = manager.GetBaseField(af, info);
                if (clip["m_Name"].AsString != args[3]) continue;
                Banks b = ReadBanks(clip);
                Console.WriteLine("banks stream=" + b.StreamCurves + " dense=" + b.DenseCurves +
                                  " denseFrames=" + b.DenseFrames + " denseRate=" + b.DenseRate +
                                  " const=" + b.ConstCount + " start=" + b.Start + " stop=" + b.Stop);
                for (int c = 0; c < Math.Min(3, b.StreamCurves); c++)
                {
                    if (b.Keys[c] == null) { Console.WriteLine("curve " + c + ": no keys"); continue; }
                    var sb = new StringBuilder("curve " + c + " keys " + b.Keys[c].Count + " times");
                    for (int k = 0; k < Math.Min(6, b.Keys[c].Count); k++) sb.Append(' ').Append(b.Keys[c][k][0]);
                    Console.WriteLine(sb.ToString());
                }
                return 0;
            }
        }
        finally { manager.UnloadAll(); }
        Console.Error.WriteLine("no clip named " + args[3]);
        return 1;
    }
}
