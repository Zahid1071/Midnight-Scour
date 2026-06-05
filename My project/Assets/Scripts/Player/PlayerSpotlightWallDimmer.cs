using UnityEngine;

[DisallowMultipleComponent]
public sealed class PlayerSpotlightWallDimmer : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Light spotlight;
    [SerializeField] private Transform castSource;

    [Header("Raycasts")]
    [Tooltip("Max distance to consider the player 'face-palming' a surface.")]
    [Min(0.01f)]
    [SerializeField] private float facePalmDistance = 0.35f;

    [Tooltip("Yaw angle for the left/right rays.")]
    [Range(0f, 25f)]
    [SerializeField] private float sideYawDegrees = 7f;

    [Tooltip("Layers that count as walls/obstacles for dimming.")]
    [SerializeField] private LayerMask obstacleLayers = ~0;

    [SerializeField] private QueryTriggerInteraction triggerInteraction = QueryTriggerInteraction.Ignore;

    [Header("Ignore")]
    [Tooltip("Ignore hits on objects that have a Cage component in their parents.")]
    [SerializeField] private bool ignoreCage = true;

    [Tooltip("Ignore hits on colliders that belong to this player (self-hit protection).")]
    [SerializeField] private bool ignoreSelf = true;

    [Header("Dimming")]
    [Tooltip("Normal intensity (set this in the inspector/code). This script does not read the Light's initial intensity.")]
    [Min(0f)]
    [SerializeField] private float normalIntensity = 1.0f;

    [Tooltip("Minimum intensity when face-palming a wall.")]
    [Min(0f)]
    [SerializeField] private float minIntensity = 0.25f;

    [Tooltip("Small deadzone to prevent micro-adjustments.")]
    [Min(0f)]
    [SerializeField] private float intensityDeadzone = 0.02f;

    [Header("Smoothing")]
    [Tooltip("How quickly the light dims when blocked (higher = faster).")]
    [Min(0f)]
    [SerializeField] private float dimSpeed = 18f;

    [Tooltip("How quickly the light restores when unblocked (lower = smoother).")]
    [Min(0f)]
    [SerializeField] private float restoreSpeed = 6f;

    [Header("Hiding")]
    [Tooltip("If true, force the light to 0 while the player is hidden in a hide spot.")]
    [SerializeField] private bool dimWhileHidden = true;

    private float _currentIntensity;
    private Collider[] _selfColliders;
    private PlayerHider _playerHider;

    private void Awake()
    {
        ResolveReferences();
        _selfColliders = ignoreSelf ? GetComponentsInChildren<Collider>(true) : null;
        TryGetComponent(out _playerHider);
    }

    private void OnEnable()
    {
        ResolveReferences();
        if (spotlight != null)
            _currentIntensity = spotlight.intensity;
    }

    private void ResolveReferences()
    {
        if (spotlight == null)
        {
            var found = GetComponentInChildren<Light>(true);
            if (found != null && found.type == LightType.Spot)
                spotlight = found;
        }

        if (castSource == null)
        {
            var cam = Camera.main;
            if (cam != null)
                castSource = cam.transform;
        }
    }

    private void Update()
    {
        if (spotlight == null || castSource == null)
            return;

        float normalI = Mathf.Max(0f, normalIntensity);
        float minI = Mathf.Clamp(minIntensity, 0f, normalI);

        bool isHidden = dimWhileHidden && _playerHider != null && _playerHider.isHidden;
        bool shouldDim = !isHidden && IsWallFacePalm();

        float target = isHidden ? 0f : (shouldDim ? minI : normalI);
        float speed = isHidden ? dimSpeed : (shouldDim ? dimSpeed : restoreSpeed);

        if (speed <= 0f)
        {
            _currentIntensity = target;
        }
        else
        {
            float t = 1f - Mathf.Exp(-speed * Time.deltaTime);
            _currentIntensity = Mathf.Lerp(_currentIntensity, target, t);
        }

        if (Mathf.Abs(spotlight.intensity - _currentIntensity) > intensityDeadzone)
            spotlight.intensity = _currentIntensity;
    }

    private bool IsWallFacePalm()
    {
        float dist = Mathf.Max(0.01f, facePalmDistance);
        Vector3 fwd = castSource.forward;
        Vector3 up = castSource.up;

        // Forward, slight right, slight left
        Vector3 d0 = fwd;
        Vector3 d1 = Quaternion.AngleAxis(sideYawDegrees, up) * fwd;
        Vector3 d2 = Quaternion.AngleAxis(-sideYawDegrees, up) * fwd;

        return RayBlocked(d0, dist) || RayBlocked(d1, dist) || RayBlocked(d2, dist);
    }

    private bool RayBlocked(Vector3 direction, float maxDistance)
    {
        Vector3 origin = castSource.position;

        // Use RaycastAll so we can ignore cage/self and still find the next hit.
        RaycastHit[] hits = Physics.RaycastAll(origin, direction.normalized, maxDistance, obstacleLayers, triggerInteraction);
        if (hits == null || hits.Length == 0)
            return false;

        // Find nearest acceptable hit.
        float best = float.PositiveInfinity;
        for (int i = 0; i < hits.Length; i++)
        {
            Collider c = hits[i].collider;
            if (c == null) continue;

            if (ignoreSelf && IsSelfCollider(c))
                continue;

            if (ignoreCage && c.GetComponentInParent<Cage>() != null)
                continue;

            float d = hits[i].distance;
            if (d < best)
                best = d;
        }

        return float.IsFinite(best);
    }

    private bool IsSelfCollider(Collider c)
    {
        if (_selfColliders == null) return false;
        for (int i = 0; i < _selfColliders.Length; i++)
        {
            if (_selfColliders[i] == c)
                return true;
        }
        return false;
    }
}

