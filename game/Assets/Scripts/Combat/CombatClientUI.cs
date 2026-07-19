using System.Collections.Generic;
using System.Linq;
using RadiantPool.Rules;
using UnityEngine;

namespace RadiantPool.Game
{
    /// <summary>Combat HUD: initiative, log, overhead enemy status, direct click-to-attack,
    /// individual spell target pickers, Dodge and End Turn.</summary>
    public class CombatClientUI : MonoBehaviour
    {
        public static CombatClientUI Instance { get; private set; }

        private enum Mode { Root, PickAttackTarget, PickSpellTarget }
        private Mode _mode = Mode.Root;
        private string _pendingSpell = "";
        private Vector2 _logScroll;
        private GameObject _hoverMarker;

        // Approach and strike: one left-click on an enemy is the whole attack, however far
        // away it stands. The click remembers the target, the walk happens, and the blow
        // lands the moment the fighter arrives. It used to take TWO clicks — one to walk
        // into reach, one to swing — and the second was the easiest thing in the game to
        // forget, so a turn ended having done nothing but shuffle a square.
        private string _autoTarget = "";
        private string _hudTarget = "";
        private Vector2Int _autoFrom;    // where we stood when the walk was ordered
        private float _autoUntil;        // give up if the walk never resolves (blocked path)
        private float _nextPartyScan;
        private readonly List<PlayerCharacterHolder> _cachedPartyHolders =
            new List<PlayerCharacterHolder>();

        private void Awake() => Instance = this;
        private void OnDestroy() { if (Instance == this) Instance = null; }

        /// <summary>HotBar hooks: Attack and individual spells open target pickers.</summary>
        public void PickAttack()
        {
            _autoTarget = "";
            _hudTarget = "";
            _pendingSpell = "";
            _mode = Mode.PickAttackTarget;
        }

        public void PickSpell(string spellId)
        {
            _autoTarget = "";
            _hudTarget = "";
            _pendingSpell = spellId;
            _mode = Mode.PickSpellTarget;
        }

        /// <summary>Shared target confirmation used by target buttons and unattended
        /// combat verification. It submits the same intent selected by the visible menu.</summary>
        public void PickTarget(string targetId)
        {
            var combat = CombatManager.Instance;
            if (combat == null || !combat.CanAcceptPlayerInput) return;
            var picked = combat.ClientUnits.FirstOrDefault(u => u.Id == targetId);
            _hudTarget = picked != null && !picked.IsPc ? picked.Id : "";
            if (_mode == Mode.PickAttackTarget)
            {
                var target = combat.ClientUnits.FirstOrDefault(u => u.Id == targetId);
                _mode = Mode.Root;
                if (target != null) ClickCell(target.Cell);
                return;
            }
            if (_mode == Mode.PickSpellTarget && _pendingSpell.Length > 0)
                combat.CmdCast(_pendingSpell, targetId);
            else
                return;
            _mode = Mode.Root;
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

        private static PlayerCharacterHolder _localPlayerHolder;

        public static PlayerCharacterHolder LocalPlayerHolder()
        {
            // HotBar asks every GUI frame. Cache the owned component instead of repeating
            // a scene-wide search throughout combat; Unity fake-null handles despawn here.
            if (_localPlayerHolder == null)
                _localPlayerHolder = Object.FindObjectsByType<PlayerCharacterHolder>(
                        FindObjectsSortMode.None).FirstOrDefault(p => p.IsOwner);
            return _localPlayerHolder;
        }

        private static readonly System.Collections.Generic.Dictionary<string, Texture2D>
            IconCache = new System.Collections.Generic.Dictionary<string, Texture2D>();

        public const int TargetShapeCount = 6;
        private static readonly Texture2D[] TargetShapeIcons = new Texture2D[TargetShapeCount];
        public int LastMonsterOverlayCount { get; private set; }
        public int LastDistinctTargetShapeCount { get; private set; }
        private GUIStyle _worldNameStyle;
        private GUIStyle _worldHpStyle;

        /// <summary>Generated geometry, not font glyphs: every encounter assigns enemies a
        /// deterministic triangle, square, circle, diamond, hexagon, or cross marker.</summary>
        public static Texture2D TargetShapeTexture(int shape)
        {
            shape = Mathf.Abs(shape) % TargetShapeCount;
            if (TargetShapeIcons[shape] != null) return TargetShapeIcons[shape];

            const int size = 24;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = $"TargetShape_{shape}",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave
            };
            var pixels = new Color[size * size];
            float center = (size - 1) * 0.5f;
            float radius = size * 0.48f;
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float nx = (x - center) / radius;
                float ny = (y - center) / radius;
                float ax = Mathf.Abs(nx);
                float ay = Mathf.Abs(ny);
                bool inside = shape switch
                {
                    0 => ny >= -0.82f && ny <= 0.88f
                         && ax <= (0.9f - ny) * 0.55f,
                    1 => ax <= 0.74f && ay <= 0.74f,
                    2 => nx * nx + ny * ny <= 0.58f,
                    3 => ax + ay <= 0.98f,
                    4 => ay <= 0.72f && ax <= 0.84f && ax + ay * 0.55f <= 1f,
                    _ => Mathf.Max(ax, ay) <= 0.78f && (ax <= 0.22f || ay <= 0.22f)
                };
                pixels[y * size + x] = inside ? Color.white : Color.clear;
            }
            tex.SetPixels(pixels);
            tex.Apply(false, true);
            TargetShapeIcons[shape] = tex;
            return tex;
        }

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

        /// <summary>Click-to-move: hover a grid cell to highlight it, click to walk
        /// there (the server validates every step). Active only on your turn, outside
        /// the target pickers, and away from the HUD boxes.</summary>
        private void Update()
        {
            var combat = CombatManager.Instance;
            if (combat != null)
            {
                foreach (var unit in combat.ClientUnits)
                {
                    unit.DisplayHp = Mathf.MoveTowards(unit.DisplayHp, unit.Hp,
                        Mathf.Max(12f, unit.MaxHp * 2.5f) * Time.deltaTime);
                    if (Mathf.Abs(unit.DisplayHp - unit.Hp) < 0.01f)
                        unit.DisplayHp = unit.Hp;
                }
            }
            var mine = combat?.MyUnit;
            bool acting = combat != null && combat.CanAcceptPlayerInput && _mode == Mode.Root
                          && mine is { Down: false, Dead: false };

            if (combat != null && combat.CanAcceptPlayerInput && !Ui.Typing && !Ui.PanelOpen)
            {
                if (Input.GetKeyDown(KeyCode.A)) PickAttack();
                if (Input.GetKeyDown(KeyCode.Backspace)) _mode = Mode.Root;
            }

            // A walk ordered by an earlier click may have arrived: swing. This is deliberately
            // OUTSIDE the camera check below — the blow has to land in a headless run too
            // (-attacktest), where there is nothing to raycast against.
            if (acting) TickAutoAttack(combat, mine); else _autoTarget = "";

            bool interactive = acting && Camera.main != null;
            UpdateRangeOverlay(combat, interactive && combat.MoveLeft >= 5);
            if (!interactive) { ShowHover(false); return; }

            // Space / Enter: end the turn without touching the mouse.
            if (Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.Return))
            {
                _autoTarget = "";
                combat.CmdEndTurn();
                return;
            }

            // Prefer the monster's rendered bounds over ground projection. Projecting a
            // click on a tall torso directly to the floor can land a square behind it.
            if (Input.GetMouseButtonDown(0) && !IsMouseOverHud(combat))
            {
                string clickedId = EnemyIdAtScreenPoint(Input.mousePosition);
                var clickedEnemy = combat.ClientUnits.FirstOrDefault(u => u.Id == clickedId);
                if (clickedEnemy != null)
                {
                    ShowHover(true);
                    SetHoverColor(new Color(0.9f, 0.25f, 0.2f));
                    _hoverMarker.transform.position = combat.GridOrigin + new Vector3(
                        clickedEnemy.Cell.x * CombatManager.CellSize, 0.05f,
                        clickedEnemy.Cell.y * CombatManager.CellSize);
                    ClickCell(clickedEnemy.Cell);
                    return;
                }
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

            if (Input.GetMouseButtonDown(0)) ClickCell(cell);
        }

        /// <summary>The exact renderer/nameplate hit-test used by the real mouse path,
        /// exposed so unattended verification can prove clicks do not rely on ground-only
        /// projection.</summary>
        public string EnemyIdAtScreenPoint(Vector3 pointer)
        {
            var combat = CombatManager.Instance;
            var camera = Camera.main;
            if (combat == null || camera == null) return "";
            return EnemyUnderPointer(combat, camera, pointer)?.Id ?? "";
        }

        private static CombatManager.UnitView EnemyUnderPointer(CombatManager combat,
            Camera camera, Vector3 pointer)
        {
            CombatManager.UnitView best = null;
            float bestDepth = float.MaxValue;
            foreach (var unit in combat.ClientUnits)
            {
                if (unit.IsPc || unit.Dead || unit.Visual == null) continue;
                bool hasBounds = false;
                float minX = float.MaxValue, minY = float.MaxValue;
                float maxX = float.MinValue, maxY = float.MinValue;
                float depth = float.MaxValue;
                foreach (var renderer in unit.Visual.GetComponentsInChildren<Renderer>())
                {
                    if (renderer == null || !renderer.enabled) continue;
                    Bounds b = renderer.bounds;
                    for (int corner = 0; corner < 8; corner++)
                    {
                        var world = new Vector3(
                            (corner & 1) == 0 ? b.min.x : b.max.x,
                            (corner & 2) == 0 ? b.min.y : b.max.y,
                            (corner & 4) == 0 ? b.min.z : b.max.z);
                        Vector3 screen = camera.WorldToScreenPoint(world);
                        if (screen.z <= 0f) continue;
                        hasBounds = true;
                        minX = Mathf.Min(minX, screen.x);
                        maxX = Mathf.Max(maxX, screen.x);
                        minY = Mathf.Min(minY, screen.y);
                        maxY = Mathf.Max(maxY, screen.y);
                        depth = Mathf.Min(depth, screen.z);
                    }
                }
                if (!hasBounds) continue;

                float padding = 8f * Ui.Scale;
                var body = Rect.MinMaxRect(minX - padding, minY - padding,
                    maxX + padding, maxY + padding);
                Vector3 anchor = camera.WorldToScreenPoint(unit.Visual.position
                    + Vector3.up * unit.LabelHeight);
                float halfBar = 50f * Ui.Scale;
                var nameplate = Rect.MinMaxRect(anchor.x - halfBar,
                    anchor.y - 14f * Ui.Scale, anchor.x + halfBar,
                    anchor.y + 24f * Ui.Scale);
                if ((body.Contains(pointer) || nameplate.Contains(pointer)) && depth < bestDepth)
                {
                    best = unit;
                    bestDepth = depth;
                }
            }
            return best;
        }

        /// <summary>What a left-click on a square of the board MEANS — the one definition of
        /// it. Update calls it with the cell under the mouse; the `-attacktest` self-check
        /// calls it with an enemy's cell, so the test drives the very code the mouse does and
        /// cannot pass on a path the player never takes.</summary>
        public void ClickCell(Vector2Int cell)
        {
            var combat = CombatManager.Instance;
            var mine = combat?.MyUnit;
            if (combat == null || mine == null || !combat.CanAcceptPlayerInput
                || cell == mine.Cell) return;

            var enemy = combat.ClientUnits.FirstOrDefault(
                u => !u.IsPc && !u.Dead && u.Cell == cell);
            _hudTarget = enemy != null ? enemy.Id : "";

            // Click an enemy in reach: swing now. Out of reach: walk in (CmdMoveTo stops
            // adjacent to occupied cells) and REMEMBER it — TickAutoAttack lands the blow the
            // moment the walk arrives, so one click is the whole attack.
            if (enemy != null && combat.ActionLeft && InWeaponRange(mine.Cell, cell))
            {
                _autoTarget = "";
                CombatFx.Instance?.ShowTargetMarker(enemy.Visual);
                combat.CmdAttack(enemy.Id);
            }
            else if (combat.MoveLeft >= 5)
            {
                if (enemy != null)
                {
                    CombatFx.Instance?.ShowTargetMarker(enemy.Visual);
                    _autoTarget = combat.ActionLeft ? enemy.Id : "";
                    _autoFrom = mine.Cell;
                    _autoUntil = Time.time + 10f;
                }
                else _autoTarget = "";   // a ground click is a move order, nothing more
                combat.CmdMoveTo(cell.x, cell.y);
            }
        }

        /// <summary>The second half of a click on a distant enemy: once the walk the click
        /// ordered has actually landed us in reach, swing — without a second click.
        ///
        /// The unit's Cell updates the instant the server confirms the move, but the BODY is
        /// still gliding to it (CombatFx.GlideRoutine), so it waits for the visual to settle:
        /// attacking mid-stride reads as the sword swinging at empty air.</summary>
        private void TickAutoAttack(CombatManager combat, CombatManager.UnitView mine)
        {
            if (_autoTarget.Length == 0) return;

            var enemy = combat.ClientUnits.FirstOrDefault(
                u => u.Id == _autoTarget && !u.Dead);
            // It died on the way in (a companion got there first), we have no action left,
            // or the walk never resolved at all — a blocked path leaves the cell unchanged.
            if (enemy == null || !combat.ActionLeft || Time.time > _autoUntil)
            { _autoTarget = ""; return; }

            if (!InWeaponRange(mine.Cell, enemy.Cell))
            {
                // The move resolved and we are STILL short: this turn's movement was not
                // enough to reach it. Drop the order rather than leave it hanging.
                if (mine.Cell != _autoFrom) _autoTarget = "";
                return;
            }
            if (StillGliding(combat, mine)) return;

            _autoTarget = "";
            CombatFx.Instance?.ShowTargetMarker(enemy.Visual);
            combat.CmdAttack(enemy.Id);
        }

        /// <summary>True while the body is still catching up with the cell it already
        /// occupies on the server's board.</summary>
        private static bool StillGliding(CombatManager combat, CombatManager.UnitView u)
        {
            if (u.Visual == null) return false;
            Vector3 seat = combat.GridOrigin + new Vector3(
                u.Cell.x * CombatManager.CellSize, 0f, u.Cell.y * CombatManager.CellSize);
            Vector3 at = u.Visual.position;
            return new Vector2(at.x - seat.x, at.z - seat.z).sqrMagnitude > 0.25f;
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
            if (_logRect.Contains(m)) return true;
            if (PlayerFrameRect.Contains(m)) return true;
            if (PartyFramesRect.Contains(m)) return true;
            if (TargetFrameRect.Contains(m)) return true;
            if (QuestTracker.CardRect.Contains(m)) return true;
            if (HotBar.BarRect.Contains(m)) return true;
            if (MiniMap.MapRect.Contains(m)) return true;
            if (combat.IsMyTurn && _mode != Mode.Root && _actionsRect.Contains(m)) return true;
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

        private static void DrawTargetShape(Rect rect, int shape, Color color)
        {
            var tex = TargetShapeTexture(shape);
            Color old = GUI.color;
            GUI.color = new Color(0.05f, 0.04f, 0.035f, 0.95f);
            GUI.DrawTexture(new Rect(rect.x - 2f, rect.y - 2f,
                rect.width + 4f, rect.height + 4f), tex, ScaleMode.StretchToFill, true);
            GUI.color = color;
            GUI.DrawTexture(rect, tex, ScaleMode.StretchToFill, true);
            GUI.color = old;
        }

        /// <summary>Screen-space status stays legible at any camera angle while its anchor
        /// follows the actual rendered model height. These are passive draw calls, so a click
        /// through the marker still reaches ClickCell and performs the default attack.</summary>
        private void DrawMonsterOverlays(CombatManager combat)
        {
            LastMonsterOverlayCount = 0;
            LastDistinctTargetShapeCount = 0;
            var camera = Camera.main;
            if (camera == null) return;

            if (_worldNameStyle == null)
            {
                _worldNameStyle = new GUIStyle(Theme.Caps)
                {
                    alignment = TextAnchor.MiddleCenter,
                    fontSize = 10,
                    fontStyle = FontStyle.Bold,
                    // One line, clipped: inherited word wrap let long names fold into a
                    // second line that spilled above the plate and across the hp bar.
                    wordWrap = false,
                    clipping = TextClipping.Clip
                };
                _worldHpStyle = new GUIStyle(Theme.Caps)
                {
                    alignment = TextAnchor.MiddleCenter,
                    fontSize = 9,
                    fontStyle = FontStyle.Bold
                };
            }

            var shapes = new HashSet<int>();
            foreach (var unit in combat.ClientUnits)
            {
                if (unit.IsPc || unit.Dead || unit.Visual == null) continue;
                Vector3 screen = camera.WorldToScreenPoint(unit.Visual.position
                    + Vector3.up * unit.LabelHeight);
                if (screen.z <= 0f) continue;

                float x = screen.x / Ui.Scale;
                float y = (Screen.height - screen.y) / Ui.Scale;
                if (x < -80f || x > Ui.W + 80f || y < -40f || y > Ui.H + 40f) continue;

                // Plate hugs the name (capped): a fixed 96 clipped long names mid-word.
                float nameWidth = _worldNameStyle.CalcSize(new GUIContent(unit.Name)).x;
                float barWidth = Mathf.Clamp(nameWidth + 24f, 96f, 150f);
                // Rounded dark nameplate so the name and bar stay legible over bright scenery
                // (contrast > decoration); the current target gets a gold-lit rim behind it.
                bool targeted = unit.Id == _autoTarget;
                var plate = new Rect(x - barWidth * 0.5f - 4f, y - 22f, barWidth + 8f, 36f);
                if (targeted)
                    GUI.DrawTexture(new Rect(plate.x - 1.5f, plate.y - 1.5f,
                        plate.width + 3f, plate.height + 3f), Texture2D.whiteTexture,
                        ScaleMode.StretchToFill, true, 0, Theme.Gold, Vector4.zero, 7f);
                GUI.DrawTexture(plate, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0,
                    new Color(0.05f, 0.045f, 0.035f, 0.62f), Vector4.zero, 6f);

                var marker = new Rect(x - barWidth * 0.5f, y - 19f, 16f, 16f);
                Color markerColor = targeted
                    ? Theme.Gold : new Color(1f, 0.38f, 0.32f);
                DrawTargetShape(marker, unit.TargetShape, markerColor);
                GUI.Label(new Rect(x - barWidth * 0.5f + 19f, y - 20f,
                    barWidth - 19f, 17f), unit.Name, _worldNameStyle);

                var bar = new Rect(x - barWidth * 0.5f, y, barWidth, 12f);
                Theme.Bar(bar, unit.MaxHp > 0 ? unit.DisplayHp / unit.MaxHp : 0f,
                    Theme.HpRed);
                GUI.Label(bar, $"{unit.Hp}/{unit.MaxHp}", _worldHpStyle);
                LastMonsterOverlayCount++;
                shapes.Add(unit.TargetShape);
            }
            LastDistinctTargetShapeCount = shapes.Count;
        }

        private void OnGUI()
        {
            Ui.Begin();
            // A screen is up (journal, settings): it owns the display. The combat strip is
            // still THERE — IsMouseOverHud already refuses clicks while a panel is open — it
            // just doesn't draw through the panel.
            if (Ui.PanelOpen)
            {
                _logRect = default;
                _actionsRect = default;
                PartyFramesRect = default;
                TargetFrameRect = default;
                return;
            }
            var holder = LocalPlayerHolder();
            if (holder != null) DrawUnitFrames(holder);
            var combat = CombatManager.Instance;
            if (combat == null)
            {
                _logRect = default;
                _actionsRect = default;
                TargetFrameRect = default;
                return;
            }

            DrawOutcomeBanner(combat);
            if (combat.ClientUnits.Count == 0)
            {
                _logRect = default;
                _actionsRect = default;
                return;
            }

            DrawMonsterOverlays(combat);
            DrawInitiative(combat);
            bool choosingTarget = combat.IsMyTurn && _mode != Mode.Root;
            // Target selection is a short, high-priority interaction. Stow the journal until
            // the choice is made so it can never cover either the attack/spell picker or the
            // hotbar on narrow canvases.
            if (choosingTarget) _logRect = default;
            else DrawLog(combat);
            if (choosingTarget) DrawActions(combat);
            else
            {
                _actionsRect = default;
                if (!combat.IsMyTurn) _mode = Mode.Root;
            }
        }

        // ---------- HUD geometry (one definition per panel; IsMouseOverHud reuses these) ----------

        public static Rect PlayerFrameRect
        {
            get
            {
                // Compact WoW-style frame: the hp/max readout rides ON the bar, so the
                // frame hugs portrait + a ~60%-shorter bar instead of a wide strip.
                float w = Mathf.Clamp(Ui.W * 0.155f, 150f, 172f);
                return new Rect(12f, 12f, w, 64f);
            }
        }

        public static Rect PartyFramesRect { get; private set; }
        public static Rect TargetFrameRect { get; private set; }

        /// <summary>Combat log, bottom-left but docked above the complete hotbar bounds
        /// (including its XP strip). It used to anchor to Ui.H and physically cover the
        /// combat/spell row and utility row whenever the hotbar grew wide enough.</summary>
        private static Rect LogRect
        {
            get
            {
                float w = Mathf.Clamp(Ui.W * 0.40f, 250f, 460f);
                float wantedHeight = Mathf.Clamp(Ui.H * 0.26f, 110f, 162f);
                // BarRect is the one authoritative definition shared with click blocking.
                // The conservative fallback protects the first OnGUI frame, before HotBar
                // has published its layout (a wrapped bar plus health reaches ~163 units up).
                float barTop = HotBar.BarRect.height > 0f
                    ? HotBar.BarRect.yMin : Ui.H - 170f;
                float bottom = Mathf.Min(Ui.H - 12f, barTop - 8f);
                float h = Mathf.Min(wantedHeight, Mathf.Max(84f, bottom - 12f));
                return new Rect(12f, bottom - h, w, h);
            }
        }

        /// <summary>Initiative order, top-right — docked BELOW the minimap (it used to be
        /// pinned to the top-right corner and drew straight through it) and capped so a
        /// nine-unit fight scrolls instead of running off the bottom of the screen.</summary>
        public static Rect InitiativeRect(CombatManager combat)
        {
            float w = Mathf.Clamp(Ui.W * 0.22f, 190f, 250f);
            float top = MiniMap.MapRect.yMax + 8f;
            // Round title + two grouped section headers (PARTY / ENEMIES) + a row per unit.
            float wanted = 44f + 40f + combat.ClientUnits.Count * 36f;
            float h = Mathf.Min(wanted,
                Mathf.Min(Ui.H * 0.38f, Mathf.Max(80f, Ui.H - top - 130f)));
            // When the list scrolls, clip on a row boundary — a row cut mid-text reads
            // as a broken panel, not as scrollable content.
            if (h < wanted)
                h = 84f + Mathf.Max(1, Mathf.FloorToInt((h - 92f) / 36f)) * 36f + 8f;
            return new Rect(Ui.W - w - 12f, top, w, Mathf.Max(h, 80f));
        }

        private static readonly Texture2D[] ClassEmblems = new Texture2D[4];

        /// <summary>WoW-style unit cluster: the local character owns the top-left corner,
        /// party frames stack beneath it, and a selected hostile appears to its right.</summary>
        private void DrawUnitFrames(PlayerCharacterHolder holder)
        {
            var combat = CombatManager.Instance;
            var mine = combat != null && combat.InCombat.Value ? combat.MyUnit : null;
            int max = mine != null ? mine.MaxHp : holder.MaxHpSynced.Value;
            int hp = Mathf.Clamp(mine != null ? mine.Hp : holder.CurrentHpSynced.Value,
                0, Mathf.Max(0, max));
            bool inCombat = mine != null;
            DrawPlayerFrame(holder, inCombat ? mine.Name : holder.CharacterName.Value,
                hp, max, inCombat ? mine.Down : hp <= 0, inCombat);

            if (combat != null && combat.InCombat.Value && combat.ClientUnits.Count > 0)
            {
                var party = combat.ClientUnits.Where(u => u.IsPc && u != mine).Take(3).ToList();
                DrawCombatPartyFrames(party);
                var target = combat.ClientUnits.FirstOrDefault(
                    u => u.Id == _hudTarget && !u.IsPc && !u.Dead);
                DrawTargetFrame(target);
            }
            else
            {
                if (Time.time >= _nextPartyScan)
                {
                    _nextPartyScan = Time.time + 1f;
                    _cachedPartyHolders.Clear();
                    _cachedPartyHolders.AddRange(
                        FindObjectsByType<PlayerCharacterHolder>(FindObjectsSortMode.None));
                    _cachedPartyHolders.RemoveAll(p => p == null || p == holder);
                    _cachedPartyHolders.Sort((a, b) => string.Compare(
                        a.CharacterName.Value, b.CharacterName.Value,
                        System.StringComparison.Ordinal));
                    if (_cachedPartyHolders.Count > 3)
                        _cachedPartyHolders.RemoveRange(3, _cachedPartyHolders.Count - 3);
                }
                DrawWorldPartyFrames(_cachedPartyHolders);
                TargetFrameRect = default;
                _hudTarget = "";
            }
        }

        private void DrawPlayerFrame(PlayerCharacterHolder holder, string name, int hp, int max,
            bool down, bool inCombat)
        {
            var rect = PlayerFrameRect;
            GUI.Box(rect, GUIContent.none, Theme.PanelStyle);
            const float portrait = 44f;
            var portraitRect = new Rect(rect.x + 8f, rect.y + 10f, portrait, portrait);
            GUI.Box(portraitRect, GUIContent.none, Theme.SlotStyle);
            int classIndex = Mathf.Clamp(holder.ClassIndex.Value, 0, ClassEmblems.Length - 1);
            GUI.DrawTexture(new Rect(portraitRect.x + 5f, portraitRect.y + 5f,
                portrait - 10f, portrait - 10f), ClassEmblem(classIndex), ScaleMode.ScaleToFit, true);

            float x = portraitRect.xMax + 8f;
            float w = rect.xMax - x - 8f;
            int level = Mathf.Clamp(holder.LevelSynced.Value, 1, Progression.MaxLevel);
            string safeName = string.IsNullOrWhiteSpace(name) ? "Adventurer" : name;
            GUI.Label(new Rect(x, rect.y + 7f, w, 15f),
                $"<b>{safeName}</b>  <color=#cbbb9c>L{level}</color>", NameLineStyle());
            float fraction = max > 0 ? (float)hp / max : 0f;
            // The readout rides ON the bar (like the monster overheads), which is what
            // lets the bar run this short without clipping text off the frame edge.
            var hpRect = new Rect(x, rect.y + 25f, w, 13f);
            Theme.Bar(hpRect, fraction, Theme.HpGreen);
            string hpText = down ? "DOWN" : (max > 0 ? $"{hp}/{max}" : "syncing");
            GUI.Label(hpRect, hpText, BarReadoutStyle(down ? Theme.Crimson : Color.white));

            var cls = (CharacterClass)classIndex;
            int capacity = ClassData.SpellSlots(cls, level).Sum();
            if (capacity > 0)
            {
                var combat = CombatManager.Instance;
                int remaining = combat != null && combat.InCombat.Value
                    ? combat.MySlots.Sum() : holder.SlotsRemainingTotalSynced.Value;
                remaining = Mathf.Clamp(remaining, 0, capacity);
                var resource = new Rect(x, rect.y + 42f, w, 11f);
                Theme.Bar(resource, (float)remaining / capacity, Theme.MpBlue);
                GUI.Label(resource, $"slots {remaining}/{capacity}",
                    BarReadoutStyle(Color.white, 8));
            }
        }

        private static GUIStyle _nameLineStyle;
        private static GUIStyle _barReadoutStyle;

        private static GUIStyle NameLineStyle()
        {
            if (_nameLineStyle == null)
                _nameLineStyle = new GUIStyle(Theme.Body)
                    { fontSize = 12, wordWrap = false, clipping = TextClipping.Clip };
            return _nameLineStyle;
        }

        /// <summary>Centred mini-readout drawn over a Theme.Bar, monster-overhead style.</summary>
        private static GUIStyle BarReadoutStyle(Color color, int fontSize = 9)
        {
            if (_barReadoutStyle == null)
                _barReadoutStyle = new GUIStyle(Theme.Caps)
                {
                    alignment = TextAnchor.MiddleCenter,
                    fontStyle = FontStyle.Bold,
                    wordWrap = false,
                    clipping = TextClipping.Clip
                };
            _barReadoutStyle.fontSize = fontSize;
            _barReadoutStyle.normal.textColor = color;
            return _barReadoutStyle;
        }

        private static void DrawCombatPartyFrames(IReadOnlyList<CombatManager.UnitView> party)
        {
            if (party.Count == 0) { PartyFramesRect = default; return; }
            PartyFramesRect = new Rect(PlayerFrameRect.x, PlayerFrameRect.yMax + 6f,
                PlayerFrameRect.width, party.Count * 36f + 8f);
            GUI.Box(PartyFramesRect, GUIContent.none, Theme.PanelStyle);
            for (int i = 0; i < party.Count; i++)
                DrawCompactPartyRow(PartyFramesRect, i, party[i].Name, party[i].Hp,
                    party[i].MaxHp, party[i].Down || party[i].Dead);
        }

        private static void DrawWorldPartyFrames(IReadOnlyList<PlayerCharacterHolder> party)
        {
            int liveCount = 0;
            for (int i = 0; i < party.Count; i++)
                if (party[i] != null) liveCount++;
            if (liveCount == 0) { PartyFramesRect = default; return; }
            PartyFramesRect = new Rect(PlayerFrameRect.x, PlayerFrameRect.yMax + 6f,
                PlayerFrameRect.width, liveCount * 36f + 8f);
            GUI.Box(PartyFramesRect, GUIContent.none, Theme.PanelStyle);
            int row = 0;
            for (int i = 0; i < party.Count; i++)
            {
                var member = party[i];
                if (member == null) continue;
                DrawCompactPartyRow(PartyFramesRect, row++, member.CharacterName.Value,
                    member.CurrentHpSynced.Value, member.MaxHpSynced.Value,
                    member.CurrentHpSynced.Value <= 0);
            }
        }

        private static void DrawCompactPartyRow(Rect group, int index, string name,
            int hp, int max, bool down)
        {
            float y = group.y + 5f + index * 36f;
            string label = string.IsNullOrWhiteSpace(name) ? "Party member" : name;
            GUI.Label(new Rect(group.x + 8f, y, group.width - 16f, 15f),
                down ? $"{label}  <color=#ff9e9e>down</color>" : label, Theme.Body);
            var bar = new Rect(group.x + 8f, y + 18f, group.width - 16f, 9f);
            Theme.Bar(bar, max > 0 ? (float)Mathf.Clamp(hp, 0, max) / max : 0f, Theme.HpGreen);
        }

        /// <summary>WoW-style target frame: mirror of the player frame directly to its
        /// right — bars on the left, the enemy's generated target-shape icon as the
        /// portrait on the right, so the frame pair reads player-vs-target.</summary>
        private void DrawTargetFrame(CombatManager.UnitView target)
        {
            if (target == null) { TargetFrameRect = default; return; }
            float x = PlayerFrameRect.xMax + 12f;
            // Never reach the minimap column, however narrow the window gets.
            float w = Mathf.Min(PlayerFrameRect.width,
                MiniMap.MapRect.xMin - 8f - x);
            if (w < 110f) { TargetFrameRect = default; return; }
            TargetFrameRect = new Rect(x, PlayerFrameRect.y, w, PlayerFrameRect.height);
            GUI.Box(TargetFrameRect, GUIContent.none, Theme.PanelStyle);

            const float portrait = 44f;
            var portraitRect = new Rect(TargetFrameRect.xMax - 8f - portrait,
                TargetFrameRect.y + 10f, portrait, portrait);
            GUI.Box(portraitRect, GUIContent.none, Theme.SlotStyle);
            // Same icon + colour language as the overhead plates: gold when this enemy
            // is the pending auto-attack target, hostile red otherwise.
            Color iconColor = target.Id == _autoTarget
                ? Theme.Gold : new Color(1f, 0.38f, 0.32f);
            DrawTargetShape(new Rect(portraitRect.x + 9f, portraitRect.y + 9f,
                portrait - 18f, portrait - 18f), target.TargetShape, iconColor);

            float tx = TargetFrameRect.x + 8f;
            float tw = portraitRect.x - 8f - tx;
            GUI.Label(new Rect(tx, TargetFrameRect.y + 7f, tw, 15f),
                $"<b>{target.Name}</b>", NameLineStyle());
            var hpRect = new Rect(tx, TargetFrameRect.y + 25f, tw, 13f);
            Theme.Bar(hpRect, target.MaxHp > 0 ? target.DisplayHp / target.MaxHp : 0f,
                Theme.HpRed);
            GUI.Label(hpRect, $"{target.Hp}/{target.MaxHp}",
                BarReadoutStyle(Color.white));
        }

        private static Texture2D ClassEmblem(int classIndex)
        {
            classIndex = Mathf.Clamp(classIndex, 0, ClassEmblems.Length - 1);
            if (ClassEmblems[classIndex] != null) return ClassEmblems[classIndex];
            const int size = 32;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
                { hideFlags = HideFlags.HideAndDontSave, filterMode = FilterMode.Bilinear };
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float cx = x - 15.5f, cy = y - 15.5f;
                    bool ink = classIndex switch
                    {
                        0 => Mathf.Abs(cx - cy) < 2.2f || Mathf.Abs(cx + cy) < 2.2f,
                        1 => cx * cx + cy * cy < 92f ||
                             (Mathf.Abs(cx) < 2f && Mathf.Abs(cy) < 14f),
                        2 => (Mathf.Abs(cx) < 3f && Mathf.Abs(cy) < 13f) ||
                             (Mathf.Abs(cy) < 3f && Mathf.Abs(cx) < 13f),
                        _ => Mathf.Abs(cx) + Mathf.Abs(cy) < 12f &&
                             !(Mathf.Abs(cx) < 3f && cy < -2f)
                    };
                    tex.SetPixel(x, y, ink ? Theme.Gold : Color.clear);
                }
            tex.Apply(false, true);
            ClassEmblems[classIndex] = tex;
            return tex;
        }

        /// <summary>Big centered VICTORY / DEFEATED card, shown for a few seconds after
        /// the fight resolves (fades near the end).</summary>
        private void DrawOutcomeBanner(CombatManager combat)
        {
            if (!combat.OutcomeOpen || combat.BannerTitle.Length == 0) return;

            var rect = Ui.Fit(460f, 200f);
            rect.y = Ui.H * 0.22f;
            GUI.Box(rect, GUIContent.none, Theme.PanelStyle);

            // Pack flourish: a soft glow lights the crest and title (gold for victory, a
            // cold red for defeat), an ornamental crest sits on the header, and a licensed
            // separator rules off the title from the reward/summary text below.
            var glowRect = new Rect(rect.center.x - 150f, rect.y - 6f, 300f, 96f);
            Color glowTint = combat.BannerVictory
                ? new Color(1f, 0.82f, 0.35f) : new Color(1f, 0.38f, 0.32f);
            var oldColor = GUI.color;
            GUI.color = glowTint;
            Theme.Glow(glowRect, combat.BannerVictory ? 0.55f : 0.4f);
            GUI.color = oldColor;
            // Wide filigree along the modal's top edge (the pack's General_Decoration reads as
            // a thin flourish, not a crest, so it frames the header rather than sitting in it).
            Theme.Decoration(new Rect(rect.x + 26f, rect.y + 6f, rect.width - 52f, 14f), 0.85f);

            var titleStyle = new GUIStyle(Theme.HeaderBig)
            {
                alignment = TextAnchor.MiddleCenter
            };
            string color = combat.BannerVictory ? "#f2ca50" : "#ff7a6b";
            GUI.Label(new Rect(rect.x, rect.y + 24, rect.width, 46),
                $"<color={color}>{combat.BannerTitle}</color>", titleStyle);
            Theme.Divider(new Rect(rect.x + 40f, rect.y + 70f, rect.width - 80f, 6f));

            var detailStyle = new GUIStyle(Theme.Body)
            {
                alignment = TextAnchor.UpperCenter,
                fontSize = 16,
                wordWrap = true
            };
            GUI.Label(new Rect(rect.x + 16, rect.y + 84, rect.width - 32, rect.height - 130),
                combat.BannerDetail, detailStyle);

            float buttonY = rect.yMax - 42f;
            if (combat.BannerVictory)
            {
                if (GUI.Button(new Rect(rect.x + 140f, buttonY, 180f, 30f),
                        "Continue", Theme.BtnPrimary))
                    combat.DismissOutcome();
            }
            else
            {
                if (GUI.Button(new Rect(rect.x + 38f, buttonY, 180f, 30f),
                        "Retry Battle", Theme.BtnPrimary))
                    combat.CmdRetryEncounter();
                if (GUI.Button(new Rect(rect.x + 242f, buttonY, 180f, 30f),
                        "Return to Havenrock"))
                    combat.DismissOutcome();
            }
        }

        private Vector2 _initScroll;

        private void DrawInitiative(CombatManager combat)
        {
            var rect = InitiativeRect(combat);
            GUILayout.BeginArea(rect, Theme.PanelStyle);
            GUILayout.Label($"Round {combat.Round}", Theme.Header);

            _initScroll = GUILayout.BeginScrollView(_initScroll);
            // Panel padding + a possible vertical scrollbar both eat viewport width;
            // budgeting only the padding is what clipped the hp readouts off the edge.
            float barW = Mathf.Max(70f, rect.width - 96f);
            // Two clearly separated rosters so allies and foes never read as one list:
            // your party in green, the enemy in red. The active unit still lights gold, so
            // whose turn it is stays obvious even though the order is grouped by side now.
            DrawInitiativeGroup(combat, "YOUR PARTY",
                combat.ClientUnits.Where(u => u.IsPc), Theme.HpGreen, "#8fd694", barW);
            DrawInitiativeGroup(combat, "ENEMIES",
                combat.ClientUnits.Where(u => !u.IsPc), Theme.HpRed, "#ff9e9e", barW);
            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        /// <summary>One team's roster inside the initiative list: a coloured section title,
        /// then a name + HP bar per unit. Bar colour encodes the side (green ally / red foe)
        /// so the two groups never blur together.</summary>
        private void DrawInitiativeGroup(CombatManager combat, string title,
            IEnumerable<CombatManager.UnitView> units, Color barColor, string teamColor,
            float barW)
        {
            var roster = units.ToList();
            if (roster.Count == 0) return;
            GUILayout.Space(3);
            GUILayout.Label($"<color={teamColor}><b>{title}</b></color>", Theme.Caps);
            for (int i = 0; i < roster.Count; i++)
            {
                var u = roster[i];
                bool active = u.Id == combat.ActiveUnitId;
                string nameColor = u.Dead ? "#948a7c" : active ? "#f2ca50" : teamColor;
                // State in words, never a dingbat: the fonts have no ✝/► glyph and a
                // missing glyph renders as a tofu box.
                string state = u.Dead ? "  (slain)" : u.Down ? "  (down)" : "";
                // Numbered within the group (1., 2., ...) so each side reads as its own
                // ordered roster; the "> " marker still flags whose turn it is.
                GUILayout.Label($"{(active ? "> " : "")}<color=#d0c5af>{i + 1}.</color> " +
                    $"<color={nameColor}>{u.Name}</color>" +
                    $"<color=#d0c5af>{state}</color>", Theme.Body);
                if (!u.Dead)
                {
                    var row = GUILayoutUtility.GetRect(barW + 48f, 9f);
                    Theme.Bar(new Rect(row.x + 2, row.y, barW, 7),
                        u.MaxHp > 0 ? u.DisplayHp / u.MaxHp : 0f, barColor);
                    GUI.Label(new Rect(row.x + barW + 8f, row.y - 4, 46, 14),
                        $"{u.Hp}/{u.MaxHp}", Theme.Caps);
                }
                GUILayout.Space(2);
            }
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
            _logRect = LogRect;
            GUILayout.BeginArea(_logRect, Theme.PanelStyle);
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
        private Rect _logRect;
        private Vector2 _actionsScroll;

        /// <summary>Read-only UI state for the unattended combat test. Keeping the test on
        /// the visible target-picker path prevents the Attack slot from silently regressing
        /// into a button that looks present but never submits an attack.</summary>
        public bool AttackPickerOpenForTest => _mode == Mode.PickAttackTarget;
        public Rect ActionPanelRectForTest => _actionsRect;
        public Rect CombatLogRectForTest => _logRect;

        /// <summary>Temporary target picker docked above the hotbar. The old permanent
        /// instruction/status strip is gone; height derives from target count so rows
        /// cannot spill beyond the panel.</summary>
        private void DrawActions(CombatManager combat)
        {
            CombatTargetType targetType;
            bool allowDowned;
            string title;
            if (_mode == Mode.PickAttackTarget)
            {
                targetType = CombatTargetType.Hostile;
                allowDowned = false;
                title = "Choose Attack Target";
            }
            else
            {
                var spell = SpellLibrary.Get(_pendingSpell);
                targetType = spell.TargetType;
                allowDowned = spell.AllowDownedTarget;
                title = $"Choose {spell.Name} Target";
            }

            var mine = combat.MyUnit;
            int targets = combat.ClientUnits.Count(u =>
                ClientTargetAllowed(mine, u, targetType, allowDowned));
            float w = Mathf.Min(620f, Ui.W - 24f);
            int columns = w >= 520f ? 4 : w >= 380f ? 3 : 2;
            int rows = Mathf.Max(1, Mathf.CeilToInt((targets + 1) / (float)columns));
            // UI7's framed panel consumes more vertical padding than the old flat skin.
            // Budget the chrome explicitly so a 30-unit target button is never reduced to
            // a clipped sliver; the scroll view still caps unusually large encounters.
            float wantedHeight = 82f + rows * 44f;
            float barTop = HotBar.BarRect.height > 0f ? HotBar.BarRect.y : Ui.H - 82f;
            float height = Mathf.Min(wantedHeight, Mathf.Max(88f, barTop - 24f));
            float y = Mathf.Max(12f, barTop - height - 6f);
            _actionsRect = new Rect(Ui.W / 2f - w / 2f, y, w, height);
            GUILayout.BeginArea(_actionsRect, Theme.PanelStyle);
            GUILayout.Label(title, Theme.Caps);
            _actionsScroll = GUILayout.BeginScrollView(_actionsScroll, false, false);
            float buttonWidth = Mathf.Max(90f,
                (w - 64f - (columns - 1) * 4f) / columns);
            DrawTargets(combat, targetType, allowDowned, columns, buttonWidth, PickTarget);
            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        private void DrawTargets(CombatManager combat, CombatTargetType targetType,
            bool allowDowned, int columns, float buttonWidth,
            System.Action<string> onPick)
        {
            var mine = combat.MyUnit;
            int col = 0;
            GUILayout.BeginHorizontal();
            foreach (var u in combat.ClientUnits.Where(u =>
                ClientTargetAllowed(mine, u, targetType, allowDowned)))
            {
                if (col == columns)
                {
                    GUILayout.EndHorizontal();
                    GUILayout.BeginHorizontal();
                    col = 0;
                }
                if (GUILayout.Button(new GUIContent($"{u.Name}\n{u.Hp}/{u.MaxHp} HP", u.Name),
                        GUILayout.Width(buttonWidth), GUILayout.Height(38f)))
                {
                    CombatFx.Instance?.ShowTargetMarker(u.Visual);
                    onPick(u.Id);
                }
                col++;
            }
            if (col == columns)
            {
                GUILayout.EndHorizontal();
                GUILayout.BeginHorizontal();
                col = 0;
            }
            if (GUILayout.Button(new GUIContent("Back", "Backspace"),
                    GUILayout.Width(buttonWidth), GUILayout.Height(38f)))
                _mode = Mode.Root;
            col++;
            while (col++ < columns) GUILayout.Space(buttonWidth);
            GUILayout.EndHorizontal();
        }

        private static bool ClientTargetAllowed(CombatManager.UnitView actor,
            CombatManager.UnitView target, CombatTargetType targetType, bool allowDowned)
        {
            if (actor == null || target == null || target.Dead
                || (target.Down && !allowDowned)) return false;
            bool same = actor.Id == target.Id;
            bool sameTeam = actor.IsPc == target.IsPc;
            return targetType switch
            {
                CombatTargetType.Hostile => !same && !sameTeam,
                CombatTargetType.Friendly => sameTeam,
                CombatTargetType.Self => same,
                CombatTargetType.AnyLiving => true,
                _ => false
            };
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
