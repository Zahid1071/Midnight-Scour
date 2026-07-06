using System.Collections;
using UnityEngine;

public class HidingSpot : MonoBehaviour, IInteractable
{
    public bool isOccupied { get; private set; }

    [Header("Setup - Sides")]
    public Transform leftPivot;
    public Animator leftAnimator;

    public Transform rightPivot;
    public Animator rightAnimator;

    [Header("Settings")]
    [Tooltip("Camera crossfade duration when leaving a hide spot.")]
    public float blendDuration = 1.0f;
    [Tooltip("Seconds after Exit starts before the camera blends back to the player. Door animation keeps playing.")]
    [Min(0f)]
    public float exitCameraBlendStartTime = 0.35f;
    public float animationDelay = 0.2f;
    [Tooltip("Seconds after hide starts before the player moves to the hidden layer. Enter animation plays immediately.")]
    [Min(0f)]
    public float stealthLayerDelay = 1f;
    [Tooltip("Restore the player layer immediately when exit begins (before the exit animation finishes).")]
    public bool restoreLayerOnExitStart = true;
    [Tooltip("Seconds after exit starts before the player spotlight fades back in. Tune per hide spot.")]
    [Min(0f)]
    public float restoreLightInSeconds = 1.5f;

    [Header("Door SFX")]
    [SerializeField] AudioClip doorOpenSfx;
    [SerializeField] AudioClip doorCloseSfx;
    [SerializeField] float doorSfxVolume = 1f;
    [Tooltip("Seconds after Enter starts before playing the open SFX.")]
    [Min(0f)]
    [SerializeField] float doorOpenSfxDelay = 0.2f;
    [Tooltip("Seconds after Exit starts before playing the close SFX.")]
    [Min(0f)]
    [SerializeField] float doorCloseSfxDelay = 0.2f;

    private Transform activePivot;
    private Animator activeAnimator;

    private int hiddenLayer = 7;
    private const float MinDistanceForHiddenLayerMeters = 2f;
    private bool isToggling = false;

    private int originalLayerIndex;
    private GameObject playerRef;

    public Transform PivotForNPCSearchRoutine;

    [Header("NPC Search")]
    [Tooltip("NPC pauses here during room search but the player cannot hide. Uses the same pivot/POI as a normal hiding spot.")]
    public bool searchCheckpointOnly;

    public float GetSearchPickWeight()
    {
        var player = GameObject.FindWithTag("Player");
        var hider = player != null ? player.GetComponent<PlayerHider>() : null;
        return hider != null ? hider.GetSearchPickWeight(this) : 1f;
    }

    public float GetCaptureChance(float baseChance)
    {
        var hider = ResolveOccupyingHider();
        return hider != null ? hider.GetCaptureChance(this, baseChance) : baseChance;
    }

    public PlayerHider ResolveOccupyingHider()
    {
        if (!isOccupied || playerRef == null)
            return null;

        return playerRef.GetComponent<PlayerHider>();
    }

    public Transform GetSearchPivot() =>
        PivotForNPCSearchRoutine != null ? PivotForNPCSearchRoutine : transform;

    public float GetSearchWaitSeconds(NPCData data)
    {
        if (!searchCheckpointOnly)
            return 4f;

        Vector2 range = data != null ? data.searchPositionWaitRange : new Vector2(2f, 3f);
        return Random.Range(
            Mathf.Max(0f, range.x),
            Mathf.Max(range.x, range.y));
    }

    public Transform ResolveSearchLookTarget()
    {
        var tfs = GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < tfs.Length; i++)
        {
            if (tfs[i] != null && (tfs[i].name == "HidingPOI" || tfs[i].name == "hidingPOI"))
                return tfs[i];
        }

        return GetSearchPivot();
    }

    /// <summary>Cage ile aynı: NPC bu hiding spot collider merkezine LOS ile bakıyorsa true.</summary>
    private bool IsHidingSpotVisibleToAnyNPC()
    {
#if UNITY_2023_1_OR_NEWER
        var perceivers = Object.FindObjectsByType<PerceptionController>(FindObjectsSortMode.None);
#else
        var perceivers = Object.FindObjectsOfType<PerceptionController>();
#endif
        Vector3 spotCenter = transform.position + Vector3.up * 1.5f;
        Collider myCol = GetComponent<Collider>();
        if (myCol != null) spotCenter = myCol.bounds.center;

        foreach (var p in perceivers)
        {
            if (p == null) continue;

            Vector3 eye = p.transform.position + Vector3.up * 1.7f;
            Vector3 dir = (spotCenter - eye).normalized;
            float dist = Vector3.Distance(eye, spotCenter);

            if (dist > p.MaxViewDistance) continue;

            float angle = Vector3.Angle(p.transform.forward, dir);
            if (angle > p.MaxViewHalfAngle) continue;

            if (Physics.Raycast(eye, dir, out RaycastHit hit, p.MaxViewDistance, ~p.IgnoreLayer, QueryTriggerInteraction.Ignore))
            {
                if (hit.collider != null && hit.collider.GetComponentInParent<HidingSpot>() == this)
                    return true;
            }
        }

        return false;
    }

    /// <summary>
    /// PerceptionController.HasDirectLineOfSightToPlayer — layer 7/captive’tan bağımsız ham LOS (oyuncu collider).
    /// NPC oyuncuyu görebiliyorken hidden layer uygulanmaz.
    /// </summary>
    private bool IsAnyNPCHasDirectLineOfSightToPlayer()
    {
#if UNITY_2023_1_OR_NEWER
        var perceivers = Object.FindObjectsByType<PerceptionController>(FindObjectsSortMode.None);
#else
        var perceivers = Object.FindObjectsOfType<PerceptionController>();
#endif
        foreach (var p in perceivers)
        {
            if (p != null && p.HasDirectLineOfSightToPlayer())
                return true;
        }

        return false;
    }

    /// <summary>
    /// Checked only when entering a hide spot — blocks stealth if an NPC is on top of you.
    /// </summary>
    private bool IsAnyNPCTooCloseForHiddenLayer(Vector3 playerPos)
    {
#if UNITY_2023_1_OR_NEWER
        var perceivers = Object.FindObjectsByType<PerceptionController>(FindObjectsSortMode.None);
#else
        var perceivers = Object.FindObjectsOfType<PerceptionController>();
#endif
        foreach (var p in perceivers)
        {
            if (p == null) continue;
            if (Vector3.Distance(playerPos, p.transform.position) < MinDistanceForHiddenLayerMeters)
                return true;
        }

        return false;
    }

    /// <summary>Stealth is granted once at hide entry; it is not stripped when NPCs approach later.</summary>
    private bool CanGrantStealthAtEntry(GameObject player)
    {
        Vector3 playerPos = player.transform.position;
        return !IsHidingSpotVisibleToAnyNPC()
            && !IsAnyNPCHasDirectLineOfSightToPlayer()
            && !IsAnyNPCTooCloseForHiddenLayer(playerPos);
    }

    private void TryCaptureOnFailedHide(GameObject player)
    {
#if UNITY_2023_1_OR_NEWER
        var npcs = Object.FindObjectsByType<NPCController>(FindObjectsSortMode.None);
#else
        var npcs = Object.FindObjectsOfType<NPCController>();
#endif

        NPCController best = null;
        float bestDist = float.MaxValue;

        foreach (var npc in npcs)
        {
            if (npc == null || npc.currentState != NPCController.AIState.Chase) continue;

            float dist = Vector3.Distance(npc.transform.position, player.transform.position);
            if (dist > npc.captureDistance || dist >= bestDist) continue;

            var perception = npc.perception;
            if (perception != null && !perception.CanSeePlayer) continue;

            best = npc;
            bestDist = dist;
        }

        if (best == null)
        {
            foreach (var npc in npcs)
            {
                if (npc == null || npc.currentState != NPCController.AIState.Chase) continue;

                float dist = Vector3.Distance(npc.transform.position, player.transform.position);
                if (dist >= MinDistanceForHiddenLayerMeters || dist >= bestDist) continue;

                best = npc;
                bestDist = dist;
            }
        }

        if (best != null)
            best.fsm.SetState(NPCController.AIState.Capture);
    }

    private void AbortFailedHide(GameObject player, PlayerHider hider, bool captured)
    {
        HidingCameraSystem.Instance.SetActive(false);
        HidingCameraSystem.Instance.Detach();

        SetHideLighting(player, false);

        hider.SetVisuals(true);
        hider.SetHiddenState(false, null);
        hider.SetHidingEngaged(false);
        if (!captured)
            hider.EnableControls();

        player.layer = originalLayerIndex;
        playerRef = null;
        isOccupied = false;
        isToggling = false;
        activePivot = null;
        activeAnimator = null;
    }

    public bool Interact()
    {
        if (searchCheckpointOnly)
            return false;

        if (isToggling)
        {
            HudManager.Instance?.ShowSubtitle("Wait a moment.", 2f);
            return false;
        }

        GameObject player = GameObject.FindWithTag("Player");
        if (player == null) return false;

        PlayerHider hider = player.GetComponent<PlayerHider>();
        if (hider == null) return false;

        if (hider.isHidden)
        {
            if (hider.IsHidingIn(this))
            {
                RevealPlayer(player);
                return true;
            }

            HudManager.Instance?.ShowSubtitle("You are already hiding.", 2.5f);
            return false;
        }

        var hidingStamina = player.GetComponent<HidingStamina>();
        if (hidingStamina != null && !hidingStamina.CanEnterHide)
        {
            if (hidingStamina.IsOnCooldown)
                HudManager.Instance?.ShowSubtitle("I have to catch my breath first.", 2.5f);
            else
                HudManager.Instance?.ShowSubtitle("I am too exhausted to hide.", 2.5f);
            return false;
        }

        if (rightPivot == null)
        {
            activePivot = leftPivot;
            activeAnimator = leftAnimator;
        }
        else
        {
            float distToLeft = Vector3.Distance(player.transform.position, leftPivot.parent.parent.position);
            float distToRight = Vector3.Distance(player.transform.position, rightPivot.parent.parent.position);

            if (distToLeft < distToRight)
            {
                activePivot = leftPivot;
                activeAnimator = leftAnimator;
            }
            else
            {
                activePivot = rightPivot;
                activeAnimator = rightAnimator;
            }
        }

        StartCoroutine(EnterHidingRoutine(player, hider));
        return true;
    }

    private IEnumerator EnterHidingRoutine(GameObject player, PlayerHider hider)
    {
        isToggling = true;
        isOccupied = true;
        originalLayerIndex = player.layer;
        playerRef = player;

        hider.SetHidingEngaged(true);

        SetHideLighting(player, true);

        HidingCameraSystem.Instance.AttachToPivot(activePivot);
        HidingCameraSystem.Instance.SetActive(true);
        hider.DisableControlsOnly();

        if (activeAnimator != null)
        {
            activeAnimator.Play("Idle");
            activeAnimator.SetTrigger("Enter");
        }

        StartCoroutine(PlayDoorSfxAfterDelay(doorOpenSfx, doorOpenSfxDelay));

        if (stealthLayerDelay > 0f)
            yield return new WaitForSeconds(stealthLayerDelay);

        if (!CanGrantStealthAtEntry(player))
        {
            TryCaptureOnFailedHide(player);
            bool captured = PlayerControlGate.IsAcquired(PlayerControlGate.ReasonCaptured);
            AbortFailedHide(player, hider, captured);
            yield break;
        }

        player.layer = hiddenLayer;
        hider.SetVisuals(false);
        hider.SetHiddenState(true, this);

        isToggling = false;
    }

    public void RevealPlayer(GameObject player)
    {
        if (!isOccupied || isToggling) return;
        StartCoroutine(ExitHidingRoutine(player, forCapture: false));
    }

    /// <summary>Starts exit anim for capture; caller waits before jumpscare. Keeps hiding camera at pivot.</summary>
    public void RevealPlayerForCapture(GameObject player)
    {
        if (player == null) return;

        var hider = player.GetComponent<PlayerHider>();
        if (hider == null || !hider.IsHidingIn(this)) return;

        StartCoroutine(ExitHidingRoutine(player, forCapture: true));
    }

    private IEnumerator ExitHidingRoutine(GameObject player, bool forCapture)
    {
        isToggling = true;
        playerRef = null;

        PlayerHider hider = player.GetComponent<PlayerHider>();

        if (!forCapture)
            StartCoroutine(RestoreHideLightAfterSeconds(player, restoreLightInSeconds));

        if (!forCapture && restoreLayerOnExitStart)
        {
            player.layer = originalLayerIndex;
            if (hider != null)
            {
                hider.SetHiddenState(false, null);
                hider.SetVisuals(true);
            }
        }

        float elapsed = 0f;
        bool cameraHandoffStarted = false;

        if (activeAnimator != null)
        {
            float startOffset = 0f;
            AnimatorStateInfo stateInfo = activeAnimator.GetCurrentAnimatorStateInfo(0);

            if (stateInfo.IsName("Enter") && stateInfo.normalizedTime < 1.0f)
                startOffset = 1.0f - stateInfo.normalizedTime;

            activeAnimator.Play("Exit", 0, startOffset);
            yield return null;

            StartCoroutine(PlayDoorSfxAfterDelay(doorCloseSfx, doorCloseSfxDelay));

            AnimatorStateInfo exitState = activeAnimator.GetCurrentAnimatorStateInfo(0);
            float exitDuration = exitState.length / Mathf.Max(0.01f, Mathf.Abs(exitState.speed));

            while (elapsed < exitDuration)
            {
                elapsed += Time.deltaTime;

                if (!forCapture && !cameraHandoffStarted && elapsed >= exitCameraBlendStartTime)
                {
                    cameraHandoffStarted = true;
                    HidingCameraSystem.Instance.SetActive(false);
                }

                yield return null;
            }
        }
        else if (!forCapture)
        {
            yield return new WaitForSeconds(0.5f);
        }

        if (!forCapture && !restoreLayerOnExitStart)
        {
            if (hider != null)
            {
                hider.SetVisuals(true);
                hider.SetHiddenState(false, null);
            }

            player.layer = originalLayerIndex;
        }

        if (forCapture)
        {
            isOccupied = false;
            isToggling = false;
            yield break;
        }

        if (activeAnimator == null || !cameraHandoffStarted)
            HidingCameraSystem.Instance.SetActive(false);

        if (activeAnimator != null)
        {
            float blendElapsed = cameraHandoffStarted ? elapsed - exitCameraBlendStartTime : 0f;
            float blendRemaining = Mathf.Max(0f, blendDuration - blendElapsed);
            if (blendRemaining > 0f)
                yield return new WaitForSeconds(blendRemaining);
        }
        else if (blendDuration > 0f)
        {
            yield return new WaitForSeconds(blendDuration);
        }

        if (hider != null)
            hider.EnableControls();

        if (hider != null)
            hider.SetHidingEngaged(false);

        HidingCameraSystem.Instance.Detach();

        isOccupied = false;
        isToggling = false;
        activePivot = null;
        activeAnimator = null;
    }

    private IEnumerator RestoreHideLightAfterSeconds(GameObject player, float seconds)
    {
        if (seconds > 0f)
            yield return new WaitForSeconds(seconds);

        SetHideLighting(player, false);
    }

    public static void EndHidingCameraForCapture()
    {
        if (HidingCameraSystem.Instance == null) return;
        HidingCameraSystem.Instance.SetActive(false);
        HidingCameraSystem.Instance.Detach();
    }

    static void SetHideLighting(GameObject player, bool suppressed)
    {
        if (player == null)
            return;

        var dimmer = player.GetComponent<PlayerSpotlightWallDimmer>();
        if (dimmer != null)
            dimmer.SetHideLightSuppressed(suppressed);
    }

    public void PlayDoorOpenSfx(Vector3 worldPosition)
    {
        if (doorOpenSfx == null || SoundManager.Instance == null)
            return;

        SoundManager.Instance.PlaySFXAt(doorOpenSfx, worldPosition, doorSfxVolume);
    }

    public void PlayDoorCloseSfx(Vector3 worldPosition)
    {
        if (doorCloseSfx == null || SoundManager.Instance == null)
            return;

        SoundManager.Instance.PlaySFXAt(doorCloseSfx, worldPosition, doorSfxVolume);
    }

    IEnumerator PlayDoorSfxAfterDelay(AudioClip clip, float seconds)
    {
        if (clip == null || SoundManager.Instance == null)
            yield break;

        if (seconds > 0f)
            yield return new WaitForSeconds(seconds);

        Vector3 pos = activePivot != null ? activePivot.position : transform.position;
        SoundManager.Instance.PlaySFXAt(clip, pos, doorSfxVolume);
    }
}
