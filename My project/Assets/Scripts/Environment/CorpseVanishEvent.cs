using System.Collections;
using System.Collections.Generic;
using FS_Atmo;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.Events;

/// <summary>
/// Cage room corpse scare, part 1 (scripted):
/// 1. The first padlock pin the player sets: a clue sound from the body.
/// 2. The player first walks out of the open cage: a dedicated Cinemachine camera takes over and dolly-zooms onto
///    the body (pushes in while the lens widens) in silence; HUD and viewmodel are hidden meanwhile. The body gives
///    another clue shortly after control returns.
/// 3. The player opens the cage room door: the body is gone, the lighter (and any lit candles nearby) go out,
///    and running footsteps tear away from where he lay, kicking a tin can halfway. The lighter then only sparks
///    for a few strikes before it catches again.
/// </summary>
[DisallowMultipleComponent]
public class CorpseVanishEvent : MonoBehaviour
{
    public enum Stage { WaitingForPin, WaitingForExit, Focusing, WaitingForDoor, Done }

    const string FocusLockReason = "corpse_focus";

    [Header("Body")]
    [Tooltip("Hidden when the lights go out.")]
    [SerializeField] Renderer[] bodyRenderers;
    [Tooltip("Point the view turns to; sounds and the run start here. Empty = centre of the body renderers.")]
    [SerializeField] Transform lookTarget;

    [Header("1. First padlock pin")]
    [SerializeField] AudioClip pinClue;
    [SerializeField, Range(0f, 1f)] float pinClueVolume = 0.8f;
    [Tooltip("Seconds after the pin sets before the clue plays.")]
    [SerializeField] float pinClueDelay = 0.8f;

    [Header("2. Leaving the cage")]
    [SerializeField] SimpleOpenClose cageDoor;
    [Tooltip("Seconds the player has to be out of the cage before the view turns (trigger edges flicker).")]
    [SerializeField] float exitConfirmTime = 0.3f;
    [Tooltip("Dedicated camera for the shot; its priority is raised while it plays (blend times come from the brain's custom blends).")]
    [SerializeField] CinemachineCamera focusCamera;
    [SerializeField] int focusPriority = 100;
    [Tooltip("The camera dollies from here to Focus Shot End, always looking at the body.")]
    [SerializeField] Transform focusShotStart;
    [SerializeField] Transform focusShotEnd;
    [Tooltip("Lens at the start; it widens as the camera pushes in so the body keeps its size on screen (dolly zoom).")]
    [SerializeField] float focusStartFov = 38f;
    [SerializeField] float focusDollyTime = 3.2f;
    [Tooltip("Seconds held at the end of the dolly before blending back to the player.")]
    [SerializeField] float focusEndHold = 0.4f;
    [Tooltip("Seconds of the blend back to the player view; control returns after it.")]
    [SerializeField] float focusBlendOut = 1f;
    [Tooltip("Lights the body for the shot when the player's lighter isn't burning.")]
    [SerializeField] Light focusFillLight;
    [Tooltip("Played one after another after the shot, never during it, so the player isn't sure what they saw.")]
    [SerializeField] AudioClip[] focusClues;
    [Tooltip("Seconds after the shot ends (control back) before the first clue.")]
    [SerializeField] float focusClueDelay = 1f;
    [SerializeField, Range(0f, 1f)] float focusClueVolume = 0.85f;
    [SerializeField] float focusClueGap = 0.2f;

    [Header("3. Opening the cage room door")]
    [SerializeField] SimpleOpenClose exitDoor;
    [Tooltip("Seconds after the door starts opening before the lights go out.")]
    [SerializeField] float lightsOutDelay = 0.35f;
    [Tooltip("Lit candles within this distance of the body go out with the lighter.")]
    [SerializeField] float candleRadius = 12f;
    [Tooltip("Optional breath next to the player's ear as the flame dies. Empty = silent.")]
    [SerializeField] AudioClip blowOutClip;
    [SerializeField, Range(0f, 1f)] float blowOutVolume = 0.9f;
    [SerializeField] Vector3 blowOutOffset = new Vector3(-0.3f, 0.05f, -0.25f);
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

    [Header("Audio")]
    [SerializeField] AudioMixerGroup mixerGroup;

    [Header("Events")]
    public UnityEvent onFocusStarted;
    public UnityEvent onLightsOut;

    public Stage CurrentStage => _stage;

    Stage _stage;
    Vector3 _lookPoint;
    AudioSource _bodySource;
    bool _wasInCage;
    float _outsideTimer;
    bool _exitDoorWasOpen;
    bool _focusActive;
    readonly List<Renderer> _hiddenViewmodel = new List<Renderer>();

    void Awake()
    {
        _lookPoint = lookTarget != null ? lookTarget.position : BodyCenter();
        _bodySource = CreateSource("Corpse Audio", _lookPoint);
        if (focusCamera != null)
            focusCamera.Priority = 0;
        if (focusFillLight != null)
            focusFillLight.enabled = false;
    }

    void OnEnable() => PadlockLockpickMinigame.PinSet += OnPinSet;

    void OnDisable()
    {
        PadlockLockpickMinigame.PinSet -= OnPinSet;
        EndFocus();
    }

    void OnPinSet(PadlockLockpickMinigame padlock)
    {
        if (_stage != Stage.WaitingForPin)
            return;
        _stage = Stage.WaitingForExit;
        StartCoroutine(PlayDelayed(pinClue, pinClueVolume, pinClueDelay));
    }

    void Update()
    {
        if (_stage == Stage.WaitingForPin || _stage == Stage.WaitingForExit)
            TickCageExit();
        else if (_stage == Stage.WaitingForDoor)
            TickExitDoor();
    }

    // The pin clue is optional for the exit: a player who somehow skips it still gets the focus.
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
        if (_outsideTimer >= exitConfirmTime)
            StartCoroutine(FocusRoutine());
    }

    void TickExitDoor()
    {
        bool open = exitDoor != null && exitDoor.objectOpen;
        if (open && !_exitDoorWasOpen)
        {
            _stage = Stage.Done;
            StartCoroutine(LightsOutRoutine());
        }
        _exitDoorWasOpen = open;
    }

    IEnumerator FocusRoutine()
    {
        _stage = Stage.Focusing;
        if (focusCamera == null || focusShotStart == null || focusShotEnd == null)
        {
            _stage = Stage.WaitingForDoor;
            yield break;
        }

        BeginFocus();
        onFocusStarted?.Invoke();

        Vector3 start = focusShotStart.position;
        Vector3 end = focusShotEnd.position;
        float startDistance = Mathf.Max(0.01f, Vector3.Distance(start, _lookPoint));
        float halfTan = Mathf.Tan(focusStartFov * 0.5f * Mathf.Deg2Rad);
        PoseFocusCamera(start, focusStartFov);
        focusCamera.Priority = focusPriority;

        for (float t = 0f; t < focusDollyTime; t += Time.deltaTime)
        {
            Vector3 pos = Vector3.Lerp(start, end, Mathf.SmoothStep(0f, 1f, t / focusDollyTime));
            // Keep the body's size on screen while the camera closes in: the background stretches away.
            float distance = Mathf.Max(0.01f, Vector3.Distance(pos, _lookPoint));
            float fov = 2f * Mathf.Atan(halfTan * startDistance / distance) * Mathf.Rad2Deg;
            PoseFocusCamera(pos, Mathf.Clamp(fov, 1f, 120f));
            yield return null;
        }
        if (focusEndHold > 0f)
            yield return new WaitForSeconds(focusEndHold);

        // The blend back lands on the player looking at where the body lies.
        FacePlayerToBody();
        focusCamera.Priority = 0;
        if (focusBlendOut > 0f)
            yield return new WaitForSeconds(focusBlendOut);

        EndFocus();
        _exitDoorWasOpen = exitDoor != null && exitDoor.objectOpen;
        _stage = Stage.WaitingForDoor;
        StartCoroutine(PlayFocusClues());
    }

    void PoseFocusCamera(Vector3 position, float fov)
    {
        focusCamera.transform.SetPositionAndRotation(position, Quaternion.LookRotation(_lookPoint - position));
        LensSettings lens = focusCamera.Lens;
        lens.FieldOfView = fov;
        focusCamera.Lens = lens;
    }

    void BeginFocus()
    {
        _focusActive = true;
        PlayerControlGate.Acquire(FocusLockReason);
        GameplayHudVisibility.Instance?.HideGameplayHud();

        // Hands and lighter ride on the main camera; hide them for the shot but keep their lights on.
        _hiddenViewmodel.Clear();
        Camera cam = Camera.main;
        if (cam != null)
            foreach (Renderer r in cam.GetComponentsInChildren<Renderer>())
                if (r.enabled)
                {
                    r.enabled = false;
                    _hiddenViewmodel.Add(r);
                }

        if (focusFillLight != null)
            focusFillLight.enabled = !LighterLit();
    }

    void EndFocus()
    {
        if (!_focusActive)
            return;
        _focusActive = false;

        if (focusCamera != null)
            focusCamera.Priority = 0;
        if (focusFillLight != null)
            focusFillLight.enabled = false;
        foreach (Renderer r in _hiddenViewmodel)
            if (r != null)
                r.enabled = true;
        _hiddenViewmodel.Clear();
        GameplayHudVisibility.Instance?.ShowGameplayHud();
        PlayerControlGate.Release(FocusLockReason);
    }

    void FacePlayerToBody()
    {
        var mouseLook = FindFirstObjectByType<CinemachineMouseLook>();
        var controller = mouseLook != null ? mouseLook.GetComponentInParent<CharacterController>() : null;
        if (controller == null)
            return;

        Vector3 flat = _lookPoint - controller.transform.position;
        controller.transform.rotation = Quaternion.Euler(0f, Mathf.Atan2(flat.x, flat.z) * Mathf.Rad2Deg, 0f);
        Vector3 toBody = _lookPoint - mouseLook.transform.position;
        float horizontal = new Vector2(toBody.x, toBody.z).magnitude;
        float pitch = Mathf.Clamp(Mathf.Atan2(-toBody.y, horizontal) * Mathf.Rad2Deg, -80f, 80f);
        mouseLook.transform.localRotation = Quaternion.Euler(pitch, 0f, 0f);
        mouseLook.SyncPitchFromLocalRotation();
    }

    static bool LighterLit()
    {
        HeldItemView view = HeldItemController.Instance != null ? HeldItemController.Instance.CurrentView : null;
        return view != null && view.TryGetComponent(out HeldLighter lighter) && lighter.IsLit;
    }

    IEnumerator LightsOutRoutine()
    {
        if (lightsOutDelay > 0f)
            yield return new WaitForSeconds(lightsOutDelay);

        foreach (Renderer r in bodyRenderers)
            if (r != null)
                r.enabled = false;

        Camera cam = Camera.main;
        if (blowOutClip != null && cam != null)
        {
            AudioSource breath = CreateSource("Lighter Blow Audio", cam.transform.TransformPoint(blowOutOffset));
            breath.minDistance = 0.3f;
            breath.PlayOneShot(blowOutClip, blowOutVolume);
            Destroy(breath.gameObject, blowOutClip.length + 0.25f);
        }

        // Also when the lighter isn't in hand: its view stays in the scene, so the misfires wait for the next draw.
        HeldLighter lighter = FindFirstObjectByType<HeldLighter>(FindObjectsInactive.Include);
        if (lighter != null)
        {
            if (lighter.IsLit)
                lighter.Extinguish();
            lighter.MisfireNextStrikes(lighterMisfires);
        }
        foreach (LightableCandle candle in FindObjectsByType<LightableCandle>(FindObjectsSortMode.None))
            if (candle.IsLit && Vector3.Distance(candle.transform.position, _lookPoint) <= candleRadius)
                candle.SetLit(false);
        onLightsOut?.Invoke();

        if (runDelay > 0f)
            yield return new WaitForSeconds(runDelay);
        yield return RunRoutine();
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

    IEnumerator PlayFocusClues()
    {
        if (focusClues == null)
            yield break;
        if (focusClueDelay > 0f)
            yield return new WaitForSeconds(focusClueDelay);
        foreach (AudioClip clip in focusClues)
        {
            if (clip == null || _stage == Stage.Done)
                continue;
            _bodySource.PlayOneShot(clip, focusClueVolume);
            yield return new WaitForSeconds(clip.length + focusClueGap);
        }
    }

    IEnumerator PlayDelayed(AudioClip clip, float volume, float delay)
    {
        if (clip == null)
            yield break;
        if (delay > 0f)
            yield return new WaitForSeconds(delay);
        _bodySource.PlayOneShot(clip, volume);
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
