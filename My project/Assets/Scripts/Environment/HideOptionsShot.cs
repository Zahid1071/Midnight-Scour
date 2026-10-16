using System.Collections;
using System.Collections.Generic;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.Serialization;

/// <summary>
/// A short shot that points something out (where to hide, a stuck wrench, a key under a grate). From the player's own
/// view it takes each target in turn in a single move where the camera travels and the lens zooms at once: pulling back
/// while the lens closes in squeezes the room up behind the target; pushing in while the lens opens out stretches it
/// away. It can also circle the target as if on a round track. A subtitle runs over it; control returns once the view
/// has blended back.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(CinemachineCamera))]
public class HideOptionsShot : MonoBehaviour
{
    const string LockReason = "hide_options_shot";

    [Tooltip("Shown in this order.")]
    [SerializeField] Transform[] targets;
    [SerializeField] string subtitleKey = "gameplay.gotta_hide";
    [SerializeField] float subtitleSeconds = 3f;
    [SerializeField] int priority = 100;
    [Tooltip("How much of the frame a target fills at the end of its move.")]
    [SerializeField, Range(0.1f, 1f)] float frameFill = 0.55f;

    [Header("Move")]
    [Tooltip("Metres the camera pulls back from each target while the lens closes in (less if something is behind it). " +
             "Negative pushes in toward it instead.")]
    [SerializeField] float pull = 0.6f;
    [FormerlySerializedAs("vertigoTime")]
    [SerializeField] float moveTime = 1.8f;
    [Tooltip("Share of the move (0-1) by which the view has turned onto the target.")]
    [FormerlySerializedAs("vertigoTurn")]
    [SerializeField, Range(0.05f, 1f)] float turnShare = 0.35f;
    [Tooltip("Share of the move (0-1) over which the lens brings the target up to its frame fill; for the rest it only " +
             "holds its size while the camera keeps moving (narrowing as it pulls back, widening as it pushes in).")]
    [SerializeField, Range(0.05f, 1f)] float zoomShare = 1f;
    [Tooltip("Optional: degrees the camera circles each target over its move and hold, at its own height and distance, " +
             "as if on a round track; the sign sets the direction. 0 for none.")]
    [SerializeField] float orbitDegrees;
    [SerializeField] float hold = 0.25f;

    [Header("Ending")]
    [Tooltip("Optional: partway through the last target's move the view slides off it onto this point.")]
    [SerializeField] Transform refocus;
    [Tooltip("Point in the last move (0-1) where the view starts sliding onto the refocus point.")]
    [SerializeField, Range(0f, 1f)] float refocusAt = 0.5f;
    [Tooltip("On: the player is left facing the last look point on the level. Off: they keep looking right at it.")]
    [SerializeField] bool levelLookAfter = true;
    [Tooltip("Seconds of the blend back to the player view (the brain's custom blend); control returns after it.")]
    [SerializeField] float blendOut = 0.8f;
    [Tooltip("Lights each target in turn so it reads in the dark; off outside the shot.")]
    [SerializeField] Light focusLight;
    [Tooltip("Where the light sits: metres out from the target toward the camera (x) and up (y).")]
    [SerializeField] Vector2 focusLightOffset = new Vector2(1.2f, 0.6f);

    CinemachineCamera _cam;
    CinemachineBasicMultiChannelPerlin _noise;
    float _noiseAmplitude;
    float _startFov;
    bool _locked;
    float _focusIntensity;
    readonly List<Renderer> _hiddenViewmodel = new List<Renderer>();

    void Awake()
    {
        _cam = GetComponent<CinemachineCamera>();
        _cam.Priority = 0;
        _noise = GetComponent<CinemachineBasicMultiChannelPerlin>();
        if (_noise != null)
            _noiseAmplitude = _noise.AmplitudeGain;
        if (focusLight != null)
        {
            _focusIntensity = focusLight.intensity;
            // It's placed at each target in world space, so it mustn't ride along with the camera.
            if (focusLight.transform.IsChildOf(transform))
                focusLight.transform.SetParent(transform.parent, true);
            focusLight.enabled = false;
        }
    }

    void OnDisable() => Stop();

    public IEnumerator Play()
    {
        Camera main = Camera.main;
        if (main == null)
            yield break;
        Begin(main);
        if (HudManager.Instance != null && !string.IsNullOrEmpty(subtitleKey))
            HudManager.Instance.ShowSubtitle(Loc.Get(subtitleKey), subtitleSeconds);

        Vector3 eye = main.transform.position;
        Vector3 pos = eye;
        Quaternion rot = main.transform.rotation;
        float fov = main.fieldOfView;
        _startFov = fov;
        Pose(pos, rot, fov);
        _cam.Priority = priority;

        Vector3 lastLook = pos + rot * Vector3.forward;
        bool lit = false;
        bool orbiting = !Mathf.Approximately(orbitDegrees, 0f);
        int last = targets.Length - 1;
        while (last >= 0 && targets[last] == null)
            last--;
        for (int i = 0; i <= last; i++)
        {
            Transform target = targets[i];
            if (target == null)
                continue;
            Bounds bounds = BoundsOf(target);
            Vector3 look = bounds.center;
            Vector3 lightAt = look + Flat(pos - look).normalized * focusLightOffset.x + Vector3.up * focusLightOffset.y;
            float size = bounds.extents.magnitude;
            float distance = Mathf.Max(0.5f, Vector3.Distance(pos, look));
            Vector3 focus = i == last && refocus != null ? refocus.position : look;

            // One move: turn onto it (landing early), move and zoom at once, the light leaving the last target and
            // coming up on this one; on the last target the view may slide onto the refocus point. An orbit carries on
            // through the hold so the shot never sits still.
            // A push stops where holding the frame fill would need a lens wider than the player's.
            Vector3 dir = (look - pos).normalized;
            float tanStart = Mathf.Tan(_startFov * 0.5f * Mathf.Deg2Rad);
            float travel = pull >= 0f
                ? -Mathf.Clamp(Clearance(pos, -dir, pull + 0.4f, null), 0f, pull)
                : Mathf.Clamp(Mathf.Min(-pull, distance - size / (frameFill * tanStart), Clearance(pos, dir, distance, target)), 0f, -pull);
            Vector3 from = pos;
            Vector3 to = pos + dir * travel;
            Quaternion fromRot = rot;
            float fromFill = size / (distance * Mathf.Tan(fov * 0.5f * Mathf.Deg2Rad));
            float total = moveTime + (orbiting ? hold : 0f);
            for (float t = 0f; t < total; t += Time.deltaTime)
            {
                float k = Mathf.Clamp01(t / moveTime);
                float u = SmootherStep(k);
                pos = Orbit(Vector3.Lerp(from, to, u), look, orbitDegrees * Mathf.SmoothStep(0f, 1f, t / total));
                Vector3 aim = Vector3.Lerp(look, focus, SmootherStep(Mathf.InverseLerp(refocusAt, 1f, k)));
                rot = Quaternion.Slerp(fromRot, Quaternion.LookRotation(aim - pos), SmootherStep(k / turnShare));
                float fill = Mathf.Lerp(fromFill, frameFill, SmootherStep(k / zoomShare));
                fov = Mathf.Clamp(2f * Mathf.Atan(size / (fill * Vector3.Distance(pos, look))) * Mathf.Rad2Deg, 3f, _startFov);
                Pose(pos, rot, fov);
                FadeFocusLight(lightAt, lit, k / turnShare);
                yield return null;
            }
            pos = Orbit(to, look, orbitDegrees);
            rot = Quaternion.LookRotation(focus - pos);
            Pose(pos, rot, fov);
            SetFocusLight(lightAt, 1f);
            lit = true;
            lastLook = focus;
            if (hold > 0f && !orbiting)
                yield return new WaitForSeconds(hold);
        }

        // The player is left facing where the shot last looked as the view blends back.
        CinemachineMouseLook mouseLook = FindFirstObjectByType<CinemachineMouseLook>();
        if (mouseLook != null)
            mouseLook.SetLook(Quaternion.LookRotation(levelLookAfter ? Flat(lastLook - eye) : lastLook - eye));
        _cam.Priority = 0;
        for (float t = 0f; t < blendOut; t += Time.deltaTime)
        {
            SetFocusLight(null, 1f - t / blendOut);
            yield return null;
        }
        Stop();
    }

    // Over k (0-1): the light leaves the last target in the first half and comes up here in the second, or just comes
    // up here when nothing was lit yet.
    void FadeFocusLight(Vector3 lightAt, bool fromLast, float k)
    {
        k = Mathf.Clamp01(k);
        if (!fromLast || k >= 0.5f)
            SetFocusLight(lightAt, fromLast ? (k - 0.5f) * 2f : k);
        else
            SetFocusLight(null, 1f - k * 2f);
    }

    void SetFocusLight(Vector3? position, float level)
    {
        if (focusLight == null)
            return;
        if (position.HasValue)
            focusLight.transform.position = position.Value;
        level = Mathf.Clamp01(level);
        focusLight.intensity = _focusIntensity * level;
        focusLight.enabled = level > 0f;
    }

    /// <summary>Ends the shot at once and gives control back.</summary>
    public void Stop()
    {
        if (_cam != null)
            _cam.Priority = 0;
        if (focusLight != null)
            focusLight.enabled = false;
        if (_noise != null)
            _noise.AmplitudeGain = _noiseAmplitude;
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

    void Pose(Vector3 position, Quaternion rotation, float fov)
    {
        _cam.transform.SetPositionAndRotation(position, rotation);
        LensSettings lens = _cam.Lens;
        lens.FieldOfView = fov;
        _cam.Lens = lens;
        // The noise is in degrees, so a narrow lens magnifies it on screen.
        if (_noise != null && _startFov > 0f)
            _noise.AmplitudeGain = _noiseAmplitude * Mathf.Min(1f, fov / _startFov);
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

    // Swings a point round the vertical axis through the pivot, keeping its height and its distance along the floor.
    static Vector3 Orbit(Vector3 point, Vector3 pivot, float degrees) =>
        pivot + Quaternion.AngleAxis(degrees, Vector3.up) * (point - pivot);

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
}
