using UnityEngine;

/// <summary>
/// Hides gameplay HUD (crosshair, inventory slots) during cinematics / capture / hiding
/// while leaving other canvas UI (e.g. logo) visible.
/// Crosshair visibility tracks player control: on when cinematic lock is released.
/// </summary>
public class GameplayHudVisibility : MonoBehaviour
{
    public static GameplayHudVisibility Instance { get; private set; }

    [SerializeField] private GameObject interactionRoot;
    [SerializeField] private GameObject inventoryRoot;

    [SerializeField] private float fadeDuration = 0.25f;

    bool _hiddenForCinematic;
    bool _hiddenForHiding;
    CanvasGroup _interactionGroup;
    CanvasGroup _inventoryGroup;
    float _targetAlpha = 1f;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(this);
            return;
        }

        Instance = this;
        ResolveRoots();
        CacheGroups();
        SyncCinematicFromGate();
        Apply();
    }

    private void Start()
    {
        SyncCinematicFromGate();
        Apply();
    }

    private void LateUpdate()
    {
        SyncCinematicFromGate();
        Apply();
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    public void HideGameplayHud()
    {
        _hiddenForCinematic = true;
        Apply();
    }

    public void ShowGameplayHud()
    {
        _hiddenForCinematic = false;
        Apply();
    }

    /// <summary>Hide crosshair + inventory while the player is in a hiding spot.</summary>
    public void SetHiddenDuringHiding(bool hidden)
    {
        _hiddenForHiding = hidden;
        Apply();
    }

    /// <summary>
    /// Clears the hide-spot latch only. Capture / jumpscare / game-over must keep the HUD
    /// down via <see cref="HideGameplayHud"/> or gate flags — never by leaving this stuck true.
    /// </summary>
    public void ClearHidingLatch()
    {
        if (!_hiddenForHiding)
            return;

        _hiddenForHiding = false;
        Apply();
    }

    /// <summary>
    /// Game-over / post-jumpscare: drop every sticky hide reason so Continue / Restart
    /// do not inherit a permanent blank inventory/crosshair from a hide capture.
    /// Keeps HUD visually off while <see cref="JumpscareManager.CaptureUiActive"/> or
    /// <see cref="PlayerControlGate.ReasonCaptured"/> still apply; callers should
    /// re-<see cref="HideGameplayHud"/> if they need HUD off after those clear.
    /// </summary>
    public void ClearAllStickyHides()
    {
        _hiddenForHiding = false;
        _hiddenForCinematic = false;
        Apply();
    }

    bool SyncCinematicFromGate()
    {
        // Only force-hide when cinematic is locked. Never clear a manual HideGameplayHud()
        // (capture / jumpscare) just because ReasonCinematic is not held.
        if (!PlayerControlGate.IsAcquired(PlayerControlGate.ReasonCinematic))
            return false;

        if (_hiddenForCinematic)
            return false;

        _hiddenForCinematic = true;
        return true;
    }

    void Apply()
    {
        bool lockpick = PlayerControlGate.IsAcquired(PlayerControlGate.ReasonLockpick);
        bool captured = PlayerControlGate.IsAcquired(PlayerControlGate.ReasonCaptured);
        bool reading = PlayerControlGate.IsAcquired(PlayerControlGate.ReasonReading);
        bool show = !_hiddenForCinematic && !_hiddenForHiding && !lockpick && !captured && !reading
            && !JumpscareManager.CaptureUiActive;

        _targetAlpha = show ? 1f : 0f;
        float speed = fadeDuration > 0f ? Time.unscaledDeltaTime / fadeDuration : 1f;

        LerpGroup(_interactionGroup, interactionRoot, _targetAlpha, speed);
        LerpGroup(_inventoryGroup, inventoryRoot, _targetAlpha, speed);
    }

    static void LerpGroup(CanvasGroup group, GameObject root, float target, float speed)
    {
        if (target > 0f && root != null && !root.activeSelf)
            root.SetActive(true);

        if (group != null)
        {
            group.alpha = Mathf.MoveTowards(group.alpha, target, speed);
            bool interactive = group.alpha > 0.01f;
            group.interactable = interactive;
            group.blocksRaycasts = interactive;

            if (group.alpha <= 0f && root != null && root.activeSelf)
                root.SetActive(false);
            return;
        }

        if (root != null)
            root.SetActive(target > 0f);
    }

    void CacheGroups()
    {
        _interactionGroup = EnsureCanvasGroup(interactionRoot);
        _inventoryGroup = EnsureCanvasGroup(inventoryRoot);
    }

    static CanvasGroup EnsureCanvasGroup(GameObject root)
    {
        if (root == null)
            return null;

        var group = root.GetComponent<CanvasGroup>();
        if (group == null)
            group = root.AddComponent<CanvasGroup>();
        return group;
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
