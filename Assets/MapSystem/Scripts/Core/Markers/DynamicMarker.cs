namespace Artifika.Map.Core
{
    public abstract class DynamicMarker : MapMarkerBase
    {
        protected DynamicMarker(string markerId, MarkerType markerType)
            : base(markerId, markerType)
        {
        }
    }
}