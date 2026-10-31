using System;
using System.Collections;
using Sirenix.OdinInspector;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Artifika.Map.UI
{
    public class MapInputHandler : MonoBehaviour
    {
        [Title("References")]
        [SerializeField] private MapPanelController panelController;
        [SerializeField] private MapViewController viewController;
        [SerializeField] private MapInteractionController interactionController;

        [Title("Settings")] [SerializeField, Range(1f, 20f)]
        private float dragThreshold = 6f;

        [SerializeField, Range(0.5f, 10f)] private float keyboardPanSpeed = 4f;

        [Title("Debug")]
        [ShowInInspector, ReadOnly] private Vector2 currentPos;

        private PlayerInputs _playerInputs;
        private bool _isSubscribed = false;
        
        private void Start()
        {
            StartCoroutine(nameof(BindInputBootstrap));
        }

        private IEnumerator BindInputBootstrap()
        {
            while (Artifika.InputSystem.InputBootstrap.Local == null || !Artifika.InputSystem.InputBootstrap.Local.IsInitialized)
            {
                yield return null;
            }

            _playerInputs = Artifika.InputSystem.InputBootstrap.Local.Actions;
            SubscribeToActions();
        }
        
        private void OnEnable()
        { 
            SubscribeToActions();   
        }

        private void OnDisable()
        {
            UnsubscribeToActions();
        }

        private void SubscribeToActions()
        {
            if (_playerInputs == null || _isSubscribed) return;
            
            _playerInputs.Player.Map.performed += HandleToggle;
            _playerInputs.Player.EscapeGame.performed += HandleEscape;
            
            var map = _playerInputs.Map;
            map.PointerPos.performed += HandlePointerMoved;
            map.MousePan.performed += HandleMousePan;
            map.KeyboardPan.performed += HandleKeyboardPanPerformed;
            map.KeyboardPan.canceled += HandleKeyboardPanCanceled;
            map.Zoom.performed += HandleZoom;
            map.PrimaryKey.performed += HandlePrimaryPressed; 
            map.SecondaryKey.performed += HandleSecondaryClicked;
            map.FocusPlayer.performed += HandleFocusOnPlayer;
            
            _isSubscribed = true;
        }

        private void UnsubscribeToActions()
        {
            if(_playerInputs == null || !_isSubscribed) return;
            
            _playerInputs.Player.Map.performed -= HandleToggle;
            _playerInputs.Player.EscapeGame.performed -= HandleEscape;
            
            var map = _playerInputs.Map;
            map.PointerPos.performed -= HandlePointerMoved;
            map.MousePan.performed -= HandleMousePan;
            map.KeyboardPan.performed -= HandleKeyboardPanPerformed;
            map.KeyboardPan.canceled -= HandleKeyboardPanCanceled;
            map.Zoom.performed -= HandleZoom;
            map.PrimaryKey.performed -= HandlePrimaryPressed; 
            map.SecondaryKey.performed -= HandleSecondaryClicked;
            map.FocusPlayer.performed -= HandleFocusOnPlayer;
            
            _isSubscribed = false;
        }
        
        private void HandleToggle(InputAction.CallbackContext ctx)
        {
            if (Artifika.InputSystem.UIInputFocusGuard.IsTextInputFocused())
                return;

            panelController.Toggle();
        }

        private void HandleEscape(InputAction.CallbackContext ctx)
        {
            Artifika.InputSystem.UIInputFocusGuard.DeactivateFocusedTextInput();

            if (panelController.IsOpen)
                panelController.Toggle();
        }

        private void HandlePointerMoved(InputAction.CallbackContext ctx)
        {
            currentPos = ctx.ReadValue<Vector2>();
            interactionController.OnHover(currentPos);
        }
        
        private void HandleMousePan(InputAction.CallbackContext ctx)
        {
            var delta = ctx.ReadValue<Vector2>();
            if (delta == Vector2.zero) return;
            if (delta.magnitude < dragThreshold) return;

            viewController.Pan(delta);
        }

        private void HandleKeyboardPanPerformed(InputAction.CallbackContext ctx)
        {
            if (Artifika.InputSystem.UIInputFocusGuard.IsTextInputFocused())
            {
                viewController.SetKeyboardPanDelta(Vector2.zero);
                return;
            }

            HandleKeyboardPan(ctx.ReadValue<Vector2>());
        }

        private void HandleKeyboardPanCanceled(InputAction.CallbackContext ctx)
        {
            HandleKeyboardPan(Vector2.zero);
        }
        
        private void HandleKeyboardPan(Vector2 delta)
        {
            viewController.SetKeyboardPanDelta(delta * keyboardPanSpeed);
        }

        private void HandleZoom(InputAction.CallbackContext ctx) =>
            viewController.ZoomAtScreenPoint(ctx.ReadValue<float>(), currentPos);

        private void HandlePrimaryPressed(InputAction.CallbackContext ctx)
        {
            interactionController.OnLeftClick(currentPos);
        }

        private void HandleSecondaryClicked(InputAction.CallbackContext ctx) =>
            interactionController.OnRightClick(currentPos);

        private void HandleFocusOnPlayer(InputAction.CallbackContext ctx)
        {
            if (Artifika.InputSystem.UIInputFocusGuard.IsTextInputFocused())
                return;

            viewController.CenterOnPlayer();
        }
    }
}
