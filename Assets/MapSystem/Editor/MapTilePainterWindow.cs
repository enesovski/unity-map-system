#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using Artifika.World;
using UnityEditor;
using UnityEngine;

namespace Artifika.Map.Core
{
    /// <summary>
    /// Visual editor for the map's island art: the world laid out as a tile grid, one sprite per
    /// 1024 m terrain tile, arranged exactly as it will appear in game.
    ///
    /// Built for artists first. Pick a sprite from the palette once and click tiles to paint it —
    /// no coordinates to type, no object picker to hunt through, no hierarchy to understand.
    /// Tiles with no art of their own show the tile set's default sprite (the ocean), so the map
    /// always reads as a whole world rather than a grid with holes.
    ///
    /// Performance: only cells inside the scroll viewport are drawn, so grid size costs nothing
    /// until you scroll to it. Thumbnails come from Unity's shared preview cache while zoomed out
    /// and switch to the source texture only when cells are large enough to need it — which
    /// culling already limits to a few dozen.
    /// </summary>
    public sealed class MapTilePainterWindow : EditorWindow
    {
        private const float MinCell = 28f;
        private const float MaxCell = 320f;
        private const float FullResThreshold = 150f;
        private const float FooterHeight = 62f;
        private const float PaletteThumb = 58f;

        [SerializeField] private MapProjection projection;
        [SerializeField] private MapTileSet tileSet;
        [SerializeField] private WorldLayout layout;
        [SerializeField] private DefaultAsset paletteFolder;

        [SerializeField] private float cellSize = 96f;
        [SerializeField] private bool showCoordinates = true;
        [SerializeField] private bool tintByIsland = true;
        [SerializeField] private bool showUnclaimedTiles;
        [SerializeField] private bool showPalette = true;
        [SerializeField] private int islandFilter = -1;

        private Vector2 scroll;
        private Vector2 paletteScroll;
        private string paletteSearch = string.Empty;
        private List<Sprite> palette;
        private string paletteFolderPath;

        private Sprite brush;
        private bool eraser;
        private Sprite clipboard;

        private Vector2Int? selected;
        private Vector2Int? hovered;
        private Vector2Int? lastPainted;
        private bool painting;

        private static Texture2D checkerTexture;
        private bool previewsPending;

        // Per-cell lookups. Without these every visible cell scanned every island's tile list and
        // every tile-set entry on every repaint — ~13k list probes a frame on a 13x13 world.
        private readonly Dictionary<Vector2Int, int> ownerByTile = new();
        private readonly Dictionary<Vector2Int, Sprite> artByTile = new();
        private bool lookupsValid;
        private string cachedStats;

        // GUIStyles were being constructed per cell per frame, which is the bulk of the garbage.
        private static GUIStyle coordStyle;
        private static GUIStyle centreTagStyle;

        [MenuItem("Elysium/Map/Map Tile Painter", priority = 30)]
        public static void Open()
        {
            var window = GetWindow<MapTilePainterWindow>("Map Tiles");
            window.minSize = new Vector2(620f, 480f);
            window.AutoResolve();
            window.Show();
        }

        private void OnEnable()
        {
            AutoResolve();
            AssetPreview.SetPreviewTextureCacheSize(512);
        }

        private void AutoResolve()
        {
            projection ??= FindAsset<MapProjection>();
            tileSet ??= FindAsset<MapTileSet>();
            layout ??= FindAsset<WorldLayout>();
            InvalidateLookups();
        }

        private void OnGUI()
        {
            DrawToolbar();

            if (layout == null || tileSet == null)
            {
                EditorGUILayout.HelpBox(
                    "Assign a World Layout and a Map Tile Set to begin. " +
                    "Create them from Elysium ▸ Map ▸ Map Grid Setup.",
                    MessageType.Info);
                DrawAssetFields();
                return;
            }

            if (showPalette) DrawPalette();

            if (!TryGetGridBounds(out Vector2Int min, out Vector2Int max))
            {
                EditorGUILayout.HelpBox("The world plan has no painted tiles yet.", MessageType.Warning);
                return;
            }

            previewsPending = false;
            EnsureLookups();
            HandleGlobalShortcuts();
            DrawGrid(min, max);
            DrawFooter();

            // Asset previews load asynchronously; keep repainting only while some are outstanding.
            if (previewsPending) Repaint();
        }

        // ── chrome ────────────────────────────────────────────────────────────

        private void DrawToolbar()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                if (GUILayout.Button("Assets", EditorStyles.toolbarDropDown, GUILayout.Width(56)))
                    ShowAssetMenu();

                showPalette = GUILayout.Toggle(showPalette, "Palette", EditorStyles.toolbarButton, GUILayout.Width(54f));

                GUILayout.Space(6f);
                GUILayout.Label("Zoom", EditorStyles.miniLabel, GUILayout.Width(34f));
                cellSize = GUILayout.HorizontalSlider(cellSize, MinCell, MaxCell, GUILayout.Width(96f));
                if (GUILayout.Button("Fit", EditorStyles.toolbarButton, GUILayout.Width(30f)))
                    FitToWindow();

                GUILayout.Space(6f);
                showCoordinates = GUILayout.Toggle(showCoordinates, "Coords", EditorStyles.toolbarButton, GUILayout.Width(50f));
                tintByIsland = GUILayout.Toggle(tintByIsland, "Islands", EditorStyles.toolbarButton, GUILayout.Width(52f));
                showUnclaimedTiles = GUILayout.Toggle(showUnclaimedTiles, "Open Sea", EditorStyles.toolbarButton, GUILayout.Width(66f));

                DrawIslandFilter();

                GUILayout.FlexibleSpace();

                if (GUILayout.Button("Auto-Assign By Filename", EditorStyles.toolbarButton, GUILayout.Width(146f)))
                    AutoAssignByFilename();

                DrawApplyButton();

                if (GUILayout.Button("?", EditorStyles.toolbarButton, GUILayout.Width(22f)))
                    ShowHelp();
            }
        }

        /// <summary>
        /// Painting edits the tile set; the map draws objects built from it. This is the step
        /// between, and the label calls out when the two have drifted apart.
        /// </summary>
        private void DrawApplyButton()
        {
            bool known = MapTileApplier.TryGetCounts(out int inSet, out int drawn);
            bool stale = known && inSet != drawn;

            Color previous = GUI.backgroundColor;
            if (stale) GUI.backgroundColor = new Color(1f, 0.82f, 0.35f);

            string label = stale
                ? $"Apply To Map  ({inSet - drawn:+0;-0} pending)"
                : "Apply To Map";

            if (GUILayout.Button(label, EditorStyles.toolbarButton, GUILayout.Width(150f)))
            {
                if (MapTileApplier.Apply(out string report))
                {
                    Debug.Log("[MapTilePainter] " + report);
                    ShowNotification(new GUIContent(report));
                }
                else
                {
                    Debug.LogWarning("[MapTilePainter] " + report);
                    EditorUtility.DisplayDialog("Could not apply tiles", report, "OK");
                }
            }

            GUI.backgroundColor = previous;
        }

        private void DrawIslandFilter()
        {
            if (layout == null) return;

            var names = new string[layout.islands.Count + 1];
            names[0] = "All Islands";
            for (int i = 0; i < layout.islands.Count; i++)
                names[i + 1] = IslandName(i);

            int current = islandFilter + 1;
            int next = EditorGUILayout.Popup(current, names, EditorStyles.toolbarPopup, GUILayout.Width(112f));
            if (next != current) islandFilter = next - 1;
        }

        private void DrawAssetFields()
        {
            projection = (MapProjection)EditorGUILayout.ObjectField("Projection", projection, typeof(MapProjection), false);
            tileSet = (MapTileSet)EditorGUILayout.ObjectField("Tile Set", tileSet, typeof(MapTileSet), false);
            layout = (WorldLayout)EditorGUILayout.ObjectField("World Layout", layout, typeof(WorldLayout), false);
        }

        private void ShowAssetMenu()
        {
            var menu = new GenericMenu();
            menu.AddItem(new GUIContent("Select Tile Set"), false, () => Ping(tileSet));
            menu.AddItem(new GUIContent("Select World Layout"), false, () => Ping(layout));
            menu.AddItem(new GUIContent("Select Projection"), false, () => Ping(projection));
            menu.AddSeparator("");
            menu.AddItem(new GUIContent("Re-find Assets"), false, () =>
            {
                projection = null; tileSet = null; layout = null; AutoResolve();
            });
            menu.ShowAsContext();
        }

        private static void ShowHelp()
        {
            EditorUtility.DisplayDialog(
                "Map Tile Painter",
                "PAINTING\n" +
                "  • Pick a sprite in the palette, then click or drag across tiles to paint it.\n" +
                "  • Alt+click a tile to pick up its sprite as the brush (eyedropper).\n" +
                "  • Ctrl/Cmd+C copies the selected tile, Ctrl/Cmd+V pastes onto another.\n" +
                "  • Delete clears the selected tile.\n" +
                "  • Drag sprites straight from the Project window onto a tile.\n\n" +
                "AUTO-ASSIGN BY FILENAME\n" +
                "  Matches sprites to tiles by their file name alone:\n" +
                "      MapTile_x-3_z2.png  →  tile (-3, 2)\n" +
                "  Point it at a folder and every correctly named sprite lands on its tile.\n" +
                "  Anything that does not match is reported, never silently skipped.\n" +
                "  Best for a fresh bulk export; use the palette for art named otherwise.\n\n" +
                "OCEAN\n" +
                "  The tile set's Default Art fills every covered tile with no art of its own,\n" +
                "  so the sea never has to be drawn tile by tile.",
                "Got it");
        }

        // ── palette ───────────────────────────────────────────────────────────

        private void DrawPalette()
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUI.BeginChangeCheck();
                    paletteFolder = (DefaultAsset)EditorGUILayout.ObjectField(
                        paletteFolder, typeof(DefaultAsset), false, GUILayout.Width(180f));
                    if (EditorGUI.EndChangeCheck()) RefreshPalette();

                    paletteSearch = EditorGUILayout.TextField(paletteSearch, EditorStyles.toolbarSearchField, GUILayout.Width(150f));

                    if (GUILayout.Button("Refresh", EditorStyles.miniButton, GUILayout.Width(56f)))
                        RefreshPalette();

                    GUILayout.Space(10f);

                    GUILayout.Label("Brush:", EditorStyles.miniBoldLabel, GUILayout.Width(40f));
                    DrawBrushSwatch();

                    if (GUILayout.Button(eraser ? "Erasing ✓" : "Eraser", EditorStyles.miniButton, GUILayout.Width(64f)))
                    {
                        eraser = !eraser;
                        if (eraser) brush = null;
                    }

                    using (new EditorGUI.DisabledScope(brush == null && !eraser))
                    {
                        if (GUILayout.Button("Clear", EditorStyles.miniButton, GUILayout.Width(46f)))
                        {
                            brush = null;
                            eraser = false;
                        }
                    }

                    GUILayout.FlexibleSpace();

                    GUILayout.Label("Ocean:", EditorStyles.miniBoldLabel, GUILayout.Width(44f));
                    EditorGUI.BeginChangeCheck();
                    var ocean = (Sprite)EditorGUILayout.ObjectField(tileSet.defaultArt, typeof(Sprite), false, GUILayout.Width(150f));
                    if (EditorGUI.EndChangeCheck())
                    {
                        Undo.RecordObject(tileSet, "Set Default Map Tile");
                        tileSet.defaultArt = ocean;
                        EditorUtility.SetDirty(tileSet);
                    }
                }

                DrawPaletteStrip();
            }
        }

        private void DrawBrushSwatch()
        {
            Rect rect = GUILayoutUtility.GetRect(30f, 18f, GUILayout.Width(30f), GUILayout.Height(18f));

            if (eraser)
            {
                EditorGUI.DrawRect(rect, new Color(0.7f, 0.3f, 0.3f, 0.8f));
                return;
            }

            if (brush == null)
            {
                EditorGUI.DrawRect(rect, new Color(0f, 0f, 0f, 0.25f));
                return;
            }

            DrawSpriteThumb(rect, brush);
        }

        private void DrawPaletteStrip()
        {
            EnsurePalette();

            if (palette == null || palette.Count == 0)
            {
                EditorGUILayout.LabelField(
                    paletteFolder == null
                        ? "Pick a folder of map sprites to build a palette — then click tiles to paint."
                        : "No sprites found in that folder.",
                    EditorStyles.miniLabel);
                return;
            }

            List<Sprite> filtered = string.IsNullOrWhiteSpace(paletteSearch)
                ? palette
                : palette.Where(s => s != null &&
                        s.name.IndexOf(paletteSearch, System.StringComparison.OrdinalIgnoreCase) >= 0).ToList();

            Rect strip = GUILayoutUtility.GetRect(0f, PaletteThumb + 18f, GUILayout.ExpandWidth(true));
            var content = new Rect(0f, 0f, filtered.Count * (PaletteThumb + 4f), PaletteThumb);

            paletteScroll = GUI.BeginScrollView(strip, paletteScroll, content);

            int first = Mathf.Max(0, Mathf.FloorToInt(paletteScroll.x / (PaletteThumb + 4f)));
            int last = Mathf.Min(filtered.Count - 1, Mathf.CeilToInt((paletteScroll.x + strip.width) / (PaletteThumb + 4f)));

            for (int i = first; i <= last; i++)
            {
                Sprite sprite = filtered[i];
                if (sprite == null) continue;

                var rect = new Rect(i * (PaletteThumb + 4f), 0f, PaletteThumb, PaletteThumb);
                DrawSpriteThumb(rect, sprite);

                if (brush == sprite)
                    DrawOutline(rect, new Color(1f, 0.78f, 0.2f), 2f);

                if (Event.current.type == EventType.MouseDown && rect.Contains(Event.current.mousePosition))
                {
                    brush = sprite;
                    eraser = false;
                    Event.current.Use();
                    Repaint();
                }

                GUI.Label(new Rect(rect.x, rect.yMax - 14f, rect.width, 14f), string.Empty,
                    new GUIStyle { normal = { background = null } });
            }

            GUI.EndScrollView();

            string label = filtered.Count == palette.Count
                ? $"{palette.Count} sprite(s) — click one, then click tiles to paint."
                : $"{filtered.Count} of {palette.Count} sprite(s) match '{paletteSearch}'.";
            EditorGUILayout.LabelField(label, EditorStyles.miniLabel);
        }

        private void EnsurePalette()
        {
            string path = paletteFolder != null ? AssetDatabase.GetAssetPath(paletteFolder) : null;
            if (path == paletteFolderPath && palette != null) return;

            paletteFolderPath = path;
            RefreshPalette();
        }

        private void RefreshPalette()
        {
            palette = new List<Sprite>();
            paletteFolderPath = paletteFolder != null ? AssetDatabase.GetAssetPath(paletteFolder) : null;

            if (string.IsNullOrEmpty(paletteFolderPath) || !AssetDatabase.IsValidFolder(paletteFolderPath))
                return;

            foreach (string guid in AssetDatabase.FindAssets("t:Sprite", new[] { paletteFolderPath }))
            {
                var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(AssetDatabase.GUIDToAssetPath(guid));
                if (sprite != null) palette.Add(sprite);
            }

            palette.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
        }

        // ── grid ──────────────────────────────────────────────────────────────

        private bool TryGetGridBounds(out Vector2Int min, out Vector2Int max)
        {
            min = max = default;
            bool any = false;

            foreach (Vector2Int tile in EnumerateRelevantTiles())
            {
                if (!any) { min = max = tile; any = true; continue; }
                min = Vector2Int.Min(min, tile);
                max = Vector2Int.Max(max, tile);
            }

            if (!any) return false;

            if (projection != null && !projection.IsLegacy)
            {
                min = Vector2Int.Min(min, projection.OriginTile);
                max = Vector2Int.Max(max, projection.MaxTile);
            }

            return true;
        }

        private IEnumerable<Vector2Int> EnumerateRelevantTiles()
        {
            if (layout != null)
            {
                for (int i = 0; i < layout.islands.Count; i++)
                {
                    if (islandFilter >= 0 && i != islandFilter) continue;

                    IslandLayout island = layout.islands[i];
                    if (island?.tiles == null) continue;

                    foreach (Vector2Int tile in island.tiles)
                        yield return tile;
                }
            }

            if (tileSet?.tiles == null || islandFilter >= 0) yield break;

            foreach (MapTileArt entry in tileSet.tiles)
                if (entry != null) yield return entry.tile;
        }

        private void DrawGrid(Vector2Int min, Vector2Int max)
        {
            int columns = max.x - min.x + 1;
            int rows = max.y - min.y + 1;

            Rect viewport = GUILayoutUtility.GetRect(
                0f, 0f, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
            viewport.height = Mathf.Max(40f, viewport.height - FooterHeight);

            var content = new Rect(0f, 0f, columns * cellSize, rows * cellSize);
            HandleZoomWheel(viewport);

            scroll = GUI.BeginScrollView(viewport, scroll, content);

            // Cull to the visible window — this is what keeps a large world cheap to draw.
            int firstCol = Mathf.Max(0, Mathf.FloorToInt(scroll.x / cellSize));
            int lastCol = Mathf.Min(columns - 1, Mathf.CeilToInt((scroll.x + viewport.width) / cellSize));
            int firstRow = Mathf.Max(0, Mathf.FloorToInt(scroll.y / cellSize));
            int lastRow = Mathf.Min(rows - 1, Mathf.CeilToInt((scroll.y + viewport.height) / cellSize));

            hovered = null;

            for (int row = firstRow; row <= lastRow; row++)
            {
                for (int col = firstCol; col <= lastCol; col++)
                {
                    // Row 0 is the TOP of the view, so world +Z reads upward as in the scene.
                    var tile = new Vector2Int(min.x + col, max.y - row);
                    var cell = new Rect(col * cellSize, row * cellSize, cellSize, cellSize);
                    DrawCell(cell, tile);
                }
            }

            GUI.EndScrollView();

            if (Event.current.type == EventType.MouseUp)
            {
                painting = false;
                lastPainted = null;
            }
        }

        private void HandleZoomWheel(Rect viewport)
        {
            Event e = Event.current;
            if (e.type != EventType.ScrollWheel || !e.control || !viewport.Contains(e.mousePosition))
                return;

            cellSize = Mathf.Clamp(cellSize - e.delta.y * 6f, MinCell, MaxCell);
            e.Use();
            Repaint();
        }

        /// <summary>
        /// Rebuilds the tile lookups. Cheap enough to redo whenever the data is touched, and it
        /// turns per-cell list scans into dictionary hits.
        /// </summary>
        private void EnsureLookups()
        {
            if (lookupsValid) return;

            ownerByTile.Clear();
            artByTile.Clear();

            if (layout != null)
            {
                for (int i = 0; i < layout.islands.Count; i++)
                {
                    IslandLayout island = layout.islands[i];
                    if (island?.tiles == null) continue;

                    foreach (Vector2Int tile in island.tiles)
                        ownerByTile[tile] = i;
                }
            }

            if (tileSet?.tiles != null)
            {
                foreach (MapTileArt entry in tileSet.tiles)
                    if (entry != null && entry.art != null)
                        artByTile[entry.tile] = entry.art;
            }

            cachedStats = null;
            lookupsValid = true;
        }

        private void InvalidateLookups() => lookupsValid = false;

        private int OwnerOf(Vector2Int tile) => ownerByTile.TryGetValue(tile, out int i) ? i : -1;

        private Sprite ArtOf(Vector2Int tile) => artByTile.TryGetValue(tile, out Sprite s) ? s : null;

        private void DrawCell(Rect cell, Vector2Int tile)
        {
            int owner = OwnerOf(tile);
            bool painted = owner >= 0;
            Sprite ownArt = ArtOf(tile);
            bool hasOwnArt = ownArt != null;
            bool covered = projection == null || projection.IsLegacy || projection.ContainsTile(tile);

            if (!painted && !hasOwnArt && !showUnclaimedTiles) return;
            if (islandFilter >= 0 && owner != islandFilter && !hasOwnArt) return;

            var inner = new Rect(cell.x + 1f, cell.y + 1f, cell.width - 2f, cell.height - 2f);

            if (hasOwnArt)
            {
                DrawSpriteThumb(inner, ownArt);
            }
            else if (tileSet.defaultArt != null && covered)
            {
                // Show what the built map will actually look like here — ocean, dimmed so it
                // still reads as "not authored yet".
                DrawSpriteThumb(inner, tileSet.defaultArt);
                EditorGUI.DrawRect(inner, new Color(0f, 0f, 0f, 0.32f));
                if (painted && inner.width >= 52f) DrawCentreTag(inner, "OCEAN");
            }
            else
            {
                DrawPlaceholder(inner, painted);
            }

            if (tintByIsland && painted && owner < layout.islands.Count)
            {
                Color tint = layout.islands[owner].color;
                if (hasOwnArt)
                {
                    EditorGUI.DrawRect(new Rect(inner.x, inner.y, inner.width, 2f), tint);
                }
                else
                {
                    tint.a = 0.20f;
                    EditorGUI.DrawRect(inner, tint);
                }
            }

            if (!covered)
                EditorGUI.DrawRect(new Rect(inner.x, inner.yMax - 3f, inner.width, 3f), new Color(0.9f, 0.2f, 0.2f, 0.9f));

            if (showCoordinates && cellSize >= 46f)
                DrawCoordinateLabel(inner, tile, hasOwnArt);

            if (selected.HasValue && selected.Value == tile)
                DrawOutline(inner, new Color(1f, 0.78f, 0.2f), 2f);
            else if (hovered.HasValue && hovered.Value == tile)
                DrawOutline(inner, new Color(1f, 1f, 1f, 0.45f), 1f);

            HandleCellEvents(cell, tile);
        }

        private static void EnsureStyles()
        {
            if (coordStyle != null) return;

            coordStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = Color.white }
            };

            centreTagStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = new Color(1f, 1f, 1f, 0.7f) },
                fontStyle = FontStyle.Bold
            };
        }

        private static void DrawCentreTag(Rect rect, string text)
        {
            EnsureStyles();
            GUI.Label(new Rect(rect.x, rect.center.y - 16f, rect.width, 16f), text, centreTagStyle);
        }

        private void DrawCoordinateLabel(Rect inner, Vector2Int tile, bool hasArt)
        {
            EnsureStyles();

            var labelRect = new Rect(inner.x, inner.yMax - 16f, inner.width, 15f);
            EditorGUI.DrawRect(labelRect, new Color(0f, 0f, 0f, hasArt ? 0.55f : 0.35f));

            GUI.Label(labelRect, $"{tile.x}, {tile.y}", coordStyle);
        }

        private void DrawPlaceholder(Rect rect, bool painted)
        {
            EnsureChecker();
            float tiles = Mathf.Max(2f, rect.width / 16f);
            GUI.DrawTextureWithTexCoords(rect, checkerTexture, new Rect(0f, 0f, tiles, tiles));

            if (painted && rect.width >= 52f) DrawCentreTag(rect, "NO ART");
        }

        private void DrawSpriteThumb(Rect rect, Sprite sprite)
        {
            if (sprite == null) { DrawPlaceholder(rect, false); return; }

            // Zoomed out: shared preview cache. Zoomed in: source texture, but culling means
            // only the few dozen cells actually on screen ever hit it.
            if (rect.width < FullResThreshold)
            {
                Texture2D preview = AssetPreview.GetAssetPreview(sprite);
                if (preview != null)
                {
                    GUI.DrawTexture(rect, preview, ScaleMode.ScaleAndCrop);
                    return;
                }

                if (AssetPreview.IsLoadingAssetPreview(sprite.GetInstanceID()))
                {
                    previewsPending = true;
                    DrawPlaceholder(rect, false);
                    return;
                }
            }

            Texture2D texture = sprite.texture;
            if (texture == null) { DrawPlaceholder(rect, false); return; }

            Rect tr = sprite.textureRect;
            var uv = new Rect(
                tr.x / texture.width, tr.y / texture.height,
                tr.width / texture.width, tr.height / texture.height);

            GUI.DrawTextureWithTexCoords(rect, texture, uv);
        }

        private static void DrawOutline(Rect rect, Color color, float thickness)
        {
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width, thickness), color);
            EditorGUI.DrawRect(new Rect(rect.x, rect.yMax - thickness, rect.width, thickness), color);
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, thickness, rect.height), color);
            EditorGUI.DrawRect(new Rect(rect.xMax - thickness, rect.y, thickness, rect.height), color);
        }

        // ── interaction ───────────────────────────────────────────────────────

        private void HandleCellEvents(Rect cell, Vector2Int tile)
        {
            Event e = Event.current;
            if (!cell.Contains(e.mousePosition)) return;

            hovered = tile;

            switch (e.type)
            {
                case EventType.MouseDown when e.button == 0 && e.alt:
                    brush = tileSet.GetArt(tile);
                    eraser = false;
                    selected = tile;
                    e.Use();
                    Repaint();
                    break;

                case EventType.MouseDown when e.button == 0:
                    selected = tile;
                    if (e.clickCount == 2 && brush == null && !eraser)
                    {
                        OpenSpritePicker(tile);
                    }
                    else if (brush != null || eraser)
                    {
                        painting = true;
                        PaintCell(tile);
                    }
                    e.Use();
                    Repaint();
                    break;

                case EventType.MouseDrag when e.button == 0 && painting:
                    if (lastPainted != tile) PaintCell(tile);
                    e.Use();
                    Repaint();
                    break;

                case EventType.MouseDown when e.button == 1:
                    selected = tile;
                    ShowCellContextMenu(tile);
                    e.Use();
                    break;

                case EventType.DragUpdated:
                    DragAndDrop.visualMode = FirstSpriteIn(DragAndDrop.objectReferences) != null
                        ? DragAndDropVisualMode.Copy
                        : DragAndDropVisualMode.Rejected;
                    e.Use();
                    break;

                case EventType.DragPerform:
                    DragAndDrop.AcceptDrag();
                    AssignDroppedSprites(tile, DragAndDrop.objectReferences);
                    selected = tile;
                    e.Use();
                    Repaint();
                    break;
            }
        }

        private void PaintCell(Vector2Int tile)
        {
            Undo.RecordObject(tileSet, eraser ? "Erase Map Tile" : "Paint Map Tile");

            if (eraser) ClearInternal(tile);
            else Assign(tile, brush);

            lastPainted = tile;
            EditorUtility.SetDirty(tileSet);
        }

        private void HandleGlobalShortcuts()
        {
            Event e = Event.current;
            if (e.type != EventType.KeyDown) return;

            bool ctrl = e.control || e.command;

            if (ctrl && e.keyCode == KeyCode.C && selected.HasValue)
            {
                clipboard = tileSet.GetArt(selected.Value);
                if (clipboard != null) { brush = clipboard; eraser = false; }
                ShowNotification(new GUIContent(clipboard != null
                    ? $"Copied {clipboard.name}"
                    : "That tile has no art to copy"));
                e.Use();
                return;
            }

            if (ctrl && e.keyCode == KeyCode.V && selected.HasValue && clipboard != null)
            {
                Undo.RecordObject(tileSet, "Paste Map Tile");
                Assign(selected.Value, clipboard);
                EditorUtility.SetDirty(tileSet);
                ShowNotification(new GUIContent($"Pasted {clipboard.name}"));
                e.Use();
                return;
            }

            if ((e.keyCode == KeyCode.Delete || e.keyCode == KeyCode.Backspace) && selected.HasValue)
            {
                Undo.RecordObject(tileSet, "Clear Map Tile");
                ClearInternal(selected.Value);
                EditorUtility.SetDirty(tileSet);
                e.Use();
            }
        }

        /// <summary>
        /// Dropping one sprite assigns it here. Dropping several assigns each by the tile in its
        /// file name, so an artist can drag a whole export in at once.
        /// </summary>
        private void AssignDroppedSprites(Vector2Int tile, Object[] dropped)
        {
            var sprites = new List<Sprite>();
            foreach (Object obj in dropped)
            {
                Sprite sprite = AsSprite(obj);
                if (sprite != null) sprites.Add(sprite);
            }

            if (sprites.Count == 0) return;

            Undo.RecordObject(tileSet, "Assign Map Tile Art");

            if (sprites.Count == 1)
            {
                Assign(tile, sprites[0]);
                brush = sprites[0];
                eraser = false;
            }
            else
            {
                int matched = 0, unmatched = 0;
                foreach (Sprite sprite in sprites)
                {
                    if (MapTileImporter.TryParseTile(sprite.name, out Vector2Int parsed))
                    {
                        Assign(parsed, sprite);
                        matched++;
                    }
                    else unmatched++;
                }

                Debug.Log($"[MapTilePainter] Dropped {sprites.Count} sprite(s): {matched} placed by file name, " +
                          $"{unmatched} skipped (name did not match MapTile_x#_z#).");
            }

            EditorUtility.SetDirty(tileSet);
        }

        private void Assign(Vector2Int tile, Sprite sprite)
        {
            if (sprite == null) { ClearInternal(tile); return; }

            if (tileSet.TryGet(tile, out MapTileArt entry))
                entry.art = sprite;
            else
                tileSet.tiles.Add(new MapTileArt
                {
                    tile = tile,
                    art = sprite,
                    islandId = ResolveIslandId(tile)
                });

            InvalidateLookups();
        }

        private void ClearInternal(Vector2Int tile)
        {
            for (int i = tileSet.tiles.Count - 1; i >= 0; i--)
                if (tileSet.tiles[i] != null && tileSet.tiles[i].tile == tile)
                    tileSet.tiles.RemoveAt(i);

            InvalidateLookups();
        }

        private string ResolveIslandId(Vector2Int tile)
        {
            if (layout == null) return null;
            int owner = OwnerOf(tile);
            return owner >= 0 ? layout.islands[owner].islandId : null;
        }

        private void ShowCellContextMenu(Vector2Int tile)
        {
            bool hasArt = tileSet.HasOwnArt(tile);
            var menu = new GenericMenu();

            menu.AddItem(new GUIContent("Assign Sprite…"), false, () => OpenSpritePicker(tile));

            if (hasArt)
            {
                menu.AddItem(new GUIContent("Copy"), false, () =>
                {
                    clipboard = tileSet.GetArt(tile);
                    brush = clipboard;
                    eraser = false;
                });
                menu.AddItem(new GUIContent("Pick Up As Brush"), false, () =>
                {
                    brush = tileSet.GetArt(tile);
                    eraser = false;
                });
            }
            else
            {
                menu.AddDisabledItem(new GUIContent("Copy"));
                menu.AddDisabledItem(new GUIContent("Pick Up As Brush"));
            }

            if (clipboard != null)
                menu.AddItem(new GUIContent("Paste"), false, () =>
                {
                    Undo.RecordObject(tileSet, "Paste Map Tile");
                    Assign(tile, clipboard);
                    EditorUtility.SetDirty(tileSet);
                });
            else
                menu.AddDisabledItem(new GUIContent("Paste"));

            menu.AddSeparator("");

            if (hasArt)
            {
                menu.AddItem(new GUIContent("Select Sprite In Project"), false, () => Ping(tileSet.GetArt(tile)));
                menu.AddItem(new GUIContent("Clear Tile"), false, () =>
                {
                    Undo.RecordObject(tileSet, "Clear Map Tile");
                    ClearInternal(tile);
                    EditorUtility.SetDirty(tileSet);
                });
            }
            else
            {
                menu.AddDisabledItem(new GUIContent("Select Sprite In Project"));
                menu.AddDisabledItem(new GUIContent("Clear Tile"));
            }

            menu.AddSeparator("");
            menu.AddItem(new GUIContent("Copy Expected File Name"), false, () =>
                EditorGUIUtility.systemCopyBuffer = MapTileImporter.BuildFileName(tile) + ".png");

            menu.ShowAsContext();
        }

        private void OpenSpritePicker(Vector2Int tile)
        {
            tileSet.TryGet(tile, out MapTileArt entry);
            EditorGUIUtility.ShowObjectPicker<Sprite>(entry?.art, false, string.Empty, PickerIdFor(tile));
        }

        private static int PickerIdFor(Vector2Int tile) => (tile.x * 73856093) ^ (tile.y * 19349663);

        // ── footer ────────────────────────────────────────────────────────────

        private void DrawFooter()
        {
            HandleObjectPickerResult();

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                Vector2Int? focus = hovered ?? selected;

                using (new EditorGUILayout.HorizontalScope())
                {
                    if (focus.HasValue)
                    {
                        Vector2Int tile = focus.Value;
                        int owner = OwnerOf(tile);
                        string island = owner >= 0 ? IslandName(owner) : "open sea";

                        GUILayout.Label($"Tile ({tile.x}, {tile.y})  ·  {island}",
                            EditorStyles.boldLabel, GUILayout.Width(210f));

                        tileSet.TryGet(tile, out MapTileArt entry);

                        EditorGUI.BeginChangeCheck();
                        var picked = (Sprite)EditorGUILayout.ObjectField(entry?.art, typeof(Sprite), false);
                        if (EditorGUI.EndChangeCheck())
                        {
                            Undo.RecordObject(tileSet, "Assign Map Tile Art");
                            Assign(tile, picked);
                            EditorUtility.SetDirty(tileSet);
                        }

                        GUILayout.Label(MapTileImporter.BuildFileName(tile) + ".png", EditorStyles.miniLabel);
                    }
                    else
                    {
                        GUILayout.Label("Pick a palette sprite, then click tiles to paint. Alt+click picks up a tile's sprite.",
                            EditorStyles.miniLabel);
                    }
                }

                GUILayout.Label(BuildStatsLine(), EditorStyles.miniLabel);
            }
        }

        private string BuildStatsLine()
        {
            // Recomputed only when the tile data changes, not every repaint.
            if (cachedStats != null) return cachedStats;

            int painted = 0, withArt = 0;
            foreach (Vector2Int tile in layout.AllTiles())
            {
                painted++;
                if (artByTile.ContainsKey(tile)) withArt++;
            }

            string ocean = tileSet.defaultArt != null
                ? $"ocean '{tileSet.defaultArt.name}'"
                : "no ocean sprite set";

            cachedStats =
                $"{withArt} of {painted} planned tiles have art  ·  {painted - withArt} on {ocean}" +
                "   |   Ctrl+scroll zoom · Alt+click eyedropper · Ctrl+C/V copy-paste · Del clear";

            return cachedStats;
        }

        private void HandleObjectPickerResult()
        {
            if (Event.current.commandName != "ObjectSelectorClosed") return;
            if (!selected.HasValue || PickerIdFor(selected.Value) != EditorGUIUtility.GetObjectPickerControlID()) return;

            Undo.RecordObject(tileSet, "Assign Map Tile Art");
            Assign(selected.Value, EditorGUIUtility.GetObjectPickerObject() as Sprite);
            EditorUtility.SetDirty(tileSet);
            Repaint();
        }

        // ── actions ───────────────────────────────────────────────────────────

        private void AutoAssignByFilename()
        {
            string folder = EditorUtility.OpenFolderPanel("Choose the map tile sprite folder", "Assets", "");
            if (string.IsNullOrEmpty(folder)) return;

            if (!folder.StartsWith(Application.dataPath))
            {
                EditorUtility.DisplayDialog("Outside the project",
                    "Pick a folder inside this Unity project's Assets directory.", "OK");
                return;
            }

            string relative = "Assets" + folder.Substring(Application.dataPath.Length).Replace('\\', '/');
            MapTileImporter.ImportResult result = MapTileImporter.Import(tileSet, relative);
            InvalidateLookups();

            Debug.Log($"[MapTilePainter] Import from '{relative}': {result}", tileSet);
            ShowNotification(new GUIContent(result.ToString()));
            Repaint();
        }

        private void FitToWindow()
        {
            if (!TryGetGridBounds(out Vector2Int min, out Vector2Int max)) return;

            int columns = Mathf.Max(1, max.x - min.x + 1);
            int rows = Mathf.Max(1, max.y - min.y + 1);

            float available = Mathf.Min(position.width - 24f, position.height - FooterHeight - 120f);
            cellSize = Mathf.Clamp(available / Mathf.Max(columns, rows), MinCell, MaxCell);
            scroll = Vector2.zero;
        }

        // ── helpers ───────────────────────────────────────────────────────────

        private static void Ping(Object asset)
        {
            if (asset == null) return;
            Selection.activeObject = asset;
            EditorGUIUtility.PingObject(asset);
        }

        private static Sprite FirstSpriteIn(Object[] objects)
        {
            foreach (Object obj in objects)
            {
                Sprite sprite = AsSprite(obj);
                if (sprite != null) return sprite;
            }
            return null;
        }

        private static Sprite AsSprite(Object obj)
        {
            if (obj is Sprite sprite) return sprite;
            if (obj is Texture2D texture)
                return AssetDatabase.LoadAssetAtPath<Sprite>(AssetDatabase.GetAssetPath(texture));
            return null;
        }

        private string IslandName(int index)
        {
            if (layout == null || index < 0 || index >= layout.islands.Count)
                return "open sea";

            string islandName = layout.islands[index]?.name;
            return string.IsNullOrWhiteSpace(islandName) ? $"Island {index + 1}" : islandName;
        }

        private static void EnsureChecker()
        {
            if (checkerTexture != null) return;

            checkerTexture = new Texture2D(2, 2, TextureFormat.RGBA32, false)
            {
                name = "MapTilePainterChecker",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Repeat,
                hideFlags = HideFlags.HideAndDontSave
            };

            var dark = new Color(0.16f, 0.17f, 0.19f);
            var light = new Color(0.22f, 0.23f, 0.26f);
            checkerTexture.SetPixels(new[] { dark, light, light, dark });
            checkerTexture.Apply();
        }

        private static T FindAsset<T>() where T : ScriptableObject
        {
            string[] guids = AssetDatabase.FindAssets($"t:{typeof(T).Name}");
            return guids.Length == 0
                ? null
                : AssetDatabase.LoadAssetAtPath<T>(AssetDatabase.GUIDToAssetPath(guids[0]));
        }
    }
}
#endif
