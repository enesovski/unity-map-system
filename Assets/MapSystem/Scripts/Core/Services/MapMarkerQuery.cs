using System.Collections.Generic;
using Artifika.Map.Core;

namespace Artifika.Map
{
    public interface IMapMarkerQuery
    {
        MapMarkerBase GetById(string markerId);
        IReadOnlyList<MapMarkerBase> GetAllVisible();
    }

    public class MapMarkerQuery : IMapMarkerQuery
    {
        private readonly IDynamicMarkerRepository dynamicMarkers;
        private readonly IStaticMarkerRepository staticMarkers;

        public MapMarkerQuery(IDynamicMarkerRepository dynamicMarkers, IStaticMarkerRepository staticMarkers)
        {
            this.dynamicMarkers = dynamicMarkers;
            this.staticMarkers = staticMarkers;
        }

        public MapMarkerBase GetById(string markerId)
        {
            var staticMarker = staticMarkers.GetById(markerId);
            if (staticMarker != null) return staticMarker;
            return dynamicMarkers.GetById(markerId);
        }

        public IReadOnlyList<MapMarkerBase> GetAllVisible()
        {
            var result = new List<MapMarkerBase>();

            foreach (var marker in staticMarkers.GetAll())
                if (marker.State != MarkerState.Hidden)
                    result.Add(marker);

            foreach (var marker in dynamicMarkers.GetAll())
                if (marker.State != MarkerState.Hidden)
                    result.Add(marker);

            return result;
        }
    }
}
