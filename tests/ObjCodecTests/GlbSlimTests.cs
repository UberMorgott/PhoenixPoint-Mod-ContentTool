using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using Morgott.ContentTool.Import;

/// <summary>
/// SLIM: the clip census, and the guarded trim built on it. Before anything is deleted from a .glb,
/// the tool has to be able to SAY what a clip costs - and to say honestly that dropping it would
/// free nothing, which is the whole point of the u8 fixture: 278 accessors packed into 5
/// bufferViews, so no clip owns a view alone and no clip can free a byte. A census that reported
/// per-clip savings there would sell a trim that cannot happen.
///
/// The trim arm asks the harder question: after the BIN chunk is rebuilt, does every SURVIVOR still
/// point at the bytes it used to? Every check that matters compares the actual byte range each
/// surviving accessor and image reads, before and after - an index remap that is off by one still
/// produces a file that loads, and only a byte comparison catches it.
///
/// The job arm asks the question a user only gets to ask once: when a run is cancelled or refused,
/// is the file they pointed at still the file they had? Every one of those checks reads the
/// destination bytes back and looks for the temp file the run should have cleaned up.
///
/// Falsified by counting a shared bufferView as exclusive (the u8 census arm goes red), by
/// compacting BIN without remapping bufferView indices (the survivor-bytes arm goes red), or by
/// writing straight to the destination instead of through a temp (the cancel arm goes red).
/// </summary>
internal static class GlbSlimTests
{
    private const string Apocd =
        @"E:\DEV\PhoenixPoint\ContentTool\APOCD GLBs for content tool without apply tranforms\";

    private static int checks;

    internal static string Run()
    {
        checks = 0;
        string skipped = "";

        // 1. Every clip in the file, in file order, none invented and none dropped.
        List<GlbSlim.ClipRow> u9 = GlbSlim.Census(GlbDocument.Load(Fixture("u9_probe.glb")));
        Check(Names(u9) == "Walk,walk,Morphs,Hold", "u9_probe.glb lists its 4 clips in file order, not " + Names(u9));

        // 2. The six columns a picker draws, per row: index, name, channels, samplers, and the two
        //    byte costs. Walk and walk share both their accessors, Hold owns only its input times,
        //    Morphs owns all three of its views - so only the last two can free anything.
        Check(Row(u9[0], 0, "Walk", 1, 1, 48, 0) && Row(u9[1], 1, "walk", 1, 1, 48, 0) &&
              Row(u9[2], 2, "Morphs", 2, 2, 40, 40) && Row(u9[3], 3, "Hold", 1, 1, 48, 12),
              "each u9 row carries its index, name, channel/sampler counts and both byte costs");

        // 3. AccessorBytes recomputed straight off the JSON, so the column is the formula and not a
        //    number this gate and the implementation happen to agree on.
        GlbDocument doc9 = GlbDocument.Load(Fixture("u9_probe.glb"));
        bool sums = true;
        foreach (GlbSlim.ClipRow row in GlbSlim.Census(doc9)) sums &= row.AccessorBytes == Expected(doc9, row.Index);
        Check(sums, "AccessorBytes is count x element size over the accessors the clip's samplers name");

        // 4-5. The shared-bufferView file: five clips, and not one byte any of them could free.
        List<GlbSlim.ClipRow> u8 = GlbSlim.Census(GlbDocument.Load(Fixture("u8_probe.glb")));
        Check(Names(u8) == "Spider_Attack,Spider_Death,Spider_Idle,Spider_Jump,Spider_Walk",
              "u8_probe.glb lists its 5 Spider clips");
        bool free = false;
        long accessorBytes = 0;
        foreach (GlbSlim.ClipRow row in u8) { free |= row.ExclusiveBytes != 0; accessorBytes += row.AccessorBytes; }
        Check(!free && accessorBytes > 0,
              "u8_probe.glb's clips cost " + accessorBytes + " B of accessors and free nothing - 278 accessors, 5 shared bufferViews");

        // 6. A real export with no animations at all: a census of nothing, not a crash.
        string ll = Apocd + "CHR_PX_HVY_LL_M_V01_0fa9bde0c679e665.glb";
        if (File.Exists(ll)) Check(GlbSlim.Census(GlbDocument.Load(ll)).Count == 0,
                                   "a real export carrying no animations censuses to no rows");
        else skipped = " (SKIPPED the real-world arms - no " + Apocd + ")";

        // 7. The flag that stops a trim from deleting the pose a creature needs to stand up.
        Check(u9[0].Mandatory && u9[1].Mandatory && !u9[2].Mandatory && !u9[3].Mandatory &&
              u8[0].Mandatory && u8[1].Mandatory && u8[2].Mandatory && u8[3].Mandatory && u8[4].Mandatory,
              "the mandatory heuristic marks Walk/walk and every Spider_* action, and leaves Morphs/Hold alone");

        // --- the trim ---

        byte[] u9bytes = File.ReadAllBytes(Fixture("u9_probe.glb"));

        // 8. Dropping the two clips nobody needs: the survivors stay, the dead weight goes, and the
        //    saving is exactly the exclusive bytes the census promised (Morphs 40 + Hold 12).
        GlbDocument trimmed = GlbDocument.Load(u9bytes);
        long delta = GlbSlim.Trim(trimmed, new HashSet<int> { 2, 3 });
        byte[] output = trimmed.Write();
        GlbDocument reloaded = GlbDocument.Load(output);
        Check(delta == -52 && Names(GlbSlim.Census(reloaded)) == "Walk,walk",
              "dropping Morphs+Hold leaves Walk,walk and frees the 52 B the census promised, not " + delta);

        // 9. The check an index remap cannot fake: every surviving accessor reads the SAME bytes.
        //    u9 accessors 0-4 are the mesh, 5 the skin's inverse binds, 6 and 8 the Walk sampler.
        GlbDocument before = GlbDocument.Load(u9bytes);
        int[] survivors = { 0, 1, 2, 3, 4, 5, 6, 8 };
        bool identical = true;
        for (int i = 0; i < survivors.Length; i++)
            identical &= Same(Slice(before, survivors[i]), Slice(reloaded, i));
        Check(identical && ((List<object>)reloaded.Json["accessors"]).Count == survivors.Length,
              "every accessor that survived the trim still reads its own bytes after BIN compaction");

        // 10. The trimmed file is itself a well-formed .glb, not merely one this reader tolerates.
        Check(Same(reloaded.Write(), output), "the trimmed file round-trips byte-identically");

        // 11. Nothing dangles: every index lands inside its array, every view inside BIN, and the
        //     buffer's declared length is the BIN chunk it actually got.
        Check(Sound(reloaded), "the trimmed document has no dangling accessor or bufferView reference");

        // 12. The single-clip case the panel will sell: Morphs owns three views and nothing else.
        GlbDocument one = GlbDocument.Load(u9bytes);
        Check(GlbSlim.Trim(one, new HashSet<int> { 2 }) == -40 && one.Bin.Length == 340,
              "dropping Morphs alone frees exactly its 40 exclusive bytes");

        // 13-14. The guard that stops a soldier from T-posing, and the override for when it is wrong.
        GlbDocument doc = GlbDocument.Load(u9bytes);
        string refusal = GlbSlim.Guard(doc, new HashSet<int> { 0 }, false);
        Check(refusal != null && refusal.Contains("Walk"),
              "dropping the mandatory Walk clip is refused by name, not silently allowed");
        Check(GlbSlim.Guard(doc, new HashSet<int> { 0 }, true) == null,
              "force lets the mandatory clip go");

        // 15. The shared-bufferView file again, this time trimmed: a clip leaves, not one byte does.
        GlbDocument u8doc = GlbDocument.Load(Fixture("u8_probe.glb"));
        byte[] u8bin = (byte[])u8doc.Bin.Clone();
        long u8delta = GlbSlim.Trim(u8doc, new HashSet<int> { 0 });
        Check(u8delta == 0 && Same(u8doc.Bin, u8bin) && GlbSlim.Census(u8doc).Count == 4,
              "dropping a Spider clip from the shared-bufferView file frees 0 B and moves no byte");

        // 16. The hole-closer: buffer data owned by something the trim never walks - a sparse
        //     accessor, Draco, an unknown extension - is a refusal, force or not.
        byte[] extra = Glb(JsonOf(u9bytes).Replace("\"scene\":0", "\"scene\":0,\"extras\":{\"bufferView\":0}"),
                           BinOf(u9bytes));
        string structural = GlbSlim.Guard(GlbDocument.Load(extra), new HashSet<int>(), true);
        Check(structural != null && structural.Contains("bufferView"),
              "a bufferView named by something other than an accessor or an image is refused even with force");

        // 17. A rigged character with a clip library is not a prop; trimming one needs a deliberate act.
        var clips = new List<string>();
        for (int i = 0; i < 31; i++) clips.Add("{\"name\":\"clipA" + i + "\"}");
        byte[] rig = Glb("{\"asset\":{\"version\":\"2.0\"},\"nodes\":[{\"name\":\"n\"}],\"skins\":[{\"joints\":[0]}]," +
                         "\"animations\":[" + string.Join(",", clips.ToArray()) + "]}", new byte[4]);
        GlbDocument rigDoc = GlbDocument.Load(rig);
        string rigRefusal = GlbSlim.Guard(rigDoc, new HashSet<int> { 0 }, false);
        Check(rigRefusal != null && rigRefusal.Contains("skin") &&
              GlbSlim.Guard(rigDoc, new HashSet<int> { 0 }, true) == null,
              "a skinned file with 31 clips is refused without force and allowed with it");

        // 18. The real export: a trim that drops nothing must touch nothing - the 4 MB image and the
        //     skin come out of it byte for byte, or the tool is not safe to point at a shipped asset.
        string ts = Apocd + "CHR_PX_HVY_TS_M_V01_7c71cfba6f4e08f7.glb";
        if (File.Exists(ll) && File.Exists(ts))
        {
            byte[] llBytes = File.ReadAllBytes(ll);
            GlbDocument llDoc = GlbDocument.Load(llBytes);
            byte[] image = ImageBytes(GlbDocument.Load(llBytes));
            long llDelta = GlbSlim.Trim(llDoc, new HashSet<int>());
            GlbDocument tsDoc = GlbDocument.Load(ts);
            long tsDelta = GlbSlim.Trim(tsDoc, new HashSet<int>());
            Check(llDelta == 0 && tsDelta == 0 && Same(llDoc.Write(), llBytes) &&
                  Same(tsDoc.Write(), File.ReadAllBytes(ts)) && image.Length > 0 &&
                  Same(ImageBytes(llDoc), image),
                  "a real export trimmed of nothing comes back byte-identical, image bytes included");
        }
        else skipped = " (SKIPPED the real-world arms - no " + Apocd + ")";

        // --- the job around it: progress, cancel, atomic save ---

        string work = Path.Combine(Path.GetTempPath(), "ct_slim_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);
        try
        {
            string source = Path.Combine(work, "u9.glb");
            File.WriteAllBytes(source, u9bytes);
            string target = Path.Combine(work, "out.glb");
            Func<bool> noTmp = () => Directory.GetFiles(work, "*.ct_tmp").Length == 0;

            // 19. The save REPLACES: the destination already holds a file, and what lands is the
            //     trimmed .glb whole - never bytes written over the ones that were there. A run that
            //     drops nothing copies the source verbatim, so the safe case stays byte-exact.
            GlbDocument expect = GlbDocument.Load(u9bytes);
            GlbSlim.Trim(expect, new HashSet<int> { 2, 3 });
            byte[] want = expect.Write();
            File.WriteAllBytes(target, new byte[] { 1, 2, 3 });
            var seen = new List<SlimProgress>();
            SlimJob.Execute(source, target, new HashSet<int> { 2, 3 }, false, CancellationToken.None, seen.Add);
            string fresh = Path.Combine(work, "fresh.glb");
            SlimJob.Execute(source, fresh, new HashSet<int>(), false, CancellationToken.None, null);
            Check(Same(File.ReadAllBytes(target), want) && noTmp() &&
                  Same(File.ReadAllBytes(source), u9bytes) && Same(File.ReadAllBytes(fresh), u9bytes),
                  "a run replaces the destination with the trimmed file, leaves no .ct_tmp behind and " +
                  "copies the source verbatim when nothing is dropped");

            // 20. Progress: one snapshot per stage, never running backwards, never past its total,
            //     and ending ON it - a bar the panel can draw without arithmetic of its own.
            bool orderly = seen.Count > 0 && seen[seen.Count - 1].Done == seen[0].Total;
            for (int i = 0; i < seen.Count; i++)
                orderly &= seen[i].Total == seen[0].Total && seen[i].Done <= seen[i].Total &&
                           !string.IsNullOrEmpty(seen[i].Stage) && !string.IsNullOrEmpty(seen[i].Message) &&
                           (i == 0 || seen[i].Done >= seen[i - 1].Done);
            Check(orderly, "progress snapshots arrive in order, never exceed their total and finish at it");

            // 21. Cancel: the destination is exactly the file it was, and no half-written temp is
            //     left for the next run to trip over.
            byte[] landed = File.ReadAllBytes(target);
            var cts = new CancellationTokenSource();
            cts.Cancel();
            bool cancelled = false;
            try { SlimJob.Execute(source, target, new HashSet<int> { 2, 3 }, false, cts.Token, null); }
            catch (OperationCanceledException) { cancelled = true; }
            Check(cancelled && Same(File.ReadAllBytes(target), landed) && noTmp(),
                  "a cancelled run throws, leaves the destination byte-identical and leaves no .ct_tmp");

            // 22. A refusal is the same story with a sentence attached: the guard's own words reach
            //     the caller, and nothing on disk moved.
            string reported = null;
            try { SlimJob.Execute(source, target, new HashSet<int> { 0 }, false, CancellationToken.None, null); }
            catch (InvalidOperationException ex) { reported = ex.Message; }
            Check(reported == GlbSlim.Guard(GlbDocument.Load(u9bytes), new HashSet<int> { 0 }, false) &&
                  reported != null && Same(File.ReadAllBytes(target), landed) && noTmp(),
                  "a refused run reports the guard's refusal verbatim and leaves the destination alone");

            // 23. A cancel that lands AFTER the swap is a write, not a cancel: the file is already
            //     in place, so the run returns its sentence instead of claiming it left things alone.
            var late = new CancellationTokenSource();
            string outcome = SlimJob.Execute(source, target, new HashSet<int> { 2, 3 }, false, late.Token,
                delegate (SlimProgress p)
                {
                    if (p.Stage != "Done") return;
                    late.Cancel();
                    late.Token.ThrowIfCancellationRequested();
                });
            Check(outcome != null && outcome.StartsWith("dropped 2 of 4") &&
                  Same(File.ReadAllBytes(target), want) && noTmp(),
                  "a cancel arriving after the swap reports the write that happened and keeps the file");
        }
        finally { Directory.Delete(work, true); }

        return "SLIM PASS, " + checks + " check(s)" + skipped;
    }

    /// <summary>The bytes an accessor reads: its bufferView's slice, offset by its own byteOffset.</summary>
    private static byte[] Slice(GlbDocument doc, int accessor)
    {
        var a = (Dictionary<string, object>)((List<object>)doc.Json["accessors"])[accessor];
        var v = (Dictionary<string, object>)((List<object>)doc.Json["bufferViews"])[(int)(double)a["bufferView"]];
        int component = (int)(double)a["componentType"] == 5123 ? 2 : 4;
        string type = (string)a["type"];
        int lanes = type == "SCALAR" ? 1 : type == "VEC3" ? 3 : type == "VEC4" ? 4 : type == "MAT4" ? 16 : 0;
        return Cut(doc.Bin, (int)(L(v, "byteOffset") + L(a, "byteOffset")), (int)L(a, "count") * component * lanes);
    }

    /// <summary>The bytes the first image reads out of BIN.</summary>
    private static byte[] ImageBytes(GlbDocument doc)
    {
        var image = (Dictionary<string, object>)((List<object>)doc.Json["images"])[0];
        var v = (Dictionary<string, object>)((List<object>)doc.Json["bufferViews"])[(int)(double)image["bufferView"]];
        return Cut(doc.Bin, (int)L(v, "byteOffset"), (int)L(v, "byteLength"));
    }

    /// <summary>Every index in the trimmed document lands somewhere real, and BIN is exactly the
    /// span its bufferViews claim plus the container's own 4-byte alignment.</summary>
    private static bool Sound(GlbDocument doc)
    {
        var accessors = (List<object>)doc.Json["accessors"];
        var views = (List<object>)doc.Json["bufferViews"];
        foreach (object animation in (List<object>)doc.Json["animations"])
            foreach (object sampler in (List<object>)((Dictionary<string, object>)animation)["samplers"])
            {
                var s = (Dictionary<string, object>)sampler;
                if ((int)(double)s["input"] >= accessors.Count || (int)(double)s["output"] >= accessors.Count) return false;
            }
        foreach (object mesh in (List<object>)doc.Json["meshes"])
            foreach (object primitive in (List<object>)((Dictionary<string, object>)mesh)["primitives"])
            {
                var p = (Dictionary<string, object>)primitive;
                if ((int)(double)p["indices"] >= accessors.Count) return false;
                foreach (KeyValuePair<string, object> slot in (Dictionary<string, object>)p["attributes"])
                    if ((int)(double)slot.Value >= accessors.Count) return false;
            }
        foreach (object skin in (List<object>)doc.Json["skins"])
            if ((int)(double)((Dictionary<string, object>)skin)["inverseBindMatrices"] >= accessors.Count) return false;

        long span = 0;
        foreach (object accessor in accessors)
            if ((int)(double)((Dictionary<string, object>)accessor)["bufferView"] >= views.Count) return false;
        foreach (object view in views)
        {
            var v = (Dictionary<string, object>)view;
            long end = L(v, "byteOffset") + L(v, "byteLength");
            if (end > doc.Bin.Length) return false;
            span = Math.Max(span, end);
        }
        var buffer = (Dictionary<string, object>)((List<object>)doc.Json["buffers"])[0];
        return L(buffer, "byteLength") == doc.Bin.Length && doc.Bin.Length - span < 4;
    }

    private static long L(Dictionary<string, object> map, string key) =>
        map.TryGetValue(key, out object value) ? (long)(double)value : 0;

    private static string JsonOf(byte[] glb) => Encoding.UTF8.GetString(glb, 20, (int)U32(glb, 12));

    private static byte[] BinOf(byte[] glb)
    {
        int at = 20 + (int)U32(glb, 12);
        return Cut(glb, at + 8, (int)U32(glb, at));
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

    private static uint U32(byte[] bytes, int at) =>
        (uint)(bytes[at] | (bytes[at + 1] << 8) | (bytes[at + 2] << 16) | (bytes[at + 3] << 24));

    private static byte[] Cut(byte[] data, int at, int length)
    {
        var slice = new byte[length];
        Buffer.BlockCopy(data, at, slice, 0, length);
        return slice;
    }

    private static bool Same(byte[] a, byte[] b)
    {
        if (a.Length != b.Length) return false;
        for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
        return true;
    }

    /// <summary>count x element size over the clip's sampler accessors, read off the JSON here.</summary>
    private static long Expected(GlbDocument doc, int clip)
    {
        var accessors = (List<object>)doc.Json["accessors"];
        var animation = (Dictionary<string, object>)((List<object>)doc.Json["animations"])[clip];
        var seen = new HashSet<int>();
        long bytes = 0;
        foreach (object sampler in (List<object>)animation["samplers"])
        {
            var s = (Dictionary<string, object>)sampler;
            foreach (string end in new[] { "input", "output" })
            {
                int index = (int)(double)s[end];
                if (!seen.Add(index)) continue;
                var a = (Dictionary<string, object>)accessors[index];
                int component = (int)(double)a["componentType"] == 5123 ? 2 : 4;
                string type = (string)a["type"];
                int lanes = type == "SCALAR" ? 1 : type == "VEC3" ? 3 : type == "VEC4" ? 4 : type == "MAT4" ? 16 : 0;
                bytes += (long)(double)a["count"] * component * lanes;
            }
        }
        return bytes;
    }

    private static bool Row(GlbSlim.ClipRow row, int index, string name, int channels, int samplers,
                            long accessorBytes, long exclusiveBytes) =>
        row.Index == index && row.Name == name && row.Channels == channels && row.Samplers == samplers &&
        row.AccessorBytes == accessorBytes && row.ExclusiveBytes == exclusiveBytes;

    private static string Names(List<GlbSlim.ClipRow> rows)
    {
        var names = new List<string>();
        foreach (GlbSlim.ClipRow row in rows) names.Add(row.Name);
        return string.Join(",", names.ToArray());
    }

    private static string Fixture(string name) =>
        Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,
                                      @"..\..\..\..\..\lib\" + name));

    private static void Check(bool ok, string what)
    {
        checks++;
        if (!ok) throw new Exception("SLIM FAIL: " + what);
    }
}
