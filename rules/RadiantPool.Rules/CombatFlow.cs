using System;
using System.Collections.Generic;
using System.Linq;

namespace RadiantPool.Rules
{
    /// <summary>The one authoritative phase of a battle. Presentation may observe this
    /// value, but only the server advances it.</summary>
    public enum BattleState
    {
        Inactive,
        Initializing,
        StartingBattle,
        CalculatingTurnOrder,
        WaitingForPlayerInput,
        SelectingAction,
        SelectingTarget,
        ExecutingPlayerAction,
        ExecutingEnemyAction,
        ApplyingDamage,
        UpdatingUi,
        CheckingBattleResult,
        Victory,
        Defeat,
        Paused
    }

    public enum CombatTargetType
    {
        Hostile,
        Friendly,
        Self,
        AnyLiving
    }

    public readonly struct TargetValidation
    {
        public bool Allowed { get; }
        public string Reason { get; }

        private TargetValidation(bool allowed, string reason)
        {
            Allowed = allowed;
            Reason = reason;
        }

        public static TargetValidation Accept() => new TargetValidation(true, "");
        public static TargetValidation Reject(string reason) =>
            new TargetValidation(false, reason);
    }

    /// <summary>Team, self-target, and defeated-target checks shared by attacks and
    /// spells. Range and line-of-sight remain game-layer geometry.</summary>
    public static class CombatTargeting
    {
        public static TargetValidation Validate(Creature? actor, Creature? target,
            CombatTargetType targetType, bool allowDowned = false)
        {
            if (actor == null) return TargetValidation.Reject("Missing acting character.");
            if (target == null) return TargetValidation.Reject("Missing target.");
            if (actor.IsDead || actor.IsDown)
                return TargetValidation.Reject("A defeated character cannot act.");
            if (target.IsDead)
                return TargetValidation.Reject("Defeated characters cannot be targeted.");
            if (target.IsDown && !allowDowned)
                return TargetValidation.Reject("That target is already down.");

            bool same = ReferenceEquals(actor, target) || actor.Id == target.Id;
            bool sameTeam = actor.IsPlayerCharacter == target.IsPlayerCharacter;
            switch (targetType)
            {
                case CombatTargetType.Hostile:
                    if (same || sameTeam)
                        return TargetValidation.Reject("Choose a living enemy target.");
                    break;
                case CombatTargetType.Friendly:
                    if (!sameTeam)
                        return TargetValidation.Reject("Choose a living ally target.");
                    break;
                case CombatTargetType.Self:
                    if (!same)
                        return TargetValidation.Reject("This ability targets only its caster.");
                    break;
                case CombatTargetType.AnyLiving:
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(targetType));
            }
            return TargetValidation.Accept();
        }
    }

    public enum BattleResult
    {
        InProgress,
        Victory,
        Defeat
    }

    /// <summary>Pure battle completion policy, separate from rewards, UI, and animation.</summary>
    public static class BattleResultEvaluator
    {
        public static BattleResult Evaluate(IEnumerable<Creature> combatants)
        {
            if (combatants == null) throw new ArgumentNullException(nameof(combatants));
            var all = combatants.ToList();
            if (all.Count == 0) return BattleResult.InProgress;

            bool anyPlayerUp = all.Any(c => c.IsPlayerCharacter && !c.IsDead && !c.IsDown);
            bool anyEnemyUp = all.Any(c => !c.IsPlayerCharacter && !c.IsDead
                                            && !c.Conditions.Has(ConditionType.Asleep));
            if (!anyPlayerUp) return BattleResult.Defeat;
            return anyEnemyUp ? BattleResult.InProgress : BattleResult.Victory;
        }
    }

    /// <summary>A small serial action queue. Keys reject double-clicks while an action is
    /// queued or resolving; Complete always releases the active key.</summary>
    public sealed class CombatActionQueue<T>
    {
        private readonly Queue<(string Key, T Action)> _pending =
            new Queue<(string Key, T Action)>();
        private readonly HashSet<string> _keys = new HashSet<string>(StringComparer.Ordinal);
        private string? _activeKey;

        public bool IsResolving => _activeKey != null;
        public int PendingCount => _pending.Count;
        public int Count => _pending.Count + (IsResolving ? 1 : 0);

        public bool TryEnqueue(string key, T action)
        {
            if (string.IsNullOrWhiteSpace(key))
                throw new ArgumentException("An action key is required.", nameof(key));
            if (!_keys.Add(key)) return false;
            _pending.Enqueue((key, action));
            return true;
        }

        public bool TryStartNext(out T action)
        {
            if (IsResolving || _pending.Count == 0)
            {
                action = default!;
                return false;
            }
            var next = _pending.Dequeue();
            _activeKey = next.Key;
            action = next.Action;
            return true;
        }

        public bool Complete()
        {
            if (_activeKey == null) return false;
            _keys.Remove(_activeKey);
            _activeKey = null;
            return true;
        }

        public void Clear()
        {
            _pending.Clear();
            _keys.Clear();
            _activeKey = null;
        }
    }

    /// <summary>Controlled action timing with an animation-event fast path and a timed
    /// fallback. A missing event can never leave an impact or completion permanently stuck.</summary>
    public sealed class CombatActionTimeline
    {
        private readonly double _impactAt;
        private readonly double _timeoutAt;
        private bool _impactTaken;

        public CombatActionTimeline(double startedAt, double impactDelay,
            double completionTimeout)
        {
            if (impactDelay < 0) throw new ArgumentOutOfRangeException(nameof(impactDelay));
            if (completionTimeout <= impactDelay)
                throw new ArgumentException("Completion timeout must follow impact.");
            _impactAt = startedAt + impactDelay;
            _timeoutAt = startedAt + completionTimeout;
        }

        public bool TryTakeImpact(double now, bool animationEventReceived = false)
        {
            if (_impactTaken || (!animationEventReceived && now < _impactAt)) return false;
            _impactTaken = true;
            return true;
        }

        public bool TimedOut(double now) => now >= _timeoutAt;
    }
}
