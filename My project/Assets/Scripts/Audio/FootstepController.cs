using UnityEngine;

[RequireComponent(typeof(AudioSource))]
public class FootstepController : MonoBehaviour
{
    [Header("Footstep Audio")]
    public AudioSource footstepSource;
    public AudioClip[] leftFootClips;
    public AudioClip[] rightFootClips;

    [Header("Mixer/FX (Optional)")]
    public UnityEngine.Audio.AudioMixerGroup mixerGroup;
    public float reverbSendLevel = 1f;

    [Header("Volumes")]
    public float walkVolume = 1.0f;
    public float runVolume = 1.3f;
    public float sneakVolume = 0.4f;

    [Header("Occlusion (walls / objects between listener and feet)")]
    [Tooltip("Layers that can block sound. Hits on Player/NPC/Interactable are ignored.")]
    [SerializeField] private LayerMask occlusionLayers = ~0;
    [SerializeField] private float occludedVolumeMultiplier = 0.4f;
    [SerializeField] private float occludedLowpassCutoff = 1400f;
    [SerializeField] private float clearLowpassCutoff = 22000f;
    [Tooltip("Optional. Uses AudioListener or main camera if unset.")]
    [SerializeField] private Transform listenerTransform;

    AudioLowPassFilter _lowPassFilter;
    Transform _soundOrigin;
    bool isLeftFoot = true;

    void Awake()
    {
        if (!footstepSource)
            footstepSource = GetComponent<AudioSource>();

        _soundOrigin = footstepSource.transform;
        if (mixerGroup)
            footstepSource.outputAudioMixerGroup = mixerGroup;

        footstepSource.spatialBlend = 1f;

        _lowPassFilter = footstepSource.GetComponent<AudioLowPassFilter>();
        if (_lowPassFilter == null)
            _lowPassFilter = footstepSource.gameObject.AddComponent<AudioLowPassFilter>();

        _lowPassFilter.enabled = false;
        _lowPassFilter.cutoffFrequency = clearLowpassCutoff;
    }

    public void PlayFootstep(float volume = 1f)
    {
        AudioClip clip = null;

        if (isLeftFoot && leftFootClips.Length > 0)
            clip = leftFootClips[Random.Range(0, leftFootClips.Length)];
        else if (!isLeftFoot && rightFootClips.Length > 0)
            clip = rightFootClips[Random.Range(0, rightFootClips.Length)];

        if (!clip)
            return;

        bool occluded = IsSoundOccluded();
        float volumeScale = Random.Range(0.96f, 1.04f) * (occluded ? occludedVolumeMultiplier : 1f);

        ApplyOcclusionFilter(occluded);

        footstepSource.clip = clip;
        footstepSource.volume = Mathf.Clamp01(volume * volumeScale);
        footstepSource.pitch = Random.Range(0.97f, 1.03f);
        footstepSource.Play();

        isLeftFoot = !isLeftFoot;
    }

    bool IsSoundOccluded()
    {
        Transform listener = GetListenerTransform();
        if (listener == null || _soundOrigin == null)
            return false;

        Vector3 origin = listener.position;
        Vector3 target = _soundOrigin.position;
        Vector3 delta = target - origin;
        float distance = delta.magnitude;
        if (distance < 0.05f)
            return false;

        RaycastHit[] hits = Physics.RaycastAll(
            origin,
            delta / distance,
            distance,
            occlusionLayers,
            QueryTriggerInteraction.Ignore);

        if (hits == null || hits.Length == 0)
            return false;

        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

        foreach (RaycastHit hit in hits)
        {
            if (ShouldIgnoreOcclusionHit(hit))
                continue;
            return true;
        }

        return false;
    }

    bool ShouldIgnoreOcclusionHit(RaycastHit hit)
    {
        if (hit.collider == null)
            return true;

        Transform hitTransform = hit.collider.transform;
        if (hitTransform.IsChildOf(transform.root))
            return true;

        int layer = hit.collider.gameObject.layer;
        if (layer == LayerMask.NameToLayer("Player"))
            return true;
        if (layer == LayerMask.NameToLayer("NPC"))
            return true;
        if (layer == LayerMask.NameToLayer("Interactable "))
            return true;
        if (layer == LayerMask.NameToLayer("UI"))
            return true;
        if (layer == LayerMask.NameToLayer("Ignore Raycast"))
            return true;

        return false;
    }

    void ApplyOcclusionFilter(bool occluded)
    {
        if (_lowPassFilter == null)
            return;

        _lowPassFilter.enabled = occluded;
        _lowPassFilter.cutoffFrequency = occluded ? occludedLowpassCutoff : clearLowpassCutoff;
    }

    Transform GetListenerTransform()
    {
        if (listenerTransform != null)
            return listenerTransform;

        Camera main = Camera.main;
        if (main != null)
            return main.transform;

        AudioListener listener = Object.FindFirstObjectByType<AudioListener>();
        return listener != null ? listener.transform : null;
    }
}
