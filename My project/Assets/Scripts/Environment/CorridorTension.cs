using UnityEngine;
using UnityEngine.Audio;

/// <summary>
/// The corridor in the dark, from the lights going out until the player walks out through the far door:
/// - a low drone that swells while they force the lock, and drops to dead silence just before the steps come;
/// - sparse creaks, drips and rattles somewhere behind them, never while the lock scenes play;
/// - walking out through the door: the room lights flick on by themselves and the corridor falls quiet.
/// Coming back for the bar: once the player is back in the cage room with the guy standing there, the drone and the
/// sounds come back around them, until the bar is picked up (then dead silence).
/// </summary>
[DisallowMultipleComponent]
public class CorridorTension : MonoBehaviour
{
    enum State { Waiting, Active, Left, Return, Done }

    [Tooltip("Starts when this event is done (the lights went out).")]
    [SerializeField] CorpseVanishEvent startAfter;
    [SerializeField] ScrewdriverLockBreakMinigame lockMinigame;
    [SerializeField] DoorBreakScare doorScare;
    [SerializeField] AudioMixerGroup mixerGroup;

    [Header("Drone")]
    [SerializeField] AudioClip droneLoop;
    [SerializeField, Range(0f, 1f)] float droneVolume = 0.18f;
    [Tooltip("Volume with the lock bar full; it climbs with the bar.")]
    [SerializeField, Range(0f, 1f)] float droneLockVolume = 0.45f;
    [Tooltip("Pitch with the lock bar full.")]
    [SerializeField] float droneLockPitch = 1.12f;
    [SerializeField] float droneFadeIn = 6f;
    [Tooltip("Seconds to cut to silence when the steps are about to come.")]
    [SerializeField] float droneCutTime = 0.35f;
    [Tooltip("Seconds to creep back in after the look back.")]
    [SerializeField] float droneReturnTime = 8f;
    [SerializeField] float droneFadeOut = 3f;

    [Header("Sounds in the dark")]
    [SerializeField] AudioClip[] darkSounds;
    [SerializeField] Vector2 darkInterval = new Vector2(7f, 15f);
    [SerializeField] float firstDarkDelay = 5f;
    [SerializeField] Vector2 darkVolume = new Vector2(0.45f, 0.85f);
    [Tooltip("How far from the player they play, mostly behind them.")]
    [SerializeField] Vector2 darkDistance = new Vector2(3f, 6.5f);
    [SerializeField] float darkMinDistance = 2.5f;
    [Tooltip("Sounds are kept inside this box (world space).")]
    [SerializeField] Bounds area = new Bounds(new Vector3(1.35f, -2f, 10.65f), new Vector3(8.6f, 2f, 2.6f));

    [Header("Leaving")]
    [Tooltip("At the far door, forward pointing out of the corridor.")]
    [SerializeField] Transform exitPlane;
    [SerializeField] float exitMargin = 1f;
    [SerializeField] LightSwitch lightsOnExit;

    [Header("Back in the cage room")]
    [Tooltip("The cage room (world space): stepping back into it while the guy stands there brings the drone and the sounds back, kept inside this box.")]
    [SerializeField] Bounds returnArea = new Bounds(new Vector3(10.4f, -2f, 9f), new Vector3(9f, 2.5f, 7f));
    [SerializeField, Range(0f, 1f)] float returnDroneVolume = 0.3f;
    [SerializeField] float returnFadeIn = 4f;
    [SerializeField] float returnFirstDarkDelay = 2.5f;
    [Tooltip("Seconds to cut to silence when the bar is picked up.")]
    [SerializeField] float returnCutTime = 0.12f;

    State _state;
    AudioSource _drone;
    float _droneLevel;
    float _droneScale;
    float _droneRampTime;
    bool _cut;
    float _nextDark;
    float _fadeOutTime;
    Transform _player;

    /// <summary>The player has walked out through the far door at least once.</summary>
    public bool HasLeftCorridor => _state == State.Left || _state == State.Return || _state == State.Done;

    /// <summary>Checkpoint load: the corridor was already left; back in the cage room with him there, it returns.</summary>
    public void RestoreLeft()
    {
        StopDrone();
        _cut = false;
        _state = State.Left;
    }

    void OnEnable()
    {
        if (doorScare != null)
        {
            doorScare.ApproachStarted += OnApproachStarted;
            doorScare.LookBackFinished += OnLookBackFinished;
        }
    }

    void OnDisable()
    {
        if (doorScare != null)
        {
            doorScare.ApproachStarted -= OnApproachStarted;
            doorScare.LookBackFinished -= OnLookBackFinished;
        }
    }

    void OnApproachStarted() => _cut = true;

    void OnLookBackFinished()
    {
        _cut = false;
        _droneScale = 0f;
        _droneRampTime = droneReturnTime;
        _nextDark = Time.time + Random.Range(darkInterval.x, darkInterval.y);
    }

    void Update()
    {
        if (_state == State.Done)
        {
            FadeDrone(0f, _fadeOutTime);
            if (_drone == null || _droneLevel <= 0f)
            {
                StopDrone();
                enabled = false;
            }
            return;
        }

        if (_state == State.Waiting)
        {
            if (startAfter != null && startAfter.CurrentStage != CorpseVanishEvent.Stage.Done)
                return;
            Begin();
        }

        if (_player == null)
        {
            GameObject p = GameObject.FindWithTag("Player");
            if (p == null)
                return;
            _player = p.transform;
        }

        if (_state == State.Left)
        {
            FadeDrone(0f, droneFadeOut);
            if (_drone != null && _droneLevel <= 0f)
                StopDrone();
            if (startAfter == null)
            {
                _state = State.Done;
                return;
            }
            if (startAfter.BarTaken)
                return;
            if (startAfter.WallStanderShown && returnArea.Contains(_player.position))
                BeginReturn();
            return;
        }

        if (_state == State.Return)
        {
            if (startAfter.BarTaken)
            {
                _state = State.Done;
                _fadeOutTime = returnCutTime;
                return;
            }
            TickDrone(returnDroneVolume);
            TickDark(returnArea);
            return;
        }

        if (exitPlane != null
            && Vector3.Dot(_player.position - exitPlane.position, exitPlane.forward) > exitMargin)
        {
            Leave();
            return;
        }

        TickDrone(droneVolume);
        TickDark(area);
    }

    void Begin()
    {
        _state = State.Active;
        _droneScale = 0f;
        _droneRampTime = droneFadeIn;
        _nextDark = Time.time + firstDarkDelay;
        StartDrone();
    }

    void BeginReturn()
    {
        _state = State.Return;
        _cut = false;
        _droneScale = 0f;
        _droneRampTime = returnFadeIn;
        _nextDark = Time.time + returnFirstDarkDelay;
        if (_drone == null)
            StartDrone();
    }

    void StartDrone()
    {
        if (droneLoop == null)
            return;
        var go = new GameObject("Corridor Drone");
        go.transform.SetParent(transform, false);
        _drone = go.AddComponent<AudioSource>();
        _drone.clip = droneLoop;
        _drone.loop = true;
        _drone.playOnAwake = false;
        _drone.spatialBlend = 0f;
        _drone.volume = 0f;
        _droneLevel = 0f;
        _drone.outputAudioMixerGroup = mixerGroup;
        _drone.Play();
    }

    void StopDrone()
    {
        if (_drone != null)
            Destroy(_drone.gameObject);
        _drone = null;
        _droneLevel = 0f;
    }

    void TickDrone(float baseVolume)
    {
        if (_drone == null)
            return;
        bool locking = lockMinigame != null && lockMinigame.IsRunning;
        float bar = locking ? lockMinigame.BreakNormalized : 0f;
        if (_cut)
        {
            FadeDrone(0f, droneCutTime);
            return;
        }
        _droneScale = Mathf.MoveTowards(_droneScale, 1f, Time.deltaTime / Mathf.Max(0.01f, _droneRampTime));
        float target = Mathf.Lerp(baseVolume, droneLockVolume, bar) * _droneScale;
        _droneLevel = Mathf.MoveTowards(_droneLevel, target, Time.deltaTime * 0.25f);
        _drone.volume = _droneLevel;
        _drone.pitch = Mathf.MoveTowards(_drone.pitch, Mathf.Lerp(1f, droneLockPitch, bar), Time.deltaTime * 0.05f);
    }

    void FadeDrone(float target, float seconds)
    {
        if (_drone == null)
            return;
        float rate = Mathf.Max(_droneLevel, 0.01f) / Mathf.Max(0.01f, seconds);
        _droneLevel = Mathf.MoveTowards(_droneLevel, target, rate * Time.deltaTime);
        _drone.volume = _droneLevel;
    }

    void TickDark(Bounds bounds)
    {
        if (darkSounds == null || darkSounds.Length == 0 || Time.time < _nextDark)
            return;
        bool busy = _cut || PlayerControlGate.IsInputBlocked
                    || (lockMinigame != null && lockMinigame.CurrentStage != ScrewdriverLockBreakMinigame.Stage.Idle);
        if (busy)
        {
            _nextDark = Time.time + 2f;
            return;
        }
        _nextDark = Time.time + Random.Range(darkInterval.x, darkInterval.y);

        AudioClip clip = darkSounds[Random.Range(0, darkSounds.Length)];
        if (clip == null)
            return;
        Camera cam = Camera.main;
        Vector3 back = cam != null ? -cam.transform.forward : -_player.forward;
        back.y = 0f;
        Vector3 dir = Quaternion.Euler(0f, Random.Range(-70f, 70f), 0f) * back.normalized;
        Vector3 p = _player.position + dir * Random.Range(darkDistance.x, darkDistance.y);
        p.y = _player.position.y + Random.Range(0.2f, 2f);
        p = bounds.ClosestPoint(p);

        SpatialAudio.PlayAt(clip, p, Random.Range(darkVolume.x, darkVolume.y), darkMinDistance, 20f, mixerGroup, transform,
            Random.Range(0.9f, 1.05f));
    }

    void Leave()
    {
        _state = State.Left;
        if (lightsOnExit != null && !lightsOnExit.IsOn)
            lightsOnExit.Interact();
    }
}
