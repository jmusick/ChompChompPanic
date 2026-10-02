using System;
using UnityEngine;
using Random = UnityEngine.Random;

namespace ChompChompPanic
{
    /// <summary>
    /// Everything that isn't the player: prey (people, soldiers, cars, jeeps, tanks, planes) and rival kaiju.
    ///
    /// Prey moves according to its <see cref="PreyType.Movement"/>. Given a street map, walkers and drivers
    /// stay on the streets: they move from junction to junction, and flee by turning around or taking the
    /// side street that leads away. Prey only flees a kaiju big enough to eat it. Drivers keep to roads (no alleys) in the left-hand lane. Planes make
    /// straight passes over the kaiju. Armed prey fires at the kaiju while it is in range; some stand
    /// still to do it.
    ///
    /// Rival kaiju roam freely: they hunt the player when they're big enough to eat it, run when the
    /// player is big enough to eat them, and wander off after a while.
    /// </summary>
    [RequireComponent(typeof(Blob))]
    public class EnemyBlob : MonoBehaviour
    {
        /// <summary>Planes draw over everything on the ground (but under projectiles and effects).</summary>
        const int FlyingOrder = short.MaxValue - 100;
        /// <summary>Plane shadows lie on the streets, over ground stains, under buildings.</summary>
        const int ShadowOrder = -840;

        Blob threat;
        SpriteAnimator animator;
        Movement movement;
        float speed;
        float fleeSpeed;
        float fleeDistance;
        float idleChance;

        // Free movement (no city, planes and rivals)
        Vector2 velocity;
        float turnTimer;

        // Street walking and driving
        StreetLayout streets;
        Vector2Int fromNode;
        Vector2Int toNode;
        /// <summary>Where across the street this walks, from -1 to 1 (sidewalks are near the ends).</summary>
        float lane;
        float strideFactor = 1f;
        float idleTimer;

        // Weapon
        Weapon weapon;
        Action<EnemyBlob, Vector2, Vector2> fire;
        float cooldown;
        int burstLeft;
        float burstTimer;

        /// <summary>How many times bigger the kaiju must be to eat this (prey), or this the kaiju (rival).</summary>
        float eatRatio;

        // Rival kaiju
        RivalSettings rival;
        float playerBaseSpeed;
        float referenceRadius;
        float stayTimer;

        public Blob Blob { get; private set; }
        /// <summary>The prey type, or null for a rival kaiju.</summary>
        public PreyType Type { get; private set; }
        /// <summary>Index of <see cref="Type"/> in the spawner's list.</summary>
        public int TypeIndex { get; private set; }
        public bool IsRival => rival != null;
        /// <summary>A rival that has had its time and is heading away.</summary>
        public bool IsLeaving => IsRival && stayTimer <= 0f;
        /// <summary>Session time before this prey can hurt the kaiju again by being run into.</summary>
        public float NextRamTime { get; set; }

        void Awake()
        {
            Blob = GetComponent<Blob>();
        }

        void Start()
        {
            animator = GetComponent<SpriteAnimator>();
        }

        /// <summary>
        /// Set up as prey that runs from (or fights) <paramref name="kaiju"/>. With <paramref name="streetLayout"/>,
        /// walkers and drivers move onto the nearest street and stay on streets.
        /// </summary>
        /// <param name="eatRatioToEat">How many times bigger the kaiju must be to eat this; it ignores a smaller kaiju.</param>
        /// <param name="onFire">Fires one shot: (shooter, muzzle position, direction).</param>
        public void InitPrey(int typeIndex, PreyType type, Blob kaiju, float eatRatioToEat, StreetLayout streetLayout,
            Action<EnemyBlob, Vector2, Vector2> onFire)
        {
            eatRatio = eatRatioToEat;
            TypeIndex = typeIndex;
            Type = type;
            threat = kaiju;
            movement = type.Movement;
            speed = type.Speed;
            fleeSpeed = type.FleeSpeed;
            fleeDistance = type.FleeDistance;
            idleChance = type.IdleChance;
            if (type.Weapon is { IsArmed: true })
            {
                weapon = type.Weapon;
                fire = onFire;
                cooldown = Random.Range(0.5f, weapon.Cooldown.y);
            }

            if (movement == Movement.Fly)
            {
                StartFlightPass();
                return;
            }

            PickDirection();
            streets = streetLayout;
            if (streets == null)
                return;
            var (point, a, b) = streets.SnapToStreet(transform.position, movement == Movement.Drive);
            (fromNode, toNode) = Random.value < 0.5f ? (a, b) : (b, a);
            PickLane();
            transform.position = point + Perpendicular(Direction) * LaneOffset;
        }

        /// <summary>Set up as a rival kaiju sizing up <paramref name="kaiju"/>.</summary>
        public void InitRival(RivalSettings settings, Blob kaiju, float eatRatioToEat, float baseSpeed, float startRadius)
        {
            rival = settings;
            threat = kaiju;
            movement = Movement.Roam;
            eatRatio = eatRatioToEat;
            playerBaseSpeed = baseSpeed;
            referenceRadius = startRadius;
            stayTimer = Random.Range(settings.StayTime.x, settings.StayTime.y);
            speed = RivalSpeed;
            PickDirection();
        }

        /// <summary>
        /// Moves like a kaiju its size, but never as fast as the player: a big rival hunting the player
        /// can be outrun, and a small one fleeing can be caught.
        /// </summary>
        float RivalSpeed => PlayerController.SpeedForRadius(Mathf.Min(Blob.Radius, threat.Radius), playerBaseSpeed, referenceRadius)
            * rival.SpeedFactor;

        void Update()
        {
            if (threat == null)
                return;
            if (IsRival)
            {
                RivalUpdate();
                return;
            }
            if (movement == Movement.Fly)
            {
                transform.position += (Vector3)(velocity * Time.deltaTime);
                UpdateWeapon(threat.transform.position - transform.position);
                return;
            }

            Vector2 away = transform.position - threat.transform.position;
            float edgeDistance = away.magnitude - threat.Radius;
            bool threatened = threat.Radius >= Blob.Radius * eatRatio;
            bool fleeing = threatened && fleeDistance > 0f && edgeDistance < fleeDistance;
            bool engaged = weapon != null && edgeDistance <= weapon.Range;
            bool holding = engaged && weapon.HoldsPosition && !fleeing;

            Vector2 move = holding ? Vector2.zero : streets != null ? StreetMove(fleeing, away) : FreeMove(fleeing, away);
            transform.position += (Vector3)(move * Time.deltaTime);

            if (animator != null)
            {
                if (holding) animator.StandFacing(-away);
                else animator.SetMoving(move);
            }
            if (weapon != null)
                UpdateWeapon(-away);
        }

        // ------------------------------------------------------------------ weapons

        void UpdateWeapon(Vector2 toTarget)
        {
            cooldown -= Time.deltaTime;
            if (burstLeft > 0)
            {
                burstTimer -= Time.deltaTime;
                if (burstTimer <= 0f)
                {
                    Shoot(toTarget);
                    burstLeft--;
                    burstTimer = weapon.BurstInterval;
                }
                return;
            }

            if (cooldown > 0f || toTarget.magnitude - threat.Radius > weapon.Range)
                return;
            if (weapon.ForwardArc > 0f && Vector2.Angle(velocity, toTarget) > weapon.ForwardArc)
                return;
            burstLeft = Mathf.Max(1, weapon.BurstCount);
            burstTimer = 0f;
            cooldown = Random.Range(weapon.Cooldown.x, weapon.Cooldown.y);
            if (animator != null)
                animator.PlayAttack();
        }

        void Shoot(Vector2 toTarget)
        {
            var direction = (Vector2)(Quaternion.Euler(0f, 0f, Random.Range(-weapon.Spread, weapon.Spread)) * toTarget.normalized);
            // From the front of the sprite, a little above its middle (where the guns are).
            var muzzle = (Vector2)transform.position + direction * (Blob.Radius * 0.6f);
            if (movement != Movement.Fly)
                muzzle += Vector2.up * (Blob.Radius * 0.15f);
            fire(this, muzzle, direction);
        }

        // ------------------------------------------------------------------ flying

        /// <summary>Head for a point near the kaiju and keep going straight until far past it.</summary>
        void StartFlightPass()
        {
            var aim = (Vector2)threat.transform.position + Random.insideUnitCircle * (threat.Radius * 2f);
            velocity = (aim - (Vector2)transform.position).normalized * speed;
            transform.rotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(velocity.y, velocity.x) * Mathf.Rad2Deg);
            Blob.Sprite.sortingOrder = FlyingOrder;
            FlyingShadow.Attach(Blob.Sprite, new Vector2(0.3f, -0.6f) * Blob.Radius, ShadowOrder);
        }

        // ------------------------------------------------------------------ rival kaiju

        void RivalUpdate()
        {
            Vector2 toPlayer = threat.transform.position - transform.position;
            float distance = toPlayer.magnitude;
            bool inSight = distance < rival.SightRadii * Blob.Radius;
            bool canEatPlayer = Blob.Radius >= threat.Radius * eatRatio;
            bool isEdible = threat.Radius >= Blob.Radius * eatRatio;
            stayTimer -= Time.deltaTime;
            speed = RivalSpeed;

            Vector2 move;
            if (IsLeaving || (isEdible && inSight))
                move = -toPlayer.normalized * speed;
            else if (canEatPlayer && inSight)
                move = toPlayer.normalized * speed;
            else
            {
                // Prowl: wander, drifting towards the player so it stays around.
                turnTimer -= Time.deltaTime;
                if (turnTimer <= 0f)
                {
                    velocity = (Random.insideUnitCircle.normalized + toPlayer.normalized * 0.8f).normalized * (speed * 0.6f);
                    turnTimer = Random.Range(1.5f, 3f);
                }
                move = velocity;
            }
            transform.position += (Vector3)(move * Time.deltaTime);
            if (animator != null)
                animator.SetMoving(move);
        }

        // ------------------------------------------------------------------ free movement

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

        // ------------------------------------------------------------------ street walking and driving

        Vector2 Direction => (streets.NodePosition(toNode) - streets.NodePosition(fromNode)).normalized;

        float LaneOffset => lane * StreetLayout.HalfWidth(streets.Arm(fromNode, toNode - fromNode));

        /// <summary>How far ahead along the street walkers and drivers aim, so they merge into their lane quickly.</summary>
        const float LaneMergeDistance = 2f;

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

            // Follow the lane line, merging onto it within a short distance (rather than drifting
            // across over the whole street, which shows on long diagonal avenues).
            var direction = Direction;
            var offset = Perpendicular(direction) * LaneOffset;
            var start = streets.NodePosition(fromNode) + offset;
            var target = streets.NodePosition(toNode) + offset;
            var position = (Vector2)transform.position;
            float length = Vector2.Distance(start, target);
            float along = Vector2.Dot(position - start, direction);
            var aim = start + direction * Mathf.Min(length, along + LaneMergeDistance);
            var toTarget = target - position;
            float moveSpeed = fleeing ? fleeSpeed : speed * strideFactor;
            float step = moveSpeed * Time.deltaTime;
            if (step > 0f && toTarget.magnitude <= step)
            {
                transform.position = target;
                ArriveAtJunction(fleeing, away);
                return Vector2.zero;
            }
            return (aim - position).normalized * moveSpeed;
        }

        /// <summary>Pick the next street: any way but back when wandering, the way that leads furthest from the kaiju when fleeing.</summary>
        void ArriveAtJunction(bool fleeing, Vector2 away)
        {
            var node = toNode;
            var position = streets.NodePosition(node);
            Vector2Int best = fromNode;
            float bestScore = float.MinValue;
            foreach (var direction in StreetLayout.AllDirections)
            {
                var arm = streets.Arm(node, direction);
                if (movement == Movement.Drive ? !StreetLayout.IsDrivable(arm) : !StreetLayout.IsWalkable(arm))
                    continue;
                var next = StreetLayout.Neighbor(node, direction);
                float score = fleeing ? Vector2.Dot((streets.NodePosition(next) - position).normalized, away.normalized) : Random.value;
                if (next == fromNode)
                    score -= fleeing ? 0.5f : 2f;  // turning back is a last resort (dead ends)
                if (score > bestScore)
                {
                    bestScore = score;
                    best = next;
                }
            }

            fromNode = node;
            toNode = best;
            if (Random.value < 0.3f)
                PickLane();
            strideFactor = Random.Range(0.6f, 1f);
            if (!fleeing && Random.value < idleChance)
                idleTimer = Random.Range(1f, 3f);
        }

        /// <summary>Walkers: mostly on the sidewalks, sometimes out on the road. Drivers: the left-hand lane.</summary>
        void PickLane()
        {
            if (movement == Movement.Drive)
            {
                // Perpendicular() points to the left of the direction of travel.
                lane = StreetLayout.CarLaneOffset / StreetLayout.RoadHalfWidth;
                return;
            }

            float side = Random.value < 0.5f ? -1f : 1f;
            lane = Random.value < 0.75f ? side * Random.Range(0.8f, 0.95f) : Random.Range(-0.6f, 0.6f);
        }
    }
}
