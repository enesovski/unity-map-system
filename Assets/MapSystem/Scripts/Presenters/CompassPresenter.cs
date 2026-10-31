using System;
using System.Collections.Generic;
using Artifika.Map.View;
using UnityEngine;

namespace Artifika.Map.Core
{
    public class CompassPresenter : MonoBehaviour
    {
        [SerializeField] private MapIconConfig iconConfig;

        private MapSystemContext mapSystemContext;

        private readonly List<CompassEntry> entries = new();
        private bool dirty = true;
        private bool bound;

        public event Action OnEntriesChanged;

        private void OnEnable()
        {
            if (MapSystemContext.TryGetInitialized(out var context))
            {
                Bind(context);
                return;
            }

            MapSystemContext.OnInitialized -= Bind;
            MapSystemContext.OnInitialized += Bind;
        }

        private void OnDisable()
        {
            MapSystemContext.OnInitialized -= Bind;
            MapSystemContext.OnShutdown -= OnMapShutdown;
            Unbind();
        }

        private void Bind(MapSystemContext context)
        {
            if (bound)
                return;

            mapSystemContext = context;
            bound = true;

            MapSystemContext.OnInitialized -= Bind;
            MapSystemContext.OnShutdown -= OnMapShutdown;
            MapSystemContext.OnShutdown += OnMapShutdown;

            mapSystemContext.StaticMarkers.OnStaticMarkerStateChanged += OnStaticMarkerStateChanged;
            mapSystemContext.DynamicMarkers.OnMarkerAdded += OnMarkerChanged;
            mapSystemContext.DynamicMarkers.OnMarkerRemoved += OnMarkerRemoved;
            mapSystemContext.DynamicMarkers.OnMarkerUpdated += OnMarkerChanged;
            dirty = true;
            OnEntriesChanged?.Invoke();
        }

        private void Unbind()
        {
            if (mapSystemContext != null)
            {
                mapSystemContext.StaticMarkers.OnStaticMarkerStateChanged -= OnStaticMarkerStateChanged;
                mapSystemContext.DynamicMarkers.OnMarkerAdded -= OnMarkerChanged;
                mapSystemContext.DynamicMarkers.OnMarkerRemoved -= OnMarkerRemoved;
                mapSystemContext.DynamicMarkers.OnMarkerUpdated -= OnMarkerChanged;
            }

            entries.Clear();
            mapSystemContext = null;
            bound = false;
            dirty = true;
            OnEntriesChanged?.Invoke();
        }

        private void OnMapShutdown()
        {
            Unbind();
        }

        public IReadOnlyList<CompassEntry> GetEntries()
        {
            if (mapSystemContext == null)
                return entries;

            if (dirty) RebuildEntries();

            var player = mapSystemContext.EntityTracking.GetLocalPlayer();
            if (player == null) return entries;

            foreach (var entry in entries)
            {
                var targetPos = ResolveWorldPosition(entry.MarkerId);
                entry.WorldPosition = targetPos;
                entry.WorldAngle = CalculateAngle(player.WorldPosition, player.FacingAngle, targetPos);
            }

            return entries;
        }

        private void RebuildEntries()
        {
            entries.Clear();

            if (mapSystemContext == null)
                return;

            foreach (var marker in mapSystemContext.StaticMarkers.GetAll())
            {
                if (marker is not POIMarker) continue;
                if (marker.State != MarkerState.Visible) continue;

                entries.Add(new CompassEntry(
                    marker.MarkerId,
                    marker.DisplayName,
                    iconConfig.GetIcon(marker),
                    MarkerType.POI)
                {
                    IgnoreDistanceFade = false
                });
            }

            foreach (var marker in mapSystemContext.DynamicMarkers.GetAll())
            {
                switch (marker.MarkerType)
                {
                    case MarkerType.QuestObjective:
                        if (marker.State != MarkerState.Active) continue;
                        entries.Add(new CompassEntry(
                            marker.MarkerId,
                            marker.DisplayName ?? string.Empty,
                            iconConfig.GetIcon(marker),
                            MarkerType.QuestObjective)
                        {
                            IgnoreDistanceFade = true
                        });
                        break;

                    case MarkerType.Pin:
                        if (marker.State != MarkerState.Visible) continue;
                        entries.Add(new CompassEntry(
                            marker.MarkerId,
                            marker.DisplayName ?? string.Empty,
                            iconConfig.GetIcon(marker),
                            MarkerType.Pin)
                        {
                            IgnoreDistanceFade = false
                        });
                        break;

                    case MarkerType.DeathMarker:
                        if (marker.State != MarkerState.Visible && marker.State != MarkerState.Active) continue;
                        entries.Add(new CompassEntry(
                            marker.MarkerId,
                            marker.DisplayName ?? string.Empty,
                            iconConfig.GetIcon(marker),
                            MarkerType.DeathMarker)
                        {
                            IgnoreDistanceFade = true
                        });
                        break;

                    case MarkerType.Teammate:
                        if (marker.State != MarkerState.Visible) continue;
                        entries.Add(new CompassEntry(
                            marker.MarkerId,
                            marker.DisplayName ?? string.Empty,
                            iconConfig.GetIcon(marker),
                            MarkerType.Teammate)
                        {
                            IgnoreDistanceFade = true
                        });
                        break;
                }
            }

            dirty = false;
            OnEntriesChanged?.Invoke();
        }

        private void OnStaticMarkerStateChanged(StaticMarker _) => dirty = true;
        private void OnMarkerChanged(MapMarkerBase _) => dirty = true;
        private void OnMarkerRemoved(string _) => dirty = true;

        private Vector3 ResolveWorldPosition(string markerId)
        {
            var marker = mapSystemContext.MarkerQuery.GetById(markerId);
            return marker?.WorldPosition ?? Vector3.zero;
        }

        private static float CalculateAngle(Vector3 playerPos, float playerFacingAngle, Vector3 targetPos)
        {
            var dir = targetPos - playerPos;
            dir.y = 0f;
            if (dir.sqrMagnitude < 0.0001f) return 0f;

            float targetAngle = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;
            return Mathf.DeltaAngle(playerFacingAngle, targetAngle);
        }
    }
}
