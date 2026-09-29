using UnityEngine;

namespace ChompChompPanic
{
    /// <summary>
    /// Non-player blob that wanders in random directions. People also flee from the kaiju
    /// when it gets close.
    /// </summary>
    [RequireComponent(typeof(Blob))]
    public class EnemyBlob : MonoBehaviour
    {
        Vector2 velocity;
        float speed;
        float turnTimer;
        float idleChance;
        Blob threat;
        float fleeSpeed;
        float fleeDistance;
        SpriteAnimator animator;

        public Blob Blob { get; private set; }
        public bool IsPerson { get; private set; }

        void Awake()
        {
            Blob = GetComponent<Blob>();
        }

        void Start()
        {
            // Optional: people have one, plain circles don't.
            animator = GetComponent<SpriteAnimator>();
        }

        public void Init(float wanderSpeed)
        {
            speed = wanderSpeed;
            PickDirection();
        }

        /// <summary>Make this a person: sometimes stands still, and runs from <paramref name="kaiju"/>.</summary>
        public void InitPerson(Blob kaiju, float runSpeed, float panicDistance, float standStillChance)
        {
            IsPerson = true;
            threat = kaiju;
            fleeSpeed = runSpeed;
            fleeDistance = panicDistance;
            idleChance = standStillChance;
            PickDirection();
        }

        void Update()
        {
            Vector2 move = velocity;
            bool fleeing = false;

            if (threat != null)
            {
                Vector2 away = transform.position - threat.transform.position;
                float panicRange = fleeDistance + threat.Radius;
                if (away.sqrMagnitude < panicRange * panicRange)
                {
                    move = away.normalized * fleeSpeed;
                    fleeing = true;
                }
            }

            if (!fleeing)
            {
                turnTimer -= Time.deltaTime;
                if (turnTimer <= 0f)
                    PickDirection();
                move = velocity;
            }

            transform.position += (Vector3)(move * Time.deltaTime);

            if (animator != null)
                animator.SetMoving(move);
        }

        void PickDirection()
        {
            velocity = Random.value < idleChance
                ? Vector2.zero
                : Random.insideUnitCircle.normalized * (speed * Random.Range(0.4f, 1f));
            turnTimer = Random.Range(1.5f, 4f);
        }
    }
}
