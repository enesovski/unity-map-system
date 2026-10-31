using System;
using System.Collections;
using System.Collections.Generic;
using Artifika.GameFlow;
using Artifika.Map.Core;
using Artifika.Player;
using Unity.Netcode;
using UnityEngine;

namespace Artifika.Map
{
    public sealed class PlayerMapFogDiscoveryPN : NetworkBehaviour, ISaveable, IPlayerNetworkedService
    {
        private const int SnapshotChunkSize = 4 * 1024;
        private static readonly MapFogDiscoveryRegistry FallbackRegistry = new();

        [Header("World")]
        [SerializeField] private MapWorldDefinition worldDefinition;

        [Header("Discovery")]
        [SerializeField, Range(64, 1024)] private int maskResolution = 256;
        [SerializeField, Min(1f)] private float revealRadius = 45f;
        [SerializeField, Min(0.25f)] private float sampleDistance = 6f;

        [Header("Ownership")]
        [SerializeField] private MapFogScopeType fogScope = MapFogScopeType.Player;
        [SerializeField] private string sharedScopeId = "World";

        [Header("Network Deltas")]
        [SerializeField, Range(8, 64)] private int deltaTileSize = MapFogTileDeltaCodec.DefaultTileSize;
        [SerializeField, Range(1, 64)] private int maxTilesPerDelta = 32;
        [SerializeField, Min(0.05f)] private float deltaSendInterval = 0.2f;

        private readonly List<int> serverDeltaTiles = new();

        private IUniqueIdentifier uniqueIdentifier;
        private PlayerRootNetwork playerRoot;
        private MapFogDiscoveryRegistry fogRegistry;
        private MapFogScopeKey registeredScopeKey;
        private bool hasRegisteredScope;
        private byte[] localMask;
        private int localMaskResolution;
        private Coroutine pushSnapshotCoroutine;
        private uint nextSnapshotId;
        private uint incomingSnapshotId;
        private int incomingSnapshotEncoding;
        private int incomingSnapshotResolution;
        private int incomingSnapshotUncompressedLength;
        private uint incomingSnapshotChecksum;
        private byte[] incomingSnapshotPayload;
        private int incomingSnapshotBytesReceived;
        private Vector3 lastSamplePosition;
        private bool hasSamplePosition;
        private bool localMaskApplied;
        private bool initialized;
        private float nextDeltaSendTime;

        public bool IsInitialized => initialized;
        public LoadPriorityLevel LoadPriority => LoadPriorityLevel.Normal;

        public string SaveKey => $"MapFogDiscovery_{ResolveScopeKey().ToSaveKeySegment()}";

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();

            playerRoot = GetComponentInParent<PlayerRootNetwork>(true);
            ResolveWorldDefinition();
            ResolveFogRegistry();

            if (!IsOwner)
                return;

            MapFogTextureService.InstanceChanged += OnFogTextureServiceChanged;
            EnsureLocalMask();
            TryApplyLocalMask();
        }

        public override void OnNetworkDespawn()
        {
            if (IsOwner)
                MapFogTextureService.InstanceChanged -= OnFogTextureServiceChanged;

            UnregisterFromScope();
            StopPushSnapshot();
            ClearIncomingSnapshot();
            initialized = false;
            base.OnNetworkDespawn();
        }

        public void ResolveReferences(ServiceContainer container, NetworkObject playerNetObj)
        {
            uniqueIdentifier = PlayerSaveIdentity.Resolve(this, playerNetObj);
            playerRoot ??= playerNetObj.GetComponent<PlayerRootNetwork>();
            ResolveWorldDefinition();
            ResolveFogRegistry();
        }

        public void Initialize()
        {
            if (initialized)
                return;

            if (!IsServer)
            {
                GameLog.Error("[PlayerMapFogDiscoveryPN] Initialize must run on the server.");
                return;
            }

            ResolveFogRegistry();
            registeredScopeKey = ResolveScopeKey();
            fogRegistry.RegisterSubscriber(registeredScopeKey, maskResolution, OwnerClientId);
            hasRegisteredScope = true;

            PushMaskToOwner();
            initialized = true;
        }

        public void Shutdown()
        {
            UnregisterFromScope();
            StopPushSnapshot();
            initialized = false;
        }

        private void Update()
        {
            if (IsServer)
                FlushPendingTileDeltas();

            if (!IsOwner ||
                !IsSpawned ||
                incomingSnapshotPayload != null ||
                playerRoot == null ||
                !playerRoot.PlayerReady.Value)
            {
                return;
            }

            if (!TryGetWorldBounds(out Rect worldBounds))
                return;

            Vector3 currentPosition = transform.position;
            if (hasSamplePosition &&
                (currentPosition - lastSamplePosition).sqrMagnitude < sampleDistance * sampleDistance)
            {
                return;
            }

            lastSamplePosition = currentPosition;
            hasSamplePosition = true;

            Vector2 uv = MapCoordinateCalculator.WorldToMapUV(currentPosition, worldBounds);
            if (RevealLocally(uv, worldBounds))
                RevealPointServerRpc(uv);
        }

        private bool RevealLocally(Vector2 uv, Rect worldBounds)
        {
            EnsureLocalMask();

            bool changed = MapFogMaskRasterizer.RevealCircle(
                localMask,
                localMaskResolution,
                uv,
                revealRadius,
                worldBounds,
                out RectInt dirtyRect);

            // Push only the texels that moved. A full upload here would be the mask's entire
            // size on every sample step, which is what makes a high-resolution mask expensive.
            if (changed)
                MapFogTextureService.Instance?.SetMaskRegion(localMask, localMaskResolution, dirtyRect);

            return changed;
        }

        [ServerRpc(RequireOwnership = true)]
        private void RevealPointServerRpc(Vector2 uv)
        {
            if (!IsFinite(uv) || !TryGetWorldBounds(out Rect worldBounds))
                return;

            ResolveFogRegistry();
            fogRegistry.Reveal(
                ResolveScopeKey(),
                maskResolution,
                new Vector2(Mathf.Clamp01(uv.x), Mathf.Clamp01(uv.y)),
                revealRadius,
                worldBounds,
                NormalizeTileSize(deltaTileSize));
        }

        public object RetrieveSaveData()
        {
            ResolveFogRegistry();
            MapFogScopeKey scopeKey = ResolveScopeKey();
            MapFogScopeState state = fogRegistry.GetOrCreate(scopeKey, maskResolution);
            return MapFogDiscoverySaveCodec.Encode(state.GetSnapshot(), state.Resolution, scopeKey);
        }

        public void LoadData(object state)
        {
            if (!IsServer)
                return;

            if (state is not MapFogDiscoverySaveData saveData ||
                !MapFogDiscoverySaveCodec.TryDecode(saveData, out int resolution, out byte[] restoredMask, out _))
            {
                GameLog.Warning($"[PlayerMapFogDiscoveryPN] Ignored invalid save data for {SaveKey}.");
                return;
            }

            maskResolution = resolution;
            ResolveFogRegistry();
            fogRegistry.SetMask(ResolveScopeKey(), resolution, restoredMask);
            PushMaskToOwner();
        }

        private void PushMaskToOwner()
        {
            if (!IsServer || !IsSpawned)
                return;

            ResolveFogRegistry();
            StopPushSnapshot();

            MapFogScopeState state = fogRegistry.GetOrCreate(ResolveScopeKey(), maskResolution);
            MapFogMaskSnapshot snapshot = state.GetSnapshot();
            pushSnapshotCoroutine = StartCoroutine(SendSnapshotToOwner(snapshot, state.Resolution));
        }

        private IEnumerator SendSnapshotToOwner(MapFogMaskSnapshot snapshot, int resolution)
        {
            uint snapshotId = ++nextSnapshotId;
            var rpcParams = new ClientRpcParams
            {
                Send = new ClientRpcSendParams
                {
                    TargetClientIds = new[] { OwnerClientId }
                }
            };

            BeginMaskSnapshotClientRpc(
                snapshotId,
                snapshot.Encoding,
                resolution,
                snapshot.UncompressedLength,
                snapshot.Payload.Length,
                snapshot.Checksum,
                rpcParams);

            for (int offset = 0; offset < snapshot.Payload.Length; offset += SnapshotChunkSize)
            {
                int chunkLength = Mathf.Min(SnapshotChunkSize, snapshot.Payload.Length - offset);
                var chunk = new byte[chunkLength];
                Buffer.BlockCopy(snapshot.Payload, offset, chunk, 0, chunkLength);

                PushMaskSnapshotChunkClientRpc(snapshotId, offset, chunk, rpcParams);
                yield return null;
            }

            pushSnapshotCoroutine = null;
        }

        [ClientRpc]
        private void BeginMaskSnapshotClientRpc(
            uint snapshotId,
            int encoding,
            int resolution,
            int uncompressedLength,
            int payloadLength,
            uint checksum,
            ClientRpcParams rpcParams = default)
        {
            if (!IsOwner ||
                !MapFogMaskCodec.TryGetMaskByteCount(resolution, out int expectedMaskLength) ||
                uncompressedLength != expectedMaskLength ||
                !MapFogMaskCodec.IsValidPayloadLength(payloadLength))
            {
                ClearIncomingSnapshot();
                return;
            }

            incomingSnapshotId = snapshotId;
            incomingSnapshotEncoding = encoding;
            incomingSnapshotResolution = resolution;
            incomingSnapshotUncompressedLength = uncompressedLength;
            incomingSnapshotChecksum = checksum;
            incomingSnapshotPayload = new byte[payloadLength];
            incomingSnapshotBytesReceived = 0;
        }

        [ClientRpc]
        private void PushMaskSnapshotChunkClientRpc(
            uint snapshotId,
            int offset,
            byte[] chunk,
            ClientRpcParams rpcParams = default)
        {
            if (!IsOwner || incomingSnapshotPayload == null || snapshotId != incomingSnapshotId)
                return;

            if (chunk == null ||
                chunk.Length <= 0 ||
                chunk.Length > SnapshotChunkSize ||
                offset != incomingSnapshotBytesReceived ||
                offset + chunk.Length > incomingSnapshotPayload.Length)
            {
                GameLog.Warning("[PlayerMapFogDiscoveryPN] Ignored an invalid fog mask snapshot chunk.");
                ClearIncomingSnapshot();
                return;
            }

            Buffer.BlockCopy(chunk, 0, incomingSnapshotPayload, offset, chunk.Length);
            incomingSnapshotBytesReceived += chunk.Length;

            if (incomingSnapshotBytesReceived != incomingSnapshotPayload.Length)
                return;

            if (!MapFogMaskCodec.TryDecode(
                    incomingSnapshotEncoding,
                    incomingSnapshotPayload,
                    incomingSnapshotUncompressedLength,
                    incomingSnapshotChecksum,
                    out byte[] mask))
            {
                GameLog.Warning("[PlayerMapFogDiscoveryPN] Ignored a corrupt fog mask snapshot.");
                ClearIncomingSnapshot();
                return;
            }

            localMaskResolution = incomingSnapshotResolution;
            localMask = mask;
            localMaskApplied = false;
            ClearIncomingSnapshot();
            TryApplyLocalMask();
        }

        [ClientRpc]
        private void PushMaskTileDeltaClientRpc(
            int encoding,
            int resolution,
            int tileSize,
            int tileCount,
            int uncompressedLength,
            uint checksum,
            byte[] payload,
            ClientRpcParams rpcParams = default)
        {
            if (!IsOwner || incomingSnapshotPayload != null)
                return;

            if (!MapFogMaskCodec.TryGetMaskByteCount(resolution, out int expectedMaskLength))
                return;

            if (localMask == null || localMaskResolution != resolution || localMask.Length != expectedMaskLength)
            {
                localMaskResolution = resolution;
                localMask = new byte[expectedMaskLength];
            }

            if (!MapFogTileDeltaCodec.TryApplyDelta(
                    encoding,
                    resolution,
                    tileSize,
                    tileCount,
                    uncompressedLength,
                    checksum,
                    payload,
                    localMask,
                    mergeMax: true))
            {
                GameLog.Warning("[PlayerMapFogDiscoveryPN] Ignored an invalid fog tile delta.");
                return;
            }

            MapFogTextureService.Instance?.SetMask(localMask, localMaskResolution);
            localMaskApplied = true;
        }

        private void FlushPendingTileDeltas()
        {
            if (!initialized ||
                !hasRegisteredScope ||
                !IsSpawned ||
                pushSnapshotCoroutine != null ||
                Time.unscaledTime < nextDeltaSendTime)
            {
                return;
            }

            nextDeltaSendTime = Time.unscaledTime + deltaSendInterval;

            ResolveFogRegistry();
            MapFogScopeState state = fogRegistry.GetOrCreate(registeredScopeKey, maskResolution);
            int clampedMaxTiles = Mathf.Clamp(maxTilesPerDelta, 1, MapFogTileDeltaCodec.MaxTilesPerDelta);

            if (!state.TryCollectDirtyTiles(OwnerClientId, clampedMaxTiles, serverDeltaTiles))
                return;

            MapFogTileDeltaSnapshot delta;
            try
            {
                delta = MapFogTileDeltaCodec.Encode(
                    state.Mask,
                    state.Resolution,
                    NormalizeTileSize(deltaTileSize),
                    serverDeltaTiles);
            }
            catch (ArgumentException ex)
            {
                GameLog.Warning($"[PlayerMapFogDiscoveryPN] Failed to encode fog tile delta: {ex.Message}");
                return;
            }

            var rpcParams = new ClientRpcParams
            {
                Send = new ClientRpcSendParams
                {
                    TargetClientIds = new[] { OwnerClientId }
                }
            };

            PushMaskTileDeltaClientRpc(
                delta.Encoding,
                delta.Resolution,
                delta.TileSize,
                delta.TileCount,
                delta.UncompressedLength,
                delta.Checksum,
                delta.Payload,
                rpcParams);
        }

        private void OnFogTextureServiceChanged(MapFogTextureService service)
        {
            localMaskApplied = false;
            TryApplyLocalMask();
        }

        private void TryApplyLocalMask()
        {
            if (!IsOwner || localMaskApplied || MapFogTextureService.Instance == null)
                return;

            EnsureLocalMask();
            MapFogTextureService.Instance.SetMask(localMask, localMaskResolution);
            localMaskApplied = true;
        }

        private void EnsureLocalMask()
        {
            if (localMaskResolution <= 0)
                localMaskResolution = NormalizeResolution(maskResolution);

            if (localMask == null || localMask.Length != localMaskResolution * localMaskResolution)
                localMask = new byte[localMaskResolution * localMaskResolution];
        }

        private bool TryGetWorldBounds(out Rect worldBounds)
        {
            // Prefer the projection so the fog and the markers cannot end up describing
            // different worlds. Falls back to the world definition when the map has not
            // initialised yet — a client can sample fog before the map UI exists.
            //
            // NOTE: the mask is rasterised in world-aligned UV, so this deliberately does NOT
            // route through Projector.WorldToNormalized. Map yaw would rotate the mask relative
            // to the world it records. Revisit when the projection stops being legacy.
            IMapProjector projector = MapSystemContext.Instance != null
                ? MapSystemContext.Instance.Projector
                : null;

            if (projector != null)
            {
                worldBounds = projector.Projection.WorldBounds;
                return worldBounds.width > 0f && worldBounds.height > 0f;
            }

            ResolveWorldDefinition();
            worldBounds = worldDefinition != null ? worldDefinition.WorldBoundsRect : default;

            return worldDefinition != null &&
                   worldBounds.width > 0f &&
                   worldBounds.height > 0f;
        }

        private void ResolveWorldDefinition()
        {
            if (worldDefinition == null && MapSystemContext.Instance != null)
                worldDefinition = MapSystemContext.Instance.WorldDefinition;
        }

        private void ResolveFogRegistry()
        {
            fogRegistry = MapSystemContext.Instance != null
                ? MapSystemContext.Instance.FogDiscoveryRegistry
                : FallbackRegistry;
        }

        private MapFogScopeKey ResolveScopeKey()
        {
            if (fogScope == MapFogScopeType.World)
                return new MapFogScopeKey(MapFogScopeType.World, "World");

            if (fogScope == MapFogScopeType.Player)
                return new MapFogScopeKey(MapFogScopeType.Player, ResolveOwnerKey());

            string scopeId = string.IsNullOrWhiteSpace(sharedScopeId)
                ? ResolveOwnerKey()
                : sharedScopeId;

            return new MapFogScopeKey(fogScope, scopeId);
        }

        private string ResolveOwnerKey()
        {
            uniqueIdentifier ??= PlayerSaveIdentity.Resolve(this);
            if (uniqueIdentifier != null)
                return uniqueIdentifier.UniqueID;

            return $"Player_{OwnerClientId}";
        }

        private void UnregisterFromScope()
        {
            if (!hasRegisteredScope)
                return;

            ResolveFogRegistry();
            fogRegistry.UnregisterSubscriber(registeredScopeKey, OwnerClientId);
            hasRegisteredScope = false;
        }

        private static int NormalizeResolution(int resolution)
        {
            return Mathf.Clamp(
                resolution,
                MapFogMaskCodec.MinResolution,
                MapFogMaskCodec.MaxResolution);
        }

        private static int NormalizeTileSize(int tileSize)
        {
            return MapFogTileDeltaCodec.NormalizeTileSize(tileSize);
        }

        private void StopPushSnapshot()
        {
            if (pushSnapshotCoroutine == null)
                return;

            StopCoroutine(pushSnapshotCoroutine);
            pushSnapshotCoroutine = null;
        }

        private void ClearIncomingSnapshot()
        {
            incomingSnapshotId = 0;
            incomingSnapshotEncoding = 0;
            incomingSnapshotResolution = 0;
            incomingSnapshotUncompressedLength = 0;
            incomingSnapshotChecksum = 0;
            incomingSnapshotPayload = null;
            incomingSnapshotBytesReceived = 0;
        }

        private static bool IsFinite(Vector2 value)
        {
            return !float.IsNaN(value.x) &&
                   !float.IsInfinity(value.x) &&
                   !float.IsNaN(value.y) &&
                   !float.IsInfinity(value.y);
        }
    }
}
