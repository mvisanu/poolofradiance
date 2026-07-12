using System.Collections.Generic;
using System.Linq;
using RadiantPool.Rules;
using UnityEngine;

namespace RadiantPool.Game
{
    /// <summary>IMGUI combat HUD for the gray-box phase: initiative order with HP,
    /// combat log, and — on your turn — click-to-move on the grid, attack/spell target
    /// pickers, Dodge and End Turn. Replaced by the themed UI at 3f.</summary>
    public class CombatClientUI : MonoBehaviour
    {
        public static CombatClientUI Instance { get; private set; }

        private enum Mode { Root, PickAttackTarget, PickSpell, PickSpellTarget }
        private Mode _mode = Mode.Root;
        private string _pendingSpell = "";
        private Vector2 _logScroll;
        private GameObject _hoverMarker;

        private void Awake() => Instance = this;
        private void OnDestroy() { if (Instance == this) Instance = null; }

        /// <summary>HotBar hooks: begin target picking for the basic attack / a spell.</summary>
        public void PickAttack() => _mode = Mode.PickAttackTarget;

        public void PickSpell(string spellId)
        {
            _pendingSpell = spellId;
            _mode = Mode.PickSpellTarget;
        }

        /// <summary>Class spell loadout (v1 fixed lists) — shared with the HotBar.</summary>
        public static string[] KnownSpells(CharacterClass cls) => cls switch
        {
            CharacterClass.Wizard =>
                new[] { "fire_bolt", "magic_missile", "burning_hands", "sleep" },
            CharacterClass.Cleric =>
                new[] { "sacred_flame", "guiding_bolt", "cure_wounds", "healing_word", "bless" },
            _ => System.Array.Empty<string>()
        };

        public static PlayerCharacterHolder LocalPlayerHolder() =>
            Object.FindObjectsByType<PlayerCharacterHolder>(FindObjectsSortMode.None)
                .FirstOrDefault(p => p.IsOwner);

        private static readonly System.Collections.Generic.Dictionary<string, Texture2D>
            IconCache = new System.Collections.Generic.Dictionary<string, Texture2D>();

        /// <summary>Action/spell icon from Resources/SpellIcons (game-icons.net, CC BY —
        /// see README). Swap the art by overwriting the same-named PNGs, e.g. with an
        /// imported Asset Store icon pack. Missing icons fall back to text-only.</summary>
        public static Texture2D Icon(string id)
        {
            if (!IconCache.TryGetValue(id, out var tex))
            {
                tex = Resources.Load<Texture2D>($"SpellIcons/{id}");
                IconCache[id] = tex;   // caches null too
            }
            return tex;
        }

        private static GUIContent WithIcon(string id, string label)
        {
            var tex = Icon(id);
            return tex != null ? new GUIContent(" " + label, tex) : new GUIContent(label);
        }

        /// <summary>Click-to-move: hover a grid cell to highlight it, click to walk
        /// there (the server validates every step). Active only on your turn, outside
        /// the target pickers, and away from the HUD boxes.</summary>
        private void Update()
        {
            var combat = CombatManager.Instance;
            var mine = combat?.MyUnit;
            bool interactive = combat != null && combat.IsMyTurn && _mode == Mode.Root
                               && mine is { Down: false, Dead: false }
                               && Camera.main != null;
            UpdateRangeOverlay(combat, interactive && combat.MoveLeft >= 5);
            if (!interactive) { ShowHover(false); return; }

            // Space / Enter: end the turn without touching the mouse.
            if (Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.Return))
            {
                combat.CmdEndTurn();
                return;
            }

            var plane = new Plane(Vector3.up, Vector3.zero);
            var ray = Camera.main.ScreenPointToRay(Input.mousePosition);
            if (!plane.Raycast(ray, out float dist)) { ShowHover(false); return; }

            Vector3 local = ray.GetPoint(dist) - combat.GridOrigin;
            var cell = new Vector2Int(
                Mathf.RoundToInt(local.x / CombatManager.CellSize),
                Mathf.RoundToInt(local.z / CombatManager.CellSize));
            if (Mathf.Abs(cell.x) > 8 || Mathf.Abs(cell.y) > 8 || IsMouseOverHud(combat))
            { ShowHover(false); return; }

            ShowHover(true);
            // Red over an attackable enemy, blue-white for movement — per the mock.
            var enemy = combat.ClientUnits.FirstOrDefault(
                u => !u.IsPc && !u.Dead && u.Cell == cell);
            SetHoverColor(enemy != null
                ? new Color(0.9f, 0.25f, 0.2f) : new Color(0.45f, 0.9f, 1f));
            _hoverMarker.transform.position = combat.GridOrigin + new Vector3(
                cell.x * CombatManager.CellSize, 0.05f, cell.y * CombatManager.CellSize);

            if (Input.GetMouseButtonDown(0) && cell != mine.Cell)
            {
                // Click an enemy: attack when the weapon reaches, otherwise walk into
                // reach (CmdMoveTo stops adjacent to occupied cells).
                if (enemy != null && combat.ActionLeft && InWeaponRange(mine.Cell, cell))
                {
                    CombatFx.Instance?.ShowTargetMarker(enemy.Visual);
                    combat.CmdAttack(enemy.Id);
                }
                else if (combat.MoveLeft >= 5)
                {
                    if (enemy != null) CombatFx.Instance?.ShowTargetMarker(enemy.Visual);
                    combat.CmdMoveTo(cell.x, cell.y);
                }
            }
        }

        private bool InWeaponRange(Vector2Int from, Vector2Int to)
        {
            int reach = 5;
            var holder = LocalHolder();
            var weapon = holder != null ? GameItem.Get(holder.WeaponId.Value) : null;
            if (weapon != null && weapon.RangeFeet > 0) reach = weapon.RangeFeet;
            int distFeet = Mathf.Max(Mathf.Abs(to.x - from.x), Mathf.Abs(to.y - from.y)) * 5;
            return distFeet <= reach;
        }

        // ---------- movement-range overlay (blue cells, per the mock) ----------

        private readonly List<GameObject> _rangeQuads = new List<GameObject>();
        private Material _rangeMat;
        private (string unit, int move, Vector2Int cell) _rangeKey;

        private void UpdateRangeOverlay(CombatManager combat, bool show)
        {
            if (!show)
            {
                if (_rangeQuads.Count > 0)
                {
                    foreach (var q in _rangeQuads) if (q != null) Destroy(q);
                    _rangeQuads.Clear();
                    _rangeKey = default;
                }
                return;
            }
            var mine = combat.MyUnit;
            var key = (combat.ActiveUnitId, combat.MoveLeft, mine.Cell);
            if (key == _rangeKey) return;
            _rangeKey = key;
            foreach (var q in _rangeQuads) if (q != null) Destroy(q);
            _rangeQuads.Clear();

            if (_rangeMat == null)
            {
                var src = Resources.Load<Material>("Fx/M_GridOverlay");
                if (src != null) _rangeMat = new Material(src);
                else
                {
                    var temp = GameObject.CreatePrimitive(PrimitiveType.Quad);
                    _rangeMat = new Material(temp.GetComponent<Renderer>().sharedMaterial);
                    Destroy(temp);
                }
                _rangeMat.color = new Color(0.3f, 0.5f, 1f, 0.4f);
            }

            int steps = combat.MoveLeft / 5;
            for (int dx = -steps; dx <= steps; dx++)
                for (int dy = -steps; dy <= steps; dy++)
                {
                    if (dx == 0 && dy == 0) continue;
                    var cell = new Vector2Int(mine.Cell.x + dx, mine.Cell.y + dy);
                    if (Mathf.Abs(cell.x) > 8 || Mathf.Abs(cell.y) > 8) continue;
                    if (combat.ClientUnits.Any(u => !u.Dead && u.Cell == cell)) continue;
                    var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
                    Destroy(quad.GetComponent<Collider>());
                    quad.name = "MoveRange";
                    quad.transform.position = combat.GridOrigin + new Vector3(
                        cell.x * CombatManager.CellSize, 0.045f, cell.y * CombatManager.CellSize);
                    quad.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
                    quad.transform.localScale = Vector3.one * (CombatManager.CellSize * 0.9f);
                    quad.GetComponent<Renderer>().sharedMaterial = _rangeMat;
                    _rangeQuads.Add(quad);
                }
        }

        /// <summary>The HUD rects from OnGUI, in Ui-scaled space — clicks inside them
        /// are button presses, not move orders.</summary>
        private bool IsMouseOverHud(CombatManager combat)
        {
            var m = new Vector2(Input.mousePosition.x / Ui.Scale,
                (Screen.height - Input.mousePosition.y) / Ui.Scale);
            if (InitiativeRect(combat).Contains(m)) return true;
            if (new Rect(12, Ui.H - 174, 384, 162).Contains(m)) return true;      // log
            if (new Rect(12, Ui.H - 282, 264, 100).Contains(m)) return true;      // player card
            if (HotBar.BarRect.Contains(m)) return true;                          // hotbar
            if (combat.IsMyTurn && _actionsRect.Contains(m)) return true;         // status strip
            return false;
        }

        private Material _hoverMat;
        private Color _hoverColor;

        private void ShowHover(bool on)
        {
            if (_hoverMarker == null)
            {
                if (!on) return;
                _hoverMarker = GameObject.CreatePrimitive(PrimitiveType.Quad);
                Destroy(_hoverMarker.GetComponent<Collider>());
                _hoverMarker.name = "MoveHoverMarker";
                _hoverMarker.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
                _hoverMarker.transform.localScale =
                    Vector3.one * (CombatManager.CellSize * 0.9f);
                _hoverMat = _hoverMarker.GetComponent<Renderer>().material;
                _hoverMat.EnableKeyword("_EMISSION");
                SetHoverColor(new Color(0.45f, 0.9f, 1f));
            }
            _hoverMarker.SetActive(on);
        }

        private void SetHoverColor(Color color)
        {
            if (_hoverMat == null || _hoverColor == color) return;
            _hoverColor = color;
            _hoverMat.color = color;
            _hoverMat.SetColor("_EmissionColor", color * 0.9f);
        }

        private void OnGUI()
        {
            Ui.Begin();
            var combat = CombatManager.Instance;
            if (combat == null) return;

            DrawOutcomeBanner(combat);
            if (combat.ClientUnits.Count == 0) return;

            DrawInitiative(combat);
            DrawLog(combat);
            DrawMyCard(combat);
            if (combat.IsMyTurn) DrawActions(combat);
            else _mode = Mode.Root;
        }

        /// <summary>Persistent player card above the log (mock: portrait card with HP/MP
        /// bars): name in serif gold, red HP bar, remaining spell slots as blue pips.</summary>
        private void DrawMyCard(CombatManager combat)
        {
            var mine = combat.MyUnit;
            if (mine == null) return;
            GUILayout.BeginArea(new Rect(12, Ui.H - 282, 264, 100), Theme.PanelStyle);
            GUILayout.Label(mine.Name +
                (combat.IsMyTurn ? "   <size=11><color=#d0c5af>— your turn</color></size>" : ""),
                Theme.Header);
            var row = GUILayoutUtility.GetRect(232, 14);
            GUI.Label(new Rect(row.x, row.y - 2, 26, 14), "HP", Theme.Caps);
            Theme.Bar(new Rect(row.x + 28, row.y + 1, 142, 9),
                mine.MaxHp > 0 ? (float)mine.Hp / mine.MaxHp : 0f, Theme.HpRed);
            GUI.Label(new Rect(row.x + 176, row.y - 2, 60, 14),
                $"{mine.Hp}/{mine.MaxHp}", Theme.Caps);
            int slots = combat.MySlots.Sum();
            if (slots > 0)
                GUILayout.Label($"SLOTS  <color=#3d6ff2>{new string('●', Mathf.Min(slots, 12))}</color>",
                    Theme.Caps);
            if (mine.Down)
                GUILayout.Label("<color=#c62828><b>DOWN — roll death saves</b></color>", Theme.Caps);
            GUILayout.EndArea();
        }

        /// <summary>Big centered VICTORY / DEFEATED card, shown for a few seconds after
        /// the fight resolves (fades near the end).</summary>
        private void DrawOutcomeBanner(CombatManager combat)
        {
            float remaining = combat.BannerUntil - Time.time;
            if (remaining <= 0f || combat.BannerTitle.Length == 0) return;

            float alpha = Mathf.Clamp01(remaining / 1.2f);   // fade out over the last bit
            var prevColor = GUI.color;
            GUI.color = new Color(1f, 1f, 1f, alpha);

            const float w = 460f;
            const float h = 150f;
            var rect = new Rect(Ui.W / 2f - w / 2f, Ui.H * 0.22f, w, h);
            GUI.Box(rect, GUIContent.none, Theme.PanelStyle);

            var titleStyle = new GUIStyle(Theme.HeaderBig)
            {
                alignment = TextAnchor.MiddleCenter
            };
            string color = combat.BannerVictory ? "#f2ca50" : "#ff7a6b";
            GUI.Label(new Rect(rect.x, rect.y + 12, rect.width, 46),
                $"<color={color}>{(combat.BannerVictory ? "⚔  " : "")}{combat.BannerTitle}</color>",
                titleStyle);

            var detailStyle = new GUIStyle(Theme.Body)
            {
                alignment = TextAnchor.UpperCenter,
                fontSize = 16
            };
            GUI.Label(new Rect(rect.x + 12, rect.y + 62, rect.width - 24, rect.height - 70),
                combat.BannerDetail, detailStyle);

            GUI.color = prevColor;
        }

        private Rect InitiativeRect(CombatManager combat) => new Rect(Ui.W - 262, 12, 250,
            Mathf.Min(52f + combat.ClientUnits.Count * 36f, Ui.H - 24f));

        private void DrawInitiative(CombatManager combat)
        {
            GUILayout.BeginArea(InitiativeRect(combat), Theme.PanelStyle);
            GUILayout.Label($"Round {combat.Round}", Theme.Header);
            foreach (var u in combat.ClientUnits)
            {
                bool active = u.Id == combat.ActiveUnitId;
                string nameColor = u.Dead ? "#6f6f6f"
                    : active ? "#f2ca50"
                    : u.IsPc ? "#b2c5ff" : "#ff9e9e";
                string state = u.Dead ? "  ✝" : u.Down ? "  (down)" : "";
                GUILayout.Label($"{(active ? "► " : "")}<color={nameColor}>{u.Name}</color>" +
                    $"<color=#d0c5af>{state}</color>", Theme.Body);
                if (!u.Dead)
                {
                    var row = GUILayoutUtility.GetRect(216, 9);
                    Theme.Bar(new Rect(row.x + 2, row.y, 168, 7),
                        u.MaxHp > 0 ? (float)u.Hp / u.MaxHp : 0f, Theme.HpRed);
                    GUI.Label(new Rect(row.x + 176, row.y - 4, 46, 14),
                        $"{u.Hp}/{u.MaxHp}", Theme.Caps);
                }
                GUILayout.Space(2);
            }
            GUILayout.EndArea();
        }

        /// <summary>Rich-text pass over server log lines: wounds red, healing green,
        /// misses muted — per the mock's colorized combat journal.</summary>
        private static string Colorize(string line)
        {
            if (line.Contains("misses") || line.Contains("miss ("))
                return $"<color=#8a8a8a>{line}</color>";
            if (line.Contains("CRITS"))
                return line.Replace("CRITS", "<b><color=#c62828>CRITS</color></b>");
            if (line.Contains("slain") || line.Contains("goes down")
                || line.Contains("has died") || line.Contains("destroyed"))
                return $"<color=#c62828>{line}</color>";
            if (line.Contains("restores") || line.Contains("back on their feet")
                || line.Contains("stable"))
                return $"<color=#2e7d32>{line}</color>";
            return line;
        }

        private void DrawLog(CombatManager combat)
        {
            GUILayout.BeginArea(new Rect(12, Ui.H - 174, 384, 162), Theme.PanelStyle);
            GUILayout.Label("Combat", Theme.Header);
            _logScroll = GUILayout.BeginScrollView(_logScroll);
            GUILayout.BeginVertical(Theme.ParchmentStyle);
            foreach (var line in combat.Log.Skip(Mathf.Max(0, combat.Log.Count - 30)))
                GUILayout.Label(Colorize(line), Theme.BodyInk);
            GUILayout.EndVertical();
            GUILayout.EndScrollView();
            GUILayout.EndArea();
            if (Event.current.type == EventType.Repaint)
                _logScroll.y = float.MaxValue;
        }

        private Rect _actionsRect;

        /// <summary>Slim status strip docked directly above the hotbar — the center of
        /// the screen stays clear so the movement grid is always clickable. Expands
        /// only while a target picker is open.</summary>
        private void DrawActions(CombatManager combat)
        {
            float h = _mode == Mode.Root ? 50f : 100f;
            _actionsRect = new Rect(Ui.W / 2f - 370f, Ui.H - 94f - h, 740f, h);
            GUILayout.BeginArea(_actionsRect, Theme.PanelStyle);
            GUILayout.BeginHorizontal();
            GUILayout.Label("Your Turn", Theme.Header, GUILayout.Width(96));
            string info = combat.LastRejection.Length > 0
                ? $"<color=#f2ca50>{combat.LastRejection}</color>"
                : $"<color=#d0c5af>move <b>{combat.MoveLeft} ft</b> · click square = walk · " +
                  "enemy = attack · Space = end turn" +
                  $" · action {(combat.ActionLeft ? "<color=#2e7d32>✔</color>" : "<color=#c62828>✘</color>")}" +
                  $" bonus {(combat.BonusLeft ? "<color=#2e7d32>✔</color>" : "<color=#c62828>✘</color>")}" +
                  "</color>";
            GUILayout.Label(info, Theme.Body);
            GUILayout.EndHorizontal();

            switch (_mode)
            {
                case Mode.PickAttackTarget: DrawTargets(combat, enemiesOnly: true,
                    id => { combat.CmdAttack(id); _mode = Mode.Root; }); break;
                case Mode.PickSpell: DrawSpells(combat); break;
                case Mode.PickSpellTarget:
                    bool friendly = _pendingSpell is "cure_wounds" or "healing_word" or "bless" or "shield";
                    DrawTargets(combat, enemiesOnly: !friendly,
                        id => { combat.CmdCast(_pendingSpell, id); _mode = Mode.Root; });
                    break;
            }
            GUILayout.EndArea();
        }

        private void DrawSpells(CombatManager combat)
        {
            var holder = LocalHolder();
            if (holder == null) { _mode = Mode.Root; return; }
            var known = holder.Class == CharacterClass.Wizard
                ? new[] { "fire_bolt", "magic_missile", "burning_hands", "sleep" }
                : new[] { "sacred_flame", "guiding_bolt", "cure_wounds", "healing_word", "bless" };
            int col = 0;
            GUILayout.BeginHorizontal();
            foreach (var id in known)
            {
                if (col == 3) { GUILayout.EndHorizontal(); GUILayout.BeginHorizontal(); col = 0; }
                col++;
                var spell = SpellLibrary.Get(id);
                bool usable = spell.Level == 0
                    ? combat.ActionLeft
                    : (spell.IsBonusAction ? combat.BonusLeft : combat.ActionLeft)
                      && combat.MySlots[0] + combat.MySlots[1] + combat.MySlots[2] > 0;
                GUI.enabled = usable;
                if (GUILayout.Button(WithIcon(id, spell.Name), GUILayout.Height(32)))
                {
                    _pendingSpell = id;
                    _mode = Mode.PickSpellTarget;
                }
            }
            GUI.enabled = true;
            if (GUILayout.Button("Back")) _mode = Mode.Root;
            GUILayout.EndHorizontal();
        }

        private void DrawTargets(CombatManager combat, bool enemiesOnly,
            System.Action<string> onPick)
        {
            int col = 0;
            GUILayout.BeginHorizontal();
            foreach (var u in combat.ClientUnits.Where(u => !u.Dead
                && (enemiesOnly ? !u.IsPc : u.IsPc)))
            {
                if (col == 3) { GUILayout.EndHorizontal(); GUILayout.BeginHorizontal(); col = 0; }
                col++;
                if (GUILayout.Button($"{u.Name} ({u.Hp})")) onPick(u.Id);
            }
            if (GUILayout.Button("Back")) _mode = Mode.Root;
            GUILayout.EndHorizontal();
        }

        private PlayerCharacterHolder LocalHolder()
        {
            var combat = CombatManager.Instance;
            var mine = combat?.MyUnit;
            if (mine?.Visual == null) return null;
            return mine.Visual.GetComponent<PlayerCharacterHolder>();
        }
    }
}
