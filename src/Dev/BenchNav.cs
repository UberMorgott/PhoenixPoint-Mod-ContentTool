using System;
using UnityEngine;

namespace Morgott.ContentTool.Dev
{
    /// <summary>
    /// THE BENCH'S NAVIGATION, one frame element for every screen: [Home] [Back] and the breadcrumb
    /// "Home > Replace a model > Check" at the top left, clickable done steps in each screen's step bar, and
    /// Esc as Back. A screen takes part through three calls and nothing else:
    ///   * the frame names the screen (<see cref="Screen"/>) - a screen that says nothing more still gets
    ///     Home, Back (= Home) and its crumb;
    ///   * <see cref="BenchUi.Steps"/> reports the step bar it draws (<see cref="StepsSeen"/>), so the
    ///     crumb's last word is the step the author is on;
    ///   * a screen with sub-views registers <see cref="Handlers"/>: back() closes its innermost sub-view
    ///     (a file browser, the game-part browser) and says so, jump(i) re-opens the picker of a done step.
    ///     Neither ever clears a pick - going back loses nothing.
    /// Rules (agreed with Codex 2026-09-28): Back closes the open sub-view, else goes Home; Home on Home does
    /// nothing; Esc = Back, except that Esc in a text field only drops the focus; Close stays top-right.
    ///
    /// IMGUI DISCIPLINE: what the row draws is a SNAPSHOT taken at the start of each Layout pass
    /// (<see cref="BeginFrame"/>) from what the screen reported during the previous one, so every event of a
    /// frame lays out the same controls. Every press is a REQUEST, run by <see cref="Apply"/> at the start of
    /// the next Layout, before anything is drawn.
    /// </summary>
    internal static class BenchNav
    {
        private sealed class Report
        {
            internal string Title, Sub;
            internal string[] Steps;
            internal int Current = -1;
            internal Func<bool> Back;
            internal Func<int, bool> Jump;
            internal int MaxJump = -1;
        }

        private static Report shown = new Report(), pending = new Report();
        private enum Ask { None, Home, Back, Task, Jump }
        private static Ask ask;
        private static int askStep;

        /// <summary>Layout only: the next pass draws what the screen reported during the last one.</summary>
        internal static void BeginFrame()
        {
            if (Event.current.type != EventType.Layout) return;
            shown = pending;
            pending = new Report();
        }

        /// <summary>The frame's name for the screen on show (null on Home).</summary>
        internal static void Screen(string title) { if (Event.current.type == EventType.Layout) pending.Title = title; }

        /// <summary>From BenchUi.Steps: the bar the screen drew and where the author is on it.</summary>
        internal static void StepsSeen(string[] steps, int current)
        {
            if (Event.current.type != EventType.Layout) return;
            pending.Steps = steps;
            pending.Current = current;
        }

        /// <summary>A screen's own Back (true = it closed a sub-view) and step jump (true = it opened that
        /// step's picker, for steps 0..<paramref name="maxJump"/> only), and the name of the sub-view open now, if any.</summary>
        internal static void Handlers(Func<bool> back, Func<int, bool> jump, int maxJump, string subView)
        {
            if (Event.current.type != EventType.Layout) return;
            pending.Back = back;
            pending.Jump = jump;
            pending.MaxJump = maxJump;
            pending.Sub = subView;
        }

        /// <summary>Is step <paramref name="i"/> of the bar on show a done step the author can jump back to?</summary>
        internal static bool CanJump(int i) { return shown.Jump != null && i >= 0 && i < shown.Current && i <= shown.MaxJump; }
        internal static void RequestJump(int i) { ask = Ask.Jump; askStep = i; }
        internal static void RequestBack() { ask = Ask.Back; }

        /// <summary>
        /// Start of the frame's Layout pass, before anything draws: runs the last pass's request and returns
        /// true when the bench should go Home. <paramref name="free"/> false (a Doctor press armed, a build
        /// running on its own tab) refuses every move, like the old "&lt; Tasks" button did.
        /// </summary>
        internal static bool Apply(bool free)
        {
            if (Event.current.type != EventType.Layout || ask == Ask.None) return false;
            Ask a = ask; ask = Ask.None;
            if (!free || shown.Title == null) return false;
            switch (a)
            {
                case Ask.Home: return true;
                case Ask.Task: if (shown.Back != null) while (shown.Back()) { } return false;
                case Ask.Jump: if (shown.Jump != null && askStep < shown.Current) shown.Jump(askStep); return false;
                default: return shown.Back == null || !shown.Back();
            }
        }

        /// <summary>
        /// THE ROW: [Home] [Back] and the crumbs. Constant control count for a frame (from the snapshot).
        /// Buttons are dead while <paramref name="free"/> is false.
        /// </summary>
        internal static void Row(bool free)
        {
            bool home = shown.Title == null;
            bool was = GUI.enabled;
            GUI.enabled = was && free && !home;
            if (GUILayout.Button(new GUIContent("Home", "back to the task list (nothing you picked is lost)"),
                                 GUILayout.Width(56f))) ask = Ask.Home;
            if (GUILayout.Button(new GUIContent("< Back", "one step back - closes the open picker, else Home (Esc)"),
                                 GUILayout.Width(64f))) ask = Ask.Back;
            GUI.enabled = was;
        }

        /// <summary>The breadcrumb line under the row: "Home > Task > Step", every crumb but the last clickable.</summary>
        internal static void Crumbs(bool free)
        {
            GUILayout.BeginHorizontal();
            bool was = GUI.enabled;
            GUI.enabled = was && free;
            if (shown.Title == null) GUILayout.Label("Home", Style(true));
            else
            {
                if (GUILayout.Button("Home", Style(false))) ask = Ask.Home;
                GUILayout.Label(">", Style(true));
                string last = shown.Sub ?? (shown.Steps != null && shown.Current >= 0 && shown.Current < shown.Steps.Length
                                            ? shown.Steps[shown.Current] : null);
                if (last == null) GUILayout.Label(shown.Title, Style(true));
                else
                {
                    if (GUILayout.Button(shown.Title, Style(false))) ask = Ask.Task;
                    GUILayout.Label(">", Style(true));
                    GUILayout.Label(last, Style(true));
                }
            }
            GUI.enabled = was;
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
        }

        /// <summary>Esc = Back, run at the END of OnGUI so the gizmo and the bone inspector, which take Esc
        /// first, have already had it. In a text field Esc only drops the focus.</summary>
        internal static void Escape(bool free)
        {
            Event e = Event.current;
            if (e.type != EventType.KeyDown || e.keyCode != KeyCode.Escape) return;
            if (GUIUtility.keyboardControl != 0) { GUI.FocusControl(null); GUIUtility.keyboardControl = 0; e.Use(); return; }
            if (free && shown.Title != null) ask = Ask.Back;
            e.Use();
        }

        private static GUIStyle crumb, crumbOn;
        private static GUIStyle Style(bool plain)
        {
            if (crumb == null)
            {
                crumb = new GUIStyle(GUI.skin.label) { fontSize = 12 };
                crumb.normal.textColor = new Color(0.45f, 0.75f, 1f);
                crumb.hover.textColor = Color.white;
                crumbOn = new GUIStyle(GUI.skin.label) { fontSize = 12 };
                crumbOn.normal.textColor = new Color(0.8f, 0.82f, 0.86f);
            }
            return plain ? crumbOn : crumb;
        }
    }
}
