using Artifika.Map.Core;
using Sirenix.OdinInspector;
using UnityEngine;

namespace Artifika.Map.World
{
    public class MapAreaAnchor : MapMarkerAnchorBase
    {
        [Title("Area Settings")] [SerializeField]
        private string areaId;

        [SerializeField] private MarkerType areaType = MarkerType.CorruptedArea;

        [SerializeField, MinValue(1f)] private float worldRadius = 50f;

        [SerializeField] [Tooltip("Initial visibility state when first registered.")]
        private MarkerState initialState = MarkerState.Visible;

        [SerializeField] [Tooltip("Optional mask sprite override. Falls back to MapAreaConfig.DefaultMask if empty.")]
        private Sprite maskSpriteOverride;

        public override MarkerType Type => areaType;

        private IStaticMarkerRepository staticMarkers;

        public override void Register()
        {
            if (!Validate()) return;

            staticMarkers = Context.StaticMarkers;

            if (staticMarkers.GetById(areaId) != null) return;

            var marker = new AreaMarker(areaId, areaType, worldRadius, maskSpriteOverride)
            {
                WorldPosition = transform.position,
                DisplayName = gameObject.name,
                State = initialState
            };

            staticMarkers.Register(marker);
        }

        public override void Unregister()
        {
            if (staticMarkers == null || string.IsNullOrWhiteSpace(areaId)) return;

            staticMarkers.SetState(areaId, MarkerState.Hidden);
            staticMarkers.Unregister(areaId);
        }


        private bool Validate()
        {
            if (Context == null) return false;

            if (string.IsNullOrWhiteSpace(areaId))
            {
                Debug.LogError($"[MapAreaAnchor] areaId is empty on '{gameObject.name}'.", this);
                return false;
            }

            return true;
        }

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            var color = areaType == MarkerType.CorruptedArea
                ? new Color(0.55f, 0f, 0.8f, 0.5f)
                : new Color(1f, 0.85f, 0f, 0.5f);

            Gizmos.color = color;
            DrawWireCircle(transform.position, worldRadius, 48);
        }

        private static void DrawWireCircle(Vector3 center, float radius, int segments)
        {
            float step = 2f * Mathf.PI / segments;
            var prev = center + new Vector3(radius, 0f, 0f);
            for (int i = 1; i <= segments; i++)
            {
                float a = i * step;
                var next = center + new Vector3(Mathf.Cos(a) * radius, 0f, Mathf.Sin(a) * radius);
                Gizmos.DrawLine(prev, next);
                prev = next;
            }
        }

#endif
    }
}