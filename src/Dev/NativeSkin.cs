using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Morgott.ContentTool.Dev
{
    /// <summary>
    /// THE GAME'S LOOK FOR THE BENCH'S IMGUI BODY (stage C of the native move). The screens' bodies - pickers,
    /// lists, text fields, toggles, sliders, scroll bars, the selectable log - stay IMGUI (their press, focus,
    /// typing and copy behaviour is IMGUI's and is what the bench is built on), but they are drawn with a skin
    /// cut from the game's OWN sprites and font: the button plate of the Load Game window, its list-row tab,
    /// the text field, toggle, slider and scroll bar of the game's options screens, "Purista Semibold".
    ///
    /// PER WIDGET FALLBACK: each style is cut on its own; a sprite that is not found (or cannot be copied)
    /// leaves THAT style as the stock one and is named in the one log line <see cref="Describe"/> writes.
    ///
    /// Sprites live in atlases the CPU cannot read, so each one is copied out on the GPU (Blit of its atlas
    /// rect into a RenderTexture, then ReadPixels) and SCALED by the game's own UI scale (NativeKit.K), so a
    /// 4K-authored border is as thick as it is in the game's own windows at this resolution.
    /// </summary>
    internal static class NativeSkin
    {
        private static GUISkin skin;
        private static bool tried;
        private static readonly List<string> found = new List<string>(), missing = new List<string>();
        private static readonly List<Texture2D> owned = new List<Texture2D>();

        /// <summary>The skin, built on first use for this resolution; null when not even the font was found.</summary>
        internal static GUISkin Skin
        {
            get
            {
                if (skin != null || tried) return skin;
                tried = true;
                try { Build(); }
                catch (Exception ex)
                {
                    skin = null;
                    ContentToolMain.Say("ct_bench: native skin OFF - building it threw " + ex.GetType().Name + ": " + ex.Message);
                }
                return skin;
            }
        }

        internal static string Describe()
        {
            return "native skin: " + string.Join(", ", found.ToArray()) +
                   (missing.Count > 0 ? "; stock look kept for " + string.Join(", ", missing.ToArray()) : "");
        }

        private static readonly Color Text = new Color(0.86f, 0.88f, 0.92f);
        private static readonly Color TextHover = Color.white;
        private static readonly Color TextOff = new Color(0.45f, 0.48f, 0.53f);

        private static void Build()
        {
            foreach (Texture2D t in owned) if (t != null) UnityEngine.Object.Destroy(t);
            owned.Clear(); found.Clear(); missing.Clear();
            if (skin != null) UnityEngine.Object.Destroy(skin);
            skin = null;
            if (NativeKit.Font == null && !NativeKit.Resolve()) return;
            Font font = NativeKit.Font;
            GUISkin stock = GUI.skin;
            var s = UnityEngine.Object.Instantiate(stock);
            s.name = "ContentTool.NativeSkin";
            s.font = font;
            foreach (GUIStyle st in new[] { s.label, s.button, s.toggle, s.textField, s.textArea, s.box, s.window })
                SetFont(st, 13);
            s.label.normal.textColor = Text;

            // The game's sprites are WHITE shapes its own colour controllers tint at run time
            // (UIInteractableColorController), so each plate is cut once and tinted here in the game's own
            // dark-panel palette: fill + the grey frame line of the Load Game window's action button.
            GameObject btn = NativeKit.ButtonTemplate;
            Sprite fill = NativeKit.SpriteNamed(btn, "Background") ?? NativeKit.SpriteOf(btn);
            Sprite frame = NativeKit.SpriteNamed(btn, "Frame");
            if (Plate(s.button, "button", fill, frame, Dark, Line, HoverFill, Color.white, PressFill))
            {
                s.button.normal.textColor = Text;
                s.button.hover.textColor = s.button.active.textColor = TextHover;
                s.button.onNormal.textColor = s.button.onHover.textColor = TextHover;
                s.button.padding = new RectOffset(8, 8, 4, 4);
            }
            // BOX: the window frame.
            Plate(s.box, "box", NativeKit.FrameSprite, null, Panel, Panel, Panel, Panel, Panel);
            // TEXT FIELD / AREA: the game's input field plate.
            Sprite field = Graphic<InputField>(x => x.targetGraphic as Image);
            if (Plate(s.textField, "text field", field, frame, Field, Line, Field, Accent, Field))
            { s.textField.normal.textColor = s.textField.focused.textColor = Color.white; s.textField.padding = new RectOffset(6, 6, 3, 3); }
            if (Plate(s.textArea, "log box", field, null, Field, Field, Field, Field, Field))
            { s.textArea.normal.textColor = s.textArea.focused.textColor = Color.white; s.textArea.padding = new RectOffset(6, 6, 4, 4); }
            // TOGGLE: a box in the text field's colours, the accent square in it when on.
            Texture2D off = Tail(Box(16), 16);
            if (off != null)
            {
                Texture2D on = Square(off, Accent, 16);
                GUIStyle t = s.toggle;
                t.normal.background = t.hover.background = t.active.background = t.focused.background = off;
                t.onNormal.background = t.onHover.background = t.onActive.background = t.onFocused.background = on;
                // IMGUI stretches a toggle's background over its whole rect: the box is the LEFT border and
                // the one transparent column after it is what stretches under the label.
                t.border = new RectOffset(16, 1, 0, 0);
                t.overflow = new RectOffset(0, 0, 0, 0);
                t.padding = new RectOffset(22, 0, 1, 0);
                t.fixedHeight = 16f;
                // The game font's line is taller than the 16 px box: the label runs a few pixels past the
                // rect, so the margin below keeps the next control (a slider) out of its way.
                t.margin = new RectOffset(t.margin.left, t.margin.right, t.margin.top, 6);
                t.normal.textColor = t.onNormal.textColor = Text;
                t.hover.textColor = t.onHover.textColor = TextHover;
                found.Add("toggle");
            }
            else missing.Add("toggle");
            // SLIDER: the options screen's track and handle.
            Sprite track = Graphic<Slider>(x => Track(x)), knob = Graphic<Slider>(x => x.handleRect != null ? x.handleRect.GetComponent<Image>() : null);
            if (Plate(s.horizontalSlider, "slider track", track, null, Line, Line, Line, Line, Line)) s.horizontalSlider.fixedHeight = 4f;
            Texture2D kn = Layer(Cut(knob, 14), Text, null, Color.clear);
            if (kn != null)
            {
                GUIStyle th = s.horizontalSliderThumb;
                th.normal.background = kn;
                th.hover.background = th.active.background = Layer(Cut(knob, 14), Color.white, null, Color.clear);
                th.fixedWidth = kn.width;
                th.fixedHeight = kn.height;
                th.border = new RectOffset(0, 0, 0, 0);
                found.Add("slider knob");
            }
            else missing.Add("slider knob");
            // SCROLL BAR: the save list's own.
            Sprite bar = Graphic<Scrollbar>(x => x.GetComponent<Image>()), thumb = Graphic<Scrollbar>(x => x.handleRect != null ? x.handleRect.GetComponent<Image>() : null);
            if (Plate(s.verticalScrollbar, "scroll track", bar, null, Field, Field, Field, Field, Field)) s.verticalScrollbar.fixedWidth = 6f;
            if (Plate(s.verticalScrollbarThumb, "scroll thumb", thumb, null, Line, Line, Text, Text, Text)) s.verticalScrollbarThumb.fixedWidth = 6f;
            skin = s;
            ContentToolMain.Say("ct_bench: " + Describe());
        }

        // The game's dark-panel palette (Load Game window / TFTV card colours, pp-native-ugui-widget-kit.md).
        private static readonly Color Dark = new Color(0.06f, 0.07f, 0.09f, 0.96f);
        private static readonly Color HoverFill = new Color(0.16f, 0.19f, 0.24f, 0.98f);
        private static readonly Color PressFill = new Color(0.03f, 0.035f, 0.045f, 1f);
        private static readonly Color Field = new Color(0.03f, 0.04f, 0.055f, 0.95f);
        private static readonly Color Panel = new Color(0.07f, 0.09f, 0.12f, 0.9f);
        private static readonly Color Line = new Color(0.42f, 0.47f, 0.54f, 1f);
        private static readonly Color Accent = new Color(0.45f, 0.75f, 1f, 1f);
        private static Image Track(Slider x)
        {
            Transform bg = x.transform.Find("Background");
            if (bg != null && bg.GetComponent<Image>() != null) return bg.GetComponent<Image>();
            return x.fillRect != null ? x.fillRect.GetComponent<Image>() : null;
        }

        private static void SetFont(GUIStyle st, int size)
        {
            st.font = NativeKit.Font;
            st.fontSize = size;
            st.fontStyle = FontStyle.Normal;
        }

        /// <summary>A sliced plate on every state of <paramref name="st"/>: the <paramref name="fill"/> shape and
        /// (when given) the <paramref name="frame"/> line over it, tinted per state. False (and the stock style
        /// kept) when the fill cannot be cut.</summary>
        private static bool Plate(GUIStyle st, string what, Sprite fill, Sprite frame, Color normal, Color line,
                                  Color hover, Color hoverLine, Color press)
        {
            Texture2D f = Cut(fill, 0);
            if (f == null) { missing.Add(what); return false; }
            Texture2D fr = frame != null ? Cut(frame, 0) : null;
            if (fr != null && (fr.width != f.width || fr.height != f.height)) fr = null;
            Texture2D n = Layer(f, normal, fr, line), h = Layer(f, hover, fr, hoverLine), a = Layer(f, press, fr, hoverLine);
            Texture2D d = Layer(f, new Color(normal.r, normal.g, normal.b, normal.a * 0.6f), fr, new Color(line.r, line.g, line.b, 0.4f));
            st.normal.background = n; st.onNormal.background = h;
            st.hover.background = h; st.onHover.background = h;
            st.active.background = a; st.onActive.background = a;
            st.focused.background = h; st.onFocused.background = h;
            owned.Add(d);
            const float k = 1f / 3f;
            Vector4 b = fill.border;
            st.border = new RectOffset(Mathf.CeilToInt(b.x * k), Mathf.CeilToInt(b.z * k),
                                       Mathf.CeilToInt(b.w * k), Mathf.CeilToInt(b.y * k));
            st.overflow = new RectOffset(0, 0, 0, 0);
            found.Add(what + " '" + fill.name + "'" + (fr != null ? "+'" + frame.name + "'" : ""));
            return true;
        }

        /// <summary><paramref name="under"/>'s shape in colour <paramref name="uc"/>, with <paramref name="over"/>'s
        /// shape in <paramref name="oc"/> blended on top. Sprites are white shapes, so a shape's alpha is all
        /// that is kept of it. Null when <paramref name="under"/> is.</summary>
        private static Texture2D Layer(Texture2D under, Color uc, Texture2D over, Color oc)
        {
            if (under == null) return null;
            var t = new Texture2D(under.width, under.height, TextureFormat.RGBA32, false);
            Color[] p = under.GetPixels();
            Color[] q = over != null && over.width == under.width && over.height == under.height ? over.GetPixels() : null;
            for (int i = 0; i < p.Length; i++)
            {
                float lum = Mathf.Max(p[i].r, Mathf.Max(p[i].g, p[i].b));
                Color c = new Color(uc.r * lum, uc.g * lum, uc.b * lum, uc.a * p[i].a);
                if (q != null)
                {
                    float oa = q[i].a * oc.a;
                    c = new Color(Mathf.Lerp(c.r, oc.r, oa), Mathf.Lerp(c.g, oc.g, oa), Mathf.Lerp(c.b, oc.b, oa), Mathf.Max(c.a, oa));
                }
                p[i] = c;
            }
            t.SetPixels(p);
            t.Apply();
            t.hideFlags = HideFlags.DontSave;
            owned.Add(t);
            return t;
        }

        /// <summary><paramref name="box"/> with a filled square of <paramref name="c"/> in its middle - a ticked box.</summary>
        /// <summary><paramref name="box"/> (a square, <paramref name="w"/> px) followed by a transparent column,
        /// for a style whose background is the left border only.</summary>
        private static Texture2D Tail(Texture2D box, int w)
        {
            if (box == null) return null;
            var t = new Texture2D(w + 3, box.height, TextureFormat.RGBA32, false);
            for (int y = 0; y < box.height; y++)
                for (int x = 0; x < w + 3; x++)
                    t.SetPixel(x, y, x < w && x < box.width ? box.GetPixel(x, y) : Color.clear);
            t.Apply();
            t.hideFlags = HideFlags.DontSave;
            owned.Add(t);
            return t;
        }

        /// <summary>A w x w check box in the field colours: dark fill, one-pixel frame line.</summary>
        private static Texture2D Box(int w)
        {
            var t = new Texture2D(w, w, TextureFormat.RGBA32, false);
            for (int y = 0; y < w; y++)
                for (int x = 0; x < w; x++)
                    t.SetPixel(x, y, x == 0 || y == 0 || x == w - 1 || y == w - 1 ? Line : Field);
            t.Apply();
            t.hideFlags = HideFlags.DontSave;
            owned.Add(t);
            return t;
        }

        private static Texture2D Square(Texture2D box, Color c, int w)
        {
            var t = new Texture2D(box.width, box.height, TextureFormat.RGBA32, false);
            Color[] p = box.GetPixels();
            int m = Mathf.Max(3, box.height / 4);
            for (int y = m; y < box.height - m; y++)
                for (int x = m; x < w - m; x++) p[y * box.width + x] = c;
            t.SetPixels(p);
            t.Apply();
            t.hideFlags = HideFlags.DontSave;
            owned.Add(t);
            return t;
        }
        /// <summary>The first live widget of type T with a sprite behind <paramref name="pick"/>.</summary>
        private static Sprite Graphic<T>(Func<T, Image> pick) where T : Component
        {
            Sprite best = null;
            foreach (T x in Resources.FindObjectsOfTypeAll<T>())
            {
                if (x == null) continue;
                Image img = null;
                try { img = pick(x); } catch (Exception) { }
                if (img == null || img.sprite == null) continue;
                if (x.gameObject.scene.IsValid()) return img.sprite;
                if (best == null) best = img.sprite;
            }
            return best;
        }

        /// <summary>The sprite's pixels copied out of its atlas, scaled by K (or to <paramref name="px"/>
        /// pixels tall when set). Null when the sprite cannot be cut.</summary>
        private static Texture2D Cut(Sprite sp, int px)
        {
            if (sp == null || sp.texture == null) return null;
            try
            {
                Rect r = sp.textureRect;
                Texture src = sp.texture;
                float scale = px > 0 ? px / r.height : 1f / 3f;   // virtual pixels: the game authors at 3x the bench
                int w = Mathf.Max(2, Mathf.RoundToInt(r.width * scale)), h = Mathf.Max(2, Mathf.RoundToInt(r.height * scale));
                var rt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                Graphics.Blit(src, rt, new Vector2(r.width / src.width, r.height / src.height),
                              new Vector2(r.x / src.width, r.y / src.height));
                RenderTexture was = RenderTexture.active;
                RenderTexture.active = rt;
                var t = new Texture2D(w, h, TextureFormat.RGBA32, false);
                t.ReadPixels(new Rect(0, 0, w, h), 0, 0);
                t.Apply();
                RenderTexture.active = was;
                RenderTexture.ReleaseTemporary(rt);
                t.name = "ct-native-" + sp.name;
                t.hideFlags = HideFlags.DontSave;
                owned.Add(t);
                return t;
            }
            catch (Exception) { return null; }
        }
    }
}
