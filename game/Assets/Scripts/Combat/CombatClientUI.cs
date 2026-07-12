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
        private enum Mode { Root, PickAttackTarget, PickSpell, PickSpellTarget }
        private Mode _mode = Mode.Root;
        private string _pendingSpell = "";
        private Vector2 _logScroll;
        private GameObject _hoverMarker;

        private static readonly System.Collections.Generic.Dictionary<string, Texture2D>
            IconCache = new System.Collections.Generic.Dictionary<string, Texture2D>();

        /// <summary>Action/spell icon from Resources/SpellIcons (game-icons.net, CC BY —
        /// see README). Swap the art by overwriting the same-named PNGs, e.g. with an
        /// imported Asset Store icon pack. Missing icons fall back to text-only.</summary>
        private static Texture2D Icon(string id)
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
            bool canMove = combat != null && combat.IsMyTurn && _mode == Mode.Root
                           && mine is { Down: false, Dead: false }
                           && combat.MoveLeft >= 5 && Camera.main != null;
            UpdateRangeOverlay(combat, canMove);
            if (!canMove) { ShowHover(false); return; }

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
            // Red over an enemy (walk into reach), blue-white otherwise — per the mock.
            bool enemyThere = combat.ClientUnits.Any(u => !u.IsPc && !u.Dead && u.Cell == cell);
            SetHoverColor(enemyThere
                ? new Color(0.9f, 0.25f, 0.2f) : new Color(0.45f, 0.9f, 1f));
            _hoverMarker.transform.position = combat.GridOrigin + new Vector3(
                cell.x * CombatManager.CellSize, 0.05f, cell.y * CombatManager.CellSize);

            if (Input.GetMouseButtonDown(0) && cell != mine.Cell)
                combat.CmdMoveTo(cell.x, cell.y);
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
            if (new Rect(12, Ui.H - 208, 520, 196).Contains(m)) return true;      // log
            if (new Rect(12, Ui.H - 318, 264, 102).Contains(m)) return true;      // player card
            if (combat.IsMyTurn
                && new Rect(Ui.W / 2f - 280, Ui.H - 336, 560, 134).Contains(m))    // actions
                return true;
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
            GUILayout.BeginArea(new Rect(12, Ui.H - 318, 264, 102), Theme.PanelStyle);
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
            GUILayout.BeginArea(new Rect(12, Ui.H - 208, 520, 196), Theme.PanelStyle);
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

        private void DrawActions(CombatManager combat)
        {
            GUILayout.BeginArea(new Rect(Ui.W / 2f - 280, Ui.H - 336, 560, 134),
                Theme.PanelStyle);
            GUILayout.BeginHorizontal();
            GUILayout.Label("Your Turn", Theme.Header, GUILayout.Width(104));
            GUILayout.Label($"<color=#d0c5af>move <b>{combat.MoveLeft} ft</b> (click a square)" +
                $"   action {(combat.ActionLeft ? "<color=#2e7d32>✔</color>" : "<color=#c62828>✘</color>")}" +
                $"   bonus {(combat.BonusLeft ? "<color=#2e7d32>✔</color>" : "<color=#c62828>✘</color>")}" +
                (combat.MySlots.Any(s => s > 0)
                    ? $"   slots <b>{string.Join("/", combat.MySlots)}</b>" : "") + "</color>",
                Theme.Body);
            GUILayout.EndHorizontal();
            if (combat.LastRejection.Length > 0)
                GUILayout.Label($"<color=#f2ca50>{combat.LastRejection}</color>", Theme.Body);

            switch (_mode)
            {
                case Mode.Root: DrawRoot(combat); break;
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

        private void DrawRoot(CombatManager combat)
        {
            GUILayout.BeginHorizontal();
            GUI.enabled = combat.ActionLeft;
            if (GUILayout.Button(WithIcon("attack", "Attack"), GUILayout.Height(34)))
                _mode = Mode.PickAttackTarget;
            var holder = LocalHolder();
            bool caster = holder != null &&
                (holder.Class == CharacterClass.Wizard || holder.Class == CharacterClass.Cleric);
            GUI.enabled = caster && (combat.ActionLeft || combat.BonusLeft);
            if (GUILayout.Button(WithIcon("cast", "Cast"), GUILayout.Height(34)))
                _mode = Mode.PickSpell;
            GUI.enabled = combat.ActionLeft;
            if (GUILayout.Button(WithIcon("dodge", "Dodge"), GUILayout.Height(34)))
                combat.CmdDodge();
            GUI.enabled = true;
            if (GUILayout.Button(WithIcon("end_turn", "End Turn"), GUILayout.Height(34)))
                combat.CmdEndTurn();
            GUILayout.EndHorizontal();
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
