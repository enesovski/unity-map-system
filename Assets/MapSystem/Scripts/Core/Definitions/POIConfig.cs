using System.Collections.Generic;
using UnityEngine;

namespace Artifika.Map.Core
{
    [CreateAssetMenu(fileName = "POIConfig", menuName = "Map/POIConfig")]
    public class POIConfig : ScriptableObject
    {
        public List<MapPOIDefinition> POIDefinitions = new();
    }
}