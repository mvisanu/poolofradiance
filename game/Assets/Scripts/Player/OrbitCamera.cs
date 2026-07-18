using System.Collections.Generic;
using UnityEngine;

namespace RadiantPool.Game
{
    /// <summary>WoW-style orbit camera: follows a target, right-mouse drag rotates,
    /// scroll wheel zooms. The camera NEVER moves on its own — not at the start of
    /// combat, not for collision avoidance. The only automatic behaviors are the
    /// smoothed follow of the focus point (so the view doesn't hitch when the target
    /// steps) and putting the pan back on you at a fight's start/end (see UpdatePan).
    /// Yaw, pitch, and zoom distance change only from direct player input or an
    /// explicit F recentre.
    ///
    /// Whatever hides what you're looking at fades to a see-through ghost instead of
    /// the camera pulling in or swinging around it (props have no colliders, so this
    /// tests renderer bounds against the view lines). Out of combat only your own line
    /// of sight is cleared; in combat every unit on the board gets one too, so a
    /// monster standing inside a warehouse is still something you can see and click —
    /// and your own camera-to-you line is always included in both states.
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

        private Transform _target;
        private float _yaw;
        private float _pitch = 25f;
        private Vector3 _pan;         // world-space XZ offset of the focus point
        private bool _wasInCombat;
        private Vector3 _smoothedFocus;
        private bool _focusReady;
        private float _trauma;

        public float Yaw => _yaw;
        public float Pitch => _pitch;
        public float Distance => distance;

        /// <summary>True while the view is pushed off the player (HUD shows a hint).</summary>
        public bool IsPanned => _pan.sqrMagnitude > 0.01f;

        public void RecenterPan() => _pan = Vector3.zero;

        /// <summary>One-shot camera composition for command-line screenshot tests. The
        /// same follow/orbit path still renders the frame; this only supplies a known
        /// bearing, pitch and distance without sending desktop input.</summary>
        public void SetPresentationView(float yaw, float pitch, float wantedDistance)
        {
            _yaw = yaw;
            _pitch = Mathf.Clamp(pitch, pitchMin, pitchMax);
            distance = Mathf.Clamp(wantedDistance, minDistance, maxDistance);
            _pan = Vector3.zero;
            _focusReady = false;
        }

        /// <summary>Adds a short, bounded presentation shake. Combat rules never read it;
        /// the camera applies it after follow/orbit so it cannot accumulate drift.</summary>
        public void AddTrauma(float amount)
        {
            if (SettingsMenu.ReducedMotion) return;
            _trauma = Mathf.Clamp01(_trauma + amount);
        }

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

            // The ONLY automatic camera behavior left at a fight's start/end is putting
            // the pan back on you — a stale free-look pan would otherwise frame the
            // wrong ground the moment the fight begins (or ends). Yaw, pitch, and zoom
            // are never touched here; they change only from direct input below.
            if (inCombat != _wasInCombat) RecenterPan();
            _wasInCombat = inCombat;

            float scroll = Input.GetAxis("Mouse ScrollWheel");

            if (Input.GetMouseButton(1))
            {
                float sens = SettingsMenu.MouseSensitivity > 0f
                    ? SettingsMenu.MouseSensitivity : sensitivity;
                _yaw += Input.GetAxis("Mouse X") * sens;
                _pitch -= Input.GetAxis("Mouse Y") * sens;
                _pitch = Mathf.Clamp(_pitch, pitchMin, pitchMax);
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
            // No collision pull-in: the rendered distance is exactly what the player
            // dialed in with scroll. Whatever stands between the camera and the focus
            // fades to see-through instead (UpdateOcclusion below) — the camera itself
            // never moves to dodge geometry.
            transform.position = focus + backwards * distance;
            transform.rotation = rotation;
            ApplyTrauma(rotation);

            UpdateOcclusion(focus);
        }

        private void ApplyTrauma(Quaternion baseRotation)
        {
            if (SettingsMenu.ReducedMotion)
            {
                _trauma = 0f;
                return;
            }
            if (_trauma <= 0f) return;
            float power = _trauma * _trauma;
            float t = Time.unscaledTime * 22f;
            float x = (Mathf.PerlinNoise(t, 3.1f) - 0.5f) * 2f;
            float y = (Mathf.PerlinNoise(7.7f, t) - 0.5f) * 2f;
            float roll = (Mathf.PerlinNoise(t, 13.4f) - 0.5f) * 2f;
            transform.position += (baseRotation * Vector3.right) * (x * 0.18f * power)
                                  + (baseRotation * Vector3.up) * (y * 0.12f * power);
            transform.rotation *= Quaternion.Euler(y * 0.65f * power,
                x * 0.55f * power, roll * 0.8f * power);
            _trauma = Mathf.MoveTowards(_trauma, 0f, Time.unscaledDeltaTime * 2.6f);
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

        /// <summary>How many renderers the x-ray currently judges to be blocking a sight
        /// line (the camera's own line to the focus, plus every living unit's in combat).
        /// Read-only diagnostic for self-tests — this is the ONE mechanism that answers
        /// "building in the way" now that the camera never pulls in or swings to dodge one.</summary>
        public int BlockingCount => _blocking.Count;

        /// <summary>True if every renderer currently judged as blocking has actually had its
        /// see-through ghost swapped in (or is mid-fade to it). False only signals a real
        /// bug — DriveFades applies the swap the same frame a blocker is recomputed.</summary>
        public bool AllBlockersGhosted
        {
            get
            {
                foreach (var r in _blocking)
                {
                    if (r == null) continue;
                    if (!_ghosts.TryGetValue(r, out var g) || !g.FadeApplied) return false;
                }
                return true;
            }
        }

        /// <summary>Test-only: makes the next frame run both the environment cache
        /// refresh and the blocker recompute immediately instead of waiting out their
        /// normal 5s/0.05s intervals. AttackSelfTest plants a synthetic occluder and
        /// calls this so the x-ray half of `[CombatCameraTest]` observes a real,
        /// deterministic fade rather than whatever happened to be in view. Guarded by
        /// the same -attacktest flag as other self-test-only fixtures.</summary>
        public void ForceOcclusionRescanForTest()
        {
            if (System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-attacktest") < 0)
                return;
            _envScanAt = 0f;
            _blockScanAt = 0f;
        }

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
            // Combat retry destroys the old encounter renderers between the blocker scan
            // and this fade pass. Unity's destroyed-object fake null must be tested before
            // touching sharedMaterials; otherwise the native renderer throws here.
            if (r == null) return;

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
            if (r == null) return null;
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
