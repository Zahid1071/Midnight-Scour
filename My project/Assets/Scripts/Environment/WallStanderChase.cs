using System.Collections;
using FS_Atmo;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Audio;
using UnityEngine.Serialization;

/// <summary>
/// The guy at the wall, once the bar behind him is taken: with Hold Chase Until External Start, turning away only
/// signals WouldStartChase (BasementBarEscapeTrap bangs the door shut for another screwdriver break). Call
/// BeginChaseNow when that lock finally breaks — then the lighter dies and he turns and screams. A second after the
/// scream ends (at once if they look back at him) he comes after them with the Butcher's run, faster than the player
/// walks but slower than they sprint. The cage room door can't be closed behind them. If he reaches the
/// corridor doorway before it's shut he shoves it back open and comes on faster than they can sprint.
/// Shutting it with him on the other side: he pounds on it while a shot shows the player where to hide, and keeps
/// pounding until they've hidden (or run out of time), then breaks in. Hidden, they're safe: he runs in, checks the
/// hiding spots (theirs last) without finding them, and goes back to his wall, closing the doors behind him through
/// the Butcher's door-aware Navigator. Out in the open, or leaving their spot in his sight, he runs them down.
/// Reaching them plays the Butcher's capture (axe swing, headbutt, bite) through the jumpscare camera and the normal
/// game over. Continue puts him back at the wall, waiting again once the player is out of the cage.
/// Coming back for the bar: once he's placed (the note was read), stepping into the corridor through the escape door
/// brings up the hiding spot's mic bar. It only shows until the reveal on him is over and the bar is taken; after
/// that, a loud noise sets him off just like turning away with the bar. Once he's after them it goes away as they
/// reach the escape door.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(NavMeshAgent))]
public class WallStanderChase : MonoBehaviour
{
    enum State { Waiting, Roaring, Chasing, Captured, Pounding, Searching, Returning, Gone }

    [SerializeField] CorpseVanishEvent wallStanderEvent;
    [SerializeField] Animator animator;
    [Tooltip("Turned off while he chases (it keeps the player off him while he stands at the wall).")]
    [SerializeField] Collider keepAwayBarrier;
    [SerializeField] AudioMixerGroup mixerGroup;

    [Header("Trigger")]
    [Tooltip("When on, turning away / mic noise only raises WouldStartChase — call BeginChaseNow after the escape lock break.")]
    [SerializeField] bool holdChaseUntilExternalStart = true;
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
    [Tooltip("Multiplies Roar Volume; above 1 plays the scream louder than the clip itself.")]
    [SerializeField] float roarBoost = 2f;
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
    [Tooltip("Loops from the moment he screams until he heads for the player's own hiding spot in the search.")]
    [SerializeField] AudioClip chaseMusic;
    [SerializeField] AudioMixerGroup musicGroup;
    [SerializeField, Range(0f, 1f)] float chaseMusicVolume = 1f;
    [SerializeField] float chaseMusicFadeIn = 0.4f;
    [Tooltip("Seconds it fades over once he heads for the player's hiding spot (or has caught them).")]
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
    [Tooltip("Getting this close (m, beyond his radius) to the escape door before it's shut gets him through; the closing door's obstacle keeps him from touching it.")]
    [SerializeField] float breachReach = 0.35f;
    [Tooltip("Metres per second once he's through the escape door before it shut; faster than the player sprints (4).")]
    [SerializeField] float breachSpeed = 5.5f;
    [SerializeField] float breachAcceleration = 20f;
    [Tooltip("Camera roll (degrees) each pound gives the player, full within Bang Min Distance of the door.")]
    [SerializeField] float bangShakeDegrees = 0.8f;
    [SerializeField] float bangShakeDuration = 0.35f;
    [Tooltip("No shake from this far away.")]
    [SerializeField] float bangShakeMaxDistance = 15f;

    [Header("Escape look-back reveal")]
    [Tooltip("Where he stands for the screwdriver look-back (cage doorframe). Empty = Exit Point.")]
    [SerializeField] Transform escapeRevealPoint;
    [Tooltip("World offset from Escape Reveal Point / Exit Point when placing him.")]
    [SerializeField] Vector3 escapeRevealOffset = new Vector3(0.7f, 0f, 0f);
    [Tooltip("Idle / stand animator state while revealed at the doorframe (not Wall Pose).")]
    [SerializeField] string escapeRevealState = "Base Blend";

    [Header("Shut out: hide, he breaks in and searches")]
    [Tooltip("Shows the player where to hide as the pounding starts.")]
    [SerializeField] HideOptionsShot hideShot;
    [Tooltip("Seconds after the shot before he breaks in, if the player hasn't hidden.")]
    [SerializeField] float hideTime = 10f;
    [Tooltip("Seconds after the player hides before he breaks in.")]
    [SerializeField] float breakInAfterHidden = 2.5f;
    [SerializeField] float breakInShakeDegrees = 1.6f;
    [Tooltip("Seconds he stands in the doorway after breaking in.")]
    [SerializeField] float doorwayPause = 0.8f;
    [Tooltip("Hiding spots he checks, the player's last. He never finds them there.")]
    [SerializeField] HidingSpot[] searchSpots;
    [SerializeField] float searchWait = 2.5f;
    [Tooltip("Seconds he lingers at the player's spot.")]
    [SerializeField] float searchWaitAtPlayer = 4f;
    [Tooltip("Leaving their spot in his sight within this distance brings him after them.")]
    [SerializeField] float spotDistance = 12f;
    [Tooltip("Loud on the mic while hidden, with their spot within this distance of him as he searches or heads home: he goes straight to it and drags them out.")]
    [SerializeField] float hearingDistance = 15f;
    [Tooltip("Metres per second while he searches and heads home. The Butcher's blend tree walks at 2.5 and runs from 3.")]
    [FormerlySerializedAs("walkSpeed")]
    [SerializeField] float searchSpeed = 3.6f;
    [Tooltip("Base layer state with the Butcher's idle / walk / jog blend tree, driven by Speed Parameter.")]
    [SerializeField] string locomotionState = "Base Blend";
    [SerializeField] string speedParameter = "Speed";
    [SerializeField] float walkBlend = 0.3f;

    [Header("Capture")]
    [Tooltip("Flat distance to the player at which he catches them.")]
    [SerializeField] float captureDistance = 1.2f;
    [Tooltip("Stopped within this distance (m, flat) of the player's hiding spot, as close as the navmesh lets him get, he drags them out.")]
    [SerializeField] float hiddenCaptureReach = 3f;
    [SerializeField] string captureTrigger = "Jumpscare";
    [Tooltip("Under his head bone, turned to match the Butcher's head, so the jumpscare camera sits where it does for the Butcher.")]
    [SerializeField] Transform jumpscareAnchor;
    [Tooltip("Kept off until the capture; the lighter is out by then, so this lights his face for the jumpscare.")]
    [SerializeField] Light captureLight;
    [Tooltip("Seconds between pulling the player out of a hiding spot and the capture.")]
    [SerializeField] float hideExitSecondsBeforeCapture = 1.5f;

    [Header("Mic bar on the way back for the bar")]
    [Tooltip("Half the corridor's width at the escape door, so walking past the doorway outside doesn't count.")]
    [SerializeField] float corridorHalfWidth = 1.5f;
    [Tooltip("Once he's after them, the bar goes away when the player is this close (m, flat) to the escape door.")]
    [SerializeField] float micRemoveDistance = 1f;
    [Tooltip("Tutorial card shown once, this many seconds after the bar first comes up.")]
    [SerializeField] float micTutorialDelay = 0.75f;
    [SerializeField] string micTutorialId = "Microphone";
    [SerializeField] string micTutorialTitleKey = "tutorial.mic.title";
    [SerializeField] string micTutorialBodyKey = "tutorial.mic.body";
    [Tooltip("Loops on the card. Empty = the mic icon.")]
    [SerializeField] UnityEngine.Video.VideoClip micTutorialVideo;
    [Tooltip("Card shown right after the mic card is closed, pointing at the mic sensitivity setting.")]
    [SerializeField] string micSettingsTutorialId = "Microphone Settings";
    [SerializeField] string micSettingsTutorialTitleKey = "tutorial.mic_settings.title";
    [SerializeField] string micSettingsTutorialBodyKey = "tutorial.mic_settings.body";
    [Tooltip("Loops on the settings card. Empty = the mic icon.")]
    [SerializeField] UnityEngine.Video.VideoClip micSettingsTutorialVideo;

    enum Mic { Off, Shown, Done }

    State _state;
    Mic _mic;
    HidingStamina _stamina;
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
    AudioSource _roarSource;
    float _musicTarget;
    float _musicRate;
    float _effectLevel;
    bool _owningEffects;
    KinoGlitch.AnalogGlitchController _glitch;
    float _baseAcceleration;
    bool _breached;
    bool _doorShutting;
    float _shutTime;
    Quaternion _leafLast;
    int _leafStill;
    Coroutine _shake;
    CinemachineCamera _shakeCam;
    float _shakeBaseDutch;
    Navigator _nav;
    DoorController _escapeDoorAI;
    Coroutine _hideShotRoutine;
    bool _hideShotDone;
    int _speedId;
    bool _hasSpeed;
    float _animSpeed;
    float _stillFor;
    string _locomotion;
    readonly System.Collections.Generic.List<Renderer> _hiddenRenderers = new System.Collections.Generic.List<Renderer>();

    public bool IsChasing => _state == State.Chasing;
    /// <summary>He's behind the shut escape door, pounding on it.</summary>
    public bool IsShutOut => _state == State.Pounding;
    /// <summary>Waiting, and hold-chase is on — walk-away / mic would normally start him.</summary>
    public bool IsHoldingForExternalStart =>
        holdChaseUntilExternalStart && _state == State.Waiting;
    /// <summary>The door is shut on him and the hide shot is over (the auto-save checkpoint).</summary>
    public event System.Action ShutOut;
    /// <summary>Player turned away / backed off / made noise while chase is held for the escape lock break.</summary>
    public event System.Action WouldStartChase;

    bool _wouldStartRaised;

    /// <summary>Starts the roar / run after the escape lock breakthrough (or deadline).</summary>
    public void BeginChaseNow()
    {
        if (_state != State.Waiting || _routine != null)
            return;
        _routine = StartCoroutine(RoarRoutine());
    }

    /// <summary>
    /// Moves him to the cage doorframe facing the player for the escape screwdriver look-back.
    /// Stays there until chase / reset.
    /// </summary>
    public void PlaceForEscapeReveal()
    {
        if (_state != State.Waiting)
            return;

        Transform anchor = escapeRevealPoint != null ? escapeRevealPoint : exitPoint;
        Vector3 spot = anchor != null
            ? anchor.position + escapeRevealOffset
            : transform.position + Vector3.left * 0.5f;
        spot.y = transform.position.y;
        if (NavMesh.SamplePosition(spot, out NavMeshHit hit, 2.5f, NavMesh.AllAreas))
            spot = hit.position;

        Vector3 face = _player != null
            ? Flat(_player.position - spot)
            : (escapeDoor != null ? Flat(escapeDoor.transform.position - spot) : -transform.right);
        if (face.sqrMagnitude < 1e-4f)
            face = Vector3.left;

        if (keepAwayBarrier != null)
            keepAwayBarrier.enabled = false;

        transform.SetPositionAndRotation(spot, Quaternion.LookRotation(face));
        if (animator != null)
        {
            animator.applyRootMotion = false;
            int hash = Animator.StringToHash(escapeRevealState);
            if (animator.HasState(0, hash))
            {
                animator.Play(escapeRevealState, 0, 0f);
                if (_hasSpeed)
                    animator.SetFloat(_speedId, 0f);
            }
            animator.Update(0f);
        }
        _locomotion = escapeRevealState;
        _stillFor = 0f;
    }

    void Awake()
    {
        _agent = GetComponent<NavMeshAgent>();
        _agent.enabled = false;
        _baseAcceleration = _agent.acceleration;
        _nav = GetComponent<Navigator>();
        if (_nav == null)
            _nav = null;
        _escapeDoorAI = escapeDoor != null ? escapeDoor.GetComponentInParent<DoorController>() : null;
        _speedId = Animator.StringToHash(speedParameter);
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
            {
                _hasRunSpeed |= p.nameHash == _runSpeedId && p.type == AnimatorControllerParameterType.Float;
                _hasSpeed |= p.nameHash == _speedId && p.type == AnimatorControllerParameterType.Float;
            }
        // Decode / build Steam Audio sources up front so the scream frame isn't a hitch.
        WarmChaseAudio();
    }

    /// <summary>
    /// Prefetch roar + chase music and build the roar source. Safe to call again during the
    /// escape lock window so Streaming/Compressed clips are Loaded before BeginChaseNow.
    /// </summary>
    public void WarmChaseAudio()
    {
        SoundManager.EnsureClipReady(roarClip);
        SoundManager.EnsureClipReady(chaseMusic);
        if (bangClips != null)
        {
            for (int i = 0; i < bangClips.Length; i++)
                SoundManager.EnsureClipReady(bangClips[i]);
        }
        EnsureRoarSource();
        EnsureMusicSource();
    }

    void OnEnable()
    {
        CaptureContinue.Applied += ResetToWall;
        if (_stamina != null)
            _stamina.NoiseAlert += OnNoise;
    }

    void OnDisable()
    {
        CaptureContinue.Applied -= ResetToWall;
        if (_stamina != null)
        {
            _stamina.NoiseAlert -= OnNoise;
            _stamina.SetOpenListening(false);
        }
        ReleaseEffects();
        StopMusic();
        StopShake();
        StopHideShot();
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
            && (_state != State.Waiting || (wallStanderEvent != null && wallStanderEvent.BarTaken)))
            noCloseDoor.PlayerCanClose = false;

        FindStamina();
        TickMic();
        if (_state == State.Waiting)
            TickTrigger();
        else if (_state == State.Chasing)
            TickChase();
        else if (_state == State.Searching || _state == State.Returning)
            TickSearch();
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
            RaiseOrStartChase();
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
        RaiseOrStartChase();
    }

    void RaiseOrStartChase()
    {
        if (holdChaseUntilExternalStart)
        {
            if (_wouldStartRaised)
                return;
            _wouldStartRaised = true;
            // Escape lock still ahead — use that window to finish audio decode / Steam setup.
            WarmChaseAudio();
            WouldStartChase?.Invoke();
            return;
        }
        _routine = StartCoroutine(RoarRoutine());
    }

    void FindStamina()
    {
        if (_stamina != null)
            return;
        _stamina = FindFirstObjectByType<HidingStamina>();
        if (_stamina != null)
            _stamina.NoiseAlert += OnNoise;
    }

    /// <summary>Checkpoint / outdoor Continue: mic bar stays off for the rest of the run.</summary>
    public void MarkMicDone()
    {
        _mic = Mic.Done;
        if (_stamina != null)
            _stamina.SetOpenListening(false);
        else
        {
            FindStamina();
            _stamina?.SetOpenListening(false);
        }
    }

    void TickMic()
    {
        if (_mic == Mic.Done || wallStanderEvent == null || escapeSide == null || _stamina == null)
            return;

        if (_mic == Mic.Off)
        {
            if (!wallStanderEvent.WallStanderShown)
                return;

            bool inCorridor = InCorridor();
            // Mid-return checkpoint (reveal done, bar still there): bar is already past the corridor.
            bool midReturnNoBar = wallStanderEvent.WallRevealPlayed && !wallStanderEvent.BarTaken;
            // After bar, only while still waiting on him in the basement — never outdoors / post-escape.
            bool postBarWait = wallStanderEvent.WallRevealPlayed && wallStanderEvent.BarTaken
                               && _state == State.Waiting && !IsOutOfBasement();

            if (!inCorridor && !midReturnNoBar && !postBarWait)
            {
                // Leave-basement Continue: WallRevealPlayed is true outdoors — lock the bar off.
                if (wallStanderEvent.WallRevealPlayed && wallStanderEvent.BarTaken)
                    MarkMicDone();
                return;
            }

            _mic = Mic.Shown;
            _stamina.SetOpenListening(true);
            if (TutorialPopupUI.ShowOnce(micTutorialId, micTutorialTitleKey, micTutorialBodyKey,
                    micTutorialVideo, _stamina.MicrophoneIcon, micTutorialDelay))
                TutorialPopupUI.ShowOnce(micSettingsTutorialId, micSettingsTutorialTitleKey, micSettingsTutorialBodyKey,
                    micSettingsTutorialVideo, _stamina.MicrophoneIcon, micTutorialDelay, micTutorialId);
        }

        bool after = _state != State.Waiting && _state != State.Captured;
        if (_state == State.Pounding || _state == State.Gone
            || (after && Flat(_player.position - escapeSide.position).magnitude <= micRemoveDistance)
            || (wallStanderEvent.BarTaken && wallStanderEvent.WallRevealOver && IsOutOfBasement()))
        {
            MarkMicDone();
        }
    }

    bool IsOutOfBasement()
    {
        ButcherWindowShot shot = FindFirstObjectByType<ButcherWindowShot>(FindObjectsInactive.Include);
        if (shot == null || _player == null)
            return false;
        Camera cam = Camera.main;
        Vector3 eye = cam != null ? cam.transform.position : _player.position + Vector3.up * 1.6f;
        return shot.IsOutOfBasement(eye);
    }

    bool InCorridor()
    {
        Vector3 offset = _player.position - escapeSide.position;
        return Vector3.Dot(offset, escapeSide.forward) < 0f
               && Mathf.Abs(Vector3.Dot(offset, escapeSide.right)) <= corridorHalfWidth;
    }

    // Loud on the mic once the reveal on him is over and the bar is in hand: he hears them and comes.
    void OnNoise()
    {
        if (Searching)
        {
            HeardInHiding();
            return;
        }
        if (_mic != Mic.Shown || _state != State.Waiting || !wallStanderEvent.WallRevealOver || !wallStanderEvent.BarTaken
            || PlayerControlGate.IsInputBlocked || ThunderManager.ArePlayerNoisesMasked
            || _player == null || Cage.IsPlayerInsideAnyCage(_player))
            return;
        RaiseOrStartChase();
    }

    // Loud in their spot while he searches or heads home: he runs to it and the chase drags them out.
    void HeardInHiding()
    {
        HidingSpot spot = PlayerSpot();
        if (spot == null || !_agent.enabled || ThunderManager.ArePlayerNoisesMasked)
            return;
        Transform pivot = spot.GetSearchPivot();
        Vector3 noisePos = pivot != null ? pivot.position : spot.transform.position;
        if (Vector3.Distance(transform.position, noisePos) > hearingDistance)
            return;
        CaptureContext.Set(CaptureReason.HideNoise);
        BreakIntoChase();
    }

    IEnumerator RoarRoutine()
    {
        _state = State.Roaring;
        WarmChaseAudio();

        // Finish any in-flight decode before Play — assigning a still-loading clip freezes the main thread.
        float wait = 0f;
        while (wait < 1.5f
               && ((roarClip != null && roarClip.loadState == AudioDataLoadState.Loading)
                   || (chaseMusic != null && chaseMusic.loadState == AudioDataLoadState.Loading)))
        {
            wait += Time.unscaledDeltaTime;
            yield return null;
        }

        HeldLighter lighter = FindFirstObjectByType<HeldLighter>(FindObjectsInactive.Include);
        if (lighter != null)
        {
            if (lighter.IsLit)
                lighter.Extinguish();
            lighter.MisfireNextStrikes(lighterMisfires);
        }

        // Music first, then roar on the next frame so crossfade + scream aren't one hitch.
        StartMusic();
        yield return null;

        if (roarClip != null)
        {
            EnsureRoarSource();
            if (_roarSource != null)
                _roarSource.PlayOneShot(roarClip, roarVolume * roarBoost);
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

    void EnsureRoarSource()
    {
        if (_roarSource != null || roarClip == null)
            return;
        Transform mouth = _head != null ? _head : transform;
        _roarSource = SpatialAudio.Create("Wall Stander Roar Audio", mouth, mouth.position, mixerGroup,
            roarMinDistance, 30f, atmospheric: false);
        _roarSource.playOnAwake = false;
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
        // Return scare walk-cap ends once he actually chases.
        if (wallStanderEvent != null)
            wallStanderEvent.SetReturnWalkSlow(false);
        if (keepAwayBarrier != null)
            keepAwayBarrier.enabled = false;
        _locomotion = null;
        _stillFor = 0f;
        PlayLocomotion(runState, runBlend);

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
        TickChase();
    }

    void EnsureMusicSource()
    {
        if (chaseMusic == null || _music != null)
            return;
        var go = new GameObject("Wall Stander Chase Music");
        go.transform.SetParent(transform, false);
        _music = go.AddComponent<AudioSource>();
        _music.playOnAwake = false;
        _music.loop = true;
        _music.spatialBlend = 0f;
        _music.outputAudioMixerGroup = musicGroup;
        _music.clip = chaseMusic;
        _music.volume = 0f;
    }

    void StartMusic()
    {
        // Route through SoundManager so chase never stacks with basement / house beds.
        if (chaseMusic == null || SoundManager.Instance == null)
            return;
        SoundManager.EnsureClipReady(chaseMusic);
        SoundManager.Instance.SetChaseMusicActive(true, chaseMusic);
        // Keep legacy local source silent if it was created earlier.
        if (_music != null)
        {
            _music.Stop();
            _musicTarget = 0f;
        }
    }

    void FadeOutMusic(float seconds)
    {
        if (SoundManager.Instance != null && SoundManager.Instance.IsChaseMusicActive)
            SoundManager.Instance.SetChaseMusicActive(false);
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
        if (SoundManager.Instance != null && SoundManager.Instance.IsChaseMusicActive)
            SoundManager.Instance.SetChaseMusicActive(false);
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
        else if ((_state == State.Roaring || _state == State.Chasing || Searching) && _player != null)
            target = 1f - Mathf.InverseLerp(effectMinDistance, effectMaxDistance,
                Vector3.Distance(transform.position, _player.position));
        else if (_state == State.Gone || _state == State.Pounding)
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

    KinoGlitch.AnalogGlitchController Glitch()
    {
        if (_glitch != null)
            return _glitch;
        Camera cam = Camera.main;
        if (cam == null)
            return null;
        _glitch = cam.GetComponent<KinoGlitch.AnalogGlitchController>();
        return _glitch;
    }

    void TickChase()
    {
        if (!_agent.isOnNavMesh)
            return;

        if (!_breached && escapeDoor != null && escapeSide != null && TickEscapeDoor())
        {
            _routine = StartCoroutine(PoundRoutine());
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

        TickChaseAnimation();

        Vector3 offset = target - transform.position;
        float flat = Flat(offset).magnitude;
        bool reached = flat <= captureDistance + (hidden ? 0.5f : 0f);
        // The spot's pivot can sit off the navmesh, inside the hiding place: stopped as close as he can get counts.
        if (hidden && !reached && flat <= hiddenCaptureReach)
            reached = _stillFor >= 0.3f
                      || (!_agent.pathPending && _agent.remainingDistance <= _agent.stoppingDistance + 0.3f);
        if (reached && Mathf.Abs(offset.y) < 1.5f)
            _routine = StartCoroutine(CaptureRoutine(hider, hidden));
    }

    // The Butcher's run while he's moving; held up (a door, the end of his path) he drops to the idle of the blend tree.
    void TickChaseAnimation()
    {
        float speed = _agent.velocity.magnitude;
        _stillFor = speed < 0.3f ? _stillFor + Time.deltaTime : 0f;
        bool still = _stillFor >= 0.2f;
        PlayLocomotion(still ? locomotionState : runState, still ? walkBlend : runBlend);
        if (animator == null)
            return;
        if (still && _hasSpeed)
            animator.SetFloat(_speedId, 0f);
        if (_hasRunSpeed)
            animator.SetFloat(_runSpeedId, Mathf.Clamp(speed / Mathf.Max(0.01f, runClipSpeed), 0.6f, 2f));
    }

    float SideOfEscape(Vector3 point) => Vector3.Dot(point - escapeSide.position, escapeSide.forward);

    // True once the escape door has finished swinging shut with him still on the far side → pound.
    // He only "makes it through" if he is already clearly on the player's side when it shuts.
    bool TickEscapeDoor()
    {
        if (escapeDoor == null)
            return false;

        if (escapeDoor.objectOpen)
        {
            _doorShutting = false;
            return false;
        }

        float his = SideOfEscape(transform.position);
        // Clearly past the door plane onto the escape side — he got through before it shut.
        if (his > Mathf.Max(0.25f, _agent != null ? _agent.radius : 0.25f))
        {
            MakeItThrough(his);
            return false;
        }

        // Door is shut and he is still on the corridor / cage side → wait for it to settle, then pound.
        // Do not open it from this side.
        Transform leaf = escapeDoor.transform;
        if (!_doorShutting)
        {
            _doorShutting = true;
            _shutTime = 0f;
            _leafLast = leaf.rotation;
            _leafStill = 0;
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
        if (escapeDoor != null)
            escapeDoor.PlayerCanClose = false;
        // Only shove open if he's already through — never from the far side.
        if (side > 0f && escapeDoor != null && !escapeDoor.objectOpen)
        {
            escapeDoor.ForceOpenAfterUnlock();
            if (_escapeDoorAI != null)
                _escapeDoorAI.ForceOpenInstant();
        }
    }

    /// <summary>
    /// Checkpoint load: back to him pounding on the shut escape door, the hide shot already seen. The player gets the
    /// usual time to hide before he breaks in.
    /// </summary>
    public void RestoreShutOut()
    {
        if (escapeDoor == null || escapeSide == null)
            return;
        if (_player == null)
        {
            GameObject p = GameObject.FindWithTag("Player");
            if (p == null)
                return;
            _player = p.transform;
        }
        if (_routine != null)
            StopCoroutine(_routine);
        if (keepAwayBarrier != null)
            keepAwayBarrier.enabled = false;
        _mic = Mic.Done;
        if (_stamina != null)
            _stamina.SetOpenListening(false);
        _breached = false;
        _doorShutting = false;
        transform.SetPositionAndRotation(escapeSide.position - escapeSide.forward * 1.2f,
            Quaternion.LookRotation(Flat(escapeSide.forward)));
        StartMusic();
        _routine = StartCoroutine(PoundRoutine(resumed: true));
    }

    // The moment the door is shut he slams into it and keeps pounding while the shot shows the player where to hide.
    // Once they've hidden (or had their chance) he breaks in. Resumed from a checkpoint, the slam and the shot are past.
    IEnumerator PoundRoutine(bool resumed = false)
    {
        _state = State.Pounding;

        // Behind the shut door, out of sight.
        if (_agent.isOnNavMesh)
            _agent.ResetPath();
        _agent.enabled = false;
        SetVisible(false);

        Collider doorCollider = escapeDoor.GetComponentInChildren<Collider>();
        // On the player's face of the door: the door itself booms, it isn't heard through a wall.
        Vector3 bangPoint = (doorCollider != null ? doorCollider.bounds.center : escapeDoor.transform.position)
                            + escapeSide.forward * 0.15f;

        _hideShotDone = resumed || hideShot == null;
        bool checkpointed = resumed;
        int bangs = resumed ? 1 : 0;
        float nextBang = resumed ? Time.time + bangGap.y : 0f;
        float waited = 0f;
        float hiddenFor = 0f;
        while (!escapeDoor.objectOpen)
        {
            if (Time.time >= nextBang)
            {
                Bang(bangs, bangPoint, bangShakeDegrees);
                bangs++;
                nextBang = Time.time + Random.Range(bangGap.x, bangGap.y);
                if (bangs == 1 && hideShot != null)
                    _hideShotRoutine = StartCoroutine(HideShotRoutine());
            }
            if (_hideShotDone)
            {
                if (!checkpointed)
                {
                    checkpointed = true;
                    ShutOut?.Invoke();
                }
                waited += Time.deltaTime;
                hiddenFor = PlayerHidden() ? hiddenFor + Time.deltaTime : 0f;
                if (hiddenFor >= breakInAfterHidden || waited >= hideTime)
                    break;
            }
            yield return null;
        }

        // He bursts through.
        StopHideShot();
        Bang(bangs, bangPoint, breakInShakeDegrees);
        escapeDoor.ForceOpenAfterUnlock();
        if (_escapeDoorAI != null)
            _escapeDoorAI.ForceOpenInstant();
        escapeDoor.PlayerCanClose = false;

        Vector3 entry = escapeSide.position - escapeSide.forward * 1.2f;
        if (NavMesh.SamplePosition(entry, out NavMeshHit hit, 1.5f, NavMesh.AllAreas))
            entry = hit.position;
        transform.SetPositionAndRotation(entry, Quaternion.LookRotation(Flat(escapeSide.forward)));
        SetVisible(true);
        if (keepAwayBarrier != null)
            keepAwayBarrier.enabled = false;
        _agent.enabled = true;
        _agent.Warp(entry);
        _agent.speed = searchSpeed;
        _agent.acceleration = _baseAcceleration;
        _agent.isStopped = false;
        _breached = true;
        _locomotion = null;
        _animSpeed = 0f;
        if (animator != null && _hasSpeed)
            animator.SetFloat(_speedId, 0f);
        PlayLocomotion(locomotionState);

        if (!PlayerHidden())
        {
            BreakIntoChase();
            yield break;
        }

        // Hidden: he runs in, checks the spots (theirs last) and never finds them.
        _state = State.Searching;
        yield return Linger(doorwayPause, null);
        System.Collections.Generic.List<HidingSpot> order = SearchOrder();
        HidingSpot theirs = PlayerSpot();
        foreach (HidingSpot spot in order)
        {
            if (spot == theirs)
                FadeOutMusic(chaseMusicFadeOut);
            Transform pivot = spot.GetSearchPivot();
            yield return WalkTo(pivot != null ? pivot.position : spot.transform.position, 40f);
            Transform look = spot.ResolveSearchLookTarget();
            Vector3 lookAt = look != null && look != pivot ? look.position : BoundsCenter(spot.transform);
            yield return Linger(spot == theirs ? searchWaitAtPlayer : searchWait, lookAt);
        }

        // Back to his wall, closing the doors behind him.
        _state = State.Returning;
        Vector3 home = _startPosition;
        if (NavMesh.SamplePosition(_startPosition, out NavMeshHit homeHit, 1.5f, NavMesh.AllAreas))
            home = homeHit.position;
        yield return WalkTo(home, 90f);

        if (_agent.isOnNavMesh)
            _agent.ResetPath();
        _agent.enabled = false;
        PlayLocomotion(wallState);
        Vector3 fromPos = transform.position;
        Quaternion fromRot = transform.rotation;
        for (float t = 0f; t < 0.6f; t += Time.deltaTime)
        {
            float u = Mathf.SmoothStep(0f, 1f, t / 0.6f);
            transform.SetPositionAndRotation(Vector3.Lerp(fromPos, _startPosition, u), Quaternion.Slerp(fromRot, _startRotation, u));
            yield return null;
        }
        transform.SetPositionAndRotation(_startPosition, _startRotation);
        if (keepAwayBarrier != null)
            keepAwayBarrier.enabled = true;
        _state = State.Gone;
        if (noCloseDoor != null)
            noCloseDoor.PlayerCanClose = true;
        escapeDoor.PlayerCanClose = true;
        _routine = null;
    }

    IEnumerator HideShotRoutine()
    {
        yield return hideShot.Play();
        _hideShotRoutine = null;
        _hideShotDone = true;
    }

    void StopHideShot()
    {
        if (_hideShotRoutine != null)
            StopCoroutine(_hideShotRoutine);
        _hideShotRoutine = null;
        if (hideShot != null)
            hideShot.Stop();
    }

    bool Searching => _state == State.Searching || _state == State.Returning;

    // Walks there through the Butcher's Navigator: around by the room graph, opening each door on the way and
    // closing it once through.
    IEnumerator WalkTo(Vector3 point, float timeout)
    {
        if (!_agent.isOnNavMesh)
            yield break;
        if (_nav != null)
            _nav.GoToPoint(point);
        else
            _agent.SetDestination(point);
        yield return null;
        float t = 0f;
        for (; t < timeout && Searching; t += Time.deltaTime)
        {
            bool arrived = _nav != null
                ? !_nav.IsFollowingRoute
                : !_agent.pathPending && _agent.remainingDistance <= _agent.stoppingDistance + 0.2f;
            if (arrived)
                break;
            yield return null;
        }
        if (!Searching || !_agent.isOnNavMesh)
            yield break;
        if (_nav != null)
            _nav.ClearRoute();

        // Short of it (a route given up on the way): the rest of the way straight there.
        if (Flat(point - transform.position).magnitude > 0.6f)
        {
            _agent.SetDestination(point);
            yield return null;
            for (; t < timeout && Searching && _agent.isOnNavMesh; t += Time.deltaTime)
            {
                if (!_agent.pathPending && _agent.remainingDistance <= _agent.stoppingDistance + 0.2f)
                    break;
                yield return null;
            }
        }
        if (Searching && _agent.isOnNavMesh)
            _agent.ResetPath();
    }

    // Stands looking around, turned toward lookAt if there is one.
    IEnumerator Linger(float seconds, Vector3? lookAt)
    {
        Quaternion from = transform.rotation;
        Quaternion to = from;
        if (lookAt.HasValue)
        {
            Vector3 dir = Flat(lookAt.Value - transform.position);
            if (dir.sqrMagnitude > 1e-4f)
                to = Quaternion.LookRotation(dir);
        }
        for (float t = 0f; t < seconds && Searching; t += Time.deltaTime)
        {
            transform.rotation = Quaternion.Slerp(from, to, Mathf.SmoothStep(0f, 1f, t / 0.6f));
            yield return null;
        }
    }

    // The spots he checks: the free ones nearest first, then the player's (whichever it is).
    System.Collections.Generic.List<HidingSpot> SearchOrder()
    {
        var order = new System.Collections.Generic.List<HidingSpot>();
        HidingSpot theirs = PlayerSpot();
        if (searchSpots != null)
            foreach (HidingSpot spot in searchSpots)
                if (spot != null && spot != theirs && !order.Contains(spot))
                    order.Add(spot);
        Vector3 from = transform.position;
        order.Sort((a, b) => (a.transform.position - from).sqrMagnitude.CompareTo((b.transform.position - from).sqrMagnitude));
        if (theirs != null)
            order.Add(theirs);
        return order;
    }

    HidingSpot PlayerSpot()
    {
        PlayerHider hider = _player != null ? _player.GetComponent<PlayerHider>() : null;
        return hider != null && hider.isHidden ? hider.CurrentHidingSpot : null;
    }

    bool PlayerHidden() => PlayerSpot() != null;

    // The Butcher's idle / walk blend while he searches and heads home, fed his speed the way the Butcher's
    // InteractionController feeds it; out of their spot in his sight, he runs them down.
    void TickSearch()
    {
        if (!_agent.enabled)
            return;
        PlayLocomotion(locomotionState);
        _animSpeed = Mathf.Lerp(_animSpeed, _agent.velocity.magnitude, Time.deltaTime * 6f);
        if (_animSpeed < 0.01f)
            _animSpeed = 0f;
        if (animator != null && _hasSpeed)
            animator.SetFloat(_speedId, _animSpeed);

        if (!PlayerHidden() && SeesPlayer())
            BreakIntoChase();
    }

    void PlayLocomotion(string state) => PlayLocomotion(state, walkBlend);

    void PlayLocomotion(string state, float blend)
    {
        if (state == _locomotion)
            return;
        _locomotion = state;
        if (animator == null || string.IsNullOrEmpty(state) || !animator.HasState(0, Animator.StringToHash(state)))
            return;
        animator.applyRootMotion = false;
        animator.CrossFadeInFixedTime(state, blend, 0);
    }

    bool SeesPlayer()
    {
        Camera cam = Camera.main;
        if (cam == null)
            return false;
        Vector3 eye = _head != null ? _head.position : transform.position + Vector3.up * 1.6f;
        Vector3 target = cam.transform.position;
        Vector3 toPlayer = target - eye;
        if (toPlayer.magnitude > spotDistance || Vector3.Angle(Flat(transform.forward), Flat(toPlayer)) > 75f)
            return false;
        RaycastHit[] hits = Physics.RaycastAll(eye, toPlayer.normalized, toPlayer.magnitude, ~0, QueryTriggerInteraction.Ignore);
        foreach (RaycastHit h in hits)
            if (!h.transform.IsChildOf(transform) && !h.transform.IsChildOf(_player) && !h.transform.IsChildOf(cam.transform))
                return false;
        return true;
    }

    // Seen, or not hidden when he gets in: the Butcher's run, after them.
    void BreakIntoChase()
    {
        if (_routine != null)
            StopCoroutine(_routine);
        _routine = null;
        StopHideShot();
        if (_nav != null && _agent.isOnNavMesh)
            _nav.ClearRoute();
        _locomotion = null;
        _stillFor = 0f;
        PlayLocomotion(runState, runBlend);
        _agent.speed = chaseSpeed;
        _agent.acceleration = _baseAcceleration;
        _agent.isStopped = false;
        _nextRepath = 0f;
        _breached = true;
        _state = State.Chasing;
        StartMusic();
    }

    void SetVisible(bool visible)
    {
        if (!visible)
        {
            _hiddenRenderers.Clear();
            foreach (Renderer r in GetComponentsInChildren<Renderer>())
                if (r.enabled)
                {
                    r.enabled = false;
                    _hiddenRenderers.Add(r);
                }
            return;
        }
        foreach (Renderer r in _hiddenRenderers)
            if (r != null)
                r.enabled = true;
        _hiddenRenderers.Clear();
    }

    static Vector3 BoundsCenter(Transform t)
    {
        Collider c = t.GetComponentInChildren<Collider>();
        return c != null ? c.bounds.center : t.position;
    }

    void Bang(int index, Vector3 bangPoint, float shakeDegrees)
    {
        if (bangClips == null || bangClips.Length == 0)
            return;
        AudioClip clip = bangClips[index % bangClips.Length];
        if (clip == null)
            return;
        SpatialAudio.PlayAt(clip, bangPoint, bangVolume, bangMinDistance, 25f, mixerGroup, null, Random.Range(0.94f, 1.02f));
        BangShake(bangPoint, shakeDegrees);
    }

    void BangShake(Vector3 bangPoint, float degrees)
    {
        float strength = 1f - Mathf.InverseLerp(bangMinDistance, bangShakeMaxDistance, Vector3.Distance(_player.position, bangPoint));
        if (strength <= 0f || degrees <= 0f)
            return;
        StopShake();
        Camera cam = Camera.main;
        CinemachineBrain brain = cam != null ? cam.GetComponent<CinemachineBrain>() : null;
        _shakeCam = brain != null ? brain.ActiveVirtualCamera as CinemachineCamera : null;
        if (_shakeCam == null)
            return;
        _shakeBaseDutch = _shakeCam.Lens.Dutch;
        _shake = StartCoroutine(ShakeRoutine(degrees * strength));
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
        {
            if (door == null || door.IsOpen)
                continue;
            // Never AI-open the escape door while shut-out / pounding — that skips the bash.
            if (door == _escapeDoorAI && !_breached)
                continue;
            if (Vector3.Distance(door.transform.position, transform.position) <= doorOpenDistance)
                door.RequestOpen(bypassLock: true);
        }
    }

    // Same order as the Butcher's capture (FSMController.CapturePlayerRoutine / DoCaptureNow).
    IEnumerator CaptureRoutine(PlayerHider hider, bool hidden)
    {
        _state = State.Captured;
        FadeOutMusic(chaseMusicFadeOut);
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
            PlayLocomotion(locomotionState, walkBlend);
            if (animator != null && _hasSpeed)
                animator.SetFloat(_speedId, 0f);
            Vector3 toSpot = Flat(hider.CurrentHidingSpot.transform.position - transform.position);
            if (toSpot.sqrMagnitude > 1e-4f)
                transform.rotation = Quaternion.LookRotation(toSpot);
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
        if (_state == State.Gone)
            return;
        if (_routine != null)
            StopCoroutine(_routine);
        _routine = null;
        StopHideShot();
        if (_nav != null && _agent.enabled && _agent.isOnNavMesh)
            _nav.ClearRoute();
        SetVisible(true);

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
        _locomotion = wallState;
        _stillFor = 0f;
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
        _wouldStartRaised = false;
        if (_mic == Mic.Done)
            _mic = Mic.Off;
    }

    static Vector3 Flat(Vector3 v)
    {
        v.y = 0f;
        return v;
    }
}
