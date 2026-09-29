using UnityEngine;

namespace ChompChompPanic
{
    /// <summary>
    /// Non-player blob that wanders in random directions. People also flee from the kaiju
    /// when it gets close. Given a street map, people stay on the streets: they walk from junction
    /// to junction, and flee by turning around or taking the side street that leads away.
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

        // Street walking (people only, when there is a city)
        StreetLayout streets;
        Vector2Int fromNode;
        Vector2Int toNode;
        /// <summary>Where across the street this person walks, from -1 to 1 (sidewalks are near the ends).</summary>
        float lane;
        float strideFactor = 1f;
        float idleTimer;

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

        /// <summary>
        /// Make this a person: sometimes stands still, and runs from <paramref name="kaiju"/>.
        /// With <paramref name="streetLayout"/>, the person moves onto the nearest street and stays on streets.
        /// </summary>
        public void InitPerson(Blob kaiju, float runSpeed, float panicDistance, float standStillChance, StreetLayout streetLayout = null)
        {
            IsPerson = true;
            threat = kaiju;
            fleeSpeed = runSpeed;
            fleeDistance = panicDistance;
            idleChance = standStillChance;
            PickDirection();

            streets = streetLayout;
            if (streets == null)
                return;
            var (point, a, b) = streets.SnapToStreet(transform.position);
            (fromNode, toNode) = Random.value < 0.5f ? (a, b) : (b, a);
            PickLane();
            transform.position = point + Perpendicular(Direction) * LaneOffset;
        }

        void Update()
        {
            Vector2 away = Vector2.zero;
            bool fleeing = false;
            if (threat != null)
            {
                away = transform.position - threat.transform.position;
                float panicRange = fleeDistance + threat.Radius;
                fleeing = away.sqrMagnitude < panicRange * panicRange;
            }

            Vector2 move = streets != null ? StreetMove(fleeing, away) : FreeMove(fleeing, away);
            transform.position += (Vector3)(move * Time.deltaTime);

            if (animator != null)
                animator.SetMoving(move);
        }

        Vector2 FreeMove(bool fleeing, Vector2 away)
        {
            if (fleeing)
                return away.normalized * fleeSpeed;

            turnTimer -= Time.deltaTime;
            if (turnTimer <= 0f)
                PickDirection();
            return velocity;
        }

        void PickDirection()
        {
            velocity = Random.value < idleChance
                ? Vector2.zero
                : Random.insideUnitCircle.normalized * (speed * Random.Range(0.4f, 1f));
            turnTimer = Random.Range(1.5f, 4f);
        }

        // ------------------------------------------------------------------ street walking

        Vector2 Direction => (Vector2)(toNode - fromNode);

        float LaneOffset => lane * StreetLayout.HalfWidth(streets.Arm(fromNode, toNode - fromNode));

        static Vector2 Perpendicular(Vector2 direction) => new(-direction.y, direction.x);

        Vector2 StreetMove(bool fleeing, Vector2 away)
        {
            if (fleeing)
            {
                idleTimer = 0f;
                // Heading towards the kaiju? Turn around on the spot.
                if (Vector2.Dot(Direction, away.normalized) < -0.2f)
                    (fromNode, toNode) = (toNode, fromNode);
            }
            else if (idleTimer > 0f)
            {
                idleTimer -= Time.deltaTime;
                return Vector2.zero;
            }

            var target = StreetLayout.NodePosition(toNode) + Perpendicular(Direction) * LaneOffset;
            var toTarget = target - (Vector2)transform.position;
            float moveSpeed = fleeing ? fleeSpeed : speed * strideFactor;
            float step = moveSpeed * Time.deltaTime;
            if (step > 0f && toTarget.magnitude <= step)
            {
                transform.position = target;
                ArriveAtJunction(fleeing, away);
                return Vector2.zero;
            }
            return toTarget.normalized * moveSpeed;
        }

        /// <summary>Pick the next street: any way but back when wandering, the way that leads furthest from the kaiju when fleeing.</summary>
        void ArriveAtJunction(bool fleeing, Vector2 away)
        {
            var node = toNode;
            var back = fromNode - toNode;
            Vector2Int best = back;
            float bestScore = float.MinValue;
            foreach (var direction in StreetLayout.Directions)
            {
                if (!StreetLayout.IsWalkable(streets.Arm(node, direction)))
                    continue;
                float score = fleeing ? Vector2.Dot(direction, away.normalized) : Random.value;
                if (direction == back)
                    score -= fleeing ? 0.5f : 2f;  // turning back is a last resort (dead ends)
                if (score > bestScore)
                {
                    bestScore = score;
                    best = direction;
                }
            }

            fromNode = node;
            toNode = node + best;
            if (Random.value < 0.3f)
                PickLane();
            strideFactor = Random.Range(0.6f, 1f);
            if (!fleeing && Random.value < idleChance)
                idleTimer = Random.Range(1f, 3f);
        }

        /// <summary>Mostly on the sidewalks, sometimes out on the road.</summary>
        void PickLane()
        {
            float side = Random.value < 0.5f ? -1f : 1f;
            lane = Random.value < 0.75f ? side * Random.Range(0.8f, 0.95f) : Random.Range(-0.6f, 0.6f);
        }
    }
}
