using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

/// The browser HUD's stylesheet, in UGUI terms (via the Godot port's ui.gd). belt-runner-3d.html styles its HUD with
/// CSS: chamfered glass panes with cyan corner brackets, segmented gauges that glow, Chakra Petch for display type, IBM
/// Plex Sans for body text and IBM Plex Mono for numbers, amber primary buttons cut at two corners, key chips.
/// Everything here is a static helper that builds one of those pieces from code, or a small custom Graphic that draws
/// one, so Hud.cs and Menu.cs read like the markup. The fonts are the same three families the browser pulls from Google
/// Fonts, bundled under Resources/Fonts (Open Font Licence).
public static class Ui
{
    public static readonly Color BG = Data.Hex("#070912");
    public static readonly Color PANEL = Data.Hex("#0E1224");
    public static readonly Color PANEL2 = Data.Hex("#151A30");
    public static readonly Color LINE = Data.Hex("#262D4C");
    public static readonly Color LINE2 = Data.Hex("#343C62");
    public static readonly Color TEXT = Data.Hex("#E8ECF8");
    public static readonly Color MUTED = Data.Hex("#8E96B4");
    public static readonly Color DIM = Data.Hex("#5B6384");
    public static readonly Color AMBER = Data.Hex("#F2A33A");
    public static readonly Color AMBER2 = Data.Hex("#FFC466");
    public static readonly Color AMBER_DIM = Data.Hex("#7A5320");
    public static readonly Color CYAN = Data.Hex("#5ED3F0");
    public static readonly Color RED = Data.Hex("#F26B5E");
    public static readonly Color GREEN = Data.Hex("#6BD69A");
    public static readonly Color CARGO = Data.Hex("#B8C0DA");
    public static readonly Color INK = Data.Hex("#1A1004");
    public static readonly Color GLOW_TEXT = Data.Hex("#DFF7FF");
    public static readonly Color HUD_DIM = new Color(0.369f, 0.827f, 0.941f, 0.55f);
    public static readonly Color HUD_FAINT = new Color(0.369f, 0.827f, 0.941f, 0.22f);
    public static readonly Color HUD_GLOW = new Color(0.369f, 0.827f, 0.941f, 0.45f);
    public static readonly Color AMBER_GLOW = new Color(0.949f, 0.639f, 0.227f, 0.55f);
    public static readonly Color GLASS_TOP = new Color(0.031f, 0.047f, 0.102f, 0.74f);
    public static readonly Color GLASS_BOT = new Color(0.031f, 0.047f, 0.102f, 0.5f);
    public static readonly Color CARD_BG = new Color(0.055f, 0.071f, 0.141f, 0.94f);
    public static readonly Color SIDE_BG = new Color(0.055f, 0.071f, 0.141f, 0.84f);
    public static readonly Color OVERLAY = new Color(0.016f, 0.024f, 0.055f, 0.62f);
    public static readonly Color HOVER = Data.Hex("#1C2340");
    public static readonly Color CLEAR = new Color(0f, 0f, 0f, 0f);

    public static Color A(Color c, float a) { c.a = a; return c; }
    public static string Hex(Color c) { return "#" + ColorUtility.ToHtmlStringRGB(c); }
    public static string Col(string text, Color c) { return "<color=" + Hex(c) + ">" + text + "</color>"; }

    // ---- fonts: display / display_bold / display_med / body / mono / mono_med / mono_semi
    static readonly Dictionary<string, string> FONT_FILES = new Dictionary<string, string>
    {
        { "display", "Fonts/ChakraPetch-SemiBold" }, { "display_bold", "Fonts/ChakraPetch-Bold" }, { "display_med", "Fonts/ChakraPetch-Medium" },
        { "body", "Fonts/IBMPlexSans-Variable" }, { "body_semi", "Fonts/IBMPlexSans-Variable" },
        { "mono", "Fonts/IBMPlexMono-Regular" }, { "mono_med", "Fonts/IBMPlexMono-Medium" }, { "mono_semi", "Fonts/IBMPlexMono-SemiBold" },
    };
    static readonly Dictionary<string, Font> _fonts = new Dictionary<string, Font>();
    static Font _fallback;
    public static int fontsMissing;

    public static Font FontFor(string kind)
    {
        Font f;
        if (_fonts.TryGetValue(kind, out f)) return f;
        string path;
        if (FONT_FILES.TryGetValue(kind, out path)) f = Resources.Load<Font>(path);
        if (f == null)
        {
            fontsMissing++;
            if (_fallback == null) _fallback = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            f = _fallback;
        }
        _fonts[kind] = f;
        return f;
    }

    // ---- rects and text
    public static RectTransform Rect(string name, RectTransform parent, Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 size)
    {
        var go = new GameObject(name, typeof(RectTransform));
        var rt = go.GetComponent<RectTransform>();
        rt.SetParent(parent, false);
        rt.anchorMin = rt.anchorMax = anchor;
        rt.pivot = pivot;
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;
        return rt;
    }

    /// A rect that fills its parent (with optional insets: left, bottom, right, top).
    public static RectTransform Stretch(string name, RectTransform parent, float l = 0f, float b = 0f, float r = 0f, float t = 0f)
    {
        var go = new GameObject(name, typeof(RectTransform));
        var rt = go.GetComponent<RectTransform>();
        rt.SetParent(parent, false);
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.offsetMin = new Vector2(l, b);
        rt.offsetMax = new Vector2(-r, -t);
        return rt;
    }

    public static void At(RectTransform rt, Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 size)
    {
        rt.anchorMin = rt.anchorMax = anchor;
        rt.pivot = pivot;
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;
    }

    public static readonly Vector2 TL = new Vector2(0f, 1f);
    public static readonly Vector2 TR = new Vector2(1f, 1f);
    public static readonly Vector2 BL = new Vector2(0f, 0f);
    public static readonly Vector2 BR = new Vector2(1f, 0f);
    public static readonly Vector2 TC = new Vector2(0.5f, 1f);
    public static readonly Vector2 BC = new Vector2(0.5f, 0f);
    public static readonly Vector2 MID = new Vector2(0.5f, 0.5f);

    /// A text label: font by role, size, colour; anchored top-left with no size until the caller places it.
    public static Text Label(RectTransform parent, string text, string kind, int size, Color color, TextAnchor align = TextAnchor.UpperLeft, bool wrap = false)
    {
        var rt = Rect("Text", parent, TL, TL, Vector2.zero, Vector2.zero);
        var t = rt.gameObject.AddComponent<Text>();
        t.font = FontFor(kind);
        t.fontSize = size;
        t.fontStyle = kind == "body_semi" ? FontStyle.Bold : FontStyle.Normal;
        t.color = color;
        t.alignment = align;
        t.text = text;
        t.supportRichText = true;
        t.horizontalOverflow = wrap ? HorizontalWrapMode.Wrap : HorizontalWrapMode.Overflow;
        t.verticalOverflow = VerticalWrapMode.Overflow;
        t.raycastTarget = false;
        return t;
    }

    /// A label sized to its text and placed: anchor and pivot the same corner, at pos.
    public static Text Fixed(RectTransform parent, string text, string kind, int size, Color color, Vector2 anchor, Vector2 pos, TextAnchor align = TextAnchor.UpperLeft)
    {
        var t = Label(parent, text, kind, size, color, align);
        At(t.rectTransform, anchor, anchor, pos, new Vector2(Mathf.Ceil(t.preferredWidth) + 2f, Mathf.Ceil(t.preferredHeight)));
        return t;
    }

    /// .eyebrow: display 11px, uppercase, muted (hud-dim on the flight HUD, muted on cards).
    public static Text Eyebrow(RectTransform parent, string text, Color color, int size = 11)
    {
        return Label(parent, text.ToUpperInvariant(), "display", size, color);
    }

    /// The CSS text-shadow glow: a soft outline in the glow colour.
    public static Text Glow(Text t, Color c, float size = 1f)
    {
        var o = t.gameObject.AddComponent<Outline>();
        o.effectColor = c;
        o.effectDistance = new Vector2(size, -size);
        o.useGraphicAlpha = true;
        return t;
    }

    public static Image Fill(RectTransform rt, Color c, bool raycast = false)
    {
        var img = rt.gameObject.AddComponent<Image>();
        img.color = c;
        img.raycastTarget = raycast;
        return img;
    }

    public static Image HRule(RectTransform parent, float x, float y, float w, Color? c = null)
    {
        var rt = Rect("Rule", parent, TL, TL, new Vector2(x, y), new Vector2(w, 1f));
        return Fill(rt, c ?? LINE);
    }

    /// The width of a single-line text at a font and size, for laying rows out.
    public static float Measure(Text t) { return Mathf.Ceil(t.preferredWidth); }

    // ---- a top-down flow for the panels: a cursor that hangs paragraphs, rules and boxes under each other
    public class Flow
    {
        public RectTransform parent;
        public float x, y, w;
        public Flow(RectTransform parent, float left, float top, float width) { this.parent = parent; x = left; y = -top; w = width; }
        public float Used { get { return -y; } }

        public Text Para(string text, string kind, int size, Color color, float gap = 0f, TextAnchor align = TextAnchor.UpperLeft, float indent = 0f, float width = -1f)
        {
            float ww = width > 0f ? width : w - indent;
            var t = Label(parent, text, kind, size, color, align, true);
            At(t.rectTransform, TL, TL, new Vector2(x + indent, y), new Vector2(ww, 0f));
            float h = Mathf.Ceil(t.preferredHeight);
            t.rectTransform.sizeDelta = new Vector2(ww, h);
            y -= h + gap;
            return t;
        }

        public void Gap(float g) { y -= g; }
        public void Rule(Color? c = null) { HRule(parent, x, y, w, c); y -= 1f; }

        /// An empty box of the given height at the cursor, for rows laid out by hand.
        public RectTransform Box(float h, string name = "Row")
        {
            var rt = Rect(name, parent, TL, TL, new Vector2(x, y), new Vector2(w, h));
            y -= h;
            return rt;
        }
    }

    // ---- .pane / .panel / .card: the chamfered surfaces
    /// The glass pane of the flight HUD: a gradient glass cut at the top-right and bottom-left corners, a faint cyan
    /// border and two bright corner brackets (top-left, bottom-right). `card` swaps to the solid panel of the overlays:
    /// the panel colour, a line2 border, and the cut at the other two corners, without brackets.
    public class Pane : MaskableGraphic
    {
        public float cut = 14f;
        public bool card;
        public bool brackets = true;
        public Color top = GLASS_TOP, bot = GLASS_BOT, border = HUD_FAINT, bracket = CYAN;
        public float alpha = 1f;

        public void Card()
        {
            card = true;
            top = bot = PANEL;
            border = LINE2;
            brackets = false;
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            var r = GetPixelAdjustedRect();
            var poly = Chamfer(r, cut, card);
            var cols = new Color[poly.Length];
            for (int i = 0; i < poly.Length; i++) cols[i] = A(Color.Lerp(top, bot, Mathf.Clamp01((r.yMax - poly[i].y) / Mathf.Max(1f, r.height))), Mathf.Lerp(top.a, bot.a, Mathf.Clamp01((r.yMax - poly[i].y) / Mathf.Max(1f, r.height))) * alpha);
            Poly(vh, poly, cols);
            Outline(vh, poly, 1f, A(border, border.a * alpha));
            if (brackets)
            {
                float b = 16f;
                var bc = A(bracket, bracket.a * alpha);
                Line(vh, new Vector2(r.xMin + 1f, r.yMax - b), new Vector2(r.xMin + 1f, r.yMax - 1f), 2f, bc);
                Line(vh, new Vector2(r.xMin, r.yMax - 1f), new Vector2(r.xMin + b, r.yMax - 1f), 2f, bc);
                Line(vh, new Vector2(r.xMax - b, r.yMin + 1f), new Vector2(r.xMax, r.yMin + 1f), 2f, bc);
                Line(vh, new Vector2(r.xMax - 1f, r.yMin), new Vector2(r.xMax - 1f, r.yMin + b), 2f, bc);
            }
        }
    }

    /// A polygon with two corners cut: the pane cuts top-right and bottom-left, the card / button cut top-left and bottom-right.
    public static Vector2[] Chamfer(Rect r, float cut, bool card)
    {
        float c = Mathf.Min(cut, r.width * 0.5f, r.height * 0.5f);
        if (card)
            return new[] { new Vector2(r.xMin, r.yMax - c), new Vector2(r.xMin + c, r.yMax), new Vector2(r.xMax, r.yMax), new Vector2(r.xMax, r.yMin + c), new Vector2(r.xMax - c, r.yMin), new Vector2(r.xMin, r.yMin) };
        return new[] { new Vector2(r.xMin, r.yMax), new Vector2(r.xMax - c, r.yMax), new Vector2(r.xMax, r.yMax - c), new Vector2(r.xMax, r.yMin), new Vector2(r.xMin + c, r.yMin), new Vector2(r.xMin, r.yMin + c) };
    }

    // ---- mesh helpers for the custom graphics
    public static void Vert(VertexHelper vh, Vector2 p, Color c)
    {
        var v = UIVertex.simpleVert;
        v.position = new Vector3(p.x, p.y, 0f);
        v.color = c;
        v.uv0 = Vector2.zero;
        vh.AddVert(v);
    }

    /// A convex polygon, fanned from its first vertex.
    public static void Poly(VertexHelper vh, Vector2[] pts, Color[] cols)
    {
        int s = vh.currentVertCount;
        for (int i = 0; i < pts.Length; i++) Vert(vh, pts[i], cols[i]);
        for (int i = 1; i + 1 < pts.Length; i++) vh.AddTriangle(s, s + i, s + i + 1);
    }

    public static void Poly(VertexHelper vh, Vector2[] pts, Color c)
    {
        var cols = new Color[pts.Length];
        for (int i = 0; i < cols.Length; i++) cols[i] = c;
        Poly(vh, pts, cols);
    }

    public static void Quad(VertexHelper vh, Vector2 a, Vector2 b, Vector2 c, Vector2 d, Color col)
    {
        int s = vh.currentVertCount;
        Vert(vh, a, col); Vert(vh, b, col); Vert(vh, c, col); Vert(vh, d, col);
        vh.AddTriangle(s, s + 1, s + 2);
        vh.AddTriangle(s, s + 2, s + 3);
    }

    public static void Rectangle(VertexHelper vh, Rect r, Color c)
    {
        Quad(vh, new Vector2(r.xMin, r.yMin), new Vector2(r.xMin, r.yMax), new Vector2(r.xMax, r.yMax), new Vector2(r.xMax, r.yMin), c);
    }

    /// A line as a quad of width w.
    public static void Line(VertexHelper vh, Vector2 a, Vector2 b, float w, Color c)
    {
        var d = b - a;
        if (d.sqrMagnitude < 1e-6f) return;
        var n = new Vector2(-d.y, d.x).normalized * (w * 0.5f);
        Quad(vh, a - n, a + n, b + n, b - n, c);
    }

    public static void Outline(VertexHelper vh, Vector2[] pts, float w, Color c)
    {
        for (int i = 0; i < pts.Length; i++) Line(vh, pts[i], pts[(i + 1) % pts.Length], w, c);
    }

    /// A rectangle outline of width w, drawn inside the rect.
    public static void Frame(VertexHelper vh, Rect r, float w, Color c)
    {
        Rectangle(vh, new Rect(r.xMin, r.yMax - w, r.width, w), c);
        Rectangle(vh, new Rect(r.xMin, r.yMin, r.width, w), c);
        Rectangle(vh, new Rect(r.xMin, r.yMin + w, w, r.height - 2f * w), c);
        Rectangle(vh, new Rect(r.xMax - w, r.yMin + w, w, r.height - 2f * w), c);
    }

    public static Vector2 Rot(Vector2 v, float ang)
    {
        float c = Mathf.Cos(ang), s = Mathf.Sin(ang);
        return new Vector2(v.x * c - v.y * s, v.x * s + v.y * c);
    }

    /// A rotated square outline (the HUD's diamonds) centred on p.
    public static void Diamond(VertexHelper vh, Vector2 p, float half, float w, Color c)
    {
        var pts = new Vector2[4];
        pts[0] = p + Rot(new Vector2(-half, -half), Mathf.PI * 0.25f);
        pts[1] = p + Rot(new Vector2(half, -half), Mathf.PI * 0.25f);
        pts[2] = p + Rot(new Vector2(half, half), Mathf.PI * 0.25f);
        pts[3] = p + Rot(new Vector2(-half, half), Mathf.PI * 0.25f);
        Outline(vh, pts, w, c);
    }

    /// An arrow head pointing along ang (radians, 0 = right), centred on p.
    public static void Arrow(VertexHelper vh, Vector2 p, float size, float ang, Color c)
    {
        float a = ang - Mathf.PI * 0.5f;
        Poly(vh, new[] { p + Rot(new Vector2(0f, size), a), p + Rot(new Vector2(size, -size * 0.85f), a), p + Rot(new Vector2(-size, -size * 0.85f), a) }, c);
    }

    // ---- .box: a filled rect with a border of per-side widths (toasts, chips, slots, the tutorial card, setting rows)
    public class Box : MaskableGraphic
    {
        public Color bg = PANEL2, border = LINE2;
        public float bl = 1f, bt = 1f, br = 1f, bb = 1f;
        public Color? leftEdge;    // a coloured left edge (toasts, the tutorial card) or top edge (slots)
        public Color? topEdge;
        public float edgeW = 3f;
        public float alpha = 1f;

        public void Set(Color bgc, Color bc, float w = 1f) { bg = bgc; border = bc; bl = bt = br = bb = w; SetVerticesDirty(); }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            var r = GetPixelAdjustedRect();
            if (bg.a > 0f) Rectangle(vh, r, A(bg, bg.a * alpha));
            var bc = A(border, border.a * alpha);
            if (bt > 0f) Rectangle(vh, new Rect(r.xMin, r.yMax - bt, r.width, bt), bc);
            if (bb > 0f) Rectangle(vh, new Rect(r.xMin, r.yMin, r.width, bb), bc);
            if (bl > 0f) Rectangle(vh, new Rect(r.xMin, r.yMin, bl, r.height), bc);
            if (br > 0f) Rectangle(vh, new Rect(r.xMax - br, r.yMin, br, r.height), bc);
            if (leftEdge.HasValue) Rectangle(vh, new Rect(r.xMin, r.yMin, edgeW, r.height), A(leftEdge.Value, leftEdge.Value.a * alpha));
            if (topEdge.HasValue) Rectangle(vh, new Rect(r.xMin, r.yMax - edgeW, r.width, edgeW), A(topEdge.Value, topEdge.Value.a * alpha));
        }
    }

    public static Box MakeBox(RectTransform rt, Color bg, Color border, float w = 1f, bool raycast = false)
    {
        var b = rt.gameObject.AddComponent<Box>();
        b.bg = bg; b.border = border; b.bl = b.bt = b.br = b.bb = w;
        b.raycastTarget = raycast;
        return b;
    }

    // ---- .bar: the segmented gauge bar (6 px on, 2 px off) with a glow under the filled part
    public class SegBar : MaskableGraphic
    {
        public float value;
        public Color fill = CYAN;
        public Color track = new Color(0.369f, 0.827f, 0.941f, 0.08f);
        public bool segmented = true;
        public bool blink;
        public float alpha = 1f;
        float _t;

        public void Set(float v, Color c)
        {
            if (Mathf.Abs(v - value) > 0.001f || c != fill) { value = v; fill = c; SetVerticesDirty(); }
        }

        void Update()
        {
            _t += Time.deltaTime;
            float a = blink ? ((_t % 1f) > 0.5f ? 0.35f : 1f) : 1f;
            if (a != alpha) { alpha = a; SetVerticesDirty(); }
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            var r = GetPixelAdjustedRect();
            float w = r.width, h = r.height;
            float fw = Mathf.Clamp01(value) * w;
            var tc = A(track, track.a * alpha);
            var fc = A(fill, fill.a * alpha);
            if (segmented)
            {
                for (float x = 0f; x < w; x += 8f) Rectangle(vh, new Rect(r.xMin + x, r.yMin, Mathf.Min(6f, w - x), h), tc);
                if (fw > 0.5f)
                {
                    Rectangle(vh, new Rect(r.xMin - 2f, r.yMin - 2f, fw + 4f, h + 4f), A(fill, 0.16f * alpha));
                    for (float x = 0f; x < fw; x += 8f) Rectangle(vh, new Rect(r.xMin + x, r.yMin, Mathf.Min(6f, fw - x), h), fc);
                }
            }
            else
            {
                Rectangle(vh, r, tc);
                if (fw > 0.5f) Rectangle(vh, new Rect(r.xMin, r.yMin, fw, h), fc);
            }
        }
    }

    // ---- .gauge: a head row (eyebrow name, mono reading) over a segmented bar
    public class Gauge
    {
        public RectTransform rt;
        public Text name, value;
        public SegBar bar;
        public float height;

        public static Gauge Make(RectTransform parent, string title, Color color, float x, float y, float w, bool big = false, float barH = 8f)
        {
            var g = new Gauge();
            float headH = big ? 20f : 16f;
            float gap = big ? 7f : 5f;
            g.height = headH + gap + barH;
            g.rt = Rect("Gauge " + title, parent, TL, TL, new Vector2(x, y), new Vector2(w, g.height));
            g.name = Eyebrow(g.rt, title, HUD_DIM, big ? 14 : 11);
            At(g.name.rectTransform, TL, TL, Vector2.zero, new Vector2(w * 0.6f, headH));
            g.name.alignment = TextAnchor.LowerLeft;
            g.value = Glow(Label(g.rt, "", "mono", big ? 15 : 12, GLOW_TEXT, TextAnchor.LowerRight), A(HUD_GLOW, 0.35f));
            At(g.value.rectTransform, TR, TR, Vector2.zero, new Vector2(w * 0.7f, headH));
            var brt = Rect("Bar", g.rt, BL, BL, Vector2.zero, new Vector2(w, barH));
            g.bar = brt.gameObject.AddComponent<SegBar>();
            g.bar.fill = color;
            g.bar.raycastTarget = false;
            return g;
        }

        public void Show(float v, string text, Color color, bool blink = false)
        {
            if (value.text != text) value.text = text;
            bar.Set(v, color);
            bar.blink = blink;
        }
    }

    // ---- .btn: display type, uppercase, cut at the top-left and bottom-right corners; `primary` is the amber one
    public class Face : MaskableGraphic, IPointerEnterHandler, IPointerExitHandler
    {
        public Btn btn;
        public bool hover;
        public float cut = 6f;

        public void OnPointerEnter(PointerEventData e) { hover = true; SetVerticesDirty(); }
        public void OnPointerExit(PointerEventData e) { hover = false; SetVerticesDirty(); }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            if (btn == null) return;
            var r = GetPixelAdjustedRect();
            bool on = btn.interactable;
            Color bg, border;
            if (btn.primary) { bg = hover && on ? AMBER2 : AMBER; border = bg; }
            else { bg = hover && on ? HOVER : PANEL2; border = LINE2; }
            if (!on) { bg.a *= 0.42f; border.a *= 0.42f; }
            var poly = Chamfer(r, cut, true);
            Poly(vh, poly, bg);
            Ui.Outline(vh, poly, 1f, border);
        }
    }

    public class Btn
    {
        public RectTransform rt;
        public Button button;
        public Text label;
        public Face face;
        public bool primary, mono;
        bool _on = true;
        public bool interactable
        {
            get { return _on; }
            set { _on = value; button.interactable = value; face.SetVerticesDirty(); Recolor(); }
        }
        public void SetPrimary(bool p) { primary = p; face.SetVerticesDirty(); Recolor(); }
        public void SetText(string s) { label.text = mono ? s : s.ToUpperInvariant(); }
        public void Recolor()
        {
            var c = primary ? INK : TEXT;
            if (!_on) c.a *= 0.42f;
            label.color = c;
        }
        public float Width { get { return rt.sizeDelta.x; } }
        public float Height { get { return rt.sizeDelta.y; } }
    }

    public static Btn Button(RectTransform parent, string text, Action onClick, bool primary = false, bool mono = false, float minW = 0f, int fontSize = 13, float padX = 16f, float padY = 9f)
    {
        var b = new Btn { primary = primary, mono = mono };
        b.rt = Rect("Btn " + text, parent, TL, TL, Vector2.zero, Vector2.zero);
        b.face = b.rt.gameObject.AddComponent<Face>();
        b.face.btn = b;
        b.face.raycastTarget = true;
        b.button = b.rt.gameObject.AddComponent<Button>();
        b.button.transition = Selectable.Transition.None;
        b.button.onClick.AddListener(() => onClick());
        b.label = Label(b.rt, mono ? text : text.ToUpperInvariant(), mono ? "mono_med" : "display", fontSize, TEXT, TextAnchor.MiddleCenter);
        float w = Mathf.Max(minW, Mathf.Ceil(b.label.preferredWidth) + 2f * padX + 4f);
        float h = Mathf.Ceil(fontSize * 1.3f) + 2f * padY;
        b.rt.sizeDelta = new Vector2(w, h);
        At(b.label.rectTransform, MID, MID, Vector2.zero, new Vector2(w, h));
        b.Recolor();
        return b;
    }

    // ---- .link: dim text that turns red under the mouse
    public class Hover : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        public Text text;
        public Color normal = DIM, over = RED;
        public void OnPointerEnter(PointerEventData e) { text.color = over; }
        public void OnPointerExit(PointerEventData e) { text.color = normal; }
    }

    public static Text Link(RectTransform parent, string text, Action onClick, int size = 12, Color? color = null)
    {
        var t = Label(parent, text, "body", size, color ?? DIM);
        t.raycastTarget = true;
        t.rectTransform.sizeDelta = new Vector2(Mathf.Ceil(t.preferredWidth) + 8f, Mathf.Ceil(t.preferredHeight) + 6f);
        t.alignment = TextAnchor.MiddleCenter;
        var b = t.gameObject.AddComponent<Button>();
        b.transition = Selectable.Transition.None;
        b.onClick.AddListener(() => onClick());
        var h = t.gameObject.AddComponent<Hover>();
        h.text = t;
        h.normal = color ?? DIM;
        return t;
    }

    // ---- <kbd>: a key chip. HUD chips are cyan glass; card chips are the darker panel2 with a heavier bottom edge
    public static RectTransform Chip(RectTransform parent, string key, bool hud, Vector2 pos, Vector2? anchor = null)
    {
        var rt = Rect("Key " + key, parent, anchor ?? TL, anchor ?? TL, pos, Vector2.zero);
        var box = MakeBox(rt, hud ? A(CYAN, 0.08f) : PANEL2, hud ? HUD_FAINT : LINE2, 1f);
        if (!hud) box.bb = 2f;
        var t = Label(rt, key, "mono", hud ? 10 : 11, hud ? GLOW_TEXT : TEXT, TextAnchor.MiddleCenter);
        float w = Mathf.Max(hud ? 16f : 20f, Mathf.Ceil(t.preferredWidth) + (hud ? 10f : 12f));
        float h = hud ? 16f : 19f;
        rt.sizeDelta = new Vector2(w, h);
        At(t.rectTransform, MID, MID, Vector2.zero, new Vector2(w, h));
        return rt;
    }

    /// A row of chips from (x, y) top-left; returns the row's width.
    public static float Keys(RectTransform parent, string[] keys, bool hud, float x, float y)
    {
        float cx = x;
        foreach (var k in keys)
        {
            var c = Chip(parent, k, hud, new Vector2(cx, y));
            cx += c.sizeDelta.x + 4f;
        }
        return cx - x - 4f;
    }

    // ---- .reticle: four corner brackets round whatever the nose is on; amber while the laser cuts; heavier when locked
    public class Reticle : MaskableGraphic
    {
        public bool hot, locked;
        public void Set(bool h, bool l) { if (h != hot || l != locked) { hot = h; locked = l; SetVerticesDirty(); } }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            var r = GetPixelAdjustedRect();
            var c = hot ? AMBER : CYAN;
            float w = locked ? 3f : 2f;
            float l = locked ? 18f : 14f;
            for (int pass = 0; pass < 2; pass++)
            {
                var col = pass == 0 ? A(c, 0.35f) : c;
                float wd = pass == 0 ? w + 3f : w;
                Line(vh, new Vector2(r.xMin, r.yMax - l), new Vector2(r.xMin, r.yMax), wd, col); Line(vh, new Vector2(r.xMin, r.yMax), new Vector2(r.xMin + l, r.yMax), wd, col);
                Line(vh, new Vector2(r.xMax - l, r.yMax), new Vector2(r.xMax, r.yMax), wd, col); Line(vh, new Vector2(r.xMax, r.yMax), new Vector2(r.xMax, r.yMax - l), wd, col);
                Line(vh, new Vector2(r.xMin, r.yMin + l), new Vector2(r.xMin, r.yMin), wd, col); Line(vh, new Vector2(r.xMin, r.yMin), new Vector2(r.xMin + l, r.yMin), wd, col);
                Line(vh, new Vector2(r.xMax - l, r.yMin), new Vector2(r.xMax, r.yMin), wd, col); Line(vh, new Vector2(r.xMax, r.yMin), new Vector2(r.xMax, r.yMin + l), wd, col);
            }
        }
    }

    // ---- the gunnery crosshair: a small plus at the mouse; the hit marker flashes round it
    public class Crosshair : MaskableGraphic
    {
        public bool hot;
        public float hit;    // the hit marker: an X over the ring, this strong (0 hides it)
        public bool kill;    // red for the hit that killed
        public void Set(bool h) { if (h != hot) { hot = h; SetVerticesDirty(); } }
        public void SetHit(float f, bool k)
        {
            f = Mathf.Clamp01(f);
            if (Mathf.Abs(f - hit) < 0.02f && k == kill && !(f == 0f && hit != 0f)) return;
            hit = f; kill = k; SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            var c = hot ? AMBER : A(CYAN, 0.9f);
            // a small plus: four arms with a gap at the centre
            float r = 8f;
            foreach (var d in new[] { Vector2.up, -Vector2.up, new Vector2(1f, 0f), new Vector2(-1f, 0f) }) Line(vh, d * 2f, d * r, 1.5f, c);
            if (hit > 0f)
            {
                // the hit marker: four diagonal strokes just outside the ring, swelling as they fade
                var hc = A(kill ? RED : Color.white, hit);
                float g = r + 4f + (1f - hit) * 6f, len = 9f;
                foreach (var d in new[] { new Vector2(1f, 1f), new Vector2(-1f, 1f), new Vector2(1f, -1f), new Vector2(-1f, -1f) })
                {
                    var n = d.normalized;
                    Line(vh, n * g, n * (g + len), 2f, hc);
                }
            }
        }
    }

    // ---- .marker: a rotated square (or, off screen, an arrow) over a small letter-spaced label; the HUD moves it each frame
    public class Marker
    {
        public RectTransform rt;
        public MarkerIcon icon;
        public Text label;
        public Color color = AMBER;
        public bool small, round;

        public static Marker Make(RectTransform parent, Color color, bool small = false, bool round = false)
        {
            var m = new Marker { color = color, small = small, round = round };
            m.rt = Rect("Marker", parent, BL, TC, Vector2.zero, new Vector2(300f, 44f));
            var irt = Rect("Icon", m.rt, TC, TC, new Vector2(0f, -8f), new Vector2(24f, 24f));
            m.icon = irt.gameObject.AddComponent<MarkerIcon>();
            m.icon.raycastTarget = false;
            m.icon.color = color;
            m.icon.small = small;
            m.icon.round = round;
            m.label = Label(m.rt, "", "display", small ? 9 : 10, color, TextAnchor.UpperCenter);
            m.label.horizontalOverflow = HorizontalWrapMode.Overflow;
            At(m.label.rectTransform, TC, TC, new Vector2(0f, -18f), new Vector2(300f, 16f));
            Glow(m.label, new Color(0f, 0f, 0f, 0.8f), 1f);
            m.rt.gameObject.SetActive(false);
            return m;
        }

        public void Place(Vector2 pos, bool off, float ang, string text)
        {
            rt.gameObject.SetActive(true);
            rt.anchoredPosition = pos + new Vector2(0f, 8f) + (off ? Vector2.zero : new Vector2(0f, 30f));
            if (icon.off != off || Mathf.Abs(icon.ang - ang) > 0.001f) { icon.off = off; icon.ang = ang; icon.SetVerticesDirty(); }
            if (label.text != text) label.text = text;
        }

        public void Hide() { if (rt.gameObject.activeSelf) rt.gameObject.SetActive(false); }
    }

    public class MarkerIcon : MaskableGraphic
    {
        public bool off, small, round;
        public float ang;

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            var c = color;
            var p = Vector2.zero;
            float half = small ? 4f : 5f;
            if (off) Arrow(vh, p, 6f, ang, c);
            else if (round)
            {
                for (int k = 0; k < 8; k++)
                {
                    float a0 = k * Mathf.PI * 2f / 8f, a1 = (k + 0.6f) * Mathf.PI * 2f / 8f;
                    Line(vh, p + new Vector2(Mathf.Cos(a0), Mathf.Sin(a0)) * 4.5f, p + new Vector2(Mathf.Cos(a1), Mathf.Sin(a1)) * 4.5f, 2f, c);
                }
            }
            else Diamond(vh, p, half, 2f, c);
        }
    }

    // ---- radar blips: a diamond in the ore's colour on every marked rock in view (an arrow at the screen edge for the
    // ones out of view), the nearest few with a name-and-range label on a dark backing
    public struct Blip { public Vector2 pos; public bool off; public float ang; public Color color; public string label; public float alpha; }

    public class Blips : MaskableGraphic
    {
        public readonly List<Blip> items = new List<Blip>();
        readonly List<Text> _labels = new List<Text>();

        public void Apply()
        {
            SetVerticesDirty();
            int li = 0;
            foreach (var it in items)
            {
                if (string.IsNullOrEmpty(it.label)) continue;
                if (li >= _labels.Count)
                {
                    var t = Label(rectTransform, "", "mono_med", 13, GLOW_TEXT, TextAnchor.MiddleCenter);
                    At(t.rectTransform, BL, TC, Vector2.zero, new Vector2(300f, 20f));
                    _labels.Add(t);
                }
                var l = _labels[li++];
                l.gameObject.SetActive(true);
                l.text = it.label;
                l.color = A(GLOW_TEXT, it.alpha);
                l.rectTransform.anchoredPosition = it.pos + new Vector2(0f, -11f);
            }
            for (; li < _labels.Count; li++) _labels[li].gameObject.SetActive(false);
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            foreach (var it in items)
            {
                var c = A(it.color, it.alpha);
                if (it.off) Arrow(vh, it.pos, 7f, it.ang, c);
                else
                {
                    Diamond(vh, it.pos, 6.5f, 4f, A(c, c.a * 0.35f));
                    Diamond(vh, it.pos, 4.5f, 2f, c);
                }
                if (!string.IsNullOrEmpty(it.label))
                {
                    float tw = it.label.Length * 7.8f + 12f;
                    var r = new Rect(it.pos.x - tw * 0.5f, it.pos.y - 31f, tw, 20f);
                    Rectangle(vh, r, new Color(0.024f, 0.039f, 0.094f, 0.6f * it.alpha));
                    Frame(vh, r, 1f, A(CYAN, 0.25f * it.alpha));
                }
            }
        }
    }

    // ---- the afterburner's speed streaks: thin pale lines racing out from the centre of the screen toward the edges,
    // each on its own spoke at its own pace, tapered and faint near the centre, brighter as it nears the edge. The HUD
    // sets the intensity (0 hides it) and the streaks step outward each frame.
    public class SpeedStreaks : MaskableGraphic
    {
        const int N = 64;
        readonly float[] _ang = new float[N], _r = new float[N], _len = new float[N], _spd = new float[N], _w = new float[N];
        float _k;
        bool _seeded;

        void Seed(int i)
        {
            _ang[i] = UnityEngine.Random.Range(0f, Mathf.PI * 2f);
            _r[i] = UnityEngine.Random.Range(0.05f, 0.6f);        // start radius, as a fraction of the half-diagonal
            _len[i] = UnityEngine.Random.Range(0.08f, 0.22f);
            _spd[i] = UnityEngine.Random.Range(0.9f, 1.8f);       // fractions of the half-diagonal a second
            _w[i] = UnityEngine.Random.Range(1f, 2.2f);
        }

        /// `k` is the intensity 0..1; `dt` steps the streaks outward, faster with `speed` (0..1).
        public void Set(float k, float dt, float speed)
        {
            if (!_seeded) { for (int i = 0; i < N; i++) { Seed(i); _r[i] = UnityEngine.Random.Range(0.05f, 1f); } _seeded = true; }
            _k = k;
            if (k <= 0.005f) { SetVerticesDirty(); return; }
            for (int i = 0; i < N; i++)
            {
                _r[i] += _spd[i] * (0.6f + 0.8f * speed) * dt;
                if (_r[i] > 1.05f) { Seed(i); _r[i] = UnityEngine.Random.Range(0.02f, 0.12f); }
            }
            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            if (_k <= 0.005f) return;
            var rect = GetPixelAdjustedRect();
            var c = rect.center;
            float half = new Vector2(rect.width, rect.height).magnitude * 0.5f;
            for (int i = 0; i < N; i++)
            {
                float r0 = _r[i], r1 = Mathf.Min(1.05f, r0 + _len[i]);
                if (r0 < 0.1f) continue;   // the centre stays clear
                var d = new Vector2(Mathf.Cos(_ang[i]), Mathf.Sin(_ang[i]));
                var a = c + d * r0 * half;
                var b = c + d * r1 * half;
                // faint near the centre, brightest two thirds out, gone at the edge
                float mid = (r0 + r1) * 0.5f;
                float bright = Mathf.Clamp01((mid - 0.1f) / 0.45f) * Mathf.Clamp01((1.05f - mid) / 0.3f);
                var col = new Color(0.8f, 0.9f, 1f, 0.55f * _k * bright);
                // tapered: a sliver at the near end, the full width at the far end
                var n = new Vector2(-d.y, d.x);
                float w = _w[i] * (0.6f + 0.6f * mid);
                Quad(vh, a - n * 0.2f, a + n * 0.2f, b + n * w * 0.5f, b - n * w * 0.5f, col);
            }
        }
    }

    // ---- .hudfx and .dmg: a soft vignette, and the red flash on a hull knock
    public class Vignette : MaskableGraphic
    {
        public Color tint = new Color(0.008f, 0.016f, 0.047f);
        public float inner = 0.58f;
        public float strength = 0.5f;

        public void Set(float s) { if (Mathf.Abs(s - strength) > 0.002f) { strength = s; SetVerticesDirty(); } }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            if (strength <= 0.001f) return;
            var r = GetPixelAdjustedRect();
            var c = r.center;
            float ax = r.width * 0.5f * 1.41421f, ay = r.height * 0.5f * 1.41421f;
            const int N = 40;
            float[] rings = { inner, (inner + 1f) * 0.5f, 1f, 1.5f };
            float[] alphas = { 0f, 0.5f, 1f, 1f };
            int start = vh.currentVertCount;
            for (int ri = 0; ri < rings.Length; ri++)
            {
                for (int i = 0; i < N; i++)
                {
                    float a = i * Mathf.PI * 2f / N;
                    Vert(vh, c + new Vector2(Mathf.Cos(a) * ax * rings[ri], Mathf.Sin(a) * ay * rings[ri]), A(tint, alphas[ri] * strength));
                }
            }
            for (int ri = 0; ri + 1 < rings.Length; ri++)
            {
                for (int i = 0; i < N; i++)
                {
                    int a = start + ri * N + i, b = start + ri * N + (i + 1) % N;
                    int c2 = start + (ri + 1) * N + (i + 1) % N, d = start + (ri + 1) * N + i;
                    vh.AddTriangle(a, b, c2);
                    vh.AddTriangle(a, c2, d);
                }
            }
        }
    }

    // ---- .tut-ring: pulsing amber frames round the HUD pieces the tutorial is talking about
    public class Rings : MaskableGraphic
    {
        public readonly List<Rect> rects = new List<Rect>();
        float _t;

        void Update()
        {
            _t += Time.deltaTime;
            if (rects.Count > 0) SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            float pulse = 0.5f + 0.5f * Mathf.Sin(_t * 4.2f);
            foreach (var r0 in rects)
            {
                var rr = Grow(r0, 6f);
                Frame(vh, Grow(rr, 3f), 3f, A(AMBER, 0.18f));
                Frame(vh, Grow(rr, 6f), 8f, A(AMBER, 0.12f * pulse));
                Frame(vh, rr, 2f, A(AMBER, 0.55f + 0.45f * pulse));
            }
        }

        static Rect Grow(Rect r, float g) { return new Rect(r.xMin - g, r.yMin - g, r.width + 2f * g, r.height + 2f * g); }
    }

    // ---- the nav computer's chart: the browser's 112 x 64 SVG (grid, dashed lanes with distances, a node per zone)
    public class Chart : MaskableGraphic, IPointerClickHandler
    {
        public Data.Zone cur, sel;
        public Action<Data.Zone> picked;
        readonly List<Text> _texts = new List<Text>();
        float _u = 1f;
        Vector2 _o;

        void Fit()
        {
            var r = GetPixelAdjustedRect();
            _u = Mathf.Min(r.width / 112f, r.height / 64f);
            _o = new Vector2(r.xMin + (r.width - 112f * _u) * 0.5f, r.yMax - (r.height - 64f * _u) * 0.5f);
        }

        Vector2 P(Vector2 v) { return _o + new Vector2(v.x * _u, -v.y * _u); }

        public void OnPointerClick(PointerEventData e)
        {
            Vector2 local;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(rectTransform, e.position, e.pressEventCamera, out local)) return;
            Fit();
            foreach (var z in Data.ZONES)
            {
                if (Vector2.Distance(P(z.map), local) < 8f * _u)
                {
                    sel = z;
                    if (picked != null) picked(z);
                    Rebuild();
                    return;
                }
            }
        }

        /// Redraw, and lay the labels out again.
        public void Rebuild()
        {
            SetVerticesDirty();
            foreach (var t in _texts) Destroy(t.gameObject);
            _texts.Clear();
            if (cur == null) return;
            Fit();
            foreach (var z in Data.ZONES)
            {
                if (z.id == cur.id) continue;
                var mid = (cur.map + z.map) * 0.5f + new Vector2(1f, -1f);
                var t = Label(rectTransform, Data.ZoneLy(cur, z) + " ly", "mono", Mathf.Max(9, Mathf.RoundToInt(2.2f * _u)), DIM);
                t.rectTransform.sizeDelta = new Vector2(200f, 16f);
                PlaceLocal(t.rectTransform, P(mid), TL);
                _texts.Add(t);
            }
            foreach (var z in Data.ZONES)
            {
                bool isCur = z.id == cur.id;
                bool isSel = sel != null && z.id == sel.id;
                bool right = z.map.x > 85f;
                float tx = right ? z.map.x - 6f : z.map.x + 5f;
                var name = Label(rectTransform, z.name.ToUpperInvariant(), "display", Mathf.Max(10, Mathf.RoundToInt(3.1f * _u)), isSel ? AMBER2 : TEXT, right ? TextAnchor.UpperRight : TextAnchor.UpperLeft);
                name.rectTransform.sizeDelta = new Vector2(60f * _u, 20f);
                PlaceLocal(name.rectTransform, P(new Vector2(tx, z.map.y - 1.5f)), right ? TR : TL);
                _texts.Add(name);
                string sub = isCur ? "current position" : (z.hub ? "colony · sells ore" : Exclusives(z));
                var st = Label(rectTransform, sub, "mono", Mathf.Max(9, Mathf.RoundToInt(2.3f * _u)), isSel ? AMBER : MUTED, right ? TextAnchor.UpperRight : TextAnchor.UpperLeft);
                st.rectTransform.sizeDelta = new Vector2(60f * _u, 16f);
                PlaceLocal(st.rectTransform, P(new Vector2(tx, z.map.y + 2f)), right ? TR : TL);
                _texts.Add(st);
            }
        }

        /// Place a child rect by a local point of this graphic (the child's anchor sits at the graphic's centre).
        void PlaceLocal(RectTransform rt, Vector2 local, Vector2 pivot)
        {
            rt.anchorMin = rt.anchorMax = MID;
            rt.pivot = pivot;
            rt.anchoredPosition = local - rectTransform.rect.center;
        }

        static string Exclusives(Data.Zone z)
        {
            var ex = new List<string>();
            foreach (var o in Data.ORES) if (o.zone == z.id) ex.Add(o.name);
            return string.Join(" · ", ex.ToArray());
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            Fit();
            float u = _u;
            float gw = Mathf.Max(1f, 0.15f * u);
            for (int x = 10; x < 112; x += 10) Line(vh, P(new Vector2(x, 2)), P(new Vector2(x, 62)), gw, LINE);
            for (int y = 10; y < 64; y += 10) Line(vh, P(new Vector2(2, y)), P(new Vector2(110, y)), gw, LINE);
            if (cur == null) return;
            foreach (var z in Data.ZONES)
            {
                if (z.id == cur.id) continue;
                Dashed(vh, P(cur.map), P(z.map), Mathf.Max(1f, 0.28f * u), 1.2f * u, 1f * u, LINE2);
            }
            foreach (var z in Data.ZONES)
            {
                var c = P(z.map);
                var accent = z.accent;
                bool isCur = z.id == cur.id;
                bool isSel = sel != null && z.id == sel.id;
                if (isCur) Circle(vh, c, 3.2f * u, Mathf.Max(1f, 0.3f * u), accent);
                if (isSel) Circle(vh, c, 4.6f * u, Mathf.Max(1f, 0.25f * u), A(AMBER2, 0.9f));
                if (z.hub)
                {
                    Circle(vh, c, 2.1f * u, Mathf.Max(1f, 0.7f * u), accent);
                    Disc(vh, c, 0.7f * u, accent);
                }
                else
                {
                    var half = 1.6f * u;
                    Poly(vh, new[] { c + Rot(new Vector2(-half, -half), Mathf.PI * 0.25f), c + Rot(new Vector2(half, -half), Mathf.PI * 0.25f), c + Rot(new Vector2(half, half), Mathf.PI * 0.25f), c + Rot(new Vector2(-half, half), Mathf.PI * 0.25f) }, accent);
                }
            }
        }

        static void Dashed(VertexHelper vh, Vector2 a, Vector2 b, float w, float dash, float gap, Color c)
        {
            var d = b - a;
            float len = d.magnitude;
            if (len < 1e-3f) return;
            var n = d / len;
            for (float s = 0f; s < len; s += dash + gap) Line(vh, a + n * s, a + n * Mathf.Min(len, s + dash), w, c);
        }

        static void Circle(VertexHelper vh, Vector2 c, float r, float w, Color col)
        {
            const int N = 40;
            for (int i = 0; i < N; i++)
            {
                float a0 = i * Mathf.PI * 2f / N, a1 = (i + 1) * Mathf.PI * 2f / N;
                Line(vh, c + new Vector2(Mathf.Cos(a0), Mathf.Sin(a0)) * r, c + new Vector2(Mathf.Cos(a1), Mathf.Sin(a1)) * r, w, col);
            }
        }

        static void Disc(VertexHelper vh, Vector2 c, float r, Color col)
        {
            const int N = 24;
            var pts = new Vector2[N];
            for (int i = 0; i < N; i++) { float a = i * Mathf.PI * 2f / N; pts[i] = c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r; }
            Poly(vh, pts, col);
        }
    }

    // ---- a vertical scroll area: viewport with a mask, a content rect the caller fills top-down, mouse wheel scrolls
    public class Scroll
    {
        public RectTransform viewport, content;
        public ScrollRect rect;

        public static Scroll Make(RectTransform parent, float l, float b, float r, float t)
        {
            var s = new Scroll();
            s.viewport = Stretch("Viewport", parent, l, b, r, t);
            s.viewport.gameObject.AddComponent<RectMask2D>();
            var img = s.viewport.gameObject.AddComponent<Image>();
            img.color = CLEAR;
            img.raycastTarget = true;
            s.content = Rect("Content", s.viewport, TL, TL, Vector2.zero, new Vector2(0f, 10f));
            s.content.anchorMin = new Vector2(0f, 1f);
            s.content.anchorMax = new Vector2(1f, 1f);
            s.content.offsetMin = new Vector2(0f, -10f);
            s.content.offsetMax = new Vector2(0f, 0f);
            s.rect = s.viewport.gameObject.AddComponent<ScrollRect>();
            s.rect.content = s.content;
            s.rect.viewport = s.viewport;
            s.rect.horizontal = false;
            s.rect.vertical = true;
            s.rect.movementType = ScrollRect.MovementType.Clamped;
            s.rect.scrollSensitivity = 30f;
            s.rect.inertia = false;
            return s;
        }

        public float Width { get { return viewport.rect.width; } }

        public void SetHeight(float h)
        {
            content.offsetMin = new Vector2(0f, -h);
            content.offsetMax = new Vector2(0f, 0f);
        }

        public void Clear()
        {
            for (int i = content.childCount - 1; i >= 0; i--) UnityEngine.Object.Destroy(content.GetChild(i).gameObject);
        }

        public void Top() { content.anchoredPosition = Vector2.zero; }
    }

    // ---- a slider in the sheet's colours: a panel2 track with a line2 border, an amber fill and grabber
    public static Slider MakeSlider(RectTransform parent, float min, float max, bool whole, Vector2 pos, float w, Action<float> onChange)
    {
        var rt = Rect("Slider", parent, TL, TL, pos, new Vector2(w, 16f));
        var track = Stretch("Track", rt, 0f, 5f, 0f, 5f);
        MakeBox(track, PANEL2, LINE2, 1f, true);
        var fillArea = Stretch("Fill Area", rt, 1f, 6f, 1f, 6f);
        var fill = Stretch("Fill", fillArea);
        Fill(fill, AMBER);
        var handleArea = Stretch("Handle Area", rt, 6f, 0f, 6f, 0f);
        var handle = Rect("Handle", handleArea, BL, MID, Vector2.zero, new Vector2(12f, 16f));
        Fill(handle, AMBER2, true);
        var s = rt.gameObject.AddComponent<Slider>();
        s.fillRect = fill;
        s.handleRect = handle;
        s.targetGraphic = handle.GetComponent<Image>();
        s.direction = Slider.Direction.LeftToRight;
        s.minValue = min;
        s.maxValue = max;
        s.wholeNumbers = whole;
        s.transition = Selectable.Transition.None;
        s.onValueChanged.AddListener(v => onChange(v));
        return s;
    }
}
