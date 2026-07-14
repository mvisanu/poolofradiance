using System;
using System.Collections.Generic;
using UnityEngine;

namespace RadiantPool.Game
{
    /// <summary>Procedurally synthesized SFX and music — no audio assets required.
    /// Every clip is generated once on first use and cached. Replaced/augmented by
    /// licensed audio at the 3f art pass; the hooks stay the same.</summary>
    public static class AudioSynth
    {
        private const int Rate = 44100;
        private static readonly Dictionary<string, AudioClip> Cache =
            new Dictionary<string, AudioClip>();
        private static readonly System.Random Rand = new System.Random(1);

        public static AudioClip Get(string id)
        {
            if (Cache.TryGetValue(id, out var clip)) return clip;
            clip = Build(id);
            Cache[id] = clip;
            return clip;
        }

        private static AudioClip Build(string id) => id switch
        {
            "hit" => FromSamples(id, Hit(0.22f, 160f, 1f)),
            "crit" => FromSamples(id, Hit(0.35f, 120f, 1.4f)),
            "miss" => FromSamples(id, Whoosh(0.18f)),
            "spell" => FromSamples(id, Zap(0.3f)),
            "heal" => FromSamples(id, Chime(new[] { 523.25f, 659.25f, 783.99f }, 0.45f)),
            "chime" => FromSamples(id, Chime(new[] { 440f, 554.37f }, 0.35f)),
            "down" => FromSamples(id, Drone(0.6f, 82f)),
            "combat_start" => FromSamples(id, CombatStart()),
            "victory" => FromSamples(id, Arp(new[] { 392f, 493.88f, 587.33f, 783.99f }, 0.12f)),
            "defeat" => FromSamples(id, Arp(new[] { 392f, 369.99f, 311.13f, 261.63f }, 0.2f)),
            "distant_cry" => FromSamples(id, DistantCry()),
            "distant_bell" => FromSamples(id, DistantBell()),
            "night_ambience" => FromSamples(id, NightAmbience(), loop: true),
            "combat_tension" => FromSamples(id, CombatTension(), loop: true),
            "explore_music" => FromSamples(id, ExploreLoop(), loop: true),
            "combat_music" => FromSamples(id, CombatLoop(), loop: true),
            _ => FromSamples(id, Whoosh(0.1f))
        };

        private static AudioClip FromSamples(string id, float[] samples, bool loop = false)
        {
            var clip = AudioClip.Create(id, samples.Length, 1, Rate, false);
            clip.SetData(samples, 0);
            return clip;
        }

        // ---------- one-shots ----------

        /// <summary>Weapon impact: short swing transient, noise crack, metallic ring
        /// partials, and a pitched body thump. Crits get a sub-drop and longer ring.</summary>
        private static float[] Hit(float dur, float thumpHz, float gain)
        {
            bool crit = gain > 1.2f;
            var s = New(dur);
            float[] ringHz = { 1870f, 2745f, 3390f, 4210f };
            for (int i = 0; i < s.Length; i++)
            {
                float t = (float)i / Rate;
                // Crack: bright noise, very fast decay.
                float crack = ((float)Rand.NextDouble() * 2f - 1f) * Mathf.Exp(-t * 90f) * 0.9f;
                // Metallic ring: detuned high partials, medium decay.
                float ring = 0f;
                for (int p = 0; p < ringHz.Length; p++)
                    ring += Mathf.Sin(2f * Mathf.PI * ringHz[p] * t)
                            * Mathf.Exp(-t * (crit ? 14f : 22f)) / (p + 1.5f);
                // Body: pitched thump falling in frequency.
                float thump = Mathf.Sin(2f * Mathf.PI * thumpHz * t * (1f - t * 1.2f))
                              * Mathf.Exp(-t * 16f);
                // Crit sub-drop.
                float sub = crit
                    ? Mathf.Sin(2f * Mathf.PI * (62f - 25f * t) * t) * Mathf.Exp(-t * 7f) * 0.7f
                    : 0f;
                s[i] = (crack * 0.45f + ring * 0.35f + thump * 0.55f + sub) * gain * 0.55f;
            }
            return s;
        }

        private static float[] Whoosh(float dur)
        {
            var s = New(dur);
            float last = 0f;
            for (int i = 0; i < s.Length; i++)
            {
                float t = (float)i / Rate;
                float env = Mathf.Sin(Mathf.PI * t / dur);      // swell in and out
                float raw = (float)Rand.NextDouble() * 2f - 1f;
                last += ((raw - last) * 0.12f);                  // crude low-pass
                s[i] = last * env * 0.5f;
            }
            return s;
        }

        /// <summary>Spell bolt: FM synthesis (falling carrier, decaying modulation index)
        /// with a sparkle tail — rounder and more magical than a raw saw.</summary>
        private static float[] Zap(float dur)
        {
            var s = New(dur);
            float phase = 0f;
            for (int i = 0; i < s.Length; i++)
            {
                float t = (float)i / Rate;
                float env = Mathf.Exp(-t * 8f);
                float carrier = 620f - 380f * (t / dur);
                float modIndex = 5.5f * Mathf.Exp(-t * 10f);
                float mod = Mathf.Sin(2f * Mathf.PI * carrier * 2.7f * t) * modIndex;
                phase += 2f * Mathf.PI * carrier / Rate;
                float fm = Mathf.Sin(phase + mod);
                float sparkle = ((float)Rand.NextDouble() * 2f - 1f)
                                * Mathf.Exp(-t * 12f) * 0.12f;
                s[i] = (fm * env + sparkle) * 0.38f;
            }
            return s;
        }

        private static float[] Chime(float[] freqs, float dur)
        {
            var s = New(dur);
            for (int i = 0; i < s.Length; i++)
            {
                float t = (float)i / Rate;
                float env = Mathf.Exp(-t * 5f);
                float v = 0f;
                for (int f = 0; f < freqs.Length; f++)
                {
                    float delay = f * 0.06f;
                    if (t > delay)
                        v += Mathf.Sin(2f * Mathf.PI * freqs[f] * (t - delay))
                             * Mathf.Exp(-(t - delay) * 6f);
                }
                s[i] = v * env * 0.3f;
            }
            return s;
        }

        private static float[] Drone(float dur, float hz)
        {
            var s = New(dur);
            for (int i = 0; i < s.Length; i++)
            {
                float t = (float)i / Rate;
                float env = Mathf.Exp(-t * 4f);
                s[i] = (Mathf.Sin(2f * Mathf.PI * hz * t)
                        + 0.5f * Mathf.Sin(2f * Mathf.PI * hz * 0.5f * t)) * env * 0.4f;
            }
            return s;
        }

        private static float[] CombatStart()
        {
            var s = New(0.7f);
            for (int i = 0; i < s.Length; i++)
            {
                float t = (float)i / Rate;
                float boom = Mathf.Sin(2f * Mathf.PI * 65f * t) * Mathf.Exp(-t * 6f);
                float snare = ((float)Rand.NextDouble() * 2f - 1f) * Mathf.Exp(-t * 25f) * 0.4f;
                float horn = t > 0.15f
                    ? Mathf.Sin(2f * Mathf.PI * 196f * (t - 0.15f)) * Mathf.Exp(-(t - 0.15f) * 4f) * 0.35f
                    : 0f;
                s[i] = (boom + snare + horn) * 0.6f;
            }
            return s;
        }

        private static float[] DistantCry()
        {
            const float dur = 2.4f;
            var s = New(dur);
            float phase = 0f;
            float wind = 0f;
            for (int i = 0; i < s.Length; i++)
            {
                float t = (float)i / Rate;
                float env = Mathf.Sin(Mathf.PI * Mathf.Clamp01(t / dur))
                            * Mathf.Exp(-t * 0.55f);
                float hz = 410f - 115f * (t / dur) + Mathf.Sin(t * 8f) * 18f;
                phase += 2f * Mathf.PI * hz / Rate;
                float raw = (float)Rand.NextDouble() * 2f - 1f;
                wind += (raw - wind) * 0.018f;
                s[i] = (Mathf.Sin(phase) * 0.16f
                        + Mathf.Sin(phase * 0.503f) * 0.09f + wind * 0.22f) * env;
            }
            return s;
        }

        private static float[] DistantBell()
        {
            const float dur = 3.6f;
            var s = New(dur);
            float[] partials = { 92f, 137f, 221f, 314f, 487f };
            for (int i = 0; i < s.Length; i++)
            {
                float t = (float)i / Rate;
                float value = 0f;
                for (int p = 0; p < partials.Length; p++)
                    value += Mathf.Sin(2f * Mathf.PI * partials[p] * t)
                             * Mathf.Exp(-t * (0.75f + p * 0.34f)) / (p + 1f);
                s[i] = value * 0.13f;
            }
            return s;
        }

        private static float[] Arp(float[] notes, float noteDur)
        {
            var s = New(notes.Length * noteDur + 0.4f);
            for (int n = 0; n < notes.Length; n++)
            {
                int start = (int)(n * noteDur * Rate);
                float sustain = n == notes.Length - 1 ? 0.4f : noteDur;
                int len = (int)((noteDur + sustain) * Rate);
                for (int i = 0; i < len && start + i < s.Length; i++)
                {
                    float t = (float)i / Rate;
                    s[start + i] += (Mathf.Sin(2f * Mathf.PI * notes[n] * t)
                        + 0.3f * Mathf.Sin(4f * Mathf.PI * notes[n] * t))
                        * Mathf.Exp(-t * 5f) * 0.3f;
                }
            }
            return s;
        }

        // ---------- music loops ----------

        /// <summary>Explore ambience: slow Am–F–C–G pad, ~19 s seamless loop.</summary>
        private static float[] ExploreLoop()
        {
            float[][] chords =
            {
                new[] { 73.42f, 110f, 146.83f, 155.56f },
                new[] { 73.42f, 103.83f, 146.83f, 164.81f },
                new[] { 69.30f, 103.83f, 138.59f, 146.83f },
                new[] { 65.41f, 98f, 130.81f, 138.59f }
            };
            const float chordDur = 5.5f;
            var s = New(chords.Length * chordDur);
            for (int c = 0; c < chords.Length; c++)
            {
                int start = (int)(c * chordDur * Rate);
                int len = (int)(chordDur * Rate);
                for (int i = 0; i < len; i++)
                {
                    float t = (float)i / Rate;
                    // Crossfade chord edges for a seamless loop.
                    float fade = Mathf.Min(1f, Mathf.Min(t / 1.4f, (chordDur - t) / 1.4f));
                    float v = 0f;
                    foreach (float f in chords[c])
                        v += Mathf.Sin(2f * Mathf.PI * f * t)
                             * (1f + 0.12f * Mathf.Sin(2f * Mathf.PI * 0.17f * t));
                    float distant = Mathf.Sin(2f * Mathf.PI * chords[c][0] * 0.5f * t)
                                     * (0.55f + 0.45f * Mathf.Sin(t * 0.31f));
                    s[start + i] = (v / chords[c].Length + distant * 0.35f) * fade * 0.11f;
                }
            }
            return s;
        }

        /// <summary>Combat loop: driving low pulse in A minor with percussion ticks, ~7.7 s.</summary>
        private static float[] CombatLoop()
        {
            const float bpm = 88f;
            const float beat = 60f / bpm;
            const int beats = 16;
            var s = New(beats * beat);
            float[] bass = { 73.42f, 73.42f, 77.78f, 69.30f };
            for (int b = 0; b < beats; b++)
            {
                int start = (int)(b * beat * Rate);
                int len = (int)(beat * Rate);
                float note = bass[(b / 4) % 4];
                for (int i = 0; i < len; i++)
                {
                    float t = (float)i / Rate;
                    float pulse = Mathf.Sin(2f * Mathf.PI * note * t) * Mathf.Exp(-t * 4f) * 0.34f;
                    float tick = b % 2 == 0 && t < 0.045f
                        ? ((float)Rand.NextDouble() * 2f - 1f) * (1f - t / 0.045f) * 0.34f
                        : 0f;
                    float tritone = Mathf.Sin(2f * Mathf.PI * note * 1.414f * t)
                                    * Mathf.Exp(-t * 6f) * 0.11f;
                    s[start + i] = pulse + tick + tritone;
                }
            }
            return s;
        }

        /// <summary>Seamless night bed: filtered wind, a low two-note drone and barely
        /// audible high movement. It is atmosphere, not another melody.</summary>
        private static float[] NightAmbience()
        {
            const float dur = 16f;
            var s = New(dur);
            float low = 0f, high = 0f;
            for (int i = 0; i < s.Length; i++)
            {
                float t = (float)i / Rate;
                float raw = (float)Rand.NextDouble() * 2f - 1f;
                low += (raw - low) * 0.0035f;
                high += (raw - high) * 0.038f;
                float seam = Mathf.Min(1f, Mathf.Min(t / 1.2f, (dur - t) / 1.2f));
                float drone = Mathf.Sin(2f * Mathf.PI * 46.25f * t) * 0.13f
                              + Mathf.Sin(2f * Mathf.PI * 51.91f * t) * 0.09f;
                float shimmer = Mathf.Sin(2f * Mathf.PI * 733f * t
                                           + Mathf.Sin(t * 0.4f) * 4f) * 0.012f;
                s[i] = (low * 1.45f + (raw - high) * 0.025f + drone + shimmer) * seam;
            }
            return s;
        }

        /// <summary>Heartbeat/sub-rumble layer crossfaded under combat music.</summary>
        private static float[] CombatTension()
        {
            const float dur = 8f;
            var s = New(dur);
            for (int i = 0; i < s.Length; i++)
            {
                float t = (float)i / Rate;
                float beatPhase = t % 1.15f;
                float heart = 0f;
                if (beatPhase < 0.12f)
                    heart += Mathf.Sin(2f * Mathf.PI * 54f * beatPhase)
                             * Mathf.Exp(-beatPhase * 28f);
                float second = beatPhase - 0.19f;
                if (second >= 0f && second < 0.13f)
                    heart += Mathf.Sin(2f * Mathf.PI * 48f * second)
                             * Mathf.Exp(-second * 25f) * 0.72f;
                float sub = Mathf.Sin(2f * Mathf.PI * 31f * t) * 0.055f;
                s[i] = heart * 0.46f + sub;
            }
            return s;
        }

        private static float[] New(float seconds) => new float[(int)(seconds * Rate)];
    }
}
