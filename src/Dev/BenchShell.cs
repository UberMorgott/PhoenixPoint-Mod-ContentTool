using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Morgott.ContentTool.Dev
{
    /// <summary>
    /// THE BENCH'S NATIVE SHELL: the frame every screen shares, built from the game's own widgets
    /// (<see cref="NativeKit"/>) on the bench's OWN canvas - the game's canvases are all hidden while the
    /// bench is up (FitBench.Hide), so nothing of theirs can be borrowed in place.
    ///
    /// What it draws (stage A of the native move, agreed with Codex 2026-09-28): the left panel's ground,
    /// the top band - [Home] [Back] ... [Reset view] [Close], the breadcrumb, the screen's title and its step
    /// bar - the Home screen's task cards, and the opaque ground of the screens with no 3D view. Everything
    /// else (the screens' bodies, the right-hand panes) is still IMGUI, drawn ON TOP of this canvas, which
    /// is why the IMGUI area simply starts under <see cref="BandHeight"/>.
    ///
    /// IMGUI DISCIPLINE, kept: <see cref="Live"/> is decided ONCE per frame, at the Layout pass
    /// (<see cref="BeginFrame"/>), so every event of a frame lays out the same IMGUI controls. Every press
    /// here is a REQUEST - BenchNav's own for Home/Back/crumb/step, <see cref="TakeTab"/>,
    /// <see cref="TakeClose"/>, <see cref="TakeReset"/> for the rest - applied at the next Layout exactly
    /// where the IMGUI presses are.
    ///
    /// NEVER A CRASH: any exception out of the shell switches it off for the rest of the session and says
    /// so once in the log; the next Layout draws the IMGUI frame instead.
    /// </summary>
    internal static class BenchShell
    {
        internal struct Card
        {
            internal string Name, Line, Refusal;
            internal int Tab;
        }

        /// <summary>What the frame shows this pass, handed down by FitBench.</summary>
        internal sealed class Frame
        {
            internal string Title, CloseLabel, HomeHint;
            internal bool Home, Free, ModelScreen;
            internal float PanelWidth;
            internal Card[] Cards;
        }

        internal static bool Live { get; private set; }
        internal static float BandHeight { get; private set; }

        private static bool failed;
        private static string failure;
        private static GameObject root;
        private static RectTransform band, cardsBox, crumbsBox, stepsBox;
        private static Image ground, panel;
        private static NativeKit.Widget home, back, reset, close;
        private static Text title, hint;
        private static readonly List<NativeKit.Widget> cardTabs = new List<NativeKit.Widget>();
        private static readonly List<Text[]> cardTexts = new List<Text[]>();
        private static string crumbKey, stepKey;
        private static int builtW, builtH;
        private static int wantTab = -1;
        private static bool wantClose, wantReset;

        private static readonly Color Accent = new Color(0.45f, 0.75f, 1f);
        private static readonly Color Grey = new Color(0.62f, 0.66f, 0.72f);
        private static readonly Color Plain = new Color(0.8f, 0.82f, 0.86f);
        private static readonly Color Done = new Color(0.35f, 0.82f, 0.45f);

        /// <summary>Is <paramref name="c"/> the shell's own canvas? FitBench.Hide skips it by REFERENCE.</summary>
        internal static bool Owns(Canvas c) { return root != null && c != null && c.gameObject == root; }

        internal static string Failure { get { return failure; } }

        /// <summary>The shell's canvas root (null while it is down) - BenchSlots parents its layer here.</summary>
        internal static Transform Root { get { return root != null ? root.transform : null; } }

        /// <summary>Builds the shell. Called by FitBench.Open AFTER it hid the game's canvases.</summary>
        internal static void Open(Func<float> panelWidth)
        {
            Live = false;
            // A failure closes the native frame for ONE bench visit; the next open tries again.
            failed = false;
            try
            {
                if (!NativeKit.Resolve())
                {
                    Fail(NativeKit.Describe() + " - the bench keeps its plain look");
                    return;
                }
                if (EventSystem.current == null) { Fail("no EventSystem - native buttons could not be clicked"); return; }
                Build(panelWidth());
                ContentToolMain.Say("ct_bench: " + NativeKit.Describe());
            }
            catch (Exception ex) { Fail("building the native frame threw " + ex); }
        }

        /// <summary>Takes the shell down. Called by FitBench.Close before the game's canvases come back.</summary>
        internal static void Close()
        {
            Live = false;
            wantTab = -1; wantClose = wantReset = false;
            Teardown();
        }

        private static void Teardown()
        {
            if (root != null) UnityEngine.Object.Destroy(root);
            root = null;
            cardTabs.Clear(); cardTexts.Clear();
            BenchSlots.Reset();
            crumbKey = stepKey = null;
        }

        internal static void Fail(string why)
        {
            failed = true;
            Live = false;
            failure = why;
            try { Teardown(); } catch (Exception) { root = null; }
            ContentToolMain.Say("ct_bench: native frame OFF for this bench visit - " + why);
        }

        internal static bool TakeTab(out int t) { t = wantTab; wantTab = -1; return t >= 0; }
        internal static bool TakeClose() { bool w = wantClose; wantClose = false; return w; }
        internal static bool TakeReset() { bool w = wantReset; wantReset = false; return w; }

        // ------------------------------------------------------------------ build

        private static void Build(float w)
        {
            Teardown();
            root = new GameObject("ContentTool.BenchShell", typeof(RectTransform));
            UnityEngine.Object.DontDestroyOnLoad(root);
            int ui = LayerMask.NameToLayer("UI");
            if (ui >= 0) root.layer = ui;
            var canvas = root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 5000;
            // The game's own scale (NativeKit.K): clones and the game's tooltip come out exactly as in its own
            // windows. Positions are still given in screen pixels (NativeKit.Place), because the band's height
            // is the top of the IMGUI area under it.
            var scaler = root.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
            scaler.scaleFactor = NativeKit.K;
            root.AddComponent<GraphicRaycaster>();
            builtW = Screen.width; builtH = Screen.height;
            Transform r = root.transform;

            ground = NativeKit.Plate(r, "Ground", new Color(0.055f, 0.065f, 0.085f, 1f));
            NativeKit.Place(ground.rectTransform, 0f, 0f, BenchScale.W, BenchScale.H);
            panel = NativeKit.Frame(r, "Panel", new Color(0.04f, 0.05f, 0.07f, 0.94f));
            NativeKit.Place(panel.rectTransform, 0f, 0f, w, BenchScale.H);

            band = NativeKit.Rect(r, "Band");
            NativeKit.Place(band, 0f, 0f, w, 120f);
            home = NativeKit.Button(band, "Home", "HOME", BenchNav.RequestHome);
            back = NativeKit.Button(band, "Back", "< BACK", BenchNav.RequestBack);
            reset = NativeKit.Button(band, "Reset", "RESET VIEW", () => wantReset = true);
            close = NativeKit.Button(band, "Close", "CLOSE", () => wantClose = true);
            // 8 | Home 66 | 4 | Back 76 | ... | Reset 104 | 4 | Close 168 | 8 - fits the 448 px panel with room.
            const float pad = 8f, row = 28f, closeW = 168f, resetW = 104f;
            NativeKit.Place(home, pad, pad, 66f, row);
            NativeKit.Place(back, pad + 70f, pad, 76f, row);
            NativeKit.Place(close, w - pad - closeW, pad, closeW, row);
            NativeKit.Place(reset, w - pad - closeW - 4f - resetW, pad, resetW, row);
            NativeKit.Tip(home, "back to the task list (nothing you picked is lost)");
            NativeKit.Tip(back, "one step back - closes the open picker, else Home (Esc)");
            NativeKit.Tip(reset, "camera back to the start (Home key)");
            NativeKit.Tip(close, "leave the bench - the game comes back exactly as it was");

            crumbsBox = Row(band, "Crumbs", pad, 42f, w - 2f * pad, 18f);
            title = NativeKit.Label(band, "Title", 18, Color.white, TextAnchor.MiddleLeft);
            NativeKit.Place(title.rectTransform, pad, 62f, w - 2f * pad, 26f);
            stepsBox = Row(band, "Steps", pad, 92f, w - 2f * pad, 20f);

            cardsBox = NativeKit.Rect(r, "Cards");
            hint = NativeKit.Label(cardsBox, "Hint", 12, Grey, TextAnchor.UpperLeft);
        }

        private static RectTransform Row(Transform parent, string name, float x, float y, float w, float h)
        {
            RectTransform rt = NativeKit.Rect(parent, name);
            NativeKit.Place(rt, x, y, w, h);
            var g = rt.gameObject.AddComponent<HorizontalLayoutGroup>();
            g.spacing = NativeKit.U(6f);
            g.childAlignment = TextAnchor.MiddleLeft;
            g.childControlWidth = true; g.childControlHeight = true;
            g.childForceExpandWidth = false; g.childForceExpandHeight = true;
            return rt;
        }

        /// <summary>A crumb or a step: a label, and when <paramref name="click"/> is set, a link that lights
        /// white under the mouse (the Text is its own target graphic).</summary>
        private static void Link(Transform parent, string s, Color c, int size, Action click)
        {
            Text t = NativeKit.Label(parent, "Link", size, c, TextAnchor.MiddleLeft);
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.text = s;
            if (click == null) return;
            t.raycastTarget = true;
            var b = t.gameObject.AddComponent<Button>();
            b.targetGraphic = t;
            var nav = b.navigation; nav.mode = Navigation.Mode.None; b.navigation = nav;
            ColorBlock cb = b.colors;
            cb.normalColor = Color.white; cb.highlightedColor = new Color(1.6f, 1.6f, 1.6f, 1f);
            cb.pressedColor = new Color(0.8f, 0.8f, 0.8f, 1f); cb.selectedColor = Color.white;
            cb.colorMultiplier = 1.6f;
            b.colors = cb;
            b.onClick.AddListener(() =>
            {
                if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
                click();
            });
        }

        private static void Clear(Transform t)
        {
            for (int i = t.childCount - 1; i >= 0; i--)
            {
                GameObject g = t.GetChild(i).gameObject;
                g.SetActive(false);       // out of the layout NOW; Destroy only lands at the end of the frame
                UnityEngine.Object.Destroy(g);
            }
        }

        // ------------------------------------------------------------------ per frame

        /// <summary>
        /// The Layout pass, after BenchNav.BeginFrame and before anything draws: brings the shell up to
        /// date with the snapshot and decides <see cref="Live"/> for the whole frame.
        /// </summary>
        internal static void BeginFrame(Frame f)
        {
            if (Event.current.type != EventType.Layout) return;
            if (failed || root == null) { Live = false; return; }
            try
            {
                // A live resize (or a mode change) is a different BenchScale: rebuilt at the new size.
                if (Screen.width != builtW || Screen.height != builtH) Build(f.PanelWidth);
                Sync(f);
                Live = true;
            }
            catch (Exception ex) { Fail("the native frame threw " + ex); }
        }

        private static void Sync(Frame f)
        {
            ground.enabled = !f.ModelScreen;
            home.SetInteractable(f.Free && !f.Home);
            back.SetInteractable(f.Free && !f.Home);
            if (reset.Box.gameObject.activeSelf != f.ModelScreen) reset.Box.gameObject.SetActive(f.ModelScreen);
            close.SetLabel(f.CloseLabel);
            if (title.text != f.Title) title.text = f.Title;

            // The crumbs: rebuilt only when what they say changes.
            string last = BenchNav.ShownLast, task = BenchNav.ShownTitle;
            string ck = (f.Free ? "1" : "0") + "|" + task + "|" + last;
            if (ck != crumbKey)
            {
                crumbKey = ck;
                Clear(crumbsBox);
                bool free = f.Free;
                if (task == null) Link(crumbsBox, "Home", Plain, 12, null);
                else
                {
                    Link(crumbsBox, "Home", Accent, 12, free ? (Action)BenchNav.RequestHome : null);
                    Link(crumbsBox, ">", Plain, 12, null);
                    if (last == null) Link(crumbsBox, task, Plain, 12, null);
                    else
                    {
                        Link(crumbsBox, task, Accent, 12, free ? (Action)BenchNav.RequestTask : null);
                        Link(crumbsBox, ">", Plain, 12, null);
                        Link(crumbsBox, last, Plain, 12, null);
                    }
                }
            }

            // The step bar, from the steps the screen drew last pass; a done step it can re-open is a link.
            string[] steps = f.Home ? null : BenchNav.ShownSteps;
            int cur = BenchNav.ShownCurrent;
            string sk = null;
            if (steps != null)
            {
                sk = string.Join("|", steps) + "#" + cur + "#" + (f.Free ? "1" : "0");
                for (int i = 0; i < steps.Length; i++) sk += BenchNav.CanJump(i) ? "j" : "-";
            }
            if (sk != stepKey)
            {
                stepKey = sk;
                Clear(stepsBox);
                if (steps != null)
                    for (int i = 0; i < steps.Length; i++)
                    {
                        string label = (i + 1) + " " + steps[i] + (i < steps.Length - 1 ? "   >" : "");
                        int step = i;
                        if (BenchNav.CanJump(i) && f.Free)
                            Link(stepsBox, label, Done, 12, () => BenchNav.RequestJump(step));
                        else Link(stepsBox, label, i == cur ? Accent : Grey, 12, null);
                    }
            }
            if (stepsBox.gameObject.activeSelf != (steps != null)) stepsBox.gameObject.SetActive(steps != null);
            BandHeight = steps != null ? 118f : 94f;
            NativeKit.Place(band, 0f, 0f, f.PanelWidth, BandHeight);

            // The Home screen's task cards.
            if (cardsBox.gameObject.activeSelf != f.Home) cardsBox.gameObject.SetActive(f.Home);
            if (!f.Home) return;
            NativeKit.Place(cardsBox, 0f, BandHeight, f.PanelWidth, BenchScale.H - BandHeight);
            const float pad = 8f, h = 58f, gap = 6f;
            float cw = f.PanelWidth - 2f * pad;
            for (int i = 0; i < f.Cards.Length; i++)
            {
                if (cardTabs.Count <= i)
                {
                    int slot = i;
                    // Named after the task, so a driver (PPCLI `ui`) can press it by name.
                    NativeKit.Widget tab = NativeKit.Tab(cardsBox, "Card " + f.Cards[i].Name, () =>
                    {
                        Card[] cs = shownCards;
                        if (cs != null && slot < cs.Length && cs[slot].Refusal == null) wantTab = cs[slot].Tab;
                    });
                    NativeKit.Place(tab, pad, 4f + i * (h + gap), cw, h);
                    Text name = NativeKit.Label(tab.Box, "Name", 15, Color.white, TextAnchor.UpperLeft);
                    NativeKit.Place(name.rectTransform, 12f, 7f, cw - 24f, 22f);
                    Text line = NativeKit.Label(tab.Box, "Line", 12, Grey, TextAnchor.UpperLeft);
                    NativeKit.Place(line.rectTransform, 12f, 31f, cw - 24f, 22f);
                    cardTabs.Add(tab);
                    cardTexts.Add(new[] { name, line });
                }
                Card c = f.Cards[i];
                Text[] tx = cardTexts[i];
                string upper = c.Name.ToUpperInvariant();
                if (tx[0].text != upper) tx[0].text = upper;
                string sub = c.Refusal ?? c.Line;
                if (tx[1].text != sub) tx[1].text = sub;
                bool on = c.Refusal == null;
                tx[0].color = on ? Color.white : Grey;
                cardTabs[i].SetInteractable(on);
            }
            shownCards = f.Cards;
            hint.text = f.HomeHint;
            NativeKit.Place(hint.rectTransform, pad, 4f + f.Cards.Length * (h + gap) + 4f, cw, 60f);
        }

        private static Card[] shownCards;
    }
}
