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
    [Tooltip("Seconds after the jam breaks, so the crack of the lock doesn't cover the sound.")]
    [SerializeField] float approachDelay = 0.9f;
    [Tooltip("Comes up behind the player, moving from the start distance to the stop distance over its length.")]
    [SerializeField] AudioClip approachClip;
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

    /// <summary>The steps start coming.</summary>
    public event Action ApproachStarted;
    /// <summary>The look back is over and the player has control again.</summary>
    public event Action LookBackFinished;

    bool _hookDone;
    bool _falseAlarmDone;
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
        // One beat per jam; a beat whose jam was missed (not armed yet) plays on the next one, the look back first.
        if (!_lookDone && Reached(lookBackOnJam, count))
        {
            _lookDone = true;
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

    IEnumerator LookBackRoutine()
    {
        ApproachStarted?.Invoke();
        yield return new WaitForSeconds(approachDelay);
        Camera cam = Camera.main;
        if (cam == null || behindPoint == null)
            yield break;

        // Something rushes from the dark toward the player's back.
        Vector3 listener = cam.transform.position;
        Vector3 toPlayer = listener - behindPoint.position;
        toPlayer.y = 0f;
        Vector3 dir = toPlayer.normalized;
        float startDistance = Mathf.Min(toPlayer.magnitude, approachStartDistance);
        // Head height: the approach clip is a scream, and with HRTF a source at the floor is heard from below.
        float mouthY = behindPoint.position.y - approachDropBelowEye;
        Vector3 from = listener - dir * startDistance;
        Vector3 to = listener - dir * Mathf.Min(startDistance, approachStopDistance);
        from.y = mouthY;
        to.y = mouthY;
        AudioSource approach = PlayAt(approachClip, from, approachVolume, approachMinDistance);
        if (approach != null)
            StartCoroutine(Glide(approach.transform, from, to, approachClip.length));
        if (lookBackDelay > 0f)
            yield return new WaitForSeconds(lookBackDelay);

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
