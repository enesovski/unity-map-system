using System.Collections.Generic;
using UnityEngine;

namespace Artifika.Map.Core
{
    [CreateAssetMenu(fileName = "MapWorldDefinition", menuName = "Map/Map World Definition")]
    public class MapWorldDefinition : ScriptableObject
    {
        public Rect WorldBoundsRect;
        public List<IslandDefinition> Islands = new();
 
        
    }

}
