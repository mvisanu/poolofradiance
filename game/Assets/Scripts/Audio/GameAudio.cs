using FishNet.Object;
using UnityEngine;

namespace RadiantPool.Game
{
    /// <summary>2D SFX playback + adaptive music: an ambient pad while exploring, a
    /// driving pulse in combat, crossfaded on the replicated combat flag. All clips
    /// come from AudioSynth (procedural) so the build ships silent-asset-free.</summary>
    public class GameAudio : MonoBehaviour
    {
        public static GameAudio Instance { get; private set; }

        private AudioSource[] _sfx;
        private int _nextSfx;
        private AudioSource _exploreMusic;
        private AudioSource _combatMusic;
        private const float MusicVolume = 0.55f;
        private const float FadeSpeed = 1.2f;

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
        }

        private void OnDestroy() { if (Instance == this) Instance = null; }

        private void Update()
        {
            bool combat = CombatManager.Instance != null && CombatManager.Instance.InCombat.Value;
            float target = combat ? 0f : MusicVolume;
            _exploreMusic.volume = Mathf.MoveTowards(
                _exploreMusic.volume, target, FadeSpeed * Time.deltaTime);
            _combatMusic.volume = Mathf.MoveTowards(
                _combatMusic.volume, MusicVolume - target, FadeSpeed * Time.deltaTime);
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
