using Sirenix.OdinInspector;
using UnityEngine;

namespace Artifika.Map.UI
{
    public class MapZoomVisibilityController : MonoBehaviour
    {
        [Title("Layers")] [SerializeField] private CanvasGroup iconLayer;
        [SerializeField] private CanvasGroup questLayer;
        [SerializeField] private CanvasGroup entityLayer;
        [SerializeField] private CanvasGroup labelLayer;
        [SerializeField] private CanvasGroup areaLayer;

        [Title("Configuration")]

        [SerializeField, Range(0f, 1f)] private float closeToMidThreshold = 0.45f;
        [SerializeField, Range(0f, 1f)] private float midToFarThreshold = 0.2f;

        [SerializeField, Range(0f, 0.3f)]
        [Tooltip("How wide the blend zone is around each threshold. Larger = smoother transition.")]
        private float crossfadeWidth = 0.08f;

        [Title("Controller")] [SerializeField, Required]
        private MapViewController viewController;

        [Title("Debug")] [ShowInInspector, ReadOnly]
        private float _currentNormalizedZoom;

        [ShowInInspector, ReadOnly] private string currentTier = "—";

        private void OnEnable()
        {
            viewController.OnZoomChanged += OnZoomChanged;
            OnZoomChanged(viewController.NormalizedZoom);
        }

        private void OnDisable() => viewController.OnZoomChanged -= OnZoomChanged;

        private void OnZoomChanged(float normalizedZoom)
        {
            _currentNormalizedZoom = normalizedZoom;

            // Close: all icons visible
            float iconAlpha = FadeValue(normalizedZoom, visibleAbove: closeToMidThreshold, width: crossfadeWidth);

            // Mid+: entities and area overlays
            float entityAlpha = FadeValue(normalizedZoom, visibleAbove: midToFarThreshold, width: crossfadeWidth);

            float labelAlpha = 1f - FadeValue(normalizedZoom, visibleAbove: closeToMidThreshold, width: crossfadeWidth);

            SetAlpha(iconLayer, iconAlpha);
            SetAlpha(entityLayer, entityAlpha);
            SetAlpha(labelLayer, labelAlpha);
            SetAlpha(areaLayer, entityAlpha);
            SetAlpha(questLayer, iconAlpha);

            UpdateDebugTier(normalizedZoom);
        }

        private float FadeValue(float zoom, float visibleAbove, float width)
        {
            float half = Mathf.Max(0.001f, width * 0.5f);
            return Mathf.SmoothStep(0f, 1f,
                Mathf.InverseLerp(visibleAbove - half, visibleAbove + half, zoom));
        }

        private static void SetAlpha(CanvasGroup group, float alpha)
        {
            if (!group) return;
            group.alpha = alpha;
        }

        private void UpdateDebugTier(float zoom)
        {
            if (zoom >= closeToMidThreshold) currentTier = "CLOSE — All icons + areas visible";
            else if (zoom >= midToFarThreshold) currentTier = "MID — Entities + areas + labels";
            else currentTier = "FAR — Labels only";
        }

        [Button("Apply Default Thresholds")]
        private void ApplyDefaults()
        {
            closeToMidThreshold = 0.45f;
            midToFarThreshold = 0.2f;
            crossfadeWidth = 0.08f;
        }
    }
}