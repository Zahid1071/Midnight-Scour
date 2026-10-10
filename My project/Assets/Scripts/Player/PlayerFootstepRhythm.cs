using StarterAssets;
using UnityEngine;

[RequireComponent(typeof(CharacterController))]
public class PlayerFootstepRhythm : MonoBehaviour
{
    [Header("References")]
    public FootstepController footstepController;

    [Header("Step Distances (meters)")]
    public float walkStepDistance = 2.0f;
    public float runStepDistance = 1.29f;
    public float sneakStepDistance = 1.3f;

    [Header("Movement Thresholds")]
    public float runThreshold = 4.0f;
    public float walkThreshold = 2.0f;
    public float sneakThreshold = 0.1f;



    private CharacterController controller;
    private FirstPersonController fpsController;

    private Vector3 lastFramePosition;
    private float distanceAccumulator = 0f;
    private float lastStepDistance = -1f;
    private int stepsTaken;

    /// <summary>Footsteps played plus progress toward the next one; whole numbers land on a footstep.</summary>
    public float StepProgress => stepsTaken + (lastStepDistance > 0f ? distanceAccumulator / lastStepDistance : 0f);

    void Awake()
    {
        controller = GetComponent<CharacterController>();
    }

    void Start()
    {
        lastFramePosition = transform.position;
    }

    void Update()
    {
        HandleFootstepRhythm();
    }

    void HandleFootstepRhythm()
    {
        float speed = new Vector3(controller.velocity.x, 0, controller.velocity.z).magnitude;
        if (!controller.isGrounded || speed <= sneakThreshold)
        {
            lastFramePosition = transform.position;
            return;
        }

        float stepDistance = GetStepDistanceForSpeed(speed);
        // Keep the fraction of the current step when the stride changes, so the head bob doesn't jump.
        if (lastStepDistance > 0f && !Mathf.Approximately(stepDistance, lastStepDistance))
            distanceAccumulator *= stepDistance / lastStepDistance;
        lastStepDistance = stepDistance;
        float volume = GetVolumeForSpeed(speed);
        bool useRunClips = speed > runThreshold;

        // Accumulate true distance traveled, not net displacement
        float delta = Vector3.Distance(transform.position, lastFramePosition);
        distanceAccumulator += delta;
        lastFramePosition = transform.position;
        while (distanceAccumulator >= stepDistance)
        {
            footstepController.PlayFootstep(volume, useRunClips);

            float suspicionValue = 0f;
            if (speed > runThreshold) suspicionValue = 50;
            else if (speed > walkThreshold) suspicionValue = 20;

            if (suspicionValue > 0f)
                NotifyNPCsOfFootstep(suspicionValue);


            distanceAccumulator -= stepDistance; // keep remainder for smooth rhythm
            stepsTaken++;
        }
    }

    float GetStepDistanceForSpeed(float speed)
    {
        if (speed > runThreshold) return runStepDistance;
        if (speed > walkThreshold) return walkStepDistance;
        if (speed > sneakThreshold) return sneakStepDistance;
        return walkStepDistance; // fallback
    }

    float GetVolumeForSpeed(float speed)
    {
        if (speed > runThreshold) return footstepController.runVolume;
        if (speed > walkThreshold) return footstepController.walkVolume;
        if (speed > sneakThreshold) return footstepController.sneakVolume;
        return footstepController.walkVolume; // fallback
    }



    [SerializeField] float maxHearingDistance = PerceptionController.DefaultMaxHearingDistance;
    [SerializeField] float minHearingDistance = PerceptionController.DefaultMinHearingDistance;

    void NotifyNPCsOfFootstep(float suspicionAmount)
    {
        foreach (var npc in Object.FindObjectsByType<PerceptionController>(FindObjectsSortMode.None))
        {
            float addValue = suspicionAmount * PerceptionController.DistanceHearingMultiplier(
                transform.position,
                npc.transform.position,
                maxHearingDistance,
                minHearingDistance);

            if (addValue <= 0f)
                continue;

            npc.HearPlayerFootstep(transform, addValue);
        }
    }

}
