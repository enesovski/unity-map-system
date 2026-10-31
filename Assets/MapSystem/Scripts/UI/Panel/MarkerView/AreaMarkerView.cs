using Artifika.Map.Core;
using UnityEngine;
using UnityEngine.UI;

namespace Artifika.Map.UI
{
    [RequireComponent(typeof(RectTransform))]
    public class AreaMarkerView : MonoBehaviour
    {
        [SerializeField] private Image fillImage;
        [SerializeField] private Image outlineImage;
        
        private RectTransform rectTransform;

        private void Awake()
        {
            rectTransform = GetComponent<RectTransform>();
            SetRaycastTarget(fillImage, false);
            SetRaycastTarget(outlineImage, false);
        }

        public void Bind(AreaMarker marker, float canvasDiameter, Color outlineColor, Color fillColor, Sprite fallbackMask)
        {
            fillImage.color = fillColor;
            outlineImage.color = outlineColor;

            //fillImage.sprite = marker.MaskSprite != null ? marker.MaskSprite : fallbackMask;
            rectTransform.sizeDelta = Vector2.one * canvasDiameter;
        }

        public void SetAnchoredPosition(Vector2 pos) => rectTransform.anchoredPosition = pos;
        public void SetVisible(bool visible) => gameObject.SetActive(visible);

        private static void SetRaycastTarget(Graphic graphic, bool value)
        {
            if (graphic != null)
                graphic.raycastTarget = value;
        }
    }
}
