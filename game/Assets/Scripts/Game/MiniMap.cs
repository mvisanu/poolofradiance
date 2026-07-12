using System.Linq;
using UnityEngine;

namespace RadiantPool.Game
{
    /// <summary>Top-right minimap: an orthographic camera renders the world straight
    /// down into a RenderTexture that follows the local player. North-fixed; the player
    /// is the arrow in the middle, the quest objective a gold dot (edge-clamped).
    /// Scroll wheel over the map zooms it (the orbit camera yields while hovered).</summary>
    public class MiniMap : MonoBehaviour
    {
        private const int Size = 210;           // on-screen pixels (pre UI scale)
        private const float MinRadius = 20f;    // world meters shown from center to edge
        private const float MaxRadius = 220f;   // wide enough to bring any zone objective in view

        private float _viewRadius = 42f;

        private Camera _mapCam;
        private RenderTexture _rt;
        private Texture2D _playerArrow;
        private Texture2D _questDot;

        /// <summary>On-screen map rect in Ui-scaled space (shared with HUD hit-tests).</summary>
        public static Rect MapRect => new Rect(Ui.W - Size - 12, 118, Size, Size);

        /// <summary>True while the cursor is over the minimap — scroll belongs to it.</summary>
        public static bool MouseOverMap
        {
            get
            {
                var m = new Vector2(Input.mousePosition.x / Ui.Scale,
                    (Screen.height - Input.mousePosition.y) / Ui.Scale);
                return MapRect.Contains(m);
            }
        }

        private void Start()
        {
            _rt = new RenderTexture(256, 256, 16);
            var go = new GameObject("MiniMapCamera");
            go.transform.SetParent(transform, false);
            _mapCam = go.AddComponent<Camera>();
            _mapCam.orthographic = true;
            _mapCam.orthographicSize = _viewRadius;
            _mapCam.targetTexture = _rt;
            _mapCam.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            _mapCam.backgroundColor = new Color(0.12f, 0.14f, 0.18f);
            _mapCam.clearFlags = CameraClearFlags.SolidColor;

            _playerArrow = MakeArrowTexture(new Color(0.35f, 0.8f, 1f));
            _questDot = MakeXTexture(new Color(0.95f, 0.15f, 0.1f));
        }

        private Transform LocalPlayer()
        {
            var holder = FindObjectsByType<PlayerCharacterHolder>(FindObjectsSortMode.None)
                .FirstOrDefault(p => p.IsOwner);
            return holder != null ? holder.transform : null;
        }

        private void LateUpdate()
        {
            var player = LocalPlayer();
            if (player == null) { _mapCam.enabled = false; return; }
            _mapCam.enabled = true;
            _mapCam.transform.position = player.position + Vector3.up * 90f;

            float scroll = Input.GetAxis("Mouse ScrollWheel");
            if (Mathf.Abs(scroll) > 0.001f && MouseOverMap)
                _viewRadius = Mathf.Clamp(
                    _viewRadius * (scroll > 0f ? 1f / 1.25f : 1.25f),
                    MinRadius, MaxRadius);
            _mapCam.orthographicSize = _viewRadius;
        }

        private void OnGUI()
        {
            Ui.Begin();
            var player = LocalPlayer();
            if (player == null || _rt == null) return;

            var rect = MapRect;
            GUI.Box(new Rect(rect.x - 3, rect.y - 3, rect.width + 6, rect.height + 6),
                GUIContent.none);
            GUI.DrawTexture(rect, _rt, ScaleMode.StretchToFill);

            // Quest objective: a pulsing red X (edge-clamped when out of view).
            var tracker = QuestTracker.Instance;
            if (tracker != null && tracker.HasTarget)
            {
                Vector3 delta = tracker.TargetPosition - player.position;
                var mapDelta = new Vector2(delta.x, -delta.z)
                               * (Size / 2f / _viewRadius);
                mapDelta = Vector2.ClampMagnitude(mapDelta, Size / 2f - 12f);
                var dotPos = new Vector2(rect.x + Size / 2f + mapDelta.x,
                    rect.y + Size / 2f + mapDelta.y);
                float xs = 22f + Mathf.Sin(Time.time * 4f) * 3f;
                GUI.DrawTexture(new Rect(dotPos.x - xs / 2f, dotPos.y - xs / 2f, xs, xs),
                    _questDot);
            }

            // Player arrow, rotated to face heading (map stays north-up).
            var center = new Vector2(rect.x + Size / 2f, rect.y + Size / 2f);
            var prev = GUI.matrix;
            GUIUtility.RotateAroundPivot(player.eulerAngles.y, center);
            GUI.DrawTexture(new Rect(center.x - 9, center.y - 9, 18, 18), _playerArrow);
            GUI.matrix = prev;
        }

        private static Texture2D MakeArrowTexture(Color color)
        {
            const int s = 32;
            var tex = new Texture2D(s, s, TextureFormat.RGBA32, false);
            for (int y = 0; y < s; y++)
                for (int x = 0; x < s; x++)
                {
                    // Upward triangle: wide at bottom, point at top.
                    float half = (1f - (float)y / s) * (s * 0.38f);
                    bool inside = y > s * 0.15f && Mathf.Abs(x - s / 2f) < half
                                  && y < s * 0.85f;
                    tex.SetPixel(x, s - 1 - y, inside ? color : Color.clear);
                }
            tex.Apply();
            return tex;
        }

        /// <summary>Bold X with a black outline so it reads on any terrain color.</summary>
        private static Texture2D MakeXTexture(Color color)
        {
            const int s = 28;
            var tex = new Texture2D(s, s, TextureFormat.RGBA32, false);
            for (int y = 0; y < s; y++)
                for (int x = 0; x < s; x++)
                {
                    float d1 = Mathf.Abs(x - y);              // main diagonal stroke
                    float d2 = Mathf.Abs(x + y - (s - 1));    // anti-diagonal stroke
                    float m = Mathf.Min(d1, d2);
                    Color c = m < 3f ? color
                        : m < 5f ? Color.black
                        : Color.clear;
                    tex.SetPixel(x, y, c);
                }
            tex.Apply();
            return tex;
        }
    }
}
