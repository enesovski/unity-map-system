using System.Collections.Generic;
using UnityEngine;

namespace Artifika.Map.Core
{
    public static class MapFogMaskRasterizer
    {
        // AA rim width at the disc boundary, in texels. Everything inside is written
        // at full strength so the stored mask stays bright; the overlay shader owns
        // all soft watercolor feathering (_EdgeWidth / _EdgeSoftness / domain warp).
        private const float EdgeAntialiasTexels = 1.5f;

        public static bool RevealCircle(
            byte[] mask,
            int resolution,
            Vector2 uv,
            float radiusWorld,
            Rect worldBounds)
        {
            return RevealCircle(mask, resolution, uv, radiusWorld, worldBounds, null, 0, out _);
        }

        /// <summary>
        /// Reveals a disc and reports the texel rectangle that actually changed, so callers can
        /// upload just that patch instead of the whole mask. At 1024² a full upload is 1 MB per
        /// step; a reveal typically touches a couple of hundred bytes.
        /// </summary>
        public static bool RevealCircle(
            byte[] mask,
            int resolution,
            Vector2 uv,
            float radiusWorld,
            Rect worldBounds,
            out RectInt dirtyRect)
        {
            return RevealCircle(mask, resolution, uv, radiusWorld, worldBounds, null, 0, out dirtyRect);
        }

        public static bool RevealCircle(
            byte[] mask,
            int resolution,
            Vector2 uv,
            float radiusWorld,
            Rect worldBounds,
            ISet<int> dirtyTileIndices,
            int dirtyTileSize)
        {
            return RevealCircle(mask, resolution, uv, radiusWorld, worldBounds, dirtyTileIndices, dirtyTileSize, out _);
        }

        public static bool RevealCircle(
            byte[] mask,
            int resolution,
            Vector2 uv,
            float radiusWorld,
            Rect worldBounds,
            ISet<int> dirtyTileIndices,
            int dirtyTileSize,
            out RectInt dirtyRect)
        {
            dirtyRect = default;

            if (mask == null || mask.Length != resolution * resolution ||
                resolution <= 0 || radiusWorld <= 0f ||
                worldBounds.width <= 0f || worldBounds.height <= 0f)
            {
                return false;
            }

            uv = new Vector2(Mathf.Clamp01(uv.x), Mathf.Clamp01(uv.y));

            float centerX = uv.x * (resolution - 1);
            float centerY = uv.y * (resolution - 1);
            float radiusX = Mathf.Max(1f, radiusWorld / worldBounds.width * resolution);
            float radiusY = Mathf.Max(1f, radiusWorld / worldBounds.height * resolution);

            int minX = Mathf.Max(0, Mathf.FloorToInt(centerX - radiusX));
            int maxX = Mathf.Min(resolution - 1, Mathf.CeilToInt(centerX + radiusX));
            int minY = Mathf.Max(0, Mathf.FloorToInt(centerY - radiusY));
            int maxY = Mathf.Min(resolution - 1, Mathf.CeilToInt(centerY + radiusY));

            float feather = Mathf.Clamp01(EdgeAntialiasTexels / Mathf.Min(radiusX, radiusY));
            float solidUntil = 1f - feather;
            bool trackDirtyTiles = dirtyTileIndices != null && dirtyTileSize > 0;
            int tilesPerRow = trackDirtyTiles
                ? (resolution + dirtyTileSize - 1) / dirtyTileSize
                : 0;
            bool changed = false;
            int dirtyMinX = int.MaxValue, dirtyMinY = int.MaxValue;
            int dirtyMaxX = int.MinValue, dirtyMaxY = int.MinValue;

            for (int y = minY; y <= maxY; y++)
            {
                float normalizedY = (y - centerY) / radiusY;
                int rowStart = y * resolution;

                for (int x = minX; x <= maxX; x++)
                {
                    float normalizedX = (x - centerX) / radiusX;
                    float distance = Mathf.Sqrt(normalizedX * normalizedX + normalizedY * normalizedY);
                    if (distance >= 1f)
                        continue;

                    // Full strength across the core; only the outer ~1.5 texels ramp down.
                    float reveal = distance <= solidUntil
                        ? 1f
                        : 1f - (distance - solidUntil) / feather;

                    byte revealByte = (byte)Mathf.RoundToInt(Mathf.Clamp01(reveal) * byte.MaxValue);
                    int index = rowStart + x;

                    if (revealByte <= mask[index])
                        continue;

                    mask[index] = revealByte;
                    if (trackDirtyTiles)
                        dirtyTileIndices.Add((y / dirtyTileSize) * tilesPerRow + (x / dirtyTileSize));

                    if (x < dirtyMinX) dirtyMinX = x;
                    if (x > dirtyMaxX) dirtyMaxX = x;
                    if (y < dirtyMinY) dirtyMinY = y;
                    if (y > dirtyMaxY) dirtyMaxY = y;

                    changed = true;
                }
            }

            if (changed)
            {
                dirtyRect = new RectInt(
                    dirtyMinX,
                    dirtyMinY,
                    dirtyMaxX - dirtyMinX + 1,
                    dirtyMaxY - dirtyMinY + 1);
            }

            return changed;
        }
    }
}
