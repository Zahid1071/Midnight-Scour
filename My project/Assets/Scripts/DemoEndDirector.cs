using System.Collections;
using FS_Atmo;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Demo finale: when the player picks up the basement key, the butcher stops reacting to
/// noise, abandons routine, moves to a door ambush point, and waits for line-of-sight
/// before chasing into the existing capture / jumpscare / menu flow.
/// </summary>
public class DemoEndDirector : MonoBehaviour
{
    [Header("References")]
    [SerializeField] NPCController butcher;
    [SerializeField] Transform doorAmbushPoint;
    [SerializeField] InventoryItem triggerKey;

    [Header("Movement")]
    [SerializeField] float arriveDistance = 0.4f;
    [SerializeField] float alignRotationDuration = 0.4f;

    bool _started;
    Coroutine _ambushRoutine;

    public void NotifyItemPickedUp(InventoryItem item)
    {
        if (item == null || triggerKey == null || item != triggerKey)
            return;

        BeginDoorAmbush();
    }

    public void BeginDoorAmbush()
    {
        if (_started || butcher == null || doorAmbushPoint == null)
            return;

        _started = true;
        butcher.SetHearingDisabled(true);

        if (_ambushRoutine != null)
            StopCoroutine(_ambushRoutine);

        _ambushRoutine = StartCoroutine(DoorAmbushRoutine());
    }

    IEnumerator DoorAmbushRoutine()
    {
        butcher.interaction?.CancelInteraction();
        butcher.GetComponent<Navigator>()?.ClearRoute();
        butcher.poiLookDirector?.CancelHoldAndClearLook();
        butcher.fsm.SetState(NPCController.AIState.Scripted);

        var nav = butcher.GetComponent<Navigator>();
        var agent = butcher.GetComponent<NavMeshAgent>();
        if (agent == null)
            yield break;

        agent.isStopped = false;
        agent.speed = butcher.runSpeed;
        agent.acceleration = Mathf.Max(agent.acceleration, butcher.runSpeed * 4f);

        if (nav != null)
            nav.GoToPoint(doorAmbushPoint.position);
        else
            agent.SetDestination(doorAmbushPoint.position);

        while (agent.pathPending || agent.remainingDistance > arriveDistance)
            yield return null;

        agent.isStopped = true;
        agent.ResetPath();
        agent.velocity = Vector3.zero;

        yield return AlignToAmbushRotation(agent.transform);

        butcher.poiLookDirector?.LockLookToPlayer();
        yield return WaitForPlayerSight();
        _ambushRoutine = null;
    }

    IEnumerator AlignToAmbushRotation(Transform npcRoot)
    {
        if (npcRoot == null || doorAmbushPoint == null)
            yield break;

        Quaternion startRot = npcRoot.rotation;
        Quaternion targetRot = doorAmbushPoint.rotation;
        float duration = Mathf.Max(0.01f, alignRotationDuration);
        float t = 0f;

        while (t < duration)
        {
            t += Time.deltaTime;
            npcRoot.rotation = Quaternion.Slerp(startRot, targetRot, t / duration);
            yield return null;
        }

        npcRoot.rotation = targetRot;
    }

    IEnumerator WaitForPlayerSight()
    {
        var perception = butcher.perception;
        if (perception == null)
            yield break;

        while (butcher.currentState == NPCController.AIState.Scripted)
        {
            butcher.poiLookDirector?.TickLockedPlayerLook();

            if (IsPlayerVisible(perception))
            {
                butcher.BeginChaseFromPerception();
                yield break;
            }

            yield return null;
        }
    }

    static bool IsPlayerVisible(PerceptionController perception)
    {
        if (perception == null || !perception.CanSeePlayer)
            return false;

        var player = perception.PlayerTransform;
        if (player == null)
            return false;

        if (player.gameObject.layer == 7)
            return false;

        var hider = player.GetComponent<PlayerHider>();
        return hider == null || !hider.isHidden;
    }
}
