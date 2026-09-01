using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Morgott.ContentTool.Dev
{
    /// <summary>
    /// ============ PICKING A .glb WITHOUT TYPING A PATH ============
    ///
    /// A native file dialog is not reachable from inside the player without dragging in a Windows
    /// interop dependency, and this needs three things a dependency would not buy: the DRIVES (a
    /// model lives on whichever disk the author's Blender project lives on, not under the game), a
    /// FILTER (a content folder is mostly .png and .blend1) and THE FIVE FILES he actually works on
    /// - the same mesh is re-exported and re-checked a dozen times in an afternoon.
    ///
    /// Every disk call here is wrapped, because this runs inside <c>OnGUI</c>: a card reader with no
    /// card in it and a folder the user has no rights to are both NORMAL on a real machine, and an
    /// exception out of a layout pass tears the whole panel down mid-frame. An unreadable folder is
    /// SAID IN WORDS (<see cref="problem"/>) rather than drawn as an empty one - "the drive is not
    /// ready" and "this folder has no .glb in it" look identical otherwise, and only one of them is
    /// something the author can act on.
    ///
    /// ponytail: no thumbnails, no sorting choice, no search box. Add them when picking a file is
    /// what an author complains about.
    /// </summary>
    internal sealed class GlbFileBrowser
    {
        private const int Recents = 5;
        private const string Extension = ".glb";

        private string dir;
        private string problem;
        private Vector2 scroll;
        private readonly List<string> recent = new List<string>();

        internal bool Open { get; private set; }

        internal void Show(string startDir)
        {
            dir = Exists(startDir) ? startDir : FirstDrive();
            problem = null;
            LoadRecent();
            Open = true;
        }

        internal void Hide() { Open = false; }

        /// <summary>Draws the browser and returns the picked path, or null. Call once per OnGUI while
        /// <see cref="Open"/>.</summary>
        internal string Draw(float height)
        {
            string picked = null;
            GUILayout.BeginVertical(GUI.skin.box);

            GUILayout.BeginHorizontal();
            GUILayout.Label("in: " + BenchList.Elide(dir, BenchList.NameChars));
            if (GUILayout.Button("up", GUILayout.Width(40f))) Up();
            if (GUILayout.Button("x", GUILayout.Width(24f))) Open = false;
            GUILayout.EndHorizontal();

            if (recent.Count > 0)
            {
                GUILayout.Label("recent");
                // A copy, because picking one calls Remember and reorders the list we are walking.
                foreach (string r in recent.ToArray())
                    if (GUILayout.Button(BenchList.Elide(Path.GetFileName(r), BenchList.NameChars),
                                         GUILayout.Height(18f)))
                        picked = r;
            }

            string[] subs = Subdirectories();
            string[] files = Files();
            if (problem != null) GUILayout.Label(problem);

            scroll = GUILayout.BeginScrollView(scroll, GUILayout.Height(height));
            foreach (string drive in Drives())
                if (GUILayout.Button("[" + drive + "]", GUILayout.Height(18f))) Go(drive);
            foreach (string sub in subs)
                if (GUILayout.Button("> " + BenchList.Elide(Leaf(sub), BenchList.NameChars - 2),
                                     GUILayout.Height(18f)))
                    Go(sub);
            foreach (string file in files)
                if (GUILayout.Button(BenchList.Elide(Path.GetFileName(file), BenchList.NameChars),
                                     GUILayout.Height(18f)))
                    picked = file;
            GUILayout.EndScrollView();
            GUILayout.EndVertical();

            if (picked != null) { Remember(picked); Open = false; }
            return picked;
        }

        // ------------------------------------------------------------------ disk, defensively

        private void Go(string target)
        {
            dir = target;
            problem = null;
            scroll = Vector2.zero;
        }

        private void Up()
        {
            try
            {
                DirectoryInfo parent = Directory.GetParent(dir);
                if (parent != null) Go(parent.FullName);
            }
            catch (Exception ex) { problem = "cannot leave this folder: " + ex.Message; }
        }

        private string[] Subdirectories()
        {
            try { return Sorted(Directory.GetDirectories(dir)); }
            catch (Exception ex) { problem = Unreadable(ex); return new string[0]; }
        }

        /// <summary>
        /// The .glb in this folder. The EndsWith is not decoration: Windows still matches a
        /// three-character wildcard extension against the short name, so <c>*.glb</c> alone also
        /// hands back <c>foo.glbx</c> and anything else beginning "glb" - and a file that is not a
        /// glb reaches the reader as a refusal the author cannot explain.
        /// </summary>
        private string[] Files()
        {
            string[] all;
            try { all = Directory.GetFiles(dir, "*" + Extension); }
            catch (Exception ex) { problem = Unreadable(ex); return new string[0]; }

            List<string> keep = new List<string>(all.Length);
            foreach (string f in all)
                if (f.EndsWith(Extension, StringComparison.OrdinalIgnoreCase)) keep.Add(f);
            return Sorted(keep.ToArray());
        }

        /// <summary>A drive with no disk in it, or a folder this user may not read - both normal, and
        /// both otherwise indistinguishable from "there is nothing here".</summary>
        private static string Unreadable(Exception ex)
        {
            return ex is UnauthorizedAccessException
                ? "UNAVAILABLE - no permission to read this folder."
                : "UNAVAILABLE - " + ex.GetType().Name + ": " + ex.Message;
        }

        private static string[] Drives()
        {
            try { return Directory.GetLogicalDrives(); } catch (Exception) { return new string[0]; }
        }

        private static string FirstDrive()
        {
            string[] drives = Drives();
            return drives.Length > 0 ? drives[0] : ".";
        }

        private static bool Exists(string path)
        {
            try { return !string.IsNullOrEmpty(path) && Directory.Exists(path); }
            catch (Exception) { return false; }
        }

        private static string[] Sorted(string[] paths)
        {
            Array.Sort(paths, StringComparer.OrdinalIgnoreCase);
            return paths;
        }

        /// <summary>The last segment of a FOLDER path. <c>Path.GetFileName</c> answers "" for a path
        /// ending in a separator, which is exactly what a drive root looks like.</summary>
        private static string Leaf(string path)
        {
            if (string.IsNullOrEmpty(path)) return "";
            string name = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar,
                                                        Path.AltDirectorySeparatorChar));
            return string.IsNullOrEmpty(name) ? path : name;
        }

        // ------------------------------------------------------------------ the five that matter

        /// <summary>The mod has no settings store, so this is a plain text file beside everything else
        /// ContentTool writes (<see cref="ContentToolMain.PatchedRoot"/>). One path per line, newest
        /// first.</summary>
        private static string RecentFile()
        {
            return Path.Combine(Path.Combine(Application.persistentDataPath, "ContentTool"),
                                "doctor-recent.txt");
        }

        /// <summary>Reads the recents, dropping any that have since been deleted or renamed: a button
        /// that names a file which is no longer there is worse than no button.</summary>
        private void LoadRecent()
        {
            recent.Clear();
            try
            {
                if (!File.Exists(RecentFile())) return;
                foreach (string line in File.ReadAllLines(RecentFile()))
                    if (line.Length > 0 && File.Exists(line) && recent.Count < Recents) recent.Add(line);
            }
            catch (Exception) { }
        }

        /// <summary>Newest first, no duplicates, five at most - and the browser reopens where the file
        /// was. Never throws: a recents list that cannot be written is a lost convenience, not a lost
        /// pick, and the pick has already happened by the time this runs.</summary>
        private void Remember(string path)
        {
            recent.Remove(path);
            recent.Insert(0, path);
            while (recent.Count > Recents) recent.RemoveAt(recent.Count - 1);
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(RecentFile()));
                File.WriteAllLines(RecentFile(), recent.ToArray());
            }
            catch (Exception) { }
            try
            {
                string folder = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(folder)) dir = folder;
            }
            catch (Exception) { }
        }
    }
}
