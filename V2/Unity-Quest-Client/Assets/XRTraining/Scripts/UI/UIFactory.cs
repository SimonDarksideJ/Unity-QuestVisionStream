using System;
using UnityEngine;
using UnityEngine.UI;
using XRTraining.Theme;

namespace XRTraining.UI
{
    /// <summary>
    /// Terse, themed uGUI construction helpers. One factory is created per UI
    /// build and carries the active <see cref="ThemePalette"/>, so callers read
    /// close to the original CSS: <c>f.PrimaryButton(parent, "Begin training", ...)</c>.
    ///
    /// Rounded plates are a single sliced Image plus a hairline uGUI Outline effect
    /// (uGUI has no native border), giving the MRTK-style bordered plates.
    /// </summary>
    public class UIFactory
    {
        public readonly ThemePalette T;
        static Font _font;

        public UIFactory(ThemePalette theme) { T = theme; }

        public static Font UIFont
        {
            get
            {
                if (_font == null)
                {
                    _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                    if (_font == null) _font = Resources.GetBuiltinResource<Font>("Arial.ttf");
                }
                return _font;
            }
        }

        // ---------------------------------------------------------------- rects

        public RectTransform Rect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            return rt;
        }

        /// <summary>Stretch a rect to fill its parent, inset by <paramref name="pad"/> px on every side.</summary>
        public static void Stretch(RectTransform rt, float pad = 0f)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(pad, pad);
            rt.offsetMax = new Vector2(-pad, -pad);
        }

        public VerticalLayoutGroup VLayout(RectTransform rt, RectOffset padding, float spacing,
            TextAnchor align = TextAnchor.UpperLeft, bool controlWidth = true, bool controlHeight = true,
            bool expandWidth = true)
        {
            var v = rt.gameObject.AddComponent<VerticalLayoutGroup>();
            v.padding = padding;
            v.spacing = spacing;
            v.childAlignment = align;
            v.childControlWidth = controlWidth;
            v.childControlHeight = controlHeight;
            v.childForceExpandWidth = expandWidth;
            v.childForceExpandHeight = false;
            return v;
        }

        public HorizontalLayoutGroup HLayout(RectTransform rt, float spacing,
            TextAnchor align = TextAnchor.MiddleLeft, bool controlWidth = true, bool controlHeight = true,
            bool expandWidth = false, RectOffset padding = null)
        {
            var h = rt.gameObject.AddComponent<HorizontalLayoutGroup>();
            h.padding = padding ?? new RectOffset(0, 0, 0, 0);
            h.spacing = spacing;
            h.childAlignment = align;
            h.childControlWidth = controlWidth;
            h.childControlHeight = controlHeight;
            h.childForceExpandWidth = expandWidth;
            h.childForceExpandHeight = false;
            return h;
        }

        public ContentSizeFitter FitVertical(RectTransform rt)
        {
            var f = rt.gameObject.AddComponent<ContentSizeFitter>();
            f.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            f.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            return f;
        }

        public LayoutElement Sized(RectTransform rt, float? w = null, float? h = null, float? flexW = null)
        {
            var le = rt.GetComponent<LayoutElement>();
            if (le == null) le = rt.gameObject.AddComponent<LayoutElement>();
            if (w.HasValue) le.preferredWidth = w.Value;
            if (h.HasValue) le.preferredHeight = h.Value;
            if (flexW.HasValue) le.flexibleWidth = flexW.Value;
            return le;
        }

        // -------------------------------------------------------------- graphics

        public Image Img(Transform parent, Color color, int radius = 0, bool raycast = false, string name = "Image")
        {
            var rt = Rect(name, parent);
            var img = rt.gameObject.AddComponent<Image>();
            if (radius > 0)
            {
                img.sprite = RoundedSprite.Get(radius);
                img.type = Image.Type.Sliced;
            }
            img.color = color;
            img.raycastTarget = raycast;
            return img;
        }

        public Text Label(Transform parent, string text, int size, Color color,
            FontStyle style = FontStyle.Normal, TextAnchor align = TextAnchor.UpperLeft,
            bool wrap = true, float letterSpacingPx = 0f)
        {
            var rt = Rect("Label", parent);
            var t = rt.gameObject.AddComponent<Text>();
            t.font = UIFont;
            // uGUI's legacy Text has no letter-spacing; the design's 0.14–0.18em
            // tracking on eyebrow labels is dropped here (swap to TextMeshPro to restore).
            t.text = text;
            t.fontSize = size;
            t.color = color;
            t.fontStyle = style;
            t.alignment = align;
            t.horizontalOverflow = wrap ? HorizontalWrapMode.Wrap : HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Truncate;
            t.supportRichText = true;
            t.raycastTarget = false;
            return t;
        }

        // --------------------------------------------------------------- plates

        /// <summary>Adds a 1px hairline border to a graphic via uGUI's Outline effect.</summary>
        public static void AddBorder(GameObject go, Color color)
        {
            if (color.a <= 0.001f) return;
            var o = go.AddComponent<Outline>();
            o.effectColor = color;
            o.effectDistance = new Vector2(1f, 1f);
            o.useGraphicAlpha = false;
        }

        /// <summary>
        /// A themed rounded plate: one Image with a hairline Outline border. Returns
        /// the plate rect — add your own layout group + padding and parent children
        /// to it. The plate catches raycasts so world-space rays land cleanly.
        /// </summary>
        public RectTransform Plate(Transform parent, int radius = 18, Color? fill = null, Color? border = null,
            string name = "Plate")
        {
            var rt = Rect(name, parent);
            var img = rt.gameObject.AddComponent<Image>();
            img.sprite = RoundedSprite.Get(radius);
            img.type = Image.Type.Sliced;
            img.color = fill ?? T.Panel();
            img.raycastTarget = true;
            AddBorder(rt.gameObject, border ?? T.PanelBorder());
            return rt;
        }

        // -------------------------------------------------------------- buttons

        public class ButtonSpec
        {
            public string text;
            public Color fill, fillHover, textColor;
            public Color? border;
            public int radius = 10;
            public int fontSize = 14;
            public FontStyle fontStyle = FontStyle.Bold;
            public TextAnchor align = TextAnchor.MiddleCenter;
            public float height = 44;
            public float padH = 16;
            public bool interactable = true;
            public Action onClick;
        }

        public Button MakeButton(Transform parent, ButtonSpec s)
        {
            var root = Rect("Button", parent);

            // Fill graphic is the button's targetGraphic — ColorTint drives hover/press.
            var fillImg = root.gameObject.AddComponent<Image>();
            fillImg.sprite = RoundedSprite.Get(s.radius);
            fillImg.type = Image.Type.Sliced;
            fillImg.color = Color.white;
            fillImg.raycastTarget = true;
            if (s.border.HasValue) AddBorder(root.gameObject, s.border.Value);

            var label = Label(root, s.text, s.fontSize, s.textColor, s.fontStyle, s.align, wrap: false);
            var lrt = label.rectTransform;
            lrt.anchorMin = Vector2.zero;
            lrt.anchorMax = Vector2.one;
            lrt.offsetMin = new Vector2(s.padH, 0);
            lrt.offsetMax = new Vector2(-s.padH, 0);

            // Give the button a natural width (label + horizontal padding) so it
            // sizes correctly inside horizontal layout groups; callers add
            // flexibleWidth to make a button stretch instead.
            float natural = Mathf.Ceil(label.preferredWidth) + s.padH * 2f;
            Sized(root, w: natural, h: s.height);

            var btn = root.gameObject.AddComponent<Button>();
            btn.targetGraphic = fillImg;
            btn.transition = Selectable.Transition.ColorTint;
            var cb = btn.colors;
            cb.normalColor = s.fill;
            cb.highlightedColor = s.fillHover;
            cb.pressedColor = s.fillHover;
            cb.selectedColor = s.fill;
            cb.disabledColor = new Color(s.fill.r, s.fill.g, s.fill.b, s.fill.a * 0.5f);
            cb.colorMultiplier = 1f;
            cb.fadeDuration = 0.08f;
            btn.colors = cb;
            btn.interactable = s.interactable;
            if (s.onClick != null) btn.onClick.AddListener(() => s.onClick());
            return btn;
        }

        public Button PrimaryButton(Transform parent, string text, Action onClick,
            float height = 44, int fontSize = 14, float flexW = 0f)
        {
            var b = MakeButton(parent, new ButtonSpec
            {
                text = text, fill = T.accent, fillHover = T.accentHover, textColor = T.onAccent,
                height = height, fontSize = fontSize, onClick = onClick
            });
            if (flexW > 0f) b.GetComponent<LayoutElement>().flexibleWidth = flexW;
            return b;
        }

        public Button SecondaryButton(Transform parent, string text, Action onClick,
            float height = 44, int fontSize = 14)
        {
            return MakeButton(parent, new ButtonSpec
            {
                text = text, fill = T.SecondaryBg(), fillHover = T.Overlay(0.12f), textColor = T.secondaryText,
                border = T.SecondaryBorder(), fontStyle = FontStyle.Bold, height = height, fontSize = fontSize,
                onClick = onClick
            });
        }

        public Button GhostButton(Transform parent, string text, Action onClick,
            float height = 44, int fontSize = 14, Color? textColor = null)
        {
            return MakeButton(parent, new ButtonSpec
            {
                text = text, fill = new Color(0, 0, 0, 0), fillHover = T.AccentA(0.10f),
                textColor = textColor ?? T.accent, fontStyle = FontStyle.Bold, height = height, fontSize = fontSize,
                onClick = onClick
            });
        }

        public Button IconButton(Transform parent, string glyph, Action onClick, float size = 48)
        {
            var b = MakeButton(parent, new ButtonSpec
            {
                text = glyph, fill = T.SecondaryBg(), fillHover = T.Overlay(0.12f), textColor = T.secondaryText,
                border = T.SecondaryBorder(), radius = 12, fontSize = 18, height = size, padH = 0, onClick = onClick
            });
            Sized(b.GetComponent<RectTransform>(), w: size, h: size);
            b.GetComponent<LayoutElement>().flexibleWidth = 0;
            return b;
        }

        // --------------------------------------------------------------- pieces

        /// <summary>Thin rounded track with an accent fill at <paramref name="fraction"/> (0..1).</summary>
        public RectTransform ProgressBar(Transform parent, float fraction, float height = 6f)
        {
            var track = Rect("Progress", parent);
            var trackImg = track.gameObject.AddComponent<Image>();
            trackImg.sprite = RoundedSprite.Get(Mathf.RoundToInt(height / 2f));
            trackImg.type = Image.Type.Sliced;
            trackImg.color = T.Overlay(0.10f);
            trackImg.raycastTarget = false;
            Sized(track, h: height);

            var fill = Rect("Fill", track);
            fill.anchorMin = new Vector2(0, 0);
            fill.anchorMax = new Vector2(Mathf.Clamp01(fraction), 1);
            fill.offsetMin = Vector2.zero;
            fill.offsetMax = Vector2.zero;
            var fillImg = fill.gameObject.AddComponent<Image>();
            fillImg.sprite = RoundedSprite.Get(Mathf.RoundToInt(height / 2f));
            fillImg.type = Image.Type.Sliced;
            fillImg.color = T.accent;
            fillImg.raycastTarget = false;
            return track;
        }

        /// <summary>A small circular status dot with an optional glyph (✓ or step number).</summary>
        public RectTransform Dot(Transform parent, string glyph, Color bg, Color fg, float size = 20)
        {
            var dot = Rect("Dot", parent);
            var img = dot.gameObject.AddComponent<Image>();
            img.sprite = RoundedSprite.Get(Mathf.RoundToInt(size / 2f));
            img.type = Image.Type.Sliced;
            img.color = bg;
            img.raycastTarget = false;
            Sized(dot, w: size, h: size);
            if (!string.IsNullOrEmpty(glyph))
            {
                var l = Label(dot, glyph, Mathf.RoundToInt(size * 0.55f), fg, FontStyle.Bold, TextAnchor.MiddleCenter, wrap: false);
                Stretch(l.rectTransform);
            }
            return dot;
        }
    }
}
