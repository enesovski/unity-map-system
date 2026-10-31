using Sirenix.OdinInspector;
using UnityEngine;

namespace Artifika.Map.Core
{
    [CreateAssetMenu(fileName = "Island Definition", menuName = "Map/IslandDefinition", order = 0)]
    public class IslandDefinition : ScriptableObject
    {
        public string IslandId;
        public string DisplayName;
        [ReadOnly] public string displayNameLocalizationKey;
        public Rect WorldBoundsRect;

        [Title("Reveal")]
        public bool startAsRevealed;

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (!string.IsNullOrWhiteSpace(displayNameLocalizationKey)) return;
            var token = DisplayName.ToUpperInvariant().Replace(' ', '_');
            displayNameLocalizationKey = $"ISLAND_{token}_NAME";
            UnityEditor.EditorUtility.SetDirty(this);
        }
#endif
    }
}