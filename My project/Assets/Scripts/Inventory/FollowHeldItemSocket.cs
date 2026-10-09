using UnityEngine;

/// <summary>
/// Keeps this object (e.g. a flame light rig) on a socket of the currently held item view,
/// upright in world space. Its children are switched off while no view with that socket is held
/// (and, optionally, while that view is still drawing or, if it has a HeldLighter, until it is lit).
/// </summary>
[DisallowMultipleComponent]
public sealed class FollowHeldItemSocket : MonoBehaviour
{
    [Tooltip("Name of the transform inside the held view to follow (exported socket bone).")]
    [SerializeField] private string socketName = "Lighter_FlameSocket";

    [Tooltip("World-space offset from the socket.")]
    [SerializeField] private Vector3 worldOffset = Vector3.zero;

    [SerializeField] private bool hideChildrenWhenNotHeld = true;

    [Tooltip("Also keep the children off while the view plays its draw animation.")]
    [SerializeField] private bool hideWhileDrawing = true;

    private HeldItemView _view;
    private Transform _socket;
    private HeldLighter _lighter;
    private bool? _childrenShown;

    private void LateUpdate()
    {
        HeldItemView view = HeldItemController.Instance != null ? HeldItemController.Instance.CurrentView : null;
        if (view != _view)
        {
            _view = view;
            _socket = view != null ? view.FindSocket(socketName) : null;
            _lighter = view != null ? view.GetComponent<HeldLighter>() : null;
        }

        bool held = _socket != null && _socket.gameObject.activeInHierarchy;
        if (held)
            transform.SetPositionAndRotation(_socket.position + worldOffset, Quaternion.identity);

        // A lighter drawn already lit (picked up burning) shows its flame during the draw too.
        bool show = held
                    && (_lighter != null ? _lighter.IsLit : !(hideWhileDrawing && _view.IsDrawing));
        if (hideChildrenWhenNotHeld && _childrenShown != show)
        {
            _childrenShown = show;
            foreach (Transform child in transform)
                child.gameObject.SetActive(show);
        }
    }
}
