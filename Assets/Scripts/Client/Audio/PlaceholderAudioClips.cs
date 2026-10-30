using UnityEngine;

namespace MMO.Client
{
    /// <summary>
    /// Procedural stand-ins so the spatial/voice-capped audio path is audible before the art
    /// pass. Real clips replace these in #77; nothing else in the service changes.
    ///
    /// Cached for the process lifetime and built lazily, so no clip is allocated until the first
    /// impact actually requests one.
    /// </summary>
    public static class PlaceholderAudioClips
    {
        private const int SampleRate = 44100;

        private static AudioClip _impact;
        private static AudioClip _spell;

        /// <summary>A short dry thud for a landed hit.</summary>
        public static AudioClip Impact => _impact ??= Build("PlaceholderImpact", 0.14f, 180f, noise: true);

        /// <summary>A short bright burst for a spell resolving.</summary>
        public static AudioClip Spell => _spell ??= Build("PlaceholderSpell", 0.22f, 520f, noise: false);

        private static AudioClip Build(string name, float duration, float frequency, bool noise)
        {
            int sampleCount = Mathf.Max(1, Mathf.CeilToInt(duration * SampleRate));
            var samples = new float[sampleCount];
            uint seed = noise ? 0x9E3779B9u : 0x85EBCA6Bu;

            for (int i = 0; i < sampleCount; i++)
            {
                float time = i / (float)SampleRate;
                float envelope = 1f - i / (float)sampleCount;
                float value = Mathf.Sin(2f * Mathf.PI * frequency * time) * envelope;

                if (noise)
                {
                    // Deterministic LCG keeps the placeholder identical between runs.
                    seed = seed * 1664525u + 1013904223u;
                    float white = (seed >> 8) / 8388608f - 1f;
                    value += white * 0.35f * envelope;
                }

                samples[i] = Mathf.Clamp(value, -1f, 1f);
            }

            AudioClip clip = AudioClip.Create(name, sampleCount, channels: 1, SampleRate, stream: false);
            clip.SetData(samples, 0);
            return clip;
        }
    }
}
