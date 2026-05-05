using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;

public class InventoryUI : MonoBehaviour
{
    public static InventoryUI Instance { get; private set; }

    [SerializeField] private Image[] slotImages;
    [SerializeField] private Image[] slotBackgrounds; // These are the background images
    [SerializeField] private Color selectedColor = Color.red;
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

    private void Update()
    {
        if (PlayerControlGate.IsInputBlocked)
            return;

        if (Input.GetKeyDown(KeyCode.Alpha1)) SelectSlot(0);
        if (Input.GetKeyDown(KeyCode.Alpha2)) SelectSlot(1);
        if (Input.GetKeyDown(KeyCode.Alpha3)) SelectSlot(2);
        if (Input.GetKeyDown(KeyCode.Alpha4)) SelectSlot(3);

        if (Input.GetKeyDown(KeyCode.E)) TryUseSelectedItem();
    }

    public void RefreshUI()
    {
        if (InventoryManager.Instance == null) return;

        var items = InventoryManager.Instance.GetInventory();

        for (int i = 0; i < slotImages.Length; i++)
        {
            bool hasItem = i < items.Count && items[i] != null && items[i].icon != null;

            slotImages[i].sprite = hasItem ? items[i].icon : null;
            slotImages[i].enabled = hasItem;

            // Set selection color
            if (slotBackgrounds[i] != null)
            {
                slotBackgrounds[i].color = (i == selectedIndex) ? selectedColor : defaultColor;
            }
        }
    }


    public void SelectSlot(int index)
    {
        selectedIndex = index;
        RefreshUI();
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
                HudManager.Instance?.ShowSubtitle("You cannot use that here.", 3f);
            }
        }
        else
        {
            HudManager.Instance?.ShowSubtitle("Nothing in reach.", 2.5f);
        }
    }

}
