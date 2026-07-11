using System;
using System.Collections.Generic;

namespace RadiantPool.Rules
{
    /// <summary>The 10 v1 SRD spells, defined with the same data model the /content JSON
    /// uses. ContentLoader can override these from disk; this in-code copy keeps the rules
    /// library self-contained for tests and the server.</summary>
    public static class SpellLibrary
    {
        public static readonly IReadOnlyDictionary<string, SpellDefinition> All = Build();

        public static SpellDefinition Get(string id) =>
            All.TryGetValue(id, out var s)
                ? s
                : throw new KeyNotFoundException($"Unknown spell '{id}'.");

        private static Dictionary<string, SpellDefinition> Build()
        {
            var spells = new List<SpellDefinition>
            {
                new SpellDefinition
                {
                    Id = "fire_bolt", Name = "Fire Bolt", Level = 0,
                    Delivery = SpellDelivery.SpellAttack, RangeFeet = 120,
                    Classes = { CharacterClass.Wizard },
                    Effects = { new EffectOp { Kind = EffectOpKind.Damage, Dice = "1d10",
                        DicePerCasterTier = "1d10", DamageType = DamageType.Fire } }
                },
                new SpellDefinition
                {
                    Id = "sacred_flame", Name = "Sacred Flame", Level = 0,
                    Delivery = SpellDelivery.SaveNegates, SaveAbility = Ability.Dex, RangeFeet = 60,
                    Classes = { CharacterClass.Cleric },
                    Effects = { new EffectOp { Kind = EffectOpKind.Damage, Dice = "1d8",
                        DicePerCasterTier = "1d8", DamageType = DamageType.Radiant } }
                },
                new SpellDefinition
                {
                    Id = "magic_missile", Name = "Magic Missile", Level = 1,
                    Delivery = SpellDelivery.AutoHit, RangeFeet = 120, MaxTargets = 3,
                    Classes = { CharacterClass.Wizard },
                    // One event per dart; TurnEngine passes one target entry per dart.
                    Effects = { new EffectOp { Kind = EffectOpKind.Damage, Dice = "1d4+1",
                        DamageType = DamageType.Force } }
                },
                new SpellDefinition
                {
                    Id = "burning_hands", Name = "Burning Hands", Level = 1,
                    Delivery = SpellDelivery.SaveHalf, SaveAbility = Ability.Dex,
                    RangeFeet = 15, AreaOfEffect = true, MaxTargets = 8,
                    Classes = { CharacterClass.Wizard },
                    Effects = { new EffectOp { Kind = EffectOpKind.Damage, Dice = "3d6",
                        DicePerExtraSlotLevel = "1d6", DamageType = DamageType.Fire } }
                },
                new SpellDefinition
                {
                    Id = "cure_wounds", Name = "Cure Wounds", Level = 1,
                    Delivery = SpellDelivery.NoRoll, RangeFeet = 5,
                    Classes = { CharacterClass.Cleric },
                    Effects = { new EffectOp { Kind = EffectOpKind.Heal, Dice = "1d8",
                        DicePerExtraSlotLevel = "1d8", AddSpellcastingMod = true } }
                },
                new SpellDefinition
                {
                    Id = "healing_word", Name = "Healing Word", Level = 1,
                    Delivery = SpellDelivery.NoRoll, RangeFeet = 60, IsBonusAction = true,
                    Classes = { CharacterClass.Cleric },
                    Effects = { new EffectOp { Kind = EffectOpKind.Heal, Dice = "1d4",
                        DicePerExtraSlotLevel = "1d4", AddSpellcastingMod = true } }
                },
                new SpellDefinition
                {
                    Id = "bless", Name = "Bless", Level = 1,
                    Delivery = SpellDelivery.NoRoll, RangeFeet = 30, MaxTargets = 3,
                    Classes = { CharacterClass.Cleric },
                    // Concentration approximated as fixed 10-round duration in v1 (flagged).
                    Effects = { new EffectOp { Kind = EffectOpKind.ApplyCondition,
                        Condition = ConditionType.Blessed, ConditionRounds = 10 } }
                },
                new SpellDefinition
                {
                    Id = "shield", Name = "Shield", Level = 1,
                    Delivery = SpellDelivery.NoRoll, RangeFeet = 0, IsReaction = true,
                    Classes = { CharacterClass.Wizard },
                    // +5 AC until start of caster's next turn (1 round).
                    Effects = { new EffectOp { Kind = EffectOpKind.ApplyCondition,
                        Condition = ConditionType.Shielded, ConditionRounds = 1 } }
                },
                new SpellDefinition
                {
                    Id = "sleep", Name = "Sleep", Level = 1,
                    Delivery = SpellDelivery.NoRoll, RangeFeet = 90, AreaOfEffect = true,
                    MaxTargets = 8,
                    Classes = { CharacterClass.Wizard },
                    Effects = { new EffectOp { Kind = EffectOpKind.SleepPool, Dice = "5d8" } }
                },
                new SpellDefinition
                {
                    Id = "guiding_bolt", Name = "Guiding Bolt", Level = 1,
                    Delivery = SpellDelivery.SpellAttack, RangeFeet = 120,
                    Classes = { CharacterClass.Cleric },
                    Effects =
                    {
                        new EffectOp { Kind = EffectOpKind.Damage, Dice = "4d6",
                            DicePerExtraSlotLevel = "1d6", DamageType = DamageType.Radiant },
                        new EffectOp { Kind = EffectOpKind.GrantAdvantageToNextAttacker }
                    }
                },
            };

            var dict = new Dictionary<string, SpellDefinition>();
            foreach (var s in spells) dict[s.Id] = s;
            return dict;
        }
    }
}
