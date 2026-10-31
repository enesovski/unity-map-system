using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;
using UnityEngine.UI;

namespace Artifika.Map.Core
{
    /// <summary>
    /// Draws the world's tile grid over the map so you can check, at a glance, that the art,
    /// the markers and the player all land on the tile they should.
    ///
    /// Lines are built from the same <see cref="MapProjector"/> the rest of the map uses, so if
    /// the grid lines up with the island art then the art is placed correctly — and if the
    /// highlighted tile matches where the player actually is in the world, the projection is right.
    ///
    /// Drawn as one thin image per grid line (28 for a 13×13 world), not per tile.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MapDebugGrid : MonoBehaviour
    {
        [Title("Setup")]
        [SerializeField, Required] private MapProjection projection;

        [Tooltip("Parent for the generated lines. Use a layer above the tiles so they stay readable.")]
        [SerializeField, Required] private RectTransform gridRoot;

        [Title("Toggle")]
        [Tooltip("Tick this at runtime to show the grid. Safe to leave off in builds.")]
        [SerializeField] private bool showGrid;

        [Title("Look")]
        [SerializeField, Min(0.5f)] private float lineThickness = 2f;
        [SerializeField] private Color lineColor = new(1f, 1f, 1f, 0.22f);
        [SerializeField] private Color originColor = new(1f, 0.85f, 0.2f, 0.9f);
        [SerializeField] private Color playerTileColor = new(0.3f, 1f, 0.5f, 0.25f);

        [Title("Debug")]
        [ShowInInspector, ReadOnly] private string playerTile = "—";

        private readonly List<GameObject> lines = new();
        private Image playerTileHighlight;
        private MapProjector projector;
        private bool built;

        private void OnDisable() => Clear();

        private void Update()
        {
            if (showGrid == built)
            {
                if (built) UpdatePlayerTile();
                return;
            }

            if (showGrid) Build();
            else Clear();
        }

        [Button("Rebuild Grid")]
        public void Rebuild()
        {
            Clear();
            if (showGrid) Build();
        }

        private void Build()
        {
            if (projection == null || gridRoot == null || !projection.HasGrid)
                return;

            projector = new MapProjector(projection);

            // Every distinct tile edge, so this stays correct even if the map is rotated.
            var xs = new SortedSet<float>();
            var ys = new SortedSet<float>();

            Vector2Int origin = projection.OriginTile;
            Vector2Int max = projection.MaxTile;

            for (int x = origin.x; x <= max.x; x++)
            {
                for (int y = origin.y; y <= max.y; y++)
                {
                    Rect r = projector.TileToMapLocalRect(new Vector2Int(x, y));
                    xs.Add(Mathf.Round(r.xMin));
                    xs.Add(Mathf.Round(r.xMax));
                    ys.Add(Mathf.Round(r.yMin));
                    ys.Add(Mathf.Round(r.yMax));
                }
            }

            Vector2 size = projection.MapPixelSize;

            foreach (float x in xs)
                CreateLine(new Vector2(x, 0f), new Vector2(lineThickness, size.y), lineColor, "GridV");

            foreach (float y in ys)
                CreateLine(new Vector2(0f, y), new Vector2(size.x, lineThickness), lineColor, "GridH");

            // World origin — the anchor everything else is measured from.
            Vector2 originPoint = projector.WorldToMapLocal(Vector3.zero);
            CreateLine(originPoint, new Vector2(lineThickness * 3f, size.y * 0.04f), originColor, "OriginY");
            CreateLine(originPoint, new Vector2(size.x * 0.04f, lineThickness * 3f), originColor, "OriginX");

            playerTileHighlight = CreateLine(Vector2.zero, Vector2.one, playerTileColor, "PlayerTile")
                .GetComponent<Image>();
            playerTileHighlight.enabled = false;

            built = true;
            UpdatePlayerTile();
        }

        private void UpdatePlayerTile()
        {
            if (playerTileHighlight == null || projector == null)
                return;

            if (!MapSystemContext.TryGetInitialized(out MapSystemContext context))
                return;

            EntityMarker player = context.EntityTracking?.GetLocalPlayer();
            if (player == null)
            {
                playerTileHighlight.enabled = false;
                playerTile = "no player";
                return;
            }

            Vector2Int tile = projector.WorldToTile(player.WorldPosition);
            Rect rect = projector.TileToMapLocalRect(tile);

            var rt = playerTileHighlight.rectTransform;
            rt.sizeDelta = rect.size;
            rt.anchoredPosition = rect.center;
            playerTileHighlight.enabled = true;

            playerTile = $"{tile}  ({player.WorldPosition.x:0}, {player.WorldPosition.z:0})";
        }

        private GameObject CreateLine(Vector2 position, Vector2 size, Color color, string name)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.layer = gridRoot.gameObject.layer;
            go.hideFlags = HideFlags.DontSave;

            var rt = go.GetComponent<RectTransform>();
            rt.SetParent(gridRoot, false);
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = size;
            rt.anchoredPosition = position;

            var image = go.GetComponent<Image>();
            image.color = color;
            image.raycastTarget = false;

            lines.Add(go);
            return go;
        }

        private void Clear()
        {
            foreach (GameObject line in lines)
            {
                if (line == null) continue;
                if (Application.isPlaying) Destroy(line);
                else DestroyImmediate(line);
            }

            lines.Clear();
            playerTileHighlight = null;
            projector = null;
            built = false;
            playerTile = "—";
        }
    }
}
