using UnityEngine;

/// <summary>
/// Shared listener-to-source occlusion test for all 3D world one-shots.
/// </summary>
public static class WorldSfxOcclusion
{
    public struct Result
    {
        public bool IsOccluded;
        public float VolumeMultiplier;
        public float LowpassCutoff;
    }

    public static Result Evaluate(
        Vector3 soundPosition,
        LayerMask occlusionLayers,
        float occludedVolumeMultiplier,
        float occludedLowpassCutoff,
        float clearLowpassCutoff,
        Transform ignoreRoot = null)
    {
        bool occluded = IsOccluded(soundPosition, occlusionLayers, ignoreRoot);
        return new Result
        {
            IsOccluded = occluded,
            VolumeMultiplier = occluded ? occludedVolumeMultiplier : 1f,
            LowpassCutoff = occluded ? occludedLowpassCutoff : clearLowpassCutoff
        };
    }

    static bool IsOccluded(Vector3 soundPosition, LayerMask occlusionLayers, Transform ignoreRoot)
    {
        Transform listener = GetListenerTransform();
        if (listener == null)
            return false;

        Vector3 origin = listener.position;
        Vector3 delta = soundPosition - origin;
        float distance = delta.magnitude;
        if (distance < 0.05f)
            return false;

        RaycastHit[] hits = Physics.RaycastAll(
            origin,
            delta / distance,
            distance,
            occlusionLayers,
            QueryTriggerInteraction.Ignore);

        if (hits == null || hits.Length == 0)
            return false;

        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

        foreach (RaycastHit hit in hits)
        {
            if (ShouldIgnoreHit(hit, ignoreRoot))
                continue;
            return true;
        }

        return false;
    }

    static bool ShouldIgnoreHit(RaycastHit hit, Transform ignoreRoot)
    {
        if (hit.collider == null)
            return true;

        if (ignoreRoot != null && hit.collider.transform.IsChildOf(ignoreRoot))
            return true;

        int layer = hit.collider.gameObject.layer;
        if (layer == LayerMask.NameToLayer("Player"))
            return true;
        if (layer == LayerMask.NameToLayer("NPC"))
            return true;
        if (layer == LayerMask.NameToLayer("Interactable "))
            return true;
        if (layer == LayerMask.NameToLayer("UI"))
            return true;
        if (layer == LayerMask.NameToLayer("Ignore Raycast"))
            return true;

        return false;
    }

    static Transform GetListenerTransform()
    {
        Camera main = Camera.main;
        if (main != null)
            return main.transform;

        AudioListener listener = Object.FindFirstObjectByType<AudioListener>();
        return listener != null ? listener.transform : null;
    }
}
