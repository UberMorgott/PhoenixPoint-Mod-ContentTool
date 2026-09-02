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
        }
        finally { try { Directory.Delete(dir, true); } catch (Exception) { } }
        return "MANIFEST PASS, " + checks + " check(s) - atomic write";
    }

    private static string[] Temps(string dir) { return Directory.GetFiles(dir, "*.tmp"); }

    private static int Check(bool condition, string what)
    {
        if (!condition) throw new Exception("MANIFEST FAILURE: " + what);
        return 1;
    }
}
