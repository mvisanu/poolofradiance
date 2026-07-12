using System.Linq;
using RadiantPool.Rules;
using UnityEngine;

namespace RadiantPool.Game
{
    /// <summary>IMGUI combat HUD for the gray-box phase: initiative order with HP,
    /// combat log, and — on your turn — movement compass, attack/spell target pickers,
    /// Dodge and End Turn. Replaced by the themed UI at 3f.</summary>
    public class CombatClientUI : MonoBehaviour
    {
        private enum Mode { Root, PickAttackTarget, PickSpell, PickSpellTarget }
        private Mode _mode = Mode.Root;
        private string _pendingSpell = "";
        private Vector2 _logScroll;

        private void OnGUI()
        {
            Ui.Begin();
            var combat = CombatManager.Instance;
            if (combat == null) return;

            DrawOutcomeBanner(combat);
            if (combat.ClientUnits.Count == 0) return;

            DrawInitiative(combat);
            DrawLog(combat);
            if (combat.IsMyTurn) DrawActions(combat);
            else _mode = Mode.Root;
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
            GUI.Box(rect, GUIContent.none);

            var titleStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 34,
                fontStyle = FontStyle.Bold,
                richText = true
            };
            string color = combat.BannerVictory ? "#ffd75e" : "#ff7a6b";
            GUI.Label(new Rect(rect.x, rect.y + 12, rect.width, 46),
                $"<color={color}>{(combat.BannerVictory ? "⚔  " : "")}{combat.BannerTitle}</color>",
                titleStyle);

            var detailStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.UpperCenter,
                fontSize = 17
            };
            GUI.Label(new Rect(rect.x + 12, rect.y + 62, rect.width - 24, rect.height - 70),
                combat.BannerDetail, detailStyle);

            GUI.color = prevColor;
        }

        private void DrawInitiative(CombatManager combat)
        {
            GUILayout.BeginArea(new Rect(Ui.W - 250, 12, 238, 320), GUI.skin.box);
            GUILayout.Label($"— Round {combat.Round} —");
            foreach (var u in combat.ClientUnits)
            {
                string marker = u.Id == combat.ActiveUnitId ? "► " : "   ";
                string state = u.Dead ? " ✝" : u.Down ? " (down)" : "";
                var style = new GUIStyle(GUI.skin.label) { richText = true };
                string color = u.IsPc ? "#9ecbff" : "#ff9e9e";
                GUILayout.Label(
                    $"{marker}<color={color}>{u.Name}</color> {u.Hp}/{u.MaxHp}{state}", style);
            }
            GUILayout.EndArea();
        }

        private void DrawLog(CombatManager combat)
        {
            GUILayout.BeginArea(new Rect(12, Ui.H - 190, 520, 178), GUI.skin.box);
            _logScroll = GUILayout.BeginScrollView(_logScroll);
            foreach (var line in combat.Log.Skip(Mathf.Max(0, combat.Log.Count - 30)))
                GUILayout.Label(line);
            GUILayout.EndScrollView();
            GUILayout.EndArea();
            if (Event.current.type == EventType.Repaint)
                _logScroll.y = float.MaxValue;
        }

        private void DrawActions(CombatManager combat)
        {
            GUILayout.BeginArea(new Rect(Ui.W / 2f - 220, Ui.H - 330, 440, 128),
                GUI.skin.box);
            GUILayout.Label($"YOUR TURN — move {combat.MoveLeft} ft, " +
                $"action {(combat.ActionLeft ? "✔" : "✘")}, bonus {(combat.BonusLeft ? "✔" : "✘")}" +
                (combat.MySlots.Any(s => s > 0)
                    ? $"   slots {string.Join("/", combat.MySlots)}" : ""));
            if (combat.LastRejection.Length > 0)
            {
                var warn = new GUIStyle(GUI.skin.label) { richText = true };
                GUILayout.Label($"<color=#ffcc66>{combat.LastRejection}</color>", warn);
            }

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

            // Movement compass, bottom center.
            var mine = combat.MyUnit;
            if (mine != null && !mine.Down && combat.MoveLeft >= 5)
            {
                GUILayout.BeginArea(new Rect(Ui.W / 2f - 220, Ui.H - 196, 200, 120),
                    GUI.skin.box);
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("NW")) combat.CmdMove(-1, 1);
                if (GUILayout.Button("N")) combat.CmdMove(0, 1);
                if (GUILayout.Button("NE")) combat.CmdMove(1, 1);
                GUILayout.EndHorizontal();
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("W")) combat.CmdMove(-1, 0);
                GUILayout.Label("move", new GUIStyle(GUI.skin.label)
                    { alignment = TextAnchor.MiddleCenter }, GUILayout.Width(60));
                if (GUILayout.Button("E")) combat.CmdMove(1, 0);
                GUILayout.EndHorizontal();
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("SW")) combat.CmdMove(-1, -1);
                if (GUILayout.Button("S")) combat.CmdMove(0, -1);
                if (GUILayout.Button("SE")) combat.CmdMove(1, -1);
                GUILayout.EndHorizontal();
                GUILayout.EndArea();
            }
        }

        private void DrawRoot(CombatManager combat)
        {
            GUILayout.BeginHorizontal();
            GUI.enabled = combat.ActionLeft;
            if (GUILayout.Button("Attack")) _mode = Mode.PickAttackTarget;
            var holder = LocalHolder();
            bool caster = holder != null &&
                (holder.Class == CharacterClass.Wizard || holder.Class == CharacterClass.Cleric);
            GUI.enabled = caster && (combat.ActionLeft || combat.BonusLeft);
            if (GUILayout.Button("Cast")) _mode = Mode.PickSpell;
            GUI.enabled = combat.ActionLeft;
            if (GUILayout.Button("Dodge")) combat.CmdDodge();
            GUI.enabled = true;
            if (GUILayout.Button("End Turn")) combat.CmdEndTurn();
            GUILayout.EndHorizontal();
        }

        private void DrawSpells(CombatManager combat)
        {
            var holder = LocalHolder();
            if (holder == null) { _mode = Mode.Root; return; }
            var known = holder.Class == CharacterClass.Wizard
                ? new[] { "fire_bolt", "magic_missile", "burning_hands", "sleep" }
                : new[] { "sacred_flame", "guiding_bolt", "cure_wounds", "healing_word", "bless" };
            GUILayout.BeginHorizontal();
            foreach (var id in known)
            {
                var spell = SpellLibrary.Get(id);
                bool usable = spell.Level == 0
                    ? combat.ActionLeft
                    : (spell.IsBonusAction ? combat.BonusLeft : combat.ActionLeft)
                      && combat.MySlots[0] + combat.MySlots[1] + combat.MySlots[2] > 0;
                GUI.enabled = usable;
                if (GUILayout.Button(spell.Name))
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
            GUILayout.BeginHorizontal();
            foreach (var u in combat.ClientUnits.Where(u => !u.Dead
                && (enemiesOnly ? !u.IsPc : u.IsPc)))
            {
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
