using System;
using System.Collections.Generic;
using System.Linq;

namespace RadiantPool.Rules
{
    public enum SpellDelivery
    {
        AutoHit,        // Magic Missile
        SpellAttack,    // Fire Bolt, Guiding Bolt
        SaveNegates,    // Sacred Flame (no damage on save)
        SaveHalf,       // Burning Hands
        NoRoll          // Cure Wounds, Bless, Shield, Healing Word
    }

    public enum EffectOpKind
    {
        Damage, Heal, ApplyCondition, GrantAdvantageToNextAttacker /*Guiding Bolt rider*/, SleepPool
    }

    /// <summary>One atomic thing a spell does. Spells are lists of these — new spells
    /// are data, not code.</summary>
    public sealed class EffectOp
    {
        public EffectOpKind Kind { get; set; }
        public string? Dice { get; set; }                 // "1d10", scaling handled below
        public string? DicePerExtraSlotLevel { get; set; }// e.g. Burning Hands "1d6"
        public string? DicePerCasterTier { get; set; }    // extra die at character levels 5, 11, and 17
        public DamageType? DamageType { get; set; }
        public ConditionType? Condition { get; set; }
        public int ConditionRounds { get; set; } = -1;
        public bool AddSpellcastingMod { get; set; }      // Cure Wounds/Healing Word add mod
    }

    public sealed class SpellDefinition
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string Description { get; set; } = "";
        public int Level { get; set; }                    // 0 = cantrip
        public SpellDelivery Delivery { get; set; }
        public Ability SaveAbility { get; set; }
        public bool IsBonusAction { get; set; }           // Healing Word
        public bool IsReaction { get; set; }              // Shield
        public int RangeFeet { get; set; }
        public int MaxTargets { get; set; } = 1;          // Bless = 3, Magic Missile darts = 3 (+1/slot)
        public bool AreaOfEffect { get; set; }            // Burning Hands cone, Sleep sphere
        public CombatTargetType TargetType { get; set; } = CombatTargetType.Hostile;
        public bool AllowDownedTarget { get; set; }
        public string AnimationTrigger { get; set; } = "Cast";
        public string VisualEffectId { get; set; } = "";
        public string SoundEffectId { get; set; } = "";
        public double ImpactDelaySeconds { get; set; } = 0.35;
        public bool RequiresApproach { get; set; }
        public bool CriticalHitEligible { get; set; }
        public int CooldownRounds { get; set; }
        public List<EffectOp> Effects { get; set; } = new List<EffectOp>();
        public List<CharacterClass> Classes { get; set; } = new List<CharacterClass>();
    }

    public abstract class SpellEvent
    {
        public string CasterId { get; set; } = "";
        public string SpellId { get; set; } = "";
    }

    public sealed class SpellAttackEvent : SpellEvent
    {
        public string TargetId { get; set; } = "";
        public AttackResult Result { get; set; }
    }

    public sealed class SpellSaveEvent : SpellEvent
    {
        public string TargetId { get; set; } = "";
        public SaveResult Result { get; set; }
        public int Dc { get; set; }
    }

    public sealed class SpellDamageEvent : SpellEvent
    {
        public string TargetId { get; set; } = "";
        public int Damage { get; set; }
        public DamageType DamageType { get; set; }
        public bool TargetDowned { get; set; }
        public bool TargetDied { get; set; }
    }

    public sealed class SpellHealEvent : SpellEvent
    {
        public string TargetId { get; set; } = "";
        public int Healed { get; set; }
    }

    public sealed class SpellConditionEvent : SpellEvent
    {
        public string TargetId { get; set; } = "";
        public ConditionType Condition { get; set; }
        public bool Applied { get; set; }   // false = resisted/immune
    }

    public static class SpellEngine
    {
        /// <summary>Casts a spell at targets. Caller (TurnEngine/server) has already
        /// validated slot availability, range, and action economy; this consumes the slot
        /// and resolves effects. Returns the ordered event list for replication.</summary>
        public static List<SpellEvent> Cast(CharacterSheet caster, SpellDefinition spell,
            IReadOnlyList<Creature> targets, int slotLevel, IRng rng)
        {
            if (spell.Level > 0)
            {
                if (slotLevel < spell.Level)
                    throw new RuleViolationException(
                        $"{spell.Name} needs a slot of level {spell.Level}+.");
                caster.ConsumeSlot(slotLevel);
            }

            var events = new List<SpellEvent>();
            int upcast = spell.Level > 0 ? slotLevel - spell.Level : 0;
            // Cantrip damage gains one die at character levels 5, 11, and 17.
            int cantripTier = spell.Level == 0
                ? (caster.Level >= 17 ? 3 : caster.Level >= 11 ? 2 : caster.Level >= 5 ? 1 : 0)
                : 0;

            foreach (var target in ResolveTargets(spell, targets, upcast))
            {
                switch (spell.Delivery)
                {
                    case SpellDelivery.SpellAttack:
                    {
                        var attack = new AttackDefinition(spell.Name, caster.SpellAttackBonus,
                            "0", DamageType.Force, spell.RangeFeet);
                        var d20 = Dice.RollD20(rng, target.Conditions.Has(ConditionType.Dodging)
                            ? Advantage.Disadvantage : Advantage.None);
                        int blessBonus = caster.Conditions.Has(ConditionType.Blessed) ? rng.Next(1, 4) : 0;
                        int total = d20.Value + caster.SpellAttackBonus + blessBonus;
                        bool hit = !d20.IsNat1 && (d20.IsNat20 || total >= target.ArmorClass);
                        events.Add(new SpellAttackEvent
                        {
                            CasterId = caster.Id, SpellId = spell.Id, TargetId = target.Id,
                            Result = new AttackResult(d20, total, hit, hit && d20.IsNat20, 0, false, false)
                        });
                        if (hit)
                            ApplyEffects(caster, spell, target, upcast, cantripTier,
                                crit: spell.CriticalHitEligible && d20.IsNat20,
                                halve: false, rng, events);
                        break;
                    }
                    case SpellDelivery.SaveNegates:
                    case SpellDelivery.SaveHalf:
                    {
                        var save = CombatMath.ResolveSave(target, spell.SaveAbility,
                            caster.SpellSaveDc, rng);
                        events.Add(new SpellSaveEvent
                        {
                            CasterId = caster.Id, SpellId = spell.Id, TargetId = target.Id,
                            Result = save, Dc = caster.SpellSaveDc
                        });
                        if (save.Success && spell.Delivery == SpellDelivery.SaveNegates)
                            break;
                        ApplyEffects(caster, spell, target, upcast, cantripTier,
                            crit: false, halve: save.Success, rng, events);
                        break;
                    }
                    default:
                        ApplyEffects(caster, spell, target, upcast, cantripTier,
                            crit: false, halve: false, rng, events);
                        break;
                }
            }
            return events;
        }

        private static IEnumerable<Creature> ResolveTargets(SpellDefinition spell,
            IReadOnlyList<Creature> targets, int upcast)
        {
            // Magic Missile: 3 darts +1 per upcast level; caller passes one entry per dart
            // (or fewer targets to stack darts) — we just cap total effect instances.
            int max = spell.MaxTargets + (spell.Id == "magic_missile" ? upcast : 0);
            if (spell.Id == "bless") max = spell.MaxTargets + upcast;
            if (targets.Count > max)
                throw new RuleViolationException($"{spell.Name} allows at most {max} targets.");
            return targets;
        }

        private static void ApplyEffects(CharacterSheet caster, SpellDefinition spell,
            Creature target, int upcast, int cantripTier, bool crit, bool halve,
            IRng rng, List<SpellEvent> events)
        {
            foreach (var op in spell.Effects)
            {
                switch (op.Kind)
                {
                    case EffectOpKind.Damage:
                    {
                        var expr = ScaledDice(op, upcast, cantripTier);
                        int dmg = CombatMath.RollDamage(expr, crit, rng);
                        if (halve) dmg /= 2;
                        var outcome = target.TakeDamage(dmg, op.DamageType!.Value);
                        events.Add(new SpellDamageEvent
                        {
                            CasterId = caster.Id, SpellId = spell.Id, TargetId = target.Id,
                            Damage = outcome.DamageDealt, DamageType = op.DamageType.Value,
                            TargetDowned = outcome.BecameDown, TargetDied = outcome.Died
                        });
                        break;
                    }
                    case EffectOpKind.Heal:
                    {
                        var expr = ScaledDice(op, upcast, cantripTier);
                        int amount = expr.Roll(rng).Total;
                        if (op.AddSpellcastingMod)
                            amount += caster.Abilities.Modifier(caster.SpellcastingAbility!.Value);
                        int healed = target.Heal(Math.Max(0, amount));
                        events.Add(new SpellHealEvent
                        {
                            CasterId = caster.Id, SpellId = spell.Id,
                            TargetId = target.Id, Healed = healed
                        });
                        break;
                    }
                    case EffectOpKind.ApplyCondition:
                    {
                        target.Conditions.Add(op.Condition!.Value, op.ConditionRounds, caster.Id);
                        events.Add(new SpellConditionEvent
                        {
                            CasterId = caster.Id, SpellId = spell.Id, TargetId = target.Id,
                            Condition = op.Condition.Value, Applied = true
                        });
                        break;
                    }
                    case EffectOpKind.GrantAdvantageToNextAttacker:
                    {
                        target.Conditions.Add(ConditionType.Guided, 1, caster.Id);
                        events.Add(new SpellConditionEvent
                        {
                            CasterId = caster.Id, SpellId = spell.Id, TargetId = target.Id,
                            Condition = ConditionType.Guided, Applied = true
                        });
                        break;
                    }
                    case EffectOpKind.SleepPool:
                    {
                        // Handled at multi-target level by CastSleep; single-target fallback:
                        int pool = ScaledDice(op, upcast, 0).Roll(rng).Total;
                        if (target.CurrentHp > 0 && target.CurrentHp <= pool)
                        {
                            target.Conditions.Add(ConditionType.Asleep, 10, caster.Id); // 1 minute
                            events.Add(new SpellConditionEvent
                            {
                                CasterId = caster.Id, SpellId = spell.Id, TargetId = target.Id,
                                Condition = ConditionType.Asleep, Applied = true
                            });
                        }
                        break;
                    }
                }
            }
        }

        /// <summary>Sleep needs whole-group resolution: 5d8 HP pool, ascending current HP
        /// order, undead/immune skipped (v1: nothing immune among our monsters).</summary>
        public static List<SpellEvent> CastSleep(CharacterSheet caster, SpellDefinition spell,
            IReadOnlyList<Creature> creaturesInArea, int slotLevel, IRng rng)
        {
            caster.ConsumeSlot(slotLevel);
            int upcast = slotLevel - spell.Level;
            int pool = DiceExpression.Parse($"{5 + 2 * upcast}d8").Roll(rng).Total;
            var events = new List<SpellEvent>();
            foreach (var c in creaturesInArea.Where(c => c.CurrentHp > 0)
                                             .OrderBy(c => c.CurrentHp))
            {
                if (c.CurrentHp > pool) break;
                pool -= c.CurrentHp;
                c.Conditions.Add(ConditionType.Asleep, 10, caster.Id);
                events.Add(new SpellConditionEvent
                {
                    CasterId = caster.Id, SpellId = spell.Id, TargetId = c.Id,
                    Condition = ConditionType.Asleep, Applied = true
                });
            }
            return events;
        }

        private static DiceExpression ScaledDice(EffectOp op, int upcast, int cantripTier)
        {
            var baseExpr = DiceExpression.Parse(op.Dice ?? "0");
            int extraDice = 0;
            if (upcast > 0 && op.DicePerExtraSlotLevel != null)
                extraDice += DiceExpression.Parse(op.DicePerExtraSlotLevel).Count * upcast;
            if (cantripTier > 0 && op.DicePerCasterTier != null)
                extraDice += DiceExpression.Parse(op.DicePerCasterTier).Count * cantripTier;
            return new DiceExpression(baseExpr.Count + extraDice,
                Math.Max(baseExpr.Sides, 2), baseExpr.Modifier);
        }
    }
}
