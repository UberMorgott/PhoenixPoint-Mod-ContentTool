using System;
using System.IO;
using Morgott.ContentTool.Import;
using Morgott.ContentTool.Wwise;

/// <summary>
/// FINAL-PLAN 30.1 (OBJ: vertices, normals, UV, triangles, malformed input), offline. Ported from
/// ResourceReplacer's tests\ObjCodecTests. Every refusal arm asserts the CAUSE in the message, so a
/// parser that threw for the wrong reason would not pass.
///
///   dotnet run --project tests\ObjCodecTests
/// </summary>
internal static class Program
{
    private static int checks;

    /// <summary>
    /// `--bake <wav> <mediaId> <shipped.wem|-> <out.bnk>`: the SAME BuildWem + BuildMediaOnly the
    /// in-game `ct_sound bake` runs, driven from here so a bank can be produced without a game
    /// launch. The shipped .wem is READ only, for its smpl loop region.
    /// </summary>
    private static int Bake(string[] a)
    {
        string why;
        WwisePcm.Wav w = WwisePcm.ReadWav(File.ReadAllBytes(a[1]), out why);
        if (w == null) { Console.WriteLine("BAKE REFUSED " + a[1] + " " + why); return 1; }
        uint media = uint.Parse(a[2]);
        WwiseWem.Info target = a[3] == "-" ? null : WwiseWem.Parse(File.ReadAllBytes(a[3]));
        long frames = w.Pcm16.Length / (2L * Math.Max(1, w.Channels));
        bool loops = target != null && target.HasLoop;
        byte[] wem = WwisePcm.BuildWem(w.Pcm16, w.Channels, w.SampleRate, loops ? frames : 0,
                                       loops ? target.LoopPlayCount : 0u);
        byte[] bank = BankGen.BuildMediaOnly(WwiseId.Hash("offline_" + media), media, wem);
        File.WriteAllBytes(a[4], bank);
        Console.WriteLine("BAKED " + a[4] + ": " + bank.Length + " B, media " + media + ", " +
                          w.Channels + "ch " + w.SampleRate + "Hz, " + frames + " frames" +
                          (loops ? ", loop 0.." + (frames - 1) + " playCount " + target.LoopPlayCount : ", no loop"));
        return 0;
    }

    /// <summary>
    /// `--u9probe &lt;out.glb&gt;`: write the hand-built fixture the IN-GAME U8-grid/U8-step/U9-plan arm
    /// reads. The file is committed (lib\u9_probe.glb) because the game cannot run this project; it
    /// prints what the reader makes of what it just wrote, so a fixture that drifted from the builder
    /// is visible here rather than three minutes later in a game launch.
    /// </summary>
    private static int U9Probe(string[] a)
    {
        byte[] glb = ClipPlan.Probe();
        File.WriteAllBytes(a[1], glb);
        var clips = new System.Collections.Generic.List<Morgott.ContentTool.Import.SampledClip>();
        Morgott.ContentTool.Import.GlbReader.Read(glb, clips);
        Console.WriteLine("WROTE " + a[1] + " " + glb.Length + " B");
        foreach (Morgott.ContentTool.Import.SampledClip c in clips)
            Console.WriteLine("  '" + c.Name + "' " + c.Tracks.Count + " track(s) @ " + c.SampleRate +
                              " Hz x " + c.Times.Length + " frame(s) | " + c.LossyReason);
        return 0;
    }

    private static int Main(string[] args)
    {
        if (args.Length == 5 && args[0] == "--bake") return Bake(args);
        if (args.Length == 2 && args[0] == "--u9probe") return U9Probe(args);
        ObjDocument quad = ObjCodec.Parse("v 0 0 0\nv 1 0 0\nv 1 1 0\nv 0 1 0\nvt 0 0\nvt 1 0\nvt 1 1\nvt 0 1\nvn 0 0 1\nf 1/1/1 2/2/1 3/3/1 4/4/1\n");
        Check(quad.Positions.Count == 4, "positions");
        Check(quad.TextureCoordinates.Count == 4 && quad.Normals.Count == 1, "uv and normals");
        Check(quad.Triangles[0].A.Texture == 0 && quad.Triangles[0].A.Normal == 0, "face attribute indices");
        Check(quad.Triangles.Count == 2, "quad triangulation");
        Check(quad.Triangles[1].A.Position == 0 && quad.Triangles[1].C.Position == 3, "fan order");
        ObjDocument negative = ObjCodec.Parse("v 0 0 0\nv 1 0 0\nv 0 1 0\nf -3 -2 -1\n");
        Check(negative.Triangles[0].C.Position == 2, "negative indices");
        Check(ObjCodec.Parse(ObjCodec.Write(quad)).Triangles.Count == 2, "round trip");
        Check(ObjCodec.Checksum(ObjCodec.Parse(ObjCodec.Write(quad))) == ObjCodec.Checksum(quad), "checksum survives the round trip");
        Check(ObjCodec.Checksum(negative) != ObjCodec.Checksum(quad), "checksum separates two documents");
        ObjDocument grouped = ObjCodec.Parse("v 0 0 0\nv 1 0 0\nv 0 1 0\nf 1 2 3\ng A\nf 1 2 3\ng B\nf 1 2 3\n");
        Check(grouped.Triangles[0].Group == 0 && grouped.Triangles[1].Group == 1 && grouped.Triangles[2].Group == 2, "default and named groups");
        ExpectFailure("", "no faces");
        ExpectFailure("v NaN 0 0\nf 1 1 1\n", "finite");
        ExpectFailure("v 0 0 0\nf 1 2 3\n", "range");
        ExpectFailure("v 0 0 0\nv 1 0 0\nv 0 1 0\nf 1 2 3//\n", "malformed face vertex");
        // A .obj comes out of a folder the player controls, so it must hit a ceiling rather than
        // let a hostile file grow a List<> until the game dies.
        var huge = new System.Text.StringBuilder(9 * 1024 * 1024);
        for (int i = 0; i <= 1000000; i++) huge.Append("v 0 0 0\n");
        ExpectFailure(huge.ToString(), "past the limit");

        // ---- OBJ -> the buffers a serialized Mesh carries
        BakedMesh baked = MeshBuild.From(quad);
        Check(baked.VertexCount == 4 && baked.IndexCount == 6, "quad bakes to 4 vertices and 6 indices");
        Check(baked.VertexData.Length == 4 * BakedMesh.Stride, "vertex stream is stride x count");
        Check(!baked.Index32 && baked.IndexData.Length == 12, "small mesh stays UInt16");
        Check(Math.Abs(baked.ExtentX - 0.5f) < 1e-6 && Math.Abs(baked.CenterX - 0.5f) < 1e-6, "bounds are centre and HALF extent");
        Check(BitConverter.ToSingle(baked.VertexData, 20) == 1f, "normal reaches its channel offset");
        Check(BitConverter.ToSingle(baked.VertexData, 24 + BakedMesh.Stride) == 1f, "uv reaches its channel offset");
        // Two faces sharing a corner must SHARE the vertex, or a mesh doubles in size per submesh.
        Check(MeshBuild.From(ObjCodec.Parse("v 0 0 0\nv 1 0 0\nv 1 1 0\nv 0 1 0\nf 1 2 3\nf 1 3 4\n")).VertexCount == 4,
              "vertices are de-duplicated across faces");
        // Same position, different uv = a DIFFERENT vertex; a shared one would tear the texture.
        Check(MeshBuild.From(ObjCodec.Parse("v 0 0 0\nv 1 0 0\nv 1 1 0\nvt 0 0\nvt 1 1\nf 1/1 2/1 3/1\nf 1/2 2/1 3/1\n")).VertexCount == 4,
              "the uv is part of the vertex key");
        BakedMesh noNormals = MeshBuild.From(ObjCodec.Parse("v 0 0 0\nv 1 0 0\nv 0 1 0\nf 1 2 3\n"));
        Check(Math.Abs(BitConverter.ToSingle(noNormals.VertexData, 20) - 1f) < 1e-6,
              "a .obj with no vn gets computed normals, not zeroes");

        Console.WriteLine("OBJ: ALL PASS, " + checks + " check(s)");
        Console.WriteLine(MeshMergeTests.Run());
        Console.WriteLine(SubmeshSlots.Run());
        Console.WriteLine(MeshRoundTrip.Run());
        Console.WriteLine(PrefabRoundTrip.Run());
        Console.WriteLine(SkinRoundTrip.Run());
        Console.WriteLine(ModelRoundTrip.Run());
        Console.WriteLine(SkinWidth.Run());
        Console.WriteLine(SkinAbove.Run());
        Console.WriteLine(SourceSkip.Run());
        Console.WriteLine(RefusalCount.Run());
        Console.WriteLine(BoneNames.Run());
        Console.WriteLine(OrbitTests.Run());
        Console.WriteLine(BinderFrozen.Run());
        Console.WriteLine(DecisionGolden.Run());
        Console.WriteLine(AliasTests.Run());
        Console.WriteLine(PreflightTests.Run());
        Console.WriteLine(MaterialBake.Run());
        Console.WriteLine(MaterialTweakFixture.Run());
        Console.WriteLine(ClipRoundTrip.Run());
        Console.WriteLine(ClipImport.Run());
        Console.WriteLine(RootMotionBake.Run());
        Console.WriteLine(ClimbSynthesis.Run());
        Console.WriteLine(RoleFill.Run());
        Console.WriteLine(StartRoster.Run());
        Console.WriteLine(PackageGate.Run());
        Console.WriteLine(ClipBake.Run());
        Console.WriteLine(ClipPlan.Run());
        Console.WriteLine(Compressed.Run());
        Console.WriteLine(DracoTests.Run());
        Console.WriteLine(AssetIndexTests.Run());
        Console.WriteLine(InspectTests.Run());
        Console.WriteLine(GlbDocTests.Run());
        Console.WriteLine(GlbSlimTests.Run());
        Console.WriteLine(MeshExtractTests.Run());
        Console.WriteLine(VideoExtractTests.Run());
        Console.WriteLine(VideoCatalogTests.Run());
        Console.WriteLine(AudioExtractTests.Run());
        string wav = WavReadTests.Run();
        Console.WriteLine(wav);
        string src = SourceAudioTests.Run();
        Console.WriteLine(src);
        string dec = SourceDecodeTests.Run();
        Console.WriteLine(dec);
        string loop = WemLoopTests.Run();
        Console.WriteLine(loop);
        string banks = DemoBankTests.Run();
        Console.WriteLine(banks);
        return wav.Contains("FAILURE") || src.Contains("FAILURE") || dec.Contains("FAILURE") ||
               loop.Contains("FAILURE") || banks.Contains("FAILURE") ? 1 : 0;
    }

    private static void Check(bool condition, string name)
    {
        if (!condition)
            throw new Exception("Check failed: " + name);
        checks++;
    }

    private static void ExpectFailure(string text, string cause)
    {
        try
        {
            ObjCodec.Parse(text);
            throw new Exception("Expected FormatException: " + cause);
        }
        catch (FormatException exception)
        {
            Check(exception.Message.IndexOf(cause, StringComparison.OrdinalIgnoreCase) >= 0, cause);
        }
    }
}
