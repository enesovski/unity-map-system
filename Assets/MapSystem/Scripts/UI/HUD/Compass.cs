using System.Collections.Generic;
using Artifika.Map.Core;
using Artifika.Map.View;
using Sirenix.OdinInspector;
using UnityEngine;
using UnityEngine.UI;

namespace Artifika.Map.UI
{
    public class Compass : MonoBehaviour
    {
        [Title("Compass UI")]
        [SerializeField] private RectTransform compassBar;
        [SerializeField] private CompassPresenter compassPresenter;

        [Range(30f, 360f)]
        [SerializeField] private float visibleAngle = 180f;

        [Title("Icon Settings")]
        [SerializeField] private GameObject markerPrefab;
        [SerializeField] private bool hideOffscreen = true;
        [SerializeField] private Vector2 distanceFadeRange = new Vector2(5f, 250f);
        [SerializeField] private float iconYOffset = 0f;

        private MapSystemContext mapSystemContext;

        private readonly Dictionary<string, Image> markerImages = new();
        
        private void Start()
        {
            if (compassBar == null || markerPrefab == null)
            {
                enabled = false;
                return;
            }

            compassBar.gameObject.SetActive(true);
        }

        private void OnEnable()
        {
            if (MapSystemContext.TryGetInitialized(out var context))
            {
                BindMapContext(context);
                return;
            }

            MapSystemContext.OnInitialized -= BindMapContext;
            MapSystemContext.OnInitialized += BindMapContext;
        }

        private void OnDisable()
        {
            MapSystemContext.OnInitialized -= BindMapContext;
            MapSystemContext.OnShutdown -= OnMapShutdown;
            mapSystemContext = null;
            ClearMarkerImages();
        }

        private void BindMapContext(MapSystemContext context)
        {
            mapSystemContext = context;
            MapSystemContext.OnInitialized -= BindMapContext;
            MapSystemContext.OnShutdown -= OnMapShutdown;
            MapSystemContext.OnShutdown += OnMapShutdown;
        }

        private void OnMapShutdown()
        {
            mapSystemContext = null;
            ClearMarkerImages();
        }

        private void Update() => UpdateCompass();

        private void UpdateCompass()
        {
            if (compassBar == null || compassPresenter == null || mapSystemContext == null) return;

            var entries = compassPresenter.GetEntries();
            if (entries == null || entries.Count == 0) return;

            var player = mapSystemContext.EntityTracking.GetLocalPlayer();
            if (player == null) return;

            float barWidth = compassBar.rect.width;
            float halfVisible = Mathf.Max(1f, visibleAngle * 0.5f);

            foreach (var entry in entries)
            {
                if (!markerImages.TryGetValue(entry.MarkerId, out var image) || image == null)
                    image = CreateImageForEntry(entry);

                image.sprite = entry.Icon;

                float signedAngle = entry.WorldAngle;
                float normalized = Mathf.Clamp(signedAngle / halfVisible, -1f, 1f);
                float x = (barWidth * 0.5f) * normalized;
                bool outOfRange = Mathf.Abs(signedAngle) > halfVisible;

                image.rectTransform.anchoredPosition = new Vector2(x, iconYOffset);

                float alphaByDistance = 1f;
                if (!entry.IgnoreDistanceFade && distanceFadeRange.x < distanceFadeRange.y)
                {
                    float dist = HorizontalDistance(player.WorldPosition, entry.WorldPosition);
                    alphaByDistance = 1f - Mathf.InverseLerp(distanceFadeRange.x, distanceFadeRange.y, dist);
                }

                float edgeFactor = 1f - Mathf.Clamp01(
                    (Mathf.Abs(signedAngle) - halfVisible * 0.8f) / (halfVisible * 0.2f));

                float targetAlpha = alphaByDistance * Mathf.Clamp01(edgeFactor);
                if (hideOffscreen && outOfRange) targetAlpha = 0f;

                SetImageAlpha(image, targetAlpha);
                image.gameObject.SetActive(targetAlpha > 0.001f);
            }

            CleanStaleImages(entries);
        }

        private Image CreateImageForEntry(CompassEntry entry)
        {
            var go = Instantiate(markerPrefab, compassBar);
            var image = go.GetComponent<Image>() ?? go.AddComponent<Image>();

            image.name = $"CompassIcon_{entry.Label}";
            image.sprite = entry.Icon;
            image.preserveAspect = true;

            var rt = image.rectTransform;
            rt.anchorMin = new Vector2(0.5f, rt.anchorMin.y);
            rt.anchorMax = new Vector2(0.5f, rt.anchorMax.y);
            rt.pivot = new Vector2(0.5f, rt.pivot.y);

            markerImages[entry.MarkerId] = image;
            return image;
        }

        private void CleanStaleImages(IReadOnlyList<CompassEntry> current)
        {
            var activeIds = new HashSet<string>(current.Count);
            foreach (var entry in current)
                activeIds.Add(entry.MarkerId);

            var toRemove = new List<string>();
            foreach (var kvp in markerImages)
                if (!activeIds.Contains(kvp.Key)) toRemove.Add(kvp.Key);

            foreach (var id in toRemove)
            {
                if (markerImages[id])
                    Destroy(markerImages[id].gameObject);
                markerImages.Remove(id);
            }
        }

        private void ClearMarkerImages()
        {
            foreach (var image in markerImages.Values)
            {
                if (image != null)
                    Destroy(image.gameObject);
            }

            markerImages.Clear();
        }

        private static void SetImageAlpha(Image img, float alpha)
        {
            var color = img.color;
            color.a = Mathf.Clamp01(alpha);
            img.color = color;
        }

        private static float HorizontalDistance(Vector3 a, Vector3 b)
        {
            a.y = 0f;
            b.y = 0f;
            return Vector3.Distance(a, b);
        }
    }
}
