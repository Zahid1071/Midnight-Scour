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
        {
            return;
        }

        // Raycast to detect item receiver
        if (Physics.Raycast(Camera.main.transform.position, Camera.main.transform.forward, out RaycastHit hit, 3f))
        {
            // ✅ Önce direkt IItemReceiver kontrolü yap
            IItemReceiver receiver = hit.collider.GetComponent<IItemReceiver>();
            
            // ✅ Parent'ta kontrol et (kapılar genelde child objelerde olabilir)
            if (receiver == null)
            {
                receiver = hit.collider.GetComponentInParent<IItemReceiver>();
            }
            
            // ✅ Child'larda da kontrol et (root objede olabilir)
            if (receiver == null)
            {
                receiver = hit.collider.GetComponentInChildren<IItemReceiver>();
            }
            
            // ✅ Root'ta kontrol et
            if (receiver == null && hit.collider.transform.root != null)
            {
                receiver = hit.collider.transform.root.GetComponent<IItemReceiver>();
            }
            
            if (receiver != null)
            {
                receiver.ReceiveItem(selected);
            }
            else
            {
                Debug.Log($"❌ This object doesn't accept items. (Hit: {hit.collider.name})");
            }
        }
    }

}
