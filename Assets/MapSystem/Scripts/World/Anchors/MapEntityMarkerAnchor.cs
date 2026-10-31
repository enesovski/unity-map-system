using Artifika.Map.Core;
using Sirenix.OdinInspector;
using UnityEngine;

namespace Artifika.Map.World
{
    public class MapEntityMarkerAnchor : MapMarkerAnchorBase
    {
        [Title("Entity Settings")]
        [SerializeField] private MarkerType markerType = MarkerType.Companion;
        [SerializeField] private EntityType entityType = EntityType.Nimblix;
        [SerializeField] private string displayName;
        [SerializeField] private string displayNameLocalizationKey;

        [Title("Facing Direction")]
        [SerializeField] private Transform facingReference;
        [SerializeField] private bool trackFacingAngle;

        [Title("Map Update Throttling")]
        [SerializeField, Min(0.02f)] private float mapUpdateInterval = 0.15f;
        [SerializeField, Min(0f)] private float minPositionDelta = 0.5f;
        [SerializeField, Min(0f)] private float minFacingDelta = 2f;
        
        public override MarkerType Type => markerType;

        private string markerId;
        private IEntityTrackingService entityTracking;
        private Vector3 lastPublishedPosition;
        private float lastPublishedFacingAngle;
        private float nextMapUpdateTime;
        private bool hasPublishedTransform;


        public override void Register()
        {
            if (Context == null) return;

            entityTracking = Context.EntityTracking;
            markerId = GenerateMarkerId();

            var tempName = string.IsNullOrWhiteSpace(displayName) ? gameObject.name : displayName;
            entityTracking.RegisterEntity(markerId, tempName, entityType, markerType);
            PublishTransform(force: true);

            var marker = Context.DynamicMarkers.GetById(markerId) as EntityMarker;
            if (marker != null)
            {
                marker.DisplayNameLocalizationKey = displayNameLocalizationKey;
            }
        }

        public override void Unregister()
        {
            entityTracking?.RemoveEntity(markerId);
        }


        private void LateUpdate()
        {
            PublishTransform(force: false);
        }

        protected virtual string GenerateMarkerId() => GetInstanceID().ToString();


        private float GetFacingAngle()
        {
            if (!trackFacingAngle) return 0f;
            return (facingReference ? facingReference : transform).eulerAngles.y;
        }

        private void PublishTransform(bool force)
        {
            if (entityTracking == null || string.IsNullOrEmpty(markerId))
                return;

            Vector3 currentPosition = transform.position;
            float currentFacingAngle = GetFacingAngle();

            if (!force && hasPublishedTransform)
            {
                if (Time.unscaledTime < nextMapUpdateTime)
                    return;

                float minPositionDeltaSqr = minPositionDelta * minPositionDelta;
                bool positionChanged = (currentPosition - lastPublishedPosition).sqrMagnitude >= minPositionDeltaSqr;
                bool facingChanged = Mathf.Abs(Mathf.DeltaAngle(lastPublishedFacingAngle, currentFacingAngle)) >= minFacingDelta;

                if (!positionChanged && !facingChanged)
                    return;
            }

            entityTracking.UpdateEntityPosition(markerId, currentPosition, currentFacingAngle);
            lastPublishedPosition = currentPosition;
            lastPublishedFacingAngle = currentFacingAngle;
            nextMapUpdateTime = Time.unscaledTime + mapUpdateInterval;
            hasPublishedTransform = true;
        }
    }
}
