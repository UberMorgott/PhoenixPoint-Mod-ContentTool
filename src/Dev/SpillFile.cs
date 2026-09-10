using System;
using System.IO;

namespace Morgott.ContentTool.Dev
{
    /// <summary>
    /// WHERE A WHOLE OUTPUT LANDS when the log call that names it had to be bounded. Split out of
    /// <c>ContentToolMain.Spill</c> so it carries no UnityEngine type and the one rule that matters can
    /// be proven offline: a spill NEVER lands on another spill.
    ///
    /// The name used to be prefix + DateTime.Now to the millisecond and the write was File.WriteAllText,
    /// so two spills inside the same millisecond silently overwrote each other - measured 2026-09-11:
    /// 200 messages left 14 files, 186 texts gone. Milliseconds are not a unique key; a command that
    /// bounds several reports in a row produces them far faster than one per tick.
    ///
    /// So the file is opened FileMode.CreateNew and a taken name is retried with a counter. CreateNew is
    /// what makes it safe rather than a File.Exists probe: the check and the create are one operation,
    /// so two writers racing on the same name cannot both win it.
    /// </summary>
    internal static class SpillFile
    {
        /// <summary>Writes <paramref name="msg"/> into a NEW file under <paramref name="dir"/> and
        /// returns its path. Throws only what the caller's own catch is there for (no disk, no
        /// permission); a name that is taken is not a failure, it is the next name.</summary>
        internal static string Write(string dir, string prefix, string msg)
        {
            Directory.CreateDirectory(dir);
            string stem = Path.Combine(dir, prefix + "-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff"));
            for (int n = 1; ; n++)
            {
                string path = n == 1 ? stem + ".txt" : stem + "-" + n + ".txt";
                try
                {
                    using (FileStream stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write,
                                                              FileShare.None))
                    using (StreamWriter writer = new StreamWriter(stream))
                        writer.Write(msg ?? "");
                    return path;
                }
                // The name was taken, or something else refused the create. Only the first is worth a
                // retry, and the two are not distinguishable by type - so the counter is bounded and
                // whatever it was reaches the caller instead of spinning.
                catch (IOException) when (n < 1000 && File.Exists(path)) { }
            }
        }
    }
}
