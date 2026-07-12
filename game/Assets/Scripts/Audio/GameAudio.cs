using System.Linq;
using FishNet.Object;
using UnityEngine;

namespace RadiantPool.Game
{
    /// <summary>2D SFX playback + adaptive music: an ambient pad while exploring, a
    /// driving pulse in combat, and optional per-zone tracks, crossfaded on the
    /// replicated combat flag and the player's position. Clips default to AudioSynth
    /// (procedural) so the build ships silent-asset-free; any clip dropped into
    /// Resources/Music (explore / combat / zone_&lt;zoneId&gt;) overrides its loop —
    /// see Resources/Music/README.txt.</summary>
    public class GameAudio : MonoBehaviour
    {
        public static GameAudio Instance { get; private set; }

        private AudioSource[] _sfx;
        private int _nextSfx;
        private AudioSource _exploreMusic;
        private AudioSource _combatMusic;
        private AudioSource _zoneMusic;
        private const float MusicVolume = 0.55f;
        private const float FadeSpeed = 1.2f;

        private readonly System.Collections.Generic.Dictionary<string, AudioClip> _zoneClips =
            new System.Collections.Generic.Dictionary<string, AudioClip>();
        private System.Collections.Generic.Dictionary<string, Bounds> _zoneBounds;
        private Transform _localPlayer;
        private float _nextZoneScan;
        private string _currentZone = "";

        private void Awake()
        {
            Instance = this;
            // Round-robin voices so overlapping hits can each carry a random pitch.
            _sfx = new AudioSource[4];
            for (int i = 0; i < _sfx.Length; i++)
            {
                _sfx[i] = gameObject.AddComponent<AudioSource>();
                _sfx[i].playOnAwake = false;
            }

            _exploreMusic = gameObject.AddComponent<AudioSource>();
            _exploreMusic.clip = AudioSynth.Get("explore_music");
            _exploreMusic.loop = true;
            _exploreMusic.volume = MusicVolume;
            _exploreMusic.Play();

            _combatMusic = gameObject.AddComponent<AudioSource>();
            _combatMusic.clip = AudioSynth.Get("combat_music");
            _combatMusic.loop = true;
            _combatMusic.volume = 0f;
            _combatMusic.Play();

            // Drop-in overrides (e.g. an imported Asset Store music pack).
            var exploreOverride = Resources.Load<AudioClip>("Music/explore");
            if (exploreOverride != null) _exploreMusic.clip = exploreOverride;
            var combatOverride = Resources.Load<AudioClip>("Music/combat");
            if (combatOverride != null) _combatMusic.clip = combatOverride;
            if (exploreOverride != null || combatOverride != null)
            {
                _exploreMusic.Stop(); _exploreMusic.Play();
                _combatMusic.Stop(); _combatMusic.Play();
            }

            _zoneMusic = gameObject.AddComponent<AudioSource>();
            _zoneMusic.loop = true;
            _zoneMusic.volume = 0f;
        }

        private void OnDestroy() { if (Instance == this) Instance = null; }

        private void Update()
        {
            bool combat = CombatManager.Instance != null && CombatManager.Instance.InCombat.Value;
            var zoneClip = combat ? null : CurrentZoneClip();
            if (zoneClip != null && _zoneMusic.clip != zoneClip)
            {
                _zoneMusic.clip = zoneClip;
                _zoneMusic.Play();
            }
            FadeTo(_combatMusic, combat ? MusicVolume : 0f);
            FadeTo(_zoneMusic, !combat && zoneClip != null ? MusicVolume : 0f);
            FadeTo(_exploreMusic, !combat && zoneClip == null ? MusicVolume : 0f);
        }

        private static void FadeTo(AudioSource src, float target) =>
            src.volume = Mathf.MoveTowards(src.volume, target, FadeSpeed * Time.deltaTime);

        /// <summary>While the player stands in a quest zone (bounding box of its
        /// encounter blocks, expanded by a margin), a clip at Resources/Music/
        /// zone_&lt;zoneId&gt; takes over from the explore loop. Null = no override.</summary>
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
                _zoneClips[_currentZone] = clip;   // caches null too
            }
            return clip;
        }

        private void BuildZoneBounds()
        {
            _zoneBounds = new System.Collections.Generic.Dictionary<string, Bounds>();
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

        public static void Play(string id, float volume = 1f)
        {
            if (Instance == null) return;
            var voice = Instance._sfx[Instance._nextSfx];
            Instance._nextSfx = (Instance._nextSfx + 1) % Instance._sfx.Length;
            // Small random pitch spread keeps repeated impacts from sounding cloned.
            voice.pitch = id is "hit" or "crit" or "miss" or "spell"
                ? Random.Range(0.92f, 1.08f) : 1f;
            voice.PlayOneShot(AudioSynth.Get(id), volume);
        }
    }
}
