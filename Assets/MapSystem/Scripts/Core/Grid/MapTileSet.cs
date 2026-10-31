using System;
using System.Collections.Generic;
using UnityEngine;

namespace Artifika.Map.Core
{
    /// <summary>
    /// The map's island art, keyed by world tile coordinate.
    ///
    /// This replaces the old arrangement where a tile's position came from its order in the
    /// hierarchy and empty slots had to be padded with placeholder objects. Here a tile states
    /// which world tile it is, so the list can be in any order and gaps cost nothing.
    ///
    /// Kept separate from <see cref="MapProjection"/> so art can be swapped or versioned without
    /// touching the projection the whole map depends on.
    /// </summary>
    [CreateAssetMenu(fileName = "MapTileSet", menuName = "Map/Map Tile Set", order = 1)]
    public sealed class MapTileSet : ScriptableObject
    {
        [Tooltip("One entry per world tile that has map art. Order is irrelevant.")]
        public List<MapTileArt> tiles = new();

        [Tooltip("Drawn on every covered tile that has no art of its own — normally open ocean. " +
                 "Keeps the map whole while islands are still being drawn, and means the sea " +
                 "never has to be authored tile by tile.")]
        public Sprite defaultArt;

        /// <summary>
        /// The sprite to draw for a tile: its own art if it has any, otherwise the shared default.
        /// This is what both the painter and the tile builder use, so preview and result agree.
        /// </summary>
        public Sprite ResolveArt(Vector2Int tile)
        {
            Sprite own = GetArt(tile);
            return own != null ? own : defaultArt;
        }

        public bool HasOwnArt(Vector2Int tile) => TryGet(tile, out MapTileArt entry) && entry.art != null;

        public bool TryGet(Vector2Int tile, out MapTileArt result)
        {
            foreach (MapTileArt entry in tiles)
            {
                if (entry == null || entry.tile != tile) continue;

                result = entry;
                return true;
            }

            result = null;
            return false;
        }

        public Sprite GetArt(Vector2Int tile) => TryGet(tile, out MapTileArt entry) ? entry.art : null;

        public bool Contains(Vector2Int tile) => TryGet(tile, out _);

        /// <summary>Tile coordinates listed more than once. Should always be empty.</summary>
        public List<Vector2Int> FindDuplicateTiles()
        {
            var seen = new HashSet<Vector2Int>();
            var duplicates = new List<Vector2Int>();

            foreach (MapTileArt entry in tiles)
            {
                if (entry == null) continue;
                if (!seen.Add(entry.tile) && !duplicates.Contains(entry.tile))
                    duplicates.Add(entry.tile);
            }

            return duplicates;
        }

        /// <summary>Entries with no sprite assigned — they would render as blank cells.</summary>
        public List<Vector2Int> FindEmptyArt()
        {
            var empty = new List<Vector2Int>();

            foreach (MapTileArt entry in tiles)
                if (entry != null && entry.art == null)
                    empty.Add(entry.tile);

            return empty;
        }
    }

    [Serializable]
    public sealed class MapTileArt
    {
        [Tooltip("World tile coordinate — the same one the designer painted in IslandPlanner.")]
        public Vector2Int tile;

        public Sprite art;

        [Tooltip("Optional. Which island this tile belongs to, for validation and tooling.")]
        public string islandId;
    }
}
