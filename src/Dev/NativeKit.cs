using System;
using System.Collections.Generic;
using I2.Loc;
using PhoenixPoint.Common.View.ViewControllers;
using PhoenixPoint.Common.View.ViewModules;
using UnityEngine;
using UnityEngine.UI;

namespace Morgott.ContentTool.Dev
{
    /// <summary>
    /// THE GAME'S OWN WIDGETS, for the bench's native shell (<see cref="BenchShell"/>). Nothing here is drawn
    /// by hand: the button, the list-row "tab", the close X and the window frame are CLONES of the Load Game
    /// window's parts, and the font is the game's own "Purista Semibold" - so the bench reads as part of the
    /// game. Research + every file:line: docs\research\pp-native-ugui-widget-kit.md (outer repo).
    ///
    /// WHERE FROM. The bench only ever opens on the geoscape, so the source is one the geoscape has too:
    /// <c>UIModuleSaveGame</c> - the Load/Save window every pause menu carries - through its
    /// <c>SaveSlotPrefab</c> (UIButton_LOAD, the row's Background/UITab) and <c>CloseSubmenuButton</c>. Found
    /// by TYPE (Resources.FindObjectsOfTypeAll), never by a guessed scene path, and preferring a scene
    /// instance over a prefab asset.
    ///
    /// NEVER A CRASH. <see cref="Resolve"/> answers false and <see cref="Missing"/> says what was not found;
    /// the bench then keeps its IMGUI look for the session. Clones are made under an INACTIVE staging parent
    /// so no Awake/OnEnable runs before the clone is stripped of what would fight us: I2 <c>Localize</c>
    /// (rewrites the label on every enable), the serialized click listeners (a fresh ButtonClickedEvent - a
    /// RemoveAllListeners keeps persistent ones) and PP's own PointerClicked delegates.
    /// </summary>
    internal static class NativeKit
    {
        internal static Font Font;
        private static GameObject buttonTpl, tabTpl, closeTpl;
        private static Sprite frameSprite;
        private static Image.Type frameType;
        private static Color frameColor = Color.white;
        /// <summary>Null when every template was found; else the first thing that was not.</summary>
        internal static string Missing { get; private set; }

        /// <summary>
        /// The shell canvas's scale factor: the game's UI is authored at 3840x2160, the bench at a 720-tall
        /// virtual screen (BenchScale), so K = S / 3 - the game's own height-matched scale. Clones and the
        /// game's tooltip come out at their native proportions and grow with the window exactly like the
        /// IMGUI under them (GUI.matrix = S).
        /// </summary>
        internal static float K { get { return BenchScale.S / 3f; } }

        /// <summary>Virtual bench pixels (BenchScale) to canvas units: the canvas is authored at 3840x2160
        /// over the bench's 720-tall design, so one virtual pixel is three units at any resolution.</summary>
        internal static float U(float px) { return px * 3f; }

        /// <summary>Finds the templates. Cheap to call again: it re-checks that what it holds still exists
        /// (a scene instance dies with its level) and looks again only then.</summary>
        internal static bool Resolve()
        {
            if (buttonTpl != null && tabTpl != null && Font != null) return true;
            buttonTpl = tabTpl = closeTpl = null;
            frameSprite = null;
            Missing = null;
            UIModuleSaveGame module = null;
            foreach (UIModuleSaveGame m in Resources.FindObjectsOfTypeAll<UIModuleSaveGame>())
            {
                if (m == null || m.SaveSlotPrefab == null || m.SaveSlotPrefab.LoadGameButton == null) continue;
                module = m;
                if (m.gameObject.scene.IsValid()) break;
            }
            if (module == null) { Missing = "the game's Load Game window (UIModuleSaveGame)"; return false; }

            UIModuleSaveGameSlot slot = module.SaveSlotPrefab;
            buttonTpl = Holder(slot.LoadGameButton);
            foreach (PhoenixGeneralButton b in slot.GetComponentsInChildren<PhoenixGeneralButton>(true))
                if (b != null && b.name == "UITab") { tabTpl = b.gameObject; break; }
            if (module.CloseSubmenuButton != null) closeTpl = Holder(module.CloseSubmenuButton);

            // The window's frame: its biggest sliced Image (the hex-patterned plate behind the list).
            float best = 0f;
            foreach (Image img in module.GetComponentsInChildren<Image>(true))
            {
                if (img == null || img.sprite == null || img.type != Image.Type.Sliced) continue;
                Rect r = ((RectTransform)img.transform).rect;
                if (r.width * r.height <= best) continue;
                best = r.width * r.height;
                frameSprite = img.sprite; frameType = img.type; frameColor = img.color;
            }

            foreach (Text t in buttonTpl.GetComponentsInChildren<Text>(true))
                if (t != null && t.font != null) { Font = t.font; break; }
            if (Font == null || Font.name != "Purista Semibold")
                foreach (Font f in Resources.FindObjectsOfTypeAll<Font>())
                    if (f != null && f.name == "Purista Semibold") { Font = f; break; }

            if (buttonTpl == null) Missing = "the Load button of a save slot (UIModuleSaveGameSlot.LoadGameButton)";
            else if (tabTpl == null) Missing = "the save slot's row tab (Background/UITab)";
            else if (Font == null) Missing = "the game's font";
            return Missing == null;
        }

        internal static GameObject ButtonTemplate { get { return buttonTpl; } }
        internal static GameObject TabTemplate { get { return tabTpl; } }
        internal static Sprite FrameSprite { get { return frameSprite; } }

        /// <summary>The sprite of the first Image under <paramref name="go"/> whose sprite name contains
        /// <paramref name="part"/> (the button's "...Background", "...Frame" layers), or null.</summary>
        internal static Sprite SpriteNamed(GameObject go, string part)
        {
            if (go == null) return null;
            foreach (Image img in go.GetComponentsInChildren<Image>(true))
                if (img != null && img.sprite != null && img.sprite.name.IndexOf(part, StringComparison.OrdinalIgnoreCase) >= 0)
                    return img.sprite;
            return null;
        }

        /// <summary>The biggest sprite-carrying Image under <paramref name="go"/> - a widget's plate.</summary>
        internal static Sprite SpriteOf(GameObject go)
        {
            if (go == null) return null;
            Sprite best = null;
            float area = 0f;
            foreach (Image img in go.GetComponentsInChildren<Image>(true))
            {
                if (img == null || img.sprite == null) continue;
                Rect r = ((RectTransform)img.transform).rect;
                float a = Mathf.Max(1f, r.width * r.height);
                if (a <= area) continue;
                area = a; best = img.sprite;
            }
            return best;
        }

        /// <summary>The object that carries the PP button (its animator and hover), not the bare Unity
        /// Button that may sit on a child of it.</summary>
        private static GameObject Holder(Component c)
        {
            PhoenixGeneralButton p = c.GetComponentInParent<PhoenixGeneralButton>();
            return p != null ? p.gameObject : c.gameObject;
        }

        /// <summary>One line for the log: what was found and how big it is.</summary>
        internal static string Describe()
        {
            if (Missing != null) return "native widgets NOT found: " + Missing;
            return "native widgets: button '" + buttonTpl.name + "' " + Size(buttonTpl) + ", row tab " + Size(tabTpl) +
                   ", close " + (closeTpl != null ? Size(closeTpl) : "none") + ", frame sprite " +
                   (frameSprite != null ? "'" + frameSprite.name + "'" : "none") + ", font '" + Font.name + "', K=" +
                   K.ToString("F3");
        }

        private static string Size(GameObject g)
        {
            var r = g.transform as RectTransform;
            return r == null ? "?" : r.rect.width.ToString("F0") + "x" + r.rect.height.ToString("F0");
        }

        // ------------------------------------------------------------------ factory

        /// <summary>A plain rect under <paramref name="parent"/>, anchored top-left, in canvas pixels.</summary>
        internal static RectTransform Rect(Transform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = parent.gameObject.layer;
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0f, 1f);
            return rt;
        }

        /// <summary>Places a top-left-anchored rect at (x, y) VIRTUAL pixels (BenchScale) from its parent's
        /// top-left, w x h - in the canvas's units.</summary>
        internal static void Place(RectTransform rt, float x, float y, float w, float h)
        {
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = new Vector2(U(x), -U(y));
            rt.sizeDelta = new Vector2(U(w), U(h));
        }

        /// <summary>A label in the game's font. Never a raycast target: a label must not eat the click meant
        /// for the button under it.</summary>
        internal static Text Label(Transform parent, string name, int size, Color color, TextAnchor anchor)
        {
            RectTransform rt = Rect(parent, name);
            var t = rt.gameObject.AddComponent<Text>();
            t.font = Font;
            t.fontSize = Mathf.RoundToInt(U(size));
            t.color = color;
            t.alignment = anchor;
            t.raycastTarget = false;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Truncate;
            return t;
        }

        /// <summary>A flat colour plate (the panel ground). Not a raycast target.</summary>
        internal static Image Plate(Transform parent, string name, Color color)
        {
            RectTransform rt = Rect(parent, name);
            var img = rt.gameObject.AddComponent<Image>();
            img.color = color;
            img.raycastTarget = false;
            return img;
        }

        /// <summary>The Load Game window's frame plate, or a flat plate when it was not found.</summary>
        internal static Image Frame(Transform parent, string name, Color fallback)
        {
            Image img = Plate(parent, name, fallback);
            if (frameSprite == null) return img;
            img.sprite = frameSprite;
            img.type = frameType;
            img.color = frameColor;
            return img;
        }

        /// <summary>A native widget in a box: the clone at the game's own scale, filling the box.</summary>
        internal sealed class Widget
        {
            internal RectTransform Box;
            internal GameObject Clone;
            internal Button Button;
            internal PhoenixGeneralButton Pp;
            internal Text Text;

            internal void SetLabel(string s)
            {
                if (Clone == null) return;
                foreach (Text t in Clone.GetComponentsInChildren<Text>(true)) if (t != null && t.text != s) t.text = s;
            }

            /// <summary>Greyed and dead, the game's own way (animator state + Selectable).</summary>
            internal void SetInteractable(bool on)
            {
                // Only on a CHANGE: the PP setter also resets the button's animations, which every frame
                // would kill its hover.
                if (state == on) return;
                state = on;
                if (Button != null) Button.interactable = on;
                if (Pp != null && Pp.BaseButton != null) Pp.SetInteractable(on);
            }
            private bool? state;
        }

        internal static Widget Button(Transform parent, string name, string label, Action click)
        { return Make(buttonTpl, parent, name, label, click); }

        /// <summary>The save list's row: a wide plate that lights up under the mouse.</summary>
        internal static Widget Tab(Transform parent, string name, Action click)
        { return Make(tabTpl, parent, name, null, click); }

        internal static Widget Close(Transform parent, string name, Action click)
        { return closeTpl == null ? null : Make(closeTpl, parent, name, null, click); }

        private static Transform staging;

        private static Widget Make(GameObject tpl, Transform parent, string name, string label, Action click)
        {
            if (tpl == null) throw new InvalidOperationException("native template missing: " + name);
            if (staging == null)
            {
                var s = new GameObject("ContentTool.NativeStaging");
                s.SetActive(false);
                UnityEngine.Object.DontDestroyOnLoad(s);
                staging = s.transform;
            }
            var w = new Widget { Box = Rect(parent, name) };
            GameObject go = UnityEngine.Object.Instantiate(tpl, staging, false);
            go.name = name + ".native";
            foreach (Localize l in go.GetComponentsInChildren<Localize>(true)) UnityEngine.Object.DestroyImmediate(l);
            foreach (Button b in go.GetComponentsInChildren<Button>(true))
            {
                b.onClick = new Button.ButtonClickedEvent();
                var nav = b.navigation; nav.mode = Navigation.Mode.None; b.navigation = nav;
            }
            foreach (PhoenixGeneralButton p in go.GetComponentsInChildren<PhoenixGeneralButton>(true))
            {
                // Not RemoveAllClickedDelegates: it walks PointerClicked's invocation list and throws on a
                // button nobody subscribed to (PhoenixGeneralButton.cs:359-366).
                p.PointerClicked = null;
                p.HoldedPointerClicked = null;
                p.PointerHover = null;
                p.PointerHoverUnfiltered = null;
                p.PointerHold = null;
                p.TabbingControl = null;
            }
            w.Pp = go.GetComponent<PhoenixGeneralButton>();
            w.Button = w.Pp != null && w.Pp.BaseButton != null ? w.Pp.BaseButton : go.GetComponentInChildren<Button>(true);
            foreach (Text t in go.GetComponentsInChildren<Text>(true)) { w.Text = t; if (label != null) t.text = label; }
            Sanitize(go);

            var rt = (RectTransform)go.transform;
            rt.SetParent(w.Box, false);
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = Vector2.zero;
            rt.localScale = Vector3.one;
            w.Clone = go;
            if (w.Button != null && click != null)
                w.Button.onClick.AddListener(() =>
                {
                    // A selected button answers Submit (Enter/Space) - which the author types into the bench's
                    // text fields. Nothing of ours stays selected.
                    var es = UnityEngine.EventSystems.EventSystem.current;
                    if (es != null) es.SetSelectedGameObject(null);
                    click();
                });
            go.SetActive(true);
            return w;
        }

        /// <summary>The game's own hover tooltip (UITooltipText: its widget comes from
        /// Resources "Interface/UI_Prefabs/UI_Tooltip" and lands on the nearest root canvas - ours). A tooltip
        /// that cannot be attached is only a missing tooltip, never a failure.</summary>
        internal static void Tip(Widget w, string text)
        {
            if (w == null || w.Clone == null || string.IsNullOrEmpty(text)) return;
            try
            {
                var t = w.Clone.GetComponent<UITooltipText>();
                if (t == null) t = w.Clone.AddComponent<UITooltipText>();
                t.TipText = text;
                t.Enabled = true;
            }
            catch (Exception) { }
        }

        /// <summary>Sizes a widget's box and its clone together, in screen pixels (the clone fills the box).</summary>
        internal static void Place(Widget w, float x, float y, float width, float height)
        {
            Place(w.Box, x, y, width, height);
            if (w.Clone == null) return;
            ((RectTransform)w.Clone.transform).sizeDelta = new Vector2(U(width), U(height));
        }

        /// <summary>All the stripping, re-checked on a whole subtree: nothing under the shell may run a
        /// SoftMask (it throws every frame without a game canvas above it - FitBench.masks).</summary>
        internal static void Sanitize(GameObject root)
        {
            var kill = new List<Component>();
            foreach (Behaviour b in root.GetComponentsInChildren<Behaviour>(true))
                if (b != null && b.GetType().Name == "SoftMask") kill.Add(b);
            foreach (Component c in kill) UnityEngine.Object.DestroyImmediate(c);
        }
    }
}
