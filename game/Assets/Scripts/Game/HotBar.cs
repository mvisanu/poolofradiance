using System.Linq;
using RadiantPool.Rules;
using UnityEngine;

namespace RadiantPool.Game
{
    /// <summary>Persistent bottom action bar (classic MMO hotbar): attack/dodge/spell
    /// slots while in combat, plus potion, inventory, journal, and settings buttons at
    /// all times. Icon slots use Resources/SpellIcons; combat clicks delegate to
    /// CombatClientUI's target pickers.</summary>
    public class HotBar : MonoBehaviour
    {
        /// <summary>This frame's bar rect in Ui space — CombatClientUI excludes it from
        /// click-to-move.</summary>
        public static Rect BarRect { get; private set; }

        private const float Slot = 46f;
        private const float SlotH = 44f;

        private static GUIContent IconOnly(string id, string fallback)
        {
            var tex = CombatClientUI.Icon(id);
            return tex != null ? new GUIContent(tex, fallback) : new GUIContent(fallback);
        }

        private void OnGUI()
        {
            Ui.Begin();
            var holder = CombatClientUI.LocalPlayerHolder();
            var director = GameDirector.Instance;
            if (holder == null || director == null) { BarRect = default; return; }

            var combat = CombatManager.Instance;
            bool inCombat = combat != null && combat.InCombat.Value;
            bool myTurn = inCombat && combat.IsMyTurn;
            var cls = (CharacterClass)Mathf.Max(0, holder.ClassIndex.Value);
            string[] spells = inCombat ? CombatClientUI.KnownSpells(cls)
                : System.Array.Empty<string>();

            int combatSlots = inCombat ? 3 + spells.Length : 0;   // attack, dodge, end
            int utilSlots = 4;                                     // potion, bag, journal, cog
            float w = (combatSlots + utilSlots) * (Slot + 4f) + (inCombat ? 14f : 0f) + 28f;
            var rect = new Rect(Ui.W / 2f - w / 2f, Ui.H - SlotH - 22f, w, SlotH + 16f);
            BarRect = rect;

            GUILayout.BeginArea(rect, Theme.PanelStyle);
            GUILayout.BeginHorizontal();

            if (inCombat)
            {
                GUI.enabled = myTurn && combat.ActionLeft;
                if (Btn("attack", "Attack")) CombatClientUI.Instance?.PickAttack();
                if (Btn("dodge", "Dodge")) combat.CmdDodge();

                foreach (var id in spells)
                {
                    var spell = SpellLibrary.Get(id);
                    bool usable = myTurn && (spell.Level == 0
                        ? combat.ActionLeft
                        : (spell.IsBonusAction ? combat.BonusLeft : combat.ActionLeft)
                          && combat.MySlots.Sum() > 0);
                    GUI.enabled = usable;
                    if (Btn(id, spell.Name)) CombatClientUI.Instance?.PickSpell(id);
                }

                GUI.enabled = myTurn;
                if (Btn("end_turn", "End Turn (Space)")) combat.CmdEndTurn();
                GUI.enabled = true;
                GUILayout.Space(14f);
            }

            int potions = director.Stash.Count(s => s == "potion_healing");
            GUI.enabled = potions > 0 && !inCombat;
            if (GUILayout.Button(new GUIContent(CombatClientUI.Icon("potion"),
                    $"Drink Potion of Healing ({potions} left)"),
                    GUILayout.Width(Slot), GUILayout.Height(SlotH)))
                director.CmdUsePotion();
            GUI.enabled = true;

            if (Btn("bag", "Inventory (I)")) InventoryUI.Instance?.Toggle();
            if (Btn("journal", "Journal (J)")) director.ToggleJournal();
            if (Btn("settings", "Settings (Esc)")) SettingsMenu.Instance?.Toggle();

            GUILayout.EndHorizontal();
            GUILayout.EndArea();

            // Potion count badge + hover tooltip line above the bar.
            if (potions > 0)
                GUI.Label(new Rect(rect.x + (inCombat ? combatSlots * (Slot + 4f) + 28f : 14f)
                        + Slot - 16f, rect.y + SlotH - 10f, 30f, 16f),
                    $"<b><color=#f2ca50>{potions}</color></b>", Theme.Caps);
            if (GUI.tooltip.Length > 0)
            {
                var tipStyle = new GUIStyle(Theme.Caps)
                    { alignment = TextAnchor.MiddleCenter, wordWrap = false };
                GUI.Label(new Rect(rect.x, rect.y - 20f, rect.width, 18f),
                    GUI.tooltip, tipStyle);
            }
        }

        private static bool Btn(string icon, string tooltip) =>
            GUILayout.Button(IconOnly(icon, tooltip),
                GUILayout.Width(Slot), GUILayout.Height(SlotH));
    }
}
