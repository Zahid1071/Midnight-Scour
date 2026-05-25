using UnityEngine;

/// <summary>
/// Hides gameplay HUD (crosshair, inventory slots) during cinematics while leaving other canvas UI (e.g. logo) visible.
/// </summary>
public class GameplayHudVisibility : MonoBehaviour
{
    public static GameplayHudVisibility Instance { get; private set; }

    [SerializeField] private GameObject interactionRoot;
    [SerializeField] private GameObject inventoryRoot;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(this);
            return;
        }

        Instance = this;
        ResolveRoots();
    }

    private void Start()
    {
        if (PlayerControlGate.IsAcquired(PlayerControlGate.ReasonCinematic))
            HideGameplayHud();
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    public void HideGameplayHud()
    {
        if (interactionRoot != null)
            interactionRoot.SetActive(false);
        if (inventoryRoot != null)
            inventoryRoot.SetActive(false);
    }

    public void ShowGameplayHud()
    {
        if (interactionRoot != null)
            interactionRoot.SetActive(true);
        if (inventoryRoot != null)
            inventoryRoot.SetActive(true);
    }

    private void ResolveRoots()
    {
        Transform canvas = transform.Find("Canvas");
        if (canvas == null)
            return;

        if (interactionRoot == null)
        {
            Transform interaction = canvas.Find("Interaction Root");
            if (interaction != null)
                interactionRoot = interaction.gameObject;
        }

        if (inventoryRoot == null)
        {
            Transform inventory = canvas.Find("Inventory Root");
            if (inventory != null)
                inventoryRoot = inventory.gameObject;
        }
    }
}
