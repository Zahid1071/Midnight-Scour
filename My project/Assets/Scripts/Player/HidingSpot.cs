using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class HidingSpot : MonoBehaviour, IInteractable
{
    public bool isOccupied { get; private set; }

    public enum UsableSide { Both, LeftOnly, RightOnly }

    [Header("Setup - Sides")]
    public Transform leftPivot;
    public Animator leftAnimator;

    public Transform rightPivot;
    public Animator rightAnimator;

    [Tooltip("Restrict which side of this hiding spot the player can enter from. Use this when one side is blocked/faces a wall and shouldn't be usable.")]
    public UsableSide usableSide = UsableSide.Both;

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
    [Tooltip("Seconds after hide starts before the player spotlight starts fading out. Tune per hide spot.")]
    [Min(0f)]
    public float suppressLightInSeconds = 0f;
    [Tooltip("Seconds after exit starts before the player spotlight fades back in. Tune per hide spot.")]
    [Min(0f)]
    public float restoreLightInSeconds = 1.5f;
    [Tooltip("Seconds to ease player XZ to the hide pivot when leaving. Runs under the hide cam so it is not visible. Y unchanged.")]
    [Min(0.05f)]
    [SerializeField] float exitSlideDuration = 0.35f;

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

    readonly List<Collider> _suppressedSolids = new();
    readonly List<bool> _suppressedSolidWasEnabled = new();

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

    /// <summary>
    /// Player finished entering while an NPC could see it — send that NPC to this spot to capture
    /// (guaranteed, like a successful search hide-spot roll), instead of ejecting the player.
    /// </summary>
    private void AlertNpcsOfWitnessedHide(GameObject player)
    {
#if UNITY_2023_1_OR_NEWER
        var npcs = Object.FindObjectsByType<NPCController>(FindObjectsSortMode.None);
#else
        var npcs = Object.FindObjectsOfType<NPCController>();
#endif

        NPCController best = null;
        float bestScore = float.MaxValue;
        Vector3 spotPos = GetSearchPivot() != null ? GetSearchPivot().position : transform.position;

        foreach (var npc in npcs)
        {
            if (npc == null)
                continue;
            if (npc.currentState == NPCController.AIState.Capture
                || npc.currentState == NPCController.AIState.Dead
                || npc.currentState == NPCController.AIState.Scripted)
                continue;

            float dist = Vector3.Distance(npc.transform.position, spotPos);
            var perception = npc.perception;

            bool chasing = npc.currentState == NPCController.AIState.Chase;
            bool hasLos = perception != null && perception.HasDirectLineOfSightToPlayer();
            bool close = dist < MinDistanceForHiddenLayerMeters;
            bool inViewRange = perception != null && dist <= perception.MaxViewDistance;

            if (!chasing && !hasLos && !close && !inViewRange)
                continue;

            // Prefer chasing / LOS witnesses, then closer NPCs.
            float score = dist;
            if (chasing) score -= 1000f;
            if (hasLos) score -= 500f;
            if (close) score -= 250f;

            if (score < bestScore)
            {
                best = npc;
                bestScore = score;
            }
        }

        if (best == null)
        {
            // Fallback: nearest eligible NPC so a witnessed enter never leaves the player stranded safe.
            foreach (var npc in npcs)
            {
                if (npc == null)
                    continue;
                if (npc.currentState == NPCController.AIState.Capture
                    || npc.currentState == NPCController.AIState.Dead
                    || npc.currentState == NPCController.AIState.Scripted)
                    continue;

                float dist = Vector3.Distance(npc.transform.position, spotPos);
                if (dist < bestScore)
                {
                    best = npc;
                    bestScore = dist;
                }
            }
        }

        if (best == null)
            return;

        if (best.perception != null)
            best.perception.RedirectLastKnownPosition(spotPos);

        CaptureContext.Set(CaptureReason.WitnessedHide);
        best.BeginWitnessedHideCapture(this);
    }

    public bool Interact()
    {
        if (searchCheckpointOnly)
            return false;

        if (isToggling)
        {
            HudManager.Instance?.ShowSubtitle(Loc.Get("gameplay.wait_moment"), 2f);
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

            HudManager.Instance?.ShowSubtitle(Loc.Get("gameplay.already_hiding"), 2.5f);
            return false;
        }

        if (!TrySelectHideSide(player.transform.position, out activePivot, out activeAnimator))
            return false;

        StartCoroutine(EnterHidingRoutine(player, hider));
        return true;
    }

    static Vector3 GetSideAnchor(Transform pivot)
    {
        if (pivot == null)
            return Vector3.zero;

        if (pivot.parent != null && pivot.parent.parent != null)
            return pivot.parent.parent.position;

        return pivot.position;
    }

    bool TrySelectHideSide(Vector3 playerPos, out Transform pivot, out Animator animator)
    {
        pivot = null;
        animator = null;

        switch (usableSide)
        {
            case UsableSide.LeftOnly:
                if (leftPivot == null)
                    return false;

                if (rightPivot != null && IsCloserToRightSide(playerPos))
                    return false;

                pivot = leftPivot;
                animator = leftAnimator;
                return true;

            case UsableSide.RightOnly:
                if (rightPivot == null)
                    return false;

                if (leftPivot != null && !IsCloserToRightSide(playerPos))
                    return false;

                pivot = rightPivot;
                animator = rightAnimator;
                return true;

            default:
                if (rightPivot == null)
                {
                    pivot = leftPivot;
                    animator = leftAnimator;
                }
                else if (leftPivot == null)
                {
                    pivot = rightPivot;
                    animator = rightAnimator;
                }
                else if (IsCloserToRightSide(playerPos))
                {
                    pivot = rightPivot;
                    animator = rightAnimator;
                }
                else
                {
                    pivot = leftPivot;
                    animator = leftAnimator;
                }

                return pivot != null;
        }
    }

    bool IsCloserToRightSide(Vector3 playerPos)
    {
        float distToLeft = Vector3.Distance(playerPos, GetSideAnchor(leftPivot));
        float distToRight = Vector3.Distance(playerPos, GetSideAnchor(rightPivot));
        return distToRight < distToLeft;
    }

    private IEnumerator EnterHidingRoutine(GameObject player, PlayerHider hider)
    {
        isToggling = true;
        isOccupied = true;
        originalLayerIndex = player.layer;
        playerRef = player;

        hider.SetHidingEngaged(true);

        if (suppressLightInSeconds <= 0f)
            SetHideLighting(player, true);
        else
            StartCoroutine(SuppressHideLightAfterSeconds(player, suppressLightInSeconds));

        HidingCameraSystem.Instance.AttachToPivot(activePivot);
        HidingCameraSystem.Instance.SetActive(true);
        hider.DisableControlsOnly();

        // One frame after hide-cam kickoff so CC disable does not hitch with the blend.
        yield return null;
        hider.SuspendCollision();
        SuppressSolidColliders();

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
            // Alert while the player is still on the visible layer so LOS scoring stays valid,
            // then finish the hide — do not eject. Butcher paths here and captures like a
            // successful search hide-spot roll.
            AlertNpcsOfWitnessedHide(player);

            player.layer = hiddenLayer;
            hider.SetVisuals(false);
            hider.SetHiddenState(true, this);
            isToggling = false;
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

        // Body stays out of physics until after the XZ slide. Spot solids stay off until
        // controls return so a closed door cannot shove the first Move through the floor.
        if (hider != null)
            hider.SuspendCollision();

        if (hider != null && activePivot != null)
        {
            Vector3 standPos = GetSideAnchor(activePivot);
            float slide = forCapture ? 0.05f : exitSlideDuration;
            yield return hider.SmoothMoveToXZ(standPos.x, standPos.z, slide);
        }

        if (!forCapture)
            StartCoroutine(RestoreHideLightAfterSeconds(player, restoreLightInSeconds));

        // Always clear the stealth layer/hidden flag as soon as the exit begins - even when
        // exiting for a capture. Previously this was skipped for forCapture, so if the
        // downstream jumpscare/capture sequence ever failed or threw before it could reload
        // the scene, the player was left permanently on the hidden layer with isHidden stuck
        // true, making every NPC blind to them for the rest of the session.
        if (forCapture || restoreLayerOnExitStart)
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
            // Capture path used to leave the hide cam live until DoCaptureNow — if anything
            // aborted before that, the player stayed stuck in the locker lens with no UI.
            if (HidingCameraSystem.Instance != null)
            {
                HidingCameraSystem.Instance.SetActive(false);
                HidingCameraSystem.Instance.Detach();
            }

            // Keep CC off for jumpscare/reload, but restore spot solids and clear engaged
            // so a failed capture cannot leave the world in a broken collision state.
            RestoreSolidColliders();
            if (hider != null)
                hider.SetHidingEngaged(false);
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

        // Wake the body at Pivot Root while spot solids are still off, then bring solids back.
        if (hider != null)
        {
            hider.RestoreCollision();
            hider.EnableControls();
            hider.SetHidingEngaged(false);
        }

        RestoreSolidColliders();

        HidingCameraSystem.Instance.Detach();

        isOccupied = false;
        isToggling = false;
        activePivot = null;
        activeAnimator = null;
    }

    void SuppressSolidColliders()
    {
        RestoreSolidColliders();

        var cols = GetComponentsInChildren<Collider>(true);
        for (int i = 0; i < cols.Length; i++)
        {
            Collider col = cols[i];
            if (col == null || col.isTrigger)
                continue;

            _suppressedSolids.Add(col);
            _suppressedSolidWasEnabled.Add(col.enabled);
            col.enabled = false;
        }
    }

    void RestoreSolidColliders()
    {
        for (int i = 0; i < _suppressedSolids.Count; i++)
        {
            Collider col = _suppressedSolids[i];
            if (col != null)
                col.enabled = _suppressedSolidWasEnabled[i];
        }

        _suppressedSolids.Clear();
        _suppressedSolidWasEnabled.Clear();
    }

    private IEnumerator SuppressHideLightAfterSeconds(GameObject player, float seconds)
    {
        if (seconds > 0f)
            yield return new WaitForSeconds(seconds);
        if (isOccupied && playerRef == player)
            SetHideLighting(player, true);
    }

    private IEnumerator RestoreHideLightAfterSeconds(GameObject player, float seconds)
    {
        if (seconds > 0f)
            yield return new WaitForSeconds(seconds);

        SetHideLighting(player, false);
    }

    public static void EndHidingCameraForCapture()
    {
        if (HidingCameraSystem.Instance != null)
        {
            HidingCameraSystem.Instance.SetActive(false);
            HidingCameraSystem.Instance.Detach();
        }

        // Hide peek / locker aim must not leave the gameplay camera tilted after capture.
        ResetGameplayLook();
    }

    /// <summary>Clear peek + mouse-look pitch so Continue / post-capture camera is upright.</summary>
    public static void ResetGameplayLook()
    {
        var player = GameObject.FindGameObjectWithTag("Player");
        if (player == null)
            return;

        var look = player.GetComponentInChildren<CinemachineMouseLook>(true);
        look?.ResetLocalRotation();
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
