using Artifika.Map.Core;
using UnityEngine;

namespace Artifika.Map
{
    public interface IEntityTrackingService
    {
        void RegisterEntity(string id, string displayName, EntityType entityType, MarkerType type, int playerSlot = 0);
        void UpdateEntityPosition(string id, Vector3 worldPos, float facingAngle = 0f);
        void RemoveEntity(string id);
        EntityMarker GetLocalPlayer();
    }

    public class EntityTrackingService : IEntityTrackingService
    {
        private readonly IDynamicMarkerRepository repository;
        private string localPlayerId;

        public EntityTrackingService(IDynamicMarkerRepository repository)
        {
            this.repository = repository;
        }

        public void RegisterEntity(string id, string displayName, EntityType entityType, MarkerType type, int playerSlot = 0)
        {
            repository.Register(new EntityMarker(id, displayName, entityType, type, playerSlot));

            if (type == MarkerType.Player)
                localPlayerId = id;
        }

        public void UpdateEntityPosition(string id, Vector3 worldPos, float facingAngle = 0f)
        {
            var marker = repository.GetById(id) as EntityMarker;
            if (marker == null) return;

            marker.UpdatePosition(worldPos, facingAngle);
        }

        public void RemoveEntity(string id)
        {
            repository.Unregister(id);
            if (id == localPlayerId) localPlayerId = null;
        }

        public EntityMarker GetLocalPlayer()
        {
            if (localPlayerId == null) return null;
            return repository.GetById(localPlayerId) as EntityMarker;
        }
    }
}
