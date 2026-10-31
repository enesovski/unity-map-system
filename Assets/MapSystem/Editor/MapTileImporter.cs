using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace Artifika.Map.Core
{
    /// <summary>
    /// Fills a <see cref="MapTileSet"/> from a folder of sprites named by tile coordinate.
    ///
    /// The convention is <c>MapTile_x{X}_z{Z}</c> — for example <c>MapTile_x-3_z2.png</c>. Naming
    /// the file after its tile makes the art self-describing: the mapping survives re-exports,
    /// folder moves, and rebuilding the map prefab from scratch. Anything that does not match the
    /// pattern is reported rather than silently skipped, and existing entries can be preserved so
    /// hand-assigned tiles are not clobbered.
    /// </summary>
    public static class MapTileImporter
    {
        public const string FilenamePattern = @"^MapTile_x(?<x>-?\d+)_z(?<z>-?\d+)$";

        private static readonly Regex Pattern = new(FilenamePattern, RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public readonly struct ImportResult
        {
            public ImportResult(int imported, int replaced, int skipped, List<string> unmatched)
            {
                Imported = imported;
                Replaced = replaced;
                Skipped = skipped;
                Unmatched = unmatched;
            }

            public int Imported { get; }
            public int Replaced { get; }
            public int Skipped { get; }
            public List<string> Unmatched { get; }

            public override string ToString()
            {
                var sb = new StringBuilder();
                sb.Append($"{Imported} added, {Replaced} updated, {Skipped} left alone");

                if (Unmatched.Count > 0)
                {
                    sb.Append($", {Unmatched.Count} filename(s) did not match {nameof(MapTileImporter)}'s pattern: ");
                    sb.Append(string.Join(", ", Unmatched.GetRange(0, Mathf.Min(5, Unmatched.Count))));
                    if (Unmatched.Count > 5) sb.Append($" (+{Unmatched.Count - 5} more)");
                }

                return sb.ToString();
            }
        }

        /// <summary>Parses a tile coordinate out of a sprite's file name.</summary>
        public static bool TryParseTile(string assetName, out Vector2Int tile)
        {
            tile = default;

            Match match = Pattern.Match(assetName);
            if (!match.Success) return false;

            if (!int.TryParse(match.Groups["x"].Value, out int x)) return false;
            if (!int.TryParse(match.Groups["z"].Value, out int z)) return false;

            tile = new Vector2Int(x, z);
            return true;
        }

        public static string BuildFileName(Vector2Int tile) => $"MapTile_x{tile.x}_z{tile.y}";

        /// <summary>
        /// Scans <paramref name="folderPath"/> (an Assets-relative path) for sprites and writes
        /// matching ones into <paramref name="tileSet"/>.
        /// </summary>
        public static ImportResult Import(
            MapTileSet tileSet,
            string folderPath,
            bool overwriteExisting = true)
        {
            var unmatched = new List<string>();
            int imported = 0, replaced = 0, skipped = 0;

            if (tileSet == null || string.IsNullOrEmpty(folderPath) || !AssetDatabase.IsValidFolder(folderPath))
                return new ImportResult(0, 0, 0, unmatched);

            string[] guids = AssetDatabase.FindAssets("t:Sprite", new[] { folderPath });

            Undo.RecordObject(tileSet, "Import Map Tiles");
            tileSet.tiles ??= new List<MapTileArt>();

            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
                if (sprite == null) continue;

                string fileName = Path.GetFileNameWithoutExtension(path);

                if (!TryParseTile(sprite.name, out Vector2Int tile) &&
                    !TryParseTile(fileName, out tile))
                {
                    unmatched.Add(fileName);
                    continue;
                }

                if (tileSet.TryGet(tile, out MapTileArt existing))
                {
                    if (!overwriteExisting)
                    {
                        skipped++;
                        continue;
                    }

                    if (existing.art == sprite)
                    {
                        skipped++;
                        continue;
                    }

                    existing.art = sprite;
                    replaced++;
                    continue;
                }

                tileSet.tiles.Add(new MapTileArt { tile = tile, art = sprite });
                imported++;
            }

            EditorUtility.SetDirty(tileSet);
            AssetDatabase.SaveAssetIfDirty(tileSet);

            return new ImportResult(imported, replaced, skipped, unmatched);
        }
    }
}
