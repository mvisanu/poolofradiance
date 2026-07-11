using FishNet.Object;
using FishNet.Object.Synchronizing;
using RadiantPool.Rules;
using UnityEngine;

namespace RadiantPool.Game
{
    /// <summary>Server-owned character sheet for a connected player. Until 3c's creation
    /// screen, classes are dealt by join order (Fighter, Cleric, Wizard, Rogue) with the
    /// SRD standard array, so a 2-player party always has frontline + healer.</summary>
    public class PlayerCharacterHolder : NetworkBehaviour
    {
        private static int _joinCounter;

        public readonly SyncVar<int> ClassIndex = new SyncVar<int>(-1);

        /// <summary>Authoritative sheet — exists only on the server.</summary>
        public CharacterSheet Sheet { get; private set; }

        public CharacterClass Class => (CharacterClass)ClassIndex.Value;

        public override void OnStartServer()
        {
            base.OnStartServer();
            int idx = _joinCounter++ % 4;
            ClassIndex.Value = idx;
            Sheet = CreateSheet((CharacterClass)idx, $"pc_{OwnerId}", $"Player {OwnerId}");
        }

        /// <summary>SRD standard array (15,14,13,12,10,8) arranged sensibly per class.</summary>
        public static CharacterSheet CreateSheet(CharacterClass cls, string id, string name)
        {
            AbilityScores scores = cls switch
            {
                CharacterClass.Fighter => new AbilityScores(15, 13, 14, 8, 12, 10),
                CharacterClass.Cleric  => new AbilityScores(14, 8, 13, 10, 15, 12),
                CharacterClass.Wizard  => new AbilityScores(8, 14, 13, 15, 12, 10),
                _                      => new AbilityScores(10, 15, 13, 14, 12, 8), // Rogue
            };
            var race = cls switch
            {
                CharacterClass.Fighter => Race.Human,
                CharacterClass.Cleric => Race.Dwarf,
                CharacterClass.Wizard => Race.Elf,
                _ => Race.Halfling,
            };
            var sheet = new CharacterSheet(id, name, race, cls, scores);
            switch (cls)
            {
                case CharacterClass.Fighter:
                    sheet.EquipArmor(ArmorDefinition.ChainMail);
                    sheet.SetShield(true);
                    break;
                case CharacterClass.Cleric:
                    sheet.EquipArmor(ArmorDefinition.ScaleMail);
                    sheet.SetShield(true);
                    sheet.KnownSpells.AddRange(new[]
                        { "sacred_flame", "cure_wounds", "healing_word", "bless", "guiding_bolt" });
                    break;
                case CharacterClass.Wizard:
                    sheet.KnownSpells.AddRange(new[]
                        { "fire_bolt", "magic_missile", "burning_hands", "shield", "sleep" });
                    break;
                case CharacterClass.Rogue:
                    sheet.EquipArmor(ArmorDefinition.Leather);
                    break;
            }
            return sheet;
        }

        /// <summary>The weapon used for the basic Attack action, per class (v1 loadout;
        /// inventory arrives at 3e).</summary>
        public AttackDefinition BasicAttack()
        {
            var sheet = Sheet;
            return Class switch
            {
                CharacterClass.Fighter => new AttackDefinition("Longsword",
                    sheet.ProficiencyBonus + sheet.Abilities.Modifier(Ability.Str),
                    $"1d8+{sheet.Abilities.Modifier(Ability.Str)}", DamageType.Slashing, 5),
                CharacterClass.Cleric => new AttackDefinition("Mace",
                    sheet.ProficiencyBonus + sheet.Abilities.Modifier(Ability.Str),
                    $"1d6+{sheet.Abilities.Modifier(Ability.Str)}", DamageType.Bludgeoning, 5),
                CharacterClass.Wizard => new AttackDefinition("Quarterstaff",
                    sheet.ProficiencyBonus + sheet.Abilities.Modifier(Ability.Str),
                    $"1d6+{sheet.Abilities.Modifier(Ability.Str)}", DamageType.Bludgeoning, 5),
                _ => new AttackDefinition("Shortsword",
                    sheet.ProficiencyBonus + sheet.Abilities.Modifier(Ability.Dex),
                    $"1d6+{sheet.Abilities.Modifier(Ability.Dex)}", DamageType.Piercing, 5),
            };
        }

        public static void ResetJoinCounter() => _joinCounter = 0;
    }
}
