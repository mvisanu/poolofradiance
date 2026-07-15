using System.Collections.Generic;

namespace RadiantPool.Rules
{
    /// <summary>Factory for monsters from SRD stat blocks (reskinned names per
    /// IP-CHECKLIST). Content JSON mirrors these; kept in code for tests/server default.</summary>
    public sealed class MonsterDefinition
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public int ArmorClass { get; set; }
        public string HpDice { get; set; } = "1d8";
        public int Speed { get; set; } = 30;
        public AbilityScores Abilities { get; set; } = new AbilityScores(10, 10, 10, 10, 10, 10);
        public List<AttackDefinition> Attacks { get; } = new List<AttackDefinition>();
        public int Xp { get; set; }
        public string LootTable { get; set; } = "";
        public string SrdRef { get; set; } = "";   // per IP-CHECKLIST guardrail

        public Creature Spawn(string instanceId, IRng rng, bool averageHp = false)
        {
            var expr = DiceExpression.Parse(HpDice);
            int hp = Difficulty.EaseMonsterHp(
                averageHp ? expr.Average : expr.Roll(rng).Total);
            var c = new Creature(instanceId, Name, Abilities, ArmorClass, System.Math.Max(1, hp))
            {
                Speed = Speed,
                IsPlayerCharacter = false
            };
            return c;
        }
    }

    public static class MonsterLibrary
    {
        public static readonly IReadOnlyDictionary<string, MonsterDefinition> All = Build();

        public static MonsterDefinition Get(string id) => All[id];

        private static Dictionary<string, MonsterDefinition> Build()
        {
            var list = new List<MonsterDefinition>
            {
                new MonsterDefinition
                {
                    Id = "marsh_skulker", Name = "Marsh Skulker",
                    ArmorClass = 12, HpDice = "2d8+2", Speed = 30,
                    Abilities = new AbilityScores(11, 12, 12, 10, 10, 10),
                    Xp = 25, LootTable = "lt_skulker", SrdRef = "Bandit (SRD 5.1 p.395)",
                    Attacks =
                    {
                        new AttackDefinition("Rusty Blade", 3, "1d6+1", DamageType.Slashing, 5),
                        new AttackDefinition("Light Crossbow", 3, "1d8+1", DamageType.Piercing, 80)
                    }
                },
                new MonsterDefinition
                {
                    Id = "dock_rat", Name = "Dock Scavenger",
                    ArmorClass = 12, HpDice = "2d6", Speed = 30,
                    Abilities = new AbilityScores(7, 15, 11, 2, 10, 4),
                    Xp = 25, LootTable = "lt_vermin", SrdRef = "Giant Rat (SRD 5.1 p.379)",
                    Attacks = { new AttackDefinition("Bite", 4, "1d4+2", DamageType.Piercing, 5) }
                },
                new MonsterDefinition
                {
                    Id = "risen_drowned", Name = "Risen Drowned",
                    ArmorClass = 8, HpDice = "3d8+9", Speed = 20,
                    Abilities = new AbilityScores(13, 6, 16, 3, 6, 5),
                    Xp = 50, LootTable = "lt_undead", SrdRef = "Zombie (SRD 5.1 p.396)",
                    Attacks = { new AttackDefinition("Slam", 3, "1d6+1", DamageType.Bludgeoning, 5) }
                },
                new MonsterDefinition
                {
                    Id = "bonewalker", Name = "Bonewalker",
                    ArmorClass = 13, HpDice = "2d8+4", Speed = 30,
                    Abilities = new AbilityScores(10, 14, 15, 6, 8, 5),
                    Xp = 50, LootTable = "lt_undead", SrdRef = "Skeleton (SRD 5.1 p.394)",
                    Attacks =
                    {
                        new AttackDefinition("Ancient Blade", 4, "1d6+2", DamageType.Slashing, 5),
                        new AttackDefinition("Shortbow", 4, "1d6+2", DamageType.Piercing, 80)
                    }
                },
                new MonsterDefinition
                {
                    Id = "kindled_zealot", Name = "Kindled Zealot",
                    ArmorClass = 12, HpDice = "2d8", Speed = 30,
                    Abilities = new AbilityScores(11, 12, 10, 10, 11, 10),
                    Xp = 25, LootTable = "lt_kindled", SrdRef = "Cultist (SRD 5.1 p.398)",
                    Attacks = { new AttackDefinition("Curved Blade", 3, "1d6+1", DamageType.Slashing, 5) }
                },
                new MonsterDefinition
                {
                    Id = "hollow_warden", Name = "Warden Sorrel, Hollow-Flame Host",
                    ArmorClass = 18, HpDice = "6d8+12", Speed = 30,
                    Abilities = new AbilityScores(16, 11, 14, 11, 11, 15),
                    Xp = 700, LootTable = "lt_warden", SrdRef = "Knight (SRD 5.1 p.400)",
                    Attacks =
                    {
                        new AttackDefinition("Flame-Wreathed Blade", 5, "2d6+3", DamageType.Slashing, 5),
                        new AttackDefinition("Gout of Radiance", 4, "2d6", DamageType.Fire, 30)
                    }
                },
                new MonsterDefinition
                {
                    Id = "orc", Name = "Orc Raider",
                    ArmorClass = 13, HpDice = "2d8+6", Speed = 30,
                    Abilities = new AbilityScores(16, 12, 16, 7, 11, 10),
                    Xp = 100, LootTable = "lt_raider", SrdRef = "Orc (SRD 5.1)",
                    Attacks =
                    {
                        new AttackDefinition("Greataxe", 5, "1d12+3", DamageType.Slashing, 5),
                        new AttackDefinition("Javelin", 5, "1d6+3", DamageType.Piercing, 30)
                    }
                },
                new MonsterDefinition
                {
                    Id = "orc_warchief", Name = "Karg Splitjaw, Warchief",
                    ArmorClass = 16, HpDice = "6d8+18", Speed = 30,
                    Abilities = new AbilityScores(18, 12, 16, 8, 11, 12),
                    Xp = 450, LootTable = "lt_raider", SrdRef = "Orc (SRD 5.1), elite variant",
                    Attacks =
                    {
                        new AttackDefinition("Great Cleaver", 6, "2d10+4", DamageType.Slashing, 5),
                        new AttackDefinition("Hurled Axe", 6, "1d6+4", DamageType.Slashing, 30)
                    }
                },
                new MonsterDefinition
                {
                    Id = "giant_spider", Name = "Giant Spider",
                    ArmorClass = 14, HpDice = "4d10+4", Speed = 30,
                    Abilities = new AbilityScores(14, 16, 12, 2, 11, 4),
                    Xp = 200, LootTable = "lt_beast_den", SrdRef = "Giant Spider (SRD 5.1)",
                    Attacks =
                    {
                        new AttackDefinition("Venomous Bite", 5, "1d8+3", DamageType.Piercing, 5)
                    }
                },
                new MonsterDefinition
                {
                    Id = "brown_bear", Name = "Brown Bear",
                    ArmorClass = 11, HpDice = "4d10+12", Speed = 40,
                    Abilities = new AbilityScores(19, 10, 16, 2, 13, 7),
                    Xp = 200, LootTable = "lt_beast_den", SrdRef = "Brown Bear (SRD 5.1)",
                    Attacks =
                    {
                        new AttackDefinition("Claws", 6, "2d6+4", DamageType.Slashing, 5),
                        new AttackDefinition("Bite", 6, "1d8+4", DamageType.Piercing, 5)
                    }
                },
                new MonsterDefinition
                {
                    Id = "goblin", Name = "Goblin Ambusher",
                    ArmorClass = 15, HpDice = "2d6", Speed = 30,
                    Abilities = new AbilityScores(8, 14, 10, 10, 8, 8),
                    Xp = 50, LootTable = "lt_goblin", SrdRef = "Goblin (SRD 5.1)",
                    Attacks =
                    {
                        new AttackDefinition("Scimitar", 4, "1d6+2", DamageType.Slashing, 5),
                        new AttackDefinition("Shortbow", 4, "1d6+2", DamageType.Piercing, 80)
                    }
                },
            };

            var dict = new Dictionary<string, MonsterDefinition>();
            foreach (var m in list) dict[m.Id] = m;
            return dict;
        }
    }
}
