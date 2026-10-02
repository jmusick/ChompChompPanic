using System.Linq;
using UnityEngine;

namespace ChompChompPanic
{
    /// <summary>
    /// Plays a map's music tracks in random order, crossfading from one to the next. Lives across scene loads,
    /// so restarting a session with the same tracks carries on with the current song instead of starting over.
    /// Volume is the scene's mix level times <see cref="GameSettings.MusicVolume"/>. Empty or missing tracks mean silence.
    /// </summary>
    public class MusicPlayer : MonoBehaviour
    {
        /// <summary>Seconds one track takes to fade into the next.</summary>
        const float CrossfadeSeconds = 2f;

        /// <summary>How fast the mix level follows <see cref="SetLevel"/>, per second.</summary>
        const float LevelSpeed = 1.5f;

        static MusicPlayer instance;

        // Two decks, so the outgoing track can fade out while the next fades in.
        readonly AudioSource[] decks = new AudioSource[2];
        readonly float[] fade = new float[2];
        readonly float[] fadeTarget = new float[2];
        int current;
        AudioClip[] playlist;
        int lastTrack = -1;
        float level;
        float targetLevel;
        float trackStartedAt;

        /// <summary>
        /// Play <paramref name="tracks"/> shuffled at mix level <paramref name="volume"/> (0-1). If the same tracks
        /// are already playing, they carry on and only the level changes.
        /// </summary>
        public static void Play(AudioClip[] tracks, float volume)
        {
            var valid = tracks?.Where(t => t != null).ToArray();
            if (valid is not { Length: > 0 })
            {
                Stop();
                return;
            }
            if (instance == null)
            {
                var go = new GameObject("Music Player");
                DontDestroyOnLoad(go);
                instance = go.AddComponent<MusicPlayer>();
            }
            instance.targetLevel = volume;
            if (instance.playlist != null && instance.playlist.SequenceEqual(valid))
                return;
            instance.playlist = valid;
            instance.lastTrack = -1;
            instance.NextTrack();
        }

        /// <summary>Fade the current music to mix level <paramref name="volume"/> (0-1), e.g. to duck it under a menu.</summary>
        public static void SetLevel(float volume)
        {
            if (instance != null)
                instance.targetLevel = volume;
        }

        /// <summary>Fade out and stop.</summary>
        public static void Stop()
        {
            if (instance == null)
                return;
            instance.playlist = null;
            instance.fadeTarget[0] = instance.fadeTarget[1] = 0f;
        }

        void Awake()
        {
            for (int i = 0; i < decks.Length; i++)
            {
                var deck = gameObject.AddComponent<AudioSource>();
                deck.playOnAwake = false;
                deck.spatialBlend = 0f;
                deck.ignoreListenerPause = true;
                decks[i] = deck;
            }
        }

        /// <summary>Fade out the playing deck and start a random track (not the one just played) on the other.</summary>
        void NextTrack()
        {
            bool first = !decks[0].isPlaying && !decks[1].isPlaying;
            fadeTarget[current] = 0f;
            current = 1 - current;

            int track = Random.Range(0, playlist.Length);
            if (track == lastTrack && playlist.Length > 1)
                track = (track + 1 + Random.Range(0, playlist.Length - 1)) % playlist.Length;
            lastTrack = track;

            var deck = decks[current];
            deck.clip = playlist[track];
            deck.time = 0f;
            deck.Play();
            trackStartedAt = Time.unscaledTime;
            fade[current] = first ? 1f : 0f;
            fadeTarget[current] = 1f;
            if (first)
                level = targetLevel;
        }

        void Update()
        {
            float dt = Time.unscaledDeltaTime;
            level = Mathf.MoveTowards(level, targetLevel, LevelSpeed * dt);

            if (playlist != null)
            {
                // A streamed clip can take a moment to report it's playing, so only trust isPlaying after a second.
                var deck = decks[current];
                bool ended = !deck.isPlaying && Time.unscaledTime - trackStartedAt > 1f;
                if (ended || deck.time >= deck.clip.length - CrossfadeSeconds)
                    NextTrack();
            }

            for (int i = 0; i < decks.Length; i++)
            {
                fade[i] = Mathf.MoveTowards(fade[i], fadeTarget[i], dt / CrossfadeSeconds);
                if (fade[i] <= 0f && fadeTarget[i] <= 0f && decks[i].isPlaying)
                    decks[i].Stop();
                decks[i].volume = fade[i] * level * GameSettings.MusicVolume;
            }
        }
    }
}
