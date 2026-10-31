using Artifika.Map.World;
using Artifika.Quests;
using UnityEngine;

namespace Artifika.Map.Core
{
    public interface IMarkerFactory
    {
        void SetMarkerState(string markerId, MarkerState state);
        void RemoveMarker(string markerId, MarkerReplicationMode replicationMode = MarkerReplicationMode.LocalOnly);

        string PlacePin(Vector3 worldPos, string displayName = "", MarkerReplicationMode replicationMode = MarkerReplicationMode.LocalOnly);
        string AddPlacedMarker(MarkerType type, Vector3 worldPos, string displayName = "", MarkerReplicationMode replicationMode = MarkerReplicationMode.LocalOnly);
        void UpsertReplicatedPlacedMarker(string markerId, MarkerType type, Vector3 worldPos, string displayName, MarkerState state);
        void RemoveReplicatedMarker(string markerId);

        /// <summary>
        /// Recreates a PlacedMarker from save data, preserving its original ID.
        /// </summary>
        void RestorePlacedMarker(PlacedMarkerSaveData data);
    }

    public interface IMapNetworkMarkerService
    {
        bool CanReplicate(MarkerType markerType);
        bool TryRequestCreatePlacedMarker(
            MarkerType markerType,
            Vector3 worldPos,
            string displayName,
            MarkerState state,
            out string markerId);
        bool TryRequestRemoveMarker(string markerId);
    }

    public static class MapNetworkMarkerServiceLocator
    {
        public static IMapNetworkMarkerService Current { get; private set; }

        public static void Register(IMapNetworkMarkerService service)
        {
            Current = service;
        }

        public static void Unregister(IMapNetworkMarkerService service)
        {
            if (ReferenceEquals(Current, service))
                Current = null;
        }
    }

    public class MarkerFactory : IMarkerFactory
    {
        private readonly IDynamicMarkerRepository repository;
        private IMapNetworkMarkerService networkMarkerService;

        public MarkerFactory(IDynamicMarkerRepository repository)
        {
            this.repository = repository;
        }

        public void SetNetworkMarkerService(IMapNetworkMarkerService service)
        {
            networkMarkerService = service;
        }

        public void SetMarkerState(string markerId, MarkerState state)
        {
            var marker = repository.GetById(markerId);
            if (marker == null) return;

            marker.State = state;
            repository.NotifyUpdated(markerId);
        }

        public void RemoveMarker(string markerId, MarkerReplicationMode replicationMode = MarkerReplicationMode.LocalOnly)
        {
            var marker = repository.GetById(markerId);
            var effectiveMode = marker != null && marker.ReplicationMode == MarkerReplicationMode.SharedServerAuthoritative
                ? MarkerReplicationMode.SharedServerAuthoritative
                : replicationMode;

            if (effectiveMode == MarkerReplicationMode.SharedServerAuthoritative)
            {
                var service = networkMarkerService ?? MapNetworkMarkerServiceLocator.Current;
                if (service != null && service.TryRequestRemoveMarker(markerId))
                    return;

                GameLog.Warning($"[MarkerFactory] Shared marker removal requested for '{markerId}', but no network marker service is ready.");
                return;
            }

            repository.Unregister(markerId);
        }

        public string PlacePin(
            Vector3 worldPos,
            string displayName = "",
            MarkerReplicationMode replicationMode = MarkerReplicationMode.LocalOnly)
        {
            return AddPlacedMarker(MarkerType.Pin, worldPos, displayName, replicationMode, MarkerState.Tracked);
        }

        public string AddPlacedMarker(
            MarkerType type,
            Vector3 worldPos,
            string displayName = "",
            MarkerReplicationMode replicationMode = MarkerReplicationMode.LocalOnly)
        {
            return AddPlacedMarker(type, worldPos, displayName, replicationMode, MarkerState.Visible);
        }

        private string AddPlacedMarker(
            MarkerType type,
            Vector3 worldPos,
            string displayName,
            MarkerReplicationMode replicationMode,
            MarkerState state)
        {
            if (replicationMode == MarkerReplicationMode.SharedServerAuthoritative)
            {
                var service = networkMarkerService ?? MapNetworkMarkerServiceLocator.Current;
                if (service != null &&
                    service.TryRequestCreatePlacedMarker(type, worldPos, displayName, state, out var markerId))
                {
                    return markerId;
                }

                GameLog.Warning($"[MarkerFactory] Shared marker creation requested for '{type}', but no network marker service is ready.");
                return null;
            }

            return RegisterPlacedMarker(null, type, worldPos, displayName, state, MarkerReplicationMode.LocalOnly);
        }

        public void UpsertReplicatedPlacedMarker(
            string markerId,
            MarkerType type,
            Vector3 worldPos,
            string displayName,
            MarkerState state)
        {
            var existing = repository.GetById(markerId) as PlacedMarker;
            if (existing != null)
            {
                existing.WorldPosition = worldPos;
                existing.DisplayName = displayName;
                existing.State = state;
                existing.ReplicationMode = MarkerReplicationMode.SharedServerAuthoritative;
                repository.NotifyUpdated(markerId);
                return;
            }

            RegisterPlacedMarker(markerId, type, worldPos, displayName, state, MarkerReplicationMode.SharedServerAuthoritative);
        }

        public void RemoveReplicatedMarker(string markerId)
        {
            repository.Unregister(markerId);
        }

        private string RegisterPlacedMarker(
            string markerId,
            MarkerType type,
            Vector3 worldPos,
            string displayName,
            MarkerState state,
            MarkerReplicationMode replicationMode)
        {
            var marker = string.IsNullOrWhiteSpace(markerId)
                ? new PlacedMarker(type, displayName)
                : new PlacedMarker(markerId, type, displayName);

            marker.WorldPosition = worldPos;
            marker.State = state;
            marker.ReplicationMode = replicationMode;

            repository.Register(marker);
            return marker.MarkerId;
        }

        public void RestorePlacedMarker(PlacedMarkerSaveData data)
        {
            var marker = new PlacedMarker(data.MarkerId, data.MarkerType, data.DisplayName)
            {
                WorldPosition = data.WorldPosition,
                State = data.State,
                ReplicationMode = data.ReplicationMode
            };

            repository.Register(marker);
        }
    }
}
