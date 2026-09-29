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
        public float IdleFps = 6f;
        public float WalkFps = 10f;
        public float ChompFps = 14f;
        public float DeathFps = 10f;

        public bool IsValid => Idle is { Length: > 0 } && Walk is { Length: > 0 };
    }

    /// <summary>
    /// Code-driven frame animation: loops idle/walk, plays chomp and death as one-shots.
    /// Uses unscaled time so the death animation still plays after the game pauses.
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

        void Awake()
        {
            sprite = GetComponent<SpriteRenderer>();
        }

        public void Init(CharacterSprites characterSprites)
        {
            sprites = characterSprites;
            Play(sprites.Idle, sprites.IdleFps, true);
        }

        public void SetMoving(Vector2 direction)
        {
            if (direction.x > 0.01f) sprite.flipX = false;
            else if (direction.x < -0.01f) sprite.flipX = true;

            bool isMoving = direction.sqrMagnitude > 0.0001f;
            if (isMoving == moving)
                return;
            moving = isMoving;
            if (loop && !dead)
                PlayLoop();
        }

        public void PlayChomp()
        {
            if (!dead && sprites.Chomp is { Length: > 0 })
                Play(sprites.Chomp, sprites.ChompFps, false);
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

            time += Time.unscaledDeltaTime;
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
            if (moving) Play(sprites.Walk, sprites.WalkFps, true);
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
