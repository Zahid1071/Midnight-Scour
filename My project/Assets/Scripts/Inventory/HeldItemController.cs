using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Lives under the gameplay camera. Shows the HeldItemView of the selected inventory item
/// (InventoryItem.heldViewPrefab) and draws it on equip. Views are spawned once and reused.
/// </summary>
[DisallowMultipleComponent]
public sealed class HeldItemController : MonoBehaviour
{
    public static HeldItemController Instance { get; private set; }

    private readonly Dictionary<InventoryItem, HeldItemView> _views = new();
    private InventoryItem _current;
    private HeldItemView _currentView;
    private bool _shown;
    private PlayerHider _hider;
    private bool _hiderLookedUp;

    public InventoryItem CurrentItem => _shown ? _current : null;
    public HeldItemView CurrentView => _currentView;

    private void Awake()
    {
        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    private void LateUpdate()
    {
        InventoryItem selected = InventoryUI.Instance != null ? InventoryUI.Instance.GetSelectedItem() : null;
        bool canShow = CanShowHeldItem();
        if (selected == _current && canShow == _shown)
            return;

        if (_currentView != null)
            _currentView.Holster();

        _current = selected;
        _shown = canShow;
        _currentView = canShow ? GetView(selected) : null;
        if (_currentView != null)
            _currentView.Draw();
    }

    private bool CanShowHeldItem()
    {
        if (PlayerControlGate.IsAcquired(PlayerControlGate.ReasonCinematic)
            || PlayerControlGate.IsAcquired(PlayerControlGate.ReasonCaptured))
            return false;

        if (!_hiderLookedUp)
        {
            _hider = FindFirstObjectByType<PlayerHider>();
            _hiderLookedUp = true;
        }
        return _hider == null || !_hider.isHidden;
    }

    private HeldItemView GetView(InventoryItem item)
    {
        if (item == null || item.heldViewPrefab == null)
            return null;

        if (_views.TryGetValue(item, out HeldItemView view) && view != null)
            return view;

        GameObject go = Instantiate(item.heldViewPrefab, transform, false);
        go.name = item.heldViewPrefab.name;
        view = go.GetComponent<HeldItemView>();
        if (view == null)
            view = go.AddComponent<HeldItemView>();
        view.Holster();
        _views[item] = view;
        return view;
    }
}
