using UnityEngine;

/// <summary>
/// Replaces exploration music while the player is inside this trigger (e.g. farmhouse bed).
/// Uses <see cref="SoundManager.SetZoneMusicActive"/> so chase music still wins.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(BoxCollider))]
public class LocationMusicZone : MonoBehaviour
{
    [SerializeField] AudioClip music;
    [SerializeField] BoxCollider zone;
    [Tooltip("World-space box used if the collider has not been sized yet.")]
    [SerializeField] Vector3 worldCenter;
    [SerializeField] Vector3 worldSize = new Vector3(20f, 8f, 20f);
    [SerializeField] bool applyWorldBoxOnAwake = true;

    void Awake()
    {
        if (zone == null)
            zone = GetComponent<BoxCollider>();
        zone.isTrigger = true;

        if (applyWorldBoxOnAwake && worldSize.sqrMagnitude > 0.01f)
            ApplyWorldBox(worldCenter, worldSize);

        SoundManager.EnsureClipReady(music);
    }

    public void ApplyWorldBox(Vector3 center, Vector3 size)
    {
        worldCenter = center;
        worldSize = size;
        if (zone == null)
            zone = GetComponent<BoxCollider>();
        zone.isTrigger = true;
        zone.center = transform.InverseTransformPoint(center);
        Vector3 lossy = transform.lossyScale;
        zone.size = new Vector3(
            size.x / Mathf.Max(0.001f, Mathf.Abs(lossy.x)),
            size.y / Mathf.Max(0.001f, Mathf.Abs(lossy.y)),
            size.z / Mathf.Max(0.001f, Mathf.Abs(lossy.z)));
    }

    void OnTriggerEnter(Collider other)
    {
        if (!IsPlayer(other) || music == null || SoundManager.Instance == null)
            return;
        SoundManager.EnsureClipReady(music);
        SoundManager.Instance.SetZoneMusicActive(true, music);
    }

    void OnTriggerExit(Collider other)
    {
        if (!IsPlayer(other) || SoundManager.Instance == null)
            return;
        if (SoundManager.Instance.IsZoneMusicActive)
            SoundManager.Instance.SetZoneMusicActive(false, null, restoreGameplay: true);
    }

    void OnDisable()
    {
        if (SoundManager.Instance != null && SoundManager.Instance.IsZoneMusicActive)
            SoundManager.Instance.SetZoneMusicActive(false, null, restoreGameplay: true);
    }

    static bool IsPlayer(Collider other)
    {
        if (other == null)
            return false;
        if (other.CompareTag("Player"))
            return true;
        Transform t = other.attachedRigidbody != null ? other.attachedRigidbody.transform : other.transform;
        if (t.CompareTag("Player"))
            return true;
        return t.root != null && t.root.CompareTag("Player");
    }
}
