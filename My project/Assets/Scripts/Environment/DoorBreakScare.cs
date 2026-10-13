using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.Audio;

/// <summary>
/// While the player breaks a lock open with the screwdriver, something moves in the dark behind them:
/// - First jam forced through: a hook behind them swings and something snarls in the dark.
/// - Second: a false alarm, a few dull knocks from further off, then nothing.
/// - Third: quick bare steps come up behind them; the player drops the lock, turns round on a sting and the
///   view pushes into the dark. Nothing's there. The view stays turned round; the lock keeps its progress.
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

    [Header("First breakthrough")]
    [SerializeField] HangingHook hook;
    [Tooltip("Speed (m/s) the hook is shoved at, across the room.")]
    [SerializeField] float hookKnockSpeed = 1.2f;
    [SerializeField] AudioClip firstClip;
    [SerializeField, Range(0f, 1f)] float firstVolume = 1f;
    [Tooltip("Full volume within this distance of the hook.")]
    [SerializeField] float firstMinDistance = 3f;
    [Tooltip("Seconds after the jam breaks, so the crack of the lock doesn't cover it.")]
    [SerializeField] float firstDelay = 0.9f;

    [Header("Second breakthrough (false alarm)")]
    [SerializeField] AudioClip secondClip;
    [SerializeField, Range(0f, 1f)] float secondVolume = 0.8f;
    [Tooltip("Where the knocks come from. Empty = the behind point.")]
    [SerializeField] Transform secondPoint;
    [SerializeField] float secondMinDistance = 3f;
    [SerializeField] float secondDelay = 1.6f;

    [Header("Third breakthrough")]
    [SerializeField] int lookBackJam = 3;
    [Tooltip("Seconds after the jam breaks, so the crack of the lock doesn't cover the first step.")]
    [SerializeField] float approachDelay = 0.9f;
    [SerializeField] AudioClip[] approachSteps;
    [SerializeField, Range(0f, 1f)] float approachVolume = 1f;
    [Tooltip("Steps stay at full volume within this distance, so the lock's own noise doesn't bury them.")]
    [SerializeField] float approachMinDistance = 4f;
    [SerializeField] int approachStepCount = 5;
    [SerializeField] float approachStepInterval = 0.3f;
    [Tooltip("The first step is at most this far behind the player.")]
    [SerializeField] float approachStartDistance = 4.5f;
    [Tooltip("The steps stop this far behind the player.")]
    [SerializeField] float approachStopDistance = 1.8f;
    [Tooltip("Played right behind the player after the steps stop. Empty = turn right after the last step.")]
    [SerializeField] AudioClip closeClip;
    [SerializeField, Range(0f, 1f)] float closeVolume = 0.9f;
    [SerializeField] float closeDistance = 0.8f;
    [SerializeField] float closeDelay = 0.25f;
    [Tooltip("Seconds between the close sound (or the last step) and the head turn.")]
    [SerializeField] float reactionDelay = 0.15f;

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

    /// <summary>The steps start coming (third breakthrough).</summary>
    public event Action ApproachStarted;
    /// <summary>The look back is over and the player has control again.</summary>
    public event Action LookBackFinished;

    bool _firstDone;
    bool _secondDone;
    bool _lookDone;
    bool _locked;
    bool _hudHidden;
    readonly List<Renderer> _hiddenViewmodel = new List<Renderer>();

    void Awake()
    {
        if (lookCamera != null)
            lookCamera.Priority = 0;
    }

    void OnEnable()
    {
        if (lockMinigame != null)
            lockMinigame.JamBroken += OnJamBroken;
    }

    void OnDisable()
    {
        if (lockMinigame != null)
            lockMinigame.JamBroken -= OnJamBroken;
        EndCinematic();
    }

    bool Armed => (waitFor == null || waitFor.CurrentStage == CorpseVanishEvent.Stage.Done)
                  && !(SoundManager.Instance != null && SoundManager.Instance.IsChaseMusicActive);

    void OnJamBroken(int count)
    {
        if (!Armed)
            return;
        if (!_firstDone && count >= 1)
        {
            _firstDone = true;
            StartCoroutine(FirstRoutine());
        }
        else if (!_secondDone && count >= 2 && count < lookBackJam)
        {
            _secondDone = true;
            StartCoroutine(SecondRoutine());
        }
        else if (!_lookDone && count >= lookBackJam)
        {
            _lookDone = true;
            StartCoroutine(LookBackRoutine());
        }
    }

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

    IEnumerator LookBackRoutine()
    {
        ApproachStarted?.Invoke();
        yield return new WaitForSeconds(approachDelay);
        Camera cam = Camera.main;
        if (cam == null || behindPoint == null)
            yield break;

        // Steps rush from the dark toward the player's back.
        Vector3 listener = cam.transform.position;
        Vector3 toPlayer = listener - behindPoint.position;
        toPlayer.y = 0f;
        Vector3 dir = toPlayer.normalized;
        float startDistance = Mathf.Min(toPlayer.magnitude, approachStartDistance);
        Vector3 from = listener - dir * startDistance;
        float run = Mathf.Max(0f, startDistance - approachStopDistance);
        float floorY = behindPoint.position.y - 1.4f;
        if (approachSteps != null && approachSteps.Length > 0)
        {
            for (int i = 0; i < approachStepCount; i++)
            {
                float u = approachStepCount > 1 ? (float)i / (approachStepCount - 1) : 1f;
                Vector3 p = from + dir * (run * u);
                p.y = floorY;
                AudioClip step = approachSteps[i % approachSteps.Length];
                PlayAt(step, p, approachVolume * Mathf.Lerp(0.85f, 1f, u), approachMinDistance, UnityEngine.Random.Range(0.9f, 0.98f));
                yield return new WaitForSeconds(approachStepInterval * Mathf.Lerp(1.15f, 0.85f, u));
            }
        }

        if (closeClip != null)
        {
            yield return new WaitForSeconds(closeDelay);
            PlayAt(closeClip, listener - dir * closeDistance, closeVolume, 0.6f);
        }
        if (reactionDelay > 0f)
            yield return new WaitForSeconds(reactionDelay);

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
        Vector3 look = behindPoint.position;
        Quaternion endRot = Quaternion.LookRotation(look - start);
        Pose(start, startRot, startFov);
        lookCamera.Priority = lookPriority;
        if (lockMinigame != null && lockMinigame.IsRunning)
            lockMinigame.ForceExit();
        Play2D(turnSting, turnStingVolume);

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
        if (lookHold > 0f)
            yield return new WaitForSeconds(lookHold);

        // The player stays facing where the shot ended; only the zoom and the push ease back.
        CinemachineMouseLook mouseLook = FindFirstObjectByType<CinemachineMouseLook>();
        if (mouseLook != null)
            mouseLook.SetLook(lookCamera.transform.rotation);
        lookCamera.Priority = 0;
        if (lookBlendOut > 0f)
            yield return new WaitForSeconds(lookBlendOut);
        EndCinematic();
        LookBackFinished?.Invoke();
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

    void Play2D(AudioClip clip, float volume)
    {
        if (clip == null)
            return;
        var go = new GameObject(clip.name + " (Door Scare)");
        go.transform.SetParent(transform, false);
        var src = go.AddComponent<AudioSource>();
        src.playOnAwake = false;
        src.outputAudioMixerGroup = mixerGroup;
        src.spatialBlend = 0f;
        src.PlayOneShot(clip, volume);
        Destroy(go, clip.length + 0.25f);
    }

    void PlayAt(AudioClip clip, Vector3 position, float volume, float minDistance, float pitch = 1f)
    {
        if (clip == null)
            return;
        var go = new GameObject(clip.name + " (Door Scare)");
        go.transform.SetParent(transform, false);
        go.transform.position = position;
        var src = go.AddComponent<AudioSource>();
        src.playOnAwake = false;
        src.outputAudioMixerGroup = mixerGroup;
        src.spatialBlend = 1f;
        src.rolloffMode = AudioRolloffMode.Logarithmic;
        src.minDistance = minDistance;
        src.maxDistance = 20f;
        src.dopplerLevel = 0f;
        src.pitch = pitch;
        src.PlayOneShot(clip, volume);
        Destroy(go, clip.length / Mathf.Max(0.1f, pitch) + 0.25f);
    }
}
