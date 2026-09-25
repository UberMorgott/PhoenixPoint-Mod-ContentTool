using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Morgott.ContentTool.Project;
using Morgott.ContentTool.Wwise;

/// <summary>
/// The project-model / packaging / audio hardening of the 2026-09-26 review, offline:
///
///   1. "publish" and "sounds" are read off Manifest's TREE, not a regex - a ']' inside a file name, an
///      escaped quote and a media ID past uint.MaxValue each broke the old read (the last one THREW);
///   2. ppcontent.json "id"/"bundle" must be ONE path component - "..\..\x" wrote outside the project;
///   3. two content projects no longer allocate their added sounds' media IDs from the same number;
///   4. a same-stem collision is caught even when another file sorts BETWEEN the two;
///   5. the packager reads both JSON files for real, never ships a bank baked from an older source,
///      names a declared sound with neither file nor bank, and never picks the assembly out of obj\;
///   6. small audio hardening: BankGen.SelfCheck's walk cannot wrap, WwisePcm reports an unreadable
///      source instead of throwing, and WwiseWem.ToWav swaps its output in whole.
///
/// ContentProject cannot join this compile list (JsonUtility, Texture2D), so its members are exercised on
/// the SHIPPED ContentTool.dll by reflection - the arrangement RefusalCount uses.
/// </summary>
internal static class ProjectAudioChecks
{
    private static int checks;
    private static Type project;

    private const string Meta =
        "{ \"ID\": \"com.test.Mod\", \"Dependencies\": [ \"com.morgott.ContentTool\" ], \"AssemblyName\": \"\" }";

    internal static string Run()
    {
        string dll = Path.Combine(AppDomain.CurrentDomain.BaseDirectory,
            @"..\..\..\..\..\bin\Release\ContentTool\ContentTool.dll");
        if (!System.IO.File.Exists(dll))
            throw new Exception("PROJECT-AUDIO FAILURE: no " + Path.GetFullPath(dll) +
                                " - run `dotnet build -c Release` before this suite");
        project = Assembly.LoadFrom(dll).GetType("Morgott.ContentTool.Project.ContentProject", true);

        string root = Path.Combine(Path.GetTempPath(), "ct-projectaudio-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(root);
            Readers();
            Names();
            MediaBase();
            Stems(root);
            PackageJson(root);
            Stale(root);
            Assembly_(root);
            Audio(root);
            return "PROJECT-AUDIO PASS, " + checks + " check(s)";
        }
        finally { try { Directory.Delete(root, true); } catch (Exception) { } }
    }

    // ---- 1
    private static void Readers()
    {
        var refusals = new List<string>();
        IList sounds = (IList)Call("ParseSounds",
            "{\"sounds\":[{\"media\":123,\"file\":\"odd]name.mp3\"},{\"media\":\"456\",\"file\":\"say \\\"hi\\\".mp3\"}]}",
            refusals);
        Check(sounds.Count == 2 && refusals.Count == 0 &&
              FileOf(sounds[0]) == "odd]name.mp3" && FileOf(sounds[1]) == "say \"hi\".mp3",
              "a ']' inside a file name and an escaped quote both read - the regex stopped at the first ']' " +
              "and kept the backslash: " + (sounds.Count > 0 ? FileOf(sounds[0]) : "(none)"));

        refusals.Clear();
        sounds = (IList)Call("ParseSounds",
            "{\"sounds\":[{\"media\":99999999999,\"file\":\"big.mp3\"},{\"media\":1.5,\"file\":\"half.mp3\"}," +
            "{\"media\":7,\"file\":\"ok.mp3\"}]}", refusals);
        Check(sounds.Count == 1 && refusals.Count == 2 &&
              refusals[0].StartsWith("\"sounds\" row REFUSED", StringComparison.Ordinal),
              "a media past uint.MaxValue and a fraction are REFUSED rows, not an OverflowException out of " +
              "the whole reader, and the whole row beside them still reads: " + string.Join(" | ", refusals.ToArray()));

        refusals.Clear();
        IList keys = (IList)Call("ParsePublish",
            "{\"publish\":[{\"key\":\"a/b]\",\"asset\":\"textures/x\",\"opts\":{\"n\":1}},5]}", refusals);
        Check(keys.Count == 1 && refusals.Count == 1 && refusals[0].IndexOf("got 5", StringComparison.Ordinal) > 0,
              "a publish row with a ']' in its key and a NESTED member reads, and a primitive element is " +
              "refused ALONE: " + string.Join(" | ", refusals.ToArray()));

        // A wrong-shaped "replace" is replace's refusal: the publish rows beside it still read.
        refusals.Clear();
        keys = (IList)Call("ParsePublish",
            "{\"replace\":5,\"publish\":[{\"key\":\"a/b\",\"asset\":\"textures/x\"}]}", refusals);
        Check(keys.Count == 1 && refusals.Count == 0,
              "a wrong-shaped \"replace\" does not take \"publish\" down with it");

        List<string> said = new List<string>();
        List<KeyValuePair<uint, string>> rows = Manifest.Sounds(
            Manifest.Tree("{\"sounds\":{\"media\":1}}", "ppcontent.json"), said);
        Check(rows.Count == 0 && said.Count == 1,
              "a \"sounds\" VALUE that is not an array is one refused row, not silence: " + string.Join(" | ", said.ToArray()));
    }

    // ---- 2
    private static void Names()
    {
        foreach (string bad in new[] { "..", ".", "..\\..\\x", "a/b", "C:x", "x.", " x", "a\"b", "" })
            Check(Manifest.UnsafeName("id", bad) != null, "'" + bad + "' is refused as an id");
        foreach (string good in new[] { "morgott.sample", "Author_Mod-2", "MyMod.bundle", "мод" })
            Check(Manifest.UnsafeName("bundle", good) == null, "'" + good + "' is a plain name");
        Check(Manifest.UnsafeName("id", "..\\x").IndexOf("\"id\"", StringComparison.Ordinal) >= 0,
              "and the refusal names the key");
    }

    // ---- 3
    private static void MediaBase()
    {
        uint a = (uint)Call1("MediaIdBase", "morgott.one"), b = (uint)Call1("MediaIdBase", "morgott.two");
        Check(a != b && (uint)Call1("MediaIdBase", "morgott.one") == a,
              "two projects start their media IDs apart (" + a + " vs " + b + "), and one project always " +
              "starts at the same one");
        Check(a != 0xC7000100 && b != 0xC7000100, "and neither is the old shared constant");
    }

    // ---- 4
    private static void Stems(string root)
    {
        string textures = Path.Combine(root, "stems", "Content", "Textures");
        Directory.CreateDirectory(textures);
        foreach (string f in new[] { "swatch.jpg", "swatch.old.png", "swatch.png", "other.png" })
            System.IO.File.WriteAllBytes(Path.Combine(textures, f), new byte[] { 1 });
        var dropped = new List<string>();
        string[] kept = ContentMods.Sources(Path.Combine(root, "stems"), "Textures", dropped,
                                            ContentMods.TexturePatterns);
        Check(kept.Length == 2 && dropped.Count == 1 && dropped[0].IndexOf("swatch.jpg", StringComparison.Ordinal) > 0 &&
              Array.Exists(kept, k => k.EndsWith("swatch.old.png", StringComparison.Ordinal)),
              "swatch.jpg + swatch.png collide even with swatch.old.png sorted between them; the others " +
              "stay -> kept " + kept.Length + ", " + string.Join(" | ", dropped.ToArray()));
    }

    // ---- 5a
    private static void PackageJson(string root)
    {
        Check(Refused(root, "comma", "{ \"id\": \"a.b\", \"bundle\": \"a.bundle\", \"weapons\": [ { \"id\": \"x\" }, ] }")
                  .IndexOf("NOT VALID JSON", StringComparison.Ordinal) >= 0,
              "a trailing comma - balanced braces, invalid JSON - is refused; the brace count passed it");
        Check(Refused(root, "escape", "{ \"id\": \"..\\\\..\\\\x\", \"bundle\": \"a.bundle\", \"weapons\": [ { \"id\": \"x\" } ] }")
                  .IndexOf("\"id\"", StringComparison.Ordinal) >= 0,
              "an id that climbs out of its folder is refused before anything ships");
        Check(Package.MetaRefusal("{\"ID\":\"x\",\"Dependencies\":[\"com.morgott.ContentTool\"", null)
                  .IndexOf("did not read as JSON", StringComparison.Ordinal) >= 0,
              "a torn meta.json holding both right substrings is refused by the PACKAGER too, not only R13");
        Check(Package.MetaRefusal("{\"id\":\"x\",\"dependencies\":[\"com.morgott.ContentTool\"]}", null) == null,
              "while lowercase keys pass - the game's JsonConvert binds them case-blind");
        Check(!Package.Ships(null, "{ \"notes\": \"\\\"weapons\\\": [1]\" }") &&
              Package.Ships(null, "{ \"weapons\": [ { \"id\": \"x\" } ] }") &&
              !Package.Ships(null, "{ \"weapons\": [] }"),
              "a rung spelled inside a STRING is no payload; a real non-empty one is; an empty one is not");
    }

    // ---- 5b
    private static void Stale(string root)
    {
        // Baked, ledger agrees: packages, and the source is left behind.
        string p = Sounded(root, "fresh", "{ \"media\": 123, \"file\": \"hit.wav\" }", "hit.wav", true);
        string banks = Path.Combine(p, "Dist", "Sounds");
        Package.WriteLedger(banks, new Dictionary<uint, string[]>
        {
            { 123, new[] { Package.Sha1Of(Path.Combine(p, "Content", "Audio", "Replace", "hit.wav")), "hit.wav" } }
        });
        Check(Said(p, root, "fresh") == null, "a source whose bytes match the ledger packages");

        // Same project, the source edited after the bake: the bank ships the OLD sound.
        System.IO.File.WriteAllText(Path.Combine(p, "Content", "Audio", "Replace", "hit.wav"), "edited");
        string said = Said(p, root, "fresh2");
        Check(said != null && said.IndexOf("CHANGED SINCE IT WAS BAKED", StringComparison.Ordinal) >= 0 &&
              said.IndexOf("hit.wav", StringComparison.Ordinal) >= 0,
              "a source changed after its bake is refused by name: " + (said ?? "it PACKAGED"));

        // No ledger (an older bake, or a demo that ships none): only the dates speak, and a date is
        // what every unzip and sync rewrites - so it WARNS, and never refuses.
        p = Sounded(root, "dated", null, "123.wav", true);
        string src = Path.Combine(p, "Content", "Audio", "Replace", "123.wav");
        System.IO.File.SetLastWriteTimeUtc(src, DateTime.UtcNow.AddHours(-1));
        bool ok;
        said = Package.Run(p, Path.Combine(root, "dated-out"), null, out ok);
        Check(ok && said.IndexOf("WARN", StringComparison.Ordinal) < 0,
              "with no ledger a source OLDER than its bank packages without a word: " + said);
        System.IO.File.SetLastWriteTimeUtc(src, DateTime.UtcNow.AddHours(1));
        said = Package.Run(p, Path.Combine(root, "dated2-out"), null, out ok);
        Check(ok && said.IndexOf("WARN: " + Path.Combine("Content", "Audio", "Replace", "123.wav"), StringComparison.Ordinal) >= 0 &&
              said.IndexOf("CHANGED SINCE IT WAS BAKED", StringComparison.Ordinal) < 0,
              "and one written AFTER it still packages, with a WARN line naming it: " + said);

        // A declared row whose file is gone: fine with a bank, dead without one.
        p = Sounded(root, "gone", "{ \"media\": 123, \"file\": \"gone.wav\" }", null, true);
        Check(Said(p, root, "gone") == null, "a declared sound with no source but a bank packages");
        System.IO.File.Delete(Path.Combine(p, "Dist", "Sounds", "123.bnk"));
        said = Said(p, root, "gone2");
        Check(said != null && said.IndexOf("NEVER BAKED", StringComparison.Ordinal) >= 0 &&
              said.IndexOf("gone.wav", StringComparison.Ordinal) >= 0,
              "and with neither file nor bank it is NEVER BAKED, by name: " + (said ?? "it PACKAGED"));
    }

    // ---- 5c
    private static void Assembly_(string root)
    {
        string p = Path.Combine(root, "asm");
        Directory.CreateDirectory(p);
        System.IO.File.WriteAllText(Path.Combine(p, "meta.json"),
            "{ \"ID\": \"a.b\", \"AssemblyName\": \"X.dll\", \"Dependencies\": [ \"com.morgott.ContentTool\" ] }");
        string refAsm = Path.Combine(p, "obj", "Release", "net472", "ref", "X.dll");
        Directory.CreateDirectory(Path.GetDirectoryName(refAsm));
        System.IO.File.WriteAllBytes(refAsm, new byte[4]);
        Check(Package.BuiltAssembly(p) == null, "a reference assembly under obj\\ is never the shipped one");

        string release = Path.Combine(p, "bin", "Release", "net472", "X.dll");
        string debug = Path.Combine(p, "bin", "Debug", "net472", "X.dll");
        Directory.CreateDirectory(Path.GetDirectoryName(release));
        Directory.CreateDirectory(Path.GetDirectoryName(debug));
        System.IO.File.WriteAllBytes(release, new byte[4]);
        System.IO.File.WriteAllBytes(debug, new byte[4]);
        System.IO.File.SetLastWriteTimeUtc(release, DateTime.UtcNow.AddHours(-2));
        System.IO.File.SetLastWriteTimeUtc(refAsm, DateTime.UtcNow);
        Check(Package.BuiltAssembly(p) == release,
              "bin\\Release wins over a NEWER Debug build and a newer obj\\ copy -> " + Package.BuiltAssembly(p));
    }

    // ---- 6
    private static void Audio(string root)
    {
        byte[] bank = BankGen.BuildMediaOnly(1, 2, new byte[16]);
        // DIDX's size field (BKHD is 8 + 20 bytes, so DIDX's header starts at 28), turned into one that
        // runs 4 GB past the end.
        Check(System.Text.Encoding.ASCII.GetString(bank, 28, 4) == "DIDX", "the fixture points at DIDX");
        bank[28 + 4] = 0xF0; bank[28 + 5] = 0xFF; bank[28 + 6] = 0xFF; bank[28 + 7] = 0xFF;
        string flaw = BankGen.SelfCheck(bank);
        Check(flaw != null && flaw.IndexOf("past the end", StringComparison.Ordinal) >= 0,
              "a chunk size near 4 GB is reported as running past the end, not wrapped: " + flaw);

        string dirWav = Path.Combine(root, "folder.wav");
        Directory.CreateDirectory(dirWav);
        string why;
        WwisePcm.Wav w = WwisePcm.ReadAudio(dirWav, out why);
        Check(w == null && why != null, "a source the OS will not open is a reason, not a throw: " + why);

        byte[] wem = WwisePcm.BuildWem(new byte[400], 1, 44100);
        string wav = Path.Combine(root, "out.wav");
        System.IO.File.WriteAllText(wav, "previous");
        Check(WwiseWem.ToWav(wem, wav) == null && new FileInfo(wav).Length > 400 &&
              Directory.GetFiles(root, "out.wav.*.tmp").Length == 0,
              "ToWav REPLACES an existing .wav through a sibling temp and leaves no temp behind");
    }

    // ---------------------------------------------------------------- helpers

    private static string Refused(string root, string name, string manifest)
    {
        string p = Path.Combine(root, name);
        Directory.CreateDirectory(p);
        System.IO.File.WriteAllText(Path.Combine(p, "meta.json"), Meta);
        System.IO.File.WriteAllText(Path.Combine(p, "ppcontent.json"), manifest);
        return Said(p, root, name) ?? "PACKAGED";
    }

    /// <summary>A project with one replacement source (or none) and, when asked, its bank.</summary>
    private static string Sounded(string root, string name, string row, string source, bool bank)
    {
        string p = Path.Combine(root, name);
        string replace = Path.Combine(p, "Content", "Audio", "Replace");
        string banks = Path.Combine(p, "Dist", "Sounds");
        Directory.CreateDirectory(replace);
        Directory.CreateDirectory(banks);
        System.IO.File.WriteAllText(Path.Combine(p, "meta.json"), Meta);
        System.IO.File.WriteAllText(Path.Combine(p, "ppcontent.json"),
            "{ \"id\": \"a.b\", \"bundle\": \"a.bundle\"" + (row == null ? "" : ", \"sounds\": [ " + row + " ]") + " }");
        if (source != null) System.IO.File.WriteAllText(Path.Combine(replace, source), "pcm");
        if (bank) System.IO.File.WriteAllBytes(Path.Combine(banks, "123.bnk"), new byte[16]);
        return p;
    }

    private static string Said(string project, string root, string outName)
    {
        bool ok;
        string said = Package.Run(project, Path.Combine(root, outName + "-out"), null, out ok);
        return ok ? null : said;
    }

    private static string FileOf(object soundEntry)
    {
        return (string)soundEntry.GetType().GetField("File", BindingFlags.Instance | BindingFlags.NonPublic |
                                                              BindingFlags.Public).GetValue(soundEntry);
    }

    private static object Call(string method, string json, List<string> refusals)
    {
        MethodInfo m = project.GetMethod(method, BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
        if (m == null) throw new Exception("PROJECT-AUDIO FAILURE: ContentProject has no " + method);
        try { return m.Invoke(null, new object[] { json, refusals }); }
        catch (TargetInvocationException e) { throw e.InnerException; }
    }

    private static object Call1(string method, string arg)
    {
        MethodInfo m = project.GetMethod(method, BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
        if (m == null) throw new Exception("PROJECT-AUDIO FAILURE: ContentProject has no " + method);
        return m.Invoke(null, new object[] { arg });
    }

    private static void Check(bool ok, string what)
    {
        checks++;
        if (!ok) throw new Exception("PROJECT-AUDIO FAILURE: " + what);
    }
}
