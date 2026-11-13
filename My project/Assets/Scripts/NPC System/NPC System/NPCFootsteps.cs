using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Audio;
#if STEAMAUDIO_ENABLED
using SteamAudioSource = SteamAudio.SteamAudioSource;
using SteamAudioProbeBatch = SteamAudio.SteamAudioProbeBatch;
using HRTFInterpolation = SteamAudio.HRTFInterpolation;
using OcclusionType = SteamAudio.OcclusionType;
using TransmissionType = SteamAudio.TransmissionType;
#endif

/// <summary>
/// NPC footsteps fired the moment a foot of the animated skeleton touches down, and played from that foot. Walk,
/// jog and every blend between them stay in sync with what you see, because nothing is timed by clip events or
/// distance. The animator keeps animating off-screen so steps behind walls stay in time too. Rigs without
/// humanoid feet fall back to the clips' "PlayFootstepAnimEvent" events.
/// With Steam Audio the emitter is HRTF-spatialized (front/back/up/down), occluded and muffled through walls by
/// their acoustic material, and routed around corners and through open doors by the scene's baked probe batch.
/// </summary>
public class NPCFootsteps : MonoBehaviour
{
    [Header("Clips")]
    [SerializeField] FootstepBank bank;
    [Tooltip("Occasional floorboard creak layered on wood steps.")]
    [SerializeField] AudioClip[] creakClips;
    [SerializeField, Range(0f, 1f)] float creakChance = 0.12f;
    [SerializeField, Range(0f, 2f)] float creakVolume = 0.7f;

    [Header("Loudness / Weight")]
    [SerializeField, Range(0f, 4f)] float walkVolume = 1f;
    [SerializeField, Range(0f, 4f)] float runVolume = 1.35f;
    [Tooltip("Below 1 = heavier than the player's steps on the same floor.")]
    [SerializeField] Vector2 walkPitch = new Vector2(0.8f, 0.88f);
    [SerializeField] Vector2 runPitch = new Vector2(0.86f, 0.94f);

    [Header("Walk / Run")]
    [Tooltip("Speed above this uses run clips/volume.")]
    [Min(0f)]
    [SerializeField] float runSpeedThreshold = 3.25f;
    [Tooltip("When true, Chase AI state always uses run footstep treatment.")]
    [SerializeField] bool forceRunClipsDuringChase = true;

    [Header("Foot Contact")]
    [Tooltip("How far (m) a foot must swing forward, relative to the body, before its next touchdown counts.")]
    [SerializeField, Min(0.05f)] float swingDistance = 0.2f;
    [Tooltip("A swung foot only lands within this height (m) of its lowest planted height — not mid-air.")]
    [SerializeField, Min(0.01f)] float contactHeight = 0.06f;
    [Tooltip("The foot has landed once it falls slower than this (m/s).")]
    [SerializeField, Min(0f)] float settleSpeed = 0.05f;
    [Tooltip("Touchdowns only sound while moving faster than this, or within Stop Grace of stopping (the last step).")]
    [SerializeField, Min(0f)] float minMoveSpeed = 0.15f;
    [SerializeField, Min(0f)] float stopGrace = 0.35f;

    [Header("Emitter")]
    [SerializeField] AudioMixerGroup output;
    [SerializeField] float minDistance = 2f;
    [SerializeField] float maxDistance = 35f;
    [Tooltip("Volume over distance; X is 0-1 of Max Distance. Kept high so steps carry across the whole house — " +
             "walls and doors do the muffling, not distance.")]
    [SerializeField] AnimationCurve rolloff = new AnimationCurve(
        new Keyframe(0f, 1f),
        new Keyframe(0.06f, 1f),
        new Keyframe(0.25f, 0.55f),
        new Keyframe(0.5f, 0.28f),
        new Keyframe(0.8f, 0.1f),
        new Keyframe(1f, 0f));

    [Header("Steam Audio")]
    [Tooltip("Steps behind a wall are muffled by the wall's acoustic material instead of cut off.")]
    [SerializeField] bool transmission = true;
    [Tooltip("Steps reach the listener around corners and through open doors (needs a baked probe batch in the scene).")]
    [SerializeField] bool pathing = true;
    [SerializeField, Range(0f, 2f)] float pathingMix = 1f;

    // Height and forward position are in the rig's frame (metres, relative to the body), so walking speed,
    // turning and slopes don't change them.
    class Foot
    {
        public Transform bone;
        public bool primed;
        public float height, forward;
        public float planted = float.MaxValue;
        public float rearmost = float.MaxValue;
        public bool swung;
    }

    // How fast the planted-height estimate creeps up, so it can follow a pose that settles higher.
    const float PlantedDrift = 0.02f;
    // Must exceed OcclusionRadius: the volumetric sample sphere dipping into the floor mesh counts as occluded.
    const float OcclusionRadius = 0.35f;
    const float EmitterHeight = OcclusionRadius + 0.1f;

    NavMeshAgent _agent;
    NPCController _npc;
    Transform _root;
    Animator _animator;
    Foot _left, _right;
    AudioSource _source;
#if STEAMAUDIO_ENABLED
    SteamAudioSource _steam;
    SteamAudioProbeBatch[] _batches;
    float _nextBatchCheck;
#endif

    Vector3 _lastPosition;
    float _lastMovingTime = -10f;

    bool HasFeet => _left != null && _right != null;

    void Awake()
    {
        _agent = GetComponentInParent<NavMeshAgent>();
        _npc = GetComponentInParent<NPCController>();
        _root = _npc != null ? _npc.transform : _agent != null ? _agent.transform : transform;
        _lastPosition = _root.position;

        _animator = GetComponent<Animator>();
        if (_animator == null)
            _animator = _root.GetComponentInChildren<Animator>();
        if (_animator != null && _animator.isHuman && _animator.avatar != null)
        {
            Transform left = _animator.GetBoneTransform(HumanBodyBones.LeftFoot);
            Transform right = _animator.GetBoneTransform(HumanBodyBones.RightFoot);
            if (left != null && right != null)
            {
                _left = new Foot { bone = left };
                _right = new Foot { bone = right };
                // Culled transforms freeze the feet whenever he's off-screen — exactly when you listen for him.
                _animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            }
        }
    }

    void Start()
    {
        BuildEmitter();
    }

    /// <summary>Animation event receiver; only times steps for rigs without humanoid feet.</summary>
    public void PlayFootstepAnimEvent()
    {
        if (!HasFeet && IsMoving())
            Step(_root.position);
    }

    void Update()
    {
        float dt = Time.deltaTime;
        if (dt <= 0f)
            return;

#if STEAMAUDIO_ENABLED
        // Pathing only works inside the batch the source uses, so follow the NPC between buildings.
        if (_steam != null && _steam.pathing && Time.time >= _nextBatchCheck)
        {
            _nextBatchCheck = Time.time + 0.5f;
            SteamAudioProbeBatch batch = FindProbeBatch();
            if (batch != null)
                _steam.pathingProbeBatch = batch;
        }
#endif

        Vector3 delta = _root.position - _lastPosition;
        _lastPosition = _root.position;
        delta.y = 0f;
        // Teleports (respawn, cutscene snaps) aren't walking.
        float speed = delta.magnitude > 2f ? 0f : delta.magnitude / dt;
        if (speed > minMoveSpeed)
            _lastMovingTime = Time.time;
    }

    // After the animator has posed the skeleton for this frame.
    void LateUpdate()
    {
        if (!HasFeet || !_animator.isActiveAndEnabled || Time.deltaTime <= 0f)
            return;

        Transform rig = _animator.transform;
        Quaternion toRig = Quaternion.Inverse(rig.rotation);
        bool moving = IsMoving();
        TickFoot(_left, rig.position, toRig, moving);
        TickFoot(_right, rig.position, toRig, moving);
    }

    // A footfall: after swinging forward, the foot is now travelling back under the body (planted on a floor that
    // slides past) and has stopped falling, close to its planted height.
    void TickFoot(Foot foot, Vector3 rigPosition, Quaternion toRig, bool moving)
    {
        float dt = Time.deltaTime;
        Vector3 local = toRig * (foot.bone.position - rigPosition);
        float height = local.y;
        float forward = local.z;

        if (!foot.primed)
        {
            foot.primed = true;
            foot.height = height;
            foot.forward = forward;
            return;
        }

        float fallSpeed = (foot.height - height) / dt;
        bool movingBack = forward < foot.forward;
        foot.height = height;
        foot.forward = forward;
        foot.planted = Mathf.Min(foot.planted + PlantedDrift * dt, height);

        if (!foot.swung)
        {
            foot.rearmost = Mathf.Min(foot.rearmost, forward);
            if (forward > foot.rearmost + swingDistance)
                foot.swung = true;
            return;
        }

        if (!movingBack || fallSpeed > settleSpeed || height > foot.planted + contactHeight)
            return;

        foot.swung = false;
        foot.rearmost = forward;
        if (moving)
            Step(foot.bone.position);
    }

    bool IsMoving() => Time.time - _lastMovingTime <= stopGrace;

    void Step(Vector3 footPosition)
    {
        if (bank == null || _source == null)
            return;

        FootstepSurfaceType type = FootstepSurface.Detect(footPosition, _root, bank.fallback);
        FootstepBank.Surface surface = bank.Get(type);
        bool running = IsRunning();
        AudioClip clip = bank.Pick(surface, running);
        if (clip == null)
            return;

        _source.transform.position = new Vector3(footPosition.x, _root.position.y + EmitterHeight, footPosition.z);
        Vector2 pitch = running ? runPitch : walkPitch;
        _source.pitch = Random.Range(pitch.x, pitch.y);
        _source.PlayOneShot(clip, (running ? runVolume : walkVolume) * surface.volume * Random.Range(0.9f, 1f));

        if (type == FootstepSurfaceType.Wood && creakClips != null && creakClips.Length > 0 && Random.value < creakChance)
        {
            AudioClip creak = FootstepBank.Tight(creakClips[Random.Range(0, creakClips.Length)]);
            if (creak != null)
                _source.PlayOneShot(creak, creakVolume * Random.Range(0.7f, 1f));
        }
    }

    bool IsRunning()
    {
        if (forceRunClipsDuringChase && _npc != null && _npc.currentState == NPCController.AIState.Chase)
            return true;
        return _agent != null && _agent.enabled && _agent.velocity.magnitude > runSpeedThreshold;
    }

    void BuildEmitter()
    {
        var go = new GameObject("Footstep Emitter");
        // Steam Audio reads the source's settings when it wakes, so everything is set before activation.
        go.SetActive(false);
        go.transform.SetParent(_root, false);
        go.transform.localPosition = new Vector3(0f, EmitterHeight, 0f);

        _source = go.AddComponent<AudioSource>();
        _source.playOnAwake = false;
        _source.loop = false;
        _source.outputAudioMixerGroup = output;
        _source.priority = 0;
        _source.spatialBlend = 1f;
        _source.spatialize = true;
        _source.dopplerLevel = 0f;
        _source.spread = 0f;
        _source.rolloffMode = AudioRolloffMode.Custom;
        _source.minDistance = minDistance;
        _source.maxDistance = maxDistance;
        _source.SetCustomCurve(AudioSourceCurveType.CustomRolloff, rolloff);

#if STEAMAUDIO_ENABLED
        _batches = FindObjectsByType<SteamAudioProbeBatch>(FindObjectsSortMode.None);
        var steam = _steam = go.AddComponent<SteamAudioSource>();
        steam.directBinaural = true;
        steam.interpolation = HRTFInterpolation.Bilinear;
        steam.distanceAttenuation = true;
        steam.airAbsorption = true;
        steam.occlusion = true;
        steam.occlusionType = OcclusionType.Volumetric;
        steam.occlusionRadius = OcclusionRadius;
        steam.occlusionSamples = 16;
        steam.transmission = transmission;
        steam.transmissionType = TransmissionType.FrequencyDependent;
        steam.maxTransmissionSurfaces = 3;

        SteamAudioProbeBatch batch = pathing ? FindProbeBatch() : null;
        steam.pathing = batch != null;
        steam.pathingProbeBatch = batch;
        steam.pathValidation = true;
        steam.findAlternatePaths = true;
        steam.applyHRTFToPathing = true;
        steam.pathingMixLevel = pathingMix;
#endif

        go.SetActive(true);
    }

#if STEAMAUDIO_ENABLED
    SteamAudioProbeBatch FindProbeBatch()
    {
        SteamAudioProbeBatch first = null;
        foreach (SteamAudioProbeBatch batch in _batches)
        {
            if (batch == null)
                continue;
            if (first == null)
                first = batch;
            Vector3 local = batch.transform.InverseTransformPoint(_root.position);
            if (Mathf.Abs(local.x) <= 0.5f && Mathf.Abs(local.y) <= 0.5f && Mathf.Abs(local.z) <= 0.5f)
                return batch;
        }
        return first;
    }
#endif
}
