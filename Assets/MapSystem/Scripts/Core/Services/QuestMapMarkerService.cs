using System;
using System.Collections.Generic;
using Artifika.Map.Core;
using Artifika.Quests;
using Unity.Collections;
using UnityEngine;

namespace Artifika.Map
{

    public class QuestMapMarkerService : MonoBehaviour
    {
        private ObjectiveTracker tracker;

        private readonly HashSet<string> activePointMarkers = new();
        private readonly HashSet<string> activeAreaMarkers = new();


        public void Initialize(ObjectiveTracker objectiveTracker)
        {
            if (tracker != null) Unsubscribe();

            tracker = objectiveTracker;
            Subscribe();

            if (MapSystemContext.TryGetInitialized(out _))
                Hydrate();
            else
                MapSystemContext.OnInitialized += OnMapInitialized;
        }

        private void OnDestroy()
        {
            MapSystemContext.OnInitialized -= OnMapInitialized;
            Unsubscribe();
        }

        private void OnMapInitialized(MapSystemContext _)
        {
            MapSystemContext.OnInitialized -= OnMapInitialized;
            Hydrate();
        }


        private void Subscribe()
        {
            if (tracker == null) return;
            tracker.QuestStarted += OnQuestStarted;
            tracker.ObjectiveCompleted += OnObjectiveCompleted;
            tracker.QuestCompleted += OnQuestCompleted;
        }

        private void Unsubscribe()
        {
            MapSystemContext.OnInitialized -= OnMapInitialized;

            if (tracker == null) return;
            tracker.QuestStarted -= OnQuestStarted;
            tracker.ObjectiveCompleted -= OnObjectiveCompleted;
            tracker.QuestCompleted -= OnQuestCompleted;
            tracker = null;
        }

        private void Hydrate()
        {
            if (tracker == null) return;

            foreach (var questProgress in tracker.ActiveQuests)
            {
                if (questProgress.completed) continue;

                var questSO = QuestChainRegistry.GetQuest(questProgress.guid.ToString());
                if (questSO == null) continue;

                RegisterMarkersForQuest(questSO, questProgress);
            }
        }

        // ── Event handlers ────────────────────────────────────────────────────

        private void OnQuestStarted(FixedString64Bytes questGuid)
        {
            var questSO = QuestChainRegistry.GetQuest(questGuid.ToString());
            if (questSO == null) return;

            // ActiveQuests is a NetworkList synced before RPCs fire.
            // Reading it here gives us the correct completion state for each objective,
            // avoiding a race with ObjectiveCompleted RPCs that fire before QuestStarted.
            var questProgress = FindQuestProgress(questGuid);
            if (questProgress == null) return;

            RegisterMarkersForQuest(questSO, questProgress.Value);
        }

        private void OnObjectiveCompleted(FixedString64Bytes questGuid, FixedString64Bytes objectiveGuid)
        {
            UnregisterMarker(questGuid.ToString(), objectiveGuid.ToString());
        }

        private void OnQuestCompleted(FixedString64Bytes questGuid)
        {
            var questSO = QuestChainRegistry.GetQuest(questGuid.ToString());
            if (questSO == null) return;

            // Clean up any remaining markers (e.g. optional objectives never completed)
            foreach (var objSO in questSO.objectives)
                UnregisterMarker(questGuid.ToString(), objSO.name);
        }

        // ── Core logic ────────────────────────────────────────────────────────

        private void RegisterMarkersForQuest(QuestSO questSO, QuestProgress questProgress)
        {
            foreach (var objSO in questSO.objectives)
            {
                if (!objSO.HasMapMarker) continue;
                if (IsObjectiveCompleted(questProgress, objSO.name)) continue;

                RegisterMarker(questProgress.guid.ToString(), objSO, LocalizationService.Get("Quests", questSO.titleLocalizationKey, questSO.title));
            }
        }

        private void RegisterMarker(string questGuid, ObjectiveSO objSO, string questTitle)
        {
            if (!MapSystemContext.TryGetInitialized(out var ctx)) return;

            string pointKey = MarkerKey(questGuid, objSO.name);
            string areaKey  = MarkerKey(questGuid, objSO.name) + "_area";
            string description = LocalizationService.Get("Quests", objSO.descriptionLocalizationKey, objSO.description);

            // Always register the point marker so the quest icon appears on the map and compass.
            if (activePointMarkers.Add(pointKey))
            {
                var pointMarker = new ObjectiveMarker(pointKey, questTitle, description)
                {
                    WorldPosition = objSO.MarkerPosition
                };
                ctx.DynamicMarkers.Register(pointMarker);
            }

            // Additionally register an area overlay when the objective has a radius zone.
            if (objSO.IsAreaMarker && activeAreaMarkers.Add(areaKey))
            {
                var areaMarker = new AreaMarker(areaKey, MarkerType.QuestArea, objSO.AreaRadius)
                {
                    WorldPosition = objSO.MarkerPosition,
                    DisplayName   = questTitle,
                    Description   = description,
                    State         = MarkerState.Visible
                };
                ctx.StaticMarkers.Register(areaMarker);
            }
        }

        private void UnregisterMarker(string questGuid, string objectiveGuid)
        {
            string pointKey = MarkerKey(questGuid, objectiveGuid);
            string areaKey  = MarkerKey(questGuid, objectiveGuid) + "_area";
            if (!MapSystemContext.TryGetInitialized(out var ctx)) return;

            if (activePointMarkers.Remove(pointKey))
                ctx.DynamicMarkers.Unregister(pointKey);

            if (activeAreaMarkers.Remove(areaKey))
            {
                ctx.StaticMarkers.SetState(areaKey, MarkerState.Hidden);
                ctx.StaticMarkers.Unregister(areaKey);
            }
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private QuestProgress? FindQuestProgress(FixedString64Bytes questGuid)
        {
            foreach (var qp in tracker.ActiveQuests)
                if (qp.guid.Equals(questGuid))
                    return qp;
            return null;
        }

        private static bool IsObjectiveCompleted(QuestProgress quest, string objectiveAssetName)
        {
            for (int i = 0; i < quest.objectives.Length; i++)
                if (quest.objectives[i].guid.Equals(objectiveAssetName))
                    return quest.objectives[i].completed;
            return false;
        }

        private static string MarkerKey(string questGuid, string objectiveGuid)
            => $"{questGuid}_{objectiveGuid}";
    }
}
