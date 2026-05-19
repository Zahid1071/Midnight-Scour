using UnityEngine;

/// <summary>
/// Spawns the selected inventory item's <see cref="InventoryItem.heldPrefab"/> under a hand anchor
/// (child of PlayerCameraRoot by default). Wired from <see cref="InventoryUI"/> and <see cref="InventoryManager"/>.
/// </summary>
[DefaultExecutionOrder(-100)]
public class HeldItemDisplay : MonoBehaviour
{
    public static HeldItemDisplay Instance { get; private set; }

    [SerializeField] private Transform handAnchor;

    [Tooltip("Used when hand anchor is not assigned: parent transform name under the player.")]
    [SerializeField] private string cameraRootName = "PlayerCameraRoot";

    [SerializeField] private string autoHandObjectName = "HandHold";

    [SerializeField] private Vector3 defaultHandLocalPosition = new Vector3(0.28f, -0.22f, 0.42f);

    [SerializeField] private Vector3 defaultHandLocalEulerAngles = new Vector3(0f, 15f, 0f);

    private GameObject currentVisual;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(this);
            return;
        }

        Instance = this;
        EnsureHandAnchor();
    }

    private void OnDestroy()
    {
        ClearVisual();
        if (Instance == this)
            Instance = null;
    }

    private void Start()
    {
        RefreshHeldVisual();
    }

    private void EnsureHandAnchor()
    {
        if (handAnchor != null)
            return;

        Transform root = transform.Find(cameraRootName);
        if (root == null)
        {
            Debug.LogWarning($"[{nameof(HeldItemDisplay)}] No '{cameraRootName}' child found under player — assign hand anchor in the inspector.");
            return;
        }

        Transform existing = root.Find(autoHandObjectName);
        if (existing != null)
        {
            handAnchor = existing;
            return;
        }

        var handGo = new GameObject(autoHandObjectName);
        handGo.transform.SetParent(root, false);
        handGo.transform.localPosition = defaultHandLocalPosition;
        handGo.transform.localRotation = Quaternion.Euler(defaultHandLocalEulerAngles);
        handAnchor = handGo.transform;
    }

    /// <summary>Rebuilds the in-hand mesh from the currently selected inventory slot.</summary>
    public void RefreshHeldVisual()
    {
        ClearVisual();

        if (handAnchor == null)
            EnsureHandAnchor();
        if (handAnchor == null)
            return;

        InventoryItem item = InventoryUI.Instance != null ? InventoryUI.Instance.GetSelectedItem() : null;
        if (item == null || item.heldPrefab == null)
            return;

        currentVisual = Instantiate(item.heldPrefab, handAnchor, false);
        ApplyItemHandTransform(item, currentVisual.transform);
        StripPhysicsFromHeld(currentVisual);
    }

    private static void ApplyItemHandTransform(InventoryItem item, Transform t)
    {
        t.localPosition = item.heldLocalPosition;
        t.localRotation = Quaternion.Euler(item.heldLocalEulerAngles);
    }

    private static void StripPhysicsFromHeld(GameObject root)
    {
        foreach (var c in root.GetComponentsInChildren<Collider>())
            c.enabled = false;
        foreach (var rb in root.GetComponentsInChildren<Rigidbody>())
        {
            rb.isKinematic = true;
            rb.useGravity = false;
        }
    }

    private void ClearVisual()
    {
        if (currentVisual != null)
        {
            Destroy(currentVisual);
            currentVisual = null;
        }
    }
}
