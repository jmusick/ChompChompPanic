using UnityEngine;

namespace ChompChompPanic
{
    /// <summary>
    /// Player-facing options (music and sound-effect volume), kept in <see cref="PlayerPrefs"/> between runs.
    /// Changes take effect immediately; call <see cref="Save"/> to write them to disk.
    /// </summary>
    public static class GameSettings
    {
        const string MusicKey = "musicVolume";
        const string SfxKey = "sfxVolume";

        static float? musicVolume;
        static float? sfxVolume;

        /// <summary>0-1, multiplied into every music track.</summary>
        public static float MusicVolume
        {
            get => musicVolume ??= PlayerPrefs.GetFloat(MusicKey, 0.8f);
            set
            {
                musicVolume = Mathf.Clamp01(value);
                PlayerPrefs.SetFloat(MusicKey, musicVolume.Value);
            }
        }

        /// <summary>0-1, multiplied into every sound effect.</summary>
        public static float SfxVolume
        {
            get => sfxVolume ??= PlayerPrefs.GetFloat(SfxKey, 1f);
            set
            {
                sfxVolume = Mathf.Clamp01(value);
                PlayerPrefs.SetFloat(SfxKey, sfxVolume.Value);
            }
        }

        public static void Save() => PlayerPrefs.Save();
    }
}
