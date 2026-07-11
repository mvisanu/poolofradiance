using System.Linq;
using RadiantPool.Rules;
using Xunit;

namespace RadiantPool.Rules.Tests
{
    public class TurnEngineTests
    {
        private static Creature Unit(string id, int dex = 10, int hp = 10, bool pc = false) =>
            new Creature(id, id, new AbilityScores(10, dex, 10, 10, 10, 10), 12, hp)
            { IsPlayerCharacter = pc };

        [Fact]
        public void Initiative_OrdersByRollPlusDex()
        {
            var a = Unit("a", dex: 14);              // +2
            var b = Unit("b", dex: 10);              // +0
            // a rolls 5 → 7; b rolls 15 → 15. b first.
            var engine = new TurnEngine(new[] { a, b }, new FixedRng(5, 15));
            Assert.Equal(new[] { "b", "a" }, engine.InitiativeOrder.Select(c => c.Id));
            Assert.Equal("b", engine.ActiveCreature.Id);
        }

        [Fact]
        public void Initiative_TieBrokenByDexScore()
        {
            var a = Unit("a", dex: 14);  // roll 10 → 12
            var b = Unit("b", dex: 12);  // roll 11 → 12, lower dex
            var engine = new TurnEngine(new[] { a, b }, new FixedRng(10, 11));
            Assert.Equal(new[] { "a", "b" }, engine.InitiativeOrder.Select(c => c.Id));
        }

        [Fact]
        public void ActionEconomy_OneActionOneBonusPerTurn()
        {
            var a = Unit("a");
            var engine = new TurnEngine(new[] { a, Unit("b") }, new FixedRng(15, 5));
            engine.SpendAction();
            Assert.Throws<RuleViolationException>(() => engine.SpendAction());
            engine.SpendBonusAction();
            Assert.Throws<RuleViolationException>(() => engine.SpendBonusAction());
        }

        [Fact]
        public void Movement_LimitedBySpeed_DashDoubles()
        {
            var a = Unit("a");
            a.Speed = 30;
            var engine = new TurnEngine(new[] { a, Unit("b") }, new FixedRng(15, 5));
            engine.SpendMovement(25);
            Assert.Throws<RuleViolationException>(() => engine.SpendMovement(10));
            engine.Dash();
            engine.SpendMovement(35);
            Assert.Equal(0, engine.ActiveBudget.MovementRemaining);
        }

        [Fact]
        public void EndTurn_AdvancesAndSkipsDead_TicksRoundsAtWrap()
        {
            var a = Unit("a", dex: 18);
            var b = Unit("b", dex: 14);
            var c = Unit("c", dex: 10);
            var engine = new TurnEngine(new[] { a, b, c }, new FixedRng(10, 10, 10));
            Assert.Equal("a", engine.ActiveCreature.Id);

            b.TakeDamage(100, DamageType.Fire); // b dies (monster)
            Assert.Equal("c", engine.EndTurn().Id);

            a.Conditions.Add(ConditionType.Blessed, 1);
            Assert.Equal("a", engine.EndTurn().Id); // wrap → round 2, durations tick
            Assert.Equal(2, engine.Round);
            Assert.False(a.Conditions.Has(ConditionType.Blessed));
        }

        [Fact]
        public void Dodge_SetsCondition_ClearedAtOwnNextTurn()
        {
            var a = Unit("a", dex: 18);
            var b = Unit("b", dex: 10);
            var engine = new TurnEngine(new[] { a, b }, new FixedRng(10, 10));
            engine.Dodge();
            Assert.True(a.Conditions.Has(ConditionType.Dodging));
            engine.EndTurn();                       // b's turn — still dodging
            Assert.True(a.Conditions.Has(ConditionType.Dodging));
            engine.EndTurn();                       // back to a — cleared
            Assert.False(a.Conditions.Has(ConditionType.Dodging));
        }

        [Fact]
        public void DownedPc_StillGetsTurn_WithNoMovement()
        {
            var pc = Unit("pc", dex: 18, pc: true);
            var m = Unit("m", dex: 10);
            var engine = new TurnEngine(new[] { pc, m }, new FixedRng(10, 10));
            pc.TakeDamage(10, DamageType.Slashing);
            Assert.True(pc.IsDown);
            engine.EndTurn();                       // m
            Assert.Equal("pc", engine.EndTurn().Id); // downed PC keeps its slot
            Assert.Equal(0, engine.ActiveBudget.MovementRemaining);
        }

        [Fact]
        public void CombatOver_WhenOneSideEliminated()
        {
            var pc = Unit("pc", dex: 18, pc: true);
            var m1 = Unit("m1");
            var m2 = Unit("m2");
            var engine = new TurnEngine(new[] { pc, m1, m2 }, new FixedRng(10, 10, 10));
            Assert.False(engine.CombatOver(out _));

            m1.TakeDamage(100, DamageType.Fire);
            m2.Conditions.Add(ConditionType.Asleep, 10);  // asleep counts as neutralized
            Assert.True(engine.CombatOver(out bool playersWon));
            Assert.True(playersWon);

            var pc2 = Unit("pc2", pc: true);
            var m3 = Unit("m3");
            var e2 = new TurnEngine(new[] { pc2, m3 }, new FixedRng(10, 10));
            pc2.TakeDamage(10, DamageType.Slashing); // pc down
            Assert.True(e2.CombatOver(out bool won2));
            Assert.False(won2);
        }

        [Fact]
        public void MonsterSpawn_HpFromDice_AttacksResolve()
        {
            var def = MonsterLibrary.Get("marsh_skulker");
            var m = def.Spawn("m1", new FixedRng(4, 5)); // 2d8+2 → 11
            Assert.Equal(11, m.MaxHp);
            Assert.False(m.IsPlayerCharacter);

            var pc = Unit("pc", pc: true, hp: 20);
            // Rusty Blade +3: d20=12 +3 = 15 ≥ 12 → 1d6=4 +1 = 5
            var r = CombatMath.ResolveAttack(m, pc, def.Attacks[0], new FixedRng(12, 4));
            Assert.True(r.Hit);
            Assert.Equal(15, pc.CurrentHp);
        }

        [Fact]
        public void FullSkirmish_TwoPcsVsTwoSkulkers_Deterministic()
        {
            // Integration: a seeded 2v2 runs to completion without rule violations.
            var rng = new SeededRng(1234);
            var f = new CharacterSheet("f", "Bran", Race.Human, CharacterClass.Fighter,
                new AbilityScores(15, 13, 14, 8, 12, 10));
            f.EquipArmor(ArmorDefinition.ScaleMail);
            var w = new CharacterSheet("w", "Selra", Race.Elf, CharacterClass.Wizard,
                new AbilityScores(8, 14, 13, 15, 12, 10));
            var def = MonsterLibrary.Get("marsh_skulker");
            var m1 = def.Spawn("m1", rng, averageHp: true);
            var m2 = def.Spawn("m2", rng, averageHp: true);
            var sword = new AttackDefinition("Longsword", 5, "1d8+3", DamageType.Slashing);

            var engine = new TurnEngine(new Creature[] { f, w, m1, m2 }, rng);
            int safety = 200;
            while (!engine.CombatOver(out _) && safety-- > 0)
            {
                var active = engine.ActiveCreature;
                if (TurnEngine.CanAct(active))
                {
                    engine.SpendAction();
                    if (active == f)
                    {
                        var target = m1.IsDead ? m2 : m1;
                        if (!target.IsDead) CombatMath.ResolveAttack(f, target, sword, rng);
                    }
                    else if (active == w)
                    {
                        var target = m1.IsDead ? m2 : m1;
                        if (!target.IsDead)
                            SpellEngine.Cast(w, SpellLibrary.Get("fire_bolt"),
                                new[] { target }, 0, rng);
                    }
                    else
                    {
                        var target = !f.IsDead && !f.IsDown ? (Creature)f : w;
                        if (!target.IsDead && !target.IsDown)
                            CombatMath.ResolveAttack(active, target,
                                def.Attacks[0], rng);
                    }
                }
                else if (active.IsPlayerCharacter && active.IsDown && !active.IsStable)
                {
                    CombatMath.RollDeathSave(active, rng);
                }
                engine.EndTurn();
            }
            Assert.True(safety > 0, "combat did not terminate");
        }
    }
}
