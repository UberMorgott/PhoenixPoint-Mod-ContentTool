using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
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

        // --- preservation, idempotence and the pose: what the rewrite must NOT change ---
        //
        // Every fixture is loaded ONCE and zipped IN PLACE, with everything the rewrite must not
        // touch captured off the document before the call and read off the same document after it.
        // Holding a before-and-after pair would double a 36 MB model's footprint for no more truth.

        var runs = new Dictionary<string, Rerun>();
        foreach (string name in Preserved) runs[name] = Rezip(GlbDocument.Load(Fixture(name)));

        // 19. Every accessor no sampler names - positions, indices, joints, weights, bind matrices -
        //     still reads the same bytes. Matched by VALUE and not by index, because Trim renumbers
        //     everything it keeps and an index is therefore not a name.
        Check(runs["u8_probe.glb"].Payloads,
              "u8_probe.glb keeps every non-animation accessor byte-identical through the rewrite");

        // 20. And on the two files where Trim really does compact BIN - the harder half, because the
        //     bytes MOVE and still have to come out the same.
        Check(runs["u8_rootfold.glb"].Payloads && runs["u9_probe.glb"].Payloads,
              "u8_rootfold.glb and u9_probe.glb keep theirs across a BIN compaction");

        // 21. Images are the loudest thing a compaction can cut loose: they own a bufferView directly,
        //     with no accessor between, so a view that moves without its image following is a texture
        //     of garbage that still loads.
        Rerun tiffany = Tiffany();
        Check(runs["u8_probe.glb"].Images && runs["u8_rootfold.glb"].Images &&
              runs["u9_probe.glb"].Images && (tiffany == null || tiffany.Images),
              "every image's bytes survive the rewrite" + (tiffany == null ? "" : ", the 6 in tiffany included"));

        // 22. And the skin: inverse bind matrices are what stands a mesh on its rig, and they are an
        //     accessor no animation names, so nothing else in this gate would catch them moving.
        Check(runs["u8_probe.glb"].Skins && runs["u8_rootfold.glb"].Skins && runs["u9_probe.glb"].Skins,
              "every skin's inverseBindMatrices read the same bytes afterwards");

        // 23-24. IDEMPOTENCE. ReadFloats understands the normalized SHORT the quantiser writes, so a
        //        second run reads back what the first wrote and re-emits it - not approximately, bit
        //        for bit. A tool that drifts on the second run cannot be run by a pipeline.
        Check(Idempotent(runs["u8_rootfold.glb"]), "zipping u8_rootfold.glb twice is zipping it once");
        Check(Idempotent(runs["u8_probe.glb"]), "zipping u8_probe.glb twice is zipping it once");

        // 25. The same claim at scale and against a file this C# did not write: 36 MB, 300 clips,
        //     29,724 accessors, already zipped by tools\ppzip.py. An already-zipped file is a FIXED
        //     POINT - the BIN chunk comes back byte for byte, the JSON comes back as the same
        //     document, and zipping the RESULT again is byte-identical to zipping once.
        //
        //     NOT byte-identical to the Python file, and deliberately so: measured 2026-09-02, all
        //     271,197 number tokens parse to the same doubles and only their SPELLING differs -
        //     1,447 integral doubles Python writes as "1.0"/"0.0" and glTF is happier reading as
        //     "1"/"0", 393 exponents cased "e-05" against "E-05", and 16 doubles whose shortest
        //     round-tripping form has two equally short spellings that Python's Grisu and .NET's
        //     G16 break differently. That is a float printer's business and not the zip's, so the
        //     gate asserts what the zip is actually responsible for.
        Check(tiffany == null || (tiffany.Fixed && tiffany.Stats.Collapsed == 15149 &&
                                  tiffany.Stats.Quantised == 27284 &&
                                  tiffany.Stats.KeysBefore == 2721855 && tiffany.Stats.KeysAfter == 2721855),
              tiffany == null ? "tiffany_ppfit.glb is absent, so the real-world fixed point is skipped"
                              : "tiffany_ppfit.glb is a fixed point: " + tiffany.Note);

        // 26. NOT ONE CLIP, CHANNEL OR KEY IS DROPPED - the whole promise of the tool. Every clip name,
        //     every channel's target node and path, and on a file where nothing collapsed, every key.
        Check(runs["u8_probe.glb"].Clips && runs["u8_rootfold.glb"].Clips && runs["u9_probe.glb"].Clips,
              "every clip keeps its name, its channel count and every channel's target node and path");

        // 27. Quantising a quaternion component-wise moves it off the unit sphere by at most the
        //     quantum; nothing renormalises, deliberately, so the gate asserts the drift instead.
        Check(runs["u8_probe.glb"].Norms && runs["u8_rootfold.glb"].Norms && runs["u9_probe.glb"].Norms,
              "no quantised rotation key leaves the unit sphere by more than 1e-4");

        // 28. And the claim none of the above makes: the POSE. Sampled off the raw curves at 17 times
        //     per sampler, by a sampler that owes GlbReader nothing, so the gate can disagree with the
        //     importer. Rotations within two quanta, everything else EXACTLY equal.
        Check(runs["u8_probe.glb"].Pose == null && runs["u8_rootfold.glb"].Pose == null &&
              runs["u9_probe.glb"].Pose == null,
              "every sampler poses the same at 17 times before and after: " +
              (runs["u8_probe.glb"].Pose ?? runs["u8_rootfold.glb"].Pose ?? runs["u9_probe.glb"].Pose));

        // 29. A hostile accessor cannot divide the rewrite by zero. An unknown "type" costs
        //     ElementSize 0, which Packed refuses before ReadFloats can splice anything - the curve is
        //     left alone and the clip still comes out whole.
        GlbDocument junk = GlbDocument.Load(Synthetic());
        GlbSlim.Obj(GlbSlim.Arr(junk.Json, "accessors")[2])["type"] = "MAT7";
        GlbZip.Stats junkStats = GlbZip.Zip(junk, true, true);
        Check(junkStats.Skipped >= 1 && junkStats.Quantised <= 1 && Names(junk) == "Clip",
              "an accessor whose type glTF does not define is skipped, not divided by");

        // --- the job around the rewrite: stages, cancel, the atomic swap and the read-back ---
        //
        // Everything below runs in a temp directory on a COPY of a fixture, so a gate that goes wrong
        // cannot rewrite a file the repo committed.

        string work = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ct_zip_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);
        try
        {
            string source = System.IO.Path.Combine(work, "u8_rootfold.glb");
            byte[] sourceBytes = File.ReadAllBytes(Fixture("u8_rootfold.glb"));
            File.WriteAllBytes(source, sourceBytes);
            Func<bool> noTmp = () => Directory.GetFiles(work, "*.ct_tmp").Length == 0;

            // 30. The shrink case end to end: the destination is a NEW smaller file and the source is
            //     the file it was - a rewrite the author cannot undo is a rewrite nobody ran.
            string target = System.IO.Path.Combine(work, "out.glb");
            var seen = new List<SlimProgress>();
            string sentence = SlimJob.Zip(source, target, true, true, CancellationToken.None, seen.Add);
            Check(File.Exists(target) && new FileInfo(target).Length < sourceBytes.Length &&
                  Same(File.ReadAllBytes(source), sourceBytes) && sentence.Contains("reads back as 1 clip(s)"),
                  "a zip run writes a smaller sibling, leaves the source alone and says the file still " +
                  "imports: " + sentence);

            // 31. Cancel: nothing is created at all. The swap is the only line that touches the
            //     destination and a cancel is seen at the stage boundary before it.
            string never = System.IO.Path.Combine(work, "never.glb");
            var cts = new CancellationTokenSource();
            cts.Cancel();
            bool cancelled = false;
            try { SlimJob.Zip(source, never, true, true, cts.Token, null); }
            catch (OperationCanceledException) { cancelled = true; }
            Check(cancelled && !File.Exists(never) && Same(File.ReadAllBytes(source), sourceBytes),
                  "a cancelled zip throws, creates no destination and leaves the source byte-identical");

            // 32. And no half-written temp is left for the next run to trip over, whichever way the
            //     run ended.
            Check(noTmp(), "no .ct_tmp survives a completed or a cancelled run");

            // 33. The bar the panel draws: one snapshot per checkpoint, never past its total, ending
            //     ON it with the sentence to show.
            bool orderly = seen.Count >= 6 && seen[seen.Count - 1].Stage == "Done" &&
                           seen[seen.Count - 1].Done == seen[0].Total &&
                           seen[4].Stage == "Verify" && seen[5].Stage == "Write";
            for (int i = 0; i < seen.Count; i++)
                orderly &= seen[i].Done <= seen[i].Total && seen[i].Total == seen[0].Total &&
                           !string.IsNullOrEmpty(seen[i].Stage) && (i == 0 || seen[i].Done >= seen[i - 1].Done);
            Check(orderly, "the zip publishes " + seen.Count + " orderly snapshots and finishes on Done");

            // 34. THE GROWTH CASE IS A REFUSAL TO WRITE. u8_probe.glb interleaves its animation with
            //     mesh data in shared bufferViews, so the dense keys cannot be freed and the rewrite
            //     ADDS to them (ppzip measures +7.9%). A bigger file is not a save.
            string grown = System.IO.Path.Combine(work, "grown.glb");
            string growth = SlimJob.Zip(Fixture("u8_probe.glb"), grown, true, true, CancellationToken.None, null);
            Check(growth != null && growth.Contains("would grow") && !File.Exists(grown) && noTmp(),
                  "a rewrite that would make the file bigger is reported and not written: " + growth);

            // 35. And a file the pre-flight refuses never reaches the rewrite: the guard's own words,
            //     nothing on disk.
            string refused = System.IO.Path.Combine(work, "refused.glb");
            string reported = null;
            try { SlimJob.Zip(Fixture("u12_norm.glb"), refused, true, true, CancellationToken.None, null); }
            catch (InvalidOperationException ex) { reported = ex.Message; }
            Check(reported != null && reported == Refusal("u12_norm.glb") && !File.Exists(refused) && noTmp(),
                  "a refused zip reports the guard verbatim and writes nothing");

            // 36. A temp the game's importer refuses never becomes the destination: Verify runs on the
            //     temp BEFORE the swap, so the file that was there stays byte-identical and the temp goes.
            byte[] targetBytes = File.ReadAllBytes(target);
            Func<string, int> realReadBack = SlimJob.ReadBack;
            string unread;
            try
            {
                SlimJob.ReadBack = path => { throw new InvalidOperationException("hostile importer"); };
                unread = SlimJob.Zip(source, target, true, true, CancellationToken.None, null);
            }
            finally { SlimJob.ReadBack = realReadBack; }
            Check(unread != null && unread.Contains("does not import: hostile importer") &&
                  unread.Contains("destination left alone") && Same(File.ReadAllBytes(target), targetBytes) && noTmp(),
                  "a temp that fails read-back is reported, the destination is untouched and no .ct_tmp survives: " + unread);
        }
        finally { Directory.Delete(work, true); }

        return "GLB-ZIP PASS, " + checks + " check(s)" +
               (tiffany == null ? "\n  (skipped local\\PpFit\\Content\\Models\\tiffany_ppfit.glb - not present)" : "");
    }

    // --- the before/after machinery -------------------------------------------------------------

    /// <summary>The fixtures the whole-file promises are made over: the shrink case, the shared-view
    /// growth case, and the one ppzip crashes on.</summary>
    private static readonly string[] Preserved = { "u8_rootfold.glb", "u8_probe.glb", "u9_probe.glb" };

    /// <summary>One zip and every verdict that can be read off it.</summary>
    private sealed class Rerun
    {
        internal GlbZip.Stats Stats;
        internal bool Payloads, Images, Skins, Clips, Norms, Fixed;
        internal string Pose;        // null = every sampled pose matched
        internal string Note;
        internal byte[] Written;
    }

    /// <summary>Capture, zip in place, capture again. One document rather than a before-and-after
    /// pair, because holding a 36 MB model twice buys no more truth than reading it twice.</summary>
    private static Rerun Rezip(GlbDocument doc)
    {
        Dictionary<string, int> payloads = Payloads(doc);
        List<string> images = ImageBytes(doc), skins = BindBytes(doc), clips = ClipShape(doc);
        List<Curve> before = Curves(doc);

        var run = new Rerun { Stats = GlbZip.Zip(doc, true, true) };
        run.Payloads = SameBag(payloads, Payloads(doc));
        run.Images = SameList(images, ImageBytes(doc));
        run.Skins = SameList(skins, BindBytes(doc));
        run.Clips = SameList(clips, ClipShape(doc)) &&
                    (run.Stats.Collapsed != 0 || run.Stats.KeysBefore == run.Stats.KeysAfter);
        run.Norms = Unit(doc);
        run.Pose = Posed(before, Curves(doc));
        run.Written = doc.Write();
        return run;
    }

    /// <summary>The 36 MB real-world fixed point, or null when `local\` is not on this machine - it is
    /// gitignored, so a clone has no way to produce it and its absence is not a failure.</summary>
    private static Rerun Tiffany()
    {
        string path = Local(@"local\PpFit\Content\Models\tiffany_ppfit.glb");
        if (!System.IO.File.Exists(path)) return null;
        byte[] source = System.IO.File.ReadAllBytes(path);
        Rerun run = Rezip(GlbDocument.Load(source));

        GlbDocument was = GlbDocument.Load(source), now = GlbDocument.Load(run.Written);
        bool bin = Same(was.Bin, now.Bin), json = SameJson(was.Json, now.Json), again = Idempotent(run);
        run.Fixed = bin && json && again;
        run.Note = "BIN identical " + bin + " (" + now.Bin.Length + " B), JSON the same document " +
                   json + ", zipping the result again is a no-op " + again + "; " + run.Stats.Collapsed +
                   " collapsed (15149), " + run.Stats.Quantised + " quantised (27284), keys " +
                   run.Stats.KeysBefore + " -> " + run.Stats.KeysAfter + " (2721855)";
        return run;
    }

    /// <summary>Zip the written file again and ask for the very same bytes.</summary>
    private static bool Idempotent(Rerun run)
    {
        GlbDocument again = GlbDocument.Load(run.Written);
        GlbZip.Zip(again, true, true);
        return Same(again.Write(), run.Written);
    }

    /// <summary>Every accessor NO animation sampler names, as a multiset of its bytes. A multiset
    /// because Trim renumbers what it keeps, so the only stable identity an accessor has is its
    /// content - and two accessors with identical content are interchangeable by definition.</summary>
    private static Dictionary<string, int> Payloads(GlbDocument doc)
    {
        var animated = new HashSet<int>();
        foreach (object animation in GlbSlim.Arr(doc.Json, "animations") ?? new List<object>())
            foreach (object sampler in GlbSlim.Arr(GlbSlim.Obj(animation), "samplers") ?? new List<object>())
            {
                animated.Add(GlbSlim.Int(GlbSlim.Obj(sampler), "input", -1));
                animated.Add(GlbSlim.Int(GlbSlim.Obj(sampler), "output", -1));
            }

        var bag = new Dictionary<string, int>();
        List<object> accessors = GlbSlim.Arr(doc.Json, "accessors") ?? new List<object>();
        for (int i = 0; i < accessors.Count; i++)
        {
            if (animated.Contains(i)) continue;
            byte[] slice = Slice(doc, i);
            if (slice == null) continue;
            string key = Convert.ToBase64String(slice);
            bag.TryGetValue(key, out int seen);
            bag[key] = seen + 1;
        }
        return bag;
    }

    /// <summary>Each image's bufferView bytes, in image order - images are never renumbered.</summary>
    private static List<string> ImageBytes(GlbDocument doc)
    {
        var list = new List<string>();
        foreach (object image in GlbSlim.Arr(doc.Json, "images") ?? new List<object>())
            list.Add(Convert.ToBase64String(ViewBytes(doc, GlbSlim.Int(GlbSlim.Obj(image), "bufferView", -1))));
        return list;
    }

    /// <summary>Each skin's inverse bind matrices, followed through the skin rather than by index.</summary>
    private static List<string> BindBytes(GlbDocument doc)
    {
        var list = new List<string>();
        foreach (object skin in GlbSlim.Arr(doc.Json, "skins") ?? new List<object>())
            list.Add(Convert.ToBase64String(
                Slice(doc, GlbSlim.Int(GlbSlim.Obj(skin), "inverseBindMatrices", -1)) ?? new byte[0]));
        return list;
    }

    /// <summary>Name, channel count and every channel's target - the shape a rewrite may not alter.</summary>
    private static List<string> ClipShape(GlbDocument doc)
    {
        var list = new List<string>();
        foreach (object animation in GlbSlim.Arr(doc.Json, "animations") ?? new List<object>())
        {
            Dictionary<string, object> clip = GlbSlim.Obj(animation);
            var row = new StringBuilder(GlbSlim.Str(clip, "name") ?? "");
            List<object> channels = GlbSlim.Arr(clip, "channels") ?? new List<object>();
            row.Append('|').Append(channels.Count);
            foreach (object channel in channels)
            {
                Dictionary<string, object> target = GlbSlim.Obj(GlbSlim.Get(GlbSlim.Obj(channel), "target"));
                row.Append('|').Append(GlbSlim.Int(target, "node", -1)).Append(':')
                   .Append(GlbSlim.Str(target, "path") ?? "");
            }
            list.Add(row.ToString());
        }
        return list;
    }

    /// <summary>Whether every quantised rotation key is still a unit quaternion. Nothing renormalises
    /// - ppzip does not either - so this is where that decision is held to its own bound.</summary>
    private static bool Unit(GlbDocument doc)
    {
        foreach (Curve curve in Curves(doc))
        {
            if (curve == null || !curve.Rotation || curve.Stride != 4) continue;
            for (int key = 0; key + 4 <= curve.Values.Length; key += 4)
            {
                double sum = 0;
                for (int c = 0; c < 4; c++) sum += curve.Values[key + c] * (double)curve.Values[key + c];
                if (Math.Abs(Math.Sqrt(sum) - 1.0) > 1e-4) return false;
            }
        }
        return true;
    }

    // --- the pose oracle ------------------------------------------------------------------------

    /// <summary>One animation sampler as raw numbers, with no glTF index left in it.</summary>
    private sealed class Curve
    {
        internal float[] Times, Values;
        /// <summary>Components per KEY, derived from the key count rather than from the accessor's
        /// type: a morph-weight sampler stores N weights per key in a SCALAR accessor, so the
        /// accessor's element size is not the stride.</summary>
        internal int Stride;
        internal bool Rotation, Step, Cubic;
    }

    /// <summary>Every sampler of every clip, in file order, so before and after line up by position.
    /// A sampler this gate cannot read flat comes back null and both sides have to agree on that.</summary>
    private static List<Curve> Curves(GlbDocument doc)
    {
        var list = new List<Curve>();
        foreach (object animation in GlbSlim.Arr(doc.Json, "animations") ?? new List<object>())
        {
            Dictionary<string, object> clip = GlbSlim.Obj(animation);
            var pathOf = new Dictionary<int, string>();
            foreach (object channel in GlbSlim.Arr(clip, "channels") ?? new List<object>())
            {
                int si = GlbSlim.Int(GlbSlim.Obj(channel), "sampler", -1);
                string path = GlbSlim.Str(GlbSlim.Obj(GlbSlim.Get(GlbSlim.Obj(channel), "target")), "path");
                if (si >= 0 && path != null && !pathOf.ContainsKey(si)) pathOf[si] = path;
            }

            List<object> samplers = GlbSlim.Arr(clip, "samplers") ?? new List<object>();
            for (int si = 0; si < samplers.Count; si++)
            {
                Dictionary<string, object> sampler = GlbSlim.Obj(samplers[si]);
                float[] times = GlbZip.ReadFloats(doc, GlbSlim.Int(sampler, "input", -1));
                float[] values = GlbZip.ReadFloats(doc, GlbSlim.Int(sampler, "output", -1));
                string interpolation = GlbSlim.Str(sampler, "interpolation") ?? "LINEAR";
                pathOf.TryGetValue(si, out string path);
                list.Add(times == null || values == null || times.Length == 0 ||
                         values.Length % times.Length != 0
                    ? null
                    : new Curve
                      {
                          Times = times, Values = values, Stride = values.Length / times.Length,
                          Rotation = path == "rotation", Step = interpolation == "STEP",
                          Cubic = interpolation == "CUBICSPLINE"
                      });
            }
        }
        return list;
    }

    /// <summary>17 poses per sampler, before against after. Returns null when every one matched, else
    /// the first disagreement in words - the number matters more than the boolean when it goes red.</summary>
    private static string Posed(List<Curve> before, List<Curve> after)
    {
        if (before.Count != after.Count)
            return "the file came out with " + after.Count + " sampler(s) where it had " + before.Count;
        for (int i = 0; i < before.Count; i++)
        {
            Curve a = before[i], b = after[i];
            if ((a == null) != (b == null)) return "sampler " + i + " changed whether it can be read flat";
            if (a == null) continue;
            if (a.Stride != b.Stride || a.Rotation != b.Rotation || a.Step != b.Step || a.Cubic != b.Cubic)
                return "sampler " + i + " changed shape: stride " + a.Stride + " -> " + b.Stride;

            // CUBICSPLINE stores a tangent either side of every value, so sampling it as a line would
            // compare the gate against itself. Zip refuses to rewrite one at all, so the bytes are the
            // whole claim here.
            if (a.Cubic)
            {
                if (!Same(a.Values, b.Values, 0f)) return "sampler " + i + " (CUBICSPLINE) values moved";
                continue;
            }

            float tolerance = a.Rotation ? 2f * GlbZip.QuantMaxError : 0f;
            float first = a.Times[0], last = a.Times[a.Times.Length - 1];
            for (int step = 0; step <= 16; step++)
            {
                float t = first + (last - first) * step / 16f;
                float[] want = SampleAt(a, t), got = SampleAt(b, t);
                for (int c = 0; c < want.Length; c++)
                    if (Math.Abs(want[c] - got[c]) > tolerance)
                        return "sampler " + i + " component " + c + " at t=" + t + " moved by " +
                               Math.Abs(want[c] - got[c]) + " (allowed " + tolerance + ")";
            }
        }
        return null;
    }

    /// <summary>
    /// Sample one animation channel at a time, over the RAW curves, with no Unity and no GlbReader:
    /// the gate has to be able to disagree with the importer. LINEAR between the two bracketing keys,
    /// clamped at both ends; STEP holds the earlier key; rotations are slerped over the shorter arc
    /// the way GlbReader does (src\Import\GlbReader.cs:1472-1485), because a component-wise lerp takes
    /// the same path at the wrong speed and would fail this test for a reason that is not the zip's.
    /// </summary>
    private static float[] SampleAt(Curve curve, float t)
    {
        float[] times = curve.Times, values = curve.Values;
        int stride = curve.Stride, last = times.Length - 1;
        var pose = new float[stride];
        if (t <= times[0]) { Array.Copy(values, 0, pose, 0, stride); return pose; }
        if (t >= times[last]) { Array.Copy(values, last * stride, pose, 0, stride); return pose; }

        int i = 0;
        while (i + 1 < last && times[i + 1] <= t) i++;
        float span = times[i + 1] - times[i];
        float u = curve.Step || span <= 0 ? 0f : (t - times[i]) / span;
        if (curve.Rotation && stride == 4 && !curve.Step) return Slerp(values, i, u);
        for (int c = 0; c < stride; c++)
            pose[c] = values[i * stride + c] + (values[(i + 1) * stride + c] - values[i * stride + c]) * u;
        return pose;
    }

    /// <summary>The shorter-arc slerp of keys i and i+1, normalised. No lerp shortcut above some
    /// cosine threshold: before and after could land on OPPOSITE sides of such a threshold and differ
    /// by far more than the quantum for a reason that has nothing to do with the rewrite. Only a
    /// genuinely degenerate sine falls back, where lerp and slerp differ by ~1e-7.</summary>
    private static float[] Slerp(float[] v, int i, float u)
    {
        int a = i * 4, b = (i + 1) * 4;
        double bx = v[b], by = v[b + 1], bz = v[b + 2], bw = v[b + 3];
        double dot = v[a] * bx + v[a + 1] * by + v[a + 2] * bz + v[a + 3] * bw;
        if (dot < 0) { bx = -bx; by = -by; bz = -bz; bw = -bw; dot = -dot; }
        if (dot > 1) dot = 1;

        double s0 = 1 - u, s1 = u;
        if (dot < 0.999999)
        {
            double theta = Math.Acos(dot), sine = Math.Sin(theta);
            s0 = Math.Sin((1 - u) * theta) / sine;
            s1 = Math.Sin(u * theta) / sine;
        }
        double x = v[a] * s0 + bx * s1, y = v[a + 1] * s0 + by * s1,
               z = v[a + 2] * s0 + bz * s1, w = v[a + 3] * s0 + bw * s1;
        double length = Math.Sqrt(x * x + y * y + z * z + w * w);
        if (length <= 0) return new float[4];
        return new[] { (float)(x / length), (float)(y / length), (float)(z / length), (float)(w / length) };
    }

    // --- byte helpers ---------------------------------------------------------------------------

    /// <summary>One whole bufferView, or an empty run when the document names no such view.</summary>
    private static byte[] ViewBytes(GlbDocument doc, int index)
    {
        List<object> views = GlbSlim.Arr(doc.Json, "bufferViews");
        if (views == null || index < 0 || index >= views.Count || doc.Bin == null) return new byte[0];
        Dictionary<string, object> view = GlbSlim.Obj(views[index]);
        long at = GlbSlim.Long(view, "byteOffset", 0), length = GlbSlim.Long(view, "byteLength", 0);
        if (at < 0 || length <= 0 || at + length > doc.Bin.Length) return new byte[0];
        var bytes = new byte[length];
        Buffer.BlockCopy(doc.Bin, (int)at, bytes, 0, (int)length);
        return bytes;
    }

    /// <summary>Two parsed glTF documents, compared as VALUES: same keys in the same order, same
    /// numbers, same strings. Not as text - two correct float printers disagree about how to spell a
    /// double (Python writes an integral one as "1.0" and cases an exponent "e-05"), and that is the
    /// printer's business, not the rewrite's.</summary>
    private static bool SameJson(object a, object b)
    {
        if (a is Dictionary<string, object> left)
        {
            if (!(b is Dictionary<string, object> right) || left.Count != right.Count) return false;
            using (var one = left.GetEnumerator())
            using (var two = right.GetEnumerator())
                while (one.MoveNext() && two.MoveNext())
                {
                    if (one.Current.Key != two.Current.Key) return false;
                    if (!SameJson(one.Current.Value, two.Current.Value)) return false;
                }
            return true;
        }
        if (a is List<object> items)
        {
            if (!(b is List<object> others) || items.Count != others.Count) return false;
            for (int i = 0; i < items.Count; i++) if (!SameJson(items[i], others[i])) return false;
            return true;
        }
        return a == null ? b == null : a.Equals(b);
    }

    private static bool SameBag(Dictionary<string, int> a, Dictionary<string, int> b)
    {
        if (a.Count != b.Count) return false;
        foreach (KeyValuePair<string, int> pair in a)
            if (!b.TryGetValue(pair.Key, out int seen) || seen != pair.Value) return false;
        return true;
    }

    private static bool SameList(List<string> a, List<string> b)
    {
        if (a.Count != b.Count) return false;
        for (int i = 0; i < a.Count; i++) if (a[i] != b[i]) return false;
        return true;
    }

    private static string Local(string relative) =>
        System.IO.Path.GetFullPath(System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory,
                                                          @"..\..\..\..\..\" + relative));

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

    private static Dictionary<string, object> Accessor(GlbDocument doc, int index)
    {
        List<object> accessors = GlbSlim.Arr(doc.Json, "accessors");
        return accessors == null || index < 0 || index >= accessors.Count
            ? null : GlbSlim.Obj(accessors[index]);
    }

    private static long Count(GlbDocument doc, int accessor) =>
        GlbSlim.Long(Accessor(doc, accessor), "count", 0);

    /// <summary>The bytes an accessor reads: its view's slice, offset by its own byteOffset. Compared
    /// by VALUE and not by index, because Trim renumbers everything it keeps.</summary>
    private static byte[] Slice(GlbDocument doc, int index)
    {
        Dictionary<string, object> accessor = Accessor(doc, index);
        List<object> views = GlbSlim.Arr(doc.Json, "bufferViews");
        int vi = GlbSlim.Int(accessor, "bufferView", -1);
        if (views == null || vi < 0 || vi >= views.Count || doc.Bin == null) return null;
        Dictionary<string, object> view = GlbSlim.Obj(views[vi]);
        long at = GlbSlim.Long(view, "byteOffset", 0) + GlbSlim.Long(accessor, "byteOffset", 0);
        long length = GlbSlim.Long(accessor, "count", 0) * GlbSlim.ElementSize(accessor);
        if (at < 0 || length <= 0 || at + length > doc.Bin.Length) return null;
        var slice = new byte[length];
        Buffer.BlockCopy(doc.Bin, (int)at, slice, 0, (int)length);
        return slice;
    }

    private static bool Same(byte[] a, byte[] b)
    {
        if (a.Length != b.Length) return false;
        for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
        return true;
    }

    private static string Fixture(string name) => Local(@"lib\" + name);

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
