using System;
using System.Collections.Generic;
using System.IO;
using Morgott.ContentTool.Dev;

/// <summary>
/// A SPILL MAY NEVER LAND ON ANOTHER SPILL. The file behind a bounded log call is the only copy of the
/// text - the log itself has the truncated version - so one overwrite is one report gone with nothing
/// saying so. The old namer was prefix + DateTime.Now to the millisecond plus File.WriteAllText, and a
/// command that bounds several reports in a row writes them far faster than a tick: 200 messages left
/// 14 files, MEASURED 2026-09-11.
///
/// The arm is the same shape as the defect: 200 spills as fast as the machine will write them, in one
/// folder, then count files and read every one back.
/// </summary>
internal static class SpillTests
{
    internal static string Run()
    {
        string dir = Path.Combine(Path.GetTempPath(), "ct-spill-" + Guid.NewGuid().ToString("N"));
        try
        {
            var paths = new List<string>();
            for (int i = 0; i < 200; i++) paths.Add(SpillFile.Write(dir, "log", "message " + i));

            int checks = Check(new HashSet<string>(paths).Count == 200,
                "200 spills written in one instant get 200 DISTINCT names, not one per millisecond: " +
                new HashSet<string>(paths).Count);
            checks += Check(Directory.GetFiles(dir).Length == 200,
                "and 200 files are actually on disk: " + Directory.GetFiles(dir).Length);

            for (int i = 0; i < 200; i++)
                if (File.ReadAllText(paths[i]) != "message " + i)
                    throw new Exception("SPILL FAILURE: " + paths[i] + " no longer holds message " + i +
                                        " - a later spill overwrote it");
            checks++;

            // The first name of a millisecond stays the plain one - the counter is the collision arm,
            // not the normal one - and every path is under the folder it was given.
            checks += Check(Path.GetFileName(paths[0]).StartsWith("log-", StringComparison.Ordinal) &&
                            paths[0].EndsWith(".txt", StringComparison.Ordinal) &&
                            Path.GetDirectoryName(paths[0]) == dir,
                "the name is still prefix-stamp.txt in the folder asked for: " + paths[0]);
            return "SPILL PASS, " + checks + " check(s) - 200 spills, 200 files, 200 texts";
        }
        finally { try { Directory.Delete(dir, true); } catch (Exception) { } }
    }

    private static int Check(bool condition, string what)
    {
        if (!condition) throw new Exception("SPILL FAILURE: " + what);
        return 1;
    }
}
