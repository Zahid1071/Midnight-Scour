using UnityEngine;

/// <summary>
/// Spawns a dropped world item with physics: ensures collider + rigidbody, then applies a light throw impulse.
/// </summary>
public static class ItemDropHelper
{
    public static GameObject SpawnAndThrow(
        GameObject prefab,
        Vector3 position,
        Quaternion rotation,
        Vector3 throwDirection,
        float throwForce,
        float upwardForce,
        float mass = 1f,
        AudioClip landClipOverride = null)
    {
        GameObject instance = Object.Instantiate(prefab, position, rotation);
        Rigidbody rb = PreparePhysics(instance, mass, landClipOverride);

        Vector3 dir = throwDirection.sqrMagnitude > 0.0001f ? throwDirection.normalized : Vector3.forward;
        Vector3 impulse = dir * throwForce + Vector3.up * upwardForce;
        rb.AddForce(impulse, ForceMode.Impulse);

        return instance;
    }

    /// <summary>Spawns a dropped world item at rest at the given pose (e.g. a PlacementPreview's), without a throw.</summary>
    public static GameObject SpawnPlaced(
        GameObject prefab,
        Vector3 position,
        Quaternion rotation,
        float mass = 1f,
        AudioClip landClipOverride = null)
    {
        GameObject instance = Object.Instantiate(prefab, position, rotation);
        PreparePhysics(instance, mass, landClipOverride);
        return instance;
    }

    private static Rigidbody PreparePhysics(GameObject root, float mass, AudioClip landClipOverride)
    {
        EnsureColliders(root);

        Rigidbody rb = root.GetComponent<Rigidbody>();
        if (rb == null)
            rb = root.AddComponent<Rigidbody>();

        rb.mass = mass;
        rb.isKinematic = false;
        rb.useGravity = true;
        rb.collisionDetectionMode = CollisionDetectionMode.Continuous;

        EnsureDropFeedback(root, landClipOverride);

        return rb;
    }

    private static void EnsureDropFeedback(GameObject root, AudioClip landClipOverride)
    {
        if (root == null) return;

        var fragile = root.GetComponent<FragileDropReplaceOnImpact>();
        if (fragile != null)
        {
            AudioClip clip = landClipOverride != null
                ? landClipOverride
                : (SoundManager.Instance != null ? SoundManager.Instance.itemDropLandClip : null);
            fragile.Init(null, clip);
            return;
        }

        EnsureLandSfx(root, landClipOverride);
    }

    private static void EnsureLandSfx(GameObject root, AudioClip landClipOverride)
    {
        if (root == null) return;

        var sfx = root.GetComponent<DroppedItemLandSfx>();
        if (sfx == null)
            sfx = root.AddComponent<DroppedItemLandSfx>();

        AudioClip clip = landClipOverride != null ? landClipOverride : (SoundManager.Instance != null ? SoundManager.Instance.itemDropLandClip : null);
        sfx.Init(clip);
    }

    private static void EnsureColliders(GameObject root)
    {
        Collider[] colliders = root.GetComponentsInChildren<Collider>();
        if (colliders.Length > 0)
        {
            foreach (Collider c in colliders)
                c.enabled = true;
            return;
        }

        if (TryAddMeshCollider(root))
            return;

        root.AddComponent<BoxCollider>();
    }

    private static bool TryAddMeshCollider(GameObject root)
    {
        MeshFilter meshFilter = root.GetComponent<MeshFilter>();
        if (meshFilter == null || meshFilter.sharedMesh == null)
            return false;

        var meshCollider = root.AddComponent<MeshCollider>();
        meshCollider.sharedMesh = meshFilter.sharedMesh;
        meshCollider.convex = true;
        return true;
    }
}
