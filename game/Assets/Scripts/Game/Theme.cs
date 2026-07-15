using System.Collections.Generic;
using UnityEngine;

namespace RadiantPool.Game
{
    /// <summary>The "Gilded Quest" design system from theme/ (Stitch mockups): charcoal
    /// stone panels with gold borders, parchment content cards, mage-blue primary
    /// buttons, Source Serif 4 headers + Inter body, class accent colors, and glassy
    /// HP/MP bars. When the locally licensed RPG & MMO UI 7 pack is installed, its baked
    /// frames and controls replace the procedural surfaces globally; public builds retain
    /// the generated fallback. Fonts ship in Resources/Fonts (SIL OFL).</summary>
    public static class Theme
    {
        // ---------- palette ----------
        // Base: theme/.../gilded_quest/DESIGN.md, refined with the ui-ux-pro-max
        // "Academia (Scholarly)" palette: mahogany/oak panels instead of flat gray,
        // brass borders instead of raw gold, true parchment instead of glare-white,
        // and parchment text colors that pass the 4.5:1 contrast guideline.
        public static readonly Color Surface        = Hex("#1c1714");   // mahogany
        public static readonly Color Panel          = Hex("#241e19");   // oak
        public static readonly Color PanelHigh      = Hex("#2e2721");
        public static readonly Color PanelBright    = Hex("#3d332b");   // worn leather
        public static readonly Color OnSurface      = Hex("#e8e2d8");
        public static readonly Color OnSurfaceMuted = Hex("#cbbb9c");
        public static readonly Color Gold           = Hex("#f2ca50");   // active/emphasis
        public static readonly Color GoldDeep       = Hex("#c9a962");   // brass borders
        public static readonly Color OnGold         = Hex("#33270a");
        public static readonly Color Parchment      = Hex("#e8dfd4");   // true parchment
        public static readonly Color ParchmentEdge  = Hex("#c4b69e");
        public static readonly Color Ink            = Hex("#2a241e");
        public static readonly Color InkMuted       = Hex("#6b6257");   // 4.5:1 on parchment
        public static readonly Color Crimson        = Hex("#8b2635");   // library crimson
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
            // Headers use MedievalSharp (user's pick); Source Serif is the fallback.
            _serif = Resources.Load<Font>("Fonts/MedievalSharp");
            if (_serif == null) _serif = Resources.Load<Font>("Fonts/SourceSerif4-Bold");
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

        public static bool RpgMmoUi7Ready => RpgMmoUi7Skin.Available;
        public static Texture2D PanelTex => RpgMmoUi7Skin.Get("panel",
            Rounded("panel", Panel, GoldDeep, 2, 8));
        public static Texture2D PanelPlainTex => RpgMmoUi7Skin.Get("panel_plain",
            Rounded("panelPlain", Panel, PanelBright, 1, 6));
        public static Texture2D ParchmentTex => Rounded("parchment", Parchment, ParchmentEdge, 1, 6);
        public static Texture2D GoldWashTex => Rounded("goldwash", new Color(0.94f, 0.88f, 0.72f), GoldDeep, 2, 6);
        public static Texture2D WellTex => RpgMmoUi7Skin.Get("bar",
            Rounded("well", Hex("#0e0e0e"), Color.black, 1, 4));
        public static Texture2D BtnStoneTex => RpgMmoUi7Skin.Get("button",
            Rounded("btnStone", PanelHigh, GoldDeep, 1, 10));
        public static Texture2D BtnStoneHoverTex => RpgMmoUi7Skin.Get("button_hover",
            Rounded("btnStoneHov", PanelBright, Gold, 1, 10));
        public static Texture2D BtnBlueTex => RpgMmoUi7Skin.Get("primary",
            Rounded("btnBlue", MageBlue, MageBlue, 1, 10, 2, GoldDeep));
        public static Texture2D BtnBlueHoverTex => RpgMmoUi7Skin.Get("primary_hover",
            Rounded("btnBlueHov", Hex("#4a72d0"), Hex("#4a72d0"), 1, 10, 2, Gold));
        public static Texture2D BtnGoldTex => RpgMmoUi7Skin.Get("button_pressed",
            Rounded("btnGold", Gold, GoldDeep, 2, 10));
        public static Texture2D FieldTex => RpgMmoUi7Skin.Get("field", ParchmentTex);
        public static Texture2D SlotTex => RpgMmoUi7Skin.Get("slot", BtnStoneTex);

        // ---------- styles ----------
        private static GUIStyle _header, _headerBig, _headerInk, _caps, _body14, _bodyInk,
            _btnPrimary, _slot, _tab, _tabActive, _panel, _parchment, _goldWash, _toast;

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
            _header ??= MakeLabel(Serif, 16, Gold, FontStyle.Bold);

        /// <summary>Big serif display header — title screen, banners.</summary>
        public static GUIStyle HeaderBig =>
            _headerBig ??= MakeLabel(Serif, 28, Gold, FontStyle.Bold);

        /// <summary>Serif ink header for parchment areas.</summary>
        public static GUIStyle HeaderInk =>
            _headerInk ??= MakeLabel(SerifSemi, 14, Ink, FontStyle.Bold);

        /// <summary>All-caps small label (category headers, stat names).</summary>
        public static GUIStyle Caps =>
            _caps ??= MakeLabel(BodyBold, 10, OnSurfaceMuted, FontStyle.Bold);

        /// <summary>Standard body text on dark panels.</summary>
        public static GUIStyle Body =>
            _body14 ??= MakeLabel(BodyFont, 12, OnSurface, FontStyle.Normal);

        /// <summary>Body text on parchment.</summary>
        public static GUIStyle BodyInk =>
            _bodyInk ??= MakeLabel(BodyFont, 12, Ink, FontStyle.Normal);

        /// <summary>Mage-blue primary button with the gold press-edge.</summary>
        public static GUIStyle BtnPrimary
        {
            get
            {
                if (_btnPrimary != null) return _btnPrimary;
                _btnPrimary = new GUIStyle(GUI.skin.button);
                if (BodyBold != null) _btnPrimary.font = BodyBold; else _btnPrimary.fontStyle = FontStyle.Bold;
                _btnPrimary.fontSize = 12;
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

        /// <summary>Square MMO action-slot chrome for hotbar and compact icon controls.</summary>
        public static GUIStyle SlotStyle
        {
            get
            {
                if (_slot != null) return _slot;
                _slot = new GUIStyle(GUI.skin.button)
                {
                    border = new RectOffset(10, 10, 10, 10),
                    padding = new RectOffset(5, 5, 5, 5),
                    alignment = TextAnchor.MiddleCenter
                };
                _slot.normal.background = SlotTex;
                _slot.hover.background = RpgMmoUi7Skin.Get("tab_active", BtnStoneHoverTex);
                _slot.active.background = BtnGoldTex;
                _slot.focused.background = RpgMmoUi7Skin.Get("tab_active", BtnStoneHoverTex);
                _slot.normal.textColor = OnSurface;
                _slot.hover.textColor = Gold;
                _slot.active.textColor = OnGold;
                return _slot;
            }
        }

        /// <summary>MMO character/category tab chrome. The selected state remains readable
        /// without disabling the control (Unity's disabled tint muddies licensed artwork).</summary>
        public static GUIStyle TabStyle(bool selected)
        {
            GUIStyle cached = selected ? _tabActive : _tab;
            if (cached != null) return cached;
            cached = new GUIStyle(GUI.skin.button)
            {
                border = new RectOffset(12, 12, 10, 10),
                padding = new RectOffset(10, 10, 6, 7),
                alignment = TextAnchor.MiddleCenter
            };
            Texture2D normal = RpgMmoUi7Skin.Get(selected ? "tab_active" : "tab",
                selected ? BtnBlueTex : BtnStoneTex);
            cached.normal.background = normal;
            cached.hover.background = selected ? normal : BtnStoneHoverTex;
            cached.active.background = normal;
            cached.focused.background = normal;
            cached.normal.textColor = selected ? Color.white : OnSurface;
            cached.hover.textColor = selected ? Color.white : Gold;
            cached.active.textColor = cached.normal.textColor;
            if (selected) _tabActive = cached;
            else _tab = cached;
            return cached;
        }

        /// <summary>Charcoal stone panel with gold border (Level 1 elevation).</summary>
        public static GUIStyle PanelStyle
        {
            get
            {
                if (_panel != null) return _panel;
                int edge = RpgMmoUi7Ready ? 16 : 10;
                _panel = new GUIStyle(GUI.skin.box)
                {
                    border = new RectOffset(edge, edge, edge, edge),
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
                    fontSize = 13,
                    richText = true,
                    wordWrap = false
                };
                if (SerifSemi != null) _toast.font = SerifSemi; else _toast.fontStyle = FontStyle.Bold;
                _toast.normal.background = RpgMmoUi7Skin.Get("tooltip", PanelTex);
                _toast.normal.textColor = Gold;
                return _toast;
            }
        }

        private static GUIStyle _toastWrap;

        /// <summary>Auto-sized toast: measures the text and fits the box to it (never
        /// spills past the border); very long text wraps onto extra lines instead.</summary>
        public static void DrawToast(float centerX, float y, string text, float maxW = 660f)
        {
            var content = new GUIContent(text);
            float w = Toast.CalcSize(content).x + 12f;
            if (w <= maxW)
            {
                GUI.Box(new Rect(centerX - w / 2f, y, w, 38f), content, Toast);
            }
            else
            {
                if (_toastWrap == null) _toastWrap = new GUIStyle(Toast) { wordWrap = true };
                float h = _toastWrap.CalcHeight(content, maxW - 28f) + 20f;
                GUI.Box(new Rect(centerX - maxW / 2f, y, maxW, h), content, _toastWrap);
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
            skin.button.onNormal.background = BtnBlueTex;
            skin.button.onHover.background = BtnBlueHoverTex;
            skin.button.onActive.background = BtnBlueTex;
            skin.button.onFocused.background = BtnBlueHoverTex;
            skin.button.normal.textColor = OnSurface;
            skin.button.hover.textColor = Gold;
            skin.button.active.textColor = OnGold;
            skin.button.focused.textColor = Gold;
            skin.button.onNormal.textColor = Color.white;
            skin.button.onHover.textColor = Color.white;
            skin.button.onActive.textColor = Color.white;
            skin.button.onFocused.textColor = Color.white;

            skin.textField.normal.background = FieldTex;
            skin.textField.focused.background = FieldTex;
            skin.textField.hover.background = FieldTex;
            skin.textField.border = new RectOffset(6, 6, 6, 6);
            skin.textField.padding = new RectOffset(8, 8, 5, 5);
            skin.textField.normal.textColor = Ink;
            skin.textField.focused.textColor = Ink;
            skin.textField.hover.textColor = Ink;
            if (_body != null) skin.textField.font = _body;

            skin.toggle.normal.textColor = OnSurface;
            skin.toggle.hover.textColor = Gold;

            var scrollTrack = RpgMmoUi7Skin.Get("scroll_track",
                Solid("scrollBg", Hex("#0e0e0e")));
            var scrollThumb = RpgMmoUi7Skin.Get("scroll_thumb",
                Solid("scrollThumb", GoldDeep));
            skin.horizontalScrollbar.normal.background = scrollTrack;
            skin.verticalScrollbar.normal.background = scrollTrack;
            skin.horizontalScrollbarThumb.normal.background = scrollThumb;
            skin.verticalScrollbarThumb.normal.background = scrollThumb;

            // Sliders were raw Unity gray and the thumb was a 10 px sliver — the settings
            // panel is the one place the mouse has to hit something small, so the thumb is
            // sized to be grabbable and the groove is themed like the HP wells.
            skin.horizontalSlider.normal.background = RpgMmoUi7Skin.Get("slider", WellTex);
            skin.horizontalSlider.border = new RectOffset(4, 4, 4, 4);
            skin.horizontalSlider.fixedHeight = 14f;
            skin.horizontalSlider.margin = new RectOffset(4, 4, 8, 8);
            var thumb = RpgMmoUi7Skin.Get("thumb", BtnGoldTex);
            skin.horizontalSliderThumb.normal.background = thumb;
            skin.horizontalSliderThumb.hover.background = thumb;
            skin.horizontalSliderThumb.active.background = thumb;
            skin.horizontalSliderThumb.border = new RectOffset(6, 6, 6, 6);
            skin.horizontalSliderThumb.fixedWidth = 20f;
            skin.horizontalSliderThumb.fixedHeight = 20f;

            var toggleOff = RpgMmoUi7Skin.Get("toggle_off");
            var toggleOn = RpgMmoUi7Skin.Get("toggle_on");
            if (toggleOff != null && toggleOn != null)
            {
                skin.toggle.normal.background = toggleOff;
                skin.toggle.hover.background = toggleOff;
                skin.toggle.active.background = toggleOff;
                skin.toggle.onNormal.background = toggleOn;
                skin.toggle.onHover.background = toggleOn;
                skin.toggle.onActive.background = toggleOn;
                skin.toggle.border = new RectOffset(8, 8, 8, 8);
                skin.toggle.padding = new RectOffset(24, 4, 2, 2);
            }

            Debug.Log(RpgMmoUi7Ready
                ? $"[RpgMmoUi7] READY - {RpgMmoUi7Skin.LoadedRoleCount}/" +
                  $"{RpgMmoUi7Skin.Roles.Length} themed roles active on all UI screens."
                : "[RpgMmoUi7] licensed art unavailable; generated Gilded Quest fallback active.");
        }

        // ---------- text markers ----------
        // The fonts here have no dingbat glyphs — ✔ ✘ ⚔ ✝ ● all render as tofu boxes on
        // MedievalSharp/Inter. State reads in words (and colour on top), never in a glyph
        // that may not exist. Same rule as the minimap's generated icons.

        /// <summary>"ready" / "spent" pair for action economy — colour AND word.</summary>
        public static string Ready(bool on) => on
            ? "<color=#7fc47f>ready</color>"
            : "<color=#c47f7f>spent</color>";

        /// <summary>Done/outstanding checklist marker (ASCII, always renders).</summary>
        public static string Check(bool done) => done
            ? "<color=#7fc47f>[x]</color>"
            : "<color=#f2ca50>[ ]</color>";

        /// <summary>Spell slots as ASCII pips — "[+][+][ ][ ]".</summary>
        public static string Pips(int filled, int total)
        {
            filled = Mathf.Clamp(filled, 0, total);
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < total; i++)
                sb.Append(i < filled ? "<color=#3d6ff2>[+]</color>" : "<color=#6b6257>[ ]</color>");
            return sb.ToString();
        }
    }
}
