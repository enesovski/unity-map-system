using System;
using UnityEngine;

namespace Artifika.Map.Core
{
    /// <summary>
    /// Default <see cref="IMapProjector"/>. Plain C# — no MonoBehaviour, no scene, no play mode —
    /// so the editor tooling can place tile art with nothing else running.
    /// <see cref="MapSystemContext"/> holds one; it does not own the type.
    ///
    /// Orientation (yaw and flips) is applied here and nowhere else. Any other code that rotates
    /// map content is a bug, because it would rotate one consumer and not the others — which is
    /// exactly how the art and the markers ended up in different frames before this existed.
    /// </summary>
    public sealed class MapProjector : IMapProjector
    {
        private readonly MapProjection projection;

        public MapProjector(MapProjection projection)
        {
            this.projection = projection != null
                ? projection
                : throw new ArgumentNullException(nameof(projection));
        }

        public MapProjection Projection => projection;

        public Vector2 MapPixelSize => projection.MapPixelSize;

        // ── World → map ───────────────────────────────────────────────────────

        public Vector2 WorldToNormalized(Vector3 world) => WorldToNormalized(world.x, world.z);

        public Vector2 WorldToNormalized(float worldX, float worldZ)
        {
            Rect bounds = projection.WorldBounds;

            // Mathf.InverseLerp clamps to 0…1 — matching the behaviour this replaces.
            var normalized = new Vector2(
                Mathf.InverseLerp(bounds.xMin, bounds.xMax, worldX),
                Mathf.InverseLerp(bounds.yMin, bounds.yMax, worldZ));

            if (projection.EffectiveFlipX) normalized.x = 1f - normalized.x;
            if (projection.EffectiveFlipZ) normalized.y = 1f - normalized.y;

            return projection.EffectiveYaw.Rotate(normalized);
        }

        public Vector2 WorldToMapLocal(Vector3 world) => NormalizedToMapLocal(WorldToNormalized(world));

        public Vector2 WorldToMapLocal(float worldX, float worldZ) =>
            NormalizedToMapLocal(WorldToNormalized(worldX, worldZ));

        // ── Map → world ───────────────────────────────────────────────────────

        public Vector3 NormalizedToWorld(Vector2 normalized)
        {
            // Undo orientation in reverse order: yaw first, then the flips.
            normalized = projection.EffectiveYaw.RotateInverse(normalized);

            if (projection.EffectiveFlipX) normalized.x = 1f - normalized.x;
            if (projection.EffectiveFlipZ) normalized.y = 1f - normalized.y;

            Rect bounds = projection.WorldBounds;
            return new Vector3(
                Mathf.Lerp(bounds.xMin, bounds.xMax, normalized.x),
                0f,
                Mathf.Lerp(bounds.yMin, bounds.yMax, normalized.y));
        }

        public Vector3 MapLocalToWorld(Vector2 mapLocal) => NormalizedToWorld(MapLocalToNormalized(mapLocal));

        // ── Tiles ─────────────────────────────────────────────────────────────

        public Vector2Int WorldToTile(Vector3 world) =>
            projection.HasGrid ? projection.Grid.WorldToTile(world) : Vector2Int.zero;

        /// <summary>
        /// Where a tile's art belongs, in map-local pixels. Uses the same conversion as
        /// <see cref="WorldToMapLocal"/>, which is what keeps art and markers in one frame.
        ///
        /// A tile outside the projection's coverage produces a clamped, degenerate rect — the map
        /// validator reports those rather than silently drawing them at the edge.
        /// </summary>
        public Rect TileToMapLocalRect(Vector2Int tile)
        {
            if (!projection.HasGrid)
                return default;

            Rect worldRect = projection.Grid.TileRect(tile);

            Vector2 a = WorldToMapLocal(worldRect.xMin, worldRect.yMin);
            Vector2 b = WorldToMapLocal(worldRect.xMax, worldRect.yMax);

            // A quarter turn can swap which corner is which, so rebuild from min/max.
            return Rect.MinMaxRect(
                Mathf.Min(a.x, b.x),
                Mathf.Min(a.y, b.y),
                Mathf.Max(a.x, b.x),
                Mathf.Max(a.y, b.y));
        }

        // ── Scale ─────────────────────────────────────────────────────────────

        public float MetresToPixels(float metres) => metres * projection.PixelsPerMetre;

        public float PixelsToMetres(float pixels) => pixels * projection.MetresPerPixel;

        // ── Internals ─────────────────────────────────────────────────────────

        private Vector2 NormalizedToMapLocal(Vector2 normalized)
        {
            Vector2 size = projection.MapPixelSize;
            return new Vector2(
                (normalized.x - 0.5f) * size.x,
                (normalized.y - 0.5f) * size.y);
        }

        private Vector2 MapLocalToNormalized(Vector2 mapLocal)
        {
            Vector2 size = projection.MapPixelSize;
            return new Vector2(
                size.x > 0f ? mapLocal.x / size.x + 0.5f : 0.5f,
                size.y > 0f ? mapLocal.y / size.y + 0.5f : 0.5f);
        }
    }
}
