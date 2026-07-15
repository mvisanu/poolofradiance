using System.Collections;
using System.Collections.Generic;
using System.Linq;
using FishNet.Object;
using UnityEngine;

namespace RadiantPool.Game
{
    /// <summary>2D combat SFX and adaptive music. Locally licensed Asset Store clips
    /// override the procedural AudioSynth fallback: Caves and Dungeons while exploring,
    /// an Action RPG Battle Music playlist in combat, and event-specific weapon/spell
    /// recordings. Raw licensed clips remain gitignored and are installed per machine.</summary>
    public class GameAudio : MonoBehaviour
    {
        public static GameAudio Instance { get; private set; }

        private static readonly string[] ZoneIds =
        {
            "old_docks", "drowned_market", "sunken_warcamp", "glasslit_temple", "ashen_ward"
        };

        private static readonly string[] CombatTrackTitles =
        {
            "Horns Of War", "The Ambush", "Enemy Approaches", "Swords At Midnight"
        };

        private static readonly string[] RequiredSfx =
        {
            "weapon_swing", "weapon_hit", "weapon_miss", "weapon_bash", "weapon_crit",
            "weapon_bow", "spell_fire_cast", "spell_fire_impact", "spell_arcane_cast",
            "spell_arcane_impact", "spell_radiant_cast", "spell_radiant_impact",
            "spell_heal", "spell_control", "spell_shield"
        };

        private AudioSource[] _sfx;
        private int _nextSfx;
        private AudioSource _exploreMusic;
        private AudioSource _combatMusic;
        private AudioSource _zoneMusic;
        private AudioSource _nightAmbience;
        private AudioSource _combatTension;
        private AudioClip[] _combatPlaylist;
        private int _activeCombatIndex = -1;
        private bool _wasCombat;
        private const float MusicVolume = 0.43f;
        private const float FadeSpeed = 1.2f;
        private float _nextScareCue;

        public float NightAmbienceLevel => _nightAmbience != null ? _nightAmbience.volume : 0f;
        public int SfxEventsPlayed { get; private set; }
        public int LicensedSfxEventsPlayed { get; private set; }
        public int CombatTrackChanges { get; private set; }
        public string ActiveCombatTrackName { get; private set; } = "";
        public string LastLicensedCue { get; private set; } = "";
        public int CombatPlaylistCount => _combatPlaylist?.Length ?? 0;
        public int CavesTrackCount { get; private set; }
        public bool AssetMusicReady { get; private set; }
        public bool AssetSfxReady { get; private set; }
        public bool AssetAudioReady => AssetMusicReady && AssetSfxReady;

        private readonly Dictionary<string, AudioClip> _zoneClips =
            new Dictionary<string, AudioClip>();
        private readonly Dictionary<string, AudioClip[]> _assetSfx =
            new Dictionary<string, AudioClip[]>();
        private Dictionary<string, Bounds> _zoneBounds;
        private Transform _localPlayer;
        private float _nextZoneScan;
        private string _currentZone = "";

        private void Awake()
        {
            Instance = this;
            // WorldAtmosphere is a local renderer of GameDirector's replicated clock, so
            // it is safe to add dynamically and does not consume a FishNet behaviour slot.
            if (GetComponent<WorldAtmosphere>() == null)
                gameObject.AddComponent<WorldAtmosphere>();

            // More voices than the old synthetic set: a swing, impact, critical accent and
            // spell tail must be able to overlap without cutting one another off.
            _sfx = new AudioSource[8];
            for (int i = 0; i < _sfx.Length; i++)
            {
                _sfx[i] = gameObject.AddComponent<AudioSource>();
                _sfx[i].playOnAwake = false;
                _sfx[i].spatialBlend = 0f;
            }

            foreach (string cue in RequiredSfx)
                _assetSfx[cue] = LoadVariants(cue);
            AssetSfxReady = RequiredSfx.All(c => _assetSfx[c].Length > 0);

            var exploreOverride = Resources.Load<AudioClip>("Music/explore");
            _combatPlaylist = LoadNumberedClips("Music/combat", 4);
            foreach (string zone in ZoneIds)
            {
                var clip = Resources.Load<AudioClip>($"Music/zone_{zone}");
                _zoneClips[zone] = clip;
                if (clip != null) CavesTrackCount++;
            }

            AssetMusicReady = exploreOverride != null
                              && _combatPlaylist.Length == CombatTrackTitles.Length
                              && CavesTrackCount == ZoneIds.Length;

            _exploreMusic = MusicSource(exploreOverride ?? AudioSynth.Get("explore_music"), MusicVolume);

            var legacyCombat = Resources.Load<AudioClip>("Music/combat");
            var initialCombat = _combatPlaylist.Length > 0
                ? _combatPlaylist[0]
                : legacyCombat ?? AudioSynth.Get("combat_music");
            _combatMusic = MusicSource(initialCombat, 0f);

            _zoneMusic = gameObject.AddComponent<AudioSource>();
            _zoneMusic.loop = true;
            _zoneMusic.playOnAwake = false;
            _zoneMusic.volume = 0f;

            _nightAmbience = LoopingSource("night_ambience");
            _combatTension = LoopingSource("combat_tension");
            _nextScareCue = Time.unscaledTime + Random.Range(12f, 24f);

            Debug.Log($"[AudioAssets] {(AssetAudioReady ? "READY" : "FALLBACK")} - " +
                      $"Caves and Dungeons {CavesTrackCount + (exploreOverride != null ? 1 : 0)}/6, " +
                      $"Action RPG Battle Music {_combatPlaylist.Length}/4, " +
                      $"realistic SFX {_assetSfx.Count(kv => kv.Value.Length > 0)}/{RequiredSfx.Length}");
        }

        private void OnDestroy() { if (Instance == this) Instance = null; }

        private void Update()
        {
            bool combat = CombatManager.Instance != null && CombatManager.Instance.InCombat.Value;
            if (combat && !_wasCombat) BeginCombatTrack();
            _wasCombat = combat;

            var zoneClip = combat ? null : CurrentZoneClip();
            if (zoneClip != null && _zoneMusic.clip != zoneClip)
            {
                _zoneMusic.clip = zoneClip;
                _zoneMusic.Play();
            }
            FadeTo(_combatMusic, combat ? MusicVolume : 0f);
            FadeTo(_zoneMusic, !combat && zoneClip != null ? MusicVolume : 0f);
            FadeTo(_exploreMusic, !combat && zoneClip == null ? MusicVolume : 0f);
            FadeTo(_nightAmbience, Mathf.Lerp(0.02f, combat ? 0.22f : 0.34f,
                WorldAtmosphere.NightWeight));
            FadeTo(_combatTension, combat ? 0.18f : 0f);

            // Sparse, quiet cues make an empty street feel inhabited by something unseen.
            if (!combat && WorldAtmosphere.NightWeight > 0.72f
                && Time.unscaledTime >= _nextScareCue)
            {
                Play(Random.value > 0.45f ? "distant_cry" : "distant_bell", 0.18f);
                _nextScareCue = Time.unscaledTime + Random.Range(15f, 31f);
            }
        }

        private AudioSource MusicSource(AudioClip clip, float volume)
        {
            var source = gameObject.AddComponent<AudioSource>();
            source.clip = clip;
            source.loop = true;
            source.playOnAwake = false;
            source.volume = volume;
            source.Play();
            return source;
        }

        private void BeginCombatTrack()
        {
            if (_combatPlaylist.Length == 0) return;
            int next = _combatPlaylist.Length == 1 ? 0 : Random.Range(0, _combatPlaylist.Length);
            if (next == _activeCombatIndex) next = (next + 1) % _combatPlaylist.Length;
            _activeCombatIndex = next;
            _combatMusic.clip = _combatPlaylist[next];
            _combatMusic.Play();
            ActiveCombatTrackName = CombatTrackTitles[next];
            CombatTrackChanges++;
            Debug.Log($"[CombatMusic] Action RPG Battle Music - {ActiveCombatTrackName}");
        }

        private AudioSource LoopingSource(string id)
        {
            var source = gameObject.AddComponent<AudioSource>();
            source.clip = AudioSynth.Get(id);
            source.loop = true;
            source.volume = 0f;
            source.Play();
            return source;
        }

        private static void FadeTo(AudioSource src, float target) =>
            src.volume = Mathf.MoveTowards(src.volume, target, FadeSpeed * Time.deltaTime);

        private static AudioClip[] LoadNumberedClips(string pathPrefix, int maximum)
        {
            var clips = new List<AudioClip>();
            for (int i = 1; i <= maximum; i++)
            {
                var clip = Resources.Load<AudioClip>($"{pathPrefix}_{i:00}");
                if (clip != null) clips.Add(clip);
            }
            return clips.ToArray();
        }

        private static AudioClip[] LoadVariants(string cue) =>
            LoadNumberedClips($"Sfx/{cue}", 8);

        /// <summary>While the player stands in a quest zone (bounding box of its
        /// encounter blocks, expanded by a margin), its Caves and Dungeons loop takes
        /// over from the general exploration loop.</summary>
        private AudioClip CurrentZoneClip()
        {
            if (_zoneBounds == null || Time.time >= _nextZoneScan)
            {
                _nextZoneScan = Time.time + 1.5f;
                if (_zoneBounds == null || _zoneBounds.Count == 0) BuildZoneBounds();
                if (_localPlayer == null)
                    _localPlayer = FindObjectsByType<PlayerCharacterHolder>(FindObjectsSortMode.None)
                        .FirstOrDefault(p => p.IsOwner)?.transform;
                _currentZone = "";
                if (_localPlayer != null)
                    foreach (var kv in _zoneBounds)
                    {
                        var b = kv.Value;
                        b.Expand(24f);
                        if (b.Contains(_localPlayer.position)) { _currentZone = kv.Key; break; }
                    }
            }
            if (_currentZone.Length == 0) return null;
            if (!_zoneClips.TryGetValue(_currentZone, out var clip))
            {
                clip = Resources.Load<AudioClip>($"Music/zone_{_currentZone}");
                _zoneClips[_currentZone] = clip;
            }
            return clip;
        }

        private void BuildZoneBounds()
        {
            _zoneBounds = new Dictionary<string, Bounds>();
            foreach (var t in FindObjectsByType<EncounterTrigger>(FindObjectsSortMode.None))
            {
                if (string.IsNullOrEmpty(t.ZoneId)) continue;
                var box = t.GetComponent<BoxCollider>();
                var b = new Bounds(t.transform.position,
                    box != null ? box.size : Vector3.one * 8f);
                if (_zoneBounds.TryGetValue(t.ZoneId, out var agg))
                {
                    agg.Encapsulate(b);
                    _zoneBounds[t.ZoneId] = agg;
                }
                else _zoneBounds[t.ZoneId] = b;
            }
        }

        /// <summary>Play the physical wind-up now and the material impact at the same
        /// 0.12-second point as CombatFx. Bow and blunt attacks use distinct recordings.</summary>
        public static void PlayWeaponAttack(string attackName, bool hit, bool critical)
        {
            if (Instance == null) return;
            string name = (attackName ?? "").ToLowerInvariant();
            bool bow = name.Contains("bow") || name.Contains("crossbow");
            bool blunt = name.Contains("mace") || name.Contains("hammer")
                         || name.Contains("club") || name.Contains("staff")
                         || name.Contains("bash");
            Instance.PlayCue(bow ? "weapon_bow" : "weapon_swing", "miss", 0.76f);
            Instance.StartCoroutine(Instance.DelayedWeaponImpact(hit, critical, blunt));
        }

        private IEnumerator DelayedWeaponImpact(bool hit, bool critical, bool blunt)
        {
            yield return new WaitForSeconds(0.12f);
            if (!hit)
            {
                PlayCue("weapon_miss", "miss", 0.72f);
                yield break;
            }
            PlayCue(blunt ? "weapon_bash" : "weapon_hit", "hit", 0.9f);
            if (critical) PlayCue("weapon_crit", "crit", 0.92f);
        }

        /// <summary>Choose a cast and impact recording from the rules spell id. The same
        /// method is called by real combat RPCs and the unattended audio assertion.</summary>
        public static void PlaySpell(string spellId, bool isHeal)
        {
            if (Instance == null) return;
            string id = (spellId ?? "").ToLowerInvariant();
            if (isHeal || id.Contains("heal") || id.Contains("cure") || id.Contains("potion"))
            {
                Instance.PlayCue("spell_heal", "heal", 0.85f);
                return;
            }
            if (id.Contains("shield"))
            {
                Instance.PlayCue("spell_shield", "spell", 0.84f);
                return;
            }
            if (id.Contains("sleep") || id.Contains("bless"))
            {
                Instance.PlayCue("spell_control", "spell", 0.84f);
                return;
            }

            string family = id.Contains("fire") || id.Contains("burning") ? "fire"
                : id.Contains("sacred") || id.Contains("guiding") ? "radiant"
                : "arcane";
            Instance.PlayCue($"spell_{family}_cast", "spell", 0.82f);
            Instance.StartCoroutine(Instance.DelayedSpellImpact(family));
        }

        private IEnumerator DelayedSpellImpact(string family)
        {
            yield return new WaitForSeconds(0.16f);
            PlayCue($"spell_{family}_impact", "spell", 0.86f);
        }

        public static void Play(string id, float volume = 1f)
        {
            if (Instance == null) return;
            Instance.PlayCue(id, id, volume);
        }

        private void PlayCue(string assetCue, string fallbackId, float volume)
        {
            SfxEventsPlayed++;
            AudioClip clip = null;
            if (_assetSfx.TryGetValue(assetCue, out var variants) && variants.Length > 0)
            {
                clip = variants[Random.Range(0, variants.Length)];
                LicensedSfxEventsPlayed++;
                LastLicensedCue = assetCue;
            }
            if (clip == null) clip = AudioSynth.Get(fallbackId);

            var voice = _sfx[_nextSfx];
            _nextSfx = (_nextSfx + 1) % _sfx.Length;
            voice.pitch = Random.Range(0.96f, 1.04f);
            voice.PlayOneShot(clip, volume);
        }
    }
}
