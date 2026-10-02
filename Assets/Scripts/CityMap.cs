using System;
using System.Collections.Generic;
using UnityEngine;

namespace ChompChompPanic
{
    /// <summary>
    /// Endless night-time Tokyo drawn under everything else. The world is a grid of city blocks
    /// (chunks) of a few different sizes that are created around the camera and destroyed once far
    /// away. The street network, including diagonal avenues, comes from <see cref="StreetLayout"/>; a
    /// block's contents come from a hash of its coordinates, so it looks the same whenever you return.
    /// The kaiju smashes buildings no bigger than itself by stepping on them and is blocked by bigger
    /// ones; they stay rubble for the rest of the session. Art and sprite rects come from
    /// ArtSource/City/build_city.py.
    /// </summary>
    public class CityMap
    {
        // Layout in chunk pixels, measured from the chunk's bottom-left corner (matches build_city.py).
        // Chunks are 256 to 768 px along each side; ground art is drawn for a nominal 512 px block.
        const int ChunkPixels = 512;
        const int Inner = 68;        // road (48) + sidewalk (20) along each edge
        const int Road = 48;
        const int AlleyWidth = 22;
        const int LotPitch = 128;    // lots sit on a 128 px lattice; every 4th row/column is a street
        const int HalfStride = 60;
        const int StallOffset = 44;  // stalls line alleys, this far from the alley's center line
        static readonly Vector2 TempleCenter = new(256, 356);
        static readonly Vector2 TowerCenter = new(256, 256);
        // Diagonal avenues, in pixels from the bottom-left corner of their 2 x 2 cell.
        const int CellPixels = 2 * ChunkPixels;
        const int DiagonalEnd = 256;    // end pieces cover this square at each end
        const int DiagonalStep = 128;   // middle pieces repeat this far apart along both axes
        /// <summary>How far an avenue's sidewalks reach from its center line, measured along x (or y).</summary>
        const float AvenueReach = Inner * 1.41421356f;

        const int GroundOrder = -1000;
        const int GroundDetailOrder = -999;
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
            /// <summary>Size in pixels.</summary>
            public int Width, Height;
            public Diagonal Diagonal;
            /// <summary>World position of the bottom-left corner of the chunk's 2 x 2 cell.</summary>
            public Vector2 CellOrigin;
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
            var min = Layout.BlockAt(center - Vector2.one * viewRadius);
            var max = Layout.BlockAt(center + Vector2.one * viewRadius);

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

        /// <summary>
        /// Smash every standing building the circle touches that is no bigger than the circle
        /// (its longest side fits within the diameter). Returns how many were smashed.
        /// </summary>
        public int Stomp(Vector2 position, float radius)
        {
            int count = 0;
            float diameter = radius * 2f;
            // A block's buildings along its left and bottom edges stick out into the neighbouring
            // block, so also check the blocks one to the right and one above.
            var min = Layout.BlockAt(position - Vector2.one * radius);
            var max = Layout.BlockAt(position + Vector2.one * radius) + Vector2Int.one;
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
                        if (TooBig(building, diameter))
                            continue;
                        Smash(building);
                        smashed.Add((x, y, i));
                        SpawnDust(building.Footprint);
                        count++;
                    }
                }
            return count;
        }

        /// <summary>
        /// Push the circle out of every standing building too big for it to stomp, so it slides
        /// along their walls. Returns the corrected position.
        /// </summary>
        public Vector2 PushOut(Vector2 position, float radius)
        {
            float diameter = radius * 2f;
            var min = Layout.BlockAt(position - Vector2.one * radius);
            var max = Layout.BlockAt(position + Vector2.one * radius) + Vector2Int.one;
            for (int y = min.y; y <= max.y; y++)
                for (int x = min.x; x <= max.x; x++)
                {
                    if (!chunks.TryGetValue(new Vector2Int(x, y), out var chunk))
                        continue;
                    foreach (var building in chunk.Buildings)
                        if (!building.Smashed && TooBig(building, diameter))
                            position = PushOutOfRect(building.Footprint, position, radius);
                }
            return position;
        }

        /// <summary>A building can only be knocked down once its longest side fits within the diameter.</summary>
        static bool TooBig(Building building, float diameter)
        {
            return Mathf.Max(building.Footprint.width, building.Footprint.height) > diameter;
        }

        static Vector2 PushOutOfRect(Rect rect, Vector2 center, float radius)
        {
            var closest = new Vector2(Mathf.Clamp(center.x, rect.xMin, rect.xMax), Mathf.Clamp(center.y, rect.yMin, rect.yMax));
            var away = center - closest;
            float distance = away.magnitude;
            if (distance >= radius)
                return center;
            if (distance > 0f)
                return closest + away / distance * radius;

            // Center is inside the building: leave through the nearest wall.
            float left = center.x - rect.xMin, right = rect.xMax - center.x;
            float bottom = center.y - rect.yMin, top = rect.yMax - center.y;
            float nearest = Mathf.Min(Mathf.Min(left, right), Mathf.Min(bottom, top));
            if (nearest == left) return new Vector2(rect.xMin - radius, center.y);
            if (nearest == right) return new Vector2(rect.xMax + radius, center.y);
            if (nearest == bottom) return new Vector2(center.x, rect.yMin - radius);
            return new Vector2(center.x, rect.yMax + radius);
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

        Chunk CreateChunk(Vector2Int key)
        {
            var rect = Layout.BlockRect(key);
            var cell = StreetLayout.CellOf(key);
            var chunk = new Chunk
            {
                Root = new GameObject($"Block {key.x},{key.y}"),
                Key = key,
                Width = Mathf.RoundToInt(rect.width * pixelsPerUnit),
                Height = Mathf.RoundToInt(rect.height * pixelsPerUnit),
                Diagonal = Layout.DiagonalInCell(cell),
                CellOrigin = StreetLayout.CellOrigin(cell),
            };
            chunk.Root.transform.SetParent(root, false);
            chunk.Root.transform.position = rect.min;

            // Pavement tiles to any block size; parks, shrines and plazas sit in the middle of it.
            var type = BlockTypeAt(key);
            var middle = new Vector2(chunk.Width, chunk.Height) * 0.5f;
            var ground = AddSprite(chunk, grounds[BlockType.Downtown], middle, GroundOrder, "Ground");
            ground.drawMode = SpriteDrawMode.Tiled;
            ground.size = new Vector2(chunk.Width, chunk.Height) / pixelsPerUnit;
            if (type != BlockType.Downtown)
                AddSprite(chunk, grounds[type], middle, GroundDetailOrder, type.ToString());
            AddStreets(chunk);
            AddDiagonal(chunk);

            var rng = new System.Random((int)StreetLayout.Hash(key.x, key.y, seed));
            var shift = middle - Vector2.one * (ChunkPixels * 0.5f);
            switch (type)
            {
                case BlockType.Downtown:
                    FillLots(chunk);
                    break;
                case BlockType.Shrine:
                    AddBuilding(chunk, Pick(rng, "temple"), TempleCenter + shift);
                    break;
                case BlockType.Plaza:
                    AddBuilding(chunk, Pick(rng, "tower"), TowerCenter + shift);
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

            float w = chunk.Width, h = chunk.Height;
            const float near = Inner * 0.5f;
            AddEdge(chunk, left, "left", new Vector2(near, h * 0.5f), bottom, top);
            AddEdge(chunk, right, "right", new Vector2(w - near, h * 0.5f), bottom, top);
            AddEdge(chunk, bottom, "bottom", new Vector2(w * 0.5f, near), left, right);
            AddEdge(chunk, top, "top", new Vector2(w * 0.5f, h - near), left, right);

            AddCorner(chunk, "bl", left, bottom, key, Vector2Int.down);
            AddCorner(chunk, "br", right, bottom, key + Vector2Int.right, Vector2Int.down);
            AddCorner(chunk, "tl", left, top, key + Vector2Int.up, Vector2Int.up);
            AddCorner(chunk, "tr", right, top, key + Vector2Int.one, Vector2Int.up);
        }

        /// <param name="startSide">The street on the side where this edge starts (its left or bottom end).</param>
        /// <param name="endSide">The street on the side where this edge ends (its right or top end).</param>
        void AddEdge(Chunk chunk, Edge edge, string side, Vector2 stripCenter, Edge startSide, Edge endSide)
        {
            // Strips come in one length per block size, named by the block's side length.
            int length = side is "bottom" or "top" ? chunk.Width : chunk.Height;
            switch (edge)
            {
                case Edge.Road:
                    AddPiece(chunk, $"road_{side}_{length}", stripCenter, StreetOrder, "Road");
                    break;
                case Edge.Canal:
                    AddPiece(chunk, $"canal_{side}_{length}", new Vector2(chunk.Width * 0.5f, stripCenter.y), StreetOrder, "Canal");
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
            bool horizontal = side is "bottom" or "top";
            float length = horizontal ? chunk.Width : chunk.Height;
            float start = startSide is Edge.Road or Edge.Canal ? Road : 0f;
            float end = endSide is Edge.Road or Edge.Canal ? length - Road : length;
            float across = side is "bottom" or "left" ? AlleyWidth * 0.5f : (horizontal ? chunk.Height : chunk.Width) - AlleyWidth * 0.5f;
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
            var center = new Vector2(isLeft ? Inner * 0.5f : chunk.Width - Inner * 0.5f,
                corner[0] == 'b' ? Inner * 0.5f : chunk.Height - Inner * 0.5f);

            if (vertical == Edge.Road && horizontal == Edge.Road)
            {
                // Where a diagonal avenue opens out of this corner, drop the corner's lamp (it would stand in the road).
                var inward = new Vector2Int(isLeft ? 1 : -1, corner[0] == 'b' ? 1 : -1);
                string name = Layout.Arm(node, inward) == Edge.Road ? "corner_open_" : "corner_";
                AddPiece(chunk, name + corner, center, StreetOrder, "Crossing");
            }
            else if (vertical == Edge.Road && horizontal == Edge.Canal)
            {
                // A road meeting a canal crosses it on a bridge if it carries on the other side.
                if (Layout.Arm(node, outward) == Edge.Road)
                {
                    AddPiece(chunk, "bridge_" + corner, center, StreetOverlayOrder, "Bridge");
                }
                else
                {
                    float shift = (Inner - Road) * 0.5f * (isLeft ? -1f : 1f);
                    AddPiece(chunk, "deadend_" + corner, center + new Vector2(shift, 0f), StreetOverlayOrder, "Dead end");
                }
            }
            else if (vertical == Edge.Road || horizontal == Edge.Road)
            {
                // The road runs straight past this corner. Crosswalks only where a side street joins.
                string axis = vertical == Edge.Road ? "sideV" : "sideH";
                string kind = Layout.RoadDegree(node) >= 3 ? "_cross_" : "_plain_";
                AddPiece(chunk, axis + kind + corner, center, StreetOrder, "Road");
            }
        }

        /// <summary>
        /// The part of a diagonal avenue that falls in this block: an end piece at each corner of the cell
        /// it starts or ends at, and middle pieces in between. Each piece belongs to the block holding its
        /// center and may hang over into the next block.
        /// </summary>
        void AddDiagonal(Chunk chunk)
        {
            if (chunk.Diagonal == Diagonal.None)
                return;
            bool rising = chunk.Diagonal == Diagonal.Rising;
            // Lay out a rising avenue, mirrored left to right for a falling one.
            void Place(string name, float x, float y)
            {
                var local = new Vector2(rising ? x : CellPixels - x, y);
                var world = chunk.CellOrigin + local / pixelsPerUnit;
                if (Layout.BlockAt(world) == chunk.Key)
                    AddPiece(chunk, name, (world - (Vector2)chunk.Root.transform.position) * pixelsPerUnit, StreetOverlayOrder, "Avenue");
            }

            const float half = DiagonalEnd * 0.5f;
            Place(rising ? "diag_rise_bl" : "diag_fall_br", half, half);
            Place(rising ? "diag_rise_tr" : "diag_fall_tl", CellPixels - half, CellPixels - half);
            for (int c = 2 * DiagonalStep; c <= CellPixels - 2 * DiagonalStep; c += DiagonalStep)
                Place(rising ? "diag_rise_mid" : "diag_fall_mid", c, c);
        }

        /// <summary>True when a footprint (in world units) would stand on this block's diagonal avenue.</summary>
        bool OnAvenue(Chunk chunk, Rect footprint)
        {
            if (chunk.Diagonal == Diagonal.None)
                return false;
            float reach = AvenueReach / pixelsPerUnit;
            var min = footprint.min - chunk.CellOrigin;
            var max = footprint.max - chunk.CellOrigin;
            // Offset across the avenue, measured along x: x - y for a rising one, x + y - cell for a falling one.
            float lo, hi;
            if (chunk.Diagonal == Diagonal.Rising)
            {
                lo = min.x - max.y;
                hi = max.x - min.y;
            }
            else
            {
                lo = min.x + min.y - StreetLayout.CellSize;
                hi = max.x + max.y - StreetLayout.CellSize;
            }
            return lo < reach && hi > -reach;
        }

        // ------------------------------------------------------------------ block contents

        /// <summary>
        /// Mostly downtown, with some parks, shrines and the odd Tokyo Tower. Merged blocks and blocks
        /// smaller than the nominal size are always downtown. Two special blocks of the same kind never
        /// touch: the one with the lower hash turns into downtown instead.
        /// </summary>
        BlockType BlockTypeAt(Vector2Int key)
        {
            var type = CandidateBlockType(key, out uint hash);
            if (type == BlockType.Downtown)
                return type;
            foreach (var offset in StreetLayout.Directions)
                if (CandidateBlockType(key + offset, out uint other) == type && other > hash)
                    return BlockType.Downtown;
            return type;
        }

        BlockType CandidateBlockType(Vector2Int key, out uint hash)
        {
            var rect = Layout.BlockRect(key);
            if (Layout.IsMerged(key) || Mathf.Min(rect.width, rect.height) < StreetLayout.BlockSize)
            {
                hash = 0;
                return BlockType.Downtown;
            }
            return RawBlockType(key, out hash);
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
        /// Lots sit on a 128 px lattice that runs through the whole city (blocks are whole numbers of lots
        /// across). A block owns the lots inside it, plus - where it merges with a neighbour on its left or
        /// bottom - the lots where the street would have been. Alleys get a row of tiny stalls on each side
        /// instead.
        /// </summary>
        void FillLots(Chunk chunk)
        {
            var key = chunk.Key;
            var left = Layout.VerticalEdge(key.x, key.y);
            var bottom = Layout.HorizontalEdge(key.x, key.y);
            bool cornerOpen = true;
            foreach (var direction in StreetLayout.Directions)
                cornerOpen &= Layout.Arm(key, direction) == Edge.Open;

            var firstLot = Vector2Int.RoundToInt((Vector2)chunk.Root.transform.position * pixelsPerUnit / LotPitch);
            for (int j = 0; j < chunk.Height / LotPitch; j++)
                for (int i = 0; i < chunk.Width / LotPitch; i++)
                {
                    bool owned = (i, j) switch
                    {
                        (0, 0) => cornerOpen,
                        (0, _) => left == Edge.Open,
                        (_, 0) => bottom == Edge.Open,
                        _ => true,
                    };
                    if (owned)
                        FillLot(chunk, new Vector2(i * LotPitch, j * LotPitch), firstLot + new Vector2Int(i, j));
                }

            if (left == Edge.Alley)
                AddStalls(chunk, false);
            if (bottom == Edge.Alley)
                AddStalls(chunk, true);
        }

        /// <summary>
        /// One big building, two halves, four small ones or an open lot. A lot cut by a diagonal avenue
        /// keeps whichever small buildings fit beside it.
        /// </summary>
        void FillLot(Chunk chunk, Vector2 center, Vector2Int slot)
        {
            var rng = new System.Random((int)StreetLayout.Hash(slot.x, slot.y, seed ^ 0x1B873593));
            const float quarter = HalfStride * 0.5f;
            double r = rng.NextDouble();
            if (OnAvenue(chunk, Footprint(chunk, center, Vector2.one * (LotPitch - 16))))
                r = 0.8;
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
                        AddDecor(chunk, decor["vending"], center + offset, "Vending machines");
                    else
                        AddBuilding(chunk, Pick(rng, "small"), center + offset);
                }
            }
            else
            {
                string name = rng.NextDouble() < 0.5 ? "parking" : "garden";
                AddDecor(chunk, decor[name], center, name);
            }
        }

        /// <summary>Tiny stalls on both sides of the alley along this block's left (or bottom) edge.</summary>
        void AddStalls(Chunk chunk, bool horizontalAlley)
        {
            var rng = new System.Random((int)StreetLayout.Hash(chunk.Key.x, chunk.Key.y, seed ^ (horizontalAlley ? 0x2545F491 : 0x4F1BBCDD)));
            int lots = (horizontalAlley ? chunk.Width : chunk.Height) / LotPitch;
            for (int j = 1; j < lots; j++)
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

        /// <summary>Add a smashable building, unless it would stand on a diagonal avenue.</summary>
        void AddBuilding(Chunk chunk, BuildingSprites sprites, Vector2 centerPixels)
        {
            var footprint = Footprint(chunk, centerPixels, sprites.Intact.rect.size - Vector2.one * (2f * FootprintInset));
            if (OnAvenue(chunk, footprint))
                return;
            var renderer = AddSprite(chunk, sprites.Intact, centerPixels, BuildingOrder, sprites.Intact.name);
            chunk.Buildings.Add(new Building { Footprint = footprint, Renderer = renderer, Rubble = sprites.Rubble });
        }

        void AddDecor(Chunk chunk, Sprite sprite, Vector2 centerPixels, string name)
        {
            if (!OnAvenue(chunk, Footprint(chunk, centerPixels, sprite.rect.size)))
                AddSprite(chunk, sprite, centerPixels, DecorOrder, name);
        }

        /// <summary>A rectangle in world units, from its center and size in chunk pixels.</summary>
        Rect Footprint(Chunk chunk, Vector2 centerPixels, Vector2 sizePixels)
        {
            return new Rect((Vector2)chunk.Root.transform.position + (centerPixels - sizePixels * 0.5f) / pixelsPerUnit,
                sizePixels / pixelsPerUnit);
        }

        /// <summary>Add a street piece by name; leaves a gap if the atlas doesn't have it.</summary>
        void AddPiece(Chunk chunk, string name, Vector2 centerPixels, int order, string label)
        {
            if (pieces.TryGetValue(name, out var sprite))
                AddSprite(chunk, sprite, centerPixels, order, label);
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
