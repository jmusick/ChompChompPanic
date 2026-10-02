using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace ChompChompPanic
{
    /// <summary>
    /// Runs a session: spawns the player, prey and rival kaiju, resolves eating, gunfire and building
    /// smashing, follows with the camera, tracks the timer and health, plays the map's music, and draws a minimal HUD.
    /// Esc / Start pauses the game with a menu (resume, options, title screen, quit).
    /// </summary>
    public class GameManager : MonoBehaviour
    {
        enum State { Playing, Won, Lost }

        [Header("Session")]
        [SerializeField, Tooltip("Seconds the player must survive")]
        float sessionLength = 10f * 60f;

        [Header("Player")]
        [SerializeField] float playerStartRadius = 0.5f;
        [SerializeField] float playerBaseSpeed = 5f;
        [SerializeField, Tooltip("Player tint when no character sprites are assigned")]
        Color playerColor = new(0.35f, 0.75f, 1f);
        [SerializeField] CharacterSprites playerSprites;
        [SerializeField] float maxHealth = 100f;
        [SerializeField, Tooltip("Player tint when hit, and when healed by eating a human")]
        Color hurtColor = new(1f, 0.3f, 0.3f);
        [SerializeField] Color healColor = new(0.55f, 1f, 0.55f);

        [Header("Eating")]
        [SerializeField, Tooltip("How many times bigger (by radius) something must be to eat another")]
        float eatRatio = 1.05f;

        [Header("Prey (smallest first). Sprites: Chomp Chomp Panic > Import Art")]
        [SerializeField] PreyType[] preyTypes = DefaultPreyTypes();

        [Header("Rival kaiju")]
        [SerializeField] RivalSettings rivals = new();

        [Header("Weapon effects (from ArtSource/Military/build_military.py)")]
        [SerializeField, Tooltip("Flash at the muzzle of every shot")]
        Sprite[] muzzleFlash;
        [SerializeField, Tooltip("Shells, rockets and missiles blow up with this")]
        Sprite[] explosion;
        [SerializeField, Tooltip("World size of an explosion")]
        float explosionSize = 1.6f;

        [Header("Camera")]
        [SerializeField] float cameraBaseSize = 6f;
        [SerializeField, Tooltip("How much the camera zooms out as the player grows (0 = never, 1 = player stays the same size on screen)")]
        float cameraZoomExponent = 0.6f;
        [SerializeField] Color backgroundColor = new(0.07f, 0.08f, 0.11f);
        [SerializeField, Tooltip("Camera shake per smashed building, as a fraction of the camera's half-height")]
        float smashShake = 0.015f;
        [SerializeField, Tooltip("Camera shake when an explosive shot hits the kaiju")]
        float explosionShake = 0.04f;

        [Header("City (from ArtSource/City/build_city.py; a plain grid is drawn without it)")]
        [SerializeField] Texture2D cityAtlas;
        [SerializeField, Tooltip("city_atlas.json: where each sprite sits in the atlas")]
        TextAsset cityAtlasData;

        [Header("Blood (from ArtSource/Effects/build_effects.py; none is shown without it)")]
        [SerializeField, Tooltip("Spurt played on top of the kaiju when it eats someone")]
        Sprite[] bloodBurst;
        [SerializeField, Tooltip("Ground splats left behind; one is picked at random")]
        Sprite[] bloodStains;
        [SerializeField, Tooltip("World size of the blood spurt at the kaiju's start size (grows with the square root of its size)")]
        float bloodBurstSize = 0.8f;
        [SerializeField, Tooltip("Seconds a stain stays before it starts to fade, and how long the fade takes")]
        Vector2 bloodStainHoldAndFade = new(6f, 2f);

        [Header("Sound (from ArtSource/Audio/build_sfx.py; assign with Chomp Chomp Panic > Import Audio; silent without it)")]
        [SerializeField, Range(0f, 1f)] float soundVolume = 0.8f;
        [SerializeField, Tooltip("Eating a person or soldier")]
        AudioClip chompSound;
        [SerializeField, Tooltip("Eating a vehicle or a rival kaiju")]
        AudioClip crunchSound;
        [SerializeField, Tooltip("Rifle and machine-gun shots")]
        AudioClip gunshotSound;
        [SerializeField, Tooltip("Rockets, tank shells and missiles being fired")]
        AudioClip launchSound;
        [SerializeField] AudioClip explosionSound;
        [SerializeField, Tooltip("A bullet hitting the kaiju")]
        AudioClip hitSound;
        [SerializeField, Tooltip("A building being smashed; a different variant is picked each time")]
        AudioClip[] smashSounds;
        [SerializeField, Tooltip("Seconds between smash sounds (random in this range); buildings smashed in between make the next one louder")]
        Vector2 smashSoundInterval = new(0.3f, 0.55f);
        [SerializeField, Tooltip("Kaiju footsteps")]
        AudioClip stompSound;
        [SerializeField, Tooltip("The kaiju has grown big enough to eat the next tier of prey")]
        AudioClip growSound;
        [SerializeField] AudioClip winSound;
        [SerializeField] AudioClip loseSound;
        [SerializeField, Tooltip("Distance walked between footsteps, in kaiju radii")]
        float strideLength = 3f;
        [SerializeField, Tooltip("Moving between pause-menu entries")]
        AudioClip menuMoveSound;
        [SerializeField, Tooltip("Confirming a pause-menu entry")]
        AudioClip menuSelectSound;

        [Header("Music (Assets/Audio/Music/music_tokyo_<n>.wav; assign with Chomp Chomp Panic > Import Audio; silent without it)")]
        [SerializeField, Tooltip("Tokyo map tracks, played in random order with a crossfade between them")]
        AudioClip[] tokyoMusic;
        [SerializeField, Range(0f, 1f), Tooltip("Music mix level, before the player's music volume option")]
        float musicVolume = 0.5f;
        [SerializeField, Range(0f, 1f), Tooltip("Music level while paused or on the game-over screen, as a fraction of the mix level")]
        float musicDuck = 0.4f;

        readonly List<EnemyBlob> blobs = new();
        int[] liveCounts;
        EnemyBlob rival;
        float nextRivalTime;
        Camera cam;
        Blob player;
        SpriteAnimator playerAnimator;
        Transform blobRoot;
        SpriteRenderer grid;
        CityMap city;
        float shake;
        int smashedCount;
        float elapsed;
        int eatenCount;
        float health;
        float tint;
        Color tintColor;
        State state;
        string lossMessage;
        GUIStyle hudStyle;
        GUIStyle bannerStyle;
        GUIStyle markerStyle;
        float[] tierRadii;
        int nextTier;
        Vector2 lastStepPosition;
        float stepDistance;
        int unheardSmashes;
        float nextSmashSoundTime;
        int lastSmashSound = -1;
        bool paused;
        readonly Menu pauseMenu = new();

        float Progress => Mathf.Clamp01(elapsed / sessionLength);

        /// <summary>Distance from the camera center to a screen corner.</summary>
        float ViewRadius => cam.orthographicSize * Mathf.Sqrt(1f + cam.aspect * cam.aspect);

        /// <summary>
        /// Count ramp for the ground military: front-loaded, so by minute 2 of 10 they are
        /// about 45% of the way to their end-of-session numbers instead of 20%.
        /// </summary>
        const float MilitaryRamp = 0.5f;

        /// <summary>
        /// The prey ladder: each tier is about twice the one before, so the kaiju has to grow to reach the next.
        /// People and soldiers (0.45) -> cars and jeeps (0.9) -> tanks (1.8) -> fighter jets (2.7).
        /// </summary>
        static PreyType[] DefaultPreyTypes() => new[]
        {
            new PreyType
            {
                Name = "People", SpritePrefix = "person", Count = new(40f, 28f), Radius = 0.45f, VisualDiameter = 0.45f,
                Heal = 3f, Movement = Movement.Walk, Speed = 1.2f, FleeSpeed = 3.2f, FleeDistance = 2.5f, IdleChance = 0.3f,
            },
            new PreyType
            {
                Name = "Riflemen", SpritePrefix = "soldier_rifle", Count = new(4f, 18f), CountRampExponent = MilitaryRamp, Radius = 0.45f, VisualDiameter = 0.45f,
                Heal = 5f, Movement = Movement.Walk, Speed = 1.4f, FleeSpeed = 3f, FleeDistance = 1f, IdleChance = 0.15f,
                Weapon = new Weapon
                {
                    Range = 6f, Cooldown = new(1.8f, 2.8f), BurstCount = 3, BurstInterval = 0.12f, Damage = 1f,
                    ProjectileSpeed = 14f, Spread = 6f, HoldsPosition = true, ProjectileArt = "fx_bullet",
                },
            },
            new PreyType
            {
                Name = "Bazooka troops", SpritePrefix = "soldier_bazooka", Count = new(1f, 8f), CountRampExponent = MilitaryRamp, Radius = 0.45f, VisualDiameter = 0.45f,
                Heal = 5f, Movement = Movement.Walk, Speed = 1.2f, FleeSpeed = 2.8f, FleeDistance = 1f, IdleChance = 0.15f,
                Weapon = new Weapon
                {
                    Range = 7f, Cooldown = new(4f, 5.5f), Damage = 6f, ProjectileSpeed = 8f, Spread = 3f,
                    HoldsPosition = true, Explodes = true, ProjectileArt = "fx_rocket",
                },
            },
            new PreyType
            {
                Name = "Cars", SpritePrefix = "car", Count = new(14f, 10f), Radius = 0.9f, VisualDiameter = 0.9f,
                Movement = Movement.Drive, Speed = 2.5f, FleeSpeed = 4.5f, FleeDistance = 3.5f, CrunchShake = 0.03f,
            },
            new PreyType
            {
                Name = "Jeeps", SpritePrefix = "jeep", Count = new(0f, 6f), CountRampExponent = MilitaryRamp, Radius = 0.9f, VisualDiameter = 0.9f,
                Movement = Movement.Drive, Speed = 3f, FleeSpeed = 4.5f, FleeDistance = 2f, CrunchShake = 0.03f,
                Weapon = new Weapon
                {
                    Range = 6f, Cooldown = new(1.6f, 2.4f), BurstCount = 4, BurstInterval = 0.1f, Damage = 1f,
                    ProjectileSpeed = 14f, Spread = 8f, ProjectileArt = "fx_bullet",
                },
            },
            new PreyType
            {
                Name = "Tanks", SpritePrefix = "tank", Count = new(1f, 7f), CountRampExponent = MilitaryRamp, Radius = 1.8f, VisualDiameter = 1.8f,
                Movement = Movement.Drive, Speed = 1.4f, FleeSpeed = 1.4f, FleeDistance = 0f, CrunchShake = 0.06f,
                Weapon = new Weapon
                {
                    Range = 9f, Cooldown = new(3.5f, 5f), Damage = 10f, ProjectileSpeed = 11f, Spread = 2f,
                    HoldsPosition = true, Explodes = true, ProjectileArt = "fx_shell",
                },
            },
            new PreyType
            {
                Name = "Fighter jets", SpritePrefix = "plane", Count = new(0f, 3f), Radius = 2.7f, VisualDiameter = 2.7f,
                Movement = Movement.Fly, Speed = 10f, FleeDistance = 0f, CrunchShake = 0.08f,
                Weapon = new Weapon
                {
                    Range = 11f, Cooldown = new(1f, 1.6f), BurstCount = 2, BurstInterval = 0.25f, Damage = 7f,
                    ProjectileSpeed = 18f, Spread = 2f, ForwardArc = 30f, Explodes = true, ProjectileArt = "fx_missile",
                },
            },
        };

        void Start()
        {
            Time.timeScale = 1f;
            health = maxHealth;
            nextRivalTime = rivals.FirstArrival;

            cam = Camera.main;
            cam.backgroundColor = backgroundColor;
            cam.orthographicSize = TargetCameraSize(playerStartRadius);

            if (cityAtlas != null && cityAtlasData != null)
                city = new CityMap(cityAtlas, cityAtlasData.text, Random.Range(int.MinValue, int.MaxValue));
            else
                CreateGrid();
            CreatePlayer();
            lastStepPosition = player.transform.position;

            // Prey sizes the kaiju has yet to grow into; reaching each one plays the grow sound.
            var radii = new SortedSet<float>();
            foreach (var type in preyTypes)
                radii.Add(type.Radius);
            tierRadii = new float[radii.Count];
            radii.CopyTo(tierRadii);
            while (nextTier < tierRadii.Length && CanEat(tierRadii[nextTier]))
                nextTier++;

            blobRoot = new GameObject("Prey").transform;
            liveCounts = new int[preyTypes.Length];
            SpawnPrey(initial: true);

            MusicPlayer.Play(tokyoMusic, musicVolume);
            pauseMenu.MoveSound = menuMoveSound;
            pauseMenu.SelectSound = menuSelectSound;
        }

        void Update()
        {
            if (paused)
            {
                pauseMenu.Update();
                return;
            }
            if (state != State.Playing)
            {
                if (RestartPressed())
                    SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
                else if (TitlePressed())
                    SceneManager.LoadScene(TitleScene);
                return;
            }
            if (PausePressed())
            {
                Pause();
                return;
            }

            elapsed += Time.deltaTime;
            if (elapsed >= sessionLength)
            {
                EndSession(State.Won);
                return;
            }

            UpdateBlobs();
            if (state != State.Playing)
                return;
            SpawnPrey(initial: false);
            if (rival == null && elapsed >= nextRivalTime && rivals.HasSprites)
                SpawnRival();
            if (city != null)
                StompBuildings();
            Footsteps();
        }

        void StompBuildings()
        {
            int count = city.Stomp(player.transform.position, player.Radius);
            if (count > 0)
            {
                smashedCount += count;
                shake = Mathf.Min(shake + smashShake * count, smashShake * 4f);
                unheardSmashes += count;
            }
            SmashSound();
            // Rivals flatten the city too.
            if (rival != null)
                city.Stomp(rival.transform.position, rival.Blob.Radius);
        }

        /// <summary>
        /// A big kaiju smashes buildings nearly every frame. Rather than a constant wall of the same clip,
        /// play a random variant every so often, louder the more buildings fell since the last one.
        /// </summary>
        void SmashSound()
        {
            if (unheardSmashes == 0 || elapsed < nextSmashSoundTime || smashSounds is not { Length: > 0 })
                return;
            int pick = Random.Range(0, smashSounds.Length);
            if (pick == lastSmashSound && smashSounds.Length > 1)
                pick = (pick + 1) % smashSounds.Length;
            lastSmashSound = pick;
            float volume = Mathf.Lerp(0.55f, 0.9f, (unheardSmashes - 1) / 4f);
            PlaySound(smashSounds[pick], volume, SizePitch * Random.Range(0.92f, 1.08f));
            unheardSmashes = 0;
            nextSmashSoundTime = elapsed + Random.Range(smashSoundInterval.x, smashSoundInterval.y);
        }

        /// <summary>A thud every stride; heavier, slower and deeper as the kaiju grows.</summary>
        void Footsteps()
        {
            Vector2 position = player.transform.position;
            stepDistance += Vector2.Distance(position, lastStepPosition);
            lastStepPosition = position;
            if (stepDistance < player.Radius * strideLength)
                return;
            stepDistance = 0f;
            float growth = player.Radius / playerStartRadius;
            PlaySound(stompSound, Mathf.Lerp(0.2f, 0.7f, (growth - 1f) / 4f), SizePitch);
        }

        /// <summary>Pitch for the kaiju's own sounds: lower as it grows.</summary>
        float SizePitch => Mathf.Clamp(Mathf.Pow(playerStartRadius / player.Radius, 0.25f), 0.6f, 1.1f);

        bool CanEat(float radius) => player.Radius >= radius * eatRatio;

        /// <summary>Play a sound at the master volume; <paramref name="position"/>, if given, fades it with distance from the kaiju.</summary>
        void PlaySound(AudioClip clip, float volume = 1f, float pitch = 1f, Vector2? position = null)
        {
            if (position.HasValue)
            {
                float distance = Vector2.Distance(position.Value, player.transform.position) - player.Radius;
                volume *= Mathf.Clamp01(1.3f - distance / ViewRadius);
            }
            SoundPlayer.Play(clip, volume * soundVolume, pitch);
        }

        void LateUpdate()
        {
            if (player == null)
                return;

            var target = player.transform.position;
            cam.transform.position = new Vector3(target.x, target.y, cam.transform.position.z);
            cam.orthographicSize = Mathf.Lerp(cam.orthographicSize, TargetCameraSize(player.Radius),
                1f - Mathf.Exp(-3f * Time.unscaledDeltaTime));

            // Shake decays even while paused, so the game-over screen settles.
            shake *= Mathf.Exp(-12f * Time.unscaledDeltaTime);
            if (shake > 0.0005f)
                cam.transform.position += (Vector3)(Random.insideUnitCircle * (shake * cam.orthographicSize));

            // Hurt / heal tint fades back to normal.
            tint *= Mathf.Exp(-6f * Time.unscaledDeltaTime);
            var baseColor = playerAnimator != null ? Color.white : playerColor;
            player.Sprite.color = Color.Lerp(baseColor, tintColor, tint);

            if (city != null)
                city.UpdateView(cam.transform.position, ViewRadius + 1f);
            else
                UpdateGrid();
        }

        // ------------------------------------------------------------------ eating

        void UpdateBlobs()
        {
            Vector2 playerPos = player.transform.position;
            float despawnDistance = ViewRadius * 3.5f;

            for (int i = blobs.Count - 1; i >= 0; i--)
            {
                var blob = blobs[i];
                float playerRadius = player.Radius;
                float blobRadius = blob.Blob.Radius;
                float distance = Vector2.Distance(playerPos, blob.transform.position);

                // Something is eaten once its center is inside the eater.
                if (playerRadius >= blobRadius * eatRatio && distance < playerRadius)
                {
                    Eat(blob);
                    RemoveBlob(i);
                    continue;
                }

                // Only rival kaiju eat the player; prey that's too big just gets away.
                if (blob.IsRival && blobRadius >= playerRadius * eatRatio && distance < blobRadius)
                {
                    EndSession(State.Lost, "CHOMPED!");
                    return;
                }

                // Rivals hang around until they've had their time.
                bool farAway = distance > despawnDistance + blobRadius;
                if (farAway && (!blob.IsRival || blob.IsLeaving || distance > despawnDistance * 2f))
                    RemoveBlob(i);
            }
        }

        void Eat(EnemyBlob blob)
        {
            var type = blob.Type;
            player.Absorb(blob.Blob.Radius, blob.IsRival ? rivals.GrowthEfficiency : type.GrowthEfficiency);
            eatenCount++;
            if (playerAnimator != null)
                playerAnimator.PlayChomp();
            if (blob.IsRival || type.Bleeds)
                SplatterBlood(blob.transform.position);
            shake = Mathf.Min(shake + (blob.IsRival ? 0.1f : type.CrunchShake), 0.12f);
            if (type != null && type.Heal > 0f)
            {
                health = Mathf.Min(maxHealth, health + type.Heal);
                Flash(healColor, 0.6f);
            }

            // Vehicles (anything that shakes the camera) crunch; people get chomped.
            bool crunchy = blob.IsRival || type.CrunchShake > 0f;
            PlaySound(crunchy ? crunchSound : chompSound, crunchy ? 1f : 0.7f, SizePitch);

            bool grew = false;
            while (nextTier < tierRadii.Length && CanEat(tierRadii[nextTier]))
            {
                nextTier++;
                grew = true;
            }
            if (grew)
                PlaySound(growSound, 0.6f, 1f);
        }

        void SplatterBlood(Vector2 position)
        {
            // Stains lie on the ground (under buildings and characters); the spurt shows on top of the kaiju.
            const int stainOrder = -850;
            if (bloodStains is { Length: > 0 })
                GroundStain.Spawn(bloodStains[Random.Range(0, bloodStains.Length)], position, 1f, stainOrder,
                    bloodStainHoldAndFade.x, bloodStainHoldAndFade.y);
            if (bloodBurst is { Length: > 0 })
            {
                // Spurt from the kaiju's mouth: the front edge of the sprite, on the side it faces.
                float radius = player.Radius;
                float facing = player.Sprite.flipX ? -1f : 1f;
                var mouth = (Vector2)player.transform.position + new Vector2(facing * 0.6f, -0.1f) * radius;
                float size = bloodBurstSize * Mathf.Sqrt(radius / playerStartRadius);
                DustPuff.Spawn(bloodBurst, mouth, size / bloodBurst[0].bounds.size.x, short.MaxValue);
            }
        }

        // ------------------------------------------------------------------ gunfire and health

        void Fire(EnemyBlob shooter, Vector2 muzzle, Vector2 direction)
        {
            var weapon = shooter.Type.Weapon;
            if (muzzleFlash is { Length: > 0 })
                DustPuff.Spawn(muzzleFlash, muzzle, weapon.ProjectileScale * Mathf.Sqrt(shooter.Blob.Radius / 0.45f),
                    short.MaxValue - 30);
            // Fly on a little past the kaiju's far side, so misses still sail by.
            float range = weapon.Range + player.Radius * 2f + 2f;
            Projectile.Fire(weapon, muzzle, direction, range, player, OnImpact);
            if (weapon.Explodes)
                PlaySound(launchSound, 0.6f, Random.Range(0.9f, 1.15f), muzzle);
            else
                PlaySound(gunshotSound, 0.35f, Random.Range(0.9f, 1.2f), muzzle);
        }

        void OnImpact(Projectile projectile, bool hit)
        {
            var position = (Vector2)projectile.transform.position;
            if (projectile.Explodes)
                PlaySound(explosionSound, 0.9f, 1f, position);
            else if (hit)
                PlaySound(hitSound, 0.5f, 1f);
            if (projectile.Explodes && explosion is { Length: > 0 })
                DustPuff.Spawn(explosion, position, explosionSize / explosion[0].bounds.size.x, short.MaxValue - 25);
            else if (hit && muzzleFlash is { Length: > 0 })
                DustPuff.Spawn(muzzleFlash, position, 1.5f, short.MaxValue - 25);  // spark off the hide

            if (!hit || state != State.Playing)
                return;
            health -= projectile.Damage;
            Flash(hurtColor, 0.8f);
            if (projectile.Explodes)
                shake = Mathf.Min(shake + explosionShake, 0.12f);
            if (health <= 0f)
            {
                health = 0f;
                EndSession(State.Lost, "TAKEN DOWN!");
            }
        }

        void Flash(Color color, float strength)
        {
            tintColor = color;
            tint = Mathf.Max(tint, strength);
        }

        // ------------------------------------------------------------------ spawning

        /// <summary>Top up every prey type to its count for this point in the session.</summary>
        void SpawnPrey(bool initial)
        {
            for (int i = 0; i < preyTypes.Length; i++)
            {
                var type = preyTypes[i];
                if (!type.HasSprites)
                    continue;
                float ramp = Mathf.Pow(Progress, type.CountRampExponent);
                int target = Mathf.RoundToInt(Mathf.Lerp(type.Count.x, type.Count.y, ramp));
                while (liveCounts[i] < target)
                    SpawnPrey(i, initial);
            }
        }

        void SpawnPrey(int index, bool initial)
        {
            var type = preyTypes[index];
            var enemy = CreateEnemy(type.Name, type.Radius, type.VisualDiameter, type.Variants, initial);
            if (type.Movement == Movement.Fly)
            {
                // Planes always come in from off screen, on a pass over the kaiju.
                enemy.transform.position = OffScreenPoint(type.Radius, 1.2f);
                enemy.InitPrey(index, type, player, null, Fire);
            }
            else
            {
                void Place() => enemy.InitPrey(index, type, player, city?.Layout, Fire);
                Place();
                // Moving onto the nearest street can pull a new spawn into view; try other spots.
                for (int tries = 0; !initial && tries < 6 && IsOnScreen(enemy.transform.position); tries++)
                {
                    enemy.transform.position = SpawnPoint(type.Radius, initial);
                    Place();
                }
            }
            liveCounts[index]++;
        }

        void SpawnRival()
        {
            var range = new Vector2(Mathf.Lerp(rivals.StartSize.x, rivals.EndSize.x, Progress),
                Mathf.Lerp(rivals.StartSize.y, rivals.EndSize.y, Progress));
            float radius = playerStartRadius * Random.Range(range.x, range.y);
            rival = CreateEnemy("Rival kaiju", radius, 1f, rivals.Variants, initial: false);
            rival.transform.position = OffScreenPoint(radius, 1.1f);
            rival.InitRival(rivals, player, eatRatio, playerBaseSpeed, playerStartRadius);
        }

        EnemyBlob CreateEnemy(string objectName, float radius, float visualDiameter, CharacterSprites[] variants, bool initial)
        {
            var go = new GameObject(objectName);
            go.transform.SetParent(blobRoot, false);
            go.transform.position = SpawnPoint(radius, initial);

            var blob = go.AddComponent<Blob>();
            blob.VisualDiameter = visualDiameter;
            blob.Radius = radius;
            go.AddComponent<SpriteAnimator>().Init(variants[Random.Range(0, variants.Length)]);

            var enemy = go.AddComponent<EnemyBlob>();
            blobs.Add(enemy);
            return enemy;
        }

        /// <summary>
        /// At the start, fill the area around the player (leaving some breathing room).
        /// Afterwards, spawn just off screen.
        /// </summary>
        Vector2 SpawnPoint(float radius, bool initial)
        {
            float view = ViewRadius;
            float distance = initial ? Random.Range(view * 0.4f, view * 2.5f) : Random.Range(view * 1.1f, view * 2.5f);
            return (Vector2)player.transform.position + Random.insideUnitCircle.normalized * (distance + radius);
        }

        /// <summary>A point just outside the view, <paramref name="margin"/> view radii out.</summary>
        Vector2 OffScreenPoint(float radius, float margin)
        {
            return (Vector2)player.transform.position + Random.insideUnitCircle.normalized * (ViewRadius * margin + radius);
        }

        bool IsOnScreen(Vector2 position)
        {
            return Vector2.Distance(position, cam.transform.position) < ViewRadius;
        }

        void RemoveBlob(int index)
        {
            var blob = blobs[index];
            if (blob.IsRival)
            {
                rival = null;
                nextRivalTime = elapsed + Random.Range(rivals.Interval.x, rivals.Interval.y);
            }
            else
            {
                liveCounts[blob.TypeIndex]--;
            }
            Destroy(blob.gameObject);
            blobs.RemoveAt(index);
        }

        void CreatePlayer()
        {
            var go = new GameObject("Player");
            player = go.AddComponent<Blob>();
            player.Radius = playerStartRadius;
            if (playerSprites != null && playerSprites.IsValid)
            {
                playerAnimator = go.AddComponent<SpriteAnimator>();
                playerAnimator.Init(playerSprites);
            }
            else
            {
                player.Sprite.color = playerColor;
            }

            var controller = go.AddComponent<PlayerController>();
            controller.BaseSpeed = playerBaseSpeed;
            controller.ReferenceRadius = playerStartRadius;
            controller.City = city;
        }

        void CreateGrid()
        {
            var go = new GameObject("Grid");
            grid = go.AddComponent<SpriteRenderer>();
            grid.sprite = Sprites.GridCell;
            grid.drawMode = SpriteDrawMode.Tiled;
            grid.sortingOrder = short.MinValue;
        }

        void UpdateGrid()
        {
            // Cover the view with an even number of cells and snap to the cell size,
            // so the lines stay fixed in the world while the grid follows the camera.
            const float cell = Sprites.GridCellSize;
            int cells = 2 * Mathf.CeilToInt(ViewRadius / cell) + 2;
            grid.size = Vector2.one * (cells * cell);

            var camPos = cam.transform.position;
            grid.transform.position = new Vector3(Mathf.Round(camPos.x / cell) * cell, Mathf.Round(camPos.y / cell) * cell, 0f);
        }

        float TargetCameraSize(float playerRadius)
        {
            return cameraBaseSize * Mathf.Pow(playerRadius / playerStartRadius, cameraZoomExponent);
        }

        void EndSession(State result, string message = null)
        {
            state = result;
            lossMessage = message;
            Time.timeScale = 0f;
            MusicPlayer.SetLevel(musicVolume * musicDuck);
            PlaySound(result == State.Won ? winSound : loseSound);
            if (result == State.Lost)
            {
                if (playerAnimator != null)
                    playerAnimator.PlayDeath();
                else
                    player.gameObject.SetActive(false);
            }
        }

        // ------------------------------------------------------------------ pause menu

        void Pause()
        {
            paused = true;
            Time.timeScale = 0f;
            MusicPlayer.SetLevel(musicVolume * musicDuck);
            SoundPlayer.Play(menuSelectSound, pauseMenu.SoundVolume, 1f, 0f);
            ShowPauseMenu();
        }

        void Resume()
        {
            paused = false;
            Time.timeScale = 1f;
            MusicPlayer.SetLevel(musicVolume);
        }

        void ShowPauseMenu()
        {
            var items = new List<Menu.Item>
            {
                Menu.Button("Resume", Resume),
                Menu.Button("Options", () => pauseMenu.ShowOptions(ShowPauseMenu)),
                Menu.Button("Title Screen", () => SceneManager.LoadScene(TitleScene)),
            };
            if (Menu.CanQuit)
                items.Add(Menu.Button("Quit Game", Menu.QuitGame));
            pauseMenu.Show("Paused", Resume, items.ToArray());
        }

        static bool PausePressed()
        {
            var kb = Keyboard.current;
            if (kb != null && (kb.escapeKey.wasPressedThisFrame || kb.pKey.wasPressedThisFrame))
                return true;

            var pad = Gamepad.current;
            return pad != null && pad.startButton.wasPressedThisFrame;
        }

        /// <summary>The title screen is the first scene in the build profile.</summary>
        const int TitleScene = 0;

        static bool TitlePressed()
        {
            var kb = Keyboard.current;
            if (kb != null && kb.escapeKey.wasPressedThisFrame)
                return true;

            var pad = Gamepad.current;
            return pad != null && (pad.buttonEast.wasPressedThisFrame || pad.selectButton.wasPressedThisFrame);
        }

        static bool RestartPressed()
        {
            var kb = Keyboard.current;
            if (kb != null && (kb.rKey.wasPressedThisFrame || kb.spaceKey.wasPressedThisFrame || kb.enterKey.wasPressedThisFrame))
                return true;

            var pad = Gamepad.current;
            return pad != null && (pad.buttonSouth.wasPressedThisFrame || pad.startButton.wasPressedThisFrame);
        }

        // ------------------------------------------------------------------ HUD

        void OnGUI()
        {
            if (hudStyle == null)
            {
                hudStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.UpperCenter };
                bannerStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
                markerStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
            }
            hudStyle.fontSize = Mathf.RoundToInt(Screen.height * 0.035f);
            bannerStyle.fontSize = Mathf.RoundToInt(Screen.height * 0.07f);
            markerStyle.fontSize = Mathf.RoundToInt(Screen.height * 0.03f);

            float remaining = Mathf.Max(0f, sessionLength - elapsed);
            int minutes = Mathf.FloorToInt(remaining / 60f);
            int seconds = Mathf.FloorToInt(remaining % 60f);
            float size = player != null ? player.Radius / playerStartRadius : 1f;
            string hud = $"{minutes:00}:{seconds:00}     Size {size * size * 10f:0}     Chomped {eatenCount}     Smashed {smashedCount}";
            GUI.Label(new Rect(0, Screen.height * 0.02f, Screen.width, Screen.height * 0.1f), hud, hudStyle);

            DrawHealthBar();
            if (rival != null && player != null)
                DrawRivalMarker();

            if (paused)
            {
                DrawPauseMenu();
                return;
            }
            if (state == State.Playing)
                return;

            string title = state == State.Won ? "You survived!" : lossMessage ?? "CHOMPED!";
            string subtitle = "Press R, Space or Enter to play again, Esc for the title screen";
            var bannerRect = new Rect(0, Screen.height * 0.35f, Screen.width, Screen.height * 0.15f);
            GUI.Label(bannerRect, title, bannerStyle);
            GUI.Label(new Rect(0, bannerRect.yMax, Screen.width, Screen.height * 0.1f), subtitle, hudStyle);
        }

        void DrawPauseMenu()
        {
            var old = GUI.color;
            GUI.color = new Color(0.02f, 0.03f, 0.08f, 0.7f);
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);
            GUI.color = old;
            pauseMenu.Draw(Screen.height * 0.22f);
            var hint = new Rect(0, 0, Screen.width, Screen.height * 0.98f);
            hudStyle.fontSize = Mathf.RoundToInt(Screen.height * 0.022f);
            hudStyle.alignment = TextAnchor.LowerCenter;
            Menu.DrawShadowed(hint, Menu.Hint, hudStyle, new Color(1f, 1f, 1f, 0.45f));
            hudStyle.alignment = TextAnchor.UpperCenter;
        }

        void DrawHealthBar()
        {
            float width = Screen.width * 0.3f;
            float height = Screen.height * 0.022f;
            var back = new Rect((Screen.width - width) * 0.5f, Screen.height * 0.075f, width, height);
            float fraction = Mathf.Clamp01(health / maxHealth);
            var old = GUI.color;
            GUI.color = new Color(0.06f, 0.08f, 0.18f, 0.85f);
            GUI.DrawTexture(back, Texture2D.whiteTexture);
            GUI.color = Color.Lerp(new Color(0.9f, 0.2f, 0.25f), new Color(0.35f, 0.85f, 0.4f), fraction);
            var inner = new Rect(back.x + 2f, back.y + 2f, (back.width - 4f) * fraction, back.height - 4f);
            GUI.DrawTexture(inner, Texture2D.whiteTexture);
            GUI.color = old;
        }

        /// <summary>Warns about a rival: its color says whether it's dangerous, and an edge marker points at it off screen.</summary>
        void DrawRivalMarker()
        {
            bool dangerous = rival.Blob.Radius >= player.Radius * eatRatio;
            bool edible = player.Radius >= rival.Blob.Radius * eatRatio;
            var color = dangerous ? new Color(1f, 0.35f, 0.35f) : edible ? new Color(0.45f, 0.95f, 0.5f) : new Color(1f, 0.85f, 0.35f);
            string label = dangerous ? "RIVAL KAIJU - RUN!" : edible ? "RIVAL KAIJU - EAT IT!" : "RIVAL KAIJU";

            var old = GUI.contentColor;
            GUI.contentColor = color;
            GUI.Label(new Rect(0, Screen.height * 0.105f, Screen.width, Screen.height * 0.06f), label, hudStyle);

            var viewport = cam.WorldToViewportPoint(rival.transform.position);
            bool onScreen = viewport.x is > 0f and < 1f && viewport.y is > 0f and < 1f;
            if (!onScreen)
            {
                const float margin = 0.04f;
                var center = new Vector2(0.5f, 0.5f);
                var offset = (Vector2)viewport - center;
                float scale = (0.5f - margin) / Mathf.Max(Mathf.Abs(offset.x), Mathf.Abs(offset.y));
                var edge = center + offset * scale;
                float boxSize = Screen.height * 0.05f;
                var rect = new Rect(edge.x * Screen.width - boxSize * 0.5f, (1f - edge.y) * Screen.height - boxSize * 0.5f,
                    boxSize, boxSize);
                GUI.Label(rect, "!", markerStyle);
            }
            GUI.contentColor = old;
        }
    }
}
