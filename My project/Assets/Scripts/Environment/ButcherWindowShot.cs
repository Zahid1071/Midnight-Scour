using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

/// <summary>
/// Plays once, the moment the player steps through the frame of the basement exit door. From their own view it closes
/// in on the car the way the hide shot does, a light coming up over it, then turns heavily to a window of the house where the
/// butcher stands lit from behind, staring out.
/// It is the real butcher, moved there and held for the stare: he wheels round and sets off on a relentless hunt for
/// the player, and control returns once the view has blended back, his chase music taking over until the hunt ends.
/// Installs itself in any scene with Outdoor Fog unless the scene already has one (put it in the scene to assign the
/// sounds); left empty, the car, window and butcher are found when it plays.
/// The object must be named "Butcher Window Camera": the brain's custom blends cut into it and ease out of it by name.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(CinemachineCamera))]
public class ButcherWindowShot : MonoBehaviour
{
    const string LockReason = "butcher_window_shot";
    const string CameraName = "Butcher Window Camera";

    [Header("Trigger")]
    [Tooltip("Camera height (world Y) that counts as out of the basement; matches Outdoor Fog's full-fog height.")]
    [SerializeField] float outdoorEyeY = 0.3f;
    [Tooltip("The door out of the basement, at the top of the stairs. Left empty, it is found by the name below.")]
    [SerializeField] Transform basementExit;
    [SerializeField] string basementExitName = "Door Basement Exit";
    [Tooltip("Metres past the basement exit door's frame the player's eye must be before the shot starts.")]
    [SerializeField] float exitClearance = 0f;
    [SerializeField] int priority = 100;

    [Header("Car")]
    [Tooltip("Falls back to the car holding the escape point.")]
    [SerializeField] Transform car;
    [Tooltip("How much of the frame the car fills at the end of the move.")]
    [SerializeField, Range(0.1f, 1f)] float carFrameFill = 0.6f;
    [Tooltip("Metres the camera pulls back from the car while the lens zooms (less if something is behind). Negative " +
             "pushes in toward it instead, short of anything in the way.")]
    [SerializeField] float pull = -3.5f;
    [SerializeField] float carMoveTime = 2.2f;
    [Tooltip("Share of the move (0-1) by which the view has turned onto the car.")]
    [SerializeField, Range(0.05f, 1f)] float carTurn = 0.35f;
    [Tooltip("Share of the move (0-1) over which the lens brings the car up to its frame fill; for the rest it only holds " +
             "its size while the camera keeps moving (widening as it pushes in, narrowing as it pulls back).")]
    [SerializeField, Range(0.05f, 1f)] float carZoom = 0.45f;
    [SerializeField] float carHold = 0.5f;
    [Tooltip("Shown as the view lands on the car.")]
    [SerializeField] string carSubtitleKey = "gameplay.thats_my_car";
    [SerializeField] float carSubtitleSeconds = 2.5f;

    [Header("Car Light")]
    [Tooltip("Comes up over the car as the view closes in so it reads in the dark, and dies as the view turns away.")]
    [SerializeField] Color carLightColor = new Color(1f, 0.72f, 0.45f);
    [SerializeField] float carLightIntensity = 8f;
    [SerializeField] float carLightRange = 9f;
    [Tooltip("Where the light sits: metres out from the car toward the camera (x) and up from its roof (y).")]
    [SerializeField] Vector2 carLightOffset = new Vector2(1.2f, 1.5f);

    [Header("Window")]
    [Tooltip("At the middle of the window glass, blue axis pointing out of the house. Left empty, the best window " +
             "the player can see is picked from the walls named below.")]
    [SerializeField] Transform window;
    [SerializeField] string windowWallPrefix = "WallOut_3x_window";
    [SerializeField] float maxWindowDistance = 90f;
    [Tooltip("Spawned at the window only if the scene has no butcher at all; normally the scene's butcher is moved " +
             "there, switched on first if he's off.")]
    [SerializeField] GameObject butcherPrefab;
    [Tooltip("Metres from the window spot to search for the navmesh he is placed on.")]
    [SerializeField] float standNavRadius = 2f;
    [Tooltip("Where his feet go relative to the window marker: x right, y up, z out of the house.")]
    [SerializeField] Vector3 standOffset = new Vector3(0f, -1.6f, -0.8f);
    [Tooltip("How far inside a picked window he stands.")]
    [SerializeField] float standDepth = 0.9f;
    [Tooltip("Metres of height the window shot takes in; smaller is tighter.")]
    [SerializeField] float windowFrameHeight = 2.4f;
    [Tooltip("Seconds of the head turn from the car to the window; the view lags a little behind and settles after.")]
    [SerializeField] float lookTime = 1.4f;
    [Tooltip("How quickly the view follows the turn (Hz); lower feels heavier and lags more.")]
    [SerializeField] float lookFrequency = 1.3f;
    [Tooltip("Damping of the turn: below 1 it swings a touch past the window and settles back.")]
    [SerializeField, Range(0.3f, 1f)] float lookDamping = 0.6f;
    [Tooltip("Degrees the view rolls into the turn at its fastest.")]
    [SerializeField] float lookLean = 2.5f;
    [Tooltip("Degrees the view dips while turning, like a head swinging round.")]
    [SerializeField] float lookDip = 1.5f;
    [Tooltip("Share of the turn (0-1) before the lens starts zooming toward him.")]
    [SerializeField, Range(0f, 0.9f)] float zoomDelay = 0.25f;
    [Tooltip("Seconds the zoom carries on past the end of the turn, easing into the stare's creep.")]
    [SerializeField] float zoomSettle = 0.5f;
    [Tooltip("Share of the turn (0-1) at which the sting plays, as he comes into view.")]
    [SerializeField, Range(0f, 1f)] float stingAt = 0.55f;
    [Tooltip("Seconds he stands staring while the lens creeps in.")]
    [SerializeField] float stareTime = 1.6f;
    [Tooltip("Field of view at the end of the stare, as a share of where it started.")]
    [SerializeField, Range(0.5f, 1f)] float stareCreep = 0.85f;
    [Tooltip("Outdoor fog density while on the window, as a share of normal, so he reads from across the yard.")]
    [SerializeField, Range(0f, 1f)] float windowFog = 0.25f;

    [Header("Sound (all played flat, through the SFX mixer)")]
    [Tooltip("Played as the shot cuts in from the player's view.")]
    [SerializeField] AudioClip startSfx;
    [SerializeField, Range(0f, 1f)] float startVolume = 1f;
    [Tooltip("Played as the view lands on the car and its light is full.")]
    [SerializeField] AudioClip carSfx;
    [SerializeField, Range(0f, 1f)] float carVolume = 1f;
    [Tooltip("Played as the view starts turning from the car to the window.")]
    [SerializeField] AudioClip turnSfx;
    [SerializeField, Range(0f, 1f)] float turnVolume = 1f;
    [Tooltip("Played as the butcher comes into view in the window (Sting At, under Window).")]
    [SerializeField] AudioClip sting;
    [SerializeField, Range(0f, 1f)] float stingVolume = 1f;
    [Tooltip("Played as he wheels round and sets off after the player.")]
    [SerializeField] AudioClip butcherTurnSfx;
    [SerializeField, Range(0f, 1f)] float butcherTurnVolume = 1f;
    [Tooltip("Played as the light in the empty window dies.")]
    [SerializeField] AudioClip lightOutSfx;
    [SerializeField, Range(0f, 1f)] float lightOutVolume = 1f;

    [Header("Chase Music")]
    [Tooltip("Takes over the music as the view blends back to the player, until the hunt ends (capture or Continue).")]
    [SerializeField] AudioClip chaseMusic;

    [Header("Run")]
    [Tooltip("Played on him when he is put at the window, so a seat or routine pose doesn't carry over.")]
    [SerializeField] string locomotionState = "Base Blend";
    [Tooltip("Seconds he takes to wheel round toward the way out before the hunt starts.")]
    [SerializeField] float turnTime = 0.25f;
    [Tooltip("Seconds the view follows him after he turns, panning with him as he runs off after the player.")]
    [SerializeField] float runTime = 2f;
    [Tooltip("Height on him (metres above his feet) the view follows.")]
    [SerializeField] float followHeight = 1.4f;
    [Tooltip("How tightly the view keeps up with him; lower lags further behind, like a head swinging after him.")]
    [SerializeField] float followSharpness = 5f;
    [Tooltip("Field of view at the end of the follow, as a multiple of the stare's, so his run reads in the frame.")]
    [SerializeField, Range(1f, 2f)] float followWiden = 1.35f;
    [Tooltip("Seconds the window light takes to die once he has gone, the view still on him, before it goes back.")]
    [SerializeField] float emptyHold = 0.45f;
    [Tooltip("Seconds of the blend back to the player view (the brain's custom blend); control returns after it.")]
    [SerializeField] float blendOut = 0.8f;

    [Header("Backlight")]
    [SerializeField] Color backlightColor = new Color(1f, 0.55f, 0.25f);
    [SerializeField] float backlightIntensity = 8f;
    [SerializeField] float backlightRange = 7f;
    [Tooltip("Where the light sits: metres behind him, deeper into the house (x), and up from his feet (y).")]
    [SerializeField] Vector2 backlightOffset = new Vector2(1.6f, 1.9f);
    [SerializeField, Range(0f, 1f)] float flicker = 0.15f;

    /// <summary>True once the shot has played in this scene and handed control back to the player.</summary>
    public static bool HasFinished { get; private set; }

    /// <summary>The player is out of the basement and the shot is about to take over; nothing has moved yet.</summary>
    public event Action Triggered;

    CinemachineCamera _cam;
    NPCController[] _npcs;
    bool _played;
    bool _locked;
    NPCController _held;
    bool _heldHearingDisabled;
    NPCController _hunter;
    bool _chaseMusicOn;
    bool _runPromptOn;
    Light _backlight;
    Light _carLight;
    bool _hasExit;
    Vector3 _exitPoint, _exitOut;
    readonly List<Renderer> _hiddenViewmodel = new List<Renderer>();

    CinemachineMouseLook _player;
    readonly List<Transform> _windowWalls = new List<Transform>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Hook()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
        Install();
    }

    static void OnSceneLoaded(Scene scene, LoadSceneMode mode) => Install();

    static void Install()
    {
        HasFinished = false;
        if (FindFirstObjectByType<OutdoorFog>() == null || FindFirstObjectByType<ButcherWindowShot>(FindObjectsInactive.Include) != null)
            return;
        new GameObject(CameraName).AddComponent<ButcherWindowShot>();
    }

    void Awake()
    {
        _cam = GetComponent<CinemachineCamera>();
        _cam.Priority = 0;
        if (gameObject.name != CameraName)
            Debug.LogWarning($"ButcherWindowShot: name this object \"{CameraName}\" or its custom blends won't apply.", this);
    }

    void Start()
    {
        _npcs = FindObjectsByType<NPCController>(FindObjectsSortMode.None);
        _player = FindFirstObjectByType<CinemachineMouseLook>();
        if (window == null)
            CollectWindows();
        FindExit();
    }

    // The exit door sits in the outer wall of the basement: outward is whichever way faces away from the rest of it.
    void FindExit()
    {
        if (basementExit == null && !string.IsNullOrEmpty(basementExitName))
        {
            GameObject found = GameObject.Find(basementExitName);
            if (found != null)
                basementExit = found.transform;
        }
        if (basementExit == null)
        {
            Debug.LogWarning($"ButcherWindowShot: no basement exit \"{basementExitName}\"; going by eye height alone.", this);
            return;
        }
        _exitPoint = basementExit.position;
        _exitOut = Flat(basementExit.forward).normalized;
        if (Vector3.Dot(Flat(BoundsOf(basementExit.root).center - _exitPoint), _exitOut) > 0f)
            _exitOut = -_exitOut;
        _hasExit = true;
    }

    bool OutOfBasement(Vector3 eye) =>
        eye.y >= outdoorEyeY && (!_hasExit || Vector3.Dot(Flat(eye - _exitPoint), _exitOut) >= exitClearance);

    void OnDisable()
    {
        Stop();
        HideRunPrompt();
        EndChaseMusic();
    }

    void Update()
    {
        // Before the music: ending it forgets the hunter.
        if (_runPromptOn && !Hunting())
            HideRunPrompt();
        if (_chaseMusicOn && !Hunting())
            EndChaseMusic();
        if (_played)
            return;
        if (_player == null)
            _player = FindFirstObjectByType<CinemachineMouseLook>();
        // The player's own eye, not the main camera, which rides intro and other shots.
        if (_player == null || !OutOfBasement(_player.transform.position) || PlayerControlGate.IsInputBlocked)
            return;
        // The butcher is spawned in after the scene loads.
        if (_npcs == null || _npcs.Length == 0)
            _npcs = FindObjectsByType<NPCController>(FindObjectsSortMode.None);
        if (Chased())
            return;
        Triggered?.Invoke();
        StartCoroutine(Play());
    }

    struct Aim
    {
        public Vector3 look, outward, feet;
        public string name;
    }

    bool Chased()
    {
        if (_npcs == null)
            return false;
        foreach (NPCController npc in _npcs)
            if (npc != null && (npc.currentState == NPCController.AIState.Chase || npc.currentState == NPCController.AIState.Capture))
                return true;
        return false;
    }

    IEnumerator Play()
    {
        _played = true;
        Camera main = Camera.main;
        if (main == null)
            yield break;
        Vector3 eye = main.transform.position;

        NPCController butcher = FindButcher(out bool woken);
        if (butcher == null && butcherPrefab == null)
        {
            Debug.LogWarning("ButcherWindowShot: no butcher to put in the window.", this);
            yield break;
        }
        Begin(main);
        // A butcher just switched on runs his Start next frame, which would kick his routine over the hold.
        if (woken)
            yield return null;

        Vector3 pos = eye;
        Quaternion rot = main.transform.rotation;
        float fov = main.fieldOfView;
        Pose(pos, rot, fov);
        _cam.Priority = priority;
        PlaySfx(startSfx, startVolume);

        // The car, in one move: turn to it (landing early) while the camera travels and the lens zooms at once, the
        // light coming up over it. A push stops where holding the frame would need a lens wider than the player's.
        Transform carTarget = car != null ? car : FallbackCar();
        if (carTarget != null)
        {
            Bounds bounds = BoundsOf(carTarget);
            Vector3 look = bounds.center;
            SpawnCarLight(new Vector3(look.x, bounds.max.y, look.z) + Flat(pos - look).normalized * carLightOffset.x + Vector3.up * carLightOffset.y);
            float size = bounds.extents.magnitude;
            float distance = Mathf.Max(0.5f, Vector3.Distance(pos, look));
            Vector3 dir = (look - pos).normalized;
            float fromFov = fov;
            float travel = pull >= 0f
                ? -Mathf.Clamp(Clearance(pos, -dir, pull + 0.4f, null), 0f, pull)
                : Mathf.Clamp(Mathf.Min(-pull, distance - size / (carFrameFill * Mathf.Tan(fromFov * 0.5f * Mathf.Deg2Rad)),
                    Clearance(pos, dir, distance, carTarget)), 0f, -pull);
            Vector3 from = pos;
            Vector3 to = pos + dir * travel;
            Quaternion fromRot = rot;
            float fromFill = size / (distance * Mathf.Tan(fov * 0.5f * Mathf.Deg2Rad));
            bool landed = false;
            for (float t = 0f; t < carMoveTime; t += Time.deltaTime)
            {
                float k = t / carMoveTime;
                if (!landed && k >= carTurn)
                {
                    landed = true;
                    PlaySfx(carSfx, carVolume);
                    ShowCarSubtitle();
                }
                float u = SmootherStep(k);
                pos = Vector3.Lerp(from, to, u);
                rot = Quaternion.Slerp(fromRot, Quaternion.LookRotation(look - pos), SmootherStep(k / carTurn));
                float fill = Mathf.Lerp(fromFill, carFrameFill, SmootherStep(k / carZoom));
                fov = Mathf.Clamp(2f * Mathf.Atan(size / (fill * Vector3.Distance(pos, look))) * Mathf.Rad2Deg, 3f, fromFov);
                Pose(pos, rot, fov);
                SetCarLight(k / carTurn);
                yield return null;
            }
            pos = to;
            rot = Quaternion.LookRotation(look - pos);
            Pose(pos, rot, fov);
            SetCarLight(1f);
            if (!landed)
            {
                PlaySfx(carSfx, carVolume);
                ShowCarSubtitle();
            }
            if (carHold > 0f)
                yield return new WaitForSeconds(carHold);
        }

        // Picked from where the car move ended: the doorway itself can have every window walled off.
        if (!TryAim(pos, true, out Aim aim) && !TryAim(pos, false, out aim))
        {
            Debug.LogWarning("ButcherWindowShot: no window to show the butcher in.", this);
            _cam.Priority = 0;
            yield return new WaitForSeconds(blendOut);
            Stop();
            yield break;
        }
        Vector3 windowLook = aim.look, outward = aim.outward, feet = aim.feet;
        Debug.Log($"ButcherWindowShot: playing at {aim.name} ({Vector3.Distance(pos, windowLook):F0} m).", this);
        Quaternion facingCamera = Quaternion.LookRotation(Flat(pos - feet));
        if (butcher == null)
        {
            butcher = SpawnButcher(feet, facingCamera);
            if (butcher != null)
                yield return null;
        }
        if (butcher == null || !HoldAtWindow(butcher, feet, facingCamera))
            Debug.LogWarning("ButcherWindowShot: couldn't put the butcher in the window.", this);
        SpawnBacklight(feet - outward * backlightOffset.x + Vector3.up * backlightOffset.y);

        // The turn and the stare, as one move: the head swings off the car with weight (slow to start, lagging the
        // turn, drifting a touch past the window and settling), leaning into it, while the lens zooms in from partway
        // through and runs on into the slow creep of the stare.
        float windowDistance = Mathf.Max(0.5f, Vector3.Distance(pos, windowLook));
        float windowFov = Mathf.Clamp(2f * Mathf.Atan(windowFrameHeight * 0.5f / windowDistance) * Mathf.Rad2Deg, 3f, 60f);
        {
            Quaternion fromRot = rot;
            Quaternion toRot = Quaternion.LookRotation(windowLook - pos);
            float side = Mathf.Sign(Vector3.SignedAngle(Flat(fromRot * Vector3.forward), Flat(toRot * Vector3.forward), Vector3.up));
            float headTime = Mathf.Max(0.05f, lookTime);
            float omega = 2f * Mathf.PI * Mathf.Max(0.1f, lookFrequency);
            float peakSpeed = 1.875f / headTime;
            float zoomStart = zoomDelay * headTime;
            float zoomLength = Mathf.Max(0.05f, headTime - zoomStart + zoomSettle);
            float logFrom = Mathf.Log(fov), logTo = Mathf.Log(windowFov), logCreep = Mathf.Log(stareCreep);
            float x = 0f, v = 0f;
            bool stung = false;
            float total = headTime + stareTime;
            PlaySfx(turnSfx, turnVolume);
            for (float t = 0f; t < total; t += Time.deltaTime)
            {
                const float step = 1f / 240f;
                float dt = Time.deltaTime;
                int steps = Mathf.Max(1, Mathf.CeilToInt(dt / step));
                float h = dt / steps;
                for (int i = 0; i < steps; i++)
                {
                    float target = SmootherStep((t + h * (i + 1)) / headTime);
                    v += (omega * omega * (target - x) - 2f * lookDamping * omega * v) * h;
                    x += v * h;
                }

                float speed = Mathf.Clamp(v / peakSpeed, -1f, 1f);
                Quaternion sway = Quaternion.Euler(lookDip * Mathf.Abs(speed), 0f, -side * lookLean * speed);
                Quaternion look = Quaternion.SlerpUnclamped(fromRot, toRot, x) * sway;
                float zoom = SmootherStep((t - zoomStart) / zoomLength);
                float creep = SmootherStep((t - headTime) / Mathf.Max(0.01f, stareTime));
                fov = Mathf.Exp(Mathf.Lerp(logFrom, logTo, zoom) + logCreep * creep);
                Pose(pos, look, fov);

                float turned = Mathf.Clamp01(x);
                OutdoorFog.DensityScale = Mathf.Lerp(1f, windowFog, turned);
                SetCarLight(1f - Mathf.Clamp01(turned * 1.5f));
                if (!stung && t >= stingAt * headTime)
                {
                    stung = true;
                    PlaySfx(sting, stingVolume);
                }
                if (_held != null)
                    _held.poiLookDirector?.TickLockedPlayerLook();
                FlickerBacklight(1f);
                yield return null;
            }
            rot = toRot;
            fov = windowFov * stareCreep;
            Pose(pos, rot, fov);
            OutdoorFog.DensityScale = windowFog;
            SetCarLight(0f);
        }

        // The run: he wheels round toward the way out and goes after the player for real.
        Transform runner = null;
        if (_held != null)
        {
            PlaySfx(butcherTurnSfx, butcherTurnVolume);
            runner = _held.transform;
            Quaternion facing = runner.rotation;
            Quaternion away = Quaternion.LookRotation(RunDirection(_held, runner.position, -outward));
            for (float t = 0f; t < turnTime && _held != null; t += Time.deltaTime)
            {
                runner.rotation = Quaternion.Slerp(facing, away, SmootherStep(t / Mathf.Max(0.01f, turnTime)));
                FlickerBacklight(1f);
                yield return null;
            }
            ReleaseIntoHunt();
        }

        // The view swings after him as he runs, lagging a little, the lens easing wider; once he's gone the window
        // light dies behind him while the view stays on him.
        Vector3 lastSeen = windowLook;
        {
            float fromFov = fov;
            float followTotal = runTime + emptyHold;
            bool lightOut = false;
            for (float t = 0f; t < followTotal; t += Time.deltaTime)
            {
                if (runner != null)
                    lastSeen = runner.position + Vector3.up * followHeight;
                Vector3 toTarget = lastSeen - pos;
                if (toTarget.sqrMagnitude > 0.01f)
                    rot = Quaternion.Slerp(rot, Quaternion.LookRotation(toTarget), 1f - Mathf.Exp(-followSharpness * Time.deltaTime));
                fov = Mathf.Lerp(fromFov, Mathf.Min(fromFov * followWiden, 60f), SmootherStep(t / Mathf.Max(0.01f, runTime)));
                Pose(pos, rot, fov);

                if (t < runTime)
                    FlickerBacklight(1f);
                else
                {
                    if (!lightOut)
                    {
                        lightOut = true;
                        PlaySfx(lightOutSfx, lightOutVolume);
                    }
                    FlickerBacklight(1f - (t - runTime) / Mathf.Max(0.01f, emptyHold));
                }
                yield return null;
            }
            if (!lightOut)
                PlaySfx(lightOutSfx, lightOutVolume);
            FlickerBacklight(0f);
        }

        // The player is left facing where he was last seen as the view blends back.
        CinemachineMouseLook mouseLook = FindFirstObjectByType<CinemachineMouseLook>();
        if (mouseLook != null)
            mouseLook.SetLook(Quaternion.LookRotation(Flat(lastSeen - eye)));
        _cam.Priority = 0;
        StartChaseMusic();
        for (float t = 0f; t < blendOut; t += Time.deltaTime)
        {
            OutdoorFog.DensityScale = Mathf.Lerp(windowFog, 1f, SmootherStep(t / blendOut));
            yield return null;
        }
        Stop();
        ShowRunPrompt();
    }

    /// <summary>Ends the shot at once and gives control back.</summary>
    public void Stop()
    {
        if (_cam != null)
            _cam.Priority = 0;
        OutdoorFog.DensityScale = 1f;
        ReleaseIntoHunt();
        if (_backlight != null)
            Destroy(_backlight.gameObject);
        if (_carLight != null)
            Destroy(_carLight.gameObject);
        foreach (Renderer r in _hiddenViewmodel)
            if (r != null)
                r.enabled = true;
        _hiddenViewmodel.Clear();
        if (_played)
            HasFinished = true;
        if (!_locked)
            return;
        _locked = false;
        GameplayHudVisibility.Instance?.ShowGameplayHud();
        PlayerControlGate.Release(LockReason);
    }

    void Begin(Camera main)
    {
        _locked = true;
        PlayerControlGate.Acquire(LockReason);
        GameplayHudVisibility.Instance?.HideGameplayHud();
        // Hands and lighter ride on the main camera; hide them for the shot but keep their lights on.
        _hiddenViewmodel.Clear();
        foreach (Renderer r in main.GetComponentsInChildren<Renderer>())
            if (r.enabled)
            {
                r.enabled = false;
                _hiddenViewmodel.Add(r);
            }
    }

    // Small windows are too small to show him from across the yard.
    void CollectWindows()
    {
        foreach (Transform t in FindObjectsByType<Transform>(FindObjectsSortMode.None))
            if (t.name.StartsWith(windowWallPrefix, StringComparison.OrdinalIgnoreCase)
                && t.name.IndexOf("door", StringComparison.OrdinalIgnoreCase) < 0
                && t.name.IndexOf("small", StringComparison.OrdinalIgnoreCase) < 0
                && (t.parent == null || !t.parent.name.StartsWith(windowWallPrefix, StringComparison.OrdinalIgnoreCase)))
                _windowWalls.Add(t);
    }

    // The marker window if there is one, else the window wall (in clear sight, if needSight) facing the camera most
    // squarely, close and on the floor nearest its eye.
    bool TryAim(Vector3 eye, bool needSight, out Aim aim)
    {
        aim = default;
        if (window != null)
        {
            aim.look = window.position;
            aim.outward = Flat(window.forward).normalized;
            aim.feet = window.position + window.rotation * standOffset;
            aim.name = window.name;
            float d = Vector3.Distance(eye, aim.look);
            return d <= maxWindowDistance && (!needSight || InSight(eye, aim.look, window));
        }

        float best = float.NegativeInfinity;
        foreach (Transform wall in _windowWalls)
        {
            if (wall == null || !wall.gameObject.activeInHierarchy)
                continue;
            Bounds b = BoundsOf(wall);
            Vector3 normal = b.size.x < b.size.z ? Vector3.right : Vector3.forward;
            Vector3 toEye = eye - b.center;
            float distance = toEye.magnitude;
            if (distance < 4f || distance > maxWindowDistance)
                continue;
            if (Vector3.Dot(normal, toEye) < 0f)
                normal = -normal;
            float facing = Vector3.Dot(normal, Flat(toEye).normalized);
            if (facing < 0.35f || (needSight && !InSight(eye, b.center, wall)))
                continue;
            float score = facing * 2f - distance / maxWindowDistance - Mathf.Abs(b.center.y - eye.y) * 0.5f;
            if (score <= best)
                continue;
            best = score;
            aim.look = b.center;
            aim.outward = normal;
            aim.name = wall.name;
        }
        if (float.IsNegativeInfinity(best))
            return false;

        Vector3 stand = aim.look - aim.outward * standDepth;
        aim.feet = Physics.Raycast(stand + Vector3.up * 0.3f, Vector3.down, out RaycastHit floor, 3f, ~0, QueryTriggerInteraction.Ignore)
            ? floor.point
            : stand + Vector3.down * 1.5f;
        return true;
    }

    static bool InSight(Vector3 eye, Vector3 target, Transform wall)
    {
        Vector3 dir = target - eye;
        float distance = dir.magnitude;
        foreach (RaycastHit hit in Physics.RaycastAll(eye, dir / distance, distance, ~0, QueryTriggerInteraction.Ignore))
        {
            if (hit.distance >= distance - 0.4f || hit.transform.IsChildOf(wall) || hit.collider.GetComponentInParent<CharacterController>() != null)
                continue;
            return false;
        }
        return true;
    }

    // The live butcher, or a switched-off one switched back on (woken). One held by another script is left alone.
    NPCController FindButcher(out bool woken)
    {
        woken = false;
        _npcs = FindObjectsByType<NPCController>(FindObjectsSortMode.None);
        foreach (NPCController npc in _npcs)
            if (CanHunt(npc))
                return npc;
        foreach (NPCController npc in FindObjectsByType<NPCController>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (npc == null || npc.isActiveAndEnabled || !npc.gameObject.scene.IsValid())
                continue;
            for (Transform t = npc.transform; t != null; t = t.parent)
                t.gameObject.SetActive(true);
            npc.enabled = true;
            woken = true;
            return npc;
        }
        return null;
    }

    NPCController SpawnButcher(Vector3 feet, Quaternion facing)
    {
        if (butcherPrefab == null)
            return null;
        if (NavMesh.SamplePosition(feet, out NavMeshHit hit, standNavRadius, NavMesh.AllAreas))
            feet = hit.position;
        NPCController npc = Instantiate(butcherPrefab, feet, facing).GetComponentInChildren<NPCController>();
        if (npc == null)
            Debug.LogWarning($"ButcherWindowShot: {butcherPrefab.name} has no NPCController.", this);
        return npc;
    }

    static bool CanHunt(NPCController npc) =>
        npc != null && npc.isActiveAndEnabled
        && npc.currentState != NPCController.AIState.Dead
        && npc.currentState != NPCController.AIState.Scripted
        && npc.currentState != NPCController.AIState.Capture;

    // Puts him at the window facing the camera, staring at the player, deaf and still until released.
    bool HoldAtWindow(NPCController butcher, Vector3 feet, Quaternion facing)
    {
        NavMeshAgent agent = butcher.GetComponent<NavMeshAgent>();
        if (agent == null || butcher.fsm == null || !CanHunt(butcher))
            return false;
        if (!agent.enabled)
            agent.enabled = true;
        if (!NavMesh.SamplePosition(feet, out NavMeshHit hit, standNavRadius, agent.areaMask))
        {
            Debug.LogWarning($"ButcherWindowShot: no navmesh within {standNavRadius} m of the window spot {feet}.", this);
            return false;
        }

        // Scripted drops any seat, routine, search or ambush on the spot and keeps perception from taking over.
        butcher.fsm.SetState(NPCController.AIState.Scripted);
        butcher.GetComponent<FS_Atmo.Navigator>()?.ClearRoute();
        _held = butcher;
        _heldHearingDisabled = butcher.HearingDisabled;
        butcher.SetHearingDisabled(true);

        agent.updatePosition = true;
        agent.updateRotation = true;
        if (!agent.Warp(hit.position))
        {
            ReleaseIntoHunt();
            return false;
        }
        butcher.transform.SetPositionAndRotation(hit.position, facing);
        agent.ResetPath();
        agent.velocity = Vector3.zero;
        agent.isStopped = true;

        Animator animator = butcher.GetComponentInChildren<Animator>();
        if (animator != null && animator.HasState(0, Animator.StringToHash(locomotionState)))
            animator.Play(locomotionState, 0, 0f);
        butcher.poiLookDirector?.LockLookToPlayer();
        return true;
    }

    // Lets the held butcher go on a hunt that only ends in a capture.
    void ReleaseIntoHunt()
    {
        NPCController butcher = _held;
        _held = null;
        if (butcher == null)
            return;
        butcher.SetHearingDisabled(_heldHearingDisabled);
        // The hunt refuses a Scripted butcher; this only flips the state, it starts no routine.
        butcher.SetState(NPCController.AIState.Natural);
        butcher.ForceRelentlessHunt(CaptureReason.Spotted);
        if (butcher.currentState != NPCController.AIState.Chase)
        {
            Debug.LogWarning($"ButcherWindowShot: {butcher.name} didn't start the hunt (state {butcher.currentState}).", this);
            butcher.poiLookDirector?.UnlockLookFromPlayer();
            butcher.EnsureNaturalRoutineStarted();
            return;
        }
        _hunter = butcher;
    }

    // Capture can drop back into the chase (he breaks off to drag them from a hiding spot), so it still counts.
    bool Hunting() =>
        _hunter != null && !PlayerControlGate.IsAcquired(PlayerControlGate.ReasonCaptured)
        && (_hunter.currentState == NPCController.AIState.Chase || _hunter.currentState == NPCController.AIState.Capture);

    void StartChaseMusic()
    {
        if (chaseMusic == null || SoundManager.Instance == null || !Hunting())
            return;
        _chaseMusicOn = true;
        SoundManager.Instance.SetChaseMusicActive(true, chaseMusic);
    }

    void EndChaseMusic()
    {
        _hunter = null;
        if (!_chaseMusicOn)
            return;
        _chaseMusicOn = false;
        if (SoundManager.Instance != null)
            SoundManager.Instance.SetChaseMusicActive(false);
    }

    void ShowRunPrompt()
    {
        if (!Hunting())
            return;
        _runPromptOn = true;
        RunPromptUI.Show();
    }

    void HideRunPrompt()
    {
        if (!_runPromptOn)
            return;
        _runPromptOn = false;
        RunPromptUI.Hide();
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

    void ShowCarSubtitle()
    {
        if (HudManager.Instance != null && !string.IsNullOrEmpty(carSubtitleKey))
            HudManager.Instance.ShowSubtitle(Loc.Get(carSubtitleKey), carSubtitleSeconds);
    }

    // First leg of his way from the window to the player, so the turn already points where the hunt will take him.
    Vector3 RunDirection(NPCController hunter, Vector3 from, Vector3 fallback)
    {
        fallback = Flat(fallback).normalized;
        if (hunter == null || _player == null)
            return fallback;
        NavMeshAgent agent = hunter.GetComponent<NavMeshAgent>();
        int mask = agent != null ? agent.areaMask : NavMesh.AllAreas;
        if (!NavMesh.SamplePosition(from, out NavMeshHit start, standNavRadius, mask)
            || !NavMesh.SamplePosition(_player.transform.position, out NavMeshHit end, 4f, mask))
            return fallback;
        var path = new NavMeshPath();
        if (!NavMesh.CalculatePath(start.position, end.position, mask, path) || path.corners.Length < 2)
            return fallback;
        for (int i = 1; i < path.corners.Length; i++)
        {
            Vector3 leg = Flat(path.corners[i] - start.position);
            if (leg.sqrMagnitude > 0.25f)
                return leg.normalized;
        }
        return fallback;
    }

    void SpawnBacklight(Vector3 position)
    {
        var go = new GameObject("Butcher Window Backlight");
        go.transform.position = position;
        _backlight = go.AddComponent<Light>();
        _backlight.type = LightType.Point;
        _backlight.color = backlightColor;
        _backlight.range = backlightRange;
        _backlight.shadows = LightShadows.Soft;
        _backlight.intensity = backlightIntensity;
    }

    void SpawnCarLight(Vector3 position)
    {
        var go = new GameObject("Butcher Window Car Light");
        go.transform.position = position;
        _carLight = go.AddComponent<Light>();
        _carLight.type = LightType.Point;
        _carLight.color = carLightColor;
        _carLight.range = carLightRange;
        _carLight.shadows = LightShadows.Soft;
        SetCarLight(0f);
    }

    void SetCarLight(float level)
    {
        if (_carLight == null)
            return;
        level = Mathf.Clamp01(level);
        _carLight.intensity = carLightIntensity * level;
        _carLight.enabled = level > 0f;
    }

    void FlickerBacklight(float level)
    {
        if (_backlight == null)
            return;
        float noise = Mathf.PerlinNoise(Time.time * 11f, 0.37f);
        _backlight.intensity = backlightIntensity * Mathf.Clamp01(level) * (1f - flicker * noise);
        _backlight.enabled = level > 0f;
    }

    // The escape point sits inside the car: climb to the biggest parent that is still car-sized.
    static Transform FallbackCar()
    {
        CarEscapePoint escape = FindFirstObjectByType<CarEscapePoint>();
        if (escape == null)
            return null;
        Transform car = escape.transform;
        while (car.parent != null && BoundsOf(car.parent).size.magnitude < 15f)
            car = car.parent;
        return car;
    }

    void Pose(Vector3 position, Quaternion rotation, float fov)
    {
        _cam.transform.SetPositionAndRotation(position, rotation);
        LensSettings lens = _cam.Lens;
        lens.FieldOfView = fov;
        _cam.Lens = lens;
    }

    static Bounds BoundsOf(Transform target)
    {
        Renderer[] renderers = target.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0)
            return new Bounds(target.position, Vector3.one);
        Bounds bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++)
            bounds.Encapsulate(renderers[i].bounds);
        return bounds;
    }

    // Free distance along the move before anything that isn't the target itself.
    static float Clearance(Vector3 from, Vector3 dir, float distance, Transform target)
    {
        float free = distance;
        foreach (RaycastHit hit in Physics.SphereCastAll(from, 0.25f, dir, distance, ~0, QueryTriggerInteraction.Ignore))
        {
            if (hit.distance <= 0f || (target != null && hit.transform.IsChildOf(target)) || hit.collider.GetComponentInParent<CharacterController>() != null)
                continue;
            free = Mathf.Min(free, hit.distance - 0.4f);
        }
        return free;
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
        if (window == null)
            return;
        Vector3 outward = Flat(window.forward).normalized;
        Vector3 feet = window.position + window.rotation * standOffset;
        Gizmos.color = Color.red;
        Gizmos.DrawWireCube(feet + Vector3.up * 0.95f, new Vector3(0.6f, 1.9f, 0.4f));
        Gizmos.DrawLine(window.position, window.position + outward);
        Gizmos.color = backlightColor;
        Gizmos.DrawWireSphere(feet - outward * backlightOffset.x + Vector3.up * backlightOffset.y, 0.2f);
    }
}
