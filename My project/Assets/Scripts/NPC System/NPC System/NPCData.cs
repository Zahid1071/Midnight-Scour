using UnityEngine;
using System.Collections.Generic;

[System.Serializable]
public struct VisionSettings
{
    [Min(0f)] public float maxViewDistance;          // meters
    [Range(0f, 180f)] public float maxViewAngle;     // degrees (half-angle)
    public LayerMask ignoreLayer;

    [Tooltip("Seconds of continuous line-of-sight required before the NPC starts chasing.")]
    [Min(0f)] public float confirmSeconds;

    [Tooltip("Vision meter fill per second while seeing the player.")]
    [Min(0f)] public float buildRate;

    [Tooltip("Vision meter drain per second when not seeing.")]
    [Min(0f)] public float decayRate;
}

[System.Serializable]
public struct HearingSettings
{
    [Tooltip("While ambushing, scale down suspicion gained from audio.")]
    [Range(0.1f, 1f)] public float ambushSuspicionMultiplier;
}

[System.Serializable]
public struct SuspicionSettings
{
    [Header("General")]
    [Min(0f)] public float baseThreshold;
    [Min(0f)] public float baseDecayPerSecond;

    [Header("Footsteps")]
    [Min(0f)] public float footstepThreshold;
    [Min(0f)] public float footstepDecayPerSecond;
}

[System.Serializable]
public struct LocomotionSettings
{
    [Min(0f)] public float walkingSpeed; // NavMeshAgent speed for walking
    [Min(0f)] public float runningSpeed; // NavMeshAgent speed for running
}

[System.Serializable]
public struct ChaseRetentionSettings
{
    [Tooltip("Keep chasing this long after losing LoS (seconds).")]
    [Min(0f)] public float gracePeriod;

    [Tooltip("If nearer than this (meters), keep chasing even with no LoS.")]
    [Min(0f)] public float keepChasingDistance;
}

[System.Serializable]
public struct POILookSettings
{
    [Header("Natural")]
    [Min(0.01f)] public float naturalPoiIntervalSeconds;
    [Range(0f, 1f)] public float naturalLookHoldChance;
}

[CreateAssetMenu(fileName = "NPCData", menuName = "NPC/NPC Data")]
public class NPCData : ScriptableObject
{
    public string npcName;
    public GameObject npcPrefab;

    [RoutineID(RoutineIDAttribute.IDType.Zone)]
    public string homeID;
    

    [Header("Routine / Search")]
    [Range(0f, 1f)] public float searchChance = 0.3f; // chance to actually search a hiding spot
    public List<WeightedZonePlan> weightedRoutinePlans;

    [Header("Patrol / Ambush")]
    [Range(0f, 1f)] public float patrolChance = 0.5f;
    public float patrolDuration = 10f;
    [Range(0f, 1f)] public float ambushChance = 0.2f;
    public Vector2 ambushWaitRange = new Vector2(5f, 8f);

    [Header("Audio")]
    public float footstepVolume = .5f;

    [Header("Perception Profiles")]
    public VisionSettings vision = new VisionSettings {
        maxViewDistance = 10f,
        maxViewAngle    = 90f,
        confirmSeconds  = 1.5f,
        buildRate       = 1.0f,
        decayRate       = 1.5f
    };

    public HearingSettings hearing = new HearingSettings {
        ambushSuspicionMultiplier = 0.5f
    };

    [Header("Suspicion Profiles")]
    public SuspicionSettings suspicion = new SuspicionSettings {
        baseThreshold          = 100f,
        baseDecayPerSecond     = 5f,
        footstepThreshold      = 100f,
        footstepDecayPerSecond = 10f
    };

    [Header("Movement")]
    public LocomotionSettings locomotion = new LocomotionSettings {
        walkingSpeed = 2.5f,
        runningSpeed = 6f
    };

    [Header("Chase Retention")]
    public ChaseRetentionSettings chaseRetention = new ChaseRetentionSettings {
        gracePeriod         = 2f,
        keepChasingDistance = 3f
    };

    [Header("POI Look")]
    public POILookSettings poiLook = new POILookSettings {
        naturalPoiIntervalSeconds = 5f,
        naturalLookHoldChance     = 0.30f
    };
}
