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
        private Texture2D _atlasTexture, _atlasLocked, _atlasOpen, _atlasDone;

        private GUIStyle _tagStyle, _tipStyle, _atlasRegionStyle, _atlasPlaceStyle,
            _atlasOceanStyle, _atlasDetailStyle;

        private struct Marker { public Vector3 Pos; public Texture2D Tex; public float Size; }
        private readonly List<Marker> _markers = new List<Marker>();

        /// <summary>Quarter names painted on the map (centre of each zone's fights), so
        /// "retake the Old Docks" has somewhere to point to. Active quest zone is gold.</summary>
        private struct Place { public Vector3 Pos; public string Name; public bool Active; }
        private readonly List<Place> _places = new List<Place>();
        private GUIStyle _placeStyle;
        private Transform _player;
        private float _nextScan;
        private string _atlasPlayerPlace = "council_hall";

        /// <summary>The maximized view is a campaign atlas rather than a magnified tactical
        /// camera. Every playable zone belongs to one authored parent region, and its pin
        /// sits inside that region's coastline. Positions are normalized atlas coordinates
        /// (top-left origin), deliberately independent of the bootstrap's remote-cell grid.</summary>
        private sealed class AtlasPlace
        {
            public string ZoneId;
            public string Label;
            public Vector2 Position;
            public Vector2 LabelOffset;
        }

        private sealed class AtlasRegion
        {
            public string Name;
            public Vector2 LabelPosition;
            public Color LandColor;
            public Vector2[] Coast;
            public AtlasPlace[] Places;
        }

        private static AtlasPlace Ap(string id, string label, float x, float y,
            float labelX = 0f, float labelY = 9f) => new AtlasPlace
        {
            ZoneId = id, Label = label, Position = new Vector2(x, y),
            LabelOffset = new Vector2(labelX, labelY)
        };

        private static AtlasRegion Ar(string name, float labelX, float labelY, Color land,
            Vector2[] coast, params AtlasPlace[] places) => new AtlasRegion
        {
            Name = name, LabelPosition = new Vector2(labelX, labelY), LandColor = land,
            Coast = coast, Places = places
        };

        // Six readable landmasses mirror the actual campaign hierarchy. The nested
        // Observatory floors remain one region; the five Aldenmere districts surround
        // Council Hall; and the final four commissions form one eastward ascent.
        private static readonly AtlasRegion[] AtlasRegions =
        {
            Ar("CINDERWELL COAST", .19f, .105f, new Color(.58f, .38f, .23f), new[]
            {
                new Vector2(.035f,.13f), new Vector2(.11f,.055f), new Vector2(.27f,.07f),
                new Vector2(.35f,.17f), new Vector2(.315f,.33f), new Vector2(.23f,.405f),
                new Vector2(.085f,.36f), new Vector2(.025f,.245f)
            },
                Ap("drowned_bastion", "Drowned Bastion", .085f, .205f, 10f, -13f),
                Ap("cinderwell_yard", "Cinderwell Yard", .17f, .17f, 0f, -13f),
                Ap("cinderwell_undercroft", "Undercroft", .255f, .175f, 0f, -13f),
                Ap("ember_archive", "Ember Archive", .295f, .245f, 4f, 9f),
                Ap("loomhouse_enclave", "Loomhouse", .105f, .29f, 0f, 9f),
                Ap("blackbriar_manor", "Blackbriar", .19f, .315f, 0f, 9f),
                Ap("gilded_quarter", "Gilded Quarter", .275f, .325f, 5f, 9f)),

            Ar("ALDENMERE", .445f, .325f, new Color(.24f, .49f, .32f), new[]
            {
                new Vector2(.285f,.405f), new Vector2(.37f,.29f), new Vector2(.51f,.285f),
                new Vector2(.60f,.405f), new Vector2(.565f,.545f), new Vector2(.46f,.62f),
                new Vector2(.345f,.585f), new Vector2(.285f,.50f)
            },
                Ap("drowned_market", "Drowned Market", .425f, .405f, 0f, -13f),
                Ap("ashen_ward", "Ashen Ward", .525f, .395f, 0f, -13f),
                Ap("old_docks", "Old Docks", .34f, .49f, -2f, 9f),
                Ap("council_hall", "Council Hall", .435f, .49f, 0f, 9f),
                Ap("glasslit_temple", "Glasslit Temple", .535f, .49f, 0f, 9f),
                Ap("sunken_warcamp", "Sunken Warcamp", .435f, .565f, 0f, 9f)),

            Ar("EMBERWILD", .18f, .565f, new Color(.43f, .54f, .21f), new[]
            {
                new Vector2(.035f,.59f), new Vector2(.125f,.47f), new Vector2(.265f,.485f),
                new Vector2(.36f,.625f), new Vector2(.315f,.83f), new Vector2(.17f,.925f),
                new Vector2(.055f,.82f)
            },
                Ap("emberwild_expanse", "Emberwild Expanse", .095f, .69f, 10f, -13f),
                Ap("wild_lairs", "Wilder Dens", .17f, .78f, 0f, 9f),
                Ap("reedwind_encampment", "Reedwind Camp", .245f, .675f, 0f, -13f),
                Ap("goblin_delves", "Gloam Delves", .285f, .795f, 0f, 9f)),

            Ar("DROWNED OBSERVATORY", .525f, .075f, new Color(.38f, .53f, .61f), new[]
            {
                new Vector2(.42f,.13f), new Vector2(.475f,.035f), new Vector2(.575f,.045f),
                new Vector2(.625f,.13f), new Vector2(.595f,.255f), new Vector2(.515f,.305f),
                new Vector2(.435f,.245f)
            },
                Ap("drowned_observatory_approach", "Approach", .455f, .19f, -5f, 9f),
                Ap("drowned_observatory_underworks", "Underworks", .525f, .235f, 0f, 9f),
                Ap("drowned_observatory_crown", "Crown", .585f, .16f, 0f, 9f)),

            Ar("MIREWATCH REACH", .525f, .64f, new Color(.22f, .45f, .43f), new[]
            {
                new Vector2(.375f,.68f), new Vector2(.445f,.565f), new Vector2(.585f,.56f),
                new Vector2(.685f,.68f), new Vector2(.65f,.86f), new Vector2(.52f,.94f),
                new Vector2(.405f,.855f)
            },
                Ap("mirewatch_citadel", "Mirewatch", .425f, .76f, 2f, -13f),
                Ap("tidebreaker_anchorage", "Tidebreaker", .50f, .695f, 0f, -13f),
                Ap("iron_concord_redoubt", "Iron Concord", .59f, .75f, 0f, 9f),
                Ap("lanternfall_necropolis", "Lanternfall", .535f, .855f, 0f, 9f)),

            Ar("EMBER CROWN", .81f, .205f, new Color(.50f, .22f, .25f), new[]
            {
                new Vector2(.66f,.19f), new Vector2(.76f,.095f), new Vector2(.90f,.135f),
                new Vector2(.965f,.31f), new Vector2(.91f,.49f), new Vector2(.95f,.69f),
                new Vector2(.84f,.91f), new Vector2(.70f,.835f), new Vector2(.635f,.64f),
                new Vector2(.69f,.43f)
            },
                Ap("cinder_gate", "Cinder Gate", .72f, .52f, 0f, 9f),
                Ap("crownless_citadel", "Crownless Citadel", .79f, .405f, 0f, -13f),
                Ap("thornmaze", "Thornmaze", .865f, .56f, 0f, 9f),
                Ap("ember_crown_spire", "Crown Spire", .865f, .30f, 0f, -13f))
        };

        /// <summary>Map size in logical units. The normal map gives ground back on a short
        /// canvas (a big UI scale can shrink the logical height below the design 630) so it
        /// never crowds the initiative list docked underneath it.</summary>
        private static float Side => Mathf.Clamp(Ui.H * 0.33f, 150f, NormalSide);

        /// <summary>Full interactive frame (header buttons + map) in Ui-scaled space;
        /// this is the rect HUD hit-tests block clicks against.</summary>
        /// <summary>The corner itself: the hosting strip that used to hold the top of the HUD
        /// is now a hotbar icon (SessionPanel), so nothing sits above the map any more.</summary>
        private const float MapTop = 12f;

        public static Rect MapRect
        {
            get
            {
                if (EffectiveMode == 0) return new Rect(Ui.W - 126 - 12, MapTop, 126, Header);
                if (EffectiveMode == 2)
                {
                    float w = Mathf.Min(820f, Ui.W - 24f);
                    float h = Mathf.Min(500f, Ui.H - 150f);
                    return new Rect(Ui.W - w - 12f, MapTop, w, h + Header);
                }
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
            _atlasLocked = MakeCircle(new Color(.30f, .31f, .30f), outline);
            _atlasOpen = MakeCircle(Theme.Parchment, outline);
            _atlasDone = MakeCircle(new Color(.25f, .72f, .62f), outline);
            if (SystemInfo.graphicsDeviceType != UnityEngine.Rendering.GraphicsDeviceType.Null)
                _atlasTexture = MakeAtlasTexture();

            _icoExpand = MakeCornerArrows(outward: true);    // grow / fullscreen
            _icoShrink = MakeCornerArrows(outward: false);   // maximized → normal
            _icoMinimize = MakeMinimizeIcon();               // normal → corner pill
            _icoRestore = _icoExpand;                        // pill → normal
            ValidateAtlas();
        }

        /// <summary>Shared public entry for screenshot QA. It exercises the same state
        /// transition as the M/header controls without sending input to the desktop.</summary>
        public void ShowCampaignAtlasForTest()
        {
            Recenter();
            _sizeMode = 2; // capture must not overwrite the player's remembered preference
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
            if (Input.GetKeyDown(KeyCode.M) && !Ui.Typing && !Ui.PanelOpen)
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

            // The atlas shows a separate YOU ARE HERE pin. Remote destinations have a
            // 45 m arrival footprint; otherwise the player is at the Council Hall hub.
            _atlasPlayerPlace = "council_hall";
            float closestDestination = 45f * 45f;
            foreach (var destination in FindObjectsByType<CampaignDestination>(FindObjectsSortMode.None))
            {
                float sqr = (destination.transform.position - _player.position).sqrMagnitude;
                if (sqr >= closestDestination) continue;
                if (destination.ZoneIndex < 0)
                {
                    closestDestination = sqr;
                    _atlasPlayerPlace = "council_hall";
                    continue;
                }
                if (director == null || destination.ZoneIndex >= director.Zones.Length) continue;
                closestDestination = sqr;
                _atlasPlayerPlace = director.Zones[destination.ZoneIndex].ZoneId;
            }

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
            // A screen is up (bags, journal, settings): it owns the display. MapRect keeps
            // reporting where the map WOULD be, so the initiative list still docks off it.
            if (Ui.PanelOpen) return;
            if (_player == null || _rt == null) return;

            if (EffectiveMode == 0)
            {
                // Collapsed: a labelled pill — icon alone would be a guessing game.
                var pill = MapRect;
                string label = $"{WorldAtmosphere.ClockLabel}  MAP";
                if (IconButton(pill, _icoRestore, "Show map (M)", label)) SetSize(1);
                return;
            }

            var frame = MapRect;
            var view = ViewRect;
            GUI.Box(new Rect(frame.x - 3, frame.y - 3, frame.width + 6, frame.height + 6),
                GUIContent.none);

            // Header strip: title, recenter (only once panned), then the two size icons.
            string mapTitle = EffectiveMode == 2 ? "CAMPAIGN ATLAS"
                : _pan == Vector2.zero ? "MAP" : "MAP (PANNED)";
            GUI.Label(new Rect(frame.x + 6, frame.y + 5, 128, 16),
                $"{mapTitle}  {WorldAtmosphere.ClockLabel}", Theme.Caps);
            if (EffectiveMode != 2 && _pan != Vector2.zero
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

            if (EffectiveMode == 2)
            {
                DrawCampaignAtlas(view);
                DrawTooltip(frame);
                return;
            }

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

        private void DrawCampaignAtlas(Rect view)
        {
            if (_atlasTexture != null)
                GUI.DrawTexture(view, _atlasTexture, ScaleMode.StretchToFill);
            else
                DrawSolid(view, new Color(.055f, .14f, .20f));

            var director = GameDirector.Instance;
            var pins = new Dictionary<string, Vector2>();
            foreach (var region in AtlasRegions)
                foreach (var place in region.Places)
                {
                    pins[place.ZoneId] = AtlasPoint(view, place.Position);
                }

            // The prerequisite graph becomes the atlas road-and-sea-lane network. Locked
            // routes are subdued; a reachable/completed destination lights its approach.
            if (director != null && pins.TryGetValue("council_hall", out Vector2 hall))
            {
                for (int i = 0; i < director.Zones.Length; i++)
                {
                    var zone = director.Zones[i];
                    if (!pins.TryGetValue(zone.ZoneId, out Vector2 destination)) continue;
                    QuestState state = director.GetZoneState(i);
                    Color route = state == QuestState.Locked
                        ? new Color(.08f, .07f, .06f, .34f)
                        : state == QuestState.Completed
                            ? new Color(.35f, .86f, .72f, .62f)
                            : new Color(.94f, .87f, .70f, .68f);
                    if (zone.PrerequisiteZoneIds.Length == 0)
                        DrawDashedLine(hall, destination, route, 1.5f);
                    else
                        foreach (string prerequisite in zone.PrerequisiteZoneIds)
                            if (pins.TryGetValue(prerequisite, out Vector2 start))
                                DrawDashedLine(start, destination, route, 1.5f);
                }
            }

            EnsureAtlasStyles();
            DrawShadowLabel(new Rect(view.x + view.width * .31f, view.y + view.height * .19f,
                    view.width * .17f, 15f), "CINDERFLOW SEA", _atlasOceanStyle,
                new Color(.66f, .83f, .89f, .54f));
            DrawShadowLabel(new Rect(view.x + view.width * .60f, view.y + view.height * .86f,
                    view.width * .18f, 15f), "ASHEN SEA", _atlasOceanStyle,
                new Color(.66f, .83f, .89f, .46f));

            foreach (var region in AtlasRegions)
            {
                Vector2 label = AtlasPoint(view, region.LabelPosition);
                DrawShadowLabel(new Rect(label.x - 90f, label.y - 8f, 180f, 16f),
                    region.Name, _atlasRegionStyle, Theme.Parchment);
            }

            AtlasPlace hovered = null;
            string hoveredRegion = "";
            foreach (var region in AtlasRegions)
                foreach (var place in region.Places)
                {
                    Vector2 point = pins[place.ZoneId];
                    bool hubPin = place.ZoneId == "council_hall";
                    int zoneIndex = director == null || hubPin ? -1
                        : System.Array.FindIndex(director.Zones, z => z.ZoneId == place.ZoneId);
                    QuestState state = zoneIndex >= 0
                        ? director.GetZoneState(zoneIndex) : QuestState.Active;
                    bool active = zoneIndex >= 0 && state == QuestState.Active;
                    Texture2D pin = active ? _questX : state == QuestState.Completed
                        ? _atlasDone : state == QuestState.Locked ? _atlasLocked : _atlasOpen;
                    float size = active ? 18f + Mathf.Sin(Time.time * 4f) * 2f
                        : hubPin ? 14f : 11f;
                    GUI.DrawTexture(new Rect(point.x - size / 2f, point.y - size / 2f, size, size), pin);

                    Color text = active ? Theme.Gold : state == QuestState.Locked
                        ? Theme.OnSurfaceMuted : Theme.OnSurface;
                    var label = new Rect(point.x - 52f + place.LabelOffset.x,
                        point.y + place.LabelOffset.y, 104f, 14f);
                    DrawShadowLabel(label, place.Label.ToUpperInvariant(), _atlasPlaceStyle, text);

                    var hit = new Rect(point.x - 12f, point.y - 12f, 24f, 24f);
                    if (hit.Contains(Event.current.mousePosition))
                    {
                        hovered = place;
                        hoveredRegion = region.Name;
                    }
                }

            // A separate teal heading marker makes current location unmistakable even when
            // it shares a pin with an active quest.
            if (pins.TryGetValue(_atlasPlayerPlace, out Vector2 you))
            {
                var old = GUI.matrix;
                GUIUtility.RotateAroundPivot(0f, you);
                GUI.DrawTexture(new Rect(you.x - 8f, you.y - 25f, 16f, 16f), _playerArrow);
                GUI.matrix = old;
                DrawShadowLabel(new Rect(you.x - 38f, you.y + 22f, 76f, 13f), "YOU ARE HERE",
                    _atlasPlaceStyle, new Color(.40f, .88f, 1f));
            }

            DrawAtlasCompass(view);
            DrawAtlasKey(view, hovered, hoveredRegion, director);

            // Crisp inset edge over the map texture, like an engraved atlas frame.
            DrawSolid(new Rect(view.x, view.y, view.width, 2f), Theme.GoldDeep);
            DrawSolid(new Rect(view.x, view.yMax - 2f, view.width, 2f), Theme.GoldDeep);
            DrawSolid(new Rect(view.x, view.y, 2f, view.height), Theme.GoldDeep);
            DrawSolid(new Rect(view.xMax - 2f, view.y, 2f, view.height), Theme.GoldDeep);
        }

        private void DrawAtlasKey(Rect view, AtlasPlace hovered, string hoveredRegion,
            GameDirector director)
        {
            var panel = new Rect(view.xMax - 244f, view.yMax - 52f, 236f, 44f);
            DrawSolid(panel, new Color(.055f, .045f, .035f, .88f));
            DrawSolid(new Rect(panel.x, panel.y, 3f, panel.height), Theme.GoldDeep);
            string title = "REGION > DESTINATION";
            string detail = "Pins sit inside their region; dashed routes show what unlocks next.";
            if (hovered != null)
            {
                title = $"{hoveredRegion} > {hovered.Label}".ToUpperInvariant();
                if (hovered.ZoneId == "council_hall") detail = "Party hub and waystone network.";
                else if (director != null)
                {
                    int i = System.Array.FindIndex(director.Zones, z => z.ZoneId == hovered.ZoneId);
                    detail = i < 0 ? "Campaign destination" : StatusLabel(director.GetZoneState(i));
                }
            }
            GUI.Label(new Rect(panel.x + 9f, panel.y + 5f, panel.width - 14f, 14f), title,
                Theme.Caps);
            GUI.Label(new Rect(panel.x + 9f, panel.y + 21f, panel.width - 14f, 20f), detail,
                _atlasDetailStyle);

            var legend = new Rect(view.x + 8f, view.yMax - 29f, 242f, 21f);
            DrawSolid(legend, new Color(.055f, .045f, .035f, .82f));
            DrawAtlasLegendItem(legend.x + 7f, legend.y + 5f, _atlasOpen, "OPEN");
            DrawAtlasLegendItem(legend.x + 65f, legend.y + 5f, _questX, "ACTIVE");
            DrawAtlasLegendItem(legend.x + 135f, legend.y + 5f, _atlasDone, "DONE");
            DrawAtlasLegendItem(legend.x + 194f, legend.y + 5f, _atlasLocked, "LOCKED");
        }

        private void DrawAtlasLegendItem(float x, float y, Texture2D icon, string label)
        {
            GUI.DrawTexture(new Rect(x, y, 10f, 10f), icon);
            GUI.Label(new Rect(x + 13f, y - 2f, 48f, 14f), label, _atlasPlaceStyle);
        }

        private void DrawAtlasCompass(Rect view)
        {
            Vector2 c = new Vector2(view.x + 31f, view.y + 34f);
            DrawLine(new Vector2(c.x, c.y - 16f), new Vector2(c.x, c.y + 16f),
                new Color(.93f, .87f, .75f, .85f), 1.5f);
            DrawLine(new Vector2(c.x - 16f, c.y), new Vector2(c.x + 16f, c.y),
                new Color(.93f, .87f, .75f, .85f), 1.5f);
            GUI.DrawTexture(new Rect(c.x - 4f, c.y - 18f, 8f, 15f), _playerArrow);
            GUI.Label(new Rect(c.x - 7f, c.y - 30f, 14f, 13f), "N", Theme.Caps);
            GUI.Label(new Rect(c.x + 18f, c.y - 7f, 14f, 13f), "E", Theme.Caps);
            GUI.Label(new Rect(c.x - 30f, c.y - 7f, 14f, 13f), "W", Theme.Caps);
        }

        private void EnsureAtlasStyles()
        {
            if (_atlasRegionStyle != null) return;
            _atlasRegionStyle = new GUIStyle(Theme.Caps)
                { alignment = TextAnchor.MiddleCenter, fontSize = 12, fontStyle = FontStyle.Bold };
            _atlasPlaceStyle = new GUIStyle(Theme.Caps)
                { alignment = TextAnchor.MiddleCenter, fontSize = 8, fontStyle = FontStyle.Bold };
            _atlasOceanStyle = new GUIStyle(Theme.Caps)
                { alignment = TextAnchor.MiddleCenter, fontSize = 9, fontStyle = FontStyle.Italic };
            _atlasDetailStyle = new GUIStyle(Theme.Body)
                { alignment = TextAnchor.UpperLeft, fontSize = 9, wordWrap = true };
        }

        private static string StatusLabel(QuestState state)
        {
            switch (state)
            {
                case QuestState.Active: return "Active commission - follow the gold route.";
                case QuestState.ObjectivesMet: return "Objectives met - report at Council Hall.";
                case QuestState.Completed: return "Commission completed.";
                default: return "Locked - complete its prerequisite route.";
            }
        }

        private static Vector2 AtlasPoint(Rect view, Vector2 normalized) =>
            new Vector2(view.x + normalized.x * view.width, view.y + normalized.y * view.height);

        private static void DrawShadowLabel(Rect rect, string text, GUIStyle style, Color color)
        {
            style.normal.textColor = new Color(0f, 0f, 0f, Mathf.Max(.60f, color.a));
            GUI.Label(new Rect(rect.x + 1f, rect.y + 1f, rect.width, rect.height), text, style);
            style.normal.textColor = color;
            GUI.Label(rect, text, style);
        }

        private static void DrawSolid(Rect rect, Color color)
        {
            Color old = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = old;
        }

        private static void DrawDashedLine(Vector2 start, Vector2 end, Color color, float width)
        {
            float length = Vector2.Distance(start, end);
            if (length < 1f) return;
            Vector2 direction = (end - start) / length;
            const float dash = 6f, gap = 4f;
            for (float at = 0f; at < length; at += dash + gap)
                DrawLine(start + direction * at,
                    start + direction * Mathf.Min(length, at + dash), color, width);
        }

        private static void DrawLine(Vector2 start, Vector2 end, Color color, float width)
        {
            Vector2 delta = end - start;
            float length = delta.magnitude;
            if (length < .5f) return;
            Matrix4x4 oldMatrix = GUI.matrix;
            Color oldColor = GUI.color;
            GUI.color = color;
            GUIUtility.RotateAroundPivot(Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg, start);
            GUI.DrawTexture(new Rect(start.x, start.y - width / 2f, length, width),
                Texture2D.whiteTexture);
            GUI.matrix = oldMatrix;
            GUI.color = oldColor;
        }

        private void ValidateAtlas()
        {
            var director = GameDirector.Instance;
            if (director == null) return;
            string[] mapped = AtlasRegions.SelectMany(r => r.Places)
                .Where(p => p.ZoneId != "council_hall").Select(p => p.ZoneId).ToArray();
            var expected = new HashSet<string>(director.Zones.Select(z => z.ZoneId));
            var actual = new HashSet<string>(mapped);
            bool unique = mapped.Length == actual.Count;
            bool covered = expected.SetEquals(actual);
            int routes = director.Zones.Sum(z => Mathf.Max(1, z.PrerequisiteZoneIds.Length));
            if (unique && covered && AtlasRegions.Length == 6)
                Debug.Log($"[WorldMap] PASS - {AtlasRegions.Length} regions contain " +
                          $"{actual.Count}/{expected.Count} campaign destinations; " +
                          $"{routes} prerequisite routes charted");
            else
                Debug.LogError($"[WorldMap] FAIL - regions {AtlasRegions.Length}, mapped " +
                               $"{actual.Count}/{expected.Count}, unique {unique}");
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

        /// <summary>Builds the atlas's ocean and six coastlines once. This stays procedural
        /// like every other minimap icon, so it is resolution-independent, source-controlled,
        /// original art, and cannot disappear because a font or imported texture is missing.</summary>
        private static Texture2D MakeAtlasTexture()
        {
            const int width = 1024, height = 600;
            var tex = new Texture2D(width, height, TextureFormat.RGBA32, false)
            {
                hideFlags = HideFlags.HideAndDontSave,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            var regionAt = new int[width * height];
            for (int i = 0; i < regionAt.Length; i++) regionAt[i] = -1;

            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                {
                    Vector2 p = new Vector2((x + .5f) / width, (y + .5f) / height);
                    for (int r = 0; r < AtlasRegions.Length; r++)
                        if (InsidePolygon(p, AtlasRegions[r].Coast))
                        {
                            regionAt[y * width + x] = r;
                            break;
                        }
                }

            var pixels = new Color[width * height];
            var oceanDeep = new Color(.035f, .105f, .155f);
            var oceanLight = new Color(.075f, .235f, .30f);
            var coast = new Color(.09f, .075f, .055f);
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                {
                    int mapIndex = y * width + x;
                    int region = regionAt[mapIndex];
                    float nx = (x + .5f) / width, ny = (y + .5f) / height;
                    float vignette = Mathf.Clamp01(Mathf.Abs(nx - .5f) * .75f
                                                   + Mathf.Abs(ny - .5f) * .48f);
                    float wave = (Mathf.Sin(nx * 91f + ny * 37f)
                                  + Mathf.Sin(nx * 31f - ny * 73f)) * .012f;
                    Color value;
                    if (region < 0)
                    {
                        value = Color.Lerp(oceanLight, oceanDeep, .25f + vignette * .65f);
                        value += new Color(wave, wave, wave, 0f);
                        // A faint cartographic grid gives the water scale without competing
                        // with roads or labels.
                        if ((x % 128 == 0 || y % 100 == 0))
                            value = Color.Lerp(value, new Color(.32f, .51f, .56f), .12f);
                        if (NeighborRegion(regionAt, width, height, x, y, 4) >= 0)
                            value = Color.Lerp(value, new Color(.025f, .04f, .045f), .70f);
                    }
                    else
                    {
                        Color land = AtlasRegions[region].LandColor;
                        float grain = Hash01(x, y) * .10f - .05f;
                        float light = .86f + (1f - ny) * .17f + grain;
                        value = new Color(land.r * light, land.g * light,
                            land.b * light, 1f);
                        if (NeighborRegion(regionAt, width, height, x, y, 3) != region)
                            value = Color.Lerp(coast, value, .26f);
                    }
                    // Pixel storage is bottom-up; atlas positions deliberately use a
                    // top-left origin, so flip once here and nowhere in the UI code.
                    pixels[(height - 1 - y) * width + x] = value;
                }
            tex.SetPixels(pixels);
            tex.Apply(false, false);
            return tex;
        }

        private static int NeighborRegion(int[] regions, int width, int height,
            int x, int y, int distance)
        {
            int own = regions[y * width + x];
            int[] xs = { x - distance, x + distance, x, x };
            int[] ys = { y, y, y - distance, y + distance };
            for (int i = 0; i < 4; i++)
            {
                if (xs[i] < 0 || ys[i] < 0 || xs[i] >= width || ys[i] >= height) continue;
                int other = regions[ys[i] * width + xs[i]];
                if (other != own) return other;
            }
            return own;
        }

        private static bool InsidePolygon(Vector2 point, Vector2[] polygon)
        {
            bool inside = false;
            for (int i = 0, j = polygon.Length - 1; i < polygon.Length; j = i++)
            {
                Vector2 a = polygon[i], b = polygon[j];
                bool crosses = (a.y > point.y) != (b.y > point.y)
                    && point.x < (b.x - a.x) * (point.y - a.y)
                    / (b.y - a.y + .000001f) + a.x;
                if (crosses) inside = !inside;
            }
            return inside;
        }

        private static float Hash01(int x, int y)
        {
            uint n = (uint)(x * 374761393 + y * 668265263);
            n = (n ^ (n >> 13)) * 1274126177u;
            return (n & 0xffffu) / 65535f;
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
