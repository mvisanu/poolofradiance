using System;
using System.Collections.Generic;
using System.Linq;

namespace RadiantPool.Rules
{
    /// <summary>Who joins when the party musters help. The retinue is picked by ROLE, not
    /// by class order: a healer first (a party without one cannot sustain a four-fight
    /// zone), then damage dealers of two different classes, and only then spare bodies.
    /// Classes already being played count towards the roles, so hires never duplicate a
    /// role the party has covered while a role it lacks goes unfilled.</summary>
    public static class PartyComposition
    {
        public const int MaxPartySize = 4;

        public const CharacterClass Healer = CharacterClass.Cleric;

        public static readonly CharacterClass[] DamageDealers =
            { CharacterClass.Fighter, CharacterClass.Wizard, CharacterClass.Rogue };

        /// <summary>The classes to hire, in order, given who is already in the party and
        /// how many slots are free. Never returns more than <paramref name="slots"/>.</summary>
        public static IReadOnlyList<CharacterClass> Recruits(
            IEnumerable<CharacterClass> party, int slots)
        {
            var hires = new List<CharacterClass>();
            if (slots <= 0) return hires;

            var roster = party?.ToList() ?? new List<CharacterClass>();
            void Hire(CharacterClass c) { hires.Add(c); roster.Add(c); }
            int DistinctDamage() => DamageDealers.Count(roster.Contains);

            // 1. A healer, always.
            if (!roster.Contains(Healer)) Hire(Healer);

            // 2. Two damage dealers of different classes.
            foreach (var dd in DamageDealers)
            {
                if (hires.Count >= slots || DistinctDamage() >= 2) break;
                if (!roster.Contains(dd)) Hire(dd);
            }

            // 3. Spare slots: any class still unrepresented, then repeat damage dealers
            //    (a second healer adds far less than a third sword).
            foreach (var c in Enum.GetValues(typeof(CharacterClass)).Cast<CharacterClass>())
            {
                if (hires.Count >= slots) break;
                if (!roster.Contains(c)) Hire(c);
            }
            for (int i = 0; hires.Count < slots; i++)
                Hire(DamageDealers[i % DamageDealers.Length]);

            return hires;
        }
    }
}
