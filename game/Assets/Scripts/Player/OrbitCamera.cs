using System.Collections.Generic;
using UnityEngine;

namespace RadiantPool.Game
{
    /// <summary>WoW-style orbit camera: follows a target, right-mouse drag rotates,
    /// scroll wheel zooms. In combat it eases to a steeper tactical angle (so rooftops
    /// don't hide the grid), and any environment piece that hides a combatant fades to a
    /// see-through ghost (props have no colliders, so this tests renderer bounds against
    /// the view lines). Out of combat only your own line of sight is cleared; in combat
    /// every unit on the board gets one, so a monster standing inside a warehouse is
    /// still something you can see and click.
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
        [SerializeField] private float followSharpness = 16f;
        [SerializeField] private float collisionRadius = 0.28f;
        [SerializeField] private float collisionPadding = 0.18f;

        private Transform _target;
        private float _yaw;
        private float _pitch = 25f;
        private Vector3 _pan;         // world-space XZ offset of the focus point
        private bool _tacticalEase;   // one-shot board-view nudge at the start of a fight
        private bool _wasInCombat;
        private Vector3 _smoothedFocus;
        private bool _focusReady;
        private readonly RaycastHit[] _cameraHits = new RaycastHit[16];

        public float Yaw => _yaw;

        /// <summary>True while the view is pushed off the player (HUD shows a hint).</summary>
        public bool IsPanned => _pan.sqrMagnitude > 0.01f;

        public void RecenterPan() => _pan = Vector3.zero;

        public void SetTarget(Transform target)
        {
            _target = target;
            _focusReady = false;
        }

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
            Vector3 rawFocus = _target.position + targetOffset + _pan;
            if (!_focusReady)
            {
                _smoothedFocus = rawFocus;
                _focusReady = true;
            }
            else
            {
                float follow = 1f - Mathf.Exp(-followSharpness * Time.unscaledDeltaTime);
                _smoothedFocus = Vector3.Lerp(_smoothedFocus, rawFocus, follow);
            }

            Vector3 focus = _smoothedFocus;
            Vector3 backwards = -(rotation * Vector3.forward);
            float cameraDistance = CollisionDistance(focus, backwards, distance);
            transform.position = focus + backwards * cameraDistance;
            transform.rotation = rotation;

            UpdateOcclusion(focus);
        }

        private float CollisionDistance(Vector3 focus, Vector3 direction, float wanted)
        {
            int count = Physics.SphereCastNonAlloc(focus, collisionRadius, direction,
                _cameraHits, wanted, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            float nearest = wanted;
            for (int i = 0; i < count; i++)
            {
                var hit = _cameraHits[i];
                if (hit.collider == null) continue;
                var t = hit.collider.transform;
                if (_target != null && (t == _target || t.IsChildOf(_target))) continue;
                nearest = Mathf.Min(nearest, hit.distance - collisionPadding);
            }
            return Mathf.Clamp(nearest, minDistance * 0.35f, wanted);
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

        // ---------- x-ray: see through whatever is hiding a combatant ----------

        /// <summary>A blocking renderer's two faces: its real materials, and transparent
        /// clones we fade in its place. Cloned per renderer (not per source material) so
        /// two walls sharing M_Wall can be at different alphas.</summary>
        private class Ghost
        {
            public Material[] Solid;
            public Material[] Fade;
            public UnityEngine.Rendering.ShadowCastingMode Shadows;
            public float Alpha = 1f;      // 1 = solid, GhostAlpha = fully ghosted
            public bool FadeApplied;
        }

        private const float GhostAlpha = 0.18f;   // enough silhouette to keep the shape
        private const float FadeSpeed = 5f;       // alpha per second

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");

        private readonly List<Renderer> _envRenderers = new List<Renderer>();
        private readonly Dictionary<Renderer, Ghost> _ghosts = new Dictionary<Renderer, Ghost>();
        private readonly HashSet<Renderer> _blocking = new HashSet<Renderer>();
        private readonly List<Vector3> _sightPoints = new List<Vector3>();
        private readonly List<Renderer> _sweep = new List<Renderer>();
        private float _envScanAt;
        private float _blockScanAt;

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

        /// <summary>Everything that must stay visible: the focus point out of combat, plus
        /// every living unit on the board once a fight starts.</summary>
        private void CollectSightPoints(Vector3 focus)
        {
            _sightPoints.Clear();
            _sightPoints.Add(focus);

            var combat = CombatManager.Instance;
            if (combat == null || !combat.InCombat.Value) return;

            foreach (var u in combat.ClientUnits)
            {
                if (u == null || u.Visual == null || u.Dead) continue;
                // Chest height: the head can clear a low wall the body is buried in.
                _sightPoints.Add(u.Visual.position + Vector3.up * 1.0f);
            }
        }

        private void UpdateOcclusion(Vector3 focus)
        {
            if (Time.time >= _envScanAt)
            {
                _envScanAt = Time.time + 5f;
                RefreshEnvCache();
            }

            // The blocking set only changes when things move — a 20 Hz sweep is plenty,
            // and the alpha lerp below still runs every frame so the fade stays smooth.
            if (Time.time >= _blockScanAt)
            {
                _blockScanAt = Time.time + 0.05f;
                RecomputeBlockers(focus);
            }

            DriveFades();
        }

        private void RecomputeBlockers(Vector3 focus)
        {
            _blocking.Clear();
            CollectSightPoints(focus);
            Vector3 camPos = transform.position;

            foreach (var r in _envRenderers)
            {
                if (r == null || !r.enabled) continue;
                var b = r.bounds;
                foreach (var p in _sightPoints)
                {
                    Vector3 to = p - camPos;
                    float len = to.magnitude;
                    if (len < 0.5f) continue;
                    // Contains() is the case that actually bit us: a monster spawned
                    // *inside* a warehouse is not behind the wall, it is in the box.
                    bool blocks = b.Contains(p)
                        || (b.IntersectRay(new Ray(camPos, to / len), out float d)
                            && d < len - 0.4f);
                    if (!blocks) continue;
                    _blocking.Add(r);
                    break;
                }
            }
        }

        /// <summary>Lerp every ghost toward its target alpha; swap the real materials back
        /// in once a renderer is solid again so untouched geometry costs nothing.</summary>
        private void DriveFades()
        {
            foreach (var r in _blocking)
                Fade(r, GhostAlpha);

            _sweep.Clear();
            foreach (var kv in _ghosts)
                if (kv.Key == null || !_blocking.Contains(kv.Key)) _sweep.Add(kv.Key);
            foreach (var r in _sweep)
            {
                if (r == null)   // renderer destroyed under us — drop its clones
                {
                    if (_ghosts.TryGetValue(r, out var dead))
                        foreach (var m in dead.Fade) if (m != null) Destroy(m);
                    _ghosts.Remove(r);
                    continue;
                }
                Fade(r, 1f);
            }
        }

        private void Fade(Renderer r, float target)
        {
            var g = GhostFor(r);
            if (g == null) return;

            g.Alpha = Mathf.MoveTowards(g.Alpha, target, FadeSpeed * Time.deltaTime);

            if (g.Alpha >= 0.999f)
            {
                if (g.FadeApplied)
                {
                    r.sharedMaterials = g.Solid;
                    r.shadowCastingMode = g.Shadows;
                    g.FadeApplied = false;
                }
                return;
            }

            if (!g.FadeApplied)
            {
                r.sharedMaterials = g.Fade;
                // A ghosted roof still casting a hard shadow leaves the monster inside it
                // sitting in a dark box — drop the shadow with the walls.
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                g.FadeApplied = true;
            }
            foreach (var m in g.Fade) SetAlpha(m, g.Alpha);
        }

        private Ghost GhostFor(Renderer r)
        {
            if (_ghosts.TryGetValue(r, out var g)) return g;

            var solid = r.sharedMaterials;
            var fade = new Material[solid.Length];
            for (int i = 0; i < solid.Length; i++) fade[i] = MakeFade(solid[i]);
            g = new Ghost { Solid = solid, Fade = fade, Shadows = r.shadowCastingMode };
            _ghosts[r] = g;
            return g;
        }

        /// <summary>Transparent twin of a material. Cloned from the scene material, so the
        /// shader is already referenced by an asset — Shader.Find would return null in a
        /// build for a shader nothing else uses.</summary>
        private static Material MakeFade(Material src)
        {
            if (src == null) return null;
            var m = new Material(src);
            m.SetOverrideTag("RenderType", "Transparent");
            m.SetFloat("_Surface", 1f);          // URP Lit/Unlit: transparent
            m.SetFloat("_Blend", 0f);            // alpha blend
            m.SetFloat("_AlphaClip", 0f);
            m.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            m.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            m.SetInt("_ZWrite", 0);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.DisableKeyword("_ALPHATEST_ON");
            m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            return m;
        }

        private static void SetAlpha(Material m, float a)
        {
            if (m == null) return;
            if (m.HasProperty(BaseColorId))
            {
                var c = m.GetColor(BaseColorId);
                c.a = a;
                m.SetColor(BaseColorId, c);
            }
            if (m.HasProperty(ColorId))
            {
                var c = m.GetColor(ColorId);
                c.a = a;
                m.SetColor(ColorId, c);
            }
        }

        private void OnDisable()
        {
            foreach (var kv in _ghosts)
            {
                var r = kv.Key;
                var g = kv.Value;
                if (r != null && g.FadeApplied)
                {
                    r.sharedMaterials = g.Solid;
                    r.shadowCastingMode = g.Shadows;
                }
                foreach (var m in g.Fade) if (m != null) Destroy(m);
            }
            _ghosts.Clear();
            _blocking.Clear();
        }
    }
}
