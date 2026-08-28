using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Morgott.ContentTool.Wwise;

namespace Morgott.ContentTool.Bake
{
    /// <summary>
    /// The other half of `ct_sound bake`: a bank the project no longer declares LEAVES
    /// <see cref="SoundReplace.ShippedBanks"/>.
    ///
    /// Why it has to exist. The bake only overwrites the banks it is writing THIS time, and
    /// <see cref="SoundLoad.LoadMod"/> loads every *.bnk in that folder - so a replacement the author
    /// removed from ppcontent.json kept playing on their machine and would ship inside the package,
    /// with nothing anywhere saying why.
    ///
    /// WHICH FILES IT MAY DELETE, and the rule is deliberately narrow: NAME AND CONTENT MUST AGREE.
    /// A file is this bake's own only when it is called &lt;mediaId&gt;.bnk AND its BKHD carries the bank
    /// id this project stamps for that media (fnv1_lower32("&lt;modid&gt;_&lt;mediaId&gt;"), the id
    /// <see cref="SoundReplace"/> computes at the write). A bank the modder dropped in by hand, one
    /// another project baked, and anything that is not a Wwise bank at all fail that test and are left
    /// alone - the folder is the mod's own, and deleting a file we did not write is not a tidy-up, it
    /// is data loss.
    /// Deliberately free of UnityEngine types, like <see cref="Project.ContentMods"/>, so the rule is
    /// falsifiable offline against real files.
    /// </summary>
    internal static class BankPrune
    {
        /// <summary>The bank id `ct_sound bake` stamps into &lt;media&gt;.bnk for this project.</summary>
        internal static uint BankId(string modId, uint media)
        {
            return WwiseId.Hash((modId ?? "").ToLowerInvariant() + "_" + media);
        }

        /// <summary>
        /// Did THIS project's bake write that file? Reads the 16-byte BKHD prologue only - fourCC,
        /// chunk size, bank version, bank id - which is all the evidence there is and all we need.
        /// </summary>
        internal static bool Generated(string path, string modId, out uint media)
        {
            media = 0;
            if (!uint.TryParse(Path.GetFileNameWithoutExtension(path), out media)) return false;
            byte[] head = new byte[16];
            try
            {
                using (FileStream f = File.OpenRead(path))
                    if (f.Read(head, 0, head.Length) != head.Length) return false;
            }
            catch (IOException) { return false; }
            catch (UnauthorizedAccessException) { return false; }
            return Encoding.ASCII.GetString(head, 0, 4) == "BKHD"
                   && BitConverter.ToUInt32(head, 12) == BankId(modId, media);
        }

        /// <summary>
        /// Removes every bank of ours whose media is not in <paramref name="keep"/> - the media set the
        /// bake just wrote. Returns the line to log, or null when there was nothing to say.
        /// </summary>
        internal static string Sweep(string dir, string modId, ICollection<uint> keep)
        {
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return null;
            List<string> gone = new List<string>(), foreign = new List<string>();
            foreach (string path in Directory.GetFiles(dir, "*.bnk"))
            {
                uint media;
                string name = Path.GetFileName(path);
                if (!Generated(path, modId, out media)) { foreign.Add(name); continue; }
                if (keep != null && keep.Contains(media)) continue;
                try { File.Delete(path); gone.Add(name); }
                catch (IOException ex) { foreign.Add(name + " (could not be removed: " + ex.Message + ")"); }
                catch (UnauthorizedAccessException ex) { foreign.Add(name + " (could not be removed: " + ex.Message + ")"); }
            }
            if (gone.Count == 0 && foreign.Count == 0) return null;

            StringBuilder log = new StringBuilder();
            if (gone.Count > 0)
                log.Append("removed ").Append(gone.Count).Append(" stale bank(s) this project no longer ")
                   .Append("declares: ").Append(string.Join(", ", gone.ToArray()));
            if (foreign.Count > 0)
                log.Append(gone.Count > 0 ? " | " : "").Append("left ").Append(foreign.Count)
                   .Append(" bank(s) alone - not written by this project's bake: ")
                   .Append(string.Join(", ", foreign.ToArray()));
            return log.ToString();
        }
    }
}
