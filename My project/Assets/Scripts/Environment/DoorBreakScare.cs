using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.Audio;

/// <summary>
/// While the player breaks a lock open with the screwdriver, something moves in the dark behind them. Each beat fires
/// on its own jam number (0 = never):
/// - Look back (first jam): something comes up behind them; a second later the player drops the lock, turns round on a
///   sting and the view pushes into the dark. Nothing's there. The view stays turned round; the lock keeps its progress.
/// - False alarm (third jam): a few dull knocks from further off, then nothing.
/// - Hook (off): a hook behind them swings and something snarls in the dark.
/// </summary>
[DisallowMultipleComponent]
public class DoorBreakScare : MonoBehaviour
{
    const string LockReason = "door_break_scare";

    [SerializeField] ScrewdriverLockBreakMinigame lockMinigame;
    [Tooltip("Nothing happens until this event has finished (lights out). Empty = no wait.")]
    [SerializeField] CorpseVanishEvent waitFor;
    [Tooltip("A point at eye height in the dark room behind the player; the steps come from here and the look-back aims at it.")]
    [SerializeField] Transform behindPoint;
    [SerializeField] AudioMixerGroup mixerGroup;

    [Header("Hook swing")]
    [Tooltip("Jam forced through that swings the hook. 0 = never.")]
    [SerializeField] int hookOnJam = 0;
    [SerializeField] HangingHook hook;
    [Tooltip("Speed (m/s) the hook is shoved at, across the room.")]
    [SerializeField] float hookKnockSpeed = 1.2f;
    [SerializeField] AudioClip firstClip;
    [SerializeField, Range(0f, 1f)] float firstVolume = 1f;
    [Tooltip("Full volume within this distance of the hook.")]
    [SerializeField] float firstMinDistance = 3f;
    [Tooltip("Seconds after the jam breaks, so the crack of the lock doesn't cover it.")]
    [SerializeField] float firstDelay = 0.9f;

    [Header("False alarm")]
    [Tooltip("Jam forced through that plays the knocks. 0 = never.")]
    [SerializeField] int falseAlarmOnJam = 3;
    [SerializeField] AudioClip secondClip;
    [SerializeField, Range(0f, 1f)] float secondVolume = 0.8f;
    [Tooltip("Where the knocks come from. Empty = the behind point.")]
    [SerializeField] Transform secondPoint;
    [SerializeField] float secondMinDistance = 3f;
    [SerializeField] float secondDelay = 1.6f;

    [Header("Approach and look back")]
    [Tooltip("Jam forced through that brings the steps and the look back. 0 = never.")]
    [SerializeField] int lookBackOnJam = 1;
    [Tooltip("When the escape look-back is armed, fire it on this jam breakthrough count (3 = third breakthrough).")]
    [SerializeField] int escapeLookBackOnJam = 3;
    [Tooltip("Legacy: also fire escape look-back on PenultimateJamBroken. Prefer Escape Look Back On Jam.")]
    [SerializeField] bool lookBackOnPenultimateWhenEscapeArmed = false;
    [Tooltip("Seconds after the jam breaks, so the crack of the lock doesn't cover the sound.")]
    [SerializeField] float approachDelay = 0.9f;
    [Tooltip("Comes up behind the player, moving from the start distance to the stop distance over its length.")]
    [SerializeField] AudioClip approachClip;
    [Tooltip("Used instead of Approach Clip while the bar-escape look back is armed (footsteps). Empty = Approach Clip.")]
    [SerializeField] AudioClip escapeApproachClip;
    [Tooltip("Metres below the behind point for the escape approach (1.4 ≈ footsteps).")]
    [SerializeField] float escapeApproachDropBelowEye = 1.4f;
    [Tooltip("Above 1 boosts the sound past the clip's own level.")]
    [SerializeField, Range(0f, 2f)] float approachVolume = 2f;
    [Tooltip("Full volume within this distance. Keep it under the start distance so it swells as it closes in.")]
    [SerializeField] float approachMinDistance = 2f;
    [Tooltip("The sound starts at most this far behind the player.")]
    [SerializeField] float approachStartDistance = 4.5f;
    [Tooltip("The sound stops this far behind the player.")]
    [SerializeField] float approachStopDistance = 1.8f;
    [Tooltip("Metres below the behind point (eye height) the sound travels at: about 0.2 for a voice, 1.4 for footsteps.")]
    [SerializeField] float approachDropBelowEye = 0.2f;
    [Tooltip("Seconds after the sound starts before the head turns.")]
    [SerializeField] float lookBackDelay = 1f;

    [Header("Look back")]
    [Tooltip("Dedicated camera; cuts in from the player's exact view (blend times come from the brain's custom blends).")]
    [SerializeField] CinemachineCamera lookCamera;
    [SerializeField] int lookPriority = 100;
    [SerializeField] float lookTurnTime = 0.45f;
    [Tooltip("Played flat (not from a point) as the head turns.")]
    [SerializeField] AudioClip turnSting;
    [SerializeField, Range(0f, 1f)] float turnStingVolume = 0.7f;
    [SerializeField] float lookZoomFov = 30f;
    [SerializeField] float lookZoomTime = 0.65f;
    [Tooltip("Metres the view drifts toward the dark while zooming.")]
    [SerializeField] float lookPush = 0.25f;
    [SerializeField] float lookHold = 1.2f;
    [Tooltip("Seconds of the blend back to the player view; control returns after it.")]
    [SerializeField] float lookBlendOut = 0.8f;

    [Header("Escape look back (bar taken)")]
    [Tooltip("Places him at the cage doorframe before the look-back. Empty = find in scene.")]
    [SerializeField] WallStanderChase wallStanderChase;
    [Tooltip("Look at this instead of Behind Point during the escape look back (wall stander). Empty = Wall Stander.")]
    [SerializeField] Transform escapeLookTarget;
    [Tooltip("Eye-height offset on Escape Look Target.")]
    [SerializeField] float escapeLookHeight = 1.4f;
    [Tooltip("Flat sting when the zoom finishes and he is fully framed (escape look only). Empty = Turn Sting.")]
    [SerializeField] AudioClip escapeRevealSting;
    [SerializeField, Range(0f, 1f)] float escapeRevealStingVolume = 0.85f;
    [SerializeField] LightSwitch[] escapeFlickerSwitches;
    [SerializeField] GameObject[] escapeFlickerLamps;
    [Tooltip("How many on/off flashes when he is fully framed (escape look).")]
    [SerializeField] int escapeRevealFlashes = 4;
    [SerializeField] float escapeRevealFlashOn = 0.1f;
    [SerializeField] float escapeRevealFlashOff = 0.08f;

    [Header("Escape closing-in hooks")]
    [Tooltip("Wooden creak right before the first hook rattle (escape look-back cue). Empty = skip.")]
    [SerializeField] AudioClip escapePreHookCreak;
    [SerializeField, Range(0f, 2f)] float escapePreHookCreakVolume = 1.35f;
    [Tooltip("Full volume within this distance of the first hook / behind point.")]
    [SerializeField] float escapePreHookCreakMinDistance = 3.5f;
    [Tooltip("Beat after the creak before the first hook starts rattling.")]
    [SerializeField] float escapePreHookCreakLead = 0.2f;
    [Tooltip("Knocked before the escape look-back: furthest (near him) then the next toward the player. Empty = auto-pick.")]
    [SerializeField] HangingHook[] escapeClosingHooks;
    [SerializeField] float escapeHookKnockSpeed = 2.2f;
    [Tooltip("Multiplies each hook's chain-loop max volume during the closing-in pass (escape look-back cue).")]
    [SerializeField, Range(1f, 3f)] float escapeHookChainVolumeScale = 2.35f;
    [Tooltip("How long each hook rattles during the closing-in pass.")]
    [SerializeField] float escapeHookPassDuration = 1.15f;
    [Tooltip("Seconds after the jam before the first (furthest) hook is hit.")]
    [SerializeField] float escapeHookFirstDelay = 0.35f;
    [Tooltip("Seconds between the far hook and the next one closing in.")]
    [SerializeField] float escapeHookGap = 0.55f;
    [Tooltip("Seconds after the second hook before the head turns.")]
    [SerializeField] float escapeHookToLookDelay = 0.4f;

    /// <summary>The steps start coming.</summary>
    public event Action ApproachStarted;
    /// <summary>The look back is over and the player has control again.</summary>
    public event Action LookBackFinished;

    bool _hookDone;
    bool _falseAlarmDone;
    bool _lookDone;
    bool _locked;
    bool _hudHidden;
    bool _escapeArmed;
    bool _escapeLook;
    readonly List<Renderer> _hiddenViewmodel = new List<Renderer>();
    Coroutine _escapeLampFlicker;
    Light[] _cachedEscapeLights;
    bool _escapeLightsCached;

    /// <summary>Bar-escape stuck door is up — 2nd jam breakthrough runs the look back at him.</summary>
    public void ArmEscapeLookBack()
    {
        _escapeArmed = true;
        _lookDone = false;
        _escapeLook = false;
        EnsureEscapeLightsCached();
        BindDeferFinalBreak();
    }

    public bool EscapeLookBackDone => _escapeArmed && _lookDone;

    void Awake()
    {
        if (lookCamera != null)
            lookCamera.Priority = 0;
        SoundManager.EnsureClipReady(turnSting);
        SoundManager.EnsureClipReady(escapeRevealSting);
    }

    void OnEnable()
    {
        if (lockMinigame != null)
        {
            lockMinigame.JamBroken += OnJamBroken;
            lockMinigame.PenultimateJamBroken += OnPenultimateJamBroken;
        }
        BindDeferFinalBreak();
    }

    void OnDisable()
    {
        if (lockMinigame != null)
        {
            lockMinigame.JamBroken -= OnJamBroken;
            lockMinigame.PenultimateJamBroken -= OnPenultimateJamBroken;
            if (lockMinigame.ShouldDeferFinalBreak == DeferFinalBreakForEscapeLook)
                lockMinigame.ShouldDeferFinalBreak = null;
        }
        StopEscapeLampFlicker();
        EndCinematic();
    }

    void BindDeferFinalBreak()
    {
        if (lockMinigame != null)
            lockMinigame.ShouldDeferFinalBreak = DeferFinalBreakForEscapeLook;
    }

    bool DeferFinalBreakForEscapeLook() => _escapeArmed && !_lookDone;

    bool Armed => (waitFor == null || waitFor.LeftCageRoomFirstPass)
                  && !(SoundManager.Instance != null && SoundManager.Instance.IsChaseMusicActive);

    void OnPenultimateJamBroken()
    {
        if (!_escapeArmed || !lookBackOnPenultimateWhenEscapeArmed || _lookDone)
            return;
        StartEscapeLookBack();
    }

    void OnJamBroken(int count)
    {
        // Escape: 3rd breakthrough → look-back (do not use the first-pass Armed gate).
        if (_escapeArmed && !_lookDone && escapeLookBackOnJam > 0 && count >= escapeLookBackOnJam)
        {
            if (SoundManager.Instance == null || !SoundManager.Instance.IsChaseMusicActive)
            {
                StartEscapeLookBack();
                return;
            }
        }

        if (!Armed)
            return;

        if (_escapeArmed)
        {
            if (!_hookDone && Reached(hookOnJam, count))
            {
                _hookDone = true;
                StartCoroutine(FirstRoutine());
            }
            else if (!_falseAlarmDone && Reached(falseAlarmOnJam, count))
            {
                _falseAlarmDone = true;
                StartCoroutine(SecondRoutine());
            }
            return;
        }

        if (!_lookDone && Reached(lookBackOnJam, count))
        {
            _lookDone = true;
            _escapeLook = false;
            StartCoroutine(LookBackRoutine());
        }
        else if (!_hookDone && Reached(hookOnJam, count))
        {
            _hookDone = true;
            StartCoroutine(FirstRoutine());
        }
        else if (!_falseAlarmDone && Reached(falseAlarmOnJam, count))
        {
            _falseAlarmDone = true;
            StartCoroutine(SecondRoutine());
        }
    }

    void StartEscapeLookBack()
    {
        if (_lookDone)
            return;
        _lookDone = true;
        _escapeLook = true;
        StartCoroutine(EscapeLookBackSequence());
    }

    /// <summary>Hooks close in (far then nearer), then the look-back — reaction, not out of nowhere.</summary>
    IEnumerator EscapeLookBackSequence()
    {
        ApproachStarted?.Invoke();
        PlaceWallStanderForEscapeReveal();
        yield return EscapeHookClosingIn();
        yield return LookBackRoutine(skipApproachPrefix: true);
    }

    IEnumerator EscapeHookClosingIn()
    {
        HangingHook[] hooks = ResolveEscapeClosingHooks();
        if (hooks == null || hooks.Length == 0)
            yield break;

        if (escapeHookFirstDelay > 0f)
            yield return new WaitForSeconds(escapeHookFirstDelay);

        if (escapePreHookCreak != null)
        {
            Vector3 creakAt = hooks[0] != null ? hooks[0].transform.position
                : behindPoint != null ? behindPoint.position : transform.position;
            PlayAt(escapePreHookCreak, creakAt, escapePreHookCreakVolume, escapePreHookCreakMinDistance);
            if (escapePreHookCreakLead > 0f)
                yield return new WaitForSeconds(escapePreHookCreakLead);
        }

        Camera cam = Camera.main;
        for (int i = 0; i < hooks.Length; i++)
        {
            HangingHook h = hooks[i];
            if (h == null)
                continue;
            Vector3 across = Vector3.right;
            if (cam != null)
            {
                across = h.transform.position - cam.transform.position;
                across.y = 0f;
                if (across.sqrMagnitude > 1e-4f)
                    across = Vector3.Cross(Vector3.up, across.normalized);
                else
                    across = Vector3.forward;
            }
            // Same chain loop the player hears when walking through — drive velocity + loop volume.
            yield return h.KnockPassRoutine(across, escapeHookKnockSpeed, escapeHookPassDuration,
                escapeHookChainVolumeScale);
            if (i < hooks.Length - 1 && escapeHookGap > 0f)
                yield return new WaitForSeconds(escapeHookGap);
        }

        if (escapeHookToLookDelay > 0f)
            yield return new WaitForSeconds(escapeHookToLookDelay);
    }

    HangingHook[] ResolveEscapeClosingHooks()
    {
        if (escapeClosingHooks != null && escapeClosingHooks.Length > 0)
            return escapeClosingHooks;

        Transform from = wallStanderChase != null ? wallStanderChase.transform
            : escapeLookTarget != null ? escapeLookTarget : null;
        if (from == null)
            return null;

        HangingHook[] all = FindObjectsByType<HangingHook>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        if (all == null || all.Length == 0)
            return null;

        System.Array.Sort(all, (a, b) =>
        {
            float da = Vector3.Distance(a.transform.position, from.position);
            float db = Vector3.Distance(b.transform.position, from.position);
            return da.CompareTo(db);
        });

        // Closest to him (furthest down the corridor from the escape door), then the next toward the player.
        int n = Mathf.Min(2, all.Length);
        var picked = new HangingHook[n];
        for (int i = 0; i < n; i++)
            picked[i] = all[i];
        return picked;
    }

    static bool Reached(int jam, int count) => jam > 0 && count >= jam;

    IEnumerator FirstRoutine()
    {
        yield return new WaitForSeconds(firstDelay);
        Camera cam = Camera.main;
        if (hook != null && cam != null)
        {
            Vector3 across = hook.transform.position - cam.transform.position;
            across.y = 0f;
            across = Vector3.Cross(Vector3.up, across.normalized);
            hook.Knock(across * hookKnockSpeed);
        }
        Vector3 at = hook != null ? hook.transform.position : behindPoint != null ? behindPoint.position : transform.position;
        PlayAt(firstClip, at, firstVolume, firstMinDistance);
    }

    IEnumerator SecondRoutine()
    {
        yield return new WaitForSeconds(secondDelay);
        Transform from = secondPoint != null ? secondPoint : behindPoint;
        PlayAt(secondClip, from != null ? from.position : transform.position, secondVolume, secondMinDistance);
    }

    IEnumerator LookBackRoutine(bool skipApproachPrefix = false)
    {
        if (!skipApproachPrefix)
        {
            ApproachStarted?.Invoke();
            yield return new WaitForSeconds(approachDelay);
        }

        Camera cam = Camera.main;
        Transform soundFrom = behindPoint;
        if (cam == null || soundFrom == null)
        {
            if (_escapeLook)
                PlaceWallStanderForEscapeReveal();
            LookBackFinished?.Invoke();
            yield break;
        }

        if (_escapeLook)
            PlaceWallStanderForEscapeReveal();

        if (!skipApproachPrefix && !_escapeLook)
        {
            // Something rushes from the dark toward the player's back.
            Vector3 listener = cam.transform.position;
            Vector3 toPlayer = listener - soundFrom.position;
            toPlayer.y = 0f;
            Vector3 dir = toPlayer.normalized;
            if (dir.sqrMagnitude < 1e-4f)
                dir = -cam.transform.forward;
            dir.y = 0f;
            dir.Normalize();
            float startDistance = Mathf.Min(toPlayer.magnitude > 0.1f ? toPlayer.magnitude : approachStartDistance, approachStartDistance);
            float mouthY = soundFrom.position.y - approachDropBelowEye;
            Vector3 from = listener - dir * startDistance;
            Vector3 to = listener - dir * Mathf.Min(startDistance, approachStopDistance);
            from.y = mouthY;
            to.y = mouthY;
            AudioClip approach = approachClip;
            AudioSource approachSrc = approach != null ? PlayAt(approach, from, approachVolume, approachMinDistance) : null;
            if (approachSrc != null && approach != null)
                StartCoroutine(Glide(approachSrc.transform, from, to, approach.length));
            if (lookBackDelay > 0f)
                yield return new WaitForSeconds(lookBackDelay);
        }

        // Drop the lock and turn round straight from the lock view; the look camera outranks the exit blend.
        PlayerControlGate.Acquire(LockReason);
        _locked = true;
        if (lookCamera == null)
        {
            if (lockMinigame != null && lockMinigame.IsRunning)
                lockMinigame.ForceExit();
            EndCinematic();
            LookBackFinished?.Invoke();
            yield break;
        }

        BeginCinematic(cam);
        Vector3 start = cam.transform.position;
        Quaternion startRot = cam.transform.rotation;
        float startFov = cam.fieldOfView;
        Vector3 look = LookPoint();
        Quaternion endRot = Quaternion.LookRotation(look - start);
        Pose(start, startRot, startFov);
        lookCamera.Priority = lookPriority;
        if (lockMinigame != null && lockMinigame.IsRunning)
            lockMinigame.ForceExit();
        // Normal look-back sting on the turn; escape sting waits until he is fully framed.
        // Stop any approach whoosh/scream so it cannot mask or replace the turn sting.
        if (!_escapeLook)
        {
            StopChildOneShots();
            PlayTurnSting();
        }

        for (float t = 0f; t < lookTurnTime; t += Time.deltaTime)
        {
            float u = Mathf.SmoothStep(0f, 1f, t / lookTurnTime);
            // A quick head turn: overshoots a touch, with a small dip in the middle.
            Quaternion rot = Quaternion.SlerpUnclamped(startRot, endRot, u + Mathf.Sin(u * Mathf.PI) * 0.06f);
            rot *= Quaternion.Euler(Mathf.Sin(u * Mathf.PI) * 4f, 0f, 0f);
            Pose(start, rot, startFov);
            yield return null;
        }

        Vector3 end = start + (look - start).normalized * lookPush;
        for (float t = 0f; t < lookZoomTime; t += Time.deltaTime)
        {
            float u = Mathf.SmoothStep(0f, 1f, t / lookZoomTime);
            Vector3 pos = Vector3.Lerp(start, end, u);
            Pose(pos, Quaternion.LookRotation(look - pos), Mathf.Lerp(startFov, lookZoomFov, u));
            yield return null;
        }
        // Land fully zoomed, then wait a beat so the flicker isn't early.
        Pose(end, Quaternion.LookRotation(look - end), lookZoomFov);
        if (_escapeLook)
        {
            yield return null;
            yield return new WaitForSeconds(0.2f);
            yield return EscapeRevealPunch();
        }

        if (lookHold > 0f)
            yield return new WaitForSeconds(lookHold);

        // Hand control back while still facing the look target — do not snap to the pre-shot door view.
        Quaternion stayFacing = lookCamera.transform.rotation;
        CinemachineMouseLook mouseLook = FindFirstObjectByType<CinemachineMouseLook>();
        if (mouseLook != null)
            mouseLook.SetLook(stayFacing);
        lookCamera.Priority = 0;
        if (lookBlendOut > 0f)
        {
            float blend = lookBlendOut;
            // Escape: shorter blend so it eases into free look without fighting the lock camera.
            if (_escapeLook)
                blend = Mathf.Min(blend, 0.45f);
            float t = 0f;
            while (t < blend)
            {
                if (mouseLook != null)
                    mouseLook.SetLook(stayFacing);
                t += Time.deltaTime;
                yield return null;
            }
        }
        if (mouseLook != null)
            mouseLook.SetLook(stayFacing);
        EndCinematic();
        LookBackFinished?.Invoke();
    }

    Vector3 LookPoint()
    {
        if (_escapeLook)
        {
            Transform target = escapeLookTarget;
            if (target == null && wallStanderChase != null)
                target = wallStanderChase.transform;
            if (target != null)
                return target.position + Vector3.up * escapeLookHeight;
        }
        return behindPoint != null ? behindPoint.position : transform.position;
    }

    void PlaceWallStanderForEscapeReveal()
    {
        if (wallStanderChase == null)
            wallStanderChase = FindFirstObjectByType<WallStanderChase>(FindObjectsInactive.Include);
        wallStanderChase?.PlaceForEscapeReveal();
        if (escapeLookTarget == null && wallStanderChase != null)
            escapeLookTarget = wallStanderChase.transform;
    }

    /// <summary>Sting + several brief flashes so he reads clearly, then lights stay off.</summary>
    IEnumerator EscapeRevealPunch()
    {
        AudioClip sting = escapeRevealSting != null ? escapeRevealSting : turnSting;
        Play2D(sting, escapeRevealSting != null ? escapeRevealStingVolume : turnStingVolume);

        StopEscapeLampFlicker(leaveOn: false);
        int flashes = Mathf.Clamp(escapeRevealFlashes, 3, 6);
        for (int i = 0; i < flashes; i++)
        {
            SetEscapeLights(true);
            yield return new WaitForSeconds(Mathf.Max(0.04f, escapeRevealFlashOn));
            SetEscapeLights(false);
            if (i < flashes - 1)
                yield return new WaitForSeconds(Mathf.Max(0.03f, escapeRevealFlashOff));
        }
    }

    void StopEscapeLampFlicker(bool leaveOn = false)
    {
        if (_escapeLampFlicker != null)
        {
            StopCoroutine(_escapeLampFlicker);
            _escapeLampFlicker = null;
        }
        SetEscapeLights(leaveOn);
    }

    void SetEscapeLights(bool on)
    {
        SetEscapeLampsLit(on);
        if (escapeFlickerSwitches == null)
            return;
        foreach (LightSwitch sw in escapeFlickerSwitches)
            sw?.PulseLit(on);
    }

    void SetEscapeLampsLit(bool lit)
    {
        EnsureEscapeLightsCached();
        if (_cachedEscapeLights == null)
            return;
        for (int i = 0; i < _cachedEscapeLights.Length; i++)
        {
            Light l = _cachedEscapeLights[i];
            if (l != null)
                l.enabled = lit;
        }
    }

    void EnsureEscapeLightsCached()
    {
        if (_escapeLightsCached)
            return;
        _escapeLightsCached = true;
        if (escapeFlickerLamps == null || escapeFlickerLamps.Length == 0)
        {
            _cachedEscapeLights = System.Array.Empty<Light>();
            return;
        }

        var list = new List<Light>(8);
        foreach (GameObject lamp in escapeFlickerLamps)
        {
            if (lamp == null)
                continue;
            foreach (Light l in lamp.GetComponentsInChildren<Light>(true))
            {
                if (l == null)
                    continue;
                if (l.GetComponentInParent<CinemachineCamera>() != null)
                    continue;
                list.Add(l);
            }
        }
        _cachedEscapeLights = list.ToArray();
    }

    void Pose(Vector3 position, Quaternion rotation, float fov)
    {
        lookCamera.transform.SetPositionAndRotation(position, rotation);
        LensSettings lens = lookCamera.Lens;
        lens.FieldOfView = fov;
        lookCamera.Lens = lens;
    }

    void BeginCinematic(Camera cam)
    {
        _hudHidden = true;
        GameplayHudVisibility.Instance?.HideGameplayHud();
        // Hands and lighter ride on the main camera; hide them for the shot but keep their lights on.
        _hiddenViewmodel.Clear();
        foreach (Renderer r in cam.GetComponentsInChildren<Renderer>())
            if (r.enabled)
            {
                r.enabled = false;
                _hiddenViewmodel.Add(r);
            }
    }

    void EndCinematic()
    {
        if (lookCamera != null)
            lookCamera.Priority = 0;
        foreach (Renderer r in _hiddenViewmodel)
            if (r != null)
                r.enabled = true;
        _hiddenViewmodel.Clear();
        if (_hudHidden)
        {
            _hudHidden = false;
            GameplayHudVisibility.Instance?.ShowGameplayHud();
        }
        if (_locked)
        {
            _locked = false;
            PlayerControlGate.Release(LockReason);
        }
    }

    void PlayTurnSting()
    {
        AudioClip clip = turnSting;
        if (clip == null)
        {
            Debug.LogError("DoorBreakScare: turnSting is not assigned.", this);
            return;
        }
        Play2D(clip, turnStingVolume);
    }

    void StopChildOneShots()
    {
        var sources = GetComponentsInChildren<AudioSource>(true);
        for (int i = 0; i < sources.Length; i++)
        {
            if (sources[i] != null && sources[i].isPlaying)
                sources[i].Stop();
        }
    }

    void Play2D(AudioClip clip, float volume)
    {
        if (clip == null)
            return;
        SoundManager.EnsureClipReady(clip);
        // Dedicated 2D source so this never gets lost in the SFX pool / wrong bus.
        var go = new GameObject(clip.name + " (Door Scare Turn)");
        go.transform.SetParent(transform, false);
        var src = go.AddComponent<AudioSource>();
        src.playOnAwake = false;
        src.outputAudioMixerGroup = mixerGroup;
        src.spatialBlend = 0f;
        src.clip = clip;
        src.volume = Mathf.Clamp01(volume);
        src.Play();
        Destroy(go, clip.length / Mathf.Max(0.1f, Mathf.Abs(src.pitch)) + 0.35f);
    }

    static IEnumerator Glide(Transform t, Vector3 from, Vector3 to, float duration)
    {
        for (float e = 0f; e < duration && t != null; e += Time.deltaTime)
        {
            t.position = Vector3.Lerp(from, to, e / duration);
            yield return null;
        }
    }

    AudioSource PlayAt(AudioClip clip, Vector3 position, float volume, float minDistance, float pitch = 1f)
    {
        return SpatialAudio.PlayAt(clip, position, volume, minDistance, 20f, mixerGroup, transform, pitch);
    }
}
