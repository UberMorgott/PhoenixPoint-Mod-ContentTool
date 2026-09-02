using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

/// <summary>
/// A REFUSAL THAT NOBODY COUNTS BECOMES "ALL PASS", and Route7.cs:249 reads ALL PASS as permission to
/// stamp the patch cache current - so declared work that did not happen is reported to the modder as a
/// finished bake. Three ways ppcontent.json used to do exactly that, all of them fixed in
/// <c>ContentProject</c>:
///
///   1. an unreadable or unsupported SOURCE was added to SourceRefusals and never counted, so only an
///      importer that THREW reached the failure total;
///   4. one incomplete "replace" / "sounds" / "publish" object threw out of its array parser, so a
///      half-typed row - even a "publish" one, which no texture bake reads - ended the run as
///      "ct_project THREW", with no summary and no line naming the row;
///   6. "scale": -1 threw InvalidDataException straight out of Load.
///
/// WHY REFLECTION AND NOT A DIRECT CALL: ContentProject decodes textures through Texture2D and reads
/// its manifest through JsonUtility, and JsonUtility is an ECall into the player - calling it outside a
/// Unity runtime throws SecurityException ("ECall methods must be packaged into a system module"),
/// MEASURED. So the file cannot join this project's compile list, and the members below are exercised
/// on the REAL shipped ContentTool.dll instead. What that leaves unproven offline is the one line
/// `p.ImportFailures = p.SourceRefusals.Count` at the end of Load - it needs a game - so this arm
/// proves the other half: every refusal above lands in the ONE list that line counts.
/// </summary>
internal static class RefusalCount
{
    private static Type project;

    internal static string Run()
    {
        string dll = Path.Combine(AppDomain.CurrentDomain.BaseDirectory,
            @"..\..\..\..\..\bin\Release\ContentTool\ContentTool.dll");
        if (!File.Exists(dll))
            throw new Exception("REFUSAL-COUNT FAILURE: no " + Path.GetFullPath(dll) +
                                " - run `dotnet build -c Release` before this suite");
        project = Assembly.LoadFrom(dll).GetType("Morgott.ContentTool.Project.ContentProject", true);

        // ONE list, the way Load keeps one: what this arm counts at the end is what Load counts.
        var refusals = new List<string>();

        // ---- 6: a negative "scale" is a refused ROW, not the end of the bake
        float scale = (float)Call("ScaleOrRefuse", -1f, refusals);
        int checks = Check(scale == 1f && refusals.Count == 1,
            "\"scale\": -1 no longer throws out of Load - it clamps to 1 and refuses by name: " +
            (refusals.Count == 1 ? refusals[0] : "nothing was reported at all"));
        checks += Check(refusals[0].IndexOf("scale", StringComparison.Ordinal) >= 0 &&
                        refusals[0].IndexOf("SKIPPED", StringComparison.Ordinal) >= 0,
            "and the refusal names the key AND says what the bake did instead: " + refusals[0]);
        checks += Check((float)Call("ScaleOrRefuse", 0.005f, refusals) == 0.005f &&
                        (float)Call("ScaleOrRefuse", 0f, refusals) == 1f && refusals.Count == 1,
            "a declared scale still arrives, an absent one is still 1, and neither is refused");

        // ---- 4: one incomplete object per array, and the array's OTHER rows still parse
        checks += Check(Rows("ParseReplace",
            "{\"replace\":[{\"bundle\":\"a.bundle\",\"asset\":\"Foo\",\"texture\":\"swatch\"}," +
            "{\"bundle\":\"a.bundle\"}]}", refusals) == 1 && refusals.Count == 2,
            "an incomplete \"replace\" row is refused and the complete one beside it still bakes");
        checks += Check(Rows("ParseSounds",
            "{\"sounds\":[{\"media\":123,\"file\":\"hit.mp3\"},{\"file\":\"no-media.mp3\"}]}",
            refusals) == 1 && refusals.Count == 3,
            "the same for \"sounds\"");
        checks += Check(Rows("ParsePublish",
            "{\"publish\":[{\"key\":\"a/b\",\"asset\":\"textures/x\"},{\"key\":\"c/d\"}]}",
            refusals) == 1 && refusals.Count == 4,
            "and for \"publish\" - the kind that has nothing to do with a texture bake and used to end it");
        checks += Check(refusals[1].StartsWith("\"replace\" row REFUSED", StringComparison.Ordinal) &&
                        refusals[2].StartsWith("\"sounds\" row REFUSED", StringComparison.Ordinal) &&
                        refusals[3].StartsWith("\"publish\" row REFUSED", StringComparison.Ordinal),
            "each refusal names WHICH array the row came from: " + refusals[3]);

        // ---- the TREE reader, on the shipped DLL: three shapes the regex read wrong, and the sentence.
        checks += Check(Rows("ParseReplace",
            "{\"replace\":[{\"bundle\":\"a.bundle\",\"asset\":\"Foo\",\"mesh\":\"body\"," +
            "\"opts\":{\"x\":1}},{\"bundle\":\"b.bundle\",\"asset\":\"Bar]\",\"texture\":\"t\"}]}",
            refusals) == 2 && refusals.Count == 4,
            "a NESTED map in a row and a ']' inside a string leave BOTH rows readable, and refuse neither");
        checks += Check(Threw("ParseReplace", "{\"replace\":[{\"bundle\":\"a.bundle\"}]}"),
            "an incomplete row with no list to collect into still THROWS, exactly as before");
        checks += Check(Said("ParseReplace", "{\"replace\":[]}") ==
                        "ppcontent.json declares \"replace\" but no complete entry was read from it",
            "and a declared-but-empty array throws THAT sentence, word for word");
        checks += Check((Said("ParseReplace", "{\"replace\":[1]}") ?? "")
                            .IndexOf("ARRAY OF ROWS", StringComparison.Ordinal) >= 0,
            "a \"replace\" holding a primitive is a manifest this cannot read - with no list it THROWS, " +
            "it does not report an empty project");

        checks += Check(Threw("ParsePublish", "{\"publish\":[{\"key\":\"c/d\"}]}"),
            "with no list to collect into (LoadDeclared, SoundReplace's S1 gate) the throw is unchanged");

        // ---- 1: a source the tool never accepts is a refusal too, in the SAME list
        string dir = Path.Combine(Path.GetTempPath(), "ct-refusalcount-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "hit.flac"), "x");
            string said = (string)project.GetMethod("RefuseUnsupported",
                BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)
                .Invoke(null, new object[] { dir });
            refusals.Add(said);
            checks += Check(said != null && said.IndexOf("hit.flac", StringComparison.Ordinal) >= 0,
                "an unsupported source is named: " + (said ?? "nothing was reported at all"));
        }
        finally { try { Directory.Delete(dir, true); } catch { } }

        checks += Check(refusals.Count == 5,
            "and ALL FIVE refusals sit in the one list Load counts (ImportFailures = SourceRefusals.Count), " +
            "so none of them can reach the modder as ALL PASS: " + refusals.Count);

        return "REFUSAL-COUNT PASS, " + checks + " check(s) - 5 refusals, 5 failures";
    }

    /// <summary>How many rows the parser KEPT, with its refusals collected instead of thrown.</summary>
    private static int Rows(string method, string json, List<string> refusals)
    {
        return ((ICollection)Call(method, json, refusals)).Count;
    }

    private static bool Threw(string method, string json)
    {
        try { Call(method, json, null); return false; }
        catch (InvalidDataException) { return true; }
    }

    /// <summary>The sentence the parser THREW with no list to collect into, or null if it did not.</summary>
    private static string Said(string method, string json)
    {
        try { Call(method, json, null); return null; }
        catch (InvalidDataException refused) { return refused.Message; }
    }

    private static object Call(string method, object first, List<string> refusals)
    {
        MethodInfo m = project.GetMethod(method,
            BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
        if (m == null) throw new Exception("REFUSAL-COUNT FAILURE: ContentProject has no " + method);
        try { return m.Invoke(null, new object[] { first, refusals }); }
        catch (TargetInvocationException e) { throw e.InnerException; }
    }

    private static int Check(bool condition, string what)
    {
        if (!condition) throw new Exception("REFUSAL-COUNT FAILURE: " + what);
        return 1;
    }
}
