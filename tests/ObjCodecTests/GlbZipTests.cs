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

        return "GLB-ZIP PASS, " + checks + " check(s)";
    }

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
