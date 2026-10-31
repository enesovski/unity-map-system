using Artifika.Map.Core;
using Sirenix.OdinInspector;
using UnityEngine;

namespace Artifika.Map.World
{

    public class MapStaticMarkerAnchor : MapMarkerAnchorBase
    {
        [Title("POI Definition")]
        [SerializeField, Required] private MapPOIDefinition poiDefinition;

        [Title("Overrides")]
        [InfoBox("Leave blank to use values from the POI Definition.")]
        [SerializeField] private string displayNameOverride;
        [SerializeField, TextArea(2, 3)] private string descriptionOverride;
        [SerializeField] private bool overrideState;
        [SerializeField, ShowIf(nameof(overrideState))] private MarkerState stateOverride = MarkerState.Visible;

        public override MarkerType Type => MarkerType.POI;

        private IStaticMarkerRepository staticMarkers;
        
        public override void Register()
        {
            if (!ValidateContext()) return;

            staticMarkers = Context.StaticMarkers;
            string markerId = poiDefinition.PoiId;

            var existing = staticMarkers.GetById(markerId) as POIMarker;
            if (existing != null)
            {
                ApplyOverrides(existing);
                return;
            }

            var marker = new POIMarker(markerId, poiDefinition.Category)
            {
                DisplayName = LocalizationService.Get("POIs", poiDefinition.DisplayNameLocalizationKey, poiDefinition.DisplayName),
                DisplayNameLocalizationKey = poiDefinition.DisplayNameLocalizationKey,   // ← add
                Description = LocalizationService.Get("POIs", poiDefinition.DescriptionLocalizationKey, poiDefinition.Description),
                DescriptionLocalizationKey = poiDefinition.DescriptionLocalizationKey,   // ← add
                State = poiDefinition.DefaultState
            };

            ApplyOverrides(marker);
            marker.RegisterWorldPosition(transform.position);
            staticMarkers.Register(marker);
        }

        public override void Unregister()
        {
            staticMarkers?.Unregister(poiDefinition?.PoiId);
        }
        
        private void ApplyOverrides(POIMarker marker)
        {
            if (!string.IsNullOrWhiteSpace(displayNameOverride))
                marker.DisplayName = displayNameOverride;

            if (!string.IsNullOrWhiteSpace(descriptionOverride))
                marker.Description = descriptionOverride;

            if (overrideState)
                marker.State = stateOverride;
        }

        private bool ValidateContext()
        {
            if (Context == null) return false;

            if (poiDefinition == null)
            {
                Debug.LogError($"[MapStaticMarkerAnchor] No POIDefinition assigned on '{gameObject.name}'.", this);
                return false;
            }

            return true;
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (poiDefinition == null)
                Debug.LogWarning($"[MapStaticMarkerAnchor] '{gameObject.name}' has no POIDefinition assigned.", this);
        }
#endif
    }
}