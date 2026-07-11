using FishNet.Object;
using UnityEngine;

namespace RadiantPool.Game
{
    /// <summary>Third-person explore-mode movement (WASD relative to camera yaw, jump,
    /// gravity) driven only on the owning client. Phase 2 replicates via a
    /// client-authoritative NetworkTransform; server-reconciled prediction replaces this
    /// when combat authority lands (see ARCHITECTURE.md §2).</summary>
    [RequireComponent(typeof(CharacterController))]
    public class PlayerMotor : NetworkBehaviour
    {
        [SerializeField] private float moveSpeed = 6f;
        [SerializeField] private float rotationSpeedDeg = 720f;
        [SerializeField] private float jumpHeight = 1.2f;
        [SerializeField] private float gravity = -20f;

        private CharacterController _controller;
        private float _verticalVelocity;
        private OrbitCamera _camera;

        public override void OnStartClient()
        {
            base.OnStartClient();
            _controller = GetComponent<CharacterController>();
            if (IsOwner)
            {
                _camera = FindFirstObjectByType<OrbitCamera>();
                if (_camera != null) _camera.SetTarget(transform);
            }
        }

        private void Update()
        {
            if (!IsOwner || _controller == null) return;

            // Combat freezes free movement for everyone; the grid takes over
            // (server-driven mode flag — ARCHITECTURE §4).
            if (CombatManager.Instance != null && CombatManager.Instance.InCombat.Value) return;

            float h = Input.GetAxisRaw("Horizontal");
            float v = Input.GetAxisRaw("Vertical");

            // Camera-relative planar movement (WoW-style).
            float camYaw = _camera != null ? _camera.Yaw : 0f;
            Vector3 forward = Quaternion.Euler(0f, camYaw, 0f) * Vector3.forward;
            Vector3 right = Quaternion.Euler(0f, camYaw, 0f) * Vector3.right;
            Vector3 move = (forward * v + right * h);
            if (move.sqrMagnitude > 1f) move.Normalize();

            if (_controller.isGrounded)
            {
                _verticalVelocity = -1f;
                if (Input.GetButtonDown("Jump"))
                    _verticalVelocity = Mathf.Sqrt(jumpHeight * -2f * gravity);
            }
            else
            {
                _verticalVelocity += gravity * Time.deltaTime;
            }

            Vector3 velocity = move * moveSpeed + Vector3.up * _verticalVelocity;
            _controller.Move(velocity * Time.deltaTime);

            // Face movement direction.
            if (move.sqrMagnitude > 0.001f)
            {
                Quaternion look = Quaternion.LookRotation(move, Vector3.up);
                transform.rotation = Quaternion.RotateTowards(
                    transform.rotation, look, rotationSpeedDeg * Time.deltaTime);
            }
        }
    }
}
