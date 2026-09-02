using System;
using System.IO;
using System.Text;

namespace Morgott.ContentTool.IO
{
    /// <summary>
    /// The tmp-then-swap write, in ONE place - AliasMap.SaveSidecar:245-257 consolidated, with its one
    /// real weakness fixed: the temp name is UNIQUE, so two writers cannot land on each other and a
    /// ".tmp" a crash left behind is just another file rather than a blocker. File.Replace REQUIRES an
    /// existing destination, which is why the two arms are not one call; a backupPath is honoured only
    /// on the replace arm, since a file being created has nothing to back up.
    /// Never write `IO.Something` from another namespace of this mod: it would bind here, not to System.IO.
    /// </summary>
    internal static class AtomicFile
    {
        internal static void Write(string path, byte[] bytes, string backupPath = null)
        {
            string tmp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (FileStream stream = new FileStream(tmp, FileMode.CreateNew, FileAccess.Write,
                                                          FileShare.None))
                {
                    stream.Write(bytes, 0, bytes.Length);
                    // The swap is atomic, the CONTENT is not: without this the name can be in place
                    // while the bytes are still in the OS cache, and a power cut leaves an empty file
                    // under a name that says it is the author's manifest.
                    stream.Flush(true);
                }
                if (File.Exists(path)) File.Replace(tmp, path, backupPath);
                else File.Move(tmp, path);
            }
            finally
            {
                // A successful swap moved it away, so this is a no-op; any failure above - the open, the
                // write, the flush or the commit - is cleaned up here. Best effort either way: the
                // exception the caller needs is the one from the try, never one from this line.
                try { File.Delete(tmp); } catch (Exception) { }
            }
        }

        /// <summary>The encoding's PREAMBLE is NOT written - a BOM belongs in the bytes overload, where
        /// it is explicit and the caller can see it. Both callers pass new UTF8Encoding(false).</summary>
        internal static void WriteText(string path, string text, Encoding encoding, string backupPath = null)
        {
            Write(path, (encoding ?? new UTF8Encoding(false)).GetBytes(text), backupPath);
        }
    }
}
