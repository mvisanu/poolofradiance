using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace RadiantPool.Game
{
    /// <summary>Points the player at their current objective: the nearest uncleared
    /// encounter in the active zone, or Councilor Veresk when it's time to accept or
    /// turn in. Shows a golden light beacon at the target, a big steering arrow above the
    /// hotbar that always points the way to walk (rotated into camera space, so "up" is
    /// straight ahead), a HUD line with the objective + distance, and feeds the minimap
    /// X.</summary>
    public class QuestTracker : MonoBehaviour
    {
        public static QuestTracker Instance { get; private set; }

        public bool HasTarget { get; private set; }
        public Vector3 TargetPosition { get; private set; }
        public string TargetLabel { get; private set; } = "";

        private GameObject _beacon;
        private float _nextScan;
        private Texture2D _steerArrow;
        private GUIStyle _distStyle, _questTitle, _stepStyle, _stepDone, _cardBtn;

        private void Awake() => Instance = this;
        private void OnDestroy() { if (Instance == this) Instance = null; }

        private Transform LocalPlayer()
        {
            var holder = FindObjectsByType<PlayerCharacterHolder>(FindObjectsSortMode.None)
                .FirstOrDefault(p => p.IsOwner);
            return holder != null ? holder.transform : null;
        }

        private void Update()
        {
            if (Time.time < _nextScan) return;
            _nextScan = Time.time + 0.5f;
            Scan();
            UpdateBeacon();
        }

        /// <summary>Inside this range of a quarter's centre you are "there", and the
        /// tracker stops pointing at the district and starts pointing at the next fight.</summary>
        private const float DistrictRange = 26f;

        /// <summary>Centre of a quarter — the mean of the fights that make it up, so it
        /// needs no hand-placed anchor and can never drift out of sync with the map.</summary>
        private static Vector3 Centre(List<EncounterTrigger> triggers)
        {
            Vector3 sum = Vector3.zero;
            foreach (var t in triggers) sum += t.transform.position;
            return sum / triggers.Count;
        }

        private void Scan()
        {
            HasTarget = false;
            var director = GameDirector.Instance;
            var player = LocalPlayer();
            bool inCombat = CombatManager.Instance != null && CombatManager.Instance.InCombat.Value;
            if (director == null || player == null || inCombat
                || director.Zones.Length == 0) return;

            // Talking to Veresk: muster, any turn-in, or campaign done.
            bool needVeresk = (QuestState)director.MusterState.Value == QuestState.Active;
            int activeZone = -1;
            for (int i = 0; i < director.Zones.Length; i++)
            {
                var state = director.GetZoneState(i);
                if (state == QuestState.ObjectivesMet) needVeresk = true;
                if (state == QuestState.Active && activeZone < 0) activeZone = i;
            }

            if (needVeresk)
            {
                var veresk = FindObjectsByType<NpcInteract>(FindObjectsSortMode.None)
                    .FirstOrDefault();
                if (veresk != null)
                {
                    TargetPosition = veresk.transform.position;
                    TargetLabel = "Councilor Veresk";
                    HasTarget = true;
                }
                return;
            }

            // No active zone = the campaign is done. Rather than leaving the party with
            // nothing to chase, point them at whatever threat is still standing.
            var candidates = FindObjectsByType<EncounterTrigger>(FindObjectsSortMode.None)
                .Where(t => !t.Consumed && t.MonsterIds.Length > 0
                            && !director.ConsumedEncounterIds.Contains(t.EncounterId))
                .ToList();
            if (activeZone >= 0)
            {
                string zoneId = director.Zones[activeZone].ZoneId;
                var zoneFights = candidates
                    .Where(t => t.ZoneId == zoneId && t.RequiredForClear).ToList();
                if (zoneFights.Count == 0) return;

                // "Retake the Old Docks" is meaningless if you don't know where the Old
                // Docks are. While the party is still out of the quarter, steer them at
                // the quarter itself, named; once inside, switch to the next fight in it.
                Vector3 district = Centre(zoneFights);
                float toDistrict = Vector3.Distance(player.position, district);
                if (toDistrict > DistrictRange)
                {
                    TargetPosition = district;
                    TargetLabel = director.Zones[activeZone].DisplayName;
                    HasTarget = true;
                    return;
                }
                candidates = zoneFights;
            }

            var next = candidates
                .OrderBy(t => Vector3.Distance(player.position, t.transform.position))
                .FirstOrDefault();
            if (next == null) return;

            TargetPosition = next.transform.position;
            // Name the quarter alongside the spot, so the objective always reads in the
            // same words the quest does.
            string quarter = activeZone >= 0 ? director.Zones[activeZone].DisplayName : null;
            TargetLabel = quarter != null ? $"{quarter} — {next.DisplayName}" : next.DisplayName;
            HasTarget = true;
        }

        /// <summary>The live objective list: the quest the party is on and exactly what is
        /// left to do on it. Ticked lines are done, hollow lines are outstanding.</summary>
        public static (string title, List<(string text, bool done)> steps) Objectives()
        {
            var steps = new List<(string, bool)>();
            var d = GameDirector.Instance;
            if (d == null || d.Zones.Length == 0) return ("", steps);

            if ((QuestState)d.MusterState.Value == QuestState.Active)
            {
                steps.Add(("Speak with Councilor Veresk", false));
                return ("Report to the Council", steps);
            }

            for (int i = 0; i < d.Zones.Length; i++)
            {
                var state = d.GetZoneState(i);
                if (state != QuestState.Active && state != QuestState.ObjectivesMet) continue;

                var cfg = d.Zones[i];
                int done = i < d.ZoneClearedCounts.Count ? d.ZoneClearedCounts[i] : 0;
                int need = cfg.RequiredEncounters;
                bool cleared = state == QuestState.ObjectivesMet || done >= need;

                steps.Add((cleared
                    ? $"Clear {cfg.DisplayName} — {need}/{need}"
                    : $"Clear {cfg.DisplayName} — {done}/{need} ({need - done} left)", cleared));
                steps.Add(("Report back to Councilor Veresk", false));
                return (cfg.QuestName, steps);
            }

            // Campaign over: the standing order is to mop up whatever still lurks.
            int lurking = FindObjectsByType<EncounterTrigger>(FindObjectsSortMode.None)
                .Count(t => !t.Consumed && t.MonsterIds.Length > 0
                            && !d.ConsumedEncounterIds.Contains(t.EncounterId));
            if (lurking > 0)
            {
                steps.Add(($"Hunt the threats still at large — {lurking} left", false));
                return ("Standing Orders: Purge the Ruins", steps);
            }

            steps.Add(("Every threat has been put down", true));
            return ("The Hollow Flame Recedes", steps);
        }

        private void UpdateBeacon()
        {
            if (!HasTarget)
            {
                if (_beacon != null) _beacon.SetActive(false);
                return;
            }
            if (_beacon == null)
            {
                _beacon = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                Destroy(_beacon.GetComponent<Collider>());
                _beacon.name = "QuestBeacon";
                // Repaint: the primitive's default material is built-in Standard, which
                // URP renders as a MAGENTA pillar in a build (it looked gold in editor).
                RuntimeArt.Paint(_beacon, new Color(1f, 0.85f, 0.3f, 1f), emission: 1.4f);
            }
            _beacon.SetActive(true);
            _beacon.transform.position = new Vector3(TargetPosition.x, 14f, TargetPosition.z);
            float pulse = 0.8f + 0.18f * Mathf.Sin(Time.time * 2.4f);
            _beacon.transform.localScale = new Vector3(pulse, 14f, pulse);
        }

        private void OnGUI()
        {
            Ui.Begin();
            // A screen is up: the card, the banner and the steering arrow all step aside —
            // the banner used to land right on the top edge of the open panel.
            if (Ui.PanelOpen) return;
            var player = LocalPlayer();
            bool inCombat = CombatManager.Instance != null && CombatManager.Instance.InCombat.Value;
            if (player == null || inCombat) return;

            DrawObjectives();   // shown even with no target, so the party is never adrift
            if (!HasTarget) return;

            Vector3 delta = TargetPosition - player.position;
            float dist = new Vector2(delta.x, delta.z).magnitude;

            // Bearing relative to the camera, so screen-up always means "walk forward".
            float camYaw = Camera.main != null ? Camera.main.transform.eulerAngles.y : 0f;
            float bearing = Mathf.Atan2(delta.x, delta.z) * Mathf.Rad2Deg - camYaw;

            Theme.DrawToast(Ui.W / 2f, 58,
                $"NEXT: {TargetLabel}  <color=#d0c5af>— {dist:0} m {Bearing(bearing)}</color>",
                maxW: Mathf.Min(660f, Ui.W - 40f));

            DrawSteeringArrow(bearing, dist);
            DrawWorldMarker(dist);
        }

        /// <summary>Persistent quest card, top-left: the active quest and a checklist of
        /// what is still outstanding. It updates the instant a quest hands over, so
        /// finishing one immediately shows the next.</summary>
        /// <summary>The card sits in the top-left corner itself now — the hosting strip that
        /// used to hold that spot moved onto the hotbar (SessionPanel), so nothing is above it.</summary>
        private const float CardTop = 12f;

        /// <summary>Collapsed state, remembered like the minimap's size: a player who wants the
        /// city rather than the checklist should not have to hide it again every session.</summary>
        private static bool Collapsed
        {
            get => PlayerPrefs.GetInt("questCardCollapsed", 0) == 1;
            set => PlayerPrefs.SetInt("questCardCollapsed", value ? 1 : 0);
        }

        private void DrawObjectives()
        {
            var (title, steps) = Objectives();
            if (string.IsNullOrEmpty(title)) return;

            if (_questTitle == null)
            {
                _questTitle = new GUIStyle(Theme.Header) { fontSize = 14, wordWrap = true };
                _stepStyle = new GUIStyle(Theme.Body) { fontSize = 12, wordWrap = true };
                _stepDone = new GUIStyle(Theme.Body) { fontSize = 12, wordWrap = true };
                _stepDone.normal.textColor = Theme.OnSurfaceMuted;
            }

            float w = Mathf.Clamp(Ui.W * 0.24f, 200f, 250f);
            const float pad = 12f;
            // The Hide/Show button. It has to be wide enough for its own WORD at this font:
            // at 46 px the skin's padding ate the label and "Show" rendered as "how".
            const float btn = 62f;
            if (_cardBtn == null)
                _cardBtn = new GUIStyle(GUI.skin.button) { fontSize = 11, wordWrap = false,
                    padding = new RectOffset(2, 2, 2, 2), clipping = TextClipping.Overflow };

            // Collapsed: one slim bar with the quest's name, and the way back.
            if (Collapsed)
            {
                var pill = new Rect(12, CardTop, w, 30f);
                GUI.Box(pill, GUIContent.none, Theme.PanelStyle);
                var nameStyle = new GUIStyle(Theme.Body) { fontSize = 12, wordWrap = false,
                    clipping = TextClipping.Clip };
                GUI.Label(new Rect(pill.x + 8f, pill.y + 6f, w - btn - 14f, 18f), title, nameStyle);
                if (GUI.Button(new Rect(pill.xMax - btn - 5f, pill.y + 4f, btn, 22f),
                        "Show", _cardBtn))
                    Collapsed = false;
                return;
            }

            float titleW = w - pad * 2f - btn;
            float h = pad * 2f + _questTitle.CalcHeight(new GUIContent(title), titleW) + 4f;
            foreach (var (text, _) in steps)
                h += _stepStyle.CalcHeight(new GUIContent(text), w - pad * 2f - 18f) + 2f;

            var panel = new Rect(12, CardTop, w, Mathf.Min(h, Ui.H - CardTop - 140f));
            GUI.Box(panel, GUIContent.none, Theme.PanelStyle);

            float y = panel.y + pad;
            float tH = _questTitle.CalcHeight(new GUIContent(title), titleW);
            GUI.Label(new Rect(panel.x + pad, y, titleW, tH), title, _questTitle);
            if (GUI.Button(new Rect(panel.xMax - btn - 5f, panel.y + 6f, btn, 22f),
                    "Hide", _cardBtn))
                Collapsed = true;
            y += tH + 4f;

            foreach (var (text, done) in steps)
            {
                var style = done ? _stepDone : _stepStyle;
                float sH = style.CalcHeight(new GUIContent(text), w - pad * 2f - 18f);
                if (y + sH > panel.yMax - pad) break;   // never spill out of the card
                // ASCII markers only: the body font has no box/tick glyphs, and a missing
                // glyph renders as tofu. State is carried by the marker, not just colour.
                GUI.Label(new Rect(panel.x + pad, y, 18f, sH), Theme.Check(done), style);
                GUI.Label(new Rect(panel.x + pad + 18f, y, w - pad * 2f - 18f, sH), text, style);
                y += sH + 2f;
            }
        }

        /// <summary>The big "go this way" arrow: a gold chevron above the hotbar, rotated
        /// to the objective's bearing (up = walk straight ahead) and gently pulsing. It is
        /// the primary wayfinding cue — the minimap X is the map-level confirmation.</summary>
        private void DrawSteeringArrow(float bearing, float dist)
        {
            if (_steerArrow == null) _steerArrow = MakeSteerArrow();

            float size = 96f + Mathf.Sin(Time.time * 2.6f) * 5f;
            float baseY = HotBar.BarRect.height > 0f ? HotBar.BarRect.y : Ui.H - 92f;
            var center = new Vector2(Ui.W / 2f, baseY - 22f - size / 2f);

            var prev = GUI.matrix;
            GUIUtility.RotateAroundPivot(bearing, center);
            GUI.DrawTexture(new Rect(center.x - size / 2f, center.y - size / 2f, size, size),
                _steerArrow);
            GUI.matrix = prev;

            if (_distStyle == null)
            {
                _distStyle = new GUIStyle(Theme.Caps)
                    { alignment = TextAnchor.MiddleCenter, wordWrap = false, fontSize = 12 };
                _distStyle.normal.textColor = Theme.Gold;
            }
            GUI.Label(new Rect(center.x - 90, center.y + size / 2f - 4, 180, 16),
                $"{dist:0} m", _distStyle);
        }

        /// <summary>Chunky up-pointing arrow (triangle head + tail) with a dark outline so
        /// it reads over sky, grass or stone. Generated, so it needs no art asset.</summary>
        private static Texture2D MakeSteerArrow()
        {
            const int s = 64;
            const float ApexY = s - 4f, BaseY = s * 0.44f, TailY = 5f;
            const float HeadHalf = s * 0.46f, TailHalf = s * 0.15f;

            // Grow the whole silhouette by g to get the outline band.
            bool Inside(int x, int y, float g)
            {
                float fx = Mathf.Abs(x - (s - 1) / 2f);
                float headHalf = Mathf.Lerp(0f, HeadHalf,
                    Mathf.InverseLerp(ApexY, BaseY, y));       // 0 at the point
                bool head = y <= ApexY + g && y >= BaseY - g && fx <= headHalf + g;
                bool tail = y >= TailY - g && y <= BaseY + g && fx <= TailHalf + g;
                return head || tail;
            }

            var tex = new Texture2D(s, s, TextureFormat.RGBA32, false)
                { hideFlags = HideFlags.HideAndDontSave };
            var edge = new Color(0.13f, 0.09f, 0.03f);
            for (int y = 0; y < s; y++)
                for (int x = 0; x < s; x++)
                    tex.SetPixel(x, y,
                        Inside(x, y, 0f) ? Theme.Gold
                        : Inside(x, y, 3f) ? edge
                        : Color.clear);
            tex.Apply();
            return tex;
        }

        /// <summary>Gold chevron floating over the objective in the world; when the
        /// objective is off-screen the marker clamps to the screen edge and the arrow
        /// points at it, so there is always something to walk toward. The chevron is the
        /// generated arrow texture, rotated — the "▼" it used to draw is not a glyph any
        /// of the shipped fonts actually has.</summary>
        private void DrawWorldMarker(float dist)
        {
            var cam = Camera.main;
            var player = LocalPlayer();
            if (cam == null || player == null) return;
            if (_steerArrow == null) _steerArrow = MakeSteerArrow();

            Vector3 sp = cam.WorldToScreenPoint(TargetPosition + Vector3.up * 3.2f);
            bool behind = sp.z < 0f;
            if (behind) { sp.x = Screen.width - sp.x; sp.y = Screen.height - sp.y; }
            var gui = new Vector2(sp.x / Ui.Scale, (Screen.height - sp.y) / Ui.Scale);
            bool offscreen = behind || gui.x < 24 || gui.x > Ui.W - 24
                                    || gui.y < 96 || gui.y > Ui.H - 60;
            gui.x = Mathf.Clamp(gui.x, 40, Ui.W - 40);
            gui.y = Mathf.Clamp(gui.y, 100, Ui.H - 70);

            var caption = new GUIStyle(Theme.Caps)
                { alignment = TextAnchor.MiddleCenter, wordWrap = false };
            float bob = offscreen ? 0f : Mathf.Sin(Time.time * 3f) * 4f;

            Vector3 d = TargetPosition - player.position;
            float bearing = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg - cam.transform.eulerAngles.y;
            // Off-screen: the arrow points the way. On-screen: it points down at the spot.
            float angle = offscreen ? bearing : 180f;
            var at = new Vector2(gui.x, gui.y - 8f + bob);

            var prev = GUI.matrix;
            GUIUtility.RotateAroundPivot(angle, at);
            GUI.DrawTexture(new Rect(at.x - 14f, at.y - 14f, 28f, 28f), _steerArrow);
            GUI.matrix = prev;

            GUI.Label(new Rect(gui.x - 110, gui.y + 10 + bob, 220, 16),
                offscreen ? $"{TargetLabel} — {dist:0} m" : TargetLabel, caption);
        }

        /// <summary>Relative bearing in words — "ahead", "ahead-right", … Words instead of
        /// arrow glyphs (↑↗→ are not in MedievalSharp/Inter and render as tofu), and they
        /// read faster than a symbol anyway.</summary>
        private static string Bearing(float degrees)
        {
            degrees = (degrees % 360f + 360f) % 360f;
            string[] names =
            {
                "ahead", "ahead-right", "to your right", "behind-right",
                "behind you", "behind-left", "to your left", "ahead-left"
            };
            return names[Mathf.RoundToInt(degrees / 45f) % 8];
        }
    }
}
