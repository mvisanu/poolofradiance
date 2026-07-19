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
        [SerializeField] private float sprintSpeed = 8.5f;
        [SerializeField] private float acceleration = 26f;
        [SerializeField] private float deceleration = 34f;
        [SerializeField, Range(0f, 1f)] private float airControl = 0.35f;
        [SerializeField] private float rotationSpeedDeg = 720f;
        [SerializeField] private float jumpHeight = 1.2f;
        [SerializeField] private float gravity = -20f;
        [SerializeField] private float coyoteTime = 0.12f;
        [SerializeField] private float jumpBuffer = 0.12f;

        private CharacterController _controller;
        private Vector3 _planarVelocity;
        private float _verticalVelocity;
        private float _lastGroundedAt = float.NegativeInfinity;
        private float _jumpPressedAt = float.NegativeInfinity;
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

            if (transform.position.y < -25f)
            {
                _controller.enabled = false;
                transform.position = new Vector3(-9f, 0.5f, -14f);
                _controller.enabled = true;
                _planarVelocity = Vector3.zero;
                _verticalVelocity = 0f;
                _lastGroundedAt = Time.time;
            }

            // Combat freezes free movement for everyone; the grid takes over
            // (server-driven mode flag — ARCHITECTURE §4).
            if (CombatManager.Instance != null && CombatManager.Instance.InCombat.Value) return;

            float h = Input.GetAxisRaw("Horizontal");
            float v = Input.GetAxisRaw("Vertical");
            if (Input.GetButtonDown("Jump")) _jumpPressedAt = Time.time;

            // Camera-relative planar movement (WoW-style).
            float camYaw = _camera != null ? _camera.Yaw : 0f;
            Vector3 forward = Quaternion.Euler(0f, camYaw, 0f) * Vector3.forward;
            Vector3 right = Quaternion.Euler(0f, camYaw, 0f) * Vector3.right;
            Vector3 move = (forward * v + right * h);
            if (move.sqrMagnitude > 1f) move.Normalize();

            bool grounded = _controller.isGrounded;
            if (grounded)
            {
                _lastGroundedAt = Time.time;
                _verticalVelocity = -1f;
            }
            else
            {
                _verticalVelocity += gravity * Time.deltaTime;
            }

            if (Time.time - _jumpPressedAt <= jumpBuffer
                && Time.time - _lastGroundedAt <= coyoteTime)
            {
                _verticalVelocity = Mathf.Sqrt(jumpHeight * -2f * gravity);
                _jumpPressedAt = float.NegativeInfinity;
                _lastGroundedAt = float.NegativeInfinity;
            }

            bool sprinting = Input.GetKey(KeyCode.LeftShift) && v > 0.1f;
            Vector3 targetVelocity = move * (sprinting ? sprintSpeed : moveSpeed);
            float rate = move.sqrMagnitude > 0.001f ? acceleration : deceleration;
            if (!grounded) rate *= airControl;
            _planarVelocity = Vector3.MoveTowards(
                _planarVelocity, targetVelocity, rate * Time.deltaTime);

            Vector3 velocity = _planarVelocity + Vector3.up * _verticalVelocity;
            _controller.Move(velocity * Time.deltaTime);

            // Face movement direction.
            if (_planarVelocity.sqrMagnitude > 0.05f)
            {
                Quaternion look = Quaternion.LookRotation(_planarVelocity, Vector3.up);
                transform.rotation = Quaternion.RotateTowards(
                    transform.rotation, look, rotationSpeedDeg * Time.deltaTime);
            }
        }
    }
}
