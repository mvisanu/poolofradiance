using System.Collections.Generic;
using UnityEngine;

namespace RadiantPool.Game
{
    /// <summary>WoW-style orbit camera: follows a target, right-mouse drag rotates,
    /// scroll wheel zooms. In combat it eases to a steeper tactical angle (so rooftops
    /// don't hide the grid), and any tall environment piece sitting between the camera
    /// and the player is temporarily hidden (props have no colliders, so this tests
    /// renderer bounds against the view line).
    ///
    /// The view can also be panned off the player — middle-mouse drag any time, and in
    /// combat WASD/arrows too (the grid owns movement then, so those keys are free), which
    /// is how you scout the far side of a fight. Wherever you leave the camera, it stays:
    /// nothing drags it back mid-fight. F recentres on demand, and it returns to the party
    /// when the fight ends. Purely local — never networked.</summary>
    public class OrbitCamera : MonoBehaviour
    {
        [SerializeField] private float distance = 8f;
        [SerializeField] private float minDistance = 2.5f;
        [SerializeField] private float maxDistance = 16f;
        [SerializeField] private float sensitivity = 3.5f;
        [SerializeField] private float pitchMin = -30f;
        [SerializeField] private float pitchMax = 75f;
        [SerializeField] private Vector3 targetOffset = new Vector3(0f, 1.6f, 0f);
        [SerializeField] private float panSpeed = 14f;      // metres/sec on the keys
        [SerializeField] private float maxPan = 40f;        // leash, so you can't get lost

        private Transform _target;
        private float _yaw;
        private float _pitch = 25f;
        private Vector3 _pan;         // world-space XZ offset of the focus point
        private bool _tacticalEase;   // one-shot board-view nudge at the start of a fight
        private bool _wasInCombat;

        public float Yaw => _yaw;

        /// <summary>True while the view is pushed off the player (HUD shows a hint).</summary>
        public bool IsPanned => _pan.sqrMagnitude > 0.01f;

        public void RecenterPan() => _pan = Vector3.zero;

        public void SetTarget(Transform target) => _target = target;

        private void LateUpdate()
        {
            if (_target == null) return;

            bool inCombat = CombatManager.Instance != null
                            && CombatManager.Instance.InCombat.Value;

            // The tactical assist is a ONE-TIME nudge when a fight starts, not a spring.
            // It used to run every frame combat was active, so the moment you let go of
            // the mouse it hauled the pitch back to 55° and the zoom back to 11 m — the
            // camera would never stay where you put it. Any camera input at all now ends
            // the assist for the rest of the fight: the view is yours.
            if (inCombat && !_wasInCombat) _tacticalEase = true;
            if (!inCombat)
            {
                _tacticalEase = false;
                if (_wasInCombat) RecenterPan();   // fight over: put the view back on you
            }
            _wasInCombat = inCombat;

            float scroll = Input.GetAxis("Mouse ScrollWheel");
            bool touchingCamera = Input.GetMouseButton(1) || Input.GetMouseButton(2)
                || Mathf.Abs(scroll) > 0.001f
                || (inCombat && (Input.GetAxisRaw("Horizontal") != 0f
                                 || Input.GetAxisRaw("Vertical") != 0f));
            if (touchingCamera) _tacticalEase = false;

            if (Input.GetMouseButton(1))
            {
                float sens = SettingsMenu.MouseSensitivity > 0f
                    ? SettingsMenu.MouseSensitivity : sensitivity;
                _yaw += Input.GetAxis("Mouse X") * sens;
                _pitch -= Input.GetAxis("Mouse Y") * sens;
                _pitch = Mathf.Clamp(_pitch, pitchMin, pitchMax);
            }
            else if (_tacticalEase)
            {
                // Ease once toward a board view so the grid is readable, then let go.
                _pitch = Mathf.MoveTowards(_pitch, Mathf.Max(_pitch, 55f),
                    40f * Time.deltaTime);
                if (distance < 11f)
                    distance = Mathf.MoveTowards(distance, 11f, 8f * Time.deltaTime);
                if (_pitch >= 54.5f && distance >= 10.9f) _tacticalEase = false;
            }

            if (Mathf.Abs(scroll) > 0.001f && !MiniMap.MouseOverMap)
                distance = Mathf.Clamp(distance - scroll * 4f, minDistance, maxDistance);

            UpdatePan(inCombat);

            Quaternion rotation = Quaternion.Euler(_pitch, _yaw, 0f);
            Vector3 focus = _target.position + targetOffset + _pan;
            transform.position = focus - rotation * Vector3.forward * distance;
            transform.rotation = rotation;

            UpdateOcclusion(focus);
        }

        /// <summary>Free-look panning across the ground plane. Middle-mouse drags the view
        /// (grab-the-world), and during combat WASD/arrows push it as well.
        ///
        /// A pan STAYS where you put it — in combat it is never taken back, because the
        /// whole point is to look at the far side of the fight while you decide. Out of
        /// combat it eases home only once you actually walk somewhere (the camera has to
        /// follow you then); standing still and looking around holds. F recentres on
        /// demand, and the offset is leashed to maxPan so you can't get lost.</summary>
        private void UpdatePan(bool inCombat)
        {
            if (Input.GetKeyDown(KeyCode.F)) { RecenterPan(); return; }

            bool walking = !inCombat && (Input.GetAxisRaw("Horizontal") != 0f
                                         || Input.GetAxisRaw("Vertical") != 0f);
            if (walking && _pan != Vector3.zero && !Input.GetMouseButton(2))
                _pan = Vector3.MoveTowards(_pan, Vector3.zero, 24f * Time.deltaTime);

            // Ground-plane basis from the current yaw: pan is always screen-relative.
            Vector3 right = Quaternion.Euler(0f, _yaw, 0f) * Vector3.right;
            Vector3 forward = Quaternion.Euler(0f, _yaw, 0f) * Vector3.forward;
            Vector3 delta = Vector3.zero;

            if (Input.GetMouseButton(2))   // middle-drag: world follows the cursor
            {
                float scale = distance * 0.08f;
                delta -= right * (Input.GetAxis("Mouse X") * scale);
                delta -= forward * (Input.GetAxis("Mouse Y") * scale);
            }

            // In combat the grid owns movement, so the movement keys are free to pan.
            if (inCombat && !Input.GetMouseButton(1) && !AnyPanelWantsKeys())
            {
                float h = Input.GetAxisRaw("Horizontal");
                float v = Input.GetAxisRaw("Vertical");
                if (h != 0f || v != 0f)
                    delta += (right * h + forward * v) * (panSpeed * Time.deltaTime);
            }

            if (delta == Vector3.zero) return;
            _pan = Vector3.ClampMagnitude(_pan + delta, maxPan);
        }

        /// <summary>Don't steal WASD while the player is typing into an IMGUI field.</summary>
        private static bool AnyPanelWantsKeys() =>
            GUIUtility.keyboardControl != 0;

        // ---------- occlusion hiding ----------

        private readonly List<Renderer> _envRenderers = new List<Renderer>();
        private readonly List<Renderer> _hidden = new List<Renderer>();
        private float _envScanAt;

        /// <summary>Static environment renderers tall enough to block the view (houses,
        /// walls, pillars). Ground/roads (flat bounds) and anything belonging to a
        /// networked unit or combat visual are excluded.</summary>
        private void RefreshEnvCache()
        {
            _envRenderers.Clear();
            foreach (var r in FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
            {
                if (r.bounds.size.y < 1.5f) continue;   // flat things never block
                if (r.GetComponentInParent<PlayerCharacterHolder>() != null) continue;
                if (r.GetComponentInParent<FishNet.Object.NetworkObject>() != null) continue;
                var root = r.transform.root.name;
                if (root.StartsWith("Monster_") || root == "GridOverlay"
                    || root == "TurnRing" || root == "MoveHoverMarker"
                    || root == "MoveRange" || root == "QuestBeacon"
                    || root == "DmgPopup") continue;
                _envRenderers.Add(r);
            }
        }

        private void UpdateOcclusion(Vector3 focus)
        {
            foreach (var r in _hidden) if (r != null) r.enabled = true;
            _hidden.Clear();

            if (Time.time >= _envScanAt)
            {
                _envScanAt = Time.time + 5f;
                RefreshEnvCache();
            }

            Vector3 camPos = transform.position;
            Vector3 toFocus = focus - camPos;
            float len = toFocus.magnitude;
            if (len < 0.5f) return;
            var ray = new Ray(camPos, toFocus / len);

            foreach (var r in _envRenderers)
            {
                if (r == null || !r.enabled) continue;
                var b = r.bounds;
                bool blocks = b.Contains(focus)   // standing inside the building
                    || (b.IntersectRay(ray, out float d) && d < len - 0.4f);
                if (blocks)
                {
                    r.enabled = false;
                    _hidden.Add(r);
                }
            }
        }
    }
}
