using System;

namespace Artifika.Map.Core
{
    public class PlacedMarker : DynamicMarker
    {
        public bool ShowLabelOnSelect => MarkerType == MarkerType.Pin;

        public PlacedMarker(MarkerType type, string displayName = "")
            : base(Guid.NewGuid().ToString(), type)
        {
            DisplayName = displayName;
            State = MarkerState.Visible;
        }
        
        public PlacedMarker(string markerId, MarkerType type, string displayName = "")
            : base(markerId, type)
        {
            DisplayName = displayName;
            State = MarkerState.Visible;
        }
    }
}