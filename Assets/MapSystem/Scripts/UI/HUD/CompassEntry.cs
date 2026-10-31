using Artifika.Map.Core;
using UnityEngine;

namespace Artifika.Map.View
{
    public class CompassEntry
    {
        public string MarkerId { get; }
        public string Label { get; }
        public Sprite Icon { get; }
        public MarkerType MarkerType { get; }

        public float WorldAngle { get; set; }

        public Vector3 WorldPosition { get; set; }

        public bool IgnoreDistanceFade { get; set; }

        public CompassEntry(string markerId, string label, Sprite icon, MarkerType markerType)
        {
            MarkerId = markerId;
            Label = label;
            Icon = icon;
            MarkerType = markerType;
        }
    }
}