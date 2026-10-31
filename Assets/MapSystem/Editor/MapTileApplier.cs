using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Artifika.Map.Core
{
    /// <summary>
    /// Pushes the tile set into the actual map.
    ///
    /// Painting only edits the <see cref="MapTileSet"/> asset; the map draws real Image objects
    /// built from it. This is the step that turns one into the other, and it has to reach into
    /// the map prefab because the canvas is spawned at runtime rather than living in a scene.
    /// </summary>
    public static class MapTileApplier
    {
        /// <summary>How many tiles the tile set holds versus how many the map currently draws.</summary>
        public static bool TryGetCounts(out int inTileSet, out int drawn)
        {
            inTileSet = 0;
            drawn = 0;

            MapTileSet tileSet = MapGridValidator.FindAsset<MapTileSet>();
            if (tileSet?.tiles != null)
                foreach (MapTileArt entry in tileSet.tiles)
                    if (entry != null && entry.art != null) inTileSet++;

            MapTileLayerBuilder builder = FindInLoadedScenes();
            if (builder != null)
            {
                drawn = CountTiles(builder);
                return true;
            }

            string path = FindPrefabPath();
            if (string.IsNullOrEmpty(path)) return false;

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            builder = prefab != null ? prefab.GetComponentInChildren<MapTileLayerBuilder>(true) : null;
            if (builder == null) return false;

            drawn = CountTiles(builder);
            return true;
        }

        public static bool Apply(out string report)
        {
            var sb = new StringBuilder();

            // A canvas that is open in a scene wins — that is what the developer is looking at.
            MapTileLayerBuilder inScene = FindInLoadedScenes();
            if (inScene != null)
            {
                Undo.RegisterFullObjectHierarchyUndo(inScene.gameObject, "Apply Map Tiles");
                int placedInScene = inScene.Rebuild();
                EditorUtility.SetDirty(inScene);
                EditorSceneManager.MarkSceneDirty(inScene.gameObject.scene);

                report = $"Applied {placedInScene} tile(s) to '{inScene.gameObject.scene.name}' (scene left unsaved).";
                return true;
            }

            string path = FindPrefabPath();
            if (string.IsNullOrEmpty(path))
            {
                report = "No MapTileLayerBuilder found in a loaded scene or in any prefab.";
                return false;
            }

            GameObject root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                var builder = root.GetComponentInChildren<MapTileLayerBuilder>(true);
                if (builder == null)
                {
                    report = $"'{path}' no longer contains a MapTileLayerBuilder.";
                    return false;
                }

                if (!builder.CanRebuild)
                {
                    report = "The builder is missing its projection, tile set or tile root.";
                    return false;
                }

                int placed = builder.Rebuild();
                PrefabUtility.SaveAsPrefabAsset(root, path);

                sb.Append($"Applied {placed} tile(s) to {System.IO.Path.GetFileName(path)}.");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }

            report = sb.ToString();
            return true;
        }

        private static int CountTiles(MapTileLayerBuilder builder)
        {
            if (builder.TileRoot == null) return 0;

            int count = 0;
            foreach (Transform child in builder.TileRoot)
                if (child.name.StartsWith("MapTile_")) count++;

            return count;
        }

        private static MapTileLayerBuilder FindInLoadedScenes() =>
            Object.FindFirstObjectByType<MapTileLayerBuilder>(FindObjectsInactive.Include);

        private static string cachedPrefabPath;

        private static string FindPrefabPath()
        {
            if (!string.IsNullOrEmpty(cachedPrefabPath) && AssetDatabase.LoadAssetAtPath<GameObject>(cachedPrefabPath) != null)
                return cachedPrefabPath;

            foreach (string guid in AssetDatabase.FindAssets("t:Prefab"))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!path.StartsWith("Assets/Project")) continue;

                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab == null || prefab.GetComponentInChildren<MapTileLayerBuilder>(true) == null) continue;

                cachedPrefabPath = path;
                return path;
            }

            return null;
        }
    }
}
