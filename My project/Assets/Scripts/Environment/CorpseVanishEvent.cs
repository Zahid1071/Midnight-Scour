using System.Collections;
using System.Collections.Generic;
using FS_Atmo;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.Events;

/// <summary>
/// Cage room corpse scare (scripted):
/// 1. The first padlock pin the player sets: a faint scratch from the body, quiet enough to doubt.
/// 2. The player walks out of the open cage (this arms the door); a second faint sound follows a moment later.
/// 3. The player opens the cage room door (then, if set, walks up to the next door): the body is gone, the lighter (and any
///    lit candles near the body or the player) go out in a gust, the tool the player needs starts to glint, and running
///    footsteps tear away from where he lay, kicking a tin can halfway. The lighter then only sparks for a few
///    strikes before it catches again. When the steps end (or later, once something calls ShowWallStander) he's
///    standing, face to the wall, across the room.
/// 4. The first time the lit lighter shows him (WallStanderSeenEvent), then a step closer: a sting and a quick dolly
///    zoom on him, then the shot hands back to the player's view as it tilts down onto the bar left behind him.
/// 5. Taking the bar sets BarTaken (the corridor music goes silent on it; WallStanderChase arms on it).
/// </summary>
[DisallowMultipleComponent]
public class CorpseVanishEvent : MonoBehaviour
{
    public enum Stage { WaitingForPin, WaitingForExit, WaitingForDoor, WaitingForNextDoor, WaitingForLightsOut, Done }

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
    [Tooltip("Played from the body after the player is out of the cage; uses the pin clue's min distance.")]
    [SerializeField] AudioClip exitClue;
    [SerializeField, Range(0f, 1f)] float exitClueVolume = 0.4f;
    [Tooltip("Seconds after leaving the cage before the clue.")]
    [SerializeField] float exitClueDelay = 1f;

    [Header("3. Opening the cage room door, then the next door")]
    [SerializeField] SimpleOpenClose exitDoor;
    [Tooltip("Once the door is open, the lights go out when the player gets this close to it. Empty = as the door opens.")]
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

    [Header("Standing at the wall")]
    [Tooltip("Placed in the scene (standing with his face to the wall across from the door) and kept off until the steps end; then he stays.")]
    [SerializeField] GameObject wallStander;
    [Tooltip("On: he only shows up once ShowWallStander is called (e.g. by a note), never before the steps end.")]
    [SerializeField] bool wallStanderWaitsForCall;
    [Tooltip("Kept off until he shows up, then turned on with him (e.g. an item left behind him).")]
    [SerializeField] GameObject[] appearWithWallStander;
    [Tooltip("The lighter's light; the reveal only happens while it's burning.")]
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

    [Header("Audio")]
    [SerializeField] AudioMixerGroup mixerGroup;

    [Header("Events")]
    public UnityEvent onLightsOut;

    public Stage CurrentStage => _stage;
    public bool WallStanderShown => wallStander != null && wallStander.activeInHierarchy;
    public bool WallStanderSeen => _wallSeen;
    /// <summary>True once the bar behind him has been picked up.</summary>
    public bool BarTaken => _barTaken;
    public Vector3 WallStanderPosition => wallStander != null ? wallStander.transform.position : transform.position;
    public event System.Action WallStanderSeenEvent;

    Stage _stage;
    Vector3 _lookPoint;
    AudioSource _bodySource;
    bool _wasInCage;
    float _outsideTimer;
    bool _exitDoorWasOpen;
    bool _cinematicActive;
    bool _wallRevealPlayed;
    bool _barTaken;
    bool _runFinished;
    bool _wallStanderCalled;
    bool _wallSeen;
    float _wallSeenDistance;
    bool _barShown;

    void Awake()
    {
        _lookPoint = lookTarget != null ? lookTarget.position : BodyCenter();
        _bodySource = CreateSource("Corpse Audio", _lookPoint);
        if (wallZoomCamera != null)
            wallZoomCamera.Priority = 0;
        SetWallStanderActive(false);
    }

    /// <summary>Lets him show up at the wall; if the steps haven't ended yet he appears when they do.</summary>
    public void ShowWallStander()
    {
        _wallStanderCalled = true;
        if (_runFinished)
            SetWallStanderActive(true);
    }

    void SetWallStanderActive(bool active)
    {
        if (wallStander != null)
            wallStander.SetActive(active);
        if (appearWithWallStander != null)
            foreach (GameObject go in appearWithWallStander)
                if (go != null)
                    go.SetActive(active);
        if (active && barBehindHim != null)
            _barShown = true;
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
        else if (_stage == Stage.Done)
        {
            if (!_wallRevealPlayed)
                TickWallReveal();
            TickBarTaken();
        }
    }

    // The bar destroys itself when picked up.
    void TickBarTaken()
    {
        if (!_barTaken && _barShown && barBehindHim == null)
            _barTaken = true;
    }

    static float SmootherStep(float u) => u * u * u * (u * (u * 6f - 15f) + 10f);

    void TickWallReveal()
    {
        if (wallStander == null || !wallStander.activeInHierarchy || wallRevealLight == null || !wallRevealLight.isActiveAndEnabled)
            return;
        Camera cam = Camera.main;
        if (cam == null)
            return;

        Vector3 point = wallStander.transform.position + Vector3.up * wallRevealHeight;
        if (Vector3.Distance(wallRevealLight.transform.position, point) > wallRevealLight.range)
            return;
        Vector3 vp = cam.WorldToViewportPoint(point);
        float fromCentre = new Vector2(vp.x - 0.5f, vp.y - 0.5f).magnitude;
        float distance = Vector3.Distance(cam.transform.position, point);

        if (!_wallSeen)
        {
            if (vp.z <= 0f || fromCentre > wallSeenScreenRadius || !HasLineOfSight(cam.transform.position, point))
                return;
            _wallSeen = true;
            _wallSeenDistance = distance;
            WallStanderSeenEvent?.Invoke();
        }

        if (vp.z <= 0f || fromCentre > wallRevealScreenRadius
            || distance > Mathf.Min(wallRevealDistance, _wallSeenDistance - wallRevealCloser))
            return;

        _wallRevealPlayed = true;
        if (wallStingClip != null)
        {
            AudioSource sting = CreateSource("Wall Sting Audio", cam.transform.position);
            sting.spatialBlend = 0f;
            sting.PlayOneShot(wallStingClip, wallStingVolume);
            Destroy(sting.gameObject, wallStingClip.length + 0.25f);
        }
        if (wallZoomCamera != null && !PlayerControlGate.IsInputBlocked)
            StartCoroutine(WallRevealRoutine(cam, point));
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
        _exitDoorWasOpen = exitDoor != null && exitDoor.objectOpen;
        _stage = Stage.WaitingForDoor;
        // Delay counts from the moment the player stepped out, not from the confirmation.
        StartCoroutine(PlayClue(exitClue, exitClueVolume, exitClueDelay - exitConfirmTime));
    }

    void TickExitDoor()
    {
        bool open = exitDoor != null && exitDoor.objectOpen;
        if (open && !_exitDoorWasOpen)
        {
            if (nextDoor != null)
                _stage = Stage.WaitingForNextDoor;
            else
            {
                _stage = Stage.WaitingForLightsOut;
                StartCoroutine(LightsOutRoutine());
            }
        }
        _exitDoorWasOpen = open;
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
        _stage = Stage.WaitingForLightsOut;
        StartCoroutine(LightsOutRoutine());
    }

    void BeginCinematic()
    {
        _cinematicActive = true;
        PlayerControlGate.Acquire(CinematicLockReason);
        GameplayHudVisibility.Instance?.HideGameplayHud();
    }

    void EndCinematic()
    {
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
        if (lightsOutDelay > 0f)
            yield return new WaitForSeconds(lightsOutDelay);
        _stage = Stage.Done;

        Camera cam = Camera.main;
        if (blowOutClip != null && cam != null)
        {
            AudioSource gust = CreateSource("Lights Out Gust Audio", cam.transform.TransformPoint(blowOutOffset));
            gust.minDistance = blowOutMinDistance;
            gust.PlayOneShot(blowOutClip, blowOutVolume);
            Destroy(gust.gameObject, blowOutClip.length + 0.25f);
            if (blowOutLead > 0f)
                yield return new WaitForSeconds(blowOutLead);
        }

        foreach (Renderer r in bodyRenderers)
            if (r != null)
                r.enabled = false;

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
        if (runClip == null)
            yield break;

        AudioSource steps = CreateSource("Corpse Run Audio", _lookPoint);
        steps.clip = runClip;
        steps.volume = runVolume;
        steps.minDistance = runMinDistance;
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
                AudioSource can = CreateSource("Corpse Mid Run Audio", steps.transform.position);
                can.minDistance = runMinDistance;
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
        AudioSource clue = CreateSource("Corpse Clue Audio", _lookPoint);
        clue.minDistance = pinClueMinDistance;
        clue.PlayOneShot(clip, volume);
        Destroy(clue.gameObject, clip.length + 0.25f);
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

    AudioSource CreateSource(string sourceName, Vector3 position)
    {
        var go = new GameObject(sourceName);
        go.transform.SetParent(transform, false);
        go.transform.position = position;
        var src = go.AddComponent<AudioSource>();
        src.playOnAwake = false;
        src.outputAudioMixerGroup = mixerGroup;
        src.spatialBlend = 1f;
        src.rolloffMode = AudioRolloffMode.Logarithmic;
        src.minDistance = 1.2f;
        src.maxDistance = 20f;
        src.dopplerLevel = 0f;
        return src;
    }
}
