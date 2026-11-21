using UnityEngine;
using UnityEngine.Audio;

/// <summary>
/// Return-trip music for the bar: once the guy is placed (the note was read), stepping into the corridor starts an
/// ambient bed that climbs the closer the player gets to him and cuts when the bar is picked up.
/// (Lock-break tension music was removed.)
/// </summary>
[DisallowMultipleComponent]
public class TensionMusicCues : MonoBehaviour
{
    [SerializeField] AudioMixerGroup musicGroup;

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

    AudioSource _return;
    Cue _returnState;
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

    void Awake()
    {
        // Bar-return bed removed — keep component for checkpoint restore hooks, but never start music.
        returnMusic = null;
        if (_return != null)
        {
            Destroy(_return.gameObject);
            _return = null;
        }
        _returnState = Cue.Done;
    }

    void Update()
    {
        bool chase = SoundManager.Instance != null && SoundManager.Instance.IsChaseMusicActive;
        TickReturn(chase);
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
