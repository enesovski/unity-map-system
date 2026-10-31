using Artifika.Map.Core;
using Artifika.Map.View;
using DG.Tweening;
using Sirenix.OdinInspector;
using TMPro;
using UnityEngine;

namespace Artifika.Map.UI
{
    [RequireComponent(typeof(CanvasGroup))]
    public class MapDetailPanel : MonoBehaviour, IDetailPanelView
    {
        [Title("UI")]
        [SerializeField] private TMP_Text titleText;
        [SerializeField] private TMP_Text descriptionText;
        [SerializeField] private TMP_Text distanceText;

        [Title("Animation")]
        [SerializeField] private float animDuration = 0.25f;

        private CanvasGroup canvasGroup;
        private RectTransform rectTransform;
        private void Awake()
        {
            canvasGroup = GetComponent<CanvasGroup>();
            rectTransform = GetComponent<RectTransform>();
            
            SetActive(false, instant: true);
        }

        public void Show(MapMarkerContext context)
        {
            titleText.text = context.Title;
            descriptionText.text = context.Description;
            if(distanceText) distanceText.text = FormatDistance(context.DistanceFromPlayer);
            
            SetActive(true, instant: false);
        }

        public void Hide()
        {
            DOTween.Kill(rectTransform);
            DOTween.Kill(canvasGroup);

            canvasGroup.DOFade(0f, animDuration)
                .OnComplete(() => SetActive(false, instant: true));
        }

        private void SetActive(bool active, bool instant)
        {
            canvasGroup.blocksRaycasts = active;
            canvasGroup.interactable = active;

            if (instant)
            {
                canvasGroup.alpha = active ? 1f : 0f;
                return;
            }

            DOTween.Kill(rectTransform);
            DOTween.Kill(canvasGroup);

            canvasGroup.DOFade(1f, animDuration);
        }

        private static string FormatDistance(float meters)
        {
            return meters >= 1000f
                ? $"{meters / 1000f:F1} km"
                : $"{Mathf.RoundToInt(meters)} m";
        }
    }
}
