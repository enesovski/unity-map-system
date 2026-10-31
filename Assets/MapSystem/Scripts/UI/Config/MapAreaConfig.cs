using Sirenix.OdinInspector;
using UnityEngine;

namespace Artifika.Map.Core
{
    [CreateAssetMenu(fileName = "MapAreaConfig", menuName = "Map/Area Config")]
    public class MapAreaConfig : ScriptableObject
    {
        [Title("Area Colors")] 
        [SerializeField] private Color corruptedOutlineColor = new Color(0.55f, 0f, 0.8f, 0.45f);
        [SerializeField] private Color corruptedFillColor = new Color(0.55f, 0f, 0.8f, 0.45f);

        [SerializeField] private Color questOutlineColor = new Color(1f, 0.85f, 0f, 0.4f);
        [SerializeField] private Color questFillColor = new Color(1f, 0.85f, 0f, 0.4f);

        [Title("Default Mask")]
        [SerializeField]
        private Sprite defaultMask;

        public Sprite DefaultMask => defaultMask;

        public Color GetOutlineColor(MarkerType type) => type switch
        {
            MarkerType.CorruptedArea => corruptedOutlineColor,
            MarkerType.QuestArea => questOutlineColor,
            _ => Color.white
        };
        
        public Color GetFillColor(MarkerType type) => type switch
        {
            MarkerType.CorruptedArea => corruptedFillColor,
            MarkerType.QuestArea => questFillColor,
            _ => Color.white
        };

    }
}