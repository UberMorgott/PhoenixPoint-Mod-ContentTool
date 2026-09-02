using System;
using System.Collections.Generic;
using System.IO;
using Morgott.ContentTool.Import;

/// <summary>
/// SLIM: the clip census. Before anything is deleted from a .glb, the tool has to be able to SAY
/// what a clip costs - and to say honestly that dropping it would free nothing, which is the whole
/// point of the u8 fixture: 278 accessors packed into 5 bufferViews, so no clip owns a view alone
/// and no clip can free a byte. A census that reported per-clip savings there would sell a trim
/// that cannot happen.
///
/// Falsified by counting a shared bufferView as exclusive: the u8 arm goes red, and so does the u9
/// split (only Morphs and Hold own views alone).
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
        else skipped = " (SKIPPED the real-world arm - no " + ll + ")";

        // 7. The flag that stops a trim from deleting the pose a creature needs to stand up.
        Check(u9[0].Mandatory && u9[1].Mandatory && !u9[2].Mandatory && !u9[3].Mandatory &&
              u8[0].Mandatory && u8[1].Mandatory && u8[2].Mandatory && u8[3].Mandatory && u8[4].Mandatory,
              "the mandatory heuristic marks Walk/walk and every Spider_* action, and leaves Morphs/Hold alone");

        return "SLIM PASS, " + checks + " check(s)" + skipped;
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
