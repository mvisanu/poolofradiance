using System.Collections.Generic;
using UnityEngine;

namespace RadiantPool.Game
{
    /// <summary>The "Gilded Quest" design system from theme/ (Stitch mockups): charcoal
    /// stone panels with gold borders, parchment content cards, mage-blue primary
    /// buttons, Source Serif 4 headers + Inter body, class accent colors, and glassy
    /// HP/MP bars. Hard outlines instead of soft shadows. All textures are generated
    /// at runtime (9-sliced rounded rects); fonts ship in Resources/Fonts (SIL OFL).</summary>
    public static class Theme
    {
        // ---------- palette (theme/.../gilded_quest/DESIGN.md) ----------
        public static readonly Color Surface        = Hex("#131313");
        public static readonly Color Panel          = Hex("#202020");
        public static readonly Color PanelHigh      = Hex("#2a2a2a");
        public static readonly Color PanelBright    = Hex("#393939");
        public static readonly Color OnSurface      = Hex("#e5e2e1");
        public static readonly Color OnSurfaceMuted = Hex("#d0c5af");
        public static readonly Color Gold           = Hex("#f2ca50");
        public static readonly Color GoldDeep       = Hex("#d4af37");
        public static readonly Color OnGold         = Hex("#3c2f00");
        public static readonly Color Parchment      = Hex("#f5f5f5");
        public static readonly Color ParchmentEdge  = Hex("#cfc9ba");
        public static readonly Color Ink            = Hex("#212121");
        public static readonly Color InkMuted       = Hex("#5c5c5c");
        public static readonly Color MageBlue       = Hex("#3860be");
        public static readonly Color FighterRed     = Hex("#c62828");
        public static readonly Color RangerGreen    = Hex("#2e7d32");
        public static readonly Color ClericGold     = Hex("#ffd700");
        public static readonly Color HpRed          = Hex("#e53935");
        public static readonly Color MpBlue         = Hex("#3d6ff2");
        public static readonly Color Outline        = Hex("#99907c");

        private static Color Hex(string h)
        {
            ColorUtility.TryParseHtmlString(h, out var c);
            return c;
        }

        public static Color ClassColor(RadiantPool.Rules.CharacterClass c) => c switch
        {
            RadiantPool.Rules.CharacterClass.Fighter => FighterRed,
            RadiantPool.Rules.CharacterClass.Cleric => ClericGold,
            RadiantPool.Rules.CharacterClass.Wizard => MageBlue,
            _ => RangerGreen
        };

        // ---------- fonts ----------
        private static Font _serif, _serifSemi, _body, _bodyBold;
        private static bool _fontsLoaded;

        private static void LoadFonts()
        {
            if (_fontsLoaded) return;
            _fontsLoaded = true;
            _serif = Resources.Load<Font>("Fonts/SourceSerif4-Bold");
            _serifSemi = Resources.Load<Font>("Fonts/SourceSerif4-Semibold");
            _body = Resources.Load<Font>("Fonts/Inter-Regular");
            _bodyBold = Resources.Load<Font>("Fonts/Inter-Bold");
        }

        public static Font Serif { get { LoadFonts(); return _serif; } }
        public static Font SerifSemi { get { LoadFonts(); return _serifSemi; } }
        public static Font BodyFont { get { LoadFonts(); return _body; } }
        public static Font BodyBold { get { LoadFonts(); return _bodyBold; } }

        // ---------- generated textures ----------
        private static readonly Dictionary<string, Texture2D> _texCache =
            new Dictionary<string, Texture2D>();

        /// <summary>Rounded-rect texture for 9-slice styles: fill + hard border, with an
        /// optional thicker bottom border (the mockups' press-able button look).</summary>
        private static Texture2D Rounded(string key, Color fill, Color border,
            int borderPx = 2, int radius = 8, int extraBottomPx = 0, Color? bottom = null)
        {
            if (_texCache.TryGetValue(key, out var cached) && cached != null) return cached;
            const int size = 32;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            tex.hideFlags = HideFlags.HideAndDontSave;
            var clear = new Color(0, 0, 0, 0);
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    // Signed distance to the rounded-rect edge.
                    float cx = Mathf.Max(radius - x, x - (size - 1 - radius), 0);
                    float cy = Mathf.Max(radius - y, y - (size - 1 - radius), 0);
                    float d = Mathf.Sqrt(cx * cx + cy * cy);
                    Color c;
                    if (d > radius) c = clear;
                    else if (d > radius - borderPx) c = border;
                    else if (y < borderPx + extraBottomPx && bottom.HasValue) c = bottom.Value;
                    else c = fill;
                    tex.SetPixel(x, y, c);
                }
            tex.Apply();
            _texCache[key] = tex;
            return tex;
        }

        private static Texture2D Solid(string key, Color c)
        {
            if (_texCache.TryGetValue(key, out var cached) && cached != null) return cached;
            var tex = new Texture2D(1, 1, TextureFormat.RGBA32, false)
                { hideFlags = HideFlags.HideAndDontSave };
            tex.SetPixel(0, 0, c);
            tex.Apply();
            _texCache[key] = tex;
            return tex;
        }

        /// <summary>Vertical gradient bar fill with a glassy highlight on the top half.</summary>
        private static Texture2D Glassy(string key, Color c)
        {
            if (_texCache.TryGetValue(key, out var cached) && cached != null) return cached;
            const int h = 16;
            var tex = new Texture2D(1, h, TextureFormat.RGBA32, false)
                { hideFlags = HideFlags.HideAndDontSave };
            for (int y = 0; y < h; y++)
            {
                float t = y / (float)(h - 1);                       // 0 bottom → 1 top
                var row = Color.Lerp(c * 0.72f, c * 1.05f, t);
                if (t > 0.55f) row = Color.Lerp(row, Color.white, 0.22f);   // glass
                row.a = 1f;
                tex.SetPixel(0, y, row);
            }
            tex.Apply();
            _texCache[key] = tex;
            return tex;
        }

        public static Texture2D PanelTex => Rounded("panel", Panel, GoldDeep, 2, 8);
        public static Texture2D PanelPlainTex => Rounded("panelPlain", Panel, PanelBright, 1, 6);
        public static Texture2D ParchmentTex => Rounded("parchment", Parchment, ParchmentEdge, 1, 6);
        public static Texture2D GoldWashTex => Rounded("goldwash", new Color(0.98f, 0.93f, 0.72f), GoldDeep, 2, 6);
        public static Texture2D WellTex => Rounded("well", Hex("#0e0e0e"), Color.black, 1, 4);
        public static Texture2D BtnStoneTex => Rounded("btnStone", PanelHigh, GoldDeep, 1, 10);
        public static Texture2D BtnStoneHoverTex => Rounded("btnStoneHov", PanelBright, Gold, 1, 10);
        public static Texture2D BtnBlueTex => Rounded("btnBlue", MageBlue, MageBlue, 1, 10, 2, GoldDeep);
        public static Texture2D BtnBlueHoverTex => Rounded("btnBlueHov", Hex("#4a72d0"), Hex("#4a72d0"), 1, 10, 2, Gold);
        public static Texture2D BtnGoldTex => Rounded("btnGold", Gold, GoldDeep, 2, 10);

        // ---------- styles ----------
        private static GUIStyle _header, _headerBig, _headerInk, _caps, _body14, _bodyInk,
            _btnPrimary, _panel, _parchment, _goldWash, _toast;

        private static GUIStyle MakeLabel(Font font, int size, Color color, FontStyle fallback)
        {
            var s = new GUIStyle(GUI.skin.label)
            {
                fontSize = size,
                richText = true,
                wordWrap = true
            };
            if (font != null) s.font = font;
            else s.fontStyle = fallback;
            s.normal.textColor = color;
            s.hover.textColor = color;
            return s;
        }

        /// <summary>Serif gold header — panel titles ("Journal", NPC names).</summary>
        public static GUIStyle Header =>
            _header ??= MakeLabel(Serif, 19, Gold, FontStyle.Bold);

        /// <summary>Big serif display header — title screen, banners.</summary>
        public static GUIStyle HeaderBig =>
            _headerBig ??= MakeLabel(Serif, 34, Gold, FontStyle.Bold);

        /// <summary>Serif ink header for parchment areas.</summary>
        public static GUIStyle HeaderInk =>
            _headerInk ??= MakeLabel(SerifSemi, 16, Ink, FontStyle.Bold);

        /// <summary>All-caps small label (category headers, stat names).</summary>
        public static GUIStyle Caps =>
            _caps ??= MakeLabel(BodyBold, 11, OnSurfaceMuted, FontStyle.Bold);

        /// <summary>Standard body text on dark panels.</summary>
        public static GUIStyle Body =>
            _body14 ??= MakeLabel(BodyFont, 14, OnSurface, FontStyle.Normal);

        /// <summary>Body text on parchment.</summary>
        public static GUIStyle BodyInk =>
            _bodyInk ??= MakeLabel(BodyFont, 14, Ink, FontStyle.Normal);

        /// <summary>Mage-blue primary button with the gold press-edge.</summary>
        public static GUIStyle BtnPrimary
        {
            get
            {
                if (_btnPrimary != null) return _btnPrimary;
                _btnPrimary = new GUIStyle(GUI.skin.button);
                if (BodyBold != null) _btnPrimary.font = BodyBold; else _btnPrimary.fontStyle = FontStyle.Bold;
                _btnPrimary.fontSize = 14;
                _btnPrimary.border = new RectOffset(12, 12, 12, 12);
                _btnPrimary.padding = new RectOffset(14, 14, 8, 10);
                _btnPrimary.normal.background = BtnBlueTex;
                _btnPrimary.hover.background = BtnBlueHoverTex;
                _btnPrimary.active.background = BtnBlueTex;
                _btnPrimary.normal.textColor = Color.white;
                _btnPrimary.hover.textColor = Color.white;
                _btnPrimary.active.textColor = new Color(0.9f, 0.9f, 1f);
                return _btnPrimary;
            }
        }

        /// <summary>Charcoal stone panel with gold border (Level 1 elevation).</summary>
        public static GUIStyle PanelStyle
        {
            get
            {
                if (_panel != null) return _panel;
                _panel = new GUIStyle(GUI.skin.box)
                {
                    border = new RectOffset(10, 10, 10, 10),
                    padding = new RectOffset(14, 14, 12, 12)
                };
                _panel.normal.background = PanelTex;
                _panel.normal.textColor = OnSurface;
                return _panel;
            }
        }

        /// <summary>Parchment content card nested inside a panel (Level 2).</summary>
        public static GUIStyle ParchmentStyle
        {
            get
            {
                if (_parchment != null) return _parchment;
                _parchment = new GUIStyle(GUI.skin.box)
                {
                    border = new RectOffset(8, 8, 8, 8),
                    padding = new RectOffset(12, 12, 10, 10)
                };
                _parchment.normal.background = ParchmentTex;
                _parchment.normal.textColor = Ink;
                return _parchment;
            }
        }

        /// <summary>Faint gold wash box — active quest highlight.</summary>
        public static GUIStyle GoldWashStyle
        {
            get
            {
                if (_goldWash != null) return _goldWash;
                _goldWash = new GUIStyle(GUI.skin.box)
                {
                    border = new RectOffset(8, 8, 8, 8),
                    padding = new RectOffset(10, 10, 8, 8)
                };
                _goldWash.normal.background = GoldWashTex;
                _goldWash.normal.textColor = Ink;
                return _goldWash;
            }
        }

        /// <summary>Centered toast/notice: stone panel, serif gold text.</summary>
        public static GUIStyle Toast
        {
            get
            {
                if (_toast != null) return _toast;
                _toast = new GUIStyle(PanelStyle)
                {
                    alignment = TextAnchor.MiddleCenter,
                    fontSize = 15,
                    richText = true,
                    wordWrap = false
                };
                if (SerifSemi != null) _toast.font = SerifSemi; else _toast.fontStyle = FontStyle.Bold;
                _toast.normal.textColor = Gold;
                return _toast;
            }
        }

        // ---------- bars ----------
        /// <summary>Recessed well + glassy gradient fill, per the mock HP/MP bars.</summary>
        public static void Bar(Rect r, float fraction, Color fill)
        {
            GUI.DrawTexture(r, WellTex, ScaleMode.StretchToFill, true, 0,
                Color.white, Vector4.zero, 4f);
            float f = Mathf.Clamp01(fraction);
            if (f <= 0f) return;
            var inner = new Rect(r.x + 2, r.y + 2, (r.width - 4) * f, r.height - 4);
            GUI.DrawTexture(inner, Glassy("glass" + ColorUtility.ToHtmlStringRGB(fill), fill),
                ScaleMode.StretchToFill, true, 0, Color.white, Vector4.zero, 3f);
        }

        // ---------- global skin ----------
        private static bool _applied;

        /// <summary>Re-skins GUI.skin so every OnGUI inherits the theme: stone boxes,
        /// gold-outlined stone buttons (gold when active), Inter labels, parchment
        /// text fields. Called from Ui.Begin().</summary>
        public static void Apply()
        {
            var skin = GUI.skin;
            if (_applied) return;
            _applied = true;
            LoadFonts();

            if (_body != null) { skin.label.font = _body; skin.button.font = _bodyBold; }
            skin.label.normal.textColor = OnSurface;
            skin.label.richText = true;

            skin.box.normal.background = PanelPlainTex;
            skin.box.border = new RectOffset(8, 8, 8, 8);
            skin.box.padding = new RectOffset(10, 10, 8, 8);
            skin.box.normal.textColor = OnSurfaceMuted;

            skin.button.border = new RectOffset(12, 12, 12, 12);
            skin.button.padding = new RectOffset(12, 12, 7, 8);
            skin.button.normal.background = BtnStoneTex;
            skin.button.hover.background = BtnStoneHoverTex;
            skin.button.active.background = BtnGoldTex;
            skin.button.focused.background = BtnStoneHoverTex;
            skin.button.normal.textColor = OnSurface;
            skin.button.hover.textColor = Gold;
            skin.button.active.textColor = OnGold;
            skin.button.focused.textColor = Gold;

            skin.textField.normal.background = ParchmentTex;
            skin.textField.focused.background = ParchmentTex;
            skin.textField.hover.background = ParchmentTex;
            skin.textField.border = new RectOffset(6, 6, 6, 6);
            skin.textField.padding = new RectOffset(8, 8, 5, 5);
            skin.textField.normal.textColor = Ink;
            skin.textField.focused.textColor = Ink;
            skin.textField.hover.textColor = Ink;
            if (_body != null) skin.textField.font = _body;

            skin.toggle.normal.textColor = OnSurface;
            skin.toggle.hover.textColor = Gold;

            skin.horizontalScrollbar.normal.background = Solid("scrollBg", Hex("#0e0e0e"));
            skin.verticalScrollbar.normal.background = Solid("scrollBg", Hex("#0e0e0e"));
            skin.horizontalScrollbarThumb.normal.background = Solid("scrollThumb", GoldDeep);
            skin.verticalScrollbarThumb.normal.background = Solid("scrollThumb", GoldDeep);
        }
    }
}
