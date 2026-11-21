using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Mixamo-bone ragdoll for the crawl scare body. Starts kinematic; <see cref="Activate"/>
/// enables physics (call when the crawl clip ends).
/// </summary>
[DisallowMultipleComponent]
public class CrawlScareRagdoll : MonoBehaviour
{
    [SerializeField] Transform bodyRoot;
    [SerializeField] float totalMass = 60f;
    [SerializeField] float crawlCarryVelocity = 1.35f;
    [SerializeField] Vector3 crawlVelocityLocal = new Vector3(0f, 0.15f, 1f);

    readonly List<Rigidbody> _bodies = new List<Rigidbody>();
    readonly List<Collider> _colliders = new List<Collider>();
    bool _built;
    bool _active;

    public bool IsActive => _active;
    public bool IsBuilt => _built;

    public void SetBodyRoot(Transform root) => bodyRoot = root;

    public void EnsureBuilt()
    {
        if (_built && _bodies.Count > 0)
            return;
        Build();
    }

    [ContextMenu("Build Ragdoll")]
    public void Build()
    {
        ClearBuilt();

        if (bodyRoot == null)
            bodyRoot = transform;

        Transform hips = FindBone("Hips");
        Transform spine2 = FindBone("Spine2") ?? FindBone("Spine1") ?? FindBone("Spine");
        Transform head = FindBone("Head");
        Transform leftUpLeg = FindBone("LeftUpLeg");
        Transform leftLeg = FindBone("LeftLeg");
        Transform rightUpLeg = FindBone("RightUpLeg");
        Transform rightLeg = FindBone("RightLeg");
        Transform leftArm = FindBone("LeftArm");
        Transform leftForeArm = FindBone("LeftForeArm");
        Transform rightArm = FindBone("RightArm");
        Transform rightForeArm = FindBone("RightForeArm");

        if (hips == null)
        {
            Debug.LogError("CrawlScareRagdoll: no Hips bone under " + bodyRoot.name, this);
            return;
        }

        // Mass distribution roughly humanoid.
        Rigidbody hipsRb = AddPart(hips, null, 0.22f * totalMass, PartShape.CapsuleAlongChild, 0.14f);
        Rigidbody chestRb = spine2 != null ? AddPart(spine2, hipsRb, 0.18f * totalMass, PartShape.CapsuleAlongChild, 0.13f) : hipsRb;
        if (head != null)
            AddPart(head, chestRb, 0.08f * totalMass, PartShape.Sphere, 0.11f);

        if (leftUpLeg != null)
        {
            Rigidbody thigh = AddPart(leftUpLeg, hipsRb, 0.10f * totalMass, PartShape.CapsuleAlongChild, 0.09f);
            if (leftLeg != null)
                AddPart(leftLeg, thigh, 0.06f * totalMass, PartShape.CapsuleAlongChild, 0.07f);
        }
        if (rightUpLeg != null)
        {
            Rigidbody thigh = AddPart(rightUpLeg, hipsRb, 0.10f * totalMass, PartShape.CapsuleAlongChild, 0.09f);
            if (rightLeg != null)
                AddPart(rightLeg, thigh, 0.06f * totalMass, PartShape.CapsuleAlongChild, 0.07f);
        }
        if (leftArm != null)
        {
            Rigidbody upper = AddPart(leftArm, chestRb, 0.05f * totalMass, PartShape.CapsuleAlongChild, 0.055f);
            if (leftForeArm != null)
                AddPart(leftForeArm, upper, 0.035f * totalMass, PartShape.CapsuleAlongChild, 0.045f);
        }
        if (rightArm != null)
        {
            Rigidbody upper = AddPart(rightArm, chestRb, 0.05f * totalMass, PartShape.CapsuleAlongChild, 0.055f);
            if (rightForeArm != null)
                AddPart(rightForeArm, upper, 0.035f * totalMass, PartShape.CapsuleAlongChild, 0.045f);
        }

        IgnoreSelfCollisions();
        IgnorePlayerCollisions();
        SetPhysicsActive(false);
        _built = true;
    }

    public void Activate()
    {
        EnsureBuilt();
        if (_active || _bodies.Count == 0)
            return;

        _active = true;
        // Player may spawn after Build in edit/play — refresh ignore pairs on activate.
        IgnorePlayerCollisions();
        SetPhysicsActive(true);

        Vector3 worldVel = transform.TransformDirection(crawlVelocityLocal.normalized) * crawlCarryVelocity;
        for (int i = 0; i < _bodies.Count; i++)
        {
            Rigidbody rb = _bodies[i];
            if (rb == null)
                continue;
            rb.linearVelocity = worldVel;
            rb.angularVelocity = Random.insideUnitSphere * 0.75f;
        }
    }

    void SetPhysicsActive(bool on)
    {
        for (int i = 0; i < _bodies.Count; i++)
        {
            Rigidbody rb = _bodies[i];
            if (rb == null)
                continue;
            rb.isKinematic = !on;
            rb.detectCollisions = on;
            if (on)
                rb.WakeUp();
        }

        for (int i = 0; i < _colliders.Count; i++)
        {
            if (_colliders[i] != null)
                _colliders[i].enabled = true;
        }
    }

    void ClearBuilt()
    {
        // Remove previously generated joints/bodies/colliders we own on bones.
        for (int i = 0; i < _bodies.Count; i++)
        {
            Rigidbody rb = _bodies[i];
            if (rb == null)
                continue;
            var joints = rb.GetComponents<CharacterJoint>();
            for (int j = 0; j < joints.Length; j++)
                DestroyImmediateSafe(joints[j]);
            var cols = rb.GetComponents<Collider>();
            for (int j = 0; j < cols.Length; j++)
                DestroyImmediateSafe(cols[j]);
            DestroyImmediateSafe(rb);
        }
        _bodies.Clear();
        _colliders.Clear();
        _built = false;
        _active = false;
    }

    static void DestroyImmediateSafe(Object obj)
    {
        if (obj == null)
            return;
        if (Application.isPlaying)
            Object.Destroy(obj);
        else
            Object.DestroyImmediate(obj);
    }

    enum PartShape { CapsuleAlongChild, Sphere }

    Rigidbody AddPart(Transform bone, Rigidbody connectedTo, float mass, PartShape shape, float radius)
    {
        Rigidbody rb = bone.GetComponent<Rigidbody>();
        if (rb == null)
            rb = bone.gameObject.AddComponent<Rigidbody>();
        rb.mass = Mathf.Max(0.1f, mass);
        rb.linearDamping = 0.15f;
        rb.angularDamping = 0.6f;
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        rb.isKinematic = true;
        rb.detectCollisions = false;
        _bodies.Add(rb);

        // Fresh collider
        foreach (var existing in bone.GetComponents<Collider>())
            DestroyImmediateSafe(existing);

        if (shape == PartShape.Sphere)
        {
            var sphere = bone.gameObject.AddComponent<SphereCollider>();
            sphere.radius = radius;
            sphere.center = Vector3.zero;
            _colliders.Add(sphere);
        }
        else
        {
            Transform child = FirstJointChild(bone);
            float length = child != null ? Mathf.Max(0.08f, Vector3.Distance(bone.position, child.position)) : radius * 2.5f;
            var capsule = bone.gameObject.AddComponent<CapsuleCollider>();
            capsule.radius = radius;
            capsule.height = Mathf.Max(radius * 2.2f, length);
            capsule.direction = 1; // Y-axis in local bone space (Mixamo bones aim along local Y)
            if (child != null)
            {
                Vector3 localChild = bone.InverseTransformPoint(child.position);
                capsule.center = localChild * 0.5f;
                capsule.direction = DominantAxis(localChild);
                capsule.height = Mathf.Max(radius * 2.2f, localChild.magnitude);
            }
            else
            {
                capsule.center = new Vector3(0f, length * 0.5f, 0f);
            }
            _colliders.Add(capsule);
        }

        if (connectedTo != null)
        {
            foreach (var old in bone.GetComponents<CharacterJoint>())
                DestroyImmediateSafe(old);

            var joint = bone.gameObject.AddComponent<CharacterJoint>();
            joint.connectedBody = connectedTo;
            joint.enableProjection = true;
            joint.enablePreprocessing = false;
            SoftLimits(joint);
        }

        return rb;
    }

    static void SoftLimits(CharacterJoint joint)
    {
        SoftJointLimit low = new SoftJointLimit { limit = -45f };
        SoftJointLimit high = new SoftJointLimit { limit = 45f };
        SoftJointLimitSpring spring = new SoftJointLimitSpring { spring = 0f, damper = 0f };
        joint.lowTwistLimit = low;
        joint.highTwistLimit = high;
        joint.swing1Limit = new SoftJointLimit { limit = 45f };
        joint.swing2Limit = new SoftJointLimit { limit = 25f };
        joint.twistLimitSpring = spring;
        joint.swingLimitSpring = spring;
        joint.axis = Vector3.right;
        joint.swingAxis = Vector3.up;
    }

    static int DominantAxis(Vector3 local)
    {
        Vector3 a = new Vector3(Mathf.Abs(local.x), Mathf.Abs(local.y), Mathf.Abs(local.z));
        if (a.x >= a.y && a.x >= a.z) return 0;
        if (a.z >= a.y && a.z >= a.x) return 2;
        return 1;
    }

    static Transform FirstJointChild(Transform bone)
    {
        for (int i = 0; i < bone.childCount; i++)
        {
            Transform c = bone.GetChild(i);
            // Prefer Mixamo deform bones over helpers.
            if (c.name.Contains("End") || c.name.Contains("WGT"))
                continue;
            return c;
        }
        return bone.childCount > 0 ? bone.GetChild(0) : null;
    }

    Transform FindBone(string suffix)
    {
        if (bodyRoot == null)
            return null;
        Transform[] all = bodyRoot.GetComponentsInChildren<Transform>(true);
        Transform best = null;
        for (int i = 0; i < all.Length; i++)
        {
            string n = all[i].name;
            if (n == suffix || n.EndsWith(":" + suffix) || n.EndsWith("_" + suffix))
            {
                // Prefer exact Mixamo deform bone (mixamorig5:Head over HeadTop_End).
                if (n.EndsWith(":" + suffix))
                    return all[i];
                best = all[i];
            }
        }
        return best;
    }

    void IgnoreSelfCollisions()
    {
        for (int i = 0; i < _colliders.Count; i++)
        {
            for (int j = i + 1; j < _colliders.Count; j++)
            {
                if (_colliders[i] != null && _colliders[j] != null)
                    Physics.IgnoreCollision(_colliders[i], _colliders[j], true);
            }
        }
    }

    /// <summary>Player walks through the corpse; ragdoll still collides with the world.</summary>
    void IgnorePlayerCollisions()
    {
        Collider[] playerCols = FindPlayerColliders();
        if (playerCols == null || playerCols.Length == 0)
            return;

        for (int i = 0; i < _colliders.Count; i++)
        {
            Collider ragCol = _colliders[i];
            if (ragCol == null)
                continue;
            for (int j = 0; j < playerCols.Length; j++)
            {
                if (playerCols[j] != null)
                    Physics.IgnoreCollision(ragCol, playerCols[j], true);
            }
        }
    }

    static Collider[] FindPlayerColliders()
    {
        GameObject player = GameObject.FindGameObjectWithTag("Player");
        if (player == null)
            return null;
        return player.GetComponentsInChildren<Collider>(true);
    }
}
