using UnityEngine;
using System;

public class SuspicionSystem : MonoBehaviour
{
    // runtime meters
    public float suspicionLevel { get; private set; }
    public float footstepSuspicion { get; private set; }

    // cached config
    float suspicionThreshold, suspicionDecay;
    float footstepThreshold, footstepDecay;

    public event Action OnSuspicionThresholdReached;

    void Awake()
    {
        var data = GetComponent<InteractionController>()?.data;
        if (data == null)
        {
            Debug.LogWarning($"{name}: NPCData missing for SuspicionSystem. Using defaults.");
            suspicionThreshold = 100f; suspicionDecay = 5f;
            footstepThreshold  = 100f; footstepDecay = 10f;
            return;
        }

        suspicionThreshold = data.suspicion.baseThreshold;
        suspicionDecay     = data.suspicion.baseDecayPerSecond;
        footstepThreshold  = data.suspicion.footstepThreshold;
        footstepDecay      = data.suspicion.footstepDecayPerSecond;
    }

    void Update()
    {

        if (suspicionLevel > 0f)
        {
            suspicionLevel -= suspicionDecay * Time.deltaTime;
            if (suspicionLevel < 0f) suspicionLevel = 0f;
        }

        HandleFootstepSuspicion();
    }

    public void AddFootstepSuspicion(float amount)
    {
        //Debug.Log(footstepSuspicion);
        footstepSuspicion += amount;
    }

    void HandleFootstepSuspicion()
    {
        if (footstepSuspicion > 0f)
        {
            footstepSuspicion -= footstepDecay * Time.deltaTime;
            if (footstepSuspicion < 0f) footstepSuspicion = 0f;
        }

        if (footstepSuspicion >= footstepThreshold)
        {
            suspicionLevel += footstepSuspicion;

            if (suspicionLevel >= suspicionThreshold)
            {
                suspicionLevel = suspicionThreshold;
                OnSuspicionThresholdReached?.Invoke();
            }

            footstepSuspicion = 0f;
        }
    }
}
