using System.Collections.Generic;
using StarterAssets;
using UnityEngine;

/// <summary>
/// Lives under the gameplay camera. Shows the HeldItemView (placed in the scene under this object)
/// of the selected inventory item and draws it on equip. Sprinting puts the item away; it is drawn
/// again once the player stops running.
/// </summary>
[DisallowMultipleComponent]
public sealed class HeldItemController : MonoBehaviour
{
    public static HeldItemController Instance { get; private set; }

    [Header("Sprint holster")]
    [SerializeField] private bool holsterWhileSprinting = true;

    [Tooltip("Horizontal speed (m/s) a sprint has to reach before the item is put away. Keep in line with the " +
             "footsteps' run threshold so it goes away as the running steps start.")]
    [Min(0f)]
    [SerializeField] private float sprintHolsterSpeed = 3f;

    [Tooltip("Seconds after the sprint ends before the item is drawn again, so short gaps don't flicker it.")]
    [Min(0f)]
    [SerializeField] private float sprintDrawDelay = 0.25f;

    private readonly Dictionary<InventoryItem, HeldItemView> _views = new();
    private InventoryItem _current;
    private HeldItemView _currentView;
    private bool _shown;
    private PlayerHider _hider;
    private bool _hiderLookedUp;
    private FirstPersonController _player;
    private StarterAssetsInputs _input;
    private CharacterController _body;
    private bool _playerLookedUp;
    private bool _sprintHolstered;
    private float _sprintEndedTime = -1f;
    private HeldLighter _relightAfterSprint;

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
        bool sprinting = UpdateSprintHolster();
        bool canShow = !hiding && !sprinting && CanShowHeldItem();
        bool animatedPutDown = canShow || hiding || sprinting;
        if (hiding || !CanShowHeldItem())
            _relightAfterSprint = null;

        if (_currentView != null && _currentView.IsHolstering)
        {
            if (animatedPutDown)
                return;
            _currentView.HolsterImmediate();
        }

        bool viewShown = _currentView != null && _currentView.gameObject.activeSelf;
        if (selected == _current && canShow == _shown && (_currentView == null || viewShown))
            return;

        if (viewShown)
        {
            // Switching items, starting to hide or sprinting: the put-down plays first.
            if (animatedPutDown)
            {
                // A lighter put away burning for a sprint lights itself again when it comes back up.
                if (sprinting && !hiding && _currentView.TryGetComponent(out HeldLighter lighter) && lighter.IsLit)
                    _relightAfterSprint = lighter;
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
        {
            _currentView.Draw();
            if (_relightAfterSprint != null && _currentView.gameObject == _relightAfterSprint.gameObject)
                _relightAfterSprint.StrikeAfterDraw();
            _relightAfterSprint = null;
        }
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

    // Holsters once a sprint is actually up to running speed (Shift against a wall or sprinting backwards at
    // walking pace doesn't count), and stays holstered while Shift and a move key are held.
    private bool UpdateSprintHolster()
    {
        if (!holsterWhileSprinting || !FindPlayer())
            return _sprintHolstered = false;

        bool sprintHeld = _input.sprint && _input.move != Vector2.zero && !_player.IsSneaking
                          && !PlayerControlGate.IsInputBlocked;
        if (!_sprintHolstered)
        {
            Vector3 v = _body.velocity;
            _sprintHolstered = sprintHeld && new Vector2(v.x, v.z).magnitude >= sprintHolsterSpeed;
            _sprintEndedTime = -1f;
            return _sprintHolstered;
        }

        if (sprintHeld)
            _sprintEndedTime = -1f;
        else if (_sprintEndedTime < 0f)
            _sprintEndedTime = Time.time;
        else if (Time.time - _sprintEndedTime >= sprintDrawDelay)
            _sprintHolstered = false;
        return _sprintHolstered;
    }

    private bool FindPlayer()
    {
        if (!_playerLookedUp)
        {
            _player = FindFirstObjectByType<FirstPersonController>();
            if (_player != null)
            {
                _input = _player.GetComponent<StarterAssetsInputs>();
                _body = _player.GetComponent<CharacterController>();
            }
            _playerLookedUp = true;
        }
        return _player != null && _input != null && _body != null;
    }

    public bool TryGetView(InventoryItem item, out HeldItemView view)
    {
        view = GetView(item);
        return view != null;
    }

    private HeldItemView GetView(InventoryItem item) =>
        item != null && _views.TryGetValue(item, out HeldItemView view) ? view : null;
}
