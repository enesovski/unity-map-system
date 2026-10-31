using System;
using System.Collections.Generic;

namespace Artifika.Map.Core
{
    public interface IDynamicMarkerRepository
    {
        DynamicMarker GetById(string markerId);
        IReadOnlyList<DynamicMarker> GetAll();
        IReadOnlyList<T> GetAllOfType<T>() where T : DynamicMarker;

        void Register(DynamicMarker marker);
        void Unregister(string markerId);
        void NotifyUpdated(string markerId);

        event Action<DynamicMarker> OnMarkerAdded;
        event Action<string> OnMarkerRemoved;
        event Action<DynamicMarker> OnMarkerUpdated;
    }

    public class DynamicMarkerRepository : IDynamicMarkerRepository
    {
        private readonly Dictionary<string, DynamicMarker> markers = new();

        public event Action<DynamicMarker> OnMarkerAdded;
        public event Action<string> OnMarkerRemoved;
        public event Action<DynamicMarker> OnMarkerUpdated;

        public DynamicMarker GetById(string markerId)
        {
            markers.TryGetValue(markerId, out var marker);
            return marker;
        }

        public IReadOnlyList<DynamicMarker> GetAll() => new List<DynamicMarker>(markers.Values);

        public IReadOnlyList<T> GetAllOfType<T>() where T : DynamicMarker
        {
            var result = new List<T>();
            foreach (var marker in markers.Values)
                if (marker is T typed) result.Add(typed);
            return result;
        }

        public void Register(DynamicMarker marker)
        {
            markers[marker.MarkerId] = marker;
            OnMarkerAdded?.Invoke(marker);
        }
        
        public void Unregister(StaticMarker marker)
        {
            if (markers.ContainsKey(marker.MarkerId))
            {
                markers.Remove(marker.MarkerId);
            }
        }

        public void Unregister(string markerId)
        {
            if (!markers.Remove(markerId)) return;
            OnMarkerRemoved?.Invoke(markerId);
        }

        public void NotifyUpdated(string markerId)
        {
            if (!markers.TryGetValue(markerId, out var marker)) return;
            OnMarkerUpdated?.Invoke(marker);
        }
    }
}
