using System;
using System.Collections.Generic;
using System.IO;
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
        }
        finally { try { Directory.Delete(dir, true); } catch (Exception) { } }
        return "PROJECT-SCAFFOLD PASS, " + checks + " check(s) - name table, project templates";
    }

    /// <summary>The meta.json the scaffold must produce, spelled here independently of the code that writes
    /// it: a template compared against itself proves nothing. The argument is the MANIFEST's id, which is
    /// the folder name only for a project this tool created.</summary>
    private static string Template(string id)
    {
        return "{\n  \"ID\": \"" + id + "\",\n" +
               "  \"Version\": \"1.0.0\",\n" +
               "  \"Name\": [ { \"Key\": \"English\", \"Value\": \"" + id + "\" } ],\n" +
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
