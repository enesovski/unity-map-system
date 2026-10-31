using Artifika.Map.Core;
using UnityEngine;

namespace Artifika.Map
{
    public interface IMarkerView
    {
        string MarkerId { get; }
        void Bind(MapMarkerBase marker, Sprite icon);
        void SetAnchoredPosition(Vector2 position);
        void SetSelected(bool selected);
        void SetVisible(bool visible);
    }

    public interface IRotatableMarkerView
    {
        void SetRotation(float angleDegrees);
    }
}