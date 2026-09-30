using UnityEngine;

namespace ChompChompPanic
{
    /// <summary>What runs along one side of a city block.</summary>
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
    }

    /// <summary>
    /// The endless street network, as pure functions of a seed. Blocks sit on a grid (block (x, y)
    /// covers [x, x+1) x [y, y+1) in block units) and streets run along the grid lines between them.
    ///
    /// The grid is grouped into 2 x 2 cells. Within a cell, blocks may merge into 2 x 1, 1 x 2 or
    /// 2 x 2 superblocks, which removes the streets between them (they become alleys or open ground).
    /// Merges never cross a cell boundary and merged groups are always rectangles, so every junction is
    /// a crossroads, a T or a straight run - no dead ends or L-bends. Some east-west cell-boundary lines
    /// are canals instead of roads.
    /// </summary>
    public class StreetLayout
    {
        public const float BlockSize = 16f;
        /// <summary>Walkable half-width either side of a road's center line (road plus sidewalk).</summary>
        public const float RoadHalfWidth = 2f;
        /// <summary>Walkable half-width of an alley.</summary>
        public const float AlleyHalfWidth = 0.5f;
        /// <summary>Distance from a road's center line to the middle of each traffic lane (traffic keeps left, as in Japan).</summary>
        public const float CarLaneOffset = 0.75f;

        // Streets removed inside a 2 x 2 cell, as bit masks. Vertical lines between the cell's
        // left and right columns (bottom / top row); horizontal lines between its rows (left / right column).
        const int VerticalBottom = 1, VerticalTop = 2, HorizontalLeft = 4, HorizontalRight = 8;
        static readonly (float weight, int removed)[] CellPatterns =
        {
            (0.40f, 0),                                                              // four single blocks
            (0.10f, VerticalBottom), (0.10f, VerticalTop), (0.07f, VerticalBottom | VerticalTop),
            (0.10f, HorizontalLeft), (0.10f, HorizontalRight), (0.07f, HorizontalLeft | HorizontalRight),
            (0.06f, VerticalBottom | VerticalTop | HorizontalLeft | HorizontalRight),  // 2 x 2 superblock
        };

        public static readonly Vector2Int[] Directions = { Vector2Int.right, Vector2Int.up, Vector2Int.left, Vector2Int.down };

        readonly int seed;

        public StreetLayout(int seed)
        {
            this.seed = seed;
        }

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
            return Hash(x * 2 + salt, y, seed ^ 0x6A09E667) % 2 == 0 ? Edge.Alley : Edge.Open;
        }

        /// <summary>The street leaving a junction in the given direction.</summary>
        public Edge Arm(Vector2Int node, Vector2Int direction)
        {
            if (direction == Vector2Int.up) return VerticalEdge(node.x, node.y);
            if (direction == Vector2Int.down) return VerticalEdge(node.x, node.y - 1);
            if (direction == Vector2Int.right) return HorizontalEdge(node.x, node.y);
            return HorizontalEdge(node.x - 1, node.y);
        }

        public int RoadDegree(Vector2Int node)
        {
            int count = 0;
            foreach (var direction in Directions)
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

        public static Vector2 NodePosition(Vector2Int node) => (Vector2)node * BlockSize;

        /// <summary>
        /// The closest point on a walkable (or, with <paramref name="roadsOnly"/>, drivable) street's center
        /// line to <paramref name="position"/>, and the two junctions at the ends of that street. Every block
        /// has a road on at least one of its left or right sides, so this always finds one.
        /// </summary>
        public (Vector2 point, Vector2Int nodeA, Vector2Int nodeB) SnapToStreet(Vector2 position, bool roadsOnly = false)
        {
            bool Usable(Edge edge) => roadsOnly ? IsDrivable(edge) : IsWalkable(edge);

            var block = new Vector2Int(Mathf.FloorToInt(position.x / BlockSize), Mathf.FloorToInt(position.y / BlockSize));
            var local = position / BlockSize - block;

            float best = float.MaxValue;
            (Vector2, Vector2Int, Vector2Int) result = default;
            void Consider(bool walkable, float distance, Vector2 point, Vector2Int a, Vector2Int b)
            {
                if (walkable && distance < best)
                {
                    best = distance;
                    result = (point * BlockSize, a, b);
                }
            }

            Consider(Usable(VerticalEdge(block.x, block.y)), local.x, new Vector2(block.x, block.y + local.y),
                block, block + Vector2Int.up);
            Consider(Usable(VerticalEdge(block.x + 1, block.y)), 1f - local.x, new Vector2(block.x + 1, block.y + local.y),
                block + Vector2Int.right, block + Vector2Int.one);
            Consider(Usable(HorizontalEdge(block.x, block.y)), local.y, new Vector2(block.x + local.x, block.y),
                block, block + Vector2Int.right);
            Consider(Usable(HorizontalEdge(block.x, block.y + 1)), 1f - local.y, new Vector2(block.x + local.x, block.y + 1),
                block + Vector2Int.up, block + Vector2Int.one);
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
