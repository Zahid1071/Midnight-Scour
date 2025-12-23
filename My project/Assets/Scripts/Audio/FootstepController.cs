using UnityEngine;

[RequireComponent(typeof(AudioSource))]
public class FootstepController : MonoBehaviour
{
    [Header("Footstep Audio")]
    public AudioSource footstepSource;           // Assign in Inspector (or auto-get)
    public AudioClip[] leftFootClips;            // Drag your left foot SFX here
    public AudioClip[] rightFootClips;           // Drag your right foot SFX here

    [Header("Mixer/FX (Optional)")]
    public UnityEngine.Audio.AudioMixerGroup mixerGroup; // Assign your AudioMixer group (e.g. "Footsteps")
    public float reverbSendLevel = 1f;                 // Optional for special FX

    [Header("Volumes")]
    public float walkVolume = 1.0f;
    public float runVolume = 1.3f;
    public float sneakVolume = 0.4f;

    private bool isLeftFoot = true;                    // Alternates per step

    void Awake()
    {
        if (!footstepSource) footstepSource = GetComponent<AudioSource>();
        if (mixerGroup) footstepSource.outputAudioMixerGroup = mixerGroup;
        footstepSource.spatialBlend = 1.0f; // 3D sound!
    }

    public void PlayFootstep(float volume = 1f)
    {
        AudioClip clip = null;

        if (isLeftFoot && leftFootClips.Length > 0)
            clip = leftFootClips[Random.Range(0, leftFootClips.Length)];
        else if (!isLeftFoot && rightFootClips.Length > 0)
            clip = rightFootClips[Random.Range(0, rightFootClips.Length)];

        if (!clip) return;

        footstepSource.clip = clip;
        footstepSource.volume = Mathf.Clamp01(volume * Random.Range(0.96f, 1.04f));
        footstepSource.pitch = Random.Range(0.97f, 1.03f);

        footstepSource.Play();

        isLeftFoot = !isLeftFoot;
    }
}
