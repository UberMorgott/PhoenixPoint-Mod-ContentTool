using System;
using System.Collections.Generic;

namespace Morgott.ContentTool.Import
{
    /// <summary>
    /// A .glb read for REPLACEMENT, with everything the callers have to be able to say about it. A
    /// bare SkinnedModel is not enough: neither LiveMesh.Load nor the bake could then name the
    /// sidecar it applied, and "never silent" (design §5) would be a promise nothing could keep.
    /// </summary>
    internal sealed class ReplacementSource
    {
        /// <summary>The model with aliases APPLIED - what a preview and a bake must both use.</summary>
        internal SkinnedModel Model;
        /// <summary>The same file read again with NO aliases. The outcome is computed from this one
        /// whenever the sidecar did not apply, and the Doctor re-derives every alias edit from its
        /// joint names so edits are order-independent.</summary>
        internal SkinnedModel Original;
        internal string Path;
        internal string Sha256;
        internal long Bytes;
        /// <summary>The sidecar that WAS applied, or null.</summary>
        internal string SidecarPath;
        internal int AliasesApplied;
        internal AliasMap Aliases;
        /// <summary>Sidecar keys the file has no bone for - one AliasUnused warning each.</summary>
        internal IList<string> UnusedAliasKeys = new List<string>();
        /// <summary>Why a sidecar that EXISTS was not applied, or null. Always a warning, never an
        /// outcome: ignoring a sidecar leaves a file that may still bind by name on its own.</summary>
        internal string SidecarRefusal;
        /// <summary>One block naming the sidecar and every mapping, ready for the log. Null when no
        /// sidecar applied.</summary>
        internal string AliasLog;
    }

    /// <summary>
    /// THE single "read a .glb for a replacement" helper. LiveMesh.Load, ContentProject.ImportMesh and
    /// ReplacementPreflight all come through here, so a sidecar cannot apply on one path and not on
    /// another - which is the way a preview and a bake start disagreeing.
    /// </summary>
    internal static class GlbSource
    {
        internal static ReplacementSource ReadReplacement(byte[] bytes, string path)
        {
            if (bytes == null) throw new ArgumentNullException(nameof(bytes));

            var source = new ReplacementSource
            {
                Path = path,
                Bytes = bytes.Length,
                Sha256 = AliasMap.Sha256(bytes),
                Original = GlbReader.Read(bytes)
            };
            source.Model = source.Original;

            string why;
            AliasMap map = AliasMap.LoadSidecar(path, source.Sha256, out why);
            source.SidecarRefusal = why;
            if (map == null) return source;

            // The aliased model is a SECOND read, not a mutation of Original: the Doctor re-applies a
            // changed map onto the pristine names every keystroke, and a model renamed in place has
            // already lost them.
            source.Model = GlbReader.Read(bytes);
            IList<string> unused;
            map.Apply(source.Model, out unused);
            source.Aliases = map;
            source.AliasesApplied = map.Count - unused.Count;
            source.UnusedAliasKeys = unused;
            source.SidecarPath = AliasMap.SidecarPathOf(path);
            source.AliasLog = map.Describe(source.SidecarPath, unused);
            return source;
        }
    }
}
