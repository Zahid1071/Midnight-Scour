using UnityEngine;
using UnityEngine.Audio;

/// <summary>
/// The player's own running footstep sounds, on the steps counted by <see cref="PlayerFootstepRhythm"/>.
/// Walking and sneaking are silent. Played 2D and kept quiet so they never mask NPC footsteps.
/// </summary>
[RequireComponent(typeof(PlayerFootstepRhythm))]
public class PlayerFootsteps : MonoBehaviour
{
    [SerializeField] FootstepBank bank;
    [SerializeField] AudioMixerGroup output;
    [SerializeField, Range(0f, 1f)] float runVolume = 0.5f;
    [SerializeField] Vector2 pitch = new Vector2(0.97f, 1.06f);

    PlayerFootstepRhythm _rhythm;
    AudioSource _source;

    void Awake()
    {
        _rhythm = GetComponent<PlayerFootstepRhythm>();

        var go = new GameObject("Player Footsteps Audio");
        go.transform.SetParent(transform, false);
        _source = go.AddComponent<AudioSource>();
        _source.playOnAwake = false;
        _source.loop = false;
        _source.spatialBlend = 0f;
        _source.priority = 32;
        _source.outputAudioMixerGroup = output;
    }

    void OnEnable() => _rhythm.Stepped += OnStepped;

    void OnDisable() => _rhythm.Stepped -= OnStepped;

    void OnStepped(float speed)
    {
        if (bank == null || speed <= _rhythm.runThreshold)
            return;

        FootstepSurfaceType type = FootstepSurface.Detect(transform.position, transform, bank.fallback);
        FootstepBank.Surface surface = bank.Get(type);
        AudioClip clip = bank.Pick(surface, true);
        if (clip == null)
            return;

        _source.pitch = Random.Range(pitch.x, pitch.y);
        _source.PlayOneShot(clip, runVolume * surface.volume * Random.Range(0.9f, 1f));
    }
}
