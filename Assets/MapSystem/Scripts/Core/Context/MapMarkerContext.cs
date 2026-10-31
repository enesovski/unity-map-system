using UnityEngine;

namespace Artifika.Map.Core
{
    public class MapMarkerContext
    {
        public string Title { get; set; }
        public string Description { get; set; }
        public Sprite Icon { get; set; }
        public MarkerState MarkerState { get; set; }
        public MarkerType MarkerType { get; set; }
        public Vector3 WorldPosition { get; set; }
        public float DistanceFromPlayer { get; set; }
    }
}