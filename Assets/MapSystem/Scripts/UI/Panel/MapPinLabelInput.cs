using Artifika.Map.Core;
using Michsky.UI.Reach;
using Sirenix.OdinInspector;
using TMPro;
using UnityEngine;

namespace Artifika.Map.UI
{
    public class MapPinLabelInput : MonoBehaviour
    {
        [Title("Reach UI")]
        [SerializeField] private ModalWindowManager modalWindow;

        [Title("Input")]
        [SerializeField] private TMP_InputField inputField;

        private MapSystemContext mapSystemContext;
        private Vector3 pendingWorldPos;
        private bool eventsBound;

        public bool IsVisible { get; private set; }

        private void Awake()
        {
            if (MapSystemContext.TryGetInitialized(out var context))
                mapSystemContext = context;

            modalWindow.showConfirmButton = true;
            modalWindow.showCancelButton = true;
            
            BindModalEvents();
            Hide();
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
        }

        private void OnDestroy()
        {
            UnbindModalEvents();
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
        }

        public void Show(Vector3 worldPos)
        {
            pendingWorldPos = worldPos;

            if (inputField != null)
            {
                inputField.text = string.Empty;
            }

            if (modalWindow != null)
            {
                modalWindow.UpdateUI();
                modalWindow.OpenWindow();
            }

            IsVisible = true;

            if (inputField != null)
            {
                inputField.ActivateInputField();
                inputField.Select();
            }
        }

        public void Hide()
        {
            if (modalWindow != null)
            {
                modalWindow.CloseWindow();
            }

            IsVisible = false;
        }
        

        private void OnConfirm()
        {
            if (mapSystemContext == null)
                return;

            string label = inputField != null ? inputField.text.Trim() : string.Empty;
            mapSystemContext.MarkerFactory.PlacePin(
                pendingWorldPos,
                label,
                MarkerReplicationMode.SharedServerAuthoritative);
            Hide();
        }

        private void OnCancel()
        {
            Hide();
        }

        private void OnOpened()
        {
            IsVisible = true;

            if (inputField != null)
            {
                inputField.ActivateInputField();
                inputField.Select();
            }
        }

        private void OnClosed()
        {
            IsVisible = false;
        }

        private void BindModalEvents()
        {
            if (eventsBound || modalWindow == null)
                return;

            modalWindow.onConfirm.AddListener(OnConfirm);
            modalWindow.onCancel.AddListener(OnCancel);
            modalWindow.onOpen.AddListener(OnOpened);
            modalWindow.onClose.AddListener(OnClosed);

            if (inputField != null)
            {
                inputField.onSubmit.AddListener(_ => OnConfirm());
            }

            eventsBound = true;
        }

        private void UnbindModalEvents()
        {
            if (!eventsBound || modalWindow == null)
                return;

            modalWindow.onConfirm.RemoveListener(OnConfirm);
            modalWindow.onCancel.RemoveListener(OnCancel);
            modalWindow.onOpen.RemoveListener(OnOpened);
            modalWindow.onClose.RemoveListener(OnClosed);

            if (inputField != null)
            {
                inputField.onSubmit.RemoveListener(_ => OnConfirm());
            }

            eventsBound = false;
        }
    }
}
