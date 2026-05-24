using FS_Atmo;
using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerInteractor : MonoBehaviour
{
    public static PlayerInteractor Instance { get; private set; }

    [SerializeField] private float interactRange = 3f;
    [SerializeField] private LayerMask interactLayer;
    [SerializeField] private Transform cameraTransform;

    private PlayerHider _playerHider;

    public float InteractRange => interactRange;
    public LayerMask InteractLayer => interactLayer;

    private void Awake()
    {
        Instance = this;

        if (!TryGetComponent<HeldItemDisplay>(out _))
            gameObject.AddComponent<HeldItemDisplay>();

        TryGetComponent(out _playerHider);
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
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

    public void TryInteract()
    {
        if (ShouldDeferInteractToItemUse())
            return;

        if (TryGetFocusedInteractable(out IInteractable interactable))
            interactable.Interact();
    }

    static bool WasInteractPressedThisFrame()
    {
        return Keyboard.current != null && Keyboard.current.eKey.wasPressedThisFrame;
    }

    bool ShouldDeferInteractToItemUse()
    {
        if (InventoryUI.Instance == null || InventoryUI.Instance.GetSelectedItem() == null)
            return false;
        if (!TryGetFocusedInteractable(out IInteractable interactable))
            return false;
        if (interactable is SimpleOpenClose door)
            return door.isLocked;

        return interactable is IItemReceiver;
    }

    public bool TryGetFocusedInteractable(out IInteractable interactable)
    {
        interactable = null;
        if (cameraTransform == null)
            return false;
        if (!Physics.Raycast(cameraTransform.position, cameraTransform.forward, out RaycastHit hit, interactRange, interactLayer))
            return false;

        interactable = hit.collider.GetComponent<IInteractable>();
        if (interactable == null)
            interactable = hit.collider.GetComponentInParent<IInteractable>();
        return interactable != null;
    }

}
