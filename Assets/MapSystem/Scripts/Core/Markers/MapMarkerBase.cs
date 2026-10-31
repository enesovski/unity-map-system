using UnityEngine;

namespace Artifika.Map.Core
{
    public abstract class MapMarkerBase
    {
        public string MarkerId { get; }
        public MarkerType MarkerType { get; }
        public MarkerState State { get; set; }
        public Vector3 WorldPosition { get; set; }

        public string DisplayName { get; set; }
        public string DisplayNameLocalizationKey { get; set; }
        public string Description { get; set; }
        public string DescriptionLocalizationKey { get; set; }
        public MarkerReplicationMode ReplicationMode { get; set; } = MarkerReplicationMode.LocalOnly;

        protected MapMarkerBase(string markerId, MarkerType markerType)
        {
            MarkerId = markerId;
            MarkerType = markerType;
            State = MarkerState.Hidden;
        }
    }
}
