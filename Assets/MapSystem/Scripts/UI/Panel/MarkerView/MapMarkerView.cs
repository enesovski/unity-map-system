using System;
using Artifika.Map.Core;
using Artifika.Map.View;
using TMPro;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.UI;

namespace Artifika.Map.UI
{
    [RequireComponent(typeof(RectTransform))]
    public class MapMarkerView : MonoBehaviour, IMarkerView, IRotatableMarkerView
    {
        public Image Icon;
        [FormerlySerializedAs("rotationRoot")] [SerializeField] private RectTransform iconRoot;

        [SerializeField] private MarkerVisuals markerVisuals;

        public event Action<MapMarkerView> OnHoverEnter;
        public event Action<MapMarkerView> OnHoverExit;
        public event Action<MapMarkerView> OnSelected;

        public string MarkerId { get; private set; }

        private RectTransform rectTransform;

        private void Awake()
        {
            rectTransform = GetComponent<RectTransform>();

            if (Icon != null)
                Icon.raycastTarget = true;
        }

        public void Bind(MapMarkerBase marker, Sprite markerIcon)
        {
            Bind(marker, markerIcon, MapIconConfig.DefaultIconSize);
        }

        public void Bind(MapMarkerBase marker, Sprite markerIcon, float markerSize)
        {
            MarkerId = marker.MarkerId;
            Icon.sprite = markerIcon;
            rectTransform.sizeDelta = Vector2.one * markerSize;
            
            ApplyStateColor(marker.State);
        }

        public void SetAnchoredPosition(Vector2 position)
        {
            rectTransform.anchoredPosition = position;
        }

        public void SetMapZoomScale(float scale)
        {
            if (rectTransform == null)
                rectTransform = GetComponent<RectTransform>();

            rectTransform.localScale = Vector3.one * scale;
        }

        public void SetSelected(bool selected)
        {
            if (selected)
                OnSelected?.Invoke(this);
        }

        public void SetHovered(bool hovered)
        {
            if (hovered) OnHoverEnter?.Invoke(this);
            else OnHoverExit?.Invoke(this);
        }

        public void SetVisible(bool visible)
        {
            gameObject.SetActive(visible);
        }

        public void SetRotation(float angleDegrees)
        {
            if (iconRoot)
                iconRoot.localRotation = Quaternion.Euler(0f, 0f, -angleDegrees);
        }


        private void ApplyStateColor(MarkerState state)
        {
            Icon.color = state switch
            {
                MarkerState.Cleared => new Color(0.5f, 0.5f, 0.5f, 0.6f),
                MarkerState.Disabled => new Color(1f, 1f, 1f, 0.3f),
                _ => Color.white
            };
        }
    }
}
