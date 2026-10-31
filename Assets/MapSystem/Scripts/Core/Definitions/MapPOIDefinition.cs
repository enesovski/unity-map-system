using Sirenix.OdinInspector;
using UnityEngine;

namespace Artifika.Map.Core
{
    [CreateAssetMenu(fileName = "Map POI Definition", menuName = "Map/Map POI Definition", order = 0)]
    public class MapPOIDefinition : ScriptableObject
    {
        public string PoiId;
        public string DisplayName;
        [ReadOnly] public string DisplayNameLocalizationKey;
        [ReadOnly] public string DescriptionLocalizationKey;

        [TextArea(2, 5)]
        public string Description;
        public POICategory Category;

        public string IslandId;
        
        public bool RevealOnDiscovery = true;
        public MarkerState DefaultState = MarkerState.Hidden;

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (!string.IsNullOrWhiteSpace(DisplayNameLocalizationKey)) return;
            var token = DisplayName.ToUpperInvariant().Replace(' ', '_');
            DisplayNameLocalizationKey = $"POI_{token}_NAME";
            UnityEditor.EditorUtility.SetDirty(this);

            if (!string.IsNullOrWhiteSpace(DescriptionLocalizationKey)) return;
            DescriptionLocalizationKey = $"POI_{token}_DESC";
            UnityEditor.EditorUtility.SetDirty(this);
        }
#endif
    }
}