using System.Collections.Generic;
using Artifika.Map.Core;
using Sirenix.OdinInspector;
using UnityEngine;

namespace Artifika.Map.UI
{
    public class AreaLayerPresenter : MonoBehaviour
    {
        [Title("References")] [SerializeField]
        private RectTransform mapContentRoot;

        [SerializeField] private RectTransform islandsRect;

        [SerializeField] private Transform areaLayerRoot;
        [SerializeField] private MapAreaConfig areaConfig;

        [Title("Prefab")] [SerializeField, Required]
        private AreaMarkerView areaViewPrefab;

        private MapSystemContext mapSystemContext;
        private readonly Dictionary<string, AreaMarkerView> views = new();
        private bool bound;

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
            Unbind();
        }

        private void Bind(MapSystemContext context)
        {
            if (bound)
                return;

            mapSystemContext = context;
            bound = true;

            MapSystemContext.OnInitialized -= Bind;
            MapSystemContext.OnShutdown -= OnMapShutdown;
            MapSystemContext.OnShutdown += OnMapShutdown;

            mapSystemContext.StaticMarkers.OnStaticMarkerStateChanged += OnStateChanged;

            foreach (var marker in mapSystemContext.StaticMarkers.GetAll())
                if (marker is AreaMarker area)
                    RefreshView(area);
        }

        private void Unbind()
        {
            if (mapSystemContext != null)
                mapSystemContext.StaticMarkers.OnStaticMarkerStateChanged -= OnStateChanged;

            ClearViews();
            mapSystemContext = null;
            bound = false;
        }

        private void OnMapShutdown()
        {
            Unbind();
        }

        private void OnStateChanged(StaticMarker marker)
        {
            if (marker is AreaMarker area) RefreshView(area);
        }

        private void RefreshView(AreaMarker marker)
        {
            bool show = marker.State == MarkerState.Visible
                        || marker.State == MarkerState.Active;

            if (!show)
            {
                DestroyView(marker.MarkerId);
                return;
            }

            if (!views.TryGetValue(marker.MarkerId, out var view))
                view = SpawnView(marker.MarkerId);

            float diameter = WorldRadiusToCanvasDiameter(marker.WorldRadius);
            Color outlineColor = areaConfig.GetOutlineColor(marker.MarkerType);
            Color fillColor = areaConfig.GetFillColor(marker.MarkerType);

            view.Bind(marker, diameter, outlineColor, fillColor, areaConfig.DefaultMask);
            view.SetAnchoredPosition(ToAnchoredPosition(marker.WorldPosition));
            view.SetVisible(true);
        }

        private AreaMarkerView SpawnView(string markerId)
        {
            var view = Instantiate(areaViewPrefab, areaLayerRoot);
            views[markerId] = view;
            return view;
        }

        private void DestroyView(string markerId)
        {
            if (!views.TryGetValue(markerId, out var view)) return;
            views.Remove(markerId);
            if (view != null)
                Destroy(view.gameObject);
        }

        private void ClearViews()
        {
            foreach (var view in views.Values)
            {
                if (view != null)
                    Destroy(view.gameObject);
            }

            views.Clear();
        }

        private float WorldRadiusToCanvasDiameter(float worldRadius)
        {
            if (mapSystemContext?.Projector == null || islandsRect == null)
                return 0f;

            float uvRadius = worldRadius / mapSystemContext.Projector.Projection.WorldBounds.width;
            return uvRadius * islandsRect.rect.width * 2f;
        }

        private Vector2 ToAnchoredPosition(Vector3 worldPos)
        {
            if (mapSystemContext?.Projector == null || islandsRect == null)
                return Vector2.zero;

            Vector2 uv = mapSystemContext.Projector.WorldToNormalized(worldPos);

            Vector3 uiWorld = MapCoordinateCalculator.MapUVToUIWorldPoint(uv, islandsRect);

            return islandsRect.InverseTransformPoint(uiWorld);
        }
    }
}
