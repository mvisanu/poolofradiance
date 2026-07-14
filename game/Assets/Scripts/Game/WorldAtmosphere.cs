using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace RadiantPool.Game
{
    /// <summary>Client presentation for the host computer's synchronized local clock. The
    /// server replicates a fractional hour; every client derives the same sun, moon, fog,
    /// ambient light, sky and lantern state, interpolating real seconds between updates.</summary>
    public class WorldAtmosphere : MonoBehaviour
    {
        public static WorldAtmosphere Instance { get; private set; }

        public const float RealHoursPerSecond = 1f / 3600f;
        // A carried torch should make the party readable without flattening the night.
        // Unity point lights have a hard physical range, so this is also the visibility
        // circle's authoritative radius (nine 5-foot combat cells).
        public const float PartyTorchRadius = 13.5f;

        public static float NightWeight => Instance != null ? Instance._nightWeight : 0f;
        public static string ClockLabel => Instance != null
            ? $"{Mathf.FloorToInt(Instance._visualHour):00}:" +
              $"{Mathf.FloorToInt((Instance._visualHour % 1f) * 60f):00}"
            : "--:--";
        public static string PhaseLabel => Instance != null ? PhaseFor(Instance._visualHour) : "";

        public float SunIntensity => _sun != null ? _sun.intensity : 0f;
        public float MoonIntensity => _moon != null ? _moon.intensity : 0f;
        public float FogDensity => RenderSettings.fogDensity;
        public float AverageLampIntensity => _lamps.Count == 0
            ? 0f : _lamps.Keys.Where(l => l != null).Select(l => l.intensity).DefaultIfEmpty().Average();
        public float PartyTorchIntensity => _partyTorch != null && _partyTorch.enabled
            ? _partyTorch.intensity : 0f;
        public float PartyTorchRange => _partyTorch != null ? _partyTorch.range : 0f;
        public float PartyTorchHorizontalOffset
        {
            get
            {
                if (_partyTorch == null || _partyAnchor == null) return float.PositiveInfinity;
                Vector3 delta = _partyTorch.transform.position - _partyAnchor.position;
                delta.y = 0f;
                return delta.magnitude;
            }
        }

        private Light _sun;
        private Light _moon;
        private Light _partyTorch;
        private Transform _partyAnchor;
        private Material _sky;
        private ParticleSystem _motes;
        private readonly Dictionary<Light, float> _lamps = new Dictionary<Light, float>();
        private float _visualHour = 20.5f;
        private float _nightWeight;
        private float _nextReferenceScan;
        private bool _ready;
        private float _nextPartyScan;

        private static readonly Color DaySky = new Color(0.26f, 0.30f, 0.34f);
        private static readonly Color NightSky = new Color(0.050f, 0.068f, 0.115f);
        private static readonly Color DayFog = new Color(0.31f, 0.34f, 0.35f);
        private static readonly Color NightFog = new Color(0.055f, 0.075f, 0.105f);
        private static readonly Color Amber = new Color(1f, 0.55f, 0.22f);
        private float _syncLogAt;
        private bool _syncLogged;

        private void Awake()
        {
            Instance = this;
            RefreshSceneReferences();
        }

        private void Start()
        {
            var args = System.Environment.GetCommandLineArgs();
            if (System.Array.IndexOf(args, "-atmospheretest") >= 0)
                StartCoroutine(AtmosphereSelfTest());

            int capture = System.Array.IndexOf(args, "-atmospherecapture");
            if (capture >= 0 && capture + 1 < args.Length)
                StartCoroutine(CaptureRepresentativeTimes(args[capture + 1]));
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
            if (_sky != null) Destroy(_sky);
        }

        private void Update()
        {
            if (Time.unscaledTime >= _nextReferenceScan)
            {
                _nextReferenceScan = Time.unscaledTime + 5f;
                RefreshSceneReferences();
            }

            float target = GameDirector.Instance != null
                ? GameDirector.Instance.WorldHour.Value : _visualHour;
            float hourDelta = Mathf.DeltaAngle(_visualHour * 15f, target * 15f) / 15f;
            if (!_ready || Mathf.Abs(hourDelta) > 0.75f)
                _visualHour = target;
            else
                _visualHour = Mathf.Repeat(_visualHour
                    + RealHoursPerSecond * Time.unscaledDeltaTime
                    + hourDelta * Mathf.Min(1f, Time.unscaledDeltaTime * 2f), 24f);
            _ready = true;
            ApplyAtmosphere(_visualHour);
            UpdatePartyTorch();

            if (!_syncLogged && GameDirector.Instance != null
                && GameDirector.Instance.IsClientStarted)
            {
                if (_syncLogAt <= 0f) _syncLogAt = Time.unscaledTime + 0.75f;
                if (Time.unscaledTime >= _syncLogAt)
                {
                    _syncLogged = true;
                    Debug.Log($"[Atmosphere] client clock synchronized at " +
                              $"{ClockLabel} {PhaseLabel}");
                }
            }
        }

        private void RefreshSceneReferences()
        {
            if (_sun == null)
                _sun = GameObject.Find("Directional Light")?.GetComponent<Light>();

            if (_moon == null)
            {
                var moonGo = GameObject.Find("Moon Light") ?? new GameObject("Moon Light");
                _moon = moonGo.GetComponent<Light>() ?? moonGo.AddComponent<Light>();
                _moon.type = LightType.Directional;
                _moon.shadows = LightShadows.Soft;
                _moon.color = new Color(0.36f, 0.48f, 0.72f);
            }

            if (_sky == null && RenderSettings.skybox != null)
            {
                _sky = new Material(RenderSettings.skybox) { name = "Runtime Haunted Sky" };
                RenderSettings.skybox = _sky;
            }

            if (_motes == null)
                _motes = GameObject.Find("Ambient Sun Motes")?.GetComponent<ParticleSystem>();

            foreach (var light in FindObjectsByType<Light>(FindObjectsSortMode.None))
                if (light != null && light.gameObject.name == "GlowLight"
                    && !_lamps.ContainsKey(light))
                    _lamps[light] = Mathf.Max(1.6f, light.intensity);

            if (_lamps.Count == 0) CreateFallbackLanterns();
        }

        private void CreatePartyTorch()
        {
            var root = new GameObject("Party Torch Light");
            _partyTorch = root.AddComponent<Light>();
            _partyTorch.type = LightType.Point;
            _partyTorch.color = new Color(1f, 0.50f, 0.22f);
            _partyTorch.range = PartyTorchRadius;
            _partyTorch.intensity = 0f;
            _partyTorch.shadows = LightShadows.Soft;
            _partyTorch.shadowStrength = 0.48f;
            _partyTorch.shadowBias = 0.06f;
            _partyTorch.renderMode = LightRenderMode.ForcePixel;
            _partyTorch.enabled = false;
        }

        /// <summary>The torch is client-local: each player sees a visibility circle around
        /// their own party leader. Companions already cluster around that leader, while
        /// co-op players who split up retain their own useful pool of light.</summary>
        private void UpdatePartyTorch()
        {
            if (_partyTorch == null) CreatePartyTorch();
            if (_partyAnchor == null || Time.unscaledTime >= _nextPartyScan)
            {
                _nextPartyScan = Time.unscaledTime + 0.4f;
                var holder = FindObjectsByType<PlayerCharacterHolder>(FindObjectsSortMode.None)
                    .FirstOrDefault(p => p.IsOwner);
                _partyAnchor = holder != null ? holder.transform : null;
            }

            if (_partyAnchor == null)
            {
                _partyTorch.enabled = false;
                return;
            }

            // Slightly above shoulder height spreads carried fire into a broad circular
            // pool instead of overexposing the character's head. The quick exponential
            // follow avoids lag without snapping during network correction.
            Vector3 desired = _partyAnchor.position + Vector3.up * 2.35f;
            float follow = 1f - Mathf.Exp(-14f * Time.unscaledDeltaTime);
            if (!_partyTorch.enabled)
                _partyTorch.transform.position = desired;
            else
                _partyTorch.transform.position = Vector3.Lerp(
                    _partyTorch.transform.position, desired, follow);
        }

        private void CreateFallbackLanterns()
        {
            foreach (var pos in new[]
                     {
                         new Vector3(-5f, 0f, -10f), new Vector3(5f, 0f, -10f),
                         new Vector3(-5f, 0f, 2f), new Vector3(5f, 0f, 2f)
                     })
            {
                var root = new GameObject("Night Lantern");
                root.transform.position = pos;
                var post = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                post.name = "Lantern Post";
                Destroy(post.GetComponent<Collider>());
                post.transform.SetParent(root.transform, false);
                post.transform.localPosition = Vector3.up * 0.9f;
                post.transform.localScale = new Vector3(0.06f, 0.9f, 0.06f);
                RuntimeArt.Paint(post, new Color(0.10f, 0.08f, 0.07f));

                var ember = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                ember.name = "Lantern Ember";
                Destroy(ember.GetComponent<Collider>());
                ember.transform.SetParent(root.transform, false);
                ember.transform.localPosition = Vector3.up * 1.85f;
                ember.transform.localScale = Vector3.one * 0.18f;
                RuntimeArt.Paint(ember, Amber, emission: 2.2f);

                var glowGo = new GameObject("GlowLight");
                glowGo.transform.SetParent(root.transform, false);
                glowGo.transform.localPosition = Vector3.up * 1.85f;
                var light = glowGo.AddComponent<Light>();
                light.type = LightType.Point;
                light.color = Amber;
                light.range = 10f;
                light.intensity = 2.2f;
                _lamps[light] = 2.2f;
            }
        }

        private void ApplyAtmosphere(float hour)
        {
            float solar = Mathf.Sin((hour - 6f) / 24f * Mathf.PI * 2f);
            float daylight = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-0.12f, 0.22f, solar));
            float edgeDistance = Mathf.Min(WrappedDistance(hour, 6f), WrappedDistance(hour, 18f));
            float twilight = 1f - Mathf.SmoothStep(0f, 1f,
                Mathf.InverseLerp(0f, 2.25f, edgeDistance));
            _nightWeight = 1f - daylight;
            bool combat = CombatManager.Instance != null && CombatManager.Instance.InCombat.Value;
            float combatWeight = combat ? 1f : 0f;

            if (_sun != null)
            {
                _sun.intensity = daylight * 0.95f + twilight * 0.08f;
                _sun.color = Color.Lerp(new Color(1f, 0.46f, 0.25f),
                    new Color(0.82f, 0.88f, 0.92f), daylight);
                float yaw = Mathf.Repeat((hour - 6f) * 15f - 65f, 360f);
                float pitch = Mathf.Lerp(7f, 54f, Mathf.Clamp01(solar));
                _sun.transform.rotation = Quaternion.Euler(pitch, yaw, 0f);
            }

            if (_moon != null)
            {
                _moon.intensity = _nightWeight * (combat ? 0.42f : 0.34f);
                _moon.color = combat ? new Color(0.34f, 0.38f, 0.58f)
                    : new Color(0.36f, 0.48f, 0.72f);
                _moon.transform.rotation = Quaternion.Euler(42f,
                    Mathf.Repeat((hour - 6f) * 15f + 115f, 360f), 0f);
            }

            Color sky = Color.Lerp(NightSky, DaySky, daylight);
            Color fog = Color.Lerp(NightFog, DayFog, daylight);
            Color duskFog = hour < 12f ? new Color(0.26f, 0.20f, 0.19f)
                : new Color(0.28f, 0.14f, 0.12f);
            sky = Color.Lerp(sky, duskFog * 0.8f, twilight * 0.48f);
            fog = Color.Lerp(fog, duskFog, twilight * 0.42f);
            if (combat)
            {
                sky *= 0.78f;
                fog = Color.Lerp(fog, new Color(0.09f, 0.025f, 0.025f), 0.20f);
            }

            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = sky * (combat ? 0.68f : 0.82f);
            RenderSettings.ambientEquatorColor = Color.Lerp(
                new Color(0.045f, 0.060f, 0.090f), new Color(0.25f, 0.27f, 0.25f), daylight);
            RenderSettings.ambientGroundColor = Color.Lerp(
                new Color(0.022f, 0.030f, 0.046f), new Color(0.12f, 0.13f, 0.11f), daylight);
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Exponential;
            RenderSettings.fogColor = fog;
            RenderSettings.fogDensity = Mathf.Lerp(0.029f, 0.011f, daylight)
                                        + twilight * 0.003f + combatWeight * 0.004f;

            if (_sky != null)
            {
                if (_sky.HasProperty("_SkyTint")) _sky.SetColor("_SkyTint", sky);
                if (_sky.HasProperty("_GroundColor"))
                    _sky.SetColor("_GroundColor", Color.Lerp(new Color(0.012f, 0.015f, 0.025f),
                        new Color(0.18f, 0.19f, 0.17f), daylight));
                if (_sky.HasProperty("_Exposure"))
                    _sky.SetFloat("_Exposure", Mathf.Lerp(0.34f, 0.82f, daylight)
                        - combatWeight * 0.08f);
                if (_sky.HasProperty("_AtmosphereThickness"))
                    _sky.SetFloat("_AtmosphereThickness", Mathf.Lerp(0.45f, 0.85f, daylight));
            }

            foreach (var pair in _lamps.ToArray())
            {
                var lamp = pair.Key;
                if (lamp == null) { _lamps.Remove(lamp); continue; }
                float flicker = 0.92f + Mathf.PerlinNoise(
                    Time.unscaledTime * 5.2f, lamp.GetInstanceID() * 0.013f) * 0.14f;
                lamp.range = Mathf.Max(lamp.range, 12f);
                lamp.intensity = pair.Value * Mathf.Lerp(0.035f, 1.72f, _nightWeight) * flicker;
                lamp.color = Color.Lerp(Amber, new Color(1f, 0.33f, 0.18f), combatWeight * 0.35f);
            }

            if (_partyTorch != null)
            {
                // Off in full daylight; increasingly useful through dusk; warm and bright
                // at night. Range never changes, preserving the promised visibility circle.
                float torchWeight = Mathf.SmoothStep(0f, 1f,
                    Mathf.InverseLerp(0.08f, 0.72f, _nightWeight));
                _partyTorch.enabled = _partyAnchor != null && torchWeight > 0.01f;
                _partyTorch.range = PartyTorchRadius;
                _partyTorch.intensity = Mathf.Lerp(0f, combat ? 5.50f : 5.00f, torchWeight);
                _partyTorch.color = Color.Lerp(new Color(1f, 0.64f, 0.32f),
                    new Color(1f, 0.44f, 0.18f), combatWeight * 0.55f);
            }

            if (_motes != null)
            {
                var main = _motes.main;
                main.startColor = new ParticleSystem.MinMaxGradient(
                    Color.Lerp(new Color(1f, 0.72f, 0.35f, 0.08f),
                        new Color(0.30f, 0.48f, 0.72f, 0.08f), _nightWeight),
                    Color.Lerp(new Color(1f, 0.82f, 0.48f, 0.28f),
                        new Color(0.46f, 0.68f, 0.90f, 0.32f), _nightWeight));
                var emission = _motes.emission;
                emission.rateOverTime = Mathf.Lerp(2.5f, combat ? 12f : 8f, _nightWeight);
            }
        }

        private IEnumerator AtmosphereSelfTest()
        {
            yield return new WaitForSeconds(7f);
            var director = GameDirector.Instance;
            if (director == null || !director.IsServerStarted)
            {
                Debug.Log("[AtmosphereTest] FAIL - no authoritative clock");
                yield break;
            }

            float computerAtStart = GameDirector.ComputerLocalHourNow();
            float synchronizedAtStart = director.WorldHour.Value;
            bool startedOnComputerClock = director.ComputerClockActive
                && WrappedDistance(computerAtStart, synchronizedAtStart) < 0.02f;

            var questLamps = FindObjectsByType<FlameLampPost>(FindObjectsSortMode.None)
                .Where(l => l.AreaId == FlameLampPost.QuestCenterArea).ToArray();
            var questGivers = FindObjectsByType<NpcInteract>(FindObjectsSortMode.None);
            int healthyQuestLamps = questLamps.Count(l => l.HasVisibleFlame
                && l.HasWorkingLight && _lamps.ContainsKey(l.Glow));
            bool surroundsQuestGivers = questGivers.Length > 0 && questGivers.All(g =>
            {
                var nearby = questLamps.Select(l => l.transform.position - g.transform.position)
                    .Where(delta => new Vector2(delta.x, delta.z).magnitude <= 8.5f).ToArray();
                return nearby.Any(d => d.x < -1f) && nearby.Any(d => d.x > 1f)
                       && nearby.Any(d => d.z < -1f) && nearby.Any(d => d.z > 1f);
            });
            bool questLampCoverage = questLamps.Length >= 4
                                     && healthyQuestLamps == questLamps.Length
                                     && surroundsQuestGivers;
            var hall = GameObject.Find("Council Hall");
            var hallTarget = GameObject.Find("Council Hall Facade Light Target");
            var hallLamps = questLamps.Where(l => l.IlluminatesCouncilHall).ToArray();
            int hallLightsInRange = hallTarget == null ? 0 : hallLamps.Count(l =>
                l.Glow != null && Vector3.Distance(l.Glow.transform.position,
                    hallTarget.transform.position) <= l.Glow.range);
            bool councilHallIlluminated = hall != null && hallTarget != null
                                          && hallLamps.Length >= 2
                                          && hallLightsInRange == hallLamps.Length;

            director.ServerSetWorldHourForTest(12f);
            yield return new WaitForSeconds(1f);
            float daySun = SunIntensity;
            float dayMoon = MoonIntensity;
            float dayFog = FogDensity;
            float dayLamps = AverageLampIntensity;
            float dayHallLamps = hallLamps.Where(l => l.Glow != null).Sum(l => l.Glow.intensity);
            float dayTorch = PartyTorchIntensity;

            director.ServerSetWorldHourForTest(0f);
            yield return new WaitForSeconds(1.5f);
            float nightSun = SunIntensity;
            float nightMoon = MoonIntensity;
            float nightFog = FogDensity;
            float nightLamps = AverageLampIntensity;
            float nightHallLamps = hallLamps.Where(l => l.Glow != null).Sum(l => l.Glow.intensity);
            float nightTorch = PartyTorchIntensity;
            float torchRange = PartyTorchRange;
            float torchOffset = PartyTorchHorizontalOffset;
            float nightAudio = GameAudio.Instance != null ? GameAudio.Instance.NightAmbienceLevel : 0f;
            bool nightPhase = PhaseLabel == "NIGHT";

            director.ServerClearWorldHourTestOverride();
            yield return new WaitForSeconds(0.75f);
            float computerAfterTest = GameDirector.ComputerLocalHourNow();
            float synchronizedAfterTest = director.WorldHour.Value;
            bool returnedToComputerClock = director.ComputerClockActive
                && WrappedDistance(computerAfterTest, synchronizedAfterTest) < 0.02f;
            director.ServerSaveCampaign();
            bool saveIndependent = SaveSystem.Read() != null
                && !File.ReadAllText(SaveSystem.SavePath).Contains("\"GameHour\"");

            bool pass = startedOnComputerClock
                        && questLampCoverage
                        && councilHallIlluminated
                        && daySun > 0.65f && dayMoon < 0.05f
                        && nightSun < 0.05f && nightMoon > 0.25f
                        && nightFog > dayFog + 0.01f
                        && dayLamps > 0f && nightLamps > dayLamps * 4f
                        && dayHallLamps > 0f && nightHallLamps > dayHallLamps * 4f
                        && dayTorch < 0.05f && nightTorch > 4.5f
                        && Mathf.Abs(torchRange - PartyTorchRadius) < 0.05f
                        && torchOffset < 0.35f
                        && nightAudio > 0.12f && nightPhase
                        && returnedToComputerClock && saveIndependent;
            Debug.Log($"[AtmosphereTest] {(pass ? "PASS" : "FAIL")} - " +
                      $"host computer {FormatHour(computerAtStart)} matched; " +
                      $"quest-center flame lamps {healthyQuestLamps}/{questLamps.Length} " +
                      $"visible/lit and {(surroundsQuestGivers ? "surround" : "DO NOT SURROUND")} " +
                      $"{questGivers.Length} quest giver(s); " +
                      $"Council Hall lit by {hallLightsInRange}/{hallLamps.Length} marked lamps " +
                      $"({dayHallLamps:0.00} noon to {nightHallLamps:0.00} midnight); " +
                      $"noon sun {daySun:0.00}/moon {dayMoon:0.00}/fog {dayFog:0.000}/lamps {dayLamps:0.00}/torch {dayTorch:0.00}; " +
                      $"midnight sun {nightSun:0.00}/moon {nightMoon:0.00}/fog {nightFog:0.000}/lamps {nightLamps:0.00}/" +
                      $"torch {nightTorch:0.00} at {torchRange:0.0}m radius ({torchOffset:0.00}m offset), " +
                      $"night audio {nightAudio:0.00}; returned to computer " +
                      $"{FormatHour(synchronizedAfterTest)}, save " +
                      $"{(saveIndependent ? "excludes clock" : "STILL STORES CLOCK")}");
        }

        private IEnumerator CaptureRepresentativeTimes(string directory)
        {
            yield return new WaitForSeconds(8f);
            var director = GameDirector.Instance;
            if (director == null || !director.IsServerStarted) yield break;
            Directory.CreateDirectory(directory);

            // First frame is untouched wall-clock presentation; the other two are
            // deterministic regression references and are cleared back to wall time.
            ScreenCapture.CaptureScreenshot(Path.Combine(directory, "atmosphere_realtime.png"));
            yield return new WaitForSeconds(2f);

            director.ServerSetWorldHourForTest(12f);
            yield return new WaitForSeconds(2f);
            ScreenCapture.CaptureScreenshot(Path.Combine(directory, "atmosphere_noon.png"));
            yield return new WaitForSeconds(2f);

            director.ServerSetWorldHourForTest(0f);
            yield return new WaitForSeconds(2f);
            ScreenCapture.CaptureScreenshot(Path.Combine(directory, "atmosphere_midnight.png"));
            director.ServerClearWorldHourTestOverride();
            Debug.Log($"[AtmosphereCapture] wrote realtime, noon and midnight frames to {directory}; " +
                      $"restored host computer time {FormatHour(GameDirector.ComputerLocalHourNow())}");
        }

        private static string FormatHour(float hour)
        {
            hour = Mathf.Repeat(hour, 24f);
            int whole = Mathf.FloorToInt(hour);
            int minute = Mathf.FloorToInt((hour - whole) * 60f);
            return $"{whole:00}:{minute:00}";
        }

        private static float WrappedDistance(float a, float b)
        {
            float d = Mathf.Abs(a - b) % 24f;
            return Mathf.Min(d, 24f - d);
        }

        private static string PhaseFor(float hour)
        {
            hour = Mathf.Repeat(hour, 24f);
            if (hour < 5f || hour >= 20f) return "NIGHT";
            if (hour < 7f) return "DAWN";
            if (hour < 18f) return "DAY";
            return "DUSK";
        }
    }
}
