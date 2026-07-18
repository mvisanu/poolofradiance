using System;
using System.Collections.Generic;

namespace RadiantPool.Rules
{
    /// <summary>SRD rest rules, v1 scope: short rest = spend hit dice to heal (we grant
    /// half hit-die-count uses per rest, rounded up, no pool tracking in v1); long rest =
    /// full HP + all slots. Random-encounter risk on rest is decided by the caller (zone
    /// data), not here.</summary>
    public static class Rest
    {
        /// <summary>Short rest: each character may roll up to ceil(level/2) hit dice,
        /// adding Con modifier per die (min 0 per die).</summary>
        public static int ShortRest(CharacterSheet sheet, int diceToSpend, IRng rng)
        {
            if (sheet.IsDead) return 0;
            int maxDice = (sheet.Level + 1) / 2;
            diceToSpend = Math.Clamp(diceToSpend, 0, maxDice);
            int die = ClassData.HitDie(sheet.Class);
            int con = sheet.Abilities.Modifier(Ability.Con);
            int healed = 0;
            for (int i = 0; i < diceToSpend; i++)
                healed += Math.Max(0, rng.Next(1, die) + con);
            return sheet.Heal(healed);
        }

        // ---- Out-of-combat recovery (house rule, like Progression's ability points) ----
        // The party slowly knits wounds on safe ground and heals notably faster inside
        // town. Percent-of-max so it stays meaningful from level 1 to 20; integer floors
        // keep level-1 characters (10 HP) from stalling at zero-per-tick.

        /// <summary>Seconds between server regen ticks.</summary>
        public const float RegenTickSeconds = 3f;
        /// <summary>Fraction of max HP recovered per tick on friendly ground.</summary>
        public const double FieldRegenFraction = 0.02;
        /// <summary>Fraction of max HP recovered per tick inside town.</summary>
        public const double TownRegenFraction = 0.06;

        /// <summary>HP restored by one out-of-combat tick: 2% of max (min 1) in the
        /// field, 6% of max (min 2) in town — full recovery in roughly 2.5 field
        /// minutes or under a town minute.</summary>
        public static int RegenPerTick(int maxHp, bool inTown)
        {
            double fraction = inTown ? TownRegenFraction : FieldRegenFraction;
            int floor = inTown ? 2 : 1;
            return Math.Max(floor, (int)Math.Floor(maxHp * fraction));
        }

        /// <summary>Apply one regen tick. Dead characters never regenerate (revival is
        /// combat victory / the shrine, never a trickle). Returns HP actually healed.</summary>
        public static int RegenTick(CharacterSheet sheet, bool inTown)
        {
            if (sheet.IsDead) return 0;
            return sheet.Heal(RegenPerTick(sheet.MaxHp, inTown));
        }

        /// <summary>Long rest: full HP, all spell slots, conditions cleared (v1: all
        /// non-death conditions).</summary>
        public static void LongRest(CharacterSheet sheet)
        {
            if (sheet.IsDead) return;
            sheet.RestoreFull();
            sheet.RestoreAllSlots();
            foreach (var type in new[]
            {
                ConditionType.Poisoned, ConditionType.Frightened, ConditionType.Blessed,
                ConditionType.Dodging, ConditionType.Shielded, ConditionType.Guided,
                ConditionType.Asleep
            })
                sheet.Conditions.Remove(type);
        }
    }

    /// <summary>SRD point-buy (27 points, scores 8–15 before racial bonuses) for the 3c
    /// character-creation screen. The server re-validates every submitted build.</summary>
    public static class PointBuy
    {
        public const int Budget = 27;

        private static readonly Dictionary<int, int> Cost = new Dictionary<int, int>
        {
            { 8, 0 }, { 9, 1 }, { 10, 2 }, { 11, 3 }, { 12, 4 }, { 13, 5 }, { 14, 7 }, { 15, 9 }
        };

        public static int TotalCost(int str, int dex, int con, int intl, int wis, int cha)
        {
            int total = 0;
            foreach (int s in new[] { str, dex, con, intl, wis, cha })
            {
                if (!Cost.TryGetValue(s, out int c))
                    throw new RuleViolationException($"Score {s} is outside point-buy range 8-15.");
                total += c;
            }
            return total;
        }

        public static bool IsValid(int str, int dex, int con, int intl, int wis, int cha,
            out string error)
        {
            try
            {
                int cost = TotalCost(str, dex, con, intl, wis, cha);
                if (cost > Budget)
                {
                    error = $"Build costs {cost} points; budget is {Budget}.";
                    return false;
                }
                error = "";
                return true;
            }
            catch (RuleViolationException e)
            {
                error = e.Message;
                return false;
            }
        }
    }
}
