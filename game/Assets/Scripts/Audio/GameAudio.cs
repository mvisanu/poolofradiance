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

        private AudioSource _sfx;
        private AudioSource _exploreMusic;
        private AudioSource _combatMusic;
        private const float MusicVolume = 0.55f;
        private const float FadeSpeed = 1.2f;

        private void Awake()
        {
            Instance = this;
            _sfx = gameObject.AddComponent<AudioSource>();
            _sfx.playOnAwake = false;

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
            Instance._sfx.PlayOneShot(AudioSynth.Get(id), volume);
        }
    }
}
