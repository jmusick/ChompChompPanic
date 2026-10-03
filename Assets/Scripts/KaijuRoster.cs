using System;
using System.Collections.Generic;
using UnityEngine;

namespace ChompChompPanic
{
    /// <summary>A kaiju the player can play as. The ones that aren't the player's pick can turn up as rivals.</summary>
    [Serializable]
    public class PlayableKaiju
    {
        public string Name = "Kaiju";
        [Tooltip("Sprite file prefix under Assets/Art (<prefix>_idle.png, ...), used by Chomp Chomp Panic > Import Art. "
            + "Sounds named sfx_<prefix>_<name>.wav fill the <Name>Sound fields (Chomp Chomp Panic > Import Audio). Also its save key.")]
        public string SpritePrefix = "kaiju";
        [Tooltip("Playable from the start. Otherwise it unlocks when the player survives a whole session: one locked kaiju per win, in roster order.")]
        public bool StartsUnlocked = true;
        public CharacterSprites Sprites;
        [Tooltip("Eating a person or soldier (the GameManager's chomp sound when empty)")]
        public AudioClip ChompSound;
        [Tooltip("Footsteps (the GameManager's stomp sound when empty)")]
        public AudioClip StompSound;
        [Tooltip("Played when it's picked on the title screen and when it arrives as a rival")]
        public AudioClip RoarSound;
        [Tooltip("Played over the lose jingle when it's chomped or taken down")]
        public AudioClip DeathSound;

        public bool HasSprites => Sprites != null && Sprites.IsValid;
    }

    /// <summary>
    /// Every playable kaiju, shared by the title screen (kaiju select) and the game. Keeps which ones are
    /// unlocked and which one the player picked in <see cref="PlayerPrefs"/>.
    /// </summary>
    [CreateAssetMenu(menuName = "Chomp Chomp Panic/Kaiju Roster", fileName = "KaijuRoster")]
    public class KaijuRoster : ScriptableObject
    {
        const string UnlockKeyPrefix = "kaijuUnlocked_";
        const string SelectedKey = "selectedKaiju";

        [Tooltip("The first unlocked one is the default pick")]
        public PlayableKaiju[] Kaiju = new PlayableKaiju[0];

        public bool IsUnlocked(PlayableKaiju kaiju) =>
            kaiju.StartsUnlocked || PlayerPrefs.GetInt(UnlockKeyPrefix + kaiju.SpritePrefix, 0) != 0;

        /// <summary>The kaiju the player can pick from, in roster order (only ones with sprites).</summary>
        public List<PlayableKaiju> Unlocked
        {
            get
            {
                var list = new List<PlayableKaiju>();
                foreach (var kaiju in Kaiju)
                    if (kaiju.HasSprites && IsUnlocked(kaiju))
                        list.Add(kaiju);
                return list;
            }
        }

        /// <summary>The player's pick, falling back to the first unlocked kaiju (null when none has sprites).</summary>
        public PlayableKaiju Selected
        {
            get
            {
                var unlocked = Unlocked;
                string prefix = PlayerPrefs.GetString(SelectedKey, "");
                return unlocked.Find(k => k.SpritePrefix == prefix) ?? (unlocked.Count > 0 ? unlocked[0] : null);
            }
            set
            {
                PlayerPrefs.SetString(SelectedKey, value.SpritePrefix);
                PlayerPrefs.Save();
            }
        }

        /// <summary>How many kaiju (with sprites) are still locked.</summary>
        public int LockedCount
        {
            get
            {
                int count = 0;
                foreach (var kaiju in Kaiju)
                    if (kaiju.HasSprites && !IsUnlocked(kaiju))
                        count++;
                return count;
            }
        }

        /// <summary>Reward for surviving a session: unlock the next locked kaiju in roster order. Returns it, or null when all are unlocked.</summary>
        public PlayableKaiju UnlockNext()
        {
            foreach (var kaiju in Kaiju)
            {
                if (!kaiju.HasSprites || IsUnlocked(kaiju))
                    continue;
                PlayerPrefs.SetInt(UnlockKeyPrefix + kaiju.SpritePrefix, 1);
                PlayerPrefs.Save();
                return kaiju;
            }
            return null;
        }
    }
}
