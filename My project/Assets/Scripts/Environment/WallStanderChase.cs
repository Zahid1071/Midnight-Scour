using System.Collections;
using FS_Atmo;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Audio;

/// <summary>
/// The guy at the wall, once the bar behind him is taken: the moment the player turns their back on him and faces the
/// way out, the lighter dies and he turns and screams. A second after the scream ends (at once if they look back at him)
/// he comes after them with the Butcher's run, faster than the player walks but slower than they sprint. The cage room door can't be closed behind them. Shutting the corridor door with him on the
/// other side ends it: he pounds on it and is gone for good. If he reaches the doorway before it's shut he shoves it back
/// open and comes on faster than they can sprint. Reaching them plays the Butcher's capture (axe swing,
/// headbutt, bite) through the jumpscare camera and the normal game over. Continue puts him back at the wall, waiting
/// again once the player is out of the cage.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(NavMeshAgent))]
public class WallStanderChase : MonoBehaviour
{
    enum State { Waiting, Roaring, Chasing, Captured, Gone }

    [SerializeField] CorpseVanishEvent wallStanderEvent;
    [SerializeField] Animator animator;
    [Tooltip("Turned off while he chases (it keeps the player off him while he stands at the wall).")]
    [SerializeField] Collider keepAwayBarrier;
    [SerializeField] AudioMixerGroup mixerGroup;

    [Header("Trigger")]
    [Tooltip("The way out of the cage room.")]
    [SerializeField] Transform exitPoint;
    [Tooltip("Starts once the view is within this many degrees (flat) of the way out...")]
    [SerializeField] float facingExitAngle = 50f;
    [Tooltip("...with him at least this many degrees (flat) off the view.")]
    [SerializeField] float awayFromHimAngle = 110f;
    [Tooltip("Backing away this far (m, flat) from where the bar was taken starts it too, even while watching him.")]
    [SerializeField] float backAwayDistance = 3f;

    [Header("Roar")]
    [SerializeField] AudioClip roarClip;
    [SerializeField, Range(0f, 1f)] float roarVolume = 1f;
    [SerializeField] float roarMinDistance = 4f;
    [Tooltip("Strikes of the lighter that only spark after it dies; the next one catches.")]
    [SerializeField] int lighterMisfires = 2;
    [Tooltip("Base layer state he screams in, turned to face the player, as the roar plays.")]
    [SerializeField] string screamState = "Scream";
    [SerializeField] float screamBlend = 0.15f;
    [Tooltip("Seconds into the scream before he starts running.")]
    [SerializeField] float runAfterScreamStart = 1.5f;
    [Tooltip("Seconds he takes to turn from the wall to the player as the scream starts.")]
    [SerializeField] float screamTurnTime = 0.3f;
    [Tooltip("Looking back at him (within this many degrees of the view) makes him run straight away.")]
    [SerializeField] float lookBackAngle = 35f;

    [Header("Chase")]
    [Tooltip("Metres per second; the player walks at 2.6 and sprints at 4.")]
    [SerializeField] float chaseSpeed = 3.6f;
    [SerializeField] string wallState = "Wall Pose";
    [Tooltip("Base layer state with the Butcher's run.")]
    [SerializeField] string runState = "Run";
    [Tooltip("Ground speed the run clip is authored at (m/s); it plays slower to match his speed.")]
    [SerializeField] float runClipSpeed = 3f;
    [SerializeField] string runSpeedParameter = "RunSpeed";
    [SerializeField] float runBlend = 0.25f;
    [SerializeField] float repathInterval = 0.2f;
    [Tooltip("Closed doors this close to him open when they're in his way.")]
    [SerializeField] float doorOpenDistance = 2.5f;

    [Header("Chase music")]
    [Tooltip("Starts as he starts running.")]
    [SerializeField] AudioClip chaseMusic;
    [SerializeField] AudioMixerGroup musicGroup;
    [SerializeField, Range(0f, 1f)] float chaseMusicVolume = 1f;
    [SerializeField] float chaseMusicFadeIn = 0.4f;
    [Tooltip("Seconds it fades over once the escape door is shut.")]
    [SerializeField] float chaseMusicFadeOut = 1.5f;

    [Header("Close to him (same as the Butcher's proximity glitch and whisper)")]
    [Tooltip("Full intensity at this distance or closer.")]
    [SerializeField] float effectMinDistance = 2f;
    [Tooltip("Starts at this distance.")]
    [SerializeField] float effectMaxDistance = 7f;
    [SerializeField] float maxScanLineJitter = 0.05f;
    [SerializeField] float maxHorizontalShake = 0.05f;
    [SerializeField] float maxColorDrift = 0.05f;
    [SerializeField] float maxHorizontalRipple = 0.05f;
    [SerializeField] float maxWhisperIntensity = 1f;
    [Tooltip("Per second, while it follows the distance.")]
    [SerializeField] float effectSmoothing = 5f;
    [Tooltip("Seconds it fades away over once the escape door is shut.")]
    [SerializeField] float effectFadeOut = 2f;

    [Header("Doors")]
    [Tooltip("The player can't close this once the bar is taken (the cage room door).")]
    [SerializeField] SimpleOpenClose noCloseDoor;
    [Tooltip("Closing this behind them with him on the other side ends the chase (the corridor door).")]
    [SerializeField] SimpleOpenClose escapeDoor;
    [Tooltip("At the escape door, forward pointing to the player's side.")]
    [SerializeField] Transform escapeSide;
    [Tooltip("Pounds on the escape door, played in order: the first the moment it's shut.")]
    [SerializeField] AudioClip[] bangClips;
    [SerializeField, Range(0f, 1f)] float bangVolume = 1f;
    [Tooltip("Full volume up to this distance from the door.")]
    [SerializeField] float bangMinDistance = 6f;
    [Tooltip("Seconds between pounds.")]
    [SerializeField] Vector2 bangGap = new Vector2(0.9f, 1.5f);
    [Tooltip("Longest wait for the door to finish swinging shut before the first pound.")]
    [SerializeField] float maxDoorCloseWait = 1.2f;
    [Tooltip("Metres per second once he's through the escape door before it shut; faster than the player sprints (4).")]
    [SerializeField] float breachSpeed = 5.5f;
    [SerializeField] float breachAcceleration = 20f;
    [Tooltip("Seconds the chase music is cut over at the first pound, so nothing covers it.")]
    [SerializeField] float musicCutOnFirstBang = 0.3f;
    [Tooltip("Camera roll (degrees) each pound gives the player, full within Bang Min Distance of the door.")]
    [SerializeField] float bangShakeDegrees = 0.8f;
    [SerializeField] float bangShakeDuration = 0.35f;
    [Tooltip("No shake from this far away.")]
    [SerializeField] float bangShakeMaxDistance = 15f;

    [Header("Capture")]
    [Tooltip("Flat distance to the player at which he catches them.")]
    [SerializeField] float captureDistance = 1.2f;
    [SerializeField] string captureTrigger = "Jumpscare";
    [Tooltip("Under his head bone, turned to match the Butcher's head, so the jumpscare camera sits where it does for the Butcher.")]
    [SerializeField] Transform jumpscareAnchor;
    [Tooltip("Kept off until the capture; the lighter is out by then, so this lights his face for the jumpscare.")]
    [SerializeField] Light captureLight;
    [Tooltip("Seconds between pulling the player out of a hiding spot and the capture.")]
    [SerializeField] float hideExitSecondsBeforeCapture = 1.5f;

    State _state;
    bool _needsCageExit;
    NavMeshAgent _agent;
    Transform _player;
    Transform _head;
    Vector3 _startPosition;
    Quaternion _startRotation;
    float _nextRepath;
    int _runSpeedId;
    bool _hasRunSpeed;
    Coroutine _routine;
    CinemachineCamera _jumpscareCam;
    Transform _camFollow;
    Transform _camLookAt;
    bool _haveBarSpot;
    Vector3 _barSpot;
    AudioSource _music;
    float _musicTarget;
    float _musicRate;
    float _effectLevel;
    bool _owningEffects;
    float _baseAcceleration;
    bool _breached;
    bool _doorShutting;
    float _shutTime;
    Quaternion _leafLast;
    int _leafStill;
    Coroutine _shake;
    CinemachineCamera _shakeCam;
    float _shakeBaseDutch;

    public bool IsChasing => _state == State.Chasing;

    void Awake()
    {
        _agent = GetComponent<NavMeshAgent>();
        _agent.enabled = false;
        _baseAcceleration = _agent.acceleration;
        if (captureLight != null)
            captureLight.enabled = false;
        if (animator == null)
            animator = GetComponent<Animator>();
        _head = animator != null && animator.isHuman ? animator.GetBoneTransform(HumanBodyBones.Head) : transform;
        _startPosition = transform.position;
        _startRotation = transform.rotation;
        _runSpeedId = Animator.StringToHash(runSpeedParameter);
        if (animator != null)
            foreach (AnimatorControllerParameter p in animator.parameters)
                _hasRunSpeed |= p.nameHash == _runSpeedId && p.type == AnimatorControllerParameterType.Float;
    }

    void OnEnable() => CaptureContinue.Applied += ResetToWall;

    void OnDisable()
    {
        CaptureContinue.Applied -= ResetToWall;
        ReleaseEffects();
        StopMusic();
        StopShake();
    }

    void Update()
    {
        if (_player == null)
        {
            GameObject p = GameObject.FindWithTag("Player");
            if (p == null)
                return;
            _player = p.transform;
        }

        if (noCloseDoor != null && noCloseDoor.PlayerCanClose && _state != State.Gone
            && wallStanderEvent != null && wallStanderEvent.BarTaken)
            noCloseDoor.PlayerCanClose = false;

        if (_state == State.Waiting)
            TickTrigger();
        else if (_state == State.Chasing)
            TickChase();
        TickMusic();
        TickEffects();
    }

    void TickTrigger()
    {
        if (wallStanderEvent == null || !wallStanderEvent.BarTaken || PlayerControlGate.IsInputBlocked)
            return;
        if (_needsCageExit)
        {
            if (Cage.IsPlayerInsideAnyCage(_player))
                return;
            _needsCageExit = false;
        }
        if (!_haveBarSpot)
        {
            _haveBarSpot = true;
            _barSpot = _player.position;
        }
        if (Flat(_player.position - _barSpot).magnitude >= backAwayDistance)
        {
            _routine = StartCoroutine(RoarRoutine());
            return;
        }

        Camera cam = Camera.main;
        if (cam == null || exitPoint == null)
            return;
        Vector3 view = Flat(cam.transform.forward);
        if (view.sqrMagnitude < 0.01f)
            return;
        if (Vector3.Angle(view, Flat(exitPoint.position - cam.transform.position)) > facingExitAngle
            || Vector3.Angle(view, Flat(transform.position - cam.transform.position)) < awayFromHimAngle)
            return;
        _routine = StartCoroutine(RoarRoutine());
    }

    IEnumerator RoarRoutine()
    {
        _state = State.Roaring;
        HeldLighter lighter = FindFirstObjectByType<HeldLighter>(FindObjectsInactive.Include);
        if (lighter != null)
        {
            if (lighter.IsLit)
                lighter.Extinguish();
            lighter.MisfireNextStrikes(lighterMisfires);
        }

        if (roarClip != null)
        {
            var go = new GameObject("Wall Stander Roar Audio");
            go.transform.SetParent(_head, false);
            var src = go.AddComponent<AudioSource>();
            src.playOnAwake = false;
            src.outputAudioMixerGroup = mixerGroup;
            src.spatialBlend = 1f;
            src.rolloffMode = AudioRolloffMode.Logarithmic;
            src.minDistance = roarMinDistance;
            src.maxDistance = 30f;
            src.dopplerLevel = 0f;
            src.PlayOneShot(roarClip, roarVolume);
            Destroy(go, roarClip.length + 0.25f);
        }

        // He turns to the player and screams, running a moment into it or the instant they look at him.
        // Watched from the start (they backed away), he skips the scream and the agent turns him as he runs.
        if (!PlayerLooksAtHim())
        {
            if (animator != null && animator.HasState(0, Animator.StringToHash(screamState)))
            {
                animator.applyRootMotion = false;
                animator.CrossFadeInFixedTime(screamState, screamBlend, 0);
            }
            Quaternion from = transform.rotation;
            for (float t = 0f; t < runAfterScreamStart && !PlayerLooksAtHim(); t += Time.deltaTime)
            {
                Vector3 toPlayer = Flat(_player.position - transform.position);
                if (toPlayer.sqrMagnitude > 1e-4f)
                    transform.rotation = Quaternion.Slerp(from, Quaternion.LookRotation(toPlayer),
                        screamTurnTime > 0f ? Mathf.SmoothStep(0f, 1f, t / screamTurnTime) : 1f);
                yield return null;
            }
        }
        StartChase();
        _routine = null;
    }

    bool PlayerLooksAtHim()
    {
        Camera cam = Camera.main;
        if (cam == null)
            return false;
        Vector3 point = _head != null ? _head.position : transform.position + Vector3.up * 1.5f;
        return Vector3.Angle(cam.transform.forward, point - cam.transform.position) <= lookBackAngle;
    }

    void StartChase()
    {
        if (keepAwayBarrier != null)
            keepAwayBarrier.enabled = false;
        if (animator != null)
        {
            animator.applyRootMotion = false;
            animator.CrossFadeInFixedTime(runState, runBlend, 0);
        }

        _agent.enabled = true;
        if (NavMesh.SamplePosition(transform.position, out NavMeshHit hit, 2f, NavMesh.AllAreas))
            _agent.Warp(hit.position);
        _agent.speed = chaseSpeed;
        _agent.acceleration = _baseAcceleration;
        _agent.isStopped = false;
        _nextRepath = 0f;
        _breached = false;
        _doorShutting = false;
        _state = State.Chasing;
        StartMusic();
        TickChase();
    }

    void StartMusic()
    {
        if (chaseMusic == null)
            return;
        if (_music == null)
        {
            var go = new GameObject("Wall Stander Chase Music");
            _music = go.AddComponent<AudioSource>();
            _music.playOnAwake = false;
            _music.loop = true;
            _music.spatialBlend = 0f;
            _music.outputAudioMixerGroup = musicGroup;
            _music.clip = chaseMusic;
            _music.volume = 0f;
            _music.Play();
        }
        _musicTarget = chaseMusicVolume;
        _musicRate = chaseMusicVolume / Mathf.Max(0.01f, chaseMusicFadeIn);
    }

    void FadeOutMusic(float seconds)
    {
        if (_music == null)
            return;
        _musicTarget = 0f;
        _musicRate = Mathf.Max(0.01f, _music.volume) / Mathf.Max(0.01f, seconds);
    }

    void TickMusic()
    {
        if (_music == null)
            return;
        _music.volume = Mathf.MoveTowards(_music.volume, _musicTarget, _musicRate * Time.deltaTime);
        if (_musicTarget <= 0f && _music.volume <= 0f)
            StopMusic();
    }

    void StopMusic()
    {
        if (_music != null)
            Destroy(_music.gameObject);
        _music = null;
    }

    // The Butcher's proximity glitch and whisper (ButcherProximityThreat), driven by distance during the chase,
    // full during the capture and fading away once he's shut out.
    void TickEffects()
    {
        float target = 0f;
        float rate = effectSmoothing;
        if (_state == State.Captured)
            target = 1f;
        else if ((_state == State.Roaring || _state == State.Chasing) && _player != null)
            target = 1f - Mathf.InverseLerp(effectMinDistance, effectMaxDistance,
                Vector3.Distance(transform.position, _player.position));
        else if (_state == State.Gone)
            rate = 1f / Mathf.Max(0.01f, effectFadeOut);

        _effectLevel = Mathf.MoveTowards(_effectLevel, target, rate * Time.deltaTime);
        if (_effectLevel <= 0f)
        {
            ReleaseEffects();
            return;
        }

        _owningEffects = true;
        KinoGlitch.AnalogGlitchController glitch = Glitch();
        if (glitch != null)
        {
            glitch.ScanLineJitter = _effectLevel * maxScanLineJitter;
            glitch.HorizontalShake = _effectLevel * maxHorizontalShake;
            glitch.ColorDrift = _effectLevel * maxColorDrift;
            glitch.HorizontalRipple = _effectLevel * maxHorizontalRipple;
            glitch.VerticalJump = 0f;
        }
        if (SoundManager.Instance != null)
            SoundManager.Instance.SetSearchTension(_effectLevel * maxWhisperIntensity);
    }

    void ReleaseEffects()
    {
        _effectLevel = 0f;
        if (!_owningEffects)
            return;
        _owningEffects = false;
        KinoGlitch.AnalogGlitchController glitch = Glitch();
        if (glitch != null)
        {
            glitch.ScanLineJitter = 0f;
            glitch.HorizontalShake = 0f;
            glitch.ColorDrift = 0f;
            glitch.HorizontalRipple = 0f;
            glitch.VerticalJump = 0f;
        }
        if (SoundManager.Instance != null)
            SoundManager.Instance.StopSearchTension();
    }

    static KinoGlitch.AnalogGlitchController Glitch()
    {
        Camera cam = Camera.main;
        return cam != null ? cam.GetComponent<KinoGlitch.AnalogGlitchController>() : null;
    }

    void TickChase()
    {
        if (!_agent.isOnNavMesh)
            return;

        if (!_breached && escapeDoor != null && escapeSide != null && TickEscapeDoor())
        {
            _routine = StartCoroutine(ShutOutRoutine());
            return;
        }

        PlayerHider hider = _player.GetComponent<PlayerHider>();
        bool hidden = hider != null && hider.isHidden && hider.CurrentHidingSpot != null;
        Vector3 target = _player.position;
        if (hidden)
        {
            Transform pivot = hider.CurrentHidingSpot.GetSearchPivot();
            target = pivot != null ? pivot.position : hider.CurrentHidingSpot.transform.position;
        }

        if (Time.time >= _nextRepath)
        {
            _nextRepath = Time.time + repathInterval;
            _agent.SetDestination(target);
            if (_agent.pathStatus != NavMeshPathStatus.PathComplete)
                OpenDoorsInTheWay();
        }

        if (animator != null && _hasRunSpeed)
            animator.SetFloat(_runSpeedId, Mathf.Clamp(_agent.velocity.magnitude / Mathf.Max(0.01f, runClipSpeed), 0.6f, 2f));

        Vector3 offset = target - transform.position;
        if (Flat(offset).magnitude <= captureDistance + (hidden ? 0.5f : 0f) && Mathf.Abs(offset.y) < 1.5f)
            _routine = StartCoroutine(CaptureRoutine(hider, hidden));
    }

    float SideOfEscape(Vector3 point) => Vector3.Dot(point - escapeSide.position, escapeSide.forward);

    // True once the escape door has finished swinging shut with him still wholly on the far side. Reaching the
    // doorway first (or being through already) means he made it.
    bool TickEscapeDoor()
    {
        if (escapeDoor.objectOpen || SideOfEscape(_player.position) <= 0f)
        {
            if (_doorShutting)
                StartMusic();
            _doorShutting = false;
            return false;
        }

        float his = SideOfEscape(transform.position);
        if (his > -_agent.radius)
        {
            MakeItThrough(his);
            return false;
        }

        Transform leaf = escapeDoor.transform;
        if (!_doorShutting)
        {
            _doorShutting = true;
            _shutTime = 0f;
            _leafLast = leaf.rotation;
            _leafStill = 0;
            FadeOutMusic(chaseMusicFadeOut);
            return false;
        }

        _shutTime += Time.deltaTime;
        _leafStill = Quaternion.Angle(leaf.rotation, _leafLast) < 0.05f ? _leafStill + 1 : 0;
        _leafLast = leaf.rotation;
        return _shutTime >= maxDoorCloseWait || (_leafStill >= 3 && _shutTime >= 0.3f);
    }

    void MakeItThrough(float side)
    {
        _breached = true;
        _doorShutting = false;
        _agent.speed = breachSpeed;
        _agent.acceleration = Mathf.Max(_baseAcceleration, breachAcceleration);
        _nextRepath = 0f;
        StartMusic();
        escapeDoor.PlayerCanClose = false;
        if (side < _agent.radius + 0.3f && !escapeDoor.objectOpen)
            escapeDoor.ForceOpenAfterUnlock();
    }

    // The moment the door is shut he slams into it, then pounds on it a few more times, and that's the last of him.
    IEnumerator ShutOutRoutine()
    {
        _state = State.Gone;

        // Behind the shut door, out of sight.
        if (_agent.isOnNavMesh)
            _agent.ResetPath();
        _agent.enabled = false;
        foreach (Renderer r in GetComponentsInChildren<Renderer>())
            r.enabled = false;
        FadeOutMusic(musicCutOnFirstBang);

        Collider doorCollider = escapeDoor.GetComponentInChildren<Collider>();
        // On the player's face of the door: the door itself booms, it isn't heard through a wall.
        Vector3 bangPoint = (doorCollider != null ? doorCollider.bounds.center : escapeDoor.transform.position)
                            + escapeSide.forward * 0.15f;
        if (bangClips != null)
            for (int i = 0; i < bangClips.Length; i++)
            {
                AudioClip clip = bangClips[i];
                if (clip == null)
                    continue;
                if (i > 0)
                    yield return new WaitForSeconds(Random.Range(bangGap.x, bangGap.y));
                var go = new GameObject("Door Bang Audio");
                go.transform.position = bangPoint;
                var src = go.AddComponent<AudioSource>();
                src.playOnAwake = false;
                src.outputAudioMixerGroup = mixerGroup;
                src.spatialBlend = 1f;
                src.rolloffMode = AudioRolloffMode.Logarithmic;
                src.minDistance = bangMinDistance;
                src.maxDistance = 25f;
                src.dopplerLevel = 0f;
                src.pitch = Random.Range(0.94f, 1.02f);
                SpatialAudioCues.Ignore(src);
                src.PlayOneShot(clip, bangVolume);
                Destroy(go, clip.length / src.pitch + 0.25f);
                BangShake(bangPoint);
            }

        if (noCloseDoor != null)
            noCloseDoor.PlayerCanClose = true;
        while (_music != null || _owningEffects || _shake != null)
            yield return null;
        _routine = null;
        gameObject.SetActive(false);
    }

    void BangShake(Vector3 bangPoint)
    {
        float strength = 1f - Mathf.InverseLerp(bangMinDistance, bangShakeMaxDistance, Vector3.Distance(_player.position, bangPoint));
        if (strength <= 0f || bangShakeDegrees <= 0f)
            return;
        StopShake();
        Camera cam = Camera.main;
        CinemachineBrain brain = cam != null ? cam.GetComponent<CinemachineBrain>() : null;
        _shakeCam = brain != null ? brain.ActiveVirtualCamera as CinemachineCamera : null;
        if (_shakeCam == null)
            return;
        _shakeBaseDutch = _shakeCam.Lens.Dutch;
        _shake = StartCoroutine(ShakeRoutine(bangShakeDegrees * strength));
    }

    IEnumerator ShakeRoutine(float degrees)
    {
        float duration = Mathf.Max(0.05f, bangShakeDuration);
        for (float t = 0f; t < duration; t += Time.deltaTime)
        {
            float falloff = 1f - t / duration;
            SetShakeDutch(_shakeBaseDutch + Random.Range(-degrees, degrees) * falloff * falloff);
            yield return null;
        }
        _shake = null;
        StopShake();
    }

    void StopShake()
    {
        if (_shake != null)
            StopCoroutine(_shake);
        _shake = null;
        SetShakeDutch(_shakeBaseDutch);
        _shakeCam = null;
    }

    void SetShakeDutch(float dutch)
    {
        if (_shakeCam == null)
            return;
        LensSettings lens = _shakeCam.Lens;
        lens.Dutch = dutch;
        _shakeCam.Lens = lens;
    }

    void OpenDoorsInTheWay()
    {
        foreach (DoorController door in FindObjectsByType<DoorController>(FindObjectsSortMode.None))
            if (!door.IsOpen && Vector3.Distance(door.transform.position, transform.position) <= doorOpenDistance)
                door.RequestOpen(bypassLock: true);
    }

    // Same order as the Butcher's capture (FSMController.CapturePlayerRoutine / DoCaptureNow).
    IEnumerator CaptureRoutine(PlayerHider hider, bool hidden)
    {
        _state = State.Captured;
        FadeOutMusic(1f);
        if (_agent.isOnNavMesh)
        {
            _agent.isStopped = true;
            _agent.ResetPath();
        }
        _agent.velocity = Vector3.zero;

        if (JumpscareManager.Instance != null && JumpscareManager.Instance.IsJumpscarePlaying)
            yield break;
        JumpscareManager.BeginCaptureUi();

        if (hidden && hider.CurrentHidingSpot != null)
        {
            hider.CurrentHidingSpot.RevealPlayerForCapture(_player.gameObject);
            if (hideExitSecondsBeforeCapture > 0f)
                yield return new WaitForSeconds(hideExitSecondsBeforeCapture);
        }

        HidingSpot.EndHidingCameraForCapture();
        var rhythm = _player.GetComponent<PlayerFootstepRhythm>();
        if (rhythm != null)
            rhythm.enabled = false;

        if (SoundManager.Instance != null && SoundManager.Instance.tensionSFXBank != null)
        {
            AudioClip[] bank = SoundManager.Instance.tensionSFXBank;
            if (bank.Length > 0) SoundManager.Instance.PlaySFXAt(bank[0], transform.position);
            if (bank.Length > 1) SoundManager.Instance.PlaySFXAt(bank[1], transform.position);
        }

        if (!PlayerControlGate.IsAcquired(PlayerControlGate.ReasonCaptured))
            PlayerControlGate.Acquire(PlayerControlGate.ReasonCaptured);

        Vector3 toPlayer = Flat(_player.position - transform.position);
        if (toPlayer.sqrMagnitude > 1e-4f)
            transform.rotation = Quaternion.LookRotation(toPlayer);
        if (animator != null)
            animator.SetTrigger(captureTrigger);
        if (captureLight != null)
            captureLight.enabled = true;

        JumpscareManager manager = JumpscareManager.Instance;
        if (manager == null)
        {
            Debug.LogError("[WallStanderChase] No JumpscareManager - cannot run the capture.");
            yield break;
        }
        Transform anchor = jumpscareAnchor != null ? jumpscareAnchor : _head;
        _jumpscareCam = manager.jumpscareCam;
        if (_jumpscareCam != null)
        {
            _camFollow = _jumpscareCam.Follow;
            _camLookAt = _jumpscareCam.LookAt;
            _jumpscareCam.Follow = anchor;
            _jumpscareCam.LookAt = _head;
        }
        manager.TriggerJumpscare(anchor, null);
        _routine = null;
    }

    void ResetToWall()
    {
        if (_routine != null)
            StopCoroutine(_routine);
        _routine = null;

        if (_jumpscareCam != null)
        {
            _jumpscareCam.Follow = _camFollow;
            _jumpscareCam.LookAt = _camLookAt;
            _jumpscareCam = null;
        }

        if (_agent.enabled)
        {
            if (_agent.isOnNavMesh)
                _agent.ResetPath();
            _agent.enabled = false;
        }
        transform.SetPositionAndRotation(_startPosition, _startRotation);
        if (keepAwayBarrier != null)
            keepAwayBarrier.enabled = true;
        if (captureLight != null)
            captureLight.enabled = false;
        if (animator != null)
        {
            animator.Rebind();
            animator.Play(wallState, 0, 0f);
            animator.Update(0f);
        }
        StopMusic();
        ReleaseEffects();
        StopShake();
        _breached = false;
        _doorShutting = false;
        if (escapeDoor != null)
            escapeDoor.PlayerCanClose = true;
        _state = State.Waiting;
        _needsCageExit = true;
        _haveBarSpot = false;
    }

    static Vector3 Flat(Vector3 v)
    {
        v.y = 0f;
        return v;
    }
}
