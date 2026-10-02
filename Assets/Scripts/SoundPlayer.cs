using System.Collections.Generic;
using UnityEngine;

namespace ChompChompPanic
{
    /// <summary>
    /// Plays one-shot sound effects through a small pool of 2D audio sources, each with its own volume and
    /// pitch plus a little random pitch variation so repeats don't sound mechanical. The pool lives across
    /// scene loads, so a menu confirm or game-over sting isn't cut off when the scene changes.
    /// Null clips are ignored: missing audio just means silence.
    /// </summary>
    public class SoundPlayer : MonoBehaviour
    {
        const int Voices = 24;

        /// <summary>The same clip isn't restarted more often than this, so a volley of shots doesn't stack into noise.</summary>
        const float MinRepeatInterval = 0.05f;

        static SoundPlayer instance;

        readonly AudioSource[] sources = new AudioSource[Voices];
        readonly Dictionary<AudioClip, float> lastPlayed = new();
        int next;

        /// <summary>Play <paramref name="clip"/> once at the given volume (0-1) and pitch (1 = as recorded).</summary>
        public static void Play(AudioClip clip, float volume = 1f, float pitch = 1f, float pitchJitter = 0.05f)
        {
            if (clip == null || volume <= 0.01f)
                return;
            if (instance == null)
            {
                var go = new GameObject("Sound Player");
                DontDestroyOnLoad(go);
                instance = go.AddComponent<SoundPlayer>();
            }
            instance.PlayClip(clip, volume, pitch * (1f + Random.Range(-pitchJitter, pitchJitter)));
        }

        void Awake()
        {
            for (int i = 0; i < Voices; i++)
            {
                var source = gameObject.AddComponent<AudioSource>();
                source.playOnAwake = false;
                source.spatialBlend = 0f;
                sources[i] = source;
            }
        }

        void PlayClip(AudioClip clip, float volume, float pitch)
        {
            float now = Time.unscaledTime;
            if (lastPlayed.TryGetValue(clip, out float last) && now - last < MinRepeatInterval)
                return;
            lastPlayed[clip] = now;

            // Use the next idle voice; when all are busy, cut off the one after the last we started.
            var source = sources[next];
            for (int i = 0; i < Voices; i++)
            {
                var candidate = sources[(next + i) % Voices];
                if (!candidate.isPlaying)
                {
                    source = candidate;
                    break;
                }
            }
            next = (System.Array.IndexOf(sources, source) + 1) % Voices;

            source.clip = clip;
            source.volume = volume;
            source.pitch = pitch;
            source.Play();
        }
    }
}
