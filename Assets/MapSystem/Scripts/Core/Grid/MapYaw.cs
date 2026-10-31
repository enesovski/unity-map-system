using UnityEngine;

namespace Artifika.Map.Core
{
    /// <summary>
    /// How the map is oriented relative to world +Z. Quarter turns only — anything else would
    /// stop UI tile cells from lining up with world tiles, which is the point of the grid.
    /// </summary>
    public enum MapYaw
    {
        None = 0,
        CW90 = 90,
        CW180 = 180,
        CW270 = 270
    }

    /// <summary>
    /// Rotation helpers that operate in normalized map space — the unit square, where (0,0) is
    /// the map's minimum corner and (1,1) its maximum.
    ///
    /// Rotating in normalized space keeps the maths independent of the map's aspect ratio; the
    /// aspect is handled once, later, by <c>MapProjection.MapPixelSize</c> swapping its axes on a
    /// quarter turn. Doing it in pixel space instead would need the aspect in two places.
    /// </summary>
    public static class MapYawExtensions
    {
        /// <summary>True when this rotation exchanges the map's width and height.</summary>
        public static bool SwapsAxes(this MapYaw yaw) => yaw == MapYaw.CW90 || yaw == MapYaw.CW270;

        /// <summary>Rotates a normalized point clockwise about the map centre (0.5, 0.5).</summary>
        public static Vector2 Rotate(this MapYaw yaw, Vector2 normalized) => yaw switch
        {
            MapYaw.CW90  => new Vector2(normalized.y, 1f - normalized.x),
            MapYaw.CW180 => new Vector2(1f - normalized.x, 1f - normalized.y),
            MapYaw.CW270 => new Vector2(1f - normalized.y, normalized.x),
            _            => normalized
        };

        /// <summary>Undoes <see cref="Rotate"/>. Required for map-click → world position.</summary>
        public static Vector2 RotateInverse(this MapYaw yaw, Vector2 normalized) =>
            yaw.Inverse().Rotate(normalized);

        public static MapYaw Inverse(this MapYaw yaw) => yaw switch
        {
            MapYaw.CW90  => MapYaw.CW270,
            MapYaw.CW270 => MapYaw.CW90,
            _            => yaw
        };
    }
}
