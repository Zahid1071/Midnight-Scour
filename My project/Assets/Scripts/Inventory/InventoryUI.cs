using UnityEngine;
using UnityEngine.UI;
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
        if (PlayerControlGate.IsInputBlocked)
            return;

        if (Input.GetKeyDown(KeyCode.Alpha1)) SelectSlot(0);
        if (Input.GetKeyDown(KeyCode.Alpha2)) SelectSlot(1);
        if (Input.GetKeyDown(KeyCode.Alpha3)) SelectSlot(2);
        if (Input.GetKeyDown(KeyCode.Alpha4)) SelectSlot(3);

        if (Input.GetKeyDown(KeyCode.E)) TryUseSelectedItem();
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


    public void SelectSlot(int index)
    {
        selectedIndex = index;
        RefreshUI();
        HeldItemDisplay.Instance?.RefreshHeldVisual();
    }


    public InventoryItem GetSelectedItem()
    {
        var inventory = InventoryManager.Instance.GetInventory();
        if (selectedIndex >= 0 && selectedIndex < inventory.Count)
            return inventory[selectedIndex];
        return null;
    }

    private void TryUseSelectedItem()
    {
        InventoryItem selected = GetSelectedItem();
        if (selected == null)
            return;

        if (Camera.main == null)
            return;

        // Raycast to detect interactable
        if (Physics.Raycast(Camera.main.transform.position, Camera.main.transform.forward, out RaycastHit hit, 3f))
        {
            IInteractable target = hit.collider.GetComponent<IInteractable>();
            if (target == null)
                target = hit.collider.GetComponentInParent<IInteractable>();

            if (target is IItemReceiver receiver)
            {
                receiver.ReceiveItem(selected);
            }
            else if (target != null)
            {
                // ConditionalInteractible, InteractableItem, etc. — resolved via PlayerInteractor.Interact(), not item-on-receiver
                return;
            }
            else
            {
                HudManager.Instance?.ShowGameplaySubtitle(LocalizationTables.GameplayKeys.CannotUseHere, 3f);
            }
        }
        else
        {
            HudManager.Instance?.ShowGameplaySubtitle(LocalizationTables.GameplayKeys.NothingInReach, 2.5f);
        }
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
            HudManager.Instance?.ShowGameplaySubtitle(LocalizationTables.GameplayKeys.CannotDropItem, 2.5f);
            return;
        }

        GameObject dropPrefab = selected.GetDropPrefab();
        if (dropPrefab == null)
        {
            HudManager.Instance?.ShowGameplaySubtitle(LocalizationTables.GameplayKeys.CannotDropItem, 2.5f);
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
        HeldItemDisplay.Instance?.RefreshHeldVisual();

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
            dropItemMass);

        if (selected.pickUpSFX != null && SoundManager.Instance != null)
            SoundManager.Instance.PlaySFXAt(selected.pickUpSFX, dropped.transform.position);

        HudManager.Instance?.ShowGameplaySubtitle(
            LocalizationTables.GameplayKeys.DroppedItem, 2f,
            ("item", selected.itemName));
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
