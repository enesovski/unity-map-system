using Artifika.Map.Core;
using Artifika.Map.View;
using Sirenix.OdinInspector;
using UnityEngine;

namespace Artifika.Map
{
    public class MapDetailPresenter : MonoBehaviour
    {
        [Title("References")] 
        [SerializeField] private MapIconConfig iconConfig;
        
        private MapSystemContext mapSystemContext;
        private IDetailPanelView panel;

        private void Awake()
        {
            panel = GetComponentInChildren<IDetailPanelView>(includeInactive: true);
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

        public void ShowForMarker(MapMarkerBase marker)
        {
            if (marker == null)
            {
                Hide();
                return;
            }

            if (mapSystemContext == null || panel == null)
                return;

            panel.Show(BuildContext(marker));
        }

        public void Hide() => panel?.Hide();

        private MapMarkerContext BuildContext(MapMarkerBase marker)
        {
            var player = mapSystemContext?.EntityTracking.GetLocalPlayer();
            var playerPos = player?.WorldPosition ?? Vector3.zero;

            var ctx = new MapMarkerContext
            {
                MarkerState = marker.State,
                MarkerType = marker.MarkerType,
                WorldPosition = marker.WorldPosition,
                DistanceFromPlayer = Vector3.Distance(playerPos, marker.WorldPosition),
                Icon = iconConfig.GetIcon(marker)
            };

            switch (marker)
            {
                case POIMarker poi:
                    FillFromPoi(ctx, poi);
                    break;

                case EntityMarker entity:
                    ctx.Title = entity.DisplayName;
                    ctx.Description = entity.Description;
                    break;

                case PlacedMarker placed:
                    ctx.Title = string.IsNullOrEmpty(placed.DisplayName)
                        ? placed.MarkerType.ToString()
                        : placed.DisplayName;
                    ctx.Description = placed.Description;
                    break;

                case ObjectiveMarker quest:
                    ctx.Title = string.IsNullOrEmpty(quest.DisplayName) ? "Objective" : quest.DisplayName;
                    ctx.Description = quest.Description;

                    // TODO: populate richer quest description from quest system via MapQuestBridge
                    break;

                default:
                    ctx.Title = marker.DisplayName;
                    ctx.Description = marker.Description;
                    break;
            }

            return ctx;
        }

        private void FillFromPoi(MapMarkerContext ctx, POIMarker marker)
        {
            var def = mapSystemContext?.StaticMarkers.GetById(marker.MarkerId);

            ctx.Title = def != null ? def.DisplayName : marker.MarkerId;
            ctx.Description = LocalizationService.Get("POIs", marker.DescriptionLocalizationKey, def?.Description ?? string.Empty);
        }

    }
}
