using System;
using Artifika.Map.Core;
using Sirenix.OdinInspector;
using TMPro;
using UnityEngine;

namespace Artifika.Map.UI
{
    [RequireComponent(typeof(CanvasGroup))]
    public class MapTooltip : MonoBehaviour
    {
        [Title("UI")]
        [SerializeField] private TMP_Text nameText;
        [SerializeField] private TMP_Text distanceText;
        [Space]
        [SerializeField] private Vector2 cursorOffset = new(12f, -12f);

        private MapSystemContext mapSystemContext;
        
        private RectTransform tooltipRect;
        private CanvasGroup canvasGroup;
        private bool isVisible;
        
        private void Awake()
        {
            tooltipRect = GetComponent<RectTransform>();
            canvasGroup = GetComponent<CanvasGroup>();
            Hide();        
        }

        private void OnEnable()
        {
            if (MapSystemContext.TryGetInitialized(out var context))
            {
                BindMapContext(context);
                return;
            }

            MapSystemContext.OnInitialized -= BindMapContext;
            MapSystemContext.OnInitialized += BindMapContext;
        }

        private void OnDisable()
        {
            MapSystemContext.OnInitialized -= BindMapContext;
            MapSystemContext.OnShutdown -= OnMapShutdown;
            mapSystemContext = null;
        }

        private void BindMapContext(MapSystemContext context)
        {
            mapSystemContext = context;
            MapSystemContext.OnInitialized -= BindMapContext;
            MapSystemContext.OnShutdown -= OnMapShutdown;
            MapSystemContext.OnShutdown += OnMapShutdown;
        }

        private void OnMapShutdown()
        {
            mapSystemContext = null;
            Hide();
        }

        private void Update()
        {
            if (isVisible)
                FollowCursor();
        }

        public void ShowForMarker(MapMarkerBase marker, Vector2 screenPos)
        {
            nameText.text = ResolveName(marker);
            distanceText.text = ResolveDistance(marker);

            SetVisible(true);
            FollowCursor();
        }

        public void Hide() => SetVisible(false);


        private void FollowCursor()
        {
            tooltipRect.position = (Vector2)Input.mousePosition + cursorOffset;
            ClampToScreen();
        }

        private void ClampToScreen()
        {
            var pos = tooltipRect.position;
            var size = tooltipRect.rect.size;

            pos.x = Mathf.Clamp(pos.x, 0f, Screen.width - size.x);
            pos.y = Mathf.Clamp(pos.y, size.y, Screen.height);

            tooltipRect.position = pos;
        }

        private string ResolveName(MapMarkerBase marker) => marker.DisplayName;

        private string ResolveDistance(MapMarkerBase marker)
        {
            if (mapSystemContext == null)
                return string.Empty;

            var player = mapSystemContext.EntityTracking.GetLocalPlayer();
            if (player == null) return string.Empty;

            float dist = Vector3.Distance(player.WorldPosition, marker.WorldPosition);
            return dist >= 1000f ? $"{dist / 1000f:F1} km" : $"{Mathf.RoundToInt(dist)} m";
        }

        private void SetVisible(bool visible)
        {
            isVisible = visible;
            canvasGroup.alpha = visible ? 1f : 0f;
            canvasGroup.blocksRaycasts = false;
        }
    }
}
