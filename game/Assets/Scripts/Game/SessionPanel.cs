using UnityEngine;

namespace RadiantPool.Game
{
    /// <summary>The session/hosting screen, reached from the hotbar (and closed the same way).
    ///
    /// It used to be a strip nailed to the top-left corner of the play screen for the whole
    /// campaign — the invite code matters for about ten seconds, when a friend is joining, and
    /// then it is just a box sitting on the city. Now it is one more icon on the bar: open it
    /// when someone wants the code, close it and have the view back.
    ///
    /// SessionLauncher stays the owner of the truth (SessionLauncher.Status / HostCode); this
    /// only draws it.</summary>
    public class SessionPanel : MonoBehaviour
    {
        private static Texture2D _icon;

        /// <summary>A generated icon, not a font glyph: the body font has no party/link glyph
        /// and a missing one renders as a tofu box (see the minimap buttons). Two figures
        /// shoulder to shoulder — the party, and who else is in it.</summary>
        public static Texture2D Icon
        {
            get
            {
                if (_icon != null) return _icon;
                const int n = 32;
                _icon = new Texture2D(n, n, TextureFormat.RGBA32, false)
                    { hideFlags = HideFlags.HideAndDontSave, filterMode = FilterMode.Bilinear };
                var clear = new Color(0f, 0f, 0f, 0f);
                var gold = Theme.Gold;
                var deep = Theme.GoldDeep;

                for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    bool headL = Inside(x, y, 11f, 22f, 4.4f);
                    bool headR = Inside(x, y, 21f, 22f, 4.4f);
                    bool bodyL = Inside(x, y, 11f, 11f, 7.2f) && y <= 15;
                    bool bodyR = Inside(x, y, 21f, 11f, 7.2f) && y <= 15;
                    bool on = headL || headR || bodyL || bodyR;
                    _icon.SetPixel(x, y, on ? (headR || bodyR ? deep : gold) : clear);
                }
                _icon.Apply();
                return _icon;
            }
        }

        private static bool Inside(int x, int y, float cx, float cy, float r) =>
            (x - cx) * (x - cx) + (y - cy) * (y - cy) <= r * r;

        private void OnGUI()
        {
            Ui.Begin();
            if (!Ui.IsOpen(Ui.Panel.Session)) return;

            GUILayout.BeginArea(Ui.FitTop(400f, 250f, top: 70f, bottomMargin: 110f),
                Theme.PanelStyle);
            GUILayout.BeginHorizontal();
            GUILayout.Label("Session", Theme.Header);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("X", GUILayout.Width(28), GUILayout.Height(22)))
                Ui.Close(Ui.Panel.Session);
            GUILayout.EndHorizontal();

            GUILayout.Label($"<color=#f2ca50>{SessionLauncher.Status}</color>",
                new GUIStyle(Theme.Body) { richText = true, wordWrap = true });

            if (SessionLauncher.HostCode.Length > 0)
            {
                GUILayout.Space(4);
                if (GUILayout.Button($"Copy invite code  {SessionLauncher.HostCode}",
                        GUILayout.Height(28)))
                    GUIUtility.systemCopyBuffer = SessionLauncher.HostCode;
                GUILayout.Label("<color=#d0c5af>Friends join with this code from the " +
                                "title screen.</color>",
                    new GUIStyle(Theme.Body) { richText = true, wordWrap = true });
            }

            GUILayout.Space(6);
            GUILayout.Label("CONTROLS", Theme.Caps);
            GUILayout.Label("<color=#d0c5af>WASD move · RMB camera · E talk · I bags · " +
                            "J journal · L level up · M map · Esc settings</color>",
                new GUIStyle(Theme.Body) { richText = true, wordWrap = true });
            GUILayout.EndArea();
        }
    }
}
