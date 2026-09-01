using System;
using System.Collections.Generic;
using System.IO;
using Morgott.ContentTool.Import;

/// <summary>
/// THE ONE FIX THAT IS HONEST TO DO IN-GAME: renaming a bone IN THE FILE so it names the bone the
/// game's skeleton already has. The rename is SIMULTANEOUS - every new name is read off the ORIGINAL
/// names - or a map that swaps two bones would apply one half and then the other onto its own result.
/// Nothing else moves: the joint order, the weights, the inverse bind matrices and the node parents
/// are the file's own, byte for byte, which is what the last check measures.
/// </summary>
internal static class AliasTests
{
    internal static string Run()
    {
        int checks = 0;

        // ---- a swap, which is the case a sequential rename gets wrong.
        SkinnedModel m = Model("A", "B");
        byte[] beforeIbm = Bytes(m.InverseBindMatrices);
        ushort[] beforeJoints = (ushort[])m.Joints.Clone();
        var map = new Dictionary<string, string> { { "A", "B" }, { "B", "A" } };
        IList<string> unused;
        AliasMap.Of(map).Apply(m, out unused);
        checks += Check(m.JointNames[0] == "B" && m.JointNames[1] == "A",
                        "A<->B swapped simultaneously: " + m.JointNames[0] + "," + m.JointNames[1]);
        checks += Check(m.Nodes[m.JointNodes[0]].Name == "B" && m.Nodes[m.JointNodes[1]].Name == "A",
                        "the node names followed the joint names");
        checks += Check(unused.Count == 0, "nothing was reported unused: " + unused.Count);

        // ---- the index tables are untouched. This is the whole safety argument for renaming in place.
        checks += Check(Same(beforeIbm, Bytes(m.InverseBindMatrices)), "the inverse bind matrices are byte-identical");
        checks += Check(Same(beforeJoints, m.Joints), "the per-vertex joint slots are unchanged");

        // ---- a key the file does not have is IGNORED and reported, and the rest still applies.
        SkinnedModel partial = Model("A", "B");
        AliasMap.Of(new Dictionary<string, string> { { "A", "Root" }, { "Q", "Neck" } })
                .Apply(partial, out unused);
        checks += Check(partial.JointNames[0] == "Root" && partial.JointNames[1] == "B",
                        "the valid entry applied while the absent key did not: " + partial.JointNames[0] +
                        "," + partial.JointNames[1]);
        checks += Check(unused.Count == 1 && unused[0] == "Q", "the absent key is named back: " + unused.Count);

        // ---- an output used twice is refused whole: it would make two file bones one, which is
        // exactly the PlainCollision the binder already refuses, only silently.
        checks += Check(AliasMap.Of(new Dictionary<string, string> { { "A", "R" }, { "B", "R" } }) == null,
                        "a colliding output is refused");
        checks += Check(AliasMap.Of(new Dictionary<string, string> { { "A", "" } }) == null,
                        "an empty output is refused");
        checks += Check(AliasMap.Of(null) == null, "a null map is refused");

        // ---- the sidecar: what LOADS, what does not, and why.
        string dir = Path.Combine(Path.GetTempPath(), "ct_alias_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            string glb = Path.Combine(dir, "x.glb");
            byte[] bytes = new byte[] { 1, 2, 3, 4 };
            File.WriteAllBytes(glb, bytes);
            string sha = AliasMap.Sha256(bytes);

            string why;
            checks += Check(AliasMap.LoadSidecar(glb, sha, out why) == null && why == null,
                            "no sidecar is not a problem, and says nothing: " + why);

            AliasMap.SaveSidecar(glb, sha, bytes.Length,
                                 new Dictionary<string, string> { { "A", "Root" } });
            checks += Check(File.Exists(AliasMap.SidecarPathOf(glb)), "the sidecar was created");
            AliasMap loaded = AliasMap.LoadSidecar(glb, sha, out why);
            checks += Check(loaded != null && loaded.Count == 1 && why == null,
                            "a matching sidecar loads clean: " + why);

            // updating an existing sidecar goes down File.Replace, not File.Move
            AliasMap.SaveSidecar(glb, sha, bytes.Length,
                                 new Dictionary<string, string> { { "A", "Root" }, { "B", "Neck" } });
            checks += Check(AliasMap.LoadSidecar(glb, sha, out why).Count == 2, "the sidecar was updated in place");

            checks += Check(AliasMap.LoadSidecar(glb, "deadbeef", out why) == null &&
                            why != null && why.IndexOf("re-exported", StringComparison.Ordinal) >= 0,
                            "a stale hash is NOT applied and says so: " + why);

            File.WriteAllText(AliasMap.SidecarPathOf(glb), "{ not json ");
            checks += Check(AliasMap.LoadSidecar(glb, sha, out why) == null && why != null,
                            "malformed JSON is not applied and says so: " + why);

            File.WriteAllText(AliasMap.SidecarPathOf(glb),
                              "{\"schema\":99,\"source\":{\"sha256\":\"" + sha + "\"},\"bones\":{}}");
            checks += Check(AliasMap.LoadSidecar(glb, sha, out why) == null &&
                            why != null && why.IndexOf("99", StringComparison.Ordinal) >= 0,
                            "an unknown schema is not applied and names the number: " + why);

            // ---- the READ that every replacement path goes through, with its provenance intact.
            byte[] real = File.ReadAllBytes(Probe());
            string realGlb = Path.Combine(dir, "probe.glb");
            File.WriteAllBytes(realGlb, real);
            ReplacementSource plain = GlbSource.ReadReplacement(real, realGlb);
            checks += Check(plain.Model != null && plain.AliasesApplied == 0 && plain.SidecarPath == null,
                            "a .glb with no sidecar reads clean and claims no aliases");
            checks += Check(plain.Sha256 == AliasMap.Sha256(real) && plain.Bytes == real.Length,
                            "the envelope carries the bytes' own hash and length");

            string firstBone = plain.Model.JointNames[0];
            AliasMap.SaveSidecar(realGlb, plain.Sha256, real.Length,
                                 new Dictionary<string, string> { { firstBone, "CT_RENAMED" } });
            ReplacementSource aliased = GlbSource.ReadReplacement(real, realGlb);
            checks += Check(aliased.Model.JointNames[0] == "CT_RENAMED" && aliased.AliasesApplied == 1,
                            "the sidecar renamed the file's first bone: " + aliased.Model.JointNames[0]);
            checks += Check(aliased.AliasLog != null &&
                            aliased.AliasLog.IndexOf("CT_RENAMED", StringComparison.Ordinal) >= 0,
                            "and the log NAMES the mapping, so nothing is silent: " + aliased.AliasLog);
            checks += Check(aliased.Original.JointNames[0] == firstBone,
                            "the pristine names survive for re-aliasing without a re-parse");

            // ---- the block COUNTS what applied, so it cannot disagree with the bake's own number.
            SkinnedModel two = Model("A", "B");
            AliasMap described = AliasMap.Of(new Dictionary<string, string> { { "A", "Root" }, { "Q", "Neck" } });
            described.Apply(two, out unused);
            string block = described.Describe("s.json", unused);
            checks += Check(block.StartsWith("1 alias(es) from s.json", StringComparison.Ordinal) &&
                            block.IndexOf("'A' -> 'Root'", StringComparison.Ordinal) >= 0 &&
                            block.IndexOf("unused (this file has no such bone): 'Q'", StringComparison.Ordinal) >= 0 &&
                            block.IndexOf("'Q' -> 'Neck'", StringComparison.Ordinal) < 0,
                            "the log block counts the APPLIED alias and names the unused key apart: " + block);

            // ---- an empty "bones" is its own cause, not the collision sentence.
            File.WriteAllText(AliasMap.SidecarPathOf(glb),
                              "{\"schema\":1,\"source\":{\"sha256\":\"" + sha + "\"},\"bones\":{}}");
            checks += Check(AliasMap.LoadSidecar(glb, sha, out why) == null && why != null &&
                            why.IndexOf("no aliases", StringComparison.Ordinal) >= 0 &&
                            why.IndexOf("onto one", StringComparison.Ordinal) < 0,
                            "an empty sidecar says it is EMPTY, not that it collides: " + why);

            // ---- a failed write leaves no half-map beside the model.
            string blocked = Path.Combine(dir, "y.glb");
            File.WriteAllBytes(blocked, bytes);
            Directory.CreateDirectory(AliasMap.SidecarPathOf(blocked));   // the destination cannot be a file
            bool threw = false;
            try { AliasMap.SaveSidecar(blocked, sha, bytes.Length, new Dictionary<string, string> { { "A", "Root" } }); }
            catch (Exception) { threw = true; }
            checks += Check(threw && !File.Exists(AliasMap.SidecarPathOf(blocked) + ".tmp"),
                            "a write that fails rethrows and takes its .tmp with it");
        }
        finally { try { Directory.Delete(dir, true); } catch (Exception) { } }

        return "ALIAS PASS, " + checks + " check(s) - simultaneous rename, untouched index tables, sidecar policy";
    }

    private static SkinnedModel Model(params string[] names)
    {
        var m = new SkinnedModel { Joints = new ushort[] { 0, 1, 0, 0 }, Weights = new[] { 0.5f, 0.5f, 0f, 0f } };
        m.Nodes.Add(new SkinNode { Name = "rig", Parent = -1 });
        m.JointNodes = new int[names.Length];
        m.InverseBindMatrices = new float[names.Length][];
        for (int j = 0; j < names.Length; j++)
        {
            m.JointNames.Add(names[j]);
            m.Nodes.Add(new SkinNode { Name = names[j], Parent = 0 });
            m.JointNodes[j] = j + 1;
            m.InverseBindMatrices[j] = new float[16];
            m.InverseBindMatrices[j][12] = j + 1f;
        }
        return m;
    }

    private static byte[] Bytes(float[][] rows)
    {
        var all = new List<byte>();
        foreach (float[] r in rows) foreach (float f in r) all.AddRange(BitConverter.GetBytes(f));
        return all.ToArray();
    }

    private static bool Same(byte[] a, byte[] b)
    {
        if (a.Length != b.Length) return false;
        for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
        return true;
    }

    private static bool Same(ushort[] a, ushort[] b)
    {
        if (a.Length != b.Length) return false;
        for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
        return true;
    }

    /// <summary>The committed rigged fixture. Copied beside the build output by ContentTool.csproj,
    /// and read here from the repo so this gate does not depend on a deploy.</summary>
    internal static string Probe()
    {
        string here = AppDomain.CurrentDomain.BaseDirectory;
        string path = Path.GetFullPath(Path.Combine(here, "..\\..\\..\\..\\..\\lib\\u9_probe.glb"));
        if (!File.Exists(path)) throw new Exception("ALIAS FAILURE: lib\\u9_probe.glb is missing at " + path);
        return path;
    }

    private static int Check(bool condition, string what)
    {
        if (!condition) throw new Exception("ALIAS FAILURE: " + what);
        return 1;
    }
}
