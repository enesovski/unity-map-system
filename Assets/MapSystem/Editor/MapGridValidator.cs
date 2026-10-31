using System.Collections.Generic;
using System.Text;
using Artifika.World;
using UnityEditor;
using UnityEngine;

namespace Artifika.Map.Core
{
    public enum MapValidationSeverity
    {
        Info,
        Warning,
        Error
    }

    public readonly struct MapValidationIssue
    {
        public MapValidationIssue(MapValidationSeverity severity, string category, string message, Object context = null)
        {
            Severity = severity;
            Category = category;
            Message = message;
            Context = context;
        }

        public MapValidationSeverity Severity { get; }
        public string Category { get; }
        public string Message { get; }
        public Object Context { get; }

        public override string ToString() => $"[{Severity}] {Category}: {Message}";
    }

    /// <summary>
    /// Checks that the map, the world plan and the built terrain all agree about where tiles are.
    ///
    /// This is what turns "is the map aligned?" from an argument into a report. Run it before and
    /// after any step that moves terrain or art — it names the exact tile or island at fault.
    ///
    /// Terrain checks use <c>Transform.position</c> (world space) deliberately: a scene's objects
    /// may sit under a parent transform, so local positions say nothing about grid alignment.
    /// </summary>
    public static class MapGridValidator
    {
        private const string MenuPath = "Elysium/Map/Validate Map Grid";
        private const float SnapToleranceMetres = 0.5f;

        [MenuItem(MenuPath)]
        public static void ValidateAndLog()
        {
            List<MapValidationIssue> issues = Validate(
                FindAsset<MapProjection>(),
                FindAsset<WorldLayout>(),
                FindAsset<MapTileSet>(),
                Object.FindFirstObjectByType<IslandPlanner>(FindObjectsInactive.Include));

            int errors = 0, warnings = 0;
            var sb = new StringBuilder();
            sb.AppendLine("[MapGridValidator] Report");

            foreach (MapValidationIssue issue in issues)
            {
                switch (issue.Severity)
                {
                    case MapValidationSeverity.Error: errors++; break;
                    case MapValidationSeverity.Warning: warnings++; break;
                }

                sb.AppendLine($"  {issue}");
            }

            sb.AppendLine($"  ── {errors} error(s), {warnings} warning(s), {issues.Count} check result(s).");

            if (errors > 0) Debug.LogError(sb.ToString());
            else if (warnings > 0) Debug.LogWarning(sb.ToString());
            else Debug.Log(sb.ToString());
        }

        public static List<MapValidationIssue> Validate(
            MapProjection projection,
            WorldLayout layout,
            MapTileSet tileSet,
            IslandPlanner planner,
            RectTransform mapSurface = null)
        {
            var issues = new List<MapValidationIssue>();

            ValidateProjection(projection, issues);
            ValidateLayout(layout, planner, issues);
            ValidateGridAgreement(projection, layout, planner, issues);
            ValidateCoverage(projection, layout, issues);
            ValidateTileArt(projection, layout, tileSet, issues);
            ValidateMapSurface(projection, mapSurface, issues);
            ValidateTerrainSnapping(layout, issues);
            ValidateIslandDefinitionLinks(layout, issues);

            if (issues.Count == 0)
                issues.Add(new MapValidationIssue(MapValidationSeverity.Info, "All", "Everything checks out."));

            return issues;
        }

        // ── checks ────────────────────────────────────────────────────────────

        private static void ValidateProjection(MapProjection projection, List<MapValidationIssue> issues)
        {
            if (projection == null)
            {
                issues.Add(new MapValidationIssue(MapValidationSeverity.Warning, "Projection",
                    "No MapProjection asset found. The map is falling back to WorldDefinition.WorldBoundsRect."));
                return;
            }

            if (projection.IsLegacy)
            {
                issues.Add(new MapValidationIssue(MapValidationSeverity.Info, "Projection",
                    $"Legacy bounds are ON — the map still uses the hand-tuned rectangle " +
                    $"{projection.WorldBounds}. Grid coverage settings are inactive until this is turned off.",
                    projection));
            }

            if (!projection.HasGrid)
            {
                issues.Add(new MapValidationIssue(MapValidationSeverity.Error, "Projection",
                    "MapProjection has no WorldGridSettings assigned — tile conversions will return zero.",
                    projection));
            }
        }

        private static void ValidateLayout(WorldLayout layout, IslandPlanner planner, List<MapValidationIssue> issues)
        {
            if (layout == null)
            {
                issues.Add(new MapValidationIssue(MapValidationSeverity.Error, "Layout",
                    "No WorldLayout asset found. Run Artifika ▸ World ▸ Extract World Layout."));
                return;
            }

            if (layout.grid == null)
            {
                issues.Add(new MapValidationIssue(MapValidationSeverity.Error, "Layout",
                    "WorldLayout has no WorldGridSettings assigned.", layout));
            }

            foreach (Vector2Int duplicate in layout.FindDuplicateTiles())
            {
                issues.Add(new MapValidationIssue(MapValidationSeverity.Error, "Layout",
                    $"Tile {duplicate} is claimed by more than one island.", layout));
            }

            if (planner == null)
            {
                issues.Add(new MapValidationIssue(MapValidationSeverity.Info, "Layout",
                    "IslandPlanner is not in a loaded scene, so the layout could not be checked for staleness. " +
                    "Open Scene_Bootstrap to include this check."));
                return;
            }

            if (!WorldLayoutExtractor.IsInSync(planner, layout, out string report))
            {
                issues.Add(new MapValidationIssue(MapValidationSeverity.Warning, "Layout",
                    $"WorldLayout is out of date with the planner — {report}", layout));
            }
        }

        private static void ValidateGridAgreement(
            MapProjection projection, WorldLayout layout, IslandPlanner planner, List<MapValidationIssue> issues)
        {
            WorldGridSettings mapGrid = projection != null ? projection.Grid : null;
            WorldGridSettings layoutGrid = layout != null ? layout.grid : null;

            if (mapGrid != null && layoutGrid != null && mapGrid != layoutGrid)
            {
                issues.Add(new MapValidationIssue(MapValidationSeverity.Error, "Grid",
                    $"The map and the world plan reference different WorldGridSettings assets " +
                    $"('{mapGrid.name}' vs '{layoutGrid.name}'). Tile coordinates will not mean the same thing."));
            }

            WorldGridSettings grid = mapGrid != null ? mapGrid : layoutGrid;
            if (grid == null || planner == null) return;

            if (!Mathf.Approximately(grid.tileSize, planner.TileSize))
            {
                issues.Add(new MapValidationIssue(MapValidationSeverity.Error, "Grid",
                    $"Tile size disagrees — grid asset says {grid.tileSize} m, the planner says {planner.TileSize} m.",
                    grid));
            }

            if (grid.tileCenteredOnOrigin != planner.TileCentered)
            {
                issues.Add(new MapValidationIssue(MapValidationSeverity.Error, "Grid",
                    $"Origin convention disagrees — grid asset tileCenteredOnOrigin is {grid.tileCenteredOnOrigin}, " +
                    $"the planner says {planner.TileCentered}. Everything would be offset by half a tile.",
                    grid));
            }
        }

        private static void ValidateCoverage(MapProjection projection, WorldLayout layout, List<MapValidationIssue> issues)
        {
            if (projection == null || layout == null || projection.IsLegacy) return;

            if (!layout.TryGetTileBounds(out Vector2Int min, out Vector2Int max))
            {
                issues.Add(new MapValidationIssue(MapValidationSeverity.Warning, "Coverage",
                    "The world plan has no painted tiles.", layout));
                return;
            }

            var outside = new List<Vector2Int>();
            foreach (Vector2Int tile in layout.AllTiles())
                if (!projection.ContainsTile(tile))
                    outside.Add(tile);

            if (outside.Count > 0)
            {
                issues.Add(new MapValidationIssue(MapValidationSeverity.Warning, "Coverage",
                    $"{outside.Count} painted tile(s) fall outside the map's coverage " +
                    $"({projection.OriginTile} … {projection.MaxTile}) and will be cropped. " +
                    $"Painted range is {min} … {max}. Use Sync Range to widen it.",
                    projection));
            }
        }

        private static void ValidateTileArt(
            MapProjection projection, WorldLayout layout, MapTileSet tileSet, List<MapValidationIssue> issues)
        {
            if (tileSet == null)
            {
                issues.Add(new MapValidationIssue(MapValidationSeverity.Info, "Art",
                    "No MapTileSet asset found. Tile art has not been migrated yet."));
                return;
            }

            foreach (Vector2Int duplicate in tileSet.FindDuplicateTiles())
            {
                issues.Add(new MapValidationIssue(MapValidationSeverity.Error, "Art",
                    $"Tile {duplicate} appears more than once in the tile set.", tileSet));
            }

            foreach (Vector2Int empty in tileSet.FindEmptyArt())
            {
                issues.Add(new MapValidationIssue(MapValidationSeverity.Warning, "Art",
                    $"Tile {empty} has an entry but no sprite — it will render blank.", tileSet));
            }

            if (layout == null) return;

            var painted = new HashSet<Vector2Int>(layout.AllTiles());

            foreach (MapTileArt entry in tileSet.tiles)
            {
                if (entry == null || painted.Contains(entry.tile)) continue;

                issues.Add(new MapValidationIssue(MapValidationSeverity.Warning, "Art",
                    $"Tile {entry.tile} has art but no island claims it in the world plan.", tileSet));
            }

            if (projection == null || projection.IsLegacy) return;

            int missing = 0;
            foreach (Vector2Int tile in painted)
                if (projection.ContainsTile(tile) && !tileSet.Contains(tile))
                    missing++;

            if (missing > 0)
            {
                issues.Add(new MapValidationIssue(MapValidationSeverity.Warning, "Art",
                    $"{missing} painted tile(s) inside the map's coverage have no art yet.", tileSet));
            }
        }

        private static void ValidateMapSurface(
            MapProjection projection, RectTransform mapSurface, List<MapValidationIssue> issues)
        {
            if (projection == null || mapSurface == null) return;

            if (mapSurface.pivot != new Vector2(0.5f, 0.5f))
            {
                issues.Add(new MapValidationIssue(MapValidationSeverity.Error, "Surface",
                    $"The map surface pivot is {mapSurface.pivot}, but the projector assumes a centred pivot " +
                    $"(0.5, 0.5). Markers and art would both be offset.", mapSurface));
            }

            Vector2 expected = projection.MapPixelSize;
            Vector2 actual = mapSurface.rect.size;

            if (Vector2.Distance(expected, actual) > 0.5f)
            {
                issues.Add(new MapValidationIssue(MapValidationSeverity.Warning, "Surface",
                    $"The map surface is {actual} px but the projection expects {expected} px. " +
                    $"Use Fit Map Rect.", mapSurface));
            }
        }

        private static void ValidateTerrainSnapping(WorldLayout layout, List<MapValidationIssue> issues)
        {
            if (layout?.grid == null) return;

            WorldGridSettings grid = layout.grid;
            Terrain[] terrains = Object.FindObjectsByType<Terrain>(FindObjectsInactive.Include, FindObjectsSortMode.None);

            if (terrains.Length == 0)
            {
                issues.Add(new MapValidationIssue(MapValidationSeverity.Info, "Terrain",
                    $"No terrain is loaded, so grid snapping could not be checked.{DescribeUncheckedScenes(layout)}"));
                return;
            }

            int offGrid = 0;
            foreach (Terrain terrain in terrains)
            {
                // World position, not local — a scene's roots may sit under a parent transform.
                Vector3 position = terrain.transform.position;

                bool snapped = grid.IsOnTileBoundary(position.x, SnapToleranceMetres) &&
                               grid.IsOnTileBoundary(position.z, SnapToleranceMetres);

                if (snapped) continue;

                offGrid++;
                if (offGrid > 5) continue;

                float dx = grid.DistanceToTileBoundary(position.x);
                float dz = grid.DistanceToTileBoundary(position.z);

                issues.Add(new MapValidationIssue(MapValidationSeverity.Warning, "Terrain",
                    $"'{terrain.name}' at ({position.x:0.##}, {position.z:0.##}) is off the tile grid " +
                    $"by ({dx:0.##}, {dz:0.##}) m. Run IslandSnapper.", terrain));
            }

            if (offGrid > 5)
            {
                issues.Add(new MapValidationIssue(MapValidationSeverity.Warning, "Terrain",
                    $"…and {offGrid - 5} more off-grid terrain(s). {offGrid} of {terrains.Length} total."));
            }
            else if (offGrid == 0)
            {
                issues.Add(new MapValidationIssue(MapValidationSeverity.Info, "Terrain",
                    $"All {terrains.Length} loaded terrain(s) sit on the tile grid."));
            }
        }

        /// <summary>
        /// Names the island scenes that exist but are not loaded, so an unchecked result reads as
        /// "load these to check" rather than "nothing to check".
        /// </summary>
        private static string DescribeUncheckedScenes(WorldLayout layout)
        {
            if (layout == null) return string.Empty;

            var unloaded = new List<string>();

            foreach (IslandLayout island in layout.islands)
            {
                if (island?.scenePaths == null) continue;

                foreach (string path in island.scenePaths)
                {
                    if (string.IsNullOrEmpty(path)) continue;

                    UnityEngine.SceneManagement.Scene scene =
                        UnityEditor.SceneManagement.EditorSceneManager.GetSceneByPath(path);

                    if (!scene.IsValid() || !scene.isLoaded)
                        unloaded.Add($"{island.name} ({System.IO.Path.GetFileNameWithoutExtension(path)})");
                }
            }

            if (unloaded.Count == 0)
                return " No island scenes are assigned in the world plan.";

            return $" Load these additively to include it: {string.Join(", ", unloaded)}.";
        }

        private static void ValidateIslandDefinitionLinks(WorldLayout layout, List<MapValidationIssue> issues)
        {
            if (layout == null) return;

            string[] guids = AssetDatabase.FindAssets($"t:{nameof(IslandDefinition)}");
            var definitions = new List<IslandDefinition>(guids.Length);

            foreach (string guid in guids)
            {
                var definition = AssetDatabase.LoadAssetAtPath<IslandDefinition>(AssetDatabase.GUIDToAssetPath(guid));
                if (definition != null) definitions.Add(definition);
            }

            if (definitions.Count == 0) return;

            foreach (IslandLayout island in layout.islands)
            {
                if (island == null || !island.IsBuilt) continue;

                if (string.IsNullOrWhiteSpace(island.islandId))
                {
                    IslandDefinition guess = definitions.Find(d =>
                        string.Equals(d.DisplayName, island.name, System.StringComparison.OrdinalIgnoreCase));

                    string hint = guess != null ? $" Looks like '{guess.IslandId}'." : string.Empty;

                    issues.Add(new MapValidationIssue(MapValidationSeverity.Warning, "Islands",
                        $"'{island.name}' is built but has no islandId, so it cannot be linked to its " +
                        $"IslandDefinition.{hint}", layout));
                    continue;
                }

                if (definitions.Find(d => string.Equals(d.IslandId, island.islandId,
                        System.StringComparison.OrdinalIgnoreCase)) == null)
                {
                    issues.Add(new MapValidationIssue(MapValidationSeverity.Warning, "Islands",
                        $"'{island.name}' has islandId '{island.islandId}' but no IslandDefinition uses that id.",
                        layout));
                }
            }
        }

        // ── helpers ───────────────────────────────────────────────────────────

        public static T FindAsset<T>() where T : ScriptableObject
        {
            string[] guids = AssetDatabase.FindAssets($"t:{typeof(T).Name}");
            if (guids.Length == 0) return null;

            return AssetDatabase.LoadAssetAtPath<T>(AssetDatabase.GUIDToAssetPath(guids[0]));
        }
    }
}
