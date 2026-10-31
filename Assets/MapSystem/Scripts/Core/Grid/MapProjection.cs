using Artifika.World;
using Sirenix.OdinInspector;
using UnityEngine;

namespace Artifika.Map.Core
{
    /// <summary>
    /// Describes how the world is laid out on the map, in the same tile coordinates the world is
    /// planned in.
    ///
    /// This replaces the hand-tuned <c>MapWorldDefinition.WorldBoundsRect</c>. Because coverage is
    /// expressed as an integer tile range rather than a free rectangle, the map's world bounds are
    /// grid-aligned by construction — an off-grid origin is not a value you can enter.
    ///
    /// <b>Migration:</b> while <see cref="legacyBounds"/> is on, this asset reproduces the old
    /// hand-tuned rectangle exactly, so the projector can be rolled out with no visible change.
    /// Turning it off is the single switch that moves the map onto the real grid.
    /// </summary>
    [CreateAssetMenu(fileName = "MapProjection", menuName = "Map/Map Projection", order = 0)]
    public sealed class MapProjection : ScriptableObject
    {
        [Title("World Grid")]
        [Tooltip("The tile grid the world is planned on. Shared with IslandPlanner via WorldGridSettings.")]
        [SerializeField, Required] private WorldGridSettings grid;

        [Title("Coverage")]
        [Tooltip("Inclusive minimum tile the map covers. Integer coordinates — this is what keeps the map on-grid.")]
        [SerializeField] private Vector2Int originTile = new(-1, -1);

        [Tooltip("How many tiles the map spans, in X (world X) and Y (world Z).")]
        [SerializeField] private Vector2Int tileCount = new(3, 3);

        [Title("Presentation")]
        [Tooltip("UI pixels per world tile. A layout unit, not a quality setting — sharpness comes " +
                 "from source sprite resolution and the zoom range.")]
        [SerializeField, Min(1f)] private float pixelsPerTile = 360f;

        [Tooltip("Map north relative to world +Z. Applied to markers AND tile art, so the two cannot disagree.")]
        [SerializeField] private MapYaw yaw = MapYaw.None;

        [SerializeField] private bool flipX;
        [SerializeField] private bool flipZ;

        [Title("Migration")]
        [Tooltip("ON = reproduce the old hand-tuned rectangle exactly. This is the rollback switch.")]
        [SerializeField] private bool legacyBounds = true;

        [SerializeField, ShowIf(nameof(legacyBounds))]
        private Rect legacyWorldBounds = new(-859f, -2449.32f, 3072f, 3072f);

        [SerializeField, ShowIf(nameof(legacyBounds))]
        private Vector2 legacyMapPixelSize = new(1080f, 1080f);

        // ── Authoring surface ─────────────────────────────────────────────────

        public WorldGridSettings Grid => grid;
        public Vector2Int OriginTile => originTile;
        public Vector2Int TileCount => tileCount;
        public float PixelsPerTile => pixelsPerTile;
        public bool IsLegacy => legacyBounds;

        /// <summary>Legacy mode is a strict passthrough, so orientation is neutralised while it is on.</summary>
        public MapYaw EffectiveYaw => legacyBounds ? MapYaw.None : yaw;
        public bool EffectiveFlipX => !legacyBounds && flipX;
        public bool EffectiveFlipZ => !legacyBounds && flipZ;

        public bool HasGrid => grid != null;

        // ── Derived — cannot be authored, cannot drift ────────────────────────

        /// <summary>
        /// The world-space XZ footprint the map covers. Rect.y is world Z.
        /// Every edge is a multiple of the tile size unless legacy mode is on.
        /// </summary>
        public Rect WorldBounds
        {
            get
            {
                if (legacyBounds || grid == null)
                    return legacyWorldBounds;

                return grid.TileRangeRect(originTile, tileCount);
            }
        }

        /// <summary>
        /// Size of the map surface in UI pixels. Axes swap on a quarter turn, which matters the
        /// moment the tile range stops being square.
        /// </summary>
        public Vector2 MapPixelSize
        {
            get
            {
                if (legacyBounds)
                    return legacyMapPixelSize;

                var size = new Vector2(
                    Mathf.Max(1, tileCount.x) * pixelsPerTile,
                    Mathf.Max(1, tileCount.y) * pixelsPerTile);

                return yaw.SwapsAxes() ? new Vector2(size.y, size.x) : size;
            }
        }

        /// <summary>
        /// UI pixels per world metre. Rotation-invariant, which is what makes it correct for
        /// circular things like area-marker radii even on a non-square map.
        /// </summary>
        public float PixelsPerMetre
        {
            get
            {
                if (legacyBounds)
                {
                    float width = legacyWorldBounds.width;
                    return width > 0f ? legacyMapPixelSize.x / width : 0f;
                }

                return grid != null && grid.tileSize > 0f ? pixelsPerTile / grid.tileSize : 0f;
            }
        }

        public float MetresPerPixel
        {
            get
            {
                float pixelsPerMetre = PixelsPerMetre;
                return pixelsPerMetre > 0f ? 1f / pixelsPerMetre : 0f;
            }
        }

        /// <summary>Inclusive maximum tile covered by the map.</summary>
        public Vector2Int MaxTile => originTile + new Vector2Int(
            Mathf.Max(1, tileCount.x) - 1,
            Mathf.Max(1, tileCount.y) - 1);

        public bool ContainsTile(Vector2Int tile)
        {
            Vector2Int max = MaxTile;
            return tile.x >= originTile.x && tile.x <= max.x &&
                   tile.y >= originTile.y && tile.y <= max.y;
        }

        // ── Editor / tooling entry points ─────────────────────────────────────

        /// <summary>
        /// Sets coverage from a tile range. Used by the map tool's <c>Sync Range</c> so the
        /// designer never types a coordinate that the planner already knows.
        /// </summary>
        public void SetTileRange(Vector2Int minTile, Vector2Int maxTile)
        {
            originTile = Vector2Int.Min(minTile, maxTile);
            Vector2Int max = Vector2Int.Max(minTile, maxTile);
            tileCount = max - originTile + Vector2Int.one;
        }

        public void SetGrid(WorldGridSettings worldGrid) => grid = worldGrid;

        public void SetLegacyBounds(bool enabled) => legacyBounds = enabled;

        /// <summary>Builds a detached projection for tests and previews. Not an asset.</summary>
        public static MapProjection CreateRuntime(
            WorldGridSettings grid,
            Vector2Int originTile,
            Vector2Int tileCount,
            float pixelsPerTile = 360f,
            MapYaw yaw = MapYaw.None)
        {
            var projection = CreateInstance<MapProjection>();
            projection.grid = grid;
            projection.originTile = originTile;
            projection.tileCount = tileCount;
            projection.pixelsPerTile = pixelsPerTile;
            projection.yaw = yaw;
            projection.legacyBounds = false;
            return projection;
        }

        /// <summary>Builds a detached projection that reproduces the pre-refactor behaviour exactly.</summary>
        public static MapProjection CreateLegacy(Rect worldBounds, Vector2 mapPixelSize)
        {
            var projection = CreateInstance<MapProjection>();
            projection.legacyBounds = true;
            projection.legacyWorldBounds = worldBounds;
            projection.legacyMapPixelSize = mapPixelSize;
            return projection;
        }
    }
}
