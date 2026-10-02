using UnityEngine;

namespace ChompChompPanic
{
    /// <summary>A circle with a radius. Shared by the player and every other blob.</summary>
    [RequireComponent(typeof(SpriteRenderer))]
    public class Blob : MonoBehaviour
    {
        /// <summary>Gap between neighbouring blobs' depths; small enough to be invisible to the camera.</summary>
        const float DepthStep = 0.0001f;
        const int DepthSlots = 10000;

        static int nextDepthSlot;

        float radius = 0.5f;

        /// <summary>
        /// A z unique to this blob. Blobs of the same size share a sorting order, and with equal z Unity
        /// swaps their draw order from frame to frame (flicker). A fixed z gives a stable tie-break.
        /// </summary>
        float depth;

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
            depth = nextDepthSlot * DepthStep;
            nextDepthSlot = (nextDepthSlot + 1) % DepthSlots;
        }

        void LateUpdate()
        {
            // Movement code assigns 2D positions, which resets z; put our depth back before rendering.
            var position = transform.position;
            if (position.z != depth)
                transform.position = new Vector3(position.x, position.y, depth);
        }

        /// <summary>Absorb another blob, adding (a fraction of) its area to ours.</summary>
        public void Absorb(float eatenRadius, float efficiency)
        {
            Radius = Mathf.Sqrt(radius * radius + eatenRadius * eatenRadius * efficiency);
        }
    }
}
