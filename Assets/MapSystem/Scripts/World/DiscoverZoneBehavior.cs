using Artifika.Map.Core;
using Sirenix.OdinInspector;
using UnityEngine;
using Unity.Netcode;
using Sirenix.OdinInspector;

[System.Serializable]
public class DiscoverZoneBehavior : IZoneBehavior
{
    [SerializeField] private MapPOIDefinition poiDefinition;

    [SerializeField] private bool overrideMarkerId = false;
    [SerializeField, ShowIf("overrideMarkerId")] private string markerIdOverride;

    public void OnEnter(GameObject other)
    {
        if (!other.CompareTag("Player")) return;

        var networkObject = other.GetComponent<NetworkObject>();
        if (networkObject != null && !networkObject.IsOwner) return;

        string markerId = string.IsNullOrWhiteSpace(markerIdOverride)
            ? poiDefinition?.PoiId
            : markerIdOverride;

        if (string.IsNullOrWhiteSpace(markerId)) return;

        bool wasDiscovered = MapSystemContext.Instance.Discovery.TryDiscover(markerId);

        if (wasDiscovered)
            EventManager.Instance.TriggerEvent(new ZoneDiscoveredEvent(
                poiDefinition.PoiId,
                poiDefinition.DisplayNameLocalizationKey,
                poiDefinition.DisplayName
            ));
    }

    public void OnExit(GameObject other) { }
}