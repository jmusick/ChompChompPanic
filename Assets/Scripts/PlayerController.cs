using UnityEngine;
using UnityEngine.InputSystem;

namespace ChompChompPanic
{
    /// <summary>Moves the player blob with WASD / arrow keys / gamepad left stick.</summary>
    [RequireComponent(typeof(Blob))]
    public class PlayerController : MonoBehaviour
    {
        public float BaseSpeed = 5f;
        public float ReferenceRadius = 0.5f;

        Blob blob;

        void Awake()
        {
            blob = GetComponent<Blob>();
        }

        void Update()
        {
            float speed = SpeedForRadius(blob.Radius, BaseSpeed, ReferenceRadius);
            transform.position += (Vector3)(ReadMove() * (speed * Time.deltaTime));
        }

        /// <summary>
        /// World-space speed grows with size, but slower than the camera zooms out,
        /// so bigger blobs feel a bit more sluggish on screen.
        /// </summary>
        public static float SpeedForRadius(float radius, float baseSpeed, float referenceRadius)
        {
            return baseSpeed * Mathf.Sqrt(radius / referenceRadius);
        }

        static Vector2 ReadMove()
        {
            Vector2 move = Vector2.zero;

            var kb = Keyboard.current;
            if (kb != null)
            {
                if (kb.aKey.isPressed || kb.leftArrowKey.isPressed) move.x -= 1f;
                if (kb.dKey.isPressed || kb.rightArrowKey.isPressed) move.x += 1f;
                if (kb.sKey.isPressed || kb.downArrowKey.isPressed) move.y -= 1f;
                if (kb.wKey.isPressed || kb.upArrowKey.isPressed) move.y += 1f;
            }

            var pad = Gamepad.current;
            if (pad != null)
                move += pad.leftStick.ReadValue();

            return Vector2.ClampMagnitude(move, 1f);
        }
    }
}
