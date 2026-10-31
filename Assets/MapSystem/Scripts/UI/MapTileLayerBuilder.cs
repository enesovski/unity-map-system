using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;
using UnityEngine.UI;

namespace Artifika.Map.Core
{
    /// <summary>
    /// Places the map's island art, one Image per world tile, using the same projector that
    /// places markers.
    ///
    /// This replaces the old GridLayoutGroup arrangement where a tile's position came from its
    /// sibling index and gaps needed placeholder objects. Here every tile states its own
    /// coordinate, so hierarchy order is irrelevant and a missing tile costs nothing.
    ///
    /// Rebuilding is an editor action, not something that happens at runtime — the tiles are
    /// static once built.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MapTileLayerBuilder : MonoBehaviour
    {
        private static readonly int FogMaskScaleId = Shader.PropertyToID("_FogMaskScale");

        [Title("Data")]
        [SerializeField, Required] private MapProjection projection;
        [SerializeField, Required] private MapTileSet tileSet;

        [Title("Scene")]
        [Tooltip("Parent for the generated tile Images. Its children are fully managed — anything " +
                 "else parented here will be destroyed on Rebuild.")]
        [SerializeField, Required] private RectTransform tileRoot;

        [Tooltip("The map surface these tiles live on. Resized to the projection by Fit Map Rect.")]
        [SerializeField] private RectTransform mapSurface;

        [Title("Options")]
        [Tooltip("Tile art never needs to receive clicks; markers do. Leaving this on keeps the " +
                 "raycast cost of the map proportional to marker count, not tile count.")]
        [SerializeField] private bool disableRaycastOnTiles = true;

        [Tooltip("Generate an ocean image behind the tiles, sized to the whole map.\n\n" +
                 "Leave this OFF when the map already has its own sea layer — P_Canvas_Map does, " +
                 "stretched to the viewport so it covers the view at any zoom, which is better " +
                 "than one tied to map size.\n\n" +
                 "Either way, island art is authored with a transparent background and overlays " +
                 "the sea, so a tile with no island needs no object at all. Filling empty tiles " +
                 "individually would be pure overdraw and would put a seam on every boundary.")]
        [SerializeField] private bool manageOceanBackdrop;

        [Tooltip("Optional. The ocean image beneath the tile layer. Generated when " +
                 "Manage Ocean Backdrop is on and this is empty; an existing one is resized in " +
                 "place and never reparented.")]
        [SerializeField] private Image oceanBackdrop;

        [Tooltip("Ocean size as a multiple of the map. Must exceed 1 because the view can be " +
                 "zoomed out past the map and panned beyond its edges — at the minimum zoom the " +
                 "visible area is larger than the map itself, so a sea sized to the map would " +
                 "leave the screen edges bare.\n\n" +
                 "Roughly aspect × (1 + edge pan allowance) ÷ fit margin is needed; 6 covers " +
                 "up to ultrawide.")]
        [SerializeField, Min(1f)] private float oceanCoverage = 6f;

        [Tooltip("Optional. The fog overlay graphic. Sized to match the ocean so unexplored sea " +
                 "outside the world still reads as fog rather than open water.")]
        [SerializeField] private Graphic fogLayer;

        [Tooltip("Fog quad size as a multiple of the map. Written to the fog material as " +
                 "_FogMaskScale so the mask still lands on the map's footprint.")]
        [SerializeField, Min(1f)] private float fogCoverage = 6f;

        public MapProjection Projection => projection;
        public MapTileSet TileSet => tileSet;
        public RectTransform TileRoot => tileRoot;
        public RectTransform MapSurface => mapSurface;

        public bool CanRebuild => projection != null && tileSet != null && tileRoot != null;

        /// <summary>Rebuilds every tile Image from the tile set. Returns how many were placed.</summary>
        public int Rebuild()
        {
            if (!CanRebuild)
            {
                GameLog.Warning("[MapTileLayerBuilder] Cannot rebuild — projection, tile set or tile root is missing.");
                return 0;
            }

            var projector = new MapProjector(projection);

            ClearTiles();

            int placed = 0;
            var drawn = new HashSet<Vector2Int>();

            // One ocean image behind everything, rather than one per empty tile. Island art is
            // authored with a transparent background, so the sea shows through wherever a tile
            // has no island on it — and stays seamless across tile boundaries.
            if (manageOceanBackdrop) EnsureOceanBackdrop();
            EnsureFogLayer();

            foreach (MapTileArt entry in tileSet.tiles)
            {
                if (entry == null || entry.art == null || !drawn.Add(entry.tile))
                    continue;

                Rect target = projector.TileToMapLocalRect(entry.tile);
                if (target.width <= 0f || target.height <= 0f)
                    continue;

                CreateTile(entry.tile, entry.art, target);
                placed++;
            }

            return placed;
        }

        /// <summary>
        /// Creates or updates the single sea image sitting under the tile layer, sized to the
        /// whole map surface.
        /// </summary>
        private void EnsureOceanBackdrop()
        {
            bool created = false;

            if (oceanBackdrop == null)
            {
                if (tileSet.defaultArt == null)
                    return;

                var go = new GameObject("MapOcean", typeof(RectTransform), typeof(Image));
                go.layer = tileRoot.gameObject.layer;
                go.transform.SetParent(tileRoot, worldPositionStays: false);
                oceanBackdrop = go.GetComponent<Image>();
                oceanBackdrop.sprite = tileSet.defaultArt;
                created = true;
            }

            // Centre-anchor and size to cover the whole pannable area. An existing sea keeps its
            // own sprite and its place in the hierarchy — we only correct its geometry.
            var rect = oceanBackdrop.rectTransform;
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            rect.localScale = Vector3.one;
            rect.localRotation = Quaternion.identity;
            rect.sizeDelta = projection.MapPixelSize * Mathf.Max(1f, oceanCoverage);

            oceanBackdrop.raycastTarget = false;

            if (created)
                rect.SetAsFirstSibling();
        }

        public void ClearTiles()
        {
            if (tileRoot == null) return;

            for (int i = tileRoot.childCount - 1; i >= 0; i--)
            {
                GameObject child = tileRoot.GetChild(i).gameObject;

                // The ocean is managed separately and outlives a rebuild.
                if (oceanBackdrop != null && child == oceanBackdrop.gameObject)
                    continue;

                if (Application.isPlaying) Destroy(child);
                else DestroyImmediate(child);
            }
        }

        /// <summary>Resizes the map surface to match the projection. Returns the size applied.</summary>
        public Vector2 FitMapSurface()
        {
            if (projection == null || mapSurface == null)
                return Vector2.zero;

            Vector2 size = projection.MapPixelSize;
            mapSurface.sizeDelta = size;
            return size;
        }

        /// <summary>
        /// Grows the fog quad past the map and tells the shader how far, so fog keeps covering
        /// the view when zoomed out or panned past the world's edge. Without this the fog stops
        /// at the map border and the sea beyond it reads as already-explored.
        /// </summary>
        private void EnsureFogLayer()
        {
            if (fogLayer == null) return;

            float coverage = Mathf.Max(1f, fogCoverage);

            var rect = fogLayer.rectTransform;
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            rect.localScale = Vector3.one;
            rect.localRotation = Quaternion.identity;
            rect.sizeDelta = projection.MapPixelSize * coverage;

            // The runtime clone made by MapFogTextureService inherits the asset's properties,
            // so writing it here is enough.
            Material material = fogLayer.material;
            if (material != null && material.HasProperty(FogMaskScaleId))
                material.SetFloat(FogMaskScaleId, coverage);
        }

        /// <summary>Tiles currently in the set that the projection does not cover.</summary>
        public List<Vector2Int> FindTilesOutsideCoverage()
        {
            var outside = new List<Vector2Int>();
            if (projection == null || tileSet == null || projection.IsLegacy)
                return outside;

            foreach (MapTileArt entry in tileSet.tiles)
                if (entry != null && !projection.ContainsTile(entry.tile))
                    outside.Add(entry.tile);

            return outside;
        }

        private void CreateTile(Vector2Int tile, Sprite art, Rect target)
        {
            var go = new GameObject($"MapTile_x{tile.x}_z{tile.y}", typeof(RectTransform), typeof(Image));
            go.layer = tileRoot.gameObject.layer;

            var rect = go.GetComponent<RectTransform>();
            rect.SetParent(tileRoot, worldPositionStays: false);
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = target.size;
            rect.anchoredPosition = target.center;
            rect.localScale = Vector3.one;
            rect.localRotation = Quaternion.identity;

            var image = go.GetComponent<Image>();
            image.sprite = art;
            image.raycastTarget = !disableRaycastOnTiles;
        }

#if UNITY_EDITOR
        public void EditorAssign(MapProjection newProjection, MapTileSet newTileSet, RectTransform root, RectTransform surface)
        {
            projection = newProjection;
            tileSet = newTileSet;
            tileRoot = root;
            mapSurface = surface;
        }
#endif
    }
}
