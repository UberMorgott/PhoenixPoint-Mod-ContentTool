using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Morgott.ContentTool.Import;
using Morgott.ContentTool.IO;

namespace Morgott.ContentTool.Project
{
    /// <summary>
    /// ONE PRESS, ONE MOD FOLDER. Turns a green Doctor verdict into a real project beside Mods\ContentTool\:
    /// ppcontent.json + meta.json + Content\Meshes\&lt;stem&gt;.glb + its alias sidecar, with one "replace" row
    /// added to whatever the author already had.
    ///
    /// It AUTHORS nothing itself. Every byte goes out through ManifestFile.Save (atomic splice, .bak, the E5
    /// fingerprint), AtomicFile and AliasMap.SaveSidecar, which is why an existing project's own formatting,
    /// key order and unknown keys survive a press by construction.
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
            /// question of whether the two are the same bytes. Null until Task 3.</summary>
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
            DirectoryInfo mods = Directory.GetParent(Normalized(modDir));
            return mods == null ? null : Path.Combine(mods.FullName, name);
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

            if (!string.IsNullOrEmpty(modDir)) modDir = Normalized(modDir);
            DirectoryInfo mods = string.IsNullOrEmpty(modDir) ? null : Directory.GetParent(modDir);
            if (mods == null)
                throw new InvalidDataException("ContentTool's own mod folder is not known, so there is nowhere " +
                                               "beside it to put a project");

            var result = new Result();
            result.Root = Path.Combine(mods.FullName, name);
            result.ManifestPath = Path.Combine(result.Root, ContentMods.Manifest);
            result.MetaPath = Path.Combine(result.Root, "meta.json");
            string stem = Path.GetFileNameWithoutExtension(sourceGlb);
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

            Directory.CreateDirectory(result.Root);
            if (result.Created)
            {
                // The two keys ManifestFile.Load requires (E2) and nothing else: an authored project's own
                // "id" and "bundle" are never rewritten, so this shape is only ever the FIRST press's.
                var tree = new Dictionary<string, object>(StringComparer.Ordinal)
                {
                    { "id", name }, { "bundle", name + ".bundle" }
                };
                AtomicFile.WriteText(result.ManifestPath,
                                     new JsonWriter().Val(tree).ToString() + "\n", new UTF8Encoding(false));
            }

            // THE MANIFEST FIRST, and its ID rather than the folder name. "id == name" is true of a project
            // THIS tool made and of nothing else - an authored ppcontent.json keeps whatever id its author
            // chose - and a meta.json keyed on the folder name would then list one mod while every route
            // resolves another. Load is also the strict reader (E1/E2), so a manifest this tool cannot edit
            // safely stops the press before a meta is written beside it.
            ManifestFile file = ManifestFile.Load(result.ManifestPath);
            string id = file.Manifest.Id;
            if (!File.Exists(result.MetaPath))
                AtomicFile.WriteText(result.MetaPath, Meta(id), new UTF8Encoding(false));
            else
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
                catch (FormatException bad) { said = "meta.json did not read as JSON: " + bad.Message; }
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
            {
                file.Manifest.AddMeshReplacement(shippedBundle, shippedAsset, stem);
                file.Manifest.Validate();
            }
            // The splice, the .bak and the E5 fingerprint are ManifestFile's; nothing outside the "replace"
            // value span moves - and with nothing pending, Save validates and writes NOTHING (Manifest.cs:321).
            file.Save();

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
