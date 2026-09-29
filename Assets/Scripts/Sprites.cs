using UnityEngine;

namespace ChompChompPanic
{
    /// <summary>Procedurally generated placeholder sprites, so the prototype needs no art assets.</summary>
    public static class Sprites
    {
        public const float GridCellSize = 2f;

        static Sprite circle;
        static Sprite gridCell;

        /// <summary>White circle, 1 world unit across, with a slightly darker rim.</summary>
        public static Sprite Circle
        {
            get
            {
                if (circle != null)
                    return circle;

                const int size = 256;
                const float rimWidth = 14f;
                var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
                {
                    name = "Circle",
                    filterMode = FilterMode.Bilinear,
                    wrapMode = TextureWrapMode.Clamp,
                };

                var pixels = new Color32[size * size];
                float center = (size - 1) * 0.5f;
                float radius = size * 0.5f;
                for (int y = 0; y < size; y++)
                {
                    for (int x = 0; x < size; x++)
                    {
                        float d = Mathf.Sqrt((x - center) * (x - center) + (y - center) * (y - center));
                        float alpha = Mathf.Clamp01(radius - d);                  // 1px anti-aliased edge
                        float rim = Mathf.Clamp01(d - (radius - rimWidth));       // soft rim blend
                        byte v = (byte)Mathf.Lerp(255f, 185f, rim);
                        pixels[y * size + x] = new Color32(v, v, v, (byte)(alpha * 255f));
                    }
                }
                tex.SetPixels32(pixels);
                tex.Apply();

                circle = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
                circle.name = "Circle";
                return circle;
            }
        }

        /// <summary>One faint grid cell (lines on left and bottom edges), for use with tiled draw mode.</summary>
        public static Sprite GridCell
        {
            get
            {
                if (gridCell != null)
                    return gridCell;

                const int size = 64;
                const int lineWidth = 2;
                var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
                {
                    name = "GridCell",
                    filterMode = FilterMode.Bilinear,
                    wrapMode = TextureWrapMode.Clamp,
                };

                var pixels = new Color32[size * size];
                for (int y = 0; y < size; y++)
                {
                    for (int x = 0; x < size; x++)
                    {
                        bool line = x < lineWidth || y < lineWidth;
                        // Opaque dark line rather than faint white: low-alpha white blends far too
                        // brightly in linear color space.
                        pixels[y * size + x] = line ? new Color32(27, 30, 41, 255) : new Color32(0, 0, 0, 0);
                    }
                }
                tex.SetPixels32(pixels);
                tex.Apply();

                gridCell = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f),
                    size / GridCellSize, 0, SpriteMeshType.FullRect);
                gridCell.name = "GridCell";
                return gridCell;
            }
        }
    }
}
