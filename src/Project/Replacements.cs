using System;
using UnityEngine;

namespace Morgott.ContentTool.Project
{
    /// <summary>Per-kind content hashes - the value a record stores when the author picks a target.</summary>
    /// <remarks>
    /// replacements.json and its JsonUtility shell (ReplacementFile) are GONE: nothing read the table
    /// they loaded, JsonUtility returns an empty List for an array of custom classes here (the measured
    /// reason ContentProject.ParseReplace reads the manifest's tree instead), and its refusals were never
    /// counted - a file an author could write that the tool silently did nothing with. The target-path
    /// grammar it would have carried stays in TargetPath.cs, proven offline, for the day a reader needs it.
    /// </remarks>
    internal static class AssetSha1
    {
        /// <summary>
        /// Hash of what a texture actually shows, via the same blit readback the capture uses - a
        /// shipped texture is usually not CPU-readable, so GetPixels would simply throw.
        /// </summary>
        internal static string OfTexture(Texture2D t)
        {
            return Sha1.Hex(TextureCapture.Rgba32(t));
        }

        // ponytail: no OfMaterial yet - the property block needs the shader property walk Task 25
        // owns; writing it here would be an untested guess at an API no gate exercises.
    }

    /// <summary>
    /// FINAL-PLAN 29.5: read a texture the CPU cannot touch by drawing it into a RenderTexture and
    /// reading THAT back. This is the whole reason a dev-mode texture replacement is revertible -
    /// ImageConversion.LoadImage destroys the pixels it overwrites.
    /// </summary>
    internal static class TextureCapture
    {
        /// <summary>Straight RGBA32 bytes, or null when the readback fails.</summary>
        internal static byte[] Rgba32(Texture2D t)
        {
            Texture2D copy = Read(t);
            if (copy == null) return null;
            try { return copy.GetRawTextureData(); }
            finally { UnityEngine.Object.Destroy(copy); }
        }

        /// <summary>PNG of the same readback - what LoadImage needs to put the original back.</summary>
        internal static byte[] Png(Texture2D t)
        {
            Texture2D copy = Read(t);
            if (copy == null) return null;
            try { return copy.EncodeToPNG(); }
            finally { UnityEngine.Object.Destroy(copy); }
        }

        private static Texture2D Read(Texture2D t)
        {
            if (t == null || t.width <= 0 || t.height <= 0) return null;
            RenderTexture rt = RenderTexture.GetTemporary(
                t.width, t.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            RenderTexture prev = RenderTexture.active;
            Texture2D copy = null;
            try
            {
                Graphics.Blit(t, rt);
                RenderTexture.active = rt;
                copy = new Texture2D(t.width, t.height, TextureFormat.RGBA32, false);
                copy.ReadPixels(new Rect(0, 0, t.width, t.height), 0, 0);
                copy.Apply();
                return copy;
            }
            catch (Exception)
            {
                if (copy != null) UnityEngine.Object.Destroy(copy);
                return null;   // the caller REFUSES; it never writes on a failed capture
            }
            finally
            {
                RenderTexture.active = prev;
                RenderTexture.ReleaseTemporary(rt);
            }
        }
    }
}
