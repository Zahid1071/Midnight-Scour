using UnityEngine;

/// <summary>
/// Add to the same object as Rig Builder. Assign skeleton bones (Multi-Aim Constrained Object).
/// Runs after rig solve and limits how many degrees each bone may rotate per second.
/// </summary>
[DefaultExecutionOrder(100)]
public class BoneRotationSpeedCap : MonoBehaviour
{
    [Header("Small-angle slow down")]
    [Tooltip("When the remaining rotation is at/below this angle, rotation is slowed the most.")]
    public float smallAngleThresholdDegrees = 10f;

    [Tooltip("At very small angles, rotate this many times slower than maxDegreesPerSecond.")]
    public float smallAngleSlowFactor = 10f;

    [System.Serializable]
    public struct Entry
    {
        public Transform bone;
        public float maxDegreesPerSecond;
    }

    public Entry[] bones;

    Quaternion[] _last;

    void Start()
    {
        _last = new Quaternion[bones.Length];
        for (int i = 0; i < bones.Length; i++)
        {
            if (bones[i].bone != null)
                _last[i] = bones[i].bone.rotation;
        }
    }

    void LateUpdate()
    {
        float threshold = Mathf.Max(0.001f, smallAngleThresholdDegrees);
        float slowFactor = Mathf.Max(1f, smallAngleSlowFactor);

        for (int i = 0; i < bones.Length; i++)
        {
            Transform t = bones[i].bone;
            if (t == null) continue;

            float remainingAngle = Quaternion.Angle(_last[i], t.rotation);
            float a = Mathf.Clamp01(remainingAngle / threshold);
            float speedScale = Mathf.Lerp(1f / slowFactor, 1f, a);

            float maxStep = bones[i].maxDegreesPerSecond * speedScale * Time.deltaTime;
            _last[i] = Quaternion.RotateTowards(_last[i], t.rotation, maxStep);
            t.rotation = _last[i];
        }
    }
}
