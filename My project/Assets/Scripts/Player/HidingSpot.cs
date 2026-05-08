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
    private bool hideLayerEvaluationActive;

    public Transform PivotForNPCSearchRoutine;

    private void Update()
    {
        if (!isOccupied || playerRef == null || !hideLayerEvaluationActive) return;

        int targetLayer = originalLayerIndex;
        if (!IsHidingSpotVisibleToAnyNPC()
            && !IsAnyNPCHasDirectLineOfSightToPlayer()
            && !IsAnyNPCTooCloseForHiddenLayer())
            targetLayer = hiddenLayer;

        if (playerRef.layer != targetLayer)
            playerRef.layer = targetLayer;
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
    /// Any NPC within this distance keeps the player on the normal layer (even with no LOS).
    /// </summary>
    private bool IsAnyNPCTooCloseForHiddenLayer()
    {
        if (playerRef == null) return true;
#if UNITY_2023_1_OR_NEWER
        var perceivers = Object.FindObjectsByType<PerceptionController>(FindObjectsSortMode.None);
#else
        var perceivers = Object.FindObjectsOfType<PerceptionController>();
#endif
        Vector3 playerPos = playerRef.transform.position;
        foreach (var p in perceivers)
        {
            if (p == null) continue;
            if (Vector3.Distance(playerPos, p.transform.position) < MinDistanceForHiddenLayerMeters)
                return true;
        }

        return false;
    }

    public void Interact()
    {
        if (isToggling)
        {
            HudManager.Instance?.ShowSubtitle("Wait a moment.", 2f);
            return;
        }

        GameObject player = GameObject.FindWithTag("Player");
        if (player == null) return;

        PlayerHider hider = player.GetComponent<PlayerHider>();
        if (hider == null) return;

        if (hider.isHidden)
        {
            HudManager.Instance?.ShowSubtitle("You are already hiding.", 2.5f);
            return;
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
    }

    private IEnumerator EnterHidingRoutine(GameObject player, PlayerHider hider)
    {
        isToggling = true;
        isOccupied = true;
        originalLayerIndex = player.layer;
        hideLayerEvaluationActive = false;

        HidingCameraSystem.Instance.AttachToPivot(activePivot);

        if (activeAnimator != null) activeAnimator.Play("Idle");

        HidingCameraSystem.Instance.SetActive(true);
        hider.DisableControlsOnly();

        yield return new WaitForSeconds(blendDuration);

        hider.SetVisuals(false);
        hider.SetHiddenState(true, this);
        playerRef = player;
        hideLayerEvaluationActive = true;

        yield return new WaitForSeconds(animationDelay);

        if (activeAnimator != null) activeAnimator.SetTrigger("Enter");

        isToggling = false;
    }

    public void RevealPlayer(GameObject player)
    {
        if (!isOccupied || isToggling) return;
        StartCoroutine(ExitHidingRoutine(player));
    }

    private IEnumerator ExitHidingRoutine(GameObject player)
    {
        isToggling = true;
        hideLayerEvaluationActive = false;
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

            yield return new WaitUntil(() =>
                activeAnimator.GetCurrentAnimatorStateInfo(0).normalizedTime >= 1.0f);
        }
        else
        {
            yield return new WaitForSeconds(0.5f);
        }

        HidingCameraSystem.Instance.SetActive(false);

        PlayerHider hider = player.GetComponent<PlayerHider>();
        if (hider != null)
        {
            hider.SetVisuals(true);
            hider.SetHiddenState(false, null);
        }
        player.layer = originalLayerIndex;

        yield return new WaitForSeconds(blendDuration);

        if (hider != null) hider.EnableControls();
        HidingCameraSystem.Instance.Detach();

        isOccupied = false;
        isToggling = false;

        activePivot = null;
        activeAnimator = null;
    }
}
