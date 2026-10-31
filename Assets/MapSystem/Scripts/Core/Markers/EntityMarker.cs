using System;
using UnityEngine;

namespace Artifika.Map.Core
{
    public class EntityMarker : DynamicMarker
    {
        public float FacingAngle { get; set; }
        public EntityType EntityType { get; set; }
        public int PlayerSlot { get; set; }

        public event Action<EntityMarker> OnPositionChanged;

        public EntityMarker(string id, string displayName, EntityType entityType, MarkerType type, int playerSlot = 0)
            : base(id, type)
        {
            DisplayName = displayName;
            EntityType = entityType;
            PlayerSlot = playerSlot;
            State = MarkerState.Visible;
        }

        public void UpdatePosition(Vector3 position, float facingAngle = 0f)
        {
            WorldPosition = position;
            FacingAngle = facingAngle;
            OnPositionChanged?.Invoke(this);
        }
    }
}
