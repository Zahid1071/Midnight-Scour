using UnityEngine;

/// <summary>
/// Add to the same object as Rig Builder. Assign skeleton bones (Multi-Aim Constrained Object).
/// Runs after rig solve and limits how many degrees each bone may rotate per second.
/// </summary>
[DefaultExecutionOrder(100)]
public class BoneRotationSpeedCap : MonoBehaviour
{
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
        for (int i = 0; i < bones.Length; i++)
        {
            Transform t = bones[i].bone;
            if (t == null) continue;

            float maxStep = bones[i].maxDegreesPerSecond * Time.deltaTime;
            _last[i] = Quaternion.RotateTowards(_last[i], t.rotation, maxStep);
            t.rotation = _last[i];
        }
    }
}
