using UnityEngine;
using UnityEngine.Audio;

/// <summary>
/// Scripted music for two beats (there's no exploration music, so these carry the scenes):
/// - Forcing the corridor door: a suspense bed while the lock minigame runs. It keeps climbing (with time on the lock
///   and with every jam broken) until the running steps come, then cuts dead and never plays again.
/// - Going back for the bar: once the guy is placed (the note was read), the moment the player steps into the
///   corridor the ambient track starts; it keeps climbing the closer they get to him and stops dead, leaving silence,
///   the moment the bar behind him is picked up.
/// </summary>
[DisallowMultipleComponent]
public class TensionMusicCues : MonoBehaviour
{
    [SerializeField] AudioMixerGroup musicGroup;

    [Header("Forcing the door")]
    [SerializeField] ScrewdriverLockBreakMinigame lockMinigame;
    [SerializeField] DoorBreakScare doorScare;
    [SerializeField] AudioClip doorMusic;
    [SerializeField, Range(0f, 1f)] float doorStartVolume = 0.4f;
    [SerializeField, Range(0f, 1f)] float doorVolume = 1f;
    [Tooltip("Seconds on the lock to climb from the start volume to full.")]
    [SerializeField] float doorBuildTime = 45f;
    [Tooltip("Jams broken before the steps come; each one pushes the build that much further.")]
    [SerializeField] int doorBuildJams = 3;
    [SerializeField] float doorFadeIn = 1.5f;
    [Tooltip("Seconds to fade while the player is off the lock (it comes back where it left off).")]
    [SerializeField] float doorFadeOut = 1.5f;
    [Tooltip("Seconds to cut to silence when the steps are about to come.")]
    [SerializeField] float doorCutTime = 0.3f;

    [Header("Going back for the bar")]
    [SerializeField] CorpseVanishEvent wallStanderEvent;
    [Tooltip("The corridor's main room door; forward points out of the corridor. Crossing it inward starts the music.")]
    [SerializeField] Transform corridorEntrance;
    [Tooltip("Half the corridor's width at the entrance, so walking past it in the room doesn't count.")]
    [SerializeField] float corridorHalfWidth = 1.5f;
    [SerializeField] AudioClip returnMusic;
    [SerializeField, Range(0f, 1f)] float returnFarVolume = 0.45f;
    [SerializeField, Range(0f, 1f)] float returnNearVolume = 1f;
    [Tooltip("Distance to him (m) at or beyond which it plays at the far volume.")]
    [SerializeField] float returnFarDistance = 16f;
    [Tooltip("Distance to him (m) at which it reaches the near volume (about where the bar is picked up); it climbs fastest over the last stretch.")]
    [SerializeField] float returnNearDistance = 1.5f;
    [SerializeField] float returnFadeIn = 1.5f;
    [Tooltip("Seconds to cut to silence when the bar is picked up.")]
    [SerializeField] float returnCutTime = 0.12f;

    enum Cue { Waiting, Playing, Done }

    AudioSource _door;
    AudioSource _return;
    Cue _doorState;
    Cue _returnState;
    float _doorTime;
    Transform _player;

    public bool ReturnMusicPlaying => _returnState == Cue.Playing;

    /// <summary>Checkpoint load: the walk back for the bar had already started its music.</summary>
    public void RestoreReturnMusic()
    {
        if (_returnState != Cue.Waiting || returnMusic == null || wallStanderEvent == null || wallStanderEvent.BarTaken)
            return;
        GameObject p = GameObject.FindWithTag("Player");
        if (p == null)
            return;
        _player = p.transform;
        _returnState = Cue.Playing;
        _return = CreateSource("Return Music", returnMusic);
    }

    void OnEnable()
    {
        if (doorScare != null)
            doorScare.ApproachStarted += OnApproachStarted;
    }

    void OnDisable()
    {
        if (doorScare != null)
            doorScare.ApproachStarted -= OnApproachStarted;
    }

    void OnApproachStarted() => _doorState = Cue.Done;

    void Update()
    {
        bool chase = SoundManager.Instance != null && SoundManager.Instance.IsChaseMusicActive;
        TickDoor(chase);
        TickReturn(chase);
    }

    void TickDoor(bool chase)
    {
        if (doorMusic == null || lockMinigame == null)
            return;

        if (_doorState == Cue.Done)
        {
            if (_door == null)
                return;
            Fade(_door, 0f, doorVolume / Mathf.Max(0.01f, doorCutTime));
            if (_door.volume <= 0f)
            {
                Destroy(_door.gameObject);
                _door = null;
            }
            return;
        }

        bool onLock = lockMinigame.IsRunning && !chase;
        if (onLock && _door == null)
        {
            _door = CreateSource("Door Tension Music", doorMusic);
            _doorState = Cue.Playing;
        }
        if (_door == null)
            return;

        if (!onLock)
        {
            Fade(_door, 0f, doorVolume / Mathf.Max(0.01f, doorFadeOut));
            return;
        }
        _doorTime += Time.deltaTime;
        float build = Mathf.Max(_doorTime / Mathf.Max(0.01f, doorBuildTime),
                                lockMinigame.JamsBroken / (float)Mathf.Max(1, doorBuildJams));
        float target = Mathf.Lerp(doorStartVolume, doorVolume, Mathf.Clamp01(build));
        Fade(_door, target, doorVolume / Mathf.Max(0.01f, doorFadeIn));
    }

    void TickReturn(bool chase)
    {
        if (returnMusic == null || wallStanderEvent == null || _returnState == Cue.Done)
            return;

        if (_returnState == Cue.Waiting)
        {
            if (!wallStanderEvent.WallStanderShown || wallStanderEvent.BarTaken || !InCorridor())
                return;
            _returnState = Cue.Playing;
            _return = CreateSource("Return Music", returnMusic);
        }

        if (wallStanderEvent.BarTaken)
        {
            Fade(_return, 0f, returnNearVolume / Mathf.Max(0.01f, returnCutTime));
            if (_return.volume <= 0f)
            {
                Destroy(_return.gameObject);
                _return = null;
                _returnState = Cue.Done;
            }
            return;
        }

        float target = 0f;
        if (!chase)
        {
            Vector3 offset = wallStanderEvent.WallStanderPosition - _player.position;
            offset.y = 0f;
            float near = Mathf.InverseLerp(returnFarDistance, returnNearDistance, offset.magnitude);
            target = Mathf.Lerp(returnFarVolume, returnNearVolume, near * near);
        }
        Fade(_return, target, returnNearVolume / Mathf.Max(0.01f, returnFadeIn));
    }

    bool InCorridor()
    {
        if (corridorEntrance == null)
            return false;
        if (_player == null)
        {
            GameObject p = GameObject.FindWithTag("Player");
            if (p == null)
                return false;
            _player = p.transform;
        }
        Vector3 offset = _player.position - corridorEntrance.position;
        return Vector3.Dot(offset, corridorEntrance.forward) < 0f
               && Mathf.Abs(Vector3.Dot(offset, corridorEntrance.right)) <= corridorHalfWidth;
    }

    static void Fade(AudioSource source, float target, float ratePerSecond)
    {
        source.volume = Mathf.MoveTowards(source.volume, target, ratePerSecond * Time.deltaTime);
        if (source.volume <= 0f && target <= 0f)
        {
            if (source.isPlaying)
                source.Pause();
        }
        else if (!source.isPlaying)
            source.UnPause();
    }

    AudioSource CreateSource(string sourceName, AudioClip clip)
    {
        var go = new GameObject(sourceName);
        go.transform.SetParent(transform, false);
        var src = go.AddComponent<AudioSource>();
        src.clip = clip;
        src.loop = true;
        src.playOnAwake = false;
        src.spatialBlend = 0f;
        src.volume = 0f;
        src.outputAudioMixerGroup = musicGroup;
        src.Play();
        return src;
    }
}
