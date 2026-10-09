using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Lives under the gameplay camera. Shows the HeldItemView (placed in the scene under this object)
/// of the selected inventory item and draws it on equip.
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

        foreach (var view in GetComponentsInChildren<HeldItemView>(true))
        {
            if (view.Item == null)
            {
                Debug.LogWarning($"[HeldItemController] {view.name} has no item assigned.", view);
                continue;
            }
            _views[view.Item] = view;
            view.HolsterImmediate();
        }
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    private void LateUpdate()
    {
        InventoryItem selected = InventoryUI.Instance != null ? InventoryUI.Instance.GetSelectedItem() : null;
        bool hiding = IsHiding();
        bool canShow = !hiding && CanShowHeldItem();

        if (_currentView != null && _currentView.IsHolstering)
        {
            if (canShow || hiding)
                return;
            _currentView.HolsterImmediate();
        }

        bool viewShown = _currentView != null && _currentView.gameObject.activeSelf;
        if (selected == _current && canShow == _shown && (_currentView == null || viewShown))
            return;

        if (viewShown)
        {
            // Switching items or starting to hide: the put-down plays first.
            if (canShow || hiding)
            {
                _currentView.Holster();
                if (_currentView.IsHolstering)
                    return;
            }
            else
                _currentView.HolsterImmediate();
        }

        _current = selected;
        _shown = canShow;
        _currentView = canShow ? GetView(selected) : null;
        if (_currentView != null)
            _currentView.Draw();
    }

    private static bool CanShowHeldItem() =>
        !PlayerControlGate.IsAcquired(PlayerControlGate.ReasonCinematic)
        && !PlayerControlGate.IsAcquired(PlayerControlGate.ReasonCaptured);

    private bool IsHiding()
    {
        if (!_hiderLookedUp)
        {
            _hider = FindFirstObjectByType<PlayerHider>();
            _hiderLookedUp = true;
        }
        return _hider != null && (_hider.isHidden || _hider.IsHidingEngaged);
    }

    public bool TryGetView(InventoryItem item, out HeldItemView view)
    {
        view = GetView(item);
        return view != null;
    }

    private HeldItemView GetView(InventoryItem item) =>
        item != null && _views.TryGetValue(item, out HeldItemView view) ? view : null;
}
