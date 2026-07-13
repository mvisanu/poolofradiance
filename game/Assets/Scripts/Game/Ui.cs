using UnityEngine;

namespace RadiantPool.Game
{
    /// <summary>IMGUI scaling and layout for ANY window: call Begin() first in every
    /// OnGUI, then lay out against Ui.W/Ui.H (never Screen.width/height) and size every
    /// panel through Fit()/Clamp so it can't run off a small screen.
    ///
    /// The canvas is height-driven — the HUD is designed against a 630-unit-tall logical
    /// screen, so 1080p, 1440p and 4K all get the *same* layout at bigger pixels. Two
    /// guards keep that honest at the extremes: a narrow or short window scales DOWN
    /// (rather than cropping the HUD off the edges), and the player's own UI-scale
    /// preference multiplies the whole thing for eyesight or a 4K TV across the room.</summary>
    public static class Ui
    {
        /// <summary>Logical canvas height everything is laid out against.</summary>
        public const float BaseHeight = 630f;

        /// <summary>The HUD needs this much logical width (initiative right, log left,
        /// hotbar centre). A narrower window scales the UI down instead of clipping it.</summary>
        private const float MinLogicalWidth = 880f;

        private const string ScalePref = "ui.scale";
        private static float _userScale = -1f;

        /// <summary>Player-set UI size multiplier (Settings). 1 = design size.</summary>
        public static float UserScale
        {
            get
            {
                if (_userScale < 0f)
                    _userScale = Mathf.Clamp(PlayerPrefs.GetFloat(ScalePref, 1f), 0.7f, 1.6f);
                return _userScale;
            }
            set
            {
                _userScale = Mathf.Clamp(value, 0.7f, 1.6f);
                PlayerPrefs.SetFloat(ScalePref, _userScale);
            }
        }

        public static float Scale
        {
            get
            {
                float s = Mathf.Clamp(Screen.height / BaseHeight, 1f, 4f) * UserScale;
                // Never let the logical canvas get narrower than the HUD needs: on a short
                // or narrow window the whole UI shrinks so every panel still fits on screen.
                s = Mathf.Min(s, Screen.width / MinLogicalWidth);
                return Mathf.Max(s, 0.5f);
            }
        }

        /// <summary>Logical canvas size — lay everything out against these.</summary>
        public static float W => Screen.width / Scale;
        public static float H => Screen.height / Scale;

        /// <summary>Mouse position in logical (Ui) space, for hit-testing HUD rects.</summary>
        public static Vector2 Mouse => new Vector2(
            Input.mousePosition.x / Scale,
            (Screen.height - Input.mousePosition.y) / Scale);

        /// <summary>The player is typing into a text field, so single-letter hotkeys are
        /// letters, not commands. Naming a character "Jim" used to open the journal, the
        /// bags and the map on the way through.</summary>
        public static bool Typing => GUIUtility.keyboardControl != 0;

        /// <summary>A centred panel that always fits: the requested size, shrunk to the
        /// space actually available (minus margins), never off-screen.</summary>
        public static Rect Fit(float wantW, float wantH, float marginX = 16f,
            float marginY = 16f)
        {
            float w = Mathf.Min(wantW, W - marginX * 2f);
            float h = Mathf.Min(wantH, H - marginY * 2f);
            return new Rect((W - w) / 2f, (H - h) / 2f, w, h);
        }

        /// <summary>Same, but anchored under the top edge (panels that must not cover the
        /// centre of the screen, e.g. the journal over the battlefield).</summary>
        public static Rect FitTop(float wantW, float wantH, float top,
            float bottomMargin = 110f, float marginX = 16f)
        {
            float w = Mathf.Min(wantW, W - marginX * 2f);
            float h = Mathf.Min(wantH, H - top - bottomMargin);
            h = Mathf.Max(h, 120f);
            return new Rect((W - w) / 2f, top, w, h);
        }

        // ---------- one panel at a time ----------

        /// <summary>The big screens are mutually exclusive: opening one closes the others,
        /// so they can never stack into an unreadable pile (they used to overlap dead
        /// centre). Esc closes whatever is open before it reaches for Settings.</summary>
        public enum Panel { None, Inventory, Journal, Settings, LevelUp, Session }

        public static Panel OpenPanel { get; private set; } = Panel.None;

        public static bool IsOpen(Panel p) => OpenPanel == p;

        public static void Show(Panel p) => OpenPanel = p;

        public static void Close(Panel p) { if (OpenPanel == p) OpenPanel = Panel.None; }

        public static void CloseAll() => OpenPanel = Panel.None;

        public static void Toggle(Panel p) =>
            OpenPanel = OpenPanel == p ? Panel.None : p;

        public static void Begin()
        {
            GUI.matrix = Matrix4x4.Scale(new Vector3(Scale, Scale, 1f));
            Theme.Apply();   // Gilded Quest skin — see Theme.cs and theme/ mockups
            GUI.skin.label.fontSize = 12;
            GUI.skin.button.fontSize = 12;
            GUI.skin.textField.fontSize = 12;
            GUI.skin.box.fontSize = 11;
            GUI.skin.toggle.fontSize = 12;
        }
    }
}
