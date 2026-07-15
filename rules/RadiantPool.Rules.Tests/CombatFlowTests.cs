using RadiantPool.Rules;
using Xunit;

namespace RadiantPool.Rules.Tests
{
    public class CombatFlowTests
    {
        private static Creature Unit(string id, bool player = false, int hp = 10) =>
            new Creature(id, id, new AbilityScores(10, 10, 10, 10, 10, 10), 12, hp)
            { IsPlayerCharacter = player };

        [Fact]
        public void Targeting_RejectsFriendlyFireSelfAndDefeatedEnemies()
        {
            var hero = Unit("hero", player: true);
            var ally = Unit("ally", player: true);
            var enemy = Unit("enemy");

            Assert.True(CombatTargeting.Validate(hero, enemy, CombatTargetType.Hostile).Allowed);
            Assert.False(CombatTargeting.Validate(hero, ally, CombatTargetType.Hostile).Allowed);
            Assert.False(CombatTargeting.Validate(hero, hero, CombatTargetType.Hostile).Allowed);

            enemy.TakeDamage(enemy.MaxHp, DamageType.Fire);
            Assert.False(CombatTargeting.Validate(hero, enemy, CombatTargetType.Hostile).Allowed);
        }

        [Fact]
        public void Targeting_AllowsDownedAllyOnlyForExplicitRecoveryAbility()
        {
            var hero = Unit("hero", player: true);
            var ally = Unit("ally", player: true);
            ally.TakeDamage(ally.MaxHp, DamageType.Fire);

            Assert.False(CombatTargeting.Validate(
                hero, ally, CombatTargetType.Friendly).Allowed);
            Assert.True(CombatTargeting.Validate(
                hero, ally, CombatTargetType.Friendly, allowDowned: true).Allowed);
        }

        [Fact]
        public void BattleResult_HandlesMultipleCharactersOnBothTeams()
        {
            var h1 = Unit("h1", player: true);
            var h2 = Unit("h2", player: true);
            var e1 = Unit("e1");
            var e2 = Unit("e2");
            var all = new[] { h1, h2, e1, e2 };

            Assert.Equal(BattleResult.InProgress, BattleResultEvaluator.Evaluate(all));
            e1.TakeDamage(e1.MaxHp, DamageType.Fire);
            e2.TakeDamage(e2.MaxHp, DamageType.Fire);
            Assert.Equal(BattleResult.Victory, BattleResultEvaluator.Evaluate(all));

            var h3 = Unit("h3", player: true);
            var h4 = Unit("h4", player: true);
            var e3 = Unit("e3");
            h3.TakeDamage(h3.MaxHp, DamageType.Fire);
            h4.TakeDamage(h4.MaxHp, DamageType.Fire);
            Assert.Equal(BattleResult.Defeat,
                BattleResultEvaluator.Evaluate(new[] { h3, h4, e3 }));
        }

        [Fact]
        public void ActionQueue_RejectsDuplicateInputAndCompletesInOrder()
        {
            var queue = new CombatActionQueue<string>();
            Assert.True(queue.TryEnqueue("hero:attack:1", "attack"));
            Assert.False(queue.TryEnqueue("hero:attack:1", "duplicate"));
            Assert.True(queue.TryEnqueue("hero:move:1", "move"));

            Assert.True(queue.TryStartNext(out string first));
            Assert.Equal("attack", first);
            Assert.True(queue.IsResolving);
            Assert.False(queue.TryStartNext(out _));
            Assert.True(queue.Complete());

            Assert.True(queue.TryStartNext(out string second));
            Assert.Equal("move", second);
            Assert.True(queue.Complete());
            Assert.Equal(0, queue.Count);
        }

        [Fact]
        public void Timeline_MissingAnimationEventFallsBackAndTimesOutSafely()
        {
            var timeline = new CombatActionTimeline(
                startedAt: 10.0, impactDelay: 0.35, completionTimeout: 2.0);

            Assert.False(timeline.TryTakeImpact(10.34));
            Assert.True(timeline.TryTakeImpact(10.35));
            Assert.False(timeline.TryTakeImpact(11.0));
            Assert.False(timeline.TimedOut(11.99));
            Assert.True(timeline.TimedOut(12.0));

            var eventDriven = new CombatActionTimeline(20.0, 0.35, 2.0);
            Assert.True(eventDriven.TryTakeImpact(20.1, animationEventReceived: true));
        }
    }
}
