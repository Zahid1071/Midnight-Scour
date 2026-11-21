using System.Collections;
using System.Collections.Generic;
using FS_Atmo;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.Events;

/// <summary>
/// Cage room corpse scare (scripted):
/// 1. First padlock pin: a faint scratch from the body.
/// 2. Leave the cage: cloth rustle only — the body stays lying there. The bar is visible but not pickable yet.
/// 3. First leave through the cage-room doorway: no lights-out. Unlock the corridor door (no look-back scream),
///    walk out; room lights come on via CorridorTension.
/// 4. Read the note (calls ShowWallStander) → go back. Corridor door trap / stuck is handled separately.
/// 5. Re-enter the cage room: lights flicker. Step into the cage: he sits up, then lights snap out with the gust /
///    run-away beat. Body is gone; wall stander appears (if the note already called). Bar becomes pickable.
/// 6. Lit lighter reveal / bar look cinematic still runs when the player finds him at the wall.
/// </summary>
[DisallowMultipleComponent]
public class CorpseVanishEvent : MonoBehaviour
{
    public enum Stage
    {
        WaitingForPin,
        WaitingForExit,
        WaitingForDoor,
        WaitingForNextDoor,
        WaitingForReturn,
        WaitingForLightsOut,
        Done
    }

    const string CinematicLockReason = "corpse_wall_reveal";

    [Header("Body")]
    [Tooltip("Hidden when the lights go out.")]
    [SerializeField] Renderer[] bodyRenderers;
    [Tooltip("Point the view turns to; sounds and the run start here. Empty = centre of the body renderers.")]
    [SerializeField] Transform lookTarget;

    [Header("1. First padlock pin")]
    [Tooltip("Played from the body; keep it on the edge of hearing.")]
    [SerializeField] AudioClip pinClue;
    [SerializeField, Range(0f, 1f)] float pinClueVolume = 0.5f;
    [Tooltip("Full volume up to this distance from the body; the player is ~4.5 m away at the padlock.")]
    [SerializeField] float pinClueMinDistance = 3f;
    [Tooltip("Seconds after the pin sets before the clue.")]
    [SerializeField] float pinClueDelay = 0.8f;

    [Header("2. Leaving the cage")]
    [SerializeField] SimpleOpenClose cageDoor;
    [Tooltip("Seconds the player has to be out of the cage before it counts (trigger edges flicker).")]
    [SerializeField] float exitConfirmTime = 0.3f;
    [Tooltip("Cloth rustle from the body once the player has walked past it toward the door with their back turned.")]
    [SerializeField] AudioClip exitClue;
    [SerializeField, Range(0f, 1f)] float exitClueVolume = 0.4f;
    [Tooltip("Optional second rustle/shift after the first. Empty = nothing.")]
    [SerializeField] AudioClip exitFollowUpClip;
    [SerializeField, Range(0f, 1f)] float exitFollowUpVolume = 1f;
    [Tooltip("Seconds after the first rustle before the follow-up.")]
    [SerializeField] float exitFollowUpDelay = 0.8f;
    [Tooltip("How far past the body (m, toward the door) before the rustle can play.")]
    [SerializeField] float rustlePastBodyDistance = 1.2f;
    [Tooltip("Player view must be at least this many degrees away from the body for the rustle.")]
    [SerializeField] float rustleLookAwayAngle = 100f;
    [Tooltip("Starts sitting up under the sheet when the player re-enters the cage room on the return scare.")]
    [SerializeField] CorpseSitUp sitUp;
    [Tooltip("Played from the body as he sits up (Steam Audio). Empty = nothing.")]
    [SerializeField] AudioClip sitUpClip;
    [SerializeField, Range(0f, 1f)] float sitUpVolume = 1f;
    [Tooltip("Flat horror sting the moment sit-up starts (not spatial).")]
    [SerializeField] AudioClip sitUpStingClip;
    [SerializeField, Range(0f, 1f)] float sitUpStingVolume = 0.9f;
    [Tooltip("Lights snap out when sit-up reaches this fraction (0-1). 0 = use Sit Up Hold Before Lights Out.")]
    [SerializeField, Range(0f, 1f)] float lightsOutAtSitUpNormalized = 0.75f;
    [Tooltip("Absolute seconds before lights out if Lights Out At Sit Up Normalized is 0. 0 = end of sit-up.")]
    [SerializeField] float sitUpHoldBeforeLightsOut = 0f;
    [Tooltip("Pull the sit-up lights-out earlier by this many seconds.")]
    [SerializeField] float sitUpLightsOutEarlyBy = 1f;
    [Tooltip("On/off pulse durations for the sit-up kill flicker (must end off — last state is dark).")]
    [SerializeField] float[] sitUpKillFlickerPattern = { 0.05f, 0.08f, 0.04f, 0.12f, 0.05f, 0.09f, 0.04f, 0.15f };

    [Header("3. Leaving through the cage room doorway, then the next door")]
    [Tooltip("The cage room's doorframe; the side the body is on counts as inside the room.")]
    [SerializeField] Transform exitDoorway;
    [Tooltip("Half the width of the opening, measured along the doorframe.")]
    [SerializeField] float doorwayHalfWidth = 0.8f;
    [Tooltip("Metres past the doorframe the player has to be before it counts as crossed.")]
    [SerializeField] float doorwayCrossDepth = 0.3f;
    [Tooltip("Played the moment the player reaches the doorframe after leaving the cage. Empty = nothing.")]
    [SerializeField] AudioClip doorwayClip;
    [SerializeField, Range(0f, 1f)] float doorwayClipVolume = 1f;
    [Tooltip("Where it plays from. Empty = flat, not from a point.")]
    [SerializeField] Transform doorwayClipPoint;
    [Tooltip("Once through the doorway, the lights go out when the player gets this close to it. Empty = as the doorway is crossed.")]
    [SerializeField] Transform nextDoor;
    [SerializeField] float nextDoorDistance = 2f;
    [Tooltip("Seconds after the trigger before the lights go out.")]
    [SerializeField] float lightsOutDelay = 0.35f;
    [Tooltip("Lit candles within this distance of the body or the player go out with the lighter.")]
    [SerializeField] float candleRadius = 12f;
    [Tooltip("Renderers that get the highlight material added when the lights go out (e.g. the screwdriver).")]
    [SerializeField] Renderer[] highlightOnLightsOut;
    [SerializeField] Material highlightMaterial;
    [Tooltip("Gust past the player that puts the flame out. Empty = the lights just go out.")]
    [SerializeField] AudioClip blowOutClip;
    [SerializeField, Range(0f, 1f)] float blowOutVolume = 0.9f;
    [SerializeField] Vector3 blowOutOffset = new Vector3(-0.3f, 0.05f, -0.25f);
    [SerializeField] float blowOutMinDistance = 1.5f;
    [Tooltip("Seconds of the gust before the flames die, so it's heard pushing them out.")]
    [SerializeField] float blowOutLead = 0.6f;
    [Tooltip("Strikes of the lighter that only spark after the lights go out; the next one catches.")]
    [SerializeField] int lighterMisfires = 3;

    [Header("Run")]
    [Tooltip("Seconds after the lights go out before the steps start.")]
    [SerializeField] float runDelay = 0.3f;
    [SerializeField] AudioClip riseClip;
    [SerializeField, Range(0f, 1f)] float riseVolume = 0.8f;
    [SerializeField] AudioClip runClip;
    [SerializeField, Range(0f, 1f)] float runVolume = 1f;
    [Tooltip("The steps stay at full volume up to this distance, so the run reads clearly across the room.")]
    [SerializeField] float runMinDistance = 3f;
    [Tooltip("The steps travel from the body through these points over the run clip's length.")]
    [SerializeField] Transform[] runPath;
    [Tooltip("Played where he is on the path at Mid Run Time (e.g. a kicked tin can).")]
    [SerializeField] AudioClip midRunClip;
    [SerializeField, Range(0f, 1f)] float midRunVolume = 1f;
    [Tooltip("Point in the run (0-1) where the mid-run clip plays.")]
    [SerializeField, Range(0f, 1f)] float midRunTime = 0.5f;
    [Tooltip("Steps played one after another along the run path once the rise clip ends. Empty = none.")]
    [SerializeField] AudioClip[] runUpSteps;
    [Tooltip("Above 1 boosts the steps past the clip's own level.")]
    [SerializeField, Range(0f, 2f)] float runUpVolume = 2f;
    [SerializeField] int runUpStepCount = 5;
    [SerializeField] float runUpStepInterval = 0.3f;

    [Header("Standing at the wall")]
    [Tooltip("Placed in the scene (standing with his face to the wall across from the door) and kept off until the steps end; then he stays.")]
    [SerializeField] GameObject wallStander;
    [Tooltip("On: he only shows up once ShowWallStander is called (e.g. by a note), never before the steps end.")]
    [SerializeField] bool wallStanderWaitsForCall;
    [Tooltip("Kept off until he shows up, then turned on with him (e.g. an item left behind him).")]
    [SerializeField] GameObject[] appearWithWallStander;
    [Tooltip("The lighter's light; the reveal only happens while it (or the flashlight's beam) reaches him.")]
    [SerializeField] Light wallRevealLight;
    [Tooltip("Height on him (from his feet) the reveal checks and the zoom looks at.")]
    [SerializeField] float wallRevealHeight = 1.4f;
    [Tooltip("The reveal waits until the camera is this close to him (and within the light's range).")]
    [SerializeField] float wallRevealDistance = 3.8f;
    [Tooltip("...and he's within this distance of the screen centre (viewport units, 0.5 = edge).")]
    [SerializeField] float wallRevealScreenRadius = 0.2f;
    [Tooltip("He counts as seen (WallStanderSeen) once he's lit, unblocked and this close to the screen centre.")]
    [SerializeField] float wallSeenScreenRadius = 0.35f;
    [Tooltip("The reveal also waits until the camera is this much closer to him than where he was first seen.")]
    [SerializeField] float wallRevealCloser = 0.3f;
    [Tooltip("...or until he's been held within the reveal distance and screen radius this many seconds (his barrier can stop the step closer).")]
    [SerializeField] float wallRevealLookTime = 1f;
    [Tooltip("Any part of him on screen and in sight this close to the camera plays the reveal at once, lit or not, so the bar behind him can't be taken first.")]
    [SerializeField] float wallRevealNearDistance = 2.6f;
    [Tooltip("Played as the zoom on him starts.")]
    [SerializeField] AudioClip wallStingClip;
    [SerializeField, Range(0f, 1f)] float wallStingVolume = 1f;
    [Tooltip("Dedicated camera for the reveal's dolly zoom; cuts in from the player's exact view (blend times come from the brain's custom blends).")]
    [SerializeField] CinemachineCamera wallZoomCamera;
    [SerializeField] int wallZoomPriority = 100;
    [Tooltip("Metres the camera pushes in toward him; the lens widens to keep his size, so the room stretches behind him.")]
    [SerializeField] float wallZoomPush = 0.9f;
    [Tooltip("The push stops this far from him.")]
    [SerializeField] float wallZoomMinDistance = 1.1f;
    [SerializeField] float wallZoomTime = 0.4f;
    [SerializeField] float wallZoomHold = 1.3f;
    [Tooltip("Seconds of the blend back to the player view when there's no bar to look at.")]
    [SerializeField] float wallZoomBlendOut = 0.8f;
    [Tooltip("The item left behind him; after the zoom the shot hands back to the player's view, tilting down onto it.")]
    [SerializeField] GameObject barBehindHim;
    [Tooltip("Seconds the view takes to settle on the bar.")]
    [SerializeField] float wallBarLookTime = 1.8f;
    [Tooltip("The lens eases to this fraction of its FOV on the bar.")]
    [SerializeField] float wallBarZoom = 0.8f;
    [SerializeField] float wallBarHold = 1f;
    [Tooltip("Seconds the lens takes to ease back before control returns.")]
    [SerializeField] float wallBarReturnTime = 0.8f;

    [Header("5. Return scare (after the note)")]
    [Tooltip("Electric lights that flicker when the player steps back into the cage room, then snap off with the scare.")]
    [SerializeField] LightSwitch[] lightsToFlickerAndKill;
    [Tooltip("Extra lamp roots toggled with the light switches (cage / corridor wall lamps not on a switch).")]
    [SerializeField] GameObject[] extraLampsToKill;

    [Header("6. Coming back for the body (legacy cinematic — keep off)")]
    [Tooltip("After the note calls ShowWallStander, stepping back into the cage room through the doorway plays a shot on " +
             "where the body lay (on the wall zoom camera).")]
    [SerializeField] bool returnShot = false;
    [SerializeField] string returnSubtitleKey = "gameplay.where_is_the_body";
    [SerializeField] float returnSubtitleSeconds = 3f;
    [Tooltip("Share of the turn (0-1) at which the subtitle shows.")]
    [SerializeField, Range(0f, 1f)] float returnSubtitleAt = 0.6f;
    [Tooltip("Played flat as the view starts turning. Empty = nothing.")]
    [SerializeField] AudioClip returnClip;
    [SerializeField, Range(0f, 1f)] float returnClipVolume = 1f;
    [Tooltip("Seconds the view takes to turn onto the empty spot.")]
    [SerializeField] float returnTurnTime = 1.6f;
    [Tooltip("Metres the camera drifts in toward the spot over the shot (less if something is in the way).")]
    [SerializeField] float returnPush = 0.6f;
    [Tooltip("The lens narrows to this fraction of its FOV by the end of the hold.")]
    [SerializeField, Range(0.4f, 1f)] float returnZoom = 0.7f;
    [Tooltip("Seconds the view stays on the spot after the turn, still drifting in.")]
    [SerializeField] float returnHold = 1.8f;
    [Tooltip("Seconds of the blend back to the player view; control returns after it.")]
    [SerializeField] float returnBlendOut = 0.8f;
    [Tooltip("A faint light comes up over the spot so the empty floor reads in the dark. 0 = none.")]
    [SerializeField] float returnLightIntensity = 1.5f;
    [SerializeField] float returnLightRange = 3.5f;
    [SerializeField] Color returnLightColor = new Color(0.6f, 0.7f, 1f);

    [Header("Audio")]
    [SerializeField] AudioMixerGroup mixerGroup;

    [Header("Events")]
    public UnityEvent onLightsOut;

    public Stage CurrentStage => _stage;
    /// <summary>Player has left the cage room once (corridor unlock / leave path is open).</summary>
    public bool LeftCageRoomFirstPass =>
        _stage == Stage.WaitingForReturn || _stage == Stage.WaitingForLightsOut || _stage == Stage.Done;
    /// <summary>Note has armed the return scare / wall stander.</summary>
    public bool ReturnArmed => _wallStanderCalled;
    /// <summary>Bar is in the world and may be picked up (after the return lights-out).</summary>
    public bool BarPickupAllowed => _runFinished;
    public bool WallStanderShown => wallStander != null && wallStander.activeInHierarchy;
    public bool WallStanderSeen => _wallSeen;
    /// <summary>True once the bar behind him has been picked up.</summary>
    public bool BarTaken => _barTaken;
    public Vector3 WallStanderPosition => wallStander != null ? wallStander.transform.position : transform.position;
    public bool WallRevealPlayed => _wallRevealPlayed;
    /// <summary>The reveal on him has finished and the player has control again.</summary>
    public bool WallRevealOver => _wallRevealOver;
    public event System.Action WallStanderSeenEvent;
    /// <summary>The reveal on him is over and the player has control again.</summary>
    public event System.Action WallRevealFinished;
    /// <summary>The bar behind him was just picked up (the auto-save checkpoint).</summary>
    public event System.Action BarPickedUp;

    Stage _stage;
    Vector3 _lookPoint;
    AudioSource _bodySource;
    bool _wasInCage;
    float _outsideTimer;
    bool _wasInsideRoom;
    bool _doorwayClipPlayed;
    bool _cinematicActive;
    bool _wallRevealPlayed;
    bool _wallRevealOver;
    bool _barTaken;
    bool _runFinished;
    bool _wallStanderCalled;
    bool _wallSeen;
    float _wallSeenDistance;
    float _wallRevealHeld;
    bool _barShown;
    bool _wasOutsideRoom;
    bool _returnShotPlayed;
    bool _returnRoomFlickered;
    bool _returnScareStarted;
    bool _exitRustlePlayed;
    Light _returnLight;

    /// <summary>Corridor return trap: lights flicker once the player has stepped back in.</summary>
    public void NotifyReturnFlicker()
    {
        if (_returnRoomFlickered)
            return;
        _returnRoomFlickered = true;
        FlickerReturnLights();
    }

    public bool ReturnFlickerDone => _returnRoomFlickered;

    /// <summary>True when the player is on the cage-room side of <see cref="exitDoorway"/> (or no doorway is set).</summary>
    public bool IsPlayerInCageRoom(Vector3 worldPos) => IsInsideCageRoom(worldPos);


    /// <summary>
    /// Legacy hook. Tension walk is owned by <see cref="BasementTenseWalkZones"/> (return-for-bar + crawl house).
    /// Callers clear the flag when a beat ends.
    /// </summary>
    public void SetReturnWalkSlow(bool slow)
    {
        var fpc = FindFirstObjectByType<StarterAssets.FirstPersonController>(FindObjectsInactive.Include);
        if (fpc == null)
            return;
        if (!slow)
            fpc.TensionWalk = false;
    }

    void Awake()
    {
        _lookPoint = lookTarget != null ? lookTarget.position : BodyCenter();
        _bodySource = CreateSource("Corpse Audio", _lookPoint);
        if (wallZoomCamera != null)
            wallZoomCamera.Priority = 0;
        SetWallStanderActive(false);
        // Bar sits in the room from the start, but pickup stays blocked until the return scare finishes.
        if (barBehindHim != null)
            barBehindHim.SetActive(true);
    }

    /// <summary>
    /// Arms him for after the return lights-out. Never enables him early — a lit lighter must not reveal him
    /// while the room lights are still on.
    /// </summary>
    public void ShowWallStander()
    {
        _wallStanderCalled = true;
        // Appearance happens only in AppearWallStanderAfterLightsOut (end of return lights-out / run).
    }

    void SetWallStanderActive(bool active)
    {
        if (wallStander != null)
            wallStander.SetActive(active);
        if (appearWithWallStander != null)
            foreach (GameObject go in appearWithWallStander)
            {
                if (go == null || go == barBehindHim)
                    continue;
                go.SetActive(active);
            }
        if (active && barBehindHim != null)
        {
            barBehindHim.SetActive(true);
            _barShown = true;
        }
    }

    void OnEnable() => PadlockLockpickMinigame.PinSet += OnPinSet;

    void OnDisable()
    {
        PadlockLockpickMinigame.PinSet -= OnPinSet;
        EndCinematic();
    }

    void OnPinSet(PadlockLockpickMinigame padlock)
    {
        if (_stage != Stage.WaitingForPin)
            return;
        _stage = Stage.WaitingForExit;
        StartCoroutine(PlayClue(pinClue, pinClueVolume, pinClueDelay));
    }

    void Update()
    {
        if (_stage == Stage.WaitingForPin || _stage == Stage.WaitingForExit)
            TickCageExit();
        else if (_stage == Stage.WaitingForDoor)
            TickExitDoor();
        else if (_stage == Stage.WaitingForNextDoor)
            TickNextDoor();
        else if (_stage == Stage.WaitingForReturn)
            TickReturnScare();
        else if (_stage == Stage.Done)
        {
            TickBarTaken();
            if (returnShot && !_returnShotPlayed && _runFinished && _wallStanderCalled && !_wallRevealPlayed && !_cinematicActive)
                TickReturnDoorway();
            if (!_wallRevealPlayed)
            {
                // Bar grabbed without the reveal: it would only cut into the chase now.
                if (_barTaken)
                {
                    _wallRevealPlayed = true;
                    _wallRevealOver = true;
                    WallRevealFinished?.Invoke();
                }
                else
                    TickWallReveal();
            }
        }
    }

    // The bar destroys itself when picked up.
    void TickBarTaken()
    {
        if (_barTaken || !_barShown || barBehindHim != null)
            return;
        _barTaken = true;
        BarPickedUp?.Invoke();
    }

    static float SmootherStep(float u) => u * u * u * (u * (u * 6f - 15f) + 10f);

    void TickWallReveal()
    {
        if (wallStander == null || !wallStander.activeInHierarchy)
            return;
        Camera cam = Camera.main;
        if (cam == null)
            return;

        Vector3 point = wallStander.transform.position + Vector3.up * wallRevealHeight;
        float distance = Vector3.Distance(cam.transform.position, point);
        bool near = distance <= wallRevealNearDistance && PartlyInView(cam);
        if (!near && !LitByPlayer(point))
            return;
        Vector3 vp = cam.WorldToViewportPoint(point);
        float fromCentre = new Vector2(vp.x - 0.5f, vp.y - 0.5f).magnitude;

        if (!_wallSeen)
        {
            if (!near && (vp.z <= 0f || fromCentre > wallSeenScreenRadius || !HasLineOfSight(cam.transform.position, point)))
                return;
            _wallSeen = true;
            _wallSeenDistance = distance;
            WallStanderSeenEvent?.Invoke();
        }

        bool framed = vp.z > 0f && fromCentre <= wallRevealScreenRadius && distance <= wallRevealDistance;
        _wallRevealHeld = framed ? _wallRevealHeld + Time.deltaTime : 0f;
        if (!near && (!framed || (distance > _wallSeenDistance - wallRevealCloser && _wallRevealHeld < wallRevealLookTime)))
            return;
        // A tutorial card or note open right now: wait for it rather than lose the zoom.
        if (wallZoomCamera != null && PlayerControlGate.IsInputBlocked)
            return;

        _wallRevealPlayed = true;
        if (wallStingClip != null)
        {
            AudioSource sting = CreateFlatSource("Wall Sting Audio");
            sting.PlayOneShot(wallStingClip, wallStingVolume);
            Destroy(sting.gameObject, wallStingClip.length + 0.25f);
            CameraKick.KickPlayer(1.55f, 15f, 0.45f);
        }
        if (wallZoomCamera != null && !PlayerControlGate.IsInputBlocked)
            StartCoroutine(WallRevealRoutine(cam, point));
        else
        {
            _wallRevealOver = true;
            WallRevealFinished?.Invoke();
        }
    }

    bool LitByPlayer(Vector3 point)
    {
        if (wallRevealLight != null && wallRevealLight.isActiveAndEnabled
            && Vector3.Distance(wallRevealLight.transform.position, point) <= wallRevealLight.range)
            return true;
        foreach (HeldFlashlight flashlight in FindObjectsByType<HeldFlashlight>(FindObjectsSortMode.None))
        {
            Light beam = flashlight.Beam;
            if (flashlight.IsOn && beam != null && beam.isActiveAndEnabled
                && Vector3.Distance(beam.transform.position, point) <= beam.range)
                return true;
        }
        return false;
    }

    // Looking down at the bar behind him only his legs may be on screen.
    bool PartlyInView(Camera cam)
    {
        Vector3 feet = wallStander.transform.position;
        for (int i = 1; i <= 3; i++)
        {
            Vector3 p = feet + Vector3.up * (wallRevealHeight * i / 3f);
            Vector3 vp = cam.WorldToViewportPoint(p);
            if (vp.z > 0f && vp.x > 0.05f && vp.x < 0.95f && vp.y > 0.05f && vp.y < 0.95f
                && HasLineOfSight(cam.transform.position, p))
                return true;
        }
        return false;
    }

    bool HasLineOfSight(Vector3 from, Vector3 to)
    {
        foreach (RaycastHit hit in Physics.RaycastAll(from, to - from, Vector3.Distance(from, to), ~0, QueryTriggerInteraction.Ignore))
        {
            if (hit.transform.IsChildOf(wallStander.transform) || hit.collider.CompareTag("Player"))
                continue;
            if (hit.collider.GetComponentInParent<CharacterController>() != null)
                continue;
            return false;
        }
        return true;
    }

    // A quick dolly zoom on him; then the shot hands back to the player's own view as it tilts down onto the bar.
    IEnumerator WallRevealRoutine(Camera cam, Vector3 look)
    {
        CinemachineMouseLook mouseLook = FindFirstObjectByType<CinemachineMouseLook>();
        CinemachineBrain brain = cam.GetComponent<CinemachineBrain>();
        CinemachineCamera playerCam = brain != null ? brain.ActiveVirtualCamera as CinemachineCamera : null;
        float baseFov = playerCam != null ? playerCam.Lens.FieldOfView : 0f;
        Vector3 start = cam.transform.position;
        Quaternion startRot = cam.transform.rotation;
        float startDistance = Mathf.Max(0.01f, Vector3.Distance(start, look));
        float push = Mathf.Clamp(startDistance - wallZoomMinDistance, 0f, wallZoomPush);
        Vector3 end = start + (look - start).normalized * push;
        float halfTan = Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad);

        BeginCinematic();
        PoseWallZoomCamera(start, startRot, cam.fieldOfView);
        wallZoomCamera.Priority = wallZoomPriority;

        for (float t = 0f; t < wallZoomTime; t += Time.deltaTime)
        {
            float u = Mathf.SmoothStep(0f, 1f, t / wallZoomTime);
            Vector3 pos = Vector3.Lerp(start, end, u);
            Quaternion rot = Quaternion.Slerp(startRot, Quaternion.LookRotation(look - pos), Mathf.Sqrt(u));
            float fov = 2f * Mathf.Atan(halfTan * startDistance / Mathf.Max(0.01f, Vector3.Distance(pos, look))) * Mathf.Rad2Deg;
            PoseWallZoomCamera(pos, rot, Mathf.Clamp(fov, 1f, 120f));
            yield return null;
        }
        if (wallZoomHold > 0f)
            yield return new WaitForSeconds(wallZoomHold);

        // The brain blends back to the player's view while that view is already tilting down onto the bar.
        wallZoomCamera.Priority = 0;
        if (mouseLook != null && barBehindHim != null && barBehindHim.activeInHierarchy)
        {
            Renderer barRenderer = barBehindHim.GetComponentInChildren<Renderer>();
            Vector3 bar = barRenderer != null ? barRenderer.bounds.center : barBehindHim.transform.position;
            Quaternion barRot = Quaternion.LookRotation(bar - start);
            for (float t = 0f; t < wallBarLookTime; t += Time.deltaTime)
            {
                float u = SmootherStep(t / wallBarLookTime);
                mouseLook.SetLook(Quaternion.Slerp(startRot, barRot, u));
                SetLensFov(playerCam, Mathf.Lerp(baseFov, baseFov * wallBarZoom, u));
                yield return null;
            }
            mouseLook.SetLook(barRot);
            SetLensFov(playerCam, baseFov * wallBarZoom);
            if (wallBarHold > 0f)
                yield return new WaitForSeconds(wallBarHold);
            for (float t = 0f; t < wallBarReturnTime; t += Time.deltaTime)
            {
                SetLensFov(playerCam, Mathf.Lerp(baseFov * wallBarZoom, baseFov, SmootherStep(t / wallBarReturnTime)));
                yield return null;
            }
            SetLensFov(playerCam, baseFov);
        }
        else if (wallZoomBlendOut > 0f)
            yield return new WaitForSeconds(wallZoomBlendOut);
        EndCinematic();
        _wallRevealOver = true;
        WallRevealFinished?.Invoke();
    }

    /// <summary>
    /// Checkpoint load: straight to the end of the reveal. The body is gone, the lights are out (the lighter and
    /// candles come back from the save), he stands at the wall, and the reveal won't replay. With
    /// <paramref name="barTaken"/> the bar is already in hand (the save removes it from the world), so he waits to go.
    /// </summary>
    public void RestoreAfterWallReveal(bool barTaken)
    {
        _barTaken = barTaken;
        StopAllCoroutines();
        EndCinematic();
        _stage = Stage.Done;
        _runFinished = true;
        SetReturnWalkSlow(false);
        _wallStanderCalled = true;
        _wallSeen = true;
        _wallRevealPlayed = true;
        _wallRevealOver = true;
        _returnShotPlayed = true;
        _returnScareStarted = true;
        _returnRoomFlickered = true;
        // Mid-return checkpoint: keep insects/basement bed muted until exit + chase end.
        if (!barTaken)
            SoundManager.Instance?.SetReturnScareAudioLock(true);
        foreach (Renderer r in bodyRenderers)
            if (r != null)
                r.enabled = false;
        AddHighlights();
        KillReturnLights();
        SetWallStanderActive(true);
    }

    void PoseWallZoomCamera(Vector3 position, Quaternion rotation, float fov)
    {
        wallZoomCamera.transform.SetPositionAndRotation(position, rotation);
        LensSettings lens = wallZoomCamera.Lens;
        lens.FieldOfView = fov;
        wallZoomCamera.Lens = lens;
    }

    static void SetLensFov(CinemachineCamera lensCam, float fov)
    {
        if (lensCam == null || fov <= 0f)
            return;
        LensSettings lens = lensCam.Lens;
        lens.FieldOfView = fov;
        lensCam.Lens = lens;
    }

    // The pin clue is optional for the exit: a player who somehow skips it can still trigger the door.
    void TickCageExit()
    {
        GameObject player = GameObject.FindWithTag("Player");
        if (player == null)
            return;

        if (Cage.IsPlayerInsideAnyCage(player.transform))
        {
            _wasInCage = true;
            _outsideTimer = 0f;
            return;
        }

        bool doorOpen = cageDoor == null || cageDoor.objectOpen;
        if (!_wasInCage || !doorOpen || PlayerControlGate.IsInputBlocked)
        {
            _outsideTimer = 0f;
            return;
        }

        _outsideTimer += Time.deltaTime;
        if (_outsideTimer < exitConfirmTime)
            return;
        _wasInsideRoom = true;
        _stage = Stage.WaitingForDoor;
        // Rustle waits until they walk past the body toward the door with their back turned.
    }

    void TickExitDoor()
    {
        GameObject player = GameObject.FindWithTag("Player");
        if (player == null)
            return;

        TickExitRustle(player.transform);

        if (exitDoorway == null)
            return;

        Vector3 normal = DoorwayInward();
        Vector3 offset = player.transform.position - exitDoorway.position;
        offset.y = 0f;
        float depth = Vector3.Dot(offset, normal);
        if (depth >= 0f)
        {
            _wasInsideRoom = true;
            return;
        }
        float across = (offset - normal * depth).magnitude;
        if (_wasInsideRoom && !_doorwayClipPlayed && across <= doorwayHalfWidth)
        {
            _doorwayClipPlayed = true;
            PlayDoorwayClip();
        }
        if (!_wasInsideRoom || depth > -doorwayCrossDepth || across > doorwayHalfWidth)
            return;

        // First pass: leave the body where it is and open the corridor unlock path. Scare waits for the return.
        _wasOutsideRoom = true;
        if (nextDoor != null)
            _stage = Stage.WaitingForNextDoor;
        else
            _stage = Stage.WaitingForReturn;
    }

    void TickExitRustle(Transform player)
    {
        if (_exitRustlePlayed || exitClue == null && exitFollowUpClip == null)
            return;
        if (PlayerControlGate.IsInputBlocked)
            return;

        Vector3 body = _lookPoint;
        Vector3 toDoor = exitDoorway != null ? Flat(exitDoorway.position - body) : Flat(player.position - body);
        if (toDoor.sqrMagnitude < 1e-4f)
            return;
        toDoor.Normalize();

        Vector3 toPlayer = Flat(player.position - body);
        float along = Vector3.Dot(toPlayer, toDoor);
        if (along < rustlePastBodyDistance)
            return;

        Camera cam = Camera.main;
        if (cam == null)
            return;
        Vector3 toBody = body + Vector3.up * 0.6f - cam.transform.position;
        if (toBody.sqrMagnitude > 1e-4f
            && Vector3.Angle(cam.transform.forward, toBody) < rustleLookAwayAngle)
            return;

        _exitRustlePlayed = true;
        StartCoroutine(PlayClue(exitClue, exitClueVolume, 0f));
        if (exitFollowUpClip != null)
            StartCoroutine(PlayClue(exitFollowUpClip, exitFollowUpVolume, exitFollowUpDelay));
    }

    void TickReturnScare()
    {
        if (!_wallStanderCalled || _returnScareStarted)
            return;
        GameObject player = GameObject.FindWithTag("Player");
        if (player == null)
            return;

        bool inRoom = IsInsideCageRoom(player.transform.position);
        if (!inRoom)
        {
            _wasOutsideRoom = true;
            return;
        }

        // Sit-up when they step back into the cage room (after the corridor flicker / door bang).
        if (!_wasOutsideRoom)
            return;

        _returnScareStarted = true;
        StartCoroutine(SitUpThenLightsOut());
    }

    bool IsInsideCageRoom(Vector3 worldPos)
    {
        if (exitDoorway == null)
            return true;
        Vector3 normal = DoorwayInward();
        Vector3 offset = worldPos - exitDoorway.position;
        offset.y = 0f;
        return Vector3.Dot(offset, normal) >= -doorwayCrossDepth;
    }

    void FlickerReturnLights()
    {
        if (lightsToFlickerAndKill != null)
        {
            foreach (LightSwitch sw in lightsToFlickerAndKill)
                if (sw != null)
                    sw.PlayFlicker();
        }
        // Corridor / cage wall lamps aren't on the switch — pulse them too.
        if (extraLampsToKill != null && extraLampsToKill.Length > 0)
            StartCoroutine(FlickerExtraLampsRoutine());
    }

    IEnumerator FlickerExtraLampsRoutine()
    {
        float[] pulse = { 0.05f, 0.1f, 0.04f, 0.18f, 0.06f, 0.12f, 0.05f };
        bool lit = false;
        foreach (float duration in pulse)
        {
            SetExtraLampsLit(lit);
            yield return new WaitForSeconds(duration);
            lit = !lit;
        }
        SetExtraLampsLit(true);
    }

    void SetExtraLampsLit(bool lit)
    {
        if (extraLampsToKill == null)
            return;
        foreach (GameObject lamp in extraLampsToKill)
        {
            if (lamp == null)
                continue;
            foreach (Light l in lamp.GetComponentsInChildren<Light>(true))
            {
                if (l == null)
                    continue;
                if (l.GetComponentInParent<Unity.Cinemachine.CinemachineCamera>() != null)
                    continue;
                l.enabled = lit;
            }
        }
    }

    void KillReturnLights()
    {
        if (lightsToFlickerAndKill != null)
        {
            foreach (LightSwitch sw in lightsToFlickerAndKill)
                if (sw != null && sw.IsOn)
                    sw.SetOnInstant(false);
        }
        if (extraLampsToKill != null)
        {
            foreach (GameObject lamp in extraLampsToKill)
            {
                if (lamp == null)
                    continue;
                foreach (Light l in lamp.GetComponentsInChildren<Light>(true))
                    if (l != null)
                        l.enabled = false;
            }
        }
    }

    IEnumerator SitUpThenLightsOut()
    {
        _stage = Stage.WaitingForLightsOut;
        // Sting waits so it lands once he can be seen; sit-up motion + lights keep their timing.
        if (sitUpStingClip != null)
            StartCoroutine(PlaySitUpStingDelayed(0.5f));
        if (sitUpClip != null)
        {
            AudioSource src = CreateSource("Corpse Sit Up Audio", _lookPoint, pinClueMinDistance);
            src.PlayOneShot(sitUpClip, sitUpVolume);
            Destroy(src.gameObject, sitUpClip.length + 0.25f);
        }
        if (sitUp != null)
            sitUp.Play();

        float hold;
        if (sitUp != null && lightsOutAtSitUpNormalized > 0f)
            hold = sitUp.RealtimeUntil(lightsOutAtSitUpNormalized);
        else if (sitUpHoldBeforeLightsOut > 0f)
            hold = sitUpHoldBeforeLightsOut;
        else
            hold = sitUp != null ? sitUp.PlayDurationRealtime : 0.35f;

        float flickerLead = SitUpKillFlickerDuration();
        // Land darkness sitUpLightsOutEarlyBy earlier than the authored sit-up mark;
        // start the kill flicker just before that so it ends on the early mark.
        float killAt = Mathf.Max(0f, hold - Mathf.Max(0f, sitUpLightsOutEarlyBy));
        float waitBeforeFlicker = Mathf.Max(0f, killAt - flickerLead);
        if (waitBeforeFlicker > 0f)
            yield return new WaitForSeconds(waitBeforeFlicker);

        yield return FlickerKillReturnLightsRoutine();
        yield return LightsOutRoutine();
    }

    IEnumerator PlaySitUpStingDelayed(float delay)
    {
        if (delay > 0f)
            yield return new WaitForSeconds(delay);
        if (sitUpStingClip != null)
            SoundManager.Instance?.PlaySFX(sitUpStingClip, sitUpStingVolume);
    }

    float SitUpKillFlickerDuration()
    {
        if (sitUpKillFlickerPattern == null || sitUpKillFlickerPattern.Length == 0)
            return 0.55f;
        float total = 0f;
        for (int i = 0; i < sitUpKillFlickerPattern.Length; i++)
            total += Mathf.Max(0f, sitUpKillFlickerPattern[i]);
        return Mathf.Max(0.05f, total);
    }

    /// <summary>
    /// Sit-up kill flicker: stutters then ends dark (unlike corridor PlayFlicker which leaves lights on).
    /// </summary>
    IEnumerator FlickerKillReturnLightsRoutine()
    {
        float[] pulse = sitUpKillFlickerPattern != null && sitUpKillFlickerPattern.Length > 0
            ? sitUpKillFlickerPattern
            : new[] { 0.05f, 0.08f, 0.04f, 0.12f, 0.05f, 0.09f, 0.04f, 0.15f };

        // Start from lit so the first pulse reads as a blink.
        bool lit = true;
        foreach (float duration in pulse)
        {
            SetReturnLightsLit(lit);
            yield return new WaitForSeconds(Mathf.Max(0.01f, duration));
            lit = !lit;
        }

        // Always finish dark — never leave the corridor-style "end on" state.
        KillReturnLights();
    }

    void SetReturnLightsLit(bool lit)
    {
        if (lightsToFlickerAndKill != null)
        {
            foreach (LightSwitch sw in lightsToFlickerAndKill)
            {
                if (sw == null)
                    continue;
                // Visual only — rocker / IsOn settle in KillReturnLights.
                sw.PulseLit(lit);
            }
        }
        SetExtraLampsLit(lit);
    }

    void PlayDoorwayClip()
    {
        if (doorwayClip == null)
            return;
        AudioSource src = doorwayClipPoint != null ? CreateSource("Doorway Audio", doorwayClipPoint.position)
            : CreateFlatSource("Doorway Audio");
        src.PlayOneShot(doorwayClip, doorwayClipVolume);
        Destroy(src.gameObject, doorwayClip.length + 0.25f);
    }

    // Flat normal of the doorframe, pointing into the room (the side the body is on).
    Vector3 DoorwayInward()
    {
        Vector3 normal = exitDoorway.forward;
        normal.y = 0f;
        normal.Normalize();
        if (Vector3.Dot(_lookPoint - exitDoorway.position, normal) < 0f)
            normal = -normal;
        return normal;
    }

    void TickReturnDoorway()
    {
        if (exitDoorway == null)
            return;
        GameObject player = GameObject.FindWithTag("Player");
        if (player == null)
            return;

        Vector3 normal = DoorwayInward();
        Vector3 offset = player.transform.position - exitDoorway.position;
        offset.y = 0f;
        float depth = Vector3.Dot(offset, normal);
        if (depth <= -doorwayCrossDepth)
        {
            _wasOutsideRoom = true;
            return;
        }
        float across = (offset - normal * depth).magnitude;
        // A note still open as they step in: the shot waits for it to close.
        if (!_wasOutsideRoom || depth < doorwayCrossDepth || across > doorwayHalfWidth || PlayerControlGate.IsInputBlocked)
            return;

        _returnShotPlayed = true;
        Camera cam = Camera.main;
        if (wallZoomCamera != null && cam != null)
            StartCoroutine(ReturnShotRoutine(cam));
        else
            ShowReturnSubtitle();
    }

    // The head turns onto where he lay while the camera drifts in and the lens tightens on the empty floor, a faint
    // light coming up over it; then the shot hands back with the player still facing the spot.
    IEnumerator ReturnShotRoutine(Camera cam)
    {
        CinemachineMouseLook mouseLook = FindFirstObjectByType<CinemachineMouseLook>();
        Vector3 start = cam.transform.position;
        Quaternion startRot = cam.transform.rotation;
        float baseFov = cam.fieldOfView;
        Vector3 look = _lookPoint;
        Vector3 dir = (look - start).normalized;
        float push = Mathf.Clamp(Mathf.Min(returnPush, Vector3.Distance(start, look) - wallZoomMinDistance, Clearance(start, dir, returnPush + 0.4f)), 0f, returnPush);
        Vector3 end = start + dir * push;

        BeginCinematic();
        PoseWallZoomCamera(start, startRot, baseFov);
        wallZoomCamera.Priority = wallZoomPriority;
        SpawnReturnLight(look + Flat(start - look).normalized * 0.8f + Vector3.up * 1.2f);
        if (returnClip != null)
        {
            AudioSource src = CreateFlatSource("Return Shot Audio");
            src.PlayOneShot(returnClip, returnClipVolume);
            Destroy(src.gameObject, returnClip.length + 0.25f);
        }

        float turnTime = Mathf.Max(0.05f, returnTurnTime);
        float total = turnTime + Mathf.Max(0f, returnHold);
        bool subtitled = false;
        for (float t = 0f; t < total; t += Time.deltaTime)
        {
            float k = Mathf.Clamp01(t / turnTime);
            float u = SmootherStep(Mathf.Clamp01(t / total));
            Vector3 pos = Vector3.Lerp(start, end, u);
            Quaternion rot = Quaternion.Slerp(startRot, Quaternion.LookRotation(look - pos), SmootherStep(k));
            PoseWallZoomCamera(pos, rot, baseFov * Mathf.Lerp(1f, returnZoom, u));
            SetReturnLight(k);
            if (!subtitled && k >= returnSubtitleAt)
            {
                subtitled = true;
                ShowReturnSubtitle();
            }
            yield return null;
        }
        if (!subtitled)
            ShowReturnSubtitle();

        if (mouseLook != null)
            mouseLook.SetLook(Quaternion.LookRotation(look - start));
        wallZoomCamera.Priority = 0;
        for (float t = 0f; t < returnBlendOut; t += Time.deltaTime)
        {
            SetReturnLight(1f - t / returnBlendOut);
            yield return null;
        }
        EndCinematic();
    }

    void ShowReturnSubtitle()
    {
        if (HudManager.Instance != null && !string.IsNullOrEmpty(returnSubtitleKey))
            HudManager.Instance.ShowSubtitle(Loc.Get(returnSubtitleKey), returnSubtitleSeconds);
    }

    void SpawnReturnLight(Vector3 position)
    {
        if (returnLightIntensity <= 0f)
            return;
        var go = new GameObject("Return Shot Light");
        go.transform.SetParent(transform, false);
        go.transform.position = position;
        _returnLight = go.AddComponent<Light>();
        _returnLight.type = LightType.Point;
        _returnLight.color = returnLightColor;
        _returnLight.range = returnLightRange;
        _returnLight.shadows = LightShadows.Soft;
        SetReturnLight(0f);
    }

    void SetReturnLight(float level)
    {
        if (_returnLight == null)
            return;
        level = Mathf.Clamp01(level);
        _returnLight.intensity = returnLightIntensity * level;
        _returnLight.enabled = level > 0f;
    }

    // Free distance along the move before anything solid.
    static float Clearance(Vector3 from, Vector3 dir, float distance)
    {
        float free = distance;
        foreach (RaycastHit hit in Physics.SphereCastAll(from, 0.25f, dir, distance, ~0, QueryTriggerInteraction.Ignore))
        {
            if (hit.distance <= 0f || hit.collider.GetComponentInParent<CharacterController>() != null)
                continue;
            free = Mathf.Min(free, hit.distance - 0.4f);
        }
        return free;
    }

    static Vector3 Flat(Vector3 v)
    {
        v.y = 0f;
        return v;
    }

    void TickNextDoor()
    {
        GameObject player = GameObject.FindWithTag("Player");
        if (player == null)
            return;
        Vector3 offset = player.transform.position - nextDoor.position;
        offset.y = 0f;
        if (offset.magnitude > nextDoorDistance)
            return;
        _wasOutsideRoom = true;
        _stage = Stage.WaitingForReturn;
    }

    void BeginCinematic()
    {
        _cinematicActive = true;
        PlayerControlGate.Acquire(CinematicLockReason);
        GameplayHudVisibility.Instance?.HideGameplayHud();
    }

    void EndCinematic()
    {
        if (_returnLight != null)
            Destroy(_returnLight.gameObject);
        if (!_cinematicActive)
            return;
        _cinematicActive = false;

        if (wallZoomCamera != null)
            wallZoomCamera.Priority = 0;
        GameplayHudVisibility.Instance?.ShowGameplayHud();
        PlayerControlGate.Release(CinematicLockReason);
    }

    IEnumerator LightsOutRoutine()
    {
        // Snap on the return sit-up scare; keep the authored delay for any other callers.
        if (lightsOutDelay > 0f && !_returnScareStarted)
            yield return new WaitForSeconds(lightsOutDelay);
        _stage = Stage.Done;

        // Sit-up path already ran FlickerKillReturnLightsRoutine; keep this as a hard settle.
        KillReturnLights();
        foreach (Renderer r in bodyRenderers)
            if (r != null)
                r.enabled = false;

        Camera cam = Camera.main;
        if (blowOutClip != null && cam != null)
        {
            AudioSource gust = CreateSource("Lights Out Gust Audio", cam.transform.TransformPoint(blowOutOffset), blowOutMinDistance);
            gust.PlayOneShot(blowOutClip, blowOutVolume);
            Destroy(gust.gameObject, blowOutClip.length + 0.25f);
            if (blowOutLead > 0f && !_returnScareStarted)
                yield return new WaitForSeconds(blowOutLead);
        }

        // Also when the lighter isn't in hand: its view stays in the scene, so the misfires wait for the next draw.
        HeldLighter lighter = FindFirstObjectByType<HeldLighter>(FindObjectsInactive.Include);
        if (lighter != null)
        {
            if (lighter.IsLit)
                lighter.Extinguish();
            lighter.MisfireNextStrikes(lighterMisfires);
        }
        GameObject player = GameObject.FindWithTag("Player");
        foreach (LightableCandle candle in FindObjectsByType<LightableCandle>(FindObjectsSortMode.None))
        {
            Vector3 p = candle.transform.position;
            if (candle.IsLit && (Vector3.Distance(p, _lookPoint) <= candleRadius
                                 || (player != null && Vector3.Distance(p, player.transform.position) <= candleRadius)))
                candle.SetLit(false);
        }
        AddHighlights();
        onLightsOut?.Invoke();

        if (runDelay > 0f)
            yield return new WaitForSeconds(runDelay);
        yield return RunRoutine();
        _runFinished = true;
        // Walk stays capped at sneak until WallStanderChase starts.
        if (barBehindHim != null)
        {
            barBehindHim.SetActive(true);
            _barShown = true;
        }
        AppearWallStanderAfterLightsOut();
    }

    void AppearWallStanderAfterLightsOut()
    {
        if (!wallStanderWaitsForCall || _wallStanderCalled)
            SetWallStanderActive(true);
    }

    void AddHighlights()
    {
        if (highlightMaterial == null || highlightOnLightsOut == null)
            return;
        foreach (Renderer r in highlightOnLightsOut)
        {
            if (r == null)
                continue;
            var mats = new List<Material>(r.sharedMaterials);
            if (mats.Contains(highlightMaterial))
                continue;
            mats.Add(highlightMaterial);
            r.sharedMaterials = mats.ToArray();
        }
    }

    IEnumerator RunRoutine()
    {
        if (riseClip != null)
            _bodySource.PlayOneShot(riseClip, riseVolume);
        Coroutine runUp = StartCoroutine(RunUpStepsRoutine(riseClip != null ? riseClip.length : 0f));
        if (runClip != null)
            yield return RunClipRoutine();
        yield return runUp;
    }

    IEnumerator RunUpStepsRoutine(float delay)
    {
        if (runUpSteps == null || runUpSteps.Length == 0)
            yield break;
        if (delay > 0f)
            yield return new WaitForSeconds(delay);
        for (int i = 0; i < runUpStepCount; i++)
        {
            float u = runUpStepCount > 1 ? (float)i / (runUpStepCount - 1) : 1f;
            AudioClip step = runUpSteps[i % runUpSteps.Length];
            if (step != null)
            {
                AudioSource src = CreateSource("Corpse Run Up Audio", PointOnPath(u), runMinDistance);
                src.pitch = UnityEngine.Random.Range(0.9f, 0.98f);
                src.PlayOneShot(step, runUpVolume);
                Destroy(src.gameObject, step.length / src.pitch + 0.25f);
            }
            yield return new WaitForSeconds(runUpStepInterval * Mathf.Lerp(1.15f, 0.85f, u));
        }
    }

    IEnumerator RunClipRoutine()
    {
        AudioSource steps = CreateSource("Corpse Run Audio", _lookPoint, runMinDistance);
        steps.clip = runClip;
        steps.volume = runVolume;
        steps.Play();

        float duration = runClip.length;
        bool midPlayed = midRunClip == null;
        for (float t = 0f; t < duration && steps.isPlaying; t += Time.deltaTime)
        {
            // Eases in: a stumbling start, then a sprint.
            float u = t / duration;
            steps.transform.position = PointOnPath(u * u * (2f - u));
            if (!midPlayed && u >= midRunTime)
            {
                midPlayed = true;
                AudioSource can = CreateSource("Corpse Mid Run Audio", steps.transform.position, runMinDistance);
                can.PlayOneShot(midRunClip, midRunVolume);
                Destroy(can.gameObject, midRunClip.length + 0.25f);
            }
            yield return null;
        }
        Destroy(steps.gameObject, 0.5f);
    }

    IEnumerator PlayClue(AudioClip clip, float volume, float delay)
    {
        if (clip == null)
            yield break;
        if (delay > 0f)
            yield return new WaitForSeconds(delay);
        if (_stage == Stage.Done)
            yield break;
        // Interactive spatial (no reflection wash / occlusion choke) so rustles read in the room cleanly.
        AudioSource clue = SpatialAudio.Create("Corpse Clue Audio", transform, _lookPoint, mixerGroup, 1.25f, 20f,
            AudioRolloffMode.Logarithmic, atmospheric: false);
        clue.PlayOneShot(clip, volume);
        Destroy(clue.gameObject, clip.length + 0.85f);
    }

    // Body first, then the run path points.
    Vector3 PointOnPath(float u)
    {
        var points = new System.Collections.Generic.List<Vector3> { _lookPoint };
        if (runPath != null)
            foreach (Transform p in runPath)
                if (p != null)
                    points.Add(p.position);
        if (points.Count == 1)
            return _lookPoint;

        float total = 0f;
        for (int i = 1; i < points.Count; i++)
            total += Vector3.Distance(points[i - 1], points[i]);
        float d = Mathf.Clamp01(u) * total;
        for (int i = 1; i < points.Count; i++)
        {
            float seg = Vector3.Distance(points[i - 1], points[i]);
            if (d <= seg || i == points.Count - 1)
                return Vector3.Lerp(points[i - 1], points[i], seg > 0f ? Mathf.Clamp01(d / seg) : 1f);
            d -= seg;
        }
        return points[points.Count - 1];
    }

    Vector3 BodyCenter()
    {
        bool any = false;
        Bounds b = default;
        if (bodyRenderers != null)
            foreach (Renderer r in bodyRenderers)
            {
                if (r == null)
                    continue;
                if (any)
                    b.Encapsulate(r.bounds);
                else
                    b = r.bounds;
                any = true;
            }
        return any ? b.center + Vector3.up * 0.05f : transform.position;
    }

    AudioSource CreateSource(string sourceName, Vector3 position, float minDistance = 1.2f)
    {
        return SpatialAudio.Create(sourceName, transform, position, mixerGroup, minDistance, 20f);
    }

    AudioSource CreateFlatSource(string sourceName)
    {
        var go = new GameObject(sourceName);
        go.transform.SetParent(transform, false);
        var src = go.AddComponent<AudioSource>();
        src.playOnAwake = false;
        src.outputAudioMixerGroup = mixerGroup;
        src.spatialBlend = 0f;
        return src;
    }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
    /// <summary>Dev shortcut: return scare and wall reveal are done; the bar is still in the world.</summary>
    public void DebugPrepareForBarPickup()
    {
        RestoreAfterWallReveal(barTaken: false);
        PrepareDoorOpen(cageDoor);
        if (nextDoor != null)
        {
            SimpleOpenClose door = nextDoor.GetComponent<SimpleOpenClose>()
                                   ?? nextDoor.GetComponentInParent<SimpleOpenClose>();
            PrepareDoorOpen(door);
        }
    }

    public bool TryGetBarApproachPose(out Vector3 feet, out float yaw)
    {
        feet = default;
        yaw = 0f;
        if (!WallStanderShown && wallStander == null)
            return false;

        Vector3 bar = WallStanderPosition;
        if (barBehindHim != null && barBehindHim.activeInHierarchy)
        {
            Renderer barRenderer = barBehindHim.GetComponentInChildren<Renderer>();
            bar = barRenderer != null ? barRenderer.bounds.center : barBehindHim.transform.position;
        }

        Vector3 towardBar = bar - WallStanderPosition;
        towardBar.y = 0f;
        Vector3 approach = towardBar.sqrMagnitude > 0.04f ? towardBar.normalized : Vector3.forward;
        Vector3 spot = bar + approach * 1.65f;
        if (Physics.Raycast(spot + Vector3.up * 0.75f, Vector3.down, out RaycastHit hit, 2.5f, ~0, QueryTriggerInteraction.Ignore))
            feet = hit.point;
        else
            feet = spot;
        yaw = Quaternion.LookRotation(-approach, Vector3.up).eulerAngles.y;
        return true;
    }

    static void PrepareDoorOpen(SimpleOpenClose door)
    {
        if (door == null)
            return;
        if (door.isLocked)
            door.UnlockSilently();
        if (!door.objectOpen)
            door.SnapOpenState(true);
        DoorController ai = door.GetComponentInParent<DoorController>();
        if (ai != null)
            ai.SetLocked(false);
    }
#endif
}
