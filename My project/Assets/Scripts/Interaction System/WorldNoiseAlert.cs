using UnityEngine;

/// <summary>
/// Alerts NPCs to a loud one-shot at a world position (same path as body bag impact / grate open).
/// </summary>
public static class WorldNoiseAlert
{
    public static void Notify(Vector3 worldPosition, float footstepSuspicion = 300f)
    {
        if (footstepSuspicion <= 0f)
            return;

        foreach (var perception in Object.FindObjectsByType<PerceptionController>(FindObjectsSortMode.None))
        {
            if (perception != null)
                perception.HearLoudWorldNoise(worldPosition, footstepSuspicion);
        }
    }
}
