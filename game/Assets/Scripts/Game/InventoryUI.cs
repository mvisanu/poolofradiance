using System.Linq;
using RadiantPool.Rules;
using UnityEngine;

namespace RadiantPool.Game
{
    /// <summary>Inventory panel (I). Left: the character sheet — what they are wearing,
    /// slot by slot, with the stats each piece contributes and the AC/attack it adds up
    /// to. Right: the party stash, every item labelled with its damage or protection and
    /// compared against what is currently worn, so an upgrade is obvious. Equip requests
    /// go to the server, which enforces class rules and returns the swapped-out item to
    /// the stash.</summary>
    public class InventoryUI : MonoBehaviour
    {
        public static InventoryUI Instance { get; private set; }

        private Vector2 _scroll, _wornScroll;
        private GUIStyle _slotName, _slotStat, _itemStat, _better, _worse;

        // Who will buy, if anyone. Resolved once a frame in Update (OnGUI runs several times
        // a frame, and this searches the scene); the server re-checks it on every sale.
        private bool _traderNear;
        private string _traderName;

        private void Awake() => Instance = this;
        private void OnDestroy() { if (Instance == this) Instance = null; }

        public void Toggle() => Ui.Toggle(Ui.Panel.Inventory);

        private PlayerCharacterHolder LocalHolder() =>
            FindObjectsByType<PlayerCharacterHolder>(FindObjectsSortMode.None)
                .FirstOrDefault(p => p.IsOwner);

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.I) && !Ui.Typing) Ui.Toggle(Ui.Panel.Inventory);
            if (CombatManager.Instance != null && CombatManager.Instance.InCombat.Value)
                Ui.Close(Ui.Panel.Inventory);

            if (!Ui.IsOpen(Ui.Panel.Inventory)) return;
            var holder = LocalHolder();
            _traderNear = holder != null
                && GameDirector.TraderNear(holder.transform.position, out _traderName);
        }

        private void EnsureStyles()
        {
            if (_slotName != null) return;
            _slotName = new GUIStyle(Theme.Body) { fontSize = 12, wordWrap = false };
            _slotStat = new GUIStyle(Theme.Caps) { wordWrap = false };
            _itemStat = new GUIStyle(Theme.BodyInk) { fontSize = 11, wordWrap = false };
            _itemStat.normal.textColor = Theme.InkMuted;
            _better = new GUIStyle(_itemStat) { fontSize = 11 };
            _better.normal.textColor = new Color(0.16f, 0.45f, 0.20f);   // ink-green on parchment
            _worse = new GUIStyle(_itemStat) { fontSize = 11 };
            _worse.normal.textColor = Theme.Crimson;
        }

        private void OnGUI()
        {
            Ui.Begin();
            if (!Ui.IsOpen(Ui.Panel.Inventory)) return;
            var director = GameDirector.Instance;
            var holder = LocalHolder();
            if (director == null || holder == null) return;
            EnsureStyles();

            // Fits any window: full size when there is room, shrunk (and scrolled) when not.
            var rect = Ui.FitTop(620f, 460f, top: 56f, bottomMargin: 96f);
            GUILayout.BeginArea(rect, Theme.PanelStyle);
            GUILayout.BeginHorizontal();
            GUILayout.Label("Inventory", Theme.Header);
            GUILayout.FlexibleSpace();
            GUILayout.Label("<color=#d0c5af>I or Esc to close</color>", Theme.Body);
            if (GUILayout.Button("X", GUILayout.Width(28), GUILayout.Height(22)))
                Ui.Close(Ui.Panel.Inventory);
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            // The worn column takes a share of the panel, not a fixed 250 px — on a narrow
            // window the fixed column used to squeeze the stash into nothing.
            DrawEquipped(holder, Mathf.Clamp(rect.width * 0.42f, 180f, 260f));
            GUILayout.Space(10);
            DrawStash(director, holder);
            GUILayout.EndHorizontal();
            GUILayout.EndArea();
        }

        /// <summary>The "what am I wearing" column: every slot, filled or empty, plus the
        /// totals those pieces produce (AC, HP, to-hit and damage of the equipped weapon).</summary>
        private void DrawEquipped(PlayerCharacterHolder holder, float width)
        {
            GUILayout.BeginVertical(GUILayout.Width(width));
            GUILayout.Label("WORN BY " + (holder.CharacterName.Value.Length > 0
                ? holder.CharacterName.Value.ToUpperInvariant() : "YOU"), Theme.Caps);

            _wornScroll = GUILayout.BeginScrollView(_wornScroll);
            GUILayout.BeginVertical(Theme.ParchmentStyle);
            var weapon = GameItem.Get(holder.WeaponId.Value);
            var armor = GameItem.Get(holder.ArmorId.Value);
            var shield = holder.ShieldEquipped.Value ? GameItem.Get("shield") : null;

            Slot("Weapon", weapon);
            Slot("Armor", armor);
            Slot("Off hand", shield);
            GUILayout.EndVertical();

            int str = holder.StrModSynced.Value, dex = holder.DexModSynced.Value;
            int prof = holder.ProficiencySynced.Value;

            GUILayout.Space(6);
            GUILayout.Label("TOTALS", Theme.Caps);
            GUILayout.BeginVertical(Theme.ParchmentStyle);

            // AC, with the breakdown that produced it — no mystery numbers.
            var baseArmor = armor?.Armor ?? ArmorDefinition.Unarmored;
            int dexUsed = Mathf.Min(dex, baseArmor.MaxDexBonus == int.MaxValue
                ? dex : baseArmor.MaxDexBonus);
            string acParts = $"{baseArmor.BaseAc} {baseArmor.Name.ToLowerInvariant()}"
                + (dexUsed != 0 ? $" {Signed(dexUsed)} Dex" : "")
                + (shield != null ? $" +{GameItem.ShieldAcBonus} shield" : "");
            Stat("Armor Class", holder.ArmorClassSynced.Value.ToString(), acParts);
            Stat("Hit Points", holder.MaxHpSynced.Value.ToString(),
                $"level {holder.LevelSynced.Value}");

            if (weapon != null)
            {
                int mod = weapon.Finesse || weapon.RangeFeet > 5 ? Mathf.Max(str, dex) : str;
                Stat("Attack", Signed(prof + mod), $"{weapon.Name.ToLowerInvariant()}");
                Stat("Damage", $"{weapon.Damage}{(mod != 0 ? Signed(mod) : "")}",
                    weapon.DamageType.ToString().ToLowerInvariant());
            }
            GUILayout.EndVertical();
            GUILayout.EndScrollView();
            GUILayout.EndVertical();
        }

        private void Slot(string label, GameItem item)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label.ToUpperInvariant(), _slotStat, GUILayout.Width(58));
            ItemIcon.Draw(item?.Id, 30f);   // empty slots reserve the gap, so rows line up
            GUILayout.Space(4);
            GUILayout.BeginVertical();
            GUILayout.Label(item != null
                ? $"<b>{item.Name}</b>"
                : "<color=#6b6257>— empty —</color>", new GUIStyle(Theme.BodyInk)
                    { fontSize = 12, richText = true, wordWrap = false });
            if (item != null) GUILayout.Label(item.StatLine(), _itemStat);
            GUILayout.EndVertical();
            GUILayout.EndHorizontal();
            GUILayout.Space(4);
        }

        private void Stat(string label, string value, string detail)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label.ToUpperInvariant(), _slotStat, GUILayout.Width(84));
            GUILayout.Label($"<b>{value}</b>", new GUIStyle(Theme.BodyInk)
                { fontSize = 13, richText = true }, GUILayout.Width(46));
            GUILayout.Label(detail, _itemStat, GUILayout.ExpandWidth(true));
            GUILayout.EndHorizontal();
        }

        private void DrawStash(GameDirector director, PlayerCharacterHolder holder)
        {
            GUILayout.BeginVertical();
            GUILayout.BeginHorizontal();
            GUILayout.Label("PARTY STASH", Theme.Caps);
            GUILayout.FlexibleSpace();
            GUILayout.Label($"<color=#f2ca50><b>{director.PartyGold.Value}</b> gold</color>",
                Theme.Body);
            GUILayout.EndHorizontal();

            // Selling needs a buyer in front of you — say which one, or where to find one.
            GUILayout.Label(_traderNear
                ? $"<color=#d0c5af>Trading with {_traderName}.</color>"
                : "<color=#d0c5af>Stand by a trader to sell what you don't need.</color>",
                new GUIStyle(Theme.Body) { fontSize = 11, wordWrap = false });

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
                ItemIcon.Draw(g.Key, 38f);
                GUILayout.Space(6);
                GUILayout.BeginVertical();
                GUILayout.Label($"<b>{label}</b> ×{g.Count()}", new GUIStyle(Theme.BodyInk)
                    { fontSize = 12, richText = true, wordWrap = false });
                if (item != null)
                {
                    GUILayout.Label(item.StatLine(), _itemStat);
                    var (text, style) = Comparison(item, holder);
                    if (text != null) GUILayout.Label(text, style);
                }
                GUILayout.EndVertical();
                GUILayout.FlexibleSpace();

                if (item != null && item.Slot is ItemSlot.Weapon or ItemSlot.Armor
                    or ItemSlot.Shield)
                {
                    bool usable = holder.Sheet == null   // client: Sheet is server-only
                        ? item.UsableBy((CharacterClass)holder.ClassIndex.Value)
                        : item.UsableBy(holder.Sheet.Class);
                    GUI.enabled = usable;
                    if (GUILayout.Button(usable ? "Equip" : "Can't use", GUILayout.Width(80)))
                        director.CmdEquipItem(g.Key);
                    GUI.enabled = true;
                }
                else if (g.Key == "potion_healing")
                {
                    if (GUILayout.Button("Drink", GUILayout.Width(80)))
                        director.CmdUsePotion();
                }

                // Sell it where you find it: no need to reopen the vendor's own panel.
                bool buys = GameDirector.SellValue.TryGetValue(g.Key, out int price) && price > 0;
                GUI.enabled = buys && _traderNear;
                if (GUILayout.Button(buys ? $"Sell {price}g" : "No buyer", GUILayout.Width(80)))
                    director.CmdSellItem(g.Key);
                GUI.enabled = true;
                GUILayout.EndHorizontal();
                GUILayout.Space(6);
            }
            GUILayout.EndVertical();
            GUILayout.EndScrollView();
            GUILayout.EndVertical();
        }

        /// <summary>How this item stacks up against the equipped one — AC for armour,
        /// average damage per hit for weapons. Spelled out in words, not just colour.</summary>
        private (string text, GUIStyle style) Comparison(GameItem item,
            PlayerCharacterHolder holder)
        {
            int str = holder.StrModSynced.Value, dex = holder.DexModSynced.Value;

            if (item.Slot == ItemSlot.Armor)
            {
                var worn = GameItem.Get(holder.ArmorId.Value);
                bool shield = holder.ShieldEquipped.Value;
                int now = worn != null
                    ? worn.AcWith(dex, shield)
                    : ArmorDefinition.Unarmored.BaseAc + dex + (shield ? GameItem.ShieldAcBonus : 0);
                return Delta(item.AcWith(dex, shield) - now, "AC");
            }
            if (item.Slot == ItemSlot.Shield)
                return holder.ShieldEquipped.Value
                    ? ("already equipped", _itemStat)
                    : Delta(GameItem.ShieldAcBonus, "AC");
            if (item.Slot == ItemSlot.Weapon)
            {
                var worn = GameItem.Get(holder.WeaponId.Value);
                float now = worn?.AverageDamage(str, dex) ?? 0f;
                float diff = item.AverageDamage(str, dex) - now;
                if (Mathf.Abs(diff) < 0.05f) return ("same damage as equipped", _itemStat);
                return (diff > 0
                    ? $"upgrade: +{diff:0.#} avg damage"
                    : $"downgrade: {diff:0.#} avg damage",
                    diff > 0 ? _better : _worse);
            }
            return (null, null);
        }

        private (string, GUIStyle) Delta(int diff, string unit) => diff switch
        {
            > 0 => ($"upgrade: +{diff} {unit}", _better),
            < 0 => ($"downgrade: {diff} {unit}", _worse),
            _ => ($"same {unit} as equipped", _itemStat)
        };

        private static string Signed(int v) => v >= 0 ? $"+{v}" : v.ToString();
    }
}
