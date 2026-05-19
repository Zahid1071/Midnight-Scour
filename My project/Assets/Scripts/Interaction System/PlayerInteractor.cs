using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerInteractor : MonoBehaviour
{
    [SerializeField] private float interactRange = 1f;
    [SerializeField] private LayerMask interactLayer;
    [SerializeField] private Transform cameraTransform;

    private PlayerHider _playerHider;

    private void Awake()
    {
        if (!TryGetComponent<HeldItemDisplay>(out _))
            gameObject.AddComponent<HeldItemDisplay>();

        TryGetComponent(out _playerHider);
    }

    private void Update()
    {
        if (PlayerControlGate.IsInputBlocked)
            return;

        if (_playerHider != null && _playerHider.isHidden)
            return;

        if (WasInteractPressedThisFrame())
            TryInteract();
    }

    static bool WasInteractPressedThisFrame()
    {
        if (Keyboard.current != null && Keyboard.current.eKey.wasPressedThisFrame)
            return true;
        return Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame;
    }

    void TryInteract()
    {
        if (cameraTransform == null)
            return;
        if (Physics.Raycast(cameraTransform.position, cameraTransform.forward, out RaycastHit hit, interactRange, interactLayer))
        {
            IInteractable interactable = hit.collider.GetComponent<IInteractable>();
            if (interactable == null)
                interactable = hit.collider.GetComponentInParent<IInteractable>();
            interactable?.Interact();
        }
    }
}
