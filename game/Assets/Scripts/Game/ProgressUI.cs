using System.Linq;
using RadiantPool.Rules;
using UnityEngine;

namespace RadiantPool.Game
{
    /// <summary>The character's progress, in two pieces.
    ///
    /// The XP BAR sits just above the hotbar and is always up: level, the fraction of the way
    /// to the next one, and the XP either side of it — so a kill or a quest visibly moves
    /// something. At level 5 (the campaign cap) it reads MAX rather than pretending.
    ///
    /// The LEVEL-UP SCREEN (L, or the button on the bar) spends the points a level-up granted:
    /// one per level, two at 4th (Progression's house rule). Each row says what the ability
    /// actually does for THIS character, because "+1 CHA" means nothing to a player deciding.
    /// The client only asks — GameDirector.CmdSpendAbilityPoint and the rules lib decide.</summary>
    public class ProgressUI : MonoBehaviour
    {
        /// <summary>The XP strip's rect this frame — CombatClientUI excludes it from
        /// click-to-move, the same way it does the hotbar and the minimap.</summary>
        public static Rect XpRect { get; private set; }

        private static readonly Ability[] Order =
            { Ability.Str, Ability.Dex, Ability.Con, Ability.Int, Ability.Wis, Ability.Cha };

        private PlayerCharacterHolder Me() =>
            FindObjectsByType<PlayerCharacterHolder>(FindObjectsSortMode.None)
                .FirstOrDefault(p => p.IsOwner);

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.L) && !Ui.Typing) Ui.Toggle(Ui.Panel.LevelUp);
            if (CombatManager.Instance != null && CombatManager.Instance.InCombat.Value)
                Ui.Close(Ui.Panel.LevelUp);   // spend points between fights, not during one
        }

        private void OnGUI()
        {
            Ui.Begin();
            var me = Me();
            if (me == null || me.ClassIndex.Value < 0) { XpRect = default; return; }

            DrawXpBar(me);
            if (Ui.IsOpen(Ui.Panel.LevelUp)) DrawLevelUpPanel(me);
        }

        private void DrawXpBar(PlayerCharacterHolder me)
        {
            int level = Mathf.Clamp(me.LevelSynced.Value, 1, Progression.MaxLevel);
            int xp = me.XpSynced.Value;
            int points = me.PendingPointsSynced.Value;
            var (into, span, fraction) = Progression.Progress(level, xp);
            bool capped = level >= Progression.MaxLevel;

            // Docked to the hotbar, which moves with the window — never to a screen literal.
            var bar = HotBar.BarRect;
            float w = Mathf.Min(Mathf.Max(bar.width, 300f), Ui.W - 24f);
            float h = 34f;
            float y = (bar.height > 0f ? bar.y : Ui.H - 48f) - h - 4f;
            var rect = new Rect(Ui.W / 2f - w / 2f, y, w, h);
            XpRect = rect;

            GUILayout.BeginArea(rect, Theme.PanelStyle);
            GUILayout.BeginHorizontal();
            GUILayout.Label($"<color=#f2ca50><b>LV {level}</b></color>",
                new GUIStyle(Theme.Body) { richText = true, fontSize = 12 },
                GUILayout.Width(46));

            var track = GUILayoutUtility.GetRect(60f, 13f, GUILayout.ExpandWidth(true));
            track.y += 3f;
            Theme.Bar(track, capped ? 1f : fraction, Theme.Gold);

            GUILayout.Space(6);
            GUILayout.Label(capped
                    ? "<color=#cbbb9c>MAX</color>"
                    : $"<color=#cbbb9c>{into}/{span} ({Mathf.FloorToInt(fraction * 100f)}%)</color>",
                new GUIStyle(Theme.Body) { richText = true, fontSize = 11, wordWrap = false },
                GUILayout.Width(112));

            if (points > 0 && GUILayout.Button($"Level up! ({points})",
                    Theme.BtnPrimary, GUILayout.Width(96), GUILayout.Height(22)))
                Ui.Show(Ui.Panel.LevelUp);

            GUILayout.EndHorizontal();
            GUILayout.EndArea();
        }

        private void DrawLevelUpPanel(PlayerCharacterHolder me)
        {
            var director = GameDirector.Instance;
            if (director == null) return;
            int points = me.PendingPointsSynced.Value;

            GUILayout.BeginArea(Ui.FitTop(460f, 400f, top: 60f, bottomMargin: 110f),
                Theme.PanelStyle);
            GUILayout.BeginHorizontal();
            GUILayout.Label("Level Up", Theme.Header);
            GUILayout.FlexibleSpace();
            GUILayout.Label("<color=#d0c5af>L or Esc to close</color>", Theme.Body);
            if (GUILayout.Button("X", GUILayout.Width(28), GUILayout.Height(22)))
                Ui.Close(Ui.Panel.LevelUp);
            GUILayout.EndHorizontal();

            GUILayout.Label(points > 0
                    ? $"<color=#f2ca50><b>{points}</b></color> ability point" +
                      $"{(points == 1 ? "" : "s")} to spend. Every level grants one (two at 4th)."
                    : "<color=#cbbb9c>No points to spend — they arrive when you level.</color>",
                new GUIStyle(Theme.Body) { richText = true, wordWrap = true });

            GUILayout.Space(4);
            GUILayout.BeginVertical(Theme.ParchmentStyle);
            foreach (var a in Order)
            {
                int score = me.ScoreOf(a);
                int mod = Mathf.FloorToInt((score - 10) / 2f);
                bool capped = score >= Progression.MaxAbilityScore;

                GUILayout.BeginHorizontal();
                GUILayout.BeginVertical();
                GUILayout.Label(
                    $"<b>{a.ToString().ToUpperInvariant()} {score}</b> ({(mod >= 0 ? "+" : "")}{mod})",
                    new GUIStyle(Theme.BodyInk) { fontSize = 13, richText = true });
                GUILayout.Label(WhatItDoes(a, me), new GUIStyle(Theme.BodyInk)
                    { fontSize = 11, wordWrap = true });
                GUILayout.EndVertical();
                GUILayout.FlexibleSpace();

                GUI.enabled = points > 0 && !capped;
                if (GUILayout.Button(capped ? "at 20" : "+1",
                        GUILayout.Width(64), GUILayout.Height(26)))
                    director.CmdSpendAbilityPoint((int)a);
                GUI.enabled = true;
                GUILayout.EndHorizontal();
                GUILayout.Space(5);
            }
            GUILayout.EndVertical();
            GUILayout.EndArea();
        }

        /// <summary>What the point BUYS this character — an odd score buys nothing yet (SRD
        /// modifiers move every two points), and that is the single most confusing thing about
        /// spending one, so say it outright.</summary>
        private static string WhatItDoes(Ability a, PlayerCharacterHolder me)
        {
            bool evenNext = me.ScoreOf(a) % 2 == 1;   // odd now -> +1 completes the modifier
            string effect = a switch
            {
                Ability.Str => "melee attack and damage, and what heavy armour you can bear",
                Ability.Dex => "armour class in light armour, finesse and ranged attacks, initiative",
                Ability.Con => "hit points — retroactively, at every level you have",
                Ability.Int => "wizard spell attacks and save DC",
                Ability.Wis => "cleric spell attacks and save DC, and healing",
                _ => "persuasion and the party's standing"
            };
            return evenNext
                ? $"+1 raises the modifier: {effect}."
                : $"+1 alone changes nothing yet (modifiers move every 2). Then: {effect}.";
        }
    }
}
