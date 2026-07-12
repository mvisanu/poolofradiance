using System.Linq;
using UnityEngine;

namespace RadiantPool.Game
{
    /// <summary>Points the player at their current objective: the nearest uncleared
    /// encounter in the active zone, or Councilor Veresk when it's time to accept or
    /// turn in. Shows a golden light beacon at the target, a HUD line with direction
    /// arrow + distance, and feeds the minimap dot.</summary>
    public class QuestTracker : MonoBehaviour
    {
        public static QuestTracker Instance { get; private set; }

        public bool HasTarget { get; private set; }
        public Vector3 TargetPosition { get; private set; }
        public string TargetLabel { get; private set; } = "";

        private GameObject _beacon;
        private float _nextScan;

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

            if (activeZone < 0) return;   // campaign complete or nothing active

            string zoneId = director.Zones[activeZone].ZoneId;
            var next = FindObjectsByType<EncounterTrigger>(FindObjectsSortMode.None)
                .Where(t => t.ZoneId == zoneId && t.RequiredForClear
                            && !director.ConsumedEncounterIds.Contains(t.EncounterId))
                .OrderBy(t => Vector3.Distance(player.position, t.transform.position))
                .FirstOrDefault();
            if (next == null) return;

            TargetPosition = next.transform.position;
            TargetLabel = next.DisplayName;
            HasTarget = true;
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
                var mat = _beacon.GetComponent<Renderer>().material;
                mat.EnableKeyword("_EMISSION");
                mat.color = new Color(1f, 0.85f, 0.3f, 1f);
                mat.SetColor("_EmissionColor", new Color(1.4f, 1.1f, 0.35f));
            }
            _beacon.SetActive(true);
            _beacon.transform.position = new Vector3(TargetPosition.x, 14f, TargetPosition.z);
            float pulse = 0.8f + 0.18f * Mathf.Sin(Time.time * 2.4f);
            _beacon.transform.localScale = new Vector3(pulse, 14f, pulse);
        }

        private void OnGUI()
        {
            Ui.Begin();
            var player = LocalPlayer();
            bool inCombat = CombatManager.Instance != null && CombatManager.Instance.InCombat.Value;
            if (!HasTarget || player == null || inCombat) return;

            Vector3 delta = TargetPosition - player.position;
            float dist = new Vector2(delta.x, delta.z).magnitude;

            // Bearing arrow relative to camera view.
            float camYaw = Camera.main != null ? Camera.main.transform.eulerAngles.y : 0f;
            float bearing = Mathf.Atan2(delta.x, delta.z) * Mathf.Rad2Deg - camYaw;
            string arrow = BearingArrow(bearing);

            GUI.Box(new Rect(Ui.W / 2f - 210, 52, 420, 34),
                $"{arrow}  NEXT: {TargetLabel}  <color=#d0c5af>— {dist:0} m</color>",
                Theme.Toast);

            DrawWorldMarker(dist);
        }

        /// <summary>Gold chevron floating over the objective in the world; when the
        /// objective is off-screen the marker clamps to the screen edge and shows the
        /// bearing arrow instead, so there is always something to walk toward.</summary>
        private void DrawWorldMarker(float dist)
        {
            var cam = Camera.main;
            if (cam == null) return;
            Vector3 sp = cam.WorldToScreenPoint(TargetPosition + Vector3.up * 3.2f);
            bool behind = sp.z < 0f;
            if (behind) { sp.x = Screen.width - sp.x; sp.y = Screen.height - sp.y; }
            var gui = new Vector2(sp.x / Ui.Scale, (Screen.height - sp.y) / Ui.Scale);
            bool offscreen = behind || gui.x < 24 || gui.x > Ui.W - 24
                                    || gui.y < 96 || gui.y > Ui.H - 60;
            gui.x = Mathf.Clamp(gui.x, 40, Ui.W - 40);
            gui.y = Mathf.Clamp(gui.y, 100, Ui.H - 70);

            var mark = new GUIStyle(Theme.Header)
                { alignment = TextAnchor.MiddleCenter, fontSize = 24, wordWrap = false };
            float bob = offscreen ? 0f : Mathf.Sin(Time.time * 3f) * 4f;
            if (offscreen)
            {
                Vector3 d = TargetPosition - LocalPlayer().position;
                float camYaw = cam.transform.eulerAngles.y;
                string a = BearingArrow(Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg - camYaw);
                GUI.Label(new Rect(gui.x - 60, gui.y - 16, 120, 30),
                    $"{a} {dist:0}m", mark);
            }
            else
            {
                GUI.Label(new Rect(gui.x - 60, gui.y - 18 + bob, 120, 26), "▼", mark);
                var caption = new GUIStyle(Theme.Caps)
                    { alignment = TextAnchor.MiddleCenter, wordWrap = false };
                GUI.Label(new Rect(gui.x - 110, gui.y + 8 + bob, 220, 16),
                    TargetLabel, caption);
            }
        }

        private static string BearingArrow(float degrees)
        {
            degrees = (degrees % 360f + 360f) % 360f;
            string[] arrows = { "↑", "↗", "→", "↘", "↓", "↙", "←", "↖" };
            return arrows[Mathf.RoundToInt(degrees / 45f) % 8];
        }
    }
}
