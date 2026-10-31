using Sirenix.OdinInspector;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Artifika.Map.Core
{
    public class MapSystemContext : MonoBehaviour
    {
        [Title("References")]
        [SerializeField] private MapWorldDefinition worldDefinition;

        [Title("Projection")]
        [Tooltip("Describes how the world maps onto the map surface. Optional during migration — " +
                 "when empty, a legacy projection is built from WorldDefinition.WorldBoundsRect, " +
                 "which reproduces the pre-refactor behaviour exactly.")]
        [SerializeField] private MapProjection projection;

        [Tooltip("Map surface size used only when no MapProjection is assigned. Must match islandsRect's " +
                 "size or map-local conversions will be wrong. Ignored once a projection asset is set.")]
        [SerializeField] private Vector2 legacyMapSurfaceSize = new(1080f, 1080f);

        public IStaticMarkerRepository StaticMarkers { get; private set; }
        public IDynamicMarkerRepository DynamicMarkers { get; private set; }
        public IMapMarkerQuery MarkerQuery { get; private set; }
        public IDiscoveryService Discovery { get; private set; }
        public IEntityTrackingService EntityTracking { get; private set; }
        public IMarkerFactory MarkerFactory { get; private set; }
        public MapFogTextureService FogTexture { get; private set; }
        public MapFogDiscoveryRegistry FogDiscoveryRegistry { get; private set; }

        /// <summary>
        /// The one place world coordinates become map coordinates. Every presenter, the click
        /// handler, the camera and the fog sampler go through this, which is what keeps them
        /// in agreement.
        /// </summary>
        public IMapProjector Projector { get; private set; }

        public MapWorldDefinition WorldDefinition => worldDefinition;
        public IslandRepository IslandRepository;
        public static MapSystemContext Instance { get; private set; }

        public static event Action<MapSystemContext> OnInitialized;
        public static event Action OnShutdown;

        public bool IsInitialized { get; private set; }

        private void Awake()
        {
            if (Instance != null && Instance != this && Instance.IsInitialized)
            {
                enabled = false;
                return;
            }

            Instance = this;
        }

        public void Initialize()
        {
            if (IsInitialized)
            {
                if (Instance != this)
                    Instance = this;

                OnInitialized?.Invoke(this);
                return;
            }

            if (Instance != null && Instance != this && Instance.IsInitialized)
            {
                GameLog.Warning($"[MapSystemContext] Another initialized context already exists on '{Instance.name}'.");
                enabled = false;
                return;
            }

            Instance = this;

            StaticMarkers = new StaticMarkerRepository();
            DynamicMarkers = new DynamicMarkerRepository();

            MarkerQuery = new MapMarkerQuery(DynamicMarkers, StaticMarkers);
            Discovery = new DiscoveryService(StaticMarkers);
            EntityTracking = new EntityTrackingService(DynamicMarkers);
            MarkerFactory = new MarkerFactory(DynamicMarkers);
            FogDiscoveryRegistry = new MapFogDiscoveryRegistry();
            Projector = new MapProjector(ResolveProjection());

            FogTexture = GetComponent<MapFogTextureService>();
            if (FogTexture == null)
                FogTexture = gameObject.AddComponent<MapFogTextureService>();

            FogTexture.Initialize(worldDefinition);

            IsInitialized = true;
            OnInitialized?.Invoke(this);
        }

        /// <summary>
        /// Uses the assigned projection when there is one. Otherwise builds a legacy projection
        /// from the world definition, so the system behaves exactly as it did before the
        /// projector existed and needs no scene setup to keep working.
        /// </summary>
        private MapProjection ResolveProjection()
        {
            if (projection != null)
                return projection;

            Rect bounds = worldDefinition != null ? worldDefinition.WorldBoundsRect : default;
            return MapProjection.CreateLegacy(bounds, legacyMapSurfaceSize);
        }

        public void Shutdown()
        {
            if (!IsInitialized)
                return;

            OnShutdown?.Invoke();

            IsInitialized = false;
            StaticMarkers = null;
            DynamicMarkers = null;
            MarkerQuery = null;
            Discovery = null;
            EntityTracking = null;
            MarkerFactory = null;
            FogDiscoveryRegistry = null;
            Projector = null;
        }

        private void OnDestroy()
        {
            Shutdown();

            if (Instance == this)
                Instance = null;
        }

        public static bool TryGetInitialized(out MapSystemContext context)
        {
            context = Instance;
            return context != null && context.IsInitialized;
        }
    }

    public sealed class MapFogDiscoveryRegistry
    {
        private readonly Dictionary<MapFogScopeKey, MapFogScopeState> states = new();

        public MapFogScopeState GetOrCreate(MapFogScopeKey scopeKey, int resolution)
        {
            resolution = NormalizeResolution(resolution);

            if (!states.TryGetValue(scopeKey, out MapFogScopeState state))
            {
                state = new MapFogScopeState(scopeKey, resolution);
                states.Add(scopeKey, state);
                return state;
            }

            state.EnsureResolution(resolution);
            return state;
        }

        public void RegisterSubscriber(MapFogScopeKey scopeKey, int resolution, ulong clientId)
        {
            GetOrCreate(scopeKey, resolution).RegisterSubscriber(clientId);
        }

        public void UnregisterSubscriber(MapFogScopeKey scopeKey, ulong clientId)
        {
            if (states.TryGetValue(scopeKey, out MapFogScopeState state))
                state.UnregisterSubscriber(clientId);
        }

        public bool Reveal(
            MapFogScopeKey scopeKey,
            int resolution,
            Vector2 uv,
            float radiusWorld,
            Rect worldBounds,
            int tileSize)
        {
            MapFogScopeState state = GetOrCreate(scopeKey, resolution);
            return state.Reveal(uv, radiusWorld, worldBounds, tileSize);
        }

        public void SetMask(MapFogScopeKey scopeKey, int resolution, byte[] mask)
        {
            GetOrCreate(scopeKey, resolution).SetMask(resolution, mask);
        }

        private static int NormalizeResolution(int resolution)
        {
            return Mathf.Clamp(
                resolution,
                MapFogMaskCodec.MinResolution,
                MapFogMaskCodec.MaxResolution);
        }
    }

    public sealed class MapFogScopeState
    {
        private readonly Dictionary<ulong, HashSet<int>> subscriberDirtyTiles = new();
        private MapFogMaskSnapshot snapshot;
        private bool snapshotDirty = true;

        public MapFogScopeState(MapFogScopeKey scopeKey, int resolution)
        {
            ScopeKey = scopeKey;
            Resolution = resolution;
            Mask = new byte[resolution * resolution];
        }

        public MapFogScopeKey ScopeKey { get; }
        public int Resolution { get; private set; }
        public byte[] Mask { get; private set; }
        public uint Revision { get; private set; }

        public void EnsureResolution(int resolution)
        {
            if (Mask != null && Resolution == resolution && Mask.Length == resolution * resolution)
                return;

            Resolution = resolution;
            Mask = new byte[resolution * resolution];
            Revision++;
            snapshotDirty = true;
        }

        public void RegisterSubscriber(ulong clientId)
        {
            if (!subscriberDirtyTiles.ContainsKey(clientId))
                subscriberDirtyTiles.Add(clientId, new HashSet<int>());
        }

        public void UnregisterSubscriber(ulong clientId)
        {
            subscriberDirtyTiles.Remove(clientId);
        }

        public bool Reveal(Vector2 uv, float radiusWorld, Rect worldBounds, int tileSize)
        {
            var changedTiles = new HashSet<int>();
            bool changed = MapFogMaskRasterizer.RevealCircle(
                Mask,
                Resolution,
                uv,
                radiusWorld,
                worldBounds,
                changedTiles,
                tileSize);

            if (!changed)
                return false;

            Revision++;
            snapshotDirty = true;
            MarkTilesDirty(changedTiles);
            return true;
        }

        public void SetMask(int resolution, byte[] mask)
        {
            if (mask == null || mask.Length != resolution * resolution)
                return;

            Resolution = resolution;
            Mask = new byte[mask.Length];
            Buffer.BlockCopy(mask, 0, Mask, 0, mask.Length);
            Revision++;
            snapshotDirty = true;
        }

        public MapFogMaskSnapshot GetSnapshot()
        {
            if (snapshotDirty || snapshot.Payload == null)
            {
                snapshot = MapFogMaskCodec.Encode(Mask);
                snapshotDirty = false;
            }

            return snapshot;
        }

        public bool TryCollectDirtyTiles(ulong clientId, int maxTiles, List<int> output)
        {
            output?.Clear();

            if (output == null ||
                maxTiles <= 0 ||
                !subscriberDirtyTiles.TryGetValue(clientId, out HashSet<int> dirtyTiles) ||
                dirtyTiles.Count == 0)
            {
                return false;
            }

            foreach (int tileIndex in dirtyTiles)
            {
                output.Add(tileIndex);
                if (output.Count >= maxTiles)
                    break;
            }

            for (int i = 0; i < output.Count; i++)
                dirtyTiles.Remove(output[i]);

            return output.Count > 0;
        }

        private void MarkTilesDirty(HashSet<int> tileIndices)
        {
            if (tileIndices == null || tileIndices.Count == 0)
                return;

            foreach (HashSet<int> subscriberTiles in subscriberDirtyTiles.Values)
            {
                foreach (int tileIndex in tileIndices)
                    subscriberTiles.Add(tileIndex);
            }
        }
    }
}
