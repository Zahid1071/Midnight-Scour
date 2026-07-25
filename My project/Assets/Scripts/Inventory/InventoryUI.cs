using FS_Atmo;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;
using System.Collections.Generic;

public class InventoryUI : MonoBehaviour
{
    public static InventoryUI Instance { get; private set; }

    [SerializeField] private Image[] slotImages;
    [SerializeField] private Image[] slotBackgrounds; // These are the background images
    [SerializeField] private Color selectedColor = Color.red;

    [Header("Drop throw")]
    [SerializeField] private float dropSpawnForward = 0.55f;
    [SerializeField] private float dropSpawnDown = 0.25f;
    [SerializeField] private float dropThrowForce = 2.2f;
    [SerializeField] private float dropThrowUpward = 0.6f;
    [SerializeField] private float dropItemMass = 1f;

    private Color defaultColor = Color.white;

    private int selectedIndex = -1;

    private void Awake()
    {
        Instance = this;
        defaultColor = slotBackgrounds[0].color;
    }

    private void Start()
    {
        RefreshUI();
    }

    private void OnEnable()
    {
        RefreshUI();
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    private void Update()
    {
        if (PlayerControlGate.IsInputBlocked || ReadPanelUI.IsBlockingPlayerInput)
            return;

        if (Input.GetKeyDown(KeyCode.Alpha1)) SelectSlot(0);
        if (Input.GetKeyDown(KeyCode.Alpha2)) SelectSlot(1);
        if (Input.GetKeyDown(KeyCode.Alpha3)) SelectSlot(2);
        if (Input.GetKeyDown(KeyCode.Alpha4)) SelectSlot(3);

        if (Mouse.current != null)
        {
            float scroll = Mouse.current.scroll.ReadValue().y;
            if (scroll > 0f)
                CycleSelectedSlot(-1);
            else if (scroll < 0f)
                CycleSelectedSlot(1);
        }

        if (WasUseItemPressedThisFrame())
            HandleInteractPress(isPrimaryClick: true);

        if (WasUseItemOnInteractKeyThisFrame())
            HandleInteractPress(isPrimaryClick: false);

        if (Input.GetKeyDown(KeyCode.G)) TryDropSelectedItem();
    }

    public void RefreshUI()
    {
        if (!this || InventoryManager.Instance == null) return;
        if (slotImages == null || slotBackgrounds == null) return;

        var items = InventoryManager.Instance.GetInventory();

        for (int i = 0; i < slotImages.Length; i++)
        {
            if (slotImages[i] == null)
                continue;
            if (i >= slotBackgrounds.Length)
                break;

            bool hasItem = i < items.Count && items[i] != null && items[i].icon != null;

            slotImages[i].sprite = hasItem ? items[i].icon : null;
            slotImages[i].enabled = hasItem;

            if (slotBackgrounds[i] != null)
                slotBackgrounds[i].color = (i == selectedIndex) ? selectedColor : defaultColor;
        }
    }


    static bool WasUseItemPressedThisFrame()
    {
        return Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame;
    }

    static bool WasUseItemOnInteractKeyThisFrame()
    {
        return Keyboard.current != null && Keyboard.current.eKey.wasPressedThisFrame;
    }

    void HandleInteractPress(bool isPrimaryClick)
    {
        if (GetSelectedItem() != null)
            TryUseSelectedItem(isPrimaryClick);
        else
            PlayerInteractor.Instance?.TryInteract();
    }

    public void SelectSlot(int index)
    {
        selectedIndex = index;
        RefreshUI();
    }

    void CycleSelectedSlot(int direction)
    {
        int slotCount = slotImages != null ? slotImages.Length : InventoryManager.Instance?.SlotCount() ?? 4;
        if (slotCount <= 0)
            return;

        if (selectedIndex < 0)
        {
            SelectSlot(direction > 0 ? 0 : slotCount - 1);
            return;
        }

        int next = (selectedIndex + direction + slotCount) % slotCount;
        SelectSlot(next);
    }


    public InventoryItem GetSelectedItem()
    {
        var inventory = InventoryManager.Instance.GetInventory();
        if (selectedIndex >= 0 && selectedIndex < inventory.Count)
            return inventory[selectedIndex];
        return null;
    }

    private void TryUseSelectedItem(bool isPrimaryClick)
    {
        InventoryItem selected = GetSelectedItem();
        if (selected == null)
            return;

        var interactor = PlayerInteractor.Instance;
        if (interactor == null)
            return;

        float actionRange = interactor.GetActionRangeForHeldItem();

        if (!interactor.TryRaycastForHint(out RaycastHit hit, out IInteractable target))
        {
            ShowWarning(Loc.Get("gameplay.nothing_in_reach"), 2.5f);
            return;
        }

        if (!interactor.IsWithinActionRange(hit, actionRange))
        {
            if (TryShowRegularBoneOnScrewdriverHint(selected, target))
                return;

            interactor.ShowOutOfReach();
            return;
        }

        if (LockedOpenClose.IsUnlockedDoor(target))
        {
            interactor.PerformInteract(target, hit);
            return;
        }

        IItemReceiver receiver = target as IItemReceiver;
        if (receiver == null)
            receiver = hit.collider.GetComponent<IItemReceiver>();
        if (receiver == null)
            receiver = hit.collider.GetComponentInParent<IItemReceiver>();

        if (receiver != null)
        {
            if (interactor.IsDuplicateInteractThisFrame(hit.collider))
                return;

            interactor.RegisterInteractForFrame(hit.collider);
            if (receiver.ReceiveItem(selected))
            {
                // Receivers that own their success SFX/shake (avoid held-item useSuccessSound).
                if (receiver is ItemCombineReceiver
                    || receiver is BenchVicePuzzle
                    || receiver is ToiletWaterFlush)
                {
                    // Handled inside the receiver.
                }
                else if (receiver is InteractableItem)
                {
                    // World pickup: plays pickUpSFX only — not the held item's useSuccessSound
                    // (e.g. hammer hit must not fire when picking up a vase while hammer is selected).
                    InteractionUIManager.Instance?.PlaySuccessShake();
                }
                else if (receiver is SimpleOpenClose || receiver is LockedOpenClose)
                {
                    InteractionUIManager.Instance?.PlaySuccessShake();
                }
                else
                {
                    InteractionFeedback.PlaySound(selected.useSuccessSound, hit.point);
                    InteractionUIManager.Instance?.PlaySuccessShake();
                }
            }
            return;
        }

        // Pickups, bolts, etc. — extended reach tools (e.g. gummy bone through grate).
        interactor.PerformInteract(target, hit);
    }

    void ShowWarning(string message, float duration) => InteractionFeedback.Fail(message, duration);

    const string DefaultBoneOnScrewdriverHintKey = "gameplay.bone_slip";

    static bool IsRegularBone(InventoryItem item) =>
        item != null && item.itemName == "Bone" && item.interactionReachBonus <= 0f;

    static bool IsScrewdriverPickup(InteractableItem pickup) =>
        pickup != null && pickup.itemData != null && pickup.itemData.itemName == "Screwdriver";

    bool TryShowRegularBoneOnScrewdriverHint(InventoryItem selected, IInteractable target)
    {
        if (target is not InteractableItem pickup)
            return false;

        return TryShowRegularBoneOnScrewdriverHint(selected, pickup);
    }

    bool TryShowRegularBoneOnScrewdriverHint(InventoryItem selected, InteractableItem pickup)
    {
        if (!IsRegularBone(selected) || !IsScrewdriverPickup(pickup))
            return false;

        ShowWarning(GetBoneOnScrewdriverHint(selected), 4f);
        return true;
    }

    static string GetBoneOnScrewdriverHint(InventoryItem selected)
    {
        return !string.IsNullOrWhiteSpace(selected.reachAttemptFailMessage)
            ? selected.reachAttemptFailMessage
            : Loc.Get(DefaultBoneOnScrewdriverHintKey);
    }

    private void TryDropSelectedItem()
    {
        if (InventoryManager.Instance == null)
            return;

        InventoryItem selected = GetSelectedItem();
        if (selected == null)
            return;

        if (!selected.canDrop)
        {
            ShowWarning("You cannot drop that item.", 2.5f);
            return;
        }

        GameObject dropPrefab = selected.GetDropPrefab();
        if (dropPrefab == null)
        {
            ShowWarning("This item has no drop prefab assigned.", 2.5f);
            return;
        }

        if (Camera.main == null)
            return;

        Transform cam = Camera.main.transform;
        int dropIndex = selectedIndex;

        if (!InventoryManager.Instance.RemoveItemAt(dropIndex, refreshUI: false))
            return;

        AdjustSelectionAfterRemove();
        RefreshUI();

        Vector3 spawnPos = GetDropThrowSpawnPosition(cam);
        Quaternion spawnRot = GetDropSpawnRotation(cam);
        Vector3 throwDir = cam.forward;

        GameObject dropped = ItemDropHelper.SpawnAndThrow(
            dropPrefab,
            spawnPos,
            spawnRot,
            throwDir,
            dropThrowForce,
            dropThrowUpward,
            dropItemMass,
            selected.dropLandSFX);

        if (selected.pickUpSFX != null && SoundManager.Instance != null)
            SoundManager.Instance.PlaySFXAt(selected.pickUpSFX, dropped.transform.position);

        string msg = Loc.Format("gameplay.item_dropped", selected.GetLocalizedName());
        HudManager.Instance?.ShowSubtitle(msg, 2f);
    }

    private Vector3 GetDropThrowSpawnPosition(Transform cam)
    {
        return cam.position + cam.forward * dropSpawnForward - cam.up * dropSpawnDown;
    }

    private static Quaternion GetDropSpawnRotation(Transform cam)
    {
        Vector3 forward = cam.forward;
        forward.y = 0f;
        if (forward.sqrMagnitude < 0.001f)
            forward = cam.forward;

        forward.Normalize();
        return Quaternion.LookRotation(forward, Vector3.up);
    }

    private void AdjustSelectionAfterRemove()
    {
        var items = InventoryManager.Instance.GetInventory();
        if (items.Count == 0)
        {
            selectedIndex = -1;
            return;
        }

        if (selectedIndex >= items.Count)
            selectedIndex = items.Count - 1;
    }

}
