using System;
using UnityEngine;

namespace ChompChompPanic
{
    /// <summary>Frames and playback speeds for a character's animations.</summary>
    [Serializable]
    public class CharacterSprites
    {
        public Sprite[] Idle;
        public Sprite[] Walk;
        public Sprite[] Chomp;
        public Sprite[] Death;
        [Tooltip("Optional: shown instead of Walk while moving up the screen (seen from behind)")]
        public Sprite[] WalkUp;
        [Tooltip("Optional: shown instead of Walk while moving down the screen (seen from the front)")]
        public Sprite[] WalkDown;
        [Tooltip("Optional: played once each time it fires (soldiers)")]
        public Sprite[] Attack;
        public float IdleFps = 6f;
        public float WalkFps = 10f;
        public float ChompFps = 14f;
        public float DeathFps = 10f;
        public float AttackFps = 14f;

        public bool IsValid => Idle is { Length: > 0 } && Walk is { Length: > 0 };

        public bool HasVerticalViews => WalkUp is { Length: > 0 } && WalkDown is { Length: > 0 };
    }

    /// <summary>
    /// Code-driven frame animation: loops idle/walk, plays chomp, attack and death as one-shots.
    /// Freezes with the game when it pauses, except the death animation, which plays on over the game-over screen.
    /// Characters with up/down views (cars) turn to face the way they move; others flip left/right.
    /// </summary>
    [RequireComponent(typeof(SpriteRenderer))]
    public class SpriteAnimator : MonoBehaviour
    {
        SpriteRenderer sprite;
        CharacterSprites sprites;
        Sprite[] frames;
        float fps;
        float time;
        bool loop;
        bool moving;
        bool dead;
        /// <summary>-1 facing down the screen, 0 sideways, 1 up the screen.</summary>
        int facing;

        void Awake()
        {
            sprite = GetComponent<SpriteRenderer>();
        }

        public void Init(CharacterSprites characterSprites)
        {
            sprites = characterSprites;
            Play(sprites.Idle, sprites.IdleFps, true);
        }

        /// <summary>Walk (or idle, for a zero direction) and face the way it's going.</summary>
        public void SetMoving(Vector2 direction)
        {
            bool isMoving = direction.sqrMagnitude > 0.0001f;
            Turn(isMoving ? direction : Vector2.zero, isMoving);
        }

        /// <summary>Stand still, facing <paramref name="direction"/> (e.g. aiming at a target).</summary>
        public void StandFacing(Vector2 direction)
        {
            Turn(direction, false);
        }

        void Turn(Vector2 direction, bool isMoving)
        {
            int newFacing = facing;
            if (direction != Vector2.zero && sprites.HasVerticalViews)
                newFacing = Mathf.Abs(direction.y) > Mathf.Abs(direction.x) ? (direction.y > 0f ? 1 : -1) : 0;

            // Front and back views are never mirrored.
            if (newFacing != 0) sprite.flipX = false;
            else if (direction.x > 0.01f) sprite.flipX = false;
            else if (direction.x < -0.01f) sprite.flipX = true;

            if (isMoving == moving && newFacing == facing)
                return;
            moving = isMoving;
            facing = newFacing;
            if (loop && !dead)
                PlayLoop();
        }

        public void PlayChomp()
        {
            if (!dead && sprites.Chomp is { Length: > 0 })
                Play(sprites.Chomp, sprites.ChompFps, false);
        }

        public void PlayAttack()
        {
            if (!dead && sprites.Attack is { Length: > 0 })
                Play(sprites.Attack, sprites.AttackFps, false);
        }

        public void PlayDeath()
        {
            dead = true;
            if (sprites.Death is { Length: > 0 })
                Play(sprites.Death, sprites.DeathFps, false);
        }

        void Update()
        {
            if (frames == null)
                return;

            time += dead ? Time.unscaledDeltaTime : Time.deltaTime;
            int frame = Mathf.FloorToInt(time * fps);

            if (frame >= frames.Length)
            {
                if (loop)
                {
                    frame %= frames.Length;
                }
                else if (dead)
                {
                    // Hold the last death frame.
                    frame = frames.Length - 1;
                }
                else
                {
                    // One-shot finished: go back to idle/walk.
                    PlayLoop();
                    return;
                }
            }

            sprite.sprite = frames[frame];
        }

        void PlayLoop()
        {
            var walk = facing > 0 ? sprites.WalkUp : facing < 0 ? sprites.WalkDown : sprites.Walk;
            if (moving) Play(walk, sprites.WalkFps, true);
            // Standing still facing up or down: hold the first frame of that view.
            else if (facing != 0) Play(walk, 0f, true);
            else Play(sprites.Idle, sprites.IdleFps, true);
        }

        void Play(Sprite[] clip, float framesPerSecond, bool looping)
        {
            frames = clip;
            fps = framesPerSecond;
            loop = looping;
            time = 0f;
            sprite.sprite = frames[0];
        }
    }
}
