using System;
using System.Collections.Generic;

namespace RadiantPool.Rules
{
    /// <summary>Runtime combat state shared by player characters and monsters.
    /// The server owns the authoritative instance; clients get replicated snapshots.</summary>
    public class Creature
    {
        public string Id { get; }
        public string Name { get; }
        public AbilityScores Abilities { get; }
        public int BaseArmorClass { get; set; }
        public int MaxHp { get; protected set; }
        public int CurrentHp { get; private set; }
        public int TempHp { get; private set; }
        public int Speed { get; set; } = 30;
        public int ProficiencyBonus { get; set; } = 2;
        public ConditionSet Conditions { get; } = new ConditionSet();
        public HashSet<Ability> SaveProficiencies { get; } = new HashSet<Ability>();
        public HashSet<DamageType> Resistances { get; } = new HashSet<DamageType>();
        public HashSet<DamageType> Immunities { get; } = new HashSet<DamageType>();

        // Death saves (PCs). Monsters die at 0 HP (IsPlayerCharacter=false).
        public bool IsPlayerCharacter { get; set; }
        public int DeathSaveSuccesses { get; private set; }
        public int DeathSaveFailures { get; private set; }
        public bool IsDead { get; private set; }
        public bool IsDown => CurrentHp == 0 && !IsDead;
        public bool IsStable { get; private set; }

        public Creature(string id, string name, AbilityScores abilities, int baseAc, int maxHp)
        {
            Id = id;
            Name = name;
            Abilities = abilities;
            BaseArmorClass = baseAc;
            MaxHp = maxHp;
            CurrentHp = maxHp;
        }

        /// <summary>Effective AC including condition effects (e.g. Shield spell +5).</summary>
        public int ArmorClass =>
            BaseArmorClass + (Conditions.Has(ConditionType.Shielded) ? 5 : 0);

        public int SaveBonus(Ability a) =>
            Abilities.Modifier(a) + (SaveProficiencies.Contains(a) ? ProficiencyBonus : 0);

        public void GrantTempHp(int amount) => TempHp = Math.Max(TempHp, amount);

        /// <summary>Applies damage honoring temp HP, resistance/immunity, and 0-HP rules.
        /// Returns the damage actually dealt to real HP.</summary>
        public DamageOutcome TakeDamage(int amount, DamageType type)
        {
            if (amount < 0) throw new ArgumentException("damage must be >= 0");
            if (Immunities.Contains(type)) amount = 0;
            else if (Resistances.Contains(type)) amount /= 2;

            int fromTemp = Math.Min(TempHp, amount);
            TempHp -= fromTemp;
            int remaining = amount - fromTemp;

            bool wasDown = IsDown;
            int applied = Math.Min(CurrentHp, remaining);
            CurrentHp -= applied;

            bool becameDown = false, died = false;
            if (wasDown && remaining > 0 && !IsDead)
            {
                // Damage while at 0 HP = death save failure (a second failure for
                // crits is added by the caller, which knows the attack was critical).
                IsStable = false;
                RecordDeathSave(false);
                died = IsDead;
            }
            else if (CurrentHp == 0 && applied > 0)
            {
                if (!IsPlayerCharacter)
                {
                    IsDead = true; died = true;
                }
                else if (remaining - applied >= MaxHp)
                {
                    // Massive damage instant death (SRD).
                    IsDead = true; died = true;
                }
                else
                {
                    becameDown = true;
                    IsStable = false;
                    Conditions.Add(ConditionType.Unconscious);
                    // Sleeping creatures that take damage wake; unconscious-at-0 supersedes.
                    Conditions.Remove(ConditionType.Asleep);
                }
            }
            else if (applied > 0)
            {
                // Any damage wakes a sleeping creature.
                Conditions.Remove(ConditionType.Asleep);
            }

            return new DamageOutcome(applied + fromTemp, becameDown, died);
        }

        public int Heal(int amount)
        {
            if (amount < 0) throw new ArgumentException("healing must be >= 0");
            if (IsDead) return 0;
            bool wasDown = IsDown;
            int healed = Math.Min(MaxHp - CurrentHp, amount);
            CurrentHp += healed;
            if (wasDown && CurrentHp > 0)
            {
                Conditions.Remove(ConditionType.Unconscious);
                ResetDeathSaves();
            }
            return healed;
        }

        public void RecordDeathSave(bool success, bool critical = false)
        {
            if (!IsDown || IsStable) return;
            if (success)
            {
                if (critical) { Heal(1); return; }  // nat 20: back up with 1 HP
                DeathSaveSuccesses++;
                if (DeathSaveSuccesses >= 3) { IsStable = true; }
            }
            else
            {
                DeathSaveFailures += critical ? 2 : 1;
                if (DeathSaveFailures >= 3) IsDead = true;
            }
        }

        public void ResetDeathSaves()
        {
            DeathSaveSuccesses = 0;
            DeathSaveFailures = 0;
            IsStable = false;
        }

        /// <summary>Full restore (long rest / respawn).</summary>
        public void RestoreFull()
        {
            if (IsDead) return;
            CurrentHp = MaxHp;
            TempHp = 0;
            Conditions.Remove(ConditionType.Unconscious);
            ResetDeathSaves();
        }

        /// <summary>Party-wipe recovery: clears death and fully restores. Server-only
        /// path used when the party is carried back to the hub (no permadeath v1).</summary>
        public void ReviveFull()
        {
            IsDead = false;
            RestoreFull();
        }
    }

    public readonly struct DamageOutcome
    {
        public int DamageDealt { get; }
        public bool BecameDown { get; }
        public bool Died { get; }

        public DamageOutcome(int damageDealt, bool becameDown, bool died)
        {
            DamageDealt = damageDealt;
            BecameDown = becameDown;
            Died = died;
        }
    }
}
