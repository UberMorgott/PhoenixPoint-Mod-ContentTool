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
                Publish(tmp, path, backupPath);
            }
            finally
            {
                // KEPT, even though Publish now has a guard of its own. This one also covers a failed
                // open, a failed write and a failed flush - none of which ever reach Publish - so
                // moving it wholesale would strand this method's temp on exactly the paths that have
                // one today. Two temps, two owners. Best effort either way: the exception the caller
                // needs is the one from the try, never one from this line.
                try { File.Delete(tmp); } catch (Exception) { }
            }
        }

        /// <summary>Publish a temp the CALLER streamed. <see cref="Write"/> makes its own temp (:19) and
        /// so cannot be handed one - a bake that streams a bundle straight into a sibling temp needs the
        /// swap without the buffer. ONE swap in the codebase, so a publication cannot drift from a write.
        ///
        /// Throws when there is no temp to publish: silently doing nothing there would leave the previous
        /// file in place and read, from the outside, exactly like a successful publication.
        ///
        /// THE TEMP MUST BE A SIBLING OF <paramref name="path"/>. Across volumes File.Move is a copy plus a
        /// delete - not a swap, and a reader can see a half-written destination - and File.Replace throws
        /// outright, so the guard below refuses instead of publishing non-atomically.</summary>
        internal static void Publish(string tempPath, string path, string backupPath = null)
        {
            if (!string.Equals(Path.GetDirectoryName(Path.GetFullPath(tempPath)),
                               Path.GetDirectoryName(Path.GetFullPath(path)),
                               StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("the temp to publish must sit beside its destination: '" +
                                            tempPath + "' is not in the folder of '" + path + "'");
            if (File.Exists(path)) File.Replace(tempPath, path, backupPath);
            else
                // THE DESTINATION CAN APPEAR between the Exists above and this Move - a second writer, or
                // the same bake retried - and the Move then throws over a file that is already there.
                try { File.Move(tempPath, path); }
                catch (IOException) when (File.Exists(path)) { File.Replace(tempPath, path, backupPath); }
            // NO cleanup arm. A swap that worked moved the temp away already; a swap that THREW must keep
            // it - the caller streamed a whole bundle into it and has no second copy to retry from, and
            // nobody else knows its name either way.
        }

        /// <summary>The encoding's PREAMBLE is NOT written - a BOM belongs in the bytes overload, where
        /// it is explicit and the caller can see it. Both callers pass new UTF8Encoding(false).</summary>
        internal static void WriteText(string path, string text, Encoding encoding, string backupPath = null)
        {
            Write(path, (encoding ?? new UTF8Encoding(false)).GetBytes(text), backupPath);
        }
    }
}
