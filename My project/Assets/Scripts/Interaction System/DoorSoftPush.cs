using UnityEngine;

/// <summary>
/// On an animated door leaf (no Rigidbody). A leaf swinging into the CharacterController makes its overlap
/// recovery fling the player; instead the leaf nudges them out of its way at a capped speed.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Collider))]
public class DoorSoftPush : MonoBehaviour
{
    [Tooltip("Gap kept between a swinging leaf and the player's capsule.")]
    [SerializeField] float margin = 0.05f;
    [Tooltip("Fastest the leaf shoves the player (m/s); above sprint speed so a swinging leaf can't be walked through.")]
    [SerializeField] float maxPushSpeed = 5f;

    Collider _leaf;
    Transform _player;
    CharacterController _cc;
    Collider[] _playerColliders;
    Vector3 _lastPosition;
    Quaternion _lastRotation;
    bool _ignoring;

    void Awake()
    {
        _leaf = GetComponent<Collider>();
        _lastPosition = transform.position;
        _lastRotation = transform.rotation;
    }

    void OnDisable() => SetIgnore(false);

    // After the Animator has posed the leaf for this frame.
    void LateUpdate()
    {
        if (_cc == null && !FindPlayer())
            return;

        bool moving = Quaternion.Angle(transform.rotation, _lastRotation) > 0.01f
                      || (transform.position - _lastPosition).sqrMagnitude > 1e-8f;
        _lastPosition = transform.position;
        _lastRotation = transform.rotation;

        // Collisions stay on, so walking into the leaf just stops the player. Only when the leaf has swung into
        // them is it switched off, for the push and for as long as they're still inside it, so the controller's
        // next move doesn't resolve the overlap in one violent step.
        float overlap = Overlap(moving ? margin : 0f, out Vector3 away);
        if (overlap <= 0f || (!moving && !_ignoring) || !_cc.enabled || !_cc.gameObject.activeInHierarchy)
        {
            SetIgnore(false);
            return;
        }

        SetIgnore(true);
        _cc.Move(away * Mathf.Min(overlap, maxPushSpeed * Time.deltaTime));
        SetIgnore(Overlap(0f, out _) > 0.001f);
    }

    bool FindPlayer()
    {
        GameObject p = GameObject.FindWithTag("Player");
        if (p == null)
            return false;
        _cc = p.GetComponent<CharacterController>();
        if (_cc == null)
            return false;
        _player = p.transform;
        _playerColliders = p.GetComponentsInChildren<Collider>(true);
        return true;
    }

    // How far (flat) the leaf is inside the player's capsule grown by pad, and the way out.
    float Overlap(float pad, out Vector3 away)
    {
        away = Vector3.zero;
        Vector3 center = _player.TransformPoint(_cc.center);
        float radius = _cc.radius + pad;
        float half = Mathf.Max(0f, _cc.height * 0.5f - _cc.radius);
        Bounds b = _leaf.bounds;
        float lo = Mathf.Max(center.y - half, b.min.y);
        float hi = Mathf.Min(center.y + half, b.max.y);
        if (lo > hi)
            return 0f;

        Vector3 q = new Vector3(center.x, Mathf.Clamp(center.y, lo, hi), center.z);
        Vector3 p = _leaf.ClosestPoint(q);
        Vector3 d = q - p;
        d.y = 0f;
        float distance = d.magnitude;
        if (distance > 1e-4f)
        {
            away = d / distance;
            return radius - distance;
        }

        // Centre inside the leaf: out through the nearer face of its thin side.
        Vector3 normal = ThinAxis();
        away = Vector3.Dot(q - b.center, normal) >= 0f ? normal : -normal;
        return radius;
    }

    Vector3 ThinAxis()
    {
        Vector3 axis = transform.forward;
        if (_leaf is BoxCollider box)
        {
            Vector3 s = Vector3.Scale(box.size, transform.lossyScale);
            s = new Vector3(Mathf.Abs(s.x), Mathf.Abs(s.y), Mathf.Abs(s.z));
            axis = s.x <= s.z ? transform.right : transform.forward;
        }
        axis.y = 0f;
        return axis.sqrMagnitude > 1e-6f ? axis.normalized : Vector3.forward;
    }

    void SetIgnore(bool ignore)
    {
        if (_ignoring == ignore || _playerColliders == null)
            return;
        _ignoring = ignore;
        foreach (Collider c in _playerColliders)
            if (c != null)
                Physics.IgnoreCollision(_leaf, c, ignore);
    }
}
