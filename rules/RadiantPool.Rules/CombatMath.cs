using System;

namespace RadiantPool.Rules
{
    public sealed class AttackDefinition
    {
        public string Id { get; }
        public string Name { get; }
        public string Description { get; }
        public int ToHitBonus { get; }
        public DiceExpression Damage { get; }
        public DamageType DamageType { get; }
        public int RangeFeet { get; }
        public CombatTargetType TargetType => CombatTargetType.Hostile;
        public string AnimationTrigger { get; }
        public string VisualEffectId { get; }
        public string SoundEffectId { get; }
        public double ImpactDelaySeconds { get; }
        public bool RequiresApproach { get; }
        public bool CriticalHitEligible { get; }
        public int CooldownRounds { get; }

        public AttackDefinition(string name, int toHitBonus, string damage,
            DamageType damageType, int rangeFeet = 5, string? id = null,
            string description = "", string animationTrigger = "Attack",
            string visualEffectId = "physical_hit", string soundEffectId = "weapon",
            double impactDelaySeconds = 0.25, bool requiresApproach = true,
            bool criticalHitEligible = true, int cooldownRounds = 0)
        {
            Id = string.IsNullOrWhiteSpace(id)
                ? name.Trim().ToLowerInvariant().Replace(' ', '_') : id;
            Name = name;
            Description = description;
            ToHitBonus = toHitBonus;
            Damage = DiceExpression.Parse(damage);
            DamageType = damageType;
            RangeFeet = rangeFeet;
            AnimationTrigger = animationTrigger;
            VisualEffectId = visualEffectId;
            SoundEffectId = soundEffectId;
            ImpactDelaySeconds = impactDelaySeconds;
            RequiresApproach = requiresApproach && rangeFeet <= 5;
            CriticalHitEligible = criticalHitEligible;
            CooldownRounds = Math.Max(0, cooldownRounds);
        }
    }

    public readonly struct AttackResult
    {
        public D20Result Roll { get; }
        public int Total { get; }
        public bool Hit { get; }
        public bool Critical { get; }
        public int DamageDealt { get; }
        public bool TargetDowned { get; }
        public bool TargetDied { get; }

        public AttackResult(D20Result roll, int total, bool hit, bool critical,
                            int damageDealt, bool targetDowned, bool targetDied)
        {
            Roll = roll; Total = total; Hit = hit; Critical = critical;
            DamageDealt = damageDealt; TargetDowned = targetDowned; TargetDied = targetDied;
        }
    }

    public readonly struct SaveResult
    {
        public D20Result Roll { get; }
        public int Total { get; }
        public bool Success { get; }

        public SaveResult(D20Result roll, int total, bool success)
        {
            Roll = roll; Total = total; Success = success;
        }
    }

    public static class CombatMath
    {
        /// <summary>Attack roll per SRD: d20 + bonus vs AC; nat 20 always hits and crits
        /// (doubling damage dice), nat 1 always misses. Attacks against unconscious
        /// targets within 5 ft have advantage and crit on any hit (melee).</summary>
        public static AttackResult ResolveAttack(Creature attacker, Creature target,
            AttackDefinition attack, IRng rng, Advantage advantage = Advantage.None)
        {
            bool pointBlankOnHelpless = target.Conditions.Has(ConditionType.Unconscious)
                                        && attack.RangeFeet <= 5;
            if (pointBlankOnHelpless && advantage == Advantage.None)
                advantage = Advantage.Advantage;
            if (target.Conditions.Has(ConditionType.Dodging))
                advantage = Combine(advantage, Advantage.Disadvantage);
            if (target.Conditions.Has(ConditionType.Guided))
            {
                advantage = Combine(advantage, Advantage.Advantage);
                target.Conditions.Remove(ConditionType.Guided); // consumed by this attack
            }

            var d20 = Dice.RollD20(rng, advantage);
            int blessBonus = attacker.Conditions.Has(ConditionType.Blessed)
                ? rng.Next(1, 4) : 0;
            // Runtime quest scaling and global easing both live in Difficulty.cs. PCs
            // continue to attack at full SRD values.
            int easing = attacker.IsPlayerCharacter ? 0 : Difficulty.MonsterToHitPenalty;
            int scaling = attacker.IsPlayerCharacter ? 0
                : Difficulty.MonsterToHitBonus(attacker.EncounterLevel);
            int total = d20.Value + attack.ToHitBonus + blessBonus - easing + scaling;

            bool hit = !d20.IsNat1 && (d20.IsNat20 || total >= target.ArmorClass);
            bool crit = attack.CriticalHitEligible
                        && hit && (d20.IsNat20 || pointBlankOnHelpless);

            if (!hit)
                return new AttackResult(d20, total, false, false, 0, false, false);

            bool wasDown = target.IsDown;
            int damage = RollDamage(attack.Damage, crit, rng);
            if (!attacker.IsPlayerCharacter)
                damage += Difficulty.MonsterDamageBonus(attacker.EncounterLevel);
            var outcome = target.TakeDamage(damage, attack.DamageType);
            // Crit damage while at 0 HP = two failures total; TakeDamage recorded one.
            if (crit && wasDown && !target.IsDead)
                target.RecordDeathSave(false);

            return new AttackResult(d20, total, true, crit,
                outcome.DamageDealt, outcome.BecameDown, outcome.Died || target.IsDead);
        }

        /// <summary>Crits double the number of damage dice (modifier unchanged).</summary>
        public static int RollDamage(DiceExpression damage, bool critical, IRng rng)
        {
            var result = damage.Roll(rng);
            int total = result.Total;
            if (critical)
                total += new DiceExpression(damage.Count, Math.Max(damage.Sides, 2), 0).Roll(rng).Total;
            return Math.Max(0, total);
        }

        public static SaveResult ResolveSave(Creature target, Ability ability, int dc, IRng rng,
            Advantage advantage = Advantage.None)
        {
            var d20 = Dice.RollD20(rng, advantage);
            int blessBonus = target.Conditions.Has(ConditionType.Blessed) ? rng.Next(1, 4) : 0;
            int total = d20.Value + target.SaveBonus(ability) + blessBonus;
            return new SaveResult(d20, total, total >= dc);
        }

        /// <summary>A downed PC rolls a death save at the start of its turn.</summary>
        public static SaveResult RollDeathSave(Creature creature, IRng rng)
        {
            var d20 = Dice.RollD20(rng);
            bool success = d20.Value >= 10;
            // nat 20: regain 1 HP; nat 1: two failures — both via the critical flag.
            creature.RecordDeathSave(success, critical: d20.IsNat20 || d20.IsNat1);
            return new SaveResult(d20, d20.Value, success);
        }

        public static Advantage Combine(Advantage a, Advantage b)
        {
            if (a == b) return a;
            if (a == Advantage.None) return b;
            if (b == Advantage.None) return a;
            return Advantage.None; // adv + disadv cancel (SRD)
        }
    }
}
