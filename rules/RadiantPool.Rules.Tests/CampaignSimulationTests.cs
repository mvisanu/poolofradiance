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
    /// the XP curve lands the party at level 5 by the final turn-in.</summary>
    public class CampaignSimulationTests
    {
        private const int MusterXp = 50;
        private static readonly (string zone, int questXp)[] ZoneChain =
        {
            ("old_docks", 300), ("drowned_market", 900),
            ("sunken_warcamp", 1200), ("glasslit_temple", 3400)
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
                .Select((id, i) => MonsterLibrary.Get(id).Spawn($"m{i}_{id}", rng))
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
                        // At level 5 the SRD fighter attacks twice (Extra Attack).
                        int swings = fighter.Level >= 5 ? 2 : 1;
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
                        int slot = cleric.HasSlot(1) ? 1 : cleric.HasSlot(2) ? 2
                            : cleric.HasSlot(3) ? 3 : 0;   // upcast when low slots run dry
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
        }

        [Theory]
        [InlineData(11)]
        [InlineData(1234)]
        [InlineData(777)]
        [InlineData(2026)]
        [InlineData(31337)]
        public void TwoPlayerParty_CompletesCampaign_AtLevel5(int seed)
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
                    int encounterXp = units.Sum(u => MonsterLibrary.Get(u).Xp);
                    Award(fighter, encounterXp);
                    Award(cleric, encounterXp);
                    Rest.LongRest(fighter);
                    Rest.LongRest(cleric);
                }
                Award(fighter, questXp);
                Award(cleric, questXp);
            }

            Assert.Equal(5, fighter.Level);
            Assert.Equal(5, cleric.Level);
            Assert.False(fighter.IsDead);
            Assert.False(cleric.IsDead);
            // Difficulty sanity: basic tactics shouldn't wipe more than a few times total.
            Assert.True(totalWipes <= 6, $"campaign too hard: {totalWipes} wipes at seed {seed}");
        }

        [Fact]
        public void XpBudget_RequiredContentAlone_ReachesLevel5()
        {
            // Pure math check, independent of combat outcomes.
            int xp = MusterXp;
            foreach (var (zoneId, questXp) in ZoneChain)
            {
                xp += ZoneEncounters(zoneId).Where(e => e.required)
                    .Sum(e => e.units.Sum(u => MonsterLibrary.Get(u).Xp));
                xp += questXp;
            }
            Assert.True(xp >= ClassData.XpThresholds[4],
                $"required-content XP {xp} < level-5 threshold {ClassData.XpThresholds[4]}");
        }
    }
}
