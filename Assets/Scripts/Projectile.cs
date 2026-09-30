using System;
using UnityEngine;

namespace ChompChompPanic
{
    /// <summary>
    /// A bullet, shell, rocket or missile flying in a straight line at the kaiju. It hits once its
    /// center is inside the target, and otherwise flies out its weapon's range and is gone
    /// (explosive ones blow up there). The owner decides what a hit or miss does.
    /// </summary>
    [RequireComponent(typeof(SpriteRenderer))]
    public class Projectile : MonoBehaviour
    {
        const float Fps = 12f;
        /// <summary>Hits land a little inside the target's circle, where the body is.</summary>
        const float HitRadiusFactor = 0.8f;

        Sprite[] frames;
        SpriteRenderer sprite;
        Vector2 velocity;
        Blob target;
        float lifetime;
        float time;
        Action<Projectile, bool> onImpact;

        public float Damage { get; private set; }
        public bool Explodes { get; private set; }

        /// <param name="onImpact">Called once, with true for a hit on the target and false for a miss.</param>
        public static void Fire(Weapon weapon, Vector2 from, Vector2 direction, float range, Blob target,
            Action<Projectile, bool> onImpact)
        {
            var go = new GameObject("Projectile");
            go.transform.position = from;
            go.transform.localScale = Vector3.one * weapon.ProjectileScale;
            go.transform.rotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg);
            var projectile = go.AddComponent<Projectile>();
            projectile.frames = weapon.Projectile;
            projectile.sprite.sprite = weapon.Projectile[0];
            projectile.sprite.sortingOrder = short.MaxValue - 20;  // over everything but the blood spurt
            projectile.velocity = direction.normalized * weapon.ProjectileSpeed;
            projectile.lifetime = range / weapon.ProjectileSpeed;
            projectile.target = target;
            projectile.Damage = weapon.Damage;
            projectile.Explodes = weapon.Explodes;
            projectile.onImpact = onImpact;
        }

        void Awake()
        {
            sprite = GetComponent<SpriteRenderer>();
        }

        void Update()
        {
            time += Time.deltaTime;
            transform.position += (Vector3)(velocity * Time.deltaTime);
            if (frames.Length > 1)
                sprite.sprite = frames[Mathf.FloorToInt(time * Fps) % frames.Length];

            if (target != null)
            {
                float hitRadius = target.Radius * HitRadiusFactor;
                if (((Vector2)(transform.position - target.transform.position)).sqrMagnitude < hitRadius * hitRadius)
                {
                    Impact(true);
                    return;
                }
            }
            if (time >= lifetime)
                Impact(false);
        }

        void Impact(bool hit)
        {
            onImpact?.Invoke(this, hit);
            Destroy(gameObject);
        }
    }
}
