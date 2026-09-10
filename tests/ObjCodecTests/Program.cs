using System;
using System.Collections.Generic;
using System.IO;
using AssetsTools.NET;
using AssetsTools.NET.Extra;
using Morgott.ContentTool.Bake;
using Morgott.ContentTool.Doctor;
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

    /// <summary>
    /// `--dropshard &lt;in.glb&gt; &lt;out.glb&gt;`: delete every mesh primitive drawn by 8 triangles or fewer
    /// while a bigger one survives - MeshFields' own shard rule (SubmeshReport), APPLIED to the file
    /// instead of only reported about it. Unity paints submesh i with material i and the bake keeps
    /// the file's primitive order, so a leftover shard in front of the body pushes the real geometry
    /// onto the material after it; dropping it is exactly what the Blender advice does by hand, for
    /// an author who has no Blender. Container surgery only - GlbDocument interprets no glTF, the
    /// accessors the dropped primitive owned are simply left unreferenced.
    /// </summary>
    private static int DropShard(string[] a)
    {
        GlbDocument doc = GlbDocument.Load(a[1]);
        var accessors = (List<object>)doc.Json["accessors"];
        int dropped = 0;
        var meshes = (List<object>)doc.Json["meshes"];
        for (int m = 0; m < meshes.Count; m++)
        {
            var mesh = (Dictionary<string, object>)meshes[m];
            // glTF mesh.name is optional and GlbReader tolerates its absence, so the log line must too.
            object nameObj;
            string label = mesh.TryGetValue("name", out nameObj) && nameObj != null
                ? nameObj.ToString() : "mesh #" + m;
            var prims = (List<object>)mesh["primitives"];
            var tris = new int[prims.Count];
            int most = 0;
            for (int i = 0; i < prims.Count; i++)
            {
                var acc = (Dictionary<string, object>)accessors[(int)(double)((Dictionary<string, object>)prims[i])["indices"]];
                tris[i] = (int)(double)acc["count"] / 3;
                if (tris[i] > most) most = tris[i];
            }
            for (int i = prims.Count - 1; i >= 0; i--)
            {
                if (tris[i] > 8 || most <= 8) continue;
                Console.WriteLine("dropping part " + (i + 1) + " of " + prims.Count + " (" + tris[i] +
                                  " triangle(s)) from mesh '" + label + "'");
                prims.RemoveAt(i);
                dropped++;
            }
        }
        if (dropped == 0) { Console.WriteLine("no shard in " + a[1] + " - nothing written"); return 1; }
        doc.Dirty = true;
        doc.Write(a[2]);
        Console.WriteLine("WROTE " + a[2] + " " + new FileInfo(a[2]).Length + " B, " + dropped + " part(s) dropped");
        return 0;
    }

    /// <summary>
    /// `--fit &lt;bundle&gt; &lt;MeshName&gt; [replacement.glb]`: what the SHIPPED target really is - its
    /// material slots in PAINT order, the Texture2D each of those materials samples, its bind poses
    /// and its bone names - and, when a file is named, the Doctor's own verdict on it. The same
    /// MeshFields / SkinFields / ReplacementPreflight the bake and the panel run, driven offline, so
    /// a manifest row can be written from what the bundle holds instead of from a guess.
    /// </summary>
    private static int Fit(string[] a)
    {
        AssetsManager man = new AssetsManager();
        man.LoadClassPackage(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,
                                          @"..\..\..\..\..\lib\classdata.tpk"));
        BundleFileInstance bun = man.LoadBundleFile(a[1], true);
        AssetsFileInstance af = man.LoadAssetsFileFromBundle(bun, 0, false);
        man.LoadClassDatabaseFromPackage(af.file.Metadata.UnityVersion);
        string[] slots, bones;
        int poses;
        try
        {
            long meshId = AssetIndex.FindUnique(man, af, AssetClassID.Mesh, a[2], a[1]).PathId;
            slots = MeshFields.MaterialNames(man, af, meshId);
            bones = SkinFields.BoneNames(man, af, meshId);
            poses = PrefabFields.Get(man, af, meshId)["m_BindPose"]["Array"].Children.Count;
            Console.WriteLine(a[2] + ": " + (slots == null ? 0 : slots.Length) + " material slot(s), " +
                              poses + " bind pose(s), " + (bones == null ? 0 : bones.Length) + " named bone(s)");
            for (int i = 0; slots != null && i < slots.Length; i++)
            {
                Console.WriteLine("  slot " + i + " -> material '" + slots[i] + "'");
                // The slot text is the FOLD of the renderer variants, so the alternatives are split back
                // out here to look each material up. A material genuinely named 'Red or Blue' would
                // split wrongly - this is a dump, not the bake's own rule (MeshFields.Fold keeps names).
                foreach (string alt in slots[i].Replace(" (varies by renderer variant)", "")
                                               .Split(new[] { " or " }, StringSplitOptions.None))
                    foreach (AssetFileInfo m in af.file.Metadata.GetAssetsOfType(AssetClassID.Material))
                    {
                        AssetTypeValueField mat = man.GetBaseField(af, m);
                        if (mat["m_Name"].AsString != alt) continue;
                        foreach (AssetTypeValueField t in mat["m_SavedProperties"]["m_TexEnvs"]["Array"].Children)
                        {
                            long tex = t["second"]["m_Texture"]["m_PathID"].AsLong;
                            if (tex == 0) continue;
                            Console.WriteLine("      " + alt + " " + t["first"].AsString + " -> " +
                                              PrefabFields.Name(man, af, tex));
                        }
                    }
            }
        }
        finally { man.UnloadAll(); }

        if (a.Length < 4) return 0;
        var target = new RigTarget
        {
            BoneNames = bones,
            MaterialNames = slots,
            Rigged = poses > 0,
            BindPoseCount = poses,
            MeshName = a[2]
        };
        ReplacementPreflightResult r = ReplacementPreflight.Run(File.ReadAllBytes(a[3]), a[3], target);
        Console.WriteLine(Path.GetFileName(a[3]) + ": " + r.Report.Header() + " | outcome " + r.Outcome +
                          " | " + r.Report.Rows.Count + " row(s)");
        foreach (Diagnostic d in r.Report.Rows)
            Console.WriteLine("  [" + d.Severity + "/" + d.Side + "] " + d.Code + ": " + d.Message);
        // A mapping row is INFO - the report states which part lands where even when nothing is wrong -
        // so the gate is "no row that is more than information", not "no rows". NotRigged is a verdict
        // about the TARGET (no bind poses), not a fault in the replacement: a static target is a pass.
        return (r.Outcome == Outcome.ByName || r.Outcome == Outcome.NotRigged) &&
               r.Report.Count(Severity.Info) == r.Report.Rows.Count ? 0 : 1;
    }

    /// <summary>
    /// `--uses &lt;bundle&gt; &lt;Texture2D&gt;`: what a texture row would actually repaint - the shipped
    /// texture's own size and format, every material that samples it, and every mesh those materials
    /// draw. A row aimed at a texture whose materials also paint geometry the project does NOT
    /// replace repaints that geometry with an atlas whose UVs were never meant for it, and this is
    /// the only way to see that before baking 120 MB.
    /// </summary>
    private static int Uses(string[] a)
    {
        AssetsManager man = new AssetsManager();
        man.LoadClassPackage(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,
                                          @"..\..\..\..\..\lib\classdata.tpk"));
        BundleFileInstance bun = man.LoadBundleFile(a[1], true);
        AssetsFileInstance af = man.LoadAssetsFileFromBundle(bun, 0, false);
        man.LoadClassDatabaseFromPackage(af.file.Metadata.UnityVersion);
        try
        {
            AssetFileInfo texInfo = AssetIndex.FindUnique(man, af, AssetClassID.Texture2D, a[2], a[1]);
            AssetTypeValueField tex = man.GetBaseField(af, texInfo);
            Console.WriteLine(a[2] + ": " + tex["m_Width"].AsInt + "x" + tex["m_Height"].AsInt +
                              " format=" + tex["m_TextureFormat"].AsInt + " mips=" + tex["m_MipCount"].AsInt +
                              " streamed=" + (tex["m_StreamData"]["path"].AsString.Length > 0));
            foreach (AssetFileInfo mi in af.file.Metadata.GetAssetsOfType(AssetClassID.Material))
            {
                AssetTypeValueField mat = man.GetBaseField(af, mi);
                foreach (AssetTypeValueField t in mat["m_SavedProperties"]["m_TexEnvs"]["Array"].Children)
                {
                    if (t["second"]["m_Texture"]["m_PathID"].AsLong != texInfo.PathId) continue;
                    Console.WriteLine("  material '" + mat["m_Name"].AsString + "' " + t["first"].AsString);
                    foreach (AssetClassID kind in new[] { AssetClassID.SkinnedMeshRenderer, AssetClassID.MeshRenderer })
                        foreach (AssetFileInfo ri in af.file.Metadata.GetAssetsOfType(kind))
                        {
                            AssetTypeValueField r = man.GetBaseField(af, ri);
                            bool draws = false;
                            foreach (AssetTypeValueField slot in r["m_Materials"]["Array"].Children)
                                draws |= slot["m_PathID"].AsLong == mi.PathId;
                            if (!draws) continue;
                            long meshId = 0;
                            if (kind == AssetClassID.MeshRenderer)
                            {
                                // A static renderer HAS no m_Mesh field at all - reading it throws - so the
                                // mesh comes only from the MeshFilter on the same GameObject.
                                long go = r["m_GameObject"]["m_PathID"].AsLong;
                                foreach (AssetFileInfo fi in af.file.Metadata.GetAssetsOfType(AssetClassID.MeshFilter))
                                {
                                    AssetTypeValueField f = man.GetBaseField(af, fi);
                                    if (f["m_GameObject"]["m_PathID"].AsLong == go)
                                        meshId = f["m_Mesh"]["m_PathID"].AsLong;
                                }
                            }
                            else meshId = r["m_Mesh"]["m_PathID"].AsLong;
                            Console.WriteLine("      drawn on mesh '" + PrefabFields.Name(man, af, meshId) + "'");
                        }
                }
            }
        }
        finally { man.UnloadAll(); }
        return 0;
    }

    /// <summary>
    /// `--validate &lt;projectDir&gt; [shipped.bundle ...]`: the dashboard's Validate stage, run offline on a
    /// real project folder. The same StageValidate the panel's first row runs - manifest shape, every
    /// row's source file, and the patch key - so a manifest edit can be proven before a game launch.
    /// </summary>
    private static int Validate(string[] a)
    {
        var shipped = new List<string>();
        for (int i = 2; i < a.Length; i++) shipped.Add(a[i]);
        LifecycleState.StageReport r = StageValidate.Run(a[1], Path.Combine(a[1], "ppcontent.json"),
                                                         shipped, new Dictionary<string, bool>());
        Console.WriteLine(r.Outcome + " - " + r.Verdict);
        return r.Outcome == GateOutcome.Pass ? 0 : 1;
    }

    private static int Main(string[] args)
    {
        if (args.Length == 5 && args[0] == "--bake") return Bake(args);
        if (args.Length == 2 && args[0] == "--u9probe") return U9Probe(args);
        if (args.Length == 3 && args[0] == "--dropshard") return DropShard(args);
        if ((args.Length == 3 || args.Length == 4) && args[0] == "--fit") return Fit(args);
        if (args.Length == 3 && args[0] == "--uses") return Uses(args);
        if (args.Length >= 2 && args[0] == "--validate") return Validate(args);
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
        Console.WriteLine(CatalogTests.Run());
        Console.WriteLine(OverlayTests.Run());
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
        Console.WriteLine(ManifestTests.Run());
        Console.WriteLine(ProjectScaffoldTests.Run());
        Console.WriteLine(LifecycleTests.Run());
        Console.WriteLine(GlbSkelTests.Run());
        Console.WriteLine(GlbZipTests.Run());
        Console.WriteLine(MeshExtractTests.Run());
        Console.WriteLine(VideoExtractTests.Run());
        Console.WriteLine(VideoCatalogTests.Run());
        Console.WriteLine(AudioExtractTests.Run());
        Console.WriteLine(SoundbankNamesTests.Run());
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
