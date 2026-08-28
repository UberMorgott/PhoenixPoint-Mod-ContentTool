using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Morgott.ContentTool.Import;

/// <summary>
/// Gate <b>U12</b>: KHR_draco_mesh_compression, decoded in-house, so a Draco download needs no
/// round trip through Blender to become a Phoenix Point asset.
///
/// THE ORACLE IS NOT OURS, the U10 arrangement repeated. Each fixture is measured against the SAME
/// mesh decompressed by glTF-Transform's Draco decoder - Google's own draco3d WebAssembly build,
/// sharing no line of code with <see cref="Draco"/> - so every number below is cross-validation
/// between two independent decoders, not a round trip through our own writer.
///
/// TWO fixtures, because one of them cannot exercise the format's most intricate corner:
///  - <c>u12_uv.glb</c> is a REAL third-party Draco stream: Khronos' own CC0 "Avocado", glTF-Draco
///    variant, repacked byte-for-byte into a .glb. It carries TEXCOORD_0 and TANGENT, so it is the
///    only fixture that runs MESH_PREDICTION_TEX_COORDS_PORTABLE - the predictor that walks back
///    through the POSITION attribute's own portable values and an integer square root.
///  - <c>u12_probe.glb</c> is the CC0 spider this suite already uses, Draco-encoded by Google's
///    reference encoder. It carries a 39-bone skin, JOINTS_0/WEIGHTS_0 (the INTEGER decoder path,
///    which is not the quantized one), TWO primitives in one compressed block, and five animation
///    clips that Draco does not touch - the proof that the rest of the file survives the detour.
///    Its oracle is <c>u8_probe.glb</c>, which is already in this repo and is what was compressed.
/// </summary>
internal static class DracoTests
{
    internal static string Run()
    {
        string uv = Textured();
        string normals = GeometricNormals();
        string rig = Rigged();
        string hostile = Hostile();
        return "DRACO import PASS\n  " + uv + "\n  " + normals + "\n  " + rig + "\n  " + hostile;
    }

    // ------------------------------------------------------------------ the trust boundary

    /// <summary>
    /// The two ways a HOSTILE .glb - one a player downloaded and dropped into a mod folder - gets
    /// past a decoder that only checks lengths. Both fixtures are built here from bytes, so neither
    /// costs the repo a file.
    ///
    /// (1) A TWENTY-SIX-BYTE stream that declares two billion points. The sequential header states
    ///     the point count outright, and every array the decoder builds afterwards is one entry per
    ///     point, so an unchecked count is an out-of-memory inside the game process for the price of
    ///     five bytes. It must be refused BY NAME, before anything is sized from it.
    /// (2) A mapping that is WRONG BUT LENGTH-COMPATIBLE: the Avocado with POSITION and NORMAL
    ///     swapped inside the extension's own attribute map. Both are VEC3/FLOAT with one value per
    ///     point, so the value COUNT matches exactly - which is the whole point. A decoder that
    ///     checks only the count accepts it and hands the game a mesh whose vertices are its normals.
    /// </summary>
    private static string Hostile()
    {
        var stream = new List<byte>();
        foreach (char c in "DRACO") stream.Add((byte)c);
        stream.Add(2); stream.Add(2);                 // bitstream 2.2
        stream.Add(1);                                // encoder type MESH
        stream.Add(0);                                // MESH_SEQUENTIAL_ENCODING
        stream.Add(0); stream.Add(0);                 // flags: no metadata
        stream.Add(0);                                // faces: none, so the file is all header
        Leb(stream, 2000000000);                      // points: two billion, in five bytes
        stream.Add(1);                                // however it claims to store its indices
        // ...and one attribute decoder, so the stream really does reach the code that builds one
        // entry per point. Without this the file would die as "truncated" and prove nothing.
        stream.Add(1);                                // one attribute decoder
        stream.Add(1);                                // holding one attribute
        stream.Add(0); stream.Add(9); stream.Add(3); stream.Add(0);   // POSITION, float32, VEC3, plain
        stream.Add(0);                                // its unique id
        stream.Add(0);                                // stored by the GENERIC sequential decoder
        byte[] hostile = stream.ToArray();
        long before = GC.GetTotalMemory(true);
        string one = Refused(() => Draco.Decode(hostile, 0, hostile.Length, "hostile.glb"));
        long grew = (GC.GetTotalMemory(false) - before) / (1024 * 1024);
        Assert(one.Contains("2000000000") && one.Contains("1000000"),
               "a " + hostile.Length + "-byte stream claiming two billion points is refused by name " +
               "and by ceiling: \"" + one + "\"");
        // The refusal has to come BEFORE the allocation, which is the whole finding: with the ceiling
        // lifted, the same stream cut to 5 000 000 points allocated 89 MB before dying of something
        // else, so the fixture's two billion would be ~35 GB.
        Assert(grew < 8, "and refused it before allocating for it: " + grew + " MB");

        byte[] file = File.ReadAllBytes(Path.Combine(Lib(), "u12_uv.glb"));
        int jsonLength = BitConverter.ToInt32(file, 12);
        string json = System.Text.Encoding.UTF8.GetString(file, 20, jsonLength);
        const string Real = "\"attributes\":{\"TEXCOORD_0\":0,\"NORMAL\":1,\"TANGENT\":2,\"POSITION\":3}";
        const string Swapped = "\"attributes\":{\"TEXCOORD_0\":0,\"NORMAL\":3,\"TANGENT\":2,\"POSITION\":1}";
        Assert(json.Contains(Real), "the fixture's compressed attribute map is the one this arm swaps");
        byte[] patched = System.Text.Encoding.UTF8.GetBytes(json.Replace(Real, Swapped));
        Assert(patched.Length == jsonLength, "the swap changes no length anywhere: " + patched.Length +
               " vs " + jsonLength);
        Array.Copy(patched, 0, file, 20, jsonLength);
        string two = Refused(() => GlbReader.Read(file, new List<SampledClip>()));
        // Named, not merely thrown: the message has to say the mapping is wrong, NOT that a count
        // disagreed - a count arm cannot fire here, which is what makes this fixture worth having.
        Assert(two.Contains("the file itself calls") && !two.Contains("values but"),
               "the swapped pair is refused for what it IS, not for its length: \"" + two + "\"");

        return "hostile: " + hostile.Length + "-byte stream declaring 2000000000 points REFUSED by " +
               "name; the Avocado with POSITION<->NORMAL swapped in its own map (406 x VEC3/FLOAT " +
               "either way, so every length still matches) REFUSED: \"" + two + "\"";
    }

    private static void Leb(List<byte> into, ulong value)
    {
        do { byte b = (byte)(value & 0x7F); value >>= 7; into.Add((byte)(value != 0 ? b | 0x80 : b)); }
        while (value != 0);
    }

    private static string Refused(Action what)
    {
        try { what(); }
        catch (FormatException e) { return e.Message; }
        throw new Exception("DRACO import FAILED: a hostile fixture was ACCEPTED");
    }

    // ------------------------------------------------------------------ the geometric-normal predictor

    /// <summary>
    /// The one predictor the other two fixtures cannot reach.
    /// MESH_PREDICTION_GEOMETRIC_NORMAL predicts a vertex normal from the POSITIONS around it - it
    /// sums the cross products of the whole fan, canonicalizes the result onto an octahedron and
    /// then reads a per-vertex FLIP bit - and it is what 261 of the 263 real Draco primitives
    /// measured for this gate use. The Avocado is one of the two that do NOT: its normals come back
    /// through PREDICTION_DIFFERENCE, so with that fixture alone the whole predictor would go in
    /// unmeasured against any oracle.
    ///
    /// The fixture is Khronos' CC0 "BarramundiFish", glTF-Draco variant, repacked the same way; the
    /// oracle is that file decoded by glTF-Transform. The arm ASSERTS the scheme the file used, so
    /// it cannot pass while measuring a path this fixture never took either.
    /// </summary>
    private static string GeometricNormals()
    {
        SkinnedModel ours = Read("u12_norm.glb"), oracle = Read("u12_norm_plain.glb");
        if (ours == null || oracle == null) return "geometric normals VOID - no lib\\u12_norm.glb";

        int scheme = Scheme("u12_norm.glb", 1);      // draco's GeometryAttribute::Type NORMAL = 1
        Assert(scheme == 6, "the fixture's normals really are predicted from its geometry " +
               "(MESH_PREDICTION_GEOMETRIC_NORMAL = 6), not simply differenced: scheme " + scheme);

        Assert(ours.Positions.Length == oracle.Positions.Length && ours.Normals != null,
               "the compressed file holds the same vertices and carries normals: " +
               ours.Positions.Length + " vs " + oracle.Positions.Length);
        double worstNormal = 0, worstPosition = 0;
        int folded = 0;
        for (int v = 0; v < ours.Positions.Length; v++)
        {
            worstPosition = Math.Max(worstPosition, Distance(ours.Positions[v], oracle.Positions[v]));
            worstNormal = Math.Max(worstNormal, Distance(ours.Normals[v], oracle.Normals[v]));
            // The canonicalized octahedron folds anything with a negative first component; a fixture
            // whose normals all pointed one way would not measure the fold at all.
            if (ours.Normals[v].X < 0) folded++;
        }
        Assert(worstNormal < 1e-5, "every geometric-normal prediction matches glTF-Transform's own " +
               "decode: worst " + F(worstNormal));
        Assert(worstPosition < 1e-5, "and so does every position: worst " + F(worstPosition));
        Assert(folded > 100, "the fixture actually contains normals in the folded half of the " +
               "octahedron: " + folded + " of " + ours.Positions.Length);

        int indices = 0;
        for (int s = 0; s < ours.Submeshes.Count; s++)
        {
            Assert(ours.Submeshes[s].Length == oracle.Submeshes[s].Length, "same triangle count");
            for (int i = 0; i < ours.Submeshes[s].Length; i++)
            {
                Assert(ours.Submeshes[s][i] == oracle.Submeshes[s][i],
                       "triangle index " + i + ": " + ours.Submeshes[s][i] + " vs " + oracle.Submeshes[s][i]);
                indices++;
            }
        }

        return "real Khronos Draco .glb u12_norm.glb (CC0 BarramundiFish, geometric normals): " +
               ours.Positions.Length + " vertices, " + indices + " indices EXACT | worst normal " +
               F(worstNormal) + ", position " + F(worstPosition) + ", " + folded + " folded normal(s)";
    }

    /// <summary>
    /// The prediction scheme a fixture's attribute of type <paramref name="type"/> actually used,
    /// read from the decoder itself rather than assumed - the .glb's own Draco block, decoded once
    /// more straight through <see cref="Draco"/>.
    /// </summary>
    private static int Scheme(string name, int type)
    {
        byte[] file = File.ReadAllBytes(Path.Combine(Lib(), name));
        int jsonLength = BitConverter.ToInt32(file, 12);
        var root = (Dictionary<string, object>)Json.Parse(
            System.Text.Encoding.UTF8.GetString(file, 20, jsonLength), 64);
        int binAt = 20 + jsonLength + 8;
        var mesh = (Dictionary<string, object>)((List<object>)root["meshes"])[0];
        var primitive = (Dictionary<string, object>)((List<object>)mesh["primitives"])[0];
        var extensions = (Dictionary<string, object>)primitive["extensions"];
        var block = (Dictionary<string, object>)extensions["KHR_draco_mesh_compression"];
        var view = (Dictionary<string, object>)((List<object>)root["bufferViews"])
            [(int)Convert.ToInt64(block["bufferView"])];
        object offset;
        view.TryGetValue("byteOffset", out offset);
        int at = binAt + (offset == null ? 0 : (int)Convert.ToInt64(offset));
        int length = (int)Convert.ToInt64(view["byteLength"]);
        // The attribute's own id inside the compressed data, from the extension's own map.
        var names = (Dictionary<string, object>)block["attributes"];
        int id = (int)Convert.ToInt64(names["NORMAL"]);
        foreach (Draco.Attribute attribute in Draco.Decode(file, at, length, name).Attributes)
            if (attribute.UniqueId == id) return attribute.Prediction;
        throw new Exception("DRACO import FAILED: " + name + " carries no attribute of type " + type);
    }

    // ------------------------------------------------------------------ a real Khronos Draco file

    /// <summary>
    /// The compressed file and the SAME file decompressed by glTF-Transform have to agree value for
    /// value: both hold the quantized numbers Draco stores, so this arm is EXACT, not tolerant.
    /// </summary>
    private static string Textured()
    {
        SkinnedModel ours = Read("u12_uv.glb"), oracle = Read("u12_uv_plain.glb");
        if (ours == null || oracle == null) return "textured VOID - no lib\\u12_uv.glb";

        int checks = 0;
        Assert(ours.Positions.Length == oracle.Positions.Length,
               "the compressed file holds the same vertex count: " + ours.Positions.Length + " vs " +
               oracle.Positions.Length);
        Assert(ours.Uv0 != null && oracle.Uv0 != null && ours.Tangents != null,
               "the fixture actually carries TEXCOORD_0 and TANGENT - without them this arm would " +
               "measure neither the texture predictor nor a four-component attribute");

        int indices = 0;
        Assert(ours.Submeshes.Count == oracle.Submeshes.Count, "same submesh count");
        for (int s = 0; s < ours.Submeshes.Count; s++)
        {
            Assert(ours.Submeshes[s].Length == oracle.Submeshes[s].Length, "same triangle count");
            for (int i = 0; i < ours.Submeshes[s].Length; i++)
            {
                Assert(ours.Submeshes[s][i] == oracle.Submeshes[s][i],
                       "triangle index " + i + " of submesh " + s + ": " + ours.Submeshes[s][i] +
                       " vs " + oracle.Submeshes[s][i]);
                indices++; checks++;
            }
        }

        double worstPosition = 0, worstNormal = 0, worstUv = 0, worstTangent = 0;
        for (int v = 0; v < ours.Positions.Length; v++)
        {
            worstPosition = Math.Max(worstPosition, Distance(ours.Positions[v], oracle.Positions[v]));
            if (ours.Normals != null) worstNormal = Math.Max(worstNormal, Distance(ours.Normals[v], oracle.Normals[v]));
            worstUv = Math.Max(worstUv, Math.Max(Math.Abs(ours.Uv0[v].X - oracle.Uv0[v].X),
                                                 Math.Abs(ours.Uv0[v].Y - oracle.Uv0[v].Y)));
            for (int c = 0; c < 4; c++)
                worstTangent = Math.Max(worstTangent, Math.Abs(ours.Tangents[v * 4 + c] - oracle.Tangents[v * 4 + c]));
            checks += 4;
        }
        // Both sides are the SAME dequantized floats produced from the same integers, so the only
        // slack allowed is the last bit of a float - a decoder that is actually wrong misses by a
        // quantization step or by a whole hemisphere, never by 1e-6.
        Assert(worstPosition < 1e-5, "every vertex matches glTF-Transform's own decode: worst " + F(worstPosition));
        Assert(worstNormal < 1e-5, "every normal matches: worst " + F(worstNormal));
        Assert(worstUv < 1e-5, "every texture coordinate matches: worst " + F(worstUv));
        Assert(worstTangent < 1e-5, "every tangent matches: worst " + F(worstTangent));

        // Anti-vacuity. A decoder that returned the SAME value for every vertex would satisfy every
        // arm above, since the oracle would have to return it too only if both were broken the same
        // way - but a fixture whose values barely vary would hide a real collapse, so the spread is
        // asserted here: the model has to be a model.
        double spread = 0, uvSpread = 0;
        for (int v = 1; v < ours.Positions.Length; v++)
        {
            spread = Math.Max(spread, Distance(ours.Positions[v], ours.Positions[0]));
            uvSpread = Math.Max(uvSpread, Math.Abs(ours.Uv0[v].X - ours.Uv0[0].X));
        }
        Assert(spread > 0.001 && uvSpread > 0.01,
               "the fixture is a real model, not a collapsed one: " + F(spread) + " across, " +
               F(uvSpread) + " of UV");

        return "real Khronos Draco .glb u12_uv.glb (CC0 Avocado, edgebreaker): " + ours.Positions.Length +
               " vertices, " + indices + " indices EXACT vs glTF-Transform, " + checks + " check(s) | " +
               "worst position " + F(worstPosition) + ", normal " + F(worstNormal) + ", uv " + F(worstUv) +
               ", tangent " + F(worstTangent);
    }

    // ------------------------------------------------------------------ the rigged spider

    /// <summary>
    /// The compressed spider against the file it was compressed FROM. Draco quantizes, so this arm
    /// cannot be exact - and it does not pretend to be: the bound is ONE quantization step of the
    /// model's own bounding box at the encoder's default 14 bits, computed here from the oracle
    /// rather than typed in.
    ///
    /// Draco also renumbers and reorders vertices, so the triangles are compared as a MULTISET over
    /// POSITION CLASSES (each oracle vertex position, with the duplicates a UV/normal seam creates
    /// collapsed into one class). That is the arm that proves the edgebreaker connectivity: a
    /// decoder that recovered every position but wired them into different triangles passes a
    /// per-vertex comparison and fails here.
    /// </summary>
    private static string Rigged()
    {
        var clips = new List<SampledClip>();
        SkinnedModel ours = Read("u12_probe.glb", clips);
        SkinnedModel oracle = Read("u8_probe.glb");
        if (ours == null || oracle == null) return "rigged VOID - no lib\\u12_probe.glb";

        Assert(ours.Joints != null && ours.Weights != null,
               "the fixture actually carries JOINTS_0/WEIGHTS_0 - the INTEGER decoder path, which " +
               "the quantized attributes above never reach");
        // NOT a vertex-count comparison: the encoder WELDS points whose whole attribute tuple is
        // equal, so the compressed file legitimately holds fewer (5386 of 5461 here). What must
        // survive is the SURFACE, which the triangle arm below measures.
        Assert(ours.Positions.Length <= oracle.Positions.Length && ours.Positions.Length > 0,
               "compression welds vertices but never invents them: " + ours.Positions.Length +
               " of " + oracle.Positions.Length);

        // The bound, derived: the encoder's default is 14 bits over the longest axis of the model's
        // bounding box, so one step is that length / (2^14 - 1). Everything below is measured in it.
        double size = 0;
        double minX = double.MaxValue, minY = double.MaxValue, minZ = double.MaxValue;
        double maxX = double.MinValue, maxY = double.MinValue, maxZ = double.MinValue;
        foreach (ObjVector3 p in oracle.Positions)
        {
            minX = Math.Min(minX, p.X); maxX = Math.Max(maxX, p.X);
            minY = Math.Min(minY, p.Y); maxY = Math.Max(maxY, p.Y);
            minZ = Math.Min(minZ, p.Z); maxZ = Math.Max(maxZ, p.Z);
        }
        size = Math.Max(maxX - minX, Math.Max(maxY - minY, maxZ - minZ));
        double step = size / ((1 << 14) - 1);
        Assert(size > 0.01, "the oracle is a real model: " + F(size) + " across");

        // One class per distinct oracle position; a seam's duplicated vertices share theirs.
        var classOf = new Dictionary<long, int>();
        var vertexClass = new int[oracle.Positions.Length];
        var classPosition = new List<ObjVector3>();
        for (int v = 0; v < oracle.Positions.Length; v++)
        {
            long key = Key(oracle.Positions[v]);
            int id;
            if (!classOf.TryGetValue(key, out id))
            {
                id = classPosition.Count;
                classOf[key] = id;
                classPosition.Add(oracle.Positions[v]);
            }
            vertexClass[v] = id;
        }

        // A grid over the oracle's classes, so "which class is this decoded vertex" is a lookup and
        // not a scan of 5 461 vertices per vertex.
        double cell = Math.Max(step * 4, 1e-6);
        var grid = new Dictionary<long, List<int>>();
        for (int c = 0; c < classPosition.Count; c++)
        {
            long key = Cell(classPosition[c], cell);
            List<int> bucket;
            if (!grid.TryGetValue(key, out bucket)) grid[key] = bucket = new List<int>();
            bucket.Add(c);
        }

        var ourClass = new int[ours.Positions.Length];
        double worst = 0;
        for (int v = 0; v < ours.Positions.Length; v++)
        {
            int best = -1;
            double bestDistance = double.MaxValue;
            ObjVector3 p = ours.Positions[v];
            for (int dx = -1; dx <= 1; dx++)
                for (int dy = -1; dy <= 1; dy++)
                    for (int dz = -1; dz <= 1; dz++)
                    {
                        List<int> bucket;
                        if (!grid.TryGetValue(Cell(p, cell) + dx * 1000000L + dy * 1000L + dz, out bucket)) continue;
                        foreach (int c in bucket)
                        {
                            double distance = Distance(p, classPosition[c]);
                            if (distance >= bestDistance) continue;
                            bestDistance = distance; best = c;
                        }
                    }
            Assert(best >= 0, "vertex " + v + " landed nowhere near the model it was compressed from");
            ourClass[v] = best;
            worst = Math.Max(worst, bestDistance);
        }
        // The whole bound, stated in the unit it is bounded by.
        Assert(worst <= step * 1.001,
               "every vertex is within ONE quantization step of where it started: worst " + F(worst) +
               " = " + F(worst / step) + " step(s) of " + F(step));

        // THE CONNECTIVITY ARM: the same triangles, as classes, in any order.
        var counts = new Dictionary<string, int>();
        int triangles = 0;
        foreach (int[] submesh in oracle.Submeshes)
            for (int i = 0; i + 2 < submesh.Length; i += 3)
            {
                Bump(counts, Triangle(vertexClass[submesh[i]], vertexClass[submesh[i + 1]], vertexClass[submesh[i + 2]]), 1);
                triangles++;
            }
        foreach (int[] submesh in ours.Submeshes)
            for (int i = 0; i + 2 < submesh.Length; i += 3)
                Bump(counts, Triangle(ourClass[submesh[i]], ourClass[submesh[i + 1]], ourClass[submesh[i + 2]]), -1);
        int different = 0;
        foreach (int value in counts.Values) if (value != 0) different++;
        Assert(different == 0,
               "the decoded triangles are the file's own, wired the same way: " + different +
               " of " + triangles + " differ");

        // Bone weights, which ride the INTEGER path and are not quantized at all.
        var byClass = new Dictionary<int, List<int>>();
        for (int v = 0; v < oracle.Positions.Length; v++)
        {
            List<int> bucket;
            if (!byClass.TryGetValue(vertexClass[v], out bucket)) byClass[vertexClass[v]] = bucket = new List<int>();
            bucket.Add(v);
        }
        int matched = 0;
        double worstWeight = 0;
        for (int v = 0; v < ours.Positions.Length; v++)
        {
            List<int> candidates = byClass[ourClass[v]];
            bool ok = false;
            double best = double.MaxValue;
            foreach (int o in candidates)
            {
                double difference = 0;
                bool sameBones = true;
                for (int c = 0; c < 4; c++)
                {
                    if (ours.Joints[v * 4 + c] != oracle.Joints[o * 4 + c] &&
                        ours.Weights[v * 4 + c] > 1e-6) sameBones = false;
                    difference = Math.Max(difference, Math.Abs(ours.Weights[v * 4 + c] - oracle.Weights[o * 4 + c]));
                }
                if (!sameBones) continue;
                best = Math.Min(best, difference);
                ok = true;
            }
            Assert(ok, "vertex " + v + " keeps the bones it was weighted to");
            worstWeight = Math.Max(worstWeight, best);
            matched++;
        }
        // Weights are 8-bit normalized in this file, so one step is 1/255; anything larger means the
        // integer path decoded a different number, not a rounding.
        Assert(worstWeight <= 1.01 / 255.0,
               "and its weights, within one 8-bit step: worst " + F(worstWeight) + " = " +
               F(worstWeight * 255) + " step(s)");

        // The rest of the file survives the detour untouched: Draco compresses geometry, nothing else.
        Assert(clips.Count == 5, "the file's five clips still read: " + clips.Count);
        Assert(ours.JointNames.Count == oracle.JointNames.Count && ours.JointNames.Count == 39,
               "the 39-bone skin still reads: " + ours.JointNames.Count);
        for (int i = 0; i < ours.JointNames.Count; i++)
            Assert(ours.JointNames[i] == oracle.JointNames[i], "bone " + i + " keeps its name");

        return "encoded CC0 spider u12_probe.glb (" + new FileInfo(Path.Combine(Lib(), "u12_probe.glb")).Length +
               " B) vs u8_probe.glb (" + new FileInfo(Path.Combine(Lib(), "u8_probe.glb")).Length + " B): " +
               ours.Positions.Length + " vertices, " + triangles + " triangles EXACT as classes, " +
               ours.Submeshes.Count + " primitive(s), 39 bones, " + clips.Count + " clip(s)\n  " +
               "worst vertex " + F(worst / step) + " of one 14-bit step (" + F(step) + "), " + matched +
               " weight set(s) kept, worst weight " + F(worstWeight * 255) + " of one 8-bit step";
    }

    // ------------------------------------------------------------------ helpers

    private static string Triangle(int a, int b, int c)
    {
        int x = Math.Min(a, Math.Min(b, c)), z = Math.Max(a, Math.Max(b, c));
        int y = a + b + c - x - z;
        return x + "/" + y + "/" + z;
    }

    private static void Bump(Dictionary<string, int> counts, string key, int by)
    {
        int value;
        counts.TryGetValue(key, out value);
        counts[key] = value + by;
    }

    private static long Key(ObjVector3 p) =>
        (long)BitConverter.ToInt32(BitConverter.GetBytes(p.X), 0) * 73856093L ^
        (long)BitConverter.ToInt32(BitConverter.GetBytes(p.Y), 0) * 19349663L ^
        (long)BitConverter.ToInt32(BitConverter.GetBytes(p.Z), 0) * 83492791L;

    private static long Cell(ObjVector3 p, double cell) =>
        (long)Math.Floor(p.X / cell) * 1000000L + (long)Math.Floor(p.Y / cell) * 1000L +
        (long)Math.Floor(p.Z / cell);

    private static double Distance(ObjVector3 a, ObjVector3 b)
    {
        double dx = a.X - b.X, dy = a.Y - b.Y, dz = a.Z - b.Z;
        return Math.Sqrt(dx * dx + dy * dy + dz * dz);
    }

    private static string Lib() =>
        Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"..\..\..\..\..\lib"));

    private static SkinnedModel Read(string name) => Read(name, new List<SampledClip>());

    private static SkinnedModel Read(string name, List<SampledClip> clips)
    {
        string path = Path.Combine(Lib(), name);
        if (!File.Exists(path)) return null;
        return GlbReader.Read(File.ReadAllBytes(path), clips);
    }

    private static string F(double v) => v.ToString("0.#######", CultureInfo.InvariantCulture);

    private static void Assert(bool ok, string what)
    {
        if (!ok) throw new Exception("DRACO import FAILED: " + what);
    }
}
