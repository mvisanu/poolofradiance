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

        /// <summary>The HUD rects from OnGUI, in Ui-scaled space — clicks inside them are
        /// button presses, not move orders. These are the SAME Rect properties the panels
        /// draw with: hand-copied literals here drifted from the panels and let clicks
        /// fall through the HUD onto the grid.</summary>
        private bool IsMouseOverHud(CombatManager combat)
        {
            if (Ui.OpenPanel != Ui.Panel.None) return true;   // a screen is up: never move
            var m = Ui.Mouse;
            if (InitiativeRect(combat).Contains(m)) return true;
            if (LogRect.Contains(m)) return true;
            if (MyCardRect.Contains(m)) return true;
            if (HotBar.BarRect.Contains(m)) return true;
            if (MiniMap.MapRect.Contains(m)) return true;
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
                // Repainted, not born-with: the primitive's default material is built-in
                // Standard and renders magenta under URP in a build.
                _hoverMat = RuntimeArt.Paint(_hoverMarker,
                    new Color(0.45f, 0.9f, 1f, 0.55f), emission: 0.9f, glow: true);
                _hoverColor = new Color(0.45f, 0.9f, 1f, 0.55f);
            }
            _hoverMarker.SetActive(on);
        }

        private void SetHoverColor(Color color)
        {
            color.a = 0.55f;   // the marker is a translucent wash over the grid cell
            if (_hoverMat == null || _hoverColor == color) return;
            _hoverColor = color;
            RuntimeArt.Tint(_hoverMat, color);
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

        // ---------- HUD geometry (one definition per panel; IsMouseOverHud reuses these) ----------

        /// <summary>Combat log, bottom-left. Width and height give way on a small window
        /// rather than sliding off the edge.</summary>
        private static Rect LogRect
        {
            get
            {
                float w = Mathf.Clamp(Ui.W * 0.32f, 240f, 384f);
                float h = Mathf.Clamp(Ui.H * 0.26f, 110f, 162f);
                return new Rect(12f, Ui.H - 12f - h, w, h);
            }
        }

        /// <summary>Player card, stacked directly above the log.</summary>
        private static Rect MyCardRect
        {
            get
            {
                float w = Mathf.Min(264f, Ui.W * 0.28f);
                var log = LogRect;
                return new Rect(12f, log.y - 8f - 100f, Mathf.Max(w, 200f), 100f);
            }
        }

        /// <summary>Initiative order, top-right — docked BELOW the minimap (it used to be
        /// pinned to the top-right corner and drew straight through it) and capped so a
        /// nine-unit fight scrolls instead of running off the bottom of the screen.</summary>
        private static Rect InitiativeRect(CombatManager combat)
        {
            float w = Mathf.Clamp(Ui.W * 0.22f, 190f, 250f);
            float top = MiniMap.MapRect.yMax + 8f;
            float wanted = 44f + combat.ClientUnits.Count * 36f;
            float h = Mathf.Min(wanted, Ui.H - top - 12f);
            return new Rect(Ui.W - w - 12f, top, w, Mathf.Max(h, 80f));
        }

        /// <summary>Persistent player card above the log (mock: portrait card with HP/MP
        /// bars): name in serif gold, red HP bar, remaining spell slots as pips.</summary>
        private void DrawMyCard(CombatManager combat)
        {
            var mine = combat.MyUnit;
            if (mine == null) return;
            var rect = MyCardRect;
            GUILayout.BeginArea(rect, Theme.PanelStyle);
            GUILayout.Label(mine.Name +
                (combat.IsMyTurn ? "   <size=11><color=#d0c5af>— your turn</color></size>" : ""),
                Theme.Header);

            float inner = rect.width - 28f;
            var row = GUILayoutUtility.GetRect(inner, 14f);
            GUI.Label(new Rect(row.x, row.y - 2, 26, 14), "HP", Theme.Caps);
            float barW = Mathf.Max(60f, inner - 28f - 56f);
            Theme.Bar(new Rect(row.x + 28, row.y + 1, barW, 9),
                mine.MaxHp > 0 ? (float)mine.Hp / mine.MaxHp : 0f, Theme.HpRed);
            GUI.Label(new Rect(row.x + 32 + barW, row.y - 2, 56, 14),
                $"{mine.Hp}/{mine.MaxHp}", Theme.Caps);

            // Pips = spell slots still available (the client only knows what is left, not
            // the capacity, so every pip drawn is one you can actually spend).
            int slots = combat.MySlots.Sum();
            if (slots > 0)
                GUILayout.Label($"SLOTS  {Theme.Pips(slots, Mathf.Min(slots, 10))}", Theme.Caps);
            if (mine.Down)
                GUILayout.Label("<color=#ff8a80><b>DOWN — rolling death saves</b></color>",
                    Theme.Caps);
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

            var rect = Ui.Fit(460f, 150f);
            rect.y = Ui.H * 0.22f;
            GUI.Box(rect, GUIContent.none, Theme.PanelStyle);

            var titleStyle = new GUIStyle(Theme.HeaderBig)
            {
                alignment = TextAnchor.MiddleCenter
            };
            string color = combat.BannerVictory ? "#f2ca50" : "#ff7a6b";
            GUI.Label(new Rect(rect.x, rect.y + 12, rect.width, 46),
                $"<color={color}>{combat.BannerTitle}</color>", titleStyle);

            var detailStyle = new GUIStyle(Theme.Body)
            {
                alignment = TextAnchor.UpperCenter,
                fontSize = 16,
                wordWrap = true
            };
            GUI.Label(new Rect(rect.x + 12, rect.y + 62, rect.width - 24, rect.height - 70),
                combat.BannerDetail, detailStyle);

            GUI.color = prevColor;
        }

        private Vector2 _initScroll;

        private void DrawInitiative(CombatManager combat)
        {
            var rect = InitiativeRect(combat);
            GUILayout.BeginArea(rect, Theme.PanelStyle);
            GUILayout.Label($"Round {combat.Round}", Theme.Header);

            _initScroll = GUILayout.BeginScrollView(_initScroll);
            float barW = Mathf.Max(80f, rect.width - 28f - 52f);
            foreach (var u in combat.ClientUnits)
            {
                bool active = u.Id == combat.ActiveUnitId;
                string nameColor = u.Dead ? "#948a7c"
                    : active ? "#f2ca50"
                    : u.IsPc ? "#b2c5ff" : "#ff9e9e";
                // State in words, never a dingbat: the fonts have no ✝/► glyph and a
                // missing glyph renders as a tofu box.
                string state = u.Dead ? "  (slain)" : u.Down ? "  (down)" : "";
                GUILayout.Label($"{(active ? "> " : "")}<color={nameColor}>{u.Name}</color>" +
                    $"<color=#d0c5af>{state}</color>", Theme.Body);
                if (!u.Dead)
                {
                    var row = GUILayoutUtility.GetRect(barW + 48f, 9f);
                    Theme.Bar(new Rect(row.x + 2, row.y, barW, 7),
                        u.MaxHp > 0 ? (float)u.Hp / u.MaxHp : 0f, Theme.HpRed);
                    GUI.Label(new Rect(row.x + barW + 8f, row.y - 4, 46, 14),
                        $"{u.Hp}/{u.MaxHp}", Theme.Caps);
                }
                GUILayout.Space(2);
            }
            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        /// <summary>Rich-text pass over server log lines: wounds red, healing green,
        /// misses muted — per the mock's colorized combat journal.</summary>
        private static string Colorize(string line)
        {
            if (line.Contains("misses") || line.Contains("miss ("))
                return $"<color=#6b6257>{line}</color>";
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
            GUILayout.BeginArea(LogRect, Theme.PanelStyle);
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
            float w = Mathf.Min(740f, Ui.W - 24f);
            // Sits on the hotbar, wherever the hotbar ended up — it moves with the window.
            float barTop = HotBar.BarRect.height > 0f ? HotBar.BarRect.y : Ui.H - 82f;
            _actionsRect = new Rect(Ui.W / 2f - w / 2f, barTop - h - 6f, w, h);
            GUILayout.BeginArea(_actionsRect, Theme.PanelStyle);
            GUILayout.BeginHorizontal();
            GUILayout.Label("Your Turn", Theme.Header, GUILayout.Width(88));
            // The hint line is the thing that gets cut off first on a narrow window, so it
            // sheds detail rather than overflowing: essentials always, the rest if it fits.
            bool roomy = Ui.W > 900f;
            string info = combat.LastRejection.Length > 0
                ? $"<color=#f2ca50>{combat.LastRejection}</color>"
                : $"<color=#d0c5af>move <b>{combat.MoveLeft} ft</b> · click square = walk · " +
                  "enemy = attack · Space = end turn"
                  + (roomy ? " · WASD/middle-drag = pan, F = recenter" : "")
                  + $" · action {Theme.Ready(combat.ActionLeft)}"
                  + $" · bonus {Theme.Ready(combat.BonusLeft)}</color>";
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
            // One source of truth for who knows what: this used to fall back to the CLERIC
            // list for anyone who wasn't a wizard, so a Fighter was offered Cure Wounds.
            var known = KnownSpells(holder.Class);
            if (known.Length == 0) { _mode = Mode.Root; return; }
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
