using UnityEngine;

namespace ChompChompPanic
{
    /// <summary>A circle with a radius. Shared by the player and every other blob.</summary>
    [RequireComponent(typeof(SpriteRenderer))]
    public class Blob : MonoBehaviour
    {
        float radius = 0.5f;

        public SpriteRenderer Sprite { get; private set; }

        /// <summary>
        /// World-space diameter of the sprite's body at scale 1. The circle sprite is 1 unit across;
        /// character sprites may differ. Set before <see cref="Radius"/>.
        /// </summary>
        public float VisualDiameter { get; set; } = 1f;

        public float Radius
        {
            get => radius;
            set
            {
                radius = Mathf.Max(0.05f, value);
                transform.localScale = Vector3.one * (radius * 2f / VisualDiameter);
                // Bigger blobs draw on top of smaller ones.
                Sprite.sortingOrder = Mathf.Min(Mathf.RoundToInt(radius * 100f), short.MaxValue);
            }
        }

        void Awake()
        {
            Sprite = GetComponent<SpriteRenderer>();
            Sprite.sprite = Sprites.Circle;
        }

        /// <summary>Absorb another blob, adding (a fraction of) its area to ours.</summary>
        public void Absorb(float eatenRadius, float efficiency)
        {
            Radius = Mathf.Sqrt(radius * radius + eatenRadius * eatenRadius * efficiency);
        }
    }
}
