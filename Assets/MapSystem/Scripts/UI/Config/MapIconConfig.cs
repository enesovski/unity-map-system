using System;
using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;

namespace Artifika.Map.Core
{
    [CreateAssetMenu(menuName = "Map/Icon Config", fileName = "MapIconConfig")]
    public class MapIconConfig : ScriptableObject
    {
        public const float DefaultIconSize = 28f;

        [Title("Fallback")]
        [SerializeField] private Sprite fallbackIcon;
        [SerializeField] private float fallbackIconSize = DefaultIconSize;

        [Title("Marker Icons")]
        [SerializeField] private List<MarkerTypeIconEntry> markerTypeIcons = new();

        [Title("Player Slot Icons")]
        [SerializeField] private List<PlayerSlotIconEntry> playerSlotIcons = new(4);

        [Title("POI Icons")]
        [SerializeField] private List<PoiCategoryIconEntry> poiCategoryIcons = new();

        [Title("Entity Marker Icons")]
        [SerializeField] private List<EntityTypeIconEntry> entityMarkerIcons = new();

        private Dictionary<MarkerType, Sprite> _markerTypeIconMap;
        private Dictionary<POICategory, Sprite> _poiCategoryIconMap;
        private Dictionary<EntityType, Sprite> _entityTypeIconMap;

        private void OnEnable()
        {
            RebuildCaches();
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            RebuildCaches();
        }
#endif

        public Sprite GetIcon(MapMarkerBase marker)
        {
            if (marker == null)
                return fallbackIcon;

            if (marker is POIMarker poiMarker)
                return GetPoiIcon(poiMarker.Category);

            if (marker is EntityMarker playerMarker &&
                marker.MarkerType is MarkerType.Player or MarkerType.Teammate)
            {
                return GetPlayerSlotIcon(playerMarker.PlayerSlot, marker.MarkerType);
            }

            if (marker is EntityMarker entityMarker &&
                marker.MarkerType == MarkerType.Companion &&
                entityMarker.EntityType != EntityType.None)
            {
                return GetEntityMarkerIcon(entityMarker.EntityType, marker.MarkerType);
            }

            return GetMarkerTypeIcon(marker.MarkerType);
        }

        public float GetIconSize(MapMarkerBase marker)
        {
            if (marker == null)
                return NormalizeSize(fallbackIconSize);

            if (marker is POIMarker poiMarker)
                return GetPoiIconSize(poiMarker.Category);

            if (marker is EntityMarker playerMarker &&
                marker.MarkerType is MarkerType.Player or MarkerType.Teammate)
            {
                return GetPlayerSlotIconSize(playerMarker.PlayerSlot, marker.MarkerType);
            }

            if (marker is EntityMarker entityMarker &&
                marker.MarkerType == MarkerType.Companion &&
                entityMarker.EntityType != EntityType.None)
            {
                return GetEntityMarkerIconSize(entityMarker.EntityType, marker.MarkerType);
            }

            return GetMarkerTypeIconSize(marker.MarkerType);
        }

        public Sprite GetMarkerTypeIcon(MarkerType markerType)
        {
            if (_markerTypeIconMap != null &&
                _markerTypeIconMap.TryGetValue(markerType, out var icon) &&
                icon != null)
            {
                return icon;
            }

            return fallbackIcon;
        }

        public Sprite GetPlayerSlotIcon(int playerSlot, MarkerType fallbackMarkerType)
        {
            int index = playerSlot - 1;
            if (playerSlotIcons != null &&
                index >= 0 &&
                index < playerSlotIcons.Count &&
                playerSlotIcons[index].Icon != null)
            {
                return playerSlotIcons[index].Icon;
            }

            return GetMarkerTypeIcon(fallbackMarkerType);
        }

        private float GetPlayerSlotIconSize(int playerSlot, MarkerType fallbackMarkerType)
        {
            int index = playerSlot - 1;
            if (playerSlotIcons != null &&
                index >= 0 &&
                index < playerSlotIcons.Count &&
                playerSlotIcons[index].Icon != null)
            {
                return NormalizeSize(playerSlotIcons[index].Size);
            }

            return GetMarkerTypeIconSize(fallbackMarkerType);
        }

        public Sprite GetPoiIcon(POICategory category)
        {
            if (_poiCategoryIconMap != null &&
                _poiCategoryIconMap.TryGetValue(category, out var icon) &&
                icon != null)
            {
                return icon;
            }

            return fallbackIcon;
        }

        public Sprite GetEntityMarkerIcon(EntityType entityType, MarkerType fallbackMarkerType)
        {
            if (entityType != EntityType.None &&
                _entityTypeIconMap != null &&
                _entityTypeIconMap.TryGetValue(entityType, out var icon) &&
                icon != null)
            {
                return icon;
            }

            return GetMarkerTypeIcon(fallbackMarkerType);
        }

        private float GetMarkerTypeIconSize(MarkerType markerType)
        {
            if (TryGetEntry(markerTypeIcons, entry => entry.Type == markerType, out var entry) &&
                entry.Icon != null)
            {
                return NormalizeSize(entry.Size);
            }

            return NormalizeSize(fallbackIconSize);
        }

        private float GetPoiIconSize(POICategory category)
        {
            if (TryGetEntry(poiCategoryIcons, entry => entry.Category == category, out var entry) &&
                entry.Icon != null)
            {
                return NormalizeSize(entry.Size);
            }

            return NormalizeSize(fallbackIconSize);
        }

        private float GetEntityMarkerIconSize(EntityType entityType, MarkerType fallbackMarkerType)
        {
            if (TryGetEntry(entityMarkerIcons, entry => entry.EntityType == entityType, out var entry) &&
                entry.Icon != null)
            {
                return NormalizeSize(entry.Size);
            }

            return GetMarkerTypeIconSize(fallbackMarkerType);
        }

        private static bool TryGetEntry<T>(List<T> entries, Predicate<T> predicate, out T result)
        {
            if (entries != null)
            {
                foreach (var entry in entries)
                {
                    if (!predicate(entry))
                        continue;

                    result = entry;
                    return true;
                }
            }

            result = default;
            return false;
        }

        private static float NormalizeSize(float size)
            => size > 0f ? size : DefaultIconSize;

        [Button]
        private void ValidateConfiguration()
        {
            RebuildCaches();

            foreach (MarkerType markerType in Enum.GetValues(typeof(MarkerType)))
            {
                if (markerType == MarkerType.POI)
                    continue;

                if (!_markerTypeIconMap.TryGetValue(markerType, out var icon) || icon == null)
                    Debug.LogWarning($"Missing icon for marker type: {markerType}", this);
            }

            foreach (POICategory category in Enum.GetValues(typeof(POICategory)))
            {
                if (!_poiCategoryIconMap.TryGetValue(category, out var icon) || icon == null)
                    Debug.LogWarning($"Missing icon for POI category: {category}", this);
            }

            foreach (EntityType entityType in Enum.GetValues(typeof(EntityType)))
            {
                if (entityType == EntityType.None)
                    continue;

                if (!_entityTypeIconMap.TryGetValue(entityType, out var icon) || icon == null)
                    Debug.LogWarning($"Missing entity marker icon for entity type: {entityType}", this);
            }
        }

        private void RebuildCaches()
        {
            _markerTypeIconMap = BuildMarkerTypeMap(markerTypeIcons);
            _poiCategoryIconMap = BuildPoiCategoryMap(poiCategoryIcons);
            _entityTypeIconMap = BuildEntityTypeMap(entityMarkerIcons);
        }

        private Dictionary<MarkerType, Sprite> BuildMarkerTypeMap(List<MarkerTypeIconEntry> entries)
        {
            var map = new Dictionary<MarkerType, Sprite>();
            if (entries == null)
                return map;

            foreach (var entry in entries)
            {
                if (map.ContainsKey(entry.Type))
                    Debug.LogWarning($"Duplicate marker type icon entry found for {entry.Type}. Last one wins.", this);

                map[entry.Type] = entry.Icon;
            }

            return map;
        }

        private Dictionary<POICategory, Sprite> BuildPoiCategoryMap(List<PoiCategoryIconEntry> entries)
        {
            var map = new Dictionary<POICategory, Sprite>();
            if (entries == null)
                return map;

            foreach (var entry in entries)
            {
                if (map.ContainsKey(entry.Category))
                    Debug.LogWarning($"Duplicate POI category icon entry found for {entry.Category}. Last one wins.", this);

                map[entry.Category] = entry.Icon;
            }

            return map;
        }

        private Dictionary<EntityType, Sprite> BuildEntityTypeMap(List<EntityTypeIconEntry> entries)
        {
            var map = new Dictionary<EntityType, Sprite>();
            if (entries == null)
                return map;

            foreach (var entry in entries)
            {
                if (map.ContainsKey(entry.EntityType))
                    Debug.LogWarning($"Duplicate entity marker icon entry found for {entry.EntityType}. Last one wins.", this);

                map[entry.EntityType] = entry.Icon;
            }

            return map;
        }
    }
    
    [Serializable]
    public struct MarkerTypeIconEntry
    {
        public MarkerType Type;
        public Sprite Icon;
        [Min(1f)] public float Size;
    }

    [Serializable]
    public struct PlayerSlotIconEntry
    {
        public Sprite Icon;
        [Min(1f)] public float Size;
    }

    [Serializable]
    public struct PoiCategoryIconEntry
    {
        public POICategory Category;
        public Sprite Icon;
        [Min(1f)] public float Size;
    }

    [Serializable]
    public struct EntityTypeIconEntry
    {
        public EntityType EntityType;
        public Sprite Icon;
        [Min(1f)] public float Size;
    }
}
