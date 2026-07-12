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
                var hint = new GUIStyle(GUI.skin.box) { alignment = TextAnchor.MiddleCenter };
                GUI.Box(new Rect(Ui.W / 2f - 130, Ui.H - 92, 260, 28),
                    $"[E] Browse {VendorName}", hint);
                return;
            }
            if (!_open) return;

            GUILayout.BeginArea(new Rect(Ui.W / 2f - 210, Ui.H / 2f - 170, 420, 340),
                GUI.skin.box);
            GUILayout.Label($"<b>{VendorName}</b> — arms & armor",
                new GUIStyle(GUI.skin.label) { richText = true });
            GUILayout.Label($"Party gold: {director.PartyGold.Value}   " +
                            "(bought gear goes to the stash — equip with I)");

            bool wroteArmorHeader = false;
            GUILayout.Label("<b>Weapons</b>", new GUIStyle(GUI.skin.label) { richText = true });
            foreach (var (id, price) in GameDirector.SmithStock)
            {
                var item = GameItem.Get(id);
                if (item == null) continue;
                if (!wroteArmorHeader
                    && (item.Slot == ItemSlot.Armor || item.Slot == ItemSlot.Shield))
                {
                    GUILayout.Label("<b>Armor</b>",
                        new GUIStyle(GUI.skin.label) { richText = true });
                    wroteArmorHeader = true;
                }
                int owned = director.Stash.Count(s => s == id);
                GUI.enabled = director.PartyGold.Value >= price;
                if (GUILayout.Button($"{item.Name} — {price}g" +
                                     (owned > 0 ? $"   (stash: {owned})" : "")))
                    director.CmdBuyItem(id);
            }
            GUI.enabled = true;
            if (GUILayout.Button("Leave")) _open = false;
            GUILayout.EndArea();
        }
    }
}
