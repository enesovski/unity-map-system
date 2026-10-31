using UnityEngine;

namespace Artifika.Map.Core
{
    /// <summary>
    /// The one place world coordinates become map coordinates.
    ///
    /// Markers, area overlays, click-to-place, camera centring, fog sampling and the tile art all
    /// go through this. That shared path is what guarantees a marker and the art beneath it stay
    /// in agreement — before this existed, the conversion was written out longhand in five files
    /// and the tile art did not use it at all.
    ///
    /// Map-local coordinates are UI pixels measured from the centre of the map surface, which is
    /// what a RectTransform with a centred pivot expects for <c>anchoredPosition</c>.
    ///
    /// Implementations must be usable outside play mode — the editor tooling places tile art with
    /// no MapSystemContext running.
    /// </summary>
    public interface IMapProjector
    {
        MapProjection Projection { get; }

        /// <summary>Size of the map surface in UI pixels. Drives <c>islandsRect.sizeDelta</c>.</summary>
        Vector2 MapPixelSize { get; }

        /// <summary>Position within the map as 0…1, after orientation. Clamped to the map's bounds.</summary>
        Vector2 WorldToNormalized(Vector3 world);

        Vector3 NormalizedToWorld(Vector2 normalized);

        /// <summary>UI pixels from the centre of the map surface.</summary>
        Vector2 WorldToMapLocal(Vector3 world);

        /// <summary>Inverse of <see cref="WorldToMapLocal"/>. Returns y = 0.</summary>
        Vector3 MapLocalToWorld(Vector2 mapLocal);

        /// <summary>
        /// The world tile containing this position — the same coordinate the designer painted in
        /// IslandPlanner. Returns <c>Vector2Int.zero</c> when the projection has no grid assigned.
        /// </summary>
        Vector2Int WorldToTile(Vector3 world);

        /// <summary>
        /// Where a world tile's art belongs on the map surface, in the same space as
        /// <see cref="WorldToMapLocal"/>. Shares its code path, so art and markers cannot drift.
        /// </summary>
        Rect TileToMapLocalRect(Vector2Int tile);

        /// <summary>
        /// Converts a world distance to UI pixels. Rotation-invariant, so it stays correct for
        /// radii on a rotated or non-square map.
        /// </summary>
        float MetresToPixels(float metres);
    }
}
