using System.Linq;
using UnityEngine;

namespace RadiantPool.Game
{
    /// <summary>The Salvage Exchange: buy healing potions, sell battlefield salvage.
    /// All transactions resolve on the server via GameDirector.</summary>
    public class VendorInteract : MonoBehaviour
    {
        public string VendorName = "The Salvage Exchange";
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
                    $"[E] Trade at {VendorName}", hint);
                return;
            }
            if (!_open) return;

            GUILayout.BeginArea(new Rect(Ui.W / 2f - 200, Ui.H / 2f - 100, 400, 200),
                GUI.skin.box);
            GUILayout.Label($"<b>{VendorName}</b>", new GUIStyle(GUI.skin.label) { richText = true });
            GUILayout.Label($"Party gold: {director.PartyGold.Value}");

            int potions = director.Stash.Count(s => s == "potion_healing");
            int salvage = director.Stash.Count - potions;
            int salvageValue = director.Stash.Where(s => s != "potion_healing")
                .Sum(s => GameDirector.SellValue.TryGetValue(s, out int v) ? v : 0);

            GUI.enabled = director.PartyGold.Value >= GameDirector.PotionBuyPrice;
            if (GUILayout.Button($"Buy Potion of Healing — {GameDirector.PotionBuyPrice}g " +
                                 $"(have {potions})"))
                director.CmdBuyPotion();
            GUI.enabled = salvage > 0;
            if (GUILayout.Button($"Sell all salvage ({salvage} items) — {salvageValue}g"))
                director.CmdSellAll();
            GUI.enabled = true;
            if (GUILayout.Button("Leave")) _open = false;
            GUILayout.EndArea();
        }
    }
}
