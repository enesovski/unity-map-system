using System;
using Artifika.Map.Core;
using UnityEngine;

namespace Artifika.Map
{
    public interface IDiscoveryService
    {
        bool TryDiscover(string markerId);
        event Action<StaticMarker> OnStaticMarkerDiscovered;
    }

    public class DiscoveryService : IDiscoveryService
    {
        private readonly IStaticMarkerRepository staticMarkers;

        public event Action<StaticMarker> OnStaticMarkerDiscovered;

        public DiscoveryService(IStaticMarkerRepository staticMarkers)
        {
            this.staticMarkers = staticMarkers;
        }

        public bool TryDiscover(string markerId)
        {
            StaticMarker marker = staticMarkers.GetById(markerId);

            if (marker == null)
            {
                Debug.LogWarning($"[DiscoveryService] Unknown static marker: {markerId}");
                return false;
            }

            if (marker.State != MarkerState.Hidden) return false;

            staticMarkers.SetState(markerId, MarkerState.Visible);
            OnStaticMarkerDiscovered?.Invoke(marker);
            return true;
        }
    }
}
