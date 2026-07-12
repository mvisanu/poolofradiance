using System.Linq;
using UnityEngine;

namespace RadiantPool.Game
{
    /// <summary>Inventory panel (I): your equipped gear with live stats, plus the party
    /// stash with Equip/Drink buttons. Equip requests go to the server, which enforces
    /// class rules and returns the swapped-out item to the stash.</summary>
    public class InventoryUI : MonoBehaviour
    {
        private bool _open;
        private Vector2 _scroll;

        private PlayerCharacterHolder LocalHolder() =>
            FindObjectsByType<PlayerCharacterHolder>(FindObjectsSortMode.None)
                .FirstOrDefault(p => p.IsOwner);

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.I)) _open = !_open;
            if (CombatManager.Instance != null && CombatManager.Instance.InCombat.Value)
                _open = false;
        }

        private void OnGUI()
        {
            Ui.Begin();
            if (!_open) return;
            var director = GameDirector.Instance;
            var holder = LocalHolder();
            if (director == null || holder == null) return;

            GUILayout.BeginArea(new Rect(Ui.W / 2f - 230, 60, 460, 420), GUI.skin.box);
            GUILayout.Label("<b>Inventory</b> (I to close)",
                new GUIStyle(GUI.skin.label) { richText = true });

            // Equipped gear.
            var weapon = GameItem.Get(holder.WeaponId.Value);
            var armor = GameItem.Get(holder.ArmorId.Value);
            GUILayout.Label($"Weapon:  {(weapon != null ? $"{weapon.Name} ({weapon.Damage})" : "—")}");
            GUILayout.Label($"Armor:   {(armor != null ? armor.Name : "none")}" +
                            $"{(holder.ShieldEquipped.Value ? "  + Shield" : "")}");
            GUILayout.Space(6);
            GUILayout.Label($"Party gold: {director.PartyGold.Value}");
            GUILayout.Label("<b>Party stash:</b>",
                new GUIStyle(GUI.skin.label) { richText = true });

            _scroll = GUILayout.BeginScrollView(_scroll, GUILayout.ExpandHeight(true));
            var groups = director.Stash.GroupBy(id => id).OrderBy(g => g.Key).ToList();
            if (groups.Count == 0)
                GUILayout.Label("(empty — loot the docks!)");
            foreach (var g in groups)
            {
                var item = GameItem.Get(g.Key);
                string label = item != null ? item.Name : g.Key.Replace('_', ' ');
                GUILayout.BeginHorizontal();
                GUILayout.Label($"{label} ×{g.Count()}", GUILayout.ExpandWidth(true));
                if (item != null && item.Slot is ItemSlot.Weapon or ItemSlot.Armor
                    or ItemSlot.Shield)
                {
                    bool usable = holder.Sheet == null   // client: Sheet is server-only
                        ? item.UsableBy((RadiantPool.Rules.CharacterClass)holder.ClassIndex.Value)
                        : item.UsableBy(holder.Sheet.Class);
                    GUI.enabled = usable;
                    if (GUILayout.Button(usable ? "Equip" : "Can't use", GUILayout.Width(90)))
                        director.CmdEquipItem(g.Key);
                    GUI.enabled = true;
                }
                else if (g.Key == "potion_healing")
                {
                    if (GUILayout.Button("Drink", GUILayout.Width(90)))
                        director.CmdUsePotion();
                }
                GUILayout.EndHorizontal();
            }
            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }
    }
}
