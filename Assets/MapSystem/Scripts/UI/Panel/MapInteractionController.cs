using System.Collections.Generic;
using Artifika.Map.Core;
using Sirenix.OdinInspector;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Artifika.Map.UI
{
    public class MapInteractionController : MonoBehaviour
    {
        [Title("UI")]
        [SerializeField] private RectTransform mapContentRoot;

        [SerializeField] private RectTransform islandRect;
        [SerializeField] private MapDetailPresenter detailPresenter;
        [SerializeField] private MapTooltip tooltip;
        [SerializeField] private MapPinLabelInput pinLabelInput;

        [Title("References")]
        [SerializeField] private MarkerLayerPresenter markerPresenter;

        [Title("Raycast Optimization")]
        [SerializeField] private bool disableNonMarkerRaycastTargets = true;

        private MapSystemContext mapSystemContext;

        private string selectedMarkerId;
        private string hoveredMarkerId;
        private readonly List<RaycastResult> raycastResults = new();

        private void Awake()
        {
            if (disableNonMarkerRaycastTargets)
                DisableNonMarkerRaycastTargets();
        }

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

        public void OnLeftClick(Vector2 screenPos)
        {
            if (mapSystemContext == null)
                return;

            var view = HitTest(screenPos);
            if (view != null)
            {
                SelectMarker(view.MarkerId);
                var marker = ResolveMarker(view.MarkerId);
                if (marker != null) detailPresenter.ShowForMarker(marker);
                return;
            }

            Deselect();
            detailPresenter.Hide();
        }

        public void OnRightClick(Vector2 screenPos)
        {
            if (mapSystemContext == null)
                return;

            var view = HitTest(screenPos);
            if (view != null)
            {
                bool consumed = HandleRightClickOnMarker(view);
                if (consumed) return;
            }

            pinLabelInput.Show(ScreenToWorldPosition(screenPos));
        }

        private bool HandleRightClickOnMarker(MapMarkerView view)
        {
            var marker = ResolveMarker(view.MarkerId);

            if (marker is PlacedMarker { MarkerType: MarkerType.Pin })
            {
                mapSystemContext.MarkerFactory.RemoveMarker(view.MarkerId);
                return true;
            }

            return false;
        }

        public void OnHover(Vector2 screenPos)
        {
            if (mapSystemContext == null)
                return;

            var view = HitTest(screenPos);

            if (view != null)
            {
                if (hoveredMarkerId == view.MarkerId) return;

                if (hoveredMarkerId != null)
                    FindView(hoveredMarkerId)?.SetHovered(false);

                hoveredMarkerId = view.MarkerId;
                view.SetHovered(true);

                var marker = ResolveMarker(view.MarkerId);
                if (marker != null) tooltip.ShowForMarker(marker, screenPos);
                return;
            }

            if (hoveredMarkerId != null)
            {
                FindView(hoveredMarkerId)?.SetHovered(false);
                hoveredMarkerId = null;
            }

            tooltip.Hide();
        }

        private void SelectMarker(string markerId)
        {
            if (selectedMarkerId == markerId) return;
            Deselect();
            selectedMarkerId = markerId;
            markerPresenter.Select(markerId);
        }

        private void Deselect()
        {
            if (selectedMarkerId == null) return;
            markerPresenter.Deselect();
            selectedMarkerId = null;
        }

        private MapMarkerView HitTest(Vector2 screenPos)
        {
            if (EventSystem.current == null) return null;

            var eventData = new PointerEventData(EventSystem.current) { position = screenPos };
            raycastResults.Clear();
            EventSystem.current.RaycastAll(eventData, raycastResults);

            foreach (var result in raycastResults)
            {
                var view = result.gameObject.GetComponentInParent<MapMarkerView>();
                if (view != null) return view;
            }

            return null;
        }

        private void DisableNonMarkerRaycastTargets()
        {
            if (mapContentRoot == null)
                return;

            var graphics = mapContentRoot.GetComponentsInChildren<Graphic>(includeInactive: true);
            foreach (var graphic in graphics)
            {
                if (graphic == null || graphic.GetComponentInParent<MapMarkerView>(includeInactive: true) != null)
                    continue;

                graphic.raycastTarget = false;
            }
        }

        private MapMarkerView FindView(string markerId)
        {
            if (markerPresenter == null) return null;
            return markerPresenter.TryGetView(markerId, out var view) ? view : null;
        }

        private MapMarkerBase ResolveMarker(string markerId) => mapSystemContext.MarkerQuery.GetById(markerId);

        private Vector3 ScreenToWorldPosition(Vector2 screenPos)
        {
            if (mapSystemContext?.Projector == null)
                return Vector3.zero;

            Camera eventCamera = islandRect.GetComponentInParent<Canvas>().worldCamera;

            RectTransformUtility.ScreenPointToWorldPointInRectangle(
                islandRect,
                screenPos,
                eventCamera,
                out Vector3 uiWorldPoint
            );

            var corners = new Vector3[4];
            islandRect.GetWorldCorners(corners);

            var uv = new Vector2(
                Mathf.InverseLerp(corners[0].x, corners[2].x, uiWorldPoint.x),
                Mathf.InverseLerp(corners[0].y, corners[2].y, uiWorldPoint.y)
            );

            return mapSystemContext.Projector.NormalizedToWorld(uv);
        }
    }
}
