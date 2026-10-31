using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Artifika.Map.Core
{
    public class MapSaveHandler : MonoBehaviour, ISaveable
    {
        public string SaveKey => "MapDiscoveryData";
        public LoadPriorityLevel LoadPriority => LoadPriorityLevel.Normal;

        private IStaticMarkerRepository StaticMarkers => MapSystemContext.Instance.StaticMarkers;
        private IDynamicMarkerRepository DynamicMarkers => MapSystemContext.Instance.DynamicMarkers;
        private IMarkerFactory MarkerFactory => MapSystemContext.Instance.MarkerFactory;

        private void Awake()
        {
            SaveManager.Instance.Register(this);
        }

        private void OnDestroy()
        {
            SaveManager.Instance.Unregister(this);
        }

        public object RetrieveSaveData()
        {
            var discoveredIds = StaticMarkers.GetAll()
                .Where(m => m.State == MarkerState.Visible)
                .Select(m => m.MarkerId)
                .ToList();

            var placedMarkers = DynamicMarkers.GetAllOfType<PlacedMarker>()
                .Select(m => new PlacedMarkerSaveData
                {
                    MarkerId    = m.MarkerId,
                    MarkerType  = m.MarkerType,
                    WorldPosition = m.WorldPosition,
                    DisplayName = m.DisplayName,
                    State       = m.State,
                    ReplicationMode = m.ReplicationMode
                })
                .ToList();

            return new MapDiscoverySaveData
            {
                DiscoveredMarkerIds = discoveredIds,
                PlacedMarkers       = placedMarkers
            };
        }

        public void LoadData(object state)
        {
            var data = (MapDiscoverySaveData)state;

            foreach (var markerId in data.DiscoveredMarkerIds)
                StaticMarkers.SetState(markerId, MarkerState.Visible);

            if (data.PlacedMarkers == null) return;

            foreach (var markerData in data.PlacedMarkers)
                MarkerFactory.RestorePlacedMarker(markerData);
        }
    }

    [System.Serializable]
    public class MapDiscoverySaveData
    {
        public List<string> DiscoveredMarkerIds;
        public List<PlacedMarkerSaveData> PlacedMarkers;
    }

    [System.Serializable]
    public class PlacedMarkerSaveData
    {
        public string      MarkerId;
        public MarkerType  MarkerType;
        public Vector3     WorldPosition;
        public string      DisplayName;
        public MarkerState State;
        public MarkerReplicationMode ReplicationMode;
    }
}
