using UnityEngine;

/// <summary>
/// Keeps the held item view out of walls. Sphere-casts from the camera to points on the hand and item
/// and, when geometry is in the way, pulls the hand in toward the body with a two-bone IK on the arm
/// (the elbow bends, the grip is kept). Lights that follow sockets on the hand move with it, so they
/// stay on the player's side of the wall.
/// </summary>
[DefaultExecutionOrder(-40)] // after ViewmodelSway (-50), before FollowHeldItemSocket (0)
[DisallowMultipleComponent]
public sealed class ViewmodelWallAvoidance : MonoBehaviour
{
    [Header("Arm Bones (names inside the held view)")]
    [SerializeField] private string upperArmBone = "upperarm_r";
    [SerializeField] private string lowerArmBone = "lowerarm_r";
    [SerializeField] private string handBone = "hand_r";

    [Tooltip("Points that must stay out of geometry: hand, fingertips, item tip. Missing names are skipped.")]
    [SerializeField] private string[] probeBones = { "hand_r", "index_03_r", "Lighter_Grip", "Lighter_FlameSocket" };

    [Tooltip("Animated objects that are not children of the hand bone but are held by it (the FBX flattens " +
             "them to the root); they are carried along with the hand. Missing names are skipped.")]
    [SerializeField] private string[] handFollowers = { "Lighter_body", "Lighter_flint", "Lighter_hinge", "Lighter_top", "Lighter_wick" };

    [Header("Detection")]
    [SerializeField] private LayerMask obstacleLayers = ~0;

    [Tooltip("Colliders under this are never obstacles (the player changes layer, e.g. to Captive). " +
             "Found by the Player tag if empty.")]
    [SerializeField] private Transform ignoredRoot;

    [Min(0f)]
    [SerializeField] private float probeRadius = 0.035f;

    [Tooltip("Extra clearance kept between the probes and the wall.")]
    [Min(0f)]
    [SerializeField] private float skin = 0.02f;

    [Header("Pull")]
    [SerializeField] private float maxPull = 0.35f;

    [Tooltip("Camera-space direction the hand is pulled in: mostly back, a little up and toward the centre.")]
    [SerializeField] private Vector3 pullDirection = new(-0.1f, -0.45f, -1f);

    [Tooltip("Degrees the hand tilts back (item top toward the player) at full pull.")]
    [SerializeField] private float tiltAtMaxPull = 25f;

    [Min(0.001f)]
    [SerializeField] private float pullInTime = 0.04f;

    [Min(0.001f)]
    [SerializeField] private float releaseTime = 0.15f;

    private HeldItemView _view;
    private Transform _upper, _lower, _hand;
    private Transform[] _probes;
    private Transform[] _followers;
    private float _pull;
    private float _pullVelocity;
    private readonly RaycastHit[] _hits = new RaycastHit[16];

    public float CurrentPull => _pull;

    private void Awake()
    {
        if (ignoredRoot == null)
        {
            GameObject player = GameObject.FindGameObjectWithTag("Player");
            if (player != null)
                ignoredRoot = player.transform;
        }
    }

    private void LateUpdate()
    {
        Step(Time.deltaTime);
    }

    public void Step(float dt)
    {
        HeldItemView view = HeldItemController.Instance != null ? HeldItemController.Instance.CurrentView : null;
        if (view != _view)
            Bind(view);

        Transform cam = transform.parent;
        if (_hand == null || cam == null || !_view.gameObject.activeInHierarchy)
        {
            _pull = 0f;
            _pullVelocity = 0f;
            return;
        }

        Vector3 pullDir = cam.TransformDirection(pullDirection.normalized);
        float needed = Mathf.Min(RequiredPull(cam.position, pullDir), maxPull);

        if (dt > 0f)
            _pull = Mathf.SmoothDamp(_pull, needed, ref _pullVelocity, needed > _pull ? pullInTime : releaseTime,
                                     Mathf.Infinity, dt);
        if (_pull < 0.0005f)
            return;

        Vector3 handPosition = _hand.position;
        Quaternion handRotation = _hand.rotation;
        SolveTwoBone(_upper, _lower, _hand, handPosition + pullDir * _pull, cam.up);
        _hand.rotation = Quaternion.AngleAxis(-tiltAtMaxPull * (_pull / maxPull), cam.right) * handRotation;

        Quaternion delta = _hand.rotation * Quaternion.Inverse(handRotation);
        foreach (Transform follower in _followers)
            follower.SetPositionAndRotation(_hand.position + delta * (follower.position - handPosition),
                                            delta * follower.rotation);
    }

    private float RequiredPull(Vector3 origin, Vector3 pullDir)
    {
        float needed = 0f;
        foreach (Transform probe in _probes)
        {
            Vector3 to = probe.position - origin;
            float dist = to.magnitude;
            if (dist < 0.0001f)
                continue;
            Vector3 dir = to / dist;

            if (!NearestObstacle(origin, dir, dist + skin, out float hitDistance))
                continue;

            // Pulling by L along pullDir shortens this probe's ray by L * -dot(pullDir, dir).
            float penetration = dist + skin - hitDistance;
            float effectiveness = Mathf.Max(0.2f, -Vector3.Dot(pullDir, dir));
            needed = Mathf.Max(needed, penetration / effectiveness);
        }
        return needed;
    }

    private bool NearestObstacle(Vector3 origin, Vector3 dir, float maxDistance, out float distance)
    {
        distance = float.MaxValue;
        int count = Physics.SphereCastNonAlloc(origin, probeRadius, dir, _hits, maxDistance, obstacleLayers,
                                               QueryTriggerInteraction.Ignore);
        for (int i = 0; i < count; i++)
        {
            RaycastHit hit = _hits[i];
            // Distance 0 marks colliders already overlapping the camera, which SphereCast never reports.
            if (hit.distance <= 0f || (ignoredRoot != null && hit.collider.transform.IsChildOf(ignoredRoot)))
                continue;
            if (hit.distance < distance)
                distance = hit.distance;
        }
        return distance < float.MaxValue;
    }

    private void Bind(HeldItemView view)
    {
        _view = view;
        _upper = _lower = _hand = null;
        _probes = System.Array.Empty<Transform>();
        _followers = System.Array.Empty<Transform>();
        _pull = 0f;
        _pullVelocity = 0f;
        if (view == null)
            return;

        _upper = view.FindSocket(upperArmBone);
        _lower = view.FindSocket(lowerArmBone);
        _hand = view.FindSocket(handBone);
        if (_upper == null || _lower == null || _hand == null)
        {
            _hand = null;
            return;
        }

        _probes = FindAll(view, probeBones);
        _followers = FindAll(view, handFollowers);
    }

    private static Transform[] FindAll(HeldItemView view, string[] names)
    {
        var found = new System.Collections.Generic.List<Transform>();
        foreach (string n in names)
        {
            Transform t = view.FindSocket(n);
            if (t != null)
                found.Add(t);
        }
        return found.ToArray();
    }

    /// <summary>
    /// Analytic two-bone IK: rotates the upper and lower bone so the end bone reaches the target,
    /// keeping the current bend plane (elbow direction).
    /// </summary>
    private static void SolveTwoBone(Transform upper, Transform lower, Transform end, Vector3 target, Vector3 fallbackPole)
    {
        Vector3 a = upper.position, b = lower.position, c = end.position;
        float lab = (b - a).magnitude;
        float lcb = (c - b).magnitude;
        float lat = Mathf.Clamp((target - a).magnitude, 0.001f, lab + lcb - 0.001f);

        Vector3 ac = (c - a).normalized;
        Vector3 ab = (b - a).normalized;
        Vector3 at = (target - a).normalized;

        float acAb0 = Mathf.Acos(Mathf.Clamp(Vector3.Dot(ac, ab), -1f, 1f));
        float baBc0 = Mathf.Acos(Mathf.Clamp(Vector3.Dot((a - b).normalized, (c - b).normalized), -1f, 1f));
        float acAt0 = Mathf.Acos(Mathf.Clamp(Vector3.Dot(ac, at), -1f, 1f));
        float acAb1 = Mathf.Acos(Mathf.Clamp((lcb * lcb - lab * lab - lat * lat) / (-2f * lab * lat), -1f, 1f));
        float baBc1 = Mathf.Acos(Mathf.Clamp((lat * lat - lab * lab - lcb * lcb) / (-2f * lab * lcb), -1f, 1f));

        Vector3 bendAxis = Vector3.Cross(ac, ab);
        if (bendAxis.sqrMagnitude < 1e-8f)
            bendAxis = Vector3.Cross(ac, fallbackPole);
        bendAxis.Normalize();
        Vector3 swingAxis = Vector3.Cross(ac, at);

        Quaternion r0 = Quaternion.AngleAxis((acAb1 - acAb0) * Mathf.Rad2Deg, bendAxis);
        Quaternion r1 = Quaternion.AngleAxis((baBc1 - baBc0) * Mathf.Rad2Deg, bendAxis);
        Quaternion r2 = swingAxis.sqrMagnitude > 1e-8f
            ? Quaternion.AngleAxis(acAt0 * Mathf.Rad2Deg, swingAxis.normalized)
            : Quaternion.identity;

        lower.rotation = r1 * lower.rotation;
        upper.rotation = r2 * r0 * upper.rotation;
    }
}
