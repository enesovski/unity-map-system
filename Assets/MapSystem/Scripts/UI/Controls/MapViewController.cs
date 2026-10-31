using System;
using System.Collections;
using Artifika.Map.Core;
using Sirenix.OdinInspector;
using UnityEngine;

namespace Artifika.Map.UI
{
    public class MapViewController : MonoBehaviour
    {
        [Title("UI")] [SerializeField] private RectTransform mapContentRoot;
        [SerializeField] private RectTransform viewport;
        [SerializeField] private RectTransform islandsRect;

        [Space] [SerializeField] private MapPinLabelInput mapPinLabelInput;

        [Title("Controls")] [MinMaxSlider(0.1f, 10f, ShowFields = true)] [SerializeField]
        private Vector2 zoomRange = new(0.5f, 3f);

        [SerializeField, Range(0.05f, 1f)] private float zoomStep = 0.2f;
        [SerializeField, Range(0.01f, 0.5f)] private float zoomSmoothTime = 0.1f;

        [Space] [SerializeField, Range(0.1f, 10f)]
        private float panSpeed = 1f;

        [SerializeField, Range(0.01f, 0.3f)] private float panSmoothTime = 0.05f;

        [Tooltip("How far the map may be dragged past its own edges, as a fraction of half the " +
                 "viewport.\n\n" +
                 "0 pins the map so its edges never leave the viewport — which means a marker on " +
                 "an edge tile can never be brought to the middle of the screen.\n" +
                 "1 allows the map's edge to reach the centre of the view, so any point on the " +
                 "map can be centred. The slack shows open sea.")]
        [SerializeField, Range(0f, 1f)] private float edgePanAllowance = 1f;

        [Tooltip("How much of the viewport the whole map fills when fully zoomed out. " +
                 "0.9 leaves a ring of sea around the world so its full extent reads at a glance.")]
        [SerializeField, Range(0.3f, 1f)] private float fitMargin = 0.9f;

        [Space] [SerializeField, Range(0.1f, 1f)]
        private float centerAnimDuration = 0.3f;

        [Title("Debug")]
        [ShowInInspector, ReadOnly]
        public float CurrentZoom => currentZoom;

        [ShowInInspector, ReadOnly]
        public float NormalizedZoom => Mathf.InverseLerp(zoomRange.x, zoomRange.y, currentZoom);

        [ShowInInspector, ReadOnly] private Vector2 PanPosition => targetPan;

        public bool IsInteractionBlocked { get; set; } = false;
        public event Action<float> OnZoomChanged;

        private MapSystemContext mapSystemContext;

        private float targetZoom = 1f;
        private float currentZoom = 1f;
        private float zoomVelocity;
        private Vector2 targetPan;
        private Vector2 panVelocity;
        private Coroutine centerCoroutine;

        private Vector2 keyboardPanDelta;

        private void OnEnable()
        {
            if (MapSystemContext.TryGetInitialized(out var context))
            {
                Bind(context);
                return;
            }

            MapSystemContext.OnInitialized -= Bind;
            MapSystemContext.OnInitialized += Bind;
        }

        private void OnDisable()
        {
            MapSystemContext.OnInitialized -= Bind;
            MapSystemContext.OnShutdown -= OnMapShutdown;
            mapSystemContext = null;
        }

        public void Start()
        {
            currentZoom = 1f;
            targetZoom = 1f;
        }

        private void Bind(MapSystemContext context)
        {
            mapSystemContext = context;

            MapSystemContext.OnInitialized -= Bind;
            MapSystemContext.OnShutdown -= OnMapShutdown;
            MapSystemContext.OnShutdown += OnMapShutdown;
        }

        private void OnMapShutdown()
        {
            mapSystemContext = null;
        }

        private void LateUpdate()
        {
            if (mapPinLabelInput != null && mapPinLabelInput.IsVisible) return;

            if (keyboardPanDelta != Vector2.zero)
            {
                targetPan += (-keyboardPanDelta * panSpeed);
                ClampPan();
            }

            ApplyZoomSmoothing();
            ApplyPanSmoothing();
        }

        public void SetKeyboardPanDelta(Vector2 delta)
        {
            keyboardPanDelta = delta;
        }

        public void Pan(Vector2 screenDelta)
        {
            if (IsInteractionBlocked) return;

            // No division by zoom. targetPan drives mapContentRoot.anchoredPosition, which lives
            // in the unscaled viewport space — zoom is a localScale on the content itself and does
            // not affect it. Dividing made a drag 5x faster zoomed out than zoomed in, which is
            // why a small drag threw the whole map at minimum zoom.
            targetPan += screenDelta * panSpeed;
            ClampPan();
        }

        [Button("Center On Player")]
        public void CenterOnPlayer()
        {
            if (!TryGetPlayerCenterPan(out var targetPoint))
                return;

            if (centerCoroutine != null)
            {
                StopCoroutine(centerCoroutine);
                centerCoroutine = null;
            }

            targetPan = targetPoint;
            ClampPan();

            centerCoroutine = StartCoroutine(AnimatePanTo(targetPan, centerAnimDuration));
        }

        public void CenterOnPlayerImmediately()
        {
            if (!TryGetPlayerCenterPan(out var targetPoint))
                return;

            if (centerCoroutine != null)
            {
                StopCoroutine(centerCoroutine);
                centerCoroutine = null;
            }

            targetPan = targetPoint;
            ClampPan();

            panVelocity = Vector2.zero;
            mapContentRoot.anchoredPosition = targetPan;
        }

        private bool TryGetPlayerCenterPan(out Vector2 targetPoint)
        {
            targetPoint = Vector2.zero;

            if (mapSystemContext?.Projector == null)
                return false;

            var player = mapSystemContext.EntityTracking.GetLocalPlayer();
            if (player == null)
                return false;

            Vector2 uv = mapSystemContext.Projector.WorldToNormalized(player.WorldPosition);

            Vector3 uiWorld = MapCoordinateCalculator.MapUVToUIWorldPoint(uv, islandsRect);
            Vector2 localInContent = mapContentRoot.InverseTransformPoint(uiWorld);

            targetPoint = -localInContent * currentZoom;
            return true;
        }

        private float GetDynamicMinZoom()
        {
            if (islandsRect.rect.width <= 0 || islandsRect.rect.height <= 0)
                return zoomRange.x;

            float minZoomX = viewport.rect.width / islandsRect.rect.width;
            float minZoomY = viewport.rect.height / islandsRect.rect.height;

            // Min, not Max. Max is "cover" — the map always fills the viewport, so on a square
            // map in a wide window the top and bottom are always off screen and the whole world
            // can never be seen at once. Min is "contain": zoom out until the map fits entirely.
            // Cover only made sense when nothing was drawn outside the map; the sea covers that
            // now, so there is no reason to forbid seeing the edges.
            float fitZoom = Mathf.Min(minZoomX, minZoomY) * Mathf.Clamp01(fitMargin);

            return Mathf.Max(fitZoom, zoomRange.x);
        }

        public void ZoomAtScreenPoint(float delta, Vector2 screenPoint)
        {
            if (IsInteractionBlocked) return;

            float prevZoom = targetZoom;
            float dynamicMin = GetDynamicMinZoom();

            targetZoom = Mathf.Clamp(targetZoom + delta * zoomStep, dynamicMin, zoomRange.y);
            if (Mathf.Approximately(prevZoom, targetZoom)) return;

            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                viewport, screenPoint, null, out var localPoint);

            float zoomRatio = targetZoom / prevZoom;
            targetPan = (targetPan - localPoint) * zoomRatio + localPoint;
            ClampPan();
        }

        public void SetZoomImmediate(float zoom)
        {
            float dynamicMin = GetDynamicMinZoom();
            currentZoom = Mathf.Clamp(zoom, dynamicMin, zoomRange.y);
            targetZoom = currentZoom;
            zoomVelocity = 0f;
            mapContentRoot.localScale = Vector3.one * currentZoom;
            ClampPan();
            mapContentRoot.anchoredPosition = GetClampedPan(mapContentRoot.anchoredPosition, currentZoom);
            panVelocity = Vector2.zero;
            OnZoomChanged?.Invoke(NormalizedZoom);
        }

        private void ApplyZoomSmoothing()
        {
            float prev = currentZoom;
            currentZoom = Mathf.SmoothDamp(currentZoom, targetZoom, ref zoomVelocity, zoomSmoothTime);
            mapContentRoot.localScale = Vector3.one * currentZoom;
            if (!Mathf.Approximately(prev, currentZoom))
                OnZoomChanged?.Invoke(NormalizedZoom);
        }

        /// <summary>True while the zoom is still easing toward its target.</summary>
        private bool IsZooming => Mathf.Abs(targetZoom - currentZoom) > 0.0001f;

        private void ApplyPanSmoothing()
        {
            // Zoom-to-cursor works by moving the pan and the scale together. If the two ease at
            // different rates the focal point drifts while they converge, which reads as the view
            // sliding around during a zoom. Matching the time constants keeps them in step.
            float smoothTime = IsZooming ? zoomSmoothTime : panSmoothTime;

            var next = Vector2.SmoothDamp(
                mapContentRoot.anchoredPosition, targetPan, ref panVelocity, smoothTime);
            var clampedNext = GetClampedPan(next, currentZoom);

            // Killing the velocity on contact with the clamp is right for a pan that runs into
            // the edge, but wrong mid-zoom: the clamp bounds move every frame as the scale
            // changes, so the pan is repeatedly stopped and released — the vertical judder.
            if (!IsZooming)
            {
                if (!Mathf.Approximately(next.x, clampedNext.x))
                    panVelocity.x = 0f;
                if (!Mathf.Approximately(next.y, clampedNext.y))
                    panVelocity.y = 0f;
            }

            mapContentRoot.anchoredPosition = clampedNext;
        }

        private void ClampPan()
        {
            targetPan = GetClampedPan(targetPan, targetZoom);
        }

        private Vector2 GetClampedPan(Vector2 pan, float zoom)
        {
            Vector2 islandsOffset = islandsRect.anchoredPosition * zoom;
            Vector2 islandsSize = islandsRect.rect.size * zoom;
            Vector2 viewSize = viewport.rect.size;

            float maxX = Mathf.Max(0f, (islandsSize.x - viewSize.x) * 0.5f);
            float maxY = Mathf.Max(0f, (islandsSize.y - viewSize.y) * 0.5f);

            // Slack past the edges. Without it the map's own border is the travel limit, so a
            // marker on an edge tile can never reach the middle of the screen — the clamp stops
            // the pan first. Half a viewport of overscroll makes every point centreable, and the
            // slack reads as open sea because the sea layer covers the whole viewport.
            Vector2 allowance = viewSize * 0.5f * edgePanAllowance;
            maxX += allowance.x;
            maxY += allowance.y;

            pan.x = Mathf.Clamp(pan.x, -islandsOffset.x - maxX, -islandsOffset.x + maxX);
            pan.y = Mathf.Clamp(pan.y, -islandsOffset.y - maxY, -islandsOffset.y + maxY);
            return pan;
        }

        private IEnumerator AnimatePanTo(Vector2 target, float duration)
        {
            float elapsed = 0f;
            var start = mapContentRoot.anchoredPosition;
            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = 1f - Mathf.Pow(1f - Mathf.Clamp01(elapsed / duration), 3f);
                mapContentRoot.anchoredPosition = Vector2.Lerp(start, target, t);
                yield return null;
            }

            mapContentRoot.anchoredPosition = target;
            targetPan = target;
        }
    }
}
