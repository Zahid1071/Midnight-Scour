using FS_Atmo;
using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(CharacterController))]
public class PlayerInteractor : MonoBehaviour
{
    public static PlayerInteractor Instance { get; private set; }

    [Tooltip("How far the UI can see interactables (crosshair hint).")]
    [SerializeField] private float hintRange = 3f;
    [Tooltip("How far the player can actually interact (arm reach). Bone adds interactionReachBonus.")]
    [SerializeField] private float interactRange = 1.5f;
    [SerializeField] private LayerMask interactLayer;
    [SerializeField] private Transform cameraTransform;
    [SerializeField] private string outOfReachMessage = "I can't reach that.";

    private PlayerHider _playerHider;
    private CharacterController _characterController;
    private int _lastInteractFrame = -1;
    private Collider _lastInteractCollider;

    public float HintRange => hintRange;
    public float BaseInteractRange => interactRange;
    public LayerMask InteractLayer => interactLayer;
    public Transform CameraTransform => cameraTransform;

    /// <summary>Kept for callers expecting base range; prefer <see cref="GetActionRangeForHeldItem"/> or <see cref="GetWorldInteractRange"/>.</summary>
    public float InteractRange => interactRange;

    private void Awake()
    {
        Instance = this;
        _characterController = GetComponent<CharacterController>();

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

        if (!WasInteractPressedThisFrame())
            return;

        // E with a held item: world interact (open door, pickup, etc.) — handled in InventoryUI.
        if (InventoryUI.Instance != null && InventoryUI.Instance.GetSelectedItem() != null)
            return;

        TryInteract();
    }

    /// <summary>Arm reach when using the held item (includes interactionReachBonus).</summary>
    public float GetActionRangeForHeldItem()
    {
        float range = interactRange;
        InventoryItem selected = InventoryUI.Instance?.GetSelectedItem();
        if (selected != null)
            range += selected.interactionReachBonus;
        return range;
    }

    /// <summary>Arm reach for empty-hand / world interact (E on door, LMB with nothing selected).</summary>
    public float GetWorldInteractRange() => interactRange;

    /// <summary>Reach used by <see cref="TryInteract"/> (includes held-item bonus when something is selected).</summary>
    public float GetInteractActionRange()
    {
        if (InventoryUI.Instance != null && InventoryUI.Instance.GetSelectedItem() != null)
            return GetActionRangeForHeldItem();
        return GetWorldInteractRange();
    }

    public bool TryRaycastInteractable(float maxRange, out RaycastHit hit, out IInteractable interactable)
    {
        interactable = null;
        hit = default;

        if (cameraTransform == null)
            return false;

        if (!Physics.Raycast(cameraTransform.position, cameraTransform.forward, out hit, maxRange, interactLayer))
            return false;

        interactable = hit.collider.GetComponent<IInteractable>();
        if (interactable == null)
            interactable = hit.collider.GetComponentInParent<IInteractable>();
        return interactable != null;
    }

    /// <summary>
    /// In range if the target is close enough along the view ray OR from the player's feet
    /// (so floor pickups aren't blocked by eye-to-target ray length).
    /// </summary>
    public bool IsWithinRange(RaycastHit hit, float range)
    {
        float fromCamera = hit.distance;
        float fromFeet = Vector3.Distance(GetFeetPosition(), hit.point);
        return fromCamera <= range || fromFeet <= range;
    }

    public bool IsWithinHintRange(RaycastHit hit) => IsWithinRange(hit, hintRange);

    public bool IsWithinActionRange(RaycastHit hit, float actionRange) => IsWithinRange(hit, actionRange);

    /// <summary>Ray length so a floor target within hint range from the feet can still be hit.</summary>
    float GetHintRaycastMaxDistance()
    {
        if (cameraTransform == null)
            return hintRange;
        return hintRange + Vector3.Distance(GetFeetPosition(), cameraTransform.position);
    }

    /// <summary>Raycast for UI / targeting; filters with <see cref="IsWithinHintRange"/>.</summary>
    public bool TryRaycastForHint(out RaycastHit hit, out IInteractable interactable)
    {
        if (!TryRaycastInteractable(GetHintRaycastMaxDistance(), out hit, out interactable))
            return false;
        return IsWithinHintRange(hit);
    }

    public bool TryGetFocusedInteractable(out IInteractable interactable)
    {
        return TryRaycastInteractable(GetActionRangeForHeldItem(), out _, out interactable);
    }

    /// <summary>World interact: open doors, pick up items, etc.</summary>
    public bool TryInteract()
    {
        if (!TryRaycastForHint(out RaycastHit hit, out IInteractable interactable))
        {
            InteractionFeedback.Fail("Nothing here.");
            return false;
        }

        if (!IsWithinActionRange(hit, GetInteractActionRange()))
        {
            ShowOutOfReach();
            return false;
        }

        return PerformInteract(interactable, hit);
    }

    /// <summary>Runs <see cref="IInteractable.Interact"/> once per target per frame (avoids E+LMB double pickup).</summary>
    public bool PerformInteract(IInteractable interactable, RaycastHit hit)
    {
        if (interactable == null)
            return false;

        if (IsDuplicateInteractThisFrame(hit.collider))
            return false;

        RegisterInteractForFrame(hit.collider);

        bool lockedDoorAttempt = interactable is SimpleOpenClose door && !door.objectOpen && door.isLocked;
        interactable.Interact();

        if (!lockedDoorAttempt)
            InteractionUIManager.Instance?.PlaySuccessShake();

        return true;
    }

    public void ShowOutOfReach() => InteractionFeedback.Fail(outOfReachMessage, 3f);

    Vector3 GetFeetPosition()
    {
        if (_characterController != null)
        {
            Vector3 worldCenter = transform.TransformPoint(_characterController.center);
            float halfHeight = _characterController.height * 0.5f;
            return worldCenter + Vector3.down * (halfHeight - _characterController.skinWidth);
        }

        return transform.position;
    }

    public bool IsDuplicateInteractThisFrame(Collider collider) =>
        Time.frameCount == _lastInteractFrame && collider != null && collider == _lastInteractCollider;

    public void RegisterInteractForFrame(Collider collider)
    {
        _lastInteractFrame = Time.frameCount;
        _lastInteractCollider = collider;
    }

    static bool WasInteractPressedThisFrame()
    {
        return Keyboard.current != null && Keyboard.current.eKey.wasPressedThisFrame;
    }
}
