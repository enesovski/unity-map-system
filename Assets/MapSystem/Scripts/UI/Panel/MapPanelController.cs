using DG.Tweening;
using Sirenix.OdinInspector;
using UnityEngine;
using UnityEngine.Serialization;

namespace Artifika.Map.UI
{
    [RequireComponent(typeof(CanvasGroup))]
    public class MapPanelController : MonoBehaviour
    {
        [Title("References")] 
        [SerializeField] private MapPinLabelInput mapPinLabelInput;
        [SerializeField] private MapViewController mapViewController; 

        [Title("Fade")]
        [SerializeField] private float fadeDuration = 0.2f;
        
        [Title("Audio")]
        [SerializeField] private AudioSource audioSource;
        [SerializeField] private AudioClip onMapOpen;
        [SerializeField] private AudioClip onMapClose;

        [Title("Initial State"), FormerlySerializedAs("defaultZoom")]
        public float DefaultZoom = 3f;
        
        public bool IsOpen { get; private set; }

        private CanvasGroup canvasGroup;

        private void Awake()
        {
            canvasGroup = GetComponent<CanvasGroup>();
            SetPanelActive(false, instant: true);
        }
        

        public void Open()
        {
            if (IsOpen) return;
            IsOpen = true;

            mapPinLabelInput.Hide();
            SetPanelActive(true, instant: false);
            audioSource.PlayOneShot(onMapOpen);
            mapViewController.SetZoomImmediate(DefaultZoom);
            mapViewController.CenterOnPlayerImmediately(); 
        }
        
        public void Close()
        {
            if (!IsOpen) return;
            IsOpen = false;

            audioSource.PlayOneShot(onMapClose);
            mapPinLabelInput.Hide();
            
            canvasGroup.DOFade(0f, fadeDuration)
                .OnComplete(() => SetPanelActive(false, instant: true));
        }

        public void Toggle()
        {
            if (IsOpen) Close();
            else Open();
        }


        private void SetPanelActive(bool active, bool instant)
        {
            canvasGroup.blocksRaycasts = active;
            canvasGroup.interactable = active;

            if (instant)
            {
                canvasGroup.alpha = active ? 1f : 0f;
                return;
            }

            canvasGroup.DOFade(active ? 1f : 0f, fadeDuration);
        }
    }
}
