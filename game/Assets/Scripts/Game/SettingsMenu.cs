using UnityEngine;

namespace RadiantPool.Game
{
    /// <summary>Esc settings panel: audio volume, UI size, window mode, vsync, camera
    /// sensitivity. Persisted via PlayerPrefs. Keybinds stay fixed in v1 (documented in
    /// README) — full rebinding is listed in known-limitations.
    ///
    /// Esc is a "back" key, not a settings toggle: with the inventory or journal up it
    /// closes that first, and only opens Settings from a clear screen. That is the
    /// behaviour every player already expects from Esc.</summary>
    public class SettingsMenu : MonoBehaviour
    {
        public static float MouseSensitivity { get; private set; } = 3.5f;
        public static SettingsMenu Instance { get; private set; }

        private void Awake() => Instance = this;
        private void OnDestroy() { if (Instance == this) Instance = null; }

        public void Toggle() => Ui.Toggle(Ui.Panel.Settings);

        private float _volume;
        private bool _fullscreen;
        private bool _vsync;

        private void Start()
        {
            _volume = PlayerPrefs.GetFloat("volume", 0.8f);
            _fullscreen = PlayerPrefs.GetInt("fullscreen", 0) == 1;
            _vsync = PlayerPrefs.GetInt("vsync", 1) == 1;
            MouseSensitivity = PlayerPrefs.GetFloat("sensitivity", 3.5f);
            Apply();
        }

        private void Apply()
        {
            AudioListener.volume = _volume;
            Screen.fullScreenMode = _fullscreen
                ? FullScreenMode.FullScreenWindow : FullScreenMode.Windowed;
            QualitySettings.vSyncCount = _vsync ? 1 : 0;
            PlayerPrefs.SetFloat("volume", _volume);
            PlayerPrefs.SetInt("fullscreen", _fullscreen ? 1 : 0);
            PlayerPrefs.SetInt("vsync", _vsync ? 1 : 0);
            PlayerPrefs.SetFloat("sensitivity", MouseSensitivity);
        }

        private void Update()
        {
            if (!Input.GetKeyDown(KeyCode.Escape) || Ui.Typing) return;
            // Back, not toggle: dismiss whatever screen is up before opening Settings.
            if (Ui.OpenPanel != Ui.Panel.None) Ui.CloseAll();
            else Ui.Show(Ui.Panel.Settings);
        }

        private void OnGUI()
        {
            Ui.Begin();
            if (!Ui.IsOpen(Ui.Panel.Settings)) return;

            var rect = Ui.Fit(360f, 330f);
            GUILayout.BeginArea(rect, Theme.PanelStyle);
            GUILayout.BeginHorizontal();
            GUILayout.Label("Settings", Theme.Header);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("X", GUILayout.Width(28), GUILayout.Height(22)))
                Ui.Close(Ui.Panel.Settings);
            GUILayout.EndHorizontal();

            GUILayout.Label($"MASTER VOLUME — {_volume:P0}", Theme.Caps);
            float volume = GUILayout.HorizontalSlider(_volume, 0f, 1f);

            // The one control that makes the game usable on a 4K panel or a tiny window:
            // everything (fonts, panels, hit targets) scales off this.
            GUILayout.Label($"UI SIZE — {Ui.UserScale:P0}", Theme.Caps);
            float uiScale = GUILayout.HorizontalSlider(Ui.UserScale, 0.7f, 1.6f);

            GUILayout.Label($"CAMERA SENSITIVITY — {MouseSensitivity:0.0}", Theme.Caps);
            float sens = GUILayout.HorizontalSlider(MouseSensitivity, 1f, 8f);

            GUILayout.Space(6);
            bool fullscreen = GUILayout.Toggle(_fullscreen, " Fullscreen (borderless)");
            bool vsync = GUILayout.Toggle(_vsync, " VSync");

            if (!Mathf.Approximately(uiScale, Ui.UserScale))
                Ui.UserScale = uiScale;
            if (!Mathf.Approximately(volume, _volume) || fullscreen != _fullscreen
                || vsync != _vsync || !Mathf.Approximately(sens, MouseSensitivity))
            {
                _volume = volume;
                _fullscreen = fullscreen;
                _vsync = vsync;
                MouseSensitivity = sens;
                Apply();
            }

            GUILayout.Space(8);
            GUILayout.Label("<color=#cbbb9c>WASD move · RMB camera · E talk · I bags · " +
                "J journal · M map · F5 save (host)\nIn combat: click to walk, click an " +
                "enemy to attack, Space ends your turn.</color>", Theme.Body);

            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Close", Theme.BtnPrimary)) Ui.Close(Ui.Panel.Settings);
            GUILayout.EndArea();
        }
    }
}
