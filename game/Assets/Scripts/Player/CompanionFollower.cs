using System.Linq;
using FishNet.Object;
using UnityEngine;

namespace RadiantPool.Game
{
    /// <summary>Server-side explore movement for hired companions: trail the nearest real
    /// player at a small offset. Clients see the motion through the NetworkTransform.
    /// Combat freezes it like everyone else; the grid takes over.</summary>
    public class CompanionFollower : NetworkBehaviour
    {
        private const float FollowDistance = 2.6f;
        private const float Speed = 5.8f;
        private const float CatchUpSpeed = 9.5f;

        private CharacterController _controller;
        private Transform _leader;
        private float _nextLeaderScan;
        private int _slot;   // fan companions out so they don't stack
        private static int _slotCounter;

        public override void OnStartServer()
        {
            base.OnStartServer();
            _controller = GetComponent<CharacterController>();
            _slot = _slotCounter++ % 3;
        }

        private void Update()
        {
            if (!IsServerStarted) return;
            var holder = GetComponent<PlayerCharacterHolder>();
            if (holder == null || !holder.IsCompanion) return;
            if (CombatManager.Instance != null && CombatManager.Instance.InCombat.Value) return;

            if (_leader == null || Time.time >= _nextLeaderScan)
            {
                _nextLeaderScan = Time.time + 0.5f;
                _leader = FindObjectsByType<PlayerCharacterHolder>(FindObjectsSortMode.None)
                    .Where(p => !p.IsCompanion && p.Sheet != null)
                    .OrderBy(p => Vector3.Distance(p.transform.position, transform.position))
                    .Select(p => p.transform)
                    .FirstOrDefault();
            }
            if (_leader == null) return;

            // Trail position: behind the leader, fanned by slot.
            float angle = (_slot - 1) * 45f;
            Vector3 offset = Quaternion.Euler(0f, angle, 0f)
                             * -_leader.forward * FollowDistance;
            Vector3 target = _leader.position + offset;
            Vector3 delta = target - transform.position;
            delta.y = 0f;
            if (delta.magnitude < 0.6f) return;

            float topSpeed = delta.magnitude > 6f ? CatchUpSpeed : Speed;
            Vector3 step = delta.normalized * Mathf.Min(topSpeed, delta.magnitude * 2f);
            if (_controller != null && _controller.enabled)
                _controller.Move((step + Physics.gravity * 0.2f) * Time.deltaTime);
            else
                transform.position += step * Time.deltaTime;
            if (step.sqrMagnitude > 0.01f)
                transform.rotation = Quaternion.RotateTowards(transform.rotation,
                    Quaternion.LookRotation(step, Vector3.up), 540f * Time.deltaTime);
        }
    }
}
