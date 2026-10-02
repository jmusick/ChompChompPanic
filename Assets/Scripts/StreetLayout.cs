using UnityEngine;

namespace ChompChompPanic
{
    /// <summary>What runs along one side of a city block (or leaves a junction diagonally).</summary>
    public enum Edge
    {
        /// <summary>A road with sidewalks.</summary>
        Road,
        /// <summary>An east-west canal (people can't cross it except on bridges).</summary>
        Canal,
        /// <summary>A narrow backstreet between two merged blocks.</summary>
        Alley,
        /// <summary>No street: two merged blocks run into each other.</summary>
        Open,
        /// <summary>No street at all (a diagonal direction with no avenue).</summary>
        None,
    }

    /// <summary>Which way a diagonal avenue crosses a 2 x 2 cell, corner to corner.</summary>
    public enum Diagonal
    {
        None,
        /// <summary>From the cell's bottom-left corner to its top-right one.</summary>
        Rising,
        /// <summary>From the cell's bottom-right corner to its top-left one.</summary>
        Falling,
    }

    /// <summary>
    /// The endless street network, as pure functions of a seed. Junctions (nodes) sit where numbered grid
    /// lines cross, and block (x, y) lies between vertical lines x and x + 1 and horizontal lines y and y + 1.
    ///
    /// The grid is grouped into 2 x 2 cells of <see cref="CellSize"/>. Lines on cell boundaries are evenly
    /// spaced; the line through the middle of each column (or row) of cells is shifted by a random amount,
    /// so blocks come in different sizes (multiples of <see cref="LotSize"/>).
    ///
    /// Within a cell, blocks may merge into 2 x 1, 1 x 2 or 2 x 2 superblocks, which removes the streets
    /// between them (they become alleys or open ground). Merges never cross a cell boundary and merged
    /// groups are always rectangles, so every junction is a crossroads, a T or a straight run - no dead
    /// ends or L-bends. Some east-west cell-boundary lines are canals instead of roads.
    ///
    /// Diagonal avenues run along cell diagonals. A cell on an avenue becomes one superblock cut corner to
    /// corner by the avenue, so avenues only meet other streets at cell corners, which are always
    /// crossroads. Avenues stop short of canals.
    /// </summary>
    public class StreetLayout
    {
        /// <summary>Nominal block size: the size of a block whose middle line isn't shifted.</summary>
        public const float BlockSize = 16f;
        public const float CellSize = 2f * BlockSize;
        /// <summary>Lots (and so block sizes and line shifts) come in multiples of this.</summary>
        public const float LotSize = 4f;
        /// <summary>Walkable half-width either side of a road's center line (road plus sidewalk).</summary>
        public const float RoadHalfWidth = 2f;
        /// <summary>Walkable half-width of an alley.</summary>
        public const float AlleyHalfWidth = 0.5f;
        /// <summary>Distance from a road's center line to the middle of each traffic lane (traffic keeps left, as in Japan).</summary>
        public const float CarLaneOffset = 0.75f;

        // Streets removed inside a 2 x 2 cell, as bit masks. Vertical lines between the cell's
        // left and right columns (bottom / top row); horizontal lines between its rows (left / right column).
        const int VerticalBottom = 1, VerticalTop = 2, HorizontalLeft = 4, HorizontalRight = 8;
        const int AllRemoved = VerticalBottom | VerticalTop | HorizontalLeft | HorizontalRight;
        static readonly (float weight, int removed)[] CellPatterns =
        {
            (0.40f, 0),                                                              // four single blocks
            (0.10f, VerticalBottom), (0.10f, VerticalTop), (0.07f, VerticalBottom | VerticalTop),
            (0.10f, HorizontalLeft), (0.10f, HorizontalRight), (0.07f, HorizontalLeft | HorizontalRight),
            (0.06f, AllRemoved),                                                     // 2 x 2 superblock
        };

        // How far a cell's middle line moves off center, in lots. Blocks end up 2 to 6 lots across.
        static readonly (float weight, int lots)[] MiddleShifts =
        {
            (0.40f, 0), (0.20f, -1), (0.20f, 1), (0.10f, -2), (0.10f, 2),
        };

        /// <summary>Roughly one cell diagonal in this many carries an avenue, in each direction.</summary>
        const float AvenueSpacing = 10f;
        /// <summary>Avenues come in runs of this many cells (some runs are left out), as a power of two.</summary>
        const int AvenueRunShift = 3;

        /// <summary>The four directions along the grid lines.</summary>
        public static readonly Vector2Int[] Directions = { Vector2Int.right, Vector2Int.up, Vector2Int.left, Vector2Int.down };

        /// <summary>The grid directions plus the four diagonals (a diagonal arm skips a cell's middle node).</summary>
        public static readonly Vector2Int[] AllDirections =
        {
            Vector2Int.right, Vector2Int.up, Vector2Int.left, Vector2Int.down,
            new(1, 1), new(-1, 1), new(-1, -1), new(1, -1),
        };

        readonly int seed;

        public StreetLayout(int seed)
        {
            this.seed = seed;
        }

        // ------------------------------------------------------------------ geometry

        /// <summary>World x of vertical line <paramref name="x"/>.</summary>
        public float LineX(int x) => LinePosition(x, 0x1F83D9AB);

        /// <summary>World y of horizontal line <paramref name="y"/>.</summary>
        public float LineY(int y) => LinePosition(y, 0x5BE0CD19);

        float LinePosition(int line, int salt)
        {
            int cell = line >> 1;
            float start = cell * CellSize;
            if ((line & 1) == 0)
                return start;
            float r = Hash(cell, salt, seed) / (float)uint.MaxValue;
            foreach (var (weight, lots) in MiddleShifts)
            {
                if (r < weight)
                    return start + BlockSize + lots * LotSize;
                r -= weight;
            }
            return start + BlockSize;
        }

        public Vector2 NodePosition(Vector2Int node) => new(LineX(node.x), LineY(node.y));

        /// <summary>The block containing a world position.</summary>
        public Vector2Int BlockAt(Vector2 position)
        {
            int cx = Mathf.FloorToInt(position.x / CellSize), cy = Mathf.FloorToInt(position.y / CellSize);
            return new Vector2Int(position.x < LineX(cx * 2 + 1) ? cx * 2 : cx * 2 + 1,
                position.y < LineY(cy * 2 + 1) ? cy * 2 : cy * 2 + 1);
        }

        /// <summary>A block's extent in world units, between the center lines of the streets around it.</summary>
        public Rect BlockRect(Vector2Int block)
        {
            float x = LineX(block.x), y = LineY(block.y);
            return new Rect(x, y, LineX(block.x + 1) - x, LineY(block.y + 1) - y);
        }

        /// <summary>The 2 x 2 cell a block belongs to.</summary>
        public static Vector2Int CellOf(Vector2Int block) => new(block.x >> 1, block.y >> 1);

        public static Vector2 CellOrigin(Vector2Int cell) => (Vector2)cell * CellSize;

        // ------------------------------------------------------------------ streets

        /// <summary>The street on the vertical line x, between blocks (x - 1, y) and (x, y).</summary>
        public Edge VerticalEdge(int x, int y)
        {
            if ((x & 1) == 1 && (RemovedInCell(x >> 1, y >> 1) & ((y & 1) == 0 ? VerticalBottom : VerticalTop)) != 0)
                return MergedEdge(x, y, 1);
            return Edge.Road;
        }

        /// <summary>The street on the horizontal line y, between blocks (x, y - 1) and (x, y).</summary>
        public Edge HorizontalEdge(int x, int y)
        {
            if ((y & 1) == 1)
            {
                if ((RemovedInCell(x >> 1, y >> 1) & ((x & 1) == 0 ? HorizontalLeft : HorizontalRight)) != 0)
                    return MergedEdge(x, y, 2);
                return Edge.Road;
            }
            return IsCanalLine(y) ? Edge.Canal : Edge.Road;
        }

        /// <summary>Canals run along some even east-west lines (never through the starting crossroads).</summary>
        bool IsCanalLine(int y)
        {
            return y != 0 && Hash(0x0CA7A1, y, seed) % 5 == 0;
        }

        int RemovedInCell(int cx, int cy)
        {
            if (DiagonalInCell(new Vector2Int(cx, cy)) != Diagonal.None)
                return AllRemoved;
            float r = Hash(cx, cy, seed ^ 0x3C6EF372) / (float)uint.MaxValue;
            foreach (var (weight, removed) in CellPatterns)
            {
                if (r < weight)
                    return removed;
                r -= weight;
            }
            return 0;
        }

        Edge MergedEdge(int x, int y, int salt)
        {
            // The avenue takes the place of the streets inside its cell, so no alleys there.
            if (DiagonalInCell(new Vector2Int(x >> 1, y >> 1)) != Diagonal.None)
                return Edge.Open;
            return Hash(x * 2 + salt, y, seed ^ 0x6A09E667) % 2 == 0 ? Edge.Alley : Edge.Open;
        }

        /// <summary>The diagonal avenue crossing a cell, if any.</summary>
        public Diagonal DiagonalInCell(Vector2Int cell)
        {
            // Keep avenues off canal lines, which would need diagonal bridges.
            if (IsCanalLine(cell.y * 2) || IsCanalLine(cell.y * 2 + 2))
                return Diagonal.None;
            bool rising = OnAvenue(cell.x - cell.y, cell.x, 0x2F4A1B3);
            bool falling = OnAvenue(cell.x + cell.y, cell.x, 0x71C9E05);
            if (rising && falling)
                return Hash(cell.x, cell.y, seed ^ 0x34E90C6C) % 2 == 0 ? Diagonal.Rising : Diagonal.Falling;
            return rising ? Diagonal.Rising : falling ? Diagonal.Falling : Diagonal.None;
        }

        bool OnAvenue(int line, int along, int salt)
        {
            if (Hash(line, 0x0A7E, seed ^ salt) / (float)uint.MaxValue >= 1f / AvenueSpacing)
                return false;
            return Hash(line, along >> AvenueRunShift, seed ^ salt ^ 0x55555555) / (float)uint.MaxValue < 0.7f;
        }

        /// <summary>
        /// The node at the other end of the street leaving <paramref name="node"/> in <paramref name="direction"/>
        /// (one of <see cref="AllDirections"/>). Diagonal arms cross a whole cell, so they skip two lines.
        /// </summary>
        public static Vector2Int Neighbor(Vector2Int node, Vector2Int direction)
        {
            return node + (direction.x != 0 && direction.y != 0 ? direction * 2 : direction);
        }

        /// <summary>The street leaving a junction towards a neighbouring node (or in a direction).</summary>
        public Edge Arm(Vector2Int node, Vector2Int towards)
        {
            var direction = new Vector2Int(System.Math.Sign(towards.x), System.Math.Sign(towards.y));
            if (direction.x != 0 && direction.y != 0)
            {
                // Avenues start and end at cell corners, where both line numbers are even.
                if ((node.x & 1) != 0 || (node.y & 1) != 0)
                    return Edge.None;
                var cell = new Vector2Int((node.x >> 1) + (direction.x > 0 ? 0 : -1), (node.y >> 1) + (direction.y > 0 ? 0 : -1));
                var diagonal = DiagonalInCell(cell);
                bool matches = direction.x == direction.y ? diagonal == Diagonal.Rising : diagonal == Diagonal.Falling;
                return matches ? Edge.Road : Edge.None;
            }
            if (direction == Vector2Int.up) return VerticalEdge(node.x, node.y);
            if (direction == Vector2Int.down) return VerticalEdge(node.x, node.y - 1);
            if (direction == Vector2Int.right) return HorizontalEdge(node.x, node.y);
            if (direction == Vector2Int.left) return HorizontalEdge(node.x - 1, node.y);
            return Edge.None;
        }

        public int RoadDegree(Vector2Int node)
        {
            int count = 0;
            foreach (var direction in AllDirections)
                if (Arm(node, direction) == Edge.Road)
                    count++;
            return count;
        }

        /// <summary>True when a block shares a side with another block (no street between them).</summary>
        public bool IsMerged(Vector2Int block)
        {
            return IsMergedEdge(VerticalEdge(block.x, block.y)) || IsMergedEdge(VerticalEdge(block.x + 1, block.y))
                || IsMergedEdge(HorizontalEdge(block.x, block.y)) || IsMergedEdge(HorizontalEdge(block.x, block.y + 1));
        }

        public static bool IsMergedEdge(Edge edge) => edge == Edge.Alley || edge == Edge.Open;

        public static bool IsWalkable(Edge edge) => edge == Edge.Road || edge == Edge.Alley;

        public static bool IsDrivable(Edge edge) => edge == Edge.Road;

        public static float HalfWidth(Edge edge) => edge == Edge.Alley ? AlleyHalfWidth : RoadHalfWidth;

        /// <summary>
        /// The closest point on a walkable (or, with <paramref name="roadsOnly"/>, drivable) street's center
        /// line to <paramref name="position"/>, and the two junctions at the ends of that street. Every block
        /// has a road on at least one of its left or right sides, so this always finds one.
        /// </summary>
        public (Vector2 point, Vector2Int nodeA, Vector2Int nodeB) SnapToStreet(Vector2 position, bool roadsOnly = false)
        {
            bool Usable(Edge edge) => roadsOnly ? IsDrivable(edge) : IsWalkable(edge);

            var block = BlockAt(position);
            var rect = BlockRect(block);

            float best = float.MaxValue;
            (Vector2, Vector2Int, Vector2Int) result = default;
            void Consider(bool walkable, float distance, Vector2 point, Vector2Int a, Vector2Int b)
            {
                if (walkable && distance < best)
                {
                    best = distance;
                    result = (point, a, b);
                }
            }

            Consider(Usable(VerticalEdge(block.x, block.y)), position.x - rect.xMin, new Vector2(rect.xMin, position.y),
                block, block + Vector2Int.up);
            Consider(Usable(VerticalEdge(block.x + 1, block.y)), rect.xMax - position.x, new Vector2(rect.xMax, position.y),
                block + Vector2Int.right, block + Vector2Int.one);
            Consider(Usable(HorizontalEdge(block.x, block.y)), position.y - rect.yMin, new Vector2(position.x, rect.yMin),
                block, block + Vector2Int.right);
            Consider(Usable(HorizontalEdge(block.x, block.y + 1)), rect.yMax - position.y, new Vector2(position.x, rect.yMax),
                block + Vector2Int.up, block + Vector2Int.one);

            var cell = CellOf(block);
            var diagonal = DiagonalInCell(cell);
            if (diagonal != Diagonal.None)
            {
                var corner = cell * 2;
                var (a, b) = diagonal == Diagonal.Rising
                    ? (corner, corner + new Vector2Int(2, 2))
                    : (corner + new Vector2Int(2, 0), corner + new Vector2Int(0, 2));
                Vector2 pa = NodePosition(a), pb = NodePosition(b);
                var along = pb - pa;
                float t = Mathf.Clamp01(Vector2.Dot(position - pa, along) / along.sqrMagnitude);
                var point = pa + along * t;
                Consider(true, Vector2.Distance(position, point), point, a, b);
            }
            return result;
        }

        public static uint Hash(int x, int y, int seed)
        {
            unchecked
            {
                uint h = (uint)seed * 0x9E3779B9u;
                h ^= (uint)x * 0x85EBCA6Bu;
                h = (h << 13) | (h >> 19);
                h ^= (uint)y * 0xC2B2AE35u;
                h ^= h >> 16;
                h *= 0x7FEB352Du;
                h ^= h >> 15;
                h *= 0x846CA68Bu;
                h ^= h >> 16;
                return h;
            }
        }
    }
}
