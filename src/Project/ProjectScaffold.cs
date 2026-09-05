using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Morgott.ContentTool.Import;

namespace Morgott.ContentTool.Project
{
    /// <summary>
    /// ONE PRESS, ONE MOD FOLDER. Turns a green Doctor verdict into a real project beside Mods\ContentTool\:
    /// ppcontent.json + meta.json + Content\Meshes\&lt;stem&gt;.glb + its alias sidecar, with one "replace" row
    /// added to whatever the author already had.
    ///
    /// It AUTHORS nothing itself. Every byte goes out through ManifestFile.Save (atomic splice, .bak, the E5
    /// fingerprint) and AliasMap.SaveSidecar, which is why an existing project's own formatting, key order
    /// and unknown keys survive a press by construction. The three files that must NOT already exist - the
    /// two templates and the mesh copy - go out through <see cref="CreateNew"/> instead, never through
    /// AtomicFile's upsert writer.
    ///
    /// PLACEMENT IS THE WHOLE POINT: the SIBLING Mods\&lt;name&gt;, never ContentMods.ProjectDir's
    /// Mods\ContentTool\&lt;name&gt; fallback (ContentMods.cs:147). A folder under ContentTool is not a mod the
    /// manager can discover (ModGate.Decide:38 -> Unknown) or the player can switch off, so shipping into one
    /// would produce content nobody can turn off - gate G1's bug through a different door.
    ///
    /// UnityEngine-free on purpose: the whole disk half is proven in tests\ObjCodecTests instead of by pressing
    /// a button in a running game.
    /// </summary>
    internal static class ProjectScaffold
    {
        /// <summary>What one press produced, so the panel can name the folder it wrote and the bake can be
        /// handed the ABSOLUTE root rather than a name the console parser would have to re-resolve.</summary>
        internal sealed class Result
        {
            internal string Root, ManifestPath, MetaPath, MeshPath, SidecarPath;
            internal bool Created, MeshAlreadyPresent, RowAlreadyPresent;
            /// <summary>The bytes that were VERIFIED against the verdict's sha and are now the copy's, so
            /// the caller re-judges what it wrote instead of re-reading the file and re-opening the
            /// question of whether the two are the same bytes.</summary>
            internal byte[] MeshBytes;
        }

        /// <summary>Windows reserves these with OR without an extension, so "nul.glb" is a folder that cannot
        /// be created and whose failure reads like a bug in this tool.</summary>
        private static readonly string[] Devices =
        {
            "CON", "PRN", "AUX", "NUL",
            "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
            "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
        };

        /// <summary>Null when the name may be a folder beside the player's other mods; the refusal otherwise.
        /// R1. The rule is deliberately narrower than the filesystem's: this name is also the mod ID, the
        /// meta.json "ID" and the bundle's stem, and every one of those is compared as text somewhere.</summary>
        internal static string NameRefusal(string name)
        {
            if (!Usable(name))
                return "project name REFUSED: '" + (name ?? "") + "' - use 1-64 characters starting with a " +
                       "letter or digit, then letters, digits, '.', '_' or '-'; no path separators, no device names";
            return null;
        }

        /// <summary>What the panel offers before the author types. Never refused by NameRefusal, which is what
        /// makes the Ship button live the moment a slot resolves.</summary>
        internal static string DefaultName(string shippedAsset)
        {
            var spelled = new StringBuilder("Replace_");
            foreach (char c in shippedAsset ?? "")
                spelled.Append(Alnum(c) || c == '.' || c == '_' || c == '-' ? c : '_');
            string name = spelled.ToString();
            if (name.Length > 64) name = name.Substring(0, 64);
            // A cut can land on a '.', and a name ending in one is refused - so the cut trims its own tail
            // rather than handing the author a default the button will not accept.
            return name.TrimEnd('.', ' ');
        }

        /// <summary>The folder <see cref="AddMeshReplacement"/> would use, or null when the name or the mod
        /// folder makes one impossible. Spelled once, here, so the ship gate's catch-all can say whether
        /// that folder exists without re-deriving the path it never got back (R12).</summary>
        internal static string RootOf(string modDir, string name)
        {
            if (NameRefusal(name) != null || string.IsNullOrEmpty(modDir)) return null;
            // Path.GetFullPath THROWS on a ModDir no path can be made of, and the ship gate's catch-all
            // calls this while it is already handling a failure - a second exception out of the handler
            // would replace the refusal the author needs to read. "No root" is exactly what that is.
            try
            {
                DirectoryInfo mods = Directory.GetParent(Normalized(modDir));
                return mods == null ? null : Path.Combine(mods.FullName, name);
            }
            // Narrow on purpose: only the exceptions a PATH can raise mean "no root". Anything else -
            // an OOM, a security refusal - is a real failure and must not come back as a tidy null.
            catch (Exception bad) when (bad is ArgumentException || bad is NotSupportedException ||
                                        bad is PathTooLongException || bad is IOException)
            { return null; }
        }

        /// <summary>ModDir made absolute with its trailing separator off. Directory.GetParent("...\Mods\
        /// ContentTool\") answers "...\Mods\ContentTool", so one trailing separator would put the project
        /// UNDER ContentTool - exactly where the manager never discovers it (ModGate.Decide:38 -> Unknown) -
        /// and the post-condition below would accept it, because ContentMods.ProjectDir walks the same wrong
        /// parent. Normalized once, at the door, so every derivation downstream shares one spelling.</summary>
        private static string Normalized(string modDir)
        {
            return Path.GetFullPath(modDir)
                       .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }

        /// <summary>
        /// Add one mesh replacement to the project of that name, creating it when it does not exist.
        /// <paramref name="modDir"/> is ContentToolMain.ModDir; the project lands in its PARENT.
        /// </summary>
        /// <exception cref="InvalidDataException">R1, R2, R5, or R6/E3/E4 out of Manifest.Validate.</exception>
        /// <exception cref="IOException">R3, R4, or E5/E6 out of ManifestFile.Save.</exception>
        internal static Result AddMeshReplacement(string modDir, string name, string sourceGlb, string expectedSha,
                                                  string shippedBundle, string shippedAsset,
                                                  IDictionary<string, string> aliases)
        {
            string refusal = NameRefusal(name);
            if (refusal != null) throw new InvalidDataException(refusal);
            if (string.IsNullOrEmpty(sourceGlb) || !File.Exists(sourceGlb))
                throw new FileNotFoundException("the .glb to ship is not on disk", sourceGlb ?? "");
            if (string.IsNullOrEmpty(shippedBundle) || string.IsNullOrEmpty(shippedAsset))
                throw new InvalidDataException("no shipped target was derived for this slot, so there is no " +
                                               "row to write - pick the slot again");

            // THE MAP IS VETTED BEFORE ANYTHING IS WRITTEN, by the same judge that will READ it back.
            // SaveSidecar writes whatever it is handed, and LoadSidecar then refuses an empty name or two
            // file bones on one game bone (AliasMap.cs:212-218) - so an unvetted press leaves a sidecar the
            // bake silently drops. Of also NORMALIZES: what goes out is its ordinal copy, not the caller's
            // dictionary. Null here means "no map", which is R5's business further down.
            AliasMap vetted = AliasMap.Of(aliases);
            if (vetted == null && aliases != null && aliases.Count != 0)
                throw new InvalidDataException("the bone map sends two of the file's bones onto one of the " +
                                               "game's, or leaves a name empty, so nothing was written - the " +
                                               "bake would refuse the sidecar this press produced; fix the " +
                                               "map, then press Ship again");

            // Normalized THROWS on a ModDir no path can be made of, and this is the press an author drives:
            // a raw ArgumentException out of the Ship button names nothing, while "no root" is exactly what
            // that spelling means - the same answer RootOf's null already stands for.
            DirectoryInfo mods = null;
            if (!string.IsNullOrEmpty(modDir))
                try { modDir = Normalized(modDir); mods = Directory.GetParent(modDir); }
                catch (Exception bad) when (bad is ArgumentException || bad is NotSupportedException ||
                                            bad is PathTooLongException || bad is IOException)
                { }
            if (mods == null)
                throw new InvalidDataException("ContentTool's own mod folder is not known, so there is nowhere " +
                                               "beside it to put a project");

            var result = new Result();
            result.Root = Path.Combine(mods.FullName, name);
            result.ManifestPath = Path.Combine(result.Root, ContentMods.Manifest);
            result.MetaPath = Path.Combine(result.Root, "meta.json");
            string stem = Path.GetFileNameWithoutExtension(sourceGlb);
            // THE STEM IS A NAME TOO, and the name table above only ever saw the PROJECT's. This one becomes
            // the copy's file name and the row's "mesh" value: empty, it writes mesh:"" and comes back as
            // Manifest's E3 naming no cause the author can act on; a device name is a file Windows refuses
            // to create, and that failure reads like a bug in this tool.
            if (string.IsNullOrEmpty(stem))
                throw new InvalidDataException("'" + sourceGlb + "' has no name before its extension, so " +
                                               "there is no mesh name to write into the row - rename the " +
                                               "file, then press Ship again");
            int stemDot = stem.IndexOf('.');
            string bareStem = stemDot < 0 ? stem : stem.Substring(0, stemDot);
            foreach (string device in Devices)
                if (string.Equals(bareStem, device, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("'" + stem + ".glb' would be the copy under Content\\" +
                                                   "Meshes\\, and Windows reserves '" + bareStem + "' with " +
                                                   "or without an extension, so that file cannot be created " +
                                                   "- rename the file, then press Ship again");
            result.MeshPath = Path.Combine(result.Root, "Content", "Meshes", stem + ".glb");
            result.SidecarPath = AliasMap.SidecarPathOf(result.MeshPath);
            result.Created = !File.Exists(result.ManifestPath);

            // R2. Only a folder that already declares itself a project may be added to; anything else with
            // files in it belongs to someone, and this tool does not move into it.
            if (result.Created && Directory.Exists(result.Root) &&
                (Directory.GetFiles(result.Root).Length != 0 ||
                 Directory.GetDirectories(result.Root).Length != 0))
                throw new InvalidDataException("'" + result.Root + "' already exists, is not empty, and " +
                                               "holds no ppcontent.json, so it is not a ContentTool project " +
                                               "- pick another project name");

            // R14. Content\Meshes\ is resolved BY STEM (ContentProject.Sources), so this stem under any OTHER
            // supported mesh extension is not a second file - it is a collision that makes the bake SKIP BOTH,
            // and the author loses the mesh already shipped as well as the one being shipped. The extension
            // list is ContentProject's own, so a format added there can never go unguarded here.
            string meshDir = Path.GetDirectoryName(result.MeshPath);
            string shipping = Path.GetFileName(result.MeshPath);
            foreach (string pattern in ContentMods.MeshPatterns)
            {
                string twin = stem + pattern.Substring(1);
                if (string.Equals(twin, shipping, StringComparison.OrdinalIgnoreCase)) continue;
                if (File.Exists(Path.Combine(meshDir, twin)))
                    throw new InvalidDataException("Content\\Meshes\\" + twin + " is already there and a " +
                                                   "replacement names only the stem '" + stem + "', so " +
                                                   "shipping " + shipping + " beside it would make the bake " +
                                                   "SKIP BOTH - delete " + twin + ", or rename the file you " +
                                                   "are shipping");
            }

            // R3, AND IT COMES FIRST. The refusal says "nothing was written", so it has to be true: read and
            // hash the source before a directory, a template or a meta exists, and a press that fails here
            // leaves an author with no folder to delete. The Doctor's verdict was about THESE bytes; a
            // re-export between the green report and this press would ship a file nobody has read.
            byte[] bytes = File.ReadAllBytes(sourceGlb);
            string sha = AliasMap.Sha256(bytes);
            if (!string.Equals(sha, expectedSha, StringComparison.OrdinalIgnoreCase))
                throw new IOException("'" + sourceGlb + "' changed on disk after its green verdict, so nothing " +
                                      "was written - pick it again, read the report, then press Ship again");
            result.MeshBytes = bytes;

            Directory.CreateDirectory(result.Root);
            if (result.Created)
            {
                // The two keys ManifestFile.Load requires (E2) and nothing else: an authored project's own
                // "id" and "bundle" are never rewritten, so this shape is only ever the FIRST press's.
                var tree = new Dictionary<string, object>(StringComparer.Ordinal)
                {
                    { "id", name }, { "bundle", name + ".bundle" }
                };
                CreateNew(result.ManifestPath,
                          new UTF8Encoding(false).GetBytes(new JsonWriter().Val(tree).ToString() + "\n"));
            }

            // THE MANIFEST FIRST, and its ID rather than the folder name. "id == name" is true of a project
            // THIS tool made and of nothing else - an authored ppcontent.json keeps whatever id its author
            // chose - and a meta.json keyed on the folder name would then list one mod while every route
            // resolves another. Load is also the strict reader (E1/E2), so a manifest this tool cannot edit
            // safely stops the press before a meta is written beside it.
            ManifestFile file = ManifestFile.Load(result.ManifestPath);
            string id = file.Manifest.Id;
            if (File.Exists(result.MetaPath))
            {
                // R13. An existing meta is never rewritten and never trusted: PACKAGE'S own validator says
                // whether a player would end up with a working mod, so the wizard and the packager cannot
                // disagree. stagedFiles is null on purpose - nothing is staged yet, and that null is what
                // switches off MetaRefusal's AssemblyName arm (Package.cs:324).
                // MetaRefusal is REGEX-based, so an unclosed object that happens to hold a matching "ID" and
                // "Dependencies" sails through it while the game's own reader refuses the file. The strict
                // reader this codebase already has runs first; no second parser is grown for it.
                string text = File.ReadAllText(result.MetaPath);
                string said;
                try
                {
                    said = Json.Parse(text, 64) is Dictionary<string, object>
                               ? Package.MetaRefusal(text, null)
                               : "meta.json is not a JSON object.";
                }
                // Json's own sentence ends in advice meant for a glTF ("re-export it rather than editing
                // it by hand", Json.cs:142-145), which is wrong for a file the author is expected to fix
                // by hand; only the POSITION and the CAUSE it names carry over to a meta.json.
                catch (FormatException bad)
                {
                    string why = bad.Message;
                    int glb = why.LastIndexOf("; re-export", StringComparison.Ordinal);
                    if (glb > 0) why = why.Substring(0, glb);
                    int at = why.IndexOf("at character ", StringComparison.Ordinal);
                    said = "meta.json did not read as JSON " + (at > 0 ? why.Substring(at) : why) + ".";
                }
                if (said != null)
                    throw new InvalidDataException("'" + result.MetaPath + "' already exists but is not a " +
                                                   "mod this project can ship: " + said + " - fix that file, " +
                                                   "or ship into another project");
            }

            // IDEMPOTENT REUSE, not a refusal. A row that is EXACTLY this one is what the PREVIOUS press
            // left, and every "fix it and press Ship again" in the design walks straight into it - reading
            // that as R6 would make the retry the design promises impossible. R6 stays for a CONFLICTING
            // row (same target, different mesh), and it lands HERE, before the copy, so a press that cannot
            // add its row never leaves a .glb behind that nothing references.
            result.RowAlreadyPresent = Reuses(file.Manifest, shippedBundle, shippedAsset, stem);
            if (!result.RowAlreadyPresent)
                file.Manifest.AddMeshReplacement(shippedBundle, shippedAsset, stem);
            // Save validates too (Manifest.cs:320), but Save now happens AFTER the copy - and a press that
            // cannot add its row must not leave a .glb behind that nothing references. So R6 is asked for
            // here, explicitly, before the first byte of content moves - on BOTH arms. A row that is already
            // there proves nothing about the manifest's OTHER entries: one element Save refuses (a bare
            // number in "replace") used to be found only after the copy and the sidecar had landed.
            file.Manifest.Validate();

            // R5. A sidecar already beside the copy, with an empty map in hand, would be applied by the bake
            // and by nothing the author ever looked at. SaveSidecar rewrites the whole "bones" object, so the
            // only safe answers are "write mine" or "refuse".
            if (vetted == null && File.Exists(result.SidecarPath))
                throw new InvalidDataException(stem + ".glb.aliases.json already sits beside the copy but this " +
                                               "Doctor session has no bone map, so the bake would silently use " +
                                               "mappings you never saw - delete it, or set the map");

            Directory.CreateDirectory(Path.GetDirectoryName(result.MeshPath));
            result.MeshAlreadyPresent = CopyOrVerify(result.MeshPath, bytes, sha, stem);
            // Keyed on the COPY and on the COPY's sha, because that is the file the bake hashes
            // (AliasMap.LoadSidecar:196) - the source the author picked is gone from the story by now.
            if (vetted != null)
                AliasMap.SaveSidecar(result.MeshPath, sha, bytes.LongLength, vetted.Pairs);

            // THE SPLICE LAST, deliberately: a manifest row pointing at a mesh file that is not there yet is
            // the one half-written state a retry cannot fix by pressing again (design §7, stages 6-8). The
            // splice, the .bak and the E5 fingerprint are ManifestFile's; nothing outside the "replace" value
            // span moves - and with nothing pending, Save validates and writes NOTHING (Manifest.cs:321).
            file.Save();

            // THE META LAST, because it is the file that turns a folder into a MOD the manager lists. An R6
            // refusal or a failed Save above leaves the press with nothing added, and a meta.json written
            // before that decision would hand the author a mod id for a project that gained no row.
            if (!File.Exists(result.MetaPath))
                CreateNew(result.MetaPath, new UTF8Encoding(false).GetBytes(Meta(id)));

            // THE POST-CONDITION, asserted rather than assumed: this is what makes `ct_project <name>` and
            // `ct_route7 apply <name>` find the folder that was just written (ContentMods.Sibling:128).
            if (!string.Equals(ContentMods.ProjectDir(modDir, name), result.Root,
                               StringComparison.OrdinalIgnoreCase))
                throw new IOException("'" + result.Root + "' was written but ContentMods.ProjectDir still does " +
                                      "not resolve '" + name + "' to it, so a bake would read the wrong folder");
            return result;
        }

        /// <summary>The code-free content mod's meta.json, shaped like the shipped demo
        /// demos\MaterialTweak\meta.json, keyed on the MANIFEST's id. "AssemblyName" is omitted deliberately -
        /// ModMeta defaults it to string.Empty, ModRoster.AfterLoadMod supplies the content-only instance, and
        /// Package.MetaRefusal only objects when the field NAMES a file that is not in the package.
        /// "Dependencies" is what makes Phoenix Point's manager enable ContentTool for the player
        /// (Package.EngineId:35, MetaRefusal:319-322); without it the mod installs and silently does nothing.
        /// The id goes through JsonWriter rather than into a quoted hole: a NEW project's id is
        /// NameRefusal-limited to letters, digits, '.', '_' and '-', but an EXISTING project's came back
        /// DECODED from ManifestFile.Load, so it may hold a quote or a backslash that would end the file's
        /// JSON in the wrong place.</summary>
        private static string Meta(string id)
        {
            string quoted = new JsonWriter().Val(id).ToString();     // quoted AND escaped
            return "{\n  \"ID\": " + quoted + ",\n" +
                   "  \"Version\": \"1.0.0\",\n" +
                   "  \"Name\": [ { \"Key\": \"English\", \"Value\": " + quoted + " } ],\n" +
                   "  \"Dependencies\": [ \"" + Package.EngineId + "\" ]\n}\n";
        }

        /// <summary>True when the destination already held these exact bytes. R4 otherwise: the .glb under
        /// Content\Meshes\ is an authored input, and PatchCache.Key stamps it by path/size/mtime (:43/:49),
        /// so a same-size overwrite would be INVISIBLE to the freshness check and the player would keep being
        /// served last bake's copy.
        ///
        /// "Absent" is decided by the CREATE ITSELF, never by a File.Exists that another writer can falsify
        /// between the question and the write - see <see cref="CreateNew"/>. The loser of a race re-reads the
        /// winner and judges it by the same SHA, so two presses agree.</summary>
        private static bool CopyOrVerify(string meshPath, byte[] bytes, string sha, string stem)
        {
            if (CreateNew(meshPath, bytes)) return false;
            string have = AliasMap.Sha256(File.ReadAllBytes(meshPath));
            if (string.Equals(have, sha, StringComparison.OrdinalIgnoreCase)) return true;
            throw new IOException("Content\\Meshes\\" + stem + ".glb already holds DIFFERENT bytes (sha " +
                                  have + " vs " + sha + "), so it was NOT overwritten - rename the file you " +
                                  "are shipping, or ship into another project");
        }

        /// <summary>The absent-only writer for all three files that must NOT already exist - the mesh copy
        /// and the two templates. True when THIS call created it; false when someone else got there first,
        /// and the caller then reads the winner back rather than trusting it (the SHA for the copy,
        /// ManifestFile.Load for the manifest, Json.Parse + Package.MetaRefusal for the meta).
        ///
        /// WRITTEN WHOLE, THEN MOVED, so the destination never exists half-written: FileMode.CreateNew alone
        /// created the file at its final name and only then started copying into it, and a press killed at
        /// that moment left a truncated .glb that every later press met as R4 forever - unrecoverable by
        /// pressing again, which is the one state this whole class is arranged to avoid. The temp is a GUID
        /// in the SAME directory (a cross-volume Move is a copy, not a rename), and File.Move with no
        /// overwrite flag is the stdlib's own atomic create-or-throw. Anything that is not that collision -
        /// a full disk, a denied write - propagates, so a failure is never read as "it was already there".
        /// The temp goes either way; a leftover .tmp is inert, since nothing here ever scans the folder.</summary>
        private static bool CreateNew(string path, byte[] bytes)
        {
            string temp = Path.Combine(Path.GetDirectoryName(path), Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                File.WriteAllBytes(temp, bytes);
                try { File.Move(temp, path); }
                catch (IOException) when (File.Exists(path)) { return false; }
                return true;
            }
            finally { try { File.Delete(temp); } catch (IOException) { } }
        }

        /// <summary>Does the project ALREADY declare exactly this replacement? Each field folded the way the
        /// thing that will READ it folds: the bundle case-blind (ProjectBake.cs:1534, Manifest.Validate:203),
        /// the asset ORDINAL (shipped names are folded nowhere), the mesh stem case-blind because
        /// ProjectBake.FindMesh:2152 resolves it that way and two spellings are one file on Windows.</summary>
        private static bool Reuses(Manifest manifest, string bundle, string asset, string stem)
        {
            foreach (ReplaceRow row in manifest.Replace)
                if (row.Kind == "mesh" &&
                    string.Equals(row.Bundle, bundle, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(row.Asset, asset, StringComparison.Ordinal) &&
                    string.Equals(row.Mesh, stem, StringComparison.OrdinalIgnoreCase))
                    return true;
            return false;
        }

        private static bool Usable(string name)
        {
            if (string.IsNullOrEmpty(name) || name.Length > 64) return false;
            if (!Alnum(name[0])) return false;
            foreach (char c in name)
                if (!Alnum(c) && c != '.' && c != '_' && c != '-') return false;
            // ' ' is already out by the loop above; the trailing '.' is not, and Windows silently strips it,
            // so "Foo." and "Foo" would be one folder under two names.
            if (name[name.Length - 1] == '.') return false;
            string bare = name;
            int dot = bare.IndexOf('.');
            if (dot >= 0) bare = bare.Substring(0, dot);
            foreach (string device in Devices)
                if (string.Equals(bare, device, StringComparison.OrdinalIgnoreCase)) return false;
            return true;
        }

        /// <summary>ASCII only, deliberately: char.IsLetterOrDigit would accept a name whose spelling depends
        /// on the machine's code page once it becomes a folder, a mod ID and a bundle stem.</summary>
        private static bool Alnum(char c)
        {
            return (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9');
        }
    }
}
