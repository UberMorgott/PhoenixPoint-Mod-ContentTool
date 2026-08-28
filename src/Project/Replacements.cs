using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Morgott.ContentTool.Project
{
    /// <summary>
    /// replacements.json - the authored replacement table, read with the same reader ppcontent.json
    /// uses (JsonUtility, so no JSON dependency enters the tool). Everything that can be decided
    /// without the engine lives in TargetPath.cs and is proven offline; this file is only the shell.
    ///
    ///   { "replacements": [ { "target": "...", "content": "...", "sha1": "..." } ] }
    /// </summary>
    internal static class ReplacementFile
    {
        internal const string FileName = "replacements.json";

        [Serializable]
        private sealed class Record
        {
            public string target = null;
            public string content = null;
            public string sha1 = null;
        }

        [Serializable]
        private sealed class File_
        {
            public List<Record> replacements = new List<Record>();
        }

        /// <summary>
        /// Loads and validates. A missing file is an empty table, not an error - most projects only
        /// ADD content. Refused records are named in <paramref name="refusals"/> and skipped.
        /// </summary>
        internal static List<ReplacementRule> Load(string root, List<string> refusals)
        {
            string path = Path.Combine(root, FileName);
            if (!File.Exists(path)) return new List<ReplacementRule>();

            File_ f = JsonUtility.FromJson<File_>(File.ReadAllText(path));
            List<ReplacementRule> raw = new List<ReplacementRule>();
            if (f != null && f.replacements != null)
                foreach (Record r in f.replacements)
                    raw.Add(new ReplacementRule { Target = r.target, Content = r.content, Sha1 = r.sha1 });

            return ReplacementSet.Validate(raw, refusals);
        }

        /// <summary>Writes the table back in the canonical target-path spelling (TargetPath.Format).</summary>
        internal static void Save(string root, IEnumerable<ReplacementRule> rules)
        {
            File_ f = new File_();
            foreach (ReplacementRule r in rules)
                f.replacements.Add(new Record
                {
                    target = r.Path != null ? r.Path.Format() : r.Target,
                    content = r.Content,
                    sha1 = r.Sha1
                });
            File.WriteAllText(Path.Combine(root, FileName), JsonUtility.ToJson(f, true));
        }
    }

    /// <summary>Per-kind content hashes - the value a record stores when the author picks a target.</summary>
    internal static class AssetSha1
    {
        /// <summary>
        /// Null when the mesh is not CPU-readable, which most shipped meshes are not: that is
        /// Verification.Unverifiable, not a failure (FINAL-PLAN 39.2 dropped the dump pipeline that
        /// existed to force readability).
        /// </summary>
        internal static string OfMesh(Mesh m)
        {
            if (m == null || !m.isReadable) return null;
            using (MemoryStream ms = new MemoryStream())
            using (BinaryWriter w = new BinaryWriter(ms))
            {
                Vector3[] v = m.vertices, n = m.normals;
                Vector2[] uv = m.uv;
                int[] tri = m.triangles;
                w.Write(v.Length); foreach (Vector3 p in v) { w.Write(p.x); w.Write(p.y); w.Write(p.z); }
                w.Write(n.Length); foreach (Vector3 p in n) { w.Write(p.x); w.Write(p.y); w.Write(p.z); }
                w.Write(uv.Length); foreach (Vector2 p in uv) { w.Write(p.x); w.Write(p.y); }
                w.Write(tri.Length); foreach (int t in tri) w.Write(t);
                w.Flush();
                return Sha1.Hex(ms.ToArray());
            }
        }

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
