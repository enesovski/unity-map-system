using UnityEngine;

namespace Artifika.Map.Core
{
    public class POIMarker : StaticMarker
    {
        public POICategory Category { get; }
        public bool IsTracked { get; set; }
        public long DiscoveredAtTimestamp { get; set; }
        public bool IsPositionRegistered { get; private set; }

        public POIMarker(string markerId, POICategory category)
            : base(markerId, MarkerType.POI)
        {
            Category = category;
        }

        public void RegisterWorldPosition(Vector3 position)
        {
            if (IsPositionRegistered) return;
            WorldPosition = position;
            IsPositionRegistered = true;
        }
    }
}