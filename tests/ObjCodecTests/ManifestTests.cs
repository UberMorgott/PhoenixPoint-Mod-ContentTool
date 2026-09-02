using System;
using System.IO;
using System.Text;
using Morgott.ContentTool.IO;
using Morgott.ContentTool.Project;

/// <summary>The manifest core: read ppcontent.json into a facade over the REAL tree, add one "replace"
/// row, write it back with every byte outside the "replace" value span untouched. Every arm is a case
/// the regex at ContentProject.cs:388-392 gets wrong, or a way a user edit could be lost.</summary>
internal static class ManifestTests
{
    internal static string Run()
    {
        int checks = 0;
        string dir = Path.Combine(Path.GetTempPath(), "ct_manifest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            // ---- AtomicFile_WriteLeavesBakAndNoTmp
            string f = Path.Combine(dir, "a.txt");
            AtomicFile.Write(f, new byte[] { 1, 2, 3 }, f + ".bak");
            checks += Check(File.Exists(f) && !File.Exists(f + ".bak"),
                            "a FIRST write leaves no .bak - File.Replace would throw, File.Move is what runs");
            checks += Check(Temps(dir).Length == 0, "and no temp survives it");
            AtomicFile.Write(f, new byte[] { 9 }, f + ".bak");
            byte[] bak = File.ReadAllBytes(f + ".bak");
            checks += Check(bak.Length == 3 && bak[0] == 1 && bak[2] == 3,
                            "an overwrite leaves the PRE-write bytes in .bak: " + bak.Length + " B");
            checks += Check(File.ReadAllBytes(f).Length == 1 && Temps(dir).Length == 0,
                            "the destination is the new bytes and no temp is left");

            // A ".tmp" a previous crash left behind must not block the next write, and must not be
            // adopted by it: the name is unique per write, so it is simply another file.
            File.WriteAllBytes(f + ".tmp", new byte[] { 7, 7 });
            AtomicFile.Write(f, new byte[] { 4, 5 }, f + ".bak");
            checks += Check(File.ReadAllBytes(f).Length == 2 && File.Exists(f + ".tmp"),
                            "a stale .tmp neither blocks the write nor is adopted by it");
            File.Delete(f + ".tmp");

            // A failure BEFORE the commit leaves no temp of its own behind.
            string wall = Path.Combine(dir, "wall.txt");
            Directory.CreateDirectory(wall);          // the destination cannot be a file
            bool blocked = false;
            try { AtomicFile.Write(wall, new byte[] { 1 }, null); }
            catch (Exception) { blocked = true; }
            checks += Check(blocked && Temps(dir).Length == 0,
                            "a write that cannot commit rethrows and takes its temp with it");

            // ---- Manifest_LoadsKnownAndUnknownTree: the case "\{[^{}]*\}" cannot read at all.
            const string tree =
                "{\n  \"id\": \"m.demo\",\n  \"bundle\": \"M.bundle\",\n  \"scale\": 0.008,\n" +
                "  \"play\": \"Idle\",\n  \"loop\": \"Idle, Walk\",\n  \"replace\": [\n" +
                "    { \"bundle\": \"a.bundle\", \"asset\": \"Foo\", \"mesh\": \"body\", \"opts\": { \"x\": 1 } },\n" +
                "    { \"bundle\": \"b.bundle\", \"asset\": \"Bar]\", \"texture\": \"swatch\" }\n  ],\n" +
                "  \"creature\": { \"clips\": { \"Spider_Walk\": \"walk\" } },\n  \"somethingNew\": [ 1, 2, 3 ]\n}\n";
            Manifest read = Manifest.Parse(tree);
            checks += Check(read.Id == "m.demo" && read.Bundle == "M.bundle" && read.Play == "Idle" &&
                            read.Loop == "Idle, Walk" && read.Scale == 0.008,
                            "the root scalars arrive typed, scale as a double: " + read.Scale);
            checks += Check(read.Replace.Count == 2,
                            "BOTH rows read - a nested map and a ']' inside a string end neither: " + read.Replace.Count);
            checks += Check(read.Replace[0].Kind == "mesh" && read.Replace[0].Asset == "Foo" &&
                            read.Replace[1].Kind == "texture" && read.Replace[1].Asset == "Bar]",
                            "each row's kind and asset, the bracketed one included");
            checks += Check(read.Replace[0].Tree.ContainsKey("opts"),
                            "the unknown nested member of a row is RETAINED, not dropped");
            checks += Check(read.Root.ContainsKey("creature") && read.Root.ContainsKey("somethingNew"),
                            "unknown root keys survive - the tree is the file's, not a model of it");
            Manifest bare = Manifest.Parse("{ \"bundle\": \"M.bundle\" }");
            checks += Check(bare.Id == null && bare.Replace.Count == 0 && !bare.Declares("replace"),
                            "Parse is the TOLERANT entry: no id, no replace, no throw - Package holds text, not a path");

            // ---- ManifestFile.Load, the strict boundary
            string path = Path.Combine(dir, "ppcontent.json");
            File.WriteAllBytes(path, Bytes(true, Crlf));
            ManifestFile file = ManifestFile.Load(path);
            checks += Check(file.Manifest.Id == "m.demo" && file.Manifest.Replace.Count == 1 && file.Path == path,
                            "a BOM + CRLF file loads, and the facade came with it");
            checks += Check(file.Manifest.Replace[0].Kind == "texture", "its one row is a texture row");

            // ---- Manifest_RefusesMalformedWithoutWriting
            string broken = Path.Combine(dir, "broken.json");
            byte[] before = Bytes(false, "{ \"id\": \"m\", \"bundle\": \"M.bundle\", \"replace\": [ {");
            File.WriteAllBytes(broken, before);
            string why = null;
            try { ManifestFile.Load(broken); }
            catch (InvalidDataException bad) { why = bad.Message; }
            checks += Check(why != null && why.IndexOf(broken, StringComparison.Ordinal) >= 0 &&
                            why.IndexOf("is not valid JSON", StringComparison.Ordinal) >= 0,
                            "truncated JSON is refused and the sentence NAMES THE PATH: " + why);
            checks += Check(Same(File.ReadAllBytes(broken), before) &&
                            Temps(dir).Length == 0 && !File.Exists(broken + ".bak"),
                            "and the original bytes are untouched, with no temp and no .bak beside them");
            string headless = Path.Combine(dir, "headless.json");
            File.WriteAllBytes(headless, Bytes(false, "{ \"bundle\": \"M.bundle\" }"));
            why = null;
            try { ManifestFile.Load(headless); }
            catch (InvalidDataException bad) { why = bad.Message; }
            checks += Check(why == "ppcontent.json needs both \"id\" and \"bundle\"",
                            "E2 is the sentence ContentProject.cs:289 already says, word for word: " + why);
            checks += Check(Manifest.Parse("{ \"bundle\": \"M.bundle\" }").Bundle == "M.bundle",
                            "and Manifest.Parse does NOT apply that rule - only the file boundary does");

            // V1, the STRICT decode: a byte that is not UTF-8 must refuse, not decode to U+FFFD and get written back.
            string mangled = Path.Combine(dir, "mangled.json");
            byte[] raw = Bytes(false, "{ \"id\": \"m\", \"bundle\": \"M.bundle\", \"note\": \"XX\" }");
            raw[raw.Length - 5] = 0xFF;                  // the second 'X': 0xFF is not a UTF-8 byte anywhere
            File.WriteAllBytes(mangled, raw);
            why = null;
            try { ManifestFile.Load(mangled); }
            catch (InvalidDataException bad) { why = bad.Message; }
            checks += Check(why != null && why.IndexOf("is not valid JSON", StringComparison.Ordinal) >= 0,
                            "a byte that is not UTF-8 is REFUSED, not silently turned into U+FFFD: " + why);

            // V9: root keys are DECODED before they are compared, so an escaped spelling cannot smuggle in a
            // second "replace" that the tree and the span scanner would then disagree about.
            string twinned = Path.Combine(dir, "twinned.json");
            File.WriteAllBytes(twinned, Bytes(false,
                "{ \"id\": \"m\", \"bundle\": \"M.bundle\", \"replace\": [], \"\\u0072eplace\": [] }"));
            why = null;
            try { ManifestFile.Load(twinned); }
            catch (InvalidDataException bad) { why = bad.Message; }
            checks += Check(why != null && why.IndexOf("\"replace\" twice", StringComparison.Ordinal) >= 0,
                            "an escaped root key decodes, so it is caught as a SECOND \"replace\" (E8): " + why);
            string quoted = Path.Combine(dir, "quoted.json");
            File.WriteAllBytes(quoted, Bytes(false,
                "{ \"id\": \"m\", \"bundle\": \"M.bundle\", \"pa\\\"th\": \"x\", \"replace\": [] }"));
            checks += Check(ManifestFile.Load(quoted).Manifest.Root.ContainsKey("pa\"th"),
                            "and an escaped quote inside a KEY neither ends the key nor collides with anything");
        }
        finally { try { Directory.Delete(dir, true); } catch (Exception) { } }
        return "MANIFEST PASS, " + checks + " check(s) - atomic write";
    }

    private static string[] Temps(string dir) { return Directory.GetFiles(dir, "*.tmp"); }

    /// <summary>BOM + CRLF fixture. One "replace" row, exactly one '[' and one ']' in the whole text,
    /// pure ASCII - so a byte marker can be located independently in the before and after files.</summary>
    private const string Crlf =
        "{\r\n  \"id\": \"m.demo\",\r\n  \"bundle\": \"M.bundle\",\r\n  \"replace\": [\r\n" +
        "    { \"bundle\": \"a.bundle\", \"asset\": \"Foo\", \"texture\": \"swatch\" }\r\n" +
        "  ],\r\n  \"creature\": { \"name\": \"Spider\" }\r\n}\r\n";

    private static byte[] Bytes(bool bom, string text)
    {
        byte[] body = new UTF8Encoding(false).GetBytes(text);
        if (!bom) return body;
        byte[] all = new byte[body.Length + 3];
        all[0] = 0xEF; all[1] = 0xBB; all[2] = 0xBF;
        Buffer.BlockCopy(body, 0, all, 3, body.Length);
        return all;
    }

    private static bool Same(byte[] a, byte[] b)
    {
        if (a.Length != b.Length) return false;
        for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
        return true;
    }

    private static int Check(bool condition, string what)
    {
        if (!condition) throw new Exception("MANIFEST FAILURE: " + what);
        return 1;
    }
}
