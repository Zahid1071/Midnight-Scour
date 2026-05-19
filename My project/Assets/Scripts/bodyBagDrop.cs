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

    private bool hasDropped = false;
    private Rigidbody topRb;
    private Rigidbody bottomRb;

    void Start()
    {
        if (topAnchorBone != null) topRb = topAnchorBone.GetComponent<Rigidbody>();
        if (bottomBone != null) bottomRb = bottomBone.GetComponent<Rigidbody>();
    }

    void Update()
    {

        if (hasDropped || topAnchorBone == null || bottomBone == null) return;

        // Check 1: Did it swing too far?
        Vector2 topPos = new Vector2(topAnchorBone.position.x, topAnchorBone.position.z);
        Vector2 bottomPos = new Vector2(bottomBone.position.x, bottomBone.position.z);

        if (Vector2.Distance(topPos, bottomPos) > breakDistanceThreshold)
        {
            DropBag();
            return;
        }
    }

    private void DropBag()
    {
        hasDropped = true;

        if (topRb != null)
        {
            topRb.isKinematic = false;
            // Unlocks the XYZ axes so gravity can actually pull it down
            topRb.constraints = RigidbodyConstraints.None; 
        }

        if (snapSound != null && SoundManager.Instance != null)
        {
            SoundManager.Instance.PlaySFXAt(snapSound, topAnchorBone.position);
        }

        Invoke(nameof(PlayImpactSound), impactDelay);
    }

    private void PlayImpactSound()
    {
        Vector3 impactPos = bottomBone != null ? bottomBone.position : transform.position;

        if (impactSound != null && SoundManager.Instance != null)
            SoundManager.Instance.PlaySFXAt(impactSound, impactPos);

        foreach (var perception in Object.FindObjectsByType<PerceptionController>(FindObjectsSortMode.None))
            perception.HearLoudWorldNoise(impactPos, npcSuspicionFromImpact);
    }
}