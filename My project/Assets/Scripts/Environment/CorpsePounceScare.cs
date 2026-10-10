using System.Collections;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.Events;

/// <summary>
/// Cage room corpse scare, part 2: once armed (CorpseVanishEvent's On Lights Out), the next time the player's
/// lighter catches, the corpse is crouched right in front of them and pounces (coil, leap, grab), landing with his
/// face at the camera. The view stays locked on his face. He lets go after Release Delay, or on Release() when
/// Release Delay is negative (for a scripted follow-up).
/// </summary>
[DisallowMultipleComponent]
public class CorpsePounceScare : MonoBehaviour
{
    const string LockReason = "corpse_pounce";

    [Header("Corpse")]
    [Tooltip("Kept inactive until the pounce. The pounce clip carries the leap, so root motion stays off.")]
    [SerializeField] Animator corpse;
    [SerializeField] string pounceState = "Pounce";
    [Tooltip("Horizontal distance from the camera to where he crouches. The clip carries him ~1.4 m, so this sets how " +
             "close his face ends up (keep it beyond the lighter, or the flame lights him from inside his head).")]
    [SerializeField] float spawnDistance = 1.95f;
    [Tooltip("Tried in order (degrees off the view direction) when something blocks the leap; the view turns to him.")]
    [SerializeField] float[] fallbackAngles = { 35f, -35f, 70f, -70f, 110f, -110f, 180f };
    [SerializeField] LayerMask obstacleMask = ~0;

    [Header("Face")]
    [SerializeField] SkinnedMeshRenderer face;
    [SerializeField] string[] snarlShapes =
    {
        "Brow_Drop_L", "Brow_Drop_R", "Eye_Wide_L", "Eye_Wide_R",
        "Nose_Sneer_L", "Nose_Sneer_R", "Mouth_Stretch_L", "Mouth_Stretch_R"
    };
    [SerializeField, Range(0f, 100f)] float snarlWeight = 80f;

    [Header("View")]
    [Tooltip("How tightly the view follows his face (higher = snappier).")]
    [SerializeField] float aimSharpness = 10f;
    [Tooltip("Shadowless light by the camera, on only during the pounce, so his face reads once he's past the lighter.")]
    [SerializeField] Light faceLight;
    [SerializeField] CinemachineBasicMultiChannelPerlin shakeNoise;
    [SerializeField] float impactShake = 2.5f;
    [SerializeField] float shakeDecay = 0.7f;

    [Header("Timing (seconds after the flame catches)")]
    [SerializeField] float screamTime = 0.22f;
    [SerializeField] float impactTime = 0.72f;
    [SerializeField] float growlTime = 0.85f;
    [Tooltip("Seconds after the impact before he lets go. Negative = hold until Release() is called.")]
    [SerializeField] float releaseDelay = 2.5f;

    [Header("Audio")]
    [SerializeField] AudioClip screamClip;
    [SerializeField, Range(0f, 1f)] float screamVolume = 1f;
    [SerializeField] AudioClip impactClip;
    [SerializeField, Range(0f, 1f)] float impactVolume = 0.9f;
    [SerializeField] AudioClip growlClip;
    [SerializeField, Range(0f, 1f)] float growlVolume = 0.9f;
    [SerializeField] AudioMixerGroup mixerGroup;

    [Header("Events")]
    public UnityEvent onPounce;
    public UnityEvent onGrabbed;
    public UnityEvent onReleased;

    bool _armed;
    bool _running;
    bool _releaseRequested;

    public bool IsRunning => _running;

    public void Arm()
    {
        if (!_running)
            _armed = true;
    }

    public void Release() => _releaseRequested = true;

    void Awake()
    {
        if (corpse != null)
            corpse.gameObject.SetActive(false);
        if (faceLight != null)
            faceLight.enabled = false;
    }

    void OnEnable() => HeldLighter.StruckAlight += OnLighterLit;

    void OnDisable()
    {
        HeldLighter.StruckAlight -= OnLighterLit;
        if (_running)
            Finish();
    }

    void OnLighterLit(HeldLighter lighter)
    {
        if (!_armed || corpse == null || Camera.main == null)
            return;
        _armed = false;
        StartCoroutine(PounceRoutine());
    }

    IEnumerator PounceRoutine()
    {
        _running = true;
        _releaseRequested = false;
        PlayerControlGate.Acquire(LockReason);
        GameplayHudVisibility.Instance?.HideGameplayHud();

        Transform cam = Camera.main.transform;
        var mouseLook = FindFirstObjectByType<CinemachineMouseLook>();
        var body = mouseLook != null ? mouseLook.GetComponentInParent<CharacterController>() : null;

        float floorY = body != null
            ? body.transform.TransformPoint(body.center).y - body.height * 0.5f * body.transform.lossyScale.y
            : cam.position.y - 1.6f;
        Vector3 forward = Vector3.ProjectOnPlane(cam.forward, Vector3.up);
        if (forward.sqrMagnitude < 1e-4f)
            forward = body != null ? body.transform.forward : Vector3.forward;
        forward.Normalize();

        Vector3 dir = PickDirection(new Vector3(cam.position.x, floorY, cam.position.z), forward);
        Vector3 spot = new Vector3(cam.position.x, floorY, cam.position.z) + dir * spawnDistance;
        if (Physics.Raycast(spot + Vector3.up, Vector3.down, out RaycastHit floor, 2.5f, obstacleMask, QueryTriggerInteraction.Ignore))
            spot.y = floor.point.y;

        corpse.applyRootMotion = false;
        corpse.transform.SetPositionAndRotation(spot, Quaternion.LookRotation(-dir, Vector3.up));
        corpse.gameObject.SetActive(true);
        if (faceLight != null)
            faceLight.enabled = true;
        Snarl();
        corpse.Play(pounceState, 0, 0f);
        corpse.Update(0f);
        onPounce?.Invoke();

        Transform head = corpse.GetBoneTransform(HumanBodyBones.Head);
        bool screamed = false, impacted = false, growled = false;
        for (float t = 0f; ; t += Time.deltaTime)
        {
            if (!screamed && t >= screamTime)
            {
                screamed = true;
                PlayAt(screamClip, screamVolume, head, 1.5f);
            }
            if (!impacted && t >= impactTime)
            {
                impacted = true;
                PlayAt(impactClip, impactVolume, cam, 1f);
                StartCoroutine(Shake());
                onGrabbed?.Invoke();
            }
            if (!growled && t >= growlTime)
            {
                growled = true;
                PlayAt(growlClip, growlVolume, head, 1.5f);
            }

            AimAt(head != null ? head.position + Vector3.up * 0.06f : spot + Vector3.up, body, mouseLook);

            bool timedOut = releaseDelay >= 0f && t >= impactTime + releaseDelay;
            if (impacted && (timedOut || _releaseRequested))
                break;
            yield return null;
        }

        Finish();
    }

    void Finish()
    {
        _running = false;
        if (corpse != null)
            corpse.gameObject.SetActive(false);
        if (faceLight != null)
            faceLight.enabled = false;
        GameplayHudVisibility.Instance?.ShowGameplayHud();
        PlayerControlGate.Release(LockReason);
        onReleased?.Invoke();
    }

    // First direction (view first, then the fallbacks) with room for him to crouch and leap; else the roomiest one.
    Vector3 PickDirection(Vector3 feet, Vector3 forward)
    {
        float need = spawnDistance + 0.45f;
        Vector3 best = forward;
        float bestRoom = -1f;
        for (int i = -1; i < fallbackAngles.Length; i++)
        {
            Vector3 d = i < 0 ? forward : Quaternion.Euler(0f, fallbackAngles[i], 0f) * forward;
            float room = Room(feet, d, need);
            if (room >= need)
                return d;
            if (room > bestRoom)
            {
                bestRoom = room;
                best = d;
            }
        }
        return best;
    }

    float Room(Vector3 feet, Vector3 dir, float need)
    {
        float room = need;
        foreach (float h in new[] { 0.5f, 1.3f })
            if (Physics.SphereCast(feet + Vector3.up * h, 0.3f, dir, out RaycastHit hit, need, obstacleMask, QueryTriggerInteraction.Ignore))
                room = Mathf.Min(room, hit.distance);
        return room;
    }

    void AimAt(Vector3 target, CharacterController body, CinemachineMouseLook mouseLook)
    {
        if (body == null || mouseLook == null)
            return;
        float k = 1f - Mathf.Exp(-aimSharpness * Time.deltaTime);

        Vector3 flat = target - body.transform.position;
        flat.y = 0f;
        if (flat.sqrMagnitude > 1e-4f)
            body.transform.rotation = Quaternion.Slerp(body.transform.rotation, Quaternion.LookRotation(flat), k);

        Vector3 toTarget = target - mouseLook.transform.position;
        float horizontal = new Vector2(toTarget.x, toTarget.z).magnitude;
        float wantPitch = Mathf.Clamp(Mathf.Atan2(-toTarget.y, horizontal) * Mathf.Rad2Deg, -80f, 80f);
        float pitch = mouseLook.transform.localEulerAngles.x;
        if (pitch > 180f)
            pitch -= 360f;
        mouseLook.transform.localRotation = Quaternion.Euler(Mathf.Lerp(pitch, wantPitch, k), 0f, 0f);
        mouseLook.SyncPitchFromLocalRotation();
    }

    void Snarl()
    {
        if (face == null || face.sharedMesh == null)
            return;
        foreach (string shape in snarlShapes)
        {
            int index = face.sharedMesh.GetBlendShapeIndex(shape);
            if (index >= 0)
                face.SetBlendShapeWeight(index, snarlWeight);
        }
    }

    IEnumerator Shake()
    {
        if (shakeNoise == null)
            yield break;
        float baseAmplitude = shakeNoise.AmplitudeGain;
        for (float t = 0f; t < shakeDecay; t += Time.deltaTime)
        {
            float k = 1f - t / shakeDecay;
            shakeNoise.AmplitudeGain = baseAmplitude + impactShake * k * k;
            yield return null;
        }
        shakeNoise.AmplitudeGain = baseAmplitude;
    }

    // Left where it starts (not parented to him) so hiding him on release doesn't cut the sound.
    void PlayAt(AudioClip clip, float volume, Transform at, float minDistance)
    {
        if (clip == null || at == null)
            return;
        var go = new GameObject("Corpse Pounce Audio");
        go.transform.SetParent(transform, false);
        go.transform.position = at.position;
        var src = go.AddComponent<AudioSource>();
        src.outputAudioMixerGroup = mixerGroup;
        src.spatialBlend = 1f;
        src.rolloffMode = AudioRolloffMode.Logarithmic;
        src.minDistance = minDistance;
        src.maxDistance = 25f;
        src.dopplerLevel = 0f;
        src.PlayOneShot(clip, volume);
        Destroy(go, clip.length + 0.25f);
    }
}
