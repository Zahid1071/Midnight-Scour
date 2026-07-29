using UnityEngine;

/// <summary>
/// Animation-event targets for the jumpscare axe swing.
/// Function names must match the clip's Animation Events exactly.
/// </summary>
public class JumpscareSfxRelay : MonoBehaviour
{
    [Header("Clips")]
    [SerializeField] AudioClip swingSfx;
    [SerializeField] AudioClip slashSfx;
    [SerializeField] AudioClip gruntSfx;
    [SerializeField] AudioClip sitSfx;

    [Header("Volumes")]
    [SerializeField, Range(0f, 1f)] float swingVolume = 1f;
    [SerializeField, Range(0f, 1f)] float slashVolume = 1f;
    [SerializeField, Range(0f, 1f)] float gruntVolume = 1f;
    [SerializeField, Range(0f, 1f)] float sitVolume = 1f;

    Transform followRoot;

    void Awake()
    {
        var npc = GetComponentInParent<NPCController>();
        followRoot = npc != null ? npc.transform : transform;
    }

    public void AE_Swing() => Play(swingSfx, swingVolume);

    /// <summary>
    /// Impact frame: slash SFX + FOV punch + short camera shake.
    /// </summary>
    public void AE_Slash()
    {
        Play(slashSfx, slashVolume);
        JumpscareManager.Instance?.PlayImpactPunch();
    }

    public void AE_Grunt() => Play(gruntSfx, gruntVolume);

    /// <summary>Sitting-down impact (bed/sofa). Function name must match the clip event.</summary>
    public void AE_Sit() => Play(sitSfx, sitVolume);

    void Play(AudioClip clip, float volume)
    {
        if (clip == null || SoundManager.Instance == null)
            return;

        SoundManager.Instance.PlaySFXFollowing(clip, followRoot, volume);
    }
}
