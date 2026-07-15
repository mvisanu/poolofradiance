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
        private string _selectedName = "";

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

        private PlayerCharacterHolder[] PartyHolders() =>
            FindObjectsByType<PlayerCharacterHolder>(FindObjectsSortMode.None)
                .Where(p => p.ClassIndex.Value >= 0)
                .OrderByDescending(p => p.IsOwner).ThenBy(p => p.CharacterName.Value).ToArray();

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
            var local = LocalHolder();
            if (director == null || local == null) return;
            var party = PartyHolders();
            var holder = party.FirstOrDefault(p => p.CharacterName.Value == _selectedName)
                         ?? local;
            _selectedName = holder.CharacterName.Value;
            EnsureStyles();

            // Fits any window: full size when there is room, shrunk (and scrolled) when not.
            var rect = Ui.FitTop(760f, 500f, top: 56f, bottomMargin: 96f);
            GUILayout.BeginArea(rect, Theme.PanelStyle);
            GUILayout.BeginHorizontal();
            GUILayout.Label("Inventory", Theme.Header);
            GUILayout.FlexibleSpace();
            GUILayout.Label("<color=#d0c5af>I or Esc to close</color>", Theme.Body);
            if (GUILayout.Button("X", GUILayout.Width(28), GUILayout.Height(22)))
                Ui.Close(Ui.Panel.Inventory);
            GUILayout.EndHorizontal();
            Theme.Divider(GUILayoutUtility.GetRect(10f, 6f, GUILayout.ExpandWidth(true)));
            GUILayout.Space(3);

            DrawPartyTabs(party, holder);
            GUILayout.Space(5);

            GUILayout.BeginHorizontal();
            // The worn column takes a share of the panel, not a fixed 250 px — on a narrow
            // window the fixed column used to squeeze the stash into nothing.
            DrawEquipped(director, local, holder,
                Mathf.Clamp(rect.width * 0.40f, 190f, 280f));
            GUILayout.Space(10);
            DrawStash(director, holder);
            GUILayout.EndHorizontal();
            GUILayout.EndArea();
        }

        private void DrawPartyTabs(PlayerCharacterHolder[] party,
            PlayerCharacterHolder selected)
        {
            GUILayout.BeginHorizontal(Theme.ParchmentStyle);
            foreach (var member in party)
            {
                string role = member.IsCompanion
                    ? PartyComposition.RoleOf(member.Class).ToString().ToUpperInvariant()
                    : "YOU";
                bool current = member == selected;
                if (GUILayout.Button($"{role}: {member.CharacterName.Value}",
                        Theme.TabStyle(current), GUILayout.MinWidth(118f), GUILayout.Height(28f))
                    && !current)
                {
                    _selectedName = member.CharacterName.Value;
                    _wornScroll = Vector2.zero;
                }
            }
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
        }

        /// <summary>The "what am I wearing" column: every slot, filled or empty, plus the
        /// totals those pieces produce (AC, HP, to-hit and damage of the equipped weapon).</summary>
        private void DrawEquipped(GameDirector director, PlayerCharacterHolder local,
            PlayerCharacterHolder holder, float width)
        {
            GUILayout.BeginVertical(GUILayout.Width(width));

            // Who you are — level and XP live HERE, not on the main screen.
            GUILayout.Label(holder.CharacterName.Value.Length > 0
                ? $"{holder.CharacterName.Value.ToUpperInvariant()}, {holder.Class}".ToUpperInvariant()
                : "YOUR CHARACTER", Theme.Caps);
            if (holder.IsCompanion)
                GUILayout.Label($"{PartyComposition.RoleOf(holder.Class).ToString().ToUpperInvariant()} COMPANION",
                    new GUIStyle(Theme.Body) { fontSize = 11, wordWrap = false });
            ProgressUI.XpBlock(holder);

            _wornScroll = GUILayout.BeginScrollView(_wornScroll);
            DrawAbilities(holder);

            GUILayout.Space(6);
            GUILayout.Label("WORN", Theme.Caps);
            GUILayout.BeginVertical(Theme.ParchmentStyle);
            var weapon = GameItem.Get(holder.WeaponId.Value);
            var armor = GameItem.Get(holder.ArmorId.Value);
            var offhand = GameItem.Get(holder.OffhandId.Value);
            var ring1 = GameItem.Get(holder.Ring1Id.Value);
            var ring2 = GameItem.Get(holder.Ring2Id.Value);

            Slot("Weapon", weapon);
            Slot("Armor", armor);
            Slot("Off hand", offhand);
            Slot("Ring 1", ring1);
            Slot("Ring 2", ring2);
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
            int offhandAc = offhand?.OffhandAcBonus ?? 0;
            int magicAc = (armor?.MagicBonus ?? 0)
                + (ring1?.RingAc ?? 0) + (ring2?.RingAc ?? 0);
            string acParts = $"{baseArmor.BaseAc} {baseArmor.Name.ToLowerInvariant()}"
                + (dexUsed != 0 ? $" {Signed(dexUsed)} Dex" : "")
                + (offhandAc != 0 ? $" +{offhandAc} off hand" : "")
                + (magicAc != 0 ? $" +{magicAc} magic" : "");
            Stat("Armor Class", holder.ArmorClassSynced.Value.ToString(), acParts);
            Stat("Hit Points", holder.MaxHpSynced.Value.ToString(),
                $"level {holder.LevelSynced.Value}");

            if (weapon != null)
            {
                int mod = weapon.Finesse || weapon.RangeFeet > 5 ? Mathf.Max(str, dex) : str;
                int atkBonus = weapon.MagicBonus
                    + (ring1?.RingAttack ?? 0) + (ring2?.RingAttack ?? 0);
                int dmgBonus = weapon.MagicBonus
                    + (ring1?.RingDamage ?? 0) + (ring2?.RingDamage ?? 0);
                Stat("Attack", Signed(prof + mod + atkBonus), weapon.DisplayName.ToLowerInvariant());
                Stat("Damage", $"{weapon.Damage}{(mod + dmgBonus != 0 ? Signed(mod + dmgBonus) : "")}",
                    weapon.DamageType.ToString().ToLowerInvariant());
            }
            GUILayout.EndVertical();

            if (holder.IsCompanion)
            {
                GUILayout.Space(8);
                if (GUILayout.Button($"Release {holder.CharacterName.Value}", GUILayout.Height(26f)))
                {
                    director.CmdReleaseCompanion(holder.CharacterName.Value);
                    _selectedName = local.CharacterName.Value;
                }
                GUILayout.Label("They keep this level and gear and can be rehired at Council Hall.",
                    new GUIStyle(Theme.Body) { fontSize = 10, wordWrap = true });
            }
            GUILayout.EndScrollView();
            GUILayout.EndVertical();
        }

        private static readonly Ability[] AbilityOrder =
            { Ability.Str, Ability.Dex, Ability.Con, Ability.Int, Ability.Wis, Ability.Cha };

        /// <summary>The six scores — the first thing a character sheet is supposed to say, and
        /// the sheet used to jump straight from the name to the armour. Score AND modifier: the
        /// modifier is the number that rolls, the score is the number a level-up point buys.
        /// Two to a row, so all six fit the column without a scroll of their own.</summary>
        private void DrawAbilities(PlayerCharacterHolder holder)
        {
            GUILayout.Label("ABILITIES", Theme.Caps);
            GUILayout.BeginVertical(Theme.ParchmentStyle);
            for (int i = 0; i < AbilityOrder.Length; i += 2)
            {
                GUILayout.BeginHorizontal();
                AbilityCell(holder, AbilityOrder[i]);
                AbilityCell(holder, AbilityOrder[i + 1]);
                GUILayout.EndHorizontal();
                GUILayout.Space(3);
            }
            GUILayout.EndVertical();
        }

        private void AbilityCell(PlayerCharacterHolder holder, Ability a)
        {
            int score = holder.ScoreOf(a);
            GUILayout.BeginHorizontal(GUILayout.MinWidth(76));
            GUILayout.Label(a.ToString().ToUpperInvariant(), _slotStat, GUILayout.Width(32));
            GUILayout.Label(
                $"<b>{score}</b>  <color=#6b6257>{Signed(holder.ModOf(a))}</color>",
                new GUIStyle(Theme.BodyInk)
                    { fontSize = 13, richText = true, wordWrap = false });
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
        }

        private void Slot(string label, GameItem item)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label.ToUpperInvariant(), _slotStat, GUILayout.Width(58));
            ItemIcon.Draw(item?.Id, 30f);   // empty slots reserve the gap, so rows line up
            GUILayout.Space(4);
            GUILayout.BeginVertical();
            GUILayout.Label(item != null
                ? $"<b>{item.DisplayName}</b>"
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
            GUILayout.Label($"PARTY STASH - EQUIP {holder.CharacterName.Value.ToUpperInvariant()}",
                Theme.Caps);
            GUILayout.FlexibleSpace();
            GUILayout.Label(
                $"<color=#f2ca50><b>{director.PartyGold.Value:N0}</b> gold</color>",
                new GUIStyle(Theme.Body) { richText = true, wordWrap = false });
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
                string label = item != null ? item.DisplayName : g.Key.Replace('_', ' ');

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
                        director.CmdEquipItem(g.Key,
                            holder.IsCompanion ? holder.CharacterName.Value : "");
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

        /// <summary>The comparison itself lives in ItemCompare — the smith asks the very same
        /// question, and the two must never give different answers. Only the PAINT is local:
        /// ink-green/crimson on the bag's parchment.</summary>
        private (string text, GUIStyle style) Comparison(GameItem item,
            PlayerCharacterHolder holder)
        {
            var (text, verdict) = ItemCompare.Versus(item, holder);
            if (text == null) return (null, null);
            return (text, verdict switch
            {
                ItemCompare.Verdict.Better => _better,
                ItemCompare.Verdict.Worse => _worse,
                _ => _itemStat
            });
        }

        private static string Signed(int v) => v >= 0 ? $"+{v}" : v.ToString();
    }
}
