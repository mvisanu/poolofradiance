using System;
using System.Collections.Generic;
using System.Linq;

namespace RadiantPool.Rules
{
    public enum ConditionType
    {
        Prone, Poisoned, Frightened, Restrained, Grappled,
        Unconscious, Stunned, Paralyzed, Blinded, Charmed, Asleep,
        Dodging, Blessed, Shielded,  // Dodging/Blessed/Shielded model action & spell effects
        Guided                       // Guiding Bolt rider: next attack vs target has advantage
    }

    /// <summary>An active condition/effect on a creature, with an optional duration in rounds
    /// (-1 = until removed by rule, e.g. Unconscious).</summary>
    public sealed class ActiveCondition
    {
        public ConditionType Type { get; }
        public int RoundsRemaining { get; set; }
        public string? SourceId { get; }

        public ActiveCondition(ConditionType type, int roundsRemaining = -1, string? sourceId = null)
        {
            Type = type;
            RoundsRemaining = roundsRemaining;
            SourceId = sourceId;
        }
    }

    public sealed class ConditionSet
    {
        private readonly List<ActiveCondition> _active = new List<ActiveCondition>();

        public IReadOnlyList<ActiveCondition> All => _active;

        public bool Has(ConditionType type) => _active.Any(c => c.Type == type);

        public void Add(ConditionType type, int rounds = -1, string? sourceId = null)
        {
            if (!Has(type))
                _active.Add(new ActiveCondition(type, rounds, sourceId));
        }

        public void Remove(ConditionType type) => _active.RemoveAll(c => c.Type == type);

        /// <summary>Ticks round-based durations; returns conditions that expired.</summary>
        public List<ConditionType> TickRound()
        {
            var expired = new List<ConditionType>();
            foreach (var c in _active.Where(c => c.RoundsRemaining > 0))
            {
                c.RoundsRemaining--;
                if (c.RoundsRemaining == 0) expired.Add(c.Type);
            }
            _active.RemoveAll(c => c.RoundsRemaining == 0);
            return expired;
        }
    }
}
