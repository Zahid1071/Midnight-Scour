using UnityEngine;

[DisallowMultipleComponent]
public sealed class DroppedItemLandSfx : MonoBehaviour
{
    [SerializeField] private AudioClip landClip;
    [SerializeField] private float minImpactSpeed = 1.2f;
    [SerializeField] private float cooldownSeconds = 0.2f;
    [SerializeField] private float volume = 1f;

    private float _lastPlayTime = -999f;
    private bool _played;

    public void Init(AudioClip clip)
    {
        landClip = clip;
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (_played) return;
        if (landClip == null || SoundManager.Instance == null) return;

        // Ignore player collisions so dropping near your feet doesn't instantly "land" on you.
        if (collision.collider != null && collision.collider.CompareTag("Player"))
            return;

        float speed = collision.relativeVelocity.magnitude;
        if (speed < minImpactSpeed)
            return;

        float now = Time.time;
        if (now - _lastPlayTime < cooldownSeconds)
            return;

        _lastPlayTime = now;
        _played = true;
        SoundManager.Instance.PlaySFXAt(landClip, collision.contacts.Length > 0 ? collision.contacts[0].point : transform.position, volume);
    }
}
