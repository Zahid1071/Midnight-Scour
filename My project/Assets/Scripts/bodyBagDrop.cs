using UnityEngine;

public class BodyBagDrop : MonoBehaviour
{
    [Header("Rig References")]
    public Transform topAnchorBone;
    public Transform bottomBone;

    [Header("Drop Thresholds")]
    [Tooltip("How far (in meters) the bottom must swing horizontally to break.")]
    public float breakDistanceThreshold = 1.0f;
    [Tooltip("How fast the bottom bone must be moving to break from a hard hit.")]
    public float breakVelocityThreshold = 4.0f; 
    
    [Header("Audio")]
    public AudioClip snapSound;
    public AudioClip impactSound;
    public float impactDelay = 1.2f;

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

        // Check 2: Was it hit too hard? 
        // (If using a Unity version older than 6, change .linearVelocity to .velocity)
        if (bottomRb != null && bottomRb.linearVelocity.magnitude > breakVelocityThreshold)
        {
            DropBag();
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
        if (impactSound != null && SoundManager.Instance != null)
        {
            SoundManager.Instance.PlaySFXAt(impactSound, bottomBone.position);
        }
    }
}