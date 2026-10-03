using System;
using UnityEngine;

namespace ChompChompPanic
{
    /// <summary>How a prey type gets around the city.</summary>
    public enum Movement
    {
        /// <summary>Streets and alleys, on the sidewalks or out on the road.</summary>
        Walk,
        /// <summary>Roads only, in the left-hand lane.</summary>
        Drive,
        /// <summary>Straight passes over the city, ignoring streets (planes).</summary>
        Fly,
        /// <summary>Anywhere, stomping through buildings (rival kaiju).</summary>
        Roam,
    }

    /// <summary>A gun, cannon or launcher. Fires at the kaiju whenever it is in range.</summary>
    [Serializable]
    public class Weapon
    {
        [Tooltip("Fires when the kaiju's edge is this close")]
        public float Range = 6f;
        [Tooltip("Seconds between bursts (random in this range)")]
        public Vector2 Cooldown = new(2f, 3f);
        [Tooltip("Shots per burst, and seconds between them")]
        public int BurstCount = 1;
        public float BurstInterval = 0.12f;
        [Tooltip("Health taken from the kaiju per hit")]
        public float Damage = 1f;
        public float ProjectileSpeed = 12f;
        [Tooltip("Random aim error either side, in degrees")]
        public float Spread = 5f;
        [Tooltip("Only fires at targets within this angle of the direction of travel (0 = any direction). For planes.")]
        public float ForwardArc;
        [Tooltip("Stands still to fire instead of moving (soldiers, tanks)")]
        public bool HoldsPosition;
        [Tooltip("Explodes on impact or at the end of its range (shells, rockets, missiles)")]
        public bool Explodes;
        [Tooltip("Projectile animation frames, pointing right")]
        public Sprite[] Projectile;
        [Tooltip("Sprite file name (no extension) under Assets/Art, used by Chomp Chomp Panic > Import Art")]
        public string ProjectileArt = "";
        [Tooltip("Projectile scale (2 = the people's 2x pixel scale)")]
        public float ProjectileScale = 2f;

        public bool IsArmed => Damage > 0f && Projectile is { Length: > 0 };
    }

    /// <summary>One kind of thing the kaiju can eat, from people up to fighter jets.</summary>
    [Serializable]
    public class PreyType
    {
        public string Name = "Prey";
        [Tooltip("Sprite file prefix under Assets/Art (person, soldier, car, ...), used by Chomp Chomp Panic > Import Art")]
        public string SpritePrefix = "";
        [Tooltip("One entry per look; a random one is picked for each spawn")]
        public CharacterSprites[] Variants;
        [Tooltip("How many are around at the start and at the end of the session")]
        public Vector2 Count = new(10f, 10f);
        [Tooltip("Shape of the ramp from start to end count: 1 = steady, under 1 = most of the change comes early")]
        public float CountRampExponent = 1f;
        [Tooltip("Collision radius. Doesn't change. The kaiju can eat it once the kaiju's radius is this x Eat Ratio.")]
        public float Radius = 0.45f;
        [Tooltip("Sets the sprite's scale: scale = 2 x radius / this. Equal to Radius draws at the people's 2x pixel scale.")]
        public float VisualDiameter = 0.45f;
        [Tooltip("Fraction of its area the kaiju gains when eating one")]
        public float GrowthEfficiency = 0.25f;
        [Tooltip("Health the kaiju gets back when eating one")]
        public float Heal;
        public Movement Movement = Movement.Walk;
        public float Speed = 1.2f;
        public float FleeSpeed = 3.2f;
        [Tooltip("Runs away when the kaiju's edge is this close and the kaiju is big enough to eat it (0 = never runs)")]
        public float FleeDistance = 2.5f;
        [Range(0f, 1f), Tooltip("Chance of stopping for a while at a junction instead of moving on")]
        public float IdleChance;
        [Tooltip("Blood splatters when eaten (anything with people in it)")]
        public bool Bleeds = true;
        [Tooltip("Camera shake when eaten, as a fraction of the camera's half-height")]
        public float CrunchShake;
        [Tooltip("Health taken from the kaiju when it runs into one while too small to eat it (0 = harmless)")]
        public float RamDamage;
        public Weapon Weapon = new() { Damage = 0f };

        public bool HasSprites => Variants is { Length: > 0 } && Variants[0] != null && Variants[0].IsValid;
    }

    /// <summary>Rare rival kaiju: bigger ones hunt the player, smaller ones run from it.</summary>
    [Serializable]
    public class RivalSettings
    {
        [Tooltip("Sprite file prefix under Assets/Art, used by Chomp Chomp Panic > Import Art")]
        public string SpritePrefix = "rival";
        public CharacterSprites[] Variants;
        [Tooltip("Seconds into the session before the first rival shows up")]
        public float FirstArrival = 60f;
        [Tooltip("Seconds between one rival leaving and the next arriving (random in this range)")]
        public Vector2 Interval = new(70f, 130f);
        [Tooltip("Rival radius as a multiple of the kaiju's start radius, at the start of the session (random in this range)")]
        public Vector2 StartSize = new(3.5f, 5f);
        [Tooltip("Rival radius as a multiple of the kaiju's start radius, at the end of the session (random in this range)")]
        public Vector2 EndSize = new(10f, 16f);
        [Tooltip("Rival speed relative to the slower of itself and the player at their sizes. Under 1 so the player can always outrun it (and catch it).")]
        public float SpeedFactor = 0.85f;
        [Tooltip("Hunts the kaiju when it is this many of its own radii away or closer")]
        public float SightRadii = 8f;
        [Tooltip("Seconds a rival stays before it wanders off")]
        public Vector2 StayTime = new(45f, 75f);
        [Tooltip("Fraction of its area the kaiju gains when eating one")]
        public float GrowthEfficiency = 0.5f;
        [Tooltip("Health the kaiju gets back when eating one (at or above Max Health = a full refill)")]
        public float Heal = 1000f;
        [Range(0f, 1f), Tooltip("Chance a rival is one of the other playable kaiju (unlocked or not) instead of a recolor")]
        public float OtherKaijuChance = 0.3f;

        public bool HasSprites => Variants is { Length: > 0 } && Variants[0] != null && Variants[0].IsValid;
    }
}
