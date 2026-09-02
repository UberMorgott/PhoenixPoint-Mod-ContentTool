using System;
using System.Collections.Generic;
using System.Text;
using Morgott.ContentTool.Import;

/// <summary>
/// ZIP: the half of the animation rewrite that DECIDES. Nothing here writes a file - it asks whether
/// an accessor is the flat little-endian run the rewrite assumes, what its values are, whether the
/// curve ever moves, and what one key becomes as int16.
///
/// The document is ppzip.py's own selfcheck fixture (tools\ppzip.py:211-238), one still quaternion
/// curve and one moving one, so a divergence between the Python that MEASURED the fixtures and the
/// C# that ships shows up as a value here rather than as a file nobody can open.
///
/// Falsified by reading a strided or sparse accessor as a flat run (the Packed arms go red), by
/// clamping a quaternion component to -32768 instead of -32767 - which GlbReader.cs:2212 decodes as
/// -1.0000305 - or by calling a two-key curve constant on the strength of its first key alone.
/// </summary>
internal static class GlbZipTests
{
    /// <summary>4 identical quaternion keys - a curve that holds one rotation for the whole clip.</summary>
    private static readonly float[] Still = { 0f, 0f, 0f, 1f, 0f, 0f, 0f, 1f, 0f, 0f, 0f, 1f, 0f, 0f, 0f, 1f };
    private static readonly float[] Moving = { 0f, 0f, 0f, 1f, 0.5f, 0f, 0f, 0.86602540f };

    private static int checks;

    internal static string Run()
    {
        checks = 0;

        // 1. The values come back as authored, from bytes this gate wrote itself - so a reader that
        //    agrees with the packer but not with glTF cannot pass.
        GlbDocument doc = GlbDocument.Load(Synthetic());
        float[] read = GlbZip.ReadFloats(doc, 2);
        Check(read != null && Same(read, Still, 0f),
              "ReadFloats hands back the 16 float32 values the still curve was authored with");

        // 2. And the OTHER form it has to understand, which is the one that makes the tool
        //    idempotent: a second run reads back what the first run wrote.
        var authored = new List<float>(Still);
        authored.AddRange(Moving);
        float[] round = GlbZip.ReadFloats(GlbDocument.Load(Quantised(authored.ToArray())), 0);
        Check(round != null && Same(round, authored.ToArray(), GlbZip.QuantMaxError),
              "ReadFloats decodes normalized SHORT back to within one half quantum of the floats packed into it");

        // 3. Both sampler outputs of a file nothing exotic touches are rewritable.
        Check(GlbZip.Packed(doc, 2) && GlbZip.Packed(doc, 3),
              "a plain float32 VEC4 sampler output in a view with no byteStride is packed");

        // 4-6. The three shapes ReadFloats would MISREAD, each refused before it can be read.
        GlbDocument strided = GlbDocument.Load(Synthetic());
        GlbSlim.Obj(GlbSlim.Arr(strided.Json, "bufferViews")[2])["byteStride"] = 32.0;
        Check(!GlbZip.Packed(strided, 2) && GlbZip.ReadFloats(strided, 2) == null,
              "a sampler output whose view declares a byteStride wider than one element is refused, not spliced");

        GlbDocument sparse = GlbDocument.Load(Synthetic());
        GlbSlim.Obj(GlbSlim.Arr(sparse.Json, "accessors")[2])["sparse"] = new Dictionary<string, object>();
        Check(!GlbZip.Packed(sparse, 2), "a sparse sampler output is refused - its values are not the run in the view");

        GlbDocument wide = GlbDocument.Load(Synthetic());
        GlbSlim.Obj(GlbSlim.Arr(wide.Json, "accessors")[2])["componentType"] = 5125.0;
        Check(!GlbZip.Packed(wide, 2),
              "a componentType that is neither FLOAT nor normalized SHORT is a skip, not the SystemExit ppzip takes");

        // 7. The pass that decides whether 805 keys are saying one thing.
        Check(GlbZip.IsConstant(Still, 4) && !GlbZip.IsConstant(Moving, 4) &&
              !GlbZip.IsConstant(new[] { 0f, 0f, 0f, 1f }, 4),
              "a curve whose every key equals the first is constant; one that moves, and one with a single key, are not");

        // 8. The clamp. -32768 is a legal int16 and the wrong answer: GlbReader.cs:2212 decodes it as
        //    -1.0000305 and clamps it back, so the round trip would not be exact.
        short[] packed = Shorts(GlbZip.Pack(new[] { -1f, 1f, 0f, -1.2f }, true));
        Check(packed.Length == 4 && packed[0] == -32767 && packed[1] == 32767 && packed[2] == 0 &&
              packed[3] == -32767,
              "Pack quantises to +-32767 and clamps past it, never emitting -32768");

        // --- the rewrite, against the files ppzip.py was MEASURED on (2026-09-02) ---

        // 9. The shrink case: one clip, exclusive views, one rotation curve. ppzip lands on 2,212 B.
        GlbDocument fold = GlbDocument.Load(Fixture("u8_rootfold.glb"));
        GlbZip.Stats foldStats = GlbZip.Zip(fold, true, true);
        byte[] foldOut = fold.Write();
        Check(foldOut.Length < 2240 && foldStats.Quantised == 1 && Names(fold) == "RootDrive",
              "u8_rootfold.glb zips to " + foldOut.Length + " B (ppzip: 2212) with its one rotation " +
              "quantised and its clip kept");

        // 10. The GROWTH case, and it is not a bug to chase: 278 accessors in 5 shared bufferViews,
        //     so the dense keys the rewrite replaces cannot be freed and the file gets BIGGER. The
        //     job refuses the swap; this gate only pins the number.
        GlbDocument spider = GlbDocument.Load(Fixture("u8_probe.glb"));
        GlbZip.Stats spiderStats = GlbZip.Zip(spider, true, true);
        byte[] spiderOut = spider.Write();
        Check(spiderOut.Length > 349468 && spiderStats.Quantised == 137 &&
              Names(spider) == "Spider_Attack,Spider_Death,Spider_Idle,Spider_Jump,Spider_Walk",
              "u8_probe.glb keeps its 5 clips, quantises 137 rotations and GROWS to " + spiderOut.Length +
              " B (ppzip: 377256)");

        // 11. The file ppzip CRASHES on (struct.error, offset 380 of a 380-byte buffer): accessor 8 is
        //     the output of Walk, of walk AND of Hold, so ppzip rewrites it three times and re-reads it
        //     out of the stale blob. Here a shared output is left exactly as it is.
        byte[] u9bytes = System.IO.File.ReadAllBytes(Fixture("u9_probe.glb"));
        GlbDocument before9 = GlbDocument.Load(u9bytes);
        byte[] held = Slice(before9, GlbSlim.Int(Sampler(before9, "Hold", 0), "output", -1));
        long heldCount = Count(before9, GlbSlim.Int(Sampler(before9, "Hold", 0), "output", -1));
        GlbDocument u9 = GlbDocument.Load(u9bytes);
        GlbZip.Stats u9Stats = GlbZip.Zip(u9, true, true);
        Dictionary<string, object> walk = Sampler(u9, "Walk", 0);
        Dictionary<string, object> walkOut = Accessor(u9, GlbSlim.Int(walk, "output", -1));
        Check(Names(u9) == "Walk,walk,Morphs,Hold" && u9Stats.Shared >= 1 &&
              GlbSlim.Int(walkOut, "componentType", 0) == 5126 &&
              GlbSlim.Long(walkOut, "count", 0) == heldCount,
              "u9_probe.glb finishes with all 4 clips, and the output 3 clips share keeps float32 and " +
              "its " + heldCount + " keys");

        // 12. And the STEP sampler that shares it is still STEP, byte for byte.
        Dictionary<string, object> hold = Sampler(u9, "Hold", 0);
        Check(GlbSlim.Str(hold, "interpolation") == "STEP" &&
              Same(Slice(u9, GlbSlim.Int(hold, "output", -1)), held),
              "the STEP sampler of Hold keeps its interpolation and its bytes");

        // 13-15. The three refusals. The first two the shipped key-count guard already makes; the
        //        third is the new one, and without it u10_probe passes and is silently invalidated -
        //        EXT_meshopt_compression keeps its own byteOffset inside the view's extension block,
        //        which Compact does not move.
        Check(Refusal("u12_norm.glb") != null,
              "the Draco file is refused - 1 bufferView key against 5 accessors");
        Check(Refusal("u12_probe.glb") != null && Refusal("u12_uv.glb") != null,
              "the other two Draco files are refused too");
        string meshopt = Refusal("u10_probe.glb");
        Check(meshopt != null && meshopt.Contains("extension"),
              "a bufferView carrying an extensions block is refused by name, not compacted out from under it");

        // 16. Two constant curves reading the same key times get ONE 2-key input between them, not
        //     one each (ppzip.py:181-188).
        GlbDocument shared = GlbDocument.Load(Shared());
        GlbZip.Stats sharedStats = GlbZip.Zip(shared, true, true);
        int first = GlbSlim.Int(Sampler(shared, "Clip", 0), "input", -1);
        int third = GlbSlim.Int(Sampler(shared, "Clip", 2), "input", -1);
        Check(sharedStats.Collapsed == 2 && first == third &&
              GlbSlim.Long(Accessor(shared, first), "count", 0) == 2 &&
              GlbSlim.Str(Accessor(shared, first), "type") == "SCALAR",
              "two constant curves sharing one input accessor collapse onto a single new 2-key input");

        // 17. A clip whose EVERY curve is constant is left dense. GlbReader picks a clip's rate from
        //     the coarsest rate every key time lands on and derives its LENGTH from it, so collapsing
        //     the last dense channel would let the rate drop and the clip come out LONGER.
        GlbDocument solo = GlbDocument.Load(Synthetic());
        Dictionary<string, object> clip = GlbSlim.Obj(GlbSlim.Arr(solo.Json, "animations")[0]);
        GlbSlim.Arr(clip, "samplers").RemoveAt(1);
        GlbSlim.Arr(clip, "channels").RemoveAt(1);
        GlbZip.Stats soloStats = GlbZip.Zip(solo, true, false);
        float[] times = GlbZip.ReadFloats(solo, GlbSlim.Int(Sampler(solo, "Clip", 0), "input", -1));
        Check(soloStats.Collapsed == 0 && times != null &&
              Same(times, new[] { 0f, 0.25f, 0.5f, 0.75f }, 0f),
              "a wholly constant clip keeps its 4 key times, so its frame rate cannot drift");

        // 18. The document the rewrite hands on is internally consistent: BIN grew, the buffer says so,
        //     and the writer knows to re-serialise the JSON rather than echo the original chunk.
        Check(fold.Dirty &&
              GlbSlim.Long(GlbSlim.Obj(GlbSlim.Arr(fold.Json, "buffers")[0]), "byteLength", -1) == fold.Bin.Length,
              "a zipped document is Dirty and its buffer's byteLength is the BIN chunk it actually got");

        return "GLB-ZIP PASS, " + checks + " check(s)";
    }

    /// <summary>Whether the pre-flight refuses this fixture at all - zip reaches it with force, since
    /// it drops no clip and neither the mandatory nor the rigged-character arm can apply.</summary>
    private static string Refusal(string name) =>
        GlbSlim.Guard(GlbDocument.Load(Fixture(name)), new HashSet<int>(), true);

    /// <summary>ppzip's fixture with a THIRD channel: two constant curves reading the same key times,
    /// which is the shape the input dedup exists for, plus the moving curve that keeps collapse on.</summary>
    private static byte[] Shared()
    {
        byte[] bin = Concat(Bytes(0f, 0.25f, 0.5f, 0.75f), Bytes(0f, 0.25f), Bytes(Still), Bytes(Moving),
                            Bytes(Still));
        return Glb("{\"asset\":{\"version\":\"2.0\"}," +
                   "\"buffers\":[{\"byteLength\":" + bin.Length + "}]," +
                   "\"bufferViews\":[{\"buffer\":0,\"byteOffset\":0,\"byteLength\":16}," +
                                    "{\"buffer\":0,\"byteOffset\":16,\"byteLength\":8}," +
                                    "{\"buffer\":0,\"byteOffset\":24,\"byteLength\":64}," +
                                    "{\"buffer\":0,\"byteOffset\":88,\"byteLength\":32}," +
                                    "{\"buffer\":0,\"byteOffset\":120,\"byteLength\":64}]," +
                   "\"accessors\":[{\"bufferView\":0,\"componentType\":5126,\"count\":4,\"type\":\"SCALAR\"," +
                                   "\"min\":[0],\"max\":[0.75]}," +
                                  "{\"bufferView\":1,\"componentType\":5126,\"count\":2,\"type\":\"SCALAR\"," +
                                   "\"min\":[0],\"max\":[0.25]}," +
                                  "{\"bufferView\":2,\"componentType\":5126,\"count\":4,\"type\":\"VEC4\"}," +
                                  "{\"bufferView\":3,\"componentType\":5126,\"count\":2,\"type\":\"VEC4\"}," +
                                  "{\"bufferView\":4,\"componentType\":5126,\"count\":4,\"type\":\"VEC4\"}]," +
                   "\"nodes\":[{},{},{}]," +
                   "\"animations\":[{\"name\":\"Clip\"," +
                     "\"samplers\":[{\"input\":0,\"output\":2},{\"input\":1,\"output\":3}," +
                                   "{\"input\":0,\"output\":4}]," +
                     "\"channels\":[{\"sampler\":0,\"target\":{\"node\":0,\"path\":\"rotation\"}}," +
                                   "{\"sampler\":1,\"target\":{\"node\":1,\"path\":\"rotation\"}}," +
                                   "{\"sampler\":2,\"target\":{\"node\":2,\"path\":\"rotation\"}}]}]}", bin);
    }

    /// <summary>Every clip name in file order - the list a rewrite must never shorten.</summary>
    private static string Names(GlbDocument doc)
    {
        var names = new List<string>();
        foreach (object animation in GlbSlim.Arr(doc.Json, "animations") ?? new List<object>())
            names.Add(GlbSlim.Str(GlbSlim.Obj(animation), "name") ?? "");
        return string.Join(",", names.ToArray());
    }

    private static Dictionary<string, object> Sampler(GlbDocument doc, string clip, int index)
    {
        foreach (object animation in GlbSlim.Arr(doc.Json, "animations") ?? new List<object>())
            if (GlbSlim.Str(GlbSlim.Obj(animation), "name") == clip)
                return GlbSlim.Obj(GlbSlim.Arr(GlbSlim.Obj(animation), "samplers")[index]);
        return null;
    }

    private static Dictionary<string, object> Accessor(GlbDocument doc, int index) =>
        GlbSlim.Obj(GlbSlim.Arr(doc.Json, "accessors")[index]);

    private static long Count(GlbDocument doc, int accessor) =>
        GlbSlim.Long(Accessor(doc, accessor), "count", 0);

    /// <summary>The bytes an accessor reads: its view's slice, offset by its own byteOffset. Compared
    /// by VALUE and not by index, because Trim renumbers everything it keeps.</summary>
    private static byte[] Slice(GlbDocument doc, int index)
    {
        Dictionary<string, object> accessor = Accessor(doc, index);
        Dictionary<string, object> view =
            GlbSlim.Obj(GlbSlim.Arr(doc.Json, "bufferViews")[GlbSlim.Int(accessor, "bufferView", -1)]);
        int at = (int)(GlbSlim.Long(view, "byteOffset", 0) + GlbSlim.Long(accessor, "byteOffset", 0));
        var slice = new byte[GlbSlim.Long(accessor, "count", 0) * GlbSlim.ElementSize(accessor)];
        Buffer.BlockCopy(doc.Bin, at, slice, 0, slice.Length);
        return slice;
    }

    private static bool Same(byte[] a, byte[] b)
    {
        if (a.Length != b.Length) return false;
        for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
        return true;
    }

    private static string Fixture(string name) =>
        System.IO.Path.GetFullPath(System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory,
                                                          @"..\..\..\..\..\lib\" + name));

    /// <summary>ppzip.py:211-238's own fixture: 4 still quaternion keys and one moving 2-key curve,
    /// 4 bufferViews, 4 accessors, one clip with two rotation channels.</summary>
    private static byte[] Synthetic()
    {
        byte[] bin = Concat(Bytes(0f, 0.25f, 0.5f, 0.75f), Bytes(0f, 0.25f), Bytes(Still), Bytes(Moving));
        return Glb("{\"asset\":{\"version\":\"2.0\"}," +
                   "\"buffers\":[{\"byteLength\":" + bin.Length + "}]," +
                   "\"bufferViews\":[{\"buffer\":0,\"byteOffset\":0,\"byteLength\":16}," +
                                    "{\"buffer\":0,\"byteOffset\":16,\"byteLength\":8}," +
                                    "{\"buffer\":0,\"byteOffset\":24,\"byteLength\":64}," +
                                    "{\"buffer\":0,\"byteOffset\":88,\"byteLength\":32}]," +
                   "\"accessors\":[{\"bufferView\":0,\"componentType\":5126,\"count\":4,\"type\":\"SCALAR\"," +
                                   "\"min\":[0],\"max\":[0.75]}," +
                                  "{\"bufferView\":1,\"componentType\":5126,\"count\":2,\"type\":\"SCALAR\"," +
                                   "\"min\":[0],\"max\":[0.25]}," +
                                  "{\"bufferView\":2,\"componentType\":5126,\"count\":4,\"type\":\"VEC4\"}," +
                                  "{\"bufferView\":3,\"componentType\":5126,\"count\":2,\"type\":\"VEC4\"}]," +
                   "\"nodes\":[{},{}]," +
                   "\"animations\":[{\"name\":\"Clip\"," +
                     "\"samplers\":[{\"input\":0,\"output\":2},{\"input\":1,\"output\":3}]," +
                     "\"channels\":[{\"sampler\":0,\"target\":{\"node\":0,\"path\":\"rotation\"}}," +
                                   "{\"sampler\":1,\"target\":{\"node\":1,\"path\":\"rotation\"}}]}]}", bin);
    }

    /// <summary>One accessor of normalized int16 VEC4 keys, written by the packer under test - which
    /// is the point: what the writer emits is what the reader has to understand.</summary>
    private static byte[] Quantised(float[] values)
    {
        byte[] bin = GlbZip.Pack(values, true);
        return Glb("{\"asset\":{\"version\":\"2.0\"}," +
                   "\"buffers\":[{\"byteLength\":" + bin.Length + "}]," +
                   "\"bufferViews\":[{\"buffer\":0,\"byteOffset\":0,\"byteLength\":" + bin.Length + "}]," +
                   "\"accessors\":[{\"bufferView\":0,\"componentType\":5122,\"normalized\":true," +
                                  "\"count\":" + values.Length / 4 + ",\"type\":\"VEC4\"}]}", bin);
    }

    /// <summary>Little-endian float32, written here rather than by the packer, so check 1 compares
    /// the reader against glTF and not against its own writer.</summary>
    private static byte[] Bytes(params float[] values)
    {
        var bytes = new byte[values.Length * 4];
        for (int i = 0; i < values.Length; i++)
            Buffer.BlockCopy(BitConverter.GetBytes(values[i]), 0, bytes, i * 4, 4);
        return bytes;
    }

    private static short[] Shorts(byte[] bytes)
    {
        var values = new short[bytes.Length / 2];
        for (int i = 0; i < values.Length; i++) values[i] = BitConverter.ToInt16(bytes, i * 2);
        return values;
    }

    private static byte[] Concat(params byte[][] blocks)
    {
        int total = 0;
        foreach (byte[] block in blocks) total += block.Length;
        var bytes = new byte[total];
        int at = 0;
        foreach (byte[] block in blocks) { Buffer.BlockCopy(block, 0, bytes, at, block.Length); at += block.Length; }
        return bytes;
    }

    private static bool Same(float[] got, float[] want, float tolerance)
    {
        if (got.Length != want.Length) return false;
        for (int i = 0; i < got.Length; i++) if (Math.Abs(got[i] - want[i]) > tolerance) return false;
        return true;
    }

    /// <summary>A .glb built out of a JSON chunk and a BIN chunk, so a gate can pose a shape no
    /// fixture on disk has.</summary>
    private static byte[] Glb(string json, byte[] bin)
    {
        byte[] text = Pad(Encoding.UTF8.GetBytes(json), 0x20);
        bin = Pad(bin, 0x00);
        var bytes = new byte[12 + 8 + text.Length + 8 + bin.Length];
        Put(bytes, 0, 0x46546C67); Put(bytes, 4, 2); Put(bytes, 8, (uint)bytes.Length);
        Put(bytes, 12, (uint)text.Length); Put(bytes, 16, 0x4E4F534A);
        Buffer.BlockCopy(text, 0, bytes, 20, text.Length);
        int at = 20 + text.Length;
        Put(bytes, at, (uint)bin.Length); Put(bytes, at + 4, 0x004E4942);
        Buffer.BlockCopy(bin, 0, bytes, at + 8, bin.Length);
        return bytes;
    }

    private static byte[] Pad(byte[] data, byte filler)
    {
        var padded = new byte[data.Length + (4 - data.Length % 4) % 4];
        Buffer.BlockCopy(data, 0, padded, 0, data.Length);
        for (int i = data.Length; i < padded.Length; i++) padded[i] = filler;
        return padded;
    }

    private static void Put(byte[] bytes, int at, uint value)
    {
        for (int i = 0; i < 4; i++) bytes[at + i] = (byte)(value >> (8 * i));
    }

    private static void Check(bool ok, string what)
    {
        checks++;
        if (!ok) throw new Exception("GLB-ZIP FAIL: " + what);
    }
}
