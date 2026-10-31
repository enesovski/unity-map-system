using UnityEngine;

namespace Artifika.Map.Core
{
    public class AreaMarker : StaticMarker
    {
        public float WorldRadius { get; }

        public Sprite MaskSprite { get; }

        public AreaMarker(string markerId, MarkerType type, float worldRadius, Sprite maskSprite = null)
            : base(markerId, type)
        {
            WorldRadius = worldRadius;
            MaskSprite  = maskSprite;
        }
    }
}