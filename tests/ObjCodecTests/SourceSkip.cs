using System;
using System.Collections.Generic;
using System.IO;
using Morgott.ContentTool.Import;
using Morgott.ContentTool.Project;

/// <summary>
/// ONE UNUSABLE SOURCE MUST NOT COST THE PROJECT ITS BUNDLE. ContentProject.Load and ProjectBake both
/// state that contract in prose - "a source the importer could not use is REPORTED and skipped, never
/// fatal" - and the mesh, model and texture folders did not honour it: GlbReader throws by design, the
/// throw escaped Load, and the whole bake died with it. A modder whose one bad body-part mesh sat next
/// to good ones got no bundle at all, which reaches them as "the mod does not activate".
///
/// <c>SourceImport.Each</c> is the seam, and it carries no UnityEngine type on purpose so this runs
/// offline. The importer here is the REAL <see cref="GlbReader"/> over real bytes, so the refusal
/// asserted below is a refusal the tool actually produces.
///
/// ponytail: the arm proves the skip rule, not ProjectBake.Run - a bake needs a live Unity and a
/// shipped bundle to open. What the bake adds on top is one line (failures += p.ImportFailures), and
/// the count this returns is what it adds.
/// </summary>
internal static class SourceSkip
{
    internal static string Run()
    {
        string good = Path.Combine(AppDomain.CurrentDomain.BaseDirectory,
            @"..\..\..\..\..\lib\u8_rootfold.glb");
        if (!File.Exists(good))
            throw new Exception("SOURCE-SKIP FAILURE: the fixture is gone - no " + Path.GetFullPath(good));

        byte[] whole = File.ReadAllBytes(good);
        // The SAME file cut off mid-chunk: a real .glb the reader cannot finish, rather than a byte
        // pattern that would trip the container check before any importer ran.
        var truncated = new byte[100];
        Array.Copy(whole, truncated, truncated.Length);

        var models = new List<SkinnedModel>();
        var refusals = new List<string>();
        // Sorted the way Sources() hands them over, so the BAD one is imported FIRST - the ordering
        // that used to guarantee the good one never got its turn.
        int failures = SourceImport.Each(new[] { "broken.glb", "u8_rootfold.glb" }, models, refusals,
            f => GlbReader.Read(f == "broken.glb" ? truncated : whole));

        int checks = Check(models.Count == 1 && models[0].JointNames.Count == 2,
            "the good source still imported after the bad one threw: " + models.Count + " model(s)");
        checks += Check(failures == 1,
            "exactly one source failed, and the count reaches the bake's failure total: " + failures);
        checks += Check(refusals.Count == 1 && refusals[0].StartsWith("broken.glb", StringComparison.Ordinal),
            "the skipped source is named: " + (refusals.Count == 0 ? "nothing was reported at all" : refusals[0]));
        checks += Check(refusals[0].IndexOf("SKIPPED", StringComparison.Ordinal) >= 0 &&
                        refusals[0].Length > "broken.glb: - SKIPPED".Length + 20,
            "and its CAUSE is carried with it, not just its name: " + refusals[0]);

        // Content\Videos\ takes the SAME sink now (ContentProject.ImportVideos): two clips sharing a stem
        // are one counted, named refusal and both skipped - the clip beside them still imports.
        string project = Path.Combine(Path.GetTempPath(), "ct-sourceskip-" + Guid.NewGuid().ToString("N"));
        try
        {
            string videos = Directory.CreateDirectory(Path.Combine(project, "Content", "Videos")).FullName;
            foreach (string n in new[] { "intro.webm", "intro.mp4", "outro.webm" })
                File.WriteAllText(Path.Combine(videos, n), "x");
            var said = new List<string>();
            string[] kept = ContentMods.Sources(project, "Videos", said, "*.webm", "*.mp4", "*.mov");
            checks += Check(kept.Length == 1 && Path.GetFileName(kept[0]) == "outro.webm" && said.Count == 1 &&
                            said[0].IndexOf("intro.mp4 and intro.webm", StringComparison.Ordinal) >= 0 &&
                            said[0].IndexOf("SKIPPED", StringComparison.Ordinal) >= 0,
                "a video stem collision is one counted SKIPPED line, not a throw: " + string.Join(" | ", said.ToArray()));
        }
        finally { try { Directory.Delete(project, true); } catch (Exception) { } }

        return "SOURCE-SKIP PASS, " + checks + " check(s) - " + refusals[0];
    }

    private static int Check(bool condition, string what)
    {
        if (!condition) throw new Exception("SOURCE-SKIP FAILURE: " + what);
        return 1;
    }
}
