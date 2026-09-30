using UnityEngine;

namespace ChompChompPanic
{
    /// <summary>
    /// A dark copy of a flying object's sprite, cast on the ground below and behind it, so planes
    /// read as being up in the air. Sits over the streets but under buildings' rooftops and characters.
    /// </summary>
    [RequireComponent(typeof(SpriteRenderer))]
    public class FlyingShadow : MonoBehaviour
    {
        static readonly Color ShadowColor = new(0f, 0f, 0.05f, 0.4f);

        SpriteRenderer source;
        SpriteRenderer shadow;
        Vector2 offset;

        /// <param name="groundOffset">Where the shadow falls relative to the object, in world units.</param>
        public static void Attach(SpriteRenderer caster, Vector2 groundOffset, int sortingOrder)
        {
            var go = new GameObject("Shadow");
            go.transform.SetParent(caster.transform, false);
            var shadow = go.AddComponent<FlyingShadow>();
            shadow.source = caster;
            shadow.offset = groundOffset;
            shadow.shadow.color = ShadowColor;
            shadow.shadow.sortingOrder = sortingOrder;
        }

        void Awake()
        {
            shadow = GetComponent<SpriteRenderer>();
        }

        void LateUpdate()
        {
            shadow.sprite = source.sprite;
            shadow.flipX = source.flipX;
            transform.position = source.transform.position + (Vector3)offset;
        }
    }
}
