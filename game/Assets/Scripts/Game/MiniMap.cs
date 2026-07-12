using System.Linq;
using UnityEngine;

namespace RadiantPool.Game
{
    /// <summary>Top-right minimap: an orthographic camera renders the world straight
    /// down into a RenderTexture that follows the local player. North-fixed; the player
    /// is the arrow in the middle, the quest objective a gold dot (edge-clamped).</summary>
    public class MiniMap : MonoBehaviour
    {
        private const int Size = 210;          // on-screen pixels (pre UI scale)
        private const float ViewRadius = 42f;  // world meters shown from center to edge

        private Camera _mapCam;
        private RenderTexture _rt;
        private Texture2D _playerArrow;
        private Texture2D _questDot;

        private void Start()
        {
            _rt = new RenderTexture(256, 256, 16);
            var go = new GameObject("MiniMapCamera");
            go.transform.SetParent(transform, false);
            _mapCam = go.AddComponent<Camera>();
            _mapCam.orthographic = true;
            _mapCam.orthographicSize = ViewRadius;
            _mapCam.targetTexture = _rt;
            _mapCam.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            _mapCam.backgroundColor = new Color(0.12f, 0.14f, 0.18f);
            _mapCam.clearFlags = CameraClearFlags.SolidColor;

            _playerArrow = MakeArrowTexture(new Color(0.35f, 0.8f, 1f));
            _questDot = MakeDotTexture(new Color(1f, 0.82f, 0.25f));
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
        }

        private void OnGUI()
        {
            Ui.Begin();
            var player = LocalPlayer();
            if (player == null || _rt == null) return;

            var rect = new Rect(Ui.W - Size - 12, 118, Size, Size);
            GUI.Box(new Rect(rect.x - 3, rect.y - 3, rect.width + 6, rect.height + 6),
                GUIContent.none);
            GUI.DrawTexture(rect, _rt, ScaleMode.StretchToFill);

            // Quest objective dot (edge-clamped).
            var tracker = QuestTracker.Instance;
            if (tracker != null && tracker.HasTarget)
            {
                Vector3 delta = tracker.TargetPosition - player.position;
                var mapDelta = new Vector2(delta.x, -delta.z)
                               * (Size / 2f / ViewRadius);
                mapDelta = Vector2.ClampMagnitude(mapDelta, Size / 2f - 8f);
                var dotPos = new Vector2(rect.x + Size / 2f + mapDelta.x,
                    rect.y + Size / 2f + mapDelta.y);
                GUI.DrawTexture(new Rect(dotPos.x - 7, dotPos.y - 7, 14, 14), _questDot);
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

        private static Texture2D MakeDotTexture(Color color)
        {
            const int s = 16;
            var tex = new Texture2D(s, s, TextureFormat.RGBA32, false);
            for (int y = 0; y < s; y++)
                for (int x = 0; x < s; x++)
                {
                    float d = Vector2.Distance(new Vector2(x, y), new Vector2(s / 2f, s / 2f));
                    tex.SetPixel(x, y, d < s / 2f - 1 ? color : Color.clear);
                }
            tex.Apply();
            return tex;
        }
    }
}
