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

        public Creature Spawn(string instanceId, IRng rng, bool averageHp = false,
            int encounterLevel = 1)
        {
            var expr = DiceExpression.Parse(HpDice);
            int hp = Difficulty.ScaleMonsterHp(
                averageHp ? expr.Average : expr.Roll(rng).Total, encounterLevel);
            int level = System.Math.Max(1, encounterLevel);
            var c = new Creature(instanceId, Name, Abilities,
                ArmorClass + Difficulty.MonsterArmorBonus(level), System.Math.Max(1, hp))
            {
                Speed = Speed,
                IsPlayerCharacter = false,
                EncounterLevel = level,
                ProficiencyBonus = ClassData.ProficiencyBonus(
                    System.Math.Min(Progression.MaxLevel, level))
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
                new MonsterDefinition
                {
                    Id = "ashfang_stalker", Name = "Ashfang Stalker",
                    ArmorClass = 14, HpDice = "5d10+10", Speed = 50,
                    Abilities = new AbilityScores(17, 15, 15, 3, 12, 7),
                    Xp = 200, LootTable = "lt_beast_den", SrdRef = "Dire Wolf (SRD 5.1)",
                    Attacks = { new AttackDefinition("Ashfang Bite", 5, "2d6+3", DamageType.Piercing, 5) }
                },
                new MonsterDefinition
                {
                    Id = "ironbound_veteran", Name = "Ironbound Veteran",
                    ArmorClass = 17, HpDice = "9d8+18", Speed = 30,
                    Abilities = new AbilityScores(16, 13, 14, 10, 11, 10),
                    Xp = 700, LootTable = "lt_warden", SrdRef = "Veteran (SRD 5.1)",
                    Attacks =
                    {
                        new AttackDefinition("Tempered Longsword", 5, "1d8+3", DamageType.Slashing, 5),
                        new AttackDefinition("Heavy Crossbow", 3, "1d10+1", DamageType.Piercing, 100)
                    }
                },
                new MonsterDefinition
                {
                    Id = "veil_adept", Name = "Veil Adept",
                    ArmorClass = 13, HpDice = "6d8+6", Speed = 30,
                    Abilities = new AbilityScores(11, 14, 12, 10, 13, 14),
                    Xp = 450, LootTable = "lt_kindled", SrdRef = "Cult Fanatic (SRD 5.1)",
                    Attacks =
                    {
                        new AttackDefinition("Veil Knife", 4, "1d4+2", DamageType.Piercing, 5),
                        new AttackDefinition("Umbral Spark", 4, "2d6", DamageType.Necrotic, 60)
                    }
                },
                new MonsterDefinition
                {
                    Id = "mire_troll", Name = "Mire Troll",
                    ArmorClass = 15, HpDice = "8d10+40", Speed = 30,
                    Abilities = new AbilityScores(18, 13, 20, 7, 9, 7),
                    Xp = 1800, LootTable = "lt_beast_den", SrdRef = "Troll (SRD 5.1)",
                    Attacks =
                    {
                        new AttackDefinition("Rending Claw", 7, "2d6+4", DamageType.Slashing, 5),
                        new AttackDefinition("Bite", 7, "1d6+4", DamageType.Piercing, 5)
                    }
                },
                new MonsterDefinition
                {
                    Id = "grave_wraith", Name = "Grave Wraith",
                    ArmorClass = 13, HpDice = "9d8+27", Speed = 60,
                    Abilities = new AbilityScores(6, 16, 16, 12, 14, 15),
                    Xp = 1800, LootTable = "lt_undead", SrdRef = "Wraith (SRD 5.1)",
                    Attacks = { new AttackDefinition("Life Drain", 6, "4d8+3", DamageType.Necrotic, 5) }
                },
                new MonsterDefinition
                {
                    Id = "storm_magus", Name = "Storm Magus",
                    ArmorClass = 12, HpDice = "9d8", Speed = 30,
                    Abilities = new AbilityScores(9, 14, 11, 17, 12, 11),
                    Xp = 2300, LootTable = "lt_warden", SrdRef = "Mage (SRD 5.1)",
                    Attacks =
                    {
                        new AttackDefinition("Stormglass Ray", 5, "3d8", DamageType.Lightning, 90),
                        new AttackDefinition("Quarterstaff", 2, "1d6-1", DamageType.Bludgeoning, 5)
                    }
                },
                new MonsterDefinition
                {
                    Id = "frost_reaver", Name = "Frost Reaver",
                    ArmorClass = 16, HpDice = "15d8+45", Speed = 30,
                    Abilities = new AbilityScores(18, 15, 16, 10, 12, 15),
                    Xp = 1800, LootTable = "lt_raider", SrdRef = "Gladiator (SRD 5.1)",
                    Attacks =
                    {
                        new AttackDefinition("Rime Spear", 7, "2d8+4", DamageType.Piercing, 5),
                        new AttackDefinition("Cast Spear", 7, "2d6+4", DamageType.Piercing, 60)
                    }
                },
                new MonsterDefinition
                {
                    Id = "stone_colossus", Name = "Stone Colossus",
                    ArmorClass = 17, HpDice = "17d10+85", Speed = 30,
                    Abilities = new AbilityScores(22, 9, 20, 3, 11, 1),
                    Xp = 5900, LootTable = "lt_warden", SrdRef = "Stone Golem (SRD 5.1)",
                    Attacks = { new AttackDefinition("Granite Slam", 10, "3d8+6", DamageType.Bludgeoning, 5) }
                },
                new MonsterDefinition
                {
                    Id = "cinder_giant", Name = "Cinder Giant",
                    ArmorClass = 18, HpDice = "13d12+78", Speed = 30,
                    Abilities = new AbilityScores(25, 9, 23, 10, 14, 13),
                    Xp = 5000, LootTable = "lt_raider", SrdRef = "Fire Giant (SRD 5.1)",
                    Attacks =
                    {
                        new AttackDefinition("Cinder Greatblade", 11, "6d6+7", DamageType.Slashing, 10),
                        new AttackDefinition("Furnace Rock", 11, "4d10+7", DamageType.Bludgeoning, 120)
                    }
                },
                new MonsterDefinition
                {
                    Id = "night_regent", Name = "Night Regent",
                    ArmorClass = 16, HpDice = "17d8+68", Speed = 30,
                    Abilities = new AbilityScores(18, 18, 18, 17, 15, 18),
                    Xp = 10000, LootTable = "lt_warden", SrdRef = "Vampire (SRD 5.1)",
                    Attacks =
                    {
                        new AttackDefinition("Regent's Talons", 9, "2d8+4", DamageType.Slashing, 5),
                        new AttackDefinition("Sanguine Bolt", 9, "3d8+4", DamageType.Necrotic, 60)
                    }
                },
                new MonsterDefinition
                {
                    Id = "starbound_juggernaut", Name = "Starbound Juggernaut",
                    ArmorClass = 20, HpDice = "20d10+100", Speed = 30,
                    Abilities = new AbilityScores(24, 9, 20, 3, 11, 1),
                    Xp = 15000, LootTable = "lt_warden", SrdRef = "Iron Golem (SRD 5.1)",
                    Attacks =
                    {
                        new AttackDefinition("Star-Iron Slam", 13, "3d8+7", DamageType.Bludgeoning, 5),
                        new AttackDefinition("Furnace Breath", 10, "6d8", DamageType.Fire, 30)
                    }
                },
                new MonsterDefinition
                {
                    Id = "hollow_star_lich", Name = "The Hollow Star",
                    ArmorClass = 17, HpDice = "18d8+54", Speed = 30,
                    Abilities = new AbilityScores(11, 16, 16, 20, 14, 16),
                    Xp = 33000, LootTable = "lt_warden", SrdRef = "Lich (SRD 5.1)",
                    Attacks =
                    {
                        new AttackDefinition("Starfall Lance", 12, "6d8", DamageType.Force, 90),
                        new AttackDefinition("Grave Touch", 12, "3d6+3", DamageType.Necrotic, 5)
                    }
                },

                // --- Variety roster (added to widen encounter mixes). Every entry shares an
                // existing XP tier value so it is a drop-in, balance-neutral alternative for a
                // same-tier monster: equal Xp keeps encounter XP totals (and the pinned
                // level-20 curve) identical whether it appears via runtime substitution or an
                // equal-XP authored swap. Each carries an SRD stat-block reference. ---

                // Tier 25 -------------------------------------------------------------------
                new MonsterDefinition
                {
                    Id = "plague_rat", Name = "Plague Rat",
                    ArmorClass = 12, HpDice = "2d6", Speed = 30,
                    Abilities = new AbilityScores(7, 15, 11, 2, 10, 4),
                    Xp = 25, LootTable = "lt_vermin",
                    SrdRef = "Giant Rat (SRD 5.1 p.379), diseased variant",
                    Attacks = { new AttackDefinition("Filthy Bite", 4, "1d4+2", DamageType.Piercing, 5) }
                },
                new MonsterDefinition
                {
                    Id = "fen_croaker", Name = "Fen Croaker",
                    ArmorClass = 11, HpDice = "2d8", Speed = 30,
                    Abilities = new AbilityScores(12, 13, 11, 2, 10, 3),
                    Xp = 25, LootTable = "lt_beast_den",
                    SrdRef = "Giant Frog (SRD 5.1 p.377)",
                    Attacks = { new AttackDefinition("Gulping Bite", 3, "1d6+1", DamageType.Piercing, 5) }
                },
                new MonsterDefinition
                {
                    Id = "grave_crawler", Name = "Grave Crawler",
                    ArmorClass = 12, HpDice = "2d6", Speed = 30,
                    Abilities = new AbilityScores(5, 14, 12, 1, 7, 3),
                    Xp = 25, LootTable = "lt_undead",
                    SrdRef = "Giant Centipede (SRD 5.1 p.377)",
                    Attacks = { new AttackDefinition("Venom Bite", 4, "1d4+2", DamageType.Poison, 5) }
                },
                new MonsterDefinition
                {
                    Id = "gray_knife", Name = "Gray Knife Cutter",
                    ArmorClass = 12, HpDice = "2d8", Speed = 30,
                    Abilities = new AbilityScores(13, 11, 12, 10, 10, 11),
                    Xp = 25, LootTable = "lt_skulker", SrdRef = "Thug (SRD 5.1 p.395)",
                    Attacks =
                    {
                        new AttackDefinition("Cudgel", 4, "1d6+2", DamageType.Bludgeoning, 5),
                        new AttackDefinition("Sling", 4, "1d4+2", DamageType.Bludgeoning, 30)
                    }
                },
                new MonsterDefinition
                {
                    Id = "ember_acolyte", Name = "Ember Acolyte",
                    ArmorClass = 12, HpDice = "2d8", Speed = 30,
                    Abilities = new AbilityScores(10, 11, 11, 10, 14, 11),
                    Xp = 25, LootTable = "lt_kindled", SrdRef = "Acolyte (SRD 5.1 p.395)",
                    Attacks =
                    {
                        new AttackDefinition("Ritual Dagger", 3, "1d4+1", DamageType.Piercing, 5),
                        new AttackDefinition("Ember Bolt", 4, "1d8", DamageType.Fire, 60)
                    }
                },

                // Tier 50 -------------------------------------------------------------------
                new MonsterDefinition
                {
                    Id = "dust_jackal", Name = "Dust Jackal",
                    ArmorClass = 12, HpDice = "2d8+2", Speed = 40,
                    Abilities = new AbilityScores(13, 15, 12, 3, 12, 6),
                    Xp = 50, LootTable = "lt_beast_den", SrdRef = "Wolf (SRD 5.1 p.341)",
                    Attacks = { new AttackDefinition("Snapping Bite", 4, "1d6+2", DamageType.Piercing, 5) }
                },
                new MonsterDefinition
                {
                    Id = "sand_scuttler", Name = "Sand Scuttler",
                    ArmorClass = 13, HpDice = "2d8+2", Speed = 30,
                    Abilities = new AbilityScores(12, 16, 13, 3, 11, 4),
                    Xp = 50, LootTable = "lt_beast_den",
                    SrdRef = "Giant Wolf Spider (SRD 5.1 p.378)",
                    Attacks = { new AttackDefinition("Fanged Bite", 4, "1d6+2", DamageType.Piercing, 5) }
                },
                new MonsterDefinition
                {
                    Id = "reed_scout", Name = "Reed Scout",
                    ArmorClass = 13, HpDice = "2d8+2", Speed = 30,
                    Abilities = new AbilityScores(11, 14, 12, 11, 13, 11),
                    Xp = 50, LootTable = "lt_skulker", SrdRef = "Scout (SRD 5.1 p.394)",
                    Attacks =
                    {
                        new AttackDefinition("Shortsword", 4, "1d6+2", DamageType.Slashing, 5),
                        new AttackDefinition("Longbow", 4, "1d8+2", DamageType.Piercing, 150)
                    }
                },

                // Tier 100 ------------------------------------------------------------------
                new MonsterDefinition
                {
                    Id = "ironpost_soldier", Name = "Ironpost Soldier",
                    ArmorClass = 15, HpDice = "2d8+6", Speed = 30,
                    Abilities = new AbilityScores(15, 12, 16, 10, 10, 9),
                    Xp = 100, LootTable = "lt_raider", SrdRef = "Hobgoblin (SRD 5.1)",
                    Attacks =
                    {
                        new AttackDefinition("Longsword", 4, "1d8+2", DamageType.Slashing, 5),
                        new AttackDefinition("Longbow", 3, "1d8+1", DamageType.Piercing, 150)
                    }
                },

                // Tier 200 ------------------------------------------------------------------
                new MonsterDefinition
                {
                    Id = "thornback_boar", Name = "Thornback Boar",
                    ArmorClass = 12, HpDice = "5d8+10", Speed = 40,
                    Abilities = new AbilityScores(17, 11, 16, 2, 9, 5),
                    Xp = 200, LootTable = "lt_beast_den", SrdRef = "Giant Boar (SRD 5.1)",
                    Attacks = { new AttackDefinition("Goring Tusks", 5, "2d6+3", DamageType.Slashing, 5) }
                },
                new MonsterDefinition
                {
                    Id = "bloated_drowned", Name = "Bloated Drowned",
                    ArmorClass = 9, HpDice = "6d8+18", Speed = 20,
                    Abilities = new AbilityScores(17, 6, 17, 3, 6, 5),
                    Xp = 200, LootTable = "lt_undead", SrdRef = "Ogre Zombie (SRD 5.1)",
                    Attacks = { new AttackDefinition("Heaving Slam", 5, "2d8+4", DamageType.Bludgeoning, 5) }
                },
                new MonsterDefinition
                {
                    Id = "iron_sentinel", Name = "Iron Sentinel",
                    ArmorClass = 18, HpDice = "6d8+6", Speed = 25,
                    Abilities = new AbilityScores(14, 11, 13, 1, 3, 1),
                    Xp = 200, LootTable = "lt_warden", SrdRef = "Animated Armor (SRD 5.1)",
                    Attacks = { new AttackDefinition("Iron Fist", 4, "1d10+2", DamageType.Bludgeoning, 5) }
                },

                // Tier 450 ------------------------------------------------------------------
                new MonsterDefinition
                {
                    Id = "ash_ogre", Name = "Ash Ogre",
                    ArmorClass = 11, HpDice = "7d10+21", Speed = 40,
                    Abilities = new AbilityScores(19, 8, 16, 5, 7, 7),
                    Xp = 450, LootTable = "lt_raider", SrdRef = "Ogre (SRD 5.1)",
                    Attacks =
                    {
                        new AttackDefinition("Ash Greatclub", 6, "2d8+4", DamageType.Bludgeoning, 5),
                        new AttackDefinition("Hurled Rock", 5, "2d6+4", DamageType.Bludgeoning, 60)
                    }
                },

                // Tier 700 ------------------------------------------------------------------
                new MonsterDefinition
                {
                    Id = "barrow_wight", Name = "Barrow Wight",
                    ArmorClass = 14, HpDice = "6d8+12", Speed = 30,
                    Abilities = new AbilityScores(15, 14, 16, 10, 13, 15),
                    Xp = 700, LootTable = "lt_undead", SrdRef = "Wight (SRD 5.1)",
                    Attacks =
                    {
                        new AttackDefinition("Barrow Blade", 4, "2d6+2", DamageType.Slashing, 5),
                        new AttackDefinition("Life Leech", 4, "1d8+2", DamageType.Necrotic, 5)
                    }
                },
            };

            var dict = new Dictionary<string, MonsterDefinition>();
            foreach (var m in list) dict[m.Id] = m;
            return dict;
        }
    }
}
