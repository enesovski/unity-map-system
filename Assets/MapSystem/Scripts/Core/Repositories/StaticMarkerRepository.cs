using System;
using System.Collections.Generic;

namespace Artifika.Map.Core
{
    public interface IStaticMarkerRepository
    {
        StaticMarker GetById(string markerId);
        IReadOnlyList<StaticMarker> GetAll();
        void Register(StaticMarker marker);
        void Unregister(string markerId);
        void SetState(string markerId, MarkerState state);

        event Action<StaticMarker> OnStaticMarkerStateChanged;
    }

    public class StaticMarkerRepository : IStaticMarkerRepository
    {
        private readonly Dictionary<string, StaticMarker> markers = new();

        public event Action<StaticMarker> OnStaticMarkerStateChanged;

        public StaticMarker GetById(string markerId)
        {
            markers.TryGetValue(markerId, out var marker);
            return marker;
        }

        public IReadOnlyList<StaticMarker> GetAll() => new List<StaticMarker>(markers.Values);

        public void Register(StaticMarker marker)
        {
            if (markers.ContainsKey(marker.MarkerId))
            {
                return;
            }
            markers[marker.MarkerId] = marker;
            OnStaticMarkerStateChanged?.Invoke(marker);
        }
        
        public void Unregister(string markerId)
        {
            if (string.IsNullOrWhiteSpace(markerId)) 
                return;
            markers.Remove(markerId);
        }
        
        public void Unregister(StaticMarker marker)
        {
            if (markers.ContainsKey(marker.MarkerId))
            {
                markers.Remove(marker.MarkerId);
            }
        }


        public void SetState(string markerId, MarkerState state)
        {
            if (!markers.TryGetValue(markerId, out StaticMarker marker)) return;
            marker.State = state;
            OnStaticMarkerStateChanged?.Invoke(marker);
        }
    }
}