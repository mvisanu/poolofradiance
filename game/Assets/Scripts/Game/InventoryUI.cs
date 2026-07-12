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

            GUILayout.BeginArea(new Rect(Ui.W / 2f - 230, 60, 460, 420), Theme.PanelStyle);
            GUILayout.BeginHorizontal();
            GUILayout.Label("Inventory", Theme.Header);
            GUILayout.FlexibleSpace();
            GUILayout.Label("<color=#d0c5af>I to close</color>", Theme.Body);
            GUILayout.EndHorizontal();

            // Equipped gear.
            var weapon = GameItem.Get(holder.WeaponId.Value);
            var armor = GameItem.Get(holder.ArmorId.Value);
            GUILayout.Label("EQUIPPED", Theme.Caps);
            GUILayout.Label($"Weapon:  <b>{(weapon != null ? $"{weapon.Name} ({weapon.Damage})" : "—")}</b>",
                Theme.Body);
            GUILayout.Label($"Armor:   <b>{(armor != null ? armor.Name : "none")}" +
                            $"{(holder.ShieldEquipped.Value ? "  + Shield" : "")}</b>", Theme.Body);
            GUILayout.Space(6);
            GUILayout.Label($"<color=#f2ca50><b>{director.PartyGold.Value}</b> gold</color>",
                Theme.Body);
            GUILayout.Label("PARTY STASH", Theme.Caps);

            _scroll = GUILayout.BeginScrollView(_scroll, GUILayout.ExpandHeight(true));
            GUILayout.BeginVertical(Theme.ParchmentStyle);
            var groups = director.Stash.GroupBy(id => id).OrderBy(g => g.Key).ToList();
            if (groups.Count == 0)
                GUILayout.Label("(empty — loot the docks!)", Theme.BodyInk);
            foreach (var g in groups)
            {
                var item = GameItem.Get(g.Key);
                string label = item != null ? item.Name : g.Key.Replace('_', ' ');
                GUILayout.BeginHorizontal();
                GUILayout.Label($"{label} ×{g.Count()}", Theme.BodyInk, GUILayout.ExpandWidth(true));
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
            GUILayout.EndVertical();
            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }
    }
}
