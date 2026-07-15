using RadiantPool.Rules;
using UnityEngine;

namespace RadiantPool.Game
{
    /// <summary>"Is this better than what I am wearing?" — asked in the bag AND at the smith,
    /// answered in ONE place. AC for armour, average damage per hit for weapons, both worked
    /// out with the character's OWN modifiers (a finesse blade is worth more to a rogue than
    /// to a fighter), and spelled out in words, never colour alone.
    ///
    /// The verdict comes back as a value, not a GUIStyle: each panel paints it in its own
    /// palette (parchment ink in the bag, panel text in the shop), but they must never
    /// disagree about what the answer IS.</summary>
    public static class ItemCompare
    {
        public enum Verdict { Same, Better, Worse, NotApplicable }

        /// <summary>How this item stacks up against the one currently equipped.</summary>
        public static (string text, Verdict verdict) Versus(GameItem item,
            PlayerCharacterHolder holder)
        {
            if (item == null || holder == null) return (null, Verdict.NotApplicable);
            int str = holder.StrModSynced.Value, dex = holder.DexModSynced.Value;

            if (item.Slot == ItemSlot.Armor)
            {
                var worn = GameItem.Get(holder.ArmorId.Value);
                bool shield = holder.ShieldEquipped.Value;
                int now = worn != null
                    ? worn.AcWith(dex, shield)
                    : ArmorDefinition.Unarmored.BaseAc + dex
                      + (shield ? GameItem.ShieldAcBonus : 0);
                return Delta(item.AcWith(dex, shield) - now, "AC");
            }

            if (item.Slot == ItemSlot.Shield)
            {
                // The off hand: a shield's +2 (plus its enchantment) or a caster orb's plus,
                // measured against whatever is already held there.
                int cur = GameItem.Get(holder.OffhandId.Value)?.OffhandAcBonus ?? 0;
                return Delta(item.OffhandAcBonus - cur, "AC");
            }

            if (item.Slot == ItemSlot.Ring)
            {
                bool free = string.IsNullOrEmpty(holder.Ring1Id.Value)
                            || string.IsNullOrEmpty(holder.Ring2Id.Value);
                if (free)
                {
                    string boons = item.StatLine();
                    return (string.IsNullOrEmpty(boons) ? "a ring" : $"gain {boons}",
                        Verdict.Better);
                }
                // Both fingers taken: the fair swap is against the weaker ring's AC.
                int weaker = Mathf.Min(GameItem.Get(holder.Ring1Id.Value)?.RingAc ?? 0,
                    GameItem.Get(holder.Ring2Id.Value)?.RingAc ?? 0);
                return Delta(item.RingAc - weaker, "AC");
            }

            if (item.Slot == ItemSlot.Weapon)
            {
                var worn = GameItem.Get(holder.WeaponId.Value);
                float now = worn?.AverageDamage(str, dex) ?? 0f;
                float diff = item.AverageDamage(str, dex) - now;
                if (Mathf.Abs(diff) < 0.05f) return ("same damage as equipped", Verdict.Same);
                return (diff > 0
                    ? $"upgrade: +{diff:0.#} avg damage"
                    : $"downgrade: {diff:0.#} avg damage",
                    diff > 0 ? Verdict.Better : Verdict.Worse);
            }

            return (null, Verdict.NotApplicable);
        }

        /// <summary>Can this character even use it? The shop must say so BEFORE the gold is
        /// spent — the server would refuse the equip afterwards, which is a cruel way to find
        /// out. On a client the sheet is server-only, so the synced class is the source.</summary>
        public static bool Usable(GameItem item, PlayerCharacterHolder holder)
        {
            if (item == null || holder == null) return false;
            var cls = holder.Sheet != null
                ? holder.Sheet.Class
                : (CharacterClass)Mathf.Max(0, holder.ClassIndex.Value);
            return item.UsableBy(cls);
        }

        private static (string, Verdict) Delta(int diff, string unit) =>
            diff > 0 ? ($"upgrade: +{diff} {unit}", Verdict.Better)
            : diff < 0 ? ($"downgrade: {diff} {unit}", Verdict.Worse)
            : ($"same {unit} as equipped", Verdict.Same);
    }
}
