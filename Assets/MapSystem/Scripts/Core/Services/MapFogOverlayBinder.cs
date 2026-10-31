using UnityEngine;
using UnityEngine.UI;

namespace Artifika.Map.Core
{
    [RequireComponent(typeof(Graphic))]
    public sealed class MapFogOverlayBinder : MonoBehaviour
    {
        private Graphic graphic;

        private void Awake()
        {
            graphic = GetComponent<Graphic>();
            graphic.raycastTarget = false;
        }

        private void OnEnable()
        {
            MapFogTextureService.InstanceChanged += OnServiceChanged;
            MapFogTextureService.Instance?.BindOverlay(graphic);
        }

        private void OnDisable()
        {
            MapFogTextureService.InstanceChanged -= OnServiceChanged;
            MapFogTextureService.Instance?.UnbindOverlay(graphic);
        }

        private void OnServiceChanged(MapFogTextureService service)
        {
            service?.BindOverlay(graphic);
        }
    }
}
