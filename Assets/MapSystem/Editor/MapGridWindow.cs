using System.Collections.Generic;
using System.IO;
using Artifika.World;
using UnityEditor;
using UnityEngine;

namespace Artifika.Map.Core
{
    /// <summary>
    /// The map's setup surface: point it at the world plan, let it derive coverage, drop in art
    /// named by tile coordinate, rebuild, validate.
    ///
    /// Interaction deliberately mirrors <see cref="IslandPlannerEditor"/> — the team already knows
    /// that tool, and the point of this refactor is that the map and the world plan behave like one
    /// system rather than two.
    /// </summary>
    public sealed class MapGridWindow : EditorWindow
    {
        private const float CellSize = 26f;
        private const float CellGap = 2f;

        private MapProjection projection;
        private WorldLayout layout;
        private MapTileSet tileSet;
        private MapTileLayerBuilder builder;

        private DefaultAsset artFolder;
        private Vector2 scroll;
        private Vector2Int? hoveredTile;
        private List<MapValidationIssue> lastReport;

        [MenuItem("Elysium/Map/Map Grid Setup", priority = 31)]
        public static void Open()
        {
            MapGridWindow window = GetWindow<MapGridWindow>("Map Grid");
            window.minSize = new Vector2(420f, 520f);
            window.AutoResolve();
        }

        private void OnEnable() => AutoResolve();

        private void AutoResolve()
        {
            projection ??= MapGridValidator.FindAsset<MapProjection>();
            layout ??= MapGridValidator.FindAsset<WorldLayout>();
            tileSet ??= MapGridValidator.FindAsset<MapTileSet>();
            builder ??= FindFirstObjectByType<MapTileLayerBuilder>(FindObjectsInactive.Include);
        }

        private void OnGUI()
        {
            scroll = EditorGUILayout.BeginScrollView(scroll);

            DrawAssets();
            EditorGUILayout.Space();
            DrawCoverage();
            EditorGUILayout.Space();
            DrawArt();
            EditorGUILayout.Space();
            DrawGrid();
            EditorGUILayout.Space();
            DrawValidation();

            EditorGUILayout.EndScrollView();
        }

        // ── sections ──────────────────────────────────────────────────────────

        private void DrawAssets()
        {
            EditorGUILayout.LabelField("Assets", EditorStyles.boldLabel);

            projection = (MapProjection)EditorGUILayout.ObjectField("Projection", projection, typeof(MapProjection), false);
            layout = (WorldLayout)EditorGUILayout.ObjectField("World Layout", layout, typeof(WorldLayout), false);
            tileSet = (MapTileSet)EditorGUILayout.ObjectField("Tile Set", tileSet, typeof(MapTileSet), false);
            builder = (MapTileLayerBuilder)EditorGUILayout.ObjectField("Tile Builder", builder, typeof(MapTileLayerBuilder), true);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Find In Project")) { projection = null; layout = null; tileSet = null; builder = null; AutoResolve(); }
            if (GUILayout.Button("Create Missing Assets")) CreateMissingAssets();
            EditorGUILayout.EndHorizontal();

            if (projection != null && projection.IsLegacy)
            {
                EditorGUILayout.HelpBox(
                    "Legacy bounds are ON. The map still uses the old hand-tuned rectangle and the " +
                    "coverage settings below are inactive. Turn this off on the projection asset when " +
                    "the tile art is ready — that is the switch that moves the map onto the grid.",
                    MessageType.Info);
            }
        }

        private void DrawCoverage()
        {
            EditorGUILayout.LabelField("Coverage", EditorStyles.boldLabel);

            if (projection == null)
            {
                EditorGUILayout.HelpBox("Assign a MapProjection to edit coverage.", MessageType.Warning);
                return;
            }

            EditorGUILayout.LabelField("Tile range", $"{projection.OriginTile} … {projection.MaxTile}  " +
                                                     $"({projection.TileCount.x}×{projection.TileCount.y})");
            EditorGUILayout.LabelField("World bounds", projection.WorldBounds.ToString());
            EditorGUILayout.LabelField("Map surface", $"{projection.MapPixelSize.x} × {projection.MapPixelSize.y} px");
            EditorGUILayout.LabelField("Scale", $"{projection.PixelsPerMetre:0.####} px/m  ·  {projection.PixelsPerTile} px/tile");

            using (new EditorGUI.DisabledScope(layout == null))
            {
                if (GUILayout.Button("Sync Range From World Plan"))
                    SyncRange();
            }

            using (new EditorGUI.DisabledScope(builder == null || builder.MapSurface == null))
            {
                if (GUILayout.Button("Fit Map Rect To Projection"))
                {
                    Undo.RecordObject(builder.MapSurface, "Fit Map Rect");
                    Vector2 size = builder.FitMapSurface();
                    EditorUtility.SetDirty(builder.MapSurface);
                    Debug.Log($"[MapGrid] Map surface resized to {size}.", builder.MapSurface);
                }
            }
        }

        private void DrawArt()
        {
            EditorGUILayout.LabelField("Art", EditorStyles.boldLabel);

            artFolder = (DefaultAsset)EditorGUILayout.ObjectField("Sprite Folder", artFolder, typeof(DefaultAsset), false);

            EditorGUILayout.HelpBox(
                $"Sprites are matched by file name: {MapTileImporter.BuildFileName(new Vector2Int(-3, 2))}.png " +
                "→ tile (-3, 2). Anything that does not match is reported, never silently skipped.",
                MessageType.None);

            EditorGUILayout.BeginHorizontal();

            using (new EditorGUI.DisabledScope(tileSet == null || artFolder == null))
            {
                if (GUILayout.Button("Import Tiles"))
                    ImportTiles();
            }

            using (new EditorGUI.DisabledScope(layout == null || projection == null))
            {
                if (GUILayout.Button("Export Tile Sheet"))
                    ExportTileSheet();
            }

            EditorGUILayout.EndHorizontal();

            using (new EditorGUI.DisabledScope(builder == null || !builder.CanRebuild))
            {
                if (GUILayout.Button("Rebuild Tile Images"))
                {
                    Undo.RegisterFullObjectHierarchyUndo(builder.TileRoot.gameObject, "Rebuild Map Tiles");
                    int placed = builder.Rebuild();
                    EditorUtility.SetDirty(builder);
                    Debug.Log($"[MapGrid] Rebuilt {placed} tile image(s).", builder);
                }
            }
        }

        private void DrawGrid()
        {
            EditorGUILayout.LabelField("Tile Map", EditorStyles.boldLabel);

            if (layout == null)
            {
                EditorGUILayout.HelpBox("Assign a WorldLayout to see the tile map.", MessageType.Info);
                return;
            }

            if (!layout.TryGetTileBounds(out Vector2Int min, out Vector2Int max))
            {
                EditorGUILayout.HelpBox("The world plan has no painted tiles.", MessageType.Warning);
                return;
            }

            int width = max.x - min.x + 1;
            int height = max.y - min.y + 1;

            Rect area = GUILayoutUtility.GetRect(
                width * (CellSize + CellGap),
                height * (CellSize + CellGap));

            hoveredTile = null;

            // Draw top-down so +Z reads upward, matching the scene view.
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    var tile = new Vector2Int(min.x + x, max.y - y);
                    var cell = new Rect(
                        area.x + x * (CellSize + CellGap),
                        area.y + y * (CellSize + CellGap),
                        CellSize, CellSize);

                    DrawCell(cell, tile);
                }
            }

            string label = hoveredTile.HasValue
                ? DescribeTile(hoveredTile.Value)
                : "Hover a cell for details.";
            EditorGUILayout.LabelField(label, EditorStyles.miniLabel);

            EditorGUILayout.LabelField(
                "Filled = art assigned · Outlined = painted, no art · Red = outside map coverage",
                EditorStyles.miniLabel);

            if (Event.current.type == EventType.MouseMove) Repaint();
        }

        private void DrawCell(Rect cell, Vector2Int tile)
        {
            int owner = layout.OwnerIndexOf(tile);
            bool painted = owner >= 0;
            bool hasArt = tileSet != null && tileSet.Contains(tile);
            bool covered = projection == null || projection.IsLegacy || projection.ContainsTile(tile);

            if (!painted && !hasArt) return;

            Color baseColor = painted && owner < layout.islands.Count
                ? layout.islands[owner].color
                : Color.grey;

            Color fill = hasArt ? baseColor : new Color(baseColor.r, baseColor.g, baseColor.b, 0.18f);
            EditorGUI.DrawRect(cell, fill);

            if (!covered)
            {
                var stripe = new Rect(cell.x, cell.yMax - 3f, cell.width, 3f);
                EditorGUI.DrawRect(stripe, Color.red);
            }

            if (cell.Contains(Event.current.mousePosition))
            {
                hoveredTile = tile;
                EditorGUI.DrawRect(new Rect(cell.x, cell.y, cell.width, 2f), Color.white);
            }
        }

        private string DescribeTile(Vector2Int tile)
        {
            int owner = layout.OwnerIndexOf(tile);
            string island = owner >= 0 ? layout.islands[owner].name : "unclaimed";
            string art = tileSet != null && tileSet.Contains(tile) ? "art assigned" : "no art";
            string covered = projection == null || projection.IsLegacy || projection.ContainsTile(tile)
                ? "in coverage"
                : "OUTSIDE COVERAGE";

            return $"Tile {tile} — {island} · {art} · {covered}";
        }

        private void DrawValidation()
        {
            EditorGUILayout.LabelField("Validation", EditorStyles.boldLabel);

            if (GUILayout.Button("Validate"))
            {
                lastReport = MapGridValidator.Validate(
                    projection, layout, tileSet,
                    FindFirstObjectByType<IslandPlanner>(FindObjectsInactive.Include),
                    builder != null ? builder.MapSurface : null);
            }

            if (lastReport == null) return;

            foreach (MapValidationIssue issue in lastReport)
            {
                MessageType type = issue.Severity switch
                {
                    MapValidationSeverity.Error => MessageType.Error,
                    MapValidationSeverity.Warning => MessageType.Warning,
                    _ => MessageType.Info
                };

                EditorGUILayout.HelpBox($"{issue.Category}: {issue.Message}", type);

                if (issue.Context == null) continue;
                if (GUILayout.Button("Select", GUILayout.Width(60)))
                    Selection.activeObject = issue.Context;
            }
        }

        // ── actions ───────────────────────────────────────────────────────────

        private void SyncRange()
        {
            if (!layout.TryGetTileBounds(out Vector2Int min, out Vector2Int max))
            {
                Debug.LogWarning("[MapGrid] The world plan has no painted tiles to sync from.");
                return;
            }

            Undo.RecordObject(projection, "Sync Map Range");
            projection.SetTileRange(min, max);

            if (projection.Grid == null && layout.grid != null)
                projection.SetGrid(layout.grid);

            EditorUtility.SetDirty(projection);
            AssetDatabase.SaveAssetIfDirty(projection);

            Debug.Log($"[MapGrid] Coverage set to tiles {min} … {max} " +
                      $"({max.x - min.x + 1}×{max.y - min.y + 1}), map surface {projection.MapPixelSize}.",
                      projection);
        }

        private void ImportTiles()
        {
            string folderPath = AssetDatabase.GetAssetPath(artFolder);
            MapTileImporter.ImportResult result = MapTileImporter.Import(tileSet, folderPath);

            Debug.Log($"[MapGrid] Tile import from '{folderPath}': {result}", tileSet);
        }

        /// <summary>
        /// Writes a contact sheet naming every tile the map needs and which island owns it —
        /// the brief an artist works from.
        /// </summary>
        private void ExportTileSheet()
        {
            if (!layout.TryGetTileBounds(out Vector2Int min, out Vector2Int max))
                return;

            string path = EditorUtility.SaveFilePanel("Export Tile Sheet", "", "MapTileSheet", "txt");
            if (string.IsNullOrEmpty(path)) return;

            var sb = new System.Text.StringBuilder();
            sb.AppendLine("Elysium — map tile brief");
            sb.AppendLine($"Tile size: {(projection.Grid != null ? projection.Grid.tileSize : 0f)} m   " +
                          $"Art size: {projection.PixelsPerTile} px/tile");
            sb.AppendLine($"Painted range: {min} … {max}");
            sb.AppendLine();
            sb.AppendLine("file name                 tile        island        status");
            sb.AppendLine(new string('-', 68));

            int needed = 0;
            foreach (IslandLayout island in layout.islands)
            {
                if (island?.tiles == null) continue;

                var sorted = new List<Vector2Int>(island.tiles);
                sorted.Sort((a, b) => a.x != b.x ? a.x.CompareTo(b.x) : a.y.CompareTo(b.y));

                foreach (Vector2Int tile in sorted)
                {
                    bool hasArt = tileSet != null && tileSet.Contains(tile);
                    if (!hasArt) needed++;

                    sb.AppendLine($"{MapTileImporter.BuildFileName(tile) + ".png",-26}{tile.ToString(),-12}" +
                                  $"{island.name,-14}{(hasArt ? "done" : "NEEDED")}");
                }
            }

            sb.AppendLine();
            sb.AppendLine($"{needed} tile(s) still need art.");

            File.WriteAllText(path, sb.ToString());
            Debug.Log($"[MapGrid] Tile sheet written to {path} — {needed} tile(s) still need art.");
            EditorUtility.RevealInFinder(path);
        }

        private void CreateMissingAssets()
        {
            string folder = "Assets/Project/Systems/MapSystem/Data";
            if (!AssetDatabase.IsValidFolder(folder))
                folder = "Assets";

            if (projection == null)
            {
                projection = CreateAsset<MapProjection>(folder, "MapProjection");
                if (layout != null && layout.grid != null) projection.SetGrid(layout.grid);
                EditorUtility.SetDirty(projection);
            }

            if (tileSet == null)
                tileSet = CreateAsset<MapTileSet>(folder, "MapTileSet");

            AssetDatabase.SaveAssets();
        }

        private static T CreateAsset<T>(string folder, string fileName) where T : ScriptableObject
        {
            var asset = CreateInstance<T>();
            string path = AssetDatabase.GenerateUniqueAssetPath($"{folder}/{fileName}.asset");
            AssetDatabase.CreateAsset(asset, path);
            Debug.Log($"[MapGrid] Created {path}", asset);
            return asset;
        }
    }
}
