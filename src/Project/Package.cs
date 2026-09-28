using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Morgott.ContentTool.Import;
using Morgott.ContentTool.IO;

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
                return Bake.StageText.PackageRefused(outDir);    // ONE copy of the refusal, in StageText

            string manifestText = File.ReadAllText(manifest);
            // A MANIFEST NOBODY CAN READ IS A MOD THAT DOES NOTHING. A zero-byte or half-typed file
            // declares no rung and parses into no bundle, so without this gate it sails through as a
            // package that installs and sits there. The runtime reader is the one that would refuse it -
            // on the player's machine, hours later. READ BY THAT READER (Manifest, the same Json.Parse the
            // bake uses): a balanced-brace count used to pass a trailing comma, a bare word or an
            // unquoted key, all of which the runtime refuses.
            string unreadable = ManifestRefusal(manifest, manifestText);
            if (unreadable != null) return unreadable;

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
                // EVERY STEP THAT TOUCHES THE STAGED FOLDER sits inside this try, not only the copy: a
                // source that could not be deleted or read below used to escape with outDir half-pruned,
                // and the "already holds files" check above then refused every later run.
                return Staged(outDir, meta, manifestText, out ok);
            }
            catch (Exception copy)
            {
                // HALF A PACKAGE POISONS EVERY LATER RUN. The refusal path deletes outDir for exactly this
                // reason; an IO error mid-copy used to escape instead, leaving a folder that the "already
                // holds files" check above then refuses forever.
                ok = false;
                string kept = Discard(outDir);
                return "REFUSED: STAGING FAILED while copying into " + outDir + " - " + copy.Message + " " +
                       (kept == null
                           ? "The half-written folder has been deleted rather than left behind, so this " +
                             "command still works once the file is free (close whatever holds it open) and " +
                             "no leftover of a broken run can be shipped by accident."
                           : StillThere(outDir, kept));
            }
        }

        /// <summary>The checks over a folder <see cref="Run"/> has finished copying. Throws on an IO
        /// failure, which Run turns into its STAGING FAILED refusal.</summary>
        private static string Staged(string outDir, string meta, string manifestText, out bool ok)
        {
            ok = false;
            long saved = 0;
            List<string> unbaked, stale, newer;
            List<string> dropped = BakedAlready(outDir, manifestText, out unbaked, out stale, out newer);
            // NO LEDGER, ONLY DATES: said, never refused - a zip or a sync rewrites every mtime.
            StringBuilder warn = new StringBuilder();
            foreach (string rel in newer)
                warn.Append("\nWARN: ").Append(rel).Append(" is newer than its bank in ").Append(ShippedBanks)
                    .Append(" and no ").Append(SourceLedger).Append(" entry says which bytes that bank holds. ")
                    .Append("If you edited it since the last 'ct_sound bake', bake again - the player hears the bank.");
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
            // Everything above is a "you are shipping the game's data" refusal; the sound ones below are
            // not, and the preamble that explains redistribution would be a lie in front of them alone.
            bool redistribution = refusals.Count > 0;

            // A "video" ROW WHOSE CLIP IS NOT IN THE PACKAGE SHIPS DEAD: ct_video serves a row only from
            // Content\Videos\<stem>.webm/.mp4/.mov (VideoCatalog.LiveAt), so a typo or a same-stem pair
            // installs a mod whose row is skipped on the player's machine. The Lifecycle stage checks
            // this (StageValidate); a bare 'ct_package' did not.
            refusals.AddRange(VideoRefusals(outDir, manifestText));

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
            // ...AND ONE BAKED BEFORE ITS SOURCE CHANGED SHIPS THE OLD SOUND, just as silently: the bank is
            // there, so the rule above is satisfied, and it holds whatever the source was at the last bake.
            foreach (string rel in stale)
                refusals.Add(rel + " - CHANGED SINCE IT WAS BAKED. " + ShippedBanks + " still holds the " +
                             "audio from the last 'ct_sound bake', and that bank - not this file - is what the " +
                             "player hears. Run 'ct_sound bake <YourMod>' in game, then package again.");

            if (refusals.Count > 0)
            {
                string kept = Discard(outDir);
                StringBuilder bad = new StringBuilder();
                bad.Append("REFUSED - this package is NOT publishable, and ").Append(outDir)
                   .AppendLine(kept == null ? " has been deleted rather than half-written."
                                            : " could NOT be deleted: " + StillThere(outDir, kept));
                if (redistribution)
                    bad.AppendLine("Phoenix Point's own data must never be redistributed. A patched bundle is " +
                                   "the game's own file with a few of your bytes in it: ContentTool builds those " +
                                   "ON THE PLAYER'S MACHINE, out of the player's own installation, into their " +
                                   "AppData (Route7.ApplyProject) - which is exactly why no release, Workshop " +
                                   "item or zip has to contain one.");
                foreach (string r in refusals) bad.Append("  REFUSED: ").AppendLine(r);
                return bad.ToString().TrimEnd() + warn;
            }

            long bytes = 0;
            foreach (string f in files) bytes += new FileInfo(Path.Combine(outDir, f)).Length;
            ok = true;
            return Bake.StageText.S7(files.Count, bytes, outDir) +      // ONE copy of S7, in StageText
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
                   "ContentTool for them because meta.json declares it." + warn;
        }

        /// <summary>
        /// The mod's OWN built DLL inside the author folder, or null - what `ct_package` hands
        /// <see cref="Run"/> as its <c>assembly</c>.
        ///
        /// IT PICKS ONE UP, IT DOES NOT BUILD ONE. Compiling is the author's own business: a modder
        /// writing C# already has Visual Studio or Rider open and a built DLL on disk, and a
        /// content-only mod has no code at all. So this looks for exactly the file meta.json names -
        /// newest copy under the project, which finds both bin\Release\net472\&lt;name&gt;.dll and a DLL
        /// simply dropped in the project root - and answers null for everything else.
        ///
        /// NEVER OUT OF obj\, and bin\Release\ FIRST. obj\ holds the compiler's intermediate copy and
        /// obj\...\ref\ a REFERENCE assembly - metadata only, no method bodies - which is routinely the
        /// newest file of that name and loads in the game as a mod that throws on its first call. A
        /// Debug build is only picked when there is no Release one at all.
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
            Dictionary<string, object> tree;
            if (MetaTree(File.ReadAllText(meta), out tree) != null) return null;
            string dll = MetaStr(tree, "AssemblyName");
            // A NAME, never a path: Directory.GetFiles takes it as a search pattern, and "..\x.dll" or
            // "*.dll" would pick up a file this project does not own.
            if (string.IsNullOrEmpty(dll) || dll != Path.GetFileName(dll) || dll.IndexOfAny(new[] { '*', '?' }) >= 0)
                return null;

            string newest = null;
            int newestRank = int.MaxValue;
            foreach (string f in Directory.GetFiles(authorDir, dll, SearchOption.AllDirectories))
            {
                // Exact name only: NTFS also matches a pattern against the 8.3 SHORT name.
                if (!string.Equals(Path.GetFileName(f), dll, StringComparison.OrdinalIgnoreCase)) continue;
                int rank = BuildRank(authorDir, f);
                if (rank < 0) continue;
                if (newest == null || rank < newestRank ||
                    (rank == newestRank && File.GetLastWriteTimeUtc(f) > File.GetLastWriteTimeUtc(newest)))
                {
                    newest = f;
                    newestRank = rank;
                }
            }
            return newest;
        }

        /// <summary>How good a candidate for the SHIPPED assembly <paramref name="file"/> is: 0 under
        /// bin\Release, 1 anywhere else, 2 under bin\Debug, -1 never (anything under obj\, the compiler's
        /// intermediate and reference copies).</summary>
        private static int BuildRank(string authorDir, string file)
        {
            string rel = file.Substring(authorDir.TrimEnd('\\', '/').Length).Replace('/', '\\').TrimStart('\\');
            string[] parts = rel.Split('\\');
            for (int i = 0; i < parts.Length - 1; i++)
                if (string.Equals(parts[i], "obj", StringComparison.OrdinalIgnoreCase)) return -1;
            for (int i = 0; i + 1 < parts.Length - 1; i++)
                if (string.Equals(parts[i], "bin", StringComparison.OrdinalIgnoreCase))
                {
                    if (string.Equals(parts[i + 1], "Release", StringComparison.OrdinalIgnoreCase)) return 0;
                    if (string.Equals(parts[i + 1], "Debug", StringComparison.OrdinalIgnoreCase)) return 2;
                }
            return 1;
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
        /// "replace": {} declare nothing and do nothing, so the collection must hold at least one
        /// member - otherwise the empty rung escapes a refusal whose own text says a row is required.
        /// Read off the ROOT of the parsed tree: the regex this replaced also counted a "weapons" key
        /// nested inside some other block, or spelled inside a string.
        /// </summary>
        internal static bool Ships(IList<string> stagedFiles, string manifestText)
        {
            if (stagedFiles != null)
                foreach (string rel in stagedFiles)
                    if (!IsPaperwork(Path.GetFileName(rel))) return true;
            Dictionary<string, object> tree;
            try { tree = Manifest.Tree(manifestText ?? "", "ppcontent.json"); }
            catch (InvalidDataException) { return false; }
            foreach (string rung in new[] { "replace", "publish", "sounds", "creature", "weapons" })
            {
                object value;
                if (!tree.TryGetValue(rung, out value)) continue;
                if (value is List<object> rows && rows.Count > 0) return true;
                if (value is Dictionary<string, object> block && block.Count > 0) return true;
            }
            return false;
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
            // PARSED, not matched: the regex read passed an unclosed object that happened to hold the
            // right two substrings, and the game's own reader (ModMeta.FromDir -> JsonConvert) then
            // refused the file on the player's machine - a package that installs as nothing.
            Dictionary<string, object> tree;
            string unreadable = MetaTree(metaText, out tree);
            if (unreadable != null) return unreadable;
            string id = MetaStr(tree, "ID");
            if (string.IsNullOrEmpty(id))
                return "meta.json declares no \"ID\" - the mod manager keys every mod on it.";
            if (!DependsOnEngine(tree))
                return "meta.json does not declare \"Dependencies\": [ \"" + EngineId + "\" ] - without it " +
                       "the player can install this mod with the engine switched off and it will silently " +
                       "do nothing. With it, Phoenix Point enables ContentTool for them.";
            string dll = MetaStr(tree, "AssemblyName");
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
            List<string> stale;
            return BakedAlready(dir, manifestText, out unbaked, out stale);
        }

        /// <summary>
        /// The same walk, also reporting the sources whose bank is OLDER than the source - see
        /// <see cref="Run"/> - and every "sounds" row whose file is not in the project at all and has no
        /// bank either (a declared replacement that can only ship dead; it used to go unmentioned
        /// because the walk only visited files that exist).
        ///
        /// STALE IS A FINGERPRINT, not a guess: `ct_sound bake` records each source's SHA-1 in
        /// <see cref="SourceLedger"/> beside the banks, and a source whose bytes no longer match is
        /// stale whatever its timestamps say. A bank with no ledger entry (baked before the ledger
        /// existed, or a demo that ships none) falls back to the one thing there is - a source written
        /// AFTER its bank - and lands in <paramref name="newer"/>, a WARNING rather than a refusal:
        /// unzipping, a git checkout or a sync rewrites mtimes, so a date alone proves nothing.
        /// </summary>
        internal static List<string> BakedAlready(string dir, string manifestText, out List<string> unbaked,
                                                  out List<string> stale)
        {
            List<string> newer;
            return BakedAlready(dir, manifestText, out unbaked, out stale, out newer);
        }

        internal static List<string> BakedAlready(string dir, string manifestText, out List<string> unbaked,
                                                  out List<string> stale, out List<string> newer)
        {
            List<string> drop = new List<string>();
            unbaked = new List<string>();
            stale = new List<string>();
            newer = new List<string>();
            string sources = Path.Combine(dir, ReplaceSources);
            string banks = Path.Combine(dir, ShippedBanks);
            Dictionary<string, string> declared = DeclaredSounds(manifestText);
            Dictionary<uint, string[]> ledger = ReadLedger(banks);

            foreach (KeyValuePair<string, string> row in declared)
                if (row.Key.IndexOfAny(Path.GetInvalidPathChars()) < 0 &&
                    !File.Exists(Path.Combine(sources, row.Key)) &&
                    !File.Exists(Path.Combine(banks, row.Value + ".bnk")))
                    unbaked.Add(Path.Combine(ReplaceSources, row.Key) + " (media " + row.Value +
                                ", declared in \"sounds\" - and the file itself is not in the project either)");
            if (!Directory.Exists(sources))
            {
                unbaked.Sort(StringComparer.OrdinalIgnoreCase);
                return drop;
            }

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
                string bank = Path.Combine(banks, media + ".bnk");
                string rel = Path.Combine(ReplaceSources, name);
                if (!File.Exists(bank))
                {
                    unbaked.Add(rel + " (media " + media + ")");
                    continue;
                }
                drop.Add(rel);
                bool dated;
                if (Stale(f, bank, media, ledger, out dated))
                    (dated ? newer : stale).Add(rel + " (media " + media + ")");
            }
            drop.Sort(StringComparer.OrdinalIgnoreCase);
            unbaked.Sort(StringComparer.OrdinalIgnoreCase);
            stale.Sort(StringComparer.OrdinalIgnoreCase);
            newer.Sort(StringComparer.OrdinalIgnoreCase);
            return drop;
        }

        /// <summary>Does <paramref name="bank"/> hold something other than <paramref name="source"/> as it
        /// is NOW? The ledger's SHA-1 when there is one, else "the source was written after the bank".</summary>
        private static bool Stale(string source, string bank, string media, Dictionary<uint, string[]> ledger,
                                  out bool dated)
        {
            uint id;
            string[] entry;
            dated = false;
            if (uint.TryParse(media, NumberStyles.None, CultureInfo.InvariantCulture, out id) &&
                ledger.TryGetValue(id, out entry))
                return !string.Equals(entry[0], Sha1Of(source), StringComparison.OrdinalIgnoreCase);
            dated = true;
            return File.GetLastWriteTimeUtc(source) > File.GetLastWriteTimeUtc(bank);
        }

        /// <summary>
        /// The "sounds" array as file name -> media ID, through the ONE reader the bake uses
        /// (<see cref="Manifest.Sounds"/>). Deliberately SILENT on a malformed entry: the bake refuses
        /// one by name, and a packager that threw here would turn a typo into a crash instead of a
        /// release that merely carries one extra file.
        /// </summary>
        private static Dictionary<string, string> DeclaredSounds(string manifestText)
        {
            Dictionary<string, string> byFile = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            Dictionary<string, object> tree;
            try { tree = Manifest.Tree(manifestText ?? "", "ppcontent.json"); }
            catch (InvalidDataException) { return byFile; }
            foreach (KeyValuePair<uint, string> s in Manifest.Sounds(tree, new List<string>()))
                byFile[s.Value] = s.Key.ToString(CultureInfo.InvariantCulture);
            return byFile;
        }

        /// <summary>
        /// `ct_sound bake`'s record of WHICH BYTES each bank in <see cref="ShippedBanks"/> was baked from:
        /// one line per bank, "&lt;mediaId&gt;\t&lt;source SHA-1&gt;\t&lt;source file name&gt;". Written by the
        /// bake (SoundReplace), read here, so a bank whose source changed afterwards is refused instead of
        /// shipping the old sound. It lives beside the banks and ships with them; SoundLoad loads *.bnk only.
        /// </summary>
        internal const string SourceLedger = "sources.ledger";

        /// <summary>The ledger in <paramref name="banksDir"/> as media -> { sha1, file }; empty when there is
        /// none or a line does not read (a hand-edited line is ignored, never trusted).</summary>
        internal static Dictionary<uint, string[]> ReadLedger(string banksDir)
        {
            Dictionary<uint, string[]> ledger = new Dictionary<uint, string[]>();
            string path = Path.Combine(banksDir ?? "", SourceLedger);
            if (!File.Exists(path)) return ledger;
            foreach (string line in File.ReadAllLines(path))
            {
                string[] f = line.Split('\t');
                uint media;
                if (f.Length == 3 && f[1].Length == 40 &&
                    uint.TryParse(f[0], NumberStyles.None, CultureInfo.InvariantCulture, out media))
                    ledger[media] = new[] { f[1], f[2] };
            }
            return ledger;
        }

        /// <summary>Writes the whole ledger in one atomic swap, sorted by media so it diffs cleanly.</summary>
        internal static void WriteLedger(string banksDir, IDictionary<uint, string[]> ledger)
        {
            List<uint> order = new List<uint>(ledger.Keys);
            order.Sort();
            StringBuilder text = new StringBuilder();
            foreach (uint media in order)
                text.Append(media.ToString(CultureInfo.InvariantCulture)).Append('\t')
                    .Append(ledger[media][0]).Append('\t').Append(ledger[media][1]).Append('\n');
            AtomicFile.WriteText(Path.Combine(banksDir, SourceLedger), text.ToString(), new UTF8Encoding(false));
        }

        /// <summary>SHA-1 of a file's bytes, lowercase hex - the ledger's fingerprint.</summary>
        internal static string Sha1Of(string path)
        {
            using (SHA1 sha = SHA1.Create())
            using (FileStream f = File.OpenRead(path))
            {
                StringBuilder hex = new StringBuilder(40);
                foreach (byte b in sha.ComputeHash(f)) hex.Append(b.ToString("x2", CultureInfo.InvariantCulture));
                return hex.ToString();
            }
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
        /// PARSE declares no target here - Run's ManifestRefusal has already refused that package before
        /// anything reads this.</summary>
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

        /// <summary>Every "video" row whose clip stem the staged Content\Videos\ does not answer with
        /// exactly one .webm/.mp4/.mov - the same top-level, extension-checked read ContentMods.SourceFile
        /// does, restated because this file compiles alone.</summary>
        internal static List<string> VideoRefusals(string outDir, string manifestText)
        {
            List<string> said = new List<string>();
            IReadOnlyList<ReplaceRow> rows;
            try { rows = Manifest.Parse(manifestText).Replace; }
            catch (InvalidDataException) { return said; }
            string dir = Path.Combine(Path.Combine(outDir, "Content"), "Videos");
            foreach (ReplaceRow row in rows)
            {
                if (string.IsNullOrEmpty(row.Video)) continue;
                List<string> hits = new List<string>();
                if (Directory.Exists(dir))
                    foreach (string f in Directory.GetFiles(dir))
                    {
                        string ext = Path.GetExtension(f).ToLowerInvariant();
                        if ((ext == ".webm" || ext == ".mp4" || ext == ".mov") &&
                            string.Equals(Path.GetFileNameWithoutExtension(f), row.Video, StringComparison.OrdinalIgnoreCase))
                            hits.Add(Path.GetFileName(f));
                    }
                if (hits.Count == 1) continue;
                hits.Sort(StringComparer.OrdinalIgnoreCase);
                said.Add(hits.Count == 0
                    ? "the \"video\" row '" + row.Video + "' names no .webm/.mp4/.mov under Content\\Videos\\ - " +
                      "ct_video skips that row on the player's machine. Fix the name or add the clip, then package again."
                    : "the \"video\" row '" + row.Video + "' is answered by " + hits[0] + " and " + hits[1] +
                      " in Content\\Videos\\ - both are SKIPPED, so the row plays nothing. Keep one, then package again.");
            }
            return said;
        }

        /// <summary>
        /// The ONE gate on ppcontent.json before anything is staged: null when the runtime can read it,
        /// else the whole REFUSED sentence. Read by the runtime's own reader (<see cref="Manifest.Parse"/>),
        /// so what packages is what loads - and "id"/"bundle", when present, pass the same path-safety rule
        /// ContentProject.Load applies, since a manifest that fails it loads as nothing on the player's side.
        /// </summary>
        private static string ManifestRefusal(string manifest, string manifestText)
        {
            const string Does = " ContentTool reads that file to learn what this mod replaces, publishes or " +
                                "adds, so a package built from it would install and do nothing. Fix the file, " +
                                "then package again.";
            if (manifestText.Trim().Length == 0)
                return "REFUSED: " + manifest + " is EMPTY OR NOT VALID JSON - it holds nothing." + Does;
            Manifest parsed;
            try { parsed = Manifest.Parse(manifestText); }
            catch (InvalidDataException bad)
            {
                return Manifest.IsNotAnArray(bad)
                    ? "REFUSED: " + manifest + " - " + bad.Message + "." + Does
                    : "REFUSED: " + manifest + " is EMPTY OR NOT VALID JSON - " + bad.Message + Does;
            }
            string unsafeName = string.IsNullOrEmpty(parsed.Id) ? null : Manifest.UnsafeName("id", parsed.Id);
            if (unsafeName == null && !string.IsNullOrEmpty(parsed.Bundle))
                unsafeName = Manifest.UnsafeName("bundle", parsed.Bundle);
            return unsafeName == null ? null
                : "REFUSED: " + manifest + " - " + unsafeName + ". ContentTool refuses to load such a project " +
                  "on the player's machine, so a package built from it would install and do nothing.";
        }

        /// <summary>
        /// meta.json read AS THE GAME READS IT: null with the tree, or the sentence saying why it does not
        /// read. Json's own sentence ends in advice meant for a glTF ("re-export it rather than editing it
        /// by hand", Json.cs:142-145), which is wrong for a file the author fixes by hand - only the
        /// POSITION and the CAUSE carry over. ONE copy, shared with ProjectScaffold's R13.
        ///
        /// The game's reader is JsonConvert (ModMeta.FromDir, ModMeta.cs:55), which takes // and /* */
        /// comments and a trailing comma; a strict parse refused meta.json files the game loads fine.
        /// </summary>
        internal static string MetaTree(string metaText, out Dictionary<string, object> tree)
        {
            tree = null;
            object parsed;
            try { parsed = Json.Parse(AsJsonConvertReads(metaText), Manifest.MaxDepth); }
            catch (FormatException bad)
            {
                string why = bad.Message;
                int glb = why.LastIndexOf("; re-export", StringComparison.Ordinal);
                if (glb > 0) why = why.Substring(0, glb);
                int at = why.IndexOf("at character ", StringComparison.Ordinal);
                return "meta.json did not read as JSON " + (at > 0 ? why.Substring(at) : why) + ".";
            }
            tree = parsed as Dictionary<string, object>;
            return tree == null ? "meta.json is not a JSON object." : null;
        }

        /// <summary>
        /// The two things JsonConvert tolerates that a strict parse does not - comments and a trailing
        /// comma before ] or } - blanked to SPACES, so every "at character N" still points at the
        /// author's own text. Strings are skipped whole; an unclosed /* is left for the parser to refuse.
        /// </summary>
        internal static string AsJsonConvertReads(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            char[] c = text.ToCharArray();
            int comma = -1;
            for (int i = 0; i < c.Length; i++)
            {
                char ch = c[i];
                if (ch == '"')
                {
                    comma = -1;
                    for (i++; i < c.Length && c[i] != '"'; i++) if (c[i] == '\\') i++;
                    continue;
                }
                if (ch == '/' && i + 1 < c.Length && c[i + 1] == '/')
                {
                    for (; i < c.Length && c[i] != '\n' && c[i] != '\r'; i++) c[i] = ' ';
                    i--;
                    continue;
                }
                if (ch == '/' && i + 1 < c.Length && c[i + 1] == '*')
                {
                    int end = text.IndexOf("*/", i + 2, StringComparison.Ordinal);
                    if (end < 0) break;
                    for (; i < end + 2; i++) if (c[i] != '\n' && c[i] != '\r') c[i] = ' ';
                    i--;
                    continue;
                }
                if (ch == ',') { comma = i; continue; }
                if ((ch == ']' || ch == '}') && comma >= 0) c[comma] = ' ';
                if (!char.IsWhiteSpace(ch)) comma = -1;
            }
            return new string(c);
        }

        /// <summary>A meta.json STRING member, matched the way the game's reader matches it: JsonConvert
        /// binds "ID" to "id" too, so the exact spelling wins and any casing is the fallback.</summary>
        private static string MetaStr(Dictionary<string, object> tree, string key)
        {
            object value;
            if (tree.TryGetValue(key, out value)) return value as string;
            foreach (KeyValuePair<string, object> member in tree)
                if (string.Equals(member.Key, key, StringComparison.OrdinalIgnoreCase)) return member.Value as string;
            return null;
        }

        /// <summary>Does meta.json's "Dependencies" ARRAY name the engine mod?</summary>
        private static bool DependsOnEngine(Dictionary<string, object> tree)
        {
            object value = null;
            if (!tree.TryGetValue("Dependencies", out value))
                foreach (KeyValuePair<string, object> member in tree)
                    if (string.Equals(member.Key, "Dependencies", StringComparison.OrdinalIgnoreCase)) value = member.Value;
            List<object> deps = value as List<object>;
            if (deps == null) return false;
            foreach (object dep in deps)
                if (string.Equals(dep as string, EngineId, StringComparison.Ordinal)) return true;
            return false;
        }

        /// <summary>Deletes the staged folder. Null when it is gone; otherwise why it is NOT, so no message
        /// claims a deletion that did not happen (the old catch swallowed that failure and said "deleted").</summary>
        private static string Discard(string outDir)
        {
            try
            {
                if (Directory.Exists(outDir)) Directory.Delete(outDir, true);
                return null;
            }
            catch (Exception ex) { return ex.Message; }
        }

        /// <summary>The sentence for a staged folder <see cref="Discard"/> could not remove.</summary>
        private static string StillThere(string outDir, string why)
        {
            return "The half-written folder could NOT be deleted (" + why + ") and is still there - delete " +
                   outDir + " yourself before packaging again: this command refuses a folder that already " +
                   "holds files, and NOTHING in it may be shipped.";
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
        /// into it. The GUID is what makes the name this tool's.
        ///
        /// BOTH SHAPES THE TOOL WRITES. The scaffold's is the GUID alone; every SIBLING TEMP - the bake's
        /// own Dist bundle and each patched copy - is '&lt;the real name&gt;.&lt;guid&gt;.tmp', because a temp
        /// must be a sibling of the file it will replace (AtomicFile.Publish:56). Only the first was
        /// recognised, so a bake that threw mid-serialization left a full-size
        /// 'MyMod.bundle.&lt;guid&gt;.tmp' in Dist\ and CopyDir staged it into the release.</summary>
        internal static bool IsOwnTemp(string path)
        {
            if (!string.Equals(Path.GetExtension(path), ".tmp", StringComparison.OrdinalIgnoreCase))
                return false;
            string stem = Path.GetFileNameWithoutExtension(path);
            Guid ours;
            if (Guid.TryParseExact(stem, "N", out ours)) return true;
            int cut = stem.Length - 32;
            if (cut <= 0 || stem[cut - 1] != '.') return false;
            return Guid.TryParseExact(stem.Substring(cut), "N", out ours);
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
