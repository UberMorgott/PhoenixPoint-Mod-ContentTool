using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Morgott.ContentTool.Import;

/// <summary>
/// Gate <b>U10</b>: a .glb that arrives COMPRESSED is read with nothing but this mod. Until now
/// <see cref="GlbReader"/> refused every glTF extension, so the U8/U9 probe had to be run once
/// through <c>npx @gltf-transform/cli dequantize</c> - an external tool in the author's path, which
/// the project's own mandate calls a bug rather than the author's job.
///
/// The oracle is the strongest one available and it is not ours: <c>lib\u8_probe.glb</c> IS
/// <c>lib\u10_probe.glb</c> decompressed, by an INDEPENDENT implementation (gltf-transform's
/// JavaScript meshopt decoder). So the two files are the same model stated twice, and every number
/// this gate compares was produced by two decoders that share no line of code. A round trip through
/// our own writer could not do that; neither could a fixture we authored.
///
/// What the pair covers, measured from the file rather than hoped for: all three bitstream MODES
/// this mod implements are exercised except one - <c>ATTRIBUTES</c> on 12 of the 13 bufferViews and
/// <c>TRIANGLES</c> on the 13th - and ALL FOUR filters appear (<c>NONE</c>, <c>OCTAHEDRAL</c> on the
/// normals, <c>QUATERNION</c> on every rotation curve, <c>EXPONENTIAL</c> on every translation
/// curve). <c>INDICES</c> (mode 2) appears in neither, so it is asserted on hand-built bytes below.
///
/// The comparison is NOT vertex-position-by-vertex-position, and the reason is the whole point of
/// KHR_mesh_quantization: the compressed file keeps its positions as 16-bit integers and states the
/// scale back to real units in the skin's inverseBindMatrices, while the dequantized file folds that
/// scale into the positions and takes it back out of the matrices. Both describe the same model. The
/// quantity that survives is <c>inverseBindMatrix * position</c> - the vertex in BONE space, which is
/// exactly what skinning consumes - because the dequantization Q cancels: (IBM * Q^-1)(Q * p) = IBM * p.
/// </summary>
internal static class Compressed
{
    private const string Packed = "u10_probe.glb";     // EXT_meshopt_compression + KHR_mesh_quantization
    private const string Plain = "u8_probe.glb";       // the same model, decompressed by gltf-transform

    private static int checks;

    internal static string Run()
    {
        string packed = Find(Packed), plain = Find(Plain);
        if (packed == null || plain == null)
            return "COMPRESSED import VOID - no " + (packed == null ? Packed : Plain);

        var packedClips = new List<SampledClip>();
        var plainClips = new List<SampledClip>();
        SkinnedModel a = GlbReader.Read(File.ReadAllBytes(packed), packedClips);
        SkinnedModel b = GlbReader.Read(File.ReadAllBytes(plain), plainClips);

        string mesh = Mesh(a, b);
        string clips = Clips(packedClips, plainClips);
        string triangles = TriangleCodes();
        string sequence = Sequence();
        string refused = Refusals();
        return "COMPRESSED import PASS, " + checks + " check(s)\n  " + mesh + "\n  " + clips +
               "\n  " + triangles + "\n  " + sequence + "\n  " + refused;
    }

    // ------------------------------------------------------------------ the mesh

    private static string Mesh(SkinnedModel a, SkinnedModel b)
    {
        Assert(a.Positions.Length == b.Positions.Length,
               "the compressed file yields the same vertex count: " + a.Positions.Length + " vs " + b.Positions.Length);
        Assert(a.JointNames.Count == b.JointNames.Count, "the same bone count: " + a.JointNames.Count);
        for (int j = 0; j < a.JointNames.Count; j++)
        {
            Assert(a.JointNames[j] == b.JointNames[j], "bone " + j + " keeps its name: '" + a.JointNames[j] + "'");
            Assert(a.Nodes[j].Parent == b.Nodes[j].Parent,
                   "bone '" + a.JointNames[j] + "' keeps its parent: " + a.Nodes[j].Parent + " vs " + b.Nodes[j].Parent);
        }

        // --- TRIANGLES codec, mode 1. It is LOSSLESS, so this is an EXACT comparison of every index:
        // the edge/vertex FIFOs, the codeaux table and the high-watermark `next` all have to agree
        // with the encoder step for step, and the first place they do not shows up here.
        Assert(a.Submeshes.Count == b.Submeshes.Count, "the same submesh count: " + a.Submeshes.Count);
        int indices = 0, firstBad = -1;
        for (int s = 0; s < a.Submeshes.Count; s++)
        {
            int[] x = a.Submeshes[s], y = b.Submeshes[s];
            Assert(x.Length == y.Length, "submesh " + s + " has the same index count: " + x.Length + " vs " + y.Length);
            for (int i = 0; i < x.Length; i++)
            {
                if (x[i] != y[i] && firstBad < 0) firstBad = indices + i;
                Assert(x[i] == y[i], "every decompressed triangle index is EXACT: submesh " + s + " index " + i +
                       " is " + x[i] + ", not " + y[i] + " (first divergence at " + firstBad + " of " +
                       (indices + x.Length) + ")");
            }
            indices += x.Length;
        }

        // --- the vertex, in BONE space: the quantity the dequantization transform cannot change.
        Assert(a.InverseBindMatrices != null && b.InverseBindMatrices != null, "both files carry bind poses");
        double worstVertex = 0.0;
        int span = 0;
        for (int v = 0; v < a.Positions.Length; v++)
        {
            for (int k = 0; k < 4; k++)
            {
                Assert(a.Joints[v * 4 + k] == b.Joints[v * 4 + k],
                       "vertex " + v + " slot " + k + " names the same bone: " + a.Joints[v * 4 + k] + " vs " + b.Joints[v * 4 + k]);
                double weight = Math.Abs(a.Weights[v * 4 + k] - b.Weights[v * 4 + k]);
                Assert(weight < 2.0 / 255.0, "vertex " + v + " slot " + k + " keeps its weight: " +
                       a.Weights[v * 4 + k] + " vs " + b.Weights[v * 4 + k]);
                int joint = a.Joints[v * 4 + k];
                if (a.Weights[v * 4 + k] <= 0f) continue;
                double[] p = Apply(a.InverseBindMatrices[joint], a.Positions[v]);
                double[] q = Apply(b.InverseBindMatrices[joint], b.Positions[v]);
                double d = Math.Abs(p[0] - q[0]) + Math.Abs(p[1] - q[1]) + Math.Abs(p[2] - q[2]);
                if (d > worstVertex) worstVertex = d;
                double reach = Math.Abs(q[0]) + Math.Abs(q[1]) + Math.Abs(q[2]);
                if (reach > span) span = (int)reach;
            }
        }
        // 16-bit positions over a model whose bones reach single-digit units: a whole quantization
        // step is well under this, so a decoder off by one delta byte in one block cannot hide here.
        Assert(worstVertex < 0.02, "every vertex lands in the same place in BONE space, within 16-bit " +
               "quantization: worst total error " + F(worstVertex));

        // --- OCTAHEDRAL filter. The compressed normals are 8-bit octahedral pairs the filter unfolds;
        // the plain file's are floats gltf-transform unfolded from the SAME pairs. Anything but the
        // exact octahedral fold - including the copysign sign, which is a no-op for the whole UPPER
        // hemisphere and only wrong below it - separates them here.
        Assert(a.Normals != null && b.Normals != null, "both files carry normals");
        double worstNormal = 0.0, worstComponent = 0.0;
        int lower = 0, differ = 0;
        for (int i = 0; i < a.Normals.Length; i++)
        {
            // DIRECTION only. Neither file's normals are unit length - both are integer components
            // over their own scale (c/127), so a length comparison would measure the quantization
            // grid rather than the fold.
            double dot = Dot(a.Normals[i], b.Normals[i]) / (Norm(a.Normals[i]) * Norm(b.Normals[i]));
            double err = Math.Abs(1.0 - dot);
            if (err > worstNormal) worstNormal = err;
            double component = Math.Max(Math.Abs(a.Normals[i].X - b.Normals[i].X),
                               Math.Max(Math.Abs(a.Normals[i].Y - b.Normals[i].Y),
                                        Math.Abs(a.Normals[i].Z - b.Normals[i].Z)));
            if (component > worstComponent) worstComponent = component;
            if (component > 1e-6) differ++;
            if (b.Normals[i].Z < -0.05f) lower++;
        }
        // Anti-vacuity for the copysign: the fold only differs where z < 0, so the fixture has to
        // CONTAIN such normals or the arm above measures nothing.
        Assert(lower > 100, "the fixture actually contains normals in the folded hemisphere: " + lower +
               " of " + a.Normals.Length);
        // 1/127 is ONE step of the 8-bit grid both files land on. The two decoders are allowed to
        // disagree by a step (the extension states its filters "to one unit in last place"); a fold
        // that is actually wrong misses by a hemisphere, not by a step.
        Assert(worstComponent <= 1.01 / 127.0, "every octahedral normal unfolds within one 8-bit step: worst " +
               "component " + F(worstComponent) + " (" + F(worstComponent * 127.0) + " step(s)), " + differ +
               " of " + a.Normals.Length + " differ at all");
        Assert(worstNormal < 0.0002, "and to the same DIRECTION: worst 1-dot " + F(worstNormal));

        return "mesh " + Packed + " (130436 B, compressed) == " + Plain + " (349468 B, decompressed by " +
               "gltf-transform): " + a.Positions.Length + " vertices, " + a.JointNames.Count + " bones, " +
               indices + " triangle indices EXACT | worst bone-space vertex error " + F(worstVertex) +
               " | normals: worst 1-dot " + F(worstNormal) + ", worst component " + F(worstComponent) +
               " = " + F(worstComponent * 127.0) + " of one 8-bit step, " + differ + " of " + a.Normals.Length +
               " differ, " + lower + " in the folded hemisphere";
    }

    private static double Dot(ObjVector3 a, ObjVector3 b) =>
        (double)a.X * b.X + (double)a.Y * b.Y + (double)a.Z * b.Z;

    private static double Norm(ObjVector3 a)
    {
        double length = Math.Sqrt(Dot(a, a));
        return length <= 1e-12 ? 1.0 : length;
    }

    private static double[] Apply(float[] m, ObjVector3 p)
    {
        // glTF stores a matrix column-major, which is how GlbReader's own Bake() reads one.
        return new[]
        {
            m[0] * (double)p.X + m[4] * (double)p.Y + m[8] * (double)p.Z + m[12],
            m[1] * (double)p.X + m[5] * (double)p.Y + m[9] * (double)p.Z + m[13],
            m[2] * (double)p.X + m[6] * (double)p.Y + m[10] * (double)p.Z + m[14],
        };
    }

    // ------------------------------------------------------------------ the clips

    /// <summary>
    /// Every rotation curve of this file rides the QUATERNION filter and every translation curve the
    /// EXPONENTIAL one, so the five clips are where those two are measured. Neither is lossy in the
    /// direction that matters here: the plain file's numbers are what an independent decoder got out
    /// of the very same bits, so agreement is cross-validation and disagreement is a bug in one of us.
    /// </summary>
    private static string Clips(List<SampledClip> a, List<SampledClip> b)
    {
        Assert(a.Count == b.Count && a.Count == 5, "both files carry the same five clips: " + a.Count + " vs " + b.Count);
        double worstRotation = 0.0, worstTranslation = 0.0, worstScale = 0.0;
        int rotations = 0, translations = 0, scales = 0, negative = 0;
        for (int c = 0; c < a.Count; c++)
        {
            Assert(a[c].Name == b[c].Name, "clip " + c + " keeps its name: '" + a[c].Name + "' vs '" + b[c].Name + "'");
            Assert(a[c].Times.Length == b[c].Times.Length,
                   "clip '" + a[c].Name + "' has the same frame count: " + a[c].Times.Length + " vs " + b[c].Times.Length);
            Assert(Math.Abs(a[c].SampleRate - b[c].SampleRate) < 1e-4,
                   "clip '" + a[c].Name + "' derives the same rate: " + F(a[c].SampleRate) + " vs " + F(b[c].SampleRate));
            Assert(a[c].Tracks.Count == b[c].Tracks.Count,
                   "clip '" + a[c].Name + "' drives the same bone count: " + a[c].Tracks.Count + " vs " + b[c].Tracks.Count);
            for (int t = 0; t < a[c].Tracks.Count; t++)
            {
                SampledTrack x = a[c].Tracks[t], y = b[c].Tracks[t];
                Assert(x.Node == y.Node, "clip '" + a[c].Name + "' track " + t + " drives the same bone slot: " +
                       x.Node + " vs " + y.Node);
                if (x.Rotations != null)
                {
                    Assert(y.Rotations != null, "clip '" + a[c].Name + "' track " + t + " rotates in both files");
                    for (int f = 0; f < x.Rotations.Length; f++)
                    {
                        // |dot|, because q and -q are the same rotation and the max-component encoding
                        // is free to pick either sign back.
                        double dot = Math.Abs((double)x.Rotations[f].X * y.Rotations[f].X +
                                              (double)x.Rotations[f].Y * y.Rotations[f].Y +
                                              (double)x.Rotations[f].Z * y.Rotations[f].Z +
                                              (double)x.Rotations[f].W * y.Rotations[f].W);
                        double err = Math.Abs(1.0 - dot);
                        if (err > worstRotation) worstRotation = err;
                        rotations++;
                    }
                }
                if (x.Translations != null)
                {
                    Assert(y.Translations != null, "clip '" + a[c].Name + "' track " + t + " translates in both files");
                    for (int f = 0; f < x.Translations.Length; f++)
                    {
                        double d = Math.Abs(x.Translations[f].X - y.Translations[f].X) +
                                   Math.Abs(x.Translations[f].Y - y.Translations[f].Y) +
                                   Math.Abs(x.Translations[f].Z - y.Translations[f].Z);
                        if (d > worstTranslation) worstTranslation = d;
                        // Anti-vacuity for the exponential filter's SIGN: its mantissa is a signed
                        // 24-bit field, and reading it unsigned is exact for every positive value.
                        if (y.Translations[f].X < -1e-6f || y.Translations[f].Y < -1e-6f || y.Translations[f].Z < -1e-6f) negative++;
                        translations++;
                    }
                }
                if (x.Scales == null) continue;
                Assert(y.Scales != null, "clip '" + a[c].Name + "' track " + t + " scales in both files");
                for (int f = 0; f < x.Scales.Length; f++)
                {
                    double d = Math.Abs(x.Scales[f].X - y.Scales[f].X) + Math.Abs(x.Scales[f].Y - y.Scales[f].Y) +
                               Math.Abs(x.Scales[f].Z - y.Scales[f].Z);
                    if (d > worstScale) worstScale = d;
                    scales++;
                }
            }
        }
        Assert(rotations > 1000 && translations > 100, "the clips actually carry curves to compare: " +
               rotations + " rotation and " + translations + " translation sample(s)");
        Assert(negative > 100, "the exponential curves actually contain NEGATIVE values, so reading their " +
               "mantissa unsigned is measurable: " + negative + " of " + translations);
        Assert(worstRotation < 1e-6, "every QUATERNION-filtered rotation sample matches: worst 1-|dot| " + F(worstRotation));
        Assert(worstTranslation < 1e-6, "every EXPONENTIAL-filtered translation sample matches: worst " + F(worstTranslation));
        Assert(worstScale < 1e-6, "every scale sample matches: worst " + F(worstScale));

        return "clips: 5 identical | " + rotations + " QUATERNION rotation sample(s) worst 1-|dot| " +
               F(worstRotation) + " | " + translations + " EXPONENTIAL translation sample(s) (" + negative +
               " negative) worst " + F(worstTranslation) + " | " + scales + " scale sample(s) worst " + F(worstScale);
    }

    // ------------------------------------------------------------------ the triangle codes the probe never uses

    /// <summary>
    /// The probe decodes IDENTICALLY whether the <c>0xXd</c>/<c>0xXe</c> codes are read as
    /// "<c>last-1</c> / <c>last+1</c>" (index codec v1, which the header byte <c>0xe1</c> pins) or as
    /// two more vertex-FIFO slots (v0) - MEASURED, by making that exact substitution and watching all
    /// 8136 indices still match. gltfpack simply never emits those two codes for this mesh, so the
    /// version fact the header carries would have gone into the tree UNFALSIFIED.
    ///
    /// So the stream below is assembled here, by hand, to use them - and to use the two neighbours
    /// that share their branch: <c>0xXf</c>, an index stated explicitly as a zigzag varint delta from
    /// <c>last</c>, and <c>0xfe</c> with a zero codeaux, which resets the <c>next</c> high-watermark.
    /// Every one of the four triangles below therefore turns on a different rule.
    /// </summary>
    private static string TriangleCodes()
    {
        //  0xfe + codeaux 0x00 -> reset next, three brand new vertices          -> (0, 1, 2)
        //  0x0f -> newest edge (0, 2), third index explicit: last 0 + delta 5   -> (0, 2, 5)
        //  0x0e -> newest edge (0, 5), third index last + 1                     -> (0, 5, 6)
        //  0x0d -> newest edge (0, 6), third index last - 1                     -> (0, 6, 5)
        var stream = new List<byte> { 0xe1, 0xfe, 0x0f, 0x0e, 0x0d, 0x00, 0x0a };
        stream.AddRange(new byte[16]);                 // the codeaux table, unread by these codes
        int[] want = { 0, 1, 2, 0, 2, 5, 0, 5, 6, 0, 6, 5 };
        byte[] source = stream.ToArray();
        byte[] got = Meshopt.Decode(source, 0, source.Length, want.Length, 4, "TRIANGLES", null, "the hand-built triangles");
        for (int i = 0; i < want.Length; i++)
            Assert(BitConverter.ToInt32(got, i * 4) == want[i],
                   "hand-built TRIANGLES stream: index " + i + " is " + BitConverter.ToInt32(got, i * 4) +
                   ", not " + want[i] + " (reading 0xXd/0xXe as vertex-FIFO slots instead of last-1/last+1 " +
                   "is exactly what this arm exists to catch)");
        return "hand-built TRIANGLES (mode 1) stream: 0xfe reset, 0xXf explicit delta, 0xXe last+1, " +
               "0xXd last-1 -> " + string.Join(",", Array.ConvertAll(want, x => x.ToString(CultureInfo.InvariantCulture)));
    }

    // ------------------------------------------------------------------ mode 2, which neither file uses

    /// <summary>
    /// Mode 2 (INDICES) appears in neither probe - gltfpack writes mode 1 for a triangle list - so it
    /// is asserted on a stream built HERE from the encoder the specification's own Appendix A states,
    /// with a sequence chosen so the format's distinguishing feature actually matters: TWO running
    /// baselines picked by the delta's low bit. A decoder that kept a single `last` decodes the first
    /// two indices correctly and then diverges, which is why the sequence interleaves two runs rather
    /// than climbing once.
    /// </summary>
    private static string Sequence()
    {
        int[] want = { 0, 1000, 1, 1001, 2, 1002, 3, 1003, 5, 900 };
        var stream = new List<byte> { 0xd1 };
        var last = new int[2];
        for (int i = 0; i < want.Length; i++)
        {
            int baseline = i & 1;                    // alternate, which is what the low bit is for
            int delta = want[i] - last[baseline];
            last[baseline] = want[i];
            uint zigzag = (uint)((delta << 1) ^ (delta >> 31));
            uint v = (zigzag << 1) | (uint)baseline;
            do { stream.Add((byte)((v & 127) | (v > 127 ? 128u : 0u))); v >>= 7; } while (v != 0);
        }
        stream.AddRange(new byte[] { 0, 0, 0, 0 });
        byte[] source = stream.ToArray();
        byte[] got = Meshopt.Decode(source, 0, source.Length, want.Length, 4, "INDICES", null, "the hand-built sequence");
        for (int i = 0; i < want.Length; i++)
            Assert(BitConverter.ToInt32(got, i * 4) == want[i],
                   "hand-built INDICES stream decodes its two interleaved baselines: index " + i + " is " +
                   BitConverter.ToInt32(got, i * 4) + ", not " + want[i]);
        return "hand-built INDICES (mode 2) stream: " + string.Join(",", Array.ConvertAll(want, x => x.ToString(CultureInfo.InvariantCulture))) +
               " over two interleaved baselines, " + source.Length + " B";
    }

    // ------------------------------------------------------------------ what is still refused, by name

    /// <summary>
    /// A refusal is part of the deliverable: an author who downloads a Draco file did nothing wrong,
    /// and "unsupported format" tells them nothing. Each arm asserts the message names the extension
    /// AND says what to do about it, so a refusal that degraded to a shrug reads RED here.
    /// </summary>
    private static string Refusals()
    {
        // KHR_draco_mesh_compression used to be refused here. It is DECODED as of U12
        // (DracoTests), so the arm now asserts the opposite: the document-level guard lets it
        // through, and what stops this fixture is its empty mesh list - i.e. the refusal moved on
        // rather than being deleted. Leaving the old arm green would have hidden that.
        Accept("KHR_draco_mesh_compression", "no mesh");
        Refuse("KHR_texture_transform", "texture coordinates", "Blender");
        Refuse("EXT_made_up_extension", "EXT_made_up_extension", "Blender");
        return "Draco accepted at the document guard; refusals still name the extension and the fix: " +
               "KHR_texture_transform, unknown";
    }

    /// <summary>
    /// An extension the document guard now LETS THROUGH: the same fixture, refused for a different
    /// reason further down. Asserting the later message keeps this from passing on a reader that
    /// simply stopped checking extensionsRequired at all.
    /// </summary>
    private static void Accept(string extension, string laterReason)
    {
        string json = "{\"asset\":{\"version\":\"2.0\"},\"extensionsRequired\":[\"" + extension +
                      "\"],\"buffers\":[{\"byteLength\":4}],\"meshes\":[],\"nodes\":[]}";
        try
        {
            GlbReader.Read(Glb(json));
            throw new Exception("Expected the empty-mesh refusal for " + extension);
        }
        catch (FormatException exception)
        {
            Assert(exception.Message.IndexOf(extension, StringComparison.Ordinal) < 0,
                   "'" + extension + "' is no longer refused by name: " + exception.Message);
            Assert(exception.Message.IndexOf(laterReason, StringComparison.OrdinalIgnoreCase) >= 0,
                   "and the file stops for its own reason instead: " + exception.Message);
        }
    }

    private static void Refuse(string extension, string mustSay, string mustAlsoSay)
    {
        // A .glb whose JSON is valid up to the extension guard, so the refusal under test is the one
        // that fires - not a missing-mesh complaint from further down.
        string json = "{\"asset\":{\"version\":\"2.0\"},\"extensionsRequired\":[\"" + extension +
                      "\"],\"buffers\":[{\"byteLength\":4}],\"meshes\":[],\"nodes\":[]}";
        try
        {
            GlbReader.Read(Glb(json));
            throw new Exception("Expected a refusal for " + extension);
        }
        catch (FormatException exception)
        {
            Assert(exception.Message.IndexOf(extension, StringComparison.Ordinal) >= 0,
                   "the refusal names '" + extension + "': " + exception.Message);
            Assert(exception.Message.IndexOf(mustSay, StringComparison.OrdinalIgnoreCase) >= 0,
                   "the refusal says what '" + extension + "' means: " + exception.Message);
            Assert(exception.Message.IndexOf(mustAlsoSay, StringComparison.OrdinalIgnoreCase) >= 0,
                   "the refusal names the tool the author fixes it in: " + exception.Message);
        }
    }

    private static byte[] Glb(string json)
    {
        byte[] text = System.Text.Encoding.UTF8.GetBytes(json);
        int padded = (text.Length + 3) & ~3;
        var file = new byte[12 + 8 + padded + 8 + 4];
        Write(file, 0, 0x46546C67); Write(file, 4, 2); Write(file, 8, file.Length);
        Write(file, 12, padded); Write(file, 16, 0x4E4F534A);
        Array.Copy(text, 0, file, 20, text.Length);
        for (int i = text.Length; i < padded; i++) file[20 + i] = 0x20;
        Write(file, 20 + padded, 4); Write(file, 24 + padded, 0x004E4942);
        return file;
    }

    private static void Write(byte[] file, int at, int value)
    {
        file[at] = (byte)value; file[at + 1] = (byte)(value >> 8);
        file[at + 2] = (byte)(value >> 16); file[at + 3] = (byte)(value >> 24);
    }

    // ------------------------------------------------------------------ plumbing

    private static string Find(string name)
    {
        string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"..\..\..\..\..\lib\" + name);
        return File.Exists(path) ? path : null;
    }

    private static string F(double value) => value.ToString("0.#######", CultureInfo.InvariantCulture);

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new Exception("COMPRESSED import FAILED: " + message);
        checks++;
    }
}
