using System;
using System.Collections.Generic;
using UnityEngine;

namespace ChompChompPanic
{
    /// <summary>
    /// Endless night-time Tokyo drawn under everything else. The world is a grid of city blocks
    /// (chunks) that are created around the camera and destroyed once far away. The street network
    /// comes from <see cref="StreetLayout"/>; a block's contents come from a hash of its coordinates,
    /// so it looks the same whenever you return. The kaiju smashes buildings by stepping on them;
    /// they stay rubble for the rest of the session. Art and sprite rects come from
    /// ArtSource/City/build_city.py.
    /// </summary>
    public class CityMap
    {
        // Layout in chunk pixels, measured from the chunk's bottom-left corner (matches build_city.py).
        const int ChunkPixels = 512;
        const int Inner = 68;        // road (48) + sidewalk (20) along each edge
        const int Road = 48;
        const int AlleyWidth = 22;
        const int LotPitch = 128;    // lots sit on a 128 px lattice; every 4th row/column is a street
        const int HalfStride = 60;
        const int StallOffset = 44;  // stalls line alleys, this far from the alley's center line
        static readonly Vector2 TempleCenter = new(256, 356);
        static readonly Vector2 TowerCenter = new(256, 256);

        const int GroundOrder = -1000;
        const int StreetOrder = -990;
        const int StreetOverlayOrder = -985;
        const int AlleyOrder = -980;
        const int DecorOrder = -900;
        const int BuildingOrder = -800;
        const int DustOrder = 1;
        /// <summary>Pixels trimmed off each side of a building's sprite before testing for a stomp.</summary>
        const float FootprintInset = 3f;

        enum BlockType { Downtown, Park, Shrine, Plaza }

        [Serializable] class AtlasRect { public int x, y, width, height; }
        [Serializable] class NamedRect { public string name; public string size; public AtlasRect rect; }
        [Serializable] class BuildingEntry { public string name; public string size; public AtlasRect intact; public AtlasRect rubble; }
        [Serializable] class AtlasData
        {
            public int pixelsPerUnit;
            public int chunkPixels;
            public NamedRect[] grounds;
            public NamedRect[] pieces;
            public NamedRect[] decor;
            public BuildingEntry[] buildings;
            public AtlasRect[] dust;
        }

        struct BuildingSprites { public Sprite Intact, Rubble; }

        class Building
        {
            public Rect Footprint;
            public SpriteRenderer Renderer;
            public Sprite Rubble;
            public bool Smashed;
        }

        class Chunk
        {
            public GameObject Root;
            public Vector2Int Key;
            public readonly List<Building> Buildings = new();
        }

        readonly float pixelsPerUnit;
        readonly float chunkSize;
        readonly int seed;
        readonly Transform root;
        readonly Dictionary<BlockType, Sprite> grounds = new();
        readonly Dictionary<string, Sprite> pieces = new();
        readonly Dictionary<string, Sprite> decor = new();
        readonly Dictionary<string, List<BuildingSprites>> buildingsBySize = new();
        readonly Sprite[] dust;
        readonly Dictionary<Vector2Int, Chunk> chunks = new();
        readonly HashSet<(int x, int y, int index)> smashed = new();
        readonly List<Vector2Int> toRemove = new();

        public StreetLayout Layout { get; }

        public CityMap(Texture2D atlas, string json, int seed)
        {
            var data = JsonUtility.FromJson<AtlasData>(json);
            pixelsPerUnit = data.pixelsPerUnit;
            chunkSize = data.chunkPixels / pixelsPerUnit;
            Debug.Assert(Mathf.Approximately(chunkSize, StreetLayout.BlockSize), "City chunks must match the street grid");
            this.seed = seed;
            Layout = new StreetLayout(seed);
            root = new GameObject("City").transform;

            foreach (var ground in data.grounds)
                grounds[Enum.Parse<BlockType>(ground.name, true)] = CreateSprite(atlas, ground.rect, ground.name);
            foreach (var piece in data.pieces)
                pieces[piece.name] = CreateSprite(atlas, piece.rect, piece.name);
            foreach (var entry in data.decor)
                decor[entry.name] = CreateSprite(atlas, entry.rect, entry.name);
            foreach (var entry in data.buildings)
            {
                if (!buildingsBySize.TryGetValue(entry.size, out var list))
                    buildingsBySize[entry.size] = list = new List<BuildingSprites>();
                list.Add(new BuildingSprites
                {
                    Intact = CreateSprite(atlas, entry.intact, entry.name),
                    Rubble = CreateSprite(atlas, entry.rubble, entry.name + "_rubble"),
                });
            }
            dust = new Sprite[data.dust.Length];
            for (int i = 0; i < dust.Length; i++)
                dust[i] = CreateSprite(atlas, data.dust[i], "dust_" + i);
        }

        Sprite CreateSprite(Texture2D atlas, AtlasRect r, string name)
        {
            var sprite = Sprite.Create(atlas, new Rect(r.x, r.y, r.width, r.height), new Vector2(0.5f, 0.5f),
                pixelsPerUnit, 0, SpriteMeshType.FullRect);
            sprite.name = name;
            return sprite;
        }

        /// <summary>Create the blocks that cover the view and drop the ones well outside it.</summary>
        public void UpdateView(Vector2 center, float viewRadius)
        {
            var min = ChunkAt(center - Vector2.one * viewRadius);
            var max = ChunkAt(center + Vector2.one * viewRadius);

            for (int y = min.y; y <= max.y; y++)
                for (int x = min.x; x <= max.x; x++)
                {
                    var key = new Vector2Int(x, y);
                    if (!chunks.ContainsKey(key))
                        chunks[key] = CreateChunk(key);
                }

            // Keep one extra ring of blocks so walking back and forth along an edge doesn't churn.
            toRemove.Clear();
            foreach (var key in chunks.Keys)
                if (key.x < min.x - 1 || key.x > max.x + 1 || key.y < min.y - 1 || key.y > max.y + 1)
                    toRemove.Add(key);
            foreach (var key in toRemove)
            {
                UnityEngine.Object.Destroy(chunks[key].Root);
                chunks.Remove(key);
            }
        }

        /// <summary>Smash every standing building the circle touches. Returns how many were smashed.</summary>
        public int Stomp(Vector2 position, float radius)
        {
            int count = 0;
            // A block's buildings along its left and bottom edges stick out into the neighbouring
            // block, so also check the blocks one to the right and one above.
            var min = ChunkAt(position - Vector2.one * radius);
            var max = ChunkAt(position + Vector2.one * radius) + Vector2Int.one;
            for (int y = min.y; y <= max.y; y++)
                for (int x = min.x; x <= max.x; x++)
                {
                    if (!chunks.TryGetValue(new Vector2Int(x, y), out var chunk))
                        continue;
                    for (int i = 0; i < chunk.Buildings.Count; i++)
                    {
                        var building = chunk.Buildings[i];
                        if (building.Smashed || !Overlaps(building.Footprint, position, radius))
                            continue;
                        Smash(building);
                        smashed.Add((x, y, i));
                        SpawnDust(building.Footprint);
                        count++;
                    }
                }
            return count;
        }

        static bool Overlaps(Rect rect, Vector2 center, float radius)
        {
            var closest = new Vector2(Mathf.Clamp(center.x, rect.xMin, rect.xMax), Mathf.Clamp(center.y, rect.yMin, rect.yMax));
            return (closest - center).sqrMagnitude < radius * radius;
        }

        static void Smash(Building building)
        {
            building.Smashed = true;
            building.Renderer.sprite = building.Rubble;
        }

        void SpawnDust(Rect footprint)
        {
            if (dust.Length == 0)
                return;
            // Dust frames are 48 px; scale the poof to cover most of the building.
            float frameSize = dust[0].rect.width / pixelsPerUnit;
            float scale = Mathf.Max(footprint.width, footprint.height) * 1.1f / frameSize;
            DustPuff.Spawn(dust, footprint.center, scale, DustOrder);
        }

        Vector2Int ChunkAt(Vector2 position)
        {
            return new Vector2Int(Mathf.FloorToInt(position.x / chunkSize), Mathf.FloorToInt(position.y / chunkSize));
        }

        Chunk CreateChunk(Vector2Int key)
        {
            var chunk = new Chunk { Root = new GameObject($"Block {key.x},{key.y}"), Key = key };
            chunk.Root.transform.SetParent(root, false);
            chunk.Root.transform.position = (Vector2)key * chunkSize;

            var type = BlockTypeAt(key);
            AddSprite(chunk, grounds[type], Vector2.one * (ChunkPixels * 0.5f), GroundOrder, "Ground");
            AddStreets(chunk);

            var rng = new System.Random((int)StreetLayout.Hash(key.x, key.y, seed));
            switch (type)
            {
                case BlockType.Downtown:
                    FillLots(chunk);
                    break;
                case BlockType.Shrine:
                    AddBuilding(chunk, Pick(rng, "temple"), TempleCenter);
                    break;
                case BlockType.Plaza:
                    AddBuilding(chunk, Pick(rng, "tower"), TowerCenter);
                    break;
            }

            // Buildings smashed on an earlier visit come back as rubble.
            for (int i = 0; i < chunk.Buildings.Count; i++)
                if (smashed.Contains((key.x, key.y, i)))
                    Smash(chunk.Buildings[i]);
            return chunk;
        }

        // ------------------------------------------------------------------ streets

        /// <summary>
        /// Each block draws its own half of the street along each of its four sides, plus the
        /// quarter of each junction at its corners. The neighbours draw the other halves.
        /// </summary>
        void AddStreets(Chunk chunk)
        {
            var key = chunk.Key;
            var left = Layout.VerticalEdge(key.x, key.y);
            var right = Layout.VerticalEdge(key.x + 1, key.y);
            var bottom = Layout.HorizontalEdge(key.x, key.y);
            var top = Layout.HorizontalEdge(key.x, key.y + 1);

            const float mid = ChunkPixels * 0.5f, near = Inner * 0.5f, far = ChunkPixels - Inner * 0.5f;
            AddEdge(chunk, left, "left", new Vector2(near, mid), bottom, top);
            AddEdge(chunk, right, "right", new Vector2(far, mid), bottom, top);
            AddEdge(chunk, bottom, "bottom", new Vector2(mid, near), left, right);
            AddEdge(chunk, top, "top", new Vector2(mid, far), left, right);

            AddCorner(chunk, "bl", left, bottom, key, Vector2Int.down);
            AddCorner(chunk, "br", right, bottom, key + Vector2Int.right, Vector2Int.down);
            AddCorner(chunk, "tl", left, top, key + Vector2Int.up, Vector2Int.up);
            AddCorner(chunk, "tr", right, top, key + Vector2Int.one, Vector2Int.up);
        }

        /// <param name="startSide">The street on the side where this edge starts (its left or bottom end).</param>
        /// <param name="endSide">The street on the side where this edge ends (its right or top end).</param>
        void AddEdge(Chunk chunk, Edge edge, string side, Vector2 stripCenter, Edge startSide, Edge endSide)
        {
            switch (edge)
            {
                case Edge.Road:
                    AddSprite(chunk, pieces["road_" + side], stripCenter, StreetOrder, "Road");
                    break;
                case Edge.Canal:
                    AddSprite(chunk, pieces["canal_" + side], new Vector2(ChunkPixels * 0.5f, stripCenter.y), StreetOrder, "Canal");
                    break;
                case Edge.Alley:
                    AddAlley(chunk, side, startSide, endSide);
                    break;
            }
        }

        /// <summary>
        /// Half of a backstreet along one side. It runs into the sidewalk of a crossing road (or the
        /// canal promenade) at either end, or all the way to the corner when it meets other alleys.
        /// </summary>
        void AddAlley(Chunk chunk, string side, Edge startSide, Edge endSide)
        {
            float start = startSide is Edge.Road or Edge.Canal ? Road : 0f;
            float end = endSide is Edge.Road or Edge.Canal ? ChunkPixels - Road : ChunkPixels;
            bool horizontal = side is "bottom" or "top";
            float across = side is "bottom" or "left" ? AlleyWidth * 0.5f : ChunkPixels - AlleyWidth * 0.5f;
            float along = (start + end) * 0.5f;

            var renderer = AddSprite(chunk, pieces["alley_" + side],
                horizontal ? new Vector2(along, across) : new Vector2(across, along), AlleyOrder, "Alley");
            renderer.drawMode = SpriteDrawMode.Tiled;
            var size = horizontal ? new Vector2(end - start, AlleyWidth) : new Vector2(AlleyWidth, end - start);
            renderer.size = size / pixelsPerUnit;
        }

        /// <param name="vertical">This block's street on the vertical side of the corner.</param>
        /// <param name="horizontal">This block's street on the horizontal side of the corner.</param>
        /// <param name="node">The junction at this corner.</param>
        /// <param name="outward">Vertical direction from the corner away from this block.</param>
        void AddCorner(Chunk chunk, string corner, Edge vertical, Edge horizontal, Vector2Int node, Vector2Int outward)
        {
            bool isLeft = corner[1] == 'l';
            var center = new Vector2(isLeft ? Inner * 0.5f : ChunkPixels - Inner * 0.5f,
                corner[0] == 'b' ? Inner * 0.5f : ChunkPixels - Inner * 0.5f);

            if (vertical == Edge.Road && horizontal == Edge.Road)
            {
                AddSprite(chunk, pieces["corner_" + corner], center, StreetOrder, "Crossing");
            }
            else if (vertical == Edge.Road && horizontal == Edge.Canal)
            {
                // A road meeting a canal crosses it on a bridge if it carries on the other side.
                if (Layout.Arm(node, outward) == Edge.Road)
                {
                    AddSprite(chunk, pieces["bridge_" + corner], center, StreetOverlayOrder, "Bridge");
                }
                else
                {
                    float shift = (Inner - Road) * 0.5f * (isLeft ? -1f : 1f);
                    AddSprite(chunk, pieces["deadend_" + corner], center + new Vector2(shift, 0f), StreetOverlayOrder, "Dead end");
                }
            }
            else if (vertical == Edge.Road || horizontal == Edge.Road)
            {
                // The road runs straight past this corner. Crosswalks only where a side street joins.
                string axis = vertical == Edge.Road ? "sideV" : "sideH";
                string kind = Layout.RoadDegree(node) >= 3 ? "_cross_" : "_plain_";
                AddSprite(chunk, pieces[axis + kind + corner], center, StreetOrder, "Road");
            }
        }

        // ------------------------------------------------------------------ block contents

        /// <summary>
        /// Mostly downtown, with some parks, shrines and the odd Tokyo Tower. Merged blocks are always
        /// downtown. Two special blocks of the same kind never touch: the one with the lower hash turns
        /// into downtown instead.
        /// </summary>
        BlockType BlockTypeAt(Vector2Int key)
        {
            if (Layout.IsMerged(key))
                return BlockType.Downtown;
            var type = RawBlockType(key, out uint hash);
            if (type == BlockType.Downtown)
                return type;
            foreach (var offset in StreetLayout.Directions)
            {
                var neighbour = key + offset;
                if (!Layout.IsMerged(neighbour) && RawBlockType(neighbour, out uint other) == type && other > hash)
                    return BlockType.Downtown;
            }
            return type;
        }

        BlockType RawBlockType(Vector2Int key, out uint hash)
        {
            hash = StreetLayout.Hash(key.x, key.y, seed ^ 0x5bd1e995);
            double r = hash / (double)uint.MaxValue;
            if (r < 0.75) return BlockType.Downtown;
            if (r < 0.88) return BlockType.Park;
            if (r < 0.98) return BlockType.Shrine;
            return BlockType.Plaza; // Tokyo Tower is a rare landmark
        }

        /// <summary>
        /// Lots sit on a 128 px lattice. A block owns the lots inside it, plus - where it merges with a
        /// neighbour on its left or bottom - the lots where the street would have been. Alleys get a row
        /// of tiny stalls on each side instead.
        /// </summary>
        void FillLots(Chunk chunk)
        {
            var key = chunk.Key;
            var left = Layout.VerticalEdge(key.x, key.y);
            var bottom = Layout.HorizontalEdge(key.x, key.y);
            bool cornerOpen = true;
            foreach (var direction in StreetLayout.Directions)
                cornerOpen &= Layout.Arm(key, direction) == Edge.Open;

            for (int j = 0; j < 4; j++)
                for (int i = 0; i < 4; i++)
                {
                    bool owned = (i, j) switch
                    {
                        (0, 0) => cornerOpen,
                        (0, _) => left == Edge.Open,
                        (_, 0) => bottom == Edge.Open,
                        _ => true,
                    };
                    if (owned)
                        FillLot(chunk, new Vector2(i * LotPitch, j * LotPitch), key * 4 + new Vector2Int(i, j));
                }

            if (left == Edge.Alley)
                AddStalls(chunk, false);
            if (bottom == Edge.Alley)
                AddStalls(chunk, true);
        }

        /// <summary>One big building, two halves, four small ones or an open lot.</summary>
        void FillLot(Chunk chunk, Vector2 center, Vector2Int slot)
        {
            var rng = new System.Random((int)StreetLayout.Hash(slot.x, slot.y, seed ^ 0x1B873593));
            const float quarter = HalfStride * 0.5f;
            double r = rng.NextDouble();
            if (r < 0.30)
            {
                AddBuilding(chunk, Pick(rng, "big"), center);
            }
            else if (r < 0.55)
            {
                AddBuilding(chunk, Pick(rng, "tall"), center + new Vector2(-quarter, 0f));
                AddBuilding(chunk, Pick(rng, "tall"), center + new Vector2(quarter, 0f));
            }
            else if (r < 0.70)
            {
                AddBuilding(chunk, Pick(rng, "wide"), center + new Vector2(0f, -quarter));
                AddBuilding(chunk, Pick(rng, "wide"), center + new Vector2(0f, quarter));
            }
            else if (r < 0.90)
            {
                for (int k = 0; k < 4; k++)
                {
                    var offset = new Vector2(k % 2 == 0 ? -quarter : quarter, k < 2 ? -quarter : quarter);
                    if (rng.NextDouble() < 0.1)
                        AddSprite(chunk, decor["vending"], center + offset, DecorOrder, "Vending machines");
                    else
                        AddBuilding(chunk, Pick(rng, "small"), center + offset);
                }
            }
            else
            {
                string name = rng.NextDouble() < 0.5 ? "parking" : "garden";
                AddSprite(chunk, decor[name], center, DecorOrder, name);
            }
        }

        /// <summary>Tiny stalls on both sides of the alley along this block's left (or bottom) edge.</summary>
        void AddStalls(Chunk chunk, bool horizontalAlley)
        {
            var rng = new System.Random((int)StreetLayout.Hash(chunk.Key.x, chunk.Key.y, seed ^ (horizontalAlley ? 0x2545F491 : 0x4F1BBCDD)));
            for (int j = 1; j < 4; j++)
                for (int k = 0; k < 4; k++)
                {
                    if (rng.NextDouble() > 0.7)
                        continue;
                    float along = j * LotPitch + (k % 2 == 0 ? -HalfStride * 0.5f : HalfStride * 0.5f);
                    float across = k < 2 ? -StallOffset : StallOffset;
                    AddBuilding(chunk, Pick(rng, "stall"), horizontalAlley ? new Vector2(along, across) : new Vector2(across, along));
                }
        }

        BuildingSprites Pick(System.Random rng, string size)
        {
            var list = buildingsBySize[size];
            return list[rng.Next(list.Count)];
        }

        void AddBuilding(Chunk chunk, BuildingSprites sprites, Vector2 centerPixels)
        {
            var renderer = AddSprite(chunk, sprites.Intact, centerPixels, BuildingOrder, sprites.Intact.name);
            var size = sprites.Intact.rect.size - Vector2.one * (2f * FootprintInset);
            var footprint = new Rect((Vector2)chunk.Root.transform.position + (centerPixels - size * 0.5f) / pixelsPerUnit,
                size / pixelsPerUnit);
            chunk.Buildings.Add(new Building { Footprint = footprint, Renderer = renderer, Rubble = sprites.Rubble });
        }

        SpriteRenderer AddSprite(Chunk chunk, Sprite sprite, Vector2 centerPixels, int order, string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(chunk.Root.transform, false);
            go.transform.localPosition = centerPixels / pixelsPerUnit;
            var renderer = go.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            renderer.sortingOrder = order;
            return renderer;
        }
    }
}
