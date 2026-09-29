using UnityEngine;

namespace ChompChompPanic
{
    /// <summary>A sprite left on the ground that stays for a while, fades out and removes itself.</summary>
    [RequireComponent(typeof(SpriteRenderer))]
    public class GroundStain : MonoBehaviour
    {
        SpriteRenderer sprite;
        float holdTime;
        float fadeTime;
        float time;

        public static void Spawn(Sprite stain, Vector2 position, float scale, int sortingOrder, float hold, float fade)
        {
            var go = new GameObject("Stain");
            go.transform.position = position;
            go.transform.localScale = Vector3.one * scale;
            var stainComponent = go.AddComponent<GroundStain>();
            stainComponent.holdTime = hold;
            stainComponent.fadeTime = fade;
            var renderer = stainComponent.sprite;
            renderer.sprite = stain;
            renderer.sortingOrder = sortingOrder;
            // Mirror at random so repeated stains don't look stamped (no rotation: it would break the pixel grid).
            renderer.flipX = Random.value < 0.5f;
            renderer.flipY = Random.value < 0.5f;
        }

        void Awake()
        {
            sprite = GetComponent<SpriteRenderer>();
        }

        void Update()
        {
            time += Time.deltaTime;
            if (time < holdTime)
                return;
            float fade = (time - holdTime) / fadeTime;
            if (fade >= 1f)
            {
                Destroy(gameObject);
                return;
            }
            var color = sprite.color;
            color.a = 1f - fade;
            sprite.color = color;
        }
    }
}
