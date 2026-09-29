using UnityEngine;

namespace ChompChompPanic
{
    /// <summary>One-shot frame animation that removes itself when done (e.g. the poof of a smashed building).</summary>
    [RequireComponent(typeof(SpriteRenderer))]
    public class DustPuff : MonoBehaviour
    {
        const float Fps = 14f;

        Sprite[] frames;
        SpriteRenderer sprite;
        float time;

        public static void Spawn(Sprite[] frames, Vector2 position, float scale, int sortingOrder)
        {
            var go = new GameObject("Dust");
            go.transform.position = position;
            go.transform.localScale = Vector3.one * scale;
            var puff = go.AddComponent<DustPuff>();
            puff.frames = frames;
            puff.sprite.sprite = frames[0];
            puff.sprite.sortingOrder = sortingOrder;
        }

        void Awake()
        {
            sprite = GetComponent<SpriteRenderer>();
        }

        void Update()
        {
            time += Time.deltaTime;
            int frame = Mathf.FloorToInt(time * Fps);
            if (frame >= frames.Length)
            {
                Destroy(gameObject);
                return;
            }
            sprite.sprite = frames[frame];
        }
    }
}
