using UnityEngine;

namespace RadiantPool.Game
{
    /// <summary>Esc settings panel (Phase 4): audio volume, window mode, vsync/quality,
    /// camera sensitivity. Persisted via PlayerPrefs. Keybinds stay fixed in v1
    /// (documented in README) — full rebinding is listed in known-limitations.</summary>
    public class SettingsMenu : MonoBehaviour
    {
        public static float MouseSensitivity { get; private set; } = 3.5f;

        private bool _open;
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
            if (Input.GetKeyDown(KeyCode.Escape)) _open = !_open;
        }

        private void OnGUI()
        {
            if (!_open) return;
            GUILayout.BeginArea(new Rect(Screen.width / 2f - 170, Screen.height / 2f - 130,
                340, 260), GUI.skin.box);
            GUILayout.Label("<b>Settings</b> (Esc to close)",
                new GUIStyle(GUI.skin.label) { richText = true });

            GUILayout.Label($"Master volume: {_volume:P0}");
            float volume = GUILayout.HorizontalSlider(_volume, 0f, 1f);
            GUILayout.Label($"Camera sensitivity: {MouseSensitivity:0.0}");
            float sens = GUILayout.HorizontalSlider(MouseSensitivity, 1f, 8f);
            bool fullscreen = GUILayout.Toggle(_fullscreen, " Fullscreen (borderless)");
            bool vsync = GUILayout.Toggle(_vsync, " VSync");

            if (!Mathf.Approximately(volume, _volume) || fullscreen != _fullscreen
                || vsync != _vsync || !Mathf.Approximately(sens, MouseSensitivity))
            {
                _volume = volume;
                _fullscreen = fullscreen;
                _vsync = vsync;
                MouseSensitivity = sens;
                Apply();
            }

            GUILayout.Space(6);
            GUILayout.Label("Keys: WASD move, Space jump, RMB camera, E interact, " +
                            "J journal, F5 save (host).");
            if (GUILayout.Button("Close")) _open = false;
            GUILayout.EndArea();
        }
    }
}
