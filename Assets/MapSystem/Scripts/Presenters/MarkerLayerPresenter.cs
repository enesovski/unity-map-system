using System.Collections.Generic;
using Artifika.Map.Core;
using Artifika.Map.UI;
using Michsky.UI.Reach;
using Sirenix.OdinInspector;
using UnityEngine;

namespace Artifika.Map
{
    public class MarkerLayerPresenter : MonoBehaviour
    {
        [Title("References")] [SerializeField] private MapIconConfig iconConfig;
        [SerializeField] private RectTransform mapContentRoot;
        [SerializeField] private MapViewController viewController;

        [SerializeField] private RectTransform islandsRect;

        [Title("Layers")] [SerializeField] private Transform entityLayerRoot;
        [SerializeField] private Transform questLayerRoot;
        [SerializeField] private Transform poiLayerRoot;
        [SerializeField] private Transform iconLayerRoot;

        [Title("Prefab")] [SerializeField, Required]
        private GameObject markerViewPrefab;

        [Title("Zoom Scaling")]
        [SerializeField] private bool enableDynamicMarkerScaling = true;
        [SerializeField, Min(0.01f)] private float zoomedOutMarkerScale = 1.15f;
        [SerializeField, Min(0.01f)] private float zoomedInMarkerScale = 0.7f;
        [SerializeField, Min(0.0001f)] private float markerScaleEpsilon = 0.005f;

        private MapSystemContext mapSystemContext;
        private readonly Dictionary<string, MapMarkerView> views = new();
        private readonly Dictionary<string, EntityMarker> trackedEntities = new();
        private string selectedMarkerId;
        private bool bound;
        private float lastAppliedMarkerScale = float.NaN;
        private bool subscribedToZoom;

        [SerializeField] private LocalizedObject languageChangeListener;

        private void OnEnable()
        {
            SubscribeToZoom();

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
            UnsubscribeFromZoom();
            Unbind();
        }

        private void Bind(MapSystemContext context)
        {
            if (bound)
                return;

            SubscribeToZoom();

            mapSystemContext = context;
            bound = true;

            MapSystemContext.OnInitialized -= Bind;
            MapSystemContext.OnShutdown -= OnMapShutdown;
            MapSystemContext.OnShutdown += OnMapShutdown;

            mapSystemContext.DynamicMarkers.OnMarkerAdded += OnMarkerAdded;
            mapSystemContext.DynamicMarkers.OnMarkerRemoved += OnMarkerRemoved;
            mapSystemContext.DynamicMarkers.OnMarkerUpdated += OnMarkerUpdated;
            mapSystemContext.StaticMarkers.OnStaticMarkerStateChanged += OnStaticMarkerStateChanged;

            foreach (var poi in mapSystemContext.StaticMarkers.GetAll()) RefreshMarkerView(poi);
            foreach (var marker in mapSystemContext.DynamicMarkers.GetAll()) RefreshMarkerView(marker);
            UpdateMarkerZoomScale(force: true);

            if (languageChangeListener != null)
                languageChangeListener.onLanguageChanged.AddListener(OnLanguageChanged);
        }

        private void Unbind()
        {
            if (mapSystemContext != null)
            {
                mapSystemContext.DynamicMarkers.OnMarkerAdded -= OnMarkerAdded;
                mapSystemContext.DynamicMarkers.OnMarkerRemoved -= OnMarkerRemoved;
                mapSystemContext.DynamicMarkers.OnMarkerUpdated -= OnMarkerUpdated;
                mapSystemContext.StaticMarkers.OnStaticMarkerStateChanged -= OnStaticMarkerStateChanged;
            }

            if (languageChangeListener != null)
                languageChangeListener.onLanguageChanged.RemoveListener(OnLanguageChanged);

            ClearViews();
            mapSystemContext = null;
            bound = false;
        }

        private void OnMapShutdown()
        {
            Unbind();
        }

        public void Select(string markerId)
        {
            if (selectedMarkerId != null && views.TryGetValue(selectedMarkerId, out var prev))
                prev.SetSelected(false);

            selectedMarkerId = markerId;

            if (views.TryGetValue(markerId, out var next))
                next.SetSelected(true);
        }

        public void Deselect()
        {
            if (selectedMarkerId != null && views.TryGetValue(selectedMarkerId, out var view))
                view.SetSelected(false);

            selectedMarkerId = null;
        }

        public bool TryGetView(string markerId, out MapMarkerView view)
            => views.TryGetValue(markerId, out view);

        private void OnMarkerAdded(MapMarkerBase marker) => RefreshMarkerView(marker);
        private void OnMarkerRemoved(string markerId) => DestroyView(markerId);
        private void OnMarkerUpdated(MapMarkerBase marker) => RefreshMarkerView(marker);
        private void OnStaticMarkerStateChanged(StaticMarker marker) => RefreshMarkerView(marker);

        private void RefreshMarkerView(MapMarkerBase marker)
        {
            if (marker == null || mapSystemContext == null || !islandsRect)
                return;

            if (marker is AreaMarker) return;

            if (marker.State == MarkerState.Hidden)
            {
                DestroyView(marker.MarkerId);
                return;
            }

            if (!views.TryGetValue(marker.MarkerId, out var view))
                view = SpawnView(marker);

            view.Bind(marker, iconConfig.GetIcon(marker), iconConfig.GetIconSize(marker));
            view.SetAnchoredPosition(ToAnchoredPosition(marker.WorldPosition));
            if (marker is EntityMarker entity && view is IRotatableMarkerView rotatable)
                rotatable.SetRotation(entity.FacingAngle);
            ApplyZoomScale(view);
            view.SetVisible(true);
        }

        private MapMarkerView SpawnView(MapMarkerBase marker)
        {
            var root = GetLayerRoot(marker);
            var go = Instantiate(markerViewPrefab, root);
            var view = go.GetComponent<MapMarkerView>();
            views[marker.MarkerId] = view;

            if (marker is EntityMarker entity)
                TrackEntity(entity);

            ApplyZoomScale(view);
            return view;
        }

        private void SubscribeToZoom()
        {
            if (subscribedToZoom)
                return;

            if (!TryResolveViewController())
                return;

            viewController.OnZoomChanged -= OnZoomChanged;
            viewController.OnZoomChanged += OnZoomChanged;
            subscribedToZoom = true;
            UpdateMarkerZoomScale(force: true);
        }

        private void UnsubscribeFromZoom()
        {
            if (subscribedToZoom && viewController != null)
                viewController.OnZoomChanged -= OnZoomChanged;

            subscribedToZoom = false;
        }

        private bool TryResolveViewController()
        {
            if (viewController != null)
                return true;

            viewController = GetComponentInParent<MapViewController>(true);
            if (viewController != null)
                return true;

            viewController = FindFirstObjectByType<MapViewController>(FindObjectsInactive.Include);
            return viewController != null;
        }

        private void OnZoomChanged(float _)
        {
            UpdateMarkerZoomScale(force: false);
        }

        private void UpdateMarkerZoomScale(bool force)
        {
            float scale = GetCurrentMarkerZoomScale();
            if (!force && !float.IsNaN(lastAppliedMarkerScale) &&
                Mathf.Abs(scale - lastAppliedMarkerScale) < markerScaleEpsilon)
            {
                return;
            }

            lastAppliedMarkerScale = scale;

            foreach (var view in views.Values)
                ApplyZoomScale(view, scale);
        }

        private void ApplyZoomScale(MapMarkerView view)
        {
            ApplyZoomScale(view, GetCurrentMarkerZoomScale());
        }

        private static void ApplyZoomScale(MapMarkerView view, float scale)
        {
            if (view == null)
                return;

            view.SetMapZoomScale(scale);
        }

        private float GetCurrentMarkerZoomScale()
        {
            if (!enableDynamicMarkerScaling || viewController == null)
                return 1f;

            float visualScale = Mathf.Lerp(
                zoomedOutMarkerScale,
                zoomedInMarkerScale,
                viewController.NormalizedZoom);

            return visualScale / Mathf.Max(0.0001f, viewController.CurrentZoom);
        }

        private void DestroyView(string markerId)
        {
            if (!views.TryGetValue(markerId, out var view)) return;

            UntrackEntity(markerId);

            views.Remove(markerId);
            if (view != null)
                Destroy(view.gameObject);
        }

        private void OnEntityPositionChanged(EntityMarker entity)
        {
            if (!isActiveAndEnabled || entity == null || !islandsRect)
                return;

            if (!views.TryGetValue(entity.MarkerId, out var view)) return;
            view.SetAnchoredPosition(ToAnchoredPosition(entity.WorldPosition));
            if (view is IRotatableMarkerView rotatable)
                rotatable.SetRotation(entity.FacingAngle);
        }

        private Transform GetLayerRoot(MapMarkerBase marker) => marker.MarkerType switch
        {
            MarkerType.Player or MarkerType.Teammate or MarkerType.Companion => entityLayerRoot,
            MarkerType.QuestObjective => questLayerRoot,
            MarkerType.POI => poiLayerRoot,
            _ => iconLayerRoot
        };

        private Vector2 ToAnchoredPosition(Vector3 worldPos)
        {
            if (mapSystemContext?.Projector == null || !islandsRect)
                return Vector2.zero;

            Vector2 uv = mapSystemContext.Projector.WorldToNormalized(worldPos);

            Vector3 uiWorld = MapCoordinateCalculator.MapUVToUIWorldPoint(uv, islandsRect);

            return islandsRect.InverseTransformPoint(uiWorld);
        }

        private void TrackEntity(EntityMarker entity)
        {
            if (entity == null)
                return;

            UntrackEntity(entity.MarkerId);

            entity.OnPositionChanged -= OnEntityPositionChanged;
            entity.OnPositionChanged += OnEntityPositionChanged;
            trackedEntities[entity.MarkerId] = entity;
        }

        private void UntrackEntity(string markerId)
        {
            if (string.IsNullOrEmpty(markerId))
                return;

            if (!trackedEntities.TryGetValue(markerId, out var entity))
                return;

            entity.OnPositionChanged -= OnEntityPositionChanged;
            trackedEntities.Remove(markerId);
        }

        private void ClearViews()
        {
            foreach (var entity in trackedEntities.Values)
            {
                if (entity != null)
                    entity.OnPositionChanged -= OnEntityPositionChanged;
            }

            trackedEntities.Clear();

            foreach (var view in views.Values)
            {
                if (view != null)
                    Destroy(view.gameObject);
            }

            views.Clear();
            selectedMarkerId = null;
        }

        private void OnLanguageChanged(string _)
        {
            RefreshAllMarkerDisplayNames();
        }

        private void RefreshAllMarkerDisplayNames()
        {
            if (mapSystemContext == null)
                return;

            Debug.Log("Refreshing marker display names for new language");

            foreach (var poi in mapSystemContext.StaticMarkers.GetAll())
            {
                if (string.IsNullOrEmpty(poi.DisplayNameLocalizationKey)) continue;
                poi.DisplayName = LocalizationService.Get("POIs", poi.DisplayNameLocalizationKey, poi.DisplayName);
                poi.Description = LocalizationService.Get("POIs", poi.DescriptionLocalizationKey, poi.Description);
                RefreshMarkerView(poi);
            }

            foreach (var marker in mapSystemContext.DynamicMarkers.GetAll())
            {
                if (string.IsNullOrEmpty(marker.DisplayNameLocalizationKey)) continue;

                if (marker is EntityMarker entity && entity.MarkerType == MarkerType.Teammate)
                {
                    string prefix = LocalizationService.Get("POIs", marker.DisplayNameLocalizationKey, "Player");
                    marker.DisplayName = $"{prefix} {entity.MarkerId}";
                }
                else
                {
                    marker.DisplayName = LocalizationService.Get("POIs", marker.DisplayNameLocalizationKey, marker.DisplayName);
                }

                RefreshMarkerView(marker);
            }
        }
    }
}
