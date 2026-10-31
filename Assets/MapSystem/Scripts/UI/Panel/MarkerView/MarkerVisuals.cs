using DG.Tweening;
using Sirenix.OdinInspector;
using UnityEngine;
using UnityEngine.UI;

namespace Artifika.Map.UI
{
    [RequireComponent(typeof(MapMarkerView))]
    public class MarkerVisuals : MonoBehaviour
    {
        [Title("Hover Animation")]
        [SerializeField] private float hoverScale = 1.15f;
        [SerializeField] private float animationDuration = 0.15f;
        [SerializeField] private Ease hoverEase = Ease.OutQuad;
        [SerializeField] private Ease exitEase = Ease.OutQuad;
        [SerializeField] private Color hoverColor = new Color(1f, 0.95f, 0.6f, 1f);

        [Title("Audio")]
        [SerializeField] private AudioSource audioSource;
        [SerializeField] private AudioClip hoverClip;
        [SerializeField] private AudioClip selectedClip;

        private MapMarkerView view;
        private Image icon;
        private RectTransform iconRect;

        private Vector3 initialScale;
        private Color initialColor;
        private Tween scaleTween;
        private Tween colorTween;

        private void Awake()
        {
            view = GetComponent<MapMarkerView>();
            icon = view.Icon;

            if (icon != null)
            {
                iconRect = icon.rectTransform;
                initialScale = iconRect.localScale;
                initialColor = icon.color;
            }

            if (audioSource == null)
                audioSource = GetComponent<AudioSource>();

            view.OnHoverEnter += HandleHoverEnter;
            view.OnHoverExit += HandleHoverExit;
            view.OnSelected += HandleSelected;
        }

        private void OnDestroy()
        {
            if (view != null)
            {
                view.OnHoverEnter -= HandleHoverEnter;
                view.OnHoverExit -= HandleHoverExit;
                view.OnSelected -= HandleSelected;
            }

            scaleTween?.Kill();
            colorTween?.Kill();
        }

        private void HandleHoverEnter(MapMarkerView _)
        {
            AnimateHover(true);
            PlayClip(hoverClip);
        }

        private void HandleHoverExit(MapMarkerView _)
        {
            AnimateHover(false);
            PlayClip(hoverClip); // same sound for enter and exit
        }

        private void HandleSelected(MapMarkerView _)
        {
            PlayClip(selectedClip);
        }

        private void AnimateHover(bool hovered)
        {
            if (icon == null || iconRect == null)
                return;

            scaleTween?.Kill();
            colorTween?.Kill();

            Vector3 targetScale = hovered ? initialScale * hoverScale : initialScale;
            Color targetColor = hovered ? hoverColor : initialColor;
            Ease ease = hovered ? hoverEase : exitEase;

            scaleTween = iconRect.DOScale(targetScale, animationDuration).SetEase(ease);
            colorTween = icon.DOColor(targetColor, animationDuration).SetEase(ease);
        }

        private void PlayClip(AudioClip clip)
        {
            if (audioSource == null || clip == null)
                return;

            audioSource.PlayOneShot(clip);
        }
    }
}