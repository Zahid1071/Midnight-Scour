using UnityEngine;

[DisallowMultipleComponent]
public sealed class PlayerSpotlightWallDimmer : MonoBehaviour
{
    const int CaptiveLayer = 12;

    [Header("References")]
    [SerializeField] private Light spotlight;
    [SerializeField] private Transform castSource;

    [Header("Raycasts")]
    [Min(0.01f)]
    [SerializeField] private float facePalmDistance = 0.35f;

    [Range(0f, 25f)]
    [SerializeField] private float sideYawDegrees = 7f;

    [SerializeField] private LayerMask obstacleLayers = ~0;
    [SerializeField] private QueryTriggerInteraction triggerInteraction = QueryTriggerInteraction.Ignore;

    [Header("Ignore")]
    [SerializeField] private bool ignoreCage = true;
    [SerializeField] private bool ignoreSelf = true;

    [Header("Dimming")]
    [Min(0f)]
    [SerializeField] private float normalIntensity = 1.0f;

    [Min(0f)]
    [SerializeField] private float minIntensity = 0.25f;

    [Min(0f)]
    [SerializeField] private float cageInsideIntensity = 0.5f;

    [Min(0f)]
    [SerializeField] private float minigameIntensity = 0.25f;

    [Tooltip("Intensity units per second for cage / minigame transitions.")]
    [Min(0.05f)]
    [SerializeField] private float cageTransitionSpeed = 1.25f;

    [Header("Smoothing")]
    [Min(0.05f)]
    [SerializeField] private float dimSpeed = 10f;

    [Min(0.05f)]
    [SerializeField] private float restoreSpeed = 4f;

    [Header("Hiding")]
    [SerializeField] private bool dimWhileHidden = true;

    private float _currentIntensity;
    private bool _hasSynced;
    private Collider[] _selfColliders;
    private bool _hideLightSuppressed;
    private bool _wasInCinematic;

    public void SetHideLightSuppressed(bool suppressed) => _hideLightSuppressed = suppressed;

    public void ResetAfterCaptureContinue()
    {
        _hideLightSuppressed = false;
        enabled = true;
        ResolveReferences();
        SyncFromSpotlight();
    }

    private void Awake()
    {
        ResolveReferences();
        _selfColliders = ignoreSelf ? GetComponentsInChildren<Collider>(true) : null;
        SyncFromSpotlight();
    }

    private void OnEnable()
    {
        ResolveReferences();
        SyncFromSpotlight();
    }

    private void SyncFromSpotlight()
    {
        if (spotlight == null)
            return;
        _currentIntensity = spotlight.intensity;
        _hasSynced = true;
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
        ResolveReferences();
        if (spotlight == null)
            return;

        if (!_hasSynced)
        {
            SyncFromSpotlight();
            if (!_hasSynced)
                return;
        }

        // Timeline owns intensity during cinematics.
        if (PlayerControlGate.IsAcquired(PlayerControlGate.ReasonCinematic))
        {
            SyncFromSpotlight();
            _wasInCinematic = true;
            return;
        }

        if (_wasInCinematic)
        {
            // Pick up whatever the Timeline left — then blend to gameplay target.
            SyncFromSpotlight();
            _wasInCinematic = false;
        }

        if (castSource == null)
            return;

        float normalI = Mathf.Max(0f, normalIntensity);
        float minI = Mathf.Clamp(minIntensity, 0f, normalI);
        float cageI = Mathf.Clamp(cageInsideIntensity, 0f, normalI);
        float minigameI = Mathf.Max(0f, minigameIntensity);

        GetTarget(normalI, minI, cageI, minigameI, out float target, out float speed);

        _currentIntensity = Mathf.MoveTowards(_currentIntensity, target, Mathf.Max(0.05f, speed) * Time.deltaTime);
        spotlight.intensity = _currentIntensity;
    }

    void GetTarget(float normalI, float minI, float cageI, float minigameI, out float target, out float speed)
    {
        // Priority: hide → minigame → cage/captive → wall → normal.
        if (dimWhileHidden && _hideLightSuppressed)
        {
            target = 0f;
            speed = dimSpeed;
            return;
        }

        if (PlayerControlGate.IsAcquired(PlayerControlGate.ReasonLockpick))
        {
            target = minigameI;
            speed = cageTransitionSpeed;
            return;
        }

        if (IsInCageContext())
        {
            target = cageI;
            speed = cageTransitionSpeed;
            return;
        }

        if (IsWallFacePalm())
        {
            target = minI;
            speed = dimSpeed;
            return;
        }

        target = normalI;
        speed = restoreSpeed;
    }

    /// <summary>
    /// Occupancy can flicker for a frame after camera unlock; Captive layer stays reliable in the cage.
    /// </summary>
    bool IsInCageContext()
    {
        if (Cage.IsPlayerInsideAnyCage(transform))
            return true;

        // Player root (or this object) on Captive while door is closed.
        Transform root = transform.root != null ? transform.root : transform;
        return root.gameObject.layer == CaptiveLayer;
    }

    private bool IsWallFacePalm()
    {
        float dist = Mathf.Max(0.01f, facePalmDistance);
        Vector3 fwd = castSource.forward;
        Vector3 up = castSource.up;

        return RayBlocked(fwd, dist)
            || RayBlocked(Quaternion.AngleAxis(sideYawDegrees, up) * fwd, dist)
            || RayBlocked(Quaternion.AngleAxis(-sideYawDegrees, up) * fwd, dist);
    }

    private bool RayBlocked(Vector3 direction, float maxDistance)
    {
        RaycastHit[] hits = Physics.RaycastAll(
            castSource.position,
            direction.normalized,
            maxDistance,
            obstacleLayers,
            triggerInteraction);

        if (hits == null || hits.Length == 0)
            return false;

        for (int i = 0; i < hits.Length; i++)
        {
            Collider c = hits[i].collider;
            if (c == null)
                continue;
            if (ignoreSelf && IsSelfCollider(c))
                continue;
            if (ignoreCage && c.GetComponentInParent<Cage>() != null)
                continue;
            return true;
        }

        return false;
    }

    private bool IsSelfCollider(Collider c)
    {
        if (_selfColliders == null)
            return false;
        for (int i = 0; i < _selfColliders.Length; i++)
        {
            if (_selfColliders[i] == c)
                return true;
        }
        return false;
    }
}
