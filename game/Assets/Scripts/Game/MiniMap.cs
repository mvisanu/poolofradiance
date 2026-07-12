using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace RadiantPool.Game
{
    /// <summary>Top-right minimap: an orthographic camera renders the world straight
    /// down into a RenderTexture that follows the local player. North-fixed; the player
    /// is the arrow in the middle. Three sizes — collapsed pill / normal / maximized
    /// ([-]/[+] buttons or M key), remembered in PlayerPrefs. World objects get distinct
    /// shape+color markers (never color alone): enemies red triangle, quest gold X,
    /// NPCs green diamond, shops filled squares, locked gates hollow square, party teal
    /// circles. Maximized shows a legend. Scroll wheel over the map zooms it (the orbit
    /// camera yields while hovered).</summary>
    public class MiniMap : MonoBehaviour
    {
        private const int NormalSide = 210;     // on-screen pixels (pre UI scale)
        private const int Header = 20;          // control strip above the map
        private const float MinRadius = 20f;    // world meters shown from center to edge
        private const float MaxRadius = 220f;   // wide enough to bring any zone objective in view
        private const string SizePref = "minimap.size";

        private float _viewRadius = 42f;

        // 0 = collapsed pill, 1 = normal, 2 = maximized. Static so MapRect can stay a
        // static property (CombatClientUI/OrbitCamera hit-test it without an instance).
        private static int _sizeMode = 1;

        // A maximized map would cover the battlefield and swallow combat clicks
        // (MapRect gates click-to-move), so combat caps it at normal; the stored
        // preference survives and the big map returns after the fight.
        private static int EffectiveMode =>
            _sizeMode == 2 && CombatManager.Instance != null
                && CombatManager.Instance.InCombat.Value ? 1 : _sizeMode;

        private Camera _mapCam;
        private RenderTexture _rt;
        private Texture2D _playerArrow, _questX, _enemyTri, _npcDiamond,
            _vendorSq, _smithSq, _gateSq, _partyDot;

        private struct Marker { public Vector3 Pos; public Texture2D Tex; public float Size; }
        private readonly List<Marker> _markers = new List<Marker>();
        private Transform _player;
        private float _nextScan;

        private static float Side => EffectiveMode == 2
            ? Mathf.Min(Ui.W - 24f, Ui.H - 200f)
            : NormalSide;

        /// <summary>Full interactive frame (header buttons + map) in Ui-scaled space;
        /// this is the rect HUD hit-tests block clicks against.</summary>
        public static Rect MapRect
        {
            get
            {
                if (EffectiveMode == 0) return new Rect(Ui.W - 74 - 12, 118, 74, Header);
                float s = Side;
                return new Rect(Ui.W - s - 12, 118, s, s + Header);
            }
        }

        /// <summary>Just the rendered map area (frame minus the header strip).</summary>
        private static Rect ViewRect
        {
            get
            {
                var r = MapRect;
                return new Rect(r.x, r.y + Header, r.width, r.height - Header);
            }
        }

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
            _sizeMode = Mathf.Clamp(PlayerPrefs.GetInt(SizePref, 1), 0, 2);

            _rt = new RenderTexture(512, 512, 16);
            var go = new GameObject("MiniMapCamera");
            go.transform.SetParent(transform, false);
            _mapCam = go.AddComponent<Camera>();
            _mapCam.orthographic = true;
            _mapCam.orthographicSize = _viewRadius;
            _mapCam.targetTexture = _rt;
            _mapCam.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            _mapCam.backgroundColor = new Color(0.12f, 0.14f, 0.18f);
            _mapCam.clearFlags = CameraClearFlags.SolidColor;

            var outline = new Color(0.08f, 0.06f, 0.05f);
            _playerArrow = MakeArrow(new Color(0.35f, 0.8f, 1f));
            _questX = MakeX(Theme.Gold, outline);
            _enemyTri = MakeTriangle(new Color(0.90f, 0.22f, 0.16f), outline);
            _npcDiamond = MakeDiamond(new Color(0.30f, 0.78f, 0.36f), outline);
            _vendorSq = MakeSquare(new Color(0.72f, 0.48f, 0.95f), outline, hollow: false);
            _smithSq = MakeSquare(new Color(0.86f, 0.54f, 0.22f), outline, hollow: false);
            _gateSq = MakeSquare(Theme.Parchment, outline, hollow: true);
            _partyDot = MakeCircle(new Color(0.25f, 0.85f, 0.78f), outline);
        }

        private static void SetSize(int mode)
        {
            _sizeMode = Mathf.Clamp(mode, 0, 2);
            PlayerPrefs.SetInt(SizePref, _sizeMode);
        }

        private void LateUpdate()
        {
            if (Time.time >= _nextScan)
            {
                _nextScan = Time.time + 0.5f;
                Rescan();
            }

            if (_player == null) { _mapCam.enabled = false; return; }

            // M toggles between normal and maximized (restores if collapsed).
            if (Input.GetKeyDown(KeyCode.M)) SetSize(_sizeMode == 2 ? 1 : 2);

            _mapCam.enabled = EffectiveMode > 0;
            _mapCam.transform.position = _player.position + Vector3.up * 90f;

            float scroll = Input.GetAxis("Mouse ScrollWheel");
            if (Mathf.Abs(scroll) > 0.001f && EffectiveMode > 0 && MouseOverMap)
                _viewRadius = Mathf.Clamp(
                    _viewRadius * (scroll > 0f ? 1f / 1.25f : 1.25f),
                    MinRadius, MaxRadius);
            _mapCam.orthographicSize = _viewRadius;
        }

        /// <summary>Refreshes the local player handle and the marker list (0.5 s cadence
        /// — FindObjectsByType is too heavy for every frame).</summary>
        private void Rescan()
        {
            var holder = FindObjectsByType<PlayerCharacterHolder>(FindObjectsSortMode.None)
                .FirstOrDefault(p => p.IsOwner);
            _player = holder != null ? holder.transform : null;

            _markers.Clear();
            if (_player == null) return;
            var director = GameDirector.Instance;

            foreach (var t in FindObjectsByType<EncounterTrigger>(FindObjectsSortMode.None))
            {
                if (t.Consumed || t.MonsterIds.Length == 0) continue;
                if (director != null && director.ConsumedEncounterIds.Contains(t.EncounterId))
                    continue;
                _markers.Add(new Marker { Pos = t.transform.position, Tex = _enemyTri, Size = 13 });
            }
            foreach (var n in FindObjectsByType<NpcInteract>(FindObjectsSortMode.None))
                _markers.Add(new Marker { Pos = n.transform.position, Tex = _npcDiamond, Size = 12 });
            foreach (var v in FindObjectsByType<VendorInteract>(FindObjectsSortMode.None))
                _markers.Add(new Marker { Pos = v.transform.position, Tex = _vendorSq, Size = 11 });
            foreach (var s in FindObjectsByType<SmithInteract>(FindObjectsSortMode.None))
                _markers.Add(new Marker { Pos = s.transform.position, Tex = _smithSq, Size = 11 });
            foreach (var g in FindObjectsByType<ZoneGate>(FindObjectsSortMode.None))
            {
                if (director == null
                    || director.GetZoneState(g.ZoneIndex) != QuestState.Locked) continue;
                _markers.Add(new Marker { Pos = g.transform.position, Tex = _gateSq, Size = 12 });
            }
            foreach (var p in FindObjectsByType<PlayerCharacterHolder>(FindObjectsSortMode.None))
            {
                if (p.IsOwner) continue;
                _markers.Add(new Marker { Pos = p.transform.position, Tex = _partyDot, Size = 10 });
            }
            foreach (var c in FindObjectsByType<CompanionFollower>(FindObjectsSortMode.None))
                _markers.Add(new Marker { Pos = c.transform.position, Tex = _partyDot, Size = 8 });
        }

        private void OnGUI()
        {
            Ui.Begin();
            if (_player == null || _rt == null) return;

            if (EffectiveMode == 0)
            {
                var pill = MapRect;
                if (GUI.Button(pill, "MAP +")) SetSize(1);
                return;
            }

            var frame = MapRect;
            var view = ViewRect;
            GUI.Box(new Rect(frame.x - 3, frame.y - 3, frame.width + 6, frame.height + 6),
                GUIContent.none);

            // Header strip: title + shrink/grow controls.
            GUI.Label(new Rect(frame.x + 6, frame.y + 2, 80, 16), "MAP", Theme.Caps);
            if (GUI.Button(new Rect(frame.xMax - 46, frame.y, 22, Header - 2), "-"))
                SetSize(_sizeMode - 1);
            GUI.enabled = _sizeMode < 2;
            if (GUI.Button(new Rect(frame.xMax - 23, frame.y, 22, Header - 2), "+"))
                SetSize(_sizeMode + 1);
            GUI.enabled = true;

            GUI.DrawTexture(view, _rt, ScaleMode.StretchToFill);

            // North indicator (map is north-up): dark shadow pass, then parchment.
            var nRect = new Rect(view.x + view.width / 2f - 6, view.y + 2, 14, 14);
            var prevColor = GUI.color;
            GUI.color = Color.black;
            GUI.Label(new Rect(nRect.x + 1, nRect.y + 1, nRect.width, nRect.height), "N", Theme.Caps);
            GUI.color = prevColor;
            GUI.Label(nRect, "<color=#e8dfd4>N</color>", Theme.Caps);

            float half = view.width / 2f;

            // World-object markers: drawn only while inside the view radius.
            foreach (var mk in _markers)
            {
                var d = ToMap(mk.Pos, half);
                if (d.magnitude > half - 10f) continue;
                GUI.DrawTexture(new Rect(view.x + half + d.x - mk.Size / 2f,
                    view.y + half + d.y - mk.Size / 2f, mk.Size, mk.Size), mk.Tex);
            }

            // Quest objective: a pulsing gold X, edge-clamped when out of view.
            var tracker = QuestTracker.Instance;
            if (tracker != null && tracker.HasTarget)
            {
                var d = Vector2.ClampMagnitude(ToMap(tracker.TargetPosition, half), half - 12f);
                float xs = 22f + Mathf.Sin(Time.time * 4f) * 3f;
                GUI.DrawTexture(new Rect(view.x + half + d.x - xs / 2f,
                    view.y + half + d.y - xs / 2f, xs, xs), _questX);
            }

            // Player arrow, rotated to face heading (map stays north-up).
            var center = new Vector2(view.x + half, view.y + half);
            var prev = GUI.matrix;
            GUIUtility.RotateAroundPivot(_player.eulerAngles.y, center);
            GUI.DrawTexture(new Rect(center.x - 9, center.y - 9, 18, 18), _playerArrow);
            GUI.matrix = prev;

            if (EffectiveMode == 2) DrawLegend(view);
        }

        /// <summary>World offset from the player → map pixels (north-up).</summary>
        private Vector2 ToMap(Vector3 world, float half)
        {
            Vector3 delta = world - _player.position;
            return new Vector2(delta.x, -delta.z) * (half / _viewRadius);
        }

        private void DrawLegend(Rect view)
        {
            (Texture2D tex, string label)[] rows =
            {
                (_playerArrow, "You"), (_partyDot, "Party"), (_enemyTri, "Enemy"),
                (_questX, "Quest"), (_npcDiamond, "NPC"), (_vendorSq, "Vendor"),
                (_smithSq, "Smithy"), (_gateSq, "Locked gate"),
            };
            const float rowH = 16f;
            var panel = new Rect(view.x + 6, view.yMax - rows.Length * rowH - 16,
                104, rows.Length * rowH + 10);
            GUI.Box(panel, GUIContent.none);
            for (int i = 0; i < rows.Length; i++)
            {
                float y = panel.y + 5 + i * rowH;
                GUI.DrawTexture(new Rect(panel.x + 6, y + 2, 11, 11), rows[i].tex);
                GUI.Label(new Rect(panel.x + 22, y, panel.width - 24, rowH),
                    rows[i].label, Theme.Caps);
            }
        }

        // ---------- marker textures (procedural, dark-outlined so they read on any
        // terrain color; shape + color together distinguish the categories) ----------

        private static Texture2D NewTex(int s) =>
            new Texture2D(s, s, TextureFormat.RGBA32, false)
                { hideFlags = HideFlags.HideAndDontSave };

        /// <summary>Fills a texture from a signed-distance-ish function: d &lt; 0 is
        /// inside (fill color), 0..outlinePx is the outline band, beyond is clear.</summary>
        private static Texture2D FromField(int s, System.Func<float, float, float> field,
            Color fill, Color outline, float outlinePx = 2f)
        {
            var tex = NewTex(s);
            for (int y = 0; y < s; y++)
                for (int x = 0; x < s; x++)
                {
                    float d = field(x, y);
                    Color c = d < 0f ? fill : d < outlinePx ? outline : Color.clear;
                    tex.SetPixel(x, y, c);
                }
            tex.Apply();
            return tex;
        }

        private static Texture2D MakeArrow(Color color)
        {
            const int s = 32;
            var tex = NewTex(s);
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

        /// <summary>Bold X — the quest objective.</summary>
        private static Texture2D MakeX(Color color, Color outline)
        {
            const int s = 28;
            return FromField(s, (x, y) =>
                Mathf.Min(Mathf.Abs(x - y), Mathf.Abs(x + y - (s - 1))) - 3f,
                color, outline);
        }

        /// <summary>Up-pointing warning triangle — danger/encounters.</summary>
        private static Texture2D MakeTriangle(Color color, Color outline)
        {
            const int s = 24;
            const float apexY = s - 4f, baseY = 3f;         // texture rows run bottom-up
            return FromField(s, (x, y) =>
            {
                float t = Mathf.InverseLerp(apexY, baseY, y);   // 0 at apex → 1 at base
                float d = Mathf.Abs(x - (s - 1) / 2f) - t * (s * 0.42f);
                return Mathf.Max(d, Mathf.Max(baseY - y, y - apexY));
            }, color, outline);
        }

        private static Texture2D MakeDiamond(Color color, Color outline)
        {
            const int s = 24;
            float c = (s - 1) / 2f;
            return FromField(s, (x, y) =>
                Mathf.Abs(x - c) + Mathf.Abs(y - c) - (c - 2f), color, outline);
        }

        private static Texture2D MakeSquare(Color color, Color outline, bool hollow)
        {
            const int s = 24;
            float c = (s - 1) / 2f;
            return FromField(s, (x, y) =>
            {
                float d = Mathf.Max(Mathf.Abs(x - c), Mathf.Abs(y - c)) - (c - 2.5f);
                return hollow ? Mathf.Max(d, -3.5f - d) : d;   // ring for gates
            }, color, outline);
        }

        private static Texture2D MakeCircle(Color color, Color outline)
        {
            const int s = 24;
            float c = (s - 1) / 2f;
            return FromField(s, (x, y) =>
                Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c)) - (c - 2.5f),
                color, outline);
        }
    }
}
