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
        private GUIStyle _name, _stat, _better, _worse;

        private PlayerCharacterHolder LocalHolder() =>
            FindObjectsByType<PlayerCharacterHolder>(FindObjectsSortMode.None)
                .FirstOrDefault(p => p.IsOwner);

        private Transform LocalPlayer()
        {
            var holder = LocalHolder();
            return holder != null ? holder.transform : null;
        }

        private void EnsureStyles()
        {
            if (_name != null) return;
            _name = new GUIStyle(Theme.Body) { fontSize = 12, richText = true, wordWrap = false };
            _stat = new GUIStyle(Theme.Body) { fontSize = 11, wordWrap = false };
            _stat.normal.textColor = Theme.OnSurfaceMuted;
            _better = new GUIStyle(_stat) { fontSize = 11 };
            _better.normal.textColor = new Color(0.42f, 0.80f, 0.45f);   // green ON PANEL, not
            _worse = new GUIStyle(_stat) { fontSize = 11 };              // the bag's parchment
            _worse.normal.textColor = new Color(0.90f, 0.42f, 0.38f);
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

            EnsureStyles();
            var holder = LocalHolder();

            GUILayout.BeginArea(Ui.FitTop(460f, 480f, top: 50f, bottomMargin: 96f),
                Theme.PanelStyle);
            GUILayout.Label($"{VendorName} — arms & armor", Theme.Header);
            GUILayout.Label($"<color=#f2ca50><b>{director.PartyGold.Value:N0}</b> gold</color>" +
                "   <color=#d0c5af>(bought gear goes to the stash — equip with I)</color>",
                Theme.Body);
            GUILayout.Label(holder != null
                    ? $"<color=#d0c5af>Compared against what {(holder.CharacterName.Value.Length > 0 ? holder.CharacterName.Value : "you")} " +
                      "is wearing right now.</color>"
                    : "",
                new GUIStyle(Theme.Body) { richText = true, fontSize = 11, wordWrap = false });

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
                bool usable = ItemCompare.Usable(item, holder);
                bool affordable = director.PartyGold.Value >= price;
                var (verdictText, verdict) = ItemCompare.Versus(item, holder);

                GUILayout.BeginHorizontal();
                ItemIcon.Draw(id, 38f);
                GUILayout.Space(6);

                // What it IS, what it DOES, and whether it beats what you have — before the
                // gold is spent, not after. A name and a price told you nothing.
                GUILayout.BeginVertical();
                GUILayout.Label($"<b>{item.Name}</b>  <color=#f2ca50>{price}g</color>" +
                                (owned > 0 ? $"  <color=#d0c5af>(stash: {owned})</color>" : ""),
                    _name);
                GUILayout.Label(item.StatLine(), _stat);
                if (!usable)
                    GUILayout.Label($"a {holder?.Class.ToString().ToLowerInvariant() ?? "hero"} " +
                                    "cannot use this", _worse);
                else if (verdictText != null)
                    GUILayout.Label(verdictText, verdict switch
                    {
                        ItemCompare.Verdict.Better => _better,
                        ItemCompare.Verdict.Worse => _worse,
                        _ => _stat
                    });
                GUILayout.EndVertical();
                GUILayout.FlexibleSpace();

                GUI.enabled = affordable;
                if (GUILayout.Button(affordable ? "Buy" : "Too dear",
                        GUILayout.Width(72), GUILayout.Height(30)))
                    director.CmdBuyItem(id);
                GUI.enabled = true;
                GUILayout.EndHorizontal();
                GUILayout.Space(6);
            }
            GUILayout.EndScrollView();
            if (GUILayout.Button("Leave")) _open = false;
            GUILayout.EndArea();
        }
    }
}
