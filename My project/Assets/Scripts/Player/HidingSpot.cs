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
    public float blendDuration = 1.0f;
    public float animationDelay = 0.2f;

    private Transform activePivot;
    private Animator activeAnimator;

    private int hiddenLayer = 7;
    private const float MinDistanceForHiddenLayerMeters = 2f;
    private bool isToggling = false;

    private int originalLayerIndex;
    private GameObject playerRef;

    public Transform PivotForNPCSearchRoutine;

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

        hider.SetVisuals(true);
        hider.SetHiddenState(false, null);
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

        HidingCameraSystem.Instance.AttachToPivot(activePivot);

        if (activeAnimator != null) activeAnimator.Play("Idle");

        HidingCameraSystem.Instance.SetActive(true);
        hider.DisableControlsOnly();

        yield return new WaitForSeconds(blendDuration);

        playerRef = player;

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

        yield return new WaitForSeconds(animationDelay);

        if (activeAnimator != null) activeAnimator.SetTrigger("Enter");

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

        if (activeAnimator != null)
        {
            float startOffset = 0f;
            AnimatorStateInfo stateInfo = activeAnimator.GetCurrentAnimatorStateInfo(0);

            if (stateInfo.IsName("Enter"))
            {
                if (stateInfo.normalizedTime < 1.0f)
                    startOffset = 1.0f - stateInfo.normalizedTime;
            }

            activeAnimator.Play("Exit", 0, startOffset);

            yield return null;

            if (!forCapture)
            {
                yield return new WaitUntil(() =>
                    activeAnimator.GetCurrentAnimatorStateInfo(0).normalizedTime >= 1.0f);
            }
        }
        else if (!forCapture)
        {
            yield return new WaitForSeconds(0.5f);
        }

        PlayerHider hider = player.GetComponent<PlayerHider>();
        if (hider != null)
        {
            hider.SetVisuals(true);
            hider.SetHiddenState(false, null);
        }
        player.layer = originalLayerIndex;

        if (forCapture)
        {
            // Keep hiding camera at pivot until capture/jumpscare finishes cleanup.
            isOccupied = false;
            isToggling = false;
            yield break;
        }

        HidingCameraSystem.Instance.SetActive(false);

        yield return new WaitForSeconds(blendDuration);

        if (hider != null) hider.EnableControls();
        HidingCameraSystem.Instance.Detach();

        isOccupied = false;
        isToggling = false;

        activePivot = null;
        activeAnimator = null;
    }

    public static void EndHidingCameraForCapture()
    {
        if (HidingCameraSystem.Instance == null) return;
        HidingCameraSystem.Instance.SetActive(false);
        HidingCameraSystem.Instance.Detach();
    }
}
