using UnityEngine;

namespace ChompChompPanic
{
    /// <summary>Non-player blob that wanders in random directions.</summary>
    [RequireComponent(typeof(Blob))]
    public class EnemyBlob : MonoBehaviour
    {
        Vector2 velocity;
        float speed;
        float turnTimer;

        public Blob Blob { get; private set; }

        void Awake()
        {
            Blob = GetComponent<Blob>();
        }

        public void Init(float wanderSpeed)
        {
            speed = wanderSpeed;
            PickDirection();
        }

        void Update()
        {
            turnTimer -= Time.deltaTime;
            if (turnTimer <= 0f)
                PickDirection();

            transform.position += (Vector3)(velocity * Time.deltaTime);
        }

        void PickDirection()
        {
            velocity = Random.insideUnitCircle.normalized * (speed * Random.Range(0.4f, 1f));
            turnTimer = Random.Range(1.5f, 4f);
        }
    }
}
