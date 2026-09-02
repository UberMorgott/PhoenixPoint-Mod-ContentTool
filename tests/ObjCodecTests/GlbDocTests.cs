using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Morgott.ContentTool.Import;

/// <summary>
/// GLB-DOC: the lossless container. A .glb read and written back with nothing touched must come out
/// BYTE for byte - not "an equivalent file" - because this is what a maintenance tool rewrites, and
/// the tool has to be able to rewrite files GlbReader itself refuses. The two committed probes are
/// the fixtures: lib\u9_probe.glb (hand-built, 4 clips) and lib\u8_probe.glb (a real 349 KB export).
///
/// Falsified by making Write() re-serialize unconditionally: the two byte-equality checks go red,
/// because Json.Parse -> JsonWriter cannot reproduce an exporter's own spacing and number spelling.
/// </summary>
internal static class GlbDocTests
{
    private static int checks;

    internal static string Run()
    {
        byte[] u9 = File.ReadAllBytes(Fixture("u9_probe.glb"));
        byte[] u8 = File.ReadAllBytes(Fixture("u8_probe.glb"));

        // 1-2. The whole point: nothing mutated, nothing re-serialized, nothing moved.
        GlbDocument doc9 = GlbDocument.Load(u9);
        Check(Same(doc9.Write(), u9), "u9_probe.glb comes back byte-identical");
        Check(Same(GlbDocument.Load(u8).Write(), u8), "u8_probe.glb comes back byte-identical");

        // 3. The header and chunk types the writer emitted, read back off the bytes.
        byte[] written = doc9.Write();
        Check(U32(written, 0) == 0x46546C67 && doc9.Version == 2 && U32(written, 4) == 2 &&
              U32(written, 8) == written.Length && U32(written, 16) == 0x4E4F534A &&
              U32(written, 20 + (int)U32(written, 12) + 4) == 0x004E4942,
              "the written header carries 'glTF', version 2, its own length, a 'JSON' chunk then a 'BIN\\0' one");

        // 4-5. The JSON really parsed, and the BIN chunk really spans what the bufferViews claim.
        Check(doc9.Json.ContainsKey("asset"), "the parsed JSON chunk is a glTF object with an 'asset'");
        long span = 0;
        foreach (object view in (List<object>)doc9.Json["bufferViews"])
        {
            var v = (Dictionary<string, object>)view;
            long offset = v.ContainsKey("byteOffset") ? (long)(double)v["byteOffset"] : 0;
            span = Math.Max(span, offset + (long)(double)v["byteLength"]);
        }
        Check(span > 0 && doc9.Bin.Length >= span && doc9.Bin.Length - span < 4,
              "the BIN chunk is the bufferView span (" + span + " B) plus alignment, not " + doc9.Bin.Length);

        // 6-7. The Dirty path: it re-serializes, and what it re-serializes still says the same thing.
        GlbDocument dirty = GlbDocument.Load(u9);
        dirty.Dirty = true;
        byte[] rewritten = dirty.Write();
        Check(Canonical(GlbDocument.Load(rewritten).Json) == Canonical(doc9.Json),
              "a dirty write re-parses to the same document");
        byte[] serialized = Encoding.UTF8.GetBytes(new JsonWriter().Val(dirty.Json).ToString());
        Check(Chunk(rewritten).Length >= serialized.Length &&
              Chunk(rewritten).Length - serialized.Length < 4 &&
              Same(Copy(Chunk(rewritten), serialized.Length), serialized),
              "the dirty write really re-serialized, rather than re-emitting the original bytes");

        // 8. The path overload is the byte overload with a File.ReadAllBytes in front of it.
        Check(Same(GlbDocument.Load(Fixture("u9_probe.glb")).Write(), u9),
              "Load(path) reads what Load(bytes) reads");

        // 9-10. What a maintenance tool must refuse rather than half-read.
        byte[] corrupt = (byte[])u9.Clone();
        corrupt[0] ^= 0xFF;
        Check(Refused(() => GlbDocument.Load(corrupt), "does not start with 'glTF'"),
              "a file that is not a .glb is refused by name");
        Check(Refused(() => GlbDocument.Load(Copy(u9, u9.Length - 40)), "truncated"),
              "a truncated .glb is refused by name");

        // 11. Nothing removed, only reassigned - so the key order the exporter chose survives.
        var before = new List<string>(doc9.Json.Keys);
        var after = new List<string>(GlbDocument.Load(rewritten).Json.Keys);
        Check(string.Join(",", before.ToArray()) == string.Join(",", after.ToArray()),
              "the root key order survives a dirty round trip");

        return "GLB-DOC PASS, " + checks + " check(s)";
    }

    /// <summary>The JSON chunk of a written .glb, unpadded length included.</summary>
    private static byte[] Chunk(byte[] glb) => Copy(Skip(glb, 20), (int)U32(glb, 12));

    private static string Canonical(Dictionary<string, object> json) => new JsonWriter().Val(json).ToString();

    private static string Fixture(string name) =>
        Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,
                                      @"..\..\..\..\..\lib\" + name));

    private static byte[] Copy(byte[] data, int length)
    {
        var cut = new byte[length];
        Buffer.BlockCopy(data, 0, cut, 0, length);
        return cut;
    }

    private static byte[] Skip(byte[] data, int at)
    {
        var rest = new byte[data.Length - at];
        Buffer.BlockCopy(data, at, rest, 0, rest.Length);
        return rest;
    }

    private static uint U32(byte[] bytes, int at) =>
        (uint)(bytes[at] | (bytes[at + 1] << 8) | (bytes[at + 2] << 16) | (bytes[at + 3] << 24));

    private static bool Same(byte[] a, byte[] b)
    {
        if (a.Length != b.Length) return false;
        for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
        return true;
    }

    /// <summary>True when the call threw AND the message names the cause - not merely that it threw.</summary>
    private static bool Refused(Action call, string cause)
    {
        try { call(); }
        catch (FormatException ex) { return ex.Message.Contains(cause); }
        return false;
    }

    private static void Check(bool ok, string what)
    {
        checks++;
        if (!ok) throw new Exception("GLB-DOC FAIL: " + what);
    }
}
