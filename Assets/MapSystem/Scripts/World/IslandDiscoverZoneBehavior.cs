using Artifika.Map.Core;
using UnityEngine;
using Unity.Netcode;

[System.Serializable]
public class IslandDiscoverZoneBehavior : IZoneBehavior
{
    [SerializeField] private IslandDefinition islandDefinition;

    public void OnEnter(GameObject other)
    {
        if (!other.CompareTag("Player")) return;
        if (islandDefinition == null) return;

        var networkObject = other.GetComponent<NetworkObject>();
        if (networkObject != null && !networkObject.IsOwner) return;

        MapSystemContext.Instance.IslandRepository.DiscoverIsland(islandDefinition.IslandId);
    }
    public void OnExit(GameObject other) { }
}