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

        private CharacterController _controller;
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

            var leader = FindObjectsByType<PlayerCharacterHolder>(FindObjectsSortMode.None)
                .Where(p => !p.IsCompanion && p.Sheet != null)
                .OrderBy(p => Vector3.Distance(p.transform.position, transform.position))
                .FirstOrDefault();
            if (leader == null) return;

            // Trail position: behind the leader, fanned by slot.
            float angle = (_slot - 1) * 45f;
            Vector3 offset = Quaternion.Euler(0f, angle, 0f)
                             * -leader.transform.forward * FollowDistance;
            Vector3 target = leader.transform.position + offset;
            Vector3 delta = target - transform.position;
            delta.y = 0f;
            if (delta.magnitude < 0.6f) return;

            Vector3 step = delta.normalized * Mathf.Min(Speed, delta.magnitude * 2f);
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
