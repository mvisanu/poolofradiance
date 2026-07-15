using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace RadiantPool.Game
{
    /// <summary>Client-side combat juice: floating damage numbers, attack lunges, hit
    /// flashes, spell bolts, and the active-turn ring. Pure presentation — driven by
    /// replicated combat events, never consulted by rules.</summary>
    public class CombatFx : MonoBehaviour
    {
        public static CombatFx Instance { get; private set; }

        private GameObject _turnRing;
        private Texture2D _combatVignette;
        private float _impactFlash;
        private string _combatCapturePath = "";

        public int AttackFeedbackEvents { get; private set; }
        public bool CombatPresentationReady => _combatVignette != null;

        private void Awake()
        {
            Instance = this;
            _combatVignette = BuildVignette();
            var args = System.Environment.GetCommandLineArgs();
            int capture = System.Array.IndexOf(args, "-combatcapture");
            if (capture >= 0 && capture + 1 < args.Length)
                _combatCapturePath = args[capture + 1];
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
            if (_combatVignette != null) Destroy(_combatVignette);
        }

        private void Update()
        {
            _impactFlash = Mathf.MoveTowards(_impactFlash, 0f, Time.unscaledDeltaTime * 2.8f);
        }

        private void OnGUI()
        {
            if (Ui.PanelOpen) return;
            bool combat = CombatManager.Instance != null && CombatManager.Instance.InCombat.Value;
            if (!combat && _impactFlash <= 0f) return;

            var old = GUI.color;
            if (combat && _combatVignette != null)
            {
                GUI.color = new Color(0.30f, 0.08f, 0.08f,
                    0.62f + Mathf.Sin(Time.unscaledTime * 1.7f) * 0.04f);
                GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height),
                    _combatVignette, ScaleMode.StretchToFill);
            }
            if (_impactFlash > 0f)
            {
                GUI.color = new Color(0.72f, 0.06f, 0.025f, _impactFlash * 0.16f);
                GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), Texture2D.whiteTexture);
            }
            GUI.color = old;
        }

        private static Texture2D BuildVignette()
        {
            const int size = 96;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = "Combat Vignette",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float nx = Mathf.Abs((x + 0.5f) / size * 2f - 1f);
                    float ny = Mathf.Abs((y + 0.5f) / size * 2f - 1f);
                    float edge = Mathf.Clamp01((Mathf.Max(nx, ny) - 0.38f) / 0.62f);
                    edge = edge * edge * (3f - 2f * edge);
                    tex.SetPixel(x, y, new Color(0f, 0f, 0f, edge * 0.72f));
                }
            tex.Apply(false, true);
            return tex;
        }

        public void CombatStarted(Vector3 origin)
        {
            _impactFlash = Mathf.Max(_impactFlash, 0.55f);
            AddCameraTrauma(0.42f);
            CastFlare(origin, new Color(0.38f, 0.045f, 0.025f));
        }

        public void CombatEnded(bool victory)
        {
            _impactFlash = Mathf.Max(_impactFlash, victory ? 0.18f : 0.75f);
            AddCameraTrauma(victory ? 0.12f : 0.55f);
        }

        /// <summary>One presentation entry point per replicated attack, hit or miss.
        /// Keeps the unattended attack test on the same event path as live play.</summary>
        public void AttackFeedback(Vector3 at, bool hit, bool critical)
        {
            AttackFeedbackEvents++;
            if (_combatCapturePath.Length > 0)
            {
                StartCoroutine(CaptureCombatFrame(_combatCapturePath));
                _combatCapturePath = "";
            }
            if (!hit)
            {
                AddCameraTrauma(0.07f);
                return;
            }
            AddCameraTrauma(critical ? 0.62f : 0.28f);
            _impactFlash = Mathf.Max(_impactFlash, critical ? 0.82f : 0.28f);
            StartCoroutine(ImpactSparks(at + Vector3.up * 1.15f, critical ? 12 : 7));
        }

        private readonly Dictionary<string, GameObject> _configuredVfx =
            new Dictionary<string, GameObject>();

        /// <summary>Optional drop-in adapter for purchased or custom ability VFX. A prefab
        /// at Resources/CombatVfx/&lt;ability visualEffectId&gt; replaces no rules code; missing
        /// art is a silent generated-FX fallback.</summary>
        public void ConfiguredEffect(string visualEffectId, Vector3 at)
        {
            if (string.IsNullOrEmpty(visualEffectId)) return;
            if (!_configuredVfx.TryGetValue(visualEffectId, out var prefab))
            {
                prefab = Resources.Load<GameObject>($"CombatVfx/{visualEffectId}");
                _configuredVfx[visualEffectId] = prefab;
            }
            if (prefab == null) return;
            var instance = Instantiate(prefab, at, Quaternion.identity);
            Destroy(instance, 4f);
        }

        private static IEnumerator CaptureCombatFrame(string path)
        {
            // The replicated event arrives at wind-up; capture when the lunge/impact lands.
            yield return new WaitForSeconds(0.14f);
            string directory = System.IO.Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory)) System.IO.Directory.CreateDirectory(directory);
            ScreenCapture.CaptureScreenshot(path);
            Debug.Log($"[CombatCapture] wrote presentation frame to {path}");
        }

        private static void AddCameraTrauma(float amount)
        {
            var camera = Camera.main;
            if (camera == null) return;
            var orbit = camera.GetComponent<OrbitCamera>();
            if (orbit != null) orbit.AddTrauma(amount);
        }

        private IEnumerator ImpactSparks(Vector3 origin, int count)
        {
            var parts = new GameObject[count];
            var velocities = new Vector3[count];
            for (int i = 0; i < count; i++)
            {
                var p = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                p.name = "Impact Spark";
                Destroy(p.GetComponent<Collider>());
                p.transform.position = origin;
                p.transform.localScale = Vector3.one * Random.Range(0.035f, 0.085f);
                RuntimeArt.Paint(p, Color.Lerp(new Color(0.75f, 0.08f, 0.025f),
                    new Color(1f, 0.58f, 0.18f), Random.value), emission: 1.8f);
                parts[i] = p;
                velocities[i] = new Vector3(Random.Range(-3.2f, 3.2f),
                    Random.Range(-0.2f, 2.8f), Random.Range(-3.2f, 3.2f));
            }
            float t = 0f;
            const float duration = 0.32f;
            while (t < duration)
            {
                t += Time.deltaTime;
                for (int i = 0; i < count; i++)
                {
                    if (parts[i] == null) continue;
                    velocities[i] += Physics.gravity * (Time.deltaTime * 0.25f);
                    parts[i].transform.position += velocities[i] * Time.deltaTime;
                    parts[i].transform.localScale *= 0.90f;
                }
                yield return null;
            }
            foreach (var p in parts) if (p != null) Destroy(p);
        }

        // ---------- damage numbers ----------

        private readonly Queue<(GameObject Root, TextMesh Text)> _popupPool =
            new Queue<(GameObject Root, TextMesh Text)>();

        public void Popup(Vector3 worldPos, string text, Color color, float size = 1f)
        {
            GameObject go;
            TextMesh tm;
            if (_popupPool.Count > 0)
            {
                var pooled = _popupPool.Dequeue();
                go = pooled.Root;
                tm = pooled.Text;
                go.SetActive(true);
            }
            else
            {
                go = new GameObject("DmgPopup");
                go.transform.SetParent(transform, true);
                tm = go.AddComponent<TextMesh>();
                go.AddComponent<Billboard>();
            }
            go.transform.position = worldPos + Vector3.up * 2.2f
                + new Vector3(Random.Range(-0.3f, 0.3f), 0f, Random.Range(-0.3f, 0.3f));
            tm.text = text;
            tm.characterSize = 0.12f * size;
            tm.fontSize = 46;
            tm.fontStyle = FontStyle.Bold;
            tm.anchor = TextAnchor.MiddleCenter;
            tm.color = color;
            StartCoroutine(FloatAndFade(go, tm));
        }

        private IEnumerator FloatAndFade(GameObject go, TextMesh tm)
        {
            float t = 0f;
            const float dur = 0.9f;
            Vector3 start = go.transform.position;
            Color c = tm.color;
            while (t < dur)
            {
                t += Time.deltaTime;
                go.transform.position = start + Vector3.up * (t * 1.6f);
                tm.color = new Color(c.r, c.g, c.b, 1f - t / dur * t / dur);
                yield return null;
            }
            if (go != null)
            {
                go.SetActive(false);
                _popupPool.Enqueue((go, tm));
            }
        }

        // ---------- grid glide ----------

        /// <summary>World speed for combat grid movement. The server derives its turn
        /// pacing waits from this — keep CombatManager.GlideSeconds in sync.</summary>
        public const float GlideSpeed = 6f;

        private readonly Dictionary<Transform, Coroutine> _glides =
            new Dictionary<Transform, Coroutine>();

        /// <summary>Walks a unit's visual to a destination at GlideSpeed instead of
        /// teleporting it. MotionAnimator picks up the displacement and plays the walk
        /// cycle. A new glide on the same unit cancels the previous one.</summary>
        public void Glide(Transform unit, Vector3 dest)
        {
            if (unit == null) return;
            if (_glides.TryGetValue(unit, out var running) && running != null)
            {
                StopCoroutine(running);
                // The canceled routine never reached its restore step — un-park the
                // controller so the new glide sees (and restores) the true state.
                var cc = unit.GetComponent<CharacterController>();
                if (cc != null) cc.enabled = true;
            }
            _glides[unit] = StartCoroutine(GlideRoutine(unit, dest));
        }

        private IEnumerator GlideRoutine(Transform unit, Vector3 dest)
        {
            // A CharacterController (the local player's body) fights direct transform
            // writes — park it for the duration of the walk.
            var cc = unit.GetComponent<CharacterController>();
            bool hadCc = cc != null && cc.enabled;
            if (hadCc) cc.enabled = false;
            Face(unit, dest);
            while (unit != null && (unit.position - dest).sqrMagnitude > 0.0004f)
            {
                unit.position = Vector3.MoveTowards(
                    unit.position, dest, GlideSpeed * Time.deltaTime);
                yield return null;
            }
            if (unit != null)
            {
                unit.position = dest;
                if (hadCc && cc != null) cc.enabled = true;
            }
            _glides.Remove(unit);
        }

        /// <summary>Runs an effect after a delay — used to land impact feedback at the
        /// moment a lunge reaches its target rather than at wind-up.</summary>
        public void After(float delay, System.Action action)
        {
            StartCoroutine(AfterRoutine(delay, action));
        }

        private IEnumerator AfterRoutine(float delay, System.Action action)
        {
            yield return new WaitForSeconds(delay);
            action();
        }

        // ---------- facing ----------

        /// <summary>Turns a unit to face a world position (Y-axis only, instant).</summary>
        public static void Face(Transform unit, Vector3 at)
        {
            if (unit == null) return;
            var dir = at - unit.position;
            dir.y = 0f;
            if (dir.sqrMagnitude > 0.01f)
                unit.rotation = Quaternion.LookRotation(dir, Vector3.up);
        }

        // ---------- melee lunge + hit flash ----------

        public void Lunge(Transform attacker, Vector3 targetPos)
        {
            if (attacker != null) StartCoroutine(LungeRoutine(attacker, targetPos));
        }

        private IEnumerator LungeRoutine(Transform attacker, Vector3 targetPos)
        {
            Vector3 origin = attacker.position;
            Vector3 toward = Vector3.MoveTowards(origin, targetPos, 0.7f);
            float t = 0f;
            while (t < 1f && attacker != null)
            {
                t += Time.deltaTime * 8f;
                attacker.position = Vector3.Lerp(origin,
                    t < 0.5f ? toward : origin, t < 0.5f ? t * 2f : (t - 0.5f) * 2f);
                yield return null;
            }
            if (attacker != null) attacker.position = origin;
        }

        public void Flash(Transform target, Color color)
        {
            if (target != null) StartCoroutine(FlashRoutine(target, color));
        }

        private IEnumerator FlashRoutine(Transform target, Color color)
        {
            var renderers = target.GetComponentsInChildren<Renderer>();
            var originals = new Color[renderers.Length];
            for (int i = 0; i < renderers.Length; i++)
            {
                if (renderers[i] is MeshRenderer)
                {
                    originals[i] = renderers[i].material.color;
                    renderers[i].material.color = color;
                }
            }
            yield return new WaitForSeconds(0.12f);
            for (int i = 0; i < renderers.Length; i++)
                if (target != null && renderers[i] != null && renderers[i] is MeshRenderer)
                    renderers[i].material.color = originals[i];
        }

        // ---------- blood + cast flare ----------

        /// <summary>Spray of red droplets with gravity — reads as a wound at this art
        /// scale. Crits spray harder.</summary>
        public void Blood(Vector3 at, bool heavy = false)
        {
            StartCoroutine(BloodRoutine(at + Vector3.up * 1.2f, heavy ? 14 : 8));
        }

        private IEnumerator BloodRoutine(Vector3 origin, int drops)
        {
            var parts = new GameObject[drops];
            var velocities = new Vector3[drops];
            var dark = new Color(0.55f, 0.05f, 0.05f);
            for (int i = 0; i < drops; i++)
            {
                var p = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                Destroy(p.GetComponent<Collider>());
                p.transform.position = origin;
                p.transform.localScale = Vector3.one * Random.Range(0.08f, 0.18f);
                RuntimeArt.Paint(p,
                    Color.Lerp(dark, new Color(0.8f, 0.1f, 0.08f), Random.value));
                parts[i] = p;
                velocities[i] = new Vector3(Random.Range(-1.6f, 1.6f),
                    Random.Range(1.5f, 3.4f), Random.Range(-1.6f, 1.6f));
            }
            float t = 0f;
            const float dur = 0.7f;
            while (t < dur)
            {
                t += Time.deltaTime;
                for (int i = 0; i < drops; i++)
                {
                    if (parts[i] == null) continue;
                    velocities[i] += Physics.gravity * Time.deltaTime;
                    parts[i].transform.position += velocities[i] * Time.deltaTime;
                    if (parts[i].transform.position.y < 0.03f)
                        parts[i].transform.position = new Vector3(
                            parts[i].transform.position.x, 0.03f, parts[i].transform.position.z);
                }
                yield return null;
            }
            foreach (var p in parts) if (p != null) Destroy(p);
        }

        /// <summary>Quick expanding ring at a caster's feet when a spell goes off.</summary>
        public void CastFlare(Vector3 at, Color color)
        {
            StartCoroutine(FlareRoutine(at, color));
        }

        private IEnumerator FlareRoutine(Vector3 at, Color color)
        {
            var ring = GameObject.CreatePrimitive(PrimitiveType.Quad);
            Destroy(ring.GetComponent<Collider>());
            ring.transform.position = at + Vector3.up * 0.06f;
            ring.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            // Transparent so it can fade — and NOT the primitive's default material, which
            // is built-in Standard and renders magenta under URP in a build.
            var mat = RuntimeArt.Paint(ring, color, emission: 1.8f, glow: true);
            float t = 0f;
            const float dur = 0.35f;
            while (t < dur)
            {
                t += Time.deltaTime;
                float k = t / dur;
                ring.transform.localScale = Vector3.one * (0.5f + k * 2.6f);
                RuntimeArt.Tint(mat, new Color(color.r, color.g, color.b, 1f - k));
                if (mat != null) mat.SetColor("_EmissionColor", color * (1.8f * (1f - k)));
                yield return null;
            }
            Destroy(ring);
        }

        // ---------- spell bolt + burst ----------

        public void Bolt(Vector3 from, Vector3 to, Color color, bool burst = true)
        {
            StartCoroutine(BoltRoutine(from + Vector3.up * 1.4f, to + Vector3.up * 1.2f,
                color, burst));
        }

        private IEnumerator BoltRoutine(Vector3 from, Vector3 to, Color color, bool burst)
        {
            var bolt = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Destroy(bolt.GetComponent<Collider>());
            bolt.transform.localScale = Vector3.one * 0.35f;
            RuntimeArt.Paint(bolt, color, emission: 2.2f);

            float t = 0f;
            const float dur = 0.18f;
            while (t < dur)
            {
                t += Time.deltaTime;
                bolt.transform.position = Vector3.Lerp(from, to, t / dur);
                yield return null;
            }
            Destroy(bolt);
            if (burst) yield return BurstRoutine(to, color);
        }

        private IEnumerator BurstRoutine(Vector3 at, Color color)
        {
            var burst = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Destroy(burst.GetComponent<Collider>());
            burst.transform.position = at;
            var mat = RuntimeArt.Paint(burst, color, emission: 2f, glow: true);
            float t = 0f;
            const float dur = 0.25f;
            while (t < dur)
            {
                t += Time.deltaTime;
                float k = t / dur;
                burst.transform.localScale = Vector3.one * (0.3f + k * 1.6f);
                RuntimeArt.Tint(mat, new Color(color.r, color.g, color.b, 1f - k));
                if (mat != null) mat.SetColor("_EmissionColor", color * (2f * (1f - k)));
                yield return null;
            }
            Destroy(burst);
        }

        // ---------- attack-target marker ----------

        private GameObject _targetMarker;
        private Coroutine _targetRoutine;

        /// <summary>Small red triangle bobbing over the unit the local player is
        /// attacking. Re-showing moves it; it hides itself after the duration.</summary>
        public void ShowTargetMarker(Transform unit, float duration = 2.5f)
        {
            if (unit == null) return;
            if (_targetMarker == null)
            {
                _targetMarker = new GameObject("TargetMarker");
                var mesh = new Mesh
                {
                    vertices = new[]
                    {
                        new Vector3(-0.28f, 0.45f, 0f),
                        new Vector3(0.28f, 0.45f, 0f),
                        new Vector3(0f, 0f, 0f)
                    },
                    triangles = new[] { 0, 1, 2, 2, 1, 0 }   // double-sided
                };
                mesh.RecalculateNormals();
                _targetMarker.AddComponent<MeshFilter>().mesh = mesh;
                var mr = _targetMarker.AddComponent<MeshRenderer>();
                var red = new Color(1f, 0.22f, 0.16f);
                mr.sharedMaterial = RuntimeArt.Lit(red, emission: 1.4f);
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                _targetMarker.AddComponent<Billboard>();
            }
            if (_targetRoutine != null) StopCoroutine(_targetRoutine);
            _targetRoutine = StartCoroutine(TargetMarkerRoutine(unit, duration));
        }

        private IEnumerator TargetMarkerRoutine(Transform unit, float duration)
        {
            // Sit just above the unit's head, whatever its model height.
            float top = 2.3f;
            var rs = unit.GetComponentsInChildren<Renderer>();
            if (rs.Length > 0)
            {
                var b = rs[0].bounds;
                for (int i = 1; i < rs.Length; i++) b.Encapsulate(rs[i].bounds);
                top = Mathf.Max(1.2f, b.max.y - unit.position.y + 0.15f);
            }
            _targetMarker.SetActive(true);
            float until = Time.time + duration;
            while (Time.time < until && unit != null)
            {
                float bob = Mathf.Sin(Time.time * 6f) * 0.1f;
                _targetMarker.transform.position = unit.position + Vector3.up * (top + bob);
                yield return null;
            }
            if (_targetMarker != null) _targetMarker.SetActive(false);
        }

        // ---------- active-turn ring ----------

        public void SetTurnMarker(Transform unit, bool isPc)
        {
            if (_turnRing == null)
            {
                _turnRing = GameObject.CreatePrimitive(PrimitiveType.Quad);
                Destroy(_turnRing.GetComponent<Collider>());
                _turnRing.name = "TurnRing";
                _turnRing.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            }
            if (unit == null) { _turnRing.SetActive(false); return; }
            _turnRing.SetActive(true);
            var color = isPc ? new Color(0.4f, 0.75f, 1f, 0.75f)
                             : new Color(1f, 0.45f, 0.35f, 0.75f);
            RuntimeArt.Paint(_turnRing, color, emission: 1.2f, glow: true);
            StartCoroutine(FollowRoutine(unit));
        }

        public void ClearTurnMarker()
        {
            if (_turnRing != null) _turnRing.SetActive(false);
        }

        private IEnumerator FollowRoutine(Transform unit)
        {
            while (_turnRing != null && _turnRing.activeSelf && unit != null)
            {
                float pulse = 1.3f + 0.15f * Mathf.Sin(Time.time * 5f);
                _turnRing.transform.position = new Vector3(
                    unit.position.x, 0.06f, unit.position.z);
                _turnRing.transform.localScale = Vector3.one * pulse;
                yield return null;
            }
        }
    }
}
