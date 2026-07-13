using System.Linq;
using UnityEngine;

namespace RadiantPool.Game
{
    /// <summary>The Broken Anvil: buy weapon and armor upgrades into the party stash
    /// (equip afterwards from the inventory). Transactions resolve on the server via
    /// GameDirector.CmdBuyItem; prices come from GameDirector.SmithStock.</summary>
    public class SmithInteract : MonoBehaviour
    {
        public string VendorName = "The Broken Anvil";
        public float InteractRange = 3.5f;

        private bool _open;
        private Vector2 _scroll;

        private Transform LocalPlayer()
        {
            var holder = FindObjectsByType<PlayerCharacterHolder>(FindObjectsSortMode.None)
                .FirstOrDefault(p => p.IsOwner);
            return holder != null ? holder.transform : null;
        }

        private bool InRange()
        {
            var player = LocalPlayer();
            return player != null
                && Vector3.Distance(player.position, transform.position) <= InteractRange;
        }

        private void Update()
        {
            bool combat = CombatManager.Instance != null && CombatManager.Instance.InCombat.Value;
            if (combat) { _open = false; return; }
            if (Input.GetKeyDown(KeyCode.E) && InRange()) _open = !_open;
            if (_open && !InRange()) _open = false;
        }

        private void OnGUI()
        {
            Ui.Begin();
            if (CombatManager.Instance != null && CombatManager.Instance.InCombat.Value) return;
            var director = GameDirector.Instance;
            if (director == null) return;

            if (!_open && InRange())
            {
                Theme.DrawToast(Ui.W / 2f, 190f, $"[E] Browse {VendorName}");
                return;
            }
            if (!_open) return;

            GUILayout.BeginArea(Ui.Fit(420f, 420f), Theme.PanelStyle);
            GUILayout.Label($"{VendorName} — arms & armor", Theme.Header);
            GUILayout.Label($"<color=#f2ca50><b>{director.PartyGold.Value:N0}</b> gold</color>" +
                "   <color=#d0c5af>(bought gear goes to the stash — equip with I)</color>",
                Theme.Body);

            _scroll = GUILayout.BeginScrollView(_scroll, GUILayout.ExpandHeight(true));
            bool wroteArmorHeader = false;
            GUILayout.Label("WEAPONS", Theme.Caps);
            foreach (var (id, price) in GameDirector.SmithStock)
            {
                var item = GameItem.Get(id);
                if (item == null) continue;
                if (!wroteArmorHeader
                    && (item.Slot == ItemSlot.Armor || item.Slot == ItemSlot.Shield))
                {
                    GUILayout.Label("ARMOR", Theme.Caps);
                    wroteArmorHeader = true;
                }
                int owned = director.Stash.Count(s => s == id);
                GUI.enabled = director.PartyGold.Value >= price;
                GUILayout.BeginHorizontal();
                ItemIcon.Draw(id, 30f);
                GUILayout.Space(5);
                if (GUILayout.Button($"{item.Name} — {price}g" +
                                     (owned > 0 ? $"   (stash: {owned})" : ""),
                                     GUILayout.Height(30)))
                    director.CmdBuyItem(id);
                GUILayout.EndHorizontal();
            }
            GUI.enabled = true;
            GUILayout.EndScrollView();
            if (GUILayout.Button("Leave")) _open = false;
            GUILayout.EndArea();
        }
    }
}
