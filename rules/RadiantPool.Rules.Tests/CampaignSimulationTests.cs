using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using RadiantPool.Rules;
using Xunit;

namespace RadiantPool.Rules.Tests
{
    /// <summary>Plays the entire first-region campaign headlessly: a 2-player party
    /// (fighter + cleric) fights every encounter from the content JSON in order, with a
    /// long rest between encounters and quest XP on zone completion. Validates the two
    /// design claims the game makes: the campaign is survivable with basic tactics, and
    /// the XP curve lands the party at level 20 during the final dungeon.</summary>
    public class CampaignSimulationTests
    {
        private const int MusterXp = 50;
        // One valid topological play order through every live location. Parallel Council
        // commissions can be chosen differently in-game; the simulator needs only one
        // deterministic order that respects the same prerequisite graph.
        private static readonly (string zone, int questXp)[] ZoneChain =
        {
            ("old_docks", 300),
            ("drowned_bastion", 500),
            ("cinderwell_yard", 350),
            ("drowned_market", 900),
            ("sunken_warcamp", 1200),
            ("cinderwell_undercroft", 450),
            ("ember_archive", 650),
            ("loomhouse_enclave", 700),
            ("blackbriar_manor", 700),
            ("glasslit_temple", 3400),
            ("ashen_ward", 1200),
            ("emberwild_expanse", 400),
            ("wild_lairs", 650),
            ("reedwind_encampment", 800),
            ("goblin_delves", 850),
            ("drowned_observatory_approach", 400),
            ("drowned_observatory_underworks", 400),
            ("drowned_observatory_crown", 1200),
            ("gilded_quarter", 750),
            ("mirewatch_citadel", 1000),
            ("tidebreaker_anchorage", 1000),
            ("iron_concord_redoubt", 1100),
            ("lanternfall_necropolis", 1000),
            ("cinder_gate", 1300),
            ("crownless_citadel", 700),
            ("thornmaze", 800),
            ("ember_crown_spire", 2000),
            ("duskmire_crossing", 4000),
            ("whispervault", 4500),
            ("stormglass_foundry", 5000),
            ("frostvein_pass", 5500),
            ("hoarfire_halls", 14600),
            ("winter_crown_vault", 20400),
            ("shattered_coast", 15600),
            ("colossus_road", 17500),
            ("titan_foundry", 24900),
            ("veil_threshold", 7000),
            ("hollow_star_depths", 3000),
            ("dawnspire_nexus", 20500)
        };

        private static string ContentRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "content")))
                dir = dir.Parent;
            return Path.Combine(dir!.FullName, "content");
        }

        private static List<(string id, bool required, string[] units)> ZoneEncounters(string zoneId)
        {
            using var doc = JsonDocument.Parse(
                File.ReadAllText(Path.Combine(ContentRoot(), "zones", zoneId + ".json")));
            return doc.RootElement.GetProperty("encounters").EnumerateArray()
                .Select(e => (
                    e.GetProperty("id").GetString()!,
                    e.GetProperty("requiredForClear").GetBoolean(),
                    e.GetProperty("units").EnumerateArray().Select(u => u.GetString()!).ToArray()))
                .ToList();
        }

        private static (CharacterSheet fighter, CharacterSheet cleric) Party()
        {
            var fighter = new CharacterSheet("f", "Bran", Race.Human, CharacterClass.Fighter,
                new AbilityScores(15, 13, 14, 8, 12, 10));
            fighter.EquipArmor(ArmorDefinition.ChainMail);
            fighter.SetShield(true);
            var cleric = new CharacterSheet("c", "Korga", Race.Dwarf, CharacterClass.Cleric,
                new AbilityScores(14, 8, 13, 10, 15, 12));
            cleric.EquipArmor(ArmorDefinition.ScaleMail);
            cleric.SetShield(true);
            return (fighter, cleric);
        }

        /// <summary>Runs one encounter to completion. Returns true on victory. Mirrors the
        /// game's tactics floor: fighter swings at the strongest enemy, cleric heals a
        /// downed/badly hurt ally (Healing Word/Cure Wounds) or attacks; monsters swing at
        /// the fighter first (it stands in front), then the cleric.</summary>
        private static bool RunEncounter(CharacterSheet fighter, CharacterSheet cleric,
            string[] monsterIds, IRng rng)
        {
            var monsters = monsterIds
                .Select((id, i) => MonsterLibrary.Get(id).Spawn($"m{i}_{id}", rng,
                    encounterLevel: Difficulty.TargetMonsterLevel(
                        Math.Max(fighter.Level, cleric.Level))))
                .ToList();
            var all = new List<Creature> { fighter, cleric };
            all.AddRange(monsters);
            var engine = new TurnEngine(all, rng);

            var sword = new AttackDefinition("Longsword",
                fighter.ProficiencyBonus + fighter.Abilities.Modifier(Ability.Str),
                $"1d8+{fighter.Abilities.Modifier(Ability.Str)}", DamageType.Slashing);
            var mace = new AttackDefinition("Mace",
                cleric.ProficiencyBonus + cleric.Abilities.Modifier(Ability.Str),
                $"1d6+{cleric.Abilities.Modifier(Ability.Str)}", DamageType.Bludgeoning);

            int safety = 400;
            while (!engine.CombatOver(out _) && safety-- > 0)
            {
                var active = engine.ActiveCreature;
                if (TurnEngine.CanAct(active))
                {
                    Creature? target = monsters.FirstOrDefault(m => !m.IsDead
                        && !m.Conditions.Has(ConditionType.Asleep));
                    if (active == fighter)
                    {
                        engine.SpendAction();
                        int swings = ClassData.AttacksPerAction(fighter.Class, fighter.Level);
                        for (int s = 0; s < swings && target != null; s++)
                        {
                            CombatMath.ResolveAttack(fighter, target, sword, rng);
                            if (target.IsDead)
                                target = monsters.FirstOrDefault(m => !m.IsDead
                                    && !m.Conditions.Has(ConditionType.Asleep));
                        }
                    }
                    else if (active == cleric)
                    {
                        Creature? hurt =
                            fighter.IsDown ? fighter :
                            cleric.IsDown ? cleric :
                            fighter.CurrentHp * 3 < fighter.MaxHp ? fighter : null;
                        int slot = Enumerable.Range(1, cleric.SlotsRemaining.Count).Reverse()
                            .FirstOrDefault(cleric.HasSlot); // high-tier fights demand real upcasts
                        engine.SpendAction();
                        if (hurt != null && slot > 0)
                            SpellEngine.Cast(cleric, SpellLibrary.Get("cure_wounds"),
                                new[] { hurt }, slot, rng);
                        else if (!fighter.Conditions.Has(ConditionType.Blessed) && slot > 0)
                            // Open with Bless — the party's standard first move.
                            SpellEngine.Cast(cleric, SpellLibrary.Get("bless"),
                                new Creature[] { fighter, cleric }, slot, rng);
                        else if (target != null)
                        {
                            var save = SpellLibrary.Get("sacred_flame");
                            SpellEngine.Cast(cleric, save, new[] { target }, 0, rng);
                        }
                    }
                    else
                    {
                        var def = MonsterLibrary.All.Values
                            .First(d => active.Id.EndsWith(d.Id));
                        var victim = !fighter.IsDead && !fighter.IsDown ? (Creature)fighter
                            : !cleric.IsDead && !cleric.IsDown ? cleric : null;
                        if (victim != null)
                            CombatMath.ResolveAttack(active, victim, def.Attacks[0], rng);
                    }
                }
                else if (active.IsPlayerCharacter && active.IsDown
                         && !active.IsStable && !active.IsDead)
                {
                    CombatMath.RollDeathSave(active, rng);
                }
                engine.EndTurn();
            }
            Assert.True(safety > 0, "encounter did not terminate");
            engine.CombatOver(out bool playersWon);
            return playersWon;
        }

        private static void Award(CharacterSheet sheet, int xp)
        {
            sheet.GainXp(xp);
            while (sheet.CanLevelUp) sheet.LevelUp();
            // The campaign assumes players use the level-up screen instead of carrying
            // a growing pile of unspent points into high-tier encounters.
            var priorities = new[]
            {
                Progression.PrimaryAbility(sheet.Class), Ability.Con, Ability.Dex,
                Ability.Wis, Ability.Str, Ability.Int, Ability.Cha
            };
            while (sheet.PendingAbilityPoints > 0)
            {
                var ability = priorities.FirstOrDefault(sheet.CanSpendPointOn);
                if (!sheet.CanSpendPointOn(ability)) break;
                sheet.SpendAbilityPoint(ability);
            }
        }

        [Theory]
        [InlineData(11)]
        [InlineData(1234)]
        [InlineData(777)]
        [InlineData(2026)]
        [InlineData(31337)]
        public void TwoPlayerParty_CompletesCampaign_AtLevel20(int seed)
        {
            var rng = new SeededRng(seed);
            var (fighter, cleric) = Party();
            Award(fighter, MusterXp);
            Award(cleric, MusterXp);

            int totalWipes = 0;
            foreach (var (zoneId, questXp) in ZoneChain)
            {
                foreach (var (id, required, units) in ZoneEncounters(zoneId))
                {
                    if (!required) continue;   // required-only run must still reach L5
                    // Party-wipe rule: revive at hub, try the block again (trigger not consumed).
                    int attempts = 0;
                    while (true)
                    {
                        attempts++;
                        Assert.True(attempts <= 8, $"{id} unbeatable at seed {seed}");
                        bool won = RunEncounter(fighter, cleric, units, rng);
                        if (won) break;
                        totalWipes++;
                        fighter.ReviveFull(); cleric.ReviveFull();
                        fighter.RestoreAllSlots(); cleric.RestoreAllSlots();
                    }
                    // Runtime victory revives a fallen winner at 1 HP before exploration
                    // resumes; mirror that rule so the simulation measures the real game.
                    if (fighter.IsDead)
                    {
                        fighter.ReviveFull();
                        fighter.TakeDamage(fighter.MaxHp - 1, DamageType.Bludgeoning);
                    }
                    if (cleric.IsDead)
                    {
                        cleric.ReviveFull();
                        cleric.TakeDamage(cleric.MaxHp - 1, DamageType.Bludgeoning);
                    }
                    int encounterXp = units.Sum(u => MonsterLibrary.Get(u).Xp);
                    Award(fighter, encounterXp);
                    Award(cleric, encounterXp);
                    Rest.LongRest(fighter);
                    Rest.LongRest(cleric);
                }
                Award(fighter, questXp);
                Award(cleric, questXp);
            }

            Assert.Equal(20, fighter.Level);
            Assert.Equal(20, cleric.Level);
            Assert.False(fighter.IsDead);
            Assert.False(cleric.IsDead);
            // Difficulty sanity across 81 required fights: basic tactics should still
            // average well below one wipe per location.
            Assert.True(totalWipes <= 30, $"campaign too hard: {totalWipes} wipes at seed {seed}");
        }

        [Fact]
        public void XpBudget_RequiredContentAlone_ReachesLevel20()
        {
            // Pure math check, independent of combat outcomes.
            int xp = MusterXp;
            foreach (var (zoneId, questXp) in ZoneChain)
            {
                xp += ZoneEncounters(zoneId).Where(e => e.required)
                    .Sum(e => e.units.Sum(u => MonsterLibrary.Get(u).Xp));
                xp += questXp;
            }
            Assert.True(xp >= ClassData.XpThresholds[19],
                $"required-content XP {xp} < level-20 threshold {ClassData.XpThresholds[19]}");
        }

        [Fact]
        public void FinalArc_DeliversLevelTwentyBeforeTheLastBoss()
        {
            int xp = MusterXp;
            foreach (var (zoneId, questXp) in ZoneChain)
            {
                var encounters = ZoneEncounters(zoneId).Where(e => e.required).ToList();
                for (int i = 0; i < encounters.Count; i++)
                {
                    xp += encounters[i].units.Sum(u => MonsterLibrary.Get(u).Xp);
                    if (zoneId == "dawnspire_nexus" && i == 0)
                        Assert.True(xp >= ClassData.XpThresholds[19],
                            "the normal campaign path must reach level 20 during the finale");
                }
                xp += questXp;
            }
        }
    }
}
