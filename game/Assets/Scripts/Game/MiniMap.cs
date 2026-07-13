using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace RadiantPool.Game
{
    /// <summary>Minimap, docked in the TOP-RIGHT CORNER itself. An orthographic camera renders
    /// the world straight down into a RenderTexture. North-fixed. It starts COLLAPSED — the
    /// city is what the screen is for, and the map is one keypress (M) or one icon away when
    /// it is wanted. Three sizes — collapsed pill / normal /
    /// maximized (header icons or M key), remembered in PlayerPrefs. Drag inside the
    /// map to pan away from the player and go looking for the objective; the RECENTER
    /// button (or releasing after a pan idles out) snaps back. World objects get distinct
    /// shape+color markers (never color alone): enemies red triangle, quest gold X, NPCs
    /// green diamond, shops filled squares, locked gates hollow square, party teal
    /// circles. The quest X is edge-clamped with its distance, so it is always on screen
    /// even when the objective is far outside the view. Maximized shows a legend. Scroll
    /// over the map zooms it (the orbit camera yields while hovered).</summary>
    public class MiniMap : MonoBehaviour
    {
        private const int NormalSide = 210;     // on-screen pixels (pre UI scale)
        private const int Header = 26;          // control strip above the map
        private const int BtnW = 28, BtnH = 22; // icon buttons — readable, easy to hit
        private const float MinRadius = 20f;    // world meters shown from center to edge
        private const float MaxRadius = 220f;   // wide enough to bring any zone objective in view
        private const float MaxPan = 500f;      // metres the view may stray from the player
        // Renamed key, so the new default (collapsed) actually reaches players who already
        // have the old one stored — a preference nobody set is not worth honouring forever.
        private const string SizePref = "minimap.size2";

        private float _viewRadius = 42f;

        /// <summary>World-space XZ offset of the map view from the player (drag to pan).</summary>
        private Vector2 _pan;
        private bool _panning;

        // 0 = collapsed pill, 1 = normal, 2 = maximized. Static so MapRect can stay a
        // static property (CombatClientUI/OrbitCamera hit-test it without an instance).
        private static int _sizeMode;   // starts collapsed: M (or the icon) brings it back

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
        private Texture2D _icoExpand, _icoShrink, _icoMinimize, _icoRestore;

        private GUIStyle _tagStyle, _tipStyle;

        private struct Marker { public Vector3 Pos; public Texture2D Tex; public float Size; }
        private readonly List<Marker> _markers = new List<Marker>();

        /// <summary>Quarter names painted on the map (centre of each zone's fights), so
        /// "retake the Old Docks" has somewhere to point to. Active quest zone is gold.</summary>
        private struct Place { public Vector3 Pos; public string Name; public bool Active; }
        private readonly List<Place> _places = new List<Place>();
        private GUIStyle _placeStyle;
        private Transform _player;
        private float _nextScan;

        /// <summary>Map size in logical units. The normal map gives ground back on a short
        /// canvas (a big UI scale can shrink the logical height below the design 630) so it
        /// never crowds the initiative list docked underneath it.</summary>
        private static float Side => EffectiveMode == 2
            ? Mathf.Min(Ui.W - 24f, Ui.H - 200f)
            : Mathf.Clamp(Ui.H * 0.33f, 150f, NormalSide);

        /// <summary>Full interactive frame (header buttons + map) in Ui-scaled space;
        /// this is the rect HUD hit-tests block clicks against.</summary>
        /// <summary>The corner itself: the hosting strip that used to hold the top of the HUD
        /// is now a hotbar icon (SessionPanel), so nothing sits above the map any more.</summary>
        private const float MapTop = 12f;

        public static Rect MapRect
        {
            get
            {
                if (EffectiveMode == 0) return new Rect(Ui.W - 74 - 12, MapTop, 74, Header);
                float s = Side;
                return new Rect(Ui.W - s - 12, MapTop, s, s + Header);
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
        public static bool MouseOverMap => MapRect.Contains(Ui.Mouse);

        private void Start()
        {
            _sizeMode = Mathf.Clamp(PlayerPrefs.GetInt(SizePref, 0), 0, 2);   // hidden until asked for

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

            _icoExpand = MakeCornerArrows(outward: true);    // grow / fullscreen
            _icoShrink = MakeCornerArrows(outward: false);   // maximized → normal
            _icoMinimize = MakeMinimizeIcon();               // normal → corner pill
            _icoRestore = _icoExpand;                        // pill → normal
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

            // M cycles hidden -> normal -> maximized -> hidden. It has to come back round to
            // hidden: the map now STARTS that way, and a key that can only ever grow it would
            // be a one-way door out of the clear view the player asked for.
            if (Input.GetKeyDown(KeyCode.M) && !Ui.Typing)
                SetSize(_sizeMode == 0 ? 1 : _sizeMode == 1 ? 2 : 0);

            bool inCombat = CombatManager.Instance != null
                            && CombatManager.Instance.InCombat.Value;
            if (inCombat) Recenter();   // a fight is never the time to be looking elsewhere

            _mapCam.enabled = EffectiveMode > 0;
            _mapCam.transform.position = MapCenter + Vector3.up * 90f;

            float scroll = Input.GetAxis("Mouse ScrollWheel");
            if (Mathf.Abs(scroll) > 0.001f && EffectiveMode > 0 && MouseOverMap)
                _viewRadius = Mathf.Clamp(
                    _viewRadius * (scroll > 0f ? 1f / 1.25f : 1.25f),
                    MinRadius, MaxRadius);
            _mapCam.orthographicSize = _viewRadius;
        }

        /// <summary>World point the map is centred on: the player, plus any drag pan.</summary>
        private Vector3 MapCenter => _player.position + new Vector3(_pan.x, 0f, _pan.y);

        private void Recenter() { _pan = Vector2.zero; _panning = false; }

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

            // Quarter names, positioned at the centre of each zone's encounters.
            _places.Clear();
            if (director == null) return;
            var triggers = FindObjectsByType<EncounterTrigger>(FindObjectsSortMode.None);
            for (int i = 0; i < director.Zones.Length; i++)
            {
                var cfg = director.Zones[i];
                var inZone = triggers.Where(t => t.ZoneId == cfg.ZoneId).ToList();
                if (inZone.Count == 0) continue;
                Vector3 sum = Vector3.zero;
                foreach (var t in inZone) sum += t.transform.position;
                _places.Add(new Place
                {
                    Pos = sum / inZone.Count,
                    Name = cfg.DisplayName,
                    Active = director.GetZoneState(i) == QuestState.Active
                });
            }
        }

        private void OnGUI()
        {
            Ui.Begin();
            if (_player == null || _rt == null) return;

            if (EffectiveMode == 0)
            {
                // Collapsed: a labelled pill — icon alone would be a guessing game.
                var pill = MapRect;
                if (IconButton(pill, _icoRestore, "Show map (M)", "MAP")) SetSize(1);
                return;
            }

            var frame = MapRect;
            var view = ViewRect;
            GUI.Box(new Rect(frame.x - 3, frame.y - 3, frame.width + 6, frame.height + 6),
                GUIContent.none);

            // Header strip: title, recenter (only once panned), then the two size icons.
            GUI.Label(new Rect(frame.x + 6, frame.y + 5, 84, 16),
                _pan == Vector2.zero ? "MAP" : "MAP (PANNED)", Theme.Caps);
            if (_pan != Vector2.zero
                && GUI.Button(new Rect(frame.xMax - 130, frame.y + 2, 62, BtnH), "RECENTER"))
                Recenter();

            // Left icon shrinks one step: maximized → normal (arrows in), normal →
            // corner pill (minimize bar). Right icon grows; disabled once maximized.
            var shrinkRect = new Rect(frame.xMax - BtnW * 2 - 6, frame.y + 2, BtnW, BtnH);
            var growRect = new Rect(frame.xMax - BtnW - 2, frame.y + 2, BtnW, BtnH);

            bool atMax = _sizeMode >= 2;
            if (IconButton(shrinkRect, atMax ? _icoShrink : _icoMinimize,
                    atMax ? "Shrink map" : "Collapse to corner"))
                SetSize(_sizeMode - 1);

            GUI.enabled = !atMax;
            if (IconButton(growRect, _icoExpand, atMax ? "Already maximized" : "Expand map (M)"))
                SetSize(_sizeMode + 1);
            GUI.enabled = true;

            GUI.DrawTexture(view, _rt, ScaleMode.StretchToFill);

            float half = view.width / 2f;
            HandlePanDrag(view, half);

            // North indicator (map is north-up): dark shadow pass, then parchment.
            var nRect = new Rect(view.x + half - 6, view.y + 2, 14, 14);
            var prevColor = GUI.color;
            GUI.color = Color.black;
            GUI.Label(new Rect(nRect.x + 1, nRect.y + 1, nRect.width, nRect.height), "N", Theme.Caps);
            GUI.color = prevColor;
            GUI.Label(nRect, "<color=#e8dfd4>N</color>", Theme.Caps);

            // Quarter names first (under the icons): the district the quest names is
            // written on the map, in gold while it is the active objective.
            if (_placeStyle == null)
                _placeStyle = new GUIStyle(Theme.Caps)
                    { alignment = TextAnchor.MiddleCenter, wordWrap = false };
            foreach (var place in _places)
            {
                var d = ToMap(place.Pos, half);
                if (Mathf.Abs(d.x) > half - 4f || Mathf.Abs(d.y) > half - 8f) continue;
                var at = new Rect(view.x + half + d.x - 70f, view.y + half + d.y - 7f, 140f, 14f);
                _placeStyle.normal.textColor = Color.black;      // shadow for legibility
                GUI.Label(new Rect(at.x + 1, at.y + 1, at.width, at.height),
                    place.Name.ToUpperInvariant(), _placeStyle);
                _placeStyle.normal.textColor = place.Active ? Theme.Gold : Theme.OnSurfaceMuted;
                GUI.Label(at, place.Name.ToUpperInvariant(), _placeStyle);
            }

            // World-object markers: drawn only while inside the view radius.
            foreach (var mk in _markers)
            {
                var d = ToMap(mk.Pos, half);
                if (d.magnitude > half - 10f) continue;
                GUI.DrawTexture(new Rect(view.x + half + d.x - mk.Size / 2f,
                    view.y + half + d.y - mk.Size / 2f, mk.Size, mk.Size), mk.Tex);
            }

            // Player arrow, rotated to face heading (map stays north-up). Sits at the
            // centre until the view is panned away from them.
            var pd = Vector2.ClampMagnitude(ToMap(_player.position, half), half - 9f);
            var me = new Vector2(view.x + half + pd.x, view.y + half + pd.y);
            var prev = GUI.matrix;
            GUIUtility.RotateAroundPivot(_player.eulerAngles.y, me);
            GUI.DrawTexture(new Rect(me.x - 9, me.y - 9, 18, 18), _playerArrow);
            GUI.matrix = prev;

            // Quest objective LAST so nothing can hide it: a pulsing gold X, clamped to
            // the map edge with its distance when the objective is outside the view.
            var tracker = QuestTracker.Instance;
            if (tracker != null && tracker.HasTarget)
            {
                var raw = ToMap(tracker.TargetPosition, half);
                var d = Vector2.ClampMagnitude(raw, half - 14f);
                bool offMap = raw != d;
                float xs = 24f + Mathf.Sin(Time.time * 4f) * 3f;
                var at = new Vector2(view.x + half + d.x, view.y + half + d.y);
                GUI.DrawTexture(new Rect(at.x - xs / 2f, at.y - xs / 2f, xs, xs), _questX);

                if (_tagStyle == null)
                    _tagStyle = new GUIStyle(Theme.Caps)
                        { alignment = TextAnchor.MiddleCenter, wordWrap = false };
                _tagStyle.normal.textColor = offMap ? Theme.Gold : Theme.OnSurface;
                float dist = Vector3.Distance(tracker.TargetPosition, _player.position);
                GUI.Label(new Rect(at.x - 40, at.y + xs / 2f - 2, 80, 14),
                    $"{dist:0} m", _tagStyle);
            }

            if (EffectiveMode == 2) DrawLegend(view);
            DrawTooltip(frame);   // last, so map markers can never cover the hint
        }

        /// <summary>A themed button whose face is a generated icon (fonts have no glyphs
        /// for these, and a bare "+"/"-" was unreadable at HUD size). The icon is drawn
        /// over the button so it keeps the stone/gold press states; `tip` shows on hover.
        /// Optional `text` labels the button for cases where the icon alone is ambiguous.</summary>
        private bool IconButton(Rect r, Texture2D icon, string tip, string text = null)
        {
            bool clicked = GUI.Button(r, new GUIContent(text ?? "", tip));

            float ico = Mathf.Min(16f, r.height - 6f);
            float x = text == null ? r.center.x - ico / 2f : r.xMax - ico - 8f;
            var prevColor = GUI.color;
            // Match the skin's text colours: parchment normally, gold on hover.
            if (!GUI.enabled) GUI.color = new Color(1f, 1f, 1f, 0.4f);
            else if (r.Contains(Event.current.mousePosition)) GUI.color = Theme.Gold;
            else GUI.color = Theme.OnSurface;
            GUI.DrawTexture(new Rect(x, r.center.y - ico / 2f, ico, ico), icon);
            GUI.color = prevColor;

            return clicked;
        }

        /// <summary>Hover hint for the icon buttons, drawn just under the header so it
        /// never runs off the right edge of the screen.</summary>
        private void DrawTooltip(Rect frame)
        {
            if (string.IsNullOrEmpty(GUI.tooltip)) return;
            if (_tipStyle == null)
                _tipStyle = new GUIStyle(Theme.Caps)
                    { alignment = TextAnchor.MiddleCenter, wordWrap = false };

            var size = _tipStyle.CalcSize(new GUIContent(GUI.tooltip));
            float w = size.x + 16f, h = 18f;
            var r = new Rect(Mathf.Max(4f, frame.xMax - w), frame.y + Header + 4f, w, h);
            GUI.Box(r, GUIContent.none);
            GUI.Label(r, GUI.tooltip, _tipStyle);
        }

        /// <summary>Left-drag inside the map slides the view over the world (grab-the-map
        /// metaphor) so the objective can be hunted down without walking there. The orbit
        /// camera only rotates on right-drag, so the two never fight.</summary>
        private void HandlePanDrag(Rect view, float half)
        {
            var e = Event.current;
            float pxPerMetre = half / _viewRadius;
            switch (e.type)
            {
                case EventType.MouseDown when e.button == 0 && view.Contains(e.mousePosition):
                    _panning = true;
                    e.Use();
                    break;
                case EventType.MouseDrag when _panning && e.button == 0:
                    _pan = Vector2.ClampMagnitude(
                        _pan + new Vector2(-e.delta.x, e.delta.y) / pxPerMetre, MaxPan);
                    e.Use();
                    break;
                case EventType.MouseUp when _panning:
                    _panning = false;
                    e.Use();
                    break;
            }
        }

        /// <summary>World position → map pixels, relative to the (possibly panned) map
        /// centre. North-up: +x east, −y north.</summary>
        private Vector2 ToMap(Vector3 world, float half)
        {
            Vector3 delta = world - MapCenter;
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

        // ---------- header icons (white, tinted by GUI.color at draw time) ----------

        /// <summary>Fullscreen-style corner brackets. Outward = the four brackets hug the
        /// outer corners ("expand"); inward = their corners sit in the middle with arms
        /// reaching out ("shrink"). Reads at 16 px where a "+"/"-" glyph does not.</summary>
        private static Texture2D MakeCornerArrows(bool outward)
        {
            const int s = 32, th = 3, near = 3, far = 13;
            var tex = NewTex(s);
            Fill(tex, Color.clear);

            // One corner, then mirrored into the other three.
            for (int mx = 0; mx < 2; mx++)
                for (int my = 0; my < 2; my++)
                {
                    int cx = outward ? near : far;       // bracket corner
                    int cy = outward ? near : far;
                    int ex = outward ? far : near;       // arm ends
                    int ey = outward ? far : near;
                    Bar(tex, s, mx, my, Mathf.Min(cx, ex), cy, Mathf.Max(cx, ex), cy, th);
                    Bar(tex, s, mx, my, cx, Mathf.Min(cy, ey), cx, Mathf.Max(cy, ey), th);
                }
            tex.Apply();
            return tex;
        }

        /// <summary>Window-minimize bar: "put the map away into the corner".</summary>
        private static Texture2D MakeMinimizeIcon()
        {
            const int s = 32;
            var tex = NewTex(s);
            Fill(tex, Color.clear);
            Bar(tex, s, 0, 0, 7, 20, 24, 20, 4);        // the bar
            Bar(tex, s, 0, 0, 7, 9, 24, 9, 2);          // hint of the shrunken window
            Bar(tex, s, 0, 0, 7, 9, 7, 14, 2);
            Bar(tex, s, 0, 0, 24, 9, 24, 14, 2);
            tex.Apply();
            return tex;
        }

        /// <summary>Axis-aligned thick bar, optionally mirrored into another quadrant.</summary>
        private static void Bar(Texture2D tex, int s, int mirrorX, int mirrorY,
            int x0, int y0, int x1, int y1, int thickness)
        {
            int h = thickness / 2;
            for (int y = Mathf.Min(y0, y1) - h; y <= Mathf.Max(y0, y1) + h; y++)
                for (int x = Mathf.Min(x0, x1) - h; x <= Mathf.Max(x0, x1) + h; x++)
                {
                    if (x < 0 || y < 0 || x >= s || y >= s) continue;
                    int px = mirrorX == 0 ? x : s - 1 - x;
                    int py = mirrorY == 0 ? y : s - 1 - y;
                    tex.SetPixel(px, py, Color.white);
                }
        }

        private static void Fill(Texture2D tex, Color c)
        {
            var pixels = new Color[tex.width * tex.height];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = c;
            tex.SetPixels(pixels);
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
