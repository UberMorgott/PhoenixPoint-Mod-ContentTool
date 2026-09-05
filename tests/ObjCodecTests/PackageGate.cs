using System;
using System.Collections.Generic;
using System.IO;
using Morgott.ContentTool.Project;

/// <summary>
/// A DECLARED PIECE OF WORK THAT DID NOT HAPPEN MUST NOT PACKAGE AS SUCCESS. Two ways it used to:
///
///   1. ppcontent.json was only ever REGEX-SCRAPED, never checked. A zero-byte or half-typed manifest
///      matched nothing, declared nothing, and packaged as PACKAGED - a mod that installs and does
///      nothing, with the runtime reader refusing it hours later on the player's machine.
///   2. BakedAlready filtered by the .wav/.ogg/.mp3 whitelist BEFORE it looked at the manifest, so a
///      "sounds" row naming hit.flac never reached the NEVER BAKED refusal - the one case where the
///      author explicitly said this file is a replacement.
///
/// <c>Package</c> carries no UnityEngine type on purpose (its own remark: tools\Package compiles it
/// alone), so what may ship is proven here rather than by uploading a dead mod.
/// </summary>
internal static class PackageGate
{
    /// <summary>Shaped exactly like ProjectScaffold.CreateNew's leftover: Guid.ToString("N") + ".tmp".</summary>
    private static readonly string OwnTemp = Guid.NewGuid().ToString("N") + ".tmp";

    /// <summary>The tool's OTHER temp shape: the SIBLING temp every atomic write streams beside the file
    /// it will replace - '&lt;the real name&gt;.&lt;guid&gt;.tmp' (AtomicFile.Publish:56). A bake that threw
    /// mid-serialization leaves one of these, full size, in the author's own Dist\.</summary>
    private static readonly string SiblingTemp = "x.bundle." + Guid.NewGuid().ToString("N") + ".tmp";

    private const string Meta =
        "{ \"ID\": \"com.test.Mod\", \"Dependencies\": [ \"com.morgott.ContentTool\" ], \"AssemblyName\": \"\" }";

    internal static string Run()
    {
        string root = Path.Combine(Path.GetTempPath(), "ct-packagegate-" + Guid.NewGuid().ToString("N"));
        try
        {
            int checks = Check(Refusal(root, "broken", "{ \"publish\": [ { \"name\": \"a\" ,,, BROKEN", null)
                                   .IndexOf("NOT VALID JSON", StringComparison.Ordinal) >= 0,
                "a brace-unbalanced ppcontent.json is refused, not packaged");
            checks += Check(Refusal(root, "empty", "", null)
                                .IndexOf("EMPTY OR NOT VALID JSON", StringComparison.Ordinal) >= 0,
                "a zero-byte ppcontent.json is refused too");
            checks += Check(Refusal(root, "good", "{ \"publish\": [ { \"name\": \"a\" } ] }", null) == null,
                "a valid manifest still packages");

            // The declared row the whitelist used to hide: an extension the guesser skips, a media
            // that has no Dist\Sounds\123.bnk beside it.
            string said = Refusal(root, "flac", "{ \"sounds\": [ { \"file\": \"hit.flac\", \"media\": 123 } ] }",
                                  "hit.flac");
            checks += Check(said != null && said.IndexOf("NEVER BAKED", StringComparison.Ordinal) >= 0,
                "a declared sound outside the .wav/.ogg/.mp3 whitelist still reaches the NEVER BAKED " +
                "refusal: " + (said ?? "it PACKAGED"));
            checks += Check(said.IndexOf("hit.flac", StringComparison.Ordinal) >= 0,
                "and the refusal names the file");
            // The stray file nobody declared keeps its old silence.
            checks += Check(Refusal(root, "stray", "{ \"publish\": [ { \"name\": \"a\" } ] }", "notes.txt") == null,
                "an UNDECLARED file of an unknown extension is still not this rule's business");

            // A PRESS KILLED MID-WRITE LEAVES A <guid>.tmp UNDER Content\, and Content\ is copied
            // verbatim - so that half-written byte string used to ship inside the release zip. Only
            // THIS tool's own GUID name is dropped; an author's own .tmp is their file and still ships.
            checks += Check(Refusal(root, "leftover", "{ \"publish\": [ { \"name\": \"a\" } ] }", null) == null &&
                            !File.Exists(Path.Combine(root, "leftover-out", "Content", OwnTemp)) &&
                            File.Exists(Path.Combine(root, "leftover-out", "Content", "artist-recovery.tmp")),
                "a killed press's <guid>.tmp is not staged into the package, an author's own .tmp is");
            // AND THE SIBLING SHAPE, which is the one a THROWN BAKE leaves: 'MyMod.bundle.<guid>.tmp' in
            // Dist\, hundreds of megabytes of it, staged verbatim by the same CopyDir.
            checks += Check(!File.Exists(Path.Combine(root, "leftover-out", "Content", SiblingTemp)),
                "a thrown bake's '<name>.bundle.<guid>.tmp' is not staged either - the GUID is the " +
                "signature wherever it sits in the name");

            return "PACKAGE-GATE PASS, " + checks + " check(s)";
        }
        finally
        {
            try { if (Directory.Exists(root)) Directory.Delete(root, true); } catch { }
        }
    }

    /// <summary>Stages one throwaway project and answers the refusal text, or null when it packaged.
    /// <paramref name="sound"/> is a file dropped in Content\Audio\Replace\ with no bank anywhere.</summary>
    private static string Refusal(string root, string name, string manifest, string sound)
    {
        string project = Path.Combine(root, name);
        Directory.CreateDirectory(Path.Combine(project, "Content"));
        File.WriteAllText(Path.Combine(project, "meta.json"), Meta);
        File.WriteAllText(Path.Combine(project, "ppcontent.json"), manifest);
        File.WriteAllText(Path.Combine(project, "Content", "a.png"), "x");
        File.WriteAllText(Path.Combine(project, "Content", OwnTemp), "x");
        File.WriteAllText(Path.Combine(project, "Content", "artist-recovery.tmp"), "x");
        File.WriteAllText(Path.Combine(project, "Content", SiblingTemp), "x");
        if (sound != null)
        {
            string replace = Path.Combine(project, "Content\\Audio\\Replace");
            Directory.CreateDirectory(replace);
            File.WriteAllText(Path.Combine(replace, sound), "x");
        }

        bool ok;
        string said = Package.Run(project, Path.Combine(root, name + "-out"), null, out ok);
        return ok ? null : said;
    }

    private static int Check(bool condition, string what)
    {
        if (!condition) throw new Exception("PACKAGE-GATE FAILURE: " + what);
        return 1;
    }
}
