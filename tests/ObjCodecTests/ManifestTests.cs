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

            // ---- Manifest_RefusesInvalidReplaceRows: V4, V5, V6, each with E3's wording.
            // NOT named `bad`: Task 4's `catch (InvalidDataException bad)` blocks sit in a nested scope of this
            // same try, and a later outer local of that name is CS0136.
            string[] rejects =
            {
                "{ \"bundle\": \"a.bundle\", \"asset\": \"Foo\" }",
                "{ \"bundle\": \"a.bundle\", \"asset\": \"Foo\", \"mesh\": \"m\", \"clip\": \"c\" }",
                "{ \"bundle\": \"a.bundle\", \"mesh\": \"m\" }",
                "{ \"asset\": \"Foo\", \"mesh\": \"m\" }",
                "{ \"bundle\": \"a.bundle\", \"asset\": \"Foo\", \"mesh\": { \"file\": \"m\" } }",
                "{ \"bundle\": \"a.bundle\", \"asset\": \"Foo\", \"mesh\": \"m\", \"clip\": null }"
            };
            foreach (string reject in rejects)
            {
                Manifest one = Manifest.Parse(
                    "{ \"id\": \"m\", \"bundle\": \"M.bundle\", \"replace\": [ " + reject + " ] }");
                string said = null;
                try { one.Validate(); }
                catch (InvalidDataException refused) { said = refused.Message; }
                checks += Check(said != null &&
                                said.StartsWith("\"replace\" row REFUSED: every entry needs exactly one of",
                                                StringComparison.Ordinal) &&
                                said.EndsWith("- SKIPPED, this project's other rows still bake", StringComparison.Ordinal),
                                "E3 verbatim for " + reject + " -> " + said);
            }

            // ---- Manifest_RefusesDuplicateMeshTarget: V7, bundle case-blind, asset verbatim.
            Manifest twice = Manifest.Parse(
                "{ \"id\": \"m\", \"bundle\": \"M.bundle\", \"replace\": [" +
                " { \"bundle\": \"a.bundle\", \"asset\": \"Foo\", \"mesh\": \"one\" }," +
                " { \"bundle\": \"A.BUNDLE\", \"asset\": \"Foo\", \"mesh\": \"two\" } ] }");
            string dup = null;
            try { twice.Validate(); }
            catch (InvalidDataException refused) { dup = refused.Message; }
            checks += Check(dup == "ppcontent.json already replaces \"Foo\" in \"A.BUNDLE\" with a mesh, so a second " +
                                   "row for the same target was NOT written - edit the existing row instead",
                            "E4 names the asset, the bundle and the kind: " + dup);
            Manifest apart = Manifest.Parse(
                "{ \"id\": \"m\", \"bundle\": \"M.bundle\", \"replace\": [" +
                " { \"bundle\": \"a.bundle\", \"asset\": \"Foo\", \"mesh\": \"one\" }," +
                " { \"bundle\": \"a.bundle\", \"asset\": \"foo\", \"texture\": \"two\" } ] }");
            apart.Validate();
            checks += Check(true, "a different asset CASE and a different kind are different targets - assets fold nowhere");

            // ---- Manifest_AppendsMeshWithoutCollateralRewrite: the whole point of the slice.
            string add = Path.Combine(dir, "add.json");
            byte[] originalBytes = Bytes(true, Crlf);
            File.WriteAllBytes(add, originalBytes);
            ManifestFile target = ManifestFile.Load(add);
            target.Manifest.AddMeshReplacement("b.bundle", "Torso", "torso");
            target.Save();
            byte[] afterBytes = File.ReadAllBytes(add);
            // The markers are located INDEPENDENTLY in each file, so nothing here assumes the two agree on any
            // offset. The fixture holds exactly one '[' and one ']', both belonging to "replace".
            int openWas = IndexOf(originalBytes, (byte)'['), openNow = IndexOf(afterBytes, (byte)'[');
            int closeWas = LastIndexOf(originalBytes, (byte)']'), closeNow = LastIndexOf(afterBytes, (byte)']');
            checks += Check(openWas == openNow && Same(Head(originalBytes, openWas), Head(afterBytes, openNow)),
                            "every byte BEFORE the array's '[' is identical, BOM included");
            checks += Check(Same(Tail(originalBytes, originalBytes.Length - closeWas),
                                 Tail(afterBytes, afterBytes.Length - closeNow)),
                            "and every byte from its ']' on - the \"creature\" block was not rewritten");
            byte[] wasRow = new UTF8Encoding(false).GetBytes(
                "{ \"bundle\": \"a.bundle\", \"asset\": \"Foo\", \"texture\": \"swatch\" }");
            checks += Check(afterBytes.Length > originalBytes.Length && Holds(afterBytes, wasRow),
                            "the row that was already there survives BYTE FOR BYTE - nothing reserialized it");
            string afterText = new UTF8Encoding(false).GetString(afterBytes, 3, afterBytes.Length - 3);
            checks += Check(afterText.Replace("\r\n", "").IndexOf('\n') < 0, "no bare LF was introduced - still CRLF");
            checks += Check(afterText.IndexOf("}\r\n  ]", StringComparison.Ordinal) >= 0,
                            "the author's whitespace before the ']' was COPIED, not regenerated as \"}]\"");
            ManifestFile reread = ManifestFile.Load(add);
            checks += Check(reread.Manifest.Replace.Count == 2 && reread.Manifest.Replace[1].Mesh == "torso" &&
                            reread.Manifest.Replace[1].Asset == "Torso" && reread.Manifest.Replace[1].Kind == "mesh",
                            "the added row reads back as exactly one mesh row");
            checks += Check(reread.Manifest.Root.ContainsKey("creature") && reread.Manifest.Replace[0].Texture == "swatch",
                            "and the row that was already there, plus the creature block, are what they were");

            // ---- Manifest_InsertsMissingReplaceArray (demos\CustomCreature\ppcontent.json has no "replace" at all)
            string none = Path.Combine(dir, "none.json");
            File.WriteAllBytes(none, Bytes(false,
                "{\n  \"id\": \"m.demo\",\n  \"bundle\": \"M.bundle\",\n  \"creature\": { \"name\": \"Spider\" }\n}\n"));
            ManifestFile fresh = ManifestFile.Load(none);
            fresh.Manifest.AddMeshReplacement("a.bundle", "Foo", "body");
            fresh.Save();
            ManifestFile grown = ManifestFile.Load(none);
            checks += Check(grown.Manifest.Replace.Count == 1 && grown.Manifest.Replace[0].Kind == "mesh",
                            "a manifest with no \"replace\" gets one holding exactly one valid row");
            string grownText = File.ReadAllText(none);
            checks += Check(grownText.IndexOf("\"creature\"", StringComparison.Ordinal) <
                            grownText.IndexOf("\"replace\"", StringComparison.Ordinal),
                            "added as the LAST root member, so nothing the author wrote moved");
            checks += Check(grownText.StartsWith("{\n  \"id\": \"m.demo\",\n  \"bundle\": \"M.bundle\",",
                                                 StringComparison.Ordinal),
                            "and the head of the file is byte-for-byte what it was");

            // No final newline in, NONE out: "...]}" is the ACCEPTED output. This tool inserts, it never reformats.
            string tight = Path.Combine(dir, "tight.json");
            File.WriteAllBytes(tight, Bytes(false, "{\"id\":\"m\",\"bundle\":\"M.bundle\"}"));
            ManifestFile squeezed = ManifestFile.Load(tight);
            squeezed.Manifest.AddMeshReplacement("a.bundle", "Foo", "body");
            squeezed.Save();
            string tightText = File.ReadAllText(tight);
            checks += Check(tightText.EndsWith("]}", StringComparison.Ordinal) &&
                            ManifestFile.Load(tight).Manifest.Replace.Count == 1,
                            "a file with no final newline ends \"]}\" and still re-reads: " + tightText);

            // An INLINE empty array is the whitespace-only branch: a body appears between the brackets.
            string inline = Path.Combine(dir, "inline.json");
            File.WriteAllBytes(inline, Bytes(false,
                "{\n  \"id\": \"m\",\n  \"bundle\": \"M.bundle\",\n  \"replace\": [],\n  \"tail\": 1\n}\n"));
            ManifestFile flat = ManifestFile.Load(inline);
            flat.Manifest.AddMeshReplacement("a.bundle", "Foo", "body");
            flat.Save();
            string inlineText = File.ReadAllText(inline);
            checks += Check(ManifestFile.Load(inline).Manifest.Replace.Count == 1,
                            "an inline \"[]\" takes the row: " + inlineText);
            checks += Check(inlineText.StartsWith("{\n  \"id\": \"m\",\n  \"bundle\": \"M.bundle\",",
                                                  StringComparison.Ordinal) &&
                            inlineText.EndsWith(",\n  \"tail\": 1\n}\n", StringComparison.Ordinal),
                            "and everything on either side of it is untouched");

            // The scanner's hard cases in ONE file: an escaped quote in a value, a '{' and a '[' inside a string,
            // and a NON-ASCII character before the span, so a character index is not a byte index.
            const string hard =
                "{\n  \"id\": \"m\",\n  \"bundle\": \"M.bundle\",\n  \"note\": \"café { [ \\\" ]\",\n" +
                "  \"replace\": [\n    { \"bundle\": \"a.bundle\", \"asset\": \"Fo\\\"o\", \"mesh\": \"m\" }\n  ],\n" +
                "  \"tail\": \"]\"\n}\n";
            string tricky = Path.Combine(dir, "tricky.json");
            File.WriteAllBytes(tricky, Bytes(false, hard));
            ManifestFile odd = ManifestFile.Load(tricky);
            checks += Check(odd.Manifest.Replace.Count == 1 && odd.Manifest.Replace[0].Asset == "Fo\"o",
                            "a '{', a '[' and an escaped quote inside STRINGS move neither the depth nor the span");
            odd.Manifest.AddMeshReplacement("b.bundle", "Bar", "bar");
            odd.Save();
            byte[] trickyNow = File.ReadAllBytes(tricky);
            UTF8Encoding utf8 = new UTF8Encoding(false);
            byte[] headWas = utf8.GetBytes(hard.Substring(0, hard.IndexOf("\"replace\"", StringComparison.Ordinal)));
            byte[] tailWas = utf8.GetBytes(hard.Substring(hard.IndexOf("  ],\n", StringComparison.Ordinal)));
            checks += Check(Holds(trickyNow, headWas) && Holds(trickyNow, tailWas) &&
                            Holds(trickyNow, utf8.GetBytes("\"Fo\\\"o\"")),
                            "everything before and after the array - the two-byte 'e-acute' included - is byte-identical");
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

    private static byte[] Head(byte[] all, int count)
    {
        byte[] part = new byte[count];
        Buffer.BlockCopy(all, 0, part, 0, count);
        return part;
    }

    private static byte[] Tail(byte[] all, int count)
    {
        byte[] part = new byte[count];
        Buffer.BlockCopy(all, all.Length - count, part, 0, count);
        return part;
    }

    private static int IndexOf(byte[] all, byte b)
    {
        for (int i = 0; i < all.Length; i++) if (all[i] == b) return i;
        return -1;
    }

    private static int LastIndexOf(byte[] all, byte b)
    {
        for (int i = all.Length - 1; i >= 0; i--) if (all[i] == b) return i;
        return -1;
    }

    /// <summary>Whether <paramref name="needle"/> appears in <paramref name="hay"/> unbroken.</summary>
    private static bool Holds(byte[] hay, byte[] needle)
    {
        for (int i = 0; i + needle.Length <= hay.Length; i++)
        {
            int k = 0;
            while (k < needle.Length && hay[i + k] == needle[k]) k++;
            if (k == needle.Length) return true;
        }
        return false;
    }

    private static int Check(bool condition, string what)
    {
        if (!condition) throw new Exception("MANIFEST FAILURE: " + what);
        return 1;
    }
}
