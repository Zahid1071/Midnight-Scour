using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerInteractor : MonoBehaviour
{
    [SerializeField] private float interactRange = 1f;
    [SerializeField] private LayerMask interactLayer;
    [SerializeField] private Transform cameraTransform;

    private void Update()
    {
        if (PlayerControlGate.IsInputBlocked)
            return;
        if (Keyboard.current != null && Keyboard.current.eKey.wasPressedThisFrame)
        {
            TryInteract();
        }
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
