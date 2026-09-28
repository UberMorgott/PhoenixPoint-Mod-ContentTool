using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Morgott.ContentTool.Import;
using Morgott.ContentTool.IO;

namespace Morgott.ContentTool.Project
{
    /// <summary>
    /// The DISK half of the bench's Sounds, Videos and Add-a-weapon screens: the same mod folder the Doctor's
    /// SHIP makes (a sibling of Mods\ContentTool\, discoverable and switchable by the mod manager), plus the one
    /// file and the one row each press adds. Nothing here is a new route - every file lands where the existing
    /// verbs already read it: `ct_sound bake` reads Content\Audio\Replace\&lt;mediaId&gt;.ext (the file name IS the
    /// target), `ct_video live` reads a "replace" row with "video", and the bake + WeaponBuild read "publish" and
    /// "weapons". UnityEngine-free, so it is proven in tests\ObjCodecTests.
    /// </summary>
    internal static partial class ProjectScaffold
    {
        internal static readonly string[] AudioExtensions = { ".wav", ".ogg", ".mp3" };
        internal static readonly string[] VideoExtensions = { ".webm", ".mp4", ".mov" };

        /// <summary>The shipped Unity built-in shader bundle every published model depends on (the demos'
        /// "deps", demos\WeaponAdd\ppcontent.json).</summary>
        internal const string ModelDeps = "defaultlocalgroup_unitybuiltinshaders.bundle";

        /// <summary>
        /// The project of that name beside ContentTool, created (ppcontent.json {id, bundle} + meta.json) when
        /// it is not there yet. Same refusals as the Doctor's SHIP: R1 on the name, R2 on a folder that holds
        /// files but no ppcontent.json, R13 on a meta.json no package would ship. Returns the absolute root.
        /// </summary>
        internal static string EnsureProject(string modDir, string name)
        {
            string refusal = NameRefusal(name);
            if (refusal != null) throw new InvalidDataException(refusal);
            string root = RootOf(modDir, name);
            if (root == null)
                throw new InvalidDataException("ContentTool's own mod folder is not known, so there is nowhere " +
                                               "beside it to put a project");
            string manifest = Path.Combine(root, ContentMods.Manifest);
            string meta = Path.Combine(root, "meta.json");
            if (!File.Exists(manifest))
            {
                if (Directory.Exists(root) && (Occupied(root) || Directory.GetDirectories(root).Length != 0))
                    throw new InvalidDataException("'" + root + "' already exists, is not empty, and holds no " +
                                                   "ppcontent.json, so it is not a ContentTool project - pick " +
                                                   "another mod name");
                Directory.CreateDirectory(root);
                var tree = new Dictionary<string, object>(StringComparer.Ordinal)
                {
                    { "id", name }, { "bundle", name + ".bundle" }
                };
                CreateNew(manifest, new UTF8Encoding(false).GetBytes(new JsonWriter().Val(tree).ToString() + "\n"));
            }
            string id = ManifestFile.Load(manifest).Manifest.Id;     // E1/E2: a manifest this tool can edit
            MetaMustBeShippable(meta, "or pick another mod name");
            if (!File.Exists(meta)) CreateNew(meta, new UTF8Encoding(false).GetBytes(Meta(id)));
            // The console verbs the screens call (`ct_sound bake <name>`) resolve the NAME, so it has to land here.
            if (!string.Equals(ContentMods.ProjectDir(modDir, name), root, StringComparison.OrdinalIgnoreCase))
                throw new IOException("'" + root + "' was written but the name '" + name + "' resolves to " +
                                      ContentMods.ProjectDir(modDir, name) + " - pick another mod name");
            return root;
        }

        // ------------------------------------------------------------------ sounds

        /// <summary>
        /// Replace the shipped media <paramref name="media"/> with <paramref name="source"/>: the file is
        /// copied to Content\Audio\Replace\&lt;media&gt;&lt;ext&gt;, which is the whole declaration `ct_sound bake`
        /// needs. A copy under another of the three extensions is the SAME media (SoundReplace.Sources refuses
        /// the pair), so it is removed - it is this screen's own earlier copy, the author's original is
        /// wherever it was picked from. Returns the copy's path.
        /// </summary>
        internal static string AddSound(string modDir, string name, string source, uint media)
        {
            string ext = Extension(source, AudioExtensions, "a sound");
            if (media == 0) throw new InvalidDataException("pick the game sound to replace first");
            byte[] bytes = File.ReadAllBytes(source);
            string root = EnsureProject(modDir, name);

            // A "sounds" row already aiming at this media would be a SECOND file for it - the bake refuses
            // the pair, so it is refused here, before anything is copied.
            string manifest = Path.Combine(root, ContentMods.Manifest);
            var refused = new List<string>();
            foreach (KeyValuePair<uint, string> row in
                     Manifest.Sounds(Manifest.Tree(File.ReadAllText(manifest), ContentMods.Manifest), refused))
                if (row.Key == media && !string.Equals(row.Value, media + ext, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("ppcontent.json's \"sounds\" already replaces media " + media +
                                                   " with '" + row.Value + "' - remove that row first");

            string dir = SoundDir(root);
            Directory.CreateDirectory(dir);
            string dest = Path.Combine(dir, media + ext);
            // The NEW copy first, the old twin after: a failed write leaves the mod its previous sound.
            AtomicFile.Write(dest, bytes);
            foreach (string other in AudioExtensions)
            {
                string twin = Path.Combine(dir, media + other);
                if (other != ext && File.Exists(twin)) File.Delete(twin);
            }
            return dest;
        }

        internal static string SoundDir(string root)
        {
            return Path.Combine(Path.Combine(Path.Combine(root, "Content"), "Audio"), "Replace");
        }

        /// <summary>One replaced sound of a project, and how far it has got.</summary>
        internal sealed class SoundItem
        {
            internal uint Media;
            internal string File;
            /// <summary>"built", "out of date" (the file is newer than its bank) or "not built".</summary>
            internal string State;
        }

        /// <summary>Every replacement the project carries - the "sounds" rows and the &lt;mediaId&gt;.ext files,
        /// exactly the two sources SoundReplace.Replacements reads - with its bank's state under Dist\Sounds.
        /// Never throws: it is asked from a panel.</summary>
        internal static List<SoundItem> Sounds(string root)
        {
            var items = new List<SoundItem>();
            try
            {
                string dir = SoundDir(root);
                if (!Directory.Exists(dir)) return items;
                var named = new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase);
                string manifest = Path.Combine(root, ContentMods.Manifest);
                if (File.Exists(manifest))
                    foreach (KeyValuePair<uint, string> row in
                             Manifest.Sounds(Manifest.Tree(File.ReadAllText(manifest), ContentMods.Manifest),
                                             new List<string>()))
                        named[row.Value] = row.Key;
                var files = new List<string>(Directory.GetFiles(dir));
                files.Sort(StringComparer.OrdinalIgnoreCase);
                foreach (string f in files)
                {
                    string leaf = Path.GetFileName(f);
                    if (Array.IndexOf(AudioExtensions, Path.GetExtension(f).ToLowerInvariant()) < 0) continue;
                    uint media;
                    if (!named.TryGetValue(leaf, out media) &&
                        !uint.TryParse(Path.GetFileNameWithoutExtension(f), out media)) continue;
                    string bank = Path.Combine(Path.Combine(Path.Combine(root, "Dist"), "Sounds"), media + ".bnk");
                    string state = !File.Exists(bank) ? "not built"
                                 : File.GetLastWriteTimeUtc(bank) < File.GetLastWriteTimeUtc(f) ? "out of date"
                                 : "built";
                    items.Add(new SoundItem { Media = media, File = leaf, State = state });
                }
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException ||
                                       ex is InvalidDataException)
            { }
            return items;
        }

        // ------------------------------------------------------------------ videos

        /// <summary>
        /// Put <paramref name="source"/> in Content\Videos\ and add ONE "replace" row naming it:
        /// <paramref name="shippedAsset"/> is the shipped clip it replaces (a StreamableCopiedAssets path, the
        /// form `ct_video` matches by tail), or null to ADD a new clip. The same row again is reused, not
        /// refused; the same stem or the same shipped clip claimed by ANOTHER row is refused, nothing written.
        /// Returns the copy's path.
        /// </summary>
        internal static string AddVideo(string modDir, string name, string source, string shippedAsset)
        {
            string ext = Extension(source, VideoExtensions, "a video");
            string stem = Stem(source);
            byte[] bytes = File.ReadAllBytes(source);
            string root = EnsureProject(modDir, name);
            string dir = Path.Combine(Path.Combine(root, "Content"), "Videos");
            foreach (string other in VideoExtensions)
                if (other != ext && File.Exists(Path.Combine(dir, stem + other)))
                    throw new InvalidDataException("Content\\Videos\\" + stem + other + " is already there, and a " +
                                                   "row names only the stem '" + stem + "' - rename your file");

            ManifestFile file = ManifestFile.Load(Path.Combine(root, ContentMods.Manifest));
            bool reuse = false;
            foreach (ReplaceRow row in file.Manifest.Replace)
            {
                if (row.Kind != "video") continue;
                bool sameStem = string.Equals(row.Video, stem, StringComparison.OrdinalIgnoreCase);
                bool sameAsset = !string.IsNullOrEmpty(shippedAsset) &&
                                 string.Equals(row.Asset, shippedAsset, StringComparison.OrdinalIgnoreCase);
                if (sameStem && string.Equals(row.Asset ?? "", shippedAsset ?? "", StringComparison.OrdinalIgnoreCase))
                { reuse = true; continue; }
                if (sameStem)
                    throw new InvalidDataException("this mod already uses a clip named '" + stem + "' for " +
                                                   (string.IsNullOrEmpty(row.Asset) ? "a new video" : row.Asset) +
                                                   " - rename your file");
                if (sameAsset)
                    throw new InvalidDataException("this mod already replaces " + shippedAsset + " with '" +
                                                   row.Video + "' - pick that clip, or another game video");
            }
            if (!reuse) file.Manifest.AddVideoReplacement(stem, shippedAsset);
            file.Manifest.Validate();

            Directory.CreateDirectory(dir);
            string dest = Path.Combine(dir, stem + ext);
            // A clip already there with other bytes is the author's previous version: kept as .bak.
            AtomicFile.Write(dest, bytes, File.Exists(dest) ? dest + ".bak" : null);
            file.Save();                                    // the row LAST: never a row without its file
            return dest;
        }

        /// <summary>The project's video rows as (clip stem, shipped clip or null for a new one, file there).</summary>
        internal static List<KeyValuePair<string, string>> Videos(string root, out List<bool> present)
        {
            var list = new List<KeyValuePair<string, string>>();
            present = new List<bool>();
            try
            {
                string manifest = Path.Combine(root, ContentMods.Manifest);
                if (!File.Exists(manifest)) return list;
                string dir = Path.Combine(Path.Combine(root, "Content"), "Videos");
                foreach (ReplaceRow row in Manifest.Parse(File.ReadAllText(manifest)).Replace)
                {
                    if (row.Kind != "video") continue;
                    list.Add(new KeyValuePair<string, string>(row.Video, row.Asset));
                    bool there = false;
                    foreach (string ext in VideoExtensions)
                        there |= !string.IsNullOrEmpty(row.Video) && File.Exists(Path.Combine(dir, row.Video + ext));
                    present.Add(there);
                }
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException ||
                                       ex is InvalidDataException)
            { }
            return list;
        }

        // ------------------------------------------------------------------ weapons

        /// <summary>What one Add-a-weapon press wrote.</summary>
        internal sealed class WeaponResult
        {
            internal string Root, WeaponId, ModelKey, ModelPath;
            internal bool ModelReused;
        }

        /// <summary>The def name a weapon called <paramref name="weaponName"/> gets in project
        /// <paramref name="project"/>: letters and digits only, so it is a def name the repository and every
        /// manifest reader take as it stands.</summary>
        internal static string WeaponId(string project, string weaponName)
        {
            return Alnums(project) + "_" + Alnums(weaponName) + "_WeaponDef";
        }

        /// <summary>
        /// A NEW weapon cloned from the shipped <paramref name="donor"/> and wearing <paramref name="glb"/>:
        /// the .glb copied to Content\Models\ (baked as "models/&lt;stem&gt;"), one "publish" row giving that
        /// prefab a fresh key, and one "weapons" row - id, name, clone, guid, model, "fit": "auto" - the minimum
        /// demos\WeaponAdd documents. A model already published by this project under that stem is reused.
        /// Nothing is written when the id is taken or a different file already holds the stem.
        /// </summary>
        internal static WeaponResult AddWeapon(string modDir, string name, string glb, string donor, string weaponName)
        {
            Extension(glb, new[] { ".glb" }, "a model");
            if (string.IsNullOrEmpty(donor)) throw new InvalidDataException("pick the game weapon to start from");
            string display = (weaponName ?? "").Trim();
            if (Alnums(display).Length == 0 || display.Length > 40)
                throw new InvalidDataException("give the weapon a name of 1-40 characters with a letter or digit in it");
            string stem = Stem(glb);
            byte[] bytes = File.ReadAllBytes(glb);
            var result = new WeaponResult { Root = EnsureProject(modDir, name), WeaponId = WeaponId(name, display) };

            ManifestFile file = ManifestFile.Load(Path.Combine(result.Root, ContentMods.Manifest));
            var junk = new List<object>();
            foreach (Dictionary<string, object> row in Manifest.Rows(file.Manifest.Root, "weapons", junk))
                if (string.Equals(Manifest.Str(row, "id"), result.WeaponId, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("this mod already has a weapon '" + result.WeaponId +
                                                   "' - pick another name");

            string asset = "models/" + stem;
            foreach (Dictionary<string, object> row in Manifest.Rows(file.Manifest.Root, "publish", junk))
                if (string.Equals(Manifest.Str(row, "asset"), asset, StringComparison.OrdinalIgnoreCase))
                { result.ModelKey = Manifest.Str(row, "key"); result.ModelReused = true; }

            result.ModelPath = Path.Combine(Path.Combine(Path.Combine(result.Root, "Content"), "Models"), stem + ".glb");
            if (File.Exists(result.ModelPath) &&
                !string.Equals(AliasMap.Sha256(File.ReadAllBytes(result.ModelPath)), AliasMap.Sha256(bytes),
                               StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Content\\Models\\" + stem + ".glb already holds a DIFFERENT file, " +
                                               "so it was not overwritten - rename your .glb");

            if (string.IsNullOrEmpty(result.ModelKey))
            {
                result.ModelKey = Guid.NewGuid().ToString("N");
                file.Manifest.AddRow("publish", new Dictionary<string, object>(StringComparer.Ordinal)
                {
                    { "key", result.ModelKey }, { "asset", asset }, { "type", "GameObject" }, { "deps", ModelDeps }
                });
            }
            file.Manifest.AddRow("weapons", new Dictionary<string, object>(StringComparer.Ordinal)
            {
                { "id", result.WeaponId }, { "name", display }, { "clone", donor },
                { "guid", Guid.NewGuid().ToString("D") }, { "model", result.ModelKey }, { "fit", "auto" },
                // Starting storage: WeaponBuild.Seed puts count weapons + clips magazines in a new campaign's
                // base, and reads an absent value as 0 - the demo's own numbers.
                { "count", "2" }, { "clips", "10" }
            });
            file.Manifest.Validate();

            Directory.CreateDirectory(Path.GetDirectoryName(result.ModelPath));
            CreateNew(result.ModelPath, bytes);             // absent -> written; present -> same bytes, checked above
            file.Save();
            return result;
        }

        /// <summary>The project's weapons as (id, name, donor). Never throws.</summary>
        internal static List<string[]> Weapons(string root)
        {
            var list = new List<string[]>();
            try
            {
                string manifest = Path.Combine(root, ContentMods.Manifest);
                if (!File.Exists(manifest)) return list;
                foreach (Dictionary<string, object> row in
                         Manifest.Rows(Manifest.Tree(File.ReadAllText(manifest), ContentMods.Manifest), "weapons",
                                       new List<object>()))
                    list.Add(new[] { Manifest.Str(row, "id"), Manifest.Str(row, "name"), Manifest.Str(row, "clone") });
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException ||
                                       ex is InvalidDataException)
            { }
            return list;
        }

        // ------------------------------------------------------------------ shared

        /// <summary>The file's extension, lower-case, when it is one of <paramref name="allowed"/> and the
        /// file is on disk; the refusal otherwise.</summary>
        private static string Extension(string path, string[] allowed, string what)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
                throw new FileNotFoundException("pick " + what + " file first", path ?? "");
            string ext = Path.GetExtension(path).ToLowerInvariant();
            if (Array.IndexOf(allowed, ext) < 0)
                throw new InvalidDataException("'" + Path.GetFileName(path) + "' is not " + what + " file this tool " +
                                               "reads - use " + string.Join(", ", allowed));
            return ext;
        }

        /// <summary>The file's name without extension, refused when it is empty or a Windows device name -
        /// it becomes a file name in the project and the value of a row.</summary>
        private static string Stem(string path)
        {
            string stem = Path.GetFileNameWithoutExtension(path);
            int dot = string.IsNullOrEmpty(stem) ? -1 : stem.IndexOf('.');
            string bare = dot < 0 ? stem : stem.Substring(0, dot);
            if (string.IsNullOrEmpty(stem) || Array.Exists(Devices, d => string.Equals(d, bare, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidDataException("'" + Path.GetFileName(path) + "' cannot be a file name in the " +
                                               "project - rename it");
            return stem;
        }

        private static string Alnums(string s)
        {
            var b = new StringBuilder();
            foreach (char c in s ?? "") if (Alnum(c)) b.Append(c);
            return b.ToString();
        }
    }
}
