// Copyright 2026 Capitol Interactive LLC
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//      http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

using System.Collections.Generic;
using UnityEngine;

namespace Telewheel
{
    public enum TwSound
    {
        Click,
        Tick,
        Beep,
        Go,
        Ding,
        Buzz,
        Pop,
        Whoosh,
        Poof,
        Fanfare,
    }

    /// <summary>
    /// Sound effects, synthesised at runtime so the game has audio before any recordings exist,
    /// plus a hook for the voice-over: drop an AudioClip named like "vo_get_ready" into
    /// Resources/Telewheel/Audio and it plays at that moment.
    /// </summary>
    public static class TwAudio
    {
        private const int SampleRate = 44100;

        private static readonly Dictionary<TwSound, AudioClip> s_Clips = new Dictionary<TwSound, AudioClip>();
        private static AudioSource s_Source;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            s_Clips.Clear();
            s_Source = null;
        }

        public static void Init(GameObject host)
        {
            s_Source = host.AddComponent<AudioSource>();
            s_Source.playOnAwake = false;
            s_Source.spatialBlend = 0f;
        }

        public static void Play(TwSound sound, float volume = 0.5f)
        {
            if (s_Source == null)
            {
                return;
            }
            s_Source.PlayOneShot(Get(sound), volume);
        }

        /// <summary>Plays the voice-over clip with this name, if one has been supplied.</summary>
        public static void PlayVoice(string key)
        {
            if (s_Source == null)
            {
                return;
            }
            AudioClip clip = Resources.Load<AudioClip>("Telewheel/Audio/" + key);
            if (clip != null)
            {
                s_Source.PlayOneShot(clip, 1f);
            }
        }

        private static AudioClip Get(TwSound sound)
        {
            AudioClip clip;
            if (s_Clips.TryGetValue(sound, out clip) && clip != null)
            {
                return clip;
            }
            clip = Build(sound);
            s_Clips[sound] = clip;
            return clip;
        }

        private static AudioClip Build(TwSound sound)
        {
            float[] samples;
            switch (sound)
            {
                case TwSound.Click:
                    samples = Tone(880f, 0.05f, 40f);
                    break;
                case TwSound.Tick:
                    samples = Tone(1400f, 0.025f, 80f, 0.4f);
                    break;
                case TwSound.Beep:
                    samples = Tone(660f, 0.15f, 12f);
                    break;
                case TwSound.Go:
                    samples = Tone(990f, 0.4f, 6f);
                    break;
                case TwSound.Ding:
                    samples = Sequence(new[] { 523.25f, 659.25f, 783.99f }, 0.09f, 0.35f);
                    break;
                case TwSound.Buzz:
                    samples = Sweep(220f, 110f, 0.4f, 5f);
                    break;
                case TwSound.Pop:
                    samples = Sweep(300f, 900f, 0.12f, 20f);
                    break;
                case TwSound.Whoosh:
                    samples = Noise(0.45f, 5f, 0.12f, true);
                    break;
                case TwSound.Poof:
                    samples = Noise(0.4f, 9f, 0.25f, false);
                    break;
                default:
                    samples = Sequence(new[] { 523.25f, 659.25f, 783.99f, 1046.5f }, 0.12f, 0.5f);
                    break;
            }
            AudioClip clip = AudioClip.Create("Telewheel " + sound, samples.Length, 1, SampleRate, false);
            clip.SetData(samples, 0);
            return clip;
        }

        // A decaying tone with a soft overtone and a tiny attack so it does not click.
        private static float[] Tone(float frequency, float seconds, float decay, float gain = 0.5f)
        {
            var samples = new float[(int)(SampleRate * seconds)];
            for (int i = 0; i < samples.Length; i++)
            {
                float t = i / (float)SampleRate;
                float envelope = Mathf.Exp(-decay * t) * Mathf.Min(1f, i / 120f);
                float wave = Mathf.Sin(2f * Mathf.PI * frequency * t)
                    + 0.3f * Mathf.Sin(4f * Mathf.PI * frequency * t);
                samples[i] = wave * envelope * gain;
            }
            return samples;
        }

        // Notes one after another, each ringing on into the next.
        private static float[] Sequence(float[] notes, float spacing, float gain)
        {
            int tail = (int)(SampleRate * 0.4f);
            int step = (int)(SampleRate * spacing);
            var samples = new float[step * (notes.Length - 1) + tail];
            for (int n = 0; n < notes.Length; n++)
            {
                float[] tone = Tone(notes[n], 0.4f, 7f, gain);
                int offset = step * n;
                for (int i = 0; i < tone.Length && offset + i < samples.Length; i++)
                {
                    samples[offset + i] += tone[i];
                }
            }
            return samples;
        }

        // A tone whose pitch glides from one frequency to another.
        private static float[] Sweep(float from, float to, float seconds, float decay)
        {
            var samples = new float[(int)(SampleRate * seconds)];
            float phase = 0f;
            for (int i = 0; i < samples.Length; i++)
            {
                float t = i / (float)SampleRate;
                float frequency = Mathf.Lerp(from, to, i / (float)samples.Length);
                phase += 2f * Mathf.PI * frequency / SampleRate;
                float envelope = Mathf.Exp(-decay * t) * Mathf.Min(1f, i / 80f);
                samples[i] = Mathf.Sign(Mathf.Sin(phase)) * 0.5f * envelope * 0.5f
                    + Mathf.Sin(phase) * envelope * 0.4f;
            }
            return samples;
        }

        // Softened noise: a poof (loudest at the start) or a whoosh (swelling in the middle).
        private static float[] Noise(float seconds, float decay, float smoothing, bool swell)
        {
            var random = new TwRandom(7);
            var samples = new float[(int)(SampleRate * seconds)];
            float previous = 0f;
            for (int i = 0; i < samples.Length; i++)
            {
                float t = i / (float)samples.Length;
                float envelope = swell
                    ? Mathf.Sin(t * Mathf.PI) * 0.8f
                    : Mathf.Exp(-decay * t * seconds);
                float raw = random.NextRange(-1f, 1f);
                previous = Mathf.Lerp(previous, raw, smoothing);
                samples[i] = previous * envelope * 0.9f;
            }
            return samples;
        }
    }
}
