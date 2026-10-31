using UnityEngine;

namespace Artifika.Map.Core
{
    public static class MapCoordinateCalculator
    {
        public static Vector2 WorldToMapUV(Vector3 worldPos, Rect worldBounds)
        {
            return new Vector2(
                Mathf.InverseLerp(worldBounds.xMin, worldBounds.xMax, worldPos.x),
                Mathf.InverseLerp(worldBounds.yMin, worldBounds.yMax, worldPos.z)
            );
        }

        public static Vector3 MapUVToWorld(Vector2 uv, Rect worldBounds)
        {
            return new Vector3(
                Mathf.Lerp(worldBounds.xMin, worldBounds.xMax, uv.x),
                0f,
                Mathf.Lerp(worldBounds.yMin, worldBounds.yMax, uv.y)
            );
        }

        public static Vector3 MapUVToUIWorldPoint(Vector2 uv, RectTransform rect)
        {
            var corners = new Vector3[4];
            rect.GetWorldCorners(corners);

            return new Vector3(
                Mathf.Lerp(corners[0].x, corners[2].x, uv.x),
                Mathf.Lerp(corners[0].y, corners[2].y, uv.y),
                0f
            );
        }

        public static Vector2 MapUVToLocalPoint(Vector2 uv, RectTransform rect)
        {
            var size = rect.rect.size;
            var pivot = rect.pivot;
            return new Vector2(
                (uv.x - pivot.x) * size.x,
                (uv.y - pivot.y) * size.y
            );
        }
    }
}