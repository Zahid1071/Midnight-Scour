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
        float mass = 1f)
    {
        GameObject instance = Object.Instantiate(prefab, position, rotation);
        Rigidbody rb = PreparePhysics(instance, mass);

        Vector3 dir = throwDirection.sqrMagnitude > 0.0001f ? throwDirection.normalized : Vector3.forward;
        Vector3 impulse = dir * throwForce + Vector3.up * upwardForce;
        rb.AddForce(impulse, ForceMode.Impulse);

        return instance;
    }

    private static Rigidbody PreparePhysics(GameObject root, float mass)
    {
        EnsureColliders(root);

        Rigidbody rb = root.GetComponent<Rigidbody>();
        if (rb == null)
            rb = root.AddComponent<Rigidbody>();

        rb.mass = mass;
        rb.isKinematic = false;
        rb.useGravity = true;
        rb.collisionDetectionMode = CollisionDetectionMode.Continuous;

        return rb;
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
