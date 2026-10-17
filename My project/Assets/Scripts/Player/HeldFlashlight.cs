using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Flashlight controls on the flashlight's HeldItemView: left-click switches the beam on and off. The on/off
/// state is kept while the flashlight is put away. The beam sits on the lens socket and is steered most of the
/// way toward the camera's view so the hand sway doesn't throw it around.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(HeldItemView))]
public sealed class HeldFlashlight : MonoBehaviour
{
    [SerializeField] private Light beam;

    [Tooltip("Optional faint light on the Viewmodel rendering layer so the hand isn't black while the beam is on.")]
    [SerializeField] private Light viewmodelSpill;

    [Tooltip("Lens transform inside the view; the beam is placed on it.")]
    [SerializeField] private string socketName = "Flashlight_LightSocket";

    [SerializeField] private bool startOn = true;

    [Tooltip("0 = the beam follows the torch exactly, 1 = it points where the camera looks.")]
    [Range(0f, 1f)]
    [SerializeField] private float aimTowardView = 0.85f;

    [Tooltip("Distance ahead of the camera the beam is steered toward.")]
    [Min(0.5f)]
    [SerializeField] private float aimDistance = 8f;

    [Header("Audio")]
    [SerializeField] private AudioClip clickSfx;
    [SerializeField, Range(0f, 1f)] private float clickVolume = 0.8f;

    public bool IsOn { get; private set; }
    public Light Beam => beam;

    private HeldItemView _view;
    private Transform _socket;
    private Camera _camera;

    private void Awake()
    {
        _view = GetComponent<HeldItemView>();
        _socket = _view.FindSocket(socketName);
        _camera = GetComponentInParent<Camera>();
        if (_camera == null)
            _camera = Camera.main;
        SetOn(startOn);
    }

    private void Update()
    {
        if (_view.IsHolstering || Mouse.current == null || Cursor.lockState != CursorLockMode.Locked)
            return;
        // Left-click while G is held places the item (InventoryUI).
        if (InventoryUI.Instance != null && InventoryUI.Instance.IsDropKeyHeld)
            return;

        if (Mouse.current.leftButton.wasPressedThisFrame)
            Toggle();
    }

    private void LateUpdate()
    {
        if (beam == null || _socket == null)
            return;

        Vector3 lensForward = _socket.forward;
        Vector3 forward = lensForward;
        if (_camera != null)
        {
            Vector3 target = _camera.transform.position + _camera.transform.forward * aimDistance;
            forward = Vector3.Slerp(lensForward, (target - _socket.position).normalized, aimTowardView);
        }
        beam.transform.SetPositionAndRotation(_socket.position, Quaternion.LookRotation(forward, _socket.up));
    }

    public void Toggle()
    {
        SetOn(!IsOn);
        if (clickSfx != null && SoundManager.Instance != null)
            SoundManager.Instance.PlaySFX(clickSfx, clickVolume);
    }

    public void SetOn(bool on)
    {
        IsOn = on;
        if (beam != null)
            beam.enabled = on;
        if (viewmodelSpill != null)
            viewmodelSpill.enabled = on;
    }
}
