using Morgott.ContentTool.Doctor;
using UnityEngine;

namespace Morgott.ContentTool.Dev
{
    /// <summary>
    /// The bench's few shared looks, so every tab says the same things the same way: a title, a grey
    /// hint, a pass/warn/fail badge with ONE sentence, a step bar, ONE main button that says why it is
    /// disabled, and a collapsed Details drawer for logs. IMGUI only - these are styles over the stock
    /// skin, built once on first use inside OnGUI (GUI.skin is only valid there).
    /// </summary>
    internal static class BenchUi
    {
        private static GUIStyle title, hint, text, badge, step, stepOn, main, card, cardSub, tip, head;
        private static readonly Color Grey = new Color(0.62f, 0.66f, 0.72f);
        private static readonly Color PassC = new Color(0.35f, 0.82f, 0.45f);
        private static readonly Color WarnC = new Color(0.95f, 0.75f, 0.25f);
        private static readonly Color FailC = new Color(0.95f, 0.38f, 0.35f);
        private static readonly Color Accent = new Color(0.45f, 0.75f, 1f);

        private static void Init()
        {
            if (title != null) return;
            title = new GUIStyle(GUI.skin.label) { fontSize = 16, fontStyle = FontStyle.Bold, wordWrap = true };
            text = new GUIStyle(GUI.skin.label) { wordWrap = true };
            head = new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold, fontSize = 13 };
            head.normal.textColor = Accent;
            hint = new GUIStyle(GUI.skin.label) { wordWrap = true, fontSize = 11 };
            hint.normal.textColor = Grey;
            badge = new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            step = new GUIStyle(GUI.skin.label) { fontSize = 11, alignment = TextAnchor.MiddleCenter };
            step.normal.textColor = Grey;
            stepOn = new GUIStyle(step) { fontStyle = FontStyle.Bold };
            stepOn.normal.textColor = Accent;
            main = new GUIStyle(GUI.skin.button) { fontSize = 14, fontStyle = FontStyle.Bold, fixedHeight = 32f };
            card = new GUIStyle(GUI.skin.button)
            {
                alignment = TextAnchor.UpperLeft,
                wordWrap = true,
                fontSize = 14,
                fontStyle = FontStyle.Bold,
                padding = new RectOffset(10, 10, 8, 26),
                fixedHeight = 58f
            };
            cardSub = new GUIStyle(hint) { fontSize = 11 };
            tip = new GUIStyle(GUI.skin.box) { wordWrap = true, fontSize = 11, alignment = TextAnchor.UpperLeft };
            tip.normal.textColor = Color.white;
        }

        internal static void Title(string s) { Init(); GUILayout.Label(s, title); }
        internal static void Text(string s) { Init(); GUILayout.Label(s, text); }
        /// <summary>A section heading inside a screen ("Build the mod").</summary>
        internal static void Section(string s) { Init(); GUILayout.Space(6f); GUILayout.Label(s, head); }
        /// <summary>A grey hint line. ALWAYS lays out one control, even for an empty string: a line that
        /// appears only when its text is non-empty appears mid-event when a press or a keystroke sets that
        /// text, and IMGUI answers a pass with a different control count than its Layout by throwing.</summary>
        internal static void Hint(string s) { Init(); GUILayout.Label(string.IsNullOrEmpty(s) ? " " : s, hint); }
        internal static void Hint(string s, string tooltip)
        {
            Init();
            GUILayout.Label(new GUIContent(string.IsNullOrEmpty(s) ? " " : s, tooltip), hint);
        }

        /// <summary>A coloured PASS / WARN / FAIL tag and one wrapped sentence beside it. The tooltip keeps
        /// the technical wording the sentence replaced.</summary>
        internal static void Badge(Grade g, string sentence, string tooltip = null)
        {
            Init();
            Color c = g == Grade.Pass ? PassC : g == Grade.Warn ? WarnC : FailC;
            badge.normal.textColor = c;
            GUILayout.BeginHorizontal();
            GUILayout.Label(new GUIContent(g == Grade.Pass ? "PASS" : g == Grade.Warn ? "WARN" : "FAIL", tooltip),
                            badge, GUILayout.Width(46f));
            GUILayout.Label(new GUIContent(sentence, tooltip), text);
            GUILayout.EndHorizontal();
        }

        /// <summary>A short coloured status word in a fixed-width cell (a stepper row's PASS / FAIL); grey
        /// when <paramref name="g"/> is null (not run).</summary>
        internal static void Mark(string word, Grade? g, string tooltip, float width)
        {
            Init();
            badge.normal.textColor = g == null ? Grey : g == Grade.Pass ? PassC : g == Grade.Warn ? WarnC : FailC;
            GUILayout.Label(new GUIContent(word, tooltip), badge, GUILayout.Width(width));
        }

        /// <summary>"1 Model  2 Target  3 Check  4 Build" with the current one lit.</summary>
        internal static void Steps(string[] names, int current)
        {
            Init();
            GUILayout.BeginHorizontal();
            for (int i = 0; i < names.Length; i++)
                GUILayout.Label((i + 1) + " " + names[i] + (i < names.Length - 1 ? "   >" : ""),
                                i == current ? stepOn : step);
            GUILayout.EndHorizontal();
        }

        /// <summary>THE one main button of a screen. Enabled only when <paramref name="refusal"/> is null
        /// (and the caller's own GUI.enabled allows it); otherwise the reason is drawn right under it, so a
        /// grey button always says why it is grey.</summary>
        internal static bool Main(string label, string refusal, string tooltip = null)
        {
            Init();
            bool was = GUI.enabled;
            GUI.enabled = was && refusal == null;
            bool pressed = GUILayout.Button(new GUIContent(label, tooltip), main);
            GUI.enabled = was;
            // The reason line is ALWAYS laid out (blank when there is none): typing a mod name can make a
            // refusal appear or vanish inside one KeyDown pass.
            Hint(refusal);
            return pressed && refusal == null;
        }

        /// <summary>A task card on the home screen: a big title and one line under it. A card that cannot
        /// be used yet is drawn disabled with its reason as the line.</summary>
        internal static bool Card(string name, string line, string refusal = null)
        {
            Init();
            bool was = GUI.enabled;
            GUI.enabled = was && refusal == null;
            bool pressed = GUILayout.Button(name, card);
            Rect r = GUILayoutUtility.GetLastRect();
            GUI.Label(new Rect(r.x + 10f, r.y + 30f, r.width - 20f, 22f), refusal ?? line, cardSub);
            GUI.enabled = was;
            GUILayout.Space(2f);
            return pressed && refusal == null;
        }

        // ---- folds. A press is RECORDED and applied on the next Layout pass (see Frame): flipping a fold
        // mid-event changes how many controls the rest of the pass draws, and IMGUI answers a pass that
        // lays out a different count than its Layout cached with an exception - which closes the bench.
        private static readonly System.Collections.Generic.HashSet<string> opened =
            new System.Collections.Generic.HashSet<string>();
        private static readonly System.Collections.Generic.HashSet<string> flips =
            new System.Collections.Generic.HashSet<string>();

        /// <summary>Applies the fold presses of the last pass. Called first thing in the bench's OnGUI.</summary>
        internal static void Frame()
        {
            if (Event.current.type != EventType.Layout || flips.Count == 0) return;
            foreach (string k in flips) if (!opened.Remove(k)) opened.Add(k);
            flips.Clear();
        }

        internal static bool IsOpen(string key) { return opened.Contains(key); }

        /// <summary>A fold header button; returns whether the fold is open THIS pass.</summary>
        internal static bool Fold(string key, string label, string tooltip = null)
        {
            bool open = opened.Contains(key);
            if (GUILayout.Button(new GUIContent((open ? "v  " : ">  ") + label, tooltip), Left())) flips.Add(key);
            return open;
        }

        /// <summary>The collapsed drawer logs and technical detail live in. Returns whether it is open.</summary>
        internal static bool Details(string key, string label = "Details")
        {
            GUILayout.Space(4f);
            return Fold(key, label);
        }

        /// <summary>A left-aligned list row button (search results).</summary>
        internal static bool Pick(GUIContent content) { return GUILayout.Button(content, Left()); }

        private static GUIStyle left;
        private static GUIStyle Left()
        {
            if (left == null) left = new GUIStyle(GUI.skin.button) { alignment = TextAnchor.MiddleLeft };
            return left;
        }

        /// <summary>A label / value line: grey label, value in normal text.</summary>
        internal static void Field(string label, string value, string tooltip = null)
        {
            Init();
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, hint, GUILayout.Width(84f));
            GUILayout.Label(new GUIContent(value, tooltip), text);
            GUILayout.EndHorizontal();
        }

        /// <summary>Draws GUI.tooltip beside the mouse. Called once per pass, after every panel has drawn
        /// (a tooltip is only known after the control that owns it).</summary>
        internal static void Tooltip()
        {
            if (Event.current.type != EventType.Repaint || string.IsNullOrEmpty(GUI.tooltip)) return;
            Init();
            Vector2 m = Event.current.mousePosition;
            var content = new GUIContent(GUI.tooltip);
            float w = 320f, h = tip.CalcHeight(content, w);
            float x = Mathf.Min(m.x + 16f, Screen.width - w - 4f), y = Mathf.Min(m.y + 18f, Screen.height - h - 4f);
            GUI.Box(new Rect(x, y, w, h), content, tip);
        }
    }
}
