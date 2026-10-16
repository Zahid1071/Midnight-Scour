using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Plays once, the first time the player is fully out of the basement, past its exit door. From their own view it closes
/// in on the car the way the hide shot does, a light coming up over it, then whips to a window of the house where the
/// butcher stands lit from behind, staring out.
/// He turns and runs back into the house, and control returns once the view has blended back.
/// Installs itself in any scene with Outdoor Fog; left empty, the car, window and butcher are found when it plays.
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
    [Tooltip("Metres the player must be past the basement exit door before the shot can start.")]
    [SerializeField] float exitClearance = 2f;
    [Tooltip("Seconds the player must be out, free to move and not chased, before the shot starts.")]
    [SerializeField] float settleTime = 0.6f;
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

    [Header("Car Light")]
    [Tooltip("Comes up over the car as the view closes in so it reads in the dark, and dies in the whip.")]
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
    [Tooltip("The butcher to copy. A lifeless double is spawned for the shot: only its looks and animator are kept. " +
             "Left empty, the butcher in the scene is copied.")]
    [SerializeField] GameObject butcherPrefab;
    [Tooltip("Where his feet go relative to the window marker: x right, y up, z out of the house.")]
    [SerializeField] Vector3 standOffset = new Vector3(0f, -1.6f, -0.8f);
    [Tooltip("How far inside a picked window he stands.")]
    [SerializeField] float standDepth = 0.9f;
    [Tooltip("Metres of height the window shot takes in; smaller is tighter.")]
    [SerializeField] float windowFrameHeight = 2.4f;
    [Tooltip("Seconds of the whip from the car to the window.")]
    [SerializeField] float whipTime = 0.16f;
    [Tooltip("Seconds he stands staring while the lens creeps in.")]
    [SerializeField] float stareTime = 1.6f;
    [Tooltip("Field of view at the end of the stare, as a share of where it started.")]
    [SerializeField, Range(0.5f, 1f)] float stareCreep = 0.85f;
    [Tooltip("Outdoor fog density while on the window, as a share of normal, so he reads from across the yard.")]
    [SerializeField, Range(0f, 1f)] float windowFog = 0.25f;
    [Tooltip("Played as the view whips to the window.")]
    [SerializeField] AudioClip sting;
    [SerializeField, Range(0f, 1f)] float stingVolume = 1f;

    [Header("Run")]
    [SerializeField] string locomotionState = "Base Blend";
    [SerializeField] string speedParameter = "Speed";
    [Tooltip("Speed value of the run in the locomotion blend tree.")]
    [SerializeField] float runBlendValue = 3f;
    [SerializeField] float turnTime = 0.25f;
    [SerializeField] float runSpeed = 5f;
    [SerializeField] float runTime = 0.9f;
    [Tooltip("Seconds on the empty window, the light dying, before the view goes back.")]
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

    CinemachineCamera _cam;
    NPCController[] _npcs;
    bool _played;
    bool _locked;
    float _settled;
    GameObject _double;
    Light _backlight;
    Light _carLight;
    bool _hasExit;
    Vector3 _exitPoint, _exitOut;
    readonly List<Renderer> _hiddenViewmodel = new List<Renderer>();

    CinemachineMouseLook _player;
    readonly List<Transform> _windowWalls = new List<Transform>();
    Aim _aim;
    bool _aimed;
    float _aimCheck;

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

    void OnDisable() => Stop();

    void Update()
    {
        if (_played)
            return;
        if (_player == null)
            _player = FindFirstObjectByType<CinemachineMouseLook>();
        // The player's own eye, not the main camera, which rides intro and other shots.
        bool ready = _player != null && OutOfBasement(_player.transform.position) && !PlayerControlGate.IsInputBlocked && !Chased();
        if (!ready)
        {
            _settled = 0f;
            _aimed = false;
            return;
        }
        // Waits until a window is actually in sight: the stairs out of the basement are walled in.
        _aimCheck -= Time.deltaTime;
        if (_aimCheck <= 0f)
        {
            _aimCheck = 0.2f;
            // The butcher is spawned in after the scene loads.
            _npcs = FindObjectsByType<NPCController>(FindObjectsSortMode.None);
            _aimed = TryAim(_player.transform.position, out _aim);
        }
        _settled = _aimed ? _settled + Time.deltaTime : 0f;
        if (_settled >= settleTime)
            StartCoroutine(Play(_aim));
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

    IEnumerator Play(Aim aim)
    {
        _played = true;
        Camera main = Camera.main;
        if (main == null)
            yield break;
        Vector3 eye = main.transform.position;
        Vector3 windowLook = aim.look, outward = aim.outward, feet = aim.feet;

        GameObject template = butcherPrefab;
        if (template == null)
            foreach (NPCController npc in _npcs)
                if (npc != null)
                {
                    template = npc.gameObject;
                    break;
                }
        if (template == null)
        {
            NPCController sleeping = FindFirstObjectByType<NPCController>(FindObjectsInactive.Include);
            if (sleeping != null)
                template = sleeping.gameObject;
        }
        if (template == null)
        {
            Debug.LogWarning("ButcherWindowShot: no butcher to copy.", this);
            yield break;
        }
        Debug.Log($"ButcherWindowShot: playing at {aim.name} ({Vector3.Distance(eye, windowLook):F0} m).", this);
        Begin(main);

        Vector3 pos = eye;
        Quaternion rot = main.transform.rotation;
        float fov = main.fieldOfView;
        Pose(pos, rot, fov);
        _cam.Priority = priority;

        Animator animator = SpawnDouble(template, feet, Quaternion.LookRotation(Flat(eye - feet)));
        SpawnBacklight(feet - outward * backlightOffset.x + Vector3.up * backlightOffset.y);
        int speedId = Animator.StringToHash(speedParameter);

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
            for (float t = 0f; t < carMoveTime; t += Time.deltaTime)
            {
                float k = t / carMoveTime;
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
            if (carHold > 0f)
                yield return new WaitForSeconds(carHold);
        }

        // The whip: a snap to the window, tight on him.
        float windowDistance = Mathf.Max(0.5f, Vector3.Distance(pos, windowLook));
        float windowFov = Mathf.Clamp(2f * Mathf.Atan(windowFrameHeight * 0.5f / windowDistance) * Mathf.Rad2Deg, 3f, 60f);
        if (sting != null)
            AudioSource.PlayClipAtPoint(sting, main.transform.position, stingVolume);
        {
            Quaternion fromRot = rot;
            Quaternion toRot = Quaternion.LookRotation(windowLook - pos);
            float fromFov = fov;
            for (float t = 0f; t < whipTime; t += Time.deltaTime)
            {
                float u = 1f - Mathf.Pow(1f - Mathf.Clamp01(t / whipTime), 3f);
                Pose(pos, Quaternion.Slerp(fromRot, toRot, u), Mathf.Lerp(fromFov, windowFov, u));
                OutdoorFog.DensityScale = Mathf.Lerp(1f, windowFog, u);
                FlickerBacklight(1f);
                SetCarLight(1f - u);
                yield return null;
            }
            rot = toRot;
            fov = windowFov;
            OutdoorFog.DensityScale = windowFog;
            SetCarLight(0f);
        }

        // The stare: he stands there while the lens creeps in.
        for (float t = 0f; t < stareTime; t += Time.deltaTime)
        {
            Pose(pos, rot, Mathf.Lerp(windowFov, windowFov * stareCreep, SmootherStep(t / stareTime)));
            FlickerBacklight(1f);
            yield return null;
        }
        fov = windowFov * stareCreep;

        // The run: he wheels round and bolts back into the house.
        if (animator != null)
        {
            Transform body = _double.transform;
            Quaternion facing = body.rotation;
            Quaternion away = Quaternion.LookRotation(-outward);
            float total = turnTime + runTime;
            for (float t = 0f; t < total; t += Time.deltaTime)
            {
                float turn = SmootherStep(t / Mathf.Max(0.01f, turnTime));
                body.rotation = Quaternion.Slerp(facing, away, turn);
                body.position += -outward * (runSpeed * turn * Time.deltaTime);
                animator.SetFloat(speedId, runBlendValue, 0.08f, Time.deltaTime);
                FlickerBacklight(1f);
                yield return null;
            }
            _double.SetActive(false);
        }

        // The empty window, the light dying.
        for (float t = 0f; t < emptyHold; t += Time.deltaTime)
        {
            FlickerBacklight(1f - t / emptyHold);
            yield return null;
        }
        FlickerBacklight(0f);

        // The player is left facing the window as the view blends back.
        CinemachineMouseLook mouseLook = FindFirstObjectByType<CinemachineMouseLook>();
        if (mouseLook != null)
            mouseLook.SetLook(Quaternion.LookRotation(Flat(windowLook - eye)));
        _cam.Priority = 0;
        for (float t = 0f; t < blendOut; t += Time.deltaTime)
        {
            OutdoorFog.DensityScale = Mathf.Lerp(windowFog, 1f, SmootherStep(t / blendOut));
            yield return null;
        }
        Stop();
    }

    /// <summary>Ends the shot at once and gives control back.</summary>
    public void Stop()
    {
        if (_cam != null)
            _cam.Priority = 0;
        OutdoorFog.DensityScale = 1f;
        if (_double != null)
            Destroy(_double);
        if (_backlight != null)
            Destroy(_backlight.gameObject);
        if (_carLight != null)
            Destroy(_carLight.gameObject);
        foreach (Renderer r in _hiddenViewmodel)
            if (r != null)
                r.enabled = true;
        _hiddenViewmodel.Clear();
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

    // The marker window if there is one, else the window wall in clear sight facing the player most squarely, close
    // and on the floor nearest their eye.
    bool TryAim(Vector3 eye, out Aim aim)
    {
        aim = default;
        if (window != null)
        {
            aim.look = window.position;
            aim.outward = Flat(window.forward).normalized;
            aim.feet = window.position + window.rotation * standOffset;
            aim.name = window.name;
            float d = Vector3.Distance(eye, aim.look);
            return d <= maxWindowDistance && InSight(eye, aim.look, window);
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
            if (facing < 0.35f || !InSight(eye, b.center, wall))
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

    Animator SpawnDouble(GameObject template, Vector3 position, Quaternion rotation)
    {
        // Spawned under an inactive holder so none of the butcher's AI wakes up before it is stripped away.
        var holder = new GameObject("Butcher Window Double");
        holder.SetActive(false);
        _double = Instantiate(template, position, rotation, holder.transform);
        StripToLooks(_double);
        _double.transform.SetParent(null, true);
        Destroy(holder);
        _double.SetActive(true);

        Animator animator = _double.GetComponentInChildren<Animator>();
        if (animator == null)
            return null;
        animator.applyRootMotion = false;
        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        animator.SetFloat(speedParameter, 0f);
        if (animator.HasState(0, Animator.StringToHash(locomotionState)))
            animator.Play(locomotionState, 0, 0f);
        return animator;
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

    // Leaves only what it looks like and how it moves; a component another still needs goes after that one.
    static void StripToLooks(GameObject root)
    {
        var doomed = new List<Component>();
        foreach (Component c in root.GetComponentsInChildren<Component>(true))
            if (c != null && !(c is Transform || c is Animator || c is Renderer || c is MeshFilter || c is LODGroup || c is Light))
                doomed.Add(c);
        for (int pass = 0; pass < 8 && doomed.Count > 0; pass++)
            for (int i = doomed.Count - 1; i >= 0; i--)
                if (!IsRequired(doomed[i], doomed))
                {
                    DestroyImmediate(doomed[i]);
                    doomed.RemoveAt(i);
                }
    }

    static bool IsRequired(Component component, List<Component> others)
    {
        Type type = component.GetType();
        foreach (Component other in others)
        {
            if (other == component || other.gameObject != component.gameObject)
                continue;
            foreach (RequireComponent req in Attribute.GetCustomAttributes(other.GetType(), typeof(RequireComponent), true))
                if (Needs(req.m_Type0, type) || Needs(req.m_Type1, type) || Needs(req.m_Type2, type))
                    return true;
        }
        return false;
    }

    static bool Needs(Type required, Type type) => required != null && required.IsAssignableFrom(type);

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
