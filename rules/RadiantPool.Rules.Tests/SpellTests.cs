using System.Linq;
using RadiantPool.Rules;
using Xunit;

namespace RadiantPool.Rules.Tests
{
    public class SpellTests
    {
        private static CharacterSheet Wizard(int level = 1) =>
            new CharacterSheet("wiz", "Selra", Race.Elf, CharacterClass.Wizard,
                new AbilityScores(8, 14, 13, 15, 12, 10), level);   // Int 16 → DC13, atk +5

        private static CharacterSheet Cleric(int level = 1) =>
            new CharacterSheet("clr", "Korga", Race.Dwarf, CharacterClass.Cleric,
                new AbilityScores(14, 8, 14, 10, 15, 12), level);   // Wis 16 → DC13, mod +3

        private static Creature Enemy(int ac = 12, int hp = 10) =>
            new Creature("m1", "Skulker", new AbilityScores(11, 12, 12, 10, 10, 10), ac, hp);

        [Fact]
        public void Library_HasAllTenSpells()
        {
            var expected = new[] { "fire_bolt", "sacred_flame", "magic_missile", "burning_hands",
                "cure_wounds", "healing_word", "bless", "shield", "sleep", "guiding_bolt" };
            foreach (var id in expected)
                Assert.True(SpellLibrary.All.ContainsKey(id), $"missing {id}");
            Assert.Equal(10, SpellLibrary.All.Count);
        }

        [Fact]
        public void FireBolt_SpellAttack_HitDealsFireDamage_NoSlotUsed()
        {
            var w = Wizard();
            var e = Enemy(ac: 12, hp: 20);
            // d20=10 +5 = 15 hit; 1d10 = 6
            var events = SpellEngine.Cast(w, SpellLibrary.Get("fire_bolt"),
                new[] { e }, 0, new FixedRng(10, 6));
            Assert.Equal(14, e.CurrentHp);
            Assert.Equal(new[] { 2, 0, 0 }, w.SlotsRemaining); // cantrip: no slot
            Assert.Contains(events, ev => ev is SpellDamageEvent d && d.Damage == 6
                && d.DamageType == DamageType.Fire);
        }

        [Fact]
        public void FireBolt_ScalesAtCharacterLevel5()
        {
            var w = Wizard(5);
            var e = Enemy(ac: 1, hp: 50);
            // hit with d20=10; 2d10 = 6+7 = 13
            SpellEngine.Cast(w, SpellLibrary.Get("fire_bolt"), new[] { e }, 0,
                new FixedRng(10, 6, 7));
            Assert.Equal(37, e.CurrentHp);
        }

        [Fact]
        public void SacredFlame_SaveNegates()
        {
            var c = Cleric();
            var e1 = Enemy(hp: 20);
            // save d20=13 +1 dex = 14 ≥ DC13 → no damage, no dice consumed after save
            SpellEngine.Cast(c, SpellLibrary.Get("sacred_flame"), new[] { e1 }, 0,
                new FixedRng(13));
            Assert.Equal(20, e1.CurrentHp);

            var e2 = Enemy(hp: 20);
            // save d20=5 +1 = 6 < 13 → 1d8 = 8 radiant
            SpellEngine.Cast(c, SpellLibrary.Get("sacred_flame"), new[] { e2 }, 0,
                new FixedRng(5, 8));
            Assert.Equal(12, e2.CurrentHp);
        }

        [Fact]
        public void MagicMissile_AutoHits_ConsumesSlot_CapsDarts()
        {
            var w = Wizard();
            var e = Enemy(hp: 20);
            // 3 darts at one target: 1d4+1 each → (2+1)+(3+1)+(1+1) = 9
            var events = SpellEngine.Cast(w, SpellLibrary.Get("magic_missile"),
                new[] { e, e, e }, 1, new FixedRng(2, 3, 1));
            Assert.Equal(11, e.CurrentHp);
            Assert.Equal(new[] { 1, 0, 0 }, w.SlotsRemaining);
            Assert.Equal(3, events.OfType<SpellDamageEvent>().Count());

            // 4 darts on a level-1 slot is illegal
            var w2 = Wizard();
            Assert.Throws<RuleViolationException>(() =>
                SpellEngine.Cast(w2, SpellLibrary.Get("magic_missile"),
                    new[] { e, e, e, e }, 1, new FixedRng(1)));
        }

        [Fact]
        public void BurningHands_SaveHalves_UpcastAddsDice()
        {
            var w = Wizard(5);
            var e1 = Enemy(hp: 30);
            var e2 = Enemy(hp: 30);
            // e1 fails (d20=2+1=3): 3d6 = 4+5+6 = 15. e2 saves (d20=18+1): 15/2 = 7
            SpellEngine.Cast(w, SpellLibrary.Get("burning_hands"),
                new[] { e1, e2 }, 1, new FixedRng(2, 4, 5, 6, 18, 4, 5, 6));
            Assert.Equal(15, e1.CurrentHp);
            Assert.Equal(23, e2.CurrentHp);

            // Upcast at slot 2 → 4d6
            var w2 = Wizard(5);
            var e3 = Enemy(hp: 30);
            SpellEngine.Cast(w2, SpellLibrary.Get("burning_hands"),
                new[] { e3 }, 2, new FixedRng(2, 1, 1, 1, 1));
            Assert.Equal(26, e3.CurrentHp);
            Assert.Equal(new[] { 4, 2, 2 }, w2.SlotsRemaining);
        }

        [Fact]
        public void CureWounds_HealsWithModifier_CannotExceedMax()
        {
            var c = Cleric();
            var f = new CharacterSheet("f", "Bran", Race.Human, CharacterClass.Fighter,
                new AbilityScores(15, 13, 14, 8, 12, 10)); // 12 max hp
            f.TakeDamage(8, DamageType.Slashing);
            // 1d8 = 4, +3 Wis = 7 healed, capped at 8 missing → 7
            var events = SpellEngine.Cast(c, SpellLibrary.Get("cure_wounds"),
                new[] { (Creature)f }, 1, new FixedRng(4));
            Assert.Equal(11, f.CurrentHp);
            Assert.Equal(7, events.OfType<SpellHealEvent>().Single().Healed);
        }

        [Fact]
        public void HealingWord_IsBonusAction_RevivesDowned()
        {
            var spell = SpellLibrary.Get("healing_word");
            Assert.True(spell.IsBonusAction);

            var c = Cleric();
            var f = new CharacterSheet("f", "Bran", Race.Human, CharacterClass.Fighter,
                new AbilityScores(15, 13, 14, 8, 12, 10));
            f.TakeDamage(12, DamageType.Slashing);
            Assert.True(f.IsDown);
            SpellEngine.Cast(c, spell, new[] { (Creature)f }, 1, new FixedRng(1));
            Assert.False(f.IsDown);
            Assert.Equal(4, f.CurrentHp); // 1d4=1 +3
        }

        [Fact]
        public void Bless_AppliesToThreeTargets()
        {
            var c = Cleric();
            var t1 = Enemy(); var t2 = Enemy(); var t3 = Enemy();
            var events = SpellEngine.Cast(c, SpellLibrary.Get("bless"),
                new Creature[] { t1, t2, t3 }, 1, new FixedRng());
            Assert.All(new[] { t1, t2, t3 },
                t => Assert.True(t.Conditions.Has(ConditionType.Blessed)));
            Assert.Equal(new[] { 1, 0, 0 }, c.SlotsRemaining);

            var c2 = Cleric();
            Assert.Throws<RuleViolationException>(() =>
                SpellEngine.Cast(c2, SpellLibrary.Get("bless"),
                    new Creature[] { t1, t2, t3, Enemy() }, 1, new FixedRng()));
        }

        [Fact]
        public void Shield_GivesPlus5Ac_ExpiresAfterRoundTick()
        {
            var w = Wizard();
            int baseAc = w.ArmorClass;
            SpellEngine.Cast(w, SpellLibrary.Get("shield"), new[] { (Creature)w }, 1,
                new FixedRng());
            Assert.Equal(baseAc + 5, w.ArmorClass);
            w.Conditions.TickRound();
            Assert.Equal(baseAc, w.ArmorClass);
        }

        [Fact]
        public void Sleep_PoolDropsWeakestFirst()
        {
            var w = Wizard();
            var weak = Enemy(hp: 5);
            var mid = new Creature("m2", "Mid", new AbilityScores(10, 10, 10, 10, 10, 10), 12, 12);
            var strong = new Creature("m3", "Strong", new AbilityScores(10, 10, 10, 10, 10, 10), 12, 30);
            // 5d8 pool = 4+4+4+4+4 = 20 → sleeps weak(5) and mid(12), 3 left < 30
            SpellEngine.CastSleep(w, SpellLibrary.Get("sleep"),
                new[] { weak, mid, strong }, 1, new FixedRng(4, 4, 4, 4, 4));
            Assert.True(weak.Conditions.Has(ConditionType.Asleep));
            Assert.True(mid.Conditions.Has(ConditionType.Asleep));
            Assert.False(strong.Conditions.Has(ConditionType.Asleep));
        }

        [Fact]
        public void Sleep_DamageWakesTarget()
        {
            var e = Enemy(hp: 10);
            e.Conditions.Add(ConditionType.Asleep, 10);
            e.TakeDamage(3, DamageType.Piercing);
            Assert.False(e.Conditions.Has(ConditionType.Asleep));
        }

        [Fact]
        public void GuidingBolt_DealsRadiantAndMarksTarget()
        {
            var c = Cleric();
            var e = Enemy(ac: 10, hp: 30);
            // d20=10 +5 = 15 hit; 4d6 = 1+2+3+4 = 10
            SpellEngine.Cast(c, SpellLibrary.Get("guiding_bolt"), new[] { e }, 1,
                new FixedRng(10, 1, 2, 3, 4));
            Assert.Equal(20, e.CurrentHp);
            Assert.True(e.Conditions.Has(ConditionType.Guided));
        }

        [Fact]
        public void CastingLeveledSpell_WithoutSlot_Throws()
        {
            var w = Wizard();
            w.ConsumeSlot(1); w.ConsumeSlot(1);
            Assert.Throws<RuleViolationException>(() =>
                SpellEngine.Cast(w, SpellLibrary.Get("magic_missile"),
                    new[] { Enemy() }, 1, new FixedRng()));
        }
    }
}
