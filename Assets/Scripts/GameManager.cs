using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace ChompChompPanic
{
    /// <summary>
    /// Runs a session: spawns the player and blobs, resolves eating and building smashing,
    /// follows with the camera, tracks the timer and draws a minimal HUD.
    /// </summary>
    public class GameManager : MonoBehaviour
    {
        enum State { Playing, Won, Lost }

        [Header("Session")]
        [SerializeField, Tooltip("Seconds the player must survive")]
        float sessionLength = 20f * 60f;

        [Header("Player")]
        [SerializeField] float playerStartRadius = 0.5f;
        [SerializeField] float playerBaseSpeed = 5f;
        [SerializeField, Tooltip("Player tint when no character sprites are assigned")]
        Color playerColor = new(0.35f, 0.75f, 1f);
        [SerializeField] CharacterSprites playerSprites;

        [Header("Eating")]
        [SerializeField, Tooltip("How many times bigger (by radius) a blob must be to eat another")]
        float eatRatio = 1.05f;
        [SerializeField, Tooltip("Fraction of an eaten blob's area added to the eater")]
        float growthEfficiency = 1f;

        [Header("Blob spawning")]
        [SerializeField] int blobCount = 80;
        [SerializeField, Tooltip("Baseline blob radius at the end of the session, as a multiple of the player's start radius")]
        float finalSizeMultiplier = 8f;
        [SerializeField, Range(0f, 1f), Tooltip("Chance a new blob is bigger than the baseline at the start")]
        float startBiggerChance = 0.1f;
        [SerializeField, Range(0f, 1f), Tooltip("Chance a new blob is bigger than the baseline at the end")]
        float endBiggerChance = 0.6f;
        [SerializeField, Tooltip("Radius range for smaller blobs, as a multiple of the baseline (people fill the tier below this)")]
        Vector2 smallerSizeRange = new(0.5f, 0.9f);
        [SerializeField, Tooltip("Radius range for bigger blobs, as a multiple of the baseline")]
        Vector2 biggerSizeRange = new(1.15f, 2f);
        [SerializeField, Tooltip("Blob wander speed relative to a player of the same size")]
        float blobSpeedFactor = 0.4f;

        [Header("People (smallest prey, fixed size)")]
        [SerializeField, Tooltip("One entry per person variant; a random one is picked for each spawn")]
        CharacterSprites[] peopleVariants;
        [SerializeField] int peopleCount = 40;
        [SerializeField, Tooltip("Collision radius of a person. People don't grow over the session. Must be under the kaiju's start radius / eat ratio to be edible from the start.")]
        float personRadius = 0.45f;
        [SerializeField, Tooltip("Sets the sprite's scale: scale = 2 x radius / this. 0.45 with radius 0.45 draws people at exactly 2x pixel scale.")]
        float personVisualDiameter = 0.45f;
        [SerializeField, Tooltip("Fraction of a person's area the kaiju gains when eating one (circles use Growth Efficiency)")]
        float personGrowthEfficiency = 0.25f;
        [SerializeField] float personWanderSpeed = 1.2f;
        [SerializeField] float personFleeSpeed = 3.2f;
        [SerializeField, Tooltip("People start running away when the kaiju's edge is this close")]
        float personFleeDistance = 2.5f;
        [SerializeField, Range(0f, 1f), Tooltip("Chance a wandering person stands still (panicking on the spot) instead of walking")]
        float personIdleChance = 0.3f;

        [Header("Camera")]
        [SerializeField] float cameraBaseSize = 6f;
        [SerializeField, Tooltip("How much the camera zooms out as the player grows (0 = never, 1 = player stays the same size on screen)")]
        float cameraZoomExponent = 0.6f;
        [SerializeField] Color backgroundColor = new(0.07f, 0.08f, 0.11f);
        [SerializeField, Tooltip("Camera shake per smashed building, as a fraction of the camera's half-height")]
        float smashShake = 0.015f;

        [Header("City (from ArtSource/City/build_city.py; a plain grid is drawn without it)")]
        [SerializeField] Texture2D cityAtlas;
        [SerializeField, Tooltip("city_atlas.json: where each sprite sits in the atlas")]
        TextAsset cityAtlasData;

        [Header("Blob colors (relative to the player)")]
        [SerializeField] Color edibleColor = new(0.45f, 0.9f, 0.5f);
        [SerializeField] Color neutralColor = new(0.95f, 0.85f, 0.35f);
        [SerializeField] Color dangerColor = new(1f, 0.35f, 0.35f);

        readonly List<EnemyBlob> blobs = new();
        Camera cam;
        Blob player;
        SpriteAnimator playerAnimator;
        Transform blobRoot;
        SpriteRenderer grid;
        CityMap city;
        float shake;
        int smashedCount;
        int circleCount;
        int personCount;
        float elapsed;
        int eatenCount;
        State state;
        GUIStyle hudStyle;
        GUIStyle bannerStyle;

        float Progress => Mathf.Clamp01(elapsed / sessionLength);

        /// <summary>Distance from the camera center to a screen corner.</summary>
        float ViewRadius => cam.orthographicSize * Mathf.Sqrt(1f + cam.aspect * cam.aspect);

        void Start()
        {
            Time.timeScale = 1f;

            cam = Camera.main;
            cam.backgroundColor = backgroundColor;
            cam.orthographicSize = TargetCameraSize(playerStartRadius);

            if (cityAtlas != null && cityAtlasData != null)
                city = new CityMap(cityAtlas, cityAtlasData.text, Random.Range(int.MinValue, int.MaxValue));
            else
                CreateGrid();
            CreatePlayer();

            blobRoot = new GameObject("Blobs").transform;
            for (int i = 0; i < blobCount; i++)
                SpawnBlob(initial: true);
            if (HasPeople)
                for (int i = 0; i < peopleCount; i++)
                    SpawnPerson(initial: true);
        }

        bool HasPeople => peopleVariants is { Length: > 0 };

        void Update()
        {
            if (state != State.Playing)
            {
                if (RestartPressed())
                    SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
                return;
            }

            elapsed += Time.deltaTime;
            if (elapsed >= sessionLength)
            {
                EndSession(State.Won);
                return;
            }

            UpdateBlobs();
            if (state == State.Playing && city != null)
                StompBuildings();
        }

        void StompBuildings()
        {
            int count = city.Stomp(player.transform.position, player.Radius);
            if (count == 0)
                return;
            smashedCount += count;
            shake = Mathf.Min(shake + smashShake * count, smashShake * 4f);
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

            if (city != null)
                city.UpdateView(cam.transform.position, ViewRadius + 1f);
            else
                UpdateGrid();
        }

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

                bool playerCanEat = playerRadius >= blobRadius * eatRatio;
                bool blobCanEat = blobRadius >= playerRadius * eatRatio;

                // A blob is eaten once its center is inside the eater.
                if (playerCanEat && distance < playerRadius)
                {
                    player.Absorb(blobRadius, blob.IsPerson ? personGrowthEfficiency : growthEfficiency);
                    eatenCount++;
                    if (playerAnimator != null)
                        playerAnimator.PlayChomp();
                    RemoveBlob(i);
                    continue;
                }

                if (blobCanEat && distance < blobRadius)
                {
                    EndSession(State.Lost);
                    return;
                }

                if (distance > despawnDistance)
                {
                    RemoveBlob(i);
                    continue;
                }

                // People keep their own colors; circles are tinted to show whether they're safe to eat.
                if (!blob.IsPerson)
                    blob.Blob.Sprite.color = playerCanEat ? edibleColor : blobCanEat ? dangerColor : neutralColor;
            }

            while (circleCount < blobCount)
                SpawnBlob(initial: false);
            if (HasPeople)
                while (personCount < peopleCount)
                    SpawnPerson(initial: false);
        }

        void SpawnBlob(bool initial)
        {
            // Blob sizes follow a baseline that grows over the session; over time
            // more of them are bigger than the baseline, so the player has to keep growing.
            float baseline = playerStartRadius * Mathf.Lerp(1f, finalSizeMultiplier, Progress);
            bool bigger = Random.value < Mathf.Lerp(startBiggerChance, endBiggerChance, Progress);
            var range = bigger ? biggerSizeRange : smallerSizeRange;
            float radius = baseline * Random.Range(range.x, range.y);

            var enemy = CreateEnemy("Blob", radius, 1f, initial);
            enemy.Init(PlayerController.SpeedForRadius(radius, playerBaseSpeed, playerStartRadius) * blobSpeedFactor);
            circleCount++;
        }

        void SpawnPerson(bool initial)
        {
            var enemy = CreateEnemy("Person", personRadius, personVisualDiameter, initial);
            var animator = enemy.gameObject.AddComponent<SpriteAnimator>();
            animator.Init(peopleVariants[Random.Range(0, peopleVariants.Length)]);
            enemy.Init(personWanderSpeed);
            enemy.InitPerson(player, personFleeSpeed, personFleeDistance, personIdleChance, city?.Layout);
            // Moving onto the nearest street can pull a new person into view; try other spots.
            for (int tries = 0; !initial && tries < 6 && IsOnScreen(enemy.transform.position); tries++)
            {
                enemy.transform.position = SpawnPoint(personRadius, initial);
                enemy.InitPerson(player, personFleeSpeed, personFleeDistance, personIdleChance, city?.Layout);
            }
            personCount++;
        }

        EnemyBlob CreateEnemy(string objectName, float radius, float visualDiameter, bool initial)
        {
            var go = new GameObject(objectName);
            go.transform.SetParent(blobRoot, false);
            go.transform.position = SpawnPoint(radius, initial);

            var blob = go.AddComponent<Blob>();
            blob.VisualDiameter = visualDiameter;
            blob.Radius = radius;

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

        bool IsOnScreen(Vector2 position)
        {
            return Vector2.Distance(position, cam.transform.position) < ViewRadius;
        }

        void RemoveBlob(int index)
        {
            var blob = blobs[index];
            if (blob.IsPerson) personCount--;
            else circleCount--;
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

        void EndSession(State result)
        {
            state = result;
            Time.timeScale = 0f;
            if (result == State.Lost)
            {
                if (playerAnimator != null)
                    playerAnimator.PlayDeath();
                else
                    player.gameObject.SetActive(false);
            }
        }

        static bool RestartPressed()
        {
            var kb = Keyboard.current;
            if (kb != null && (kb.rKey.wasPressedThisFrame || kb.spaceKey.wasPressedThisFrame || kb.enterKey.wasPressedThisFrame))
                return true;

            var pad = Gamepad.current;
            return pad != null && (pad.buttonSouth.wasPressedThisFrame || pad.startButton.wasPressedThisFrame);
        }

        void OnGUI()
        {
            if (hudStyle == null)
            {
                hudStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.UpperCenter };
                bannerStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
            }
            hudStyle.fontSize = Mathf.RoundToInt(Screen.height * 0.035f);
            bannerStyle.fontSize = Mathf.RoundToInt(Screen.height * 0.07f);

            float remaining = Mathf.Max(0f, sessionLength - elapsed);
            int minutes = Mathf.FloorToInt(remaining / 60f);
            int seconds = Mathf.FloorToInt(remaining % 60f);
            float size = player != null ? player.Radius / playerStartRadius : 1f;
            string hud = $"{minutes:00}:{seconds:00}     Size {size * size * 10f:0}     Chomped {eatenCount}     Smashed {smashedCount}";
            GUI.Label(new Rect(0, Screen.height * 0.02f, Screen.width, Screen.height * 0.1f), hud, hudStyle);

            if (state == State.Playing)
                return;

            string title = state == State.Won ? "You survived!" : "CHOMPED!";
            string subtitle = "Press R, Space or Enter to play again";
            var bannerRect = new Rect(0, Screen.height * 0.35f, Screen.width, Screen.height * 0.15f);
            GUI.Label(bannerRect, title, bannerStyle);
            GUI.Label(new Rect(0, bannerRect.yMax, Screen.width, Screen.height * 0.1f), subtitle, hudStyle);
        }
    }
}
