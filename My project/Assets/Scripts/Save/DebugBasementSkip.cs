#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections;
using FS_Atmo;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Playables;

/// <summary>
/// Dev shortcut: Ctrl+1 (top-row or numpad) teleports the player just outside the basement exit door and
/// applies the same leave-basement state as walking out (butcher may leave the basement again after grace,
/// cage occupancy cleared, leave-basement car shot / checkpoint). Editor and development builds only.
/// </summary>
public class DebugBasementSkip : MonoBehaviour
{
    const string ExitDoorName = "Door Basement Exit";
    const float OutsideClearance = 1.25f;

    static DebugBasementSkip _instance;
    bool _busy;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Install()
    {
        if (_instance != null)
            return;
        var go = new GameObject("Debug Basement Skip");
        DontDestroyOnLoad(go);
        _instance = go.AddComponent<DebugBasementSkip>();
    }

    void Update()
    {
        Keyboard kb = Keyboard.current;
        if (kb == null || !kb.ctrlKey.isPressed || _busy)
            return;
        if (!kb.digit1Key.wasPressedThisFrame && !kb.numpad1Key.wasPressedThisFrame)
            return;
        StartCoroutine(SkipOutOfBasement());
    }

    IEnumerator SkipOutOfBasement()
    {
        _busy = true;
        try
        {
            GameObject player = GameObject.FindWithTag("Player");
            if (player == null)
            {
                Debug.LogWarning("[DebugBasementSkip] No Player in the scene.");
                yield break;
            }

            if (PlayerControlGate.IsAcquired(PlayerControlGate.ReasonCaptured) || JumpscareManager.CaptureUiActive)
            {
                Debug.LogWarning("[DebugBasementSkip] Can't skip while captured.");
                yield break;
            }

            if (!TryGetOutsidePose(out Vector3 feet, out float yaw))
            {
                Debug.LogWarning($"[DebugBasementSkip] No \"{ExitDoorName}\" to stand outside of.");
                yield break;
            }

            SkipIntroCinematic();
            ReleaseBlockingLocks();

            var body = player.GetComponent<CharacterController>();
            if (body != null)
                body.enabled = false;
            player.transform.SetPositionAndRotation(feet, Quaternion.Euler(0f, yaw, 0f));
            if (body != null)
                body.enabled = true;
            Physics.SyncTransforms();

            FindFirstObjectByType<CinemachineMouseLook>()?.SetLook(Quaternion.Euler(0f, yaw, 0f));

            // Same as Continue teleport: drop stale cage hold, then treat this as already outside.
            Cage.RebindAllAfterContinueTeleport(player);
            InteractionController.ForcePlayerLeftBasement();
            OpenExitDoor();

            // One frame so CharacterController / mouse look settle, then fire the real leave sequence.
            yield return null;

            ButcherWindowShot shot = FindFirstObjectByType<ButcherWindowShot>(FindObjectsInactive.Include);
            if (shot != null)
            {
                if (shot.TryDebugTrigger())
                    Debug.Log("[DebugBasementSkip] Teleported outside the basement; leave-basement shot starting.");
                else
                    Debug.Log("[DebugBasementSkip] Teleported outside the basement (leave shot already played).");
            }
            else
            {
                Debug.LogWarning("[DebugBasementSkip] Teleported outside, but no ButcherWindowShot to trigger.");
            }
        }
        finally
        {
            _busy = false;
        }
    }

    static bool TryGetOutsidePose(out Vector3 feet, out float yaw)
    {
        feet = default;
        yaw = 0f;

        GameObject doorGo = GameObject.Find(ExitDoorName);
        if (doorGo == null)
            return false;

        Transform door = doorGo.transform;
        Vector3 exitPoint = door.position;
        Vector3 exitOut = Flat(door.forward).normalized;
        if (exitOut.sqrMagnitude < 0.01f)
            exitOut = Flat(door.right).normalized;

        // Same as ButcherWindowShot: outward faces away from the house/basement root.
        if (Vector3.Dot(Flat(BoundsOf(door.root).center - exitPoint), exitOut) > 0f)
            exitOut = -exitOut;

        Vector3 probe = exitPoint + exitOut * OutsideClearance + Vector3.up * 1.5f;
        if (Physics.Raycast(probe, Vector3.down, out RaycastHit hit, 6f, ~0, QueryTriggerInteraction.Ignore))
            feet = hit.point;
        else
            feet = exitPoint + exitOut * OutsideClearance;

        yaw = Quaternion.LookRotation(exitOut, Vector3.up).eulerAngles.y;
        return true;
    }

    static void OpenExitDoor()
    {
        GameObject doorGo = GameObject.Find(ExitDoorName);
        if (doorGo == null)
            return;

        var door = doorGo.GetComponentInChildren<SimpleOpenClose>()
                   ?? doorGo.GetComponentInParent<SimpleOpenClose>();
        if (door == null)
            return;

        if (door.isLocked)
            door.UnlockSilently();
        if (!door.objectOpen)
            door.SnapOpenState(true);

        DoorController ai = door.GetComponentInParent<DoorController>();
        if (ai != null)
            ai.SetLocked(false);
    }

    static void SkipIntroCinematic()
    {
        foreach (PlayableDirector director in FindObjectsByType<PlayableDirector>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (director.playableAsset != null
                && director.playableAsset.name.IndexOf("GameplayTimeline", System.StringComparison.OrdinalIgnoreCase) >= 0)
                director.Stop();
        }

        while (PlayerControlGate.IsAcquired(PlayerControlGate.ReasonCinematic))
            PlayerControlGate.Release(PlayerControlGate.ReasonCinematic);
        GameplayHudVisibility.Instance?.ShowGameplayHud();
        Cursor.visible = false;
        Cursor.lockState = CursorLockMode.Locked;
    }

    static void ReleaseBlockingLocks()
    {
        while (PlayerControlGate.IsAcquired(PlayerControlGate.ReasonReading))
            PlayerControlGate.Release(PlayerControlGate.ReasonReading);
        while (PlayerControlGate.IsAcquired(PlayerControlGate.ReasonLockpick))
            PlayerControlGate.Release(PlayerControlGate.ReasonLockpick);
        // Mid leave-basement shot: drop so we can re-fire cleanly from the door.
        while (PlayerControlGate.IsAcquired("butcher_window_shot"))
            PlayerControlGate.Release("butcher_window_shot");
    }

    static Vector3 Flat(Vector3 v)
    {
        v.y = 0f;
        return v;
    }

    static Bounds BoundsOf(Transform target)
    {
        Renderer[] renderers = target.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0)
            return new Bounds(target.position, Vector3.one);
        Bounds bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++)
            bounds.Encapsulate(renderers[i].bounds);
        return bounds;
    }
}
#endif
