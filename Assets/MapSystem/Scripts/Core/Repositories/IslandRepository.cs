using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.UI;

namespace Artifika.Map.Core
{
    public class IslandRepository : MonoBehaviour, ISaveable
    {
        [SerializeField] private List<IslandInfo> islands = new();

        private HashSet<string> discoveredIslands = new();
        private HashSet<string> revealedIslands   = new();

        public string SaveKey => "IslandDiscoveryData";

        private void Awake()
        {
            MapSystemContext.Instance.IslandRepository = this;
            SaveManager.Instance.Register(this);

            // Apply StartRevealed defaults for fresh sessions (overridden by LoadData if a save exists).
            foreach (var island in islands)
            {
                if (island.Definition != null && island.Definition.startAsRevealed)
                    revealedIslands.Add(island.Definition.IslandId);
            }
        }

        private void OnDestroy()
        {
            SaveManager.Instance.Unregister(this);
        }

        // ── Discovery ─────────────────────────────────────────────────────────

        public bool IsDiscovered(string islandId) => discoveredIslands.Contains(islandId);

        public void DiscoverIsland(string islandId)
        {
            if (!discoveredIslands.Add(islandId)) return;

            RefreshIslandUI(islandId);

            var info = islands.Find(i => i.Definition?.IslandId == islandId);
            EventManager.Instance.TriggerEvent(new IslandDiscoveredEvent(
                islandId,
                info?.Definition?.displayNameLocalizationKey,
                info?.Definition?.DisplayName ?? islandId
            ));
        }

        // ── Reveal ────────────────────────────────────────────────────────────

        public bool IsRevealed(string islandId) => revealedIslands.Contains(islandId);

        public void RevealIsland(string islandId)
        {
            if (!revealedIslands.Add(islandId)) return;

            RefreshIslandUI(islandId);

            var info = islands.Find(i => i.Definition?.IslandId == islandId);
            EventManager.Instance.TriggerEvent(new IslandRevealedEvent(
                islandId,
                info?.Definition?.displayNameLocalizationKey,
                info?.Definition?.DisplayName ?? islandId
            ));
        }
        
        private void RefreshIslandUI(string islandId)
        {
            var info = islands.Find(i => i.Definition != null && i.Definition.IslandId == islandId);
            if (info == null) return;

            bool discovered = discoveredIslands.Contains(islandId);
            bool revealed   = revealedIslands.Contains(islandId);
            info.RefreshIslandDisplay(discovered, revealed);
        }


        public object RetrieveSaveData()
        {
            return new IslandDiscoverySaveData
            {
                DiscoveredIslandIds = discoveredIslands.ToList(),
                RevealedIslandIds   = revealedIslands.ToList()
            };
        }

        public void LoadData(object state)
        {
            var data = (IslandDiscoverySaveData)state;
            discoveredIslands = new HashSet<string>(data.DiscoveredIslandIds);
            revealedIslands   = new HashSet<string>(data.RevealedIslandIds ?? new List<string>());

            foreach (var island in islands)
            {
                if (island.Definition != null && island.Definition.startAsRevealed)
                    revealedIslands.Add(island.Definition.IslandId);
            }

            foreach (var island in islands)
            {
                if (island.Definition == null) continue;
                bool discovered = discoveredIslands.Contains(island.Definition.IslandId);
                bool revealed   = revealedIslands.Contains(island.Definition.IslandId);
                island.RefreshIslandDisplay(discovered, revealed);
            }
        }
    }

    [System.Serializable]
    public class IslandInfo
    {
        public IslandDefinition Definition;

        [Space]
        [FormerlySerializedAs("DiscoveredSprite")]
        public Sprite RevealedSprite;

        [Space]
        public Image MapImage;
        public TMP_Text IslandName;

        public void RefreshIslandDisplay(bool discovered, bool revealed)
        {
            if (MapImage != null)
            {
                MapImage.sprite = RevealedSprite;
            }

            if (IslandName != null)
                IslandName.SetText(discovered ? Definition.DisplayName : "");
        }
    }

    [System.Serializable]
    public class IslandDiscoverySaveData
    {
        public List<string> DiscoveredIslandIds;
        public List<string> RevealedIslandIds;  
    }
}