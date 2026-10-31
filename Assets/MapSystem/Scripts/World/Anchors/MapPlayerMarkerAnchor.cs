using Artifika.Map.Core;
using Artifika.Map.World;
using Artifika.Player;
using Sirenix.OdinInspector;
using Steamworks;
using Unity.Netcode;
using UnityEngine;

namespace Artifika.Map
{
    [DisallowMultipleComponent]
    public class MapPlayerMarkerAnchor : NetworkBehaviour, IMapMarkerAnchor
    {
        [Title("Display Names")]
        private string localDisplayName = "You";
        private string localDisplayNameLocalizationKey = "POI_MAP_YOU";
        [SerializeField] private string remoteDisplayNamePrefix = "Player";
        [SerializeField] private string remoteDisplayNamePrefixLocalizationKey = "POI_MAP_PLAYER_PREFIX";

        [Title("Facing Direction")]
        [SerializeField] private Transform facingReference;

        [Title("Map Update Throttling")]
        [SerializeField, Min(0.02f)] private float mapUpdateInterval = 0.1f;
        [SerializeField, Min(0f)] private float minPositionDelta = 0.5f;
        [SerializeField, Min(0f)] private float minFacingDelta = 2f;

        public MarkerType Type => IsOwner ? MarkerType.Player : MarkerType.Teammate;

        private static readonly bool[] occupiedPlayerSlots = new bool[4];

        private readonly NetworkVariable<int> playerSlot = new(
            0,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        private MapSystemContext mapSystemContext;
        private IEntityTrackingService entityTracking;
        private string markerId;
        private bool resolvedRemoteDisplayName;
        private Vector3 lastPublishedPosition;
        private float lastPublishedFacingAngle;
        private float nextMapUpdateTime;
        private bool hasPublishedTransform;
        private bool isNetworkSpawned;
        private bool registered;

        public override void OnNetworkSpawn()
        {
            isNetworkSpawned = true;

            if (IsServer)
                playerSlot.Value = AcquirePlayerSlot();

            playerSlot.OnValueChanged += OnPlayerSlotChanged;
            TryRegisterWhenReady();
        }

        public override void OnNetworkDespawn()
        {
            isNetworkSpawned = false;
            MapSystemContext.OnInitialized -= OnMapInitialized;
            MapSystemContext.OnShutdown -= OnMapShutdown;
            playerSlot.OnValueChanged -= OnPlayerSlotChanged;

            if (IsServer)
                ReleasePlayerSlot(playerSlot.Value);

            Unregister();
        }

        private void OnDestroy()
        {
            MapSystemContext.OnInitialized -= OnMapInitialized;
            MapSystemContext.OnShutdown -= OnMapShutdown;

            if (registered)
                Unregister();
        }

        private void TryRegisterWhenReady()
        {
            if (!isNetworkSpawned || registered)
                return;

            if (MapSystemContext.TryGetInitialized(out var context))
            {
                BindAndRegister(context);
                return;
            }

            MapSystemContext.OnInitialized -= OnMapInitialized;
            MapSystemContext.OnInitialized += OnMapInitialized;
        }

        private void OnMapInitialized(MapSystemContext context)
        {
            BindAndRegister(context);
        }

        private void OnMapShutdown()
        {
            Unregister();
            mapSystemContext = null;
            entityTracking = null;
        }

        private void BindAndRegister(MapSystemContext context)
        {
            if (!isNetworkSpawned || registered)
                return;

            mapSystemContext = context;
            entityTracking = context.EntityTracking;

            MapSystemContext.OnInitialized -= OnMapInitialized;
            MapSystemContext.OnShutdown -= OnMapShutdown;
            MapSystemContext.OnShutdown += OnMapShutdown;

            Register();
        }

        public void Register()
        {
            if (registered || entityTracking == null)
                return;

            markerId = NetworkObjectId.ToString();

            string resolvedLocal = LocalizationService.Get("POIs", localDisplayNameLocalizationKey, localDisplayName);
            string resolvedRemote = $"{LocalizationService.Get("POIs", remoteDisplayNamePrefixLocalizationKey, remoteDisplayNamePrefix)} {OwnerClientId}";
            string displayName = IsOwner ? resolvedLocal : resolvedRemote;

            entityTracking.RegisterEntity(markerId, displayName, EntityType.None, Type, playerSlot.Value);
            registered = true;
            PublishTransform(force: true);

            var marker = mapSystemContext?.DynamicMarkers.GetById(markerId) as EntityMarker;
            if (marker != null)
            {
                marker.DisplayNameLocalizationKey = IsOwner
                    ? localDisplayNameLocalizationKey
                    : remoteDisplayNamePrefixLocalizationKey;
            }

            TryResolveRemoteDisplayName();
        }

        public void Unregister()
        {
            if (!registered || string.IsNullOrEmpty(markerId))
                return;

            entityTracking?.RemoveEntity(markerId);
            registered = false;
            markerId = null;
            hasPublishedTransform = false;
        }

        private void LateUpdate()
        {
            if (!registered || entityTracking == null)
                return;

            TryResolveRemoteDisplayName();
            PublishTransform(force: IsOwner);
        }

        private void TryResolveRemoteDisplayName()
        {
            if (IsOwner || resolvedRemoteDisplayName || !SteamClient.IsValid)
                return;

            var rootNetwork = GetComponentInParent<PlayerRootNetwork>();
            if (rootNetwork == null ||
                !ulong.TryParse(rootNetwork.PersistentUserId, out ulong steamId))
            {
                return;
            }

            var id = new SteamId { Value = steamId };
            SteamFriends.RequestUserInformation(id, false);

            string steamName = new Friend(id).Name;
            if (string.IsNullOrWhiteSpace(steamName))
                return;

            var marker = mapSystemContext?.DynamicMarkers.GetById(markerId) as EntityMarker;
            if (marker == null)
                return;

            marker.DisplayName = steamName;
            marker.DisplayNameLocalizationKey = null;
            resolvedRemoteDisplayName = true;
            mapSystemContext.DynamicMarkers.NotifyUpdated(markerId);
        }

        private void OnPlayerSlotChanged(int _, int current)
        {
            if (string.IsNullOrEmpty(markerId))
                return;

            var markers = mapSystemContext?.DynamicMarkers;
            var marker = markers?.GetById(markerId) as EntityMarker;
            if (marker == null)
                return;

            marker.PlayerSlot = current;
            markers.NotifyUpdated(markerId);
        }

        private static int AcquirePlayerSlot()
        {
            for (int i = 0; i < occupiedPlayerSlots.Length; i++)
            {
                if (occupiedPlayerSlots[i])
                    continue;

                occupiedPlayerSlots[i] = true;
                return i + 1;
            }

            GameLog.Warning("[MapPlayerMarkerAnchor] No free player icon slot is available.");
            return 0;
        }

        private static void ReleasePlayerSlot(int slot)
        {
            int index = slot - 1;
            if (index >= 0 && index < occupiedPlayerSlots.Length)
                occupiedPlayerSlots[index] = false;
        }

        private float GetFacingAngle() => (facingReference ? facingReference : transform).eulerAngles.y;

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
