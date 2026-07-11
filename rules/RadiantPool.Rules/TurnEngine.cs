using System;
using System.Collections.Generic;
using System.Linq;

namespace RadiantPool.Rules
{
    /// <summary>Per-turn action-economy budget (SRD: one action, one bonus action,
    /// movement up to speed).</summary>
    public sealed class TurnBudget
    {
        public bool ActionAvailable { get; set; } = true;
        public bool BonusActionAvailable { get; set; } = true;
        public int MovementRemaining { get; set; }

        public TurnBudget(int speed) => MovementRemaining = speed;
    }

    /// <summary>Server-side combat turn sequencer: initiative, turn order, budgets,
    /// round ticking. Grid geometry lives in the game layer (3b); this owns WHO acts
    /// and WHAT the rules allow.</summary>
    public sealed class TurnEngine
    {
        private readonly List<Creature> _order = new List<Creature>();
        private readonly Dictionary<string, int> _initiative = new Dictionary<string, int>();
        public int Round { get; private set; } = 1;
        private int _index;

        public IReadOnlyList<Creature> InitiativeOrder => _order;
        public Creature ActiveCreature => _order[_index];
        public TurnBudget ActiveBudget { get; private set; } = new TurnBudget(0);

        /// <summary>Rolls initiative (d20 + Dex mod, ties broken by Dex score then id)
        /// and starts round 1.</summary>
        public TurnEngine(IEnumerable<Creature> combatants, IRng rng)
        {
            foreach (var c in combatants)
            {
                _initiative[c.Id] = Dice.RollD20(rng).Value + c.Abilities.Modifier(Ability.Dex);
                _order.Add(c);
            }
            if (_order.Count == 0) throw new ArgumentException("No combatants.");
            _order.Sort((a, b) =>
            {
                int cmp = _initiative[b.Id].CompareTo(_initiative[a.Id]);
                if (cmp != 0) return cmp;
                cmp = b.Abilities[Ability.Dex].CompareTo(a.Abilities[Ability.Dex]);
                return cmp != 0 ? cmp : string.CompareOrdinal(a.Id, b.Id);
            });
            _index = 0;
            BeginTurn();
        }

        public int InitiativeOf(string creatureId) => _initiative[creatureId];

        private void BeginTurn()
        {
            var c = ActiveCreature;
            ActiveBudget = new TurnBudget(CanAct(c) ? c.Speed : 0);
            // Dodge and Shield last until the start of the creature's next turn.
            c.Conditions.Remove(ConditionType.Dodging);
            c.Conditions.Remove(ConditionType.Shielded);
        }

        /// <summary>True if the creature can take actions (not down/dead/asleep).</summary>
        public static bool CanAct(Creature c) =>
            !c.IsDead && !c.IsDown
            && !c.Conditions.Has(ConditionType.Unconscious)
            && !c.Conditions.Has(ConditionType.Asleep);

        /// <summary>Advances to the next living combatant's turn; ticks round durations
        /// when the top of the order comes back around.</summary>
        public Creature EndTurn()
        {
            do
            {
                _index++;
                if (_index >= _order.Count)
                {
                    _index = 0;
                    Round++;
                    foreach (var c in _order) c.Conditions.TickRound();
                }
            } while (ActiveCreature.IsDead);
            BeginTurn();
            return ActiveCreature;
        }

        public void SpendAction()
        {
            if (!ActiveBudget.ActionAvailable)
                throw new RuleViolationException("Action already used this turn.");
            ActiveBudget.ActionAvailable = false;
        }

        public void SpendBonusAction()
        {
            if (!ActiveBudget.BonusActionAvailable)
                throw new RuleViolationException("Bonus action already used this turn.");
            ActiveBudget.BonusActionAvailable = false;
        }

        public void SpendMovement(int feet)
        {
            if (feet < 0) throw new ArgumentException("feet must be >= 0");
            if (feet > ActiveBudget.MovementRemaining)
                throw new RuleViolationException(
                    $"Not enough movement ({ActiveBudget.MovementRemaining} ft left).");
            ActiveBudget.MovementRemaining -= feet;
        }

        /// <summary>Dash action: converts the action into extra movement.</summary>
        public void Dash()
        {
            SpendAction();
            ActiveBudget.MovementRemaining += ActiveCreature.Speed;
        }

        /// <summary>Dodge action: attacks against the creature have disadvantage until
        /// its next turn.</summary>
        public void Dodge()
        {
            SpendAction();
            ActiveCreature.Conditions.Add(ConditionType.Dodging, -1);
        }

        public bool CombatOver(out bool playersWon)
        {
            bool anyPlayerUp = _order.Any(c => c.IsPlayerCharacter && !c.IsDead && !c.IsDown);
            bool anyMonsterUp = _order.Any(c => !c.IsPlayerCharacter && !c.IsDead
                                                && !c.Conditions.Has(ConditionType.Asleep));
            playersWon = !anyMonsterUp;
            return !anyPlayerUp || !anyMonsterUp;
        }
    }
}
