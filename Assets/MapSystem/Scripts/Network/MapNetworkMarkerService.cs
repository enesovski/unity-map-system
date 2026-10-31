using System.Collections.Generic;
using Artifika.GameFlow;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace Artifika.Map.Core
{
    [DisallowMultipleComponent]
    public sealed class MapNetworkMarkerService : NetworkBehaviour, IGameService, IMapNetworkMarkerService
    {
        [SerializeField] private MarkerType[] sharedMarkerAllowList = { MarkerType.Pin };
        [SerializeField, Min(1)] private int maxDisplayNameLength = 64;
        [SerializeField] private bool allowAnyClientToRemoveSharedMarkers = true;

        private readonly Dictionary<string, NetworkedMarkerPayload> serverMarkers = new();

        private MapSystemContext context;
        private bool initialized;
        private bool boundToMap;
        private bool snapshotRequested;

        public bool IsInitialized => initialized;

        public override void OnNetworkSpawn()
        {
            BindWhenMapReady();

            if (IsServer)
                ImportExistingSharedMarkersOnServer();

            if (IsClient && !IsServer && !snapshotRequested)
            {
                RequestSnapshotServerRpc();
                snapshotRequested = true;
            }
        }

        public override void OnNetworkDespawn()
        {
            UnbindMap();
            serverMarkers.Clear();
            snapshotRequested = false;
        }

        public void ResolveReferences(ServiceContainer container)
        {
            BindWhenMapReady();
        }

        public void Initialize()
        {
            initialized = true;
            BindWhenMapReady();
        }

        public void Shutdown()
        {
            initialized = false;
            UnbindMap();
            serverMarkers.Clear();
            snapshotRequested = false;
        }

        public bool CanReplicate(MarkerType markerType)
        {
            if (sharedMarkerAllowList == null || sharedMarkerAllowList.Length == 0)
                return markerType == MarkerType.Pin;

            foreach (var allowedType in sharedMarkerAllowList)
            {
                if (allowedType == markerType)
                    return true;
            }

            return false;
        }

        public bool TryRequestCreatePlacedMarker(
            MarkerType markerType,
            Vector3 worldPos,
            string displayName,
            MarkerState state,
            out string markerId)
        {
            markerId = null;

            if (!CanReplicate(markerType) || !IsSpawned)
                return false;

            displayName = SanitizeDisplayName(displayName);

            if (IsServer)
            {
                markerId = CreateServerMarker(
                    markerType,
                    worldPos,
                    displayName,
                    state,
                    NetworkManager.Singleton != null ? NetworkManager.Singleton.LocalClientId : NetworkManager.ServerClientId);
                return !string.IsNullOrEmpty(markerId);
            }

            RequestCreatePlacedMarkerServerRpc(
                (int)markerType,
                worldPos,
                new FixedString128Bytes(displayName),
                (int)state);
            return true;
        }

        public bool TryRequestRemoveMarker(string markerId)
        {
            if (string.IsNullOrWhiteSpace(markerId) || !IsSpawned)
                return false;

            if (IsServer)
            {
                RemoveServerMarker(
                    markerId,
                    NetworkManager.Singleton != null ? NetworkManager.Singleton.LocalClientId : NetworkManager.ServerClientId);
                return true;
            }

            RequestRemoveMarkerServerRpc(new FixedString128Bytes(markerId));
            return true;
        }

        private void BindWhenMapReady()
        {
            if (boundToMap)
                return;

            if (MapSystemContext.TryGetInitialized(out var mapContext))
            {
                BindMap(mapContext);
                return;
            }

            MapSystemContext.OnInitialized -= BindMap;
            MapSystemContext.OnInitialized += BindMap;
        }

        private void BindMap(MapSystemContext mapContext)
        {
            if (boundToMap)
                return;

            context = mapContext;
            boundToMap = true;

            MapSystemContext.OnInitialized -= BindMap;
            MapSystemContext.OnShutdown -= OnMapShutdown;
            MapSystemContext.OnShutdown += OnMapShutdown;

            MapNetworkMarkerServiceLocator.Register(this);
            if (context.MarkerFactory is MarkerFactory markerFactory)
                markerFactory.SetNetworkMarkerService(this);

            if (IsServer)
                ImportExistingSharedMarkersOnServer();
        }

        private void UnbindMap()
        {
            MapSystemContext.OnInitialized -= BindMap;
            MapSystemContext.OnShutdown -= OnMapShutdown;
            MapNetworkMarkerServiceLocator.Unregister(this);

            if (context?.MarkerFactory is MarkerFactory markerFactory)
                markerFactory.SetNetworkMarkerService(null);

            context = null;
            boundToMap = false;
        }

        private void OnMapShutdown()
        {
            UnbindMap();
        }

        [ServerRpc(RequireOwnership = false)]
        private void RequestCreatePlacedMarkerServerRpc(
            int markerTypeValue,
            Vector3 worldPos,
            FixedString128Bytes displayName,
            int stateValue,
            ServerRpcParams rpcParams = default)
        {
            var markerType = (MarkerType)markerTypeValue;
            if (!CanReplicate(markerType))
                return;

            CreateServerMarker(
                markerType,
                worldPos,
                displayName.ToString(),
                (MarkerState)stateValue,
                rpcParams.Receive.SenderClientId);
        }

        [ServerRpc(RequireOwnership = false)]
        private void RequestRemoveMarkerServerRpc(
            FixedString128Bytes markerId,
            ServerRpcParams rpcParams = default)
        {
            RemoveServerMarker(markerId.ToString(), rpcParams.Receive.SenderClientId);
        }

        [ServerRpc(RequireOwnership = false)]
        private void RequestSnapshotServerRpc(ServerRpcParams rpcParams = default)
        {
            ImportExistingSharedMarkersOnServer();

            var target = new ClientRpcParams
            {
                Send = new ClientRpcSendParams
                {
                    TargetClientIds = new[] { rpcParams.Receive.SenderClientId }
                }
            };

            foreach (var payload in serverMarkers.Values)
                UpsertPlacedMarkerClientRpc(payload, target);
        }

        [ClientRpc]
        private void UpsertPlacedMarkerClientRpc(
            NetworkedMarkerPayload payload,
            ClientRpcParams rpcParams = default)
        {
            ApplyPayload(payload);
        }

        [ClientRpc]
        private void RemoveMarkerClientRpc(
            FixedString128Bytes markerId,
            ClientRpcParams rpcParams = default)
        {
            RemoveLocal(markerId.ToString());
        }

        private string CreateServerMarker(
            MarkerType markerType,
            Vector3 worldPos,
            string displayName,
            MarkerState state,
            ulong ownerClientId)
        {
            if (!CanReplicate(markerType))
                return null;

            var payload = new NetworkedMarkerPayload
            {
                MarkerId = new FixedString128Bytes(System.Guid.NewGuid().ToString()),
                MarkerType = (int)markerType,
                WorldPosition = worldPos,
                DisplayName = new FixedString128Bytes(SanitizeDisplayName(displayName)),
                State = (int)state,
                OwnerClientId = ownerClientId
            };

            string markerId = payload.MarkerId.ToString();
            serverMarkers[markerId] = payload;
            ApplyPayload(payload);
            UpsertPlacedMarkerClientRpc(payload);
            return markerId;
        }

        private void RemoveServerMarker(string markerId, ulong requesterClientId)
        {
            if (!serverMarkers.TryGetValue(markerId, out var payload))
                return;

            if (!allowAnyClientToRemoveSharedMarkers && payload.OwnerClientId != requesterClientId)
                return;

            serverMarkers.Remove(markerId);
            RemoveLocal(markerId);
            RemoveMarkerClientRpc(new FixedString128Bytes(markerId));
        }

        private void ImportExistingSharedMarkersOnServer()
        {
            if (!IsServer || context?.DynamicMarkers == null)
                return;

            foreach (var marker in context.DynamicMarkers.GetAllOfType<PlacedMarker>())
            {
                if (marker.ReplicationMode != MarkerReplicationMode.SharedServerAuthoritative ||
                    !CanReplicate(marker.MarkerType) ||
                    serverMarkers.ContainsKey(marker.MarkerId))
                {
                    continue;
                }

                serverMarkers[marker.MarkerId] = new NetworkedMarkerPayload
                {
                    MarkerId = new FixedString128Bytes(marker.MarkerId),
                    MarkerType = (int)marker.MarkerType,
                    WorldPosition = marker.WorldPosition,
                    DisplayName = new FixedString128Bytes(SanitizeDisplayName(marker.DisplayName)),
                    State = (int)marker.State,
                    OwnerClientId = NetworkManager.ServerClientId
                };
            }
        }

        private void ApplyPayload(NetworkedMarkerPayload payload)
        {
            if (context?.MarkerFactory == null)
                return;

            context.MarkerFactory.UpsertReplicatedPlacedMarker(
                payload.MarkerId.ToString(),
                (MarkerType)payload.MarkerType,
                payload.WorldPosition,
                payload.DisplayName.ToString(),
                (MarkerState)payload.State);
        }

        private void RemoveLocal(string markerId)
        {
            context?.MarkerFactory?.RemoveReplicatedMarker(markerId);
        }

        private string SanitizeDisplayName(string displayName)
        {
            displayName = displayName?.Trim() ?? string.Empty;
            if (displayName.Length <= maxDisplayNameLength)
                return displayName;

            return displayName.Substring(0, maxDisplayNameLength);
        }

        public struct NetworkedMarkerPayload : INetworkSerializable
        {
            public FixedString128Bytes MarkerId;
            public int MarkerType;
            public Vector3 WorldPosition;
            public FixedString128Bytes DisplayName;
            public int State;
            public ulong OwnerClientId;

            public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
            {
                serializer.SerializeValue(ref MarkerId);
                serializer.SerializeValue(ref MarkerType);
                serializer.SerializeValue(ref WorldPosition);
                serializer.SerializeValue(ref DisplayName);
                serializer.SerializeValue(ref State);
                serializer.SerializeValue(ref OwnerClientId);
            }
        }
    }
}
