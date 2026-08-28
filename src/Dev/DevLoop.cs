using System;
using System.Collections.Generic;
using System.IO;

namespace Morgott.ContentTool.Dev
{
    /// <summary>
    /// S2, the DEVELOPER LIVE LOOP (M1 parity with Resource_Replacer): a modder edits a .png on disk
    /// and sees it in the running game, and flips between variant sets with one key.
    ///
    /// This half is deliberately free of UnityEngine types, so the parts that are pure decisions -
    /// the debounce, the cross-thread hand-off, and which file a set resolves to - are proven OFFLINE
    /// (gate S2 in tests\TargetPathTests). The Unity half is <see cref="DevRunner"/>: it owns the
    /// coroutine, the hotkey and the re-apply, and it does nothing this class has not first decided.
    ///
    /// PORTED FROM RR, WITHOUT ITS DEFECTS:
    ///   * the watcher itself         - Resource_Replacer.cs:219-225 (three watchers, one per folder;
    ///                                  one root here, because every source a mark carries is a file
    ///                                  under the project the author turned dev mode on for);
    ///   * MakeWatcher's filter set   - :491-509 (Size|LastWrite|CreationTime|FileName, subdirs on,
    ///                                  Changed+Created+Renamed - an editor that saves via a temp file
    ///                                  and renames raises only the last of those);
    ///   * mark-dirty / drain-on-the- - :219 and :2157-2185. RR times the stamp BEFORE the volatile
    ///     main-thread split             flag and then reads flag and stamp on the main thread with no
    ///                                  lock at all (:161-165, :2163-2170). Two plain fields written by
    ///                                  a watcher thread and read by another are not a hand-off: the
    ///                                  reader can see the flag against the previous stamp. Here the
    ///                                  whole hand-off is ONE lock, so a drain can never observe a
    ///                                  half-published event;
    ///   * F12 cycles the set         - :36 (NextTextureSetKey = KeyCode.F12), :539-540, :582-587.
    ///
    /// OFF COSTS NOTHING. Every entry point below returns immediately while <see cref="Enabled"/> is
    /// false, and <see cref="Off"/> disposes every watcher it made - RR keeps them in a static list
    /// too (:103, :271-275), but a mod that ships this to a player must never have made one at all.
    /// </summary>
    internal static class DevLoop
    {
        /// <summary>
        /// How long the last write has to be quiet before anything is re-read. Copying a folder of
        /// png in fires hundreds of events (RR's own reason, :2153-2156) and an editor writes a file
        /// in several bursts; re-reading on the first of them reads a half-written file.
        /// </summary>
        internal const double DebounceSeconds = 0.5;

        /// <summary>
        /// A file event cannot reveal an object that did not exist yet, so a periodic pass is what
        /// catches a binding whose target was instantiated after the swap (RR polls for the same
        /// reason, :1021-1027, ScanIntervalSeconds = 2.0 at :41). Budgeted by the caller.
        /// </summary>
        internal const double ScanSeconds = 3.0;

        /// <summary>Where a set folder is looked for, mirroring RR's Images\select\&lt;Set&gt;\ (:88).</summary>
        internal const string SetsFolder = "select";

        /// <summary>The authored files themselves - "no set", not "a set called Default".</summary>
        internal const string DefaultSet = "Default";

        internal static bool Enabled { get; private set; }

        /// <summary>The folder being watched, or null when dev mode is off.</summary>
        internal static string Root { get; private set; }

        /// <summary>Set by <see cref="DevRunner"/> only, so "what is scheduled" is one readable fact.</summary>
        internal static bool LoopOn, HotkeyOn;

        private static readonly List<FileSystemWatcher> Watchers = new List<FileSystemWatcher>();

        /// <summary>
        /// The ONE lock the watcher threads and the main thread meet over. Both the set and the stamp
        /// are inside it: they are one event, and reading them apart is RR's :1028-1035 bug.
        /// </summary>
        private static readonly object Gate = new object();
        private static readonly HashSet<string> Dirty = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static DateTime dirtyAt;

        private static DateTime nextScan;
        private static string activeSet = DefaultSet;

        internal static int WatcherCount { get { return Watchers.Count; } }

        /// <summary>
        /// How many paths are queued. Exists for gate S2: asserting only that Pump() returns null
        /// while dev mode is off is a VACUOUS pass, because Pump gates on Enabled too - the arm has
        /// to see that a watcher event off is not even RECORDED, which is the only reading that
        /// separates "nothing was scheduled" from "something was scheduled and then hidden".
        /// </summary>
        internal static int DirtyCount { get { lock (Gate) { return Dirty.Count; } } }
        internal static string ActiveSet { get { return activeSet; } }

        /// <summary>
        /// Everything dev mode has running, as one string. The falsifiable form of "off costs
        /// nothing": with dev mode off this must read all-zero, and gate S2 asserts exactly that.
        /// </summary>
        internal static string Scheduled
        {
            get
            {
                return "watchers=" + Watchers.Count +
                       " loop=" + (LoopOn ? "on" : "off") +
                       " hotkey=" + (HotkeyOn ? "on" : "off");
            }
        }

        // ------------------------------------------------------------------ on / off

        internal static string On(string root, Action<string> warn)
        {
            if (Enabled) return "ct_dev already ON, watching " + Root + " (" + Scheduled + ")";
            if (string.IsNullOrEmpty(root) || !Directory.Exists(root))
                return "ct_dev REFUSED: no such folder '" + root + "' - nothing was started";

            Root = root;
            Enabled = true;
            activeSet = DefaultSet;
            lock (Gate) { Dirty.Clear(); dirtyAt = default(DateTime); }
            nextScan = DateTime.MinValue;      // the first tick discovers, rather than waiting a scan
            Watch(root, warn);
            return "ct_dev ON, watching " + root + " (" + Scheduled + "); sets: " +
                   string.Join(", ", Sets().ToArray()) + "; active '" + activeSet + "'";
        }

        internal static string Off()
        {
            int n = Watchers.Count;
            foreach (FileSystemWatcher w in Watchers)
            {
                try { w.EnableRaisingEvents = false; w.Dispose(); }
                catch (Exception) { }
            }
            Watchers.Clear();
            Enabled = false;
            Root = null;
            activeSet = DefaultSet;
            lock (Gate) { Dirty.Clear(); dirtyAt = default(DateTime); }
            return "ct_dev OFF - " + n + " watcher(s) disposed (" + Scheduled + ")";
        }

        private static void Watch(string path, Action<string> warn)
        {
            try
            {
                FileSystemWatcher w = new FileSystemWatcher(path, "*")
                {
                    NotifyFilter = NotifyFilters.Size | NotifyFilters.LastWrite |
                                   NotifyFilters.CreationTime | NotifyFilters.FileName,
                    IncludeSubdirectories = true
                };
                FileSystemEventHandler h = (s, e) => Mark(e.FullPath, DateTime.UtcNow);
                w.Changed += h;
                w.Created += h;
                w.Renamed += (s, e) => Mark(e.FullPath, DateTime.UtcNow);
                w.EnableRaisingEvents = true;
                Watchers.Add(w);           // RETAINED, so Off() can dispose it
            }
            catch (Exception ex)
            {
                if (warn != null) warn("ct_dev: hot reload unavailable for " + path + ": " + ex.Message);
            }
        }

        // ------------------------------------------------------------------ dirty hand-off

        /// <summary>
        /// Called from a WATCHER THREAD. Records the path and pushes the quiet-period deadline out;
        /// it must never touch anything of Unity's, and it does not.
        /// </summary>
        internal static void Mark(string path, DateTime now)
        {
            if (!Enabled || string.IsNullOrEmpty(path)) return;
            lock (Gate)
            {
                Dirty.Add(path);
                dirtyAt = now;
            }
        }

        /// <summary>
        /// Called on the MAIN THREAD. Null while there is nothing to do or the last write is still
        /// inside the quiet period; otherwise the coalesced set of changed paths, drained in one go -
        /// so N writes to one file are ONE reload, and a burst across files is one pass, not N passes.
        /// </summary>
        internal static List<string> Pump(DateTime now)
        {
            lock (Gate)
            {
                if (!Enabled || Dirty.Count == 0) return null;
                if (now - dirtyAt < TimeSpan.FromSeconds(DebounceSeconds)) return null;
                List<string> paths = new List<string>(Dirty);
                Dirty.Clear();
                return paths;
            }
        }

        /// <summary>True at most once per <see cref="ScanSeconds"/>, and never while dev mode is off.</summary>
        internal static bool DueScan(DateTime now)
        {
            if (!Enabled) return false;
            if (now < nextScan) return false;
            nextScan = now + TimeSpan.FromSeconds(ScanSeconds);
            return true;
        }

        /// <summary>A scene load invalidates every live binding at once; discover on the next tick.</summary>
        internal static void ForceScan()
        {
            nextScan = DateTime.MinValue;
        }

        // ------------------------------------------------------------------ variant sets

        private static string SetsDir()
        {
            return Root == null ? null : Path.Combine(Root, SetsFolder);
        }

        /// <summary>
        /// Default first, then every &lt;root&gt;\select\&lt;Name&gt;\ folder, sorted - so the cycle order is
        /// the same on every machine whatever order the filesystem hands the directories back in.
        /// </summary>
        internal static List<string> Sets()
        {
            List<string> names = new List<string>();
            try
            {
                string dir = SetsDir();
                if (dir != null && Directory.Exists(dir))
                    foreach (string d in Directory.GetDirectories(dir))
                    {
                        string n = Path.GetFileName(d);
                        if (n.Length > 0 && !string.Equals(n, DefaultSet, StringComparison.OrdinalIgnoreCase))
                            names.Add(n);
                    }
            }
            catch (Exception) { }
            names.Sort(StringComparer.OrdinalIgnoreCase);
            names.Insert(0, DefaultSet);
            return names;
        }

        internal static string Next()
        {
            List<string> s = Sets();
            int at = s.FindIndex(n => string.Equals(n, activeSet, StringComparison.OrdinalIgnoreCase));
            activeSet = s[(at + 1) % s.Count];      // at < 0 lands on Default, which is index 0
            return activeSet;
        }

        internal static bool Select(string name, out string why)
        {
            List<string> s = Sets();
            int i = s.FindIndex(n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase));
            if (i < 0)
            {
                why = "no set named '" + name + "' (known: " + string.Join(", ", s.ToArray()) + ")";
                return false;
            }
            activeSet = s[i];
            why = null;
            return true;
        }

        /// <summary>
        /// The file a swap should actually read, given the active set: the same NAME under
        /// &lt;root&gt;\select\&lt;set&gt;\ when that set carries one, and the authored path otherwise - so a
        /// set that only re-skins two of ten bindings leaves the other eight exactly as authored
        /// (RR says the same thing per key at :605-636).
        /// </summary>
        internal static string Resolve(string source)
        {
            if (!Enabled || source == null || Root == null) return source;
            if (string.Equals(activeSet, DefaultSet, StringComparison.OrdinalIgnoreCase)) return source;
            try
            {
                string candidate = Path.Combine(Path.Combine(SetsDir(), activeSet), Path.GetFileName(source));
                return File.Exists(candidate) ? candidate : source;
            }
            catch (Exception) { return source; }
        }
    }
}
