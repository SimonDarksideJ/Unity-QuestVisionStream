using System.Collections.Generic;
using UnityEngine;

namespace XRTraining.UI
{
    /// <summary>
    /// Generates white, 9-sliced rounded-rectangle sprites at runtime and caches
    /// them by corner radius. In the shipping uGUI project these would be a single
    /// shared 9-slice sprite asset (32px corners) tinted per state — here we bake
    /// them so the project needs no imported art. Tint via <c>Image.color</c>.
    /// </summary>
    public static class RoundedSprite
    {
        static readonly Dictionary<int, Sprite> _cache = new Dictionary<int, Sprite>();

        /// <summary>A rounded sprite with the given corner radius (px). radius 0 = plain rect.</summary>
        public static Sprite Get(int radius)
        {
            radius = Mathf.Max(0, radius);
            if (_cache.TryGetValue(radius, out var cached) && cached != null) return cached;

            // Texture is (2r+pad) square; border = r keeps corners crisp at any size.
            int pad = 2;
            int size = radius * 2 + pad;
            if (size < 4) size = 4;

            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = $"RoundedRect_{radius}",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave
            };

            var pixels = new Color32[size * size];
            float r = radius;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float a = CornerAlpha(x, y, size, r);
                    byte alpha = (byte)Mathf.RoundToInt(Mathf.Clamp01(a) * 255f);
                    pixels[y * size + x] = new Color32(255, 255, 255, alpha);
                }
            }
            tex.SetPixels32(pixels);
            tex.Apply(false, false);

            var border = new Vector4(radius, radius, radius, radius);
            var sprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f),
                100f, 0, SpriteMeshType.FullRect, border);
            sprite.name = $"RoundedRect_{radius}";
            sprite.hideFlags = HideFlags.HideAndDontSave;

            _cache[radius] = sprite;
            return sprite;
        }

        /// <summary>Anti-aliased coverage for a pixel, rounding only the four corners.</summary>
        static float CornerAlpha(int x, int y, int size, float r)
        {
            if (r <= 0f) return 1f;

            // Distance from pixel centre into the nearest corner's rounded region.
            float px = x + 0.5f;
            float py = y + 0.5f;

            float cx = px < r ? r : (px > size - r ? size - r : px);
            float cy = py < r ? r : (py > size - r ? size - r : py);

            float dx = px - cx;
            float dy = py - cy;
            float dist = Mathf.Sqrt(dx * dx + dy * dy);

            // 1px soft edge for anti-aliasing.
            return Mathf.Clamp01(r - dist + 0.5f);
        }
    }
}
