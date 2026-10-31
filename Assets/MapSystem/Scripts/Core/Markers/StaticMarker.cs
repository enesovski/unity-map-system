namespace Artifika.Map.Core
{
    public abstract class StaticMarker : MapMarkerBase
    {
        protected StaticMarker(string markerId, MarkerType markerType)
            : base(markerId, markerType)
        {
        }
    }
}