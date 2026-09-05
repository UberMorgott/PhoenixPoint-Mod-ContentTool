using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Morgott.ContentTool.Import;
using Morgott.ContentTool.Project;

/// <summary>The "Replace one mesh" wizard's DISK half: a project folder beside Mods\ContentTool\ that the mod
/// manager can discover, one "replace" row per press, the .glb copied under Content\Meshes\ and never
/// overwritten, and its alias sidecar keyed on the COPY. Every arm here is a way one press could quietly
/// destroy an author's work - an overwritten mesh, a lost row, a project the manager cannot see.</summary>
internal static class ProjectScaffoldTests
{
    internal static string Run()
    {
        int checks = 0;
        string dir = Path.Combine(Path.GetTempPath(), "ct_scaffold_" + Guid.NewGuid().ToString("N"));
        string mods = Path.Combine(dir, "Mods");
        string modDir = Path.Combine(mods, "ContentTool");
        Directory.CreateDirectory(modDir);
        var empty = new Dictionary<string, string>(StringComparer.Ordinal);
        try
        {
            // ---- Scaffold_NameTable (R1). The folder is created BESIDE the player's other mods, so a name
            // that walks out of Mods\ is not a validation nicety - it is a write anywhere on the disk.
            checks += Check(ProjectScaffold.NameRefusal("Replace_Rifle") == null,
                            "an ordinary name is accepted");
            string[] bad =
            {
                "", new string('a', 65), "..", "a\\b", "a/b", "C:\\x", "CON", "nul.glb", "-lead", "trail.",
                "trail "
            };
            foreach (string name in bad)
            {
                string said = ProjectScaffold.NameRefusal(name);
                checks += Check(said != null &&
                                said.StartsWith("project name REFUSED: '" + name + "'", StringComparison.Ordinal) &&
                                said.EndsWith("no path separators, no device names", StringComparison.Ordinal),
                                "R1 verbatim for '" + name + "' -> " + said);
            }
            checks += Check(Directory.GetDirectories(mods).Length == 1,
                            "and not one of them created a folder - Mods still holds only ContentTool");

            // ---- Scaffold_DefaultName: what the panel puts in the field before the author types.
            checks += Check(ProjectScaffold.DefaultName("WPN_PX_RG_Assault_Rifle_T01_V01") ==
                            "Replace_WPN_PX_RG_Assault_Rifle_T01_V01",
                            "the default name is Replace_ plus the shipped asset");
            checks += Check(ProjectScaffold.DefaultName("A B/C") == "Replace_A_B_C",
                            "anything the name table would refuse becomes '_'");
            string longName = ProjectScaffold.DefaultName(new string('x', 200));
            checks += Check(longName.Length == 64 && ProjectScaffold.NameRefusal(longName) == null,
                            "and a long asset name is cut to a name the table accepts: " + longName.Length);

            // ---- Scaffold_NormalizesModDir. Directory.GetParent("...\Mods\ContentTool\") answers
            // "...\Mods\ContentTool", so a trailing separator on ModDir would bury the project UNDER
            // ContentTool - where the manager never discovers it (ModGate.Decide:38 -> Unknown) - and the
            // post-condition would accept it, because ContentMods.ProjectDir walks the same wrong parent.
            checks += Check(ProjectScaffold.RootOf(modDir + Path.DirectorySeparatorChar, "Replace_Rifle") ==
                            ProjectScaffold.RootOf(modDir, "Replace_Rifle"),
                            "RootOf ignores a trailing separator on ModDir: " +
                            ProjectScaffold.RootOf(modDir + Path.DirectorySeparatorChar, "Replace_Rifle"));

            // ---- Scaffold_CreatesProjectTemplates
            string glb = Path.Combine(dir, "body.glb");
            File.WriteAllBytes(glb, new byte[] { 1, 2, 3 });
            string sha = AliasMap.Sha256(File.ReadAllBytes(glb));
            ProjectScaffold.Result made = ProjectScaffold.AddMeshReplacement(
                modDir, "Replace_Rifle", glb, sha,
                "px_equipment_assets_all.bundle", "WPN_PX_RG_Assault_Rifle_T01_V01", empty);
            checks += Check(made.Created && made.Root == Path.Combine(mods, "Replace_Rifle"),
                            "the project is the SIBLING Mods\\<name>, never a folder under ContentTool: " + made.Root);
            checks += Check(File.Exists(made.ManifestPath) && File.Exists(made.MetaPath),
                            "both templates are on disk");
            Manifest fresh = Manifest.Parse(File.ReadAllText(made.ManifestPath));
            checks += Check(fresh.Id == "Replace_Rifle" && fresh.Bundle == "Replace_Rifle.bundle",
                            "the manifest declares id and bundle: " + fresh.Id + " / " + fresh.Bundle);
            checks += Check(File.ReadAllText(made.MetaPath) == Template("Replace_Rifle"),
                            "meta.json is the design §4.2 template, byte for byte");
            checks += Check(ContentMods.ProjectDir(modDir, "Replace_Rifle") == made.Root,
                            "and ContentMods.ProjectDir now resolves that name to it - ct_project <name> finds it");

            ProjectScaffold.Result trailing = ProjectScaffold.AddMeshReplacement(
                modDir + Path.DirectorySeparatorChar, "Trail_Sep", glb, sha, "a.bundle", "Foo", empty);
            checks += Check(trailing.Root == Path.Combine(mods, "Trail_Sep"),
                            "a press made through the trailing-separator spelling lands BESIDE ContentTool, " +
                            "not under it: " + trailing.Root);

            // ---- Scaffold_KeepsAnAuthoredId. ID == folder name is true of a project THIS tool made and of
            // nothing else: an authored ppcontent.json keeps whatever "id" its author chose, and the
            // meta.json written beside it has to key the mod on THAT, or the manager lists one id while
            // every route resolves another.
            string authored = Path.Combine(mods, "Authored");
            Directory.CreateDirectory(authored);
            File.WriteAllText(Path.Combine(authored, "ppcontent.json"),
                              "{\n  \"id\": \"com.someone.hand.written\",\n  \"bundle\": \"theirs.bundle\"\n}\n");
            ProjectScaffold.Result joined = ProjectScaffold.AddMeshReplacement(
                modDir, "Authored", glb, sha, "a.bundle", "Foo", empty);
            checks += Check(!joined.Created && File.ReadAllText(joined.MetaPath) ==
                            Template("com.someone.hand.written"),
                            "the generated meta.json carries the MANIFEST's id, not the folder name");
            checks += Check(Manifest.Parse(File.ReadAllText(joined.ManifestPath)).Bundle == "theirs.bundle",
                            "and the authored id/bundle are not rewritten");

            // ---- Scaffold_RefusesAnUnshippableMeta (R13). Reachable only for a folder that IS a project
            // already (anything else is R2), and validated by the PACKAGER'S own validator, so "what ships"
            // and "what the wizard accepts" cannot drift.
            string idless = Project(mods, "IdLess", "{ \"Version\": \"1.0.0\" }");
            byte[] metaWas = File.ReadAllBytes(Path.Combine(idless, "meta.json"));
            string noId = null;
            try { ProjectScaffold.AddMeshReplacement(modDir, "IdLess", glb, sha, "a.bundle", "Foo", empty); }
            catch (InvalidDataException refused) { noId = refused.Message; }
            checks += Check(noId != null &&
                            noId.StartsWith("'" + Path.Combine(idless, "meta.json") + "' already exists but " +
                                            "is not a mod this project can ship: ", StringComparison.Ordinal) &&
                            noId.EndsWith("the mod manager keys every mod on it. - fix that file, or ship " +
                                          "into another project", StringComparison.Ordinal),
                            "R13 wraps Package.MetaRefusal's own ID sentence: " + noId);
            Project(mods, "NoDependency", "{ \"ID\": \"NoDependency\", \"Dependencies\": [] }");
            string noDep = null;
            try { ProjectScaffold.AddMeshReplacement(modDir, "NoDependency", glb, sha, "a.bundle", "Foo", empty); }
            catch (InvalidDataException refused) { noDep = refused.Message; }
            checks += Check(noDep != null &&
                            noDep.IndexOf("does not declare \"Dependencies\": [ \"com.morgott.ContentTool\" ]",
                                          StringComparison.Ordinal) > 0,
                            "R13 also carries the DEPENDENCY sentence: " + noDep);
            checks += Check(Same(File.ReadAllBytes(Path.Combine(idless, "meta.json")), metaWas),
                            "and a refused meta.json is never rewritten");

            // Package.MetaRefusal is REGEX-based, so an unclosed object that happens to hold a matching "ID"
            // and "Dependencies" sails straight through it while the game's own reader refuses the file.
            // The strict reader runs FIRST, so R13 means "a player would get a working mod", not "the text
            // contains the right two substrings".
            string torn = Project(mods, "TornMeta",
                                  "{\"ID\":\"x\",\"Dependencies\":[\"com.morgott.ContentTool\"");
            byte[] tornWas = File.ReadAllBytes(Path.Combine(torn, "meta.json"));
            string tornSaid = null;
            try { ProjectScaffold.AddMeshReplacement(modDir, "TornMeta", glb, sha, "a.bundle", "Foo", empty); }
            catch (InvalidDataException refused) { tornSaid = refused.Message; }
            checks += Check(tornSaid != null &&
                            tornSaid.StartsWith("'" + Path.Combine(torn, "meta.json") + "' already exists but " +
                                                "is not a mod this project can ship: ", StringComparison.Ordinal) &&
                            Same(File.ReadAllBytes(Path.Combine(torn, "meta.json")), tornWas),
                            "a meta.json that does not PARSE is R13, and is not rewritten: " + tornSaid);
            string listy = Project(mods, "ListMeta", "[1,2]");
            byte[] listyWas = File.ReadAllBytes(Path.Combine(listy, "meta.json"));
            string listySaid = null;
            try { ProjectScaffold.AddMeshReplacement(modDir, "ListMeta", glb, sha, "a.bundle", "Foo", empty); }
            catch (InvalidDataException refused) { listySaid = refused.Message; }
            checks += Check(listySaid != null &&
                            listySaid.IndexOf("is not a mod this project can ship: ",
                                              StringComparison.Ordinal) > 0 &&
                            Same(File.ReadAllBytes(Path.Combine(listy, "meta.json")), listyWas),
                            "a meta.json that is not an OBJECT is R13 too: " + listySaid);

            // ---- Scaffold_QuotesAnAuthoredId. An authored id comes back DECODED from ManifestFile.Load, so
            // it can carry a quote that would end meta.json's JSON in the wrong place.
            string quotedAt = Path.Combine(mods, "Quoted");
            Directory.CreateDirectory(quotedAt);
            File.WriteAllText(Path.Combine(quotedAt, "ppcontent.json"),
                              "{\n  \"id\": \"com.test\\\"quote\",\n  \"bundle\": \"q.bundle\"\n}\n");
            ProjectScaffold.Result quoted = ProjectScaffold.AddMeshReplacement(
                modDir, "Quoted", glb, sha, "a.bundle", "Foo", empty);
            string quotedMeta = File.ReadAllText(quoted.MetaPath);
            checks += Check(quotedMeta == Template("com.test\"quote") &&
                            (string)((Dictionary<string, object>)Json.Parse(quotedMeta, 64))["ID"] ==
                                "com.test\"quote",
                            "an authored id carrying a quote is ESCAPED into meta.json and re-reads intact");
            checks += Check(Package.MetaRefusal(quotedMeta, null) == null,
                            "and the packager still accepts that meta.json");

            // ---- Scaffold_RefusesAnUnrelatedFolder (R2)
            string squatter = Path.Combine(mods, "Squatter");
            Directory.CreateDirectory(squatter);
            File.WriteAllText(Path.Combine(squatter, "readme.txt"), "not a project");
            string why = null;
            try
            {
                ProjectScaffold.AddMeshReplacement(modDir, "Squatter", glb, sha, "a.bundle", "Foo", empty);
            }
            catch (InvalidDataException refused) { why = refused.Message; }
            checks += Check(why == "'" + squatter + "' already exists, is not empty, and holds no " +
                                   "ppcontent.json, so it is not a ContentTool project - pick another " +
                                   "project name",
                            "R2 verbatim: " + why);
            checks += Check(!File.Exists(Path.Combine(squatter, "ppcontent.json")) &&
                            !File.Exists(Path.Combine(squatter, "meta.json")),
                            "and nothing was written into someone else's folder");

            // ---- Scaffold_FillsAnEmptyFolder. An EMPTY folder of that name is not someone else's work -
            // it is a folder, and refusing it would strand an author who created it in Explorer first.
            Directory.CreateDirectory(Path.Combine(mods, "EmptyOne"));
            ProjectScaffold.Result filled = ProjectScaffold.AddMeshReplacement(
                modDir, "EmptyOne", glb, sha, "a.bundle", "Foo", empty);
            checks += Check(filled.Created && File.Exists(filled.ManifestPath),
                            "an empty folder of that name counts as new and is filled in");

            // ---- Scaffold_AppendsSecondRow. The first press is the one made above; this is what it left.
            ManifestFile one = ManifestFile.Load(made.ManifestPath);
            checks += Check(one.Manifest.Replace.Count == 1 &&
                            one.Manifest.Replace[0].Bundle == "px_equipment_assets_all.bundle" &&
                            one.Manifest.Replace[0].Asset == "WPN_PX_RG_Assault_Rifle_T01_V01" &&
                            one.Manifest.Replace[0].Mesh == "body",
                            "the first press left exactly one mesh row, mesh = the .glb's own stem");

            string second = Path.Combine(dir, "hand.glb");
            File.WriteAllBytes(second, new byte[] { 4, 5, 6, 7 });
            string secondSha = AliasMap.Sha256(File.ReadAllBytes(second));

            // The append is proved against a HAND-WRITTEN manifest, not one this tool authored: the template
            // has no unknown member, no nested value and no BOM, so a splice that lost any of those would
            // still pass a check made against it. The row's own bytes are asserted INSIDE an independently
            // located span, and everything outside that span is compared byte for byte - a substring search
            // alone proves nothing about what moved elsewhere.
            string handAt = Path.Combine(mods, "Handwritten");
            Directory.CreateDirectory(handAt);
            string handManifest = Path.Combine(handAt, "ppcontent.json");
            const string handwritten =
                "\uFEFF{\n  \"id\": \"Handwritten\",\n  \"bundle\": \"Handwritten.bundle\",\n" +
                "  \"note\": \"\u00FCnknown member, kept verbatim\",\n" +
                "  \"replace\": [ {\"bundle\":\"px_equipment_assets_all.bundle\"," +
                "\"asset\":\"WPN_PX_RG_Assault_Rifle_T01_V01\",\"mesh\":\"body\"} ],\n" +
                "  \"nested\": { \"a\": [ 1, 2, { \"b\": true } ] }\n}\n";
            File.WriteAllText(handManifest, handwritten, new UTF8Encoding(false));
            string beforeAppend = File.ReadAllText(handManifest);
            ProjectScaffold.Result grew = ProjectScaffold.AddMeshReplacement(
                modDir, "Handwritten", second, secondSha,
                "px_equipment_assets_all.bundle", "WPN_PX_Hand", empty);
            checks += Check(!grew.Created && grew.Root == handAt,
                            "the SECOND press joins the AUTHORED project instead of making another one");
            string afterAppend = File.ReadAllText(handManifest);
            // Located independently in each text - the '[' after the "replace" key through its ']' - so the
            // comparison never borrows the writer's own idea of where it wrote.
            int wasOpen = beforeAppend.IndexOf('[', beforeAppend.IndexOf("\"replace\"", StringComparison.Ordinal));
            int wasClose = beforeAppend.IndexOf(']', wasOpen);
            int isOpen = afterAppend.IndexOf('[', afterAppend.IndexOf("\"replace\"", StringComparison.Ordinal));
            int isClose = afterAppend.IndexOf(']', isOpen);
            checks += Check(beforeAppend.Substring(0, wasOpen) == afterAppend.Substring(0, isOpen) &&
                            beforeAppend.Substring(wasClose) == afterAppend.Substring(isClose),
                            "every byte OUTSIDE the replace span is unchanged - BOM, unknown member, nested " +
                            "value, prefix AND suffix");
            const string firstRow = "{\"bundle\":\"px_equipment_assets_all.bundle\"," +
                                    "\"asset\":\"WPN_PX_RG_Assault_Rifle_T01_V01\",\"mesh\":\"body\"}";
            checks += Check(afterAppend.Substring(isOpen, isClose - isOpen)
                                .IndexOf(firstRow, StringComparison.Ordinal) >= 0,
                            "and the original row survived INSIDE the new span as ONE unbroken byte run");
            ManifestFile two = ManifestFile.Load(handManifest);
            checks += Check(two.Manifest.Replace.Count == 2 && two.Manifest.Replace[1].Mesh == "hand" &&
                            two.Manifest.Id == "Handwritten" && two.Manifest.Bundle == "Handwritten.bundle",
                            "two rows now, id and bundle untouched: " + two.Manifest.Replace.Count);
            checks += Check(File.ReadAllText(Path.Combine(handAt, "meta.json")) == Template("Handwritten"),
                            "and the meta written beside it is the §4.2 template on the MANIFEST's id");

            // ---- Scaffold_ReusesAnIdenticalRow. THE RETRY PATH, in a FRESH project, so the assertion is
            // "exactly ONE row after two identical runs" rather than "two rows, one of them older". Every
            // "fix it and press Ship again" in the design meets a row this tool already committed; if that
            // read as R6 the author could never retry anything.
            ProjectScaffold.Result once = ProjectScaffold.AddMeshReplacement(
                modDir, "Replace_Twice", second, secondSha,
                "px_equipment_assets_all.bundle", "WPN_PX_Hand", empty);
            byte[] afterFirst = File.ReadAllBytes(once.ManifestPath);
            ProjectScaffold.Result reused = ProjectScaffold.AddMeshReplacement(
                modDir, "Replace_Twice", second, secondSha,
                "PX_EQUIPMENT_ASSETS_ALL.BUNDLE", "WPN_PX_Hand", empty);
            checks += Check(reused.RowAlreadyPresent && !reused.Created && reused.Root == once.Root,
                            "the IDENTICAL press reuses the row instead of refusing it");
            checks += Check(ManifestFile.Load(once.ManifestPath).Manifest.Replace.Count == 1,
                            "and the file holds exactly ONE row after two identical runs");
            checks += Check(Same(File.ReadAllBytes(once.ManifestPath), afterFirst),
                            "the manifest bytes did not move at all - a reuse writes nothing");

            // ---- Scaffold_RefusesConflictingTarget (R6 == Manifest.Validate's E4, verbatim). The same
            // target with a DIFFERENT mesh is the case R6 was written for, and the only one left.
            string dupSrc = Path.Combine(dir, "dupsrc.glb");
            File.WriteAllBytes(dupSrc, new byte[] { 8, 9 });
            byte[] beforeDup = File.ReadAllBytes(once.ManifestPath);
            string dup = null;
            try
            {
                ProjectScaffold.AddMeshReplacement(modDir, "Replace_Twice", dupSrc,
                                                   AliasMap.Sha256(File.ReadAllBytes(dupSrc)),
                                                   "PX_EQUIPMENT_ASSETS_ALL.BUNDLE", "WPN_PX_Hand", empty);
            }
            catch (InvalidDataException refused) { dup = refused.Message; }
            checks += Check(dup == "ppcontent.json already replaces \"WPN_PX_Hand\" in " +
                                   "\"PX_EQUIPMENT_ASSETS_ALL.BUNDLE\" with a mesh, so a second row for the " +
                                   "same target was NOT written - edit the existing row instead",
                            "R6 is E4 verbatim, the bundle folded case-blind: " + dup);
            checks += Check(Same(File.ReadAllBytes(once.ManifestPath), beforeDup),
                            "the manifest bytes are identical after the refusal");
            checks += Check(!File.Exists(Path.Combine(once.Root, "Content", "Meshes", "dupsrc.glb")),
                            "and the refused row copied no .glb - Validate runs before the first byte moves");
            checks += Check(ManifestFile.Load(once.ManifestPath).Manifest.Replace.Count == 1,
                            "a conflicting press leaves the one row that was already there");
        }
        finally { try { Directory.Delete(dir, true); } catch (Exception) { } }
        return "PROJECT-SCAFFOLD PASS, " + checks + " check(s) - name table, project templates";
    }

    /// <summary>The meta.json the scaffold must produce, spelled here independently of the code that writes
    /// it: a template compared against itself proves nothing. The argument is the MANIFEST's id, which is
    /// the folder name only for a project this tool created.</summary>
    private static string Template(string id)
    {
        // Escaped the way JSON escapes, spelled here rather than borrowed from JsonWriter - an id that came
        // out of an AUTHORED manifest may carry a quote or a backslash.
        string q = "\"" + id.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
        return "{\n  \"ID\": " + q + ",\n" +
               "  \"Version\": \"1.0.0\",\n" +
               "  \"Name\": [ { \"Key\": \"English\", \"Value\": " + q + " } ],\n" +
               "  \"Dependencies\": [ \"com.morgott.ContentTool\" ]\n}\n";
    }

    /// <summary>A folder that already IS a project - a valid ppcontent.json plus the meta.json under
    /// test - because R2 refuses any other non-empty folder before R13 could ever be reached.</summary>
    private static string Project(string mods, string name, string metaText)
    {
        string at = Path.Combine(mods, name);
        Directory.CreateDirectory(at);
        File.WriteAllText(Path.Combine(at, "ppcontent.json"),
                          "{\n  \"id\": \"" + name + "\",\n  \"bundle\": \"" + name + ".bundle\"\n}\n");
        File.WriteAllText(Path.Combine(at, "meta.json"), metaText);
        return at;
    }

    private static bool Same(byte[] a, byte[] b)
    {
        if (a.Length != b.Length) return false;
        for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
        return true;
    }

    private static int Check(bool condition, string what)
    {
        if (!condition) throw new Exception("PROJECT-SCAFFOLD FAILURE: " + what);
        return 1;
    }
}
