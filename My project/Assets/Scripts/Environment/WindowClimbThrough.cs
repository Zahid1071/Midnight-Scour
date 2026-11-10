using System.Collections;
using System.Collections.Generic;
using FS_Atmo;
using Unity.Cinemachine;
using UnityEngine;

/// <summary>
/// After the linked window is open, looking through the gap shows the normal hand interact icon.
/// Interacting plays a weighted climb-through cinematic: the camera heaves over the sill, the player
/// body/colliders teleport to the far side halfway through, then the view blends back onto the head.
/// Object must be named "Window Climb Camera" (or point <see cref="climbCamera"/> at one) for the brain's
/// custom blends to cut in and ease out by name.
/// </summary>
[DisallowMultipleComponent]
public class WindowClimbThrough : MonoBehaviour, IInteractable
{
    const string LockReason = "window_climb";
    public const string CameraName = "Window Climb Camera";

    [Header("Window")]
    [Tooltip("The openable sash. Empty = find SimpleOpenClose on parents.")]
    [SerializeField] SimpleOpenClose window;
    [Tooltip("How open (0-1) the window must be before the gap can be climbed.")]
    [SerializeField, Range(0.1f, 1f)] float minOpenAmount = 0.75f;
    [Tooltip("Collider in the open gap (this object or a child). Enabled only while the window is open enough.")]
    [SerializeField] Collider gapCollider;

    [Header("Climb Path")]
    [Tooltip("Blue axis should point out of the house. Empty = this transform.")]
    [SerializeField] Transform outwardAxis;
    [Tooltip("Metres from the window plane to the outside stand (feet).")]
    [SerializeField] float outsideStandDistance = 0.9f;
    [Tooltip("Metres from the window plane to the inside stand (feet).")]
    [SerializeField] float insideStandDistance = 0.9f;
    [Tooltip("Eye height above feet while standing outside/inside.")]
    [SerializeField] float eyeHeight = 1.6f;
    [Tooltip("How far up from the open-gap centre the camera heaves (stays inside the gap).")]
    [SerializeField, Range(0f, 0.5f)] float gapHeave = 0.12f;
    [Tooltip("Gravity applied to the body after the mid-climb teleport while control is still locked.")]
    [SerializeField] float lockedGravity = -15f;

    [Header("Cinematic")]
    [SerializeField] CinemachineCamera climbCamera;
    [SerializeField] int priority = 100;
    [SerializeField] float climbDuration = 2.6f;
    [Tooltip("Share of the climb (0-1) at which the player body/colliders teleport to the far side.")]
    [SerializeField, Range(0.15f, 0.9f)] float teleportAt = 0.5f;
    [Tooltip("Seconds of the blend back to the player head (brain custom blend); control returns after it.")]
    [SerializeField] float blendOut = 0.8f;
    [Tooltip("Degrees the view dips while heaving over the sill.")]
    [SerializeField] float heaveDip = 8f;
    [Tooltip("Degrees the view rolls into the climb at peak effort.")]
    [SerializeField] float heaveRoll = 4f;
    [Tooltip("How much the climb slows at the crest (0 = even, 1 = heavy stall over the sill).")]
    [SerializeField, Range(0f, 1f)] float crestWeight = 0.65f;

    [Header("Audio")]
    [SerializeField] AudioClip climbSfx;
    [SerializeField, Range(0f, 1f)] float climbVolume = 1f;

    static CinemachineCamera _sharedCam;
    static bool _busy;

    bool _locked;
    bool _climbing;
    readonly List<Renderer> _hiddenViewmodel = new List<Renderer>();

    void Awake()
    {
        if (window == null)
            window = GetComponentInParent<SimpleOpenClose>();
        if (gapCollider == null)
            gapCollider = GetComponent<Collider>();
        if (outwardAxis == null)
            outwardAxis = transform;
        EnsureClimbCamera();
        if (climbCamera != null)
            climbCamera.Priority = 0;
        RefreshGap();
    }

    void OnDisable()
    {
        if (_climbing)
            EndClimbImmediate();
    }

    void LateUpdate()
    {
        if (!_climbing)
            RefreshGap();
    }

    public bool Interact()
    {
        if (_busy || _climbing || PlayerControlGate.IsInputBlocked)
            return false;

        if (!IsWindowOpenEnough())
        {
            InteractionFeedback.Fail();
            return false;
        }

        Camera main = Camera.main;
        if (main == null || !EnsureClimbCamera())
        {
            InteractionFeedback.Fail();
            return false;
        }

        GameObject player = GameObject.FindWithTag("Player");
        if (player == null)
        {
            InteractionFeedback.Fail();
            return false;
        }

        StartCoroutine(ClimbRoutine(player, main));
        return true;
    }

    bool IsWindowOpenEnough()
    {
        if (window == null)
            return true;
        if (!window.objectOpen)
            return false;
        return EstimateOpenAmount() >= minOpenAmount;
    }

    float EstimateOpenAmount()
    {
        if (window == null)
            return 1f;
        Animator animator = window.GetComponent<Animator>() ?? window.GetComponentInParent<Animator>();
        if (animator == null)
            return window.objectOpen ? 1f : 0f;
        AnimatorStateInfo state = animator.IsInTransition(0)
            ? animator.GetNextAnimatorStateInfo(0)
            : animator.GetCurrentAnimatorStateInfo(0);
        // Open clip progresses 0→1; Close is treated as inverse of its normalized time.
        if (state.IsName("Open") || state.IsName("open"))
            return Mathf.Clamp01(state.normalizedTime);
        if (state.IsName("Close") || state.IsName("close"))
            return 1f - Mathf.Clamp01(state.normalizedTime);
        return window.objectOpen ? 1f : 0f;
    }

    void RefreshGap()
    {
        if (gapCollider == null)
            return;
        bool open = IsWindowOpenEnough();
        if (gapCollider.enabled != open)
            gapCollider.enabled = open;
    }

    bool EnsureClimbCamera()
    {
        if (climbCamera != null)
            return true;
        if (_sharedCam == null)
        {
            foreach (CinemachineCamera cam in FindObjectsByType<CinemachineCamera>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (cam != null && cam.gameObject.name == CameraName)
                {
                    _sharedCam = cam;
                    break;
                }
            }
        }
        climbCamera = _sharedCam;
        if (climbCamera == null)
        {
            Debug.LogWarning($"WindowClimbThrough: no \"{CameraName}\" in the scene.", this);
            return false;
        }
        return true;
    }

    IEnumerator ClimbRoutine(GameObject player, Camera main)
    {
        _busy = true;
        _climbing = true;
        Begin(main);

        Vector3 outward = Flat(outwardAxis != null ? outwardAxis.forward : transform.forward);
        if (outward.sqrMagnitude < 0.01f)
            outward = Vector3.forward;
        outward.Normalize();

        Vector3 windowOrigin = outwardAxis != null ? outwardAxis.position : transform.position;
        Vector3 playerPos = player.transform.position;
        bool startingOutside = Vector3.Dot(Flat(playerPos - windowOrigin), outward) > 0f;

        Vector3 fromFeet = windowOrigin + outward * (startingOutside ? outsideStandDistance : -insideStandDistance);
        Vector3 toFeet = windowOrigin + outward * (startingOutside ? -insideStandDistance : outsideStandDistance);
        fromFeet = SnapFeetToFloor(fromFeet);
        toFeet = SnapFeetToFloor(toFeet);

        Vector3 fromEye = fromFeet + Vector3.up * eyeHeight;
        Vector3 toEye = toFeet + Vector3.up * eyeHeight;
        // Vinyl sash slides up when open — passage is the open gap collider, not the upper frame.
        Vector3 passage = GetGapPassagePoint();

        Vector3 travelDir = Flat(toFeet - fromFeet);
        if (travelDir.sqrMagnitude < 0.01f)
            travelDir = startingOutside ? -outward : outward;
        travelDir.Normalize();
        Quaternion faceThrough = Quaternion.LookRotation(travelDir, Vector3.up);

        // Start from the player's real eye so the cut-in matches their view.
        Vector3 startEye = main.transform.position;
        Quaternion startRot = main.transform.rotation;
        float startFov = main.fieldOfView;

        Pose(startEye, startRot, startFov);
        climbCamera.Priority = priority;
        PlaySfx(climbSfx, climbVolume);

        CharacterController body = player.GetComponent<CharacterController>();
        PlayerHider hider = player.GetComponent<PlayerHider>();
        CapsuleCollider capsule = player.GetComponent<CapsuleCollider>();
        bool capsuleWasEnabled = capsule != null && capsule.enabled;
        bool teleported = false;
        bool collisionRestored = false;
        float fallVelocity = -2f;

        // Suspend only until the body is on the far side — keeping it off through blend-out kills gravity.
        if (hider != null)
            hider.SuspendCollision();
        else if (body != null)
            body.enabled = false;
        if (capsule != null)
            capsule.enabled = false;

        float duration = Mathf.Max(0.2f, climbDuration);
        for (float t = 0f; t < duration; t += Time.deltaTime)
        {
            float linear = Mathf.Clamp01(t / duration);
            float u = WeightedClimb(linear);

            // Through the open gap centre (mild heave still inside the gap), not over the upper rail.
            Vector3 a = Vector3.Lerp(startEye, passage, Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(u * 2f)));
            Vector3 b = Vector3.Lerp(passage, toEye, Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(u * 2f - 1f)));
            Vector3 eye = Vector3.Lerp(a, b, u);

            float effort = Mathf.Sin(u * Mathf.PI); // 0 at ends, 1 at crest
            Quaternion look = Quaternion.Slerp(startRot, faceThrough, SmootherStep(Mathf.Clamp01(u * 1.35f)));
            look *= Quaternion.Euler(heaveDip * effort, 0f, heaveRoll * Mathf.Sin(u * Mathf.PI * 2f) * effort);

            float fov = Mathf.Lerp(startFov, startFov * 0.92f, effort);
            Pose(eye, look, fov);

            if (!teleported && linear >= teleportAt)
            {
                teleported = true;
                TeleportPlayer(player, body, toFeet, faceThrough);
                RestorePlayerCollision(hider, body, capsule, capsuleWasEnabled);
                collisionRestored = true;
                fallVelocity = -2f;
            }

            if (collisionRestored)
                TickLockedGravity(body, ref fallVelocity);

            yield return null;
        }

        if (!teleported)
        {
            TeleportPlayer(player, body, toFeet, faceThrough);
            RestorePlayerCollision(hider, body, capsule, capsuleWasEnabled);
            collisionRestored = true;
            fallVelocity = -2f;
        }

        Pose(toEye, faceThrough, startFov);

        CinemachineMouseLook mouseLook = FindFirstObjectByType<CinemachineMouseLook>();
        if (mouseLook != null)
            mouseLook.SetLook(faceThrough);

        climbCamera.Priority = 0;
        for (float t = 0f; t < blendOut; t += Time.deltaTime)
        {
            if (collisionRestored)
                TickLockedGravity(body, ref fallVelocity);
            yield return null;
        }

        if (!collisionRestored)
            RestorePlayerCollision(hider, body, capsule, capsuleWasEnabled);

        EndClimb();
    }

    Vector3 GetGapPassagePoint()
    {
        if (gapCollider is BoxCollider box)
        {
            Vector3 center = gapCollider.transform.TransformPoint(box.center);
            float height = box.size.y * gapCollider.transform.lossyScale.y;
            float heave = Mathf.Min(gapHeave, height * 0.2f);
            return center + Vector3.up * heave;
        }

        if (gapCollider != null)
        {
            Bounds b = gapCollider.bounds;
            float heave = Mathf.Min(gapHeave, b.extents.y * 0.4f);
            return b.center + Vector3.up * heave;
        }

        Vector3 origin = outwardAxis != null ? outwardAxis.position : transform.position;
        return origin + Vector3.up * gapHeave;
    }

    void TeleportPlayer(GameObject player, CharacterController body, Vector3 feet, Quaternion facing)
    {
        if (player == null)
            return;
        if (body != null && body.enabled)
            body.enabled = false;
        feet = SnapFeetToFloor(feet);
        player.transform.SetPositionAndRotation(feet, Quaternion.Euler(0f, facing.eulerAngles.y, 0f));
        Physics.SyncTransforms();
    }

    void RestorePlayerCollision(
        PlayerHider hider,
        CharacterController body,
        CapsuleCollider capsule,
        bool capsuleWasEnabled)
    {
        if (capsule != null)
            capsule.enabled = capsuleWasEnabled;

        if (hider != null)
        {
            hider.RestoreCollision();
        }
        else if (body != null)
        {
            Physics.SyncTransforms();
            body.enabled = true;
        }

        body?.GetComponent<StarterAssets.FirstPersonController>()?.ResetMovementState();
    }

    /// <summary>
    /// FPC skips Update while the control gate is held, so gravity must be applied here after teleport.
    /// </summary>
    void TickLockedGravity(CharacterController body, ref float fallVelocity)
    {
        if (body == null || !body.enabled)
            return;

        bool grounded = body.isGrounded;
        if (grounded && fallVelocity < 0f)
            fallVelocity = -2f;
        else
            fallVelocity += lockedGravity * Time.deltaTime;

        body.Move(new Vector3(0f, fallVelocity, 0f) * Time.deltaTime);
    }

    void Begin(Camera main)
    {
        _locked = true;
        PlayerControlGate.Acquire(LockReason);
        GameplayHudVisibility.Instance?.HideGameplayHud();
        _hiddenViewmodel.Clear();
        foreach (Renderer r in main.GetComponentsInChildren<Renderer>())
        {
            if (!r.enabled)
                continue;
            r.enabled = false;
            _hiddenViewmodel.Add(r);
        }
    }

    void EndClimb()
    {
        foreach (Renderer r in _hiddenViewmodel)
            if (r != null)
                r.enabled = true;
        _hiddenViewmodel.Clear();
        if (climbCamera != null)
            climbCamera.Priority = 0;
        if (_locked)
        {
            _locked = false;
            GameplayHudVisibility.Instance?.ShowGameplayHud();
            PlayerControlGate.Release(LockReason);
        }
        _climbing = false;
        _busy = false;
        RefreshGap();
    }

    void EndClimbImmediate()
    {
        StopAllCoroutines();
        GameObject player = GameObject.FindWithTag("Player");
        if (player != null)
        {
            RestorePlayerCollision(
                player.GetComponent<PlayerHider>(),
                player.GetComponent<CharacterController>(),
                player.GetComponent<CapsuleCollider>(),
                true);
        }
        EndClimb();
    }

    void Pose(Vector3 position, Quaternion rotation, float fov)
    {
        if (climbCamera == null)
            return;
        climbCamera.transform.SetPositionAndRotation(position, rotation);
        LensSettings lens = climbCamera.Lens;
        lens.FieldOfView = fov;
        climbCamera.Lens = lens;
    }

    /// <summary>Slows the middle of the climb so clearing the sill has weight.</summary>
    float WeightedClimb(float linear)
    {
        float u = SmootherStep(linear);
        if (crestWeight <= 0.001f)
            return u;
        // Bias time toward the ends so the crest lingers.
        float crest = 0.5f;
        float pull = crestWeight * 0.85f;
        if (u < crest)
            return Mathf.Lerp(u, crest * (u / crest) * (u / crest), pull);
        float r = (u - crest) / (1f - crest);
        return Mathf.Lerp(u, crest + (1f - crest) * (1f - (1f - r) * (1f - r)), pull);
    }

    static Vector3 SnapFeetToFloor(Vector3 feet)
    {
        Vector3 origin = feet + Vector3.up * 1.2f;
        if (Physics.Raycast(origin, Vector3.down, out RaycastHit hit, 3f, ~0, QueryTriggerInteraction.Ignore))
        {
            if (hit.collider.GetComponentInParent<CharacterController>() == null)
                return hit.point;
        }
        return feet;
    }

    static void PlaySfx(AudioClip clip, float volume)
    {
        if (clip == null)
            return;
        if (SoundManager.Instance != null)
            SoundManager.Instance.PlaySFX(clip, volume);
        else if (Camera.main != null)
            AudioSource.PlayClipAtPoint(clip, Camera.main.transform.position, volume);
    }

    static float SmootherStep(float u)
    {
        u = Mathf.Clamp01(u);
        return u * u * u * (u * (u * 6f - 15f) + 10f);
    }

    static Vector3 Flat(Vector3 v)
    {
        v.y = 0f;
        return v;
    }

    void OnDrawGizmosSelected()
    {
        Transform axis = outwardAxis != null ? outwardAxis : transform;
        Vector3 outward = Flat(axis.forward);
        if (outward.sqrMagnitude < 0.01f)
            return;
        outward.Normalize();
        Vector3 origin = axis.position;
        Vector3 outside = origin + outward * outsideStandDistance;
        Vector3 inside = origin - outward * insideStandDistance;
        Vector3 passage = gapCollider != null ? gapCollider.bounds.center + Vector3.up * gapHeave : origin + Vector3.up * gapHeave;
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(outside + Vector3.up * eyeHeight, 0.08f);
        Gizmos.DrawWireSphere(passage, 0.08f);
        Gizmos.DrawWireSphere(inside + Vector3.up * eyeHeight, 0.08f);
        Gizmos.DrawLine(outside + Vector3.up * eyeHeight, passage);
        Gizmos.DrawLine(passage, inside + Vector3.up * eyeHeight);
        Gizmos.color = Color.yellow;
        Gizmos.DrawRay(origin, outward * 0.5f);
        if (gapCollider != null)
        {
            Gizmos.color = new Color(0.2f, 1f, 0.4f, 0.35f);
            Gizmos.matrix = Matrix4x4.TRS(gapCollider.transform.position, gapCollider.transform.rotation, gapCollider.transform.lossyScale);
            if (gapCollider is BoxCollider box)
                Gizmos.DrawCube(box.center, box.size);
        }
    }
}
