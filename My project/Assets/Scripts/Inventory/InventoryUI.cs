using FS_Atmo;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;
using System.Collections.Generic;

public class InventoryUI : MonoBehaviour
{
    public static InventoryUI Instance { get; private set; }

    [SerializeField] private Image[] slotImages;
    [SerializeField] private Image[] slotBackgrounds;
    [SerializeField] private Image[] slotOutlines;

    [Header("Drop throw")]
    [SerializeField] private float dropSpawnForward = 0.55f;
    [SerializeField] private float dropSpawnDown = 0.25f;
    [SerializeField] private float dropThrowForce = 2.2f;
    [SerializeField] private float dropThrowUpward = 0.6f;
    [SerializeField] private float dropItemMass = 1f;

    [Header("Drop place (hold G)")]
    [Tooltip("Holding G this long shows the placement preview; releasing then sets the item down there instead of throwing it.")]
    [Min(0.05f)]
    [SerializeField] private float holdToPlaceSeconds = 0.3f;
    [Tooltip("Material for the ghost of the item shown where it will be placed.")]
    [SerializeField] private Material placePreviewMaterial;
    [Tooltip("Length of the aim ray. Placement itself is limited to the player's interact reach (from the camera or the feet), so the item can be picked up again from the same spot.")]
    [SerializeField] private float placeRayLength = 3f;
    [Tooltip("Kept inside the interact reach by this much.")]
    [SerializeField] private float placeReachMargin = 0.1f;
    [Tooltip("When aiming at a wall, the item is set on the floor at most this far in front of the player.")]
    [SerializeField] private float placeFloorAhead = 0.5f;
    [Tooltip("Minimum surface normal Y to count as something the item can rest on.")]
    [Range(0f, 1f)]
    [SerializeField] private float placeMinSurfaceUp = 0.6f;
    [SerializeField] private LayerMask placeSurfaceMask = Physics.DefaultRaycastLayers;

    private int selectedIndex = -1;
    private float _dropHeldTime = -1f;
    private InventoryItem _dropHeldItem;
    private int _dropClickFrame = -1;
    private PlacementPreview _placePreview;
    TextMeshProUGUI[] _stackLabels;

    private void Awake()
    {
        Instance = this;
        EnsureStackLabels();
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
        _placePreview?.Dispose();
    }

    private void Update()
    {
        if (PlayerControlGate.IsInputBlocked
            || ReadPanelUI.IsBlockingPlayerInput
            || JumpscareManager.CaptureUiActive
            || (GameOverUI.Instance != null && GameOverUI.Instance.IsShowing))
        {
            CancelDropHold();
            return;
        }

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

        bool clickPlacedItem = TickDropKey();

        if (!clickPlacedItem && WasUseItemPressedThisFrame())
            HandleInteractPress(isPrimaryClick: true);

        if (WasUseItemOnInteractKeyThisFrame())
            HandleInteractPress(isPrimaryClick: false);
    }

    /// <summary>True while G is held for a drop (and on the frame a left-click placed from it); left-click then places the item instead of using it.</summary>
    public bool IsDropKeyHeld => _dropHeldTime >= 0f || _dropClickFrame == Time.frameCount;

    /// <summary>
    /// Tap G throws the selected item on release. Holding G shows a ghost of it on the aimed surface;
    /// releasing (or left-clicking while G is held) sets it down there. Nothing happens if no valid surface is aimed at.
    /// Returns true when this frame's left-click was used to place the item.
    /// </summary>
    bool TickDropKey()
    {
        if (Input.GetKeyDown(KeyCode.G))
        {
            _dropHeldTime = 0f;
            _dropHeldItem = GetSelectedItem();
        }
        if (_dropHeldTime < 0f)
            return false;

        if (GetSelectedItem() != _dropHeldItem)
        {
            CancelDropHold();
            return false;
        }

        if (WasUseItemPressedThisFrame())
        {
            _dropClickFrame = Time.frameCount;
            UpdatePlacePreview();
            PlaceAtPreviewAndEndHold();
            return true;
        }

        if (!Input.GetKey(KeyCode.G))
        {
            if (_dropHeldTime < holdToPlaceSeconds)
            {
                CancelDropHold();
                TryDropSelectedItem(place: false);
            }
            else
                PlaceAtPreviewAndEndHold();
            return false;
        }

        _dropHeldTime += Time.unscaledDeltaTime;
        if (_dropHeldTime >= holdToPlaceSeconds)
            UpdatePlacePreview();
        return false;
    }

    /// <summary>Places the item where the preview shows (before the hold reset hides it), or warns if it can't be dropped.</summary>
    void PlaceAtPreviewAndEndHold()
    {
        if (_placePreview != null && _placePreview.IsVisible)
            TryDropSelectedItem(place: true);
        else
            TryGetDroppableSelected(warn: true, out _, out _);
        CancelDropHold();
    }

    void CancelDropHold()
    {
        _dropHeldTime = -1f;
        _dropHeldItem = null;
        _placePreview?.Hide();
    }

    void UpdatePlacePreview()
    {
        Camera cam = Camera.main;
        if (cam == null
            || !TryGetDroppableSelected(warn: false, out _, out GameObject dropPrefab)
            || !TryGetPlacePoint(cam.transform, out Vector3 point, out Vector3 normal))
        {
            _placePreview?.Hide();
            return;
        }

        _placePreview ??= new PlacementPreview(placePreviewMaterial);
        _placePreview.Show(dropPrefab, point, normal, GetDropSpawnRotation(cam.transform).eulerAngles.y);
    }

    public void RefreshUI()
    {
        if (!this || InventoryManager.Instance == null) return;
        if (slotImages == null || slotBackgrounds == null) return;

        EnsureStackLabels();
        var items = InventoryManager.Instance.GetInventory();

        for (int i = 0; i < slotImages.Length; i++)
        {
            if (slotImages[i] == null)
                continue;
            if (i >= slotBackgrounds.Length)
                break;

            InventoryItem item = i < items.Count ? items[i] : null;
            bool hasItem = item != null;
            bool hasIcon = hasItem && item.icon != null;

            slotImages[i].sprite = hasIcon ? item.icon : null;
            // Keep Image enabled when stacked with no sprite yet so the slot still selects.
            slotImages[i].enabled = hasItem;
            if (hasItem && !hasIcon)
                slotImages[i].color = new Color(1f, 1f, 1f, 0.15f);
            else
                slotImages[i].color = Color.white;

            if (slotOutlines != null && i < slotOutlines.Length && slotOutlines[i] != null)
                slotOutlines[i].enabled = (i == selectedIndex);

            int count = hasItem ? InventoryManager.Instance.GetCountAt(i) : 0;
            if (_stackLabels != null && i < _stackLabels.Length && _stackLabels[i] != null)
            {
                bool showCount = count > 1;
                _stackLabels[i].enabled = showCount;
                if (showCount)
                    _stackLabels[i].text = count.ToString();
            }
        }
    }

    void EnsureStackLabels()
    {
        if (slotImages == null || slotImages.Length == 0)
            return;
        if (_stackLabels != null && _stackLabels.Length == slotImages.Length)
            return;

        _stackLabels = new TextMeshProUGUI[slotImages.Length];
        for (int i = 0; i < slotImages.Length; i++)
        {
            if (slotImages[i] == null)
                continue;

            var existing = slotImages[i].GetComponentInChildren<TextMeshProUGUI>(true);
            if (existing != null && existing.name == "Stack Count")
            {
                ApplyCubicFont(existing);
                _stackLabels[i] = existing;
                continue;
            }

            var go = new GameObject("Stack Count", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
            go.transform.SetParent(slotImages[i].transform, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(1f, 0f);
            rt.anchorMax = new Vector2(1f, 0f);
            rt.pivot = new Vector2(1f, 0f);
            rt.anchoredPosition = new Vector2(-10f, 4f);
            rt.sizeDelta = new Vector2(48f, 28f);

            var tmp = go.GetComponent<TextMeshProUGUI>();
            tmp.text = "";
            tmp.fontSize = 22f;
            tmp.fontStyle = FontStyles.Bold;
            tmp.alignment = TextAlignmentOptions.BottomRight;
            tmp.color = Color.white;
            tmp.raycastTarget = false;
            tmp.enabled = false;

            ApplyCubicFont(tmp);
            _stackLabels[i] = tmp;
        }
    }


    static void ApplyCubicFont(TextMeshProUGUI tmp)
    {
        var cubicFont = UIFont.ChineseOrNull;
        if (cubicFont == null || tmp == null) return;
        tmp.font = cubicFont;
        if (cubicFont.material != null)
            tmp.fontSharedMaterial = cubicFont.material;
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
        // Exit hide is handled by PlayerHider (E/Space) — no world/item use while hidden.
        var hider = PlayerInteractor.Instance != null
            ? PlayerInteractor.Instance.GetComponent<PlayerHider>()
            : null;
        if (hider != null && hider.isHidden)
            return;

        InventoryItem selected = GetSelectedItem();
        if (selected != null && selected.hasPrimaryAction)
        {
            // LMB belongs to the item itself (e.g. HeldLighter); E interacts with the world.
            if (!isPrimaryClick)
                PlayerInteractor.Instance?.TryInteract();
        }
        else if (selected != null)
            TryUseSelectedItem(isPrimaryClick);
        else
            PlayerInteractor.Instance?.TryInteract();
    }

    public int SelectedIndex => selectedIndex;

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
        if (InventoryManager.Instance == null)
            return null;

        var inventory = InventoryManager.Instance.GetInventory();
        if (inventory == null)
            return null;

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
                else if (receiver is SimpleOpenClose
                    || receiver is LockedOpenClose
                    || receiver is PadlockLockpickMinigame)
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

    bool TryGetDroppableSelected(bool warn, out InventoryItem selected, out GameObject dropPrefab)
    {
        selected = null;
        dropPrefab = null;

        var hider = PlayerInteractor.Instance != null
            ? PlayerInteractor.Instance.GetComponent<PlayerHider>()
            : null;
        if (hider != null && hider.isHidden)
            return false;

        if (InventoryManager.Instance == null)
            return false;

        selected = GetSelectedItem();
        if (selected == null)
            return false;

        if (!selected.canDrop)
        {
            if (warn)
                ShowWarning("You cannot drop that item.", 2.5f);
            return false;
        }

        dropPrefab = selected.GetDropPrefab();
        if (dropPrefab == null)
        {
            if (warn)
                ShowWarning("This item has no drop prefab assigned.", 2.5f);
            return false;
        }

        return true;
    }

    /// <summary>Throws the selected item, or with <paramref name="place"/> sets it down at the shown placement preview.</summary>
    private void TryDropSelectedItem(bool place)
    {
        if (!TryGetDroppableSelected(warn: true, out InventoryItem selected, out GameObject dropPrefab))
            return;

        if (Camera.main == null)
            return;

        Transform cam = Camera.main.transform;
        int dropIndex = selectedIndex;

        if (place && (_placePreview == null || !_placePreview.IsVisible))
            return;
        Vector3 placePosition = place ? _placePreview.Position : default;
        Quaternion placeRotation = place ? _placePreview.Rotation : default;
        _placePreview?.Hide();

        // Read before removal: the held view puts the flame out when it is hidden.
        HeldItemView heldView = HeldItemController.Instance != null ? HeldItemController.Instance.CurrentView : null;
        if (heldView != null && heldView.Item != selected)
            heldView = null;
        bool heldLit = heldView != null && heldView.TryGetComponent(out HeldLighter heldLighter) && heldLighter.IsLit;

        if (!InventoryManager.Instance.RemoveItemAt(dropIndex, refreshUI: false))
            return;

        AdjustSelectionAfterRemove();
        RefreshUI();

        GameObject dropped;
        if (place)
        {
            // The item appears on the surface right away, so skip the put-down animation of the held view.
            if (heldView != null)
                heldView.HolsterImmediate();

            dropped = ItemDropHelper.SpawnPlaced(dropPrefab, placePosition, placeRotation, dropItemMass, selected.dropLandSFX);
        }
        else
        {
            dropped = ItemDropHelper.SpawnAndThrow(
                dropPrefab,
                GetDropThrowSpawnPosition(cam),
                GetDropSpawnRotation(cam),
                cam.forward,
                dropThrowForce,
                dropThrowUpward,
                dropItemMass,
                selected.dropLandSFX);
        }

        if (dropped.TryGetComponent(out WorldLighter worldLighter))
            worldLighter.SetLit(place && heldLit);

        if (selected.pickUpSFX != null && SoundManager.Instance != null)
            SoundManager.Instance.PlaySFXAt(selected.pickUpSFX, dropped.transform.position);

        if (!place)
        {
            string msg = Loc.Format("gameplay.item_dropped", selected.GetLocalizedName());
            HudManager.Instance?.ShowSubtitle(msg, 2f);
        }
    }

    /// <summary>
    /// Surface point and normal to set the item down on: the aimed surface if it faces up enough and is within
    /// reach. Aiming at a wall in reach uses the floor just in front of it. Nothing otherwise.
    /// </summary>
    bool TryGetPlacePoint(Transform cam, out Vector3 point, out Vector3 normal)
    {
        point = default;
        normal = Vector3.up;

        var interactor = PlayerInteractor.Instance;
        float reach = (interactor != null ? interactor.GetWorldInteractRange() : 1.5f) - placeReachMargin;

        Transform player = interactor != null ? interactor.transform : null;
        if (!RaycastPlaceSurface(cam.position, cam.forward, placeRayLength, player, out RaycastHit hit)
            || !IsWithinPlaceReach(interactor, cam, hit.point, reach))
            return false;

        if (hit.normal.y >= placeMinSurfaceUp)
        {
            point = hit.point;
            normal = hit.normal;
            return true;
        }

        Vector3 toWall = hit.point - cam.position;
        toWall.y = 0f;
        if (toWall.sqrMagnitude < 0.0001f)
            return false;

        float ahead = Mathf.Max(0f, Mathf.Min(placeFloorAhead, toWall.magnitude - 0.15f));
        Vector3 origin = cam.position + toWall.normalized * ahead;
        if (!RaycastPlaceSurface(origin, Vector3.down, 3f, player, out hit)
            || hit.normal.y < placeMinSurfaceUp
            || !IsWithinPlaceReach(interactor, cam, hit.point, reach))
            return false;

        point = hit.point;
        normal = hit.normal;
        return true;
    }

    readonly RaycastHit[] _placeHits = new RaycastHit[16];

    /// <summary>Nearest hit that is not part of the player.</summary>
    bool RaycastPlaceSurface(Vector3 origin, Vector3 direction, float length, Transform player, out RaycastHit nearest)
    {
        nearest = default;
        int count = Physics.RaycastNonAlloc(origin, direction, _placeHits, length, placeSurfaceMask, QueryTriggerInteraction.Ignore);
        bool found = false;
        for (int i = 0; i < count; i++)
        {
            RaycastHit hit = _placeHits[i];
            if (player != null && hit.collider.transform.IsChildOf(player))
                continue;
            if (!found || hit.distance < nearest.distance)
            {
                nearest = hit;
                found = true;
            }
        }
        return found;
    }

    static bool IsWithinPlaceReach(PlayerInteractor interactor, Transform cam, Vector3 point, float reach)
    {
        if (Vector3.Distance(cam.position, point) <= reach)
            return true;
        return interactor != null && Vector3.Distance(interactor.GetFeetPosition(), point) <= reach;
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
