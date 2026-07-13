using System.Linq;
using UnityEngine;

namespace RadiantPool.Game
{
    /// <summary>The Salvage Exchange: buy healing potions, sell the bag — item by item at
    /// its own price, or the whole salvage pile in one go. All transactions resolve on the
    /// server via GameDirector (CmdSellItem re-checks that the seller is standing here).</summary>
    public class VendorInteract : MonoBehaviour
    {
        public string VendorName = "The Salvage Exchange";
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
            if (Input.GetKeyDown(KeyCode.E) && InRange() && !Ui.PanelOpen) _open = !_open;
            if (_open && !InRange()) _open = false;
        }

        private void OnGUI()
        {
            Ui.Begin();
            if (CombatManager.Instance != null && CombatManager.Instance.InCombat.Value) return;
            if (Ui.PanelOpen) return;   // a screen is up: the shop waits behind it
            var director = GameDirector.Instance;
            if (director == null) return;

            if (!_open && InRange())
            {
                Theme.DrawToast(Ui.W / 2f, 146f, $"[E] Trade at {VendorName}");
                return;
            }
            if (!_open) return;

            GUILayout.BeginArea(Ui.Fit(440f, 400f), Theme.PanelStyle);
            GUILayout.Label(VendorName, Theme.Header);
            GUILayout.Label($"<color=#f2ca50><b>{director.PartyGold.Value:N0}</b> gold</color>",
                Theme.Body);

            int potions = director.Stash.Count(s => s == "potion_healing");
            int salvage = director.Stash.Count - potions;
            int salvageValue = director.Stash.Where(s => s != "potion_healing")
                .Sum(s => GameDirector.SellValue.TryGetValue(s, out int v) ? v : 0);

            GUI.enabled = director.PartyGold.Value >= GameDirector.PotionBuyPrice;
            if (GUILayout.Button($"Buy Potion of Healing — {GameDirector.PotionBuyPrice}g " +
                                 $"(have {potions})"))
                director.CmdBuyPotion();
            GUI.enabled = true;

            // Sell one piece at a time — the bag is grouped by item, and each row shows what
            // the Exchange pays for one of them. Sell-all below still dumps the whole pile.
            GUILayout.Space(6);
            GUILayout.Label("SELL FROM THE BAG", Theme.Caps);
            _scroll = GUILayout.BeginScrollView(_scroll, GUILayout.ExpandHeight(true));
            GUILayout.BeginVertical(Theme.ParchmentStyle);
            var groups = director.Stash.GroupBy(id => id).OrderBy(g => g.Key).ToList();
            if (groups.Count == 0)
                GUILayout.Label("(nothing in the bag — loot the docks!)", Theme.BodyInk);

            foreach (var g in groups)
            {
                var item = GameItem.Get(g.Key);
                string label = item != null ? item.Name : g.Key.Replace('_', ' ');
                bool buys = GameDirector.SellValue.TryGetValue(g.Key, out int price) && price > 0;

                GUILayout.BeginHorizontal();
                ItemIcon.Draw(g.Key, 34f);
                GUILayout.Space(5);
                GUILayout.BeginVertical();
                GUILayout.Label($"<b>{label}</b> x{g.Count()}", new GUIStyle(Theme.BodyInk)
                    { fontSize = 12, richText = true, wordWrap = false });
                // What you are about to part with — selling the better sword by mistake is
                // exactly the thing a bare name lets you do.
                if (item != null)
                    GUILayout.Label(item.StatLine(), new GUIStyle(Theme.BodyInk)
                        { fontSize = 11, wordWrap = false, normal = { textColor = Theme.InkMuted } });
                GUILayout.EndVertical();
                GUILayout.FlexibleSpace();
                GUI.enabled = buys;
                if (GUILayout.Button(buys ? $"Sell {price}g" : "No buyer", GUILayout.Width(88)))
                    director.CmdSellItem(g.Key);
                GUI.enabled = true;
                GUILayout.EndHorizontal();
            }
            GUILayout.EndVertical();
            GUILayout.EndScrollView();

            GUI.enabled = salvage > 0;
            if (GUILayout.Button($"Sell all salvage ({salvage} items) — {salvageValue}g"))
                director.CmdSellAll();
            GUI.enabled = true;
            if (GUILayout.Button("Leave")) _open = false;
            GUILayout.EndArea();
        }
    }
}
