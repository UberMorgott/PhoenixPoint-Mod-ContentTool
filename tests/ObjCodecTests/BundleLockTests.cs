using System;
using System.IO;
using System.Text.RegularExpressions;
using AssetsTools.NET.Extra;

/// <summary>
/// A BUNDLE THAT DOES NOT PARSE MUST NOT STAY LOCKED. BundleBaker's readers opened a bundle with the PATH
/// overload of `AssetsManager.LoadBundleFile`, which opens the file itself and, when the header throws,
/// registers the stream nowhere - so `UnloadAll` closes nothing and the copy stays locked for the session
/// (the next bake cannot replace it). The loads also sat BEFORE the try that reaches UnloadAll.
///
/// Two arms. The library's behaviour, with its own control in the same run: the path overload leaves a
/// garbage file locked after UnloadAll, a caller-owned stream releases it. And BundleBaker's SOURCE - it
/// carries UnityEngine and cannot be linked here - never calls the path overload again.
/// </summary>
internal static class BundleLockTests
{
    internal static string Run()
    {
        string dir = Path.Combine(Path.GetTempPath(), "ct-lock-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            byte[] garbage = new byte[200];
            for (int i = 0; i < garbage.Length; i++) garbage[i] = (byte)(i + 1);

            // THE CONTROL: the path overload, exactly as the readers called it.
            string byPath = Path.Combine(dir, "path.bundle");
            File.WriteAllBytes(byPath, garbage);
            AssetsManager a = new AssetsManager();
            bool threw = false;
            try { a.LoadBundleFile(byPath, true); }
            catch (Exception) { threw = true; }
            finally { a.UnloadAll(); }
            bool lockedByPath = !Deletes(byPath);
            int checks = Check(threw && lockedByPath,
                "the path overload throws on a garbage header AND leaves the file locked after UnloadAll " +
                "- the defect the readers carried (threw=" + threw + ", locked=" + lockedByPath + ")");

            // THE FIX: the caller owns the stream and disposes it.
            string byStream = Path.Combine(dir, "stream.bundle");
            File.WriteAllBytes(byStream, garbage);
            AssetsManager b = new AssetsManager();
            threw = false;
            using (FileStream file = File.OpenRead(byStream))
            {
                try { b.LoadBundleFile(file, true); }
                catch (Exception) { threw = true; }
                finally { b.UnloadAll(); }
            }
            checks += Check(threw && Deletes(byStream),
                "a caller-owned stream releases the same garbage file the moment it is disposed");

            string baker = BakerSource();
            checks += Check(baker != null &&
                            !Regex.IsMatch(baker, @"LoadBundleFile\(\s*(bundlePath|sourceBundlePath)\s*,") &&
                            Regex.Matches(baker, @"LoadBundleFile\(").Count == 2,
                "BundleBaker opens bundles only through its own streams - the ctor and the one Read " +
                "helper - never the path overload -> " + (baker == null ? "(source not found)" : "BundleBaker.cs"));
            return "BUNDLE LOCK PASS, " + checks + " check(s) - a garbage bundle is released, never left locked";
        }
        finally
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();     // the control's orphaned stream, so the folder can go
            try { Directory.Delete(dir, true); } catch (Exception) { }
        }
    }

    private static bool Deletes(string path)
    {
        try { File.Delete(path); return !File.Exists(path); }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }

    private static string BakerSource()
    {
        for (DirectoryInfo d = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory); d != null; d = d.Parent)
        {
            string f = Path.Combine(d.FullName, "src", "Bake", "BundleBaker.cs");
            if (File.Exists(f)) return File.ReadAllText(f);
        }
        return null;
    }

    private static int Check(bool condition, string what)
    {
        if (!condition) throw new Exception("BUNDLE LOCK FAILURE: " + what);
        return 1;
    }
}
