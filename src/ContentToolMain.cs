using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using AssetsTools.NET.Extra;
using Base.Core;
using Base.Utils.GameConsole;
using PhoenixPoint.Modding;

namespace Morgott.ContentTool
{
    /// <summary>
    /// In-game Phoenix Point content authoring and bake tool. Everything the tool does is driven from
    /// the developer console under the ct_ prefix; the rr_ commands belong to the donor harness
    /// ResourceReplacer and are not part of this mod.
    /// </summary>
    public class ContentToolMain : ModMain
    {
        private static ModLogger log;

        /// <summary>
        /// The mod's own folder, where streamed media is materialized (FINAL-PLAN 4.3, ARM1).
        /// ModEntry.Directory is authoritative: the loader uses Assembly.Load(byte[]), so
        /// Assembly.Location is empty and there is nothing else to read it off.
        /// </summary>
        internal static string ModDir { get; private set; }

        /// <summary>
        /// A project folder under the mod directory, by NAME. Commands take a name and never a path:
        /// the game console's own CommandLineParser eats every backslash as an escape - even inside
        /// quotes - and ends a token at whitespace (decompiled Base.Utils.GameConsole\
        /// CommandLineParser.cs:180-215), so `ct_route7 apply D:\Steam\...\Phoenix Point\...` arrives
        /// as `D:SteamsteamappscommonPhoenix`. A bare name has neither character.
        ///
        /// A name that is not ours resolves to the SIBLING mod folder of that name, because every demo
        /// is its own mod beside us now (Project.ContentMods.ProjectDir).
        /// </summary>
        internal static string ProjectDir(string name)
        {
            return Project.ContentMods.ProjectDir(ModDir, name);
        }

        /// <summary>
        /// Where install-time patched copies live, per mod. NOT the mod folder: a mod can be a Steam
        /// Workshop item (steamapps\workshop\content\839770\&lt;id&gt;\), which Steam re-downloads,
        /// repairs and wipes without warning - a catalog record pointing in there would sooner or
        /// later name a file that no longer exists. persistentDataPath is ours, writable, per-user
        /// and untouched by Steam, and the &lt;modid&gt; segment keeps two mods from colliding.
        /// (This is not the FINAL-PLAN 4.4 case: that rejected persistentDataPath for a mod's own
        /// SHIPPED media, which must stay self-contained. This file is generated on the player's
        /// machine and must not be shipped at all.)
        /// </summary>
        internal static string PatchedDir(string modId)
        {
            return Path.Combine(Path.Combine(PatchedRoot, InstallTag), modId);
        }

        /// <summary>The folder every install's patched copies live under - what the startup sweep
        /// walks (Project.PatchCache.Prune).</summary>
        internal static string PatchedRoot
        {
            get
            {
                return Path.Combine(Path.Combine(
                    UnityEngine.Application.persistentDataPath, "ContentTool"), "Patched");
            }
        }

        /// <summary>THIS installation, hashed. persistentDataPath is per user and per product, so
        /// without it the player's Steam copy and his second test instance share one folder and
        /// overwrite each other's copies. dataPath is the install's own, read-only here.</summary>
        internal static string InstallTag
        {
            get { return Project.PatchCache.InstallTag(UnityEngine.Application.dataPath); }
        }

        /// <summary>
        /// Every project id whose patched copies are still someone's: the ENABLED content mods, via
        /// the one discovery every route shares (Project.ContentMods.Enabled - never a second one),
        /// plus ContentTool's own subprojects, which no mod manager lists and `ct_route7 apply
        /// &lt;name&gt;` bakes. Throwing rather than guessing is deliberate: the caller's catch turns
        /// "I could not read the roster" into no sweep at all, which is the safe answer.
        /// </summary>
        private static List<string> LiveProjectIds()
        {
            List<string> ids = new List<string>();
            int skipped;
            foreach (string dir in Project.ContentMods.Enabled(ModDir, Project.ContentMods.Manifest,
                                                               Project.ModRoster.Build(), null, out skipped))
                ids.Add(Project.ContentProject.LoadDeclared(dir).Id);

            DirectoryInfo own = string.IsNullOrEmpty(ModDir) ? null : new DirectoryInfo(ModDir);
            if (own != null && own.Exists)
                foreach (DirectoryInfo sub in own.GetDirectories())
                    if (File.Exists(Path.Combine(sub.FullName, Project.ContentMods.Manifest)))
                        ids.Add(Project.ContentProject.LoadDeclared(sub.FullName).Id);
            return ids;
        }

        public override void OnModEnabled()
        {
            log = Logger;
            ModDir = Instance?.Entry?.Directory;
            ConsoleBridge.Register();
            log?.LogInfo(Version());
            // Teaches the game's own loader that a media-only mod is a loadable mod, so the manager's
            // switch is a real switch for it. Must be in place before any dependent mod is enabled -
            // it is, because a dependency is enabled before its dependents (ModManager.cs:200-207).
            try { log?.LogInfo(Project.ModRoster.Install(Instance?.Entry?.ID)); }
            catch (Exception ex) { log?.LogError("ct_content install THREW " + ex); }
            // The engine half of a SHIPPED content mod: every ENABLED dependent mod's own media -
            // replacement banks before anything can post them, declared clips served out of the mod's
            // own folder. No console command, no install step, and nothing in the install is touched.
            //
            // ONE FRAME LATER, on purpose. We are called from inside the startup enable pass
            // (PhoenixGame.cs:851 ModManager.EnableModsFromStore -> TryEnableMod -> ModEntry.cs:219
            // OnModEnabled), and every mod that DEPENDS on us is enabled after us - so at this instant
            // their ModEntry.Enabled is still false. Reading the roster now would skip exactly the
            // content mods the player has switched ON. The pass is synchronous, so the next frame sees
            // final flags.
            try
            {
                Timing timing = Timing.Current;
                if (timing != null) timing.Start(LoadContentCrt(), NextUpdate.NextFrame);
                else { log?.LogWarning("ct_sound: no Timing to defer on - reading the roster early"); LoadContent(); }
            }
            catch (Exception ex) { log?.LogError("ct_sound defer THREW " + ex); }
            // The engine half of a CUSTOM-MODEL CREATURE: a tactical actor assembled out of a .glb has
            // no authored colliders, so nothing can hover it and nothing can shoot it. Installed
            // unconditionally and self-limiting - the fit only fires for an actor that has NO
            // Characters-layer collider, which no shipped unit ever is (Tactical.CreatureFit).
            try { Tactical.CreatureFit.Install(m => log?.LogInfo(m)); }
            catch (Exception ex) { log?.LogError("ct_creature install THREW " + ex); }
            // The weapon fit workbench's hotkey poll. Armed unconditionally because it is one
            // GetKeyDown per frame and the whole point is that it is there when an eye needs it; the
            // view itself allocates nothing until it is opened.
            try { Dev.FitBench.Install(); }
            catch (Exception ex) { log?.LogError("ct_bench install THREW " + ex); }
            Dev.AutoRun.MaybeStart(ModDir, m => log?.LogInfo(m));
        }

        private static IEnumerator<NextUpdate> LoadContentCrt()
        {
            LoadContent();
            yield break;
        }

        private static void LoadContent()
        {
            // Both routes read the SAME roster in the SAME frame. The video pass used to run inside
            // OnModEnabled, where every dependent mod still reads Enabled=false - it could not be
            // gated there at all.
            try { log?.LogInfo(Bake.VideoCatalog.LiveAll()); }
            catch (Exception ex) { log?.LogError("ct_video LiveAll THREW " + ex); }
            try { log?.LogInfo(Bake.SoundLoad.LoadAll(ModDir)); }
            catch (Exception ex) { log?.LogError("ct_sound load THREW " + ex); }
            // The Addressables routes' half of the same roster read, in BOTH directions: the live
            // redirections and published keys are session-only, so every ENABLED content mod has to
            // have them installed again on this launch, and a mod switched off BEFORE launch never
            // reaches the SetEnabled postfix at all. Silent unless there is something to say.
            // BEFORE the reconcile, and only here: this is the last instant at which no bundle has
            // been installed for this session, so nothing the sweep deletes can be loaded. It reads
            // the SAME final roster the reconcile below does.
            try
            {
                string swept = Project.PatchCache.Prune(PatchedRoot, InstallTag, LiveProjectIds());
                if (swept != null) log?.LogInfo(swept);
            }
            catch (Exception ex) { log?.LogError("ct_cache prune THREW " + ex); }
            try { string moved = Project.ModRoster.Reconcile(ModDir); if (moved != null) log?.LogInfo(moved); }
            catch (Exception ex) { log?.LogError("ct_content reconcile THREW " + ex); }
            // The creature route's half of the same roster read. A content mod that declares a
            // "creature" block and ships no C# has nobody else to build it - the only caller of
            // CreatureBuild.Build was a mod's own DLL, so such a mod loaded and minted nothing,
            // silently. AFTER the reconcile: the bundle it loads is the one the reconcile installed.
            try { string born = Tactical.CreatureBuild.BuildAll(ModDir); if (born != null) log?.LogInfo(born); }
            catch (Exception ex) { log?.LogError("ct_creature BuildAll THREW " + ex); }
            // The startup pass is over - the roster above was read from its final flags. From here
            // the mod manager's checkbox is the only thing that decides, so the dependency keep-alive
            // (ModRoster.BeforeDisable) must stop having an opinion: a mod the player switches OFF
            // stays off, ours included. Last, and outside the try above, so no earlier failure can
            // leave the veto armed for the rest of the session.
            Project.ModRoster.EndStartupPass();
        }

        public override void OnModDisabled()
        {
            // The probe patches a call every character resolve goes through, and it writes one
            // prefab; neither may outlive the mod.
            if (Dev.SeamSwap.Active) log?.LogInfo(Dev.SeamSwap.Run(new[] { "off" }));
            // A watcher thread and a coroutine must not outlive the mod that made them.
            if (Dev.DevLoop.Enabled) log?.LogInfo(Dev.DevRunner.Run(new[] { "off" }));
            // Closes the workbench first if it is open, so a mod switched off mid-fit does not leave
            // the geoscape's canvases hidden and its camera hinted at an empty squad bay.
            Dev.FitBench.Uninstall();
            Tactical.CreatureFit.Uninstall();
            Project.ModRoster.Uninstall();
            ConsoleBridge.Unregister();
            log = null;
        }

        /// <summary>
        /// One line proving the whole assembly is intact on the player's machine: the merged
        /// AssetsTools.NET (a sibling DLL would never resolve - PPModLoader uses Assembly.Load(byte[])
        /// and installs no AssemblyResolve) and the embedded class database the bake writer needs.
        /// </summary>
        private static string Version()
        {
            return $"ContentTool {Assembly.GetExecutingAssembly().GetName().Version} | " +
                   $"build={BuildStamp()} | " +
                   $"AssetsTools.NET merged: {typeof(AssetsManager).Assembly == Assembly.GetExecutingAssembly()} | " +
                   $"classdata.tpk embedded: {ClassDataBytes()} B";
        }

        /// <summary>
        /// Which DLL this session is actually running: the first 8 hex of the SHA-1 of the file the
        /// loader read. AssemblyVersion is a constant and Assembly.Location is empty (Assembly.Load
        /// (byte[])), so neither can tell a fresh deploy from a stale one - and three of the seven
        /// runs it took to close the other lane were the game holding a DLL that was replaced AFTER
        /// it launched. Deploying mid-session cannot change this value, because it is read at init
        /// from the file that was loaded, which is exactly what makes the mismatch detectable.
        /// </summary>
        private static string BuildStamp()
        {
            try
            {
                string dll = Path.Combine(ModDir ?? "", "ContentTool.dll");
                return File.Exists(dll)
                    ? Project.Sha1.Hex(File.ReadAllBytes(dll)).Substring(0, 8)
                    : "no-dll";
            }
            catch (Exception) { return "unreadable"; }
        }

        /// <summary>Size of the embedded class database; -1 when the resource is missing.</summary>
        private static long ClassDataBytes()
        {
            using (Stream s = ClassData()) return s == null ? -1 : s.Length;
        }

        /// <summary>
        /// The class database, read from the assembly itself. FINAL-PLAN 1.6: never from a path.
        /// Null only if the build lost the EmbeddedResource.
        /// </summary>
        internal static Stream ClassData()
        {
            return Assembly.GetExecutingAssembly()
                .GetManifestResourceStream("Morgott.ContentTool.classdata.tpk");
        }

        /// <summary>The gate V1 probe clip, embedded for the same reason the class database is.</summary>
        internal static Stream ProbeVideo()
        {
            return Assembly.GetExecutingAssembly()
                .GetManifestResourceStream("Morgott.ContentTool.v1_probe.webm");
        }

        /// <summary>
        /// The log sink for work that finishes AFTER its console command returned - gate V1 waits on
        /// a video decoder, so by the time its arms have an answer there is no IConsole left to write
        /// to. Player.log is where autogate reads the arms from anyway.
        /// </summary>
        internal static void Say(string msg)
        {
            log?.LogInfo(msg);
        }

        /// <summary>
        /// Runs one "ct_x arg arg" line through the SAME table the console dispatches from, with no
        /// IConsole - so the output goes to Player.log through the command's own Out() fallback.
        /// There is no second dispatcher and no script language: a line is a console command.
        /// </summary>
        internal static string RunConsoleLine(string line)
        {
            return ConsoleBridge.InvokeLine(line);
        }

        // ---------------------------------------------------------------- developer console
        /// <summary>
        /// The game console only ever scans its OWN assembly (ConsoleCommandAttribute.LoadCommands calls
        /// Assembly.GetExecutingAssembly()), and exposes no registration API, so the command table is
        /// injected directly. LoadCommands writes through the SortedList indexer and never clears the
        /// table, and it runs exactly once (Base.Core.Game.Initialize -> GameConsole.Init), so a single
        /// registration here survives regardless of whether the mod loads before or after the console.
        ///
        /// Any failure is contained: the console is the tool's UI for now, but losing it must not take
        /// the assembly down with it.
        /// </summary>
        private static class ConsoleBridge
        {
            private static SortedList<string, ConsoleCommandAttribute> store;
            private static readonly List<string> registered = new List<string>();

            internal static void Register()
            {
                try
                {
                    Type attr = typeof(ConsoleCommandAttribute);
                    // Reading the static field also forces the type initialiser, so the table always exists.
                    store = attr.GetField("CommandToInfo", BindingFlags.NonPublic | BindingFlags.Static)
                                ?.GetValue(null) as SortedList<string, ConsoleCommandAttribute>;
                    FieldInfo fMethod = attr.GetField("_methodInfo", BindingFlags.NonPublic | BindingFlags.Instance);
                    FieldInfo fVarArgs = attr.GetField("_variableArguments", BindingFlags.NonPublic | BindingFlags.Instance);
                    if (store == null || fMethod == null || fVarArgs == null)
                        throw new MissingFieldException("GameConsole internals changed (CommandToInfo/_methodInfo/_variableArguments).");

                    Unregister();
                    foreach (MethodInfo m in typeof(ConsoleBridge).GetMethods(BindingFlags.Static | BindingFlags.Public))
                    {
                        if (!(Attribute.GetCustomAttribute(m, attr) is ConsoleCommandAttribute info)) continue;
                        if (string.IsNullOrEmpty(info.Command)) continue;

                        // Mirror LoadCommands exactly so the entries are indistinguishable from native ones.
                        ParameterInfo[] ps = m.GetParameters();
                        ParameterInfo last = ps[ps.Length - 1];
                        fMethod.SetValue(info, m);
                        fVarArgs.SetValue(info, ps.Length > 1
                            && last.ParameterType == typeof(string[])
                            && last.GetCustomAttributes(typeof(ParamArrayAttribute), false).Length > 0);

                        store[info.Command] = info;
                        registered.Add(info.Command);
                    }
                    log?.LogInfo("Console commands: " + string.Join(", ", registered.ToArray()));
                }
                catch (Exception ex)
                {
                    store = null;
                    registered.Clear();
                    log?.LogWarning("Console commands unavailable: " + ex.Message);
                }
            }

            /// <summary>
            /// Looks the command up in the table we registered into and invokes it exactly as the
            /// console would, minus the console. Returns a named refusal rather than throwing, so a
            /// bad line in an autorun list costs that line and nothing else.
            /// </summary>
            internal static string InvokeLine(string line)
            {
                string[] parts = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                MethodInfo mi = null;
                foreach (MethodInfo cand in typeof(ConsoleBridge).GetMethods(BindingFlags.Static | BindingFlags.Public))
                {
                    ConsoleCommandAttribute a = Attribute.GetCustomAttribute(cand, typeof(ConsoleCommandAttribute)) as ConsoleCommandAttribute;
                    if (a != null && a.Command == parts[0]) { mi = cand; break; }
                }
                if (mi == null) return "ct_autorun: '" + parts[0] + "' is not a ct_ command";

                string[] args = new string[parts.Length - 1];
                Array.Copy(parts, 1, args, 0, args.Length);
                object[] call = mi.GetParameters().Length > 1
                    ? new object[] { null, args }
                    : new object[] { null };
                mi.Invoke(null, call);
                return "ct_autorun: ran " + parts[0] + (args.Length > 0 ? " (" + args.Length + " arg(s))" : "");
            }

            internal static void Unregister()
            {
                try
                {
                    if (store != null)
                        foreach (string name in registered) store.Remove(name);
                }
                catch (Exception) { }
                registered.Clear();
            }

            /// <summary>
            /// IConsole.WriteLine takes a format string, so user data goes through {0}.
            ///
            /// It writes to Player.log by itself - GameConsoleWindow.WriteLineWithColor ends in
            /// AppendToLogFile (GameConsoleWindow.cs:266), which is the "[CONSOLE] hh:mm:ss | ..."
            /// line - so mirroring it through the mod logger as well is what printed every command
            /// twice. The logger is the FALLBACK for a caller that has no console, not a second sink.
            /// </summary>
            private static void Out(IConsole console, string msg)
            {
                try
                {
                    if (console != null) { WriteLines(console, msg ?? ""); return; }
                }
                catch (Exception) { }
                log?.LogInfo(msg);
            }

            /// <summary>
            /// ONE console call per line, because GameConsoleWindow.WriteLineWithColor instantiates one
            /// UnityEngine.UI.Text per call (GameConsoleWindow.cs:255-271) and a UI.Text mesh dies at
            /// 65000 vertices. ct_seamprobe handed it a ~6 KB report in a single call and the whole
            /// component threw "Mesh can not have more than 65000 vertices" in Text.UpdateGeometry, so
            /// it rendered NOTHING while still taking up its layout height - the blank output with
            /// "invisible characters". The console already trims itself to 200 line objects, which only
            /// works if lines arrive as lines.
            /// </summary>
            private const int MaxConsoleLines = 80;
            private const int MaxConsoleLineChars = 400;

            private static void WriteLines(IConsole console, string msg)
            {
                string[] lines = msg.Split('\n');
                int n = Math.Min(lines.Length, MaxConsoleLines);
                bool lost = lines.Length > n;
                for (int i = 0; i < n; i++)
                {
                    string line = lines[i].TrimEnd('\r');
                    if (line.Length > MaxConsoleLineChars)
                    {
                        line = line.Substring(0, MaxConsoleLineChars) + " ...(clipped)";
                        lost = true;
                    }
                    console.WriteLine("{0}", line);
                }
                if (lines.Length > n)
                    console.WriteLine("{0}", "... " + (lines.Length - n) + " more line(s) not shown");
                // Nothing is allowed to disappear: whatever the console could not take goes to the log
                // whole. The console writes what it DID show there by itself (AppendToLogFile).
                if (lost) log?.LogInfo(msg);
            }

            [ConsoleCommand(Command = "ct_version", Description = "ContentTool: version, and whether the merged AssetsTools.NET and the embedded classdata.tpk are present.")]
            public static void CtVersion(IConsole console)
            {
                Out(console, Version());
            }

            /// <summary>
            /// The console dispatches on the main thread (GameConsole is driven from the UI update),
            /// which is where AssetBundle.LoadFromFile has to run.
            ///
            /// The optional source-bundle argument is a `params string[]`, not a C# default: the
            /// console counts declared parameters (ConsoleCommandAttribute.Invoke) and rejects a
            /// missing one, so a defaulted parameter would make the bare command unusable.
            /// </summary>
            [ConsoleCommand(Command = "ct_bake", Description = "ContentTool: bake writer regression - writes a bundle with an authored Texture2D and a binary TextAsset, then loads that file back (U0a/U0b/U1). Optional arg: source bundle file name.")]
            public static void CtBake(IConsole console, params string[] args)
            {
                try { Out(console, Bake.BakeSelfCheck.Run(args != null && args.Length > 0 ? args[0] : null)); }
                catch (Exception ex) { Out(console, "ct_bake THREW " + ex); }
            }

            [ConsoleCommand(Command = "ct_audio", Description = "ContentTool: gates A1/A2/A3 - one generated v140 bank carrying an embedded and a streamed sound, packaged inside a bundle, read back out, materialized and played (with control posts before the bank is loaded). Optional arg: source bundle file name.")]
            public static void CtAudio(IConsole console, params string[] args)
            {
                try { Out(console, Bake.AudioSelfCheck.Run(args != null && args.Length > 0 ? args[0] : null)); }
                catch (Exception ex) { Out(console, "ct_audio THREW " + ex); }
            }

            [ConsoleCommand(Command = "ct_dump", Description = "ContentTool: print the serialized field tree of a Unity class (Mesh, GameObject, Transform, MeshRenderer, ...) from the bake class database. Args: <ClassName> [depth, default 3].")]
            public static void CtDump(IConsole console, params string[] args)
            {
                try { Out(console, Bake.BakeSelfCheck.Dump(args)); }
                catch (Exception ex) { Out(console, "ct_dump THREW " + ex); }
            }

            [ConsoleCommand(Command = "ct_project", Description = "ContentTool: import a content project (ppcontent.json + Content\\Textures\\*.png + Content\\Audio\\*.wav *.ogg *.mp3) and bake it into its Dist bundle, then load that bundle back. .wav, .ogg and .mp3 are all decoded by the tool itself - no flag, no external converter. Optional args: <project> [twice] - 'twice' runs gate B1, re-baking while the project's bundle is held open the way an enabled mod holds it. With no project, a generated sample under the mod folder is used.")]
            public static void CtProject(IConsole console, params string[] args)
            {
                try
                {
                    // A NAME under the mod folder, never a path - see ProjectDir.
                    string root = ProjectDir(args != null && args.Length > 0 ? args[0] : null);
                    if (args == null || args.Length == 0)
                    {
                        // Rewritten whenever it predates the current sample, so an older generated
                        // copy on disk cannot silently skip the gates the new one exercises.
                        string meta = Path.Combine(root, "ppcontent.json");
                        if (!File.Exists(meta) || !File.ReadAllText(meta).Contains(Bake.ProjectBake.SampleStamp))
                            Out(console, "wrote sample project to " + Bake.ProjectBake.WriteSample(root));
                    }
                    // Gate B1: the authoring loop itself - bake, hold the result the way an enabled
                    // mod's own DLL holds it, bake again. A single bake cannot see that collision.
                    if (args != null && args.Length > 1 && args[1].ToLowerInvariant() == "twice")
                    { Out(console, Bake.BundleResidency.Gate(root)); return; }

                    // One synchronous path for every project. .ogg/.mp3 used to need an armed
                    // coroutine because the ENGINE decoded them and cannot answer in the frame it
                    // was asked; the tool decodes them itself now (WwisePcm.ReadAudio).
                    Out(console, Bake.ProjectBake.Run(root));
                }
                catch (Exception ex) { Out(console, "ct_project THREW " + ex); }
            }

            /// <summary>
            /// The release step, in game, so a modder never needs a script outside it. The rule is
            /// Project.Package - the same body package.ps1 reaches through tools\Package - and every
            /// refusal it implements is its, not restated here.
            ///
            /// IT COMPILES NOTHING. The author's DLL is built in the author's IDE; this picks up the
            /// one meta.json names (Package.BuiltAssembly) and, when it names one that is nowhere,
            /// refuses the package by that name rather than shipping a mod the game will not load.
            ///
            /// The output folder is REMOVED first, exactly as package.ps1 does it: the packager
            /// refuses a non-empty target on purpose, so re-running the verb after a bake has to
            /// start from nothing rather than merge into last run's release.
            /// </summary>
            [ConsoleCommand(Command = "ct_package", Description = "ContentTool: build the PUBLISHABLE folder for a content mod - copies only what a player needs, refuses BY NAME anything that would redistribute Phoenix Point's own data, and says what to zip. It does NOT compile and does NOT bake: build your DLL in your IDE, run 'ct_project'/'ct_sound bake' first. Writes to <persistentDataPath>\\ContentTool\\Packaged\\<project>. Args: <project>.")]
            public static void CtPackage(IConsole console, params string[] args)
            {
                try
                {
                    if (args == null || args.Length == 0 || string.IsNullOrEmpty(args[0]))
                    { Out(console, "usage: ct_package <project>"); return; }

                    // A NAME under the mod folder, never a path - see ProjectDir.
                    string root = ProjectDir(args[0]);
                    string outDir = Path.Combine(Path.Combine(Path.Combine(
                        UnityEngine.Application.persistentDataPath, "ContentTool"), "Packaged"),
                        Path.GetFileName(root.TrimEnd('\\', '/')));
                    if (Directory.Exists(outDir)) Directory.Delete(outDir, true);

                    bool ok;
                    Out(console, Project.Package.Run(root, outDir, Project.Package.BuiltAssembly(root), out ok));
                }
                catch (Exception ex) { Out(console, "ct_package THREW " + ex); }
            }

            /// <summary>
            /// The falsifiable check for the sink itself: this exact payload is what used to blank the
            /// console. Control in the same run: the first line must still be readable AFTER the giant
            /// block, so a console that survived is proven, not assumed.
            /// </summary>
            [ConsoleCommand(Command = "ct_outtest", Description = "ContentTool: console output sink check - emits 200 lines plus one 5000-character line. Before the per-line fix this blanked the whole console.")]
            public static void CtOutTest(IConsole console)
            {
                System.Text.StringBuilder b = new System.Text.StringBuilder();
                b.AppendLine("ct_outtest HEAD - readable = sink survived; expect a clipped y-line, 80 lines, then '... more line(s) not shown'");
                b.AppendLine(new string('y', 5000));
                for (int i = 0; i < 200; i++) b.AppendLine("ct_outtest line " + i + " " + new string('x', 120));
                b.Append("ct_outtest TAIL - only in Player.log");
                Out(console, b.ToString());
            }

            [ConsoleCommand(Command = "ct_route7", Description = "ContentTool: route vii - replacement of a shipped bundle, LIVE. Bakes a patched private copy into the mod's own AppData folder and redirects the game's live Addressables locations at it; nothing is written into the game installation. Args: apply <project> | status.")]
            public static void CtRoute7(IConsole console, params string[] args)
            {
                try { Out(console, Bake.Route7.Run(args)); }
                catch (Exception ex) { Out(console, "ct_route7 THREW " + ex); }
            }

            [ConsoleCommand(Command = "ct_catalog", Description = "ContentTool: gate C1 - route iii, LIVE. Appends an Addressables locator so the game serves a NEW key out of the MOD's own bundle; nothing is written into the game installation and no restart is needed. Args: apply [project] | verify | status.")]
            public static void CtCatalog(IConsole console, params string[] args)
            {
                try { Out(console, Bake.Route7.RunCatalog(args)); }
                catch (Exception ex) { Out(console, "ct_catalog THREW " + ex); }
            }

            [ConsoleCommand(Command = "ct_video", Description = "ContentTool: gate V1 - video, LIVE. Serves a project's .webm out of the MOD's own folder by pointing the streamable catalog row at it IN MEMORY (\"asset\" names the shipped clip to replace; no \"asset\" ADDS a row with a derived RuntimeKey). Nothing is copied into StreamingAssets and no restart is needed. Args: live [project] | status | defs | resolve <key> | open <key> | play <defname> | quit.")]
            public static void CtVideo(IConsole console, params string[] args)
            {
                try { Out(console, Bake.VideoCatalog.Run(args)); }
                catch (Exception ex) { Out(console, "ct_video THREW " + ex); }
            }

            [ConsoleCommand(Command = "ct_sound", Description = "ContentTool: replacement of an EXISTING shipped sound, baked INTO THE MOD. bake builds one media-only .bnk per replacement into the mod's own Dist\\Sounds, which ContentTool loads at init - no shipped .wem is overwritten and no shipped .bnk is patched. Args: bake [project] | selftest | probe <mediaId> | probe event <eventId> | shapec [mediaId] | status [mediaId].")]
            public static void CtSound(IConsole console, params string[] args)
            {
                try { Out(console, Bake.SoundReplace.Run(args)); }
                catch (Exception ex) { Out(console, "ct_sound THREW " + ex); }
            }

            [ConsoleCommand(Command = "ct_voices", Description = "ContentTool: DEV instrument - counts what the GAME posts. 'watch [seconds]' patches AkSoundEngine.PostEvent, prints a timeline plus live voice counts at t=2/6/12/20s. Answers whether a sound is being re-posted (accumulation) or re-entered (one voice), which no S1 arm can see.")]
            public static void CtVoices(IConsole console, params string[] args)
            {
                try { Out(console, Dev.MusicWatch.Run(args)); }
                catch (Exception ex) { Out(console, "ct_voices THREW " + ex); }
            }

            [ConsoleCommand(Command = "ct_list", Description = "ContentTool: what is IN the game - the discovery half of extraction. Args: bundles [nameFilter] | assets <bundleFile> [typeFilter] [nameFilter] | videos [nameFilter] | audio [nameFilter] | defs <nameFilter> [typeFilter] | bones <bundleFile> <meshName> [nameFilter] (the skeleton a shipped Mesh is skinned to, in m_BindPose order - the names a replacement rig must spell) | props <bundleFile> <materialName> (the property names a \"material\": \"_Prop=value\" row takes) | clip <bundleFile> <clipName> (one named AnimationClip's fields).")]
            public static void CtList(IConsole console, params string[] args)
            {
                try { Out(console, Dev.Extract.List(args)); }
                catch (Exception ex) { Out(console, "ct_list THREW " + ex); }
            }

            [ConsoleCommand(Command = "ct_extract", Description = "ContentTool: pull a shipped asset out as an editable file. Args: tex <bundleFile> <assetName> | gate (X1/X2/X3).")]
            public static void CtExtract(IConsole console, params string[] args)
            {
                try { Out(console, Dev.Extract.Run(args)); }
                catch (Exception ex) { Out(console, "ct_extract THREW " + ex); }
            }

            [ConsoleCommand(Command = "ct_fmt", Description = "ContentTool (dev spike): gate F1 - hands every file in <mod>\\FormatProbes\\ to the engine's own image/audio/video decoders and prints which formats this install actually decodes. Write the probes with tools\\make-format-probes.ps1.")]
            public static void CtFmt(IConsole console)
            {
                try { Out(console, Dev.FormatProbe.Run()); }
                catch (Exception ex) { Out(console, "ct_fmt THREW " + ex); }
            }

            [ConsoleCommand(Command = "ct_replace", Description = "ContentTool (dev workbench): write a texture (.png), a model (.glb/.obj) or a material property into a slot a target path names, live, no restart - re-applied whenever Addressables re-acquires the prefab. Args: <targetpath> <file|value>. Needs ct_seamprobe on (guid:) or ct_scan on (name:).")]
            public static void CtReplace(IConsole console, params string[] args)
            {
                try { Out(console, Dev.SeamSwap.Replace(args)); }
                catch (Exception ex) { Out(console, "ct_replace THREW " + ex); }
            }

            [ConsoleCommand(Command = "ct_fit", Description = "ContentTool (dev workbench): dial a built weapon's fit in LIVE - move its mesh in the soldier's hand while you look at it, no rebuild and no restart, and print the ready-to-paste manifest block (scale/rotate/offset). A box fit aligns CENTRES and a hand grips a GRIP, so the last centimetres are a thing only an eye can judge. Args: [show] | <weapon> [<dx,dy,dz> | move <dx,dy,dz> | pos <x,y,z> | turn <dx,dy,dz> | rot <x,y,z> | scale <f> | save | reload]. 'save' writes scale/rotate/offset back into the mod's OWN ppcontent.json, every other byte preserved.")]
            public static void CtFit(IConsole console, params string[] args)
            {
                try { Out(console, Tactical.WeaponBuild.Fit(args)); }
                catch (Exception ex) { Out(console, "ct_fit THREW " + ex); }
            }

            [ConsoleCommand(Command = "ct_bench", Description = "ContentTool (dev workbench): the WEAPON FIT WORKBENCH - a full-screen view with a unit standing in the squad bay and lists to pick a unit template, pick a weapon, nudge its position/rotation/scale per axis and save. Same service ct_fit drives, with an eye on it. Needs a loaded geoscape campaign (the squad bay lives there); shipped weapons are viewable for comparison but have no manifest row to tune or save. Hotkey Ctrl+Alt+B (NOT F9 - that is the game's own quickload). Args: [open|close], none toggles.")]
            public static void CtBench(IConsole console, params string[] args)
            {
                try { Out(console, Dev.FitBench.Run(args)); }
                catch (Exception ex) { Out(console, "ct_bench THREW " + ex); }
            }

            [ConsoleCommand(Command = "ct_revert", Description = "ContentTool (dev workbench): put every replaced asset back.")]
            public static void CtRevert(IConsole console)
            {
                try { Out(console, Dev.SeamSwap.Revert()); }
                catch (Exception ex) { Out(console, "ct_revert THREW " + ex); }
            }

            [ConsoleCommand(Command = "ct_texswap", Description = "ContentTool: gate R2 - in one run, sha1 a shipped texture, replace it, sha1 (differs), revert, sha1 (equals the first), with an untouched second texture as the control. Needs ct_seamprobe on.")]
            public static void CtTexSwap(IConsole console)
            {
                try { Out(console, Dev.SeamSwap.TexSwapGate()); }
                catch (Exception ex) { Out(console, "ct_texswap THREW " + ex); }
            }

            [ConsoleCommand(Command = "ct_meshswap", Description = "ContentTool: gate R3 - in one run, swap a skinned renderer's mesh and material, log vert count/bounds/shader, then revert to the exact origin objects, with a sibling renderer as the untouched control. Needs ct_seamprobe on.")]
            public static void CtMeshSwap(IConsole console)
            {
                try { Out(console, Dev.SeamSwap.MeshSwapGate()); }
                catch (Exception ex) { Out(console, "ct_meshswap THREW " + ex); }
            }

            [ConsoleCommand(Command = "ct_liveswap", Description = "ContentTool: gate R5 - drive the real ct_replace with a .glb onto a renderer that is already on screen (vertex/index counts from the file itself), rebind it to the target's own skeleton, change a material property, revert both to the origin OBJECTS, with an untouched second renderer as the control.")]
            public static void CtLiveSwap(IConsole console)
            {
                try { Out(console, Dev.LiveMesh.Gate()); }
                catch (Exception ex) { Out(console, "ct_liveswap THREW " + ex); }
            }

            [ConsoleCommand(Command = "ct_dev", Description = "ContentTool (dev workbench): the DEVELOPER LIVE LOOP - edit a .png/.glb on disk and the running game shows it, no restart, and F12 cycles variant sets (<project>\\select\\<Set>\\<same filename>). OFF by default and off for every player: with it off there is no watcher, no coroutine, no hotkey poll and no scan. Args: on [project] | off | status | sets | set <name> | next | reload.")]
            public static void CtDev(IConsole console, params string[] args)
            {
                try { Out(console, Dev.DevRunner.Run(args)); }
                catch (Exception ex) { Out(console, "ct_dev THREW " + ex); }
            }

            [ConsoleCommand(Command = "ct_scan", Description = "ContentTool (dev workbench): gate R4 - the scan fallback for targets with no guid: anchor. OFF by default; an ambiguous name is refused, never guessed; a scan-found target can never be baked. Args: on | off | status (default) | gate.")]
            public static void CtScan(IConsole console, params string[] args)
            {
                try { Out(console, Dev.DevScan.Run(args)); }
                catch (Exception ex) { Out(console, "ct_scan THREW " + ex); }
            }

            [ConsoleCommand(Command = "ct_mission", Description = "ContentTool (dev workbench): gate M1 - the seam measured INSIDE a loaded tactical mission instead of on the roster. Loads a savegame that declares IsTacticalSave, waits for the mission to be live, then reports coverage and runs R2/R3 there. Args: list | gate <savename>.")]
            public static void CtMission(IConsole console, params string[] args)
            {
                try { Out(console, Dev.MissionGate.Run(args)); }
                catch (Exception ex) { Out(console, "ct_mission THREW " + ex); }
            }

            [ConsoleCommand(Command = "ct_creature", Description = "ContentTool: gate C1 - spawn a custom-model creature (a character template with a rig and NO bodypart items) into a live tactical mission and prove it is hoverable, shootable and killable. Args: [list | gate <tactical savename>].")]
            public static void CtCreature(IConsole console, params string[] args)
            {
                try { Out(console, Tactical.CreatureGate.Run(args)); }
                catch (Exception ex) { Out(console, "ct_creature THREW " + ex); }
            }

            [ConsoleCommand(Command = "ct_music", Description = "ContentTool (dev instrument): what is AUDIBLE on the screen you are on right now - every live Wwise voice, named off the shipped bank .txt, and whether it is MUSIC. A run in which no voice could be named reads VOID, never SILENT. Args: probe [waitSeconds] | gate <savename> (loads a save, waits for the level to play, then probes).")]
            public static void CtMusic(IConsole console, params string[] args)
            {
                try { Out(console, Dev.MusicProbe.Run(args)); }
                catch (Exception ex) { Out(console, "ct_music THREW " + ex); }
            }

            [ConsoleCommand(Command = "ct_seamprobe", Description = "ContentTool: gate R1 - postfix AddonSkinDataBase.GetPrefabAsset, log (AssetGUID, prefab, renderer subpath), write ONE prefab to see whether the write survives, and take a scan pass as the control. Args: on | off | report (default).")]
            public static void CtSeamProbe(IConsole console, params string[] args)
            {
                try { Out(console, Dev.SeamSwap.Run(args)); }
                catch (Exception ex) { Out(console, "ct_seamprobe THREW " + ex); }
            }
        }
    }
}
