using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Morgott.ContentTool.Dev
{
    /// <summary>
    /// NATIVE WIDGETS INSIDE AN IMGUI SCREEN (stage B of the native move). A screen's body is still laid out
    /// by IMGUI; for the few pieces that carry the game's look best - THE main button, the PASS / WARN / FAIL
    /// badge, a stepper row's status word - IMGUI only RESERVES the space (the same rect the IMGUI control
    /// would have taken, so the layout does not move by a pixel) and the native widget is drawn under it on
    /// the shell's canvas, clipped to the panel's scroll view.
    ///
    /// Per pass: <see cref="BeginPass"/> resets the call counter; each slot call is numbered in call order;
    /// on Repaint its rect is recorded in screen space; <see cref="EndRepaint"/> puts the pooled widgets
    /// there. A native press is REMEMBERED by slot number and handed back by that same slot call on the
    /// next non-Layout event - normally the IMGUI MouseUp of the very same click, i.e. exactly the event an
    /// IMGUI button would have answered on - so the screens' press handling is untouched.
    /// </summary>
    internal static class BenchSlots
    {
        private struct MainRec { internal Rect R; internal string Label, Tip; internal bool On; }
        private struct TextRec { internal Rect R; internal string Text; internal Color Color; internal int Size; internal TextAnchor Anchor; }

        private static readonly List<MainRec> mains = new List<MainRec>();
        private static readonly List<TextRec> texts = new List<TextRec>();
        private static readonly List<NativeKit.Widget> mainPool = new List<NativeKit.Widget>();
        private static readonly List<Text> textPool = new List<Text>();
        private static RectTransform layer;
        private static int mainIndex, textIndex;
        private static int pressed = -1, pressedFrame;
        private static bool inPanel;
        private static Rect view;

        /// <summary>Is the IMGUI code running now inside the left panel's scroll view (the only place slots
        /// are used)? Set by FitBench around its panel.</summary>
        internal static bool Active { get { return inPanel && BenchShell.Live; } }

        internal static void PanelBegin(Rect screenView) { inPanel = true; view = screenView; }
        internal static void PanelEnd() { inPanel = false; }

        internal static void BeginPass()
        {
            mainIndex = textIndex = 0;
            if (Event.current.type == EventType.Repaint) { mains.Clear(); texts.Clear(); }
            // A press nobody took within a few frames (the screen changed under it) is dropped, never
            // delivered to whatever slot takes its number later.
            if (pressed >= 0 && Time.frameCount - pressedFrame > 3) pressed = -1;
        }

        internal static void Reset()
        {
            mainPool.Clear(); textPool.Clear(); layer = null; pressed = -1;
            mains.Clear(); texts.Clear();
        }

        /// <summary>The slot of a main button: reserves <paramref name="r"/> and answers a native press.</summary>
        internal static bool Main(Rect r, string label, bool on, string tip)
        {
            int i = mainIndex++;
            EventType t = Event.current.type;
            if (t == EventType.Repaint) mains.Add(new MainRec { R = ToScreen(r), Label = label, On = on, Tip = tip });
            if (t == EventType.Layout || pressed != i) return false;
            pressed = -1;
            return on;
        }

        /// <summary>The slot of a coloured word or line in the game's font.</summary>
        internal static void Text(Rect r, string s, Color c, int size, TextAnchor anchor)
        {
            textIndex++;
            if (Event.current.type == EventType.Repaint)
                texts.Add(new TextRec { R = ToScreen(r), Text = s, Color = c, Size = size, Anchor = anchor });
        }

        private static Rect ToScreen(Rect r)
        {
            Vector2 p = GUIUtility.GUIToScreenPoint(r.position);
            return new Rect(p.x, p.y, r.width, r.height);
        }

        /// <summary>After the Repaint pass: the pooled widgets onto the recorded rects, the rest hidden.</summary>
        internal static void EndRepaint()
        {
            if (Event.current.type != EventType.Repaint || !BenchShell.Live) return;
            try
            {
                Transform root = BenchShell.Root;
                if (root == null) return;
                if (layer == null)
                {
                    layer = NativeKit.Rect(root, "Slots");
                    layer.gameObject.AddComponent<RectMask2D>();
                }
                // Under the band's widgets (the band never overlaps the view) but over the panel ground.
                NativeKit.Place(layer, view.x, view.y, view.width, view.height);

                for (int i = 0; i < mains.Count; i++)
                {
                    if (mainPool.Count <= i)
                    {
                        int slot = i;
                        mainPool.Add(NativeKit.Button(layer, "Main" + i, "", () => { pressed = slot; pressedFrame = Time.frameCount; }));
                    }
                    MainRec m = mains[i];
                    NativeKit.Widget w = mainPool[i];
                    if (!w.Box.gameObject.activeSelf) w.Box.gameObject.SetActive(true);
                    NativeKit.Place(w, m.R.x - view.x, m.R.y - view.y, m.R.width, m.R.height);
                    w.SetLabel(m.Label.ToUpperInvariant());
                    w.SetInteractable(m.On);
                    w.Box.name = "Main " + m.Label;
                }
                for (int i = mains.Count; i < mainPool.Count; i++)
                    if (mainPool[i].Box.gameObject.activeSelf) mainPool[i].Box.gameObject.SetActive(false);

                for (int i = 0; i < texts.Count; i++)
                {
                    if (textPool.Count <= i) textPool.Add(NativeKit.Label(layer, "Text" + i, 12, Color.white, TextAnchor.MiddleLeft));
                    TextRec x = texts[i];
                    Text t = textPool[i];
                    if (!t.gameObject.activeSelf) t.gameObject.SetActive(true);
                    NativeKit.Place(t.rectTransform, x.R.x - view.x, x.R.y - view.y, x.R.width, x.R.height);
                    if (t.text != x.Text) t.text = x.Text;
                    t.color = x.Color;
                    t.alignment = x.Anchor;
                    t.fontSize = Mathf.RoundToInt(NativeKit.U(x.Size));
                }
                for (int i = texts.Count; i < textPool.Count; i++)
                    if (textPool[i].gameObject.activeSelf) textPool[i].gameObject.SetActive(false);
            }
            catch (Exception ex) { BenchShell.Fail("the native slots threw " + ex); }
        }
    }
}
