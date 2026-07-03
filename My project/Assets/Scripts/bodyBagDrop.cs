using UnityEngine;

public class BodyBagDrop : MonoBehaviour
{

    [Header("Rig References")]
    public Transform topAnchorBone;
    public Transform bottomBone;

    [Header("Drop Thresholds")]
    [Tooltip("How far (in meters) the bottom must swing horizontally to break.")]
    public float breakDistanceThreshold = 1.0f;
    
    [Header("Audio")]
    public AudioClip snapSound;
    public AudioClip impactSound;
    public float impactDelay = 1.2f;

    [Header("NPC Perception")]
    [Tooltip("Footstep suspicion added per NPC on impact — keep high so footstep + base thresholds trip like a max footstep hit.")]
    [Min(1f)]
    public float npcSuspicionFromImpact = 300f;

    bool hasDropped;
    bool lastTouchWasPlayer;
    Rigidbody topRb;
    Rigidbody bottomRb;

    void Start()
    {
        if (topAnchorBone != null)
        {
            topRb = topAnchorBone.GetComponent<Rigidbody>();
            BodyBagDropCollisionRelay.Attach(this, topAnchorBone);
        }

        if (bottomBone != null)
        {
            bottomRb = bottomBone.GetComponent<Rigidbody>();
            BodyBagDropCollisionRelay.Attach(this, bottomBone);
        }
    }

    void Update()
    {
        if (hasDropped || topAnchorBone == null || bottomBone == null)
            return;

        Vector2 topPos = new Vector2(topAnchorBone.position.x, topAnchorBone.position.z);
        Vector2 bottomPos = new Vector2(bottomBone.position.x, bottomBone.position.z);

        if (Vector2.Distance(topPos, bottomPos) > breakDistanceThreshold && lastTouchWasPlayer)
            DropBag();
    }

    internal void RegisterCollision(Collider other)
    {
        if (other == null || hasDropped)
            return;

        if (IsPlayerCollider(other))
            lastTouchWasPlayer = true;
        else if (IsNPCCollider(other))
            lastTouchWasPlayer = false;
    }

    static bool IsPlayerCollider(Collider other)
    {
        if (other.CompareTag("Player"))
            return true;

        return other.GetComponentInParent<PlayerInteractor>() != null
            || other.transform.root.CompareTag("Player");
    }

    static bool IsNPCCollider(Collider other)
    {
        return other.GetComponentInParent<NPCController>() != null
            || other.GetComponentInParent<PerceptionController>() != null;
    }

    void DropBag()
    {
        hasDropped = true;

        if (topRb != null)
        {
            topRb.isKinematic = false;
            topRb.constraints = RigidbodyConstraints.None;
        }

        if (snapSound != null && SoundManager.Instance != null)
            SoundManager.Instance.PlaySFXAt(snapSound, topAnchorBone.position);

        Invoke(nameof(PlayImpactSound), impactDelay);
    }

    void PlayImpactSound()
    {
        Vector3 impactPos = bottomBone != null ? bottomBone.position : transform.position;

        if (impactSound != null && SoundManager.Instance != null)
            SoundManager.Instance.PlaySFXAt(impactSound, impactPos);

        foreach (var perception in Object.FindObjectsByType<PerceptionController>(FindObjectsSortMode.None))
            perception.HearLoudWorldNoise(impactPos, npcSuspicionFromImpact);
    }
}

[DisallowMultipleComponent]
public class BodyBagDropCollisionRelay : MonoBehaviour
{
    BodyBagDrop _drop;

    public static void Attach(BodyBagDrop drop, Transform bone)
    {
        if (drop == null || bone == null)
            return;

        var relay = bone.GetComponent<BodyBagDropCollisionRelay>();
        if (relay == null)
            relay = bone.gameObject.AddComponent<BodyBagDropCollisionRelay>();

        relay._drop = drop;
    }

    void OnCollisionEnter(Collision collision)
    {
        if (collision != null)
            _drop?.RegisterCollision(collision.collider);
    }
}
