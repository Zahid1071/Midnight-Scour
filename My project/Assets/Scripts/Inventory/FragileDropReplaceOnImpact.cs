using UnityEngine;

/// <summary>
/// On a hard landing, destroys this dropped object and spawns a replacement prefab (e.g. broken vase shards).
/// Add to the item's world/drop prefab and assign <see cref="brokenPrefab"/>.
/// </summary>
[DisallowMultipleComponent]
public sealed class FragileDropReplaceOnImpact : MonoBehaviour
{
    [SerializeField] private GameObject brokenPrefab;
    [SerializeField] private GameObject contentPrefab;
    [SerializeField] private float minImpactSpeed = 2f;
    [SerializeField] private float breakSfxVolume = 1f;

    [Header("Broken debris")]
    [SerializeField] private float debrisDisappearMinSeconds = 2f;
    [SerializeField] private float debrisDisappearMaxSeconds = 3f;

    [Header("NPC reaction")]
    [Min(0f)]
    [SerializeField] private float npcSuspicionFromBreak = 300f;

    private AudioClip _breakClip;
    private bool _replaced;

    public void Init(GameObject replacePrefab, AudioClip breakClip = null)
    {
        if (replacePrefab != null)
            brokenPrefab = replacePrefab;
        if (breakClip != null)
            _breakClip = breakClip;
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (_replaced || brokenPrefab == null)
            return;

        if (collision.collider != null && collision.collider.CompareTag("Player"))
            return;

        float speed = collision.relativeVelocity.magnitude;
        if (speed < minImpactSpeed)
            return;

        _replaced = true;

        Vector3 pos = transform.position;
        Quaternion rot = transform.rotation;
        Vector3 impactPoint = collision.contactCount > 0 ? collision.GetContact(0).point : pos;

        if (_breakClip != null && SoundManager.Instance != null)
            SoundManager.Instance.PlaySFXAt(_breakClip, impactPoint, breakSfxVolume);

        GameObject debris = Instantiate(brokenPrefab, pos, rot);
        GameObject content = Instantiate(contentPrefab, pos, rot);

        var settle = debris.GetComponent<BrokenDebrisSettle>();
        if (settle == null)
            settle = debris.AddComponent<BrokenDebrisSettle>();
        settle.Init(debrisDisappearMinSeconds, debrisDisappearMaxSeconds);

        if (npcSuspicionFromBreak > 0f)
        {
            foreach (var perception in Object.FindObjectsByType<PerceptionController>(FindObjectsSortMode.None))
                perception.HearLoudWorldNoise(impactPoint, npcSuspicionFromBreak);
        }

        Destroy(gameObject);
    }
}
