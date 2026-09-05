using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace Morgott.ContentTool.Project
{
    /// <summary>
    /// Turns an AUTHOR FOLDER into a folder that can be zipped and uploaded - and refuses to produce
    /// one that carries Phoenix Point's own data.
    ///
    /// TWO CALLERS, ONE BODY. `ct_package &lt;project&gt;` in the developer console is the one a modder
    /// needs, because it means no external script; package.ps1 -> tools\Package is ours, and runs
    /// with the game shut. This class - plain System.IO, no UnityEngine type - is what both call, so
    /// the rule that decides what may ship cannot drift between them. Neither caller COMPILES: the
    /// author's DLL is the author's own business (see <see cref="BuiltAssembly"/>).
    ///
    /// WHAT IT DOES NOT DO: bake. The mod's own bundle and its banks are produced by ct_project /
    /// ct_sound bake INSIDE the game (they need Unity's texture decoder and the player's own install),
    /// so this copies what those verbs already wrote into the project's Dist\ and refuses a package
    /// that carries no payload at all - see <see cref="Ships"/> for what counts as one, and why the
    /// answer is not "a Content\ or a Dist\ folder".
    ///
    /// THE REFUSAL IS THE POINT. A patched shipped bundle is Phoenix Point's data with a few of the
    /// author's bytes in it. Route7.ApplyProject builds those on the PLAYER's machine, out of the
    /// PLAYER's installation, into their own AppData - so they never have to be, and must never be,
    /// redistributed. Everything below that names one is refused by name and the staged folder is
    /// deleted rather than half-shipped.
    /// </summary>
    internal static class Package
    {
        /// <summary>The engine mod every content mod must declare, or the player installs a mod that
        /// silently does nothing (PP's manager auto-enables a declared dependency, ModEntry.cs:53-63).</summary>
        internal const string EngineId = "com.morgott.ContentTool";

        /// <summary>
        /// What a player needs, and nothing else. An allowlist rather than "copy the folder minus a
        /// few things": src\, tools\, bin\, obj\, .git\ and the runtime WwiseAudio\ extraction cache
        /// are all things a release must not carry, and the next one nobody thought of is excluded by
        /// default this way round.
        /// </summary>
        private static readonly string[] Shipped =
        {
            "meta.json", "ppcontent.json", "README.md", "SOURCES.md", "LICENSE", "LICENSE.md",
            "Content", "Icons", "Dist"
        };

        /// <summary>Where an author keeps the replacement audio `ct_sound bake` reads (mirrors
        /// SoundReplace's own ReplaceDir), and where that bake writes the bank it produces
        /// (SoundReplace.ShippedBanks). Restated rather than referenced: this file is deliberately
        /// free of UnityEngine types so tools\Package can compile it alone.</summary>
        private const string ReplaceSources = "Content\\Audio\\Replace";
        private const string ShippedBanks = "Dist\\Sounds";

        /// <summary>
        /// Stages <paramref name="authorDir"/> into <paramref name="outDir"/> and validates the
        /// result. <paramref name="assembly"/> is the mod's own built DLL when it has one (package.ps1
        /// builds it), null for a content-only mod.
        /// </summary>
        internal static string Run(string authorDir, string outDir, string assembly, out bool ok)
        {
            ok = false;
            if (string.IsNullOrEmpty(authorDir) || !Directory.Exists(authorDir))
                return "REFUSED: no author folder at '" + authorDir + "'.";
            if (string.IsNullOrEmpty(outDir))
                return "REFUSED: name the folder to write the package into.";

            string meta = Path.Combine(authorDir, "meta.json");
            string manifest = Path.Combine(authorDir, "ppcontent.json");
            if (!File.Exists(meta))
                return "REFUSED: there is no " + meta + ". Phoenix Point's mod manager lists a folder " +
                       "only when it holds a meta.json, so without one nobody can install this.";
            if (!File.Exists(manifest))
                return "REFUSED: there is no " + manifest + ". That file is what tells ContentTool " +
                       "what this mod replaces, publishes or adds.";
            if (Directory.Exists(outDir) && Directory.GetFileSystemEntries(outDir).Length > 0)
                return "REFUSED: " + outDir + " already holds files. Name a folder that does not exist " +
                       "yet - a package is built from nothing, so no leftover of a previous run can be " +
                       "shipped by accident.";

            string manifestText = File.ReadAllText(manifest);
            // A MANIFEST NOBODY CAN READ IS A MOD THAT DOES NOTHING. A zero-byte or half-typed file
            // declares no rung, matches no regex and parses into no bundle, so without this gate it
            // sails through as a package that installs and sits there. The runtime reader is the one
            // that would refuse it - on the player's machine, hours later. Balanced braces are all
            // this check asks: it runs BEFORE OwnBundle/ReplaceTargets parse, and those two answer
            // "nothing declared" for a manifest that is merely balanced rather than valid.
            if (manifestText.Trim().Length == 0 || Depth(manifestText, manifestText.Length) != 0)
                return "REFUSED: " + manifest + " is EMPTY OR NOT VALID JSON - its braces and brackets " +
                       "do not close. ContentTool reads that file to learn what this mod replaces, " +
                       "publishes or adds, so a package built from it would install and do nothing. " +
                       "Fix the file, then package again.";

            try
            {
                Directory.CreateDirectory(outDir);
                foreach (string item in Shipped)
                {
                    string from = Path.Combine(authorDir, item);
                    if (File.Exists(from)) File.Copy(from, Path.Combine(outDir, item), true);
                    else if (Directory.Exists(from)) CopyDir(from, Path.Combine(outDir, item));
                }
                if (!string.IsNullOrEmpty(assembly) && File.Exists(assembly))
                    File.Copy(assembly, Path.Combine(outDir, Path.GetFileName(assembly)), true);
            }
            catch (Exception copy)
            {
                // HALF A PACKAGE POISONS EVERY LATER RUN. The refusal path below deletes outDir for
                // exactly this reason; an IO error mid-copy used to escape instead, leaving a folder
                // that the "already holds files" check above then refuses forever.
                try { if (Directory.Exists(outDir)) Directory.Delete(outDir, true); } catch { }
                return "REFUSED: STAGING FAILED while copying into " + outDir + " - " + copy.Message +
                       " The half-written folder has been deleted rather than left behind, so this " +
                       "command still works once the file is free (close whatever holds it open) and " +
                       "no leftover of a broken run can be shipped by accident.";
            }

            long saved = 0;
            List<string> unbaked;
            List<string> dropped = BakedAlready(outDir, manifestText, out unbaked);
            foreach (string rel in dropped)
            {
                string staged = Path.Combine(outDir, rel);
                saved += new FileInfo(staged).Length;
                File.Delete(staged);
            }
            PruneEmpty(Path.Combine(outDir, ReplaceSources), outDir);

            List<string> files = Relative(outDir);
            List<string> refusals = new List<string>();
            string said = MetaRefusal(File.ReadAllText(meta), files);
            if (said != null) refusals.Add(said);
            refusals.AddRange(Refusals(outDir, OwnBundle(manifestText), ReplaceTargets(manifestText)));
            if (!Ships(files, manifestText))
                refusals.Add("this package ships nothing at all - no asset file, no assembly, and a " +
                             "ppcontent.json that declares no \"replace\", \"publish\", \"sounds\", " +
                             "\"creature\" or \"weapons\" row. Either the bake has not been run " +
                             "('ct_project <YourMod>', 'ct_sound bake <YourMod>'), or the manifest " +
                             "never declared what this mod does.");
            // Everything above is a "you are shipping the game's data" refusal; the sound one below is
            // not, and the preamble that explains redistribution would be a lie in front of it alone.
            bool redistribution = refusals.Count > 0;

            // A SOUND REPLACEMENT THAT WAS NEVER BAKED SHIPS DEAD, AND SILENTLY.
            //
            // The player's game reads exactly one thing for a replacement: Dist\Sounds\<mediaId>.bnk,
            // loaded at ContentTool's init (SoundLoad). Content\Audio\Replace\ is the AUTHOR's source
            // folder and is never opened on the player's machine - so a source with no bank beside it
            // is a mod that installs, enables, reports nothing wrong and plays the shipped sound.
            // That is not the same case as a project with Content\ and no Dist\ on the BUNDLE route,
            // where Route7.ApplyProject really does read Content\ on the player's machine; there the
            // source IS the shipping form, and here it is not.
            foreach (string rel in unbaked)
                refusals.Add(rel + " - a sound replacement that was NEVER BAKED. The player's game " +
                             "loads only " + ShippedBanks + "\\<mediaId>.bnk; it never opens " +
                             ReplaceSources + ". Without that bank this mod installs, enables and " +
                             "plays the shipped sound, with nothing anywhere saying why. Run " +
                             "'ct_sound bake <YourMod>' in game, then package again.");

            if (refusals.Count > 0)
            {
                Directory.Delete(outDir, true);
                StringBuilder bad = new StringBuilder();
                bad.Append("REFUSED - this package is NOT publishable, and ").Append(outDir)
                   .AppendLine(" has been deleted rather than half-written.");
                if (redistribution)
                    bad.AppendLine("Phoenix Point's own data must never be redistributed. A patched bundle is " +
                                   "the game's own file with a few of your bytes in it: ContentTool builds those " +
                                   "ON THE PLAYER'S MACHINE, out of the player's own installation, into their " +
                                   "AppData (Route7.ApplyProject) - which is exactly why no release, Workshop " +
                                   "item or zip has to contain one.");
                foreach (string r in refusals) bad.Append("  REFUSED: ").AppendLine(r);
                return bad.ToString().TrimEnd();
            }

            long bytes = 0;
            foreach (string f in files) bytes += new FileInfo(Path.Combine(outDir, f)).Length;
            ok = true;
            return "PACKAGED " + files.Count + " file(s), " + bytes + " B into " + outDir +
                   (dropped.Count == 0 ? "" :
                    "\nLEFT BEHIND " + dropped.Count + " source file(s), " + saved + " B: " +
                    string.Join(", ", dropped.ToArray()) + " - ct_sound bake already turned each of " +
                    "those into a " + ShippedBanks + " bank, which is what the player's game loads. " +
                    "They stay in your project; only the release does without them.") +
                   // ZIP THE FOLDER, not its contents. Both layouts install correctly if the player
                   // unzips to the matching place, but only one survives the thing players actually
                   // do: drop the archive into Mods\ and extract here. A contents-rooted zip then
                   // lands meta.json in Mods\ itself, where the loader - which discovers only
                   // top-level DIRECTORIES holding a meta.json - never sees the mod at all.
                   "\nZip the FOLDER itself, so the archive holds " + Path.GetFileName(outDir.TrimEnd('\\', '/')) +
                   "\\meta.json, and upload it. The player unzips it into Mods\\ (ending up with " +
                   "Mods\\<YourMod>\\meta.json) or subscribes on the Workshop; the mod manager enables " +
                   "ContentTool for them because meta.json declares it.";
        }

        /// <summary>
        /// The mod's OWN built DLL inside the author folder, or null - what `ct_package` hands
        /// <see cref="Run"/> as its <c>assembly</c>.
        ///
        /// IT PICKS ONE UP, IT DOES NOT BUILD ONE. Compiling is the author's own business: a modder
        /// writing C# already has Visual Studio or Rider open and a built DLL on disk, and a
        /// content-only mod has no code at all. So this looks for exactly the file meta.json names -
        /// newest copy, anywhere under the project, which finds both bin\Release\net472\&lt;name&gt;.dll
        /// and a DLL simply dropped in the project root - and answers null for everything else.
        ///
        /// A DECLARED ASSEMBLY THAT IS NOWHERE IS DELIBERATELY NOT HANDLED HERE. Run's MetaRefusal
        /// already refuses that package BY NAME and says to build it; a second opinion here would
        /// only get to say it worse.
        /// </summary>
        internal static string BuiltAssembly(string authorDir)
        {
            if (string.IsNullOrEmpty(authorDir) || !Directory.Exists(authorDir)) return null;
            string meta = Path.Combine(authorDir, "meta.json");
            if (!File.Exists(meta)) return null;
            string dll = Json(File.ReadAllText(meta), "AssemblyName");
            if (string.IsNullOrEmpty(dll)) return null;

            string newest = null;
            foreach (string f in Directory.GetFiles(authorDir, dll, SearchOption.AllDirectories))
                if (newest == null || File.GetLastWriteTimeUtc(f) > File.GetLastWriteTimeUtc(newest))
                    newest = f;
            return newest;
        }

        /// <summary>
        /// Every reason this staged folder may not ship. Each names the offending file, because a
        /// count would leave the author guessing which of 300 files is the problem.
        ///
        /// This one does NOT ask whether the package ships anything - see <see cref="Ships"/>, which
        /// needs the manifest and so lives beside the caller that has it.
        ///
        /// The four categories are the four ways Phoenix Point's own bytes reach a package:
        /// a Patched\ folder (a patched copy of a shipped bundle), a .bundle that is not the mod's
        /// own, the .ct-backup/.ct-new an older ContentTool left inside the installation, and the
        /// catalog / .ct-edits ledger that went with them.
        /// </summary>
        internal static List<string> Refusals(string dir, string ownBundle, IList<string> replaceTargets)
        {
            List<string> refusals = new List<string>();
            foreach (string rel in Relative(dir))
            {
                string name = Path.GetFileName(rel), ext = Path.GetExtension(rel).ToLowerInvariant();

                if (HasSegment(rel, "Patched"))
                    refusals.Add(rel + " - a PATCHED COPY of a Phoenix Point bundle. ContentTool bakes " +
                                 "these on the player's machine from the player's own game files when the " +
                                 "mod is first enabled; delete the Patched folder from your project.");
                else if (ext == ".bundle" && !string.Equals(name, ownBundle, StringComparison.OrdinalIgnoreCase))
                    refusals.Add(rel + " - a SHIPPED PHOENIX POINT BUNDLE IDENTITY. This package may " +
                                 "carry exactly one bundle, your own '" + (ownBundle ?? "(none declared)") +
                                 "'" + (Names(replaceTargets, name)
                                        ? ", and your ppcontent.json \"replace\" names this one as a TARGET - " +
                                          "targets are patched on the player's machine, never shipped."
                                        : "."));
                else if (ext == ".ct-backup" || ext == ".ct-new")
                    refusals.Add(rel + " - an INSTALL BACKUP an older ContentTool left inside the game " +
                                 "folder. It is a copy of a Phoenix Point file; it belongs in nobody's mod.");
                else if (ext == ".ct-edits" || string.Equals(name, "catalog.json", StringComparison.OrdinalIgnoreCase))
                    refusals.Add(rel + " - an EDIT LEDGER or the game's own catalog. ContentTool writes " +
                                 "nothing into the installation any more, and a package that carries one of " +
                                 "these ships the game's own file.");
            }
            return refusals;
        }

        /// <summary>
        /// Whether this staged package carries anything a player would get out of installing it.
        ///
        /// THE OLD RULE WAS "there is a Content\ or a Dist\ folder", AND IT REFUSED REAL MODS. Two
        /// shapes the recipes explicitly bless have neither: a MATERIAL TWEAK, which is a few numbers
        /// in ppcontent.json and no file at all, and a WEAPON WITH NO MODEL OF ITS OWN, which is a
        /// manifest plus a DLL - "model" is optional, and an entry without one keeps the SkinData of
        /// the weapon it cloned. Both install, both work, and the only documented builder deleted
        /// their staging and told them to bake something that does not exist.
        ///
        /// So the question is not "is there a folder" but "is there a PAYLOAD", and a payload is
        /// either a staged file that is not paperwork - anything under Content\, Dist\ or Icons\, and
        /// the mod's own assembly - or a manifest that declares one of the rungs. A project with
        /// neither genuinely ships nothing and is still refused: an empty ppcontent.json beside a
        /// meta.json is a folder a player installs for no effect.
        ///
        /// A DECLARED RUNG IS ONLY A PAYLOAD WHEN IT HAS AN ENTRY IN IT. "weapons": [] and
        /// "replace": {} declare nothing and do nothing, so the collection must be seen to hold at
        /// least one non-space character before its closing bracket - otherwise the empty rung
        /// escapes a refusal whose own text says a row is required.
        /// </summary>
        internal static bool Ships(IList<string> stagedFiles, string manifestText)
        {
            if (stagedFiles != null)
                foreach (string rel in stagedFiles)
                    if (!IsPaperwork(Path.GetFileName(rel))) return true;
            return Regex.IsMatch(manifestText ?? "",
                                 "\"(replace|publish|sounds|creature|weapons)\"\\s*:\\s*" +
                                 "(\\[\\s*[^\\s\\]]|\\{\\s*[^\\s}])");
        }

        /// <summary>The staged files that are ABOUT the mod rather than part of it. Everything else -
        /// a texture, a bundle, a bank, an icon, the assembly - is content a player receives.</summary>
        private static bool IsPaperwork(string name)
        {
            foreach (string paper in new[] { "meta.json", "ppcontent.json", "README.md", "SOURCES.md",
                                             "LICENSE", "LICENSE.md" })
                if (string.Equals(name, paper, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        /// <summary>
        /// What meta.json has to say for an ORDINARY PLAYER to end up with a working mod (M3): an ID
        /// the manager can list, the ContentTool dependency the manager uses to auto-enable the engine,
        /// and - when the mod declares an assembly - that assembly actually being in the package.
        /// </summary>
        internal static string MetaRefusal(string metaText, IList<string> stagedFiles)
        {
            if (string.IsNullOrEmpty(metaText)) return "meta.json is empty.";
            string id = Json(metaText, "ID");
            if (string.IsNullOrEmpty(id))
                return "meta.json declares no \"ID\" - the mod manager keys every mod on it.";
            if (!Regex.IsMatch(metaText, "\"Dependencies\"\\s*:\\s*\\[[^\\]]*\"" + Regex.Escape(EngineId) + "\""))
                return "meta.json does not declare \"Dependencies\": [ \"" + EngineId + "\" ] - without it " +
                       "the player can install this mod with the engine switched off and it will silently " +
                       "do nothing. With it, Phoenix Point enables ContentTool for them.";
            string dll = Json(metaText, "AssemblyName");
            if (!string.IsNullOrEmpty(dll) && stagedFiles != null && !stagedFiles.Contains(dll))
                return "meta.json declares \"AssemblyName\": \"" + dll + "\" but the package does not " +
                       "contain that file - the game refuses to load the mod. Build it, or set " +
                       "\"AssemblyName\": \"\" for a content-only mod.";
            return null;
        }

        /// <summary>
        /// The staged files a release does NOT have to carry, because the artefact baked FROM each of
        /// them is already in the package. Relative paths, sorted, so the caller can drop them and say
        /// by name what it left behind.
        ///
        /// THE PREDICATE IS NOT "a source, and something was baked". It is: THIS source has a NAMED
        /// baked artefact that stands in for it AT RUNTIME. Exactly one pair in the tool has that
        /// shape - `ct_sound bake` reads Content\Audio\Replace\&lt;source&gt; on the AUTHOR's machine and
        /// writes Dist\Sounds\&lt;mediaId&gt;.bnk, and the player's game then loads the bank and never opens
        /// the source (SoundLoad). Shipping both ships the same seconds of audio twice: demos\MenuMusic
        /// carried 8 MB of .mp3 behind 49 MB of banks that already contained it.
        ///
        /// EVERY OTHER SOURCE STILL SHIPS, and not by omission. A texture or mesh under Content\ is
        /// read on the PLAYER's machine - Route7.ApplyProject patches the player's own bundle out of it
        /// when the mod is first enabled - and a served video is streamed out of Content\Videos\ for
        /// the whole run. A mod's own Dist\&lt;name&gt;.bundle is not "the baked form of" any one of those
        /// files, so it can never license dropping one.
        /// </summary>
        internal static List<string> BakedAlready(string dir, string manifestText)
        {
            List<string> unbaked;
            return BakedAlready(dir, manifestText, out unbaked);
        }

        /// <summary>
        /// The same walk, also reporting the sources that name a media and have NO bank - the ones a
        /// release would ship DEAD. See <see cref="Run"/> for why that is a refusal.
        /// </summary>
        internal static List<string> BakedAlready(string dir, string manifestText, out List<string> unbaked)
        {
            List<string> drop = new List<string>();
            unbaked = new List<string>();
            string sources = Path.Combine(dir, ReplaceSources);
            if (!Directory.Exists(sources)) return drop;

            Dictionary<string, string> declared = DeclaredSounds(manifestText);
            foreach (string f in Directory.GetFiles(sources))
            {
                string name = Path.GetFileName(f);
                // Both ways a source names its media, in SoundReplace's own order of precedence: a
                // "sounds" declaration keeps the author's filename, and the bare <mediaId>.ext
                // convention is the lazy way to do one file.
                //
                // A DECLARED ROW IS JUDGED WHATEVER ITS EXTENSION. The whitelist below is about
                // GUESSING - a stray .txt or .reaper next to the tracks is not a source this rule has
                // any opinion about - but the author who wrote "hit.flac" into "sounds" said this file
                // is a replacement, and one the bake never turned into a bank ships just as dead as a
                // .wav would. Filtering by extension first let exactly that one out of the refusal.
                string media;
                if (!declared.TryGetValue(name, out media))
                {
                    // The same whitelist Content\Audio\ takes (SoundReplace.Sources).
                    string ext = Path.GetExtension(name).ToLowerInvariant();
                    if (ext != ".wav" && ext != ".ogg" && ext != ".mp3") continue;
                    uint id;
                    if (!uint.TryParse(Path.GetFileNameWithoutExtension(name), out id)) continue;
                    media = id.ToString();
                }
                if (File.Exists(Path.Combine(Path.Combine(dir, ShippedBanks), media + ".bnk")))
                    drop.Add(Path.Combine(ReplaceSources, name));
                else
                    unbaked.Add(Path.Combine(ReplaceSources, name) + " (media " + media + ")");
            }
            drop.Sort(StringComparer.OrdinalIgnoreCase);
            unbaked.Sort(StringComparer.OrdinalIgnoreCase);
            return drop;
        }

        /// <summary>
        /// The "sounds" array as file name -> media ID. Read the way ContentProject.ParseSounds reads
        /// it, and deliberately SILENT on a malformed entry: the runtime reader refuses one by name at
        /// bake time, and a packager that threw here would turn a typo into a crash instead of a
        /// release that merely carries one extra file.
        /// </summary>
        private static Dictionary<string, string> DeclaredSounds(string manifestText)
        {
            Dictionary<string, string> byFile = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            Match arr = Regex.Match(manifestText ?? "", "\"sounds\"\\s*:\\s*\\[(.*?)\\]", RegexOptions.Singleline);
            if (!arr.Success) return byFile;
            foreach (Match o in Regex.Matches(arr.Groups[1].Value, "\\{[^{}]*\\}", RegexOptions.Singleline))
            {
                Match media = Regex.Match(o.Value, "\"media\"\\s*:\\s*\"?(\\d+)\"?");
                string file = Json(o.Value, "file");
                if (media.Success && !string.IsNullOrEmpty(file)) byFile[file] = media.Groups[1].Value;
            }
            return byFile;
        }

        /// <summary>Deletes <paramref name="leaf"/> and its parents, up to but never including
        /// <paramref name="stop"/>, for as long as they are empty - so dropping the last source leaves
        /// no hollow Content\Audio\Replace\ tree in the release.</summary>
        private static void PruneEmpty(string leaf, string stop)
        {
            string end = Path.GetFullPath(stop).TrimEnd('\\', '/');
            string dir = Directory.Exists(leaf) ? Path.GetFullPath(leaf).TrimEnd('\\', '/') : null;
            while (dir != null && !string.Equals(dir, end, StringComparison.OrdinalIgnoreCase) &&
                   Directory.Exists(dir) && Directory.GetFileSystemEntries(dir).Length == 0)
            {
                string parent = Path.GetDirectoryName(dir);
                Directory.Delete(dir);
                dir = string.IsNullOrEmpty(parent) ? null : parent.TrimEnd('\\', '/');
            }
        }

        /// <summary>The mod's OWN bundle: the "bundle" property of the ROOT object, as opposed to the ones
        /// nested inside "replace" entries (the shipped targets). Read from the parsed tree, so property
        /// ORDER cannot change the answer (S14-order-blind) and a "bundle" key inside any other nested
        /// block is mistaken for neither.</summary>
        internal static string OwnBundle(string manifestText)
        {
            try { return Manifest.Parse(manifestText).Bundle; }
            catch (InvalidDataException) { return null; }
        }

        /// <summary>The SHIPPED bundles the project declares as replacement targets - named in the refusal,
        /// so an author who dropped one in sees why that file is the problem. A manifest that will not
        /// PARSE declares no target here; Package.cs:87 is a coarser gate that refuses only a manifest
        /// whose braces and brackets do not close, not one that fails to parse.</summary>
        internal static List<string> ReplaceTargets(string manifestText)
        {
            List<string> targets = new List<string>();
            try
            {
                foreach (ReplaceRow row in Manifest.Parse(manifestText).Replace)
                    if (!string.IsNullOrEmpty(row.Bundle) && !targets.Contains(row.Bundle))
                        targets.Add(row.Bundle);
            }
            catch (InvalidDataException) { }
            return targets;
        }

        /// <summary>
        /// How deeply nested the text at <paramref name="at"/> sits: 0 at the end of a balanced text.
        ///
        /// The ONE remaining caller is <see cref="Run"/>'s balanced-brace gate (`:87`), which runs before
        /// anything parses and refuses a manifest whose braces and brackets do not close. The bundle
        /// readers no longer use it: they read the parsed tree, where nesting is structure rather than a
        /// count of characters.
        /// </summary>
        private static int Depth(string text, int at)
        {
            int depth = 0;
            bool inString = false;
            for (int i = 0; i < at; i++)
            {
                char c = text[i];
                if (inString)
                {
                    if (c == '\\') i++;
                    else if (c == '"') inString = false;
                }
                else if (c == '"') inString = true;
                else if (c == '{' || c == '[') depth++;
                else if (c == '}' || c == ']') depth--;
            }
            return depth;
        }

        private static bool Names(IList<string> targets, string file)
        {
            if (targets == null) return false;
            foreach (string t in targets) if (string.Equals(t, file, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        /// <summary>A path segment, not a substring: "Unpatched\x" is not a Patched folder.</summary>
        private static bool HasSegment(string rel, string segment)
        {
            foreach (string part in rel.Replace('\\', '/').Split('/'))
                if (string.Equals(part, segment, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        private static string Json(string text, string field)
        {
            return Regex.Match(text, "\"" + field + "\"\\s*:\\s*\"([^\"]*)\"").Groups[1].Value;
        }

        /// <summary>Every file under <paramref name="dir"/>, relative and sorted, so the refusals read
        /// in a stable order and the count is the count.</summary>
        internal static List<string> Relative(string dir)
        {
            List<string> rel = new List<string>();
            if (!Directory.Exists(dir)) return rel;
            int cut = dir.TrimEnd('\\', '/').Length + 1;
            foreach (string f in Directory.GetFiles(dir, "*", SearchOption.AllDirectories))
                rel.Add(f.Substring(cut));
            rel.Sort(StringComparer.OrdinalIgnoreCase);
            return rel;
        }

        /// <summary>Is this one of ContentTool's OWN half-written leftovers - the temp
        /// <see cref="ProjectScaffold"/> writes whole and then Moves into place, named
        /// <c>Guid.ToString("N") + ".tmp"</c>?
        ///
        /// SPELLED HERE, not in ProjectScaffold: tools\Package and tests\TargetPathTests link Package.cs
        /// ALONE, so the dependency can only run this way. Both callers need the same answer - the packager
        /// must not ship one, and the scaffold's emptiness scan must not count one - and a second spelling
        /// would let them drift.
        ///
        /// THE EXTENSION ALONE IS NOT THE SIGNATURE. Authors and their tools write .tmp files too; a folder
        /// holding only "artist-recovery.tmp" is someone's work, and reading it as empty moved the scaffold
        /// into it. The GUID is what makes the name this tool's.</summary>
        internal static bool IsOwnTemp(string path)
        {
            Guid ours;
            return string.Equals(Path.GetExtension(path), ".tmp", StringComparison.OrdinalIgnoreCase) &&
                   Guid.TryParseExact(Path.GetFileNameWithoutExtension(path), "N", out ours);
        }

        private static void CopyDir(string from, string to)
        {
            Directory.CreateDirectory(to);
            // Content\ is copied VERBATIM, so a press killed between the temp and its Move used to ship its
            // half-written bytes inside the release zip.
            foreach (string f in Directory.GetFiles(from))
                if (!IsOwnTemp(f)) File.Copy(f, Path.Combine(to, Path.GetFileName(f)), true);
            foreach (string d in Directory.GetDirectories(from))
                CopyDir(d, Path.Combine(to, Path.GetFileName(d)));
        }
    }
}
