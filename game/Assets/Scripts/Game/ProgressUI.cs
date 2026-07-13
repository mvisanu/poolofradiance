using System.Linq;
using RadiantPool.Rules;
using UnityEngine;

namespace RadiantPool.Game
{
    /// <summary>The character's progress, in two pieces.
    ///
    /// LEVEL AND XP live on the CHARACTER SHEET (I), not on the main screen: they say who you
    /// are, not what is happening, and the battlefield is what the main screen is for. The
    /// sheet's block is drawn by XpBlock below, so there is one definition of it — the level,
    /// the bar, the XP either side, MAX at the campaign cap.
    ///
    /// The LEVEL-UP SCREEN (L, or the button on the sheet) spends the points a level-up
    /// granted: one per level, two at 4th (Progression's house rule). Each row says what the
    /// ability actually does for THIS character, because "+1 CHA" means nothing to a player
    /// deciding. The client only asks — CmdSpendAbilityPoint and the rules lib decide.</summary>
    public class ProgressUI : MonoBehaviour
    {
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
            if (me == null || me.ClassIndex.Value < 0) return;
            if (Ui.IsOpen(Ui.Panel.LevelUp)) DrawLevelUpPanel(me);
        }

        /// <summary>Level + XP, drawn wherever the character sheet wants it (InventoryUI calls
        /// this). Kept here next to the level-up screen it feeds: one owner of what "progress"
        /// looks like, so the sheet and the screen can never disagree about it.</summary>
        public static void XpBlock(PlayerCharacterHolder me)
        {
            int level = Mathf.Clamp(me.LevelSynced.Value, 1, Progression.MaxLevel);
            int points = me.PendingPointsSynced.Value;
            var (into, span, fraction) = Progression.Progress(level, me.XpSynced.Value);
            bool capped = level >= Progression.MaxLevel;

            GUILayout.BeginHorizontal();
            GUILayout.Label($"<color=#f2ca50><b>LEVEL {level}</b></color>",
                new GUIStyle(Theme.Body) { richText = true, fontSize = 13 });
            GUILayout.FlexibleSpace();
            GUILayout.Label(capped
                    ? "<color=#cbbb9c>MAX</color>"
                    : $"<color=#cbbb9c>{into}/{span} XP " +
                      $"({Mathf.FloorToInt(fraction * 100f)}% to {level + 1})</color>",
                new GUIStyle(Theme.Body) { richText = true, fontSize = 11, wordWrap = false });
            GUILayout.EndHorizontal();

            var track = GUILayoutUtility.GetRect(60f, 12f, GUILayout.ExpandWidth(true));
            Theme.Bar(track, capped ? 1f : fraction, Theme.Gold);

            if (points > 0)
            {
                GUILayout.Space(4);
                // 24 px cropped the label's descenders clean off at this font — the button has
                // to be as tall as the words it carries.
                if (GUILayout.Button($"Level up: spend {points} point{(points == 1 ? "" : "s")} (L)",
                        Theme.BtnPrimary, GUILayout.Height(30)))
                    Ui.Show(Ui.Panel.LevelUp);
            }
            GUILayout.Space(6);
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
                int mod = me.ModOf(a);
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
